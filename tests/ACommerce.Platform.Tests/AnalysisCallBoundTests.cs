using System.Text.RegularExpressions;
using ACommerce.Templates.Customer.Marketplace.Services.Incubator;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ مُهلَةُ نِداءِ التَحليل — جَوابٌ مِن 8000 تَوكِنٍ تَحتَ مُهلَةِ 60 ثانِيَة ═══
//
// **المَقيس** (RELEASE-READINESS §٣.١، و«ج» S12·٥): `RunAnalysisAsync` يَطلُبُ جَواباً حَتّى `MaxTokens: 8000`، والعَميلُ في الخَلفِيّاتِ
// الثَلاثِ يَقطَعُ كُلَّ نِداءٍ عِندَ `HttpClient.Timeout` ‏60 ثانِيَة — ومُهلَةُ العَميلِ واحِدَةٌ لِكُلِّ نِداءٍ مِن كُلِّ وَكيل: مُحادَثَةُ
// الاستوديو الَّتي يَنتَظِرُها المُتَصَفِّحُ والتَحليلُ الَّذي لا يَنتَظِرُه أَحَد (يُطلَقُ بِـ`Task.Run` ويُرَدُّ الطَلَبُ فَوراً). وبَديلٌ يُجيبُ
// بَعدَ 65 ثانِيَة ← «فَشِلَ التَّحليل» بِنَصِّ المُهلَة بَعدَ 120 ثانِيَة (‏`rewalk-140207`، `rewalk-241004`). و`StaleAfter` ‏4 دَقائِق مُشتَقٌّ
// مِن ذلكَ الرَقَم (‏60 × مُحاوَلَتانِ × ضِعف). والتَشغيلاتُ الثَلاثُ في الخَلفِيَّةِ تَبتَلِعُ استِثناءَها بِـ`catch { }` بِلا سَطر.
//
// **صُفوفُ «Today_» تَوصيفٌ يُقلَبُ بِاسمِه** في كوميتِ الإصلاح (القاعِدَة ٣)، والباقي يَبقى بِحَرفِه.
public class AnalysisCallBoundTests(ITestOutputHelper output)
{
    private static string Src(params string[] parts)
    {
        var path = Path.Combine(new[] { ThemeZeroEquivalenceTests.RepoRoot }.Concat(parts).ToArray());
        Assert.True(File.Exists(path), $"مَفقود: {path}");
        return WriteEndpointGuardTests.StripComments(File.ReadAllText(path));
    }

    private const string Services = "libs/templates/ACommerce.Templates.Customer.Marketplace/Services";

    private static string Backends() => Src(Services, "AgentBackends.cs");
    private static string Analysis() => Src(Services, "Incubator", "FeasibilityAnalysisService.cs");
    private static string Studio() => Src(Services, "AgentService.cs");
    private static string Endpoints() =>
        Src("libs/templates/ACommerce.Templates.Customer.Marketplace", "MarketplaceTemplateExtensions.cs");

    /// <summary>جِسمُ الصَنفِ مِن تَعريفِه حَتّى تَعريفِ الصَنفِ التالي في المِلَفّ.</summary>
    private static string ClassBody(string code, string name)
    {
        var start = code.IndexOf($"public sealed class {name}", StringComparison.Ordinal);
        Assert.True(start >= 0, $"الصَنفُ غائِب: {name}");
        var next = code.IndexOf("public sealed class ", start + 1, StringComparison.Ordinal);
        return next < 0 ? code[start..] : code[start..next];
    }

    /// <summary>جِسمُ كُلِّ <c>_ = Task.Run(</c> حَتّى «<c>});</c>» الَّتي تُغلِقُه.</summary>
    private static IReadOnlyList<string> BackgroundRuns(string code, string call)
    {
        var runs = new List<string>();
        foreach (Match m in Regex.Matches(code, @"_ = Task\.Run\(async \(\) =>"))
        {
            var end = code.IndexOf("});", m.Index, StringComparison.Ordinal);
            Assert.True(end > m.Index, "Task.Run بِلا إغلاق.");
            var body = code[m.Index..end];
            if (body.Contains(call, StringComparison.Ordinal)) runs.Add(body);
        }
        return runs;
    }

    internal static readonly string[] BackendClasses = { "AnthropicBackend", "GeminiBackend", "OpenAIBackend" };

