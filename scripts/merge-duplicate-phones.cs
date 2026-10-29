#!/usr/bin/env dotnet
#:property JsonSerializerIsReflectionEnabledByDefault=true
#:property PublishAot=false
#:project ../libs/templates/ACommerce.Templates.Customer.Marketplace/ACommerce.Templates.Customer.Marketplace.csproj
// ═══════════════════════════════════════════════════════════════════════
//  دَمجُ الحِساباتِ المُكَرَّرَةِ بِصيغَتَي الجَوّال، وكَتبُ كُلِّ جَوّالٍ بِصيغَتِه الدَولِيَّة
// ───────────────────────────────────────────────────────────────────────
//  **لِماذا هذا المِلَفّ** (‏2026-10-29، قَرارُ المالِك د57 (٣) «أ»؛ ‏ADR-044 §٤): «ترحيلٌ يَكتُبُ
//  كُلَّ رَقَمٍ بِالصيغَةِ الدَولِيَّةِ المُوَحَّدَة، ويَجمَعُ إعلاناتِ الشَخصِ وصَفَقاتِه في حِسابٍ
//  واحِد» — و`ashare` داخِلٌ فيه كَأَيِّ مَتجَر (قَرارُ المالِك في `ashare`).
//
//  **ما يَفعَلُه — والقَرارُ كُلُّه في `DuplicatePhones` لا هُنا**: لِكُلِّ مَتجَر، المُفرَدُ الَّذي
//  لَيسَ بِصيغَتِه الدَولِيَّةِ يُعادُ كَتبُ جَوّالِه؛ والمَجموعَةُ (رَقَمٌ واحِدٌ بِأَكثَرَ مِن حِساب) يَبقى
//  أَقدَمُ حِساباتِها، وتُقاسُ (`ScanAsync`) ثُمَّ تُدمَجُ في مُعامَلَةٍ واحِدَة (`ApplyAsync`):
//  إعلاناتُ المُندَمِجِ بِحَدَثٍ يُسَمّي مالِكَها الجَديد، وصَفَقاتُه ومُحادَثاتُه ورَسائِلُه وإشعاراتُه
//  ومُفَضَّلاتُه وما سِواها تُعادُ إلى الباقي، ومَفاتيحُ رَبطِه (وَثيقَةٌ عامَّة) يُكتَبُ فاعِلَها الباقي،
//  وسَلَّتُه تُنقَل، ثُمَّ يُمحى كَحِسابٍ
//  مَحذوف (فَتَسقُطُ جَلَساتُه). ومَجموعَةٌ فيها مانِعٌ (صَفقَةُ المَرءِ مَعَ نَفسِه، مَجرى أَحداثٍ غَيرُ
//  الإعلان، …) لا يُكتَبُ فيها حَرف — يُطبَعُ مانِعُها. وحِساباتُ الاستوديو يُعادُ كَتبُ جَوّالِ
//  مُفرَدِها، ومَجموعاتُها لا تُدمَج (حِسابُ الاستوديو يَملِكُ مَتاجِر — «أ» د57).
//
//  **وما لا يَفعَلُه**: لا يَكتُبُ نُسخَةً احتِياطِيَّةً بِبَياناتٍ شَخصِيَّة — النُسخَةُ لَقطَةُ القاعِدَةِ
//  الَّتي تَسبِقُه (`marten-schema.cs --snapshot`، ‏DEPLOY.md §٤‑د، وهِيَ تَرفُضُ مَساراً داخِلَ
//  شَجَرَةِ git)؛ ولا يَطبَعُ جَوّالاً ولا بَريداً؛ ولا يَمَسُّ المُخَطَّط (`AutoCreate.None`).
//  وكُلُّ مَجموعَةٍ تُدمَجُ تَكتُبُ سَطرَ تَدقيقٍ (`user.phone_merge`) بِالمُعَرِّفاتِ وَحدَها.
//
// ─── الاستِعمال ────────────────────────────────────────────────────────
//   export ConnectionStrings__Postgres="…"        # ولا تُطبَع أَبَداً
//   dotnet run scripts/merge-duplicate-phones.cs                      # عَرضٌ فَقَط
//   dotnet run scripts/merge-duplicate-phones.cs -- --apply --confirm 7
//
//   --apply        يُنَفِّذ. بِدونِه **عَرضُ ما سَيَتَغَيَّرُ فَقَط**.
//   --confirm <N>  عَدَدُ التَغييراتِ كَما طَبَعَه العَرض (إعادَةُ كَتبٍ + مَجموعاتٌ تُدمَج). اختِلافُه ⇐
//                  تَوَقُّفٌ بِلا كِتابَة — القاعِدَةُ انجَرَفَت عَنكَ فَلا تَكتُب عَلى العَمياء.
//
// ─── رُموزُ الخُروج ────────────────────────────────────────────────────
//   0 — لا شَيءَ لِيُغَيَّر، أَو نُفِّذَ بِنَجاح.
//   2 — عَرضٌ وَجَدَ تَغييرات (لَم يُكتَب شَيء).
//   1 — خَطَأ، أَو صِفرُ مَتجَرٍ مَفحوص، أَو --confirm لا يُطابِق.
// ═══════════════════════════════════════════════════════════════════════

