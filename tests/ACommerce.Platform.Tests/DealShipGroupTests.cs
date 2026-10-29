using System.Text.Json;
using System.Text.RegularExpressions;
using ACommerce.Kit.Delivery;
using Marten;
using ACommerce.Platform.Providers;
using ACommerce.Templates.Customer.Marketplace.Services;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ مُنتَجاتُ بائِعٍ واحِدٍ في سَلَّةٍ واحِدَة — كَم طَلَباً عِندَ OTO؟ (‏2026-10-29، قَرارُ المالِك د13؛ ‏ADR-033 §٣‑٩) ═══════════════
//
// **المَقيسُ قَبلَ اللَمس** (عِندَ `46170f8`): `POST /{slug}/checkout/submit` يَمشي `cart.Items` ويَلِدُ لِكُلِّ بَندٍ صَفقَةً بِـ`deals.StartAsync`
// (`MarketplaceTemplateExtensions.cs` — الحَلقَةُ `foreach (var item in cart.Items)`) بِأَربَعِ خَصائِصَ لا خامِسَ لَها: الكَمِّيَّة، والعُنوان، والهاتِف،
// وطَريقَةُ الدَفع — فَلا شَيءَ يَربِطُ صَفقَتَي بائِعٍ واحِدٍ مِن سَلَّةٍ واحِدَة. و`DealShipping.ShipAsync` يُنشِئُ لِكُلِّ صَفقَةٍ طَلَبَها عِندَ OTO بِمُعَرِّفِها
// (`OrderIdOf(deal) = deal.Id.ToString("N")`) ومَبلَغِها وَحدَه: ثَلاثَةُ مُنتَجاتٍ مِن بائِعٍ واحِدٍ ثَلاثُ بوليصاتٍ وثَلاثُ أُجرَةِ شَحن، والمُشتَري يَدفَعُ
// لِلمَندوبِ ثَلاثَ مَرّات. والإفصاحُ في خُطوَةِ الدَفعِ يَقولُها بِحَرفِها: «كُلُّ مُنتَجٍ في سَلَّتِكَ طَلَبٌ مُستَقِلٌّ مَعَ بائِعِه — يُشحَنُ وَحدَه».
//
// **التَوصيفُ قَبلَ التَبديل** (القاعِدَة ٣): صَفّا «اليَوم» أَدناه يُودَعانِ وَحدَهُما، ويُقلَبانِ بِاسمَيهِما في كوميتِ التَجميع.
//
// **وقُلِبا بِاسمَيهِما** (‏2026-10-29؛ تَوصيفُهُما `ee46c53`): صَفَقاتُ البائِعِ الواحِدِ مِن سَلَّةٍ واحِدَةٍ يُكتَبُ لِكُلٍّ مِنها أَخَواتُها بَعدَ الطَلَب
// (`DealShipGroup.Ref`)، و«أَكمِل الشَّحن» عَلى أَيٍّ مِنها يُنشِئُ طَلَباً واحِداً بِمُعَرِّفِ الأولى ومَجموعِ المَبالِغ، والجاهِزَةُ مِنها تَخرُجُ إلى «شَحن» مَعاً.

public class DealShipGroupTests(ITestOutputHelper output)
{
    private const string Slug = DealShipTests.Slug;

    internal sealed record Pair(DocWorld Docs, DealsService Deals, DealShipping Shipping, OtoRouteHandler Oto, Guid First, Guid Second);

    /// <summary>صَفقَتا نَقدٍ لِمُشتَرٍ واحِدٍ مِن بائِعٍ واحِدٍ عِندَ «تأكيد» — كَما تَلِدُهُما سَلَّةٌ واحِدَةٌ بِبَندَين (العُنوانُ والهاتِفُ واحِد)، وخَصائِصُ
    /// <paramref name="extra"/> فَوقَها لِكُلٍّ مِنهُما. والناقِلُ مَدخَلُ البائِعِ في خِزانَةِ العالَم.</summary>
    internal static async Task<Pair> TwoConfirmedCashDeals(Func<int, Dictionary<string, string>>? extra = null, Guid? first = null, Guid? second = null)
    {
        var docs = new DocWorld();
        var deals = new DealsService(docs.Store, new ForbiddenPaymentProvider());
        var ids = new[] { first ?? Guid.NewGuid(), second ?? Guid.NewGuid() };
        for (var i = 0; i < 2; i++)
        {
            var attributes = new Dictionary<string, string>
            {
                [CheckoutPayMethods.AttributeKey] = CheckoutPayMethods.CashOnDelivery,
                [Deal.DeliveryAddressAttribute] = "حَيُّ المِثال، شارِعُ الاختِبار",
                [Deal.DeliveryPhoneAttribute] = "0500000000",
                ["qty"] = "1",
            };
            foreach (var (k, v) in extra?.Invoke(i) ?? new()) attributes[k] = v;
            var deal = await deals.StartAsync(Slug, "marketplace", DealShipTests.Buyer, "مُشتَرٍ", Guid.NewGuid(), $"سِلعَة {i + 1}", 100m + 50m * i,
                attributes, id: ids[i]);
            await deals.AttachRefAsync(Slug, deal.Id, DealsService.ListingOwnerRef, DealShipTests.Seller.ToString());
            await deals.BindListingOwnerAsync(Slug, deal.Id, DealShipTests.Seller.ToString());
            Assert.True((await deals.AcceptAsync(Slug, deal.Id, new DealAcceptor(DealShipTests.Seller, "بائِع"))).Ok);
            Assert.True((await deals.AdvanceAsync(Slug, deal.Id, DealShipTests.Seller, "بائِع")).Ok);
            Assert.Equal(DealStage.Confirmed, (await deals.LoadAsync(Slug, deal.Id))!.Stage);
        }

        var vault = DealShipTests.NewVault();
        docs.Put(DealShipTests.VendorEntry(vault, Slug, DealShipTests.Seller));
        var oto = new OtoRouteHandler();
        var options = Options.Create(new OtoOptions { Environment = OtoEnvironment.Staging, RefreshToken = DealShipTests.ConfigToken });
        var client = new OtoClient(new HttpClient(oto), options, new OtoTokenCache(), NullLogger<OtoClient>.Instance);
        var shipping = new DealShipping(deals, client, options, null, new TenantProviderService(docs.Store), vault, new FixedClock(DealShipTests.Now));
        return new Pair(docs, deals, shipping, oto, ids[0], ids[1]);
    }

