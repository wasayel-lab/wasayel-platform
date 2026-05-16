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
