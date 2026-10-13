using System.Net;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using Xunit;
using Xunit.Abstractions;
using static ACommerce.Platform.Tests.TenantGatewayEndpointsTests;

namespace ACommerce.Platform.Tests;

// ═══ فاتورَةٌ أُغلِقَت عِندَ البَوّابَة — رَمزُ التَحَقُّقِ ورابِطُ «أَكمِلِ الدَّفعَ» بَعدَه — تَوصيفُ «اليَوم» ═══════════════════════════════
//
// مُراجَعَةُ بَوّابَةِ المَوجَة 3b الثانِيَة (‏2026-10-13، G5 (xvii-g)): `GatewayPaymentProof` يُطلِقُ `gateway_invoice_unpaid` لِكُلِّ حالٍ سِوى «paid» —
// `initiated` و`failed` و`on_hold` (تُدفَعُ بَعد) و`canceled` و`expired` و`voided` و`refunded` (أُغلِقَت) سَواء — وصَفحَةُ العَودَةِ تَبقى تَرسُمُ
// «أَكمِلِ الدَّفعَ عَلى صَفحَةِ البَوّابَة» لِفاتورَةٍ مَيِّتَةٍ ولو رَأى التَحَقُّقُ إغلاقَها.
//
// **ما يُقلَبُ بِاسمِه في كوميتِ الإصلاح**: صَفُّ «Today_» أَدناه، وصَفُّ `GatewayPaymentProofTests.InvoiceUnpaid_fires_alone_for_every_status_but_paid`.

public class GatewayClosedInvoiceTests(ITestOutputHelper output)
{
    private const string PayUrl = "https://checkout.moyasar.com/invoices/efebc231-7795-4cfd-b69c-980fe4c02c49";

    [Fact]
    public async Task Today_a_verify_that_sees_a_canceled_invoice_answers_unpaid_and_the_pay_link_stays()
    {
        var w = await PaidCart();
        foreach (var id in w.Ids) await w.Deals.AttachRefAsync(Slug, id, GatewayPayments.InvoiceUrlRef, PayUrl);
        w.Moyasar.Then(HttpStatusCode.OK, PaidInvoice(w, "canceled"));

        var res = await w.Gateway.VerifyAsync(Slug, new DealPayer(Buyer), w.Ids[0].ToString("N"));
        var deal = w.Docs.Read<Deal>(w.Ids[0].ToString())!;
        output.WriteLine($"الرَمز: {res.Code} · الرابِط: {GatewayReturn.PayUrlOf(deal) ?? "لا"} · بَعدَه: {GatewayReturn.AfterVerify(res)}");

        Assert.Equal(GatewayPaymentProof.InvoiceUnpaid, res.Code);
        Assert.Equal(PayUrl, GatewayReturn.PayUrlOf(deal));
        Assert.Equal(PayUrl, GatewayReturn.PayUrlOf(w.Docs.Read<Deal>(w.Ids[1].ToString())!));
    }
}
