#!/usr/bin/env dotnet
#:property JsonSerializerIsReflectionEnabledByDefault=true
#:property PublishAot=false
#:project ../libs/templates/ACommerce.Templates.Customer.Marketplace/ACommerce.Templates.Customer.Marketplace.csproj
#:project ../libs/kits/Files/ACommerce.Kit.Files.Providers.S3/ACommerce.Kit.Files.Providers.S3.csproj
// ═══════════════════════════════════════════════════════════════════════
//  إتمامُ حَذفِ الحِساباتِ الَّتي حُذِفَت قَبلَ أَن يَبلُغَ الحَذفُ نُسَخَها
// ───────────────────────────────────────────────────────────────────────
//  **لِماذا هذا المِلَفّ** (‏2026-10-30، الموجَة ١٢ F2 · ٨؛ «أ» B-PRIV-1): الحَذفُ صارَ يَسحَبُ إعلاناتِ صاحِبِه في مُعامَلَتِه
//  (`AccountErasure.EraseCopiesAsync`، `8357c95`) — وما حُذِفَ قَبلَ ذلك بَقِيَت إعلاناتُه قائِمَةً بِمالِكٍ مَمحُوّ: تُعرَضُ في الواجِهَة،
//  وتُرَدُّ أَبوابُها بِـ`owner_gone` (`CheckoutCart.OwnerGone`)، وعُروضٌ مُعَلَّقَةٌ عَلَيها لا يَقبَلُها أَحَد، وصَفَقاتُ عَرضٍ لَم يَقبَلْها أَحَدٌ
//  تَنتَظِرُ مالِكاً لا يَعود. **وهذا السكربتُ يَحِلُّ مَحَلَّ أَيِّ مُرَشِّحٍ في الواجِهَة**: لا يُخفى الإعلانُ عِندَ العَرض، بَل يُسحَبُ كَما
//  يَسحَبُه الحَذفُ اليَوم — فَتَبقى قاعِدَةٌ واحِدَة.
//
//  **‏(‏2026-11-04، الموجَة ١٢ G1 · ١١ — B-1 + B-4 + G-DOCS-4؛ تَوصيفُه `443d914`) وكُلُّ ما يَمحوه الحَذفُ اليَوم، لا الإعلاناتُ وَحدَها**: كُلُّ حَذفٍ في
//  الإنتاجِ حَتّى اليَومِ سَبَقَ مَحوَ النُسَخ (`548ea4d`)، فَبَقِيَ اسمُه عَلى الصَفَقاتِ والمُحادَثاتِ والتَقييماتِ والعُروضِ والتَذاكِرِ والإشعارات. فَلِكُلِّ حِسابٍ
//  مَحذوفٍ — في جَلسَةٍ واحِدَةٍ وحِفظٍ واحِد — `AccountErasure.NamesOnCopiesAsync` (أَسماؤُه كَما بَقِيَت عَلى نُسَخٍ تَحمِلُ مُعَرِّفَه) ثُمَّ
//  `EraseCopiesAsync` نَفسُها بِتِلكَ الأَسماءِ وبِلا وَسيلَتَي اتِّصال: نُسَخُ الاسم، وسَحبُ إعلاناتِه ومَحوُ صُوَرِها ومَواقِعِها، ورَفضُ العُروضِ عَلَيها، وحَذفُ بَحثِه
//  ومُفَضَّلاتِه وصُفوفِه المُستَورَدَةِ ومَدخَلِه عِندَ الناقِل — ثُمَّ `CloseIncomingAsync` بَعدَ الحِفظ، ولِمُشتَري كُلِّ صَفقَةٍ أُغلِقَت إشعارُ جَرَسٍ بِبانِي البابِ نَفسِه
//  (`DealNotices.ClosedByErasureFor`). والعَرضُ هُوَ الدالَّةُ نَفسُها في جَلسَةٍ لا تُحفَظ — فَما يُطبَعُ هُوَ ما يُكتَب. والقَرارُ كُلُّه في `AccountErasure`
//  (القاعِدَة ٨)؛ ولا استِثناءَ بِاسمِ مَتجَر (`ashare` كَأَيِّ مَتجَر — قَرارُ المالِك).
//
//  **وما لا يَفعَلُه — يُقال**: لا دَفعَ فَوريّاً ولا قَرعَ جَرَسٍ لَحظِيّاً (لا مُضيفَ هُنا) — الإشعارُ مُخَزَّنٌ يَراه المُشتَري في «إشعاراتي»؛ ووَسيلَتا الاتِّصالِ
//  مَمحُوَّتانِ مِن قَبل (`Erase`) فَلا تُستَعادان: إشعارُ تَسجيلٍ قَديمٌ يَحمِلُ هاتِفَه أَو بَريدَه لا يُربَطُ بِه (يُطبَعُ ذلك)؛ وعُنوانُ صَفقَةٍ جارِيَةٍ وهاتِفُها يَبقَيانِ
//  لِيُسَلِّمَ البائِع (د52)؛ ولا يَمَسُّ المُخَطَّط (`AutoCreate.None`)؛ ولا يَطبَعُ اسماً ولا جَوّالاً ولا عُنواناً — المُعَرِّفاتُ والأَعدادُ وَحدَها.
//  **والمِلَفّات**: بِمَخزَنٍ مَضبوطٍ (مَفاتيحُ `Files__S3__*` الخَمسَةُ كُلُّها) تُسأَلُ مَفاتيحُه — صورَتُه وصُوَرُ إعلاناتِه (`AccountErasure.FileKeys`) — ويُحذَفُ
//  الموجودُ بَعدَ الحِفظِ بِأَفضَلِ جُهد (`DeleteFilesAsync`)، ويُعَدُّ في `--confirm`؛ وبِلا مَخزَنٍ يُطبَعُ عَدَدُ المَفاتيحِ الَّتي قَد تَبقى، وهِيَ في البَيان.
//  **‏(‏2026-11-05، الموجَة ١٢ G1b — مُراجَعَةُ الإصلاح ١، G1b-R1-3؛ تَوصيفُه `81a8e8e`) والمَخزَنُ شَرطُ التَنفيذِ حينَ في الخُطَّةِ صُوَرُ إعلانات**: المَحوُ يُفرِغُ
//  `MediaUrls` (`ScrubEvents`)، فَتَنفيذٌ بِلا مَخزَنٍ كانَ يَمحو الروابِطَ والمِلَفّاتُ باقِيَةٌ عامَّة، ثُمَّ لا يَراها تَشغيلٌ لاحِق («قَد تَبقى: 3 … لا شَيءَ لِيُفعَل»). فَالمَفاتيحُ
//  مِن تاريخِ مَجرى كُلِّ إعلانٍ لَه (`AccountErasure.FileKeysEverAsync`) لا مِن روابِطِه القائِمَةِ وَحدَها؛ و`--apply` بِلا مَخزَنٍ وفي الخُطَّةِ صورَةُ إعلانٍ يَخرُجُ 1 بِلا كِتابَة؛
//  ولا يُقالُ «لا شَيءَ لِيُفعَل» ومَفاتيحُ لَم تُسأَل — يَخرُجُ العَرضُ 2 ويَطلُبُ ضَبطَ `Files__S3__*`.
//
// ─── الاستِعمال ────────────────────────────────────────────────────────
//   export ConnectionStrings__Postgres="…"        # ولا تُطبَع أَبَداً
//   dotnet run scripts/withdraw-erased-owner-listings.cs                         # عَرضٌ فَقَط
//   dotnet run scripts/withdraw-erased-owner-listings.cs -- --apply --confirm 7
//
//   --apply              يُنَفِّذ. بِدونِه **عَرضُ ما سَيَتَغَيَّرُ فَقَط**.
//   --confirm <N>        عَدَدُ التَغييراتِ كَما طَبَعَه العَرض (`Counts.Changes` لِكُلِّ حِساب، ومِلَفّاتٌ مَوجودَةٌ بِمَخزَنٍ مَضبوط). اختِلافُه ⇐ تَوَقُّفٌ بِلا كِتابَة.
//   --backup-dir <path>  مَجَلَّدُ بَيانِ ما سيُمَسّ (‏`backups/`) — يُكتَبُ قَبلَ أَوَّلِ كِتابَة، بِالمُعَرِّفاتِ وإصداراتِها والأَعدادِ ومَفاتيحِ المِلَفّاتِ وَحدَها.
//                        ولَقطَةُ القاعِدَةِ قَبلَ التَنفيذ (`marten-schema.cs --snapshot`، ‏DEPLOY.md §٤‑د) هِيَ النُسخَةُ الَّتي تُستَرجَع.
//
// ─── رُموزُ الخُروج ────────────────────────────────────────────────────
//   0 — لا شَيءَ لِيُفعَل (والمَخزَنُ سُئِلَ عَن كُلِّ مِفتاح)، أَو نُفِّذَ بِنَجاح.
//   2 — عَرضٌ وَجَدَ ما يُفعَل، أَو مَفاتيحَ لَم تُسأَلْ بِلا مَخزَن (لَم يُكتَب شَيء).
//   1 — خَطَأ، أَو صِفرُ مَتجَرٍ مَفحوص، أَو --confirm لا يُطابِق، أَو --apply بِلا مَخزَنٍ وفي الخُطَّةِ صُوَرُ إعلاناتٍ أَو لا شَيءَ سِوى مَفاتيحَ لَم تُسأَل.
// ═══════════════════════════════════════════════════════════════════════

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ACommerce.Kit.Auth;
using ACommerce.Kit.Files;
using ACommerce.Kit.Files.Providers.S3;
using ACommerce.Kit.Listings;
using ACommerce.Kit.Offers;
using ACommerce.Kit.Tenants;
using ACommerce.Platform.Shared;
using ACommerce.Templates.Customer.Marketplace.Services.Audit;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using ACommerce.Templates.Customer.Marketplace.Services.Identity;
using JasperFx;
using JasperFx.Events.Projections;
using Marten;
using Microsoft.Extensions.Logging.Abstractions;

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

