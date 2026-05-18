using ACommerce.Kit.Listings;
using ACommerce.Kit.Tenants;
using Marten;

namespace ACommerce.V1.App.Seed;

/// <summary>
/// بَذر بَيانات أَوّليّة عَبر المنصّة. يُنشِئ tenantَين (ashare، ejar)
/// مَع ألوان، فِئات، وعَدَد من الإعلانات لكلّ منهما. يُحَقِّق idempotency
/// عَن طَريق فَحص وُجود الـ Tenant document قَبل الإنشاء.
/// </summary>
public static class PlatformSeed
{
    public static async Task RunAsync(IServiceProvider services)
    {
        var store = services.GetRequiredService<IDocumentStore>();
        await using var globalSession = store.LightweightSession();

        await SeedTenantIfMissingAsync(globalSession, store,
            slug: "ashare",
            name: "عَشير",
            color: "#345454",   // Deep Olive Green — هُويَّة عَشير V3 الرَسمِيَّة
            city: "إب",
            tagLine: "السَكَن المُشتَرَك بأَريَحيّة",
            authChannel: "nafath",
            categories: new[]
            {
                ("roommate_has",   "عشير عنده سكن", "🏠", "roommate"),
                ("roommate_wants", "عشير يدور سكن", "🔎", "roommate"),
            },
            sampleListings: new (string title, decimal price, string cat, string city, string district)[]
            {
                ("عِندي شَقّة وأَدوَر شَريك سَكَن",       45000m, "roommate_has",   "إب",     "حَوبان"),
                ("غُرفَة قَريبَة مِن الجامِعَة",          55000m, "roommate_has",   "إب",     "المُدير"),
                ("شَقّة طالِبات مَفروشَة شَريك",         180000m, "roommate_has",   "إب",     "حَوبان"),
                ("أَدوَر شَريك سَكَن طالِب",               0m,    "roommate_wants", "إب",     "حَوبان"),
                ("أَدوَر غُرفَة في شَقّة هادِئَة",          0m,    "roommate_wants", "تَعِز",  "ذِنوبَة"),
                ("ستوديو شَريك وَسَط المَدينَة",         95000m, "roommate_has",   "تَعِز",  "الجَحمَليَّة"),
                ("أَدوَر سَكَن مَع طُلّاب طِبّ",            0m,    "roommate_wants", "صَنعاء", "حَدّة"),
                ("غُرفَة شَريك في شَقّة عائِلِيَّة",      50000m, "roommate_has",   "صَنعاء", "عَصر"),
            });

        await SeedTenantIfMissingAsync(globalSession, store,
            slug: "ejar",
            name: "إيجار",
            color: "#1d4ed8",  // Marketplace Blue — هُويَّة إيجار V1 الرَسمِيَّة
            city: "إب",
            tagLine: "كلّ ما يُؤَجَّر في مَدينَتك",
            authChannel: "phone",
            categories: new[]
            {
                // عَقارات سَكَنيَّة
                ("apartment", "شَقّة",   "🏢", "residential"),
                ("villa",     "فيلا",    "🏡", "residential"),
                ("studio",    "ستوديو",  "🛌", "residential"),
                ("room",      "غُرفَة",   "🚪", "residential"),
                // عَقارات تِجاريَّة
                ("office",    "مَكتَب",  "💼", "commercial"),
                ("shop",      "مَحلّ",    "🏪", "commercial"),
                ("storage",   "مَخزَن",  "📦", "commercial"),
                // مُناسَبات
                ("hall",      "صالَة أَفراح", "🎉", "events"),
                // مَركَبات
                ("car",       "سَيّارَة", "🚗", "vehicles"),
            },
            sampleListings: new (string title, decimal price, string cat, string city, string district)[]
            {
                ("شَقّة فاخِرَة ٣ غُرَف",                350000m, "apartment", "إب",     "حَوبان"),
                ("فيلا حَديثَة بِحَديقَة",              1200000m, "villa",     "إب",     "السَلام"),
                ("مَكتَب إداريّ مُجَهَّز",                180000m, "office",    "إب",     "المُدير"),
                ("مَحلّ تِجاريّ على شارِع رَئيسيّ",       240000m, "shop",      "إب",     "المُدير"),
                ("مَخزَن واسِع",                          120000m, "storage",   "إب",     "حَوبان"),
                ("شَقّة عائِليّة ٤ غُرَف — صَنعاء",       420000m, "apartment", "صَنعاء", "حَدّة"),
                ("فيلا كَبيرَة لِلإيجار السَنَويّ",      1800000m, "villa",     "صَنعاء", "بَيت بَوس"),
                ("مَكتَب صَغير اقتِصاديّ — تَعِز",         90000m, "office",    "تَعِز",  "الجَحمَلِيَّة"),
                ("مَحلّ صَغير في الجَنَد",                160000m, "shop",      "تَعِز",  "الجَنَد"),
                ("فيلا فاخِرَة — صَنعاء",                2500000m, "villa",     "صَنعاء", "الجَريف"),
                ("شَقّة دور أَرضيّ — عَدَن",              280000m, "apartment", "عَدَن",  "خور مَكسَر"),
                ("مَخزَن قَريب مِن المَيناء — عَدَن",      210000m, "storage",   "عَدَن",  "التَواهي"),
            });

        await SeedPlansIfMissingAsync(store, "ashare");
        await SeedPlansIfMissingAsync(store, "ejar");

        Console.WriteLine("[Seed] ✅ Platform seed complete.");
    }

