using System.Text.RegularExpressions;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ صَفحَةُ الدِراسَةِ عَلى `?refining=` تُحَدِّثُ نَفسَها كُلَّ ٤ ثَوانٍ إلى الأَبَد — تَوصيفُ «اليَوم» (‏2026-10-24، تَيّارُ S16؛ مُراجَعَةُ S15 الجَولَة 2) ═══
//
// **المَقروء** (`1c72873`): نُقطَةُ `/studio/s/{id}/refine` تَرُدُّ إلى `/studio/s/{id}?refining={section}` ولا تَحمِلُ مَعَه لَحظَةَ الطَلَب، والصَفحَةُ
// تَرسُمُ `<meta http-equiv="refresh" content="4" />` وشَريطَ «جارٍ التَحسين» ما دامَ `Refining` غَيرَ فارِغٍ والدِراسَةُ مُكتَمِلَةً والبابُ مَفتوحاً —
// ولا شَرطَ يَسأَلُ أَكُتِبَ التَحسينُ أَم انقَضى وَقتُه. وتَحسينٌ اكتَمَلَ يُبقي الحالَ «مُكتَمِلَة» والمُعامِلَ في الرابِط، فَكُلُّ لِسانٍ مَفتوحٍ يَستَطلِعُ
// الخادِمَ إلى الأَبَدِ والشَريطُ لا يَزول (أَفادَ بِه التَيّارُ مَسأَلَةً مَفتوحَة). صَفُّ «Today_» يُقلَبُ بِاسمِه في كوميتِ الإصلاح.
public class StudioRefineWaitTests(ITestOutputHelper output)
{
    private static string Read(params string[] parts)
    {
        var path = Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, Path.Combine(parts));
        Assert.True(File.Exists(path), $"مَصدَرٌ مَفقود: {path}");
        var text = File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal);
        Assert.True(text.Length > 1000, $"أَداةٌ عَمياء: {path}.");
        return text;
    }

    private static string Page() => Read("libs", "templates", "ACommerce.Templates.Customer.Marketplace", "Components", "Pages", "StudioStudy.razor");
    private static string Endpoints() => Read("libs", "templates", "ACommerce.Templates.Customer.Marketplace", "MarketplaceTemplateExtensions.cs");

    internal const string RefreshIf = "@if (!string.IsNullOrEmpty(Refining) && session?.Status == IncubatorStatus.Completed && StudioAnalysisDoor.Opens(Incubator.IsConfigured))";

    /// <summary>كُتلَةُ <c>@if</c> المُسَمّاةِ بِحَرفِها — مِن قَوسِها المَفتوحِ إلى مُقابِلِه.</summary>
    internal static string Block(string text, string head)
    {
        var at = text.IndexOf(head, StringComparison.Ordinal);
        Assert.True(at >= 0, $"الكُتلَةُ غائِبَة: {head}");
        var open = text.IndexOf('{', at + head.Length);
        var depth = 0;
        for (var i = open; i < text.Length; i++)
        {
            if (text[i] == '{') depth++;
            else if (text[i] == '}' && --depth == 0) return text[(open + 1)..i];
        }
        Assert.Fail("كُتلَةٌ لا تُغلَق — القَصُّ فاشِل.");
        return "";
    }

    /// <summary><b>اليَوم: التَحديثُ الذاتِيُّ لا يَسأَلُ إلّا الرابِطَ والحالَ والباب</b> — جِسمُ كُتلَتِه وَسمُ التَحديثِ مُباشَرَةً بِلا شَرطٍ ثانٍ،
    /// والرابِطُ الَّذي تَرُدُّ إلَيه النُقطَةُ <c>?refining={section}</c> وَحدَه بِلا لَحظَة.</summary>
    [Fact]
    public void Today_a_completed_refine_keeps_the_study_page_refreshing_every_four_seconds()
    {
        var body = Block(Page(), RefreshIf).Trim();
        output.WriteLine($"جِسمُ كُتلَةِ التَحديث: {body}");
        Assert.Equal("@* Refine يَجري في الخَلفِيَّة — حَدِّث الصَفحَة كُلّ ٤ث حَتَّى تَتَحَدَّث Updated. *@\n    <HeadContent><meta http-equiv=\"refresh\" content=\"4\" /></HeadContent>", body);
        Assert.DoesNotContain("UpdatedAt", body, StringComparison.Ordinal);
        Assert.Contains("return Results.Redirect($\"/studio/s/{id}?refining={section}\");", Endpoints(), StringComparison.Ordinal);
        Assert.DoesNotMatch(new Regex(@"refining=\{section\}&"), Endpoints());
    }
}
