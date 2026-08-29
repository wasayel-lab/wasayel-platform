using ACommerce.Platform.I18n;
using Microsoft.AspNetCore.Components;

namespace ACommerce.Templates.Customer.Marketplace.Services;

/// <summary>
/// <para><b>لُغَةُ وَثيقَةٍ واحِدَة — مِن المَسارِ لا مِن الكوكي.</b>
/// صَفَحاتُ المَنَصَّةِ القانونِيَّةُ الثَلاثُ لَها فَرعٌ إنجليزيٌّ
/// بِمَسارٍ مُستَقِلّ (<c>/terms/en</c>…)، ومُراجِعُ بَوّابَةِ الدَفعِ
/// يَفتَحُه بِرابِطٍ مُباشِرٍ بِلا جَلسَةٍ ولا تَفضيل.</para>
///
/// <para><b>ولِماذا لا يُستَعمَلُ مُبَدِّلُ اللُغَةِ القائِم</b> —
/// وهُوَ سُؤالٌ يَجِبُ أَن يُجابَ لا أَن يُتَجاوَز (القاعِدَة ٨):
/// ‏<c>POST /lang/{lang}</c> يَكتُبُ كوكيَ لُغَةٍ <b>لِلمَوقِعِ
/// كُلِّه</b>، فَنَقرَةٌ عَلَيه تَقلِبُ اتِّجاهَ كُلِّ شاشَةٍ عَرَبِيَّةٍ
/// إلى LTR بِلا أَن تُتَرجِمَ مِنها حَرفاً — وهُوَ بِعَينِه «المَدخَلُ
/// الَّذي يَضُرّ» المُوَثَّقُ في القاعِدَة ١٢ وفي
/// <c>docs/LANGUAGE-AND-DIRECTION-DEBT.md</c>، ولِأَجلِه أُخفِيَ
/// الزِرّ. فَالمَسارُ المُستَقِلُّ يُعطي المُراجِعَ نُسخَتَه
/// <b>بِلا لَمسِ حالَةِ المُتَصَفِّحِ ولا اتِّجاهِ بَقِيَّةِ
/// المَوقِع</b>.</para>
///
/// <para><b>وشَرطُ استِخراجِه مَكتوبٌ فيه</b> (القاعِدَة ١): لَه
/// <b>سِتَّةُ</b> مُستَهلِكينَ في وَقتِ التَشغيل — ثَلاثُ وَثائِقَ في
/// فَرعَين لُغَوِيَّين. ولَو نَزَلَ العَدَدُ إلى واحِدٍ فَمَكانُه
/// جِسمُ تِلكَ الصَفحَةِ لا مِلَفٌّ مُستَقِلّ.</para>
/// </summary>
public sealed class PlatformDocLanguage
{
    /// <summary>رَمزُ اللُغَةِ الوَحيدُ المَقبولُ في المَسار. مَعجَمٌ
    /// مُغلَقٌ مِن عُنصُرٍ واحِد — وما خَرَجَ عَنه
    /// <see cref="IsRecognised"/> يَرُدُّه، فَلا يَفتَحُ المَسارُ
    /// فَضاءَ عَناوينَ لا نِهائِيّاً لِلزاحِف.</summary>
    public const string English = "en";

    private PlatformDocLanguage(string lang, bool recognised)
    {
        Lang = lang;
        IsRecognised = recognised;
    }

    /// <summary>
    /// <para>يَقرَأُ وَسيطَ المَسار. <c>null</c> — أَي المَسارُ
    /// الجَذر — هُوَ العَرَبِيَّة، و<c>en</c> هي الإنجليزِيَّة، وما
    /// عَداهُما <b>غَيرُ مَعروف</b>.</para>
    ///
    /// <para><b>و<c>/terms/ar</c> غَيرُ مَعروفٍ عَمداً</b>: العَرَبِيَّةُ
    /// تَسكُنُ المَسارَ الجَذرَ وَحدَه، فَقَبولُ الوَسيطِ الصَريحِ
    /// كانَ سَيُعطي عُنوانَين لِوَثيقَةٍ واحِدَةٍ بِنَفسِ البايتات —
    /// ازدِواجُ مُحتَوىً يَراهُ الزاحِف. والمَعجَمُ المُغلَقُ يُغلَقُ
    /// مِن الطَرَفَين أَو لا يُغلَق.</para>
    /// </summary>
    public static PlatformDocLanguage FromRoute(string? lang)
        => lang is null
            ? new PlatformDocLanguage(LocaleCatalog.Arabic, recognised: true)
            : string.Equals(lang, English, StringComparison.OrdinalIgnoreCase)
                ? new PlatformDocLanguage(English, recognised: true)
                : new PlatformDocLanguage(lang, recognised: false);

    public string Lang { get; }

    public bool IsEnglish => Lang == English;

    /// <summary>هَل هذا المَسارُ لُغَةٌ نَعرِفُها؟ الجَذرُ
    /// و<c>/en</c> نَعَم، وما سِواهُما لا.</summary>
    public bool IsRecognised { get; }

    /// <summary>اتِّجاهُ الوَثيقَةِ وَحدَها. يُكتَبُ عَلى حاوِيَةِ
    /// الوَثيقَةِ لا عَلى <c>html</c> — فَالتَرويسَةُ والتَذييلُ
    /// وبَقِيَّةُ المَوقِعِ تَبقى عَرَبِيَّةً بِاتِّجاهِها.</summary>
    public string Dir => IsEnglish ? "ltr" : "rtl";

    /// <summary>النَصُّ خاماً — لِلخَصائِصِ ولِلكود.</summary>
    public string this[string key] => LocaleCatalog.Text(Lang, key);

    /// <summary>النَصُّ كَعُقدَةِ مارك-أَب، كَما يَفعَلُ
    /// <see cref="L.Markup(string)"/> ولِنَفسِ السَبَبِ حَرفاً:
    /// القيمَةُ مَحروسَةٌ عِندَ البَوّابَةِ مِن
    /// <c>&lt; &gt; &amp;</c>، فَتُكتَبُ كَما كانَت تُكتَبُ
    /// الحَرفِيَّة.</summary>
    public MarkupString Markup(string key) => new(this[key]);
}
