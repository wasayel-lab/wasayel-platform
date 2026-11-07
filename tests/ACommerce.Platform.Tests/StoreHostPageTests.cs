using System.Text.RegularExpressions;
using ACommerce.Kit.Tenants;
using ACommerce.Kit.Tenants.Server;
using ACommerce.Templates.Customer.Marketplace.Services.Ux;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ الصَفحَةُ كَما يَراها المُتَصَفِّحُ عَلى مُضيفِ المَتجَر — G10 وG11 وG13 (‏2026-11-07، ADR-050 §٩ · W13-4·C2a·6) ═══
//
// **المَقيسُ قَبل** (A2 §3.3 و§3.5 عِندَ `b03b375`، وأُعيدَ عَدُّه عِندَ `31f8bc1`): الـcanonical وog:url يُبنَيانِ مِن `Request.Path` بَعدَ الحَقنِ فَيَصيرانِ
// `https://ashare.R/ashare/…`؛ و`OriginalPathItem` يَكتُبُه الحاقِنُ بِصِفرِ قارِئ؛ وأَربَعَةُ أَسطُرٍ في `App.razor` و`realtime-nav.js` تَستَخرِجُ السلاجَ مِن
// `location.pathname`؛ وخَريطَةُ المَوقِعِ عَلى مُضيفِ المَتجَرِ تَسرُدُ كُلَّ مَتجَر. **وبِلا نِطاقٍ مُهَيَّأٍ لا يَتَغَيَّرُ مِن الـcanonical حَرف.**

public class StoreHostPageTests(ITestOutputHelper output)
{
    private static string RepoRoot => ThemeZeroEquivalenceTests.RepoRoot;
    private const string Tpl = "libs/templates/ACommerce.Templates.Customer.Marketplace";

    // ─── G10 ──────────────────────────────────────────────────────────────────────────────────────────

    /// <summary><b>G10</b>: (المَسارُ الظاهِر، مَتجَرُ المُضيف) ← مَسارُ الـcanonical. بِلا مُضيفٍ هُوَ نَفسُه بِحَرفِه؛ وعَلى المُضيفِ يَسقُطُ <c>/{slug}</c> البادِئ.</summary>
    [Theory]
    [InlineData("/ashare/listings/3f2c", null, "/ashare/listings/3f2c")]
    [InlineData("/ashare", null, "/ashare")]
    [InlineData("/", null, "/")]
    [InlineData("/listings/3f2c", "ashare", "/listings/3f2c")]
    [InlineData("/", "ashare", "/")]
    [InlineData("/ashare/listings/3f2c", "ashare", "/listings/3f2c")]
    [InlineData("/ASHARE/explore", "ashare", "/explore")]
    [InlineData("/ashare", "ashare", "/")]
    [InlineData("/ashare-x/explore", "ashare", "/ashare-x/explore")]
    [InlineData("/r/vendor/me", "ashare", "/r/vendor/me")]
    public void Canonical_is_the_visible_url(string visible, string? hostSlug, string expected)
        => Assert.Equal(expected, StoreHostPage.CanonicalPath(visible, hostSlug));

    /// <summary>والمَسارُ الظاهِرُ ما كَتَبَه الحاقِنُ قَبلَ الحَقن، وبِلا حاقِنٍ مَسارُ الطَلَب.</summary>
    [Fact]
    public void The_visible_path_is_the_one_before_the_injection()
    {
        var hosted = new DefaultHttpContext();
        hosted.Request.Path = "/ashare/listings";
        hosted.Items[StoreHostItems.Slug] = "ashare";
        hosted.Items[StoreHostItems.OriginalPath] = "/listings";
        var plain = new DefaultHttpContext();
        plain.Request.Path = "/ashare/listings";
        output.WriteLine($"{StoreHostPage.VisiblePath(hosted)} · {StoreHostPage.HostSlug(hosted)} · {StoreHostPage.VisiblePath(plain)} · {StoreHostPage.HostSlug(plain) ?? "—"}");
        Assert.Equal(("/listings", "ashare"), (StoreHostPage.VisiblePath(hosted), StoreHostPage.HostSlug(hosted)));
        Assert.Equal(("/ashare/listings", (string?)null), (StoreHostPage.VisiblePath(plain), StoreHostPage.HostSlug(plain)));
    }