// ‏المَخزَنُ كَما يُرَكِّبُه المُضيفُ لِما يَمَسُّه المَحو (‏`HostingExtensions.AddPlatformHost` و`MarketplaceTemplateExtensions`): الإيجارُ المُقتَرِنُ لِلوَثائِقِ
// والأَحداث، و`Tenant` و`ApiKeyDocument` عامّانِ، و**نُسَخُ `Listing` و`Offer` و`Ticket` تُحَدَّثُ في المُعامَلَة** — فَحَدَثُ السَحبِ والتَسمِيَةِ يَبلُغُ ما تَقرَؤُه
// الشاشات — والصَفقَةُ والحِسابُ بِتَزامُنٍ تَفاؤُلِيّ (`DealsService.RegisterDocument` نَفسُها)، فَإلغاءٌ يُسابِقُ قَبولاً يُرَدُّ ويُعادُ مِن قِراءَةٍ جَديدَة.
using var store = DocumentStore.For(o =>
{
    o.Connection(cs);
    o.DatabaseSchemaName = "platform";
    o.AutoCreateSchemaObjects = AutoCreate.None;   // ← لا يُمَسُّ المُخَطَّط (‏Marten 9 يُعيدُ كِتابَةَ مُخَطَّطِ Marten 8 — CLAUDE.md)
    o.Policies.AllDocumentsAreMultiTenanted();
    o.Events.TenancyStyle = JasperFx.MultiTenancy.TenancyStyle.Conjoined;
    o.Schema.For<Tenant>().SingleTenanted().Identity(x => x.Id);
    o.Schema.For<User>().Identity(x => x.Id).UseOptimisticConcurrency(true);
    o.Schema.For<ACommerce.Templates.Customer.Marketplace.Services.Api.ApiKeyDocument>().SingleTenanted().Identity(x => x.Id);
    o.Projections.Snapshot<Listing>(SnapshotLifecycle.Inline);
    o.Projections.Snapshot<Offer>(SnapshotLifecycle.Inline);
    o.Projections.Snapshot<ACommerce.Kit.Support.Ticket>(SnapshotLifecycle.Inline);
    DealsService.RegisterDocument(o);
});

