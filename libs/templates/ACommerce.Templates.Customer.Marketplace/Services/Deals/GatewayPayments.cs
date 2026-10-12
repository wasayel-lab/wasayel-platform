using System.Security.Cryptography;
using ACommerce.Kit.Payments;
using ACommerce.Platform.Providers;
using ACommerce.Templates.Customer.Marketplace.Services.Providers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ACommerce.Templates.Customer.Marketplace.Services.Deals;

// ═══ التَحَقُّقُ مِن دَفعِ المُشتَري عِندَ بَوّابَةِ المَتجَر — ما وَراءَ زِرِّ صَفحَةِ العَودَة (‏ADR-032 §٢‑هـ الخُطوَة 9) ═══════════
//
// المُشتَري يَدفَعُ عَلى صَفحَةِ البَوّابَةِ المُستَضافَةِ ويَعودُ إلى `/{slug}/pay/return` — صَفحَةٌ **تَقرَأُ ولا تَكتُب** — فَيَضغَطُ زِرّاً
// واحِداً يُرسِلُ `POST /{slug}/payments/verify`. وهذِه الخِدمَةُ ما وَراءَ الزِرّ: تَسأَلُ أَهذِه صَفقَةُ صاحِبِ الجَلسَةِ وعَلَيها فاتورَة
// (`DealPayAuthorization` النَقِيَّة) **قَبلَ أَيِّ نِداءٍ خارِجيّ**، ثُمَّ تَفُكُّ مِفتاحَ المَتجَرِ مِن الخِزانَةِ لِهذا النِداءِ وَحدَه، وتَجلِبُ
// الفاتورَةَ بِه، وتَعرِضُها مَعَ صَفَقاتِها عَلى `GatewayPaymentProof.Gate`. **ولا تَكتُبُ شَيئاً في هذِه الخُطوَة**: كاتِبُ «مَدفوع» عَلى
// الصَفقَةِ خُطوَةُ ‏ADR-032 العاشِرَة (`ConfirmGatewayPaymentAsync`) ويَطلُبُ في تَوقيعِه ما تُخرِجُه البَوّابَة (`ProvenGatewayPayment`).
//
// **وجاءَت العاشِرَةُ (‏2026-10-12، تَيّارُ S9 — ولا يُعادُ حَرفٌ فَوقَ ما سَبَق)**: الدَفعُ الثابِتُ يُكتَبُ عَلى صَفَقاتِه بِـ
// `DealsService.ConfirmGatewayPaymentAsync`، وما تَقِفُ مِنها قَبلَ «دَفع» تَدخُلُها بِفاعِلِها — المُشتَري نَفسُه، بِبَوّابَةِ `AdvanceAsync` لا
// حَولَها. وإتمامُ الشِراءِ بِالبِطاقَةِ يَفتَحُ الفاتورَةَ الواحِدَةَ هُنا (`OpenInvoiceAsync`)، وما لَم يُدفَع لا يَدخُلُ «دَفع» (`AwaitsPayment`).
//
// **والعَودَةُ لَيسَت بُرهاناً، والزِرُّ لَيسَ بُرهاناً**: يَضغَطُه مَن دَفَعَ ومَن لَم يَدفَع — والبُرهانُ جَوابُ البَوّابَةِ بِمِفتاحِ المَتجَر.

/// <summary>
/// <para><b>مَن يَدفَعُ — سِجِلُّ سُلطَةٍ مِن الجَلسَة</b> (القاعِدَة ٦): يَبنيه سَطرُ النُقطَةِ مِن صاحِبِ الجَلسَةِ <b>قَبلَ أَن يُقرَأَ
/// حَقل</b>، ويَطلُبُه تَوقيعُ <see cref="GatewayPayments.VerifyAsync"/> — والقَرارُ «أَهذِه صَفقَتُه؟» في
/// <see cref="DealPayAuthorization.Validate"/> النَقِيَّة. نَسَقُ <c>DealShipper</c> و<c>DealAcceptor</c>.</para>
/// </summary>
public sealed record DealPayer(Guid ActorId);

/// <summary>خَرقٌ في سُلطَةِ الدافِع — <c>Code</c> مِن <see cref="DealPayAuthorization.All"/>.</summary>
public sealed record DealPayViolation(string Code, string MessageAr);

/// <summary>
/// <para><b>أَيَتَحَقَّقُ هذا الدافِعُ مِن دَفعِ هذِه الصَفقَة؟</b> — دالَّةٌ نَقِيَّةٌ تَقرَؤُها صَفحَةُ العَودَةِ لِتَرسُمَ الزِرّ،
/// والخِدمَةُ قَبلَ أَن تَفُكَّ مِفتاحاً أَو تُنادي البَوّابَة: فَما يُرسَمُ هُوَ ما يُقبَل.</para>
/// </summary>
public static class DealPayAuthorization
{
    /// <summary>المُشتَرَكُ مَعَ القَبولِ بِحَرفِه — مِفتاحُ نَصٍّ واحِدٌ في كُلِّ مُعجَم.</summary>
    public const string DealNotFound = DealAcceptAuthorization.DealNotFound;