    /// <summary>و<c>OriginalPathItem</c> لَه قارِئٌ واحِدٌ عَلى الأَقَلّ (القاعِدَة ١)، والـcanonical وog:url في <c>App.razor</c> مِنه.</summary>
    [Fact]
    public void The_original_path_has_a_reader_and_the_shell_canonical_asks_it()
    {
        var readers = new List<string>();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(RepoRoot, "libs"), "*.*", SearchOption.AllDirectories)
                                      .Where(f => f.EndsWith(".cs", StringComparison.Ordinal) || f.EndsWith(".razor", StringComparison.Ordinal)))
        {
            var rel = Path.GetRelativePath(RepoRoot, file).Replace('\\', '/');
            if (rel.Contains("/bin/") || rel.Contains("/obj/")) continue;
            var code = WriteEndpointGuardTests.StripComments(File.ReadAllText(file));
            if (Regex.IsMatch(code, @"Items\[StoreHostItems\.OriginalPath\]\s+as\b")) readers.Add(rel);
        }
        output.WriteLine($"قُرّاءُ OriginalPath: {readers.Count} — {string.Join(" · ", readers)}");
        Assert.NotEmpty(readers);
        var app = File.ReadAllText(Path.Combine(RepoRoot, Tpl, "Components/App.razor"));
        Assert.Contains("var seoCanonical = seoBase.Length > 0 ? seoBase + seoVisible : null;", app, StringComparison.Ordinal);
        Assert.Contains("StoreHostPage.CanonicalPath(", app, StringComparison.Ordinal);
        Assert.Contains("<meta property=\"og:url\" content=\"@seoCanonical\" />", app, StringComparison.Ordinal);
    }

    // ─── G11 ──────────────────────────────────────────────────────────────────────────────────────────

    private static readonly Regex PathnameAlias = new(@"\b(?:var|let|const)\s+(?<v>\w+)\s*=\s*(?:window\.)?location\.pathname\s*;", RegexOptions.Compiled);

    /// <summary><b>أَسطُرٌ تَستَخرِجُ مِن المَسارِ مَقطَعاً</b>: <c>location.pathname.match(</c>/<c>.split(</c>، أَو اسمٌ أُسنِدَ إلَيه <c>location.pathname</c>
    /// ثُمَّ <c>.match(</c>/<c>.split(</c> عَلَيه. و<c>.test(</c> و<c>.startsWith(</c> و<c>.replace(</c> لا تَستَخرِجُ شَيئاً.</summary>
    internal static IReadOnlyList<string> SlugFromPathnameLines(string text)
    {
        var hits = new List<string>();
        var aliases = PathnameAlias.Matches(text).Select(m => m.Groups["v"].Value).Distinct().ToList();
        var lines = text.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var l = lines[i];
            if (l.TrimStart().StartsWith("//", StringComparison.Ordinal)) continue;
            if (Regex.IsMatch(l, @"location\.pathname\s*\.\s*(?:match|split)\s*\(")
                || aliases.Any(a => Regex.IsMatch(l, $@"\b{Regex.Escape(a)}\s*\.\s*(?:match|split)\s*\(")))
                hits.Add($"{i + 1}: {l.Trim()}");
        }
        return hits;
    }

    [Fact]
    public void No_client_script_reads_the_slug_from_location()
    {
        var scanned = 0;
        var hits = new List<string>();
        foreach (var dir in new[] { "libs", "apps" })
        foreach (var file in Directory.EnumerateFiles(Path.Combine(RepoRoot, dir), "*.*", SearchOption.AllDirectories)
                                      .Where(f => f.EndsWith(".razor", StringComparison.Ordinal) || f.EndsWith(".js", StringComparison.Ordinal)))
        {
            var rel = Path.GetRelativePath(RepoRoot, file).Replace('\\', '/');
            if (rel.Contains("/bin/") || rel.Contains("/obj/") || rel.Contains("/node_modules/")) continue;
            scanned++;
            hits.AddRange(SlugFromPathnameLines(File.ReadAllText(file)).Select(h => $"{rel}:{h}"));
        }
        output.WriteLine($"المِلَفّاتُ المَفحوصَة: {scanned} · أَسطُرٌ تَستَخرِجُ مِن location.pathname: {hits.Count}");
        foreach (var h in hits) output.WriteLine(h);
        Assert.True(scanned > 100, $"أَداةٌ عَمياء: {scanned} مِلَفّاً.");
        Assert.Empty(hits);
    }

    /// <summary><b>الماسِحُ يُقاسُ قَبلَ أَن يُوثَقَ بِه</b> (القاعِدَة ١٠): الأَسطُرُ الأَربَعَةُ الَّتي قاسَها A2 §3.5 بِحَرفِها (عِندَ <c>31f8bc1</c>) حَمراء، وما
    /// حَلَّ مَحَلَّها خَضراء.</summary>
    [Fact]
    public void The_scanner_goes_red_on_the_measured_lines()
    {
        const string before = """
            var match = location.pathname.match(/^\/([^\/]+)/);
            var path = location.pathname;
            var seg = path.split('/').filter(Boolean);
            var p = location.pathname;
            var segs = p.split('/').filter(Boolean);
            const path2 = window.location.pathname;
            const chat = path2.match(/^\/[^\/]+(?:\/r\/[^\/]+)?\/chats\/([0-9a-fA-F-]{36})\/?$/);
            """;
        const string after = """
            var slug = (window.acTenant || {}).slug;
            var p = location.pathname;
            var isTransient = /\/(login|verify)(\/|$|\?)/.test(p) || p.startsWith('/admin');
            const path = window.location.pathname;
            const rel = path.replace(new RegExp('^/' + slug + '(?=/|$)', 'i'), '');
            const chat = rel.match(/^(?:\/r\/[^\/]+)?\/chats\/([0-9a-fA-F-]{36})\/?$/);
            """;
        var red = SlugFromPathnameLines(before);
        output.WriteLine(string.Join("\n", red));
        Assert.Equal(4, red.Count);
        Assert.Empty(SlugFromPathnameLines(after));
    }

    /// <summary>والخادِمُ يُرسِلُ السِياقَ في الصَدَفَة، والكُتَلُ تَقرَؤُه.</summary>
    [Fact]
    public void The_shell_emits_the_tenant_context_and_its_blocks_read_it()
    {
        var app = File.ReadAllText(Path.Combine(RepoRoot, Tpl, "Components/App.razor"));
        Assert.Contains("window.acTenant = @((MarkupString)ACommerce.Templates.Customer.Marketplace.Services.Ux.StoreHostPage.ClientContextJson(", app, StringComparison.Ordinal);
        var readers = Regex.Matches(app, @"window\.acTenant\s*\|\|\s*\{\}").Count;
        output.WriteLine($"كُتَلٌ تَقرَأُ window.acTenant: {readers}");
        Assert.Equal(3, readers);
        Assert.Equal("""{"slug":"ashare","role":"vendor","hostMode":true,"home":false}""", StoreHostPage.ClientContextJson("ashare", "vendor", true, false));
        Assert.Equal("""{"slug":"","role":"","hostMode":false,"home":false}""", StoreHostPage.ClientContextJson(null, null, false, false));
        Assert.DoesNotContain("<", StoreHostPage.ClientContextJson("</script><x>", null, false, false), StringComparison.Ordinal);
    }

    // ─── G13 ──────────────────────────────────────────────────────────────────────────────────────────

    private static Tenant Store(string slug, params string[] categories) => new()
    {
        Id = slug, Name = slug,
        Categories = categories.Select((c, i) => new Category { Slug = c, SortOrder = i }).ToList(),
    };

    [Fact]
    public void The_store_host_entries_are_the_store_alone_at_its_clean_urls()
    {
        var entries = SeoDocuments.StoreHostEntries(Store("ashare", "cars", "homes"), "https://ashare.example.test/");
        foreach (var e in entries) output.WriteLine(e.Loc);
        Assert.Equal(new[] { "https://ashare.example.test/", "https://ashare.example.test/explore",
                             "https://ashare.example.test/explore?category=cars", "https://ashare.example.test/explore?category=homes" },
                     entries.Select(e => e.Loc));
        Assert.Empty(SeoDocuments.StoreHostEntries(null, "https://x.example.test"));
        Assert.Empty(SeoDocuments.StoreHostEntries(new Tenant { Id = "ashare", IsSuspended = true }, "https://x.example.test"));
    }

    /// <summary><b>G13 بِالمُعالِجِ نَفسِه</b>: عَلى مُضيفِ المَتجَرِ تَسرُدُ الخَريطَةُ مَتجَرَه وَحدَه — ومَتجَرٌ آخَرُ في القاعِدَةِ لا يَرِد.</summary>
    [Fact]
    public async Task Sitemap_on_a_store_host_lists_only_that_store()
    {
        var world = new DocWorld().Put(Store("ashare", "cars")).Put(Store("ejar", "homes"));
        var ctx = new DefaultHttpContext { RequestServices = new ServiceCollection().BuildServiceProvider() };
        ctx.Request.Scheme = "https";
        ctx.Request.Host = new HostString("ashare.example.test");
        ctx.Request.Path = "/sitemap.xml";
        ctx.Items[StoreHostItems.Slug] = "ashare";
        ctx.Items[StoreHostItems.OriginalPath] = "/sitemap.xml";
        var body = new MemoryStream();
        ctx.Response.Body = body;

        await SeoHandlers.Sitemap(ctx, world.Store);

        var xml = System.Text.Encoding.UTF8.GetString(body.ToArray());
        var locs = Regex.Matches(xml, "<loc>(?<l>[^<]+)</loc>").Select(m => m.Groups["l"].Value).ToList();
        foreach (var l in locs) output.WriteLine(l);
        Assert.Equal(new[] { "https://ashare.example.test/", "https://ashare.example.test/explore", "https://ashare.example.test/explore?category=cars" }, locs);
        Assert.DoesNotContain("ejar", xml, StringComparison.Ordinal);
    }
}
