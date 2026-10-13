using ACommerce.Kit.Payments;

namespace ACommerce.Templates.Customer.Marketplace.Services.Deals;

// ═══ بُرهانُ الدَفعِ عِندَ بَوّابَةِ المُستَأجِر — بَوّابَةٌ نَقِيَّةٌ بِرُموزِ خَرقٍ ثابِتَة (‏ADR-032 §٢‑هـ الخُطوَة 8) ═══════════
//
// **«وسايل تتحقّقُ من الخادم»** (‏ADR-032 §٢‑ج، المَرحَلَة ١): المُشتَري يَدفَعُ عَلى صَفحَةِ البَوّابَةِ المُستَضافَةِ ويَعود، والعَودَةُ
// لَيسَت بُرهاناً — يَعودُ مَن دَفَعَ ومَن لَم يَدفَع، ورابِطُ العَودَةِ يَملِكُه مَن يَملِكُه. البُرهانُ فاتورَةٌ تُجلَبُ بِمِفتاحِ المُستَأجِر
// (`MoyasarClient.FetchInvoiceAsync`) فَتُعرَضُ عَلى هذِه الدالَّةِ مَعَ صَفَقاتِها؛ ولا يُكتَبُ عَلى صَفقَةٍ «مَدفوعَة» إلّا ما عَبَرَها.
//
// **ورُموزُها خَمسَةٌ لا سادِسَ لَها** — (‏2026-10-13، G5 (xvii-g): وسادِسٌ `gateway_invoice_closed` يَنقَسِمُ بِه الحالُ إلى مُنتَظِرَةٍ ومُغلَقَة) — (القاعِدَة ٤: `record XViolation(Code, MessageAr)` + `Validate` + `IsValid`، ولِكُلِّ رَمزٍ صَفٌّ
// مُوجَبٌ وصَفٌّ سالِبٌ في `GatewayPaymentProofTests`):
//   • الحالُ «paid» بِحَرفِ مَعجَمِ الفاتورَةِ عِندَ البَوّابَة؛
//   • العُملَةُ الرِيال؛
//   • المَبلَغُ مَجموعُ الصَفَقاتِ بِأَصغَرِ وَحدَة — بِالدالَّةِ نَفسِها الَّتي حَسَبَت مَبلَغَ الفاتورَةِ عِندَ إنشائِها، بِلا تَقريب؛
//   • مُعَرِّفاتُ الصَفَقاتِ في `metadata` الفاتورَةِ هِيَ الصَفَقاتُ المَعروضَةُ نَفسُها — لا أَكثَرَ ولا أَقَلّ؛
//   • والفاتورَةُ هِيَ الَّتي سَجَّلَها إتمامُ الشِراءِ عَلى كُلِّ صَفقَة (`gateway_invoice_id`).
// **وأَنّ الفاتورَةَ لِمِفتاحِ هذا المُستَأجِرِ لا يُفحَصُ هُنا لِأَنّه مُتَحَقِّقٌ قَبلَها**: تُجلَبُ بِمِفتاحِه، وفاتورَةُ حِسابٍ آخَرَ
// تُجيبُ ‏404 فَلا تَبلُغُ هذِه الدالَّة (`MoyasarFailures.Refused`).

/// <summary>خَرقٌ واحِدٌ في بُرهانِ الدَفع — <c>Code</c> مِن <see cref="GatewayPaymentProof.Codes"/>.</summary>
public sealed record GatewayPaymentViolation(string Code, string MessageAr);

/// <summary>
/// <para><b>دَفعٌ ثَبَتَ عِندَ البَوّابَة — سِجِلُّ سُلطَة</b>: لا يُبنى إلّا بِـ<see cref="GatewayPaymentProof.Gate"/> (المُنشِئُ
/// داخِلِيّ)، ويَطلُبُه تَوقيعُ كاتِبِه في الخِدمَة — فَلا يُكتَبُ «مَدفوع» عَلى صَفقَةٍ بِلا فاتورَةٍ عَبَرَت البَوّابَة (القاعِدَة ٦).</para>
/// </summary>
public sealed class ProvenGatewayPayment
{
    internal ProvenGatewayPayment(string invoiceId, IReadOnlyList<Guid> dealIds, long amount, string currency)
    {
        InvoiceId = invoiceId;
        DealIds = dealIds;
        Amount = amount;
        Currency = currency;
    }

