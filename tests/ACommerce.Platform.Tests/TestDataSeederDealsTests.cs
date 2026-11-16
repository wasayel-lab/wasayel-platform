using System.Text.RegularExpressions;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ بَذرَةُ الاختِبارِ تُطيعُ كاتالوجَ الصَفَقات (موجزُ الفجوات P3-7) ═══
//
// **المَقيسُ قَبلَ هذا المِلَفّ** (‏2026-10-13): `TestDataSeeder.EnsureSampleDealsAsync` بَذَرَ صَفقَةَ `marketplace` مُكتَمِلَةً
// عِندَ `Received` — ولَيسَت مِن مَراحِلِ نَمَطِها (`DealPatternCatalog`: ‏Offered · Booked · Confirmed · Paid · Shipping ·
// Delivered · Reviewed) — وسِجِلُّها يَقفِزُ `Paid → Delivered` مُتَجاوِزاً `Shipping`، و«assigned» يُحَرِّكُ المَرحَلَةَ
// `Offered → Booked` والكودُ يَكتُبُه بِمَرحَلَةٍ ثابِتَة (`AssignCounterpartyAsync`)، وكُلُّ خُطوَةٍ بِاسمِ المُبادِرِ ولَو كانَ
// فاعِلُها البائِع. (والعَمولَةُ ‏5% الَّتي يَذكُرُها الموجَزُ أُسقِطَت في `ad828fa` — لا `CommissionSar` في البَذرَة، مَقيس.)
//
// **والفَحصُ مِن مَصدَرِ البَذرَة** لِأَنّ المَشروعَ لا يُرَجِّعُ `apps/V1.App` (كَجيرانِه في `DealPaidStageCharacterizationTests`):
// يَقرَأُ كُلَّ `new Deal { … }` في المِلَف، ويَسأَلُ `DealsPolicy` نَفسَها — لا جَدوَلاً مَنسوخاً — عَن كُلِّ خُطوَة. ويَطبَعُ
// عَدَدَ الصَفَقاتِ والخُطُواتِ المَفحوصَةِ ويَفشَلُ عَلى الصِفر (القاعِدَة ١٠).

public class TestDataSeederDealsTests(ITestOutputHelper output)
{
    private sealed record SeededEvent(DealStage Before, DealStage After, string Action, string Actor);
    private sealed record SeededDeal(string Pattern, DealStage Stage, DealStatus Status, IReadOnlyList<SeededEvent> Timeline);

    private static readonly Regex DealBlock = new(@"new Deal\s*\{", RegexOptions.Compiled);
    private static readonly Regex Pattern = new(@"Pattern\s*=\s*""(?<p>[^""]+)""", RegexOptions.Compiled);
    private static readonly Regex Stage = new(@"\bStage\s*=\s*DealStage\.(?<s>\w+)", RegexOptions.Compiled);
    private static readonly Regex Status = new(@"\bStatus\s*=\s*DealStatus\.(?<s>\w+)", RegexOptions.Compiled);
    // ‏الفاعِلُ وَسيطٌ في `Ev(…)` إن حَمَلَه الصانِع، وإلّا فَهُوَ الثابِتُ في جِسمِ الصانِعِ نَفسِه (`new(from, to, action, X.Id, …)`)
    // — فَيُقرَأُ السِجِلُّ القَديمُ والجَديدُ بِالمُحَلِّلِ نَفسِه، ويُقاسُ الفَحصُ عَلى ما كانَ قَبلَ أَن يُقاسَ عَلى ما صار.
    private static readonly Regex Ev = new(
        @"\bEv\(\s*DealStage\.(?<b>\w+)\s*,\s*DealStage\.(?<a>\w+)\s*,\s*""(?<act>\w+)""\s*,(?:\s*(?<who>[A-Za-z_]\w*)\s*,)?\s*\d+\s*\)",
        RegexOptions.Compiled);
    private static readonly Regex EvMaker = new(
        @"DealEvent Ev\((?<params>[^)]*)\)\s*=>\s*new\(\s*from\s*,\s*to\s*,\s*action\s*,\s*(?<id>\w+)\.Id",
        RegexOptions.Compiled);

