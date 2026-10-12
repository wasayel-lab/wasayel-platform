#!/usr/bin/env dotnet
#:property JsonSerializerIsReflectionEnabledByDefault=true
#:project ../libs/core/ACommerce.Platform.Hosting/ACommerce.Platform.Hosting.csproj
#:project ../libs/templates/ACommerce.Templates.Customer.Marketplace/ACommerce.Templates.Customer.Marketplace.csproj
// ═══════════════════════════════════════════════════════════════════════
//  مُخَطَّطُ Marten — عَرضٌ ثُمَّ تَطبيقٌ ثُمَّ تَوكيدٌ عَلى قاعِدَةٍ مُسَمّاة، قَبلَ النَشر
// ───────────────────────────────────────────────────────────────────────
//  **لِماذا هذا المِلَفّ** (‏P3-1، ‏2026-10-12): الإنتاجُ يُغَيِّرُ مُخَطَّطَه بِنَفسِه، ولا أَحَدَ يَرى ما
//  يُغَيِّرُه قَبلَ أَن يَقَع. `HostingExtensions` يَضبِطُ `AutoCreate.All` في Development وَحدَه، وخارِجَه
//  يَبقى افتِراضُ Marten — **وهُوَ `CreateOrUpdate` لا «لا شَيء»** (مَقيسٌ عَلى Marten 9.22.5:
//  `new StoreOptions().AutoCreateSchemaObjects`). فَأَوَّلُ استِعمالٍ لِنَوعِ وَثيقَةٍ في الإنتاجِ يُنشِئُ
//  جَدوَلَه أَو يُعَدِّلُه ساعَتَها — مَقيس: استِعلامانِ عَلى قاعِدَةٍ فارِغَةٍ بِتَركيبِ Production أَنشَآ
//  `mt_doc_tenant` و`mt_doc_incubatorsession` — بِلا لَقطَةٍ ولا عَرضٍ ولا تَوكيد. وهذا ما تُسَمّيه
//  `CLAUDE.md` «Marten 9 يُعيدُ كِتابَةَ مُخَطَّطِ Marten 8 بِمُجَرَّدِ الإقلاع».
//
//  **والتَركيبُ تَركيبُ التَطبيقِ لا نُسخَةٌ مِنه** (القاعِدَتانِ ١ و٨): إعدادُ Marten كُلُّه في نِداءَين —
//  `AddPlatformHost` (النَواة) و`AddCustomerMarketplaceTemplate` (‏`ConfigureMarten` القالِب، ومِنه
//  `DealsService.RegisterDocument`) — وهُما ما يُناديه `apps/V1.App/Program.cs`. فَيُناديهِما هذا المِلَفُّ
//  بِعَينِهِما ولا يَكتُبُ `Schema.For<…>` واحِداً؛ و`MartenSchemaVerbTests` يَحمَرُّ إن ظَهَرَ مَوضِعُ
//  إعدادٍ ثالِثٌ لا يَمُرُّ بِهِما، فَلا يَعرِضُ الفِعلُ مُخَطَّطاً غَيرَ الَّذي يُشَغَّل.
//
// ─── ما لا يَشمَلُه — يُقالُ ولا يُبتلَع ────────────────────────────────
//   ١. **أَنواعُ الوَثائِقِ غَيرُ المُسَجَّلَةِ في الإعداد** (مِنها `IncubatorSession` و`AuditEntry` — مَقيس:
//      لَيسَتا بَينَ الكائِناتِ المُهَيَّأَة) لا يَعرِفُها Marten قَبلَ أَوَّلِ استِعمال، فَلا يَعرِضُها هذا الفِعلُ
//      ولا يُطَبِّقُها؛ يُنشِئُها التَطبيقُ عِندَ أَوَّلِ استِعمالٍ كَما قيلَ أَعلاه.
//   ٢. **جَداوِلُ Wolverine** (`wolverine_*`) خارِجَ كائِناتِ Marten المُهَيَّأَة (مَقيس: صِفرٌ مِنها في العَدّ).
//   ٣. **ولا يَحذِفُ شَيئاً**: التَطبيقُ بِـ`AutoCreate.CreateOrUpdate` صَراحَةً لا `All`، وفَرقٌ يَقتَضي
//      حَذفاً وإعادَةً (‏`Invalid`) يُوقِفُ الفِعلَ قَبلَ أَيِّ كِتابَة.
//   ٤. **ولا يُرَحِّلُ بَيانات**: يُغَيِّرُ الجَداوِلَ والدَوالَّ لا مُحتَوى عَمودِ `data`. (وَثائِقُ `Deal`
//      الحامِلَةُ لِـ`CommissionSar` لا تَحتاجُ رُقعَةً — مَقيسٌ في `DealStoredCommissionFieldTests`.)
//
// ─── الاستِعمال — والإجراءُ كامِلاً بِتَرتيبِه في `docs/DEPLOY.md` §٤‑د ─────
//   export ConnectionStrings__Postgres="…"                        # ولا تُطبَع أَبَداً
//   dotnet run scripts/marten-schema.cs -- --snapshot <مِلَفّ>     # لَقطَةٌ (pg_dump) قَبلَ أَيِّ فِعل — خارِجَ المُستَودَع
//   dotnet run scripts/marten-schema.cs                           # عَرضُ الفَرقِ فَقَط
//   dotnet run scripts/marten-schema.cs -- --apply --confirm <N>  # تَطبيقُ ما عَرَضَه العَرض
//   dotnet run scripts/marten-schema.cs -- --assert               # القاعِدَةُ تُطابِقُ الإعداد؟
//
//   --confirm <N>   عَدَدُ الفُروقِ كَما طَبَعَه العَرض. اختِلافُه ⇐ تَوَقُّفٌ بِلا كِتابَة: القاعِدَةُ لَيسَت
//                   الَّتي عَرَضتَها، أَو تَغَيَّرَت بَعدَه (نَسَقُ `repair-role-floor.cs` حَرفاً).
//   --snapshot <f>  ‏`pg_dump --format=custom` لِلقاعِدَةِ كُلِّها بِحُقولِ السِلسِلَةِ نَفسِها (كَلِمَةُ المُرورِ في
//                   البيئَةِ لا في سَطرِ الأَمر)، ولا يَكتُبُ فَوقَ مِلَفٍّ قائِم، ويَعُدُّ جَداوِلَ اللَقطَةِ ويَفشَلُ عَلى
//                   الصِفر. ويَحتاجُ `pg_dump` بِنُسخَةٍ لا تَقِلُّ عَن نُسخَةِ الخادِم.
//                   (‏2026-10-12، مُراجَعَةُ تَيّارِ S10 — الجَولَة ١: **ويَرفُضُ مَساراً داخِلَ شَجَرَةِ عَمَلِ git** قَبلَ أَيِّ أَداة —
//                   اللَقطَةُ نُسخَةُ الإنتاجِ بِبَياناتِ مُستَخدِميه، و`git add .` واحِدٌ يَرفَعُها إلى تاريخِ GitHub. و`*.dump`
//                   في `.gitignore` حاجِزٌ ثانٍ.)
//
// ─── رُموزُ الخُروج ────────────────────────────────────────────────────
//   0 — القاعِدَةُ تُطابِقُ الإعداد، أَو طُبِّقَ الفَرقُ وأُعيدَ قِياسُه صِفراً، أَو كُتِبَت اللَقطَةُ وعُدَّت.
//   1 — خَطَأ: اتِّصالٌ أَو استِعلام، أَو `--confirm` لا يُطابِق، أَو فَرقٌ يَقتَضي حَذفاً، أَو بَقِيَ فَرقٌ
//       بَعدَ التَطبيق، أَو صِفرُ كائِنٍ مُهَيَّأٍ أَو صِفرُ جَدوَلٍ في اللَقطَة (أَداةٌ عَمياء)، أَو مَسارُ لَقطَةٍ قائِمٌ أَو
//       داخِلَ شَجَرَةِ عَمَلِ git (‏2026-10-12).
//   2 — فَرقٌ قائِم (عَرضٌ أَو تَوكيد) — ولَم يُكتَب شَيء.
//   3 — تَهيئَةٌ ناقِصَةٌ عِندَ المُشَغِّل: `ConnectionStrings__Postgres` غائِبٌ أَو لا يُقرَأ، أَو `pg_dump` غائِبَة.
//
//  **ولا يُطبَعُ حَرفٌ مِن سِلسِلَةِ الاتِّصال**: المَطبوعُ لاحِقَةُ المُضيفِ وبَصمَةُ sha256 لَه مِن ثَمانِيَةِ
//  أَحرُف — بِعَينِ ما تَطبَعُه `verify-production-boot.sh`، فَيُقابَلُ سَطرا الأَداتَينِ عَلى الفَرعِ نَفسِه.
// ═══════════════════════════════════════════════════════════════════════

