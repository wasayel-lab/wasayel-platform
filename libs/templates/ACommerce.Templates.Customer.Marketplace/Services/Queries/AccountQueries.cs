using ACommerce.Kit.Auth;
using ACommerce.Kit.Cart;
using ACommerce.Kit.Chat;
using ACommerce.Kit.Favorites;
using ACommerce.Kit.Listings;
using ACommerce.Kit.Notifications;
using ACommerce.Kit.Offers;
using ACommerce.Kit.SavedSearches;
using ACommerce.Kit.Support;
using ACommerce.Templates.Customer.Marketplace.Gates;
using Marten;

namespace ACommerce.Templates.Customer.Marketplace.Services.Queries;

/// <summary>
/// <para><b>لَقطَةُ صَفحَة «حِسابي»</b> — الوَثيقَةُ وثَلاثَةُ عَدّادات،
/// بِجَلسَةٍ واحِدَة كَما كانَت في <c>Me.razor</c> حَرفاً.</para>
///
/// <para><b>ولِماذا تُعاد <see cref="User"/> كامِلَةً هُنا</b> بِخِلاف
/// <see cref="ShellState"/> الَّذي رَفَضَها: الشِلُّ يَعرِض عَدَدَين
/// ودَوراً فَتَمريرُ الوَثيقَة إلَيه يَجعَل كُلَّ حَقلٍ فيها سَطحاً؛
/// وصَفحَةُ الحِساب <b>مَوضوعُها المُستَخدِمُ نَفسُه</b> — تَعرِض اسمَه
/// وصورَتَه وهاتِفَه ودَورَه. فَالوَثيقَةُ هُنا لَيسَت تَسَرُّباً بَل هي
/// المَطلوب.</para>
/// </summary>
public sealed record AccountSummary(
    User? Me, int Favorites, int Conversations, int UnreadNotifications)
{
    /// <summary>«لا جَلسَة» — نَفسُ قيَم البَدء في <c>Me.razor</c>
    /// حَرفاً.</summary>
    public static readonly AccountSummary Empty = new(null, 0, 0, 0);
}

/// <summary>
/// <para><b>قائِمَةُ المُحادَثات وصُوَرُ أَطرافِها</b> — استِعلامان في
/// جَلسَةٍ واحِدَة كَما كانا في <c>Chats.razor</c>.</para>
/// </summary>
public sealed record ChatList(
    IReadOnlyList<Conversation> Conversations,
    IReadOnlyDictionary<Guid, string?> PartnerAvatars)
{
    public static readonly ChatList Empty =
        new(Array.Empty<Conversation>(), new Dictionary<Guid, string?>());
}

/// <summary>
/// <para><b>عُروضي ومَواقِعُ التِقاطِها</b>. و<c>Pickups</c> مَبنِيَّةٌ
/// مِن <b>مَجرى أَحداث</b> كُلّ إعلان (<c>AggregateStreamAsync</c>) لا
/// مِن وَثيقَة — نُقِلَ كَما هُوَ، فَتَبديلُه إلى قِراءَةِ لَقطَة
/// تَحسينٌ لَه مَوجَتُه.</para>
/// </summary>
public sealed record MyOffersView(
    IReadOnlyList<Offer> Offers,
    IReadOnlyDictionary<Guid, (double Lat, double Lng)?> Pickups)
{
    public static readonly MyOffersView Empty =
        new(Array.Empty<Offer>(), new Dictionary<Guid, (double, double)?>());
}

