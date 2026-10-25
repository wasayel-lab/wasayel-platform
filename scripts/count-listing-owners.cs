#!/usr/bin/env dotnet
#:property JsonSerializerIsReflectionEnabledByDefault=true
#:property PublishAot=false
#:project ../libs/templates/ACommerce.Templates.Customer.Marketplace/ACommerce.Templates.Customer.Marketplace.csproj
// ═══════════════════════════════════════════════════════════════════════
//  عَدّادُ مالِكي الإعلاناتِ المُمَيَّزينَ لِكُلِّ مَتجَر — قِراءَةٌ فَقَط
// ───────────────────────────────────────────────────────────────────────
//  **لِماذا هذا المِلَفّ** (‏ADR-032 §٦‑ج الصَفّ 1، أَداةُ عَتَبَةِ الرُجوعِ في
//  §٦‑ز): «المَتجَرُ المُفرَدُ حالَةٌ مِن المُتَعَدِّد» — مَتجَرٌ كُلُّ إعلاناتِه
//  لِمالِكِه هُوَ المُنتَجُ نَفسُه بِعَدّادِ مالِكي إعلاناتٍ يُساوي واحِداً.
//  ولا عَدّادَ قائِمٌ يَقولُ ذلك: `PlatformStatsQueries.ListingCountsAsync`
//  يَعُدُّ الإعلاناتِ لا مالِكيها، و`grep 'distinct.*owner'` = صِفر. والعَتَبَةُ
//  رَقمٌ يَكتُبُه المالِكُ عَلى عَدٍّ مَقيس — فَهذا العَدّ.
//
//  **ويَطبَعُ لِكُلِّ مُستَأجِر**: الإعلاناتِ غَيرَ المَحذوفَة · المالِكينَ
//  المُمَيَّزينَ في `Attributes["owner_id"]` (ومَن مِنهُم لَه مُستَخدِمٌ قائِمٌ
//  بِدَورٍ فَعّالٍ في المَتجَر) · هَل `Roles` تَحوي `vendor` · هَل لِمالِكِ
//  المَتجَرِ (‏`Tenant.OwnerUserId` — مُستَخدِمُ الاستوديو) مُستَخدِمٌ بِدَورٍ
//  فَعّالٍ في واجِهَةِ مَتجَرِه، وهَل هُوَ بَينَ مالِكي الإعلانات · كِندَ الفِئاتِ
//  المُمَيَّزَ والنَمَطَ المَرسومَ مِنه وسَلَّتَه.
//
//  **والنَمَطُ يُقرَأُ بِدالَّةِ الواجِهَةِ نَفسِها** (‏`PatternProfileResolver.PatternOf`)
//  لا بِنُسخَة: فَمَتجَرُ استوديو وُلِدَ قَبلَ `ADR-034` بِفِئَتَينِ كِندُهُما
//  `commercial` يُطبَعُ هُنا كَما يَراه زائِرُه — `Rental` بِلا سَلَّة — ويُعَدُّ
//  في السَطرِ الأَخيرِ إن قالَ تَحليلُه `marketplace` (بِـ`NormalizePattern` نَفسِها
//  الَّتي يُناديها المَصنَع). ‏`asal-albaha` و`tomoor-qassim` مُسَمَّيانِ بِذلكَ في
//  `scripts/repair-role-floor.cs`؛ وعَدَدُ أَمثالِهِما في الإنتاجِ غَيرُ مَقيسٍ قَبلَ هذا.
//
//  **وعَدَدُ الإعلاناتِ مَقيسٌ بِالعَدّادِ القائِم**: يُنادى
//  `PlatformStatsQueries.ListingCountsAsync` بِعَينِه (القاعِدَة ٨)، ويُقارَنُ بِما
//  قَرَأَه هذا المِلَفّ — فَإن اختَلَفا لِمَتجَرٍ واحِدٍ خَرَجَ بِـ1: أَداةٌ لا تُطابِقُ
//  عَدّادَ الواجِهَةِ لا يوثَقُ بِعَدِّها لِلمالِكين (القاعِدَة ١٠).
//
//  **‏2026-10-25 — والوَضعُ المَوروثُ يُجمَعُ (تَدقيقُ الإطلاق، تَيّارُ W8-3 — brief-P0-7؛ ‏ADR-032 §٦‑د؛ يُلحَقُ ولا
//  يُعادُ حَرفٌ فَوقَ ما سَبَق)**: مَتجَرٌ بِلا أَدوارٍ يُعطي كُلَّ مُسَجَّلٍ كُلَّ صَلاحِيَّة — ومِنها `tenant.manage` الَّتي تَفتَحُ صَفحَةَ
//  مُستَخدِميه بِهَواتِفِهِم — و§٦‑د يَقولُ «يُقاسُ أَوَّلاً كَم مُستَأجِراً بَلَغَ ذلك». فَيُسأَلُ لِكُلِّ مَتجَرٍ **دالَّةُ الصَلاحِيَّةِ نَفسُها**
//  (‏`RolePermissions.Has(t.Roles, activeRole: null, "tenant.manage")` — لا `Roles.Count == 0` مَنسوخاً: يَومَ يُقلَبُ `RoleCatalog.cs:133`
//  يَعُدُّ هذا السَطرُ صِفراً بِلا تَعديل)، ويُعَدُّ مُستَخدِموه القائِمونَ (عَدَداً لا أَسماء) — فَكُلُّهُم يَحمِلُها؛ وفي الخُلاصَةِ سَطرٌ يَجمَعُه:
//  مِنها مِن الاستوديو، وبِمالِكِ استوديو، ومُستَخدِموها. **ولا يَعرِفُ كَيفَ بَلَغَها**: بَذرٌ (`PlatformSeed` يَلِدُ بِلا أَدوار) أَم حِفظٌ مِن
//  `roles/save` — قيدُ التَدقيقِ `tenant.roles_save` يَحمِلُ الجَوابَ ولا يُقرَأُ هُنا (الأَمرُ في «ج» W8-3 · ٢ مِن سِجِلِّ المالِك).
//
// ─── ما لا يَفعَلُه — وذلك مَقصودٌ ومُعلَن ──────────────────────────────
//   ١. **لا يَكتُبُ حَرفاً**، بِالبِنيَةِ لا بِالانضِباط: لا يَفتَحُ إلّا
//      `IQuerySession` — نَوعٌ لا يَملِكُ `Store` ولا `SaveChanges` — و`AutoCreate.None`
//      فَلا يَمَسُّ المُخَطَّط (‏Marten 9 يُعيدُ كِتابَةَ مُخَطَّطِ Marten 8 بِمُجَرَّدِ
//      الإقلاع — ‏CLAUDE.md). واختِبارُ `ListingOwnerCountScriptTests` يَحمَرُّ إن
//      ظَهَرَ فيه بابُ كِتابَة.
//      (‏2026-10-24 — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه: **والقاعِدَةُ تَرفُضُ الكِتابَةَ أَيضاً**
//      — كانَ «لا يَكتُبُ» بِنَوعِ الجَلسَةِ وَحدَه. صارَ الاتِّصالُ يُفتَحُ بِـ
//      `Options=-c default_transaction_read_only=on`، فَكُلُّ مُعامَلَةٍ عَلَيه لِلقِراءَةِ عِندَ
//      Postgres نَفسِه، ويُقرَأُ الإعدادُ مِن الخادِمِ قَبلَ أَوَّلِ عَدٍّ ويُطبَعُ، ويَخرُجُ بِـ1 إن
//      لَم يَكُن `on` — فَوَسيطٌ يُسقِطُ الخِيارَ لا يُشَغِّلُه بِلا حاجِز.)
//   ٢. **لا يَطبَعُ هاتِفاً ولا بَريداً ولا مُعَرِّفَ مُستَخدِم**: مُطابَقَةُ مالِكِ
//      الاستوديو بِمُستَخدِمِ واجِهَتِه تَقرَأُ هاتِفَه أَو بَريدَه المَحفوظَ وتُقارِنُه
//      حَرفاً بِحَرف (‏`Phone`/`Email` كَما يَكتُبُهُما بابا الدُخول)، ويُطبَعُ «نَعَم/لا»
//      وَحدَه. و«لا» هُنا حَدٌّ أَدنى لا نَفيٌ قاطِع: مالِكٌ دَخَلَ واجِهَتَه بِقَناةٍ
//      أُخرى (نَفاذ، أَو هاتِفٍ بِصيغَةٍ غَيرِ صيغَةِ الاستوديو) لا يُطابِق.
//   ٣. **لا يُقَرِّرُ عَتَبَة**: يَعُدّ، والرَقمُ الَّذي يُفتَحُ عِندَه «حارِسُ مَن يَبيعُ
//      هُنا» أَو «وَضعُ المَتجَر» يَكتُبُه المالِك (‏ADR-032 §٦‑ز، القاعِدَة ١٦).
//   ٤. **ولا يُشَغَّلُ عَلى الإنتاج** — عَلى فَرعِ Neon مَنسوخ (‏ADR-032 §٦‑ج‑1). وهُوَ
//      لا يَفتَحُ صَفحَة: `ashare` يُقرَأُ هُنا بِأَداةٍ نَصِّيَّةٍ كَما يَقولُ ‏CLAUDE.md.
//
// ─── الاستِعمال ────────────────────────────────────────────────────────
//   export ConnectionStrings__Postgres="…"        # ولا تُطبَع أَبَداً
//   dotnet run scripts/count-listing-owners.cs
//
//   (‏`#:property PublishAot=false` أَعلاه لَيسَ زينَة: تَطبيقُ المِلَفِّ الواحِدِ في .NET 10
//   يُشَغَّلُ بِافتِراضِ AOT، فَيَرمي أَوَّلُ `Contains` في استِعلامِ Marten
//   `PlatformNotSupportedException: Dynamic code generation is not supported` — مَقيسٌ
//   عِندَ كِتابَتِه بِرَمزِ خُروجِ 134 قَبلَ أَوَّلِ سَطرِ عَدّ.)
//
// ─── رُموزُ الخُروج ────────────────────────────────────────────────────
//   0 — عُدَّ مُستَأجِرٌ واحِدٌ عَلى الأَقَلّ وطُبِعَ ما فُحِص.
//   1 — خَطَأ: سِلسِلَةٌ غائِبَة، أَو اتِّصالٌ أَو استِعلام، أَو **صِفرُ مُستَأجِرٍ
//       مَفحوص** (أَداةٌ عَمياء — القاعِدَة ١٠)، أَو عَدُّ إعلاناتٍ لا يُطابِقُ
//       عَدّادَ `PlatformStatsQueries`.
//       (‏2026-10-24 — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه: «اتِّصالٌ أَو استِعلام» لَم يَكُن يَخرُجُ
//       بِـ1 — Marten يَلُفُّ خَطَأَ Npgsql في `MartenCommandException`، فَكانَ فَرعا الالتِقاطِ
//       لا يَلتَقِطانِ شَيئاً ويَخرُجُ السكربتُ بِـ134 وتَتَبُّعِ مُكَدَّس: مَقيساً عَلى Postgres مَحَلِّيٍّ
//       بِقاعِدَةٍ غائِبَةٍ (3D000) وبِمُخَطَّطٍ فارِغٍ (42P01). صارَ كُلُّ خَطَأِ قاعِدَةٍ يُصَنَّفُ بِـ
//       `DatabaseFailure` — الغِلافُ وما في داخِلِه ورَمزُ SqlState، لا نَصُّ الرِسالَة — ويَخرُجُ بِـ1.
//       وإعدادُ القِراءَةِ إن لَم يَكُن `on` يَخرُجُ بِـ1 أَيضاً.)
// ═══════════════════════════════════════════════════════════════════════

