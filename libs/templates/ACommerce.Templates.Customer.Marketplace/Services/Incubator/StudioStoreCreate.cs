using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;

namespace ACommerce.Templates.Customer.Marketplace.Services.Incubator;

/// <summary>
/// <para><b>إنشاءُ مَتجَرٍ بِاليَدِ مِن الاستوديو — مُعجَمُ البابِ ومَساراتُه</b> (‏2026-11-07، الموجَة ١٣ W13-1·٣ — ADR-049 §٤).</para>
///
/// <para><b>العِلَّةُ المَقيسَة</b>: كانَ البابُ الوَحيدُ الَّذي يَملِكُ مِنه رائِدُ الأَعمالِ مَتجَراً <c>/studio/s/{id}/build</c>، يَشتَرِطُ جَلسَةَ تَحليلٍ
/// <c>Completed</c> لا يَكتُبُها إلّا نِداءُ نَموذَجٍ ناجِح — فَبِلا مِفتاحٍ لا مَتجَر. والتَحليلُ لا يُعطي المَتجَرَ إلّا تِلكَ البَوّابَةَ ومَلءَ سَطرِ الوَصف.
/// فَصارَ <see cref="Page"/> استِمارَةَ الإنشاءِ و<see cref="Endpoint"/> بابَها، بِالمَصنَعِ نَفسِه (<see cref="TenantFromAnalysisFactory.CreateAsync"/>)
/// ونَمَطٍ وقِطاعٍ فارِغَين — فَالمَتجَرُ هُوَ ما كانَ <c>/build</c> يَكتُبُه لِجَلسَةٍ بِالوَصفِ وَحدَه بايتاً، عَدا المُعَرِّفِ والوَقتِ والمَصدَرِ
/// (‏<c>StudioStoreBirthCharacterizationTests.GoldenSha256</c>). ولا مُنتَقيَ أَنماط: المُنتَجُ المَتجَرُ مُتَعَدِّدُ البائِعين (‏ADR-032).</para>
///
/// <para><b>والحارِسُ في أَوَّلِ أَسطُرِ البابِ بِتَرتيبِه</b> (القاعِدَة ٦): الهُوِيَّة ← المُوافَقَة ← حِصَّةُ المَتاجِر ← المُعَرِّف ← الاسم ← اللَون
/// ← قَناةُ الدُخول — ولا يَقرَأُ حَقلاً قَبلَ التَخويل. ويُفحَصُ بِالمَصدَرِ في <c>StudioStoreCreateTests</c>.</para>
/// </summary>
public static class StudioStoreCreate
{
    /// <summary>صَفحَةُ الاستِمارَة — المَسارُ نَفسُه الَّذي كانَت تَرسُمُه صَفحَةُ الفِكرَة، فَتَبقى روابِطُها وسَطرُ خَطِّ المَساراتِ الأَساسِيّ.</summary>
    public const string Page = "/studio/new";

    /// <summary>بابُ الكِتابَة.</summary>
    public const string Endpoint = "/studio/apps/create";

    /// <summary>صَفحَةُ المُوافَقَةِ بِوِجهَةِ عَودَةٍ إلى الاستِمارَة — يُرسَلُ إلَيها مَن لَم يَقبَل الإصدارَ القائِم، مِن الصَفحَةِ ومِن البابِ مَعاً.</summary>
    public const string ConsentPage = "/studio/consent?returnUrl=/studio/new";

    public const string NameRequired = "name_required";
    public const string ColorInvalid = "color_invalid";

    /// <summary>لَونُ <c>marketplace</c> — ما كانَت استِمارَةُ البِناءِ تُعَبِّئُه سَلَفاً لِجَلسَةٍ بِالوَصفِ وَحدَه.</summary>
    public const string DefaultColor = "#2563eb";

    /// <summary><b>رُموزُ الرَفضِ الَّتي تَرسُمُها الاستِمارَة — مُعجَمٌ مُغلَق</b>، كُلٌّ بِمِفتاحِ قامُوسٍ (<see cref="MessageKey"/>). ورَفضُ الحِصَّةِ
    /// لَيسَ مِنها: رَمزُ بَوّابَتِه القائِمُ (<c>?upgrade=…</c>) تَرسُمُه نافِذَةُ التَرقِيَة.</summary>
    public static readonly IReadOnlyList<string> Refusals =
    [
        TenantFromAnalysisFactory.SlugRequired,
        TenantFromAnalysisFactory.SlugFormat,
        TenantFromAnalysisFactory.SlugReserved,
        TenantFromAnalysisFactory.SlugTaken,
        NameRequired,
        ColorInvalid,
        TenantAuthChannelDoor.NoChannel,
    ];

    public static string MessageKey(string code) => "studio.create.err_" + code;

    /// <summary><b>وِجهَةُ ما بَعدَ الدُخول</b> (‏W13-1·٤ — ADR-049 §٨): حِسابُ استوديو لا يَملِكُ مَتجَراً يَبلُغُ استِمارَةَ الإنشاءِ مُباشَرَةً، فَدَعوَةُ
    /// الصَفحَةِ الأُولى «أَنشِئ مَتجَرَك» دُخولٌ ثُمَّ الاستِمارَة؛ ومَن يَملِكُ مَتجَراً يَبلُغُ لَوحَتَه كَما كان.</summary>
    public static string AfterSignIn(int ownedStores) => ownedStores == 0 ? Page : "/studio";

    /// <summary>حُقولُ الاستِمارَةِ كَما قُرِئَت — المُعَرِّفُ بِحُروفٍ صَغيرَة كَما كانَ <c>/build</c> يَقرَؤُه.</summary>
    public sealed record Form(string Slug, string Name, string Color, string TagLine);

    public static Form Read(IFormCollection f) => new(
        f["slug"].ToString().Trim().ToLowerInvariant(),
        f["name"].ToString().Trim(),
        f["color"].ToString().Trim(),
        f["tagline"].ToString().Trim());

    public static bool ColorValid(string color) => Regex.IsMatch(color, "^#[0-9A-Fa-f]{6}$");

    /// <summary>العَودَةُ إلى الاستِمارَةِ بِرَمزِ الرَفضِ وبِما كُتِبَ فيها — فَلا يُعيدُ صاحِبُها كِتابَةَ حُقولٍ صَحيحَةٍ لِأَجلِ حَقلٍ واحِد.</summary>
    public static string Refused(string code, Form form)
        => $"{Page}?err={Uri.EscapeDataString(code)}&slug={Uri.EscapeDataString(form.Slug)}&name={Uri.EscapeDataString(form.Name)}"
         + $"&color={Uri.EscapeDataString(form.Color)}&tagline={Uri.EscapeDataString(form.TagLine)}";
}