    /// <summary>الصَفقَةُ لَيسَت طَلَبَ صاحِبِ الجَلسَة — مُبادِرُها غَيرُه. (لا يُعادُ <c>actor_not_party</c>: نَصُّه في الإلغاء.)</summary>
    public const string ActorNotPayer = "actor_not_payer";

    /// <summary>لا فاتورَةَ بَوّابَةٍ مُسَجَّلَةً عَلى الصَفقَة (<see cref="GatewayPaymentProof.InvoiceRef"/>) — لَم تُدفَع عِندَ البَوّابَةِ أَصلاً.</summary>
    public const string InvoiceMissing = "gateway_invoice_missing";

    /// <summary>المَعجَمُ المُغلَق — ثَلاثَةٌ لا رابِعَ لَها.</summary>
    public static readonly IReadOnlyList<string> All = new[] { DealNotFound, ActorNotPayer, InvoiceMissing };

    /// <summary><c>null</c> يَعني «تَحَقَّق». ولا يَسأَلُ عَن حالِ الصَفقَة: دَفعٌ وَقَعَ عَلى صَفقَةٍ أُلغِيَت بَعدَه يُثبَتُ ولا يُبتلَع.</summary>
    public static DealPayViolation? Validate(Deal? deal, DealPayer by)
    {
        if (deal is null)
            return new(DealNotFound, "لا صَفقَةَ بِهذا المُعَرِّف.");

        if (deal.InitiatorId != by.ActorId)
            return new(ActorNotPayer, "الصَفقَةُ لَيسَت طَلَبَ صاحِبِ الجَلسَة — لا يَتَحَقَّقُ مِن دَفعِها غَيرُ مُشتَريها.");

        if (!deal.Refs.TryGetValue(GatewayPaymentProof.InvoiceRef, out var invoice) || string.IsNullOrWhiteSpace(invoice))
            return new(InvoiceMissing, "لا فاتورَةَ بَوّابَةٍ مُسَجَّلَةً عَلى الصَفقَة — لَم تُدفَع عِندَ بَوّابَةِ المَتجَر.");

        return null;
    }

    public static bool IsAllowed(Deal? deal, DealPayer by) => Validate(deal, by) is null;
}

/// <summary>نَتيجَةُ التَحَقُّق: دَفعٌ ثَبَتَ (<see cref="Payment"/>)، أَو رَمزٌ مِن <see cref="GatewayReturn.Codes"/> — ولا ثالِث.
/// و<see cref="DealId"/> الصَفقَةُ المَرجِعُ حَيثُ وُجِدَت، فَيَعودُ إلَيها الرَدّ.</summary>
public sealed record GatewayVerifyResult(Guid? DealId, ProvenGatewayPayment? Payment, string? Code)
{
    public bool Ok => Payment is not null;

    /// <summary>الصَفَقاتُ الَّتي كُتِبَ عَلَيها الدَفعُ <b>في هذا التَحَقُّق</b> — فَيُشعَرُ بائِعوها مَرَّة (‏ADR-032 الخُطوَة 10). وفارِغَةٌ
    /// لِلرَفضِ ولِلتَحَقُّقِ الثاني مِن الدَفعِ نَفسِه.</summary>
    public IReadOnlyList<Guid> Confirmed { get; init; } = Array.Empty<Guid>();
}

/// <summary>نَتيجَةُ فَتحِ الفاتورَةِ في إتمامِ الشِراء: رابِطُ الصَفحَةِ المُستَضافَةِ أَو رَمزٌ مِن <see cref="MoyasarFailures"/> — ولا ثالِث.</summary>
public sealed record GatewayCheckout(string? Url, string? Code)
{
    public bool Ok => Url is not null;
}

/// <summary>
/// <para><b>عَقدُ صَفحَةِ العَودَةِ — مَوضِعٌ واحِدٌ يَقرَؤُه كاتِبُ الرابِطِ والصَفحَةُ والنُقطَة</b>: اسمُ المُعامِلِ الَّذي يَحمِلُ
/// الصَفقَةَ المَرجِع، وقِراءَتُه، ومَسارُ الرَدِّ بَعدَ التَحَقُّق، ومَعجَمُ رُموزِ الشَريطِ ومَفاتيحُها في القامُوس (نَسَقُ
/// <c>DealRefusalTexts</c>: الرَمزُ مِن الجَدوَلِ والكَلامُ في المَعجَم — القاعِدَة ١١).</para>
/// </summary>
public static class GatewayReturn
{
    /// <summary>مَسارُ صَفحَةِ العَودَةِ داخِلَ المَتجَر — بِلا شَرطَةٍ أُولى، كَما يَبنيه <c>AuthSession.LinkFor</c>.</summary>
    public const string ReturnPath = "pay/return";

    /// <summary>اسمُ المُعامِلِ الَّذي يَحمِلُ الصَفقَةَ المَرجِع — في رابِطِ العَودَةِ وفي حَقلِ استِمارَةِ التَحَقُّقِ مَعاً.</summary>
    public const string DealKey = "deal";

