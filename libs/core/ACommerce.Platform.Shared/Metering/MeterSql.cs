namespace ACommerce.Platform.Shared.Metering;

/// <summary>
/// <para><b>عَقدُ الجَدوَلِ الواحِد</b> — أَسماءُ المُخَطَّطِ والجَداوِلِ والأَعمِدَةِ وجُمَلُ الكِتابَة، مَكتوبَةً مَرَّةً يَقرَؤُها الكاتِبون
/// (مُستَمِعُ الحِفظ، والصادِرُ واللَقطاتُ لاحِقاً) والإقلاعُ والمُجَمِّع (البَند ٤). والجَداوِلُ نَفسُها تُعلَنُ في ميزَةِ Weasel
/// (<c>UsageLedgerFeature</c> في القالِب) بِهذه الأَسماءِ لا بِنُسَخٍ مِنها.</para>
/// <para><b>جَداوِلُ خامٌ في مُخَطَّطِ <c>meter</c> لا وَثائِقُ Marten</b> (التَصميم §4، مَقيس D1–D4): وَثيقَةٌ مُقتَرِنَةٌ تَأخُذُ مُستَأجِرَ الجَلسَةِ لا
/// المَتجَر، ووَثيقَةٌ وَحيدَةُ الإيجارِ لا تَحمِلُ عَموداً افتِراضُه مُعَرِّفُ المُعامَلَةِ الكاتِبَة، وحَدَثٌ يُلحِقُه المُستَمِعُ يَسقُطُ صامِتاً.</para>
/// </summary>
public static class MeterSql
{
    public const string Schema = "meter";

    /// <summary>الدَفتَر — سَطرٌ لِكُلِّ عَمَلِيَّة (أَو حُضور)، لا يَتَغَيَّر.</summary>
    public const string UsageEntry = "usage_entry";
    /// <summary>نافِذَةُ مُطابَقَةِ كُلِّ وَحدَة: أَوَّلُ إقلاعٍ عَرَفَها (التَصميم §9).</summary>
    public const string UnitEpoch = "unit_epoch";
    /// <summary>جيلُ العُنقود (التَصميم §10.1): يُفتَحُ جَديدٌ حينَ يَرى الإقلاعُ مُعَرِّفَ مُعامَلَةٍ في الجيلِ الجاري فَوقَ العُنقود (استِعادَة).</summary>
    public const string Generation = "generation";

    /// <summary>أَعمِدَةُ الدَفتَرِ بِتَرتيبِها — والثَلاثَةُ الأَخيرَةُ لا يُمَرِّرُها الكاتِبُ إلّا الجيل.</summary>
    public static readonly IReadOnlyList<string> UsageEntryColumns =
    [
        "id", "unit", "quantity", "app", "entrepreneur", "role", "feature", "provider", "source_type", "source_id",
        "occurred_at", "generation", "writer_xid",
    ];

    /// <summary>
    /// <para><b>كِتابَةُ السَطر</b> — بِعَلاماتِ <c>?</c> الَّتي يَقرَؤُها <c>QueueSqlCommand</c>، بِتَرتيبِ <see cref="UsageEntry"/> ثُمَّ الجيل.
    /// <c>ON CONFLICT (id) DO NOTHING</c>: الإعادَةُ بِالمُعَرِّفِ نَفسِه لا تُفشِلُ الحِفظَ ولا تُضاعِفُ العَدّ (D4).</para>
    /// <para><b>والجيلُ</b> ما خَبَّأَه الإقلاعُ (<c>MeterBoot</c>)؛ و<c>0</c> «لَم يُقلِعِ التَطبيقُ عَلى هذا المَخزَن» (تَركيبٌ لا يَمُرُّ بِـ<c>Program.cs</c>:
    /// الاختِباراتُ والمُضيفُ المُصَغَّر) — فَيُؤخَذُ آخِرُ جيلٍ مَفتوح، أَو 1 عَلى قاعِدَةٍ لَم يُفتَح فيها جيل (وهُوَ ما يَفتَحُه الإقلاعُ الأَوَّل).
    /// <c>COALESCE</c> لا يُقَيِّمُ ما بَعدَ أَوَّلِ قيمَة، فَالجيلُ المُخَبَّأُ لا يَقرَأُ الجَدوَل.</para>
    /// </summary>
    public const string Insert =
        "insert into meter.usage_entry (id, unit, quantity, app, entrepreneur, role, feature, provider, source_type, source_id, generation) "
        + "values (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, coalesce(nullif(?, 0), (select max(g.generation) from meter.generation g), 1)) "
        + "on conflict (id) do nothing";

