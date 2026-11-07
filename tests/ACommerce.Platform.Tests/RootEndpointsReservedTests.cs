using System.Text.RegularExpressions;
using ACommerce.Kit.Tenants;
using ACommerce.Platform.MultiTenancy;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ كُلُّ جَذرٍ مَخدومٍ مَحجوز — G3 (‏2026-11-07، ADR-050 §٩ · W13-4·C2a·4) ═══
//
// **المَقيسُ قَبل** (A2 §2.2 عِندَ `b03b375`، وأُعيدَ هُنا بِالماسِحِ نَفسِه عِندَ `ecd351e`): ‏`/sw.js` و`/offline.html` و`/lang/{lang}` نِقاطٌ عَلى الجَذرِ
// (`MarketplaceTemplateExtensions`) **خارِجَ `ReservedTenantSlugs`** — فَعَلى مُضيفِ المَتجَرِ يَحقِنُها الوَسيطُ `/ashare/sw.js`… فَتُجابُ 404 ولا يُسَجَّلُ
// عامِلُ الخِدمَةِ قَطّ؛ وفي المَسارِ يُستَعلَمُ عَن مَتجَرٍ اسمُه «sw.js» في كُلِّ طَلَب. **والحَدُّ لا يَحفَظُه اسمُه** (القاعِدَة ٢): الماسِحُ يَجمَعُ المَقطَعَ
// الأَوَّلَ مِن كُلِّ `Map*("/…")` و`[Wolverine*("/…")]` و`@page "/…"` وكُلِّ مَدخَلٍ في جَذرِ `apps/V1.App/wwwroot` وكُلِّ أَصلٍ جَذرِيٍّ تُحَمِّلُه
// الصَفحَةُ (`src`/`href` إلى `_framework`/`_content`)، ويَطبَعُ عَدَدَه ويَفشَلُ عَلى الصِفر، ويَحمَرُّ عَلى جَذرٍ يُضافُ غَداً بِلا حَجز.

public class RootEndpointsReservedTests(ITestOutputHelper output)
{
    private static string RepoRoot => ThemeZeroEquivalenceTests.RepoRoot;

    private static readonly Regex MapRoute = new(@"\.Map(?:Get|Post|Put|Delete|Patch|Methods|Hub|Fallback)?(?:<[^>]+>)?\(\s*""/(?<s>[^""/?]*)", RegexOptions.Compiled);
    private static readonly Regex WolverineRoute = new(@"\[Wolverine(?:Get|Post|Put|Delete|Patch)\(\s*""/(?<s>[^""/?]*)", RegexOptions.Compiled);
    private static readonly Regex PageRoute = new(@"^@page\s+""/(?<s>[^""/?]*)", RegexOptions.Compiled | RegexOptions.Multiline);
    private static readonly Regex RootAsset = new(@"\b(?:src|href)=""/?(?<s>_[a-z]+)/", RegexOptions.Compiled);

    /// <summary>مِحوَرُ Blazor التَفاعُلِيُّ (<c>/_blazor</c>) يُسَجِّلُه الإطارُ لا سَطرٌ في المُستَودَع — يُذكَرُ بِاسمِه فَيُعَدّ.</summary>
    private static readonly string[] FrameworkRoots = ["_blazor"];

    /// <summary>المَقطَعُ الأَوَّلُ مِن كُلِّ جَذرٍ مَخدومٍ → مَواضِعُه.</summary>
    internal static SortedDictionary<string, List<string>> RootSegments()
    {
        var found = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);
        void Add(string seg, string where)
        {
            if (seg.Length == 0 || seg.StartsWith('{')) return;   // الجَذرُ `/` نَفسُه، وَ`/{slug}/…`
            if (!found.TryGetValue(seg, out var list)) found[seg] = list = [];
            list.Add(where);
        }

