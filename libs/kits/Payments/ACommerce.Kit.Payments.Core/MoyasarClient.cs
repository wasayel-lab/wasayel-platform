using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ACommerce.Kit.Payments;

/// <summary>
/// <para><b>عَميلُ بَوّابَةِ المُستَأجِر — صِنفُ <c>HttpClient</c> واحِدٌ بِلا واجِهَة</b> (‏ADR-032 §٢‑هـ الخُطوَة 8، والقاعِدَة ١).
/// يَعرِفُ عَمَلِيَّتَينِ لا ثالِثَةَ لَهُما في هذِه المَرحَلَة: <b>إنشاءُ فاتورَةٍ مُستَضافَة</b> يَدفَعُها المُشتَري عَلى صَفحَةِ
/// البَوّابَةِ ويَعود، و<b>جَلبُ فاتورَةٍ بِمُعَرِّفِها</b> لِيُتَحَقَّقَ مِن الدَفعِ مِن الخادِم — «لا JavaScript لِطَرَفٍ ثالِثٍ في
/// واجِهَةٍ بُنِيَت SSR» (‏ADR-032 §٢‑ج). وكُلُّ فَشَلٍ <b>رَمزٌ مُسَمّى</b> مِن <see cref="MoyasarFailures"/> — لا استِثناءٌ يَصعَدُ
/// إلى النُقطَة، ولا «لَم يَحدُث شَيء».</para>
///
/// <para><b>ولا يُنَفِّذُ عَقدَ مُزَوِّدِ الدَفعِ العامَّ — بِقَصد</b>: ذاكَ العَقدُ مُزَوِّدُ <b>النُسخَة</b> (حَجزٌ ثُمَّ قَبضٌ
/// ثُمَّ استِرجاعٌ بِمِفتاحِ المَنَصَّة)، وهذا مِفتاحُ <b>المُستَأجِر</b> يَدفَعُ بِه مُشتَريه إلى حِسابِه هُوَ. والمَفاتيحُ في
/// الكاتالوجِ بَياناتٌ تُختَم (`ProviderVault`)، لا تَسجيلٌ واحِدٌ لِلمَنَصَّة.</para>
///
/// <para><b>والسِرُّ لا يَسكُنُ هُنا</b> — حُجَّةُ <c>OtoClient</c> بِحَرفِها: المِفتاحُ السِرِّيُّ يُفَكُّ مِن الخِزانَةِ <b>لِكُلِّ
/// نِداءٍ</b> عِندَ المُنادي ويُمَرَّرُ وَسيطاً، ولا يُكتَبُ في حَقلٍ ولا في لوغٍ ولا في عُنوان: يَبلُغُ رَأسَ
/// <c>Authorization</c> وَحدَه، بِالمُخَطَّطِ الَّذي تَقولُه الوَثيقَة.</para>
///
/// <para><b>والحُقولُ والمَساراتُ مَقروءَةٌ لا مُخَمَّنَة</b> — مِن وَرَقَةِ البَحثِ `23-GATEWAYS-AND-PLATFORM-PAYMENTS.md` §٥
/// (‏<c>GP-30</c>: «صفحةُ ميسر المستضافة (<c>POST /invoices</c> → <c>url</c>)»، و<c>GP-33</c>: «The publishable API key is
/// restricted to a single operation only: Create Payment» فَالبُرهانُ جَلبٌ بِالسِرِّيّ)، ثُمَّ مِن وَثائِقِ ميسّر العامَّةِ
/// مَقروءَةً يَومَ ‏2026-10-12:</para>
/// <list type="bullet">
///   <item><c>https://docs.moyasar.com/api/authentication</c> — «Authentication to the API is performed via HTTP Basic Auth»،
///   «Username: &lt;your_api_key&gt;» و«Password: &lt;EMPTY&gt;»، ومِثالُها «<c>curl https://api.moyasar.com/v1/payments -u sk_test_123:</c>»
///   ومِنه المُضيفُ <see cref="BaseUrl"/>.</item>
///   <item><c>https://docs.moyasar.com/api/invoices/01-create-invoice</c> — <c>POST /invoices</c> بِحُقولِه السَبعَة: <c>amount</c>
///   («A positive integer representing the payment amount in the smallest currency unit»، «‏&gt;= 100»، «1.00 SAR = 100»)
///   و<c>currency</c> و<c>description</c> (إلزامِيَّةٌ ثَلاثَتُها) و<c>callback_url</c> و<c>success_url</c> («An endpoint where the
///   payer will be redirected to when the invoice is <c>paid</c>») و<c>back_url</c> و<c>expired_at</c>. <b>و<c>metadata</c> لَيسَ
///   مِنها</b> — فَلا يُرسَلُ هُنا.</item>
///   <item><c>https://docs.moyasar.com/api/invoices/05-update-invoice</c> — <c>PUT /invoices/:id</c> وجِسمُه حَقلٌ واحِد: <c>metadata</c>
///   («A set of key-value pairs where both key and value are strings»). فَمُعَرِّفاتُ الصَفَقاتِ تُلحَقُ بِالفاتورَةِ بِهذا النِداءِ
///   الثاني الموَثَّقِ لا بِحَقلٍ غَيرِ مُوَثَّقٍ في الأَوَّل. وحُدودُه مِن <c>https://docs.moyasar.com/api/metadata</c>: «up to 30 keys,
///   with key names up to 40 characters long and values up to 500 characters long».</item>
///   <item><c>https://docs.moyasar.com/api/invoices/04-show-invoice</c> — <c>GET /invoices/:id</c>، وحالُ الفاتورَةِ مِن مَعجَمٍ مُغلَق:
///   <c>initiated</c>، <c>paid</c>، <c>failed</c>، <c>refunded</c>، <c>canceled</c>، <c>on_hold</c>، <c>expired</c>، <c>voided</c>؛
///   ورُدودُ الخَطَإ ‏401 و‏403 و‏404 بِجِسمٍ فيه <c>type</c> و<c>message</c>.</item>
///   <item><c>https://docs.moyasar.com/api/invoices/06-cancel-invoice</c> (مَقروءَةً ‏2026-10-12، أُلحِقَت بِالعَمَلِيَّةِ الثالِثَةِ أَدناه) —
///   <c>PUT /invoices/:id/cancel</c> بِلا جِسم: «Cancel an existing Moyasar invoice so it can no longer be paid by the customer»؛
///   ومُعامِلُ المَسارِ «id · uuid · required · ID of invoice to cancel»، والرَدُّ ‏200 «Successful operation» الفاتورَةُ بِحالِها (مِثالُه
///   <c>"status": "canceled"</c>)، و‏401 و‏403 و‏404 بِأَشكالِ الجَلب. <b>ولا تَقولُ</b> أَيَّ حالٍ يَقبَلُ الإلغاءَ ولا بِمَ يُجابُ ما سِواه.</item>
/// </list>
///
/// <para><b>شَرطُ استِخراجِ واجِهَةٍ مِنه — مَكتوبٌ هُنا كَما تَطلُبُ القاعِدَةُ ١</b>: ثَلاثَةُ مُستَهلِكينَ وَقتَ التَشغيلِ يَحتاجونَ
/// <b>تَبديلَ</b> البَوّابَة — بَوّابَةٌ ثانِيَةٌ بِمِفتاحٍ سِرِّيٍّ في الكاتالوج (تاب أَو بيلينك، ‏§٥ مِن وَرَقَةِ البَحث) يُمَيِّزُها
/// تَعريفُها لا سَطرٌ في الكود. والكاتالوجُ اليَومَ فيه تَعريفُ دَفعٍ واحِدٌ بِحَقلٍ سِرِّيّ، ومُستَهلِكا هذا الصِنفِ في التَيّارِ
/// نَفسِه: نُقطَتا العَودَةِ والتَحَقُّقِ (الخُطوَة 9) وإتمامُ الشِراءِ بِالبِطاقَة (الخُطوَة 10). فَلا واجِهَة، ولا مُحاكيَ يُسَجَّلُ
/// مَكانَه — والاختِباراتُ تُعيدُ رُدودَ الوَثيقَةِ بِمُعالِجٍ مُزَيَّف.</para>
///
/// <para><b>وجاءَت ثالِثَة — ‏2026-10-12، مُراجَعَةُ تَيّارِ S9 الثانِيَة (ولا يُعادُ حَرفٌ فَوقَ ما سَبَق)</b>: «عَمَلِيَّتَينِ لا ثالِثَةَ لَهُما»
/// صَدَقَت عَلى ما بُنِيَ يَومَها، وكانَت الوَثيقَةُ تَعرِفُ إلغاءَ الفاتورَةِ ولَم تُقرَأ صَفحَتُه (القاعِدَة ١٦: يُقرَأُ ولا يُخَمَّن). فَصارَ
/// <b>إلغاءُ فاتورَةٍ مُستَضافَةٍ</b> (<see cref="CancelInvoiceAsync"/>) — مُستَهلِكُه وَقتَ التَشغيلِ إلغاءُ آخِرِ صَفقَةٍ قائِمَةٍ تَدفَعُ لَها
/// (<c>GatewayInvoiceCloser</c> مِن <c>DealsService.CancelAsync</c>)، فَلا يَدفَعُ مُشتَرٍ ثَمَنَ طَلَبٍ مُلغًى مِن صَفحَةٍ مَفتوحَةٍ أَمامَه.
/// <b>وصَفحَةُ «Update Invoice» مُلتَبِسَة — ويُقال</b>: مُقَدِّمَتُها «Update the details of an existing Moyasar invoice, such as its amount,
/// description, or expiry date»، وجَدوَلُ جِسمِها <c>metadata</c> وَحدَه؛ فَتَعديلُ المَبلَغِ غَيرُ مَقروءٍ يَقيناً ولا يُبنى عَلَيه. وشَرطُ
/// الاستِخراجِ أَعلاه لَم يَتَغَيَّر.</para>
/// </summary>
public sealed class MoyasarClient
{
    // ─── المُضيفُ والمَسارات — مُثَبَّتَةٌ لِيَقرَأَها الاختِبارُ بَدَلَ أَن يَنسَخَها ─────────
    /// <summary>المُضيفُ مِن مِثالِ صَفحَةِ المُصادَقَة؛ ومَفاتيحُ الاختِبارِ والحَيِّ عَلَيه مَعاً («Test: pk_test_ و sk_test_»،
    /// «Live: pk_live_ و sk_live_») — فَلا تَهيئَةَ بيئَةٍ تُختارُ هُنا: البادِئَةُ في المِفتاحِ تُقَرِّرُها البَوّابَة.</summary>
    public const string BaseUrl = "https://api.moyasar.com/v1";
    public const string InvoicesPath = "/invoices";

