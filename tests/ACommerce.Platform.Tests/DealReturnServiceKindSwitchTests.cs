using ACommerce.Kit.Tenants;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using ACommerce.Templates.Customer.Marketplace.Services.Patterns;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ «طَلَب استِرجاع» لا يُرسَمُ عَلى صَفقَةِ خِدمَةٍ أَو فَعّالِيَّةٍ وَلَدَتها السَلَّة (‏2026-11-05، الموجَة ١٢ G3 · ٨ — D-6؛ تَوصيفُه `1ae136e`) ═══
//
// **القَرار** (د30 «في متاجر بيع السلع»؛ ‏ADR-047 §5): `/checkout/submit` يَكتُبُ عَلى الصَفقَةِ عِندَ وِلادَتِها تَجميعَ فِئَةِ إعلانِها (`DealReturnRequest.CategoryKindOf` —
// فِئَتُه أَو أَقرَبُ أَبٍ لَه تَجميع) في `category_kind`، وبَوّابَةُ «طَلَب استِرجاع» تَرُدُّ «خِدمات» و«فَعّالِيّات» بِـ`pattern_not_goods` نَفسِه — المَعجَمُ سِتَّةٌ كَما كان.
// وصَفقَةٌ وُلِدَت قَبلَ اليَوم (بِلا الخاصِّيَّة) عَلى ما كانَت. ومِلَفُّ التَوصيفِ `DealReturnServiceKindTests.cs` بِبَصمَتِه قَبلُ وبَعدُ.