        foreach (var dir in new[] { "libs", "apps" })
        foreach (var file in Directory.EnumerateFiles(Path.Combine(RepoRoot, dir), "*.*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(RepoRoot, file).Replace('\\', '/');
            if (rel.Contains("/bin/") || rel.Contains("/obj/")) continue;
            if (rel.EndsWith(".cs", StringComparison.Ordinal))
            {
                var code = WriteEndpointGuardTests.StripComments(File.ReadAllText(file));
                foreach (Match m in MapRoute.Matches(code)) Add(m.Groups["s"].Value, rel);
                foreach (Match m in WolverineRoute.Matches(code)) Add(m.Groups["s"].Value, rel);
            }
            else if (rel.EndsWith(".razor", StringComparison.Ordinal))
            {
                var text = File.ReadAllText(file);
                foreach (Match m in PageRoute.Matches(text)) Add(m.Groups["s"].Value, rel);
                foreach (Match m in RootAsset.Matches(text)) Add(m.Groups["s"].Value, rel);
            }
        }

        foreach (var entry in Directory.EnumerateFileSystemEntries(Path.Combine(RepoRoot, "apps", "V1.App", "wwwroot")))
            Add(Path.GetFileName(entry), "apps/V1.App/wwwroot");
        foreach (var root in FrameworkRoots) Add(root, "Blazor");
        return found;
    }

    [Fact]
    public void Every_root_endpoint_and_root_static_entry_is_reserved()
    {
        var roots = RootSegments();
        foreach (var (seg, where) in roots)
            output.WriteLine($"{(ReservedTenantSlugs.Contains(seg) ? "✓" : "✗")} {seg}  ← {string.Join(" · ", where.Distinct().Take(3))}{(where.Distinct().Count() > 3 ? " …" : "")}");
        output.WriteLine($"الجُذورُ المَفحوصَة: {roots.Count} · خارِجَ المَحجوز: {roots.Keys.Count(s => !ReservedTenantSlugs.Contains(s))}");
        Assert.True(roots.Count > 10, $"أَداةٌ عَمياء: {roots.Count} جَذراً.");
        var unreserved = roots.Keys.Where(s => !ReservedTenantSlugs.Contains(s)).ToList();
        Assert.True(unreserved.Count == 0,
            "جَذرٌ مَخدومٌ خارِجَ ReservedTenantSlugs — عَلى مُضيفِ المَتجَرِ يُحقَنُ فيه السلاجُ فَيُجابُ 404، وفي المَسارِ يُقرَأُ مَتجَراً: " + string.Join(", ", unreserved));
    }

    /// <summary><b>الماسِحُ يُقاسُ قَبلَ أَن يُوثَقَ بِه</b> (القاعِدَة ١٠): يَجِدُ الجُذورَ الثَلاثَةَ الَّتي قاسَها A2 بِمَواضِعِها، ويَجِدُ مِن كُلِّ مَصدَرٍ واحِداً.</summary>
    [Theory]
    [InlineData("sw.js", "MarketplaceTemplateExtensions.cs")]
    [InlineData("offline.html", "MarketplaceTemplateExtensions.cs")]
    [InlineData("lang", "MarketplaceTemplateExtensions.cs")]
    [InlineData("robots.txt", "SeoHandlers.cs")]
    [InlineData("realtime", "Program.cs")]
    [InlineData("pricing", ".razor")]
    [InlineData("branding", "apps/V1.App/wwwroot")]
    [InlineData("_framework", "App.razor")]
    [InlineData("_blazor", "Blazor")]
    public void The_scanner_sees_each_kind_of_root(string segment, string source)
    {
        var roots = RootSegments();
        Assert.True(roots.TryGetValue(segment, out var where), $"«{segment}» لَم يُرَ");
        Assert.Contains(where!, w => w.Contains(source, StringComparison.Ordinal));
    }

    /// <summary>والجُذورُ الثَلاثَةُ لا تُقرَأُ مَتجَراً مِن المَسارِ ولا مِن المُضيف.</summary>
    [Theory]
    [InlineData("sw.js")]
    [InlineData("offline.html")]
    [InlineData("lang")]
    public void The_three_roots_are_not_a_store(string name)
    {
        Assert.True(ReservedTenantSlugs.Contains(name));
        Assert.Null(TenantResolverMiddleware.SlugFromPath($"/{name}"));
        Assert.Null(TenantResolverMiddleware.SlugFromHost($"{name}.example.test", "example.test"));
        Assert.Null(TenantResolverMiddleware.PathWithSlug($"/{name}", "ashare"));
    }
}
