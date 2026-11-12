using System.Text.RegularExpressions;
using ACommerce.Platform.Shared.Metering;
using ACommerce.Templates.Customer.Marketplace.Services.Queries;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ قارِئُ صَفحَتَي العَدّ: المَجموعُ حَتّى المُؤَشِّرِ والذَيلُ الخامُّ بَعدَه، داخِلَ نافِذَةِ الصَفحَة ═══════════════════════════════════════════
// (‏2026-11-12، الموجَة ١٣ W13-6·٥ — ADR-051 §١٣؛ التَصميم §11، RULES-12 وCOR-13). **الأَرقامُ صادِقَةٌ عَمِلَ المُجَمِّعُ أَم لَم يَعمَل** (لا يَعمَلُ
// عَلى مَساحَةِ HF أَصلاً): فَالحَدُّ بَينَ المَجموعِ والذَيلِ هُوَ مُؤَشِّرُ المُجَمِّعِ بِعَينِه `(generation, writer_xid, id)` — بِالجُملَةِ نَفسِها الَّتي
// يَسحَبُ بِها (`MeterSql.PageKeys`) — فَلا سَطرَ يُعَدُّ مَرَّتَينِ ولا يَسقُطُ بَينَهُما؛ والذَيلُ مَحدودٌ بِنافِذَةِ الصَفحَةِ فَلا تَقرَأُ الصَفحَةُ الدَفتَرَ كُلَّه؛
// واليَومُ يَومُ المُجَمِّعِ (`MeterSql.DayZone`) في الفَرعَين. وهُنا شَكلُ الجُمَلِ وما يُشتَقُّ مِن القِراءَة؛ والتَطابُقُ عَلى الدَفتَرِ الحَقيقِيِّ (المَجموعُ + الذَيلُ
// = عَدُّ السُطورِ في النافِذَة، والنافِذَةُ بِحافَّتَيها) في `LiveMeterAdminProofTests`.
public class MeterQueriesTests(ITestOutputHelper output)
{
    /// <summary>فَرعُ الذَيلِ مِن جُملَةِ السُطور — ما بَعدَ <c>union all</c>.</summary>
    private static string TailBranch()
    {
        var at = MeterQueries.RowsSql.IndexOf("union all", StringComparison.Ordinal);
        Assert.True(at > 0, "جُملَةُ السُطورِ بِلا فَرعَين — الأَداةُ عَمياء.");
        return MeterQueries.RowsSql[at..];
    }

    private static string AggBranch() => MeterQueries.RowsSql[..MeterQueries.RowsSql.IndexOf("union all", StringComparison.Ordinal)];

    [Fact]
    public void The_tail_starts_at_the_aggregator_cursor_and_is_bounded_by_the_page_window()
    {
        var tail = TailBranch();
        var agg = AggBranch();
        // ‏المُؤَشِّرُ نَفسُه: المَقطَعُ الَّذي يَسحَبُ بِه المُجَمِّعُ (`PageKeys`) حَرفاً — فَما تَحتَه في المَجموع وما فَوقَه في الذَيل.
        const string keyset = "(e.generation, e.writer_xid, e.id) > (@g, @x::xid8, @i)";
        var window = $"e.occurred_at >= (@from::timestamp at time zone '{MeterSql.DayZone}')";
        output.WriteLine($"[queries] المُؤَشِّرُ في جُملَةِ المُجَمِّع: {MeterSql.PageKeys.Contains(keyset, StringComparison.Ordinal)} · في الذَيل: {tail.Contains(keyset, StringComparison.Ordinal)} · "
                         + $"نافِذَةُ الذَيل: {tail.Contains(window, StringComparison.Ordinal)} · نافِذَةُ المَجموع: {agg.Contains("a.day >= @from", StringComparison.Ordinal)} · "
                         + $"نافِذَةُ المَتجَر {MeterQueries.StoreWindowDays} يَوماً");
        Assert.Contains(keyset, MeterSql.PageKeys, StringComparison.Ordinal);
        Assert.Contains(keyset, tail, StringComparison.Ordinal);
        Assert.Contains(window, tail, StringComparison.Ordinal);
        Assert.Contains("from " + MeterSql.Schema + "." + MeterSql.UsageEntry + " e where", tail, StringComparison.Ordinal);
        Assert.Contains("a.day >= @from", agg, StringComparison.Ordinal);
        Assert.Contains("from " + MeterSql.Schema + "." + MeterSql.AggDaily + " a where", agg, StringComparison.Ordinal);
        // ‏ومَتجَرُ الصَفحَةِ عَلى الفَرعَينِ مَعاً (والفِهرِسُ بِلا شَرط): `{0}` لِلمَجموع و`{1}` لِلذَيل.
        Assert.Contains("{0}", agg, StringComparison.Ordinal);
        Assert.Contains("{1}", tail, StringComparison.Ordinal);
        Assert.Equal(30, MeterQueries.StoreWindowDays);
    }

