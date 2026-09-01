using ACommerce.Kit.Compliance;
using Xunit;

namespace ACommerce.Platform.Tests;

/// <summary>
/// <para><b>بَوّابَةُ تَعريفاتِ الالتِزامات — لِكُلِّ رَمزِ خَرقٍ
/// اختِبارٌ موجِبٌ وسالِب</b> (القاعِدَة ٤). ونَفسُ شَكلِ
/// <c>ProviderDefinitionValidatorTests</c>
/// و<c>RoleDefinitionValidatorTests</c> حَرفاً.</para>
///
/// <para><b>ولِماذا يُقاسُ المُصادِقُ أَصلاً</b>: هُوَ ما يَمنَعُ
/// تَعريفاً مُشَوَّهاً مِن أَن يَصِلَ لَوحَةَ الامتِثال. ولَوحَةٌ
/// تَعرِضُ بَنداً بِلا مَصدَرٍ أَو بِرَمزِ رَفضٍ مُكَرَّرٍ أَسوَأُ
/// مِن لَوحَةٍ لا تَعمَل: الأولى تُقرَأُ ويُبنى عَلَيها.</para>
/// </summary>
public class ObligationDefinitionValidatorTests
{
    // ─── تَعريفٌ سَليمٌ يُشتَقُّ مِنه كُلُّ سالِب ──────────────────

    private static IReadOnlyDictionary<string, string?> Ar(string v) =>
        new Dictionary<string, string?> { ["ar"] = v, ["en"] = null };

    private static EvidenceRequirement GoodEvidence(
        string code = "e1", string kind = EvidenceKinds.TextPresent,
        string target = "some.key.here", string rejection = "some_rejection_code") => new()
    {
        Code = code,
        Kind = kind,
        Target = target,
        RejectionCode = rejection,
        Label = Ar("شاهِدٌ ما"),
        Remedy = Ar("يُسَدُّ هكَذا"),
    };

    private static ObligationDefinition Good() => new()
    {
        Id = "sample_obligation",
        Level = ComplianceLevels.Platform,
        Label = Ar("التِزامٌ لِلقِياس"),
        Source = new ObligationSource
        {
            Authority = "جِهَةٌ ما",
            Reference = "مادَّةٌ ما",
            QuotedAr = "نَصٌّ مَنقول.",
        },
        Evidence = [GoodEvidence()],
    };

    private static IReadOnlyList<string> CodesOf(ObligationDefinition d) =>
        ObligationDefinitionValidator.Validate(d).Select(v => v.Code).ToList();

    // ─── المَعجَمُ نَفسُه ──────────────────────────────────────────

    [Fact]
    public void The_vocabulary_is_exactly_these_eighteen_codes()
        => Assert.Equal(
            new[]
            {
                "id_empty", "id_pattern", "level_out_of_vocabulary",
                "label_missing_arabic", "source_incomplete", "evidence_empty",
                "evidence_code_empty", "evidence_code_duplicate",
                "evidence_kind_out_of_vocabulary", "evidence_target_empty",
                "evidence_label_missing_arabic", "rejection_code_empty",
                "rejection_code_pattern", "rejection_code_duplicate",
                "forbidden_phrases_required", "forbidden_phrases_forbidden",
                "route_target_not_absolute", "not_checkable_reason_missing_arabic",
            },
            ObligationDefinitionValidator.Codes);

    /// <summary>حارِسُ العَمى (القاعِدَة ١٠): كُلُّ رَمزٍ في المَعجَمِ
    /// <b>يُنتَجُ فِعلاً</b> مِن سالِبٍ في هذا المِلَفّ. رَمزٌ يُعلَنُ
    /// ولا يُنتَجُ زينَةٌ في قائِمَة.</summary>
    [Fact]
    public void Every_declared_code_is_produced_by_at_least_one_negative_here()
    {
        var produced = NegativeSamples().SelectMany(CodesOf)
                                        .Distinct(StringComparer.Ordinal)
                                        .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(18, ObligationDefinitionValidator.Codes.Count);
        foreach (var code in ObligationDefinitionValidator.Codes)
            Assert.True(produced.Contains(code),
                $"الرَمز «{code}» مُعلَنٌ في المَعجَمِ ولا يُنتِجُه سالِبٌ واحِد.");
    }

