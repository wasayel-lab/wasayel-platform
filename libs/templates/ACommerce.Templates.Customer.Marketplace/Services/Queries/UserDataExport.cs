using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using ACommerce.Kit.Auth;
using ACommerce.Kit.Chat;
using ACommerce.Kit.Listings;
using ACommerce.Kit.Offers;
using ACommerce.Kit.Reviews;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using Marten;

namespace ACommerce.Templates.Customer.Marketplace.Services.Queries;

// ═══ «بَياناتي» — حَقُّ الاطِّلاعِ والنَقلِ بِبابٍ يَفتَحُه صاحِبُه (‏2026-10-29، قَرارُ المالِك د53 «أ/ب/ج») ═══
//
// صَفحَةٌ يَقرَؤُها الإنسانُ (`/{slug}/me/data`) ومِلَفٌّ تَقرَؤُه البَرامِجُ (`GET /{slug}/me/data/download`) — **والاثنانِ يَقرَآنِ
// الاستِعلامَ نَفسَه**: <see cref="UserDataQueries.ExportAsync"/> ثُمَّ <see cref="UserDataExport.Project"/>، فَلا يَقولُ المِلَفُّ ما لا تَقولُه
// الصَفحَةُ ولا العَكس. والنِطاقُ ما سَمّاه الجَوابُ (٢ «ب»): الحِسابُ ومَعَه صَفَقاتُه ومُحادَثاتُه وتَقييماتُه وعُروضُه وإعلاناتُه.
//
// **والإسقاطُ صَريحٌ لا الوَثيقَةُ كَما هي** — لِسَبَبَين: (١) ما في الوَثائِقِ مِن بَياناتِ غَيرِه لا يُنسَخُ إلَيه: عُنوانُ التَوصيلِ وهاتِفُه
// يَخرُجانِ لِمُبادِرِ الصَفقَةِ وَحدَه (كَتَبَهُما هُو)، لا لِبائِعِها؛ (٢) وما في وَثيقَةِ الحِسابِ مِن أَسرارِ التَشغيلِ لا يُكتَبُ في مِلَفٍّ
// يُنَزَّل: مَفاتيحُ اشتِراكاتِ الإشعارِ (يُعَدُّ عَدَدُها وَحدَه) وإصدارُ الجَلسَة. **والإسقاطُ نَفسُه يُرَشِّحُ** ما لا يَخُصُّ صاحِبَ الطَلَب،
// فَاستِعلامٌ أَوسَعُ مِن حَقِّه لا يُسَرِّبُ صَفّاً (والصُفوفُ في `UserDataExportTests`).

/// <summary>بَياناتُ الحِساب — كَما تَحفَظُها وَثيقَةُ <see cref="User"/>، بِلا أَسرارِ التَشغيل.</summary>
public sealed record UserDataAccount(
    Guid Id, string FullName, string Phone, bool PhoneVerified, string Email, bool EmailVerified,
    string? NationalId, string? AvatarUrl, string ActiveRole,
    IReadOnlyDictionary<string, string> Attributes,
    IReadOnlyDictionary<string, Dictionary<string, string>> RoleAttributes,
    UserDataArea? WorkArea, int PushDevices,
    int AcceptedTermsVersion, DateTime? AcceptedTermsAt, DateTime CreatedAt, DateTime UpdatedAt);

/// <summary>مَنطِقَةُ العَمَل — حينَ ضُبِطَ نِصفُ قُطرِها.</summary>
public sealed record UserDataArea(double Lat, double Lng, int RadiusKm);

/// <summary>جانِبُ صاحِبِ الطَلَبِ مِن الصَفقَة — مَعجَمٌ مُغلَق.</summary>
public enum UserDataDealSide
{
    /// <summary>هُوَ المُبادِر (المُشتَري أَو صاحِبُ العَرض).</summary>
    Initiator,
    /// <summary>هُوَ الطَرَفُ المُقابِل، أَو مالِكُ الإعلانِ لِعَرضٍ لَم يُقبَل بَعد.</summary>
    Counterparty,
}

public sealed record UserDataDealStep(string Action, DealStage StageBefore, DealStage StageAfter, bool ByYou, DateTime At);

public sealed record UserDataDeal(
    Guid Id, UserDataDealSide Side, string Pattern, Guid? ListingId, string ListingTitle, string? OtherPartyName,
    decimal AmountSar, DealStage Stage, DealStatus Status, string? CancelReason,
    string? DeliveryAddress, string? DeliveryPhone, DateTime CreatedAt, DateTime UpdatedAt,
    IReadOnlyList<UserDataDealStep> Timeline);

