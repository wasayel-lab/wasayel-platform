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
/// مَخطَّط المَصدَر (Apps/Ejar/Domain.Data/Data/Entities.cs):
///   UserEntity         → Users         (Id Guid، FullName, Phone, Email, City…)
///   ListingEntity      → Listings      (Title, Price, PropertyType, City, District…)
///   ConversationEntity → Conversations (OwnerId, PartnerId, ListingId, Subject…)
///   MessageEntity      → Messages      (ConversationId, From, Text, SentAt)
///   NotificationEntity → Notifications (UserId, Title, Body, IsRead, RelatedId)
///   Favorite           → Favorites     (UserId, EntityId, EntityType="Listing")
///   DiscoveryCategory  → DiscoveryCategories (Slug, Label, Icon, Kind)
///
/// كُلّ الـ IDs في Ejar V1 هي Guid فلا حاجَة لِخَريطَة سِترِنغ.
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

        // 1) Tenant.Categories مِن DiscoveryCategories.
        var categories = (await src.QueryAsync<EjarCategoryRow>(
            @"SELECT TOP 50 Id, Slug, Label, Icon FROM DiscoveryCategories
              WHERE IsDeleted = 0 ORDER BY Label"
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

        // 2) Users.
        var users = (await src.QueryAsync<EjarUserRow>(
            @"SELECT Id, FullName, Phone, CreatedAt
              FROM Users WHERE IsDeleted = 0"
        )).Select(u => new User
        {
            Id         = u.Id,
            TenantSlug = TenantSlug,
            FullName   = string.IsNullOrWhiteSpace(u.FullName) ? "مُستَخدِم جَديد" : u.FullName!,
            Phone      = u.Phone ?? "",
            CreatedAt  = u.CreatedAt
        }).ToList();
        await _target.UpsertAsync(TenantSlug, users);

        // 3) Listings — PropertyType يَلعَب دَور CategorySlug عِندَنا.
        var listings = (await src.QueryAsync<EjarListingRow>(
            @"SELECT Id, Title, Description, Price, PropertyType, City, District,
                     IsDeleted, CreatedAt, UpdatedAt
              FROM Listings WHERE IsDeleted = 0"
        )).Select(l => new Listing
        {
            Id           = l.Id,
            TenantSlug   = TenantSlug,
            Title        = l.Title ?? "",
            Description  = string.IsNullOrEmpty(l.Description) ? null : l.Description,
            Price        = l.Price,
            CategorySlug = l.PropertyType ?? "",
            City         = string.IsNullOrEmpty(l.City) ? null : l.City,
            District     = string.IsNullOrEmpty(l.District) ? null : l.District,
            Attributes   = new(),
            IsDeleted    = false,
            CreatedAt    = l.CreatedAt,
            UpdatedAt    = l.UpdatedAt ?? l.CreatedAt
        }).ToList();
        await _target.UpsertListingsAsync(TenantSlug, listings);

        // 4) Favorites — generic shape (EntityType + EntityId)؛ نَأخُذ
        // فَقَط ما EntityType = "Listing".
        var favs = (await src.QueryAsync<EjarFavoriteRow>(
            @"SELECT Id, UserId, EntityId AS ListingId, CreatedAt
              FROM Favorites
              WHERE IsDeleted = 0 AND EntityType = 'Listing'"
        )).Select(f => new Favorite
        {
            Id        = Favorite.MakeId(f.UserId, f.ListingId),
            UserId    = f.UserId,
            ListingId = f.ListingId,
            At        = f.CreatedAt
        }).ToList();
        await _target.UpsertAsync(TenantSlug, favs);

        // 5) Conversations.
        var convs = (await src.QueryAsync<EjarConvRow>(
            @"SELECT Id, OwnerId, PartnerId, PartnerName, ListingId, Subject, LastAt,
                     OwnerUnread, PartnerUnread, CreatedAt
              FROM Conversations WHERE IsDeleted = 0"
        )).Select(c => new Conversation
        {
            Id            = c.Id,
            OwnerId       = c.OwnerId,
            OwnerName     = "",
            PartnerId     = c.PartnerId,
            PartnerName   = c.PartnerName ?? "",
            ListingId     = c.ListingId,
            Subject       = c.Subject,
            LastAt        = c.LastAt,
            OwnerUnread   = c.OwnerUnread,
            PartnerUnread = c.PartnerUnread,
            CreatedAt     = c.CreatedAt
        }).ToList();
        await _target.UpsertAsync(TenantSlug, convs);

        // 6) Messages — From + Text، لا SenderId/Body. From يَحوي Guid
        // بِالنَّصّ (المُرسِل).
        var msgs = (await src.QueryAsync<EjarMsgRow>(
            @"SELECT Id, ConversationId, [From] AS FromRaw, Text AS Body, SentAt
              FROM Messages WHERE IsDeleted = 0"
        )).Select(m => new Message
        {
            Id             = m.Id,
            ConversationId = m.ConversationId,
            SenderId       = Guid.TryParse(m.FromRaw, out var g) ? g : Guid.Empty,
            Body           = m.Body ?? "",
            SentAt         = m.SentAt
        }).Where(m => m.SenderId != Guid.Empty).ToList();
        await _target.UpsertAsync(TenantSlug, msgs);

        // 7) Notifications — RelatedId (لا RelatedUrl)، Type بَدَل Kind.
        var notifs = (await src.QueryAsync<EjarNotifRow>(
            @"SELECT Id, UserId, Title, Body, RelatedId, IsRead, CreatedAt AS [At]
              FROM Notifications WHERE IsDeleted = 0"
        )).Select(n => new Notification
        {
            Id         = n.Id,
            UserId     = n.UserId,
            Title      = n.Title ?? "",
            Body       = n.Body ?? "",
            RelatedUrl = n.RelatedId,
            IsRead     = n.IsRead,
            At         = n.At
        }).ToList();
        await _target.UpsertAsync(TenantSlug, notifs);

        // 8) Plans — Plan.Id في إيجار V1 هُوَ Guid، نَتَّخِذه slug في
        // platform-v1 (Plan.Id = string).
        var plans = (await src.QueryAsync<EjarPlanRow>(
            @"SELECT Id, Label, Price, MaxActiveListings, Description
              FROM Plans WHERE IsDeleted = 0"
        )).Select(p => new ACommerce.Kit.Subscriptions.Plan
        {
            Id            = p.Id.ToString(),
            Name          = p.Label ?? "",
            Price         = p.Price,
            ListingsQuota = p.MaxActiveListings,
            DaysPeriod    = 30,
            Description   = p.Description,
            IsActive      = true
        }).ToList();
        await _target.UpsertAsync(TenantSlug, plans);

        // 9) Subscriptions — event-sourced في platform-v1.
        var subs = (await src.QueryAsync<EjarSubRow>(
            @"SELECT Id, UserId, PlanId, ListingsLimit, StartDate, EndDate
              FROM Subscriptions WHERE IsDeleted = 0"
        )).Select(s => new SubscriptionImport(
            s.Id, s.UserId, s.PlanId.ToString(), s.ListingsLimit,
            (s.EndDate - s.StartDate).Days, s.StartDate
        )).ToList();
        await _target.UpsertSubscriptionsAsync(TenantSlug, subs);

        // 10) SupportTickets → Ticket (event-sourced).
        var tickets = (await src.QueryAsync<EjarSupportTicketRow>(
            @"SELECT st.Id, st.UserId, u.FullName AS AuthorName,
                     st.Subject, st.RelatedEntityId AS Body, st.CreatedAt
              FROM SupportTickets st
              LEFT JOIN Users u ON u.Id = st.UserId
              WHERE st.IsDeleted = 0"
        )).Select(t => new TicketImport(
            t.Id, t.UserId, t.AuthorName ?? "—",
            t.Subject ?? "", t.Body ?? "", t.CreatedAt
        )).ToList();
        await _target.UpsertTicketsAsync(TenantSlug, tickets);

        // 11) كُلّ الجَداوِل الباقِيَة كَ ImportedRecord.
        string[] genericTables =
        {
            "UserPushTokens", "Reports", "Invoices", "AppVersions",
            "DiscoveryRegions", "DiscoveryAmenities", "TaxonomyNodes",
            "AttributeDefinitions", "AttributeValues", "CategoryAttributeMappings",
            "SupportMessages"
        };
        foreach (var t in genericTables)
            await _target.DumpGenericAsync(src, TenantSlug, t);
    }

    private sealed record EjarCategoryRow(Guid Id, string Slug, string Label, string? Icon);
    private sealed record EjarUserRow(Guid Id, string? FullName, string? Phone, DateTime CreatedAt);
    private sealed record EjarListingRow(Guid Id, string? Title, string? Description, decimal Price,
                                          string? PropertyType, string? City, string? District,
                                          bool IsDeleted, DateTime CreatedAt, DateTime? UpdatedAt);
    private sealed record EjarFavoriteRow(Guid Id, Guid UserId, Guid ListingId, DateTime CreatedAt);
    private sealed record EjarConvRow(Guid Id, Guid OwnerId, Guid PartnerId, string? PartnerName,
                                       Guid ListingId, string? Subject, DateTime LastAt,
                                       int OwnerUnread, int PartnerUnread, DateTime CreatedAt);
    private sealed record EjarMsgRow(Guid Id, Guid ConversationId, string? FromRaw, string? Body, DateTime SentAt);
    private sealed record EjarNotifRow(Guid Id, Guid UserId, string? Title, string? Body, string? RelatedId, bool IsRead, DateTime At);
    private sealed record EjarPlanRow(Guid Id, string? Label, decimal Price, int MaxActiveListings, string? Description);
    private sealed record EjarSubRow(Guid Id, Guid UserId, Guid PlanId, int ListingsLimit, DateTime StartDate, DateTime EndDate);
    private sealed record EjarSupportTicketRow(Guid Id, Guid UserId, string? AuthorName, string? Subject, string? Body, DateTime CreatedAt);
}
