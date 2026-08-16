using ACommerce.Kit.Listings;
using Marten;

namespace ACommerce.Templates.Customer.Marketplace.Services.Queries;

/// <summary>
/// <para><b>عَدّادُ إعلانات كُلّ مُستَأجِر — لِصَفحَة الهُبوط.</b>
/// و<c>Listing</c> مُقتَرِنَةُ الإيجار، فَلا استِعلامَ واحِداً يَعُدُّها
/// لِكُلّ المَتاجِر: <b>جَلسَةٌ لِكُلّ سلاج</b>، وهذا هُوَ ثَمَنُ
/// العَزل لا نَقصٌ في الكود. الصَفحَةُ كانَت تَفعَل الشَيءَ نَفسَه
/// بِيَدِها — والنُقلَةُ لا تُغَيِّر عَدَدَ الجَلَسات.</para>
///
/// <para><b>والسُقوطُ الآمِن مَنقولٌ كَما هُوَ</b>: مُستَأجِرٌ بِلا
/// جَدوَل إعلانات بَعد يُتَخَطّى ولا يُسقِط الصَفحَة. تَحويلُه إلى
/// رَميٍ قَرارٌ لَه مَوجَتُه.</para>
/// </summary>
public sealed class PlatformStatsQueries
{
    private readonly IDocumentStore _store;

    public PlatformStatsQueries(IDocumentStore store) => _store = store;

    /// <summary>عَدَدُ الإعلانات غَير المَحذوفَة لِكُلّ سلاج مُعطى.
    /// السلاجاتُ الَّتي تَعَذَّرَت قِراءَتُها لا تَظهَر في القامُوس —
    /// نَفسُ سُلوك الصَفحَة حَرفاً.</summary>
    public async Task<IReadOnlyDictionary<string, int>> ListingCountsAsync(
        IEnumerable<string> tenantSlugs, CancellationToken ct = default)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var slug in tenantSlugs)
        {
            try
            {
                await using var s = _store.QuerySession(slug);
                counts[slug] = await s.Query<Listing>().CountAsync(l => !l.IsDeleted, ct);
            }
            catch
            {
                // مُستَأجِر بِلا جَدوَل إعلانات — يُتَخَطّى.
            }
        }

        return counts;
    }
}
