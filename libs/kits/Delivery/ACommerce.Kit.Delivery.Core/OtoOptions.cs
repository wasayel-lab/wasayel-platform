using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ACommerce.Kit.Delivery;

// ═══ OTO — مَن يَشحَن، وبِأَيِّ حِساب ═══════════════════════════════════
//
// **الشَحنُ حِسابُ البائِعِ عِندَ OTO، ووَسايِل لا تَملِكُ عَقدَ النَقل**
// (‏ADR-033 §٢‑ب و§٢‑ج): الرَمزُ الَّذي يُطلَبُ بِه رَمزُ تَحديثِ **البائِع**
// (مالِكِ الإعلان) — يُمَرَّرُ إلى كُلِّ نِداءٍ في <see cref="OtoClient"/> ولا
// يَعيشُ في العَميلِ نَفسِه. وفي هذِه المَوجَةِ يَأتي مِن مِفتاحِ تَهيئَةٍ واحِد
// (رَمزُ مَتجَرِ `theme-demo` عَلى `staging`، ‏ADR-033 §٢‑هـ القاعِدَة ٩)؛ ومَدخَلُ
// الخِزانَةِ لِكُلِّ بائِعٍ (`delivery:{listing_owner}`) خُطوَةُ ‏ADR-033 السابِعَة.

/// <summary>
/// إعداداتُ OTO — كُلُّها مِن قِسمِ <c>Shipping:Oto</c>، وفي الـSpace
/// بِشَرطَتَينِ سُفلِيَّتَين (<c>Shipping__Oto__Environment</c>). <b>ولا قيمَةَ
/// افتِراضِيَّةً لِبيئَةٍ ولا لِرَمز</b>: الغِيابُ يُغلِق ولا يُخمَّن — نَسَقُ
/// <c>PayPalOptions</c> و<c>PaddleOptions</c> حَرفاً.
/// </summary>
public sealed class OtoOptions
{
    /// <summary><c>Shipping:Oto:Environment</c> — <c>staging</c> أَو
    /// <c>production</c> حَصراً، والثانِيَةُ مُغلَقَةٌ في هذِه المَوجَة
    /// (<see cref="OtoEnvironment.IsOpen"/>).</summary>
    public string Environment { get; set; } = "";

    /// <summary><c>Shipping:Oto:RefreshToken</c> — رَمزُ تَحديثِ حِسابِ البائِع
    /// كَما تُعطيه لَوحَةُ OTO («Settings → API Integrations → Connect»، ‏`SH-10`):
    /// «‏permanent token, use only to get access_token» (‏`SH-03`). <b>لا يُكتَبُ في
    /// لوغٍ ولا في عُنوانٍ ولا في رِسالَةِ خَطَإ</b> — مُثَبَّتٌ بِاختِبار.</summary>
    public string RefreshToken { get; set; } = "";

    /// <summary>مُهلَةُ النِداءِ بِالثَواني — <see cref="OtoEnvironment.DefaultTimeoutSeconds"/>،
    /// والصِفرُ أَو السالِبُ يَرتَدُّ إلَيها.</summary>
    public int TimeoutSeconds { get; set; } = OtoEnvironment.DefaultTimeoutSeconds;

    /// <summary>
    /// <para><c>Shipping:Oto:MarketplaceToken</c> — <b>الرَمزُ السوقِيُّ</b> («‏It requires a special kind of token provided by OTO.
    /// Regular refresh tokens will not work here.» — قِسمُ «Marketplace › Register»): سِرُّ المَنَصَّةِ لا سِرُّ مُستَأجِر (‏ADR-033 §٢‑أ،
    /// <c>platform_key</c>)، يَمنَحُه فَريقُ OTO (‏`SHGAP-01`). <b>وحُضورُه يُطفِئُ اللَصق</b>: سَطحُ البائِعِ يَعرِضُ مَدخَلاً واحِداً
    /// (تَيّارُ S8، ‏2026-10-12؛ دالَّةُ «أَيُّ المَدخَلَين» في القالِب)، وغِيابُه يُخفي مَسارَ <c>register</c> ولا يُسقِطُ تَعريفَ المُزَوِّد.
    /// ولا يُكتَبُ في لوغٍ ولا عُنوان.</para>
    /// </summary>
    public string MarketplaceToken { get; set; } = "";
}

