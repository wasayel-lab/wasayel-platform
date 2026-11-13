namespace ACommerce.Platform.Shared.Metering;

// ═══ الحَدُّ الصادِر — نِداءٌ يُغادِرُ العَمَلِيَّةَ يُعلَنُ في تَوقيعِه (‏2026-11-12، الموجَة ١٣ W13-6·١١ — ADR-051 §١٨؛ التَصميم §6) ═══════════════
//
// **ما هذا المِلَفّ**: ما يَحمِلُه المُنادي إلى العُدَّةِ حينَ يُغادِرُ نِداءٌ العَمَلِيَّة — الوَحدَةُ والمُزَوِّدُ والمَتجَرُ ومُعَرِّفُ الطَلَب — والنَتائِجُ الثَلاث.
// **وهُوَ في التَوقيعِ لا في الجِسم** (القاعِدَة ٦): كُلُّ دالَّةٍ عامَّةٍ في عُدَّةٍ تُغادِرُ العَمَلِيَّةَ تَأخُذُه أَوَّلَ وُسَطائِها، فَالمَتجَرُ يَحمِلُه مَن يَعرِفُه
// (القالِبُ وبابُ الدُخول) لا مَن يُخَمِّنُه.
//
// **مُستَهلِكُه الأَوَّلُ في وَقتِ التَشغيل** (القاعِدَة ١): قَناةُ Twilio — بابُ الرَمزِ في المَتجَرِ والاستوديو، وإشعارُ رَبطِ الوَسيلَة. والعُدَدُ الباقِيَةُ تَدخُلُ
// واحِدَةً بَعدَ أُخرى (البُنودُ ١٢–١٥)، ومَعَها ما تَحتاجُه أَوَّلاً: النِداءُ المُساعِدُ الَّذي لا يُعَدّ (رَمزُ OAuth والاستِطلاع — أَوَّلُ مُنادٍ لَه نَفاذ)
// والتَحويلُ مِن `HttpClient` (أَوَّلُ مُنادٍ لَه اختِباراتُ Brevo) — لا قَبلَ مُستَهلِكِهِما.

/// <summary>
/// <para><b>نِداءٌ صادِرٌ مَعدود</b>: <see cref="Unit"/> رَمزٌ مِن المُعجَم (حَدُّه <c>outbound</c>)، و<see cref="Provider"/> اسمُ المُزَوِّد
/// (<see cref="ProviderNames"/>)، و<see cref="App"/> المَتجَرُ أَو <see cref="MeterSentinels.Platform"/>، و<see cref="RequestId"/> مُعَرِّفُ العَمَلِيَّةِ المَنطِقِيَّةِ
/// المَعروفُ <b>قَبلَ</b> النِداء — طَبيعِيٌّ حَيثُ يَمنَعُ المُزَوِّدُ التَكرارَ أَو لَنا مِفتاحٌ مَنطِقِيٌّ ثابِت، وإلّا مُعَرِّفُ نِداءٍ جَديد.</para>
/// <para><b>والمُعَرِّفُ <see cref="Id"/> = <c>{unit}:{provider}:{requestId}</c> بِلا المَتجَر</b> (التَصميم §6.2، COR-4): المَتجَرُ عَمودٌ لا جُزءٌ مِن المِفتاح،
/// فَالطَلَبُ نَفسُه تَحتَ حامِلَينِ سَطرٌ واحِد.</para>
/// <para><b>والمُزَوِّدُ يَسِمُ نَفسَه</b> (<see cref="By"/>): قَناةٌ تُنادى بِواجِهَتِها (<c>IOtpChannel</c>) لا يَعرِفُ مُناديها مَن يُنَفِّذُها، فَيَبنيها المُنادي
/// بِـ<see cref="ForChannel"/> (المُزَوِّدُ <see cref="MeterSentinels.None"/>) وتَسِمُها القَناةُ بِاسمِها قَبلَ أَن تُرسِل — وقَناةٌ لا تَسِمُ تُرى <c>_none</c> لا تَضيع.</para>
/// </summary>
public sealed record ProviderCall(string Unit, string Provider, string App, string RequestId)
{
    /// <summary>مُعَرِّفُ السَطرِ والنِيَّة: <c>{unit}:{provider}:{requestId}</c>.</summary>
    public string Id => $"{Unit}:{Provider}:{RequestId}";

    /// <summary>نِداءٌ عَلى قَناةٍ تُنادى بِواجِهَتِها — مُزَوِّدُه تَسِمُه القَناة (<see cref="By"/>).</summary>
    public static ProviderCall ForChannel(string unit, string app, string requestId) => new(unit, MeterSentinels.None, app, requestId);

