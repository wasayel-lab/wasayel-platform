using System.Diagnostics;
using System.Net;
using System.Text.RegularExpressions;
using ACommerce.Platform.Shared.Metering;
using ACommerce.Templates.Customer.Marketplace.Services.Metering;
using Npgsql;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ إقلاعٌ بارِدٌ حَقيقِيّ: الدَفتَرُ يُنشَأُ قَبلَ البَذر، والصَفقَةُ المَبذورَةُ دَورُها ورائِدُ أَعمالِها `_seed` لا `_unresolved` ═══
// (‏2026-11-12، الموجَة ١٣ W13-6·٣ — ADR-051 §١١؛ التَصميم §4 و§5.4، RULES-06). **التَطبيقُ نَفسُه لا نُسخَةٌ مِنه**: البَذّاراتُ في `V1.App`
// ولا يُشيرُ إلَيه مَشروعُ الاختِبار، فَيُشَغَّلُ تَجميعُه المَبنِيُّ (`apps/V1.App/bin/<التَهيِئَة>/net10.0/V1.App.dll`، وهُوَ ما تَبنيه خُطوَةُ CI قَبلَ
// الحَيّ) عَلى قاعِدَةٍ فارِغَةٍ يُنشِئُها الاختِبارُ ويَحذِفُها، بِبَذرِ التَطوير (`TEST_DATA_SEED` و`APPEARANCE_BASELINE_SEED`). ويُطفَأُ بِمُعَرِّفِ عَمَلِيَّتِه.
//
// **«قَبلَ البَذر» مَقيسٌ بِمُعَرِّفِ المُعامَلَة لا بِتَرتيبِ السِجِلّ**: سِجِلُّ المُسَجِّلِ يُكتَبُ مِن طابورٍ في الخَلفِيَّة فَتَرتيبُه مَعَ `Console.WriteLine`
// البَذّاراتِ لا يُوثَقُ بِه (القاعِدَة ١٠). أَمّا `meter.generation.start_xid` فَمُعامَلَةُ الإقلاع، و`writer_xid` كُلِّ سَطرٍ مَبذورٍ مُعامَلَةُ بَذرِه —
// وكِلاهُما `xid8` فَالمُقارَنَةُ دَقيقَة: لَو أَقلَعَ الدَفتَرُ بَعدَ البَذرِ (والمُستَمِعُ يَضمَنُ الجَدوَلَ بِنَفسِه) لَكانَت سُطورُ البَذرِ أَصغَر.
//
// ‏(‏2026-11-12، W13-6·٧ — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه) **وصارَ المُعجَمُ خَمسَ وَحَدات** والبَذرُ يَكتُبُ أَحداثاً مَعدودَة (إعلاناتُ `PlatformSeed.cs:185`
// وبَذرِ الاختِبار، وعَرضُ بَذرِ المَظهَر): فَسَطرُ الإقلاعِ ونَوافِذُه مِن المُعجَمِ المَضمونِ نَفسِه لا رَقَماً مَنسوخاً، و«سُطورُ البَذرِ = ما بُذِر» لِكُلِّ وَحدَة —
// الصَفَقاتُ بِـ`mt_doc_deal`، وكُلُّ وَحدَةِ حَدَثٍ بِعَدَدِ حَدَثِ وِلادَتِها في `mt_events` (قاعِدَةٌ جَديدَةٌ لا يَكتُبُ فيها إلّا البَذر).
//
// ‏(‏2026-11-12، W13-6·٨ — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه) **ووَحَداتُ الوَثائِقِ الأَربَعُ تَحتَ البَذرِ أَيضاً**: المَتاجِرُ المَبذورَةُ (قُلِبَت وِلاداتُها `Insert`)
// والأَعضاءُ وحِسابُ المُشرِف. والدَورُ `_seed` لِكُلِّ سَطر؛ أَمّا رائِدُ الأَعمالِ فَمِن الوَثيقَةِ حَيثُ تَحمِلُه (التَصميم §5.4، الخُطوَةُ ١): `store.create` مالِكُ
// المَتجَرِ لَحظَةَ وِلادَتِه — والمَبذورَةُ تولَدُ بِلا مالِكٍ ثُمَّ يُسنَدُ (`_unowned`) — و`studio.account.create` الحِسابُ نَفسُه. فَـ«الدَفتَرُ كُلُّه `_seed`»
// صارَ بِالدَور، ورائِدُ الأَعمالِ `_seed` لِما سِوى الوَحدَتَين.

[Collection(LiveProofCollection.Name)]
public class LiveMeterBootProofTests(ITestOutputHelper output)
{
    private static bool Enabled
        => LiveDealConcurrencyProofTests.Enabled && !string.IsNullOrEmpty(LiveDealConcurrencyProofTests.ConnectionString);

