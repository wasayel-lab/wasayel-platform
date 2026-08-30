using System.Collections.Concurrent;

namespace ACommerce.Kit.Payments;

// ═══ وَضعُ التَجرِبَة — مُزَوِّدٌ مُعلَّمٌ يُختارُ صَراحَةً ═════════════
//
// **الفَرقُ عَن `MockPaymentProvider` هُوَ الفَرقُ بَينَ ادِّعاءٍ
// وإعلان.** المُحاكي يَقول «نَجَحَ الدَفع» ويَصمُت — فَلا يُمَيِّزُه
// قارِئُ الصَفقَةِ بَعدَ سَنَةٍ عَن قَبضٍ حَقيقيّ. وهذا **يَقولُ عَن
// نَفسِه إنَّه تَجرِبَةٌ في ثَلاثَةِ مَواضِع**: في اسمِه، وفي المَرجِعِ
// المُخَزَّنِ على الصَفقَة، وعَلى الشاشَةِ قَبلَ النَقرَةِ الأَخيرَة.
//
// **وهُوَ يُبنى فَوقَ الحُرّاسِ لا حَولَها**:
//   • `AssertNoStubsOutsideDevelopment` **يَبقى بِحَرفِه**، وهذا
//     الصِنفُ **لا يَحمِلُ** عَلامَةَ المُحاكي التَطويريّ — وهي
//     بِعَينِها الحُجَّةُ الَّتي كَتَبَها `UnavailablePaymentProvider`
//     عَن نَفسِه: «حَملُه العَلامَةَ كانَ سَيُفشِلُ الإقلاعَ في
//     الإنتاجِ على المَضبوط».
//   • ويُضافُ حارِسٌ **مَعكوس**
//     (`PaymentProviderSelection.AssertSimulationIsExplicit`) يَرمي إن
//     حُلَّ مُزَوِّدٌ مُحاكًى بِلا أَن يَكونَ أَحَدٌ قَد كَتَبَه في
//     التَهيئَة. أَي: **التَجرِبَةُ لا تَقَعُ إلّا لِأَنّ أَحَداً
//     طَلَبَها، ولا تَقَعُ أَبَداً لِأَنّ تَهيئَةً غابَت.**
//
// **ولا فاتورَة** — `GetInvoiceAsync` تُرجِع `null`: نَفسُ جَوابِ
// `Mock` بَعدَ ADR-014 §٢-د، ونَفسُ `Noon`، ونَفسُ `Unavailable`. لا
// رَقمَ ضَريبِيّاً ولا رابِطَ PDF ولا مُستَنَداً يُشبِهُ الحَقيقيّ.

/// <summary>
/// <para><b>عَلامَةُ مُزَوِّدِ التَجرِبَة</b> — <b>مُختَلِفَةٌ عَن</b>
/// <see cref="IDevelopmentStubPaymentProvider"/> بِقَصد.</para>
///
/// <para><b>ولِماذا عَلامَتانِ لا واحِدَة</b>: العَلامَةُ الأُولى
/// تَعني «هذا يَكذِبُ ولا يَجوزُ خارِجَ التَطوير»، وهذِه تَعني «هذا
/// يُعلِنُ أَنَّه تَجرِبَة ويَجوزُ حَيثُ طُلِبَ صَراحَةً». ودَمجُهُما
/// كانَ سَيَجعَل حارِسَ الإقلاعِ القائِمَ يَرمي على مَن اختارَ
/// التَجرِبَةَ عَمداً، أَو — وهو الأَسوَأُ — يَسكُتُ عَن مُحاكٍ
/// تَسَرَّبَ.</para>
/// </summary>
public interface ISimulatedPaymentProvider { }

/// <summary>
/// <para><b>مُزَوِّدُ دَفعٍ يُعلِنُ أَنَّه تَجرِبَة</b> — يَنجَح، ولا
/// يُحَرِّكُ مالاً، ولا يُنشِئُ فاتورَةً تَبدو حَقيقِيَّة.</para>
///
/// <para><b>والمَرجِعُ يُعلِنُ طَبيعَتَه في القاعِدَةِ لا في الشاشَةِ
/// وَحدَها</b>: <c>pay_sim_…</c> و<c>sub_sim_…</c>. فَـ<c>payment_id</c>
/// المُعَلَّقُ على الصَفقَةِ يُقرَأُ بَعدَ سَنَةٍ فَيُفهَم، ولا يُخلَط
/// بِقَبضٍ وَقَع.</para>
/// </summary>
public sealed class SimulatedPaymentProvider : IPaymentProvider, ISimulatedPaymentProvider
{
    /// <summary>القيمَةُ الَّتي تُكتَب في التَهيئَةِ لِاختِيارِه —
    /// <b>مَوضِعٌ واحِدٌ</b> يَقرَؤُه القَرارُ والحارِسُ
    /// والاختِبار.</summary>
    public const string ConfiguredValue = "simulation";

    /// <summary>سابِقَةُ مُعَرِّفِ الدَفعِ — <b>عَلامَةٌ في المُعَرِّفِ
    /// نَفسِه</b> لا في حَقلٍ مُجاوِرٍ قَد لا يُنسَخ مَعَه.</summary>
    public const string PaymentIdPrefix = "pay_sim_";

    public const string SubscriptionIdPrefix = "sub_sim_";

    /// <summary>مِفتاحُ المَرجِعِ الثالِثِ على الصَفقَة، وقيمَتُه —
    /// يُكتَبانِ بِـ<c>AttachRefAsync</c> القائِمَة، بِلا حَقلٍ جَديدٍ
    /// ولا مَعجَمٍ ثانٍ.</summary>
    public const string ModeRefKey = "payment_mode";