    /// <summary>مُعَرِّفُ نِداءٍ جَديد — حينَ لا مِفتاحَ مَنطِقِيّاً يَسبِقُ النِداء، فَكُلُّ نِداءٍ عَمَلِيَّتُه.</summary>
    public static string FreshRequestId() => Guid.NewGuid().ToString("N");

    /// <summary>القَناةُ تَسِمُ النِداءَ بِاسمِها.</summary>
    public ProviderCall By(string provider) => this with { Provider = provider };

    // ─── مَصدَرُ السَطرِ حينَ لا يَكونُ مُعَرِّفَ الطَلَب (‏2026-11-12، W13-6·١٣ — التَصميم §6.2) ─────────────────────────────────────────
    // **مُعَرِّفُ الطَلَبِ مِفتاحُ العَمَلِيَّة، والمَصدَرُ ما عُدّ** — يَتَساوَيانِ في كُلِّ وَحدَةٍ إلّا الرَفع: كُلُّ رَفعٍ عَمَلِيَّةٌ (مُعَرِّفُ نِداءٍ جَديد، فَإعادَةُ
    // الرَفعِ رَفعٌ ثانٍ) والمَعدودُ المِلَفُّ نَفسُه — ومِفتاحُه يَحمِلُ مُعَرِّفَ شَخص (`tenants/{slug}/avatars/{userId}.{ext}`)، فَلا يَدخُلُ الدَفتَرَ إلّا اسماً
    // مُستَعاراً (`UsageEntryId.Pseudonym`). مُستَهلِكُه الأَوَّلُ في وَقتِ التَشغيل `S3FileStorage.UploadAsync`؛ والكاتِبُ يَقرَؤُه وإلّا فَالطَلَب.

    /// <summary>نَوعُ المَصدَرِ إن لَم يَكُن الطَلَب — <c>null</c> ⇐ الطَلَب.</summary>
    public string? SourceType { get; init; }

    /// <summary>مُعَرِّفُ المَصدَرِ إن لَم يَكُن مُعَرِّفَ الطَلَب.</summary>
    public string? SourceId { get; init; }

    /// <summary>‏(W13-6·١٣) نَوعُ مَصدَرِ الرَفع: المِلَفُّ بِمِفتاحِه المُستَعار.</summary>
    public const string FileKeySource = "file_key";

    /// <summary>النِداءُ نَفسُه ومَصدَرُه غَيرُ الطَلَب.</summary>
    public ProviderCall About(string sourceType, string sourceId) => this with { SourceType = sourceType, SourceId = sourceId };

    // ─── (‏2026-11-12، W13-6·١٤ — التَصميم §6.2) الفاتورَةُ مَصدَراً، ومِفتاحُ المُزَوِّدِ مُعَرِّفاً ─────────────────────────────────────────
    // **مَصدَرُ جَلبِ الفاتورَةِ وإلغائِها الفاتورَةُ نَفسُها** — ومُعَرِّفُ الطَلَبِ `{invoiceId}:{purpose}[:{trigger}]` يُمَيِّزُ العَمَلِيَّة. مُستَهلِكُه الأَوَّلُ في
    // وَقتِ التَشغيل `GatewayPayments` و`GatewayInvoiceCloser`.
    /// <summary>‏(W13-6·١٤) نَوعُ مَصدَرِ جَلبِ فاتورَةِ البَوّابَةِ وإلغائِها: مُعَرِّفُ الفاتورَة.</summary>
    public const string InvoiceSource = "invoice";

    /// <summary>
    /// <para>‏(W13-6·١٤) <b>مِفتاحُ المُزَوِّدِ مُعَرِّفاً</b>: العُدَّةُ تَضَعُ مُعَرِّفَ الطَلَبِ الَّذي تُرسِلُه هِيَ في رَأسِ مَنعِ التَكرار — حَيثُ لا يَعرِفُه المُنادي قَبلَ
    /// النِداء. مُستَهلِكُه الأَوَّل: خُطَّةُ PayPal، ومِفتاحُها (<c>PayPal-Request-Id</c>) مُشتَقٌّ مِن مُنتَجٍ يُعيدُه النِداءُ المُساعِدُ قَبلَها
    /// (<c>PayPalCatalogPolicy.PlanRequestId</c>). فَالطَلَبُ المُعادُ بِالمُسَوَّدَةِ نَفسِها يَحمِلُ المُعَرِّفَ نَفسَه، كَما يَراه PayPal.</para>
    /// </summary>
    public ProviderCall Keyed(string requestId) => this with { RequestId = requestId };