    /// <summary>مُعَرِّفُ الفاتورَةِ عِندَ البَوّابَة — مَرجِعُ الدَفعِ الَّذي يُكتَبُ عَلى الصَفَقات.</summary>
    public string InvoiceId { get; }

    /// <summary>صَفَقاتُ الفاتورَة — بِتَرتيبِ مَن عَرَضَها.</summary>
    public IReadOnlyList<Guid> DealIds { get; }

    /// <summary>المَبلَغُ بِأَصغَرِ وَحدَة، والعُملَة — كَما قالَتهُما الفاتورَة.</summary>
    public long Amount { get; }
    public string Currency { get; }

    public override string ToString() => $"ProvenGatewayPayment(invoice={InvoiceId}, deals={DealIds.Count}, {Amount} {Currency})";
}

/// <summary>جَوابُ البَوّابَة: دَفعٌ ثَبَتَ، أَو أَوَّلُ خَرقٍ بِرَمزِه.</summary>
public sealed record GatewayPaymentAdmission(ProvenGatewayPayment? Payment, GatewayPaymentViolation? Violation);

public static class GatewayPaymentProof
{
    // ─── الرُموز — مَعجَمٌ مُغلَق ──────────────────────────────────────────

    /// <summary>حالُ الفاتورَةِ لَيسَ <see cref="MoyasarClient.PaidStatus"/> — عادَ المُشتَري ولَم يَدفَع، أَو فَشِلَ دَفعُه، أَو أُلغِيَت.
    /// (‏2026-10-13، G5 (xvii-g) — ولا يُعادُ حَرفٌ فَوقَه: «أَو أُلغِيَت» صارَ <see cref="InvoiceClosed"/>؛ وهذا لِما يُدفَعُ بَعدُ — <c>initiated</c>
    /// و<c>failed</c> و<c>on_hold</c> وكُلُّ حالٍ خارِجَ <see cref="ClosedStatuses"/>.)</summary>
    public const string InvoiceUnpaid = "gateway_invoice_unpaid";

    /// <summary>
    /// <para><b>الفاتورَةُ أُغلِقَت عِندَ البَوّابَة</b> (‏2026-10-13، مُراجَعَةُ بَوّابَةِ المَوجَة 3b الثانِيَة، G5 (xvii-g)؛ تَوصيفُ «اليَوم» <c>5c777d7</c>):
    /// حالُها مِن <see cref="ClosedStatuses"/> — لا تُدفَعُ بَعدَ اليَوم. كانَ يُجابُ بِـ<see cref="InvoiceUnpaid"/> فَتَبقى صَفحَةُ العَودَةِ تَدعو إلى
    /// صَفحَةِ دَفعٍ مَيِّتَة؛ وصارَ التَحَقُّقُ يَكتُبُه عَلى صَفَقاتِها (<see cref="GatewayPayments.InvoiceClosedRef"/>) فَلا يُرسَمُ الرابِطُ بَعدَه.</para>
    /// </summary>
    public const string InvoiceClosed = "gateway_invoice_closed";

    /// <summary>أَحوالُ الفاتورَةِ المُغلَقَةِ مِن مَعجَمِ «Show Invoice» (رَأسُ <c>MoyasarClient</c>): أُلغِيَت، انتَهَت، أُبطِلَت، استُرجِعَت — بِحَرفِها.</summary>
    public static readonly IReadOnlyList<string> ClosedStatuses =
        new[] { MoyasarClient.CanceledStatus, "expired", "voided", "refunded" };

    /// <summary>عُملَةُ الفاتورَةِ لَيسَت <see cref="MoyasarClient.Currency"/>.</summary>
    public const string CurrencyMismatch = "gateway_currency_mismatch";

    /// <summary>مَبلَغُ الفاتورَةِ لا يُساوي مَجموعَ صَفَقاتِها بِأَصغَرِ وَحدَة — أَو المَجموعُ لا يَصيرُ هَلَلاتٍ صَحيحَة.</summary>
    public const string AmountMismatch = "gateway_amount_mismatch";

    /// <summary>مُعَرِّفاتُ الصَفَقاتِ في <c>metadata</c> الفاتورَةِ لَيسَت الصَفَقاتِ المَعروضَة — أَو لا صَفَقاتَ أَصلاً.</summary>
    public const string DealsMismatch = "gateway_deals_mismatch";

