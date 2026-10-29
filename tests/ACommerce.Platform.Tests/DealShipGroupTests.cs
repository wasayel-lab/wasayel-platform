using System.Text.Json;
using System.Text.RegularExpressions;
using ACommerce.Kit.Delivery;
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

    private sealed class ForbiddenPaymentProvider : ACommerce.Kit.Payments.IPaymentProvider
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

    /// <summary><b>اليَوم</b>: صَفقَتا نَقدٍ لِمُشتَرٍ واحِدٍ مِن بائِعٍ واحِدٍ — «أَكمِل الشَّحن» عَلى الأولى يُنشِئُ طَلَبَها وَحدَها بِمُعَرِّفِها ومَبلَغِها، والثانِيَةُ
    /// عِندَ «تأكيد» بِلا طَلَب؛ ثُمَّ عَلى الثانِيَةِ طَلَبٌ ثانٍ بِمُعَرِّفِها. طَلَبانِ وبوليصَتانِ وتَحصيلانِ عِندَ الاستِلام.</summary>
    [Fact]
    public async Task Today_two_cash_deals_of_one_vendor_and_one_buyer_ship_as_two_oto_orders_each_by_its_own_id()
    {
        var w = await TwoConfirmedCashDeals();

        var first = await w.Shipping.ShipAsync(Slug, w.First, DealShipTests.Vendor, "Riyadh");
        // قيمَتانِ لا كائِن: عالَمُ الوَثائِقِ يُعيدُ الكائِنَ المَخزونَ نَفسَه فَيَتَبَدَّلُ بِالشَحنِ التالي.
        var secondBefore = (await w.Deals.LoadAsync(Slug, w.Second))!;
        var (secondStage, secondPlaced) = (secondBefore.Stage, DealShipping.IsPlaced(secondBefore));
        var second = await w.Shipping.ShipAsync(Slug, w.Second, DealShipTests.Vendor, "Riyadh");

        var orders = Orders(w.Oto);
        output.WriteLine($"طَلَباتُ OTO: {orders.Length} — {string.Join(" · ", orders.Select(o => $"{o.OrderId[..8]}… {o.Amount}/{o.AmountDue} «{o.Description}»"))}");
        Assert.True(first.Ok, first.Code);
        Assert.Equal(DealStage.Confirmed, secondStage);
        Assert.False(secondPlaced);
        Assert.True(second.Ok, second.Code);
        Assert.Equal(2, orders.Length);
        Assert.Equal((w.First.ToString("N"), 100m, 100m, "سِلعَة 1"), orders[0]);
        Assert.Equal((w.Second.ToString("N"), 150m, 150m, "سِلعَة 2"), orders[1]);
        Assert.Equal(w.First.ToString("N"), (await w.Deals.LoadAsync(Slug, w.First))!.Refs[DealShipping.OrderRef]);
        Assert.Equal(w.Second.ToString("N"), (await w.Deals.LoadAsync(Slug, w.Second))!.Refs[DealShipping.OrderRef]);
    }

    /// <summary><b>اليَوم</b>: الوِلادَةُ في <c>/checkout/submit</c> تَكتُبُ أَربَعَ خَصائِصَ لا غَير (الكَمِّيَّةُ والعُنوانُ والهاتِفُ وطَريقَةُ الدَفع) — فَلا رابِطَ بَينَ
    /// صَفَقاتِ بائِعٍ واحِدٍ مِن السَلَّةِ نَفسِها؛ والإفصاحُ «كُلُّ مُنتَجٍ … يُشحَنُ وَحدَه».</summary>
    [Fact]
    public void Today_the_checkout_births_each_item_with_four_attributes_and_nothing_links_a_vendors_deals()
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
        Assert.Contains("يُشحَنُ وَحدَه", CheckoutCardInvoiceDisclosureTests.PerItemNote, StringComparison.Ordinal);
    }
}
