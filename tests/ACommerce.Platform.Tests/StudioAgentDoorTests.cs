using System.Text.RegularExpressions;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ وَكيلُ التَطبيقاتِ في الاستوديو (`/studio/agent`) — بابٌ يُنفِقُ مِفتاحَ المالِكِ لِكُلِّ صاحِبِ جَلسَة ═══
//
// **تَدقيقُ الإطلاقِ عِندَ `dace7cd`، تَيّارُ W8-3**: ‏`lens-security-2` (حاجِبَة) و`lens-dead-doors-1` (كُبرى).
//
// **المَقيسُ حَيّاً (‏2026-10-25، الرَأسُ `bdb8df9`، قاعِدَةٌ مَحَلِّيَّةٌ جَديدَة `wasayel_w83a`، Development، دُخولُ رَمزِ المُحاكي)**
// لِمُستَخدِمِ استوديو سَجَّلَ نَفسَه (‏`0500000011`) ولِمُشرِفِ المَنَصَّةِ (‏`PLATFORM_ADMIN_PHONE`):
// - بِلا مِفتاح: ‏`/studio` تَرسُمُ لِكِلَيهِما رابِطَينِ إلى `/studio/agent` و`/studio/ideas` رابِطاً؛ والصَفحَةُ تَرسُمُ `<textarea … disabled`
//   و«لا يوجَد مِفتاح anthropic مَضبوط. أَضِف Agent:ApiKey في الإعدادات.» و`anthropic / claude-sonnet-4-6` — نَصُّ مُشَغِّلٍ لِرائِدِ أَعمال.
// - بِمِفتاحٍ في `Agent__ApiKey` وَحدَه (‏`Agents:Studio:*` يَسقُطُ إلَيه — `AgentProfileResolver.Resolve`): المُحَرِّرُ **مَفتوحٌ لِلمُستَخدِمِ
//   العادِيِّ** (`<textarea` بِلا `disabled`) و`openai / gpt-4o` — فَكُلُّ رِسالَةٍ مِنه نِداءُ نَموذَجٍ بِتاريخِ المُحادَثَةِ كامِلاً
//   (‏`MaxTokens: 2048`) عَلى مِفتاحِ المالِك، ولا عَدّادَ ولا حَدّ (‏`LanguageModelQuotaGateTests.PinnedUngated`). ودُخولُ الاستوديو مَفتوحٌ
//   لِأَيِّ رَقَمٍ أَو بَريد. ولَقطَةُ المُستَأجِرينَ في كُلِّ نِداءٍ (`BuildTenantSnapshotAsync`) **كُلُّ المَتاجِر** لا مَتاجِرُه.
//
// هذا المِلَفُّ يُوَصِّفُ «اليَوم» (القاعِدَة ٣) قَبلَ أَيِّ تَبديل، وصُفوفُ `Today_` تُقلَبُ بِأَسمائِها في كوميتَي الإصلاح.
public class StudioAgentDoorTests(ITestOutputHelper output)
{
    private const string TemplateRoot = "libs/templates/ACommerce.Templates.Customer.Marketplace";
    private const string AgentPage = TemplateRoot + "/Components/Pages/StudioAgent.razor";
    private const string HomePage = TemplateRoot + "/Components/Pages/StudioHome.razor";