    /// <summary>ذَيلُ مَسارِ الإلغاءِ بَعدَ مُعَرِّفِ الفاتورَة — «<c>PUT /invoices/:id/cancel</c>» بِحَرفِ صَفحَةِ «Cancel invoice» (‏2026-10-12).</summary>
    public const string CancelSuffix = "/cancel";

    /// <summary>العُملَةُ الوَحيدَةُ الَّتي تُنشِئُ بِها المَنَصَّةُ فاتورَة — أَسعارُ المَتاجِرِ بِالرِيال.</summary>
    public const string Currency = "SAR";

    /// <summary>حالُ الفاتورَةِ المَدفوعَة — مِن مَعجَمِ <c>InvoiceStatus</c> بِحَرفِه.</summary>
    public const string PaidStatus = "paid";

    /// <summary>حالُ الفاتورَةِ المَفتوحَةِ الَّتي لَم تُدفَع — مِن مَعجَمِ <c>InvoiceStatus</c> بِحَرفِه، وهي حالُ مِثالِ رَدِّ «Create Invoice» نَفسِه
    /// (<c>"status": "initiated"</c>، مَقروءاً ‏2026-10-12). والوَثيقَةُ لا تَشرَحُ سائِرَ الحالات، فَلا يُلغى إلّا ما كانَ عَلَيها.</summary>
    public const string OpenStatus = "initiated";

