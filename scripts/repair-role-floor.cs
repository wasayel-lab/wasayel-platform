#!/usr/bin/env dotnet
#:property JsonSerializerIsReflectionEnabledByDefault=true
#:project ..\libs\templates\ACommerce.Templates.Customer.Marketplace\ACommerce.Templates.Customer.Marketplace.csproj
// ═══════════════════════════════════════════════════════════════════════
//  تَرميمُ أَرضِيَّةِ الأَدوار — لِلمَتاجِرِ الَّتي وُلِدَت قَبلَ الإصلاح
// ───────────────────────────────────────────────────────────────────────
//  **لِماذا هذا المِلَفّ**: إصلاحُ `b6a85753` يَمنَعُ **الجَديد** ولا
//  يُصلِحُ **القائِم**. المَتجَرُ الَّذي بُنِيَ مِن مَسارِ العَميلِ قَبلَه
//  ما زالَ في القاعِدَةِ بِـ`Roles = []`، و`RolePermissions.Has` تَقرَأُ
//  الصِفرَ «وَضعاً موروثاً» فَتُعيدُ `true` لِكُلِّ صَلاحِيَّةٍ لِكُلِّ
//  مَن سَجَّلَ في ذلكَ المَتجَر — ومِنها `tenant.manage`.
//
//  **ولا يَختَرِعُ هذا السكربتُ دَوراً واحِداً** (القاعِدَة ١٦): الأَدوارُ
//  تُؤخَذُ حَرفاً مِن `TenantFromAnalysisFactory.RolesFor` — **نَفسِ
//  الدالَّةِ الَّتي يَستَدعيها مَسارُ البِناءِ اليَوم** — بَعدَ تَسوِيَةِ
//  النَمَطِ بِـ`NormalizePattern`. فَما يُكتَبُ تَرميماً هُوَ ما كانَ
//  سَيُكتَبُ لَو أَنَّ المَتجَرَ بُنِيَ بَعدَ الإصلاح، بِالبِنيَةِ لا
//  بِالانضِباط. ولا نُسخَةَ ثانِيَةً مِنَ الخَريطَةِ تَنجَرِف.
//
//  **ومِن أَينَ يُؤخَذُ النَمَط**: مِن جَلسَةِ التَحليلِ الَّتي وَلَدَت
//  المَتجَر (`Tenant.SourceAnalysisId` ⇐ `IncubatorSession.SuggestedPattern`).
//  والفارِغُ يَسقُطُ إلى `marketplace` — وهُوَ **سُقوطُ الكودِ نَفسِه**
//  لا اختِيارٌ جَديد. والقَرينَةُ مَقيسَةٌ في القاعِدَة: مَتجَرا
//  `asal-albaha` و`tomoor-qassim` فِئاتُهُما `products,deals` وهي
//  مُخرَجُ `CategoriesForSector("ecommerce")` — أَي قِطاعُ `marketplace`
//  بِعَينِه؛ ولَونُ `asal-albaha` ‏`#2563eb` هُوَ `ColorForPattern("marketplace")`
//  حَرفاً. فَالتَرميمُ يُكمِلُ ما بَدَأَهُ البِناءُ لا يُخالِفُه.
//
// ─── ما لا يَفعَلُه — وذلك مَقصودٌ ومُعلَن ──────────────────────────────
//   ١. **لا يَلمَسُ `User.ActiveRole` لِأَحَد.** مَن يَصيرُ `tenant_admin`
//      في مَتجَرٍ قائِمٍ **قَرارُ صاحِبِ المَشروع** لا قَرارُ سكربت
//      (‏DECISIONS-PENDING). والسكربتُ يَطبَعُ مالِكَ كُلِّ مَتجَرٍ
//      لِيُتَّخَذَ القَرارُ على بَيان.
//   ٢. **لا يَلمَسُ مَتجَراً بِلا `SourceAnalysisId`** (‏`theme-demo`
//      يُبذَرُ بِـ`Roles = new()` عَمداً لِلَقطَةِ المَظهَر). ومَن أَرادَ
//      شُمولَه فَبِـ`--include-non-studio` صَريحاً.
//   ٣. **لا يُضيفُ `tenant_admin` إلى مَتجَرٍ لَه أَدوارٌ أَصلاً**
//      (‏`ashare`/`ejar`/`order` لَيسَ فيها دَورٌ يَحمِلُ `tenant.manage`
//      اليَوم — مَقيس). إضافَتُه إلَيها **تَفتَحُ** بابَ `/me/save` لا
//      تَسُدُّه، وهي قَرارُ مُنتَجٍ لا تَرميمُ عَطَب.
//   ٤. **لا يَمَسُّ المُخَطَّط**: `AutoCreate.None` — ‏Marten 9 يُعيدُ
//      كِتابَةَ مُخَطَّطِ Marten 8 بِمُجَرَّدِ الإقلاع (‏CLAUDE.md)،
//      فَالسكربتُ يُمنَعُ مِن ذلكَ بِالبِنيَة.
//
// ─── الاستِعمال ────────────────────────────────────────────────────────
//   export ConnectionStrings__Postgres="…"        # ولا تُطبَع أَبَداً
//   dotnet run scripts/repair-role-floor.cs                    # عَرضٌ فَقَط
//   dotnet run scripts/repair-role-floor.cs -- --apply --confirm 2
//
//   --apply             يُنَفِّذ. بِدونِه **عَرضُ ما سَيَتَغَيَّرُ فَقَط**.
//   --confirm <N>       عَدَدُ المَتاجِرِ المُتَوَقَّعُ تَرميمُها كَما
//                       طَبَعَهُ العَرض. اختِلافُه ⇐ توقُّفٌ بِلا كِتابَة.
//                       (‏القاعِدَةُ انجَرَفَت عَنكَ فَلا تَكتُب على العَمياء.)
//   --include-non-studio يَشمَلُ مَتاجِرَ بِلا `SourceAnalysisId`.
//   --backup-dir <path>  مَجَلَّدُ النُسخَةِ الاحتِياطِيَّة (‏`backups/`).
//
// ─── رُموزُ الخُروج ────────────────────────────────────────────────────
//   0 — لا شَيءَ لِيُرَمَّم، أَو نُفِّذَ التَرميمُ بِنَجاح.
//   2 — عَرضٌ وَجَدَ مُرَشَّحين (لَم يُكتَب شَيء). صالِحٌ لِبَوّابَةٍ آلِيَّة.
//   1 — خَطَأ.
// ═══════════════════════════════════════════════════════════════════════

