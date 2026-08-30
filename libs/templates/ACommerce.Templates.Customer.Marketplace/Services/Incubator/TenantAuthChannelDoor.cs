using ACommerce.Kit.Auth;
using Microsoft.Extensions.DependencyInjection;

namespace ACommerce.Templates.Customer.Marketplace.Services.Incubator;

/// <summary>
/// بابُ دُخولِ <b>مُستَأجِر</b> — أَيُّ القَنَواتِ يُمكِنُ أَن يَختارَها
/// مَتجَرٌ على <b>هذِه النُسخَة</b>، وأَيُّها الافتِراضيُّ حينَ يُبنى
/// مَتجَرٌ جَديد. <b>جَدوَلٌ نَقِيٌّ بِلا I/O</b>، عَلى غِرارِ
/// <see cref="StudioAuthDoor"/> و<see cref="AuthChannelSelection"/>.
///
/// <para><b>العِلَّةُ المَقيسَة (‏2026-08-30)</b>: المَسارُ الذاتِيُّ
/// كامِلٌ ويَعمَل — يُسَجِّلُ العَميلُ ويُجيبُ أَسئِلَةَ الاكتِشافِ
/// ويَنقُرُ «ابنِ»، فَيَصيرُ مَتجَرُه حَيّاً على <c>/{slug}</c> في
/// ثَوانٍ بِلا نَشرٍ ولا نِطاقٍ ولا لَمسَةٍ مِن المالِك.
/// <b>وبابُه مُغلَق</b>: ‏<c>TenantFromAnalysisFactory.CreateAsync</c>
/// كانَ يَكتُبُ <c>AuthChannel = "phone"</c> <b>ثابِتَةً مَكتوبَة</b>،
/// و<c>docs/DEPLOY.md</c> §٢·ب يُوصي بِقَناةِ البَريدِ (<c>brevo</c>)
/// لِأَنّ المُستَضيفَ يَحجُبُ مَنافِذَ SMTP. فَعَلى النُسخَةِ المُوصى
/// بِها في وَثيقَتِها، كُلُّ مَتجَرٍ يَبنيه عَميلٌ بِنَفسِه يُولَدُ
/// على قَناةٍ <b>غَيرِ مُسَجَّلَة</b> — فَتَرُدُّ <c>Login.razor</c>
/// لافِتَةً حَمراءَ بَدَلَ النَموذَج، ولا يَفتَحُه إلّا المالِكُ
/// بِيَدِه مِن <c>/admin</c>. وتِلكَ هي الخُطوَةُ اليَدَوِيَّةُ
/// <b>الحاجِبَةُ</b> الوَحيدَةُ بَينَ بِناءِ العَميلِ لِمَتجَرِه
/// وأَوَّلِ طَلَبٍ فيه.</para>
///
/// <para><b>ولِماذا دالَّةٌ جَديدَةٌ لا تَبديلُ
/// <see cref="AuthChannels.Default"/></b>: ذاكَ ثابِتٌ <b>عالَمِيّ</b>
/// يَقرَؤُه المُستَورِدونَ والبَذّاراتُ وأَداةُ الوَكيل، ومُثَبَّتٌ
/// في <c>AuthEmailChannelTests</c>. فَجَعلُه تابِعاً لِلتَهيئَةِ
/// يُغَيِّرُ سُلوكَ مَساراتٍ لا عَلاقَةَ لَها بِالاستوديو. الاشتِقاقُ
/// إذَن <b>طَبَقَةٌ فَوقَه</b>: جَدوَلٌ يَقرَأُ ما هُوَ مُسَجَّلٌ
/// فِعلاً، ويَرُدُّ <c>null</c> حينَ لا شَيء.</para>
///
/// <para><b>والوَصلَةُ الاسمِيَّةُ تُكتَبُ هُنا مَرَّةً واحِدَة</b>:
/// <c>AuthChannelKind.Sms</c> ↔ قيمَةُ المُستَأجِرِ <c>"phone"</c> —
/// اسمانِ لِشَيءٍ واحِد، ونَسخُهُما في مَوضِعَينِ هُوَ عَينُ ما وُضِعَ
/// الجَدوَلُ لِيَمنَعَه.</para>
///
/// <para><b>وثَلاثَةٌ هُنا لا اثنان</b> — بِخِلافِ
/// <see cref="StudioAuthDoor"/>: نَفاذٌ قَناةُ <b>مُستَأجِر</b>
/// يَختارُها، ولَيسَ بابَ مُشرِفِ المَنَصَّة.</para>
/// </summary>
public static class TenantAuthChannelDoor
{
    /// <summary>رَمزُ الخَرقِ حينَ لا قَناةَ مُهَيَّأَةً في هذِه
    /// النُسخَة — مَعجَمٌ مُغلَقٌ على غِرارِ
    /// <see cref="TenantFromAnalysisFactory.SlugRequired"/> وأَخَواتِها،
    /// يُتَرجِمُه القامُوسُ في الشاشَة. <b>رِسالَةٌ صَريحَةٌ لِلعَميل،
    /// لا مَتجَرٌ بِبابٍ مُغلَق.</b></summary>
    public const string NoChannel = "no_auth_channel";

