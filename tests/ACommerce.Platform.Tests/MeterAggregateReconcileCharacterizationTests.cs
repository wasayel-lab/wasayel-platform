using ACommerce.Templates.Customer.Marketplace.Services.Metering;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ تَوصيفٌ: مُطابَقَةُ المَجاميعِ بِـ(يَوم، مَتجَر، وَحدَة) — ما لا يَتَغَيَّرُ حينَ تَصيرُ بِمَفاتيحِ agg_daily السَبعَة ═══════════════════════════════════
// (‏2026-11-13، الموجَة ١٣ W13-6·E-R2·١ — مُراجَعَةُ إغلاقِ المَرحَلَةِ هـ الثانِيَة: M6E-CNT-R2-1؛ ADR-051 §٢٤؛ التَصميم §9 COR-6d، القاعِدَتانِ ٣ و١٣).
// **لِماذا هذا المِلَفّ**: `MeterReconciliation.CompareAggregates` يُقارِنُ الدَفتَرَ حَتّى المُؤَشِّرِ بِـ`agg_daily` لِكُلِّ (يَوم، مَتجَر، وَحدَة) — وجَدوَلُ
// المَجاميعِ مِفتاحُه سَبعَةُ أَعمِدَة (`MeterSql.AggUpsertRange`: اليَومُ والمَتجَرُ ورائِدُ الأَعمالِ والدَورُ والمِيزَةُ والمُزَوِّدُ والوَحدَة)، فَصَفٌّ مَنسوبٌ إلى
// غَيرِ رائِدِ أَعمالِه أَو دَورِه يُطابِقُ نَظيفاً، وصَفحَةُ رائِدِ الأَعمالِ تَقرَأُ ما فَوقَ المُؤَشِّرِ مِن ذلكَ الصَفّ.
// **والتَبديلُ التالي يُقارِنُ بِالمَفاتيحِ السَبعَة** — وهذا المِلَفُّ يُثَبِّتُ قَبلَه ما يَجِبُ أَلّا يَتَغَيَّر، ولا يُمَسُّ بَعدَه حَرفاً (القاعِدَة ٣):
//   (أ) عَدٌّ لا يَحمِلُ إلّا اليَومَ والمَتجَرَ والوَحدَة: المُتَساوي نَظيف، وكُلُّ فَرقٍ (صَفٌّ ضائِع، صَفٌّ زائِد، كَمِّيَّةٌ تَختَلِف) عَدَمُ تَطابُقٍ واحِدٌ بِيَومِه
//       ومَتجَرِه ووَحدَتِه وعَدَدَيه وكَمِّيَّتَيه، والعَدَمُ مُرَتَّبٌ بِمِفتاحِه نَصّاً (اليَومُ ثُمَّ المَتجَرُ ثُمَّ الوَحدَة، بِفاصِلِها `|`)؛
//   (ب) سَطرُ المَجاميعِ في التَقريرِ وسَطرُ كُلِّ عَدَمِ تَطابُقٍ بِحُروفِهِما، ورَمزُ الخُروج.
// ولا يُثَبِّتُ ما يَتَغَيَّرُ عَمداً: عَدٌّ يَحمِلُ رائِدَ أَعمالٍ أَو دَوراً (صَفُّه في `MeterReconciliationTests` و`LiveMeterReconciliationProofTests` مَعَ التَبديل).
public class MeterAggregateReconcileCharacterizationTests(ITestOutputHelper output)
{
    private static readonly MeterDayCount[] Ledger =
    [
        new("2026-11-13", "a", "deal.create", 3, 3),
        new("2026-11-13", "a", "file.upload", 1, 900),
        new("2026-11-12", "b", "deal.create", 2, 2),
        new("2026-11-13", "ab", "deal.create", 4, 4),
    ];

    private static string Show(IEnumerable<MeterAggregateMismatch> ms)
        => string.Join(" · ", ms.Select(m => $"{m.Day} {m.App} {m.Unit} {m.LedgerEntries}/{m.DailyEntries} {m.LedgerQuantity}/{m.DailyQuantity}"));