// ‏المَخزَنُ إن ضُبِطَت مَفاتيحُه الخَمسَةُ كُلُّها (`FileStorageSelection.ConfigKeys` — لا نُسخَةٌ مِن أَسمائِها) — وإلّا لا مَخزَن، وتُطبَعُ المَفاتيحُ الَّتي قَد تَبقى.
string? Env(string key) => Environment.GetEnvironmentVariable(key.Replace(":", "__"));
IFileStorage? files = FileStorageSelection.ConfigKeys.All(k => !string.IsNullOrWhiteSpace(Env(k)))
    ? new S3FileStorage(Microsoft.Extensions.Options.Options.Create(new S3FileStorageOptions
    {
        Endpoint = Env(FileStorageSelection.EndpointKey)!, Bucket = Env(FileStorageSelection.BucketKey)!,
        AccessKeyId = Env(FileStorageSelection.AccessKeyIdKey)!, SecretAccessKey = Env(FileStorageSelection.SecretAccessKeyKey)!,
        PublicBaseUrl = Env(FileStorageSelection.PublicBaseUrlKey)!,
    }), NullLogger<S3FileStorage>.Instance)
    : null;

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
Console.WriteLine($"المِلَفّات: {(files is null ? "لا مَخزَنَ مَضبوط (Files__S3__* الخَمسَة) — تُطبَعُ المَفاتيحُ الَّتي قَد تَبقى ولا تُحذَف" : "مَخزَنٌ مَضبوط — يُسأَلُ عَن كُلِّ مِفتاح")}");
Console.WriteLine(new string('-', 78));
if (tenants.Count == 0)
{
    Console.Error.WriteLine("✗ BLIND CHECK: صِفرُ مَتجَرٍ مَفحوص — لا يُقال «لا شَيءَ لِيُفعَل» عَن قاعِدَةٍ لَم تُقرَأ.");
    return 1;
}

