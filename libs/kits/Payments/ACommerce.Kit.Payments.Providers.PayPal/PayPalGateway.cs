using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ACommerce.Kit.Payments.Providers.PayPal;

/// <summary>
/// <para><b>البابُ الَّذي تَراهُ الشاشاتُ والنُقاط — ويُجيبُ «لا» بِلا
/// انفِجار.</b></para>
///
/// <para><b>ولِماذا غِلافٌ لا حَقنُ المُزَوِّدِ مُباشَرَةً</b>:
/// المُزَوِّدُ <b>لا يُسَجَّل إطلاقاً</b> بِلا اعتِمادٍ مَضبوط — وهذا
/// مَقصود، فَتَسجيلُه بِلا سِرٍّ يَعني صِنفاً يَرمي عِندَ أَوَّلِ
/// حَلٍّ لَه. وصَفحَةٌ تَطلُب خِدمَةً غَيرَ مُسَجَّلَةٍ تَنفَجِر عِندَ
/// التَصيير. فَهذا الغِلافُ مُسَجَّلٌ <b>دائِماً</b>، ويُجيبُ
/// <see cref="IsConfigured"/> بِـ<c>false</c> — <b>فَتُخفي الشاشَةُ
/// البِطاقَةَ بَدَلَ أَن تَعرِضَ زِرّاً يَقول «قَريباً»</b>
/// (القاعِدَة ١٢).</para>
///
/// <para><b>ومَداهُ <c>Scoped</c> لا <c>Singleton</c></b>: يَحمِل
/// مُزَوِّداً يَحمِل <c>HttpClient</c> مِن المَصنَع، وحَبسُ عَميلٍ في
/// مُفرَدَةٍ يُجَمِّد مُعالِجَه فَتَتَعَفَّن مَداخِلُ DNS. والرَمزُ
/// وَحدَه مُفرَدٌ — وهُوَ الشَيءُ الوَحيدُ الَّذي يَجِب أَن
/// يَعيشَ أَطوَلَ مِن الطَلَب.</para>
/// </summary>
public sealed class PayPalGateway
{
    private readonly PayPalOptions _opts;
    private readonly PayPalPaymentProvider? _provider;

    public PayPalGateway(PayPalOptions opts, PayPalPaymentProvider? provider)
    {
        _opts = opts;
        _provider = provider;
    }

    /// <summary>أَيُمكِن إنشاءُ رابِطِ اشتِراك؟ (اعتِمادٌ + بيئَةٌ
    /// مَعروفَة.)</summary>
    public bool IsConfigured => _provider is not null && PayPalEnvironment.IsConfigured(_opts);

    /// <summary>أَتُقبَلُ رِسالَةُ Webhook؟ يَزيدُ عَلى ما سَبَقَ
    /// <c>WebhookId</c> — <b>وهُما شَرطانِ لا شَرطٌ واحِد</b>: المالِكُ
    /// يَضبُط الاعتِمادَ يَومَ يُنشِئ التَطبيق، والـWebhook يَومَ
    /// يُنشِئ الاشتِراكَ عَلى عُنوانٍ يَعمَل.</summary>
    public bool CanVerifyWebhooks => _provider is not null && PayPalEnvironment.CanVerifyWebhooks(_opts);

    /// <summary>الخِيارات — لِتُمَرَّرَ إلى
    /// <see cref="PayPalBillingPolicy.Gate"/>، ولا تُنسَخَ قِيَمُها.
    /// <b>ولا سِرَّ يُقرَأُ مِنها في شاشَة</b>: البابُ يُسأَل
    /// <see cref="IsConfigured"/>، والخِياراتُ تُمَرَّر إلى دالَّةِ
    /// القَرارِ وَحدَها.</summary>
    public PayPalOptions Options => _opts;

    /// <summary>تَحَقُّقٌ مِن التَوقيع — و<c>false</c> حينَ لا مُزَوِّدَ
    /// أَصلاً. <b>لا استِثناءَ ولا «مَرَّت لِأَنَّها غَيرُ
    /// مُهَيَّأَة»</b>.</summary>
    public Task<bool> VerifyWebhookSignatureAsync(
        PayPalWebhookHeaders headers, string rawBody, CancellationToken ct = default)
        => _provider is null
            ? Task.FromResult(false)
            : _provider.VerifyWebhookSignatureAsync(headers, rawBody, ct);

