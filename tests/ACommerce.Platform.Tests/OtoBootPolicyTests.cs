using ACommerce.Kit.Delivery;
using Xunit;

namespace ACommerce.Platform.Tests;

// ═══ بيئَةُ OTO التَجريبِيَّةُ خارِجَ التَطوير — الجَدوَلُ الَّذي يُرفَضُ بِه الإقلاع (قَرارُ المالِك د44 «ج»، ‏2026-10-29) ═══
//
// **المَقيسُ قَبل** (‏`73506af`، `Today_a_boot_outside_development_with_oto_staging_is_not_refused`): إقلاعٌ خارِجَ التَطويرِ بِـ`staging`
// لا يُرفَض، والحِمايَةُ دَليلُ النَشرِ وسَطرُ ⚠ في بَوّابَةِ الإقلاع. وهذِه الصُفوفُ الحَدُّ المَقيسُ لِلحارِسِ (القاعِدَة ٢)، ولِرَمزِه الواحِدِ
// صَفٌّ موجِبٌ وآخَرُ سالِب (القاعِدَة ٤) — شَكلُ `AuthTokenSecretPolicyTests` حَرفاً.

public class OtoBootPolicyTests
{
    // ─── الموجِب: staging بِأَشكالِها خارِجَ التَطوير ───────────────────────────

    [Theory]
    [InlineData("staging")]
    [InlineData(" Staging ")]
    [InlineData("STAGING")]
    [InlineData("\vstaging")]
    public void Staging_outside_development_fires_its_one_code(string environment)
    {
        var v = Assert.Single(OtoBootPolicy.Validate(isDevelopment: false, environment));
        Assert.Equal(OtoBootPolicy.StagingOutsideDevelopment, v.Code);
        Assert.Contains(OtoEnvironment.EnvVarName(OtoEnvironment.EnvironmentKey), v.MessageAr, StringComparison.Ordinal);
        Assert.Contains("§٢·ط", v.MessageAr, StringComparison.Ordinal);
        Assert.False(OtoBootPolicy.IsValid(isDevelopment: false, environment));
    }

    // ─── السالِب: التَطويرُ أَيّاً كانَت القيمَة، والفَراغُ والإنتاجُ خارِجَه ─────────────────

    [Theory]
    [InlineData("staging")]
    [InlineData(" Staging ")]
    [InlineData("production")]
    [InlineData("")]
    [InlineData(null)]
    public void Development_never_fires_whatever_the_value(string? environment)
    {
        Assert.Empty(OtoBootPolicy.Validate(isDevelopment: true, environment));
        Assert.True(OtoBootPolicy.IsValid(isDevelopment: true, environment));
        OtoBootPolicy.AssertSafeOutsideDevelopment(isDevelopment: true, environment);
    }

    /// <summary>الفَراغُ «لا شَحنَ عَبرَ OTO» (حالُ كُلِّ نُسخَةٍ اليَوم)، و<c>production</c> مُغلَقٌ في الكودِ يُساوي الغِياب — لا يُرفَضُ الإقلاعُ بِأَيِّهِما؛
    /// و«sta ging» خارِجَ المَعجَمِ يَرمي بِه <c>AddOtoShipping</c> بِسَبَبِه هُوَ لا بِهذا الرَمز.</summary>
    [Theory]
    [InlineData("production")]
    [InlineData(" Production ")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("sta ging")]
    public void Outside_development_only_staging_fires(string? environment)
    {
        Assert.Empty(OtoBootPolicy.Validate(isDevelopment: false, environment));
        OtoBootPolicy.AssertSafeOutsideDevelopment(isDevelopment: false, environment);
    }

    /// <summary>
    /// <para><b>والمَفحوصُ staging بِعَينِها لا <see cref="OtoEnvironment.IsOpen"/></b>: يَومَ يُفتَحُ الإنتاجُ بِسَطرٍ هُناك لا يَصيرُ الحارِسُ رافِضاً لَه.
    /// يُقاسُ بِأَنّ الحُكمَ يَتبَعُ مُضيفَ الاختِبارِ لا «مَفتوح»: <c>production</c> لَه مُضيفٌ مَعروفٌ ولا خَرق.</para>
    /// </summary>
    [Fact]
    public void The_guard_follows_the_staging_host_not_the_open_switch()
    {
        Assert.Equal(OtoEnvironment.StagingBaseUrl, OtoEnvironment.BaseUrlFor(OtoEnvironment.Staging));
        Assert.Equal(OtoEnvironment.ProductionBaseUrl, OtoEnvironment.BaseUrlFor(OtoEnvironment.Production));
        Assert.True(OtoBootPolicy.IsValid(false, OtoEnvironment.Production));
        Assert.False(OtoBootPolicy.IsValid(false, OtoEnvironment.Staging));
    }

    // ─── الرَمي: الرِسالَةُ تَحمِلُ الرَمزَ والاسمَ والعِلاج، ولا صيغَةَ «خارِجَ المَعجَم» ───────────

    [Fact]
    public void The_throw_names_the_code_and_the_variable_and_not_the_out_of_lexicon_shape()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => OtoBootPolicy.AssertSafeOutsideDevelopment(isDevelopment: false, OtoEnvironment.Staging));
        Assert.Contains($"[{OtoBootPolicy.StagingOutsideDevelopment}]", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Shipping__Oto__Environment=staging", ex.Message, StringComparison.Ordinal);
        Assert.Contains("فَرعِ Neon مَنسوخ", ex.Message, StringComparison.Ordinal);
        // صيغَةُ «قيمَتُه «» يَقرَؤُها فَرعُ «خارِجَ المَعجَم» في boot_verdict بِسَبَبٍ آخَر.
        Assert.DoesNotContain("قيمَتُه «", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>والمَعجَمُ مُغلَقٌ بِرَمزٍ واحِد، وكُلُّ رَمزٍ فيه تَقرَؤُه <c>boot_verdict</c> بِحَرفِه — فَرَمزٌ يُضافُ ولا تَعرِفُه البَوّابَةُ يُحمِّرُ هذا الصَفّ.</summary>
    [Fact]
    public void Every_code_in_the_lexicon_is_read_by_the_boot_gate()
    {
        Assert.Equal(new[] { OtoBootPolicy.StagingOutsideDevelopment }, OtoBootPolicy.All);
        var gate = File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, "scripts", "verify-production-boot.sh"));
        Assert.True(gate.Length > 1000, "أَداةٌ عَمياء: البَوّابَةُ لَم تُقرَأ.");
        foreach (var code in OtoBootPolicy.All)
            Assert.Contains($"grep -oE '{code}'", gate, StringComparison.Ordinal);
    }
}
