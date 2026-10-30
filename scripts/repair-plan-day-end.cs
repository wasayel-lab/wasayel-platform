#!/usr/bin/env dotnet
#:property JsonSerializerIsReflectionEnabledByDefault=true
#:property PublishAot=false
#:project ../libs/templates/ACommerce.Templates.Customer.Marketplace/ACommerce.Templates.Customer.Marketplace.csproj
// ═══════════════════════════════════════════════════════════════════════
//  باقاتُ المَتاجِرِ المَحفوظَةُ قَبلَ د51 — يَومُ انتِهائِها يَنتَهي آخِرَه بِتَوقيتِ المَنَصَّة
// ───────────────────────────────────────────────────────────────────────
//  **لِماذا هذا المِلَفّ** (‏2026-10-30، تَدقيقُ الموجَة ١١ F-SDC-1؛ ‏ADR-039 مُلحَق د51): `e3c8b2d` جَعَلَ «تَنتَهي يَومَ X» آخِرَ
//  يَومِ X بِتَوقيتِ الرِياض **لَحظَةَ الحِفظ**، فَالباقاتُ المَحفوظَةُ قَبلَه ما زالَت تَنتَهي مُنتَصَفَ لَيلِه UTC (الثالِثَةَ فَجراً
//  بِتَوقيتِ الرِياض). وكانَ فِعلُ المالِكِ «أَعِد حِفظَ باقَةِ كُلٍّ مِن /admin/tenants/{slug}/plan» — والحِفظُ يُعيدُ الحالَةَ سارِيَةً
//  (فَيَظهَرُ مَتجَرٌ أَوقَفَه المُشرِف) وكانَ يُسقِطُ رَبطَ PayPal وعَلامَةَ إلغاءِ التَجديد (`24d5fb1`). فَهذا السكربتُ بَديلُ إعادَةِ الحِفظ.
//
//  **والقَرارُ كُلُّه في `TenantPlanDayEndRepair` لا هُنا**: باقَةٌ انتِهاؤُها مُنتَصَفُ لَيلٍ UTC بِالضَبط يُكتَبُ انتِهاؤُها — وبَدؤُها إن
//  كانَ مُنتَصَفَ لَيلٍ أَيضاً — آخِرَ يَومِه بِتَوقيتِ المَنَصَّة (`CultureTime.EndOfDayUtc`، الدالَّةُ الَّتي يَقرَأُ بِها الحِفظُ اليَوم).
//  **ولا يُمَسُّ غَيرُ التاريخَين**: الحالَةُ (المَوقوفَةُ تَبقى مَوقوفَة)، ورَبطُ PayPal ورابِطُ الدَفع، وعَلامَةُ إلغاءِ التَجديد، ومَن ضَبَطَ ومَتى.
//  ولا استِثناءَ بِاسمِ مَتجَر (`ashare` كَأَيِّ مَتجَر — قَرارُ المالِك).
//
//  **عَلى نَسَقِ `repair-studio-tier.cs` و`repair-commercial-categories.cs`**: عَرضٌ بِلا كِتابَةٍ افتِراضاً، ثُمَّ `--apply --confirm N`،
//  وأَمرُ استِرجاعٍ بِالمُعَرِّفاتِ والتاريخَينِ المُخَزَّنَينِ بِحَرفِهِما قَبلَ أَوَّلِ كِتابَة، وسَطرُ تَدقيقٍ لِكُلِّ باقَة، ولا مَساسَ بِالمُخَطَّط
//  (`AutoCreate.None`). **والنُسخَةُ الكامِلَةُ لَقطَةُ القاعِدَةِ الَّتي تَسبِقُه** (`marten-schema.cs --snapshot`، ‏DEPLOY.md §٤‑د).
//  **ويُجَرَّبُ أَوَّلاً عَلى فَرعِ Neon مَنسوخ** — لا يُشَغَّلُ التَطويرُ عَلى قاعِدَةِ الإنتاج (CLAUDE.md).
//
// ─── الاستِعمال ────────────────────────────────────────────────────────
//   export ConnectionStrings__Postgres="…"        # ولا تُطبَع أَبَداً
//   dotnet run scripts/repair-plan-day-end.cs                    # عَرضٌ فَقَط
//   dotnet run scripts/repair-plan-day-end.cs -- --apply --confirm 2
//
//   --apply             يُنَفِّذ. بِدونِه **عَرضُ ما سَيَتَغَيَّرُ فَقَط**.
//   --confirm <N>       عَدَدُ الباقاتِ كَما طَبَعَه العَرض. اختِلافُه ⇐ تَوَقُّفٌ بِلا كِتابَة.
//   --backup-dir <path>  مَجَلَّدُ أَمرِ الاستِرجاع (‏`backups/`).
//
// ─── رُموزُ الخُروج ────────────────────────────────────────────────────
//   0 — لا باقَةَ عَلى مُنتَصَفِ اللَيل، أَو نُفِّذَ بِنَجاح.
//   2 — عَرضٌ وَجَدَ مُرَشَّحين (لَم يُكتَب شَيء).
//   1 — خَطَأ، أَو صِفرُ مَتجَرٍ مَفحوص، أَو --confirm لا يُطابِق.
// ═══════════════════════════════════════════════════════════════════════