/// <summary>
/// <para><b>ما تَسأَلُه شاشاتُ الحِساب عَن صاحِبِها</b> — قَبولُ
/// الشُروط، والدَورُ النَشِط، والبَحوثُ المَحفوظَة. ثَلاثُ قِراءاتٍ
/// كانَت تَفتَح جَلسَةَ Marten مِن داخِل <c>GatedPage.razor</c> و
/// <c>MySearches.razor</c>.</para>
///
/// <para><b>والحارِسُ أَخطَرُ ما يُرَحَّل، فَلا يُدمَج</b>: كانَت
/// <c>GatedPage</c> تَجلِب <see cref="User"/> <b>مَرَّتَين</b> في
/// فَرعَين مَشروطَين — واحِدَةً لِلشُروط وأُخرى لِلصَلاحِيَّة. ودَمجُهُما
/// في نِداءٍ واحِدٍ يُحَسِّن عَدَدَ الاستِعلامات ويُغَيِّر <b>مَتى</b>
/// تُقرَأ الحالَة، وذاكَ تَعديلُ مَنطِقٍ في حارِس. فَبَقِيَتا
/// دالَّتَين، كُلٌّ بِجَلسَتِها، حَرفاً كَما كانَتا.</para>
///
/// <para><b>والعَزلُ بُنيَويّ</b>: كُلّ نِداءٍ هُنا يُفتَح بِـ
/// <c>QuerySession(tenantSlug)</c> — و<see cref="User"/> و
/// <see cref="SavedSearch"/> تَحتَ سِياسَة
/// <c>AllDocumentsAreMultiTenanted</c>. فَنَفسُ الهُوِيَّة في مَتجَرَين
/// وَثيقَتانِ مُستَقِلَّتان، ولا استِعلامَ عابِرَ لِلمُستَأجِرين مُمكِنٌ
/// مِن هُنا.</para>
/// </summary>
public sealed class AccountQueries
{
    private readonly IDocumentStore _store;

    public AccountQueries(IDocumentStore store) => _store = store;

    /// <summary>هَل قَبِلَ صاحِبُ الجَلسَةِ الإصدارَ الحاليّ مِن
    /// الشُروط في هذا المَتجَر؟ <b>والقَرارُ ليسَ هُنا</b> — هُوَ في
    /// <see cref="TermsPolicy.IsAccepted"/> الَّتي يَقرَؤُها الحارِسانِ
    /// أَيضاً، فَما تَفتَحُه الشاشَةُ هُوَ بِالضَبط ما تَقبَلُه
    /// النُقطَة.</summary>
    public async Task<bool> HasAcceptedCurrentTermsAsync(
        string tenantSlug, Guid userId, CancellationToken ct = default)
    {
        await using var s = _store.QuerySession(tenantSlug);
        return TermsPolicy.IsAccepted(await s.LoadAsync<User>(userId, ct));
    }

    /// <summary>الدَورُ المُخَزَّن لِلمُستَخدِم في هذا المَتجَر، أَو
    /// <c>null</c> إن لَم يوجَد. و<c>""</c> قيمَةٌ مَشروعَة تَعني «بِلا
    /// دَور» — فَالتَمييزُ بَينَها وبَينَ «لا مُستَخدِم» مَقصود.</summary>
    public async Task<string?> ActiveRoleAsync(
        string tenantSlug, Guid userId, CancellationToken ct = default)
    {
        await using var s = _store.QuerySession(tenantSlug);
        return (await s.LoadAsync<User>(userId, ct))?.ActiveRole;
    }

    /// <summary>بَحوثُ المُستَخدِم المَحفوظَة، الأَحدَثُ أَوَّلاً.
    /// <b>وتُرَتَّب في الذاكِرَة كَما كانَت</b> في الصَفحَة حَرفاً —
    /// نَقلُ مَوضِعٍ لا تَعديلُ مَنطِق.</summary>
    public async Task<IReadOnlyList<SavedSearch>> SavedSearchesAsync(
        string tenantSlug, Guid userId, CancellationToken ct = default)
    {
        await using var s = _store.QuerySession(tenantSlug);
        return (await s.Query<SavedSearch>()
                .Where(ss => ss.UserId == userId)
                .ToListAsync(ct))
            .OrderByDescending(ss => ss.CreatedAt).ToList();
    }

