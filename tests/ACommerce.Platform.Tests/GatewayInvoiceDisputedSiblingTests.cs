using System.Net;
using ACommerce.Kit.Payments;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using Xunit;
using Xunit.Abstractions;
using static ACommerce.Platform.Tests.TenantGatewayEndpointsTests;

namespace ACommerce.Platform.Tests;

// ═══ أُختٌ في «نِزاع» عَلى الفاتورَة — أَيُلغيها المُلغي؟ (‏2026-10-30، تَدقيقُ الموجَة ١١ CO12 — المُرَحَّلُ [S5]) ═════════════════════════════
//
// **الحالُ المَقيسَة**: `GatewayInvoiceClosing.Owed` = `Active` ولَم تُقبَض — فَصَفقَتانِ عَلى فاتورَةٍ واحِدَة، الثانِيَةُ في «نِزاع»، وإلغاءُ الأولى يُلغي الفاتورَةَ عِندَ
// البَوّابَة (لا قائِمَةَ عَلَيها تَدفَعُ لَها) — والثانِيَةُ قَد تُستَأنَفُ (د38) فَتَعودُ مَدينَةً لا شَيءَ تَدفَعُ بِه.

public class GatewayInvoiceDisputedSiblingTests(ITestOutputHelper output)
{
    private static Deal Listed(DealStatus status, bool paid = false)
    {
        var d = new Deal { Id = Guid.NewGuid(), Pattern = "marketplace", Status = status, InitiatorId = Buyer, AmountSar = 100m };
        d.Refs[GatewayPaymentProof.InvoiceRef] = InvoiceId;
        if (paid)
        {
            d.Refs["payment_id"] = InvoiceId;
            d.Refs["payment_status"] = nameof(PaymentStatus.Captured);
        }
        return d;
    }

    private static string[] Codes(Deal cancelled, params Deal[] named)
        => GatewayInvoiceClosing.Validate(new MoyasarInvoice(InvoiceId, MoyasarClient.OpenStatus, 20000, "SAR", null,
                GatewayPaymentProof.MetadataFor(named.Prepend(cancelled).Select(d => d.Id).Distinct().ToArray())), cancelled, named)
            .Select(v => v.Code).ToArray();

    /// <summary><b>قُلِبَ بِاسمِه</b> (تَوصيفُه <c>1a5d31c</c> — كانَ <c>Today_a_disputed_uncaptured_sibling_does_not_keep_the_invoice_open</c>): مُلغاةٌ وأُختُها في «نِزاع» لَم تُقبَض
    /// ⇒ <c>gateway_invoice_still_owed</c> — الفاتورَةُ تَبقى (المُوجِب). <b>والسالِب</b>: أُختٌ في «نِزاع» قُبِضَت، أَو مُلغاة — تُلغى كَما كانَت.</summary>
    [Fact]
    public void A_disputed_uncaptured_sibling_keeps_the_invoice_open_and_a_captured_or_cancelled_one_does_not()
    {
        var codes = Codes(Listed(DealStatus.Cancelled), Listed(DealStatus.Disputed));
        output.WriteLine($"مُلغاةٌ + في نِزاع: [{string.Join(", ", codes)}]");
        Assert.Equal(new[] { GatewayInvoiceClosing.StillOwed }, codes);
        Assert.Empty(Codes(Listed(DealStatus.Cancelled), Listed(DealStatus.Disputed, paid: true)));
        Assert.Empty(Codes(Listed(DealStatus.Cancelled), Listed(DealStatus.Cancelled)));
        Assert.Empty(Codes(Listed(DealStatus.Cancelled), Listed(DealStatus.Active, paid: true)));
        Assert.True(GatewayInvoiceClosing.KeepsOpen(Listed(DealStatus.Disputed)));
        Assert.False(GatewayInvoiceClosing.Owed(Listed(DealStatus.Disputed)));
    }

    /// <summary><b>قُلِبَ بِاسمِه</b> (تَوصيفُه <c>1a5d31c</c> — كانَ <c>Today_cancelling_the_active_deal_closes_the_invoice_its_disputed_sibling_may_still_pay_with</c>): إلغاءُ
    /// صاحِبِ المَتجَرِ الأولى وَحدَها يُقبَلُ كَما كان (الأُختُ في «نِزاع» لا تُمسِكُ الإلغاء)، <b>والفاتورَةُ لا تُلغى عِندَ البَوّابَة</b> — يُجلَبُ حالُها ولا <c>PUT …/cancel</c>،
    /// ويُكتَبُ سَطرُ «تُرِكَت الفاتورَةُ مَفتوحَة»؟ لا — الإبقاءُ عَمداً (<c>still_owed</c>) لَيسَ مِن رُموزِ <c>LeavesPayable</c>.</summary>
    /// <remarks><b>قُلِبَ بِاسمِه ثانِيَةً</b> (‏2026-11-05، الموجَة ١٢ G3 · ٢ — C-PAY-EXP-2؛ تَوصيفُه <c>7076249</c>) — كانَ <c>Cancelling_the_active_deal_keeps_the_invoice_its_disputed_sibling_may_still_pay_with</c>:
    /// إلغاءُ الأولى وَحدَها يُقبَلُ والفاتورَةُ تُبقيها الأُختُ مَفتوحَةً بِمَبلَغِها كُلِّه — فَإن استُؤنِفَت دُفِعَ ثَمَنُ المُلغاةِ مَعَها (د2 «الكل أو لا شيء» قَبلَ الدَفع). والآن إلغاؤها
    /// وَحدَها يُرَدُّ بِـ<c>deal_invoice_disputed</c> قَبلَ أَيِّ كِتابَةٍ ولا نِداءَ لِلبَوّابَة، والصَفحَةُ تَرسُمُ لِصاحِبِ المَتجَرِ «إلغاء الطَّلَب كامِلاً» الَّذي يَحسِمُ الأُختَ إلغاءً.</remarks>
    [Fact]
    public async Task Cancelling_the_active_deal_alone_while_its_sibling_is_disputed_is_refused_before_any_write()
    {
        var (w, _, ids) = await GatewayExpiryRaceTests.ExpiredCart();
        Assert.True((await w.Deals.DisputeAsync(Slug, ids[1], Guid.NewGuid(), "مالِك التَّطبيق", "شَكوى")).Ok);
        w.Moyasar.Requests.Clear();
        var saves = w.Docs.SaveCalls;

        var res = await w.Deals.CancelAsync(Slug, ids[0], DealWaybillCancelTests.StoreOwner, "طَلَبَه المُشتَري");

        output.WriteLine($"الإلغاء: {(res.Ok ? "ok" : res.Violation!.Code)} · البَوّابَة: {w.Moyasar.Requests.Count} · حِفظات: {w.Docs.SaveCalls - saves}");
        Assert.False(res.Ok);
        Assert.Equal(GatewayInvoiceShare.Disputed, res.Violation!.Code);
        Assert.Empty(w.Moyasar.Requests);
        Assert.Equal(saves, w.Docs.SaveCalls);
        Assert.Equal((DealStatus.Active, DealStatus.Disputed), (w.Docs.Read<Deal>(ids[0].ToString())!.Status, w.Docs.Read<Deal>(ids[1].ToString())!.Status));
    }
}
