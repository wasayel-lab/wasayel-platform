using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using ACommerce.Kit.Delivery;
using ACommerce.Platform.Hosting;
using ACommerce.Platform.Providers;
using ACommerce.Platform.Shared.Metering;
using ACommerce.Templates.Customer.Marketplace;
using ACommerce.Templates.Customer.Marketplace.Services;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using ACommerce.Templates.Customer.Marketplace.Services.Providers;
using ACommerce.Templates.Customer.Marketplace.Services.Shipping;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ ما يَبلُغُ OTO وخَلفِيّاتِ النَماذِجِ الثَلاث — يُثَبَّتُ عِندَ الأَبوابِ قَبلَ أَن تَمُرَّ بِالمُستَدعي المَعدود (‏2026-11-12، الموجَة ١٣ W13-6·١٥ — ADR-051) ═══
//
// **لِماذا قَبلَ التَبديل** (القاعِدَتانِ ٣ و١٣): البَندُ ١٥ يَنزِعُ `HttpClient` مِن `OtoClient` ومِن الخَلفِيّاتِ الثَلاث (`AgentBackends.cs`)، ويَقلِبُ تَسجيلَ OTO
// (`AddHttpClient<OtoClient>` في `OtoOptions.cs`) ويُسَجِّلُ لِلخَلفِيّاتِ عَميلاً مِن المَصنَعِ مَكانَ عُمَلائِها الثَلاثَةِ المَبنِيَّةِ بِـ`new`، ويُغَيِّرُ تَواقيعَ
// عَمَلِيّاتِ OTO الأَربَع. ووَعدُه أَنّ **ما يَبلُغُ المُزَوِّدَ لا يَتَغَيَّر**: الطَلَبُ بِفِعلِه وعُنوانِه ورُؤوسِه وجِسمِه، وتَرتيبُ الطَلَبات، وما يَقولُه البابُ
// بَعدَه. والوَعدُ لا يُحفَظُ بِاسمِه (القاعِدَة ٢): هذا المِلَفُّ يُثَبِّتُه ويُودَعُ وَحدَه، ثُمَّ يُعادُ بَعدَ التَبديلِ **بِلا تَعديلِ حَرف**.
//
// **والأَبواب**:
//   • OTO — `DealShipping` (الشَحنُ: إنشاءٌ ثُمَّ سُؤال، واستِردادُ طَلَبٍ ضاعَ رَدُّه، وإعادَةُ الرَمزِ عِندَ ‏401، والوارِد) و`VendorShippingService` (اللَصقُ ثُمَّ
//     تَسجيلُ الوارِد بِـPOST ثُمَّ PUT ثُمَّ PUT مَردودٌ يُعادُ POST، والتَسجيلُ بِالرَمزِ السوقِيّ)، والعَميلُ بِتَسجيلِه هُوَ (`AddOtoShipping`)، والتِقاطُه بِما يَبقى
//     بَعدَ التَبديل (التَصميم §6.3): `ConfigureHttpClientDefaults(… ConfigurePrimaryHttpMessageHandler(() => recorder))` يَسري عَلى كُلِّ عَميلٍ يَبنيه المَصنَع.
//     وقارِئُ أُجرَةِ OTO (`CheckOtoDeliveryFeeAsync`) بِلا مُنادٍ في وَقتِ التَشغيلِ فَيُنادى مِن العَميلِ المَحلولِ نَفسِه.
//   • النَماذِج — المُزَوِّدُ المُسَمّى كَما يُرَكِّبُه القالِب (`AddCustomerMarketplaceTemplate` ← `IAgentBackendProvider`) بِثَلاثَةِ مَلَفّاتِ وُكَلاءَ: Anthropic،
//     وGemini، ومُتَوافِقُ OpenAI بِعُنوانٍ لا يُحَلّ. **والالتِقاطُ بِطَريقَينِ لِأَنّ العَميلَ يَتَبَدَّل**: اليَومَ عُمَلاءُ الخَلفِيّاتِ مَبنِيّونَ بِـ`new` فَلا يَبلُغُهُم
//     مُعالِجُ المَصنَع، فَيُلتَقَطُ الطَلَبُ عِندَ حَدَثِ `System.Net.Http.HttpRequestOut.Start` (مُستَمِعُ `HttpHandlerDiagnosticListener` في كُلِّ `SocketsHttpHandler`)
//     ويُرمى قَبلَ أَن يُغادِرَ العَمَلِيَّة؛ وبَعدَ التَبديلِ يَبلُغُه مُعالِجُ المَصنَعِ فَيُلتَقَطُ هُناكَ ويُرمى بِالاستِثناءِ نَفسِه. والطَريقانِ يَكتُبانِ السَطرَ نَفسَه،
//     والصَفُّ يَقرَأُ اتِّحادَهُما — فَأَيٌّ مِنهُما التَقَطَ فَالمُقارَنُ واحِد. والمُستَمِعُ يُفَعَّلُ لِلمُضيفاتِ الثَلاثَةِ وَحدَها (مُرشِّحُ `IsEnabled` بِالطَلَب)، والصِنفُ في
//     مَجموعَةٍ تَسلسُلِيَّةٍ (`DisableParallelization`) فَلا يَمَسُّ طَلَباتِ صِنفٍ آخَرَ يَجري مَعَه.
//
// ولا يُسَمّي المِلَفُّ نَوعاً يولَدُ في البَند ولا عَمَلِيَّةً يَتَغَيَّرُ تَوقيعُها: `OtoClient` لا يُنادى إلّا بِما يَبقى (`CheckOtoDeliveryFeeAsync`)، والخَلفِيَّةُ بِأَخيها
// ذي الوَسيطَين (`CallAsync(AgentRequest, CancellationToken)`). **وبِلا شَبَكَةٍ خارِجِيَّة**. والأَداةُ تُقاسُ قَبلَ أَن يُوثَقَ بِها (القاعِدَة ١٠): كُلُّ صَفٍّ يَعُدُّ
// ما التَقَطَ ويَفشَلُ إن لَم يَكُن ما يَنتَظِر، والمُعَرِّفاتُ العَشوائِيَّةُ (الصَفقَة، سِرُّ الوارِد) تُستَبدَلُ بِأَسمائِها قَبلَ المُقارَنَة.

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ProcessWideHttpListenerCollection
{
    public const string Name = "process-wide-http-listener";
}

