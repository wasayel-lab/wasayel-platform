using ACommerce.Kit.Subscriptions;
using ACommerce.Templates.Customer.Marketplace.Services.Subscriptions;
using Marten;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ إسقاطُ روابِطِ الخُطَّةِ القَديمَةِ عَلى Postgres — الاستِعلامُ نَفسُه لا نُسخَتُه (‏2026-11-05، الموجَة ١٢ G4 · E-6) ═══
//
// `DocWorld` لا يُنَفِّذُ `Query<T>`، فَاستِعلامُ `PayPalBillingService.DropStaleLinksAsync` (الباقَةُ والرابِطُ المَحفوظ) يُقاسُ هُنا عَلى
// Postgres: خَمسُ باقاتٍ بِسلاجِ باقَةِ مَنَصَّةٍ فَريدٍ لِلجَولَة — فَلا يَلمَسُ الاستِعلامُ وَثيقَةً لَيسَت لَه — تُكتَبُ ثُمَّ يُسقَطُ ما صُنِعَ مِن غَيرِ P-25
// ويُقرَأُ بِجَلسَةٍ جَديدَة، ثُمَّ تُحذَف. والمَخزَنُ بِتَركيبِ `LiveTenantExportProofTests` لِهذا الجَدوَل (`AutoCreate.None`: لا يُغَيِّرُ مُخَطَّطاً).
// ولا يَعمَلُ في التَشغيلِ العادِيّ: يَتَخَطّى نَفسَه ما لَم تُضبَط `WASAYEL_LIVE_PROOF=1` و`ConnectionStrings__Postgres`.
[Collection(LiveProofCollection.Name)]
public class LivePayPalStaleLinkProofTests(ITestOutputHelper output)
{
    private static bool Enabled => Environment.GetEnvironmentVariable("WASAYEL_LIVE_PROOF") == "1";
    private static string? ConnectionString => Environment.GetEnvironmentVariable("ConnectionStrings__Postgres");

    [Fact]
    public async Task Rebinding_drops_on_postgres_only_the_links_made_from_another_paypal_plan()
    {
        if (!Enabled || string.IsNullOrEmpty(ConnectionString)) return;

        using var store = DocumentStore.For(o =>
        {
            o.Connection(ConnectionString!);
            o.DatabaseSchemaName = "platform";
            o.AutoCreateSchemaObjects = JasperFx.AutoCreate.None;
            o.Policies.AllDocumentsAreMultiTenanted();
            o.Schema.For<TenantPlan>().SingleTenanted().Identity(x => x.Id);
        });

        var round = Guid.NewGuid().ToString("N")[..8];
        var platformPlan = $"live-plan-{round}";
        TenantPlan Doc(string tag, string planId, string? link, string? madeFrom) => new()
        {
            Id = $"live-stale-{tag}-{round}", PlanId = planId, Status = PlatformPlanStatuses.Active,
            StartsAt = DisplayTimeZoneTests.FixedUtc, ExpiresAt = DisplayTimeZoneTests.FixedUtc.AddDays(30),
            PayPalApproveUrl = link, PayPalApprovePlanId = madeFrom, PayPalSubscriptionId = $"I-{tag}",
            RenewalCancelledAt = DisplayTimeZoneTests.FixedUtc,
        };
        var docs = new[]
        {
            Doc("a", platformPlan, "https://pp/BA-10", "P-10"),
            Doc("b", platformPlan, "https://pp/BA-25", "P-25"),
            Doc("c", $"live-other-{round}", "https://pp/BA-77", "P-77"),
            Doc("d", platformPlan, null, null),
            Doc("e", platformPlan, "https://pp/BA-OLD", null),
        };

        await using (var w = store.LightweightSession())
        {
            w.Store(docs);
            await w.SaveChangesAsync();
        }

        try
        {
            IReadOnlyList<string> dropped;
            await using (var s = store.LightweightSession())
            {
                dropped = await PayPalBillingService.DropStaleLinksAsync(s, platformPlan, "P-25");
                await s.SaveChangesAsync();
            }

            await using var r = store.QuerySession();
            var after = new Dictionary<string, TenantPlan>();
            foreach (var d in docs) after[d.Id] = (await r.LoadAsync<TenantPlan>(d.Id))!;
            output.WriteLine($"أُسقِطَ عَلى Postgres: [{string.Join(", ", dropped)}] · " +
                string.Join(" · ", docs.Select(d => $"{d.Id[11..12]}={after[d.Id].PayPalApproveUrl ?? "—"}/{after[d.Id].PayPalApprovePlanId ?? "—"}")));

            Assert.Equal(new[] { docs[0].Id, docs[4].Id }.OrderBy(x => x, StringComparer.Ordinal), dropped.OrderBy(x => x, StringComparer.Ordinal));
            Assert.Null(after[docs[0].Id].PayPalApproveUrl);
            Assert.Null(after[docs[0].Id].PayPalApprovePlanId);
            Assert.Equal("I-a", after[docs[0].Id].PayPalSubscriptionId);
            Assert.Null(after[docs[4].Id].PayPalApproveUrl);
            Assert.Equal("https://pp/BA-25", after[docs[1].Id].PayPalApproveUrl);
            Assert.Equal("P-25", after[docs[1].Id].PayPalApprovePlanId);
            Assert.Equal("https://pp/BA-77", after[docs[2].Id].PayPalApproveUrl);
            Assert.Null(after[docs[3].Id].PayPalApproveUrl);
        }
        finally
        {
            await using var d = store.LightweightSession();
            foreach (var doc in docs) d.Delete<TenantPlan>(doc.Id);
            await d.SaveChangesAsync();
        }
    }
}
