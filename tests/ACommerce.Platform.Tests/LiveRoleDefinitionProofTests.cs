using System.Text;
using ACommerce.Kit.Roles;
using ACommerce.Templates.Customer.Marketplace.Services;
using ACommerce.Templates.Customer.Marketplace.Services.Incubator;
using Marten;
using Xunit;

namespace ACommerce.Platform.Tests;

// ─── بُرهان «فَوراً» — الجانِب الَّذي يَلزَمُه قاعِدَة بَيانات ──────────
//
// هذا **ليسَ اختِبار وَحدَة**، ولا يَعمَل في التَشغيل العادِيّ: يَتَخَطّى
// نَفسَه ما لَم تُضبَط <c>WASAYEL_LIVE_PROOF=1</c> و
// <c>ConnectionStrings__Postgres</c>. الغايَة أَن تَبقى حَقيبَة
// الاختِبارات نَقِيَّة وبِلا شَبَكَة، وأَن يَبقى <b>نَفس الكود</b> الَّذي
// يَسلُكُه الوَكيل الحَقيقيّ قابِلاً لِلاستِدعاء بِنِداء أَداة مُصطَنَع.
//
// ما يَفعَلُه حينَ يُشَغَّل:
//   ١. يَستَدعي AgentToolExecutor.ExecuteAsync("define_role", …) —
//      نَفس المُنَفِّذ ونَفس المُصادِق ونَفس مَسار التَخزين.
//   ٢. يُشَغِّل السالِب: تَعريف بِصَلاحِيَّة خارِج المَعجَم — ويُثبِت
//      أَنَّه رُفِضَ بِرَمزِه وأَنَّه **لَم تُكتَب لَه وَثيقَة**.
//   ٣. يَكتُب مِلَفّ أَدِلَّة (حالَة الوَثيقَة، وهاتِف مُشرِف المَنصَّة
//      لِيُكمَل الاعتِماد عَبر HTTP بِجَلسَتِه).
//
// وما **لا** يَفعَلُه عَمداً: لا يَعتَمِد ولا يَرفُض. الاعتِماد قَرار
// بَشَريّ يَمُرّ مِن نُقطَة الموافَقَة خَلف بَوّابَة مُشرِف المَنصَّة، وهو
// ما يَجعَل «قَبل/بَعد» عَلى نَفس الخادِم بُرهاناً لا تَرتيباً.

public class LiveRoleDefinitionProofTests
{
    private const string TenantSlug = "adwar-demo";
    private const string RoleSlug   = "khayyat";
    private const string BadSlug    = "sabbagh";

    private static bool Enabled =>
        Environment.GetEnvironmentVariable("WASAYEL_LIVE_PROOF") == "1";

    private static string? ConnectionString =>
        Environment.GetEnvironmentVariable("ConnectionStrings__Postgres");

    [Fact]
    public async Task DefineRole_ThroughTheRealExecutor_LandsPendingAndTheNegativeNeverLands()
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
            o.Schema.For<TenantRoleDefinition>().Identity(x => x.Id);
            o.Schema.For<StudioUser>().Identity(x => x.Id);
            o.AutoCreateSchemaObjects = JasperFx.AutoCreate.All;
        });

        var roles = new TenantRoleService(store);
        var exec  = new AgentToolExecutor(store, roles);

        // ─── ١. المُوجَب: نِداء أَداة مُصطَنَع بِنَفس شَكل ما يُنتِجُه النَموذَج
        var payload = $$"""
        {"slug":"{{TenantSlug}}","definition":{{TenantRoleDefinitionToolTests.KhayyatJson}}}
        """;

        var (ok, msg) = await exec.ExecuteAsync("define_role", payload);
        Log($"[define_role/positive] ok={ok} :: {msg}");
        Assert.True(ok, msg);

        await using (var s = store.QuerySession(TenantSlug))
        {
            var doc = await s.LoadAsync<TenantRoleDefinition>(RoleSlug);
            Assert.NotNull(doc);
            Assert.Equal(TenantRoleStatuses.Pending, doc!.Status);
            Log($"[doc] slug={doc.Slug} status={doc.Status} by={doc.CreatedBy} at={doc.CreatedAt:O}");
        }

        // اللَقطَة المَقروءَة الآن لا تَحوي الدَور — لِأَنّ المُعَلَّق لا يُقرَأ.
        var beforeSet = await TenantRoleService.ReadUncachedAsync(store, TenantSlug);
        Assert.Empty(beforeSet.TenantAuthored);
        Log($"[set/before-approval] tenantAuthored={beforeSet.TenantAuthored.Count} " +
            $"definitions={beforeSet.Definitions.Count}");

        // ─── ٢. السالِب الحَيّ: صَلاحِيَّة خارِج المَعجَم ────────────────
        var badJson = TenantRoleDefinitionToolTests.KhayyatJson
            .Replace("\"slug\": \"khayyat\"", $"\"slug\": \"{BadSlug}\"")
            .Replace("\"listing.create\"", "\"listing.publish_everywhere\"");
        var badPayload = $$"""{"slug":"{{TenantSlug}}","definition":{{badJson}}}""";

        var (badOk, badMsg) = await exec.ExecuteAsync("define_role", badPayload);
        Log($"[define_role/negative] ok={badOk} :: {badMsg}");

        Assert.False(badOk);
        Assert.Contains("permission_out_of_vocabulary", badMsg);

        await using (var s = store.QuerySession(TenantSlug))
        {
            var bad = await s.LoadAsync<TenantRoleDefinition>(BadSlug);
            Assert.Null(bad);   // لَم تُكتَب وَثيقَة أَصلاً — لا مُعَلَّقَة ولا غَيرها
            Log($"[doc/negative] «{BadSlug}» غَير مَوجودَة — الرَّفض قَبل التَخزين.");
        }

        // ─── ٣. هاتِف مُشرِف المَنصَّة لِإكمال الاعتِماد عَبر HTTP ───────
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
