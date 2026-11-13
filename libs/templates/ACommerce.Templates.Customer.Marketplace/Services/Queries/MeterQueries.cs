using System.Data;
using System.Globalization;
using ACommerce.Platform.Shared.Metering;
using Marten;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace ACommerce.Templates.Customer.Marketplace.Services.Queries;

/// <summary>ما انتَهَت إلَيه القِراءَة: أَرقام، أَو «لا دَفتَر» (SQLSTATE ‏42P01)، أَو عُطلٌ سُجِّلَ سَطراً (لا ‏500).</summary>
public enum MeterReadState { Data, NoLedger, Error }

/// <summary>سَطرٌ واحِدٌ لِكُلِّ يَومٍ ومَتجَرٍ ورائِدِ أَعمالٍ ودَورٍ ومِيزَةٍ ومُزَوِّدٍ ووَحدَة — المَجموعُ (حَتّى المُؤَشِّر) مَعَ الذَيلِ الخامّ (بَعدَه)،
/// و<see cref="Tail"/> ما جاءَ مِن الذَيلِ مِنه. و<see cref="Day"/> يَومُ <see cref="MeterSql.DayZone"/> بِصيغَةِ <c>YYYY-MM-DD</c> كَما كَتَبَته القاعِدَة.</summary>
public sealed record MeterCountRow(
    string Day, string App, string Entrepreneur, string Role, string Feature, string Provider, string Unit,
    long Entries, long Quantity, long Tail);

