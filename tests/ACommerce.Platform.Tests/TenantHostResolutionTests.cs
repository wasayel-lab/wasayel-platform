using ACommerce.Kit.Tenants;
using ACommerce.Platform.MultiTenancy;
using ACommerce.Templates.Customer.Marketplace;
using ACommerce.Kit.Auth;
using ACommerce.Kit.Auth.Server;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace ACommerce.Platform.Tests;

/// <summary>
/// <para><b>المُستَأجِرُ يُحَلُّ مِن المُضيفِ كَما يُحَلُّ مِن
/// المَسار — والمَسارُ يَبقى.</b> هذا المِلَفُّ هُوَ الحَدُّ
/// المَقيسُ لِـ<see cref="TenantResolverMiddleware.SlugFromHost"/>،
/// ولا يُشَغِّلُ مُضيفاً ولا قاعِدَةَ بَيانات — دالَّةٌ نَقِيَّةٌ
/// تُقاسُ نَقِيَّة، كَما تُقاسُ أُختُها
/// <c>SlugFromPath</c>.</para>
///
/// <para><b>وأَخطَرُ ما في هذا التَحويل</b>: أَنَّ المُضيفَ نَصٌّ
/// يُرسِلُه العَميل. فَما لَم يُشتَرَط أَن يَنتَهِيَ بِالنِطاقِ
/// الأَساسِ المُهَيَّأ، صارَ رَأسُ <c>Host</c> باباً يَختارُ بِه
/// أَيُّ أَحَدٍ سِياقَ أَيِّ مُستَأجِر — والمَنَصَّةُ تُشَغِّلُ
/// <c>ForwardedHeaders.XForwardedHost</c> بِلا وُكَلاءَ مَوثوقين
/// (‏<c>Program.cs</c>). فَالشَرطُ هُنا لَيسَ تَجميلاً؛ هُوَ
/// القُفل. و<see cref="A_host_outside_the_base_domain_resolves_no_tenant"/>
/// هُوَ البُرهان.</para>
/// </summary>
public class TenantHostResolutionTests
{
    private const string Base = "example.com";

    // ═══ ١) المَسارُ الطَبيعيّ: المَقطَعُ الأَوَّلُ مِن المُضيفِ سلاج ═══

    [Theory]
    [InlineData("ashare.example.com", "ashare")]
    [InlineData("theme-demo.example.com", "theme-demo")]
    [InlineData("ejar.example.com", "ejar")]
    [InlineData("owner-test.example.com", "owner-test")]
    public void The_first_label_is_the_slug(string host, string expected)
        => Assert.Equal(expected, TenantResolverMiddleware.SlugFromHost(host, Base));

    /// <summary><b>والمَنفَذُ وحالَةُ الحَرفِ والنُقطَةُ الأَخيرَةُ لا
    /// تُغَيِّرُ الجَواب</b> — المُتَصَفِّحُ يُرسِلُ المَنفَذَ في
    /// التَطوير، والـFQDN يَنتَهي بِنُقطَة، و<c>Host</c> غَيرُ
    /// حَسّاسٍ لِحالَةِ الحَرف.</summary>
    [Theory]
    [InlineData("ashare.example.com:5050", "ashare")]
    [InlineData("ASHARE.EXAMPLE.COM", "ashare")]
    [InlineData("Ashare.Example.Com:443", "ashare")]
    [InlineData("ashare.example.com.", "ashare")]
    public void The_port_and_the_letter_case_and_the_trailing_dot_do_not_change_the_answer(
        string host, string expected)
        => Assert.Equal(expected, TenantResolverMiddleware.SlugFromHost(host, Base));

    /// <summary>والنِطاقُ الأَساسُ نَفسُه يُطَبَّعُ — مُهَيِّئٌ
    /// يَكتُبُ <c>.Example.Com</c> لا يَكسِرُ الحَلّ.</summary>
    [Theory]
    [InlineData("EXAMPLE.COM")]
    [InlineData(".example.com")]
    [InlineData("example.com.")]
    [InlineData("  example.com  ")]
    public void The_base_domain_is_normalised_too(string baseDomain)
        => Assert.Equal("ashare",
            TenantResolverMiddleware.SlugFromHost("ashare.example.com", baseDomain));

    // ═══ ٢) الجَذرُ و«‏www‏» — لا سلاج ═══════════════════════════════

