using ACommerce.Templates.Customer.Marketplace.Services.Incubator;
using Xunit;

namespace ACommerce.Platform.Tests;

// ═══ إعادَةُ العالِقينَ بِحُدودٍ أَضيَقَ مِن المَجّانِيَّةِ إلَيها (قَرارُ المالِك د16؛ ‏ADR-026 مُلحَق د16) ═══
//
// `scripts/repair-studio-tier.cs` يُعيدُ مَن خُزِّنَت لَه `lite`/`growth` إلى `spark` ويَحفَظُ دَرَجَتَه السابِقَةَ في سَطرِ تَدقيق. والقَرارُ في
// `StudioTierRepair.IsStuck` — يُقاسُ هُنا بِطَرَفَيه؛ وشَكلُ السكربتِ نَصّاً. والبُرهانُ الحَيُّ في رِسالَةِ الكوميت.

public class StudioTierRepairTests
{
    // ─── د16: العالِقونَ بِحُدودٍ أَضيَقَ مِن المَجّانِيَّة ──────────────────────────────────────

    [Theory]
    [InlineData("lite")]
    [InlineData("growth")]
    public void A_user_stored_on_a_tier_narrower_than_the_raised_free_tier_is_stuck(string tier)
    {
        Assert.True(TierCatalog.FreeTierTemporarilyRaised);
        Assert.True(StudioTierRepair.IsStuck(tier));
        Assert.True(TierCatalog.Exceeds(TierCatalog.For(StudioTierRepair.Free), TierCatalog.For(tier)));
    }

    [Theory]
    [InlineData("spark")]
    [InlineData("scale")]          // ‏حُدودُها حُدودُ المَجّانِيَّةِ المَرفوعَة — لا تُنزَل
    [InlineData("")]
    [InlineData(null)]
    [InlineData("enterprise")]     // ‏لا يَعرِفُها الكاتالوجُ فَيَقرَؤُها مَجّانِيَّةً أَصلاً
    public void A_user_on_the_free_tier_an_equal_tier_or_an_unknown_one_is_not_stuck(string? tier)
        => Assert.False(StudioTierRepair.IsStuck(tier));

    /// <summary>ويَومَ يُنهى الرَفعُ المُؤَقَّتُ لا يَعودُ مُرَشَّحٌ واحِد: بِحُدودِ ما قَبلَ الرَفعِ لا تَتَفَوَّقُ المَجّانِيَّةُ عَلى أَيِّ دَرَجَة —
    /// فَالسكربتُ لا يُنزِلُ أَحَداً إلى أَضيَقَ مِمّا كان (نَصُّ القَرار).</summary>
    [Fact]
    public void Without_the_raise_the_free_tier_exceeds_no_tier_so_nobody_would_be_moved_down()
    {
        foreach (var tier in TierCatalog.All.Keys.Where(k => k != StudioTierRepair.Free))
            Assert.False(TierCatalog.Exceeds(TierCatalog.FreeTierBeforeRaise, TierCatalog.For(tier)), tier);
    }

    /// <summary>السكربتُ يَعرِضُ افتِراضاً، ولا يَكتُبُ إلّا بِـ<c>--apply</c> وعَدَدٍ يُطابِقُ العَرض، ويَسأَلُ الدالَّةَ النَقِيَّة، ويَفشَلُ عَلى الصِفر،
    /// ولا يَمَسُّ المُخَطَّط — ولا اسمَ مَتجَرٍ مُستَثنى.</summary>
    [Fact]
    public void The_repair_script_shows_by_default_and_applies_only_on_a_matching_confirm()
    {
        const string file = "repair-studio-tier.cs", decide = "StudioTierRepair.IsStuck(", effect = "StudioTierRepair.Free", audit = "StudioTierRepair.AuditAction";
        var path = Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, "scripts", file);
        var text = File.ReadAllText(path);
        Assert.True(text.Length > 3000, $"أَداةٌ عَمياء: {path} طولُه {text.Length}.");

        foreach (var needle in new[]
                 {
                     "var apply      = args.Contains(\"--apply\");",
                     "if (!apply)",
                     "return 2;",
                     "if (confirm is null || confirm != plan.Count)",
                     "AutoCreateSchemaObjects = AutoCreate.None",
                     "✗ BLIND CHECK",
                     decide, effect, audit,
                 })
            Assert.Contains(needle, text, StringComparison.Ordinal);

        // ‏ولا كِتابَةَ قَبلَ بَوّابَةِ التَأكيد: أَوَّلُ `SaveChangesAsync` بَعدَها.
        var gate = text.IndexOf("if (confirm is null || confirm != plan.Count)", StringComparison.Ordinal);
        var firstSave = text.IndexOf("SaveChangesAsync", StringComparison.Ordinal);
        Assert.True(firstSave > gate && gate > text.IndexOf("if (!apply)", StringComparison.Ordinal),
            "كِتابَةٌ قَبلَ بَوّابَةِ العَرضِ أَو التَأكيد.");
        // ‏ولا اسمَ مَتجَرٍ مُستَثنى (`ashare` كَأَيِّ مَتجَر — قَرارُ المالِك).
        Assert.DoesNotContain("\"ashare\"", text, StringComparison.Ordinal);
    }
}
