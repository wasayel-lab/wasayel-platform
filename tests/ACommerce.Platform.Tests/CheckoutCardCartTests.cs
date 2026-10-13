using System.Net;
using ACommerce.Kit.Payments;
using ACommerce.Platform.I18n;
using ACommerce.Templates.Customer.Marketplace.Services;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.Abstractions;
using static ACommerce.Platform.Tests.TenantGatewayEndpointsTests;

namespace ACommerce.Platform.Tests;

// ═══ بِطاقَةُ بَوّابَةِ المَتجَرِ وسَلَّةٌ لا تَحمِلُها فاتورَةٌ واحِدَة — تَوصيفُ «اليَوم» ═══════════════════════════════════════
//
// مُراجَعَةُ بَوّابَةِ المَوجَة 3b (‏2026-10-13، G5 (xvi-g)): `CheckoutCardFacts` لا تَسأَلُ عَن السَلَّة — والفاتورَةُ الواحِدَةُ تَحمِلُ مِن صَفقَةٍ
// إلى ‏30 (`GatewayPaymentProof.FitsOneInvoice`، حَدُّ مَفاتيحِ `metadata` في الوَثيقَة) ومَبلَغاً بِهَلَلاتٍ صَحيحَةٍ لا يَقِلُّ عَن ‏100
// (`MoyasarClient.ToSmallestUnit`). فَتُرسَمُ البِطاقَةُ لِسَلَّةٍ لا تُفَوتَرُ أَبَداً، وكُلُّ إرسالٍ يُولِدُ صَفَقاتٍ مَربوطَةً بِبائِعيها ثُمَّ
// يُلغيها `OpenInvoiceAsync` بِـ`gateway_invalid` — وبائِعوها لا يُخبَرونَ بِوِلادَةٍ ولا إلغاء.
//
// **ما يُقلَبُ بِاسمِه في كوميتِ الإصلاح**: صَفُّ «Today_» أَدناه. وصَفُّ الخِدمَةِ يَبقى بِحَرفِه: دِفاعُها الأَخيرُ قائِم.
//
// **قُلِبَ** (‏2026-10-13، كوميتُ «fix(payments)» بَعدَ ‏a135d25): `CheckoutCardFacts.ForCart` تَحسُبُ مِن السَلَّةِ رَمزَ ما يَمنَعُها — `pay_card_cart_too_many`
// أَو `pay_card_amount_unbillable` — لِطَريقِ البَوّابَةِ وَحدَه، و`ChargerCanCharge` تَسأَلُه فَتَرُدُّ `Decide` القائِمَةُ البِطاقَة؛ والطَرَفانِ
// يُمَرِّرانِ السَلَّةَ إلى `CardFactsAsync` نَفسِها؛ والنُقطَةُ تَرُدُّ بِرَمزِ السَلَّة (`card.CartRefusal ?? payRefusal`). فَلا تُرسَمُ البِطاقَةُ ويُقالُ لِماذا،
// ولا تولَدُ صَفقَةٌ لِإرسالٍ مَصنوع.

public class CheckoutCardCartTests(ITestOutputHelper output)
{
    private const string ReturnUrl = "https://store.example/zz-gateway/pay/return?deal=1";
    private static readonly CheckoutCardFacts Gateway = new(StoreCollects: true, SealedKeyBound: true, VaultCanUnseal: true,
        GatewayClientRegistered: true, InstanceCanCharge: false);

    private static ACommerce.Kit.Cart.Cart CartOf(int lines, decimal each)
    {
        var cart = new ACommerce.Kit.Cart.Cart();
        for (var i = 0; i < lines; i++) cart.Items.Add(new() { ListingId = Guid.NewGuid(), Title = "سِلعَة", UnitPriceSar = each, Quantity = 1 });
        return cart;
    }

    /// <summary><b>قُلِبَ</b> (‏2026-10-13) — كانَ <c>Today_a_gateway_card_is_offered_whatever_the_carts_size_or_total</c> (‏<c>a135d25</c>): سَلَّةٌ لا تَحمِلُها
    /// فاتورَةٌ واحِدَةٌ لا تُعرَضُ لَها بِطاقَةُ البَوّابَة، وطَلَبُها يُرَدُّ نَقداً بِرَمزِها هِيَ — لا بِرَمزِ «لا يَقبِضُ المَتجَر».</summary>
    [Theory]
    [InlineData(31, "1.00", CheckoutPayMethods.CardCartTooMany)]
    [InlineData(2, "0.25", CheckoutPayMethods.CardAmountUnbillable)]
    [InlineData(2, "5.0025", CheckoutPayMethods.CardAmountUnbillable)]
    [InlineData(1, "0.99", CheckoutPayMethods.CardAmountUnbillable)]
    public void A_gateway_card_is_refused_with_its_own_code_for_a_cart_one_invoice_cannot_carry(int lines, string each, string code)
    {
        var f = Gateway.ForCart(CartOf(lines, decimal.Parse(each, System.Globalization.CultureInfo.InvariantCulture)));
        var decided = CheckoutPaymentPolicy.Decide("card", f.Collects, f.ChargerCanCharge);
        output.WriteLine($"{lines} × {each} ⇒ البِطاقَة: {f.CardOffered} · الرَمز: {f.CartRefusal} · Decide: {decided}");
        Assert.False(f.CardOffered);
        Assert.Equal(code, f.CartRefusal);
        Assert.Equal((CheckoutPayMethods.CashOnDelivery, CheckoutPayMethods.CardUnavailable), decided);   // ‏تُرَدُّ — والنَصُّ بِرَمزِ السَلَّة (`card.CartRefusal ?? payRefusal`)
        Assert.Equal((CheckoutPayMethods.CashOnDelivery, (string?)null), CheckoutPaymentPolicy.Decide("cash", f.Collects, f.ChargerCanCharge));   // ‏النَقدُ لا يُرَدّ
    }

