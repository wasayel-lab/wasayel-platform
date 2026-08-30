using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;

namespace ACommerce.Kit.Payments;

/// <summary>
/// مُزَوِّد دَفع وَهميّ — يُحاكي authorize/capture/refund + إنشاء
/// اشتِراك. يَنجَح دائِماً (إلّا لَو الـ amount &lt;= 0). يُخزَّن في
/// الذاكِرَة.
///
/// <para><b>وهُوَ مُحاكٍ تَطويريٌّ بِالعَلامَة لا بِالتَعليق</b>
/// (<see cref="IDevelopmentStubPaymentProvider"/>): «يَنجَح دائِماً» في
/// الإنتاج تَعني <b>باقَةً تُمنَح بِلا قَبض</b> و<b>صَفقَةً تُعَلَّم
/// بِدَفعٍ لَم يَقَع</b>. فَحارِسُ الإقلاع يَرمي إن سُجِّلَ خارِجَ
/// <c>Development</c> — قَبلَ أَوَّلِ طَلَبٍ لا بَعدَ أَوَّلِ ضَحِيَّة.</para>
///
/// لاستِبدالُه بـ Moyasar/Tap لاحِقاً: نَفس الواجِهَة، تَنفيذ HTTP.
/// </summary>
public sealed class MockPaymentProvider : IPaymentProvider, IDevelopmentStubPaymentProvider
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

    /// <summary>
    /// <para><b>لا فاتورَة — والمُحاكي أَقَلُّ مَن يَحِقُّ لَه إصدارُها.</b>
    /// نَفسُ جَوابِ <c>NoonPaymentProvider</c> حَرفاً.</para>
    ///
    /// <para><b>وما حُذِفَ هُنا يُقالُ ولا يُبتلَع</b> (‏2026-08-30):
    /// كانَت تُرجِع رَقماً ضَريبيّاً <b>مُخترَعاً</b> (بِحُروفِه في
    /// <c>docs/ADR-014</c>، ولا يُكتَب في مِلَفِّ كودٍ بَعدَ اليَوم)
    /// واحتِسابَ ضَريبَةٍ ‏15٪ و<c>PdfUrl</c>
    /// إلى نُقطَةٍ مُعَلَّقٍ عَلَيها في السَطرِ نَفسِه بِأَنَّها
    /// «مُستَقبَلِيَّة» — أَي <b>رابِطَ فاتورَةٍ لا يَفتَح شَيئاً</b>.
    /// ورَقَمٌ ضَريبيٌّ مُلَفَّقٌ في وَثيقَةٍ تُعرَض على تاجِر
    /// <b>خَطَرٌ قانونيٌّ لا عَيبٌ تَجميليّ</b> (القاعِدَة ١٦: لا
    /// تُخترَع بَياناتُ مُنتَج). والبَديلُ لَيسَ رَقَماً آخَرَ بَل
    /// <c>null</c>: إمّا فاتورَةٌ حَقيقِيَّةٌ مِن مُزَوِّدٍ قَبَض، أَو
    /// لا فاتورَة. وجارَتُنا <c>PayPalPaymentProvider</c> سَبَقَت إلى
    /// نَفسِ الحُكمِ بِتَرْكِ الحَقلِ فارِغاً بَدَلَ اشتِقاقِ ‏15٪.</para>
    /// </summary>
    public Task<Invoice?> GetInvoiceAsync(string paymentId, CancellationToken ct = default)
        => Task.FromResult<Invoice?>(null);

    /// <summary>رَقمُ الفاتورَةِ الداخِليّ — يُخَزَّن في وَثيقَةِ الدَفعِ
    /// المُحاكاةِ لِلتَشخيصِ، ولا يُصَدَّر في مُستَنَدٍ يُعرَض على
    /// أَحَد.</summary>
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
    /// <summary>
    /// <para><b>يُسَجِّل مُزَوِّدَ الدَفعِ المُوافِقَ لِلبيئَة — ونُقطَةُ
    /// النِداءِ الوَحيدَةُ في <c>Program.cs</c>.</b></para>
    ///
    /// <para>المَنطِقُ كُلُّه في <see cref="PaymentProviderSelection.Decide"/>
    /// (دالَّةٌ نَقِيَّةٌ تُقاس بِجَدوَل)، وهذِه أَثَرُها لا قَرارُها —
    /// نَفسُ شَكلِ <c>AuthChannelSelection</c> وسَطرِ تَسجيلِه.</para>
    /// </summary>
    public static IServiceCollection AddPaymentProvider(
        this IServiceCollection services, bool isDevelopment)
        => services.AddPaymentProvider(isDevelopment, configured: null);

    /// <summary>
    /// <para><b>نَفسُ التَسجيلِ، والقيمَةُ المَكتوبَةُ في التَهيئَةِ
    /// مُعامَلٌ</b> — والحِملُ القَديمُ يُفَوِّضُ إلَيه بِـ<c>null</c>،
    /// فَما كانَ يَقَعُ يَقَعُ بِحَرفِه.</para>
    /// </summary>
    public static IServiceCollection AddPaymentProvider(
        this IServiceCollection services, bool isDevelopment, string? configured)
        => PaymentProviderSelection.Decide(isDevelopment, configured) switch
        {
            PaymentProviderChoice.Simulation => services.AddSimulatedPayments(),
            PaymentProviderChoice.Mock       => services.AddMockPayments(),
            _                                => services.AddUnavailablePayments()
        };

    /// <summary>
    /// <para><b>تَسجيلُ وَضعِ التَجرِبَة</b> — يَبقى عامّاً لِنَفسِ
    /// سَبَبِ <c>AddMockPayments</c>: حارِسٌ بِلا طَريقٍ يُمسِكُه لا
    /// يُقاس. والحارِسُ هُنا
    /// <see cref="PaymentProviderSelection.AssertSimulationIsExplicit"/>.</para>
    /// </summary>
    public static IServiceCollection AddSimulatedPayments(this IServiceCollection services)
    {
        services.AddSingleton<IPaymentProvider, SimulatedPaymentProvider>();
        return services;
    }

    /// <summary>
    /// <para>تَسجيلٌ مُباشِرٌ لِلمُحاكي. <b>يَبقى عامّاً عَمداً</b>: هُوَ
    /// بِعَينِه السَطرُ الَّذي يَعود يَوماً سَهواً إلى <c>Program.cs</c>،
    /// وحارِسُ الإقلاع
    /// (<see cref="PaymentProviderSelection.AssertNoStubsOutsideDevelopment"/>)
    /// مَوجودٌ لِيُمسِكَه — وحارِسٌ بِلا طَريقٍ يُمسِكُه لا يُقاس. نَفسُ
    /// حُجَّةِ بَقاءِ <c>AddMockSmsChannel()</c> عامّاً.</b></para>
    /// </summary>
    public static IServiceCollection AddMockPayments(this IServiceCollection services)
    {
        services.AddSingleton<IPaymentProvider, MockPaymentProvider>();
        return services;
    }

    /// <summary>الفَشَلُ المُغلَق — مُسَجَّلٌ دائِماً خارِجَ التَطوير
    /// فَلا يَنفَجِر حَلُّ <c>DealsService</c>، ويَرُدُّ كُلَّ نِداءٍ
    /// بِسَبَبٍ مَقروء.</summary>
    public static IServiceCollection AddUnavailablePayments(this IServiceCollection services)
    {
        services.AddSingleton<IPaymentProvider, UnavailablePaymentProvider>();
        return services;
    }
}