// ─── العَرض — الدالَّةُ نَفسُها في جَلسَةٍ لا تُحفَظ ─────────────────────────────
async Task<IReadOnlyList<string>> ExistingAsync(IReadOnlyList<string> keys)
{
    if (files is null) return Array.Empty<string>();
    var found = new List<string>();
    foreach (var k in keys)
        try { if (await files.ExistsAsync(k)) found.Add(k); }
        catch (Exception ex) { Console.Error.WriteLine($"    ⚠ تَعَذَّرَ سُؤالُ المَخزَنِ عَن مِفتاح ({ex.GetType().Name}) — يُعَدُّ باقِياً."); found.Add(k); }
    return found;
}

var work = new List<(string Slug, Guid Owner, AccountErasure.Counts Counts, AccountErasure.ErasedFiles Keys, int Photos, IReadOnlyList<string> Files)>();
var (erasedSeen, keysUnchecked, photosUnchecked, personRows) = (0, 0, 0, 0);
foreach (var slug in tenants)
{
    List<Guid> erased;
    int storePersonRows;
    await using (var q = store.QuerySession(slug))
    {
        erased = (await q.Query<User>().Where(u => u.DeletedAt != null).Select(u => u.Id).ToListAsync()).ToList();
        var personTables = AccountErasure.ImportedPersonTables.Keys.ToArray();
        storePersonRows = await q.Query<ImportedRecord>().Where(r => personTables.Contains(r.Table)).CountAsync();
    }
    erasedSeen += erased.Count;
    personRows += storePersonRows;
    var mine = new List<(Guid Owner, AccountErasure.Counts Counts, AccountErasure.ErasedFiles Keys, int Photos, IReadOnlyList<string> Files)>();
    foreach (var id in erased.OrderBy(i => i))
    {
        AccountErasure.Counts counts;
        AccountErasure.ErasedFiles keys;
        await using (var dry = store.LightweightSession(slug))   // ← لا حِفظَ لِهذِه الجَلسَة: ما عَلَّمَه المَحوُ يُرمى مَعَها
        {
            counts = await AccountErasure.EraseCopiesAsync(dry, slug, id, await AccountErasure.NamesOnCopiesAsync(dry, id), Array.Empty<string>());
            // ‏(‏2026-11-05، G1b-R1-3) والمَفاتيحُ مِن تاريخِ مَجاري إعلاناتِه — تَنفيذٌ سابِقٌ بِلا مَخزَنٍ مَحا روابِطَها والمِلَفّاتُ باقِيَة.
            keys = await AccountErasure.FileKeysEverAsync(dry, slug, id);
        }
        var photos = keys.Keys.Except(AccountErasure.AvatarKeys(slug, id), StringComparer.Ordinal).Count();
        var present = await ExistingAsync(keys.Keys);
        if (files is null) (keysUnchecked, photosUnchecked) = (keysUnchecked + keys.Keys.Count, photosUnchecked + photos);
        if (counts.Changes > 0 || present.Count > 0) mine.Add((id, counts, keys, photos, present));
    }
    Console.WriteLine($"{slug,-24} مَحذوفَة={erased.Count,-3} لَها ما يُفعَل={mine.Count,-3} أَسماءٌ تُمحى={mine.Sum(m => m.Counts.Total),-4} إعلانات={mine.Sum(m => m.Counts.Listings),-3} "
        + $"عُروض={mine.Sum(m => m.Counts.RejectedOffers),-3} صَفَقات={mine.Sum(m => m.Counts.IncomingDeals.Count),-3} صُوَرٌ ومَواقِع={mine.Sum(m => m.Counts.Scrubbed),-3} "
        + $"تُحذَف={mine.Sum(m => m.Counts.Removed),-3} مِلَفّات={mine.Sum(m => m.Files.Count),-3} صُفوفُ شَخصٍ مُستَورَدَة={storePersonRows}");
    foreach (var (owner, k, _, _, present) in mine)
        Console.WriteLine($"    # {owner} — أَسماء {k.Total} · إعلانات {k.Listings} · عُروض {k.RejectedOffers} · صَفَقات {k.IncomingDeals.Count} · صُوَرٌ ومَواقِع {k.Scrubbed} · "
            + $"بَحث {k.SavedSearches} · مُفَضَّلَة {k.Favorites} · مُستَورَد {k.ImportedRows} · ناقِل {k.CarrierBindings} · مِلَفّات {present.Count}");
    work.AddRange(mine.Select(m => (slug, m.Owner, m.Counts, m.Keys, m.Photos, m.Files)));
}
var changes = work.Sum(w => w.Counts.Changes + w.Files.Count);

