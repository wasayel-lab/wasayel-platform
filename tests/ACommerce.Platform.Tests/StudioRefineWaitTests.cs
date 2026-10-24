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
//
// ‏(‏2026-10-24، تَيّارُ S16 — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه): **قُلِبَ بِاسمِه** — ما فَوقَ هذا السَطرِ وَصفُ ما كانَ قَبلَه. النُقطَةُ تَكتُبُ لَحظَةَ
// الطَلَبِ (`since`) في الرابِطِ قَبلَ الإطلاق، والكُتلَتانِ (التَحديثُ والشَريط) تَسأَلانِ داخِلَهُما `StudioRefineWait.Pending` — دالَّةً نَقِيَّةً عَلى
// الرابِطِ و`UpdatedAt` الدِراسَةِ والساعَة، بِنافِذَةٍ هي مُهلَةُ نِداءِ الخَلفِيَّةِ نَفسُها. والشَرطانِ الخارِجِيّانِ بِحَرفِهِما (يُثَبِّتُهُما
// `StudioAnalysisDoorTests`). والصُفوفُ أَدناه تُثبِتُ كُلَّ فَرعٍ مِن فُروعِها مُوجِباً وسالِباً.
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

    internal const string BannerIf = "@if (!string.IsNullOrEmpty(Refining) && StudioAnalysisDoor.Opens(Incubator.IsConfigured))";

    /// <summary><b>قُلِبَ «Today_» بِاسمِه (‏2026-10-24، تَيّارُ S16)</b> — كانَ
    /// <c>Today_a_completed_refine_keeps_the_study_page_refreshing_every_four_seconds</c> (‏<c>3375ff1</c>): جِسمُ كُتلَةِ التَحديثِ وَسمُه مُباشَرَةً بِلا
    /// شَرطٍ ثانٍ، والرابِطُ <c>?refining={section}</c> وَحدَه. والآن: وَسمُ التَحديثِ وشَريطُ «جارٍ التَحسين» كِلاهُما داخِلَ <c>@if (RefinePending)</c>
    /// وَحدَه، و<c>RefinePending</c> هو <c>StudioRefineWait.Pending</c> عَلى الرابِطِ و<c>UpdatedAt</c> الدِراسَةِ والساعَة، والنُقطَةُ تَكتُبُ
    /// <c>since</c> قَبلَ <c>Task.Run</c> وتَحمِلُه في الرابِط.</summary>
    [Fact]
    public void A_refine_stops_refreshing_once_the_study_is_written_or_the_call_window_has_passed()
    {
        var page = Page();
        const string pending = "@if (RefinePending)";
        foreach (var (name, head, mark) in new[]
        {
            ("التَحديث", RefreshIf, "<meta http-equiv=\"refresh\" content=\"4\" />"),
            ("الشَريط", BannerIf, "studio.study.refining_prefix"),
        })
        {
            var outer = Block(page, head);
            var at = outer.IndexOf(pending, StringComparison.Ordinal);
            var markAt = outer.IndexOf(mark, StringComparison.Ordinal);
            output.WriteLine($"{name}: الشَرطُ الداخِلِيُّ عِندَ {at} · العَلامَةُ عِندَ {markAt} · طولُ الكُتلَة {outer.Length}");
            Assert.True(at >= 0, $"{name}: لا شَرطَ داخِلِيّ.");
            var inner = Block(outer, pending);
            Assert.Contains(mark, inner, StringComparison.Ordinal);
            Assert.Equal(1, Regex.Matches(outer, Regex.Escape(mark)).Count);
            var outside = outer.Replace(inner, "", StringComparison.Ordinal);
            Assert.DoesNotContain("<", outside, StringComparison.Ordinal);
            Assert.DoesNotContain("@L", outside, StringComparison.Ordinal);
        }
        Assert.Contains("bool RefinePending => session is not null && StudioRefineWait.Pending(Refining, Since, session.UpdatedAt, DateTime.UtcNow);",
            page, StringComparison.Ordinal);
        Assert.Contains("[SupplyParameterFromQuery(Name = \"since\")]     public long? Since { get; set; }", page, StringComparison.Ordinal);

        var ep = Endpoints();
        var since = ep.IndexOf("var since = DateTime.UtcNow.Ticks;", StringComparison.Ordinal);
        var run = ep.IndexOf("_ = Task.Run(", since < 0 ? 0 : since, StringComparison.Ordinal);
        var redirect = ep.IndexOf("return Results.Redirect($\"/studio/s/{id}?refining={section}&since={since}\");", StringComparison.Ordinal);
        output.WriteLine($"اللَحظَةُ عِندَ {since} · الإطلاقُ عِندَ {run} · الرَدُّ عِندَ {redirect}");
        Assert.True(since > 0 && since < run && run < redirect, "اللَحظَةُ لا تَسبِقُ الإطلاق، أَو الرابِطُ لا يَحمِلُها.");
        Assert.DoesNotContain("return Results.Redirect($\"/studio/s/{id}?refining={section}\");", ep, StringComparison.Ordinal);
    }

    private static readonly DateTime Now = new(2026, 10, 24, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>فُروعُ <see cref="ACommerce.Templates.Customer.Marketplace.Services.Incubator.StudioRefineWait.Pending"/> كُلُّها — مُوجِبٌ وسالِبٌ لِكُلِّ شَرط.
    /// الثَواني نِسبَةً إلى «الآن»: <c>sinceAgo</c> مُنذُ الطَلَب، و<c>writtenAgo</c> مُنذُ آخِرِ كِتابَةٍ لِلدِراسَة.</summary>
    [Theory]
    [InlineData("just_requested_not_written", "risks", 5.0, 60.0, true)]
    [InlineData("written_at_the_request_tick", "risks", 5.0, 5.0, true)]
    [InlineData("one_second_before_the_window", "risks", 419.0, 600.0, true)]
    [InlineData("written_after_the_request", "risks", 5.0, 2.0, false)]
    [InlineData("window_reached", "risks", 420.0, 600.0, false)]
    [InlineData("window_passed", "risks", 3600.0, 7200.0, false)]
    [InlineData("since_in_the_future", "risks", -30.0, 60.0, false)]
    [InlineData("no_section", "", 5.0, 60.0, false)]
    [InlineData("null_section", null, 5.0, 60.0, false)]
    [InlineData("no_since", "risks", null, 60.0, false)]
    public void Pending_is_true_only_while_the_study_is_unwritten_since_the_request_and_the_window_is_open(
        string name, string? refining, double? sinceAgo, double writtenAgo, bool expected)
    {
        long? since = sinceAgo is { } a ? (Now - TimeSpan.FromSeconds(a)).Ticks : null;
        var written = Now - TimeSpan.FromSeconds(writtenAgo);
        var got = ACommerce.Templates.Customer.Marketplace.Services.Incubator.StudioRefineWait.Pending(refining, since, written, Now);
        output.WriteLine($"{name}: {got}");
        Assert.Equal(expected, got);
    }

    /// <summary>النافِذَةُ هي مُهلَةُ نِداءِ الخَلفِيَّةِ بِعَينِها — لا رَقَمٌ ثانٍ يَنجَرِف.</summary>
    [Fact]
    public void The_wait_window_is_the_background_call_bound_itself()
    {
        var w = ACommerce.Templates.Customer.Marketplace.Services.Incubator.StudioRefineWait.Window;
        output.WriteLine($"النافِذَة: {w.TotalSeconds} ثانِيَة");
        Assert.Equal(ACommerce.Templates.Customer.Marketplace.Services.AgentCallBound.Background, w);
        Assert.Equal(420, w.TotalSeconds);
    }
}
