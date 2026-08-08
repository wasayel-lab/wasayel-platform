using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ACommerce.Kit.Payments.Providers.Noon;

/// <summary>إعدادات Noon Payments (KSA/UAE — مَنصَّة المُؤَسَّسات الكَبيرَة).</summary>
public sealed class NoonOptions
{
    public string BusinessIdentifier { get; set; } = "";
    public string ApplicationIdentifier { get; set; } = "";
    public string Key { get; set; } = "";       // Authorization: Key_Test xxx
    public string Mode { get; set; } = "Test";  // Test أَو Live
    public string BaseUrl { get; set; } = "https://api-test.noonpayments.com";
}

/// <summary>
/// مُزَوِّد Noon — يَدعَم Mada/Visa/Mastercard/Apple Pay/STC Pay + 3DS. مُلائِم
/// لِلتُجّار الكَبار في KSA/UAE. authorize + capture + refund native.
/// </summary>
public sealed class NoonPaymentProvider : IPaymentProvider
{
    private readonly NoonOptions _opts;
    private readonly HttpClient _http;
    private readonly ILogger<NoonPaymentProvider> _logger;

    public NoonPaymentProvider(IOptions<NoonOptions> opts, HttpClient http, ILogger<NoonPaymentProvider> logger)
    {
        _opts = opts.Value;
        _http = http;
        _logger = logger;
        if (string.IsNullOrEmpty(_opts.BusinessIdentifier) || string.IsNullOrEmpty(_opts.Key))
            throw new InvalidOperationException("Noon BusinessIdentifier/Key مَطلوبَة.");
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue($"Key_{_opts.Mode}", _opts.Key);
    }

    public string ProviderName => "Noon";

    public async Task<PaymentResult> AuthorizeAsync(
        PaymentRequest req, string idempotencyKey, CancellationToken ct = default)
    {
        var body = new
        {
            apiOperation = "INITIATE",
            order = new
            {
                amount = req.AmountSar,
                currency = "SAR",
                description = req.Description,
                channel = "Web",
                reference = idempotencyKey
            },
            configuration = new { paymentAction = "AUTHORIZE", returnUrl = (string?)null },
            customer = new { firstName = req.CustomerId, email = req.CustomerEmail }
        };
        using var resp = await _http.PostAsJsonAsync($"{_opts.BaseUrl}/payment/v1/order", body, ct);
        var dto = await resp.Content.ReadFromJsonAsync<NoonOrderDto>(cancellationToken: ct);
        if (!resp.IsSuccessStatusCode || dto?.Result is null)
        {
            _logger.LogError("[Noon] فَشِل {Status}", resp.StatusCode);
            return new PaymentResult("", PaymentStatus.Failed, req.AmountSar, $"Noon فَشِل");
        }
        return new PaymentResult(
            dto.Result.Order.Id.ToString(),
            MapStatus(dto.Result.Order.Status),
            req.AmountSar,
            ReceiptUrl: dto.Result.Checkout?.PostUrl);
    }

    public async Task<PaymentResult> CaptureAsync(string paymentId, decimal? amount = null, CancellationToken ct = default)
    {
        var body = new { apiOperation = "CAPTURE", order = new { id = long.Parse(paymentId) } };
        using var resp = await _http.PostAsJsonAsync($"{_opts.BaseUrl}/payment/v1/order", body, ct);
        if (!resp.IsSuccessStatusCode)
            return new PaymentResult(paymentId, PaymentStatus.Failed, amount ?? 0, "Capture فَشِل");
        return new PaymentResult(paymentId, PaymentStatus.Captured, amount ?? 0);
    }

    public async Task<PaymentResult> RefundAsync(string paymentId, decimal amount, string reason, CancellationToken ct = default)
    {
        var body = new
        {
            apiOperation = "REVERSE",
            order = new { id = long.Parse(paymentId) },
            transaction = new { amount, currency = "SAR" }
        };
        using var resp = await _http.PostAsJsonAsync($"{_opts.BaseUrl}/payment/v1/order", body, ct);
        if (!resp.IsSuccessStatusCode)
            return new PaymentResult(paymentId, PaymentStatus.Failed, amount, "Refund فَشِل");
        return new PaymentResult(paymentId, PaymentStatus.Refunded, amount);
    }

    public Task<SubscriptionResult> CreateSubscriptionAsync(
        SubscriptionRequest req, string idempotencyKey, CancellationToken ct = default)
        => throw new NotSupportedException("Noon: لِلاشتِراك اِستَخدِم Tokenization + جَدوَلَة داخِلِيَّة.");

    public Task<bool> CancelSubscriptionAsync(string subscriptionId, CancellationToken ct = default)
        => Task.FromResult(true);

    public Task<Invoice?> GetInvoiceAsync(string paymentId, CancellationToken ct = default)
        => Task.FromResult<Invoice?>(null);

    private static PaymentStatus MapStatus(string? s) => (s ?? "").ToUpperInvariant() switch
    {
        "AUTHORIZED"   => PaymentStatus.Authorized,
        "CAPTURED"     => PaymentStatus.Captured,
        "FAILED"       => PaymentStatus.Failed,
        "REFUNDED"     => PaymentStatus.Refunded,
        "CANCELLED"    => PaymentStatus.Cancelled,
        _ => PaymentStatus.Authorized
    };

    private sealed record NoonOrderDto(NoonResultDto? Result);
    private sealed record NoonResultDto(NoonOrderInfo Order, NoonCheckout? Checkout);
    private sealed record NoonOrderInfo(long Id, string Status);
    private sealed record NoonCheckout(string? PostUrl);
}

public static class NoonExtensions
{
    public static IServiceCollection AddNoonPayments(
        this IServiceCollection services, Action<NoonOptions> configure)
    {
        services.Configure(configure);
        services.AddHttpClient<IPaymentProvider, NoonPaymentProvider>();
        return services;
    }
}