using System.Security.Cryptography;
using System.Text;
using ACommerce.Platform.Hosting;
using ACommerce.Templates.Customer.Marketplace;
using JasperFx;
using Marten;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Weasel.Core;
using Weasel.Core.Migrations;

var apply      = args.Contains("--apply");
var assert     = args.Contains("--assert");
var confirmIdx = Array.IndexOf(args, "--confirm");
int? confirm   = confirmIdx >= 0 && confirmIdx + 1 < args.Length
                   && int.TryParse(args[confirmIdx + 1], out var c) ? c : null;
var snapIdx    = Array.IndexOf(args, "--snapshot");
var snapshot   = snapIdx >= 0 && snapIdx + 1 < args.Length && !args[snapIdx + 1].StartsWith("--")
                   ? args[snapIdx + 1] : null;

if (snapIdx >= 0 && snapshot is null)
{
    Console.Error.WriteLine("خَطَأ: --snapshot يَحتاجُ مَسارَ مِلَفٍّ تُكتَبُ فيه اللَقطَة.");
    return 1;
}
if (new[] { apply, assert, snapshot is not null }.Count(v => v) > 1)
{
    Console.Error.WriteLine("خَطَأ: --snapshot و--apply و--assert أَفعالٌ مُنفَصِلَة — كُلٌّ في نِداءٍ بِتَرتيبِ §٤‑د.");
    return 1;
}

