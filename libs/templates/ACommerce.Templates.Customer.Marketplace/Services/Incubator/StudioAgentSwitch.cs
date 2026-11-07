using Microsoft.Extensions.Configuration;

namespace ACommerce.Templates.Customer.Marketplace.Services.Incubator;

/// <summary>
/// <para><b>مِفتاحُ وَكيلِ التَصميمِ («وَكيل التَّطبيقات») — مُطفَأٌ ما لَم يُضِئه المالِكُ صَراحَةً</b> (‏2026-11-07، الموجَة ١٣ W13-1·٦ — ADR-049 §٦).</para>
///
/// <para><b>لِماذا مِفتاحٌ لا «غِيابُ المِفتاح»</b>: حاضِنَةُ المُشرِفِ الباقِيَةُ قَد تَحتاجُ <c>Agent__ApiKey</c> مُشتَرَكاً، ومِلَفُّ <c>Agents:Studio</c>
/// يَسقُطُ إلَيه (<c>AgentProfileResolver.Resolve</c>) — فَغِيابُ مِفتاحِ النَموذَجِ لَيسَ إخفاءً مَوثوقاً. والمَفتاحُ الصَريحُ يَجعَلُ «مَخفِيّ» مُستَقِلّاً
/// عَن إدارَةِ مَفاتيحِ النَماذِج، ويَبقى صالِحاً يَومَ يُفتَحُ الوَكيلُ لِرُوّادِ الأَعمال.</para>
///
/// <para><b>ولِماذا أَمامَ <see cref="StudioAgentDoor.Opens"/> لا مُعامِلاً ثالِثاً فيه</b>: اختِباراتُ الوَكيلِ القائِمَةُ تُثَبِّتُ مُدخَلَي البابِ ونَصَّ
/// نِدائِه في الصَفحَتَينِ بِحَرفِه، وبَقاؤها بِلا تَعديلِ حَرفٍ هُوَ بُرهانُ أَنّ «أَبقِ» صَمَدَ مَعَ «أَخفِ». فَالسُؤالُ مُرَكَّب: مِفتاحٌ مُضاءٌ <b>و</b>بابٌ
/// مَفتوح. ومَن يَسأَلُه: <c>StudioHome</c> (رابِطا الوَكيل)، و<c>StudioAgent</c> (فَرعُ الرَسم)، و<c>/admin/agent</c> (الصَفحَةُ ونِقاطُها الأَربَع)،
/// وكُلُّ رابِطٍ إلى <c>/admin/agent</c>، و<c>AgentService</c> نَفسُه قَبلَ نِداءِ الخَلفِيَّة — فَلا نِداءَ نَموذَجٍ مِن الوَكيلِ والمِفتاحُ مُطفَأ.</para>
///
/// <para><b>ولا يَقرَأُ حالَةً مُخَزَّنَة</b> (القاعِدَة ٧): إعدادٌ يُقرَأُ مَرَّةً عِندَ التَركيب.</para>
/// </summary>
public sealed class StudioAgentSwitch(bool enabled)
{
    /// <summary>اسمُ الإعداد (والبيئَةُ <c>Studio__Agent__Enabled</c>).</summary>
    public const string Setting = "Studio:Agent:Enabled";

    public bool Enabled { get; } = enabled;

    /// <summary>الغائِبُ والفارِغُ وما لَيسَ <c>true</c> مُطفَأ — الإضاءَةُ فِعلٌ صَريحٌ لا افتِراض.</summary>
    public static bool Parse(string? raw) => bool.TryParse(raw?.Trim(), out var on) && on;

    public static StudioAgentSwitch From(IConfiguration configuration) => new(Parse(configuration[Setting]));

    /// <summary>بادِئَةُ سَطرِ الإقلاعِ الَّذي تَقرَؤُه بَوّابَةُ الإنتاج (الكُتلَة <c>agent_door</c> في <c>scripts/verify-production-boot.sh</c>) — إعلاماً لا حُكماً.</summary>
    public const string BootLogPrefix = "[studio] agent_door=";

    /// <summary>حالُ الوَكيلِ عِندَ الإقلاع: <c>hidden</c> (المِفتاحُ مُطفَأ)، أَو <c>open</c> (مُضاءٌ وخَلفِيَّةُ <c>Studio</c> مَضبوطَة — لِمُشرِفِ المَنَصَّةِ وَحدَه، د61)،
    /// أَو <c>agent_unavailable</c> (مُضاءٌ بِلا خَلفِيَّة).</summary>
    public static string BootLogValue(bool enabled, bool backendConfigured)
        => !enabled ? "hidden" : backendConfigured ? "open" : "agent_unavailable";
}
