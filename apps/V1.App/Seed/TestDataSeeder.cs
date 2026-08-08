using ACommerce.Kit.Auth;
using ACommerce.Kit.Chat;
using ACommerce.Kit.Listings;
using ACommerce.Kit.Notifications;
using ACommerce.Kit.Roles;
using ACommerce.Kit.Tenants;
using ACommerce.Kit.Favorites;
using ACommerce.Kit.Cart;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using Marten;

namespace ACommerce.V1.App.Seed;

/// <summary>
/// بَذر بَيانات اختِبار لِفَحص Layer 6 — يُضيف:
///   ١) أَدوار قِياسِيَّة (customer / rider / driver / vendor) لِكُلّ مُستَأجِر
///   ٢) مُستَخدِم اختِبار لِكُلّ دَور (هاتِف فَريد لِيُمكِن تَسجيل دُخوله)
///   ٣) إعلان واحِد + مُحادَثَة + إشعار + مُفَضَّلَة + عُنصُر سَلَّة لِكُلّ
///      مُستَخدِم لِتَمتَلِئ الصَفَحات بَيانات (تَفادي حالَة empty-state).
/// idempotent: لَو الـ user مَوجود بِنَفس الهاتِف، يُتخَطّى.
/// </summary>
public static class TestDataSeeder
{
    /// <summary>تَخطيط الاختِبار: tenant → roles[] → phone pattern.
    /// رَقم الهاتِف لِكُلّ مُستَخدِم = "05{tenant_idx}{role_idx}1234567"
    /// كَ مِفتاح بَسيط يُسَهِّل تَوليد الـ login matrix في verify-runtime.</summary>
    public static readonly (string slug, string[] roles)[] Plan =
    {
        ("ashare", new[] { "customer", "host", "vendor" }),
        ("ejar",   new[] { "customer", "host", "vendor" }),
        ("order",  new[] { "customer", "vendor", "driver" }),
        ("injez",  new[] { "rider", "driver" }),
    };

    public static async Task RunAsync(IServiceProvider services)
    {
        var store = services.GetRequiredService<IDocumentStore>();
        await using var globalSession = store.LightweightSession();

        for (int ti = 0; ti < Plan.Length; ti++)
        {
            var (slug, roles) = Plan[ti];
            var tenant = await globalSession.LoadAsync<Tenant>(slug);
            if (tenant is null) continue;   // tenant غَير مَوجود → تَخَطّي

            // (١) أَضِف الأَدوار النّاقِصَة فَقَط — احتَرِم تَخصيص الـ admin.
            bool tenantTouched = false;
            for (int ri = 0; ri < roles.Length; ri++)
            {
                var slugRole = roles[ri];
                if (tenant.Roles.Any(r => r.Slug == slugRole)) continue;
                var tmpl = RoleCatalog.Find(slugRole);
                if (tmpl is null) continue;
                tenant.Roles.Add(RoleCatalog.InstantiateRole(tmpl, sortOrder: ri));
                tenantTouched = true;
            }
            if (tenantTouched)
            {
                globalSession.Store(tenant);
                Console.WriteLine($"[TestSeed] tenant '{slug}' got {tenant.Roles.Count} roles.");
            }

            // (٢) مُستَخدِم لِكُلّ دَور.
            await using var tenantSession = store.LightweightSession(slug);
            var roleUsers = new List<(string Role, User User)>();
            for (int ri = 0; ri < roles.Length; ri++)
            {
                var slugRole = roles[ri];
                var phone = $"05{ti}{ri}1234567"; // مَثَلاً 050012345 لِـ ashare/customer
                var user = (await tenantSession.Query<User>()
                    .Where(u => u.Phone == phone).ToListAsync())
                    .FirstOrDefault();
                if (user is null)
                {
                    user = new User
                    {
                        Id = Guid.NewGuid(),
                        Phone = phone,
                        FullName = $"اختِبار {slug} {slugRole}",
                        ActiveRole = slugRole,
                        Role = slugRole,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    };
                    tenantSession.Store(user);
                    Console.WriteLine($"[TestSeed] {slug}/{slugRole} → {phone}");
                }
                else if (user.ActiveRole != slugRole)
                {
                    user.ActiveRole = slugRole;
                    tenantSession.Store(user);
                }

                // (٣) بَيانات لِتَمتَلِئ صَفَحات الـ user الشَخصِيَّة.
                await EnsureSampleListingAsync(tenantSession, slug, user);
                await EnsureSampleNotificationsAsync(tenantSession, user);
                roleUsers.Add((slugRole, user));
            }

            // (٤) مُحادَثَة بَين أَوَّل مُستَخدِمَين (لِتَمتَلِئ صَفحَة الرَّسائِل).
            if (roleUsers.Count >= 2)
                await EnsureSampleConversationAsync(tenantSession, roleUsers[0].User, roleUsers[1].User);

            // (٥) صَفقَتان (نَشِطَة + مُكتَمِلَة) لِتَمتَلِئ MyDeals/DealDetail.
            if (roleUsers.Count >= 2)
                await EnsureSampleDealsAsync(tenantSession, slug, roleUsers[0].User, roleUsers[1].User);

            await tenantSession.SaveChangesAsync();
        }
        await globalSession.SaveChangesAsync();
        Console.WriteLine("[TestSeed] ✅ test data seeded.");
    }

