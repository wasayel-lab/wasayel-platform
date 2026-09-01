namespace ACommerce.Kit.Compliance;

/// <summary>
/// <para><b>الفاحِص — دالَّةٌ نَقِيَّةٌ واحِدَةٌ فَوقَ كُلِّ
/// الالتِزامات.</b> لا فَرعَ لِمادَّةٍ، ولا اسمَ مادَّةٍ في سَطرٍ
/// واحِد: التَعريفُ يَقولُ ماذا يُطلَب، وهذا المِلَفُّ يَقولُ كَيفَ
/// يُقاسُ نَوعُ الشاهِدِ الواحِد. <b>وذلكَ هُوَ بُرهانُ أَنَّ
/// الالتِزاماتِ بَياناتٌ لا كود</b>: مادَّةٌ جَديدَةٌ مِلَفٌّ يُضاف،
/// ولا سَطرَ هُنا يُلمَس.</para>
///
/// <para><b>والحَدُّ الَّذي يَحفَظُ ذلكَ مَقيسٌ لا مَنصوح</b>: عَدَدُ
/// أَنواعِ الشاهِدِ في <c>switch</c> أَدناهُ يُساوي عَدَدَ
/// <see cref="EvidenceKinds.All"/> — يَقيسُه اختِبار. فَنَوعٌ يُضافُ
/// إلى المَعجَمِ ولا يُقاسُ هُنا يُمسَك، ونَوعٌ يُقاسُ ولا يوجَدُ في
/// المَعجَمِ يُمسَكُ كَذلك.</para>
///
/// <para><b>ولِماذا فَرعُ <c>default</c> يُنتِجُ نَقصاً لا
/// استِثناء</b>: مِلَفّاتُ الكاتالوجِ تَمُرُّ مِن المُصادِقِ عِندَ
/// الإقلاعِ فَلا يَبلُغُ هذا الفَرعَ نَوعٌ مَجهولٌ مِنها؛ لكِنَّ
/// <c>ParseDefinition</c> مَدخَلٌ عامٌّ يَقبَلُ نَصّاً لَم يُصادَق.
/// وأَداةُ امتِثالٍ تَرمي في وَجهِ صاحِبِ المَتجَرِ أَسوَأُ مِن
/// أَداةٍ تَقولُ لَه: هذا البَندُ لَم أَستَطِع فَحصَه.</para>
/// </summary>
public static class ComplianceInspector
{
    /// <summary>رَمزُ الرَفضِ حينَ يَبلُغُ الفاحِصَ نَوعُ شاهِدٍ خارِجَ
    /// المَعجَم. <b>لا يُخلَطُ بِرُموزِ الرَفضِ في المِلَفّات</b>: هذا
    /// عَطَبٌ في الأَداةِ لا نَقصٌ عِندَ المَفحوص.</summary>
    public const string UnknownKindRejection = "evidence_kind_unknown_to_inspector";

    /// <summary>يَفحَصُ كاتالوجَ المَنَصَّةِ كُلَّه بِمُستَوى
    /// اللَقطَة.</summary>
    public static ComplianceReport Inspect(ComplianceSubject subject) =>
        Inspect(ObligationCatalog.All, subject);

    /// <summary>
    /// <para>يَفحَصُ قائِمَةَ التِزاماتٍ بِعَينِها — <b>وهذا هُوَ
    /// المَدخَلُ الَّذي يَجعَلُ المِجَسَّ مُمكِناً</b>: التِزامٌ
    /// مَحقونٌ يَمُرُّ مِن هُنا كَما يَمُرُّ التِزامُ الكاتالوج،
    /// بِنَفسِ السَطرِ لا بِمَسارٍ لِلاختِبار.</para>
    ///
    /// <para><b>والتَصفِيَةُ بِالمُستَوى شَرطٌ لا تَحسين</b>: التِزامُ
    /// مَنَصَّةٍ يُفحَصُ عَلى أُصولِ المَنَصَّةِ مَرَّةً واحِدَة،
    /// والتِزامُ مُستَأجِرٍ يُفحَصُ مَرَّةً لِكُلِّ مُستَأجِر. وخَلطُهُما
    /// يَجعَلُ مُخالَفَةَ مَتجَرٍ واحِدٍ تَظهَرُ عَلى كُلِّ مَتجَر، أَو
    /// العَكس.</para>
    /// </summary>
    public static ComplianceReport Inspect(
        IReadOnlyList<ObligationDefinition> obligations, ComplianceSubject subject)
    {
        var results = new List<ObligationResult>();

        foreach (var o in obligations)
        {
            if (!string.Equals(o.Level, subject.Level, StringComparison.Ordinal)) continue;

            var evidence = new List<EvidenceResult>(o.Evidence.Count);
            foreach (var e in o.Evidence)
                evidence.Add(Evaluate(e, subject));

            results.Add(new ObligationResult(o, evidence));
        }

        return new ComplianceReport(subject, results);
    }

