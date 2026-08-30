using System.Text.Json.Serialization;

namespace ACommerce.Platform.Providers;

/// <summary>
/// <para><b>مَعجَمُ القُدُرات</b> — تِسعٌ بِأَسماءِ الواجِهاتِ التِسعِ
/// القائِمَة في العُدَد: <c>IPaymentProvider</c>, <c>IOtpChannel</c>,
/// <c>IEmailOtpChannel</c>, <c>INafathChannel</c>, <c>IMapsProvider</c>,
/// <c>IDeliveryProvider</c>, <c>IFileStorage</c>,
/// <c>INotificationChannel</c>, <c>ICache</c>.</para>
///
/// <para><b>ولا إحالَةَ نَوعٍ واحِدَة</b>: المَعجَمُ أَسماءٌ نَصِّيَّة،
/// فَلا تَجُرّ هذِه المَكتَبَةُ عُدَّةً واحِدَة ويَجوز لِأَيّ طَبَقَةٍ
/// أَن تُحيلَها. والرِباطُ بَينَ الاسمِ والواجِهَةِ مَفحوصٌ نَصِّيّاً في
/// <c>ProviderSelectionCharacterizationTests</c>.</para>
/// </summary>
public static class ProviderCapabilities
{
    public const string Payments      = "payments";
    public const string SmsOtp        = "sms_otp";
    public const string EmailOtp      = "email_otp";
    public const string Nafath        = "nafath";
    public const string Maps          = "maps";
    public const string Delivery      = "delivery";
    public const string Files         = "files";
    public const string Notifications = "notifications";
    public const string Cache         = "cache";

    public static readonly IReadOnlyList<string> All = new[]
    {
        Payments, SmsOtp, EmailOtp, Nafath, Maps,
        Delivery, Files, Notifications, Cache,
    };

    private static readonly HashSet<string> Set = new(All, StringComparer.Ordinal);

    public static bool Contains(string capability) => Set.Contains(capability);

    public static string Require(string capability)
    {
        if (!Contains(capability))
            throw new ArgumentException(
                $"القُدرَة «{capability}» خارِج مَعجَم ProviderCapabilities. " +
                $"المَعجَم: {string.Join("، ", All)}.", nameof(capability));
        return capability;
    }
}

/// <summary>
/// <para><b>حاوِيَةُ التَوطين — خَريطَةٌ مَفتوحَةٌ بِمَفاتيحِ لُغات، لا
/// حَقلا <c>Ar</c>/<c>En</c></b> (القاعِدَة ١١). واللُغَةُ الثالِثَةُ
/// تَعمَل بِلا لَمسِ سَطرٍ واحِدٍ هُنا؛ والعَرَبِيَّةُ إلزامِيَّةٌ
/// بِالمُصادَقَة.</para>
/// </summary>
public static class ProviderText
{
    public const string Arabic = "ar";

    public static readonly IReadOnlyDictionary<string, string?> Empty =
        new Dictionary<string, string?>(0, StringComparer.Ordinal);

    /// <summary>نَصُّ اللُغَةِ المَطلوبَة، وإلّا فَالعَرَبِيَّة —
    /// والسُقوطُ إلَيها لا إلى المِفتاحِ الخام.</summary>
    public static string Get(IReadOnlyDictionary<string, string?> text, string lang)
    {
        if (text.TryGetValue(lang, out var v) && !string.IsNullOrWhiteSpace(v)) return v;
        return text.TryGetValue(Arabic, out var ar) && !string.IsNullOrWhiteSpace(ar) ? ar : "";
    }

    public static bool HasArabic(IReadOnlyDictionary<string, string?> text) =>
        text.TryGetValue(Arabic, out var ar) && !string.IsNullOrWhiteSpace(ar);
}

/// <summary>حَقلٌ واحِدٌ يَملَؤُه المُستَأجِر.</summary>
public sealed record ProviderFieldDefinition
{
    public string Code { get; init; } = "";

    /// <summary>نَوعُ الاعتِمادِ لِهذا الحَقلِ وَحدَه — و<b>نَوعُ الرَبطِ
    /// هُوَ أَعلى أَنواعِ حُقولِه</b>.</summary>
    public string Kind { get; init; } = CredentialKinds.None;

    public IReadOnlyDictionary<string, string?> Label { get; init; } = ProviderText.Empty;

    public bool IsRequired { get; init; }

    /// <summary>نَمَطٌ اختِيارِيٌّ يُفحَص بِه المُدخَل
    /// (مِثل <c>^pk_(live|test)_</c>).</summary>
    public string? Pattern { get; init; }

    /// <summary>سياجُ المُضيفين لِحَقلٍ رابِط — <b>إلزامِيٌّ لِلرابِط
    /// ومَمنوعٌ عَلى السِرّ</b>.</summary>
    public IReadOnlyList<string> HostAllowlist { get; init; } = [];
}

/// <summary>اعتِمادُ المُزَوِّد: نَوعُه، وحُقولُه.</summary>
public sealed record ProviderCredentialDefinition
{
    public string Kind { get; init; } = CredentialKinds.None;
    public IReadOnlyList<ProviderFieldDefinition> Fields { get; init; } = [];
}

