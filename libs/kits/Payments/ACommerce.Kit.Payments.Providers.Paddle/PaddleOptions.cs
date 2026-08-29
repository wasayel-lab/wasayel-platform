namespace ACommerce.Kit.Payments.Providers.Paddle;

// ═══ Paddle — تاجِرُ التَسجيل، بِجِوارِ PayPal لا بَدَلاً مِنه ═════════
//
// **العِلَّةُ المَقيسَةُ الَّتي كَتَبَت هذا المِلَفّ**: ‏PayPal تَطلُب
// مِن الدافِعِ **حِسابَ PayPal** ولا تَعرِض نَموذَجَ بِطاقَة — جُرِّب
// `GUEST_CHECKOUT` في `experience_context` وجُرِّبَ الإعدادُ في
// الحِسابِ نَفسِه فَلَم يُفتَح. وزَبائِنُ المالِكِ السُعودِيُّونَ
// يَدفَعونَ **بِبِطاقَة** بِلا مَحفَظَة. و‏Paddle **تاجِرُ تَسجيل**
// (‏merchant of record): تَقبِضُ هي بِاسمِها، فَتَقبَل البِطاقَةَ
// مُباشَرَةً بِلا سِجِلٍّ تِجارِيٍّ لِلبائِع.
//
// **وهذا مُزَوِّدٌ ثانٍ لا بَديل**: مَسارُ PayPal باقٍ بِلا تَغييرِ
// حَرف، والشاشَةُ تَعرِض **المُهَيَّأَ مِنهُما** ولا تَعرِض واحِداً
// بِلا تَهيئَة (القاعِدَة ١٢).
//
// **ولا قيمَةَ افتِراضِيَّةً لِسِرٍّ ولا لِبيئَة**: الغِيابُ يُغلِق،
// و**قيمَةُ بيئَةٍ خارِجَ المَعجَمِ تُفشِلُ الإقلاع** — مُضيفٌ يُخمَّن
// يَعني إمّا نِداءَ اختِبارٍ يُظَنُّ حَقيقِيّاً أَو العَكس.

/// <summary>
/// إعداداتُ Paddle — كُلُّها مِن قِسم <c>Payments:Paddle</c>، وفي
/// الـSpace بِشَرطَتَينِ سُفلِيَّتَين
/// (<c>Payments__Paddle__ApiKey</c>).
/// </summary>
public sealed class PaddleOptions
{
    /// <summary><c>Payments:Paddle:Environment</c> — <c>sandbox</c> أَو
    /// <c>live</c> حَصراً. <b>وقيمَةٌ ثالِثَةٌ تُفشِل الإقلاع</b>
    /// (<see cref="PaddleExtensions.AddPaddleBilling"/>)، والفَراغُ
    /// يَعني «لا Paddle في هذِه النُسخَة» ولا يَرمي.</summary>
    public string Environment { get; set; } = "";

    /// <summary><c>Payments:Paddle:ApiKey</c> — مِفتاحُ الخادِم
    /// (<c>pdl_…</c>). <b>لا يُكتَب في لوغٍ ولا في رِسالَةِ خَطَإ</b>
    /// (مُثَبَّتٌ بِاختِبارٍ سالِب).</summary>
    public string ApiKey { get; set; } = "";

    /// <summary>
    /// <para><c>Payments:Paddle:WebhookSecret</c> — <b>سِرُّ وِجهَةِ
    /// الإشعار</b> (<c>pdl_ntfset_…</c>)، وهُوَ ما يُوَقَّعُ بِه
    /// الجِسم.</para>
    ///
    /// <para><b>وهُوَ غَيرُ <see cref="ApiKey"/> — والخَلطُ بَينَهُما
    /// هُوَ العَطَبُ الأَوَّلُ المُتَوَقَّع</b>: كِلاهُما يَبدَأ
    /// <c>pdl_</c>، والتَوقيعُ بِمِفتاحِ الـAPI يُنتِج بَصمَةً لا
    /// تُطابِق شَيئاً — فَتُرفَض كُلُّ رِسالَةٍ صَحيحَة، ويَبدو العَطَبُ
    /// «‏Paddle لا تُرسِل».</para>
    /// </summary>
    public string WebhookSecret { get; set; } = "";