using System.Security.Cryptography;
using System.Text;
using ACommerce.Kit.Auth;
using ACommerce.Kit.Listings;
using ACommerce.Kit.Roles;
using ACommerce.Kit.Tenants;
using ACommerce.Templates.Customer.Marketplace.Gates;
using ACommerce.Templates.Customer.Marketplace.Services.Incubator;
using ACommerce.Templates.Customer.Marketplace.Services.Listings;
using ACommerce.Templates.Customer.Marketplace.Services.Patterns;
using ACommerce.Templates.Customer.Marketplace.Services.Queries;
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

// بَصمَةُ القاعِدَةِ بِعَينِ ما يَطبَعُه `repair-role-floor.cs` — فَيُقابَلُ سَطرا
// الأَداتَينِ عَلى الفَرعِ نَفسِه بِلا كَشفِ حَرفٍ مِن السِلسِلَة.
var fingerprint = Convert.ToHexString(
    SHA256.HashData(Encoding.UTF8.GetBytes(cs))).ToLowerInvariant()[..12];

// ‏القِراءَةُ فَقَط عِندَ Postgres نَفسِه (‏2026-10-24): كُلُّ اتِّصالٍ مِن هذِه السِلسِلَةِ يَبدَأُ بِـ
// `default_transaction_read_only=on`، فَأَيُّ كِتابَةٍ تُرَدُّ 25006 ولَو ظَهَرَ في الكودِ بابُها.
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

