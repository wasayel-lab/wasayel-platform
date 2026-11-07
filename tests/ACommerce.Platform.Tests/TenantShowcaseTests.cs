using ACommerce.Kit.Listings;
using ACommerce.Kit.Subscriptions;
using ACommerce.Kit.Tenants;
using ACommerce.Templates.Customer.Marketplace.Services.Queries;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;
using static ACommerce.Platform.Tests.LiveStoreBirthOnceCharacterizationTests;

namespace ACommerce.Platform.Tests;

// ═══ واجِهَةُ المَنَصَّةِ لا كُلُّ مُستَأجِر — الصَفحَةُ الأُولى وإحصاءاتُها وخَريطَةُ المَوقِعِ بِمُسنَدٍ واحِد (‏2026-11-07، مُراجَعَةُ عَدَساتِ C1 · W13-1·C1D-3) ═══
//
// **المَقيسُ قَبل** (تَقريرُ العَدَسَة): مَتجَرانِ أُنشِئا مِن الاستِمارَةِ بِصِفرِ إعلانٍ وبِلا باقَةٍ جَعَلا «مَتاجِر مُطلَقَة 4» وسَطرَ «مَتجر doors-a · 0 إعلان» عَلى
// الصَفحَةِ الأُولى، وثَمانِيَةَ مُدخَلاتٍ في `/sitemap.xml`. **والافتِراضُ الآمِنُ** (قَرارُ العَرضِ لِلمالِك «ب»): إعلانٌ واحِدٌ عَلى الأَقَلّ، والمَتجَرُ غَيرُ
// مُعَلَّق، وباقَتُه لا في السَماحِ ولا مُعَلَّقَة — `TenantShowcase.Shows`.

public class TenantShowcaseTests
{
    private static Tenant T(bool suspended = false) => new() { Id = "s", IsSuspended = suspended };

    [Theory]
    [InlineData(1, TenantPlanState.None)]
    [InlineData(5, TenantPlanState.Active)]
    public void A_store_with_a_listing_and_a_standing_plan_is_shown(int listings, TenantPlanState plan)
        => Assert.True(TenantShowcase.Shows(T(), listings, plan));

    [Theory]
    [InlineData(0, TenantPlanState.None, false)]
    [InlineData(0, TenantPlanState.Active, false)]
    [InlineData(3, TenantPlanState.Grace, false)]
    [InlineData(3, TenantPlanState.Suspended, false)]
    [InlineData(3, TenantPlanState.None, true)]
    public void A_store_without_a_listing_in_grace_suspended_or_held_is_not_shown(int listings, TenantPlanState plan, bool adminSuspended)
        => Assert.False(TenantShowcase.Shows(T(adminSuspended), listings, plan));

    [Fact]
    public void The_landing_reads_the_showcase_not_the_whole_directory()
    {
        var page = WriteEndpointGuardTests.StripMarkupComments(File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot,
            "libs/templates/ACommerce.Templates.Customer.Marketplace/Components/Pages/Landing.razor")));
        Assert.Contains("await Showcase.ListAsync()", page, StringComparison.Ordinal);
        Assert.DoesNotContain("TenantDirectory", page, StringComparison.Ordinal);
        var seo = File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, "libs/kits/Tenants/ACommerce.Kit.Tenants.Server/SeoHandlers.cs"));
        Assert.Contains("showcase.ShowcaseAsync(tenants", seo, StringComparison.Ordinal);
    }
}

// ─── البُرهانُ عَلى Postgres ────────────────────────────────────────────────────────────────────────────────

[Collection(LiveProofCollection.Name)]
public class LiveTenantShowcaseProofTests(ITestOutputHelper output)
{
    private static bool Enabled => LiveDealConcurrencyProofTests.Enabled && !string.IsNullOrEmpty(LiveDealConcurrencyProofTests.ConnectionString);

    [Fact]
    public async Task An_empty_new_store_stays_off_the_landing_and_the_sitemap_and_a_store_with_a_listing_is_on_both_on_Postgres()
    {
        if (!Enabled) return;
        await using var host = HostWithPhone();
        // ‏`Listing` يَحمِلُ مَجرى أَحداث — قاعِدَةٌ فارِغَةٌ بِلا `mt_streams` تَرُدُّ حِفظَه (عَقدُ `UserDataExportTests` بِحَرفِه).
        await host.Store.Storage.ApplyAllConfiguredChangesToDatabaseAsync(JasperFx.AutoCreate.CreateOnly);
        var a = await UserAsync(host);
        var (empty, stocked) = (NewSlug("w13e-"), NewSlug("w13l-"));
        try
        {
            Assert.Equal((302, $"/studio/apps/{empty}?built=1"), await StudioCreateAsync(host, a, empty, "مَتجَرٌ فارِغ"));
            Assert.Equal((302, $"/studio/apps/{stocked}?built=1"), await StudioCreateAsync(host, a, stocked, "مَتجَرٌ بِإعلان"));
            await using (var w = host.Store.LightweightSession(stocked))
            {
                w.Store(new Listing { Id = Guid.NewGuid(), TenantSlug = stocked, Title = "إعلانُ الواجِهَة" });
                await w.SaveChangesAsync();
            }

            await using var scope = host.Services.CreateAsyncScope();
            var (shown, listings) = await scope.ServiceProvider.GetRequiredService<TenantShowcase>().ListAsync();
            // الخَريطَةُ كَما يَبنيها `SeoHandlers.Sitemap`: كُلُّ المُستَأجِرين ← ما تُجيزُه الواجِهَةُ المُسَجَّلَةُ (`ITenantShowcase`) ← المُدخَلات.
            await using var q = host.Store.QuerySession();
            var all = await Marten.QueryableExtensions.ToListAsync(q.Query<Tenant>());
            var registered = scope.ServiceProvider.GetRequiredService<ITenantShowcase>();
            var sitemap = SeoDocuments.BuildSitemapXml(SeoDocuments.TenantEntries(await registered.ShowcaseAsync(all), "http://localhost"));
            output.WriteLine($"[live] الواجِهَة {shown.Count} مَتجَراً · الفارِغُ فيها {shown.Any(t => t.Slug == empty)} · ذو الإعلانِ فيها {shown.Any(t => t.Slug == stocked)} ({listings.GetValueOrDefault(stocked)} إعلان)"
                           + $" · الخَريطَة: الفارِغ {sitemap.Contains("/" + empty + "<")} · ذو الإعلان {sitemap.Contains("/" + stocked + "<")}");
            Assert.DoesNotContain(shown, t => t.Slug == empty);
            Assert.Contains(shown, t => t.Slug == stocked);
            Assert.DoesNotContain($"/{empty}<", sitemap, StringComparison.Ordinal);
            Assert.Contains($"/{stocked}<", sitemap, StringComparison.Ordinal);
        }
        finally
        {
            await using (var w = host.Store.LightweightSession(stocked)) { w.DeleteWhere<Listing>(l => l.TenantSlug == stocked); await w.SaveChangesAsync(); }
            await CleanAsync(host, [a], empty, stocked);
        }
    }
}
