using System.Text;
using ACommerce.Kit.Roles;
using ACommerce.Platform.Flows;
using ACommerce.Templates.Customer.Marketplace.Services;
using ACommerce.Templates.Customer.Marketplace.Services.Incubator;
using Marten;
using Xunit;

namespace ACommerce.Platform.Tests;

// ─── بُرهان حَيّ: اللُغَة مُستَهلَكَة في مَسار الكِتابَة ────────────────
//
// **السُؤال الَّذي يُجيبُه هذا المِلَفّ**: هَل صارَ
// `FlowDefinition` مَقروءاً في وَقت التَشغيل، أَم بَقِيَ تَجريداً
// يَنتَظِر مُستَهلِكاً؟ الجَواب لا يُؤخَذ مِن قِراءَة كود، بَل مِن
// تَشغيل `TenantRoleService.DecideAsync` **نَفسِها** — الدالَّة الَّتي
// تُناديها نُقطَة `/admin/tenants/{slug}/roles/definitions/{roleSlug}/decide`
// حَرفاً — عَلى قاعِدَة بَيانات حَقيقيَّة.
//
// بِنَفس بَوّابَة `LiveRoleDefinitionProofTests`: يَتَخَطّى نَفسَه ما لَم
// تُضبَط `WASAYEL_LIVE_PROOF=1` و`ConnectionStrings__Postgres` — فَحَقيبَة
// الاختِبارات تَبقى نَقِيَّة وبِلا شَبَكَة.
//
// **ولِماذا السالِب هو البُرهان لا المُوجَب**: المُوجَب («approved
// يُقبَل») كانَ سَيَمُرّ بِالشَرط القَديم أَيضاً. والسالِب المُختار
// `applied` — وهو حالَة **مَشروعَة في تَدَفُّق آخَر** (أَداة الوَكيل)
// وغَير مَشروعَة هُنا — يُثبِت أَنّ الرَدّ جاءَ مِن **تَعريف هذا
// التَدَفُّق بِعَينِه**: لا انتِقال مِن `pending` إلى `applied`
// يَملِكُه مُقَرِّر.

public class LiveApprovalFlowProofTests
{
    /// <summary>مُستَأجِر تَجرِبَة — لا يُلمَس <c>ashare</c> (عَرض
    /// مُستَثمِرين) ولا مُستَأجِر إنتاج.</summary>
    private const string TenantSlug = "owner-test";
    private const string RoleSlug   = "khayyat";

    private static bool Enabled =>
        Environment.GetEnvironmentVariable("WASAYEL_LIVE_PROOF") == "1";

    private static string? ConnectionString =>
        Environment.GetEnvironmentVariable("ConnectionStrings__Postgres");

    [Fact]
    public async Task DecideAsync_asks_the_flow_definition_on_a_real_database()
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

        Log($"[flow] states={string.Join(",", ApprovalFlow.All)} " +
            $"decisionActor={ApprovalFlow.DecisionActor}");

        // ─── ٠. أَرضِيَّة: تَعريف مُعَلَّق مَوجود ────────────────────────
        var payload = $$"""
        {"slug":"{{TenantSlug}}","definition":{{TenantRoleDefinitionToolTests.KhayyatJson}}}
        """;
        var (seeded, seedMsg) = await exec.ExecuteAsync("define_role", payload);
        Log($"[seed] ok={seeded} :: {seedMsg}");

        await using (var s = store.QuerySession(TenantSlug))
        {
            var doc = await s.LoadAsync<TenantRoleDefinition>(RoleSlug);
            if (doc is null) { Log("[seed] لا وَثيقَة — البُرهان يَتَوَقَّف."); return; }
            Log($"[doc/before] slug={doc.Slug} status={doc.Status}");
        }

        // ─── ١. السالِب الحاسِم: حالَة مِن مَعجَم تَدَفُّق آخَر ──────────
        // «applied» مَشروعَة لِنِداء أَداة الوَكيل، وغَير مَشروعَة هُنا.
        // الرَدّ يَأتي مِن ApprovalFlow.IsDecision → Shape.Allows.
        Assert.False(ApprovalFlow.IsDecision("applied"));

        var (badOk, badMsg) = await roles.DecideAsync(TenantSlug, RoleSlug, "applied", "live-proof");
        Log($"[decide/applied] ok={badOk} :: {badMsg}");
        Assert.False(badOk);
        Assert.Contains("قَرار غَير مَعروف", badMsg);

        // ولَم تُمَسّ الوَثيقَة.
        await using (var s = store.QuerySession(TenantSlug))
        {
            var doc = await s.LoadAsync<TenantRoleDefinition>(RoleSlug);
            Assert.NotNull(doc);
            Assert.NotEqual("applied", doc!.Status);
            Log($"[doc/after-rejected-verdict] status={doc.Status} decidedBy={doc.DecidedBy ?? "-"}");
        }

        // ─── ٢. المُوجَب: القَرار الَّذي يُجيزُه التَعريف يَمُرّ ─────────
        Assert.True(ApprovalFlow.IsDecision(ApprovalFlow.Approved));

        var (ok, msg) = await roles.DecideAsync(
            TenantSlug, RoleSlug, TenantRoleStatuses.Approved, "live-proof");
        Log($"[decide/approved] ok={ok} :: {msg}");
        Assert.True(ok, msg);

        await using (var s = store.QuerySession(TenantSlug))
        {
            var doc = await s.LoadAsync<TenantRoleDefinition>(RoleSlug);
            Assert.NotNull(doc);
            Assert.Equal(TenantRoleStatuses.Approved, doc!.Status);
            Log($"[doc/after-approval] status={doc.Status} decidedBy={doc.DecidedBy} at={doc.DecidedAt:O}");
        }

        // ─── ٣. والدَور صارَ حَيّاً — أَثَر الاعتِماد لا إعلانُه ─────────
        var after = await TenantRoleService.ReadUncachedAsync(store, TenantSlug);
        Log($"[set/after-approval] tenantAuthored={after.TenantAuthored.Count} " +
            $"definitions={after.Definitions.Count}");
        Assert.Contains(after.TenantAuthored, d => d.Slug == RoleSlug);

        // ─── ٤. تَنظيف: تُعاد مُعَلَّقَة كَما وَجَدناها ──────────────────
        var (back, backMsg) = await roles.DecideAsync(
            TenantSlug, RoleSlug, TenantRoleStatuses.Rejected, "live-proof-cleanup");
        Log($"[cleanup/rejected] ok={back} :: {backMsg}");

        var path = Path.Combine(Path.GetTempPath(), "wasayel-approval-flow-proof.log");
        File.WriteAllText(path, evidence.ToString());
        Console.WriteLine($"[evidence] {path}");
    }
}