// ‏والإعدادُ يُقرَأُ مِن الخادِمِ لا يُفتَرَض — وَسيطٌ (مُجَمِّعُ اتِّصالاتٍ) يُسقِطُ الخِيارَ يُرى هُنا.
string readOnly;
try
{
    await using var probe = new NpgsqlConnection(readOnlyCs);
    await probe.OpenAsync();
    await using var show = new NpgsqlCommand("SHOW default_transaction_read_only", probe);
    readOnly = (await show.ExecuteScalarAsync())?.ToString() ?? "";
}
catch (Exception ex) when (DatabaseFailure(ex) is { } why)
{
    Console.Error.WriteLine($"خَطَأ: تَعَذَّرَ الاتِّصالُ بِالقاعِدَة ({why}).");
    return 1;
}
if (readOnly != "on")
{
    Console.Error.WriteLine($"✗ default_transaction_read_only={readOnly} لا on — الخادِمُ لَم يَأخُذ خِيارَ القِراءَة، فَلا يُشَغَّلُ بِلا حاجِزِه.");
    return 1;
}

using var store = DocumentStore.For(o =>
{
    o.Connection(readOnlyCs);
    o.DatabaseSchemaName = "platform";
    o.AutoCreateSchemaObjects = AutoCreate.None;   // ← لا يُمَسُّ المُخَطَّط
    o.Policies.AllDocumentsAreMultiTenanted();
    o.Schema.For<Tenant>().SingleTenanted().Identity(x => x.Id);
});

