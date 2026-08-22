namespace ACommerce.Templates.Customer.Marketplace.Services.Api;

/// <summary>خَرق واحِد في تَعريف مِفتاح API. <c>Code</c> مِفتاح ثابِت
/// لِلاختِبارات واللوغ، و<c>MessageAr</c> لِلمُراجِع البَشَريّ. نَفس
/// شَكل <c>CapabilityViolation</c> و<c>DealPatternViolation</c> —
/// القالِب المَرجِعيّ في القاعِدَة ٤.</summary>
public sealed record ApiKeyViolation(string Code, string MessageAr);

/// <summary>
/// <para><b>مَعجَم النِطاقات المُغلَق</b> — نِطاقانِ لا ثالِثَ لَهُما
/// اليَوم، وكُلُّ واحِدٍ مِنهُما <b>يَحرُس نُقطَةً حَيَّة</b>. وهذا
/// شَرطُ الدُخول لا زينَة: نِطاقٌ لا تَفحَصُه نُقطَة هو
/// <c>AllowCustomPattern</c> مِن جَديد — يُباع ولا يُفرَض (الخَطَر ٧
/// في وَثيقَة التَصميم).</para>
///
/// <para><b>ولِماذا مَعجَمٌ لا سِلسِلَةٌ حُرَّة</b> (القاعِدَة ٤):
/// النِطاقُ يُكتَب مَرَّةً عِندَ الإصدار ويُقرَأ عِندَ كُلّ طَلَب.
/// خَطَأٌ مَطبَعيّ في الكِتابَة يُنتِج مِفتاحاً <b>لا يَعمَل أَبَداً</b>
/// ويَرُدّ ‏403 بِلا سَبَبٍ ظاهِر — وهذا بِعَينِه الطَرَف الَّذي
/// تَرَكَه <c>PermissionCatalog</c> مَفتوحاً وأَغلَقَه
/// <c>CapabilityCatalog</c>.</para>
/// </summary>
public static class ApiScopeCatalog
{
    /// <summary>قِراءَةُ الصَفقات — <c>GET /api/v1/deals</c> و
    /// <c>GET /api/v1/deals/{id}</c>.</summary>
    public const string DealsRead = "deals:read";

    /// <summary>تَحريكُ صَفقَة أَو إلغاؤُها — <c>POST …/advance</c> و
    /// <c>POST …/cancel</c>.</summary>
    public const string DealsWrite = "deals:write";

    /// <summary>النِطاقانِ بِتَرتيبٍ أَبجَدِيّ.</summary>
    public static readonly IReadOnlyList<string> All =
        new[] { DealsRead, DealsWrite };

    public static bool Contains(string scope) => All.Contains(scope, StringComparer.Ordinal);

    /// <summary>يَرمي عِندَ الخَرق — لِمَواضِع التَركيب (تَسجيلُ
    /// النُقطَة) حَيثُ الخَطَأ يَجِب أَن يُفشِل <b>الإقلاع</b> لا
    /// طَلَباً واحِداً في اللَيل. نَفس حيلَة
    /// <c>CapabilityCatalog.Require</c> حَرفاً.</summary>
    public static string Require(string scope)
    {
        if (!Contains(scope))
            throw new ArgumentException(
                $"النِطاق «{scope}» خارِج مَعجَم ApiScopeCatalog. " +
                $"المَعجَم: {string.Join("، ", All)}.", nameof(scope));
        return scope;
    }
}