/// <summary>
/// قِراءَةٌ واحِدَةٌ لِصَفحَةٍ في لَقطَةٍ واحِدَة: النافِذَة (<see cref="From"/>–<see cref="To"/>، يَوما <see cref="MeterSql.DayZone"/> بِصيغَةِ <c>YYYY-MM-DD</c>)،
/// وهَل عَمِلَ المُجَمِّع (<see cref="AggregatorRan"/>، ومَتى سَحَبَ آخِرَ مَرَّة)، والسُطور.
/// </summary>
public sealed record MeterReading(
    MeterReadState State, string From, string To, bool AggregatorRan, DateTime? PulledAtUtc, IReadOnlyList<MeterCountRow> Rows)
{
    public static MeterReading Failed(MeterReadState state) => new(state, "", "", false, null, []);

    /// <summary>
    /// <para><b>أَسَطرُ جانِبِ التَشغيلِ هذا؟</b> (‏2026-11-13، مُراجَعَةُ المَرحَلَةِ د الأولى · W13-6·D-R1·١ — M6D-SEC-R1-1 وM6D-CNT-2 وM6D-RULES-R1-1؛ ADR-051 §٢ و§٢٣):
    /// كُلُّ وَحدَةٍ لا يُعلِنُ المُعجَمُ أَنّها حُضورٌ (<see cref="MeterSides.Technology"/>) — ووَحدَةٌ لا يَعرِفُها المُعجَمُ تُعَدُّ مِنه وتُعرَضُ بِرَمزِها ولا تُبتَلَع.</para>
    /// <para><b>ولِماذا هُنا لا في الجُملَة</b>: الجانِبانِ في جَدوَلٍ واحِدٍ (التَصميم §1)، وصَفحَةُ المَتجَرِ سَتَعرِضُ الحُضورَ في قِسمِه مِن القِراءَةِ نَفسِها (البَند ٢٠) — فَالسُطورُ
    /// (<see cref="Rows"/>) تَحمِلُ الجانِبَين، وكُلُّ دالَّةٍ هُنا تَقرَأُ جانِبَ التَشغيلِ وَحدَه بِهذا المِرشَحِ الواحِد. كانَت سَبعٌ مِنها عَمياءَ عَن الجانِب، فَلَمّا كَتَبَت
    /// لَقطَةُ الحُضورِ (<c>6c7cae5</c>) دَخَلَت سُطورُها مَجموعَ التَشغيلِ ولَوحَتَي الدَورِ ورائِدِ الأَعمالِ وقائِمَةَ الفِهرِس.</para>
    /// </summary>
    public static bool IsOperation(string unit) => MeterUnitCatalog.Default.Find(unit)?.Side != MeterSides.Technology;

    /// <summary>سُطورُ جانِبِ التَشغيلِ (<see cref="IsOperation"/>) — ما تَقرَؤُه كُلُّ دالَّةٍ أَدناه.</summary>
    private IEnumerable<MeterCountRow> OperationRows => Rows.Where(r => IsOperation(r.Unit));

    /// <summary>عَدَدُ السُطورِ (العَمَلِيّات — جانِبُ التَشغيلِ وَحدَه) بِشَرطٍ اختِيارِيٍّ عَلى المَتجَرِ والوَحدَةِ واليَوم.</summary>
    public long Entries(string? app = null, string? unit = null, string? day = null)
        => OperationRows.Where(r => (app is null || r.App == app) && (unit is null || r.Unit == unit) && (day is null || r.Day == day)).Sum(r => r.Entries);

    /// <summary>ما جاءَ مِن الذَيلِ الخامّ (فَوقَ المُؤَشِّر) مِن جانِبِ التَشغيل — كُلُّه حينَ لَم يَعمَلِ المُجَمِّع.</summary>
    public long TailEntries(string? app = null) => OperationRows.Where(r => app is null || r.App == app).Sum(r => r.Tail);

    /// <summary>المَتاجِرُ في النافِذَةِ الَّتي لَها سَطرُ تَشغيل، مُرَتَّبَة — فَمَتجَرٌ لا سَطرَ لَه إلّا الحُضورُ لا يُدرَجُ تَحتَ عُنوانِ العَمَلِيّات.</summary>
    public IReadOnlyList<string> Apps() => OperationRows.Select(r => r.App).Distinct().Order(StringComparer.Ordinal).ToList();

    /// <summary>أَيّامُ التَشغيلِ في النافِذَة، الأَحدَثُ أَوَّلاً (<c>YYYY-MM-DD</c> يُرَتَّبُ حَرفِيّاً كَما يُرَتَّبُ زَمَنِيّاً).</summary>
    public IReadOnlyList<string> Days(string? app = null)
        => OperationRows.Where(r => app is null || r.App == app).Select(r => r.Day).Distinct().OrderDescending(StringComparer.Ordinal).ToList();

    /// <summary>
    /// <b>وَحَداتُ جانِبِ التَشغيلِ</b> (العَمَلِيّات) بِعَدَدِها — والجانِبانِ لا يَختَلِطان (ADR-051 §٢): وَحدَةٌ يُعلِنُ المُعجَمُ أَنّها حُضورٌ
    /// (<see cref="MeterSides.Technology"/>) لا تُعَدُّ هُنا أَبَداً. ووَحدَةٌ لا يَعرِفُها المُعجَمُ تُعرَضُ بِرَمزِها ولا تُبتَلَع.
    /// </summary>
    public IReadOnlyList<(string Unit, long Entries)> Operations(string? app = null, string? day = null)
        => OperationRows.Where(r => (app is null || r.App == app) && (day is null || r.Day == day))
               .GroupBy(r => r.Unit, StringComparer.Ordinal)
               .Select(g => (g.Key, g.Sum(r => r.Entries)))
               .OrderBy(u => u.Key, StringComparer.Ordinal).ToList();

    /// <summary>سُطورُ تَشغيلٍ لَم يُعرَف أَحَدُ بُعدَيها (الدَور أَو رائِدُ الأَعمال) — <see cref="MeterSentinels.Unresolved"/>: تُعَدُّ وتُعرَضُ ولا تُخفى.</summary>
    public long Unresolved(string? app = null)
        => OperationRows.Where(r => (app is null || r.App == app)
                                    && (r.Role == MeterSentinels.Unresolved || r.Entrepreneur == MeterSentinels.Unresolved)).Sum(r => r.Entries);

    /// <summary>سُطورُ البَذرِ مِن جانِبِ التَشغيل (<see cref="MeterSentinels.Seed"/>) — تُطبَعُ بَعيداً عَن غَيرِها.</summary>
    public long Seed(string? app = null)
        => OperationRows.Where(r => (app is null || r.App == app) && r.Role == MeterSentinels.Seed).Sum(r => r.Entries);

    /// <summary>بُعدٌ واحِدٌ بِعَدَدِ عَمَلِيّاتِه (رائِدُ الأَعمال، أَو الدَور …) — لِمَتجَرٍ في نافِذَتِه؛ فَدَورُ سَطرِ الحُضورِ (<c>_system</c> أَو الدَورُ المَعدود) لا يَدخُلُه.</summary>
    public IReadOnlyList<(string Value, long Entries)> By(Func<MeterCountRow, string> dimension, string? app = null)
        => OperationRows.Where(r => app is null || r.App == app)
               .GroupBy(dimension, StringComparer.Ordinal)
               .Select(g => (g.Key, g.Sum(r => r.Entries)))
               .OrderByDescending(v => v.Item2).ThenBy(v => v.Key, StringComparer.Ordinal).ToList();
}

