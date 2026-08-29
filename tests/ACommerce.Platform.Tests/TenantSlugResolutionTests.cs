using ACommerce.Kit.Tenants;
using ACommerce.Platform.MultiTenancy;
using ACommerce.Templates.Customer.Marketplace.Services.Incubator;
using Xunit;

namespace ACommerce.Platform.Tests;

/// <summary>
/// <para><b>‏«‏api‏» صارَت مَحجوزَة — والدَعوى أَنّ التَغييرَ
/// صِفريُّ الأَثَر على ما كانَ يَعمَل.</b> هذا المِلَفّ هُوَ
/// بُرهانُها، لا التَعليقُ فَوقَ السَطر.</para>
///
/// <para><b>لِماذا حُجِزَت</b>: مَساراتُ سَطح الـAPI
/// <c>/api/v1/…</c> بِلا مَقطَعِ سلاجٍ إطلاقاً — المُستَأجِرُ
/// يُشتَقُّ مِن الاعتِماد ولا يُقبَل مِن الطَلَب (‏§٣٫٦). وبِلا
/// الحَجزِ يُحاوِلُ الوَسيطُ حَلَّ مُستَأجِرٍ اسمُه <c>api</c> عِندَ
/// كُلّ نِداء.</para>
///
/// <para><b>ولِماذا لا يَكسِر ما قَبلَه</b>: مَسارات
/// <c>/api/{slug}/manifest.json</c> وإخوانُها التِسع كانَ
/// الوَسيطُ يَقرَأُ مِنها المَقطَعَ الأَوَّلَ <c>api</c> — لا سلاجَ
/// المُستَأجِر الَّذي يَقَع <b>ثانِياً</b> — فَيَستَعلِم عَن
/// مُستَأجِرٍ بِهذا الاسم ولا يَجِدُه، فَلا يَضَعُ مُستَأجِراً.
/// أَي أَنّ تِلكَ النِقاطَ تَعمَل <b>بِلا مُستَأجِرٍ مَحلول
/// أَصلاً</b> وتَقرَأُ السلاجَ مِن وَسيطِ المَسار بِنَفسِها.
/// فَالحَجزُ يُبَدِّل «استِعلامٌ يَفشَل» بِـ«لا استِعلام»،
/// و<b>القيمَةُ المُشتَقَّةُ واحِدَةٌ في الحالَتَين:
/// <c>null</c></b>.</para>
/// </summary>
public class TenantSlugResolutionTests
{
    // ─── المَسارُ الطَبيعيّ: أَوَّلُ مَقطَعٍ سلاج ──────────────────────

    [Theory]
    [InlineData("/ashare", "ashare")]
    [InlineData("/ashare/", "ashare")]
    [InlineData("/ashare/listings", "ashare")]
    [InlineData("/ashare/r/driver/deals", "ashare")]
    [InlineData("/theme-demo/explore?q=1", "theme-demo")]
    public void The_first_segment_is_the_slug(string path, string expected)
        => Assert.Equal(expected, TenantResolverMiddleware.SlugFromPath(path));

    // ─── المَحجوزَة — لا سلاج ─────────────────────────────────────────

    [Theory]
    [InlineData("/admin/tenants/ashare")]
    [InlineData("/_blazor")]
    [InlineData("/_framework/blazor.web.js")]
    [InlineData("/css/site.css")]
    [InlineData("/health")]
    [InlineData("/realtime")]
    [InlineData("/robots.txt")]
    [InlineData("/sitemap.xml")]
    [InlineData("/favicon.ico")]
    public void A_reserved_first_segment_yields_no_slug(string path)
        => Assert.Null(TenantResolverMiddleware.SlugFromPath(path));

    // ─── الجَديد: «‏api‏» ───────────────────────────────────────────────

