using ACommerce.Platform.Shared.Metering;
using ACommerce.Templates.Customer.Marketplace.Services.Queries;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ تَوصيفٌ: قِراءَةُ صَفحَتَي العَدِّ عَلى جانِبِ التَشغيلِ وَحدَه — ما لا يَتَغَيَّرُ حينَ تَصيرُ القِراءَةُ واعِيَةً بِالجانِب ══════════════════════════════
// (‏2026-11-13، الموجَة ١٣ W13-6·D-R1·١ — مُراجَعَةُ المَرحَلَةِ د: M6D-SEC-R1-1 وM6D-CNT-2 وM6D-RULES-R1-1؛ ADR-051 §٢ و§٢٣؛ التَصميم §1 «the aggregator and
// the pages keep the two apart»، القاعِدَتانِ ٣ و١٣). **لِماذا هذا المِلَفّ**: `MeterReading` يُعطي صَفحَتَي العَدِّ مَجموعَ المَتجَرِ ومَتاجِرَ الفِهرِسِ وأَيّامَه
// ورائِدَ أَعمالِه ودَورَه، وكُلُّها — إلّا `Operations` — تَجمَعُ السُطورَ بِلا نَظَرٍ إلى جانِبِها. وكانَ ذلكَ كامِناً ما دامَ الدَفتَرُ بِلا سَطرِ تِقنِيَة؛ فَلَمّا كَتَبَت
// لَقطَةُ الحُضورِ (`6c7cae5`) سُطورَ `presence.*` في الجَدوَلِ نَفسِه دَخَلَت مَجموعَ التَشغيلِ ولَوحَتَي الدَورِ ورائِدِ الأَعمالِ وقائِمَةَ الفِهرِس.
// **والتَبديلُ التالي يُرَشِّحُ جانِبَ التِقنِيَةِ مِن تِلكَ الدَوالِّ كُلِّها** — وهذا المِلَفُّ يُثَبِّتُ قَبلَه ما يَجِبُ أَلّا يَتَغَيَّر، ولا يُمَسُّ بَعدَه حَرفاً:
//   (أ) قِراءَةٌ سُطورُها كُلُّها مِن جانِبِ التَشغيل (ووَحدَةٌ لا يَعرِفُها المُعجَمُ تُعَدُّ مِنه وتُعرَضُ بِرَمزِها) — كُلُّ دالَّةٍ بِأَرقامِها، كَما تَرسُمُها الصَفحَتانِ اليَوم؛
//   (ب) و`Operations` — الدالَّةُ الوَحيدَةُ الواعِيَةُ بِالجانِب — لا تَتَغَيَّرُ إن أُضيفَت سُطورُ تِقنِيَة.
// ولا يُثَبِّتُ ما يَتَغَيَّرُ عَمداً: المَجموعُ والأَبعادُ والمَتاجِرُ بِوُجودِ سُطورِ التِقنِيَة (صَفُّه في `MeterQueriesTests` و`LiveMeterAdminProofTests` مَعَ التَبديل).
public class MeterReadingOperationsCharacterizationTests(ITestOutputHelper output)
{
    /// <summary>جانِبُ التَشغيلِ وَحدَه: مَتجَرانِ ويَومان، ورائِدا أَعمالٍ والحارِسَتانِ <c>_unresolved</c> و<c>_seed</c>، ودَورانِ ووَحدَتانِ ووَحدَةٌ مَجهولَة، وذَيلٌ ومَجموع.</summary>
    private static readonly MeterCountRow[] OperationRows =
    [
        new("2026-11-13", "a", "e1", "vendor", "deal_status", "_none", "deal.create", 3, 3, 1),
        new("2026-11-13", "a", "e1", "customer", "listings", "_none", "listing.create", 2, 2, 2),
        new("2026-11-13", "a", MeterSentinels.Unresolved, MeterSentinels.Unresolved, "deal_status", "_none", "deal.create", 2, 2, 0),
        new("2026-11-12", "a", MeterSentinels.Seed, MeterSentinels.Seed, "listings", "_none", "listing.create", 4, 4, 0),
        new("2026-11-12", "a", "e1", "vendor", "x_feature", "_none", "zz.unknown", 1, 1, 1),
        new("2026-11-13", "b", "e2", "_none", "deal_status", "_none", "deal.create", 5, 5, 3),
        new("2026-11-11", "b", "e2", "vendor", "tenants", "_none", "store.create", 1, 1, 0),
    ];

