using System.Text.RegularExpressions;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ تَصديرُ بَياناتِ المُستَخدِمِ (PDPL) غائِب — وشَرطُ الإطلاقِ الَّذي يَنتَظِرُه لا يُنسى صامِتاً ═══
//
// تَدقيقُ الإطلاق ‏brief-P3-5b (كُبرى، ‏2026-10-24 عِندَ `dace7cd`؛ أُعيدَ قِياسُه عِندَ `ce026d5`): لا مَسارَ يُسَلِّمُ المُستَخدِمَ
// بَياناتِه (البَحثُ عَن `me/export|DataExport|ExportMyData|me/data` صِفر)، و`PRODUCTION-PLAN.md` §الإنتاج البَند ٤ يَعُدُّه «تَصدير/حَذف
// بَيانات المُستَخدِم (PDPL السعودي)» شَرطاً، و`RELEASE-READINESS.md` §١ قالَ «يَمنَعُ الإطلاقَ واحِد» بِلا صَفٍّ لَه — وخُطَّةُ المَوجاتِ
// أَسقَطَته بِلا ذِكر. والبِناءُ يَنتَظِرُ قَرارَ م‑٨ («ب» W8-4 · ٢ في سِجِلِّ المالِك)، فَلا يُبنى هُنا مَسار (القاعِدَة ١٦).
//
// **فَالمَحروسُ هُنا الصِدقُ لا البِناء** (القاعِدَة ٢): ما دامَ لا بابَ تَصديرٍ تَحتَ `/me` فَصَفُّ شَرطِ الإطلاقِ في §١ قائِمٌ بِحالِه
// «مَفتوح»؛ ويَومَ يولَدُ البابُ يُحمِرُّ هذا الصَفّ — فَيُقلَبُ صَفُّ §١ في الكوميتِ نَفسِه لا بَعدَه.
public class UserDataExportLaunchConditionTests(ITestOutputHelper output)
{
    /// <summary>بابُ تَصديرٍ يَراه المُستَخدِمُ لِبَياناتِه: مَسارٌ تَحتَ <c>/me</c> يُسَمّي تَصديراً أَو تَنزيلاً أَو بَياناتِه.</summary>
    internal static readonly Regex UserExport = new(@"/me(/|$).*(export|download|my-data|/data\b)|/me/(export|data)", RegexOptions.IgnoreCase);

    /// <summary>تَصديرانِ قائِمانِ لَيسا بابَ المُستَخدِمِ إلى بَياناتِه — بِسَبَبِهِما: تَصديرُ صاحِبِ المَتجَرِ لِمَتجَرِه
    /// (`docs/TENANT-EXPORT.md`)، وتَصديرُ دِراسَةِ الجَدوى لِصاحِبِها.</summary>
    internal static readonly Regex OwnerExport = new(@"^/studio/(apps/\{slug\}/export|s/\{[^}]+\}/export)", RegexOptions.IgnoreCase);

    internal const string LaunchRowMarker = "تَصديرُ بَياناتِ المُستَخدِمِ (PDPL) غائِب";
    internal const string OpenMarker = "شَرطُ إطلاقٍ مَفتوح";