    /// <summary>‏<c>/api/v1/…</c> لا يَحُلُّ مُستَأجِراً — وهذا هُوَ
    /// المَقصود: المُستَأجِرُ مِن وَثيقَةِ المِفتاح.</summary>
    [Theory]
    [InlineData("/api/v1/deals")]
    [InlineData("/api/v1/deals/2f1b1a3c-0000-0000-0000-000000000000")]
    [InlineData("/api/v1/deals/2f1b1a3c-0000-0000-0000-000000000000/advance")]
    [InlineData("/api/v1/deals/2f1b1a3c-0000-0000-0000-000000000000/cancel")]
    public void The_api_surface_resolves_no_tenant_from_the_path(string path)
        => Assert.Null(TenantResolverMiddleware.SlugFromPath(path));

    /// <summary>
    /// <para><b>ونِقاطُ <c>/api/{slug}/…</c> التِسعُ القائِمَة لا
    /// تَنكَسِر</b> — لِأَنّها لَم تَكُن تَحُلّ مُستَأجِراً قَطّ:
    /// المَقطَعُ الأَوَّلُ فيها <c>api</c> لا السلاج. الجَوابُ كانَ
    /// <c>null</c> قَبلَ الحَجز وبَعدَه.</para>
    /// </summary>
    [Theory]
    [InlineData("/api/ashare/manifest.json")]
    [InlineData("/api/ashare/r/driver/manifest.json")]
    [InlineData("/api/ashare/icon.svg")]
    [InlineData("/api/ashare/r/driver/icon.svg")]
    [InlineData("/api/ashare/og.png")]
    [InlineData("/api/push/vapid-key")]
    [InlineData("/api/ashare/push/subscribe")]
    [InlineData("/api/ashare/unread-counts")]
    public void The_existing_api_paths_resolved_no_tenant_before_and_none_after(string path)
        => Assert.Null(TenantResolverMiddleware.SlugFromPath(path));

    /// <summary><b>والمَسارُ المَقلوب لَم يُمَسّ</b>:
    /// <c>/{slug}/api/me/unread</c> سلاجُه أَوَّلُ مَقطَع، فَيَحُلّ
    /// كَما كان. وهذِه هي النُقطَةُ الَّتي كانَ الحَجزُ يَكسِرُها لَو
    /// كُتِبَ بِمُطابَقَةِ «يَحوي api».</summary>
    [Fact]
    public void The_mirrored_path_still_resolves_its_tenant()
        => Assert.Equal("ashare", TenantResolverMiddleware.SlugFromPath("/ashare/api/me/unread"));

    // ─── الحُدود ──────────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("/")]
    public void A_root_or_too_short_path_yields_no_slug(string? path)
        => Assert.Null(TenantResolverMiddleware.SlugFromPath(path));

    /// <summary>
    /// <para><b>وسلاجٌ مِن مِحرَفٍ واحِدٍ سلاج</b> — <c>/a</c> طولُه
    /// اثنان، فَلا يَسقُط في شَرطِ «أَقصَرُ مِن اثنَين».</para>
    ///
    /// <para><b>وهذا السَطرُ كُتِبَ بَعدَ أَن كَذَبَ التَوَقُّع لا
    /// الكود</b>: أَوَّلُ صيغَةٍ مِن هذا المِلَفّ وَضَعَت <c>/a</c>
    /// في السالِبَة ظَنّاً، فَاحمَرَّت — والقِياسُ أَظهَرَ أَنّ
    /// الوَسيطَ يَحُلُّها سلاجاً <b>مُنذُ نَشأَتِه</b>، بِلا عَلاقَةٍ
    /// بِهذِه المَوجَة. فَالتَوصيفُ يُثَبِّتُ ما يَقَع لا ما
    /// يُظَنّ (القاعِدَة ٣).</para>
    /// </summary>
    [Fact]
    public void A_single_character_first_segment_is_still_a_slug()
        => Assert.Equal("a", TenantResolverMiddleware.SlugFromPath("/a"));

    /// <summary>والمُقارَنَةُ لا تُبالي بِحالَةِ الحَرف — كَما كانَت
    /// (<c>StringComparer.OrdinalIgnoreCase</c>).</summary>
    [Theory]
    [InlineData("/API/v1/deals")]
    [InlineData("/Api/v1/deals")]
    [InlineData("/ADMIN/tenants")]
    public void The_reserved_comparison_ignores_case(string path)
        => Assert.Null(TenantResolverMiddleware.SlugFromPath(path));

