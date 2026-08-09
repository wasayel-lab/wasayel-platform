using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using Xunit;

namespace ACommerce.Platform.Tests;

// ─── اختِبارات تَوصيف DealsPolicy ────────────────────────────────────
// بِذرَة T5/T6 مِن TESTING-PROTOCOL §4 عَلى السِّياسَة المُجَمَّعَة
// الحاليَّة: تُثَبِّت الخَصائِص الشَّكليَّة (بِدايَة Offered، خَطّيَّة،
// حَتميَّة الانتِهاء، فاعِل مُعَرَّف لِكُلّ مَرحَلَة) قَبل رَفع السِّياسَة
// إلى بَيانات — أَيّ تَغيير سُلوكيّ صامِت يَكسِرها.

public class DealsPolicyTests
{
    /// <summary>الأَنماط الخَمسَة القِياسيَّة + نَمَط غَير مَعروف
    /// (يَسقُط عَلى التَّسَلسُل الافتِراضيّ).</summary>
    public static TheoryData<string> Patterns => new()
    {
        "trip", "rental", "marketplace", "classifieds", "service",
        "pattern_from_the_future"
    };

    private static readonly string[] KnownActors =
        { "initiator", "counterparty", "either", "platform" };

    [Theory]
    [MemberData(nameof(Patterns))]
    public void Stages_NotEmpty_And_StartWithOffered(string pattern)
    {
        var stages = DealsPolicy.StagesFor(pattern);
        Assert.NotEmpty(stages);
        Assert.Equal(DealStage.Offered, stages[0]);
    }

    [Theory]
    [MemberData(nameof(Patterns))]
    public void Stages_HaveNoDuplicates(string pattern)
    {
        var stages = DealsPolicy.StagesFor(pattern);
        Assert.Equal(stages.Count, stages.Distinct().Count());
    }

    [Theory]
    [MemberData(nameof(Patterns))]
    public void Next_WalksLinearly_And_TerminatesWithinEightSteps(string pattern)
    {
        var stages = DealsPolicy.StagesFor(pattern);
        var current = stages[0];
        var steps = 0;
        while (DealsPolicy.Next(pattern, current) is DealStage next)
        {
            steps++;
            Assert.True(steps <= 8,
                $"النَّمَط «{pattern}» لَم يَنتَهِ خِلال 8 خُطُوات — دَورَة مُحتَمَلَة.");
            Assert.Equal(stages[steps], next); // خَطّيّ: يُطابِق التَّسَلسُل المُعلَن
            current = next;
        }
        Assert.Equal(stages[^1], current);        // وَصَلنا آخِر مَرحَلَة فِعلاً
        Assert.Equal(stages.Count - 1, steps);    // بِلا قَفز ولا تَكرار
    }

    [Theory]
    [MemberData(nameof(Patterns))]
    public void Actor_IsFromClosedVocabulary_ForEveryUsedStage(string pattern)
    {
        foreach (var stage in DealsPolicy.StagesFor(pattern))
            Assert.Contains(DealsPolicy.Actor(stage), KnownActors);
    }

    [Fact]
    public void LabelAr_NonEmpty_ForAllEightStages()
    {
        var all = Enum.GetValues<DealStage>();
        Assert.Equal(8, all.Length); // المُفرَدات المُغلَقَة — META-MODEL §4.2
        foreach (var stage in all)
            Assert.False(string.IsNullOrWhiteSpace(DealsPolicy.LabelAr(stage)),
                $"LabelAr({stage}) فارِغَة.");
    }
}
