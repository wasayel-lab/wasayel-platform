using System.Text.RegularExpressions;

namespace ACommerce.Kit.Compliance;

/// <summary>خَرقٌ واحِدٌ في تَعريفِ التِزام. <c>Code</c> مِفتاحٌ ثابِتٌ
/// لِلاختِبارات واللوغ، و<c>MessageAr</c> لِلمُراجِعِ البَشَريّ. نَفسُ
/// شَكلِ <c>RoleDefinitionViolation</c> و<c>ProviderDefinitionViolation</c>
/// و<c>DealCancelViolation</c> حَرفاً (القاعِدَة ٤).</summary>
public sealed record ObligationDefinitionViolation(string Code, string MessageAr);

/// <summary>
/// <para><b>بَوّابَةُ تَعريفاتِ الالتِزامات — ثَمانِيَةَ عَشَرَ رَمزَ
/// خَرق، ولِكُلٍّ اختِبارٌ موجِبٌ وسالِب</b> (القاعِدَة ٤).</para>
///
/// <para><b>وهي مَفروضَةٌ لا مُتاحَة</b>: <see cref="ObligationCatalog"/>
/// يُمَرِّرُ كُلَّ تَعريفٍ مُحَمَّلٍ مِن هُنا ويَرمي عِندَ أَيِّ خَرق —
/// فَتَعريفٌ فاسِدٌ <b>يُفشِلُ الإقلاعَ بِرَمزِه</b> ولا يَصِلُ لَوحَةً
/// صامِتاً. ولَوحَةُ امتِثالٍ تَعرِضُ التِزاماً مُشَوَّهاً أَسوَأُ مِن
/// لَوحَةٍ لا تَعمَل.</para>
///
/// <para><b>ما لا تَفحَصُه عَمداً</b>: صِحَّةَ الاقتِباسِ نَفسِه مُقابِلَ
/// المَصدَر. لا سَبيلَ إلى ذلكَ بِلا شَبَكَة، ودَعوى الفَحصِ أَسوَأُ مِن
/// غِيابِه. الاقتِباسُ يُقرَأُ في المُراجَعَةِ البَشَرِيَّة، والمِلَفُّ
/// يَحمِلُ رابِطَه لِيُقارَن.</para>
/// </summary>
public static class ObligationDefinitionValidator
{
    /// <summary>نَمَطُ المُعَرِّفاتِ والرُموز: ‏ASCII صَغيرٌ يَبدَأُ
    /// بِحَرف، ثُمَّ حُروفٌ أَو أَرقامٌ أَو شَرطَةٌ سُفلِيَّة. نَفسُ
    /// نَمَطِ <c>RoleDefinitionValidator</c>.</summary>
    private static readonly Regex IdPattern =
        new("^[a-z][a-z0-9_]*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>الرُموزُ الثَمانِيَةَ عَشَرَ — مُعلَنَةً لِيُقاسَ أَنَّ
    /// لِكُلٍّ اختِبارَين، لا لِتُقرَأَ في تَعليق.</summary>
    public static readonly IReadOnlyList<string> Codes = new[]
    {
        "id_empty",
        "id_pattern",
        "level_out_of_vocabulary",
        "label_missing_arabic",
        "source_incomplete",
        "evidence_empty",
        "evidence_code_empty",
        "evidence_code_duplicate",
        "evidence_kind_out_of_vocabulary",
        "evidence_target_empty",
        "evidence_label_missing_arabic",
        "rejection_code_empty",
        "rejection_code_pattern",
        "rejection_code_duplicate",
        "forbidden_phrases_required",
        "forbidden_phrases_forbidden",
        "route_target_not_absolute",
        "not_checkable_reason_missing_arabic",
    };

    private static readonly HashSet<string> CodeSet = new(Codes, StringComparer.Ordinal);

    public static bool ContainsCode(string code) => CodeSet.Contains(code);

    /// <summary>القائِمَةُ الفارِغَةُ تَعني تَعريفاً صالِحاً.</summary>
    public static IReadOnlyList<ObligationDefinitionViolation> Validate(ObligationDefinition d)
    {
        var v = new List<ObligationDefinitionViolation>();

        // ─── الهُوِيَّة ────────────────────────────────────────────────
        if (string.IsNullOrWhiteSpace(d.Id))
            v.Add(new("id_empty", "الالتِزامُ بِلا مُعَرِّف."));
        else if (!IdPattern.IsMatch(d.Id))
            v.Add(new("id_pattern",
                $"المُعَرِّف «{d.Id}» خارِج النَمَط ^[a-z][a-z0-9_]*$."));

        if (!ComplianceLevels.Contains(d.Level))
            v.Add(new("level_out_of_vocabulary",
                $"المُستَوى «{d.Level}» في الالتِزام «{d.Id}» خارِج مَعجَم " +
                $"ComplianceLevels. المَعجَم: {string.Join("، ", ComplianceLevels.All)}."));

        if (!ComplianceText.HasArabic(d.Label))
            v.Add(new("label_missing_arabic",
                $"تَسمِيَةُ الالتِزام «{d.Id}»: العَرَبِيَّةُ مَفقودَةٌ في حاوِيَةِ التَوطين."));

        // ─── المَصدَر — القاعِدَة ١٦ مَفروضَةً لا مَنصوحاً بِها ────────
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(d.Source.Authority)) missing.Add("الجِهَة");
        if (string.IsNullOrWhiteSpace(d.Source.Reference)) missing.Add("المَرجِع");
        if (string.IsNullOrWhiteSpace(d.Source.QuotedAr)) missing.Add("الاقتِباس");
        if (missing.Count > 0)
            v.Add(new("source_incomplete",
                $"مَصدَرُ الالتِزام «{d.Id}» ناقِص: {string.Join("، ", missing)}. " +
                "ولا يُخترَعُ نَصٌّ نِظامِيّ (القاعِدَة ١٦)."));

        // ─── الشُهود ─────────────────────────────────────────────────
        if (d.Evidence.Count == 0)
            v.Add(new("evidence_empty",
                $"الالتِزام «{d.Id}» بِلا شاهِدٍ واحِد — والتِزامٌ بِلا شاهِدٍ " +
                "بَندٌ يُقرَأُ لا بَندٌ يُفحَص."));

        var seenCode = new HashSet<string>(StringComparer.Ordinal);
        var seenRejection = new HashSet<string>(StringComparer.Ordinal);

        foreach (var e in d.Evidence)
        {
            if (string.IsNullOrWhiteSpace(e.Code))
                v.Add(new("evidence_code_empty", $"شاهِدٌ بِلا رَمزٍ في الالتِزام «{d.Id}»."));
            else if (!seenCode.Add(e.Code))
                v.Add(new("evidence_code_duplicate",
                    $"رَمزُ الشاهِد «{e.Code}» مُكَرَّرٌ في الالتِزام «{d.Id}»."));

            if (!EvidenceKinds.Contains(e.Kind))
                v.Add(new("evidence_kind_out_of_vocabulary",
                    $"نَوعُ الشاهِد «{e.Kind}» في «{d.Id}/{e.Code}» خارِج مَعجَم " +
                    $"EvidenceKinds. المَعجَم: {string.Join("، ", EvidenceKinds.All)}."));

            if (string.IsNullOrWhiteSpace(e.Target))
                v.Add(new("evidence_target_empty",
                    $"الشاهِد «{d.Id}/{e.Code}» بِلا هَدَف."));
            else if (e.Kind == EvidenceKinds.RouteReachable && e.Target[0] != '/')
                v.Add(new("route_target_not_absolute",
                    $"هَدَفُ المَسار «{e.Target}» في «{d.Id}/{e.Code}» لا يَبدَأُ بِشَرطَةٍ مائِلَة — " +
                    "وجَدوَلُ المَساراتِ يُقارَنُ بِأَنماطٍ مُطلَقَة."));

            if (!ComplianceText.HasArabic(e.Label))
                v.Add(new("evidence_label_missing_arabic",
                    $"تَسمِيَةُ الشاهِد «{d.Id}/{e.Code}»: العَرَبِيَّةُ مَفقودَة."));

            // رَمزُ الرَفض — وهُوَ نِصفُ قيمَةِ المِلَفّ: بِه يُقالُ
            // «ماذا سَقَطَ» بِلا قِراءَةِ رِسالَةٍ عَرَبِيَّةٍ تُحَرَّر.
            if (string.IsNullOrWhiteSpace(e.RejectionCode))
                v.Add(new("rejection_code_empty",
                    $"الشاهِد «{d.Id}/{e.Code}» بِلا رَمزِ رَفض."));
            else
            {
                if (!IdPattern.IsMatch(e.RejectionCode))
                    v.Add(new("rejection_code_pattern",
                        $"رَمزُ الرَفض «{e.RejectionCode}» خارِج النَمَط ^[a-z][a-z0-9_]*$."));
                if (!seenRejection.Add(e.RejectionCode))
                    v.Add(new("rejection_code_duplicate",
                        $"رَمزُ الرَفض «{e.RejectionCode}» مُكَرَّرٌ في الالتِزام «{d.Id}» — " +
                        "ورَمزٌ يَدُلُّ عَلى شاهِدَينِ لا يَدُلُّ عَلى شَيء."));
            }

            // العِباراتُ المَمنوعَة: إلزامِيَّةٌ لِنَوعِها، مَمنوعَةٌ عَلى
            // غَيرِه. والمَنعُ لَيسَ تَزَمُّتاً: قائِمَةٌ تُكتَبُ ولا
            // تُقرَأُ تَبدو حارِسَةً وهي زينَة.
            if (e.Kind == EvidenceKinds.TextFreeOf)
            {
                if (e.ForbiddenPhrases.Count == 0)
                    v.Add(new("forbidden_phrases_required",
                        $"الشاهِد «{d.Id}/{e.Code}» مِن نَوع {EvidenceKinds.TextFreeOf} " +
                        "بِلا عِبارَةٍ مَمنوعَةٍ واحِدَة — فَلا شَيءَ يَمنَعُه."));
                else if (e.ForbiddenPhrases.Any(string.IsNullOrWhiteSpace))
                    v.Add(new("forbidden_phrases_required",
                        $"الشاهِد «{d.Id}/{e.Code}» فيه عِبارَةٌ مَمنوعَةٌ فارِغَة — " +
                        "والفارِغَةُ تُطابِقُ كُلَّ نَصّ."));
            }
            else if (e.ForbiddenPhrases.Count > 0)
                v.Add(new("forbidden_phrases_forbidden",
                    $"الشاهِد «{d.Id}/{e.Code}» مِن نَوع «{e.Kind}» يَحمِلُ عِباراتٍ " +
                    $"مَمنوعَةً لا تُقرَأ — و{EvidenceKinds.TextFreeOf} وَحدَه يَقرَؤُها."));
        }

        // ─── ما لا يُفحَص — يُعلَنُ بِسَبَبِه أَو لا يُعلَن ────────────
        foreach (var n in d.NotCheckable)
            if (!ComplianceText.HasArabic(n.Reason))
                v.Add(new("not_checkable_reason_missing_arabic",
                    $"البَند غَير المَفحوص «{d.Id}/{n.Code}» بِلا سَبَبٍ عَرَبِيّ — " +
                    "وبَندٌ يُستَثنى بِلا سَبَبٍ استِثناءٌ لا يُراجَع."));

        return v;
    }

    /// <summary>هَل يَجتازُ البَوّابَة؟</summary>
    public static bool IsValid(ObligationDefinition d) => Validate(d).Count == 0;
}
