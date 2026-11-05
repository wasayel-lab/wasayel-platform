using System.Text.RegularExpressions;
using ACommerce.Kit.Auth.Server;
using ACommerce.Templates.Customer.Marketplace.Services;
using ACommerce.Templates.Customer.Marketplace.Services.Incubator;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ دائِرَتا الوَكيلِ تَسأَلانِ الجَلسَةَ قَبلَ كُلِّ فِعل — بَعدَ التَبديل (‏2026-11-05، الموجَة ١٢ G2 · ١؛ A-SESS-W12-2) ══════════════════════════════
//
// قُلِبَ ما طَبَعَه التَوصيف (`SessionCircuitTests.cs`، `0f5a3c4`، والمِلَفُّ بِبَصمَتِه): بَعدَ «اخرج من كل الأجهزة» أَو نَزعِ الإشرافِ كانَت الدائِرَةُ المَفتوحَةُ
// «نِداءاتُ النَموذَج 3 · الأَداةُ المُطَبَّقَة error · المَرفوضَة rejected · مُحِيَ السِجِلّ True». والآن كُلُّ نَقرَةٍ تَسأَلُ
// `SessionVersionGate.PlatformAdminStillOpenAsync` بِالرَمزِ الَّذي التُقِطَ عِندَ التَصييرِ الأَوَّل، والمُغلَقُ لا يُنادي النَموذَجَ ولا يُشَغِّلُ أَداةً ولا يَمحو،
// ويَنتَقِلُ إلى `/studio/auth`.

public class SessionCircuitSwitchTests(ITestOutputHelper output)
{
    private static string Token(Guid id, int version, string tenant = StudioAuth.Tenant) => AuthHandlers.MakeToken(id, tenant, version);

    /// <summary><c>PlatformAdminStillOpen</c> — نَقِيَّة. <b>المُوجَب</b>: رَمزُ استوديو حَيٌّ لِمُشرِفٍ قائِمٍ (وإصدارٌ فَوقَ المُخَزَّنِ مَفتوحٌ كَما في <c>StillOpen</c>).
    /// <b>والسَوالِب</b>: رَمزُ ما قَبلَ الرَفع، وصاحِبٌ لَيسَ مُشرِفاً، وحِسابٌ غائِب، ورَمزُ حِسابٍ آخَر، ورَمزُ مَتجَرٍ بِالمُعَرِّفِ نَفسِه، وما لَيسَ رَمزاً.</summary>
    [Theory]
    [InlineData("live-admin", true)]
    [InlineData("above", true)]
    [InlineData("revoked", false)]
    [InlineData("not-admin", false)]
    [InlineData("missing", false)]
    [InlineData("other-account", false)]
    [InlineData("store-token", false)]
    [InlineData("garbage", false)]
    public void A_studio_session_is_still_a_platform_admin_session_only_while_open_and_admin(string row, bool open)
    {
        var account = new StudioUser { Id = Guid.NewGuid(), SessionVersion = 2, IsPlatformAdmin = row != "not-admin" };
        var token = row switch
        {
            "live-admin" or "not-admin" or "missing" => Token(account.Id, 2),
            "above" => Token(account.Id, 3),
            "revoked" => Token(account.Id, 1),
            "other-account" => Token(Guid.NewGuid(), 2),
            "store-token" => Token(account.Id, 2, "ejar"),
            _ => "not-a-token",
        };
        var answer = SessionVersionGate.PlatformAdminStillOpen(row == "missing" ? null : account, token);
        output.WriteLine($"{row} ⇒ {(answer ? "مَفتوحَة" : "مُغلَقَة")}");
        Assert.Equal(open, answer);
    }

