using System.Text.RegularExpressions;
using ACommerce.Templates.Customer.Marketplace.Services;
using ACommerce.Templates.Customer.Marketplace.Services.Incubator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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

    /// <summary><b>قُلِبَ «Today_» بِاسمِه (‏2026-10-24، S15 K)</b>: العَميلُ في كُلِّ خَلفِيَّةٍ بِلا مُهلَة، وكُلُّ نِداءٍ يَبدَأُ مُهلَتَه
    /// مِن <c>AgentRequest.Wait</c> ويُمَرِّرُها إلى الإرسالِ وقِراءَةِ الجِسمِ مَعاً، ويَقولُ انقِضاءَها بِنَصِّ المُهلَةِ قَبلَ الاستِثناءِ العامّ.</summary>
    [Fact]
    public void Every_backend_client_leaves_the_bound_to_the_request_and_each_call_starts_it()
    {
        var code = Backends();
        var all = Regex.Matches(code, @"Timeout\s*=").Count;
        output.WriteLine($"«Timeout =» في AgentBackends.cs: {all}");
        Assert.Equal(3, all);
        foreach (var name in BackendClasses)
        {
            var body = ClassBody(code, name);
            Assert.Contains("Timeout = System.Threading.Timeout.InfiniteTimeSpan", body);
            var start = body.IndexOf("using var bound = AgentCallBound.Start(req.Wait, ct);", StringComparison.Ordinal);
            Assert.True(start >= 0, $"{name}: النِداءُ لا يَبدَأُ مُهلَتَه.");
            Assert.Matches(@"await (Http|_http)\.(SendAsync\(http|PostAsJsonAsync\(url, body, JsonOpts), bound\.Token\)", body);
            Assert.Contains("ReadAsStringAsync(bound.Token)", body);
            Assert.DoesNotContain("ReadAsStringAsync(ct)", body);
            var timeout = body.IndexOf("catch (Exception ex) when (AgentCallBound.Expired(ex, bound, ct))", StringComparison.Ordinal);
            var general = body.IndexOf("AgentErrorText.Exception(ProviderName, Endpoint, ex.Message)", StringComparison.Ordinal);
            Assert.True(start < timeout && timeout < general, $"{name}: فَرعُ المُهلَةِ لا يَسبِقُ الاستِثناءَ العامّ.");
            Assert.Contains("AgentErrorText.Timeout(ProviderName, Endpoint, req.Wait)", body);
        }
    }

    /// <summary><b>قُلِبَ «Today_» بِاسمِه (‏2026-10-24، S15 K)</b>: ‏<c>StaleAfter</c> = مُحاوَلاتُ الحَلقَةِ × مُهلَةُ الخَلفِيَّةِ × ضِعف —
    /// القاعِدَةُ نَفسُها عَلى الرَقَمِ الجَديد، وأَطوَلُ مِن أَقصى عُمرِ تَشغيلَةٍ حَيَّة.</summary>
    [Fact]
    public void StaleAfter_is_twice_the_analysis_attempts_times_the_background_bound()
    {
        var a = Analysis();
        var run = a[a.IndexOf("public async Task<IncubatorSession> RunAnalysisAsync(", StringComparison.Ordinal)..];
        var attempts = int.Parse(Regex.Match(run, @"attempt < (\d+) &&").Groups[1].Value);
        var live = attempts * AgentCallBound.Background;
        output.WriteLine($"مُحاوَلات {attempts} × {AgentCallBound.Background.TotalSeconds}ث = {live.TotalSeconds}ث · StaleAfter {FeasibilityAnalysisService.StaleAfter.TotalSeconds}ث");
        Assert.Equal(live * 2, FeasibilityAnalysisService.StaleAfter);
        Assert.True(FeasibilityAnalysisService.StaleAfter > live);
    }

    /// <summary><b>قُلِبَ «Today_» بِاسمِه (‏2026-10-24، S15 K)</b>: كُلُّ تَشغيلَةٍ في الخَلفِيَّةِ تَكتُبُ استِثناءَها بِسَطرٍ يُسَمّي بابَها —
    /// ولا <c>catch { }</c> باقٍ حَولَ <c>RunAnalysisAsync</c>.</summary>
    [Fact]
    public void The_three_background_analysis_runs_log_their_exception()
    {
        var runs = BackgroundRuns(Endpoints(), "bg.RunAnalysisAsync(");
        output.WriteLine($"تَشغيلاتُ تَحليلٍ في الخَلفِيَّة: {runs.Count}");
        Assert.Equal(3, runs.Count);
        var doors = new List<string>();
        foreach (var r in runs)
        {
            Assert.DoesNotMatch(@"catch\s*\{\s*\}", r);
            var m = Regex.Match(r,
                @"catch \(Exception ex\) \{ Services\.Incubator\.FeasibilityAnalysisService\.LogBackgroundFailure\(scope\.ServiceProvider, (id|s\.Id), ""(?<door>[a-z.]+)"", ex\); \}");
            Assert.True(m.Success, "تَشغيلَةٌ بِلا سَطرِ فَشَل:\n" + r);
            doors.Add(m.Groups["door"].Value);
        }
        Assert.Equal(new[] { "admin.analyze", "studio.analyze", "studio.resume" }, doors.OrderBy(d => d, StringComparer.Ordinal));
    }

    /// <summary><b>تَوصيفُ «اليَوم»</b> (‏2026-10-24، مُراجَعَةُ تَيّارِ S15 الجَولَة 1): التَشغيلَةُ الرابِعَةُ في الخَلفِيَّة — التَحسين — تَنتَهي بِـ<c>catch { }</c>،
    /// وقَد نَقَلَها البَندُ K إلى مُهلَةِ الخَلفِيَّةِ (‏420 ثانِيَة) نَفسِها. يُقلَبُ بِاسمِه في كوميتِ إصلاحِ بابِ التَحسين.</summary>
    [Fact]
    public void Today_the_background_refine_run_swallows_its_exception()
    {
        var run = Assert.Single(BackgroundRuns(Endpoints(), "bg.RefineSectionAsync("));
        output.WriteLine($"تَشغيلَةُ التَحسين: {run.Length} مِحرَفاً · catch فارِغ: {Regex.IsMatch(run, @"catch\s*\{\s*\}")}");
        Assert.Matches(@"catch\s*\{\s*\}", run);
        Assert.DoesNotContain("LogBackgroundFailure", run, StringComparison.Ordinal);
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

    // ─── ما أَضافَه الإصلاح (‏2026-10-24، S15 K) ──────────────────────────────

    /// <summary>مُهلَةُ المُتَصَفِّحِ رَقَمُ اليَومِ بِحَرفِه، وهي افتِراضُ كُلِّ طَلَب — ومُنادٍ لا يُعلِنُ مُنتَظِرَه، أَو قيمَةٌ خارِجَ المُعجَم،
    /// يَأخُذُ أَقصَرَ المُهلَتَين.</summary>
    [Fact]
    public void The_browser_bound_is_today_s_60_seconds_and_the_default_of_every_request()
    {
        Assert.Equal(TimeSpan.FromSeconds(60), AgentCallBound.Browser);
        var req = new AgentRequest("s", Array.Empty<AgentMessage>(), Array.Empty<AgentToolDef>(), "m", MaxTokens: 16);
        Assert.Equal(AgentCallWait.Browser, req.Wait);
        Assert.Equal(AgentCallBound.Browser, AgentCallBound.For(req.Wait));
        Assert.Equal(AgentCallBound.Browser, AgentCallBound.For((AgentCallWait)7));
        Assert.Equal(AgentCallBound.Background, AgentCallBound.For(AgentCallWait.Background));
        Assert.Equal(2, Enum.GetValues<AgentCallWait>().Length);
    }

    /// <summary>مُهلَةُ الخَلفِيَّةِ مُشتَقَّةٌ مِن <c>MaxTokens</c> التَحليلِ في مَصدَرِه وعَلى المُعَدَّلِ المَفروض — لا رَقَمٌ مُستَقِلّ.</summary>
    [Fact]
    public void The_background_bound_covers_a_full_analysis_answer_at_the_assumed_pace()
    {
        var a = Analysis();
        var run = a[a.IndexOf("public async Task<IncubatorSession> RunAnalysisAsync(", StringComparison.Ordinal)..];
        var maxTokens = int.Parse(Regex.Match(run, @"MaxTokens: (\d+)\b").Groups[1].Value);
        var derived = maxTokens / AgentCallBound.AssumedTokensPerSecond + AgentCallBound.FirstTokenAllowanceSeconds;
        output.WriteLine($"{maxTokens} / {AgentCallBound.AssumedTokensPerSecond} + {AgentCallBound.FirstTokenAllowanceSeconds} = {derived}ث");
        Assert.Equal(TimeSpan.FromSeconds(derived), AgentCallBound.Background);
        Assert.Equal(TimeSpan.FromSeconds(420), AgentCallBound.Background);
        Assert.True(AgentCallBound.Background > AgentCallBound.Browser);
    }

    /// <summary>لا يَطولُ طَلَبٌ يَنتَظِرُه مُتَصَفِّح: مَن يَطلُبُ مُهلَةَ الخَلفِيَّةِ نِداءا <c>FeasibilityAnalysisService</c> وَحدَهُما — وكِلاهُما
    /// في <c>Task.Run</c> (الصَفُّ السابِق) — و<c>AgentService</c> لا يَطلُبُها.</summary>
    [Fact]
    public void Only_the_two_background_calls_ask_for_the_background_bound()
    {
        var hits = new List<string>();
        foreach (var dir in new[] { "libs", "apps" })
            foreach (var f in Directory.EnumerateFiles(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, dir), "*.*", SearchOption.AllDirectories))
            {
                if (!(f.EndsWith(".cs", StringComparison.Ordinal) || f.EndsWith(".razor", StringComparison.Ordinal))) continue;
                var sep = Path.DirectorySeparatorChar;
                if (f.Contains($"{sep}bin{sep}") || f.Contains($"{sep}obj{sep}") || f.EndsWith("AgentBackends.cs", StringComparison.Ordinal)) continue;
                var n = Regex.Matches(WriteEndpointGuardTests.StripComments(File.ReadAllText(f)), @"AgentCallWait\.Background").Count;
                for (var i = 0; i < n; i++) hits.Add(Path.GetFileName(f));
            }
        output.WriteLine($"«AgentCallWait.Background» خارِجَ AgentBackends.cs: {hits.Count} — {string.Join(", ", hits)}");
        Assert.Equal(new[] { "FeasibilityAnalysisService.cs", "FeasibilityAnalysisService.cs" }, hits);

        var a = Analysis();
        var refineAt = a.IndexOf("public async Task RefineSectionAsync(", StringComparison.Ordinal);
        var runAt = a.IndexOf("public async Task<IncubatorSession> RunAnalysisAsync(", StringComparison.Ordinal);
        Assert.Matches(@"^[\s\S]*?MaxTokens: 3000, Wait: AgentCallWait\.Background\)", a[refineAt..]);
        Assert.Matches(@"^[\s\S]*?MaxTokens: 8000, Wait: AgentCallWait\.Background\)", a[runAt..]);
        Assert.DoesNotContain("AgentCallWait", Studio());
    }

    /// <summary>المُهلَةُ تُقاسُ بِنِداءٍ حَقيقِيٍّ عَلى مُعالِجٍ لا يُجيبُ أَبَداً — لا بِنَصِّ المَصدَر: انقِضاؤُها مُهلَة، وإلغاءُ المُنادي
    /// لَيسَ مُهلَة، واستِثناءٌ غَيرُ الإلغاءِ لَيسَ مُهلَةً ولَو انقَضَت.</summary>
    [Fact]
    public async Task An_expired_bound_reads_as_a_timeout_and_a_cancelled_caller_does_not()
    {
        using var http = new HttpClient(new NeverAnswers()) { Timeout = System.Threading.Timeout.InfiniteTimeSpan };

        using (var bound = AgentCallBound.Start(TimeSpan.FromMilliseconds(50), CancellationToken.None))
        {
            var ex = await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => http.GetAsync("http://127.0.0.1:9/never", bound.Token));
            Assert.True(AgentCallBound.Expired(ex, bound, CancellationToken.None));
            Assert.False(AgentCallBound.Expired(new HttpRequestException("x"), bound, CancellationToken.None));
        }

        using var caller = new CancellationTokenSource();
        using (var bound = AgentCallBound.Start(TimeSpan.FromMinutes(5), caller.Token))
        {
            caller.CancelAfter(50);
            var ex = await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => http.GetAsync("http://127.0.0.1:9/never", bound.Token));
            Assert.False(AgentCallBound.Expired(ex, bound, caller.Token));
        }
    }

    /// <summary>نَصُّ المُهلَةِ يُسَمّيها ورَقمَها ومَوضِعَها، ولا يَقرَؤُه <c>FormatError</c> حِصَّةً نافِدَة — يَمُرُّ إلى الدِراسَةِ بِحَرفِه.</summary>
    [Fact]
    public void The_timeout_text_names_its_bound_and_is_not_read_as_a_quota()
    {
        var text = AgentErrorText.Timeout("anthropic", "https://api.anthropic.com/", AgentCallWait.Background);
        Assert.Equal(
            "anthropic (https://api.anthropic.com/) timeout: no answer within 420 seconds (AgentCallBound.Background, AgentBackends.cs)",
            text);
        Assert.Contains("within 60 seconds (AgentCallBound.Browser,",
            AgentErrorText.Timeout("anthropic", "https://api.anthropic.com/", AgentCallWait.Browser));

        var format = typeof(FeasibilityAnalysisService).GetMethod("FormatError",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(format);
        Assert.Equal(text, (string)format!.Invoke(null, new object?[] { text })!);
    }

    /// <summary>سَطرُ فَشَلِ التَشغيلِ في الخَلفِيَّة: البابُ والجَلسَةُ ونَوعُ الاستِثناءِ ورِسالَتُه، بِمُستَوى خَطَأ — وبِلا مُسَجِّلٍ لا يَرمي.</summary>
    [Fact]
    public void The_background_failure_line_names_the_door_the_session_and_the_exception()
    {
        var capture = new CaptureLogs();
        using var sp = new Microsoft.Extensions.DependencyInjection.ServiceCollection()
            .AddLogging(b => b.AddProvider(capture))
            .BuildServiceProvider();
        var id = Guid.Parse("0f8b7c4e-5d2a-4f61-9b3e-2a1c0d9e8f70");

        FeasibilityAnalysisService.LogBackgroundFailure(sp, id, "studio.analyze", new InvalidOperationException("session not found"));

        var line = Assert.Single(capture.Lines);
        Assert.Equal(Microsoft.Extensions.Logging.LogLevel.Error, line.Level);
        Assert.Equal(
            "[studio] analysis_run_failed door=studio.analyze session=0f8b7c4e-5d2a-4f61-9b3e-2a1c0d9e8f70 "
            + "System.InvalidOperationException: session not found",
            line.Message);

        using var empty = new Microsoft.Extensions.DependencyInjection.ServiceCollection().BuildServiceProvider();
        FeasibilityAnalysisService.LogBackgroundFailure(empty, id, "studio.analyze", new InvalidOperationException("x"));
    }

    private sealed class NeverAnswers : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, ct);
            throw new InvalidOperationException("لا يُبلَغ.");
        }
    }

    private sealed class CaptureLogs : Microsoft.Extensions.Logging.ILoggerProvider
    {
        public List<(Microsoft.Extensions.Logging.LogLevel Level, string Message)> Lines { get; } = new();
        public Microsoft.Extensions.Logging.ILogger CreateLogger(string categoryName) => new Logger(this);
        public void Dispose() { }

        private sealed class Logger(CaptureLogs owner) : Microsoft.Extensions.Logging.ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;
            public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId,
                TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                lock (owner.Lines) owner.Lines.Add((logLevel, formatter(state, exception)));
            }
        }
    }
}
