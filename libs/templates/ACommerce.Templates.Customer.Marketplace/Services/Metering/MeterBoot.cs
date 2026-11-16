using ACommerce.Platform.Shared.Metering;
using Marten;
using Npgsql;

namespace ACommerce.Templates.Customer.Marketplace.Services.Metering;

/// <summary>ما فَعَلَه الإقلاع — ويُطبَعُ سَطراً واحِداً (<see cref="Line"/>).</summary>
public sealed record MeterBootResult(int Generation, int Units, int EpochsNew, bool OpenedGeneration)
{
    /// <summary><c>[meter] boot generation=G units=N epochs_new=K</c> — بِحَرفِ التَصميم §4.</summary>
    public string Line => $"[meter] boot generation={Generation} units={Units} epochs_new={EpochsNew}";
}

/// <summary>
/// <para><b>إقلاعُ الدَفتَر — قَبلَ البَذرِ وقَبلَ أَن يَستَمِعَ الخادِم</b> (‏ADR-051، التَصميم §4 و§10.1). يُنادى في <c>Program.cs</c> أَوَّلَ كُتلَةِ
/// البَذر، فَكُلُّ كِتابَةٍ مَعدودَةٍ بَعدَه — ومِنها البَذر — تَجِدُ الجَدوَلَ والجيل:</para>
/// <list type="number">
/// <item><c>EnsureStorageExistsAsync(UsageLedgerFeature)</c> — يُنشِئُ مُخَطَّطَ <c>meter</c> وجَداوِلَه (D1)؛</item>
/// <item><b>الجيل</b> في مُعامَلَةٍ واحِدَةٍ بِقُفلٍ استِشارِيّ: لا جيلَ ← يَفتَحُ 1؛ وسَطرٌ في الجيلِ الجاري مُعَرِّفُ مُعامَلَتِه فَوقَ العُنقود ← استُعيدَت
/// القاعِدَةُ في عُنقودٍ جَديدٍ فَيَفتَحُ الجيلَ التالي (R3/R4)؛ ويُخَبِّئُه في مُستَمِعِ هذا المَخزَن فَيَحمِلُه كُلُّ إدخال؛</item>
/// <item><b>نافِذَةُ كُلِّ وَحدَةٍ</b> مِن المُعجَمِ المُحَمَّل (<c>unit_epoch</c>، <c>ON CONFLICT DO NOTHING</c>) — فَوَحدَةٌ تُضافُ لاحِقاً تَبدَأُ نافِذَتُها
/// بِأَوَّلِ إقلاعٍ يَعرِفُها، ولا يُعَدُّ تاريخُ ما قَبلَ العَدِّ «ناقِصاً» (§9).</item>
/// </list>
/// <para>والمُعجَمُ يُحَمَّلُ هُنا أَوَّلَ مَرَّة (<see cref="MeterUnitCatalog.Default"/>) — فَمُعجَمٌ فاسِدٌ يُفشِلُ الإقلاعَ بِرُموزِه قَبلَ أَيِّ كِتابَة.</para>
/// </summary>
public static class MeterBoot
{
    public static async Task<MeterBootResult> EnsureAsync(IDocumentStore store, CancellationToken ct = default)
    {
        var catalog = MeterUnitCatalog.Default;
        await store.Storage.Database.EnsureStorageExistsAsync(typeof(UsageLedgerFeature), ct);

        await using var conn = store.Storage.Database.CreateConnection();
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        await Execute(conn, tx, MeterSql.BootLock, ct);
        var current = await Scalar(conn, tx, MeterSql.CurrentGeneration, ct) is int g ? g : 0;
        var opened = false;
        if (current == 0)
        {
            current = 1;
            opened = true;
        }
        else
        {
            await using var check = new NpgsqlCommand(MeterSql.RegressionInGeneration, conn, tx);
            check.Parameters.AddWithValue("g", current);
            if (await check.ExecuteScalarAsync(ct) is true) { current++; opened = true; }
        }
        if (opened)
        {
            await using var open = new NpgsqlCommand(MeterSql.OpenGeneration, conn, tx);
            open.Parameters.AddWithValue("g", current);
            await open.ExecuteNonQueryAsync(ct);
        }

        var epochsNew = 0;
        foreach (var unit in catalog.Units)
        {
            await using var epoch = new NpgsqlCommand(MeterSql.EnsureEpoch, conn, tx);
            epoch.Parameters.AddWithValue("u", unit.Code);
            epochsNew += await epoch.ExecuteNonQueryAsync(ct);
        }
        await tx.CommitAsync(ct);

        foreach (var listener in (store as DocumentStore)?.Options.Listeners.OfType<UsageListener>() ?? [])
            listener.CacheGeneration(current);

        return new MeterBootResult(current, catalog.Units.Count, epochsNew, opened);
    }

    private static async Task Execute(NpgsqlConnection conn, NpgsqlTransaction tx, string sql, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(sql, conn, tx);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task<object?> Scalar(NpgsqlConnection conn, NpgsqlTransaction tx, string sql, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(sql, conn, tx);
        return await cmd.ExecuteScalarAsync(ct);
    }
}