    /// <summary>الغِلافُ يَقرَأُ الوَثيقَةَ مِن جَديدٍ ويُجيبُ بِالنَقِيَّة: مَفتوحَةٌ لِلمُشرِفِ الحَيّ؛ ومُغلَقَةٌ إن رُفِعَ الإصدارُ بَعدَ الالتِقاط؛ ورَمزٌ لِغَيرِ الحِسابِ المَسؤولِ
    /// عَنه يُرَدُّ بِلا قِراءَة.</summary>
    [Fact]
    public async Task The_circuit_question_reads_the_account_fresh_and_refuses_a_token_for_another_account_without_a_read()
    {
        var admin = new StudioUser { Id = Guid.NewGuid(), SessionVersion = 0, IsPlatformAdmin = true };
        var world = new DocWorld().Put(admin);
        var token = Token(admin.Id, 0);
        var open = await SessionVersionGate.PlatformAdminStillOpenAsync(world.Store, token, admin.Id);
        admin.SessionVersion = 1;
        var afterRevoke = await SessionVersionGate.PlatformAdminStillOpenAsync(world.Store, token, admin.Id);
        var reads = world.Touches.Count(m => m == "LoadAsync");
        var foreign = await SessionVersionGate.PlatformAdminStillOpenAsync(world.Store, token, Guid.NewGuid());
        output.WriteLine($"حَيّ ⇒ {open} · بَعدَ الرَفع ⇒ {afterRevoke} · قِراءات {reads} · رَمزٌ لِحِسابٍ آخَر ⇒ {foreign} · قِراءاتٌ بَعدَه {world.Touches.Count(m => m == "LoadAsync") - reads}");
        Assert.True(open);
        Assert.False(afterRevoke);
        Assert.Equal(2, reads);
        Assert.False(foreign);
        Assert.Equal(reads, world.Touches.Count(m => m == "LoadAsync"));
    }

    // ─── الفاحِص (القاعِدَة ٢): كُلُّ مُعالِجٍ في سَطحٍ تَفاعُلِيٍّ مُثَبَّتٍ يَسأَلُ قَبلَ أَيِّ نِداء ──────────────────────────────────────────────

    private static readonly Regex AgentCall = new(@"\b(?:Agent|Executor)\s*\.\s*\w+Async\s*\(", RegexOptions.Compiled);

    /// <summary>خَرقُ مُعالِجٍ لِسُؤالِ الدائِرَة — <c>null</c> إن كانَ شَرطُ الرُجوعِ الأَوَّلُ يَسأَلُ <c>StillPlatformAdminAsync()</c> ويَعودُ عَلى الرَفض، قَبلَ أَيِّ
    /// نِداءٍ لِلوَكيلِ أَو المُنَفِّذ. وإلّا سَبَبُ الخَرق.</summary>
    internal static string? HandlerBreach(string body)
    {
        var ask = body.IndexOf("!await StillPlatformAdminAsync()) return;", StringComparison.Ordinal);
        if (ask < 0) return "لا يَسأَلُ StillPlatformAdminAsync في شَرطِ الرُجوع";
        var call = AgentCall.Match(body);
        if (!call.Success) return "لا نِداءَ لِلوَكيلِ ولا لِلمُنَفِّذ — الأَداةُ عَمياء";
        return call.Index < ask ? $"يُنادي «{call.Value.TrimEnd('(')}» قَبلَ أَن يَسأَل" : null;
    }

    /// <summary>جِسمُ مُعالِجٍ في صَفحَةِ razor بِلا تَعليقات — مِن تَصريحِه إلى قَوسِه المُغلِق.</summary>
    internal static string HandlerBody(string page, string name)
    {
        var code = Regex.Replace(WriteEndpointGuardTests.StripMarkupComments(page), @"//[^\n]*", "");
        var decl = Regex.Match(code, $@"async\s+Task(?:<\w+>)?\s+{Regex.Escape(name)}\s*\([^)]*\)\s*\{{");
        Assert.True(decl.Success, $"«{name}» غائِب — الأَداةُ عَمياء.");
        var depth = 0;
        for (var i = decl.Index + decl.Length - 1; i < code.Length; i++)
        {
            if (code[i] == '{') depth++;
            else if (code[i] == '}' && --depth == 0) return code[decl.Index..(i + 1)];
        }
        throw new InvalidOperationException($"«{name}» بِلا قَوسٍ مُغلِق.");
    }

