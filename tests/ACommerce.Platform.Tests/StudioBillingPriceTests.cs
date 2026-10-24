using System.Text.RegularExpressions;
using ACommerce.Templates.Customer.Marketplace.Services.Incubator;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ بِطاقَةُ دَرَجَةِ الاستوديو تَرسُمُ سِعراً شَهرِيّاً لِدَرَجَةٍ لا تُباع (موجزُ الفجوات P2-3) ═══
//
// **المَقيس** (‏2026-10-13): `StudioBilling.razor` يَرسُمُ `@t.MonthlyPriceSar` و«ر.س / شَهر» في كُلِّ بِطاقَةٍ بِلا شَرط،
// و`StudioTierPurchase.SelfServiceCheckoutExists = false` وكُلُّ دَرَجَةٍ بِسِعرٍ (‏spark ‏99 مِنها) — فَلا دَرَجَةَ تُمنَحُ ذاتِيّاً،
// والبِطاقَةُ الوَحيدَةُ المَرسومَةُ دَرَجَةُ المُستَخدِمِ الحالِيَّة (`Shown`): «Spark ‏99 ر.س / شَهر» بِزِرٍّ مُعَطَّلٍ «الباقَة الحاليَّة»،
// والمُستَخدِمُ عَلَيها بِلا دَفع. مَقيسٌ حَيّاً بِأَداةِ اللَقطَة: `studio-billing` بِمِلَفِّ `studio` يَحمِلُ
// `<div style="font-size:28px;…">99 <span …>ر.س / شَهر</span></div>`. ورَقمُ الاشتِراكِ ودَورِيَّتُه غَيرُ مَوجودَينِ في أَيِّ مِلَفّ
// (ق‑٥ «كِلا الحَدَّينِ بِلا رَقم»؛ القاعِدَة ١٦) — فَالأَرقامُ لا تُمَسّ، ويُخفى عَرضُها حَيثُ لا بَيع.

