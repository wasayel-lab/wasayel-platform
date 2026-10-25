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

    /// <summary><b>«اليَوم»</b>: بابا وَكيلِ الإدارَةِ يُنادِيانِ المُنَفِّذَ بِلا أَحَد، و<c>create_tenant</c> يَكتُبُ <c>ownerUserId ?? Guid.Empty</c> — فَالمَتجَرُ
    /// يولَدُ يَتيماً ومُشرِفُ المَنَصَّةِ الَّذي أَنشَأَه لا يَملِكُه. يُقلَبُ بِاسمِه في كوميتِ الإصلاح.</summary>
    [Fact]
    public void Today_the_admin_agent_paths_run_create_tenant_without_naming_anyone()
    {
        var panel = Read($"{TemplateRoot}/Components/Pages/Admin/AgentChatPanel.razor");
        var apply = MapBody("/admin/agent/tool/{toolId}/apply");
        var create = Body(Read($"{TemplateRoot}/Services/AgentService.cs"),
            "private async Task<(bool, string)> CreateTenantAsync(");
        var panelAnon = Regex.Matches(panel, Regex.Escape("Executor.ExecuteAsync(turn.Tool.Name, turn.Tool.InputJson);")).Count;
        var applyAnon = Regex.Matches(apply, Regex.Escape("exec.ExecuteAsync(turn.Tool.Name, turn.Tool.InputJson);")).Count;
        var orphan = Regex.Matches(create, Regex.Escape("OwnerUserId = ownerUserId ?? Guid.Empty,")).Count;
        output.WriteLine($"AgentChatPanel بِلا أَحَد: {panelAnon} · ‏/admin/agent/tool/…/apply بِلا أَحَد: {applyAnon} · create_tenant يَكتُبُ ownerUserId ?? Guid.Empty: {orphan}");

        Assert.Equal(1, panelAnon);
        Assert.Equal(1, applyAnon);
        Assert.Equal(1, orphan);
    }

    /// <summary><b>«اليَوم»</b>: كُلُّ دُخولٍ إلى الاستوديو يُنادي البَذّار، والبَذّارُ يَأخُذُ أَقدَمَ مُستَخدِمٍ أَيّاً كانَ — لا يَسأَلُ أَهُوَ مُشرِفٌ — ويَكتُبُه
    /// مالِكاً لِكُلِّ يَتيم. يُقلَبُ بِاسمِه في كوميتِ الإصلاح.</summary>
    [Fact]
    public void Today_every_studio_login_hands_every_ownerless_store_to_the_oldest_studio_user()
    {
        var verify = MapBody("/studio/auth/verify");
        var seeder = Read($"{TemplateRoot}/Services/Incubator/StudioOwnershipSeeder.cs");
        var onLogin = Regex.Matches(verify, Regex.Escape("StudioOwnershipSeeder.RunAsync(store)")).Count;
        var oldest = Regex.Matches(seeder, @"\.OrderBy\(u => u\.CreatedAt\)\.Take\(1\)").Count;
        var asksAdmin = Regex.Matches(seeder, "IsPlatformAdmin").Count;
        output.WriteLine($"‏/studio/auth/verify يُنادي البَذّار: {onLogin} · أَقدَمُ مُستَخدِم: {oldest} · يَسأَلُ IsPlatformAdmin: {asksAdmin}");

        Assert.Equal(1, onLogin);
        Assert.Equal(1, oldest);
        Assert.Equal(0, asksAdmin);
    }
}
