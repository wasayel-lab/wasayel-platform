using ACommerce.Kit.Auth;
using ACommerce.Kit.Chat;
using ACommerce.Kit.Favorites;
using ACommerce.Kit.Listings;
using ACommerce.Kit.Notifications;
using ACommerce.Kit.Tenants;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace ACommerce.Importer;

/// <summary>
/// Ashare V3 → platform-v1.
///
/// مَخطَّط المَصدَر (راجِع Apps/Ashare.V3/Domain/AshareEntities.cs و
/// Apps/Ashare.V3/Domain.Data/Data/AshareV3DbContext.cs):
///   ProfileEntity         → table "Profile"
///   ProductEntity         → table "Products"
///   ProductCategoryEntity → table "ProductCategory"
///   ProductListingEntity  → table "ProductListing"
///   ChatEntity            → table "Chat"
///   MessageEntity         → table "Message"  (Content, SenderId: string)
///   FavoriteEntity        → table "Favorites" (UserId: string)
///   NotificationEntity    → table "Notifications" (UserId: string)
///   DiscoveryCategory     → table "DiscoveryCategories" (Slug, Label, Icon, Kind)
///
/// UserId في Ashare V3 هو ASP.NET Identity string (AspNetUsers.Id).
/// نُحَوِّله إلى Profile.Id (Guid) عَبر join — هذا ما يَفهَمه
/// platform-v1 (UserId مِن نَوع Guid).
/// </summary>
public sealed class AshareImporter
{
    public const string TenantSlug = "ashare";

    private readonly TargetWriter _target;
    private readonly ILogger<AshareImporter> _log;

    public AshareImporter(TargetWriter target, ILogger<AshareImporter> log)
    {
        _target = target;
        _log = log;
    }

