using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ACommerce.Kit.Payments;
using ACommerce.Kit.Payments.Providers.Paddle;
using ACommerce.Kit.Payments.Providers.PayPal;
using ACommerce.Kit.Subscriptions;
using ACommerce.Platform.I18n;
using ACommerce.Platform.Providers;
using ACommerce.Templates.Customer.Marketplace.Billing;
using ACommerce.Templates.Customer.Marketplace.Services;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using ACommerce.Templates.Customer.Marketplace.Services.Incubator;
using ACommerce.Templates.Customer.Marketplace.Services.Providers;
using ACommerce.Templates.Customer.Marketplace.Services.Subscriptions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;
using static ACommerce.Platform.Tests.TenantGatewayEndpointsTests;

namespace ACommerce.Platform.Tests;

// ═══ ما يَبلُغُ ميسّر وPayPal وPaddle — يُثَبَّتُ عِندَ الأَبوابِ قَبلَ أَن تَمُرَّ الثَلاثُ بِالمُستَدعي المَعدود (‏2026-11-12، الموجَة ١٣ W13-6·١٤ — ADR-051) ═══
//
// **لِماذا قَبلَ التَبديل** (القاعِدَتانِ ٣ و١٣): البَندُ ١٤ يَنزِعُ `HttpClient` مِن `MoyasarClient` و`PayPalPaymentProvider` و`PaddlePaymentProvider`، ويَقلِبُ
// تَسجيلاتِها الثَلاثَ (`AddHttpClient<…>`) إلى تَسجيلٍ آخَر، ويَجعَلُ إنشاءَ فاتورَةِ ميسّر نِداءً مَعدوداً ووَسمَ بَياناتِها (`PUT`) نِداءً مُساعِداً، ويُغَيِّرُ تَواقيعَ
// عَمَلِيّاتِ العُدَدِ الثَلاث. ووَعدُه أَنّ **ما يَبلُغُ المُزَوِّدَ لا يَتَغَيَّر**: الطَلَبُ بِفِعلِه وعُنوانِه ورُؤوسِه وجِسمِه، وتَرتيبُ الطَلَبات، وما يَقولُه البابُ حينَ
// يَرفُضُ المُزَوِّدُ أَو يَسقُطُ أَو يَرمي. والوَعدُ لا يُحفَظُ بِاسمِه (القاعِدَة ٢): هذا المِلَفُّ يُثَبِّتُه ويُودَعُ وَحدَه، ثُمَّ يُعادُ بَعدَ التَبديلِ **بِلا تَعديلِ حَرف**.
//
// **والالتِقاطُ بِما يَبقى بَعدَ التَبديل** (التَصميم §6.3): `ConfigureHttpClientDefaults(… ConfigurePrimaryHttpMessageHandler(() => recorder))` يَسري عَلى كُلِّ
// عَميلٍ يَبنيه المَصنَع، والعُدَدُ بِتَسجيلاتِها هِيَ (`AddMoyasarGateway`، `AddPayPalSubscriptions`، `AddPaddleBilling`) لا بِبانٍ في الاختِبار. والأَبواب:
//   • ميسّر — `GatewayPayments` بِدَوالِّها العامَّةِ الَّتي لا تَتَغَيَّرُ تَواقيعُها (الفَتح، التَحَقُّق، المُهلَة، الإشعار، تَسجيلُ الإشعار)، و`DealsService.CancelAsync`
//     بِمُلغي الفاتورَة (`GatewayInvoiceCloser`)؛ عَلى عالَمِ وَثائِقَ في الذاكِرَة.
//   • PayPal وPaddle — نِقاطُهُما عَلى مُضيفٍ مُصَغَّر (`MapPayPalBilling`، `MapPaddleBilling`) يُنادى بِـHTTP، والمُشرِفُ بِجَلسَتِه.
//
// ولا يُسَمّي المِلَفُّ نَوعاً يولَدُ في البَند ولا عَمَلِيَّةً يَتَغَيَّرُ تَوقيعُها. **وبِلا شَبَكَةٍ خارِجِيَّة**. والأَداةُ تُقاسُ قَبلَ أَن يُوثَقَ بِها (القاعِدَة ١٠):
// كُلُّ صَفٍّ يَعُدُّ ما التَقَطَ ويَفشَلُ إن لَم يَكُن ما يَنتَظِر، والمُعَرِّفاتُ العَشوائِيَّةُ تُستَبدَلُ بِأَسمائِها قَبلَ المُقارَنَة.

public class PaymentsRequestShapeCharacterizationTests(ITestOutputHelper output)
{
    /// <summary>طَلَبٌ كَما بَلَغَ المُعالِجَ الأَوَّل — والرُؤوسُ الَّتي يَحمِلُها مُزَوِّدٌ بِعَينِه بِقيَمِها.</summary>
    internal sealed record Seen(string Method, string Uri, string Version, string RequestHeaders, string Authorization,
                                string RequestId, string Prefer, string ContentHeaders, string Body)
    {
        public string Line => $"{Method} {Uri} · v{Version} · رُؤوس [{RequestHeaders}] · auth {Authorization} · rid {RequestId} · prefer {Prefer} · "
                              + $"مُحتَوى [{ContentHeaders}] · جِسم {Body}";
    }

    /// <summary>المُعالِجُ الأَوَّل: يُجيبُ بِالرُدودِ المُرَتَّبَةِ أَو يَرمي — ويُسَجِّلُ كُلَّ طَلَب.</summary>
    internal sealed class Recorder : HttpMessageHandler
    {
        private readonly Queue<Func<HttpResponseMessage>> _replies = new();
        public List<Seen> Requests { get; } = new();