public sealed record UserDataMessage(bool FromYou, string Body, DateTime SentAt);

public sealed record UserDataConversation(
    Guid Id, string? Subject, Guid? ListingId, string OtherPartyName, DateTime CreatedAt,
    IReadOnlyList<UserDataMessage> Messages);

/// <summary>اتِّجاهُ التَقييم — مَعجَمٌ مُغلَق.</summary>
public enum UserDataReviewDirection
{
    /// <summary>كَتَبَه صاحِبُ الطَلَب.</summary>
    Written,
    /// <summary>كُتِبَ عَنه.</summary>
    Received,
}

public sealed record UserDataReview(
    Guid Id, UserDataReviewDirection Direction, string OtherPartyName, int Rating, string Body,
    Guid? DealId, bool VerifiedTransaction, string? Response, DateTime At);

public sealed record UserDataOffer(
    Guid Id, Guid ListingId, decimal Price, string? Message, OfferStatus Status,
    double Lat, double Lng, DateTime SubmittedAt, DateTime ExpiresAt, DateTime? ResolvedAt);

public sealed record UserDataListing(
    Guid Id, string Title, string? Description, decimal Price, string CategorySlug, string? City, string? District,
    IReadOnlyDictionary<string, string> Attributes, IReadOnlyList<string> MediaUrls, int ViewCount, bool IsDeleted,
    DateTime CreatedAt, DateTime UpdatedAt);

