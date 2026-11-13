using ACommerce.Kit.Culture;
using ACommerce.Platform.Shared.Metering;
using Marten;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using Wolverine;

namespace ACommerce.Templates.Customer.Marketplace.Services.Metering;

// ═══ لَقطَةُ الحُضورِ اليَومِيَّة — جانِبُ التِقنِيَة (‏2026-11-13، الموجَة ١٣ W13-6·١٩ — ‏ADR-051 §٢٣؛ التَصميم §8، COR-10) ═══════════════════════
//
// **ما تَعُدُّه**: ما فَعَّلَه كُلُّ مَتجَرٍ اليَوم — نَفسُه وأَدوارُه وتَعريفاتُه المُعتَمَدَةُ ورَبطاتُه ومَفاتيحُه وقَناةُ دُخولِه — مِن الحَقيقَةِ المَخزونَة، فَالمَتجَرُ
// الخامِلُ يُعَدُّ أَيضاً. والوَحدَةُ مُشتَقَّةٌ مِن التَعريفات (`PresenceReader.Entries`). وجانِبُ التَشغيلِ (العَمَلِيّات) لا يَمَسُّه شَيءٌ هُنا.
//
// **ولا أُنبوبَ رابِع (القاعِدَة ٨)**: مُجَدوِلُ Wolverine المَتينُ المُرَكَّبُ أَصلاً — مُستَهلِكُه الثاني بَعدَ مُهلَةِ الفاتورَة (‏ADR-046، `GatewayInvoiceExpiry.Configure`):
// رِسالَةٌ `TakePresenceSnapshot(Day)` في طابورِها المَحَلِّيِّ بِصُندوقٍ مُستَديم، فَالمُجَدوَلَةُ تُحفَظُ في القاعِدَةِ وتَعبُرُ الإقلاع. لا خِدمَةَ خَلفِيَّةً ولا مُؤَقِّت.
//
// **ثَلاثُ قَواعِدَ تَمنَعُ يَوماً مَعدوداً مَرَّتَينِ أَو مَعدوداً بِغَيرِ حالِه** (COR-10):
//   ١. **الرِسالَةُ المُتَأَخِّرَةُ تُرفَض**: يَومٌ قَبلَ اليَومِ (بِمِنطَقَةِ المَنَصَّة) لا يُكتَبُ — `[meter] presence-late day=D` — ويُرسَلُ اليَومُ الآن؛ فَإقلاعٌ بَعدَ
//      انقِطاعٍ لا يَكتُبُ حالَ اليَومِ بِاسمِ أَمس، واليَومُ الغائِبُ يَبقى غائِباً («لَقطَةٌ غائِبَة»). ويَومٌ لَم يَحِن (ساعَةٌ تَسبِقُ) يُعادُ إلى بِدايَتِه.
//   ٢. **قِراءَةٌ ثُمَّ مُعامَلَةٌ خامٌ واحِدَة**: المُطالَبَةُ (`presence_claim`، مِفتاحُها اليَوم) والسُطورُ و`completed_at` تُودَعُ مَعاً أَو لا يُودَعُ شَيء —
//      فَعُطلٌ في الوَسَطِ لا يَترُكُ مُطالَبَةً ولا سَطراً ويُعيدُ Wolverine الرِسالَة؛ ومُعالِجانِ مُتَسابِقانِ يَقِفُ ثانيهِما عَلى المِفتاحِ الفَريدِ ثُمَّ يَخسَرُ ولا يَكتُب.
//   ٣. **اليَومُ التالي بَعدَ الإيداع**: الرابِحُ وَحدَه يُجَدوِلُ غَدَه عِندَ بِدايَتِه؛ والخاسِرُ لا يُجَدوِل — فَلا سِلسِلَتان.
// وعِندَ الإقلاع (`ApplicationStarted` في `Program.cs`): اليَومُ إن لَم تَكتَمِل مُطالَبَتُه، والغَد — والمُكَرَّرُ يَنطَوي عَلى المُطالَبَة.
//
// **والعَدُّ لا يُفشِلُ التَطبيق** (الثابِتُ ٧): الإقلاعُ يَكتُبُ سَطرَ تَحذيرٍ إن تَعَذَّرَت جَدوَلَتُه، ورِسالَةٌ تَفشَلُ تُعيدُها سِياسَةُ Wolverine ولا يَقِفُ لَها طَلَب.