[Collection(ProcessWideHttpListenerCollection.Name)]
public class OtoModelRequestShapeCharacterizationTests(ITestOutputHelper output)
{
    /// <summary>طَلَبٌ كَما بَلَغَ أَوَّلَ مُعالِجٍ أَو حَدَثَ البَدء — والرُؤوسُ الَّتي يَحمِلُها مُزَوِّدٌ بِعَينِه بِقيَمِها.</summary>
    internal sealed record Seen(string Method, string Uri, string Version, string RequestHeaders, string Authorization,
                                string ApiKey, string ApiVersion, string ContentHeaders, string Body)
    {
        public string Line => $"{Method} {Uri} · v{Version} · رُؤوس [{RequestHeaders}] · auth {Authorization} · key {ApiKey} · version {ApiVersion} · "
                              + $"مُحتَوى [{ContentHeaders}] · جِسم {Body}";

        /// <summary><b>رَأسُ التَأطيرِ لا يُقارَن</b>: <c>Transfer-Encoding: chunked</c> يَضَعُه <c>SocketsHttpHandler</c> نَفسُه عَلى جِسمٍ لا يُعرَفُ طولُه
        /// (<c>JsonContent</c>) قَبلَ أَن يُطلِقَ حَدَثَ البَدء — فَيَراه طَريقُ الحَدَثِ ولا يَراه مُعالِجُ المَصنَعِ الَّذي يَسبِقُه، وهُوَ عَلى السِلكِ في الحالَينِ
        /// لِأَنّ الإرسالَ في الإنتاجِ يَمُرُّ بِالمُعالِجِ نَفسِه. فَهُوَ أَثَرُ طَريقِ الالتِقاطِ لا أَثَرُ الكودِ المُقاس.</summary>
        internal const string Framing = "Transfer-Encoding";

        public static Seen Of(HttpRequestMessage request, string body)
        {
            string Values(string name) => request.Headers.TryGetValues(name, out var v) ? string.Join(";", v) : "—";
            return new Seen(request.Method.Method, request.RequestUri!.ToString(), request.Version.ToString(),
                string.Join(",", request.Headers.Select(h => h.Key).Where(k => k != Framing).Order(StringComparer.Ordinal)),
                request.Headers.Authorization?.ToString() ?? "—", Values("x-api-key"), Values("anthropic-version"),
                request.Content is null ? "" : string.Join(",", request.Content.Headers.Select(h => $"{h.Key}: {string.Join(";", h.Value)}").Order(StringComparer.Ordinal)),
                body);
        }
    }

    /// <summary>المُعالِجُ الأَوَّل: يُجيبُ مِن مَسارِه — أَو مِن رُدودٍ مُرَتَّبَةٍ لِمَسارٍ بِعَينِه — ويُسَجِّلُ كُلَّ طَلَب.</summary>
    internal sealed class Recorder : HttpMessageHandler
    {
        private readonly Dictionary<string, Queue<Func<HttpResponseMessage>>> _once = new(StringComparer.Ordinal);
        public List<Seen> Requests { get; } = new();
        public Func<HttpRequestMessage, string?, HttpResponseMessage> Route { get; set; } = (_, _) => new HttpResponseMessage(HttpStatusCode.NotFound);

        /// <summary>رَدٌّ لِلطَلَبِ التالي عَلى هذا المَسار — مَرَّةً، ثُمَّ يَعودُ المَسارُ إلى رَدِّه.</summary>
        public Recorder Once(string path, Func<HttpResponseMessage> reply)
        {
            if (!_once.TryGetValue(path, out var q)) _once[path] = q = new();
            q.Enqueue(reply);
            return this;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
            lock (Requests) Requests.Add(Seen.Of(request, body));
            var path = request.RequestUri!.AbsolutePath;
            if (_once.TryGetValue(path, out var q) && q.Count > 0) return q.Dequeue()();
            return Route(request, request.Content is null ? null : body);
        }
    }

