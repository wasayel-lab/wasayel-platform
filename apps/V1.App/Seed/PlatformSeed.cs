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
            categories: new[]
            {
                ("room",      "غُرفَة لِشَريك سَكَن", "🛏️"),
                ("apartment", "شَقّة كامِلَة",        "🏠"),
                ("studio",    "ستوديو",              "🪟"),
            },
            sampleListings: new (string title, decimal price, string cat, string district)[]
            {
                ("غُرفَة شَريك في شَقّة هادِئَة", 45000m, "room", "حَوبان"),
                ("غُرفَة قَريبَة من الجامِعَة",   55000m, "room", "المُدير"),
                ("شَقّة طالِبات مَفروشَة",       180000m, "apartment", "حَوبان"),
                ("ستوديو مُستَقِلّ مَع مَطبَخ",   90000m,  "studio", "السَلام"),
                ("شَقّة عائِليّة قَريبَة من الجامِعَة", 220000m, "apartment", "حَوبان"),
            });

        await SeedTenantIfMissingAsync(globalSession, store,
            slug: "ejar",
            name: "إيجار",
            color: "#F97316",
            city: "إب",
            tagLine: "كلّ ما يُؤَجَّر في مَدينَتك",
            categories: new[]
            {
                ("apartment", "شَقّة",  "🏢"),
                ("villa",     "فيلا",  "🏡"),
                ("office",    "مَكتَب", "💼"),
                ("shop",      "مَحلّ",  "🏪"),
                ("storage",   "مَخزَن", "📦"),
            },
            sampleListings: new (string title, decimal price, string cat, string district)[]
            {
                ("شَقّة فاخِرَة ٣ غُرَف",       350000m, "apartment", "حَوبان"),
                ("فيلا حَديثَة بـ حَديقَة",   1200000m, "villa",     "السَلام"),
                ("مَكتَب إداريّ مُجَهَّز",     180000m, "office",    "المُدير"),
                ("مَحلّ تِجاريّ على شارع رَئيسيّ", 240000m, "shop",  "المُدير"),
                ("مَخزَن واسِع",              120000m, "storage",   "حَوبان"),
                ("شَقّة عائِليّة ٤ غُرَف",     420000m, "apartment", "حَوبان"),
                ("فيلا كَبيرَة لِلإيجار السَنَويّ", 1800000m, "villa", "السَلام"),
                ("مَكتَب صَغير اقتِصاديّ",     90000m, "office",    "المُدير"),
            });

        Console.WriteLine("[Seed] ✅ Platform seed complete.");
    }

    private static async Task SeedTenantIfMissingAsync(
        IDocumentSession globalSession,
        IDocumentStore store,
        string slug, string name, string color, string city, string tagLine,
        (string slug, string label, string icon)[] categories,
        (string title, decimal price, string cat, string district)[] sampleListings)
    {
        var existing = await globalSession.LoadAsync<Tenant>(slug);
        if (existing is not null)
        {
            Console.WriteLine($"[Seed] tenant '{slug}' already exists, skipping.");
            return;
        }

        var tenant = new Tenant
        {
            Id = slug, Name = name, BrandColor = color,
            City = city, TagLine = tagLine,
            Categories = categories.Select(c => new Category
            {
                Slug = c.slug, Label = c.label, Icon = c.icon
            }).ToList()
        };
        globalSession.Store(tenant);
        await globalSession.SaveChangesAsync();
        Console.WriteLine($"[Seed] created tenant '{slug}' with {categories.Length} categories.");

        // الإعلانات داخِل session مَحصور بـ tenant slug (conjoined tenancy)
        await using var tenantSession = store.LightweightSession(slug);
        foreach (var s in sampleListings)
        {
            var id = Guid.NewGuid();
            var ev = new ListingCreated(
                id, slug, s.title, $"وَصف تَجريبيّ لِـ {s.title}", s.price,
                s.cat, city, s.district,
                new Dictionary<string, string>(), DateTime.UtcNow);
            tenantSession.Events.StartStream<Listing>(id, ev);
        }
        await tenantSession.SaveChangesAsync();
        Console.WriteLine($"[Seed] added {sampleListings.Length} listings to '{slug}'.");
    }
}
