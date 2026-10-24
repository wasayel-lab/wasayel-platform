using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ بابُ التَحليلِ في الاستوديو بِلا مِفتاح — بابٌ يُرسَمُ ولا يُفتَح (القاعِدَة ١٢) ═══
//
// **المَقيسُ حَيّاً (‏2026-10-24، قاعِدَةٌ مَحَلِّيَّةٌ جَديدَة، Development بِلا `Agents__Analysis__ApiKey` ولا `Agent__ApiKey`)**:
// ‏`GET /` يَرسُمُ استِمارَةً واحِدَةً إلى `/studio/begin`، و`POST /studio/begin` ‏302 → `/studio/auth`، ثُمَّ الدُخولُ
// والرَمزُ (‏302 → `/studio/consent`) والقَبولُ (‏302 → `/studio/s/{id}`) — وفي القاعِدَة: الجَلسَةُ `Failed` بِـ«anthropic
// (https://api.anthropic.com/) 401»، وسِجِلّا `modelcallrecord` لِلمُحاوَلَتَين. أَي أَنّ رائِدَ الأَعمالِ يَكتُبُ فِكرَتَه ويَدخُلُ
// ويَقبَلُ الشُروطَ ويُنفِقُ تَحليلاً مِن حِصَّتِه، ثُمَّ يُنادى المُزَوِّدُ بِلا مِفتاحٍ ويَقرَأُ «فَشِلَ التَّحليل» (‏RELEASE-READINESS §٣.١).
//
// **والجَذرُ مَقروء**: `FeasibilityAnalysisService.IsConfigured` تَقرَؤُه صَفحَتا الإدارَةِ وَحدَهُما (`Admin/IncubatorWizard.razor`،
// `Admin/IncubatorHome.razor`)؛ ولا يَسأَلُه مَدخَلٌ واحِدٌ في مَسارِ الاستوديو — بِخِلافِ البِطاقَةِ (`CheckoutPaymentPolicy.CardOffered`)
// والشَحنِ اللَذَينِ يُخفيهِما غِيابُ اعتِمادِهِما. هذا المِلَفُّ يُوَصِّفُ «اليَوم» (القاعِدَة ٣) قَبلَ أَيِّ تَبديل.
public class StudioAnalysisDoorTests(ITestOutputHelper output)
{
    private const string TemplateRoot = "libs/templates/ACommerce.Templates.Customer.Marketplace";