/// <summary>
/// <para><b>قَرارُ «أَيُّ مُضيفٍ، وهَل يُفتَح؟» — دالّاتٌ نَقِيَّة.</b> لا HTTP ولا
/// وِعاءَ خِدمات، فَتُقاسُ بِجَدوَلٍ كَما تُقاسُ <c>PayPalEnvironment</c>.</para>
///
/// <para><b>والمُضيفانِ مِن الوَثائِقِ لا مِن الذاكِرَة</b>: <c>https://api.tryoto.com</c>
/// («All API requests must be made over HTTPS to the following base endpoint»)،
/// و<c>https://staging-api.tryoto.com</c> («Dev URL» في قِسمِ «Sandbox Account») —
/// كِلاهُما مَقروءٌ مِن مَجموعَةِ الوَثائِقِ العامَّةِ في <c>https://apis.tryoto.com/</c>
/// يَومَ ‏2026-10-12 (وَرَقَةُ البَحثِ `24-SHIPPING.md` §٣‑أ و`SH-15`).</para>
/// </summary>
public static class OtoEnvironment
{
    // ─── مَفاتيحُ التَهيئَة — مَوضِعٌ واحِدٌ يَقرَؤُه المُنتِجُ والمُختَبِرُ
    //     ووَثيقَةُ النَشر ──────────────────────────────────────────────
    public const string SectionKey       = "Shipping:Oto";
    public const string EnvironmentKey   = "Shipping:Oto:Environment";
    public const string RefreshTokenKey  = "Shipping:Oto:RefreshToken";
    /// <summary>الرَمزُ السوقِيُّ (تَيّارُ S8، ‏2026-10-12) — <see cref="OtoOptions.MarketplaceToken"/>.</summary>
    public const string MarketplaceTokenKey = "Shipping:Oto:MarketplaceToken";

    public const string Staging    = "staging";
    public const string Production = "production";

    public const string StagingBaseUrl    = "https://staging-api.tryoto.com";
    public const string ProductionBaseUrl = "https://api.tryoto.com";

    /// <summary>خَمسَ عَشرَةَ ثانِيَة — رَقَمُ جارَيه (<c>PayPalEnvironment</c>
    /// و<c>PaddleEnvironment</c>) لا رَقَمٌ مُختَرَع: OTO لا تَنشُرُ مُهلَة.</summary>
    public const int DefaultTimeoutSeconds = 15;

    /// <summary>عُمرُ رَمزِ الوُصولِ كَما تَقولُه الوَثيقَة: «‏a temporary token with a
    /// lifespan of one hour» (‏`SH-03`) — ومِثالُ رَدِّها <c>"expires_in": "3600"</c>
    /// نَصّاً لا رَقَماً. يُستَعمَلُ حينَ يَغيبُ الحَقلُ أَو لا يُقرَأ.</summary>
    public const int DocumentedTokenLifetimeSeconds = 3600;

    /// <summary>هامِشُ التَجديد: يُطلَبُ رَمزٌ جَديدٌ قَبلَ انتِهاءِ القائِمِ
    /// بِدَقيقَة — حُجَّةُ <c>PayPalEnvironment.TokenSafetySeconds</c> بِعَينِها:
    /// رَمزٌ صالِحٌ عِندَ الفَحصِ ومُنتَهٍ عِندَ وُصولِه يَرُدُّه المُزَوِّدُ ‏401.</summary>
    public const int TokenSafetySeconds = 60;

    /// <summary>مُتَغَيِّرُ البيئَةِ المُقابِلُ لِمِفتاحِ التَهيئَة (<c>:</c> ← <c>__</c>)
    /// — وهُوَ ما يَكتُبُه المالِكُ في الـSpace.</summary>
    public static string EnvVarName(string configKey) => configKey.Replace(":", "__");

    private static string? Normalize(string? environment) => environment?.Trim().ToLowerInvariant();

    /// <summary>المُضيفُ لِقيمَةِ البيئَة، و<c>null</c> لِقيمَةٍ خارِجَ المَعجَم —
    /// <b>والغِيابُ إغلاقٌ لا افتِراض</b>: لا «الإنتاجُ» بِخَطَإِ إملاء، ولا
    /// «الاختِبارُ» عَلى حِسابٍ يُظَنُّ حَقيقِيّاً.</summary>
    public static string? BaseUrlFor(string? environment) => Normalize(environment) switch
    {
        Staging    => StagingBaseUrl,
        Production => ProductionBaseUrl,
        _          => null
    };