    /// <summary>المُوجِب: ما تَحمِلُه فاتورَةٌ واحِدَةٌ — ‏30 بَنداً، ورِيالٌ واحِدٌ بِالضَبط (‏100 هَلَلَة) — تُعرَضُ لَه البِطاقَةُ كَما كانَت؛ وسَلَّةٌ فارِغَةٌ أَو
    /// غائِبَةٌ لا تُسأَل.</summary>
    [Theory]
    [InlineData(30, "1.00")]
    [InlineData(1, "1.00")]
    [InlineData(3, "33.33")]
    public void A_cart_one_invoice_carries_keeps_the_gateway_card(int lines, string each)
    {
        var f = Gateway.ForCart(CartOf(lines, decimal.Parse(each, System.Globalization.CultureInfo.InvariantCulture)));
        Assert.True(f.CardOffered);
        Assert.Null(f.CartRefusal);
        Assert.Equal((CheckoutPayMethods.Card, (string?)null), CheckoutPaymentPolicy.Decide("card", f.Collects, f.ChargerCanCharge));
        Assert.Equal(Gateway, Gateway.ForCart(null));
        Assert.Equal(Gateway, Gateway.ForCart(new ACommerce.Kit.Cart.Cart()));
    }

    /// <summary>السالِبُ لِلطَريق: بِلا مِفتاحٍ مَختومٍ (حَجزُ مُزَوِّدِ النُسخَةِ لِكُلِّ صَفقَةٍ وَحدَها) لا حَدَّ فاتورَة — السَلَّةُ نَفسُها تُعرَضُ لَها البِطاقَةُ
    /// كَما كانَت، ولا رَمز.</summary>
    [Fact]
    public void Without_the_gateway_the_cart_is_never_asked()
    {
        var instance = new CheckoutCardFacts(StoreCollects: true, SealedKeyBound: false, VaultCanUnseal: true, GatewayClientRegistered: true, InstanceCanCharge: true);
        var f = instance.ForCart(CartOf(31, 0.25m));
        Assert.Null(f.CartRefusal);
        Assert.True(f.CardOffered);
        Assert.Equal((CheckoutPayMethods.Card, (string?)null), CheckoutPaymentPolicy.Decide("card", f.Collects, f.ChargerCanCharge));
    }

    /// <summary>الخِدمَةُ تَقرَأُ السَلَّةَ الَّتي يُمَرِّرُها الطَرَفان: رَبطٌ مَختومٌ وخِزانَةٌ وعَميلٌ — وسَلَّةٌ مِن ‏31 بَنداً ⇒ الرَمز؛ وبِلا سَلَّةٍ ⇒ كَما كانَت.</summary>
    [Fact]
    public async Task The_service_reads_the_cart_both_readers_pass()
    {
        var w = await PaidCart();
        var withCart = await w.Gateway.CardFactsAsync(Slug, storeCollects: false, instanceCanCharge: false, CartOf(31, 1m));
        var without = await w.Gateway.CardFactsAsync(Slug, storeCollects: false, instanceCanCharge: false);
        output.WriteLine($"بِسَلَّةِ 31: {withCart.CartRefusal} · بِلا سَلَّة: {without.CartRefusal ?? "لا رَمز"} · البِطاقَة: {without.CardOffered}");
        Assert.Equal(CheckoutPayMethods.CardCartTooMany, withCart.CartRefusal);
        Assert.False(withCart.CardOffered);
        Assert.Null(without.CartRefusal);
        Assert.True(without.CardOffered);
    }