    /// <summary>حالُ الفاتورَةِ بَعدَ إلغائِها كَما يَقولُه مِثالُ رَدِّ «Cancel invoice» في الوَثيقَة (<c>"status": "canceled"</c>) — يَقرَؤُه
    /// <c>GatewayInvoiceCloser</c> فَلا يَحسِبُ ‏200 بِحالٍ أُخرى «أُلغِيَت» (‏2026-10-13، مُراجَعَةُ بَوّابَةِ المَوجَة 3b).</summary>
    public const string CanceledStatus = "canceled";

    /// <summary>حالُ الفاتورَةِ بَعدَ انتِهاءِ مُهلَتِها — مِن مَعجَمِ <c>InvoiceStatus</c> بِحَرفِه، وحَقلُ <c>expired_at</c> في «Create Invoice» يَقولُ أَثَرَه:
    /// «User will be prevented from paying the invoice once expired». يَقرَؤُه التَحَقُّقُ فَيُلغي الطَلَب (‏2026-10-29، قَرارُ المالِك د5 (٤)).</summary>
    public const string ExpiredStatus = "expired";

    /// <summary>أَصغَرُ مَبلَغٍ تَقبَلُه الفاتورَة بِالهَلَلَة — «‏Possible values: &gt;= 100».</summary>
    public const long MinimumAmount = 100;

    /// <summary>حُدودُ <c>metadata</c> كَما تَقولُها الوَثيقَة.</summary>
    public const int MetadataMaxKeys = 30;
    public const int MetadataMaxKeyLength = 40;
    public const int MetadataMaxValueLength = 500;

    /// <summary>خَمسَ عَشرَةَ ثانِيَة — رَقَمُ جيرانِه (<c>PayPalEnvironment</c> و<c>PaddleEnvironment</c> و<c>OtoEnvironment</c>)
    /// لا رَقَمٌ مُختَرَع: ميسّر لا تَنشُرُ مُهلَة.</summary>
    public const int TimeoutSeconds = 15;

    private readonly HttpClient _http;
    private readonly ILogger<MoyasarClient> _log;

    public MoyasarClient(HttpClient http, ILogger<MoyasarClient> log)
    {
        _http = http;
        _log = log;
    }

    // ═══ العَمَلِيَّتان ═══════════════════════════════════════════════════

