using System.Text.RegularExpressions;
using ACommerce.Templates.Customer.Marketplace.Services.Incubator;
using Xunit;

namespace ACommerce.Platform.Tests;

// ═══ بِطاقَةُ دَرَجَةِ الاستوديو تَرسُمُ سِعراً شَهرِيّاً لِدَرَجَةٍ لا تُباع (موجزُ الفجوات P2-3) ═══
//
// **المَقيس** (‏2026-10-13): `StudioBilling.razor` يَرسُمُ `@t.MonthlyPriceSar` و«ر.س / شَهر» في كُلِّ بِطاقَةٍ بِلا شَرط،
// و`StudioTierPurchase.SelfServiceCheckoutExists = false` وكُلُّ دَرَجَةٍ بِسِعرٍ (‏spark ‏99 مِنها) — فَلا دَرَجَةَ تُمنَحُ ذاتِيّاً،
// والبِطاقَةُ الوَحيدَةُ المَرسومَةُ دَرَجَةُ المُستَخدِمِ الحالِيَّة (`Shown`): «Spark ‏99 ر.س / شَهر» بِزِرٍّ مُعَطَّلٍ «الباقَة الحاليَّة»،
// والمُستَخدِمُ عَلَيها بِلا دَفع. مَقيسٌ حَيّاً بِأَداةِ اللَقطَة: `studio-billing` بِمِلَفِّ `studio` يَحمِلُ
// `<div style="font-size:28px;…">99 <span …>ر.س / شَهر</span></div>`. ورَقمُ الاشتِراكِ ودَورِيَّتُه غَيرُ مَوجودَينِ في أَيِّ مِلَفّ
// (ق‑٥ «كِلا الحَدَّينِ بِلا رَقم»؛ القاعِدَة ١٦) — فَالأَرقامُ لا تُمَسّ، ويُخفى عَرضُها حَيثُ لا بَيع.

public class StudioBillingPriceTests
{
    private static string Billing()
    {
        var path = Path.Combine(ThemeZeroEquivalenceTests.RepoRoot,
            "libs", "templates", "ACommerce.Templates.Customer.Marketplace", "Components", "Pages", "StudioBilling.razor");
        var text = File.ReadAllText(path);
        Assert.True(text.Length > 1000, $"أَداةٌ عَمياء: {path} لَم يُقرَأ.");
        return text;
    }

    /// <summary>كُتلَةُ سِعرِ البِطاقَة — مِن فَتحِ <c>div</c> الحَجمِ ‏28 إلى إغلاقِه، بِمُوازَنَةِ وُسومِ <c>div</c>.</summary>
    private static string PriceBlock()
    {
        var razor = Billing();
        var open = razor.IndexOf("<div style=\"font-size:28px;", StringComparison.Ordinal);
        Assert.True(open > 0, "لا كُتلَةَ سِعرٍ في البِطاقَة — الأَداةُ عَمياء.");
        var depth = 0;
        foreach (Match m in Regex.Matches(razor[open..], @"<div\b|</div>"))
        {
            depth += m.Value == "</div>" ? -1 : 1;
            if (depth == 0) return razor.Substring(open, m.Index + m.Length);
        }
        Assert.Fail("كُتلَةُ السِعرِ بِلا إغلاق.");
        return "";
    }

    /// <summary><b>اليَوم: كُلُّ دَرَجَةٍ بِسِعرٍ ولا واحِدَةَ تُباعُ ذاتِيّاً</b> — فَكُلُّ بِطاقَةٍ مَرسومَةٍ بِطاقَةُ دَرَجَةٍ لا تُشتَرى
    /// مِن الشاشَة. والأَرقامُ تُقرَأُ ولا تُكتَبُ هُنا (القاعِدَة ١٦).</summary>
    [Fact]
    public void Today_every_studio_tier_carries_a_price_and_none_can_be_bought_from_the_screen()
    {
        Assert.False(StudioTierPurchase.SelfServiceCheckoutExists);
        Assert.True(TierCatalog.All.Count >= 4, $"أَداةٌ عَمياء: {TierCatalog.All.Count} دَرَجَة.");
        foreach (var t in TierCatalog.All.Values)
        {
            Assert.True(t.MonthlyPriceSar > 0, $"{t.Tier} بِلا سِعر");
            Assert.False(StudioTierPurchase.IsSelfGrantable(t.Tier), $"{t.Tier} تُمنَحُ ذاتِيّاً");
        }
    }

    /// <summary><b>اليَوم: البِطاقَةُ تَرسُمُ سِعرَها بِلا شَرط</b> — قيمَةُ <c>MonthlyPriceSar</c> و«{0} / شَهر» في كُتلَةِ السِعرِ
    /// بِلا <c>@if</c>، فَتُرسَمُ لِدَرَجَةٍ لا تُباع. (‏تَوصيفٌ يَنقَلِبُ بِقَرارٍ مَرئِيٍّ في كوميتِ الإخفاء.)</summary>
    [Fact]
    public void Today_the_tier_card_renders_its_price_unconditionally()
    {
        var block = PriceBlock();
        Assert.Contains("@t.MonthlyPriceSar", block, StringComparison.Ordinal);
        Assert.Contains("studio.billing.per_month", block, StringComparison.Ordinal);
        Assert.DoesNotContain("@if", block, StringComparison.Ordinal);
    }
}