    // ─── (‏2026-11-12، W13-6·١٥ — التَصميم §6.2) طَلَبُ الشَحنِ مَصدَراً ───────────────────────────────────────────────────────────────────
    // **مَصدَرُ إنشاءِ طَلَبِ الشَحنِ وسُؤالِ حالِه الطَلَبُ نَفسُه** (مُعَرِّفُنا)، ومُعَرِّفُ الطَلَبِ يُمَيِّزُ العَمَلِيَّة. مُستَهلِكُه الأَوَّلُ في وَقتِ التَشغيل
    // `ShippingCalls` (القالِب).
    /// <summary>‏(W13-6·١٥) نَوعُ مَصدَرِ إنشاءِ طَلَبِ الشَحنِ وسُؤالِ حالِه: مُعَرِّفُ طَلَبِنا عِندَ الناقِل.</summary>
    public const string DeliveryOrderSource = "delivery_order";

    // ─── (‏2026-11-13، مُراجَعَةُ المَرحَلَةِ ج الثالِثَة · M6C-CNT-R3-1 — ADR-051 §١٩) مَرجِعُ Paddle مَصدَراً لا مُعَرِّفاً ──────────────────────────
    // **مَرجِعُنا مِفتاحُ وَثيقَتِنا لا مِفتاحُ مَنعِ تَكرارٍ عِندَ Paddle** (`PaddlePaymentProvider.CreateTransactionAsync`: «يَمنَعُ وَثيقَتَين لا مُعامَلَتَين»؛
    // `docs/DEPLOY.md` §٥-أ): نَقرَتانِ بِالمُسَوَّدَةِ نَفسِها مُعامَلَتانِ عِندَ Paddle، فَكُلُّ إنشاءٍ مَقبولٍ عَمَلِيَّتُه (مُعَرِّفُ نِداءٍ جَديد) والمَرجِعُ ما عُدّ.
    // مُستَهلِكُه الأَوَّلُ في وَقتِ التَشغيل `PaddleSurface.CreateCall` (بابُ رابِطِ Paddle).
    /// <summary>‏(M6C-CNT-R3-1) نَوعُ مَصدَرِ إنشاءِ مُعامَلَةِ Paddle: مَرجِعُنا (<c>PaddleTransactionPolicy.Reference</c>) — والمُعَرِّفُ نِداءٌ جَديد.</summary>
    public const string PaddleReferenceSource = "paddle_reference";
}

/// <summary>
/// <para><b>نِداءٌ مُساعِدٌ لا يُعَدّ</b> (‏2026-11-12، W13-6·١٣ — التَصميم §3.4 «Helpers, not counted»، §6.1): ما يُغادِرُ العَمَلِيَّةَ لِيَخدُمَ عَمَلِيَّةً
/// مَعدودَةً أَو قِراءَةً — رَمزُ OAuth، واستِطلاعُ الحالَة، وقِراءَةُ المَخزَنِ وحَذفُه. يَمُرُّ بِالمُستَدعي (<see cref="ProviderCalls.HelperAsync{T}"/>،
/// <see cref="ProviderHttp{TKit}.SendHelperAsync"/>) فَتَراه الطَبَقَةُ ٨-ج داخِلَه، ولا نِيَّةَ لَه ولا سَطر.</para>
/// <para><b>والسَبَبُ مِن قائِمَةٍ مُغلَقَة</b> (<see cref="ProviderHelperReasons"/>): سَبَبٌ خارِجَها يَرمي عِندَ البِناء — فَلا يَصيرُ «مُساعِداً» ما لا اسمَ لَه.</para>
/// </summary>
public sealed record ProviderHelper
{
    public ProviderHelper(string provider, string reason)
    {
        if (!ProviderHelperReasons.All.Contains(reason, StringComparer.Ordinal))
            throw new ArgumentException($"سَبَبُ النِداءِ المُساعِدِ «{reason}» خارِجَ القائِمَة: {string.Join("، ", ProviderHelperReasons.All)}.", nameof(reason));
        Provider = provider;
        Reason = reason;
    }

    public string Provider { get; }
    public string Reason { get; }
}

