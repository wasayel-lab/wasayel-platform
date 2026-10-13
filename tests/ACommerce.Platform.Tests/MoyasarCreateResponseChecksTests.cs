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
//
// **قُلِبا** (‏2026-10-13، كوميتُ «fix(payments)» بَعدَ ‏e4a3f14): الرَدُّ يُقرَأُ بِشَرطِ أَخَواتِه — رابِطٌ مُطلَقٌ بِـhttp(s) ومُعَرِّفٌ uuid — وإلّا
// `gateway_unreadable` بِلا نِداءِ وَسم؛ فَيُلغي فَتحُ الفاتورَةِ صَفَقاتِه ولا يُعيدُ رابِطاً يُحَوَّلُ إلَيه. ومَعَهُما المُوجِب.

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

    /// <summary><b>قُلِبَ</b> (‏2026-10-13) — كانَ <c>Today_a_created_invoice_id_that_is_not_a_uuid_steers_the_metadata_update_to_another_path</c>
    /// (‏<c>e4a3f14</c>): مُعَرِّفٌ لَيسَ uuid «لا يُقرَأ»، ولا نِداءَ وَسمٍ بَعدَ الإنشاء.</summary>
    [Theory]
    [InlineData(MoyasarClientTests.InvoiceId + "/../../payments/7")]
    [InlineData("inv_1")]
    [InlineData("3fa85f64-5717-4562-b3fc-2c963f66afa6?x=1")]
    public async Task A_created_invoice_id_that_is_not_a_uuid_is_unreadable_and_no_update_is_sent(string odd)
    {
        var h = new MoyasarScriptedHandler()
            .Then(HttpStatusCode.Created, Created(odd, Hosted))
            .Then(HttpStatusCode.OK, Created(odd, Hosted));

        var res = await Client(h).CreateInvoiceAsync(MoyasarClientTests.Key, Draft());
        output.WriteLine($"«{odd}» ⇒ {res.Failure?.Code ?? "ok"} · النِداءات: {h.Requests.Count}");

        Assert.Equal(MoyasarFailures.Unreadable, res.Failure?.Code);
        Assert.Single(h.Requests);
        Assert.Equal(HttpMethod.Post, h.Requests[0].Method);
    }

    /// <summary><b>قُلِبَ</b> (‏2026-10-13) — كانَ <c>Today_a_created_invoice_url_that_is_not_http_is_what_the_checkout_redirects_to</c> (‏<c>e4a3f14</c>):
    /// رابِطٌ لَيسَ http(s) «لا يُقرَأ» — فَلا يُفتَحُ ولا يُعادُ رابِط، والصَفَقاتُ تُلغى بِاسمِ مُشتَريها ولا يُخصَمُ شَيء، ولا نِداءَ وَسم.</summary>
    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("/zz-gateway/pay/return")]
    [InlineData("ftp://checkout.moyasar.com/invoices/1")]
    public async Task A_created_invoice_url_that_is_not_http_is_unreadable_and_the_checkout_cancels_instead_of_redirecting(string url)
    {
        var w = await PaidCart(withSeller: true, invoice: false, bornForGateway: true);
        w.Moyasar.Then(HttpStatusCode.Created, Created(MoyasarClientTests.InvoiceId, url))
                 .Then(HttpStatusCode.OK, Created(MoyasarClientTests.InvoiceId, null));

        var opened = await w.Gateway.OpenInvoiceAsync(Slug, new DealPayer(Buyer), w.Ids, "مَتجَر", ReturnUrl, new L(new HttpContextAccessor()));
        var deal = w.Docs.Read<Deal>(w.Ids[0].ToString())!;
        output.WriteLine($"«{url}» ⇒ الفَتح: ok={opened.Ok} {opened.Code} · الرابِط: {opened.Url ?? "لا"} · الصَفقَة: {deal.Status} · النِداءات: {w.Moyasar.Requests.Count}");

        Assert.False(opened.Ok);
        Assert.Null(opened.Url);
        Assert.Equal(MoyasarFailures.Unreadable, opened.Code);
        Assert.All(w.Ids, id => Assert.Equal(DealStatus.Cancelled, w.Docs.Read<Deal>(id.ToString())!.Status));
        Assert.False(deal.Refs.ContainsKey(GatewayPayments.InvoiceUrlRef));
        Assert.Single(w.Moyasar.Requests);
        Assert.Empty(w.Platform.Calls);
    }

    /// <summary>المُوجِب: uuid ورابِطٌ بِـhttps ⇒ الوَسمُ عَلى مَسارِ الفاتورَةِ نَفسِها بِصيغَةِ <c>D</c>؛ ورابِطُ التَحديثِ إن عادَ بِغَيرِ http(s) لا يَحِلُّ
    /// مَحَلَّ رابِطِ الإنشاءِ المَفحوص، وإن عادَ بِـhttp(s) فَهُوَ الرابِط.</summary>
    [Theory]
    [InlineData(null, Hosted)]
    [InlineData("javascript:alert(2)", Hosted)]
    [InlineData("http://checkout.moyasar.com/invoices/3fa85f64-5717-4562-b3fc-2c963f66afa6?lang=en", "http://checkout.moyasar.com/invoices/3fa85f64-5717-4562-b3fc-2c963f66afa6?lang=en")]
    public async Task A_uuid_and_an_http_url_tag_the_invoices_own_path_and_the_returned_url_is_always_web(string? updatedUrl, string expected)
    {
        var h = new MoyasarScriptedHandler()
            .Then(HttpStatusCode.Created, Created(MoyasarClientTests.InvoiceId.ToUpperInvariant(), Hosted))
            .Then(HttpStatusCode.OK, Created(MoyasarClientTests.InvoiceId, updatedUrl));

        var res = await Client(h).CreateInvoiceAsync(MoyasarClientTests.Key, Draft());

        Assert.True(res.Ok, res.Failure?.Code);
        Assert.Equal($"/v1/invoices/{MoyasarClientTests.InvoiceId}", h.Requests[1].RequestUri!.AbsolutePath);
        Assert.Equal(expected, res.Value!.Url);
    }
}