    /// <summary>
    /// <para><b>يُنشِئ مُنتَجَ الكاتالوجِ ثُمَّ خُطَّةَ الفَوتَرَة،
    /// ويُعيد المُعَرِّفَين.</b> والتَرتيبُ مُلزِمٌ لا تَفضيليّ:
    /// مُنشِئُ الخُطَّةِ يَشتَرِط <c>product_id</c> قائِماً.</para>
    ///
    /// <para><b>ولِماذا التَركيبُ هُنا لا في النُقطَة</b>: النِداءانِ
    /// <b>مُتَرابِطانِ بِمُخرَجِ الأَوَّلِ مُدخَلاً لِلثاني</b>، فَتَركُ
    /// وَصلِهِما لِلنُقطَةِ يَجعَلُ التَرتيبَ سَطراً في جِسمٍ يُنسى —
    /// وهُنا يُقاس بِمُعالِجٍ وَهمِيّ يَعُدُّ الطَلَبات (القاعِدَة ٢).</para>
    ///
    /// <para><b>ويَرمي ولا يُجيب «لا» صامِتاً</b>، بِخِلافِ
    /// <see cref="CreateSubscriptionAsync"/>: تِلكَ تُنادى مِن نُقطَةٍ
    /// تُحَوِّل، وهذِه مِن نَموذَجٍ يَنتَظِرُ مُشرِفٌ نَتيجَتَه —
    /// و«لَم يَحدُث شَيء» بِلا سَبَبٍ أَسوَأُ مِن رِسالَةٍ تُسَمّي رَمزَ
    /// PayPal. <b>والشاشَةُ لا تَرسِم النَموذَجَ أَصلاً بِلا تَهيئَة</b>،
    /// فَهذا الرَميُ حِزامُ أَمانٍ ثانٍ لا الطَريقَ الأَوَّل.</para>
    /// </summary>
    public async Task<PayPalCatalogPlan> CreateCatalogPlanAsync(
        PayPalPlanDraft draft, CancellationToken ct = default)
    {
        if (_provider is null)
            throw new InvalidOperationException("PayPal غَير مُهَيَّأ في هذِه النُسخَة.");

        var productId = await _provider.CreateCatalogProductAsync(draft, ct);
        var planId    = await _provider.CreateBillingPlanAsync(productId, draft, ct);
        return new PayPalCatalogPlan(productId, planId);
    }

    /// <summary>
    /// <para><b>يُنشِئ طَلَبَ دَفعٍ مَرِناً ويُعيد رابِطَ الصَفحَةِ
    /// المُستَضافَة</b> (‏ADR-006) — و«لا» بِلا انفِجارٍ حينَ لا
    /// مُزَوِّدَ مُسَجَّلاً، كَجارَتِها
    /// <see cref="CreateSubscriptionAsync"/> حَرفاً.</para>
    ///
    /// <para><b>وهُنا يَقَع الفاصِل</b>: التَوقيعُ يَقول
    /// <c>(المُسَوَّدَة، مَرجِعُنا، مُضيفُنا، مِفتاحُ مَرَّة-واحِدَة)
    /// → رابِط</c>، وهُوَ الشَكلُ الَّذي سَيَأخُذُه أَيُّ مُزَوِّدٍ
    /// مَحَلِّيٍّ لاحِقاً. <b>ولا واجِهَةَ تُخترَعُ اليَومَ</b>: مُستَهلِكُها
    /// الثاني غَيرُ مَوجود (القاعِدَة ١).</para>
    /// </summary>
    public Task<PayPalOrderResult> CreateOrderAsync(
        PayPalOrderDraft draft, string reference, string origin, string idempotencyKey,
        CancellationToken ct = default)
        => _provider is null
            ? Task.FromResult(new PayPalOrderResult(
                "", "", null, "PayPal غَير مُهَيَّأ في هذِه النُسخَة."))
            : _provider.CreateOrderAsync(draft, reference, origin, idempotencyKey, ct);

