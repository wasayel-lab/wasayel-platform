using System.Text;
using ACommerce.Kit.Theme;
using ACommerce.Templates.Customer.Marketplace.Services.Incubator;
using Marten;
using Xunit;

namespace ACommerce.Platform.Tests;

// ─── بُرهان «فَوراً» لِلمَظهَر — الجانِب الَّذي يَلزَمُه قاعِدَة بَيانات ─
//
// هذا **لَيسَ اختِبار وَحدَة**، ولا يَعمَل في التَشغيل العادِيّ: يَتَخَطّى
// نَفسَه ما لَم تُضبَط <c>WASAYEL_LIVE_PROOF=1</c> و
// <c>ConnectionStrings__Postgres</c>. نَفس عَقد
// <see cref="LiveRoleDefinitionProofTests"/> حَرفاً — حَقيبَة
// الاختِبارات تَبقى نَقِيَّة وبِلا شَبَكَة.
//
// وما يَفعَلُه مَحدود بِقَصد: يَقرَأ حالَة وَثائِق الثيم ويُسَمّي مُشرِفي
// المَنصَّة، فَيَكتُب مِلَفّ أَدِلَّة.
//
// ‏(‏2026-10-25، بَندُ المُنَسِّقِ في W8-2 — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه) «يُسَمّي» صارَ «يَعُدُّ ويُعَرِّفُ بِالمُعَرِّف»: كانَ يَكتُبُ جَوّالَ كُلِّ مُشرِفٍ
// واسمَه ومُقتَرِحَ كُلِّ وَثيقَةٍ إلى الخَرجِ بِصِفرِ Assert. الآن تُحجَبُ الحُقولُ، ويَقيسُ البُرهانُ حالاتِ الوَثائِقِ والثيمَ الفَعّالَ وغِيابَ كُلِّ
// قيمَةٍ حَيَّةٍ عَن خَرجِه (`LiveProofRedaction`، والماسِحُ `TestOutputPersonalDataTests`). ومَن يَحتاجُ جَوّالَ المُشرِفِ لِيُكمِلَ عَبرَ HTTP
// فَهُوَ ما ضَبَطَه في `PLATFORM_ADMIN_PHONE` بِنَفسِه. **ولا يَكتُب ولا يَعتَمِد** — لِأَنّ
// البُرهان الحَقيقيّ يَجِب أَن يَمُرّ مِن **الخادِم الحَيّ نَفسِه**
// (نُقطَتا /admin/tenants/{slug}/theme/*)، وإلّا لَكانَ إثباتاً أَنّ
// عَمَلِيَّةً أُخرى تَكتُب في نَفس قاعِدَة البَيانات — لا أَنّ الخادِم
// أَبطَلَ كاشَه وأَعادَ البَثّ بِلا إعادَة تَشغيل. الكِتابَة عَبر HTTP
// هي بِعَينِها ما يَجعَل «نَفس الـPID» دَعوىً قابِلَة لِلفَحص.

[Collection(LiveProofCollection.Name)]
public class LiveThemeProofTests
{
    private const string TenantSlug = "adwar-demo";

    private static bool Enabled =>
        Environment.GetEnvironmentVariable("WASAYEL_LIVE_PROOF") == "1";

    private static string? ConnectionString =>
        Environment.GetEnvironmentVariable("ConnectionStrings__Postgres");

