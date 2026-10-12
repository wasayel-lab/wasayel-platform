using System.Net;
using System.Text.Json;
using ACommerce.Kit.Delivery;
using ACommerce.Kit.Payments;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using Marten;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ شَحنُ الصَفقَةِ عَبرَ OTO — البَوّابَةُ قَبلَ OTO، والانتِقالُ بِالسِياسَةِ لا حَولَها ═══════════
//
// ‏ADR-033 §٢‑هـ الخُطوَة 3 (‏2026-10-12): `POST /{slug}/deals/{id:guid}/ship` يُنشِئُ طَلَباً بِشُحنَتِه
// (`createShipment: true`) ومَبلَغِ التَحصيلِ لِصَفقَةِ نَقد، ويَكتُبُ مُعَرِّفَيه في `Deal.Refs`، ثُمَّ
// يُخرِجُ الصَفقَةَ إلى `Shipping` عَبرَ `DealsService.AdvanceAsync` بِالبائِع. يُقاسُ هُنا:
//   ١) البَوّابَةُ النَقِيَّة — لِكُلِّ رَمزٍ مِن العَشَرَةِ مُوجَبٌ وسالِب، والتَرتيب (القاعِدَتان ٤ و٦).
//   ٢) الرَمزُ لا يُحَلُّ إلّا لِـ`theme-demo` عَلى `staging` (‏ADR-033 §٢‑د والقاعِدَة ٩).
//   ٣) المَسارُ كامِلاً عَلى عالَمِ الوَثائِقِ في الذاكِرَة ومُعالِجٍ يُعيدُ رُدودَ وَثيقَةِ OTO: ما يُرسَل،
//      وما يُكتَب، وأَنّ الرَفضَ قَبلَ OTO لا يُرسِلُ طَلَباً ولا يَكتُب، وأَنّ إلغاءً أَو شَحناً يَدَوِيّاً
//      أَثناءَ نِداءِ OTO لا يُحَرِّكُ الصَفقَةَ مَرَّتَين، وأَنّ الإعادَةَ تَستَأنِفُ ولا تُنشِئُ طَلَباً ثانِياً،
//      وأَنّ طَلَباً ضاعَ رَدُّه يُتَبَنّى مِن «Order Tracking» بِمُعَرِّفِنا وَحدَه.

/// <summary>مُعالِجٌ يُجيبُ بِحَسَبِ المَسار — ويُنادي خُطّافاً قَبلَ الرَدِّ (إلغاءٌ أَو شَحنٌ يَدَوِيٌّ
/// «أَثناءَ نِداءِ OTO» يَصيرُ تَرتيباً حَتمِيّاً لا حَظّاً).</summary>
internal sealed class OtoRouteHandler : HttpMessageHandler
{
    public List<(string Path, string? Body)> Calls { get; } = new();
    /// <summary>رَدُّ كُلِّ مَسارٍ بِجِسمِ طَلَبِه — «Order Tracking» يُعيدُ المُعَرِّفَ الَّذي سُئِلَ عَنه
    /// كَما في مِثالِ الوَثيقَة (<c>"orderId": "OID-23331-9749"</c> في الطَلَبِ والرَدّ).</summary>
    public Dictionary<string, Func<string?, HttpResponseMessage>> Routes { get; } = new(StringComparer.Ordinal);
    public Action<string>? Before { get; set; }

    public OtoRouteHandler()
    {
        Routes[OtoClient.RefreshTokenPath] = _ => Json(HttpStatusCode.OK,
            "{\"access_token\":\"ACCESS-1\",\"success\":true,\"token_type\":\"Bearer\",\"expires_in\":\"3600\"}");
        Routes[OtoClient.CreateOrderPath] = _ => Json(HttpStatusCode.OK, "{\"success\":true,\"otoId\":540789}");
        Routes[OtoClient.OrderStatusPath] = body => Tracking(OrderIdIn(body));
    }

    public static HttpResponseMessage Json(HttpStatusCode status, string body)
        => new(status) { Content = new StringContent(body) };

    public static string? OrderIdIn(string? body)
        => body is null ? null : JsonDocument.Parse(body).RootElement.GetProperty("orderId").GetString();

    public static HttpResponseMessage Tracking(string? orderId, string otoId = "540789", string status = "searchingDriver",
        string trackingUrl = "https://app.tryoto.com/otoflex-tracking?enc=x")
        => Json(HttpStatusCode.OK,
            "{\"success\":true,\"orderId\":" + JsonSerializer.Serialize(orderId) + ",\"otoId\":\"" + otoId + "\"," +
            "\"status\":" + JsonSerializer.Serialize(status) + ",\"printAWBURL\":\"https://app.tryoto.com/print/awb?enc=y\"," +
            "\"trackingUrl\":" + JsonSerializer.Serialize(trackingUrl) + "}");

    public int CallsTo(string path) => Calls.Count(c => c.Path == path);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var path = request.RequestUri!.AbsolutePath;
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
        Calls.Add((path, body));
        Before?.Invoke(path);
        return Routes.TryGetValue(path, out var r) ? r(body) : Json(HttpStatusCode.NotFound, "{}");
    }
}

public class DealShipTests
{
    internal const string Slug = DealShipping.DemoStore;
    internal static readonly Guid Buyer   = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
    internal static readonly Guid Seller  = Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb");
    private static readonly Guid Stranger = Guid.Parse("cccccccc-cccc-4ccc-8ccc-cccccccccccc");
    private static readonly Guid AppOwner = Guid.Parse("dddddddd-dddd-4ddd-8ddd-dddddddddddd");

    private static string RepoRoot => ThemeZeroEquivalenceTests.RepoRoot;

    private sealed class ForbiddenPaymentProvider : IPaymentProvider
    {
        private static InvalidOperationException Touched() => new("صَفقَةُ النَقدِ لا تَمَسُّ المال — نودِيَ مُزَوِّدُ الدَفع.");
        public string ProviderName => "forbidden";
        public Task<PaymentResult> AuthorizeAsync(PaymentRequest req, string idempotencyKey, CancellationToken ct = default) => throw Touched();
        public Task<PaymentResult> CaptureAsync(string paymentId, decimal? amount = null, CancellationToken ct = default) => throw Touched();
        public Task<PaymentResult> RefundAsync(string paymentId, decimal amount, string reason, CancellationToken ct = default) => throw Touched();
        public Task<SubscriptionResult> CreateSubscriptionAsync(SubscriptionRequest req, string idempotencyKey, CancellationToken ct = default) => throw Touched();
        public Task<bool> CancelSubscriptionAsync(string subscriptionId, CancellationToken ct = default) => throw Touched();
        public Task<Invoice?> GetInvoiceAsync(string paymentId, CancellationToken ct = default) => throw Touched();
    }

    // ─── صَفقَةٌ جاهِزَةٌ لِلشَحن — وكُلُّ صَفٍّ يُغَيِّرُ فيها شَيئاً واحِداً ──────────────

    private static Deal Ready() => new()
    {
        Id = Guid.Parse("0f8fad5b-d9cb-469f-a165-705ed7eb6e2a"),
        TenantSlug = Slug,
        Pattern = "marketplace",
        InitiatorId = Buyer,
        InitiatorName = "مُشتَرٍ",
        CounterpartyId = Seller,
        CounterpartyName = "بائِع",
        ListingTitle = "سِلعَة",
        AmountSar = 150m,
        Stage = DealStage.Confirmed,
        Status = DealStatus.Active,
        Refs = new() { [DealsService.ListingOwnerRef] = Seller.ToString() },
        Attributes = new()
        {
            [CheckoutPayMethods.AttributeKey] = CheckoutPayMethods.CashOnDelivery,
            [Deal.DeliveryAddressAttribute] = "حَيُّ المِثال، شارِعُ الاختِبار",
            [Deal.DeliveryPhoneAttribute] = "0500000000",
        },
    };

    internal static readonly DealShipper Vendor = new(Seller, "بائِع");

    private static string? Code(Deal? deal, DealShipper? by = null, bool carrierReady = true)
        => DealShipAuthorization.Validate(deal, by ?? Vendor, carrierReady)?.Code;

    private static Deal With(Action<Deal> change)
    {
        var d = Ready();
        change(d);
        return d;
    }

    // ═══ ١. البَوّابَة — لِكُلِّ رَمزٍ مُوجَبٌ وسالِب ═══════════════════════════════

