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
/// Ejar V1 → platform-v1.
///
/// مَخطَّط المَصدَر (مُحَدَّد في Apps/Ejar/Domain.Data/Data/Entities.cs):
///   Users, Listings, Conversations, Messages, Favorites,
///   Notifications, Plans, Subscriptions, SupportTickets,
///   DiscoveryCategories, DiscoveryRegions, AttributeDefinitions، …
///
/// مَخطَّط الهَدَف (platform-v1):
///   Tenant(slug=ejar) + Listing + User + Conversation + Message +
///   Favorite + Notification — كُلّها conjoined عَلى tenant_id.
///
/// idempotent: نَستَخدِم Id الأَصلي مِن المَصدَر كَ Marten Id.
/// </summary>
public sealed class EjarImporter
{
    public const string TenantSlug = "ejar";

    private readonly TargetWriter _target;
    private readonly ILogger<EjarImporter> _log;

    public EjarImporter(TargetWriter target, ILogger<EjarImporter> log)
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

        // 1) Tenant document — ميتاداتا المَتجَر (الـ slug ثابِت لِأَنّ Ejar
        // V1 تَطبيق وَحيد بِلا مُتَعَدّد مُستَأجِرين داخِليّ).
        var categories = (await src.QueryAsync<EjarCategoryRow>(
            "SELECT TOP 50 Id, Slug, Label, Icon FROM DiscoveryCategories WHERE IsDeleted = 0 ORDER BY SortOrder, Label"
        )).ToList();

        await _target.UpsertTenantAsync(new Tenant
        {
            Id          = TenantSlug,
            Name        = "إيجار",
            BrandColor  = "#C2410C",
            City        = "إب",
            TagLine     = "كلّ ما يُؤَجَّر في مَدينَتك",
            AuthChannel = "phone",
            Categories  = categories.Select(c => new Category
            {
                Slug  = c.Slug,
                Label = c.Label,
                Icon  = string.IsNullOrEmpty(c.Icon) ? "🏠" : c.Icon
            }).ToList()
        });

        // 2) Users — قائِمَة المُسَجَّلين.
        var users = (await src.QueryAsync<EjarUserRow>(
            @"SELECT Id, FullName, Phone, Email, NationalId, IsDeleted, CreatedAt
              FROM Users WHERE IsDeleted = 0"
        )).Select(u => new User
        {
            Id           = u.Id,
            TenantSlug   = TenantSlug,
            FullName     = u.FullName ?? "مُستَخدِم جَديد",
            Phone        = u.Phone ?? "",
            NationalId   = u.NationalId,
            CreatedAt    = u.CreatedAt
        }).ToList();
        await _target.UpsertAsync(TenantSlug, users);

        // 3) Listings — الإعلانات.
        var listings = (await src.QueryAsync<EjarListingRow>(
            @"SELECT Id, Title, Description, Price, PropertyType, City, District,
                     IsDeleted, CreatedAt, UpdatedAt
              FROM Listings WHERE IsDeleted = 0"
        )).Select(l => new Listing
        {
            Id           = l.Id,
            TenantSlug   = TenantSlug,
            Title        = l.Title ?? "",
            Description  = l.Description,
            Price        = l.Price,
            CategorySlug = l.PropertyType ?? "",
            City         = string.IsNullOrEmpty(l.City) ? null : l.City,
            District     = string.IsNullOrEmpty(l.District) ? null : l.District,
            Attributes   = new(),
            IsDeleted    = false,
            CreatedAt    = l.CreatedAt,
            UpdatedAt    = l.UpdatedAt ?? l.CreatedAt
        }).ToList();
        await _target.UpsertAsync(TenantSlug, listings);

        // 4) Favorites.
        var favs = (await src.QueryAsync<EjarFavoriteRow>(
            "SELECT Id, UserId, ListingId, CreatedAt FROM Favorites WHERE IsDeleted = 0"
        )).Select(f => new Favorite
        {
            Id         = Favorite.MakeId(f.UserId, f.ListingId),
            UserId     = f.UserId,
            ListingId  = f.ListingId,
            At         = f.CreatedAt
        }).ToList();
        await _target.UpsertAsync(TenantSlug, favs);

        // 5) Conversations + Messages.
        var convs = (await src.QueryAsync<EjarConvRow>(
            @"SELECT Id, OwnerId, PartnerId, PartnerName, ListingId, Subject, LastAt,
                     OwnerUnread, PartnerUnread, CreatedAt
              FROM Conversations WHERE IsDeleted = 0"
        )).Select(c => new Conversation
        {
            Id           = c.Id,
            OwnerId      = c.OwnerId,
            OwnerName    = "",
            PartnerId    = c.PartnerId,
            PartnerName  = c.PartnerName ?? "",
            ListingId    = c.ListingId,
            Subject      = c.Subject,
            LastAt       = c.LastAt,
            OwnerUnread  = c.OwnerUnread,
            PartnerUnread = c.PartnerUnread,
            CreatedAt    = c.CreatedAt
        }).ToList();
        await _target.UpsertAsync(TenantSlug, convs);

        var msgs = (await src.QueryAsync<EjarMsgRow>(
            @"SELECT Id, ConversationId, [From] AS SenderIdRaw, Text AS Body, SentAt
              FROM Messages WHERE IsDeleted = 0"
        )).Select(m => new Message
        {
            Id             = m.Id,
            ConversationId = m.ConversationId,
            SenderId       = Guid.TryParse(m.SenderIdRaw, out var g) ? g : Guid.Empty,
            Body           = m.Body ?? "",
            SentAt         = m.SentAt
        }).ToList();
        await _target.UpsertAsync(TenantSlug, msgs);

        // 6) Notifications.
        var notifs = (await src.QueryAsync<EjarNotifRow>(
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

    // ──── Row types — مُطابِقَة لِأَعمِدَة SELECT أَعلاه ───────────────
    private sealed record EjarCategoryRow(Guid Id, string Slug, string Label, string? Icon);
    private sealed record EjarUserRow(Guid Id, string? FullName, string? Phone, string? Email, string? NationalId, bool IsDeleted, DateTime CreatedAt);
    private sealed record EjarListingRow(Guid Id, string? Title, string? Description, decimal Price,
                                          string? PropertyType, string? City, string? District,
                                          bool IsDeleted, DateTime CreatedAt, DateTime? UpdatedAt);
    private sealed record EjarFavoriteRow(Guid Id, Guid UserId, Guid ListingId, DateTime CreatedAt);
    private sealed record EjarConvRow(Guid Id, Guid OwnerId, Guid PartnerId, string? PartnerName,
                                       Guid ListingId, string? Subject, DateTime LastAt,
                                       int OwnerUnread, int PartnerUnread, DateTime CreatedAt);
    private sealed record EjarMsgRow(Guid Id, Guid ConversationId, string? SenderIdRaw, string? Body, DateTime SentAt);
    private sealed record EjarNotifRow(Guid Id, Guid UserId, string? Title, string? Body, string? RelatedUrl, bool IsRead, DateTime At);
}
