using System.Text;

namespace ACommerce.Kit.Theme;

/// <summary>حاوِيَة تَوطين — العَرَبيَّة إلزامِيَّة والإنجليزيَّة
/// اختِيارِيَّة. نَفس شَكل حاوِيَة الأَدوار، ومُعَرَّفَة هُنا لا
/// مُستَورَدَة: عُدَد المَنصَّة قائِمَة بِذاتِها بِلا اعتِماد بَينِيّ،
/// و«ثيم يَعتَمِد عَلى أَدوار» عَلاقَة لا مَعنى لَها.</summary>
public sealed record ThemeLabel(string? Ar, string? En = null)
{
    public ThemeLabel() : this(null, null) { }
}

/// <summary>
/// <para><b>تَعريف ثيم واحِد</b> — سلاج، وتَسمِيَة، و<b>قامُوس رُموز
/// مُسَطَّح</b> مَفاتيحُه مِن <see cref="ThemeTokenCatalog"/> حَصراً.</para>
///
/// <para><b>لِماذا قامُوس مُسَطَّح لا كائِن مُتَداخِل بِخُصوصِيّات
/// مُسَمّاة</b>: كائِن مِثل <c>{ "color": { "primary": … } }</c>
/// بِأَصناف C# كانَ سَيُغلِق المَعجَم عِندَ التَّرجَمَة — وهذا جَيِّد —
/// لكِنَّه يَجعَل «مِفتاح خارِج المَعجَم» <b>استِثناء قِراءَة</b> لا
/// <b>رَمز خَرق</b>. والوَكيل يُصَحِّح عَلى الرُموز لا عَلى نُصوص
/// استِثناءات الـJSON. القامُوس المُسَطَّح + كاتالوج مَفحوص يُعطي
/// الإغلاق نَفسَه <b>ورَمزاً يُقرَأ</b>:
/// <c>token_key_out_of_vocabulary</c> يُسَمّي المِفتاح المَرفوض.</para>
///
/// <para><b>والجُزئيَّة مَقصودَة</b>: تَعريف مُستَأجِر يَجوز أَن يَحمِل
/// رَمزاً واحِداً. الباقي يَسقُط عَلى الافتِراضيّ — إضافَةٌ فَوق لا
/// إحلال، بِنَفس عَقد أَدوار المُستَأجِر. الاكتِمال مَشروط عَلى الثيم
/// الافتِراضيّ وَحدَه (<see cref="ThemeDefinitionValidator.ValidateDefault"/>).</para>
/// </summary>
public sealed class ThemeDefinition
{
    public string Slug { get; set; } = "";

    public ThemeLabel Label { get; set; } = new();

    /// <summary>مِفتاح ← قيمَة. المَفاتيح مِن
    /// <see cref="ThemeTokenCatalog"/>، والقِيَم بِنَحو نَوعِها.</summary>
    public Dictionary<string, string> Tokens { get; set; } = new(StringComparer.Ordinal);

    /// <summary>
    /// <para>فَتحَة ← قيمَة. المَفاتيح مِن
    /// <see cref="ThemeVariantCatalog"/> حَصراً، والقِيَم مِن قائِمَة كُلّ
    /// فَتحَة حَصراً — <b>قامُوسانِ مُغلَقان لا نَحوٌ يُصادِق</b>.</para>
    ///
    /// <para><b>ولِماذا هُنا لا في مِلَفّ ثانٍ</b>: الهُوِيَّة البَصَرِيَّة
    /// الواحِدَة لَون <b>وشَكل</b> مَعاً. مِلَفّ لِلرُموز وآخَر
    /// لِلمُتَغايِرات كانَ سَيَسمَح بِتَطبيق نِصف هُوِيَّة — لَون الواحَة
    /// عَلى شَكل اللَيل — وهي بِالضَبط الحالَة الَّتي لا يُريدُها العَرض.
    /// الوَثيقَة واحِدَة، فَالتَطبيق ذَرِّيّ.</para>
    /// </summary>
    public Dictionary<string, string> Variants { get; set; } = new(StringComparer.Ordinal);
}

/// <summary>
/// <para><b>الثيم الفَعّال</b> — لَقطَة ساكِنَة جاهِزَة لِلبَثّ: قامُوس
/// مُكتَمِل (كُلّ رَمز في المَعجَم لَه قيمَة) وسَطر CSS مَبنيّ مَرَّةً
/// واحِدَة.</para>
///
/// <para><b>ولِماذا يُبنى النَّصّ هُنا لا عِندَ التَصيير</b>: الصَفحَة
/// تُصَيَّر آلاف المَرّات والثيم يَتَغَيَّر مَرَّةً. وأَهَمّ مِن ذلك:
/// نَصّ واحِد مَبنيّ مَرَّةً يَعني <b>بايتات واحِدَة</b> في كُلّ طَلَب —
/// وهو شَرط بَوّابَة القَبول الَّتي تُقارِن الصَفحَة بايتاً بِبايت.</para>
/// </summary>
public sealed class EffectiveTheme
{
    private readonly Dictionary<string, string> _values;
    private readonly Dictionary<string, string> _variants;

