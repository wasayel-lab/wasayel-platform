namespace ACommerce.Kit.Payments;

/// <summary>
/// <para><b>مُزَوِّدُ الدَفعِ حينَ لا مُزَوِّد — يَقول «لا» بِسَبَبٍ
/// مَقروء، ولا يَنفَجِر ولا يَكذِب.</b></para>
///
/// <para><b>ولِماذا صِنفٌ بَدَلَ «لا تَسجيلَ إطلاقاً»</b>: ‏
/// <c>IPaymentProvider</c> وَسيطٌ في باني <c>DealsService</c> المُسَجَّلَةِ
/// بِـ<c>AddScoped</c>، وفي جِسمِ <c>POST /{slug}/checkout/submit</c>.
/// فَتَركُ الوِعاءِ فارِغاً يَعني <c>InvalidOperationException</c> عِندَ
/// أَوَّلِ طَلَبِ صَفقَة — <b>عُطلٌ عامٌّ في مَسارٍ لا عَلاقَةَ لَه
/// بِالدَفع</b>، بَدَلَ رَفضٍ واحِدٍ مَوضِعيّ. نَفسُ حُجَّةِ
/// <c>PayPalGateway</c>: «غِلافٌ مُسَجَّلٌ دائِماً ويُجيبُ لا».</para>
///
/// <para><b>وكُلُّ جَوابٍ هُنا فَشَلٌ صَريحٌ ذو سَبَب، لا صَمتٌ ولا
/// نَجاح</b>: ‏<c>Failed</c> مَعَ <see cref="Reason"/>، و<c>IsActive =
/// false</c>، و<c>null</c> لِلفاتورَة. فَمَن يَقرَأُ الحالَةَ يَعرِف
/// أَنَّها لَم تَقَع؛ ومَن يَبتَلِعُها يَبتَلِع فَشَلاً مَكتوباً لا
/// نَجاحاً مُلَفَّقاً.</para>
///
/// <para><b>ولا يَحمِلُ <see cref="IDevelopmentStubPaymentProvider"/></b>
/// — فَهُوَ لَيسَ مُحاكِياً: المُحاكي يَدَّعي النَجاح، وهذا يَمتَنِع.
/// وحَملُه العَلامَةَ كانَ سَيُفشِلُ الإقلاعَ في الإنتاجِ على المَضبوط.</para>
/// </summary>
public sealed class UnavailablePaymentProvider : IPaymentProvider
{
    /// <summary>السَبَبُ المُرجَعُ في كُلِّ نَتيجَة — <b>مَوضِعٌ واحِد</b>
    /// يَقرَؤُه المُنتِجُ والمُختَبِر، فَلا يَنجَرِف نَصّان.</summary>
    public const string Reason = "لا مُزَوِّدَ دَفعٍ مَضبوطٌ في هذِه النُسخَة.";

    public string ProviderName => "unavailable";

    public Task<PaymentResult> AuthorizeAsync(
        PaymentRequest req, string idempotencyKey, CancellationToken ct = default)
        => Task.FromResult(new PaymentResult("", PaymentStatus.Failed, req.AmountSar, Reason));

    public Task<PaymentResult> CaptureAsync(
        string paymentId, decimal? amount = null, CancellationToken ct = default)
        => Task.FromResult(new PaymentResult(paymentId, PaymentStatus.Failed, amount ?? 0m, Reason));

    public Task<PaymentResult> RefundAsync(
        string paymentId, decimal amount, string reason, CancellationToken ct = default)
        => Task.FromResult(new PaymentResult(paymentId, PaymentStatus.Failed, amount, Reason));

    public Task<SubscriptionResult> CreateSubscriptionAsync(
        SubscriptionRequest req, string idempotencyKey, CancellationToken ct = default)
        => Task.FromResult(new SubscriptionResult(
            SubscriptionId: "", IsActive: false, CurrentPeriodEnd: default, FailureReason: Reason));

    public Task<bool> CancelSubscriptionAsync(string subscriptionId, CancellationToken ct = default)
        => Task.FromResult(false);

    /// <summary>لا فاتورَةَ مِن مُزَوِّدٍ لَم يَقبِض — نَفسُ جَوابِ
    /// <c>NoonPaymentProvider</c> حَرفاً.</summary>
    public Task<Invoice?> GetInvoiceAsync(string paymentId, CancellationToken ct = default)
        => Task.FromResult<Invoice?>(null);
}

/// <summary>
/// <para><b>أَيَستَطيعُ مُزَوِّدُ هذِه النُسخَةِ القَبضَ فِعلاً؟</b> — مُسنَدٌ
/// واحِدٌ تَقرَؤُه شاشَةُ إتمامِ الشِراءِ ونُقطَتُها مَعاً، عَلى سابِقَةِ
/// <c>PaymentSimulationSurface.IsSimulated</c> حَرفاً: القَرارُ مَوضِعٌ واحِد،
/// والطَرَفانِ يُنادِيانِه.</para>
///
/// <para><b>العِلَّةُ المَقيسَة (‏ADR-032 §٢‑د، ‏2026-10-11)</b>: كانَ خِيارُ
/// البِطاقَةِ يُشتَقُّ مِن <c>Tenant.PaymentProviderConfigured</c> وَحدَه —
/// أَي مِن رابِطٍ مُستَضافٍ لَصَقَه المُستَأجِر — بَينَما البِطاقَةُ تُنادي
/// <c>IPaymentProvider</c> <b>العامَّ</b>، وهُوَ في الإنتاجِ هذا الصِنفُ
/// بِعَينِه فَيَرُدُّ <c>Failed</c> لِكُلِّ حَجز. فَكانَ المُشتَري يُعرَضُ
/// عَلَيه بابٌ يُفضي إلى فَشَلٍ مُؤَكَّد (القاعِدَة ١٢).</para>
///
/// <para><b>ولِماذا نَفيُ هذا الصِنفِ لا واجِهَةُ عَلامَةٍ جَديدَة</b>:
/// المُزَوِّدُ الَّذي «لا يَستَطيع» واحِدٌ مُسَمّى — الفَشَلُ المُغلَق —
/// وما سِواهُ (المُحاكي في التَطوير، والتَجرِبَةُ المُعلَنَة، وأَيُّ بَوّابَةٍ
/// تُدمَجُ لاحِقاً) يُجيبُ حَجزَه. وتَجريدٌ ثانٍ لِحالَةٍ واحِدَةٍ هُوَ
/// الدَينُ الَّذي كَتَبَت القاعِدَةُ الأولى بِسِتِّ حالاتٍ مَقيسَة.</para>
/// </summary>
public static class PaymentChargeSurface
{
    public static bool CanCharge(IPaymentProvider? provider)
        => provider is not null && provider is not UnavailablePaymentProvider;
}