/// <summary>
/// <para><b>قارِئُ صَفحَتَي العَدّ</b> (‏2026-11-12، الموجَة ١٣ W13-6·٥ — ADR-051 §١٣؛ التَصميم §11، RULES-12 وCOR-13): <c>/admin/meter</c> و
/// <c>/admin/meter/stores/{slug}</c> خَلفَ <c>RequirePlatformAdmin</c>.</para>
/// <para><b>المَجموعُ حَتّى المُؤَشِّرِ والذَيلُ الخامُّ بَعدَه، داخِلَ نافِذَةِ الصَفحَة</b>: <c>meter.agg_daily</c> لِما جَمَعَه المُجَمِّعُ
/// (<c>(generation, writer_xid, id) ≤ المُؤَشِّر</c>)، و<c>meter.usage_entry</c> لِما فَوقَه — فَالأَرقامُ صادِقَةٌ عَمِلَ المُجَمِّعُ أَم لَم يَعمَل
/// (اليَومَ لا يَعمَلُ عَلى مَساحَةِ HF أَصلاً). والذَيلُ مَحدودٌ بِالنافِذَة (مَتجَرٌ وثَلاثونَ يَوماً، أَو اليَومُ في الفِهرِس) عَلى فِهرِسِ
/// <c>(app, occurred_at)</c>. وبِلا مُؤَشِّرٍ فَالذَيلُ هُوَ الدَفتَرُ كُلُّه في النافِذَة.</para>
/// <para><b>ولَقطَةٌ واحِدَة</b>: المُؤَشِّرُ والمَجاميعُ يُودِعُهُما المُجَمِّعُ مَعاً، فَالقِراءَةُ مُعامَلَةُ <c>REPEATABLE READ</c> لِلقِراءَةِ وَحدَها —
/// المُؤَشِّرُ ثُمَّ السُطورُ في لَقطَتِه — وإلّا عُدَّ سَطرٌ مَرَّتَينِ أَو سَقَطَ بَينَ القِراءَتَين.</para>
/// <para><b>واليَومُ يَومُ المُجَمِّع</b> (<see cref="MeterSql.DayZone"/>): تَحسُبُه القاعِدَةُ وتَكتُبُه <c>YYYY-MM-DD</c> في الفَرعَينِ كَما يَحسُبُ المُجَمِّعُ
/// <c>agg_daily.day</c> — فَالمَجموعُ والذَيلُ في دَلوٍ واحِد. يَومٌ تَقويمِيٌّ لا لَحظَة، فَلا يُحَوَّلُ ثانِيَةً بِمِنطَقَةِ العارِضِ وإلّا افتَرَقَ يَومُ الصَفحَةِ
/// عَن يَومِ المَجموع؛ أَمّا لَحظَةُ آخِرِ سَحبٍ (<c>pulled_at</c>) فَتُرسَمُ بِمِنطَقَةِ الثَقافَة (<c>Culture.FormatTime</c>).</para>
/// <para><b>والأَعطال</b>: <c>42P01</c> (جَدوَلٌ غائِب — قاعِدَةٌ لَم يُقلِع عَلَيها التَطبيقُ بِالدَفتَر) ← «لا بَيانات»؛ وأَيُّ عُطلٍ آخَرَ يُكتَبُ
/// سَطراً واحِداً <c>[meter-page]</c> ويُرسَمُ بِطاقَةَ خَطَأ — لا ‏500 أَبَداً (نَمَطُ <see cref="PlatformBillingQueries"/>، ولا يَبتَلِعُ بِلا أَثَر).</para>
/// <para><b>قِراءَةٌ لا كِتابَة</b>: لا جُملَةَ هُنا تَكتُبُ جَدوَلاً (G10، <c>MeterIndependenceTests</c>)، ولا جَلسَةَ Marten — اتِّصالٌ خامٌّ مِن مَخزَنِه
/// كَما في <c>MeterBoot</c>.</para>
/// </summary>
public sealed class MeterQueries
{
    /// <summary><b>نافِذَةُ صَفحَةِ المَتجَر</b>: ثَلاثونَ يَوماً بِاليَوم (التَصميم §11) — رَقَمٌ تَشغيلِيٌّ لا رَقَمُ مُنتَج: يَحُدُّ الذَيلَ الخامَّ
    /// فَلا تَقرَأُ الصَفحَةُ الدَفتَرَ كُلَّه حينَ لا يَعمَلُ المُجَمِّع.</summary>
    public const int StoreWindowDays = 30;