    /// <summary>أَجسامُ «Create Order» بِتَرتيبِها — المُعَرِّفُ والمَبلَغُ ووَصفُ البَند.</summary>
    internal static (string OrderId, decimal Amount, decimal AmountDue, string Description)[] Orders(OtoRouteHandler oto)
        => oto.Calls.Where(c => c.Path == OtoClient.CreateOrderPath).Select(c =>
        {
            var root = JsonDocument.Parse(c.Body!).RootElement;
            return (root.GetProperty("orderId").GetString()!, root.GetProperty("amount").GetDecimal(),
                    root.GetProperty("amount_due").GetDecimal(), root.GetProperty("item_description").GetString()!);
        }).ToArray();

    internal sealed class ForbiddenPaymentProvider : ACommerce.Kit.Payments.IPaymentProvider
    {
        private static InvalidOperationException Touched() => new("الشَحنُ لا يَمَسُّ المال — نودِيَ مُزَوِّدُ الدَفع.");
        public string ProviderName => "forbidden";
        public Task<ACommerce.Kit.Payments.PaymentResult> AuthorizeAsync(ACommerce.Kit.Payments.PaymentRequest req, string idempotencyKey, CancellationToken ct = default) => throw Touched();
        public Task<ACommerce.Kit.Payments.PaymentResult> CaptureAsync(string paymentId, decimal? amount = null, CancellationToken ct = default) => throw Touched();
        public Task<ACommerce.Kit.Payments.PaymentResult> RefundAsync(string paymentId, decimal amount, string reason, CancellationToken ct = default) => throw Touched();
        public Task<ACommerce.Kit.Payments.SubscriptionResult> CreateSubscriptionAsync(ACommerce.Kit.Payments.SubscriptionRequest req, string idempotencyKey, CancellationToken ct = default) => throw Touched();
        public Task<bool> CancelSubscriptionAsync(string subscriptionId, CancellationToken ct = default) => throw Touched();
        public Task<ACommerce.Kit.Payments.Invoice?> GetInvoiceAsync(string paymentId, CancellationToken ct = default) => throw Touched();
    }

    /// <summary>صَفقَتا الزَوجِ أُختانِ مِن سَلَّةٍ واحِدَة — كَما تَكتُبُهُما <c>/checkout/submit</c> بَعدَ الطَلَب (<see cref="DealShipGroup.WriteAsync"/>).</summary>
    internal static async Task<Pair> Grouped(Pair w)
    {
        await DealShipGroup.WriteAsync(w.Deals, Slug, new[] { (w.First, DealShipTests.Seller.ToString()), (w.Second, DealShipTests.Seller.ToString()) });
        return w;
    }

    private Deal Stored(Pair w, Guid id) => w.Docs.Read<Deal>(id.ToString())!;

    /// <summary><b>قُلِبَ بِاسمِه</b> (‏2026-10-29، قَرارُ المالِك د13 «ب»؛ كانَ <c>Today_two_cash_deals_of_one_vendor_and_one_buyer_ship_as_two_oto_orders_each_by_its_own_id</c>):
    /// صَفقَتا بائِعٍ واحِدٍ مِن سَلَّةٍ واحِدَة — «أَكمِل الشَّحن» عَلى الثانِيَةِ يُنشِئُ <b>طَلَباً واحِداً</b> بِمُعَرِّفِ الأولى ومَبلَغِ تَحصيلٍ هُوَ مَجموعُهُما ووَصفٍ
    /// بِعُنوانَيهِما، ويُكتَبُ مَرجِعُه عَلى الاثنَتَين، وتَخرُجانِ إلى «شَحن» بِالبوليصَةِ نَفسِها. ولا طَلَبَ ثانٍ.</summary>
    [Fact]
    public async Task Two_cash_deals_of_one_vendor_from_one_checkout_ship_as_one_oto_order_by_the_first_id_with_the_summed_amount()
    {
        var w = await Grouped(await TwoConfirmedCashDeals());

        var shipped = await w.Shipping.ShipAsync(Slug, w.Second, DealShipTests.Vendor, "Riyadh");

        var orders = Orders(w.Oto);
        var (first, second) = (Stored(w, w.First), Stored(w, w.Second));
        output.WriteLine($"طَلَباتُ OTO: {orders.Length} — {string.Join(" · ", orders.Select(o => $"{o.OrderId[..8]}… {o.Amount}/{o.AmountDue} «{o.Description}»"))} · " +
                         $"المَرحَلَتان: {first.Stage}/{second.Stage}");
        Assert.True(shipped.Ok, shipped.Code);
        Assert.Equal((w.First.ToString("N"), 250m, 250m, "سِلعَة 1 · سِلعَة 2"), Assert.Single(orders));
        Assert.Equal(w.First.ToString("N"), first.Refs[DealShipping.OrderRef]);
        Assert.Equal(w.First.ToString("N"), second.Refs[DealShipping.OrderRef]);
        Assert.Equal(DealShipGroup.ValueFor(new[] { w.First, w.Second }), first.Refs[DealShipping.OrderMembersRef]);
        Assert.False(second.Refs.ContainsKey(DealShipping.OrderMembersRef));
        Assert.Equal(DealStage.Shipping, first.Stage);
        Assert.Equal(DealStage.Shipping, second.Stage);
        Assert.Equal(DealShipping.LinksOf(first), DealShipping.LinksOf(second));
        Assert.Equal(1, w.Oto.CallsTo(OtoClient.OrderStatusPath));

        // ونَقرَةٌ ثانِيَةٌ عَلى الأولى — وقَد شُحِنَت — لا تُنشِئُ شَيئاً.
        var again = await w.Shipping.ShipAsync(Slug, w.First, DealShipTests.Vendor, "Riyadh");
        Assert.False(again.Ok);
        Assert.Single(Orders(w.Oto));
    }

