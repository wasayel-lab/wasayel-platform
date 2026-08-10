using ACommerce.Kit.Theme;
using ACommerce.Templates.Customer.Marketplace.Services;
using Marten;
using Xunit;

namespace ACommerce.Platform.Tests;

// ─── عَزل كاش الثيم ───────────────────────────────────────────────────
//
// كاش الثيم أَخطَر مِن كاش الأَدوار بِفَرق نَوعيّ: تَسَرُّب تَعريف دَور
// يُظهِر خِياراً لا يَملِكُه مَتجَر — خَطَأً قَد لا يُلاحَظ؛ وتَسَرُّب
// ثيم **يَصبُغ صَفحَة مَتجَر بِلَون مَتجَر آخَر** — خَطَأً يَراه كُلّ
// زائِر فَوراً.
//
// وثَلاث دَعاوى تُفحَص هُنا بِلا قاعِدَة بَيانات:
//   ١. سِياق بِلا مُستَأجِر لا يَستَعلِم أَصلاً ويُجيب بِقاعِدَة المَنصَّة.
//   ٢. **الفَشَل لا يُخَزَّن** — وهذا هو ما يَمنَع خَلَلاً عابِراً مِن
//      التَجَمُّد حالَةً دائِمَة. مَفحوص بِمَخزَن يَفشَل كُلّ قِراءَة.
//   ٣. الإبطال بِمِفتاح واحِد لا مَسح شامِل، ولا يَرمي لِمِفتاح غائِب.
//
// أَمّا العَزل بَينَ مُستَأجِرَين حَيَّين فَبُنيَويّ لا اتِّفاقيّ
// (‏QuerySession(slug)‎ + إيجار مُقتَرِن)، ومَوضِع بُرهانِه البُرهان
// الحَيّ لا اختِبار وَحدَة: تُكتَب وَثيقَة لِـadwar-demo ويُقارَن
// ashare بايتاً بِبايت.

public class TenantThemeCacheIsolationTests
{
    /// <summary>مَخزَن يُشير إلى مَنفَذ مُغلَق — كُلّ استِعلام يَرمي.
    /// (‏Marten يُؤَجِّل الاتِّصال إلى أَوَّل استِعلام، فَالبِناء
    /// يَنجَح.)</summary>
    private static IDocumentStore UnreachableStore() => DocumentStore.For(o =>
    {
        o.Connection("Host=127.0.0.1;Port=1;Database=nope;Username=nope;Password=nope;Timeout=1");
        o.DatabaseSchemaName = "platform";
        o.Policies.AllDocumentsAreMultiTenanted();
        o.Schema.For<TenantThemeDefinition>().Identity(x => x.Id);
    });

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task ContextWithoutATenant_AnswersWithThePlatformBaseline(string? slug)
    {
        // مَخزَن يَفشَل حَتماً: لَو استُعلِمَ لَظَهَرَ الفَشَل. مُروره
        // يُثبِت أَنّ الفَرع يَقطَع **قَبل** أَيّ استِعلام.
        using var store = UnreachableStore();
        var svc = new TenantThemeService(store);

        Assert.Same(TenantThemeSet.Platform, await svc.ForAsync(slug));
        Assert.Same(ThemeCatalog.Default, await svc.EffectiveAsync(slug));
    }

    [Fact]
    public async Task AFailedRead_FallsBackAndIsNeverCached()
    {
        using var store = UnreachableStore();
        var svc = new TenantThemeService(store);

        var first  = await svc.ForAsync("adwar-demo");
        var second = await svc.ForAsync("adwar-demo");

        // السُقوط: مَظهَر اليَوم حَرفاً، لا كُتلَة ناقِصَة ولا استِثناء.
        Assert.Same(ThemeCatalog.Default, first.Theme);
        Assert.Same(ThemeCatalog.Default, second.Theme);

        // ولا تَخزين: لَقطَة الفَشَل بِلا سلاج، وهي العَلامَة الَّتي
        // يَقرَؤُها الشَّرط في ForAsync. لَو خُزِّنَت لَتَجَمَّدَ خَلَل
        // ثَوانٍ حالَةً دائِمَة إلى إعادَة التَشغيل.
        Assert.Null(first.TenantSlug);
        Assert.Null(second.TenantSlug);
    }

    [Fact]
    public async Task DifferentTenantsNeverShareASnapshot()
    {
        using var store = UnreachableStore();
        var svc = new TenantThemeService(store);

        // لا تَبادُل حَتّى في مَسار السُقوط: كِلاهُما قاعِدَة المَنصَّة،
        // ولا أَحَدُهُما يُورِث الآخَر شَيئاً.
        Assert.Same(ThemeCatalog.Default, await svc.EffectiveAsync("ashare"));
        Assert.Same(ThemeCatalog.Default, await svc.EffectiveAsync("adwar-demo"));

        // والإبطال لِمِفتاح غائِب لا يَرمي ولا يَمَسّ غَيرَه.
        svc.Invalidate("adwar-demo");
        svc.Invalidate("tenant-that-never-existed");
        Assert.Same(ThemeCatalog.Default, await svc.EffectiveAsync("ashare"));
    }

    [Fact]
    public void ComposedSnapshotsOfTwoTenants_AreIndependentObjects()
    {
        var green = new TenantThemeDefinition
        {
            Id = "g", Slug = "g", Status = TenantThemeStatuses.Approved,
            DecidedAt = DateTime.UtcNow,
            DefinitionJson = """
            { "slug": "g", "label": { "ar": "أَخضَر" },
              "tokens": { "color.primary": "#14532D" } }
            """
        };

        var a = TenantThemeSet.FromDocuments("adwar-demo", new[] { green });
        var b = TenantThemeSet.FromDocuments("ashare", Array.Empty<TenantThemeDefinition>());

        Assert.Equal("#14532D", a.Theme["color.primary"]);
        // المُستَأجِر الثاني لَم يَتَلَوَّث: نَفس مَرجِع الافتِراضيّ.
        Assert.Same(ThemeCatalog.Default, b.Theme);
        Assert.Equal(ThemeCatalog.Default["color.primary"], b.Theme["color.primary"]);
        // والافتِراضيّ نَفسُه لَم يُعَدَّل في مَكانِه (لَو عُدِّلَ لَتَغَيَّرَ
        // مَظهَر كُلّ مَتجَر عَلى المَنصَّة بِوَثيقَة واحِدَة).
        Assert.NotSame(a.Theme, ThemeCatalog.Default);
    }
}
