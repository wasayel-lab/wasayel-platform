using System.Text.RegularExpressions;
using ACommerce.Kit.Tenants;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

/// <summary>
/// <para><b>مَقاطِعُ صَفَحاتِ المَتجَرِ والإخوَةُ الحَرفِيّونَ لِـ<c>{slug}</c> مُشتَقَّةٌ مِن جَدوَلِ المَساراتِ لا مُختارَة</b> (‏2026-11-11، W13-4·C2z·4 — مُراجَعَةُ C2a ·
/// C2a-P2-02 وSEC-C2a-R2-3؛ ADR-050 §٣). الماسِحُ يَقرَأُ كُلَّ <c>@page</c> في <c>.razor</c> وكُلَّ <c>Map*("…")</c> و<c>[Wolverine*("…")]</c> في <c>.cs</c> تَحتَ
/// <c>libs/</c>، ويَستَخرِجُ (أ) أَوَّلَ مَقطَعٍ حَرفِيٍّ بَعدَ <c>/{slug}/</c>، و(ب) كُلَّ مَقطَعٍ حَرفِيٍّ في مَوضِعِ <c>{slug}</c> مِن <c>/admin/tenants/…</c>
/// و<c>/studio/apps/…</c> — ويَطبَعُ عَدَدَه، ويَحمَرُّ إن افتَرَقَ عَن <see cref="StoreHostName.InStoreSegments"/> و<see cref="StoreHostName.PlatformLiteralSiblings"/> في أَيِّ
/// اتِّجاه: صَفحَةٌ جَديدَةٌ بِمَقطَعٍ لَم يُحجَز، أَو مَحجوزٌ لا صَفحَةَ لَه (القاعِدَة ٢).</para>
/// </summary>
public class InStoreSegmentReservationTests(ITestOutputHelper output)
{
    private static readonly Regex Page = new(@"@page\s+""(?<r>[^""]+)""", RegexOptions.CultureInvariant);
    private static readonly Regex Map = new(@"\.Map(?:Get|Post|Put|Delete|Patch|Methods)\(\s*""(?<r>[^""]+)""|\[Wolverine(?:Get|Post|Put|Delete)\(""(?<r>[^""]+)""", RegexOptions.CultureInvariant);
    private static readonly Regex SlugParam = new(@"^\{[Ss]lug(:[^}]*)?\}$", RegexOptions.CultureInvariant);

    private static List<string> Routes()
    {
        var libs = Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, "libs");
        var routes = new List<string>();
        foreach (var f in Directory.EnumerateFiles(libs, "*.*", SearchOption.AllDirectories))
        {
            if (f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") || f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")) continue;
            if (f.EndsWith(".razor", StringComparison.Ordinal)) routes.AddRange(Page.Matches(File.ReadAllText(f)).Select(m => m.Groups["r"].Value));
            else if (f.EndsWith(".cs", StringComparison.Ordinal)) routes.AddRange(Map.Matches(File.ReadAllText(f)).Select(m => m.Groups["r"].Value));
        }
        return routes;
    }

    private static string[] Segments(string route) => route.Split('/', StringSplitOptions.RemoveEmptyEntries);

    [Fact]
    public void Every_first_in_store_segment_is_reserved_as_a_host_name_and_nothing_else()
    {
        var routes = Routes();
        var scanned = routes.Select(Segments)
            .Where(s => s.Length >= 2 && SlugParam.IsMatch(s[0]) && !s[1].StartsWith('{'))
            .Select(s => s[1].ToLowerInvariant()).ToHashSet(StringComparer.Ordinal);
        output.WriteLine($"مَساراتٌ مَمسوحَة: {routes.Count} · مَقاطِعُ صَفَحاتِ المَتجَرِ الأولى: {scanned.Count} · المَحجوزُ في المُصادِق: {StoreHostName.InStoreSegments.Count}");
        Assert.True(routes.Count >= 200, $"فَحصٌ أَعمى: {routes.Count} مَساراً");
        Assert.True(scanned.Count >= 20, $"فَحصٌ أَعمى: {scanned.Count} مَقطَعاً");
        Assert.Empty(scanned.Except(StoreHostName.InStoreSegments, StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal));
        Assert.Empty(StoreHostName.InStoreSegments.Except(scanned, StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Every_literal_sibling_of_a_store_in_the_admin_and_studio_routes_is_reserved_and_nothing_else()
    {
        var routes = Routes();
        string[][] prefixes = [["admin", "tenants"], ["studio", "apps"]];
        var withSlug = 0;
        var scanned = new HashSet<string>(StringComparer.Ordinal);
        foreach (var s in routes.Select(Segments))
            foreach (var p in prefixes)
            {
                if (s.Length < 3 || !string.Equals(s[0], p[0], StringComparison.OrdinalIgnoreCase) || !string.Equals(s[1], p[1], StringComparison.OrdinalIgnoreCase)) continue;
                if (SlugParam.IsMatch(s[2])) withSlug++;
                else if (!s[2].StartsWith('{')) scanned.Add(s[2].ToLowerInvariant());
            }
        output.WriteLine($"مَساراتٌ بِـ{{slug}} تَحتَ الإشرافِ والاستوديو: {withSlug} · إخوَةٌ حَرفِيّون: {string.Join(" ", scanned.Order(StringComparer.Ordinal))}");
        Assert.True(withSlug >= 20, $"فَحصٌ أَعمى: {withSlug}");
        Assert.Equal(scanned.Order(StringComparer.Ordinal), StoreHostName.PlatformLiteralSiblings.Select(x => x.ToLowerInvariant()).Order(StringComparer.Ordinal));
    }
}