    /// <summary>وصَفقَتا بائِعٍ واحِدٍ مِن سَلَّتَينِ (بِلا أَخَوات) تُشحَنانِ بِطَلَبَينِ كَما كانَتا — السُلوكُ القَديمُ لِكُلِّ ما لَم تَجمَعه سَلَّة.</summary>
    [Fact]
    public async Task Two_deals_of_one_vendor_from_two_checkouts_still_ship_as_two_orders_each_by_its_own_id()
    {
        var w = await TwoConfirmedCashDeals();

        Assert.True((await w.Shipping.ShipAsync(Slug, w.First, DealShipTests.Vendor, "Riyadh")).Ok);
        Assert.Equal(DealStage.Confirmed, Stored(w, w.Second).Stage);
        Assert.True((await w.Shipping.ShipAsync(Slug, w.Second, DealShipTests.Vendor, "Riyadh")).Ok);

        var orders = Orders(w.Oto);
        Assert.Equal(new[] { (w.First.ToString("N"), 100m, 100m, "سِلعَة 1"), (w.Second.ToString("N"), 150m, 150m, "سِلعَة 2") }, orders);
        Assert.All(new[] { Stored(w, w.First), Stored(w, w.Second) }, d => Assert.False(d.Refs.ContainsKey(DealShipping.OrderMembersRef)));
    }

    /// <summary>وأُختٌ لَم تَبلُغ «تأكيد» (عِندَ «حَجز») لا تَدخُلُ الطَلَب: الأولى وَحدَها بِمُعَرِّفِها ومَبلَغِها، ولا مَرجِعَ صَفَقاتٍ (طَلَبٌ بِصَفقَةٍ واحِدَة)؛ والأُختُ
    /// بِلا طَلَب، ثُمَّ تُشحَنُ وَحدَها بِمُعَرِّفِها حينَ تَجهَز.
    /// <para><b>قُلِبَ بِاسمِه</b> (‏2026-10-29، مُراجَعَةُ تَيّارِ S4 الأولى — إتمامُ قَرارِ المالِك د13 «ب»: «تُجمَّع منتجات البائع الواحد في السلّة في طلب واحد وبوليصة
    /// واحدة»؛ كانَ <c>A_sibling_not_yet_ready_is_left_out_and_ships_alone_later</c>، مُودَعٌ في <c>5e8ae1f</c> — بوليصَتانِ وتَحصيلانِ لِسَلَّةٍ واحِدَة): أُختٌ عِندَ
    /// «حَجز» تَرُدُّ النَقرَةَ بِـ<c>siblings_not_ready</c> <b>بِلا نِداءٍ واحِدٍ لِـOTO</b> ولا كِتابَة؛ وحينَ يُؤَكِّدُها البائِعُ تُشحَنانِ في طَلَبٍ واحِدٍ بِمَجموعِهِما.</para></summary>
    [Fact]
    public async Task A_sibling_not_yet_confirmed_holds_the_click_with_nothing_sent_to_oto_until_the_vendor_confirms_it_then_both_ship_as_one_order()
    {
        var w = await Grouped(await TwoConfirmedCashDeals());
        var held = Stored(w, w.Second);
        held.Stage = DealStage.Booked;
        w.Docs.Put(held);
        var saves = w.Docs.SaveCalls;

        var refused = await w.Shipping.ShipAsync(Slug, w.First, DealShipTests.Vendor, "Riyadh");
        output.WriteLine($"النَقرَة: {refused.Code ?? "ok"} · نِداءاتُ OTO: {w.Oto.Calls.Count} · حِفظات: {w.Docs.SaveCalls - saves} · " +
                         $"المَرحَلَتان: {Stored(w, w.First).Stage}/{Stored(w, w.Second).Stage}");
        Assert.Equal(DealShipAuthorization.SiblingsNotReady, refused.Code);
        Assert.Empty(w.Oto.Calls);
        Assert.Equal(saves, w.Docs.SaveCalls);
        Assert.False(DealShipping.IsPlaced(Stored(w, w.First)));
        Assert.Equal(DealStage.Confirmed, Stored(w, w.First).Stage);

        Assert.True((await w.Deals.AdvanceAsync(Slug, w.Second, DealShipTests.Seller, "بائِع")).Ok);
        var shipped = await w.Shipping.ShipAsync(Slug, w.First, DealShipTests.Vendor, "Riyadh");

        Assert.True(shipped.Ok, shipped.Code);
        Assert.Equal((w.First.ToString("N"), 250m, 250m, "سِلعَة 1 · سِلعَة 2"), Assert.Single(Orders(w.Oto)));
        Assert.Equal(DealStage.Shipping, Stored(w, w.First).Stage);
        Assert.Equal(DealStage.Shipping, Stored(w, w.Second).Stage);
    }

    /// <summary>وأُختٌ أَلغاها طَرَفٌ لا تَنتَظِرُ شَيئاً (‏2026-10-29، مُراجَعَةُ تَيّارِ S4 الأولى): الأولى تُشحَنُ وَحدَها بِمُعَرِّفِها ومَبلَغِها، ولا مَرجِعَ صَفَقات —
    /// والمُلغاةُ بِلا طَلَب. فَمَخرَجُ البائِعِ مِن الانتِظارِ «تأكيد» الأُختِ أَو إلغاؤُها، لا بوليصَةٌ ثانِيَة.</summary>
    [Fact]
    public async Task A_cancelled_sibling_does_not_hold_and_the_rest_ships_alone()
    {
        var w = await Grouped(await TwoConfirmedCashDeals());
        var cancelled = Stored(w, w.Second);
        cancelled.Status = DealStatus.Cancelled;
        w.Docs.Put(cancelled);

        var shipped = await w.Shipping.ShipAsync(Slug, w.First, DealShipTests.Vendor, "Riyadh");

        Assert.True(shipped.Ok, shipped.Code);
        Assert.Equal((w.First.ToString("N"), 100m, 100m, "سِلعَة 1"), Assert.Single(Orders(w.Oto)));
        Assert.False(Stored(w, w.First).Refs.ContainsKey(DealShipping.OrderMembersRef));
        Assert.False(DealShipping.IsPlaced(Stored(w, w.Second)));
    }

