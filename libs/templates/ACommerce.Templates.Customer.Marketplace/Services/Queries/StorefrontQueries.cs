using ACommerce.Kit.Auth;
using ACommerce.Kit.Chat;
using ACommerce.Kit.Favorites;
using ACommerce.Kit.Listings;
using ACommerce.Kit.Offers;
using ACommerce.Kit.Reviews;
using ACommerce.Kit.SavedSearches;
using ACommerce.Templates.Customer.Marketplace.Services.Listings;
using Marten;

namespace ACommerce.Templates.Customer.Marketplace.Services.Queries;

/// <summary>صَفحَةُ مُعلِنٍ واحِد — وَثيقَتُه، وإعلاناتُه، وتَقييماتُه.
/// جَلسَةٌ واحِدَة كَما كانَت في <c>VendorProfile.razor</c>.</summary>
public sealed record VendorPage(
    User? Vendor, IReadOnlyList<Listing> Listings, IReadOnlyList<Review> Reviews)
{
    public static readonly VendorPage Empty =
        new(null, Array.Empty<Listing>(), Array.Empty<Review>());
}

/// <summary>رَئيسِيَّةُ التاجِر: إعلاناتُه وعَدَدُ مُحادَثاتِه.
/// <c>المُشاهَداتُ والعَدَدُ النَشِط</c> مُشتَقّانِ في الصَفحَة مِن
/// القائِمَة نَفسِها، فَلا يُنقَلانِ هُنا — اشتِقاقٌ لا استِعلام.</summary>
public sealed record SellerFeed(IReadOnlyList<Listing> MyListings, int Conversations)
{
    public static readonly SellerFeed Empty = new(Array.Empty<Listing>(), 0);
}

/// <summary>
/// <para>رَئيسِيَّةُ السائِق — إعداداتُ مَنطِقَتِه، والطَلَباتُ
/// المَفتوحَة الَّتي لَم تُطابَق، وعَدّادانِ.</para>
///
/// <para><b>والفَلتَرَةُ انتَقَلَت مَعَ الاستِعلام</b>: الحَلقَةُ تَقرَأ
/// <c>ListingMatch</c> لِكُلّ إعلانٍ يَقبَل العُروض حَتّى تَجمَع ‏12 —
/// فَتَركُها في الصَفحَة كانَ يَعني جَلسَةً ثانِيَة أَو جَلبَ ‏80
/// إعلاناً إلى التَخطيط. نُقِلَت كَما هي: نَفسُ السَقف، ونَفسُ شَرط
/// الحَجب، ونَفسُ التَرتيب (الأَقرَب ثُمَّ الأَحدَث).</para>
/// </summary>
public sealed record DriverFeed(
    int RadiusKm, bool HasAnchor, double AnchorLat, double AnchorLng,
    IReadOnlyList<(Listing Listing, double? DistanceKm)> Open,
    int PendingOffers, int SavedSearches)
{
    public static readonly DriverFeed Empty = new(
        0, false, 0, 0, Array.Empty<(Listing, double?)>(), 0, 0);
}

/// <summary>فَلاتِرُ شاشَة الاستِكشاف — كُلُّها مِن مُتَغَيِّرات الـURL
/// حَرفاً، ولا واحِدَةَ مِنها قَرار.</summary>
public sealed record ExploreFilters(
    string? Category = null, string? Kind = null, string? District = null,
    decimal? MinPrice = null, decimal? MaxPrice = null,
    string? Query = null, string? Sort = null);

/// <summary>ما تَعرِضُه شاشَةُ الاستِكشاف. و<c>DriverMode</c> مُعادٌ
/// هُنا لا لِأَنّ الخِدمَةَ قَرَّرَته — بَل لِأَنَّها هي مَن نادَت
/// دالَّةَ القَرار، فَتَرُدُّ ما جاءَت بِه.</summary>
public sealed record ExploreResult(
    IReadOnlyList<Listing> Items,
    IReadOnlyList<string> Districts,
    IReadOnlyDictionary<Guid, int> ReviewCounts,
    IReadOnlySet<Guid> FavouriteListingIds,
    bool DriverMode)
{
    public static readonly ExploreResult Empty = new(
        Array.Empty<Listing>(), Array.Empty<string>(),
        new Dictionary<Guid, int>(), new HashSet<Guid>(), false);
}