    private static async Task EnsureSampleListingAsync(
        IDocumentSession s, string slug, User user)
    {
        var existing = await s.Query<Listing>()
            .Where(l => l.Attributes != null && l.Attributes.ContainsKey("owner_id"))
            .Take(50).ToListAsync();
        if (existing.Any(l => l.Attributes.TryGetValue("owner_id", out var oid)
                               && oid == user.Id.ToString())) return;

        var id = Guid.NewGuid();
        var ev = new ListingCreated(
            Id: id, TenantSlug: slug,
            Title: $"إعلان اختِبار — {user.FullName}",
            Description: "وَصف وَهمِيّ لِأَغراض الفَحص.",
            Price: 100,
            CategorySlug: "general",
            City: "إب",
            District: "حَوبان",
            Attributes: new() { ["owner_id"] = user.Id.ToString() },
            At: DateTime.UtcNow);
        s.Events.StartStream<Listing>(id, ev);
    }

    private static async Task EnsureSampleNotificationsAsync(
        IDocumentSession s, User user)
    {
        var has = await s.Query<Notification>()
            .Where(n => n.UserId == user.Id).Take(1).ToListAsync();
        if (has.Count > 0) return;
        // عِدَّة إشعارات بِأَنواع مُختَلِفَة لِفَحص AcListRow بِأَيقونات + غَير مَقروء.
        var samples = new (string Type, string Title, string Body, bool Read)[]
        {
            ("saved_search_match", "إعلان جَديد يُطابِق بَحثَك", "شَقّة في حَوبان بِـ 100 ر.س", false),
            ("offer_received",     "عَرض جَديد على إعلانِك",     "قَدَّمَ أَحَدُهُم عَرضاً — راجِعه", false),
            ("welcome",            "مَرحَباً بِك",               "أَكمِل مِلَفَّك لِتَظهَر بِثِقَة.", true),
        };
        var t = DateTime.UtcNow;
        foreach (var (type, title, body, read) in samples)
            s.Store(new Notification
            {
                Id = Guid.NewGuid(), UserId = user.Id, Type = type,
                Title = title, Body = body, IsRead = read,
                At = t = t.AddMinutes(-37)
            });
    }

