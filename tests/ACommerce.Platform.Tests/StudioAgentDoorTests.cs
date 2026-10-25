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

    /// <summary><b>«اليَوم»</b>: المُعالِجاتُ الثَلاثَةُ تَبلُغُ النِداءَ بَعدَ سُؤالٍ واحِد — «أَلِلصَفحَةِ مُستَخدِم؟» (‏<c>_userId == Guid.Empty</c>)
    /// — ولا يَذكُرُ كودُ الصَفحَةِ حارِسَ مُشرِفِ المَنَصَّةِ ولا باباً. يُقلَبُ بِاسمِه في كوميتِ إصلاحِ <c>lens-security-2</c>.</summary>
    [Fact]
    public void Today_every_agent_page_handler_that_spends_asks_only_for_a_signed_in_studio_user()
    {
        var code = Code(AgentPage);
        var spending = 0;
        foreach (var (signature, spend) in SpendingHandlers)
        {
            var body = Body(code, signature);
            var spendAt = body.IndexOf(spend, StringComparison.Ordinal);
            Assert.True(spendAt > 0, $"{signature}: لا يَبلُغُ {spend} — الأَداةُ لا تَرى ما تَقيس.");
            spending++;
            var guard = body.IndexOf("_userId == Guid.Empty", StringComparison.Ordinal);
            Assert.True(guard > 0 && guard < spendAt, $"{signature}: سُؤالُ المُستَخدِمِ لَيسَ قَبلَ النِداء.");
        }
        var adminGuard = Regex.Matches(code, "PlatformAdminGuard").Count;
        var door = Regex.Matches(code, "StudioAgentDoor").Count;
        output.WriteLine($"مُعالِجاتٌ تُنفِق: {spending} · تَسأَلُ حارِسَ المُشرِف: {adminGuard} · تَسأَلُ باباً: {door}");

        Assert.Equal(3, spending);
        Assert.Equal(0, adminGuard);
        Assert.Equal(0, door);
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

    /// <summary><b>«اليَوم»</b>: رابِطانِ إلى <c>/studio/agent</c> في <c>StudioHome</c> (زِرُّ الشَريطِ وبِطاقَةُ «وَكيل التَّطبيقات») خارِجَ أَيِّ باب —
    /// يُرسَمانِ لِكُلِّ صاحِبِ جَلسَةٍ بِمِفتاحٍ وبِدونِه (مَقيسٌ حَيّاً أَعلاه: ‏2 عَلى <c>/studio</c> و1 عَلى <c>/studio/ideas</c> في الحالَتَين).
    /// يُقلَبُ بِاسمِه في كوميتِ إصلاحِ <c>lens-security-2</c>.</summary>
    [Fact]
    public void Today_two_links_to_the_agent_page_are_drawn_without_asking_any_door()
    {
        var links = AgentLinksIn(AgentSources());
        var outside = links.Where(l => !l.InsideDoor).ToList();
        output.WriteLine($"روابِطُ /studio/agent: {links.Count} · داخِلَ بابٍ: {links.Count - outside.Count} · خارِجَه: {outside.Count} ("
            + string.Join(" · ", links.GroupBy(l => l.File).Select(g => $"{g.Key}: {g.Count()}")) + ")");

        Assert.Equal(2, links.Count);
        Assert.Equal(2, outside.Count);
        Assert.All(links, l => Assert.Equal(HomePage, l.File));
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