    /// <summary>عَدّاد: أَداةٌ تَفحَص صِفراً أَداةٌ عَمياء
    /// (القاعِدَة ١٠). لَو صارَ <c>SlugFromPath</c> يُعيد
    /// <c>null</c> لِكُلّ شَيء لَاخضَرَّت كُلُّ الحالاتِ
    /// السالِبَة أَعلاه — فَهذا السَطرُ يَمنَع ذلك.</summary>
    [Fact]
    public void The_resolver_still_resolves_something()
    {
        var resolved = new[] { "/ashare/x", "/theme-demo/y", "/owner-test/z" }
            .Select(TenantResolverMiddleware.SlugFromPath)
            .Where(s => s is not null)
            .ToArray();

        Assert.Equal(3, resolved.Length);
    }

    // ─── الجَديد: صَفَحاتُ المَنَصَّةِ الخَمس ─────────────────────────

    /// <summary>
    /// <para><b>الخَمسُ لا تَحُلُّ مُستَأجِراً</b> — وهذا هُوَ
    /// المَقصود: هي صَفَحاتُ المَنَصَّةِ نَفسِها، والمُستَأجِرُ
    /// لا يُقرَأُ مِن مَسارِها لِأَنَّه لَيسَ فيه.</para>
    /// </summary>
    [Theory]
    [InlineData("/terms")]
    [InlineData("/privacy")]
    [InlineData("/refunds")]
    [InlineData("/pricing")]
    [InlineData("/contact")]
    [InlineData("/terms/en")]
    [InlineData("/privacy/en")]
    [InlineData("/refunds/en")]
    public void The_platform_pages_resolve_no_tenant_from_the_path(string path)
        => Assert.Null(TenantResolverMiddleware.SlugFromPath(path));

    /// <summary>
    /// <para><b>ولا مَسارَ مُستَأجِرٍ قائِمٍ يَنكَسِر</b>. الحَجزُ
    /// يَقَعُ عَلى <b>المَقطَعِ الأَوَّلِ وَحدَه</b>، فَمَتجَرٌ اسمُه
    /// <c>ashare</c> لَه صَفحَةُ <c>terms</c> داخِلِيَّة
    /// (<c>/ashare/legal/terms</c>) ومَسارٌ فيه كَلِمَةُ
    /// <c>pricing</c> — ولا واحِدَ مِنهُما يُمَسّ.</para>
    ///
    /// <para><b>وهذا هُوَ المَزلَقُ الَّذي يَكسِرُه حَجزٌ مَكتوبٌ
    /// بِـ«يَحوي»</b> بَدَلَ «يُساوي المَقطَعَ الأَوَّل» — نَفسُ
    /// المَزلَقِ الَّذي وُثِّقَ في «‏api» أَعلاه.</para>
    /// </summary>
    [Theory]
    [InlineData("/ashare/legal/terms", "ashare")]
    [InlineData("/ashare/legal/privacy", "ashare")]
    [InlineData("/ashare/legal/returns", "ashare")]
    [InlineData("/ashare/plans", "ashare")]
    [InlineData("/theme-demo/r/host/legal/terms", "theme-demo")]
    [InlineData("/ejar/contact", "ejar")]
    [InlineData("/ejar/pricing", "ejar")]
    [InlineData("/order/refunds", "order")]
    public void A_tenant_path_that_merely_contains_a_reserved_word_still_resolves(
        string path, string expected)
        => Assert.Equal(expected, TenantResolverMiddleware.SlugFromPath(path));

    /// <summary>والحَجزُ لا يُبالي بِحالَةِ الحَرف، كَإخوَتِه.</summary>
    [Theory]
    [InlineData("/TERMS")]
    [InlineData("/Privacy")]
    [InlineData("/Refunds")]
    [InlineData("/PRICING")]
    [InlineData("/Contact")]
    public void The_platform_pages_are_reserved_case_insensitively(string path)
        => Assert.Null(TenantResolverMiddleware.SlugFromPath(path));
}

