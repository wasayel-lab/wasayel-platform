using System.Security.Cryptography;
using System.Text;

namespace ACommerce.Platform.Providers;

// ═══ خِزانَةُ أَسرارِ المُستَأجِر — تَعبِئَةُ أَعمِدَةٍ مَحفورَة، لا مَوجَةُ بِناء ═══
//
// **العِلَّةُ المَقيسَة (‏2026-10-12، موجَزُ الفَجَوات P1-7)**: أَعمِدَةُ الظَرفِ
// `Nonce`/`Cipher`/`Tag`/`KekVersion`/`Aad` مُعلَنَةٌ عَلى `StoredValue` مُنذُ
// ‏ADR-012 §هـ **بِصِفرِ كاتِب** (`grep 'ProviderVault|Unseal|.Sealed|Providers__Kek|AesGcm'`
// في `libs/` و`apps/` و`tests/` = صِفر)، و`StoredValue.Explicit` تَرمي لِلسِرّ —
// فَلا يُخَزَّنُ مِفتاحُ بَوّابَةٍ لِمُستَأجِرٍ ولا رَمزُ ناقِلٍ لِبائِع. و«لا سِرَّ
// يُخَزَّن قَبلَ أَن تُبنى خِزانَتُه» (‏ADR-012 §٢‑ب) جُملَةٌ يَفرِضُها الكود؛ وهذا
// المِلَفُّ هُوَ الخِزانَة، ‏ADR-032 §٢‑هـ الخُطوَة 4.
//
// **مُستَهلِكاها — ولِأَجلِهِما بُنِيَت مَرَّةً واحِدَة (القاعِدَة ١ والقاعِدَة ٨ في
// ‏ADR-033 §٢‑هـ: «الخزانةُ واحدةٌ … لا خزانةَ ثانية»)**:
//   ‏١. مِفتاحُ البَوّابَةِ السِرِّيُّ (`secret_key`) يَلصَقُه صاحِبُ المَتجَرِ في
//      سَطحِ الرَبط — ‏ADR-032 الخُطوَة 7 (‏`ProviderBindSurface`)، وسِياقُه
//      `tenant|capability|field`.
//   ‏٢. رَمزُ تَحديثِ OTO لِكُلِّ بائِع — ‏ADR-033 الخُطوَة 7 (سَطحُ البائِع)،
//      وسِياقُه `tenant|capability|field|listing_owner` (القاعِدَة 8 هُناك).
// **والخِزانَةُ لا تَعرِفُ البائِعَ ولا البَوّابَة**: السِياقُ (‏AAD) نَصٌّ يَحمِلُه
// المُنادي، و`AadOf` تَبنيه بِأَيِّ عَدَدٍ مِن الأَجزاءِ بِلا لَبس — فَالشَكلانِ
// يَمُرّانِ مِن الدالَّةِ نَفسِها.
//
// **ولا واجِهَة، ولا «مُزَوِّدُ مَفاتيح»**: صِنفٌ واحِدٌ بِمِفتاحٍ رَئيسٍ واحِد
// (‏KEK) مِن التَهيئَة. **وشَرطُ الاستِخراج** (القاعِدَة ١ — ثَلاثَةُ مُستَهلِكينَ
// قَبلَه): لا تُستَخرَجُ واجِهَةٌ ولا يُدمَجُ مُزَوِّدُ مَفاتيحَ خارِجيّ (‏KMS/HSM)
// قَبلَ أَن يَكونَ لِلخِزانَةِ ثالِثُ مُستَهلِكٍ في وَقتِ التَشغيل — والمُرَشَّحُ
// المَقيسُ اليَومَ `shared_secret`/`credential_file` يَومَ يُعلِنُهُما مِلَفُّ
// تَعريف. **وتَدويرُ المِفتاحِ الرَئيس** (‏`Providers__Kek__v2` وإعادَةُ التَغليف)
// يُبنى يَومَ يُطلَبُ تَدويرٌ فِعلاً: العَمودُ `KekVersion` يَحمِلُه مِن اليَوم
// (‏ADR-012 §هـ)، والخِزانَةُ تَفُكُّ نُسخَتَها هي وتَرُدُّ غَيرَها بِاسمِه.
//
// **والحَدُّ الَّذي لا يُعبَر — مَفحوصٌ لا مَوعود**: النَصُّ الصَريحُ لا يُطبَعُ
// ولا يُسَجَّلُ ولا يُصَيَّرُ ولا يُعرَض. لا لوغَ في هذا المِلَفِّ أَصلاً،
// و`ToString` هُنا وعَلى `StoredValue` مُعَتَّمان، ورَسائِلُ الخَطَإ تَقولُ
// السَبَبَ لا القيمَة — `ProviderVaultTests` يَمسَحُ كُلَّ وَجهٍ بِنَوافِذَ مِن
// أَربَعَةِ مَحارِف.