    /// <summary>
    /// <para><b>فاتورَةٌ مُستَضافَةٌ بِمِفتاحِ المُستَأجِر</b> — <c>POST /invoices</c> بِالمَبلَغِ (بِالهَلَلَة) والعُملَةِ والوَصفِ
    /// ورابِطَي العَودَة، ثُمَّ <c>PUT /invoices/:id</c> بِـ<c>metadata</c> المُسَوَّدَة. ولا تُرَدُّ الفاتورَةُ إلّا وقَد أَخَذَت
    /// <c>metadata</c>ها: فاتورَةٌ بِلا مُعَرِّفاتِ صَفَقاتِها لا تَعبُرُ بَوّابَةَ البُرهانِ أَصلاً، فَرابِطُها لا يُعطى لِمُشتَرٍ.</para>
    ///
    /// <para><b>ومُسَوَّدَةٌ خارِجَ حُدودِ الوَثيقَةِ لا تُرسَل</b> — مَبلَغٌ دونَ <see cref="MinimumAmount"/>، أَو وَصفٌ فارِغ، أَو
    /// <c>metadata</c> فارِغَةٌ أَو فَوقَ حُدودِها: <see cref="MoyasarFailures.Invalid"/> بِلا شَبَكَة.</para>
    /// </summary>
    public async Task<MoyasarResult<MoyasarInvoice>> CreateInvoiceAsync(
        string secretKey, MoyasarInvoiceDraft draft, CancellationToken ct = default)
    {
        const string operation = "create invoice";
        if (string.IsNullOrWhiteSpace(secretKey)) return Fail<MoyasarInvoice>(MoyasarFailures.NotConfigured, operation, null);
        if (!IsSendable(draft)) return Fail<MoyasarInvoice>(MoyasarFailures.Invalid, operation, null);

        var fields = new Dictionary<string, object?>
        {
            ["amount"]      = draft.Amount,
            ["currency"]    = Currency,
            ["description"] = draft.Description,
            ["success_url"] = draft.SuccessUrl,
            ["back_url"]    = draft.BackUrl,
        };
        // وتاريخُ الانتِهاءِ حَقلُ الوَثيقَةِ نَفسِها — يُرسَلُ حينَ تَحمِلُه المُسَوَّدَة (‏2026-10-29، قَرارُ المالِك د5 (٣)؛ تَوصيفُ «اليَوم» صَفُّ
        // `Creating_an_invoice_posts_the_documented_fields…` بِخَمسَةِ حُقولٍ بِلا تاريخ — باقٍ بِحَرفِه لِمُسَوَّدَةٍ بِلا تاريخ).
        if (draft.ExpiresAt is { } expires) fields["expired_at"] = ExpiryText(expires);
        var created = await SendAsync(secretKey, HttpMethod.Post, InvoicesPath, fields, operation, ct);
        if (!created.Ok) return MoyasarResult<MoyasarInvoice>.Fail(created.Failure!);
        // والرَدُّ يُقرَأُ بِشَرطِ أَخَواتِه (‏2026-10-13، مُراجَعَةُ بَوّابَةِ المَوجَة 3b، G5 (xvi-g)؛ تَوصيفُ «اليَوم» `e4a3f14`): الرابِطُ مُطلَقٌ
        // بِـhttp(s) — شَرطُ `GatewayReturn.PayUrlOf` لِلرابِطِ نَفسِه، فَلا يُحَوَّلُ المُشتَري إلى `javascript:` — والمُعَرِّفُ uuid قَبلَ أَن يَدخُلَ مَسارَ
        // الوَسمِ، بِشَرطِ الجَلبِ والإلغاءِ حَرفاً — فَلا يَنقُلُ «/..» نِداءً بِمِفتاحِ المَتجَرِ إلى مَسارٍ آخَر. وما سِواهُما «لا يُقرَأ».
        if (Read(created.Value) is not { } invoice || !IsAbsoluteWeb(invoice.Url)) return Unreadable<MoyasarInvoice>(operation, "url");
        if (!Guid.TryParse(invoice.Id, out var id)) return Unreadable<MoyasarInvoice>(operation, "id");

        var tagged = await SendAsync(secretKey, HttpMethod.Put, InvoicesPath + "/" + id.ToString("D"),
            new Dictionary<string, object?> { ["metadata"] = draft.Metadata }, "update invoice metadata", ct);
        if (!tagged.Ok) return MoyasarResult<MoyasarInvoice>.Fail(tagged.Failure!);

        // ما عادَ مِن التَحديثِ هُوَ الفاتورَةُ كَما هِيَ عِندَ البَوّابَةِ الآن — وإن أَغفَلَ رابِطَها فَالرابِطُ مِن الإنشاء.
        // (‏2026-10-13 — ولا يُعادُ حَرفٌ فَوقَه: «أَغفَلَه» صارَ «أَغفَلَه أَو عادَ بِغَيرِ http(s)» — والرابِطُ الَّذي يُعادُ مَفحوصٌ دائِماً.)
        return Read(tagged.Value) is { } updated
            ? MoyasarResult<MoyasarInvoice>.Success(updated with { Url = IsAbsoluteWeb(updated.Url) ? updated.Url : invoice.Url })
            : Unreadable<MoyasarInvoice>("update invoice metadata", "id");
    }

