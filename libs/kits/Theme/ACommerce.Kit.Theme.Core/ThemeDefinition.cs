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

    private EffectiveTheme(string slug, IReadOnlyDictionary<string, string> values, string css)
    {
        Slug   = slug;
        _values = new Dictionary<string, string>(values, StringComparer.Ordinal);
        Css    = css;
    }

    /// <summary>سلاج الثيم المُطَبَّق — <c>default</c> أَو سلاج تَعريف
    /// المُستَأجِر. لِلوغ والتَشخيص لا لِلقَرار.</summary>
    public string Slug { get; }

    /// <summary>كُتلَة <c>:root{…}</c> بِلا وَسم — سَطر واحِد بِلا
    /// مَسافات زائِدَة.</summary>
    public string Css { get; }

    public string this[string key] => _values[key];

    public IReadOnlyDictionary<string, string> Values => _values;

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
        if (overlay is null || overlay.Tokens.Count == 0) return baseTheme;

        var merged = new Dictionary<string, string>(baseTheme._values, StringComparer.Ordinal);
        var touched = false;

        foreach (var (key, value) in overlay.Tokens)
        {
            if (!ThemeTokenCatalog.Contains(key)) continue;
            if (string.Equals(merged[key], value, StringComparison.Ordinal)) continue;
            merged[key] = value;
            touched = true;
        }

        // طَبَقَة لا تُغَيِّر قيمَةً واحِدَة = لا طَبَقَة.
        return touched
            ? new EffectiveTheme(overlay.Slug, merged, BuildCss(merged))
            : baseTheme;
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
        return new EffectiveTheme(d.Slug, values, BuildCss(values));
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
