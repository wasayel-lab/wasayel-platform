#!/usr/bin/env dotnet
#:property JsonSerializerIsReflectionEnabledByDefault=true
#:property PublishAot=false
#:project ../libs/templates/ACommerce.Templates.Customer.Marketplace/ACommerce.Templates.Customer.Marketplace.csproj
// ═══════════════════════════════════════════════════════════════════════
//  عَدُّ الحِساباتِ المُكَرَّرَةِ بِصيغَتَي الجَوّال — قِراءَةٌ فَقَط
// ───────────────────────────────────────────────────────────────────────
//  **لِماذا هذا المِلَفّ** (‏2026-10-29، قَرارُ المالِك د57 (٣) «أ»؛ ‏ADR-044 §٤): «الخُطوَةُ
//  الأولى عَدٌّ لِلقِراءَةِ فَقَط: كَم شَخصاً لَه حِسابان». قَبلَ `c5325e4` كانَ كُلُّ دُخولٍ
//  يَكتُبُ الجَوّالَ كَما كُتِب، فَـ`05…` و`+966…` لِرَقَمٍ واحِدٍ حِسابان.
//
//  **ويَطبَعُ لِكُلِّ مَتجَرٍ — `ashare` مِنها كَأَيِّ مَتجَر — ولِلاستوديو**: الحِساباتِ ذاتَ
//  الجَوّال · كَم مِنها يُعادُ كَتبُ جَوّالِه مُوَحَّداً (مُفرَدٌ لَيسَ بِصيغَتِه الدَولِيَّة) ·
//  المَجموعاتِ (رَقَمٌ واحِدٌ بِأَكثَرَ مِن حِساب) وكَم حِساباً يَندَمِج · وكَم مَجموعَةً يَدمِجُها
//  `merge-duplicate-phones.cs` وكَم يَمتَنِعُ عَنها ولِماذا (بِاسمِ الجَدوَلِ لا بِالصَفّ)، وكَم مِفتاحَ
//  رَبطٍ بِاسمِ حِسابٍ يَندَمِجُ يَصيرُ فاعِلُه الباقي (وَثيقَةٌ عامَّة لا يَراها مَسحُ الجَداوِل).
//  والقِياسُ دالَّةُ الدَمجِ بِعَينِها (`DuplicatePhones.Build` و`ScanAsync`) — لا نُسخَة.
//  ‏(‏2026-10-29، تَدقيقُ الموجَة ١١ G-4) **وكَم مَجموعَةً تَختَلِفُ حِساباتُها** في الدَورِ والاسمِ الحَقيقيِّ واسمِ النَشاط
//  (`DuplicatePhones.Differences` — أَعلامٌ لا قيَم)، فَيَعرِفُ صاحِبُ القَرارِ ما يَرِثُه الباقي (`Inherit`) قَبلَ `--apply`؛ وكَم
//  مَجموعَةً يَمنَعُها حِسابٌ إداريّ (`tenant_admin`).
//  ‏(‏2026-10-30، الموجَة ١٢ F1 البَند ١١ — A-SESS-3) **ونَوعَ كُلِّ مانِعٍ بِجانِبِ جَدوَلِه**: كانَ «مانِعٌ في {جَدوَل}» سَطراً واحِداً لِسَبعَةِ
//  أَنواعٍ لا يَرفَعُها بابٌ واحِد، فَصارَ «مانِعٌ في {جَدوَل} · {نَوع}» بِرَمزٍ مِن مَعجَمٍ مُغلَق (`DuplicatePhones.BlockKinds` و`KindOf`)، ويُطبَعُ المَعجَمُ
//  كُلُّه بِما يُرفَعُ اليَومَ وما لا يُرفَع، ثُمَّ أَمرُ العَرضِ (`merge-duplicate-phones.cs` بِلا `--apply`) الَّذي يُسَمّي كُلَّ مَجموعَةٍ بِمانِعِها.
//
// ─── ما لا يَفعَلُه — وذلك مَقصودٌ ومُعلَن ──────────────────────────────
//   ١. **لا يَكتُبُ حَرفاً**: جَلَساتُ قِراءَةٍ وَحدَها، و`AutoCreate.None`، والاتِّصالُ بِـ
//      `default_transaction_read_only=on` يُقرَأُ مِن الخادِمِ ويَخرُجُ بِـ1 إن لَم يَكُن `on`.
//      ويَحمَرُّ `DuplicatePhonesTests.The_count_script_writes_nothing_…` إن ظَهَرَ فيه بابُ كِتابَة.
//   ٢. **لا يَطبَعُ جَوّالاً ولا بَريداً ولا مُعَرِّفَ حِساب** — أَعداداً وأَسماءَ جَداوِلَ ومَتاجِر.
//   ٣. **ولا يُشَغَّلُ عَلى الإنتاجِ قَبلَ لَقطَتِه**: عَلى فَرعٍ مَنسوخٍ أَو بَعدَ
//      `marten-schema.cs --snapshot` (‏DEPLOY.md §٤‑د) — والأَمرُ في سِجِلِّ المالِك «ج» د57.
//
// ─── الاستِعمال ────────────────────────────────────────────────────────
//   export ConnectionStrings__Postgres="…"        # ولا تُطبَع أَبَداً
//   dotnet run scripts/count-duplicate-phones.cs
//
// ─── رُموزُ الخُروج ────────────────────────────────────────────────────
//   0 — عُدَّ مَتجَرٌ واحِدٌ عَلى الأَقَلّ وطُبِعَ ما فُحِص.
//   1 — خَطَأ: سِلسِلَةٌ غائِبَة، أَو اتِّصالٌ أَو استِعلامٌ أَو وَثيقَةٌ لا تُقرَأ، أَو إعدادُ القِراءَةِ لَيسَ `on`،
//       أَو **صِفرُ مَتجَرٍ مَفحوص** (أَداةٌ عَمياء — القاعِدَة ١٠).
// ═══════════════════════════════════════════════════════════════════════

