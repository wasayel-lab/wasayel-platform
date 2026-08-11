using System.Text.Json;
using System.Xml.Linq;
using ACommerce.Kit.Tenants;
using Xunit;

namespace ACommerce.Platform.Tests;

// ─── اختِبارات وَثائِق الـ SEO ────────────────────────────────────────
// كُلّها بِلا قاعِدَة بَيانات: الدَوالّ نَقِيَّة تَأخُذ مُستَأجِرين
// وَرابِطاً وَتُعيد نَصّاً. ما يُحمى هُنا: ألّا يُسَرَّب مَتجَر مُعَلَّق
// أَو مَسار إداريّ إلى الزاحِف، وَألّا يَكسِر اِسم مَتجَر وَسم
// <script> في JSON-LD.

public class SeoDocumentsTests
{
    private const string Base = "https://wasayel.app";

    private static Tenant T(string slug, string name = "مَتجَر", string tag = "",
                            string city = "", bool suspended = false,
                            params string[] categories)
    {
        var t = new Tenant
        {
            Id = slug, Name = name, TagLine = tag, City = city, IsSuspended = suspended
        };
        foreach (var c in categories)
            t.Categories.Add(new Category { Slug = c, Label = c });
        return t;
    }

    // ─── الرابِط الأَساسيّ ───────────────────────────────────────────

    [Theory]
    [InlineData("https://wasayel.app/", "https://wasayel.app")]
    [InlineData("https://wasayel.app",  "https://wasayel.app")]
    [InlineData("",   "")]
    [InlineData(null, "")]
    public void NormalizeBaseUrl_DropsTrailingSlash(string? input, string expected)
        => Assert.Equal(expected, SeoDocuments.NormalizeBaseUrl(input));

    // ─── مَن يَظهَر لِلزاحِف ─────────────────────────────────────────

    [Fact]
    public void IsPublic_ExcludesSuspended_Reserved_AndUnderscorePrefixed()
    {
        Assert.True(SeoDocuments.IsPublic(T("ashare")));
        Assert.False(SeoDocuments.IsPublic(T("ashare", suspended: true)));
        Assert.False(SeoDocuments.IsPublic(T("_admin")));
        Assert.False(SeoDocuments.IsPublic(T("studio")));
        Assert.False(SeoDocuments.IsPublic(T("admin")));
        Assert.False(SeoDocuments.IsPublic(T("")));
    }

    // ─── خَريطَة المَوقِع ────────────────────────────────────────────

    [Fact]
    public void TenantEntries_ListsHomeAndExplore_ForEveryPublicTenant()
    {
        var entries = SeoDocuments.TenantEntries(
            new[] { T("ashare"), T("ejar") }, Base, includeCategories: false);

        var locs = entries.Select(e => e.Loc).ToList();
        Assert.Contains($"{Base}/ashare", locs);
        Assert.Contains($"{Base}/ashare/explore", locs);
        Assert.Contains($"{Base}/ejar", locs);
        Assert.Contains($"{Base}/ejar/explore", locs);
        Assert.Equal(4, locs.Count);
    }

    [Fact]
    public void TenantEntries_IncludesCategories_WhenAsked()
    {
        var entries = SeoDocuments.TenantEntries(
            new[] { T("ejar", categories: new[] { "apartment", "villa" }) }, Base);

        var locs = entries.Select(e => e.Loc).ToList();
        Assert.Contains($"{Base}/ejar/explore?category=apartment", locs);
        Assert.Contains($"{Base}/ejar/explore?category=villa", locs);
    }

    [Fact]
    public void TenantEntries_NeverLeaksSuspendedOrAdminTenants()
    {
        var entries = SeoDocuments.TenantEntries(
            new[] { T("ashare"), T("dead", suspended: true), T("_admin") }, Base);

        Assert.All(entries, e =>
        {
            Assert.DoesNotContain("/dead", e.Loc);
            Assert.DoesNotContain("/_admin", e.Loc);
        });
    }

    [Fact]
    public void TenantEntries_AreDistinct_EvenWithDuplicateInput()
    {
        var entries = SeoDocuments.TenantEntries(new[] { T("ashare"), T("ashare") }, Base);
        Assert.Equal(entries.Select(e => e.Loc).Distinct().Count(), entries.Count);
    }