    /// <summary>وطَلَبٌ لَم تُثبِت OTO شُحنَتَه بَعد (لا بوليصَة) يَبقى عَلى الاثنَتَينِ بِمَرجِعِه؛ والنَقرَةُ التالِيَةُ عَلى أَيٍّ مِنهُما تَستَأنِفُه بِلا طَلَبٍ ثانٍ وتُخرِجُهُما مَعاً.</summary>
    [Fact]
    public async Task An_unconfirmed_group_order_is_resumed_from_either_deal_without_a_second_order_and_both_ship()
    {
        var w = await Grouped(await TwoConfirmedCashDeals());
        w.Oto.Routes[OtoClient.OrderStatusPath] = body => OtoRouteHandler.Tracking(OtoRouteHandler.OrderIdIn(body), label: null);

        var first = await w.Shipping.ShipAsync(Slug, w.First, DealShipTests.Vendor, "Riyadh");
        Assert.Equal(DealShipping.ShipmentUnconfirmed, first.Code);
        Assert.All(new[] { Stored(w, w.First), Stored(w, w.Second) }, d => Assert.Equal(w.First.ToString("N"), d.Refs[DealShipping.OrderRef]));

        w.Oto.Routes[OtoClient.OrderStatusPath] = body => OtoRouteHandler.Tracking(OtoRouteHandler.OrderIdIn(body));
        var resumed = await w.Shipping.ShipAsync(Slug, w.Second, DealShipTests.Vendor, null);

        Assert.True(resumed.Ok, resumed.Code);
        Assert.Single(Orders(w.Oto));
        Assert.Equal(w.First.ToString("N"), OtoRouteHandler.OrderIdIn(w.Oto.Calls.Last(c => c.Path == OtoClient.OrderStatusPath).Body));
        Assert.Equal(DealStage.Shipping, Stored(w, w.First).Stage);
        Assert.Equal(DealStage.Shipping, Stored(w, w.Second).Stage);
    }

    /// <summary>وصَفقَةٌ سَمّاها طَلَبُ أُختِها ولَم يُكتَب مَرجِعُها (انقَطَعَ الحِفظُ بَعدَ الإنشاء) تَتَبَنّاه: لا «Create Order» ثانٍ، ومَرجِعُها مَرجِعُ أُختِها.</summary>
    [Fact]
    public async Task A_deal_named_by_its_siblings_order_without_its_own_ref_adopts_that_order_and_creates_none()
    {
        var w = await Grouped(await TwoConfirmedCashDeals());
        await w.Deals.AttachRefAsync(Slug, w.First, DealShipping.OrderMembersRef, DealShipGroup.ValueFor(new[] { w.First, w.Second }));
        await w.Deals.AttachRefAsync(Slug, w.First, DealShipping.OrderRef, w.First.ToString("N"));
        await w.Deals.AttachRefAsync(Slug, w.First, DealShipping.OtoIdRef, "540789");

        var adopted = await w.Shipping.ShipAsync(Slug, w.Second, DealShipTests.Vendor, null);

        Assert.True(adopted.Ok, adopted.Code);
        Assert.Empty(Orders(w.Oto));
        Assert.Equal(w.First.ToString("N"), Stored(w, w.Second).Refs[DealShipping.OrderRef]);
        Assert.Equal("540789", Stored(w, w.Second).Refs[DealShipping.OtoIdRef]);
        Assert.Equal(DealStage.Shipping, Stored(w, w.Second).Stage);
        Assert.Equal(DealStage.Shipping, Stored(w, w.First).Stage);
    }

    /// <summary>وطَلَبٌ أَغلَقَته OTO يُكتَبُ إغلاقُه عَلى صَفَقاتِه كُلِّها — فَلا «أَكمِل الشَّحن» لِأَيٍّ مِنها.</summary>
    [Fact]
    public async Task A_group_order_closed_at_oto_is_written_on_every_deal_of_it()
    {
        var w = await Grouped(await TwoConfirmedCashDeals());
        w.Oto.Routes[OtoClient.OrderStatusPath] = body => OtoRouteHandler.Tracking(OtoRouteHandler.OrderIdIn(body), status: "canceled");

        var res = await w.Shipping.ShipAsync(Slug, w.First, DealShipTests.Vendor, "Riyadh");

        Assert.Equal(DealShipAuthorization.OrderClosedAtOto, res.Code);
        Assert.All(new[] { Stored(w, w.First), Stored(w, w.Second) }, d => Assert.True(DealShipping.ClosedAtOto(d)));
    }

    /// <summary>وإشعارُ «سُلِّمَ» لِطَلَبِ المَجموعَةِ يُسَلِّمُ صَفَقاتِه كُلَّها بِالجَوابِ نَفسِه — والإشعارُ يَحمِلُ مُعَرِّفَ الأولى وَحدَه.</summary>
    [Fact]
    public async Task A_delivered_notice_for_the_group_order_delivers_every_deal_of_it()
    {
        var w = await Grouped(await TwoConfirmedCashDeals());
        Assert.True((await w.Shipping.ShipAsync(Slug, w.First, DealShipTests.Vendor, "Riyadh")).Ok);
        w.Oto.Routes[OtoClient.OrderStatusPath] = body => OtoRouteHandler.Tracking(OtoRouteHandler.OrderIdIn(body), status: "delivered");

        var outcome = await w.Shipping.ReceiveAsync(Slug, OtoOrderStatusTests.Notice(w.First));

        output.WriteLine($"الوارِد: {outcome.Code} · {Stored(w, w.First).Stage}/{Stored(w, w.Second).Stage}");
        Assert.Equal(OtoOrderStatusGate.Delivered, outcome.Code);
        Assert.Equal(DealStage.Delivered, Stored(w, w.First).Stage);
        Assert.Equal(DealStage.Delivered, Stored(w, w.Second).Stage);
        Assert.Equal(OtoOrderStatusGate.AlreadyDelivered, (await w.Shipping.ReceiveAsync(Slug, OtoOrderStatusTests.Notice(w.First))).Code);
    }