var cs = Environment.GetEnvironmentVariable("ConnectionStrings__Postgres");
if (string.IsNullOrWhiteSpace(cs))
{
    Console.Error.WriteLine("✗ تَهيئَةٌ ناقِصَة: ‏ConnectionStrings__Postgres غَير مَضبوط — لا قاعِدَةَ مُسَمّاة.");
    return 3;
}

string host, user;
try { var parsed = new NpgsqlConnectionStringBuilder(cs); host = parsed.Host ?? ""; user = parsed.Username ?? ""; }
catch (ArgumentException)
{
    Console.Error.WriteLine("✗ تَهيئَةٌ ناقِصَة: ‏ConnectionStrings__Postgres لا يُقرَأُ سِلسِلَةَ Npgsql (لا يُطبَعُ مُحتَواها).");
    return 3;
}
var labels = host.Split('.');
var suffix = labels.Length > 1 ? $"{labels[^2]}.{labels[^1]}" : host;
var hostFp = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(host))).ToLowerInvariant()[..8];

// ‏اللَقطَةُ لا تَحتاجُ تَركيبَ التَطبيق: تَقرَأُ القاعِدَةَ كُلَّها كَما هِيَ قَبلَ أَيِّ فِعل.
if (snapshot is not null)
    return await SnapshotAsync(cs, [host, user], suffix, hostFp, snapshot);

// ─── التَركيب — تَركيبُ التَطبيقِ بِعَينِه ─────────────────────────────
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    // ‏Production صَراحَةً: في Development يَضبِطُ `AddPlatformHost` ‏`AutoCreate.All`. والتَطبيقُ يُمَرِّرُ
    // `CreateOrUpdate` عَلى كُلِّ حال، لكِنّ العَرضَ والتَوكيدَ يَقيسانِ ما يُشَغِّلُه الإنتاجُ لا التَطوير.
    EnvironmentName = Environments.Production,
    // ‏ولا `appsettings.json` يُقرَأ: سِلسِلَةُ `apps/V1.App` المَحَلِّيَّةُ لا تَحِلُّ مَحَلَّ مُتَغَيِّرٍ غائِب.
    ContentRootPath = AppContext.BaseDirectory
});
builder.AddPlatformHost();
builder.Services.AddCustomerMarketplaceTemplate();
await using var app = builder.Build();
var store = app.Services.GetRequiredService<IDocumentStore>();

var configured = store.Storage.AllObjects().Count();
Console.WriteLine($"القاعِدَة:  ***.{suffix}  (بَصمَةُ المُضيف sha256:{hostFp})");
Console.WriteLine($"الوَضع:    {(apply ? "تَطبيق (--apply)" : assert ? "تَوكيد (--assert)" : "عَرضُ الفَرقِ — بِلا كِتابَة")}");
Console.WriteLine($"كائِناتُ المُخَطَّطِ المُهَيَّأَة: {configured}");

// ‏حارِسُ العَمى (القاعِدَة ١٠): أَداةٌ قارَنَت صِفراً لا تُميَّزُ عَن أَداةٍ قارَنَت كُلَّ شَيء.
if (configured == 0)
{
    Console.Error.WriteLine("✗ BLIND CHECK: صِفرُ كائِنٍ مُهَيَّأ — التَركيبُ لَم يَبلُغ إعدادَ Marten.");
    return 1;
}