    /// <summary>SQLSTATE «جَدوَلٌ غائِب» — الوَحيدُ الَّذي يُقرَأُ «لا بَيانات».</summary>
    public const string UndefinedTable = "42P01";

    /// <summary>بادِئَةُ سَطرِ السِجِلِّ عِندَ العُطل — يَقرَؤُها المُشَغِّلُ والبُرهانُ الحَيّ.</summary>
    public const string LogTag = "[meter-page]";

    private const string Ledger = MeterSql.Schema + "." + MeterSql.UsageEntry;
    private const string Daily = MeterSql.Schema + "." + MeterSql.AggDaily;
    private const string Cursor = MeterSql.Schema + "." + MeterSql.AggCursor;
    private const string Zone = MeterSql.DayZone;

    /// <summary>
    /// <para>رَأسُ القِراءَة — أَوَّلُ جُملَةٍ في المُعامَلَةِ فَتُثَبِّتُ لَقطَتَها: اليَومُ بِمِنطَقَةِ المَنَصَّةِ مِن ساعَةِ القاعِدَة (تاريخاً لِبِدايَةِ النافِذَة،
    /// ونَصّاً لِطَرَفَيها: <c>@back</c> أَيّامُ النافِذَةِ إلّا واحِداً)، والمُؤَشِّرُ إن وُجِد.</para>
    /// </summary>
    public const string HeadSql =
        "select t.today, to_char(t.today - @back, 'YYYY-MM-DD'), to_char(t.today, 'YYYY-MM-DD'), c.after_gen, c.after_xid::text, c.after_id, c.pulled_at "
        + "from (select (transaction_timestamp() at time zone '" + Zone + "')::date as today) t left join " + Cursor + " c on c.singleton";

    /// <summary>
    /// السُطور: المَجموعُ في النافِذَة + الذَيلُ الخامُّ فَوقَ المُؤَشِّرِ في النافِذَة، مَجموعَينِ بِالأَبعادِ واليَوم. و<c>{0}</c> شَرطُ المَتجَرِ عَلى
    /// المَجموع و<c>{1}</c> عَلى الذَيل (فارِغانِ في الفِهرِس).
    /// </summary>
    public const string RowsSql =
        "select to_char(day, 'YYYY-MM-DD'), app, entrepreneur, role, feature, provider, unit, sum(entries)::bigint, sum(quantity)::bigint, sum(tail)::bigint from ("
        + "select a.day, a.app, a.entrepreneur, a.role, a.feature, a.provider, a.unit, a.entries, a.quantity, 0::bigint as tail "
        + "from " + Daily + " a where a.day >= @from {0} "
        + "union all "
        + "select (e.occurred_at at time zone '" + Zone + "')::date, e.app, e.entrepreneur, e.role, e.feature, e.provider, e.unit, 1::bigint, e.quantity, 1::bigint "
        + "from " + Ledger + " e where e.occurred_at >= (@from::timestamp at time zone '" + Zone + "') {1} "
        + "and (e.generation, e.writer_xid, e.id) > (@g, @x::xid8, @i)"
        + ") r group by day, app, entrepreneur, role, feature, provider, unit order by day desc, app, unit, entrepreneur, role, feature, provider";