    /// <summary><b>المَوضِعُ الوَحيدُ الَّذي يُقَيِّمُ شاهِداً.</b>
    /// أَربَعَةُ فُروعٍ بِعَدَدِ <see cref="EvidenceKinds.All"/>،
    /// وخامِسٌ لِلمَجهول.</summary>
    private static EvidenceResult Evaluate(EvidenceRequirement e, ComplianceSubject subject)
    {
        switch (e.Kind)
        {
            case EvidenceKinds.TextPresent:
            {
                var value = subject.Text(e.Target);
                return string.IsNullOrWhiteSpace(value)
                    ? Missing(e, $"المِفتاح «{e.Target}» غائِبٌ عَن القامُوسِ أَو قيمَتُه فارِغَة.")
                    : Met(e, $"المِفتاح «{e.Target}» مَنشورٌ بِقيمَة.");
            }

            case EvidenceKinds.TextFilled:
            {
                var value = subject.Text(e.Target);
                if (string.IsNullOrWhiteSpace(value))
                    return Missing(e, $"المِفتاح «{e.Target}» غائِبٌ عَن القامُوسِ أَو قيمَتُه فارِغَة.");
                // النائِبُ يُرَدُّ كَما يُرَدُّ الغِياب — وهذا هُوَ
                // الفَرقُ الَّذي يَجعَلُ الفاحِصَ ذا قيمَة.
                return IsPlaceholder(value)
                    ? Missing(e,
                        $"المِفتاح «{e.Target}» نائِبٌ لَم يُملَأ بَعد: {Trim(value)} — " +
                        "والنائِبُ يُرَدُّ كَما يُرَدُّ الغِياب.")
                    : Met(e, $"المِفتاح «{e.Target}» مَملوءٌ بِقيمَةٍ حَقيقيَّة.");
            }

            case EvidenceKinds.TextFreeOf:
            {
                var value = subject.Text(e.Target);
                if (string.IsNullOrWhiteSpace(value))
                    return Missing(e, $"المِفتاح «{e.Target}» غائِبٌ عَن القامُوسِ أَو قيمَتُه فارِغَة.");

                var hit = e.ForbiddenPhrases.FirstOrDefault(
                    p => value.Contains(p, StringComparison.Ordinal));

                return hit is not null
                    ? Missing(e,
                        $"المِفتاح «{e.Target}» يَحوي العِبارَةَ المَمنوعَة «{hit}»: {Trim(value)}")
                    : Met(e, $"المِفتاح «{e.Target}» خالٍ مِن العِباراتِ المَمنوعَة.");
            }

            case EvidenceKinds.RouteReachable:
                return subject.HasRoute(e.Target)
                    ? Met(e, $"المَسار «{e.Target}» مُسَجَّلٌ في جَدوَلِ المَسارات.")
                    : Missing(e,
                        $"المَسار «{e.Target}» غَيرُ مُسَجَّل — وشاشَةٌ لا تُبلَغُ " +
                        "بِالنَقرِ غَيرُ مَوجودَة (القاعِدَة ١٢).");

            default:
                return new EvidenceResult(e, EvidenceVerdict.Missing, UnknownKindRejection,
                    $"نَوعُ الشاهِد «{e.Kind}» خارِجَ ما يَعرِفُه الفاحِص — " +
                    "والبَندُ لَم يُفحَص، ولا يُحسَبُ مُستَوفىً.");
        }
    }

    private static EvidenceResult Met(EvidenceRequirement e, string detail) =>
        new(e, EvidenceVerdict.Met, null, detail);

    private static EvidenceResult Missing(EvidenceRequirement e, string detail) =>
        new(e, EvidenceVerdict.Missing, e.RejectionCode, detail);

    /// <summary>
    /// <para><b>العَلامَةُ النائِبَة — <c>[[ … ]]</c> — مَنقولَةٌ
    /// بِقيمَتِها لا بِمَرجِعِ مَشروع.</b> ‏<c>LocaleCatalog.IsPlaceholder</c>
    /// يَحمِلُ نَفسَ القَرارِ حَرفاً، وهُوَ صاحِبُ الأَصل؛ وإحالَتُه مِن
    /// هُنا كانَت سَتَجُرُّ عُدَّةَ التَوطينِ كُلَّها إلى فاحِصٍ لا
    /// يَقرَأُ قامُوساً أَصلاً (يَقرَأُ لَقطَةً مُمَرَّرَة).</para>
    ///
    /// <para><b>والانجِرافُ مَحروسٌ بِفَحصٍ لا بِنِيَّة</b>: اختِبارٌ
    /// يُقابِلُ القَوسَينِ هُنا بِقَوسَي <c>LocaleCatalog</c> — فَتَغييرُ
    /// العَلامَةِ هُناكَ يُحمِرُّ هُنا.</para>
    /// </summary>
    internal static bool IsPlaceholder(string? value)
    {
        var v = (value ?? "").Trim();
        return v.StartsWith(PlaceholderOpen, StringComparison.Ordinal)
            && v.EndsWith(PlaceholderClose, StringComparison.Ordinal);
    }

    /// <summary>فاتِحَةُ القيمَةِ النائِبَة — تُطابِقُ
    /// <c>LocaleCatalog.PlaceholderOpen</c>، ويَقيسُ التَطابُقَ
    /// اختِبار.</summary>
    public const string PlaceholderOpen = "[[";

    /// <summary>خاتِمَتُها.</summary>
    public const string PlaceholderClose = "]]";

    private static string Trim(string value) =>
        value.Length <= 80 ? value : value[..80] + "…";
}