    private readonly ConcurrentDictionary<string, SimPayment> _payments = new();
    private readonly ConcurrentDictionary<string, SimSubscription> _subs = new();
    private readonly ConcurrentDictionary<string, string> _idempotency = new();

    /// <summary><c>"simulation"</c> لا <c>"mock"</c> — فَسَطرُ الحارِسِ
    /// وسَطرُ التَدقيقِ يَقولانِ أَيُّهُما كان.</summary>
    public string ProviderName => ConfiguredValue;

    public Task<PaymentResult> AuthorizeAsync(
        PaymentRequest req, string idempotencyKey, CancellationToken ct = default)
    {
        if (_idempotency.TryGetValue(idempotencyKey, out var existingId) &&
            _payments.TryGetValue(existingId, out var existing))
            return Task.FromResult(ToResult(existing));

        if (req.AmountSar <= 0)
            return Task.FromResult(new PaymentResult(
                "", PaymentStatus.Failed, 0, "المَبلَغ يَجِب أَن يَكون مُوجَباً."));

        var id = $"{PaymentIdPrefix}{Guid.NewGuid():N}";
        var p = new SimPayment { Id = id, AmountSar = req.AmountSar, Status = PaymentStatus.Authorized };
        _payments[id] = p;
        _idempotency[idempotencyKey] = id;
        return Task.FromResult(ToResult(p));
    }

    public Task<PaymentResult> CaptureAsync(
        string paymentId, decimal? amount = null, CancellationToken ct = default)
    {
        if (!_payments.TryGetValue(paymentId, out var p))
            return Task.FromResult(new PaymentResult(paymentId, PaymentStatus.Failed, 0, "غَير مَوجود."));
        if (p.Status != PaymentStatus.Authorized) return Task.FromResult(ToResult(p));
        if (amount is not null) p.AmountSar = amount.Value;
        p.Status = PaymentStatus.Captured;
        return Task.FromResult(ToResult(p));
    }

    public Task<PaymentResult> RefundAsync(
        string paymentId, decimal amount, string reason, CancellationToken ct = default)
    {
        if (!_payments.TryGetValue(paymentId, out var p))
            return Task.FromResult(new PaymentResult(paymentId, PaymentStatus.Failed, 0, "غَير مَوجود."));
        if (p.Status is not (PaymentStatus.Captured or PaymentStatus.PartiallyRefunded))
            return Task.FromResult(new PaymentResult(
                paymentId, PaymentStatus.Failed, p.AmountSar, "لا يُمكِن الإرجاع — الحالَة لا تَسمَح."));
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

        var id = $"{SubscriptionIdPrefix}{Guid.NewGuid():N}";
        var s = new SimSubscription { Id = id, IsActive = true, CurrentPeriodEnd = DateTime.UtcNow.AddDays(30) };
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

    /// <summary><b>لا فاتورَة.</b> نَفسُ جَوابِ <c>Mock</c> بَعدَ
    /// ‏ADR-014 §٢-د و<c>Noon</c> و<c>Unavailable</c>. ومُزَوِّدُ
    /// تَجرِبَةٍ يُصدِرُ مُستَنَداً بِرَقَمٍ ضَريبيٍّ هُوَ بِعَينِه
    /// «فاتورَةٌ تَبدو حَقيقِيَّة» — وهي الحَدُّ الَّذي لا
    /// يُتَجاوَز.</summary>
    public Task<Invoice?> GetInvoiceAsync(string paymentId, CancellationToken ct = default)
        => Task.FromResult<Invoice?>(null);

    /// <summary><b>ولا رابِطَ إيصال</b>: ‏<c>Mock</c> يُرجِع
    /// <c>/api/payments/receipt/{id}</c> ولا نُقطَةَ خَلفَه. ومَرجِعٌ
    /// يَفتَحُ لا شَيءَ أَسوَأُ مِن غِيابِه.</summary>
    private static PaymentResult ToResult(SimPayment p)
        => new(p.Id, p.Status, p.AmountSar);

    private static SubscriptionResult ToSubResult(SimSubscription s)
        => new(s.Id, s.IsActive, s.CurrentPeriodEnd);

    private sealed class SimPayment
    {
        public string Id { get; set; } = "";
        public decimal AmountSar { get; set; }
        public decimal Refunded { get; set; }
        public PaymentStatus Status { get; set; }
    }

    private sealed class SimSubscription
    {
        public string Id { get; set; } = "";
        public bool IsActive { get; set; }
        public DateTime CurrentPeriodEnd { get; set; }
    }
}

/// <summary>
/// <para><b>مُسنَدٌ واحِدٌ تَقرَؤُه الشاشَةُ والنُقطَة</b> — فَلا
/// تَعرِضُ الشاشَةُ حَقيقِيّاً ما سَيُحاكيهِ المُزَوِّد.</para>
///
/// <para>سابِقَةُ <c>StudioBilling</c>/<c>StudioTierPurchase</c> و
/// <c>Plans.razor</c>/<c>PlanPurchasePolicy</c> حَرفاً: القَرارُ
/// مَوضِعٌ واحِد، والطَرَفانِ يُنادِيانِه.</para>
/// </summary>
public static class PaymentSimulationSurface
{
    public static bool IsSimulated(IPaymentProvider? provider)
        => provider is ISimulatedPaymentProvider;
}