/// <summary>خَرقٌ في مِفتاحِ الخِزانَةِ الرَئيس. نَفسُ شَكلِ
/// <see cref="ProviderDefinitionViolation"/> حَرفاً (القاعِدَة ٤). <b>ولا تَحمِلُ
/// <c>MessageAr</c> قيمَةَ المِفتاحِ أَبَداً</b> — طولَه عِندَ الحاجَة، لا نَصَّه.</summary>
public sealed record VaultViolation(string Code, string MessageAr);

/// <summary>
/// <para><b>الخِزانَة</b> — ‏AES-GCM بِمِفتاحٍ رَئيسٍ ‏256 بِتّاً، ورَقمٍ عابِرٍ
/// (‏nonce) ‏96 بِتّاً جَديدٍ عَشوائِيٍّ لِكُلِّ خَتم، وعَلامَةِ تَحَقُّقٍ ‏128 بِتّاً،
/// وسِياقٍ (‏AAD) مَربوطٍ بِالخَتم: القيمَةُ المَختومَةُ لِمُستَأجِرٍ أَو قُدرَةٍ أَو
/// حَقلٍ لا تُفَكُّ في سِياقِ غَيرِه.</para>
///
/// <para><b>ومَتى لا تَكونُ خِزانَة</b>: مِفتاحٌ غائِبٌ أَو تالِف ⇒
/// <see cref="Unavailable"/> رَمزاً مُسَمّىً، و<see cref="IsAvailable"/> كاذِب،
/// <b>ولا خَتمَ ولا فَكّ</b> — والسَطحُ الَّذي يَربِطُ يُخفي ما يَحتاجُها ويَرُدُّه
/// (‏ADR-032 الخُطوَة 11). فَشَلٌ مُغلَق، لا خَتمٌ بِمِفتاحٍ ضَعيف.</para>
/// </summary>
public sealed class ProviderVault
{
    /// <summary>مِفتاحُ التَهيئَة — المُتَغَيِّرُ <see cref="KekEnvironmentVariable"/>
    /// كَما يُتَرجِمُه .NET (الشَرطَتانِ السُفلِيَّتانِ المُضاعَفَتانِ نُقطَتانِ).</summary>
    public const string KekConfigKey = "Providers:Kek:v1";

    /// <summary>اسمُ المُتَغَيِّرِ بِحَرفِه — يَضَعُه المالِكُ في الـSpace
    /// (‏<c>docs/DEPLOY.md</c>)، وتَفحَصُه <c>verify-production-boot.sh</c>. مَوضِعٌ
    /// واحِدٌ لِلاسم.</summary>
    public const string KekEnvironmentVariable = "Providers__Kek__v1";

    /// <summary>نُسخَةُ المِفتاحِ الرَئيسِ الَّتي تَختِمُ بِها هذِه الخِزانَة —
    /// تُكتَبُ في عَمودِ <c>KekVersion</c> مَعَ كُلِّ خَتم.</summary>
    public const int KekVersion = 1;

    /// <summary>‏AES-256 — <b>قَرارٌ هَندَسِيٌّ لا رَقَمٌ مِن الصَفّ</b> (تَصحيحُ ‏2026-10-12): صَفُّ
    /// ‏ADR-032 §٢‑هـ الخُطوَة 4 يَقولُ «AES-GCM» بِلا طول، ولا يَقولُه ‏ADR-012 ولا مُستَودَعُ البَحث.
    /// فَاختيرَ أَطوَلُ مِفتاحٍ يَقبَلُه AES-GCM (‏128 أَو 192 أَو 256 بِتّاً)، وهُوَ ما يُوَلِّدُه أَمرُ
    /// <c>DEPLOY.md</c> §٢·ح (<c>openssl rand -base64 32</c>) — ‏ADR-032 §٧‑ح.</summary>
    public const int KekBytes = 32;