public class DealReturnServiceKindSwitchTests(ITestOutputHelper output)
{
    private static readonly DateTime DeliveredOn = new(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DealReturnRequester AsBuyer = new(Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa"), "مُشتَرٍ");

    /// <summary>صَفقَةٌ وَلَدَتها السَلَّةُ عَلى إعلانٍ في <paramref name="category"/> — بِسِماتِ الوِلادَةِ كَما تَبنيها النُقطَةُ (<see cref="DealReturnRequest.KindAtBirth"/> عَلى
    /// <see cref="DealReturnRequest.CategoryKindOf"/>).</summary>
    private static Deal BornOn(string category)
    {
        var store = DealReturnServiceKindTests.SellStoreWithServices();
        var deal = DealReturnGoodsGateTests.Delivered(store, cartBorn: true, title: category);
        DealReturnRequest.KindAtBirth(deal.Attributes, DealReturnRequest.CategoryKindOf(store.Categories, category));
        return deal;
    }

    /// <summary><b>مُوجَبٌ وسالِبٌ لِلشَرطِ الجَديد</b>: «تَنظيف» (خِدمات) وفَرعُها بِلا تَجميعٍ و«حَفلَة» (فَعّالِيّات) ⇒ <c>pattern_not_goods</c>؛ و«مَلابِس» بِلا تَجميعٍ ⇒ يُقبَلُ
    /// بِلا خاصِّيَّة.</summary>
    [Theory]
    [InlineData("cleaning", CategoryKinds.Services, true)]
    [InlineData("deep-cleaning", CategoryKinds.Services, true)]
    [InlineData("concert", CategoryKinds.Events, true)]
    [InlineData("clothes", null, false)]
    public void A_cart_born_service_or_event_deal_is_not_a_goods_sale(string category, string? kind, bool refused)
    {
        var deal = BornOn(category);

        var verdict = DealReturnRequest.Validate(deal, AsBuyer, DeliveredOn.AddDays(1));

        output.WriteLine($"{category}: category_kind={deal.Attributes.GetValueOrDefault(DealReturnRequest.CategoryKindAttribute) ?? "—"} · الرَدّ: {verdict?.Code ?? "يُقبَل"}");
        Assert.Equal(kind, deal.Attributes.GetValueOrDefault(DealReturnRequest.CategoryKindAttribute));
        Assert.Equal(refused, DealReturnRequest.BornOnNonGoods(deal));
        Assert.Equal(refused ? DealReturnRequest.PatternNotGoods : null, verdict?.Code);
    }

    /// <summary><b>وتَجميعٌ غَيرُهُما سِلعَةٌ كَما كان، وصَفقَةٌ قَبلَ اليَومِ بِلا الخاصِّيَّة</b>: كُلُّ تَجميعٍ في المَعجَمِ عَدا «خِدمات» و«فَعّالِيّات» يُقبَلُ طَلَبُه — لا يُختَرَعُ تَصنيفٌ لَم يُسأَل
    /// عَنه — وبِلا الخاصِّيَّةِ يُقبَل.</summary>
    [Fact]
    public void Every_other_kind_and_a_deal_born_before_the_attribute_keep_todays_answer()
    {
        var checkedKinds = 0;
        foreach (var kind in CategoryKinds.All.Where(k => !DealReturnRequest.NotGoodsKinds.Contains(k)))
        {
            var deal = DealReturnGoodsGateTests.Delivered(DealReturnGoodsGateTests.ZeroRoleStore(), cartBorn: true, title: "x");
            DealReturnRequest.KindAtBirth(deal.Attributes, kind);
            Assert.Null(DealReturnRequest.Validate(deal, AsBuyer, DeliveredOn.AddDays(1)));
            checkedKinds++;
        }
        var before = DealReturnGoodsGateTests.Delivered(DealReturnGoodsGateTests.ZeroRoleStore(), cartBorn: true, title: "قَديمَة");
        output.WriteLine($"تَجميعاتٌ تُقبَل: {checkedKinds} مِن {CategoryKinds.All.Count} · صَفقَةٌ بِلا الخاصِّيَّة: {DealReturnRequest.Validate(before, AsBuyer, DeliveredOn.AddDays(1))?.Code ?? "يُقبَل"}");
        Assert.Equal(CategoryKinds.All.Count - 2, checkedKinds);
        Assert.Equal(new[] { CategoryKinds.Services, CategoryKinds.Events }, DealReturnRequest.NotGoodsKinds);
        Assert.Null(DealReturnRequest.Validate(before, AsBuyer, DeliveredOn.AddDays(1)));
        Assert.Equal(6, DealReturnRequest.All.Count);
    }

    /// <summary><b><see cref="DealReturnRequest.CategoryKindOf"/></b> — فِئَتُه، ثُمَّ أَقرَبُ أَبٍ لَه تَجميع، و<c>null</c> لِفِئَةٍ غائِبَةٍ أَو سِلسِلَةٍ بِلا تَجميعٍ أَو سلاجٍ فارِغٍ أَو فِئاتٍ غائِبَة؛
    /// وحَلقَةٌ في الشَجَرَةِ لا تَدور.</summary>
    [Fact]
    public void The_category_kind_is_the_listings_category_or_its_nearest_parent_with_one()
    {
        var cats = DealReturnServiceKindTests.SellStoreWithServices().Categories;
        cats.Add(new Category { Slug = "loop-a", Kind = "", ParentSlug = "loop-b" });
        cats.Add(new Category { Slug = "loop-b", Kind = "", ParentSlug = "loop-a" });

        Assert.Equal(CategoryKinds.Services, DealReturnRequest.CategoryKindOf(cats, "cleaning"));
        Assert.Equal(CategoryKinds.Services, DealReturnRequest.CategoryKindOf(cats, "deep-cleaning"));
        Assert.Equal(CategoryKinds.Events, DealReturnRequest.CategoryKindOf(cats, "concert"));
        Assert.Null(DealReturnRequest.CategoryKindOf(cats, "clothes"));
        Assert.Null(DealReturnRequest.CategoryKindOf(cats, "no-such-category"));
        Assert.Null(DealReturnRequest.CategoryKindOf(cats, "loop-a"));
        Assert.Null(DealReturnRequest.CategoryKindOf(cats, ""));
        Assert.Null(DealReturnRequest.CategoryKindOf(null, "cleaning"));
    }

    /// <summary><b>والنُقطَةُ تَكتُبُه</b>: جِسمُ <c>/checkout/submit</c> يَبني سِماتِ الوِلادَةِ بِـ<see cref="DealReturnRequest.KindAtBirth"/> عَلى فِئاتِ المَتجَرِ وسلاجِ الإعلان — مَرَّةً واحِدَة.</summary>
    [Fact]
    public void The_checkout_writes_the_listings_category_kind_at_birth()
    {
        var submit = DealReturnServiceKindTests.CheckoutEndpoint();
        const string needle = "Services.Deals.DealReturnRequest.KindAtBirth(Services.Deals.GatewayPayments.MarkedAtBirth(new()";
        const string kindOf = "Services.Deals.DealReturnRequest.CategoryKindOf(tenant?.Categories, listing.CategorySlug)";
        Assert.Equal(1, Count(submit, needle));
        Assert.Equal(1, Count(submit, kindOf));
        Assert.True(submit.IndexOf(needle, StringComparison.Ordinal) < submit.IndexOf(kindOf, StringComparison.Ordinal));
    }

    private static int Count(string text, string needle)
    {
        var n = 0;
        for (var at = text.IndexOf(needle, StringComparison.Ordinal); at >= 0; at = text.IndexOf(needle, at + needle.Length, StringComparison.Ordinal)) n++;
        return n;
    }
}
