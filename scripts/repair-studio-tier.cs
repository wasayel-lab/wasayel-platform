#!/usr/bin/env dotnet
#:property JsonSerializerIsReflectionEnabledByDefault=true
#:property PublishAot=false
#:project ../libs/templates/ACommerce.Templates.Customer.Marketplace/ACommerce.Templates.Customer.Marketplace.csproj
// ═══════════════════════════════════════════════════════════════════════
//  إعادَةُ مُستَخدِمي الاستوديو العالِقينَ بِدَرَجَةٍ أَضيَقَ مِن المَجّانِيَّةِ إلَيها
// ───────────────────────────────────────────────────────────────────────
//  **لِماذا هذا المِلَفّ** (‏2026-10-29، قَرارُ المالِك د16 «أ»؛ ‏ADR-026 مُلحَق د16): مَن خُزِّنَت لَه `lite` أَو `growth`
//  قَبلَ ‏2026-08-30 حُدودُه اليَومَ أَضيَقُ مِن المَجّانِيَّةِ المَرفوعَة (‏40 تَحليلاً · 200 تَحسين · 40 مَتجَراً، مُقابِلَ 3 إلى 10)
//  ولا رُجوعَ لَه إلَيها. «أَفضَلُ المُمارَساتِ عِندَ تَغييرِ الباقاتِ أَلّا يَكونَ العَميلُ الأَقدَمُ أَسوَأَ حالاً مِن الجَديد».
//
//  **والقَرارُ كُلُّه في `StudioTierRepair.IsStuck`**: دَرَجَةٌ مَعروفَةٌ تَتَفَوَّقُ عَلَيها المَجّانِيَّةُ اليَومَ بِالأَرقام — فَيَومَ يُنهى
//  الرَفعُ المُؤَقَّتُ لا يَعودُ مُرَشَّحٌ واحِد. **والدَرَجَةُ السابِقَةُ تُحفَظُ لِكُلِّ مُستَخدِمٍ في سَطرِ تَدقيق** (`before`)، ويَكتُبُ
//  التَنفيذُ أَمرَ استِرجاعٍ بِالمُعَرِّفاتِ والدَرَجاتِ وَحدَها — **ولا نُسخَةَ بِبَياناتٍ شَخصِيَّة** (الوَثيقَةُ فيها جَوّالٌ وبَريد):
//  النُسخَةُ الكامِلَةُ لَقطَةُ القاعِدَةِ الَّتي تَسبِقُه (`marten-schema.cs --snapshot`، ‏DEPLOY.md §٤‑د). ولا يُطبَعُ جَوّالٌ ولا بَريدٌ ولا اسم.
//  **ويُجَرَّبُ أَوَّلاً عَلى فَرعِ Neon مَنسوخ.**
//
// ─── الاستِعمال ────────────────────────────────────────────────────────
//   export ConnectionStrings__Postgres="…"        # ولا تُطبَع أَبَداً
//   dotnet run scripts/repair-studio-tier.cs                    # عَرضٌ فَقَط
//   dotnet run scripts/repair-studio-tier.cs -- --apply --confirm 3
//
//   --apply             يُنَفِّذ. بِدونِه **عَرضُ ما سَيَتَغَيَّرُ فَقَط**.
//   --confirm <N>       عَدَدُ المُستَخدِمينَ كَما طَبَعَه العَرض. اختِلافُه ⇐ تَوَقُّفٌ بِلا كِتابَة.
//   --backup-dir <path>  مَجَلَّدُ أَمرِ الاستِرجاع (‏`backups/`).
//
// ─── رُموزُ الخُروج ────────────────────────────────────────────────────
//   0 — لا أَحَدَ عالِق، أَو نُفِّذَ بِنَجاح.
//   2 — عَرضٌ وَجَدَ عالِقين (لَم يُكتَب شَيء).
//   1 — خَطَأ، أَو صِفرُ مُستَخدِمٍ مَفحوص، أَو --confirm لا يُطابِق.
// ═══════════════════════════════════════════════════════════════════════

using System.Security.Cryptography;
using System.Text;
using ACommerce.Kit.Tenants;
using ACommerce.Templates.Customer.Marketplace.Services.Audit;
using ACommerce.Templates.Customer.Marketplace.Services.Incubator;
using JasperFx;
using Marten;

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
    o.AutoCreateSchemaObjects = AutoCreate.None;   // ← لا يُمَسُّ المُخَطَّط
    o.Policies.AllDocumentsAreMultiTenanted();
    o.Schema.For<Tenant>().SingleTenanted().Identity(x => x.Id);
});

List<StudioUser> users;
await using (var qs = store.QuerySession(StudioAuth.Tenant))
    users = (await qs.Query<StudioUser>().ToListAsync()).OrderBy(u => u.CreatedAt).ThenBy(u => u.Id).ToList();