using System.Security.Cryptography;
using System.Text;
using ACommerce.Kit.Roles;
using ACommerce.Kit.Tenants;
using ACommerce.Templates.Customer.Marketplace.Services.Audit;
using ACommerce.Templates.Customer.Marketplace.Services.Incubator;
using JasperFx;
using Marten;
using Npgsql;

var apply            = args.Contains("--apply");
var includeNonStudio = args.Contains("--include-non-studio");
var confirmIdx       = Array.IndexOf(args, "--confirm");
int? confirm         = confirmIdx >= 0 && confirmIdx + 1 < args.Length
                         && int.TryParse(args[confirmIdx + 1], out var c) ? c : null;
var backupIdx        = Array.IndexOf(args, "--backup-dir");
var backupDir        = backupIdx >= 0 && backupIdx + 1 < args.Length
                         ? args[backupIdx + 1] : "backups";

var cs = Environment.GetEnvironmentVariable("ConnectionStrings__Postgres");
if (string.IsNullOrWhiteSpace(cs))
{
    Console.Error.WriteLine("خَطَأ: ‏ConnectionStrings__Postgres غَير مَضبوط.");
    return 1;
}

// بَصمَةُ القاعِدَة — تُعَرِّفُ **أَيَّ** قاعِدَةٍ بِلا كَشفِ حَرفٍ مِنها.
// (‏كُلُّ تَشغيلٍ يَطبَعُها، فَيُقارَنُ التَنفيذُ بِالعَرضِ بِيَقين.)
var fingerprint = Convert.ToHexString(
    SHA256.HashData(Encoding.UTF8.GetBytes(cs))).ToLowerInvariant()[..12];

using var store = DocumentStore.For(o =>
{
    o.Connection(cs);
    o.DatabaseSchemaName = "platform";
    o.AutoCreateSchemaObjects = AutoCreate.None;   // ← لا يُمَسُّ المُخَطَّط
    o.Policies.AllDocumentsAreMultiTenanted();
    o.Schema.For<Tenant>().SingleTenanted().Identity(x => x.Id);
});

await using var conn = new NpgsqlConnection(cs);
await conn.OpenAsync();

// ─── ١) القِراءَة: كُلُّ المَتاجِرِ وحالُ أَدوارِها ──────────────────────
await using var qs = store.QuerySession();
var tenants = (await qs.Query<Tenant>().ToListAsync()).OrderBy(t => t.Id).ToList();