try
{
    var pending = await PendingAsync(store);
    Console.WriteLine($"الفُروق:   {pending.Count}");
    foreach (var d in pending)
        Console.WriteLine($"  {Kind(d.Difference),-12} {d.SchemaObject.Identifier}");
    Console.WriteLine(new string('-', 72));

    if (assert)
    {
        try
        {
            await store.Storage.Database.AssertDatabaseMatchesConfigurationAsync();
        }
        catch (DatabaseValidationException)
        {
            Console.WriteLine($"✗ انجِراف — القاعِدَةُ لا تُطابِقُ الإعدادَ في {pending.Count} مَوضِعاً. لَم يُكتَب شَيء.");
            return 2;
        }
        Console.WriteLine($"✓ مُطابِقَة — {configured} كائِناً مُهَيَّأً، وAssertDatabaseMatchesConfiguration لا يَرمي.");
        return 0;
    }

    if (pending.Count == 0)
    {
        Console.WriteLine("✓ لا فَرقَ بَينَ القاعِدَةِ والإعداد — لا كِتابَة.");
        return 0;
    }

    if (!apply)
    {
        Console.WriteLine("عَرضٌ فَقَط — لَم يُكتَب شَيء. لِلتَطبيق:");
        Console.WriteLine($"  dotnet run scripts/marten-schema.cs -- --apply --confirm {pending.Count}");
        return 2;
    }

    if (confirm != pending.Count)
    {
        Console.Error.WriteLine($"تَوَقُّف: --confirm {confirm?.ToString() ?? "(غائِب)"} لا يُطابِقُ {pending.Count} فَرقاً.");
        Console.Error.WriteLine("أَعِد العَرضَ ثُمَّ طَبِّق بِالعَدَدِ الَّذي طَبَعَه. لا كِتابَةَ عَلى العَمياء.");
        return 1;
    }

    var invalid = pending.Count(d => d.Difference == SchemaPatchDifference.Invalid);
    if (invalid > 0)
    {
        Console.Error.WriteLine($"تَوَقُّف: {invalid} فَرقاً يَقتَضي حَذفاً وإعادَة — لا يُطَبِّقُه هذا الفِعل (CreateOrUpdate لا يَحذِف).");
        return 1;
    }

    var result = await store.Storage.Database.ApplyAllConfiguredChangesToDatabaseAsync(AutoCreate.CreateOrUpdate);

    var left = await PendingAsync(store);
    if (left.Count > 0)
    {
        Console.Error.WriteLine($"✗ بَقِيَ {left.Count} فَرقاً بَعدَ التَطبيق:");
        foreach (var d in left)
            Console.Error.WriteLine($"  {Kind(d.Difference),-12} {d.SchemaObject.Identifier}");
        return 1;
    }

    Console.WriteLine($"✓ طُبِّقَ {pending.Count} فَرقاً (Marten: {result}) — وأُعيدَ القِياسُ: صِفرُ فَرق.");
    Console.WriteLine("  ثُمَّ: dotnet run scripts/marten-schema.cs -- --assert");
    return 0;
}
catch (Exception ex)
{
    // ‏رِسالَةُ Npgsql قَد تَحمِلُ المُضيفَ أَو اسمَ المُستَخدِم (‏28P01 «…for user "…"») — يُحجَبانِ كَما يُحجَبُ
    // المُضيفُ في سَطرِ القاعِدَة. وكَلِمَةُ المُرورِ لا تَرِدُ فيها.
    var state = ex is PostgresException pg ? $" (SqlState {pg.SqlState})" : "";
    Console.Error.WriteLine($"✗ خَطَأ: {ex.GetType().Name}{state}: {Redact(ex.Message.Split('\n')[0], [host, user])}");
    return 1;
}