        public Recorder Then(HttpStatusCode status, string body)
        {
            _replies.Enqueue(() => new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
            return this;
        }

        public Recorder Throws(Exception error)
        {
            _replies.Enqueue(() => throw error);
            return this;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
            string Values(string name) => request.Headers.TryGetValues(name, out var v) ? string.Join(";", v) : "—";
            lock (Requests) Requests.Add(new Seen(
                request.Method.Method, request.RequestUri!.ToString(), request.Version.ToString(),
                string.Join(",", request.Headers.Select(h => h.Key).Order(StringComparer.Ordinal)),
                request.Headers.Authorization?.ToString() ?? "—",
                Values(PayPalPaymentProvider.RequestIdHeader), Values(PayPalPaymentProvider.PreferHeader),
                request.Content is null ? "" : string.Join(",", request.Content.Headers.Select(h => $"{h.Key}: {string.Join(";", h.Value)}").Order(StringComparer.Ordinal)),
                body));
            if (_replies.Count == 0) return new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("{}") };
            return _replies.Dequeue()();
        }
    }

    /// <summary>المُعَرِّفاتُ العَشوائِيَّةُ بِأَسمائِها — فَيُقارَنُ النَصُّ بِحَرفِه.</summary>
    private static string Named(string text, IReadOnlyList<Guid> deals)
    {
        for (var i = 0; i < deals.Count; i++)
            text = text.Replace(deals[i].ToString("D"), $"{{deal{i}}}", StringComparison.Ordinal)
                       .Replace(deals[i].ToString("N"), $"{{deal{i}:N}}", StringComparison.Ordinal);
        return text;
    }

    private void Print(string title, IEnumerable<Seen> seen, IReadOnlyList<Guid>? deals = null)
    {
        output.WriteLine($"── {title}");
        foreach (var s in seen) output.WriteLine("   " + Named(s.Line, deals ?? Array.Empty<Guid>()));
    }

    // ═══ ميسّر — عَبرَ `GatewayPayments` وتَسجيلِ العَميلِ نَفسِه ═══════════════════════════════════════════════════════════════

    private static readonly DateTimeOffset Opened = new(2026, 11, 12, 9, 0, 0, TimeSpan.Zero);
    private const string Basic = "Basic c2tfdGVzdF81Y0Q0MDlIcXdwb09JRGpxT1BKUGQ6";
    private const string Json = "Content-Type: application/json; charset=utf-8";
    private const string Return = "https://store.example/r";

    private sealed record Store(DocWorld Docs, DealsService Deals, ProviderVault Vault, MoyasarClient Client, Recorder Moyasar, Guid[] Ids,
                                ServiceProvider Services)
    {
        public GatewayPayments Gateway(DateTimeOffset now)
            => new(Deals, new TenantProviderService(Docs.Store), Vault, Client, null, new FixedClock(now));
    }

    /// <summary>عَميلُ ميسّر بِتَسجيلِه (<c>AddMoyasarGateway</c>) عَلى مُعالِجٍ يُسَجِّل، وسَلَّةُ صَفَقاتِ المُشتَري بِطَريقِ البَوّابَة، ورَبطُ دَفعٍ بِمِفتاحٍ مَختوم،
    /// وخِدمَةُ صَفَقاتٍ بِمُلغي الفاتورَةِ عَلى العَميلِ نَفسِه — والصَفَقاتُ بِلا فاتورَةٍ إلّا أَن يُطلَب.</summary>
    private static async Task<Store> StoreAsync(decimal[] amounts, bool invoice)
    {
        var recorder = new Recorder();
        var services = new ServiceCollection();
        services.AddLogging();
        services.ConfigureHttpClientDefaults(b => b.ConfigurePrimaryHttpMessageHandler(() => recorder));
        services.AddMoyasarGateway();
        var sp = services.BuildServiceProvider();
        var client = sp.GetRequiredService<MoyasarClient>();

        var docs = new DocWorld();
        var vault = NewVault();
        docs.Put(PaymentsBinding(vault));
        var deals = new DealsService(docs.Store, new DealMoneyClaimFirstTests.RecordingPaymentProvider(),
            new GatewayInvoiceCloser(new TenantProviderService(docs.Store), vault, client));
        var ids = new List<Guid>();
        foreach (var amount in amounts)
        {
            var deal = await deals.StartAsync(Slug, "marketplace", Buyer, "مُشتَرٍ", null, "سِلعَة", amount, GatewayPayments.MarkedAtBirth(new()
            {
                [CheckoutPayMethods.AttributeKey] = CheckoutPayMethods.Card,
            }, viaGateway: true));
            if (invoice) await deals.AttachRefAsync(Slug, deal.Id, GatewayPaymentProof.InvoiceRef, InvoiceId);
            await deals.AttachRefAsync(Slug, deal.Id, DealsService.ListingOwnerRef, Seller.ToString());
            await deals.BindListingOwnerAsync(Slug, deal.Id, Seller.ToString());
            ids.Add(deal.Id);
        }
        return new Store(docs, deals, vault, client, recorder, ids.ToArray(), sp);
    }

    private static L Lang() => new(new HttpContextAccessor());

    private static string Invoice(string status, IReadOnlyList<Guid> deals, long amount = 15000)
        => MoyasarClientTests.InvoiceJson(status, amount, metadata: GatewayPaymentProof.MetadataFor(deals.ToArray()));

    private static Deal Load(Store s, Guid id) => s.Docs.Read<Deal>(id.ToString())!;