using System.Security.Cryptography;
using System.Text;
using ACommerce.Kit.Auth;
using ACommerce.Kit.Listings;
using ACommerce.Kit.Tenants;
using ACommerce.Templates.Customer.Marketplace.Services.Audit;
using ACommerce.Templates.Customer.Marketplace.Services.Api;
using ACommerce.Templates.Customer.Marketplace.Services.Identity;
using ACommerce.Templates.Customer.Marketplace.Services.Incubator;
using JasperFx;
using JasperFx.Events.Projections;
using Marten;
using Npgsql;

var apply = args.Contains("--apply");
var confirmIdx = Array.IndexOf(args, "--confirm");
int? confirm = confirmIdx >= 0 && confirmIdx + 1 < args.Length && int.TryParse(args[confirmIdx + 1], out var c) ? c : null;

var cs = Environment.GetEnvironmentVariable("ConnectionStrings__Postgres");
if (string.IsNullOrWhiteSpace(cs))
{
    Console.Error.WriteLine("خَطَأ: ‏ConnectionStrings__Postgres غَير مَضبوط.");
    return 1;
}
var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(cs))).ToLowerInvariant()[..12];

// ‏المَخزَنُ كَما يُرَكِّبُه المُضيفُ لِما يَمَسُّه الدَمج (‏`HostingExtensions.AddPlatformHost`): المُخَطَّطُ `platform`، والإيجارُ المُقتَرِنُ لِلوَثائِقِ
// والأَحداث، و`Tenant` عامّ، و**نُسخَةُ `Listing` تُحَدَّثُ في المُعامَلَة** — فَحَدَثُ المالِكِ الجَديدِ يَبلُغُ ما تَقرَؤُه «إعلاناتي».
// و`ApiKeyDocument` عامَّةٌ كَما يُسَجِّلُها `MarketplaceTemplateExtensions` — بِغَيرِها يَسأَلُها Marten بِـ`tenant_id` لا عَمودَ لَه.
using var store = DocumentStore.For(o =>
{
    o.Connection(cs);
    o.DatabaseSchemaName = "platform";
    o.AutoCreateSchemaObjects = AutoCreate.None;   // ← لا يُمَسُّ المُخَطَّط (‏Marten 9 يُعيدُ كِتابَةَ مُخَطَّطِ Marten 8 — CLAUDE.md)
    o.Policies.AllDocumentsAreMultiTenanted();
    o.Events.TenancyStyle = JasperFx.MultiTenancy.TenancyStyle.Conjoined;
    o.Schema.For<Tenant>().SingleTenanted().Identity(x => x.Id);
    o.Schema.For<ApiKeyDocument>().SingleTenanted().Identity(x => x.Id);   // ← عامٌّ كَما يُسَجِّلُه القالِب: مَفاتيحُ الرَبطِ تُنقَلُ بِحَقلِها
    o.Projections.Snapshot<Listing>(SnapshotLifecycle.Inline);
});
await using var conn = new NpgsqlConnection(cs);
await conn.OpenAsync();

List<string> tenants;
await using (var qs = store.QuerySession())
    tenants = (await qs.Query<Tenant>().Select(t => t.Id).ToListAsync()).OrderBy(t => t, StringComparer.Ordinal).ToList();

Console.WriteLine($"القاعِدَة: بَصمَة {fingerprint} · المَتاجِر: {tenants.Count}");
Console.WriteLine($"الوَضع:   {(apply ? "تَنفيذ (--apply)" : "عَرضُ ما سَيَتَغَيَّر — بِلا كِتابَة")}");
Console.WriteLine(new string('-', 78));
if (tenants.Count == 0)
{
    Console.Error.WriteLine("✗ BLIND CHECK: صِفرُ مَتجَرٍ مَفحوص — لا يُقال «لا شَيءَ لِيُدمَج» عَن قاعِدَةٍ لَم تُقرَأ.");
    return 1;
}