    /// <summary>قُفلُ الإقلاعِ داخِلَ مُعامَلَتِه — نُسخَتانِ تُقلِعانِ مَعاً لا تَفتَحانِ جيلَين.</summary>
    public const string BootLock = "select pg_advisory_xact_lock(hashtext('meter.boot'))";

    /// <summary>الجيلُ الجاري، أَو <c>null</c> عَلى دَفتَرٍ لَم يُفتَح فيه جيل.</summary>
    public const string CurrentGeneration = "select max(generation) from meter.generation";

    /// <summary><b>كاشِفُ التَراجُع</b> (R3/R4): سَطرٌ في الجيلِ الجاري مُعَرِّفُ مُعامَلَتِه فَوقَ العُنقودِ = قاعِدَةٌ استُعيدَت في عُنقودٍ جَديد.</summary>
    public const string RegressionInGeneration =
        "select exists(select 1 from meter.usage_entry e where e.generation = @g and e.writer_xid > pg_current_xact_id())";

    /// <summary>يَفتَحُ جيلاً — وقِيَمُ بِدايَتِه افتِراضُ القاعِدَة.</summary>
    public const string OpenGeneration = "insert into meter.generation (generation) values (@g)";

    /// <summary>نافِذَةُ وَحدَةٍ يَعرِفُها هذا الإقلاع — مَرَّةً واحِدَة؛ والصَفُّ القائِمُ لا يُمَسّ.</summary>
    public const string EnsureEpoch = "insert into meter.unit_epoch (unit) values (@u) on conflict (unit) do nothing";

    // ═══ المُجَمِّعُ المُستَقِلّ (‏2026-11-12، W13-6·٤ — ADR-051 §١٢؛ التَصميم §10) ═══════════════════════════════════════════════════
    // جَدوَلاهُ يُعلَنانِ في الميزَةِ نَفسِها فَيُنشِئُهُما إقلاعُ التَطبيق — **والمُجَمِّعُ لا يُنشِئُ شَيئاً**. وجُمَلُ كِتابَتِهِما الثَلاث
    // (`AggUpsertRange` و`AggCursorSet` و`AggRebuild`) لا يُناديها إلّا `apps/V1.Meter` — يَحرُسُه `MeterIndependenceTests` (G10).

    /// <summary>مَجاميعُ المُجَمِّعِ لِكُلِّ يَومٍ ومَتجَرٍ ورائِدِ أَعمالٍ ودَورٍ ومِيزَةٍ ومُزَوِّدٍ ووَحدَة.</summary>
    public const string AggDaily = "agg_daily";
    /// <summary>مُؤَشِّرُ المُجَمِّعِ — صَفٌّ واحِد: آخِرُ <c>(generation, writer_xid, id)</c> جُمِعَ.</summary>
    public const string AggCursor = "agg_cursor";

    /// <summary>مِفتاحُ <see cref="AggDaily"/> بِتَرتيبِه — وما سِواه عَمودانِ يَتَراكَمان: <c>entries</c> و<c>quantity</c>.</summary>
    public static readonly IReadOnlyList<string> AggDailyKey = ["day", "app", "entrepreneur", "role", "feature", "provider", "unit"];

    /// <summary><b>حَدُّ اليَوم</b> (س٥ في ADR-051 §٩): <c>Asia/Riyadh</c> — وهُوَ <c>CultureTime.DefaultTimeZone</c> (‏<c>ACommerce.Kit.Culture</c>) بِحَرفِه. والمُجَمِّعُ لا يُشيرُ إلى
    /// عُدَّةِ الثَقافَة (G10: ‏Shared وNpgsql وَحدَهُما)، فَالتَطابُقُ يَحرُسُه اختِبار؛ والسُطورُ بِوَقتِ UTC فَإعادَةُ البِناءِ تُعيدُ التَقسيمَ إن تَغَيَّر.</summary>
    public const string DayZone = "Asia/Riyadh";