    // ═══ OTO — عَبرَ `DealShipping` و`VendorShippingService` وتَسجيلِ العَميلِ نَفسِه ════════════════════════════════════════════════════════

    private const string Slug = DealShipTests.Slug;
    private const string Inbound = "https://wasayel.example/theme-demo/shipping/oto/order-status";
    private const string Marketplace = "MKT-w13-token";
    private const string Json = "Content-Type: application/json; charset=utf-8";
    private const string Staging = OtoEnvironment.StagingBaseUrl;

    /// <summary>بائِعُ صَفِّ التَسجيلِ بِمُعَرِّفٍ لا يَستَعمِلُه صِنفٌ آخَر — فَحَدُّ مُحاوَلاتِ التَسجيلِ في العَمَلِيَّةِ (<c>TryConsumeActionQuota</c>) لِهذا الصَفِّ وَحدَه.</summary>
    private static readonly Guid Registering = Guid.Parse("e15e15e1-5e15-4e15-8e15-e15e15e15e15");

    /// <summary>رُدودُ OTO بِمِثالِ وَثيقَتِها — رُدودُ <see cref="OtoRouteHandler"/> بِحَرفِها، وقارِئُ الأُجرَةِ بِخِيارٍ واحِد.</summary>
    private static HttpResponseMessage OtoAnswer(HttpRequestMessage request, string? body) => request.RequestUri!.AbsolutePath switch
    {
        OtoClient.RefreshTokenPath => OtoRouteHandler.Json(HttpStatusCode.OK,
            "{\"access_token\":\"ACCESS-1\",\"success\":true,\"token_type\":\"Bearer\",\"expires_in\":\"3600\"}"),
        OtoClient.CreateOrderPath => OtoRouteHandler.Json(HttpStatusCode.OK, "{\"success\":true,\"otoId\":540789}"),
        OtoClient.OrderStatusPath => OtoRouteHandler.Tracking(OtoRouteHandler.OrderIdIn(body)),
        OtoClient.WebhookPath => body is not null && body.Contains("\"id\":", StringComparison.Ordinal)
            ? OtoRouteHandler.Json(HttpStatusCode.OK, "{\"success\": true, \"message\": \"webhook has been updated\"}")
            : OtoRouteHandler.Json(HttpStatusCode.OK, "{\"success\": true, \"id\": \"59\", \"message\": \"webhook has been created\"}"),
        OtoClient.RegisterPath => OtoRouteHandler.Json(HttpStatusCode.OK, OtoRouteHandler.RegisterAnswer),
        OtoClient.CheckOtoDeliveryFeePath => OtoRouteHandler.Json(HttpStatusCode.OK,
            "{\"success\":true,\"deliveryCompany\":[{\"deliveryOptionId\":\"12\",\"deliveryOptionName\":\"w13\"}]}"),
        _ => OtoRouteHandler.Json(HttpStatusCode.NotFound, "{}"),
    };

    /// <summary>عَميلُ OTO بِتَسجيلِه (<c>AddOtoShipping</c>) عَلى مُعالِجٍ يُسَجِّل — بيئَةُ الاختِبارِ مَفتوحَة، ورَمزُ السوقِ إن طُلِب.</summary>
    private static (OtoClient Client, IOptions<OtoOptions> Options, ServiceProvider Services) Oto(Recorder recorder, string marketplace = "")
    {
        recorder.Route = OtoAnswer;
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [OtoEnvironment.EnvironmentKey] = OtoEnvironment.Staging,
            [OtoEnvironment.RefreshTokenKey] = DealShipTests.ConfigToken,
            [OtoEnvironment.MarketplaceTokenKey] = marketplace,
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.ConfigureHttpClientDefaults(b => b.ConfigurePrimaryHttpMessageHandler(() => recorder));
        services.AddOtoShipping(config);
        var sp = services.BuildServiceProvider();
        return (sp.GetRequiredService<OtoClient>(), sp.GetRequiredService<IOptions<OtoOptions>>(), sp);
    }

    private sealed record Ship(DocWorld Docs, DealsService Deals, DealShipping Shipping, Guid Id, ProviderVault Vault, TenantProviderService Providers);

