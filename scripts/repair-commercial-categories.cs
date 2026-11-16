#!/usr/bin/env dotnet
#:property JsonSerializerIsReflectionEnabledByDefault=true
#:property PublishAot=false
#:project ../libs/templates/ACommerce.Templates.Customer.Marketplace/ACommerce.Templates.Customer.Marketplace.csproj
// ═══════════════════════════════════════════════════════════════════════
//  تَرميمُ مَتاجِرِ المُنتَجاتِ الَّتي وُلِدَت «إيجاراً» بِلا سَلَّة
// ───────────────────────────────────────────────────────────────────────
//  **لِماذا هذا المِلَفّ** (‏2026-10-29، قَرارُ المالِك د24 «ب»؛ ‏ADR-034 مُلحَق د24): قَبلَ `c4278c0` كانَ مَصنَعُ
//  الاستوديو يَكتُبُ فِئَتَي `products` و`deals` بِتَجميعِ `commercial` فَيَقرَؤُهُما `PatternOf` إيجاراً بِلا سَلَّة. الإصلاحُ
//  مَنَعَ الجَديدَ ولَم يُصلِحِ القائِم — والعَطَبُ مِن المَنَصَّةِ نَفسِها، فَإصلاحُه عَلى وَسايِل لا عَلى صاحِبِ المَتجَر.
//
//  **والقَرارُ كُلُّه في `CommercialCategoryRepair` لا هُنا**: مَتجَرٌ بُنِيَ مِن الاستوديو (`SourceAnalysisId` مَضبوط)، وفِئَتا
//  `products`/`deals` وَحدَهُما حينَ تَجميعُهُما `commercial` بِالضَبط — تَصيرانِ بِلا تَجميعٍ كَما يَكتُبُهُما المَصنَعُ اليَوم.
//  ولا استِثناءَ بِاسمِ مَتجَر: `ashare` كَأَيِّ مَتجَر (قَرارُ المالِك في `ashare`)، ولا يَبلُغُه الشَرطُ أَصلاً (بِلا `SourceAnalysisId`).
//  وبَعدَ التَرميمِ يُغَيِّرُ صاحِبُ كُلِّ مَتجَرٍ التَجميعَ مِن مُحَرِّرِ الفِئاتِ كَما يَشاء.
//
//  **عَلى نَسَقِ `repair-role-floor.cs`**: عَرضٌ بِلا كِتابَةٍ افتِراضاً، ثُمَّ `--apply --confirm N`، ونُسخَةٌ احتِياطِيَّةٌ لِوَثائِقِ المَتاجِرِ
//  الَّتي تُمَسُّ (‏JSON + أَمرُ استِرجاعِ SQL) قَبلَ أَوَّلِ كِتابَة، وسَطرُ تَدقيقٍ لِكُلِّ مَتجَر، ولا مَساسَ بِالمُخَطَّط (`AutoCreate.None`).
//  **ويُجَرَّبُ أَوَّلاً عَلى فَرعِ Neon مَنسوخ** — لا يُشَغَّلُ التَطويرُ عَلى قاعِدَةِ الإنتاج (CLAUDE.md).
//
// ─── الاستِعمال ────────────────────────────────────────────────────────
//   export ConnectionStrings__Postgres="…"        # ولا تُطبَع أَبَداً
//   dotnet run scripts/repair-commercial-categories.cs                    # عَرضٌ فَقَط
//   dotnet run scripts/repair-commercial-categories.cs -- --apply --confirm 2
//
//   --apply             يُنَفِّذ. بِدونِه **عَرضُ ما سَيَتَغَيَّرُ فَقَط**.
//   --confirm <N>       عَدَدُ المَتاجِرِ كَما طَبَعَه العَرض. اختِلافُه ⇐ تَوَقُّفٌ بِلا كِتابَة.
//   --backup-dir <path>  مَجَلَّدُ النُسخَةِ الاحتِياطِيَّة (‏`backups/`).
//
// ─── رُموزُ الخُروج ────────────────────────────────────────────────────
//   0 — لا شَيءَ لِيُرَمَّم، أَو نُفِّذَ التَرميمُ بِنَجاح.
//   2 — عَرضٌ وَجَدَ مُرَشَّحين (لَم يُكتَب شَيء).
//   1 — خَطَأ، أَو صِفرُ مَتجَرٍ مَفحوص، أَو --confirm لا يُطابِق.
// ═══════════════════════════════════════════════════════════════════════

