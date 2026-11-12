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
    }
}
