using System.Diagnostics;
using System.Net;
using ACommerce.Platform.Shared.Metering;
using Npgsql;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ جانِبُ التَشغيلِ في دَفتَرِ إقلاعٍ حَقيقِيّ — يُثَبَّتُ قَبلَ أَن يَكتُبَ الإقلاعُ جانِبَ التِقنِيَة (‏2026-11-13، الموجَة ١٣ W13-6·١٩ — ADR-051) ═══
//
// **لِماذا قَبلَ التَبديل** (القاعِدَتانِ ٣ و١٣): البَندُ ١٩ يَجعَلُ إقلاعَ التَطبيقِ (`ApplicationStarted` في `Program.cs`) يُجَدوِلُ لَقطَةَ الحُضورِ اليَومِيَّة
// (`TakePresenceSnapshot`) في طابورِ Wolverine المَتين، فَتَكتُبُ اللَقطَةُ سُطورَ جانِبِ التِقنِيَةِ (`presence.*`) في الدَفتَرِ نَفسِه — الجَدوَلِ الَّذي يَحمِلُ
// جانِبَ التَشغيلِ (عَمَلِيّاتُ البَذرِ تَحتَ `_seed`). ووَعدُ مَبدَإِ المالِكِ أَنّ **الجانِبَينِ لا يَختَلِطان** (ADR-051 §٢): ما يَكتُبُه الإقلاعُ في جانِبِ
// التَشغيلِ لا يَتَغَيَّرُ سَطراً بِأَنَّ اللَقطَةَ صارَت تَعمَلُ مَعَه. والوَعدُ لا يُحفَظُ بِاسمِه (القاعِدَة ٢): هذا المِلَفُّ يُقلِعُ التَطبيقَ المَبنِيَّ نَفسَه
// (`apps/V1.App/bin/<التَهيِئَة>/net10.0/V1.App.dll`) عَلى قاعِدَةٍ فارِغَةٍ جَديدَةٍ بِكُلِّ مُفتاحِ بَذر، ويَترُكُه حَتّى يَسكُنَ دَفتَرُه، ويَعُدُّ سُطورَ
// **وَحَداتِ جانِبِ التَشغيلِ** — مِن المُعجَمِ المَضمونِ نَفسِه (`MeterSides.Operation`) لا مِن قائِمَةٍ مَنسوخَة — بِالوَحدَةِ والمَتجَرِ والدَورِ ونَوعِ رائِدِ
// الأَعمال، ويُثَبِّتُها بِالرَقَم. ويُودَعُ وَحدَه، ثُمَّ يُعيدُ كوميتُ التَبديلِ تَشغيلَه **بِلا تَعديلِ حَرف** (بَصمَتُه في رِسالَتِه).
//
// **وجانِبُ التِقنِيَةِ يُطبَعُ ولا يُقارَن**: قَبلَ التَبديلِ صِفرٌ (لا وَحدَةَ حُضورٍ في المُعجَم)، وبَعدَه ما كَتَبَته اللَقطَةُ إن سَبَقَت الإطفاء —
// والحُكمُ عَلَيه في بُرهانِه (`LivePresenceSnapshotProofTests`). و`platform.*` يَحرُسُه `LiveMeterSeededFootprintCharacterizationProofTests` بِلا حَرف.
//
// **«يَسكُنُ الدَفتَر»**: بَعدَ `/health/ready` يُعَدُّ الدَفتَرُ كُلَّ ثانِيَةٍ حَتّى يَثبُتَ ثَلاثَ قِراءاتٍ مُتَتالِيَة (وثَلاثونَ ثانِيَةً حَدٌّ أَعلى) — فَما تَكتُبُه
// خَلفِيَّةُ الإقلاعِ يَقَعُ قَبلَ العَدّ لا بَعدَه، ولا رَقَمَ يُقاسُ عَلى نِصفِ كِتابَة.
//
// **والأَداةُ تُقاسُ قَبلَ أَن يُوثَقَ بِها** (القاعِدَة ١٠): تَطبَعُ عَدَدَ وَحَداتِ التَشغيلِ المَفحوصَةِ والمَفاتيحِ والسُطور، وتَفشَلُ عَلى صِفرِ سَطرٍ أَو صِفرِ
// وَحدَة، وعَلى مِفتاحٍ زائِدٍ أَو ناقِصٍ أَو رَقَمٍ مُختَلِف.

[Collection(LiveProofCollection.Name)]
public class LiveMeterBootOperationsCharacterizationProofTests(ITestOutputHelper output)
{
    private static bool Enabled
        => LiveDealConcurrencyProofTests.Enabled && !string.IsNullOrEmpty(LiveDealConcurrencyProofTests.ConnectionString);