using System.Security.Cryptography;
using System.Text;
using ACommerce.Kit.Tenants;
using ACommerce.Templates.Customer.Marketplace.Services.Audit;
using ACommerce.Templates.Customer.Marketplace.Services.TenantConfig;
using JasperFx;
using Marten;
using Npgsql;

var apply      = args.Contains("--apply");
var confirmIdx = Array.IndexOf(args, "--confirm");
int? confirm   = confirmIdx >= 0 && confirmIdx + 1 < args.Length && int.TryParse(args[confirmIdx + 1], out var c) ? c : null;
var backupIdx  = Array.IndexOf(args, "--backup-dir");
var backupDir  = backupIdx >= 0 && backupIdx + 1 < args.Length ? args[backupIdx + 1] : "backups";

var cs = Environment.GetEnvironmentVariable("ConnectionStrings__Postgres");
if (string.IsNullOrWhiteSpace(cs))
{
    Console.Error.WriteLine("خَطَأ: ‏ConnectionStrings__Postgres غَير مَضبوط.");
    return 1;
}
var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(cs))).ToLowerInvariant()[..12];

using var store = DocumentStore.For(o =>
{
    o.Connection(cs);
    o.DatabaseSchemaName = "platform";
    o.AutoCreateSchemaObjects = AutoCreate.None;   // ← لا يُمَسُّ المُخَطَّط (‏Marten 9 يُعيدُ كِتابَةَ مُخَطَّطِ Marten 8 — CLAUDE.md)
    o.Policies.AllDocumentsAreMultiTenanted();
    o.Schema.For<Tenant>().SingleTenanted().Identity(x => x.Id);
});
await using var conn = new NpgsqlConnection(cs);
await conn.OpenAsync();

List<Tenant> tenants;
await using (var qs = store.QuerySession())
    tenants = (await qs.Query<Tenant>().ToListAsync()).OrderBy(t => t.Id, StringComparer.Ordinal).ToList();

Console.WriteLine($"القاعِدَة: بَصمَة {fingerprint} · المَتاجِر: {tenants.Count}");
Console.WriteLine($"الوَضع:   {(apply ? "تَنفيذ (--apply)" : "عَرضُ ما سَيَتَغَيَّر — بِلا كِتابَة")}");
Console.WriteLine(new string('-', 78));
if (tenants.Count == 0)
{
    Console.Error.WriteLine("✗ BLIND CHECK: صِفرُ مَتجَرٍ مَفحوص — لا يُقال «لا شَيءَ لِيُرَمَّم» عَن قاعِدَةٍ لَم تُقرَأ.");
    return 1;
}

var plan = tenants
    .Select(t => (T: t, Targets: CommercialCategoryRepair.Targets(t)))
    .Where(p => p.Targets.Count > 0)
    .ToList();
var studioBuilt = tenants.Count(t => t.SourceAnalysisId is not null);
Console.WriteLine($"مَفحوصٌ: {tenants.Count} مَتجَراً · مَبنِيٌّ مِن الاستوديو: {studioBuilt} · مُرَشَّحٌ لِلتَرميم: {plan.Count}");

if (plan.Count == 0)
{
    Console.WriteLine("لا مَتجَرَ يَحتاجُ تَرميماً.");
    return 0;
}

foreach (var p in plan)
    Console.WriteLine($"  # {p.T.Id,-24} «{p.T.Name}» — {string.Join("، ", p.Targets)}: commercial ← (بِلا تَجميع)");
Console.WriteLine();

if (!apply)
{
    Console.WriteLine("عَرضٌ فَقَط — لَم يُكتَب شَيء. لِلتَنفيذ بَعدَ قِراءَةِ القائِمَة:");
    Console.WriteLine($"  dotnet run scripts/repair-commercial-categories.cs -- --apply --confirm {plan.Count}");
    return 2;
}