/// <summary>كُلُّ ما يُسَلَّمُ لِصاحِبِ الحِسابِ عَن نَفسِه في مَتجَرٍ واحِد.</summary>
public sealed record UserDataExport(
    string TenantSlug, DateTime GeneratedAt, UserDataAccount Account,
    IReadOnlyList<UserDataDeal> Deals, IReadOnlyList<UserDataConversation> Conversations,
    IReadOnlyList<UserDataReview> Reviews, IReadOnlyList<UserDataOffer> Offers, IReadOnlyList<UserDataListing> Listings)
{
    /// <summary>
    /// <para><b>الإسقاطُ — دالَّةٌ نَقِيَّة</b>: لا قاعِدَة، والوَقتُ وَسيط. تَأخُذُ ما جَلَبَه الاستِعلامُ وتُبقي ما يَخُصُّ
    /// <paramref name="user"/> وَحدَه: صَفقَةٌ هُوَ طَرَفُها أَو عَرضٌ وارِدٌ عَلى إعلانِه (<see cref="DealsService.IsIncomingOfferFor"/>)،
    /// ومُحادَثَةٌ هُوَ أَحَدُ طَرَفَيها ورَسائِلُها وَحدَها، وتَقييمٌ كَتَبَه أَو كُتِبَ عَنه، وعَرضٌ قَدَّمَه، وإعلانٌ مالِكُه هُوَ
    /// (‏<see cref="ACommerce.Templates.Customer.Marketplace.Services.Listings.ListingEditService.OwnerAttribute"/> — والمَحذوفُ مِنها داخِلٌ بِعَلامَتِه: ما زالَ مَحفوظاً).</para>
    /// <para><c>null</c> لِحِسابٍ غائِبٍ أَو مَحذوف — فَلا شَيءَ يُسَلَّمُ عَن هُوِيَّةٍ زالَت.</para>
    /// </summary>
    public static UserDataExport? Project(
        string tenantSlug, DateTime now, User? user,
        IEnumerable<Deal> deals, IEnumerable<Conversation> conversations, IEnumerable<Message> messages,
        IEnumerable<Review> reviews, IEnumerable<Offer> offers, IEnumerable<Listing> listings)
    {
        if (user is null || user.DeletedAt is not null) return null;
        var me = user.Id;

        var account = new UserDataAccount(
            user.Id, user.FullName, user.Phone, user.PhoneVerified, user.Email, user.EmailVerified,
            user.NationalId, user.AvatarUrl, user.ActiveRole,
            new Dictionary<string, string>(user.AttributesJson),
            user.RoleAttributesJson.ToDictionary(r => r.Key, r => new Dictionary<string, string>(r.Value)),
            user.RadiusKm > 0 ? new UserDataArea(user.AnchorLat, user.AnchorLng, user.RadiusKm) : null,
            user.PushSubscriptions.Count, user.AcceptedTermsVersion, user.AcceptedTermsAt, user.CreatedAt, user.UpdatedAt);

        var myDeals = deals
            .Where(d => d.InitiatorId == me || d.CounterpartyId == me || DealsService.IsIncomingOfferFor(d, me))
            .GroupBy(d => d.Id).Select(g => g.First())
            .OrderByDescending(d => d.CreatedAt)
            .Select(d =>
            {
                var initiator = d.InitiatorId == me;
                return new UserDataDeal(
                    d.Id, initiator ? UserDataDealSide.Initiator : UserDataDealSide.Counterparty,
                    d.Pattern, d.ListingId, d.ListingTitle, initiator ? d.CounterpartyName : d.InitiatorName,
                    d.AmountSar, d.Stage, d.Status, d.CancelReason,
                    // ما كَتَبَه هُوَ في الطَلَب يُسَلَّمُ لَه؛ وعُنوانُ مُشتَرٍ وهاتِفُه لا يُنسَخانِ لِبائِعِه.
                    initiator && d.Attributes.TryGetValue(Deal.DeliveryAddressAttribute, out var addr) ? addr : null,
                    initiator && d.Attributes.TryGetValue(Deal.DeliveryPhoneAttribute, out var phone) ? phone : null,
                    d.CreatedAt, d.UpdatedAt,
                    d.Timeline.Select(e => new UserDataDealStep(e.Action, e.StageBefore, e.StageAfter, e.ActorId == me, e.At)).ToList());
            }).ToList();

        var myConvs = conversations.Where(c => c.OwnerId == me || c.PartnerId == me)
            .GroupBy(c => c.Id).Select(g => g.First()).ToList();
        var byConv = messages.Where(m => myConvs.Any(c => c.Id == m.ConversationId))
            .GroupBy(m => m.ConversationId).ToDictionary(g => g.Key, g => g.OrderBy(m => m.SentAt).ToList());
        var convs = myConvs.OrderByDescending(c => c.CreatedAt)
            .Select(c => new UserDataConversation(
                c.Id, c.Subject, c.ListingId, c.OwnerId == me ? c.PartnerName : c.OwnerName, c.CreatedAt,
                (byConv.TryGetValue(c.Id, out var ms) ? ms : new List<Message>())
                    .Select(m => new UserDataMessage(m.SenderId == me, m.Body, m.SentAt)).ToList()))
            .ToList();

        var myReviews = reviews.Where(r => r.AuthorId == me || r.TargetUserId == me)
            .GroupBy(r => r.Id).Select(g => g.First())
            .OrderByDescending(r => r.At)
            .Select(r => r.AuthorId == me
                ? new UserDataReview(r.Id, UserDataReviewDirection.Written, r.TargetUserName, r.Rating, r.Body, r.DealId, r.VerifiedTransaction, r.Response, r.At)
                : new UserDataReview(r.Id, UserDataReviewDirection.Received, r.AuthorName, r.Rating, r.Body, r.DealId, r.VerifiedTransaction, r.Response, r.At))
            .ToList();

        var myOffers = offers.Where(o => o.OffererId == me)
            .GroupBy(o => o.Id).Select(g => g.First())
            .OrderByDescending(o => o.SubmittedAt)
            .Select(o => new UserDataOffer(o.Id, o.ListingId, o.Price, o.Message, o.Status, o.Lat, o.Lng, o.SubmittedAt, o.ExpiresAt, o.ResolvedAt))
            .ToList();

        var meId = me.ToString();
        var myListings = listings
            .Where(l => l.Attributes.TryGetValue(ACommerce.Templates.Customer.Marketplace.Services.Listings.ListingEditService.OwnerAttribute, out var oid) && oid == meId)
            .GroupBy(l => l.Id).Select(g => g.First())
            .OrderByDescending(l => l.CreatedAt)
            .Select(l => new UserDataListing(l.Id, l.Title, l.Description, l.Price, l.CategorySlug, l.City, l.District,
                new Dictionary<string, string>(l.Attributes), l.MediaUrls.ToList(), l.ViewCount, l.IsDeleted, l.CreatedAt, l.UpdatedAt))
            .ToList();

        return new UserDataExport(tenantSlug, now, account, myDeals, convs, myReviews, myOffers, myListings);
    }

    /// <summary>خِياراتُ الصيغَةِ الآليَّة: JSON مُنَسَّقٌ بِأَسماءِ camelCase، والقِيَمُ المُغلَقَةُ بِأَسمائِها لا بِأَرقامِها، والعَرَبِيَّةُ
    /// حُروفاً لا رُموزَ <c>\uXXXX</c> (مِلَفٌّ يَفتَحُه صاحِبُه أَيضاً) — ومِحرَفاتُ HTML تَبقى مُرَمَّزَة.</summary>
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static byte[] ToJsonBytes(UserDataExport export) => JsonSerializer.SerializeToUtf8Bytes(export, JsonOptions);

    /// <summary>اسمُ المِلَفِّ المُنَزَّل: المَتجَرُ ويَومُ الإنشاءِ بِمِنطَقَةِ الثَقافَة (<c>CultureTime.FormatTime</c>، لا يَومُ UTC) — لا اسمُ الشَخصِ
    /// ولا هاتِفُه (اسمُ مِلَفٍّ يَبقى في سِجِلِّ التَنزيلات).</summary>
    public static string FileName(string tenantSlug, DateTime at, string? timeZoneId = null)
        => $"my-data-{tenantSlug}-{ACommerce.Kit.Culture.CultureTime.FormatTime(at, "yyyy-MM-dd", timeZoneId, System.Globalization.CultureInfo.InvariantCulture)}.json";
}

