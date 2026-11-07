using ACommerce.Kit.Subscriptions;
using ACommerce.Kit.Tenants;

namespace ACommerce.Templates.Customer.Marketplace.Services.Queries;

/// <summary>
/// <para><b>واجِهَةُ المَنَصَّة — أَيُّ مَتجَرٍ تَعرِضُه الصَفحَةُ الأُولى وتُدرِجُه خَريطَةُ المَوقِع</b> (‏2026-11-07، مُراجَعَةُ عَدَساتِ C1 · W13-1·C1D-3 — قَرارُ
/// العَرضِ لِلمالِك «ب»، وهذا افتِراضُه الآمِنُ القابِلُ لِلنَقض).</para>
///
/// <para><b>العِلَّةُ المَقيسَة</b>: قائِمَةُ «مَتاجِر مُطلَقَة على المَنصَّة» وإحصاءاتُها و<c>/sitemap.xml</c> كانَت تَقرَأُ كُلَّ مُستَأجِر — وقَبلَ الإنشاءِ
/// اليَدَوِيِّ لا يولَدُ مَتجَرُ رائِدِ أَعمالٍ إلّا بِتَحليلٍ مُكتَمِل. فَصارَ كُلُّ هاتِفٍ يَعبُرُ الرَمزَ يَضَعُ حَتّى <c>StoresMax</c> اسماً وسَطرَ وَصفٍ يَختارُهُما
/// عَلى الصَفحَةِ الأُولى وفي الخَريطَة: مَتجَرانِ بِصِفرِ إعلانٍ أُنشِئا مِن الاستِمارَةِ جَعَلا «مَتاجِر مُطلَقَة 4» وأَضافا ثَمانِيَةَ مُدخَلاتٍ لِلزاحِف.
/// و«مُطلَقَة» لا تَصدُقُ في مَتجَرٍ وُلِدَ قَبلَ ثانِيَةٍ بِلا إعلان.</para>
///
/// <para><b>والمُسنَدُ واحِدٌ لِثَلاثَةِ مُستَهلِكين</b> (القائِمَة، والإحصاءات، والخَريطَة — القاعِدَة ١): إعلانٌ واحِدٌ عَلى الأَقَلّ غَيرُ مَحذوف، والمَتجَرُ
/// غَيرُ مُعَلَّقٍ إدارِيّاً، وباقَتُه لَيسَت في السَماحِ ولا مُعَلَّقَة (<see cref="TenantPlanPolicy.Derive"/>).</para>
/// </summary>
public sealed class TenantShowcase(TenantDirectory directory, PlatformStatsQueries stats, PlatformBillingQueries billing) : ITenantShowcase
{
    /// <summary>أَيُعرَضُ هذا المَتجَرُ واجِهَةً لِلمَنَصَّة؟ — المُسنَدُ النَقِيّ.</summary>
    public static bool Shows(Tenant tenant, int listings, TenantPlanState plan)
        => listings >= 1 && !tenant.IsSuspended && plan is not (TenantPlanState.Grace or TenantPlanState.Suspended);

    /// <summary>المَتاجِرُ المَعروضَة بِتَرتيبِ الإنشاء، ومَعَها عَدَدُ إعلاناتِ كُلٍّ مِنها — الصَفحَةُ الأُولى تَقرَأُ الاثنَين.</summary>
    public async Task<(IReadOnlyList<Tenant> Tenants, IReadOnlyDictionary<string, int> Listings)> ListAsync(CancellationToken ct = default)
    {
        var all = await directory.ListAsync(ct);
        var listings = await stats.ListingCountsAsync(all.Select(t => t.Slug), ct);
        return (await FilterAsync(all, listings, ct), listings);
    }

    /// <summary>ما تَسأَلُه خَريطَةُ المَوقِعِ في العُدَّة (<see cref="ITenantShowcase"/>).</summary>
    public async Task<IReadOnlyList<Tenant>> ShowcaseAsync(IReadOnlyList<Tenant> tenants, CancellationToken ct = default)
        => await FilterAsync(tenants, await stats.ListingCountsAsync(tenants.Select(t => t.Slug), ct), ct);

    private async Task<IReadOnlyList<Tenant>> FilterAsync(IReadOnlyList<Tenant> tenants, IReadOnlyDictionary<string, int> listings, CancellationToken ct)
    {
        var plans = await billing.AllPlansAsync(ct);
        var now = DateTime.UtcNow;
        return tenants.Where(t => Shows(t, listings.GetValueOrDefault(t.Slug, 0), TenantPlanPolicy.Derive(plans.GetValueOrDefault(t.Slug), now))).ToList();
    }
}
