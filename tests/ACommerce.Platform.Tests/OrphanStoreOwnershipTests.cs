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

        var anonymous = new List<string>();
        var named = 0;
        foreach (var (file, text) in EntitlementContractTests.SourceFiles())
        {
            var code = WriteEndpointGuardTests.StripComments(WriteEndpointGuardTests.StripMarkupComments(text));
            anonymous.AddRange(Regex.Matches(code, @"\.ExecuteAsync\(turn\.Tool\.Name, turn\.Tool\.InputJson\)").Select(_ => Path.GetFileName(file)));
            named += Regex.Matches(code, @"\.ExecuteAs(?:PlatformAdmin)?Async\(turn\.Tool\.Name|\.ExecuteAsync\(turn\.Tool\.Name, turn\.Tool\.InputJson, _userId\)").Count;
        }
        output.WriteLine($"نِداءاتُ أَداةِ الجَلسَةِ بِاسمِ أَحَد: {named} · بِلا أَحَد: {anonymous.Count} ({string.Join(", ", anonymous)})");
        Assert.True(named >= 3, $"أَداةٌ عَمياء: {named} نِداءً — والمَقيسُ 3 (لَوحَةُ الإدارَة، ونُقطَةُ apply، وصَفحَةُ الاستوديو).");
        Assert.Empty(anonymous);
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