    /// <summary>سُطورُ جانِبِ التِقنِيَة (وَحَداتٌ يُعلِنُها المُعجَمُ <see cref="MeterSides.Technology"/>) بِأَبعادِ اللَقطَةِ: الدَورُ <c>_system</c> إلّا سَطرَ الدَور،
    /// ومَتجَرٌ ثالِثٌ <c>idle</c> لا سَطرَ تَشغيلٍ لَه.</summary>
    private static readonly MeterCountRow[] TechnologyRows =
    [
        new("2026-11-13", "a", "e1", MeterSentinels.SystemJob, "tenants", "_none", "presence.store", 1, 1, 1),
        new("2026-11-13", "a", "e1", "vendor", "roles", "_none", "presence.role", 1, 1, 1),
        new("2026-11-13", "a", "e1", MeterSentinels.SystemJob, "auth", "_none", "presence.auth_channel", 1, 1, 1),
        new("2026-11-10", "b", "e2", MeterSentinels.SystemJob, "tenants", "_none", "presence.store", 1, 1, 0),
        new("2026-11-13", "idle", MeterSentinels.Unowned, MeterSentinels.SystemJob, "tenants", "_none", "presence.store", 1, 1, 1),
    ];

    private static MeterReading Reading(IEnumerable<MeterCountRow> rows) => new(MeterReadState.Data, "2026-10-15", "2026-11-13", true, null, rows.ToList());

    private static string Show(IEnumerable<(string, long)> pairs) => string.Join(" · ", pairs.Select(p => $"{p.Item1} {p.Item2}"));

    [Fact]
    public void The_fixture_units_are_on_the_sides_the_lexicon_declares()
    {
        var sides = OperationRows.Concat(TechnologyRows).Select(r => r.Unit).Distinct().Order(StringComparer.Ordinal)
            .Select(u => $"{u}={MeterUnitCatalog.Default.Find(u)?.Side ?? "?"}").ToList();
        output.WriteLine($"[char] وَحَداتُ المِثال {sides.Count}: {string.Join(" · ", sides)}");
        Assert.All(OperationRows, r => Assert.NotEqual(MeterSides.Technology, MeterUnitCatalog.Default.Find(r.Unit)?.Side));
        Assert.All(TechnologyRows, r => Assert.Equal(MeterSides.Technology, MeterUnitCatalog.Default.Find(r.Unit)?.Side));
        Assert.Null(MeterUnitCatalog.Default.Find("zz.unknown"));
        Assert.Equal(7, sides.Count);
    }

