namespace ACommerce.Kit.Payments;

/// <summary>
/// مُجَرَّد مُزَوِّد الدَّفع — authorize → capture → refund.
/// يَدعَم idempotency-key لِتَجَنُّب الازدِواج. mockable بِالكامِل.
/// تُحقَن واجِهَتُه عَبر DI. يَنطَبِق على: اشتِراك Studio (مُتَكَرِّر)،
/// مَعامَلات داخِل التَّطبيقات (مَرَّة واحِدَة)، عَرَبون/تأمين الحَجز.
/// </summary>
public interface IPaymentProvider
{
    string ProviderName { get; }

    /// <summary>اِحجِز مَبلَغاً (authorize) — يَفتَح مُعامَلَة لكِنّ لا يَخصِم.
    /// يُستَخدَم في الحَجز عِندَما نَنتَظِر تَأكيداً مِن طَرَفَين.</summary>
    Task<PaymentResult> AuthorizeAsync(
        PaymentRequest req, string idempotencyKey, CancellationToken ct = default);

    /// <summary>اِخصِم مَبلَغاً سابِقاً مَحجوز (capture).</summary>
    Task<PaymentResult> CaptureAsync(string paymentId, decimal? amount = null, CancellationToken ct = default);

    /// <summary>اِرجاع كامِل أَو جُزئيّ.</summary>
    Task<PaymentResult> RefundAsync(string paymentId, decimal amount, string reason, CancellationToken ct = default);

    /// <summary>اِبدَأ اشتِراك مُتَكَرِّر (subscription).</summary>
    Task<SubscriptionResult> CreateSubscriptionAsync(
        SubscriptionRequest req, string idempotencyKey, CancellationToken ct = default);

    /// <summary>اِلغِ اشتِراك مُتَكَرِّر.</summary>
    Task<bool> CancelSubscriptionAsync(string subscriptionId, CancellationToken ct = default);

    /// <summary>اِجلِب الفاتورَة (Invoice) لِعَرضها/تَنزيلها (ZATCA).</summary>
    Task<Invoice?> GetInvoiceAsync(string paymentId, CancellationToken ct = default);
}

public sealed record PaymentRequest(
    decimal AmountSar,
    string Description,
    string CustomerId,
    string CustomerPhone,
    string? CustomerEmail = null,
    Dictionary<string, string>? Metadata = null);

public sealed record SubscriptionRequest(
    string CustomerId,
    string PlanId,
    decimal MonthlyAmountSar,
    string CustomerPhone);

public enum PaymentStatus
{
    Authorized, Captured, Failed, Refunded, PartiallyRefunded, Cancelled
}

public sealed record PaymentResult(
    string PaymentId,
    PaymentStatus Status,
    decimal AmountSar,
    string? FailureReason = null,
    string? ReceiptUrl = null);

/// <param name="ApproveUrl">
/// <para><b>رابِطُ المُوافَقَةِ حينَ يَحتاج المُزَوِّدُ نَقرَةَ
/// الدافِعِ عِندَه</b> (‏PayPal تُعيد <c>links[rel=approve]</c>).
/// و<c>null</c> لِمُزَوِّدٍ يَخصِم بِبِطاقَةٍ مَحفوظَةٍ بِلا مُوافَقَةٍ
/// خارِجِيَّة.</para>
///
/// <para><b>ولِماذا في العَقدِ المُشتَرَك لا في نَتيجَةِ PayPal
/// وَحدَها</b>: «اشتِراكٌ بُدِئ» و«اشتِراكٌ فُعِّل» حالَتانِ
/// مُختَلِفَتان في كُلّ مُزَوِّدٍ يَعمَل بِإعادَةِ التَوجيه، والفَرقُ
/// بَينَهُما <b>هُوَ هذا الرابِط</b>. فَإخفاؤُه خَلفَ نَوعٍ خاصٍّ
/// بِمُزَوِّدٍ يَعني أَنّ كُلَّ مُستَهلِكٍ يُعيد اكتِشافَه.</para>
/// </param>
public sealed record SubscriptionResult(
    string SubscriptionId,
    bool IsActive,
    DateTime CurrentPeriodEnd,
    string? FailureReason = null,
    string? ApproveUrl = null);

public sealed record Invoice(
    string PaymentId,
    string Number,                  // INV-2026-00001
    decimal SubtotalSar,
    decimal VatSar,
    decimal TotalSar,
    string SellerName,
    string SellerVatNumber,
    DateTime IssuedAt,
    string PdfUrl);