    /// <summary>
    /// <para><b>الصَفقَةُ المَرجِعُ مِن قيمَةِ المُعامِل</b> — المُعَرِّفُ بِصيغَةِ <c>N</c> (‏32 مِحرَفاً) <b>في رَأسِ القيمَة</b>، وما بَعدَه
    /// يُتَجاوَز. والسَبَبُ مَقروءٌ لا مُتَوَهَّم: وَثيقَةُ «Create Invoice» (مَقروءَةً ‏2026-10-12) تَقولُ إنّ <c>success_url</c> «An endpoint
    /// where the payer will be redirected to when the invoice is paid» و<c>back_url</c> «used for redirect when the user clicks on the
    /// back button» — <b>ولا تَقولُ أَتُلحِقُ البَوّابَةُ بِالرابِطِ مُعامِلاتٍ أَم لا، ولا كَيف</b>. فَإلحاقٌ يَلصَقُ «?…» بِقيمَتِنا لا
    /// يُضَيِّعُ عَلى مُشتَرٍ دَفَعَ صَفقَتَه. ولا يُمنَحُ بِهذا شَيء: الصَفقَةُ تُحَمَّلُ ثُمَّ تُسأَلُ <see cref="DealPayAuthorization"/>.</para>
    /// </summary>
    public static Guid? DealIdOf(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return null;
        var head = raw.Length > 32 ? raw[..32] : raw;
        return Guid.TryParseExact(head, "N", out var id) ? id : null;
    }

    /// <summary>مَسارُ صَفحَةِ العَودَةِ بِصَفقَتِها ورَمزِها — والرَمزُ مِن مَعجَمٍ مُغلَقٍ بِحُروفٍ لا تَحتاجُ تَرميزاً.</summary>
    public static string PathFor(Guid? dealId, string? code)
    {
        var query = new List<string>(2);
        if (dealId is { } id) query.Add($"{DealKey}={id:N}");
        if (!string.IsNullOrEmpty(code)) query.Add($"err={code}");
        return query.Count == 0 ? ReturnPath : ReturnPath + "?" + string.Join('&', query);
    }

    /// <summary>
    /// <para><b>أَينَ يَعودُ الزِرّ</b>: دَفعٌ ثَبَتَ ← صَفحَةُ الصَفقَة؛ وما سِواه ← صَفحَةُ العَودَةِ بِرَمزِه، فَيُقرَأُ الرَفضُ ولا يُبتلَع.</para>
    /// </summary>
    public static string AfterVerify(GatewayVerifyResult r)
        => r.Ok && r.DealId is { } id ? $"deals/{id}" : PathFor(r.DealId, r.Code);

    /// <summary>
    /// <para><b>أَثَبَتَ دَفعُ هذِه الصَفقَةِ عِندَ البَوّابَةِ وكُتِب؟</b> (‏ADR-032 الخُطوَة 10، ‏2026-10-12) — «Captured» ومَرجِعُ الدَفعِ
    /// فاتورَتُها. تَقرَؤُه صَفحَةُ العَودَةِ فَتَقولُ «ثَبَتَ دَفعُك» بَدَلَ زِرِّ التَحَقُّق — قِراءَةٌ لِما كَتَبَه التَأكيد، لا سُؤالٌ لِلبَوّابَة.</para>
    /// </summary>
    public static bool IsConfirmed(Deal deal) => DealsService.IsCaptured(deal) && GatewayPayments.CollectedByGateway(deal);

    /// <summary>
    /// <para><b>رابِطُ صَفحَةِ الدَفعِ المُستَضافَةِ لِهذِه الصَفقَة</b> — كَما كَتَبَه إتمامُ الشِراء (<see cref="GatewayPayments.InvoiceUrlRef"/>)،
    /// و<c>null</c> لِما لَيسَ رابِطاً مُطلَقاً بِـ<c>https</c> أَو <c>http</c> (مِثالُ الوَثيقَةِ نَفسُه <c>http://checkout.moyasar.com/…</c>):
    /// فَلا يُرسَمُ <c>javascript:</c> ولا رابِطٌ نِسبِيٌّ في «أَكمِل الدَّفع».</para>
    /// </summary>
    public static string? PayUrlOf(Deal deal)
        => deal.Refs.TryGetValue(GatewayPayments.InvoiceUrlRef, out var url)
           && Uri.TryCreate(url, UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttps || u.Scheme == Uri.UriSchemeHttp)
            ? url
            : null;

    /// <summary>بادِئَةُ مَفاتيحِ الشَريطِ — اصطِلاحُ القامُوسِ ثَلاثَةُ مَقاطِع (<c>LocaleValidator.KeyPattern</c>)، فَالرَمزُ في الثالِث.</summary>
    public const string TextPrefix = "pay.return.err_";