    private static async Task SeedPlansIfMissingAsync(IDocumentStore store, string slug)
    {
        await using var s = store.LightweightSession(slug);
        var existing = await s.Query<ACommerce.Kit.Subscriptions.Plan>().AnyAsync();
        if (existing) return;
        s.Store(new ACommerce.Kit.Subscriptions.Plan { Id = "free",  Name = "مَجّانيّ", Price = 0, ListingsQuota = 1, DaysPeriod = 30, Description = "إعلان واحِد شَهريّاً" });
        s.Store(new ACommerce.Kit.Subscriptions.Plan { Id = "basic", Name = "أساسيّ",   Price = 49, ListingsQuota = 10, DaysPeriod = 30, Description = "١٠ إعلانات شَهريّاً + إبراز" });
        s.Store(new ACommerce.Kit.Subscriptions.Plan { Id = "pro",   Name = "احتِرافيّ", Price = 199, ListingsQuota = 100, DaysPeriod = 30, Description = "حَتى ١٠٠ إعلان + دَعم أَوّليّ" });
        await s.SaveChangesAsync();
        Console.WriteLine($"[Seed] Plans added for '{slug}'.");
    }

    private static async Task SeedTenantIfMissingAsync(
        IDocumentSession globalSession,
        IDocumentStore store,
        string slug, string name, string color, string city, string tagLine, string authChannel,
        (string slug, string label, string icon, string kind)[] categories,
        (string title, decimal price, string cat, string city, string district)[] sampleListings)
    {
        var existing = await globalSession.LoadAsync<Tenant>(slug);
        if (existing is not null)
        {
            // المُستَأجِر مَوجود — لا نَلمَسه. أَيّ تَعديلات إداريَّة (مِن
            // لَوحَة التَحَكُّم أَو الوَكيل) هي مَصدَر الحَقيقَة، والـ seed
            // مُجَرَّد قائِمَة قِيَم افتِراضيَّة عِند أَوّل تَشغيل. كانَ هُنا
            // فَرع يُعيد كِتابَة الفِئات/اللَون/الاسم لَو اختَلَفَت بَصمَتُها
            // عَنِ الكود، لكِنَّه كانَ يَمسَح تَعديلات الـ admin في كُلّ
            // إعادَة تَشغيل — لِذلك أُزيل.
            Console.WriteLine($"[Seed] tenant '{slug}' exists — left untouched.");
            return;
        }

        var tenant = new Tenant
        {
            Id = slug, Name = name, BrandColor = color,
            City = city, TagLine = tagLine, AuthChannel = authChannel,
            Categories = categories.Select((c, i) => new Category
            {
                Slug = c.slug, Label = c.label, Icon = c.icon,
                Kind = c.kind, SortOrder = i
            }).ToList()
        };
        globalSession.Store(tenant);
        await globalSession.SaveChangesAsync();
        Console.WriteLine($"[Seed] created tenant '{slug}' with {categories.Length} categories.");

        await SeedListingsAsync(store, slug, sampleListings);
    }

    private static async Task SeedListingsAsync(
        IDocumentStore store, string slug,
        (string title, decimal price, string cat, string city, string district)[] sampleListings)
    {
        await using var tenantSession = store.LightweightSession(slug);
        foreach (var s in sampleListings)
        {
            var id = Guid.NewGuid();
            var ev = new ListingCreated(
                id, slug, s.title, $"وَصف تَجريبيّ لِـ {s.title}", s.price,
                s.cat, s.city, s.district,
                new Dictionary<string, string>(), DateTime.UtcNow);
            tenantSession.Events.StartStream<Listing>(id, ev);
        }
        await tenantSession.SaveChangesAsync();
        Console.WriteLine($"[Seed] added {sampleListings.Length} listings to '{slug}'.");
    }
}