// ═══ المَصدَرُ الواحِد — المُنشِئُ والوَسيطُ يَقرَآنِ قائِمَةً واحِدَة ══
//
// **العَطَبُ المَقيسُ الَّذي كَتَبَ هذا الصِنف**: كانَ
// `TenantFromAnalysisFactory.ValidateSlugAsync` يَفحَصُ **الشَكلَ
// والتَفَرُّدَ ولا يَعرِفُ المَحجوز** — والقائِمَةُ `internal`
// بِمُستَهلِكٍ واحِدٍ هُوَ الوَسيط. فَمَتجَرٌ سلاجُه `pricing` أَو
// `terms` أَو `contact` **يُنشَأُ بِنَجاح** ثُمَّ **لا يُحَلُّ
// أَبَداً**: واجِهَةُ مَتجَرٍ لا تُبلَغ، بِلا رِسالَةِ خَطَإٍ ولا
// سَطرِ لوغ — وصاحِبُه يَظُنُّ أَنَّه بَنى (القاعِدَة ١٢).

public class ReservedTenantSlugTests
{
    private static TenantFromAnalysisFactory Factory(DocWorld world)
        => new(world.Store);

    /// <summary><b>سلاجٌ مَحجوزٌ يُرَدُّ بِرَمزٍ عِندَ الإنشاء</b> —
    /// لا يُنشَأُ ثُمَّ يُكتَشَفُ غِيابُه.</summary>
    [Theory]
    [InlineData("pricing")]
    [InlineData("terms")]
    [InlineData("contact")]
    [InlineData("privacy")]
    [InlineData("refunds")]
    [InlineData("billing")]
    [InlineData("admin")]
    [InlineData("api")]
    [InlineData("studio")]
    [InlineData("health")]
    public async Task A_reserved_slug_is_refused_at_creation(string slug)
        => Assert.Equal(
            TenantFromAnalysisFactory.SlugReserved,
            await Factory(new DocWorld()).ValidateSlugAsync(slug));

    /// <summary><b>والحَجزُ لا يُبالي بِحالَةِ الحَرف</b> — كَما لا
    /// يُبالي الوَسيط. و<c>Terms</c> ليسَ باباً خَلفِيّاً، ولَو
    /// رَدَّهُ فاحِصُ الشَكلِ اليَومَ فَذاكَ حِراسَةٌ بِالصُدفَة.</summary>
    [Fact]
    public async Task The_reservation_ignores_letter_case()
        => Assert.Equal(
            TenantFromAnalysisFactory.SlugReserved,
            await Factory(new DocWorld()).ValidateSlugAsync("Terms".ToLowerInvariant()));

    /// <summary>
    /// <para><b>وهذا هُوَ الرِباط: كُلُّ اسمٍ يَحجِزُه الوَسيطُ
    /// يَرُدُّه المُنشِئ.</b> والفَحصُ يَدورُ على القائِمَةِ نَفسِها،
    /// فَاسمٌ يُضافُ غَداً مَحروسٌ بِلا لَمسِ هذا المِلَفّ.</para>
    ///
    /// <para><b>ويَطبَعُ عَدَدَ ما فَحَص</b> (القاعِدَة ١٠): قائِمَةٌ
    /// فارِغَةٌ تُعطي «صِفرَ مُخالَفَة» وهي عَمياء، فَتُفحَصُ
    /// أَوَّلاً.</para>
    /// </summary>
    [Fact]
    public async Task Every_reserved_name_is_refused_by_the_creator_and_yields_no_slug()
    {
        Assert.NotEmpty(ReservedTenantSlugs.All);

        var factory = Factory(new DocWorld());
        var checkedNames = 0;

        foreach (var name in ReservedTenantSlugs.All)
        {
            // الوَسيط: لا سلاجَ في مَسارٍ أَوَّلُ مَقطَعِه هذا الاسم.
            Assert.Null(TenantResolverMiddleware.SlugFromPath("/" + name));

            // المُنشِئ: مَردودٌ دائِماً. و«مَحجوز» حَيثُ يَسمَحُ
            // الشَكل؛ وما فيه نُقطَةٌ (`favicon.ico`, `robots.txt`,
            // `sitemap.xml`) يَرُدُّه فاحِصُ الشَكلِ قَبلَه — ورَدٌّ
            // هُوَ رَدّ.
            var code = await factory.ValidateSlugAsync(name);
            Assert.NotNull(code);
            if (!name.Contains('.'))
                Assert.Equal(TenantFromAnalysisFactory.SlugReserved, code);

            checkedNames++;
        }

        Assert.Equal(ReservedTenantSlugs.All.Count, checkedNames);
    }