    /// <summary><b>واليَومُ يَومُ المُجَمِّعِ في الفَرعَين</b>، تَكتُبُه القاعِدَةُ <c>YYYY-MM-DD</c> — يَومٌ تَقويمِيٌّ لا لَحظَةٌ تُحَوَّلُ بِمِنطَقَةِ العارِض
    /// (فَلا راسِمَ وَقتٍ في C# لِلأَيّام، و<c>DisplayTimeZoneTests</c> لا يَرى إلّا <c>Culture.FormatTime</c> لِلَحظَةِ آخِرِ سَحب).</summary>
    [Fact]
    public void The_day_of_both_branches_is_the_aggregator_day_written_by_the_database()
    {
        var zoned = Regex.Matches(MeterQueries.RowsSql + MeterQueries.HeadSql, Regex.Escape($"at time zone '{MeterSql.DayZone}'")).Count;
        output.WriteLine($"[queries] مَواضِعُ يَومِ {MeterSql.DayZone} في جُمَلِ القارِئ: {zoned} · وفي جُملَةِ المُجَمِّع: {Regex.Matches(MeterSql.AggUpsertRange, Regex.Escape($"at time zone '{MeterSql.DayZone}'")).Count} · "
                         + $"تَسمِياتُ اليَومِ مِن القاعِدَة: {Regex.Matches(MeterQueries.RowsSql + MeterQueries.HeadSql, Regex.Escape("'YYYY-MM-DD')")).Count}");
        Assert.Equal(3, zoned);   // ‏اليَومُ الجاري، ويَومُ سَطرِ الذَيل، وبِدايَةُ نافِذَتِه
        Assert.Contains($"(e.occurred_at at time zone '{MeterSql.DayZone}')::date", MeterSql.AggUpsertRange, StringComparison.Ordinal);
        Assert.Contains($"(e.occurred_at at time zone '{MeterSql.DayZone}')::date", MeterQueries.RowsSql, StringComparison.Ordinal);
        Assert.StartsWith("select to_char(day, 'YYYY-MM-DD'), ", MeterQueries.RowsSql, StringComparison.Ordinal);
        Assert.Contains("to_char(t.today - @back, 'YYYY-MM-DD'), to_char(t.today, 'YYYY-MM-DD')", MeterQueries.HeadSql, StringComparison.Ordinal);
    }

    /// <summary><b>القارِئُ لا يَكتُب</b>: لا جُملَةَ كِتابَةٍ في جُمَلِه، والمُعامَلَةُ لِلقِراءَةِ وَحدَها بِلَقطَةٍ واحِدَة (المُؤَشِّرُ ثُمَّ السُطور) — والمُجَمِّعُ
    /// يُودِعُ المَجاميعَ والمُؤَشِّرَ مَعاً، فَقِراءَتانِ في لَقطَتَينِ تَعُدّانِ سَطراً مَرَّتَينِ أَو تُسقِطانِه.</summary>
    [Fact]
    public void The_reader_writes_nothing_and_reads_one_snapshot()
    {
        var sql = (MeterQueries.HeadSql + " " + MeterQueries.RowsSql).ToLowerInvariant();
        var writes = Regex.Matches(sql, @"\b(insert|update|delete|truncate|alter|drop|create)\b").Select(m => m.Value).ToList();
        var source = File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, "libs", "templates", "ACommerce.Templates.Customer.Marketplace",
            "Services", "Queries", "MeterQueries.cs"));
        output.WriteLine($"[queries] جُمَلُ كِتابَةٍ في القارِئ {writes.Count} · REPEATABLE READ {source.Contains("IsolationLevel.RepeatableRead", StringComparison.Ordinal)} · "
                         + $"read only {source.Contains("\"set transaction read only\"", StringComparison.Ordinal)} · جَلسَةُ Marten {source.Contains("Session(", StringComparison.Ordinal)}");
        Assert.Empty(writes);
        Assert.Contains("IsolationLevel.RepeatableRead", source, StringComparison.Ordinal);
        Assert.Contains("\"set transaction read only\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Session(", source, StringComparison.Ordinal);
        Assert.Contains($"from (select (transaction_timestamp() at time zone '{MeterSql.DayZone}')::date as today) t", MeterQueries.HeadSql, StringComparison.Ordinal);
        Assert.Contains("left join " + MeterSql.Schema + "." + MeterSql.AggCursor + " c on c.singleton", MeterQueries.HeadSql, StringComparison.Ordinal);
    }

    /// <summary><b>الأَعطالُ</b>: <c>42P01</c> وَحدَه «لا بَيانات»، وغَيرُه سَطرٌ بِبادِئَتِه وبِطاقَة — والإلغاءُ المَطلوبُ وَحدَه يَمُرّ.</summary>
    [Fact]
    public void Only_an_undefined_table_reads_as_no_data_and_any_other_failure_is_one_tagged_line()
    {
        var source = File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, "libs", "templates", "ACommerce.Templates.Customer.Marketplace",
            "Services", "Queries", "MeterQueries.cs"));
        var catches = Regex.Matches(source, @"catch \((?<t>\w+) e\) when \((?<w>[^\n]*)\)").Select(m => $"{m.Groups["t"].Value} when {m.Groups["w"].Value}").ToList();
        output.WriteLine($"[queries] مَصائِدُ {catches.Count}: {string.Join(" | ", catches)}");
        Assert.Equal("42P01", MeterQueries.UndefinedTable);
        Assert.Equal("[meter-page]", MeterQueries.LogTag);
        Assert.Equal(2, catches.Count);
        Assert.Equal("PostgresException when e.SqlState == UndefinedTable", catches[0]);
        Assert.Equal("Exception when e is not OperationCanceledException || !ct.IsCancellationRequested", catches[1]);
        Assert.Contains("return MeterReading.Failed(MeterReadState.NoLedger);", source, StringComparison.Ordinal);
        Assert.Contains("_log.LogError(\"{Tag} {Page} {App}: {Type} {SqlState} {Message}\", LogTag,", source, StringComparison.Ordinal);
        Assert.Contains("return MeterReading.Failed(MeterReadState.Error);", source, StringComparison.Ordinal);
    }

    private static MeterReading Sample() => new(MeterReadState.Data, "2026-10-14", "2026-11-12", true, null,
    [
        new("2026-11-12", "a", "e1", "_none", "deal_status", "_none", "deal.create", 3, 3, 1),
        new("2026-11-12", "a", MeterSentinels.Unresolved, MeterSentinels.Unresolved, "deal_status", "_none", "deal.create", 2, 2, 2),
        new("2026-11-11", "a", MeterSentinels.Seed, MeterSentinels.Seed, "deal_status", "_none", "deal.create", 4, 4, 0),
        new("2026-11-12", "a", "e1", "_none", "x_feature", "_none", "zz.unknown", 1, 1, 1),
        new("2026-11-12", "b", "e2", "_none", "deal_status", "_none", "deal.create", 5, 5, 0),
    ]);

    /// <summary><b>ما يُشتَقُّ مِن القِراءَة</b>: العَدُّ بِشُروطِه، والذَيل، والوَحدَةُ المَجهولَةُ تُعرَضُ بِرَمزِها ولا تُبتَلَع، و<c>_unresolved</c> و<c>_seed</c>
    /// مَعدودانِ لا مَخفِيّان، والأَيّامُ الأَحدَثُ أَوَّلاً.</summary>
    [Fact]
    public void The_reading_counts_by_store_unit_and_day_and_shows_unknown_units_and_sentinels()
    {
        var r = Sample();
        output.WriteLine($"[queries] a: {r.Entries("a")} (ذَيل {r.TailEntries("a")}) · b: {r.Entries("b")} · وَحَداتُ a: "
                         + string.Join(" · ", r.Operations("a").Select(o => $"{o.Unit} {o.Entries}")) + $" · _unresolved {r.Unresolved("a")} · _seed {r.Seed("a")}");
        Assert.Equal(10L, r.Entries("a"));
        Assert.Equal(5L, r.Entries("b"));
        Assert.Equal(15L, r.Entries());
        Assert.Equal(6L, r.Entries("a", day: "2026-11-12"));
        Assert.Equal(9L, r.Entries("a", "deal.create"));
        Assert.Equal(4L, r.TailEntries("a"));
        Assert.Equal(new[] { ("deal.create", 9L), ("zz.unknown", 1L) }, r.Operations("a"));
        Assert.Equal(new[] { ("deal.create", 5L), ("zz.unknown", 1L) }, r.Operations("a", "2026-11-12"));
        Assert.Equal(2L, r.Unresolved("a"));
        Assert.Equal(4L, r.Seed("a"));
        Assert.Equal(new[] { "a", "b" }, r.Apps());
        Assert.Equal(new[] { "2026-11-12", "2026-11-11" }, r.Days("a"));
        Assert.Equal(new[] { (MeterSentinels.Seed, 4L), ("e1", 4L), (MeterSentinels.Unresolved, 2L) }, r.By(x => x.Entrepreneur, "a"));
        Assert.Equal(0L, MeterReading.Failed(MeterReadState.Error).Entries());
    }

    /// <summary><b>ولا عُضوَ في قارِئِ العَدِّ يُسَمّي سِعراً</b> (القاعِدَة ١٦ — هذه المَوجَةُ عَدٌّ وَحدَه؛ الحارِسُ G2 نَفسُه بِمُعجَمِه عَلى أَنواعِ القارِئ).</summary>
    [Fact]
    public void No_member_of_the_meter_reader_names_a_price()
    {
        var types = new[] { typeof(MeterQueries), typeof(MeterReading), typeof(MeterCountRow), typeof(MeterReadState) };
        var names = types.SelectMany(t => t.GetMembers().Select(m => m.Name)).Concat(types.Select(t => t.Name)).Distinct().ToList();
        var priced = names.Where(n => MeterUnitCatalogTests.PriceIsh.IsMatch(n)).ToList();
        output.WriteLine($"[queries] أَنواعٌ مَفحوصَة {types.Length} · أَسماء {names.Count} · تُسَمّي سِعراً {priced.Count}");
        Assert.True(names.Count > 20, $"أَداةٌ عَمياء: {names.Count} اسماً.");
        Assert.Empty(priced);
    }
}