    /// <summary><c>Payments:Paddle:ClientToken</c> — رَمزُ العَميل
    /// (<c>live_…</c>/<c>test_…</c>) الَّذي تَقرَؤُه
    /// <c>paddle.js</c> في صَفحَةِ الدَفع. <b>عَلَنيٌّ بِالتَصميم</b>:
    /// يُرسَل إلى كُلِّ مُتَصَفِّحٍ يَفتَح الصَفحَة، ولِذلك
    /// <b>يُقرَأُ مِن نُقطَةٍ عامَّة</b> ولا يُعامَل مُعامَلَةَ
    /// السِرّ.</summary>
    public string ClientToken { get; set; } = "";

    /// <summary>
    /// <para><c>Payments:Paddle:DefaultPaymentLink</c> — <b>صَفحَةُ
    /// الدَفعِ الَّتي نَستَضيفُها نَحن</b>، وهي نَفسُ العُنوانِ
    /// المَكتوبِ في لَوحَةِ Paddle تَحتَ «‏Default payment link».</para>
    ///
    /// <para><b>ورابِطُ الدَفعِ = هذا + <c>?_ptxn=&lt;txn&gt;</c></b>.
    /// فَبِلا هذا العُنوانِ يُنشَأُ الطَلَبُ ولا يُفتَح — <b>ومَدخَلٌ
    /// يَضُرّ أَسوَأُ مِن غِيابِ مَدخَل</b> (القاعِدَة ١٢)، ولِذلك
    /// يَدخُل في <see cref="PaddleEnvironment.CanSell"/>.</para>
    /// </summary>
    public string DefaultPaymentLink { get; set; } = "";

    /// <summary>مُهلَةُ النِداء بِالثَواني —
    /// <see cref="PaddleEnvironment.DefaultTimeoutSeconds"/>، والصِفرُ
    /// أَو السالِبُ يَرتَدُّ إلَيها.</summary>
    public int TimeoutSeconds { get; set; } = PaddleEnvironment.DefaultTimeoutSeconds;
}

/// <summary>
/// <para><b>قَرارُ «أَيُّ مُضيفٍ، وما الَّذي نَقدِرُ عَلَيه؟» — دالّاتٌ
/// نَقِيَّة.</b> لا HTTP ولا وِعاءَ خِدمات، فَتُقاس بِجَدوَلٍ كَما
/// تُقاس <c>PayPalEnvironment</c> جارَتُها — والحَدُّ الَّذي لا يُقاس
/// آلِيّاً يَنهار (القاعِدَة ٢).</para>
///
/// <para><b>وثَلاثُ دَرَجاتٍ لا واحِدَة</b>، ولِكُلٍّ سُؤالٌ مُختَلِف:
/// أَنَستَطيعُ النِداء؟ أَنَستَطيعُ التَحَقُّقَ مِن رِسالَة؟ أَنَستَطيعُ
/// أَن نَبيعَ فِعلاً؟ ودَمجُها في واحِدَةٍ يَجعَل الشاشَةَ تَرسِم
/// بِطاقَةً تُنشِئ رابِطاً لا يُفتَح، أَو تَقبَل مالاً لا يُمَدِّد.</para>
/// </summary>
public static class PaddleEnvironment
{
    // ─── مَفاتيحُ التَهيئَة — مَوضِعٌ واحِدٌ يَقرَؤُه المُنتِجُ
    //     والمُختَبِرُ ووَثيقَةُ النَشر ───────────────────────────────
    public const string SectionKey            = "Payments:Paddle";
    public const string EnvironmentKey        = "Payments:Paddle:Environment";
    public const string ApiKeyKey             = "Payments:Paddle:ApiKey";
    public const string WebhookSecretKey      = "Payments:Paddle:WebhookSecret";
    public const string ClientTokenKey        = "Payments:Paddle:ClientToken";
    public const string DefaultPaymentLinkKey = "Payments:Paddle:DefaultPaymentLink";

    public const string Sandbox = "sandbox";
    public const string Live    = "live";

    public const string SandboxBaseUrl = "https://sandbox-api.paddle.com";
    public const string LiveBaseUrl    = "https://api.paddle.com";

    /// <summary>خَمسَ عَشرَةَ ثانِيَة — نَفسُ رَقَمِ جارَتِها
    /// <c>PayPalEnvironment.DefaultTimeoutSeconds</c>، فَلا يَختَلِف
    /// صَبرُ نُقطَتَينِ على نَفسِ الشاشَة.</summary>
    public const int DefaultTimeoutSeconds = 15;