/// <summary>
/// <para><b>خُذ لَقطَةَ حُضورِ هذا اليَوم</b> — اليَومُ بِمِنطَقَةِ المَنَصَّةِ (<see cref="PresenceSnapshot.Zone"/>). <b>واسمُ النَوعِ عَقدٌ مُخَزَّن</b>: الرِسالَةُ تُحفَظُ
/// بِاسمِه الكامِلِ حَتّى يَحينَ غَدُها، فَإعادَةُ تَسمِيَتِه تُيَتِّمُ ما جُدوِل (كَرِسالَةِ المُهلَة، ‏ADR-046 §٤).</para>
/// </summary>
public sealed record TakePresenceSnapshot(DateOnly Day);

/// <summary>مُعالِجُ اللَقطَة — يَكتَشِفُه Wolverine بِنَوعِه (<see cref="PresenceSnapshot.Configure"/>)، والقَرارُ كُلُّه في <see cref="PresenceSnapshot.HandleAsync"/>.</summary>
public static class PresenceSnapshotHandler
{
    public static Task Handle(TakePresenceSnapshot snapshot, IDocumentStore store, ILoggerFactory logs, IMessageBus bus, CancellationToken ct)
        => new PresenceSnapshot(store, logs.CreateLogger<PresenceSnapshot>()).HandleAsync(snapshot, bus, ct);
}

/// <summary>ما تَقضي بِه الرِسالَةُ قَبلَ أَيِّ قِراءَة: تُؤخَذُ اللَقطَة، أَو تُرفَضُ مُتَأَخِّرَة، أَو تُعادُ إلى يَومِها.</summary>
public enum PresenceTiming { Take, Late, Early }

/// <summary>
/// <para>ما فَعَلَته لَقطَةٌ واحِدَة — <see cref="Claimed"/> صادِقٌ لِلرابِحِ وَحدَه (سُطورُه أُودِعَت)، وخاسِرٌ لا سَطرَ لَه. و<see cref="Line"/> سَطرُها في السِجِلّ.</para>
/// </summary>
public sealed record PresenceTaken(DateOnly Day, bool Claimed, int Stores, int Suspended, IReadOnlyList<UsageEntry> Entries, IReadOnlyList<string> Unresolved)
{
    /// <summary><c>[meter] presence day=D claimed=… stores=N suspended=K entries=M (unit qty · …)</c> — والوَحَداتُ بِعَدَدِ سُطورِها.</summary>
    public string Line =>
        $"[meter] presence day={PresenceReader.DayKey(Day)} claimed={(Claimed ? "true" : "false")} stores={Stores} suspended={Suspended} entries={Entries.Count}"
        + (Entries.Count == 0 ? "" : " (" + string.Join(" · ", Entries.GroupBy(e => e.Unit, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => $"{g.Key} {g.Count()}")) + ")")
        + (Unresolved.Count == 0 ? "" : " unresolved=" + string.Join(",", Unresolved));
}

/// <summary>
/// <para><b>اللَقطَة</b> — تَقرَأُ (<see cref="PresenceReader"/>) ثُمَّ تَكتُبُ في مُعامَلَةٍ خامٍ واحِدَةٍ عَلى اتِّصالِ الدَفتَر: المُطالَبَة، فَالسُطورُ
/// (<c>ON CONFLICT DO NOTHING</c>)، فَـ<c>completed_at</c>. والجيلُ ما خَبَّأَه الإقلاعُ في مُستَمِعِ هذا المَخزَن (كَكاتِبِ الصادِر).</para>
/// </summary>
public sealed class PresenceSnapshot(IDocumentStore store, ILogger<PresenceSnapshot> log)
{
    /// <summary><b>مِنطَقَةُ اليَوم</b> — <see cref="CultureTime.DefaultTimeZone"/> بِعَينِه (س٥؛ <c>MeterZoneTests</c> يُثَبِّتُها وحَدَّ يَومِ المُجَمِّع <c>MeterSql.DayZone</c>).</summary>
    public const string Zone = CultureTime.DefaultTimeZone;

    /// <summary>يَومُ لَحظَةٍ بِمِنطَقَةِ المَنَصَّة.</summary>
    public static DateOnly DayOf(DateTimeOffset instant) => DateOnly.FromDateTime(CultureTime.InZone(instant.UtcDateTime, Zone));