using System.Security.Cryptography;
using System.Text;
using ACommerce.Kit.Subscriptions;
using ACommerce.Kit.Tenants;
using ACommerce.Templates.Customer.Marketplace.Services.Audit;
using ACommerce.Templates.Customer.Marketplace.Services.Subscriptions;
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
    o.Schema.For<TenantPlan>().SingleTenanted().Identity(x => x.Id);
});
await using var conn = new NpgsqlConnection(cs);
await conn.OpenAsync();

List<Tenant> tenants;
List<TenantPlan> plans;
await using (var qs = store.QuerySession())
{
    tenants = (await qs.Query<Tenant>().ToListAsync()).ToList();
    plans = (await qs.Query<TenantPlan>().ToListAsync()).OrderBy(p => p.Id, StringComparer.Ordinal).ToList();
}

Console.WriteLine($"القاعِدَة: بَصمَة {fingerprint} · المَتاجِر: {tenants.Count} · باقاتٌ مَحفوظَة: {plans.Count}");
Console.WriteLine($"الوَضع:   {(apply ? "تَنفيذ (--apply)" : "عَرضُ ما سَيَتَغَيَّر — بِلا كِتابَة")}");
Console.WriteLine(new string('-', 78));
if (tenants.Count == 0)
{
    Console.Error.WriteLine("✗ BLIND CHECK: صِفرُ مَتجَرٍ مَفحوص — لا يُقال «لا باقَةَ لِتُرَمَّم» عَن قاعِدَةٍ لَم تُقرَأ.");
    return 1;
}

var plan = plans.Where(TenantPlanDayEndRepair.IsCandidate).ToList();
Console.WriteLine($"مَفحوصٌ: {plans.Count} باقَة · انتِهاؤُها مُنتَصَفُ لَيلٍ UTC: {plan.Count}");
if (plan.Count == 0)
{
    Console.WriteLine("لا باقَةَ تَحتاجُ تَرميماً.");
    return 0;
}

foreach (var p in plan)
{
    var starts = TenantPlanDayEndRepair.IsMidnightUtc(p.StartsAt) ? $"{p.StartsAt:O} ← {TenantPlanDayEndRepair.DayEnd(p.StartsAt):O}" : $"{p.StartsAt:O} (يَبقى)";
    Console.WriteLine($"  # {p.Id,-24} {p.Status,-8} انتِهاء {p.ExpiresAt:O} ← {TenantPlanDayEndRepair.DayEnd(p.ExpiresAt):O} · بَدء {starts}");
}
Console.WriteLine("  (الحالَةُ ورَبطُ PayPal ورابِطُ الدَفعِ وعَلامَةُ إلغاءِ التَجديدِ لا تُمَسّ)");
Console.WriteLine();

if (!apply)
{
    Console.WriteLine("عَرضٌ فَقَط — لَم يُكتَب شَيء. قَبلَ التَنفيذِ لَقطَةُ القاعِدَة (‏DEPLOY.md §٤‑د)، ثُمَّ بَعدَ مُراجَعَةِ العَدَد:");
    Console.WriteLine($"  dotnet run scripts/repair-plan-day-end.cs -- --apply --confirm {plan.Count}");
    return 2;
}

