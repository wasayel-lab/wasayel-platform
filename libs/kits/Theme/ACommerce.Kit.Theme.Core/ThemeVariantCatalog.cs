namespace ACommerce.Kit.Theme;

/// <summary>
/// <para><b>قيمَة واحِدَة في مُتَغايِر</b>: اسمُها كَما يُكتَب في مِلَفّ
/// الثيم، والصَنف المُعَدِّل الَّذي تُنتِجُه في الوَسم.</para>
///
/// <para><b>والقيمَة الافتِراضيَّة مُعَدِّلُها فارِغ — بِالبِناء لا
/// بِالاتِّفاق</b>. هذا هو مَوضِع التَكافُؤ الصِفريّ كُلّه: لَو كانَ
/// لِلافتِراضيّ صَنف خاصّ بِه (‏<c>…--list</c> مَثَلاً) لَتَغَيَّرَ وَسم
/// كُلّ صَفحَة في المَنصَّة يَوم دُخول هذه المَوجَة، ولَصارَت «ما زالَ
/// الشَكل كَما كان» دَعوى بَصَرِيَّة بَدَل أَن تَكون مُقارَنَةً
/// بايتِيَّة.</para>
/// </summary>
/// <param name="Value">الاسم في <c>theme.json</c> — <c>grid</c>.</param>
/// <param name="CssModifier">الصَنف المُضاف، أَو فارِغ لِلافتِراضيّ.
/// <b>مَكتوب صَراحَةً لا مُشتَقّ</b> — بِنَفس مُبَرِّر
/// <see cref="ThemeToken.CssVariable"/>: اشتِقاقُه مِن الاسم كانَ
/// سَيَجعَل إعادَة تَسمِيَة قيمَة تُغَيِّر صَنفاً تَقرَؤُه وَرَقَة
/// أَنماط، صامِتاً.</param>
public sealed record ThemeVariant(string Value, string CssModifier)
{
    private readonly string _suffix =
        CssModifier.Length == 0 ? string.Empty : " " + CssModifier;

    /// <summary>اللاحِقَة كَما تُلصَق داخِل <c>class="…"</c> بَعد الصَنف
    /// الأَساس: فارِغَة لِلافتِراضيّ، أَو مَسافَة ثُمَّ المُعَدِّل.
    /// مَبنِيَّة مَرَّةً — فَنَفس السِلسِلَة في كُلّ طَلَب.</summary>
    public string ClassSuffix => _suffix;
}

/// <summary>
/// <para><b>فَتحَة مُتَغايِر واحِدَة</b> — مُكَوِّن مَرئيّ لَه صَنف أَساس
/// ثابِت، وقيمَة افتِراضِيَّة، وقائِمَة قِيَم <b>مَعدودَة</b>.</para>
///
/// <para><b>ولِماذا قِيَم مَعدودَة لا سِلسِلَة حُرَّة</b>: قيمَة
/// المُتَغايِر تَنتَهي داخِل <c>class="…"</c> في صَفحَة يَراها كُلّ
/// زائِر. لَو مَرَّ نَصّ المُستَأجِر إلى هُناك لَكانَ الدِفاع نَحواً
/// يُصادِق أَسماء الأَصناف؛ وبِالإحالَة إلى قامُوس مُغلَق <b>لا يَصِل
/// الصَفحَة إلّا نَصّ مَكتوب في هذا المِلَفّ</b> — نَفس حُجَّة
/// <see cref="ThemeTokenCatalog"/> في أَسماء المُتَغَيِّرات.</para>
/// </summary>
public sealed record ThemeVariantSlot(
    string Key,
    string BaseClass,
    string DefaultValue,
    IReadOnlyList<ThemeVariant> Values)
{
    private readonly Dictionary<string, ThemeVariant> _byValue =
        Values.ToDictionary(x => x.Value, StringComparer.Ordinal);

    public ThemeVariant? Find(string value) =>
        _byValue.TryGetValue(value, out var x) ? x : null;

    public bool Contains(string value) => _byValue.ContainsKey(value);

    /// <summary>القيمَة الافتِراضيَّة كَكائِن. غِيابُها خَطَأ بِناء
    /// كاتالوج لا خَطَأ بَيانات — ولِذلك يَرمي.</summary>
    public ThemeVariant Default =>
        Find(DefaultValue) ?? throw new InvalidOperationException(
            $"الفَتحَة «{Key}» تُعلِن قيمَةً افتِراضيَّة «{DefaultValue}» ليسَت في قِيَمِها.");
}