List<Tenant> tenants;
try
{
    await using var qs = store.QuerySession();
    tenants = (await qs.Query<Tenant>().ToListAsync()).OrderBy(t => t.Id, StringComparer.Ordinal).ToList();
}
catch (Exception ex) when (DatabaseFailure(ex) is { } why)
{
    Console.Error.WriteLine($"خَطَأ: تَعَذَّرَت قِراءَةُ المَتاجِر ({why}).");
    return 1;
}

Console.WriteLine($"القاعِدَة: بَصمَة {fingerprint} · المَتاجِر: {tenants.Count}");
Console.WriteLine($"الوَضع:   قِراءَةٌ فَقَط — IQuerySession و AutoCreate.None و default_transaction_read_only={readOnly} (مِن الخادِم)");
Console.WriteLine(new string('-', 78));

// ‏حارِسُ العَمى (القاعِدَة ١٠): «صِفرُ مَتجَرٍ بِمالِكٍ واحِد» عَلى صِفرِ مَتجَرٍ
// مَقروء لا يُمَيَّزُ عَن أَداةٍ لَم تَبلُغ القاعِدَة.
if (tenants.Count == 0)
{
    Console.Error.WriteLine("✗ BLIND CHECK: صِفرُ مُستَأجِرٍ مَفحوص — الأَداةُ لَم تَبلُغ وَثائِقَ المَتاجِر، لا القاعِدَةُ فارِغَةُ النَتيجَة.");
    return 1;
}