    /// <summary>
    /// <para><b>أَيُنادى هذا المُضيفُ في هذِه المَوجَة؟</b> — <c>staging</c> وَحدَه.</para>
    ///
    /// <para><b>والإنتاجُ مُغلَقٌ بِقَرارٍ مَكتوبٍ لا بِنِسيان</b> (‏ADR-033 §٢‑د):
    /// «الهيكلُ الماشي على staging-api.tryoto.com وحدَها، ولا يُفتَحُ المسارُ على
    /// الإنتاجِ ولا يُعرَضُ على بائعٍ حتّى يُقرَأَ ترخيصُ OTO» (‏`SHGAP-02`). فَقيمَةُ
    /// <c>production</c> مَعروفَةُ المُضيفِ <b>ومَردودَة</b>: يُخفى الزِرُّ ويُجيبُ
    /// العَميلُ <see cref="OtoFailures.NotConfigured"/> بِلا نِداء. ويُفتَحُ بِسَطرٍ
    /// هُنا يَومَ يُقرَأُ التَرخيص — ويَحمَرُّ صَفُّه في <c>OtoClientTests</c> بِاسمِه.</para>
    /// </summary>
    public static bool IsOpen(string? environment) => Normalize(environment) == Staging;

    /// <summary>قيمَةٌ مَكتوبَةٌ خارِجَ المَعجَم — <b>نِيَّةٌ مُعلَنَةٌ لَم تَتَحَقَّق</b>،
    /// فَتُفشِلُ الإقلاعَ كَما تُفشِلُه عِندَ Paddle (‏<c>AddPaddleBilling</c>):
    /// تَجاهُلُها يُخفي الزِرَّ صامِتاً فَيَبحَثُ المالِكُ عَمّا لا يَظهَر. والفَراغُ
    /// لَيسَ كَذلك — هُوَ «لا شَحنَ عَبرَ OTO»، حالُ كُلِّ نُسخَةٍ اليَوم.</summary>
    public static bool IsMisconfiguredEnvironment(string? environment)
        => !string.IsNullOrWhiteSpace(environment) && BaseUrlFor(environment) is null;

    /// <summary>
    /// <para><b>سَطرُ الإقلاعِ الَّذي تَقرَؤُه بَوّابَةُ الإقلاع</b> (الكُتلَة <c>oto_environment</c> في <c>scripts/verify-production-boot.sh</c>) — بِقَرارِ
    /// <see cref="IsOpen"/> نَفسِه (‏2026-10-25، مُراجَعَةُ تَيّارِ W8-6 الجَولَة ٢، الصُغرى ٢؛ نَسَقُ <c>StudioAnalysisDoor.BootLogPrefix</c> و<c>WebPushService.BootLogPrefix</c>).
    /// كانَت الكُتلَةُ تُعيدُ كِتابَةَ <c>Normalize</c> بِـ<c>tr</c> — تَمحو بَياضَ ASCII في كُلِّ مَوضِعٍ ولا تَقُصُّ بَياضَ يونيكود — فَتَقولُ «خارِجَ المَعجَم»
    /// حَيثُ يَفتَحُ هذا الكودُ staging (القاعِدَة ٨: لا نُسخَةَ ثانِيَةً مِن قِراءَةِ التَهيئَة). والرَمزُ لا نَصٌّ عَرَبيٌّ يَنجَرِف.</para>
    /// </summary>
    public const string BootLogPrefix = "[shipping] oto_environment=";

    /// <summary>قيمَتا سَطرِ الإقلاع: <see cref="IsOpen"/> صادِقَةٌ أَم لا — والقيمَةُ خارِجَ المَعجَمِ لا تَبلُغُه (<c>AddOtoShipping</c> يَرمي قَبلَه).</summary>
    public const string OpenBootValue = "open";
    public const string ClosedBootValue = "closed";

    /// <summary>قيمَةُ سَطرِ الإقلاعِ لِبيئَةٍ — <see cref="OpenBootValue"/> حَيثُ <see cref="IsOpen"/>، وإلّا <see cref="ClosedBootValue"/>.</summary>
    public static string BootLogValue(string? environment) => IsOpen(environment) ? OpenBootValue : ClosedBootValue;

    /// <summary>مُهلَةٌ بِثَوانٍ، والصِفرُ أَو السالِبُ يَرتَدُّ إلى الافتِراض.</summary>
    public static TimeSpan Timeout(int seconds)
        => TimeSpan.FromSeconds(seconds > 0 ? seconds : DefaultTimeoutSeconds);
}

/// <summary>خَرقٌ واحِدٌ في بيئَةِ OTO عِندَ الإقلاع — شَكلُ <c>AuthTokenSecretViolation</c> حَرفاً (القاعِدَة ٤).</summary>
public sealed record OtoBootViolation(string Code, string MessageAr);

