using System.Text.RegularExpressions;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ مَتجَرٌ بِلا مالِكٍ يُسنَدُ صامِتاً إلى أَقدَمِ مُستَخدِمِ استوديو عِندَ كُلِّ دُخول (‏lens-security-14) ═══
//
// **تَدقيقُ الإطلاقِ عِندَ `dace7cd`، تَيّارُ W8-3 — `lens-security-14` (صُغرى)**: `StudioOwnershipSeeder.RunAsync` يَأخُذُ أَقدَمَ `StudioUser`
// أَيّاً كانَ ويَكتُبُه مالِكاً لِكُلِّ مَتجَرٍ `OwnerUserId == Guid.Empty` — ويُنادى عِندَ الإقلاعِ **وبَعدَ كُلِّ دُخولٍ إلى الاستوديو**
// (`/studio/auth/verify`). ومَن يَلِدُ مَتجَراً بِلا مالِكٍ اليَوم: بَذرُ `PlatformSeed` (‏`ashare` و`ejar` و`order`)، و`create_tenant` مِن وَكيلِ
// الإدارَةِ (`AgentChatPanel` و`/admin/agent/tool/{id}/apply` يُنادِيانِ المُنَفِّذَ بِلا مالِك، فَيَكتُبُ `ownerUserId ?? Guid.Empty`) — وأَصلَحَ
// الفَرعُ الحالَةَ نَفسَها في `/admin/tenants/create` («المُنشِئ هُوَ المالِك»).
//
// **المَقيسُ حَيّاً (‏2026-10-25، الرَأسُ `f2d3a8e`، قاعِدَةٌ مَحَلِّيَّةٌ جَديدَة `wasayel_w83b`، Development بِلا `PLATFORM_ADMIN_PHONE`)**: بَعدَ الإقلاعِ
// المَتاجِرُ الثَلاثَةُ بِلا مالِك؛ ثُمَّ مُستَخدِمٌ سَجَّلَ نَفسَه في الاستوديو بِرَمزِ المُحاكي — لَيسَ مُشرِفاً — فَصارَ **مالِكَ الثَلاثَة** عِندَ أَوَّلِ
// دُخول، ولَوحَتُه تَسرُدُ `/studio/apps/ashare` و`ejar` و`order`، ويَفتَحُ `/admin/tenants/order/users` ‏200 بِجَدوَلِ مُستَخدِميه.
//
// هذا المِلَفُّ يُوَصِّفُ «اليَوم» (القاعِدَة ٣)، وصُفوفُ `Today_` تُقلَبُ بِأَسمائِها في كوميتِ الإصلاح.
public class OrphanStoreOwnershipTests(ITestOutputHelper output)
{
    private const string TemplateRoot = "libs/templates/ACommerce.Templates.Customer.Marketplace";

