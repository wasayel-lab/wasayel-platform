using ACommerce.Platform.Providers;
using Xunit;

namespace ACommerce.Platform.Tests;

// ═══ المُصادِق — لِكُلّ رَمزِ خَرقٍ اختِبارٌ موجِبٌ وسالِب (القاعِدَة ٤) ═══
//
// «الموجِب» = تَعريفٌ سَليمٌ لا يُنتِج الرَمز، و«السالِب» = تَعريفٌ
// يَخرِقُه فَيُنتِجُه. ورَمزٌ بِلا الاثنَينِ مَعاً لا يُعرَف أَيَحرُس
// شَيئاً أَم يَنام: فَحصُ الوُجودِ وَحدَه يَمُرّ عَلى مُصادِقٍ يَرمي
// كُلَّ شَيء، وفَحصُ الغِيابِ وَحدَه يَمُرّ عَلى مُصادِقٍ لا يَرمي شَيئاً.
public class ProviderDefinitionValidatorTests
{
    private static IReadOnlyDictionary<string, string?> Ar(string text) =>
        new Dictionary<string, string?>(StringComparer.Ordinal) { ["ar"] = text, ["en"] = null };

    private static readonly IReadOnlyDictionary<string, string?> NoArabic =
        new Dictionary<string, string?>(StringComparer.Ordinal) { ["en"] = "only english" };

    /// <summary>تَعريفٌ سَليمٌ يُنتِج صِفرَ خَرق — وهُوَ الطَرَفُ
    /// الموجِبُ لِكُلّ رَمزٍ أَدناه.</summary>
    private static ProviderDefinition Valid() => new()
    {
        Slug = "sound_provider",
        Capability = ProviderCapabilities.Payments,
        Label = Ar("مُزَوِّدٌ سَليم"),
        Description = Ar("وَصفٌ عَرَبيّ."),
        Revocation = Ar("كَيفَ يُسحَب."),
        Credential = new ProviderCredentialDefinition
        {
            Kind = CredentialKinds.HostedLink,
            Fields = new[]
            {
                new ProviderFieldDefinition
                {
                    Code = "invoice_url",
                    Kind = CredentialKinds.HostedLink,
                    Label = Ar("رابِط الدَفع"),
                    IsRequired = true,
                    HostAllowlist = new[] { "moyasar.com" },
                },
            },
        },
    };

    private static string[] CodesOf(ProviderDefinition d) =>
        ProviderDefinitionValidator.Validate(d).Select(v => v.Code).ToArray();

    // ─── الطَرَفُ الموجِبُ العامّ ─────────────────────────────────────

    [Fact]
    public void A_sound_definition_produces_no_violation_at_all()
    {
        Assert.Empty(ProviderDefinitionValidator.Validate(Valid()));
        Assert.True(ProviderDefinitionValidator.IsValid(Valid()));
    }

