using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ACommerce.Kit.Payments.Providers.PayPal;

/// <summary>
/// <para><b>مُزَوِّدُ PayPal — الثالِثُ بِجِوارِ Moyasar وNoon، ولِتَدَفُّقٍ
/// آخَر.</b> ذانِكَ لِمَدفوعاتِ مُشتَري المَتجَر (‏KSA، بِطاقات)، وهذا
/// لِـ<b>اشتِراكِ المُستَأجِرِ في وَسايِل</b>. ونَفسُ نَمَطِهِما حَرفاً:
/// خِياراتٌ + <c>AddHttpClient</c> بِمُهلَة + رَميٌ عِندَ الفَشَل، ولا
/// حُزمَةَ SDK — HTTP خامٌّ إلى واجِهَةٍ مُوَثَّقَة.</para>
///
/// <para><b>وما لا يُنَفَّذ يُقالُ بِاسمِه</b>: الحَجزُ والخَصمُ
/// والاستِردادُ يَرمونَ <see cref="NotSupportedException"/> <b>تُسَمّي
/// البَديل</b>. وتَنفيذٌ شَكليٌّ يُرجِع <c>true</c> أَخطَرُ مِن
/// رَمي: يَجعَل استِرداداً لَم يَقَع يَبدو واقِعاً.</para>
/// </summary>
public sealed class PayPalPaymentProvider : IPaymentProvider
{
    // ─── نِقاطُ الواجِهَة — مُثَبَّتَةٌ لِيَقرَأَها الاختِبارُ بَدَلَ
    //     أَن يَنسَخَها ───────────────────────────────────────────────
    public const string TokenPath         = "/v1/oauth2/token";
    public const string SubscriptionsPath = "/v1/billing/subscriptions";
    public const string VerifySignaturePath = "/v1/notifications/verify-webhook-signature";
    public const string CapturesPath      = "/v2/payments/captures";

    /// <summary>كاتالوجُ المُنتَجات — <c>POST</c> يُنشِئ، وهُوَ ما
    /// يُغني عَن صَفحَةِ المُنتَجاتِ في اللَوحَة.</summary>
    public const string ProductsPath      = "/v1/catalogs/products";

    /// <summary>خُطَطُ الفَوتَرَة — والمُعَرِّفُ العائِدُ مِنها
    /// (‏<c>P-…</c>) هُوَ ما يُخَزَّن ويُمَرَّر إلى إنشاءِ
    /// الاشتِراك.</summary>
    public const string PlansPath         = "/v1/billing/plans";

    /// <summary>اسمُ العَميلِ المُطَبَّع — التَسجيلُ والحَلُّ يَقرَآنِه
    /// مِن هُنا.</summary>
    public const string HttpClientName = "paypal";

    /// <summary>رَأسُ مَرَّة-واحِدَة عِندَ PayPal — <b>يَحفَظُه الخادِمُ
    /// ‏72 ساعَة</b>، وإعادَةُ المُحاوَلَةِ بِلا مِفتاحٍ تُنشِئ خُطَّةً
    /// (أَو اشتِراكاً) ثانِياً. مَوضِعٌ واحِدٌ يَقرَؤُه المُنتِجُ
    /// والمُختَبِر.</summary>
    public const string RequestIdHeader = "PayPal-Request-Id";

    private readonly PayPalOptions _opts;
    private readonly HttpClient _http;
    private readonly PayPalTokenCache _tokens;
    private readonly ILogger<PayPalPaymentProvider> _logger;
    private readonly string _baseUrl;