/// <summary>
/// <para><b>مِفتاح API وارِد — الوَثيقَة.</b> السِرُّ لا يُخَزَّن؛
/// يُخَزَّن <see cref="SecretHash"/> وَحدَه، فَتَسريبُ قاعِدَة
/// البَيانات لا يُعطي مِفتاحاً صالِحاً.</para>
///
/// <para><b>والوَثيقَةُ عامَّة (‏<c>SingleTenanted</c>) لا
/// مُتَعَدِّدَة الإيجار، وهذا شَرطٌ لا تَحسين</b>: البَحثُ عَنها
/// يَقَع <b>قَبلَ</b> أَن يُعرَف المُستَأجِر — لا مَعلومَ في الطَلَب
/// إلّا <see cref="Id"/>. ولَو كانَت مَحصورَةً بِـ<c>tenant_id</c>
/// لَاستَحالَ تَحميلُها بِلا مَعرِفَةِ ما نَبحَث عَنه أَصلاً. نَفس
/// استِثناء وَثيقَة <c>Tenant</c> ولِنَفس السَبَب.</para>
///
/// <para><b>والمُستَأجِرُ يُشتَقّ مِن الاعتِماد ولا يُقبَل مِن
/// الطَلَب أَبَداً</b> (‏§٣٫٦): <see cref="TenantSlug"/> مَكتوبٌ هُنا
/// عِندَ الإصدار، وكُلُّ جَلسَةٍ بَعدَه تُفتَح بِه — لا بِمَقطَعِ
/// مَسارٍ ولا رَأسٍ ولا حَقلٍ في الجِسم. وهذا حَرفاً ما يَفعَلُه
/// التوكن الحاليّ إذ يَربِط السلاج داخِلَ التَوقيع.</para>
/// </summary>
public sealed class ApiKeyDocument
{
    /// <summary>‏<c>keyId</c> — المَقطَعُ الأَوسَط مِن
    /// <c>wsl_{keyId}_{secret}</c>، وهو <b>مُعَرِّف الوَثيقَة</b>.
    /// فَالبَحثُ تَحميلٌ بِالمِفتاح لا مَسحٌ لِكُلّ الصُفوف،
    /// و<b>الإبطالُ حَذفُ صَفٍّ أَو تَعطيلُه</b> لا تَدويرُ سِرٍّ
    /// عامّ يُخرِج كُلّ المُستَخدِمين (عَطَب <c>TokenSecret</c>
    /// ‏§٢٫٣).</summary>
    public string Id { get; set; } = "";

    public string TenantSlug { get; set; } = "";

    /// <summary>تَسمِيَةٌ يَكتُبُها المالِك لِيَعرِفَ ما يُبطِلُه.</summary>
    public string Name { get; set; } = "";

    /// <summary>‏<c>SHA-256</c> لِلسِرّ، بِست عَشرِيّ. لا يُخَزَّن
    /// السِرُّ نَفسُه في أَيّ مَوضِع.</summary>
    public string SecretHash { get; set; } = "";

    /// <summary>نِطاقاتٌ مِن <see cref="ApiScopeCatalog"/> حَصراً.</summary>
    public List<string> Scopes { get; set; } = new();

    /// <summary>
    /// <para><b>الفاعِلُ الَّذي يَتَكَلَّم بِهذا المِفتاح.</b> والحاجَةُ
    /// إلَيه مَقيسَة لا مُفتَرَضَة: <c>DealsService.AdvanceAsync</c>
    /// يَفحَص أَنّ الفاعِلَ مُخَوَّلٌ بِالمَرحَلَة
    /// (‏<c>initiator</c>/<c>counterparty</c>/<c>either</c>)، وجَدوَلُ
    /// الفاعِلين في <c>DealPatternCatalog.DefaultActors</c> <b>لا
    /// يُسنِد مَرحَلَةً واحِدَة إلى <c>platform</c></b> — فَمِفتاحٌ
    /// بِلا فاعِلٍ مُعَيَّن لا يَستَطيع تَحريكَ شَيء.</para>
    ///
    /// <para>فَالناقِلُ يَأخُذ مِفتاحاً مَربوطاً بِحِسابِه، ويُحَرِّك
    /// الصَفقات الَّتي هُوَ طَرَفٌ فيها — <b>والتَخويلُ يَبقى حَيثُ
    /// هُوَ</b>، في الخِدمَة، بِلا سَطرٍ مُكَرَّر.</para>
    /// </summary>
    public Guid ActorUserId { get; set; }

    /// <summary>اسمُ الفاعِل كَما يُكتَب في <c>Timeline</c> الصَفقَة.</summary>
    public string ActorName { get; set; } = "";

    /// <summary><c>active</c> أَو <c>revoked</c>.</summary>
    public string Status { get; set; } = StatusActive;

    public const string StatusActive  = "active";
    public const string StatusRevoked = "revoked";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>الانتِهاء — <c>null</c> يَعني بِلا انتِهاء. الحَقلانِ
    /// (‏هذا و<see cref="Status"/>) هُما الغائِبانِ عَن التوكن
    /// الحاليّ (‏§٢٫٣).</summary>
    public DateTime? ExpiresAt { get; set; }

    public DateTime? RevokedAt { get; set; }

    /// <summary>مُستَخدِمُ الاستوديو الَّذي أَصدَرَه — لِلتَدقيق.</summary>
    public Guid IssuedByStudioUserId { get; set; }

    public bool HasScope(string scope) => Scopes.Contains(scope, StringComparer.Ordinal);
}

