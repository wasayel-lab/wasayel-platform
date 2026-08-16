using ACommerce.Kit.Favorites;
using ACommerce.Kit.Listings;
using ACommerce.Kit.Reviews;
using ACommerce.Templates.Customer.Marketplace.Services.Listings;
using Marten;

namespace ACommerce.Templates.Customer.Marketplace.Services.Queries;

/// <summary>
/// <para><b>ما تَعرِضُه واجِهَةُ المَتجَر الرَئيسِيَّة</b> — الدَورُ
/// المَحفوظ، وشَريطا «أَحدَث» و«مُمَيَّز»، ومُدُنُ الفَلتَرَة، وعَدّاداتُ
/// تَقييم المُعلِنين، ومُفَضَّلاتُ الزائِر. <b>لَقطَةٌ واحِدَة لِلطَلَب</b>
/// كَما كانَت في الصَفحَة حَرفاً — نِداءٌ واحِد وجَلسَةٌ واحِدَة، فَلا
/// يَنمو عَدَدُ الاتِّصالات بِسَبَب النُقلَة.</para>
/// </summary>
public sealed record StorefrontHome(
    string StoredActiveRole,
    IReadOnlyList<Listing> Latest,
    IReadOnlyList<Listing> Featured,
    IReadOnlyList<string> Cities,
    IReadOnlyDictionary<Guid, int> ReviewCounts,
    IReadOnlySet<Guid> FavouriteListingIds)
{
    /// <summary>واجِهَةُ مَتجَرٍ لا وُجودَ لَه — نَفسُ قيَم البَدء في
    /// <c>TenantHome</c> حَرفاً.</summary>
    public static readonly StorefrontHome Empty = new(
        "", Array.Empty<Listing>(), Array.Empty<Listing>(), Array.Empty<string>(),
        new Dictionary<Guid, int>(), new HashSet<Guid>());
}

/// <summary>
/// <para><b>استِعلاماتُ واجِهَة المَتجَر — أَعلى قيمَةٍ لِلتَطبيق في
/// السِجِلّ كُلِّه.</b> ‏<c>/{slug}</c> هي أَوَّلُ شاشَةٍ يَراها
/// المُستَخدِم، وأَوَّلُ ما يَلزَم تَشغيلُه في MAUI Blazor Hybrid.</para>
///
/// <para><b>والفَلتَرَةُ انتَقَلَت مَعَ الاستِعلام لا بَعدَه</b>: كانَت
/// الصَفحَةُ تَجلِب ‏20 ثُمَّ تُسقِط «طَلَبات السائِق» ثُمَّ تَأخُذ ‏6 —
/// وعَدَدُ التَقييمات يُستَعلَم عَن مُلّاك <b>ما بَقِيَ</b> بَعد
/// الإسقاط. فَتَركُ الإسقاط في الصَفحَة كانَ يَعني نِدائَين وجَلسَتَين.
/// نُقِلَ كَما هُوَ — نَفسُ التَرتيب ونَفسُ الأَعداد — فَبَقِيَ النِداءُ
/// واحِداً.</para>
///
/// <para><b>والعَزلُ بُنيَويّ</b>: جَلسَةٌ واحِدَة بِـ
/// <c>QuerySession(tenantSlug)</c>، وكُلّ الوَثائِق المَقروءَة هُنا
/// (<see cref="Listing"/>، <see cref="Review"/>، <see cref="Favorite"/>،
/// <c>User</c>) مُقتَرِنَةُ الإيجار بِالسِياسَة العامَّة.</para>
/// </summary>
public sealed class StorefrontQueries
{
    /// <summary>خاصِّيَّةُ «يَقبَل العُروض» — طَلَبُ سائِقٍ مُؤَقَّت لا
    /// عُنصُرُ كاتالوج. <b>ونَفسُ الثابِت الَّذي تَقرَؤُه خِدمَةُ
    /// التَحرير</b> (<see cref="ListingEditService.AcceptsOffersAttribute"/>)،
    /// فَلا تَعريفانِ لِمَعنىً واحِد.</summary>
    public static bool IsTripRequest(Listing l) =>
        l.Attributes.TryGetValue(ListingEditService.AcceptsOffersAttribute, out var v) &&
        string.Equals(v, "true", StringComparison.OrdinalIgnoreCase);