    /// <summary>كُلُّ رَمزٍ قَد يَبلُغُ الصَفحَةَ — سُلطَةُ الدافِع، ثُمَّ مَعجَمُ فَشَلِ العَميلِ الخُماسيّ، ثُمَّ رُموزُ بَوّابَةِ البُرهانِ
    /// الخَمسَة. ثَلاثَةَ عَشَرَ لا رابِعَ عَشَر.</summary>
    public static readonly IReadOnlyList<string> Codes =
        DealPayAuthorization.All
            .Concat(MoyasarFailures.All)
            .Concat(GatewayPaymentProof.Codes)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    /// <summary>مِفتاحُ النَصِّ لِرَمزٍ مَعروف، و<c>null</c> لِما سِواه — فَلا يُرسَمُ شَريطٌ لِرَمزٍ لا نَصَّ لَه، ولا مِفتاحٌ خام.</summary>
    public static string? KeyFor(string? code)
        => code is not null && Codes.Contains(code, StringComparer.Ordinal) ? TextPrefix + code : null;
}

/// <summary>
/// <para><b>الدَفعُ عِندَ بَوّابَةِ المَتجَر — مِن جِهَةِ الصَفقَة</b>. تَقرَأُ صَفقَةً، وتَفُكُّ مِفتاحَ المَتجَرِ لِلنِداءِ الواحِد،
/// وتَسأَلُ البَوّابَة. <b>ولا تَكتُبُ في هذِه الخُطوَة</b> — وصِفرُ كِتابَةٍ مَقيسٌ في <c>TenantGatewayEndpointsTests</c>.</para>
/// </summary>
public sealed class GatewayPayments
{
    // ─── مَراجِعُ الصَفقَةِ ورَمزُ الحارِس — الكاتِبُ والقارِئُ حَرفٌ واحِد (‏ADR-032 الخُطوَة 10، ‏2026-10-12) ─────────

    /// <summary>رابِطُ صَفحَةِ الدَفعِ المُستَضافَةِ عَلى الصَفقَة — يَكتُبُه إتمامُ الشِراءِ مَعَ <see cref="GatewayPaymentProof.InvoiceRef"/>،
    /// وتَقرَؤُه صَفحَةُ العَودَةِ فَتَرسُمُ «أَكمِل الدَّفع» لِمَن لَم يَدفَع بَعد.</summary>
    public const string InvoiceUrlRef = "gateway_invoice_url";

    /// <summary>
    /// <para><b>عَلى الصَفقَةِ فاتورَةُ بَوّابَةٍ لَم يَثبُت دَفعُها، وتاليها «دَفع»</b> — رَمزُ <see cref="DealsService.AdvanceAsync"/> حينَ
    /// تَصدُقُ <see cref="AwaitsPayment"/>. يَبلُغُه زِرُّ «تَأكيد: دَفع» (<c>/advance</c> ← <c>?err=</c>) والـAPI (‏409)؛ والصَفحَةُ لا تَرسُمُ
    /// ذلكَ الزِرَّ لَها — بابُها «أَكمِل الدَّفع». نَسَقُ <see cref="DealShipping.DealAwaitsCarrier"/>.</para>
    /// </summary>
    public const string DealAwaitsPayment = "deal_awaits_payment";

    /// <summary>أَعَلى الصَفقَةِ فاتورَةُ بَوّابَةٍ سَجَّلَها إتمامُ الشِراء؟</summary>
    public static bool HasInvoice(Deal deal)
        => deal.Refs.TryGetValue(GatewayPaymentProof.InvoiceRef, out var invoice) && !string.IsNullOrWhiteSpace(invoice);

    /// <summary>
    /// <para><b>أَتَنتَظِرُ الصَفقَةُ دَفعَ فاتورَتِها؟</b> — تاليها «دَفع»، وعَلَيها فاتورَةُ بَوّابَة، ولَم يُكتَب دَفعُها
    /// (<see cref="DealsService.IsCaptured"/>). دالَّةٌ نَقِيَّةٌ تَقرَؤُها <see cref="DealsService.AdvanceAsync"/> قَبلَ الكِتابَة،
    /// وصَفحَةُ الصَفقَةِ فَتَرسُمُ «أَكمِل الدَّفع» مَكانَ «تَأكيد: دَفع» — فَما يُرسَمُ هُوَ ما يُقبَل.</para>
    /// <para><b>ولِماذا لا يَكفي حارِسُ القَبض</b>: القَبضُ عِندَ «دَفع» يَتَجاوَزُ صَفقَةً بِلا <c>payment_id</c> ويَمضي الانتِقال — فَصَفقَةُ
    /// بِطاقَةٍ لَم تُدفَع فاتورَتُها كانَت تَدخُلُ «دَفع» ثُمَّ «شَحن» بِلا مال.</para>
    /// </summary>
    public static bool AwaitsPayment(Deal deal)
        => HasInvoice(deal) && DealsPolicy.Next(deal) == DealStage.Paid && !DealsService.IsCaptured(deal);