    /// <summary><b>وَثيقَةُ المُستَخدِم كامِلَةً</b> — تَقرَؤُها الشاشاتُ
    /// الَّتي مَوضوعُها هُوَ: تَحريرُ المِلَفّ، ومِنطَقَةُ السائِق،
    /// وتَهيِئَةُ الدَور، وإنشاءُ الإعلان (لِمَعرِفَة دَورِه النَشِط).
    /// <b>ولا تُدمَج مَع <see cref="ActiveRoleAsync"/></b>: تِلكَ يَقرَؤُها
    /// الحارِسُ فَتَبقى أَضيَقَ ما يُمكِن.</summary>
    public async Task<User?> ProfileAsync(
        string tenantSlug, Guid userId, CancellationToken ct = default)
    {
        await using var s = _store.QuerySession(tenantSlug);
        return await s.LoadAsync<User>(userId, ct);
    }

    /// <summary>سَلَّةُ المُستَخدِم — مُعَرِّفُها هُوَ مُعَرِّفُه.</summary>
    public async Task<Cart?> CartAsync(
        string tenantSlug, Guid userId, CancellationToken ct = default)
    {
        await using var s = _store.QuerySession(tenantSlug);
        return await s.LoadAsync<Cart>(userId, ct);
    }

    /// <summary>إعلاناتُ المُفَضَّلَة، بِتَرتيب الإضافَة الأَحدَث أَوَّلاً
    /// و<b>المَحذوفُ مُسقَط</b> — نَفسُ خُطوَتَي <c>Favorites.razor</c>
    /// حَرفاً.</summary>
    public async Task<IReadOnlyList<Listing>> FavoriteListingsAsync(
        string tenantSlug, Guid userId, CancellationToken ct = default)
    {
        await using var s = _store.QuerySession(tenantSlug);
        var favs = await s.Query<Favorite>()
            .Where(f => f.UserId == userId)
            .OrderByDescending(f => f.At).ToListAsync(ct);
        var listingIds = favs.Select(f => f.ListingId).ToList();
        return (await s.Query<Listing>()
            .Where(l => listingIds.Contains(l.Id) && !l.IsDeleted)
            .ToListAsync(ct)).ToList();
    }

    /// <summary><b>إعلاناتي</b>. والفَلتَرَةُ في الذاكِرَة نُقلَةٌ لا
    /// اختِيار: ‏Marten لا يُفَلتِر <c>Attributes</c> في LINQ، فَتُجلَب
    /// إعلاناتُ المُستَأجِر ثُمَّ يُقارَن <c>owner_id</c> — نَفسُ ما كانَ
    /// في <c>MyListings.razor</c>، ونَفسُ المِفتاح الَّذي تَقرَؤُه
    /// <see cref="Listings.ListingEditService.IsOwnedBy"/>.</summary>
    public async Task<IReadOnlyList<Listing>> MyListingsAsync(
        string tenantSlug, Guid userId, CancellationToken ct = default)
    {
        await using var s = _store.QuerySession(tenantSlug);
        var meId = userId.ToString();
        var all = await s.Query<Listing>().Where(l => !l.IsDeleted).ToListAsync(ct);
        return all
            .Where(l => l.Attributes.TryGetValue(
                Listings.ListingEditService.OwnerAttribute, out var oid) && oid == meId)
            .OrderByDescending(l => l.CreatedAt)
            .ToList();
    }

    /// <summary><b>عُروضي</b>، والمُنتَهي يُؤَخَّر في العَرض وإن بَقِيَ
    /// <c>Pending</c> في المَخزَن (لا مُهِمَّةَ دَورِيَّة بَعد) — نُقِلَ
    /// كَما هُوَ. و<paramref name="now"/> يُمَرَّر لِتَبقى الدالَّةُ
    /// حَتمِيَّةً بِمُدخَلِها.</summary>
    public async Task<MyOffersView> MyOffersAsync(
        string tenantSlug, Guid userId, DateTime now, CancellationToken ct = default)
    {
        await using var s = _store.QuerySession(tenantSlug);

        var offers = (await s.Query<Offer>()
            .Where(o => o.OffererId == userId)
            .ToListAsync(ct))
            .OrderBy(o => o.Status == OfferStatus.Pending && o.ExpiresAt > now ? 0 : 1)
            .ThenByDescending(o => o.SubmittedAt)
            .ToList();

        var pickups = new Dictionary<Guid, (double Lat, double Lng)?>();
        foreach (var lid in offers.Select(o => o.ListingId).Distinct())
        {
            var listing = await s.Events.AggregateStreamAsync<Listing>(lid, token: ct);
            pickups[lid] = PickupOf(listing);
        }

        return new MyOffersView(offers, pickups);
    }

