using System.Diagnostics;
using System.Text.RegularExpressions;
using ACommerce.Templates.Customer.Marketplace.Services;
using ACommerce.Templates.Customer.Marketplace.Services.Incubator;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ وَكيلُ التَصميمِ خَلفَ مِفتاحٍ صَريحٍ مُطفَأٍ افتِراضاً (‏2026-11-07، الموجَة ١٣ W13-1·٦ — ADR-049 §٦) ═══
//
// **المَقيسُ قَبل**: الوَكيلُ (‏`/studio/agent` و`/admin/agent`) يُفتَحُ لِمُشرِفِ المَنَصَّةِ بِمُجَرَّدِ أَن يوجَدَ مِفتاحُ نَموذَجٍ لِمِلَفِّ `Studio` — ومِلَفُّه
// يَسقُطُ إلى `Agent__ApiKey` المُشتَرَك، فَمِفتاحٌ يُضبَطُ لِحاضِنَةِ المُشرِفِ يَفتَحُه أَيضاً. «مَخفِيّ» كانَ نَتيجَةً جانِبِيَّةً لِغِيابِ مِفتاح.
// **وما يُثَبَّت**: (١) المِفتاحُ مُطفَأٌ ما لَم يُضَأ `true` صَراحَةً — لِكُلِّ مُدخَلٍ موجَبٌ وسالِب (القاعِدَة ٤)؛ (٢) والوَكيلُ نَفسُه لا يُنادي
// خَلفِيَّتَه ولا يَكتُبُ والمِفتاحُ مُطفَأ، أَيّاً كانَ المُنادي؛ (٣) ولا رابِطَ إلى الوَكيلِ إلّا داخِلَ كُتلَةِ المِفتاح، والصَفحَتانِ والنِقاطُ الأَربَعُ تَسأَلُه؛
// (٤) وسَطرُ الإقلاعِ وكُتلَةُ البَوّابَةِ يَقولانِ الحالَ بِرَمزِه. **واختِباراتُ الوَكيلِ القائِمَةُ بِلا تَعديلِ حَرف** — بَصمَتُها في `StudioStoreBirthCharacterizationTests`.