    /// <summary>
    /// <para><b>أَقَبَضَت بَوّابَةُ المَتجَرِ ثَمَنَ هذِه الصَفقَة؟</b> — مَرجِعُ الدَفعِ هُوَ الفاتورَةُ المُسَجَّلَةُ عَلَيها (يَكتُبُهُما التَأكيد).
    /// يَقرَؤُه الإلغاءُ فَلا يُنادي مُزَوِّدَ النُسخَةِ بِاستِرجاعِ مالٍ في حِسابِ المَتجَر.</para>
    /// </summary>
    public static bool CollectedByGateway(Deal deal)
        => HasInvoice(deal)
           && deal.Refs.TryGetValue("payment_id", out var paymentId)
           && string.Equals(paymentId, deal.Refs[GatewayPaymentProof.InvoiceRef], StringComparison.OrdinalIgnoreCase);

    private readonly DealsService _deals;
    private readonly TenantProviderService _providers;
    private readonly ProviderVault _vault;
    /// <summary>عَميلُ البَوّابَة — و<c>null</c> حينَ لا يُسَجِّلُه المُضيف (‏ADR-032 الخُطوَة 8، ‏2026-10-12): مُدخَلُ «(ج) العَميلُ مُسَجَّل»
    /// في <see cref="CardFactsAsync"/> مَقيساً مِن الحَقنِ نَفسِه لا مِن دَعوى؛ وبِدونِه لا فاتورَةَ ولا جَلبَ — <see cref="MoyasarFailures.NotConfigured"/>.</summary>
    private readonly MoyasarClient? _client;
    private readonly ILogger<GatewayPayments> _log;

    public GatewayPayments(DealsService deals, TenantProviderService providers, ProviderVault vault, MoyasarClient? client = null,
        ILogger<GatewayPayments>? log = null)
    {
        _deals = deals;
        _providers = providers;
        _vault = vault;
        _client = client;
        _log = log ?? NullLogger<GatewayPayments>.Instance;
    }

    /// <summary>
    /// <para><b>حَقلُ المِفتاحِ المَختومِ في رَبطِ الدَفع</b> — الحَقلُ الَّذي يُعلِنُه التَعريفُ بِنَوعِ <c>secret_key</c> وقيمَتُه مَختومَة؛
    /// و<c>null</c> لِرَبطٍ غائِبٍ أَو لِقُدرَةٍ أُخرى أَو بِلا مِفتاحٍ مَختوم. <b>بِالنَوعِ لا بِسلاجِ المُزَوِّد</b> — الكاتالوجُ بَيانات،
    /// و<c>Exactly_one_payments_definition_carries_a_sealed_secret_key_field</c> يَحمَرُّ يَومَ يَصيرُ فيه ثانٍ.</para>
    /// <para><b>وصارَ تَعريفُه في النَواة</b> (‏ADR-032 الخُطوَة 8، ‏2026-10-12 — ولا يُعادُ حَرفٌ فَوقَ ما سَبَق): <c>ResolvedProvider.SealedSecretKeyField</c>
    /// بِحَرفِ ما كانَ هُنا — يَقرَؤُه <c>CollectsMoney</c> مُنذُ قُلِب، فَلا يَفتَرِقُ «يَقبِضُ بِالمِفتاح» عَمّا يُفَكُّ لِلنِداء (القاعِدَة ٨).</para>
    /// </summary>
    public static ProviderFieldDefinition? SealedKeyFieldOf(ResolvedProvider? payments)
        => payments?.SealedSecretKeyField;

