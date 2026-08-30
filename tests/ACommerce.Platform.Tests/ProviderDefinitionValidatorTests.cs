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
        yield return WithFieldKind("secret_key");
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

    [Fact]
    public void field_kind_out_of_vocabulary_fires_for_a_kind_with_no_vault_yet()
    {
        Assert.DoesNotContain("field_kind_out_of_vocabulary", CodesOf(Valid()));
        Assert.Contains("field_kind_out_of_vocabulary", CodesOf(WithFieldKind("secret_key")));
        Assert.Contains("field_kind_out_of_vocabulary", CodesOf(WithFieldKind("invented")));
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

    [Fact]
    public void platform_key_requires_owner_grant_fires_when_a_tenant_could_fill_it()
    {
        Assert.DoesNotContain("platform_key_requires_owner_grant",
            CodesOf(NoFieldsWithKind(CredentialKinds.PlatformKey)));
        Assert.Contains("platform_key_requires_owner_grant", CodesOf(PlatformKeyWithField()));
    }

    private static ProviderDefinition PlatformKeyWithField() => Valid() with
    {
        Credential = new ProviderCredentialDefinition
        {
            Kind = CredentialKinds.PlatformKey,
            Fields = new[]
            {
                new ProviderFieldDefinition
                {
                    Code = "client_secret", Kind = CredentialKinds.PlatformKey,
                    Label = Ar("سِرُّ العَميل"),
                },
            },
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
        Assert.Throws<ArgumentException>(() => CredentialKinds.Require(CredentialKinds.SecretKey));
        Assert.Throws<ArgumentException>(() => CredentialKinds.Require("invented"));
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