/// <summary>
/// <para><b>بَوّابَةُ تَعريفِ المِفتاح</b> — دالّاتٌ نَقِيَّة: لا
/// قاعِدَةَ بَيانات، ولا وَقتَ إلّا مُمَرَّراً، ولا عَشوائيَّة.
/// والشَكلُ مُوَحَّد: <c>Validate</c> تُعيد قائِمَةَ خُروق،
/// و<c>IsValid</c> تَختَصِرُها — ولِكُلّ رَمزٍ اختِبارٌ مُوجِبٌ
/// وسالِب (القاعِدَة ٤).</para>
/// </summary>
public static class ApiKeyValidator
{
    /// <summary>طَلَبُ إصدار — ما يَكتُبُه المالِك في الشاشَة.</summary>
    public sealed record IssueRequest(
        string Name, Guid ActorUserId, string ActorName,
        IReadOnlyList<string> Scopes, int? ExpiresInDays);

    public static IReadOnlyList<ApiKeyViolation> Validate(IssueRequest r)
    {
        var v = new List<ApiKeyViolation>();

        if (string.IsNullOrWhiteSpace(r.Name))
            v.Add(new("name_empty", "اسمُ المِفتاح فارِغ — والاسمُ هو ما يُعرَف بِه عِندَ الإبطال."));
        else if (r.Name.Trim().Length > 60)
            v.Add(new("name_too_long", "اسمُ المِفتاح أَطوَلُ مِن ‏60 مِحرَفاً."));

        if (r.ActorUserId == Guid.Empty)
            v.Add(new("actor_required",
                "المِفتاحُ بِلا فاعِل — ولا مَرحَلَةَ صَفقَةٍ يُحَرِّكُها فاعِلُ المَنَصَّة، " +
                "فَمِفتاحٌ بِلا فاعِلٍ مُعَيَّن لا يُحَرِّك شَيئاً."));

        if (r.Scopes.Count == 0)
            v.Add(new("scopes_empty", "المِفتاحُ بِلا نِطاق — لا يَبلُغ نُقطَةً واحِدَة."));

        foreach (var s in r.Scopes.Distinct(StringComparer.Ordinal))
            if (!ApiScopeCatalog.Contains(s))
                v.Add(new("scope_out_of_vocabulary",
                    $"النِطاق «{s}» خارِج المَعجَم. المَعجَم: {string.Join("، ", ApiScopeCatalog.All)}."));

        if (r.ExpiresInDays is { } d && d <= 0)
            v.Add(new("expiry_not_positive", "مُدَّةُ الصَلاحِيَّة يَجِب أَن تَكونَ يَوماً فَأَكثَر."));

        return v;
    }

    public static bool IsValid(IssueRequest r) => Validate(r).Count == 0;

    /// <summary>
    /// <para><b>شَكلُ المِفتاح المَعروض</b>:
    /// <c>wsl_{keyId}_{secret}</c> — ثَلاثَةُ مَقاطِع بِالضَبط،
    /// والمَقطَعانِ الأَخيرانِ <b>ست عَشرِيّانِ صِغار</b>. والحَرفِيَّةُ
    /// الست عَشرِيَّة مَقصودَة: <c>base64url</c> يَحوي <c>_</c>
    /// فَيَكسِر الفَصلَ بِالشَرطَة السُفلِيَّة نَفسِها.</para>
    /// </summary>
    public static (string KeyId, string Secret)? ParsePresented(string? presented)
    {
        if (string.IsNullOrWhiteSpace(presented)) return null;
        var parts = presented.Trim().Split('_');
        if (parts.Length != 3) return null;
        if (!string.Equals(parts[0], ApiKeyFormat.Prefix, StringComparison.Ordinal)) return null;
        if (!IsLowerHex(parts[1], ApiKeyFormat.KeyIdHexLength)) return null;
        if (!IsLowerHex(parts[2], ApiKeyFormat.SecretHexLength)) return null;
        return (parts[1], parts[2]);
    }

    private static bool IsLowerHex(string s, int length)
    {
        if (s.Length != length) return false;
        foreach (var c in s)
            if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))) return false;
        return true;
    }
}

/// <summary>أَطوالُ المِفتاح وبادِئَتُه — مَوضِعٌ واحِد يَقرَؤُه
/// المُصدِرُ والمُحَلِّل، فَلا يَنجَرِفانِ.</summary>
public static class ApiKeyFormat
{
    public const string Prefix = "wsl";

    /// <summary>‏8 بايتات = ‏16 مِحرَفاً ست عَشرِيّاً.</summary>
    public const int KeyIdBytes = 8;
    public const int KeyIdHexLength = KeyIdBytes * 2;

    /// <summary>‏32 بايتاً = ‏64 مِحرَفاً ست عَشرِيّاً.</summary>
    public const int SecretBytes = 32;
    public const int SecretHexLength = SecretBytes * 2;
}