    /// <summary>
    /// <para><b>التَحَقُّقُ مِن دَفعِ صَفقَةٍ عِندَ البَوّابَة — بِالتَرتيب، ولا يُقلَب</b>:</para>
    /// <list type="number">
    ///   <item><b>السُلطَةُ أَوَّلاً</b>: الصَفقَةُ المَرجِعُ صَفقَةُ <paramref name="by"/> وعَلَيها فاتورَة — وإلّا رَمزُها، <b>بِلا فَكِّ
    ///   مِفتاحٍ ولا نِداء</b>.</item>
    ///   <item>مِفتاحُ المَتجَرِ يُفَكُّ لِهذا النِداءِ وَحدَه — وغِيابُه (لا رَبط، لا خِزانَة، ظَرفٌ لا يُفتَحُ بِسِياقِه) <see cref="MoyasarFailures.NotConfigured"/>.</item>
    ///   <item>الفاتورَةُ تُجلَبُ بِه بِمُعَرِّفِها المُسَجَّلِ عَلى الصَفقَة — وفَشَلُ الجَلبِ رَمزُه مِن مَعجَمِ العَميل.</item>
    ///   <item>والصَفَقاتُ الَّتي سَمَّتها بَياناتُ الفاتورَةِ — مِن صَفَقاتِ هذا الدافِعِ وَحدَه، ومَعَها المَرجِع — تُعرَضُ عَلى
    ///   <see cref="GatewayPaymentProof.Gate"/>: فاتورَةٌ تُسَمّي صَفقَةَ غَيرِه أَو لا تُسَمّي المَرجِعَ لا تَعبُر.</item>
    /// </list>
    /// <para><b>ولا يُمَرَّرُ إلغاءُ الطَلَبِ مِن النُقطَة</b> (نَسَقُ <c>/ship</c>): مُهلَةُ العَميلِ تَحُدُّ النِداء.</para>
    /// </summary>
    public async Task<GatewayVerifyResult> VerifyAsync(
        string tenantSlug, DealPayer by, string? dealRef, CancellationToken ct = default)
    {
        var deal = GatewayReturn.DealIdOf(dealRef) is { } id ? await _deals.LoadAsync(tenantSlug, id, ct) : null;
        if (DealPayAuthorization.Validate(deal, by) is { } refusal)
            return Logged(tenantSlug, new(deal?.Id, null, refusal.Code));

        if (_client is null || await SecretKeyAsync(tenantSlug, ct) is not { } key)
            return Logged(tenantSlug, new(deal!.Id, null, MoyasarFailures.NotConfigured));

        var fetched = await _client.FetchInvoiceAsync(key, deal!.Refs[GatewayPaymentProof.InvoiceRef], ct);
        if (!fetched.Ok)
            return Logged(tenantSlug, new(deal.Id, null, fetched.Failure!.Code));

        var invoice = fetched.Value!;
        var admitted = GatewayPaymentProof.Gate(invoice, await InvoiceDealsAsync(tenantSlug, deal, invoice, by, ct));
        if (admitted.Payment is not { } proven)
            return Logged(tenantSlug, new(deal.Id, null, admitted.Violation!.Code));

        // ─── الخُطوَة 10: الدَفعُ الثابِتُ يُكتَبُ، وما يَقِفُ قَبلَ «دَفع» يَدخُلُها بِفاعِلِها ─────────────────────────
        // الكاتِبُ يَطلُبُ ما أَخرَجَته البَوّابَةُ في تَوقيعِه، ولا يَكتُبُ عَلى ما كُتِب. ثُمَّ صَفقَةٌ تاليها «دَفع» وقَد قُبِضَت تَدخُلُها
        // **بِبَوّابَةِ الانتِقالِ نَفسِها** والمُشتَري فاعِلُها — قاعِدَةُ الكاتالوجِ (`Paid` = المُبادِر)، لا تَجاوُزٌ لِفَحصِ الفاعِل؛ وما
        // قَبلَ ذلكَ يَبقى حَتّى يَبلُغَها ويُدخِلَها المُشتَري بِزِرِّه (والقَبضُ يَتَجاوَزُها: «Captured»). ورَدُّ الانتِقالِ لا يَكسِرُ التَأكيد.
        var confirmed = await _deals.ConfirmGatewayPaymentAsync(tenantSlug, proven, ct);
        foreach (var paidId in proven.DealIds)
            if (await _deals.LoadAsync(tenantSlug, paidId, ct) is { Status: DealStatus.Active } d
                && DealsPolicy.Next(d) == DealStage.Paid && DealsService.IsCaptured(d))
                await _deals.AdvanceAsync(tenantSlug, d.Id, by.ActorId, d.InitiatorName, null, ct, from: d.Stage);

        return Logged(tenantSlug, new GatewayVerifyResult(deal.Id, proven, null) { Confirmed = confirmed.Select(c => c.Id).ToArray() });
    }

    /// <summary>
    /// <para><b>مُدخَلاتُ قَرارِ البِطاقَة — تُحسَبُ هُنا مَرَّةً، ويَقرَؤُها طَرَفا إتمامِ الشِراء</b> (‏ADR-032 الخُطوَة 8 — شَطرُ <c>CardOffered</c>،
    /// تَيّارُ S9، ‏2026-10-12؛ القاعِدَة ٧): الشاشَةُ (<c>CheckoutPage.razor</c>) والنُقطَةُ (<c>/checkout/submit</c>) تُنادِيانِها بِالمُدخَلَينِ
    /// القائِمَين — عَلَمِ المَتجَرِ وقُدرَةِ مُزَوِّدِ النُسخَة — وتُضيفُ هي الثَلاثَةَ الَّتي تَخُصُّ بَوّابَةَ المَتجَر: <b>(أ)</b> رَبطُ دَفعٍ بِمِفتاحٍ
    /// سِرِّيٍّ مَختوم — مِن الرَبطِ الحَيِّ بِالحَلِّ نَفسِه الَّذي يَفُكُّ بِه المِفتاحُ لِلنِداء؛ <b>(ب)</b> خِزانَةٌ تَفُكّ (مِفتاحٌ رَئيسٌ مَضبوط)؛
    /// <b>(ج)</b> عَميلُ البَوّابَةِ مُسَجَّل. والقَرارُ نَقِيٌّ في <see cref="CheckoutCardFacts"/> — هُنا يُقرَأُ المُخَزَّن، وهُناكَ يُقَرَّر.</para>
    /// <para><b>ولا يُفَكُّ هُنا شَيء</b>: المِفتاحُ يُفَكُّ عِندَ فَتحِ الفاتورَةِ لِلنِداءِ الواحِد — وظَرفٌ لا يُفتَحُ يَومَئِذٍ يُلغي الصَفَقاتِ
    /// بِرَمزِه ولا يُخصَمُ شَيء (<see cref="OpenInvoiceAsync"/>). <b>وحَلَّت مَحَلَّ <c>ReadyAsync</c></b> (الخُطوَة 10) الَّتي كانَت تَفُكُّه
    /// لِتُجيب «أَيَفتَحُ المَتجَرُ فاتورَة؟» — مُستَهلِكُها الوَحيدُ النُقطَة، وصارَت تَقرَأُ <see cref="CheckoutCardFacts.ThroughGateway"/>.</para>
    /// </summary>
    public async Task<CheckoutCardFacts> CardFactsAsync(
        string tenantSlug, bool storeCollects, bool instanceCanCharge, CancellationToken ct = default)
        => new(storeCollects,
               SealedKeyFieldOf(await PaymentsBindingAsync(tenantSlug, ct)) is not null,
               _vault.IsAvailable,
               _client is not null,
               instanceCanCharge);