if (confirm is null || confirm != plan.Count)
{
    Console.Error.WriteLine($"تَوَقُّف: --confirm {confirm?.ToString() ?? "(غائِب)"} لا يُطابِقُ {plan.Count} باقَة.");
    Console.Error.WriteLine("أَعِد العَرضَ ثُمَّ نَفِّذ بِالعَدَدِ الَّذي طَبَعَه. لا كِتابَةَ عَلى العَمياء.");
    return 1;
}

// ─── أَمرُ الاستِرجاع — المُعَرِّفُ والتاريخانِ بِحَرفِهِما المُخَزَّن، قَبلَ أَوَّلِ كِتابَة ──
Directory.CreateDirectory(backupDir);
var stamp   = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
var sqlPath = Path.Combine(backupDir, $"plan-day-end-{stamp}.restore.sql");
var sb = new StringBuilder();
sb.AppendLine("-- اِستِرجاعُ تاريخَي الباقاتِ كَما كانا قَبلَ تَرميمِ آخِرِ اليَوم (المُعَرِّفُ والتاريخانِ وَحدَهُما).");
sb.AppendLine($"-- بَصمَةُ القاعِدَة: {fingerprint} · التارِيخ: {stamp}Z");
sb.AppendLine("BEGIN;");
foreach (var p in plan)
{
    await using var cmd = new NpgsqlCommand(
        "select data->>'StartsAt', data->>'ExpiresAt' from platform.mt_doc_tenantplan where id = @s", conn);
    cmd.Parameters.AddWithValue("s", p.Id);
    await using var r = await cmd.ExecuteReaderAsync();
    if (!await r.ReadAsync()) { Console.Error.WriteLine($"خَطَأ: {p.Id} اختَفَت بَينَ العَرضِ والتَنفيذ."); return 1; }
    var (startsRaw, expiresRaw) = (r.GetString(0), r.GetString(1));
    sb.AppendLine("UPDATE platform.mt_doc_tenantplan SET data = jsonb_set(jsonb_set(data, '{StartsAt}', to_jsonb("
                + $"{Quote(startsRaw)}::text)), '{{ExpiresAt}}', to_jsonb({Quote(expiresRaw)}::text)) WHERE id = {Quote(p.Id)};");
}
sb.AppendLine("COMMIT;");
await File.WriteAllTextAsync(sqlPath, sb.ToString());
Console.WriteLine($"أَمرُ الاسترجاع: {sqlPath}");
Console.WriteLine();

// ─── التَنفيذ ───────────────────────────────────────────────────────────
var audit = new AuditWriter(store);
var done = 0;
foreach (var p in plan)
{
    await using var s = store.LightweightSession();
    var live = await s.LoadAsync<TenantPlan>(p.Id);
    if (live is null) { Console.Error.WriteLine($"تَخَطٍّ: {p.Id} غَير مَوجودَة."); continue; }
    var before = live.ExpiresAt;
    if (TenantPlanDayEndRepair.Apply(live) == 0)   // ← تَكافُؤٌ: إعادَةُ التَشغيلِ لا تَجِدُ ما تُغَيِّرُه
    { Console.WriteLine($"تَخَطٍّ: {p.Id} تَغَيَّرَت بَينَ العَرضِ والتَنفيذ."); continue; }
    s.Store(live);
    await s.SaveChangesAsync();

    await audit.WriteAsync(p.Id, null, "repair-plan-day-end",
        TenantPlanDayEndRepair.AuditAction, "TenantPlan", p.Id,
        note: $"status={live.Status}",
        before: before.ToString("O"), after: live.ExpiresAt.ToString("O"));
    Console.WriteLine($"OK {p.Id}  {before:O} ← {live.ExpiresAt:O}  ({live.Status})");
    done++;
}

Console.WriteLine();
Console.WriteLine($"تَمَّ تَرميمُ {done} مِن {plan.Count}. وسَطرُ التَدقيقِ لِكُلٍّ «{TenantPlanDayEndRepair.AuditAction}».");
return 0;

static string Quote(string s) => "'" + s.Replace("'", "''") + "'";
