using System.Text.RegularExpressions;

namespace ACommerce.Kit.Theme;

/// <summary>خَرق واحِد في تَعريف ثيم. <c>Code</c> مِفتاح ثابِت
/// لِلاختِبارات واللوغ وتَصحيح الوَكيل، و<c>MessageAr</c> لِلمُراجِع
/// البَشَريّ. نَفس شَكل <c>RoleDefinitionViolation</c> — القالِب
/// المَرجِعيّ.</summary>
public sealed record ThemeDefinitionViolation(string Code, string MessageAr);

/// <summary>
/// <para><b>بَوّابَة تَعريفات الثيم</b> كَدَوالّ نَقِيَّة فَوق
/// <see cref="ThemeDefinition"/>: لا قاعِدَة بَيانات، لا وَقت، لا
/// عَشوائيَّة. نَفس نَمَط <c>RoleDefinitionValidator</c> حَرفاً.</para>
///
/// <para><b>ولِهذه البَوّابَة عِبء لا تَحمِلُه بَوّابَة الأَدوار</b>:
/// قيمَة الرَمز <b>تُبَثّ داخِل وَسم <c>&lt;style&gt;</c></b> في صَفحَة
/// يَراها كُلّ زائِر. تَعريف دَور فاسِد يُشَوِّه قائِمَة؛ وقيمَة ثيم
/// غَير مَفحوصَة مِثل <c>red;}body{display:none</c> تَكتُب CSS
/// عَشوائيّاً لِكُلّ زائِر. لِذلك الدِفاع <b>ثَلاث طَبَقات
/// مُستَقِلَّة</b>:</para>
/// <list type="number">
///   <item><b>المِفتاح مِن المَعجَم</b> — واسم المُتَغَيِّر المَبثوث
///   يُؤخَذ مِن <see cref="ThemeTokenCatalog"/> لا مِن الوَثيقَة، فَلا
///   يَكتُب مُستَأجِر اسماً أَصلاً.</item>
///   <item><b>مَنع المَحارِف الخَطِرَة</b> صَراحَةً
///   (<c>value_unsafe_characters</c>) — قَبل أَيّ نَحو، فَلا يَعتَمِد
///   الأَمان عَلى دِقَّة تَعبير نَمَطيّ.</item>
///   <item><b>نَحو مُثَبَّت بِـ<c>^…$</c> لِكُلّ نَوع</b> — لَون أَو
///   طول أَو عَدَد أَو وَزن، بِأَرقام ووَحَدات مَعروفَة فَقَط.</item>
/// </list>
/// <para>واحِدَة كانَت تَكفي غالِباً؛ والثَلاث تَكفي حينَ تُخطِئ
/// واحِدَة.</para>
/// </summary>
public static class ThemeDefinitionValidator
{
    /// <summary>نَفس نَمَط سلاج الأَدوار حَرفاً.</summary>
    private static readonly Regex SlugPattern =
        new("^[a-z][a-z0-9_]*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary><c>#RGB</c> / <c>#RRGGBB</c> / <c>#RRGGBBAA</c>.</summary>
    private static readonly Regex HexPattern =
        new("^#(?:[0-9a-fA-F]{3}|[0-9a-fA-F]{6}|[0-9a-fA-F]{8})$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary><c>rgb(r,g,b)</c> / <c>rgba(r,g,b,a)</c> بِأَرقام
    /// فَقَط. مَسموحَة لِأَنّ الحُدود اليَوم مَكتوبَة هكذا
    /// (<c>rgba(17,24,39,.07)</c>) — وتَحويلُها إلى HEX ثُمانيّ كانَ
    /// سَيُغَيِّر القيمَة فِعلاً (‏.07×255 = 17.85 لا عَدَد صَحيح)
    /// فَيَكسِر التَكافُؤ الصِفريّ مِن أَجل نَحو أَنظَف. القيمَة أَولى.</summary>
    private static readonly Regex RgbPattern =
        new(@"^rgba?\(\s*\d{1,3}\s*,\s*\d{1,3}\s*,\s*\d{1,3}\s*(?:,\s*(?:0|1|0?\.\d{1,4})\s*)?\)$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>طول CSS بِوَحدَة مَعروفَة، أَو <c>0</c> مُجَرَّداً.</summary>
    private static readonly Regex LengthPattern =
        new(@"^(?:0|\d{1,4}(?:\.\d{1,4})?(?:px|rem|em|%))$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>عَدَد بِلا وَحدَة.</summary>
    private static readonly Regex NumberPattern =
        new(@"^\d{1,3}(?:\.\d{1,4})?$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>وَزن خَطّ: ‏100…900.</summary>
    private static readonly Regex WeightPattern =
        new("^[1-9]00$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>مَحارِف لا تَظهَر في أَيّ قيمَة رَمز مَشروعَة، وكُلّ
    /// واحِد مِنها خُروج مُحتَمَل مِن التَصريحَة إلى القاعِدَة أَو مِن
    /// الوَسم إلى المُستَند.</summary>
    private static readonly char[] Unsafe =
        { ';', '{', '}', '<', '>', '&', '\\', '"', '\'', '@', '`', '\n', '\r', '\t', '\0' };

    /// <summary>القائِمَة فارِغَة تَعني تَعريفاً صالِحاً.</summary>
    public static IReadOnlyList<ThemeDefinitionViolation> Validate(ThemeDefinition d)
    {
        var v = new List<ThemeDefinitionViolation>();

        // ─── الهُوِيَّة ────────────────────────────────────────────────
        if (string.IsNullOrWhiteSpace(d.Slug))
            v.Add(new("slug_empty", "الثيم بِلا slug."));
        else if (!SlugPattern.IsMatch(d.Slug))
            v.Add(new("slug_pattern",
                $"الـ slug «{d.Slug}» خارِج النَّمَط ^[a-z][a-z0-9_]*$."));

        if (string.IsNullOrWhiteSpace(d.Label.Ar))
            v.Add(new("localized_arabic_missing",
                $"تَسمِيَة الثيم «{d.Slug}»: العَرَبيَّة مَفقودَة في حاوِيَة التَّوطين."));

        // ─── الرُموز ─────────────────────────────────────────────────
        if (d.Tokens.Count == 0)
            v.Add(new("tokens_empty",
                $"الثيم «{d.Slug}» بِلا رَمز واحِد — لا شَيء يُبَثّ."));

        foreach (var (key, raw) in d.Tokens.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            var token = ThemeTokenCatalog.Find(key);
            if (token is null)
            {
                v.Add(new("token_key_out_of_vocabulary",
                    $"المِفتاح «{key}» في الثيم «{d.Slug}» خارِج مَعجَم ThemeTokenCatalog."));
                continue;
            }

            if (string.IsNullOrWhiteSpace(raw))
            {
                v.Add(new("token_value_empty",
                    $"الرَمز «{key}» في الثيم «{d.Slug}» بِلا قيمَة."));
                continue;
            }

            // الطَبَقَة الثانِيَة — قَبل أَيّ نَحو.
            if (raw.IndexOfAny(Unsafe) >= 0 || raw.Contains("/*", StringComparison.Ordinal))
            {
                v.Add(new("value_unsafe_characters",
                    $"قيمَة الرَمز «{key}» في الثيم «{d.Slug}» تَحمِل مَحرَفاً " +
                    "لا يَرِد في قيمَة مَشروعَة — مَرفوضَة قَبل فَحص النَّحو."));
                continue;
            }

            var value = raw.Trim();
            switch (token.Kind)
            {
                case ThemeTokenKind.Color when !HexPattern.IsMatch(value) && !RgbPattern.IsMatch(value):
                    v.Add(new("color_malformed",
                        $"قيمَة اللَون «{value}» لِلرَمز «{key}» ليسَت HEX صالِحاً " +
                        "ولا rgb()/rgba() بِأَرقام."));
                    break;

                case ThemeTokenKind.Length when !LengthPattern.IsMatch(value):
                    v.Add(new("length_malformed",
                        $"قيمَة الطول «{value}» لِلرَمز «{key}» شاذَّة — " +
                        "المُتَوَقَّع عَدَد بِـpx أَو rem أَو em أَو % أَو 0."));
                    break;

                case ThemeTokenKind.Number when !NumberPattern.IsMatch(value):
                    v.Add(new("number_malformed",
                        $"قيمَة العَدَد «{value}» لِلرَمز «{key}» شاذَّة — " +
                        "المُتَوَقَّع عَدَد بِلا وَحدَة."));
                    break;

                case ThemeTokenKind.Weight when !WeightPattern.IsMatch(value):
                    v.Add(new("weight_out_of_range",
                        $"وَزن الخَطّ «{value}» لِلرَمز «{key}» خارِج 100…900 " +
                        "بِمَضاعَفات المِئَة."));
                    break;
            }
        }

        return v;
    }

    /// <summary>هَل يَجتاز البَوّابَة؟</summary>
    public static bool IsValid(ThemeDefinition d) => Validate(d).Count == 0;

    /// <summary>
    /// <para><b>بَوّابَة الثيم الافتِراضيّ</b> — كُلّ ما في
    /// <see cref="Validate"/>، <b>وزِيادَةٌ واحِدَة</b>: الاكتِمال. ثيم
    /// المَنصَّة هو الأَساس الَّذي يَسقُط عَلَيه كُلّ مَن لا ثيم لَه،
    /// فَنَقصُ رَمز فيه لَيسَ «جُزئيَّة» بَل مُتَغَيِّر <b>لا يُبَثّ
    /// أَصلاً</b> بَينَما أَوراق الأَنماط تَقرَؤُه — أَي خاصِّيَّة
    /// بِلا قيمَة عَلى الصَفحَة.</para>
    ///
    /// <para>ولِذلك هي دالَّة مُنفَصِلَة لا عَلَم: تَعريفات المُستَأجِر
    /// <b>يَجِب</b> أَن تَكون جُزئيَّة، ولَو كانَ الفَحص في
    /// <see cref="Validate"/> لَرَفَضَ كُلّ ثيم يُغَيِّر لَوناً واحِداً.
    /// نَفس مُبَرِّر فَصل <c>ValidateTenantDefinition</c> في
    /// الأَدوار.</para>
    /// </summary>
    public static IReadOnlyList<ThemeDefinitionViolation> ValidateDefault(ThemeDefinition d)
    {
        var v = new List<ThemeDefinitionViolation>(Validate(d));

        foreach (var token in ThemeTokenCatalog.All)
            if (!d.Tokens.ContainsKey(token.Key))
                v.Add(new("default_theme_incomplete",
                    $"الثيم الافتِراضيّ «{d.Slug}» لا يُعَرِّف الرَمز «{token.Key}»."));

        return v;
    }

    /// <summary>
    /// <para><b>بَوّابَة ثيم يُؤَلِّفُه مُستَأجِر</b> — كُلّ ما في
    /// <see cref="Validate"/>، <b>وزِيادَةٌ واحِدَة</b>: أَن لا يُصادِم
    /// سلاجُه سلاج كاتالوج المَنصَّة. الخَرق هُنا لَيسَ «الاسم شاذّ»
    /// بَل «الاسم مَأخوذ عَلى مُستَوى المَنصَّة» — رَمز مُتَمَيِّز
    /// لِرِسالَة مُتَمَيِّزَة، فَيُعيد الوَكيل التَسمِيَة بَدَل أَن يُعيد
    /// المُحاوَلَة. نَفس عَقد الأَدوار.</para>
    /// </summary>
    public static IReadOnlyList<ThemeDefinitionViolation> ValidateTenantDefinition(ThemeDefinition d)
    {
        var v = new List<ThemeDefinitionViolation>(Validate(d));

        if (!string.IsNullOrWhiteSpace(d.Slug) && ThemeCatalog.IsPlatformSlug(d.Slug))
            v.Add(new("slug_shadows_platform_catalog",
                $"الـ slug «{d.Slug}» مَأخوذ في كاتالوج المَنصَّة — " +
                "ثيمات المُستَأجِر تُضاف فَوقَه ولا تُظَلِّلُه. اِختَر اسماً آخَر."));

        return v;
    }

    /// <summary>هَل يَجتاز بَوّابَة المُستَأجِر؟</summary>
    public static bool IsValidTenantDefinition(ThemeDefinition d) =>
        ValidateTenantDefinition(d).Count == 0;
}
