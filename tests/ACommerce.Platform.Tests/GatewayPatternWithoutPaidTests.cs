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

    [Fact]
    public async Task Today_a_gateway_card_deal_in_the_trip_pattern_is_delivered_and_reviewed_with_its_invoice_unpaid()
    {
        Assert.True(Gateway.CardOffered);   // ← البِطاقَةُ لا تَسأَلُ النَمَط

        var w = await PaidCart(withSeller: true, invoice: true, bornForGateway: true);
        var deal = await w.Deals.StartAsync(Slug, "trip", Buyer, "مُشتَرٍ", null, "رِحلَة", 100m,
            GatewayPayments.MarkedAtBirth(new() { [CheckoutPayMethods.AttributeKey] = CheckoutPayMethods.Card }, viaGateway: true));
        await w.Deals.AttachRefAsync(Slug, deal.Id, GatewayPaymentProof.InvoiceRef, InvoiceId);
        await w.Deals.AttachRefAsync(Slug, deal.Id, DealsService.ListingOwnerRef, Seller.ToString());
        await w.Deals.BindListingOwnerAsync(Slug, deal.Id, Seller.ToString());

        var walked = new List<string>();
        foreach (var (actor, name) in new[] { (Seller, "بائِع"), (Buyer, "مُشتَرٍ"), (Seller, "بائِع"), (Buyer, "مُشتَرٍ") })
        {
            var stored = w.Docs.Read<Deal>(deal.Id.ToString())!;
            var (from, awaits) = (stored.Stage, GatewayPayments.AwaitsPayment(stored));
            var res = await w.Deals.AdvanceAsync(Slug, deal.Id, actor, name);
            walked.Add($"{from}→{w.Docs.Read<Deal>(deal.Id.ToString())!.Stage} (AwaitsPayment={awaits}, ok={res.Ok})");
        }
        var end = w.Docs.Read<Deal>(deal.Id.ToString())!;
        output.WriteLine(string.Join(" · ", walked) + $" · نِداءاتُ الدَفع: {w.Platform.Calls.Count} · payment_id: {end.Refs.ContainsKey("payment_id")}");

        Assert.Equal(DealStage.Reviewed, end.Stage);
        Assert.False(end.Refs.ContainsKey("payment_id"));
        Assert.Empty(w.Platform.Calls);
        Assert.True(GatewayPayments.HasInvoice(end));
    }
}
