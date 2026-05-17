using ACommerce.Importer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var argParser = new ArgParser(args);

var host = Host.CreateDefaultBuilder(args)
    .ConfigureAppConfiguration(cfg =>
    {
        cfg.AddJsonFile("appsettings.json", optional: false, reloadOnChange: false);
        cfg.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false);
        cfg.AddEnvironmentVariables(prefix: "ACOMMERCE_IMPORTER_");
    })
    .ConfigureServices((ctx, services) =>
    {
        services.Configure<ImporterOptions>(ctx.Configuration);
        services.AddSingleton<TargetWriter>();
        services.AddSingleton<AshareImporter>();
        services.AddSingleton<EjarImporter>();
    })
    .ConfigureLogging(lb => lb.AddSimpleConsole(o => { o.SingleLine = true; o.TimestampFormat = "HH:mm:ss "; }))
    .Build();

var logger = host.Services.GetRequiredService<ILogger<Program>>();
var opts   = host.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<ImporterOptions>>().Value;

// override بِـ CLI
if (argParser.Reset.HasValue) opts.Options ??= new();
if (argParser.Reset.HasValue) opts.Options!.Reset = argParser.Reset.Value;

logger.LogInformation("Target Postgres: {Cs}", Redact(opts.Target?.Postgres));
logger.LogInformation("Reset existing data: {Reset}", opts.Options?.Reset ?? false);

if (string.IsNullOrWhiteSpace(opts.Target?.Postgres))
{
    logger.LogError("Target:Postgres مَطلوب في appsettings.json. أَوقَفت.");
    return 1;
}

var target = host.Services.GetRequiredService<TargetWriter>();
await target.InitAsync();

var which = argParser.Tenant; // "ashare" | "ejar" | null=both
var ranAshare = false;
var ranEjar   = false;

if ((which is null or "ashare") && !string.IsNullOrWhiteSpace(opts.Source?.Ashare))
{
    logger.LogInformation("─── Ashare V3 → platform-v1 (slug=ashare) ───");
    await host.Services.GetRequiredService<AshareImporter>()
        .RunAsync(opts.Source!.Ashare!, opts.Options?.Reset == true);
    ranAshare = true;
}
else if (which == "ashare")
{
    logger.LogWarning("--tenant ashare طُلِب لَكِنّ Source:Ashare فارِغ. تَخَطَّيت.");
}

if ((which is null or "ejar") && !string.IsNullOrWhiteSpace(opts.Source?.Ejar))
{
    logger.LogInformation("─── Ejar V1 → platform-v1 (slug=ejar) ───");
    await host.Services.GetRequiredService<EjarImporter>()
        .RunAsync(opts.Source!.Ejar!, opts.Options?.Reset == true);
    ranEjar = true;
}
else if (which == "ejar")
{
    logger.LogWarning("--tenant ejar طُلِب لَكِنّ Source:Ejar فارِغ. تَخَطَّيت.");
}

if (!ranAshare && !ranEjar)
{
    logger.LogWarning("لم يَعمَل أَيّ importer. ضَع Source:Ashare و/أو Source:Ejar في appsettings.");
    return 2;
}

logger.LogInformation("✅ تَمّ الاستيراد.");
return 0;

static string Redact(string? cs)
{
    if (string.IsNullOrEmpty(cs)) return "(empty)";
    return System.Text.RegularExpressions.Regex.Replace(cs, @"(Password|Pwd)\s*=\s*[^;]+", "$1=***", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
}

namespace ACommerce.Importer
{
    internal sealed class ArgParser
    {
        public string? Tenant { get; }
        public bool? Reset { get; }
        public ArgParser(string[] args)
        {
            for (var i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--tenant" when i + 1 < args.Length:
                        Tenant = args[i + 1].Trim().ToLowerInvariant();
                        i++;
                        break;
                    case "--reset":
                        Reset = true;
                        break;
                    case "--no-reset":
                        Reset = false;
                        break;
                }
            }
        }
    }

    public sealed class ImporterOptions
    {
        public TargetSection?  Target  { get; set; }
        public SourceSection?  Source  { get; set; }
        public OptionsSection? Options { get; set; }
    }
    public sealed class TargetSection  { public string? Postgres { get; set; } }
    public sealed class SourceSection  { public string? Ashare { get; set; } public string? Ejar { get; set; } }
    public sealed class OptionsSection { public bool Reset { get; set; } }
}
