using System.Diagnostics;
using System.Net;
using Npgsql;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ بَصمَةُ البَذرِ في جَداوِلِ `platform` — تُثَبَّتُ قَبلَ أَن تَصيرَ وِلاداتُ المَتاجِرِ المَبذورَةُ `Insert` (‏2026-11-12، الموجَة ١٣ W13-6·٨ — ADR-051) ══
//
// **لِماذا قَبلَ التَبديل** (القاعِدَتانِ ٣ و١٣): البَندُ ٨ يَقلِبُ وِلاداتِ `Tenant` الثَلاثَ في البَذرِ مِن `Store` إلى `Insert` — `PlatformSeed.cs:164`
// (حارِسُه `LoadAsync` في `:141`) و`AppearanceBaselineSeeder.cs:246` و`:368` (حارِساهُما `:243` و`:365`) — لِأَنّ المُستَمِعَ يَعُدُّ `store.create`
// مِن `Inserts` وَحدَها، و`Store` لِوَثيقَةٍ بِلا تَزامُنٍ تَفاؤُلِيٍّ لا يُمَيِّزُ الوِلادَةَ مِن التَحديث. ووَعدُ التَبديلِ أَنّه **لا يُغَيِّرُ صَفّاً في `platform`**:
// المَتاجِرُ نَفسُها، والصُفوفُ نَفسُها في كُلِّ جَدوَل، والإقلاعُ الثاني عَلى القاعِدَةِ نَفسِها لا يَرمي ولا يُضيف. والوَعدُ لا يُحفَظُ بِاسمِه (القاعِدَة ٢):
// هذا المِلَفُّ يُقلِعُ التَطبيقَ المَبنِيَّ نَفسَه (`apps/V1.App/bin/<التَهيِئَة>/net10.0/V1.App.dll`، وهُوَ ما تَبنيه خُطوَةُ CI قَبلَ الحَيّ) عَلى قاعِدَةٍ فارِغَةٍ
// جَديدَة **بِكُلِّ مُفتاحِ بَذرٍ مُشعَلاً**، ويَعُدُّ صُفوفَ كُلِّ جَدوَلٍ في `platform` بَعدَ الإقلاعِ البارِدِ ثُمَّ بَعدَ إقلاعٍ دافِئٍ ثانٍ، ويُثَبِّتُهُما بِالرَقَم،
// ومَعَهُما مُعَرِّفاتُ المَتاجِرِ المَبذورَة. ويُودَعُ وَحدَه، ثُمَّ يُعيدُ كوميتُ التَبديلِ تَشغيلَه **بِلا تَعديلِ حَرف** (بَصمَتُه في رِسالَتِه).
//
// **ومَفاتيحُ البَذرِ كُلُّها** (`Program.cs` كُتلَةُ البَذر، `BootSeederOrderTests`): بيئَةُ `Development` (بَذرُ المَنَصَّةِ ورَبطُ المِلكِيَّةِ يَعمَلانِ دائِماً)،
// و`TEST_DATA_SEED` و`CART_SAMPLE_SEED` و`INCUBATOR_SAMPLE_SEED` و`APPEARANCE_BASELINE_SEED`، ومُشرِفُ المَنَصَّةِ بِالهاتِفِ والبَريد. فَكُلُّ بَذّارٍ في
// الكُتلَةِ يَكتُبُ، ولا يَبقى بابٌ لِلتَبديلِ لا يَمُرُّ بِه هذا البُرهان.
//
// **والاستِثناءُ الوَحيدُ مَكتوبٌ بِسَبَبِه** (RULES-18، كَبَصمَةِ الرِحلَة): `wolverine_%` — طابورُ Wolverine المَتينُ بُنيَةٌ تَحتِيَّةٌ يَكتُبُها الناقِلُ
// في الخَلفِيَّةِ بِتَوقيتٍ لا يَملِكُه الإقلاع، فَتُطبَعُ ولا تُقارَن. و`meter.*` خارِجَ `platform` أَصلاً: يُطبَعُ عَدُّه لِلبُرهانِ وَحدَه — فَهُوَ الَّذي يَنمو
// بِالتَبديل (`store.create` تَحتَ `_seed`)، و`platform` لا يَتَحَرَّك.
//
// **والأَداةُ تُقاسُ قَبلَ أَن يُوثَقَ بِها** (القاعِدَة ١٠): تَطبَعُ عَدَدَ الجَداوِلِ المُقارَنَةِ وصُفوفَ كُلٍّ، وتَفشَلُ عَلى صِفرِ جَدوَلٍ أَو صِفرِ مَتجَرٍ
// مَبذور، وعَلى جَدوَلٍ زائِدٍ أَو ناقِصٍ أَو رَقَمٍ مُختَلِف — في الإقلاعَين.