    /// <summary>اسمُ القُفلِ الاستِشارِيِّ لِلجَلسَة: مُجَمِّعٌ واحِدٌ لِكُلِّ قاعِدَة (COR-9).</summary>
    public const string AggregatorLockName = "meter.aggregator";

    /// <summary>اسمُ تَطبيقِ اتِّصالاتِ المُجَمِّعِ في <c>pg_stat_activity</c> — يَقرَؤُه المُشَغِّلُ والبُرهانُ الحَيّ.</summary>
    public const string AggregatorApplicationName = "wasayel-meter-agg";

    /// <summary>جَداوِلُ الدَفتَرِ الغائِبَة مِن الأَربَعَةِ الَّتي يَقرَؤُها المُجَمِّع — صَفٌّ لِكُلِّ غائِب، وصِفرٌ = كُلُّها قائِمَة.</summary>
    public const string LedgerTablesMissing =
        "select n from unnest(array['meter.usage_entry', 'meter.generation', 'meter.agg_daily', 'meter.agg_cursor']) as n where to_regclass(n) is null order by n";

    /// <summary>قُفلُ الجَلسَة: يَبقى ما بَقِيَ الاتِّصال، ويَسقُطُ بِمَوتِ العَمَلِيَّة.</summary>
    public const string AggregatorTryLock = "select pg_try_advisory_lock(hashtext('" + AggregatorLockName + "'))";

    /// <summary>حامِلُ القُفلِ في هذه القاعِدَة (المِفتاحُ <c>bigint</c> واحِد: <c>classid</c> نِصفُه الأَعلى و<c>objid</c> الأَدنى).</summary>
    public const string AggregatorLockHolder =
        "select l.pid from pg_locks l where l.locktype = 'advisory' and l.granted and l.objsubid = 1 "
        + "and l.database = (select d.oid from pg_database d where d.datname = current_database()) "
        + "and ((l.classid::bigint << 32) | l.objid::bigint) = hashtext('" + AggregatorLockName + "')::bigint "
        + "and l.pid <> pg_backend_pid() limit 1";

    /// <summary>
    /// <para><b>هل ما زالَت هذه الجَلسَةُ تُمسِكُ القُفل؟</b> (‏2026-11-12، مُراجَعَةُ المَرحَلَةِ أ · M6A-CNT-2) — يُسأَلُ عَلى اتِّصالِ القُفلِ قَبلَ كُلِّ صَفحَة.
    /// <c>--interval</c> يَترُكُ ذلك الاتِّصالَ خامِلاً طولَ التَشغيل، فَمُهلَةُ خُمولٍ أَو وَسيطُ اتِّصالاتٍ أَو انقِطاعٌ يُنهي جَلسَتَه فَيَسقُطُ القُفلُ ولا يَدري
    /// المُجَمِّع: نِداءٌ عَلى جَلسَةٍ مَيِّتَةٍ يَرمي، وجَلسَةٌ حَيَّةٌ بِلا قُفلٍ تُجيبُ <c>false</c> — وكِلاهُما «القُفلُ ضاع».</para>
    /// </summary>
    public const string AggregatorLockHeldHere =
        "select exists(select 1 from pg_locks l where l.locktype = 'advisory' and l.granted and l.objsubid = 1 and l.pid = pg_backend_pid() "
        + "and l.database = (select d.oid from pg_database d where d.datname = current_database()) "
        + "and ((l.classid::bigint << 32) | l.objid::bigint) = hashtext('" + AggregatorLockName + "')::bigint)";

    /// <summary>المُؤَشِّرُ المَحفوظ، أَو لا صَفّ (لَم يَعمَلِ المُجَمِّعُ بَعد).</summary>
    public const string AggCursorRead = "select after_gen, after_xid::text, after_id from meter.agg_cursor where singleton";

    /// <summary>الجيلُ الجاري، و1 عَلى دَفتَرٍ لَم يُفتَح فيه جيل (ما تَكتُبُه <see cref="Insert"/> حينَها).</summary>
    public const string CurrentGenerationOrFirst = "select coalesce(max(generation), 1) from meter.generation";

