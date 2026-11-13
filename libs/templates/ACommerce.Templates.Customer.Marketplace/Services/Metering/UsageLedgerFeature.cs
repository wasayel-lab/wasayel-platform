using ACommerce.Platform.Shared.Metering;
using Weasel.Core;
using Weasel.Core.Migrations;
using Weasel.Postgresql;
using Weasel.Postgresql.Tables;

namespace ACommerce.Templates.Customer.Marketplace.Services.Metering;

/// <summary>
/// <para><b>جَداوِلُ الدَفتَرِ — ميزَةُ Weasel واحِدَة</b> (‏ADR-051، التَصميم §4) في مُخَطَّطِ <see cref="MeterSql.Schema"/>، تُسَجَّلُ في
/// <c>ConfigureMarten</c> القالِب فَيَراها فِعلُ المُخَطَّطِ (<c>scripts/marten-schema.cs</c>) بِنِداءَيه، ويُنشِئُها <c>MeterBoot</c> قَبلَ البَذر،
/// ويَضمَنُها المُستَمِعُ بِنِداءٍ مُخَبَّأٍ لِتَركيبٍ لا يَمُرُّ بِـ<c>Program.cs</c>.</para>
/// <para><b>ويُبنى بِـ<c>Migrator</c> لا بِخِياراتِ المَخزَن</b>: مُنشِئٌ يَأخُذُ خِياراتِ المَخزَنِ مُعامِلاً يَعُدُّه <c>MartenSchemaVerbTests</c> مَوضِعَ إعدادٍ رابِعاً.</para>
/// <para>ثَلاثَةُ جَداوِلَ في البَند ٣: الدَفتَر (<see cref="MeterSql.UsageEntry"/>) بِفِهرِسَي المُؤَشِّرِ والصَفحَة، ونافِذَةُ كُلِّ وَحدَة
/// (<see cref="MeterSql.UnitEpoch"/>)، والجيل (<see cref="MeterSql.Generation"/>). وجَدوَلا المُجَمِّعِ يَلِدانِ مَعَه (البَند ٤)، والنِيَّةُ مَعَ
/// الصادِر (البَند ١١)، ومُطالَبَةُ اللَقطَةِ مَعَ الحُضور (البَند ١٩).</para>
/// <para><b>وجَدوَلا المُجَمِّع</b> (‏2026-11-12، W13-6·٤ — ADR-051 §١٢): المُؤَشِّر (<see cref="MeterSql.AggCursor"/>، صَفٌّ واحِد) والمَجاميع
/// (<see cref="MeterSql.AggDaily"/>، مِفتاحُها <see cref="MeterSql.AggDailyKey"/>). يُعلَنانِ هُنا لا في <c>apps/V1.Meter</c>: المُجَمِّعُ لا يُنشِئُ
/// شَيئاً (التَصميم §4)، فَيُنشِئُهُما إقلاعُ التَطبيقِ ويَراهُما فِعلُ المُخَطَّط؛ ولا يَكتُبُهُما إلّا المُجَمِّع (G10).</para>
/// </summary>
public sealed class UsageLedgerFeature : FeatureSchemaBase
{
    public UsageLedgerFeature(Migrator migrator) : base("UsageLedger", migrator) { }

    public override Type StorageType => typeof(UsageLedgerFeature);

    private static PostgresqlObjectName Name(string table) => new(MeterSql.Schema, table, SchemaUtils.IdentifierUsage.General);