    /// <summary>مَوقِعُ الالتِقاط مِن خَصائِص الإعلان، أَو <c>null</c>.
    /// <b>مُنفَصِلَةٌ ونَقِيَّة</b> فَتُختَبَر بِلا قاعِدَة بَيانات.</summary>
    public static (double Lat, double Lng)? PickupOf(Listing? listing)
    {
        if (listing is null) return null;
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        return listing.Attributes.TryGetValue("pickup_lat", out var latStr) &&
               listing.Attributes.TryGetValue("pickup_lng", out var lngStr) &&
               double.TryParse(latStr, System.Globalization.NumberStyles.Float, inv, out var lat) &&
               double.TryParse(lngStr, System.Globalization.NumberStyles.Float, inv, out var lng)
            ? (lat, lng)
            : null;
    }

    /// <summary>مُحادَثاتي (‏أَحدَث ‏50) وصُوَرُ الأَطراف الآخَرين —
    /// استِعلامان في جَلسَةٍ واحِدَة كَما كانا.</summary>
    public async Task<ChatList> ConversationsAsync(
        string tenantSlug, Guid userId, CancellationToken ct = default)
    {
        await using var s = _store.QuerySession(tenantSlug);

        var convs = (await s.Query<Conversation>()
            .Where(c => c.OwnerId == userId || c.PartnerId == userId)
            .OrderByDescending(c => c.LastAt)
            .Take(50).ToListAsync(ct)).ToList();

        // ‏ToList() مَقصودَة: ‏Marten يَدعَم List<T>.Contains في WHERE ولا
        // يَدعَم Array.Contains.
        var partnerIds = convs
            .Select(c => c.OwnerId == userId ? c.PartnerId : c.OwnerId)
            .Distinct().ToList();

        var avatars = new Dictionary<Guid, string?>();
        if (partnerIds.Count > 0)
        {
            var users = await s.Query<User>().Where(u => partnerIds.Contains(u.Id)).ToListAsync(ct);
            avatars = users.ToDictionary(u => u.Id, u => u.AvatarUrl);
        }

        return new ChatList(convs, avatars);
    }

    /// <summary>تَذاكِرُ الدَعم الَّتي كَتَبتُها — أَحدَث ‏50.</summary>
    public async Task<IReadOnlyList<Ticket>> SupportTicketsAsync(
        string tenantSlug, Guid userId, CancellationToken ct = default)
    {
        await using var s = _store.QuerySession(tenantSlug);
        return (await s.Query<Ticket>()
            .Where(t => t.AuthorId == userId)
            .OrderByDescending(t => t.UpdatedAt).Take(50).ToListAsync(ct)).ToList();
    }

    /// <summary>لَقطَةُ صَفحَة «حِسابي»: الوَثيقَةُ وثَلاثَةُ عَدّادات،
    /// بِجَلسَةٍ واحِدَة كَما كانَت.</summary>
    public async Task<AccountSummary> SummaryAsync(
        string tenantSlug, Guid userId, CancellationToken ct = default)
    {
        await using var s = _store.QuerySession(tenantSlug);
        return new AccountSummary(
            Me: await s.LoadAsync<User>(userId, ct),
            Favorites: await s.Query<Favorite>().CountAsync(f => f.UserId == userId, ct),
            Conversations: await s.Query<Conversation>()
                .CountAsync(c => c.OwnerId == userId || c.PartnerId == userId, ct),
            UnreadNotifications: await s.Query<Notification>()
                .CountAsync(n => n.UserId == userId && !n.IsRead, ct));
    }
}
