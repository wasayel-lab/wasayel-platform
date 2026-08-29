using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ACommerce.Kit.Payments.Providers.Paddle;

/// <summary>
/// <para><b>نِداءُ Paddle الوَحيد: إنشاءُ مُعامَلَة.</b> ولا التِقاطَ
/// ولا تَفعيلَ ولا كاتالوج — <b>وهذا هُوَ الفَرقُ العَمَليُّ عَن
/// PayPal</b>: هُناك مُوافَقَةٌ ثُمَّ التِقاطٌ ثُمَّ تَأكيد، وهُنا
/// دَفعٌ واحِدٌ يَنتَهي بِحَدَثٍ واحِد.</para>
///
/// <para><b>ولا يُنَفِّذُ <c>IPaymentProvider</c> — ويُقالُ لِماذا</b>:
/// تِلكَ الواجِهَةُ عَقدُ <b>عَرَبونِ الصَفقاتِ داخِلَ مَتجَر</b>
/// (مُشتَرٍ يَدفَع لِبائِع)، وهذا المُزَوِّدُ لِتَدَفُّقٍ آخَر
/// تَماماً: رائِدُ أَعمالٍ يَدفَع لِ<b>وَسايِل</b> ثَمَنَ باقَتِه.
/// وتَسجيلُ هذا الصِنفِ عَلَيها كانَ سَيَجعَل <c>DealsService</c>
/// تَحجُز عَرَبونَ مُشتَرٍ عَلى حِسابِ وَسايِل عِندَ Paddle —
/// <b>خَلطُ مالَينِ لا يَلتَقِيان</b> (نَفسُ حُجَّةِ
/// <c>PayPalPaymentProvider</c> حَرفاً).</para>
///
/// <para><b>ولا يُنَفِّذُها بِخِلافِ PayPal</b>: تِلكَ نَفَّذَتها
/// لِأَنّ الاشتِراكَ المُتَكَرِّرَ فيها هُوَ ما احتاجَتهُ، وهُنا
/// <b>لا مُستَهلِكَ واحِداً</b> لِلواجِهَة — وواجِهَةٌ بِلا مُستَهلِكٍ
/// في وَقتِ التَشغيلِ تَجريدٌ يَسبِقُ حاجَتَه (القاعِدَة ١).</para>
/// </summary>
public sealed class PaddlePaymentProvider
{
    /// <summary>مَسارُ إنشاءِ المُعامَلَة — <b>مَوضِعٌ واحِد</b>
    /// يَقرَؤُه المُنتِجُ والمُعالِجُ الوَهمِيُّ في
    /// الاختِبار.</summary>
    public const string TransactionsPath = "/transactions";

    /// <summary>اسمُ عَميلِ HTTP — لِيُضبَط بِمُهلَتِه في
    /// التَسجيل.</summary>
    public const string HttpClientName = "paddle";

    private readonly PaddleOptions _opts;
    private readonly HttpClient _http;
    private readonly ILogger<PaddlePaymentProvider> _logger;
    private readonly string _baseUrl;

    public PaddlePaymentProvider(
        IOptions<PaddleOptions> opts, HttpClient http, ILogger<PaddlePaymentProvider> logger)
    {
        _opts = opts.Value;
        _http = http;
        _logger = logger;

        // فَشَلٌ عِندَ التَركيبِ لا عِندَ الطَلَب، ويُسَمّي مِفتاحَ
        // التَهيئَة — نَفسُ شَكلِ `PayPalPaymentProvider`. ورِسالَةٌ
        // تَقول «فَشِل الدَفع» عِندَ أَوَّلِ دافِعٍ أَسوَأُ مِن إقلاعٍ
        // يَشتَكي.
        if (string.IsNullOrWhiteSpace(_opts.ApiKey))
            throw new InvalidOperationException(
                $"Paddle ApiKey غَير مُعَرَّف ({PaddleEnvironment.ApiKeyKey}).");

        _baseUrl = PaddleEnvironment.BaseUrlFor(_opts.Environment)
            ?? throw new InvalidOperationException(
                $"Paddle Environment «{_opts.Environment}» خارِجَ المَعجَم " +
                $"({PaddleEnvironment.Sandbox}/{PaddleEnvironment.Live}) — " +
                $"اضبِط {PaddleEnvironment.EnvironmentKey}.");
    }

    /// <summary>المُضيفُ المُختار — يَقرَؤُه الاختِبارُ فَلا يَبقى
    /// «‏sandbox أَم live» دَعوى.</summary>
    public string BaseUrl => _baseUrl;

