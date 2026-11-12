using ACommerce.Platform.Shared.Metering;

namespace ACommerce.Kit.Auth;

// ─── Provider Contracts ───────────────────────────────────────────────
// المُزَوِّدون مَفصولون في مَكتَبات مُستَقِلَّة. الـ Server يَستَدعي
// عَبر الواجهَة، والتَطبيق يَختار التَنفيذ.

/// <summary>قَناة OTP عَبر الرَسائل القَصيرَة (Twilio، Unifonic، Mock، …).</summary>
public interface IOtpChannel
{
    /// <summary>اسم القَناة (للتَشخيص فقط).</summary>
    string ChannelName { get; }

    /// <summary>أَرسِل الكود للمُستَخدِم. يَرمي عند فَشَل الإرسال.</summary>
    Task SendOtpAsync(string phone, string code, CancellationToken ct);

    /// <summary>الكود المُتَوَقَّع في وَضع التَطوير (لإظهاره في الواجهَة).
    /// <c>null</c> في الإنتاج.</summary>
    string? DevHintCode { get; }

    /// <summary>
    /// <para><b>رِسالَةُ إشعارٍ لا رَمز</b> (‏2026-10-29، تَدقيقُ الموجَة ١١ A3) — نَصٌّ قَصيرٌ إلى جَوّالٍ مُتَحَقَّقٍ مِنه: «أُضيفَت وَسيلَةُ دُخولٍ إلى
    /// حِسابِك». كانَت القَناةُ لا تُرسِلُ إلّا رَمزاً، فَلا يُخطَرُ صاحِبُ الحِسابِ حينَ يُربَطُ بِه مُصادِقٌ (OWASP ASVS 2.2.3، NIST 800-63B).
    /// <b>القَناةُ الحَيَّةُ نَفسُها تُمَدّ</b> (القاعِدَة ٨) — لا تُحيا <c>INotificationChannel</c> ذاتُ التَنفيذِ الصِفريّ. يَرمي عِندَ الفَشَلِ كَأَخيه.</para>
    /// </summary>
    Task SendNoticeAsync(string phone, string text, CancellationToken ct);

    // ─── النِداءُ المَعدود (‏2026-11-12، الموجَة ١٣ W13-6·١١ — ADR-051 §١٨؛ التَصميم §6.1) ─────────────────────────────────────────
    // **`ProviderCall` أَوَّلَ الوُسَطاء**: المُنادي يَحمِلُ المَتجَرَ ومُعَرِّفَ الطَلَب، والقَناةُ تَسِمُه بِاسمِها وتُرسِلُه بِمُستَدعيها المَعدود — فَكُلُّ
    // مُنادٍ في `libs/` و`apps/` يُنادي هذَينِ لا أَخَوَيهِما أَعلاه (يَحرُسُه `MeterTestSeamTests`، G6c).
    // **والأَخَوانِ بِلا `ProviderCall` يَبقَيانِ لِسَبَبٍ مَقيس** (القاعِدَة ٣): قَنَواتُ الاختِبارِ المُزَيَّفَةُ تُنَفِّذُهُما — ومِنها مِلَفُّ تَوصيفٍ لا يُعَدَّلُ
    // حَرفُه (`LiveMeterJourneyFootprintCharacterizationProofTests`) — ومُحاكي التَطوير (`MockSmsChannel`) الَّذي لا يُغادِرُ العَمَلِيَّة. فَلِهذَينِ
    // تَنفيذٌ افتِراضِيٌّ يُمَرِّرُ إلَيهِما، ومَن يُغادِرُ العَمَلِيَّةَ (Twilio) يُنَفِّذُهُما هُوَ ويَعُدّ.

    /// <summary>أَرسِل الرَمزَ بِنِداءٍ مَعدود — <paramref name="call"/> بِوَحدَةِ <see cref="OutboundUnits.OtpSmsSend"/>.</summary>
    Task SendOtpAsync(ProviderCall call, string phone, string code, CancellationToken ct) => SendOtpAsync(phone, code, ct);

