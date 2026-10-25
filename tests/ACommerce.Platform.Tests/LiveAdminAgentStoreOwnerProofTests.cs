using ACommerce.Templates.Customer.Marketplace.Services;
using Marten;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ─── بُرهانٌ حَيٌّ: `create_tenant` مِن وَكيلِ الإدارَةِ يَلِدُ المَتجَرَ مِلكَ مُشرِفِه — مِن المُنَفِّذِ الحَقيقيّ عَلى Postgres ──────────
//
// **ما يُثبِتُه** (‏2026-10-25، تَدقيقُ الإطلاق W8-3 — `lens-security-14`): `AgentToolExecutor.ExecuteAsPlatformAdminAsync` يَكتُبُ المُشرِفَ
// `OwnerUserId` لِلمَتجَرِ الَّذي يَلِدُه (فَلا يَتيمَ يَتَبَنّاه `StudioOwnershipSeeder`)، والمُنادي الَّذي لا يُسَمّي أَحَداً (`ExecuteAsync` بِلا مالِك —
// لا مُناديَ لَه في المَصادِرِ بَعدَ اليَوم، والبَراهينُ تُناديه) يَلِدُ `Guid.Empty` كَما كان. ولا يُنادى البَذّارُ هُنا: يَكتُبُ في كُلِّ يَتيمٍ في القاعِدَة.
// ولا يَبقى مُستَأجِرٌ: المَتجَرانِ يُحذَفانِ بَعدَه.
//
// يَتَخَطّى نَفسَه ما لَم تُضبَط WASAYEL_LIVE_PROOF=1 وConnectionStrings__Postgres — عَقدُ إخوَتِه حَرفاً، وفي مَجموعَتِهِم التَسلسُلِيَّة.
[Collection(LiveProofCollection.Name)]
public class LiveAdminAgentStoreOwnerProofTests(ITestOutputHelper output)
{
    private static bool Enabled =>
        Environment.GetEnvironmentVariable("WASAYEL_LIVE_PROOF") == "1";

    private static string? ConnectionString =>
        Environment.GetEnvironmentVariable("ConnectionStrings__Postgres");

    [Fact]
    public async Task The_admin_agent_create_tenant_is_born_owned_by_the_admin_and_the_unnamed_call_still_orphans()
    {
        if (!Enabled || string.IsNullOrEmpty(ConnectionString)) return;

        using var store = DocumentStore.For(o =>
        {
            o.Connection(ConnectionString!);
            o.DatabaseSchemaName = "platform";
            o.Policies.AllDocumentsAreMultiTenanted();
            o.Schema.For<ACommerce.Kit.Tenants.Tenant>().SingleTenanted().Identity(x => x.Id);
            o.AutoCreateSchemaObjects = JasperFx.AutoCreate.All;
        });

        var admin = Guid.NewGuid();
        var named = $"live-own-{admin:N}"[..24];
        var unnamed = $"live-orph-{admin:N}"[..24];
        string Payload(string slug) => $$"""
            {"slug":"{{slug}}","name":"live-proof","color":"#1d4ed8","channel":"phone","categories":[{"slug":"general","label":"عامّ"}]}
            """;
        try
        {
            var exec = new AgentToolExecutor(store, new TenantRoleService(store));
            var (okNamed, msgNamed) = await exec.ExecuteAsPlatformAdminAsync("create_tenant", Payload(named), admin);
            var (okUnnamed, msgUnnamed) = await exec.ExecuteAsync("create_tenant", Payload(unnamed));

            await using var q = store.QuerySession();
            var bornNamed = await q.LoadAsync<ACommerce.Kit.Tenants.Tenant>(named);
            var bornUnnamed = await q.LoadAsync<ACommerce.Kit.Tenants.Tenant>(unnamed);
            output.WriteLine($"[admin] ok={okNamed} owner=admin:{bornNamed?.OwnerUserId == admin} :: {msgNamed}");
            output.WriteLine($"[unnamed] ok={okUnnamed} owner=empty:{bornUnnamed?.OwnerUserId == Guid.Empty} :: {msgUnnamed}");

            Assert.True(okNamed, msgNamed);
            Assert.NotNull(bornNamed);
            Assert.Equal(admin, bornNamed!.OwnerUserId);
            Assert.True(okUnnamed, msgUnnamed);
            Assert.Equal(Guid.Empty, bornUnnamed!.OwnerUserId);
        }
        finally
        {
            await using var s = store.LightweightSession();
            s.Delete<ACommerce.Kit.Tenants.Tenant>(named);
            s.Delete<ACommerce.Kit.Tenants.Tenant>(unnamed);
            await s.SaveChangesAsync();
        }
    }
}