    public PayPalPaymentProvider(
        IOptions<PayPalOptions> opts, HttpClient http,
        PayPalTokenCache tokens, ILogger<PayPalPaymentProvider> logger)
    {
        _opts = opts.Value;
        _http = http;
        _tokens = tokens;
        _logger = logger;

        // فَشَلٌ عِندَ التَركيبِ لا عِندَ الطَلَب، ويُسَمّي مِفتاحَ
        // التَهيئَة — نَفسُ شَكلِ `BrevoEmailChannel`. ورِسالَةٌ تَقول
        // «فَشِل الدَفع» عِندَ أَوَّلِ دافِعٍ أَسوَأُ مِن إقلاعٍ يَشتَكي.
        if (string.IsNullOrWhiteSpace(_opts.ClientId))
            throw new InvalidOperationException(
                $"PayPal ClientId غَير مُعَرَّف ({PayPalEnvironment.ClientIdKey}).");
        if (string.IsNullOrWhiteSpace(_opts.ClientSecret))
            throw new InvalidOperationException(
                $"PayPal ClientSecret غَير مُعَرَّف ({PayPalEnvironment.ClientSecretKey}).");

        _baseUrl = PayPalEnvironment.BaseUrlFor(_opts.Environment)
            ?? throw new InvalidOperationException(
                $"PayPal Environment «{_opts.Environment}» خارِجَ المَعجَم " +
                $"({PayPalEnvironment.Sandbox}/{PayPalEnvironment.Live}) — " +
                $"اضبِط {PayPalEnvironment.EnvironmentKey}.");
    }

    public string ProviderName => "PayPal";

    /// <summary>المُضيفُ المُختار — يَقرَؤُه الاختِبارُ فَلا يَبقى
    /// «‏sandbox أَم live» دَعوى.</summary>
    public string BaseUrl => _baseUrl;

    // ═══ الرَمز ═══════════════════════════════════════════════════════

    /// <summary>
    /// <para><b>‏OAuth2 <c>client_credentials</c> — مَرَّةً لِكُلّ
    /// ثَماني ساعاتٍ لا مَرَّةً لِكُلّ نِداء.</b> التَخزينُ في
    /// <see cref="PayPalTokenCache"/> المُفرَدَة، لِأَنّ هذا الصِنفَ
    /// عابِرٌ بِتَسجيلِ <c>AddHttpClient</c>.</para>
    ///
    /// <para>والاعتِمادُ يُرسَل <c>Basic</c> — نَفسُ شَكلِ Moyasar،
    /// و<b>لا يُكتَبُ في لوغ</b>.</para>
    /// </summary>
    public Task<string> AccessTokenAsync(CancellationToken ct = default)
        => _tokens.GetAsync(FetchTokenAsync, DateTimeOffset.UtcNow, ct);