/// <summary>
/// <para><b>بيئَةُ OTO التَجريبِيَّةُ لا تُقلِعُ خارِجَ التَطوير</b> (قَرارُ المالِك د44 «ج»، ‏2026-10-29): «يُرفَضُ تَشغيلُ الخادِمِ المَنشورِ إذا
/// وُجِدَ فيه إعدادُ بيئَةِ OTO التَجريبِيَّة. والتَجرِبَةُ تَبقى مُمكِنَةً عَلى جِهازِكَ في وَضعِ التَطوير، عَبرَ نَفَقٍ إلى فَرعِ Neon مَنسوخ».
/// كانَت الحِمايَةُ دَليلَ النَشرِ وسَطرَ ⚠ في بَوّابَةِ الإقلاعِ وَحدَهُما (توصيفُ <c>Today_a_boot_outside_development_with_oto_staging_is_not_refused</c>)،
/// و<c>staging</c> في الـSpace يَفتَحُ قِسمَ الشَحنِ لِكُلِّ بائِعٍ في كُلِّ مَتجَرٍ عَلى بيئَةِ اختِبار (‏DEPLOY.md §٢·ط، brief-P1b-1).</para>
///
/// <para><b>ولا أُنبوبَ رابِع</b> (القاعِدَة ٨): شَكلُ <c>AuthTokenSecretPolicy</c> حَرفاً — جَدوَلٌ نَقِيٌّ، ورَميٌ في <c>Program.cs</c> بَعدَ بِناءِ المُضيفِ
/// وقَبلَ البَذرِ وأَوَّلِ طَلَب، عَلى <c>OtoOptions.Environment</c> الَّذي يَقرَؤُه العَميلُ نَفسُه. <b>و«خارِجَ التَطوير» لا «في الإنتاج» فَقَط</b> — حُجَّةُ جارِه.
/// <b>والمَفحوصُ «staging» بِعَينِها لا <see cref="OtoEnvironment.IsOpen"/></b>: يَومَ يُفتَحُ الإنتاجُ بِسَطرٍ هُناك لا يَصيرُ هذا الحارِسُ رافِضاً لَه.
/// والرِسالَةُ لا تَحمِلُ «قيمَتُه «» — تِلكَ صيغَةُ «خارِجَ المَعجَم» الَّتي تَقرَؤُها <c>boot_verdict</c> بِسَبَبٍ آخَر؛ وهذِه تَقرَؤُها بِرَمزِها.</para>
/// </summary>
public static class OtoBootPolicy
{
    /// <summary><c>staging</c> في إقلاعٍ خارِجَ <c>Development</c>.</summary>
    public const string StagingOutsideDevelopment = "oto_staging_outside_development";

    /// <summary>المَعجَمُ المُغلَق — رَمزٌ واحِد. تَقرَؤُه <c>boot_verdict</c> في <c>scripts/verify-production-boot.sh</c> بِحَرفِه فَتُرجِعُ 3 بِاسمِه.</summary>
    public static readonly IReadOnlyList<string> All = new[] { StagingOutsideDevelopment };

    /// <summary>القَرار: في <c>Development</c> لا خَرقَ أَيّاً كانَت القيمَة؛ وإلّا فَـ<c>staging</c> (بِتَطبيعِ <see cref="OtoEnvironment.BaseUrlFor"/>
    /// نَفسِه — قَصٌّ وأَحرُفٌ صَغيرَة) ⇒ <see cref="StagingOutsideDevelopment"/>؛ والفَراغُ و<c>production</c> لا خَرق.</summary>
    public static IReadOnlyList<OtoBootViolation> Validate(bool isDevelopment, string? environment)
    {
        if (isDevelopment || OtoEnvironment.BaseUrlFor(environment) != OtoEnvironment.StagingBaseUrl)
            return Array.Empty<OtoBootViolation>();

        return new[]
        {
            new OtoBootViolation(StagingOutsideDevelopment,
                $"‏{OtoEnvironment.EnvVarName(OtoEnvironment.EnvironmentKey)}={OtoEnvironment.Staging} خارِجَ بيئَةِ التَطوير — " +
                "بيئَةُ OTO التَجريبِيَّةُ لا تُضبَطُ في خادِمٍ مَنشورٍ يَخدِمُ بائِعينَ حَقيقِيّين: احذِفه مِن الـSpace، " +
                "وتَجرِبَةُ OTO عَلى جِهازِكَ في وَضعِ التَطوير بِنَفَقٍ إلى فَرعِ Neon مَنسوخ (docs/DEPLOY.md §٢·ط).")
        };
    }

