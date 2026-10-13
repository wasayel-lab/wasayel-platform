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

    /// <summary><b>البِطاقَةُ تَرسُمُ سِعرَها حَيثُ تُشتَرى الدَرَجَةُ وَحدَه</b> (‏2026-10-13 — انقَلَبَ بِقَرارٍ مَرئِيٍّ مِن
    /// <c>Today_the_tier_card_renders_its_price_unconditionally</c>): قيمَةُ <c>MonthlyPriceSar</c> و«{0} / شَهر» في فَرعِ
    /// <c>@if (StudioTierPurchase.IsSelfGrantable(t.Tier))</c> وَحدَه — الدالَّةُ نَفسُها الَّتي تُرسِمُ زِرَّ «اختِيار» — وما سِواه
    /// حُدودُ الدَرَجَةِ بِنَصٍّ مِن القامُوس.</summary>
    [Fact]
    public void The_tier_card_renders_a_price_only_where_the_tier_can_be_bought()
    {
        var block = PriceBlock();
        var razor = Billing();
        Assert.Equal(1, Regex.Matches(razor, Regex.Escape("@t.MonthlyPriceSar")).Count);

        var guarded = Regex.Match(block,
            @"@if \(StudioTierPurchase\.IsSelfGrantable\(t\.Tier\)\)\s*\{(?<sold>[^{}]*)\}\s*else\s*\{(?<limits>[^{}]*)\}");
        Assert.True(guarded.Success, "السِعرُ لَيسَ تَحتَ شَرطِ البَيعِ في كُتلَتِه:\n" + block);
        Assert.Contains("@t.MonthlyPriceSar", guarded.Groups["sold"].Value, StringComparison.Ordinal);
        Assert.Contains("studio.billing.per_month", guarded.Groups["sold"].Value, StringComparison.Ordinal);
        Assert.DoesNotContain("MonthlyPriceSar", guarded.Groups["limits"].Value, StringComparison.Ordinal);
        Assert.Contains("@L.Markup(\"studio.billing.limits_only\")", guarded.Groups["limits"].Value, StringComparison.Ordinal);
    }

    /// <summary><b>ولا سِعرَ يُرسَمُ عَلى أَيِّ بِطاقَةٍ ما دامَ لا مَسارَ دَفعٍ ذاتِيّ</b>: مَعَ <c>SelfServiceCheckoutExists = false</c>
    /// لا دَرَجَةَ بِسِعرٍ تَعبُرُ شَرطَ الفَرع — فَإن رُسِمَ السِعرُ خارِجَه (الصَفُّ أَعلاه) أَو عَبَرَته دَرَجَةٌ بِسِعرٍ هُنا حَمِرَ.
    /// والنَصُّ البَديلُ في المَعجَمِ بِالعَرَبِيَّةِ وَحدَها، بِلا رَقَمٍ ولا عُملَة.</summary>
    [Fact]
    public void No_price_renders_on_any_card_while_self_service_checkout_does_not_exist()
    {
        Assert.False(StudioTierPurchase.SelfServiceCheckoutExists);
        var priced = TierCatalog.All.Values.Where(t => t.MonthlyPriceSar > 0).ToList();
        Assert.True(priced.Count >= 4, $"أَداةٌ عَمياء: {priced.Count} دَرَجَةً بِسِعر.");
        var shownPrice = priced.Where(t => StudioTierPurchase.IsSelfGrantable(t.Tier)).Select(t => t.Tier).ToList();
        Assert.True(shownPrice.Count == 0, $"فُحِصَت {priced.Count} دَرَجَةً بِسِعر، ويُرسَمُ سِعرُ {shownPrice.Count}: {string.Join(", ", shownPrice)}");

        var text = ACommerce.Platform.I18n.LocaleCatalog.Find(ACommerce.Platform.I18n.LocaleCatalog.Arabic, "studio.billing.limits_only");
        Assert.False(string.IsNullOrWhiteSpace(text));
        Assert.DoesNotMatch("[0-9٠-٩]", text);
        Assert.DoesNotContain("ر.س", text, StringComparison.Ordinal);
    }
}
