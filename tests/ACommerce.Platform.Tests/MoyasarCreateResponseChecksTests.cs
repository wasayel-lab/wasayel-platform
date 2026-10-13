using System.Net;
using System.Text.Json;
using ACommerce.Kit.Payments;
using ACommerce.Platform.I18n;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.Abstractions;
using static ACommerce.Platform.Tests.TenantGatewayEndpointsTests;

namespace ACommerce.Platform.Tests;

// ═══ رَدُّ «Create Invoice» يُقرَأُ بِشَرطِ الجَلبِ نَفسِه — مُعَرِّفٌ uuid ورابِطٌ بِـhttp(s) — تَوصيفُ «اليَوم» ═══════════════════════════
//
// مُراجَعَةُ بَوّابَةِ المَوجَة 3b (‏2026-10-13، G5 (xvi-g)): `MoyasarClient.CreateInvoiceAsync` تَبني مَسارَ `PUT` لِوَسمِ `metadata` مِن مُعَرِّفِ
// الفاتورَةِ كَما عادَ — بِلا فَحصِ uuid الَّذي يَفحَصُه `FetchInvoiceAsync` و`CancelInvoiceAsync` قَبلَ أَن يَدخُلَ المُعَرِّفُ العُنوان — فَمُعَرِّفٌ
// يَحمِلُ «/..» يَنقُلُ نِداءً بِمِفتاحِ المَتجَرِ إلى مَسارٍ آخَر؛ و`/checkout/submit` يُحَوِّلُ المُشتَريَ إلى رابِطِ الفاتورَةِ كَما عادَ بِلا فَحصِ
// http/https الَّذي يُطَبِّقُه `GatewayReturn.PayUrlOf` عَلى الرابِطِ نَفسِه في صَفحَةِ العَودَة.
//
// **ما يُقلَبُ بِاسمِه في كوميتِ الإصلاح**: صَفّا «Today_» أَدناه.

public class MoyasarCreateResponseChecksTests(ITestOutputHelper output)
{
    private const string ReturnUrl = "https://store.example/zz-gateway/pay/return?deal=1";
    private const string Hosted = "https://checkout.moyasar.com/invoices/3fa85f64-5717-4562-b3fc-2c963f66afa6";

    private static string Created(string id, string? url) => JsonSerializer.Serialize(new Dictionary<string, object?>
    {
        ["id"] = id, ["status"] = "initiated", ["amount"] = 15000, ["currency"] = "SAR", ["url"] = url,
        ["metadata"] = new Dictionary<string, string>(),
    });

    private static MoyasarInvoiceDraft Draft()
        => new(15000, "مَتجَر — طَلَبُ سَلَّة", ReturnUrl, ReturnUrl, new Dictionary<string, string> { ["deal_1"] = Guid.Empty.ToString("N") });

    private static MoyasarClient Client(MoyasarScriptedHandler h) => new(new HttpClient(h), NullLogger<MoyasarClient>.Instance);

    [Fact]
    public async Task Today_a_created_invoice_id_that_is_not_a_uuid_steers_the_metadata_update_to_another_path()
    {
        const string odd = MoyasarClientTests.InvoiceId + "/../../payments/7";
        var h = new MoyasarScriptedHandler()
            .Then(HttpStatusCode.Created, Created(odd, Hosted))
            .Then(HttpStatusCode.OK, Created(odd, Hosted));

        var res = await Client(h).CreateInvoiceAsync(MoyasarClientTests.Key, Draft());
        output.WriteLine($"النَتيجَة: ok={res.Ok} · النِداءات: {h.Requests.Count} · مَسارُ الثاني: {h.Requests.ElementAtOrDefault(1)?.Method} {h.Requests.ElementAtOrDefault(1)?.RequestUri?.AbsolutePath}");

        Assert.True(res.Ok);
        Assert.Equal(2, h.Requests.Count);
        Assert.Equal(HttpMethod.Put, h.Requests[1].Method);
        Assert.Equal("/v1/payments/7", h.Requests[1].RequestUri!.AbsolutePath);   // ← خارِجَ /v1/invoices/{id}
    }

    [Fact]
    public async Task Today_a_created_invoice_url_that_is_not_http_is_what_the_checkout_redirects_to()
    {
        var w = await PaidCart(withSeller: true, invoice: false, bornForGateway: true);
        w.Moyasar.Then(HttpStatusCode.Created, Created(MoyasarClientTests.InvoiceId, "javascript:alert(1)"))
                 .Then(HttpStatusCode.OK, Created(MoyasarClientTests.InvoiceId, null));

        var opened = await w.Gateway.OpenInvoiceAsync(Slug, new DealPayer(Buyer), w.Ids, "مَتجَر", ReturnUrl, new L(new HttpContextAccessor()));
        var deal = w.Docs.Read<Deal>(w.Ids[0].ToString())!;
        output.WriteLine($"الفَتح: ok={opened.Ok} · الرابِط: {opened.Url} · عَلى الصَفقَة: {deal.Refs.GetValueOrDefault(GatewayPayments.InvoiceUrlRef)} · PayUrlOf: {GatewayReturn.PayUrlOf(deal) ?? "null"}");

        Assert.True(opened.Ok);
        Assert.Equal("javascript:alert(1)", opened.Url);                    // ← `Results.Redirect(opened?.Url ?? …)` في النُقطَة
        Assert.Null(GatewayReturn.PayUrlOf(deal));                          // ← وصَفحَةُ العَودَةِ تَرُدُّه بِشَرطِها
        Assert.Equal(DealStatus.Active, deal.Status);
    }
}
