namespace ACommerce.Kit.Auth;

/// <summary>
/// مُستَخدِم — وَثيقَة Marten مُتَعَدِّدَة المُستَأجِرين. كلّ tenant
/// له مُستَخدِموه. الـ Id هو userId الفِعليّ (Guid).
/// </summary>
public sealed class User
{
    public Guid Id { get; set; }
    public string TenantSlug { get; set; } = "";
    public string Phone { get; set; } = "";
    public string? NationalId { get; set; }
    public string FullName { get; set; } = "مُستَخدِم جَديد";
    /// <summary>رابِط صورَة المَلَفّ الشَخصيّ (مِن IFileStorage). فارِغ =
    /// نَعرِض الحَرف الأَوَّل كَ avatar نائِب.</summary>
    public string? AvatarUrl { get; set; }
    public bool PhoneVerified { get; set; }
    public string Role { get; set; } = "user";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>سِمات بروفايل ديناميكِيَّة (Bio, Occupation, Address، …).
    /// مَفاتيحها تَطابِق <c>AttributeDefinition.Code</c> في DB المُستَورَد.
    /// تُحفَظ كَ JSON snapshot في عَمود مُنفَصِل.</summary>
    public Dictionary<string, string> AttributesJson { get; set; } = new();

    /// <summary>الدَور النَّشِط لِلمُستَخدِم داخِل المَتجَر (مَثَلاً
    /// "rider" أَو "driver"). فارِغ = المَتجَر بِلا أَدوار. القائِمَة
    /// المَسموحَة مِن <c>Tenant.Roles</c>.</summary>
    public string ActiveRole { get; set; } = "";

    /// <summary>خَصائِص بروفايل خاصَّة بِكُلّ دَور: مَفتاح خارِجيّ = roleSlug،
    /// قاموس داخِليّ بِنَفس شَكل <c>AttributesJson</c>. مَثَلاً: السائِق
    /// يَملأ vehicle_type/license/plate تَحت "driver"؛ الراكِب يَترُك
    /// "driver" فارِغاً ويَملأ "rider" إن كانَت لَه خَصائِص خاصَّة. الـ
    /// <c>AttributesJson</c> العامّ يَبقَى مُشتَرَكاً بَين كُلّ الأَدوار.</summary>
    public Dictionary<string, Dictionary<string, string>> RoleAttributesJson { get; set; } = new();

    /// <summary>مَركَز المَنطِقَة الجُغرافيّة الَّتي يَعمَل فيها (السائِق
    /// يَضبُطها لِيَفلتِر فقط المَشاوير في نِطاقها). 0,0 = غَير مَضبوط.</summary>
    public double AnchorLat { get; set; }
    public double AnchorLng { get; set; }

    /// <summary>نِصف قُطر العَمَل بِالكيلومِترات. 0 = بِلا حَدّ. مَثَلاً
    /// السائِق في صَنعاء قَد يَضبُطها عَلى 15 كم.</summary>
    public int RadiusKm { get; set; }

    public bool HasAnchor => AnchorLat != 0 || AnchorLng != 0;

    /// <summary>اشتِراكات Web Push لِلجِهاز الواحِد — كُلّ subscription
    /// مَفتاح لِجِهاز/مُتَصَفِّح مُعَيَّن. السيرفر يَستَخدِمها لِإرسال
    /// إشعار push عَلى مُستَوى نِظام التَّشغيل (يَعمَل حَتَّى لَو PWA
    /// مُغلَق).</summary>
    public List<PushSubscription> PushSubscriptions { get; set; } = new();

    /// <summary>تاريخ قَبول الشُروط والأَحكام. <c>null</c> = لَم يَقبَل
    /// بَعد. الـ TermsGate يُجبِر القَبول قَبل أَيّ إجراء يَكتُب بَيانات.</summary>
    public DateTime? AcceptedTermsAt { get; set; }

    /// <summary>إصدار الشُروط الَّتي قَبِلَها — لَو تَحَدَّثَت الشُروط نَرفَع
    /// <see cref="TermsPolicy.CurrentVersion"/> وَنُجبِر القَبول مَرَّة أُخرى.</summary>
    public int AcceptedTermsVersion { get; set; }
}

/// <summary>اشتِراك Web Push واحِد — جِهاز + مُتَصَفِّح. الـ Endpoint مِن
/// تَزويد المُتَصَفِّح، P256dh + Auth مَفاتيح ECDH/HMAC تُستَخدَم لِتَشفير
/// الـ payload.</summary>
public sealed class PushSubscription
{
    public string Endpoint { get; set; } = "";
    public string P256dh { get; set; } = "";
    public string Auth { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}

// ─── Events (للـ stream المُستَقِلّ "AuthAttempts") ──────────────────
public sealed record OtpRequested(
    Guid Id, string Phone, string CodeHash, string Channel, DateTime At);

public sealed record OtpVerified(
    Guid Id, Guid UserId, DateTime At);

public sealed record OtpFailed(
    Guid Id, string Reason, DateTime At);

public sealed record NafathRequested(
    Guid Id, string NationalId, string DisplayCode, DateTime At);

public sealed record NafathVerified(
    Guid Id, Guid UserId, DateTime At);

// ─── Commands ─────────────────────────────────────────────────────────
public sealed record RequestPhoneOtp(string Phone);
public sealed record VerifyPhoneOtp(string Phone, string Code);
public sealed record RequestNafath(string NationalId);
public sealed record VerifyNafath(string AttemptId, string NationalId);

// ─── Response shapes ──────────────────────────────────────────────────
public sealed record OtpRequestResult(string AttemptId, string DisplayCode, string Hint);
public sealed record AuthResult(Guid UserId, string FullName, string Phone, string Token, string Role);
public sealed record NafathPending(string AttemptId, string DisplayCode, int AutoVerifyInSeconds);
