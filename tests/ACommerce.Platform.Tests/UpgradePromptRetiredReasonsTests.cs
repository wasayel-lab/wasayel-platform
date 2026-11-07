using System.Text.RegularExpressions;
using ACommerce.Platform.I18n;
using ACommerce.Templates.Customer.Marketplace.Services.Incubator;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ نافِذَةُ التَرقِيَةِ لا تَرسُمُ سَبَباً مُتَقاعِداً — موجَبٌ وسالِب (‏2026-11-07، الموجَة ١٣ — مُراجَعَةُ C1 · W13-1·F1؛ القَرار §٧، ADR-049) ═══
//
// **المَقيسُ قَبل** (لَقطاتُ `2deaaab`): `?upgrade=analyze` «بَلَغتَ حَدّ تَحاليل هذا الشَّهر»، و`refine` «بَلَغتَ حَدّ التَّحسينات لِهذا الشَّهر»، و`export`
// «هذِه الميزَةُ غَيرُ مَشمولَةٍ في باقَتِك» — ولا بَوّابَةَ تَكتُبُ الثَلاثَة. **والآن**: `StudioUpgradeReason.Drawn` يَسأَلُه المُكَوِّنُ قَبلَ أَن يَرسُم؛
// المُتَقاعِدُ لا شَيء، وحَدُّ المَتاجِرِ والسَبَبُ المَجهولُ بايتاً كَما كانا (`UpgradePromptLiveReasonsCharacterizationTests` — لا يُمَسّ).

public class UpgradePromptRetiredReasonsTests(ITestOutputHelper output)
{
    [Fact]
    public void The_retired_reasons_are_exactly_the_three_no_gate_writes()
    {
        Assert.Equal(new[] { StudioUpgradeReason.Analyses, StudioUpgradeReason.Refines, StudioUpgradeReason.Export }, StudioUpgradeReason.Retired);
        Assert.All(StudioUpgradeReason.Retired, r => Assert.Contains(r, StudioUpgradeReason.All));
        Assert.Equal(new[] { StudioUpgradeReason.Stores }, StudioUpgradeReason.All.Where(StudioUpgradeReason.Drawn).ToArray());
    }

    [Theory]
    [InlineData(StudioUpgradeReason.Stores, true)]
    [InlineData(UpgradePromptLiveReasonsCharacterizationTests.UnknownReason, true)]
    [InlineData(StudioUpgradeReason.Analyses, false)]
    [InlineData(StudioUpgradeReason.Refines, false)]
    [InlineData(StudioUpgradeReason.Export, false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Only_a_live_or_unknown_reason_is_drawn(string? reason, bool drawn)
        => Assert.Equal(drawn, StudioUpgradeReason.Drawn(reason));

    /// <summary><b>السالِب</b>: لِكُلِّ سَبَبٍ مُتَقاعِدٍ ولِكُلِّ دَرَجَة — ومَعَ مَوعِدِ تَجَدُّدٍ مُعطىً — لا يَرسُمُ المُكَوِّنُ حَرفاً.</summary>
    [Theory]
    [InlineData(StudioUpgradeReason.Analyses)]
    [InlineData(StudioUpgradeReason.Refines)]
    [InlineData(StudioUpgradeReason.Export)]
    public async Task A_retired_reason_draws_nothing_for_any_tier(string reason)
    {
        var drawn = new List<string>();
        foreach (var tier in TierCatalog.All.Keys)
            drawn.Add(await UpgradePromptNextStepTests.Render(reason, tier, "2026-11-18"));
        output.WriteLine($"«{reason}»: {drawn.Count} دَرَجات · أَطوالُ ما رُسِم {string.Join(", ", drawn.Select(h => h.Trim().Length))}");
        Assert.True(drawn.Count >= 4, $"أَداةٌ عَمياء: {drawn.Count} دَرَجَة.");
        Assert.All(drawn, h => Assert.Equal("", h.Trim()));
    }

    /// <summary><b>الموجَب</b>: حَدُّ المَتاجِرِ يُرسَمُ لِكُلِّ دَرَجَةٍ بِعُنوانِه ورِسالَتِه.</summary>
    [Fact]
    public async Task The_store_limit_reason_still_draws_its_title_and_message_for_every_tier()
    {
        foreach (var tier in TierCatalog.All.Keys)
        {
            var html = await UpgradePromptNextStepTests.Render(StudioUpgradeReason.Stores, tier, "2026-11-18");
            Assert.Equal(LocaleCatalog.Find(LocaleCatalog.Arabic, "upgrade.title.build"), UpgradePromptTextsCharacterizationTests.TitleOf(html));
            Assert.Equal("عَدَدُ التَطبيقاتِ في باقَتِكَ مَحدود.", UpgradePromptTextsCharacterizationTests.MessageOf(html));
        }
    }

    /// <summary>والسُؤالُ في الوَسمِ شَرطُ اللافِتَةِ الأَعلى بِحَرفِه — لا شَرطٌ ثانٍ يَنجَرِف.</summary>
    [Fact]
    public void The_banner_asks_Drawn_as_its_outer_condition()
    {
        var razor = UpgradePromptPriceTests.Prompt();
        var markup = razor[..razor.IndexOf("@code", StringComparison.Ordinal)];
        Assert.Single(Regex.Matches(markup, Regex.Escape("@if (StudioUpgradeReason.Drawn(Reason))")));
        Assert.DoesNotContain("@if (!string.IsNullOrEmpty(Reason))", markup, StringComparison.Ordinal);
    }
}
