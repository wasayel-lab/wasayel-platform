using System.Text.RegularExpressions;
using ACommerce.Templates.Customer.Marketplace.Services.Incubator;
using Xunit;

namespace ACommerce.Platform.Tests;

// ═══ لافِتَةُ التَرقِيَةِ تَعرِضُ سِعرَ دَرَجَةٍ لا تُباع — الشَقيقَةُ الَّتي لَم يَبلُغها `2db0bfb` (موجزُ الفجوات P2-3) ═══
//
// **المَقيس** (‏2026-10-13، مُراجَعَةُ تَيّارِ S11 — الجَولَة ١): `UpgradePrompt.razor` تَرسُمُ لِمَن بَلَغَ حَدَّه حُدودَ
// `TierCatalog.NextAbove(CurrentTier)` وزِرّاً إلى `/studio/billing` نَصُّه «تَرقِيَة إلى {LabelAr} ({MonthlyPriceSar} / شَهر)» — تَحتَ
// `Next is not null` وَحدَه، ولا تَسأَلُ `StudioTierPurchase.IsSelfGrantable` الَّتي يَسأَلُها سِعرُ البِطاقَةِ وزِرُّها في `/studio/billing`.
// و`/studio/billing` (`Shown`) لا تَرسُمُ دَرَجَةً لا تُمنَحُ ذاتِيّاً إلّا دَرَجَةَ المُستَخدِمِ نَفسِه — فَالزِرُّ يَحمِلُ سِعراً ويَنتَهي إلى
// صَفحَةٍ لا تَعرِضُ ما سَمّاه ولا تَبيعُه (القاعِدَة ١٢).
//
// حَيّاً عَلى نُسخَةٍ مَرمِيَّةٍ مِن قاعِدَةٍ مَبذورَة: مالِكُ استوديو عادِيٌّ، `/studio?upgrade=analyze` ثُمَّ `/studio/billing`، والدَرَجَةُ
// المُخَزَّنَةُ تُكتَبُ في النُسخَةِ بِـSQL — لا كاتِبَ في المُنتَجِ لِدَرَجَةٍ بِسِعر (`StudioTierPurchase.Refuse` يَرُدُّها)، لكِنّ ما خُزِّنَ
// قَبلَ إغلاقِ `/studio/billing/select` (‏2026-08-30) يُقرَأُ كَما هو:
//   spark  → لا قائِمَةَ ولا زِرّ                                        | billing: [Spark]
//   lite   → أَربَعَةُ بُنودٍ مِن حُدودِ Spark + «تَرقِيَة إلى Spark (99 ر.س / شَهر)» | billing: [Lite]   — ولا Spark فيها
//   growth → أَربَعَةُ بُنودٍ مِن حُدودِ Spark + «تَرقِيَة إلى Spark (99 ر.س / شَهر)» | billing: [Growth] — ولا Spark فيها
//   scale  → لا قائِمَةَ ولا زِرّ                                        | billing: [Scale]
// ويَومَ يُقلَبُ `FreeTierTemporarilyRaised` (العَودَةُ المُوَثَّقَةُ سَطرانِ في `StudioTier.cs`) يَصيرُ ذلك عَرضَ كُلِّ مُستَخدِم:
// «تَرقِيَة إلى Lite (199 ر.س / شَهر)» — وهو بِحَرفِه ما في `i18n-baseline/studio-upgrade-*.html` المُودَعَةِ مِن قَبلِ الرَفع.
//
// يُثَبَّتُ هُنا قَبلَ العِلاجِ (القاعِدَة ٣): الصَفُّ الأَوَّلُ يَنقَلِبُ بِاسمِه في كوميتِ العِلاج، والآخَرانِ يَبقَيانِ أَخضَرَينِ بِلا تَعديلِ حَرف.
//
// **العِلاج** (‏2026-10-13): `Offer` = `Next` مَصفِيّاً بِـ`StudioTierPurchase.IsSelfGrantable`، والوَسمُ يَقرَؤُه وَحدَه — فَالأَوسَعُ الَّذي لا
// يُباعُ يُرسَمُ كَما لا أَوسَع. الصَفُّ الأَوَّلُ انقَلَبَ بِاسمِه، والرابِعُ جَديد، والثاني والثالِثُ كَما أُودِعا.
public class UpgradePromptPriceTests
{
    /// <summary>نَصُّ مِلَفٍّ بِلا تَعليقاتٍ (‏<c>@* *@</c> و<c>&lt;!-- --&gt;</c> ثُمَّ تَعليقاتُ C#) — والفَهارِسُ مَحفوظَةٌ، لِأَنّ
    /// التَعليقَ يُفَرَّغُ ولا يُحذَف.</summary>
    internal static string Code(params string[] relative)
    {
        var path = Path.Combine(new[] { ThemeZeroEquivalenceTests.RepoRoot }.Concat(relative).ToArray());
        var text = File.ReadAllText(path);
        Assert.True(text.Length > 500, $"أَداةٌ عَمياء: {path} لَم يُقرَأ.");
        return WriteEndpointGuardTests.StripComments(WriteEndpointGuardTests.StripMarkupComments(text));
    }

