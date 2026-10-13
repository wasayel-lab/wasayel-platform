using ACommerce.Kit.Roles;
using ACommerce.Kit.Tenants;
using ACommerce.Templates.Customer.Marketplace;
using ACommerce.Templates.Customer.Marketplace.Services;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using Xunit;
using Xunit.Abstractions;
using static ACommerce.Platform.Tests.TenantGatewayEndpointsTests;

namespace ACommerce.Platform.Tests;

// ═══ بِطاقَةُ البَوّابَةِ في مَتجَرٍ نَمَطُ صَفَقاتِه بِلا «دَفع» — تَوصيفُ «اليَوم» ═════════════════════════════════════════════
//
// مُراجَعَةُ بَوّابَةِ المَوجَة 3b الثانِيَة (‏2026-10-13، G5 (xvii-g) — خَطَرُ مال): `PatternFromTenant` يُعيدُ «trip» لِمَتجَرٍ فيه دَورٌ
// انجِذابُه «trip» (سائِق، ناقِل، راكِب)، ونَمَطُ «trip» بِلا مَرحَلَةِ «دَفع» (`Offered → Booked → Confirmed → Delivered → Reviewed`)؛ و`/checkout/submit`
// لا يَسأَلُ النَمَطَ قَبلَ أَن يَختارَ طَريقَ البَوّابَة — فَتولَدُ صَفقَةُ بِطاقَةٍ عَلى فاتورَة، وحارِسُ «دَفع» (`AwaitsPayment`، تاليها «دَفع») لا يَصدُقُ
// أَبَداً، فَتُسَلَّمُ وتُقَيَّمُ وفاتورَتُها لَم تُدفَع.
//
// **ما يُقلَبُ بِاسمِه في كوميتِ الإصلاح**: صَفُّ «Today_» أَدناه. وصَفُّ النَمَطِ مِن الدَورِ ثابِتٌ بِحَرفِه.
//
// **قُلِبَ** (‏2026-10-13، كوميتُ «fix(payments)» بَعدَ ‏0473a70): `CheckoutCardFacts.ForPattern` تَرُدُّ بِطاقَةَ البَوّابَةِ بِـ`pay_card_pattern_unpaid`
// لِنَمَطٍ بِلا «دَفع»، والطَرَفانِ يُمَرِّرانِ `PatternFromTenant` إلى `CardFactsAsync` — فَلا تولَدُ صَفقَةُ بِطاقَةٍ عَلى فاتورَةٍ في مَتجَرِ «trip».

public class GatewayPatternWithoutPaidTests(ITestOutputHelper output)
{
    private static readonly CheckoutCardFacts Gateway = new(StoreCollects: true, SealedKeyBound: true, VaultCanUnseal: true,
        GatewayClientRegistered: true, InstanceCanCharge: false);

    private static Tenant WithRoles(params string[] roles)
        => new() { Id = Slug, Roles = roles.Select(r => new Role { Slug = r, CatalogSlug = r }).ToList() };

    [Fact]
    public void A_store_with_a_trip_affinity_role_deals_in_the_trip_pattern_which_has_no_Paid_stage()
    {
        foreach (var role in new[] { "driver", "mover", "rider" })
            Assert.Equal("trip", MarketplaceTemplateExtensions.PatternFromTenant(WithRoles(role)));
        Assert.False(DealPatternCatalog.For("trip").Includes(DealStage.Paid));
        Assert.True(DealPatternCatalog.For(MarketplaceTemplateExtensions.PatternFromTenant(null)).Includes(DealStage.Paid));
        output.WriteLine($"trip: {string.Join(" → ", DealPatternCatalog.For("trip").StageOrder)}");
    }

    /// <summary><b>قُلِبَ</b> (‏2026-10-13) — كانَ <c>Today_a_gateway_card_deal_in_the_trip_pattern_is_delivered_and_reviewed_with_its_invoice_unpaid</c>
    /// (‏<c>0473a70</c>): بِطاقَةُ البَوّابَةِ لا تُعرَضُ لِنَمَطٍ بِلا «دَفع»، وطَلَبُها يُرَدُّ نَقداً بِرَمزِه — قَبلَ أَن تولَدَ صَفقَة.</summary>
    [Theory]
    [InlineData("trip")]
    [InlineData("classifieds")]
    [InlineData("default")]
    public void A_gateway_card_is_refused_for_a_pattern_without_a_Paid_stage(string pattern)
    {
        var f = Gateway.ForPattern(pattern);
        var decided = CheckoutPaymentPolicy.Decide("card", f.Collects, f.ChargerCanCharge);
        output.WriteLine($"{pattern} ⇒ البِطاقَة: {f.CardOffered} · الرَمز: {f.GatewayRefusal} · Decide: {decided}");
        Assert.False(f.CardOffered);
        Assert.Equal(CheckoutPayMethods.CardPatternUnpaid, f.PatternRefusal);
        Assert.Equal(CheckoutPayMethods.CardPatternUnpaid, f.GatewayRefusal);
        Assert.Equal(CheckoutPayMethods.CashOnDelivery, decided.Method);
        Assert.Equal((CheckoutPayMethods.CashOnDelivery, (string?)null), CheckoutPaymentPolicy.Decide("cash", f.Collects, f.ChargerCanCharge));
    }