    /// <summary>بِدايَةُ يَومٍ (‏00:00 بِمِنطَقَةِ المَنَصَّة) لَحظَةً بِـUTC — مَوعِدُ رِسالَتِه، و<c>occurred_at</c> سُطورِه.</summary>
    public static DateTimeOffset StartOf(DateOnly day)
        => new(TimeZoneInfo.ConvertTimeToUtc(day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified), CultureTime.Zone(Zone)), TimeSpan.Zero);

    /// <summary>اليَومُ الآن.</summary>
    public static DateOnly Today() => DayOf(DateTimeOffset.UtcNow);

    /// <summary>القاعِدَةُ ١ — دالَّةٌ نَقِيَّة: يَومٌ قَبلَ اليَومِ مُتَأَخِّر، ويَومٌ بَعدَه مُبَكِّر، واليَومُ يُؤخَذ.</summary>
    public static PresenceTiming Timing(DateOnly day, DateOnly today)
        => day < today ? PresenceTiming.Late : day > today ? PresenceTiming.Early : PresenceTiming.Take;

    /// <summary>
    /// <para><b>ما يَفعَلُه المُعالِج</b>: المُتَأَخِّرَةُ تُرفَضُ ويُرسَلُ اليَومُ الآن؛ والمُبَكِّرَةُ تُعادُ إلى بِدايَةِ يَومِها؛ واليَومُ يُؤخَذ — والرابِحُ وَحدَه يُجَدوِلُ غَدَه
    /// بَعدَ الإيداع.</para>
    /// </summary>
    public async Task HandleAsync(TakePresenceSnapshot message, IMessageBus bus, CancellationToken ct = default)
    {
        var today = Today();
        switch (Timing(message.Day, today))
        {
            case PresenceTiming.Late:
                log.LogWarning("[meter] presence-late day={Day} today={Today} — nothing written; today is sent now",
                    PresenceReader.DayKey(message.Day), PresenceReader.DayKey(today));
                await bus.SendAsync(new TakePresenceSnapshot(today));
                return;
            case PresenceTiming.Early:
                log.LogInformation("[meter] presence-early day={Day} today={Today} — rescheduled at its start",
                    PresenceReader.DayKey(message.Day), PresenceReader.DayKey(today));
                await ScheduleDayAsync(bus, message.Day);
                return;
        }

        var taken = await TakeAsync(message.Day, PresenceDefinitions.Platform, ct);
        log.LogInformation("{Presence}", taken.Line);
        if (!taken.Claimed) return;
        await ScheduleDayAsync(bus, message.Day.AddDays(1));
    }

    /// <summary>
    /// <para><b>قِراءَةٌ ثُمَّ مُعامَلَةٌ خامٌ واحِدَة</b> (التَصميم §8 الخُطوَتانِ ٢ و٣): المُطالَبَةُ <c>INSERT … ON CONFLICT DO NOTHING RETURNING day</c> — لا شَيءَ يَعودُ ⇐
    /// تَتَراجَعُ وتَخسَر؛ ثُمَّ كُلُّ سَطر، ثُمَّ <c>completed_at</c>، ثُمَّ الإيداع. وما يَرمي في الوَسَطِ يَرمي: لا مُطالَبَةَ ولا سَطر.</para>
    /// </summary>
    public async Task<PresenceTaken> TakeAsync(DateOnly day, PresenceDefinitions definitions, CancellationToken ct = default)
    {
        await store.Storage.Database.EnsureStorageExistsAsync(typeof(UsageLedgerFeature), ct);
        var stores = await PresenceReader.ReadAsync(store, ct);
        var unresolved = new List<string>();
        var entries = PresenceReader.Entries(day, stores, definitions, MeterUnitCatalog.Default, unresolved);
        var suspended = stores.Count(s => s.IsSuspended);

        await using var conn = store.Storage.Database.CreateConnection();
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await using (var claim = new NpgsqlCommand(MeterSql.PresenceClaimInsert, conn, tx))
        {
            claim.Parameters.AddWithValue("day", day);
            if (await claim.ExecuteScalarAsync(ct) is null or DBNull)
            {
                await tx.RollbackAsync(ct);
                return new PresenceTaken(day, false, stores.Count, suspended, [], unresolved);
            }
        }
        var generation = Generation();
        foreach (var e in entries)
        {
            await using var insert = new NpgsqlCommand(MeterSql.PresenceInsert, conn, tx);
            insert.Parameters.AddWithValue("id", e.Id);
            insert.Parameters.AddWithValue("unit", e.Unit);
            insert.Parameters.AddWithValue("quantity", e.Quantity);
            insert.Parameters.AddWithValue("app", e.App);
            insert.Parameters.AddWithValue("entrepreneur", e.Entrepreneur);
            insert.Parameters.AddWithValue("role", e.Role);
            insert.Parameters.AddWithValue("feature", e.Feature);
            insert.Parameters.AddWithValue("provider", e.Provider);
            insert.Parameters.AddWithValue("source_type", e.SourceType);
            insert.Parameters.AddWithValue("source_id", e.SourceId);
            insert.Parameters.AddWithValue("day", day);
            insert.Parameters.AddWithValue("generation", generation);
            await insert.ExecuteNonQueryAsync(ct);
        }
        await using (var complete = new NpgsqlCommand(MeterSql.PresenceClaimComplete, conn, tx))
        {
            complete.Parameters.AddWithValue("day", day);
            await complete.ExecuteNonQueryAsync(ct);
        }
        await tx.CommitAsync(ct);
        return new PresenceTaken(day, true, stores.Count, suspended, entries, unresolved);
    }

    /// <summary>أَاكتَمَلَت مُطالَبَةُ هذا اليَوم؟ — ما يَسأَلُه الإقلاعُ قَبلَ أَن يُرسِلَ اليَوم.</summary>
    public static async Task<bool> CompletedAsync(IDocumentStore store, DateOnly day, CancellationToken ct = default)
    {
        await store.Storage.Database.EnsureStorageExistsAsync(typeof(UsageLedgerFeature), ct);
        await using var conn = store.Storage.Database.CreateConnection();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(MeterSql.PresenceClaimCompleted, conn);
        cmd.Parameters.AddWithValue("day", day);
        return await cmd.ExecuteScalarAsync(ct) is true;
    }

    /// <summary>
    /// <para><b>الإقلاع</b> (التَصميم §8؛ يُناديه <c>ApplicationStarted</c> في <c>Program.cs</c>): اليَومُ الآنَ إن لَم تَكتَمِل مُطالَبَتُه، والغَدُ عِندَ بِدايَتِه —
    /// ويُكتَبُ سَطراً <c>[meter] presence-boot …</c>. وتَعَذُّرُه سَطرُ تَحذيرٍ لا عُطلُ إقلاع (الثابِتُ ٧): الطَلَباتُ تُخدَمُ والدَفتَرُ يَنتَظِرُ الإقلاعَ التالي.</para>
    /// </summary>
    public static async Task ScheduleAtBootAsync(IServiceProvider services, CancellationToken ct = default)
    {
        var log = services.GetRequiredService<ILoggerFactory>().CreateLogger<PresenceSnapshot>();
        try
        {
            var today = Today();
            var completed = await CompletedAsync(services.GetRequiredService<IDocumentStore>(), today, ct);
            await using var scope = services.CreateAsyncScope();
            var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
            if (!completed) await bus.SendAsync(new TakePresenceSnapshot(today));
            var tomorrow = today.AddDays(1);
            await ScheduleDayAsync(bus, tomorrow);
            log.LogInformation("[meter] presence-boot today={Today} sent={Sent} tomorrow={Tomorrow} at={At}",
                PresenceReader.DayKey(today), completed ? "no (completed)" : "yes", PresenceReader.DayKey(tomorrow), StartOf(tomorrow).ToString("O"));
        }
        catch (Exception ex)
        {
            log.LogWarning("[meter] presence-boot-failed: {Error} {Message}", ex.GetType().Name, ex.Message);
        }
    }

    /// <summary>
    /// <para><b>تَركيبُ Wolverine لِرِسالَةِ اللَقطَة — سَطرانِ في مَوضِعٍ واحِد</b> يُناديهِما <c>Program.cs</c> بِـ<c>ConfigureWolverine</c>، ويُناديهِما البُرهانُ الحَيُّ
    /// نَفسُهُما (<c>LivePresenceSnapshotProofTests</c>): اكتِشافُ المُعالِجِ بِنَوعِه (لا مَكتَبَةُ القالِبِ كُلُّها)، وصُندوقٌ مُستَديمٌ لِطابورِه المَحَلِّيّ.</para>
    /// </summary>
    public static void Configure(WolverineOptions opts)
    {
        opts.Discovery.IncludeType(typeof(PresenceSnapshotHandler));
        opts.LocalQueueFor<TakePresenceSnapshot>().UseDurableInbox();
    }

    /// <summary><b>يَومٌ في طابورِه المَتينِ عِندَ بِدايَتِه</b> — مَوضِعُ الجَدوَلَةِ الوَحيدُ هُنا (الغَدُ بَعدَ الإيداع، والغَدُ عِندَ الإقلاع، والمُبَكِّرَةُ إلى يَومِها).</summary>
    private static async Task ScheduleDayAsync(IMessageBus bus, DateOnly day) => await bus.ScheduleAsync(new TakePresenceSnapshot(day), StartOf(day));

    /// <summary>الجيلُ الَّذي خَبَّأَه الإقلاعُ في مُستَمِعِ هذا المَخزَن — و<c>0</c> قَبلَه، فَيَأخُذُ الإدخالُ آخِرَ جيلٍ مَفتوح (<see cref="MeterSql.PresenceInsert"/>).</summary>
    private int Generation() => (store as DocumentStore)?.Options.Listeners.OfType<UsageListener>().FirstOrDefault()?.Generation ?? 0;
}
