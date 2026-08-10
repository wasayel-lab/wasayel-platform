namespace ACommerce.Kit.Theme;

/// <summary>
/// <para><b>نَوع قيمَة الرَمز</b> — يُحَدِّد نَحوَها المَقبول عِندَ
/// المُصادَقَة. مُغلَق: أَربَعَة لا خامِس، وكُلّ نَوع لَه نَمَط صارِم في
/// <see cref="ThemeDefinitionValidator"/>.</para>
/// </summary>
public enum ThemeTokenKind
{
    /// <summary>‏<c>#RGB</c> أَو <c>#RRGGBB</c> أَو <c>#RRGGBBAA</c> أَو
    /// <c>rgb(…)</c>/<c>rgba(…)</c> بِأَرقام فَقَط.</summary>
    Color,

    /// <summary>طول CSS: عَدَد ثُمَّ <c>px</c>/<c>rem</c>/<c>em</c>/<c>%</c>،
    /// أَو <c>0</c> مُجَرَّداً.</summary>
    Length,

    /// <summary>عَدَد بِلا وَحدَة (‏<c>1.5</c>، <c>1</c>).</summary>
    Number,

    /// <summary>وَزن خَطّ: مِئَة إلى تِسعِمِئَة بِمَضاعَفات المِئَة.</summary>
    Weight,
}

/// <summary>رَمز تَصميم واحِد في المَعجَم: مِفتاحُه في <c>theme.json</c>،
/// واسم مُتَغَيِّر CSS الَّذي يُبَثّ بِه، ونَوع قيمَتِه.</summary>
/// <param name="Key">المِفتاح كَما يُكتَب في الوَثيقَة — <c>color.primary</c>.</param>
/// <param name="CssVariable">اسم المُتَغَيِّر المَبثوث — <c>--wsl-color-primary</c>.
/// <b>مَكتوب صَراحَةً لا مُشتَقّ</b>: اشتِقاقُه مِن المِفتاح كانَ سَيَجعَل
/// إعادَة تَسمِيَة مِفتاح تُغَيِّر اسم مُتَغَيِّر تَقرَؤُه أَوراق أَنماط
/// أُخرى، صامِتاً.</param>
/// <param name="Kind">نَحو القيمَة المَقبول.</param>
public sealed record ThemeToken(string Key, string CssVariable, ThemeTokenKind Kind);

/// <summary>
/// <para><b>مَعجَم رُموز التَّصميم — مُغلَق</b>. مِفتاح خارِجَه يُرفَض
/// بِرَمز خَرق، تَماماً كَما تُرفَض صَلاحِيَّة خارِج
/// <c>PermissionCatalog</c> في مَوجَة الأَدوار. وهذا الإغلاق هو ما
/// يَجعَل بَثّ قيمَة يَكتُبُها مُستَأجِر داخِل وَسم <c>&lt;style&gt;</c>
/// آمِناً: <b>لا مِفتاح يَصِل الصَفحَة إلّا مِن هذه القائِمَة</b>، فَاسم
/// المُتَغَيِّر المَبثوث ثابِت مِن الكود لا مِن الوَثيقَة.</para>
///
/// <para><b>وكُلّ رَمز هُنا لَه مُستَهلِك حَقيقيّ مَقيس</b> — لا رَمز
/// «لِلاكتِمال». المَعجَم اشتُقَّ بِعَدّ استِعمالات <c>var(--ac-…)</c>
/// في أَوراق الأَنماط السَبع: ‏<c>--ac-primary</c> ‏244 استِعمالاً،
/// و<c>--ac-text</c> ‏151، و<c>--ac-radius-md</c> ‏72… وأُسقِطَ
/// <c>--ac-surface-alt</c> و<c>--ac-error</c> لِأَنّ عَدَدَهُما
/// <b>صِفر</b> — ولَو أُدرِجا لَكانا رَمزَين يُغَيِّرُهُما المُستَأجِر
/// فَلا يَتَغَيَّر شَيء، وهذا أَسوَأ مِن غِيابِهِما.</para>
///
/// <para><b>وما لَيسَ رَمزاً عَمداً</b>:</para>
/// <list type="bullet">
///   <item><b>عائِلَة الخَطّ</b> — ثابِتَة (‏Cairo). تَغطِيَة المَحارِف
///   العَرَبيَّة شَرط لا تَفضيل، ومُستَأجِر يَختار خَطّاً بِلا تَغطِيَة
///   يَرسُم مُرَبَّعات فارِغَة عَلى الجِهاز. تُفتَح حينَ يُفحَص كُلّ خَطّ
///   مُرَشَّح فِعلاً، لا قَبل.</item>
///   <item><b>الظِلال والانتِقالات و<c>--ac-tint-*</c></b> — قِيَمُها
///   مُرَكَّبَة (‏<c>color-mix</c>، ظِلال مُتَعَدِّدَة الطَبَقات)، ونَحوٌ
///   يَقبَلُها يَقبَل مَعَها CSS عَشوائيّاً. تَبقى مُشتَقَّة مِن
///   <c>--ac-primary</c> كَما هي.</item>
///   <item><b>الاتِّجاه</b> — تَخطيط لا رَمز، ويُحسَم مَع تَعَدُّد
///   اللُغات.</item>
/// </list>
/// </summary>
public static class ThemeTokenCatalog
{
    /// <summary>سابِقَة كُلّ مُتَغَيِّرات هذه الطَبَقَة. مُنفَصِلَة عَن
    /// <c>--ac-</c> عَمداً: <c>--ac-*</c> هي <b>مُخرَجات</b> تَقرَؤُها
    /// المُكَوِّنات، و<c>--wsl-*</c> هي <b>مُدخَلات</b> يَكتُبُها الثيم —
    /// وخَلطُهُما كانَ سَيَجعَل وَثيقَة مُستَأجِر تَكتُب مُباشَرَةً في
    /// فَضاء أَسماء تَملِكُه أَوراق الأَنماط.</summary>
    public const string Prefix = "--wsl-";