    /// <summary>
    /// <para><b>الفاتورَةُ كَما هِيَ عِندَ البَوّابَة</b> — <c>GET /invoices/:id</c> بِمِفتاحِ المُستَأجِر. والمُعَرِّفُ
    /// يُفحَصُ قَبلَ أَن يَدخُلَ العُنوان (‏uuid كَما تَقولُ الوَثيقَة)، فَلا يُبنى مَسارٌ مِن نَصٍّ يَحمِلُ «/» أَو «?».</para>
    /// <para><b>وفاتورَةُ حِسابٍ آخَرَ لا تُجلَبُ أَصلاً</b>: المِفتاحُ مِفتاحُ المُستَأجِر، والوَثيقَةُ تُجيبُ ‏404 «One of the record
    /// referenced in the request was not found» — فَتَعودُ <see cref="MoyasarFailures.Refused"/> بِحالِها.</para>
    /// </summary>
    public async Task<MoyasarResult<MoyasarInvoice>> FetchInvoiceAsync(
        string secretKey, string invoiceId, CancellationToken ct = default)
    {
        const string operation = "fetch invoice";
        if (string.IsNullOrWhiteSpace(secretKey)) return Fail<MoyasarInvoice>(MoyasarFailures.NotConfigured, operation, null);
        if (!Guid.TryParse(invoiceId, out var id)) return Fail<MoyasarInvoice>(MoyasarFailures.Invalid, operation, null);

        var res = await SendAsync(secretKey, HttpMethod.Get, InvoicesPath + "/" + id.ToString("D"), null, operation, ct);
        if (!res.Ok) return MoyasarResult<MoyasarInvoice>.Fail(res.Failure!);
        return Read(res.Value) is { } invoice
            ? MoyasarResult<MoyasarInvoice>.Success(invoice)
            : Unreadable<MoyasarInvoice>(operation, "id");
    }

    // ─── والثالِثَة — ‏2026-10-12، مُراجَعَةُ تَيّارِ S9 الثانِيَة ─────────────────────────────────

    /// <summary>
    /// <para><b>إلغاءُ فاتورَةٍ مُستَضافَةٍ فَلا تُدفَع</b> — <c>PUT /invoices/:id/cancel</c> بِلا جِسمٍ وبِمِفتاحِ المُستَأجِر: «Cancel an existing
    /// Moyasar invoice so it can no longer be paid by the customer». والمُعَرِّفُ يُفحَصُ قَبلَ أَن يَدخُلَ العُنوانَ كَالجَلبِ حَرفاً، والرَدُّ ‏200
    /// الفاتورَةُ بِحالِها كَما قالَتها البَوّابَة، وكُلُّ فَشَلٍ رَمزُه مِن المَعجَمِ الخُماسيِّ نَفسِه — لا سادِس.</para>
    /// <para><b>وما لا تَقولُه الوَثيقَة — ويُقال</b>: أَيَّ حالٍ يَقبَلُ الإلغاء، وبِمَ يُجابُ إلغاءُ فاتورَةٍ دُفِعَت أَو أُلغِيَت. فَلا يُناديه
    /// مُستَهلِكُه إلّا عَلى فاتورَةٍ جَلَبَها لِلتَّوِّ بِحالِ <see cref="OpenStatus"/>، وما يُجابُ بِه غَيرُ ذلكَ يُقاسُ في البُرهانِ الحَيّ.</para>
    /// </summary>
    public async Task<MoyasarResult<MoyasarInvoice>> CancelInvoiceAsync(
        string secretKey, string invoiceId, CancellationToken ct = default)
    {
        const string operation = "cancel invoice";
        if (string.IsNullOrWhiteSpace(secretKey)) return Fail<MoyasarInvoice>(MoyasarFailures.NotConfigured, operation, null);
        if (!Guid.TryParse(invoiceId, out var id)) return Fail<MoyasarInvoice>(MoyasarFailures.Invalid, operation, null);

        var res = await SendAsync(secretKey, HttpMethod.Put, InvoicesPath + "/" + id.ToString("D") + CancelSuffix, null, operation, ct);
        if (!res.Ok) return MoyasarResult<MoyasarInvoice>.Fail(res.Failure!);
        return Read(res.Value) is { } invoice
            ? MoyasarResult<MoyasarInvoice>.Success(invoice)
            : Unreadable<MoyasarInvoice>(operation, "id");
    }

    // ═══ المَبلَغ — مِن الرِيالِ إلى أَصغَرِ وَحدَة ════════════════════════════

    /// <summary>
    /// <para><b>تاريخُ الانتِهاءِ بِصيغَةِ الوَثيقَة</b> (‏2026-10-29، قَرارُ المالِك د5 (٣)): جَدوَلُ «Create Invoice» — <c>expired_at</c> «timestamp»
    /// و«ISO 8601 date or datetime» ومِثالُه <c>2018-01-01T09:55:14.000Z</c> — فَيُكتَبُ بِالتَوقيتِ العالَمِيِّ بِأَجزاءِ الثانِيَةِ الثَلاثَةِ وحَرفِ <c>Z</c>،
    /// بِثَقافَةٍ ثابِتَةٍ لا بِثَقافَةِ الخادِم.</para>
    /// </summary>
    public static string ExpiryText(DateTimeOffset at)
        => at.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// <para><b>المَبلَغُ بِالهَلَلَة — بِلا تَقريب</b>: «1.00 SAR = 100». ويُرجِعُ <c>null</c> لِمَبلَغٍ لا يَصيرُ عَدَداً صَحيحاً
    /// مِن الهَلَلات (كَسرٌ أَصغَرُ مِن هَلَلَة) أَو دونَ <see cref="MinimumAmount"/> — فَلا يَأكُلُ أَحَدٌ الهَلَلَةَ الباقِيَةَ صامِتاً
    /// (‏ADR-029)، ولا تُرسَلُ فاتورَةٌ تَرُدُّها البَوّابَة. دالَّةٌ واحِدَةٌ يَقرَؤُها مُنشِئُ الفاتورَةِ وبَوّابَةُ البُرهانِ مَعاً.</para>
    /// </summary>
    public static long? ToSmallestUnit(decimal amountSar)
    {
        var halalas = amountSar * 100m;
        if (halalas != decimal.Truncate(halalas) || halalas < MinimumAmount || halalas > long.MaxValue) return null;
        return (long)halalas;
    }