    /// <summary>
    /// <para><b>تَراجُعُ المُؤَشِّر</b> (R3/R4): مُؤَشِّرٌ في الجيلِ الجاري ومُعَرِّفُ مُعامَلَتِه فَوقَ العُنقود — أَو مُؤَشِّرٌ في جيلٍ لَم يُفتَح —
    /// قاعِدَةٌ استُعيدَت ولَم يُقلِعِ التَطبيقُ عَلَيها بَعد. والمُجَمِّعُ يَرفُضُ ولا يَتَقَدَّم (الخُروج ٤)، ومَعَه
    /// <see cref="RegressionInGeneration"/> لِسُطورِ الجيلِ الجاري.</para>
    /// </summary>
    public const string CursorRegression =
        "select exists(select 1 from meter.agg_cursor c where c.singleton and (c.after_gen > @cur or (c.after_gen = @cur and c.after_xid > pg_current_xact_id())))";

    /// <summary>لَقطَةُ البَدءِ T0 (COR-8) — نَصُّ <c>pg_snapshot</c>.</summary>
    public const string SnapshotNow = "select pg_current_snapshot()::text";

    /// <summary>رَأسُ الصَفحَة — أَوَّلُ جُملَةٍ في مُعامَلَتِها (<c>REPEATABLE READ</c>) فَيَتَثَبَّتُ بِها لَقطَتُها: الجيلُ الجاري والأُفُق
    /// (<c>pg_snapshot_xmin</c>: أَقدَمُ مُعامَلَةٍ جارِيَةٍ عَلى العُنقودِ كُلِّه — R2). وما تَحتَ الأُفُقِ انتَهى فَرُؤيَتُه نِهائِيَّة.</summary>
    public const string PageHead =
        "select coalesce((select max(g.generation) from meter.generation g), 1), pg_snapshot_xmin(pg_current_snapshot())::text";

    /// <summary>
    /// <para><b>مَفاتيحُ الصَفحَة — المُؤَشِّرُ <c>(generation, writer_xid, id)</c></b> (D5، R4): ما فَوقَ المُؤَشِّر، والأُفُقُ عَلى الجيلِ الجاري وَحدَه —
    /// فَالجيلُ المُغلَقُ تامٌّ لا يَنتَظِرُ أُفُقاً، وسَطرٌ بَعدَ الاستِعادَةِ في جيلٍ جَديدٍ يُقرَأُ وإن كانَ مُعَرِّفُ مُعامَلَتِه تَحتَ المُؤَشِّر.</para>
    /// </summary>
    public const string PageKeys =
        "select e.generation, e.writer_xid::text, e.id, e.unit from meter.usage_entry e "
        + "where (e.generation, e.writer_xid, e.id) > (@g, @x::xid8, @i) and (e.generation < @cur or e.writer_xid < @h::xid8) "
        + "order by e.generation, e.writer_xid, e.id limit @n";

    /// <summary>
    /// <para><b>مَجاميعُ الصَفحَة</b> — المَدى <c>(المُؤَشِّر، آخِرُ مَفاتيحِ الصَفحَة]</c> في لَقطَةِ المُعامَلَةِ نَفسِها هُوَ صُفوفُ الصَفحَةِ بِأَعيانِها:
    /// كُلُّ ما تَحتَ آخِرِ مِفتاحٍ مِن الجيلِ الجاري تَحتَ الأُفُق، وما في جيلٍ مُغلَقٍ تامّ. واليَومُ بِـ<see cref="DayZone"/>.</para>
    /// </summary>
    public const string AggUpsertRange =
        "insert into meter.agg_daily (day, app, entrepreneur, role, feature, provider, unit, entries, quantity) "
        + "select (e.occurred_at at time zone '" + DayZone + "')::date, e.app, e.entrepreneur, e.role, e.feature, e.provider, e.unit, count(*), sum(e.quantity) "
        + "from meter.usage_entry e "
        + "where (e.generation, e.writer_xid, e.id) > (@g, @x::xid8, @i) and (e.generation, e.writer_xid, e.id) <= (@lg, @lx::xid8, @li) "
        + "group by 1, 2, 3, 4, 5, 6, 7 "
        + "on conflict (day, app, entrepreneur, role, feature, provider, unit) do update "
        + "set entries = meter.agg_daily.entries + excluded.entries, quantity = meter.agg_daily.quantity + excluded.quantity";