    private static string Seeder()
    {
        var path = Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, "apps", "V1.App", "Seed", "TestDataSeeder.cs");
        Assert.True(File.Exists(path), $"مَصدَرٌ مَفقود: {path}");
        var text = File.ReadAllText(path);
        Assert.True(text.Length > 2000, $"أَداةٌ عَمياء: {path} لَم يُقرَأ.");
        return text;
    }

    /// <summary>كُتلَةُ <c>{ … }</c> بِمُوازَنَةِ الأَقواس — لا بِعَدَدِ أَسطُرٍ مُخَمَّن.</summary>
    private static string Block(string text, int open)
    {
        var depth = 0;
        for (var i = open; i < text.Length; i++)
        {
            if (text[i] == '{') depth++;
            else if (text[i] == '}' && --depth == 0) return text[open..(i + 1)];
        }
        Assert.Fail("كُتلَةٌ بِلا قَوسٍ مُقابِل.");
        return "";
    }

    private static IReadOnlyList<SeededDeal> SeededDeals()
    {
        var src = Seeder();
        var maker = EvMaker.Match(src);
        Assert.True(maker.Success, "صانِعُ خُطُواتِ السِجِلِّ `Ev` غَيرُ مَقروء — الأَداةُ عَمياء.");
        var fixedActor = maker.Groups["id"].Value;   // ‏`actor` حينَ يَكونُ وَسيطاً، وإلّا اسمُ الطَرَفِ الثابِت
        var deals = new List<SeededDeal>();
        foreach (Match m in DealBlock.Matches(src))
        {
            var block = Block(src, m.Index + m.Length - 1);
            var p = Pattern.Match(block); var st = Stage.Match(block); var su = Status.Match(block);
            Assert.True(p.Success && st.Success && su.Success, $"صَفقَةٌ مَبذورَةٌ بِلا نَمَطٍ أَو مَرحَلَةٍ أَو حالَة:\n{block}");
            var events = Ev.Matches(block).Select(e => new SeededEvent(
                Enum.Parse<DealStage>(e.Groups["b"].Value), Enum.Parse<DealStage>(e.Groups["a"].Value),
                e.Groups["act"].Value, e.Groups["who"].Success ? e.Groups["who"].Value : fixedActor)).ToList();
            Assert.Equal(Regex.Matches(block, @"\bEv\(").Count, events.Count);   // ‏لا خُطوَةَ يَفوتُها المُحَلِّل
            deals.Add(new(p.Groups["p"].Value, Enum.Parse<DealStage>(st.Groups["s"].Value),
                          Enum.Parse<DealStatus>(su.Groups["s"].Value), events));
        }
        return deals;
    }

    /// <summary>
    /// <para><b>كُلُّ صَفقَةٍ مَبذورَةٍ مَرحَلَتُها مِن نَمَطِها، وكُلُّ خُطوَةٍ في سِجِلِّها انتِقالٌ يُجيزُه المُحَرِّك</b>: السِجِلُّ يَبدَأُ
    /// مِن <c>Offered</c> مُتَّصِلاً، و«advanced» = <c>DealsPolicy.Next(pattern, before, payMethod)</c> بِعَينِه وفاعِلُه
    /// <c>DealsPolicy.Actor(after)</c> (‏<c>either</c> يَقبَلُ الطَرَفَين)، و«assigned» لا يُحَرِّكُ المَرحَلَةَ وفاعِلُه الطَرَفُ المُقابِل —
    /// كَما يَكتُبُه <c>AssignCounterpartyAsync</c> — وآخِرُ خُطوَةٍ تَنتَهي بِمَرحَلَةِ الصَفقَة، و<c>Completed</c> حَيثُ — وفَقَط حَيثُ —
    /// بَلَغَت آخِرَ مَراحِلِ نَمَطِها (‏<c>MoveAsync</c>).</para>
    /// </summary>
    [Fact]
    public void Every_seeded_deal_sits_in_its_pattern_and_every_timeline_step_is_a_legal_transition()
    {
        var deals = SeededDeals();
        var steps = deals.Sum(d => d.Timeline.Count);
        output.WriteLine($"صَفَقاتٌ مَبذورَة: {deals.Count} · خُطُواتُ سِجِلّ: {steps}");
        Assert.True(deals.Count >= 2, $"أَداةٌ عَمياء: {deals.Count} صَفقَةً مَقروءَة.");
        Assert.True(steps > 0, "أَداةٌ عَمياء: صِفرُ خُطوَةٍ مَقروءَة.");

        var violations = new List<string>();
        foreach (var d in deals)
        {
            var stages = DealsPolicy.StagesFor(d.Pattern);
            if (!stages.Contains(d.Stage))
                violations.Add($"{d.Pattern}: المَرحَلَةُ {d.Stage} لَيسَت مِن نَمَطِها ({string.Join(" · ", stages)})");

            var at = DealStage.Offered;
            foreach (var e in d.Timeline)
            {
                if (e.Before != at)
                    violations.Add($"{d.Pattern}: خُطوَةٌ مِن {e.Before} والصَفقَةُ عِندَ {at}");
                switch (e.Action)
                {
                    case "assigned":
                        if (e.After != e.Before) violations.Add($"{d.Pattern}: «assigned» يُحَرِّكُ {e.Before} → {e.After}");
                        if (e.Actor != "counterparty") violations.Add($"{d.Pattern}: «assigned» بِاسمِ {e.Actor} لا الطَرَفِ المُقابِل");
                        break;
                    case "advanced":
                        var next = DealsPolicy.Next(d.Pattern, e.Before, payMethod: null);
                        if (next != e.After)
                            violations.Add($"{d.Pattern}: {e.Before} → {e.After} والمُحَرِّكُ يُجيزُ {e.Before} → {next?.ToString() ?? "لا شَيء"}");
                        var actor = DealsPolicy.Actor(e.After);
                        if (actor != "either" && actor != e.Actor)
                            violations.Add($"{d.Pattern}: {e.Before} → {e.After} بِاسمِ {e.Actor} وفاعِلُ {e.After} {actor}");
                        break;
                    default:
                        violations.Add($"{d.Pattern}: فِعلٌ لا تَكتُبُه البَذرَةُ ولا يُفحَص: {e.Action}");
                        break;
                }
                at = e.After;
            }
            if (at != d.Stage)
                violations.Add($"{d.Pattern}: السِجِلُّ يَنتَهي عِندَ {at} والصَفقَةُ عِندَ {d.Stage}");

            var last = stages[^1];
            if ((d.Status == DealStatus.Completed) != (d.Stage == last))
                violations.Add($"{d.Pattern}: الحالَةُ {d.Status} عِندَ {d.Stage} وآخِرُ نَمَطِها {last}");
        }

        foreach (var v in violations) output.WriteLine(v);
        Assert.True(violations.Count == 0,
            $"فُحِصَت {deals.Count} صَفقَةً و{steps} خُطوَة، ومِنها {violations.Count} خَرقاً:\n" + string.Join("\n", violations));
    }
}