    /// <summary>يَلتَقِطُ المالَ لِطَلَبٍ وافَقَ عَلَيه الدافِع — <b>ولا
    /// يُمَدِّدُ باقَة</b>: التَمديدُ عِندَ
    /// <c>PAYMENT.CAPTURE.COMPLETED</c> وَحدَه.</summary>
    public Task<PayPalCaptureResult> CaptureOrderAsync(
        string orderId, string idempotencyKey, CancellationToken ct = default)
        => _provider is null
            ? Task.FromResult(new PayPalCaptureResult(
                "", "", null, "PayPal غَير مُهَيَّأ في هذِه النُسخَة."))
            : _provider.CaptureOrderAsync(orderId, idempotencyKey, ct);

    /// <summary>يُنشِئ اشتِراكاً لِمَتجَرٍ بِخُطَّةِ PayPal المَذكورَةِ
    /// في تَعريفِ الباقَة، ويَضَع سلاجَ المَتجَرِ في
    /// <c>custom_id</c>.</summary>
    public Task<SubscriptionResult> CreateSubscriptionAsync(
        string payPalPlanId, string tenantSlug, string idempotencyKey,
        CancellationToken ct = default)
        => _provider is null
            ? Task.FromResult(new SubscriptionResult(
                "", false, default, "PayPal غَير مُهَيَّأ في هذِه النُسخَة."))
            : _provider.CreateSubscriptionAsync(
                new SubscriptionRequest(tenantSlug, payPalPlanId, 0m, ""), idempotencyKey, ct);
}

public static class PayPalExtensions
{
    /// <summary>
    /// <para><b>يُسَجِّل PayPal — أَو لا يُسَجِّلُه، والقَرارُ
    /// بِالتَهيئَة.</b> نَفسُ نَمَطِ <c>AuthChannelSelection</c>:
    /// اعتِمادٌ مَضبوطٌ ⇒ مُزَوِّدٌ حَقيقيّ؛ وغِيابُه ⇒
    /// <b>لا تَسجيل</b>، والغِلافُ يَقول «لا» بِلا انفِجار.</para>
    ///
    /// <para><b>ولا يُسَجَّلُ عَلى <c>IPaymentProvider</c></b>: تِلكَ
    /// الفَتحَةُ مَشغولَةٌ بِمُزَوِّدِ عَرَبونِ الصَفقات
    /// (<c>AddMockPayments</c>)، وتَسجيلُ PayPal عَلَيها يَجعَل
    /// <c>DealsService</c> تَحجُز عَرَبونَ مُشتَرٍ عَلى حِسابِ وَسايِل
    /// — <b>خَلطُ مالَينِ لا يَلتَقِيان</b>.</para>
    /// </summary>
    public static IServiceCollection AddPayPalSubscriptions(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<PayPalOptions>(configuration.GetSection(PayPalEnvironment.SectionKey));

        // الرَمزُ مُفرَدٌ — وهُوَ الشَيءُ الوَحيدُ الَّذي يَجِب أَن
        // يَعيشَ أَطوَلَ مِن الطَلَب (وإلّا نودِيَ OAuth عِندَ كُلّ نِداء).
        services.AddSingleton<PayPalTokenCache>();

        var options = new PayPalOptions();
        configuration.GetSection(PayPalEnvironment.SectionKey).Bind(options);

        if (PayPalEnvironment.IsConfigured(options))
        {
            services.AddHttpClient<PayPalPaymentProvider>(client =>
            {
                // سَقفٌ احتِياطيٌّ لَو سَقَطَ الرَمزُ الداخِليّ —
                // **ضِعفُ** نافِذَةِ المُزَوِّدِ لا مُساوِيها، لِيَسبِقَ
                // الرَمزُ دائِماً فَتَثبُتَ رِسالَةُ الخَطَإ بَدَلَ أَن
                // تَتَبَدَّلَ بِسِباق. (نَفسُ حُجَّةِ `AddBrevoEmailChannel`.)
                client.Timeout = PayPalEnvironment.Timeout(options.TimeoutSeconds) * 2;
            });
        }

        services.AddScoped(sp => new PayPalGateway(
            sp.GetRequiredService<IOptions<PayPalOptions>>().Value,
            sp.GetService<PayPalPaymentProvider>()));

        return services;
    }
}