    /// <summary>صَفقَةُ نَقدٍ عِندَ «تأكيد» — عالَمُ <c>DealShipTests.ConfirmedCashDeal</c> بِحَرفِه، والعَميلُ مِن الحاوِيَة؛ ولِبائِعِها مَدخَلٌ مَختومٌ إلّا أَن يُنزَع.</summary>
    private static async Task<Ship> CashDealAsync(OtoClient client, IOptions<OtoOptions> options, bool vendorEntry = true)
    {
        var docs = new DocWorld();
        var deals = new DealsService(docs.Store, new DealMoneyClaimFirstTests.RecordingPaymentProvider());
        var deal = await deals.StartAsync(Slug, "marketplace", DealShipTests.Buyer, "مُشتَرٍ", null, "سِلعَة", 150m, new()
        {
            [CheckoutPayMethods.AttributeKey] = CheckoutPayMethods.CashOnDelivery,
            [Deal.DeliveryAddressAttribute] = "حَيُّ المِثال، شارِعُ الاختِبار",
            [Deal.DeliveryPhoneAttribute] = "0500000000",
        });
        await deals.AttachRefAsync(Slug, deal.Id, DealsService.ListingOwnerRef, DealShipTests.Seller.ToString());
        await deals.BindListingOwnerAsync(Slug, deal.Id, DealShipTests.Seller.ToString());
        Assert.True((await deals.AcceptAsync(Slug, deal.Id, new DealAcceptor(DealShipTests.Seller, "بائِع"))).Ok);
        Assert.True((await deals.AdvanceAsync(Slug, deal.Id, DealShipTests.Seller, "بائِع")).Ok);
        var vault = DealShipTests.NewVault();
        var providers = new TenantProviderService(docs.Store);
        if (vendorEntry) docs.Put(DealShipTests.VendorEntry(vault, Slug, DealShipTests.Seller));
        var shipping = new DealShipping(deals, client, options, null, providers, vault, new FixedClock(DealShipTests.Now));
        return new Ship(docs, deals, shipping, deal.Id, vault, providers);
    }

    /// <summary>الصَفقَةُ وسِرُّ الوارِدِ بِأَسمائِهِما — فَيُقارَنُ النَصُّ بِحَرفِه.</summary>
    private static string Named(string text, Guid deal)
        => Regex.Replace(text.Replace(deal.ToString("N"), "{deal}", StringComparison.Ordinal), "\"secretKey\":\"[^\"]+\"", "\"secretKey\":\"{secret}\"");

    private List<string> Lines(string title, Recorder recorder, Guid deal = default)
    {
        output.WriteLine($"── {title} ({recorder.Requests.Count} طَلَباً)");
        var lines = recorder.Requests.Select(s => Named(s.Line, deal)).ToList();
        foreach (var l in lines) output.WriteLine("   " + l);
        return lines;
    }

    /// <summary>مُبادَلَةُ الرَمز — بِلا <c>Authorization</c>، والجِسمُ رَمزُ التَحديثِ وَحدَه، وطولُه مِن نَصِّه.</summary>
    private static string Token(string refresh)
    {
        var body = $"{{\"refresh_token\":\"{refresh}\"}}";
        return $"POST {Staging}{OtoClient.RefreshTokenPath} · v1.1 · رُؤوس [] · auth — · key — · version — · "
               + $"مُحتَوى [Content-Length: {Encoding.UTF8.GetByteCount(body)},{Json}] · جِسم {body}";
    }

    /// <summary>عَمَلِيَّةٌ بِرَمزِ الوُصول — والطولُ طولُ الجِسمِ الحَقيقِيِّ قَبلَ أَن تُستَبدَلَ بِه الأَسماء (مُعَرِّفُ الصَفقَةِ ‏32 مَحرَفاً، والسِرُّ بِطولِه الثابِت).</summary>
    private static string Bearer(string path, string body) => Bearer(path, body, Encoding.UTF8.GetByteCount(body));

    private static string Bearer(string path, string body, int length, string method = "POST")
        => $"{method} {Staging}{path} · v1.1 · رُؤوس [Authorization] · auth Bearer ACCESS-1 · key — · version — · "
           + $"مُحتَوى [Content-Length: {length},{Json}] · جِسم {body}";

    private const string CreateOrderBody =
        "{\"orderId\":\"{deal}\",\"createShipment\":true,\"payment_method\":\"cod\",\"amount\":150,\"amount_due\":150,\"currency\":\"SAR\","
        + "\"item_description\":\"\\u0633\\u0650\\u0644\\u0639\\u064E\\u0629\",\"customer\":{\"name\":\"\\u0645\\u064F\\u0634\\u062A\\u064E\\u0631\\u064D\","
        + "\"mobile\":\"0500000000\",\"address\":\"\\u062D\\u064E\\u064A\\u064F\\u0651 \\u0627\\u0644\\u0645\\u0650\\u062B\\u0627\\u0644\\u060C "
        + "\\u0634\\u0627\\u0631\\u0650\\u0639\\u064F \\u0627\\u0644\\u0627\\u062E\\u062A\\u0650\\u0628\\u0627\\u0631\",\"city\":\"Riyadh\",\"country\":\"SA\"}}";

    private const string StatusBody = "{\"orderId\":\"{deal}\"}";