    /// <summary>
    /// <para><b>المُثَبَّتُ بَعدَ إقلاعٍ بارِدٍ بِكُلِّ مُفتاحِ بَذر</b> — <c>الوَحدَة|المَتجَر|الدَور|نَوعُ رائِدِ الأَعمال</c> ← عَدَدُ السُطور، لِوَحَداتِ جانِبِ التَشغيلِ
    /// وَحدَها. ونَوعُ رائِدِ الأَعمالِ القيمَةُ الحارِسَةُ بِعَينِها، أَو <c>id</c> لِمُعَرِّفِ حِسابِ استوديو. مَقيسٌ عِندَ <c>6a104bf</c> عَلى Postgres 16 مَحَلِّيٍّ فارِغ.</para>
    /// </summary>
    private static readonly IReadOnlyDictionary<string, long> Pinned = new SortedDictionary<string, long>(StringComparer.Ordinal)
    {
        ["chat.message|ashare|_seed|_seed"] = 4,
        ["chat.message|ejar|_seed|_seed"] = 8,
        ["chat.message|order|_seed|_seed"] = 4,
        ["deal.create|ashare|_seed|_seed"] = 2,
        ["deal.create|ejar|_seed|_seed"] = 4,
        ["deal.create|order|_seed|_seed"] = 2,
        ["listing.create|ashare|_seed|_seed"] = 11,
        ["listing.create|ejar|_seed|_seed"] = 16,
        ["listing.create|order|_seed|_seed"] = 13,
        ["listing.create|theme-demo|_seed|_seed"] = 2,
        ["member.join|ashare|_seed|_seed"] = 3,
        ["member.join|ejar|_seed|_seed"] = 4,
        ["member.join|order|_seed|_seed"] = 3,
        ["member.join|owner-test|_seed|_seed"] = 3,
        ["member.join|theme-demo|_seed|_seed"] = 8,
        ["offer.submit|theme-demo|_seed|_seed"] = 1,
        ["store.create|ashare|_seed|_unowned"] = 1,
        ["store.create|ejar|_seed|_unowned"] = 1,
        ["store.create|order|_seed|_unowned"] = 1,
        ["store.create|owner-test|_seed|_unowned"] = 1,
        ["store.create|theme-demo|_seed|_unowned"] = 1,
        ["studio.account.create|_platform|_seed|id"] = 2,
    };

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
        b.Database = $"{stem}_bo_{Guid.NewGuid().ToString("N")[..8]}";
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