[Collection(LiveProofCollection.Name)]
public class LiveMeterSeededFootprintCharacterizationProofTests(ITestOutputHelper output)
{
    private static bool Enabled
        => LiveDealConcurrencyProofTests.Enabled && !string.IsNullOrEmpty(LiveDealConcurrencyProofTests.ConnectionString);

    /// <summary>المُخَطَّطُ الَّذي يُعَدّ — مُخَطَّطُ Marten لِلمُضيف.</summary>
    private const string Schema = "platform";

    /// <summary>جَداوِلُ طابورِ Wolverine — تُطبَعُ ولا تُقارَن (بادِئَةٌ حَرفِيَّة).</summary>
    private const string ExcludedPrefix = "wolverine_";

    /// <summary><b>البَصمَةُ المُثَبَّتَةُ بَعدَ الإقلاعِ البارِد</b> — صُفوفُ كُلِّ جَدوَلٍ في <c>platform</c> (عَدا <c>wolverine_</c>)، مَقيسَةً عِندَ <c>9ebb170</c>
    /// عَلى Postgres 16 مَحَلِّيٍّ فارِغ.</summary>
    private static readonly IReadOnlyDictionary<string, long> Cold = new SortedDictionary<string, long>(StringComparer.Ordinal)
    {
         ["mt_doc_cart"] = 1,
         ["mt_doc_conversation"] = 4,
         ["mt_doc_deadletterevent"] = 0,
         ["mt_doc_deal"] = 8,
         ["mt_doc_importedrecord"] = 6,
         ["mt_doc_incubatorsession"] = 1,
         ["mt_doc_listing"] = 42,
         ["mt_doc_message"] = 16,
         ["mt_doc_notification"] = 27,
         ["mt_doc_offer"] = 1,
         ["mt_doc_plan"] = 9,
         ["mt_doc_studiouser"] = 2,
         ["mt_doc_tenant"] = 5,                 // PlatformSeed ×3 (ashare · ejar · order) + AppearanceBaselineSeeder ×2 (theme-demo · owner-test) — الوِلاداتُ الَّتي يَقلِبُها التَبديل
         ["mt_doc_tenantroledefinition"] = 2,
         ["mt_doc_tenantthemedefinition"] = 3,
         ["mt_doc_ticket"] = 1,
         ["mt_doc_user"] = 21,
         ["mt_event_progression"] = 0,
         ["mt_events"] = 70,
         ["mt_high_water_skips"] = 0,
         ["mt_streams"] = 43,
    };

    /// <summary><b>والبَصمَةُ بَعدَ إقلاعٍ دافِئٍ ثانٍ عَلى القاعِدَةِ نَفسِها</b> — حُرّاسُ الوُجودِ يَمنَعونَ الوِلادَةَ الثانِيَة، فَهِيَ البارِدَةُ بِحَرفِها (مَقيسٌ عِندَ
    /// <c>9ebb170</c>: «exists — left untouched» 3). و<c>Insert</c> بِلا حارِسٍ يَرمي هُنا <c>DocumentAlreadyExistsException</c> فَلا يَجهَزُ الإقلاع.</summary>
    private static readonly IReadOnlyDictionary<string, long> Warm = new SortedDictionary<string, long>(StringComparer.Ordinal)
    {
         ["mt_doc_cart"] = 1,
         ["mt_doc_conversation"] = 4,
         ["mt_doc_deadletterevent"] = 0,
         ["mt_doc_deal"] = 8,
         ["mt_doc_importedrecord"] = 6,
         ["mt_doc_incubatorsession"] = 1,
         ["mt_doc_listing"] = 42,
         ["mt_doc_message"] = 16,
         ["mt_doc_notification"] = 27,
         ["mt_doc_offer"] = 1,
         ["mt_doc_plan"] = 9,
         ["mt_doc_studiouser"] = 2,
         ["mt_doc_tenant"] = 5,
         ["mt_doc_tenantroledefinition"] = 2,
         ["mt_doc_tenantthemedefinition"] = 3,
         ["mt_doc_ticket"] = 1,
         ["mt_doc_user"] = 21,
         ["mt_event_progression"] = 0,
         ["mt_events"] = 70,
         ["mt_high_water_skips"] = 0,
         ["mt_streams"] = 43,
    };