    /// <summary>
    /// <para><b>الشَحنُ ثُمَّ الوارِد</b>: رَمزُ البائِعِ يُبادَلُ مَرَّة، ثُمَّ «Create Order» بِجِسمِه كُلِّه ثُمَّ «Order Tracking» بِمُعَرِّفِنا — والصَفقَةُ إلى الشَحن؛ ثُمَّ
    /// إشعارٌ مُوَقَّعٌ بِسِرِّ البائِعِ يُسأَلُ بَعدَه «Order Tracking» وَحدَه (الرَمزُ مُخَزَّن) — والصَفقَةُ إلى التَسليم.</para>
    /// </summary>
    [Fact]
    public async Task Shipping_then_the_signed_notice_reach_OTO_as_token_create_status_then_status()
    {
        var recorder = new Recorder();
        var (client, options, sp) = Oto(recorder);
        await using var _ = sp;
        var w = await CashDealAsync(client, options);

        var shipped = await w.Shipping.ShipAsync(Slug, w.Id, DealShipTests.Vendor, "Riyadh");
        var shipLines = Lines($"الشَحن ⇐ {shipped.Ok}/{shipped.Code ?? "—"} · {shipped.Deal?.Stage}", recorder, w.Id);
        Assert.True(shipped.Ok, shipped.Code);
        Assert.Equal(DealStage.Shipping, shipped.Deal!.Stage);
        Assert.Equal(new[]
        {
            Token(DealShipTests.VendorToken),
            Bearer(OtoClient.CreateOrderPath, CreateOrderBody, 498),
            Bearer(OtoClient.OrderStatusPath, StatusBody, 46),
        }, shipLines);

        recorder.Requests.Clear();
        recorder.Once(OtoClient.OrderStatusPath, () => OtoRouteHandler.Tracking(w.Id.ToString("N"), status: "delivered"));
        var notice = await w.Shipping.ReceiveAsync(Slug, OtoOrderStatusTests.Notice(w.Id));
        var noticeLines = Lines($"الوارِد ⇐ {notice.Code} · {notice.Deal?.Stage}", recorder, w.Id);
        Assert.Equal(OtoOrderStatusGate.Delivered, notice.Code);
        Assert.Equal(new[] { Bearer(OtoClient.OrderStatusPath, StatusBody, 46) }, noticeLines);
    }

    /// <summary>
    /// <para><b>رَدٌّ ضاعَ بَعدَ الإنشاءِ يُستَرَدّ</b>: «Create Order» يُجيبُ ‏503، فَيُسأَلُ «Order Tracking» بِمُعَرِّفِنا ويُتَبَنّى الطَلَب، ثُمَّ يُسأَلُ ثانِيَةً قَبلَ الانتِقال —
    /// سُؤالانِ بِالجِسمِ نَفسِه. و<b>‏401 مِن «Create Order»</b> يُسقِطُ الرَمزَ ويُعيدُ المُبادَلَةَ والإنشاءَ مَرَّةً واحِدَة.</para>
    /// </summary>
    [Fact]
    public async Task A_lost_create_answer_is_recovered_by_a_status_and_a_401_refreshes_the_token_once()
    {
        var recorder = new Recorder();
        var (client, options, sp) = Oto(recorder);
        await using var _ = sp;
        var w = await CashDealAsync(client, options);
        recorder.Once(OtoClient.CreateOrderPath, () => OtoRouteHandler.Json(HttpStatusCode.ServiceUnavailable, "<html/>"));

        var recovered = await w.Shipping.ShipAsync(Slug, w.Id, DealShipTests.Vendor, "Riyadh");
        var recoverLines = Lines($"الاستِرداد ⇐ {recovered.Ok}/{recovered.Code ?? "—"}", recorder, w.Id);
        Assert.True(recovered.Ok, recovered.Code);
        Assert.Equal(new[]
        {
            Token(DealShipTests.VendorToken),
            Bearer(OtoClient.CreateOrderPath, CreateOrderBody, 498),
            Bearer(OtoClient.OrderStatusPath, StatusBody, 46),
            Bearer(OtoClient.OrderStatusPath, StatusBody, 46),
        }, recoverLines);

        var again = new Recorder();
        var (client2, options2, sp2) = Oto(again);
        await using var __ = sp2;
        var w2 = await CashDealAsync(client2, options2);
        again.Once(OtoClient.CreateOrderPath, () => OtoRouteHandler.Json(HttpStatusCode.Unauthorized, "{}"));

        var refreshed = await w2.Shipping.ShipAsync(Slug, w2.Id, DealShipTests.Vendor, "Riyadh");
        var refreshLines = Lines($"‏401 ثُمَّ رَمزٌ جَديد ⇐ {refreshed.Ok}/{refreshed.Code ?? "—"}", again, w2.Id);
        Assert.True(refreshed.Ok, refreshed.Code);
        Assert.Equal(new[]
        {
            Token(DealShipTests.VendorToken),
            Bearer(OtoClient.CreateOrderPath, CreateOrderBody, 498),
            Token(DealShipTests.VendorToken),
            Bearer(OtoClient.CreateOrderPath, CreateOrderBody, 498),
            Bearer(OtoClient.OrderStatusPath, StatusBody, 46),
        }, refreshLines);
    }

