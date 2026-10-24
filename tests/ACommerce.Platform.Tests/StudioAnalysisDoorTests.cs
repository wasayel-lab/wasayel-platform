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
    /// <para><b>قُلِبَ صَفُّ «Today_» بِاسمِه (‏2026-10-24، S15 A)</b>: كُلُّ مَدخَلٍ مِن الخَمسَةِ يَسأَلُ
    /// <c>StudioAnalysisDoor.Opens</c> — الصَفَحاتُ الثَلاثُ لا تَرسُمُ الاستِمارَةَ إلّا داخِلَ فَرعِه، والنُقطَتانِ والاستِئنافُ
    /// يَرُدّونَ قَبلَ ما يَكتُبُ أَو يُنفِقُ أَو يُنادي. والتَرتيبُ مَقيسٌ في الصُفوفِ التالِيَة.</para>
    /// </summary>
    [Fact]
    public void Every_studio_entry_asks_the_one_analysis_door()
    {
        var pages = new[] { "Landing.razor", "StudioNewIdea.razor", "StudioStudy.razor" };
        foreach (var p in pages)
        {
            var text = Page(p);
            Assert.Contains("StudioAnalysisDoor.Opens(Incubator.IsConfigured)", text, StringComparison.Ordinal);
            output.WriteLine($"{p}: يَسأَلُ البابَ");
        }

        foreach (var route in new[] { "/studio/begin", "/studio/s/{id:guid}/analyze" })
        {
            Assert.Contains("StudioAnalysisDoor.Opens(", MapBody(route), StringComparison.Ordinal);
            output.WriteLine($"{route}: يَسأَلُ البابَ");
        }

        Assert.Contains("StudioAnalysisDoor.Opens(incubator.IsConfigured)", Resume(), StringComparison.Ordinal);
        output.WriteLine("ResumeStudioPromptAsync: يَسأَلُ البابَ");
    }

    // ─── الدالَّةُ النَقِيَّة — موجِبٌ وسالِب ─────────────────────────────────────────────────────

    [Fact]
    public void The_door_opens_when_the_analysis_backend_is_configured()
    {
        Assert.True(ACommerce.Templates.Customer.Marketplace.Services.Incubator.StudioAnalysisDoor.Opens(true));
        Assert.Equal("open", ACommerce.Templates.Customer.Marketplace.Services.Incubator.StudioAnalysisDoor.BootLogValue(true));
    }

    [Fact]
    public void The_door_stays_closed_without_a_configured_analysis_backend()
    {
        Assert.False(ACommerce.Templates.Customer.Marketplace.Services.Incubator.StudioAnalysisDoor.Opens(false));
        Assert.Equal("analysis_unavailable",
            ACommerce.Templates.Customer.Marketplace.Services.Incubator.StudioAnalysisDoor.BootLogValue(false));
    }

    // ─── التَرتيب: البابُ قَبلَ ما يَكتُبُ أَو يُنفِقُ أَو يُنادي ───────────────────────────────────

    private static int At(string body, string needle)
    {
        var i = body.IndexOf(needle, StringComparison.Ordinal);
        Assert.True(i >= 0, $"غائِب: {needle}");
        return i;
    }

    /// <summary><c>/studio/begin</c>: البابُ قَبلَ قِراءَةِ الاستِمارَةِ وكِتابَةِ الكَعكَةِ والتَحويلِ إلى الدُخول، ورَدُّه إلى <c>/</c>.</summary>
    [Fact]
    public void Begin_refuses_before_the_cookie_the_login_door_and_the_resume()
    {
        var body = MapBody("/studio/begin");
        var door = At(body, "StudioAnalysisDoor.Opens(incubator.IsConfigured)) return Results.Redirect(\"/\")");
        Assert.True(door < At(body, "req.Form[\"prompt\"]"));
        Assert.True(door < At(body, "StudioPromptCookie"));
        Assert.True(door < At(body, "ResumeStudioPromptAsync"));
        Assert.True(door < At(body, "Results.Redirect(\"/studio/auth\")"));
    }

    /// <summary>الاستِئنافُ: البابُ قَبلَ الشُروطِ والحِصَّةِ والجَلسَةِ والإطلاق — و«لا شَيءَ يُستَأنَف» (<c>null</c>).</summary>
    [Fact]
    public void Resume_refuses_before_consent_quota_session_and_the_run()
    {
        var body = Resume();
        var door = At(body, "StudioAnalysisDoor.Opens(incubator.IsConfigured)) return null;");
        Assert.True(door < At(body, "if (checkConsent)"));
        Assert.True(door < At(body, "CheckAnalyzeAsync"));
        Assert.True(door < At(body, "incubator.StartAsync"));
        Assert.True(door < At(body, "RecordAnalysisAsync"));
        Assert.True(door < At(body, "RunAnalysisAsync"));
    }

    /// <summary><c>/studio/s/{id}/analyze</c>: التَخويلُ (الجَلسَةُ ثُمَّ المِلكِيَّة) أَوَّلاً (القاعِدَة ٦)، ثُمَّ البابُ قَبلَ الحِصَّةِ والحَجزِ والإطلاق.</summary>
    [Fact]
    public void Analyze_asks_the_door_after_ownership_and_before_quota_claim_and_run()
    {
        var body = MapBody("/studio/s/{id:guid}/analyze");
        var door = At(body, "StudioAnalysisDoor.Opens(svc.IsConfigured)) return Results.Redirect($\"/studio/s/{id}\")");
        Assert.True(At(body, "auth.IsAuthenticated") < door);
        Assert.True(At(body, "session.OwnerUserId != auth.UserId") < door);
        Assert.True(door < At(body, "CheckAnalyzeAsync"));
        Assert.True(door < At(body, "TryClaimAnalysisAsync"));
        Assert.True(door < At(body, "RecordAnalysisAsync"));
        Assert.True(door < At(body, "RunAnalysisAsync"));
    }

    // ─── الصَفَحات: الاستِمارَةُ داخِلَ فَرعِ البابِ، والفَرعُ الآخَرُ نَصُّ القامُوس ────────────────

    /// <summary>كُلُّ استِمارَةٍ إلى التَحليلِ بَعدَ سُؤالِ البابِ وقَبلَ <c>else</c>ـه، و<c>else</c> يَرسُمُ مِفتاحَ القامُوسِ لا استِمارَة.</summary>
    [Theory]
    [InlineData("Landing.razor", "action=\"/studio/begin\"")]
    [InlineData("StudioNewIdea.razor", "action=\"/studio/begin\"")]
    [InlineData("StudioStudy.razor", "/studio/s/{session.Id}/analyze")]
    public void The_page_draws_the_form_only_inside_the_open_branch(string page, string form)
    {
        var text = Page(page);
        var door = At(text, "StudioAnalysisDoor.Opens(Incubator.IsConfigured))");
        var formAt = At(text, form);
        Assert.Equal(formAt, text.LastIndexOf(form, StringComparison.Ordinal));
        var elseAt = text.IndexOf("else", formAt, StringComparison.Ordinal);
        Assert.True(door < formAt && formAt < elseAt, $"{page}: الاستِمارَةُ خارِجَ فَرعِ البابِ المَفتوح.");
        var closed = text[elseAt..];
        var key = closed.IndexOf("StudioAnalysisDoor.ClosedMessageKey", StringComparison.Ordinal);
        Assert.True(key > 0 && key < 200, $"{page}: فَرعُ البابِ المُغلَقِ لا يَرسُمُ نَصَّ القامُوس.");
        Assert.DoesNotContain("<form", closed[..key], StringComparison.Ordinal);
    }

    /// <summary>نَصُّ البابِ المُغلَقِ في القامُوسِ العَرَبيّ (القاعِدَة ١١) — والمِفتاحُ مِن الثابِتِ نَفسِه لا مَنسوخاً.</summary>
    [Fact]
    public void The_closed_door_text_lives_in_the_arabic_dictionary()
    {
        var ar = Read("libs/core/ACommerce.Platform.I18n/Locales/ar.json");
        using var doc = System.Text.Json.JsonDocument.Parse(ar);
        var key = ACommerce.Templates.Customer.Marketplace.Services.Incubator.StudioAnalysisDoor.ClosedMessageKey;
        Assert.True(doc.RootElement.TryGetProperty(key, out var v), $"مِفتاحٌ غائِبٌ عَن ar.json: {key}");
        Assert.False(string.IsNullOrWhiteSpace(v.GetString()));
        output.WriteLine($"{key} = {v.GetString()}");
    }

    // ─── سَطرُ الإقلاعِ وبَوّابَةُ الإقلاع ──────────────────────────────────────────────────────

    /// <summary><c>Program.cs</c> يَطبَعُ القَرارَ نَفسَه بِرَمزِه، عَلى خَلفِيَّةِ وَكيلِ <c>Analysis</c> كَما تَبنيها الخِدمَة.</summary>
    [Fact]
    public void The_app_logs_the_door_decision_at_boot()
    {
        var program = Read("apps/V1.App/Program.cs");
        Assert.Contains("StudioAnalysisDoor.BootLogPrefix + \"{Door}\"", program, StringComparison.Ordinal);
        Assert.Contains("StudioAnalysisDoor.BootLogValue(", program, StringComparison.Ordinal);
        Assert.Contains("agentBackends.For(ACommerce.Templates.Customer.Marketplace.Services.AgentNames.Analysis).IsConfigured",
            program, StringComparison.Ordinal);
        var svc = Read($"{TemplateRoot}/Services/Incubator/FeasibilityAnalysisService.cs");
        Assert.Contains("_backend = agents.For(AgentNames.Analysis);", svc, StringComparison.Ordinal);
        Assert.Contains("public bool IsConfigured => _backend.IsConfigured;", svc, StringComparison.Ordinal);
        Assert.Equal("[studio] analysis_door=",
            ACommerce.Templates.Customer.Marketplace.Services.Incubator.StudioAnalysisDoor.BootLogPrefix);
    }

    private const string GateStart = "# ─── بِدايَةُ analysis_door ───";
    private const string GateEnd = "# ─── نِهايَةُ analysis_door ───";

    private (int Code, string Text) RunGateBlock(string log)
    {
        var gate = Read("scripts/verify-production-boot.sh");
        var a = gate.IndexOf(GateStart, StringComparison.Ordinal);
        Assert.True(a > 0, "كُتلَةُ analysis_door غائِبَة.");
        Assert.Equal(a, gate.LastIndexOf(GateStart, StringComparison.Ordinal));
        var b = gate.IndexOf(GateEnd, a, StringComparison.Ordinal);
        Assert.True(b > a);
        var fn = gate[a..(b + GateEnd.Length)];
        // ويُنادى في المَسارِ الرَئيسِ مَرَّةً واحِدَة — قَبلَ الحُكم.
        Assert.Equal(1, gate.Split("\nanalysis_door \"$APP_LOG\"").Length - 1);

        Assert.True(File.Exists("/bin/bash"), "أَداةٌ غائِبَة: /bin/bash.");
        var dir = Directory.CreateTempSubdirectory("wasayel-analysis-door-");
        try
        {
            var logPath = Path.Combine(dir.FullName, "app.log");
            File.WriteAllText(logPath, log, new System.Text.UTF8Encoding(false));
            var script = Path.Combine(dir.FullName, "run.sh");
            File.WriteAllText(script, "set -uo pipefail\n" + fn + "\nanalysis_door \"$1\"\n", new System.Text.UTF8Encoding(false));
            var psi = new System.Diagnostics.ProcessStartInfo("/bin/bash", new[] { script, logPath })
            {
                RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
            };
            using var p = System.Diagnostics.Process.Start(psi)!;
            var text = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
            Assert.True(p.WaitForExit(10_000));
            output.WriteLine($"→ {p.ExitCode} · {text.Trim()}");
            return (p.ExitCode, text.Trim());
        }
        finally { dir.Delete(recursive: true); }
    }

    private static string BootLine(bool configured)
        => "[06:48:23 INF] " +
           ACommerce.Templates.Customer.Marketplace.Services.Incubator.StudioAnalysisDoor.BootLogPrefix +
           ACommerce.Templates.Customer.Marketplace.Services.Incubator.StudioAnalysisDoor.BootLogValue(configured) + "\n";

    [Fact]
    public void The_boot_gate_names_the_missing_analysis_key_and_keeps_its_exit_code()
    {
        if (OperatingSystem.IsWindows()) { output.WriteLine("Windows — يُقاسُ في CI (‏ubuntu)."); return; }
        var (code, text) = RunGateBlock("[06:48:23 INF] [agent] Analysis: …\n" + BootLine(configured: false));
        Assert.Equal(0, code);
        Assert.StartsWith("⚠ مِفتاحُ وَكيلِ التَحليلِ غائِب — analysis_unavailable", text);
        Assert.Contains("Agents__Analysis__ApiKey", text, StringComparison.Ordinal);
        Assert.Contains("DEPLOY.md §٤·ب", text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_boot_gate_says_the_door_is_open_when_the_backend_is_configured()
    {
        if (OperatingSystem.IsWindows()) { output.WriteLine("Windows — يُقاسُ في CI (‏ubuntu)."); return; }
        var (code, text) = RunGateBlock(BootLine(configured: true));
        Assert.Equal(0, code);
        Assert.StartsWith("بابُ التَحليل: مَفتوح", text);
        Assert.DoesNotContain("⚠", text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_boot_gate_says_unmeasured_when_the_app_printed_no_door_line()
    {
        if (OperatingSystem.IsWindows()) { output.WriteLine("Windows — يُقاسُ في CI (‏ubuntu)."); return; }
        var (code, text) = RunGateBlock("[06:48:23 INF] Now listening on: http://localhost:5050\n");
        Assert.Equal(0, code);
        Assert.StartsWith("⚠ بابُ التَحليل: لَم يُقَس", text);
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