    [Fact]
    public void TenantEntries_HandlesEmptyInput()
        => Assert.Empty(SeoDocuments.TenantEntries(Array.Empty<Tenant>(), Base));

    [Fact]
    public void BuildSitemapXml_IsWellFormed_WithCorrectNamespace()
    {
        var xml = SeoDocuments.BuildSitemapXml(
            SeoDocuments.TenantEntries(new[] { T("ashare") }, Base));

        var doc = XDocument.Parse(xml);   // يَفشَل لَو غَير سَليم
        XNamespace ns = "http://www.sitemaps.org/schemas/sitemap/0.9";
        Assert.Equal(ns + "urlset", doc.Root!.Name);
        Assert.NotEmpty(doc.Root.Elements(ns + "url"));
        Assert.All(doc.Root.Elements(ns + "url"),
            u => Assert.False(string.IsNullOrWhiteSpace((string?)u.Element(ns + "loc"))));
    }

    [Fact]
    public void BuildSitemapXml_EscapesAmpersandInCategoryQuery()
    {
        var xml = SeoDocuments.BuildSitemapXml(new[]
        {
            new SitemapEntry($"{Base}/ejar/explore?category=a&kind=b")
        });

        // الـ & الخام يَكسِر الـ XML — يَجِب أَن يُهرَب.
        Assert.DoesNotContain("category=a&kind", xml);
        var doc = XDocument.Parse(xml);
        XNamespace ns = "http://www.sitemaps.org/schemas/sitemap/0.9";
        Assert.Equal($"{Base}/ejar/explore?category=a&kind=b",
            (string?)doc.Root!.Element(ns + "url")!.Element(ns + "loc"));
    }

    [Fact]
    public void BuildSitemapXml_EmitsLastModOnlyWhenGiven()
    {
        var withOut = SeoDocuments.BuildSitemapXml(new[] { new SitemapEntry($"{Base}/a") });
        Assert.DoesNotContain("lastmod", withOut);

        var with = SeoDocuments.BuildSitemapXml(new[]
        {
            new SitemapEntry($"{Base}/a", new DateTime(2026, 8, 9))
        });
        Assert.Contains("<lastmod>2026-08-09</lastmod>", with);
    }

    [Fact]
    public void BuildSitemapXml_SkipsEmptyLoc()
    {
        var xml = SeoDocuments.BuildSitemapXml(new[]
        {
            new SitemapEntry(""), new SitemapEntry($"{Base}/a")
        });
        XNamespace ns = "http://www.sitemaps.org/schemas/sitemap/0.9";
        Assert.Single(XDocument.Parse(xml).Root!.Elements(ns + "url"));
    }

    // ─── robots.txt ─────────────────────────────────────────────────

    [Fact]
    public void BuildRobotsTxt_BlocksAdminStudioAndApi_AndPointsAtSitemap()
    {
        var txt = SeoDocuments.BuildRobotsTxt(Base);

        Assert.StartsWith("User-agent: *", txt);
        Assert.Contains("Disallow: /admin", txt);
        Assert.Contains("Disallow: /_admin", txt);
        Assert.Contains("Disallow: /studio", txt);
        Assert.Contains("Disallow: /api/", txt);
        Assert.Contains($"Sitemap: {Base}/sitemap.xml", txt);
    }

    [Fact]
    public void BuildRobotsTxt_BlocksPrivateTenantPaths()
    {
        var txt = SeoDocuments.BuildRobotsTxt(Base);
        Assert.Contains("Disallow: /*/login", txt);
        Assert.Contains("Disallow: /*/me", txt);
        Assert.Contains("Disallow: /*/deals", txt);
        Assert.Contains("Disallow: /*/checkout", txt);
    }

    [Fact]
    public void BuildRobotsTxt_OmitsSitemapLine_WhenBaseUrlUnknown()
        => Assert.DoesNotContain("Sitemap:", SeoDocuments.BuildRobotsTxt(""));

    // ─── نُصوص التَرويسَة ───────────────────────────────────────────

