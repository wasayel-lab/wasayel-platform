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
/// مَخطَّط المَصدَر يَختَلِف عَن إيجار:
///   Profiles, Products, ProductCategories, ProductListings,
///   Chats, Messages, Bookings, Favorites, Notifications، …
///
/// Mapping:
///   Profile        → User
///   ProductListing → Listing (نَأخُذ السِعر والـ Title؛ ProductId يَربِط
///                              بِـ Product لِلوَصف والـ Image)
///   ProductCategory → Tenant.Categories
///   Chat + Messages → Conversation + Message (نَطوي ChatParticipant)
///   Favorite       → Favorite
///   Notification   → Notification
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

        // 1) Categories — جَدول مُتَّسِع، نَختار TOP 50 الأَكثَر ظُهوراً.
        var categories = (await src.QueryAsync<AshareCategoryRow>(
            @"SELECT TOP 50 Id, Slug, Name, Icon FROM ProductCategories
              WHERE IsDeleted = 0 AND IsActive = 1
              ORDER BY SortOrder, Name"
        )).ToList();

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
                Label = c.Name,
                Icon  = string.IsNullOrEmpty(c.Icon) ? "🛏️" : c.Icon
            }).ToList()
        });

        // 2) Users — مِن Profile + AspNetUsers (نَكتَفي بِالأَسماء والهاتِف).
        var users = (await src.QueryAsync<AshareUserRow>(
            @"SELECT p.Id, p.FirstName, p.LastName, u.PhoneNumber AS Phone, u.Email,
                     p.NationalId, p.CreatedAt
              FROM Profiles p
              LEFT JOIN AspNetUsers u ON u.Id = p.UserId
              WHERE p.IsDeleted = 0"
        )).Select(u => new User
        {
            Id         = u.Id,
            TenantSlug = TenantSlug,
            FullName   = string.IsNullOrWhiteSpace($"{u.FirstName} {u.LastName}".Trim())
                            ? "مُستَخدِم نَفاذ"
                            : $"{u.FirstName} {u.LastName}".Trim(),
            Phone      = u.Phone ?? "",
            NationalId = u.NationalId,
            CreatedAt  = u.CreatedAt
        }).ToList();
        await _target.UpsertAsync(TenantSlug, users);

        // 3) Listings — ProductListing مُدمَج مَع Product لِلوَصف.
        // الـ Category في Ashare V3 يَأتي مِن ProductCategoryMapping (M:N)؛
        // نَأخُذ الفِئَة الأَولى لِكُلّ إعلان (الأَكثَر شُيوعاً).
        var listings = (await src.QueryAsync<AshareListingRow>(
            @"SELECT pl.Id, pl.Title, COALESCE(NULLIF(pl.Description, ''), p.LongDescription, p.ShortDescription) AS Description,
                     pl.Price,
                     (SELECT TOP 1 pc.Slug FROM ProductCategoryMapping pcm
                      JOIN ProductCategory pc ON pc.Id = pcm.CategoryId
                      WHERE pcm.ProductId = pl.ProductId AND pc.IsDeleted = 0) AS CategorySlug,
                     pl.IsDeleted, pl.CreatedAt, pl.UpdatedAt
              FROM ProductListings pl
              JOIN Products p ON p.Id = pl.ProductId
              WHERE pl.IsDeleted = 0 AND pl.IsActive = 1"
        )).Select(l => new Listing
        {
            Id           = l.Id,
            TenantSlug   = TenantSlug,
            Title        = l.Title ?? "",
            Description  = l.Description,
            Price        = l.Price,
            CategorySlug = l.CategorySlug ?? "",
            City         = null,  // عَشير V3 يَستَخدِم Address مُنفَصِل — يَحتاج JOIN ثاني
            District     = null,
            Attributes   = new(),
            IsDeleted    = false,
            CreatedAt    = l.CreatedAt,
            UpdatedAt    = l.UpdatedAt ?? l.CreatedAt
        }).ToList();
        await _target.UpsertAsync(TenantSlug, listings);

        // 4) Favorites.
        var favs = (await src.QueryAsync<AshareFavoriteRow>(
            "SELECT Id, UserId, ProductListingId AS ListingId, CreatedAt FROM Favorites WHERE IsDeleted = 0"
        )).Select(f => new Favorite
        {
            Id        = Favorite.MakeId(f.UserId, f.ListingId),
            UserId    = f.UserId,
            ListingId = f.ListingId,
            At        = f.CreatedAt
        }).ToList();
        await _target.UpsertAsync(TenantSlug, favs);

        // 5) Chats → Conversations. عَشير يَستَخدِم ChatParticipant M:N،
        // نَخفِض إلى نَموذَج 1:1 (Owner + Partner) بِأَخذ أَوّل
        // مُشارِكَين فَقَط — الأَدبيّ لِـ Ashare V3 chat هُوَ 1:1.
        var convs = (await src.QueryAsync<AshareChatRow>(
            @"WITH P AS (
                SELECT ChatId,
                       MIN(CASE WHEN rn = 1 THEN UserId END) AS OwnerId,
                       MIN(CASE WHEN rn = 2 THEN UserId END) AS PartnerId,
                       MIN(CASE WHEN rn = 1 THEN UserId END) AS A,
                       MIN(CASE WHEN rn = 2 THEN UserId END) AS B
                FROM (
                    SELECT ChatId, UserId,
                           ROW_NUMBER() OVER (PARTITION BY ChatId ORDER BY JoinedAt) AS rn
                    FROM ChatParticipants WHERE IsDeleted = 0
                ) t
                GROUP BY ChatId
              )
              SELECT c.Id, P.OwnerId, P.PartnerId, c.Subject,
                     COALESCE(c.UpdatedAt, c.CreatedAt) AS LastAt,
                     c.CreatedAt
              FROM Chats c JOIN P ON P.ChatId = c.Id
              WHERE c.IsDeleted = 0 AND P.OwnerId IS NOT NULL AND P.PartnerId IS NOT NULL"
        )).Select(c => new Conversation
        {
            Id          = c.Id,
            OwnerId     = c.OwnerId,
            OwnerName   = "",
            PartnerId   = c.PartnerId,
            PartnerName = "",
            ListingId   = null,
            Subject     = c.Subject,
            LastAt      = c.LastAt,
            CreatedAt   = c.CreatedAt
        }).ToList();
        await _target.UpsertAsync(TenantSlug, convs);

        var msgs = (await src.QueryAsync<AshareMsgRow>(
            @"SELECT Id, ChatId AS ConversationId, SenderId, Body, SentAt
              FROM Messages WHERE IsDeleted = 0"
        )).Select(m => new Message
        {
            Id             = m.Id,
            ConversationId = m.ConversationId,
            SenderId       = m.SenderId,
            Body           = m.Body ?? "",
            SentAt         = m.SentAt
        }).ToList();
        await _target.UpsertAsync(TenantSlug, msgs);

        // 6) Notifications.
        var notifs = (await src.QueryAsync<AshareNotifRow>(
            @"SELECT Id, UserId, Title, Body, RelatedUrl, IsRead, CreatedAt AS [At]
              FROM Notifications WHERE IsDeleted = 0"
        )).Select(n => new Notification
        {
            Id         = n.Id,
            UserId     = n.UserId,
            Title      = n.Title ?? "",
            Body       = n.Body ?? "",
            RelatedUrl = n.RelatedUrl,
            IsRead     = n.IsRead,
            At         = n.At
        }).ToList();
        await _target.UpsertAsync(TenantSlug, notifs);
    }

    private sealed record AshareCategoryRow(Guid Id, string Slug, string Name, string? Icon);
    private sealed record AshareUserRow(Guid Id, string? FirstName, string? LastName, string? Phone,
                                         string? Email, string? NationalId, DateTime CreatedAt);
    private sealed record AshareListingRow(Guid Id, string? Title, string? Description, decimal Price,
                                            string? CategorySlug, bool IsDeleted,
                                            DateTime CreatedAt, DateTime? UpdatedAt);
    private sealed record AshareFavoriteRow(Guid Id, Guid UserId, Guid ListingId, DateTime CreatedAt);
    private sealed record AshareChatRow(Guid Id, Guid OwnerId, Guid PartnerId, string? Subject,
                                         DateTime LastAt, DateTime CreatedAt);
    private sealed record AshareMsgRow(Guid Id, Guid ConversationId, Guid SenderId, string? Body, DateTime SentAt);
    private sealed record AshareNotifRow(Guid Id, Guid UserId, string? Title, string? Body, string? RelatedUrl, bool IsRead, DateTime At);
}