    /// <summary><b>الفَتحُ</b>: ‏<c>POST /v1/invoices</c> بِالحُقولِ السِتَّةِ (والتاريخُ مِن ساعَةِ الخِدمَة) ثُمَّ <c>PUT /v1/invoices/{id}</c> بِـ<c>metadata</c>
    /// وَحدَها — كِلاهُما بِـ<c>Basic</c> مِن المِفتاحِ المَفكوك؛ والرابِطُ يَعودُ والمَراجِعُ تُكتَب.</summary>
    [Fact]
    public async Task Opening_an_invoice_posts_the_documented_fields_then_puts_the_metadata()
    {
        var s = await StoreAsync([100m, 50m], invoice: false);
        s.Moyasar.Then(HttpStatusCode.Created, MoyasarClientTests.InvoiceJson("initiated", metadata: new Dictionary<string, string>()))
                 .Then(HttpStatusCode.OK, Invoice("initiated", s.Ids));

        var opened = await s.Gateway(Opened).OpenInvoiceAsync(Slug, new DealPayer(Buyer), s.Ids, "مَتجَر", Return, Lang());
        Print("open", s.Moyasar.Requests, s.Ids);

        Assert.True(opened.Ok, opened.Code);
        Assert.Equal("http://checkout.moyasar.com/invoices/efebc231-7795-4cfd-b69c-980fe4c02c49?lang=en", opened.Url);
        Assert.Equal(new[]
        {
            """
            POST https://api.moyasar.com/v1/invoices · v1.1 · رُؤوس [Authorization] · auth Basic c2tfdGVzdF81Y0Q0MDlIcXdwb09JRGpxT1BKUGQ6 · rid — · prefer — · مُحتَوى [Content-Length: 314,Content-Type: application/json; charset=utf-8] · جِسم {"amount":15000,"currency":"SAR","description":"\u0637\u064E\u0644\u064E\u0628\u064F\u0643\u064E \u0645\u0650\u0646 \u0645\u064E\u062A\u062C\u064E\u0631\u0650 \u0645\u064E\u062A\u062C\u064E\u0631","success_url":"https://store.example/r","back_url":"https://store.example/r","expired_at":"2026-11-13T09:00:00.000Z"}
            """,
            """
            PUT https://api.moyasar.com/v1/invoices/3fa85f64-5717-4562-b3fc-2c963f66afa6 · v1.1 · رُؤوس [Authorization] · auth Basic c2tfdGVzdF81Y0Q0MDlIcXdwb09JRGpxT1BKUGQ6 · rid — · prefer — · مُحتَوى [Content-Length: 102,Content-Type: application/json; charset=utf-8] · جِسم {"metadata":{"deal_1":"{deal0:N}","deal_2":"{deal1:N}"}}
            """,
        }, s.Moyasar.Requests.Select(r => Named(r.Line, s.Ids)).ToArray());
        foreach (var id in s.Ids)
            Assert.Equal(InvoiceId, Load(s, id).Refs[GatewayPaymentProof.InvoiceRef]);
    }

    /// <summary><b>الإنشاءُ قُبِلَ ووَسمُ البَياناتِ سَقَط</b>: ‏<c>POST</c> ‏201 ثُمَّ <c>PUT</c> ‏503 — الطَلَبانِ بِحالِهِما، والرَمزُ رَمزُ الوَسم
    /// (<see cref="MoyasarFailures.Unreachable"/>)، والصَفَقَتانِ تُلغَيانِ ولا مَرجِعَ فاتورَةٍ عَلَيهِما.</summary>
    [Fact]
    public async Task A_metadata_put_that_fails_after_the_create_answers_its_code_and_cancels_the_deals()
    {
        var s = await StoreAsync([100m, 50m], invoice: false);
        s.Moyasar.Then(HttpStatusCode.Created, MoyasarClientTests.InvoiceJson("initiated", metadata: new Dictionary<string, string>()))
                 .Then(HttpStatusCode.ServiceUnavailable, "{\"type\":\"api_error\"}");

        var opened = await s.Gateway(Opened).OpenInvoiceAsync(Slug, new DealPayer(Buyer), s.Ids, "مَتجَر", Return, Lang());
        Print("open, put 503", s.Moyasar.Requests, s.Ids);

        Assert.Equal(MoyasarFailures.Unreachable, opened.Code);
        Assert.Equal(new[] { "POST https://api.moyasar.com/v1/invoices", $"PUT https://api.moyasar.com/v1/invoices/{InvoiceId}" },
                     s.Moyasar.Requests.Select(r => $"{r.Method} {r.Uri}").ToArray());
        foreach (var id in s.Ids)
        {
            Assert.Equal(DealStatus.Cancelled, Load(s, id).Status);
            Assert.False(Load(s, id).Refs.ContainsKey(GatewayPaymentProof.InvoiceRef));
        }
    }

    /// <summary><b>التَحَقُّق</b>: ‏<c>GET /v1/invoices/{id}</c> واحِدٌ بِلا جِسمٍ بِـ<c>Basic</c> — والدَفعُ يَثبُتُ عَلى الصَفقَتَين.</summary>
    [Fact]
    public async Task Verifying_fetches_the_invoice_once_with_the_store_key()
    {
        var s = await StoreAsync([100m, 50m], invoice: true);
        s.Moyasar.Then(HttpStatusCode.OK, Invoice("paid", s.Ids));

        var res = await s.Gateway(Opened).VerifyAsync(Slug, new DealPayer(Buyer), s.Ids[0].ToString("N"), Lang());
        Print("verify", s.Moyasar.Requests, s.Ids);

        Assert.True(res.Ok, res.Code);
        Assert.Equal(s.Ids.Order(), res.Confirmed.Order());
        Assert.Equal(new[] { $"GET https://api.moyasar.com/v1/invoices/{InvoiceId} · v1.1 · رُؤوس [Authorization] · auth {Basic} · rid — · prefer — · مُحتَوى [] · جِسم " },
                     s.Moyasar.Requests.Select(r => r.Line).ToArray());
    }