    [Fact]
    public void Twelve_codes_are_declared_and_none_repeats()
    {
        Assert.Equal(12, ProviderDefinitionValidator.Codes.Count);
        Assert.Equal(12, ProviderDefinitionValidator.Codes
            .Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary><b>حارِسُ العَمى</b>: كُلُّ رَمزٍ مُعلَنٍ يَجِب أَن
    /// يَظهَرَ في اختِبارٍ سالِبٍ واحِدٍ عَلى الأَقَلّ — وإلّا كانَ
    /// المَعجَمُ يَنمو بِالخَيال. القائِمَةُ أَدناه تُبنى مِن
    /// <b>نَتائِجِ</b> الاختِباراتِ السالِبَة نَفسِها لا مِن نَصٍّ
    /// مَكتوبٍ بِاليَد.</summary>
    [Fact]
    public void Every_declared_code_is_actually_produced_by_some_breach()
    {
        var produced = new HashSet<string>(StringComparer.Ordinal);
        foreach (var d in AllBreachingDefinitions())
            foreach (var c in CodesOf(d))
                produced.Add(c);

        Assert.True(produced.Count > 0, "أَداة عَمياء: صِفرُ خَرقٍ أُنتِج.");

        var never = ProviderDefinitionValidator.Codes
            .Where(c => !produced.Contains(c)).ToArray();

        Assert.True(never.Length == 0,
            "رَمزُ خَرقٍ مُعلَنٌ لا يُنتِجُه أَيُّ مُدخَل — يُحذَف أَو يُحرَس:\n  "
            + string.Join("\n  ", never));
    }

    private static IEnumerable<ProviderDefinition> AllBreachingDefinitions()
    {
        yield return Valid() with { Slug = "" };
        yield return Valid() with { Slug = "Bad-Slug" };
        yield return Valid() with { Capability = "storefront" };
        yield return Valid() with
        {
            Credential = Valid().Credential with { Kind = "vaulted_thing" },
        };
        // ‏`credential_file` لا `secret_key` مُنذُ ‏2026-10-12: دَخَلَ الثاني المَعجَمَ
        // (‏ADR-032 الخُطوَة 5)، فَلَم يَعُد يُنتِجُ هذا الرَمز.
        yield return WithFieldKind("credential_file");
        yield return DuplicateFieldCodes();
        yield return Valid() with { Label = NoArabic };
        yield return LinkWithoutAllowlist();
        yield return SecretWithAllowlist();
        yield return WebhookWithoutVerifiableField();
        yield return BindingBelowField();
        yield return PlatformKeyWithField();
    }

    // ─── ١. slug_empty ───────────────────────────────────────────────

    [Fact]
    public void slug_empty_fires_only_when_the_slug_is_empty()
    {
        Assert.DoesNotContain("slug_empty", CodesOf(Valid()));
        Assert.Contains("slug_empty", CodesOf(Valid() with { Slug = "  " }));
    }

    // ─── ٢. slug_pattern ─────────────────────────────────────────────

    [Theory]
    [InlineData("moyasar_hosted")]
    [InlineData("a")]
    [InlineData("p2p_link")]
    public void slug_pattern_accepts_a_lowercase_snake_slug(string slug)
        => Assert.DoesNotContain("slug_pattern", CodesOf(Valid() with { Slug = slug }));

    [Theory]
    [InlineData("Moyasar")]
    [InlineData("2fast")]
    [InlineData("has-dash")]
    [InlineData("has space")]
    public void slug_pattern_fires_outside_the_pattern(string slug)
        => Assert.Contains("slug_pattern", CodesOf(Valid() with { Slug = slug }));

    // ─── ٣. capability_out_of_vocabulary ─────────────────────────────

    [Fact]
    public void capability_out_of_vocabulary_accepts_all_nine_and_rejects_a_tenth()
    {
        foreach (var c in ProviderCapabilities.All)
            Assert.DoesNotContain("capability_out_of_vocabulary",
                CodesOf(Valid() with { Capability = c }));

        Assert.Contains("capability_out_of_vocabulary",
            CodesOf(Valid() with { Capability = "storefront" }));
    }

    // ─── ٤. credential_kind_out_of_vocabulary ────────────────────────

    [Fact]
    public void credential_kind_out_of_vocabulary_accepts_the_enforced_vocabulary_only()
    {
        // الموجِب: كُلُّ نَوعٍ مُلزِمٍ يُقبَل.
        foreach (var k in CredentialKinds.All)
            Assert.DoesNotContain("credential_kind_out_of_vocabulary",
                CodesOf(NoFieldsWithKind(k)));

        // والسالِب مَرَّتان: اسمٌ مُخترَع، **ونَوعٌ مِن التَصنيفِ لَم
        // تُبنَ خِزانَتُه بَعد**. والثاني هُوَ الفَشَلُ المُغلَق:
        // لا سِرَّ يُخَزَّن قَبلَ أَن توجَدَ خِزانَتُه.
        Assert.Contains("credential_kind_out_of_vocabulary",
            CodesOf(NoFieldsWithKind("vaulted_thing")));

        foreach (var k in CredentialKinds.NotYetInVocabulary)
            Assert.Contains("credential_kind_out_of_vocabulary", CodesOf(NoFieldsWithKind(k)));
    }

    private static ProviderDefinition NoFieldsWithKind(string kind) => Valid() with
    {
        Credential = new ProviderCredentialDefinition { Kind = kind, Fields = [] },
    };

    // ─── ٥. field_kind_out_of_vocabulary ─────────────────────────────

    /// <summary><b>‏`credential_file` بَدَلَ `secret_key` مُنذُ ‏2026-10-12</b> (‏ADR-032
    /// §٢‑هـ الخُطوَة 5 بِحَرفِها): الثاني دَخَلَ المَعجَمَ مَعَ خِزانَتِه ومِلَفِّه، فَصارَ
    /// سالِبُ هذا الرَمزِ نَوعاً ما زالَ بِلا خِزانَة — والموجِبُ الجَديدُ يَقولُه.</summary>
    [Fact]
    public void field_kind_out_of_vocabulary_fires_for_a_kind_with_no_vault_yet()
    {
        Assert.DoesNotContain("field_kind_out_of_vocabulary", CodesOf(Valid()));
        Assert.Contains("field_kind_out_of_vocabulary", CodesOf(WithFieldKind("credential_file")));
        Assert.Contains("field_kind_out_of_vocabulary", CodesOf(WithFieldKind("invented")));

        // و`secret_key` في المَعجَمِ الآن: لا يُنتِجُ هذا الرَمز (بَل «نَوعُ الرَبطِ
        // أَدنى مِن حَقلِه» لِأَنّ `WithFieldKind` تَربِطُ بِـ`hosted_link`).
        Assert.DoesNotContain("field_kind_out_of_vocabulary", CodesOf(WithFieldKind("secret_key")));
    }

    private static ProviderDefinition WithFieldKind(string kind) => Valid() with
    {
        Credential = new ProviderCredentialDefinition
        {
            Kind = CredentialKinds.HostedLink,
            Fields = new[]
            {
                new ProviderFieldDefinition
                {
                    Code = "f", Kind = kind, Label = Ar("حَقل"), IsRequired = true,
                },
            },
        },
    };

    // ─── ٦. field_code_duplicate ─────────────────────────────────────

    [Fact]
    public void field_code_duplicate_fires_on_a_repeated_or_empty_code()
    {
        Assert.DoesNotContain("field_code_duplicate", CodesOf(Valid()));
        Assert.Contains("field_code_duplicate", CodesOf(DuplicateFieldCodes()));
    }

    private static ProviderDefinition DuplicateFieldCodes()
    {
        var f = new ProviderFieldDefinition
        {
            Code = "invoice_url",
            Kind = CredentialKinds.HostedLink,
            Label = Ar("رابِط"),
            HostAllowlist = new[] { "moyasar.com" },
        };
        return Valid() with
        {
            Credential = new ProviderCredentialDefinition
            {
                Kind = CredentialKinds.HostedLink,
                Fields = new[] { f, f with { } },
            },
        };
    }

    // ─── ٧. label_missing_arabic ─────────────────────────────────────

    [Fact]
    public void label_missing_arabic_fires_on_each_localized_container()
    {
        Assert.DoesNotContain("label_missing_arabic", CodesOf(Valid()));
        Assert.Contains("label_missing_arabic", CodesOf(Valid() with { Label = NoArabic }));
        Assert.Contains("label_missing_arabic", CodesOf(Valid() with { Description = NoArabic }));
        Assert.Contains("label_missing_arabic", CodesOf(Valid() with { Revocation = NoArabic }));
        Assert.Contains("label_missing_arabic", CodesOf(WithFieldLabel(NoArabic)));
    }

    private static ProviderDefinition WithFieldLabel(IReadOnlyDictionary<string, string?> label)
        => Valid() with
        {
            Credential = new ProviderCredentialDefinition
            {
                Kind = CredentialKinds.HostedLink,
                Fields = new[]
                {
                    new ProviderFieldDefinition
                    {
                        Code = "invoice_url", Kind = CredentialKinds.HostedLink,
                        Label = label, HostAllowlist = new[] { "moyasar.com" },
                    },
                },
            },
        };

    // ─── ٨. host_allowlist_required_for_link ─────────────────────────

    [Fact]
    public void host_allowlist_required_for_link_fires_on_a_link_with_no_fence()
    {
        Assert.DoesNotContain("host_allowlist_required_for_link", CodesOf(Valid()));
        Assert.Contains("host_allowlist_required_for_link", CodesOf(LinkWithoutAllowlist()));
    }

    private static ProviderDefinition LinkWithoutAllowlist() => Valid() with
    {
        Credential = new ProviderCredentialDefinition
        {
            Kind = CredentialKinds.HostedLink,
            Fields = new[]
            {
                new ProviderFieldDefinition
                {
                    Code = "invoice_url", Kind = CredentialKinds.HostedLink,
                    Label = Ar("رابِط"), HostAllowlist = [],
                },
            },
        },
    };

    // ─── ٩. host_allowlist_forbidden_for_secret ──────────────────────
    //
    // ‏`platform_key` هُوَ النَوعُ الوَحيدُ الشَبيهُ بِالسِرِّ داخِلَ
    // المَعجَمِ المُلزِمِ اليَوم — فَبِه يُقاسُ الرَمز.
    // (‏2026-10-12: صارَ `secret_key` ثانِيَهُما في المَعجَم، ويُقاسُ بِه الرَمزُ
    // أَيضاً عَلى مِلَفِّه المَشحون — `ProviderCatalogTests.The_secret_key_provider_…`.)

    [Fact]
    public void host_allowlist_forbidden_for_secret_fires_when_a_secret_carries_hosts()
    {
        Assert.DoesNotContain("host_allowlist_forbidden_for_secret", CodesOf(Valid()));
        Assert.Contains("host_allowlist_forbidden_for_secret", CodesOf(SecretWithAllowlist()));
    }

    private static ProviderDefinition SecretWithAllowlist() => Valid() with
    {
        Credential = new ProviderCredentialDefinition
        {
            Kind = CredentialKinds.PlatformKey,
            Fields = new[]
            {
                new ProviderFieldDefinition
                {
                    Code = "our_key", Kind = CredentialKinds.PlatformKey,
                    Label = Ar("مِفتاحُنا"), HostAllowlist = new[] { "paypal.com" },
                },
            },
        },
    };

    // ─── ١٠. webhook_requires_verifiable_kind ────────────────────────

    [Fact]
    public void webhook_requires_verifiable_kind_fires_on_an_unverifiable_endpoint()
    {
        Assert.DoesNotContain("webhook_requires_verifiable_kind", CodesOf(Valid()));
        Assert.Contains("webhook_requires_verifiable_kind",
            CodesOf(WebhookWithoutVerifiableField()));
    }

    private static ProviderDefinition WebhookWithoutVerifiableField() => Valid() with
    {
        Webhook = new ProviderWebhookDefinition { Path = "moyasar", Verify = "secret_token" },
    };

    // ─── ١١. binding_kind_below_field_kind ───────────────────────────

    [Fact]
    public void binding_kind_below_field_kind_fires_when_the_binding_understates_its_fields()
    {
        Assert.DoesNotContain("binding_kind_below_field_kind", CodesOf(Valid()));
        Assert.Contains("binding_kind_below_field_kind", CodesOf(BindingBelowField()));
    }

    private static ProviderDefinition BindingBelowField() => Valid() with
    {
        Credential = new ProviderCredentialDefinition
        {
            // نَوعُ الرَبطِ `none` وحَقلُه `hosted_link` — أَي أَنّ
            // الشاشَةَ كانَت سَتُعامِلَه «لا شَيءَ يُخَزَّن».
            Kind = CredentialKinds.None,
            Fields = new[]
            {
                new ProviderFieldDefinition
                {
                    Code = "invoice_url", Kind = CredentialKinds.HostedLink,
                    Label = Ar("رابِط"), HostAllowlist = new[] { "moyasar.com" },
                },
            },
        },
    };

    // ─── ١٢. platform_key_requires_owner_grant ───────────────────────

    /// <summary><b>قُلِبَ بِاسمِه ‏2026-10-12</b> (‏ADR-033 §٢‑ج‑١ والخُطوَة 6، تَيّارُ S8): كانَ
    /// <c>platform_key_requires_owner_grant_fires_when_a_tenant_could_fill_it</c> — «أَيُّ حَقلٍ تَحتَ <c>platform_key</c>
    /// يُرفَض»، وسالِبُه حَقلٌ مِن نَوعِ <c>platform_key</c>. وصارَ الشَرطُ «حَقلٌ <b>غَيرُ سِرِّيِّ الشَكل</b>»: الاعتِمادُ
    /// السوقِيُّ سِرُّنا، وتَحتَه حَقلا بائِعٍ سِرِّيّانِ يُختَمان. <b>المُوجِب</b> (لا يُنتِجُ الرَمز): بِلا حَقل، وبِحَقلِ
    /// <c>secret_key</c>، وبِحَقلِ <c>issued_secret</c>، وبِهِما مَعاً (شَكلُ تَعريفِ OTO). <b>والسالِب</b> (يُنتِجُه): حَقلٌ
    /// رابِطٌ (<c>hosted_link</c> بِسياجِه، فَلا يُنتِجُ إلّا هذا الرَمز)، وحَقلٌ <c>none</c>. والرُموزُ اثنا عَشَرَ كَما كانَت.</summary>
    [Fact]
    public void platform_key_requires_owner_grant_fires_only_for_a_field_that_is_not_secret_like()
    {
        Assert.DoesNotContain("platform_key_requires_owner_grant",
            CodesOf(NoFieldsWithKind(CredentialKinds.PlatformKey)));
        Assert.Empty(CodesOf(PlatformKeyWith(CredentialKinds.SecretKey)));
        Assert.Empty(CodesOf(PlatformKeyWith(CredentialKinds.IssuedSecret)));
        Assert.Empty(CodesOf(PlatformKeyWith(CredentialKinds.SecretKey, CredentialKinds.IssuedSecret)));

        Assert.Equal(new[] { "platform_key_requires_owner_grant" }, CodesOf(PlatformKeyWithField()));
        Assert.Contains("platform_key_requires_owner_grant", CodesOf(PlatformKeyWith(CredentialKinds.None)));
        Assert.Contains("platform_key_requires_owner_grant",
            CodesOf(PlatformKeyWith(CredentialKinds.SecretKey, CredentialKinds.None)));
        Assert.Equal(12, ProviderDefinitionValidator.Codes.Count);
    }

    /// <summary>السالِبُ في <see cref="AllBreachingDefinitions"/>: حَقلٌ رابِطٌ بِسياجِه تَحتَ <c>platform_key</c>
    /// (كانَ حَقلاً مِن نَوعِ <c>platform_key</c> — وهو سِرِّيُّ الشَكلِ فَلَم يَعُد يُنتِجُ الرَمزَ ‏2026-10-12).</summary>
    private static ProviderDefinition PlatformKeyWithField() => Valid() with
    {
        Credential = new ProviderCredentialDefinition
        {
            Kind = CredentialKinds.PlatformKey,
            Fields = new[]
            {
                new ProviderFieldDefinition
                {
                    Code = "invoice_url", Kind = CredentialKinds.HostedLink,
                    Label = Ar("رابِط"), HostAllowlist = new[] { "moyasar.com" },
                },
            },
        },
    };

    private static ProviderDefinition PlatformKeyWith(params string[] kinds) => Valid() with
    {
        Credential = new ProviderCredentialDefinition
        {
            Kind = CredentialKinds.PlatformKey,
            Fields = kinds.Select((k, i) => new ProviderFieldDefinition
            {
                Code = $"f{i}", Kind = k, Label = Ar("حَقلُ بائِع"), IsRequired = true,
            }).ToArray(),
        },
    };

    // ─── المَعجَمُ نَفسُه ────────────────────────────────────────────

    [Fact]
    public void The_enforced_vocabulary_and_the_deferred_one_are_disjoint_and_cover_the_nine()
    {
        var all = CredentialKinds.All.Concat(CredentialKinds.NotYetInVocabulary).ToArray();
        Assert.Equal(9, all.Length);
        Assert.Equal(9, all.Distinct(StringComparer.Ordinal).Count());

        foreach (var k in CredentialKinds.All) Assert.True(CredentialKinds.Contains(k));
        foreach (var k in CredentialKinds.NotYetInVocabulary)
            Assert.False(CredentialKinds.Contains(k));
    }

    [Fact]
    public void Require_throws_outside_the_enforced_vocabulary_and_returns_inside_it()
    {
        Assert.Equal(CredentialKinds.HostedLink,
            CredentialKinds.Require(CredentialKinds.HostedLink));
        // ‏`credential_file` بَدَلَ `secret_key` مُنذُ ‏2026-10-12 (‏ADR-032 الخُطوَة 5).
        Assert.Throws<ArgumentException>(() => CredentialKinds.Require(CredentialKinds.CredentialFile));
        Assert.Throws<ArgumentException>(() => CredentialKinds.Require("invented"));
        Assert.Equal(CredentialKinds.SecretKey, CredentialKinds.Require(CredentialKinds.SecretKey));
    }

    /// <summary><b>‏<c>issued_secret</c> في المَعجَمِ المُلزِمِ ‏2026-10-12</b> (‏ADR-033 الخُطوَتانِ 5 و6): مُوجَبٌ — يُقبَلُ في
    /// <c>Require</c> وفي <c>All</c> بِرُتبَتِه بَينَ <c>hosted_link</c> و<c>secret_key</c>، وحَقلٌ مِنه لا يُنتِجُ
    /// <c>field_kind_out_of_vocabulary</c>، ويَصلُحُ لِلتَحَقُّقِ مِن وارِد؛ وسالِبٌ — جارُه <c>shared_secret</c> باقٍ
    /// خارِجَ المَعجَمِ يُرمى ويُرَدُّ حَقلُه، و<c>issued_secret</c> لَيسَ في القائِمَةِ المُؤَجَّلَةِ بَعدُ.</summary>
    [Fact]
    public void Issued_secret_is_enforced_now_and_its_neighbour_shared_secret_is_still_deferred()
    {
        Assert.Equal(CredentialKinds.IssuedSecret, CredentialKinds.Require(CredentialKinds.IssuedSecret));
        Assert.Equal(new[]
            {
                CredentialKinds.None, CredentialKinds.HostedLink, CredentialKinds.IssuedSecret,
                CredentialKinds.SecretKey, CredentialKinds.PlatformKey,
            },
            CredentialKinds.All.ToArray());
        Assert.DoesNotContain(CredentialKinds.IssuedSecret, CredentialKinds.NotYetInVocabulary);
        Assert.DoesNotContain("field_kind_out_of_vocabulary", CodesOf(WithFieldKind(CredentialKinds.IssuedSecret)));
        Assert.True(CredentialKinds.CanVerifyWebhook(CredentialKinds.IssuedSecret));

        Assert.Throws<ArgumentException>(() => CredentialKinds.Require(CredentialKinds.SharedSecret));
        Assert.Contains(CredentialKinds.SharedSecret, CredentialKinds.NotYetInVocabulary);
        Assert.Contains("field_kind_out_of_vocabulary", CodesOf(WithFieldKind(CredentialKinds.SharedSecret)));
    }

    [Fact]
    public void The_rank_ladder_is_strictly_ordered_across_the_nine()
    {
        var ordered = new[]
        {
            CredentialKinds.None, CredentialKinds.HostedLink, CredentialKinds.PublishedKey,
            CredentialKinds.DelegatedGrant, CredentialKinds.IssuedSecret,
            CredentialKinds.SharedSecret, CredentialKinds.SecretKey,
            CredentialKinds.CredentialFile, CredentialKinds.PlatformKey,
        };

        for (var i = 1; i < ordered.Length; i++)
            Assert.True(CredentialKinds.Rank(ordered[i]) > CredentialKinds.Rank(ordered[i - 1]),
                $"«{ordered[i]}» لَيسَ فَوقَ «{ordered[i - 1]}» في السُلَّم.");

        Assert.Equal(-1, CredentialKinds.Rank("invented"));
    }
}
