using ACommerce.Kit.Tenants;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using ACommerce.Templates.Customer.Marketplace.Services.Patterns;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ «طَلَب استِرجاع» عَلى صَفقَةِ خِدمَةٍ أَو فَعّالِيَّةٍ وَلَدَتها السَلَّةُ في مَتجَرِ «بَيع» — تَوصيفُ «اليَوم» (‏2026-11-05، الموجَة ١٢ G3 · ٨ — D-6؛ ‏ADR-047 §5 أَفصَحَه) ═══
//
// **المَقيس** (القاعِدَة ٣ — يُثَبَّتُ ما يَبقى ويُطبَعُ ما يَتَغَيَّر): بَوّابَةُ `DealReturnRequest` تَقِفُ عَلى النَمَطِ (`marketplace`) و`CartBorn` (‏`pay_method`)، والنَمَطُ مِن المَتجَرِ كُلِّه،
// و`/checkout/submit` لا يَكتُبُ عَلى الصَفقَةِ تَجميعَ فِئَةِ إعلانِها — فَمَتجَرُ بَيعٍ فيه فِئَةُ «خِدمات» أَو «فَعّالِيّات» يَرسُمُ «طَلَب استِرجاع» عَلى صَفقَةِ خِدمَةٍ سُلِّمَت.
// يُناقِضُ د30 «في متاجر بيع السلع». **وما يَبقى**: صَفقَةُ سِلعَةٍ وَلَدَتها السَلَّةُ (بِلا تَجميعٍ أَو بِتَجميعٍ غَيرِهِما) يُقبَلُ طَلَبُها، وصَفقَةٌ وُلِدَت قَبلَ اليَوم — لا تَحمِلُ التَجميع — كَذلك.

public class DealReturnServiceKindTests(ITestOutputHelper output)
{
    private static readonly DateTime DeliveredOn = new(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DealReturnRequester AsBuyer = new(Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa"), "مُشتَرٍ");

    /// <summary>مَتجَرُ «بَيع» (صِفرُ أَدوار) فيه فِئَةُ سِلَعٍ بِلا تَجميع، وفِئَةُ «خِدمات» وفِئَةٌ فَرعِيَّةٌ تَحتَها بِلا تَجميع، وفِئَةُ «فَعّالِيّات».</summary>
    internal static Tenant SellStoreWithServices() => new()
    {
        Id = "sell-with-services", Name = "مَتجَرُ بَيعٍ وخِدمات",
        Categories = new()
        {
            new() { Slug = "clothes", Label = "مَلابِس", Kind = CategoryKinds.None },
            new() { Slug = "cleaning", Label = "تَنظيف", Kind = CategoryKinds.Services },
            new() { Slug = "deep-cleaning", Label = "تَنظيفٌ عَميق", Kind = CategoryKinds.None, ParentSlug = "cleaning" },
            new() { Slug = "concert", Label = "حَفلَة", Kind = CategoryKinds.Events },
        },
    };

    /// <summary>جِسمُ نُقطَةِ إتمامِ الشِراءِ بِلا تَعليقات — قارِئُ <c>DealCheckoutPaymentOrderTests</c> بِحَرفِه.</summary>
    internal static string CheckoutEndpoint()
    {
        var path = Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, "libs", "templates", "ACommerce.Templates.Customer.Marketplace", "MarketplaceTemplateExtensions.cs");
        Assert.True(File.Exists(path), $"مَصدَرٌ مَفقود: {path} — الأَداةُ عَمياء.");
        var text = File.ReadAllText(path);
        var start = text.IndexOf("\"/{slug}/checkout/submit\"", StringComparison.Ordinal);
        Assert.True(start > 0, "نُقطَةُ إتمامِ الشِراءِ غَيرُ مَوجودَة — الأَداةُ عَمياء.");
        var end = text.IndexOf(".RequireStoreWritable();", start, StringComparison.Ordinal);
        Assert.True(end > start, "خاتِمَةُ النُقطَةِ غَيرُ مَوجودَة — الأَداةُ عَمياء.");
        return WriteEndpointGuardTests.StripComments(text.Substring(start, end - start));
    }

    /// <summary><b>صَفقَةٌ وَلَدَتها السَلَّةُ عَلى إعلانٍ في كُلِّ فِئَة</b>: ما يَبقى — نَمَطُها <c>marketplace</c> وصَفقَةُ «مَلابِس» يُقبَلُ طَلَبُها. ويُطبَعُ ما يَتَغَيَّر: رَدُّ البَوّابَةِ لِكُلِّ فِئَة،
    /// وأَيَكتُبُ <c>/checkout/submit</c> تَجميعَ الفِئَةِ عَلى الصَفقَة.</summary>
    [Theory]
    [InlineData("clothes")]
    [InlineData("cleaning")]
    [InlineData("deep-cleaning")]
    [InlineData("concert")]
    public void A_cart_born_deal_in_each_category_of_a_sell_store(string category)
    {
        var store = SellStoreWithServices();
        var deal = DealReturnGoodsGateTests.Delivered(store, cartBorn: true, title: category);
        var submit = CheckoutEndpoint();
        Assert.True(submit.Length > 1000, "أَداةٌ عَمياء: جِسمُ النُقطَةِ لَم يُقرَأ.");

        var verdict = DealReturnRequest.Validate(deal, AsBuyer, DeliveredOn.AddDays(1));

        output.WriteLine($"{category} (تَجميعُها «{store.Categories.Single(c => c.Slug == category).Kind}»): النَمَط {deal.Pattern} · الرَدّ: {verdict?.Code ?? "يُقبَل"} · " +
                         $"خَصائِصُ الصَفقَة [{string.Join(", ", deal.Attributes.Keys)}] · النُقطَةُ تَكتُبُ التَجميع؟ {submit.Contains("CategoryKind", StringComparison.Ordinal)}");
        Assert.Equal(DealReturnRequest.GoodsPattern, deal.Pattern);
        if (category == "clothes") Assert.Null(verdict);
    }
}