    /// <summary>‏96 بِتّاً: الطولُ القِياسيُّ لِـAES-GCM، وجَديدٌ عَشوائِيٌّ لِكُلِّ خَتم.</summary>
    public const int NonceBytes = 12;

    /// <summary>‏128 بِتّاً: أَطوَلُ عَلامَةٍ يُعطيها AES-GCM.</summary>
    public const int TagBytes = 16;

    // ─── مَعجَمُ الخَرقِ المُغلَق — رَمزٌ واحِدٌ لا ثانِيَ لَه ──────────────

    /// <summary>لا خِزانَة: المِفتاحُ الرَئيسُ غائِبٌ أَو لَيسَ base64 أَو
    /// لا يُفَكُّ إلى ‏32 بايتاً (أَو المَنَصَّةُ بِلا AES-GCM). <b>رَمزٌ واحِدٌ
    /// لِأَنّ الأَثَرَ واحِد</b> — إخفاءٌ ورَدّ — والرِسالَةُ تُسَمّي السَبَب.</summary>
    public const string Unavailable = "vault_unavailable";

    public static readonly IReadOnlyList<string> Codes = new[] { Unavailable };

    private readonly byte[]? _kek;

    private ProviderVault(byte[]? kek, IReadOnlyList<VaultViolation> violations)
    {
        _kek = kek;
        Violations = violations;
    }

    /// <summary>أَتَختِمُ هذِه الخِزانَةُ وتَفُكّ؟</summary>
    public bool IsAvailable => _kek is not null;

    /// <summary>لِماذا لا خِزانَة — فارِغٌ حينَ تَكون.</summary>
    public IReadOnlyList<VaultViolation> Violations { get; }

    /// <summary>
    /// <para>الخِزانَةُ مِن قيمَةِ <see cref="KekConfigKey"/> كَما قُرِئَت —
    /// <b>base64 لِـ‏32 بايتاً</b>. والمُنادي في التَركيبِ يَقرَأُ التَهيئَةَ
    /// ويُمَرِّرُ النَصّ؛ فَلا تُحيلُ هذِه المَكتَبَةُ حُزمَةَ تَهيئَة (‏ADR-012
    /// §٣: المَكتَبَةُ بِإحالَةٍ واحِدَة).</para>
    /// </summary>
    public static ProviderVault FromKek(string? kekBase64)
    {
        var violations = Validate(kekBase64);
        return violations.Count == 0
            ? new ProviderVault(Convert.FromBase64String(kekBase64!.Trim()), violations)
            : new ProviderVault(null, violations);
    }

    /// <summary>
    /// <para><b>القَرار — جَدوَلٌ لا شَرطٌ مَنثور</b>، وكُلُّ مُدخَلٍ يُعطي
    /// <see cref="Unavailable"/> بِرِسالَةٍ تُسَمّي سَبَبَه:</para>
    /// <list type="bullet">
    /// <item>غائِبٌ أَو فارِغٌ أَو بَياض.</item>
    /// <item>لَيسَ base64.</item>
    /// <item>يُفَكُّ إلى غَيرِ ‏32 بايتاً — والرِسالَةُ تَقولُ الطولَ المَقيسَ لا القيمَة.</item>
    /// <item>المَنَصَّةُ بِلا AES-GCM.</item>
    /// </list>
    /// </summary>
    public static IReadOnlyList<VaultViolation> Validate(string? kekBase64)
    {
        if (string.IsNullOrWhiteSpace(kekBase64))
            return One($"مِفتاحُ الخِزانَةِ {KekEnvironmentVariable} غائِب — فَلا خِزانَة: لا يُختَمُ سِرٌّ، " +
                       "ويُخفى كُلُّ مُزَوِّدٍ يَحتاجُها ويُرَدُّ رَبطُه.");

        // يُفَكُّ لِيُقاسَ طولُه ثُمَّ يُصَفَّر — لا يَبقى في الذاكِرَةِ مِن هُنا.
        var buffer = new byte[kekBase64.Length];
        bool decoded;
        int length;
        try
        {
            decoded = Convert.TryFromBase64String(kekBase64.Trim(), buffer, out length);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(buffer);
        }

        if (!decoded)
            return One($"مِفتاحُ الخِزانَةِ {KekEnvironmentVariable} لَيسَ base64 — يُكتَبُ مُخرَجُ " +
                       "‏`openssl rand -base64 32` كَما هُوَ.");

        if (length != KekBytes)
            return One($"مِفتاحُ الخِزانَةِ {KekEnvironmentVariable} يُفَكُّ إلى {length} بايتاً — " +
                       $"والمَطلوبُ {KekBytes} (‏AES-256).");

        if (!AesGcm.IsSupported)
            return One("‏AES-GCM غَيرُ مَدعومٍ عَلى هذِه المَنَصَّة — فَلا خِزانَة.");

        return Array.Empty<VaultViolation>();
    }