var tables = await DuplicatePhones.TenantTablesAsync(conn);
var work = new List<(string Slug, DuplicatePhones.Plan Plan, List<DuplicatePhones.Scan> Mergeable)>();
var changes = 0;
foreach (var slug in tenants)
{
    List<DuplicatePhones.Account> members;
    await using (var q = store.QuerySession(slug))
        members = (await q.Query<User>().ToListAsync())
            .Select(u => new DuplicatePhones.Account(u.Id, u.Phone, u.CreatedAt, u.DeletedAt is not null)).ToList();
    var plan = DuplicatePhones.Build(members);
    var ok = new List<DuplicatePhones.Scan>();
    foreach (var g in plan.Groups)
    {
        var scan = await DuplicatePhones.ScanAsync(conn, slug, g, tables);
        if (scan.Mergeable) ok.Add(scan);
        else Console.WriteLine($"  ✗ {slug}: مَجموعَةٌ مِن {g.Losers.Count + 1} حِسابات مُمتَنِعَة — {string.Join(" · ", scan.Blocks)}");
    }
    Console.WriteLine($"{slug,-24} يُعادُ كَتبُه={plan.Renames.Count,-4} تُدمَج={ok.Count,-3} (تَندَمِجُ حِسابات={ok.Sum(s => s.Group.Losers.Count)}، "
        + $"إعلانات={ok.Sum(s => s.Listings.Count)}، صُفوفٌ تُعادُ={ok.Sum(s => s.Repoints.Sum(r => r.Rows))}، سِلال={ok.Sum(s => s.LoserCarts)}، مَفاتيحُ رَبط={ok.Sum(s => s.Keys)}) "
        + $"مُمتَنِعَة={plan.Groups.Count - ok.Count}");
    work.Add((slug, plan, ok));
    changes += plan.Renames.Count + ok.Count;
}

List<DuplicatePhones.Account> studio;
await using (var q = store.QuerySession(StudioAuth.Tenant))
    studio = (await q.Query<StudioUser>().ToListAsync()).Select(u => new DuplicatePhones.Account(u.Id, u.Phone, u.CreatedAt, false)).ToList();
var studioPlan = DuplicatePhones.Build(studio);
Console.WriteLine($"{StudioAuth.Tenant + " (الاستوديو)",-24} يُعادُ كَتبُه={studioPlan.Renames.Count,-4} تُدمَج=0   مُمتَنِعَة={studioPlan.Groups.Count} (حِساباتُ استوديو — لا تُدمَج)");
changes += studioPlan.Renames.Count;

Console.WriteLine(new string('-', 78));
Console.WriteLine($"فُحِصَ: {tenants.Count} مَتجَراً والاستوديو · تَغييرات: {changes}");
if (changes == 0)
{
    Console.WriteLine("لا شَيءَ لِيُغَيَّر.");
    return 0;
}
if (!apply)
{
    Console.WriteLine("عَرضٌ فَقَط — لَم يُكتَب شَيء. قَبلَ التَنفيذِ لَقطَةُ القاعِدَة (‏DEPLOY.md §٤‑د)، ثُمَّ:");
    Console.WriteLine($"  dotnet run scripts/merge-duplicate-phones.cs -- --apply --confirm {changes}");
    return 2;
}
if (confirm != changes)
{
    Console.Error.WriteLine($"تَوَقُّف: --confirm {confirm?.ToString() ?? "(غائِب)"} لا يُطابِقُ {changes} تَغييراً — أَعِد العَرضَ ثُمَّ نَفِّذ بِالعَدَدِ الَّذي طَبَعَه.");
    return 1;
}

var audit = new AuditWriter(store);
var now = DateTime.UtcNow;
int renamed = 0, merged = 0;
foreach (var (slug, plan, ok) in work)
{
    renamed += await DuplicatePhones.RenameAsync<User>(store, slug, plan.Renames, (u, p) => { u.Phone = p; u.UpdatedAt = now; });
    foreach (var before in ok)
    {
        // ‏يُقاسُ مِن جَديدٍ قَبلَ الكِتابَة — ما تَغَيَّرَ بَينَ العَرضِ والتَنفيذِ لا يُكتَبُ عَلى قِياسٍ قَديم.
        var scan = await DuplicatePhones.ScanAsync(conn, slug, before.Group, tables);
        if (!scan.Mergeable)
        {
            Console.WriteLine($"  تَخَطٍّ: {slug} — صارَ فيها مانِعٌ بَينَ العَرضِ والتَنفيذ: {string.Join(" · ", scan.Blocks)}");
            continue;
        }
        var moved = await DuplicatePhones.ApplyAsync(store, slug, scan, now);
        await audit.WriteAsync(slug, null, "merge-duplicate-phones", "user.phone_merge", "User", scan.Group.Survivor.ToString(),
            note: $"losers={string.Join(",", scan.Group.Losers)} listings={scan.Listings.Count} keys={scan.Keys} moved={moved}");
        merged++;
    }
    Console.WriteLine($"OK {slug}: أُعيدَ كَتبُ {plan.Renames.Count} · دُمِجَت {ok.Count}");
}
renamed += await DuplicatePhones.RenameAsync<StudioUser>(store, StudioAuth.Tenant, studioPlan.Renames, (u, p) => u.Phone = p);
Console.WriteLine($"تَمَّ: أُعيدَ كَتبُ {renamed} جَوّالاً، ودُمِجَت {merged} مَجموعَة — مِن {changes} تَغييراً.");
return 0;
