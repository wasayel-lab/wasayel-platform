using System.Runtime.InteropServices;
using ACommerce.V1.Meter;

// ═══ V1.Meter — مُجَمِّعُ العَدِّ الداخِلِيّ، عَمَلِيَّةٌ مُنفَصِلَة (‏2026-11-12، الموجَة ١٣ W13-6·٤ — ADR-051 §١٢؛ التَصميم §10.2) ═══════
//
//   dotnet run --project apps/V1.Meter -- --once            # يَلحَقُ بِلَقطَةِ البَدءِ ثُمَّ يَخرُج
//   dotnet run --project apps/V1.Meter -- --once --rebuild  # يُفَرِّغُ المَجاميعَ ويُعيدُ المُؤَشِّرَ تَحتَ القُفلِ ثُمَّ يَلحَق
//   dotnet V1.Meter.dll --interval 60                       # آلَةُ العَدِّ في الإنتاج (البَند ٢٢)
//
// **الاتِّصالُ مِن البيئَةِ وَحدَها** — `ConnectionStrings__Postgres`، المُتَغَيِّرُ نَفسُه الَّذي يَقرَؤُه التَطبيق — ولا يُطبَعُ مِنه إلّا المُضيفُ
// واسمُ القاعِدَة (لِيُتَحَقَّقَ مِن المُضيفِ قَبلَ كُلِّ تَشغيل). **ولا مَنفَذَ ولا مُستَمِع**: لا يُدفَعُ إلَيه شَيء (G10).
// رُموزُ الخُروج: 0 تَمّ · 1 وَسيطٌ أَو عُطل · 2 أُفُقٌ مُمسَك · 3 قُفلٌ مُمسَك · 4 تَراجُعٌ في مُعَرِّفِ المُعامَلَة (`ExitCodes`).

var (options, error) = AggregatorOptions.Parse(args);
if (options is null)
{
    Console.Error.WriteLine($"[meter-agg] {error}");
    Console.Error.WriteLine(AggregatorOptions.Usage);
    return ExitCodes.Failure;
}

var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Postgres");
if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.Error.WriteLine("[meter-agg] ConnectionStrings__Postgres is not set — nothing to read");
    return ExitCodes.Failure;
}

using var stop = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };
PosixSignalRegistration? terminate = null;
try { terminate = PosixSignalRegistration.Create(PosixSignal.SIGTERM, c => { c.Cancel = true; stop.Cancel(); }); }
catch (PlatformNotSupportedException) { /* مِنَصَّةٌ بِلا SIGTERM — Ctrl+C وَحدَه */ }

try
{
    return await new Aggregator(connectionString, options, Console.Out).RunAsync(stop.Token);
}
catch (OperationCanceledException) when (stop.IsCancellationRequested)
{
    Console.Out.WriteLine("[meter-agg] stopped");
    return ExitCodes.Ok;
}
catch (Exception e)
{
    Console.Error.WriteLine($"[meter-agg] failed — {e.GetType().Name}: {e.Message}");
    return ExitCodes.Failure;
}
finally
{
    terminate?.Dispose();
}