// ‏العَدّادُ القائِم — يُنادى بِعَينِه، والمُقارَنَةُ بِه بَعدَ القِراءَة.
var existingCounter = await new PlatformStatsQueries(store)
    .ListingCountsAsync(tenants.Select(t => t.Id));

var mismatches = new List<string>();
int totalListings = 0, singleOwner = 0, singleOwnerIsStoreOwner = 0, multiOwner = 0, zeroOwner = 0;
int studioBorn = 0, studioRentalNoCart = 0, studioRentalNoCartAnalysisMarketplace = 0;
var studioRentalNoCartSlugs = new List<string>();
int legacy = 0, legacyStudio = 0, legacyOwned = 0, legacyMembers = 0;
var legacySlugs = new List<string>();

// ‏(‏2026-10-24) وكُلُّ خَطَأِ قاعِدَةٍ داخِلَ الحَلقَةِ يُصَنَّفُ ويَخرُجُ بِـ1 كَما يَعِدُ الرَأس — لا 134 بِتَتَبُّعِ مُكَدَّس.
try
{
    foreach (var t in tenants)
    {
        await using var tq = store.QuerySession(t.Id);

        // ─── الإعلاناتُ غَيرُ المَحذوفَةِ ومالِكوها — `Attributes["owner_id"]` بِحَرفِه ───
        // (‏2026-10-24: والمِفتاحُ يُقرَأُ بِـ`ListingEditService.OwnerAttribute` — الثابِتُ الَّذي يَكتُبُه الإنشاءُ
        // وتُفَلتِرُ بِه «إعلاناتي» — لا بِحَرفٍ ثانٍ يَنجَرِف.)
        List<Listing> listings;
        try
        {
            listings = (await tq.Query<Listing>().Where(l => !l.IsDeleted).ToListAsync()).ToList();
        }
        catch (MartenCommandException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UndefinedTable })
        {
            // ‏لا جَدوَلَ إعلاناتٍ في القاعِدَةِ كُلِّها — صِفرٌ مَقيسٌ لا مُفتَرَض (والعَدّادُ القائِمُ يَتَخَطّى).
            // (‏2026-10-24 — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه: «مَقيسٌ» لَم يَصدُق حَتّى اليَوم — كانَ الفَرعُ يَلتَقِطُ
            // `PostgresException` وMarten يَرمي `MartenCommandException` يَلُفُّها، فَلَم يُبلَغ قَطّ: نُسخَةٌ مَحَلِّيَّةٌ
            // بِلا `mt_doc_listing` خَرَجَت بِـ134 عِندَ هذا الاستِعلام. صارَ يَلتَقِطُ الغِلافَ ويَسأَلُ ما في داخِلِه،
            // وقيسَ عَلى النُسخَةِ نَفسِها: صِفرُ إعلانٍ لِكُلِّ مَتجَرٍ و«تَخَطّاه» عِندَ العَدّادِ القائِم، والخُروجُ 0.)
            listings = new();
        }

        existingCounter.TryGetValue(t.Id, out var counted);
        if (counted != listings.Count)
            mismatches.Add($"{t.Id}: قُرِئَ {listings.Count} والعَدّادُ القائِمُ {counted}");
        totalListings += listings.Count;

        var ownerIds = listings
            .Select(l => l.Attributes.TryGetValue(ListingEditService.OwnerAttribute, out var o) ? o : null)
            .Where(o => !string.IsNullOrWhiteSpace(o))
            .Select(o => o!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var withoutOwner = listings.Count(l => !l.Attributes.TryGetValue(ListingEditService.OwnerAttribute, out var o) || string.IsNullOrWhiteSpace(o));

        // ‏مالِكٌ «قائِمٌ بِدَورٍ فَعّال»: مُستَخدِمٌ في هذا المَتجَرِ بِمُعَرِّفِه، غَيرُ مَحذوف، ودَورُه
        // الفَعّالُ دَورٌ مِن أَدوارِ المَتجَر — والمَتجَرُ بِصِفرِ أَدوارٍ «وَضعٌ مَوروث» لا دَورَ فيه يُسأَل.
        var ownerGuids = ownerIds.Select(o => Guid.TryParse(o, out var g) ? g : Guid.Empty)
                                 .Where(g => g != Guid.Empty).ToList();
        var ownerUsers = ownerGuids.Count == 0
            ? new List<User>()
            : (await tq.Query<User>().Where(u => ownerGuids.Contains(u.Id)).ToListAsync()).ToList();
        var activeOwners = ownerUsers.Count(u => u.DeletedAt is null && HasActiveRole(t, u));

        // ─── مالِكُ المَتجَر: أَلَهُ مُستَخدِمٌ بِدَورٍ فَعّالٍ في واجِهَتِه؟ ──────────────
        string storeOwnerLine;
        var storeOwnerSells = false;
        if (t.OwnerUserId == Guid.Empty)
            storeOwnerLine = "— (بِلا مالِكِ استوديو)";
        else
        {
            await using var sq = store.QuerySession(StudioAuth.Tenant);
            var studioUser = await sq.LoadAsync<StudioUser>(t.OwnerUserId);
            if (studioUser is null)
                storeOwnerLine = "— (مالِكُ الاستوديو غَيرُ مَوجود)";
            else
            {
                var front = new List<User>();
                if (!string.IsNullOrWhiteSpace(studioUser.Phone))
                    front.AddRange(await tq.Query<User>().Where(u => u.Phone == studioUser.Phone).ToListAsync());
                if (!string.IsNullOrWhiteSpace(studioUser.Email))
                    front.AddRange(await tq.Query<User>().Where(u => u.Email == studioUser.Email).ToListAsync());
                var live = front.Where(u => u.DeletedAt is null).DistinctBy(u => u.Id).ToList();
                var active = live.Where(u => HasActiveRole(t, u)).ToList();
                storeOwnerSells = live.Any(u => ownerIds.Contains(u.Id.ToString(), StringComparer.OrdinalIgnoreCase));
                storeOwnerLine = live.Count == 0
                    ? "لا — لا مُستَخدِمَ لَه في واجِهَةِ مَتجَرِه"
                    : active.Count > 0 ? "نَعَم" : "لَه مُستَخدِمٌ بِلا دَورٍ فَعّال";
            }
        }

        // ─── الأَدوار ────────────────────────────────────────────────────────
        var hasVendor = t.Roles.Any(r => r.CatalogSlug == "vendor" || r.Slug == "vendor");
        var sellers = SelfGrantPolicy.SelfGrantable(t.Roles)
            .Where(r => r.Permissions.Contains("listing.create"))
            .Select(r => r.Slug).ToList();

        // ─── الكِندُ والنَمَطُ — بِدالَّةِ الواجِهَة ──────────────────────────────
        var kinds = t.Categories.Select(c => c.Kind ?? "").Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(k => k, StringComparer.Ordinal).ToList();
        var pattern = PatternProfileResolver.PatternOf(t);
        var usesCart = PatternProfileResolver.Profile(pattern).UsesCart;

        var studio = t.SourceAnalysisId is not null;
        string analysisPattern = "";
        if (studio)
        {
            studioBorn++;
            await using var iq = store.QuerySession(FeasibilityAnalysisService.IncubatorTenant);
            var session = await iq.LoadAsync<IncubatorSession>(t.SourceAnalysisId!.Value);
            analysisPattern = TenantFromAnalysisFactory.NormalizePattern(session?.SuggestedPattern);
            if (pattern == AppPattern.Rental && !usesCart)
            {
                studioRentalNoCart++;
                studioRentalNoCartSlugs.Add(t.Id);
                if (analysisPattern == TenantFromAnalysisFactory.FallbackPattern)
                    studioRentalNoCartAnalysisMarketplace++;
            }
        }

        // ─── الوَضعُ المَوروث — بِدالَّةِ الصَلاحِيَّةِ نَفسِها، ومُستَخدِموه عَدَداً (‏2026-10-25، ‏ADR-032 §٦‑د) ──────────
        var legacyMode = RolePermissions.Has(t.Roles, activeRole: null, "tenant.manage");
        int members = 0;
        if (legacyMode)
        {
            legacy++;
            legacySlugs.Add(t.Id);
            if (studio) legacyStudio++;
            if (t.OwnerUserId != Guid.Empty) legacyOwned++;
            members = await tq.Query<User>().Where(u => u.DeletedAt == null).CountAsync();
            legacyMembers += members;
        }

        switch (ownerIds.Count)
        {
            case 0: zeroOwner++; break;
            case 1:
                singleOwner++;
                if (storeOwnerSells) singleOwnerIsStoreOwner++;
                break;
            default: multiOwner++; break;
        }

        Console.WriteLine($"  # {t.Id}");
        Console.WriteLine($"      إعلاناتٌ غَيرُ مَحذوفَة         : {listings.Count}   (PlatformStatsQueries: {(existingCounter.ContainsKey(t.Id) ? counted.ToString() : "تَخَطّاه")})");
        Console.WriteLine($"      مالِكونَ مُمَيَّزونَ (owner_id)  : {ownerIds.Count}   (بِمُستَخدِمٍ قائِمٍ بِدَورٍ فَعّال: {activeOwners} · إعلاناتٌ بِلا owner_id: {withoutOwner})");
        Console.WriteLine($"      vendor في الأَدوار               : {(hasVendor ? "نَعَم" : "لا")}   (أَدوارٌ ذاتِيَّةُ المَنحِ تَحمِلُ listing.create: {(sellers.Count == 0 ? (t.Roles.Count == 0 ? "— وَضعٌ مَوروث" : "لا شَيء") : string.Join(", ", sellers))})");
        Console.WriteLine($"      مالِكُ المَتجَرِ في واجِهَتِه     : {storeOwnerLine}{(storeOwnerSells ? " · وهُوَ بَينَ مالِكي الإعلانات" : "")}");
        Console.WriteLine($"      كِندُ الفِئات                    : {(kinds.Count == 0 ? "(لا فِئات)" : string.Join(", ", kinds.Select(k => k.Length == 0 ? "(فارِغ)" : k)))}");
        Console.WriteLine($"      النَمَطُ المَرسوم (PatternOf)    : {pattern}   سَلَّة: {(usesCart ? "نَعَم" : "لا")}");
        Console.WriteLine($"      مِن الاستوديو                    : {(studio ? $"نَعَم — نَمَطُ تَحليلِه بَعدَ NormalizePattern: {analysisPattern}" : "لا")}");
        Console.WriteLine($"      وَضعٌ مَوروث (بِلا أَدوار)        : {(legacyMode ? $"نَعَم — مُستَخدِمونَ قائِمونَ يَحمِلُ كُلٌّ مِنهُم tenant.manage: {members}" : "لا")}");
    }
}
catch (Exception ex) when (DatabaseFailure(ex) is { } why)
{
    Console.Error.WriteLine($"خَطَأ: تَعَذَّرَت قِراءَةُ مَتجَر ({why}).");
    return 1;
}