    // ═══ الدالَّتانِ النَقِيَّتان ══════════════════════════════════════════════════════════════════════════════

    /// <summary>‏<see cref="DealShipGroup.ForCheckout"/> مُوجَباً وسالِباً: بائِعٌ بِثَلاثٍ مَجموعَةٌ بِتَرتيبِ الوِلادَة، وبائِعٌ بِواحِدَةٍ لا شَيءَ لَه، وبائِعٌ فارِغٌ لا يُجمَع.</summary>
    [Fact]
    public void ForCheckout_groups_two_or_more_per_vendor_in_birth_order_and_nothing_else()
    {
        var (a1, b1, a2, a3, e1, e2) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var groups = DealShipGroup.ForCheckout(new[] { (a1, "A"), (b1, "B"), (a2, "A"), (a3, "A"), (e1, ""), (e2, " ") });

        output.WriteLine($"مَجموعات: {groups.Count}");
        Assert.Equal(3, groups.Count);
        var value = DealShipGroup.ValueFor(new[] { a1, a2, a3 });
        Assert.All(new[] { a1, a2, a3 }, id => Assert.Equal(value, groups[id]));
        Assert.False(groups.ContainsKey(b1));
        Assert.False(groups.ContainsKey(e1));
        Assert.Empty(DealShipGroup.ForCheckout(new[] { (a1, "A"), (b1, "B") }));
        Assert.Empty(DealShipGroup.ForCheckout(Array.Empty<(Guid, string)>()));
    }

    /// <summary>‏<see cref="DealShipGroup.MembersOf"/>: المَكتوبُ كَما هُوَ؛ وغِيابُه أَو فَسادُه أَو تَكرارُه أَو غِيابُ الصَفقَةِ عَنه ⇒ الصَفقَةُ وَحدَها.</summary>
    [Theory]
    [InlineData("good", 2)]
    [InlineData("absent", 1)]
    [InlineData("garbled", 1)]
    [InlineData("duplicate", 1)]
    [InlineData("not-self", 1)]
    public void MembersOf_reads_the_written_group_or_falls_back_to_the_deal_alone(string kind, int count)
    {
        var deal = new Deal { Id = Guid.NewGuid() };
        var other = Guid.NewGuid();
        var raw = kind switch
        {
            "good" => DealShipGroup.ValueFor(new[] { deal.Id, other }),
            "garbled" => deal.Id.ToString("D") + ",not-a-guid",
            "duplicate" => DealShipGroup.ValueFor(new[] { deal.Id, deal.Id }),
            "not-self" => DealShipGroup.ValueFor(new[] { other, Guid.NewGuid() }),
            _ => null,
        };
        if (raw is not null) deal.Refs[DealShipGroup.Ref] = raw;
        var members = DealShipGroup.MembersOf(deal);
        Assert.Equal(count, members.Count);
        Assert.Contains(deal.Id, members);
        Assert.Equal(count - 1, DealShipGroup.OthersOf(deal));
    }

    /// <summary>ومُسَوَّدَةُ صَفقَةٍ واحِدَةٍ في قائِمَةٍ هي مُسَوَّدَتُها بِحَرفِها — سَلَّةٌ بِبَندٍ لِكُلِّ بائِعٍ لا يَتَغَيَّرُ طَلَبُها.</summary>
    [Fact]
    public async Task A_one_deal_draft_through_the_group_overload_is_byte_for_byte_the_single_draft()
    {
        var w = await TwoConfirmedCashDeals();
        var deal = Stored(w, w.First);
        Assert.Equal(DealShipping.DraftFor(deal, "Riyadh"), DealShipping.DraftFor(new[] { deal }, "Riyadh"));
    }

