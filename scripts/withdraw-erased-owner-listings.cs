#!/usr/bin/env dotnet
#:property JsonSerializerIsReflectionEnabledByDefault=true
#:property PublishAot=false
#:project ../libs/templates/ACommerce.Templates.Customer.Marketplace/ACommerce.Templates.Customer.Marketplace.csproj
// ═══════════════════════════════════════════════════════════════════════
//  سَحبُ إعلاناتِ الحِساباتِ الَّتي حُذِفَت قَبلَ أَن يَسحَبَها الحَذف
// ───────────────────────────────────────────────────────────────────────
//  **لِماذا هذا المِلَفّ** (‏2026-10-30، الموجَة ١٢ F2 · ٨؛ «أ» B-PRIV-1): الحَذفُ صارَ يَسحَبُ إعلاناتِ صاحِبِه في مُعامَلَتِه
//  (`AccountErasure.EraseCopiesAsync`، `8357c95`) — وما حُذِفَ قَبلَ ذلك بَقِيَت إعلاناتُه قائِمَةً بِمالِكٍ مَمحُوّ: تُعرَضُ في الواجِهَة،
//  وتُرَدُّ أَبوابُها بِـ`owner_gone` (`CheckoutCart.OwnerGone`)، وعُروضٌ مُعَلَّقَةٌ عَلَيها لا يَقبَلُها أَحَد، وصَفَقاتُ عَرضٍ لَم يَقبَلْها أَحَدٌ
//  تَنتَظِرُ مالِكاً لا يَعود. **وهذا السكربتُ يَحِلُّ مَحَلَّ أَيِّ مُرَشِّحٍ في الواجِهَة**: لا يُخفى الإعلانُ عِندَ العَرض، بَل يُسحَبُ كَما
//  يَسحَبُه الحَذفُ اليَوم — فَتَبقى قاعِدَةٌ واحِدَة.
//
//  **والقَرارُ كُلُّه في `AccountErasure` لا هُنا** (القاعِدَة ٨): `WithdrawalPlanAsync` (إعلاناتُه القائِمَةُ بِـ`Withdraws`، والعُروضُ المُعَلَّقَةُ
//  عَلَيها مِن غَيرِه، و`IncomingUnaccepted` لِلصَفقَة — عِندَ «عَرض» وبِلا طَرَف)، و`AppendWithdrawal` (‏`ListingDeleted` و`OfferRejected`)،
//  و`CloseIncomingAsync` (إلغاءُ الصَفقَةِ بِـ`ClosingCanceller` بَعدَ الحِفظ، بِقِراءَةٍ جَديدَةٍ ومُحاوَلَةٍ ثانِيَة). وهِيَ بِعَينِها ما يُنادِيه بابُ الحَذف.
//  ولا استِثناءَ بِاسمِ مَتجَر (`ashare` كَأَيِّ مَتجَر — قَرارُ المالِك).
//
//  **وما لا يَفعَلُه — يُقال**: لا يُرسِلُ إشعاراً بِجَرَسٍ أَو دَفعٍ لِمُشتَري الصَفقَةِ المُلغاة (`DealNotices` يَحتاجُ مُضيفَ SignalR والدَفعَ الفَوريّ)
//  — يَكتُبُ الإلغاءُ سَطرَه بِسَبَبِ الحَذفِ في صَفحَةِ الصَفقَة، ويُطبَعُ عَدَدُ المُلغى؛ ولا يَمَسُّ المُخَطَّط (`AutoCreate.None`)؛ ولا يَطبَعُ اسماً
//  ولا جَوّالاً ولا عُنواناً — المُعَرِّفاتُ وَحدَها.
//
// ─── الاستِعمال ────────────────────────────────────────────────────────
//   export ConnectionStrings__Postgres="…"        # ولا تُطبَع أَبَداً
//   dotnet run scripts/withdraw-erased-owner-listings.cs                         # عَرضٌ فَقَط
//   dotnet run scripts/withdraw-erased-owner-listings.cs -- --apply --confirm 7
//
//   --apply              يُنَفِّذ. بِدونِه **عَرضُ ما سَيَتَغَيَّرُ فَقَط**.
//   --confirm <N>        عَدَدُ التَغييراتِ كَما طَبَعَه العَرض (إعلاناتٌ + عُروضٌ + صَفَقات). اختِلافُه ⇐ تَوَقُّفٌ بِلا كِتابَة.
//   --backup-dir <path>  مَجَلَّدُ بَيانِ ما سيُمَسّ (‏`backups/`) — يُكتَبُ قَبلَ أَوَّلِ كِتابَة، بِالمُعَرِّفاتِ وإصداراتِها وَحدَها. ولَقطَةُ القاعِدَةِ
//                        قَبلَ التَنفيذ (`marten-schema.cs --snapshot`، ‏DEPLOY.md §٤‑د) هِيَ النُسخَةُ الَّتي تُستَرجَع.
//
// ─── رُموزُ الخُروج ────────────────────────────────────────────────────
//   0 — لا شَيءَ لِيُسحَب، أَو نُفِّذَ بِنَجاح.
//   2 — عَرضٌ وَجَدَ ما يُسحَب (لَم يُكتَب شَيء).
//   1 — خَطَأ، أَو صِفرُ مَتجَرٍ مَفحوص، أَو --confirm لا يُطابِق.
// ═══════════════════════════════════════════════════════════════════════

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ACommerce.Kit.Auth;
using ACommerce.Kit.Listings;
using ACommerce.Kit.Offers;
using ACommerce.Kit.Tenants;
using ACommerce.Templates.Customer.Marketplace.Services.Audit;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using ACommerce.Templates.Customer.Marketplace.Services.Identity;
using JasperFx;
using JasperFx.Events.Projections;
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