Console.WriteLine(new string('-', 78));
Console.WriteLine($"فُحِصَ: {tenants.Count} مَتجَراً · حِساباتٌ مَحذوفَة {erasedSeen} · تَغييرات: {changes} · صُفوفُ جَداوِلِ الشَخصِ المُستَورَدَةِ في المَتاجِر: {personRows}");
Console.WriteLine("وَسيلَتا الاتِّصال: مَمحُوَّتانِ مِن قَبل (Erase) — إشعارُ تَسجيلٍ قَديمٌ يَحمِلُ هاتِفَ صاحِبِه أَو بَريدَه لا يُربَطُ بِه ولا تُنزَعُ مِنه.");
if (files is null && keysUnchecked > 0)
    Console.WriteLine($"مَفاتيحُ مِلَفّاتٍ قَد تَبقى في المَخزَن: {keysUnchecked} — مِنها صُوَرُ إعلاناتٍ {photosUnchecked} مِن تاريخِ مَجاريها (صورَةُ كُلِّ حِسابٍ مَحذوفٍ وصُوَرُ إعلاناتِه — "
        + "في البَيانِ عِندَ التَنفيذ؛ اضبِط Files__S3__* لِتُسأَلَ وتُحذَف).");
// ‏(‏2026-11-05، G1b-R1-3) وصُوَرُ الإعلاناتِ في الخُطَّة — تَنفيذٌ بِلا مَخزَنٍ يَمحو روابِطَها ومِلَفّاتُها تَبقى عامَّة.
var planPhotos = work.Sum(w => w.Photos);
if (changes == 0 && keysUnchecked == 0)
{
    Console.WriteLine("لا شَيءَ لِيُفعَل.");
    return 0;
}
if (changes == 0)
{
    // ‏(‏2026-11-05، G1b-R1-3) لا تَغييرَ في القاعِدَة، والمَخزَنُ لَم يُسأَل — فَلا يُقالُ «لا شَيءَ لِيُفعَل» عَن مِلَفّاتٍ لَم تُفحَص.
    Console.WriteLine($"لا تَغييرَ في القاعِدَة — ومَفاتيحُ مِلَفّاتٍ لَم تُسأَل {keysUnchecked}: اضبِط Files__S3__* الخَمسَةَ ثُمَّ أَعِد العَرض.");
    return apply ? 1 : 2;
}
if (!apply)
{
    Console.WriteLine("عَرضٌ فَقَط — لَم يُكتَب شَيء.");
    if (files is null && planPhotos > 0)
        Console.WriteLine($"والتَنفيذُ يُرَدُّ بِلا مَخزَن: صُوَرُ إعلاناتٍ {planPhotos} في الخُطَّة تُمحى روابِطُها ومِلَفّاتُها تَبقى عامَّة — اضبِط Files__S3__* الخَمسَةَ ثُمَّ أَعِد العَرضَ "
            + "لِيَطبَعَ عَدَدَ التَأكيد (المِلَفّاتُ الموجودَةُ تُعَدُّ فيه).");
    else
    {
        Console.WriteLine("قَبلَ التَنفيذِ لَقطَةُ القاعِدَة (‏DEPLOY.md §٤‑د)، ثُمَّ:");
        Console.WriteLine($"  dotnet run scripts/withdraw-erased-owner-listings.cs -- --apply --confirm {changes}");
    }
    return 2;
}
if (files is null && planPhotos > 0)
{
    Console.Error.WriteLine($"تَوَقُّف: لا مَخزَنَ مَضبوط (Files__S3__* الخَمسَة) وفي الخُطَّةِ صُوَرُ إعلاناتٍ {planPhotos} — يَمحو التَنفيذُ روابِطَها ومِلَفّاتُها تَبقى عامَّة.");
    Console.Error.WriteLine("اضبِط Files__S3__* الخَمسَةَ (القيَمُ نَفسُها في بيئَةِ التَطبيق) ثُمَّ أَعِد العَرضَ والتَنفيذ. لَم يُكتَب شَيء.");
    return 1;
}
if (confirm is null || confirm != changes)
{
    Console.Error.WriteLine($"تَوَقُّف: --confirm {confirm?.ToString() ?? "(غائِب)"} لا يُطابِقُ {changes} تَغييراً.");
    Console.Error.WriteLine("أَعِد العَرضَ ثُمَّ نَفِّذ بِالعَدَدِ الَّذي طَبَعَه. لا كِتابَةَ عَلى العَمياء.");
    return 1;
}