    private static string Prompt() => Code(
        "libs", "templates", "ACommerce.Templates.Customer.Marketplace", "Components", "UpgradePrompt.razor");

    /// <summary>شُروطُ كُلِّ <c>if (…) { … }</c> تَحوي كُتلَتُه <paramref name="index"/> — مِن الخارِجِ إلى الداخِل، بِمُوازَنَةِ
    /// الأَقواسِ لا بِعَدِّ أَسطُر. و<c>if</c> بِلا كُتلَةٍ (سَطرٌ واحِد) لا يُحسَب: لا يَحوي ما بَعدَه.</summary>
    internal static IReadOnlyList<string> EnclosingIfConditions(string code, int index)
    {
        Assert.True(index >= 0, "المَوضِعُ غَيرُ مَوجودٍ في النَصّ — الأَداةُ عَمياء.");
        var found = new List<string>();
        foreach (Match m in Regex.Matches(code, @"(?<![\w.])@?if\s*\("))
        {
            if (m.Index >= index) break;
            var open = m.Index + m.Length - 1;
            var close = Matching(code, open, '(', ')');
            var brace = close + 1;
            while (brace < code.Length && char.IsWhiteSpace(code[brace])) brace++;
            if (brace >= code.Length || code[brace] != '{') continue;
            var end = Matching(code, brace, '{', '}');
            if (brace < index && index < end)
                found.Add(Regex.Replace(code[(open + 1)..close], @"\s+", " ").Trim());
        }
        return found;
    }

    private static int Matching(string code, int open, char o, char c)
    {
        var depth = 0;
        for (var i = open; i < code.Length; i++)
        {
            if (code[i] == o) depth++;
            else if (code[i] == c && --depth == 0) return i;
        }
        Assert.Fail($"«{o}» بِلا مُقابِلٍ عِندَ {open} — الأَداةُ عَمياء.");
        return -1;
    }