    /// <summary>
    /// <para>المُؤَشِّرُ يَتَقَدَّمُ إلى آخِرِ مَفاتيحِ الصَفحَة — في مُعامَلَةِ مَجاميعِها نَفسِها: يُودَعانِ مَعاً أَو لا يُودَعُ أَحَدُهُما.</para>
    /// <para><b>وبِشَرطِ ما قَرَأَتهُ الصَفحَة</b> (‏2026-11-12، مُراجَعَةُ المَرحَلَةِ أ · M6A-CNT-2): يَتَقَدَّمُ مِن <c>(@g, @x, @i)</c> — المُؤَشِّرِ المَخزونِ الَّذي
    /// قَرَأَتهُ الصَفحَةُ في لَقطَتِها (<see cref="AggCursorRead"/>) — أَو يُولَدُ إن لَم يَكُن صَفّ؛ وإلّا لا يَكتُبُ شَيئاً (صِفرُ صُفوفٍ ⇐ تَتَراجَعُ الصَفحَة).
    /// كانَ يَكتُبُ بِلا شَرطٍ فَوقَ ما كَتَبَه غَيرُه، فَصَفحَةٌ أُعيدَت بَعدَ 40001 بِمُؤَشِّرٍ في الذاكِرَةِ جَمَعَت المَدى مَرَّتَين (مَقيس: «سُطورُ الدَفتَر 13 ·
    /// مَجاميعُ agg_daily 23»). وفي <c>REPEATABLE READ</c> صَفٌّ حَدَّثَه غَيرُها بَعدَ لَقطَتِها يَرُدُّه 40001 قَبلَ الشَرط — والشَرطُ لِما سِوى ذلك.</para>
    /// </summary>
    public const string AggCursorSet =
        "insert into meter.agg_cursor (singleton, after_gen, after_xid, after_id, pulled_at) values (true, @lg, @lx::xid8, @li, transaction_timestamp()) "
        + "on conflict (singleton) do update set after_gen = excluded.after_gen, after_xid = excluded.after_xid, "
        + "after_id = excluded.after_id, pulled_at = excluded.pulled_at "
        + "where (meter.agg_cursor.after_gen, meter.agg_cursor.after_xid, meter.agg_cursor.after_id) = (@g, @x::xid8, @i)";

    /// <summary><b>إعادَةُ البِناء</b> = تَفريغُ المَجاميعِ وحَذفُ المُؤَشِّرِ في مُعامَلَةٍ واحِدَةٍ تَحتَ القُفل؛ ثُمَّ اللَحاقُ كَأَوَّلِ تَشغيل.</summary>
    public const string AggRebuild = "truncate meter.agg_daily; delete from meter.agg_cursor";

    /// <summary><b>ما أُودِعَ قَبلَ T0 ولَم يُجمَع</b>: سَطرٌ فَوقَ المُؤَشِّرِ مُعامَلَتُه مَرئِيَّةٌ في لَقطَةِ البَدء. <c>--once</c> لا يَنتَهي وهذا صَحيح.</summary>
    public const string PendingBeforeSnapshot =
        "select exists(select 1 from meter.usage_entry e where (e.generation, e.writer_xid, e.id) > (@g, @x::xid8, @i) "
        + "and pg_visible_in_snapshot(e.writer_xid, @t0::pg_snapshot))";

    /// <summary>مَن يُمسِكُ الأُفُق: الخَلفِيَّةُ (في أَيِّ قاعِدَةٍ عَلى العُنقود — R2) الَّتي مُعَرِّفُ مُعامَلَتِها هُوَ الأُفُق، أَو مُعامَلَةٌ مُعَدَّةٌ بِه.</summary>
    public const string HorizonBlockers =
        "select a.pid::text, a.backend_xid::text, coalesce(a.datname, ''), coalesce(a.state, '') from pg_stat_activity a where a.backend_xid = @h::xid8::xid "
        + "union all select 'prepared ' || p.gid, p.transaction::text, p.database, 'prepared' from pg_prepared_xacts p where p.transaction = @h::xid8::xid";

    /// <summary>تَأَخُّرُ المُجَمِّعِ بِالثَواني: عُمرُ أَقدَمِ سَطرٍ فَوقَ المُؤَشِّر، وصِفرٌ إن لَم يَبقَ شَيء.</summary>
    public const string Lag =
        "select coalesce(floor(extract(epoch from clock_timestamp() - min(e.occurred_at)))::bigint, 0) from meter.usage_entry e "
        + "where (e.generation, e.writer_xid, e.id) > (@g, @x::xid8, @i)";
}