Console.WriteLine($"القاعِدَة: بَصمَة {fingerprint} · المَتاجِر: {tenants.Count}");
Console.WriteLine($"الوَضع:   {(apply ? "تَنفيذ (--apply)" : "عَرضُ ما سَيَتَغَيَّر — بِلا كِتابَة")}");
Console.WriteLine(new string('-', 78));

var plan = new List<(Tenant T, string RawPattern, string Pattern,
                     List<Role> Roles, long Members, bool Studio)>();
var skipped = new List<(string Slug, string Why, long Members)>();

foreach (var t in tenants)
{
    var members = (long)(await Scalar(conn,
        "select count(*) from platform.mt_doc_user where tenant_id = @s", ("s", t.Id)) ?? 0L);

    if (t.Roles.Count > 0)
    {
        // مَتجَرٌ لَه أَدوار — خارِجَ نِطاقِ هذا التَرميمِ حَتّى لَو
        // لَم يَكُن فيه `tenant.manage`. إضافَتُهُ قَرارُ مُنتَج.
        var hasManage = t.Roles.Any(r => r.Permissions.Contains("tenant.manage"));
        skipped.Add((t.Id, hasManage ? "لَه أَدوارٌ وفيها tenant.manage"
                                     : "لَه أَدوارٌ بِلا tenant.manage — قَرارُ مُنتَجٍ لا تَرميم", members));
        continue;
    }

    var studio = t.SourceAnalysisId is not null;
    if (!studio && !includeNonStudio)
    {
        skipped.Add((t.Id, "صِفرُ أَدوارٍ وبِلا SourceAnalysisId — وَضعٌ موروثٌ مَقصود (‏--include-non-studio لِشُمولِه)", members));
        continue;
    }

    var raw = studio
        ? (string?)await Scalar(conn,
            "select data->>'SuggestedPattern' from platform.mt_doc_incubatorsession where id = @i",
            ("i", t.SourceAnalysisId!.Value)) ?? ""
        : "";

    var pattern = TenantFromAnalysisFactory.NormalizePattern(raw);
    var roles   = TenantFromAnalysisFactory.RolesFor(pattern).ToList();
    plan.Add((t, string.IsNullOrEmpty(raw) ? "(فارِغ)" : raw, pattern, roles, members, studio));
}

// ─── ٢) العَرض ─────────────────────────────────────────────────────────
if (skipped.Count > 0)
{
    Console.WriteLine("مَتروك:");
    foreach (var (slug, why, m) in skipped)
        Console.WriteLine($"  - {slug,-20} أَعضاء={m,-3} {why}");
    Console.WriteLine();
}

if (plan.Count == 0)
{
    Console.WriteLine("لا مَتجَرَ يَحتاجُ تَرميماً.");
    return 0;
}

Console.WriteLine($"مُرَشَّحٌ لِلتَرميم: {plan.Count}");
foreach (var p in plan)
{
    Console.WriteLine($"  # {p.T.Id}");
    Console.WriteLine($"      أَعضاء الآن            : {p.Members}");
    Console.WriteLine($"      نَمَطُ الجَلسَة (خام)   : {p.RawPattern}");
    Console.WriteLine($"      بَعدَ NormalizePattern  : {p.Pattern}");
    Console.WriteLine($"      أَدوارٌ ستُكتَب        : {string.Join(", ", p.Roles.Select(r => r.Slug + (r.IsDefault ? "*" : "")))}");
    Console.WriteLine($"      مالِكُ الاستوديو        : {p.T.OwnerUserId}");
    Console.WriteLine($"      tenant_admin لِمَن؟     : قَرارُ صاحِبِ المَشروع. السكربتُ لا يَمنَحُه.");
    Console.WriteLine($"      أَثَرٌ على الأَعضاء      : ActiveRole يَبقى كَما هُوَ؛ والفارِغُ لا يَحمِلُ صَلاحِيَّةً بَعدَ اليَوم (الافتِراضيُّ '{p.Roles.First(r => r.IsDefault).Slug}' يُختارُ مِن /me/edit).");
}
Console.WriteLine();

if (!apply)
{
    Console.WriteLine("عَرضٌ فَقَط — لَم يُكتَب شَيء. لِلتَنفيذ:");
    Console.WriteLine($"  dotnet run scripts/repair-role-floor.cs -- --apply --confirm {plan.Count}"
                    + (includeNonStudio ? " --include-non-studio" : ""));
    return 2;
}