    /// <summary><b>قُلِبَ بِاسمِه</b> (‏2026-10-29، قَرارُ المالِك د13 «ب»؛ كانَ <c>Today_the_checkout_births_each_item_with_four_attributes_and_nothing_links_a_vendors_deals</c>):
    /// الوِلادَةُ بِخَصائِصِها الأَربَعِ كَما كانَت بِحَرفِها، <b>ثُمَّ</b> — بَعدَ آخِرِ رُجوعٍ بِرَمزِ فَشَل (الفاتورَةُ الَّتي لَم تُفتَح) وقَبلَ حَذفِ السَلَّة — تُكتَبُ أَخَواتُ
    /// صَفَقاتِ كُلِّ بائِعٍ بِـ<see cref="DealShipGroup.WriteAsync"/> مِن مالِكِ إعلانِ كُلِّ صَفقَةٍ رُبِطَ؛ والإفصاحُ «لِكُلِّ بائِع».</summary>
    [Fact]
    public void The_checkout_births_each_item_with_its_four_attributes_then_links_each_vendors_deals_before_the_cart_is_dropped()
    {
        var body = CheckoutPerItemDisclosureTests.SubmitBody();
        var at = body.IndexOf("attributes: Services.Deals.GatewayPayments.MarkedAtBirth(new()", StringComparison.Ordinal);
        Assert.True(at > 0, "لا خَصائِصَ وِلادَة — الأَداةُ عَمياء.");
        var block = body[at..body.IndexOf("}, viaGateway));", at, StringComparison.Ordinal)];
        var keys = Regex.Matches(block, @"\[(?<k>[^\]]+)\]\s*=").Select(m => m.Groups["k"].Value).ToArray();
        output.WriteLine($"خَصائِصُ الوِلادَة: {keys.Length} — {string.Join(" · ", keys)}");
        Assert.Equal(new[]
        {
            "\"qty\"", "Services.Deals.Deal.DeliveryAddressAttribute", "Services.Deals.Deal.DeliveryPhoneAttribute",
            "Services.Deals.CheckoutPayMethods.AttributeKey",
        }, keys);

        var code = WriteEndpointGuardTests.StripComments(body);
        var bound = code.IndexOf("bornVendors.Add((deal.Id, oid));", StringComparison.Ordinal);
        var lastRefusal = code.LastIndexOf("RefusalPath(Services.Deals.CheckoutPayMethods.GatewayOpenFailed)", StringComparison.Ordinal);
        var write = code.IndexOf("await Services.Deals.DealShipGroup.WriteAsync(deals, slug, bornVendors);", StringComparison.Ordinal);
        var drop = code.IndexOf("s.Delete<ACommerce.Kit.Cart.Cart>(userId);", StringComparison.Ordinal);
        output.WriteLine($"الرَبط {bound} · آخِرُ رُجوع {lastRefusal} · الكِتابَة {write} · حَذفُ السَلَّة {drop}");
        Assert.True(bound > 0 && code.IndexOf("born.Add(deal.Id);", StringComparison.Ordinal) is var born && born > 0 && bound > born);
        Assert.True(lastRefusal > bound && write > lastRefusal && drop > write);
        Assert.Single(Regex.Matches(code, Regex.Escape("DealShipGroup.WriteAsync(")));
        Assert.Contains("لِكُلِّ بائِعٍ", CheckoutCardInvoiceDisclosureTests.PerItemNote, StringComparison.Ordinal);
        Assert.DoesNotContain("يُشحَنُ وَحدَه", CheckoutCardInvoiceDisclosureTests.PerItemNote, StringComparison.Ordinal);
    }

