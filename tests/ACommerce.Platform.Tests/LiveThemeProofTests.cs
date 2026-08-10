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
// المَنصَّة، فَيَكتُب مِلَفّ أَدِلَّة. **ولا يَكتُب ولا يَعتَمِد** — لِأَنّ
// البُرهان الحَقيقيّ يَجِب أَن يَمُرّ مِن **الخادِم الحَيّ نَفسِه**
// (نُقطَتا /admin/tenants/{slug}/theme/*)، وإلّا لَكانَ إثباتاً أَنّ
// عَمَلِيَّةً أُخرى تَكتُب في نَفس قاعِدَة البَيانات — لا أَنّ الخادِم
// أَبطَلَ كاشَه وأَعادَ البَثّ بِلا إعادَة تَشغيل. الكِتابَة عَبر HTTP
// هي بِعَينِها ما يَجعَل «نَفس الـPID» دَعوىً قابِلَة لِلفَحص.

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

        await using (var s = store.QuerySession(TenantSlug))
        {
            var docs = await s.Query<TenantThemeDefinition>().ToListAsync();
            Log($"[themes/{TenantSlug}] عَدَد الوَثائِق = {docs.Count}");
            foreach (var d in docs.OrderBy(d => d.CreatedAt))
                Log($"[themes/{TenantSlug}] slug={d.Slug} status={d.Status} " +
                    $"by={d.CreatedBy} decided={d.DecidedAt:o}");
        }

        // نَفس القِراءَة الَّتي يَسلُكُها الخادِم — بِلا كاش.
        var set = await ACommerce.Templates.Customer.Marketplace.Services
            .TenantThemeService.ReadUncachedAsync(store, TenantSlug);
        Log($"[effective/{TenantSlug}] theme={set.Theme.Slug} " +
            $"primary={set.Theme["color.primary"]} radius.md={set.Theme["radius.md"]}");

        await using (var s = store.QuerySession(StudioAuth.Tenant))
        {
            var users = await s.Query<StudioUser>().ToListAsync();
            foreach (var a in users.Where(u => u.IsPlatformAdmin))
                Log($"[platform-admin] phone={a.Phone} name={a.FullName}");
            if (!users.Any(u => u.IsPlatformAdmin))
                Log($"[platform-admin] (لا مُشرِف مَنصَّة — {users.Count} مُستَخدِم studio)");
        }

        var path = Environment.GetEnvironmentVariable("WASAYEL_PROOF_OUT");
        if (!string.IsNullOrEmpty(path))
            await File.WriteAllTextAsync(path, evidence.ToString());
    }
}