    /// <summary><b>المُهلَة</b>: فاتورَةٌ فُتِحَت ومَضَت مُدَّتُها وما زالَت مَفتوحَة — جَلبُ المُهلَة، ثُمَّ جَلبٌ لِكُلِّ صَفقَةٍ يُلغيها الإلغاءُ كُلُّه، ثُمَّ
    /// <c>PUT …/cancel</c> بِلا جِسمٍ عَلى الأَخيرَة؛ والرَمزُ «انتَهَت» والصَفَقَتانِ مُلغاتان.</summary>
    [Fact]
    public async Task An_expired_invoice_is_fetched_then_each_cancel_fetches_again_and_the_last_cancels_it()
    {
        var s = await StoreAsync([100m, 50m], invoice: false);
        s.Moyasar.Then(HttpStatusCode.Created, MoyasarClientTests.InvoiceJson("initiated", metadata: new Dictionary<string, string>()))
                 .Then(HttpStatusCode.OK, Invoice("initiated", s.Ids));
        Assert.True((await s.Gateway(Opened).OpenInvoiceAsync(Slug, new DealPayer(Buyer), s.Ids, "مَتجَر", Return, Lang())).Ok);
        s.Moyasar.Requests.Clear();
        s.Moyasar.Then(HttpStatusCode.OK, Invoice("initiated", s.Ids)).Then(HttpStatusCode.OK, Invoice("initiated", s.Ids))
                 .Then(HttpStatusCode.OK, Invoice("initiated", s.Ids)).Then(HttpStatusCode.OK, Invoice("canceled", s.Ids));

        var res = await s.Gateway(Opened + GatewayPayments.InvoiceLifetime + TimeSpan.FromSeconds(1)).ExpireAsync(Slug, s.Ids[0], Lang());
        Print("expire", s.Moyasar.Requests, s.Ids);

        Assert.Equal(GatewayReturn.InvoiceExpired, res.Code);
        Assert.Equal(s.Ids.Order(), res.Expired.Order());
        var get = $"GET https://api.moyasar.com/v1/invoices/{InvoiceId} · v1.1 · رُؤوس [Authorization] · auth {Basic} · rid — · prefer — · مُحتَوى [] · جِسم ";
        Assert.Equal(new[]
        {
            get, get, get,
            $"PUT https://api.moyasar.com/v1/invoices/{InvoiceId}{MoyasarClient.CancelSuffix} · v1.1 · رُؤوس [Authorization] · auth {Basic} · rid — · prefer — · مُحتَوى [] · جِسم ",
        }, s.Moyasar.Requests.Select(r => r.Line).ToArray());
        foreach (var id in s.Ids) Assert.Equal(DealStatus.Cancelled, Load(s, id).Status);
    }

    /// <summary><b>الإلغاءُ بِيَدِ المُشتَري</b>: صَفقَةٌ واحِدَةٌ عَلى فاتورَةٍ مَفتوحَة — جَلبٌ ثُمَّ <c>PUT …/cancel</c>، والإلغاءُ قائِم.</summary>
    [Fact]
    public async Task A_buyer_cancel_of_the_last_deal_fetches_then_cancels_the_invoice()
    {
        var s = await StoreAsync([100m], invoice: true);
        s.Moyasar.Then(HttpStatusCode.OK, Invoice("initiated", s.Ids, 10000)).Then(HttpStatusCode.OK, Invoice("canceled", s.Ids, 10000));

        var res = await s.Deals.CancelAsync(Slug, s.Ids[0], new DealCanceller(Buyer, "مُشتَرٍ", IsStoreAdmin: false), "عَدَلتُ");
        Print("buyer cancel", s.Moyasar.Requests, s.Ids);

        Assert.True(res.Ok);
        Assert.Equal(new[]
        {
            $"GET https://api.moyasar.com/v1/invoices/{InvoiceId} · v1.1 · رُؤوس [Authorization] · auth {Basic} · rid — · prefer — · مُحتَوى [] · جِسم ",
            $"PUT https://api.moyasar.com/v1/invoices/{InvoiceId}{MoyasarClient.CancelSuffix} · v1.1 · رُؤوس [Authorization] · auth {Basic} · rid — · prefer — · مُحتَوى [] · جِسم ",
        }, s.Moyasar.Requests.Select(r => r.Line).ToArray());
        Assert.Equal(DealStatus.Cancelled, Load(s, s.Ids[0]).Status);
    }

