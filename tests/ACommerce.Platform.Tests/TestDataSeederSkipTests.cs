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