// ─── بَيانُ ما سيُمَسّ — قَبلَ أَوَّلِ كِتابَة، بِالمُعَرِّفاتِ وإصداراتِها والأَعدادِ والمَفاتيحِ وَحدَها ─────────
Directory.CreateDirectory(backupDir);
var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
var manifestPath = Path.Combine(backupDir, $"withdraw-erased-owner-listings-{stamp}.json");
var manifest = new List<object>();
foreach (var (slug, owner, k, keys, _, present) in work)
{
    await using var q = store.QuerySession(slug);
    async Task<long?> Version(Guid stream) => (await q.Events.FetchStreamStateAsync(stream))?.Version;
    var plan = await AccountErasure.WithdrawalPlanAsync(q, owner);
    var listings = new List<object>();
    foreach (var id in plan.Listings.Concat(plan.Scrubbed.Select(l => l.Id)).Distinct()) listings.Add(new { id, version = await Version(id) });
    var offers = new List<object>();
    foreach (var id in plan.PendingOffers) offers.Add(new { id, version = await Version(id) });
    var incoming = new List<object>();
    foreach (var id in k.IncomingDeals)
        if (await q.LoadAsync<Deal>(id) is { } d) incoming.Add(new { id, status = d.Status.ToString(), stage = d.Stage.ToString() });
    manifest.Add(new
    {
        store = slug, owner, listings, offers, deals = incoming,
        counts = new { renamed = k.Total, k.Listings, k.RejectedOffers, k.Scrubbed, k.SavedSearches, k.Favorites, k.ImportedRows, k.CarrierBindings },
        fileKeys = keys.Keys, filesOutside = keys.Outside, filesPresent = present,   // ‏(‏2026-11-05، G1b-R1-3) بِتاريخِ المَجاري
    });
}
await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(new { fingerprint, stamp, changes, items = manifest },
    new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"بَيانُ ما سيُمَسّ: {manifestPath}");
Console.WriteLine();