    /// <summary><b>حُدودُ الأَوسَعِ وزِرُّه وسِعرُه لِما يُباعُ وَحدَه</b> (‏2026-10-13 — انقَلَبَ بِقَرارٍ مَرئِيٍّ مِن
    /// <c>Today_the_banner_draws_the_next_tiers_price_and_sell_button_under_no_sell_condition</c>): قائِمَةُ <c>BenefitsFor</c> وكُتلَةُ
    /// <c>&lt;a … href="/studio/billing"&gt;</c> وقيمَةُ <c>MonthlyPriceSar</c> شَرطُها الداخِلِيُّ <c>Offer is not null</c>، و<c>Offer</c> هُوَ
    /// <c>Next</c> مَصفِيّاً بِـ<c>StudioTierPurchase.IsSelfGrantable</c> — الدالَّةُ الَّتي تَرسُمُ سِعرَ البِطاقَةِ وزِرَّها في
    /// <c>/studio/billing</c>، لا شَرطٌ ثانٍ.</summary>
    [Fact]
    public void The_banner_draws_the_next_tiers_price_and_sell_button_only_for_a_tier_billing_sells()
    {
        var razor = Prompt();
        Assert.Equal(1, Regex.Matches(razor, "MonthlyPriceSar").Count);

        var expected = new[] { "!string.IsNullOrEmpty(Reason)", "Offer is not null" };
        foreach (var marker in new[] { "BenefitsFor(", "href=\"/studio/billing\"", "MonthlyPriceSar" })
            Assert.Equal(expected, EnclosingIfConditions(razor, razor.IndexOf(marker, StringComparison.Ordinal)));

        var offer = Regex.Match(razor, @"TierLimits\?\s+Offer\s*=>(?<expr>[^;]+);");
        Assert.True(offer.Success, "لا تَعريفَ لِـ`Offer` في اللافِتَة.");
        Assert.Matches(@"\bNext\b", offer.Groups["expr"].Value);
        Assert.Contains("StudioTierPurchase.IsSelfGrantable(", offer.Groups["expr"].Value, StringComparison.Ordinal);
    }

    /// <summary><b>والسُلَّمُ يَعرِضُ اليَومَ عَلى دَرَجَتَينِ مُخَزَّنَتَينِ — وعَلى المَجّانِيَّةِ يَومَ تَعودُ حُدودُها — دَرَجَةً بِسِعرٍ لا
    /// تَرسُمُها <c>/studio/billing</c> ولا تَبيعُها.</b> يُقاسُ بِالدَوالِّ نَفسِها (<c>NextAbove</c> و<c>IsSelfGrantable</c>) وبِشَرطِ
    /// <c>Shown</c> في صَفحَةِ الباقاتِ حَرفاً (تُمنَحُ ذاتِيّاً أَو هي الحالِيَّة)؛ والأَرقامُ تُقرَأُ ولا تُكتَب (القاعِدَة ١٦). وهذا لا
    /// يَتَبَدَّلُ بِالعِلاج — العِلاجُ في اللافِتَةِ لا في السُلَّم.</summary>
    [Fact]
    public void The_ladder_offers_two_stored_tiers_and_the_reverted_free_tier_a_priced_tier_billing_neither_shows_nor_sells()
    {
        Assert.False(StudioTierPurchase.SelfServiceCheckoutExists);
        Assert.True(TierCatalog.All.Count >= 4, $"أَداةٌ عَمياء: {TierCatalog.All.Count} دَرَجَة.");

        var offered = 0;
        var dead = new List<string>();
        foreach (var current in TierCatalog.All.Values)
        {
            var next = TierCatalog.NextAbove(current.Tier);
            if (next is null) continue;
            offered++;
            var shownOnBilling = StudioTierPurchase.IsSelfGrantable(next.Tier) || next.Tier == current.Tier;
            if (next.MonthlyPriceSar > 0 && !shownOnBilling) dead.Add($"{current.Tier}→{next.Tier}");
        }
        Assert.True(offered >= 2, $"أَداةٌ عَمياء: فُحِصَت {offered} دَعوَةَ تَرقِيَة.");
        Assert.Equal(new[] { "growth→spark", "lite→spark" }, dead.OrderBy(x => x, StringComparer.Ordinal).ToArray());

        // والعَودَةُ المُوَثَّقَة: المَجّانِيَّةُ بِحُدودِها قَبلَ الرَفعِ يُعرَضُ عَلَيها `lite` — بِسِعرٍ، ولا تُمنَحُ ذاتِيّاً.
        var before = TierCatalog.FreeTierBeforeRaise;
        var reverted = TierCatalog.All.Values
            .Where(t => !string.Equals(t.Tier, before.Tier, StringComparison.Ordinal))
            .Where(t => TierCatalog.Exceeds(t, before))
            .OrderBy(t => t.MonthlyPriceSar)
            .First();
        Assert.Equal("lite", reverted.Tier);
        Assert.True(reverted.MonthlyPriceSar > 0, "lite بِلا سِعر");
        Assert.False(StudioTierPurchase.IsSelfGrantable(reverted.Tier), "lite تُمنَحُ ذاتِيّاً");
    }