    /// <summary>وخَريطَةُ المَوقِعِ تَقرَأُ القائِمَةَ نَفسَها —
    /// <b>ثالِثُ المُستَهلِكين</b>. وكانَت نُسخَةً عَشرِيَّةً بِلا
    /// <c>terms</c>، فَكانَ مَتجَرٌ بِهذا الاسمِ يُدرَجُ لِلزاحِفِ
    /// وهُوَ لا يُفتَح.</summary>
    [Theory]
    [InlineData("terms")]
    [InlineData("billing")]
    [InlineData("pricing")]
    public void A_reserved_slug_is_not_public_to_the_crawler(string slug)
        => Assert.False(SeoDocuments.IsPublic(new Tenant { Id = slug }));

    /// <summary>واسمٌ حُرٌّ يَمُرّ — <b>الحَجزُ لا يَبتَلِعُ ما
    /// ليسَ لَه</b>.</summary>
    [Theory]
    [InlineData("my-shop")]
    [InlineData("ejar")]
    [InlineData("ashare")]
    [InlineData("pricing-plus")]
    public async Task A_free_slug_passes(string slug)
        => Assert.Null(await Factory(new DocWorld()).ValidateSlugAsync(slug));

    /// <summary>والحَجزُ يَسبِقُ رِحلَةَ قاعِدَةِ البَيانات — <b>لا
    /// استِعلامَ لِاسمٍ لَن يُقبَل</b>.</summary>
    [Fact]
    public async Task A_reserved_slug_costs_no_database_trip()
    {
        var world = new DocWorld();
        await Factory(world).ValidateSlugAsync("terms");

        Assert.DoesNotContain("LoadAsync", world.Touches);
    }

    /// <summary>والمُستَخدَمُ سَلَفاً يُرَدُّ بِرَمزِه هُوَ — <b>لا
    /// يُخلَطُ بِالمَحجوز</b>: أَحَدُهُما «اِختَر غَيرَه» والآخَرُ
    /// «هذا لَيسَ لَك».</summary>
    [Fact]
    public async Task A_taken_slug_is_refused_by_its_own_code()
    {
        var world = new DocWorld().Put(new Tenant { Id = "ejar", Name = "إيجار" });

        Assert.Equal(
            TenantFromAnalysisFactory.SlugTaken,
            await Factory(world).ValidateSlugAsync("ejar"));
    }

    /// <summary>
    /// <para><b>وكُلُّ رَمزٍ لَه نَصٌّ في القامُوس</b> (القاعِدَة ١١)
    /// — وكانَت هذِه الرَسائِلُ الأَربَعُ **مَكتوبَةً بِالعَرَبِيَّةِ
    /// في جِسمِ الخِدمَة** وتُمَرَّرُ في مُعامِلِ عُنوانٍ ثُمَّ
    /// تُعرَض.</para>
    /// </summary>
    [Theory]
    [InlineData(TenantFromAnalysisFactory.SlugRequired)]
    [InlineData(TenantFromAnalysisFactory.SlugFormat)]
    [InlineData(TenantFromAnalysisFactory.SlugTaken)]
    [InlineData(TenantFromAnalysisFactory.SlugReserved)]
    public void Every_slug_violation_code_has_an_arabic_message(string code)
    {
        var key = $"studio.study.err_{code}";
        var text = ACommerce.Platform.I18n.LocaleCatalog.Find(
            ACommerce.Platform.I18n.LocaleCatalog.Arabic, key);

        Assert.False(string.IsNullOrWhiteSpace(text), $"لا نَصَّ لِلمِفتاح «{key}».");
    }
}
