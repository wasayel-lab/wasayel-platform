namespace ACommerce.Kit.Theme;

/// <summary>
/// <para><b>كاتالوج ثيمات المَنصَّة</b> — واحِد اليَوم:
/// <see cref="Default"/>. يُحَمَّل مَرَّةً عِندَ أَوَّل مَسّ ويَبقى.</para>
///
/// <para><b>ولِماذا كاتالوج لِعُنصُر واحِد</b>: لِأَنّ المَوضِع الَّذي
/// يُجيب «ما الثيم الأَساس؟» يَجِب أَن يَكون <b>واحِداً مُسَمّى</b> قَبل
/// أَن يَصير اثنَين — وهو أَيضاً المَوضِع الَّذي تَسأَلُه قاعِدَة عَدَم
/// الظِلّ (<see cref="IsPlatformSlug"/>) فَلا يُسَمّي مُستَأجِر ثيمَه
/// <c>default</c> فَيُغَيِّر مَعنى الاسم عَلى المَنصَّة كُلِّها.</para>
///
/// <para><b>وقيمُه هي قِيَم اليَوم حَرفِيّاً</b> — مَنقولَة مِن
/// التَصريحَة الغالِبَة لِكُلّ مُتَغَيِّر في أَوراق الأَنماط بِتَرتيب
/// تَحميلِها (‏widgets ← app ← premium)، لا مُختارَة ولا «مُحَسَّنَة».
/// ولِذلك التَكافُؤ الصِفريّ <b>مَقيس لا مَدَّعى</b>:
/// <c>ThemeZeroEquivalenceTests</c> يُقارِن كُلّ قيمَة مَبثوثَة
/// بِالحَرفِيَّة الَّتي حَلَّت مَحَلَّها.</para>
/// </summary>
public static class ThemeCatalog
{
    /// <summary>سلاج الثيم الأَساس. مَحجوز عَلى المُستَأجِرين.</summary>
    public const string DefaultSlug = "default";

    private static readonly Lazy<ThemeDefinition> LazyDefinition =
        new(ThemeDefinitionLoader.LoadEmbeddedDefault, isThreadSafe: true);

    private static readonly Lazy<EffectiveTheme> LazyEffective =
        new(() => EffectiveTheme.FromComplete(LazyDefinition.Value), isThreadSafe: true);

    /// <summary>تَعريف الثيم الافتِراضيّ كَما قُرِئ مِن المَورِد.</summary>
    public static ThemeDefinition Definition => LazyDefinition.Value;

    /// <summary><b>الثيم الفَعّال الأَساس</b> — جَواب كُلّ مُستَأجِر
    /// لَم يُؤَلِّف ثيماً، وجَواب كُلّ سِياق بِلا مُستَأجِر (لَوحَة
    /// المَنصَّة، الاستوديو، الاختِبارات).</summary>
    public static EffectiveTheme Default => LazyEffective.Value;

    /// <summary>هَل هذا السلاج مَأخوذ عَلى مُستَوى المَنصَّة؟</summary>
    public static bool IsPlatformSlug(string slug) =>
        string.Equals(slug, DefaultSlug, StringComparison.Ordinal);
}