    /// <summary>ما تَقبَلُه هذِه النُسخَةُ فِعلاً، <b>بِتَرتيبٍ
    /// مُثَبَّت</b> — والتَرتيبُ لَيسَ ذَوقاً: أَوَّلُ المَعروضِ هُوَ
    /// الافتِراضيُّ لِمَتجَرٍ يُبنى الآن. وهُوَ تَرتيبُ
    /// <see cref="AuthChannels.All"/> نَفسُه وتَرتيبُ أَزرارِ صَفحَةِ
    /// الإدارَة — فَلا يَخفى انحِرافٌ بَينَها.</summary>
    public static IReadOnlyList<string> Offered(bool phone, bool nafath, bool email)
    {
        var list = new List<string>(3);
        if (phone)  list.Add(AuthChannels.Phone);
        if (nafath) list.Add(AuthChannels.Nafath);
        if (email)  list.Add(AuthChannels.Email);
        return list;
    }

    /// <summary>
    /// <b>الحافَّةُ الوَحيدَةُ الَّتي تَقرَأُ الوِعاء</b> — وما تَقرَؤُه
    /// <b>أَثَرُ</b> <see cref="AuthChannelSelection.Decide"/> في
    /// <c>Program.cs</c>، لا التَهيئَةَ ثانِيَةً. وهذا بِعَينِه ما
    /// يَفعَلُه <c>StudioAuth.razor</c> و<c>Login.razor</c> اليَوم
    /// (القاعِدَة ٨: لا أُنبوبَ رابِعاً).
    ///
    /// <para>ولا يَلزَمُ فَحصُ «أَمُحاكٍ هُوَ؟»: حارِسُ الإقلاعِ
    /// <see cref="AuthChannelSelection.AssertNoStubsOutsideDevelopment"/>
    /// يَرمي قَبلَ أَوَّلِ طَلَبٍ إن تَسَرَّبَ مُحاكٍ خارِجَ
    /// التَطوير — فَالمُسَجَّلُ هُنا فِعليٌّ بِبُرهانٍ لا بِظَنّ.</para>
    /// </summary>
    public static IReadOnlyList<string> OfferedIn(IServiceProvider services) => Offered(
        phone:  services.GetService<IOtpChannel>()      is not null,
        nafath: services.GetService<INafathChannel>()   is not null,
        email:  services.GetService<IEmailOtpChannel>() is not null);

    /// <summary>القَناةُ الَّتي تُكتَبُ فِعلاً: المَطلوبَةُ إن كانَت
    /// مَعروضَة، وإلّا أَوَّلُ مَعروضَة، وإلّا <c>null</c> — ونَفسُ
    /// تَوقيعِ <see cref="StudioAuthDoor.Active"/> حَرفاً.
    ///
    /// <para><b>و<c>null</c> لَيسَت «هاتِف»</b>: هُناكَ يَقَعُ العَطَبُ
    /// كُلُّه. مَن يَرُدُّ افتِراضِيّاً عِندَ الفَراغِ يَبني مَتجَراً
    /// لا يَدخُلُه أَحَد؛ ومَن يَرُدُّ <c>null</c> يُجبِرُ المُنادِيَ
    /// على أَن يَقولَ العِلَّةَ لِلعَميل.</para></summary>
    public static string? Choose(string? requested, IReadOnlyList<string> offered)
    {
        if (offered.Count == 0) return null;
        var wanted = requested?.Trim();
        if (!string.IsNullOrEmpty(wanted) && offered.Contains(wanted)) return wanted;
        return offered[0];
    }

    /// <summary>الافتِراضيُّ لِمَتجَرٍ لَم يَختَر بَعد — <b>مُشتَقٌّ لا
    /// مَكتوب</b>.</summary>
    public static string? Default(IReadOnlyList<string> offered) => Choose(null, offered);
}