    private const string NoticeSecret = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    /// <summary><b>الإشعار</b>: إشعارُ دَفعٍ قَبِلَته بَوّابَةُ السِرِّ يُجلَبُ بِه الفاتورَةُ الَّتي يُسَمّيها — <c>GET</c> واحِد — ويُكتَبُ الدَفع.</summary>
    [Fact]
    public async Task A_paid_notice_fetches_the_invoice_it_names()
    {
        var s = await StoreAsync([100m, 50m], invoice: true);
        s.Moyasar.Then(HttpStatusCode.OK, Invoice("paid", s.Ids));
        var notice = GatewayNoticeGate.Admit(NoticeSecret, JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["id"] = "evt-1", ["type"] = MoyasarClient.PaymentPaidEvent, ["secret_token"] = NoticeSecret,
            ["data"] = new Dictionary<string, object?> { ["id"] = "pay-1", ["status"] = "paid", ["invoice_id"] = InvoiceId },
        })).Notice!;

        var res = await s.Gateway(Opened).ConfirmNoticeAsync(Slug, notice);
        Print("notice", s.Moyasar.Requests, s.Ids);

        Assert.True(res.Ok, res.Code);
        Assert.Equal(new[] { $"GET https://api.moyasar.com/v1/invoices/{InvoiceId} · v1.1 · رُؤوس [Authorization] · auth {Basic} · rid — · prefer — · مُحتَوى [] · جِسم " },
                     s.Moyasar.Requests.Select(r => r.Line).ToArray());
    }

    /// <summary><b>تَسجيلُ الإشعار</b>: ‏<c>POST /v1/webhooks</c> بِالحُقولِ الأَربَعَة (والسِرُّ الجَديدُ بِاسمِه) — والرَمزُ لا شَيء، والمُعَرِّفُ في الرَبط.</summary>
    [Fact]
    public async Task Connecting_notices_posts_the_webhook_fields()
    {
        var s = await StoreAsync([100m], invoice: false);
        s.Moyasar.Then(HttpStatusCode.Created, "{\"id\":\"wh-1\"}");

        var code = await s.Gateway(Opened).ConnectNoticesAsync(Slug, ProviderCapabilities.Payments, "https://store.example");
        Print("webhook", s.Moyasar.Requests);

        Assert.Null(code);
        Assert.Equal(new[]
        {
            """
            POST https://api.moyasar.com/v1/webhooks · v1.1 · رُؤوس [Authorization] · auth Basic c2tfdGVzdF81Y0Q0MDlIcXdwb09JRGpxT1BKUGQ6 · rid — · prefer — · مُحتَوى [Content-Length: 188,Content-Type: application/json; charset=utf-8] · جِسم {"http_method":"post","url":"https://store.example/zz-gateway/payments/notify","shared_secret":"{secret}","events":["payment_paid"]}
            """,
        }, s.Moyasar.Requests.Select(r => Regex.Replace(r.Line, "\"shared_secret\":\"[^\"]+\"", "\"shared_secret\":\"{secret}\"")).ToArray());
        Assert.Equal("wh-1", s.Docs.Read<TenantProviderBinding>(ProviderCapabilities.Payments)!.WebhookId);
    }

    /// <summary><b>ما يَقولُه البابُ حينَ يَرفُضُ المُزَوِّدُ أَو يَسقُطُ أَو يَرمي</b> — رَمزُ العَميلِ نَفسُه لِكُلِّ جَواب، وطَلَبٌ واحِدٌ لِكُلٍّ.</summary>
    [Theory]
    [InlineData("503", MoyasarFailures.Unreachable)]
    [InlineData("500", MoyasarFailures.Unreachable)]
    [InlineData("429", MoyasarFailures.RateLimited)]
    [InlineData("404", MoyasarFailures.Refused)]
    [InlineData("401", MoyasarFailures.Refused)]
    [InlineData("throw", MoyasarFailures.Unreachable)]
    [InlineData("unreadable", MoyasarFailures.Unreadable)]
    public async Task Each_gateway_answer_keeps_its_code(string answer, string code)
    {
        var s = await StoreAsync([100m, 50m], invoice: true);
        _ = answer switch
        {
            "throw" => s.Moyasar.Throws(new HttpRequestException("connection refused")),
            "unreadable" => s.Moyasar.Then(HttpStatusCode.OK, "<html>ok</html>"),
            _ => s.Moyasar.Then((HttpStatusCode)int.Parse(answer, System.Globalization.CultureInfo.InvariantCulture), "{\"type\":\"error\"}"),
        };

        var res = await s.Gateway(Opened).VerifyAsync(Slug, new DealPayer(Buyer), s.Ids[0].ToString("N"), Lang());
        output.WriteLine($"{answer} ⇒ {res.Code} · طَلَبات {s.Moyasar.Requests.Count}");

        Assert.Equal(code, res.Code);
        Assert.Single(s.Moyasar.Requests);
        foreach (var id in s.Ids) Assert.Equal(DealStatus.Active, Load(s, id).Status);
    }

    // ═══ PayPal وPaddle — عَبرَ نِقاطِهِما وتَسجيلَيهِما ═══════════════════════════════════════════════════════════════════════

    private const string BillingSlug = "ejar";
    private static readonly DateTime Now = new(2026, 11, 12, 12, 0, 0, DateTimeKind.Utc);

    private static TenantPlan Plan() => new()
    {
        Id = BillingSlug, PlanId = "manual", Status = PlatformPlanStatuses.Active,
        StartsAt = Now.AddDays(-20), ExpiresAt = Now.AddDays(10), GraceDays = 14,
    };

    /// <summary>مُضيفٌ مُصَغَّرٌ بِنِقاطِ الفَوتَرَةِ وَحدَها، والعُدَّتانِ بِتَسجيلَيهِما مِن الإعداد (<c>AddPayPalSubscriptions</c>، <c>AddPaddleBilling</c>) عَلى
    /// مُعالِجٍ يُسَجِّل، والوَثائِقُ في الذاكِرَة، والمُشرِفُ بِجَلسَتِه.</summary>
    private static async Task<(WebApplication App, HttpClient Client)> BillingHostAsync(DocWorld world, Recorder recorder, Guid? admin)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.Configuration[PayPalEnvironment.ClientIdKey] = "AY-client";
        builder.Configuration[PayPalEnvironment.ClientSecretKey] = "very-secret";
        builder.Configuration[PayPalEnvironment.EnvironmentKey] = PayPalEnvironment.Live;
        builder.Configuration[PayPalEnvironment.WebhookIdKey] = "WH-TEST";
        builder.Configuration[PaddleEnvironment.EnvironmentKey] = PaddleEnvironment.Live;
        builder.Configuration[PaddleEnvironment.ApiKeyKey] = "pdl_apikey_1111111111111111111111111";
        builder.Configuration[PaddleEnvironment.WebhookSecretKey] = "pdl_ntfset_0000000000000000000000000";
        builder.Configuration[PaddleEnvironment.ClientTokenKey] = "live_token_2222";
        builder.Configuration[PaddleEnvironment.DefaultPaymentLinkKey] = "https://wasayel.example/billing/paddle/checkout.html";
        builder.Services.ConfigureHttpClientDefaults(b => b.ConfigurePrimaryHttpMessageHandler(() => recorder));
        builder.Services.AddPayPalSubscriptions(builder.Configuration);
        builder.Services.AddPaddleBilling(builder.Configuration);
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddSingleton(world.Store);
        builder.Services.AddScoped(_ => world.Session);
        builder.Services.AddSingleton(new ACommerce.Templates.Customer.Marketplace.Services.Audit.AuditWriter(world.Store));
        builder.Services.AddScoped<StudioAuth>();
        builder.Services.AddScoped<L>();

        using var probe = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        var app = builder.Build();
        app.Urls.Add($"http://127.0.0.1:{port}");
        app.MapPayPalBilling();
        app.MapPaddleBilling();
        await app.StartAsync();
        var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
        if (admin is { } a) PayPalEndpointBehaviourTests.SignIn(client, a);
        return (app, client);
    }

    private Task<(HttpResponseMessage Response, DocWorld World, Recorder Calls)> PostAsync(
        string path, HttpContent content, Recorder calls, bool asAdmin, params object[] docs)
        => SendAsync(() => new HttpRequestMessage(HttpMethod.Post, path) { Content = content }, calls, asAdmin, docs);

    private async Task<(HttpResponseMessage Response, DocWorld World, Recorder Calls)> SendAsync(
        Func<HttpRequestMessage> request, Recorder calls, bool asAdmin, params object[] docs)
    {
        var admin = Guid.NewGuid();
        var world = new DocWorld();
        foreach (var d in docs) world.Put(d);
        if (asAdmin) world.Put(PayPalEndpointBehaviourTests.Admin(admin));
        var (app, client) = await BillingHostAsync(world, calls, asAdmin ? admin : null);
        try
        {
            var sent = request();
            var path = sent.RequestUri!.OriginalString;
            var response = await client.SendAsync(sent);
            output.WriteLine($"{path} ⇒ {(int)response.StatusCode} {response.Headers.Location} · حِفظ {world.SaveCalls}");
            Print(path, calls.Requests);
            return (response, world, calls);
        }
        finally
        {
            client.Dispose();
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    private const string Token = "{\"access_token\":\"A21AA\",\"expires_in\":32400}";
    private const string TokenLine =
            """
            POST https://api-m.paypal.com/v1/oauth2/token · v1.1 · رُؤوس [Authorization] · auth Basic QVktY2xpZW50OnZlcnktc2VjcmV0 · rid — · prefer — · مُحتَوى [Content-Length: 29,Content-Type: application/x-www-form-urlencoded] · جِسم grant_type=client_credentials
            """;

    private static FormUrlEncodedContent Form(params (string Key, string Value)[] fields)
        => new(fields.ToDictionary(f => f.Key, f => f.Value));

    /// <summary><b>رِسالَةُ PayPal</b>: رُؤوسُ التَوقيعِ كامِلَة ⇒ الرَمزُ ثُمَّ <c>POST …/verify-webhook-signature</c> بِـ<c>Bearer</c> والجِسمُ مُلصَقاً بِبايتاتِه؛ و«FAILURE» ⇒ رَفض.</summary>
    [Fact]
    public async Task A_paypal_webhook_asks_paypal_to_verify_the_signature_before_reading()
    {
        const string body = "{\"id\":\"WH-1\",\"event_type\":\"BILLING.SUBSCRIPTION.ACTIVATED\",\"resource\":{\"id\":\"I-1\",\"custom_id\":\"ejar\"}}";
        var calls = new Recorder().Then(HttpStatusCode.OK, Token).Then(HttpStatusCode.OK, "{\"verification_status\":\"FAILURE\"}");

        var (response, world, _) = await SendAsync(() => PayPalEndpointBehaviourTests.Signed(body), calls, asAdmin: false);

        Assert.Equal(400, (int)response.StatusCode);
        Assert.Equal(0, world.SaveCalls);
        Assert.Equal(new[] { TokenLine, """
            POST https://api-m.paypal.com/v1/notifications/verify-webhook-signature · v1.1 · رُؤوس [Authorization] · auth Bearer A21AA · rid — · prefer — · مُحتَوى [Content-Length: 311,Content-Type: application/json; charset=utf-8] · جِسم {"auth_algo":"SHA256withRSA","cert_url":"https://api.paypal.com/cert.pem","transmission_id":"TX-1","transmission_sig":"sig==","transmission_time":"2026-08-24T12:00:00Z","webhook_id":"WH-TEST","webhook_event":{"id":"WH-1","event_type":"BILLING.SUBSCRIPTION.ACTIVATED","resource":{"id":"I-1","custom_id":"ejar"}}}
            """ }, calls.Requests.Select(r => r.Line).ToArray());
    }

    /// <summary><b>خُطَّةُ PayPal</b>: الرَمزُ ثُمَّ <c>POST /v1/catalogs/products</c> ثُمَّ <c>POST /v1/billing/plans</c>، بِـ<c>PayPal-Request-Id</c> حَتمِيٍّ لِكُلٍّ.</summary>
    [Fact]
    public async Task A_paypal_plan_creates_the_product_then_the_plan()
    {
        var calls = new Recorder().Then(HttpStatusCode.OK, Token)
            .Then(HttpStatusCode.Created, "{\"id\":\"PROD-XXCD1234QWER65782\"}")
            .Then(HttpStatusCode.Created, "{\"id\":\"P-5ML4271244454362WXNWU5NQ\"}");

        var (response, world, _) = await PostAsync($"/admin/tenants/{BillingSlug}/plan/paypal-plan",
            Form(("name", "باقَةُ الشَهر"), ("price", "49"), ("currency", "USD"), ("period", "MONTH")), calls, asAdmin: true, Plan());

        // **وتَقِفُ النُقطَةُ بَعدَ نِداءَي PayPal بِـ500 في عالَمِ الذاكِرَة**: إسقاطُ روابِطِ الخُطَّةِ القَديمَة (`DropStaleLinksAsync`) يَستَعلِمُ
        // (`Query<TenantPlan>`)، والعالَمُ لا يُجيبُ الاستِعلامَ (`DocWorld` يَرمي لِما لَم يُنَفِّذه) — فَالصَفُّ يُثَبِّتُ ما يَبلُغُ PayPal وحالَ هذا العالَم،
        // وإسقاطُ الروابِطِ مُثَبَّتٌ عَلى Postgres (`PayPalPlanRebind*`).
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(0, world.SaveCalls);
        Assert.Equal(new[] { TokenLine, """
            POST https://api-m.paypal.com/v1/catalogs/products · v1.1 · رُؤوس [Authorization,PayPal-Request-Id] · auth Bearer A21AA · rid wasayel-product-4fa97a37f3f9ca42d2adcdf2 · prefer — · مُحتَوى [Content-Type: application/json; charset=utf-8] · جِسم {"name":"\u0628\u0627\u0642\u064E\u0629\u064F \u0627\u0644\u0634\u064E\u0647\u0631","type":"SERVICE","category":"SOFTWARE"}
            """, """
            POST https://api-m.paypal.com/v1/billing/plans · v1.1 · رُؤوس [Authorization,PayPal-Request-Id] · auth Bearer A21AA · rid wasayel-plan-eb0b6828f46837a004694bb1 · prefer — · مُحتَوى [Content-Type: application/json; charset=utf-8] · جِسم {"product_id":"PROD-XXCD1234QWER65782","name":"\u0628\u0627\u0642\u064E\u0629\u064F \u0627\u0644\u0634\u064E\u0647\u0631","status":"ACTIVE","billing_cycles":[{"tenure_type":"REGULAR","sequence":1,"total_cycles":0,"frequency":{"interval_unit":"MONTH","interval_count":1},"pricing_scheme":{"fixed_price":{"value":"49.00","currency_code":"USD"}}}],"payment_preferences":{"auto_bill_outstanding":true,"setup_fee_failure_action":"CONTINUE","payment_failure_threshold":3}}
            """ }, calls.Requests.Select(r => r.Line).ToArray());
    }

    /// <summary><b>رابِطُ الاشتِراك</b>: الرَمزُ ثُمَّ <c>POST /v1/billing/subscriptions</c> بِالخُطَّةِ المَربوطَةِ ومَرجِعِ المَتجَرِ ومِفتاحِ مَرَّةٍ واحِدَة.</summary>
    [Fact]
    public async Task A_paypal_link_creates_the_subscription()
    {
        var calls = new Recorder().Then(HttpStatusCode.OK, Token).Then(HttpStatusCode.Created,
            "{\"id\":\"I-SUB-3\",\"status\":\"APPROVAL_PENDING\",\"links\":[{\"rel\":\"approve\",\"href\":\"https://www.paypal.com/webapps/billing/subscriptions?ba_token=BA-3\"}]}");
        var binding = new PlatformPlanPayPal
        {
            Id = "manual", ProductId = "PROD-1", PlanId = "P-10", Name = "باقَة", Amount = 10m, Currency = "USD", IntervalUnit = "MONTH",
        };

        var (response, world, _) = await PostAsync($"/admin/tenants/{BillingSlug}/plan/paypal-link", Form(), calls, asAdmin: true, Plan(), binding);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("https://www.paypal.com/webapps/billing/subscriptions?ba_token=BA-3", world.Read<TenantPlan>(BillingSlug)!.PayPalApproveUrl);
        Assert.Equal(new[] { TokenLine, """
            POST https://api-m.paypal.com/v1/billing/subscriptions · v1.1 · رُؤوس [Authorization,PayPal-Request-Id] · auth Bearer A21AA · rid wsl-s-dc9edcf5b4e0500276c4f432 · prefer — · مُحتَوى [Content-Type: application/json; charset=utf-8] · جِسم {"plan_id":"P-10","custom_id":"ejar"}
            """ }, calls.Requests.Select(r => r.Line).ToArray());
    }

    /// <summary><b>طَلَبُ دَفعٍ مَرِن</b>: الرَمزُ ثُمَّ <c>POST /v2/checkout/orders</c> بِمَرجِعِنا ومِفتاحِه.</summary>
    [Fact]
    public async Task A_paypal_order_creates_the_order()
    {
        var calls = new Recorder().Then(HttpStatusCode.OK, Token).Then(HttpStatusCode.Created,
            "{\"id\":\"5O1\",\"status\":\"PAYER_ACTION_REQUIRED\",\"links\":[{\"rel\":\"payer-action\",\"href\":\"https://www.paypal.com/checkoutnow?token=5O1\"}]}");

        var (response, _, _) = await PostAsync($"/admin/tenants/{BillingSlug}/plan/paypal-order",
            Form(("amount", "49"), ("currency", "USD"), ("days", "30"), ("description", "اشتِراكُ شَهر")), calls, asAdmin: true, Plan());

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(new[] { TokenLine, """
            POST https://api-m.paypal.com/v2/checkout/orders · v1.1 · رُؤوس [Authorization,PayPal-Request-Id] · auth Bearer A21AA · rid wsl-o-cf9953c493aa87485a37126c · prefer — · مُحتَوى [Content-Type: application/json; charset=utf-8] · جِسم {"intent":"CAPTURE","purchase_units":[{"reference_id":"wsl-ejar-cf9953c493aa87485a37126c","amount":{"currency_code":"USD","value":"49.00"},"custom_id":"wsl-ejar-cf9953c493aa87485a37126c","soft_descriptor":"WASAYEL","description":"\u0627\u0634\u062A\u0650\u0631\u0627\u0643\u064F \u0634\u064E\u0647\u0631"}],"payment_source":{"paypal":{"experience_context":{"brand_name":"Wasayel","locale":"ar-SA","landing_page":"GUEST_CHECKOUT","shipping_preference":"NO_SHIPPING","user_action":"PAY_NOW","payment_method_preference":"IMMEDIATE_PAYMENT_REQUIRED","return_url":"{origin}/billing/paypal/return?ref=wsl-ejar-cf9953c493aa87485a37126c","cancel_url":"{origin}/billing/paypal/cancel?ref=wsl-ejar-cf9953c493aa87485a37126c"}}}}
            """ }, calls.Requests.Select(r => Regex.Replace(r.Line, @"http://127\.0\.0\.1:\d+", "{origin}")).ToArray());
    }

    /// <summary><b>«التَقِط الآن»</b>: الرَمزُ ثُمَّ <c>POST /v2/checkout/orders/{id}/capture</c> بِـ<c>{}</c> و<c>Prefer</c> ومِفتاحِ مَرَّةٍ واحِدَة.</summary>
    [Fact]
    public async Task A_paypal_capture_captures_the_approved_order()
    {
        var calls = new Recorder().Then(HttpStatusCode.OK, Token).Then(HttpStatusCode.Created,
            "{\"id\":\"5O190127TN364715T\",\"status\":\"COMPLETED\",\"purchase_units\":[{\"payments\":{\"captures\":[{\"id\":\"3C6\",\"status\":\"COMPLETED\",\"seller_receivable_breakdown\":{\"net_amount\":{\"value\":\"46.30\"}}}]}}]}");
        var order = new PayPalOrderRecord
        {
            Id = "wsl-ejar-abc123", TenantSlug = BillingSlug, PlanId = "manual", Amount = 49m, Currency = "USD", Days = 30,
            OrderId = "5O190127TN364715T", ApproveUrl = "https://www.paypal.com/checkoutnow?token=5O1",
            Status = PayPalOrderStatuses.Approved, CreatedAt = Now, At = Now,
        };

        var (response, _, _) = await PostAsync($"/admin/tenants/{BillingSlug}/plan/paypal-capture",
            Form((PayPalOrderSurface.ReferenceField, "wsl-ejar-abc123")), calls, asAdmin: true, Plan(), order);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(new[] { TokenLine, """
            POST https://api-m.paypal.com/v2/checkout/orders/5O190127TN364715T/capture · v1.1 · رُؤوس [Authorization,PayPal-Request-Id,Prefer] · auth Bearer A21AA · rid wsl-c-a21c09388c5ae91308aa81a2 · prefer return=representation · مُحتَوى [Content-Length: 2,Content-Type: application/json; charset=utf-8] · جِسم {}
            """ }, calls.Requests.Select(r => r.Line).ToArray());
    }

    /// <summary><b>رابِطُ Paddle</b>: ‏<c>POST /transactions</c> بِـ<c>Bearer</c> مِن المِفتاحِ وجِسمِ المُعامَلَةِ بِمَرجِعِنا — طَلَبٌ واحِد.</summary>
    [Fact]
    public async Task A_paddle_link_creates_the_transaction()
    {
        var calls = new Recorder().Then(HttpStatusCode.Created,
            "{\"data\":{\"id\":\"txn_01\",\"status\":\"ready\",\"checkout\":{\"url\":\"https://wasayel.example/billing/paddle/checkout.html?_ptxn=txn_01\"}}}");

        var (response, _, _) = await PostAsync($"/admin/tenants/{BillingSlug}/plan/paddle-link",
            Form(("amount", "49"), ("currency", "USD"), ("days", "30"), ("description", "اشتِراكُ شَهر")), calls, asAdmin: true, Plan());

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(new[] { """
            POST https://api.paddle.com/transactions · v1.1 · رُؤوس [Authorization] · auth Bearer pdl_apikey_1111111111111111111111111 · rid — · prefer — · مُحتَوى [Content-Type: application/json; charset=utf-8] · جِسم {"items":[{"quantity":1,"price":{"name":"\u0627\u0634\u062A\u0650\u0631\u0627\u0643\u064F \u0634\u064E\u0647\u0631","description":"\u0627\u0634\u062A\u0650\u0631\u0627\u0643\u064F \u0634\u064E\u0647\u0631","tax_mode":"internal","unit_price":{"amount":"4900","currency_code":"USD"},"quantity":{"minimum":1,"maximum":1},"product":{"name":"\u0627\u0634\u062A\u0650\u0631\u0627\u0643\u064F \u0634\u064E\u0647\u0631","tax_category":"standard"}}}],"collection_mode":"automatic","currency_code":"USD","custom_data":{"wasayel_ref":"wsl-pd-ejar-eb30b34045d4e61ad118aa06"}}
            """ }, calls.Requests.Select(r => r.Line).ToArray());
    }

    /// <summary><b>ورَفضُ المُزَوِّدِ يُقالُ بِحالِه</b>: PayPal ‏422 عَلى الطَلَبِ وPaddle ‏400 — البابُ يُحَوِّلُ بِرِسالَتِه ولا يَحفَظ.</summary>
    [Fact]
    public async Task A_refused_paypal_order_and_paddle_link_redirect_with_the_provider_text_and_save_nothing()
    {
        var paypal = new Recorder().Then(HttpStatusCode.OK, Token).Then(HttpStatusCode.UnprocessableEntity,
            "{\"name\":\"UNPROCESSABLE_ENTITY\",\"message\":\"The requested action could not be performed.\",\"details\":[{\"issue\":\"CURRENCY_NOT_SUPPORTED\"}]}");
        var (order, orderWorld, _) = await PostAsync($"/admin/tenants/{BillingSlug}/plan/paypal-order",
            Form(("amount", "49"), ("currency", "USD"), ("days", "30"), ("description", "اشتِراكُ شَهر")), paypal, asAdmin: true, Plan());
        var paddle = new Recorder().Then(HttpStatusCode.BadRequest, "{\"error\":{\"type\":\"request_error\",\"code\":\"bad_request\"}}");
        var (link, linkWorld, _) = await PostAsync($"/admin/tenants/{BillingSlug}/plan/paddle-link",
            Form(("amount", "49"), ("currency", "USD"), ("days", "30"), ("description", "اشتِراكُ شَهر")), paddle, asAdmin: true, Plan());

        Assert.Equal(("/admin/tenants/ejar/plan?err=PayPal%20%D9%81%D9%8E%D8%B4%D9%90%D9%84%20%D8%A5%D9%86%D8%B4%D8%A7%D8%A1%D9%8F%20%D8%B7%D9%8E%D9%84%D9%8E%D8%A8%D9%90%20%D8%A7%D9%84%D8%AF%D9%8E%D9%81%D8%B9%3A%20422%20%E2%80%94%20UNPROCESSABLE_ENTITY%20%5BCURRENCY_NOT_SUPPORTED%5D%20%E2%80%94%20The%20requested%20action%20could%20not%20be%20performed.", 0), (order.Headers.Location!.OriginalString, orderWorld.SaveCalls));
        Assert.Equal(("/admin/tenants/ejar/plan?err=Paddle%20%D9%81%D9%8E%D8%B4%D9%90%D9%84%20%D8%A5%D9%86%D8%B4%D8%A7%D8%A1%D9%8F%20%D8%A7%D9%84%D9%85%D9%8F%D8%B9%D8%A7%D9%85%D9%8E%D9%84%D9%8E%D8%A9%3A%20400%20%E2%80%94%20%7B%22error%22%3A%7B%22type%22%3A%22request_error%22%2C%22code%22%3A%22bad_request%22%7D%7D", 0), (link.Headers.Location!.OriginalString, linkWorld.SaveCalls));
        Assert.Equal(2, paypal.Requests.Count);
        Assert.Single(paddle.Requests);
    }
}