    /// <summary>أَتَصلُحُ المُسَوَّدَةُ لِلإرسالِ بِحُدودِ الوَثيقَة؟ — دالَّةٌ نَقِيَّة.</summary>
    public static bool IsSendable(MoyasarInvoiceDraft? draft)
        => draft is not null
           && draft.Amount >= MinimumAmount
           && !string.IsNullOrWhiteSpace(draft.Description)
           && IsAbsoluteWeb(draft.SuccessUrl) && IsAbsoluteWeb(draft.BackUrl)
           && draft.Metadata.Count is > 0 and <= MetadataMaxKeys
           && draft.Metadata.All(kv => kv.Key.Length is > 0 and <= MetadataMaxKeyLength
                                       && kv.Value.Length is > 0 and <= MetadataMaxValueLength);

    /// <summary>رابِطُ عَودَةٍ مُطلَقٌ بِـ<c>http</c> أَو <c>https</c> — الوَثيقَةُ تَقولُ «uri» ولا تَشتَرِطُ مُخَطَّطاً؛ وما سِواهُما
    /// (‏<c>javascript:</c>، نِسبِيٌّ، فارِغ) لا يُرسَل.</summary>
    private static bool IsAbsoluteWeb(string? url)
        => Uri.TryCreate(url, UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttps || u.Scheme == Uri.UriSchemeHttp);

    // ═══ النِداءُ الواحِد ═══════════════════════════════════════════════════

    /// <summary>
    /// <para><b>كُلُّ عَمَلِيَّةٍ تَمُرُّ مِن هُنا</b>: رَأسُ <c>Authorization: Basic</c> مِن المِفتاحِ ونُقطَتَين (المِفتاحُ اسمُ
    /// المُستَخدِمِ وكَلِمَةُ السِرِّ فارِغَة)، ثُمَّ تَصنيفُ الرَدّ: ‏5xx أَو انقِطاعٌ أَو مُهلَة ← <see cref="MoyasarFailures.Unreachable"/>؛
    /// ‏4xx ← <see cref="MoyasarFailures.Refused"/> بِحالِه؛ نَجاحٌ لا يُقرَأ ← <see cref="MoyasarFailures.Unreadable"/>.</para>
    /// </summary>
    private async Task<MoyasarResult<JsonElement>> SendAsync(
        string secretKey, HttpMethod method, string path, object? body, string operation, CancellationToken ct)
    {
        using var msg = new HttpRequestMessage(method, BaseUrl + path);
        msg.Headers.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes(secretKey + ":")));
        if (body is not null)
            msg.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(TimeoutSeconds));
        HttpResponseMessage resp;
        try
        {
            resp = await _http.SendAsync(msg, cts.Token);
        }
        catch (HttpRequestException)
        {
            return Fail<JsonElement>(MoyasarFailures.Unreachable, operation, null);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // مُهلَتُنا نَحن انقَضَت — لا إلغاءُ المُنادي (ذاكَ يَصعَدُ كَما هو).
            return Fail<JsonElement>(MoyasarFailures.Unreachable, operation, null);
        }

        using (resp)
        {
            var status = (int)resp.StatusCode;
            if (status >= 500) return Fail<JsonElement>(MoyasarFailures.Unreachable, operation, status);
            if (!resp.IsSuccessStatusCode) return Fail<JsonElement>(MoyasarFailures.Refused, operation, status);

            var root = await ReadJsonAsync(resp, ct);
            return root is { } ok
                ? MoyasarResult<JsonElement>.Success(ok)
                : Fail<JsonElement>(MoyasarFailures.Unreadable, operation, status);
        }
    }

    // ═══ أَدَوات ═══════════════════════════════════════════════════════

    private MoyasarResult<T> Fail<T>(string code, string operation, int? httpStatus)
    {
        // اللوغُ يَحمِلُ العَمَلِيَّةَ والرَمزَ والحالَ — **ولا مِفتاحاً ولا جِسماً ولا عُنواناً**.
        _log.LogWarning("[gateway] {Operation} ← {Code} (HTTP {Status})", operation, code, httpStatus?.ToString() ?? "—");
        return MoyasarResult<T>.Fail(MoyasarFailures.Describe(code, httpStatus));
    }

    private MoyasarResult<T> Unreadable<T>(string operation, string field)
    {
        _log.LogWarning("[gateway] {Operation} ← {Code} (لا «{Field}» في الرَدّ)", operation, MoyasarFailures.Unreadable, field);
        return MoyasarResult<T>.Fail(MoyasarFailures.Describe(MoyasarFailures.Unreadable, null));
    }

    private static async Task<JsonElement?> ReadJsonAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        try
        {
            var text = await resp.Content.ReadAsStringAsync(ct);
            if (string.IsNullOrWhiteSpace(text)) return null;
            using var doc = JsonDocument.Parse(text);
            return doc.RootElement.ValueKind == JsonValueKind.Object ? doc.RootElement.Clone() : null;
        }
        catch (JsonException) { return null; }
    }

    /// <summary>الفاتورَةُ مِن رَدِّها — <c>null</c> إن غابَ مُعَرِّفُها أَو حالُها أَو مَبلَغُها أَو عُملَتُها: لا يُبنى قَرارٌ عَلى
    /// نِصفِ فاتورَة. و<c>metadata</c> قيَمٌ نَصِّيَّةٌ وَحدَها كَما تَقولُ الوَثيقَة؛ وما سِواها يُتَجاوَز.</summary>
    private static MoyasarInvoice? Read(JsonElement root)
    {
        if (Text(root, "id") is not { } id || Text(root, "status") is not { } status || Text(root, "currency") is not { } currency)
            return null;
        if (!root.TryGetProperty("amount", out var a) || a.ValueKind != JsonValueKind.Number || !a.TryGetInt64(out var amount))
            return null;

        var metadata = new Dictionary<string, string>(StringComparer.Ordinal);
        if (root.TryGetProperty("metadata", out var m) && m.ValueKind == JsonValueKind.Object)
            foreach (var p in m.EnumerateObject())
                if (p.Value.ValueKind == JsonValueKind.String && p.Value.GetString() is { } v)
                    metadata[p.Name] = v;

        return new MoyasarInvoice(id, status, amount, currency, Text(root, "url"), metadata);
    }

    private static string? Text(JsonElement root, string name)
        => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(v.GetString())
            ? v.GetString()!.Trim()
            : null;
}

