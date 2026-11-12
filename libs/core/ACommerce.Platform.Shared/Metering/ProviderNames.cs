namespace ACommerce.Platform.Shared.Metering;

/// <summary>
/// <para><b>أَسماءُ المُزَوِّدينَ والوَحَداتِ الصادِرَة</b> — أَسماءٌ مِن الكود، لا أَرقام (القاعِدَة ١٦). والوَحدَةُ نَفسُها صَفٌّ في <c>meter-units.json</c>
/// (حَدُّه <c>outbound</c>، وقارِئُ مُطابَقَتِه <c>intents</c>)، والثابِتُ هُنا رَمزُها الَّذي يَحمِلُه المُنادي.</para>
/// </summary>
public static class ProviderNames
{
    /// <summary>قَناةُ الرَسائِلِ القَصيرَةِ عَبرَ Twilio (اعتِمادُ المَنَصَّة).</summary>
    public const string Twilio = "twilio";

    /// <summary>‏(‏2026-11-12، W13-6·١٢) قَناةُ البَريدِ عَبرَ واجِهَةِ Brevo عَلى ‏443.</summary>
    public const string Brevo = "brevo";

    /// <summary>‏(‏2026-11-12، W13-6·١٢) قَناةُ البَريدِ عَبرَ خادِمِ SMTP المَضبوط (MailKit).</summary>
    public const string Smtp = "smtp";

    /// <summary>‏(‏2026-11-12، W13-6·١٣) قَناةُ نَفاذ (يَقين) — بَدءُ التَحَقُّق.</summary>
    public const string Nafath = "nafath";

    /// <summary>‏(‏2026-11-12، W13-6·١٣) مَخزَنُ الكائِناتِ المُتَوافِقُ مَع S3 — اسمُ العُدَّةِ نَفسِها (<c>S3FileStorage.ProviderName</c> بِحُروفٍ صَغيرَة)، <b>لا سلاجُ
    /// تَعريفِه في كاتالوجِ المُزَوِّدين</b>: الكاتالوجُ يُمنَعُ سلاجُه في الكود (<c>ProviderCatalogTests.No_source_file_outside_the_catalogue_names_a_provider_slug</c>)
    /// كَي يَكفِيَ المُزَوِّدَ التالي مِلَفُّ بَيانات — والعَمودُ «اسمُ المُزَوِّدِ أَو القَناة» (التَصميم §2).</summary>
    public const string S3 = "s3";

    /// <summary>‏(‏2026-11-12، W13-6·١٣) خِدمَةُ إشعارِ المُتَصَفِّح (Web Push بِمَفاتيحِ VAPID).</summary>
    public const string WebPush = "webpush";

    /// <summary>‏(‏2026-11-12، W13-6·١٤) <b>بَوّابَةُ المَتجَر</b> — فَواتيرُ مُستَضافَةٌ بِمِفتاحِ المُستَأجِر (<c>MoyasarClient</c>). <b>والقيمَةُ اسمُ المُزَوِّدِ نَفسُه</b>؛
    /// أَمّا اسمُ العُضوِ فَبِدَورِه لا بِاسمِه: حارِسُ القاعِدَة ١٦ (<c>MeterUnitCatalogTests.PriceIsh</c>) يَقرَأُ أَسماءَ أَعضاءِ المُجَلَّدِ بِلا حُدودِ كَلِمَة،
    /// واسمُ المُزَوِّدِ يَنتَهي بِمَقطَعٍ يُمسِكُه. ولا سلاجُ تَعريفِه في الكاتالوج (يُمنَعُ في الكود، <c>ProviderCatalogTests</c>).</summary>
    public const string StoreGateway = "moyasar";

    /// <summary>‏(‏2026-11-12، W13-6·١٤) فَوتَرَةُ اشتِراكِ المَتجَرِ في المَنَصَّة عَبرَ PayPal (<c>PayPalPaymentProvider</c>) — اسمُ العُدَّة، لا سلاجُ تَعريفِها.</summary>
    public const string PayPal = "paypal";

    /// <summary>‏(‏2026-11-12، W13-6·١٤) فَوتَرَةُ اشتِراكِ المَتجَرِ عَبرَ Paddle (<c>PaddlePaymentProvider</c>) — اسمُ العُدَّة، لا سلاجُ تَعريفِها.</summary>
    public const string Paddle = "paddle";
}

/// <summary>رُموزُ الوَحَداتِ الصادِرَةِ الَّتي يَبنيها مُنادٍ في وَقتِ التَشغيل — بِحَرفِ صُفوفِها في المُعجَم.</summary>
public static class OutboundUnits
{
    /// <summary>رَمزُ دُخولٍ بِالرَسائِلِ القَصيرَة — <c>IOtpChannel.SendOtpAsync</c>.</summary>
    public const string OtpSmsSend = "otp.sms.send";

