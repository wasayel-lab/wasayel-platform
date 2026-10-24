using System.Text;
using System.Text.RegularExpressions;
using Xunit;

namespace ACommerce.Platform.Tests;

// ═══ تَوصيفُ «اليَوم» — رَفضُ `create_tenant` عِندَ حَدِّ المَتاجِرِ نَصٌّ عَرَبِيٌّ حَرفِيٌّ في `AgentService.cs` (القاعِدَتانِ ٣ و١١) ═══
//
// **المَقيس** (‏2026-10-24، عِندَ `2939229`): أَداةُ وَكيلِ الاستوديو `create_tenant` حينَ تَرُدُّ `CheckBuildAsync` تُرجِعُ
// «بَلَغتَ حَدَّ التَطبيقاتِ في باقَتِك ({Used} مِن {Limit}). اِحذِف تَطبيقاً أَو اِرفَع باقَتَك مِن /studio/billing.» — مَكتوبَةً في
// `.cs` خارِجَ `ar.json` («ب» S11·٢١، وتَصحيحاه)، ولا صَفَّ يُثَبِّتُ النَصَّ نَفسَه. وصِياغَتُه قَرارُ المالِك («ب» S11·١٨ و·٢١، مَربوطَةٌ
// بِأ‑٤) — فَيُرَحَّلُ بِحَرفِه وَحدَه، والبُرهانُ أَنّ ما يُنتَجُ لَم يَتَغَيَّر بايتاً.
//
// **ثَلاثَةُ صُفوف**: (١) النَصُّ المُنتَجُ بِحَرفِه لِوَسيطَينِ (3، 3) مَقروءاً مِن الكودِ اليَوم — يَنقَلِبُ بِاسمِه في كوميتِ التَرحيل؛
// (٢) الرَفضُ يَتبَعُ بَوّابَةَ `CheckBuildAsync` في جِسمِ `CreateTenantAsync` — يَبقى بِلا تَعديل؛ و(٣) البُرهانُ الحَيُّ
// `LiveAgentStoreLimitRefusalProofTests` يُنادي المُنَفِّذَ الحَقيقيَّ عَلى Postgres ويُقارِنُ البايتات — يَبقى بِلا تَعديل.

public class AgentStoreLimitRefusalTests
{
    /// <summary>النَصُّ بِحَرفِه كَما يَبلُغُ الوَكيلَ والمُستَخدِم — ثابِتٌ قَبلَ التَرحيلِ وبَعدَه.</summary>
    internal static string ExpectedFor(int used, int limit) =>
        $"بَلَغتَ حَدَّ التَطبيقاتِ في باقَتِك ({used} مِن {limit}). اِحذِف تَطبيقاً أَو اِرفَع باقَتَك مِن /studio/billing.";

    internal static string AgentCode()
    {
        var path = Path.Combine(ThemeZeroEquivalenceTests.RepoRoot,
            "libs", "templates", "ACommerce.Templates.Customer.Marketplace", "Services", "AgentService.cs");
        var text = File.ReadAllText(path);
        Assert.True(text.Length > 20_000, $"أَداةٌ عَمياء: {path} لَم يُقرَأ.");
        return text;
    }

    /// <summary>جِسمُ <c>CreateTenantAsync</c> — مِن تَوقيعِه إلى الدالَّةِ الَّتي تَليه.</summary>
    internal static string CreateTenantBody()
    {
        var code = AgentCode();
        var start = code.IndexOf("private async Task<(bool, string)> CreateTenantAsync(", StringComparison.Ordinal);
        var end = code.IndexOf("private async Task<(bool, string)> SetCategoriesAsync(", StringComparison.Ordinal);
        Assert.True(start > 0 && end > start, "جِسمُ CreateTenantAsync لَم يوجَد.");
        return code[start..end];
    }

    [Fact]
    public void Today_the_store_limit_refusal_is_written_in_the_agent_code_and_yields_these_bytes()
    {
        var m = Regex.Match(CreateTenantBody(), "\\$\"(?<a>بَلَغتَ[^\"]*)\"\\s*\\+\\s*\"(?<b>[^\"]*)\"\\)");
        Assert.True(m.Success, "الحَرفِيُّ غائِبٌ مِن جِسمِ CreateTenantAsync.");
        var produced = (m.Groups["a"].Value + m.Groups["b"].Value)
            .Replace("{gate.Used}", "3", StringComparison.Ordinal)
            .Replace("{gate.Limit}", "3", StringComparison.Ordinal);
        Assert.Equal(Encoding.UTF8.GetBytes(ExpectedFor(3, 3)), Encoding.UTF8.GetBytes(produced));
    }

    [Fact]
    public void The_store_limit_refusal_follows_the_build_gate_inside_create_tenant()
    {
        var body = CreateTenantBody();
        var gate = body.IndexOf("await tierService.CheckBuildAsync(quotaOwner, ct);", StringComparison.Ordinal);
        var refused = body.IndexOf("if (!gate.Allowed)", StringComparison.Ordinal);
        var store = body.IndexOf("s.Store(new Tenant", StringComparison.Ordinal);
        Assert.True(gate > 0, "CreateTenantAsync لا يَسأَلُ CheckBuildAsync.");
        Assert.True(refused > gate && store > refused,
            $"التَرتيبُ: البَوّابَة {gate} ← الرَدّ {refused} ← الكِتابَة {store} — يَجِبُ أَن يَسبِقَ الرَدُّ الكِتابَة.");
    }
}