public class StudioBillingPriceTests(ITestOutputHelper output)
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

    // ═══ اتِّجاهُ `Shown`: أَيُّ دَرَجَةٍ تُرسَمُ لَها بِطاقَةٌ وزِرُّ «اختِيار» (‏2026-10-24 — مُراجَعَةُ الموجَة ٤، M5 المَحمولَة) ═══
    //
    // **المَقيس قَبلُ**: لا صَفَّ يَقرَأُ اتِّجاهَ `Shown` — ما يُثَبِّتُه نُسخَةٌ مِنه داخِلَ الاختِبارات (`UpgradePromptPriceTests`
    // يَحسُبُ `IsSelfGrantable(next.Tier) || next.Tier == current.Tier` بِنَفسِه)، و`PaymentLeakTests` يَسأَلُ `Contains("StudioTierPurchase")`
    // عَلى نَصٍّ يُحَقِّقُه تَعليقُ الرَأسِ وَحدَه. فَطَفرَةُ `bool Shown(TierLimits t) => true;` — كُلُّ دَرَجَةٍ بِسِعرٍ تُرسَمُ لَها
    // بِطاقَةٌ وزِرُّ «اختِيار» إلى نُقطَةٍ تَرُدُّه — مَرَّت بِالمَجموعَةِ كامِلَةً: مَقيساً عَلى نُسخَةٍ مُؤَرشَفَةٍ مِن `077e802` تَحتَها
    // DLL الاختِبارِ المَبنِيّ، ‏4162/4162 خَضراء (ومُرَشِّحُ الأَصنافِ الأَربَعَةِ ‏24/24).

    /// <summary>تَعريفُ <c>Shown</c> بِحَرفِه بَعدَ طَيِّ المَسافات — تُرسَمُ الدَرَجَةُ إن مُنِحَت ذاتِيّاً أَو كانَت دَرَجَةَ المُستَخدِمِ
    /// الحالِيَّة (بَيانُ حالٍ لا عَرضُ بَيع؛ رَأسُ <c>StudioBilling.razor</c>).</summary>
    internal const string ShownExpected = "StudioTierPurchase.IsSelfGrantable(t.Tier) || t.Tier == currentTier";

    /// <summary>مَصدَرُ الشَبَكَة بِحَرفِه — الحَلقَةُ الوَحيدَةُ الَّتي تَقرَأُ الكاتالوج.</summary>
    internal const string GridExpected = "TierCatalog.All.Values.Where(Shown)";

    /// <summary>ما يُخالِفُ اتِّجاهَ الشَبَكَةِ في نَصِّ <c>StudioBilling.razor</c> بِلا تَعليقات — فارِغَةٌ إن كانَ كُلُّه كَما يُرسَم اليَوم.
    /// يُغلِقُ عِندَ الشَكّ: تَعريفٌ لـ<c>Shown</c> غَيرُ واحِدٍ أَو بِغَيرِ شَكلِ التَعبير، أَو حَلقَةٌ تَقرَأُ <c>TierCatalog.All</c> غَيرُ واحِدَة،
    /// أَو مَصدَرُها بِغَيرِ حَرفِه، أَو نَموذَجُ «اختِيار» غَيرُ واحِدٍ أَو خارِجَ الحَلقَة، أَو خارِجَ فَرعِ <c>else</c> لِـ<c>@if (isCurrent)</c>،
    /// أَو <c>isCurrent</c> بِغَيرِ <c>t.Tier == currentTier</c>.</summary>
    internal static IReadOnlyList<string> BillingGridViolations(string code)
    {
        static string Fold(string s) => Regex.Replace(s, @"\s+", " ").Trim();
        var v = new List<string>();

        var mentions = Regex.Matches(code, @"\bbool\s+Shown\s*\(").Count;
        var defs = Regex.Matches(code, @"\bbool\s+Shown\s*\(\s*TierLimits\s+t\s*\)\s*=>(?<expr>[^;]+);");
        if (mentions != 1 || defs.Count != 1)
            v.Add($"shown_definition: {mentions} تَعريفاً و{defs.Count} بِشَكلِ التَعبير");
        else if (Fold(defs[0].Groups["expr"].Value) is var shown && shown != ShownExpected)
            v.Add($"shown_expr: «{shown}»");

        var loops = new List<(string Source, int BodyStart, int BodyEnd)>();
        foreach (Match m in Regex.Matches(code, @"@foreach\s*\("))
        {
            var open = m.Index + m.Length - 1;
            var close = Matching(code, open, '(', ')');
            var head = Fold(code[(open + 1)..close]);
            if (!head.Contains("TierCatalog.All", StringComparison.Ordinal)) continue;
            var brace = code.IndexOf('{', close);
            if (brace < 0) { v.Add("grid_body: الحَلقَةُ بِلا كُتلَة"); continue; }
            loops.Add((head, brace, Matching(code, brace, '{', '}')));
        }
        if (loops.Count != 1) v.Add($"grid_loops: {loops.Count} حَلقَةً تَقرَأُ TierCatalog.All");
        else if (loops[0].Source != "var t in " + GridExpected) v.Add($"grid_source: «{loops[0].Source}»");

        var forms = Regex.Matches(code, Regex.Escape("action=\"@($\"/studio/billing/select?tier={t.Tier}\")\"")).Cast<Match>().ToList();
        if (forms.Count != 1) v.Add($"buy_forms: {forms.Count} نَموذَجَ «اختِيار»");
        else if (loops.Count == 1)
        {
            var (bodyStart, bodyEnd) = (loops[0].BodyStart, loops[0].BodyEnd);
            var at = forms[0].Index;
            if (at < bodyStart || at > bodyEnd) v.Add("buy_form_outside_grid");
            // فَرعُ ‎@if (isCurrent)‎ بِاسمِه في جِسمِ الحَلقَة — أَوَّلُ ‎@if‎ فيها شَرطُ السِعرِ لا هذا.
            var cur = Regex.Matches(code[bodyStart..bodyEnd], @"@if\s*\(\s*isCurrent\s*\)\s*\{");
            var (elseStart, elseEnd) = (-1, -1);
            if (cur.Count == 1)
            {
                var thenEnd = Matching(code, bodyStart + cur[0].Index + cur[0].Length - 1, '{', '}');
                var elseOpen = Regex.Match(code[(thenEnd + 1)..], @"^\s*else\s*\{");
                if (elseOpen.Success)
                {
                    elseStart = thenEnd + elseOpen.Length;
                    elseEnd = Matching(code, elseStart, '{', '}');
                }
            }
            if (cur.Count != 1 || elseStart < 0 || at < elseStart || at > elseEnd)
                v.Add($"buy_form_not_in_else_of_isCurrent: {cur.Count} فَرعاً");
            var isCurrent = Regex.Matches(code, @"\bvar\s+isCurrent\s*=(?<e>[^;]+);");
            if (isCurrent.Count != 1 || Fold(isCurrent[0].Groups["e"].Value) != "t.Tier == currentTier")
                v.Add($"is_current: {isCurrent.Count} تَعريفاً" + (isCurrent.Count == 1 ? $" «{Fold(isCurrent[0].Groups["e"].Value)}»" : ""));
        }
        return v;
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

    private static string BillingCode() => UpgradePromptPriceTests.Code(
        "libs", "templates", "ACommerce.Templates.Customer.Marketplace", "Components", "Pages", "StudioBilling.razor");

    /// <summary><b>الشَبَكَةُ تَرسُمُ بِطاقَةً لِما يُمنَحُ ذاتِيّاً ولِلدَرَجَةِ الحالِيَّةِ وَحدَهُما، و«اختِيار» لِغَيرِ الحالِيَّة</b>
    /// (‏2026-10-24): <c>Shown</c> بِحَرفِه، والحَلقَةُ تَقرَؤُه بِحَرفِها، والنَموذَجُ في فَرعِ <c>else</c> لِـ<c>@if (isCurrent)</c>. ولِأَنّ
    /// التَعبيرَ مُثَبَّتٌ يُحسَبُ هُنا بِحَرفِه عَلى الكاتالوجِ — والأَرقامُ تُقرَأُ ولا تُكتَب (القاعِدَة ١٦): ما دامَ
    /// <c>SelfServiceCheckoutExists = false</c> تَرى كُلُّ دَرَجَةٍ حالِيَّةٍ بِطاقَتَها وَحدَها، وزِرُّها مُعَطَّل، فَصِفرُ نَموذَجِ «اختِيار».</summary>
    [Fact]
    public void The_billing_grid_draws_a_card_only_for_a_self_grantable_or_the_current_tier_and_a_buy_form_only_off_the_current()
    {
        var code = BillingCode();
        var violations = BillingGridViolations(code);
        // ‏(‏2026-10-24 — تَيّارُ S16، مُراجَعَةُ S13 الجَولَة ١) ويُطبَعُ ما فُحِص (القاعِدَة ١٠).
        output.WriteLine($"حَلَقاتٌ تَقرَأُ TierCatalog.All: {Regex.Matches(code, @"@foreach\s*\([^)]*TierCatalog\.All").Count} · نَماذِجُ «اختِيار»: "
            + $"{Regex.Matches(code, Regex.Escape("action=\"@($\"/studio/billing/select?tier={t.Tier}\")\"")).Count} · مُخالَفات: {violations.Count}");
        Assert.True(violations.Count == 0, "اتِّجاهُ الشَبَكَةِ انقَلَب:\n  " + string.Join("\n  ", violations));

        Assert.False(StudioTierPurchase.SelfServiceCheckoutExists);
        Func<TierLimits, string?, bool> shown = (t, currentTier) =>
            StudioTierPurchase.IsSelfGrantable(t.Tier) || t.Tier == currentTier;
        var tiers = TierCatalog.All.Values.ToList();
        Assert.True(tiers.Count >= 4, $"أَداةٌ عَمياء: {tiers.Count} دَرَجَة.");
        var cards = 0;
        var buyForms = 0;
        foreach (var current in tiers)
        {
            var drawn = TierCatalog.All.Values.Where(t => shown(t, current.Tier)).Select(t => t.Tier).ToArray();
            Assert.Equal(new[] { current.Tier }, drawn);
            cards += drawn.Length;
            buyForms += drawn.Count(t => t != current.Tier);
        }
        output.WriteLine($"دَرَجات: {tiers.Count} ({string.Join(", ", tiers.Select(t => t.Tier))}) · بِطاقاتٌ مَرسومَة: {cards} · نَماذِجُ «اختِيار»: {buyForms}");
        Assert.Equal(tiers.Count, cards);
        Assert.Equal(0, buyForms);
    }

    // ─── ‏2026-10-24 (تَيّارُ S16، مُراجَعَةُ S13 الجَولَة ١): نُسخَتا `Shown` في الاختِباراتِ مَربوطَتانِ بِحَرفِه فِعلاً ─────────
    //
    // **المَقيسُ قَبل**: الصَفُّ أَعلاه يَحسُبُ الشَبَكَةَ بِلامدا مَنسوخَةٍ مِن `Shown`، و`UpgradePromptPriceTests` بِـ`shownOnBilling` — وتَعليقُ تِلكَ
    // يَقولُ «مَربوطَةٌ بِحَرفِه» ولا شَيءَ يَربِطُهُما: تَغييرُ الصَفحَةِ و`ShownExpected` مَعاً يُبقي النُسخَتَينِ عَلى القَديمِ خَضراوَين.

    /// <summary>ما يُخالِفُ «النُسخَتانِ هُما <see cref="ShownExpected"/>» في مَصدَرَي الاختِبارَين — تُقرَأُ لامدا <c>shown</c> هُنا و<c>shownOnBilling</c>
    /// هُناك، وتُطوى مَسافاتُهُما، ويُبَدَّلُ في الثانِيَةِ <c>next.Tier</c> بِـ<c>t.Tier</c> و<c>current.Tier</c> بِـ<c>currentTier</c> — ثُمَّ تُقارَنانِ بِحَرفِه.</summary>
    internal static IReadOnlyList<string> ShownCopyViolations(string billingTests, string upgradeTests)
    {
        static string Fold(string s) => Regex.Replace(s, @"\s+", " ").Trim();
        var v = new List<string>();
        var here = Regex.Matches(billingTests, @"\bshown = \(t, currentTier\) =>(?<e>[^;]+);");
        if (here.Count != 1) v.Add($"billing_copy: {here.Count} نُسخَة");
        else if (Fold(here[0].Groups["e"].Value) is var a && a != ShownExpected) v.Add($"billing_copy_drift: «{a}»");
        var there = Regex.Matches(upgradeTests, @"\bvar shownOnBilling = (?<e>[^;]+);");
        if (there.Count != 1) v.Add($"upgrade_copy: {there.Count} نُسخَة");
        else
        {
            var b = Fold(there[0].Groups["e"].Value).Replace("next.Tier", "t.Tier", StringComparison.Ordinal)
                .Replace("current.Tier", "currentTier", StringComparison.Ordinal);
            if (b != ShownExpected) v.Add($"upgrade_copy_drift: «{b}»");
        }
        return v;
    }

    private static string TestSource(string name)
    {
        var path = Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, "tests", "ACommerce.Platform.Tests", name);
        var text = File.ReadAllText(path);
        Assert.True(text.Length > 1000, $"أَداةٌ عَمياء: {name} لَم يُقرَأ.");
        return text;
    }

    /// <summary><b>نُسخَتا <c>Shown</c> في الاختِباراتِ هُما <see cref="ShownExpected"/> بِحَرفِه</b> — فَتَغييرُ الصَفحَةِ و<see cref="ShownExpected"/> مَعاً
    /// يُحمِرُّ هُنا حَتّى تَتبَعَهُما النُسختان.</summary>
    [Fact]
    public void The_two_test_copies_of_Shown_are_ShownExpected_letter_for_letter()
    {
        var v = ShownCopyViolations(TestSource("StudioBillingPriceTests.cs"), TestSource("UpgradePromptPriceTests.cs"));
        output.WriteLine($"نُسختا Shown المَفحوصَتان: 2 (StudioBillingPriceTests.shown، UpgradePromptPriceTests.shownOnBilling) · مُخالَفات: {v.Count}"
            + (v.Count > 0 ? " — " + string.Join("؛ ", v) : ""));
        Assert.True(v.Count == 0, string.Join("\n", v));
    }

    /// <summary><b>حَقنُ عَيب — رابِطُ النُسختَينِ يُحمِرُّ فِعلاً</b>: نُسخَةٌ بِـ<c>&amp;&amp;</c>، أَو بِـ<c>!=</c>، أَو مَحذوفَة، أَو مُكَرَّرَة — في كُلٍّ مِن المِلَفَّين.</summary>
    [Fact]
    public void The_shown_copy_binder_can_actually_go_red()
    {
        var billing = TestSource("StudioBillingPriceTests.cs");
        var upgrade = TestSource("UpgradePromptPriceTests.cs");
        const string Here = "StudioTierPurchase.IsSelfGrantable(t.Tier) || t.Tier == currentTier;";
        const string There = "var shownOnBilling = StudioTierPurchase.IsSelfGrantable(next.Tier) || next.Tier == current.Tier;";
        string With(string text, string from, string to)
        {
            var at = text.IndexOf(from, StringComparison.Ordinal);
            Assert.True(at >= 0, $"المِرساةُ غائِبَة: {from}");
            return text[..at] + to + text[(at + from.Length)..];
        }
        var lambda = Regex.Match(billing, @"\bshown = \(t, currentTier\) =>\s*" + Regex.Escape(Here));
        Assert.True(lambda.Success, "لامدا shown الحَيَّةُ لَم تُقرَأ — الأَداةُ عَمياء.");
        var rejected = new (string Name, string Billing, string Upgrade)[]
        {
            ("&& هُنا", With(billing, lambda.Value, lambda.Value.Replace("||", "&&", StringComparison.Ordinal)), upgrade),
            ("!= هُنا", With(billing, lambda.Value, lambda.Value.Replace("==", "!=", StringComparison.Ordinal)), upgrade),
            ("بِلا نُسخَةٍ هُنا", With(billing, lambda.Value, "shownGone = (t, currentTier) => true;"), upgrade),
            ("&& هُناك", billing, With(upgrade, There, There.Replace("||", "&&", StringComparison.Ordinal))),
            ("دَرَجَةٌ أُخرى هُناك", billing, With(upgrade, There, There.Replace("next.Tier == current.Tier", "next.Tier == next.Tier", StringComparison.Ordinal))),
            ("نُسخَتانِ هُناك", billing, With(upgrade, There, There + " " + There.Replace("var ", "", StringComparison.Ordinal).Replace("shownOnBilling = ", "var shownOnBilling = ", StringComparison.Ordinal))),
        };
        var wrong = new List<string>();
        if (ShownCopyViolations(billing, upgrade).Count != 0) wrong.Add("رُفِضَ الحَيّ");
        foreach (var (name, b, u) in rejected)
        {
            var v = ShownCopyViolations(b, u);
            output.WriteLine($"«{name}» → {(v.Count == 0 ? "قُبِل" : string.Join("؛ ", v))}");
            if (v.Count == 0) wrong.Add($"قُبِلَ «{name}»");
        }
        output.WriteLine($"حُقِنَ {rejected.Length} عَيباً؛ أَخطَأَ الرابِطُ في {wrong.Count}");
        Assert.True(wrong.Count == 0, string.Join("\n", wrong));
    }

    /// <summary><b>حَقنُ عَيب — فاحِصُ اتِّجاهِ الشَبَكَةِ يُحمِرُّ فِعلاً</b> (‏2026-10-24؛ بِشَكلِ
    /// <c>The_sell_condition_checker_can_actually_go_red</c>): النَصُّ الحَيُّ نَفسُه تُحقَنُ فيه الطَفرَةُ الَّتي مَرَّت
    /// (<c>bool Shown(TierLimits t) =&gt; true;</c>) وأَخَواتُها — عَلى التَعريفِ، وعَلى مَصدَرِ الحَلقَة، وعَلى فَرعِ النَموذَج — ويُشتَرَطُ
    /// رَفضُها، ويُشتَرَطُ قَبولُ الحَيِّ وتَعريفِه مَطوِيّاً عَلى ثَلاثَةِ أَسطُر.</summary>
    [Fact]
    public void The_billing_grid_checker_can_actually_go_red()
    {
        var code = BillingCode();
        const string Def = "=> StudioTierPurchase.IsSelfGrantable(t.Tier) || t.Tier == currentTier;";
        string With(string from, string to)
        {
            Assert.Contains(from, code, StringComparison.Ordinal);
            Assert.Single(Regex.Matches(code, Regex.Escape(from)));
            return code.Replace(from, to, StringComparison.Ordinal);
        }
        var defLine = Regex.Match(code, @"bool\s+Shown\s*\(\s*TierLimits\s+t\s*\)\s*" + Regex.Escape(Def));
        Assert.True(defLine.Success, "تَعريفُ `Shown` الحَيُّ لَم يُقرَأ — الأَداةُ عَمياء.");

        var accepted = new (string Name, string Code)[]
        {
            ("الحَيّ", code),
            ("مَطوِيّاً", With(defLine.Value,
                "bool Shown(TierLimits t)\n        => StudioTierPurchase.IsSelfGrantable(t.Tier)\n        || t.Tier == currentTier;")),
        };
        var rejected = new (string Name, string Code)[]
        {
            ("=> true (طَفرَةُ المُراجِع)", With(defLine.Value, "bool Shown(TierLimits t) => true;")),
            ("!IsSelfGrantable", With(Def, "=> !StudioTierPurchase.IsSelfGrantable(t.Tier) || t.Tier == currentTier;")),
            ("|| true", With(Def, "=> StudioTierPurchase.IsSelfGrantable(t.Tier) || t.Tier == currentTier || true;")),
            ("!= currentTier", With(Def, "=> StudioTierPurchase.IsSelfGrantable(t.Tier) || t.Tier != currentTier;")),
            ("&& بَدَلَ ||", With(Def, "=> StudioTierPurchase.IsSelfGrantable(t.Tier) && t.Tier == currentTier;")),
            ("كُتلَةٌ بَدَلَ تَعبير", With(defLine.Value, "bool Shown(TierLimits t) { return true; }")),
            ("بِلا مُرَشِّح", With("TierCatalog.All.Values.Where(Shown)", "TierCatalog.All.Values")),
            ("مُرَشِّحٌ مَنفِيّ", With("TierCatalog.All.Values.Where(Shown)", "TierCatalog.All.Values.Where(x => !Shown(x))")),
            ("مُرَشِّحٌ صادِقٌ دائِماً", With("TierCatalog.All.Values.Where(Shown)", "TierCatalog.All.Values.Where(_ => true)")),
            ("حَلقَةٌ ثانِيَة", With("<p style=\"margin-top:18px;", "@foreach (var x in TierCatalog.All.Values) { <i>@x.Tier</i> }\n<p style=\"margin-top:18px;")),
            ("isCurrent كاذِبٌ دائِماً", With("var isCurrent = t.Tier == currentTier;", "var isCurrent = false;")),
            ("فَرعٌ مَنفِيّ", With("@if (isCurrent)", "@if (!isCurrent)")),
        };

        var wrong = accepted.Where(x => BillingGridViolations(x.Code).Count != 0)
                .Select(x => $"رُفِضَ حَيٌّ «{x.Name}»: {string.Join("؛ ", BillingGridViolations(x.Code))}")
            .Concat(rejected.Where(x => BillingGridViolations(x.Code).Count == 0).Select(x => $"قُبِلَت طَفرَةٌ «{x.Name}»"))
            .ToList();
        Assert.True(accepted.Length >= 2 && rejected.Length >= 12,
            $"أَداةٌ عَمياء: حُقِنَت {rejected.Length} طَفرَةً و{accepted.Length} حَيّاً.");
        Assert.True(wrong.Count == 0,
            $"حُقِنَت {rejected.Length} طَفرَةً و{accepted.Length} حَيّاً، وأَخطَأَ الفاحِصُ في {wrong.Count}:\n  " + string.Join("\n  ", wrong));
    }
}
