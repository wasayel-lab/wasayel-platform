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

    /// <summary>مُعالِجاتُ الصَفحَةِ الَّتي تَبلُغُ نَموذَجَ اللُغَة، والنِداءُ الَّذي تَبلُغُه — نَفسُ
    /// <see cref="LanguageModelQuotaGateTests.LlmReachingMethods"/> لا قائِمَةٌ ثانِيَة.</summary>
    private static readonly (string Signature, string Spend)[] SpendingHandlers =
    {
        ("async Task SendAsync()", "Agent.AskAsync("),
        ("async Task ApplyAsync(string toolId)", "Agent.ContinueAfterToolAsync("),
        ("async Task RejectAsync(string toolId)", "Agent.ContinueAfterToolAsync("),
    };

    // ─── الصَفحَة: مَن يُنفِقُ مِنها ──────────────────────────────────────────────────────────────

    /// <summary><b>قُلِبَ «Today_» بِاسمِه (‏2026-10-25، كوميتُ إصلاحِ <c>lens-security-2</c>)</b> — كانَ
    /// <c>Today_every_agent_page_handler_that_spends_asks_only_for_a_signed_in_studio_user</c> (‏<c>92bd370</c>): «مُعالِجاتٌ تُنفِق: 3 · حارِسُ
    /// المُشرِف: 0 · باب: 0». والآن كُلُّ مُعالِجٍ يُنفِقُ (ومَعَها «مَسح» الَّذي يَكتُبُ الجَلسَة) يَسأَلُ <c>StudioAgentDoor.Opens</c> قَبلَ نِدائِه،
    /// ومُدخَلُ البابِ قَرارُ <c>PlatformAdminGuard.EvaluateAsync</c> يُقرَأُ حَيثُ يُقرَأُ المُستَخدِم، وتَحميلُ المُحادَثَةِ خَلفَه.</summary>
    [Fact]
    public void Every_agent_page_handler_that_spends_asks_the_open_agent_door_before_its_call()
    {
        const string door = "!StudioAgentDoor.Opens(_platformAdmin)";
        var code = Code(AgentPage);
        var spending = 0;
        var gated = 0;
        foreach (var (signature, spend) in SpendingHandlers)
        {
            var body = Body(code, signature);
            var spendAt = body.IndexOf(spend, StringComparison.Ordinal);
            Assert.True(spendAt > 0, $"{signature}: لا يَبلُغُ {spend} — الأَداةُ لا تَرى ما تَقيس.");
            spending++;
            var doorAt = body.IndexOf(door, StringComparison.Ordinal);
            if (doorAt > 0 && doorAt < spendAt) gated++;
        }
        var reset = Body(code, "async Task ResetAsync()");
        var resetGated = reset.IndexOf(door, StringComparison.Ordinal) is var r && r > 0
                         && r < reset.IndexOf("Agent.ResetAsync(", StringComparison.Ordinal);
        output.WriteLine($"مُعالِجاتٌ تُنفِق: {spending} · تَسأَلُ البابَ قَبلَ نِدائِها: {gated} · «مَسح» يَسأَلُه: {resetGated}");

        Assert.Equal(3, spending);
        Assert.Equal(spending, gated);
        Assert.True(resetGated, "ResetAsync يَكتُبُ الجَلسَةَ قَبلَ سُؤالِ الباب.");

        var init = Body(code, "protected override async Task OnInitializedAsync()");
        var userAt = init.IndexOf("_userId = StudioAuth.UserId!.Value;", StringComparison.Ordinal);
        var adminAt = init.IndexOf("_platformAdmin = (await Services.PlatformAdminGuard.EvaluateAsync(Store, StudioAuth)).Allowed;", StringComparison.Ordinal);
        var loadAt = init.IndexOf("if (_userId != Guid.Empty && StudioAgentDoor.Opens(_platformAdmin))", StringComparison.Ordinal);
        Assert.True(userAt > 0 && adminAt > userAt, "قَرارُ المُشرِفِ لا يُقرَأُ مَعَ المُستَخدِم.");
        Assert.True(loadAt > adminAt && loadAt < init.IndexOf("Agent.LoadSessionAsync(", StringComparison.Ordinal),
            "المُحادَثَةُ تُحَمَّلُ قَبلَ سُؤالِ الباب.");
        Assert.Equal(1, Regex.Matches(code, "_platformAdmin = ").Count);
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
        var asks = Regex.Matches(code, Regex.Escape("@if (StudioAgentDoor.Opens(isPlatformAdmin))")).Count;
        var ownDefinition = Regex.Matches(code, Regex.Escape("IsPlatformAdmin")).Count;
        output.WriteLine($"StudioHome: يُغَذّي البابَ مِن Decide: {feeds} · كُتَلُ البابِ: {asks} · قِراءَةُ IsPlatformAdmin مُباشَرَةً: {ownDefinition}");

        Assert.Equal(1, feeds);
        Assert.Equal(2, asks);
        Assert.Equal(0, ownDefinition);
    }

    // ─── البابُ نَفسُه: دالَّةٌ نَقِيَّة ───────────────────────────────────────────────────────────

    [Fact]
    public void The_agent_door_opens_for_the_platform_admin()
        => Assert.True(ACommerce.Templates.Customer.Marketplace.Services.Incubator.StudioAgentDoor.Opens(platformAdmin: true));

    [Fact]
    public void The_agent_door_stays_closed_for_every_other_studio_user()
        => Assert.False(ACommerce.Templates.Customer.Marketplace.Services.Incubator.StudioAgentDoor.Opens(platformAdmin: false));

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

    /// <summary><b>«اليَوم»</b>: الصَفحَةُ تَطبَعُ اسمَ المُزَوِّدِ والنَموذَجِ لِكُلِّ داخِل، وبِلا مِفتاحٍ نَصَّ مُشَغِّلٍ يُسَمّي إعداداً
    /// (‏<c>Agent:ApiKey</c>) بَينَ مَفاتيحِ القامُوس، ثُمَّ مُحَرِّراً مُعَطَّلاً. يُقلَبُ بِاسمِه في كوميتِ إصلاحِ <c>lens-dead-doors-1</c>.</summary>
    [Fact]
    public void Today_the_agent_page_prints_its_provider_model_and_an_operator_setting()
    {
        var code = Code(AgentPage);
        var providerModel = Regex.Matches(code, Regex.Escape("@Agent.ProviderName / @Agent.ModelName")).Count;
        var setting = Regex.Matches(code, Regex.Escape("Agent:ApiKey")).Count;
        var missing = Regex.Matches(code, Regex.Escape("agent.key.missing_prefix")).Count;
        var disabled = Regex.Matches(code, Regex.Escape("!Agent.IsConfigured")).Count;
        output.WriteLine($"سَطرُ المُزَوِّدِ/النَموذَج: {providerModel} · «Agent:ApiKey»: {setting} · agent.key.missing_prefix: {missing} · !Agent.IsConfigured: {disabled}");

        Assert.Equal(1, providerModel);
        Assert.Equal(1, setting);
        Assert.Equal(1, missing);
        Assert.Equal(3, disabled);
    }
}
