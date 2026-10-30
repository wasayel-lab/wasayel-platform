using System.Security.Cryptography;
using ACommerce.Kit.Payments;
using ACommerce.Platform.Providers;
using ACommerce.Templates.Customer.Marketplace.Services.Providers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Wolverine;

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

    /// <summary>الصَفَقاتُ الَّتي أَلغَتها المَنصَّةُ <b>في هذا التَحَقُّق</b> لِأَنّ مُهلَةَ دَفعِ فاتورَتِها انتَهَت (‏2026-10-29، قَرارُ المالِك د5 (٤)) — فَيُخبَرُ
    /// مُشتَريها وكُلُّ بائِعٍ فيها مَرَّة (<c>DealNotices.NotifyPaymentExpiredAsync</c>). وفارِغَةٌ لِكُلِّ نَتيجَةٍ سِواها.
    /// (‏2026-10-29، مُراجَعَةُ تَيّارِ S3 الأولى — ولا يُعادُ حَرفٌ فَوقَه: وفي رِسالَةِ المُهلَةِ المُجَدوَلَةِ أَيضاً — <see cref="GatewayPayments.ExpireAsync"/>.)</summary>
    public IReadOnlyList<Guid> Expired { get; init; } = Array.Empty<Guid>();
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
    /// <para><b>بابُ الدُخولِ مِن صَفحَةِ العَودَةِ بِوِجهَتِه</b> (‏2026-10-13، مُراجَعَةُ بَوّابَةِ المَوجَة 3b الثانِيَة، G5 (xvii-g)؛ تَوصيفُ «اليَوم»
    /// <c>d066a26</c>): <c>login?returnUrl=</c> ومَسارُ صَفحَةِ العَودَةِ المُطلَقُ بِالصَفقَةِ المَقروءَةِ — بِـ<see cref="DealIdOf"/> و<see cref="PathFor"/>
    /// نَفسَيهِما، فَلا يَعبُرُ إلّا مُعَرِّفٌ بِصيغَةِ <c>N</c> — مُرَمَّزاً (نَسَقُ <c>GatedPage</c>). كانَ «login» حَرفاً فَيَضيعُ <c>?deal=</c> عَلى مُشتَرٍ
    /// انتَهَت جَلسَتُه. والوِجهَةُ بَعدَ فَكِّ التَرميزِ مَحَلِّيَّةٌ دائِماً (<c>LocalRedirect.IsLocal</c>).</para>
    /// </summary>
    public static string LoginPathFor(string slug, string? dealRef)
        => "login?returnUrl=" + Uri.EscapeDataString($"/{slug}/{PathFor(DealIdOf(dealRef), null)}");

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
    /// <remarks>(‏2026-10-13، G5 (xvii-g) — ولا يُعادُ حَرفٌ فَوقَ ما سَبَق: ولا لِصَفقَةٍ رَأى التَحَقُّقُ فاتورَتَها مُغلَقَة — <see cref="IsClosed"/>.)</remarks>
    public static string? PayUrlOf(Deal deal)
        => deal.Status == DealStatus.Active
           && !IsClosed(deal)
           && deal.Refs.TryGetValue(GatewayPayments.InvoiceUrlRef, out var url)
           && Uri.TryCreate(url, UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttps || u.Scheme == Uri.UriSchemeHttp)
            ? url
            : null;

    /// <summary>
    /// <para><b>أَانتَهَت مُهلَةُ دَفعِ فاتورَةِ هذِه الصَفقَةِ بِتاريخِها المَكتوب؟</b> (‏2026-10-29، مُراجَعَةُ تَيّارِ S3 الأولى؛ القاعِدَة ١٢؛ تَوصيفُ «اليَوم» <c>9f003b1</c>):
    /// <see cref="GatewayPayments.ExpiresAtOf"/> وقَد بَلَغَه «الآن» — والفاتورَةُ أُرسِلَت بِهذا التاريخِ فَالبَوّابَةُ تَمنَعُ دَفعَها بَعدَه («User will be prevented from paying the invoice
    /// once expired»). دالَّةٌ نَقِيَّةٌ تَسأَلُها الصَفحَتان: فَلا يُرسَمُ رابِطُ صَفحَةِ البَوّابَة (<see cref="PayUrlOf(Deal, DateTimeOffset)"/>) ولا «أَكمِلِ الدَّفع»، ويُقالُ أَنَّ
    /// المُهلَةَ انتَهَت وأَنَّ «تَحَقَّق» يُسَجِّلُ دَفعاً سَبَقَها أَو يُلغي الطَلَب. وصَفقَةٌ بِلا تاريخٍ (فُتِحَت قَبلَه) لا تَنتَهي هُنا — بابُها كَما كان.</para>
    /// </summary>
    public static bool WindowClosed(Deal deal, DateTimeOffset now)
        => GatewayPayments.ExpiresAtOf(deal) is { } at && now >= at;

    /// <summary><b>رابِطُ صَفحَةِ الدَفعِ بِالسّاعَة</b> — <see cref="PayUrlOf(Deal)"/> وَلا رابِطَ بَعدَ انتِهاءِ المُهلَة (<see cref="WindowClosed"/>). <b>تَقرَؤُه الصَفحَةُ</b>؛ والأَوَّلُ
    /// قَواعِدُ الحالِ بِلا ساعَة، يَبني عَلَيه هذا.</summary>
    public static string? PayUrlOf(Deal deal, DateTimeOffset now)
        => WindowClosed(deal, now) ? null : PayUrlOf(deal);

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

    /// <summary>أَرَأى تَحَقُّقٌ فاتورَةَ هذِه الصَفقَةِ مُغلَقَةً عِندَ البَوّابَة؟ (<see cref="GatewayPayments.InvoiceClosedRef"/>، ‏2026-10-13) — يَقرَؤُه
    /// <see cref="PayUrlOf"/> فَلا يُرسَمُ الرابِط، وصَفحَةُ العَودَةِ فَتَقولُ رَمزَه بِلا <c>?err=</c>.</summary>
    public static bool IsClosed(Deal deal) => deal.Refs.ContainsKey(GatewayPayments.InvoiceClosedRef);

    /// <summary>
    /// <para><b>عَشرُ نَقَراتِ تَحَقُّقٍ في السّاعَةِ لِكُلِّ مُشتَرٍ</b> (‏2026-10-29، قَرارُ المالِك د5 (٥) «ب»: «بالحصّة القائمة نفسها»، OWASP API4 «الاستِهلاكُ غَيرُ المَحدودِ
    /// لِلمَوارِد»؛ تَوصيفُ «اليَوم» <c>23fae63</c>): كُلُّ نَقرَةٍ تَفُكُّ مِفتاحَ المَتجَرِ وتُنادي البَوّابَةَ بِه، فَيُحَدُّ البابُ بِحِصَّةِ الأَفعالِ القائِمَة
    /// (<c>AuthHandlers.TryConsumeActionQuota</c> — عَشرٌ في السّاعَة، لا رَقَمٌ جَديد ولا آلِيَّةٌ جَديدَة) بِمِفتاحِ المَتجَرِ والمُشتَري. الرَمزُ في الشَريطِ بِنَصِّه.</para>
    /// </summary>
    public const string VerifyRateLimited = "gateway_verify_rate_limited";

    /// <summary>
    /// <para><b>انتَهَت مُهلَةُ دَفعِ الفاتورَةِ فَأُلغِيَ الطَلَبُ كُلُّه</b> (‏2026-10-29، قَرارُ المالِك د5 (٤) «ب»: «يُلغى الطلب كله آليًّا، وتُلغى الفاتورة عند ميسر إن بقيت
    /// مفتوحة، ويُخبَر المشتري وكل بائع فيه»؛ تَوصيفُ «اليَوم» <c>23fae63</c>): رَمزُ التَحَقُّقِ الَّذي أَلغى — <see cref="GatewayVerifyResult.Expired"/> —
    /// فَتَقولُه صَفحَةُ العَودَةِ بِنَصِّه في الشَريط.</para>
    /// </summary>
    public const string InvoiceExpired = "gateway_invoice_expired";

    /// <summary>مِفتاحُ حِصَّةِ نَقَراتِ التَحَقُّق — المَتجَرُ والمُشتَري بِصيغَةِ <c>D</c>، نَسَقُ <c>VendorShipping.RegisterAttemptKey</c>.</summary>
    public static string VerifyQuotaKey(string slug, Guid payer) => $"{slug}|gateway-verify:{payer:D}";

    /// <summary>رَدُّ النَقرَةِ الَّتي تَجاوَزَت الحِصَّة — إلى صَفحَةِ العَودَةِ بِصَفقَتِها ورَمزِها، بِلا مِفتاحٍ يُفَكُّ ولا نِداء.</summary>
    public static GatewayVerifyResult RateLimited(string? dealRef) => new(DealIdOf(dealRef), null, VerifyRateLimited);

    /// <summary>بادِئَةُ مَفاتيحِ الشَريطِ — اصطِلاحُ القامُوسِ ثَلاثَةُ مَقاطِع (<c>LocaleValidator.KeyPattern</c>)، فَالرَمزُ في الثالِث.</summary>
    public const string TextPrefix = "pay.return.err_";

    /// <summary>كُلُّ رَمزٍ قَد يَبلُغُ الصَفحَةَ — سُلطَةُ الدافِع، ثُمَّ مَعجَمُ فَشَلِ العَميلِ الخُماسيّ، ثُمَّ رُموزُ بَوّابَةِ البُرهانِ
    /// الخَمسَة. ثَلاثَةَ عَشَرَ لا رابِعَ عَشَر. (‏2026-10-13، G5 (xvii-g) — ولا يُعادُ حَرفٌ فَوقَه: والبُرهانُ سِتَّة — أَربَعَةَ عَشَر.)</summary>
    /// <remarks>(‏2026-10-29، قَرارُ المالِك د5 — ولا يُعادُ حَرفٌ فَوقَه: ومَعَها <see cref="VerifyRateLimited"/> — خَمسَةَ عَشَر.)</remarks>
    /// <remarks>(‏2026-10-29، قَرارُ المالِك د5 (٤) — ولا يُعادُ حَرفٌ فَوقَه: و<see cref="InvoiceExpired"/> — سِتَّةَ عَشَر.)</remarks>
    /// <remarks>(‏2026-10-30، تَدقيقُ الموجَة ١١ C-PAY-1 — ولا يُعادُ حَرفٌ فَوقَه: ومَعجَمُ العَميلِ سِتَّةٌ بِـ<see cref="MoyasarFailures.RateLimited"/> — سَبعَةَ عَشَر.)</remarks>
    public static readonly IReadOnlyList<string> Codes =
        DealPayAuthorization.All
            .Concat(MoyasarFailures.All)
            .Concat(GatewayPaymentProof.Codes)
            .Append(VerifyRateLimited)
            .Append(InvoiceExpired)
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

    /// <summary><b>فاتورَةُ الصَفقَةِ أُغلِقَت عِندَ البَوّابَة</b> — حالُها كَما قالَتها (‏2026-10-13، G5 (xvii-g)): يَكتُبُه <see cref="VerifyAsync"/> مَرَّةً
    /// عَلى صَفَقاتِ الفاتورَةِ حينَ يَرى <see cref="GatewayPaymentProof.InvoiceClosed"/>، ويَقرَؤُه <see cref="GatewayReturn.IsClosed"/> فَلا يُرسَمُ رابِطُها.</summary>
    public const string InvoiceClosedRef = "gateway_invoice_closed";

    /// <summary>
    /// <para><b>مُدَّةُ الفاتورَةِ غَيرِ المَدفوعَة: ‏24 ساعَةً مِن إنشائِها</b> (‏2026-10-29، قَرارُ المالِك د5 (٣): «تنتهي الفاتورة غير المدفوعة بعد 24 ساعة من إنشائها،
    /// ويُرسل تاريخ انتهائها مع الفاتورة إلى ميسر. هذا افتراض المنصّة»): رَقَمُ أَمانٍ تَشغيلِيٌّ قَرَّرَه المالِك لا رَقَمٌ اقتِصادِيّ (القاعِدَة ١٦)، ومُدَّةُ
    /// الإمساكِ سِياسَةُ مَتجَرٍ يُبنى إعدادُها حينَ يَطلُبُه أَوَّلُ مَتجَر. والإنشاءُ عِندَ إتمامِ الشِراءِ (<see cref="OpenInvoiceAsync"/>) — فَالمُدَّةُ مِن الطَلَب.</para>
    /// </summary>
    public static readonly TimeSpan InvoiceLifetime = TimeSpan.FromHours(24);

    /// <summary><b>تاريخُ انتِهاءِ الفاتورَةِ كَما أُرسِلَ إلى البَوّابَة</b> (‏2026-10-29، قَرارُ المالِك د5 (٣)) — يَكتُبُه <see cref="OpenInvoiceAsync"/> عَلى كُلِّ صَفقَةٍ مِنها
    /// بِصيغَةِ <see cref="MoyasarClient.ExpiryText"/> نَفسِها، فَيُقرَأُ بِلا نِداء. داخِلِيٌّ: لا يُرسَمُ لِلطَرَفَين (لا تَسمِيَةَ لَه في <c>DealRefLabels</c>).</summary>
    public const string InvoiceExpiresAtRef = "gateway_invoice_expires_at";

    /// <summary>تاريخُ انتِهاءِ فاتورَةِ الصَفقَةِ كَما كُتِب (<see cref="InvoiceExpiresAtRef"/>) — و<c>null</c> لِصَفقَةٍ بِلا تاريخٍ (فُتِحَت فاتورَتُها قَبلَ
    /// ‏2026-10-29) أَو بِنَصٍّ لا يُقرَأ. بِثَقافَةٍ ثابِتَةٍ وبِالتَوقيتِ العالَمِيّ — صيغَةُ <see cref="MoyasarClient.ExpiryText"/>.</summary>
    public static DateTimeOffset? ExpiresAtOf(Deal deal)
        => deal.Refs.TryGetValue(InvoiceExpiresAtRef, out var at)
           && DateTimeOffset.TryParse(at, System.Globalization.CultureInfo.InvariantCulture,
                  System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out var parsed)
            ? parsed : null;

    /// <summary>
    /// <para><b>أَانتَهَت مُهلَةُ دَفعِ فاتورَةِ هذِه الصَفقَة؟</b> (‏2026-10-29، قَرارُ المالِك د5 (٤)) — دالَّةٌ نَقِيَّةٌ عَلى حالِ الفاتورَةِ كَما جَلَبَها التَحَقُّقُ
    /// والصَفقَةِ و«الآن»: البَوّابَةُ تَقولُ <see cref="MoyasarClient.ExpiredStatus"/>، <b>أَو</b> الفاتورَةُ ما زالَت <see cref="MoyasarClient.OpenStatus"/> وقَد جاوَزَ
    /// «الآنَ» تاريخَها المَكتوب (<see cref="ExpiresAtOf"/>) — فَتُلغى عِندَ البَوّابَةِ بِإلغاءِ آخِرِ صَفقَةٍ عَلَيها (<c>GatewayInvoiceCloser</c>)، «إن بقيت مفتوحة».</para>
    /// <para><b>ولا تَصدُقُ لِسِواهُما</b>: <c>paid</c> يُثبِتُه البُرهانُ قَبلَها، والمُغلَقَةُ بِغَيرِ الانتِهاءِ (<c>canceled</c>، <c>voided</c>، <c>refunded</c>) والمُعَلَّقَةُ
    /// (<c>on_hold</c>) والمَرفوضَة (<c>failed</c>) لا تُلغي طَلَباً بِاسمِ المُهلَة — قَد يَكونُ في الطَريقِ مالٌ لا تَشرَحُه الوَثيقَة.</para>
    /// </summary>
    public static bool ExpiryDue(string invoiceStatus, Deal deal, DateTimeOffset now)
        => string.Equals(invoiceStatus, MoyasarClient.ExpiredStatus, StringComparison.Ordinal)
           || (string.Equals(invoiceStatus, MoyasarClient.OpenStatus, StringComparison.Ordinal) && ExpiresAtOf(deal) is { } at && now >= at);

    /// <summary>
    /// <para><b>المُلغي حينَ تَنتَهي مُهلَةُ الدَفع: المَنصَّة</b> (‏2026-10-29، قَرارُ المالِك د5 (٤): «يُلغى الطلب كله آليًّا») — لا طَرَفٌ ولا مُستَخدِم: بِلا مُعَرِّفٍ
    /// (<see cref="Guid.Empty"/>) وبِاسمِ <see cref="DealPatternCatalog.PlatformActor"/>، فَتُسَمّيه صَفحَتا الصَفقَةِ مِن القامُوس (<see cref="DealEvent.IsByPlatform"/>)،
    /// ويُخبَرُ المُشتَري والبائِعُ مَعاً (لَيسَ أَحَدَهُما). وسُلطَتُه سُلطَةُ الإدارَة (<see cref="DealCanceller.IsStoreAdmin"/>) — ولا يَبنيه إلّا التَحَقُّقُ حينَ تَصدُقُ
    /// <see cref="ExpiryDue"/> عَلى فاتورَةٍ جَلَبَها بِمِفتاحِ المَتجَر.</para>
    /// </summary>
    public static readonly DealCanceller ExpiryCanceller = new(Guid.Empty, DealPatternCatalog.PlatformActor, IsStoreAdmin: true);

    /// <summary>مِفتاحُ سَبَبِ الإلغاءِ عِندَ انتِهاءِ المُهلَة — يُكتَبُ في <c>CancelReason</c> مِن القامُوس (القاعِدَة ١١).</summary>
    public const string ExpiryReasonKey = "pay.invoice.cancel_expired";

    /// <summary>
    /// <para><b>عَلامَةُ «أُخبِرَ بِالمُهلَة»</b> (‏2026-10-30، تَدقيقُ الموجَة ١١ CO2 — المُرَحَّلُ [S3·١]): يَكتُبُها <c>ClaimExpiryNoticesAsync</c> عَلى صَفقَةٍ أَلغَتها المُهلَةُ
    /// <b>قَبلَ</b> أَن يُخبَرَ طَرَفاها — مُطالَبَةٌ بِالإشعارِ مَرَّةً (نَسَقُ المُطالَبَةِ في ‏ADR-036 الخُطوَة 2): فَرِسالَةٌ تُعادُ بَعدَ الإشعارِ لا تُخبِرُ ثانِيَة، وإعادَتُها بَعدَ
    /// انقِطاعٍ بَينَ الإلغاءِ والإشعارِ تُخبِرُ مَرَّة. داخِلِيٌّ: لا يُرسَمُ لِلطَرَفَين.</para>
    /// <para>(‏2026-10-30، جَولَةُ الإصلاحِ الأولى لِلمَسار F3؛ تَوصيفُه <c>8e19697</c> — ولا يُعادُ حَرفٌ فَوقَ ما سَبَق): <b>«قَبلَ» صارَ «بَعدَ»</b> — تُكتَبُ العَلامَةُ عَلى
    /// كُلِّ صَفقَةٍ <b>بَعدَ</b> أَن يُحفَظَ إشعارُ بائِعِها ومُشتَريها (<see cref="MarkExpiryToldAsync"/>، يُناديه <see cref="DealNotices.NotifyPaymentExpiredAsync"/> لِكُلِّ صَفقَةٍ
    /// بَعدَ إشعاراتِها). فَالمُطالَبَةُ قَبلَ الإرسالِ كانَت «مَرَّةً عَلى الأَكثَر»: إشعارٌ يَرمي فَتُعيدُه السِياسَةُ والإعادَةُ تَجِدُ الصَفَقاتِ مُطالَباً بِها فَلا تُخبِرُ أَحَداً.
    /// واليَومَ «مَرَّةً عَلى الأَقَلّ» بِمَنعِ تَكرارٍ لِكُلِّ صَفقَة: ما لَم تُكتَب عَلامَتُه يُعادُ إشعارُه، وما كُتِبَت لا يُعاد — وآثارُ الرَسائِلِ يَحتَمِلُها التَكرارُ (القاعِدَة ٥).
    /// و«ما يَنتَظِرُ الإشعار» لا يَحتاجُ عَلامَةً ثانِيَة: سَطرُ الإلغاءِ بِاسمِ المَنصَّةِ وسَبَبُ المُهلَةِ (<see cref="CancelledByExpiry"/>) يُكتَبانِ في كِتابَةِ الإلغاءِ نَفسِها.</para>
    /// </summary>
    public const string ExpiryToldRef = "gateway_expiry_told";

    /// <summary>أَأَلغَت المُهلَةُ هذِه الصَفقَة؟ — سَطرُ إلغائِها الأَخيرُ بِاسمِ المَنصَّةِ (<see cref="DealEvent.IsByPlatform"/>) وسَبَبُها نَصُّ <see cref="ExpiryReasonKey"/> بِأَيِّ لُغَةٍ في
    /// القامُوس (التَحَقُّقُ يَكتُبُه بِلُغَةِ المُشتَري، والرِسالَةُ بِالعَرَبِيَّة) — فَلا يَلتَبِسُ بِإلغاءِ إغلاقِ الحِساب (فاعِلُه المَنصَّةُ أَيضاً وسَبَبُه غَيرُ هذا). دالَّةٌ نَقِيَّة.</summary>
    public static bool CancelledByExpiry(Deal deal)
        => deal.Status == DealStatus.Cancelled
           && deal.CancelReason is { } reason
           && ACommerce.Platform.I18n.LocaleCatalog.Languages.Any(lang => ACommerce.Platform.I18n.LocaleCatalog.Find(lang, ExpiryReasonKey) == reason)
           && deal.Timeline.LastOrDefault(e => e.Action == "cancelled") is { } line && DealEvent.IsByPlatform(line);

    /// <summary>أَلغَتها المُهلَةُ ولَم يُطالَب بِإشعارِها بَعد (<see cref="ExpiryToldRef"/>) — الشَرطُ الَّذي تُكتَبُ بِه العَلامَةُ عَلى المَخزونِ داخِلَ الكِتابَة. دالَّةٌ نَقِيَّة.</summary>
    public static bool ExpiredUntold(Deal deal) => CancelledByExpiry(deal) && !deal.Refs.ContainsKey(ExpiryToldRef);

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
    /// <remarks><b>وعَلامَةُ الوِلادَةِ تَكفي — لا يَنتَظِرُ الحارِسُ الفاتورَة</b> (‏2026-10-13، مُراجَعَةُ بَوّابَةِ المَوجَة 3b، G5 (xvi-g)؛
    /// ‏ADR-032 §٨‑ج.8 مُلحَقاً): كانَ يَسأَلُ مَرجِعَ الفاتورَةِ وَحدَه، فَصَفقَةٌ وُلِدَت لِتُدفَعَ بِفاتورَةِ البَوّابَةِ ولَم يُلحَق بِها مَرجِعُها —
    /// إلغاءٌ رُدَّ بِـ<c>deal_changed_meanwhile</c> بَعدَ فَشَلِ الفَتح، أَو عُطلٌ بَينَ الوِلادَةِ والإلحاق — دَخَلَت «دَفع» ثُمَّ «شَحن» بِلا مال
    /// (تَوصيفُه <c>64b6e5c</c>). فَصارَ يَصدُقُ أَيضاً لِما يَحمِلُ <see cref="BornForGateway"/>.</remarks>
    public static bool AwaitsPayment(Deal deal)
        => (HasInvoice(deal) || BornForGateway(deal)) && DealsPolicy.Next(deal) == DealStage.Paid && !DealsService.IsCaptured(deal);

    /// <summary>
    /// <para><b>عَلامَةُ الوِلادَةِ عَلى طَريقِ بَوّابَةِ المَتجَر</b> (‏2026-10-13، G5 (xvi-g)): مِفتاحٌ في <c>Deal.Attributes</c> يَكتُبُه
    /// <c>/checkout/submit</c> <b>في نِداءِ الوِلادَةِ نَفسِه</b> (<see cref="MarkedAtBirth"/>) لِكُلِّ صَفقَةٍ سَتُدفَعُ بِفاتورَةِ البَوّابَة — قَبلَ
    /// أَيِّ نِداءٍ ولا إلحاق — فَلا تَخرُجُ مِن الوِلادَةِ صَفقَةٌ عَلى هذا الطَريقِ إلّا وحارِسُ «دَفع» يَعرِفُها. ولا تُرسَمُ لِلطَرَفَين.</para>
    /// </summary>
    public const string PayViaAttribute = "pay_via";

    /// <summary>قيمَةُ <see cref="PayViaAttribute"/> الوَحيدَة: «بَوّابَةُ المَتجَرِ بِمِفتاحِه» — والغِيابُ كُلُّ طَريقٍ سِواه (الحَجزُ عِندَ مُزَوِّدِ النُسخَة، والنَقد).</summary>
    public const string PayViaStoreGateway = "store_gateway";

    /// <summary>أَوُلِدَت الصَفقَةُ لِتُدفَعَ بِفاتورَةِ بَوّابَةِ المَتجَر؟</summary>
    public static bool BornForGateway(Deal deal)
        => deal.Attributes.TryGetValue(PayViaAttribute, out var via) && string.Equals(via, PayViaStoreGateway, StringComparison.Ordinal);

    /// <summary>سِماتُ الوِلادَةِ كَما بَناها المُنادي، ومَعَها <see cref="PayViaAttribute"/> حينَ <paramref name="viaGateway"/> — والقاموسُ نَفسُه
    /// يُعادُ فَيُمَرَّرُ إلى <c>StartAsync</c> في السَطرِ نَفسِه. وبِلا البَوّابَةِ لا مِفتاحَ يُضاف: صَفَقاتُ الطُرُقِ الأُخرى كَما كانَت بِحَرفِها.</summary>
    public static Dictionary<string, string> MarkedAtBirth(Dictionary<string, string> attributes, bool viaGateway)
    {
        if (viaGateway) attributes[PayViaAttribute] = PayViaStoreGateway;
        return attributes;
    }

    /// <summary>
    /// <para><b>أَقَبَضَت بَوّابَةُ المَتجَرِ ثَمَنَ هذِه الصَفقَة؟</b> — مَرجِعُ الدَفعِ هُوَ الفاتورَةُ المُسَجَّلَةُ عَلَيها (يَكتُبُهُما التَأكيد).
    /// يَقرَؤُه الإلغاءُ فَلا يُنادي مُزَوِّدَ النُسخَةِ بِاستِرجاعِ مالٍ في حِسابِ المَتجَر.</para>
    /// </summary>
    public static bool CollectedByGateway(Deal deal)
        => HasInvoice(deal)
           && deal.Refs.TryGetValue("payment_id", out var paymentId)
           && string.Equals(paymentId, deal.Refs[GatewayPaymentProof.InvoiceRef], StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// <para><b>أَتَدخُلُ الصَفقَةُ «دَفع» الآنَ بِاسمِ المَنصَّة؟</b> (‏2026-10-29، قَرارُ المالِك د4 «ب»: «تُدخَلُ مرحلة «دفع» آليًّا متى سُجّل الدفع، باسم المنصّة»؛
    /// تَوصيفُ «اليَوم» <c>36fd7b8</c>) — قائِمَة، وتاليها «دَفع»، وقُبِضَ ثَمَنُها (<see cref="DealsService.IsCaptured"/>) <b>بِبَوّابَةِ المَتجَرِ نَفسِها</b>
    /// (<see cref="CollectedByGateway"/>). دالَّةٌ نَقِيَّةٌ واحِدَةٌ يَسأَلُها كاتِبُ الدُخول (<see cref="DealsService.EnterPaidOnRecordAsync"/>) عَلى ما قَرَأَه في جَلسَتِه:
    /// لَحظَةَ التَحَقُّق، وبَعدَ كُلِّ تَقَدُّمٍ يُبلِغُ الصَفقَةَ ما قَبلَ «دَفع». <b>ولا تَصدُقُ لِحَجزِ مُزَوِّدِ النُسخَة</b> — هُناكَ دُخولُ «دَفع» هُوَ القَبضُ نَفسُه
    /// ويُدخِلُها المُشتَري؛ ولا لِمُلغاةٍ أَو في نِزاع.</para>
    /// </summary>
    public static bool EntersPaidOnRecord(Deal deal)
        => deal.Status == DealStatus.Active && DealsPolicy.Next(deal) == DealStage.Paid
           && DealsService.IsCaptured(deal) && CollectedByGateway(deal);

    private readonly DealsService _deals;
    private readonly TenantProviderService _providers;
    private readonly ProviderVault _vault;
    /// <summary>عَميلُ البَوّابَة — و<c>null</c> حينَ لا يُسَجِّلُه المُضيف (‏ADR-032 الخُطوَة 8، ‏2026-10-12): مُدخَلُ «(ج) العَميلُ مُسَجَّل»
    /// في <see cref="CardFactsAsync"/> مَقيساً مِن الحَقنِ نَفسِه لا مِن دَعوى؛ وبِدونِه لا فاتورَةَ ولا جَلبَ — <see cref="MoyasarFailures.NotConfigured"/>.</summary>
    private readonly MoyasarClient? _client;
    private readonly ILogger<GatewayPayments> _log;
    /// <summary>ساعَةُ «الآن» لِتاريخِ انتِهاءِ الفاتورَة (‏2026-10-29، قَرارُ المالِك د5 (٣)) — نَسَقُ <c>DealShipping</c>: <see cref="TimeProvider.System"/>
    /// ما لَم يُمَرِّر الاختِبارُ ساعَتَه.</summary>
    private readonly TimeProvider _clock;
    /// <summary>حافِلَةُ رَسائِلِ Wolverine — يُجَدوِلُ بِها فَتحُ الفاتورَةِ إلغاءَ طَلَبِها عِندَ انتِهاءِ مُهلَتِها (‏2026-10-29، قَرارُ المالِك د5 (٤)؛ ‏ADR-046).
    /// و<c>null</c> حينَ لا يُسَجِّلُها المُضيف (عالَمُ الاختِبار) — فَلا جَدوَلَةَ، ويُقالُ ذلكَ في اللوغ.</summary>
    private readonly IMessageBus? _bus;

    public GatewayPayments(DealsService deals, TenantProviderService providers, ProviderVault vault, MoyasarClient? client = null,
        ILogger<GatewayPayments>? log = null, TimeProvider? clock = null, IMessageBus? bus = null)
    {
        _deals = deals;
        _providers = providers;
        _vault = vault;
        _client = client;
        _log = log ?? NullLogger<GatewayPayments>.Instance;
        _clock = clock ?? TimeProvider.System;
        _bus = bus;
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
    /// <remarks>(‏2026-10-29، قَرارُ المالِك د5 (٤) «ب» — ولا يُعادُ حَرفٌ فَوقَ ما سَبَق: <b>وفاتورَةٌ انتَهَت مُهلَتُها يُلغى طَلَبُها كُلُّه</b> بِاسمِ المَنصَّةِ
    /// (<see cref="ExpiryDue"/>، <see cref="ExpiryCanceller"/>) بِكاتِبِ «إلغاءِ الطَلَبِ كُلِّه» القائِم، ويَعودُ <see cref="GatewayReturn.InvoiceExpired"/> ومَعَه
    /// <see cref="GatewayVerifyResult.Expired"/> لِيُخبِرَ المُنادي المُشتَريَ وكُلَّ بائِع. <b>وكَسولٌ لا مُجَدوَل</b>: يَقَعُ في أَوَّلِ تَحَقُّقٍ بَعدَ الانتِهاء — لا مُؤَقِّتَ
    /// ولا خِدمَةَ خَلفِيَّة (‏ADR-032 §٩‑٧). و<paramref name="l"/> لِنَصِّ السَبَب؛ وبِدونِه فَالعَرَبِيَّة — اللُغَةُ الإلزامِيَّة.)</remarks>
    /// <remarks>(‏2026-10-29، مُراجَعَةُ تَيّارِ S3 الأولى — ولا يُعادُ حَرفٌ فَوقَ ما سَبَق: «وكَسولٌ لا مُجَدوَل» لَم يَعُد — فَتحُ الفاتورَةِ يُجَدوِلُ رِسالَةً لِتاريخِ
    /// انتِهائِها (<see cref="GatewayInvoiceExpiryDue"/> ← <see cref="ExpireAsync"/>، ‏ADR-046)، فَيُلغى طَلَبُ مَن لا يَعود؛ والتَحَقُّقُ يُلغي إن سَبَقَها،
    /// والتَسوِيَةُ بَعدَ الجَلبِ لِلاثنَينِ في <see cref="SettleAsync"/>.)</remarks>
    public async Task<GatewayVerifyResult> VerifyAsync(
        string tenantSlug, DealPayer by, string? dealRef, ACommerce.Platform.I18n.L? l = null, CancellationToken ct = default)
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
        var invoiceDeals = await InvoiceDealsAsync(tenantSlug, deal, invoice, by.ActorId, ct);
        return await SettleAsync(tenantSlug, deal, invoice, invoiceDeals, l, ct);
    }

    /// <summary>
    /// <para><b>انتِهاءُ مُهلَةِ الفاتورَةِ مُجَدوَلاً — ما يَسلُكُه مُعالِجُ <see cref="GatewayInvoiceExpiryDue"/></b> (‏2026-10-29، مُراجَعَةُ تَيّارِ S3 الأولى عَلى قَرارِ
    /// المالِك د5 (٤) «ب»؛ تَوصيفُ «اليَوم» <c>17a9a19</c>؛ ‏ADR-046): الصَفقَةُ المَرجِعُ ← المِفتاحُ يُفَكُّ ← الفاتورَةُ تُجلَب ← صَفَقاتُها مِن صَفَقاتِ مُشتَريها ← ثُمَّ
    /// <b>التَسوِيَةُ نَفسُها الَّتي يَسلُكُها <see cref="VerifyAsync"/></b>: دَفعٌ ثَبَتَ يُكتَبُ (ولا يُكتَبُ مَرَّتَين)، وما حانَت مُهلَتُه يُلغى كُلُّه بِاسمِ المَنصَّة
    /// (<see cref="ExpiryDue"/>، <see cref="ExpiryCanceller"/>)، وما سِواهُما لا يُكتَبُ فيه شَيء.</para>
    /// <para><b>ولا سُلطَةَ دافِعٍ هُنا</b>: لا جَلسَةَ ولا طَلَب — الحارِسُ أَنَّ الرِسالَةَ لا يُجَدوِلُها إلّا فَتحُ الفاتورَة، والفاتورَةُ تُجلَبُ بِمِفتاحِ المَتجَر،
    /// والصَفَقاتُ مِن صَفَقاتِ مُبادِرِ المَرجِعِ وَحدَه (نَسَقُ <see cref="ConfirmNoticeAsync"/>). وصَفقَةٌ غائِبَةٌ أَو بِلا فاتورَةٍ رَمزُها بِلا نِداء.</para>
    /// <para><b>وفَشَلُ الجَلبِ رَمزٌ في اللوغِ لا إعادَة</b>: لا رَقَمَ لِمُحاوَلاتٍ في التَوثيق (القاعِدَة ١٦)، وزِرُّ «تَحَقَّق» باقٍ لِلمُشتَري؛ والسُطورُ مُعلَنَةٌ في ‏ADR-046 §٣.</para>
    /// </summary>
    /// <remarks>(‏2026-10-30، تَدقيقُ الموجَة ١١ CO2 — المُرَحَّلُ [S3·١]؛ تَوصيفُه <c>1e65236</c> — ولا يُعادُ حَرفٌ فَوقَ ما سَبَق): <b>والرِسالَةُ المُعادَةُ تُخبِرُ مَرَّةً
    /// بِما أَلغَته قَبلَ انقِطاعِها</b> — قَبلَ الجَلب، صَفَقاتُ الفاتورَةِ كَما سَمَّتها مَراجِعُها (<see cref="GatewayInvoiceShare.DealsOn"/>) يُطالَبُ بِإشعارِ ما أَلغَته المُهلَةُ ولَم يُطالَب
    /// بِه (<see cref="ExpiredUntold"/>)، ويُضَمُّ إلى <see cref="GatewayVerifyResult.Expired"/> أَيّاً كانَ ما أَجابَت بِه البَوّابَة. كانَت الإعادَةُ لا تَجِدُ ما «أُلغِيَ الآن» فَلا
    /// يُخبَرُ المُشتَري ولا البائِعُ أَبَداً. وما أُخبِرَ بِه لا يُعادُ: العَلامَةُ تُكتَبُ قَبلَ الإشعار.</remarks>
    /// <remarks>(‏2026-10-30، جَولَةُ الإصلاحِ الأولى لِلمَسار F3؛ تَوصيفُه <c>4bc251c</c> — ولا يُعادُ حَرفٌ فَوقَ ما سَبَق): <b>«وفَشَلُ الجَلبِ رَمزٌ في اللوغِ لا إعادَة» لَم يَعُد</b> —
    /// الدالَّةُ تُرجِعُ رَمزَ العَميلِ كَما كانَت (صُفوفُ <c>The_scheduled_expiry_cancels_nothing_and_answers_its_code_for_every_other_case</c> بِحَرفِها)، ومُعالِجُ الرِسالَةِ يَرمي بِه
    /// (<see cref="GatewayInvoiceExpiry.FailsTheMessage"/> ← <see cref="GatewayInvoiceExpiryFetchFailed"/>) فَتُعادُ ثُمَّ تَموتُ ويُخبَرُ صاحِبُ المَتجَر. والرَقَمُ قَرارٌ تِقنِيٌّ لا رَقَمُ مُنتَج
    /// (<see cref="GatewayInvoiceExpiry.RetryCooldowns"/>). وزِرُّ «تَحَقَّق» لا يَرمي: مُناديه المُشتَري وصَفحَتُه تَقولُ الرَمز.</remarks>
    public async Task<GatewayVerifyResult> ExpireAsync(
        string tenantSlug, Guid dealId, ACommerce.Platform.I18n.L? l = null, CancellationToken ct = default)
    {
        var deal = await _deals.LoadAsync(tenantSlug, dealId, ct);
        if (deal is null)
            return Logged(tenantSlug, new(dealId, null, DealPayAuthorization.DealNotFound));
        if (!HasInvoice(deal))
            return Logged(tenantSlug, new(deal.Id, null, DealPayAuthorization.InvoiceMissing));

        // (‏2026-10-30، جَولَةُ الإصلاحِ الأولى لِلمَسار F3؛ تَوصيفُه `8e19697` — ولا يُعادُ حَرفٌ فَوقَه): قِراءَةٌ لا مُطالَبَة — ما لَم يُخبَر بِه يُرجَعُ حَتّى تُكتَبَ عَلامَتُه بَعدَ إشعارِه.
        var recovered = await ExpiredUntoldAsync(tenantSlug, (GatewayInvoiceShare.DealsOn(deal) ?? Array.Empty<Guid>()).Prepend(deal.Id), ct);
        GatewayVerifyResult With(GatewayVerifyResult r)
            => recovered.Count == 0 ? r : r with { Expired = recovered.Concat(r.Expired).Distinct().ToArray() };

        if (_client is null || await SecretKeyAsync(_providers, _vault, _log, tenantSlug, ct) is not { } key)
            return With(Logged(tenantSlug, new(deal.Id, null, MoyasarFailures.NotConfigured)));

        var fetched = await _client.FetchInvoiceAsync(key, deal.Refs[GatewayPaymentProof.InvoiceRef], ct);
        if (!fetched.Ok)
            return With(Logged(tenantSlug, new(deal.Id, null, fetched.Failure!.Code)));

        var invoice = fetched.Value!;
        var invoiceDeals = await InvoiceDealsAsync(tenantSlug, deal, invoice, deal.InitiatorId, ct);
        return With(await SettleAsync(tenantSlug, deal, invoice, invoiceDeals, l, ct));
    }

    /// <summary>
    /// <para><b>ما أَلغَته المُهلَةُ ولَم يُخبَر بِه</b> (‏2026-10-30، جَولَةُ الإصلاحِ الأولى لِلمَسار F3 — حَلَّ مَحَلَّ <c>ClaimExpiryNoticesAsync</c>، المُطالَبَةِ قَبلَ الإرسالِ مِن
    /// <c>f97e6c7</c>): قِراءَةٌ خالِصَةٌ لِكُلِّ صَفقَةٍ تَصدُقُ عَلَيها <see cref="ExpiredUntold"/> الآن، بِلا كِتابَة — فَيُخبِرُ مُناديها بِها ثُمَّ يَسِمُ كُلّاً بَعدَ إشعاراتِها
    /// (<see cref="MarkExpiryToldAsync"/>). وما لَم يُوسَم — انقِطاعٌ أَو إشعارٌ رَمى — تُرجِعُه القِراءَةُ التالِيَة.</para>
    /// </summary>
    private async Task<IReadOnlyList<Guid>> ExpiredUntoldAsync(string tenantSlug, IEnumerable<Guid> dealIds, CancellationToken ct)
    {
        var untold = new List<Guid>();
        foreach (var id in dealIds.Distinct())
            if (await _deals.LoadAsync(tenantSlug, id, ct) is { } d && ExpiredUntold(d)) untold.Add(id);
        return untold;
    }

    /// <summary>
    /// <para><b>أُخبِرَ طَرَفا صَفقَةٍ أَلغَتها المُهلَة — تُكتَبُ عَلامَتُها</b> (‏2026-10-30، جَولَةُ الإصلاحِ الأولى لِلمَسار F3): <see cref="ExpiryToldRef"/> بِالكاتِبِ القائِم
    /// (<see cref="DealsService.AttachRefAsync(string, Guid, string, string, Func{Deal, bool}, CancellationToken)"/>) وبِشَرطِ <see cref="ExpiredUntold"/> عَلى المَخزونِ داخِلَ الكِتابَة
    /// — فَلا تُكتَبُ مَرَّتَين ولا عَلى صَفقَةٍ لَم تُلغِها المُهلَة. يُناديه <see cref="DealNotices.NotifyPaymentExpiredAsync"/> لِكُلِّ صَفقَةٍ <b>بَعدَ</b> إشعاراتِها، مِن المُعالِجِ ومِن
    /// بابِ «تَحَقَّق». و<see cref="CancellationToken.None"/> بِقاعِدَةِ <c>/ship</c>: الإشعارُ وَقَعَ فَلا يُلغى تَدوينُه.</para>
    /// </summary>
    public async Task MarkExpiryToldAsync(string tenantSlug, Guid dealId)
        => await _deals.AttachRefAsync(tenantSlug, dealId, ExpiryToldRef, "1", ExpiredUntold, CancellationToken.None);

    /// <summary>
    /// <para><b>التَسوِيَةُ بَعدَ الجَلب — مَوضِعٌ واحِدٌ لِلتَحَقُّقِ ولِلمُهلَة</b> (‏2026-10-29، مُراجَعَةُ تَيّارِ S3 الأولى — نُقِلَ مِن جِسمِ <see cref="VerifyAsync"/> بِحَرفِه
    /// وتَعليقاتِه، فَصُفوفُ التَحَقُّقِ كَما هي): البَوّابَةُ النَقِيَّةُ أَوَّلاً، ثُمَّ إمّا الكاتِبانِ (الدَفعُ و«دَفع» بِاسمِ المَنصَّة)، وإمّا الإغلاقُ والمُهلَة، وإمّا رَمزُها.</para>
    /// </summary>
    private async Task<GatewayVerifyResult> SettleAsync(
        string tenantSlug, Deal deal, MoyasarInvoice invoice, IReadOnlyList<Deal> invoiceDeals, ACommerce.Platform.I18n.L? l, CancellationToken ct)
    {
        var admitted = GatewayPaymentProof.Gate(invoice, invoiceDeals);
        if (admitted.Payment is not { } proven)
        {
            // فاتورَةٌ أُغلِقَت عِندَ البَوّابَةِ تُكتَبُ عَلى صَفَقاتِها مَرَّة — فَلا تَدعو صَفحَةُ العَودَةِ إلَيها بَعدَ اليَوم (‏2026-10-13، G5 (xvii-g)).
            if (admitted.Violation!.Code == GatewayPaymentProof.InvoiceClosed)
                foreach (var d in invoiceDeals.Where(d => !GatewayReturn.IsClosed(d)))
                    await _deals.AttachRefAsync(tenantSlug, d.Id, InvoiceClosedRef, invoice.Status, ct);
            // وانتَهَت المُهلَة: يُلغى الطَلَبُ كُلُّه بِاسمِ المَنصَّة، وتُلغى الفاتورَةُ عِندَ البَوّابَةِ إن بَقِيَت مَفتوحَة (‏2026-10-29، قَرارُ المالِك د5 (٤)).
            // كُلُّ صَفقَةٍ قائِمَةٍ سَمَّتها الفاتورَةُ تُلغى بِكاتِبِ «الطَلَبِ كُلِّه» — أَوَّلُها يُلغي أَخَواتِها المَكتوبَة، وما بَقِيَ (فاتورَةٌ بِلا مَرجِعِ أَخَوات) يُلغى بِدَورِه.
            if (ExpiryDue(invoice.Status, deal, _clock.GetUtcNow()))
            {
                var reason = l?[ExpiryReasonKey] ?? ACommerce.Platform.I18n.LocaleCatalog.Text(ACommerce.Platform.I18n.LocaleCatalog.Arabic, ExpiryReasonKey);
                var cancelled = new List<Guid>();
                foreach (var d in invoiceDeals.Where(d => d.Status == DealStatus.Active && !cancelled.Contains(d.Id)))
                {
                    // (‏2026-10-30، تَدقيقُ الموجَة ١١ C-PAY-2؛ تَوصيفُه `627c179` — ولا يُعادُ حَرفٌ فَوقَه): **وبِشَرطٍ عِندَ الكِتابَة** — ما قُبِضَ بَعدَ جَلبِ الفاتورَةِ
                    // لا يُلغى (`GatewayInvoiceClosing.Owed` يُسأَلُ في جَلسَةِ الإلغاءِ نَفسِها)، ولا رُجوعَ إلى «الصَفقَةِ وَحدَها»، وتَعارُضٌ يُعادُ مَرَّة.
                    var order = await _deals.CancelWholeInvoiceAsync(tenantSlug, d.Id, ExpiryCanceller, reason, ct, stillDue: GatewayInvoiceClosing.Owed);
                    cancelled.AddRange(order.Cancelled.Select(c => c.Id).Where(c => !cancelled.Contains(c)));
                    if (!order.Result.Ok && order.Result.Violation?.Code is not (DealCancelAuthorization.DealNotActive or DealsService.NoLongerDue))
                        _log.LogWarning("[gateway] verify {Slug}: invoice of deal {Deal} expired ← cancel {Code}",
                            tenantSlug, d.Id, order.Result.Violation?.Code);
                }
                // (‏2026-10-30، تَدقيقُ الموجَة ١١ CO2 — ولا يُعادُ حَرفٌ فَوقَه): وما أُلغِيَ يُطالَبُ بِإشعارِه قَبلَ أَن يُعادَ — فَلا يُخبِرُ المُنادي إلّا ما كَسَبَ مُطالَبَتَه.
                // (‏2026-10-30، جَولَةُ الإصلاحِ الأولى لِلمَسار F3؛ تَوصيفُه `8e19697` — ولا يُعادُ حَرفٌ فَوقَ ما سَبَق): «يُطالَبُ قَبلَ أَن يُعادَ» لَم يَعُد — ما أَلغاه هذا النِداءُ
                // يُعادُ كُلُّه (إلغاءٌ واحِدٌ يَكسِبُ الكِتابَةَ فَلا يَتَكَرَّرُ بَينَ مُنادِيَين)، ويَسِمُه المُنادي بَعدَ إشعاراتِه (`MarkExpiryToldAsync`).
                if (cancelled.Count > 0)
                    return Logged(tenantSlug, new GatewayVerifyResult(deal.Id, null, GatewayReturn.InvoiceExpired) { Expired = cancelled.ToArray() });
            }
            return Logged(tenantSlug, new(deal.Id, null, admitted.Violation!.Code));
        }

        // ─── الخُطوَة 10: الدَفعُ الثابِتُ يُكتَبُ، وما يَقِفُ قَبلَ «دَفع» يَدخُلُها بِفاعِلِها ─────────────────────────
        // الكاتِبُ يَطلُبُ ما أَخرَجَته البَوّابَةُ في تَوقيعِه، ولا يَكتُبُ عَلى ما كُتِب. ثُمَّ صَفقَةٌ تاليها «دَفع» وقَد قُبِضَت تَدخُلُها
        // **بِبَوّابَةِ الانتِقالِ نَفسِها** والمُشتَري فاعِلُها — قاعِدَةُ الكاتالوجِ (`Paid` = المُبادِر)، لا تَجاوُزٌ لِفَحصِ الفاعِل؛ وما
        // قَبلَ ذلكَ يَبقى حَتّى يَبلُغَها ويُدخِلَها المُشتَري بِزِرِّه (والقَبضُ يَتَجاوَزُها: «Captured»). ورَدُّ الانتِقالِ لا يَكسِرُ التَأكيد.
        // (‏2026-10-29، قَرارُ المالِك د4 «ب» — ولا يُعادُ حَرفٌ فَوقَ ما سَبَق: «والمُشتَري فاعِلُها» لَم يَعُد — تَدخُلُها المَنصَّةُ بِاسمِها بِالكاتِبِ الواحِد
        // `EnterPaidOnRecordAsync` وشَرطِه `EntersPaidOnRecord`، وما يَقِفُ قَبلَ ذلكَ يَدخُلُها لَحظَةَ يَبلُغُها بِالكاتِبِ نَفسِه — لا بِنَقرَةِ المُشتَري.)
        var confirmed = await _deals.ConfirmGatewayPaymentAsync(tenantSlug, proven, ct);
        foreach (var paidId in proven.DealIds)
            await _deals.EnterPaidOnRecordAsync(tenantSlug, paidId, ct);

        return Logged(tenantSlug, new GatewayVerifyResult(deal.Id, proven, null) { Confirmed = confirmed.Select(c => c.Id).ToArray() });
    }

    // ═══ إشعارُ الدَفعِ الآليُّ مِن البَوّابَة (‏2026-10-29، قَرارُ المالِك د5 (٢) «ج»؛ ‏ADR-032 §٩‑٨) ═══════════════════════════════

    /// <summary>مَسارُ الوارِدِ لِمَتجَرٍ بِعَينِه — <b>مَوضِعٌ واحِدٌ</b> يَقرَؤُه التَسجيلُ والاختِبارُ وتَعريفُ المُزَوِّد (<c>webhook.path</c>)؛ والنُقطَةُ تَكتُبُه
    /// حَرفاً في <c>TenantGatewayEndpoints</c>، والاختِبارُ يَحمَرُّ إن افتَرَقوا — نَسَقُ <c>OtoShippingEndpoints.OrderStatusPath</c>.</summary>
    public const string NoticePath = "/{slug}/payments/notify";

    /// <summary>عُنوانُ الوارِدِ الَّذي يُسَجَّلُ عِندَ البَوّابَة — أَصلُ الطَلَبِ والمَسارُ الحَرفِيُّ نَفسُه (نَسَقُ <c>VendorShippingSurface.InboundUrl</c>).</summary>
    public static string NoticeUrl(string origin, string tenantSlug)
        => origin.TrimEnd('/') + NoticePath.Replace("{slug}", tenantSlug, StringComparison.Ordinal);

    /// <summary>سِرُّ إشعارٍ جَديد — ‏32 بايتاً عَشوائِيّاً بِالسِتَّةَ عَشَرِيّ (نَسَقُ <c>OtoWebhookSignature.NewSecret</c>): نُصدِرُه نَحن ولا يَعرِفُه غَيرُنا وغَيرُ البَوّابَة.</summary>
    public static string NewNoticeSecret() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();

    /// <summary>حَقلُ سِرِّ الإشعارِ في تَعريفِ الرَبط — ما يُسَمّيه <c>webhook.verify</c> (مِن نَوعِ <c>issued_secret</c>)، و<c>null</c> لِتَعريفٍ بِلا وارِد.</summary>
    public static ProviderFieldDefinition? NoticeFieldOf(ResolvedProvider? payments)
        => payments?.Definition.Webhook is { } hook
            ? payments.Definition.Credential.Fields.FirstOrDefault(f => f.Code == hook.Verify && f.Kind == CredentialKinds.IssuedSecret)
            : null;

    /// <summary>
    /// <para><b>سِرُّ إشعارِ المَتجَرِ صَريحاً — لِلمُقارَنَةِ الواحِدَةِ ولا يُحفَظ</b>: رَبطُ دَفعٍ فَعّالٌ بِمِفتاحٍ مَختومٍ وسِرِّ إشعارٍ مَختومٍ يُفَكُّ بِسِياقِه. و<c>null</c>
    /// لِكُلِّ ما سِواه — فَيُجيبُ الوارِدُ «لا سِرّ» ولا يَقرَأُ جِسماً.</para>
    /// </summary>
    public async Task<string?> NoticeSecretAsync(string tenantSlug, CancellationToken ct = default)
    {
        if (!_vault.IsAvailable) return null;
        var payments = await PaymentsBindingAsync(_providers, tenantSlug, ct);
        if (SealedKeyFieldOf(payments) is null || NoticeFieldOf(payments) is not { } field
            || !payments!.Values.TryGetValue(field.Code, out var sealedSecret)) return null;
        try { return sealedSecret.Unseal(_vault, ProviderBindSurface.AadFor(tenantSlug, payments.Capability, field.Code)); }
        catch (CryptographicException)
        {
            _log.LogWarning("[gateway] {Slug} notice secret does not open — no notice", tenantSlug);
            return null;
        }
    }

    /// <summary>
    /// <para><b>تَسجيلُ إشعارِ الدَفعِ عِندَ البَوّابَةِ بَعدَ رَبطِ المِفتاح</b> (قَرارُ المالِك د5 (٢) «ج»): يُناديه بابُ الرَبطِ في الاستوديو بَعدَ أَن كَتَب. لِرَبطِ دَفعٍ
    /// فَعّالٍ بِمِفتاحٍ يُفَكُّ وتَعريفٍ يُعلِنُ وارِداً — يُصدِرُ سِرّاً جَديداً ويُسَجِّلُه بِعُنوانِ <see cref="NoticeUrl"/> (<see cref="MoyasarClient.CreateWebhookAsync"/>)،
    /// فَإن قَبِلَته البَوّابَةُ خُتِمَ السِرُّ في الرَبطِ مَعَ مُعَرِّفِ التَسجيل، وإلّا كُتِبَ رَمزُ فَشَلِها ولا سِرّ — <b>والمِفتاحُ مَربوطٌ في الحالَين</b>، وزِرُّ «تَحَقَّق»
    /// احتِياطٌ كَما قالَ القَرار. تُرجِعُ رَمزَ الفَشَلِ أَو <c>null</c>.</para>
    /// <para><b>وما لا يَفعَلُه — يُقال</b>: لا يَحذِفُ تَسجيلاً سابِقاً عِندَ البَوّابَةِ حينَ يُعادُ الرَبط (حَذفُه لَم تُقرَأ صَفحَتُه) — فَإشعارُه بِسِرِّه القَديمِ يُرَدُّ
    /// ‏401 هُنا وتُعيدُه البَوّابَةُ حَتّى تَيأَس.</para>
    /// <para><b>ولِرَبطِ الدَفعِ وَحدَه</b> (‏2026-10-29، مُراجَعَةُ تَيّارِ S3 الأولى — ولا يُعادُ حَرفٌ فَوقَ ما سَبَق؛ تَوصيفُ «اليَوم» <c>a1200e2</c>): <paramref name="boundCapability"/>
    /// القُدرَةُ الَّتي كَتَبَها البابُ لِلتَّوّ. كانَ البابُ يُناديها بَعدَ كُلِّ رَبطٍ بِلا قُدرَة، فَرَبطُ مُزَوِّدِ مِلَفّاتٍ أَو خَرائِطَ في مَتجَرٍ مَربوطٍ بِمِفتاحِ ميسّر يُسَجِّلُ إشعاراً
    /// ثانِياً بِسِرٍّ جَديدٍ ويَختِمُه فَوقَ العامِل — وإن رَدَّته البَوّابَةُ سَقَطَ السِرُّ فَيُجابُ الوارِدُ ‏404. فَما سِوى <see cref="ProviderCapabilities.Payments"/> لا نِداءَ لَه ولا كِتابَة.</para>
    /// </summary>
    public async Task<string?> ConnectNoticesAsync(string tenantSlug, string boundCapability, string origin, CancellationToken ct = default)
    {
        if (!string.Equals(boundCapability, ProviderCapabilities.Payments, StringComparison.Ordinal)) return null;
        var current = await _providers.CurrentAsync(tenantSlug, ProviderCapabilities.Payments, ct);
        var payments = await PaymentsBindingAsync(_providers, tenantSlug, ct);
        if (current is not { IsActive: true } || NoticeFieldOf(payments) is not { } field) return null;
        if (_client is null || await SecretKeyAsync(_providers, _vault, _log, tenantSlug, ct) is not { } key)
            return MoyasarFailures.NotConfigured;

        var secret = NewNoticeSecret();
        var registered = await _client.CreateWebhookAsync(key, NoticeUrl(origin, tenantSlug), secret, ct);
        var values = new Dictionary<string, StoredValue>(current.Values, StringComparer.Ordinal);
        values.Remove(field.Code);
        if (registered.Ok)
            values[field.Code] = StoredValue.Sealed(_vault, field.Kind, secret, ProviderBindSurface.AadFor(tenantSlug, current.Slug, field.Code));
        await _providers.BindProviderAsync(tenantSlug, current.Slug, current.ProviderSlug, values, current.BoundBy, ct,
            registered.Value, registered.Ok ? null : registered.Failure!.Code);
        _log.LogInformation("[gateway] {Slug} payment notice ← {Outcome}", tenantSlug, registered.Ok ? "registered" : registered.Failure!.Code);
        return registered.Ok ? null : registered.Failure!.Code;
    }

    /// <summary>
    /// <para><b>إشعارُ دَفعٍ مَقبولٌ يُثبَتُ بِما تَقولُه البَوّابَة — لا بِما في جِسمِه</b>: الفاتورَةُ تُجلَبُ بِمِفتاحِ المَتجَرِ بِمُعَرِّفِ الإشعار، وصَفَقاتُها الَّتي سَمَّتها
    /// بَياناتُها تُحَمَّلُ مِن المَتجَر (ثَلاثونَ عَلى الأَكثَر)، وتُعرَضُ عَلى <see cref="GatewayPaymentProof.Gate"/> نَفسِها — فَصَفقَةٌ لَم يُسَجِّل عَلَيها إتمامُ الشِراءِ
    /// هذِه الفاتورَةَ لا تَعبُر. ثُمَّ الكاتِبانِ نَفسُهُما: <see cref="DealsService.ConfirmGatewayPaymentAsync"/> و<see cref="DealsService.EnterPaidOnRecordAsync"/> (د4).
    /// <b>ولا سُلطَةَ دافِعٍ هُنا</b>: الحارِسُ <paramref name="notice"/> — لا يُبنى إلّا بِبَوّابَةِ السِرّ — والبُرهانُ جَلبٌ بِمِفتاحِنا.</para>
    /// </summary>
    public async Task<GatewayVerifyResult> ConfirmNoticeAsync(string tenantSlug, GatewayPaymentNotice notice, CancellationToken ct = default)
    {
        if (_client is null || await SecretKeyAsync(_providers, _vault, _log, tenantSlug, ct) is not { } key)
            return Logged(tenantSlug, new(null, null, MoyasarFailures.NotConfigured));

        var fetched = await _client.FetchInvoiceAsync(key, notice.InvoiceId, ct);
        if (!fetched.Ok) return Logged(tenantSlug, new(null, null, fetched.Failure!.Code));

        var invoice = fetched.Value!;
        var named = new List<Deal>();
        if (GatewayPaymentProof.DealIdsIn(invoice.Metadata) is { } listed && listed.Count <= GatewayPaymentProof.MaxDealsPerInvoice)
            foreach (var id in listed)
                if (await _deals.LoadAsync(tenantSlug, id, ct) is { } d) named.Add(d);

        var admitted = GatewayPaymentProof.Gate(invoice, named);
        if (admitted.Payment is not { } proven)
            return Logged(tenantSlug, new(named.FirstOrDefault()?.Id, null, admitted.Violation!.Code));

        var confirmed = await _deals.ConfirmGatewayPaymentAsync(tenantSlug, proven, ct);
        foreach (var paidId in proven.DealIds)
            await _deals.EnterPaidOnRecordAsync(tenantSlug, paidId, ct);
        return Logged(tenantSlug, new GatewayVerifyResult(proven.DealIds[0], proven, null) { Confirmed = confirmed.Select(c => c.Id).ToArray() });
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
    /// <remarks><b>والسَلَّةُ مُدخَلٌ سادِسٌ يُقرَأُ هُنا مَرَّةً</b> (‏2026-10-13، G5 (xvi-g)): <paramref name="cart"/> يُمَرِّرُه الطَرَفانِ كَما حَمَّلاه، فَيَرُدُّ
    /// <see cref="CheckoutCardFacts.ForCart"/> بِطاقَةَ البَوّابَةِ لِسَلَّةٍ لا تَحمِلُها فاتورَةٌ واحِدَة <b>قَبلَ أَن تولَدَ صَفقَة</b>. و<c>null</c> لا يُغَيِّرُ شَيئاً.</remarks>
    /// <remarks>(‏2026-10-13، G5 (xvii-g) — ولا يُعادُ حَرفٌ فَوقَ ما سَبَق: و<paramref name="pattern"/> — نَمَطُ صَفَقاتِ المَتجَرِ كَما يَحسُبُه الطَرَفانِ
    /// بِـ<c>PatternFromTenant</c> — مُدخَلٌ سابِع، فَيَرُدُّ <see cref="CheckoutCardFacts.ForPattern"/> بِطاقَةَ البَوّابَةِ لِنَمَطٍ بِلا «دَفع».)</remarks>
    public async Task<CheckoutCardFacts> CardFactsAsync(
        string tenantSlug, bool storeCollects, bool instanceCanCharge, ACommerce.Kit.Cart.Cart? cart = null, string? pattern = null,
        CancellationToken ct = default)
        => new CheckoutCardFacts(storeCollects,
               SealedKeyFieldOf(await PaymentsBindingAsync(_providers, tenantSlug, ct)) is not null,
               _vault.IsAvailable,
               _client is not null,
               instanceCanCharge).ForCart(cart).ForPattern(pattern);

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
        DateTimeOffset expiresAt = default;
        if (code is null)
        {
            if (_client is null || await SecretKeyAsync(_providers, _vault, _log, tenantSlug, ct) is not { } key)
                code = MoyasarFailures.NotConfigured;
            else
            {
                // وتاريخُ انتِهائِها مَعَها: الآنَ وأَربَعٌ وعِشرون ساعَة (‏2026-10-29، قَرارُ المالِك د5 (٣)).
                var draft = new MoyasarInvoiceDraft(amount!.Value, l.Format("pay.invoice.description", storeName), returnUrl, returnUrl,
                    GatewayPaymentProof.MetadataFor(deals.Select(d => d.Id).ToArray()), expiresAt = _clock.GetUtcNow() + InvoiceLifetime);
                var created = await _client.CreateInvoiceAsync(key, draft, ct);
                if (created.Ok) invoice = created.Value; else code = created.Failure!.Code;
            }
        }

        if (invoice is null)
        {
            // ونَتيجَةُ كُلِّ إلغاءٍ تُقرَأ (‏2026-10-13، G5 (xvi-g)؛ تَوصيفُ «اليَوم» `64b6e5c`): كانَت تُهمَل، فَإلغاءٌ رَدَّه تَعارُضُ نُسخَةٍ
            // — البائِعُ المَربوطُ عِندَ الوِلادَةِ قَبِلَ بَينَهُما — تَرَكَ صَفقَةً قائِمَةً بِلا فاتورَة. يُعادُ مَرَّةً عَلى ما كُتِب، وما بَقِيَ
            // بَعدَها يُكتَبُ خَطَأً يُسَمّي الصَفقَة — وعَلامَةُ الوِلادَةِ تُمسِكُها عَن «دَفع» (`AwaitsPayment`) حَتّى تُلغى بِاليَد.
            foreach (var d in deals)
                await CancelUnopenedAsync(tenantSlug, d, new DealCanceller(by.ActorId, d.InitiatorName, IsStoreAdmin: false),
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
            // وتاريخُ انتِهائِها كَما أُرسِل — يُقرَأُ بِلا نِداء (‏2026-10-29، قَرارُ المالِك د5 (٣)).
            await _deals.AttachRefAsync(tenantSlug, d.Id, InvoiceExpiresAtRef, MoyasarClient.ExpiryText(expiresAt), ct);
        }
        // وإلغاؤها عِندَ انتِهائِها يُجَدوَلُ الآنَ لِتاريخِها المَكتوبِ نَفسِه — لا يَنتَظِرُ عَودَةَ المُشتَري (‏2026-10-29، قَرارُ المالِك د5 (٤)؛ ‏ADR-046).
        await ScheduleExpiryAsync(tenantSlug, deals[0].Id, expiresAt, ct);
        _log.LogInformation("[gateway] open invoice {Slug} for {Count} deal(s) ← opened", tenantSlug, deals.Count);
        return new GatewayCheckout(invoice.Url, null);
    }

    /// <summary>
    /// <para><b>جَدوَلَةُ المُهلَةِ — رِسالَةٌ واحِدَةٌ لِكُلِّ فاتورَة</b> بِالصَفقَةِ المَرجِعِ لِتاريخِ انتِهائِها الَّذي أُرسِلَ إلى البَوّابَة (<see cref="GatewayInvoiceExpiryDue"/>).
    /// <b>وتَعَذُّرُها لا يُسقِطُ الشِراء</b>: الفاتورَةُ فُتِحَت وكُتِبَت عَلى صَفَقاتِها، والمُشتَري يُحَوَّلُ إلَيها — فَسَطرُ خَطَأٍ يُسَمّي الصَفقَة، وزِرُّ «تَحَقَّق»
    /// يُلغي بَعدَ المُهلَةِ كَما كان. ولا حافِلَةَ (عالَمُ الاختِبار) سَطرُ تَحذير.</para>
    /// </summary>
    private async Task ScheduleExpiryAsync(string tenantSlug, Guid dealId, DateTimeOffset at, CancellationToken ct)
    {
        if (_bus is null)
        {
            _log.LogWarning("[gateway] {Slug} deal {Deal}: no message bus — expiry not scheduled, verify cancels it", tenantSlug, dealId);
            return;
        }
        try
        {
            await _bus.ScheduleAsync(new GatewayInvoiceExpiryDue(tenantSlug, dealId), at);
        }
        catch (Exception e) when (!ct.IsCancellationRequested)
        {
            _log.LogError(e, "[gateway] {Slug} deal {Deal}: expiry not scheduled — verify cancels it", tenantSlug, dealId);
        }
    }

    /// <summary>
    /// <para><b>إلغاءُ صَفَقاتِ السَلَّةِ حينَ تَعَذَّرَ تَأمينُ دَفعِها عِندَ الشِراء — سُلوكٌ واحِدٌ لِلعُطلِ الواحِد</b> (‏2026-10-29، قَرارُ المالِك د8 (٢) «أ»:
    /// «إذا تعذّر حجز المبلغ عند الشراء أُلغيت طلبات السلّة فورًا، ويرى المشتري رسالة واضحة وسلّته كما هي»؛ تَوصيفُ «اليَوم» <c>05e2e13</c>): يُناديه
    /// <c>/checkout/submit</c> حينَ يَرمي الحَجزُ عِندَ مُزَوِّدِ النُسخَةِ — كانَ يَكتُبُ <c>payment_error</c> ويَمضي بِبَقِيَّةِ السَلَّة، وطَريقُ البَوّابَةِ وَحدَه يُلغي.
    /// والإلغاءُ هُوَ إلغاءُ فَشَلِ فَتحِ الفاتورَةِ نَفسُه (<see cref="CancelUnopenedAsync"/>) بِاسمِ المُشتَري وبِـ<paramref name="reason"/>؛ و<b>ما نَجَحَ حَجزُه يُفَكُّ</b>
    /// بِالكاتِبِ القائِمِ لا بِنِداءٍ جَديد: <see cref="DealsService.CancelAsync"/> يَستَرجِعُ ما عَلَيه <c>payment_id</c> (القاعِدَة ٨). وصَفقَةُ غَيرِ <paramref name="by"/>
    /// أَو ما لَم يَعُد قائِماً لا تُمَسّ. ولا إشعارَ هُنا: لَم يُخبَر بائِعٌ بِالوِلادَة (الرُجوعُ قَبلَ <c>NotifyNewDealsAsync</c>)، فَلا يُخبَرُ بِالإلغاء.</para>
    /// </summary>
    public async Task CancelUnsecuredAsync(
        string tenantSlug, DealPayer by, IReadOnlyList<Guid> dealIds, string reason, CancellationToken ct = default)
    {
        foreach (var id in dealIds)
            if (await _deals.LoadAsync(tenantSlug, id, ct) is { Status: DealStatus.Active } d && d.InitiatorId == by.ActorId)
                await CancelUnopenedAsync(tenantSlug, d, new DealCanceller(by.ActorId, d.InitiatorName, IsStoreAdmin: false), reason, ct);
        _log.LogWarning("[checkout] {Slug}: payment could not be secured for {Count} deal(s) — cancelled, held amounts released", tenantSlug, dealIds.Count);
    }

    /// <summary>
    /// <para><b>إلغاءُ صَفقَةٍ لَم تُفتَح فاتورَتُها — ونَتيجَتُه تُقرَأ</b> (‏2026-10-13، G5 (xvi-g)): <see cref="DealsService.ChangedMeanwhile"/>
    /// يُعادُ مَرَّةً بِجَلسَةٍ جَديدَةٍ تَقرَأُ ما كُتِب؛ و<see cref="DealCancelAuthorization.DealNotActive"/> نَجاحٌ (أَلغاها غَيرُه)؛ وكُلُّ رَدٍّ
    /// سِواهُما — أَو استِثناءٌ — سَطرُ خَطَأٍ يُسَمّي الصَفقَةَ ورَمزَها، ويَمضي إلى أُختِها فَلا يُترَكُ الباقي بِلا إلغاء.</para>
    /// </summary>
    private async Task CancelUnopenedAsync(string tenantSlug, Deal d, DealCanceller by, string reason, CancellationToken ct)
    {
        try
        {
            var res = await _deals.CancelAsync(tenantSlug, d.Id, by, reason, ct);
            if (!res.Ok && res.Violation?.Code == DealsService.ChangedMeanwhile)
                res = await _deals.CancelAsync(tenantSlug, d.Id, by, reason, ct);
            if (res.Ok || res.Violation?.Code == DealCancelAuthorization.DealNotActive) return;
            _log.LogError("[gateway] open invoice {Slug}: deal {Deal} stays active without an invoice ← cancel {Code} — it cannot enter Paid; cancel it by hand",
                tenantSlug, d.Id, res.Violation?.Code);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogError(ex, "[gateway] open invoice {Slug}: deal {Deal} stays active without an invoice ← cancel threw {Error} — it cannot enter Paid; cancel it by hand",
                tenantSlug, d.Id, ex.GetType().Name);
        }
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
        string tenantSlug, Deal referenced, MoyasarInvoice invoice, Guid payerId, CancellationToken ct)
    {
        var deals = new List<Deal> { referenced };
        var listed = GatewayPaymentProof.DealIdsIn(invoice.Metadata);
        if (listed is null || listed.Count > GatewayPaymentProof.MaxDealsPerInvoice) return deals;

        foreach (var id in listed)
        {
            if (id == referenced.Id) continue;
            if (await _deals.LoadAsync(tenantSlug, id, ct) is { } d && d.InitiatorId == payerId) deals.Add(d);
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

    /// <summary>
    /// <para><b>والصَفقَةُ في «نِزاع» تُحسَبُ عَلى فاتورَتِها ما لَم تُدفَع</b> (‏2026-10-29، قَرارُ المالِك د38 (٢) «ج» — الإلغاءُ «بِأَثَرِ الإلغاءِ الماليِّ القائِم»؛
    /// مُراجَعَةُ تَيّارِ S5 الأولى، تَوصيفُه <c>fcffb9b</c>). <see cref="Validate"/> يَسأَلُ <see cref="GatewayInvoiceClosing.Owed"/> — <c>Active</c> وَحدَها — فَكانَ حَسمُ
    /// النِزاعِ بِالإلغاءِ يُلغي المُتَنازَعَ عَلَيها وَحدَها وفاتورَتُها المَفتوحَةُ تَدفَعُ لِأُختٍ قائِمَة، فَتَبقى قابِلَةً لِلدَّفعِ بِمَبلَغِها كامِلاً ومِنه المُلغاة.
    /// والفاتورَةُ لا تَعرِفُ «نِزاعاً»: ما لَم يُكتَب دَفعُها فَهي تَطلُبُ ثَمَنَها. فَهذا البابُ لِبَوّابَةِ الحَسمِ وَحدَها (<c>CancelCoreAsync</c> بِـ<c>resolvingDispute</c>)
    /// والصَفحَةِ قَبلَ رَسمِ بَديلِ الإلغاء — بِالرَمزِ نَفسِه، فَلا رَمزَ جَديدَ في المَعجَمِ ولا في نُصوصِ الاستوديو.</para>
    /// <para>ولا يُوَسَّعُ <see cref="GatewayInvoiceClosing.Owed"/> نَفسُه: يَقرَؤُه رابِطُ الدَفعِ وإشعارُه وفَتحُ الفاتورَةِ بِحَرفِ «القائِمَة» (سِجِلُّ المالِك «أ»).</para>
    /// </summary>
    public static IReadOnlyList<DealInvoiceShareViolation> ValidateDisputed(Deal disputed, IReadOnlyList<Deal> siblings)
        => disputed.Status == DealStatus.Disputed && GatewayPayments.HasInvoice(disputed) && !DealsService.IsCaptured(disputed)
           && OwedOnInvoice(disputed, siblings).Any(d => d.Id != disputed.Id)
            ? new[] { new DealInvoiceShareViolation(Shared,
                "عَلى فاتورَةِ هذِه الصَفقَةِ المُتَنازَعِ عَلَيها المَفتوحَةِ صَفَقاتٌ قائِمَةٌ أُخرى — لا تُلغى وَحدَها: تُستَأنَفُ ثُمَّ يُلغى الطَلَبُ كُلُّه، أَو تُلغى بَعدَ الدَفع.") }
            : Array.Empty<DealInvoiceShareViolation>();

    public static bool IsValidDisputed(Deal disputed, IReadOnlyList<Deal> siblings) => ValidateDisputed(disputed, siblings).Count == 0;
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
    /// <para><b>أَيُبقي هذا الطَرَفُ الفاتورَةَ مَفتوحَةً عِندَ البَوّابَة؟</b> (‏2026-10-30، تَدقيقُ الموجَة ١١ CO12 — المُرَحَّلُ [S5]؛ تَوصيفُه <c>1a5d31c</c>؛ قَرارُ «أ» F3·٦):
    /// لَم يُقبَض دَفعُها، وهي قائِمَةٌ <b>أَو في «نِزاع»</b> — فَالنِزاعُ يُحسَمُ بِالاستِئنافِ (د38) فَتَعودُ مَدينَةً تَدفَعُ بِالفاتورَةِ نَفسِها. كانَ المُلغي يَسأَلُ <see cref="Owed"/>
    /// فَإلغاءُ أُختِها يُلغي الفاتورَةَ والمُستَأنَفَةُ بِلا ما تَدفَعُ بِه. <b>ولا يَمَسُّ <see cref="Owed"/></b>: ذاكَ «ما يَعمَلُ عَلَيه إلغاءٌ وطَلَبٌ كُلُّه» ويَطلُبُ قائِمَةً — صَفقَةٌ
    /// في «نِزاع» لا يُلغيها الإلغاءُ العامّ (<see cref="DealCancelAuthorization.DealNotActive"/>)، فَعَدُّها هُناكَ يُغلِقُ بابَ الطَلَبِ كُلِّه.</para>
    /// </summary>
    public static bool KeepsOpen(Deal deal)
        => deal.Status is DealStatus.Active or DealStatus.Disputed && !DealsService.IsCaptured(deal);

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

        // (‏2026-10-30، تَدقيقُ الموجَة ١١ CO12 — ولا يُعادُ حَرفٌ فَوقَه): `KeepsOpen` لا `Owed` — أُختٌ في «نِزاع» لَم تُقبَض تُبقيها.
        if (named.Where(d => listed?.Contains(d.Id) == true).Append(cancelled).Any(KeepsOpen))
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
    /// <summary>عُطلٌ داخِلَ المُلغي — قاعِدَةٌ في تَحميلِ الأَخَواتِ أَو رَبطِ الدَفع، أَو استِثناءُ خِزانَةٍ لَيسَ <c>CryptographicException</c> — بَعدَ أَن حُفِظَ
    /// الإلغاء: يُكتَبُ تَحذيراً ويُرجَعُ رَمزاً نَسَقَ فَرعِ الاستِرجاع، ولا يَصعَدُ مِن <see cref="DealsService.CancelAsync"/> (‏2026-10-13، مُراجَعَةُ
    /// بَوّابَةِ المَوجَة 3b — كانَ يَصعَدُ فَتُجيبُ النُقطَةُ ‏500 عَن إلغاءٍ وَقَع).</summary>
    public const string Faulted = "gateway_invoice_close_faulted";

    /// <summary>‏PUT …/cancel أُجيبَ ‏200 وحالُ الفاتورَةِ غَيرُ <see cref="MoyasarClient.CanceledStatus"/> — مَثَلاً <c>paid</c> إن وَقَعَ دَفعٌ بَينَ الجَلبِ
    /// والإلغاء: نَتيجَةٌ بِرَمزِها لا «أُلغِيَت» (‏2026-10-13).</summary>
    public const string NotCancelled = "gateway_invoice_not_cancelled";

    /// <summary>
    /// <para><b>أَتُبقي هذِه النَتيجَةُ فاتورَةً قَد تُدفَعُ ثَمَناً لِما أُلغِيَ؟</b> (‏2026-10-29، قَرارُ المالِك د3 «د» — «وبكل فاتورة تعذّر إلغاؤها») — دالَّةٌ نَقِيَّةٌ عَلى
    /// رَمزِ <see cref="CloseIfLastAsync"/>: <c>null</c> (أُلغِيَت)، و<see cref="GatewayInvoiceClosing.StillOwed"/> (تَدفَعُ لِأُختٍ قائِمَةٍ بِقَصد)، و<see cref="GatewayInvoiceClosing.NotOpen"/>
    /// (لَيسَت مَفتوحَةً عِندَ البَوّابَة — دُفِعَت فَيُكتَبُ استِرجاعُها بِالتَأكيد، أَو أُلغِيَت أَو انتَهَت)، و<see cref="DealPayAuthorization.InvoiceMissing"/> (لا فاتورَة)
    /// <b>لا تُبقي</b>؛ وكُلُّ رَمزٍ سِواها — لا مِفتاحَ يُفَكّ، وفَشَلُ الجَلبِ أَو الإلغاء، و<see cref="NotCancelled"/>، و<see cref="Faulted"/>، وفاتورَةٌ لا تُسَمّيها —
    /// <b>يُبقيها</b>، فَيُكتَبُ سَطرُ <see cref="DealEvent.GatewayInvoiceLeftOpen"/> ويُخبَرُ صاحِبُ المَتجَر. وكانَت كُلُّها سَطرَ لوغٍ لا يَقرَؤُه أَحَد.</para>
    /// </summary>
    public static bool LeavesPayable(string? code)
        => code is not null
           && code is not (GatewayInvoiceClosing.StillOwed or GatewayInvoiceClosing.NotOpen or DealPayAuthorization.InvoiceMissing);

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
        // (‏2026-10-13، مُراجَعَةُ بَوّابَةِ المَوجَة 3b — ولا يُعادُ حَرفٌ فَوقَه: ومَلفوفٌ كَفَرعِ الاستِرجاع — عُطلٌ في داخِلِه رَمزُه `Faulted` تَحذيراً.)
        try { return await CloseCoreAsync(tenantSlug, cancelled, load); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogWarning(ex, "[gateway] close invoice {Slug} deal {Deal} ← {Outcome} — the cancel stands; the invoice may still be payable",
                tenantSlug, cancelled.Id.ToString("D"), Faulted);
            return Faulted;
        }
    }

    private async Task<string?> CloseCoreAsync(string tenantSlug, Deal cancelled, Func<Guid, Task<Deal?>> load)
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
        if (!closed.Ok) return Logged(tenantSlug, cancelled, closed.Failure!.Code);
        // و‏200 بِحالٍ غَيرِ «canceled» نَتيجَةٌ بِرَمزِها لا «أُلغِيَت» (‏2026-10-13، مُراجَعَةُ بَوّابَةِ المَوجَة 3b).
        return string.Equals(closed.Value!.Status, MoyasarClient.CanceledStatus, StringComparison.Ordinal)
            ? Logged(tenantSlug, cancelled, null, closed.Value.Status)
            : Logged(tenantSlug, cancelled, NotCancelled, closed.Value.Status);
    }

    /// <summary>سَطرُ اللوغ: المَتجَرُ والصَفقَةُ والنَتيجَة — لا مِفتاحٌ ولا رابِطُ فاتورَة. <b>وتَحذيرٌ لِكُلِّ نَتيجَةٍ تَتَرُكُ فاتورَةً قَد تُدفَعُ ثَمَناً
    /// لِما أُلغِيَ</b> (‏2026-10-13، مُراجَعَةُ بَوّابَةِ المَوجَة 3b): كُلُّ رَمزٍ إلّا <see cref="GatewayInvoiceClosing.StillOwed"/> — الفاتورَةُ تَدفَعُ لِأُختٍ قائِمَةٍ
    /// بِقَصد — والإلغاءُ (<c>null</c>)؛ وكانَ الكُلُّ مَعلومَة.</summary>
    private string? Logged(string tenantSlug, Deal cancelled, string? code, string? status = null)
    {
        var outcome = code is null ? $"cancelled at the gateway (status {status ?? "—"})"
                    : status is null ? code
                    : $"{code} (status {status})";
        var level = code is null || code == GatewayInvoiceClosing.StillOwed ? LogLevel.Information : LogLevel.Warning;
        _log.Log(level, "[gateway] close invoice {Slug} deal {Deal} ← {Outcome}", tenantSlug, cancelled.Id.ToString("D"), outcome);
        return code;
    }
}
