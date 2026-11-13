#!/usr/bin/env dotnet
#:property JsonSerializerIsReflectionEnabledByDefault=true
// ‏`PublishAot=false` لَيسَ زينَة: تَطبيقُ المِلَفِّ الواحِدِ يَفتَرِضُ AOT فَيُطفِئُ الكودَ الديناميكِيَّ وَقتَ التَشغيل — كَما في marten-schema.cs.
#:property PublishAot=false
#:project ../libs/core/ACommerce.Platform.Hosting/ACommerce.Platform.Hosting.csproj
#:project ../libs/templates/ACommerce.Templates.Customer.Marketplace/ACommerce.Templates.Customer.Marketplace.csproj
// ═══════════════════════════════════════════════════════════════════════
//  مُطابَقَةُ العَدّ — المَصادِرُ مُقابِلَ السُطور، والنِيّاتُ مُقابِلَ السُطور، والسُطورُ مُقابِلَ المَجاميع
// ───────────────────────────────────────────────────────────────────────
//  (‏2026-11-13، الموجَة ١٣ W13-6·٢٠ — ADR-051 §٢٤؛ التَصميم §9 و§13 P7)
//
//  **لِماذا هذا المِلَفّ**: الدَفتَرُ يُكتَبُ في ثَلاثَةِ حُدود (الحِفظ، والصادِر، والحُضور)، والمُجَمِّعُ يَجمَعُه — ولا شَيءَ كانَ يَسأَلُ
//  «أَكُلُّ ما وُلِدَ عُدّ؟ وأَكُلُّ ما عُدَّ وُلِد؟ وأَيُطابِقُ المَجموعُ ما جَمَعَه؟». هذا السُؤالُ لِلقاعِدَةِ كُلِّها، في لَقطَةٍ واحِدَة،
//  بِالدالَّةِ الَّتي تَقرَأُ بِها صَفحَةُ المَتجَرِ مُطابَقَتَه (`MeterReconciliation.RunAsync`) — لا نُسخَةٌ مِنها.
//
//  **والتَركيبُ تَركيبُ التَطبيقِ لا نُسخَةٌ مِنه** (القاعِدَتانِ ١ و٨، كَما في marten-schema.cs): `AddPlatformHost` و`AddCustomerMarketplaceTemplate`
//  بِعَينِهِما، فَأَسماءُ الجَداوِلِ وأَسماءُ الأَحداثِ ما يُشَغِّلُه التَطبيق.
//
//  **قِراءَةٌ فَقَط**: مُعامَلَةٌ واحِدَةٌ `REPEATABLE READ` بِـ`set transaction read only` — لا يُكتَبُ حَرف، ولا يُنشَأُ جَدوَل (كُلُّ جَدوَلٍ يُسأَلُ
//  بِـ`to_regclass` قَبلَ قِراءَتِه). فَيُشَغَّلُ عَلى فَرعِ Neon المَنسوخِ قَبلَ الإنتاج، وعَلى الإنتاجِ بِحُضورِ المالِك.
//
// ─── ما يَطبَعُه ─────────────────────────────────────────────────────────
//   لِكُلِّ وَحدَةِ حِفظ:  compared N · missing M · orphans O · zero-entry stores Z · seed S
//   لِكُلِّ وَحدَةٍ صادِرَة: intents · accepted · missing · orphans · unrecorded · pending · unknown · refused
//   ثُمَّ: aggregates (المُؤَشِّرُ وصُفوفُ agg_daily والفُروق)، presence (الأَيّامُ والغائِبُ واليَتيم)، exclusions (البَذرُ والمَتاجِرُ المُستَورَدَة)،
//   وسَطرُ الحُكم: result · sources compared N · defects D → exit E
//
// ─── الاستِعمال ──────────────────────────────────────────────────────────
//   export ConnectionStrings__Postgres="…"                 # ولا تُطبَع أَبَداً
//   dotnet run scripts/meter-reconcile.cs                  # القاعِدَةُ كُلُّها
//   dotnet run scripts/meter-reconcile.cs -- --store <slug> # مَتجَرٌ واحِد (كَصَفحَتِه)
//
// ─── رُموزُ الخُروج ────────────────────────────────────────────────────
//   0 — قورِنَ مَصدَرٌ واحِدٌ عَلى الأَقَلّ، ولا عَيب.
//   1 — عَيب: ناقِصٌ أَو يَتيمٌ أَو مَتجَرٌ بِمَصادِرَ بِلا سَطرٍ أَو نِيَّةٌ مَقبولَةٌ بِلا سَطرٍ أَو مَجموعٌ لا يُطابِقُ دَفتَرَه أَو سَطرُ حُضورٍ بِلا مُطالَبَة؛
//       أَو **صِفرُ مَصدَرٍ قورِن** (أَداةٌ عَمياء، القاعِدَة ١٠)؛ أَو لا دَفتَر؛ أَو عُطلُ قِراءَة.
//   3 — تَهيئَةٌ ناقِصَة: `ConnectionStrings__Postgres` غائِبٌ أَو لا يُقرَأ.
//
//  **ولا يُطبَعُ حَرفٌ مِن سِلسِلَةِ الاتِّصال**: لاحِقَةُ المُضيفِ وبَصمَةُ sha256 لَه مِن ثَمانِيَةِ أَحرُف — بِعَينِ ما تَطبَعُه marten-schema.cs.
// ═══════════════════════════════════════════════════════════════════════

