using ACommerce.Kit.Auth;
using ACommerce.Kit.Auth.Server;
using ACommerce.Kit.Tenants;
using ACommerce.Templates.Customer.Marketplace.Services.Incubator;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ بابُ `/build` المُسَجَّلُ عَلى Postgres يَكتُبُ المَتجَرَ الَّذي ثَبَّتَه التَوصيف (‏2026-11-07، الموجَة ١٣ W13-1·٢ — ADR-049) ═══
//
// **لِماذا مِلَفٌّ مُنفَصِل**: هذا الصَفُّ يَصِلُ بَصمَةَ `StudioStoreBirthCharacterizationTests.GoldenSha256` بِالبابِ الحَيِّ نَفسِه — جِسمُه
// المُسَجَّلُ يُشَغَّلُ بِحَرَسِه، لا قِراءَتي لِمُعامِلاتِه. والبابُ يَخرُجُ مِن الرِحلَةِ في W13-1·٥، فَيَخرُجُ مَعَه هذا المِلَفُّ ويَقولُ كوميتُ
// الإزالَةِ ذلك، ويَحِلُّ مَحَلَّه بُرهانا الإنشاءِ اليَدَوِيِّ بِالبَصمَةِ نَفسِها. أَمّا التَوصيفُ فَيَبقى بِحَرفِه.
//
// **والجَلسَةُ كَما يَترُكُها مَسارُ العَميلِ عَدا نِداءَ النَموذَج**: `StartAsync` ← `SaveAnswerAsync("description")` (الوَصفُ وَحدَه، فَـ`Answers`
// بِلا قِطاعٍ و`SuggestedPattern` فارِغ) ثُمَّ `Completed` يُكتَبُ بِاليَدِ مَكانَ `RunAnalysisAsync` — لا مِفتاحَ في هذا البُرهانِ ولا يَحتاجُه.
// وقَناةُ الهاتِفِ مُسَجَّلَةٌ بِبَديلٍ لا يُرسِل، فَيَشتَقُّ البابُ `phone` كَما يَشتَقُّه عَلى نُسخَةٍ بِـSMS.

[Collection(LiveProofCollection.Name)]
public class LiveStudioBuildBirthProofTests(ITestOutputHelper output)
{
    private static bool Enabled => LiveDealConcurrencyProofTests.Enabled && !string.IsNullOrEmpty(LiveDealConcurrencyProofTests.ConnectionString);

    [Fact]
    public async Task The_registered_build_door_writes_the_pinned_store_on_Postgres()
    {
        if (!Enabled) return;

        await using var host = LiveTemplateDoorHost.Build(services: s => s.AddSingleton<IOtpChannel>(new OtpSendQuotaTests.RecordingSms()));
        var store = host.Store;
        var user = new StudioUser { Id = Guid.NewGuid(), Phone = "zz-w13b-" + Guid.NewGuid().ToString("N")[..8], FullName = "صاحِبُ الفِكرَة" };
        var slug = ("w13b-" + Guid.NewGuid().ToString("N"))[..18];
        Guid sessionId = Guid.Empty;
        try
        {
            await using (var s = store.LightweightSession(StudioAuth.Tenant)) { s.Store(user); await s.SaveChangesAsync(); }
            await using (var scope = host.Services.CreateAsyncScope())
            {
                var incubator = scope.ServiceProvider.GetRequiredService<FeasibilityAnalysisService>();
                var started = await incubator.StartAsync(user.Id, user.FullName);
                sessionId = started.Id;
                await incubator.SaveAnswerAsync(sessionId, "description", "سوقٌ يَجمَعُ بائِعي الحَيِّ في مَتجَرٍ واحِد");
            }
            await using (var s = store.LightweightSession(FeasibilityAnalysisService.IncubatorTenant))
            {
                var session = await s.LoadAsync<IncubatorSession>(sessionId);
                Assert.NotNull(session);
                Assert.Empty(session!.Answers.Keys.Where(k => k != "description"));
                Assert.Equal("", session.SuggestedPattern);
                session.Status = IncubatorStatus.Completed;
                s.Store(session);
                await s.SaveChangesAsync();
            }

            var token = AuthHandlers.MakeToken(user.Id, StudioAuth.Tenant, user.SessionVersion);
            var (status, location) = await host.PostStudioFormAsync("/studio/s/{id:guid}/build",
                new Dictionary<string, string> { ["id"] = sessionId.ToString() }, token,
                ("slug", slug), ("name", StudioStoreBirthCharacterizationTests.RefName), ("color", StudioStoreBirthCharacterizationTests.RefColor),
                ("tagline", StudioStoreBirthCharacterizationTests.RefTagLine), ("city", StudioStoreBirthCharacterizationTests.RefCity));
            output.WriteLine($"[live] /build ⇒ {status} {location}");
            Assert.Equal(302, status);
            Assert.Equal($"/studio/apps/{slug}?built=1", location);

            await using var read = store.QuerySession();
            var tenant = await read.LoadAsync<Tenant>(slug);
            Assert.NotNull(tenant);
            var canonical = StudioStoreBirthCharacterizationTests.Canonical(tenant!);
            output.WriteLine($"[live] SHA-256 = {StudioStoreBirthCharacterizationTests.Sha256(canonical)} · مالِك={tenant!.OwnerUserId == user.Id} · مَصدَر={tenant.SourceAnalysisId == sessionId}");
            Assert.Equal(StudioStoreBirthCharacterizationTests.GoldenSha256, StudioStoreBirthCharacterizationTests.Sha256(canonical));
            Assert.Equal(user.Id, tenant.OwnerUserId);
            Assert.Equal(sessionId, tenant.SourceAnalysisId);
        }
        finally
        {
            await using (var s = store.LightweightSession()) { s.Delete<Tenant>(slug); await s.SaveChangesAsync(); }
            await using (var s = store.LightweightSession(FeasibilityAnalysisService.IncubatorTenant))
            {
                if (sessionId != Guid.Empty) s.Delete<IncubatorSession>(sessionId);
                await s.SaveChangesAsync();
            }
            await using (var s = store.LightweightSession(StudioAuth.Tenant)) { s.Delete<StudioUser>(user.Id); await s.SaveChangesAsync(); }
        }
    }
}
