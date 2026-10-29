using System.Reflection;
using ACommerce.Platform.I18n;
using ACommerce.Templates.Customer.Marketplace.Services.Incubator;
using Microsoft.AspNetCore.Components;
using Xunit;
using Xunit.Abstractions;
using UpgradePrompt = ACommerce.Templates.Customer.Marketplace.Components.UpgradePrompt;

namespace ACommerce.Platform.Tests;

// ═══ رَسائِلُ الحَدِّ تَقولُ الحَدَّ ولا تُعطي خُطوَةً تالِيَة — والتَرقِيَةُ لا تُباع ═══
//
// قَرارُ المالِك د15 (+د46) «ب» (‏2026-10-29): «تبقى الرسالة تقول الحدّ، وتُضاف إليها جملة ثانية تعطي خطوة حقيقيّة: موعد تجدّد الحصّة، أو قناة
// التواصل لرفع الحدّ. وتُبدَّل كلمة «ترقية» حيث لا توجد درجة تُشترى.» **تَوصيفُ «اليَوم» قَبلَ التَبديل** (القاعِدَة ٣): لافِتَةُ الحَدِّ
// (`UpgradePrompt.razor`) لا تَأخُذُ إلّا السَبَبَ والدَرَجَة — فَلا مَوعِدَ تَجَدُّدٍ تَعرِفُه — ورِسالَتُها جُملَةُ الحَدِّ وَحدَها (مُثَبَّتَةٌ بِحَرفِها في
// `UpgradePromptTextsCharacterizationTests`)؛ ولا مِفتاحَ لِجُملَةِ تَجَدُّدٍ أَو تَواصُلٍ في القامُوس؛ ورَدُّ الوَكيلِ عِندَ حَدِّ التَطبيقاتِ يَنتَهي عِندَ
// القالَبِ المُنَسَّقِ (`AgentStoreLimitRefusalTests`). تُقلَبُ صُفوفُ «Today_» بِأَسمائِها في كوميتِ التَبديل.
public class UpgradePromptNextStepTests(ITestOutputHelper output)
{
    internal static IReadOnlyList<string> PromptParameters() => typeof(UpgradePrompt)
        .GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Where(p => p.GetCustomAttribute<ParameterAttribute>() is not null)
        .Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToList();

    [Fact]
    public async Task Today_the_banner_takes_only_a_reason_and_a_tier_and_names_no_next_step()
    {
        var parameters = PromptParameters();
        var all = await UpgradePromptTextsCharacterizationTests.RenderAll();
        var withStep = all.Count(b =>
        {
            var m = UpgradePromptTextsCharacterizationTests.MessageOf(b.Html);
            return m.Contains("تَتَجَدَّدُ", StringComparison.Ordinal) || m.Contains("تَواصَل", StringComparison.Ordinal);
        });
        output.WriteLine($"وَسائِطُ اللافِتَة: {string.Join(", ", parameters)} · لافِتاتٌ بِخُطوَةٍ تالِيَة: {withStep}/{all.Count}");
        Assert.Equal(new[] { nameof(UpgradePrompt.CurrentTier), nameof(UpgradePrompt.Reason) }, parameters);
        Assert.True(all.Count >= 16, $"أَداةٌ عَمياء: {all.Count} لافِتَة.");
        Assert.Equal(0, withStep);
        Assert.Null(LocaleCatalog.Find(LocaleCatalog.Arabic, "upgrade.renews_on"));
        Assert.Null(LocaleCatalog.Find(LocaleCatalog.Arabic, "upgrade.contact"));
    }

    [Fact]
    public void Today_the_agent_store_limit_refusal_ends_at_the_formatted_limit()
    {
        var body = AgentStoreLimitRefusalTests.CreateTenantBody();
        var at = body.IndexOf("if (!gate.Allowed)", StringComparison.Ordinal);
        Assert.True(at > 0, "لا رَدَّ عِندَ الحَدّ — الأَداةُ عَمياء.");
        var refusal = body[at..body.IndexOf(';', at)];
        output.WriteLine($"الرَدّ: {refusal.Replace('\n', ' ')}");
        Assert.EndsWith("StoreLimitRefusalKey), gate.Used, gate.Limit))", refusal.TrimEnd(), StringComparison.Ordinal);
        Assert.DoesNotContain("upgrade.contact", body, StringComparison.Ordinal);
    }
}
