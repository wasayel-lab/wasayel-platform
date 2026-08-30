using System.Text.RegularExpressions;

namespace ACommerce.Platform.Providers;

/// <summary>نَفسُ شَكلِ <c>RoleDefinitionViolation</c> و
/// <c>ApiKeyViolation</c> و<c>CapabilityViolation</c> حَرفاً
/// (القاعِدَة ٤).</summary>
public sealed record ProviderDefinitionViolation(string Code, string MessageAr);

/// <summary>
/// <para><b>المُصادِق — اثنا عَشَرَ رَمزَ خَرق، ولِكُلٍّ اختِبارٌ موجِبٌ
/// وسالِب</b> (القاعِدَة ٤).</para>
/// <para>والتَحميلُ <b>بَوّابَةٌ لا نَقل</b>: كُلُّ مِلَفٍّ يَمُرّ مِن
/// هُنا، وأَيُّ خَرقٍ <b>يُفشِلُ الإقلاعَ بِرَمزِه</b> — فَتَعريفٌ
/// فاسِدٌ لا يَصِل مُستَأجِراً صامِتاً.</para>
/// </summary>
public static class ProviderDefinitionValidator
{
    private static readonly Regex SlugPattern =
        new("^[a-z][a-z0-9_]*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>الرُموزُ الاثنا عَشَر — مُعلَنَةً لِيُقاسَ أَنّ لِكُلٍّ
    /// اختِبارَين، لا لِتُقرَأَ في تَعليق.</summary>
    public static readonly IReadOnlyList<string> Codes = new[]
    {
        "slug_empty",
        "slug_pattern",
        "capability_out_of_vocabulary",
        "credential_kind_out_of_vocabulary",
        "field_kind_out_of_vocabulary",
        "field_code_duplicate",
        "label_missing_arabic",
        "host_allowlist_required_for_link",
        "host_allowlist_forbidden_for_secret",
        "webhook_requires_verifiable_kind",
        "binding_kind_below_field_kind",
        "platform_key_requires_owner_grant",
    };

    public static IReadOnlyList<ProviderDefinitionViolation> Validate(ProviderDefinition d)
    {
        var v = new List<ProviderDefinitionViolation>();

        // ─── الهُوِيَّة ────────────────────────────────────────────────
        if (string.IsNullOrWhiteSpace(d.Slug))
            v.Add(new("slug_empty", "المُزَوِّد بِلا slug."));
        else if (!SlugPattern.IsMatch(d.Slug))
            v.Add(new("slug_pattern",
                $"الـ slug «{d.Slug}» خارِج النَمَط ^[a-z][a-z0-9_]*$."));

        // ─── القُدرَة — مِن المَعجَمِ المُغلَقِ حَصراً ─────────────────
        if (!ProviderCapabilities.Contains(d.Capability))
            v.Add(new("capability_out_of_vocabulary",
                $"القُدرَة «{d.Capability}» في المُزَوِّد «{d.Slug}» خارِج مَعجَم " +
                $"ProviderCapabilities. المَعجَم: {string.Join("، ", ProviderCapabilities.All)}."));

        // ─── حاوِيات التَوطين — العَرَبِيَّة إلزامِيَّة ────────────────
        CheckArabic(v, d.Label,       $"تَسمِيَة المُزَوِّد «{d.Slug}»");
        CheckArabic(v, d.Description, $"وَصف المُزَوِّد «{d.Slug}»");
        CheckArabic(v, d.Revocation,  $"نَصّ سَحب الرَبط في «{d.Slug}»");

        // ─── نَوعُ الرَبط ─────────────────────────────────────────────
        var bindingKindKnown = CredentialKinds.Contains(d.Credential.Kind);
        if (!bindingKindKnown)
            v.Add(new("credential_kind_out_of_vocabulary",
                $"نَوعُ اعتِماد «{d.Slug}» = «{d.Credential.Kind}» خارِج مَعجَم " +
                $"CredentialKinds المُلزِم. المَعجَم: {string.Join("، ", CredentialKinds.All)}."));

        // ─── الحُقول ─────────────────────────────────────────────────
        var seenCode = new HashSet<string>(StringComparer.Ordinal);
        foreach (var f in d.Credential.Fields)
        {
            if (string.IsNullOrWhiteSpace(f.Code) || !seenCode.Add(f.Code))
                v.Add(new("field_code_duplicate",
                    $"رَمزُ الحَقل «{f.Code}» مُكَرَّرٌ أَو فارِغٌ في «{d.Slug}»."));

            CheckArabic(v, f.Label, $"تَسمِيَة الحَقل «{f.Code}» في «{d.Slug}»");

            var fieldKindKnown = CredentialKinds.Contains(f.Kind);
            if (!fieldKindKnown)
            {
                v.Add(new("field_kind_out_of_vocabulary",
                    $"نَوعُ الحَقل «{f.Code}» في «{d.Slug}» = «{f.Kind}» خارِج المَعجَمِ " +
                    "المُلزِم — ونَوعٌ بِلا خِزانَةٍ مَبنِيَّةٍ لا يُخَزَّن."));
                continue;
            }

            // رابِطٌ بِلا سياجِ مُضيفين = إعادَةُ تَوجيهٍ مَفتوحَة.
            if (CredentialKinds.IsLink(f.Kind) && f.HostAllowlist.Count == 0)
                v.Add(new("host_allowlist_required_for_link",
                    $"الحَقل «{f.Code}» في «{d.Slug}» رابِطٌ بِلا قائِمَةِ مُضيفين."));

            // سياجُ مُضيفين عَلى سِرٍّ = خَلطُ طَبَقَتَين.
            if (CredentialKinds.IsSecretLike(f.Kind) && f.HostAllowlist.Count > 0)
                v.Add(new("host_allowlist_forbidden_for_secret",
                    $"الحَقل «{f.Code}» في «{d.Slug}» سِرٌّ يَحمِل قائِمَةَ مُضيفين."));
        }

        // ─── الوارِد — نُقطَةٌ غَيرُ مُتَحَقَّقٍ مِنها لا تُعلَن ───────
        if (d.Webhook is not null &&
            !d.Credential.Fields.Any(f => CredentialKinds.CanVerifyWebhook(f.Kind)))
            v.Add(new("webhook_requires_verifiable_kind",
                $"المُزَوِّد «{d.Slug}» يُعلِن webhook بِلا حَقلٍ يُتَحَقَّقُ بِه " +
                "(‏shared_secret أَو issued_secret)."));

        // ─── نَوعُ الرَبطِ لا يَنزِلُ تَحتَ أَعلى حُقولِه ──────────────
        if (bindingKindKnown)
        {
            var highest = d.HighestFieldKind;
            if (CredentialKinds.Contains(highest) &&
                CredentialKinds.Rank(d.Credential.Kind) < CredentialKinds.Rank(highest))
                v.Add(new("binding_kind_below_field_kind",
                    $"نَوعُ رَبط «{d.Slug}» = «{d.Credential.Kind}» أَدنى مِن أَعلى " +
                    $"حُقولِه «{highest}» — والشاشَةُ كانَت سَتُعامِلُه أَخَفَّ مِمّا يَحمِل."));

            // ‏`platform_key` يَصرِف مِن جَيبِنا: لا حَقلَ يَملَؤُه
            // مُستَأجِر، ولا سِرَّ في خِزانَتِه — سِرُّ المَنَصَّةِ
            // وَحدَه، ونُقطَتُه تُعلِن PlatformAdminGuard.
            if (d.Credential.Kind == CredentialKinds.PlatformKey &&
                d.Credential.Fields.Count > 0)
                v.Add(new("platform_key_requires_owner_grant",
                    $"المُزَوِّد «{d.Slug}» مِن نَوع platform_key ويُعلِن " +
                    $"{d.Credential.Fields.Count} حَقلاً — وسِرُّ المَنَصَّةِ لا يَملَؤُه مُستَأجِر."));
        }

        return v;
    }

    public static bool IsValid(ProviderDefinition d) => Validate(d).Count == 0;

    private static void CheckArabic(
        List<ProviderDefinitionViolation> v,
        IReadOnlyDictionary<string, string?> text, string whereAr)
    {
        if (!ProviderText.HasArabic(text))
            v.Add(new("label_missing_arabic",
                $"{whereAr}: العَرَبِيَّة مَفقودَة في حاوِيَة التَوطين."));
    }
}