    /// <summary>مُتَغَيِّرُ البيئَةِ المُقابِلُ لِمِفتاحِ التَهيئَة
    /// (<c>:</c> ← <c>__</c>) — وهُوَ ما يَكتُبُه المالِكُ في
    /// الـSpace.</summary>
    public static string EnvVarName(string configKey) => configKey.Replace(":", "__");

    /// <summary>
    /// المُضيفُ لِقيمَةِ البيئَة، و<c>null</c> لِقيمَةٍ خارِجَ
    /// المَعجَم — <b>وهذا هُوَ الفَشَلُ المُغلَق</b>: لا «الافتِراضُ
    /// live» (فَيُنادى الإنتاجُ بِخَطَإ إملاءٍ في مُتَغَيِّر) ولا
    /// «الافتِراضُ sandbox» (فَتُقبَل مَدفوعاتٌ حَقيقِيَّةٌ عَلى
    /// حِسابِ اختِبار).
    /// </summary>
    public static string? BaseUrlFor(string? environment)
        => environment?.Trim().ToLowerInvariant() switch
        {
            Sandbox => SandboxBaseUrl,
            Live    => LiveBaseUrl,
            _       => null
        };

    /// <summary><b>قيمَةٌ مَكتوبَةٌ خارِجَ المَعجَم</b> — تُفَرَّق عَن
    /// الفَراغ: الفَراغُ «لا Paddle»، وهذِه <b>خَطَأُ إملاءٍ يُفشِلُ
    /// الإقلاع</b>.</summary>
    public static bool IsMisconfiguredEnvironment(string? environment)
        => !string.IsNullOrWhiteSpace(environment) && BaseUrlFor(environment) is null;

    /// <summary>أَمُهَيَّأٌ لِلنِداء؟ مِفتاحُ الـAPI والبيئَةُ
    /// المَعروفَة — <b>ولَيسَ سِرَّ التَوقيع</b>: إنشاءُ مُعامَلَةٍ لا
    /// يَحتاجُه، واستِقبالُ رِسالَةٍ يَحتاجُه.</summary>
    public static bool IsConfigured(PaddleOptions? o)
        => o is not null
           && !string.IsNullOrWhiteSpace(o.ApiKey)
           && BaseUrlFor(o.Environment) is not null;

    /// <summary>أَتُقبَلُ رِسالَةُ Webhook أَصلاً؟ <b>سِرُّ الوِجهَةِ
    /// شَرطٌ لا تَحسين</b> — بِدونِه لا سَبيلَ إلى التَحَقُّق،
    /// فَالبابُ يُغلَق ولا يُفتَح على أَمَل.</summary>
    public static bool CanVerifyWebhooks(PaddleOptions? o)
        => IsConfigured(o) && !string.IsNullOrWhiteSpace(o!.WebhookSecret);

    /// <summary>
    /// <para><b>أَنَبيعُ فِعلاً؟</b> — وهذا وَحدَه ما يَرسُم البِطاقَةَ
    /// في <c>/admin</c>.</para>
    ///
    /// <para><b>ويَزيدُ شَرطَين على التَحَقُّق، ولِكُلٍّ عَطَبُه
    /// المُقابِل</b>: بِلا <see cref="PaddleOptions.ClientToken"/>
    /// تَفتَح صَفحَتُنا ولا تُهَيِّئ <c>paddle.js</c> فَلا تُعرَض
    /// نافِذَةُ الدَفع؛ وبِلا
    /// <see cref="PaddleOptions.DefaultPaymentLink"/> لا يوجَد عُنوانٌ
    /// يُلحَقُ بِه <c>?_ptxn=</c> أَصلاً. <b>وكِلاهُما رابِطٌ يُرسَل
    /// إلى رائِدِ أَعمالٍ فَلا يُفضي إلى شَيء</b> — وذاكَ مَدخَلٌ
    /// يَضُرّ.</para>
    /// </summary>
    public static bool CanSell(PaddleOptions? o)
        => CanVerifyWebhooks(o)
           && !string.IsNullOrWhiteSpace(o!.ClientToken)
           && !string.IsNullOrWhiteSpace(o.DefaultPaymentLink);

    /// <summary>مُهلَةٌ بِثَوانٍ، والصِفرُ أَو السالِبُ يَرتَدُّ إلى
    /// الافتِراض. نَفسُ شَكلِ <c>PayPalEnvironment.Timeout</c>.</summary>
    public static TimeSpan Timeout(int seconds)
        => TimeSpan.FromSeconds(seconds > 0 ? seconds : DefaultTimeoutSeconds);
}
