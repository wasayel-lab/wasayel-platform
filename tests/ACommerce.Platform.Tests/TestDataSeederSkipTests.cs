using System.Text.RegularExpressions;
using Xunit;

namespace ACommerce.Platform.Tests;

// ═══ `TestDataSeeder` يَتَخَطّى مُستَأجِراً غائِباً بِلا سَطر — والخُلاصَةُ «✅» بِلا عَدّ (القاعِدَة ١٠) ═══
//
// **المَقيس** (RELEASE-READINESS §٧·١): `Plan` أَربَعَةُ صُفوفٍ (`ashare`/`ejar`/`order`/`injez`)، و`PlatformSeed` يَبذُرُ ثَلاثَةً لا `injez`
// (لا بَذّارَ في `apps/V1.App/Seed` يُنشِئُه — `grep`)، فَعَلى قاعِدَةٍ مَحَلِّيَّةٍ مَبذورَةٍ `if (tenant is null) continue;` يَتَخَطّاه صامِتاً ثُمَّ
// يَطبَعُ «[TestSeed] ✅ test data seeded.» — لا يُمَيَّزُ مِن بَذّارٍ بَذَرَ الأَربَعَة. **ولَيسَ عَطَباً في القَصد**: البَذّارُ يَكتُبُ في
// المُستَأجِرينَ القائِمينَ ولا يُنشِئُهُم (‏ADR-003: «يَكتُب في المُستَأجِرينَ الحَقيقِيّين»)، و`injez` قائِمٌ حَيثُ يوجَد؛ وصَفُّه في `Plan`
// يَقرَؤُه `StudioBuildRoleFloorTests` (أَرضِيَّةُ البائِع). فَالعِلاجُ أَن يُقالَ ما تُخُطِّيَ ولِماذا، لا أَن يُختَرَعَ مُستَأجِر (القاعِدَة ١٦).
public class TestDataSeederSkipTests
{
    private static string Seeder()
    {
        var path = Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, "apps", "V1.App", "Seed", "TestDataSeeder.cs");
        Assert.True(File.Exists(path), "TestDataSeeder.cs مَفقود.");
        return File.ReadAllText(path);
    }

    /// <summary><b>قُلِبَ «Today_» بِاسمِه (‏2026-10-24، S15 F)</b>: الغائِبُ يُقالُ بِسَطرٍ يُسَمّيه وسَبَبِه قَبلَ التَخَطّي، والخُلاصَةُ تَعُدُّ
    /// ما بُذِرَ مِن <c>Plan</c> وتُسَمّي ما تُخُطِّي.</summary>
    [Fact]
    public void An_absent_tenant_is_named_with_its_reason_and_the_summary_counts_it()
    {
        var code = Seeder();
        Assert.DoesNotContain("if (tenant is null) continue;", code, StringComparison.Ordinal);
        var branch = code.IndexOf("if (tenant is null)", StringComparison.Ordinal);
        var cont = code.IndexOf("continue;", branch, StringComparison.Ordinal);
        Assert.True(branch > 0 && cont > branch);
        var body = code[branch..cont];
        Assert.Contains("skipped.Add(slug);", body, StringComparison.Ordinal);
        Assert.Contains("Console.WriteLine($\"[TestSeed] tenant '{slug}' غائِبٌ في هذِه القاعِدَة", body, StringComparison.Ordinal);
        Assert.Contains("{Plan.Length - skipped.Count} مِن {Plan.Length} مُستَأجِرين", code, StringComparison.Ordinal);
        Assert.Contains("تُخُطِّيَ الغائِب: {string.Join(\", \", skipped)}", code, StringComparison.Ordinal);
    }

    // ─── ‏2026-10-24 (تَيّارُ S16، مُراجَعَةُ S15 الجَولَة ١): الخُلاصَةُ «✅» ولَو لَم يُبذَر شَيء — تَوصيفُ «اليَوم» ───────────────
    //
    // **المَقروء** (`1c72873`): سَطرُ الخُلاصَةِ يَبدَأُ بِـ«✅ test data seeded» بِلا شَرط، والعَدُّ بَعدَه — فَقاعِدَةٌ لا مُستَأجِرَ فيها مِن الأَربَعَة
    // تَطبَعُ «✅ test data seeded — 0 مِن 4 مُستَأجِرين؛ تُخُطِّيَ الغائِب: …»: نَجاحٌ في رَأسِ سَطرٍ لَم يَكتُب حَرفاً (روحُ القاعِدَة ١٠).

    /// <summary>ما يُخالِفُ «العَلامَةُ تَسأَلُ العَدّ» في سَطرِ الخُلاصَة: سَطرٌ واحِدٌ يَبدَأُ بِـ<c>[TestSeed] </c>، عَلامَتُه تَعبيرٌ شَرطِيٌّ عَلى
    /// <c>Plan.Length - skipped.Count == 0</c> فَرعُه الصادِقُ «⚠» والكاذِبُ «✅» — لا «✅» حَرفِيٌّ قَبلَه.</summary>
    internal static IReadOnlyList<string> SummaryMarkViolations(string code)
    {
        var v = new List<string>();
        var lines = Regex.Matches(code, @"Console\.WriteLine\(\$""\[TestSeed\] (?<rest>[^\n]*)").Select(m => m.Groups["rest"].Value)
            .Where(r => r.Contains("مِن {Plan.Length} مُستَأجِرين", StringComparison.Ordinal)).ToList();
        if (lines.Count != 1) { v.Add($"summary_lines: {lines.Count}"); return v; }
        if (lines[0].StartsWith("✅", StringComparison.Ordinal)) v.Add("unconditional_success");
        else if (!lines[0].StartsWith("{(Plan.Length - skipped.Count == 0 ? \"⚠", StringComparison.Ordinal)
                 || !Regex.IsMatch(lines[0], @"^\{\(Plan\.Length - skipped\.Count == 0 \? ""⚠[^""]*"" : ""✅[^""]*""\)\}"))
            v.Add("mark_not_asking_the_count");
        return v;
    }

    /// <summary><b>قُلِبَ «Today_» بِاسمِه (‏2026-10-24، S16)</b> — كانَ <c>Today_the_summary_reads_success_even_when_nothing_was_seeded</c>
    /// (‏<c>94269fb</c>: «✅» بِلا شَرطٍ وصِفرُ «⚠»). والآن العَلامَةُ تَسأَلُ العَدّ: صِفرٌ ⇒ «⚠ test data NOT seeded».</summary>
    [Fact]
    public void The_summary_warns_when_nothing_was_seeded()
    {
        var code = Seeder();
        var v = SummaryMarkViolations(code);
        Assert.True(v.Count == 0, string.Join("; ", v));
        Assert.Contains("\"⚠ test data NOT seeded\" : \"✅ test data seeded\"", code, StringComparison.Ordinal);
    }

    /// <summary><b>حَقنُ عَيب — فاحِصُ العَلامَةِ يُحمِرُّ فِعلاً</b>: سَطرُ <c>94269fb</c> بِحَرفِه («✅» بِلا شَرط)، وشَرطٌ مَقلوب، وشَرطٌ عَلى
    /// غَيرِ العَدّ، وسَطرٌ ثانٍ — كُلٌّ يُرى؛ والحَيُّ يُقبَل.</summary>
    [Fact]
    public void The_summary_mark_checker_can_actually_go_red()
    {
        var code = Seeder();
        const string Live = "{(Plan.Length - skipped.Count == 0 ? \"⚠ test data NOT seeded\" : \"✅ test data seeded\")}";
        Assert.Contains(Live, code, StringComparison.Ordinal);
        var rejected = new (string Name, string Code)[]
        {
            ("«✅» بِلا شَرط (94269fb)", code.Replace(Live, "✅ test data seeded", StringComparison.Ordinal)),
            ("شَرطٌ مَقلوب", code.Replace(Live, "{(Plan.Length - skipped.Count == 0 ? \"✅ test data seeded\" : \"⚠ test data NOT seeded\")}", StringComparison.Ordinal)),
            ("شَرطٌ عَلى غَيرِ العَدّ", code.Replace(Live, "{(skipped.Count == 0 ? \"⚠ test data NOT seeded\" : \"✅ test data seeded\")}", StringComparison.Ordinal)),
            ("سَطرٌ ثانٍ", code.Replace("    private static async Task EnsureSampleListingAsync(",
                "    static void X(List<string> skipped) => Console.WriteLine($\"[TestSeed] ✅ — {Plan.Length - skipped.Count} مِن {Plan.Length} مُستَأجِرين\");\n    private static async Task EnsureSampleListingAsync(", StringComparison.Ordinal)),
        };
        var wrong = SummaryMarkViolations(code).Select(x => "رُفِضَ الحَيّ: " + x).ToList();
        foreach (var (name, c) in rejected)
        {
            var v = SummaryMarkViolations(c);
            if (v.Count == 0) wrong.Add($"قُبِلَ «{name}»");
        }
        Assert.True(rejected.Length == 4 && wrong.Count == 0, string.Join("\n", wrong));
    }

    /// <summary><b>ويَبقى بِحَرفِه</b>: لا بَذّارَ يُنشِئُ <c>injez</c> — فَالتَخَطّي واقِعٌ عَلى كُلِّ قاعِدَةٍ مَحَلِّيَّةٍ مَبذورَة.</summary>
    [Fact]
    public void No_seeder_creates_the_injez_tenant()
    {
        var dir = Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, "apps", "V1.App", "Seed");
        var files = Directory.GetFiles(dir, "*.cs");
        Assert.True(files.Length > 3, $"أَداةٌ عَمياء: {files.Length} بَذّاراً.");
        var mentions = files.Where(f => File.ReadAllText(f).Contains("\"injez\"", StringComparison.Ordinal))
            .Select(Path.GetFileName).ToList();
        Assert.Equal(new[] { "TestDataSeeder.cs" }, mentions);
    }
}
