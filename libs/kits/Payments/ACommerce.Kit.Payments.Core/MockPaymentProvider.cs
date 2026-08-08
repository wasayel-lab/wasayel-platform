using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;

namespace ACommerce.Kit.Payments;

/// <summary>
/// مُزَوِّد دَفع وَهميّ — يُحاكي authorize/capture/refund + إنشاء
/// اشتِراك. يَنجَح دائِماً (إلّا لَو الـ amount &lt;= 0). يُولِّد
/// invoice رَقم تَسلسُليّ. يُخزَّن في الذاكِرَة.
///
/// لاستِبدالُه بـ Moyasar/Tap لاحِقاً: نَفس الواجِهَة، تَنفيذ HTTP.
/// </summary>
public sealed class MockPaymentProvider : IPaymentProvider
{
    private readonly ConcurrentDictionary<string, MockPayment> _payments = new();
    private readonly ConcurrentDictionary<string, MockSubscription> _subs = new();
    private readonly ConcurrentDictionary<string, string> _idempotency = new();
    private int _invoiceCounter = 1;

    public string ProviderName => "mock";

    public Task<PaymentResult> AuthorizeAsync(
        PaymentRequest req, string idempotencyKey, CancellationToken ct = default)
    {
        // idempotency: نَفس المِفتاح = نَفس النَّتيجَة.
        if (_idempotency.TryGetValue(idempotencyKey, out var existingId) &&
            _payments.TryGetValue(existingId, out var existing))
            return Task.FromResult(ToResult(existing));

        if (req.AmountSar <= 0)
            return Task.FromResult(new PaymentResult("", PaymentStatus.Failed, 0, "المَبلَغ يَجِب أَن يَكون مُوجَباً."));

        var id = $"pay_mock_{Guid.NewGuid():N}";
        var p = new MockPayment
        {
            Id = id, AmountSar = req.AmountSar, Status = PaymentStatus.Authorized,
            Description = req.Description, CustomerId = req.CustomerId,
            InvoiceNumber = NextInvoice(), CreatedAt = DateTime.UtcNow
        };
        _payments[id] = p;
        _idempotency[idempotencyKey] = id;
        return Task.FromResult(ToResult(p));
    }

    public Task<PaymentResult> CaptureAsync(string paymentId, decimal? amount = null, CancellationToken ct = default)
    {
        if (!_payments.TryGetValue(paymentId, out var p))
            return Task.FromResult(new PaymentResult(paymentId, PaymentStatus.Failed, 0, "غَير مَوجود."));
        if (p.Status != PaymentStatus.Authorized)
            return Task.FromResult(ToResult(p));
        if (amount is not null) p.AmountSar = amount.Value;
        p.Status = PaymentStatus.Captured;
        p.CapturedAt = DateTime.UtcNow;
        return Task.FromResult(ToResult(p));
    }

    public Task<PaymentResult> RefundAsync(string paymentId, decimal amount, string reason, CancellationToken ct = default)
    {
        if (!_payments.TryGetValue(paymentId, out var p))
            return Task.FromResult(new PaymentResult(paymentId, PaymentStatus.Failed, 0, "غَير مَوجود."));
        if (p.Status is not (PaymentStatus.Captured or PaymentStatus.PartiallyRefunded))
            return Task.FromResult(new PaymentResult(paymentId, PaymentStatus.Failed, p.AmountSar, "لا يُمكِن الإرجاع — الحالَة لا تَسمَح."));
        p.Refunded += amount;
        p.Status = p.Refunded >= p.AmountSar ? PaymentStatus.Refunded : PaymentStatus.PartiallyRefunded;
        return Task.FromResult(ToResult(p));
    }

    public Task<SubscriptionResult> CreateSubscriptionAsync(
        SubscriptionRequest req, string idempotencyKey, CancellationToken ct = default)
    {
        if (_idempotency.TryGetValue(idempotencyKey, out var existingId) &&
            _subs.TryGetValue(existingId, out var existing))
            return Task.FromResult(ToSubResult(existing));

        var id = $"sub_mock_{Guid.NewGuid():N}";
        var s = new MockSubscription
        {
            Id = id, PlanId = req.PlanId, MonthlyAmountSar = req.MonthlyAmountSar,
            CustomerId = req.CustomerId, IsActive = true,
            CurrentPeriodEnd = DateTime.UtcNow.AddDays(30)
        };
        _subs[id] = s;
        _idempotency[idempotencyKey] = id;
        return Task.FromResult(ToSubResult(s));
    }

    public Task<bool> CancelSubscriptionAsync(string subscriptionId, CancellationToken ct = default)
    {
        if (!_subs.TryGetValue(subscriptionId, out var s)) return Task.FromResult(false);
        s.IsActive = false;
        return Task.FromResult(true);
    }

    public Task<Invoice?> GetInvoiceAsync(string paymentId, CancellationToken ct = default)
    {
        if (!_payments.TryGetValue(paymentId, out var p)) return Task.FromResult<Invoice?>(null);
        var sub = Math.Round(p.AmountSar / 1.15m, 2);     // VAT 15% inclusive
        var vat = Math.Round(p.AmountSar - sub, 2);
        return Task.FromResult<Invoice?>(new(
            p.Id, p.InvoiceNumber, sub, vat, p.AmountSar,
            SellerName: "ACommerce",
            SellerVatNumber: "300000000000003",   // VAT وَهميّ
            IssuedAt: p.CreatedAt,
            PdfUrl: $"/api/payments/invoice/{p.Id}.pdf"));   // endpoint مُستَقبَليّ
    }

    private string NextInvoice()
        => $"INV-{DateTime.UtcNow:yyyy}-{Interlocked.Increment(ref _invoiceCounter):D6}";

    private static PaymentResult ToResult(MockPayment p) =>
        new(p.Id, p.Status, p.AmountSar, ReceiptUrl: $"/api/payments/receipt/{p.Id}");

    private static SubscriptionResult ToSubResult(MockSubscription s) =>
        new(s.Id, s.IsActive, s.CurrentPeriodEnd);

    private sealed class MockPayment
    {
        public string Id { get; set; } = "";
        public decimal AmountSar { get; set; }
        public decimal Refunded { get; set; }
        public PaymentStatus Status { get; set; }
        public string Description { get; set; } = "";
        public string CustomerId { get; set; } = "";
        public string InvoiceNumber { get; set; } = "";
        public DateTime CreatedAt { get; set; }
        public DateTime? CapturedAt { get; set; }
    }

    private sealed class MockSubscription
    {
        public string Id { get; set; } = "";
        public string PlanId { get; set; } = "";
        public decimal MonthlyAmountSar { get; set; }
        public string CustomerId { get; set; } = "";
        public bool IsActive { get; set; }
        public DateTime CurrentPeriodEnd { get; set; }
    }
}

public static class PaymentServiceExtensions
{
    public static IServiceCollection AddMockPayments(this IServiceCollection services)
    {
        services.AddSingleton<IPaymentProvider, MockPaymentProvider>();
        return services;
    }
}