    /// <summary>وصَفحَةُ الصَفقَةِ تَقولُها لِلبائِعِ قَبلَ النَقرَة: داخِلَ استِمارَةِ الشَحنِ وفي الإنشاءِ لا الاستِئناف، بِالدالَّةِ الَّتي يَجمَعُ بِها الشَحن؛ ونَصُّها حَيّ.</summary>
    [Fact]
    public void The_deal_page_tells_the_vendor_before_the_click_that_ready_siblings_ship_with_it()
    {
        var page = WriteEndpointGuardTests.StripComments(WriteEndpointGuardTests.StripMarkupComments(File.ReadAllText(Path.Combine(
            ThemeZeroEquivalenceTests.RepoRoot, "libs", "templates", "ACommerce.Templates.Customer.Marketplace", "Components", "Pages", "MyDealDetail.razor"))));
        var form = page.IndexOf("@if (canShip)", StringComparison.Ordinal);
        var close = page.IndexOf("</form>", form, StringComparison.Ordinal);
        var hint = page.IndexOf("@if (!shipResumes && DealShipGroup.OthersOf(deal) is > 0 and var others)", StringComparison.Ordinal);
        var key = page.IndexOf("@L.Markup(\"deals.detail.ship_group_hint\", others)", StringComparison.Ordinal);
        Assert.True(form > 0 && hint > form && key > hint && close > key, $"الاستِمارَة {form} · الشَرط {hint} · المِفتاح {key} · الإغلاق {close}");
        Assert.Single(Regex.Matches(page, Regex.Escape("deals.detail.ship_group_hint")));
        var text = ACommerce.Platform.I18n.LocaleCatalog.Text(ACommerce.Platform.I18n.LocaleCatalog.Arabic, "deals.detail.ship_group_hint");
        Assert.NotEqual("deals.detail.ship_group_hint", text);
        Assert.Contains("{0}", text, StringComparison.Ordinal);
        Assert.Contains("بوليصَةٍ واحِدَة", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// <para><b>وأَخَواتٌ تَنتَظِرُ تَأكيدَ البائِعِ تُسَمّى بِروابِطِها مَكانَ الاستِمارَة</b> (‏2026-10-29، مُراجَعَةُ تَيّارِ S4 الأولى — إتمامُ د13 «ب»؛ القاعِدَة ١٢):
    /// الصَفحَةُ تَسأَلُ <see cref="DealShipAuthorization.WaitingSiblings"/> عَلى <see cref="DealShipping.SiblingsOfAsync"/> — الدالَّتَينِ اللَّتَينِ تَرُدُّ بِهِما <c>/ship</c>
    /// — بَعدَ بَوّابَتِها ولِطَلَبٍ لَم يُنشَأ، فَتُطفِئُ <c>canShip</c> وتَرسُمُ النَصَّ وروابِطَ الأَخَوات؛ ونَصّا القاموسِ حَيّانِ ولا يَقولانِ «جاهِزاً» عَن انتِظار.</para>
    /// </summary>
    [Fact]
    public void The_deal_page_names_the_siblings_still_waiting_instead_of_a_ship_form_the_endpoint_would_refuse()
    {
        var page = WriteEndpointGuardTests.StripComments(WriteEndpointGuardTests.StripMarkupComments(File.ReadAllText(Path.Combine(
            ThemeZeroEquivalenceTests.RepoRoot, "libs", "templates", "ACommerce.Templates.Customer.Marketplace", "Components", "Pages", "MyDealDetail.razor"))));
        var resumes = page.IndexOf("shipResumes = canShip && DealShipping.IsPlaced(deal);", StringComparison.Ordinal);
        var asks = page.IndexOf("DealShipAuthorization.WaitingSiblings(await Shipping.SiblingsOfAsync(Slug, deal), new DealShipper(uid, Auth.UserName ?? \"\"))", StringComparison.Ordinal);
        var off = page.IndexOf("canShip = canShip && shipWaiting.Count == 0;", StringComparison.Ordinal);
        Assert.True(resumes > 0 && asks > resumes && off > asks, $"الاستِئناف {resumes} · السُؤال {asks} · الإطفاء {off}");
        Assert.Contains("shipWaiting = canShip && !shipResumes", page, StringComparison.Ordinal);

        var form = page.IndexOf("@if (canShip)", StringComparison.Ordinal);
        var branch = page.IndexOf("else if (shipWaiting.Count > 0", form, StringComparison.Ordinal);
        var key = page.IndexOf("@L.Markup(\"deals.detail.ship_group_waiting\", shipWaiting.Count)", StringComparison.Ordinal);
        var link = page.IndexOf("AuthSession.LinkFor(Slug, Role, $\"deals/{waiting.Id}\")", StringComparison.Ordinal);
        Assert.True(form > 0 && branch > form && key > branch && link > key, $"الاستِمارَة {form} · الفَرع {branch} · المِفتاح {key} · الرابِط {link}");

        foreach (var k in new[] { "deals.detail.ship_group_waiting", "deals.detail.ship_group_hint", DealRefusalTexts.KeyFor(DealShipAuthorization.SiblingsNotReady)! })
        {
            var text = ACommerce.Platform.I18n.LocaleCatalog.Text(ACommerce.Platform.I18n.LocaleCatalog.Arabic, k);
            Assert.NotEqual(k, text);
            Assert.Contains("طَلَبٍ واحِدٍ وبوليصَةٍ واحِدَة", text, StringComparison.Ordinal);
            Assert.DoesNotContain("جاهِزاً", text, StringComparison.Ordinal);
        }
    }
}

// ═══ البُرهانُ الحَيّ — Postgres (‏WASAYEL_LIVE_PROOF=1 وConnectionStrings__Postgres) ═══════════════════════
//
// عالَمُ الوَثائِقِ يُعيدُ الكائِنَ المَخزونَ نَفسَه فَيُخفي نُسخَةً قَديمَة؛ وفي Postgres كُلُّ تَحميلٍ نُسخَةٌ — فَيُقاسُ هُنا أَنّ مُعَرِّفَ الطَلَبِ يُقرَأُ مِن الصَفقَةِ
// المَكتوبَةِ لا مِن المُحَمَّلَةِ قَبلَ الإلحاق (صَفقَةٌ في طَلَبِ أُختِها مُعَرِّفُ طَلَبِها غَيرُ مُعَرِّفِها). والمُستَأجِرُ المُستَعارُ نَفسُه (`TenantAlias`).

[Collection(LiveProofCollection.Name)]
public class LiveDealShipGroupProofTests(ITestOutputHelper output)
{
    private static bool Enabled
        => LiveDealConcurrencyProofTests.Enabled && !string.IsNullOrEmpty(LiveDealConcurrencyProofTests.ConnectionString);

    [Fact]
    public async Task Two_deals_of_one_checkout_ship_from_the_second_as_one_order_by_the_first_id_on_Postgres()
    {
        if (!Enabled) return;

        await using var store = LiveDealConcurrencyProofTests.BuildStore();
        var slug = DealShipping.DemoStore + "-zz-g" + Guid.NewGuid().ToString("N")[..8];
        var (buyer, seller) = (Guid.NewGuid(), Guid.NewGuid());
        try
        {
            var plain = new DealsService(store, new DealShipGroupTests.ForbiddenPaymentProvider());
            var ids = new List<Guid>();
            for (var i = 0; i < 2; i++)
            {
                var born = await plain.StartAsync(slug, "marketplace", buyer, "مُشتَرٍ", Guid.NewGuid(), $"سِلعَة {i + 1}", 100m + 50m * i, new()
                {
                    [CheckoutPayMethods.AttributeKey] = CheckoutPayMethods.CashOnDelivery,
                    [Deal.DeliveryAddressAttribute] = "حَيُّ المِثال",
                    [Deal.DeliveryPhoneAttribute] = "0500000000",
                });
                await plain.AttachRefAsync(slug, born.Id, DealsService.ListingOwnerRef, seller.ToString());
                await plain.BindListingOwnerAsync(slug, born.Id, seller.ToString());
                Assert.True((await plain.AcceptAsync(slug, born.Id, new DealAcceptor(seller, "بائِع"))).Ok);
                Assert.True((await plain.AdvanceAsync(slug, born.Id, seller, "بائِع")).Ok);
                ids.Add(born.Id);
            }
            await DealShipGroup.WriteAsync(plain, slug, ids.Select(id => (id, seller.ToString())).ToArray());

            var oto = new OtoRouteHandler();
            var options = Options.Create(new OtoOptions { Environment = OtoEnvironment.Staging, RefreshToken = "RT-live-proof" });
            var shipping = new DealShipping(new DealsService(TenantAlias.Wrap(store, DealShipping.DemoStore, slug), new DealShipGroupTests.ForbiddenPaymentProvider()),
                new OtoClient(new HttpClient(oto), options, new OtoTokenCache(), NullLogger<OtoClient>.Instance), options);

            var res = await shipping.ShipAsync(DealShipping.DemoStore, ids[1], new DealShipper(seller, "بائِع"), "Riyadh");

            var (first, second) = ((await plain.LoadAsync(slug, ids[0]))!, (await plain.LoadAsync(slug, ids[1]))!);
            var orders = DealShipGroupTests.Orders(oto);
            output.WriteLine($"الشَحن: {(res.Ok ? "ok" : res.Code)} · طَلَباتُ OTO: {orders.Length} · المَبلَغ: {orders.FirstOrDefault().Amount} · " +
                             $"المَرحَلَتان: {first.Stage}/{second.Stage} · Order Tracking سُئِلَ عَن الأولى: " +
                             $"{OtoRouteHandler.OrderIdIn(oto.Calls.Last(c => c.Path == OtoClient.OrderStatusPath).Body) == ids[0].ToString("N")}");
            Assert.True(res.Ok, res.Code);
            Assert.Equal((ids[0].ToString("N"), 250m, 250m, "سِلعَة 1 · سِلعَة 2"), Assert.Single(orders));
            Assert.Equal(ids[0].ToString("N"), first.Refs[DealShipping.OrderRef]);
            Assert.Equal(ids[0].ToString("N"), second.Refs[DealShipping.OrderRef]);
            Assert.Equal(DealShipGroup.ValueFor(ids), first.Refs[DealShipping.OrderMembersRef]);
            Assert.Equal(DealStage.Shipping, first.Stage);
            Assert.Equal(DealStage.Shipping, second.Stage);
        }
        finally
        {
            await using var s = store.LightweightSession(slug);
            foreach (var d in await s.Query<Deal>().ToListAsync()) s.Delete(d);
            await s.SaveChangesAsync();
        }
    }
    /// <summary><b>والانتِظارُ عَلى Postgres</b> (‏2026-10-29، مُراجَعَةُ تَيّارِ S4 الأولى — إتمامُ د13 «ب»): أُختٌ عِندَ «حَجز» تُقرَأُ مِن القاعِدَةِ نُسخَةً لا الكائِنَ
    /// المَخزونَ نَفسَه — فَالنَقرَةُ تُرَدُّ <c>siblings_not_ready</c> بِلا نِداءٍ لِـOTO والصَفقَتانِ بِحالِهِما؛ وبَعدَ «تأكيد» الأُختِ طَلَبٌ واحِدٌ بِمَجموعِهِما.</summary>
    [Fact]
    public async Task A_sibling_not_yet_confirmed_holds_the_click_then_both_ship_as_one_order_on_Postgres()
    {
        if (!Enabled) return;

        await using var store = LiveDealConcurrencyProofTests.BuildStore();
        var slug = DealShipping.DemoStore + "-zz-h" + Guid.NewGuid().ToString("N")[..8];
        var (buyer, seller) = (Guid.NewGuid(), Guid.NewGuid());
        try
        {
            var plain = new DealsService(store, new DealShipGroupTests.ForbiddenPaymentProvider());
            var ids = new List<Guid>();
            for (var i = 0; i < 2; i++)
            {
                var born = await plain.StartAsync(slug, "marketplace", buyer, "مُشتَرٍ", Guid.NewGuid(), $"سِلعَة {i + 1}", 100m + 50m * i, new()
                {
                    [CheckoutPayMethods.AttributeKey] = CheckoutPayMethods.CashOnDelivery,
                    [Deal.DeliveryAddressAttribute] = "حَيُّ المِثال",
                    [Deal.DeliveryPhoneAttribute] = "0500000000",
                });
                await plain.AttachRefAsync(slug, born.Id, DealsService.ListingOwnerRef, seller.ToString());
                await plain.BindListingOwnerAsync(slug, born.Id, seller.ToString());
                Assert.True((await plain.AcceptAsync(slug, born.Id, new DealAcceptor(seller, "بائِع"))).Ok);
                if (i == 0) Assert.True((await plain.AdvanceAsync(slug, born.Id, seller, "بائِع")).Ok);   // والثانِيَةُ عِندَ «حَجز»
                ids.Add(born.Id);
            }
            await DealShipGroup.WriteAsync(plain, slug, ids.Select(id => (id, seller.ToString())).ToArray());

            var oto = new OtoRouteHandler();
            var options = Options.Create(new OtoOptions { Environment = OtoEnvironment.Staging, RefreshToken = "RT-live-proof" });
            var shipping = new DealShipping(new DealsService(TenantAlias.Wrap(store, DealShipping.DemoStore, slug), new DealShipGroupTests.ForbiddenPaymentProvider()),
                new OtoClient(new HttpClient(oto), options, new OtoTokenCache(), NullLogger<OtoClient>.Instance), options);
            var shipper = new DealShipper(seller, "بائِع");

            var held = await shipping.ShipAsync(DealShipping.DemoStore, ids[0], shipper, "Riyadh");
            var heldCalls = oto.Calls.Count;
            var (first, second) = ((await plain.LoadAsync(slug, ids[0]))!, (await plain.LoadAsync(slug, ids[1]))!);
            output.WriteLine($"النَقرَة: {held.Code ?? "ok"} · نِداءاتُ OTO: {heldCalls} · المَرحَلَتان: {first.Stage}/{second.Stage}");
            Assert.Equal(DealShipAuthorization.SiblingsNotReady, held.Code);
            Assert.Equal(0, heldCalls);
            Assert.False(DealShipping.IsPlaced(first));
            Assert.Equal((DealStage.Confirmed, DealStage.Booked), (first.Stage, second.Stage));

            Assert.True((await plain.AdvanceAsync(slug, ids[1], seller, "بائِع")).Ok);
            var shipped = await shipping.ShipAsync(DealShipping.DemoStore, ids[0], shipper, "Riyadh");
            var orders = DealShipGroupTests.Orders(oto);
            (first, second) = ((await plain.LoadAsync(slug, ids[0]))!, (await plain.LoadAsync(slug, ids[1]))!);
            output.WriteLine($"بَعدَ التَأكيد: {(shipped.Ok ? "ok" : shipped.Code)} · طَلَباتُ OTO: {orders.Length} · المَبلَغ: {orders.FirstOrDefault().Amount} · " +
                             $"المَرحَلَتان: {first.Stage}/{second.Stage}");
            Assert.True(shipped.Ok, shipped.Code);
            Assert.Equal((ids[0].ToString("N"), 250m, 250m, "سِلعَة 1 · سِلعَة 2"), Assert.Single(orders));
            Assert.Equal((DealStage.Shipping, DealStage.Shipping), (first.Stage, second.Stage));
        }
        finally
        {
            await using var s = store.LightweightSession(slug);
            foreach (var d in await s.Query<Deal>().ToListAsync()) s.Delete(d);
            await s.SaveChangesAsync();
        }
    }
}
