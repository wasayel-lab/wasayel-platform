namespace ACommerce.Kit.Payments.Providers.PayPal;

// ═══ PayPal — مَن يَقبِض، ومِمَّن ═══════════════════════════════════════
//
// **هذا المُزَوِّدُ لِتَدَفُّقٍ واحِدٍ لا لِاثنَين**: رائِدُ الأَعمال
// يَدفَع لِـ**وَسايِل** ثَمَنَ باقَةِ مَتجَرِه (‏ADR-003 §٢-ب). وهُوَ
// **غَيرُ** تَدَفُّقِ `IPaymentProvider` المُسَجَّلِ في الوِعاء
// (‏`AddMockPayments`) الَّذي يَخدِم عَرَبونَ الصَفقات داخِلَ مَتجَر —
// مُشتَرٍ يَدفَع لِبائِع. المالانِ لا يَلتَقِيان، ولِذلك **لا يُسَجَّل
// هذا الصَنفُ عَلى `IPaymentProvider`** بَل عَلى نَفسِه: تَسجيلُه
// عَلَيها كانَ سَيَجعَل `DealsService` تَحجُز عَرَبونَ صَفقَةٍ على
// حِسابِ وَسايِل في PayPal.
//
// ويُنَفِّذُ الواجِهَةَ مَعَ ذلك — نَفسُ عَقدِ Moyasar وNoon حَرفاً —
// لِأَنّ الاشتِراكَ المُتَكَرِّرَ فيها هُوَ بِعَينِه ما نَحتاج، ولِأَنّ
// مُزَوِّداً لا يُنَفِّذُ الواجِهَةَ يَصير شَكلاً رابِعاً لِلمال.

/// <summary>
/// إعداداتُ PayPal — كُلُّها مِن قِسم <c>Payments:PayPal</c>، وفي
/// الـSpace بِشَرطَتَينِ سُفلِيَّتَين (<c>Payments__PayPal__ClientId</c>).
/// <b>ولا قيمَةَ افتِراضِيَّةً لِسِرٍّ ولا لِبيئَة</b>: الغِيابُ يُغلِق
/// (<see cref="PayPalEnvironment.IsConfigured"/>)، ولا يُخمَّن.
/// </summary>
public sealed class PayPalOptions
{
    /// <summary><c>Payments:PayPal:ClientId</c> — مُعَرِّفُ تَطبيقِ REST.</summary>
    public string ClientId { get; set; } = "";

    /// <summary><c>Payments:PayPal:ClientSecret</c> — لا يُكتَب في لوغٍ
    /// ولا في رِسالَةِ خَطَإ (مُثَبَّتٌ بِاختِبار).</summary>
    public string ClientSecret { get; set; } = "";

    /// <summary><c>Payments:PayPal:Environment</c> —
    /// <c>sandbox</c> أَو <c>live</c> حَصراً. وقيمَةٌ ثالِثَةٌ (أَو
    /// فارِغَة) <b>لَيسَت افتِراضاً بَل إغلاقاً</b>: مُضيفٌ يُخمَّن
    /// يَعني إمّا نِداءَ اختِبارٍ يُظَنُّ حَقيقِيّاً أَو العَكس.</summary>
    public string Environment { get; set; } = "";

    /// <summary><c>Payments:PayPal:WebhookId</c> — مُعَرِّفُ الـWebhook
    /// كَما أَنشَأَه المالِكُ في لَوحَةِ PayPal. <b>بِلا هذا لا
    /// يُتَحَقَّقُ مِن تَوقيع، وبِلا تَحَقُّقٍ لا تُقرَأُ رِسالَةٌ
    /// كَبَيانات</b> — والنُقطَةُ تَرُدُّ رَفضاً صَريحاً بَدَلَ أَن
    /// تُصَدِّقَ كُلَّ مَن طَرَقَ البابَ.</summary>
    public string WebhookId { get; set; } = "";

    /// <summary>مُهلَةُ النِداء بِالثَواني —
    /// <see cref="PayPalEnvironment.DefaultTimeoutSeconds"/>،
    /// والصِفرُ أَو السالِبُ يَرتَدُّ إلَيها.</summary>
    public int TimeoutSeconds { get; set; } = PayPalEnvironment.DefaultTimeoutSeconds;
}

/// <summary>
/// <para><b>قَرارُ «أَيُّ مُضيفٍ، وهَل نَحنُ مُهَيَّؤُون؟» — دالّاتٌ
/// نَقِيَّة.</b> لا HTTP ولا وِعاءَ خِدمات، فَتُقاس بِجَدوَلٍ كَما
/// تُقاس <c>AuthChannelSelection.Decide</c> — والحَدُّ الَّذي لا يُقاس
/// آلِيّاً يَنهار (القاعِدَة ٢).</para>
/// </summary>
public static class PayPalEnvironment
{
    // ─── مَفاتيحُ التَهيئَة — مَوضِعٌ واحِدٌ يَقرَؤُه المُنتِجُ
    //     والمُختَبِرُ ووَثيقَةُ النَشر ───────────────────────────────
    public const string ClientIdKey     = "Payments:PayPal:ClientId";
    public const string ClientSecretKey = "Payments:PayPal:ClientSecret";
    public const string EnvironmentKey  = "Payments:PayPal:Environment";
    public const string WebhookIdKey    = "Payments:PayPal:WebhookId";

    /// <summary>القِسمُ كامِلاً — يُمَرَّر إلى <c>Configuration.GetSection</c>.</summary>
    public const string SectionKey = "Payments:PayPal";

    public const string Sandbox = "sandbox";
    public const string Live    = "live";