    /// <summary>
    /// <para><b>سَطحُ البائِع</b>: اللَصقُ يُبادِلُ الرَمزَ ثُمَّ يُسَجِّلُ الوارِدَ بِـ<c>POST</c>؛ ولَصقٌ ثانٍ عَلى مَدخَلٍ فَعّالٍ يُحَدِّثُه بِـ<c>PUT</c> ومَعَه <c>id</c>؛ و<c>PUT</c>
    /// يَرُدُّه حِسابٌ آخَرُ (‏400) يُعادُ <c>POST</c>. والتَسجيلُ بِالرَمزِ السوقِيِّ: مُبادَلَتُه، ثُمَّ «Register» بِحُقولِ الاستِمارَة، ثُمَّ مُبادَلَةُ رَمزِ الحِسابِ الجَديدِ
    /// ثُمَّ الوارِدُ بِه.</para>
    /// </summary>
    [Fact]
    public async Task The_vendor_surface_reaches_OTO_as_exchange_then_webhook_post_put_and_the_marketplace_register()
    {
        var recorder = new Recorder();
        var (client, options, sp) = Oto(recorder);
        await using var _ = sp;
        var w = await CashDealAsync(client, options, vendorEntry: false);
        var reveal = new ACommerce.Templates.Customer.Marketplace.Services.Api.ApiKeyRevealStore(
            new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions()));
        var surface = new VendorShippingService(w.Providers, w.Vault, client, options,
            new ACommerce.Templates.Customer.Marketplace.Services.Audit.AuditWriter(w.Docs.Store), reveal);
        var vendor = ListingVendorGate.Admit(DealShipTests.Seller, "بائِع", 1).Vendor!;
        const string webhook = "{\"method\":\"post\",\"url\":\"" + Inbound + "\",\"secretKey\":\"{secret}\",\"webhookType\":\"orderStatus\"}";
        const string update = "{\"method\":\"post\",\"url\":\"" + Inbound + "\",\"secretKey\":\"{secret}\",\"webhookType\":\"orderStatus\",\"id\":\"59\"}";

        Assert.True((await surface.SaveTokenAsync(Slug, vendor, "RT-w13-first", Inbound)).Ok);
        Assert.True((await surface.SaveTokenAsync(Slug, vendor, "RT-w13-second", Inbound)).Ok);
        recorder.Once(OtoClient.WebhookPath, () => OtoRouteHandler.Json(HttpStatusCode.BadRequest, "{\"success\": false, \"otoErrorCode\": \"OTO1001\"}"));
        var third = await surface.SaveTokenAsync(Slug, vendor, "RT-w13-third", Inbound);
        var pasteLines = Lines($"اللَصقُ ثَلاثاً ⇐ {third.Ok} · الوارِد {third.After?.WebhookId ?? "—"}/{third.After?.WebhookRefusal ?? "—"}", recorder);
        Assert.True(third.Ok, third.Code);
        Assert.Equal(new[]
        {
            Token("RT-w13-first"), Bearer(OtoClient.WebhookPath, webhook, 193),
            Token("RT-w13-second"), Bearer(OtoClient.WebhookPath, update, 203, "PUT"),
            Token("RT-w13-third"), Bearer(OtoClient.WebhookPath, update, 203, "PUT"), Bearer(OtoClient.WebhookPath, webhook, 193),
        }, pasteLines);