    private static string Read(string relative)
    {
        var path = Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, relative.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(path), $"مَصدَرٌ مَفقود: {relative} — الأَداةُ عَمياءُ بِلا طَرَفٍ مَقروء.");
        var text = File.ReadAllText(path);
        Assert.True(text.Length > 300, $"أَداةٌ عَمياء: {relative} طولُه {text.Length} مِحرَفاً.");
        return text;
    }

    private static string Endpoints() => Read($"{TemplateRoot}/MarketplaceTemplateExtensions.cs");

    private static string Page(string name) => Read($"{TemplateRoot}/Components/Pages/{name}");

    /// <summary>جِسمُ نُقطَةٍ مِن تَسجيلِها حَتّى <c>DisableAntiforgery</c> — والقَصُّ يَمنَعُ أَن يَخضَرَّ الفَحصُ بِنُقطَةٍ مُجاوِرَة.</summary>
    private static string MapBody(string route)
    {
        var all = Endpoints();
        var marker = $"MapPost(\"{route}\"";
        var start = all.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"نُقطَةُ {route} غَيرُ مَوجودَة — الأَداةُ تَقيسُ عَدَماً.");
        Assert.Equal(start, all.LastIndexOf(marker, StringComparison.Ordinal));
        var end = all.IndexOf("DisableAntiforgery", start, StringComparison.Ordinal);
        Assert.True(end > start, $"لَم يُغلَق جِسمُ {route} — القَصُّ فاشِل.");
        var body = all[start..end];
        Assert.True(body.Length > 200, $"جِسمُ {route} المَقصوصُ {body.Length} مِحرَفاً فَقَط — القَصُّ انزَلَق.");
        return body;
    }

    /// <summary>جِسمُ دالَّةٍ مِن تَوقيعِها حَتّى أَوَّلِ تَوقيعٍ بَعدَه — بِطولٍ مَطبوعٍ لا مُفتَرَض.</summary>
    private static string FunctionBody(string text, string signature, string next)
    {
        var start = text.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"الدالَّةُ غَيرُ مَوجودَة: {signature}");
        var end = text.IndexOf(next, start + signature.Length, StringComparison.Ordinal);
        Assert.True(end > start, $"لَم يُعثَر عَلى ما بَعدَ {signature} — القَصُّ فاشِل.");
        var body = text[start..end];
        Assert.True(body.Length > 300, $"جِسمُ {signature} {body.Length} مِحرَفاً — القَصُّ انزَلَق.");
        return body;
    }

    private string Resume()
        => FunctionBody(Endpoints(),
            "private static async Task<IResult?> ResumeStudioPromptAsync",
            "private static bool ");

    /// <summary>
    /// <para><b>تَوصيفُ «اليَوم» — يُقلَبُ بِاسمِه في كوميتِ الإصلاح</b>: مَداخِلُ الاستوديو الخَمسَةُ إلى التَحليلِ (صَفحَتا الفِكرَة،
    /// وزِرُّ الإعادَةِ في صَفحَةِ الدِراسَة، و<c>/studio/begin</c>، والاستِئنافُ، و<c>/studio/s/{id}/analyze</c>) لا تَسأَلُ واحِدَةٌ
    /// مِنها أَمُزَوِّدُ التَحليلِ مَضبوط — والاستِئنافُ يَبلُغُ <c>RunAnalysisAsync</c>. فَالاستِمارَةُ تُرسَمُ والنِداءُ يَخرُج.</para>
    /// </summary>
    [Fact]
    public void Today_no_studio_entry_asks_whether_the_analysis_backend_is_configured()
    {
        var pages = new[] { "Landing.razor", "StudioNewIdea.razor", "StudioStudy.razor" };
        foreach (var p in pages)
        {
            var text = Page(p);
            Assert.DoesNotContain("IsConfigured", text, StringComparison.Ordinal);
            output.WriteLine($"{p}: لا IsConfigured");
        }
        Assert.Contains("action=\"/studio/begin\"", Page("Landing.razor"), StringComparison.Ordinal);
        Assert.Contains("action=\"/studio/begin\"", Page("StudioNewIdea.razor"), StringComparison.Ordinal);
        Assert.Contains("/studio/s/{session.Id}/analyze", Page("StudioStudy.razor"), StringComparison.Ordinal);

        foreach (var route in new[] { "/studio/begin", "/studio/s/{id:guid}/analyze" })
        {
            Assert.DoesNotContain("IsConfigured", MapBody(route), StringComparison.Ordinal);
            output.WriteLine($"{route}: لا IsConfigured");
        }

        var resume = Resume();
        Assert.DoesNotContain("IsConfigured", resume, StringComparison.Ordinal);
        Assert.Contains("RunAnalysisAsync", resume, StringComparison.Ordinal);
        output.WriteLine("ResumeStudioPromptAsync: لا IsConfigured، ويَبلُغُ RunAnalysisAsync");
    }

    /// <summary>
    /// <para><b>والخِدمَةُ نَفسُها لا تَحرُسُ — فَالبابُ فَوقَها لا فيها</b>: <c>RunAnalysisAsync</c> يُنادي
    /// <c>_backend.CallAsync</c> بِلا سُؤالٍ عَن التَهيئَة، ويُحاوِلُ مَرَّتَين. ويَبقى بِحَرفِه بَعدَ الإصلاح: مَسارُ الإدارَةِ
    /// يُناديها أَيضاً، وصَفحَتاه تُخفِيانِ زِرَّهُما بِـ<c>IsConfigured</c> (الصَفُّ التالي).</para>
    /// </summary>
    [Fact]
    public void The_analysis_run_calls_the_backend_without_asking_its_configuration()
    {
        var svc = Read($"{TemplateRoot}/Services/Incubator/FeasibilityAnalysisService.cs");
        var run = FunctionBody(svc, "public async Task<IncubatorSession> RunAnalysisAsync", "private async Task SetStatusAsync");
        Assert.Contains("_backend.CallAsync(req, ct)", run, StringComparison.Ordinal);
        Assert.Contains("attempt < 2", run, StringComparison.Ordinal);
        Assert.DoesNotContain("IsConfigured", run, StringComparison.Ordinal);
    }

    /// <summary>
    /// <para><b>وصَفحَتا الإدارَةِ تَسأَلانِه مُنذُ قَبل</b> — السابِقَةُ الَّتي لا يَتبَعُها الاستوديو. ويَبقى بِحَرفِه بَعدَ الإصلاح.</para>
    /// </summary>
    [Fact]
    public void The_admin_incubator_pages_already_ask_the_configuration()
    {
        Assert.Contains("Incubator.IsConfigured", Page("Admin/IncubatorWizard.razor"), StringComparison.Ordinal);
        Assert.Contains("Incubator.IsConfigured", Page("Admin/IncubatorHome.razor"), StringComparison.Ordinal);
    }
}