    public const string SandboxBaseUrl = "https://api-m.sandbox.paypal.com";
    public const string LiveBaseUrl    = "https://api-m.paypal.com";

    /// <summary>خَمسَ عَشرَةَ ثانِيَة — الرَقَمُ المَطلوبُ في تَكليفِ
    /// المَوجَة، لا رَقمٌ مُخترَع.</summary>
    public const int DefaultTimeoutSeconds = 15;

    /// <summary>هامِشُ تَجديدِ الرَمز: يُطلَبُ رَمزٌ جَديدٌ قَبلَ
    /// انتِهاءِ القائِمِ بِدَقيقَة. <b>وبِلا الهامِشِ يَقَع سِباقٌ
    /// صامِت</b> — رَمزٌ صالِحٌ عِندَ الفَحص ومُنتَهٍ عِندَ وُصولِه
    /// PayPal، فَيَرُدُّ ‏401 مَرَّةً كُلَّ ثَماني ساعات.</summary>
    public const int TokenSafetySeconds = 60;

    /// <summary>مُتَغَيِّرُ البيئَةِ المُقابِلُ لِمِفتاحِ التَهيئَة
    /// (<c>:</c> ← <c>__</c>) — وهُوَ ما يَكتُبُه المالِكُ في الـSpace.</summary>
    public static string EnvVarName(string configKey) => configKey.Replace(":", "__");

    /// <summary>
    /// المُضيفُ لِقيمَةِ البيئَة، و<c>null</c> لِقيمَةٍ خارِجَ
    /// المَعجَم — <b>وهذا هُوَ الفَشَلُ المُغلَق</b>: لا «الافتِراضُ
    /// live» (فَيُنادى الإنتاجُ بِخَطَإ إملاءٍ في مُتَغَيِّر) ولا
    /// «الافتِراضُ sandbox» (فَتُقبَل مَدفوعاتٌ حَقيقِيَّةٌ عَلى
    /// حِسابِ اختِبار).
    /// </summary>
    public static string? BaseUrlFor(string? environment) => environment?.Trim().ToLowerInvariant() switch
    {
        Sandbox => SandboxBaseUrl,
        Live    => LiveBaseUrl,
        _       => null
    };

    /// <summary>
    /// <para><b>عُنوانُ الخُطَّةِ كَمَورِدٍ في الواجِهَة</b> —
    /// <c>{المُضيف}/v1/billing/plans/{المُعَرِّف}</c>، و<c>null</c>
    /// لِبيئَةٍ خارِجَ المَعجَم.</para>
    ///
    /// <para><b>ولِماذا عُنوانُ المَورِدِ لا صَفحَةُ اللَوحَة</b>:
    /// عُنوانُ المَورِدِ <b>مَنصوصٌ عَلَيه في مُواصَفَةِ PayPal</b>
    /// (‏<c>GET /v1/billing/plans/{id}</c>)، وعُنوانُ صَفحَةِ الخُطَّةِ
    /// في اللَوحَةِ **لَم يُقرَأ مِن مَصدَرٍ رَسميّ** — وبِناؤُه
    /// بِالتَخمينِ رابِطٌ يَنتَهي إلى ‏404 لِمُشرِفٍ يَبحَث عَن
    /// خُطَّتِه (القاعِدَة ١٦). وهُوَ <b>يُعرَض نَصّاً لا رابِطاً
    /// يُنقَر</b>: نِداءُ المَورِدِ يَحتاج رَمزاً، ورابِطٌ يَرُدّ
    /// ‏401 مَدخَلٌ يَضُرّ (القاعِدَة ١٢).</para>
    /// </summary>
    public static string? PlanResourceUrl(string? environment, string? planId)
        => BaseUrlFor(environment) is { } host && !string.IsNullOrWhiteSpace(planId)
            ? $"{host}/v1/billing/plans/{planId.Trim()}"
            : null;

    /// <summary>أَمُهَيَّأٌ لِلنِداء؟ الاعتِمادُ والبيئَةُ — <b>ولَيسَ
    /// <c>WebhookId</c></b>: إنشاءُ رابِطِ اشتِراكٍ لا يَحتاجُه،
    /// واستِقبالُ رِسالَةٍ يَحتاجُه. فَصلُهُما يَجعَل المالِكَ
    /// يُشَغِّلُ الرابِطَ قَبلَ أَن يُنشِئَ الـWebhook، ولا يَجعَل
    /// النُقطَةَ تَقبَل رِسالَةً بِلا تَحَقُّق.</summary>
    public static bool IsConfigured(PayPalOptions? o)
        => o is not null
           && !string.IsNullOrWhiteSpace(o.ClientId)
           && !string.IsNullOrWhiteSpace(o.ClientSecret)
           && BaseUrlFor(o.Environment) is not null;

    /// <summary>أَتُقبَلُ رِسالَةُ Webhook أَصلاً؟ <b>مُعَرِّفُ
    /// الـWebhook شَرطٌ لا تَحسين</b> — بِدونِه لا سَبيلَ إلى
    /// التَحَقُّق، فَالبابُ يُغلَق ولا يُفتَح على أَمَل.</summary>
    public static bool CanVerifyWebhooks(PayPalOptions? o)
        => IsConfigured(o) && !string.IsNullOrWhiteSpace(o!.WebhookId);

    /// <summary>مُهلَةٌ بِثَوانٍ، والصِفرُ أَو السالِبُ يَرتَدُّ إلى
    /// الافتِراض. نَفسُ شَكلِ <c>OtpSendGuard.Timeout</c>.</summary>
    public static TimeSpan Timeout(int seconds)
        => TimeSpan.FromSeconds(seconds > 0 ? seconds : DefaultTimeoutSeconds);
}