    private static IEnumerable<ObligationDefinition> NegativeSamples()
    {
        yield return Good() with { Id = "" };
        yield return Good() with { Id = "Bad-Id" };
        yield return Good() with { Level = "galaxy" };
        yield return Good() with { Label = new Dictionary<string, string?> { ["en"] = "x" } };
        yield return Good() with { Source = new ObligationSource() };
        yield return Good() with { Evidence = [] };
        yield return Good() with { Evidence = [GoodEvidence(code: "  ")] };
        yield return Good() with { Evidence = [GoodEvidence(), GoodEvidence(rejection: "other_code")] };
        yield return Good() with { Evidence = [GoodEvidence(kind: "text_vibes")] };
        yield return Good() with { Evidence = [GoodEvidence(target: "")] };
        yield return Good() with
        {
            Evidence = [GoodEvidence() with { Label = new Dictionary<string, string?>() }],
        };
        yield return Good() with { Evidence = [GoodEvidence(rejection: "")] };
        yield return Good() with { Evidence = [GoodEvidence(rejection: "Bad Code")] };
        yield return Good() with { Evidence = [GoodEvidence(), GoodEvidence(code: "e2")] };
        yield return Good() with
        {
            Evidence = [GoodEvidence(kind: EvidenceKinds.TextFreeOf)],
        };
        yield return Good() with
        {
            Evidence = [GoodEvidence() with { ForbiddenPhrases = ["x"] }],
        };
        yield return Good() with
        {
            Evidence = [GoodEvidence(kind: EvidenceKinds.RouteReachable, target: "me/delete")],
        };
        yield return Good() with
        {
            NotCheckable = [new UncheckableClause { Code = "c", Reason = ComplianceText.Empty }],
        };
    }

    // ─── مُوجِب ────────────────────────────────────────────────────

    [Fact]
    public void A_well_formed_definition_passes()
    {
        Assert.Empty(ObligationDefinitionValidator.Validate(Good()));
        Assert.True(ObligationDefinitionValidator.IsValid(Good()));
    }

    [Fact]
    public void A_tenant_level_definition_passes()
        => Assert.True(ObligationDefinitionValidator.IsValid(
            Good() with { Level = ComplianceLevels.Tenant }));

    [Fact]
    public void A_route_evidence_with_an_absolute_target_passes()
        => Assert.True(ObligationDefinitionValidator.IsValid(Good() with
        {
            Evidence = [GoodEvidence(kind: EvidenceKinds.RouteReachable, target: "/{slug}/me/delete")],
        }));

    [Fact]
    public void A_text_free_of_evidence_with_phrases_passes()
        => Assert.True(ObligationDefinitionValidator.IsValid(Good() with
        {
            Evidence =
            [
                GoodEvidence(kind: EvidenceKinds.TextFreeOf) with
                {
                    ForbiddenPhrases = ["راسِلنا"],
                },
            ],
        }));

    [Fact]
    public void A_declared_uncheckable_clause_with_an_arabic_reason_passes()
        => Assert.True(ObligationDefinitionValidator.IsValid(Good() with
        {
            NotCheckable = [new UncheckableClause { Code = "c", Reason = Ar("سُلوكٌ لا نَصّ.") }],
        }));

    [Fact]
    public void Two_evidence_items_with_distinct_codes_and_rejections_pass()
        => Assert.True(ObligationDefinitionValidator.IsValid(Good() with
        {
            Evidence = [GoodEvidence(), GoodEvidence(code: "e2", rejection: "second_code")],
        }));

    // ─── سالِب — كُلُّ رَمزٍ عَلى حِدَة ────────────────────────────

    [Fact]
    public void An_empty_id_is_refused()
        => Assert.Contains("id_empty", CodesOf(Good() with { Id = "" }));

    [Fact]
    public void An_id_outside_the_pattern_is_refused()
        => Assert.Contains("id_pattern", CodesOf(Good() with { Id = "Art-6" }));

    [Fact]
    public void A_level_outside_the_vocabulary_is_refused()
        => Assert.Contains("level_out_of_vocabulary", CodesOf(Good() with { Level = "galaxy" }));

    [Fact]
    public void A_label_without_arabic_is_refused()
        => Assert.Contains("label_missing_arabic",
            CodesOf(Good() with { Label = new Dictionary<string, string?> { ["en"] = "x" } }));

    /// <summary>القاعِدَة ١٦ مَفروضَةً: التِزامٌ بِلا مَصدَرٍ مَنقولٍ
    /// لا يَمُرّ.</summary>
    [Fact]
    public void A_definition_without_a_quoted_source_is_refused()
        => Assert.Contains("source_incomplete",
            CodesOf(Good() with { Source = new ObligationSource() }));

    [Fact]
    public void A_partially_filled_source_is_still_refused()
        => Assert.Contains("source_incomplete", CodesOf(Good() with
        {
            Source = new ObligationSource { Authority = "جِهَة", Reference = "مادَّة" },
        }));

    [Fact]
    public void An_obligation_without_evidence_is_refused()
        => Assert.Contains("evidence_empty", CodesOf(Good() with { Evidence = [] }));

    [Fact]
    public void An_evidence_without_a_code_is_refused()
        => Assert.Contains("evidence_code_empty",
            CodesOf(Good() with { Evidence = [GoodEvidence(code: " ")] }));

