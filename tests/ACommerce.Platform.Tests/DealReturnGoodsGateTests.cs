using ACommerce.Kit.Tenants;
using ACommerce.Templates.Customer.Marketplace;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ «طَلَب استِرجاع» لِبَيعِ السِلَعِ وَحدَه — عَلى أَيِّ مُمَيِّزٍ يَقِف؟ (‏2026-10-30، تَدقيقُ الموجَة ١١ E-deals-1) ════════════════════════════════════
//
// **الحالُ المَقيسَة**: `DealReturnRequest.Validate` يَرُدُّ ما نَمَطُه غَيرُ `marketplace` — و`marketplace` هُوَ احتِياطُ `RoleDealPatternAffinity.Resolve`: لا
// يُعطي غَيرَه إلّا driver/mover/rider («trip») وhost («rental»). فَكُلُّ مَتجَرٍ سِواها — مَتاجِرُ صِفرِ الأَدوار، وكُلُّ مَتجَرِ «إعلاناتٍ» يَبنيه الاستوديو
// (customer + vendor + tenant_admin)، وفيها الخِدماتُ والعَقار — يَلِدُ صَفَقاتِه «marketplace»؛ فَصَفقَةُ عَرضٍ عَلى «شقة» تَبلُغُ «تَسليم» ويُرسَمُ لَها
// «استِرجاعُ السِّلعَة». وسَوالِبُ `DealReturnRequestTests` أَنماطٌ لا تَحمِلُها صَفقَةٌ حَيَّة (القاعِدَة ١٠).

public class DealReturnGoodsGateTests(ITestOutputHelper output)
{
    private static readonly Guid Buyer = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
    private static readonly DealReturnRequester AsBuyer = new(Buyer, "مُشتَرٍ");
    private static readonly DateTime DeliveredOn = new(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>صَفقَةٌ سُلِّمَت بِنَمَطِ ما يَلِدُه بابُها عَلى مَتجَرٍ بِهذِه الأَدوار (<see cref="MarketplaceTemplateExtensions.PatternFromTenant"/>) — وبِلا
    /// <c>pay_method</c> حينَ يَلِدُها العَرضُ (<paramref name="cartBorn"/> <c>false</c>)، أَو بِه حينَ تَلِدُها السَلَّة.</summary>
    internal static Deal Delivered(Tenant store, bool cartBorn, string title = "شقة")
    {
        var deal = new Deal
        {
            Id = Guid.NewGuid(), TenantSlug = store.Id, Pattern = MarketplaceTemplateExtensions.PatternFromTenant(store), Stage = DealStage.Delivered,
            Status = DealStatus.Active, AmountSar = 1200m, ListingTitle = title, InitiatorId = Buyer, InitiatorName = "مُشتَرٍ",
            CounterpartyId = Guid.NewGuid(), CounterpartyName = "مالِك",
        };
        if (cartBorn) deal.Attributes[CheckoutPayMethods.AttributeKey] = CheckoutPayMethods.CashOnDelivery;
        deal.Timeline.Add(new(DealStage.Offered, DealStage.Offered, "created", Buyer, "مُشتَرٍ", null, DeliveredOn.AddDays(-3)));
        deal.Timeline.Add(new(DealStage.Shipping, DealStage.Delivered, "advanced", deal.CounterpartyId, "مالِك", null, DeliveredOn));
        return deal;
    }

    internal static Tenant ZeroRoleStore() => new() { Id = "ejar-like", Name = "مَتجَرُ إيجار" };

    internal static Tenant StudioClassifiedsStore() => new()
    {
        Id = "classifieds-like", Name = "إعلانات",
        Roles = new() { new() { CatalogSlug = "customer" }, new() { CatalogSlug = "vendor" }, new() { CatalogSlug = "tenant_admin" } },
    };

    /// <summary><b>قُلِبَ بِاسمِه</b> (تَوصيفُه <c>d2fed52</c> — كانَ <c>Today_an_offer_path_deal_on_a_zero_role_or_studio_classifieds_store_may_request_a_goods_return</c>:
    /// يُقبَل): صَفقَةُ عَرضٍ (بِلا <c>pay_method</c>) عَلى «شقة» في مَتجَرِ صِفرِ أَدوارٍ وفي مَتجَرِ إعلاناتٍ مِن الاستوديو — نَمَطُها <c>marketplace</c> كَما كان، و«طَلَب استِرجاع»
    /// <b>يُرَدُّ</b> <c>pattern_not_goods</c> (السالِبان).</summary>
    [Fact]
    public void An_offer_path_deal_on_a_zero_role_or_studio_classifieds_store_is_not_a_goods_sale()
    {
        foreach (var store in new[] { ZeroRoleStore(), StudioClassifiedsStore() })
        {
            var deal = Delivered(store, cartBorn: false);
            var verdict = DealReturnRequest.Validate(deal, AsBuyer, DeliveredOn.AddDays(1));
            output.WriteLine($"{store.Id}: النَمَط {deal.Pattern} · pay_method؟ {deal.Attributes.ContainsKey(CheckoutPayMethods.AttributeKey)} · الرَدّ: {verdict?.Code ?? "يُقبَل"}");
            Assert.Equal(DealReturnRequest.GoodsPattern, deal.Pattern);
            Assert.Equal(DealReturnRequest.PatternNotGoods, verdict?.Code);
            Assert.False(DealReturnRequest.CartBorn(deal));
        }
    }

    /// <summary><b>المُوجِب</b>: صَفقَةُ سِلعَةٍ وَلَدَتها السَلَّةُ (‏<c>pay_method</c> نَقداً أَو بِطاقَة) في مَتجَرِ «بَيع» — يُقبَلُ طَلَبُها بَعدَ التَسليم؛ و<c>pay_method</c> فارِغٌ
    /// لا يُعَدُّ سَلَّة.</summary>
    [Theory]
    [InlineData("cash")]
    [InlineData("card")]
    public void A_cart_born_goods_deal_may_request_a_return(string method)
    {
        var deal = Delivered(ZeroRoleStore(), cartBorn: true, title: "قَميص");
        deal.Attributes[CheckoutPayMethods.AttributeKey] = method;
        Assert.True(DealReturnRequest.CartBorn(deal));
        Assert.Null(DealReturnRequest.Validate(deal, AsBuyer, DeliveredOn.AddDays(1)));
        deal.Attributes[CheckoutPayMethods.AttributeKey] = " ";
        Assert.Equal(DealReturnRequest.PatternNotGoods, DealReturnRequest.Validate(deal, AsBuyer, DeliveredOn.AddDays(1))?.Code);
    }
}