    /// <summary><b>توصيفُ اليَوم</b>: مُهلَةُ العَميلِ 60 ثانِيَة في كُلِّ خَلفِيَّة، ولا شَيءَ في الطَلَبِ يُمَيِّزُ نِداءً يَنتَظِرُه مُتَصَفِّحٌ
    /// مِن نِداءٍ في الخَلفِيَّة — فَالتَحليلُ والمُحادَثَةُ تَحتَ الرَقَمِ نَفسِه.</summary>
    [Fact]
    public void Today_every_backend_client_bounds_every_call_at_60_seconds()
    {
        var code = Backends();
        var all = Regex.Matches(code, @"Timeout\s*=").Count;
        output.WriteLine($"«Timeout =» في AgentBackends.cs: {all}");
        Assert.Equal(3, all);
        foreach (var name in BackendClasses)
            Assert.Contains("Timeout = TimeSpan.FromSeconds(60)", ClassBody(code, name));
        Assert.DoesNotContain("AgentCallWait", code);
    }

    /// <summary><b>توصيفُ اليَوم</b>: ‏<c>StaleAfter</c> ‏4 دَقائِق = مُحاوَلَتانِ × 60 ثانِيَة × ضِعف.</summary>
    [Fact]
    public void Today_StaleAfter_is_twice_two_calls_of_60_seconds()
    {
        Assert.Equal(TimeSpan.FromMinutes(4), FeasibilityAnalysisService.StaleAfter);
        Assert.Equal(TimeSpan.FromSeconds(2 * 60 * 2), FeasibilityAnalysisService.StaleAfter);
    }

    /// <summary><b>توصيفُ اليَوم</b>: التَشغيلاتُ الثَلاثُ في الخَلفِيَّةِ تَبتَلِعُ استِثناءَها بِلا سَطر — فَجَلسَةٌ تَبقى «جارِيَة» بِعِلَّةٍ
    /// خارِجَ النِداءِ لا يَقولُ سِجِلُّ الخادِمِ لِماذا.</summary>
    [Fact]
    public void Today_the_three_background_analysis_runs_swallow_their_exception_without_a_line()
    {
        var runs = BackgroundRuns(Endpoints(), "bg.RunAnalysisAsync(");
        output.WriteLine($"تَشغيلاتُ تَحليلٍ في الخَلفِيَّة: {runs.Count}");
        Assert.Equal(3, runs.Count);
        foreach (var r in runs)
            Assert.Matches(@"catch\s*\{\s*\}", r);
    }

    /// <summary><b>ويَبقى بِحَرفِه</b>: ما يَطلُبُه كُلُّ نِداء — التَحليلُ مُحاوَلَتانِ بِـ8000، والتَحسينُ 3000، والمُحادَثَةُ 2048.</summary>
    [Fact]
    public void The_analysis_tries_twice_for_8000_tokens_the_refine_asks_3000_and_the_chat_2048()
    {
        var a = Analysis();
        var run = a[a.IndexOf("public async Task<IncubatorSession> RunAnalysisAsync(", StringComparison.Ordinal)..];
        Assert.Matches(@"^[\s\S]*?attempt < 2 &&", run);
        Assert.Matches(@"^[\s\S]*?MaxTokens: 8000\b", run);
        var refine = a[a.IndexOf("public async Task RefineSectionAsync(", StringComparison.Ordinal)..];
        Assert.Matches(@"^[\s\S]*?MaxTokens: 3000\b", refine);
        Assert.Contains("MaxTokens: 2048", Studio());
    }

    /// <summary><b>ويَبقى بِحَرفِه</b>: لا طَلَبَ يَنتَظِرُ التَحليلَ ولا التَحسين — كِلاهُما في <c>Task.Run</c> والنُقطَةُ تُعيدُ تَحويلاً.</summary>
    [Fact]
    public void The_analysis_and_the_refine_run_in_the_background_and_no_request_waits_on_them()
    {
        var code = Endpoints();
        Assert.Equal(3, BackgroundRuns(code, "bg.RunAnalysisAsync(").Count);
        Assert.Single(BackgroundRuns(code, "bg.RefineSectionAsync("));
        Assert.Equal(3, Regex.Matches(code, @"await\s+\w+\.RunAnalysisAsync\(").Count);
        Assert.Single(Regex.Matches(code, @"await\s+\w+\.RefineSectionAsync\("));
    }
}