    [Fact]
    public void A_duplicate_evidence_code_is_refused()
        => Assert.Contains("evidence_code_duplicate", CodesOf(Good() with
        {
            Evidence = [GoodEvidence(), GoodEvidence(rejection: "another_code")],
        }));

    [Fact]
    public void An_evidence_kind_outside_the_vocabulary_is_refused()
        => Assert.Contains("evidence_kind_out_of_vocabulary",
            CodesOf(Good() with { Evidence = [GoodEvidence(kind: "text_vibes")] }));

    [Fact]
    public void An_evidence_without_a_target_is_refused()
        => Assert.Contains("evidence_target_empty",
            CodesOf(Good() with { Evidence = [GoodEvidence(target: "")] }));

    [Fact]
    public void An_evidence_label_without_arabic_is_refused()
        => Assert.Contains("evidence_label_missing_arabic", CodesOf(Good() with
        {
            Evidence = [GoodEvidence() with { Label = new Dictionary<string, string?>() }],
        }));

    [Fact]
    public void An_evidence_without_a_rejection_code_is_refused()
        => Assert.Contains("rejection_code_empty",
            CodesOf(Good() with { Evidence = [GoodEvidence(rejection: "")] }));

    [Fact]
    public void A_rejection_code_outside_the_pattern_is_refused()
        => Assert.Contains("rejection_code_pattern",
            CodesOf(Good() with { Evidence = [GoodEvidence(rejection: "Bad Code")] }));

    /// <summary>رَمزٌ يَدُلُّ عَلى شاهِدَينِ لا يَدُلُّ عَلى
    /// شَيء.</summary>
    [Fact]
    public void A_duplicate_rejection_code_within_one_obligation_is_refused()
        => Assert.Contains("rejection_code_duplicate", CodesOf(Good() with
        {
            Evidence = [GoodEvidence(), GoodEvidence(code: "e2")],
        }));

    [Fact]
    public void A_text_free_of_evidence_without_phrases_is_refused()
        => Assert.Contains("forbidden_phrases_required", CodesOf(Good() with
        {
            Evidence = [GoodEvidence(kind: EvidenceKinds.TextFreeOf)],
        }));

    /// <summary>العِبارَةُ الفارِغَةُ تُطابِقُ كُلَّ نَصّ — فَشاهِدٌ
    /// يَحمِلُها يَرفُضُ كُلَّ شَيءٍ دائِماً.</summary>
    [Fact]
    public void A_blank_forbidden_phrase_is_refused()
        => Assert.Contains("forbidden_phrases_required", CodesOf(Good() with
        {
            Evidence =
            [
                GoodEvidence(kind: EvidenceKinds.TextFreeOf) with
                {
                    ForbiddenPhrases = ["ok", "  "],
                },
            ],
        }));

    [Fact]
    public void Forbidden_phrases_on_another_kind_are_refused()
        => Assert.Contains("forbidden_phrases_forbidden", CodesOf(Good() with
        {
            Evidence = [GoodEvidence() with { ForbiddenPhrases = ["x"] }],
        }));

    [Fact]
    public void A_relative_route_target_is_refused()
        => Assert.Contains("route_target_not_absolute", CodesOf(Good() with
        {
            Evidence = [GoodEvidence(kind: EvidenceKinds.RouteReachable, target: "me/delete")],
        }));

    [Fact]
    public void An_uncheckable_clause_without_an_arabic_reason_is_refused()
        => Assert.Contains("not_checkable_reason_missing_arabic", CodesOf(Good() with
        {
            NotCheckable = [new UncheckableClause { Code = "c", Reason = ComplianceText.Empty }],
        }));

    // ─── المَعاجِمُ المُغلَقَة ─────────────────────────────────────

    [Fact]
    public void The_level_vocabulary_is_exactly_two()
        => Assert.Equal(new[] { "platform", "tenant" }, ComplianceLevels.All);

    [Fact]
    public void A_level_outside_the_vocabulary_throws_at_composition_time()
        => Assert.Throws<ArgumentException>(() => ComplianceLevels.Require("galaxy"));

    [Fact]
    public void The_evidence_kind_vocabulary_is_exactly_four()
        => Assert.Equal(
            new[] { "text_present", "text_filled", "text_free_of", "route_reachable" },
            EvidenceKinds.All);

    [Fact]
    public void An_evidence_kind_outside_the_vocabulary_throws_at_composition_time()
        => Assert.Throws<ArgumentException>(() => EvidenceKinds.Require("text_vibes"));

    [Fact]
    public void Three_kinds_read_text_and_one_reads_the_route_table()
    {
        Assert.Equal(3, EvidenceKinds.All.Count(EvidenceKinds.ReadsText));
        Assert.False(EvidenceKinds.ReadsText(EvidenceKinds.RouteReachable));
    }
}