using System.Security.Cryptography;
using System.Text;
using ACommerce.Kit.Auth;
using ACommerce.Kit.Tenants;
using ACommerce.Templates.Customer.Marketplace.Services.Identity;
using ACommerce.Templates.Customer.Marketplace.Services.Incubator;
using JasperFx;
using Marten;
using Marten.Exceptions;
using Npgsql;

var cs = Environment.GetEnvironmentVariable("ConnectionStrings__Postgres");
if (string.IsNullOrWhiteSpace(cs))
{
    Console.Error.WriteLine("خَطَأ: ‏ConnectionStrings__Postgres غَير مَضبوط.");
    return 1;
}

var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(cs))).ToLowerInvariant()[..12];

const string ReadOnlyOption = "-c default_transaction_read_only=on";
string readOnlyCs;
try
{
    var csb = new NpgsqlConnectionStringBuilder(cs);
    csb.Options = string.IsNullOrWhiteSpace(csb.Options) ? ReadOnlyOption : $"{csb.Options} {ReadOnlyOption}";
    readOnlyCs = csb.ConnectionString;
}
catch (ArgumentException)
{
    Console.Error.WriteLine("خَطَأ: ‏ConnectionStrings__Postgres لا يُقرَأ سِلسِلَةَ اتِّصال.");
    return 1;
}