// ─── التَنفيذ — لِكُلِّ حِسابٍ جَلسَتُه وحِفظُه، والسُؤالُ يُعادُ عَلى قِراءَةٍ جَديدَة ─────────────────
var audit = new AuditWriter(store);
var dealsService = new DealsService(store, new ACommerce.Kit.Payments.UnavailablePaymentProvider());
var l = new ACommerce.Platform.I18n.L(new Microsoft.AspNetCore.Http.HttpContextAccessor());   // ← لا طَلَب: العَرَبِيَّةُ احتِياطُ القامُوس
var reason = l["account.delete.cancel_reason"];
var (doneChanges, doneDeals, doneNotices, doneFiles, failedFiles) = (0, 0, 0, 0, 0);
foreach (var (slug, owner, _, _, _, present) in work)
{
    AccountErasure.Counts fresh;
    for (var attempt = 1; ; attempt++)
    {
        await using var s = store.LightweightSession(slug);
        var counts = await AccountErasure.EraseCopiesAsync(s, slug, owner, await AccountErasure.NamesOnCopiesAsync(s, owner), Array.Empty<string>());   // ← تَكافُؤ: إعادَةُ التَشغيلِ لا تَجِدُ شَيئاً
        try { await s.SaveChangesAsync(); fresh = counts; break; }
        catch (JasperFx.ConcurrencyException) when (attempt < ACommerce.Kit.Auth.Server.AccountWrites.Attempts) { }
    }
    var cancelled = await AccountErasure.CloseIncomingAsync(dealsService, slug, owner, fresh.IncomingDeals, reason);
    // ‏ولِمُشتَري كُلِّ صَفقَةٍ أُغلِقَت إشعارُ جَرَسٍ مُخَزَّنٌ بِبانِي البابِ نَفسِه — بِلا دَفعٍ فَوريٍّ ولا قَرعٍ لَحظِيّ.
    if (cancelled.Count > 0)
    {
        await using var n = store.LightweightSession(slug);
        foreach (var closed in cancelled) n.Store(DealNotices.ClosedByErasureFor(slug, closed, l, DateTime.UtcNow));
        await n.SaveChangesAsync();
    }
    var (deletedFiles, failed) = files is null ? (0, 0) : await AccountErasure.DeleteFilesAsync(files, present, NullLogger.Instance);
    // ‏سَطرُ التَدقيقِ هَدَفُه المَتجَرُ لا الحِسابُ المَمحُوّ — `HideErasedActors` يُخفي حاشِيَةَ سَطرٍ هَدَفُه حِسابٌ مَحذوف، فَكانَت تُخفى كُلُّها.
    await audit.WriteAsync(slug, null, "withdraw-erased-owner-listings", "listing.withdraw_erased_owner", "tenant", slug,
        note: $"owner={owner} listings={fresh.Listings} offers={fresh.RejectedOffers} deals={cancelled.Count}/{fresh.IncomingDeals.Count} renamed={fresh.Total} "
            + $"scrubbed={fresh.Scrubbed} removed={fresh.Removed} notices={cancelled.Count} files={deletedFiles}");
    Console.WriteLine($"OK {slug} {owner} <- أَسماء {fresh.Total} · إعلانات {fresh.Listings} · عُروض {fresh.RejectedOffers} · صَفَقات {cancelled.Count} مِن {fresh.IncomingDeals.Count} · "
        + $"صُوَرٌ ومَواقِع {fresh.Scrubbed} · حُذِف {fresh.Removed} · إشعارات {cancelled.Count} · مِلَفّات {deletedFiles}{(failed > 0 ? $" (تَعَذَّر {failed})" : "")}");
    (doneChanges, doneDeals, doneNotices, doneFiles, failedFiles) =
        (doneChanges + fresh.Changes, doneDeals + cancelled.Count, doneNotices + cancelled.Count, doneFiles + deletedFiles, failedFiles + failed);
}

Console.WriteLine();
Console.WriteLine($"تَمَّ: تَغييراتٌ في القاعِدَة {doneChanges} · صَفَقاتٌ أُلغِيَت {doneDeals} · إشعاراتُ جَرَسٍ مُخَزَّنَة {doneNotices} · مِلَفّاتٌ حُذِفَت {doneFiles}"
    + (failedFiles > 0 ? $" · تَعَذَّرَ {failedFiles} (أَعِد التَشغيل)" : "") + " — بِلا دَفعٍ فَوريّ.");
return 0;