    public static bool IsValid(bool isDevelopment, string? environment) => Validate(isDevelopment, environment).Count == 0;

    /// <summary>يَرمي إن كانَت بيئَةُ OTO <c>staging</c> خارِجَ التَطوير — يُستَدعى بَعدَ بِناءِ المُضيفِ بِجِوارِ حُرّاسِ الإقلاعِ الآخَرين.</summary>
    public static void AssertSafeOutsideDevelopment(bool isDevelopment, string? environment)
    {
        var violations = Validate(isDevelopment, environment);
        if (violations.Count == 0) return;
        throw new InvalidOperationException(
            "بيئَةُ الشَحنِ مَرفوضَةٌ عِندَ الإقلاع ["
            + string.Join("، ", violations.Select(v => v.Code))
            + "]: " + string.Join(" ", violations.Select(v => v.MessageAr)));
    }
}

/// <summary>
/// <para><b>رَمزُ الوُصولِ مُخَزَّناً ساعَتَه إلّا دَقيقَة — لِكُلِّ رَمزِ تَحديثٍ
/// مَدخَلُه.</b> صِنفٌ مُستَقِلٌّ عَن العَميلِ لِلسَبَبِ المَقيسِ في
/// <c>PayPalTokenCache</c> بِحَرفِه: <c>AddHttpClient&lt;T&gt;</c> يُسَجِّلُ <c>T</c>
/// <b>عابِراً</b> — نُسخَةٌ لِكُلِّ طَلَب — فَتَخزينٌ داخِلَ العَميلِ لا يُخَزِّنُ
/// شَيئاً وتُنادى OTO مَرَّتَينِ لِكُلِّ نِداء. وهذا الصِنفُ بِلا HTTP: يَأخُذُ
/// دالَّةَ الجَلبِ ويَعُدُّ نِداءاتِها، فَـ«الرَمزُ يُخَزَّن» جُملَةٌ مَعدودَة.</para>
///
/// <para><b>ولِماذا مَداخِلُ لا مَدخَلٌ واحِد</b> (بِخِلافِ PayPal): الرَمزُ
/// رَمزُ <b>بائِع</b> (‏ADR-033 §٢‑ج)، والبائِعونَ كَثيرونَ يَومَ تُبنى خُطوَةُ
/// الخِزانَة. <b>والمِفتاحُ تَجزِئَةُ رَمزِ التَحديثِ لا الرَمزُ نَفسُه</b>:
/// قاموسٌ في الذاكِرَةِ يُطبَعُ يَوماً في تَشخيصٍ لا يُسَرِّبُ سِرّاً دائِماً.</para>
/// </summary>
public sealed class OtoTokenCache
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, (string Token, DateTimeOffset ExpiresAt)> _entries =
        new(StringComparer.Ordinal);

    /// <summary>كَم مَرَّةً نودِيَت OTO فِعلاً لِرَمزٍ — عَدّادٌ لِلاختِبار.</summary>
    public int FetchCount { get; private set; }

    /// <summary>مِفتاحُ المَدخَل: ‏SHA-256 لِرَمزِ التَحديثِ سِتَّ عَشَرِيّاً.</summary>
    public static string KeyOf(string refreshToken)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken)));

    /// <summary>الرَمزُ المُخَزَّنُ إن كانَ صالِحاً عِندَ <paramref name="now"/>، وإلّا
    /// <c>null</c> — والوَقتُ يُمَرَّرُ ولا يُقرَأُ مِن الساعَة هُنا.</summary>
    public string? Cached(string key, DateTimeOffset now)
    {
        lock (_entries)
            return _entries.TryGetValue(key, out var e) && now < e.ExpiresAt ? e.Token : null;
    }

    /// <summary>
    /// يُعيدُ رَمزاً صالِحاً: المُخَزَّنَ، وإلّا يُنادي <paramref name="fetch"/> مَرَّةً
    /// واحِدَةً ويُخَزِّنُ ما أَعطَت. <b>والفَشَلُ لا يُخَزَّن</b>: يَعودُ كَما هو
    /// ويُجَرَّبُ الجَلبُ في النِداءِ التالي. والفَحصُ قَبلَ القُفلِ وبَعدَه — فالخاسِرُ
    /// في السِباقِ يَقرَأُ ما جَلَبَه الفائِز.
    /// </summary>
    public async Task<OtoResult<string>> GetAsync(
        string key,
        Func<CancellationToken, Task<OtoResult<OtoAccessToken>>> fetch,
        DateTimeOffset now, CancellationToken ct = default)
    {
        if (Cached(key, now) is { } hit) return OtoResult<string>.Success(hit);

        await _gate.WaitAsync(ct);
        try
        {
            if (Cached(key, now) is { } second) return OtoResult<string>.Success(second);

            var fetched = await fetch(ct);
            FetchCount++;
            if (fetched.Value is not { } token) return OtoResult<string>.Fail(fetched.Failure!);

            // الهامِشُ يُطرَح، ولا يُعطي عُمرٌ قَصيرٌ انتِهاءً في الماضي.
            var lifetime = Math.Max(token.ExpiresInSeconds - OtoEnvironment.TokenSafetySeconds, 1);
            lock (_entries) _entries[key] = (token.Token, now.AddSeconds(lifetime));
            return OtoResult<string>.Success(token.Token);
        }
        finally { _gate.Release(); }
    }

    /// <summary>يُسقِطُ المُخَزَّن — يُنادى عِندَ ‏401 فَيُجَدَّدُ الرَمزُ في النِداءِ
    /// التالي بَدَلَ أَن يَبقى فاسِداً حَتّى انتِهاءِ عُمرِه المُعلَن.</summary>
    public void Invalidate(string key)
    {
        lock (_entries) _entries.Remove(key);
    }
}