    public async Task RunAsync(string sourceCs, bool reset)
    {
        await using var src = new SqlConnection(sourceCs);
        await src.OpenAsync();
        _log.LogInformation("  ✓ source SQL Server connected ({Db}).", src.Database);

        if (reset) await _target.ResetTenantAsync(TenantSlug);

        // 1) Tenant.Categories: عَشير V3 يَحوي فِئَتَين فَقَط (roommate_has,
        // roommate_wants) في الواجِهَة — كُلّ ما عَداهُما تَفاصيل
        // تَصنيف داخِليَّة لا تَهُمّ المُستَخدِم. نَجلِبهما بِالاسم بِغَضّ
        // النَّظَر عَمّا في DB. الـ Id هو نَفس الـ Guid الثابِت الَّذي
        // تَستَخدِمه CategoryAttributeMappings.
        await _target.UpsertTenantAsync(new Tenant
        {
            Id          = TenantSlug,
            Name        = "عَشير",
            BrandColor  = "#345454",  // Deep Olive Green (Ashare V3 رَسمي)
            City        = "إب",
            TagLine     = "السَكَن المُشتَرَك بأَريَحيّة",
            AuthChannel = "nafath",
            Categories  = new List<Category>
            {
                new() { Slug = "roommate_has",   Label = "عشير عنده سكن",
                        Icon = "🏠", Kind = "roommate", SortOrder = 1 },
                new() { Slug = "roommate_wants", Label = "عشير يدور سكن",
                        Icon = "🔎", Kind = "roommate", SortOrder = 2 }
            }
        });

        // 2) Users — مِن Profile (FullName, Phone مَوجودان مُباشَرَةً).
        var users = (await src.QueryAsync<AshareProfileRow>(
            @"SELECT Id, FullName, Phone, NationalId, CreatedAt
              FROM Profile WHERE IsDeleted = 0"
        )).Select(p => new User
        {
            Id         = p.Id,
            TenantSlug = TenantSlug,
            FullName   = string.IsNullOrWhiteSpace(p.FullName) ? "مُستَخدِم نَفاذ" : p.FullName!,
            Phone      = p.Phone ?? "",
            NationalId = p.NationalId,
            CreatedAt  = p.CreatedAt
        }).ToList();
        await _target.UpsertAsync(TenantSlug, users);

        // خَريطَة UserId (string) → Profile.Id (Guid). نَحتاجها لِكُلّ ما
        // يُخَزِّن UserId كَ AspNetUsers.Id (Favorites, Notifications, …).
        var userIdMap = (await src.QueryAsync<AshareUserIdMapRow>(
            "SELECT UserId, Id FROM Profile WHERE IsDeleted = 0 AND UserId IS NOT NULL"
        )).ToDictionary(r => r.UserId!, r => r.Id);
        Guid? ResolveProfile(string? userId)
            => userId is not null && userIdMap.TryGetValue(userId, out var g) ? g : null;

        // 3) Listings — ProductListing + Product لِلوَصف.
        //
        // CategoryId يُحَدِّد أَيّ فِئَة (Guids ثابِتَة في
        // AshareV3RoommateAttributes). إن لم يُطابِق أَحَدَهما نَحفَظ
        // CategorySlug فارِغاً.
        //
        // قَيد IsActive=1 أُسقِط. كَذلك الـ INNER JOIN مَع Products
        // أَصبَح LEFT JOIN — لَو الإعلان يَتيم بِلا Product (بَيانات
        // مُهَجَّرَة جُزئيّاً) لا نَفقِده. كَذلك دُمنا نُلَخِّص:
        //   - عَدَد الصُفوف قَبل أَيّ فَلتَر
        //   - عَدَد المَحذوفَة
        //   - عَدَد ما يَتَبَقّى لِلكِتابَة
        var roomHas    = Guid.Parse("0a01a01a-0a01-0a01-0a01-0a01000a01a2");
        var roomWants  = Guid.Parse("0a01a01a-0a01-0a01-0a01-0a01000a01a3");

        var allRows = (await src.QueryAsync<AshareListingRow>(
            @"SELECT pl.Id, pl.Title,
                     COALESCE(NULLIF(pl.Description, ''), p.LongDescription, p.ShortDescription, '') AS Description,
                     pl.Price, pl.CategoryId,
                     pl.City, pl.AttributesJson, pl.AmenitiesJson,
                     pl.BedroomCount, pl.BathroomCount, pl.AreaSqm, pl.TimeUnit,
                     pl.IsDeleted, pl.CreatedAt, pl.UpdatedAt
              FROM ProductListing pl
              LEFT JOIN Products p ON p.Id = pl.ProductId"
        )).ToList();
        var deletedCount = allRows.Count(l => l.IsDeleted);
        var aliveRows = allRows.Where(l => !l.IsDeleted).ToList();
        _log.LogInformation("  ⓘ ProductListing: total {Total} (deleted {Del}, alive {Alive}).",
            allRows.Count, deletedCount, aliveRows.Count);

        var listings = aliveRows.Select(l =>
        {
            // AttributesJson سِمَة snapshot — نُحَلِّله إلى Dictionary، ثُمّ
            // نَدمُج الحُقول البِنيَويَّة (Bedrooms/Bathrooms/Area/TimeUnit/
            // Amenities) لِيَستَخدِمها AcDynAttrEditor عَلى صَفحَة التَفاصيل.
            var attrs = ParseAttributes(l.AttributesJson);
            if (l.BedroomCount  > 0) attrs["BedroomCount"]  = l.BedroomCount.ToString();
            if (l.BathroomCount > 0) attrs["BathroomCount"] = l.BathroomCount.ToString();
            if (l.AreaSqm       > 0) attrs["AreaSqm"]       = l.AreaSqm.ToString();
            if (!string.IsNullOrEmpty(l.TimeUnit))     attrs["TimeUnit"]  = l.TimeUnit;
            if (!string.IsNullOrEmpty(l.AmenitiesJson)) attrs["Amenities"] = l.AmenitiesJson;

            return new Listing
            {
                Id           = l.Id,
                TenantSlug   = TenantSlug,
                Title        = l.Title ?? "",
                Description  = string.IsNullOrEmpty(l.Description) ? null : l.Description,
                Price        = l.Price,
                CategorySlug = l.CategoryId == roomHas   ? "roommate_has"
                             : l.CategoryId == roomWants ? "roommate_wants"
                             : "",
                City         = string.IsNullOrEmpty(l.City) ? null : l.City,
                District     = null,
                Attributes   = attrs,
                IsDeleted    = false,
                CreatedAt    = l.CreatedAt,
                UpdatedAt    = l.UpdatedAt ?? l.CreatedAt
            };
        }).ToList();
        await _target.UpsertListingsAsync(TenantSlug, listings);

        // 4) Favorites — UserId سِترِنغ يَحتاج خَريطَة.
        var favRows = (await src.QueryAsync<AshareFavoriteRow>(
            "SELECT Id, UserId, ListingId, CreatedAt FROM Favorites WHERE IsDeleted = 0"
        )).ToList();
        var favs = favRows
            .Select(f => (Resolved: ResolveProfile(f.UserId), f.ListingId, f.CreatedAt))
            .Where(t => t.Resolved.HasValue)
            .Select(t => new Favorite
            {
                Id        = Favorite.MakeId(t.Resolved!.Value, t.ListingId),
                UserId    = t.Resolved.Value,
                ListingId = t.ListingId,
                At        = t.CreatedAt
            }).ToList();
        await _target.UpsertAsync(TenantSlug, favs);
        if (favRows.Count > favs.Count)
            _log.LogWarning("  ⚠ Favorites: {Skipped}/{Total} skipped (UserId غَير مَوجود في Profile).",
                favRows.Count - favs.Count, favRows.Count);

        // 5) Chats → Conversations. عَشير يَستَخدِم ChatParticipant M:N،
        // نَطوي إلى Owner+Partner (نَموذَج 1:1 المُتَوَقَّع في platform-v1).
        // chats مَع >2 مُشارِكين تُحَوَّل إلى أَوّل اثنَين.
        var convRows = (await src.QueryAsync<AshareChatRow>(
            @"WITH P AS (
                SELECT ChatId,
                       MAX(CASE WHEN rn = 1 THEN UserId END) AS OwnerUserId,
                       MAX(CASE WHEN rn = 2 THEN UserId END) AS PartnerUserId
                FROM (
                    SELECT ChatId, UserId,
                           ROW_NUMBER() OVER (PARTITION BY ChatId ORDER BY CreatedAt, Id) AS rn
                    FROM ChatParticipant WHERE IsDeleted = 0
                ) t
                GROUP BY ChatId
              )
              SELECT c.Id, P.OwnerUserId, P.PartnerUserId, c.Title AS Subject,
                     COALESCE(c.UpdatedAt, c.CreatedAt) AS LastAt,
                     c.CreatedAt
              FROM Chat c JOIN P ON P.ChatId = c.Id
              WHERE c.IsDeleted = 0 AND P.OwnerUserId IS NOT NULL AND P.PartnerUserId IS NOT NULL"
        )).ToList();
        var convs = convRows
            .Select(c => (
                c.Id,
                Owner:   ResolveProfile(c.OwnerUserId),
                Partner: ResolveProfile(c.PartnerUserId),
                c.Subject, c.LastAt, c.CreatedAt))
            .Where(t => t.Owner.HasValue && t.Partner.HasValue)
            .Select(t => new Conversation
            {
                Id          = t.Id,
                OwnerId     = t.Owner!.Value,
                OwnerName   = "",
                PartnerId   = t.Partner!.Value,
                PartnerName = "",
                ListingId   = null,
                Subject     = t.Subject,
                LastAt      = t.LastAt,
                CreatedAt   = t.CreatedAt
            }).ToList();
        await _target.UpsertAsync(TenantSlug, convs);