    /// <summary>فَتحَة ← لاحِقَة الصَنف الجاهِزَة. <b>تُحسَب مَرَّةً عِندَ
    /// بِناء الثيم</b> لا عِندَ التَصيير: الصَفحَة تُصَيَّر آلاف
    /// المَرّات، والثيم يَتَغَيَّر مَرَّة. ونَفس السِلسِلَة في كُلّ طَلَب
    /// شَرطُ المُقارَنَة بايتاً بِبايت — بِنَفس مُبَرِّر
    /// <see cref="Css"/>.</summary>
    private readonly Dictionary<string, string> _variantSuffixes;

    private EffectiveTheme(
        string slug,
        IReadOnlyDictionary<string, string> values,
        IReadOnlyDictionary<string, string> variants,
        string css)
    {
        Slug      = slug;
        _values   = new Dictionary<string, string>(values, StringComparer.Ordinal);
        _variants = new Dictionary<string, string>(variants, StringComparer.Ordinal);
        Css       = css;

        _variantSuffixes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var slot in ThemeVariantCatalog.All)
        {
            var chosen = _variants.TryGetValue(slot.Key, out var v) ? slot.Find(v) : null;
            _variantSuffixes[slot.Key] = (chosen ?? slot.Default).ClassSuffix;
        }
    }

    /// <summary>سلاج الثيم المُطَبَّق — <c>default</c> أَو سلاج تَعريف
    /// المُستَأجِر. لِلوغ والتَشخيص لا لِلقَرار.</summary>
    public string Slug { get; }

    /// <summary>كُتلَة <c>:root{…}</c> بِلا وَسم — سَطر واحِد بِلا
    /// مَسافات زائِدَة.</summary>
    public string Css { get; }

    public string this[string key] => _values[key];

    public IReadOnlyDictionary<string, string> Values => _values;

    /// <summary>فَتحَة ← القيمَة المُختارَة. مُكتَمِل: كُلّ فَتحَة في
    /// المَعجَم لَها قيمَة.</summary>
    public IReadOnlyDictionary<string, string> Variants => _variants;

    /// <summary>قيمَة فَتحَة واحِدَة — لِلفُروق <b>البِنيَوِيَّة</b> الَّتي
    /// لا يَقوى عَلَيها صَنف (‏حَذف وَصف البِطاقَة مَثَلاً)، ولِعَرضِها
    /// في سَطح الإدارَة.</summary>
    public string VariantValue(string slotKey) => _variants[slotKey];

    /// <summary>
    /// <para><b>ما يُلصَق في الوَسم</b> بَعد الصَنف الأَساس: فارِغ
    /// لِلقيمَة الافتِراضيَّة، أَو مَسافَة ثُمَّ المُعَدِّل.</para>
    ///
    /// <para>ولِذلك يُكتَب في الوَسم <c>class="ac-space@(…) …"</c> لا
    /// <c>class="ac-space @(…)"</c>: الأَوَّل يُعيد بِالافتِراضيّ نَفس
    /// البايتات الَّتي كانَت، والثاني يُقحِم مَسافَةً زائِدَة في كُلّ
    /// صَفحَة.</para>
    /// </summary>
    public string VariantClassSuffix(string slotKey) => _variantSuffixes[slotKey];

    /// <summary>هَل الفَتحَة عَلى هذه القيمَة؟ اختِصار لِفَرع بِنيَويّ
    /// في التَصيير.</summary>
    public bool VariantIs(string slotKey, string value) =>
        string.Equals(_variants[slotKey], value, StringComparison.Ordinal);

    /// <summary>
    /// <para>يَبني ثيماً فَعّالاً مِن <b>أَساس مُكتَمِل</b> و<b>طَبَقَة
    /// جُزئيَّة اختِيارِيَّة</b> فَوقَه. الطَبَقَة تُغَلِّب مِفتاحاً
    /// بِمِفتاح، ومَفاتيحُها المَجهولَة <b>تُتَجاهَل</b> هُنا (البَوّابَة
    /// عِندَ الكِتابَة وعِندَ القِراءَة، وهذه ثالِثَة).</para>
    ///
    /// <para><b>وطَبَقَة فارِغَة تُرجِع الأَساس نَفسَه</b> —
    /// <c>ReferenceEquals</c> صادِقَة — فَمُستَأجِر بِلا ثيم لا يَمُرّ
    /// بِسَطر بِناء واحِد ولا يُنتِج بايتاً مُختَلِفاً. هذا هو التَكافُؤ
    /// الصِفريّ بِالهُوِيَّة لا بِالمُقارَنَة، كَما في
    /// <c>TenantRoleSet</c>.</para>
    /// </summary>
    public static EffectiveTheme Compose(EffectiveTheme baseTheme, ThemeDefinition? overlay)
    {
        if (overlay is null || (overlay.Tokens.Count == 0 && overlay.Variants.Count == 0))
            return baseTheme;

        var merged   = new Dictionary<string, string>(baseTheme._values,   StringComparer.Ordinal);
        var variants = new Dictionary<string, string>(baseTheme._variants, StringComparer.Ordinal);
        var tokensTouched   = false;
        var variantsTouched = false;

        foreach (var (key, value) in overlay.Tokens)
        {
            if (!ThemeTokenCatalog.Contains(key)) continue;
            if (string.Equals(merged[key], value, StringComparison.Ordinal)) continue;
            merged[key] = value;
            tokensTouched = true;
        }

        // نَفس القاعِدَة لِلمُتَغايِرات: مِفتاح خارِج المَعجَم أَو قيمَة
        // خارِج قائِمَة فَتحَتِها **تُتَجاهَل** هُنا. البَوّابَة عِندَ
        // الكِتابَة وعِندَ القِراءَة، وهذه ثالِثَة — حَرفاً كَما لِلرُموز.
        foreach (var (key, value) in overlay.Variants)
        {
            var slot = ThemeVariantCatalog.Find(key);
            if (slot is null || !slot.Contains(value)) continue;
            if (string.Equals(variants[key], value, StringComparison.Ordinal)) continue;
            variants[key] = value;
            variantsTouched = true;
        }

        // طَبَقَة لا تُغَيِّر قيمَةً واحِدَة = لا طَبَقَة.
        if (!tokensTouched && !variantsTouched) return baseTheme;

        // ونَصّ الـCSS لا يُعاد بِناؤُه إلّا إن تَحَرَّكَ رَمز: مُتَغايِر
        // يُبَدِّل صَنفاً في الوَسم ولا يَمَسّ كُتلَة ‏:root — فَإعادَة
        // بِنائِها كانَت سَتُنتِج سِلسِلَةً جَديدَة مُساوِيَة، وتُضَيِّع
        // «نَفس البايتات» بِلا سَبَب.
        var css = tokensTouched ? BuildCss(merged) : baseTheme.Css;
        return new EffectiveTheme(overlay.Slug, merged, variants, css);
    }

    /// <summary>يَبني ثيماً فَعّالاً مِن تَعريف <b>مُكتَمِل</b>. يَرمي
    /// إن نَقَصَ رَمز — والاكتِمال مَفروض قَبلَه في
    /// <see cref="ThemeDefinitionValidator.ValidateDefault"/>.</summary>
    public static EffectiveTheme FromComplete(ThemeDefinition d)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var token in ThemeTokenCatalog.All)
        {
            if (!d.Tokens.TryGetValue(token.Key, out var v))
                throw new InvalidOperationException(
                    $"الثيم «{d.Slug}» غَير مُكتَمِل — الرَمز «{token.Key}» مَفقود.");
            values[token.Key] = v;
        }

        var variants = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var slot in ThemeVariantCatalog.All)
        {
            if (!d.Variants.TryGetValue(slot.Key, out var v))
                throw new InvalidOperationException(
                    $"الثيم «{d.Slug}» غَير مُكتَمِل — الفَتحَة «{slot.Key}» مَفقودَة.");
            if (!slot.Contains(v))
                throw new InvalidOperationException(
                    $"الثيم «{d.Slug}» يُعطي الفَتحَة «{slot.Key}» قيمَةً «{v}» " +
                    "خارِج قائِمَتِها.");
            variants[slot.Key] = v;
        }

        return new EffectiveTheme(d.Slug, values, variants, BuildCss(values));
    }

    /// <summary>البَثّ: <c>:root{--wsl-…:قيمَة;…}</c> بِتَرتيب المَعجَم
    /// الثابِت. لا مَسافات، ولا سُطور، ولا تَعليقات — أَقَلّ بايتات
    /// وأَبسَط مُقارَنَة.</summary>
    private static string BuildCss(IReadOnlyDictionary<string, string> values)
    {
        var sb = new StringBuilder(":root{");
        foreach (var token in ThemeTokenCatalog.All)
            sb.Append(token.CssVariable).Append(':').Append(values[token.Key]).Append(';');
        return sb.Append('}').ToString();
    }
}
