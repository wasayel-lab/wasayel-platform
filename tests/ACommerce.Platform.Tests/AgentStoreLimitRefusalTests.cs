using System.Text;
using System.Text.RegularExpressions;
using ACommerce.Platform.I18n;
using ACommerce.Templates.Customer.Marketplace.Services;
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

    /// <summary>(‏2026-10-24، كوميتُ التَرحيل — انقَلَبَ بِقَرارٍ مَرئِيّ: كانَ <c>Today_the_store_limit_refusal_is_written_in_the_agent_code_and_yields_these_bytes</c>
    /// يَقرَأُ الحَرفِيَّ مِن جِسمِ <c>CreateTenantAsync</c> ويُقارِنُ ما يُنتِجُه بِـ<see cref="ExpectedFor"/>؛ وصارَ يَقرَأُ القيمَةَ مِن القامُوسِ
    /// ويُقارِنُ ما تُنتِجُه بِالوَسيطَينِ نَفسَيهِما بِالثابِتِ نَفسِه — بايتاً — والجِسمُ بِلا الحَرفِيّ. و<see cref="ExpectedFor"/> لَم يُمَسّ.)</summary>
    [Fact]
    public void The_store_limit_refusal_is_read_from_the_dictionary_and_yields_the_same_bytes()
    {
        var key = AgentToolExecutor.StoreLimitRefusalKey;
        Assert.Contains(key, LocaleCatalog.Lexicon);
        var template = LocaleCatalog.Find(LocaleCatalog.Arabic, key);
        Assert.False(string.IsNullOrWhiteSpace(template));
        Assert.Equal(Encoding.UTF8.GetBytes(ExpectedFor(3, 3)), Encoding.UTF8.GetBytes(string.Format(template!, 3, 3)));

        var body = CreateTenantBody();
        Assert.Contains("LocaleCatalog.Text(LocaleCatalog.Arabic, StoreLimitRefusalKey), gate.Used, gate.Limit)", body, StringComparison.Ordinal);
        foreach (var literal in new[] { "بَلَغتَ حَدَّ التَطبيقاتِ", "اِرفَع باقَتَك" })
            Assert.DoesNotContain(literal, AgentCode(), StringComparison.Ordinal);
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
