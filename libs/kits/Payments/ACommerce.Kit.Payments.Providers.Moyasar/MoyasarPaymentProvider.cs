using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ACommerce.Kit.Payments.Providers.Moyasar;

/// <summary>إعدادات Moyasar (KSA — Mada/Visa/Mastercard/STC Pay/Apple Pay).</summary>
public sealed class MoyasarOptions
{
    public string SecretApiKey { get; set; } = "";    // sk_test_… أَو sk_live_…
    public string PublishableKey { get; set; } = "";  // pk_test_… (لِلـ frontend)
    public string BaseUrl { get; set; } = "https://api.moyasar.com";
}

/// <summary>
/// مُزَوِّد Moyasar — أَكثَر مُزَوِّد دَفع شُيوعاً في KSA لِلتُجّار الصِغار/المُتَوَسِّطين.
/// HTTP API بِـ Basic auth (SecretKey كَ user، الكَلِمَة فارِغَة).
/// </summary>
public sealed class MoyasarPaymentProvider : IPaymentProvider
{
    private readonly MoyasarOptions _opts;
    private readonly HttpClient _http;
    private readonly ILogger<MoyasarPaymentProvider> _logger;

    public MoyasarPaymentProvider(IOptions<MoyasarOptions> opts, HttpClient http, ILogger<MoyasarPaymentProvider> logger)
    {
        _opts = opts.Value;
        _http = http;
        _logger = logger;
        if (string.IsNullOrEmpty(_opts.SecretApiKey))
            throw new InvalidOperationException("Moyasar SecretApiKey مَطلوب.");
        var creds = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_opts.SecretApiKey}:"));
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", creds);
    }

    public string ProviderName => "Moyasar";

    public async Task<PaymentResult> AuthorizeAsync(
        PaymentRequest req, string idempotencyKey, CancellationToken ct = default)
    {
        // Moyasar amount بِالـ halalas (1 SAR = 100).
        var body = new
        {
            amount = (long)(req.AmountSar * 100),
            currency = "SAR",
            description = req.Description,
            metadata = req.Metadata ?? new()
        };
        using var msg = new HttpRequestMessage(HttpMethod.Post, $"{_opts.BaseUrl}/v1/payments")
        { Content = JsonContent.Create(body) };
        msg.Headers.Add("X-Idempotency-Key", idempotencyKey);
        using var resp = await _http.SendAsync(msg, ct);
        var dto = await resp.Content.ReadFromJsonAsync<PaymentDto>(cancellationToken: ct);
        if (!resp.IsSuccessStatusCode || dto is null)
        {
            _logger.LogError("[Moyasar] فَشِل {Status}", resp.StatusCode);
            return new PaymentResult("", PaymentStatus.Failed, req.AmountSar,
                $"Moyasar فَشِل: {(int)resp.StatusCode}");
        }
        return new PaymentResult(dto.Id, MapStatus(dto.Status), req.AmountSar, ReceiptUrl: dto.Source?.TransactionUrl);
    }

    public async Task<PaymentResult> CaptureAsync(string paymentId, decimal? amount = null, CancellationToken ct = default)
    {
        using var resp = await _http.PostAsync($"{_opts.BaseUrl}/v1/payments/{paymentId}/capture", null, ct);
        var dto = await resp.Content.ReadFromJsonAsync<PaymentDto>(cancellationToken: ct);
        if (!resp.IsSuccessStatusCode || dto is null)
            return new PaymentResult(paymentId, PaymentStatus.Failed, amount ?? 0, $"Capture فَشِل");
        return new PaymentResult(dto.Id, MapStatus(dto.Status), dto.Amount / 100m);
    }

    public async Task<PaymentResult> RefundAsync(string paymentId, decimal amount, string reason, CancellationToken ct = default)
    {
        var body = new { amount = (long)(amount * 100), reason };
        using var resp = await _http.PostAsJsonAsync($"{_opts.BaseUrl}/v1/payments/{paymentId}/refund", body, ct);
        var dto = await resp.Content.ReadFromJsonAsync<PaymentDto>(cancellationToken: ct);
        if (!resp.IsSuccessStatusCode || dto is null)
            return new PaymentResult(paymentId, PaymentStatus.Failed, amount, "Refund فَشِل");
        return new PaymentResult(dto.Id, PaymentStatus.Refunded, amount);
    }

    public Task<SubscriptionResult> CreateSubscriptionAsync(
        SubscriptionRequest req, string idempotencyKey, CancellationToken ct = default)
    {
        // Moyasar لا يَدعَم subscriptions بِشَكل native — نُحاكيها بِـ token مَخزون + جَدوَلَة شَهرِيَّة عَن طَريقنا.
        throw new NotSupportedException("Moyasar: لا يَدعَم subscriptions مُباشَرَةً. اِستَخدِم Saved Card + جَدوَلَة داخِلِيَّة.");
    }

    public Task<bool> CancelSubscriptionAsync(string subscriptionId, CancellationToken ct = default)
        => Task.FromResult(true);

    public async Task<Invoice?> GetInvoiceAsync(string paymentId, CancellationToken ct = default)
    {
        using var resp = await _http.GetAsync($"{_opts.BaseUrl}/v1/payments/{paymentId}", ct);
        if (!resp.IsSuccessStatusCode) return null;
        var dto = await resp.Content.ReadFromJsonAsync<PaymentDto>(cancellationToken: ct);
        if (dto is null) return null;
        var subtotal = dto.Amount / 100m / 1.15m;
        var vat = dto.Amount / 100m - subtotal;
        return new Invoice(
            dto.Id, $"INV-{dto.Id[^8..]}", Math.Round(subtotal, 2), Math.Round(vat, 2), dto.Amount / 100m,
            "ACommerce", "", dto.CreatedAt, dto.Source?.TransactionUrl ?? "");
    }

    private static PaymentStatus MapStatus(string s) => s.ToLowerInvariant() switch
    {
        "paid" => PaymentStatus.Captured,
        "authorized" => PaymentStatus.Authorized,
        "refunded" => PaymentStatus.Refunded,
        "failed" => PaymentStatus.Failed,
        "voided" => PaymentStatus.Cancelled,
        _ => PaymentStatus.Failed
    };

    private sealed record PaymentDto(string Id, string Status, long Amount, DateTime CreatedAt, SourceDto? Source);
    private sealed record SourceDto(string? TransactionUrl);
}

public static class MoyasarExtensions
{
    public static IServiceCollection AddMoyasarPayments(
        this IServiceCollection services, Action<MoyasarOptions> configure)
    {
        services.Configure(configure);
        services.AddHttpClient<IPaymentProvider, MoyasarPaymentProvider>();
        return services;
    }
}
