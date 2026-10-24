using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ تَرتيبُ البَذّاراتِ عِندَ الإقلاع — بَذّارا التَطويرِ يَسبِقانِ مَنحَ المُشرِفِ فَيَلزَمُ إقلاعٌ ثانٍ ═══
//
// **المَقيس** (RELEASE-READINESS §١ البَند ٨ و§٧): عَلى قاعِدَةٍ بارِدَةٍ يَطبَعُ الإقلاعُ «[IncubatorSample] لا مُستَخدِمَ استوديو — لا بَذرَة»
// و«[AppearanceSeed] لا مُستَخدِمَ استوديو — «theme-demo» بِلا مالِك» قَبلَ «[platform-admin] مُنِحَ الهاتِفُ …» — لِأَنّ `IncubatorSampleSeeder`
// و`AppearanceBaselineSeeder` يَسبِقانِ `PlatformAdminSeeder` في `Program.cs`، والمُشرِفُ هو مُستَخدِمُ الاستوديو الَّذي يَحتاجانِه. فَيُتِمُّ
// الإقلاعُ الثاني ما فاتَ الأَوَّل (مَقيسٌ ‏2026-10-24 عَلى `wasayel_s15e2`). **وكِلاهُما خَلفَ مُتَغَيِّرِ بيئَةٍ لا يُضبَطُ في الإنتاج**،
// فَتَرتيبُهُما لا يَمَسُّ بَذرَ الإنتاج — وهذا ما يَقيسُه الصَفُّ الثاني.
public class BootSeederOrderTests(ITestOutputHelper output)
{
    private static string Program()
    {
        var path = Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, "apps", "V1.App", "Program.cs");
        Assert.True(File.Exists(path), "Program.cs مَفقود.");
        return File.ReadAllText(path);
    }

    private static int At(string text, string needle)
    {
        var i = text.IndexOf(needle, StringComparison.Ordinal);
        Assert.True(i >= 0, $"غائِب: {needle}");
        Assert.Equal(i, text.LastIndexOf(needle, StringComparison.Ordinal));
        return i;
    }

    /// <summary><b>قُلِبَ «Today_» بِاسمِه (‏2026-10-24، S15 E)</b>: البَذّارانِ بَعدَ مَنحِ المُشرِف — فَإقلاعٌ بارِدٌ واحِدٌ يُتِمُّهُما.</summary>
    [Fact]
    public void The_two_dev_seeders_run_after_the_platform_admin_grant()
    {
        var p = Program();
        var admin = At(p, "await PlatformAdminSeeder.RunAsync(docStore, app.Environment)");
        Assert.True(At(p, "await IncubatorSampleSeeder.RunAsync(scope.ServiceProvider)") > admin);
        Assert.True(At(p, "await AppearanceBaselineSeeder.RunAsync(scope.ServiceProvider, app.Environment)") > admin);
    }

    /// <summary><b>ويَبقى بِحَرفِه</b>: بَذرُ الإنتاجِ (ما لا مُتَغَيِّرَ يَحرُسُه) بِتَرتيبِه — المَنَصَّة، ثُمَّ رَبطُ المِلكِيَّة، ثُمَّ مَنحُ المُشرِف —
    /// وكُلُّ بَذّارِ تَطويرٍ خَلفَ مُتَغَيِّرِه حَرفاً؛ فَما يُنقَلُ مِنها لا يَعمَلُ بِلا مُتَغَيِّرِه في أَيِّ مَوضِع.</summary>
    [Fact]
    public void Production_seeding_keeps_its_order_and_every_dev_seeder_stays_behind_its_variable()
    {
        var p = Program();
        var platform = At(p, "await PlatformSeed.RunAsync(scope.ServiceProvider);");
        var ownership = At(p, ".StudioOwnershipSeeder.RunAsync(docStore);");
        var admin = At(p, "await PlatformAdminSeeder.RunAsync(docStore, app.Environment)");
        Assert.True(platform < ownership && ownership < admin, $"تَرتيبُ بَذرِ الإنتاج: {platform} · {ownership} · {admin}");

        (string Guard, string Call)[] dev =
        {
            ("if (Environment.GetEnvironmentVariable(\"TEST_DATA_SEED\") == \"1\")", "await TestDataSeeder.RunAsync(scope.ServiceProvider);"),
            ("if (Environment.GetEnvironmentVariable(\"INCUBATOR_SAMPLE_SEED\") == \"1\")", "await IncubatorSampleSeeder.RunAsync(scope.ServiceProvider);"),
            ("if (Environment.GetEnvironmentVariable(\"CART_SAMPLE_SEED\") == \"1\")", "await CartSampleSeeder.RunAsync(scope.ServiceProvider);"),
            ("if (Environment.GetEnvironmentVariable(AppearanceBaselineSeeder.EnvVar) == \"1\")", "await AppearanceBaselineSeeder.RunAsync(scope.ServiceProvider, app.Environment);"),
        };
        foreach (var (guard, call) in dev)
        {
            var g = At(p, guard);
            var c = At(p, call);
            var between = p[(g + guard.Length)..c];
            output.WriteLine($"{call[6..call.IndexOf('(')]} خَلفَ مُتَغَيِّرِه: {string.IsNullOrWhiteSpace(between)}");
            Assert.True(string.IsNullOrWhiteSpace(between), $"«{call}» لَيسَ السَطرَ التالِيَ لِحارِسِه.");
        }
    }
}