    private static string Read(string relative)
    {
        var path = Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, relative.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(path), $"مَصدَرٌ مَفقود: {relative} — الأَداةُ عَمياءُ بِلا طَرَفٍ مَقروء.");
        return File.ReadAllText(path);
    }

    /// <summary>كودُ الصَفحَةِ بِلا تَعليقاتِ Razor/HTML/C# (بِطولِها) — فَشَرحٌ يَذكُرُ الحارِسَ لَيسَ حارِساً.</summary>
    private static string Code(string relative)
        => WriteEndpointGuardTests.StripComments(WriteEndpointGuardTests.StripMarkupComments(Read(relative)));

    /// <summary>جِسمُ دالَّةٍ بِتَوقيعِها — مِن قَوسِها المَفتوحِ إلى ما يُقابِلُه.</summary>
    private static string Body(string code, string signature)
    {
        var at = code.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(at >= 0, $"دالَّةٌ مَفقودَة: {signature} — القَصُّ أَعمى.");
        Assert.Equal(at, code.LastIndexOf(signature, StringComparison.Ordinal));
        var open = code.IndexOf('{', at);
        var depth = 0;
        for (var k = open; k < code.Length; k++)
        {
            if (code[k] == '{') depth++;
            else if (code[k] == '}' && --depth == 0) return code[open..(k + 1)];
        }
        Assert.Fail($"{signature}: جِسمٌ لا يُغلَق.");
        return "";
    }

    // ‏2026-10-25 (W8-3 — مُراجَعَةُ الجَولَةِ الأولى؛ يُلحَقُ ولا يُعادُ حَرفٌ فَوقَ ما سَبَق): كانَت هُنا `SpendingHandlers` — ثَلاثَةُ تَواقيعَ
    // مُسَمّاة (SendAsync, ApplyAsync, RejectAsync) ووَصفُها «نَفسُ LlmReachingMethods لا قائِمَةٌ ثانِيَة»، وهي قائِمَةٌ ثانِيَة: دالَّةٌ رابِعَةٌ
    // تُنادي Agent.AskAsync بِلا بابٍ مَرَّت بِكُلِّ صَفّ (مَقيس — والجَردُ في LanguageModelQuotaGateTests يَحكُمُ عَلى الصَفحَةِ جِسماً واحِداً
    // فَيَراها «PlatformAdminGuard» لِأَنّ EvaluateAsync فيها). صارَت النِداءاتُ تُشتَقُّ مِن الكودِ بِـSpendCallsIn أَدناه (القاعِدَة ٢).

    /// <summary>نِداءُ إنفاقٍ واحِدٌ في صَفحَة: الدالَّةُ الَّتي تَحويه — أَو <c>(markup)</c> خارِجَ كُتَلِ <c>@code</c>/<c>@functions</c> —
    /// واسمُه، وهَل يَسبِقُه في رَأسِ جِسمِ دالَّتِه حارِسُ البابِ المُغلَق.</summary>
    private sealed record SpendCall(string Member, string Spend, bool Gated);

    /// <summary>حارِسُ البابِ المُغلَقِ بِحَرفِ مُدخَلَيه — شِقٌّ مِن شُقوقِ <c>||</c> في <c>if (…) return;</c>.</summary>
    private const string ClosedDoor = "!StudioAgentDoor.Opens(Agent.IsConfigured, _platformAdmin)";

    /// <summary>ما يَبلُغُ النَموذَجَ — أَسماءُ <see cref="LanguageModelQuotaGateTests.LlmReachingMethods"/> نَفسُها لا قائِمَةٌ ثانِيَة،
    /// <b>ذِكراً لا نِداءً فَقَط</b>: مَجموعَةُ دالَّةٍ تُمَرَّرُ مُفَوَّضاً (<c>=> Agent.AskAsync;</c>) تُنفِقُ أَيضاً، وبِأَيِّ مُستَقبِلٍ كانَت.</summary>
    private static readonly Regex SpendToken = new(
        @"(?<![\w])(?<spend>" + string.Join("|", LanguageModelQuotaGateTests.LlmReachingMethods.Select(Regex.Escape)) + @")(?![\w])",
        RegexOptions.Compiled);

    /// <summary><para><b>كُلُّ نِداءِ إنفاقٍ في صَفحَةٍ مُشتَقّاً مِن كودِها</b> (بِلا تَعليقات): يَجِدُ كُلَّ ذِكرٍ لِما يَبلُغُ النَموذَج —
    /// في دالَّةٍ أَو دالَّةِ دَورَةِ حَياةٍ أَو حَقلٍ أَو تَرميز — ويَحكُمُ لِكُلٍّ بِحارِسِ رَأسِ دالَّتِه.</para>
    /// <para><b>الحارِسُ المَقبولُ واحِدٌ</b> (القاعِدَة ٦: الحِراسَةُ في الرَأسِ لا في الجِسم): عِبارَةُ <c>if (…) return;</c> (أَو
    /// <c>{ return; }</c>) <b>في المُستَوى الأَوَّلِ مِن جِسمِ الدالَّةِ</b> وفي أَوَّلِ عِبارَة — بَعدَ <c>{</c> أَو <c>;</c> أَو <c>}</c> لا بَعدَ
    /// <c>else</c> ولا <c>if (x)</c> بِلا قَوسَين — تَنتَهي قَبلَ النِداء، وأَحَدُ شُقوقِ <c>||</c> العُليا في شَرطِها <see cref="ClosedDoor"/> بِحَرفِه.
    /// فَإن كانَ في الشَرطِ <c>?</c> في مُستَواه الأَعلى رُفِض (الشَرطِيُّ أَدنى مِن <c>||</c> فَيَبتَلِعُ الشِقّ). وكُلُّ ما سِواه — حارِسٌ بَعدَ النِداء،
    /// أَو في فَرع، أَو بِـ<c>&amp;&amp;</c>، أَو بِلا <c>return</c>، أَو بِمُدخَلاتٍ أُخرى، أَو جِسمٌ تَعبيرِيّ، أَو تَرميز — <b>بِلا باب</b>.</para>
    /// <para>والبِنيَةُ (الأَقواسُ و<c>;</c> و<c>if</c>) تُقرَأُ عَلى نُسخَةٍ بُيِّضَت فيها السَلاسِلُ النَصّيَّةُ داخِلَ <c>@code</c>
    /// (<see cref="WriteEndpointGuardTests.SkipLiteral"/>)، فَـ<c>"{"</c> أَو حارِسٌ مَكتوبٌ في سِلسِلَةٍ لا يُغَيِّرُ الحُكم.</para></summary>
    private static List<SpendCall> SpendCallsIn(string code)
    {
        var structure = code.ToCharArray();
        var blocks = new List<(int Open, int Close)>();
        foreach (Match b in Regex.Matches(code, @"@(?:code|functions)\s*\{"))
        {
            var open = b.Index + b.Length - 1;
            var close = -1;
            var depth = 0;
            for (var k = open; k < code.Length; k++)
            {
                if (structure[k] is '"' or '\'')
                {
                    var end = WriteEndpointGuardTests.SkipLiteral(code, k);
                    for (var j = k + 1; j < end; j++) if (structure[j] is not ('\n' or '\r')) structure[j] = ' ';
                    k = end;
                    continue;
                }
                if (structure[k] == '{') depth++;
                else if (structure[k] == '}' && --depth == 0) { close = k; break; }
            }
            Assert.True(close > open, "كُتلَةُ @code لا تُغلَق — القَصُّ فاشِل.");
            blocks.Add((open, close));
        }
        var s = new string(structure);

        var calls = new List<SpendCall>();
        foreach (Match m in SpendToken.Matches(code))
        {
            var at = m.Index;
            var spend = m.Groups["spend"].Value;
            var block = blocks.FindIndex(b => b.Open < at && at < b.Close);
            if (block < 0) { calls.Add(new("(markup)", spend, false)); continue; }
            var (start, end) = MemberAround(s, blocks[block], at);
            calls.Add(new(MemberName(s, start, end), spend, HeadGuardBefore(code, s, start, at)));
        }
        return calls;
    }

    /// <summary>العُضوُ الَّذي يَحوي الفِهرِس في كُتلَةِ <c>@code</c>: يَنتَهي عُضوٌ بِـ<c>;</c> أَو <c>}</c> يَعودانِ بِالعُمقِ إلى صِفر.</summary>
    private static (int Start, int End) MemberAround(string s, (int Open, int Close) block, int at)
    {
        var start = block.Open + 1;
        var depth = 0;
        for (var k = block.Open + 1; k < block.Close; k++)
        {
            var c = s[k];
            if (c == '{') depth++;
            else if ((c == '}' && --depth == 0) || (c == ';' && depth == 0))
            {
                if (at < k) return (start, k + 1);
                start = k + 1;
            }
        }
        return (start, block.Close);
    }

    /// <summary>اسمُ العُضوِ لِلطِباعَة: أَوَّلُ مُعَرِّفٍ يَليه <c>(</c> أَو <c>=</c> (لا <c>=&gt;</c>) في رَأسِه، وإلّا آخِرُ مُعَرِّفٍ قَبلَ <c>=&gt;</c>
    /// (خاصِّيَّةٌ تَعبيرِيَّة).</summary>
    private static string MemberName(string s, int start, int end)
    {
        var header = s[start..end];
        var cut = header.IndexOf('{');
        var arrow = header.IndexOf("=>", StringComparison.Ordinal);
        if (arrow >= 0 && (cut < 0 || arrow < cut)) cut = arrow;
        var head = cut < 0 ? header : header[..cut];
        var m = Regex.Match(head, @"(?<name>[A-Za-z_]\w*)\s*(?:\(|=(?![>=]))");
        if (m.Success) return m.Groups["name"].Value;
        var words = Regex.Matches(head, @"[A-Za-z_]\w*");
        return words.Count > 0 ? words[^1].Value : header.Trim();
    }

    /// <summary>هَل في المُستَوى الأَوَّلِ مِن جِسمِ العُضوِ — قَبلَ الفِهرِس — حارِسُ <see cref="ClosedDoor"/>؟ (القَواعِدُ في <see cref="SpendCallsIn"/>.)</summary>
    private static bool HeadGuardBefore(string code, string s, int start, int at)
    {
        var bodyOpen = s.IndexOf('{', start, at - start);
        if (bodyOpen < 0) return false;
        var depth = 0;
        var paren = 0;
        var guarded = false;
        for (var k = bodyOpen; k < at; k++)
        {
            var c = s[k];
            if (c == '{') { depth++; continue; }
            if (c == '}') { depth--; continue; }
            if (c is '(' or '[') { paren++; continue; }
            if (c is ')' or ']') { paren--; continue; }
            if (guarded || depth != 1 || paren != 0 || !IsWord(s, k, "if")) continue;
            var prev = k - 1;
            while (prev > bodyOpen && char.IsWhiteSpace(s[prev])) prev--;
            if (s[prev] is not ('{' or ';' or '}')) continue;
            var condOpen = k + 2;
            while (condOpen < at && char.IsWhiteSpace(s[condOpen])) condOpen++;
            if (condOpen >= at || s[condOpen] != '(') continue;
            var condClose = Closing(s, condOpen);
            if (condClose < 0) continue;
            var tail = Regex.Match(s[(condClose + 1)..], @"\A\s*(?:return\s*;|\{\s*return\s*;\s*\})");
            if (!tail.Success || condClose + 1 + tail.Length > at) continue;
            guarded = TopLevelDisjuncts(code, s, condOpen + 1, condClose)
                .Any(d => Regex.Replace(d, @"\s+", "") == Regex.Replace(ClosedDoor, @"\s+", ""));
        }
        return guarded && depth >= 1;
    }

    private static bool IsWord(string s, int k, string word)
        => string.CompareOrdinal(s, k, word, 0, word.Length) == 0
           && (k == 0 || !(char.IsLetterOrDigit(s[k - 1]) || s[k - 1] is '_' or '@' or '.'))
           && (k + word.Length >= s.Length || !(char.IsLetterOrDigit(s[k + word.Length]) || s[k + word.Length] == '_'));

    private static int Closing(string s, int open)
    {
        var depth = 0;
        for (var k = open; k < s.Length; k++)
        {
            if (s[k] == '(') depth++;
            else if (s[k] == ')' && --depth == 0) return k;
        }
        return -1;
    }

    /// <summary>شُقوقُ <c>||</c> في المُستَوى الأَعلى مِن شَرط — ولا شَيءَ إن كانَ فيه <c>?</c> في مُستَواه الأَعلى.</summary>
    private static List<string> TopLevelDisjuncts(string code, string s, int from, int to)
    {
        var parts = new List<string>();
        var depth = 0;
        var last = from;
        for (var k = from; k < to; k++)
        {
            var c = s[k];
            if (c is '(' or '[' or '{') depth++;
            else if (c is ')' or ']' or '}') depth--;
            else if (depth == 0 && c == '?') return new List<string>();
            else if (depth == 0 && c == '|' && k + 1 < to && s[k + 1] == '|')
            {
                parts.Add(code[last..k]);
                last = k + 2;
                k++;
            }
        }
        parts.Add(code[last..to]);
        return parts;
    }

    // ─── الصَفحَة: مَن يُنفِقُ مِنها ──────────────────────────────────────────────────────────────

    /// <summary><b>قُلِبَ «Today_» بِاسمِه (‏2026-10-25، كوميتُ إصلاحِ <c>lens-security-2</c>)</b> — كانَ
    /// <c>Today_every_agent_page_handler_that_spends_asks_only_for_a_signed_in_studio_user</c> (‏<c>92bd370</c>): «مُعالِجاتٌ تُنفِق: 3 · حارِسُ
    /// المُشرِف: 0 · باب: 0». والآن كُلُّ مُعالِجٍ يُنفِقُ (ومَعَها «مَسح» الَّذي يَكتُبُ الجَلسَة) يَسأَلُ <c>StudioAgentDoor.Opens</c> قَبلَ نِدائِه،
    /// ومُدخَلُ البابِ قَرارُ <c>PlatformAdminGuard.EvaluateAsync</c> يُقرَأُ حَيثُ يُقرَأُ المُستَخدِم، وتَحميلُ المُحادَثَةِ خَلفَه.
    /// <para><b>‏2026-10-25 (W8-3 — مُراجَعَةُ الجَولَةِ الأولى؛ يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه)</b>: «كُلُّ مُعالِجٍ يُنفِقُ» كانَ ثَلاثَةَ تَواقيعَ
    /// مُسَمّاة، فَدالَّةٌ رابِعَةٌ تُنادي <c>Agent.AskAsync</c> بِلا بابٍ كانَت تَمُرّ. صارَ يَشتَقُّ كُلَّ نِداءٍ مِن الكود (<see cref="SpendCallsIn"/>:
    /// الدالَّاتُ ودَورَةُ الحَياةِ والحُقولُ والتَرميز) ويَشتَرِطُ لِكُلٍّ حارِسَ رَأسِ دالَّتِه، ويَطبَعُ ما عَدَّه ويَفشَلُ دونَ المَقيس (3)؛
    /// والماسِحُ يُقاسُ بِصُفوفِ <see cref="The_spend_scanner_finds_every_model_call_and_accepts_only_a_head_guard"/> المَحقونَة.</para></summary>
    [Fact]
    public void Every_agent_page_handler_that_spends_asks_the_open_agent_door_before_its_call()
    {
        // ‏(‏2026-10-25، lens-dead-doors-1) البابُ صارَ يَسأَلُ مِفتاحَ وَكيلِ Studio مَعَ قَرارِ المُشرِف — الحَرفُ بِمُدخَلَيه.
        const string door = ClosedDoor;
        var code = Code(AgentPage);
        var calls = SpendCallsIn(code);
        var ungated = calls.Where(c => !c.Gated).Select(c => $"{c.Member} ← {c.Spend}").ToList();
        var reset = Body(code, "async Task ResetAsync()");
        var resetGated = reset.IndexOf(door, StringComparison.Ordinal) is var r && r > 0
                         && r < reset.IndexOf("Agent.ResetAsync(", StringComparison.Ordinal);
        output.WriteLine($"نِداءاتُ إنفاقٍ مُشتَقَّةٌ مِن الكود: {calls.Count} · خَلفَ البابِ في رَأسِ دالَّتِها: {calls.Count - ungated.Count} ("
            + string.Join(" · ", calls.Select(c => $"{c.Member} ← {c.Spend}" + (c.Gated ? "" : " [بِلا باب]"))) + $") · «مَسح» يَسأَلُه: {resetGated}");

        Assert.True(calls.Count >= 3, $"أَداةٌ عَمياء: {calls.Count} نِداءَ إنفاقٍ في الصَفحَة — والمَقيسُ 3.");
        Assert.True(ungated.Count == 0,
            "نِداءُ نَموذَجٍ في صَفحَةِ الوَكيلِ بِلا حارِسِ البابِ في رَأسِ دالَّتِه:\n  " + string.Join("\n  ", ungated)
            + $"\nأَوَّلُ عِبارَةٍ في جِسمِها: if (… || {door}) return;");
        Assert.True(resetGated, "ResetAsync يَكتُبُ الجَلسَةَ قَبلَ سُؤالِ الباب.");

        var init = Body(code, "protected override async Task OnInitializedAsync()");
        var userAt = init.IndexOf("_userId = StudioAuth.UserId!.Value;", StringComparison.Ordinal);
        var adminAt = init.IndexOf("_platformAdmin = (await Services.PlatformAdminGuard.EvaluateAsync(Store, StudioAuth)).Allowed;", StringComparison.Ordinal);
        var loadAt = init.IndexOf("if (_userId != Guid.Empty && StudioAgentDoor.Opens(Agent.IsConfigured, _platformAdmin))", StringComparison.Ordinal);
        Assert.True(userAt > 0 && adminAt > userAt, "قَرارُ المُشرِفِ لا يُقرَأُ مَعَ المُستَخدِم.");
        Assert.True(loadAt > adminAt && loadAt < init.IndexOf("Agent.LoadSessionAsync(", StringComparison.Ordinal),
            "المُحادَثَةُ تُحَمَّلُ قَبلَ سُؤالِ الباب.");
        Assert.Equal(1, Regex.Matches(code, "_platformAdmin = ").Count);
    }

    /// <summary>صَفحَةٌ مَحقونَةٌ بِأَعضاءِ <c>@code</c> وتَرميزٍ اختِيارِيّ — مَصدَرُ صُفوفِ الماسِح.</summary>
    private static string InjectedPage(string members, string markup = "")
        => "@page \"/x\"\n@inject AgentService Agent\n@inject FeasibilityAnalysisService Feasibility\n" + markup + "\n@code {\n"
         + "    bool isWorking;\n    bool _platformAdmin;\n    Guid _userId = Guid.Empty;\n    string _scopeId = \"s\";\n    AgentSession session = new();\n"
         + members + "\n}\n";

    private const string GuardedSend =
        "    async Task SendAsync()\n    {\n        if (isWorking || _userId == Guid.Empty || !StudioAgentDoor.Opens(Agent.IsConfigured, _platformAdmin)) return;\n"
      + "        session = await Agent.AskAsync(\"m\", _scopeId);\n    }\n";

    /// <summary>أَشكالُ الإنفاقِ والحِراسَةِ المَحقونَة — بِاسمِ الصَفّ، وما يَجِبُ أَن يَعُدَّه الماسِحُ نِداءً ومَحروساً.</summary>
    private static readonly Dictionary<string, string> InjectedSpenders = new()
    {
        // ─── ما يُقبَل ───
        ["head_guard"] = InjectedPage(GuardedSend),
        ["head_guard_with_a_braced_return"] = InjectedPage(
            "    async Task RejectAsync(string id)\n    {\n        if (!StudioAgentDoor.Opens(Agent.IsConfigured, _platformAdmin)) { return; }\n"
          + "        session = await Agent.ContinueAfterToolAsync(_scopeId);\n    }\n"),
        ["two_spends_behind_one_head_guard"] = InjectedPage(
            "    async Task ApplyAsync(string id)\n    {\n        if (isWorking || !StudioAgentDoor.Opens(Agent.IsConfigured, _platformAdmin)) return;\n"
          + "        session = await Agent.AskAsync(\"a\", _scopeId);\n        session = await Agent.ContinueAfterToolAsync(_scopeId);\n    }\n"),
        ["a_brace_in_a_string_does_not_shift_the_members"] = InjectedPage(
            "    string Open() => \"{\";\n    char Semicolon = ';';\n"
          + "    async Task RetryAsync() { session = await Agent.AskAsync(\"m\", _scopeId); }\n" + GuardedSend),

        // ─── ما يُرفَض ───
        ["a_fourth_handler_without_the_door"] = InjectedPage(GuardedSend
          + "    async Task RetryAsync() { if (isWorking || _userId == Guid.Empty) return; session = await Agent.AskAsync(\"retry\", _scopeId); }\n"),
        ["door_after_the_spend"] = InjectedPage(
            "    async Task SendAsync()\n    {\n        session = await Agent.AskAsync(\"m\", _scopeId);\n"
          + "        if (!StudioAgentDoor.Opens(Agent.IsConfigured, _platformAdmin)) return;\n    }\n"),
        ["door_inside_a_branch"] = InjectedPage(
            "    async Task SendAsync()\n    {\n        if (isWorking) { if (!StudioAgentDoor.Opens(Agent.IsConfigured, _platformAdmin)) return; }\n"
          + "        session = await Agent.AskAsync(\"m\", _scopeId);\n    }\n"),
        ["door_under_an_unbraced_if"] = InjectedPage(
            "    async Task SendAsync()\n    {\n        if (isWorking)\n            if (!StudioAgentDoor.Opens(Agent.IsConfigured, _platformAdmin)) return;\n"
          + "        session = await Agent.AskAsync(\"m\", _scopeId);\n    }\n"),
        ["door_in_an_else"] = InjectedPage(
            "    async Task SendAsync()\n    {\n        if (isWorking) isWorking = false;\n        else if (!StudioAgentDoor.Opens(Agent.IsConfigured, _platformAdmin)) return;\n"
          + "        session = await Agent.AskAsync(\"m\", _scopeId);\n    }\n"),
        ["door_anded"] = InjectedPage(
            "    async Task SendAsync()\n    {\n        if (isWorking && !StudioAgentDoor.Opens(Agent.IsConfigured, _platformAdmin)) return;\n"
          + "        session = await Agent.AskAsync(\"m\", _scopeId);\n    }\n"),
        ["door_swallowed_by_a_conditional"] = InjectedPage(
            "    async Task SendAsync()\n    {\n        if (isWorking ? false : isWorking || !StudioAgentDoor.Opens(Agent.IsConfigured, _platformAdmin)) return;\n"
          + "        session = await Agent.AskAsync(\"m\", _scopeId);\n    }\n"),
        ["door_that_does_not_return"] = InjectedPage(
            "    async Task SendAsync()\n    {\n        if (!StudioAgentDoor.Opens(Agent.IsConfigured, _platformAdmin)) isWorking = true;\n"
          + "        session = await Agent.AskAsync(\"m\", _scopeId);\n    }\n"),
        ["door_with_other_inputs"] = InjectedPage(
            "    async Task SendAsync()\n    {\n        if (!StudioAgentDoor.Opens(true, true)) return;\n"
          + "        session = await Agent.AskAsync(\"m\", _scopeId);\n    }\n"),
        ["door_in_a_comment"] = InjectedPage(
            "    async Task SendAsync()\n    {\n        // if (!StudioAgentDoor.Opens(Agent.IsConfigured, _platformAdmin)) return;\n"
          + "        session = await Agent.AskAsync(\"m\", _scopeId);\n    }\n"),
        ["door_in_a_string"] = InjectedPage(
            "    async Task SendAsync()\n    {\n        var s = \"x; if (!StudioAgentDoor.Opens(Agent.IsConfigured, _platformAdmin)) return;\";\n"
          + "        session = await Agent.AskAsync(\"m\", _scopeId);\n    }\n"),
        ["a_lifecycle_method"] = InjectedPage(
            "    protected override async Task OnAfterRenderAsync(bool firstRender)\n    {\n"
          + "        if (firstRender) session = await Agent.ContinueAfterToolAsync(_scopeId);\n    }\n"),
        ["an_expression_bodied_spender"] = InjectedPage(
            "    Task<AgentSession> RetryAsync() => Agent.AskAsync(\"m\", _scopeId);\n"),
        ["a_method_group_handed_on"] = InjectedPage(
            "    Func<string, string, Task<AgentSession>> Ask => Agent.AskAsync;\n"),
        ["a_spend_in_the_markup"] = InjectedPage("",
            "<button @onclick=\"@(async () => session = await Agent.AskAsync(\"m\", _scopeId))\">x</button>\n"),
        ["another_receiver_and_the_analysis_spends"] = InjectedPage(
            "    async Task AnalyzeAsync() { await Feasibility.RunAnalysisAsync(_scopeId); }\n"
          + "    async Task RefineAsync()\n    {\n        if (!StudioAgentDoor.Opens(Agent.IsConfigured, _platformAdmin)) return;\n"
          + "        await Feasibility.RefineSectionAsync(_scopeId);\n    }\n"),
    };

    /// <summary><b>والماسِحُ يُقاسُ قَبلَ أَن يُوثَقَ بِه</b> (القاعِدَة ١٠): كُلُّ شَكلٍ مَحقونٍ بِعَدَدِ نِداءاتِه والمَحروسِ مِنها — الحارِسُ في
    /// رَأسِ الجِسمِ يُقبَل، وكُلُّ ما يُشبِهُه ولا يَحرُسُ يُرفَض: بَعدَ النِداء، في فَرع، تَحتَ <c>if</c> بِلا قَوسَين، في <c>else</c>، بِـ<c>&amp;&amp;</c>،
    /// يَبتَلِعُه شَرطِيّ، بِلا <c>return</c>، بِمُدخَلاتٍ أُخرى، في تَعليقٍ أَو سِلسِلَة؛ ودَورَةُ الحَياة، والجِسمُ التَعبيرِيّ، ومَجموعَةُ الدالَّة،
    /// والتَرميز، ومُستَقبِلٌ آخَرُ بِنِداءَي التَحليل — كُلُّها تُعَدّ.</summary>
    [Theory]
    [InlineData("head_guard", 1, 1)]
    [InlineData("head_guard_with_a_braced_return", 1, 1)]
    [InlineData("two_spends_behind_one_head_guard", 2, 2)]
    [InlineData("a_brace_in_a_string_does_not_shift_the_members", 2, 1)]
    [InlineData("a_fourth_handler_without_the_door", 2, 1)]
    [InlineData("door_after_the_spend", 1, 0)]
    [InlineData("door_inside_a_branch", 1, 0)]
    [InlineData("door_under_an_unbraced_if", 1, 0)]
    [InlineData("door_in_an_else", 1, 0)]
    [InlineData("door_anded", 1, 0)]
    [InlineData("door_swallowed_by_a_conditional", 1, 0)]
    [InlineData("door_that_does_not_return", 1, 0)]
    [InlineData("door_with_other_inputs", 1, 0)]
    [InlineData("door_in_a_comment", 1, 0)]
    [InlineData("door_in_a_string", 1, 0)]
    [InlineData("a_lifecycle_method", 1, 0)]
    [InlineData("an_expression_bodied_spender", 1, 0)]
    [InlineData("a_method_group_handed_on", 1, 0)]
    [InlineData("a_spend_in_the_markup", 1, 0)]
    [InlineData("another_receiver_and_the_analysis_spends", 2, 1)]
    public void The_spend_scanner_finds_every_model_call_and_accepts_only_a_head_guard(string sample, int spends, int gated)
    {
        var page = InjectedSpenders[sample];
        var calls = SpendCallsIn(WriteEndpointGuardTests.StripComments(WriteEndpointGuardTests.StripMarkupComments(page)));
        output.WriteLine($"{sample}: نِداءات {calls.Count} · مَحروسَة {calls.Count(c => c.Gated)} ("
            + string.Join(" · ", calls.Select(c => $"{c.Member} ← {c.Spend}" + (c.Gated ? "" : " [بِلا باب]"))) + ")");

        Assert.Equal(spends, calls.Count);
        Assert.Equal(gated, calls.Count(c => c.Gated));
    }

    /// <summary><b>والطَفرَةُ الَّتي مَرَّت عَلى الصَفحَةِ الحَقيقِيَّةِ تُحمِرُّ الآن</b>: الصَفحَةُ نَفسُها وفيها مُعالِجٌ رابِعٌ يُنادي <c>Agent.AskAsync</c>
    /// بِحِراسَةِ «أَلَه جَلسَة؟» وَحدَها — بِحَرفِ الطَفرَةِ الَّتي مَرَّت بِكُلِّ صَفٍّ قَبلَ الماسِح — فَيَعُدُّها الماسِحُ نِداءً رابِعاً بِلا باب، بِاسمِه،
    /// والثَلاثَةُ القائِمَةُ مَحروسَةٌ كَما هي.</summary>
    [Fact]
    public void The_spend_scanner_turns_red_on_the_real_agent_page_with_a_fourth_unguarded_handler()
    {
        var code = Code(AgentPage);
        const string anchor = "    async Task ScrollToBottom()";
        Assert.Single(Regex.Matches(code, Regex.Escape(anchor)));
        var mutated = code.Replace(anchor,
            "    async Task RetryAsync() { if (isWorking || _userId == Guid.Empty) return; session = await Agent.AskAsync(\"retry\", _scopeId); }\n\n" + anchor);

        var before = SpendCallsIn(code);
        var after = SpendCallsIn(mutated);
        output.WriteLine($"الصَفحَة: {before.Count} نِداءً، مَحروسَة {before.Count(c => c.Gated)} · بِالطَفرَة: {after.Count}، بِلا باب: "
            + string.Join(" · ", after.Where(c => !c.Gated).Select(c => $"{c.Member} ← {c.Spend}")));

        Assert.True(before.Count >= 3 && before.All(c => c.Gated), "الصَفحَةُ نَفسُها لَيسَت خَضراءَ قَبلَ الطَفرَة.");
        Assert.Equal(before.Count + 1, after.Count);
        Assert.Equal(new[] { "RetryAsync" }, after.Where(c => !c.Gated).Select(c => c.Member).ToArray());
    }

    // ─── الطَريقُ إلى الصَفحَة ──────────────────────────────────────────────────────────────────

    /// <summary>المَسارُ رَمزاً — تَنصيصٌ (مُزدَوِجٌ أَو مُفرَد) ثُمَّ <c>/studio/agent</c> ثُمَّ تَنصيصٌ أَو <c>?</c> أَو <c>#</c> أَو <c>/</c>
    /// (نَسَقُ <c>StudioAnalysisDoorTests.NewIdeaToken</c>).</summary>
    private static readonly Regex AgentToken = new(@"[""']/studio/agent(?=[""'?#/])", RegexOptions.Compiled);

    /// <summary>كُتَلُ <c>@if (StudioAgentDoor.Opens(…))</c> في صَفحَة — بِالبادِئَةِ لا بِالوُسَطاء، فَيَعُدُّها الماسِحُ أَيّاً كانَت مُدخَلاتُ البابِ.</summary>
    private static List<(int Open, int Close)> DoorBlocks(string text)
    {
        const string door = "@if (StudioAgentDoor.Opens(";
        var blocks = new List<(int, int)>();
        for (var i = text.IndexOf(door, StringComparison.Ordinal); i >= 0; i = text.IndexOf(door, i + door.Length, StringComparison.Ordinal))
        {
            var open = text.IndexOf('{', i + door.Length);
            Assert.True(open > 0, "بابٌ بِلا كُتلَة.");
            var depth = 0;
            var close = -1;
            for (var k = open; k < text.Length; k++)
            {
                if (text[k] == '{') depth++;
                else if (text[k] == '}' && --depth == 0) { close = k; break; }
            }
            Assert.True(close > open, "كُتلَةُ بابٍ لا تُغلَق — القَصُّ فاشِل.");
            blocks.Add((open, close));
        }
        return blocks;
    }

    /// <summary>مَصادِرُ الماسِح: <see cref="EntitlementContractTests.SourceFiles"/> (‏<c>.cs</c> و<c>.razor</c>) ومَعَها <c>.js</c> تَحتَ <c>libs</c>/<c>apps</c>.</summary>
    private static IEnumerable<(string File, string Text)> AgentSources()
    {
        foreach (var f in EntitlementContractTests.SourceFiles()) yield return f;
        foreach (var root in new[] { "libs", "apps" })
        {
            var dir = Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, root);
            if (!Directory.Exists(dir)) continue;
            foreach (var f in Directory.EnumerateFiles(dir, "*.js", SearchOption.AllDirectories))
            {
                var norm = f.Replace('\\', '/');
                if (norm.Contains("/obj/", StringComparison.Ordinal) || norm.Contains("/bin/", StringComparison.Ordinal)
                    || norm.Contains("/node_modules/", StringComparison.Ordinal)) continue;
                yield return (f, File.ReadAllText(f));
            }
        }
    }

    /// <summary>كُلُّ ذِكرٍ لِصَفحَةِ الوَكيلِ في المَصادِر، بِمِلَفِّه وهَل هُوَ داخِلَ كُتلَةِ باب. تَوجيهُ <c>@page</c> والتَعليقاتُ لا تُعَدّ.</summary>
    private static List<(string File, bool InsideDoor)> AgentLinksIn(IEnumerable<(string File, string Text)> sources)
    {
        var links = new List<(string, bool)>();
        foreach (var (file, text) in sources)
        {
            var rel = Path.IsPathRooted(file) ? Path.GetRelativePath(ThemeZeroEquivalenceTests.RepoRoot, file).Replace('\\', '/') : file;
            var razor = rel.EndsWith(".razor", StringComparison.OrdinalIgnoreCase);
            var code = razor ? WriteEndpointGuardTests.StripMarkupComments(text) : WriteEndpointGuardTests.StripComments(text);
            if (!code.Contains("/studio/agent", StringComparison.Ordinal)) continue;
            var blocks = razor ? DoorBlocks(code) : new List<(int Open, int Close)>();
            foreach (Match m in AgentToken.Matches(code))
            {
                var i = m.Index;
                if (i >= 6 && string.CompareOrdinal(code, i - 6, "@page ", 0, 6) == 0) continue;
                links.Add((rel, blocks.Any(b => b.Open < i && i < b.Close)));
            }
        }
        return links;
    }

    /// <summary><b>والماسِحُ يَرى ما يَعُدّ</b> (القاعِدَة ١٠): صَفحَةٌ مَحقونَةٌ فيها رابِطٌ داخِلَ بابٍ وآخَرُ بِـ<c>?x</c> خارِجَه، و<c>.cs</c>
    /// يُحَوِّلُ إلَيها، و<c>.js</c> بِتَنصيصٍ مُفرَد — والتَوجيهُ والتَعليقُ و<c>/studio/agents</c> لا تُعَدّ.</summary>
    [Fact]
    public void The_agent_link_scanner_counts_every_spelling_and_tells_inside_from_outside()
    {
        var sources = new[]
        {
            ("x/Injected.razor",
                "@page \"/studio/agent\"\n"
              + "@if (StudioAgentDoor.Opens(true, true))\n{\n    <a href=\"/studio/agent\">a</a>\n}\n"
              + "<a href=\"/studio/agent?x=1\">b</a>\n"
              + "@* <a href=\"/studio/agent\">c</a> *@\n"
              + "<a href=\"/studio/agents\">d</a>\n"),
            ("x/Injected.cs", "class X { object M() => Results.Redirect(\"/studio/agent#t\"); // \"/studio/agent\"\n}"),
            ("x/injected.js", "location.href = '/studio/agent';\n"),
        };
        var links = AgentLinksIn(sources);
        var outside = links.Where(l => !l.InsideDoor).Select(l => l.File).ToList();
        output.WriteLine($"مَحقونَة: مَعدودَة {links.Count} · داخِلَ بابٍ {links.Count - outside.Count} · خارِجَه {outside.Count} ({string.Join(" · ", outside)})");

        Assert.Equal(4, links.Count);
        Assert.Equal(1, links.Count(l => l.InsideDoor));
        Assert.Equal(new[] { "x/Injected.razor", "x/Injected.cs", "x/injected.js" }, outside);
    }

    /// <summary><b>قُلِبَ «Today_» بِاسمِه (‏2026-10-25، كوميتُ إصلاحِ <c>lens-security-2</c>)</b> — كانَ
    /// <c>Today_two_links_to_the_agent_page_are_drawn_without_asking_any_door</c> (‏<c>92bd370</c>): «روابِطُ /studio/agent: 2 · داخِلَ بابٍ: 0».
    /// والآن كُلُّ رابِطٍ إلى صَفحَةِ الوَكيلِ داخِلَ كُتلَةِ <c>@if (StudioAgentDoor.Opens(…))</c> — بِالماسِحِ لا بِقائِمَة، فَرابِطٌ يُضافُ غَداً
    /// خارِجَ البابِ يُحمِرُّ هُنا بِمِلَفِّه. ويَطبَعُ ما عَدَّه ويَفشَلُ عَلى ما دونَ المَقيس (القاعِدَة ١٠).</summary>
    [Fact]
    public void Every_link_to_the_agent_page_sits_inside_an_open_agent_door()
    {
        var links = AgentLinksIn(AgentSources());
        var outside = links.Where(l => !l.InsideDoor).ToList();
        output.WriteLine($"روابِطُ /studio/agent: {links.Count} · داخِلَ بابٍ: {links.Count - outside.Count} · خارِجَه: {outside.Count} ("
            + string.Join(" · ", links.GroupBy(l => l.File).Select(g => $"{g.Key}: {g.Count()}")) + ")");

        Assert.True(links.Count >= 2, $"أَداةٌ عَمياء: {links.Count} رابِطاً — والمَقيسُ 2.");
        Assert.Empty(outside);
    }

    /// <summary><c>StudioHome</c> تَسأَلُ البابَ بِقَرارِ الحارِسِ الواحِدِ عَلى صاحِبِ الجَلسَةِ الَّذي تُحَمِّلُه — لا تَعريفَ ثانٍ لِـ«مُشرِف مَنَصَّة».</summary>
    [Fact]
    public void The_home_page_feeds_the_agent_door_from_the_one_admin_decision()
    {
        var code = Code(HomePage);
        var feeds = Regex.Matches(code, Regex.Escape("isPlatformAdmin = Services.PlatformAdminGuard.Decide(u) == Services.PlatformAdminOutcome.Allowed;")).Count;
        var asks = Regex.Matches(code, Regex.Escape("@if (StudioAgentDoor.Opens(Agent.IsConfigured, isPlatformAdmin))")).Count;
        var ownDefinition = Regex.Matches(code, Regex.Escape("IsPlatformAdmin")).Count;
        output.WriteLine($"StudioHome: يُغَذّي البابَ مِن Decide: {feeds} · كُتَلُ البابِ: {asks} · قِراءَةُ IsPlatformAdmin مُباشَرَةً: {ownDefinition}");

        Assert.Equal(1, feeds);
        Assert.Equal(2, asks);
        Assert.Equal(0, ownDefinition);
    }

    // ─── البابُ نَفسُه: دالَّةٌ نَقِيَّة ───────────────────────────────────────────────────────────

    /// <summary>‏(‏2026-10-25، <c>lens-dead-doors-1</c>) البابُ بِمُدخَلَين: مِفتاحُ وَكيلِ <c>Studio</c> وقَرارُ المُشرِف — يُفتَحُ بِهِما مَعاً وَحدَهُما.</summary>
    [Fact]
    public void The_agent_door_opens_for_the_platform_admin()
        => Assert.True(ACommerce.Templates.Customer.Marketplace.Services.Incubator.StudioAgentDoor.Opens(agentBackendConfigured: true, platformAdmin: true));

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public void The_agent_door_stays_closed_for_every_other_studio_user(bool configured, bool admin)
        => Assert.False(ACommerce.Templates.Customer.Marketplace.Services.Incubator.StudioAgentDoor.Opens(configured, admin));

    /// <summary>وبِلا مِفتاحِ وَكيلِ <c>Studio</c> يُغلَقُ ولَو لِلمُشرِف — كانَ يُفتَحُ عَلى مُحَرِّرٍ مُعَطَّل (القاعِدَة ١٢).</summary>
    [Fact]
    public void The_agent_door_stays_closed_without_a_configured_studio_backend_even_for_the_admin()
        => Assert.False(ACommerce.Templates.Customer.Marketplace.Services.Incubator.StudioAgentDoor.Opens(agentBackendConfigured: false, platformAdmin: true));

    /// <summary>كُلُّ مَن يَسأَلُ البابَ يُعطيه مُدخَلَيه بِحَرفِهِما — مِفتاحَ الخَلفِيَّةِ مِن <c>AgentService.IsConfigured</c> وقَرارَ المُشرِفِ القائِم —
    /// لا ثابِتاً ولا مُدخَلاً واحِداً؛ ويَعُدُّ ويَفشَلُ عَلى ما دونَ المَقيس (القاعِدَة ١٠).</summary>
    [Fact]
    public void Every_agent_door_asker_feeds_it_the_backend_configuration_and_the_admin_decision()
    {
        var calls = new List<(string File, string Args)>();
        foreach (var (file, text) in AgentSources())
        {
            var rel = Path.GetRelativePath(ThemeZeroEquivalenceTests.RepoRoot, file).Replace('\\', '/');
            if (rel.EndsWith("/StudioAgentDoor.cs", StringComparison.Ordinal)) continue;
            var code = WriteEndpointGuardTests.StripComments(WriteEndpointGuardTests.StripMarkupComments(text));
            foreach (Match m in Regex.Matches(code, @"StudioAgentDoor\.Opens\((?<args>[^()]*)\)"))
                calls.Add((rel, m.Groups["args"].Value.Trim()));
        }
        output.WriteLine($"نِداءاتُ StudioAgentDoor.Opens: {calls.Count} — " + string.Join(" · ", calls.GroupBy(c => (Path.GetFileName(c.File), c.Args)).Select(g => $"{g.Key.Item1}({g.Key.Args})×{g.Count()}")));

        Assert.True(calls.Count >= 8, $"أَداةٌ عَمياء: {calls.Count} نِداءً — والمَقيسُ 8 (صَفحَةُ الوَكيلِ 6، ولَوحَتي 2).");
        Assert.All(calls, c => Assert.Contains(c.Args, new[] { "Agent.IsConfigured, _platformAdmin", "Agent.IsConfigured, isPlatformAdmin" }));
    }

    /// <summary>المُحادَثَةُ والمُحَرِّرُ داخِلَ فَرعِ <c>else if (StudioAgentDoor.Opens(…))</c>، و<c>else</c>ـه يَرسُمُ مِفتاحَ القامُوسِ بِلا مُحَرِّرٍ ولا زِرّ.</summary>
    [Fact]
    public void The_agent_page_draws_the_conversation_only_inside_the_open_branch_and_the_closed_text_otherwise()
    {
        var code = Code(AgentPage);
        const string open = "else if (StudioAgentDoor.Opens(";
        var openAt = code.IndexOf(open, StringComparison.Ordinal);
        Assert.True(openAt > 0, "الصَفحَةُ لا تَسأَلُ البابَ في رَسمِها.");
        Assert.Equal(openAt, code.LastIndexOf(open, StringComparison.Ordinal));
        var block = Body(code[openAt..], open);
        var closeAt = openAt + code[openAt..].IndexOf(block, StringComparison.Ordinal) + block.Length;
        var markupEnd = code.IndexOf("@code", StringComparison.Ordinal);
        var textareas = Regex.Matches(code[..markupEnd], "<textarea").Count;
        output.WriteLine($"<textarea> في الصَفحَة: {textareas} · داخِلَ الفَرعِ المَفتوح: {Regex.Matches(block, "<textarea").Count} · "
            + $"@onclick داخِلَه: {Regex.Matches(block, "@onclick").Count} · خارِجَه: {Regex.Matches(code[..markupEnd], "@onclick").Count - Regex.Matches(block, "@onclick").Count}");

        Assert.Equal(1, textareas);
        Assert.Contains("<textarea", block, StringComparison.Ordinal);
        Assert.Equal(Regex.Matches(code[..markupEnd], "@onclick").Count, Regex.Matches(block, "@onclick").Count);
        var after = code[closeAt..markupEnd].TrimStart();
        Assert.StartsWith("else", after, StringComparison.Ordinal);
        var key = after.IndexOf("StudioAgentDoor.ClosedMessageKey", StringComparison.Ordinal);
        Assert.True(key > 0 && key < 200, "فَرعُ البابِ المُغلَقِ لا يَرسُمُ نَصَّ القامُوس.");
    }

    /// <summary>نَصُّ البابِ المُغلَقِ في القامُوسِ العَرَبيّ (القاعِدَة ١١) — والمِفتاحُ مِن الثابِتِ نَفسِه لا مَنسوخاً.</summary>
    [Fact]
    public void The_closed_agent_door_text_lives_in_the_arabic_dictionary()
    {
        var ar = Read("libs/core/ACommerce.Platform.I18n/Locales/ar.json");
        using var doc = System.Text.Json.JsonDocument.Parse(ar);
        var key = ACommerce.Templates.Customer.Marketplace.Services.Incubator.StudioAgentDoor.ClosedMessageKey;
        Assert.True(doc.RootElement.TryGetProperty(key, out var v), $"مِفتاحٌ غائِبٌ عَن ar.json: {key}");
        Assert.False(string.IsNullOrWhiteSpace(v.GetString()));
        output.WriteLine($"{key} = {v.GetString()}");
    }

    // ─── ما تَرسُمُه الصَفحَةُ بِلا مِفتاح ────────────────────────────────────────────────────────

    /// <summary><b>قُلِبَ «Today_» بِاسمِه (‏2026-10-25، كوميتُ إصلاحِ <c>lens-dead-doors-1</c>)</b> — كانَ
    /// <c>Today_the_agent_page_prints_its_provider_model_and_an_operator_setting</c> (‏<c>92bd370</c>): «سَطرُ المُزَوِّد/النَموذَج 1 · Agent:ApiKey 1 ·
    /// agent.key.missing_prefix 1 · !Agent.IsConfigured 3». والآن لا يُسَمّي الصَفحَةُ إعداداً ولا تَسأَلُ المِفتاحَ إلّا في البابِ نَفسِه، واسمُ المُزَوِّدِ
    /// والنَموذَجِ داخِلَ الفَرعِ المَفتوحِ وَحدَه — لِمُشرِفِ المَنَصَّةِ بِمِفتاحٍ، أَي لِمُشَغِّلِه — والمُغلَقُ نَصُّ القامُوسِ بِلا اسمٍ ولا مِفتاح.</summary>
    [Fact]
    public void The_agent_page_names_no_setting_and_shows_its_provider_and_model_only_inside_the_open_door()
    {
        var code = Code(AgentPage);
        var providerModel = Regex.Matches(code, Regex.Escape("@Agent.ProviderName / @Agent.ModelName")).Count;
        var setting = Regex.Matches(code, Regex.Escape("Agent:ApiKey")).Count;
        var missing = Regex.Matches(code, Regex.Escape("agent.key.missing_prefix")).Count;
        var notConfigured = Regex.Matches(code, Regex.Escape("!Agent.IsConfigured")).Count;
        output.WriteLine($"سَطرُ المُزَوِّدِ/النَموذَج: {providerModel} · «Agent:ApiKey»: {setting} · agent.key.missing_prefix: {missing} · !Agent.IsConfigured: {notConfigured}");

        Assert.Equal(1, providerModel);
        Assert.Equal(0, setting);
        Assert.Equal(0, missing);
        Assert.Equal(0, notConfigured);
        const string open = "else if (StudioAgentDoor.Opens(Agent.IsConfigured, _platformAdmin))";
        var openAt = code.IndexOf(open, StringComparison.Ordinal);
        Assert.True(openAt > 0, "الفَرعُ المَفتوحُ لا يَسأَلُ المِفتاح.");
        var block = Body(code[openAt..], open);
        Assert.Contains("@Agent.ProviderName / @Agent.ModelName", block, StringComparison.Ordinal);
    }
}
