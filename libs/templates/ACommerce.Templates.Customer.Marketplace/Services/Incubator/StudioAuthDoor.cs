using ACommerce.Kit.Auth;

namespace ACommerce.Templates.Customer.Marketplace.Services.Incubator;

/// <summary>طَريقَةُ الدُخول إلى الاستوديو. اثنَتانِ لا ثالِثَة — نَفاذُ
/// لا مَوضِعَ لَه هُنا: بابُ الاستوديو بابُ **مُشرِفِ المَنَصَّةِ ورائِدِ
/// الأَعمال**، لا بابُ مُستَأجِرٍ يَختار قَناتَه.</summary>
public enum StudioAuthMethod { Phone, Email }

/// <summary>
/// بابُ جَلسَةِ الاستوديو — <b>جَدوَلٌ نَقِيٌّ بِلا I/O</b>، عَلى غِرار
/// <see cref="AuthChannelSelection"/> و<see cref="PlatformAdminGrant"/>.
///
/// <para><b>العِلَّةُ المَقيسَة (‏2026-08-23)</b>: ‏<c>cd43b366</c> جَعَلَ
/// قَنَواتِ المُستَأجِرينَ قَراراً بِالتَهيئَة وأَغلَقَ غِيابَها، لكِنّ
/// بابَ الاستوديو <b>لَم يَكُن يَمُرّ بِأَيّ قَناة</b>: صِفرُ إشارَةٍ إلى
/// <c>IOtpChannel</c>/<c>IEmailOtpChannel</c> في مُجَلَّد <c>Incubator</c>
/// كُلِّه، والتَحَقُّقُ مُقارَنَةٌ بِثابِتٍ <c>"123456"</c> بِلا شَرطِ
/// بيئَة. وبِما أَنّ هذا البابَ هُوَ <b>المَوضِعُ الوَحيد</b> الَّذي
/// يُنتِج جَلسَةَ مُشرِفِ مَنَصَّة، فَمَن يَعرِف هاتِفَ المالِكِ كانَ
/// يَدخُل مُشرِفَ مَنَصَّةٍ في الإنتاج. أَي أَنّ إصلاحَ الأَمنِ أَغلَقَ
/// أَبوابَ المُستَأجِرينَ وتَرَكَ بابَ المَنَصَّةِ مَفتوحاً.</para>
///
/// <para><b>ولِماذا جَدوَلٌ هُنا لا شَرطٌ في جِسمِ النُقطَة</b>: الحَدُّ
/// الَّذي لا يُقاس آليّاً يَنهار (القاعِدَة ٢). فَما يُعرَض وما يُرفَض
/// دالَّةٌ خالِصَةٌ تُقاس بِجَدوَل، والنُقطَةُ والصَفحَةُ أَثَرُها —
/// <b>ومِن جَدوَلٍ واحِدٍ مَعاً</b>، وإلّا عُرِضَ زِرٌّ يَقود إلى رَفض.</para>
/// </summary>
public static class StudioAuthDoor
{
    /// <summary>ما يَعرِضُه البابُ فِعلاً، مِن <b>تَسجيلِ القَنَواتِ
    /// نَفسِه</b> — والتَسجيلُ أَثَرُ <see cref="AuthChannelSelection.Decide"/>
    /// (‏<c>Program.cs</c>) وحارِسُ الإقلاعِ يَمنَع تَسَرُّبَ مُحاكٍ
    /// إلَيه. فَالإنتاجُ بِلا تَهيئَةٍ يُعطي قائِمَةً <b>فارِغَة</b>.</summary>
    public static IReadOnlyList<StudioAuthMethod> Offered(
        bool phoneChannelRegistered, bool emailChannelRegistered)
    {
        var list = new List<StudioAuthMethod>(2);
        if (phoneChannelRegistered) list.Add(StudioAuthMethod.Phone);
        if (emailChannelRegistered) list.Add(StudioAuthMethod.Email);
        return list;
    }

    /// <summary>الطَريقَةُ المُفَعَّلَةُ في الصَفحَة: المَطلوبَةُ إن كانَت
    /// مَعروضَة، وإلّا أَوَّلُ مَعروضَة، وإلّا <c>null</c> — فَلا نَموذَجَ
    /// يُعرَض. <b>لا زِرَّ يُؤَدّي إلى رَفضٍ حَيثُ يُمكِن إخفاؤُه</b>
    /// (القاعِدَة ١٢).</summary>
    public static StudioAuthMethod? Active(
        string? requested, IReadOnlyList<StudioAuthMethod> offered)
    {
        if (offered.Count == 0) return null;
        var parsed = Parse(requested);
        if (parsed is { } m && offered.Contains(m)) return m;
        return offered[0];
    }

    /// <summary>‏<c>"email"</c> ← بَريد، وما عَداها هاتِف. قيمَةٌ مَجهولَةٌ
    /// لا تَفتَح باباً — تَرتَدُّ إلى <c>null</c> ويَحسِمُها
    /// <see cref="Active"/> بِما هُوَ مَعروضٌ فِعلاً.</summary>
    public static StudioAuthMethod? Parse(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "email" => StudioAuthMethod.Email,
        "phone" => StudioAuthMethod.Phone,
        _ => null
    };

    /// <summary>الاسمُ في الـquery — يُكتَب مَرَّةً هُنا لا في كُلّ رابِط.</summary>
    public static string Slug(StudioAuthMethod method)
        => method == StudioAuthMethod.Email ? "email" : "phone";

    /// <summary>رَمزُ الخَطَإ حينَ لا قَناةَ مُسَجَّلَة — نَفسُ لاحِقَةِ
    /// <c>*_unavailable</c> الَّتي يَرُدُّها بابُ المُستَأجِر، ونَفسُ
    /// المَبدَإ: <b>الفَشَلُ المُغلَقُ يُقال في الرَدّ، لا يُترَك
    /// لِـ500</b>.</summary>
    public static string UnavailableError(StudioAuthMethod method)
        => method == StudioAuthMethod.Email ? "email_unavailable" : "phone_unavailable";

    /// <summary>نَوعُ المُحاوَلَة في <c>AuthHandlers.Attempts</c> —
    /// المَخزَنُ نَفسُه بِمُستَأجِر <c>_studio</c>.</summary>
    public static ACommerce.Kit.Auth.Server.AuthHandlers.AuthKind Kind(StudioAuthMethod method)
        => method == StudioAuthMethod.Email
            ? ACommerce.Kit.Auth.Server.AuthHandlers.AuthKind.EmailOtp
            : ACommerce.Kit.Auth.Server.AuthHandlers.AuthKind.PhoneOtp;
}