    /// <summary>أَرسِل الإشعارَ بِنِداءٍ مَعدود — <paramref name="call"/> بِوَحدَةِ <see cref="OutboundUnits.NoticeSmsSend"/>.</summary>
    Task SendNoticeAsync(ProviderCall call, string phone, string text, CancellationToken ct) => SendNoticeAsync(phone, text, ct);
}

/// <summary>قَناة OTP عَبر البَريد الإلِكترونيّ (SMTP، Mock، …). نَفس
/// تَجريد <see cref="IOtpChannel"/> بِالضَبط — المُختَلِف هو المُرسَل إلَيه
/// (بَريد بَدَل رَقم) لا آليَّة الرَمز: التَوليد والتَجزئَة والصَلاحيَّة
/// وحُدود المُعَدَّل كُلُّها مُشتَرَكَة في
/// <c>AuthHandlers</c>.</summary>
public interface IEmailOtpChannel
{
    // ─── النِداءُ المَعدود (‏2026-11-12، الموجَة ١٣ W13-6·١٢ — ADR-051 §١٨؛ التَصميم §6.1) ─────────────────────────────────────────
    // **`ProviderCall` أَوَّلَ الوُسَطاءِ في التَوقيعِ نَفسِه، فَنِسيانُه خَطَأُ بِناء** (القاعِدَة ٦): كانَ الفِعلانِ بِلا `ProviderCall` وصارا بِه، ولا أَخَ بِلا
    // `ProviderCall` يَبقى — بِخِلافِ `IOtpChannel` أَعلاه، لِسَبَبٍ مَقيس: لا مِلَفَّ تَوصيفٍ يُنَفِّذُ هذه الواجِهَة (قَنَواتُها المُزَيَّفَةُ في اختِباراتٍ عادِيَّةٍ
    // تَبِعَت التَوقيع، وتَوصيفُ الطَلَبِ `EmailRequestShapeCharacterizationTests` يَمُرُّ بِالأَبوابِ لا بِالفِعلَين). المُنادي يَحمِلُ المَتجَرَ ومُعَرِّفَ الطَلَب،
    // والقَناةُ تَسِمُه بِاسمِها.

    /// <summary>اسم القَناة (للتَشخيص فقط).</summary>
    string ChannelName { get; }

    /// <summary>أَرسِل الكود للمُستَخدِم. يَرمي عند فَشَل الإرسال.</summary>
    /// <remarks>‏(‏2026-11-12، W13-6·١٢) <paramref name="call"/> بِوَحدَةِ <see cref="OutboundUnits.OtpEmailSend"/>.</remarks>
    Task SendOtpAsync(ProviderCall call, string email, string code, CancellationToken ct);

    /// <summary>الكود المُتَوَقَّع في وَضع التَطوير (لإظهاره في الواجهَة).
    /// <c>null</c> في الإنتاج.</summary>
    string? DevHintCode { get; }

    /// <summary>
    /// <para><b>رِسالَةُ إشعارٍ لا رَمز</b> (‏2026-10-29، تَدقيقُ الموجَة ١١ A3) — عُنوانٌ ونَصٌّ عادِيٌّ (بِلا HTML) إلى بَريدٍ مُتَحَقَّقٍ مِنه. مُستَهلِكاها في
    /// وَقتِ التَشغيل: إشعارُ رَبطِ وَسيلَةٍ وحَذفِها (<c>AccountContacts</c>)، وبَريدُ مالِ صاحِبِ المَتجَر (CO3، تَيّارُ F3) — يَستَعمِلُها ولا يَمَسُّ العُدَّة.
    /// يَرمي عِندَ الفَشَلِ كَأَخيه.</para>
    /// </summary>
    /// <remarks>‏(‏2026-11-12، W13-6·١٢) <paramref name="call"/> بِوَحدَةِ <see cref="OutboundUnits.NoticeEmailSend"/>، ومُعَرِّفُ طَلَبِه مُعَرِّفُ الإشعارِ المَكتوبِ قَبلَه.</remarks>
    Task SendNoticeAsync(ProviderCall call, string email, string subject, string text, CancellationToken ct);
}