    /// <summary>
    /// <para><b>يُنشِئ مُعامَلَةً ويُعيد رابِطَ الدَفع.</b></para>
    ///
    /// <para><b>ولا رَأسَ مَرَّة-واحِدَةٍ — ويُقالُ صَراحَةً لا
    /// يُبتلَع</b>: لَم يُقَس رَأسٌ مُوَثَّقٌ لِلـidempotency في
    /// واجِهَةِ Paddle، <b>ورَأسٌ مُخترَعٌ يُتَجاهَل بِصَمتٍ فَيُعطي
    /// أَماناً مَظنوناً</b> (القاعِدَة ١٦). والحاجِزُ قائِمٌ عِندَنا
    /// وَحدَنا: مَرجِعٌ حَتميٌّ مُشتَقٌّ مِن المُدخَلات، ورَفضُ
    /// الكِتابَةِ فَوقَ وَثيقَةٍ تَجاوَزَت الانتِظار
    /// (<c>PaddleTransactionPolicy.IsOverwritable</c>) —
    /// <b>وذاكَ يَمنَعُ وَثيقَتَين لا مُعامَلَتَين</b>. والدَينُ
    /// مُعلَنٌ في <c>docs/DEPLOY.md</c>.</para>
    ///
    /// <para><b>ويُعيد سَبَباً مُسَمّىً ولا يَرمي</b>: تُنادى مِن
    /// نُقطَةِ نَموذَجٍ تُحَوِّل، ونَصُّ Paddle يُعرَض كَما هُوَ —
    /// «فَشِلَ الإنشاء» وَحدَها تُرسِل المُشرِفَ يُخَمِّن.</para>
    /// </summary>
    public async Task<PaddleTransactionResult> CreateTransactionAsync(
        PaddleTransactionDraft draft, string reference, CancellationToken ct = default)
    {
        using var msg = new HttpRequestMessage(HttpMethod.Post, _baseUrl + TransactionsPath);
        msg.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _opts.ApiKey);
        msg.Content = JsonContent.Create(PaddleTransactionPolicy.CreateBody(draft, reference));

        using var resp = await SendAsync(msg, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
        {
            _logger.LogError("[Paddle] فَشِل إنشاءُ المُعامَلَة {Status}: {Body}", resp.StatusCode, body);
            return new PaddleTransactionResult("", "", null,
                $"Paddle فَشِل إنشاءُ المُعامَلَة: {(int)resp.StatusCode} — {Describe(body)}");
        }

        var (id, status, url) = ReadTransaction(body);
        if (string.IsNullOrWhiteSpace(id))
            return new PaddleTransactionResult("", "", null, "Paddle رَدَّ بِلا مُعَرِّفِ مُعامَلَة.");

        return new PaddleTransactionResult(id!, status ?? "", url, FailureReason: null);
    }

    /// <summary>
    /// <para><b>قِراءَةُ الرَدّ — <c>data.id</c> و<c>data.status</c>
    /// و<c>data.checkout.url</c>.</b></para>
    ///
    /// <para><b>وقِراءَةٌ يَدَوِيَّةٌ لا نَوعٌ مُتَسَلسَل</b>: الرَدُّ
    /// كائِنٌ ضَخمٌ يَتَمَدَّد عِندَ Paddle مَعَ كُلِّ إصدار، ونَوعٌ
    /// مَكتوبٌ يَحمِلُ حَقلَينِ يَجعَل بَقِيَّتَه عَقداً ضِمنِيّاً لا
    /// نَحتاجُه.</para>
    /// </summary>
    public static (string? Id, string? Status, string? CheckoutUrl) ReadTransaction(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return (null, null, null);

        JsonDocument doc;
        try { doc = JsonDocument.Parse(body); }
        catch { return (null, null, null); }

        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return (null, null, null);
            if (!doc.RootElement.TryGetProperty("data", out var data)
                || data.ValueKind != JsonValueKind.Object) return (null, null, null);

            string? url = null;
            if (data.TryGetProperty("checkout", out var checkout)
                && checkout.ValueKind == JsonValueKind.Object)
                url = Str(checkout, "url");

            return (Str(data, "id"), Str(data, "status"), url);
        }
    }

    private static string? Str(JsonElement o, string name)
        => o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() : null;

    /// <summary>
    /// <para><b>رِسالَةُ Paddle كَما هي — مَقصوصَةً لا مَحذوفَة.</b>
    /// وسَقفُ الطولِ يَمنَع أَن يَبتَلِعَ رَدٌّ ضَخمٌ سَطرَ
    /// التَحويلِ في العُنوان.</para>
    /// </summary>
    public static string Describe(string? body)
    {
        var b = (body ?? "").Trim();
        if (b.Length == 0) return "بِلا جِسم";
        return b.Length <= 300 ? b : b[..300];
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage msg, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(PaddleEnvironment.Timeout(_opts.TimeoutSeconds));
        return await _http.SendAsync(msg, timeout.Token);
    }
}