try
{
    await using var conn = new NpgsqlConnection(readOnlyCs);
    await conn.OpenAsync();
    await using (var show = new NpgsqlCommand("SHOW default_transaction_read_only", conn))
    {
        var readOnly = (await show.ExecuteScalarAsync())?.ToString() ?? "";
        if (readOnly != "on")
        {
            Console.Error.WriteLine($"✗ default_transaction_read_only={readOnly} لا on — الخادِمُ لَم يَأخُذ خِيارَ القِراءَة، فَلا يُشَغَّلُ بِلا حاجِزِه.");
            return 1;
        }
    }

    // ‏مَخزَنٌ لِلقِراءَة: ما تَسأَلُه الجَلَساتُ (المُستَخدِمون، والمَتاجِر، وحِساباتُ الاستوديو) — والمَسحُ بِـSQL عَلى الاتِّصالِ نَفسِه.
    using var store = DocumentStore.For(o =>
    {
        o.Connection(readOnlyCs);
        o.DatabaseSchemaName = "platform";
        o.AutoCreateSchemaObjects = AutoCreate.None;   // ← لا يُمَسُّ المُخَطَّط
        o.Policies.AllDocumentsAreMultiTenanted();
        o.Schema.For<Tenant>().SingleTenanted().Identity(x => x.Id);
    });
    List<string> tenants;
    await using (var qs = store.QuerySession())
        tenants = (await qs.Query<Tenant>().Select(t => t.Id).ToListAsync()).OrderBy(t => t, StringComparer.Ordinal).ToList();

    Console.WriteLine($"القاعِدَة: بَصمَة {fingerprint} · المَتاجِر: {tenants.Count}");
    Console.WriteLine("الوَضع:   قِراءَةٌ فَقَط — QuerySession و AutoCreate.None و default_transaction_read_only=on (مِن الخادِم)");
    Console.WriteLine(new string('-', 78));

    if (tenants.Count == 0)
    {
        Console.Error.WriteLine("✗ BLIND CHECK: صِفرُ مَتجَرٍ مَفحوص — الأَداةُ لَم تَبلُغ وَثائِقَ المَتاجِر، لا القاعِدَةُ بِلا تَكرار.");
        return 1;
    }

    var tables = await DuplicatePhones.TenantTablesAsync(conn);
    int accounts = 0, renames = 0, groups = 0, losers = 0, mergeable = 0, blocked = 0, keys = 0, roleDiff = 0, nameDiff = 0, businessDiff = 0;
    var reasons = new SortedDictionary<string, int>(StringComparer.Ordinal);
    var kinds = new SortedDictionary<(string Table, string Kind), int>();

    foreach (var slug in tenants)
    {
        List<User> users;
        await using (var q = store.QuerySession(slug))
            users = (await q.Query<User>().ToListAsync()).ToList();
        var members = users.Select(u => new DuplicatePhones.Account(u.Id, u.Phone, u.CreatedAt, u.DeletedAt is not null)).ToList();
        var byId = users.ToDictionary(u => u.Id);
        var plan = DuplicatePhones.Build(members);
        int ok = 0, no = 0, roles = 0, names = 0, businesses = 0;
        foreach (var g in plan.Groups)
        {
            var diff = DuplicatePhones.Differences(byId[g.Survivor], g.Losers.Select(l => byId[l]));
            roles += diff.Role ? 1 : 0; names += diff.RealName ? 1 : 0; businesses += diff.BusinessName ? 1 : 0;
            var scan = await DuplicatePhones.ScanAsync(conn, slug, g, tables);
            if (scan.Mergeable) { ok++; keys += scan.Keys; }
            else
            {
                no++;
                foreach (var b in scan.Blocks)
                {
                    var table = DuplicatePhones.TableOf(b);
                    reasons[table] = reasons.GetValueOrDefault(table) + 1;
                    kinds[(table, DuplicatePhones.KindOf(b))] = kinds.GetValueOrDefault((table, DuplicatePhones.KindOf(b))) + 1;
                }
            }
        }
        Console.WriteLine($"{slug,-24} بِجَوّال={plan.Considered,-5} يُعادُ كَتبُه={plan.Renames.Count,-4} مَجموعات={plan.Groups.Count,-3} "
            + $"تَندَمِج={plan.Groups.Sum(g => g.Losers.Count),-3} تُدمَج={ok,-3} مُمتَنِعَة={no,-3} دَورٌ≠{roles} اسمٌ≠{names} نَشاطٌ≠{businesses}");
        roleDiff += roles; nameDiff += names; businessDiff += businesses;
        accounts += plan.Considered; renames += plan.Renames.Count; groups += plan.Groups.Count;
        losers += plan.Groups.Sum(g => g.Losers.Count); mergeable += ok; blocked += no;
    }

    // ‏حِساباتُ الاستوديو: يُعادُ كَتبُ المُفرَدِ مُوَحَّداً، ومَجموعاتُها تُعَدُّ ولا تُدمَج — حِسابُ الاستوديو يَملِكُ مَتاجِر
    // (‏`Tenant.OwnerUserId` وثائِقُ عامَّةٌ خارِجَ مُستَأجِرِه)، ودَمجُ المالِكَينِ قَرارٌ لَم يُطلَب («أ» د57).
    List<DuplicatePhones.Account> studio;
    await using (var q = store.QuerySession(StudioAuth.Tenant))
        studio = (await q.Query<StudioUser>().ToListAsync())
            .Select(u => new DuplicatePhones.Account(u.Id, u.Phone, u.CreatedAt, false)).ToList();
    var sp = DuplicatePhones.Build(studio);
    Console.WriteLine($"{StudioAuth.Tenant + " (الاستوديو)",-24} بِجَوّال={sp.Considered,-5} يُعادُ كَتبُه={sp.Renames.Count,-4} مَجموعات={sp.Groups.Count,-3} "
        + $"تَندَمِج=0   تُدمَج=0   مُمتَنِعَة={sp.Groups.Count} (حِساباتُ استوديو — لا تُدمَج)");

    Console.WriteLine(new string('-', 78));
    Console.WriteLine($"فُحِصَ: {tenants.Count} مَتجَراً والاستوديو · حِساباتٌ بِجَوّال: {accounts + sp.Considered} · يُعادُ كَتبُ جَوّالِه: {renames + sp.Renames.Count}");
    Console.WriteLine($"أَشخاصٌ بِأَكثَرَ مِن حِساب: {groups} في المَتاجِر ({losers} حِساباً يَندَمِج) و{sp.Groups.Count} في الاستوديو · "
        + $"يَدمِجُها السكربت: {mergeable} · يَمتَنِعُ عَنها: {blocked + sp.Groups.Count}");
    foreach (var ((table, kind), n) in kinds) Console.WriteLine($"  مانِعٌ في {table} · {kind}: {n} مَجموعَة");
    Console.WriteLine($"أَنواعُ المَوانِع — مَعجَمٌ مُغلَق (DuplicatePhones.BlockKinds): {DuplicatePhones.BlockKinds.Count}");
    foreach (var k in DuplicatePhones.BlockKinds)
        Console.WriteLine($"  {k.Code,-24} {kinds.Where(x => x.Key.Kind == k.Code).Sum(x => x.Value),-3} «{k.Phrase}» — "
            + (k.LiftableToday ? "يُرفَعُ اليَوم إن لَم يَكُن في المَجموعَةِ مانِعٌ غَيرُه: اسحَب الإدارَةَ مِن حِساباتِها، ثُمَّ ادمِج، ثُمَّ أَعِد مَنحَها لِلباقي"
                               : "لا يُرفَعُ اليَوم: تَبقى المَجموعَةُ بِلا دَمج — بَلِّغ السَطرَ ولا تَعمَل بِه"));
    var unknown = kinds.Where(x => x.Key.Kind == "unknown").Sum(x => x.Value);
    if (unknown > 0) Console.WriteLine($"  ✗ مانِعٌ خارِجَ المَعجَم: {unknown} — سَطرٌ في ScanAsync بِلا نَوعٍ في BlockKinds، بَلِّغه");
    Console.WriteLine("كُلُّ مَجموعَةٍ بِمانِعِها: dotnet run scripts/merge-duplicate-phones.cs   (عَرضٌ بِلا --apply، لا يَكتُبُ حَرفاً)");
    Console.WriteLine($"مَجموعاتٌ تَختَلِفُ حِساباتُها — في الدَور: {roleDiff} · في الاسمِ الحَقيقيّ: {nameDiff} · في اسمِ النَشاط: {businessDiff} "
        + $"(يَرِثُ الباقي ما لَيسَ لَه ويَبقى لَه ما لَه — DuplicatePhones.Inherit)");
    Console.WriteLine($"مَجموعاتٌ يَمنَعُها حِسابٌ إداريّ ({DuplicatePhones.AdminBlock}): {reasons.GetValueOrDefault(DuplicatePhones.AdminBlock)}");
    Console.WriteLine($"مَفاتيحُ رَبطٍ بِاسمِ حِساباتٍ تَندَمِجُ — يُكتَبُ فاعِلَها الباقي: {keys}");
    Console.WriteLine($"تَغييراتٌ يُنَفِّذُها merge-duplicate-phones.cs: {renames + sp.Renames.Count + mergeable}");
    return 0;
}
catch (Exception ex) when (DatabaseFailure(ex) is { } why)
{
    Console.Error.WriteLine($"خَطَأ: تَعَذَّرَ العَدّ ({why}).");
    return 1;
}
catch (Exception ex)
{
    // ‏وثيقَةٌ لا تُقرَأ (حَقلٌ بِغَيرِ شَكلِه) خَطَأٌ يُسَمّى بِنَوعِه لا بِتَتَبُّعِ مُكَدَّسٍ ورَمزِ 134 — ولا يُطبَعُ نَصُّه (قَد يَحمِلُ قيمَة).
    Console.Error.WriteLine($"خَطَأ: تَعَذَّرَ العَدّ ({ex.GetType().Name}).");
    return 1;
}

static string? DatabaseFailure(Exception ex) => ex switch
{
    MartenCommandException { InnerException: PostgresException pg } => $"MartenCommandException ← PostgresException {pg.SqlState}",
    MartenCommandException { InnerException: NpgsqlException np } => $"MartenCommandException ← {np.GetType().Name}",
    PostgresException pg => $"PostgresException {pg.SqlState}",
    NpgsqlException np => np.GetType().Name,
    _ => null,
};
