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
}