Console.WriteLine($"القاعِدَة: بَصمَة {fingerprint} · مُستَخدِمو الاستوديو: {users.Count}");
Console.WriteLine($"الوَضع:   {(apply ? "تَنفيذ (--apply)" : "عَرضُ ما سَيَتَغَيَّر — بِلا كِتابَة")}");
Console.WriteLine($"المَجّانِيَّةُ اليَوم: {Describe(TierCatalog.For(StudioTierRepair.Free))} · الرَفعُ المُؤَقَّت: {(TierCatalog.FreeTierTemporarilyRaised ? "قائِم" : "مُنتَهٍ")}");
Console.WriteLine(new string('-', 78));
if (users.Count == 0)
{
    Console.Error.WriteLine("✗ BLIND CHECK: صِفرُ مُستَخدِمٍ مَفحوص — لا يُقال «لا أَحَدَ عالِق» عَن قاعِدَةٍ لَم تُقرَأ.");
    return 1;
}

foreach (var g in users.GroupBy(u => string.IsNullOrEmpty(u.Tier) ? "(فارِغ)" : u.Tier).OrderBy(g => g.Key, StringComparer.Ordinal))
    Console.WriteLine($"  الدَرَجَة {g.Key,-10} : {g.Count()}{(StudioTierRepair.IsStuck(g.Key) ? $"  ← عالِقونَ بِحُدودٍ أَضيَق ({Describe(TierCatalog.For(g.Key))})" : "")}");

var plan = users.Where(u => StudioTierRepair.IsStuck(u.Tier)).ToList();
Console.WriteLine($"عالِقٌ يُعادُ إلى {StudioTierRepair.Free}: {plan.Count}");
if (plan.Count == 0)
{
    Console.WriteLine("لا أَحَدَ عالِق.");
    return 0;
}
foreach (var u in plan)
    Console.WriteLine($"  # {u.Id}  {u.Tier} ← {StudioTierRepair.Free}");
Console.WriteLine();

if (!apply)
{
    Console.WriteLine("عَرضٌ فَقَط — لَم يُكتَب شَيء. لِلتَنفيذ بَعدَ مُراجَعَةِ العَدَد:");
    Console.WriteLine($"  dotnet run scripts/repair-studio-tier.cs -- --apply --confirm {plan.Count}");
    return 2;
}

if (confirm is null || confirm != plan.Count)
{
    Console.Error.WriteLine($"تَوَقُّف: --confirm {confirm?.ToString() ?? "(غائِب)"} لا يُطابِقُ {plan.Count} عالِقاً.");
    Console.Error.WriteLine("أَعِد العَرضَ ثُمَّ نَفِّذ بِالعَدَدِ الَّذي طَبَعَه. لا كِتابَةَ عَلى العَمياء.");
    return 1;
}

// ─── أَمرُ الاستِرجاع — بِالمُعَرِّفاتِ والدَرَجاتِ وَحدَها، قَبلَ أَوَّلِ كِتابَة ──
Directory.CreateDirectory(backupDir);
var stamp   = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
var sqlPath = Path.Combine(backupDir, $"studio-tier-{stamp}.restore.sql");
var sb = new StringBuilder();
sb.AppendLine("-- اِستِرجاعُ دَرَجاتِ مُستَخدِمي الاستوديو كَما كانَت قَبلَ إعادَتِهِم إلى المَجّانِيَّة (مُعَرِّفاتٌ ودَرَجاتٌ وَحدَها).");
sb.AppendLine($"-- بَصمَةُ القاعِدَة: {fingerprint} · التارِيخ: {stamp}Z");
sb.AppendLine("BEGIN;");
foreach (var u in plan)
    sb.AppendLine($"UPDATE platform.mt_doc_studiouser SET data = jsonb_set(data, '{{Tier}}', to_jsonb('{u.Tier.Replace("'", "''")}'::text)) "
                + $"WHERE id = '{u.Id}' AND tenant_id = '{StudioAuth.Tenant}';");
sb.AppendLine("COMMIT;");
await File.WriteAllTextAsync(sqlPath, sb.ToString());
Console.WriteLine($"أَمرُ الاسترجاع: {sqlPath}");
Console.WriteLine();

var audit = new AuditWriter(store);
var done = 0;
foreach (var u in plan)
{
    await using var s = store.LightweightSession(StudioAuth.Tenant);
    var live = await s.LoadAsync<StudioUser>(u.Id);
    if (live is null || !StudioTierRepair.IsStuck(live.Tier))   // ← تَكافُؤٌ: إعادَةُ التَشغيلِ لا تَدوس
    { Console.WriteLine($"تَخَطٍّ: {u.Id} تَغَيَّرَ بَينَ العَرضِ والتَنفيذ."); continue; }
    var previous = live.Tier;
    live.Tier = StudioTierRepair.Free;
    s.Store(live);
    await s.SaveChangesAsync();

    await audit.WriteAsync(AuditWriter.PlatformScope, null, "repair-studio-tier",
        StudioTierRepair.AuditAction, "studio_tier", u.Id.ToString(),
        note: $"previous={previous}",
        before: previous, after: StudioTierRepair.Free);
    Console.WriteLine($"OK {u.Id}  {previous} ← {StudioTierRepair.Free}");
    done++;
}

Console.WriteLine();
Console.WriteLine($"تَمَّت إعادَةُ {done} مِن {plan.Count}. والدَرَجَةُ السابِقَةُ لِكُلٍّ في سَطرِ التَدقيق «{StudioTierRepair.AuditAction}».");
return 0;

static string Describe(TierLimits t) => $"{t.AnalysesPerMonth} تَحليلاً · {t.RefinesPerMonth} تَحسيناً · {t.StoresMax} مَتجَراً";