    /// <summary>المُوجِب: نَمَطٌ بِـ«دَفع» — والنَمَطُ يَغلِبُ السَلَّةَ في الرَمز (مَتجَرٌ بِلا «دَفع» يَرُدُّ كُلَّ سَلَّة)، ونَمَطٌ غائِبٌ لا يُسأَل، وبِلا بَوّابَةٍ لا يُسأَلُ النَمَط.</summary>
    [Fact]
    public void A_pattern_with_Paid_keeps_the_card_and_the_pattern_code_wins_over_the_cart()
    {
        foreach (var pattern in new[] { "marketplace", "rental", "service" })
        {
            var f = Gateway.ForPattern(pattern);
            Assert.True(f.CardOffered, pattern);
            Assert.Null(f.GatewayRefusal);
        }
        Assert.Equal(Gateway, Gateway.ForPattern(null));

        var many = new ACommerce.Kit.Cart.Cart();
        for (var i = 0; i < 31; i++) many.Items.Add(new() { ListingId = Guid.NewGuid(), UnitPriceSar = 1m });
        var both = Gateway.ForCart(many).ForPattern("trip");
        Assert.Equal(CheckoutPayMethods.CardCartTooMany, both.CartRefusal);
        Assert.Equal(CheckoutPayMethods.CardPatternUnpaid, both.GatewayRefusal);

        var instance = new CheckoutCardFacts(StoreCollects: true, SealedKeyBound: false, VaultCanUnseal: true, GatewayClientRegistered: true, InstanceCanCharge: true)
            .ForPattern("trip");
        Assert.Null(instance.PatternRefusal);
        Assert.True(instance.CardOffered);
    }

    /// <summary>الخِدمَةُ تَقرَأُ النَمَطَ الَّذي يُمَرِّرُه الطَرَفان — مَتجَرٌ بِسائِقٍ ⇒ الرَمز، وبِلا نَمَطٍ ⇒ كَما كانَت؛ والبابانِ يُمَرِّرانِ <c>PatternFromTenant</c>
    /// قَبلَ القَرارِ والنَصُّ حَيّ.</summary>
    [Fact]
    public async Task The_service_and_both_readers_ask_the_stores_pattern_before_any_deal_is_born()
    {
        var w = await PaidCart();
        var trip = await w.Gateway.CardFactsAsync(Slug, storeCollects: false, instanceCanCharge: false, null,
            MarketplaceTemplateExtensions.PatternFromTenant(WithRoles("driver")));
        var plain = await w.Gateway.CardFactsAsync(Slug, storeCollects: false, instanceCanCharge: false);
        output.WriteLine($"سائِق: {trip.GatewayRefusal} · بِلا نَمَط: {plain.GatewayRefusal ?? "لا رَمز"}");
        Assert.Equal(CheckoutPayMethods.CardPatternUnpaid, trip.GatewayRefusal);
        Assert.Null(plain.GatewayRefusal);

        string Code(params string[] parts) => WriteEndpointGuardTests.StripComments(WriteEndpointGuardTests.StripMarkupComments(File.ReadAllText(
            Path.Combine(new[] { ThemeZeroEquivalenceTests.RepoRoot, "libs", "templates", "ACommerce.Templates.Customer.Marketplace" }.Concat(parts).ToArray()))));
        var host = Code("MarketplaceTemplateExtensions.cs");
        var pattern = host.IndexOf("var pattern = PatternFromTenant(tenant);", StringComparison.Ordinal);
        var facts = host.IndexOf("var card = await gateway.CardFactsAsync(slug,", StringComparison.Ordinal);
        var birth = host.IndexOf("var deal = await deals.StartAsync(slug, pattern,", StringComparison.Ordinal);
        Assert.True(pattern > 0 && facts > pattern && birth > facts, $"pattern={pattern} facts={facts} birth={birth}");
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(host, System.Text.RegularExpressions.Regex.Escape("var pattern = PatternFromTenant(tenant);")));
        Assert.Contains("cart, pattern);", host[facts..birth], StringComparison.Ordinal);

        var page = Code("Components", "Pages", "CheckoutPage.razor");
        Assert.Contains("MarketplaceTemplateExtensions.PatternFromTenant(tenant));", page, StringComparison.Ordinal);
        Assert.Contains("CheckoutPayMethods.CardPatternUnpaid => L[\"checkout.payment.err_card_pattern_unpaid\"],", page, StringComparison.Ordinal);
        var text = ACommerce.Platform.I18n.LocaleCatalog.Text(ACommerce.Platform.I18n.LocaleCatalog.Arabic, "checkout.payment.err_card_pattern_unpaid");
        Assert.NotEqual("checkout.payment.err_card_pattern_unpaid", text);
        Assert.False(ACommerce.Platform.I18n.LocaleCatalog.IsPlaceholder(text));
    }
}