    protected override IEnumerable<ISchemaObject> schemaObjects()
    {
        var entry = new Table(Name(MeterSql.UsageEntry));
        entry.AddColumn("id", "text").AsPrimaryKey();
        entry.AddColumn("unit", "text").NotNull();
        entry.AddColumn("quantity", "bigint").NotNull();
        foreach (var column in new[] { "app", "entrepreneur", "role", "feature", "provider", "source_type", "source_id" })
            entry.AddColumn(column, "text").NotNull();
        entry.AddColumn("occurred_at", "timestamp with time zone").NotNull().DefaultValueByExpression("transaction_timestamp()");
        entry.AddColumn("generation", "integer").NotNull();
        entry.AddColumn("writer_xid", "xid8").NotNull().DefaultValueByExpression("pg_current_xact_id()");
        // ‏مُؤَشِّرُ المُجَمِّع (التَصميم §10.1) ونافِذَةُ الصَفحَة (التَصميم §11).
        entry.Indexes.Add(new IndexDefinition("ix_usage_entry_cursor") { Columns = ["generation", "writer_xid", "id"] });
        entry.Indexes.Add(new IndexDefinition("ix_usage_entry_app_time") { Columns = ["app", "occurred_at"] });
        yield return entry;

        var epoch = new Table(Name(MeterSql.UnitEpoch));
        epoch.AddColumn("unit", "text").AsPrimaryKey();
        epoch.AddColumn("since", "timestamp with time zone").NotNull().DefaultValueByExpression("transaction_timestamp()");
        epoch.AddColumn("since_xid", "xid8").NotNull().DefaultValueByExpression("pg_current_xact_id()");
        yield return epoch;

        var generation = new Table(Name(MeterSql.Generation));
        generation.AddColumn("generation", "integer").AsPrimaryKey();
        generation.AddColumn("started_at", "timestamp with time zone").NotNull().DefaultValueByExpression("transaction_timestamp()");
        generation.AddColumn("start_xid", "xid8").NotNull().DefaultValueByExpression("pg_current_xact_id()");
        yield return generation;

        // ‏مُؤَشِّرُ المُجَمِّع: صَفٌّ واحِدٌ مِفتاحُه `singleton` (لا افتِراضَ لَه — جُملَةُ المُجَمِّعِ تَكتُبُ `true` دائِماً).
        var cursor = new Table(Name(MeterSql.AggCursor));
        cursor.AddColumn("singleton", "boolean").AsPrimaryKey();
        cursor.AddColumn("after_gen", "integer").NotNull();
        cursor.AddColumn("after_xid", "xid8").NotNull();
        cursor.AddColumn("after_id", "text").NotNull();
        cursor.AddColumn("pulled_at", "timestamp with time zone").NotNull().DefaultValueByExpression("transaction_timestamp()");
        yield return cursor;

        // ‏المَجاميعُ لِكُلِّ يَوم (`Asia/Riyadh`) بِالأَبعادِ الخَمسَةِ والوَحدَة — والجانِبانِ لا يَختَلِطانِ لِأَنّ الوَحدَةَ في المِفتاح.
        var daily = new Table(Name(MeterSql.AggDaily));
        daily.AddColumn("day", "date").AsPrimaryKey();
        foreach (var column in MeterSql.AggDailyKey.Skip(1))
            daily.AddColumn(column, "text").AsPrimaryKey();
        daily.AddColumn("entries", "bigint").NotNull();
        daily.AddColumn("quantity", "bigint").NotNull();
        yield return daily;

        // ‏(‏2026-11-12، W13-6·١١ — ADR-051 §١٨؛ التَصميم §4 و§6.2) نِيَّاتُ الصادِر: صَفٌّ قَبلَ كُلِّ نِداءٍ مَعدودٍ بِمُعَرِّفِ سَطرِه، ونَتيجَتُه بَعدَه.
        // يَكتُبُها `UsageLedgerWriter` وَحدَه؛ والمَتجَرُ عَمودٌ لا جُزءٌ مِن المِفتاح (COR-4).
        var intent = new Table(Name(MeterSql.OutboundIntent));
        intent.AddColumn("id", "text").AsPrimaryKey();
        foreach (var column in new[] { "unit", "provider", "app", "request_id", "outcome" })
            intent.AddColumn(column, "text").NotNull();
        intent.AddColumn("created_at", "timestamp with time zone").NotNull().DefaultValueByExpression("transaction_timestamp()");
        intent.AddColumn("decided_at", "timestamp with time zone");
        yield return intent;

        // ‏(‏2026-11-13، W13-6·١٩ — ADR-051 §٢٣؛ التَصميم §4 و§8) مُطالَبَةُ لَقطَةِ الحُضور: صَفٌّ لِكُلِّ يَوم، يُكتَبُ مَعَ سُطورِه و`completed_at` في مُعامَلَةٍ واحِدَة —
        // والمِفتاحُ الفَريدُ يَجعَلُ مُعالِجاً ثانِياً لِليَومِ نَفسِه يَقِفُ ثُمَّ يَخسَر. يَكتُبُها `PresenceSnapshot` وَحدَه.
        var claim = new Table(Name(MeterSql.PresenceClaim));
        claim.AddColumn("day", "date").AsPrimaryKey();
        claim.AddColumn("claimed_at", "timestamp with time zone").NotNull().DefaultValueByExpression("transaction_timestamp()");
        claim.AddColumn("completed_at", "timestamp with time zone");
        yield return claim;

        // ‏(‏2026-11-13، مُراجَعَةُ المَرحَلَةِ د الأولى · W13-6·D-R1·٢ — M6D-CNT-1؛ ADR-051 §٢٣) حامِلُ كُلِّ يَومٍ في الطابور: صَفٌّ يُكتَبُ مَعَ رِسالَتِه المُجَدوَلَةِ في
        // مُعامَلَةٍ واحِدَة، فَلِكُلِّ يَومٍ حامِلٌ واحِدٌ مَهما فَشِلَت لَقطَةُ اليَومِ الَّذي قَبلَه. يَكتُبُه `PresenceSnapshot.CarryAsync` وَحدَه.
        var schedule = new Table(Name(MeterSql.PresenceSchedule));
        schedule.AddColumn("day", "date").AsPrimaryKey();
        schedule.AddColumn("scheduled_at", "timestamp with time zone").NotNull().DefaultValueByExpression("transaction_timestamp()");
        yield return schedule;
    }
}