    /// <summary>
    /// <para><b>فاتورَةٌ واحِدَةٌ لِسَلَّةِ المُشتَري كُلِّها</b> (‏ADR-032 §٢‑هـ الخُطوَة 10): صَفَقاتُ <paramref name="dealIds"/> — وُلِدَت لِلتَّوّ
    /// بِطَريقَةِ «بِطاقَة» — بِمَجموعِها بِالهَلَلَةِ وبِمُعَرِّفاتِها في <c>metadata</c>، ورابِطُ العَودَةِ <paramref name="returnUrl"/> لِلنَجاحِ
    /// ولِلرُجوع. فَإن فُتِحَت كُتِبَ عَلى كُلِّ صَفقَةٍ مُعَرِّفُها ورابِطُها، وأُعيدَ الرابِطُ لِيُحَوَّلَ إلَيه المُشتَري.</para>
    ///
    /// <para><b>وإن لَم تُفتَح — أُلغِيَت الصَفَقاتُ بِاسمِ مُشتَريها</b>: لا صَفقَةَ بِطاقَةٍ تَبقى بِلا فاتورَةٍ يُدفَعُ بِها، ولا خُصِمَ شَيء؛
    /// والسَلَّةُ لا تُحذَفُ (النُقطَةُ تَرجِعُ قَبلَ حَذفِها) فَيُعيدُ المُحاوَلَةَ أَو يَختارُ النَقد. والرَمزُ في اللوغِ لِلمُراجَعَة.</para>
    /// <para>و<paramref name="l"/> لِنَصَّينِ يَراهُما الناس: وَصفُ الفاتورَةِ عَلى صَفحَةِ البَوّابَة، وسَبَبُ الإلغاء.</para>
    /// </summary>
    public async Task<GatewayCheckout> OpenInvoiceAsync(
        string tenantSlug, DealPayer by, IReadOnlyList<Guid> dealIds, string storeName, string returnUrl,
        ACommerce.Platform.I18n.L l, CancellationToken ct = default)
    {
        var deals = new List<Deal>();
        foreach (var id in dealIds)
            if (await _deals.LoadAsync(tenantSlug, id, ct) is { } d && d.InitiatorId == by.ActorId) deals.Add(d);

        var amount = MoyasarClient.ToSmallestUnit(deals.Sum(d => d.AmountSar));
        string? code = deals.Count != dealIds.Count || !GatewayPaymentProof.FitsOneInvoice(deals.Count) || amount is null
            ? MoyasarFailures.Invalid
            : null;

        MoyasarInvoice? invoice = null;
        if (code is null)
        {
            if (_client is null || await SecretKeyAsync(tenantSlug, ct) is not { } key)
                code = MoyasarFailures.NotConfigured;
            else
            {
                var draft = new MoyasarInvoiceDraft(amount!.Value, l.Format("pay.invoice.description", storeName), returnUrl, returnUrl,
                    GatewayPaymentProof.MetadataFor(deals.Select(d => d.Id).ToArray()));
                var created = await _client.CreateInvoiceAsync(key, draft, ct);
                if (created.Ok) invoice = created.Value; else code = created.Failure!.Code;
            }
        }

        if (invoice is null)
        {
            foreach (var d in deals)
                await _deals.CancelAsync(tenantSlug, d.Id, new DealCanceller(by.ActorId, d.InitiatorName, IsStoreAdmin: false),
                    l["checkout.payment.cancel_gateway_open_failed"], ct);
            _log.LogWarning("[gateway] open invoice {Slug} for {Count} deal(s) ← {Code} — cancelled, nothing charged",
                tenantSlug, deals.Count, code);
            return new GatewayCheckout(null, code);
        }

        foreach (var d in deals)
        {
            await _deals.AttachRefAsync(tenantSlug, d.Id, GatewayPaymentProof.InvoiceRef, invoice.Id, ct);
            await _deals.AttachRefAsync(tenantSlug, d.Id, InvoiceUrlRef, invoice.Url!, ct);
        }
        _log.LogInformation("[gateway] open invoice {Slug} for {Count} deal(s) ← opened", tenantSlug, deals.Count);
        return new GatewayCheckout(invoice.Url, null);
    }

