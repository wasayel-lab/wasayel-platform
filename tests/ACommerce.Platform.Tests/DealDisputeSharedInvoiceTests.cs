using System.Net;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using Xunit;
using Xunit.Abstractions;
using static ACommerce.Platform.Tests.TenantGatewayEndpointsTests;

namespace ACommerce.Platform.Tests;

// ═══ حَسمُ نِزاعٍ بِالإلغاءِ عَلى صَفقَةٍ مِن فاتورَةِ بَوّابَةٍ مَفتوحَةٍ مُشتَرَكَة — تَوصيفُ «اليَوم» (‏2026-10-29، مُراجَعَةُ تَيّارِ S5 الأولى عَلى `c961e95`) ═══
//
// قاعِدَةُ G5 M4 (‏2026-10-13، ‏ADR-032 §٨‑ز مُلحَقاً): «لا تُلغى وَحدَها صَفقَةٌ عَلى فاتورَةِ بَوّابَةٍ مَفتوحَةٍ تَدفَعُ لِأُختٍ قائِمَة» — فَتَبقى الفاتورَةُ
// مُساوِيَةً لِصَفَقاتِها القائِمَة. **والمَقيسُ عِندَ الرَأس**: حَسمُ النِزاعِ بِالإلغاءِ يَمُرُّ بِـ`CancelCoreAsync` نَفسِها، لَكِنّ `GatewayInvoiceShare.Validate`
// يَسأَلُ «أَتَدفَعُ الفاتورَةُ لِهذِه الصَفقَة؟» بِـ`GatewayInvoiceClosing.Owed` — `Active` وَحدَها — فَصَفقَةٌ في «نِزاع» لا تُحسَبُ عَلَيها، فَلا رَدّ: تُلغى وَحدَها،
// ويَسأَلُ المُلغي البَوّابَةَ فَيَجِدُ الأُختَ قائِمَةً فَيُبقي الفاتورَةَ (`gateway_invoice_still_owed`) — قابِلَةً لِلدَّفعِ بِمَبلَغِها كامِلاً ومِنه المُلغاة —
// و`LeavesPayable` لا يَعُدُّ هذا الرَمز، فَلا سَطرَ «فاتورَةٌ لَم تُلغَ» ولا إشعارَ لِصاحِبِ المَتجَر. وسِجِلُّ المالِك «أ» S5·٤ قالَ العَكس.
//
// **ما يُقلَبُ بِاسمِه في كوميتِ الإصلاح**: الصَفُّ أَدناه.

public class DealDisputeSharedInvoiceTests(ITestOutputHelper output)
{
    private static readonly DealCanceller StoreOwner = new(Other, "مالِك التَّطبيق", IsStoreAdmin: true);

    private static Deal Load(World w, Guid id) => w.Docs.Read<Deal>(id.ToString())!;
    private static string[] Sent(MoyasarScriptedHandler h)
        => h.Requests.Select(r => $"{r.Method} {r.RequestUri!.AbsolutePath}").ToArray();

    [Fact]
    public async Task Today_a_dispute_cancel_of_one_unpaid_deal_on_an_open_shared_invoice_cancels_it_alone_and_leaves_the_invoice_payable_unannounced()
    {
        var (w, deals, log) = await GatewayInvoiceShareTests.OpenedTwoDealInvoice();
        Assert.True((await deals.DisputeAsync(Slug, w.Ids[1], Other, "مالِك التَّطبيق", "لَم تَصِل")).Ok);
        var disputed = Load(w, w.Ids[1]);
        Assert.True(GatewayInvoiceShare.IsValid(disputed, await deals.InvoiceSiblingsAsync(Slug, disputed)));
        w.Moyasar.Then(HttpStatusCode.OK, MoyasarClientTests.InvoiceJson("initiated", 15000, metadata: GatewayPaymentProof.MetadataFor(w.Ids)));
        var requests = w.Moyasar.Requests.Count;

        var res = await deals.ResolveDisputeAsync(Slug, w.Ids[1], StoreOwner, DealDisputeResolution.Cancel, "حَسم");

        var after = Load(w, w.Ids[1]);
        output.WriteLine($"الحَسم: {(res.Ok ? "ok" : res.Violation!.Code)} · {Load(w, w.Ids[0]).Status}/{after.Status} · البَوّابَة: [{string.Join(" · ", Sent(w.Moyasar)[requests..])}] · " +
                         $"اللوغ: {log.Lines[^1]} · ما عَلى صاحِبِ المَتجَر: {DealNotices.OwnerActionOf(after)?.ToString() ?? "لا شَيء"}");
        Assert.True(res.Ok, res.Violation?.Code);
        Assert.Equal((DealStatus.Active, DealStatus.Cancelled), (Load(w, w.Ids[0]).Status, after.Status));
        Assert.Equal(new[] { $"GET /v1/invoices/{InvoiceId}" }, Sent(w.Moyasar)[requests..]);
        Assert.Contains(GatewayInvoiceClosing.StillOwed, log.Lines[^1], StringComparison.Ordinal);
        Assert.False(GatewayInvoiceCloser.LeavesPayable(GatewayInvoiceClosing.StillOwed));
        Assert.DoesNotContain(after.Timeline, e => e.Action == DealEvent.GatewayInvoiceLeftOpen);
        Assert.Null(DealNotices.OwnerActionOf(after));
    }
}