    /// <summary>صَفقَةٌ لَم يُسَجِّل عَلَيها إتمامُ الشِراءِ هذِه الفاتورَة (<see cref="InvoiceRef"/>).</summary>
    public const string InvoiceMismatch = "gateway_invoice_mismatch";

    public static readonly IReadOnlyList<string> Codes =
        new[] { InvoiceUnpaid, InvoiceClosed, CurrencyMismatch, AmountMismatch, DealsMismatch, InvoiceMismatch };   // ‏المُغلَقَةُ ثانِيَةً: يُطلِقُها الحالُ أَوَّلاً كَأُختِها

    public static bool Contains(string code) => Codes.Contains(code, StringComparer.Ordinal);

    // ─── ما يَكتُبُه إتمامُ الشِراءِ ويَقرَؤُه البُرهان — حَرفٌ واحِدٌ لِلطَرَفَين ───────────────────

    /// <summary>مِفتاحُ مُعَرِّفِ الفاتورَةِ عَلى الصَفقَةِ (<see cref="Deal.Refs"/>) — يَكتُبُه إتمامُ الشِراءِ بِالبِطاقَة
    /// ويَقرَؤُه البُرهان. نَسَقُ <see cref="DealsService.ListingOwnerRef"/>.</summary>
    public const string InvoiceRef = "gateway_invoice_id";

    /// <summary>بادِئَةُ مَفاتيحِ الصَفَقاتِ في <c>metadata</c> الفاتورَة: <c>deal_1</c>…<c>deal_n</c>، والقيمَةُ المُعَرِّفُ بِصيغَةِ
    /// <c>N</c> (‏32 مِحرَفاً) — داخِلَ حُدودِ الوَثيقَة: «up to 30 keys, with key names up to 40 characters long and values up to
    /// 500 characters long».</summary>
    public const string DealKeyPrefix = "deal_";

    /// <summary>أَكثَرُ الصَفَقاتِ في فاتورَةٍ واحِدَة — مِفتاحٌ لِكُلِّ صَفقَةٍ مِن مَفاتيحِ <c>metadata</c> الثَلاثين؛ رَقَمُ الوَثيقَةِ لا
    /// رَقَمٌ مُختَرَع. وسَلَّةٌ أَطوَلُ مِنه لا تُدفَعُ بِفاتورَةٍ واحِدَة.</summary>
    public const int MaxDealsPerInvoice = MoyasarClient.MetadataMaxKeys;