/// <summary>نُقطَةُ الوارِدِ المُعلَنَة — و<c>null</c> يَعني «لا وارِدَ
/// لِهذا المُزَوِّد».</summary>
public sealed record ProviderWebhookDefinition
{
    public string Path { get; init; } = "";
    public string Verify { get; init; } = "";
}

/// <summary>
/// <para><b>تَعريفُ مُزَوِّدٍ — مُواطِنٌ رابِعٌ في عائِلَةِ مِلَفّاتِ
/// السِياسَة</b> بَعدَ <c>*.role.json</c> و<c>*.plan.json</c> والثيم:
/// مَورِدٌ مَضمون + فِهرِس + <c>UnmappedMemberHandling.Disallow</c> +
/// مُصادِقٌ بِرُموزٍ ثابِتَة.</para>
///
/// <para><b>وفَرقانِ يُعلَنانِ ولا يُبتَلَعان</b> (القاعِدَة ١٥):</para>
/// <para>‏(أ) <b>لا يَستَعمِل <c>ApprovalFlow</c></b>. حالَتُه
/// <c>approved</c> نِهائِيَّةٌ مُعلَنَة، وذلكَ يَصلُح لِدَورٍ أَو ثيم
/// ولا يَصلُح لِاعتِماد: المِفتاحُ المَسحوبُ يَجِب أَن يَتَوَقَّفَ
/// <b>الآن</b>. فَدَورَةُ حَياةِ الرَبطِ مُستَعارَةٌ مِن
/// <c>ApiKeyDocument.StatusActive|StatusRevoked</c> — أُنبوبٌ قائِمٌ لا
/// رابِع (القاعِدَة ٨).</para>
/// <para>‏(ب) <b>المُستَأجِرُ لا يُؤَلِّف تَعريفَ مُزَوِّد</b>، بِخِلافِ
/// الأَدوار. التَعريفُ كاتالوجُ مَنَصَّةٍ مَضمون، والمُستَأجِرُ يَختار
/// سلاجاً مِنه ويَملَأ حُقولاً. مُستَأجِرٌ يَكتُب
/// <c>"kind": "platform_key"</c> كانَ سَيَصرِف مِن جَيبِنا.</para>
/// </summary>
public sealed record ProviderDefinition
{
    public string Slug { get; init; } = "";

    public string Capability { get; init; } = "";

    public IReadOnlyDictionary<string, string?> Label { get; init; } = ProviderText.Empty;
    public IReadOnlyDictionary<string, string?> Description { get; init; } = ProviderText.Empty;

    public string? DocsUrl { get; init; }

    /// <summary><b>حَقلٌ يُصادَق، لا بَندٌ في عَقد</b>: مُستَأجِرٌ يَبيع
    /// مُحتَوىً رَقَمِيّاً يُسقِط إعفاءَ الدَفعِ لِلبِناءِ المُشتَرَكِ
    /// كُلِّه — آبِل تُعفي السِلَعَ المادِّيَّةَ بِـ«must» في
    /// ‏3.1.3(e)، وPlay تُعفي «physical goods/services».</summary>
    public bool PhysicalGoodsOnly { get; init; }

    public ProviderCredentialDefinition Credential { get; init; } = new();

    public ProviderWebhookDefinition? Webhook { get; init; }

    /// <summary>كَيفَ يَسحَبُ صاحِبُ المَتجَرِ الاعتِمادَ مِن عِندِ
    /// المُزَوِّدِ نَفسِه — نَصٌّ يُقرَأ، لِأَنّ سَحبَ الرَبطِ عِندَنا
    /// لا يُبطِلُ ما عِندَه.</summary>
    public IReadOnlyDictionary<string, string?> Revocation { get; init; } = ProviderText.Empty;

    /// <summary>
    /// <para><b>أَيَربِطُه مُستَأجِرٌ مِن شاشَتِه؟</b> — والجَوابُ
    /// مُشتَقٌّ مِن البَياناتِ لا مِن حَقلٍ يُخترَع:</para>
    /// <para>• <c>platform_key</c> <b>لا</b> — يَصرِف مِن جَيبِنا،
    /// فَنُقطَتُه تُعلِن <c>PlatformAdminGuard</c>.</para>
    /// <para>• <c>none</c> <b>لا</b> — لا شَيءَ يَحمِلُه المُستَأجِر،
    /// فَلا شَيءَ يُربَط: هذا المُزَوِّدُ <b>هُوَ</b> افتِراضُ
    /// المَنَصَّةِ القائِمُ اليَوم، ووُجودُ مِلَفِّه وَصفٌ لِما هُوَ
    /// قائِمٌ (بُرهانُ التَكافُؤِ الصِفريّ) لا عَرضٌ لِلاختِيار.</para>
    /// </summary>
    [JsonIgnore]
    public bool IsTenantBindable =>
        Credential.Kind is not (CredentialKinds.None or CredentialKinds.PlatformKey);

    /// <summary>أَعلى نَوعٍ يُعلِنُه حَقلٌ مِن حُقولِه — وهُوَ الحَدُّ
    /// الأَدنى المَقبولُ لِنَوعِ الرَبط.</summary>
    [JsonIgnore]
    public string HighestFieldKind
    {
        get
        {
            var best = CredentialKinds.None;
            foreach (var f in Credential.Fields)
                if (CredentialKinds.Rank(f.Kind) > CredentialKinds.Rank(best)) best = f.Kind;
            return best;
        }
    }
}
