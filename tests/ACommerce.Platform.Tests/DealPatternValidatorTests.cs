using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using Xunit;

namespace ACommerce.Platform.Tests;

// ─── T5/T6 — بَوّابَة صِحَّة تَعريف النَّمَط ──────────────────────────
// TESTING-PROTOCOL §4 كُتلَة ب. الدَوالّ نَقِيَّة فَوق
// DealPatternDefinition، فَالاختِبار بِلا قاعِدَة بَيانات ولا مُهَيِّئ.
//
// شَرطان مُتَقابِلان يُثبِتان أَنَّ البَوّابَة تَعمَل فِعلاً:
//   (أ) الأَنماط الخَمسَة القِياسيَّة + الاحتِياطيّ تَجتازُها كُلَّها —
//       وإلّا فَالبَوّابَة تَرفُض ما هو قائِم ومُشتَغِل.
//   (ب) أَنماط فاسِدَة مُصطَنَعَة تُرفَض بِرَمز الخَرق المَقصود —
//       وإلّا فَالبَوّابَة تُمَرِّر كُلّ شَيء وهي زينَة.

public class DealPatternValidatorTests
{
    public static TheoryData<string> StandardPatterns => new()
    {
        "trip", "rental", "marketplace", "classifieds", "service"
    };

    // ─── (أ) القِياسيَّة تَجتاز ────────────────────────────────────────

    [Theory]
    [MemberData(nameof(StandardPatterns))]
    public void StandardPatterns_PassBothGates(string pattern)
    {
        var def = DealPatternCatalog.Patterns[pattern];
        var violations = DealPatternValidator.Validate(def);
        Assert.True(violations.Count == 0,
            $"«{pattern}» يَجِب أَن يَجتاز: {string.Join(" | ", violations.Select(x => x.Code))}");
    }

    [Fact]
    public void FallbackPattern_PassesBothGates()
        => Assert.True(DealPatternValidator.IsValid(DealPatternCatalog.Fallback));

    [Fact]
    public void Catalog_HasExactlyTheFiveDocumentedPatterns()
    {
        Assert.Equal(
            new[] { "classifieds", "marketplace", "rental", "service", "trip" },
            DealPatternCatalog.Patterns.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void Catalog_ActorVocabulary_IsTheClosedFour()
        => Assert.Equal(
            new[] { "counterparty", "either", "initiator", "platform" },
            DealPatternCatalog.Actors.OrderBy(a => a, StringComparer.Ordinal).ToArray());

    // ─── (ب) الفاسِدَة تُرفَض ──────────────────────────────────────────

    [Fact]
    public void Rejects_EmptyStages()
        => AssertViolation(new DealPatternDefinition("empty", Array.Empty<DealStageRule>()),
            "stages_empty");

    [Fact]
    public void Rejects_PatternNotStartingWithOffered()
        => AssertViolation(DealPatternCatalog.Define("no_offer",
                DealStage.Booked, DealStage.Confirmed),
            "first_stage_not_offered");

    [Fact]
    public void Rejects_DuplicateStage()
        => AssertViolation(DealPatternCatalog.Define("dup",
                DealStage.Offered, DealStage.Booked, DealStage.Booked),
            "duplicate_stage");

    [Fact]
    public void Rejects_StageOutsideTheEightVocabulary()
        => AssertViolation(new DealPatternDefinition("alien", new[]
            {
                DealPatternCatalog.Row(DealStage.Offered),
                new DealStageRule((DealStage)42, "either", "مَرحَلَة مُختَرَعَة"),
            }),
            "stage_out_of_vocabulary");

    [Fact]
    public void Rejects_ActorOutsideTheFour()
        => AssertViolation(new DealPatternDefinition("bad_actor", new[]
            {
                DealPatternCatalog.Row(DealStage.Offered),
                new DealStageRule(DealStage.Booked, "notary", "حَجز"),
            }),
            "actor_out_of_vocabulary");

    [Fact]
    public void Rejects_MissingActor()
        => AssertViolation(new DealPatternDefinition("no_actor", new[]
            {
                DealPatternCatalog.Row(DealStage.Offered),
                new DealStageRule(DealStage.Booked, "", "حَجز"),
            }),
            "actor_missing");

    [Fact]
    public void Rejects_MissingLabel()
        => AssertViolation(new DealPatternDefinition("no_label", new[]
            {
                DealPatternCatalog.Row(DealStage.Offered),
                new DealStageRule(DealStage.Booked, "counterparty", "   "),
            }),
            "label_missing");

    [Fact]
    public void Rejects_EmptyPatternName()
        => AssertViolation(DealPatternCatalog.Define("  ",
                DealStage.Offered, DealStage.Booked),
            "pattern_name_empty");

    /// <summary>دَورَة: التَّكرار يَجعَل المَشي يَعود إلى مَرحَلَة زارَها —
    /// حَتميَّة الانتِهاء تَسقُط (T6).</summary>
    [Fact]
    public void Rejects_CycleFromRepeatedStage()
        => AssertViolation(DealPatternCatalog.Define("cycle",
                DealStage.Offered, DealStage.Booked, DealStage.Offered),
            "termination_not_deterministic");

    /// <summary>مَرحَلَة غَير نِهائيَّة بِفاعِل خارِج الأَربَعَة = لا أَحَد
    /// يَملِك تَحريكَها، فَالتَّدَفُّق يَقِف عِندَها إلى الأَبَد (T6).</summary>
    [Fact]
    public void Rejects_UnmovableNonFinalStage()
        => AssertViolation(new DealPatternDefinition("stuck", new[]
            {
                DealPatternCatalog.Row(DealStage.Offered),
                new DealStageRule(DealStage.Booked, "ghost", "حَجز"),
                DealPatternCatalog.Row(DealStage.Confirmed),
            }),
            "orphan_stage_unmovable");

    // ─── خَصائِص عامَّة عَلى الكاتالوج ────────────────────────────────

    [Theory]
    [MemberData(nameof(StandardPatterns))]
    public void EveryStandardStage_CarriesItsOwnActorAndLabel(string pattern)
    {
        // التَّعريف مُكتَفٍ بِذاتِه — شَرط تَخزينِه وَثيقَةً واحِدَة لاحِقاً.
        foreach (var row in DealPatternCatalog.Patterns[pattern].Stages)
        {
            Assert.Contains(row.Actor, DealPatternCatalog.Actors);
            Assert.False(string.IsNullOrWhiteSpace(row.LabelAr));
        }
    }

    [Theory]
    [MemberData(nameof(StandardPatterns))]
    public void DefinitionRows_AgreeWithPublicPolicySurface(string pattern)
    {
        // الجِسر: ما يُعلِنُه التَّعريف = ما تُجيب بِه الواجِهَة العامَّة.
        var def = DealPatternCatalog.Patterns[pattern];
        Assert.Equal(DealsPolicy.StagesFor(pattern), def.StageOrder);
        foreach (var row in def.Stages)
        {
            Assert.Equal(DealsPolicy.Actor(row.Stage), row.Actor);
            Assert.Equal(DealsPolicy.LabelAr(row.Stage), row.LabelAr);
            Assert.Equal(DealsPolicy.Next(pattern, row.Stage), def.Next(row.Stage));
        }
    }

    private static void AssertViolation(DealPatternDefinition def, string expectedCode)
    {
        var codes = DealPatternValidator.Validate(def).Select(v => v.Code).ToArray();
        Assert.Contains(expectedCode, codes);
    }
}