/// <summary>
/// <para><b>مَعجَم مُتَغايِرات المُكَوِّنات — مُغلَق</b>. ثَلاث فَتَحات
/// اليَوم، كُلُّها مُختارَة بِشَرط واحِد: أَن يَكون الفَرق بَينَ قِيَمِها
/// <b>مَرئيّاً في لَقطَة شاشَة</b> لِمَن لا يَعرِف الشيفرَة. مُتَغايِر
/// يَنقُل حافَّةً بِبِكسِلَين لَيسَ مُتَغايِراً، هو إعداد.</para>
///
/// <para><b>ولِماذا هذه الثَلاثَة بِعَينِها</b>: هي الثَلاثَة الَّتي
/// تَملَأ الشاشَة الأُولى لِزائِر المَتجَر. بَوّابَة المَتجَر
/// (‏<c>/{slug}</c>) لَيسَ فيها إلّا الترويسَة وبِطاقات الأَدوار،
/// وصَفحَة الاستِكشاف لَيسَ فيها إلّا الترويسَة وبِطاقات الإعلانات —
/// فَتَبديل الثَلاثَة يُبَدِّل ما يَراه الزائِر في أَوَّل ثانِيَة، لا
/// تَفصيلاً في صَفحَة داخِلِيَّة.</para>
///
/// <para><b>وما لَيسَ فَتحَةً عَمداً</b>: أَيّ مُكَوِّن لا يَظهَر في
/// الصَفَحات السِتّ المُوصَّفَة في
/// <c>tests/characterization/appearance/baseline</c>. الفَتحَة الَّتي لا
/// تَدخُل لَقطَة الأَساس تَدخُل بِلا تَكافُؤ صِفريّ مَقيس — أَي
/// بِدَعوى.</para>
/// </summary>
public static class ThemeVariantCatalog
{
    /// <summary>بِطاقات الأَدوار في بَوّابَة المَتجَر.</summary>
    public const string PortalRoleCards = "portal.roleCards";

    /// <summary>بِطاقَة الإعلان في الشَبَكَة والشَريط الأُفُقيّ.</summary>
    public const string ListingCard = "listing.card";

    /// <summary>غِلاف شَريط الترويسَة العُلويّ.</summary>
    public const string HeaderBar = "header.bar";

    /// <summary><b>المَعجَم بِتَرتيبِه</b> — تَرتيب العَرض في سَطح
    /// الإدارَة وتَرتيب الفَحص في المُصادَقَة.</summary>
    public static readonly IReadOnlyList<ThemeVariantSlot> All = new ThemeVariantSlot[]
    {
        // ─── بِطاقَة الدَور في البَوّابَة ───────────────────────────────
        // الافتِراضيّ عَمود واحِد بِبِطاقات عَريضَة — وهو ما تُصَيِّرُه
        // الصَفحَة اليَوم حَرفاً.
        new(PortalRoleCards, "acm-role-landing-cards", "list", new ThemeVariant[]
        {
            new("list",    ""),                                  // عَمود — اليَوم
            new("grid",    "acm-role-landing-cards--grid"),       // شَبَكَة عَمودَين
            new("compact", "acm-role-landing-cards--compact"),    // صُفوف ضَيِّقَة بِلا وَصف
        }),

        // ─── بِطاقَة الإعلان ────────────────────────────────────────────
        new(ListingCard, "ac-space", "detailed", new ThemeVariant[]
        {
            new("detailed", ""),                    // صورَة + عُنوان + مَوقِع + سِعر — اليَوم
            new("compact",  "ac-space--compact"),   // صَفّ أُفُقيّ بِصورَة صَغيرَة
            new("showcase", "ac-space--showcase"),  // صورَة طَويلَة وعُنوان أَكبَر
        }),

        // ─── شَريط الترويسَة ────────────────────────────────────────────
        new(HeaderBar, "acm-v2-topnav-wrap", "solid", new ThemeVariant[]
        {
            new("solid",       ""),                                  // مُمتَلِئ — اليَوم
            new("transparent", "acm-v2-topnav-wrap--transparent"),    // بِلا سَطح ولا حَدّ
            new("compact",     "acm-v2-topnav-wrap--compact"),        // مُرتَفِع أَقَلّ وخَطّ أَصغَر
        }),
    };

    private static readonly Dictionary<string, ThemeVariantSlot> ByKey =
        All.ToDictionary(s => s.Key, StringComparer.Ordinal);

    public static ThemeVariantSlot? Find(string key) =>
        ByKey.TryGetValue(key, out var s) ? s : null;

    public static bool Contains(string key) => ByKey.ContainsKey(key);

    /// <summary>عَدَد الفَتَحات — يُستَعمَل في فَحص اكتِمال الثيم
    /// الافتِراضيّ.</summary>
    public static int Count => All.Count;
}