public class StudioAgentSwitchTests(ITestOutputHelper output)
{
    private const string Template = "libs/templates/ACommerce.Templates.Customer.Marketplace";
    private static string Read(string rel) => File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, rel));

    // ─── (١) المُدخَل — موجَبٌ وسالِب ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("false", false)]
    [InlineData("0", false)]
    [InlineData("1", false)]
    [InlineData("yes", false)]
    [InlineData("enabled", false)]
    [InlineData("true", true)]
    [InlineData("True", true)]
    [InlineData(" TRUE ", true)]
    public void Only_an_explicit_true_lights_the_switch(string? raw, bool on)
        => Assert.Equal(on, StudioAgentSwitch.Parse(raw));

    [Theory]
    [InlineData(null, false)]
    [InlineData("false", false)]
    [InlineData("true", true)]
    public void The_switch_reads_its_one_setting_and_is_off_when_it_is_absent(string? value, bool on)
    {
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(
            value is null ? [] : [new KeyValuePair<string, string?>("Studio:Agent:Enabled", value)]).Build();
        Assert.Equal("Studio:Agent:Enabled", StudioAgentSwitch.Setting);
        Assert.Equal(on, StudioAgentSwitch.From(cfg).Enabled);
    }

    [Theory]
    [InlineData(false, false, "hidden")]
    [InlineData(false, true, "hidden")]
    [InlineData(true, true, "open")]
    [InlineData(true, false, "agent_unavailable")]
    public void The_boot_value_names_the_state(bool enabled, bool backend, string expected)
        => Assert.Equal(expected, StudioAgentSwitch.BootLogValue(enabled, backend));

    /// <summary>التَركيبُ نَفسُه يُسَجِّلُ المِفتاحَ مِن الإعداد — والغائِبُ مُطفَأ.</summary>
    [Theory]
    [InlineData(null, false)]
    [InlineData("true", true)]
    public void The_template_registers_the_switch_from_configuration(string? value, bool on)
    {
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(
            value is null ? [] : [new KeyValuePair<string, string?>(StudioAgentSwitch.Setting, value)]).Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(cfg);
        ACommerce.Templates.Customer.Marketplace.MarketplaceTemplateExtensions.AddCustomerMarketplaceTemplate(services);
        using var sp = services.BuildServiceProvider();
        Assert.Equal(on, sp.GetRequiredService<StudioAgentSwitch>().Enabled);
    }

    // ─── (٢) الوَكيلُ لا يُنادي ولا يَكتُبُ والمِفتاحُ مُطفَأ ─────────────────────────────────────────────

    /// <summary>بِلا حاقِنٍ (بِناءٌ يَدَوِيّ) وبِمِفتاحٍ مُطفَأ: <c>AskAsync</c> و<c>ContinueAfterToolAsync</c> يُعيدانِ الجَلسَةَ كَما هي — صِفرُ نِداءٍ لِلخَلفِيَّة
    /// وصِفرُ حِفظ. (والموجَبُ — مُضاءٌ ⇐ ثَلاثَةُ نِداءات — بُرهانُ الدائِرَةِ الحَيُّ القائِمُ في <c>LiveSessionCircuitProofTests</c> بِالمُضيفِ المُضاء.)</summary>
    [Theory]
    [InlineData("ask", false)]
    [InlineData("ask", true)]
    [InlineData("continue", false)]
    [InlineData("continue", true)]
    public async Task An_unlit_agent_calls_no_backend_and_writes_nothing(string what, bool injectedOff)
    {
        var world = new DocWorld();
        var probe = new ProbeAgentBackends();
        var agent = injectedOff ? new AgentService(world.Store, probe, new StudioAgentSwitch(false)) : new AgentService(world.Store, probe);
        Assert.False(agent.Enabled);
        Assert.True(agent.IsConfigured);   // مَعنى `IsConfigured` باقٍ: خَلفِيَّةٌ مَضبوطَة — والمِفتاحُ شَيءٌ آخَر.
        var session = what == "ask" ? await agent.AskAsync("أَضِف فِئَة", "studio-probe") : await agent.ContinueAfterToolAsync("studio-probe");
        Assert.NotNull(session);
        Assert.Empty(session.Turns);
        Assert.Equal(0, probe.Calls);
        Assert.Equal(0, world.SaveCalls);
    }

    [Theory]
    [InlineData("public async Task<AgentSession> AskAsync(", "if (!Enabled) return await LoadSessionAsync(scopeId, ct);")]
    [InlineData("public async Task<AgentSession> ContinueAfterToolAsync(", "if (!Enabled) return await LoadSessionAsync(scopeId, ct);")]
    [InlineData("private async Task CallBackendAsync(", "if (!Enabled) return;")]
    public void Every_agent_entry_asks_the_switch_as_its_first_statement(string signature, string guard)
    {
        var code = WriteEndpointGuardTests.StripComments(Read($"{Template}/Services/AgentService.cs"));
        var at = code.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(at > 0, signature);
        var body = code[(code.IndexOf('{', at) + 1)..].TrimStart();
        Assert.StartsWith(guard, body, StringComparison.Ordinal);
    }

    // ─── (٣) لا رابِطَ إلى الوَكيلِ خارِجَ كُتلَةِ المِفتاح، والصَفحَتانِ والنِقاطُ تَسأَلُه ──────────────────────────

    private static readonly Regex AgentLink = new(@"href=""/(?:studio|admin)/agent""", RegexOptions.Compiled);
    private const string SwitchBlock = "@if (AgentSwitch.Enabled)";

    /// <summary>كُلُّ رابِطٍ إلى صَفحَتَي الوَكيلِ في <c>.razor</c> — وهَل هُوَ داخِلَ كُتلَةِ <c>@if (AgentSwitch.Enabled)</c>.</summary>
    internal static List<(string File, bool Inside)> AgentLinksIn(IEnumerable<(string File, string Text)> pages)
    {
        var links = new List<(string, bool)>();
        foreach (var (file, text) in pages)
        {
            var code = WriteEndpointGuardTests.StripMarkupComments(text);
            var blocks = new List<(int Open, int Close)>();
            for (var at = code.IndexOf(SwitchBlock, StringComparison.Ordinal); at >= 0; at = code.IndexOf(SwitchBlock, at + 1, StringComparison.Ordinal))
            {
                var open = code.IndexOf('{', at);
                int depth = 0, i = open;
                for (; i < code.Length; i++) { if (code[i] == '{') depth++; else if (code[i] == '}' && --depth == 0) break; }
                blocks.Add((open, i));
            }
            foreach (Match m in AgentLink.Matches(code))
                links.Add((file, blocks.Any(b => b.Open < m.Index && m.Index < b.Close)));
        }
        return links;
    }

    [Fact]
    public void Every_link_to_an_agent_page_sits_inside_the_switch()
    {
        var pages = Directory.EnumerateFiles(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, Template, "Components"), "*.razor", SearchOption.AllDirectories)
            .Select(f => (Path.GetRelativePath(ThemeZeroEquivalenceTests.RepoRoot, f).Replace('\\', '/'), File.ReadAllText(f))).ToList();
        var links = AgentLinksIn(pages);
        var outside = links.Where(l => !l.Inside).Select(l => l.File).ToList();
        output.WriteLine($"صَفَحاتٌ مَفحوصَة {pages.Count} · روابِطُ الوَكيل {links.Count} · خارِجَ المِفتاح {outside.Count} — "
            + string.Join(" · ", links.GroupBy(l => Path.GetFileName(l.File)).Select(g => $"{g.Key}: {g.Count()}")));
        Assert.True(links.Count >= 7, $"أَداةٌ عَمياء: {links.Count} رابِطاً — والمَقيسُ 7 (StudioHome ‏2، والإدارَةُ 5).");
        Assert.True(outside.Count == 0, "رابِطٌ إلى الوَكيلِ خارِجَ المِفتاح: " + string.Join("، ", outside));
    }

    [Fact]
    public void The_agent_link_scanner_tells_inside_from_outside()
    {
        var links = AgentLinksIn([
            ("x/A.razor", "@if (AgentSwitch.Enabled)\n{\n@if (StudioAgentDoor.Opens(true, true))\n{\n<a href=\"/studio/agent\">a</a>\n}\n}\n<a href=\"/admin/agent\">b</a>\n@* <a href=\"/admin/agent\">c</a> *@\n"),
        ]);
        Assert.Equal(new[] { true, false }, links.Select(l => l.Inside).ToArray());
    }

    [Theory]
    [InlineData("/admin/agent/ask")]
    [InlineData("/admin/agent/tool/{toolId}/apply")]
    [InlineData("/admin/agent/tool/{toolId}/reject")]
    [InlineData("/admin/agent/reset")]
    public void Every_admin_agent_door_asks_the_switch_after_the_admin_guard_and_before_the_agent(string route)
    {
        var body = WriteEndpointGuardTests.AllMinimalApiEndpoints().Single(e => e.Route == route).Body;
        var guard = body.IndexOf("PlatformAdminGuard.EvaluateAsync", StringComparison.Ordinal);
        var sw = body.IndexOf("if (!agentSwitch.Enabled) return Results.Redirect(\"/admin/agent\");", StringComparison.Ordinal);
        var agent = Regex.Match(body, @"\bagent\.\w+\(|\bexec\.\w+\(").Index;
        Assert.True(guard > 0 && sw > guard && agent > sw, $"{route}: الحارِس@{guard} · المِفتاح@{sw} · الوَكيل@{agent}");
    }

    [Fact]
    public void The_studio_agent_page_draws_the_closed_text_before_it_asks_the_door()
    {
        var code = WriteEndpointGuardTests.StripMarkupComments(Read($"{Template}/Components/Pages/StudioAgent.razor"));
        var off = code.IndexOf("else if (!AgentSwitch.Enabled)", StringComparison.Ordinal);
        var door = code.IndexOf("else if (StudioAgentDoor.Opens(", StringComparison.Ordinal);
        Assert.True(off > 0 && off < door, "فَرعُ المِفتاحِ المُطفَأِ لا يَسبِقُ الباب.");
        var branch = code[off..door];
        Assert.Contains("StudioAgentDoor.ClosedMessageKey", branch, StringComparison.Ordinal);
        Assert.DoesNotContain("<textarea", branch, StringComparison.Ordinal);
        Assert.DoesNotContain("@onclick", branch, StringComparison.Ordinal);
    }

    // ‏(‏2026-11-07، مُراجَعَةُ C1 · W13-1·F3) **والمُعالِجاتُ الأَربَعَةُ تَسأَلُ المِفتاحَ قَبلَ الباب** — ADR-049 §٦ قالَها («الفرعُ المرسومُ ومعالجاتُه الأربعة
    // قبلَ أيِّ نداء») والكودُ لَم يَكُن يَقولُها: كانَت تَسأَلُ البابَ وَحدَه، و`ApplyAsync` يَكتُبُ بِـ`Executor`. والماسِحُ يُقاسُ بِعَيِّناتٍ مَحقونَة.

    internal static readonly string[] AgentPageHandlers = ["SendAsync", "ApplyAsync", "RejectAsync", "ResetAsync"];

    /// <summary>أَوَّلُ عِبارَةٍ في جِسمِ المُعالِج <c>if (…) return;</c>، وفي شُقوقِ <c>||</c> العُليا لِشَرطِها <c>!AgentSwitch.Enabled</c> قَبلَ
    /// <c>!StudioAgentDoor.Opens(</c> — لا بَعدَه، ولا مَربوطاً بِـ<c>&amp;&amp;</c>، ولا في عِبارَةٍ ثانِيَة.</summary>
    internal static bool AsksTheSwitchBeforeTheDoor(string code, string handler)
    {
        var at = code.IndexOf($"async Task {handler}(", StringComparison.Ordinal);
        if (at < 0) return false;
        var body = code[(code.IndexOf('{', at) + 1)..].TrimStart();
        var head = body[..(body.IndexOf(';') + 1)];
        var m = Regex.Match(head, @"^if \((?<cond>.*)\) return;$", RegexOptions.Singleline);
        if (!m.Success) return false;
        var parts = m.Groups["cond"].Value.Split("||").Select(p => p.Trim()).ToList();
        var sw = parts.IndexOf("!AgentSwitch.Enabled");
        var door = parts.FindIndex(p => p.StartsWith("!StudioAgentDoor.Opens(", StringComparison.Ordinal));
        return sw >= 0 && door > sw;
    }

    [Fact]
    public void Every_studio_agent_handler_asks_the_switch_before_the_door()
    {
        var code = WriteEndpointGuardTests.StripComments(WriteEndpointGuardTests.StripMarkupComments(Read($"{Template}/Components/Pages/StudioAgent.razor")));
        var found = AgentPageHandlers.Where(h => code.Contains($"async Task {h}(", StringComparison.Ordinal)).ToList();
        var asking = found.Where(h => AsksTheSwitchBeforeTheDoor(code, h)).ToList();
        output.WriteLine($"مُعالِجاتُ /studio/agent: {found.Count} · تَسأَلُ المِفتاحَ قَبلَ الباب: {asking.Count} ({string.Join("، ", asking)})");
        Assert.Equal(AgentPageHandlers.Length, found.Count);
        Assert.Equal(found, asking);
    }

    [Theory]
    [InlineData("    async Task SendAsync()\n    {\n        if (isWorking || !AgentSwitch.Enabled || !StudioAgentDoor.Opens(Agent.IsConfigured, _platformAdmin)) return;\n", true)]
    [InlineData("    async Task SendAsync()\n    {\n        if (isWorking || !StudioAgentDoor.Opens(Agent.IsConfigured, _platformAdmin)) return;\n", false)]
    [InlineData("    async Task SendAsync()\n    {\n        if (isWorking || !StudioAgentDoor.Opens(Agent.IsConfigured, _platformAdmin) || !AgentSwitch.Enabled) return;\n", false)]
    [InlineData("    async Task SendAsync()\n    {\n        if (isWorking && !AgentSwitch.Enabled || !StudioAgentDoor.Opens(Agent.IsConfigured, _platformAdmin)) return;\n", false)]
    [InlineData("    async Task SendAsync()\n    {\n        if (isWorking || !StudioAgentDoor.Opens(Agent.IsConfigured, _platformAdmin)) return;\n        if (!AgentSwitch.Enabled) return;\n", false)]
    [InlineData("    async Task SendAsync()\n    {\n        if (!AgentSwitch.Enabled) isWorking = true;\n", false)]
    public void The_handler_switch_scanner_goes_red_on_a_missing_late_or_weakened_switch(string sample, bool asks)
        => Assert.Equal(asks, AsksTheSwitchBeforeTheDoor(sample, "SendAsync"));

    [Fact]
    public void The_admin_agent_page_draws_its_interactive_panel_only_while_the_switch_is_lit()
    {
        var code = WriteEndpointGuardTests.StripMarkupComments(Read($"{Template}/Components/Pages/Admin/AgentChat.razor"));
        var guard = code.IndexOf("<RequirePlatformAdmin>", StringComparison.Ordinal);
        var sw = code.IndexOf(SwitchBlock, StringComparison.Ordinal);
        var panel = code.IndexOf("<AgentChatPanel", StringComparison.Ordinal);
        var closed = code.IndexOf("StudioAgentDoor.ClosedMessageKey", StringComparison.Ordinal);
        Assert.True(guard > 0 && sw > guard && panel > sw && closed > panel, $"الحارِس@{guard} · المِفتاح@{sw} · اللَوحَة@{panel} · المُغلَق@{closed}");
        Assert.Equal(1, Regex.Matches(code, "<AgentChatPanel").Count);
    }

    // ─── (٤) سَطرُ الإقلاعِ وكُتلَةُ البَوّابَة ──────────────────────────────────────────────────────

    [Fact]
    public void The_app_prints_the_agent_door_from_the_switch_and_the_studio_backend()
    {
        var program = WriteEndpointGuardTests.StripComments(Read("apps/V1.App/Program.cs"));
        Assert.Contains("StudioAgentSwitch.BootLogPrefix + \"{Door}\"", program, StringComparison.Ordinal);
        Assert.Contains("GetRequiredService<ACommerce.Templates.Customer.Marketplace.Services.Incubator.StudioAgentSwitch>().Enabled", program, StringComparison.Ordinal);
        Assert.Contains("agentBackends.For(ACommerce.Templates.Customer.Marketplace.Services.AgentNames.Studio).IsConfigured", program, StringComparison.Ordinal);
        Assert.Equal("[studio] agent_door=", StudioAgentSwitch.BootLogPrefix);
    }

    private static string RunGateBlock(string? logLine)
    {
        var gate = Read("scripts/verify-production-boot.sh");
        const string start = "# ─── بِدايَةُ agent_door ───", end = "# ─── نِهايَةُ agent_door ───";
        var block = gate[gate.IndexOf(start, StringComparison.Ordinal)..(gate.IndexOf(end, StringComparison.Ordinal) + end.Length)];
        var dir = Directory.CreateTempSubdirectory("w13-agent-door-");
        try
        {
            var log = Path.Combine(dir.FullName, "app.log");
            File.WriteAllText(log, "[01:00:00 INF] Now listening\n" + (logLine is null ? "" : logLine + "\n"));
            var script = Path.Combine(dir.FullName, "probe.sh");
            File.WriteAllText(script, "set -euo pipefail\n" + block + "\nagent_door \"$1\"\necho \"exit=$?\"\n");
            var psi = new ProcessStartInfo("bash", [script, log]) { RedirectStandardOutput = true, RedirectStandardError = true };
            using var p = Process.Start(psi)!;
            var stdout = p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            Assert.Equal(0, p.ExitCode);
            return stdout;
        }
        finally { dir.Delete(recursive: true); }
    }

    [Theory]
    [InlineData("[01:00:01 INF] [studio] agent_door=hidden", "مَخفِيّ", false)]
    [InlineData("[01:00:01 INF] [studio] agent_door=open", "مَفتوحٌ لِمُشرِفِ المَنَصَّةِ وَحدَه", false)]
    [InlineData("[01:00:01 INF] [studio] agent_door=agent_unavailable", "Agents__Studio__ApiKey", true)]
    [InlineData(null, "لَم يُقَس", true)]
    public void The_boot_gate_says_the_agent_state_and_keeps_its_exit_code(string? line, string says, bool warns)
    {
        var stdout = RunGateBlock(line);
        output.WriteLine(stdout);
        Assert.Contains(says, stdout, StringComparison.Ordinal);
        Assert.Equal(warns, stdout.Contains('⚠'));
        Assert.Contains("exit=0", stdout, StringComparison.Ordinal);
    }
}
