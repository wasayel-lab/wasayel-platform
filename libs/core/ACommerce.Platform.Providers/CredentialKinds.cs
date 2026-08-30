namespace ACommerce.Platform.Providers;

/// <summary>
/// <para><b>المَعجَمُ المُغلَقُ لِأَنواعِ الاعتِماد</b> — والفارِقُ
/// الفاصِلُ بَينَ نَوعَينِ لَيسَ الاسمَ بَل ثَلاثَةَ أَعمِدَة: <b>ما
/// يُخَزَّن، أَيُعرَض، أَيُسترَجَع</b>. ولِذلكَ لا يُطوى
/// <c>shared_secret</c> في <c>secret_key</c> (الأَوَّلُ يُتَحَقَّق بِه
/// مِن وارِد ولا يُطلَب بِه شَيء، والثاني يُطلَب بِه — والفَرقُ نِصفُ
/// قُطرِ انفِجارٍ مَقيس)، ولا <c>issued_secret</c> في
/// <c>shared_secret</c> (الأَوَّلُ نُوَلِّدُه فَنُخَزِّن تَجزِئَتَه،
/// والثاني يُوَلِّدُه المُزَوِّدُ فَلا بُدَّ مِن استِرجاعِه).</para>
///
/// <para><b>والمَعجَمُ المُلزِمُ هُوَ <see cref="All"/> لا الأَسماءُ
/// التِسعَة.</b> هذا هُوَ القَرارُ الَّذي يَحرُس المَوجَةَ الأولى مِن
/// أَن تَبيعَ ما لا تَفرِض: نَوعٌ لا يُعلِنُه مِلَفُّ مُزَوِّدٍ حَيّ
/// <b>لا يَدخُل المَعجَم</b> — وإلّا صارَ <c>AllowCustomPattern</c> مِن
/// جَديد (خَصيصَةٌ تُعرَض في بِطاقَةِ الأَسعار وصِفرُ مَوضِعٍ
/// يَفحَصُها، القاعِدَة ١٢). والانضِباطُ مَنقولٌ حَرفاً عَن
/// <c>Capability.SourceRef</c> في <c>CapabilityCatalog</c>.</para>
///
/// <para><b>وأَثَرُه فَشَلٌ مُغلَقٌ لا وَعد</b>: لا خِزانَةَ في هذِه
/// المَوجَة، فَـ<c>secret_key</c> و<c>shared_secret</c>
/// و<c>credential_file</c> و<c>issued_secret</c> و<c>published_key</c>
/// و<c>delegated_grant</c> <b>خارِجَ المَعجَمِ المُلزِم</b> — فَمِلَفُّ
/// تَعريفٍ يُعلِن أَحَدَها <b>يُفشِل الإقلاع</b> بِرَمزِه. أَي أَنّ
/// «لا سِرَّ يُخَزَّن قَبلَ أَن تُبنى خِزانَتُه» جُملَةٌ يَفرِضُها
/// الكود، لا جُملَةٌ في تَقرير.</para>
/// </summary>
public static class CredentialKinds
{
    // ─── الأَسماءُ التِسعَة — التَصنيفُ كامِلاً ───────────────────────

    /// <summary>لا شَيءَ عِندَ السُكون. مُزَوِّدُه المَقيس:
    /// <c>MockPayments</c>, <c>MockMaps</c>, <c>MockDelivery</c>,
    /// <c>LocalFileStorage</c> — مُسَجَّلَةٌ اليَومَ بِلا شَرط.</summary>
    public const string None = "none";

    /// <summary>نَصٌّ صَريح، <b>يُعرَض</b> (رابِطٌ عامّ) ويُسترَجَع.
    /// مُزَوِّدُه المَقيس: فَواتيرُ مُيَسِّر.</summary>
    public const string HostedLink = "hosted_link";

    /// <summary>نَصٌّ صَريح يُصَيَّر داخِلَ HTML. لا خِزانَةَ لَه —
    /// ولا مِلَفَّ يُعلِنُه اليَوم.</summary>
    public const string PublishedKey = "published_key";

    /// <summary>مُعَرِّفٌ صَريحٌ لَيسَ سِرّاً (حِسابُ خِدمَةٍ يُدعى في
    /// كونسولِ المُستَأجِر). لا مِلَفَّ يُعلِنُه اليَوم.</summary>
    public const string DelegatedGrant = "delegated_grant";

    /// <summary>‏SHA-256 وَحدَه، يُعرَض مَرَّةً عِندَ الإصدارِ ولا
    /// يُسترَجَع أَبَداً. نَظيرُه القائِم <c>ApiKeyDocument.SecretHash</c>
    /// — و<b>أُنبوبُه قائِمٌ خارِجَ هذا المَعجَم</b>، فَلا مِلَفَّ
    /// مُزَوِّدٍ يُعلِنُه.</summary>
    public const string IssuedSecret = "issued_secret";

    /// <summary>مُشَفَّرٌ ولا يُعرَض ويُسترَجَع (سِرُّ webhook).
    /// يَنتَظِر الخِزانَة.</summary>
    public const string SharedSecret = "shared_secret";

    /// <summary>مُشَفَّر، يُعرَض مِنه آخِرُ أَربَعَةِ مَحارِف.
    /// يَنتَظِر الخِزانَة.</summary>
    public const string SecretKey = "secret_key";

    /// <summary>كُتلَةٌ مُشَفَّرَة، يُعرَض اسمُها وبَصمَتُها.
    /// يَنتَظِر الخِزانَة.</summary>
    public const string CredentialFile = "credential_file";

