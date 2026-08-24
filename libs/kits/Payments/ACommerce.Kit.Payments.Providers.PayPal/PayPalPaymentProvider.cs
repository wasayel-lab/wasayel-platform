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

    /// <summary>اسمُ العَميلِ المُطَبَّع — التَسجيلُ والحَلُّ يَقرَآنِه
    /// مِن هُنا.</summary>
    public const string HttpClientName = "paypal";

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
        msg.Headers.Add("PayPal-Request-Id", idempotencyKey);
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
            return new SubscriptionResult("", false, default,
                $"PayPal فَشِل إنشاءُ الاشتِراك: {(int)resp.StatusCode}");
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
#pragma warning restore IDE1006
}
