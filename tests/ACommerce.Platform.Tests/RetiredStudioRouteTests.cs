using System.Text.RegularExpressions;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ مَسارٌ كانَ في شَريطِ كُلِّ صَفحَةٍ لا يَصيرُ 404 فارِغاً (‏2026-11-07، مُراجَعَةُ عَدَساتِ C1 الجَولَةُ الثانِيَة · W13-1·R2D-4) ═══
//
// **المَقيسُ قَبل** (تَقريرُ العَدَسَة): `curl -D - /studio/ideas` ⇐ `HTTP/1.1 404` و`Content-Length: 0`؛ وفي Chrome «ERR_HTTP_RESPONSE_CODE_FAILURE» — صَفحَةُ
// خَطَإِ المُتَصَفِّحِ بِلا طَريقٍ إلى الاستوديو. وعِندَ `b03b375` كانَ `@page "/studio/ideas"` ثانِياً عَلى `StudioHome` ومَدخَلاً في شَريطِ كُلِّ صَفحَةِ استوديو،
// فَهُوَ في التاريخِ والعَلامات؛ ولَقطَةُ المَساراتِ لَم تَكُن تُسَمّيه فَلَم يَرَ خُروجَه أَحَد. **والآن** `GET /studio/ideas` ⇐ 302 إلى `/studio`، ولَقطَةُ
// المَساراتِ وسكربتُها يُسَمِّيانِه.

public class RetiredStudioRouteTests(ITestOutputHelper output)
{
    private static string Read(string rel) => File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, rel));

    /// <summary>المَساراتُ المُتَقاعِدَةُ الَّتي يَجِبُ أَن تُحَوَّلَ إلى وِجهَتِها — لا أَن تُرَدَّ 404.</summary>
    internal static readonly (string Route, string To)[] Retired = [("/studio/ideas", "/studio")];

    [Fact]
    public void Every_retired_studio_route_redirects_to_its_home_and_does_nothing_else()
    {
        foreach (var (route, to) in Retired)
        {
            var found = WriteEndpointGuardTests.AllMinimalApiEndpoints().Where(e => e.Route == route).ToList();
            output.WriteLine($"{route}: {found.Count} نُقطَة · {string.Join(" | ", found.Select(f => f.Body.Trim()))}");
            Assert.Single(found);
            Assert.Matches(@"^\.MapGet\(""" + Regex.Escape(route) + @""",\s*\(\)\s*=>\s*Results\.Redirect\(""" + Regex.Escape(to) + @"""\)\)", found[0].Body.Trim());
        }
    }

    /// <summary>والمَسارُ حَرفاً في مَوضِعٍ واحِدٍ مِن المَصدَر: التَحويلُ نَفسُه — لا رابِطَ يُرسَمُ إلَيه ولا بابَ آخَر. (وهو صَفُّ «اسمٌ مُتَقاعِدٌ لا يَعيشُ في
    /// مَصدَر» في <c>StudioAnalysisRetiredTests</c> بِشَرطٍ أَضيَق: كانَ «صِفرَ مَوضِع»، وصارَ «مَوضِعاً واحِداً هُوَ التَحويل».)</summary>
    [Fact]
    public void A_retired_route_lives_in_one_source_place_and_that_place_is_its_redirect()
    {
        foreach (var (route, _) in Retired)
        {
            var hits = StudioAnalysisRetiredTests.Sources().Where(s => s.Code.Contains($"\"{route}\"", StringComparison.Ordinal)).Select(s => s.File).ToList();
            output.WriteLine($"«\"{route}\"» في: {string.Join("، ", hits)}");
            Assert.Equal(["libs/templates/ACommerce.Templates.Customer.Marketplace/MarketplaceTemplateExtensions.cs"], hits);
            var code = StudioAnalysisRetiredTests.Sources().Single(s => s.File == hits[0]).Code;
            Assert.Equal(1, Regex.Matches(code, Regex.Escape($"\"{route}\"")).Count);
        }
    }

    /// <summary>ولا صَفحَةَ تَدَّعي المَسارَ نَفسَه (فَيَتَنازَعا)، ولَقطَةُ المَساراتِ وسكربتُها يُسَمِّيانِه بِجَوابِه.</summary>
    [Fact]
    public void A_retired_route_is_claimed_by_no_page_and_is_named_in_the_route_baseline()
    {
        var root = ThemeZeroEquivalenceTests.RepoRoot;
        var pages = Directory.EnumerateFiles(Path.Combine(root, "libs"), "*.razor", SearchOption.AllDirectories)
            .Where(f => !f.Contains("/bin/") && !f.Contains("/obj/")).ToList();
        Assert.True(pages.Count > 100, $"أَداةٌ عَمياء: {pages.Count} صَفحَة.");
        var baseline = Read("tests/characterization/routes/baseline.txt");
        var script = Read("scripts/characterize-routes.sh");
        foreach (var (route, _) in Retired)
        {
            Assert.DoesNotContain(pages, f => File.ReadAllText(f).Contains($"@page \"{route}\"", StringComparison.Ordinal));
            Assert.Matches(@"(?m)^GET\s+" + Regex.Escape(route) + @"\s+302\s", baseline);
            Assert.Contains($"\"GET  {route}\"", script, StringComparison.Ordinal);
        }
    }
}
