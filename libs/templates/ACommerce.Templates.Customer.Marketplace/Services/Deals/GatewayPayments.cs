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
    /// لِلرَفضِ ولِلتَحَقُّقِ الثاني مِن الدَفعِ نَفسِه. (‏2026-10-12، مُراجَعَةُ تَيّارِ S9 الأولى — ولا يُعادُ حَرفٌ فَوقَه: وفيها المُلغاةُ
    /// إن دُفِعَت — يُكتَبُ دَفعُها ولا يُبتلَع؛ والإشعارُ لِبائِعِ القائِمَةِ وَحدَها، <c>DealNotices.PaidRecipient</c>.)</summary>
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
    /// <para><b>ولِصَفقَةٍ قائِمَةٍ وَحدَها</b> (‏2026-10-12، مُراجَعَةُ تَيّارِ S9 الأولى — ولا يُعادُ حَرفٌ فَوقَ ما سَبَق): البائِعُ المَربوطُ
    /// عِندَ الوِلادَةِ يُشعَرُ بِالطَلَبِ قَبلَ الدَفعِ ويَرفُضُه عِندَ «عرض»، والعَميلُ بِلا عَمَلِيَّةِ إبطال (§٨‑أ) فَالفاتورَةُ تَبقى قابِلَةً
    /// لِلدَّفع — وكانَ «أَكمِلِ الدَّفعَ» يُرسَمُ لِطَلَبٍ مُلغًى فَيُدعى المُشتَري إلى دَفعِ ما لَم يَعُد قائِماً (القاعِدَة ١٢: مَدخَلٌ يَضُرّ).
    /// وصَفقَةٌ في نِزاعٍ أَو مُكتَمِلَةٌ لا تُدعى إلى دَفعٍ أَيضاً. وزِرُّ التَحَقُّقِ باقٍ لِكُلِّ صَفقَة: دَفعٌ وَقَعَ يُسَجَّلُ ولا يُبتلَع.</para>
    /// <para>(‏2026-10-12، مُراجَعَةُ تَيّارِ S9 الثانِيَة — ولا يُعادُ حَرفٌ فَوقَ ما سَبَق: «بِلا عَمَلِيَّةِ إبطال» صارَ «بِإلغاءٍ لِآخِرِ قائِمَة»
    /// — الفاتورَةُ تُلغى عِندَ البَوّابَةِ حينَ تُلغى آخِرُ صَفقَةٍ قائِمَةٍ تَدفَعُ لَها، <see cref="GatewayInvoiceCloser"/>؛ وأُختٌ قائِمَةٌ تُبقيها
    /// مَفتوحَة، فَالرابِطُ لِلقائِمَةِ وَحدَها كَما هُوَ.)</para>
    /// </summary>
    public static string? PayUrlOf(Deal deal)
        => deal.Status == DealStatus.Active
           && deal.Refs.TryGetValue(GatewayPayments.InvoiceUrlRef, out var url)
           && Uri.TryCreate(url, UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttps || u.Scheme == Uri.UriSchemeHttp)
            ? url
            : null;

    /// <summary>
    /// <para><b>نَصُّ حالِ الصَفقَةِ عَلى صَفحَةِ العَودَة — أَربَعَةٌ لا خامِس</b> (‏2026-10-12، مُراجَعَةُ تَيّارِ S9 الأولى): ثَبَتَ دَفعُها
    /// أَم لا (<see cref="IsConfirmed"/>) × أُلغِيَت أَم لا — <b>بِشَرطِ الكاتِبِ نَفسِه</b>: <c>DealsService.ConfirmGatewayPaymentAsync</c>
    /// يَكتُبُ <see cref="DealEvent.GatewayRefundDue"/> لِلمُلغاةِ وَحدَها، فَالصَفحَةُ تَقولُ «مُلغًى، واستِرجاعُه عَلى المَتجَرِ مِن لَوحَةِ
    /// بَوّابَتِه» لِما يُكتَبُ لَه ذلكَ السَطرُ لا لِغَيرِه. وكانَت تَقولُ لِلمُلغاةِ «طَلَبُك قائِمٌ في صَفقاتي» قَبلَ الدَفع، و«يُتابِعُه البائِعُ
    /// مِن هُنا» بَعدَه — ولا قائِمَ ولا مُتابِع. والصَفحَةُ لا تَرسُمُ نَصَّ حالٍ إلّا بِهذِه الدالَّة.</para>
    /// </summary>
    public static string StateKeyOf(Deal deal) => (deal.Status == DealStatus.Cancelled, IsConfirmed(deal)) switch
    {
        (false, false) => "pay.return.pending",
        (false, true)  => "pay.return.confirmed",
        (true,  false) => "pay.return.cancelled_pending",
        (true,  true)  => "pay.return.cancelled_confirmed",
    };

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
    /// <para><b>صَفَقاتُ الفاتورَةِ الواحِدَة عَلى كُلِّ صَفقَةٍ مِنها</b> (‏2026-10-13، مُراجَعَةُ بَوّابَةِ المَوجَة 3b، G5 M4؛ ‏ADR-032 §٨‑ز مُلحَقاً):
    /// مُعَرِّفاتُها مَفصولَةً بِفاصِلَة، يَكتُبُها <see cref="OpenInvoiceAsync"/> مَعَ مَرجِعِ الفاتورَة — فَيَعرِفُ الإلغاءُ وصَفحَتا الصَفقَةِ
    /// أَخَواتِ الفاتورَةِ بِلا نِداءٍ إلى البَوّابَةِ ولا استِعلام (<see cref="GatewayInvoiceShare"/>). داخِلِيٌّ: لا يُرسَمُ لِلطَرَفَين.</para>
    /// </summary>
    public const string InvoiceDealsRef = "gateway_invoice_deals";

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

        if (_client is null || await SecretKeyAsync(_providers, _vault, _log, tenantSlug, ct) is not { } key)
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
               SealedKeyFieldOf(await PaymentsBindingAsync(_providers, tenantSlug, ct)) is not null,
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
    /// <para><b>وتُفَوتَرُ القائِمَةُ وَحدَها</b> (‏2026-10-12، مُراجَعَةُ تَيّارِ S9 الأولى — ولا يُعادُ حَرفٌ فَوقَ ما سَبَق): البائِعُ مَربوطٌ
    /// مُنذُ الوِلادَةِ فَالصَفقَةُ في قائِمَتِه، ويُلغيها عِندَ «عرض» — وكانَت صَفقَةٌ أَلغاها بَينَ وِلادَتِها والفَتحِ تَدخُلُ الفاتورَةَ بِمَبلَغِها
    /// ويُكتَبُ عَلَيها رابِطُها. فَلا تُحَمَّلُ إلّا <c>Active</c>، فَتَنقُصُ القائِمَةُ عَن المُعَرِّفاتِ ويُرَدُّ الفَتحُ بِرَمزِ صَفقَةِ غَيرِه
    /// نَفسِه (<see cref="MoyasarFailures.Invalid"/>): لا فاتورَةَ ولا خَصم، والباقِيَةُ تُلغى والسَلَّةُ كَما هي. <b>وما لا يُغلِقُه</b>: إلغاءٌ
    /// يَقَعُ بَعدَ التَحميلِ وقَبلَ الإلحاقِ (أَثناءَ نِداءَي البَوّابَة) يَبقى في الفاتورَة — ولا إبطالَ في العَميل (§٨‑أ)؛ وصَفحَةُ العَودَةِ
    /// وإشعارُ الدَفعِ يَسأَلانِ الحالَ بَعدَها (<see cref="GatewayReturn.PayUrlOf"/>، <c>DealNotices.PaidRecipient</c>).</para>
    /// <para>(‏2026-10-12، مُراجَعَةُ تَيّارِ S9 الثانِيَة — ولا يُعادُ حَرفٌ فَوقَ ما سَبَق: «ولا إبطالَ في العَميل» لَم يَعُد — لِلعَميلِ إلغاءٌ
    /// (<see cref="MoyasarClient.CancelInvoiceAsync"/>) يُناديه <see cref="GatewayInvoiceCloser"/> بَعدَ إلغاءٍ كُتِب؛ وذلكَ الإلغاءُ في النافِذَةِ لا
    /// يَبلُغُه لِأَنّ الصَفقَةَ بِلا مَرجِعِ فاتورَةٍ ساعَتَها، فَيَبقى كَما قيل.)</para>
    /// </summary>
    public async Task<GatewayCheckout> OpenInvoiceAsync(
        string tenantSlug, DealPayer by, IReadOnlyList<Guid> dealIds, string storeName, string returnUrl,
        ACommerce.Platform.I18n.L l, CancellationToken ct = default)
    {
        var deals = new List<Deal>();
        foreach (var id in dealIds)
            if (await _deals.LoadAsync(tenantSlug, id, ct) is { Status: DealStatus.Active } d && d.InitiatorId == by.ActorId) deals.Add(d);

        var amount = MoyasarClient.ToSmallestUnit(deals.Sum(d => d.AmountSar));
        string? code = deals.Count != dealIds.Count || !GatewayPaymentProof.FitsOneInvoice(deals.Count) || amount is null
            ? MoyasarFailures.Invalid
            : null;

        MoyasarInvoice? invoice = null;
        if (code is null)
        {
            if (_client is null || await SecretKeyAsync(_providers, _vault, _log, tenantSlug, ct) is not { } key)
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

        var siblings = GatewayInvoiceShare.ValueFor(deals.Select(d => d.Id).ToArray());
        foreach (var d in deals)
        {
            await _deals.AttachRefAsync(tenantSlug, d.Id, GatewayPaymentProof.InvoiceRef, invoice.Id, ct);
            await _deals.AttachRefAsync(tenantSlug, d.Id, InvoiceUrlRef, invoice.Url!, ct);
            // وأَخَواتُها عَلى الفاتورَة — فَلا تُلغى إحداها وَحدَها والفاتورَةُ مَفتوحَة (‏2026-10-13، G5 M4).
            await _deals.AttachRefAsync(tenantSlug, d.Id, InvoiceDealsRef, siblings, ct);
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
    /// <para><b>وصارَت ساكِنَةً بِمُدخَلاتِها</b> (‏2026-10-12، مُراجَعَةُ تَيّارِ S9 الثانِيَة — ولا يُعادُ حَرفٌ فَوقَ ما سَبَق): وُلِدَ لَها
    /// مُستَهلِكٌ ثانٍ لا يَملِكُ هذِه الخِدمَة — <see cref="GatewayInvoiceCloser"/> مِن الإلغاء، و<see cref="GatewayPayments"/> تَحقِنُ
    /// <see cref="DealsService"/> فَلا تُحقَنُ فيها. فَالفَكُّ في مَوضِعٍ واحِدٍ لِلاثنَين (القاعِدَة ٨)، والجِسمُ بِحَرفِه.</para>
    /// </summary>
    internal static async Task<string?> SecretKeyAsync(
        TenantProviderService providers, ProviderVault vault, ILogger log, string tenantSlug, CancellationToken ct)
    {
        if (!vault.IsAvailable) return null;

        var payments = await PaymentsBindingAsync(providers, tenantSlug, ct);
        if (SealedKeyFieldOf(payments) is not { } field) return null;

        try
        {
            return payments!.Values[field.Code].Unseal(vault, ProviderBindSurface.AadFor(tenantSlug, payments.Capability, field.Code));
        }
        catch (CryptographicException)
        {
            log.LogWarning("[gateway] {Slug} payments key does not open — no key", tenantSlug);
            return null;
        }
    }

    /// <summary>رَبطُ الدَفعِ الحَيُّ مَحلولاً — بِمُعَرِّفِه (<c>CurrentAsync</c>، لا لَقطَةُ الكاش) وبِـ<c>TenantProviderSet.FromDocuments</c>
    /// نَفسِها؛ يَقرَؤُه فَكُّ المِفتاحِ ومُدخَلُ «(أ)» في <see cref="CardFactsAsync"/> (‏2026-10-12)، فَلا يَفتَرِقُ «مَربوطٌ بِمِفتاح» عَمّا يُفَكّ.</summary>
    private static async Task<ResolvedProvider?> PaymentsBindingAsync(TenantProviderService providers, string tenantSlug, CancellationToken ct)
    {
        var current = await providers.CurrentAsync(tenantSlug, ProviderCapabilities.Payments, ct);
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

// ═══ إلغاءُ فاتورَةِ البَوّابَةِ حينَ تُلغى آخِرُ صَفقَةٍ قائِمَةٍ تَدفَعُ لَها (‏2026-10-12، مُراجَعَةُ تَيّارِ S9 الثانِيَة؛ ‏ADR-032 §٨‑ز) ═══════
//
// البائِعُ يُشعَرُ بِالطَلَبِ بَعدَ فَتحِ الفاتورَةِ والمُشتَري عَلى صَفحَةِ البَوّابَة، ويُلغيه بابٌ تَرسُمُه صَفحَتُه — وكانَت الفاتورَةُ تَبقى
// قابِلَةً لِلدَّفعِ عِندَ البَوّابَةِ ولَو أُلغِيَت كُلُّ صَفقَةٍ تَدفَعُ لَها (تَوصيفُه `GatewayInvoiceCancelTests`): يَدفَعُها المُشتَري مِن الصَفحَةِ
// المَفتوحَةِ أَمامَه، فَيُؤخَذُ مالُه عَن طَلَبٍ مُلغًى ويُستَرَدُّ يَدَوِيّاً (§٨‑ج.6). ووَثيقَةُ البَوّابَةِ تَعرِفُ «Cancel invoice»
// (`MoyasarClient.CancelInvoiceAsync`). فَبَعدَ أَن يُحفَظَ الإلغاءُ — المُطالَبَةُ أَوَّلاً (‏ADR-036 الخُطوَة 2) — تُجلَبُ الفاتورَةُ بِمِفتاحِ
// المَتجَرِ وتُحَمَّلُ الصَفَقاتُ الَّتي تُسَمّيها، وتُسأَلُ `GatewayInvoiceClosing` النَقِيَّة: **أَهذِه فاتورَتُها، وأَمَفتوحَةٌ هي، وأَلا قائِمَةَ
// عَلَيها لَم تُدفَع؟** — فَإن صَدَقَت الثَلاثُ أُلغِيَت عِندَ البَوّابَة. وأُختٌ قائِمَةٌ تُبقيها: الفاتورَةُ تَدفَعُ لَها، وإلغاؤها وفَتحُ أُخرى
// بِالباقي قَرارُ مُنتَجٍ لا يُتَّخَذُ هُنا («ب» S9·١٤). **وفَشَلُ أَيِّ خُطوَةٍ رَمزٌ في اللوغِ لا يَرُدُّ الإلغاء** — قَد حُفِظَ قَبلَها.

// ═══ لا تُلغى صَفقَةٌ واحِدَةٌ مِن فاتورَةٍ مَفتوحَةٍ تَدفَعُ لِغَيرِها (‏2026-10-13، مُراجَعَةُ بَوّابَةِ المَوجَة 3b، G5 M4؛ ‏ADR-032 §٨‑ز مُلحَقاً) ═══
//
// إتمامُ الشِراءِ يَضَعُ صَفَقاتِ السَلَّةِ كُلَّها عَلى فاتورَةِ Moyasar واحِدَة، والطَرَفُ المُقابِلُ يُلغي أَيَّ صَفقَةٍ قائِمَةٍ وَحدَها — فَتَبقى
// الفاتورَةُ قابِلَةً لِلدَّفعِ بِالمَبلَغِ كامِلاً (`GatewayInvoiceCloser` لا يُلغيها ما دامَت أُختٌ قائِمَة، ووَثيقَةُ Moyasar لا تُغَيِّرُ المَبلَغ:
// التَحديثُ يُغَيِّرُ البَيانات، والإلغاءُ يُلغي الفاتورَةَ كُلَّها). تَوصيفُه `GatewayInvoiceShareTests` (`411e120`). **القَرارُ تَحتَ التَفويض**
// (التَوصِيَةُ بِحَرفِها): ما دامَت الفاتورَةُ مَفتوحَةً لَم تُدفَع، **إلغاءُ صَفقَةٍ مِنها وأُختٌ قائِمَةٌ عَلَيها يُرَدُّ** بِرَمزٍ مُسَمّى — فَتَبقى الفاتورَةُ
// مُساوِيَةً لِصَفَقاتِها القائِمَة؛ **والطَلَبُ كُلُّه يُلغى** (كُلُّ صَفَقاتِ الفاتورَة — والأَخيرَةُ تُلغي الفاتورَةَ بِالمُلغي القائِم)؛ وبَعدَ الدَفعِ
// يُلغى بَعضُها بِمَسارِ «استِرجاعُه مُستَحَقٌّ عِندَ البَوّابَة» القائِم.

/// <summary>رَدُّ إلغاءِ صَفقَةٍ واحِدَةٍ مِن فاتورَةٍ مُشتَرَكَة — <c>Code</c> مِن <see cref="GatewayInvoiceShare.All"/>.</summary>
public sealed record DealInvoiceShareViolation(string Code, string MessageAr);

/// <summary>
/// <para><b>أَتُشارِكُ الصَفقَةُ فاتورَتَها المَفتوحَةَ صَفَقاتٍ قائِمَةً أُخرى؟</b> — بَوّابَةٌ نَقِيَّةٌ بِرَمزٍ واحِدٍ لا ثانِيَ لَه (القاعِدَة ٤)، يَسأَلُها
/// <see cref="DealsService.CancelAsync"/> قَبلَ أَيِّ كِتابَةٍ وصَفحَتا الصَفقَةِ قَبلَ رَسمِ بابِ الإلغاء (القاعِدَتانِ ٧ و١٢). تَقرَأُ الصَفقَةَ
/// وأَخَواتِها كَما حَمَّلَها المُنادي مِمّا سَمّاه <see cref="GatewayPayments.InvoiceDealsRef"/> — بِلا شَبَكَةٍ ولا Marten.</para>
/// <para><b>ولا تَرُدُّ ما لا تَعرِفُ أَخَواتِه</b>: صَفقَةٌ فُتِحَت فاتورَتُها قَبلَ هذا المَرجِعِ (أَو بِلا فاتورَة) تُلغى كَما كانَت — الباقي المُعلَنُ في
/// ‏ADR-032 §٨‑ز مُلحَقاً.</para>
/// </summary>
public static class GatewayInvoiceShare
{
    /// <summary>عَلى فاتورَةِ الصَفقَةِ المَفتوحَةِ — الَّتي لَم يُكتَب دَفعُها — صَفقَةٌ قائِمَةٌ أُخرى لَم يُكتَب دَفعُها.</summary>
    public const string Shared = "deal_invoice_shared";

    /// <summary>المَعجَمُ المُغلَق — واحِدٌ لا ثانِيَ لَه.</summary>
    public static readonly IReadOnlyList<string> All = new[] { Shared };

    /// <summary>قيمَةُ <see cref="GatewayPayments.InvoiceDealsRef"/> — المُعَرِّفاتُ بِصيغَةِ <c>D</c> مَفصولَةً بِفاصِلَة.</summary>
    public static string ValueFor(IReadOnlyList<Guid> dealIds) => string.Join(',', dealIds.Select(id => id.ToString("D")));

    /// <summary>أَخَواتُ الفاتورَةِ كَما كَتَبَها فَتحُها — أَو <c>null</c> حينَ لا مَرجِعَ أَو لا يُقرَأُ أَو أَطوَلُ مِن فاتورَةٍ واحِدَة.</summary>
    public static IReadOnlyList<Guid>? DealsOn(Deal deal)
    {
        if (!deal.Refs.TryGetValue(GatewayPayments.InvoiceDealsRef, out var raw) || string.IsNullOrWhiteSpace(raw)) return null;
        var ids = new List<Guid>();
        foreach (var part in raw.Split(','))
        {
            if (!Guid.TryParse(part, out var id)) return null;
            ids.Add(id);
        }
        return GatewayPaymentProof.FitsOneInvoice(ids.Count) && ids.Distinct().Count() == ids.Count ? ids : null;
    }

    /// <summary>صَفَقاتُ الفاتورَةِ الَّتي تَدفَعُ لَها الآن — الصَفقَةُ إن كانَت تَدفَعُ لَها، وكُلُّ أُختٍ سَمّاها مَرجِعُها وعَلَيها الفاتورَةُ نَفسُها
    /// وقائِمَةٌ لَم يُكتَب دَفعُها (<see cref="GatewayInvoiceClosing.Owed"/>).</summary>
    public static IReadOnlyList<Deal> OwedOnInvoice(Deal deal, IReadOnlyList<Deal> siblings)
    {
        var owed = new List<Deal>();
        if (!GatewayPayments.HasInvoice(deal)) return owed;
        if (GatewayInvoiceClosing.Owed(deal)) owed.Add(deal);
        if (DealsOn(deal) is not { } listed) return owed;
        var invoice = deal.Refs[GatewayPaymentProof.InvoiceRef];
        owed.AddRange(siblings.Where(s => s.Id != deal.Id && listed.Contains(s.Id)
            && s.Refs.TryGetValue(GatewayPaymentProof.InvoiceRef, out var other)
            && string.Equals(other, invoice, StringComparison.OrdinalIgnoreCase)
            && GatewayInvoiceClosing.Owed(s)));
        return owed;
    }

    /// <summary>فارِغَةٌ = تُلغى وَحدَها. والسَبَبُ حينَ الصَفقَةُ تَدفَعُ لِفاتورَتِها وأُختٌ أُخرى تَدفَعُ لَها.</summary>
    public static IReadOnlyList<DealInvoiceShareViolation> Validate(Deal deal, IReadOnlyList<Deal> siblings)
    {
        var owed = OwedOnInvoice(deal, siblings);
        return owed.Any(d => d.Id == deal.Id) && owed.Any(d => d.Id != deal.Id)
            ? new[] { new DealInvoiceShareViolation(Shared,
                "عَلى فاتورَةِ هذِه الصَفقَةِ المَفتوحَةِ صَفَقاتٌ قائِمَةٌ أُخرى — لا تُلغى وَحدَها: يُلغى الطَلَبُ كُلُّه، أَو تُلغى بَعدَ الدَفع.") }
            : Array.Empty<DealInvoiceShareViolation>();
    }

    public static bool IsValid(Deal deal, IReadOnlyList<Deal> siblings) => Validate(deal, siblings).Count == 0;
}

/// <summary>سَبَبٌ لِإبقاءِ الفاتورَةِ مَفتوحَةً بَعدَ إلغاءِ صَفقَةٍ عَلَيها — <c>Code</c> مِن <see cref="GatewayInvoiceClosing.All"/>.</summary>
public sealed record GatewayInvoiceCloseViolation(string Code, string MessageAr);

/// <summary>
/// <para><b>أَيُلغي إلغاءُ هذِه الصَفقَةِ فاتورَتَها عِندَ البَوّابَة؟</b> — بَوّابَةٌ نَقِيَّةٌ بِثَلاثَةِ رُموزٍ لا رابِعَ لَها (القاعِدَة ٤)، لِكُلٍّ مُوجَبٌ
/// وسالِبٌ في <c>GatewayInvoiceCancelTests</c>. تَسأَلُ الفاتورَةَ كَما جُلِبَت لِلتَّوّ، والصَفقَةَ المُلغاة، والصَفَقاتِ الَّتي تُسَمّيها الفاتورَة —
/// بِتَرتيبٍ ثابِت، ولا شَبَكَةَ ولا Marten ولا وَقت.</para>
/// <para><b>ورُموزُها لا تَبلُغُ شاشَة</b>: اللوغُ وَحدَه يَحمِلُها، فَلَيسَت في <see cref="GatewayReturn.Codes"/> ولا نَصَّ لَها في القامُوس.</para>
/// </summary>
public static class GatewayInvoiceClosing
{
    /// <summary>الفاتورَةُ لَيسَت فاتورَةَ هذِه الصَفقَة — لَيسَت المُسَجَّلَةَ عَلَيها (<see cref="GatewayPaymentProof.InvoiceRef"/>)، أَو بَياناتُها لا
    /// تُسَمّيها أَو لا تُقرَأُ أَو أَطوَلُ مِن فاتورَةٍ واحِدَة: فَلا تُلغى بِإلغائِها.</summary>
    public const string NotListed = "gateway_invoice_not_listed";

    /// <summary>حالُ الفاتورَةِ عِندَ البَوّابَةِ لَيسَ <see cref="MoyasarClient.OpenStatus"/> — دُفِعَت، أَو أُلغِيَت، أَو انتَهَت، أَو حالٌ لا تَشرَحُه
    /// الوَثيقَة: فَلا يُلغى ما دُفِع، ولا يُنادى عَلى ما لا يُعرَفُ جَوابُه.</summary>
    public const string NotOpen = "gateway_invoice_not_open";

    /// <summary>صَفقَةٌ تُسَمّيها الفاتورَةُ قائِمَةٌ لَم يُكتَب دَفعُها (<see cref="Owed"/>) — الفاتورَةُ ما زالَت تَدفَعُ لَها.</summary>
    public const string StillOwed = "gateway_invoice_still_owed";

    /// <summary>المَعجَمُ المُغلَق — ثَلاثَةٌ لا رابِعَ لَها.</summary>
    public static readonly IReadOnlyList<string> All = new[] { NotListed, NotOpen, StillOwed };

    /// <summary>أَتَدفَعُ الفاتورَةُ لِهذِه الصَفقَة؟ — قائِمَةٌ لَم يُكتَب دَفعُها (<see cref="DealsService.IsCaptured"/>). و«القائِمَةُ» بِحَرفِ
    /// <see cref="GatewayReturn.PayUrlOf"/> و<c>DealNotices.PaidRecipient</c> و<see cref="GatewayPayments.OpenInvoiceAsync"/>: <c>Active</c> وَحدَها.</summary>
    public static bool Owed(Deal deal) => deal.Status == DealStatus.Active && !DealsService.IsCaptured(deal);

    /// <summary>
    /// <para><b>كُلُّ سَبَبٍ لِإبقاءِ الفاتورَة</b> — الفاتورَةُ فاتورَتُها، ثُمَّ الحال، ثُمَّ الصَفَقاتُ الَّتي تَدفَعُ لَها: <paramref name="named"/> كَما
    /// حَمَّلَها المُنادي مِمّا تُسَمّيه البَيانات، و<paramref name="cancelled"/> مَعَها — وما في <paramref name="named"/> ولا تُسَمّيه البَياناتُ
    /// لا يُحسَب. فارِغَةٌ = تُلغى.</para>
    /// </summary>
    public static IReadOnlyList<GatewayInvoiceCloseViolation> Validate(MoyasarInvoice invoice, Deal cancelled, IReadOnlyList<Deal> named)
    {
        var v = new List<GatewayInvoiceCloseViolation>();
        var listed = GatewayPaymentProof.DealIdsIn(invoice.Metadata);

        if (!cancelled.Refs.TryGetValue(GatewayPaymentProof.InvoiceRef, out var recorded)
            || !string.Equals(recorded, invoice.Id, StringComparison.OrdinalIgnoreCase)
            || listed is null || listed.Count > GatewayPaymentProof.MaxDealsPerInvoice || !listed.Contains(cancelled.Id))
            v.Add(new(NotListed, "الفاتورَةُ لَيسَت فاتورَةَ هذِه الصَفقَة — لا تُلغى بِإلغائِها."));

        if (!string.Equals(invoice.Status, MoyasarClient.OpenStatus, StringComparison.Ordinal))
            v.Add(new(NotOpen, $"حالُ الفاتورَةِ «{invoice.Status}» لا «{MoyasarClient.OpenStatus}» — لا تُلغى."));

        if (named.Where(d => listed?.Contains(d.Id) == true).Append(cancelled).Any(Owed))
            v.Add(new(StillOwed, "صَفقَةٌ تُسَمّيها الفاتورَةُ قائِمَةٌ لَم تُدفَع — الفاتورَةُ تَدفَعُ لَها فَتَبقى."));

        return v;
    }

    public static bool IsValid(MoyasarInvoice invoice, Deal cancelled, IReadOnlyList<Deal> named)
        => Validate(invoice, cancelled, named).Count == 0;
}

/// <summary>
/// <para><b>يُلغي فاتورَةَ البَوّابَةِ بَعدَ إلغاءِ آخِرِ صَفقَةٍ قائِمَةٍ تَدفَعُ لَها</b> — يُناديه <see cref="DealsService.CancelAsync"/> بَعدَ حِفظِ
/// الإلغاءِ لِصَفقَةٍ عَلَيها فاتورَةٌ لَم يُقبَض بِها، نَسَقَ الاستِرجاع: الأَثَرُ لِمَن كَسَبَ الحِفظ. يَفُكُّ مِفتاحَ المَتجَرِ لِلنِداءِ الواحِدِ بِالدالَّةِ
/// الَّتي تَفُكُّه بِها <see cref="GatewayPayments"/> (القاعِدَة ٨)، ويَجلِبُ الفاتورَة، ويُحَمِّلُ ما تُسَمّيه مِن صَفَقاتٍ بِحَدِّ الوَثيقَة،
/// ويَسأَلُ <see cref="GatewayInvoiceClosing"/>، ثُمَّ يُلغيها عِندَ البَوّابَةِ إن صَدَقَت.</para>
/// <para><b>ولا يَرُدُّ الإلغاءَ ولا يَرمي لِفَشَلِ البَوّابَة</b>: ما يَعودُ رَمزٌ مِن مَعجَمٍ مُغلَقٍ يُكتَبُ في اللوغِ ويُرجَع — <c>null</c> «أُلغِيَت
/// عِندَ البَوّابَة»، وإلّا <see cref="DealPayAuthorization.InvoiceMissing"/> أَو رَمزٌ مِن <see cref="GatewayInvoiceClosing.All"/> أَو مِن
/// <see cref="MoyasarFailures.All"/>. <b>ولا يُمَرَّرُ إلغاءُ الطَلَب</b> (نَسَقُ <c>/ship</c> والتَحَقُّق): الإلغاءُ حُفِظَ قَبلَه فَأَثَرُه يَتبَعُه
/// ولَو انقَطَعَ المُنادي، ومُهلَةُ العَميلِ تَحُدُّ كُلَّ نِداء.</para>
/// <para><b>ولِماذا صِنفٌ لا دالَّةٌ في <see cref="GatewayPayments"/></b> (القاعِدَة ١): تِلكَ تَحقِنُ <see cref="DealsService"/>، فَلا تُحقَنُ فيها؛
/// وهذا لا يَحقِنُ الخِدمَةَ بَل يَأخُذُ مِنها مُحَمِّلَ الصَفَقاتِ لِلنِداءِ الواحِد. مُستَهلِكُه وَقتَ التَشغيلِ واحِدٌ مُسَمّى — الإلغاء.</para>
/// </summary>
public sealed class GatewayInvoiceCloser
{
    private readonly TenantProviderService _providers;
    private readonly ProviderVault _vault;
    /// <summary>عَميلُ البَوّابَة — و<c>null</c> حينَ لا يُسَجِّلُه المُضيف، فَلا جَلبَ ولا إلغاء (<see cref="MoyasarFailures.NotConfigured"/>).</summary>
    private readonly MoyasarClient? _client;
    private readonly ILogger<GatewayInvoiceCloser> _log;

    public GatewayInvoiceCloser(TenantProviderService providers, ProviderVault vault, MoyasarClient? client = null,
        ILogger<GatewayInvoiceCloser>? log = null)
    {
        _providers = providers;
        _vault = vault;
        _client = client;
        _log = log ?? NullLogger<GatewayInvoiceCloser>.Instance;
    }

    /// <summary>
    /// <para><b>بِالتَرتيب، ولا يُقلَب</b>: (١) عَلى الصَفقَةِ مَرجِعُ فاتورَة — وإلّا لا نِداء؛ (٢) مِفتاحُ المَتجَرِ يُفَكُّ — وإلّا لا نِداء؛
    /// (٣) الفاتورَةُ تُجلَبُ بِمَرجِعِها؛ (٤) الصَفَقاتُ الَّتي تُسَمّيها تُحَمَّلُ بِـ<paramref name="load"/> — ثَلاثونَ عَلى الأَكثَر؛ (٥) البَوّابَةُ
    /// النَقِيَّة؛ (٦) الإلغاءُ عِندَ البَوّابَة. و<paramref name="cancelled"/> الصَفقَةُ كَما حُفِظَ إلغاؤها.</para>
    /// </summary>
    public async Task<string?> CloseIfLastAsync(string tenantSlug, Deal cancelled, Func<Guid, Task<Deal?>> load)
    {
        if (!GatewayPayments.HasInvoice(cancelled))
            return Logged(tenantSlug, cancelled, DealPayAuthorization.InvoiceMissing);

        if (_client is null
            || await GatewayPayments.SecretKeyAsync(_providers, _vault, _log, tenantSlug, CancellationToken.None) is not { } key)
            return Logged(tenantSlug, cancelled, MoyasarFailures.NotConfigured);

        var fetched = await _client.FetchInvoiceAsync(key, cancelled.Refs[GatewayPaymentProof.InvoiceRef]);
        if (!fetched.Ok) return Logged(tenantSlug, cancelled, fetched.Failure!.Code);
        var invoice = fetched.Value!;

        var named = new List<Deal>();
        if (GatewayPaymentProof.DealIdsIn(invoice.Metadata) is { } ids && ids.Count <= GatewayPaymentProof.MaxDealsPerInvoice)
            foreach (var id in ids)
                if (await load(id) is { } d) named.Add(d);

        if (GatewayInvoiceClosing.Validate(invoice, cancelled, named) is { Count: > 0 } kept)
            return Logged(tenantSlug, cancelled, kept[0].Code);

        var closed = await _client.CancelInvoiceAsync(key, invoice.Id);
        return Logged(tenantSlug, cancelled, closed.Ok ? null : closed.Failure!.Code, closed.Value?.Status);
    }

    /// <summary>سَطرُ اللوغ: المَتجَرُ والصَفقَةُ والنَتيجَة — لا مِفتاحٌ ولا رابِطُ فاتورَة.</summary>
    private string? Logged(string tenantSlug, Deal cancelled, string? code, string? status = null)
    {
        _log.LogInformation("[gateway] close invoice {Slug} deal {Deal} ← {Outcome}",
            tenantSlug, cancelled.Id.ToString("D"), code ?? $"cancelled at the gateway (status {status ?? "—"})");
        return code;
    }
}