    [Theory]
    [InlineData("example.com")]
    [InlineData("example.com:443")]
    [InlineData("EXAMPLE.COM.")]
    public void The_apex_resolves_no_tenant(string host)
        => Assert.Null(TenantResolverMiddleware.SlugFromHost(host, Base));

    /// <summary><b>‏<c>www</c> يَبتَلِعُ الجَذر</b> إن لَم يُحجَز:
    /// زائِرُ <c>www.example.com</c> يَقَعُ في مَتجَرٍ اسمُه
    /// «‏www‏» — أَو في لا شَيء.</summary>
    [Theory]
    [InlineData("www.example.com")]
    [InlineData("WWW.example.com:8080")]
    public void The_www_label_resolves_no_tenant(string host)
        => Assert.Null(TenantResolverMiddleware.SlugFromHost(host, Base));

    // ═══ ٣) بابُ انتِحالِ المُستَأجِر — أَخطَرُ ما في التَغيير ═══════

    /// <summary>
    /// <para><b>مُضيفٌ خارِجَ النِطاقِ الأَساسِ لا يَحُلُّ
    /// مُستَأجِراً — أَبَداً.</b> والحالاتُ هُنا لَيسَت زينَة: كُلُّ
    /// واحِدَةٍ تَعبُرُ فَحصَ <c>EndsWith</c> الساذِجَ أَو تَبدو
    /// مَأنوسَةً لِلعَين.</para>
    ///
    /// <para><c>ashare.evil.com</c> نِطاقٌ أَجنَبِيٌّ كامِل؛
    /// و<c>example.com.evil.com</c> يَحمِلُ النِطاقَ الأَساسَ في
    /// وَسَطِه؛ و<c>notexample.com</c> و<c>xexample.com</c>
    /// <b>يَنتَهِيانِ بِـ<c>example.com</c> حَرفِيّاً</b> —
    /// فَ<c>EndsWith(baseDomain)</c> بِلا النُقطَةِ الفاصِلَةِ
    /// يَقبَلُهُما، وذاكَ بِعَينِه الثَغرَة.</para>
    /// </summary>
    [Theory]
    [InlineData("ashare.evil.com")]
    [InlineData("evil.com")]
    [InlineData("example.com.evil.com")]
    [InlineData("ashare.example.com.evil.com")]
    [InlineData("notexample.com")]
    [InlineData("xexample.com")]
    [InlineData("ashare.notexample.com")]
    [InlineData("ashare.example.como")]
    public void A_host_outside_the_base_domain_resolves_no_tenant(string host)
        => Assert.Null(TenantResolverMiddleware.SlugFromHost(host, Base));

    /// <summary><b>والعُمقُ الثاني لَيسَ مُستَأجِراً</b>: شَهادَةُ
    /// المُستَوى الأَوَّلِ لا تُغَطّيه أَصلاً، وقُبولُه يَفتَحُ
    /// أَسماءً لا تُحصى تَحتَ سلاجٍ واحِد.</summary>
    [Theory]
    [InlineData("a.b.example.com")]
    [InlineData("dev.ashare.example.com")]
    [InlineData("r.driver.example.com")]
    public void A_deeper_subdomain_resolves_no_tenant(string host)
        => Assert.Null(TenantResolverMiddleware.SlugFromHost(host, Base));

    // ═══ ٤) التَطويرُ يَعمَلُ بِلا نِطاق ════════════════════════════

    /// <summary><b>‏<c>localhost</c> والعَنوانُ الرَقَمِيُّ
    /// والمَنافِذُ لا تَكسِرُ شَيئاً</b> — تَحتَها يَبقى الحَلُّ
    /// بِالمَسارِ وَحدَه، وهُوَ ما يَعمَلُ اليَوم.</summary>
    [Theory]
    [InlineData("localhost")]
    [InlineData("localhost:5050")]
    [InlineData("127.0.0.1")]
    [InlineData("127.0.0.1:5050")]
    [InlineData("[::1]")]
    [InlineData("[::1]:5050")]
    [InlineData("0.0.0.0:80")]
    [InlineData("acommerceecommerce-acommerce-ecommerce.hf.space")]
    public void The_development_hosts_resolve_no_tenant(string host)
        => Assert.Null(TenantResolverMiddleware.SlugFromHost(host, Base));