if (confirm is null || confirm != plan.Count)
{
    Console.Error.WriteLine($"تَوَقُّف: --confirm {confirm?.ToString() ?? "(غائِب)"} لا يُطابِقُ {plan.Count} مُرَشَّحاً.");
    Console.Error.WriteLine("أَعِد العَرضَ ثُمَّ نَفِّذ بِالعَدَدِ الَّذي طَبَعَه. لا كِتابَةَ عَلى العَمياء.");
    return 1;
}

// ─── النُسخَةُ الاحتِياطِيَّة — قَبلَ أَوَّلِ كِتابَة ────────────────────
Directory.CreateDirectory(backupDir);
var stamp    = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
var jsonPath = Path.Combine(backupDir, $"commercial-categories-{stamp}.json");
var sqlPath  = Path.Combine(backupDir, $"commercial-categories-{stamp}.restore.sql");
var rows = new List<(string Id, string Data)>();
foreach (var p in plan)
{
    await using var cmd = new NpgsqlCommand("select data::text from platform.mt_doc_tenant where id = @s", conn);
    cmd.Parameters.AddWithValue("s", p.T.Id);
    var data = (string?)await cmd.ExecuteScalarAsync();
    if (data is null) { Console.Error.WriteLine($"خَطَأ: {p.T.Id} اختَفى بَينَ العَرضِ والتَنفيذ."); return 1; }
    rows.Add((p.T.Id, data));
}
await File.WriteAllTextAsync(jsonPath,
    "[\n" + string.Join(",\n", rows.Select(r => $"  {{\"id\": {System.Text.Json.JsonSerializer.Serialize(r.Id)}, \"data\": {r.Data}}}")) + "\n]\n");
var sb = new StringBuilder();
sb.AppendLine("-- اِستِرجاعُ وَثائِقِ المَتاجِرِ كَما كانَت قَبلَ تَرميمِ التَجميع.");
sb.AppendLine($"-- بَصمَةُ القاعِدَة: {fingerprint} · التارِيخ: {stamp}Z");
sb.AppendLine("BEGIN;");
foreach (var r in rows)
    sb.AppendLine($"UPDATE platform.mt_doc_tenant SET data = {Quote(r.Data)}::jsonb WHERE id = {Quote(r.Id)};");
sb.AppendLine("COMMIT;");
await File.WriteAllTextAsync(sqlPath, sb.ToString());
Console.WriteLine($"نُسخَةٌ احتِياطِيَّة: {jsonPath}");
Console.WriteLine($"وأَمرُ الاسترجاع  : {sqlPath}");
Console.WriteLine();

// ─── التَنفيذ ───────────────────────────────────────────────────────────
var audit = new AuditWriter(store);
var done = 0;
foreach (var p in plan)
{
    await using var s = store.LightweightSession();
    var live = await s.LoadAsync<Tenant>(p.T.Id);
    if (live is null) { Console.Error.WriteLine($"تَخَطٍّ: {p.T.Id} غَير مَوجود."); continue; }
    var changed = CommercialCategoryRepair.Apply(live);   // ← تَكافُؤٌ: إعادَةُ التَشغيلِ لا تَجِدُ ما تُغَيِّرُه
    if (changed == 0) { Console.WriteLine($"تَخَطٍّ: {p.T.Id} تَغَيَّرَ بَينَ العَرضِ والتَنفيذ."); continue; }
    s.Store(live);
    await s.SaveChangesAsync();

    await audit.WriteAsync(p.T.Id, null, "repair-commercial-categories",
        CommercialCategoryRepair.AuditAction, "Tenant", p.T.Id,
        note: string.Join(",", p.Targets),
        before: "commercial", after: "");
    Console.WriteLine($"OK {p.T.Id} <- {string.Join("، ", p.Targets)} بِلا تَجميع");
    done++;
}

Console.WriteLine();
Console.WriteLine($"تَمَّ تَرميمُ {done} مِن {plan.Count}.");
return 0;

static string Quote(string s) => "'" + s.Replace("'", "''") + "'";
