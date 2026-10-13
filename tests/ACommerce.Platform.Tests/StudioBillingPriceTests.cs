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
        Assert.Single(Regex.Matches(razor, Regex.Escape("@t.MonthlyPriceSar")));

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
    /// والنَصُّ البَديلُ في المَعجَمِ بِالعَرَبِيَّةِ وَحدَها، بِلا رَقَمٍ ولا عُملَة.
    ///
    /// <para><b>و«أَيُّ بِطاقَةٍ» تُعَدُّ لا تُفتَرَض</b> (‏2026-10-13 — وُسِّعَ بَعدَ أَن وَجَدَت مُراجَعَةُ تَيّارِ S11، الجَولَة ١، الشَقيقَةَ
    /// <c>UpgradePrompt.razor</c> تَرسُمُ السِعرَ بِزِرٍّ إلى صَفحَةٍ لا تَبيعُه، وهذا الفَحصُ لا يَقرَأُ إلّا <c>StudioBilling.razor</c>): كُلُّ
    /// <c>.razor</c> في <c>libs</c> و<c>apps</c> يَقرَأُ <c>MonthlyPriceSar</c> خارِجَ التَعليقاتِ يُعَدّ، ومَجموعَتُها مُثَبَّتَةٌ — فَراسِمٌ
    /// رابِعٌ يُحمِرُّ حَتّى يُقَرَّرَ بِاسمِه — وكُلُّ مَوضِعٍ فيه تَحتَ شَرطِ بَيعٍ: <c>StudioTierPurchase.IsSelfGrantable</c> صَريحاً، أَو
    /// عُضوٌ مُعَرَّفٌ بِه في المِلَفِّ نَفسِه. إلّا الاستِثناءَ المُسَمّى: <c>Admin/QualityMonitor.razor</c> — تَوزيعُ الدَرَجاتِ لِمُشرِفِ
    /// المَنَصَّةِ داخِلَ <c>&lt;RequirePlatformAdmin&gt;</c>، بَيانٌ لا عَرضُ بَيعٍ ولا بابَ فيه.</para></summary>
    [Fact]
    public void No_price_renders_on_any_card_while_self_service_checkout_does_not_exist()
    {
        Assert.False(StudioTierPurchase.SelfServiceCheckoutExists);
        var priced = TierCatalog.All.Values.Where(t => t.MonthlyPriceSar > 0).ToList();
        Assert.True(priced.Count >= 4, $"أَداةٌ عَمياء: {priced.Count} دَرَجَةً بِسِعر.");
        var shownPrice = priced.Where(t => StudioTierPurchase.IsSelfGrantable(t.Tier)).Select(t => t.Tier).ToList();
        Assert.True(shownPrice.Count == 0, $"فُحِصَت {priced.Count} دَرَجَةً بِسِعر، ويُرسَمُ سِعرُ {shownPrice.Count}: {string.Join(", ", shownPrice)}");

        const string Templates = "libs/templates/ACommerce.Templates.Customer.Marketplace/Components/";
        const string AdminOnly = Templates + "Pages/Admin/QualityMonitor.razor";
        var root = ThemeZeroEquivalenceTests.RepoRoot;
        var renderers = new[] { "libs", "apps" }
            .SelectMany(d => Directory.EnumerateFiles(Path.Combine(root, d), "*.razor", SearchOption.AllDirectories))
            .Select(p => (Rel: Path.GetRelativePath(root, p).Replace('\\', '/'), Path: p))
            .Where(f => !f.Rel.Contains("/bin/", StringComparison.Ordinal) && !f.Rel.Contains("/obj/", StringComparison.Ordinal))
            .Select(f => (f.Rel, Code: WriteEndpointGuardTests.StripComments(
                WriteEndpointGuardTests.StripMarkupComments(File.ReadAllText(f.Path)))))
            .Where(f => f.Code.Contains("MonthlyPriceSar", StringComparison.Ordinal))
            .OrderBy(f => f.Rel, StringComparer.Ordinal)
            .ToList();
        Assert.Equal(
            new[] { AdminOnly, Templates + "Pages/StudioBilling.razor", Templates + "UpgradePrompt.razor" },
            renderers.Select(f => f.Rel).ToArray());

        var sites = 0;
        foreach (var (rel, code) in renderers)
        {
            foreach (Match m in Regex.Matches(code, "MonthlyPriceSar"))
            {
                sites++;
                if (rel == AdminOnly)
                {
                    var gate = code.IndexOf("<RequirePlatformAdmin>", StringComparison.Ordinal);
                    var gateEnd = code.IndexOf("</RequirePlatformAdmin>", StringComparison.Ordinal);
                    Assert.True(gate >= 0 && gate < m.Index && m.Index < gateEnd, $"{rel}: السِعرُ خارِجَ <RequirePlatformAdmin>.");
                    continue;
                }
                var around = UpgradePromptPriceTests.EnclosingIfConditions(code, m.Index);
                Assert.True(around.Any(c => IsSellCondition(code, c)),
                    $"{rel}: سِعرٌ يُرسَمُ تَحتَ [{string.Join(" ∧ ", around)}] — ولا شَرطَ بَيعٍ بَينَها.");
            }
        }
        Assert.True(sites >= 3, $"أَداةٌ عَمياء: فُحِصَ {sites} مَوضِعَ سِعرٍ في {renderers.Count} مِلَفّ.");

        var text = ACommerce.Platform.I18n.LocaleCatalog.Find(ACommerce.Platform.I18n.LocaleCatalog.Arabic, "studio.billing.limits_only");
        Assert.False(string.IsNullOrWhiteSpace(text));
        Assert.DoesNotMatch("[0-9٠-٩]", text);
        Assert.DoesNotContain("ر.س", text, StringComparison.Ordinal);
    }

    /// <summary>شَرطُ بَيعٍ: يَسأَلُ <c>StudioTierPurchase.IsSelfGrantable</c> صَريحاً، أَو يَقرَأُ عُضواً مُعَرَّفاً في المِلَفِّ نَفسِه
    /// (<c>X =&gt; …;</c>) يَسأَلُها — كَـ<c>Offer</c> في <c>UpgradePrompt.razor</c>.
    ///
    /// <para><b>بِقُطبِيَّتِه لا بِاحتِوائِه</b> (‏2026-10-13 — مُراجَعَةُ تَيّارِ S11، الجَولَة ٢: «يَحوي <c>IsSelfGrantable(</c>» كانَ يَقبَلُ
    /// <c>!StudioTierPurchase.IsSelfGrantable(…)</c> و<c>… || true</c>، و«عُضوٌ يَحويها» كانَ يَقبَلُ <c>Offer is null</c> وتَبديلَ فَرعَي
    /// الشَرطِ في تَعريفِه). فَيُقبَلُ شَكلانِ لا ثالِثَ لَهُما، ويُغلِقُ الفاحِصُ عِندَ الشَكّ:</para>
    /// <list type="number">
    ///   <item>طَرَفٌ في عَطفِ المُستَوى الأَعلى (<c>&amp;&amp;</c> خارِجَ الأَقواس) هُوَ بِحَرفِه
    ///   <c>StudioTierPurchase.IsSelfGrantable(…)</c> — لا نَفيَ قَبلَه ولا مُقارَنَةَ بَعدَه؛</item>
    ///   <item>أَو طَرَفٌ هُوَ بِحَرفِه <c>X is not null</c>، و<c>X</c> عُضوٌ تَعريفُه <c>شَرطُ بَيعٍ ? قيمَة : null</c> بِالشَكلِ الأَوَّل.</item>
    /// </list>
    /// <para>وشَرطٌ فيه <c>||</c> أَو <c>?</c> لَيسَ شَرطَ بَيعٍ أَيّاً كانَ ما فيه: كِلاهُما أَدنى أَسبَقِيَّةً مِن <c>&amp;&amp;</c>، فَيَجعَلانِ
    /// طَرَفاً ظاهِرُه البَيعُ غَيرَ لازِم.</para></summary>
    private static bool IsSellCondition(string code, string condition, bool viaMember = false)
    {
        var c = Regex.Replace(condition, @"\s+", " ").Trim();
        if (c.Contains("||", StringComparison.Ordinal) || c.Contains('?')) return false;
        foreach (var term in TopLevelConjuncts(c))
        {
            if (Regex.IsMatch(term, @"^StudioTierPurchase\.IsSelfGrantable\([\w.]+\)$")) return true;
            var member = Regex.Match(term, @"^(?<m>[A-Z]\w*) is not null$");
            if (viaMember || !member.Success) continue;
            var def = Regex.Match(code, $@"\b{member.Groups["m"].Value}\s*=>(?<expr>[^;]+);");
            if (!def.Success) continue;
            var ternary = Regex.Match(Regex.Replace(def.Groups["expr"].Value, @"\s+", " ").Trim(),
                @"^(?<cond>[^?:]+) \? (?<then>\w+) : null$");
            if (ternary.Success && ternary.Groups["then"].Value != "null"
                && IsSellCondition(code, ternary.Groups["cond"].Value, viaMember: true)) return true;
        }
        return false;
    }

    /// <summary>أَطرافُ العَطفِ في المُستَوى الأَعلى — <c>&amp;&amp;</c> خارِجَ <c>( )</c> و<c>{ }</c>؛ فَطَرَفٌ داخِلَ
    /// <c>!( … )</c> لا يَخرُجُ طَرَفاً.</summary>
    private static IEnumerable<string> TopLevelConjuncts(string condition)
    {
        var depth = 0;
        var start = 0;
        for (var i = 0; i < condition.Length; i++)
        {
            if (condition[i] is '(' or '{') depth++;
            else if (condition[i] is ')' or '}') depth--;
            else if (depth == 0 && condition[i] == '&' && i + 1 < condition.Length && condition[i + 1] == '&')
            {
                yield return condition[start..i].Trim();
                start = i + 2;
                i++;
            }
        }
        yield return condition[start..].Trim();
    }

    /// <summary><b>حَقنُ عَيب — فاحِصُ شَرطِ البَيعِ يُحمِرُّ فِعلاً</b> (‏2026-10-13، الجَولَة ٢؛ بِشَكلِ
    /// <c>UpgradeLadderTests.The_ladder_checker_can_actually_go_red</c>): «كُلُّ سِعرٍ تَحتَ شَرطِ بَيع» مِن فاحِصٍ يَقبَلُ كُلَّ شَيءٍ
    /// لا يُمَيَّزُ عَن فاحِصٍ يَقرَأ. فَتُحقَنُ الأَشكالُ الَّتي قُلِبَت بِها القُطبِيَّةُ ومَرَّت — عَلى الشَرطِ صَريحاً وعَلى تَعريفِ
    /// <c>Offer</c> — ويُشتَرَطُ رَفضُها، ويُشتَرَطُ قَبولُ الشَكلَينِ الحَيَّين: النِداءُ صَريحاً (وَحدَه أَو طَرَفاً في عَطف)، والعُضو.</summary>
    [Fact]
    public void The_sell_condition_checker_can_actually_go_red()
    {
        const string Offer = "TierLimits? Offer => Next is { } n && StudioTierPurchase.IsSelfGrantable(n.Tier) ? n : null;";
        string OfferWith(string from, string to)
        {
            Assert.Contains(from, Offer, StringComparison.Ordinal);
            return Offer.Replace(from, to, StringComparison.Ordinal);
        }
        const string Sell = "StudioTierPurchase.IsSelfGrantable(n.Tier)";

        var accepted = new (string Condition, string Code)[]
        {
            ("StudioTierPurchase.IsSelfGrantable(t.Tier)", ""),
            ("!string.IsNullOrEmpty(Reason) && StudioTierPurchase.IsSelfGrantable(t.Tier)", ""),
            ("Offer is not null", Offer),
        };
        var rejected = new (string Condition, string Code)[]
        {
            ("!StudioTierPurchase.IsSelfGrantable(t.Tier)", ""),
            ("!(StudioTierPurchase.IsSelfGrantable(t.Tier))", ""),
            ("StudioTierPurchase.IsSelfGrantable(t.Tier) || true", ""),
            ("StudioTierPurchase.IsSelfGrantable(t.Tier) == false", ""),
            ("StudioTierPurchase.IsSelfGrantable(t.Tier) is false", ""),
            ("StudioTierPurchase.IsSelfGrantable(t.Tier) ^ true", ""),
            ("!(Shown && StudioTierPurchase.IsSelfGrantable(t.Tier) && Ready)", ""),
            ("StudioTierPurchase.IsSelfGrantable(t.Tier) && Ready ? false : true", ""),
            ("Offer is null", Offer),
            ("Offer is not null", OfferWith("&& " + Sell, "&& !" + Sell)),
            ("Offer is not null", OfferWith(Sell, "!(" + Sell + ")")),
            ("Offer is not null", OfferWith(Sell, "(" + Sell + " || true)")),
            ("Offer is not null", OfferWith(Sell, Sell + " == false")),
            ("Offer is not null", OfferWith(Sell, Sell + " is false")),
            ("Offer is not null", OfferWith(Sell, "(" + Sell + " ^ true)")),
            ("Offer is not null", OfferWith("? n : null", "? null : n")),
            ("Offer is not null", "TierLimits? Offer => Next;"),
        };

        var wrong = accepted.Where(x => !IsSellCondition(x.Code, x.Condition)).Select(x => $"رُفِضَ حَيٌّ: [{x.Condition}] {x.Code}")
            .Concat(rejected.Where(x => IsSellCondition(x.Code, x.Condition)).Select(x => $"قُبِلَ مَقلوبٌ: [{x.Condition}] {x.Code}"))
            .ToList();
        Assert.True(accepted.Length >= 3 && rejected.Length >= 17,
            $"أَداةٌ عَمياء: حُقِنَ {rejected.Length} شَكلاً مَقلوباً و{accepted.Length} حَيّاً.");
        Assert.True(wrong.Count == 0,
            $"حُقِنَ {rejected.Length} شَكلاً مَقلوباً و{accepted.Length} حَيّاً، وأَخطَأَ الفاحِصُ في {wrong.Count}:\n  " + string.Join("\n  ", wrong));
    }
}