    /// <summary><b>لَيسَ في خِزانَةِ المُستَأجِرِ إطلاقاً</b> — سِرُّ
    /// المَنَصَّة (‏Fly). ونَوعٌ لا حالَةُ غِياب، لِأَنَّه
    /// <b>يُكَلِّفُنا مالاً</b>: فَيَحمِل حِصَّةً، وتُعلِن نُقطَةُ
    /// كِتابَتِه <c>PlatformAdminGuard</c> لا <c>TenantAdminGuard</c>.
    /// مُزَوِّداهُ المَقيسان: <c>Payments__PayPal__*</c> و
    /// <c>Payments__Paddle__*</c> — قائِمانِ اليَوم.</summary>
    public const string PlatformKey = "platform_key";

    // ─── المَعجَمُ المُلزِم — ما يُعلِنُه مِلَفٌّ حَيّ اليَوم ─────────

    /// <summary><b>المَعجَمُ الَّذي يُفرَض.</b> ثَلاثَةٌ، ولِكُلٍّ
    /// مِلَفُّ تَعريفٍ مَشحونٌ يُعلِنُه — وذلكَ مَفحوصٌ آلِيّاً
    /// (‏<c>ProviderDefinitionCatalogTests</c>).</summary>
    public static readonly IReadOnlyList<string> All =
        new[] { None, HostedLink, PlatformKey };

    /// <summary><b>ما هُوَ خارِجَ المَعجَمِ عَمداً</b> — ولِكُلٍّ سَبَبٌ
    /// واحِد: خِزانَتُه لَم تُبنَ بَعد (أَو أُنبوبُه قائِمٌ في مَوضِعٍ
    /// آخَر). ويُفحَص آلِيّاً أَنّ <b>صِفرَ</b> مِلَفٍّ يُعلِن واحِداً
    /// مِنها — فَالقائِمَةُ تَنكَمِش بِقَرارٍ مَرئيّ ولا تَتَسَلَّل
    /// بِمِلَفّ.</summary>
    public static readonly IReadOnlyList<string> NotYetInVocabulary =
        new[] { PublishedKey, DelegatedGrant, IssuedSecret, SharedSecret, SecretKey, CredentialFile };

    private static readonly HashSet<string> Set = new(All, StringComparer.Ordinal);

    public static bool Contains(string kind) => Set.Contains(kind);

    /// <summary>نُسخَةٌ حَرفِيَّةٌ مِن <c>ApiScopeCatalog.Require</c>:
    /// المَعجَمُ يَرمي عِندَ الخُروجِ عَنه، ولا يَبتَلِع.</summary>
    public static string Require(string kind)
    {
        if (!Contains(kind))
            throw new ArgumentException(
                $"نَوعُ الاعتِماد «{kind}» خارِج مَعجَم CredentialKinds. " +
                $"المَعجَم: {string.Join("، ", All)}.", nameof(kind));
        return kind;
    }

    // ─── التَرتيبُ بِحَسَبِ ما تُلزِمُ بِه الخِزانَة ──────────────────
    //
    // ‏«نَوعُ الرَبطِ هُوَ أَعلى أَنواعِ حُقولِه» — وهذا هُوَ السُلَّم
    // الَّذي يُقاس بِه «أَعلى». والتَرتيبُ لَيسَ ذَوقاً: كُلُّ دَرَجَةٍ
    // تُلزِمُ الخِزانَةَ بِأَكثَرَ مِمّا تُلزِمُها بِه الَّتي تَحتَها.

    private static readonly Dictionary<string, int> Ranks = new(StringComparer.Ordinal)
    {
        [None]           = 0,   // لا شَيء
        [HostedLink]     = 1,   // نَصٌّ صَريحٌ عامّ
        [PublishedKey]   = 2,   // نَصٌّ صَريحٌ يُصَيَّر
        [DelegatedGrant] = 3,   // مُعَرِّفٌ صَريحٌ لَيسَ سِرّاً
        [IssuedSecret]   = 4,   // تَجزِئَةٌ لا تُسترَجَع
        [SharedSecret]   = 5,   // مُشَفَّرٌ لِلتَحَقُّقِ مِن وارِد
        [SecretKey]      = 6,   // مُشَفَّرٌ يُطلَبُ بِه
        [CredentialFile] = 7,   // كُتلَةٌ مُشَفَّرَة
        [PlatformKey]    = 8,   // مِن جَيبِنا نَحن
    };

    public static int Rank(string kind) =>
        Ranks.TryGetValue(kind, out var r) ? r : -1;

    /// <summary>الأَنواعُ الَّتي تَدخُل خِزانَةَ المُستَأجِرِ أَو
    /// تُشبِهُ السِرَّ فَلا يَجوز أَن تَحمِلَ قائِمَةَ مُضيفين —
    /// خَلطُ طَبَقَتَين.</summary>
    public static bool IsSecretLike(string kind) => Rank(kind) >= Rank(IssuedSecret);

    /// <summary>الأَنواعُ الَّتي تَصلُح لِلتَحَقُّقِ مِن وارِدِ
    /// webhook — سِرٌّ يُقارَنُ بِه، لا رابِطٌ يُنقَر.</summary>
    public static bool CanVerifyWebhook(string kind) =>
        kind is SharedSecret or IssuedSecret;

    /// <summary>الرابِطُ يَلزَمُه سياجُ مُضيفين — رابِطٌ بِلا قائِمَةٍ
    /// إعادَةُ تَوجيهٍ مَفتوحَة.</summary>
    public static bool IsLink(string kind) => kind is HostedLink;
}