    /// <summary><c>metadata</c> الفاتورَةِ لِهذِه الصَفَقات — بِتَرتيبِها. يَرمي لِقائِمَةٍ فارِغَةٍ أَو أَطوَلَ مِن
    /// <see cref="MaxDealsPerInvoice"/>: المُنادي يَسأَلُ <see cref="FitsOneInvoice"/> قَبلَ أَن يُنشِئَ فاتورَة.</summary>
    public static IReadOnlyDictionary<string, string> MetadataFor(IReadOnlyList<Guid> dealIds)
    {
        if (!FitsOneInvoice(dealIds.Count))
            throw new ArgumentException($"‏{dealIds.Count} صَفقَة — الفاتورَةُ الواحِدَةُ تَحمِلُ مِن واحِدَةٍ إلى {MaxDealsPerInvoice}.", nameof(dealIds));
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < dealIds.Count; i++) metadata[$"{DealKeyPrefix}{i + 1}"] = dealIds[i].ToString("N");
        return metadata;
    }

    /// <summary>أَتَحمِلُ فاتورَةٌ واحِدَةٌ هذا العَدَدَ مِن الصَفَقات؟</summary>
    public static bool FitsOneInvoice(int deals) => deals is > 0 and <= MaxDealsPerInvoice;

    /// <summary>مُعَرِّفاتُ الصَفَقاتِ في <c>metadata</c> — وقيمَةٌ تَحتَ مِفتاحِ صَفقَةٍ لَيسَت مُعَرِّفاً بِصيغَةِ <c>N</c> تُرجِعُ
    /// <c>null</c>: لا يُبنى بُرهانٌ عَلى قِراءَةٍ جُزئِيَّة.</summary>
    public static IReadOnlySet<Guid>? DealIdsIn(IReadOnlyDictionary<string, string> metadata)
    {
        var ids = new HashSet<Guid>();
        foreach (var (key, value) in metadata)
        {
            if (!key.StartsWith(DealKeyPrefix, StringComparison.Ordinal)) continue;
            if (!Guid.TryParseExact(value, "N", out var id) || !ids.Add(id)) return null;
        }
        return ids;
    }

    // ─── البَوّابَة ────────────────────────────────────────────────────────

    /// <summary>
    /// <para><b>كُلُّ خَرقٍ في بُرهانِ الدَفع</b> — بِتَرتيبٍ ثابِت: الحال، ثُمَّ العُملَة، ثُمَّ المَبلَغ، ثُمَّ الصَفَقاتُ في
    /// <c>metadata</c>، ثُمَّ الفاتورَةُ المُسَجَّلَةُ عَلى كُلِّ صَفقَة. دالَّةٌ نَقِيَّة: لا شَبَكَةَ ولا Marten ولا وَقت.</para>
    /// </summary>
    public static IReadOnlyList<GatewayPaymentViolation> Validate(MoyasarInvoice invoice, IReadOnlyList<Deal> deals)
    {
        var v = new List<GatewayPaymentViolation>();

        if (!invoice.IsPaid)
            v.Add(ClosedStatuses.Contains(invoice.Status, StringComparer.Ordinal)
                ? new(InvoiceClosed, $"حالُ الفاتورَةِ «{invoice.Status}» — أُغلِقَت عِندَ البَوّابَةِ فَلا تُدفَعُ بَعدَ اليَوم.")
                : new(InvoiceUnpaid, $"حالُ الفاتورَةِ «{invoice.Status}» لا «{MoyasarClient.PaidStatus}» — لَم يَثبُت الدَفع."));

        if (!string.Equals(invoice.Currency, MoyasarClient.Currency, StringComparison.Ordinal))
            v.Add(new(CurrencyMismatch, $"عُملَةُ الفاتورَةِ «{invoice.Currency}» لا «{MoyasarClient.Currency}»."));

        var expected = MoyasarClient.ToSmallestUnit(deals.Sum(d => d.AmountSar));
        if (expected is null || expected.Value != invoice.Amount)
            v.Add(new(AmountMismatch,
                $"مَبلَغُ الفاتورَةِ {invoice.Amount} هَلَلَةً، ومَجموعُ صَفَقاتِها {expected?.ToString() ?? "لا يَصيرُ هَلَلاتٍ صَحيحَة"}."));

        var listed = DealIdsIn(invoice.Metadata);
        var offered = deals.Select(d => d.Id).ToHashSet();
        if (deals.Count == 0 || listed is null || !listed.SetEquals(offered) || offered.Count != deals.Count)
            v.Add(new(DealsMismatch, "صَفَقاتُ الفاتورَةِ في بَياناتِها لَيسَت الصَفَقاتِ المَعروضَةَ نَفسَها."));

        if (deals.Any(d => !d.Refs.TryGetValue(InvoiceRef, out var recorded)
                           || !string.Equals(recorded, invoice.Id, StringComparison.OrdinalIgnoreCase)))
            v.Add(new(InvoiceMismatch, "صَفقَةٌ لَم يُسَجِّل عَلَيها إتمامُ الشِراءِ هذِه الفاتورَة."));

        return v;
    }

    public static bool IsValid(MoyasarInvoice invoice, IReadOnlyList<Deal> deals) => Validate(invoice, deals).Count == 0;

    /// <summary>
    /// <para><b>البَوّابَة</b>: دَفعٌ ثَبَتَ (<see cref="ProvenGatewayPayment"/>) أَو أَوَّلُ خَرقٍ بِرَمزِه — ولا ثالِث. و«ثَبَتَ»
    /// لا يُبنى في مَوضِعٍ آخَر: مُنشِئُه داخِلِيّ، والبَوّابَةُ وَحدَها تُناديه بَعدَ <see cref="Validate"/> فارِغَة.</para>
    /// </summary>
    public static GatewayPaymentAdmission Gate(MoyasarInvoice invoice, IReadOnlyList<Deal> deals)
    {
        var violations = Validate(invoice, deals);
        return violations.Count > 0
            ? new(null, violations[0])
            : new(new ProvenGatewayPayment(invoice.Id, deals.Select(d => d.Id).ToArray(), invoice.Amount, invoice.Currency), null);
    }
}