        // 6) Messages — Content بَدَل Body؛ SenderId سِترِنغ يَحتاج
        // خَريطَة؛ SentAt = CreatedAt في عَشير V3.
        var msgRows = (await src.QueryAsync<AshareMsgRow>(
            @"SELECT Id, ChatId AS ConversationId, SenderId, Content AS Body, CreatedAt AS SentAt
              FROM Message WHERE IsDeleted = 0"
        )).ToList();
        var msgs = msgRows
            .Select(m => (m.Id, m.ConversationId, Resolved: ResolveProfile(m.SenderId), m.Body, m.SentAt))
            .Where(t => t.Resolved.HasValue)
            .Select(t => new Message
            {
                Id             = t.Id,
                ConversationId = t.ConversationId,
                SenderId       = t.Resolved!.Value,
                Body           = t.Body ?? "",
                SentAt         = t.SentAt
            }).ToList();
        await _target.UpsertAsync(TenantSlug, msgs);

        // 7) Notifications — UserId سِترِنغ، Kind بَدَل Type، لا RelatedUrl.
        var notifRows = (await src.QueryAsync<AshareNotifRow>(
            @"SELECT Id, UserId, Title, Body, IsRead, CreatedAt AS [At]
              FROM Notifications WHERE IsDeleted = 0"
        )).ToList();
        var notifs = notifRows
            .Select(n => (n.Id, Resolved: ResolveProfile(n.UserId), n.Title, n.Body, n.IsRead, n.At))
            .Where(t => t.Resolved.HasValue)
            .Select(t => new Notification
            {
                Id         = t.Id,
                UserId     = t.Resolved!.Value,
                Title      = t.Title ?? "",
                Body       = t.Body ?? "",
                RelatedUrl = null,
                IsRead     = t.IsRead,
                At         = t.At
            }).ToList();
        await _target.UpsertAsync(TenantSlug, notifs);

