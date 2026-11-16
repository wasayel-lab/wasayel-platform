using System.Text;
using ACommerce.Templates.Customer.Marketplace.Services;
using ACommerce.Templates.Customer.Marketplace.Services.Incubator;
using Marten;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ─── بُرهانٌ حَيٌّ: رَفضُ `create_tenant` عِندَ حَدِّ المَتاجِرِ بايتاً بايتاً — مِن المُنَفِّذِ الحَقيقيّ عَلى Postgres ──────────
//
// **ما يُثبِتُه**: نَصُّ الرَفضِ الَّذي يَبلُغُ المُستَخدِمَ (`AgentToolExecutor.ExecuteAsync` ← `CreateTenantAsync` ← `CheckBuildAsync`)
// يُساوي `AgentStoreLimitRefusalTests.ExpectedFor` بايتاً — قَبلَ تَرحيلِه إلى `ar.json` وبَعدَه، بِلا تَعديلِ حَرفٍ في هذا المِلَفّ
// (القاعِدَة ٣). ولا مُستَأجِرَ يُكتَب: مُستَخدِمُ استوديو عَشوائيٌّ يَخُصُّ هذا التَشغيلَ عِندَ حَدِّه، ويُحذَفُ بَعدَه.
//
// يَتَخَطّى نَفسَه ما لَم تُضبَط WASAYEL_LIVE_PROOF=1 وConnectionStrings__Postgres — عَقدُ إخوَتِه حَرفاً، وفي مَجموعَتِهِم التَسلسُلِيَّة.

[Collection(LiveProofCollection.Name)]
public class LiveAgentStoreLimitRefusalProofTests(ITestOutputHelper output)
{
    private static bool Enabled =>
        Environment.GetEnvironmentVariable("WASAYEL_LIVE_PROOF") == "1";

    private static string? ConnectionString =>
        Environment.GetEnvironmentVariable("ConnectionStrings__Postgres");

    [Fact]
    public async Task The_create_tenant_refusal_at_the_store_limit_reaches_the_user_as_these_bytes()
    {
        if (!Enabled || string.IsNullOrEmpty(ConnectionString)) return;

        using var store = DocumentStore.For(o =>
        {
            o.Connection(ConnectionString!);
            o.DatabaseSchemaName = "platform";
            o.Policies.AllDocumentsAreMultiTenanted();
            o.Schema.For<ACommerce.Kit.Tenants.Tenant>().SingleTenanted().Identity(x => x.Id);
            o.Schema.For<StudioUser>().Identity(x => x.Id);
            o.AutoCreateSchemaObjects = JasperFx.AutoCreate.All;
        });

        var uid = Guid.NewGuid();
        var limits = TierCatalog.For("spark");
        await using (var s = store.LightweightSession(StudioAuth.Tenant))
        {
            s.Store(new StudioUser { Id = uid, Tier = "spark", StoresBuilt = limits.StoresMax, FullName = "live-proof" });
            await s.SaveChangesAsync();
        }

        var slug = $"live-limit-{uid:N}"[..24];
        try
        {
            var exec = new AgentToolExecutor(store, new TenantRoleService(store));
            var payload = $$"""
            {"slug":"{{slug}}","name":"live-proof","color":"#1d4ed8","channel":"phone","categories":[{"slug":"general","label":"عامّ"}]}
            """;
            var (ok, message) = await exec.ExecuteAsync("create_tenant", payload, uid);
            output.WriteLine($"[create_tenant] ok={ok} limit={limits.StoresMax} bytes={Encoding.UTF8.GetByteCount(message)} :: {message}");

            Assert.False(ok);
            Assert.Equal(Encoding.UTF8.GetBytes(AgentStoreLimitRefusalTests.ExpectedFor(limits.StoresMax, limits.StoresMax)),
                         Encoding.UTF8.GetBytes(message));

            await using var q = store.QuerySession();
            Assert.Null(await q.LoadAsync<ACommerce.Kit.Tenants.Tenant>(slug));
        }
        finally
        {
            await using var s = store.LightweightSession(StudioAuth.Tenant);
            s.Delete<StudioUser>(uid);
            await s.SaveChangesAsync();
        }
    }
}