/// <summary>
/// <para><b>الاستِعلامُ الواحِدُ لِـ«بَياناتي»</b> — جَلسَةُ قِراءَةٍ واحِدَةٌ بِمُستَأجِرِ المَسار (<c>QuerySession(tenantSlug)</c>، والوَثائِقُ
/// كُلُّها تَحتَ <c>AllDocumentsAreMultiTenanted</c>)، ثُمَّ <see cref="UserDataExport.Project"/>. تَقرَؤُه الصَفحَةُ والتَنزيلُ مَعاً.
/// <b>تَقرَأُ ولا تَكتُب</b> — شَرطُ مُجَلَّدِها.</para>
/// <para><b>ولا سَقفَ عَدَد</b> (لا <c>Take</c>) بِخِلافِ قَوائِمِ الشاشات: حَقُّ الاطِّلاعِ عَلى البَياناتِ كُلِّها لا عَلى أَحدَثِها.</para>
/// </summary>
public sealed class UserDataQueries
{
    private readonly IDocumentStore _store;

    public UserDataQueries(IDocumentStore store) => _store = store;

    public async Task<UserDataExport?> ExportAsync(
        string tenantSlug, Guid userId, DateTime now, CancellationToken ct = default)
    {
        await using var s = _store.QuerySession(tenantSlug);
        var user = await s.LoadAsync<User>(userId, ct);
        if (user is null || user.DeletedAt is not null) return null;

        // الشَطرانِ كَما في «صَفقاتي»: طَرَفٌ فيها، أَو عَرضٌ عَلى إعلانِه لَم يُقبَل بَعد (يُرَشِّحُه الإسقاط).
        var deals = (await s.Query<Deal>().Where(d => d.InitiatorId == userId || d.CounterpartyId == userId).ToListAsync(ct))
            .Concat(await s.Query<Deal>().Where(d => d.CounterpartyId == null && d.InitiatorId != userId).ToListAsync(ct))
            .ToList();
        var convs = (await s.Query<Conversation>().Where(c => c.OwnerId == userId || c.PartnerId == userId).ToListAsync(ct)).ToList();
        // ‏List لا مَصفوفَة: ‏Marten يَدعَمُ List<T>.Contains في WHERE (نَسَقُ `AccountQueries.ConversationsAsync`).
        var convIds = convs.Select(c => c.Id).ToList();
        var messages = convIds.Count == 0
            ? new List<Message>()
            : (await s.Query<Message>().Where(m => convIds.Contains(m.ConversationId)).ToListAsync(ct)).ToList();
        var reviews = await s.Query<Review>().Where(r => r.AuthorId == userId || r.TargetUserId == userId).ToListAsync(ct);
        var offers = await s.Query<Offer>().Where(o => o.OffererId == userId).ToListAsync(ct);
        // ‏Marten لا يُفَلتِرُ `Attributes` في LINQ — نَفسُ سَبَبِ `AccountQueries.MyListingsAsync`؛ والمالِكُ يُرَشَّحُ في الإسقاط.
        var listings = await s.Query<Listing>().ToListAsync(ct);

        return UserDataExport.Project(tenantSlug, now, user, deals, convs, messages, reviews, offers, listings);
    }
}