    /// <summary>تَهيِئَةُ البِناءِ مِن مَسارِ الاختِبارِ نَفسِه (<c>bin/Release/net10.0</c>) — فَيُشَغَّلُ التَطبيقُ المَبنِيُّ بِالتَهيِئَةِ ذاتِها.</summary>
    private static string AppDll()
    {
        var tfm = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar));
        var configuration = tfm.Parent!.Name;
        return Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, "apps", "V1.App", "bin", configuration, tfm.Name, "V1.App.dll");
    }

    private static (string Name, string ConnectionString) FreshDatabase()
    {
        var b = new NpgsqlConnectionStringBuilder(LiveDealConcurrencyProofTests.ConnectionString);
        var stem = (b.Database ?? "wasayel").ToLowerInvariant();
        if (stem.Length > 48) stem = stem[..48];
        b.Database = $"{stem}_mb_{Guid.NewGuid().ToString("N")[..8]}";
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

    /// <summary>مُعَرِّفُ الصَفقَةِ المَبذورَةِ النَشِطَة (<c>AppearanceBaselineSeeder.EjarActiveDealId</c>) — مِن مَصدَرِ البَذّار، فَالبُرهانُ عَلى صَفقَتِه بِعَينِها.</summary>
    private static Guid SeededActiveDealId()
    {
        var seeder = File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, "apps", "V1.App", "Seed", "AppearanceBaselineSeeder.cs"));
        var m = Regex.Match(seeder, @"EjarActiveDealId\s*=\s*Guid\.Parse\(""(?<g>[0-9a-f-]{36})""\)");
        Assert.True(m.Success, "مُعَرِّفُ الصَفقَةِ المَبذورَةِ غائِبٌ عَن AppearanceBaselineSeeder.cs — الأَداةُ عَمياء.");
        Assert.Contains("s.Store(new Deal", seeder, StringComparison.Ordinal);
        return Guid.Parse(m.Groups["g"].Value);
    }

    [Fact]
    public async Task A_cold_boot_creates_the_ledger_before_the_seeders_and_the_seeded_deal_is_counted_as_seed()
    {
        if (!Enabled) return;
        var dll = AppDll();
        Assert.True(File.Exists(dll), $"التَطبيقُ غَيرُ مَبنِيّ: {dll} — يُبنى الحَلُّ قَبلَ الخُطوَةِ الحَيَّة (ci.yml).");
        var seeded = SeededActiveDealId();

        var (database, cs) = FreshDatabase();
        await AdminAsync($"create database \"{database}\"");
        output.WriteLine($"[boot] قاعِدَةٌ جَديدَة: {database}");
        var lines = new List<string>();
        try
        {
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
                psi.Environment["ConnectionStrings__Postgres"] = cs;
                psi.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
                psi.Environment["PLATFORM_ADMIN_PHONE"] = "0500000001";
                psi.Environment["TEST_DATA_SEED"] = "1";
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
            }

            string[] boot;
            lock (lines) boot = lines.Where(l => l.Contains("[meter] boot", StringComparison.Ordinal))
                                      .Select(l => l[l.IndexOf("[meter] boot", StringComparison.Ordinal)..].Trim()).ToArray();
            output.WriteLine($"[boot] سِجِلُّ الإقلاع ({lines.Count} سَطراً): {string.Join(" | ", boot)}");
            var units = MeterUnitCatalog.Default.Units.Count;
            Assert.Equal(new[] { $"[meter] boot generation=1 units={units} epochs_new={units}" }, boot);

            await using var conn = new NpgsqlConnection(cs);
            await conn.OpenAsync();
            async Task<string> One(string sql)
            {
                await using var cmd = new NpgsqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("d", seeded.ToString("D"));
                return Convert.ToString(await cmd.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture) ?? "";
            }

            var generations = await One("select string_agg(generation::text, ',') from meter.generation");
            var epochs = await One("select string_agg(unit, ',' order by unit collate \"C\") from meter.unit_epoch");
            var seededRow = await One("select app || '|' || entrepreneur || '|' || role || '|' || generation from meter.usage_entry where source_id = @d");
            var bySeed = await One("select count(*) from meter.usage_entry where role = '_seed' and entrepreneur = '_seed' and unit = 'deal.create'");
            var unresolved = await One("select count(*) from meter.usage_entry where role = '_unresolved' or entrepreneur = '_unresolved'");
            var before = await One("select bool_and(g.start_xid < e.writer_xid and u.since_xid < e.writer_xid) from meter.usage_entry e, meter.generation g, meter.unit_epoch u where e.role = '_seed'");
            var seededDeals = await One("select count(*) from platform.mt_doc_deal");
            output.WriteLine($"[boot] الأَجيال {generations} · النَوافِذ {epochs} · الصَفقَةُ المَبذورَة {seeded}: {seededRow} · "
                + $"سُطورُ البَذر {bySeed} مِن صَفَقاتٍ {seededDeals} · _unresolved {unresolved} · الدَفتَرُ قَبلَ البَذر {before}");

            // ‏لِكُلِّ وَحدَةِ حَدَث: حَدَثُ وِلادَتِها في mt_events (البَذرُ وَحدَه يَكتُبُ هُنا) = سُطورُها تَحتَ `_seed`.
            var births = new List<(string Unit, string Events, string Seeds)>();
            foreach (var (type, unit) in MeterRules.StreamBirths.OrderBy(b => b.Value, StringComparer.Ordinal))
                births.Add((unit, await One($"select count(*) from platform.mt_events where mt_dotnet_type like '{type.FullName},%'"),
                            await One($"select count(*) from meter.usage_entry where unit = '{unit}' and role = '_seed' and entrepreneur = '_seed'")));
            var allRows = await One("select count(*) from meter.usage_entry");
            var allSeed = await One("select count(*) from meter.usage_entry where role = '_seed'");
            // ‏(W13-6·٨) رائِدُ الأَعمالِ مِن الوَثيقَةِ لِوَحدَتَين، و`_seed` لِما سِواهُما؛ ولِكُلِّ وَحدَةِ وَثيقَةٍ سُطورٌ بِعَدَدِ وَثائِقِها المَبذورَة.
            var notSeedOwner = await One("select coalesce(string_agg(distinct unit, ',' order by unit), '') from meter.usage_entry where entrepreneur <> '_seed'");
            var stores = (await One("select count(*) from meter.usage_entry where unit = 'store.create' and entrepreneur = '_unowned' and source_id = app"),
                          await One("select count(*) from platform.mt_doc_tenant"));
            var accounts = (await One("select count(*) from meter.usage_entry where unit = 'studio.account.create' and entrepreneur = source_id and app = '_platform'"),
                            await One("select count(*) from platform.mt_doc_studiouser"));
            var members = (await One("select count(*) from meter.usage_entry where unit = 'member.join' and entrepreneur = '_seed'"),
                           await One("select count(*) from platform.mt_doc_user"));
            output.WriteLine($"[boot] وَحَداتُ الوَثائِق (سُطور/وَثائِق): store.create {stores.Item1}/{stores.Item2} · studio.account.create {accounts.Item1}/{accounts.Item2} · "
                + $"member.join {members.Item1}/{members.Item2} · رائِدُ أَعمالٍ غَيرُ _seed في «{notSeedOwner}»");
            output.WriteLine($"[boot] وَحَداتُ الأَحداث (أَحداثُ الوِلادَة/سُطورُ _seed): {string.Join(" · ", births.Select(b => $"{b.Unit} {b.Events}/{b.Seeds}"))} · "
                + $"الدَفتَرُ {allRows} · مِنها _seed {allSeed}");

            Assert.Equal("1", generations);
            Assert.Equal(string.Join(',', MeterUnitCatalog.Default.Units.Select(u => u.Code).Order(StringComparer.Ordinal)), epochs);
            Assert.All(births, b => Assert.Equal(b.Events, b.Seeds));
            Assert.True(long.Parse(births.Single(b => b.Unit == MeterRules.ListingCreate).Seeds) >= 1, "أَداةٌ عَمياء: صِفرُ إعلانٍ مَبذور.");
            Assert.Equal(allRows, allSeed);
            Assert.Equal("store.create,studio.account.create", notSeedOwner);
            Assert.Equal(stores.Item2, stores.Item1);
            Assert.Equal(accounts.Item2, accounts.Item1);
            Assert.Equal(members.Item2, members.Item1);
            Assert.True(long.Parse(stores.Item1) >= 3 && long.Parse(accounts.Item1) >= 1 && long.Parse(members.Item1) >= 1,
                        $"أَداةٌ عَمياء: مَتاجِر {stores.Item1} · حِسابات {accounts.Item1} · أَعضاء {members.Item1}.");
            Assert.Equal("ejar|_seed|_seed|1", seededRow);
            Assert.True(long.Parse(bySeed) >= 2, $"أَداةٌ عَمياء: {bySeed} سَطرَ بَذر.");
            Assert.Equal(seededDeals, bySeed);
            Assert.Equal("0", unresolved);
            Assert.Equal("True", before);
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await AdminAsync($"drop database if exists \"{database}\" with (force)");
            output.WriteLine($"[boot] حُذِفَت {database}");
        }
    }
}