    /// <summary>(أ) قِراءَةٌ مِن جانِبِ التَشغيلِ وَحدَه: كُلُّ دالَّةٍ تَقرَؤُها الصَفحَتانِ بِأَرقامِها.</summary>
    [Fact]
    public void An_operations_only_reading_gives_every_page_number_as_it_does_today()
    {
        var r = Reading(OperationRows);
        output.WriteLine($"[char] المَجموع: a {r.Entries("a")} · b {r.Entries("b")} · الكُلّ {r.Entries()} · a/deal.create {r.Entries("a", "deal.create")} · a/2026-11-13 {r.Entries("a", day: "2026-11-13")} · "
                         + $"الذَيل: a {r.TailEntries("a")} · b {r.TailEntries("b")} · الكُلّ {r.TailEntries()}");
        output.WriteLine($"[char] المَتاجِر [{string.Join(", ", r.Apps())}] · أَيّامُ a [{string.Join(", ", r.Days("a"))}] · أَيّامُ الكُلّ [{string.Join(", ", r.Days())}] · "
                         + $"_unresolved a {r.Unresolved("a")} · _seed a {r.Seed("a")} · _unresolved الكُلّ {r.Unresolved()} · _seed الكُلّ {r.Seed()}");
        output.WriteLine($"[char] وَحَداتُ a «{Show(r.Operations("a"))}» · a/2026-11-12 «{Show(r.Operations("a", "2026-11-12"))}» · b «{Show(r.Operations("b"))}»");
        output.WriteLine($"[char] رائِدُ الأَعمال a «{Show(r.By(x => x.Entrepreneur, "a"))}» · الدَور a «{Show(r.By(x => x.Role, "a"))}» · الدَور الكُلّ «{Show(r.By(x => x.Role))}»");

        Assert.Equal(12L, r.Entries("a"));
        Assert.Equal(6L, r.Entries("b"));
        Assert.Equal(18L, r.Entries());
        Assert.Equal(5L, r.Entries("a", "deal.create"));
        Assert.Equal(7L, r.Entries("a", day: "2026-11-13"));
        Assert.Equal(1L, r.Entries("a", "zz.unknown"));
        Assert.Equal(0L, r.Entries("nobody"));
        Assert.Equal(4L, r.TailEntries("a"));
        Assert.Equal(3L, r.TailEntries("b"));
        Assert.Equal(7L, r.TailEntries());
        Assert.Equal(new[] { "a", "b" }, r.Apps());
        Assert.Equal(new[] { "2026-11-13", "2026-11-12" }, r.Days("a"));
        Assert.Equal(new[] { "2026-11-13", "2026-11-12", "2026-11-11" }, r.Days());
        Assert.Equal(2L, r.Unresolved("a"));
        Assert.Equal(4L, r.Seed("a"));
        Assert.Equal(2L, r.Unresolved());
        Assert.Equal(4L, r.Seed());
        Assert.Equal(new[] { ("deal.create", 5L), ("listing.create", 6L), ("zz.unknown", 1L) }, r.Operations("a"));
        Assert.Equal(new[] { ("listing.create", 4L), ("zz.unknown", 1L) }, r.Operations("a", "2026-11-12"));
        Assert.Equal(new[] { ("deal.create", 5L), ("store.create", 1L) }, r.Operations("b"));
        Assert.Equal(new[] { ("e1", 6L), (MeterSentinels.Seed, 4L), (MeterSentinels.Unresolved, 2L) }, r.By(x => x.Entrepreneur, "a"));
        Assert.Equal(new[] { (MeterSentinels.Seed, 4L), ("vendor", 4L), (MeterSentinels.Unresolved, 2L), ("customer", 2L) }, r.By(x => x.Role, "a"));
        Assert.Equal(new[] { (MeterSentinels.None, 5L), ("vendor", 5L), (MeterSentinels.Seed, 4L), (MeterSentinels.Unresolved, 2L), ("customer", 2L) }, r.By(x => x.Role));
        // ‏ومَجموعُ المَتجَرِ = مَجموعُ بَلاطاتِ وَحَداتِه — ما تَرسُمُه صَفحَةُ المَتجَرِ تَحتَ `data-meter-total`.
        Assert.Equal(r.Entries("a"), r.Operations("a").Sum(o => o.Entries));
        Assert.Equal(r.Entries("b"), r.Operations("b").Sum(o => o.Entries));
        Assert.Equal(0L, MeterReading.Failed(MeterReadState.Error).Entries());
        Assert.Empty(MeterReading.Failed(MeterReadState.NoLedger).Apps());
    }

    /// <summary>(ب) <c>Operations</c> واعِيَةٌ بِالجانِبِ مِن قَبل: سُطورُ التِقنِيَةِ لا تُغَيِّرُ ما تُعطيه لِأَيِّ مَتجَرٍ أَو يَوم.</summary>
    [Fact]
    public void Technology_rows_never_change_what_the_operations_tiles_show()
    {
        var plain = Reading(OperationRows);
        var mixed = Reading(OperationRows.Concat(TechnologyRows));
        output.WriteLine($"[char] سُطور: تَشغيل {OperationRows.Length} · تِقنِيَة {TechnologyRows.Length} — وَحَداتُ a «{Show(mixed.Operations("a"))}» · b «{Show(mixed.Operations("b"))}» · "
                         + $"idle «{Show(mixed.Operations("idle"))}» · الكُلّ «{Show(mixed.Operations())}»");
        foreach (var app in new string?[] { "a", "b", "idle", null })
            foreach (var day in new string?[] { "2026-11-13", "2026-11-12", "2026-11-10", null })
                Assert.Equal(plain.Operations(app, day), mixed.Operations(app, day));
        Assert.Empty(mixed.Operations("idle"));
        Assert.DoesNotContain(mixed.Operations(), o => o.Unit.StartsWith("presence.", StringComparison.Ordinal));
    }
}
