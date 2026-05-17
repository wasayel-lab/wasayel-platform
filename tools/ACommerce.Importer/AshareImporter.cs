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

        // 1) Tenant.Categories: نَجَرِّب DiscoveryCategories أَوَّلاً (الفِئات
        // الَّتي تَظهَر في الواجِهَة). إذا كانَت فارِغَة في DB الإنتاج،
        // نَسقُط لِـ ProductCategory (الـ taxonomy الداخِليّ — حَيث يَسكُن
        // الـ catalog الفِعليّ).
        var categories = (await src.QueryAsync<DiscoveryCatRow>(
            @"SELECT TOP 50 Id, Slug, Label, Icon FROM DiscoveryCategories
              WHERE IsDeleted = 0 ORDER BY Label"
        )).ToList();
        if (categories.Count == 0)
        {
            _log.LogInformation("  ⓘ DiscoveryCategories فارِغ — أَسقُط لِـ ProductCategory.");
            categories = (await src.QueryAsync<DiscoveryCatRow>(
                @"SELECT TOP 50 Id, Slug, Name AS Label, Icon FROM ProductCategory
                  WHERE IsDeleted = 0 AND IsActive = 1 ORDER BY SortOrder, Name"
            )).ToList();
        }

        await _target.UpsertTenantAsync(new Tenant
        {
            Id          = TenantSlug,
            Name        = "عَشير",
            BrandColor  = "#345454",
            City        = "إب",
            TagLine     = "السَكَن المُشتَرَك بأَريَحيّة",
            AuthChannel = "nafath",
            Categories  = categories.Select(c => new Category
            {
                Slug  = c.Slug,
                Label = c.Label,
                Icon  = string.IsNullOrEmpty(c.Icon) ? "🛏️" : c.Icon
            }).ToList()
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

        // 3) Listings — ProductListing + Product لِلوَصف. الـ Category في V3
        // مُنفَصِل عَبر ProductCategoryMapping. حاليّاً نَترُك CategorySlug
        // فارِغ لأَنّ بَيانات production قَد لا تَملُك mapping كامِل؛
        // يُمكِن تَحسينه لاحِقاً بِالتَوصيل مَع DiscoveryCategories عَن طَريق
        // attribute "category" أو slug في Product.Type.
        var listings = (await src.QueryAsync<AshareListingRow>(
            @"SELECT pl.Id, pl.Title,
                     COALESCE(NULLIF(pl.Description, ''), p.LongDescription, p.ShortDescription, '') AS Description,
                     pl.Price, p.Type AS PType,
                     pl.IsDeleted, pl.CreatedAt, pl.UpdatedAt
              FROM ProductListing pl
              JOIN Products p ON p.Id = pl.ProductId
              WHERE pl.IsDeleted = 0 AND pl.IsActive = 1"
        )).Select(l => new Listing
        {
            Id           = l.Id,
            TenantSlug   = TenantSlug,
            Title        = l.Title ?? "",
            Description  = string.IsNullOrEmpty(l.Description) ? null : l.Description,
            Price        = l.Price,
            CategorySlug = l.PType ?? "",   // Product.Type يَحوي slug الفِئَة في V3
            City         = null,
            District     = null,
            Attributes   = new(),
            IsDeleted    = false,
            CreatedAt    = l.CreatedAt,
            UpdatedAt    = l.UpdatedAt ?? l.CreatedAt
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
    }

    // ──── Row types — مُطابِقَة لِأَعمِدَة SELECT أَعلاه ───────────────
    private sealed record DiscoveryCatRow(Guid Id, string Slug, string Label, string? Icon);
    private sealed record AshareProfileRow(Guid Id, string? FullName, string? Phone, string? NationalId, DateTime CreatedAt);
    private sealed record AshareUserIdMapRow(string? UserId, Guid Id);
    private sealed record AshareListingRow(Guid Id, string? Title, string? Description, decimal Price,
                                            string? PType, bool IsDeleted, DateTime CreatedAt, DateTime? UpdatedAt);
    private sealed record AshareFavoriteRow(Guid Id, string? UserId, Guid ListingId, DateTime CreatedAt);
    private sealed record AshareChatRow(Guid Id, string? OwnerUserId, string? PartnerUserId,
                                         string? Subject, DateTime LastAt, DateTime CreatedAt);
    private sealed record AshareMsgRow(Guid Id, Guid ConversationId, string? SenderId, string? Body, DateTime SentAt);
    private sealed record AshareNotifRow(Guid Id, string? UserId, string? Title, string? Body, bool IsRead, DateTime At);
}