    private readonly IDocumentStore _store;
    private readonly ILogger<MeterQueries> _log;

    public MeterQueries(IDocumentStore store, ILogger<MeterQueries> log)
    {
        _store = store;
        _log = log;
    }

    /// <summary>الفِهرِس: كُلُّ المَتاجِرِ اليَوم.</summary>
    public Task<MeterReading> TodayAsync(CancellationToken ct = default) => ReadAsync(null, 1, "index", ct);

    /// <summary>صَفحَةُ مَتجَر: ثَلاثونَ يَوماً بِاليَوم.</summary>
    public Task<MeterReading> StoreAsync(string app, CancellationToken ct = default) => ReadAsync(app, StoreWindowDays, "store", ct);

    private async Task<MeterReading> ReadAsync(string? app, int days, string page, CancellationToken ct)
    {
        try
        {
            await using var conn = _store.Storage.Database.CreateConnection();
            await conn.OpenAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
            await using (var ro = new NpgsqlCommand("set transaction read only", conn, tx)) await ro.ExecuteNonQueryAsync(ct);

            DateOnly from;
            string fromLabel, toLabel;
            bool ran;
            DateTime? pulledAt;
            (int G, string X, string I) cursor = (0, "0", "");
            await using (var head = new NpgsqlCommand(HeadSql, conn, tx))
            {
                head.Parameters.AddWithValue("back", days - 1);
                await using var r = await head.ExecuteReaderAsync(ct);
                await r.ReadAsync(ct);
                from = r.GetFieldValue<DateOnly>(0).AddDays(-(days - 1));
                fromLabel = r.GetString(1);
                toLabel = r.GetString(2);
                ran = !r.IsDBNull(3);
                pulledAt = ran ? r.GetDateTime(6) : null;
                if (ran) cursor = (r.GetInt32(3), r.GetString(4), r.GetString(5));
            }

            var filter = app is null ? "" : "and a.app = @app";
            var tailFilter = app is null ? "" : "and e.app = @app";
            var rows = new List<MeterCountRow>();
            await using (var cmd = new NpgsqlCommand(string.Format(CultureInfo.InvariantCulture, RowsSql, filter, tailFilter), conn, tx))
            {
                cmd.Parameters.AddWithValue("from", from);
                cmd.Parameters.AddWithValue("g", cursor.G);
                cmd.Parameters.AddWithValue("x", cursor.X);
                cmd.Parameters.AddWithValue("i", cursor.I);
                if (app is not null) cmd.Parameters.AddWithValue("app", app);
                await using var r = await cmd.ExecuteReaderAsync(ct);
                while (await r.ReadAsync(ct))
                    rows.Add(new MeterCountRow(
                        r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4),
                        r.GetString(5), r.GetString(6), r.GetInt64(7), r.GetInt64(8), r.GetInt64(9)));
            }
            await tx.CommitAsync(ct);
            return new MeterReading(MeterReadState.Data, fromLabel, toLabel, ran, pulledAt, rows);
        }
        catch (PostgresException e) when (e.SqlState == UndefinedTable)
        {
            return MeterReading.Failed(MeterReadState.NoLedger);
        }
        catch (Exception e) when (e is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _log.LogError("{Tag} {Page} {App}: {Type} {SqlState} {Message}", LogTag, page, app ?? "*", e.GetType().Name,
                (e as PostgresException)?.SqlState ?? "-", e.Message);
            return MeterReading.Failed(MeterReadState.Error);
        }
    }
}