    public static bool IsValid(string? kekBase64) => Validate(kekBase64).Count == 0;

    private static IReadOnlyList<VaultViolation> One(string messageAr)
        => new[] { new VaultViolation(Unavailable, messageAr) };

    /// <summary>
    /// <para><b>ما تَختِمُه الخِزانَة</b> — <c>secret_key</c> وَحدَه اليَوم: مُشَفَّرٌ
    /// يُسترَجَعُ لِيُطلَبَ بِه، ويُعرَضُ مِنه ذَيلُه (‏ADR-012 §٢‑ب). و<c>issued_secret</c>
    /// تَجزِئَةٌ لا تُختَم (أُنبوبُها قائِم)، و<c>platform_key</c> لَيسَ في خِزانَةِ
    /// المُستَأجِرِ أَصلاً. و<c>shared_secret</c>/<c>credential_file</c> يَدخُلانِ هُنا
    /// يَومَ يُعلِنُهُما مِلَفُّ تَعريف — لا قَبلَه (القاعِدَة ١).</para>
    /// </summary>
    public static bool Seals(string kind) => kind is CredentialKinds.SecretKey;

    /// <summary>
    /// <para><b>السِياقُ المَربوطُ بِالخَتمِ (‏AAD)</b> — أَجزاءٌ تُوصَلُ بِـ<c>|</c>
    /// بِلا لَبس: جُزءٌ فارِغٌ أَو يَحمِلُ <c>|</c> يُرمى، وإلّا لَالتَبَسَ «أ|ب» + «ج»
    /// بِـ«أ» + «ب|ج». والشَكلانِ المَعروفانِ اليَوم:
    /// <c>AadOf(tenant, capability, field)</c> لِرَبطِ المَتجَر (‏ADR-032 الخُطوَة 4)،
    /// و<c>AadOf(tenant, capability, field, listingOwner)</c> لِمَدخَلِ البائِع
    /// (‏ADR-033 §٢‑ج‑٢) — والدالَّةُ لا تَعرِفُ أَيَّهُما.</para>
    /// </summary>
    public static string AadOf(params string[] parts)
    {
        if (parts is null || parts.Length == 0)
            throw new ArgumentException("السِياقُ بِلا جُزءٍ واحِد.", nameof(parts));

        foreach (var p in parts)
            if (string.IsNullOrEmpty(p) || p.Contains('|'))
                throw new ArgumentException(
                    "جُزءُ السِياقِ فارِغٌ أَو يَحمِلُ «|» — فَيَلتَبِسُ السِياقُ بِغَيرِه.", nameof(parts));

        return string.Join('|', parts);
    }

    // ─── الخَتمُ والفَكّ — يُنادَيانِ مِن StoredValue.Sealed وUnseal وَحدَهُما ──

    private string UnavailableMessage =>
        $"{Unavailable}: الخِزانَةُ غَيرُ مُتاحَة — {KekEnvironmentVariable} غائِبٌ أَو تالِف؛ " +
        "فَلا يُختَمُ سِرٌّ ولا يُفَكّ.";