    /// <summary><b>وعُنوانُ الحَدِّ ورِسالَتُه يُرسَمانِ لِكُلِّ سَبَبٍ أَيّاً كانَ المَعروض</b> — تَحتَ شَرطِ <c>Reason</c> وَحدَه، فَإخفاءُ
    /// العَرضِ لا يُسقِطُ اللافِتَة: مَن بَلَغَ حَدَّه يُقالُ لَه ذلك ولو لَم يُبَع لَه شَيء (‏<c>UpgradeLadderTests</c>: «بَلَغتَ حَدَّك» بِلا زِرِّ
    /// بَيع).</summary>
    [Fact]
    public void The_limit_title_and_message_render_for_every_reason_whatever_is_offered()
    {
        var razor = Prompt();
        foreach (var marker in new[] { "@TitleFor(Reason)", "@MessageFor(Reason)" })
        {
            var at = razor.IndexOf(marker, StringComparison.Ordinal);
            Assert.True(at > 0, $"«{marker}» غَيرُ مَرسومٍ في اللافِتَة.");
            Assert.Equal(new[] { "!string.IsNullOrEmpty(Reason)" }, EnclosingIfConditions(razor, at));
        }
    }

    /// <summary><b>ودَرَجَةٌ أَوسَعُ لا تُباعُ تُرسَمُ لافِتَتُها لافِتَةَ «لا أَوسَع» بِحَرفِها</b> (‏2026-10-13): الوَسمُ لا يَقرَأُ
    /// <c>Next</c> في أَيِّ مَوضِعٍ — يَقرَأُ <c>Offer</c> وَحدَه — فَحينَ يَكونُ <c>null</c> لِأَنّ الأَوسَعَ لا يُباعُ يَخرُجُ ما يَخرُجُ حينَ
    /// لا أَوسَع: العُنوانُ والرِسالَة. ومَعَ <c>SelfServiceCheckoutExists = false</c> لا دَرَجَةَ في الكاتالوجِ — مُخَزَّنَةً كانَت أَو
    /// مَجّانِيَّةً بَعدَ العَودَة — يُعرَضُ عَلَيها شَيء.</summary>
    [Fact]
    public void An_unsellable_wider_tier_draws_the_same_banner_as_no_wider_tier()
    {
        var razor = Prompt();
        var code = razor.IndexOf("@code", StringComparison.Ordinal);
        Assert.True(code > 300, "لا كُتلَةَ `@code` في اللافِتَة — الأَداةُ عَمياء.");
        var markup = razor[..code];
        Assert.DoesNotMatch(@"\bNext\b", markup);
        Assert.Equal(2, Regex.Matches(markup, @"@if \(Offer is not null\)").Count);

        Assert.False(StudioTierPurchase.SelfServiceCheckoutExists);
        var before = TierCatalog.FreeTierBeforeRaise;
        var reverted = TierCatalog.All.Values
            .Where(t => !string.Equals(t.Tier, before.Tier, StringComparison.Ordinal))
            .Where(t => TierCatalog.Exceeds(t, before))
            .OrderBy(t => t.MonthlyPriceSar)
            .FirstOrDefault();
        var nexts = TierCatalog.All.Values.Select(t => TierCatalog.NextAbove(t.Tier)).Append(reverted)
            .Where(n => n is not null).Select(n => n!).ToList();
        Assert.True(nexts.Count >= 3, $"أَداةٌ عَمياء: فُحِصَت {nexts.Count} دَرَجَةً أَوسَع.");
        var sold = nexts.Where(n => StudioTierPurchase.IsSelfGrantable(n.Tier)).Select(n => n.Tier).ToList();
        Assert.True(sold.Count == 0, $"فُحِصَت {nexts.Count} دَرَجَةً أَوسَع، ويُعرَضُ بِسِعرٍ {sold.Count}: {string.Join(", ", sold)}");
    }
}
