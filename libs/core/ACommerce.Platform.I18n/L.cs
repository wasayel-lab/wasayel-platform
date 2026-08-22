using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http;

namespace ACommerce.Platform.I18n;

/// <summary>
/// <para><b>مَنفَذ نُصوص الواجِهَة</b> — مُوَسَّع لا مُستَبدَل: نَفس
/// الصَنف، ونَفس الكوكي، ونَفس <c>Lang</c>/<c>Dir</c>. الجَديد أَنّ
/// القامُوسَين المَكتوبَين في هذا المِلَفّ صارا <b>مِلَفَّي JSON</b>
/// يَقرَؤُهما <see cref="LocaleCatalog"/> — فَاللُغَة الثالِثَة مِلَفّ
/// يُضاف لا نَوع يُعَدَّل.</para>
///
/// <para><b>واللُغَة تُحفَظ في كوكي تُقرَأ في المُنشِئ</b> (‏scoped) لا
/// في حالَة circuit: الطَلَب التالي — وإعادَة التَحميل — يَجِدُها.
/// وهذا مَوضِع تَفَوُّقنا على السَلَف، فَلا يُبَدَّل.</para>
///
/// <para><b>ومَنفَذان لا واحِد، والفَرق مَقيس</b>: Razor يَكتُب
/// الحَرفِيَّة في المارك-أَب <b>خاماً</b> ويَكتُب مُخرَج التَعبير
/// <b>مُرَمَّزاً</b> (‏<c>&amp;#x639;</c>…). فَلَو رُحِّلَت عُقدَة نَصّ
/// إلى تَعبير عاديّ لَتَغَيَّرَت بايتات الصَفحَة بِلا أَن يَتَغَيَّر
/// حَرفٌ واحِد مِمّا يَراه المُستَخدِم — وضاعَت بَوّابَة التَطابُق
/// البايتيّ الَّتي تَحرُس التَشكيل. لِذلك:</para>
/// <list type="bullet">
///   <item><see cref="this[string]"/> — <c>string</c> لِلخَصائِص
///   (<c>aria-label</c>، <c>title</c>، <c>placeholder</c>) ولِلكود.</item>
///   <item><see cref="Markup(string)"/> — <see cref="MarkupString"/>
///   لِعُقَد النَصّ، فَتُكتَب كَما كانَت الحَرفِيَّة تُكتَب تَماماً.</item>
/// </list>
/// <para>وسَلامَةُ الثاني مَحروسَة عِندَ البَوّابَة لا عِندَ
/// الاستِعمال: <c>value_unsafe_markup</c> في
/// <see cref="LocaleValidator"/> يَرفُض أَيّ قيمَة فيها
/// <c>&lt; &gt; &amp;</c>؛ ووُسَطاء <see cref="Markup(string, object?[])"/>
/// تُرَمَّز واحِداً واحِداً قَبل الدَمج.</para>
/// </summary>
public sealed class L
{
    public const string CookieName = ".acommerce.lang";

    public L(IHttpContextAccessor http)
    {
        var ctx = http.HttpContext;
        if (ctx is not null && ctx.Request.Cookies.TryGetValue(CookieName, out var lang))
        {
            // «لُغَة مَوجودَة» = لَها مِلَفّ. سابِقاً كانَ الشَرط
            // `lang == "en"` حَرفاً؛ والمَجموعَة اليَوم {ar, en}
            // فَالسُلوك واحِد — لكِنّ اللُغَة الثالِثَة تَعمَل بِلا
            // لَمس هذا السَطر.
            if (!string.IsNullOrEmpty(lang) && LocaleCatalog.Has(lang))
                Lang = lang;
        }
    }

    public string Lang { get; set; } = LocaleCatalog.Arabic;
    public bool IsArabic => Lang == LocaleCatalog.Arabic;
    public string Dir => IsArabic ? "rtl" : "ltr";

    /// <summary>النَصّ خاماً: لُغَتُه، ثُمَّ العَرَبِيَّة، ثُمَّ
    /// المِفتاح.</summary>
    public string this[string key] => LocaleCatalog.Text(Lang, key);

    /// <summary>النَصّ كَعُقدَة مارك-أَب — لِيَبقى المُخرَج مُطابِقاً
    /// لِلحَرفِيَّة الَّتي حَلَّ مَحَلَّها بايتاً بِبايت.</summary>
    public MarkupString Markup(string key) => new(this[key]);

    /// <summary>النَصّ المُدمَج فيه وُسَطاء. كُلّ وَسيط <b>يُرَمَّز</b>
    /// قَبل الدَمج، فَاسمُ مُستَأجِر لا يَكتُب وَسماً. والأَرقام
    /// تَمُرّ بِلا تَغَيُّر (‏لا مَحرَفَ فيها يُرَمَّز)، فَيَبقى
    /// المُخرَج كَما كان.</summary>
    public MarkupString Markup(string key, params object?[] args)
        => new(string.Format(
            this[key],
            args.Select(a => (object?)HtmlEncoder.Default.Encode(a?.ToString() ?? string.Empty))
                .ToArray()));

    /// <summary>النَصّ المُدمَج فيه وُسَطاء، خاماً — لِلخَصائِص ولِلكود.
    /// (‏الخاصِّيَّة تُرَمَّزُها Razor عِندَ الكِتابَة.)</summary>
    public string Format(string key, params object?[] args)
        => string.Format(this[key], args);

    /// <summary><b>المَنفَذ الثالِث — داخِلَ حَرفِيَّةِ سِلسِلَةِ
    /// JavaScript</b> في عُنصُر <c>&lt;script&gt;</c>:
    /// <c>'@L.Js("k")'</c>.
    ///
    /// <para>ولِماذا ثالِثٌ لا مُشتَقّ مِن الاثنَين: مُحتَوى
    /// <c>&lt;script&gt;</c> نَصٌّ خام لا يَفُكّ مَراجِعَ HTML، فَـ
    /// <see cref="this[string]"/> (المُرَمَّز) يَكسِر المَعروض، و
    /// <see cref="Markup(string)"/> (الخام) يَفتَح الخُروجَ مِن
    /// الحَرفِيَّة ومِن العُنصُر. والصَحيحُ هُروبُ JavaScript —
    /// وتَفصيلُ أَبوابِه الثَلاثَة في <see cref="JsText"/>.</para>
    ///
    /// <para>و<see cref="MarkupString"/> هُنا لِلسَبَب نَفسِه الَّذي
    /// جَعَلَها في <see cref="Markup(string)"/>: التَعبيرُ المُرَمَّز
    /// يُبَدِّل بايتات الصَفحَة بِلا أَن يُبَدِّل حَرفاً مِمّا يُرى.
    /// وسَلامَتُها هُنا مِن <see cref="JsText.Escape"/> لا مِن
    /// المُصَيِّر.</para></summary>
    public MarkupString Js(string key) => new(JsText.Escape(this[key]));
}