    [Fact]
    public void No_user_data_export_door_exists_and_release_readiness_names_it_an_open_launch_condition()
    {
        var routes = ComplianceInspectorTests.RepoRoutes;
        var me = routes.Where(r => r.Contains("/me", StringComparison.Ordinal)).OrderBy(r => r, StringComparer.Ordinal).ToList();
        var userExports = routes.Where(r => UserExport.IsMatch(r)).ToList();
        var ownerExports = routes.Where(r => r.Contains("export", StringComparison.OrdinalIgnoreCase)).ToList();
        var unexplained = ownerExports.Where(r => !OwnerExport.IsMatch(r) && !UserExport.IsMatch(r)).ToList();

        var rr = File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, "docs", "RELEASE-READINESS.md"));
        var section1 = rr[rr.IndexOf("## ١.", StringComparison.Ordinal)..rr.IndexOf("## ٢.", StringComparison.Ordinal)];
        var row = section1.Split('\n').FirstOrDefault(l => l.StartsWith("|", StringComparison.Ordinal) && l.Contains(LaunchRowMarker, StringComparison.Ordinal));

        output.WriteLine($"مَساراتٌ تَحتَ /me: {me.Count} · بابُ تَصديرٍ لِلمُستَخدِم: {userExports.Count} ({string.Join(", ", userExports)})" +
                         $" · تَصديراتُ المالِكِ المُستَثناةُ بِسَبَبِها: {ownerExports.Count} ({string.Join(", ", ownerExports)}) · صَفُّ §١: {(row is null ? "غائِب" : "قائِم")}");

        Assert.True(me.Count >= 10, $"أَداةٌ عَمياء: {me.Count} مَساراً تَحتَ /me.");
        Assert.Empty(unexplained);
        // ‏2026-10-25 (مُراجَعَةُ تَيّارِ W8-4 الجَولَة ١، مَعلومَة): الاقتِرانُ دالَّةٌ لا `Assert.Empty(userExports)` غَيرُ مَشروط — كانَ الصَفُّ يُحمِرُّ
        // يَومَ يولَدُ البابُ أَيّاً كانَ صَفُّ §١، فَكوميتُ البابِ يُعَدِّلُ هذا الاختِبارَ حَتماً. والآن: بِلا بابٍ يَبقى الصَفُّ «مَفتوحاً»، وبِبابٍ
        // يُقلَبُ — فَيَمُرُّ الاختِبارُ بِلا تَعديلٍ يَومَ يَنزِلُ البابُ وقَلبُ الصَفِّ مَعاً، ويُحمِرُّ إن نَزَلَ أَحَدُهُما وَحدَه.
        Assert.True(row is not null, $"لا صَفَّ «{LaunchRowMarker}» في RELEASE-READINESS §١.");
        Assert.True(LaunchRowAgrees(userExports.Count, row),
            userExports.Count > 0 ? $"بابُ تَصديرٍ وُلِد ({string.Join(", ", userExports)}) وصَفُّ §١ ما زالَ «{OpenMarker}» — اقلِبه في الكوميتِ نَفسِه."
                                  : $"لا بابَ تَصدير وصَفُّ §١ لا يَقولُ «{OpenMarker}».");
    }

    /// <summary>صَفُّ §١ يُوافِقُ الكود: بِلا بابِ تَصديرٍ يَقولُ «مَفتوح»، وبِبابٍ لا يَقولُه. صَفٌّ غائِبٌ لا يُوافِقُ شَيئاً.</summary>
    internal static bool LaunchRowAgrees(int userExports, string? row)
        => row is not null && (userExports > 0) != row.Contains(OpenMarker, StringComparison.Ordinal);

    /// <summary>القاعِدَة ١٠ — الاقتِرانُ يُحمِرُّ في الحالَينِ المُتَنافِرَين: بابٌ وُلِدَ وصَفٌّ مَفتوح، وصَفٌّ قُلِبَ ولا باب.</summary>
    [Theory]
    [InlineData(0, "| ١٢ | تَصديرُ بَياناتِ المُستَخدِمِ (PDPL) غائِب | شَرطُ إطلاقٍ مَفتوح |", true)]
    [InlineData(1, "| ١٢ | تَصديرُ بَياناتِ المُستَخدِمِ (PDPL) غائِب | نُفِّذ |", true)]
    [InlineData(1, "| ١٢ | تَصديرُ بَياناتِ المُستَخدِمِ (PDPL) غائِب | شَرطُ إطلاقٍ مَفتوح |", false)]
    [InlineData(0, "| ١٢ | تَصديرُ بَياناتِ المُستَخدِمِ (PDPL) غائِب | نُفِّذ |", false)]
    [InlineData(0, null, false)]
    public void The_launch_row_agrees_with_the_door_in_both_directions(int userExports, string? row, bool agrees)
        => Assert.Equal(agrees, LaunchRowAgrees(userExports, row));

    /// <summary>الفاحِصُ يَرى ما يَعُدّ (القاعِدَة ١٠): أَشكالٌ مَحقونَةٌ يَلتَقِطُها، ونَظائِرُها المُشروعَةُ يَترُكُها.</summary>
    [Theory]
    [InlineData("/{slug}/me/export", true)]
    [InlineData("/{slug}/r/{role}/me/export.zip", true)]
    [InlineData("/{slug}/me/data", true)]
    [InlineData("/{slug}/me/download", true)]
    [InlineData("/studio/me/my-data", true)]
    [InlineData("/{slug}/me", false)]
    [InlineData("/{slug}/me/delete", false)]
    [InlineData("/{slug}/me/edit", false)]
    [InlineData("/studio/apps/{slug}/export", false)]
    public void The_export_door_pattern_sees_what_it_counts(string route, bool isUserExport)
        => Assert.Equal(isUserExport, UserExport.IsMatch(route));
}