using System.Security.Cryptography;
using System.Text;
using ACommerce.Platform.Hosting;
using ACommerce.Templates.Customer.Marketplace;
using ACommerce.Templates.Customer.Marketplace.Services.Metering;
using Marten;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;

var storeIdx = Array.IndexOf(args, "--store");
string? slug = storeIdx >= 0 && storeIdx + 1 < args.Length && !args[storeIdx + 1].StartsWith("--") ? args[storeIdx + 1] : null;
if (storeIdx >= 0 && slug is null)
{
    Console.Error.WriteLine("خَطَأ: --store يَحتاجُ سلاجَ مَتجَر.");
    return 1;
}

var cs = Environment.GetEnvironmentVariable("ConnectionStrings__Postgres");
if (string.IsNullOrWhiteSpace(cs))
{
    Console.Error.WriteLine("✗ تَهيئَةٌ ناقِصَة: ‏ConnectionStrings__Postgres غَير مَضبوط — لا قاعِدَةَ مُسَمّاة.");
    return 3;
}

string host;
try { host = new NpgsqlConnectionStringBuilder(cs).Host ?? ""; }
catch (ArgumentException)
{
    Console.Error.WriteLine("✗ تَهيئَةٌ ناقِصَة: ‏ConnectionStrings__Postgres لا يُقرَأُ سِلسِلَةَ Npgsql (لا يُطبَعُ مُحتَواها).");
    return 3;
}
var labels = host.Split('.');
var suffix = labels.Length > 1 ? $"{labels[^2]}.{labels[^1]}" : host;
var hostFp = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(host))).ToLowerInvariant()[..8];

// ─── التَركيب — تَركيبُ التَطبيقِ بِعَينِه (Production: لا `AutoCreate.All`، ولا `appsettings.json`) ─────
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    EnvironmentName = Environments.Production,
    ContentRootPath = AppContext.BaseDirectory,
});
builder.AddPlatformHost();
builder.Services.AddCustomerMarketplaceTemplate();
await using var app = builder.Build();
var store = app.Services.GetRequiredService<IDocumentStore>();

Console.WriteLine($"القاعِدَة:  ***.{suffix}  (بَصمَةُ المُضيف sha256:{hostFp})");
Console.WriteLine($"النِطاق:   {(slug is null ? "كُلُّ المَتاجِر" : "مَتجَرٌ واحِد: " + slug)} — قِراءَةٌ فَقَط");

MeterReconciliationReport report;
try
{
    report = await MeterReconciliation.RunAsync(store, slug);
}
catch (Exception e)
{
    Console.Error.WriteLine($"{MeterReconciliation.LogTag} read failed: {e.GetType().Name} {(e as PostgresException)?.SqlState ?? "-"} → exit 1");
    return 1;
}

foreach (var line in report.Lines()) Console.WriteLine(line);
return report.ExitCode;