// ═══ الأَشكال — ما يُرسَل وما يَعود ═══════════════════════════════════════

/// <summary>مُسَوَّدَةُ فاتورَة — <see cref="Amount"/> بِالهَلَلَة (<see cref="MoyasarClient.ToSmallestUnit"/>)، ورابِطا العَودَة
/// مُطلَقان، و<see cref="Metadata"/> ما يُلحَقُ بِها بَعدَ الإنشاء.</summary>
/// <remarks>(‏2026-10-29، قَرارُ المالِك د5 (٣) — ولا يُعادُ حَرفٌ فَوقَه: و<see cref="ExpiresAt"/> تاريخُ انتِهائِها — حَقلُ <c>expired_at</c> في «Create Invoice»
/// («User will be prevented from paying the invoice once expired»)؛ و<c>null</c> لا يُرسِلُه، كَما كانَت المُسَوَّدَةُ قَبلَه.)</remarks>
public sealed record MoyasarInvoiceDraft(
    long Amount, string Description, string SuccessUrl, string BackUrl, IReadOnlyDictionary<string, string> Metadata,
    DateTimeOffset? ExpiresAt = null);

/// <summary>الفاتورَةُ كَما قالَتها البَوّابَة — <see cref="Amount"/> بِالهَلَلَة، و<see cref="Url"/> صَفحَةُ الدَفعِ المُستَضافَة.
/// <b>ولا يُطبَعُ رابِطُها</b>: <c>ToString</c> بِلا رابِطٍ يَملِكُه مَن يَملِكُ السَطر.</summary>
public sealed record MoyasarInvoice(
    string Id, string Status, long Amount, string Currency, string? Url, IReadOnlyDictionary<string, string> Metadata)
{
    public bool IsPaid => string.Equals(Status, MoyasarClient.PaidStatus, StringComparison.Ordinal);

    public override string ToString() => $"MoyasarInvoice(id={Id}, status={Status}, amount={Amount} {Currency})";
}

/// <summary>فَشَلٌ مُسَمّى — <c>Code</c> مِن <see cref="MoyasarFailures"/> لِلاختِبارِ واللوغِ والقامُوس، و<c>MessageAr</c>
/// لِلمُراجِعِ البَشَرِيّ؛ وحالُ HTTP حَيثُ يوجَد. نَسَقُ <c>XViolation(Code, MessageAr)</c> في القاعِدَة ٤.</summary>
public sealed record MoyasarFailure(string Code, string MessageAr, int? HttpStatus = null);

/// <summary>نَتيجَةُ عَمَلِيَّة: قيمَةٌ أَو فَشَلٌ مُسَمّى — لا استِثناء.</summary>
public sealed record MoyasarResult<T>(T? Value, MoyasarFailure? Failure)
{
    public bool Ok => Failure is null;
    public static MoyasarResult<T> Success(T value) => new(value, null);
    public static MoyasarResult<T> Fail(MoyasarFailure failure) => new(default, failure);
}

/// <summary>
/// <para><b>مَعجَمُ فَشَلِ العَميلِ — خَمسَةٌ لا سادِسَ لَها.</b> كُلٌّ بِمُوجَبٍ وسالِبٍ في <c>MoyasarClientTests</c>.</para>
/// </summary>
public static class MoyasarFailures
{
    /// <summary>لا مِفتاحَ يُمَرَّر — فَلا نِداءَ أَصلاً.</summary>
    public const string NotConfigured = "gateway_not_configured";