    /// <summary>(أ) المُتَساوي نَظيف، وكُلُّ فَرقٍ عَدَمُ تَطابُقٍ واحِدٌ بِيَومِه ومَتجَرِه ووَحدَتِه — مُرَتَّباً.</summary>
    [Fact]
    public void A_day_store_unit_count_reconciles_clean_when_equal_and_names_each_difference_by_its_day_store_and_unit_in_order()
    {
        var clean = MeterReconciliation.CompareAggregates(Ledger, Ledger);
        var lost = MeterReconciliation.CompareAggregates(Ledger, [Ledger[0], Ledger[2], Ledger[3]]);
        var extra = MeterReconciliation.CompareAggregates(Ledger, [.. Ledger, new MeterDayCount("2026-11-11", "c", "store.create", 1, 1)]);
        var bytes = MeterReconciliation.CompareAggregates(Ledger, [Ledger[0], Ledger[1] with { Quantity = 899 }, Ledger[2], Ledger[3]]);
        var entries = MeterReconciliation.CompareAggregates(Ledger, [Ledger[0] with { Entries = 2 }, Ledger[1], Ledger[2], Ledger[3]]);
        var empty = MeterReconciliation.CompareAggregates([], Ledger);
        var none = MeterReconciliation.CompareAggregates(Ledger, []);
        output.WriteLine($"[char·agg] نَظيف {clean.Count} · ضائِع «{Show(lost)}» · زائِد «{Show(extra)}» · كَمِّيَّة «{Show(bytes)}» · عَدَد «{Show(entries)}»");
        output.WriteLine($"[char·agg] دَفتَرٌ فارِغ «{Show(empty)}» — مَجاميعُ فارِغَة «{Show(none)}»");

        Assert.Empty(clean);
        Assert.Equal([new MeterAggregateMismatch("2026-11-13", "a", "file.upload", 1, 0, 900, 0)], lost);
        Assert.Equal([new MeterAggregateMismatch("2026-11-11", "c", "store.create", 0, 1, 0, 1)], extra);
        Assert.Equal([new MeterAggregateMismatch("2026-11-13", "a", "file.upload", 1, 1, 900, 899)], bytes);
        Assert.Equal([new MeterAggregateMismatch("2026-11-13", "a", "deal.create", 3, 2, 3, 3)], entries);
        Assert.Equal(
            [
                new MeterAggregateMismatch("2026-11-12", "b", "deal.create", 0, 2, 0, 2),
                new MeterAggregateMismatch("2026-11-13", "ab", "deal.create", 0, 4, 0, 4),     // ‏تَرتيبُ المِفتاحِ نَصّاً: «ab» قَبلَ «a|»
                new MeterAggregateMismatch("2026-11-13", "a", "deal.create", 0, 3, 0, 3),
                new MeterAggregateMismatch("2026-11-13", "a", "file.upload", 0, 1, 0, 900),
            ],
            empty);
        Assert.Equal(empty.Select(m => (m.Day, m.App, m.Unit)), none.Select(m => (m.Day, m.App, m.Unit)));
        Assert.All(none, m => Assert.Equal((0L, 0L), (m.DailyEntries, m.DailyQuantity)));
    }

    private static MeterReconciliationReport Report(string? cursor, int rows, IReadOnlyList<MeterAggregateMismatch> mismatches)
        => new(MeterReconcileState.Data, null,
            [new MeterUnitCheck("deal.create", "deal_ids", true, 3, 0, 0, [], 0, 0, 0, [], [])], [],
            cursor, rows, mismatches, new MeterPresenceCheck(1, 1, [], 0), [], [], 41);

    /// <summary>(ب) سَطرُ المَجاميعِ وسَطرُ كُلِّ عَدَمِ تَطابُقٍ بِحُروفِهِما، ورَمزُ الخُروج — والعَيِّناتُ خَمسٌ لا كُلُّها.</summary>
    [Fact]
    public void The_report_prints_the_aggregate_line_and_each_mismatch_by_its_day_store_and_unit_and_a_mismatch_exits_1()
    {
        var tag = MeterReconciliation.LogTag;
        var clean = Report("1|5|x", 4, []);
        var absent = Report(null, 0, []);
        var six = Enumerable.Range(1, 6).Select(i => new MeterAggregateMismatch($"2026-11-0{i}", "a", "deal.create", i, 0, i, 0)).ToList();
        var red = Report("1|5|x", 4, [new MeterAggregateMismatch("2026-11-13", "a", "file.upload", 1, 0, 900, 0), .. six]);
        var lines = red.Lines();
        output.WriteLine($"[char·agg] {string.Join(" | ", lines.Where(l => l.Contains("aggregates", StringComparison.Ordinal) || l.Contains("mismatch", StringComparison.Ordinal)))}");

        Assert.Contains($"{tag} aggregates · cursor 1|5|x · rows 4 · mismatches 0", clean.Lines());
        Assert.Equal((0, 0), (clean.ExitCode, clean.Defects));
        Assert.Contains($"{tag} aggregates · cursor (none) · rows 0 · mismatches 0", absent.Lines());
        Assert.Equal(0, absent.ExitCode);

        Assert.Contains($"{tag} aggregates · cursor 1|5|x · rows 4 · mismatches 7", lines);
        Assert.Equal(
            [
                $"{tag}   mismatch 2026-11-13 a file.upload · ledger 1/900 · agg_daily 0/0",
                $"{tag}   mismatch 2026-11-01 a deal.create · ledger 1/1 · agg_daily 0/0",
                $"{tag}   mismatch 2026-11-02 a deal.create · ledger 2/2 · agg_daily 0/0",
                $"{tag}   mismatch 2026-11-03 a deal.create · ledger 3/3 · agg_daily 0/0",
                $"{tag}   mismatch 2026-11-04 a deal.create · ledger 4/4 · agg_daily 0/0",
            ],
            lines.Where(l => l.StartsWith($"{tag}   mismatch ", StringComparison.Ordinal)));
        Assert.Equal((1, 7), (red.ExitCode, red.Defects));
        Assert.Equal($"{tag} result · sources compared 3 · defects 7 → exit 1", lines[^1]);
    }
}