    /// <summary>
    /// <para><b>مِفتاحُ المَتجَرِ صَريحاً — لِلنِداءِ الواحِدِ ولا يُحفَظ</b>. الرَبطُ يُقرَأُ بِمُعَرِّفِه (<c>CurrentAsync</c> — قِراءَةٌ حَيَّةٌ
    /// لا لَقطَةُ الكاش: رَبطٌ سُحِبَ الآنَ لا يُطلَبُ بِه) ويُحَلُّ بِـ<c>TenantProviderSet.FromDocuments</c> نَفسِها الَّتي تَحُلُّ لَقطَةَ
    /// المَتجَر — فَلا قاعِدَةَ قَبولٍ ثانِيَة. ويُفَكُّ بِسِياقِ <c>tenant|payments|field</c> الَّذي خَتَمَه بِه سَطحُ الرَبط
    /// (<see cref="ProviderBindSurface.AadFor"/>)؛ وظَرفٌ لا يُفتَحُ بِسِياقِه — مَنسوخٌ مِن مَتجَرٍ آخَر، أَو مِفتاحٌ رَئيسٌ آخَر — «لا مِفتاح».</para>
    /// <para><b>واللوغُ لا يَحمِلُ المِفتاحَ ولا ذَيلَه</b>: المَتجَرُ وَحدَه.</para>
    /// </summary>
    private async Task<string?> SecretKeyAsync(string tenantSlug, CancellationToken ct)
    {
        if (!_vault.IsAvailable) return null;

        var payments = await PaymentsBindingAsync(tenantSlug, ct);
        if (SealedKeyFieldOf(payments) is not { } field) return null;

        try
        {
            return payments!.Values[field.Code].Unseal(_vault, ProviderBindSurface.AadFor(tenantSlug, payments.Capability, field.Code));
        }
        catch (CryptographicException)
        {
            _log.LogWarning("[gateway] {Slug} payments key does not open — no key", tenantSlug);
            return null;
        }
    }

    /// <summary>رَبطُ الدَفعِ الحَيُّ مَحلولاً — بِمُعَرِّفِه (<c>CurrentAsync</c>، لا لَقطَةُ الكاش) وبِـ<c>TenantProviderSet.FromDocuments</c>
    /// نَفسِها؛ يَقرَؤُه فَكُّ المِفتاحِ ومُدخَلُ «(أ)» في <see cref="CardFactsAsync"/> (‏2026-10-12)، فَلا يَفتَرِقُ «مَربوطٌ بِمِفتاح» عَمّا يُفَكّ.</summary>
    private async Task<ResolvedProvider?> PaymentsBindingAsync(string tenantSlug, CancellationToken ct)
    {
        var current = await _providers.CurrentAsync(tenantSlug, ProviderCapabilities.Payments, ct);
        return TenantProviderSet
            .FromDocuments(tenantSlug, current is null ? Array.Empty<TenantProviderBinding>() : new[] { current })
            .For(ProviderCapabilities.Payments);
    }

    /// <summary>
    /// <para><b>الصَفَقاتُ الَّتي تُعرَضُ عَلى البَوّابَة</b>: المَرجِعُ أَوَّلاً، ثُمَّ ما سَمَّتهُ بَياناتُ الفاتورَةِ مِن صَفَقاتِ هذا
    /// الدافِعِ وَحدَه — وغَيرُ صَفَقاتِه لا تُحَمَّلُ في القائِمَة، فَتَحمَرُّ البَوّابَةُ بِـ<see cref="GatewayPaymentProof.DealsMismatch"/>.
    /// وبَياناتٌ لا تُقرَأُ أَو أَطوَلُ مِن فاتورَةٍ واحِدَةٍ (<see cref="GatewayPaymentProof.MaxDealsPerInvoice"/>) لا يُحَمَّلُ مِنها شَيء —
    /// فَالتَحميلُ مَحدودٌ بِحَدِّ الوَثيقَة.</para>
    /// </summary>
    private async Task<IReadOnlyList<Deal>> InvoiceDealsAsync(
        string tenantSlug, Deal referenced, MoyasarInvoice invoice, DealPayer by, CancellationToken ct)
    {
        var deals = new List<Deal> { referenced };
        var listed = GatewayPaymentProof.DealIdsIn(invoice.Metadata);
        if (listed is null || listed.Count > GatewayPaymentProof.MaxDealsPerInvoice) return deals;

        foreach (var id in listed)
        {
            if (id == referenced.Id) continue;
            if (await _deals.LoadAsync(tenantSlug, id, ct) is { } d && d.InitiatorId == by.ActorId) deals.Add(d);
        }
        return deals;
    }

    /// <summary>سَطرُ اللوغ: المَتجَرُ والصَفقَةُ والرَمز — لا مِفتاحٌ ولا رابِطُ فاتورَة.</summary>
    private GatewayVerifyResult Logged(string tenantSlug, GatewayVerifyResult r)
    {
        _log.LogInformation("[gateway] verify {Slug} deal {Deal} ← {Code}",
            tenantSlug, r.DealId?.ToString("D") ?? "—", r.Ok ? "proven" : r.Code);
        return r;
    }
}