    /// <summary><b>المَعجَم بِتَرتيبِه</b> — وهو تَرتيب البَثّ أَيضاً،
    /// فَمُخرَج الثيم الواحِد نَفسُه في كُلّ طَلَب (شَرط المُقارَنَة
    /// بايتاً بِبايت).</summary>
    public static readonly IReadOnlyList<ThemeToken> All = new ThemeToken[]
    {
        // ─── اللَوحَة: العَلامَة ────────────────────────────────────────
        new("color.primary",        "--wsl-color-primary",        ThemeTokenKind.Color),
        new("color.primaryDark",    "--wsl-color-primary-dark",   ThemeTokenKind.Color),
        new("color.primaryLight",   "--wsl-color-primary-light",  ThemeTokenKind.Color),
        new("color.primaryHover",   "--wsl-color-primary-hover",  ThemeTokenKind.Color),
        new("color.secondary",      "--wsl-color-secondary",      ThemeTokenKind.Color),
        new("color.secondaryHover", "--wsl-color-secondary-hover", ThemeTokenKind.Color),

        // ─── اللَوحَة: الأَسطُح ─────────────────────────────────────────
        new("color.bg",             "--wsl-color-bg",             ThemeTokenKind.Color),
        new("color.bgAlt",          "--wsl-color-bg-alt",         ThemeTokenKind.Color),
        new("color.surface",        "--wsl-color-surface",        ThemeTokenKind.Color),
        new("color.surface2",       "--wsl-color-surface-2",      ThemeTokenKind.Color),

        // ─── اللَوحَة: الحُدود ──────────────────────────────────────────
        new("color.border",         "--wsl-color-border",         ThemeTokenKind.Color),
        new("color.borderStrong",   "--wsl-color-border-strong",  ThemeTokenKind.Color),

        // ─── اللَوحَة: النَّصّ ───────────────────────────────────────────
        new("color.text",           "--wsl-color-text",           ThemeTokenKind.Color),
        new("color.textMuted",      "--wsl-color-text-muted",     ThemeTokenKind.Color),
        new("color.textSoft",       "--wsl-color-text-soft",      ThemeTokenKind.Color),

        // ─── اللَوحَة: الحالات ──────────────────────────────────────────
        new("color.success",        "--wsl-color-success",        ThemeTokenKind.Color),
        new("color.danger",         "--wsl-color-danger",         ThemeTokenKind.Color),
        new("color.warning",        "--wsl-color-warning",        ThemeTokenKind.Color),
        new("color.info",           "--wsl-color-info",           ThemeTokenKind.Color),

        // ─── أَنصاف الأَقطار ────────────────────────────────────────────
        new("radius.sm",            "--wsl-radius-sm",            ThemeTokenKind.Length),
        new("radius.md",            "--wsl-radius-md",            ThemeTokenKind.Length),
        new("radius.lg",            "--wsl-radius-lg",            ThemeTokenKind.Length),
        new("radius.xl",            "--wsl-radius-xl",            ThemeTokenKind.Length),
        new("radius.pill",          "--wsl-radius-pill",          ThemeTokenKind.Length),

        // ─── مِقياس المَسافات ───────────────────────────────────────────
        new("space.xs",             "--wsl-space-xs",             ThemeTokenKind.Length),
        new("space.sm",             "--wsl-space-sm",             ThemeTokenKind.Length),
        new("space.md",             "--wsl-space-md",             ThemeTokenKind.Length),
        new("space.lg",             "--wsl-space-lg",             ThemeTokenKind.Length),
        new("space.xl",             "--wsl-space-xl",             ThemeTokenKind.Length),

        // ─── الطِباعَة ──────────────────────────────────────────────────
        new("fontSize.sm",          "--wsl-font-size-sm",         ThemeTokenKind.Length),
        new("fontSize.base",        "--wsl-font-size-base",       ThemeTokenKind.Length),
        new("fontSize.lg",          "--wsl-font-size-lg",         ThemeTokenKind.Length),
        new("fontSize.xl",          "--wsl-font-size-xl",         ThemeTokenKind.Length),
        new("fontWeight.normal",    "--wsl-font-weight-normal",   ThemeTokenKind.Weight),
        new("fontWeight.bold",      "--wsl-font-weight-bold",     ThemeTokenKind.Weight),
        new("lineHeight.base",      "--wsl-line-height-base",     ThemeTokenKind.Number),

        // ─── الكَثافَة ──────────────────────────────────────────────────
        // مُضاعِف مِقياس المَسافات. ‏1 = الحالَة اليَوم بِالضَبط
        // (‏calc(x · 1) ≡ x)، وهو أَيضاً ما يَجعَل هذا الرَمز يَدخُل
        // بِتَكافُؤ صِفريّ بَرهانيّ لا بِدَعوى.
        new("density",              "--wsl-density",              ThemeTokenKind.Number),
    };

    private static readonly Dictionary<string, ThemeToken> ByKey =
        All.ToDictionary(t => t.Key, StringComparer.Ordinal);

    public static ThemeToken? Find(string key) =>
        ByKey.TryGetValue(key, out var t) ? t : null;

    public static bool Contains(string key) => ByKey.ContainsKey(key);

    /// <summary>عَدَد الرُموز — يُستَعمَل في فَحص اكتِمال الثيم
    /// الافتِراضيّ.</summary>
    public static int Count => All.Count;
}