// ─── ٣) بَوّابَةُ التَأكيد ──────────────────────────────────────────────
if (confirm is null || confirm != plan.Count)
{
    Console.Error.WriteLine($"تَوَقُّف: --confirm {confirm?.ToString() ?? "(غائِب)"} لا يُطابِقُ {plan.Count} مُرَشَّحاً.");
    Console.Error.WriteLine("أَعِد العَرضَ ثُمَّ نَفِّذ بِالعَدَدِ الَّذي طَبَعَه. لا كِتابَةَ على العَمياء.");
    return 1;
}

// ─── ٤) النُسخَةُ الاحتِياطِيَّة — قَبلَ أَوَّلِ كِتابَة ────────────────
Directory.CreateDirectory(backupDir);
var stamp    = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
var jsonPath = Path.Combine(backupDir, $"role-floor-{stamp}.json");
var sqlPath  = Path.Combine(backupDir, $"role-floor-{stamp}.restore.sql");

var rows = new List<(string Id, string Data)>();
foreach (var p in plan)
{
    var data = (string?)await Scalar(conn,
        "select data::text from platform.mt_doc_tenant where id = @s", ("s", p.T.Id));
    if (data is null) { Console.Error.WriteLine($"خَطَأ: {p.T.Id} اختَفى بَينَ العَرضِ والتَنفيذ."); return 1; }
    rows.Add((p.T.Id, data));
}

await File.WriteAllTextAsync(jsonPath,
    "[\n" + string.Join(",\n", rows.Select(r =>
        $"  {{\"id\": {System.Text.Json.JsonSerializer.Serialize(r.Id)}, \"data\": {r.Data}}}")) + "\n]\n");

var sb = new StringBuilder();
sb.AppendLine("-- اِستِرجاعُ وَثائِقِ المُستَأجِرِ كَما كانَت قَبلَ التَرميم.");
sb.AppendLine($"-- بَصمَةُ القاعِدَة: {fingerprint} · التارِيخ: {stamp}Z");
sb.AppendLine("BEGIN;");
foreach (var r in rows)
    sb.AppendLine($"UPDATE platform.mt_doc_tenant SET data = {Quote(r.Data)}::jsonb WHERE id = {Quote(r.Id)};");
sb.AppendLine("COMMIT;");
await File.WriteAllTextAsync(sqlPath, sb.ToString());

Console.WriteLine($"نُسخَةٌ احتِياطِيَّة: {jsonPath}");
Console.WriteLine($"وأَمرُ الاسترجاع  : {sqlPath}");
Console.WriteLine();

// ─── ٥) التَنفيذ ───────────────────────────────────────────────────────
var audit = new AuditWriter(store);
var done = 0;
foreach (var p in plan)
{
    await using var s = store.LightweightSession();
    var live = await s.LoadAsync<Tenant>(p.T.Id);
    if (live is null) { Console.Error.WriteLine($"تَخَطٍّ: {p.T.Id} غَير مَوجود."); continue; }
    if (live.Roles.Count > 0)                       // ← تَكافُؤٌ: إعادَةُ التَشغيلِ لا تَدوس
    { Console.WriteLine($"تَخَطٍّ: {p.T.Id} صارَت لَه أَدوارٌ بَينَ العَرضِ والتَنفيذ."); continue; }

    live.Roles = p.Roles;
    s.Store(live);
    await s.SaveChangesAsync();

    await audit.WriteAsync(p.T.Id, null, "repair-role-floor",
        "tenant.roles_repair", "Tenant", p.T.Id,
        note: $"pattern={p.Pattern} (raw={p.RawPattern})",
        before: "[]",
        after: string.Join(",", p.Roles.Select(r => r.Slug)));

    Console.WriteLine($"OK {p.T.Id} <- {string.Join(", ", p.Roles.Select(r => r.Slug))}");
    done++;
}

Console.WriteLine();
Console.WriteLine($"تَمَّ تَرميمُ {done} مِن {plan.Count}.");
Console.WriteLine("وما زالَ مُعَلَّقاً: مَنحُ tenant_admin لِمالِكِ كُلِّ مَتجَر — قَرارُ صاحِبِ المَشروع.");
return 0;

// ─── أَدَواتٌ صَغيرَة ───────────────────────────────────────────────────
static string Quote(string s) => "'" + s.Replace("'", "''") + "'";

static async Task<object?> Scalar(NpgsqlConnection conn, string sql, params (string, object)[] ps)
{
    await using var cmd = new NpgsqlCommand(sql, conn);
    foreach (var (n, v) in ps) cmd.Parameters.AddWithValue(n, v);
    var r = await cmd.ExecuteScalarAsync();
    return r is DBNull ? null : r;
}