        // 8) Complaints → Tickets. عَشير يَستَخدِم Complaint + ComplaintReply
        // كَنِظام دَعمه؛ shapes قَريبَة جِدّاً مِن Ticket.
        var compRows = (await src.QueryAsync<AshareComplaintRow>(
            @"SELECT c.Id, c.UserId, p.FullName AS AuthorName,
                     c.Title AS Subject, c.Description AS Body, c.CreatedAt
              FROM Complaint c
              LEFT JOIN Profile p ON p.UserId = c.UserId
              WHERE c.IsDeleted = 0"
        )).ToList();
        var tickets = compRows
            .Select(c => (c.Id, Author: ResolveProfile(c.UserId), c.AuthorName, c.Subject, c.Body, c.CreatedAt))
            .Where(t => t.Author.HasValue)
            .Select(t => new TicketImport(
                t.Id, t.Author!.Value, t.AuthorName ?? "—",
                t.Subject ?? "", t.Body ?? "", t.CreatedAt))
            .ToList();
        await _target.UpsertTicketsAsync(TenantSlug, tickets);

        // 9) كُلّ الجَداوِل الباقِيَة — نُلتَقِطها كَ ImportedRecord (JSON كامِل
        // لِكُلّ صَفّ) لِكَي لا تَضيع. الـ Id يَصير "{table}/{sourceId}".
        // قائِمَة كامِلَة مِن AshareV3DbContext.cs:
        string[] genericTables =
        {
            "Products", "ProductCategory", "ProductCategoryMapping",
            "ChatParticipant", "MessageRead",
            "ComplaintReply", "Booking", "BookingStatusHistory",
            "DeviceTokens", "AppVersions", "LegalPage",
            "Reports", "DiscoveryCategories", "DiscoveryRegions", "DiscoveryAmenities",
            "CategoryAttributeTemplates", "AttributeDefinitions", "AttributeValues",
            "AttributeValueRelationships", "CategoryAttributeMappings", "CrossAttributeConstraint",
            "Countries", "Regions", "Cities", "Neighborhoods",
            "DocumentType", "DocumentTypeAttribute", "DocumentTypeRelation", "DocumentOperation",
            "Vendor", "ProductBrand", "ProductBrandMapping", "ProductInventory",
            "ProductPrices", "ProductRelation", "ProductReview", "ProductAttributes",
            "Cart", "CartItem", "Order", "OrderItem", "Review",
            "Currencies", "ExchangeRates", "Language", "Translation",
            "MeasurementSystems", "Units", "UnitCategories", "UnitConversions",
            "SubscriptionPlans", "Subscriptions", "SubscriptionInvoices", "SubscriptionEvents",
            "OperationNotification", "RecipientGroup", "RecipientGroupUserRecipient", "UserRecipient",
            "Addresses", "ContactPoint"
        };
        foreach (var t in genericTables)
            await _target.DumpGenericAsync(src, TenantSlug, t);
    }

    // ──── Row types — مُطابِقَة لِأَعمِدَة SELECT أَعلاه ───────────────
    private sealed record AshareProfileRow(Guid Id, string? FullName, string? Phone, string? NationalId, DateTime CreatedAt);
    private sealed record AshareUserIdMapRow(string? UserId, Guid Id);
    private sealed record AshareListingRow(
        Guid Id, string? Title, string? Description, decimal Price,
        Guid? CategoryId, string? City,
        string? AttributesJson, string? AmenitiesJson,
        int BedroomCount, int BathroomCount, int AreaSqm, string? TimeUnit,
        bool IsDeleted, DateTime CreatedAt, DateTime? UpdatedAt);

    /// <summary>
    /// يُحَلِّل JSON snapshot لِسِمات الإعلان (object{key:value}) إلى
    /// <c>Dictionary&lt;string,string&gt;</c>. الأَرقام والـ booleans
    /// تَتَحَوَّل إلى نُصوصها (مَثَلاً "3" أو "true") لِأنّ Listing.Attributes
    /// نَصّيّ بِالكامِل. أَيّ خَلَل في JSON ⇒ Dictionary فارِغ.
    /// </summary>
    internal static Dictionary<string, string> ParseAttributes(string? json)
    {
        var result = new Dictionary<string, string>();
        if (string.IsNullOrWhiteSpace(json)) return result;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object) return result;
            foreach (var p in doc.RootElement.EnumerateObject())
            {
                var s = p.Value.ValueKind switch
                {
                    System.Text.Json.JsonValueKind.String => p.Value.GetString() ?? "",
                    System.Text.Json.JsonValueKind.Null   => "",
                    System.Text.Json.JsonValueKind.True   => "true",
                    System.Text.Json.JsonValueKind.False  => "false",
                    System.Text.Json.JsonValueKind.Number => p.Value.GetRawText(),
                    _                                     => p.Value.GetRawText()
                };
                if (!string.IsNullOrEmpty(s)) result[p.Name] = s;
            }
        }
        catch { /* invalid JSON — نَترُك dict فارِغ */ }
        return result;
    }
    private sealed record AshareFavoriteRow(Guid Id, string? UserId, Guid ListingId, DateTime CreatedAt);
    private sealed record AshareChatRow(Guid Id, string? OwnerUserId, string? PartnerUserId,
                                         string? Subject, DateTime LastAt, DateTime CreatedAt);
    private sealed record AshareMsgRow(Guid Id, Guid ConversationId, string? SenderId, string? Body, DateTime SentAt);
    private sealed record AshareNotifRow(Guid Id, string? UserId, string? Title, string? Body, bool IsRead, DateTime At);
    private sealed record AshareComplaintRow(Guid Id, string? UserId, string? AuthorName, string? Subject, string? Body, DateTime CreatedAt);
}