Console.WriteLine(new string('-', 78));
Console.WriteLine($"فُحِصَ: {tenants.Count} مَتجَراً · {totalListings} إعلاناً غَيرَ مَحذوف");
Console.WriteLine($"بِمالِكِ إعلانٍ واحِد: {singleOwner} (مِنها مالِكُ المَتجَرِ هُوَ ذلكَ البائِع: {singleOwnerIsStoreOwner}) · بِأَكثَرَ مِن مالِك: {multiOwner} · بِلا مالِكٍ مَقروء: {zeroOwner}");
Console.WriteLine($"مِن الاستوديو: {studioBorn} · مِنها مَرسومٌ Rental بِلا سَلَّة: {studioRentalNoCart} (ونَمَطُ تَحليلِه marketplace — وُلِدَ قَبلَ ADR-034: {studioRentalNoCartAnalysisMarketplace}){(studioRentalNoCartSlugs.Count == 0 ? "" : " ← " + string.Join(", ", studioRentalNoCartSlugs))}");
Console.WriteLine($"بِلا أَدوار (وَضعٌ مَوروث — كُلُّ مُسَجَّلٍ يَحمِلُ tenant.manage): {legacy} · مِنها مِن الاستوديو: {legacyStudio} · بِمالِكِ استوديو: {legacyOwned} · مُستَخدِمونَ قائِمونَ فيها: {legacyMembers}{(legacySlugs.Count == 0 ? "" : " ← " + string.Join(", ", legacySlugs))}");

