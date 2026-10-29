using ACommerce.Kit.Auth;
using ACommerce.Templates.Customer.Marketplace.Services.Api;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ CO10 (تَدقيقُ الموجَة ١١، ‏2026-10-29): مِفتاحٌ يولَدُ مَيِّتاً — فاعِلُه مَحذوفٌ أَو لا حِسابَ لَه ═══
//
// د56 (‏2026-10-28) جَعَلَ المِفتاحَ يُرَدُّ عِندَ الاستِعمالِ بِـ`ApiKeyRejection.ActorGone` إن رَحَلَ فاعِلُه، وأَخفى «حِسابي» المَحذوفَ مِن قائِمَةِ الفاعِلين
// في `StudioAppKeys.razor`. لَكِنَّ بابَ الإصدار `POST /studio/apps/{slug}/keys/create` يَقرَأُ `actor` مِن الاستِمارَةِ ولا يَسأَلُ الحالَ نَفسَها — فَطَلَبٌ
// بِمُعَرِّفِ حِسابٍ مَحذوفٍ (أَو بِلا حِساب) يُصدِرُ مِفتاحاً «فَعّالاً» في الشاشَةِ يُرَدُّ عِندَ أَوَّلِ استِعمال.
public class ApiKeyIssueActorTests(ITestOutputHelper output)
{
    private const string CreateRoute = "/studio/apps/{slug}/keys/create";

    private static string CreateBody()
    {
        var doors = WriteEndpointGuardTests.AllMinimalApiEndpoints().Where(e => e.Route == CreateRoute).ToList();
        Assert.True(doors.Count == 1, $"«{CreateRoute}»: {doors.Count} باباً — الأَداةُ عَمياء.");
        return doors[0].Body;
    }

    /// <summary><b>تَوصيفُ اليَوم (‏2026-10-29، CO10)</b>: بَينَ التَحَقُّقِ والإصدارِ لا سُؤالَ عَن الفاعِل، وصَفحَةُ المَفاتيحِ لا رَمزَ لَها يُسَمّيه.</summary>
    [Fact]
    public void Today_the_issue_door_asks_nothing_about_the_actor_between_validation_and_issue()
    {
        var body = CreateBody();
        var from = body.IndexOf("ApiKeyValidator.Validate(request)", StringComparison.Ordinal);
        var to = body.IndexOf("keys.IssueAsync(", StringComparison.Ordinal);
        Assert.True(from > 0 && to > from, $"التَحَقُّق@{from} الإصدار@{to}");
        var between = body[from..to];
        var page = StudioConsentCharacterizationTests.Source("libs", "templates", "ACommerce.Templates.Customer.Marketplace", "Components", "Pages", "StudioAppKeys.razor");
        var asks = between.Contains("ActorGone", StringComparison.Ordinal);
        var named = page.Contains("actor_gone", StringComparison.Ordinal);
        output.WriteLine($"اليَوم: البابُ يَسأَلُ عَن الفاعِلِ قَبلَ الإصدار: {asks} · الصَفحَةُ تُسَمّي رَحيلَه: {named}");
        Assert.False(asks);
        Assert.False(named);
    }
}

/// <summary><b>CO10 حَيّاً عَلى Postgres</b> (‏<c>WASAYEL_LIVE_PROOF=1</c>): الإصدارُ بِمَخزَنِ <see cref="LiveApiKeyActorDeletionProofTests.BuildStore"/>.</summary>
[Collection(LiveProofCollection.Name)]
public class LiveApiKeyIssueActorProofTests(ITestOutputHelper output)
{
    private static bool Enabled
        => LiveDealConcurrencyProofTests.Enabled && !string.IsNullOrEmpty(LiveDealConcurrencyProofTests.ConnectionString);

    private static ApiKeyValidator.IssueRequest For(Guid actor)
        => new("رَبطُ الناقِل", actor, "", new[] { ApiScopeCatalog.DealsRead }, null);

    /// <summary><b>تَوصيفُ اليَوم (‏2026-10-29، CO10)</b>: الخِدمَةُ تُصدِرُ لِفاعِلٍ مَحذوفٍ ولِفاعِلٍ بِلا حِساب مِفتاحاً «فَعّالاً»، ويُرَدُّ كِلاهُما عِندَ
    /// الاستِعمالِ <c>ActorGone</c> — مِفتاحٌ مَيِّتٌ يَبدو حَيّاً.</summary>
    [Fact]
    public async Task Today_a_key_issued_to_a_deleted_or_missing_actor_is_born_active_and_refused_on_use_on_Postgres()
    {
        if (!Enabled) return;
        await using var store = LiveApiKeyActorDeletionProofTests.BuildStore();
        var slug = "ak-co10-" + Guid.NewGuid().ToString("N")[..8];
        var issued = new List<string>();
        try
        {
            var gone = new User { Id = Guid.NewGuid(), TenantSlug = slug, Phone = "966500000571" };
            await using (var s = store.LightweightSession(slug))
            {
                AccountDeletion.Erase(gone, DateTime.UtcNow);
                s.Store(gone);
                await s.SaveChangesAsync();
            }
            var keys = new ApiKeyService(store);
            var deleted = await keys.IssueAsync(slug, For(gone.Id), Guid.NewGuid());
            var missing = await keys.IssueAsync(slug, For(Guid.NewGuid()), Guid.NewGuid());
            issued.AddRange(new[] { deleted.Key.Id, missing.Key.Id });
            var useDeleted = (await keys.AuthenticateAsync(deleted.Presented)).Rejection;
            var useMissing = (await keys.AuthenticateAsync(missing.Presented)).Rejection;
            output.WriteLine($"اليَوم: مِفتاحُ فاعِلٍ مَحذوف {deleted.Key.Status} ثُمَّ {useDeleted} · مِفتاحُ فاعِلٍ بِلا حِساب {missing.Key.Status} ثُمَّ {useMissing}");
            Assert.Equal((ApiKeyDocument.StatusActive, ApiKeyRejection.ActorGone), (deleted.Key.Status, useDeleted));
            Assert.Equal((ApiKeyDocument.StatusActive, ApiKeyRejection.ActorGone), (missing.Key.Status, useMissing));
        }
        finally
        {
            await using (var g = store.LightweightSession())
            {
                foreach (var id in issued) g.Delete<ApiKeyDocument>(id);
                await g.SaveChangesAsync();
            }
            await LiveZeroRoleAdminProofTests.CleanAsync(store, slug);
        }
    }
}