        var registering = new Recorder();
        var (rclient, roptions, rsp) = Oto(registering, Marketplace);
        await using var __ = rsp;
        var rw = await CashDealAsync(rclient, roptions, vendorEntry: false);
        var rsurface = new VendorShippingService(rw.Providers, rw.Vault, rclient, roptions,
            new ACommerce.Templates.Customer.Marketplace.Services.Audit.AuditWriter(rw.Docs.Store), reveal);
        Assert.Equal(VendorShippingInput.Register, rsurface.Input);
        var registered = await rsurface.RegisterAsync(Slug, VendorShippingTests.Registrant(Registering), VendorShippingTests.Form(), Inbound, ip: "203.0.113.7");
        var registerLines = Lines($"التَسجيلُ بِالرَمزِ السوقِيّ ⇐ {registered.Ok}/{registered.Code ?? "—"}", registering);
        Assert.True(registered.Ok, registered.Code);
        Assert.Equal(new[]
        {
            Token(Marketplace),
            Bearer(OtoClient.RegisterPath,
                "{\"country\":\"SA\",\"city\":\"Riyadh\",\"email\":\"vendor.test@example.com\",\"mobileNumber\":\"966500000000\","
                + "\"firstName\":\"\\u0633\\u0627\\u0631\\u064E\\u0629\",\"lastName\":\"\\u0627\\u0644\\u0628\\u0627\\u0626\\u0650\\u0639\\u064E\\u0629\","
                + "\"isVatRegistered\":false,\"billingAddress\":\"\\u062D\\u064E\\u064A\\u064F\\u0651 \\u0627\\u0644\\u0645\\u0650\\u062B\\u0627\\u0644\\u060C "
                + "\\u0634\\u0627\\u0631\\u0650\\u0639\\u064F \\u0627\\u0644\\u0627\\u062E\\u062A\\u0650\\u0628\\u0627\\u0631\"}"),
            Token("_refreshToken_"),
            Bearer(OtoClient.WebhookPath, webhook, 193),
        }, registerLines);
    }

    /// <summary><b>قارِئُ الأُجرَةِ بِلا مُنادٍ في وَقتِ التَشغيل</b> (دَينُ القاعِدَةِ ١): العَميلُ المَحلولُ يُبادِلُ الرَمزَ ثُمَّ يُرسِلُ الحُقولَ الثَلاثَةَ الإلزامِيَّة.</summary>
    [Fact]
    public async Task The_uncalled_delivery_option_reader_reaches_OTO_as_token_then_its_three_fields()
    {
        var recorder = new Recorder();
        var (client, _, sp) = Oto(recorder);
        await using var __ = sp;

        var res = await client.CheckOtoDeliveryFeeAsync(DealShipTests.VendorToken, new OtoFeeQuery("Riyadh", "Jeddah", 1.5m));

        var lines = Lines($"قارِئُ الخِيارات ⇐ {res.Ok} · {res.Value?.Count}", recorder);
        Assert.True(res.Ok);
        Assert.Equal(new[]
        {
            Token(DealShipTests.VendorToken),
            Bearer(OtoClient.CheckOtoDeliveryFeePath, "{\"originCity\":\"Riyadh\",\"destinationCity\":\"Jeddah\",\"weight\":1.5}"),
        }, lines);
    }

    // ═══ النَماذِجُ الثَلاث — عَبرَ المُزَوِّدِ المُسَمّى كَما يُرَكِّبُه القالِب ═══════════════════════════════════════════════════════════

    private const string Captured = "w13-captured-before-leaving";
    private static readonly string[] ModelHosts = ["api.anthropic.com", "generativelanguage.googleapis.com", "model-compat.test"];

    private static bool ModelHost(object? request) => request is HttpRequestMessage r && ModelHosts.Contains(r.RequestUri?.Host, StringComparer.Ordinal);

    /// <summary>المُعالِجُ الأَوَّلُ لِعُمَلاءِ المَصنَع: يَلتَقِطُ ويَرمي — فَلا طَلَبَ يُغادِر.</summary>
    private sealed class CaptureAndThrow(List<Seen> seen) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
            lock (seen) seen.Add(Seen.Of(request, body));
            throw new HttpRequestException(Captured);
        }
    }

    /// <summary>مُستَمِعُ <c>HttpHandlerDiagnosticListener</c>: يُفَعِّلُ النَشاطَ لِمُضيفاتِ النَماذِجِ وَحدَها، ويَلتَقِطُ عِندَ البَدءِ ويَرمي — فَلا طَلَبَ يُغادِر.</summary>
    private sealed class StartCapture(List<Seen> seen) : IObserver<DiagnosticListener>, IObserver<KeyValuePair<string, object?>>
    {
        private readonly List<IDisposable> _subscriptions = new();

        public void OnNext(DiagnosticListener listener)
        {
            if (listener.Name != "HttpHandlerDiagnosticListener") return;
            lock (_subscriptions)
                _subscriptions.Add(listener.Subscribe(this, (name, request, _) => name switch
                {
                    "System.Net.Http.HttpRequestOut" => ModelHost(request),
                    "System.Net.Http.HttpRequestOut.Start" => true,
                    _ => false,
                }));
        }

        public void OnNext(KeyValuePair<string, object?> e)
        {
            if (e.Key != "System.Net.Http.HttpRequestOut.Start") return;
            if (e.Value?.GetType().GetProperty("Request")?.GetValue(e.Value) is not HttpRequestMessage request || !ModelHost(request)) return;
            var body = request.Content is null ? "" : request.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            lock (seen) seen.Add(Seen.Of(request, body));
            throw new HttpRequestException(Captured);
        }

        public void Unsubscribe()
        {
            lock (_subscriptions) foreach (var s in _subscriptions) s.Dispose();
        }

        public void OnError(Exception error) { }
        public void OnCompleted() { }
    }

    /// <summary>القالِبُ كَما يُرَكِّبُه <c>Program.cs</c> (المُضيفُ ثُمَّ القالِب)، بِثَلاثَةِ مَلَفّاتِ وُكَلاءَ بِمَفاتيحَ وَهمِيَّة، وبِلا كاتِبٍ صادِر — فَلا قاعِدَةَ تُسأَل.</summary>
    private static WebApplication Template(List<Seen> seen)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Production,
            ContentRootPath = AppContext.BaseDirectory,
        });
        builder.Configuration["ConnectionStrings:Postgres"] = "Host=localhost;Database=compose_only;Username=u;Password=p";
        builder.Configuration["Agents:W13Anthropic:Provider"] = "anthropic";
        builder.Configuration["Agents:W13Anthropic:ApiKey"] = "k-w13-anthropic";
        builder.Configuration["Agents:W13Gemini:Provider"] = "gemini";
        builder.Configuration["Agents:W13Gemini:ApiKey"] = "k-w13-gemini";
        builder.Configuration["Agents:W13Compat:Provider"] = "openai";
        builder.Configuration["Agents:W13Compat:BaseUrl"] = "https://model-compat.test/";
        builder.Configuration["Agents:W13Compat:ApiKey"] = "k-w13-compat";
        builder.Host.UseDefaultServiceProvider(o => { o.ValidateOnBuild = false; o.ValidateScopes = false; });
        builder.AddPlatformHost();
        builder.Services.AddCustomerMarketplaceTemplate();
        builder.Services.RemoveAll<OutboundRecorder>();
        builder.Services.ConfigureHttpClientDefaults(b => b.ConfigurePrimaryHttpMessageHandler(() => new CaptureAndThrow(seen)));
        return builder.Build();
    }

    private static AgentRequest Request() => new("w13-system",
        [new AgentMessage("user", "w13-hello", null, null)],
        [new AgentToolDef("w13_tool", "w13 tool", "{\"type\":\"object\",\"properties\":{}}")],
        "w13-model", MaxTokens: 64);

    /// <summary>
    /// <para><b>الخَلفِيّاتُ الثَلاث</b>: طَلَبٌ واحِدٌ لِكُلٍّ بِعُنوانِه ورُؤوسِه وجِسمِه — Anthropic بِـ<c>x-api-key</c> و<c>anthropic-version</c> وكاشِ الأَداةِ والنِظام،
    /// وGemini بِالمِفتاحِ في العُنوان، ومُتَوافِقُ OpenAI بِـ<c>Authorization: Bearer</c> عَلى عُنوانِه المَضبوط — ورِسالَةُ الخَطَإِ الَّتي يَقرَؤُها المُنادي حينَ
    /// يَرمي النِداءُ قَبلَ أَن يُغادِر: التَسمِيَةُ والعُنوانُ والاستِثناء.</para>
    /// </summary>
    [Fact]
    public async Task Each_model_backend_sends_one_request_with_its_shape_and_says_the_exception_with_its_endpoint()
    {
        var seen = new List<Seen>();
        var capture = new StartCapture(seen);
        using var all = DiagnosticListener.AllListeners.Subscribe(capture);
        try
        {
            await using var app = Template(seen);
            var agents = app.Services.GetRequiredService<IAgentBackendProvider>();
            var answers = new List<string>();
            foreach (var name in new[] { "W13Anthropic", "W13Gemini", "W13Compat" })
            {
                var backend = agents.For(name);
                var resp = await backend.CallAsync(Request(), CancellationToken.None);
                answers.Add($"{backend.ProviderName} · {backend.Endpoint} · {resp.Text ?? "—"} · {resp.ToolCall?.Name ?? "—"} · {resp.Usage?.ToString() ?? "—"} · {resp.Error}");
            }

            output.WriteLine($"── النَماذِج ({seen.Count} طَلَباً)");
            foreach (var s in seen) output.WriteLine("   " + s.Line);
            foreach (var a in answers) output.WriteLine("   ⇐ " + a);

            const string anthropicBody =
                "{\"model\":\"w13-model\",\"max_tokens\":64,\"system\":[{\"type\":\"text\",\"text\":\"w13-system\",\"cache_control\":{\"type\":\"ephemeral\"}}],"
                + "\"tools\":[{\"name\":\"w13_tool\",\"description\":\"w13 tool\",\"input_schema\":{\"type\":\"object\",\"properties\":{}},\"cache_control\":{\"type\":\"ephemeral\"}}],"
                + "\"messages\":[{\"role\":\"user\",\"content\":\"w13-hello\"}]}";
            const string geminiBody =
                "{\"systemInstruction\":{\"parts\":[{\"text\":\"w13-system\"}]},\"contents\":[{\"role\":\"user\",\"parts\":[{\"text\":\"w13-hello\"}]}],"
                + "\"tools\":[{\"functionDeclarations\":[{\"name\":\"w13_tool\",\"description\":\"w13 tool\",\"parameters\":{\"type\":\"object\",\"properties\":{}}}]}],"
                + "\"generationConfig\":{\"maxOutputTokens\":64}}";
            const string compatBody =
                "{\"model\":\"w13-model\",\"messages\":[{\"role\":\"system\",\"content\":\"w13-system\"},{\"role\":\"user\",\"content\":\"w13-hello\"}],"
                + "\"tools\":[{\"type\":\"function\",\"function\":{\"name\":\"w13_tool\",\"description\":\"w13 tool\",\"parameters\":{\"type\":\"object\",\"properties\":{}}}}],"
                + "\"max_completion_tokens\":64}";
            Assert.Equal(new[]
            {
                "POST https://api.anthropic.com/v1/messages · v1.1 · رُؤوس [anthropic-version,x-api-key] · auth — · key k-w13-anthropic · version 2023-06-01 · "
                + $"مُحتَوى [{Json}] · جِسم {anthropicBody}",
                "POST https://generativelanguage.googleapis.com/v1beta/models/w13-model:generateContent?key=k-w13-gemini · v1.1 · رُؤوس [] · auth — · key — · version — · "
                + $"مُحتَوى [{Json}] · جِسم {geminiBody}",
                "POST https://model-compat.test/v1/chat/completions · v1.1 · رُؤوس [Authorization] · auth Bearer k-w13-compat · key — · version — · "
                + $"مُحتَوى [{Json}] · جِسم {compatBody}",
            }, seen.Select(s => s.Line));
            Assert.Equal(new[]
            {
                $"anthropic · https://api.anthropic.com/ · — · — · — · anthropic (https://api.anthropic.com/) exception: {Captured}",
                $"gemini · https://generativelanguage.googleapis.com/ · — · — · — · gemini (https://generativelanguage.googleapis.com/) exception: {Captured}",
                $"openai · https://model-compat.test/ · — · — · — · openai (https://model-compat.test/) exception: {Captured}",
            }, answers);
        }
        finally
        {
            capture.Unsubscribe();
        }
    }
}
