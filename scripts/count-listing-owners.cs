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
// ─── ما لا يَفعَلُه — وذلك مَقصودٌ ومُعلَن ──────────────────────────────
//   ١. **لا يَكتُبُ حَرفاً**، بِالبِنيَةِ لا بِالانضِباط: لا يَفتَحُ إلّا
//      `IQuerySession` — نَوعٌ لا يَملِكُ `Store` ولا `SaveChanges` — و`AutoCreate.None`
//      فَلا يَمَسُّ المُخَطَّط (‏Marten 9 يُعيدُ كِتابَةَ مُخَطَّطِ Marten 8 بِمُجَرَّدِ
//      الإقلاع — ‏CLAUDE.md). واختِبارُ `ListingOwnerCountScriptTests` يَحمَرُّ إن
//      ظَهَرَ فيه بابُ كِتابَة.
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
// ═══════════════════════════════════════════════════════════════════════

using System.Security.Cryptography;
using System.Text;
using ACommerce.Kit.Auth;
using ACommerce.Kit.Listings;
using ACommerce.Kit.Roles;
using ACommerce.Kit.Tenants;
using ACommerce.Templates.Customer.Marketplace.Gates;
using ACommerce.Templates.Customer.Marketplace.Services.Incubator;
using ACommerce.Templates.Customer.Marketplace.Services.Patterns;
using ACommerce.Templates.Customer.Marketplace.Services.Queries;
using JasperFx;
using Marten;
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

using var store = DocumentStore.For(o =>
{
    o.Connection(cs);
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
catch (Exception ex) when (ex is NpgsqlException or InvalidOperationException)
{
    Console.Error.WriteLine($"خَطَأ: تَعَذَّرَت قِراءَةُ المَتاجِر ({ex.GetType().Name}).");
    return 1;
}

Console.WriteLine($"القاعِدَة: بَصمَة {fingerprint} · المَتاجِر: {tenants.Count}");
Console.WriteLine("الوَضع:   قِراءَةٌ فَقَط — IQuerySession و AutoCreate.None");
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

foreach (var t in tenants)
{
    await using var tq = store.QuerySession(t.Id);

    // ─── الإعلاناتُ غَيرُ المَحذوفَةِ ومالِكوها — `Attributes["owner_id"]` بِحَرفِه ───
    List<Listing> listings;
    try
    {
        listings = (await tq.Query<Listing>().Where(l => !l.IsDeleted).ToListAsync()).ToList();
    }
    catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UndefinedTable)
    {
        // ‏لا جَدوَلَ إعلاناتٍ في القاعِدَةِ كُلِّها — صِفرٌ مَقيسٌ لا مُفتَرَض (والعَدّادُ القائِمُ يَتَخَطّى).
        listings = new();
    }

    existingCounter.TryGetValue(t.Id, out var counted);
    if (counted != listings.Count)
        mismatches.Add($"{t.Id}: قُرِئَ {listings.Count} والعَدّادُ القائِمُ {counted}");
    totalListings += listings.Count;

    var ownerIds = listings
        .Select(l => l.Attributes.TryGetValue("owner_id", out var o) ? o : null)
        .Where(o => !string.IsNullOrWhiteSpace(o))
        .Select(o => o!.Trim())
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();
    var withoutOwner = listings.Count(l => !l.Attributes.TryGetValue("owner_id", out var o) || string.IsNullOrWhiteSpace(o));

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
}

Console.WriteLine(new string('-', 78));
Console.WriteLine($"فُحِصَ: {tenants.Count} مَتجَراً · {totalListings} إعلاناً غَيرَ مَحذوف");
Console.WriteLine($"بِمالِكِ إعلانٍ واحِد: {singleOwner} (مِنها مالِكُ المَتجَرِ هُوَ ذلكَ البائِع: {singleOwnerIsStoreOwner}) · بِأَكثَرَ مِن مالِك: {multiOwner} · بِلا مالِكٍ مَقروء: {zeroOwner}");
Console.WriteLine($"مِن الاستوديو: {studioBorn} · مِنها مَرسومٌ Rental بِلا سَلَّة: {studioRentalNoCart} (ونَمَطُ تَحليلِه marketplace — وُلِدَ قَبلَ ADR-034: {studioRentalNoCartAnalysisMarketplace}){(studioRentalNoCartSlugs.Count == 0 ? "" : " ← " + string.Join(", ", studioRentalNoCartSlugs))}");

if (mismatches.Count > 0)
{
    Console.Error.WriteLine($"✗ عَدُّ الإعلاناتِ لا يُطابِقُ PlatformStatsQueries في {mismatches.Count} مَتجَراً — لا يوثَقُ بِعَدِّ المالِكين:");
    foreach (var m in mismatches) Console.Error.WriteLine($"  {m}");
    return 1;
}
return 0;

// ─── أَدَواتٌ صَغيرَة ───────────────────────────────────────────────────

// ‏دَورٌ فَعّال: `ActiveRole` غَيرُ فارِغٍ وهُوَ سلاجُ دَورٍ مِن أَدوارِ المَتجَر — بِحَرفِ
// `RolePermissions.Has` الَّتي تَقرَأُ `r.Slug == activeRole`. والوَضعُ المَوروثُ (صِفرُ أَدوار)
// لا دَورَ فيه يُسأَل، فَيُعَدُّ فيه كُلُّ مُستَخدِمٍ قائِمٍ فَعّالاً كَما تُعامِلُه الدالَّةُ نَفسُها.
static bool HasActiveRole(Tenant t, User u)
    => t.Roles.Count == 0
    || (!string.IsNullOrEmpty(u.ActiveRole) && t.Roles.Any(r => r.Slug == u.ActiveRole));