    /// <summary>مُدخَلٌ خارِجَ حُدودِ الوَثيقَة (مَبلَغٌ دونَ الأَدنى، وَصفٌ فارِغ، <c>metadata</c> فارِغَةٌ أَو فَوقَ حَدِّها، رابِطُ عَودَةٍ
    /// لَيسَ مُطلَقاً، مُعَرِّفُ فاتورَةٍ لَيسَ uuid) — فَلا نِداءَ أَصلاً.</summary>
    public const string Invalid = "gateway_invalid_request";

    /// <summary>لا جَواب: انقِطاعٌ، أَو مُهلَة، أَو ‏5xx.</summary>
    public const string Unreachable = "gateway_unreachable";

    /// <summary>أَجابَت البَوّابَةُ بِالرَفض: ‏4xx — مِفتاحٌ مَرفوض (‏401/403) أَو فاتورَةٌ لا تَعرِفُها لِهذا المِفتاح (‏404) أَو مُدخَلٌ (‏400).</summary>
    public const string Refused = "gateway_refused";

    /// <summary>أَجابَت بِنَجاحٍ لا يُقرَأ — جِسمٌ لَيسَ JSON أَو بِلا حَقلٍ مَطلوب.</summary>
    public const string Unreadable = "gateway_unreadable";

    public static readonly IReadOnlyList<string> All =
        new[] { NotConfigured, Invalid, Unreachable, Refused, Unreadable };

    public static bool Contains(string code) => All.Contains(code, StringComparer.Ordinal);

    /// <summary>الفَشَلُ بِرِسالَتِه لِلمُراجِع — ولا سِرَّ فيها ولا جِسم.</summary>
    public static MoyasarFailure Describe(string code, int? httpStatus) => code switch
    {
        NotConfigured => new(code, "لا مِفتاحَ بَوّابَةٍ يُمَرَّرُ لِهذا المَتجَر — فَلا نِداء.", httpStatus),
        Invalid       => new(code, "مُدخَلُ الفاتورَةِ خارِجَ حُدودِ وَثيقَةِ البَوّابَة — لَم يُرسَل.", httpStatus),
        Unreachable   => new(code, "لا جَوابَ مِن البَوّابَة — انقِطاعٌ أَو مُهلَةٌ أَو خَطَأُ خادِم.", httpStatus),
        Refused       => new(code, "رَفَضَت البَوّابَةُ الطَلَب — مِفتاحٌ مَرفوضٌ أَو فاتورَةٌ لا تَعرِفُها لِهذا المِفتاح.", httpStatus),
        Unreadable    => new(code, "رَدُّ البَوّابَةِ ناجِحٌ لا يُقرَأ — حَقلٌ مَطلوبٌ غائِب.", httpStatus),
        _ => throw new ArgumentException($"الرَمز «{code}» خارِج مَعجَم MoyasarFailures.", nameof(code)),
    };
}

public static class MoyasarClientExtensions
{
    /// <summary>
    /// <para><b>يُسَجِّلُ عَميلَ البَوّابَةِ بِـ<c>AddHttpClient</c> — دائِماً</b>، حُجَّةُ <c>AddOtoShipping</c> بِحَرفِها: العَميلُ
    /// لا يَحمِلُ سِرّاً (مِفتاحُ المُستَأجِرِ يُفَكُّ مِن الخِزانَةِ ويُمَرَّرُ إلى كُلِّ نِداء)، فَلا تَهيئَةَ تُحسَمُ هُنا. وبابُ
    /// البِطاقَةِ لا يُرسَمُ إلّا حَيثُ يُحَلُّ مِفتاحٌ مَختومٌ وتَفُكُّه الخِزانَة (القاعِدَة ١٢).</para>
    /// <para>سَقفُ <c>HttpClient.Timeout</c> ضِعفُ مُهلَتِنا الداخِلِيَّة، لِتَسبِقَ مُهلَتُنا دائِماً فَيَثبُتَ رَمزُ الفَشَلِ
    /// بَدَلَ أَن يَتَبَدَّلَ بِسِباق — حُجَّةُ جيرانِه.</para>
    /// </summary>
    public static IServiceCollection AddMoyasarGateway(this IServiceCollection services)
    {
        // ‏`RedactLoggedHeaders`: لوغُ مَصنَعِ العُمَلاءِ يَطبَعُ رُؤوسَ الطَلَبِ في مُستَوى التَتَبُّع — ورَأسُ المُصادَقَةِ هُنا المِفتاحُ
        // السِرِّيُّ بِـbase64 لا غَير. فَتُحجَبُ قيَمُ الرُؤوسِ كُلِّها صَراحَةً، لا اعتِماداً عَلى افتِراضِ نُسخَةٍ مِن الإطار.
        services.AddHttpClient<MoyasarClient>(client => client.Timeout = TimeSpan.FromSeconds(MoyasarClient.TimeoutSeconds * 2))
                .RedactLoggedHeaders(_ => true);
        return services;
    }
}