// ‏المَخزَنُ كَما يُرَكِّبُه المُضيفُ لِما يَمَسُّه السَحب (‏`HostingExtensions.AddPlatformHost` و`MarketplaceTemplateExtensions`): الإيجارُ المُقتَرِنُ لِلوَثائِقِ
// والأَحداث، و`Tenant` عامّ، و**نُسخَتا `Listing` و`Offer` تُحَدَّثانِ في المُعامَلَة** — فَحَدَثُ السَحبِ يَبلُغُ ما تَقرَؤُه الواجِهَة —
// والصَفقَةُ والحِسابُ بِتَزامُنٍ تَفاؤُلِيّ (`DealsService.RegisterDocument` نَفسُها)، فَإلغاءٌ يُسابِقُ قَبولاً يُرَدُّ ويُعادُ مِن قِراءَةٍ جَديدَة.
using var store = DocumentStore.For(o =>
{
    o.Connection(cs);
    o.DatabaseSchemaName = "platform";
    o.AutoCreateSchemaObjects = AutoCreate.None;   // ← لا يُمَسُّ المُخَطَّط (‏Marten 9 يُعيدُ كِتابَةَ مُخَطَّطِ Marten 8 — CLAUDE.md)
    o.Policies.AllDocumentsAreMultiTenanted();
    o.Events.TenancyStyle = JasperFx.MultiTenancy.TenancyStyle.Conjoined;
    o.Schema.For<Tenant>().SingleTenanted().Identity(x => x.Id);
    o.Schema.For<User>().Identity(x => x.Id).UseOptimisticConcurrency(true);
    o.Projections.Snapshot<Listing>(SnapshotLifecycle.Inline);
    o.Projections.Snapshot<Offer>(SnapshotLifecycle.Inline);
    DealsService.RegisterDocument(o);
});