    private static string Read(string relative)
    {
        var path = Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, relative.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(path), $"مَصدَرٌ مَفقود: {relative} — الأَداةُ عَمياءُ بِلا طَرَفٍ مَقروء.");
        return WriteEndpointGuardTests.StripComments(WriteEndpointGuardTests.StripMarkupComments(File.ReadAllText(path)));
    }

    /// <summary>جِسمُ نُقطَةٍ مِن تَسجيلِها حَتّى <c>DisableAntiforgery</c> — نَسَقُ <c>StudioAnalysisDoorTests.MapBody</c>.</summary>
    private static string MapBody(string route)
    {
        var all = Read($"{TemplateRoot}/MarketplaceTemplateExtensions.cs");
        var marker = $"MapPost(\"{route}\"";
        var start = all.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"نُقطَةُ {route} غَيرُ مَوجودَة — الأَداةُ تَقيسُ عَدَماً.");
        Assert.Equal(start, all.LastIndexOf(marker, StringComparison.Ordinal));
        var end = all.IndexOf("DisableAntiforgery", start, StringComparison.Ordinal);
        Assert.True(end > start, $"لَم يُغلَق جِسمُ {route} — القَصُّ فاشِل.");
        return all[start..end];
    }

    /// <summary>جِسمُ دالَّةٍ بِتَوقيعِها — مِن قَوسِها المَفتوحِ إلى ما يُقابِلُه.</summary>
    private static string Body(string code, string signature)
    {
        var at = code.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(at >= 0, $"دالَّةٌ مَفقودَة: {signature} — القَصُّ أَعمى.");
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

    /// <summary><b>قُلِبَ «Today_» بِاسمِه (‏2026-10-25، كوميتُ الإصلاح)</b> — كانَ <c>Today_the_admin_agent_paths_run_create_tenant_without_naming_anyone</c>
    /// (‏<c>44b77d3</c>): «AgentChatPanel 1 · apply 1 · ownerUserId ?? Guid.Empty 1». والآن البابانِ يُنادِيانِ <c>ExecuteAsPlatformAdminAsync</c> بِمُعَرِّفِ
    /// المُشرِف — المُضيفُ الثابِتُ يَقرَؤُه داخِلَ <c>RequirePlatformAdmin</c> والنُقطَةُ مِن قَرارِ حارِسِها — و<c>create_tenant</c> يَكتُبُه مالِكاً؛ ولا
    /// مُنادٍ في المَصادِرِ يُنادي المُنَفِّذَ بِأَداةِ جَلسَةٍ بِلا أَحَد (الماسِحُ يَعُدُّ ويَفشَلُ عَلى الصِفر).</summary>
    [Fact]
    public void The_admin_agent_paths_create_the_store_owned_by_the_admin_who_ran_it()
    {
        var host = Read($"{TemplateRoot}/Components/Pages/Admin/AgentChat.razor");
        var panel = Read($"{TemplateRoot}/Components/Pages/Admin/AgentChatPanel.razor");
        var apply = MapBody("/admin/agent/tool/{toolId}/apply");
        var create = Body(Read($"{TemplateRoot}/Services/AgentService.cs"),
            "private async Task<(bool, string)> CreateTenantAsync(");

        Assert.Contains("<AgentChatPanel @rendermode=\"RenderMode.InteractiveServer\" AdminUserId=\"@_adminUserId\" />", host, StringComparison.Ordinal);
        Assert.True(host.IndexOf("<RequirePlatformAdmin>", StringComparison.Ordinal) < host.IndexOf("<AgentChatPanel", StringComparison.Ordinal));
        Assert.Contains("_adminUserId = StudioAuth.IsAuthenticated ? StudioAuth.UserId : null;", host, StringComparison.Ordinal);
        Assert.Contains("Executor.ExecuteAsPlatformAdminAsync(turn.Tool.Name, turn.Tool.InputJson, AdminUserId ?? Guid.Empty);", panel, StringComparison.Ordinal);
        var guardAt = apply.IndexOf("var admin = await Services.PlatformAdminGuard.EvaluateAsync(store, auth);", StringComparison.Ordinal);
        var execAt = apply.IndexOf("exec.ExecuteAsPlatformAdminAsync(turn.Tool.Name, turn.Tool.InputJson, admin.User!.Id);", StringComparison.Ordinal);
        Assert.True(guardAt > 0 && execAt > guardAt, "نُقطَةُ apply لا تُنادي بِاسمِ المُشرِفِ بَعدَ حارِسِها.");
        Assert.Contains("OwnerUserId = ownerUserId ?? createdBy ?? Guid.Empty,", create, StringComparison.Ordinal);

        // ‏2026-10-25 (مُراجَعَةُ تَيّارِ W8-3 الجَولَة ١، تَيّارُ W8-10): الماسِحُ صارَ ExecutorCallsIn — دالَّةً تُحقَنُ بِعَيِّناتٍ
        // (The_executor_call_scanner_tells_a_named_call_from_an_anonymous_one_in_every_spelling)؛ وكانَ يَقبَلُ تَهجِئَةً واحِدَةً حَرفاً
        // (turn.Tool.Name, turn.Tool.InputJson) فَنِداءٌ بِلا أَحَدٍ بِأَسماءِ مُتَغَيِّراتٍ أُخرى أَو بِـnull صَريحٍ يَمُرُّ بِلا عَدّ.
        var (anonymous, named) = ExecutorCallsIn(EntitlementContractTests.SourceFiles());
        output.WriteLine($"نِداءاتُ أَداةِ الجَلسَةِ بِاسمِ أَحَد: {named} · بِلا أَحَد: {anonymous.Count} ({string.Join(", ", anonymous)})");
        Assert.True(named >= 3, $"أَداةٌ عَمياء: {named} نِداءً — والمَقيسُ 3 (لَوحَةُ الإدارَة، ونُقطَةُ apply، وصَفحَةُ الاستوديو).");
        Assert.Empty(anonymous);
    }

    /// <summary>نِداءاتُ مُنَفِّذِ أَدَواتِ الوَكيل (<c>ExecuteAsync</c> و<c>ExecuteAsPlatformAdminAsync</c>) عَلى مُستَقبِلٍ في اسمِه «exec»
    /// (<c>Executor</c>، <c>exec</c>، <c>_executor</c>…) — فَلا يَعُدُّ <c>pipeline.ExecuteAsync</c> ولا <c>Results.…ExecuteAsync(ctx)</c>.
    /// والوُسَطاءُ تُقسَمُ بِالفَواصِلِ العُليا (الأَقواسُ والنُصوصُ مُتَوازِنَة)، فَالنِداءُ <b>بِلا أَحَد</b>: وَسيطانِ، أَو ثالِثٌ مِن
    /// <c>null</c> · <c>null!</c> · <c>default</c> · <c>default(…)</c> · <c>Guid.Empty</c> (بِاسمِه <c>ownerUserId:</c>/<c>adminUserId:</c> أَو بِلا)، أَو ثالِثٌ
    /// هُوَ رَمزُ إلغاءٍ (<c>ct</c>، <c>cancellationToken</c>، <c>token</c>، <c>CancellationToken.None</c>، <c>….RequestAborted</c>، <c>ct:</c>) — فَهذا
    /// حِملُ <c>ExecuteAsync(name, json, ct)</c> بِلا مالِك. وما سِواه <b>بِاسمِ أَحَد</b>. <b>وما لا يَراه — يُقال</b>: مُستَقبِلٌ بِلا «exec» في اسمِه
    /// (<c>GetRequiredService&lt;AgentToolExecutor&gt;().ExecuteAsync(…)</c>)، ومُتَغَيِّرٌ اسمُه يُشبِهُ المالِكَ وقيمَتُه <c>null</c>.</summary>
    internal static (List<string> Anonymous, int Named) ExecutorCallsIn(IEnumerable<(string File, string Text)> sources)
    {
        var anonymous = new List<string>();
        var named = 0;
        foreach (var (file, text) in sources)
        {
            var code = WriteEndpointGuardTests.StripComments(WriteEndpointGuardTests.StripMarkupComments(text));
            foreach (Match m in Regex.Matches(code, @"(?<recv>[A-Za-z_][A-Za-z0-9_]*)\s*\.\s*(?<verb>ExecuteAsPlatformAdminAsync|ExecuteAsync)\s*\("))
            {
                if (m.Groups["recv"].Value.IndexOf("exec", StringComparison.OrdinalIgnoreCase) < 0) continue;
                var args = TopLevelArguments(code, m.Index + m.Length);
                var third = args.Count >= 3 ? Regex.Replace(args[2], @"^\s*(?:ownerUserId|adminUserId)\s*:\s*", "").Trim() : null;
                var nobody = third is null
                    || Regex.IsMatch(third, @"^(?:null!?|default(?:\s*\([^)]*\))?|Guid\.Empty)$")
                    || Regex.IsMatch(third, @"^(?:ct|cancellationToken|token|CancellationToken\.None|[\w.]*\.RequestAborted|ct\s*:.*)$");
                if (nobody) anonymous.Add(Path.GetFileName(file)); else named++;
            }
        }
        return (anonymous, named);
    }

    /// <summary>وُسَطاءُ النِداءِ مِن بَعدِ قَوسِه الفاتِحِ حَتّى قَوسِه الغالِق: فَواصِلُ العُمقِ الأَعلى وَحدَها، والنَصُّ بَينَ علامَتَي تَنصيصٍ لا يُقسَم.</summary>
    private static List<string> TopLevelArguments(string code, int start)
    {
        var args = new List<string>();
        var depth = 0;
        var from = start;
        for (var i = start; i < code.Length; i++)
        {
            var c = code[i];
            if (c == '"')
            {
                for (i++; i < code.Length && code[i] != '"'; i++) if (code[i] == '\\') i++;
                continue;
            }
            if (c is '(' or '[' or '{') depth++;
            else if (c is ')' or ']' or '}')
            {
                if (depth == 0) { args.Add(code[from..i]); break; }
                depth--;
            }
            else if (c == ',' && depth == 0) { args.Add(code[from..i]); from = i + 1; }
        }
        return args.Where(a => a.Trim().Length > 0).ToList();
    }

    /// <summary>القاعِدَة ١٠ — الماسِحُ يُحمِرُّ بِعَيِّنَةٍ مَحقونَة: نِداءٌ بِلا أَحَدٍ يُعَدُّ في كُلِّ تَهجِئَة، ونِداءٌ بِاسمِ أَحَدٍ لا يُحسَبُ عَلَيه، وما لَيسَ مُنَفِّذاً لا يُعَدّ.</summary>
    [Theory]
    [InlineData("var r = await Executor.ExecuteAsync(turn.Tool.Name, turn.Tool.InputJson);", 1, 0)]
    [InlineData("var r = await exec.ExecuteAsync(t.Name, t.Json);", 1, 0)]
    [InlineData("var r = await _executor.ExecuteAsync(name, json, null);", 1, 0)]
    [InlineData("var r = await Executor.ExecuteAsync(name, json, ownerUserId: null);", 1, 0)]
    [InlineData("var r = await Executor.ExecuteAsync(name, json, default);", 1, 0)]
    [InlineData("var r = await Executor.ExecuteAsync(name, json, Guid.Empty);", 1, 0)]
    [InlineData("var r = await Executor.ExecuteAsync(name, json, ct);", 1, 0)]
    [InlineData("var r = await Executor.ExecuteAsync(name, json, ctx.RequestAborted);", 1, 0)]
    [InlineData("var r = await Executor\n    .ExecuteAsync(\n        name,\n        json);", 1, 0)]
    [InlineData("var r = await Executor.ExecuteAsync(Pick(a, b), Json(c, \"x,y\"));", 1, 0)]
    [InlineData("var r = await exec.ExecuteAsPlatformAdminAsync(name, json, Guid.Empty);", 1, 0)]
    [InlineData("var r = await Executor.ExecuteAsync(name, json, _userId);", 0, 1)]
    [InlineData("var r = await exec.ExecuteAsPlatformAdminAsync(name, json, admin.User!.Id);", 0, 1)]
    [InlineData("var r = await Executor.ExecuteAsPlatformAdminAsync(name, json, AdminUserId ?? Guid.Empty);", 0, 1)]
    [InlineData("// var r = await Executor.ExecuteAsync(name, json);", 0, 0)]
    [InlineData("await pipeline.ExecuteAsync(cmd, () => handler.HandleAsync(cmd));", 0, 0)]
    [InlineData("await Results.Json(new { ready = true }).ExecuteAsync(ctx);", 0, 0)]
    public void The_executor_call_scanner_tells_a_named_call_from_an_anonymous_one_in_every_spelling(string sample, int anonymous, int named)
    {
        var (a, n) = ExecutorCallsIn(new[] { ("x/Injected.cs", sample) });
        output.WriteLine($"مَحقونَة: بِلا أَحَد {a.Count} (المُتَوَقَّع {anonymous}) · بِاسمِ أَحَد {n} (المُتَوَقَّع {named})");
        Assert.Equal(anonymous, a.Count);
        Assert.Equal(named, n);
    }

    /// <summary><b>قُلِبَ «Today_» بِاسمِه (‏2026-10-25، كوميتُ الإصلاح)</b> — كانَ <c>Today_every_studio_login_hands_every_ownerless_store_to_the_oldest_studio_user</c>
    /// (‏<c>44b77d3</c>): «verify يُنادي البَذّار 1 · أَقدَمُ مُستَخدِم 1 · IsPlatformAdmin 0». والآن لا يُنادى إلّا عِندَ الإقلاعِ (مُنادٍ واحِدٌ في
    /// <c>Program.cs</c>)، ولا يُسنِدُ إلّا لِأَقدَمِ مُشرِفِ مَنَصَّة — والمُرَشِّحُ قَبلَ التَرتيب.</summary>
    [Fact]
    public void No_studio_login_runs_the_ownership_seeder_and_it_hands_orphans_to_a_platform_admin_only()
    {
        var verify = MapBody("/studio/auth/verify");
        var seeder = Read($"{TemplateRoot}/Services/Incubator/StudioOwnershipSeeder.cs");
        var callers = EntitlementContractTests.SourceFiles()
            .SelectMany(f => Regex.Matches(WriteEndpointGuardTests.StripComments(WriteEndpointGuardTests.StripMarkupComments(f.Text)),
                    @"StudioOwnershipSeeder\s*\.\s*RunAsync\(").Select(_ => Path.GetRelativePath(ThemeZeroEquivalenceTests.RepoRoot, f.File).Replace('\\', '/')))
            .ToList();
        output.WriteLine($"مُنادو البَذّار: {callers.Count} ({string.Join(", ", callers)}) · verify يُناديه: {verify.Contains("StudioOwnershipSeeder", StringComparison.Ordinal)}");

        Assert.DoesNotContain("StudioOwnershipSeeder", verify, StringComparison.Ordinal);
        Assert.Equal(new[] { "apps/V1.App/Program.cs" }, callers);
        var adminAt = seeder.IndexOf(".Where(u => u.IsPlatformAdmin)", StringComparison.Ordinal);
        var orderAt = seeder.IndexOf(".OrderBy(u => u.CreatedAt).Take(1)", StringComparison.Ordinal);
        Assert.True(adminAt > 0 && orderAt > adminAt, "البَذّارُ لا يُرَشِّحُ المُشرِفَ قَبلَ أَن يَأخُذَ الأَقدَم.");
    }
}