/// <summary>
/// <para><b>أَسبابُ النِداءِ المُساعِد — قائِمَةٌ مُغلَقَةٌ تُطبَع</b> (التَصميم §3.4). تَدخُلُها الأَسبابُ مَعَ أَوَّلِ مُنادٍ لَها (القاعِدَة ١): نَفاذ (الرَمزُ
/// والاستِطلاع) ومَخزَنُ الكائِنات (القِراءَةُ والحَذف) في البَند ١٣؛ و<c>metadata_tag</c> (ميسّر) و<c>catalog_product</c> (PayPal) في البَند ١٤.</para>
/// <para>‏(‏2026-11-12، W13-6·١٤ — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه) <b>ودَخَلَت الثَلاثَة</b>: <see cref="MetadataTag"/> و<see cref="CatalogProduct"/> بِمُنادِيَيهِما، و<see cref="UncalledMember"/>
/// لِعُضوَي <c>IPaymentProvider</c> في PayPal بِصِفرِ مُنادٍ في وَقتِ التَشغيل (التَصميم §3.4: «دَينُ القاعِدَةِ ١ لا وَحدَة») — يَمُرّانِ بِالمُستَدعي فَتَراهُما ٨-ج،
/// ولا يُعَدّانِ لِأَنّهُما لا يَجرِيان؛ و<c>MeterTestSeamTests</c> (G6c) يَحمَرُّ عَلى أَوَّلِ مُنادٍ لَهُما في <c>libs/</c> أَو <c>apps/</c>، فَيولَدُ مَعَه صَفُّ وَحدَة.</para>
/// </summary>
public static class ProviderHelperReasons
{
    /// <summary>رَمزُ وُصولِ OAuth قَبلَ النِداء — <c>NafathChannel</c>.</summary>
    public const string OauthToken = "oauth_token";

    /// <summary>استِطلاعُ حالَةِ عَمَلِيَّةٍ عُدَّت عِندَ بَدئِها — <c>NafathChannel.IsApprovedAsync</c>.</summary>
    public const string StatusPoll = "status_poll";

    /// <summary>قِراءَةُ المَخزَنِ (الجَلبُ والوُجود) — <c>S3FileStorage</c>.</summary>
    public const string StorageRead = "storage_read";

    /// <summary>حَذفٌ مِن المَخزَن — <c>S3FileStorage.DeleteAsync</c>.</summary>
    public const string StorageDelete = "storage_delete";

    /// <summary>‏(W13-6·١٤) وَسمُ فاتورَةٍ أُنشِئَت بِبَياناتِ صَفَقاتِها (<c>PUT /invoices/:id</c>) — <c>MoyasarClient.CreateInvoiceAsync</c>: الإنشاءُ يُعَدُّ عِندَ ‏2xx الـ<c>POST</c>.</summary>
    public const string MetadataTag = "metadata_tag";

    /// <summary>‏(W13-6·١٤) مُنتَجُ الكاتالوجِ قَبلَ خُطَّةِ الفَوتَرَة — <c>PayPalPaymentProvider.CreateCatalogProductAsync</c>: الخُطَّةُ هِيَ المَعدودَة.</summary>
    public const string CatalogProduct = "catalog_product";

    /// <summary>‏(W13-6·١٤) عُضوُ واجِهَةٍ بِصِفرِ مُنادٍ في وَقتِ التَشغيل — <c>PayPalPaymentProvider.CancelSubscriptionAsync</c> و<c>GetInvoiceAsync</c>
    /// (<c>IPaymentProvider</c>، وPayPal لا يُسَجَّلُ عَلَيها): دَينُ القاعِدَةِ ١، مَحروسٌ في G6c.</summary>
    public const string UncalledMember = "uncalled_member";

    public static readonly IReadOnlyList<string> All = [OauthToken, StatusPoll, StorageRead, StorageDelete, MetadataTag, CatalogProduct, UncalledMember];
}

/// <summary>نَتيجَةُ النِداءِ الصادِر — ثَلاث: <b>قُبِل</b> (يُكتَبُ السَطر)، و<b>رُفِض</b> (4xx، ومِنه 429)، و<b>مَجهول</b> (5xx، أَو مُهلَتُنا، أَو إلغاءُ المُنادي، أَو
/// استِثناءٌ بَعدَ الإرسال) — والمَجهولُ يُعَدُّ عَلى النِيَّةِ ولا يَصيرُ سَطراً.</summary>
public enum ProviderOutcome { Accepted, Refused, Unknown }

/// <summary>ما تَقولُه العُدَّةُ عَن جَوابِ المُزَوِّد: النَتيجَةُ والكَمِّيَّة (1 لِكُلِّ عَمَلِيَّة).</summary>
public sealed record ProviderAcceptance(ProviderOutcome Outcome, long Quantity = 1);