    [Fact]
    public void The_lexicon_is_ten_closed_codes_and_three_are_the_accept_codes_by_value()
    {
        Assert.Equal(10, DealShipAuthorization.All.Count);
        Assert.Equal(10, DealShipAuthorization.All.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(DealAcceptAuthorization.DealNotFound, DealShipAuthorization.DealNotFound);
        Assert.Equal(DealAcceptAuthorization.ListingOwnerMissing, DealShipAuthorization.ListingOwnerMissing);
        Assert.Equal(DealAcceptAuthorization.DealNotActive, DealShipAuthorization.DealNotActive);
        // ورَمزُ «لَيسَ المالِك» غَيرُ رَمزِ القَبول: نَصُّ ذاكَ «لا يَقبَلُ العَرضَ إلّا مالِكُ الإعلان».
        Assert.NotEqual(DealAcceptAuthorization.ActorNotListingOwner, DealShipAuthorization.ActorNotShipper);
    }

    /// <summary>السالِبُ الجامِع: صَفقَةُ نَقدٍ مُؤَكَّدَةٌ مَربوطٌ بائِعُها، بِعُنوانٍ وهاتِفٍ ورَمزِ ناقِل —
    /// تَمُرُّ لِمالِكِ إعلانِها، ومَدينَةٌ مَكتوبَةٌ تَمُرّ.</summary>
    [Fact]
    public void A_confirmed_cash_deal_passes_the_gate_for_its_listing_owner_with_a_city()
    {
        Assert.Null(Code(Ready()));
        Assert.True(DealShipAuthorization.IsAllowed(Ready(), Vendor, carrierReady: true));
        Assert.Null(DealShipAuthorization.ValidateCity("Riyadh"));
    }

    [Fact]
    public void Deal_not_found_positive_and_negative()
    {
        Assert.Equal(DealShipAuthorization.DealNotFound, Code(null));
        Assert.NotEqual(DealShipAuthorization.DealNotFound, Code(Ready()));
    }

    [Fact]
    public void Listing_owner_missing_positive_and_negative()
    {
        Assert.Equal(DealShipAuthorization.ListingOwnerMissing, Code(With(d => d.Refs.Clear())));
        Assert.Equal(DealShipAuthorization.ListingOwnerMissing,
            Code(With(d => d.Refs[DealsService.ListingOwnerRef] = "not-a-guid")));
        // بِالقيمَةِ لا بِالحَرف: صيغَةُ N لِلمُعَرِّفِ نَفسِه مالِكٌ — الدالَّةُ الواحِدَةُ مَعَ `/accept`.
        Assert.Null(Code(With(d => d.Refs[DealsService.ListingOwnerRef] = Seller.ToString("N"))));
    }

    [Fact]
    public void Actor_not_shipper_positive_and_negative()
    {
        Assert.Equal(DealShipAuthorization.ActorNotShipper, Code(Ready(), new DealShipper(Stranger, "غَريب")));
        Assert.Equal(DealShipAuthorization.ActorNotShipper, Code(Ready(), new DealShipper(Buyer, "مُشتَرٍ")));
        Assert.Null(Code(Ready(), new DealShipper(Seller, "")));
    }

    [Fact]
    public void Deal_not_active_positive_and_negative()
    {
        Assert.Equal(DealShipAuthorization.DealNotActive, Code(With(d => d.Status = DealStatus.Cancelled)));
        Assert.Equal(DealShipAuthorization.DealNotActive, Code(With(d => d.Status = DealStatus.Disputed)));
        Assert.Null(Code(With(d => d.Status = DealStatus.Active)));
    }

    [Fact]
    public void Deal_not_cash_positive_and_negative()
    {
        Assert.Equal(DealShipAuthorization.DealNotCash,
            Code(With(d => d.Attributes[CheckoutPayMethods.AttributeKey] = CheckoutPayMethods.Card)));
        Assert.Equal(DealShipAuthorization.DealNotCash,
            Code(With(d => d.Attributes.Remove(CheckoutPayMethods.AttributeKey))));
        Assert.Null(Code(With(d => d.Attributes[CheckoutPayMethods.AttributeKey] = CheckoutPayMethods.CashOnDelivery)));
    }

    [Fact]
    public void Deal_not_ready_to_ship_positive_and_negative()
    {
        Assert.Equal(DealShipAuthorization.DealNotReadyToShip, Code(With(d => d.Stage = DealStage.Booked)));
        Assert.Equal(DealShipAuthorization.DealNotReadyToShip, Code(With(d => d.Stage = DealStage.Shipping)));
        Assert.Equal(DealShipAuthorization.DealNotReadyToShip, Code(With(d => d.Stage = DealStage.Delivered)));
        Assert.Null(Code(With(d => d.Stage = DealStage.Confirmed)));
        // صَفقَةُ نَقدٍ قَديمَةٌ بَلَغَت `Paid` قَبلَ الإسقاطِ تاليها `Shipping` — تُشحَن.
        Assert.Null(Code(With(d => d.Stage = DealStage.Paid)));
    }

    /// <summary>المُحَرِّكُ يَحكُم: طَرَفٌ مُقابِلٌ غَيرُ مالِكِ الإعلانِ (عَيَّنَ المَسارُ الإداريُّ مالِكَ
    /// التَطبيق) ⇒ لا بوليصَةَ لِانتِقالٍ سَيُرَدّ.</summary>
    [Fact]
    public void Actor_cannot_ship_positive_and_negative()
    {
        Assert.Equal(DealShipAuthorization.ActorCannotShip, Code(With(d => d.CounterpartyId = AppOwner)));
        Assert.Equal(DealShipAuthorization.ActorCannotShip, Code(With(d => d.CounterpartyId = null)));
        Assert.Null(Code(With(d => d.CounterpartyId = Seller)));
    }

    /// <summary><b>طَلَبٌ مِن مُحاوَلَةٍ سابِقَةٍ لا يَسُدُّ الطَريق</b>: صَفقَةٌ عِندَ خُطوَةِ الشَحنِ ولَها
    /// <c>oto_order_id</c> تَمُرُّ — فَتُستَأنَفُ ولا يُنشَأُ ثانٍ — وبَعدَ الشَحنِ تُرَدُّ بِرَمزِ الخُطوَة.</summary>
    [Fact]
    public void An_order_from_an_earlier_attempt_passes_the_gate_and_marks_a_resume()
    {
        var placed = With(d => d.Refs[DealShipping.OrderRef] = "0f8fad5bd9cb469fa165705ed7eb6e2a");
        Assert.Null(Code(placed));
        Assert.True(DealShipping.IsPlaced(placed));

        Assert.False(DealShipping.IsPlaced(Ready()));
        Assert.False(DealShipping.IsPlaced(With(d => d.Refs[DealShipping.OrderRef] = " ")));
        // المِفتاحُ بِعَينِه لا أَيُّ مَرجِعٍ لِـOTO.
        Assert.False(DealShipping.IsPlaced(With(d => d.Refs[DealShipping.LabelRef] = "https://app.tryoto.com/print/awb?enc=y")));

        Assert.Equal(DealShipAuthorization.DealNotReadyToShip, Code(With(d =>
        {
            d.Refs[DealShipping.OrderRef] = "0f8fad5bd9cb469fa165705ed7eb6e2a";
            d.Stage = DealStage.Shipping;
        })));
    }

    [Fact]
    public void Carrier_unavailable_positive_and_negative()
    {
        Assert.Equal(DealShipAuthorization.CarrierUnavailable, Code(Ready(), carrierReady: false));
        Assert.Null(Code(Ready(), carrierReady: true));
    }

    [Fact]
    public void Delivery_data_missing_positive_and_negative()
    {
        Assert.Equal(DealShipAuthorization.DeliveryDataMissing,
            Code(With(d => d.Attributes.Remove(Deal.DeliveryAddressAttribute))));
        Assert.Equal(DealShipAuthorization.DeliveryDataMissing,
            Code(With(d => d.Attributes[Deal.DeliveryPhoneAttribute] = "   ")));
        Assert.Null(Code(Ready()));
    }

    [Fact]
    public void Delivery_city_missing_positive_and_negative()
    {
        Assert.Equal(DealShipAuthorization.DeliveryCityMissing, DealShipAuthorization.ValidateCity(null)!.Code);
        Assert.Equal(DealShipAuthorization.DeliveryCityMissing, DealShipAuthorization.ValidateCity("  ")!.Code);
        Assert.Null(DealShipAuthorization.ValidateCity("الرياض"));
    }

    /// <summary>التَرتيب (القاعِدَة ٦): غَريبٌ عَلى صَفقَةِ بِطاقَةٍ غَيرِ جاهِزَةٍ بِلا ناقِلٍ يُجابُ
    /// «لَستَ المالِك» — لا يَعرِفُ حالَ صَفقَةٍ لَيسَت لَه.</summary>
    [Fact]
    public void Identity_is_answered_before_any_state_of_the_deal()
    {
        var mixed = With(d =>
        {
            d.Attributes[CheckoutPayMethods.AttributeKey] = CheckoutPayMethods.Card;
            d.Stage = DealStage.Booked;
            d.Attributes.Remove(Deal.DeliveryAddressAttribute);
        });
        Assert.Equal(DealShipAuthorization.ActorNotShipper,
            Code(mixed, new DealShipper(Stranger, "غَريب"), carrierReady: false));
        Assert.Equal(DealShipAuthorization.DealNotCash, Code(mixed, carrierReady: false));
    }

    // ═══ ٢. الرَمزُ لِلمَتجَرِ الواحِدِ عَلى الاختِبارِ وَحدَه ═════════════════════════

    [Theory]
    [InlineData("staging", "RT-1", "theme-demo", "RT-1")]
    [InlineData(" Staging ", " RT-1 ", "theme-demo", "RT-1")]
    [InlineData("production", "RT-1", "theme-demo", null)]
    [InlineData("", "RT-1", "theme-demo", null)]
    [InlineData("staging", "", "theme-demo", null)]
    [InlineData("staging", "RT-1", "ashare", null)]
    [InlineData("staging", "RT-1", "Theme-Demo", null)]
    public void The_vendor_token_resolves_for_theme_demo_on_staging_only(
        string environment, string token, string slug, string? expected)
        => Assert.Equal(expected, DealShipping.VendorTokenFor(
            new OtoOptions { Environment = environment, RefreshToken = token }, slug));

    // ═══ ٣. المُسَوَّدَةُ والرَوابِط — دَوالُّ نَقِيَّة ═══════════════════════════════

    [Fact]
    public void The_draft_is_cash_on_delivery_for_the_deal_amount_to_the_buyer_at_the_vendor_city()
    {
        var deal = Ready();
        var draft = DealShipping.DraftFor(deal, "  Riyadh ");

        Assert.Equal(deal.Id.ToString("N"), draft.OrderId);
        Assert.Equal(OtoClient.CashOnDelivery, draft.PaymentMethod);
        Assert.Equal(150m, draft.Amount);
        Assert.Equal(150m, draft.AmountDue);
        Assert.Equal("SAR", draft.Currency);
        Assert.Equal("سِلعَة", draft.ItemDescription);
        Assert.Equal(new OtoCustomer("مُشتَرٍ", "0500000000", "حَيُّ المِثال، شارِعُ الاختِبار", "Riyadh", "SA"), draft.Customer);
    }

    [Theory]
    [InlineData("https://app.tryoto.com/print/awb?enc=y", true)]
    [InlineData("http://app.tryoto.com/print/awb?enc=y", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("www.example.com", false)]
    [InlineData("", false)]
    public void Only_absolute_https_links_are_kept_or_shown(string url, bool kept)
    {
        Assert.Equal(kept, DealShipping.IsHttps(url));
        var deal = With(d => { d.Refs[DealShipping.LabelRef] = url; d.Refs[DealShipping.TrackingRef] = url; });
        var (label, tracking) = DealShipping.LinksOf(deal);
        Assert.Equal(kept ? url : null, label);
        Assert.Equal(kept ? url : null, tracking);
    }

    // ═══ ٤. المَسارُ كامِلاً — عالَمُ الوَثائِقِ ووَثيقَةُ OTO ════════════════════════════

    /// <summary>عالَمُ الشَحن — <c>internal</c> لِأَنّ <c>OtoOrderStatusTests</c> يَبني عَلَيه وارِدَه: نُسخَتانِ مِن أَداةِ
    /// قِياسٍ تَنجَرِفان.</summary>
    internal sealed record World(DocWorld Docs, DealsService Deals, DealShipping Shipping, OtoRouteHandler Oto, Guid Id);

    internal static async Task<World> ConfirmedCashDeal(string? environment = OtoEnvironment.Staging)
    {
        var docs = new DocWorld();
        var deals = new DealsService(docs.Store, new ForbiddenPaymentProvider());
        var deal = await deals.StartAsync(Slug, "marketplace", Buyer, "مُشتَرٍ", null, "سِلعَة", 150m, new()
        {
            [CheckoutPayMethods.AttributeKey] = CheckoutPayMethods.CashOnDelivery,
            [Deal.DeliveryAddressAttribute] = "حَيُّ المِثال، شارِعُ الاختِبار",
            [Deal.DeliveryPhoneAttribute] = "0500000000",
        });
        await deals.AttachRefAsync(Slug, deal.Id, DealsService.ListingOwnerRef, Seller.ToString());
        await deals.BindListingOwnerAsync(Slug, deal.Id, Seller.ToString());
        Assert.True((await deals.AcceptAsync(Slug, deal.Id, new DealAcceptor(Seller, "بائِع"))).Ok);
        Assert.True((await deals.AdvanceAsync(Slug, deal.Id, Seller, "بائِع")).Ok);
        Assert.Equal(DealStage.Confirmed, (await deals.LoadAsync(Slug, deal.Id))!.Stage);

        var oto = new OtoRouteHandler();
        var options = Options.Create(new OtoOptions { Environment = environment ?? "", RefreshToken = "RT-theme-demo" });
        var client = new OtoClient(new HttpClient(oto), options, new OtoTokenCache(), NullLogger<OtoClient>.Instance);
        return new World(docs, deals, new DealShipping(deals, client, options), oto, deal.Id);
    }

    [Fact]
    public async Task Shipping_creates_the_order_writes_both_ids_and_moves_the_cash_deal_to_Shipping_as_the_vendor()
    {
        var w = await ConfirmedCashDeal();

        var res = await w.Shipping.ShipAsync(Slug, w.Id, Vendor, "Riyadh");

        Assert.True(res.Ok, res.Code);
        var deal = (await w.Deals.LoadAsync(Slug, w.Id))!;
        Assert.Equal(DealStage.Shipping, deal.Stage);
        Assert.Equal(w.Id.ToString("N"), deal.Refs[DealShipping.OrderRef]);
        Assert.Equal("540789", deal.Refs[DealShipping.OtoIdRef]);
        Assert.Equal("https://app.tryoto.com/print/awb?enc=y", deal.Refs[DealShipping.LabelRef]);
        Assert.Equal("https://app.tryoto.com/otoflex-tracking?enc=x", deal.Refs[DealShipping.TrackingRef]);

        // الانتِقالُ بِالبائِعِ مِن المُحَرِّكِ نَفسِه — سَطرُ `advanced` واحِدٌ إلى الشَحن، فاعِلُه البائِع.
        var shipped = Assert.Single(deal.Timeline, e => e.Action == "advanced" && e.StageAfter == DealStage.Shipping);
        Assert.Equal(Seller, shipped.ActorId);
        Assert.Equal(DealStage.Confirmed, shipped.StageBefore);

        // وما أُرسِلَ إلى OTO: طَلَبٌ واحِدٌ بِشُحنَتِه ومَبلَغِ التَحصيل، ثُمَّ حالُه بِمُعَرِّفِنا.
        Assert.Equal(1, w.Oto.CallsTo(OtoClient.CreateOrderPath));
        Assert.Equal(w.Id.ToString("N"), OtoRouteHandler.OrderIdIn(
            Assert.Single(w.Oto.Calls, c => c.Path == OtoClient.OrderStatusPath).Body));
        var body = JsonDocument.Parse(w.Oto.Calls.Single(c => c.Path == OtoClient.CreateOrderPath).Body!).RootElement;
        Assert.True(body.GetProperty("createShipment").GetBoolean());
        Assert.Equal("cod", body.GetProperty("payment_method").GetString());
        Assert.Equal(150m, body.GetProperty("amount_due").GetDecimal());
        Assert.Equal("Riyadh", body.GetProperty("customer").GetProperty("city").GetString());
    }

    [Fact]
    public async Task A_refusal_at_the_gate_sends_nothing_to_OTO_and_writes_nothing()
    {
        var w = await ConfirmedCashDeal();
        var stored = w.Docs.Stored.Count;
        var saves = w.Docs.SaveCalls;

        var stranger = await w.Shipping.ShipAsync(Slug, w.Id, new DealShipper(Stranger, "غَريب"), "Riyadh");
        var noCity   = await w.Shipping.ShipAsync(Slug, w.Id, Vendor, " ");

        Assert.Equal(DealShipAuthorization.ActorNotShipper, stranger.Code);
        Assert.Equal(DealShipAuthorization.DeliveryCityMissing, noCity.Code);
        Assert.Empty(w.Oto.Calls);
        Assert.Equal(stored, w.Docs.Stored.Count);
        Assert.Equal(saves, w.Docs.SaveCalls);
    }

    /// <summary>بِلا بيئَةٍ مَفتوحَةٍ لا رَمزَ يُحَلّ — فَلا نِداءَ ولا كِتابَة (والاستِمارَةُ لا تُرسَمُ أَصلاً).</summary>
    [Fact]
    public async Task Without_an_open_environment_the_carrier_is_unavailable_and_nothing_leaves()
    {
        var w = await ConfirmedCashDeal(environment: OtoEnvironment.Production);
        Assert.False(w.Shipping.CarrierReady(Slug));

        var res = await w.Shipping.ShipAsync(Slug, w.Id, Vendor, "Riyadh");

        Assert.Equal(DealShipAuthorization.CarrierUnavailable, res.Code);
        Assert.Empty(w.Oto.Calls);
    }

    [Fact]
    public async Task An_OTO_refusal_returns_its_code_and_writes_nothing_on_the_deal()
    {
        var w = await ConfirmedCashDeal();
        w.Oto.Routes[OtoClient.CreateOrderPath] = _ => OtoRouteHandler.Json(HttpStatusCode.BadRequest,
            "{\"success\":false,\"otoErrorCode\":\"OTO1006\",\"otoErrorMessage\":\"Credit is not enough\"}");
        // ولا طَلَبَ بِمُعَرِّفِنا عِندَها — «‏OTO1002 — The order ID does not exist».
        w.Oto.Routes[OtoClient.OrderStatusPath] = _ => OtoRouteHandler.Json(HttpStatusCode.BadRequest,
            "{\"success\":false,\"otoErrorCode\":\"OTO1002\",\"otoErrorMessage\":\"The order ID does not exist\"}");
        var saves = w.Docs.SaveCalls;

        var res = await w.Shipping.ShipAsync(Slug, w.Id, Vendor, "Riyadh");

        Assert.Equal(OtoFailures.Refused, res.Code);
        var deal = (await w.Deals.LoadAsync(Slug, w.Id))!;
        Assert.Equal(DealStage.Confirmed, deal.Stage);
        Assert.False(deal.Refs.ContainsKey(DealShipping.OrderRef));
        Assert.Equal(saves, w.Docs.SaveCalls);
        Assert.Equal(1, w.Oto.CallsTo(OtoClient.OrderStatusPath));
    }

    /// <summary><b>رَدٌّ ضاعَ بَعدَ أَن أُنشِئَ الطَلَب</b> (‏500 أَو مُهلَة)، <b>وإعادَةٌ لَقِيَت «‏OTO1063 — Order Id is
    /// already exist»</b>: الطَلَبُ هُناكَ بِمُعَرِّفِنا، فَيُتَبَنّى مِن «Order Tracking» — يُكتَبُ مَرجِعاه وتَخرُجُ
    /// الصَفقَةُ إلى الشَحن — ولا يُنشَأُ ثانٍ. وبِلا تَبَنٍّ لا تُكتَبُ مَراجِعُ أَبَداً.</summary>
    [Theory]
    [InlineData(500, "{}")]
    [InlineData(400, "{\"success\":false,\"otoErrorCode\":\"OTO1063\",\"otoErrorMessage\":\"Order Id is already exist\"}")]
    public async Task An_order_OTO_already_holds_under_our_id_is_adopted_not_recreated(int status, string body)
    {
        var w = await ConfirmedCashDeal();
        w.Oto.Routes[OtoClient.CreateOrderPath] = _ => OtoRouteHandler.Json((HttpStatusCode)status, body);

        var res = await w.Shipping.ShipAsync(Slug, w.Id, Vendor, "Riyadh");

        Assert.True(res.Ok, res.Code);
        var deal = (await w.Deals.LoadAsync(Slug, w.Id))!;
        Assert.Equal(DealStage.Shipping, deal.Stage);
        Assert.Equal(w.Id.ToString("N"), deal.Refs[DealShipping.OrderRef]);
        Assert.Equal("540789", deal.Refs[DealShipping.OtoIdRef]);
        Assert.Equal(1, w.Oto.CallsTo(OtoClient.CreateOrderPath));
        Assert.All(w.Oto.Calls.Where(c => c.Path == OtoClient.OrderStatusPath),
            c => Assert.Equal(w.Id.ToString("N"), OtoRouteHandler.OrderIdIn(c.Body)));
    }

    /// <summary>ولا يُتَبَنّى إلّا مُعَرِّفُنا: جَوابُ «Order Tracking» لِطَلَبٍ آخَرَ لا يُكتَبُ — يَبقى فَشَلُ
    /// الإنشاءِ بِرَمزِه.</summary>
    [Fact]
    public async Task An_order_tracking_answer_for_another_order_id_is_not_adopted()
    {
        var w = await ConfirmedCashDeal();
        w.Oto.Routes[OtoClient.CreateOrderPath] = _ => OtoRouteHandler.Json(HttpStatusCode.InternalServerError, "{}");
        w.Oto.Routes[OtoClient.OrderStatusPath] = _ => OtoRouteHandler.Tracking("OID-23331-9749", "30514281");
        var saves = w.Docs.SaveCalls;

        var res = await w.Shipping.ShipAsync(Slug, w.Id, Vendor, "Riyadh");

        Assert.Equal(OtoFailures.Unreachable, res.Code);
        Assert.Equal(DealStage.Confirmed, (await w.Deals.LoadAsync(Slug, w.Id))!.Stage);
        Assert.Equal(saves, w.Docs.SaveCalls);
    }

    /// <summary>ولا سُؤالَ حَيثُ لَم يَبلُغ الطَلَبُ OTO: رَمزٌ مَرفوضٌ يُرَدُّ بِرَمزِه ولا يُسأَلُ عَن طَلَب.</summary>
    [Fact]
    public async Task A_refused_token_neither_creates_nor_asks_order_tracking()
    {
        var w = await ConfirmedCashDeal();
        w.Oto.Routes[OtoClient.RefreshTokenPath] = _ => OtoRouteHandler.Json(HttpStatusCode.BadRequest,
            "{\"success\":false}");

        var res = await w.Shipping.ShipAsync(Slug, w.Id, Vendor, "Riyadh");

        Assert.Equal(OtoFailures.TokenRefused, res.Code);
        Assert.Equal(0, w.Oto.CallsTo(OtoClient.CreateOrderPath));
        Assert.Equal(0, w.Oto.CallsTo(OtoClient.OrderStatusPath));
    }

    /// <summary><b>الإعادَةُ تَستَأنِف</b>: مُحاوَلَةٌ سابِقَةٌ كَتَبَت مَرجِعَي الطَلَبِ ولَم يَقَع انتِقالُها (رُدَّ
    /// بِـ<c>deal_changed_meanwhile</c> ونَصُّه «أَعِد المُحاوَلَة») — فَالإعادَةُ تُخرِجُها إلى الشَحنِ بِلا «Create
    /// Order» ثانٍ ولا مَدينَة، وتَجلِبُ رابِطَيه بِالمُعَرِّفِ المَكتوب.</summary>
    [Fact]
    public async Task A_retry_after_a_refused_advance_resumes_without_a_second_order()
    {
        var w = await ConfirmedCashDeal();
        await w.Deals.AttachRefAsync(Slug, w.Id, DealShipping.OrderRef, w.Id.ToString("N"));
        await w.Deals.AttachRefAsync(Slug, w.Id, DealShipping.OtoIdRef, "540789");

        var res = await w.Shipping.ShipAsync(Slug, w.Id, Vendor, city: null);

        Assert.True(res.Ok, res.Code);
        var deal = (await w.Deals.LoadAsync(Slug, w.Id))!;
        Assert.Equal(DealStage.Shipping, deal.Stage);
        Assert.Equal(0, w.Oto.CallsTo(OtoClient.CreateOrderPath));
        Assert.Equal("https://app.tryoto.com/print/awb?enc=y", deal.Refs[DealShipping.LabelRef]);
        Assert.Equal(w.Id.ToString("N"), OtoRouteHandler.OrderIdIn(
            Assert.Single(w.Oto.Calls, c => c.Path == OtoClient.OrderStatusPath).Body));
    }

    /// <summary><b>إلغاءُ المُشتَري أَثناءَ نِداءِ OTO</b>: الطَلَبُ أُنشِئَ هُناكَ فَيُكتَبُ مَرجِعُه (لا بوليصَةَ
    /// ثانِيَة)، والانتِقالُ يُرَدُّ بِـ<c>deal_changed_meanwhile</c> — الصَفقَةُ مُلغاةٌ عِندَ <c>Confirmed</c>.</summary>
    [Fact]
    public async Task A_cancel_during_the_OTO_call_keeps_the_order_refs_and_does_not_move_the_deal()
    {
        var w = await ConfirmedCashDeal();
        w.Oto.Before = path =>
        {
            if (path != OtoClient.CreateOrderPath) return;
            var d = w.Docs.Read<Deal>(w.Id.ToString())!;
            d.Status = DealStatus.Cancelled;
        };

        var res = await w.Shipping.ShipAsync(Slug, w.Id, Vendor, "Riyadh");

        Assert.False(res.Ok);
        Assert.Equal(DealsService.ChangedMeanwhile, res.Code);
        var deal = (await w.Deals.LoadAsync(Slug, w.Id))!;
        Assert.Equal(DealStatus.Cancelled, deal.Status);
        Assert.Equal(DealStage.Confirmed, deal.Stage);
        Assert.Equal(w.Id.ToString("N"), deal.Refs[DealShipping.OrderRef]);
        Assert.Equal(0, w.Oto.CallsTo(OtoClient.OrderStatusPath));
    }

    /// <summary><b>شَحنٌ يَدَوِيٌّ أَثناءَ نِداءِ OTO لا يُحَرِّكُها مَرَّتَين</b>: أَعلَنَ البائِعُ «شَحن» بِزِرِّ
    /// <c>/advance</c> بَينَ القِراءَةِ والانتِقال — فَالانتِقالُ مِن <c>Confirmed</c> يُرَدُّ ولا تَمضي إلى «تَسليم».</summary>
    [Fact]
    public async Task A_manual_ship_during_the_OTO_call_is_not_advanced_a_second_time()
    {
        var w = await ConfirmedCashDeal();
        w.Oto.Before = path =>
        {
            if (path == OtoClient.CreateOrderPath)
                Assert.True(w.Deals.AdvanceAsync(Slug, w.Id, Seller, "بائِع").GetAwaiter().GetResult().Ok);
        };

        var res = await w.Shipping.ShipAsync(Slug, w.Id, Vendor, "Riyadh");

        Assert.Equal(DealsService.ChangedMeanwhile, res.Code);
        var deal = (await w.Deals.LoadAsync(Slug, w.Id))!;
        Assert.Equal(DealStage.Shipping, deal.Stage);
        Assert.Single(deal.Timeline, e => e.Action == "advanced" && e.StageAfter == DealStage.Shipping);
        Assert.DoesNotContain(deal.Timeline, e => e.StageAfter == DealStage.Delivered);
    }

    // ═══ ٤‑ب. كِتابَةٌ تَسبِقُ حِفظَ مَرجِعِ الطَلَب — يُعادُ الإلحاقُ ولا يَضيع (‏ADR-036 §٧ و§١٠) ═══════════════
    //
    // ‏2026-10-12، إلحاقُ المُنَسِّقِ (٢) عَلى P1-6: «تَخزينُ رَقمِ طَلَبِ OTO أَو رابِطِ البوليصَةِ في `Deal.Refs` قَد يَضيعُ
    // صامِتاً». الطَلَبُ أُنشِئَ عِندَ OTO قَبلَ أَن يُكتَبَ مَرجِعُه، فَمَرجِعٌ يَضيعُ = طَلَبٌ هُناكَ لا تَعرِفُه الصَفقَة. وإلحاقُه
    // بِـ`AttachRefAsync` الَّتي يُعادُ تَطبيقُها عِندَ التَعارُضِ مُنذُ ‏ADR-036 §٧ — وهذِه الصُفوفُ تَقيسُه عَلى مَسارِ `/ship` نَفسِه:
    // كاتِبٌ آخَرُ يَحفَظُ بَينَ تَحميلِ إلحاقِ `oto_order_id` وحِفظِه، ثُمَّ يُرَدُّ الحِفظُ كَما يَرُدُّه Marten (الخُطّافُ نَفسُه
    // الَّذي تَقيسُ بِه صُفوفُ الوارِد — مُسَلَّحاً عِندَ «Create Order»). والبُرهانُ الحَيُّ عَلى Postgres في آخِرِ المِلَفّ.

    /// <summary><b>المُوجَب</b>: كِتابَةٌ لا تَمَسُّ الشَحنَ (مَرجِعٌ يُلحَق) تَغلِبُ حِفظَ مَرجِعِ الطَلَب — فَيُعادُ الإلحاقُ عَلى ما
    /// كُتِب: تَبقى الكِتابَتان، ويُكتَبُ مَرجِعُ الطَلَبِ مَرَّةً، وتَخرُجُ الصَفقَةُ إلى الشَحنِ بِلا طَلَبٍ ثانٍ.</summary>
    [Fact]
    public async Task A_write_that_wins_the_save_of_the_order_ref_is_kept_and_the_ref_is_re_applied_so_the_deal_ships()
    {
        var w = await ConfirmedCashDeal();
        var conflicts = OtoOrderStatusTests.OthersWinTheSave(w,
            () => w.Deals.AttachRefAsync(Slug, w.Id, "probe_note", "1"), armAt: OtoClient.CreateOrderPath);

        var res = await w.Shipping.ShipAsync(Slug, w.Id, Vendor, "Riyadh");

        Assert.Equal(1, conflicts());
        Assert.True(res.Ok, res.Code);
        var deal = (await w.Deals.LoadAsync(Slug, w.Id))!;
        Assert.Equal((DealStatus.Active, DealStage.Shipping), (deal.Status, deal.Stage));
        Assert.Equal(w.Id.ToString("N"), deal.Refs[DealShipping.OrderRef]);
        Assert.Equal("540789", deal.Refs[DealShipping.OtoIdRef]);
        Assert.Equal("1", deal.Refs["probe_note"]);
        Assert.Single(deal.Timeline, e => e.Action == "note" && e.Note == $"attach {DealShipping.OrderRef}={w.Id:N}");
        Assert.Single(deal.Timeline, e => e.Action == "advanced" && e.StageAfter == DealStage.Shipping);
        Assert.Equal(1, w.Oto.CallsTo(OtoClient.CreateOrderPath));
    }

    /// <summary><b>السالِب</b>: إلغاءُ المُشتَري يَغلِبُ حِفظَ مَرجِعِ الطَلَب — فَيُعادُ الإلحاقُ عَلى الصَفقَةِ المُلغاة: المَرجِعُ
    /// قائِمٌ (طَلَبٌ عِندَ OTO تَعرِفُه الصَفقَةُ فَيُلغى مِن لَوحَةِ البائِع)، والانتِقالُ يُرَدُّ بِـ<c>deal_changed_meanwhile</c>،
    /// ولا يُسأَلُ «Order Tracking».</summary>
    [Fact]
    public async Task A_cancel_that_wins_the_save_of_the_order_ref_keeps_the_ref_on_the_cancelled_deal_and_moves_nothing()
    {
        var w = await ConfirmedCashDeal();
        var conflicts = OtoOrderStatusTests.OthersWinTheSave(w, async () =>
            Assert.True((await w.Deals.CancelAsync(Slug, w.Id,
                new DealCanceller(Buyer, "مُشتَرٍ", IsStoreAdmin: false), "عَدَلتُ")).Ok),
            armAt: OtoClient.CreateOrderPath);

        var res = await w.Shipping.ShipAsync(Slug, w.Id, Vendor, "Riyadh");

        Assert.Equal(1, conflicts());
        Assert.False(res.Ok);
        Assert.Equal(DealsService.ChangedMeanwhile, res.Code);
        var deal = (await w.Deals.LoadAsync(Slug, w.Id))!;
        Assert.Equal((DealStatus.Cancelled, DealStage.Confirmed), (deal.Status, deal.Stage));
        Assert.Equal(w.Id.ToString("N"), deal.Refs[DealShipping.OrderRef]);
        Assert.Equal("540789", deal.Refs[DealShipping.OtoIdRef]);
        Assert.Single(deal.Timeline, e => e.Action == "note" && e.Note == $"attach {DealShipping.OrderRef}={w.Id:N}");
        Assert.DoesNotContain(deal.Timeline, e => e.Action == "advanced" && e.StageAfter == DealStage.Shipping);
        Assert.Equal(1, w.Oto.CallsTo(OtoClient.CreateOrderPath));
        Assert.Equal(0, w.Oto.CallsTo(OtoClient.OrderStatusPath));
    }

    /// <summary><b>والحَدّ</b> (‏ADR-036 §٧‑٢): ثَلاثُ خَسائِرَ تَصعَدُ بِاستِثناءِ Marten نَفسِه — لا إسقاطٌ صامِتٌ ولا رابِعَة — ولا
    /// يُكتَبُ في الصَفقَةِ شَيء؛ <b>والطَلَبُ عِندَ OTO لا يَضيع</b>: النَقرَةُ التالِيَةُ تَلقى «‏OTO1063 — Order Id is already exist»
    /// فَتَتَبَنّاه مِن «Order Tracking» بِمُعَرِّفِنا (‏<see cref="An_order_OTO_already_holds_under_our_id_is_adopted_not_recreated"/>)
    /// وتُخرِجُ الصَفقَةَ إلى الشَحن.</summary>
    [Fact]
    public async Task Three_lost_saves_of_the_order_ref_surface_Martens_exception_and_the_next_click_adopts_the_order()
    {
        var w = await ConfirmedCashDeal();
        var conflicts = OtoOrderStatusTests.OthersWinTheSave(w, () => Task.CompletedTask,
            times: int.MaxValue, armAt: OtoClient.CreateOrderPath);
        var saves = w.Docs.SaveCalls;

        await Assert.ThrowsAsync<JasperFx.ConcurrencyException>(() => w.Shipping.ShipAsync(Slug, w.Id, Vendor, "Riyadh"));

        Assert.Equal(3, conflicts());
        Assert.Equal(saves, w.Docs.SaveCalls);
        var lost = (await w.Deals.LoadAsync(Slug, w.Id))!;
        Assert.Equal(DealStage.Confirmed, lost.Stage);
        Assert.False(lost.Refs.ContainsKey(DealShipping.OrderRef));

        w.Docs.BeforeMember = null;
        w.Oto.Routes[OtoClient.CreateOrderPath] = _ => OtoRouteHandler.Json(HttpStatusCode.BadRequest,
            "{\"success\":false,\"otoErrorCode\":\"OTO1063\",\"otoErrorMessage\":\"Order Id is already exist\"}");

        var again = await w.Shipping.ShipAsync(Slug, w.Id, Vendor, "Riyadh");

        Assert.True(again.Ok, again.Code);
        var deal = (await w.Deals.LoadAsync(Slug, w.Id))!;
        Assert.Equal(DealStage.Shipping, deal.Stage);
        Assert.Equal(w.Id.ToString("N"), deal.Refs[DealShipping.OrderRef]);
        Assert.Equal("540789", deal.Refs[DealShipping.OtoIdRef]);
        Assert.Equal(2, w.Oto.CallsTo(OtoClient.CreateOrderPath));
    }

    // ═══ ٥. المُحَرِّك — «مِن» المَقروءَةِ، و`CanMove` ═════════════════════════════

    [Fact]
    public async Task AdvanceAsync_from_refuses_a_stage_other_than_the_one_read_without_writing()
    {
        var w = await ConfirmedCashDeal();
        var saves = w.Docs.SaveCalls;

        var wrong = await w.Deals.AdvanceAsync(Slug, w.Id, Seller, "بائِع", null, default, DealStage.Booked);
        Assert.False(wrong.Ok);
        Assert.Equal(DealsService.ChangedMeanwhile, wrong.Code);
        Assert.Equal(saves, w.Docs.SaveCalls);

        var right = await w.Deals.AdvanceAsync(Slug, w.Id, Seller, "بائِع", null, default, DealStage.Confirmed);
        Assert.True(right.Ok);
        Assert.Equal(DealStage.Shipping, right.Deal!.Stage);
    }

    [Fact]
    public void CanMove_is_the_advance_gate_without_writing()
    {
        Assert.True(DealsService.CanMove(Ready(), Seller));
        Assert.False(DealsService.CanMove(Ready(), Buyer));            // `Shipping` = الطَرَفُ المُقابِل
        Assert.False(DealsService.CanMove(Ready(), Stranger));
        Assert.False(DealsService.CanMove(With(d => d.Status = DealStatus.Cancelled), Seller));
        Assert.False(DealsService.CanMove(With(d => { d.Pattern = "classifieds"; }), Seller));   // آخِرُ مَرحَلَة
        Assert.True(DealsService.CanMove(With(d => d.Stage = DealStage.Booked), Buyer));          // `Confirmed` = أَيُّهُما
    }

    // ═══ ٧. الصَفحَة — ما يُرسَمُ هُوَ ما يُقبَل، وبِلا ناقِلٍ لا استِمارَة (القاعِدَة ١٢) ═══════════════

    private static string DealPage()
    {
        var path = Path.Combine(RepoRoot, "libs", "templates", "ACommerce.Templates.Customer.Marketplace",
            "Components", "Pages", "MyDealDetail.razor");
        var text = File.ReadAllText(path);
        Assert.True(text.Length > 1000, "أَداةٌ عَمياء: `MyDealDetail.razor` لَم يُقرَأ.");
        return text;
    }

    /// <summary>البَوّابَةُ بِلا ناقِلٍ لا تُجيزُ صَفقَةً واحِدَةً لِفاعِلٍ واحِد — عَلى شَبَكَةٍ تَعُدُّ ما فَحَصَت (القاعِدَة ١٠).
    /// والصَفحَةُ تَرسُمُ الاستِمارَةَ بِالبَوّابَةِ نَفسِها، فَهذا هُوَ «لا رَمزَ ⇒ لا زِرّ».</summary>
    [Fact]
    public void Without_a_carrier_no_deal_and_no_actor_is_ever_offered_the_ship_form()
    {
        var checkedCases = 0;
        var allowedWithCarrier = 0;
        foreach (var stage in Enum.GetValues<DealStage>())
        foreach (var status in Enum.GetValues<DealStatus>())
        foreach (var pay in new[] { CheckoutPayMethods.CashOnDelivery, CheckoutPayMethods.Card, null })
        foreach (var placed in new[] { false, true })
        foreach (var actor in new[] { Seller, Buyer, Stranger })
        {
            var deal = With(d =>
            {
                d.Stage = stage;
                d.Status = status;
                if (pay is null) d.Attributes.Remove(CheckoutPayMethods.AttributeKey);
                else d.Attributes[CheckoutPayMethods.AttributeKey] = pay;
                if (placed) d.Refs[DealShipping.OrderRef] = d.Id.ToString("N");
            });
            var by = new DealShipper(actor, "");
            Assert.False(DealShipAuthorization.IsAllowed(deal, by, carrierReady: false));
            if (DealShipAuthorization.IsAllowed(deal, by, carrierReady: true)) allowedWithCarrier++;
            checkedCases++;
        }
        Assert.True(checkedCases >= 500, $"أَداةٌ عَمياء: {checkedCases} حالَة (‏8 مَراحِلَ × 4 حالاتٍ × 3 طُرُقِ دَفعٍ × 2 × 3 فاعِلين = 576).");
        // ومَعَ الناقِل: البائِعُ وَحدَه، لِصَفقَةِ نَقدٍ فَعّالَةٍ عِندَ Confirmed أَو Paid، بِطَلَبٍ وبِلا طَلَب = 4.
        Assert.Equal(4, allowedWithCarrier);
    }

    [Fact]
    public void The_deal_page_draws_the_ship_form_with_the_endpoints_own_gate_and_reads_no_session()
    {
        var page = DealPage();
        var code = WriteEndpointGuardTests.StripComments(WriteEndpointGuardTests.StripMarkupComments(page));
        Assert.Contains("@inject Services.Deals.DealShipping Shipping", code, StringComparison.Ordinal);
        Assert.DoesNotContain("IDocumentSession", code, StringComparison.Ordinal);
        Assert.DoesNotContain("IDocumentStore", code, StringComparison.Ordinal);
        Assert.Contains(
            "canShip = DealShipAuthorization.IsAllowed(deal, new DealShipper(uid, Auth.UserName ?? \"\"), Shipping.CarrierReady(Slug));",
            code, StringComparison.Ordinal);
        Assert.Contains("shipResumes = canShip && DealShipping.IsPlaced(deal);", code, StringComparison.Ordinal);

        // الاستِمارَةُ داخِلَ `@if (canShip)` وَحدَها، وحَقلُ المَدينَةِ داخِلَ `@if (!shipResumes)`.
        var gate = code.IndexOf("@if (canShip)", StringComparison.Ordinal);
        var form = code.IndexOf("action=\"@($\"/{Slug}/deals/{deal.Id}/ship\")\"", StringComparison.Ordinal);
        var city = code.IndexOf("<input name=\"city\"", StringComparison.Ordinal);
        var cityGate = code.IndexOf("@if (!shipResumes)", StringComparison.Ordinal);
        Assert.True(gate > 0 && form > gate && cityGate > form && city > cityGate,
            $"تَرتيبُ الحارِس: canShip={gate} form={form} !shipResumes={cityGate} city={city}");
        Assert.Equal(1, CountOf(code, "/deals/{deal.Id}/ship\""));
    }

    [Fact]
    public void The_label_and_tracking_links_are_drawn_for_both_parties_from_the_shared_function_in_a_new_tab()
    {
        var code = WriteEndpointGuardTests.StripComments(WriteEndpointGuardTests.StripMarkupComments(DealPage()));
        // بَعدَ سَطرِ الإلغاءِ غَيرِ المَشروطِ لا داخِلَ فَرعِ البائِع — فَلِلطَرَفَين.
        var links = code.IndexOf("(labelUrl, trackingUrl) = DealShipping.LinksOf(deal);", StringComparison.Ordinal);
        var cancel = code.IndexOf("canCancel = DealCancelAuthorization.IsAllowed(", StringComparison.Ordinal);
        Assert.True(cancel > 0 && links > cancel, $"الرابِطانِ لا يُحسَبانِ لِلطَرَفَين: {cancel}/{links}");

        var block = code.IndexOf("@if (labelUrl is not null || trackingUrl is not null)", StringComparison.Ordinal);
        var action = code.IndexOf("<div id=\"deal-action\"></div>", StringComparison.Ordinal);
        Assert.True(block > 0 && action > block, $"كُتلَةُ الرابِطَين: {block}/{action}");
        Assert.Equal(2, CountOf(code, "target=\"_blank\" rel=\"noopener noreferrer\""));
        Assert.Contains("href=\"@labelUrl\"", code, StringComparison.Ordinal);
        Assert.Contains("href=\"@trackingUrl\"", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_new_page_text_is_a_dictionary_key_with_arabic_text()
    {
        var page = DealPage();
        var ar = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoRoot, "libs", "core", "ACommerce.Platform.I18n",
            "Locales", "ar.json"))).RootElement;
        var checkedKeys = 0;
        foreach (var key in new[]
                 {
                     "deals.detail.shipping_title", "deals.detail.shipping_label", "deals.detail.shipping_tracking",
                     "deals.detail.ship_city", "deals.detail.ship_submit", "deals.detail.ship_resume", "deals.detail.ship_hint",
                 })
        {
            Assert.Contains($"\"{key}\"", page, StringComparison.Ordinal);
            var text = ar.GetProperty(key).GetString()!;
            Assert.Matches("[\u0600-\u06FF]", text);
            Assert.DoesNotContain("<", text, StringComparison.Ordinal);
            checkedKeys++;
        }
        Assert.Equal(7, checkedKeys);
    }

    private static int CountOf(string text, string needle)
    {
        var n = 0;
        for (var i = text.IndexOf(needle, StringComparison.Ordinal); i >= 0; i = text.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
            n++;
        return n;
    }

    // ═══ ٦. النُقطَة — مَسارٌ حَرفِيّ، والحارِسُ في التَوقيع ═══════════════════════════

    [Fact]
    public void The_ship_endpoint_is_a_literal_route_whose_guard_is_in_the_signature()
    {
        var path = Path.Combine(RepoRoot, "libs", "templates", "ACommerce.Templates.Customer.Marketplace",
            "Shipping", "OtoShippingEndpoints.cs");
        var code = WriteEndpointGuardTests.StripComments(File.ReadAllText(path));
        var start = code.IndexOf("app.MapPost(\"/{slug}/deals/{id:guid}/ship\"", StringComparison.Ordinal);
        Assert.True(start > 0, "نُقطَةُ الشَحنِ غَيرُ مَوجودَةٍ بِمَسارٍ حَرفِيّ — الأَداةُ عَمياء.");
        var body = code[start..code.IndexOf(".RequireStoreWritable();", start, StringComparison.Ordinal)];

        var shipper = body.IndexOf("new Services.Deals.DealShipper(", StringComparison.Ordinal);
        var ship = body.IndexOf("shipping.ShipAsync(", StringComparison.Ordinal);
        Assert.True(shipper > 0 && ship > shipper, $"سِجِلُّ السُلطَةِ لا يَسبِقُ النِداء: {shipper}/{ship}");
        Assert.Contains("?err={res.Code}", body, StringComparison.Ordinal);
        // ولا قَرارَ في الجِسم: لا مَرحَلَةٌ ولا طَريقَةُ دَفعٍ ولا مَرجِعُ مالِكٍ يُقرَأ هُنا.
        foreach (var decision in new[] { "DealStage.", "pay_method", "listing_owner", "Refs[" })
            Assert.DoesNotContain(decision, body, StringComparison.Ordinal);
    }
}

// ═══ البُرهانُ الحَيّ — مَرجِعُ الطَلَبِ عَلى Postgres لا يَضيعُ حينَ تَسبِقُه كِتابَة (‏ADR-036 §١٠) ═══════════════
//
// إلحاقُ المُنَسِّقِ (٢) عَلى P1-6 بِحَرفِه: «يُعادُ تَطبيقُه … أَو يُرفَعُ خَطَأٌ مُسَمّى، بِاختِبارٍ حَيٍّ عَلى Postgres المَحَلِّيّ».
// بَوّابَةُ الحِفظِ نَفسُها (`SaveGate`، ‏DealConcurrencyTests) والمُستَأجِرُ المُستَعارُ نَفسُه (`TenantAlias`، ‏OtoOrderStatusTests):
// تُنشِئُ نُقطَةُ الشَحنِ الطَلَبَ عِندَ OTO (مُعالِجٌ يُعيدُ رَدَّ الوَثيقَة — لا شَبَكَة) وتَقرَأُ الصَفقَةَ لِتُلحِقَ `oto_order_id`
// وتَقِفُ قَبلَ الحِفظ؛ ثُمَّ تُكتَبُ كِتابَةٌ أُخرى وتُحفَظ؛ ثُمَّ تُستَأنَف — فَيَرُدُّها Marten نَفسُه، ويُعادُ الإلحاق.
// مُبَوَّبٌ بِـWASAYEL_LIVE_PROOF=1 وConnectionStrings__Postgres، ومُستَأجِرٌ مُؤَقَّتٌ يُمسَحُ بَعدَه.

public class LiveDealShipProofTests(ITestOutputHelper output)
{
    private sealed class ForbiddenPaymentProvider : IPaymentProvider
    {
        private static InvalidOperationException Touched() => new("صَفقَةُ النَقدِ لا تَمَسُّ المال — نودِيَ مُزَوِّدُ الدَفع.");
        public string ProviderName => "forbidden";
        public Task<PaymentResult> AuthorizeAsync(PaymentRequest req, string idempotencyKey, CancellationToken ct = default) => throw Touched();
        public Task<PaymentResult> CaptureAsync(string paymentId, decimal? amount = null, CancellationToken ct = default) => throw Touched();
        public Task<PaymentResult> RefundAsync(string paymentId, decimal amount, string reason, CancellationToken ct = default) => throw Touched();
        public Task<SubscriptionResult> CreateSubscriptionAsync(SubscriptionRequest req, string idempotencyKey, CancellationToken ct = default) => throw Touched();
        public Task<bool> CancelSubscriptionAsync(string subscriptionId, CancellationToken ct = default) => throw Touched();
        public Task<Invoice?> GetInvoiceAsync(string paymentId, CancellationToken ct = default) => throw Touched();
    }

    private static bool Enabled
        => LiveDealConcurrencyProofTests.Enabled && !string.IsNullOrEmpty(LiveDealConcurrencyProofTests.ConnectionString);

    /// <summary>صَفقَةُ نَقدٍ عِندَ <c>Confirmed</c> بِبائِعِها طَرَفاً — بِالخِدمَةِ الحَقيقِيَّةِ عَلى المَخزَنِ الحَقيقِيّ.</summary>
    private static async Task<Deal> ConfirmedAsync(DealsService plain, string slug, Guid buyer, Guid seller)
    {
        var born = await plain.StartAsync(slug, "marketplace", buyer, "مُشتَرٍ", null, "سِلعَة", 150m, new()
        {
            [CheckoutPayMethods.AttributeKey] = CheckoutPayMethods.CashOnDelivery,
            [Deal.DeliveryAddressAttribute] = "حَيُّ المِثال",
            [Deal.DeliveryPhoneAttribute] = "0500000000",
        });
        await plain.AttachRefAsync(slug, born.Id, DealsService.ListingOwnerRef, seller.ToString());
        await plain.BindListingOwnerAsync(slug, born.Id, seller.ToString());
        Assert.True((await plain.AcceptAsync(slug, born.Id, new DealAcceptor(seller, "بائِع"))).Ok);
        Assert.True((await plain.AdvanceAsync(slug, born.Id, seller, "بائِع")).Ok);   // ← Confirmed
        var confirmed = (await plain.LoadAsync(slug, born.Id))!;
        Assert.Equal(DealStage.Confirmed, confirmed.Stage);
        return confirmed;
    }

    /// <summary>سَطرُ السِجِلِّ لِلطِباعَة — والإلحاقُ بِمِفتاحِه لا بِقيمَتِه.</summary>
    private static string Line(DealEvent e) => e switch
    {
        { Action: "advanced" } => $"advanced→{e.StageAfter}",
        { Action: "note", Note: { } n } when n.StartsWith("attach ", StringComparison.Ordinal) && n.Contains('=') => n[..n.IndexOf('=')],
        _ => e.Action,
    };

    private async Task<(DealShipResult Result, Deal Final, OtoRouteHandler Oto)> RaceAsync(
        Func<DealsService, string, Guid, Guid, Task> otherWrite)
    {
        await using var store = LiveDealConcurrencyProofTests.BuildStore();
        var slug = DealShipping.DemoStore + "-zz-" + Guid.NewGuid().ToString("N")[..8];
        var buyer = Guid.NewGuid();
        var seller = Guid.NewGuid();
        try
        {
            var plain = new DealsService(store, new ForbiddenPaymentProvider());
            var deal = await ConfirmedAsync(plain, slug, buyer, seller);

            // الرَمزُ لِـtheme-demo وَحدَه — فَالشَحنُ يُنادى بِاسمِه، والمُستَأجِرُ المُؤَقَّتُ يُمَرَّرُ إلى المَخزَنِ تَحتَه.
            var gate = new SaveGate.Gate();
            var oto = new OtoRouteHandler();
            var options = Options.Create(new OtoOptions { Environment = OtoEnvironment.Staging, RefreshToken = "RT-live-proof" });
            var shipping = new DealShipping(
                new DealsService(SaveGate.Wrap(TenantAlias.Wrap(store, DealShipping.DemoStore, slug), gate), new ForbiddenPaymentProvider()),
                new OtoClient(new HttpClient(oto), options, new OtoTokenCache(), NullLogger<OtoClient>.Instance), options);

            // أَوَّلُ حِفظٍ في هذا التَدَفُّقِ حِفظُ `oto_order_id`: ما قَبلَه قِراءَةٌ ونِداءُ OTO.
            var sending = Task.Run(async () =>
            {
                gate.Marked.Value = true;
                return await shipping.ShipAsync(DealShipping.DemoStore, deal.Id, new DealShipper(seller, "بائِع"), "Riyadh");
            });
            await gate.Reached.Task.WaitAsync(TimeSpan.FromSeconds(60));
            await otherWrite(plain, slug, deal.Id, buyer);
            gate.Release.TrySetResult();
            var result = await sending.WaitAsync(TimeSpan.FromSeconds(60));

            var final = (await plain.LoadAsync(slug, deal.Id))!;
            output.WriteLine($"الشَحن: {(result.Ok ? "ok" : result.Code)} · النِهائِيَّة: {final.Status}/{final.Stage} · " +
                             $"oto_order_id: {(final.Refs.ContainsKey(DealShipping.OrderRef) ? "مَكتوب" : "—")} · " +
                             $"Create Order: {oto.CallsTo(OtoClient.CreateOrderPath)} · سِجِلّ: " +
                             string.Join(",", final.Timeline.Select(Line)));
            return (result, final, oto);
        }
        finally
        {
            await using var s = store.LightweightSession(slug);
            foreach (var d in await s.Query<Deal>().ToListAsync()) s.Delete(d);
            await s.SaveChangesAsync();
        }
    }

    /// <summary><b>المُوجَب</b>: مَرجِعٌ يُلحَقُ ويُحفَظُ بَينَ تَحميلِ إلحاقِ <c>oto_order_id</c> وحِفظِه — يَرُدُّ Marten الحِفظ، فَيُعادُ
    /// الإلحاقُ عَلى ما كُتِب: الكِتابَتانِ قائِمَتان، وسَطرُ إلحاقٍ واحِد، والصَفقَةُ عِندَ الشَحنِ بِطَلَبٍ واحِد.</summary>
    [Fact]
    public async Task A_write_saved_between_the_order_ref_load_and_its_save_stands_and_the_ref_is_re_applied()
    {
        if (!Enabled) return;

        var (result, final, oto) = await RaceAsync((plain, slug, id, _) => plain.AttachRefAsync(slug, id, "probe_note", "live"));

        Assert.True(result.Ok, result.Code);
        Assert.Equal((DealStatus.Active, DealStage.Shipping), (final.Status, final.Stage));
        Assert.Equal(final.Id.ToString("N"), final.Refs[DealShipping.OrderRef]);
        Assert.Equal("540789", final.Refs[DealShipping.OtoIdRef]);
        Assert.Equal("live", final.Refs["probe_note"]);
        Assert.Single(final.Timeline, e => e.Action == "note" && e.Note == $"attach {DealShipping.OrderRef}={final.Id:N}");
        Assert.Single(final.Timeline, e => e.Action == "advanced" && e.StageAfter == DealStage.Shipping);
        Assert.Equal(1, oto.CallsTo(OtoClient.CreateOrderPath));
    }

    /// <summary><b>السالِب</b>: المُشتَري يُلغي ويُحفَظُ إلغاؤُه بَينَهُما — يُعادُ الإلحاقُ عَلى الصَفقَةِ المُلغاة فَيَبقى مَرجِعُ
    /// الطَلَب (طَلَبٌ عِندَ OTO تَعرِفُه الصَفقَة)، والانتِقالُ يُرَدُّ بِـ<c>deal_changed_meanwhile</c>، ولا يُسأَلُ «Order Tracking».</summary>
    [Fact]
    public async Task A_cancel_saved_between_the_order_ref_load_and_its_save_keeps_the_ref_and_the_deal_does_not_move()
    {
        if (!Enabled) return;

        var (result, final, oto) = await RaceAsync(async (plain, slug, id, buyer) =>
            Assert.True((await plain.CancelAsync(slug, id, new DealCanceller(buyer, "مُشتَرٍ", IsStoreAdmin: false), "عَدَلتُ")).Ok));

        Assert.Equal(DealsService.ChangedMeanwhile, result.Code);
        Assert.Equal((DealStatus.Cancelled, DealStage.Confirmed), (final.Status, final.Stage));
        Assert.Equal(final.Id.ToString("N"), final.Refs[DealShipping.OrderRef]);
        Assert.Single(final.Timeline, e => e.Action == "note" && e.Note == $"attach {DealShipping.OrderRef}={final.Id:N}");
        Assert.DoesNotContain(final.Timeline, e => e.Action == "advanced" && e.StageAfter == DealStage.Shipping);
        Assert.Equal(1, oto.CallsTo(OtoClient.CreateOrderPath));
        Assert.Equal(0, oto.CallsTo(OtoClient.OrderStatusPath));
    }
}
