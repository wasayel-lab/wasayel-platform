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
            color: "#7C3AED",
            city: "إب",
            tagLine: "السَكَن المُشتَرَك بأَريَحيّة",
            authChannel: "nafath",
            categories: new[]
            {
                ("room",      "غُرفَة لِشَريك سَكَن", "🛏️"),
                ("apartment", "شَقّة كامِلَة",        "🏠"),
                ("studio",    "ستوديو",              "🪟"),
            },
            sampleListings: new (string title, decimal price, string cat, string city, string district)[]
            {
                ("غُرفَة شَريك في شَقّة هادِئَة",       45000m, "room",      "إب",     "حَوبان"),
                ("غُرفَة قَريبَة مِن الجامِعَة",         55000m, "room",      "إب",     "المُدير"),
                ("شَقّة طالِبات مَفروشَة",              180000m, "apartment", "إب",     "حَوبان"),
                ("ستوديو مُستَقِلّ مَع مَطبَخ",           90000m, "studio",   "إب",     "السَلام"),
                ("شَقّة عائِليّة قَريبَة مِن الجامِعَة", 220000m, "apartment", "إب",     "حَوبان"),
                ("غُرفَة في فيلا شَريك سَكَن",           60000m, "room",      "تَعِز",  "ذِنوبَة"),
                ("ستوديو حَديث وَسَط المَدينَة",         95000m, "studio",   "تَعِز",  "الجَحمَليَّة"),
                ("شَقّة فَسيحَة لِلطُلّاب",             170000m, "apartment", "صَنعاء", "حَدّة"),
                ("غُرفَة شَريك في شَقّة عائِلِيَّة",     50000m, "room",      "صَنعاء", "عَصر"),
                ("ستوديو راقٍ — الجَريف",              110000m, "studio",   "صَنعاء", "الجَريف"),
            });

        await SeedTenantIfMissingAsync(globalSession, store,
            slug: "ejar",
            name: "إيجار",
            color: "#C2410C",
            city: "إب",
            tagLine: "كلّ ما يُؤَجَّر في مَدينَتك",
            authChannel: "phone",
            categories: new[]
            {
                ("apartment", "شَقّة",  "🏢"),
                ("villa",     "فيلا",  "🏡"),
                ("office",    "مَكتَب", "💼"),
                ("shop",      "مَحلّ",  "🏪"),
                ("storage",   "مَخزَن", "📦"),
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
        (string slug, string label, string icon)[] categories,
        (string title, decimal price, string cat, string city, string district)[] sampleListings)
    {
        var existing = await globalSession.LoadAsync<Tenant>(slug);
        if (existing is not null)
        {
            // حَدِّث الحُقول المُتَغَيِّرَة في الإعدادات
            var changed = false;
            if (existing.AuthChannel != authChannel) { existing.AuthChannel = authChannel; changed = true; }
            if (existing.BrandColor != color)        { existing.BrandColor = color;        changed = true; }
            if (existing.TagLine != tagLine)         { existing.TagLine = tagLine;         changed = true; }
            if (existing.City != city)               { existing.City = city;               changed = true; }
            if (existing.Name != name)               { existing.Name = name;               changed = true; }

            if (changed)
            {
                globalSession.Store(existing);
                await globalSession.SaveChangesAsync();
                Console.WriteLine($"[Seed] tenant '{slug}': metadata updated.");
            }

            // أَعِد بَذر الإعلانات إن كانَت كُلّها مَحصورَة بِالمَدينَة
            // الافتِراضِيَّة فَقَط — يَدُلّ عَلى أنَّها مِن seed قَديم قَبل
            // تَوسيع المُدُن. حَذف نَظيف ثُمّ إعادَة بَذر بِالتَنَوُّع الجَديد.
            await using var tQ = store.QuerySession(slug);
            var actualCities = (await tQ.Query<Listing>()
                .Where(x => !x.IsDeleted && x.City != null)
                .Select(x => x.City!).ToListAsync())
                .Distinct().ToList();
            var sampleCities = sampleListings.Select(s => s.city).Distinct().ToList();
            var staleListings = actualCities.Count <= 1 && sampleCities.Count > 1;
            if (staleListings)
            {
                await using var purge = store.LightweightSession(slug);
                purge.DeleteWhere<Listing>(x => true);
                purge.DeleteWhere<ACommerce.Kit.Favorites.Favorite>(x => true);
                await purge.SaveChangesAsync();
                Console.WriteLine($"[Seed] tenant '{slug}': purged stale single-city listings.");
                await SeedListingsAsync(store, slug, sampleListings);
            }
            return;
        }

        var tenant = new Tenant
        {
            Id = slug, Name = name, BrandColor = color,
            City = city, TagLine = tagLine, AuthChannel = authChannel,
            Categories = categories.Select(c => new Category
            {
                Slug = c.slug, Label = c.label, Icon = c.icon
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