/// <summary>قَناة تَحَقُّق نَفاذ (Nafath، Yakeen-Pro، Mock، …).</summary>
public interface INafathChannel
{
    string ChannelName { get; }

    /// <summary>بَدء طَلَب تَحَقُّق. يُعيد رَقم العَرض الذي يَختاره
    /// المُستَخدِم في تَطبيق نَفاذ، ومُهلَة المُحاولَة الـ auto-verify
    /// (للـ mock).</summary>
    /// <remarks>‏(‏2026-11-12، الموجَة ١٣ W13-6·١٣ — ADR-051 §١٨؛ التَصميم §6.1) <paramref name="call"/> بِوَحدَةِ <see cref="OutboundUnits.IdentityNafathStart"/> أَوَّلَ
    /// الوُسَطاءِ في التَوقيعِ نَفسِه، ولا أَخَ بِدونِه — نِسيانُه خَطَأُ بِناء (القاعِدَة ٦)؛ وما مِن مِلَفِّ تَوصيفٍ يُنَفِّذُ هذه الواجِهَة (مَقيس: قَنَواتُها
    /// المُزَيَّفَةُ في أَربَعَةِ اختِباراتٍ عادِيَّةٍ تَبِعَت التَوقيع). المُنادي (بابُ البَدء) يَحمِلُ المَتجَرَ ومُعَرِّفَ نِداءٍ جَديد، والقَناةُ تَسِمُه بِاسمِها.
    /// والاستِطلاعُ (<see cref="IsApprovedAsync"/>) مُساعِدٌ لا يُعَدُّ فَتَوقيعُه كَما كان.</remarks>
    Task<NafathStartResult> StartAsync(ProviderCall call, string nationalId, CancellationToken ct);

    /// <summary>هَل المُستَخدِم وافَق على الطَلَب؟ يَرجَع false حتى يَنتَهي
    /// المُستَخدِم. في Mock: يَرُدّ true بَعد <c>AutoApproveAfter</c>.</summary>
    Task<bool> IsApprovedAsync(string attemptId, CancellationToken ct);
}

public sealed record NafathStartResult(
    string AttemptId,
    string DisplayCode,
    int AutoApproveInSeconds);

// ─── تَعداد قَنَوات دُخول المُستَأجِر ─────────────────────────────────
/// <summary>
/// القِيَم المُعتَمَدَة لِـ <c>Tenant.AuthChannel</c>. مَصدَر واحِد
/// لِلتَعداد: كانَ الشَّرط <c>!= "phone" &amp;&amp; != "nafath"</c> مَنسوخاً
/// في أَربَعَة مَواضِع (نَموذَجا الإدارَة ومَسارا الوَكيل)، فَكُلّ قيمَة
/// جَديدَة تُنسى في أَحَدِها تُبتَلَع صامِتَةً وتَرتَدّ إلى "phone".
/// </summary>
public static class AuthChannels
{
    public const string Phone  = "phone";
    public const string Nafath = "nafath";
    public const string Email  = "email";

    /// <summary>الافتِراضيّ حينَ تَكون القيمَة غائِبَة أَو غَير مَعروفَة.</summary>
    public const string Default = Phone;

    public static readonly IReadOnlyList<string> All = new[] { Phone, Nafath, Email };

    public static bool IsSupported(string? channel)
        => channel is not null && All.Contains(channel);

    /// <summary>يُعيد القَناة كَما هي إن كانَت مُعتَمَدَة، وإلّا
    /// <see cref="Default"/>.</summary>
    public static string NormalizeOrDefault(string? channel)
        => IsSupported(channel) ? channel! : Default;
}
