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

    /// <summary><b>تَوصيفُ «اليَوم»</b> (القاعِدَة ٣): مُلغاةٌ وأُختُها في «نِزاع» لَم تُقبَض — <b>لا سَبَبَ لِإبقاءِ الفاتورَة</b>، فَتُلغى عِندَ البَوّابَة.</summary>
    [Fact]
    public void Today_a_disputed_uncaptured_sibling_does_not_keep_the_invoice_open()
    {
        var codes = Codes(Listed(DealStatus.Cancelled), Listed(DealStatus.Disputed));
        output.WriteLine($"مُلغاةٌ + في نِزاع: [{string.Join(", ", codes)}]");
        Assert.Empty(codes);
    }

    /// <summary><b>تَوصيفُ «اليَوم»</b> بِالخِدمَةِ والمُلغي: إلغاءُ صاحِبِ المَتجَرِ الأولى وَحدَها (الأُختُ في «نِزاع» لا تُمسِكُها) ⇒ <c>PUT …/cancel</c> عِندَ البَوّابَة.</summary>
    [Fact]
    public async Task Today_cancelling_the_active_deal_closes_the_invoice_its_disputed_sibling_may_still_pay_with()
    {
        var (w, _, ids) = await GatewayExpiryRaceTests.ExpiredCart();
        Assert.True((await w.Deals.DisputeAsync(Slug, ids[1], Guid.NewGuid(), "مالِك التَّطبيق", "شَكوى")).Ok);
        w.Moyasar.Then(HttpStatusCode.OK, GatewayExpiryRaceTests.Invoice(ids, "initiated")).Then(HttpStatusCode.OK, GatewayExpiryRaceTests.Invoice(ids, "canceled"));

        var res = await w.Deals.CancelAsync(Slug, ids[0], DealWaybillCancelTests.StoreOwner, "طَلَبَه المُشتَري");

        var sent = w.Moyasar.Requests.Select(r => $"{r.Method} {r.RequestUri!.AbsolutePath}").ToArray();
        output.WriteLine($"الإلغاء: ok={res.Ok} · البَوّابَة: [{string.Join(", ", sent)}]");
        Assert.True(res.Ok, res.Violation?.Code);
        Assert.Equal($"PUT /v1/invoices/{InvoiceId}{MoyasarClient.CancelSuffix}", sent[^1]);
    }
}