    [Fact]
    public void HomeTitle_IsNamePlusTagline_AndFallsBackGracefully()
    {
        Assert.Equal("أَشارِ · شَريك سَكَنِكَ",
            SeoDocuments.HomeTitle(T("ashare", "أَشارِ", "شَريك سَكَنِكَ")));
        Assert.Equal("أَشارِ", SeoDocuments.HomeTitle(T("ashare", "أَشارِ")));
        Assert.Equal("وَسايِل · Wasayel", SeoDocuments.HomeTitle(null));
        Assert.Equal("وَسايِل · Wasayel", SeoDocuments.HomeTitle(T("x", name: "")));
    }

    [Fact]
    public void HomeDescription_JoinsTaglineAndCity_AndSkipsMissingParts()
    {
        Assert.Equal("شَريك سَكَنِكَ — الرِياض",
            SeoDocuments.HomeDescription(T("a", "أَشارِ", "شَريك سَكَنِكَ", "الرِياض")));
        Assert.Equal("شَريك سَكَنِكَ",
            SeoDocuments.HomeDescription(T("a", "أَشارِ", "شَريك سَكَنِكَ")));
        // بِلا شِعار: يَسقُط عَلى الاسم.
        Assert.Equal("أَشارِ — الرِياض",
            SeoDocuments.HomeDescription(T("a", "أَشارِ", "", "الرِياض")));
        Assert.Equal("", SeoDocuments.HomeDescription(T("a", name: "")));
    }

    // ─── JSON-LD ────────────────────────────────────────────────────

    [Fact]
    public void BuildHomeJsonLd_HasOrganizationAndWebSite_InAGraph()
    {
        var json = SeoDocuments.BuildHomeJsonLd(
            T("ashare", "أَشارِ", "شَريك سَكَنِكَ", "الرِياض"), Base);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal("https://schema.org", root.GetProperty("@context").GetString());

        var graph = root.GetProperty("@graph").EnumerateArray().ToList();
        Assert.Equal(2, graph.Count);

        var types = graph.Select(g => g.GetProperty("@type").GetString()).ToList();
        Assert.Contains("Organization", types);
        Assert.Contains("WebSite", types);

        var org = graph.First(g => g.GetProperty("@type").GetString() == "Organization");
        Assert.Equal("أَشارِ", org.GetProperty("name").GetString());
        Assert.Equal($"{Base}/ashare", org.GetProperty("url").GetString());
        Assert.Equal("الرِياض",
            org.GetProperty("address").GetProperty("addressLocality").GetString());
    }

    [Fact]
    public void BuildHomeJsonLd_WebSiteReferencesOrganization_AndCarriesSearchAction()
    {
        var json = SeoDocuments.BuildHomeJsonLd(T("ashare", "أَشارِ"), Base);
        using var doc = JsonDocument.Parse(json);
        var graph = doc.RootElement.GetProperty("@graph").EnumerateArray().ToList();

        var org  = graph.First(g => g.GetProperty("@type").GetString() == "Organization");
        var site = graph.First(g => g.GetProperty("@type").GetString() == "WebSite");

        Assert.Equal(org.GetProperty("@id").GetString(),
                     site.GetProperty("publisher").GetProperty("@id").GetString());
        Assert.Equal("ar", site.GetProperty("inLanguage").GetString());

        var target = site.GetProperty("potentialAction").GetProperty("target");
        Assert.Equal($"{Base}/ashare/explore?q={{search_term_string}}",
                     target.GetProperty("urlTemplate").GetString());
    }

    [Fact]
    public void BuildHomeJsonLd_OmitsAddress_WhenCityUnknown()
    {
        var json = SeoDocuments.BuildHomeJsonLd(T("ashare", "أَشارِ"), Base);
        using var doc = JsonDocument.Parse(json);
        var org = doc.RootElement.GetProperty("@graph").EnumerateArray()
            .First(g => g.GetProperty("@type").GetString() == "Organization");
        Assert.False(org.TryGetProperty("address", out _));
    }