public static class OtoShippingExtensions
{
    /// <summary>
    /// <para><b>يُسَجِّلُ عَميلَ OTO بِـ<c>AddHttpClient</c></b> — في المَوضِعِ الَّذي كانَ
    /// فيه <c>AddMockDelivery()</c> (‏ADR-033 الخُطوَة 3: يُحذَفُ المُحاكي في كوميتِ
    /// أَوَّلِ مُستَهلِك). <b>ويُسَجَّلُ دائِماً</b>: العَميلُ لا يَحمِلُ سِرّاً — رَمزُ
    /// البائِعِ يُمَرَّرُ إلى كُلِّ نِداء — وبِلا بيئَةٍ مَفتوحَةٍ يُجيبُ
    /// <see cref="OtoFailures.NotConfigured"/> بِلا شَبَكَة؛ والزِرُّ لا يُرسَمُ أَصلاً
    /// حَيثُ لا رَمزَ يُحَلّ (القاعِدَة ١٢).</para>
    ///
    /// <para><b>وقيمَةُ بيئَةٍ خارِجَ المَعجَمِ تُفشِلُ الإقلاع</b> — حُجَّةُ
    /// <c>AddPaddleBilling</c> بِحَرفِها: إقلاعٌ يَشتَكي بِاسمِ المُتَغَيِّرِ أَرخَصُ
    /// مِن زِرٍّ يَختَفي صامِتاً.</para>
    /// </summary>
    public static IServiceCollection AddOtoShipping(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<OtoOptions>(configuration.GetSection(OtoEnvironment.SectionKey));

        var options = new OtoOptions();
        configuration.GetSection(OtoEnvironment.SectionKey).Bind(options);

        if (OtoEnvironment.IsMisconfiguredEnvironment(options.Environment))
            throw new InvalidOperationException(
                $"‏{OtoEnvironment.EnvVarName(OtoEnvironment.EnvironmentKey)} " +
                $"قيمَتُه «{options.Environment}» وهي خارِجَ " +
                $"«{OtoEnvironment.Staging}»/«{OtoEnvironment.Production}» — " +
                "اضبِطها أَو احذِفها. ولا تُخمَّن بيئَةُ شَحن.");

        // الرَمزُ مُفرَد — الشَيءُ الوَحيدُ الَّذي يَجِبُ أَن يَعيشَ أَطوَلَ مِن الطَلَب.
        services.AddSingleton<OtoTokenCache>();

        services.AddHttpClient<OtoClient>(client =>
        {
            // سَقفٌ احتِياطيٌّ **ضِعفُ** مُهلَتِنا الداخِلِيَّة، لِتَسبِقَ مُهلَتُنا دائِماً
            // فَيَثبُتَ رَمزُ الخَطَإ بَدَلَ أَن يَتَبَدَّلَ بِسِباق — حُجَّةُ جارَيه.
            client.Timeout = OtoEnvironment.Timeout(options.TimeoutSeconds) * 2;
        });

        return services;
    }
}
