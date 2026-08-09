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
}

/// <summary>قَناة OTP عَبر البَريد الإلِكترونيّ (SMTP، Mock، …). نَفس
/// تَجريد <see cref="IOtpChannel"/> بِالضَبط — المُختَلِف هو المُرسَل إلَيه
/// (بَريد بَدَل رَقم) لا آليَّة الرَمز: التَوليد والتَجزئَة والصَلاحيَّة
/// وحُدود المُعَدَّل كُلُّها مُشتَرَكَة في
/// <c>AuthHandlers</c>.</summary>
public interface IEmailOtpChannel
{
    /// <summary>اسم القَناة (للتَشخيص فقط).</summary>
    string ChannelName { get; }

    /// <summary>أَرسِل الكود للمُستَخدِم. يَرمي عند فَشَل الإرسال.</summary>
    Task SendOtpAsync(string email, string code, CancellationToken ct);

    /// <summary>الكود المُتَوَقَّع في وَضع التَطوير (لإظهاره في الواجهَة).
    /// <c>null</c> في الإنتاج.</summary>
    string? DevHintCode { get; }
}

/// <summary>قَناة تَحَقُّق نَفاذ (Nafath، Yakeen-Pro، Mock، …).</summary>
public interface INafathChannel
{
    string ChannelName { get; }

    /// <summary>بَدء طَلَب تَحَقُّق. يُعيد رَقم العَرض الذي يَختاره
    /// المُستَخدِم في تَطبيق نَفاذ، ومُهلَة المُحاولَة الـ auto-verify
    /// (للـ mock).</summary>
    Task<NafathStartResult> StartAsync(string nationalId, CancellationToken ct);

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