    private async Task<(string Token, int ExpiresInSeconds)> FetchTokenAsync(CancellationToken ct)
    {
        using var msg = new HttpRequestMessage(HttpMethod.Post, _baseUrl + TokenPath)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials"
            })
        };
        msg.Headers.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_opts.ClientId}:{_opts.ClientSecret}")));

        using var resp = await SendAsync(msg, ct);
        if (!resp.IsSuccessStatusCode)
        {
            _logger.LogError("[PayPal] فَشِل طَلَبُ الرَمز {Status}", resp.StatusCode);
            throw new InvalidOperationException($"PayPal فَشِل طَلَبُ الرَمز: {(int)resp.StatusCode}");
        }

        var dto = await resp.Content.ReadFromJsonAsync<TokenDto>(cancellationToken: ct);
        if (dto is null || string.IsNullOrWhiteSpace(dto.access_token))
            throw new InvalidOperationException("PayPal رَدَّ بِلا access_token.");

        return (dto.access_token, dto.expires_in);
    }

    private async Task<HttpRequestMessage> AuthorizedAsync(
        HttpMethod method, string path, CancellationToken ct)
    {
        var msg = new HttpRequestMessage(method, _baseUrl + path);
        msg.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer", await AccessTokenAsync(ct));
        return msg;
    }

    // ═══ الكاتالوج: مُنتَجٌ ثُمَّ خُطَّة — بِلا لَوحَة ═════════════════
    //
    // **العِلَّة**: خُطُواتُ `docs/DEPLOY.md` §٢·ج كانَت تَفتَرِض صَفحَةَ
    // المُنتَجات/الخُطَط في لَوحَةِ PayPal، **وقَد تَعَذَّرَ على المالِكِ
    // فَتحُها**. والواجِهَةُ REST تُنشِئُهُما بِنِداءَين — فَاللَوحَةُ
    // تَصير طَريقاً أَوَّلَ لا شَرطاً.
    //
    // **والتَرتيبُ مُلزِمٌ لا تَفضيليّ**: مُنتَجٌ ثُمَّ خُطَّة. مُنشِئُ
    // الخُطَّةِ يَشتَرِط `product_id` قائِماً، فَلا خُطَّةَ بِلا مُنتَج.
    //
    // **والفَشَلُ يَرمي ولا يُرجِع نَتيجَةً صامِتَة** (نَفسُ نَمَطِ
    // `BrevoEmailChannel`): هذا نِداءٌ يُنادِيه **مُشرِفٌ يَنتَظِرُ
    // شاشَة**، لا رِسالَةٌ آلِيَّةٌ تُعيدُها PayPal. ونَتيجَةٌ فارِغَةٌ
    // تَعني «أُنشِئَت خُطَّةٌ لا مُعَرِّفَ لَها» — وذاكَ يُخَزَّن
    // فَيَنكَسِر الدَفعُ بَعدَ أَيّام. والرِسالَةُ **تَحمِل رَمزَ PayPal
    // ونَصَّه** لِيُعرَفَ ما يُصلَح، **ولا تَحمِل سِرّاً** (مُثَبَّتٌ
    // بِاختِبارٍ سالِب).

    /// <summary>
    /// <para><b>يُنشِئ مُنتَجَ الكاتالوجِ ويُعيد مُعَرِّفَه
    /// (‏<c>PROD-…</c>).</b></para>
    ///
    /// <para><b>ولا يُمَرَّرُ <c>id</c> إطلاقاً — وهذا فَخٌّ حَقيقيّ</b>:
    /// مُخَطَّطُ المُنتَجِ يَسمَح بِمُعَرِّفٍ مِن ‏6 إلى ‏50 مِحرَفاً،
    /// لكِنّ <b>مُنشِئَ الخُطَّةِ يَشتَرِط ‏22 مِحرَفاً بِالضَبط</b>
    /// ونَمَط <c>^PROD-[A-Z0-9]*$</c>. فَتَمريرُ SKU خاصٍّ بِنا
    /// <b>يَنجَح في إنشاءِ المُنتَج ثُمَّ تُرفَض الخُطَّة</b> — مُنتَجٌ
    /// يَتيمٌ عِندَ PayPal ورِسالَةُ خَطَإٍ تُشير إلى النِداءِ الخَطَإ.
    /// فَالمُعَرِّفُ يُتركُ لِـPayPal ويُخَزَّنُ ما تُعيدُه.</para>
    /// </summary>
    public async Task<string> CreateCatalogProductAsync(
        PayPalPlanDraft draft, CancellationToken ct = default)
    {
        var dto = await PostForAsync<CatalogIdDto>(
            ProductsPath, PayPalCatalogPolicy.ProductRequestId(draft),
            new Dictionary<string, object>
            {
                ["name"]     = draft.TrimmedName,
                ["type"]     = PayPalCatalogPolicy.ProductType,
                ["category"] = PayPalCatalogPolicy.ProductCategory,
            },
            "إنشاءَ مُنتَجِ الكاتالوج", ct);

        return string.IsNullOrWhiteSpace(dto.id)
            ? throw new InvalidOperationException("PayPal رَدَّ بِلا مُعَرِّفِ مُنتَج.")
            : dto.id!;
    }

    /// <summary>
    /// <para><b>يُنشِئ خُطَّةَ الفَوتَرَةِ ويُعيد مُعَرِّفَها
    /// (‏<c>P-…</c>)</b> — وهُوَ ما يُخَزَّن ويُمَرَّر إلى
    /// <see cref="CreateSubscriptionAsync"/> بَعدَ ذلك.</para>
    ///
    /// <para><b>ودَورَةٌ اعتِيادِيَّةٌ واحِدَةٌ بِلا تَجرِبَة</b>:
    /// <c>total_cycles = 0</c> أَي **لا نِهائِيَّة** — وهُوَ المَوضِعُ
    /// الَّذي يُضبَط فيه التَجديدُ الدائِم، لا <c>auto_renewal</c>
    /// المُهمَلَة على الاشتِراك.</para>
    ///
    /// <para><b>والافتِراضانِ يُضبَطانِ صَراحَةً لِأَنَّهُما قاسِيان</b>:
    /// <c>setup_fee_failure_action</c> افتِراضُها <b>CANCEL</b> و
    /// <c>payment_failure_threshold</c> افتِراضُها <b>صِفر</b> — أَي
    /// إلغاءُ اشتِراكِ مَتجَرٍ عِندَ **أَوَّلِ** تَعَثُّرِ بِطاقَة.
    /// فَتُكتَبانِ <c>CONTINUE</c> و<c>3</c> كَما في مِثالِ PayPal
    /// الرَسميّ.</para>
    ///
    /// <para><b>ولا يُرسَل وَصف</b>: حَقلٌ اختِيارِيٌّ يُملَأُ بِنَصٍّ
    /// مُخترَعٍ بَياناتُ مُنتَجٍ لا تُخترَع (القاعِدَة ١٦) — وسَقفُه
    /// هُنا ‏127 لا ‏256 كَوَصفِ المُنتَج، فَرقٌ يُنسى فَيَرتَدُّ
    /// النِداء.</para>
    /// </summary>
    public async Task<string> CreateBillingPlanAsync(
        string productId, PayPalPlanDraft draft, CancellationToken ct = default)
    {
        var dto = await PostForAsync<CatalogIdDto>(
            PlansPath, PayPalCatalogPolicy.PlanRequestId(productId, draft),
            new Dictionary<string, object>
            {
                ["product_id"] = productId,
                ["name"]       = draft.TrimmedName,
                ["status"]     = PayPalCatalogPolicy.PlanStatusActive,
                ["billing_cycles"] = new object[]
                {
                    new Dictionary<string, object>
                    {
                        ["tenure_type"]  = PayPalCatalogPolicy.TenureRegular,
                        ["sequence"]     = 1,
                        ["total_cycles"] = PayPalCatalogPolicy.InfiniteCycles,
                        ["frequency"] = new Dictionary<string, object>
                        {
                            ["interval_unit"]  = draft.NormalizedInterval,
                            ["interval_count"] = 1,
                        },
                        ["pricing_scheme"] = new Dictionary<string, object>
                        {
                            ["fixed_price"] = new Dictionary<string, object>
                            {
                                // سِلسِلَةٌ نَصِّيَّةٌ لا رَقَم — نَمَطُ
                                // PayPal يَشتَرِط ذلك حَرفاً.
                                ["value"]         = PayPalCurrencies.Money(
                                                        draft.Amount, draft.NormalizedCurrency),
                                ["currency_code"] = draft.NormalizedCurrency,
                            },
                        },
                    },
                },
                ["payment_preferences"] = new Dictionary<string, object>
                {
                    ["auto_bill_outstanding"]    = true,
                    ["setup_fee_failure_action"] = PayPalCatalogPolicy.SetupFeeFailureAction,
                    ["payment_failure_threshold"] = PayPalCatalogPolicy.PaymentFailureThreshold,
                },
            },
            "إنشاءَ خُطَّةِ الفَوتَرَة", ct);

        return string.IsNullOrWhiteSpace(dto.id)
            ? throw new InvalidOperationException("PayPal رَدَّ بِلا مُعَرِّفِ خُطَّة.")
            : dto.id!;
    }

    /// <summary>نِداءُ إنشاءٍ واحِدٌ: رَأسُ مَرَّة-واحِدَة، وجِسمٌ،
    /// ورَدٌّ يُقرَأ أَو يَرمي بِرَمزِ PayPal ونَصِّه.</summary>
    private async Task<T> PostForAsync<T>(
        string path, string requestId, object body, string whatAr, CancellationToken ct)
        where T : class
    {
        using var msg = await AuthorizedAsync(HttpMethod.Post, path, ct);
        msg.Headers.Add(RequestIdHeader, requestId);
        msg.Content = JsonContent.Create(body);

        using var resp = await SendAsync(msg, ct);
        var raw = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
        {
            if (resp.StatusCode == HttpStatusCode.Unauthorized) _tokens.Invalidate();
            _logger.LogError("[PayPal] فَشِل {What} {Status}: {Body}", whatAr, resp.StatusCode, raw);
            throw new InvalidOperationException(
                $"PayPal فَشِل {whatAr}: {(int)resp.StatusCode} — {Describe(raw)}");
        }

        return Deserialize<T>(raw)
            ?? throw new InvalidOperationException($"PayPal رَدَّ على {whatAr} بِجِسمٍ غَيرِ مَقروء.");
    }

    /// <summary>رَمزُ الخَطَإ ونَصُّه كَما تَقولُهُما PayPal —
    /// <b>لِيُعرَفَ ما يُصلَح</b>. و«‏422» وَحدَها تُرسِل المالِكَ
    /// يُخَمِّن، و«‏UNIT_AMOUNT_NOT_ALLOWED» تَقولُ لَه أَينَ
    /// المُشكِلَة.</summary>
    private static string Describe(string body)
    {
        var e = Deserialize<ErrorDto>(body);
        var code  = string.IsNullOrWhiteSpace(e?.name) ? "?" : e!.name!;
        var issue = e?.details is { Length: > 0 } && !string.IsNullOrWhiteSpace(e.details[0].issue)
            ? $" [{e.details[0].issue}]" : "";
        var text  = string.IsNullOrWhiteSpace(e?.message) ? "" : $" — {e!.message}";
        return code + issue + text;
    }

    // ═══ الاشتِراك ════════════════════════════════════════════════════

    /// <summary>
    /// <para><b>يُنشِئ اشتِراكاً ويُعيد رابِطَ المُوافَقَة.</b>
    /// <c>req.PlanId</c> هُوَ <c>paypalPlanId</c> مِن تَعريفِ الباقَة،
    /// و<c>req.CustomerId</c> هُوَ <b>سلاجُ المُستَأجِر</b> — يُوضَع في
    /// <c>custom_id</c> فَتَعودُ بِه كُلُّ رِسالَةٍ لاحِقَة. وهذا هُوَ
    /// الرِباطُ الوَحيدُ بَينَ دافِعٍ في PayPal ومَتجَرٍ عِندَنا؛ وبِلا
    /// وَضعِه هُنا تَصير كُلُّ دَفعَةٍ «مُستَأجِراً مَجهولاً».</para>
    ///
    /// <para><b>و<c>MonthlyAmountSar</c> لا يُرسَل — ويُقالُ لِماذا</b>:
    /// السِعرُ يَسكُن في خُطَّةِ PayPal الَّتي أَنشَأَها المالِك، وهُوَ
    /// ما سَتَخصِمُه فِعلاً. فَإرسالُ رَقَمٍ ثانٍ يُنشِئ <b>تَعريفَينِ
    /// لِقيمَةٍ واحِدَة</b> — أَحَدُهُما يُعرَض والآخَرُ يُخصَم، وهُوَ
    /// عَينُ الانجِرافِ الَّذي تَمنَعُه القاعِدَة ٤.</para>
    /// </summary>
    public async Task<SubscriptionResult> CreateSubscriptionAsync(
        SubscriptionRequest req, string idempotencyKey, CancellationToken ct = default)
    {
        using var msg = await AuthorizedAsync(HttpMethod.Post, SubscriptionsPath, ct);

        // ‏PayPal-Request-Id هُوَ مِفتاحُ مَرَّة-واحِدَة عِندَ PayPal —
        // نَفسُ دَورِ `X-Idempotency-Key` في Moyasar، بِاسمٍ آخَر.
        msg.Headers.Add(RequestIdHeader, idempotencyKey);
        msg.Content = JsonContent.Create(new Dictionary<string, object>
        {
            ["plan_id"]   = req.PlanId,
            ["custom_id"] = req.CustomerId,
        });

        using var resp = await SendAsync(msg, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
        {
            if (resp.StatusCode == HttpStatusCode.Unauthorized) _tokens.Invalidate();
            _logger.LogError("[PayPal] فَشِل إنشاءُ الاشتِراك {Status}: {Body}", resp.StatusCode, body);

            // **الرَمزُ والنَصُّ كَما تَقولُهُما PayPal، لا رَقَمُ
            // الحالَةِ وَحدَه** — ونَفسُ صِياغَةِ مَسارِ الخُطَّةِ
            // حَرفاً، فَيَقرَؤُهُما `PayPalFailure.ScreenCode` بِقاعِدَةٍ
            // واحِدَة. و«‏422» وَحدَها كانَت تَبتَلِع
            // `Merchant not enabled for reference transaction` — وهُوَ
            // **الخَطَأُ الَّذي يُنتَظَر وُقوعُه هُنا بِالذاتِ**: خُطَّةٌ
            // تَنجَح ثُمَّ أَوَّلُ اشتِراكٍ يَفشَل بِعَطَبِ استِحقاق.
            // و`Describe` تَقرَأُ **جِسمَ رَدِّ PayPal وَحدَه** فَلا
            // سِرَّ فيها (مُثَبَّتٌ بِاختِبارٍ سالِب).
            return new SubscriptionResult("", false, default,
                $"PayPal فَشِل إنشاءُ الاشتِراك: {(int)resp.StatusCode} — {Describe(body)}");
        }

        var dto = Deserialize<SubscriptionDto>(body);
        if (dto is null || string.IsNullOrWhiteSpace(dto.id))
            return new SubscriptionResult("", false, default, "PayPal رَدَّ بِلا مُعَرِّفِ اشتِراك.");

        var approve = dto.links?.FirstOrDefault(
            l => string.Equals(l.rel, "approve", StringComparison.OrdinalIgnoreCase))?.href;

        return new SubscriptionResult(
            dto.id,
            IsActive: string.Equals(dto.status, "ACTIVE", StringComparison.Ordinal),
            CurrentPeriodEnd: dto.billing_info?.next_billing_time ?? default,
            FailureReason: null,
            ApproveUrl: approve);
    }

    /// <summary><b>يُلغي التَجديدَ عِندَ PayPal.</b> ولا يُطفِئ مَتجَراً:
    /// الإخفاءُ عِندَنا مُشتَقٌّ مِن الوَقتِ وَحدَه، فَمَن أَلغى في
    /// مُنتَصَفِ شَهرِه يُكمِلُه (‏ADR-003 §٢-ج).</summary>
    public async Task<bool> CancelSubscriptionAsync(
        string subscriptionId, CancellationToken ct = default)
    {
        using var msg = await AuthorizedAsync(
            HttpMethod.Post, $"{SubscriptionsPath}/{subscriptionId}/cancel", ct);
        msg.Content = JsonContent.Create(new { reason = "cancelled by Wasayel platform admin" });

        using var resp = await SendAsync(msg, ct);
        if (resp.IsSuccessStatusCode) return true;

        if (resp.StatusCode == HttpStatusCode.Unauthorized) _tokens.Invalidate();
        _logger.LogError("[PayPal] فَشِل إلغاءُ الاشتِراك {Status}", resp.StatusCode);
        return false;
    }

    /// <summary>
    /// <para><b>عَمَلِيَّةُ خَصمٍ واحِدَةٌ مَقروءَةٌ مِن PayPal.</b>
    /// <paramref name="paymentId"/> هُوَ مُعَرِّفُ الـcapture.</para>
    ///
    /// <para><b>والضَريبَةُ صِفرٌ هُنا، ويُقالُ لِماذا</b>: ‏Moyasar
    /// يَشتَقُّ ‏15٪ لِأَنّ البائِعَ سُعوديٌّ والعُملَةُ ريال. وهُنا
    /// الدافِعُ قَد يَكونُ خارِجَ المَملَكَة والعُملَةُ لَيسَت
    /// بِالضَرورَةِ ريالاً، وتَسجيلُ وَسايِل الضَريبيُّ لَيسَ في هذا
    /// المِلَفّ. فَاشتِقاقُ ‏15٪ هُنا <b>رَقَمٌ مُخترَع على فاتورَة</b>
    /// (القاعِدَة ١٦) — والصِفرُ مَعَ <c>SellerVatNumber</c> فارِغاً
    /// يَقول «لَم يُحتَسَب»، ولا يَدَّعي «لا ضَريبَة».</para>
    /// </summary>
    public async Task<Invoice?> GetInvoiceAsync(string paymentId, CancellationToken ct = default)
    {
        using var msg = await AuthorizedAsync(HttpMethod.Get, $"{CapturesPath}/{paymentId}", ct);
        using var resp = await SendAsync(msg, ct);
        if (!resp.IsSuccessStatusCode)
        {
            if (resp.StatusCode == HttpStatusCode.Unauthorized) _tokens.Invalidate();
            return null;
        }

        var dto = Deserialize<CaptureDto>(await resp.Content.ReadAsStringAsync(ct));
        if (dto is null || string.IsNullOrWhiteSpace(dto.id)) return null;

        var total = decimal.TryParse(dto.amount?.value,
            System.Globalization.NumberStyles.Number,
            System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0m;

        return new Invoice(
            dto.id, $"PP-{dto.id}", total, 0m, total,
            "Wasayel", "", dto.create_time ?? default, "");
    }

    // ═══ التَحَقُّقُ مِن تَوقيعِ الرِسالَة ════════════════════════════

    /// <summary>
    /// <para><b>يَسأَل PayPal: أَأَنتِ مَن أَرسَلَ هذا؟</b> ويُرجِع
    /// <c>false</c> عِندَ أَيّ شَكّ — <b>فَشَلٌ مُغلَق</b>: غِيابُ
    /// <c>WebhookId</c>، رَأسٌ ناقِص، رَدٌّ غَيرُ ناجِح، حَقلٌ
    /// مَفقودٌ في الرَدّ، أَو عُطلُ شَبَكَة. كُلُّها «لا».</para>
    ///
    /// <para><b>والجِسمُ يُدرَج بِبايتاتِه لا بِإعادَةِ تَسَلسُل</b>:
    /// <c>webhook_event</c> يُبنى بِلَصقِ النَصِّ الوارِدِ كَما وَصَل
    /// داخِلَ الطَلَب. وإعادَةُ تَسَلسُلِه (فَكٌّ ثُمَّ كِتابَة) تُبَدِّل
    /// تَرتيبَ الحُقولِ وتَنسيقَ الأَرقام، وذاكَ يُفشِل تَحَقُّقاً
    /// صَحيحاً — <b>وفَشَلُ تَحَقُّقٍ صَحيحٍ يَبدو هُجوماً</b>.</para>
    /// </summary>
    public async Task<bool> VerifyWebhookSignatureAsync(
        PayPalWebhookHeaders headers, string rawBody, CancellationToken ct = default)
    {
        if (!PayPalEnvironment.CanVerifyWebhooks(_opts))
        {
            _logger.LogError("[PayPal] لا {Key} — تُرفَض الرِسالَةُ بِلا قِراءَة.",
                PayPalEnvironment.WebhookIdKey);
            return false;
        }
        if (!headers.IsComplete)
        {
            _logger.LogWarning("[PayPal] رَأسُ تَوقيعٍ ناقِص — تُرفَض الرِسالَة.");
            return false;
        }
        if (string.IsNullOrWhiteSpace(rawBody)) return false;

        var payload =
            "{" +
            $"\"auth_algo\":{Json(headers.AuthAlgo)}," +
            $"\"cert_url\":{Json(headers.CertUrl)}," +
            $"\"transmission_id\":{Json(headers.TransmissionId)}," +
            $"\"transmission_sig\":{Json(headers.TransmissionSig)}," +
            $"\"transmission_time\":{Json(headers.TransmissionTime)}," +
            $"\"webhook_id\":{Json(_opts.WebhookId)}," +
            $"\"webhook_event\":{rawBody}" +
            "}";

        try
        {
            using var msg = await AuthorizedAsync(HttpMethod.Post, VerifySignaturePath, ct);
            msg.Content = new StringContent(payload, Encoding.UTF8, "application/json");

            using var resp = await SendAsync(msg, ct);
            if (!resp.IsSuccessStatusCode)
            {
                if (resp.StatusCode == HttpStatusCode.Unauthorized) _tokens.Invalidate();
                _logger.LogWarning("[PayPal] تَحَقُّقُ التَوقيعِ رَدَّ {Status}", resp.StatusCode);
                return false;
            }

            var dto = Deserialize<VerificationDto>(await resp.Content.ReadAsStringAsync(ct));
            var ok = string.Equals(dto?.verification_status,
                PayPalBillingPolicy.VerificationSuccess, StringComparison.Ordinal);
            if (!ok) _logger.LogWarning("[PayPal] تَوقيعٌ غَيرُ صالِح — تُرفَض الرِسالَة.");
            return ok;
        }
        catch (Exception ex)
        {
            // عُطلُ شَبَكَةٍ عِندَ التَحَقُّقِ **لَيسَ قَبولاً**. ورَميٌ
            // هُنا كانَ سَيُخرِج ‏500 فَتُعيدُ PayPal الإرسالَ — وهذا
            // هُوَ الاتِّجاهُ الصَحيحُ لِلفَشَل، لكِنّ النُقطَةَ تَقولُه
            // بِرَمزٍ بَدَلَ أَثَرِ استِثناء.
            _logger.LogError(ex, "[PayPal] تَعَذَّرَ التَحَقُّقُ مِن التَوقيع.");
            return false;
        }
    }

    // ═══ ما لا يُنَفَّذ — يُسَمّي بَديلَه ═════════════════════════════

    private const string NotForShoppers =
        "PayPal في وَسايِل لِاشتِراكِ المُستَأجِرِ في المَنَصَّة وَحدَه " +
        "(‏ADR-003 §٢-ب). لِمَدفوعاتِ مُشتَري المَتجَر استَعمِل " +
        "Moyasar أَو Noon — وهُما مُنَفَّذانِ بِجِوارِ هذا المِلَفّ.";

    public Task<PaymentResult> AuthorizeAsync(
        PaymentRequest req, string idempotencyKey, CancellationToken ct = default)
        => throw new NotSupportedException("PayPal: لا حَجزَ هُنا. " + NotForShoppers);

    public Task<PaymentResult> CaptureAsync(
        string paymentId, decimal? amount = null, CancellationToken ct = default)
        => throw new NotSupportedException("PayPal: لا خَصمَ يَدَوِيّاً هُنا. " + NotForShoppers);

    public Task<PaymentResult> RefundAsync(
        string paymentId, decimal amount, string reason, CancellationToken ct = default)
        => throw new NotSupportedException(
            "PayPal: لا استِردادَ مِن الكود — يُرَدُّ الاشتِراكُ مِن لَوحَةِ PayPal " +
            "بِيَدِ المالِك، فَالاستِردادُ قَرارٌ ماليٌّ لا خُطوَةُ برنامَج. " + NotForShoppers);

    // ═══ أَدَوات ══════════════════════════════════════════════════════

    /// <summary>مُهلَةٌ ثانِيَةٌ إلى جانِبِ <c>HttpClient.Timeout</c>:
    /// الأولى تَحرُس النِداءَ، وهذِه تَربِطُه بِرَمزِ الطَلَب فَيَنقَطِع
    /// مَعَه. نَفسُ شَكلِ <c>BrevoEmailChannel</c>.</summary>
    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage msg, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(PayPalEnvironment.Timeout(_opts.TimeoutSeconds));
        return await _http.SendAsync(msg, cts.Token);
    }

    private static string Json(string s) => JsonSerializer.Serialize(s);

    private static T? Deserialize<T>(string body) where T : class
    {
        try { return JsonSerializer.Deserialize<T>(body); }
        catch { return null; }
    }

    // ─── DTOs — أَسماءُ PayPal حَرفاً، فَلا خَريطَةَ تُنسى ────────────
#pragma warning disable IDE1006 // أَسماءُ الواجِهَةِ الخارِجِيَّة snake_case
    private sealed record TokenDto(string access_token, int expires_in);
    private sealed record LinkDto(string? rel, string? href);
    private sealed record BillingInfoDto(DateTime? next_billing_time);
    private sealed record SubscriptionDto(
        string? id, string? status, LinkDto[]? links, BillingInfoDto? billing_info);
    private sealed record AmountDto(string? currency_code, string? value);
    private sealed record CaptureDto(string? id, AmountDto? amount, DateTime? create_time);
    private sealed record VerificationDto(string? verification_status);

    /// <summary>مُنتَجُ الكاتالوجِ وخُطَّةُ الفَوتَرَةِ يَرُدّانِ
    /// حُقولاً كَثيرَة، و<b>المَقروءُ مِنهُما واحِد</b>: المُعَرِّف.
    /// وقِراءَةُ ما لا يُستَعمَل تَجعَل تَغييرَ حَقلٍ عِندَ PayPal
    /// كَسراً عِندَنا.</summary>
    private sealed record CatalogIdDto(string? id);

    private sealed record ErrorDetailDto(string? issue, string? description);
    private sealed record ErrorDto(string? name, string? message, ErrorDetailDto[]? details);
#pragma warning restore IDE1006
}