    private static async Task<long> LedgerRowsAsync(string connectionString)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("select case when to_regclass('meter.usage_entry') is null then -1 else (select count(*) from meter.usage_entry) end", conn);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    /// <summary>إقلاعٌ واحِدٌ لِلتَطبيقِ المَبنِيّ بِكُلِّ مُفتاحِ بَذر حَتّى <c>/health/ready</c>، ثُمَّ حَتّى يَسكُنَ الدَفتَر — ثُمَّ يُطفَأُ بِمُعَرِّفِ عَمَلِيَّتِه.
    /// يُعيدُ سِجِلَّه وعَدَدَ قِراءاتِ السُكون.</summary>
    private static async Task<(IReadOnlyList<string> Log, int Polls, long Rows)> BootAsync(string dll, string connectionString)
    {
        var lines = new List<string>();
        Process? app = null;
        var polls = 0;
        long rows = -1;
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
            psi.Environment["PLATFORM_ADMIN_EMAIL"] = "boot-operations@example.com";
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

            // ‏الدَفتَرُ يَسكُن: ثَلاثُ قِراءاتٍ مُتَتالِيَةٌ بِالعَدَدِ نَفسِه، أَو ثَلاثونَ ثانِيَة.
            var stable = 0;
            for (; polls < 30 && stable < 3 && !app.HasExited; polls++)
            {
                await Task.Delay(1000);
                var now = await LedgerRowsAsync(connectionString);
                stable = now == rows ? stable + 1 : 0;
                rows = now;
            }
        }
        finally
        {
            if (app is { HasExited: false }) { app.Kill(entireProcessTree: true); await app.WaitForExitAsync(); }
            app?.Dispose();
            NpgsqlConnection.ClearAllPools();
        }
        lock (lines) return ([.. lines], polls, rows);
    }

    /// <summary>سُطورُ وَحَداتِ جانِبِ التَشغيلِ بِمَفاتيحِها، وعَدَدُ ما سِواها (جانِبُ التِقنِيَة) — بِـNpgsql لا بِـMarten.</summary>
    private static async Task<(SortedDictionary<string, long> Operations, long Others)> CountAsync(string connectionString, string[] operationUnits)
    {
        var measured = new SortedDictionary<string, long>(StringComparer.Ordinal);
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        await using (var cmd = new NpgsqlCommand(
            "select unit || '|' || app || '|' || role || '|' || case when entrepreneur like '\\_%' then entrepreneur else 'id' end, count(*) "
            + "from meter.usage_entry where unit = any(@ops) group by 1", conn))
        {
            cmd.Parameters.AddWithValue("ops", operationUnits);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync()) measured[r.GetString(0)] = r.GetInt64(1);
        }
        await using var others = new NpgsqlCommand("select count(*) from meter.usage_entry where unit <> all(@ops)", conn);
        others.Parameters.AddWithValue("ops", operationUnits);
        return (measured, Convert.ToInt64(await others.ExecuteScalarAsync()));
    }

    [Fact]
    public async Task A_cold_boot_with_every_seed_switch_leaves_the_pinned_operation_rows_whatever_the_technology_side_writes()
    {
        if (!Enabled) return;
        var dll = AppDll();
        Assert.True(File.Exists(dll), $"التَطبيقُ غَيرُ مَبنِيّ: {dll} — يُبنى الحَلُّ قَبلَ الخُطوَةِ الحَيَّة (ci.yml).");
        var operations = MeterUnitCatalog.Default.Units.Where(u => u.Side == MeterSides.Operation).Select(u => u.Code).Order(StringComparer.Ordinal).ToArray();
        Assert.True(operations.Length > 0, "أَداةٌ عَمياء: صِفرُ وَحدَةٍ في جانِبِ التَشغيل.");

        var (database, cs) = FreshDatabase();
        await AdminAsync($"create database \"{database}\"");
        output.WriteLine($"[boot-operations] قاعِدَةٌ جَديدَة: {database} · وَحَداتُ جانِبِ التَشغيلِ في المُعجَم {operations.Length}");
        try
        {
            var (log, polls, rows) = await BootAsync(dll, cs);
            output.WriteLine($"[boot-operations] سِجِلُّ الإقلاع {log.Count} سَطراً · «{string.Join(" | ", log.Where(l => l.Contains("[meter]", StringComparison.Ordinal)).Select(l => l[l.IndexOf("[meter]", StringComparison.Ordinal)..].Trim()))}»"
                + $" · سَكَنَ الدَفتَرُ بَعدَ {polls} قِراءَةً عِندَ {rows} سَطراً");

            var (measured, others) = await CountAsync(cs, operations);
            foreach (var (key, n) in measured) output.WriteLine($"[boot-operations] {key} = {n}");
            output.WriteLine($"[boot-operations] مَفاتيحُ جانِبِ التَشغيل {measured.Count} · سُطورُه {measured.Values.Sum()} · سُطورُ ما سِواه (جانِبُ التِقنِيَة، يُطبَعُ ولا يُقارَن) {others}");
            output.WriteLine("[boot-operations] المُقاسُ بِشَكلِ المِلَفّ:\n" + string.Join("\n", measured.Select(m => $"        [\"{m.Key}\"] = {m.Value},")));

            Assert.True(measured.Count > 0 && measured.Values.Sum() > 0, "أَداةٌ عَمياء: صِفرُ سَطرٍ في جانِبِ التَشغيلِ بَعدَ إقلاعٍ بِكُلِّ مُفتاحِ بَذر.");
            var diff = measured.Keys.Union(Pinned.Keys).Order(StringComparer.Ordinal)
                .Select(k => (Key: k, Pinned: Pinned.TryGetValue(k, out var p) ? p : (long?)null, Measured: measured.TryGetValue(k, out var m) ? m : (long?)null))
                .Where(d => d.Pinned != d.Measured)
                .Select(d => $"{d.Key}: المُثَبَّت {d.Pinned?.ToString() ?? "غائِب"} · المَقيس {d.Measured?.ToString() ?? "غائِب"}")
                .ToList();
            output.WriteLine($"[boot-operations] قورِنَ {Pinned.Count} مِفتاحاً مُثَبَّتاً بِـ{measured.Count} مَقيساً — فُروق {diff.Count}");
            Assert.True(diff.Count == 0, "جانِبُ التَشغيلِ في دَفتَرِ الإقلاعِ تَغَيَّر:\n" + string.Join("\n", diff));
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await AdminAsync($"drop database if exists \"{database}\" with (force)");
            output.WriteLine($"[boot-operations] حُذِفَت {database}");
        }
    }
}