    /// <summary><b>المَتاجِرُ المَبذورَة</b> بِمُعَرِّفاتِها مُرَتَّبَة — ما تَلِدُه البَذّاراتُ الثَلاثُ الَّتي يَمَسُّها التَبديل.</summary>
    private const string SeededTenants = "ashare,ejar,order,owner-test,theme-demo";

    private static string AppDll()
    {
        var tfm = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar));
        return Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, "apps", "V1.App", "bin", tfm.Parent!.Name, tfm.Name, "V1.App.dll");
    }

    private static (string Name, string ConnectionString) FreshDatabase()
    {
        var b = new NpgsqlConnectionStringBuilder(LiveDealConcurrencyProofTests.ConnectionString);
        var stem = (b.Database ?? "wasayel").ToLowerInvariant();
        if (stem.Length > 48) stem = stem[..48];
        b.Database = $"{stem}_sf_{Guid.NewGuid().ToString("N")[..8]}";
        return (b.Database, b.ConnectionString);
    }

    private static async Task AdminAsync(string sql)
    {
        await using var conn = new NpgsqlConnection(LiveDealConcurrencyProofTests.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    private static int FreePort()
    {
        using var l = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        l.Start();
        return ((IPEndPoint)l.LocalEndpoint).Port;
    }

    /// <summary>إقلاعٌ واحِدٌ لِلتَطبيقِ المَبنِيّ بِكُلِّ مُفتاحِ بَذر حَتّى <c>/health/ready</c> — البَذرُ يَسبِقُ الاستِماع — ثُمَّ يُطفَأُ بِمُعَرِّفِ عَمَلِيَّتِه.
    /// يُعيدُ سِجِلَّه.</summary>
    private static async Task<IReadOnlyList<string>> BootAsync(string dll, string connectionString)
    {
        var lines = new List<string>();
        Process? app = null;
        try
        {
            var port = FreePort();
            var psi = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, "apps", "V1.App"),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            psi.ArgumentList.Add(dll);
            psi.ArgumentList.Add($"--urls=http://127.0.0.1:{port}");
            psi.Environment["ConnectionStrings__Postgres"] = connectionString;
            psi.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
            psi.Environment["PLATFORM_ADMIN_PHONE"] = "0500000001";
            psi.Environment["PLATFORM_ADMIN_EMAIL"] = "seed-footprint@example.com";
            psi.Environment["TEST_DATA_SEED"] = "1";
            psi.Environment["CART_SAMPLE_SEED"] = "1";
            psi.Environment["INCUBATOR_SAMPLE_SEED"] = "1";
            psi.Environment["APPEARANCE_BASELINE_SEED"] = "1";
            app = Process.Start(psi)!;
            app.OutputDataReceived += (_, e) => { if (e.Data is not null) lock (lines) lines.Add(e.Data); };
            app.ErrorDataReceived += (_, e) => { if (e.Data is not null) lock (lines) lines.Add(e.Data); };
            app.BeginOutputReadLine();
            app.BeginErrorReadLine();

            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            var ready = false;
            for (var i = 0; i < 300 && !ready && !app.HasExited; i++)
            {
                try { ready = (await http.GetStringAsync($"http://127.0.0.1:{port}/health/ready")).Contains("\"ready\":true", StringComparison.Ordinal); }
                catch (Exception) { /* لَم يَستَمِع بَعد — البَذرُ يَسبِقُ الاستِماع */ }
                if (!ready) await Task.Delay(1000);
            }
            string Log() { lock (lines) return string.Join("\n", lines.TakeLast(40)); }
            Assert.True(ready, $"التَطبيقُ لَم يَجهَز (خَرَجَ: {app.HasExited}):\n{Log()}");
        }
        finally
        {
            if (app is { HasExited: false }) { app.Kill(entireProcessTree: true); await app.WaitForExitAsync(); }
            app?.Dispose();
            NpgsqlConnection.ClearAllPools();
        }
        lock (lines) return [.. lines];
    }

    /// <summary>صُفوفُ كُلِّ جَدوَلٍ في المُخَطَّطاتِ غَيرِ النِظامِيَّة — بِـNpgsql لا بِـMarten، فَالعَدُّ لا يُنشِئُ جَدوَلاً ولا يَكتُبُ صَفّاً.</summary>
    private static async Task<List<(string Schema, string Table, long Rows)>> CountAsync(string connectionString)
    {
        var tables = new List<(string Schema, string Table)>();
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        await using (var cmd = new NpgsqlCommand(
            "select n.nspname, c.relname from pg_class c join pg_namespace n on n.oid = c.relnamespace " +
            "where c.relkind in ('r','p') and not c.relispartition " +
            "and n.nspname not in ('pg_catalog','information_schema') and n.nspname not like 'pg\\_%' " +
            "order by n.nspname, c.relname", conn))
        await using (var r = await cmd.ExecuteReaderAsync())
            while (await r.ReadAsync()) tables.Add((r.GetString(0), r.GetString(1)));

        var counted = new List<(string, string, long)>();
        foreach (var (schema, table) in tables)
        {
            await using var cmd = new NpgsqlCommand($"select count(*) from \"{schema}\".\"{table}\"", conn);
            counted.Add((schema, table, Convert.ToInt64(await cmd.ExecuteScalarAsync())));
        }
        return counted;
    }

    private static async Task<string> TenantsAsync(string connectionString)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand($"select coalesce(string_agg(id, ',' order by id collate \"C\"), '') from \"{Schema}\".mt_doc_tenant", conn);
        return Convert.ToString(await cmd.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture) ?? "";
    }

    /// <summary>يُقارِنُ المَقيسَ بِالمُثَبَّت ويَطبَعُ الشَكلَ والفُروق — ويُعيدُ الفُروق.</summary>
    private List<string> Compare(string phase, List<(string Schema, string Table, long Rows)> all, IReadOnlyDictionary<string, long> pinned)
    {
        var compared = all.Where(t => t.Schema == Schema && !t.Table.StartsWith(ExcludedPrefix, StringComparison.Ordinal))
                          .ToDictionary(t => t.Table, t => t.Rows, StringComparer.Ordinal);
        var excluded = all.Where(t => t.Schema == Schema && t.Table.StartsWith(ExcludedPrefix, StringComparison.Ordinal)).ToList();
        foreach (var (table, rows) in compared.OrderBy(t => t.Key, StringComparer.Ordinal))
            output.WriteLine($"[seed-footprint·{phase}] {Schema}.{table} = {rows}");
        output.WriteLine($"[seed-footprint·{phase}] جَداوِلُ مُقارَنَة {compared.Count} · صُفوفُها {compared.Values.Sum()} · مُستَثناةٌ بِـ«{ExcludedPrefix}%» {excluded.Count}"
            + (excluded.Count == 0 ? "" : ": " + string.Join(", ", excluded.Select(t => $"{t.Table}={t.Rows}"))));
        foreach (var g in all.Where(t => t.Schema != Schema).GroupBy(t => t.Schema))
            output.WriteLine($"[seed-footprint·{phase}] خارِجَ {Schema} (يُطبَعُ ولا يُقارَن): {g.Key} — جَداوِل {g.Count()} · صُفوف {g.Sum(t => t.Rows)}"
                + ": " + string.Join(", ", g.Select(t => $"{t.Table}={t.Rows}")));
        output.WriteLine($"[seed-footprint·{phase}] المُقاسُ بِشَكلِ المِلَفّ:\n" + string.Join("\n", compared.OrderBy(t => t.Key, StringComparer.Ordinal)
            .Select(t => $"        [\"{t.Key}\"] = {t.Value},")));

        Assert.True(compared.Count > 0, $"أَداةٌ عَمياء: صِفرُ جَدوَلٍ في {Schema} — التَطبيقُ لَم يَكتُب في القاعِدَةِ الجَديدَة.");
        var diff = compared.Keys.Union(pinned.Keys).OrderBy(k => k, StringComparer.Ordinal)
            .Select(k => (Table: k, Pinned: pinned.TryGetValue(k, out var p) ? p : (long?)null, Measured: compared.TryGetValue(k, out var m) ? m : (long?)null))
            .Where(d => d.Pinned != d.Measured)
            .Select(d => $"{Schema}.{d.Table}: المُثَبَّت {d.Pinned?.ToString() ?? "غائِب"} · المَقيس {d.Measured?.ToString() ?? "غائِب"}")
            .ToList();
        output.WriteLine($"[seed-footprint·{phase}] قورِنَ {pinned.Count} جَدوَلاً مُثَبَّتاً بِـ{compared.Count} مَقيساً — فُروق {diff.Count}");
        return diff;
    }

    [Fact]
    public async Task Every_seed_switch_on_a_fresh_database_leaves_the_pinned_rows_in_every_platform_table_cold_and_warm()
    {
        if (!Enabled) return;
        var dll = AppDll();
        Assert.True(File.Exists(dll), $"التَطبيقُ غَيرُ مَبنِيّ: {dll} — يُبنى الحَلُّ قَبلَ الخُطوَةِ الحَيَّة (ci.yml).");

        var (database, cs) = FreshDatabase();
        await AdminAsync($"create database \"{database}\"");
        output.WriteLine($"[seed-footprint] قاعِدَةٌ جَديدَة: {database}");
        try
        {
            var coldLog = await BootAsync(dll, cs);
            output.WriteLine($"[seed-footprint·بارِد] سِجِلُّ الإقلاع {coldLog.Count} سَطراً · سُطورُ البَذر: "
                + string.Join(" | ", coldLog.Where(l => l.Contains("Seed]", StringComparison.Ordinal) && l.Contains("tenant", StringComparison.OrdinalIgnoreCase)
                                                    || l.Contains("أُنشِئَ المُستَأجِر", StringComparison.Ordinal))));
            var cold = Compare("بارِد", await CountAsync(cs), Cold);
            var tenantsCold = await TenantsAsync(cs);

            var warmLog = await BootAsync(dll, cs);
            output.WriteLine($"[seed-footprint·دافِئ] سِجِلُّ الإقلاع {warmLog.Count} سَطراً · «exists — left untouched» {warmLog.Count(l => l.Contains("exists — left untouched", StringComparison.Ordinal))}");
            var warm = Compare("دافِئ", await CountAsync(cs), Warm);
            var tenantsWarm = await TenantsAsync(cs);
            output.WriteLine($"[seed-footprint] المَتاجِرُ المَبذورَة: بارِد «{tenantsCold}» · دافِئ «{tenantsWarm}»");

            Assert.False(string.IsNullOrEmpty(tenantsCold), "أَداةٌ عَمياء: صِفرُ مَتجَرٍ مَبذور.");
            Assert.True(cold.Count == 0, "بَصمَةُ البَذرِ البارِدِ تَغَيَّرَت في " + Schema + ":\n" + string.Join("\n", cold));
            Assert.True(warm.Count == 0, "بَصمَةُ البَذرِ الدافِئِ تَغَيَّرَت في " + Schema + ":\n" + string.Join("\n", warm));
            Assert.Equal(SeededTenants, tenantsCold);
            Assert.Equal(SeededTenants, tenantsWarm);
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await AdminAsync($"drop database if exists \"{database}\" with (force)");
            output.WriteLine($"[seed-footprint] حُذِفَت {database}");
        }
    }
}