// ─── اللَقطَة — `pg_dump` بِحُقولِ السِلسِلَةِ نَفسِها، وكَلِمَةُ المُرورِ في البيئَةِ لا في سَطرِ الأَمر ───
// ‏`ConnectionStrings__Postgres` بِصيغَةِ Npgsql (‏`Host=…;Database=…`) لا يَقبَلُها `pg_dump`، ونَقلُها بِاليَدِ
// إلى صيغَةٍ ثانِيَةٍ سِرٌّ واحِدٌ في مَوضِعَين. فَتُفَكُّ هُنا بِـ`NpgsqlConnectionStringBuilder` إلى مُتَغَيِّراتِ libpq
// المَعروفَة (‏PGHOST…PGPASSWORD) في بيئَةِ العَمَلِيَّةِ الابنَةِ وَحدَها — فَلا تَظهَرُ في قائِمَةِ العَمَلِيّات.
// **والقِياسُ بَعدَها**: عَدَدُ جَداوِلِ اللَقطَةِ مِن `pg_restore --list`، وصِفرٌ فَشَل (القاعِدَة ١٠).
static async Task<int> SnapshotAsync(string cs, string[] hide, string suffix, string hostFp, string file)
{
    Console.WriteLine($"القاعِدَة:  ***.{suffix}  (بَصمَةُ المُضيف sha256:{hostFp})");
    Console.WriteLine("الوَضع:    لَقطَة (--snapshot) — قِراءَةٌ لا كِتابَةَ في القاعِدَة");

    // ‏(‏2026-10-12، مُراجَعَةُ تَيّارِ S10 — الجَولَة ١) **لا تُكتَبُ داخِلَ شَجَرَةِ عَمَلِ git**: اللَقطَةُ نُسخَةٌ كامِلَةٌ مِن
    // الإنتاجِ — هَواتِفُ المُستَخدِمينَ وبَريدُهُم وعَناوينُهُم وصَفَقاتُهُم — و§٤‑د كانَ يَكتُبُها في المُجَلَّدِ الحالِيّ (جَذرِ
    // المُستَودَعِ ضِمناً) و`*.dump` غَيرُ مُتَجاهَل: `git add .` واحِدٌ أَو «stage all» في واجِهَةٍ يَرفَعُها إلى تاريخِ GitHub
    // حَيثُ مَحوُها مُكلِف. فَيُرفَضُ قَبلَ أَيِّ أَداةٍ وأَيِّ كِتابَة، ويُقالُ أَينَ تُكتَب.
    var full = Path.GetFullPath(file);
    if (EnclosingWorkTree(full) is { } root)
    {
        Console.Error.WriteLine($"تَوَقُّف: {full} داخِلَ شَجَرَةِ عَمَلِ git ({root}) — اللَقطَةُ بَياناتُ إنتاجٍ شَخصِيَّة، ولا تُكتَبُ حَيثُ يَرفَعُها git add.");
        Console.Error.WriteLine("  اكتُبها خارِجَها: mkdir -p \"$HOME/wasayel-snapshots\" ثُمَّ --snapshot \"$HOME/wasayel-snapshots/<الاسم>.dump\" (DEPLOY.md §٤‑د).");
        return 1;
    }

    if (File.Exists(file))
    {
        Console.Error.WriteLine($"تَوَقُّف: {file} قائِم — اللَقطَةُ لا تُكتَبُ فَوقَ أُخرى.");
        return 1;
    }

    var tool = await RunAsync("pg_dump", ["--version"], null);
    if (tool is null)
    {
        Console.Error.WriteLine("✗ تَهيئَةٌ ناقِصَة: pg_dump غائِبَةٌ عَن المُشَغِّل (عَميلُ Postgres بِنُسخَةٍ لا تَقِلُّ عَن نُسخَةِ الخادِم).");
        return 3;
    }
    Console.WriteLine($"الأَداة:   {tool.Value.Out.Trim()}");

    var b = new NpgsqlConnectionStringBuilder(cs);
    var env = new Dictionary<string, string?>
    {
        ["PGHOST"]           = b.Host,
        ["PGPORT"]           = b.Port.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ["PGDATABASE"]       = b.Database,
        ["PGUSER"]           = b.Username,
        ["PGPASSWORD"]       = b.Password,
        ["PGSSLMODE"]        = b.SslMode switch
        {
            SslMode.VerifyCA   => "verify-ca",
            SslMode.VerifyFull => "verify-full",
            var m              => m.ToString().ToLowerInvariant()
        },
        ["PGCHANNELBINDING"] = b.ChannelBinding.ToString().ToLowerInvariant()
    };

    var dump = await RunAsync("pg_dump", ["--format=custom", "--no-owner", "--no-privileges", $"--file={file}"], env);
    if (dump is null || dump.Value.Code != 0)
    {
        Console.Error.WriteLine($"✗ فَشِلَ pg_dump (رَمزُه {dump?.Code.ToString() ?? "—"}): {Redact((dump?.Err ?? "").Split('\n')[0], hide)}");
        if (File.Exists(file)) File.Delete(file);   // ‏لَقطَةٌ ناقِصَةٌ أَسوَأُ مِن لا لَقطَة: تَبدو مَأخوذَة.
        return 1;
    }

    var list = await RunAsync("pg_restore", ["--list", file], null);
    if (list is null || list.Value.Code != 0)
    {
        Console.Error.WriteLine("✗ اللَقطَةُ كُتِبَت ولا تُقرَأ بِـpg_restore --list — لا عَدَّ، فَلا ثِقَة.");
        File.Delete(file);
        return 1;
    }
    var lines  = list.Value.Out.Split('\n');
    var tables = lines.Count(l => System.Text.RegularExpressions.Regex.IsMatch(l, @"\bTABLE (?!DATA\b)"));
    var data   = lines.Count(l => l.Contains(" TABLE DATA ", StringComparison.Ordinal));
    var bytes  = new FileInfo(file).Length;

    if (tables == 0)
    {
        Console.Error.WriteLine($"✗ لَقطَةٌ بِلا جَدوَل ({bytes} بايتاً) — القاعِدَةُ فارِغَةٌ أَو لَيسَت المَقصودَة. حُذِفَ المِلَفّ.");
        File.Delete(file);
        return 1;
    }
    Console.WriteLine($"✓ اللَقطَة: {file} · {bytes} بايتاً · {tables} جَدوَلاً ({data} بِبَياناتِه)");
    Console.WriteLine($"  وتُستَرجَعُ إلى قاعِدَةٍ جَديدَة: pg_restore --no-owner --dbname=<القاعِدَةُ الجَديدَة> {file}");
    return 0;
}