/// <summary>
/// <para><b>صَفحَةُ تَفصيل إعلانٍ واحِد</b> — الإعلانُ مِن مَجرى
/// أَحداثِه، وهَل هُوَ في مُفَضَّلَة السائِل، ومُطابَقَتُه إن وُجِدَت،
/// والعُروضُ المُعَلَّقَة. جَلسَةٌ واحِدَة كَما كانَت في
/// <c>TenantListingDetail.razor</c>.</para>
///
/// <para><b>وما لَم يَنتَقِل مَعَها: تَزييدُ عَدّاد المُشاهَدَة.</b>
/// كانَت الصَفحَةُ تُلحِق <c>ListingViewed</c> بِمَجرى الأَحداث
/// وتُودِع <b>في طَلَب <c>GET</c></b>، فَيَرفَعُ العَدّادَ كُلُّ زاحِفٍ
/// وجالِبٍ مُسبَق وكُلُّ جَولَةِ تَحَقُّقٍ بَصَريّ. وصارَ أَثَراً
/// جانِبِيّاً خارِجَ مَسار العَرض (<c>POST /{slug}/listings/{id}/view</c>).
/// فَهذِه الخِدمَةُ <b>تَقرَأ ولا تَكتُب</b>، وذاكَ شَرطُ
/// مُجَلَّدِها.</para>
/// </summary>
public sealed record ListingDetail(
    Listing? Listing, bool IsFavourite, ListingMatch? Match, IReadOnlyList<Offer> Offers)
{
    public static readonly ListingDetail Empty =
        new(null, false, null, Array.Empty<Offer>());
}

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

    /// <summary>صَفحَةُ مُعلِن. <b>ونَفسُ مِفتاح المِلكِيَّة</b> الَّذي
    /// تَقرَؤُه <see cref="ListingEditService.IsOwnedBy"/> و
    /// <see cref="AccountQueries.MyListingsAsync"/> — ثَلاثَةُ قُرّاءٍ
    /// وتَعريفٌ واحِد.</summary>
    public async Task<VendorPage> VendorAsync(
        string tenantSlug, Guid vendorId, CancellationToken ct = default)
    {
        await using var s = _store.QuerySession(tenantSlug);

        var vendor = await s.LoadAsync<User>(vendorId, ct);
        if (vendor is null) return VendorPage.Empty;

        var all = await s.Query<Listing>()
            .Where(l => !l.IsDeleted)
            .OrderByDescending(l => l.CreatedAt)
            .Take(50).ToListAsync(ct);
        var listings = all.Where(l => OwnerOf(l) == vendorId).ToList();

        var reviews = (await s.Query<Review>()
            .Where(r => r.TargetUserId == vendorId)
            .OrderByDescending(r => r.At)
            .Take(20).ToListAsync(ct)).ToList();

        return new VendorPage(vendor, listings, reviews);
    }

    /// <summary>
    /// <para><b>صَفحَةُ تَفصيل الإعلان.</b> والإعلانُ يُبنى مِن
    /// <b>مَجرى أَحداثِه</b> لا مِن لَقطَةٍ — نُقِلَ كَما هُوَ.</para>
    ///
    /// <para><b>وشَرطُ جَلب العُروض نُقِلَ مَعَها لِأَنَّه لَيسَ
    /// قَراراً</b>: «يَقبَل العُروض» هُوَ <see cref="IsTripRequest"/>
    /// نَفسُها — ثابِتُ <see cref="ListingEditService"/> الَّذي تَقرَؤُه
    /// الواجِهَةُ والاستِكشافُ ورَئيسِيَّةُ السائِق. وتَركُه في الصَفحَة
    /// كانَ يَعني جَلسَةً ثانِيَة لِاستِعلامٍ مَشروط.</para>
    ///
    /// <para><paramref name="now"/> يُمَرَّر لِتَبقى الدالَّةُ حَتمِيَّةً
    /// بِمُدخَلِها — نَفسُ ما فَعَلَته <see cref="AccountQueries.MyOffersAsync"/>.</para>
    /// </summary>
    public async Task<ListingDetail> ListingDetailAsync(
        string tenantSlug, Guid listingId, Guid? userId, DateTime now,
        CancellationToken ct = default)
    {
        await using var s = _store.QuerySession(tenantSlug);

        var listing = await s.Events.AggregateStreamAsync<Listing>(listingId, token: ct);
        // المَحذوفُ يُعاد كَما هُوَ: الصَفحَةُ تُصَيِّر لَه فَرعاً
        // خاصّاً («هذا الإعلان مَحذوف») ولا تَقرَأ لَه شَيئاً بَعدَه.
        if (listing is null || listing.IsDeleted)
            return new ListingDetail(listing, false, null, Array.Empty<Offer>());

        var isFav = userId is { } uid &&
            await s.LoadAsync<Favorite>(Favorite.MakeId(uid, listingId), ct) is not null;

        var match = await s.LoadAsync<ListingMatch>(listingId, ct);

        IReadOnlyList<Offer> offers = Array.Empty<Offer>();
        if (IsTripRequest(listing) || match is not null)
            offers = (await s.Query<Offer>()
                    .Where(o => o.ListingId == listingId && o.Status == OfferStatus.Pending)
                    .ToListAsync(ct))
                .Where(o => o.ExpiresAt > now)      // اِستَبعِد المُنتَهيَة
                .OrderBy(o => o.Price).ToList();

        return new ListingDetail(listing, isFav, match, offers);
    }

    /// <summary>سائِقو المَتجَر — مَن دَورُه النَشِط <c>driver</c>،
    /// الأَحدَثُ تَحديثاً أَوَّلاً. «مُتاح» تَعني هذا وَحدَه اليَوم،
    /// كَما كانَ في <c>TenantDrivers.razor</c> حَرفاً.</summary>
    public async Task<IReadOnlyList<User>> DriversAsync(
        string tenantSlug, CancellationToken ct = default)
    {
        await using var s = _store.QuerySession(tenantSlug);
        return (await s.Query<User>()
                .Where(u => u.ActiveRole == "driver")
                .ToListAsync(ct))
            .OrderByDescending(u => u.UpdatedAt).ToList();
    }

    /// <summary>رَئيسِيَّةُ التاجِر.</summary>
    public async Task<SellerFeed> SellerHomeAsync(
        string tenantSlug, Guid userId, CancellationToken ct = default)
    {
        await using var s = _store.QuerySession(tenantSlug);

        var mine = (await s.Query<Listing>().Where(l => !l.IsDeleted).ToListAsync(ct))
            .Where(l => OwnerOf(l) == userId)
            .OrderByDescending(l => l.CreatedAt).ToList();

        var conversations = await s.Query<Conversation>()
            .CountAsync(c => c.OwnerId == userId || c.PartnerId == userId, ct);

        return new SellerFeed(mine, conversations);
    }

    /// <summary>رَئيسِيَّةُ الراكِب — طَلَباتُه الثَمانِيَة الأَحدَث، مَع
    /// عَدَد العُروض المُعَلَّقَة على كُلٍّ وهَل طوبِقَ.</summary>
    public async Task<IReadOnlyList<(Listing Listing, int PendingOffers, bool Matched)>>
        RiderHomeAsync(string tenantSlug, Guid userId, CancellationToken ct = default)
    {
        await using var s = _store.QuerySession(tenantSlug);

        var mine = (await s.Query<Listing>().Where(l => !l.IsDeleted).ToListAsync(ct))
            .Where(l => OwnerOf(l) == userId)
            .OrderByDescending(l => l.CreatedAt)
            .Take(8).ToList();

        var result = new List<(Listing, int, bool)>(mine.Count);
        foreach (var l in mine)
        {
            var matched = await s.LoadAsync<ListingMatch>(l.Id, ct);
            var count = await s.Query<Offer>()
                .CountAsync(o => o.ListingId == l.Id && o.Status == OfferStatus.Pending, ct);
            result.Add((l, count, matched is not null));
        }
        return result;
    }

    /// <summary>رَئيسِيَّةُ السائِق. <paramref name="userId"/> فارِغٌ
    /// لِزائِرٍ — عِندَها تُقرَأ الطَلَباتُ بِلا مَنطِقَة وبِلا عَدّادات،
    /// نَفسَ ما كانَت تَفعَلُه الصَفحَة.</summary>
    public async Task<DriverFeed> DriverHomeAsync(
        string tenantSlug, Guid? userId, CancellationToken ct = default)
    {
        await using var s = _store.QuerySession(tenantSlug);

        var radius = 0; double aLat = 0, aLng = 0; var hasAnchor = false;
        if (userId is { } uid && await s.LoadAsync<User>(uid, ct) is { } me)
        {
            radius = me.RadiusKm; hasAnchor = me.HasAnchor;
            aLat = me.AnchorLat;  aLng = me.AnchorLng;
        }

        var all = await s.Query<Listing>()
            .Where(l => !l.IsDeleted)
            .OrderByDescending(l => l.CreatedAt)
            .Take(80).ToListAsync(ct);

        var open = new List<(Listing, double?)>();
        foreach (var l in all)
        {
            if (!IsTripRequest(l)) continue;
            // أَيّ مُطابَقَة (Active/Completed/Aborted) ← لِلإعلان نَتيجَة، فَيُحجَب.
            if (await s.LoadAsync<ListingMatch>(l.Id, ct) is not null) continue;

            double? dist = null;
            if (hasAnchor &&
                ParseCoord(l.Attributes, "pickup_lat") is double plat &&
                ParseCoord(l.Attributes, "pickup_lng") is double plng)
            {
                dist = OfferHelpers.DistanceKm(aLat, aLng, plat, plng);
                if (radius > 0 && dist > radius) continue;
            }

            open.Add((l, dist));
            if (open.Count >= 12) break;
        }

        var ordered = open
            .OrderBy(x => x.Item2 ?? double.MaxValue)
            .ThenByDescending(x => x.Item1.CreatedAt)
            .ToList();

        var pending = 0; var saved = 0;
        if (userId is { } u2)
        {
            pending = await s.Query<Offer>()
                .CountAsync(o => o.OffererId == u2 && o.Status == OfferStatus.Pending, ct);
            saved = await s.Query<SavedSearch>()
                .CountAsync(ss => ss.UserId == u2 && ss.IsEnabled, ct);
        }

        return new DriverFeed(radius, hasAnchor, aLat, aLng, ordered, pending, saved);
    }

    /// <summary>
    /// <para><b>شاشَةُ الاستِكشاف.</b> أُجِّلَت في المَوجَة ٥ لِأَنّ
    /// قِراءَتَها مُتَشابِكَةٌ مَع <b>قَرار تَأليف</b> في مُنتَصَفِها:
    /// تُقرَأ <c>User</c>، ثُمَّ يُحسَب «وَضعُ السائِق» مِن أَدوار
    /// المُستَأجِر ودَورِ الـURL والدَورِ المَحفوظ، ثُمَّ يُبنى
    /// الاستِعلامُ في <b>نَفس</b> الجَلسَة.</para>
    ///
    /// <para><b>والحَلُّ لا يَنقُل القَرار ولا يَشُقّ الجَلسَة</b>:
    /// <paramref name="isDriverMode"/> دالَّةٌ <b>نَقِيَّة</b> تَبقى
    /// مُعَرَّفَةً في الصَفحَة (هُناكَ يَعيش المُعجَمُ المُغلَق
    /// <c>ExploreModes</c> و<c>ResolveComposition</c>)، والخِدمَةُ
    /// تُنادِيها بِالوَثيقَة الَّتي جَلَبَتها. فَالقَرارُ في مَوضِعِه،
    /// والجَلسَةُ واحِدَة كَما كانَت، ولا بايتَ يَتَبَدَّل.</para>
    ///
    /// <para><paramref name="tenant"/> يَلزَم لِفَلتَرَة <c>kind</c>
    /// وَحدَها — تُشتَقّ مِنه سلاجاتُ الفِئات، وهو مَقروءٌ سَلَفاً في
    /// الصَفحَة فَلا يُقرَأ مَرَّتَين.</para>
    /// </summary>
    public async Task<ExploreResult> ExploreAsync(
        string tenantSlug, ACommerce.Kit.Tenants.Tenant tenant, ExploreFilters f,
        Guid? userId, Func<ACommerce.Kit.Auth.User?, bool> isDriverMode,
        CancellationToken ct = default)
    {
        await using var t = _store.QuerySession(tenantSlug);

        var driverMode = false;
        User? me = null;
        if (tenant.Roles.Count > 0 && userId is { } uid)
        {
            me = await t.LoadAsync<User>(uid, ct);
            driverMode = isDriverMode(me);
        }

        var q = t.Query<Listing>().Where(x => !x.IsDeleted && !x.IsHiddenByModerator);
        if (!string.IsNullOrEmpty(f.Category)) q = q.Where(x => x.CategorySlug == f.Category);
        else if (!string.IsNullOrEmpty(f.Kind))
        {
            // فَلتَرَة بِالـ Kind: كُلُّ سلاجات الفِئات المُنتَمِيَة إلَيه.
            var slugs = tenant.Categories
                .Where(c => c.Kind == f.Kind)
                .Select(c => c.Slug).ToList();
            q = q.Where(x => slugs.Contains(x.CategorySlug));
        }
        if (!string.IsNullOrEmpty(f.District)) q = q.Where(x => x.District == f.District);
        if (f.MinPrice is > 0) q = q.Where(x => x.Price >= f.MinPrice!.Value);
        if (f.MaxPrice is > 0) q = q.Where(x => x.Price <= f.MaxPrice!.Value);
        if (!string.IsNullOrEmpty(f.Query))
        {
            var s = f.Query;
            q = q.Where(x => x.Title.Contains(s) ||
                             (x.Description != null && x.Description.Contains(s)));
        }
        q = f.Sort switch
        {
            "price_asc"  => q.OrderBy(x => x.Price),
            "price_desc" => q.OrderByDescending(x => x.Price),
            _            => q.OrderByDescending(x => x.CreatedAt),
        };

        var items = (await q.Take(200).ToListAsync(ct)).ToList();

        if (driverMode)
        {
            // أَيّ ListingMatch (‏Active/Completed/Aborted) يَعني أَنّ
            // الإعلان صَدَرَت لَه نَتيجَة، فَلا يُعرَض فُرصَةً جَديدَة.
            var matchedIds = (await t.Query<ListingMatch>().ToListAsync(ct))
                .Select(m => m.Id).ToHashSet();
            items = items
                .Where(IsTripRequest)
                .Where(l => !matchedIds.Contains(l.Id))
                .ToList();

            if (me is { HasAnchor: true, RadiusKm: > 0 })
            {
                items = items.Where(l =>
                {
                    var plat = ParseCoord(l.Attributes, "pickup_lat");
                    var plng = ParseCoord(l.Attributes, "pickup_lng");
                    if (plat is null || plng is null) return true;
                    return OfferHelpers.DistanceKm(
                        me.AnchorLat, me.AnchorLng, plat.Value, plng.Value) <= me.RadiusKm;
                }).ToList();
            }
            items = items.Take(60).ToList();
        }
        else
        {
            // بَقِيَّةُ الزَوّار لا يَرَونَ طَلَبات السائِق المُؤَقَّتَة.
            items = items.Where(l => !IsTripRequest(l)).Take(60).ToList();
        }

        var districts = (await t.Query<Listing>()
                .Where(x => !x.IsDeleted && x.District != null)
                .Select(x => x.District!)
                .ToListAsync(ct))
            .Distinct().OrderBy(s => s).ToList();

        var reviewCounts = new Dictionary<Guid, int>();
        var ownerIds = items.Select(OwnerOf).Where(g => g.HasValue)
                            .Select(g => g!.Value).Distinct().ToList();
        if (ownerIds.Count > 0)
        {
            var revs = await t.Query<Review>()
                .Where(r => !r.Hidden && ownerIds.Contains(r.TargetUserId))
                .ToListAsync(ct);
            reviewCounts = revs.GroupBy(r => r.TargetUserId)
                               .ToDictionary(g => g.Key, g => g.Count());
        }

        var favIds = new HashSet<Guid>();
        if (userId is { } fav)
        {
            var favs = await t.Query<Favorite>().Where(x => x.UserId == fav).ToListAsync(ct);
            favIds = favs.Select(x => x.ListingId).ToHashSet();
        }

        return new ExploreResult(items, districts, reviewCounts, favIds, driverMode);
    }

    /// <summary>إحداثِيَّةٌ مِن خَصائِص الإعلان — نَقِيَّةٌ فَتُختَبَر بِلا
    /// قاعِدَة بَيانات.</summary>
    public static double? ParseCoord(IReadOnlyDictionary<string, string> attrs, string key)
        => attrs.TryGetValue(key, out var s) && !string.IsNullOrEmpty(s) &&
           double.TryParse(s, System.Globalization.NumberStyles.Float,
                           System.Globalization.CultureInfo.InvariantCulture, out var d)
            ? d : null;
}