    internal StoredValue Seal(string kind, string clear, string aad)
    {
        if (!Seals(kind))
            throw new InvalidOperationException(
                $"النَوع «{kind}» لا تَختِمُه الخِزانَة — الصَريحُ بِـStoredValue.Explicit، " +
                "والتَجزِئَةُ في أُنبوبِها.");
        if (_kek is null) throw new InvalidOperationException(UnavailableMessage);
        if (string.IsNullOrEmpty(clear))
            throw new ArgumentException("لا يُختَمُ فَراغ.", nameof(clear));
        if (string.IsNullOrEmpty(aad))
            throw new ArgumentException("لا خَتمَ بِلا سِياق (‏AAD).", nameof(aad));

        var plain = Encoding.UTF8.GetBytes(clear);
        var nonce = RandomNumberGenerator.GetBytes(NonceBytes);
        var cipher = new byte[plain.Length];
        var tag = new byte[TagBytes];
        try
        {
            using var gcm = new AesGcm(_kek, TagBytes);
            gcm.Encrypt(nonce, plain, cipher, tag, Encoding.UTF8.GetBytes(aad));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
        }

        return new StoredValue
        {
            Kind       = kind,
            Plain      = null,
            Nonce      = Convert.ToBase64String(nonce),
            Cipher     = Convert.ToBase64String(cipher),
            Tag        = Convert.ToBase64String(tag),
            KekVersion = KekVersion,
            Aad        = aad,
            Tail4      = ProviderSecrecy.TailOf(kind, clear),
        };
    }

    /// <summary>
    /// <para><b>الفَكُّ بِسِياقِ المُنادي لا بِعَمودِ <c>Aad</c> المُخَزَّن</b>: لَو
    /// فُكَّ بِالمُخَزَّنِ لَكَفى نَسخُ الظَرفِ كامِلاً — بِعَمودِه — مِن مُستَأجِرٍ
    /// إلى آخَرَ لِيُفَكَّ هُناك. فَالمُنادي يَبني السِياقَ مِمّا يَعرِفُه (المُستَأجِر،
    /// القُدرَة، الحَقل)، ويُرمى عِندَ أَوَّلِ اختِلاف.</para>
    /// </summary>
    internal string Open(StoredValue v, string aad)
    {
        if (!v.IsSealed)
            throw new InvalidOperationException("القيمَةُ لَيسَت مَختومَة — لا شَيءَ يُفَكّ.");
        if (_kek is null) throw new InvalidOperationException(UnavailableMessage);
        if (v.KekVersion != KekVersion)
            throw new InvalidOperationException(
                $"القيمَةُ مَختومَةٌ بِنُسخَةِ المِفتاحِ {v.KekVersion}، والمُحَمَّلَةُ {KekVersion} — " +
                "لا تَدويرَ مَبنِيٌّ بَعد.");
        if (!string.Equals(v.Aad, aad, StringComparison.Ordinal))
            throw new CryptographicException(
                "سِياقُ القيمَةِ المَختومَةِ (‏Aad) غَيرُ سِياقِ طالِبِها — لا تُفَكُّ خارِجَ سِياقِها.");

        byte[] nonce, cipher, tag;
        try
        {
            nonce  = Convert.FromBase64String(v.Nonce ?? "");
            cipher = Convert.FromBase64String(v.Cipher ?? "");
            tag    = Convert.FromBase64String(v.Tag ?? "");
        }
        catch (FormatException)
        {
            throw new CryptographicException("ظَرفٌ تالِف: أَحَدُ أَعمِدَتِه لَيسَ base64.");
        }

        if (nonce.Length != NonceBytes || tag.Length != TagBytes)
            throw new CryptographicException("ظَرفٌ تالِف: طولُ الرَقمِ العابِرِ أَو العَلامَةِ غَيرُ المَختوم.");

        var plain = new byte[cipher.Length];
        try
        {
            using var gcm = new AesGcm(_kek, TagBytes);
            // يَرمي AuthenticationTagMismatchException (‏CryptographicException)
            // لِمِفتاحٍ آخَرَ أَو سِياقٍ آخَرَ أَو عَمودٍ مُعبَثٍ بِه — بِلا نَصٍّ يُعاد.
            gcm.Decrypt(nonce, cipher, tag, plain, Encoding.UTF8.GetBytes(aad));
            return Encoding.UTF8.GetString(plain);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
        }
    }

    /// <summary><b>وَجهٌ مُعَتَّم</b>: الحالُ والنُسخَة — لا المِفتاحُ ولا طولُه.</summary>
    public override string ToString()
        => IsAvailable ? $"ProviderVault(kek=v{KekVersion})" : $"ProviderVault({Unavailable})";
}