if (mismatches.Count > 0)
{
    Console.Error.WriteLine($"✗ عَدُّ الإعلاناتِ لا يُطابِقُ PlatformStatsQueries في {mismatches.Count} مَتجَراً — لا يوثَقُ بِعَدِّ المالِكين:");
    foreach (var m in mismatches) Console.Error.WriteLine($"  {m}");
    return 1;
}
return 0;

// ─── أَدَواتٌ صَغيرَة ───────────────────────────────────────────────────

// ‏خَطَأُ قاعِدَةٍ مُصَنَّفاً — أَو `null` لِما لَيسَ مِنها فَيَمضي كَما هو. Marten يَلُفُّ خَطَأَ Npgsql في
// `MartenCommandException` (مَقيسٌ: 3D000 و42P01 يَصِلانِ مَلفوفَين)، والاتِّصالُ المُباشِرُ (فَحصُ الإعدادِ أَعلاه)
// يَرمي Npgsql بِلا غِلاف — فَالشَكلانِ يُقرَآن. ويُطبَعُ النَوعُ ورَمزُ SqlState وَحدَهُما: نَصُّ الرِسالَةِ قَد يَحمِلُ
// اسمَ مُضيفٍ أَو مُستَخدِمِ قاعِدَة.
static string? DatabaseFailure(Exception ex) => ex switch
{
    MartenCommandException { InnerException: PostgresException pg } => $"MartenCommandException ← PostgresException {pg.SqlState}",
    MartenCommandException { InnerException: NpgsqlException np } => $"MartenCommandException ← {np.GetType().Name}",
    PostgresException pg => $"PostgresException {pg.SqlState}",
    NpgsqlException np => np.GetType().Name,
    _ => null,
};

// ‏دَورٌ فَعّال: `ActiveRole` غَيرُ فارِغٍ وهُوَ سلاجُ دَورٍ مِن أَدوارِ المَتجَر — بِحَرفِ
// `RolePermissions.Has` الَّتي تَقرَأُ `r.Slug == activeRole`. والوَضعُ المَوروثُ (صِفرُ أَدوار)
// لا دَورَ فيه يُسأَل، فَيُعَدُّ فيه كُلُّ مُستَخدِمٍ قائِمٍ فَعّالاً كَما تُعامِلُه الدالَّةُ نَفسُها.
static bool HasActiveRole(Tenant t, User u)
    => t.Roles.Count == 0
    || (!string.IsNullOrEmpty(u.ActiveRole) && t.Roles.Any(r => r.Slug == u.ActiveRole));