    [Fact]
    public async Task ReadThemeDocumentsAndNamePlatformAdmins()
    {
        if (!Enabled || string.IsNullOrEmpty(ConnectionString)) return;

        var evidence = new StringBuilder();
        void Log(string line)
        {
            var stamped = $"{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ss.fffZ}  {line}";
            evidence.AppendLine(stamped);
            Console.WriteLine(stamped);
        }

        using var store = DocumentStore.For(o =>
        {
            o.Connection(ConnectionString!);
            o.DatabaseSchemaName = "platform";
            o.Policies.AllDocumentsAreMultiTenanted();
            o.Schema.For<ACommerce.Kit.Tenants.Tenant>().SingleTenanted().Identity(x => x.Id);
            o.Schema.For<TenantThemeDefinition>().Identity(x => x.Id);
            o.Schema.For<StudioUser>().Identity(x => x.Id);
            o.AutoCreateSchemaObjects = JasperFx.AutoCreate.All;
        });

        // ‏(‏2026-10-25، بَندُ المُنَسِّقِ في W8-2) كانَ يَكتُبُ `by={d.CreatedBy}` — وبابُ الثيمِ يَكتُبُ فيه «{FullName} · {Phone}» — و`phone=`/`name=`
        // لِكُلِّ مُشرِف، وبِصِفرِ Assert. الحُقولُ تُحجَبُ قَبلَ نِداءِ `Log` (`LiveProofRedaction`)، والبُرهانُ يَقيسُ ما قَرَأ.
        List<TenantThemeDefinition> docs;
        await using (var s = store.QuerySession(TenantSlug))
        {
            docs = (await s.Query<TenantThemeDefinition>().ToListAsync()).ToList();
            Log($"[themes/{TenantSlug}] عَدَد الوَثائِق = {docs.Count}");
            foreach (var d in docs.OrderBy(d => d.CreatedAt))
            {
                var proposer = LiveProofRedaction.Of(d.CreatedBy);
                Log($"[themes/{TenantSlug}] slug={d.Slug} status={d.Status} " +
                    $"by={proposer} decided={d.DecidedAt:o}");
            }
        }
        // كُلُّ وَثيقَةٍ بِحالَةٍ مِن مَعجَمِ الاعتِماد.
        Assert.All(docs, d => Assert.True(TenantThemeStatuses.Contains(d.Status), $"حالَةٌ خارِجَ المَعجَم: «{d.Status}»"));

        // نَفس القِراءَة الَّتي يَسلُكُها الخادِم — بِلا كاش.
        var set = await ACommerce.Templates.Customer.Marketplace.Services
            .TenantThemeService.ReadUncachedAsync(store, TenantSlug);
        Log($"[effective/{TenantSlug}] theme={set.Theme.Slug} " +
            $"primary={set.Theme["color.primary"]} radius.md={set.Theme["radius.md"]}");
        // والفَعّالُ ثيمُ المَنَصَّةِ أَو واحِدٌ مِمّا اعتُمِد — لا مُعَلَّقٌ ولا مَرفوض — ولَونُه الأَساسُ مَوجود.
        var approved = docs.Where(d => d.Status == TenantThemeStatuses.Approved).Select(d => d.Slug).ToHashSet(StringComparer.Ordinal);
        Assert.True(set.Theme.Slug == TenantThemeSet.Platform.Theme.Slug || approved.Contains(set.Theme.Slug),
            $"الثيمُ الفَعّالُ «{set.Theme.Slug}» لَيسَ ثيمَ المَنَصَّةِ ولا مُعتَمَداً ({approved.Count} مُعتَمَد).");
        Assert.False(string.IsNullOrWhiteSpace(set.Theme["color.primary"]));

        List<StudioUser> admins;
        await using (var s = store.QuerySession(StudioAuth.Tenant))
        {
            var users = await s.Query<StudioUser>().ToListAsync();
            admins = users.Where(u => u.IsPlatformAdmin).ToList();
            Log($"[platform-admin] العَدَد={admins.Count} مِن {users.Count} مُستَخدِم studio");
            foreach (var a in admins)
            {
                var phone = LiveProofRedaction.Of(a.Phone);
                var email = LiveProofRedaction.Of(a.Email);
                var name = LiveProofRedaction.Of(a.FullName);
                Log($"[platform-admin] id={a.Id:N} phone={phone} email={email} name={name}");
            }
        }

        // والخَرجُ نَفسُه يُقاس: لا جَوّالَ ولا بَريدَ ولا اسمَ مُشرِفٍ ولا مُقتَرِحَ وَثيقَة.
        var measured = LiveProofRedaction.AssertAbsent(evidence.ToString(),
            admins.SelectMany(a => new[] { a.Phone, a.Email, a.FullName }).Concat(docs.Select(d => d.CreatedBy)));
        Log($"[redaction] قيمَةٌ حَيَّةٌ مَقيسَةٌ غائِبَة عَن الخَرج: {measured}");

        var path = Environment.GetEnvironmentVariable("WASAYEL_PROOF_OUT");
        if (!string.IsNullOrEmpty(path))
            await File.WriteAllTextAsync(path, evidence.ToString());
    }
}