    /// <summary><b>وبِلا نِطاقٍ أَساسٍ مُهَيَّأٍ لا يُحَلُّ مُضيفٌ
    /// قَطّ.</b> هذا هُوَ الوَضعُ الافتِراضيُّ اليَومَ (لا مِفتاحَ في
    /// <c>appsettings.json</c>) — فَالنَقلَةُ صِفريَّةُ الأَثَرِ
    /// حَتّى يُهَيَّأَ النِطاقُ عَمداً.</summary>
    [Theory]
    [InlineData((string?)null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(".")]
    public void With_no_configured_base_domain_no_host_resolves(string? baseDomain)
    {
        Assert.Null(TenantResolverMiddleware.SlugFromHost("ashare.example.com", baseDomain));
        Assert.Null(TenantResolverMiddleware.SlugFromHost("anything.at.all", baseDomain));
    }

    [Theory]
    [InlineData((string?)null)]
    [InlineData("")]
    [InlineData("   ")]
    public void An_empty_host_resolves_no_tenant(string? host)
        => Assert.Null(TenantResolverMiddleware.SlugFromHost(host, Base));

    // ═══ ٥) شَكلُ المِلصَقِ — ما لا يَصلُحُ اسمَ مُضيفٍ لا يُحَلّ ════

    /// <summary>
    /// <para><b>الشَرطَةُ طَرَفاً، والشَرطَةُ السُفلِيَّة، وما جاوَزَ
    /// ‏63 مِحرَفاً — كُلُّها مِلصَقاتٌ باطِلَةٌ في
    /// ‏RFC 1123.</b> وفاحِصُ شَكلِ السلاجِ عِندَ الإنشاءِ يَقبَلُ
    /// بَعضَها اليَوم (‏<c>^[a-z0-9_-]+$</c>) — فَالحَدُّ هُنا
    /// يَحرُسُ الحَلَّ ولَو أُنشِئَ الاسمُ سَلَفاً.</para>
    /// </summary>
    [Theory]
    [InlineData("-shop.example.com")]
    [InlineData("shop-.example.com")]
    [InlineData("_hidden.example.com")]
    [InlineData("my_shop.example.com")]
    [InlineData("--.example.com")]
    [InlineData("-.example.com")]
    public void A_label_that_is_not_a_valid_host_label_resolves_no_tenant(string host)
        => Assert.Null(TenantResolverMiddleware.SlugFromHost(host, Base));

    /// <summary>وحَدُّ الثَلاثَةِ والسِتّينَ مَفروضٌ — <b>‏63 يَمُرُّ
    /// و‏64 يُرَدّ</b>، فَالحَدُّ حَدٌّ لا تَقريب.</summary>
    [Fact]
    public void The_sixty_three_character_limit_is_enforced()
    {
        var ok = new string('a', 63);
        var tooLong = new string('a', 64);

        Assert.Equal(ok, TenantResolverMiddleware.SlugFromHost($"{ok}.{Base}", Base));
        Assert.Null(TenantResolverMiddleware.SlugFromHost($"{tooLong}.{Base}", Base));
    }

    // ═══ ٦) المَحجوزاتُ — مَصدَرٌ واحِدٌ لِلمِحوَرَين ═══════════════

    /// <summary>
    /// <para><b>كُلُّ اسمٍ مَحجوزٍ لا يُحَلُّ مِن المُضيفِ كَما لا
    /// يُحَلُّ مِن المَسار</b> — والفَحصُ يَدورُ على القائِمَةِ
    /// نَفسِها، فَاسمٌ يُضافُ غَداً مَحروسٌ بِلا لَمسِ هذا
    /// المِلَفّ.</para>
    ///
    /// <para><b>ويَطبَعُ عَدَدَ ما فَحَص</b> (القاعِدَة ١٠): قائِمَةٌ
    /// فارِغَةٌ تُعطي «صِفرَ مُخالَفَة» وهي عَمياء.</para>
    /// </summary>
    [Fact]
    public void Every_reserved_name_resolves_no_tenant_from_the_host()
    {
        Assert.NotEmpty(ReservedTenantSlugs.All);
        var checkedNames = 0;

        foreach (var name in ReservedTenantSlugs.All)
        {
            Assert.Null(TenantResolverMiddleware.SlugFromHost($"{name}.{Base}", Base));
            checkedNames++;
        }

        Assert.Equal(ReservedTenantSlugs.All.Count, checkedNames);
    }

    /// <summary><b>وأَسماءُ خِدماتِ النِطاقِ مَحجوزَةٌ صَراحَةً</b> —
    /// <c>www</c> يَبتَلِعُ الجَذر، و<c>mail</c>/<c>smtp</c>/
    /// <c>mx</c>/<c>autodiscover</c> وإخوانُها تَكسِرُ بَريدَ
    /// النِطاقِ لَو مُنِحَت مَتجَراً.</summary>
    [Theory]
    [InlineData("www")]
    [InlineData("mail")]
    [InlineData("smtp")]
    [InlineData("imap")]
    [InlineData("pop")]
    [InlineData("mx")]
    [InlineData("webmail")]
    [InlineData("ns1")]
    [InlineData("ns2")]
    [InlineData("ftp")]
    [InlineData("autoconfig")]
    [InlineData("autodiscover")]
    [InlineData("cdn")]
    [InlineData("static")]
    [InlineData("assets")]
    [InlineData("img")]
    [InlineData("media")]
    [InlineData("uploads")]
    [InlineData("branding")]
    public void The_service_names_of_a_domain_are_reserved(string name)
    {
        Assert.True(ReservedTenantSlugs.Contains(name), $"«{name}» غَيرُ مَحجوز.");
        Assert.Null(TenantResolverMiddleware.SlugFromHost($"{name}.{Base}", Base));
        Assert.Null(TenantResolverMiddleware.SlugFromPath($"/{name}"));
    }

    // ═══ ٧) المَسارُ يَبقى — لا يُحذَفُ ولا يَنكَسِر ════════════════

    /// <summary><b>الحَلُّ بِالمَسارِ لَم يُمَسّ.</b> هذا هُوَ شَرطُ
    /// المَوجَةِ الأَوَّل: يُضافُ المُضيفُ ولا يُحذَفُ
    /// المَسار.</summary>
    [Theory]
    [InlineData("/ashare", "ashare")]
    [InlineData("/ashare/listings", "ashare")]
    [InlineData("/ashare/r/driver/deals", "ashare")]
    [InlineData("/theme-demo/explore?q=1", "theme-demo")]
    public void The_path_resolution_is_untouched(string path, string expected)
        => Assert.Equal(expected, TenantResolverMiddleware.SlugFromPath(path));

    // ═══ ٨) حَقنُ المَقطَعِ — كَيفَ يَبقى كُلُّ ما بُنِيَ عامِلاً ═══

    /// <summary>
    /// <para><b>تَحتَ النِطاقِ الفَرعِيّ يُحقَنُ السلاجُ في أَوَّلِ
    /// المَسارِ قَبلَ التَوجيه</b> — فَتَبقى
    /// <c>RouteValues["slug"]</c> و<c>IsRouteArgumentNamed("slug")</c>
    /// و<c>ExtractRoleFromPath</c> وصَفَحاتُ Razor وقَوالِبُ
    /// المَساراتِ عامِلَةً <b>بِلا حَرفٍ واحِد</b>.</para>
    ///
    /// <para>والسَطرُ الأَخيرُ هُنا هُوَ الأَهَمّ: <c>/r/driver/deals</c>
    /// تَصيرُ <c>/ashare/r/driver/deals</c>، <b>فَعَزلُ الأَدوارِ
    /// يَبقى</b> بَدَلَ أَن يَسقُطَ صامِتاً إلى فَرعِ «أَيُّ كوكي
    /// دَور».</para>
    /// </summary>
    [Theory]
    [InlineData("/", "/ashare")]
    [InlineData("/listings", "/ashare/listings")]
    [InlineData("/listings/create", "/ashare/listings/create")]
    [InlineData("/r/driver/deals", "/ashare/r/driver/deals")]
    [InlineData("/login", "/ashare/login")]
    public void The_host_slug_is_injected_at_the_head_of_the_path(string path, string expected)
        => Assert.Equal(expected, TenantResolverMiddleware.PathWithSlug(path, "ashare"));

    /// <summary><b>والحَقنُ لا يَتَكَرَّر.</b> الرَوابِطُ المُطلَقَةُ
    /// اليَومَ <c>/{slug}/…</c> بِأَعدادِها، فَلَو حُقِنَ السلاجُ
    /// فَوقَ سلاجٍ لَصارَ <c>/ashare/ashare/listings</c> — وانكَسَرَ
    /// كُلُّ رابِطٍ قائِمٍ لَحظَةَ تَشغيلِ النِطاق. <b>هذا هُوَ ما
    /// يَجعَلُ المَسارَ والمُضيفَ يَعمَلانِ مَعاً لا
    /// بَدَلاً.</b></summary>
    [Theory]
    [InlineData("/ashare")]
    [InlineData("/ashare/")]
    [InlineData("/ashare/listings")]
    [InlineData("/ashare/r/driver/deals")]
    [InlineData("/ASHARE/listings")]
    public void An_already_prefixed_path_is_left_alone(string path)
        => Assert.Null(TenantResolverMiddleware.PathWithSlug(path, "ashare"));

    /// <summary>
    /// <para><b>والمَساراتُ المَحجوزَةُ تَبقى على الجَذر.</b> لَو
    /// حُقِنَ السلاجُ فيها لَصارَت <c>/ashare/css/site.css</c>
    /// و<c>/ashare/_framework/blazor.web.js</c>
    /// و<c>/ashare/uploads/x.png</c> — <b>فَتَسقُطُ الأَنماطُ
    /// والإطارُ والصُوَرُ مَعاً</b> تَحتَ كُلِّ نِطاقٍ فَرعِيّ. وهذا
    /// أَوَّلُ ما كانَ سَيَنكَسِر.</para>
    /// </summary>
    [Theory]
    [InlineData("/css/site.css")]
    [InlineData("/js/app.js")]
    [InlineData("/lib/bootstrap/x.css")]
    [InlineData("/_framework/blazor.web.js")]
    [InlineData("/_blazor")]
    [InlineData("/_content/pkg/x.css")]
    [InlineData("/favicon.ico")]
    [InlineData("/robots.txt")]
    [InlineData("/sitemap.xml")]
    [InlineData("/health")]
    [InlineData("/realtime")]
    [InlineData("/uploads/2026/x.png")]
    [InlineData("/branding/logo.svg")]
    [InlineData("/api/v1/deals")]
    [InlineData("/admin/tenants")]
    [InlineData("/studio/study")]
    public void A_reserved_path_is_never_prefixed(string path)
        => Assert.Null(TenantResolverMiddleware.PathWithSlug(path, "ashare"));

    /// <summary>وبِلا سلاجِ مُضيفٍ لا حَقنَ إطلاقاً — <b>الحالَةُ
    /// الافتِراضِيَّةُ اليَومَ</b>.</summary>
    [Theory]
    [InlineData((string?)null)]
    [InlineData("")]
    public void With_no_host_slug_nothing_is_injected(string? slug)
        => Assert.Null(TenantResolverMiddleware.PathWithSlug("/listings", slug));

    // ═══ ٩) الجَلسَةُ لا تَتَسَرَّبُ بَينَ نِطاقَينِ فَرعِيَّين ═════

    /// <summary>
    /// <para><b>مِلَفُّ الجَلسَةِ مَقصورٌ على مُضيفِه — ولا
    /// <c>Domain</c> فيه.</b> وهذا هُوَ الحَدُّ الَّذي يَمنَعُ
    /// تَسَرُّبَ الدُخولِ بَينَ مَتجَرَين لَحظَةَ صَيرورَتِهِما
    /// نِطاقَينِ فَرعِيَّين.</para>
    ///
    /// <para><b>والفَخُّ الَّذي يَحرُسُه هذا الاختِبار</b>: أَنَّ
    /// <c>Domain=".example.com"</c> هُوَ الإصلاحُ المُغري لِخُروجِ
    /// المُستَخدِمينَ بَعدَ النَقلَة — ولَو كُتِبَ لَصارَ كُلُّ
    /// نِطاقٍ فَرعِيٍّ يَستَقبِلُ كوكياتِ إخوَتِه <b>ويَستَطيعُ
    /// الكِتابَةَ فَوقَها</b> (تَثبيتُ جَلسَة). فَالسُكوتُ عَن هذا
    /// الحَدِّ لا يَحرُسُه؛ يُكتَبُ فَيُقاس.</para>
    /// </summary>
    [Fact]
    public void The_session_cookie_is_scoped_to_its_own_host()
    {
        var ctx = new DefaultHttpContext();
        AuthSession.WriteCookie(ctx.Response, "ashare",
            new AuthResult(Guid.NewGuid(), "بو خالِد", "0500000000",
                           AuthHandlers.MakeToken(Guid.NewGuid(), "ashare"), "host"));

        var setCookies = ctx.Response.Headers.SetCookie.ToArray();

        Assert.NotEmpty(setCookies);
        foreach (var header in setCookies)
            Assert.DoesNotContain("domain=", header!, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>ومَحوُ الجَلسَةِ كَذلِك — <b>كوكي يُكتَبُ بِلا
    /// نِطاقٍ ويُمحى بِنِطاقٍ لا يُمحى أَصلاً</b>.</summary>
    [Fact]
    public void The_cleared_cookie_is_scoped_to_its_own_host_too()
    {
        var ctx = new DefaultHttpContext();
        AuthSession.ClearCookie(ctx.Response, "ashare");

        var setCookies = ctx.Response.Headers.SetCookie.ToArray();

        Assert.NotEmpty(setCookies);
        foreach (var header in setCookies)
            Assert.DoesNotContain("domain=", header!, StringComparison.OrdinalIgnoreCase);
    }

    // ═══ ١٠) الزاحِفُ يُطابِقُ ما يَرى، لا ما يَصِلُ الخادِم ═══════

    /// <summary>
    /// <para><b>‏<c>robots.txt</c> يَمنَعُ بِالشَكلَين مَعاً.</b>
    /// الزاحِفُ يُطابِقُ القاعِدَةَ على المَسارِ الظاهِرِ في
    /// الرابِط، وحَقنُ السلاجِ يَقَعُ <b>داخِلَ الخادِم</b> ولا
    /// يَراهُ أَحَد. فَتَحتَ <c>{slug}.wasayel.tld</c> يَرى
    /// الزاحِفُ <c>/login</c>، و<c>Disallow: /*/login</c> لا
    /// يُطابِقُها.</para>
    ///
    /// <para><b>وهذا عَطَبٌ صامِتٌ بِامتِياز</b>: لا اختِبارَ
    /// يَحمَرُّ، ولا صَفحَةَ تَنكَسِر — فَقَط تُفهرَسُ صَفَحاتُ
    /// الدُخولِ والسَلَّةِ والدَفعِ يَوماً ما.</para>
    /// </summary>
    [Theory]
    [InlineData("login")]
    [InlineData("me")]
    [InlineData("cart")]
    [InlineData("checkout")]
    [InlineData("deals")]
    public void The_crawler_is_blocked_on_both_the_path_and_the_host_shape(string page)
    {
        var txt = SeoDocuments.BuildRobotsTxt("https://example.com");

        Assert.Contains($"Disallow: /*/{page}\n", txt);   // سلاجٌ في المَسار
        Assert.Contains($"Disallow: /{page}\n", txt);     // سلاجٌ في المُضيف
    }

    /// <summary>وكُلُّ مَسارٍ خاصٍّ مَحروسٌ بِالشَكلَين — <b>والفَحصُ
    /// يَدورُ على القائِمَةِ نَفسِها ويَطبَعُ عَدَدَ ما
    /// فَحَص</b>.</summary>
    [Fact]
    public void Every_private_tenant_path_is_blocked_on_both_shapes()
    {
        Assert.NotEmpty(SeoDocuments.DisallowedTenantPaths);

        var txt = SeoDocuments.BuildRobotsTxt("https://example.com");
        var checkedPaths = 0;

        foreach (var p in SeoDocuments.DisallowedTenantPaths)
        {
            Assert.Contains($"Disallow: /*/{p}\n", txt);
            Assert.Contains($"Disallow: /{p}\n", txt);
            checkedPaths++;
        }

        Assert.Equal(SeoDocuments.DisallowedTenantPaths.Length, checkedPaths);
    }

    /// <summary>
    /// <para><b>وحاجِزٌ ثانٍ لا يَتَّكِلُ على المُتَصَفِّح</b>: لَو
    /// بَلَغَ كوكي مَتجَرٍ مَتجَراً آخَرَ بِأَيِّ حيلَة — رَأسٌ
    /// مَنسوخٌ، أَو <c>Domain</c> يُكتَبُ يَوماً بِسَهو — فَالتوكِنُ
    /// يَحمِلُ سلاجَ صاحِبِه موَقَّعاً، ويُرَدّ.</para>
    /// </summary>
    [Fact]
    public void A_cookie_of_one_subdomain_is_refused_by_another()
    {
        var user = Guid.NewGuid();
        var ctx = new DefaultHttpContext();
        ctx.Request.Path = "/ejar/listings";
        ctx.Request.Headers["Cookie"] =
            $"{AuthSession.CookieName("ashare")}={AuthHandlers.MakeToken(user, "ashare")}; " +
            $"{AuthSession.CookieName("ejar")}={AuthHandlers.MakeToken(user, "ashare")}";

        Assert.Null(AuthSession.ResolveToken(ctx.Request, "ejar"));
    }
}