List<string> tenants;
try
{
    await using var qs = store.QuerySession();
    tenants = (await qs.Query<Tenant>().Select(t => t.Id).ToListAsync()).OrderBy(t => t, StringComparer.Ordinal).ToList();
}
catch (Exception ex)
{
    // ‏قاعِدَةٌ بِلا مُخَطَّطِ المَنصَّة (أَو اتِّصالٌ مَرفوض) لا تُقرَأ — خَطَأٌ بِسَبَبِه لا استِثناءٌ بِلا كَلِمَة.
    Console.Error.WriteLine($"خَطَأ: تَعَذَّرَت قِراءَةُ المَتاجِر ({ex.GetType().Name}: {ex.Message.Split('\n')[0]}).");
    return 1;
}

Console.WriteLine($"القاعِدَة: بَصمَة {fingerprint} · المَتاجِر: {tenants.Count}");
Console.WriteLine($"الوَضع:   {(apply ? "تَنفيذ (--apply)" : "عَرضُ ما سَيَتَغَيَّر — بِلا كِتابَة")}");
Console.WriteLine(new string('-', 78));
if (tenants.Count == 0)
{
    Console.Error.WriteLine("✗ BLIND CHECK: صِفرُ مَتجَرٍ مَفحوص — لا يُقال «لا شَيءَ لِيُسحَب» عَن قاعِدَةٍ لَم تُقرَأ.");
    return 1;
}

// ─── العَرض — قِراءَةٌ لا تَكتُب ─────────────────────────────────────────
var work = new List<(string Slug, Guid Owner, AccountErasure.WithdrawalPlan Plan)>();
var erasedSeen = 0;
foreach (var slug in tenants)
{
    await using var q = store.QuerySession(slug);
    var erased = (await q.Query<User>().Where(u => u.DeletedAt != null).Select(u => u.Id).ToListAsync()).ToList();
    erasedSeen += erased.Count;
    var mine = new List<(Guid Owner, AccountErasure.WithdrawalPlan Plan)>();
    foreach (var id in erased.OrderBy(i => i))
        if (await AccountErasure.WithdrawalPlanAsync(q, id) is { Count: > 0 } plan) mine.Add((id, plan));
    Console.WriteLine($"{slug,-24} حِساباتٌ مَحذوفَة={erased.Count,-4} لَها ما يُسحَب={mine.Count,-3} إعلانات={mine.Sum(m => m.Plan.Listings.Count),-4} "
        + $"عُروضٌ تُرفَض={mine.Sum(m => m.Plan.PendingOffers.Count),-4} صَفَقاتُ عَرضٍ تُلغى={mine.Sum(m => m.Plan.IncomingDeals.Count)}");
    foreach (var (owner, plan) in mine)
        Console.WriteLine($"    # {owner} — إعلانات {plan.Listings.Count} · عُروض {plan.PendingOffers.Count} · صَفَقات {plan.IncomingDeals.Count}");
    work.AddRange(mine.Select(m => (slug, m.Owner, m.Plan)));
}
var changes = work.Sum(w => w.Plan.Count);

Console.WriteLine(new string('-', 78));
Console.WriteLine($"فُحِصَ: {tenants.Count} مَتجَراً · حِساباتٌ مَحذوفَة {erasedSeen} · تَغييرات: {changes}");
if (changes == 0)
{
    Console.WriteLine("لا شَيءَ لِيُسحَب.");
    return 0;
}
if (!apply)
{
    Console.WriteLine("عَرضٌ فَقَط — لَم يُكتَب شَيء. قَبلَ التَنفيذِ لَقطَةُ القاعِدَة (‏DEPLOY.md §٤‑د)، ثُمَّ:");
    Console.WriteLine($"  dotnet run scripts/withdraw-erased-owner-listings.cs -- --apply --confirm {changes}");
    return 2;
}
if (confirm is null || confirm != changes)
{
    Console.Error.WriteLine($"تَوَقُّف: --confirm {confirm?.ToString() ?? "(غائِب)"} لا يُطابِقُ {changes} تَغييراً.");
    Console.Error.WriteLine("أَعِد العَرضَ ثُمَّ نَفِّذ بِالعَدَدِ الَّذي طَبَعَه. لا كِتابَةَ عَلى العَمياء.");
    return 1;
}