    /// <summary>
    /// <para><b>كُلُّ سَطحٍ تَفاعُلِيٍّ يَكتُبُ في مُعالِجاتِ نَقرِه</b> — قائِمَةُ <c>WriteEndpointGuardTests.PinnedDisplayWriters</c> نَفسُها (الفاحِصُ الَّذي يَجِدُ كُلَّ
    /// صَفحَةِ razor تَكتُبُ بِخِدمَةٍ ويُثَبِّتُها بِمُعالِجاتِها؛ القاعِدَة ٨) — <b>كُلُّ مُعالِجٍ فيه يَسأَلُ قَبلَ أَيِّ نِداء</b> (<see cref="HandlerBreach"/>)، والصَفحَةُ
    /// تَلتَقِطُ رَمزَ الاستوديو عِندَ تَصييرِها الأَوَّلِ وتَسأَلُ بِه الغِلافَ وتَنتَقِلُ إلى الدُخولِ عَلى الرَفض. فَسَطحٌ ثالِثٌ يُثَبَّتُ هُناكَ ولا يَسأَلُ يَحمَرُّ هُنا.</para>
    /// </summary>
    [Fact]
    public void Every_handler_of_a_pinned_interactive_writer_asks_the_session_before_any_call()
    {
        var surfaces = WriteEndpointGuardTests.PinnedDisplayWriters.Where(p => p.InHandlers is not null).ToList();
        var checkedHandlers = 0;
        var handlerBreaches = new List<string>();
        var pageBreaches = new List<string>();
        foreach (var surface in surfaces)
        {
            var name = Path.GetFileName(surface.Where);
            var page = File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, surface.Where.Replace('/', Path.DirectorySeparatorChar)));
            foreach (var handler in surface.InHandlers!)
            {
                checkedHandlers++;
                if (HandlerBreach(HandlerBody(page, handler)) is { } why) handlerBreaches.Add($"{name}.{handler}: {why}");
            }
            var ask = HandlerBody(page, "StillPlatformAdminAsync");
            var init = HandlerBody(page, "OnInitializedAsync");
            if (!ask.Contains("Services.SessionVersionGate.PlatformAdminStillOpenAsync(Store, _studioToken,", StringComparison.Ordinal)) pageBreaches.Add($"{name}: السُؤالُ لا يَسأَلُ الغِلافَ بِالرَمزِ المُلتَقَط");
            if (!ask.Contains("Nav.NavigateTo(\"/studio/auth\", forceLoad: true);", StringComparison.Ordinal)) pageBreaches.Add($"{name}: الرَفضُ لا يَنتَقِلُ إلى الدُخول");
            if (!Regex.IsMatch(init, @"_studioToken\s*=\s*HttpCtx\.HttpContext\??\.Request\.Cookies\[Services\.Incubator\.StudioAuth\.CookieName\];")) pageBreaches.Add($"{name}: الرَمزُ لا يُلتَقَطُ عِندَ التَصييرِ الأَوَّل");
        }
        output.WriteLine($"سُطوحٌ تَفاعُلِيَّةٌ تَكتُبُ في مُعالِجاتِها: {surfaces.Count} ({string.Join(" · ", surfaces.Select(x => Path.GetFileName(x.Where)))}) · "
            + $"مُعالِجاتٌ مَفحوصَة {checkedHandlers} · تَسأَلُ قَبلَ أَيِّ نِداء {checkedHandlers - handlerBreaches.Count} · صَفَحاتٌ تَلتَقِطُ وتَسأَلُ وتَنتَقِل {surfaces.Count - pageBreaches.Select(b => b.Split(':')[0]).Distinct().Count()}");
        Assert.True(surfaces.Count > 0 && checkedHandlers > 0, "صِفرُ مُعالِجٍ مَفحوص — الأَداةُ عَمياء.");
        Assert.Equal(8, checkedHandlers);
        Assert.True(handlerBreaches.Count == 0 && pageBreaches.Count == 0, "سَطحٌ تَفاعُلِيٌّ لا يَسأَلُ الجَلسَة: " + string.Join(" · ", handlerBreaches.Concat(pageBreaches)));
    }

    /// <summary>حَقنُ عَيب (القاعِدَة ١٠): مُعالِجُ اليَومِ بِحَرفِه قَبلَ التَبديل، والسُؤالُ بَعدَ النِداء، والسُؤالُ بِلا رُجوع، ومُعالِجٌ لا يُنادي شَيئاً — كُلٌّ يُرَدّ؛
    /// والمُعالِجُ الحَقيقيُّ يَمُرّ.</summary>
    [Theory]
    [InlineData("today", "async Task ApplyAsync(string toolId)\n{\n    if (isWorking) return;\n    isWorking = true;\n    var (ok, msg) = await Executor.ExecuteAsPlatformAdminAsync(turn.Tool.Name, turn.Tool.InputJson, AdminUserId ?? Guid.Empty);\n}")]
    [InlineData("asks-after-the-call", "async Task SendAsync()\n{\n    session = await Agent.AskAsync(msg);\n    if (isWorking || !await StillPlatformAdminAsync()) return;\n}")]
    [InlineData("asks-without-returning", "async Task ResetAsync()\n{\n    await StillPlatformAdminAsync();\n    await Agent.ResetAsync();\n}")]
    [InlineData("calls-nothing", "async Task ResetAsync()\n{\n    if (isWorking || !await StillPlatformAdminAsync()) return;\n    newMessage = \"\";\n}")]
    public void The_handler_check_refuses_a_handler_in_an_old_or_broken_shape(string shape, string body)
    {
        var breach = HandlerBreach(body);
        output.WriteLine($"{shape}: {breach ?? "يَمُرّ"}");
        Assert.NotNull(breach);
        var real = File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, WriteEndpointGuardTests.PinnedDisplayWriters[0].Where.Replace('/', Path.DirectorySeparatorChar)));
        Assert.Null(HandlerBreach(HandlerBody(real, "ApplyAsync")));
    }
}