    private static async Task EnsureSampleConversationAsync(
        IDocumentSession s, User a, User bUser)
    {
        var existing = await s.Query<Conversation>()
            .Where(c => c.OwnerId == a.Id || c.PartnerId == a.Id).Take(1).ToListAsync();
        if (existing.Count > 0) return;

        var convId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        s.Store(new Conversation
        {
            Id = convId,
            OwnerId = a.Id, OwnerName = a.FullName,
            PartnerId = bUser.Id, PartnerName = bUser.FullName,
            LastMessage = "تَمام، نَتَّفِق على المَوعِد غَداً.",
            LastAt = now, OwnerUnread = 0, PartnerUnread = 2,
            CreatedAt = now.AddHours(-2)
        });
        var msgs = new (Guid Sender, string Body, int MinAgo)[]
        {
            (a.Id,     "السَّلام عَلَيكُم، هَل الإعلان ما زالَ مُتاحاً؟", 120),
            (bUser.Id, "وعَلَيكُم السَّلام، نَعَم مُتاح.",               110),
            (a.Id,     "مُمتاز، مَتى يُمكِن المُعايَنَة؟",                95),
            (bUser.Id, "تَمام، نَتَّفِق على المَوعِد غَداً.",            90),
        };
        foreach (var (sender, body, mins) in msgs)
            s.Store(new Message
            {
                Id = Guid.NewGuid(), ConversationId = convId,
                SenderId = sender, Body = body, SentAt = now.AddMinutes(-mins)
            });
    }

    /// <summary>صَفقَتان لِفَحص MyDeals/DealDetail مَملوءَتَين: واحِدَة نَشِطَة
    /// في مُنتَصَف التَّدَفُّق (Paid) وأُخرى مُكتَمِلَة (Reviewed) — العميل
    /// (أَوَّل مُستَخدِم) هو المُبادِر لِتَظهَر في لَوحَتِه.</summary>
    private static async Task EnsureSampleDealsAsync(
        IDocumentSession s, string slug, User initiator, User counterparty)
    {
        var existing = await s.Query<Deal>()
            .Where(d => d.InitiatorId == initiator.Id).Take(1).ToListAsync();
        if (existing.Count > 0) return;

        var now = DateTime.UtcNow;
        DealEvent Ev(DealStage from, DealStage to, string action, int hoursAgo) =>
            new(from, to, action, initiator.Id, initiator.FullName, null, now.AddHours(-hoursAgo));

        // (أ) نَشِطَة — وَصَلَت مَرحَلَة الدَّفع.
        s.Store(new Deal
        {
            Id = Guid.NewGuid(), TenantSlug = slug, Pattern = "marketplace",
            ListingTitle = "إعلان اختِبار — صَفقَة نَشِطَة",
            InitiatorId = initiator.Id, InitiatorName = initiator.FullName,
            CounterpartyId = counterparty.Id, CounterpartyName = counterparty.FullName,
            AmountSar = 100, CommissionSar = 5,
            Stage = DealStage.Paid, Status = DealStatus.Active,
            CreatedAt = now.AddHours(-26), UpdatedAt = now.AddHours(-3),
            Timeline =
            {
                Ev(DealStage.Offered,   DealStage.Booked,    "assigned",  26),
                Ev(DealStage.Booked,    DealStage.Confirmed, "advanced",  20),
                Ev(DealStage.Confirmed, DealStage.Paid,      "advanced",   3),
            }
        });

        // (ب) مُكتَمِلَة — وَصَلَت Reviewed (تُظهِر نَموذَج التَّقييم).
        s.Store(new Deal
        {
            Id = Guid.NewGuid(), TenantSlug = slug, Pattern = "marketplace",
            ListingTitle = "إعلان اختِبار — صَفقَة مُكتَمِلَة",
            InitiatorId = initiator.Id, InitiatorName = initiator.FullName,
            CounterpartyId = counterparty.Id, CounterpartyName = counterparty.FullName,
            AmountSar = 250, CommissionSar = 12.5m,
            Stage = DealStage.Received, Status = DealStatus.Completed,
            CreatedAt = now.AddDays(-6), UpdatedAt = now.AddDays(-1),
            Timeline =
            {
                Ev(DealStage.Offered,   DealStage.Booked,    "assigned",  144),
                Ev(DealStage.Booked,    DealStage.Confirmed, "advanced",  120),
                Ev(DealStage.Confirmed, DealStage.Paid,      "advanced",   96),
                Ev(DealStage.Paid,      DealStage.Delivered, "advanced",   48),
                Ev(DealStage.Delivered, DealStage.Received,  "advanced",   24),
            }
        });
    }
}