    /// <summary>البابانِ يُمَرِّرانِ السَلَّةَ ويُقَرِّرانِ بِالحَقائِقِ نَفسِها، والصَفحَةُ تَقولُ الرَمزَ حَيثُ كانَت البِطاقَة، والنَصّانِ حَيّانِ (والحَدُّ مِن الثابِت).</summary>
    [Fact]
    public void Both_readers_pass_the_cart_and_the_page_says_why_the_card_is_gone()
    {
        string Code(params string[] parts) => WriteEndpointGuardTests.StripComments(WriteEndpointGuardTests.StripMarkupComments(File.ReadAllText(
            Path.Combine(new[] { ThemeZeroEquivalenceTests.RepoRoot, "libs", "templates", "ACommerce.Templates.Customer.Marketplace" }.Concat(parts).ToArray()))));
        var host = Code("MarketplaceTemplateExtensions.cs");
        Assert.Contains("ACommerce.Kit.Payments.PaymentChargeSurface.CanCharge(payments), cart);", host, StringComparison.Ordinal);
        Assert.Contains(".RefusalPath(card.CartRefusal ?? payRefusal, name, phone, addr)));", host, StringComparison.Ordinal);
        var cartLoad = host.IndexOf("var cart = await s.LoadAsync<ACommerce.Kit.Cart.Cart>(userId);", StringComparison.Ordinal);
        var facts = host.IndexOf("var card = await gateway.CardFactsAsync(slug,", StringComparison.Ordinal);
        var birth = host.IndexOf("var deal = await deals.StartAsync(slug, pattern,", StringComparison.Ordinal);
        Assert.True(cartLoad > 0 && facts > cartLoad && birth > facts, $"cart={cartLoad} facts={facts} birth={birth}");

        var page = Code("Components", "Pages", "CheckoutPage.razor");
        Assert.Contains("PaymentChargeSurface.CanCharge(Payments), cart);", page, StringComparison.Ordinal);
        Assert.Contains("else if (CartRefusalText(Facts.CartRefusal) is { } why)", page, StringComparison.Ordinal);
        Assert.Contains("CheckoutPayMethods.CardCartTooMany or CheckoutPayMethods.CardAmountUnbillable => CartRefusalText(Err),", page, StringComparison.Ordinal);

        var many = LocaleCatalog.Text(LocaleCatalog.Arabic, "checkout.payment.err_card_cart_too_many");
        var amount = LocaleCatalog.Text(LocaleCatalog.Arabic, "checkout.payment.err_card_amount_unbillable");
        foreach (var (key, text) in new[] { ("checkout.payment.err_card_cart_too_many", many), ("checkout.payment.err_card_amount_unbillable", amount) })
        {
            Assert.NotEqual(key, text);
            Assert.False(LocaleCatalog.IsPlaceholder(text));
        }
        Assert.Contains("{0}", many, StringComparison.Ordinal);
        Assert.DoesNotContain("30", many, StringComparison.Ordinal);   // ‏الحَدُّ مِن الثابِتِ لا مِن النَصّ
        output.WriteLine($"{string.Format(many, GatewayPaymentProof.MaxDealsPerInvoice)}\n{amount}");
    }

    /// <summary><b>دِفاعُ الخِدمَةِ الأَخيرُ</b> — ثابِتٌ قَبلَ الإصلاحِ وبَعدَه: سَلَّةٌ أَطوَلُ مِن فاتورَةٍ واحِدَةٍ أَو بِمَجموعٍ لا تَقبَلُه البَوّابَةُ لا تَبلُغُ
    /// البَوّابَةَ بِنِداء، وتُلغى صَفَقاتُها بِـ<see cref="MoyasarFailures.Invalid"/> ولا يُخصَمُ شَيء.</summary>
    [Theory]
    [InlineData(31, 1.00)]
    [InlineData(2, 0.25)]
    [InlineData(2, 5.0025)]
    public async Task The_invoice_opener_refuses_a_cart_one_invoice_cannot_carry_and_cancels_what_was_born(int count, double each)
    {
        var docs = new DocWorld();
        var platform = new DealMoneyClaimFirstTests.RecordingPaymentProvider();
        var deals = new DealsService(docs.Store, platform);
        var ids = new List<Guid>();
        for (var i = 0; i < count; i++)
            ids.Add((await deals.StartAsync(Slug, "marketplace", Buyer, "مُشتَرٍ", null, "سِلعَة", (decimal)each,
                GatewayPayments.MarkedAtBirth(new() { [CheckoutPayMethods.AttributeKey] = CheckoutPayMethods.Card }, viaGateway: true))).Id);
        var vault = NewVault();
        docs.Put(PaymentsBinding(vault));
        var moyasar = new MoyasarScriptedHandler();
        var gateway = new GatewayPayments(deals, new TenantProviderService(docs.Store), vault,
            new MoyasarClient(new HttpClient(moyasar), NullLogger<MoyasarClient>.Instance));

        var opened = await gateway.OpenInvoiceAsync(Slug, new DealPayer(Buyer), ids, "مَتجَر", ReturnUrl, new L(new HttpContextAccessor()));
        var cancelled = ids.Count(id => docs.Read<Deal>(id.ToString())!.Status == DealStatus.Cancelled);
        output.WriteLine($"{count} × {each} ⇒ الفَتح: {opened.Code} · مُلغاة: {cancelled}/{count} · نِداءاتُ البَوّابَة: {moyasar.Requests.Count}");

        Assert.Equal(MoyasarFailures.Invalid, opened.Code);
        Assert.Equal(count, cancelled);
        Assert.Empty(moyasar.Requests);
        Assert.Empty(platform.Calls);
    }
}