/// <summary>الدائِرَةُ حَيّاً بَعدَ التَبديل — بِالمُضيفِ المُرَكَّبِ وصَفحَتِه بِعَينِها (<see cref="LiveSessionCircuitProofTests.RunAsync"/>).</summary>
[Collection(LiveProofCollection.Name)]
public class LiveSessionCircuitSwitchProofTests(ITestOutputHelper output)
{
    /// <summary><b>قُلِبَ ما طَبَعَه التَوصيف</b> («نِداءاتُ النَموذَج 3 · الأَداةُ المُطَبَّقَة error · المَرفوضَة rejected · مُحِيَ السِجِلّ True · التَنقُّل لا شَيء»):
    /// بَعدَ «اخرج من كل الأجهزة» أَو نَزعِ الإشرافِ — في الصَفحَتَين — صِفرُ نِداءِ نَموذَج، والأَداتانِ مُعَلَّقَتانِ كَما كانَتا، والسِجِلُّ باقٍ، وكُلُّ نَقرَةٍ مِن الأَربَعِ
    /// تَنتَقِلُ إلى <c>/studio/auth</c> بِتَحميلٍ كامِل. والمَفتوحُ يَعمَلُ كَما ثَبَّتَه التَوصيف.</summary>
    [Theory]
    [InlineData(LiveSessionCircuitProofTests.StudioPage, LiveSessionCircuitProofTests.Between.SignedOutEverywhere)]
    [InlineData(LiveSessionCircuitProofTests.StudioPage, LiveSessionCircuitProofTests.Between.NoLongerAdmin)]
    [InlineData(LiveSessionCircuitProofTests.AdminPage, LiveSessionCircuitProofTests.Between.SignedOutEverywhere)]
    [InlineData(LiveSessionCircuitProofTests.AdminPage, LiveSessionCircuitProofTests.Between.NoLongerAdmin)]
    public async Task A_circuit_whose_session_closed_makes_no_model_call_runs_no_tool_and_goes_to_sign_in_on_Postgres(string page, LiveSessionCircuitProofTests.Between between)
    {
        if (!LiveSessionCircuitProofTests.Enabled) return;
        var outcome = await LiveSessionCircuitProofTests.RunAsync(page, between);
        output.WriteLine($"{page} · {between} ⇒ {outcome}");
        Assert.Empty(outcome.Errors);
        Assert.True(outcome.DoorOpen);
        Assert.Equal(0, outcome.ModelCalls);
        Assert.Equal("pending", outcome.Applied);
        Assert.Equal("pending", outcome.Rejected);
        Assert.False(outcome.Cleared);
        Assert.Equal(Enumerable.Repeat("/studio/auth (forceLoad)", 4), outcome.Went);
    }
}