// ─── بَيانُ ما سيُمَسّ — قَبلَ أَوَّلِ كِتابَة، بِالمُعَرِّفاتِ وإصداراتِها وَحدَها ─────────
Directory.CreateDirectory(backupDir);
var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
var manifestPath = Path.Combine(backupDir, $"withdraw-erased-owner-listings-{stamp}.json");
var manifest = new List<object>();
foreach (var (slug, owner, plan) in work)
{
    await using var q = store.QuerySession(slug);
    async Task<long?> Version(Guid stream) => (await q.Events.FetchStreamStateAsync(stream))?.Version;
    var listings = new List<object>();
    foreach (var id in plan.Listings) listings.Add(new { id, version = await Version(id) });
    var offers = new List<object>();
    foreach (var id in plan.PendingOffers) offers.Add(new { id, version = await Version(id) });
    var incoming = new List<object>();
    foreach (var id in plan.IncomingDeals)
        if (await q.LoadAsync<Deal>(id) is { } d) incoming.Add(new { id, status = d.Status.ToString(), stage = d.Stage.ToString() });
    manifest.Add(new { store = slug, owner, listings, offers, deals = incoming });
}
await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(new { fingerprint, stamp, changes, items = manifest },
    new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"بَيانُ ما سيُمَسّ: {manifestPath}");
Console.WriteLine();

// ─── التَنفيذ — لِكُلِّ حِسابٍ جَلسَتُه، والسُؤالُ يُعادُ عَلى قِراءَةٍ جَديدَة ─────────────────
var audit = new AuditWriter(store);
var dealsService = new DealsService(store, new ACommerce.Kit.Payments.UnavailablePaymentProvider());
var reason = ACommerce.Platform.I18n.LocaleCatalog.Text(ACommerce.Platform.I18n.LocaleCatalog.Arabic, "account.delete.cancel_reason");
var (doneListings, doneOffers, doneDeals) = (0, 0, 0);
foreach (var (slug, owner, _) in work)
{
    AccountErasure.WithdrawalPlan fresh;
    await using (var s = store.LightweightSession(slug))
    {
        fresh = await AccountErasure.WithdrawalPlanAsync(s, owner);   // ← تَكافُؤ: إعادَةُ التَشغيلِ لا تَجِدُ ما تَسحَبُه
        AccountErasure.AppendWithdrawal(s, fresh, DateTime.UtcNow);
        await s.SaveChangesAsync();
    }
    var cancelled = await AccountErasure.CloseIncomingAsync(dealsService, slug, owner, fresh.IncomingDeals, reason);
    await audit.WriteAsync(slug, null, "withdraw-erased-owner-listings", "listing.withdraw_erased_owner", "User", owner.ToString(),
        note: $"listings={fresh.Listings.Count} offers={fresh.PendingOffers.Count} deals={cancelled.Count}/{fresh.IncomingDeals.Count}");
    Console.WriteLine($"OK {slug} {owner} <- إعلانات {fresh.Listings.Count} · عُروض {fresh.PendingOffers.Count} · صَفَقات {cancelled.Count} مِن {fresh.IncomingDeals.Count}");
    (doneListings, doneOffers, doneDeals) = (doneListings + fresh.Listings.Count, doneOffers + fresh.PendingOffers.Count, doneDeals + cancelled.Count);
}

Console.WriteLine();
Console.WriteLine($"تَمَّ: إعلاناتٌ سُحِبَت {doneListings} · عُروضٌ رُفِضَت {doneOffers} · صَفَقاتٌ أُلغِيَت {doneDeals} (بِلا إشعارِ جَرَسٍ ولا دَفع — سَطرُ الإلغاءِ في صَفحَةِ الصَفقَة).");
return 0;