static async Task<(int Code, string Out, string Err)?> RunAsync(
    string exe, string[] arguments, IReadOnlyDictionary<string, string?>? env)
{
    var psi = new System.Diagnostics.ProcessStartInfo(exe)
    {
        RedirectStandardOutput = true,
        RedirectStandardError  = true,
        UseShellExecute        = false
    };
    foreach (var a in arguments) psi.ArgumentList.Add(a);
    if (env is not null)
        foreach (var (k, v) in env)
            if (!string.IsNullOrEmpty(v)) psi.Environment[k] = v;

    try
    {
        using var p = System.Diagnostics.Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        await p.WaitForExitAsync();
        return (p.ExitCode, await stdout, await stderr);
    }
    catch (System.ComponentModel.Win32Exception)
    {
        return null;   // ‏الأَداةُ غائِبَة — تَهيئَةٌ عِندَ المُشَغِّل لا خَطَأٌ في القاعِدَة.
    }
}

/// <summary>أَقرَبُ مُجَلَّدٍ فَوقَ المَسارِ يَحمِلُ <c>.git</c> — مُجَلَّداً، أَو مِلَفّاً في شَجَرَةٍ مُلحَقَة (worktree) — وإلّا <c>null</c>.
/// اكتِشافُ git نَفسُه بِلا git: الصُعودُ مِن مُجَلَّدِ المِلَفِّ إلى الجَذر. وهُوَ أَوسَعُ مِنه لا أَضيَق (لا سَقفَ
/// <c>GIT_CEILING_DIRECTORIES</c> ولا حُدودَ نِظامِ مِلَفّات)، فَالخَطَأُ في جِهَةِ الرَفضِ لا الكَشف.</summary>
static string? EnclosingWorkTree(string fullPath)
{
    for (var dir = Path.GetDirectoryName(fullPath); !string.IsNullOrEmpty(dir); dir = Path.GetDirectoryName(dir))
    {
        var dotGit = Path.Combine(dir, ".git");
        if (Directory.Exists(dotGit) || File.Exists(dotGit)) return dir;
    }
    return null;
}

static string Redact(string line, string[] hide) =>
    hide.Where(h => h.Length > 0).Aggregate(line, (l, h) => l.Replace(h, "***", StringComparison.Ordinal));

static async Task<List<ISchemaObjectDelta>> PendingAsync(IDocumentStore store)
{
    var migration = await store.Storage.Database.CreateMigrationAsync();
    return migration.Deltas.Where(d => d.Difference != SchemaPatchDifference.None).ToList();
}

static string Kind(SchemaPatchDifference d) => d switch
{
    SchemaPatchDifference.Create  => "إنشاء",
    SchemaPatchDifference.Update  => "تَعديل",
    SchemaPatchDifference.Invalid => "حَذفٌ وإعادَة",
    _                             => d.ToString()
};