    [Fact]
    public void BuildHomeJsonLd_CannotCloseTheScriptTag()
    {
        // اِسم مَتجَر عَدائيّ — المُرَمِّز الافتِراضيّ يَجِب أَن يَهرُب < و >.
        var hostile = T("x", "</script><img src=x onerror=alert(1)>");
        var json = SeoDocuments.BuildHomeJsonLd(hostile, Base);

        Assert.DoesNotContain("</script>", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<img", json, StringComparison.OrdinalIgnoreCase);

        // ومَع ذلِك يَبقى JSON صالِحاً وَالقيمَة سَليمَة بَعد فَكّ التَرميز.
        using var doc = JsonDocument.Parse(json);
        var org = doc.RootElement.GetProperty("@graph").EnumerateArray()
            .First(g => g.GetProperty("@type").GetString() == "Organization");
        Assert.Equal(hostile.Name, org.GetProperty("name").GetString());
    }

    [Fact]
    public void BuildHomeJsonLd_EscapesSlugIntoUrls()
    {
        var json = SeoDocuments.BuildHomeJsonLd(T("my store", "اِسم"), Base);
        using var doc = JsonDocument.Parse(json);
        var org = doc.RootElement.GetProperty("@graph").EnumerateArray()
            .First(g => g.GetProperty("@type").GetString() == "Organization");
        Assert.Equal($"{Base}/my%20store", org.GetProperty("url").GetString());
    }

    // ─── الوَسم كامِلاً ──────────────────────────────────────────────

    /// <summary>
    /// نَوع المُحتَوى <b>رَمز ثابِت</b>: أَيّ هُروب فيه — ولَو كانَ
    /// <c>&amp;#x2B;</c> الَّذي يَفُكُّه مُحَلِّل مُطابِق لِلمُواصَفَة —
    /// يُخفي الكُتلَة عَن كُلّ مُستَهلِك يُطابِق النَصّ حَرفِيّاً. هذا
    /// بِالضَبط ما كانَ يَحدُث حينَ كُتِبَ الوَسم في Razor.
    /// </summary>
    [Fact]
    public void BuildHomeJsonLdScript_CarriesTheMimeTypeLiterally()
    {
        var html = SeoDocuments.BuildHomeJsonLdScript(T("ashare", "أَشارِ"), Base);

        Assert.StartsWith("<script type=\"application/ld+json\">", html, StringComparison.Ordinal);
        Assert.EndsWith("</script>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("&#x2B;", html, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>الغِلاف لا يَمَسّ الجِسم: ما بَينَ الوَسمَين هو
    /// <see cref="SeoDocuments.BuildHomeJsonLd"/> حَرفاً بِحَرف، وJSON
    /// صالِح بِـ <c>@context</c> و<c>@type</c>.</summary>
    [Fact]
    public void BuildHomeJsonLdScript_WrapsTheBodyUnchanged_AndStaysValidJson()
    {
        var t = T("ashare", "أَشارِ", "شَريك سَكَنِكَ", "الرِياض");
        var html = SeoDocuments.BuildHomeJsonLdScript(t, Base);

        const string open = "<script type=\"application/ld+json\">";
        var body = html[open.Length..^"</script>".Length];

        Assert.Equal(SeoDocuments.BuildHomeJsonLd(t, Base), body);

        using var doc = JsonDocument.Parse(body);
        Assert.Equal("https://schema.org", doc.RootElement.GetProperty("@context").GetString());
        Assert.All(doc.RootElement.GetProperty("@graph").EnumerateArray(),
            g => Assert.False(string.IsNullOrEmpty(g.GetProperty("@type").GetString())));
    }

    /// <summary>واِسم مَتجَر عَدائيّ لا يُغلِق الغِلاف — الجِسم مَهروب
    /// أَصلاً، فَالوَسم الخام يَبقى وَسماً واحِداً.</summary>
    [Fact]
    public void BuildHomeJsonLdScript_HostileNameCannotCloseTheWrapper()
    {
        var html = SeoDocuments.BuildHomeJsonLdScript(
            T("x", "</script><img src=x onerror=alert(1)>"), Base);

        // إغلاق واحِد فَقَط — وهو الَّذي كَتَبَه الغِلاف.
        Assert.Equal(1, System.Text.RegularExpressions.Regex.Matches(
            html, "</script>", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Count);
        Assert.DoesNotContain("<img", html, StringComparison.OrdinalIgnoreCase);
    }
}