    /// <summary>مالِكُ الإعلان مِن الخَصائِص — <b>نَفسُ المِفتاح</b>
    /// الَّذي تَكتُبُه نُقطَةُ الإنشاء وتَقرَؤُه
    /// <see cref="ListingEditService.IsOwnedBy"/>.</summary>
    public static Guid? OwnerOf(Listing l) =>
        l.Attributes.TryGetValue(ListingEditService.OwnerAttribute, out var s) &&
        Guid.TryParse(s, out var g) ? g : null;

    private readonly IDocumentStore _store;

    public StorefrontQueries(IDocumentStore store) => _store = store;

    /// <summary>
    /// <para>واجِهَةُ المَتجَر كامِلَةً. <paramref name="city"/> فَلتَرُ
    /// المَدينَة إن وُجِد، و<paramref name="userId"/> صاحِبُ الجَلسَة إن
    /// وُجِد — و<c>null</c> فيهِما تَعني «بِلا فَلتَر» و«زائِر».</para>
    ///
    /// <para><b>والمُمَيَّزُ لَه سُقوطٌ مُعلَن</b>: إن لَم يُعَلَّم شَيءٌ
    /// <c>IsFeatured</c> يُملَأ الكاروسيل بِالأَعلى سِعراً. قَرارٌ قابِل
    /// لِلتَبديل، ومَنقولٌ كَما هُوَ.</para>
    /// </summary>
    public async Task<StorefrontHome> HomeAsync(
        string tenantSlug, string? city, Guid? userId,
        int latestTake = 6, int featuredTake = 4, CancellationToken ct = default)
    {
        await using var s = _store.QuerySession(tenantSlug);

        var storedRole = userId is { } uid
            ? (await s.LoadAsync<ACommerce.Kit.Auth.User>(uid, ct))?.ActiveRole ?? ""
            : "";

        var baseQ = s.Query<Listing>().Where(x => !x.IsDeleted && !x.IsHiddenByModerator);
        if (!string.IsNullOrEmpty(city))
            baseQ = baseQ.Where(x => x.City == city);

        // نَجلِب أَكثَر ثُمّ نُصَفِّي — نَفسُ الأَرقام (‏20 ثُمَّ 6/4).
        var latest = (await baseQ
            .OrderByDescending(x => x.CreatedAt)
            .Take(20).ToListAsync(ct)).ToList();

        var featured = (await baseQ.Where(x => x.IsFeatured)
            .OrderByDescending(x => x.CreatedAt).Take(20).ToListAsync(ct)).ToList();
        if (featured.Count == 0)
            featured = (await baseQ.OrderByDescending(x => x.Price).Take(20).ToListAsync(ct)).ToList();

        var latestShown   = latest.Where(l => !IsTripRequest(l)).Take(latestTake).ToList();
        var featuredShown = featured.Where(l => !IsTripRequest(l)).Take(featuredTake).ToList();

        var cities = (await s.Query<Listing>()
                .Where(x => !x.IsDeleted && x.City != null)
                .Select(x => x.City!)
                .ToListAsync(ct))
            .Distinct().OrderBy(x => x).ToList();

        // استِعلام واحِد يُغَطّي كُلّ البِطاقات المَعروضَة — لا N+1.
        var reviewCounts = new Dictionary<Guid, int>();
        var ownerIds = latestShown.Concat(featuredShown)
            .Select(OwnerOf).Where(g => g.HasValue).Select(g => g!.Value)
            .Distinct().ToList();
        if (ownerIds.Count > 0)
        {
            var revs = await s.Query<Review>()
                .Where(r => !r.Hidden && ownerIds.Contains(r.TargetUserId))
                .ToListAsync(ct);
            reviewCounts = revs.GroupBy(r => r.TargetUserId)
                               .ToDictionary(g => g.Key, g => g.Count());
        }

        var favIds = new HashSet<Guid>();
        if (userId is { } fav)
        {
            var favs = await s.Query<Favorite>().Where(f => f.UserId == fav).ToListAsync(ct);
            favIds = favs.Select(f => f.ListingId).ToHashSet();
        }

        return new StorefrontHome(storedRole, latestShown, featuredShown,
                                  cities, reviewCounts, favIds);
    }
}
