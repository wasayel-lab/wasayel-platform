using System.Text.RegularExpressions;

namespace ACommerce.Templates.Customer.Marketplace.I18n;

/// <summary>خَرق واحِد في قامُوس نُصوص. <c>Code</c> مِفتاح ثابِت
/// لِلاختِبارات واللوغ وتَصحيح الوَكيل، و<c>MessageAr</c> لِلمُراجِع
/// البَشَريّ. نَفس شَكل <c>ThemeDefinitionViolation</c> و
/// <c>RoleDefinitionViolation</c> — القالِب المَرجِعيّ في هذا
/// المُستَودَع.</summary>
public sealed record LocaleViolation(string Code, string MessageAr);

/// <summary>
/// <para><b>بَوّابَة قَوامِيس النُصوص</b> كَدَوالّ نَقِيَّة: لا قاعِدَة
/// بَيانات، لا وَقت، لا عَشوائيَّة. تَقرَأ المَدخَلات <b>الخام</b>
/// (بِتَكرارِها) لِأَنّ أَحَد الخُروق الثَلاثَة لا يَظهَر بَعدَ
/// الطَيّ.</para>
///
/// <para><b>والخُروق الثَمانِيَة، ولِكُلٍّ سَبَبُه</b> — سَبعَةٌ
/// تُفحَص لِكُلّ مِفتاح، والثامِن <b>مَشروطٌ بِسِياق
/// القِراءَة</b>:</para>
/// <list type="bullet">
///   <item><c>catalog_arabic_missing</c> — لا مِلَفّ عَرَبيّ أَصلاً؛
///   فَلا مَعجَم ولا سُقوط، وكُلّ مِفتاح يُعرَض خاماً.</item>
///   <item><c>key_no_arabic</c> — مِفتاح في المَعجَم بِعَرَبِيَّة
///   فارِغَة: «مُتَرجَم» في الجَداوِل و<b>فَراغ</b> على الشاشَة.</item>
///   <item><c>key_out_of_lexicon</c> — مِفتاح في لُغَةٍ لا وُجودَ لَه في
///   العَرَبِيَّة: تَرجُمَة لِنَصّ لا يُعرَض، أَو خَطَأ مَطبَعيّ في
///   المِفتاح لَن يَظهَر إلّا لِمُستَخدِم تِلكَ اللُغَة.</item>
///   <item><c>key_duplicate</c> — المِفتاح مَرَّتَين في المِلَفّ نَفسِه؛
///   الأَخيرَة تَفوز صامِتَةً، فَتَحريرُ الأُولى لا يُغَيِّر شَيئاً على
///   الشاشَة.</item>
///   <item><c>key_malformed</c> — خارِج اصطِلاح
///   <c>domain.feature.label</c>؛ والاصطِلاح يُفرَض هُنا أَو
///   يَنهار (القاعِدَة ٢).</item>
///   <item><c>value_empty</c> — قيمَة فارِغَة في لُغَةٍ غَير
///   العَرَبِيَّة: تَحجُب السُقوط وتَرسُم فَراغاً.</item>
///   <item><c>value_unsafe_markup</c> — قيمَة تَحمِل
///   <c>&lt;</c> أَو <c>&gt;</c> أَو <c>&amp;</c>.</item>
///   <item><c>value_unsafe_js</c> — قيمَةُ مِفتاحٍ يُقرَأ في سِياق
///   JavaScript وتَحتاج هُروباً. <b>مَشروطَةٌ بِالسِياق</b>، فَلَها
///   <see cref="ValidateJs"/> لا سَطرٌ في <see cref="Validate"/> —
///   والسَبَب في تَوثيقِها.</item>
/// </list>
///
/// <para><b>ولِماذا الأَخير بِالذات</b>: عُقَد النَصّ تُكتَب عَبر
/// <see cref="Microsoft.AspNetCore.Components.MarkupString"/> —
/// وهو <b>عَينُ ما يَفعَلُه Razor بِالحَرفِيَّة الَّتي حَلَّ
/// مَحَلَّها</b> (‏قِيسَ: الحَرفِيَّة تَخرُج خاماً والتَعبير يَخرُج
/// مُرَمَّزاً)، ولِذلك وَحدَه يَبقى المُخرَج مُطابِقاً بايتاً بِبايت.
/// والثَمَن أَنّ القيمَة تُكتَب بِلا تَرميز — فَيُمنَع المَحرَف الَّذي
/// يُغَيِّر بِنيَة المُستَند <b>عِندَ البَوّابَة</b>، لا عِندَ
/// الاستِعمال. نَفس طَبَقَة <c>value_unsafe_characters</c> في
/// بَوّابَة الثيم، ولِنَفس السَبَب: القيمَة تُبَثّ في مُستَند
/// يَراه كُلّ زائِر.</para>
/// </summary>
public static class LocaleValidator
{
    /// <summary><c>domain.feature.label</c> — ثَلاثَة مَقاطِع بِالضَبط،
    /// كُلٌّ بِحُروف صَغيرَة وأَرقام وشَرطَة سُفلِيَّة. الاصطِلاح
    /// مُقتَبَس مِن المُستَودَع السَلَف، حَيث حَمَلَ ‏193 مِفتاحاً بِلا
    /// انفِراط.</summary>
    private static readonly Regex KeyPattern =
        new("^[a-z][a-z0-9_]*(?:\\.[a-z][a-z0-9_]*){2}$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>مَحارِف تُغَيِّر بِنيَة المُستَند حينَ تُكتَب بِلا
    /// تَرميز.</summary>
    private static readonly char[] UnsafeMarkup = { '<', '>', '&' };

    /// <summary>القائِمَة فارِغَة تَعني قَوامِيسَ صالِحَة.</summary>
    public static IReadOnlyList<LocaleViolation> Validate(
        IReadOnlyDictionary<string, IReadOnlyList<LocaleEntry>> byLang)
    {
        var v = new List<LocaleViolation>();

        if (!byLang.TryGetValue(LocaleCatalog.Arabic, out var arabic) || arabic.Count == 0)
        {
            v.Add(new("catalog_arabic_missing",
                "لا قامُوس عَرَبيّ — العَرَبِيَّة إلزامِيَّة، وبِدونِها لا مَعجَم ولا سُقوط."));
            arabic = Array.Empty<LocaleEntry>();
        }

        var lexicon = arabic.Select(e => e.Key).ToHashSet(StringComparer.Ordinal);

        foreach (var lang in byLang.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            var entries = byLang[lang];
            var isArabic = lang == LocaleCatalog.Arabic;

            // ─── التَكرار — يُقاس على الخام قَبل أَيّ طَيّ ────────────
            foreach (var g in entries.GroupBy(e => e.Key, StringComparer.Ordinal)
                                     .Where(g => g.Count() > 1)
                                     .OrderBy(g => g.Key, StringComparer.Ordinal))
                v.Add(new("key_duplicate",
                    $"المِفتاح «{g.Key}» مُكَرَّر {g.Count()} مَرّات في «{lang}» — " +
                    "الأَخيرَة تَفوز صامِتَةً."));

            foreach (var e in entries)
            {
                if (!KeyPattern.IsMatch(e.Key))
                    v.Add(new("key_malformed",
                        $"المِفتاح «{e.Key}» في «{lang}» خارِج اصطِلاح domain.feature.label."));

                if (!isArabic && !lexicon.Contains(e.Key))
                    v.Add(new("key_out_of_lexicon",
                        $"المِفتاح «{e.Key}» في «{lang}» لَيسَ في المَعجَم العَرَبيّ — " +
                        "تَرجُمَةٌ لِنَصّ لا يُعرَض، أَو خَطَأ في المِفتاح."));

                if (string.IsNullOrWhiteSpace(e.Value))
                {
                    if (isArabic)
                        v.Add(new("key_no_arabic",
                            $"المِفتاح «{e.Key}» بِلا عَرَبِيَّة — قيمَتُه فارِغَة."));
                    else
                        v.Add(new("value_empty",
                            $"المِفتاح «{e.Key}» في «{lang}» بِقيمَة فارِغَة — " +
                            "تَحجُب السُقوط إلى العَرَبِيَّة وتَرسُم فَراغاً."));
                }

                if (e.Value.IndexOfAny(UnsafeMarkup) >= 0)
                    v.Add(new("value_unsafe_markup",
                        $"قيمَة «{e.Key}» في «{lang}» تَحمِل < أَو > أَو & — " +
                        "وعُقَد النَصّ تُكتَب بِلا تَرميز."));
            }
        }

        return v;
    }

    /// <summary>
    /// <para><b>الخَرقُ الثامِن — <c>value_unsafe_js</c></b>، وهو
    /// <b>مَشروطٌ بِالسِياق</b> فَلِذلكَ دالَّةٌ ثانِيَة لا سَطرٌ في
    /// <see cref="Validate"/>: المِفتاحُ الَّذي يُقرَأ بِـ
    /// <c>L.Js</c> يُكتَبُ داخِلَ حَرفِيَّةِ سِلسِلَةِ JavaScript،
    /// فَيَلزَمُه ما لا يَلزَمُ غَيرَه.</para>
    ///
    /// <para><b>ولِماذا لا يُعَمَّم على كُلّ المَعجَم</b> — قِياسٌ لا
    /// ذَوق: ‏33 مِفتاحاً في <c>ar.json</c> تَحمِل سَطراً جَديداً
    /// وسِتَّةٌ تَحمِل <c>"</c>، وكُلُّها <b>سَليمَةٌ في مَوضِعِها</b>
    /// (عُقَدُ نَصّ وخَصائِص). فَتَعميمُ الشَرط يَرُدُّ قيَماً صَحيحَة،
    /// وذاكَ بَوّابَةٌ تُعاقِب الصَواب.</para>
    ///
    /// <para><b>والمِعيارُ بايتيّ لا ذَوقيّ</b>: القيمَةُ تُرَدُّ إن
    /// لَم تَكُن <see cref="JsText.IsVerbatim"/> — أَي إن كانَ
    /// الهُروبُ سَيُبَدِّلُ فيها بايتاً. وذاكَ يَحرُس شَيئَين بِشَرطٍ
    /// واحِد: أَنّ المَعروضَ لا يَنكَسِر (الهُروبُ يَعمَل)، وأَنّ
    /// بايتات الصَفحَة لا تَتَبَدَّل (الهُروبُ لا يَحتاج أَن
    /// يَعمَل).</para>
    /// </summary>
    public static IReadOnlyList<LocaleViolation> ValidateJs(
        IReadOnlyCollection<string> jsKeys,
        IReadOnlyDictionary<string, IReadOnlyList<LocaleEntry>> byLang)
    {
        var keys = jsKeys.ToHashSet(StringComparer.Ordinal);
        var v = new List<LocaleViolation>();

        foreach (var lang in byLang.Keys.OrderBy(k => k, StringComparer.Ordinal))
            foreach (var e in byLang[lang])
            {
                if (!keys.Contains(e.Key) || JsText.IsVerbatim(e.Value)) continue;

                v.Add(new("value_unsafe_js",
                    $"قيمَة «{e.Key}» في «{lang}» تُقرَأ في سِياق JavaScript " +
                    "وتَحمِل مَحرَفاً يَحتاج هُروباً (‏' \" ` \\ $ < > & أَو سَطراً " +
                    "جَديداً أَو U+2028/9) — يُبَدِّل بايتات الصَفحَة."));
            }

        return v;
    }

    /// <summary>هَل تَجتاز القَوامِيس البَوّابَة؟</summary>
    public static bool IsValid(IReadOnlyDictionary<string, IReadOnlyList<LocaleEntry>> byLang)
        => Validate(byLang).Count == 0;

    /// <summary>البَوّابَة على القَوامِيس المَشحونَة فِعلاً.</summary>
    public static IReadOnlyList<LocaleViolation> ValidateShipped()
        => Validate(LocaleCatalog.All);
}