    /// <summary>إشعارٌ بِالرَسائِلِ القَصيرَة — <c>IOtpChannel.SendNoticeAsync</c>.</summary>
    public const string NoticeSmsSend = "notice.sms.send";

    /// <summary>‏(‏2026-11-12، W13-6·١٢) رَمزُ دُخولٍ بِالبَريد — <c>IEmailOtpChannel.SendOtpAsync</c>.</summary>
    public const string OtpEmailSend = "otp.email.send";

    /// <summary>‏(‏2026-11-12، W13-6·١٢) إشعارٌ بِالبَريد — <c>IEmailOtpChannel.SendNoticeAsync</c>.</summary>
    public const string NoticeEmailSend = "notice.email.send";

    /// <summary>‏(‏2026-11-12، W13-6·١٣) بَدءُ تَحَقُّقِ نَفاذ — <c>INafathChannel.StartAsync</c> (الاستِطلاعُ والرَمزُ مُساعِدانِ لا يُعَدّان).</summary>
    public const string IdentityNafathStart = "identity.nafath.start";

    /// <summary>‏(‏2026-11-12، W13-6·١٣) رَفعُ مِلَفٍّ إلى المَخزَنِ الدائِم — <c>IFileStorage.UploadAsync</c>؛ الكَمِّيَّةُ بايتاتُه.</summary>
    public const string FileUpload = "file.upload";

    /// <summary>‏(‏2026-11-12، W13-6·١٣) إشعارٌ فَوريٌّ إلى اشتِراكِ مُتَصَفِّح — <c>WebPushService.SendAsync</c>، سَطرٌ لِكُلِّ اشتِراكٍ قَبِلَته الخِدمَة.</summary>
    public const string PushSend = "push.send";

    // ─── (‏2026-11-12، W13-6·١٤ — التَصميم §3.4 و§6.2) بَوّابَةُ المَتجَرِ وفَوتَرَةُ الاشتِراك ───────────────────────────────────────────────

    /// <summary>فاتورَةٌ مُستَضافَةٌ تُنشَأُ — <c>MoyasarClient.CreateInvoiceAsync</c>: يُعَدُّ <c>POST</c> وَحدَه حينَ يُقبَل، ووَسمُ بَياناتِها (<c>PUT</c>) مُساعِد.</summary>
    public const string PaymentGatewayInvoiceCreate = "payment.gateway.invoice.create";

    /// <summary>فاتورَةٌ تُجلَب — <c>MoyasarClient.FetchInvoiceAsync</c>؛ مُعَرِّفُ الطَلَبِ <c>{invoiceId}:{purpose}[:{trigger}]</c>.</summary>
    public const string PaymentGatewayInvoiceFetch = "payment.gateway.invoice.fetch";

    /// <summary>فاتورَةٌ تُلغى عِندَ البَوّابَة — <c>MoyasarClient.CancelInvoiceAsync</c>.</summary>
    public const string PaymentGatewayInvoiceCancel = "payment.gateway.invoice.cancel";

    /// <summary>إشعارُ الدَفعِ يُسَجَّلُ عِندَ البَوّابَة — <c>MoyasarClient.CreateWebhookAsync</c>.</summary>
    public const string PaymentGatewayWebhookCreate = "payment.gateway.webhook.create";

    /// <summary>خُطَّةُ فَوتَرَةٍ تُنشَأُ عِندَ PayPal — <c>PayPalPaymentProvider.CreateBillingPlanAsync</c> (ومُنتَجُ الكاتالوجِ قَبلَها مُساعِد).</summary>
    public const string BillingPayPalPlanCreate = "billing.paypal.plan.create";

    /// <summary>رابِطُ اشتِراكٍ يُنشَأُ عِندَ PayPal — <c>PayPalPaymentProvider.CreateSubscriptionAsync</c>.</summary>
    public const string BillingPayPalLinkCreate = "billing.paypal.link.create";

    /// <summary>طَلَبُ دَفعٍ مَرِنٌ يُنشَأُ عِندَ PayPal — <c>PayPalPaymentProvider.CreateOrderAsync</c>.</summary>
    public const string BillingPayPalOrderCreate = "billing.paypal.order.create";

    /// <summary>طَلَبُ دَفعٍ يُلتَقَطُ عِندَ PayPal — <c>PayPalPaymentProvider.CaptureOrderAsync</c>.</summary>
    public const string BillingPayPalOrderCapture = "billing.paypal.order.capture";

    /// <summary>تَوقيعُ رِسالَةِ PayPal يُتَحَقَّقُ مِنه عِندَها — <c>PayPalPaymentProvider.VerifyWebhookSignatureAsync</c>.</summary>
    public const string BillingPayPalWebhookVerify = "billing.paypal.webhook.verify";

    /// <summary>مُعامَلَةٌ تُنشَأُ عِندَ Paddle — <c>PaddlePaymentProvider.CreateTransactionAsync</c>.</summary>
    public const string BillingPaddleTransactionCreate = "billing.paddle.transaction.create";
}
