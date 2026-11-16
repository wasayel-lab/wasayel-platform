using ACommerce.Kit.Auth;
using ACommerce.Kit.Files;
using ACommerce.Kit.Listings;
using ACommerce.Kit.Tenants;
using ACommerce.Templates.Customer.Marketplace.Services.Audit;
using ACommerce.Templates.Customer.Marketplace.Services.Export;
using ACommerce.Templates.Customer.Marketplace.Services.Listings;
using Marten;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ صورَةُ مَن حَذَفَ حِسابَه وصُوَرُ إعلاناتِه ومَواقِعُها — تَوصيفٌ قَبلَ التَبديل (‏2026-11-04، الموجَة ١٢ G1 · ٣؛ H-SEC-W12-1 + B-5) ═══════════════
//
// **ما يُثَبَّتُ هُنا لِيَبقى** (القاعِدَة ٣): بابُ الحَذفِ بِجِسمِه يَرُدُّ 302 إلى `?deleted=1`، ويَمحو رابِطَ صورَتِه في وَثيقَتِه، ويَسحَبُ إعلانَه القائِم؛ ولا يَمَسُّ
// مِلَفَّ عُضوٍ آخَرَ ولا صُوَرَ إعلانِه؛ وحَقيبَةُ تَخارُجِ المَتجَرِ تَحمِلُ صورَةَ إعلانِ العُضوِ القائِم. وهذا ما لا يَمَسُّه التَبديل.
// **وما يُطبَعُ ولا يُثَبَّت**: اليَومَ لا يُحذَفُ مِلَفٌّ واحِدٌ مِن المَخزَن (صورَتُه بِمِفتاحِها الحَتمِيِّ `tenants/{slug}/avatars/{id}.{ext}` تَبقى، وشُرَكاءُ مُحادَثَتِه
// يَحمِلونَ رابِطَها، والقِراءَةُ مِن R2 عامَّةٌ عَمداً)، والإعلانُ المَسحوبُ يُبقي صُوَرَه وإحداثِيّاتِ الالتِقاطِ والإنزال (`ListingDeleted` يَضبِطُ `IsDeleted` وَحدَه)،
// وحَقيبَةُ التَخارُجِ تَشحَنُ صُوَرَ كُلِّ إعلانٍ ولو حُذِف — وصَفحَةُ الحَذفِ تَقولُ «صورَتُكَ» تُحذَفُ الآن. فَهذا يَتَغَيَّرُ في كوميتِ التَبديل وصُفوفُه في
// `AccountErasureTests`؛ وهذا المِلَفُّ يَبقى بِبَصمَتِه، ومُساعِداه (`RecordingFileStorage`، `SeedAsync`) يَقرَؤُهُما التَبديلُ ولا يُعيدُ كِتابَتَهُما.

/// <summary>مَخزَنُ مِلَفّاتٍ يُسَجِّلُ ما يُحذَف — وما حُذِفَ لا يُقرَأ بَعدَه، وما سِواه يُقرَأُ بِمُحتَوىً هُوَ مِفتاحُه. و<paramref name="throws"/> يَجعَلُ كُلَّ حَذفٍ يَرمي:
/// مَخزَنٌ لا يُبلَغ.</summary>
internal sealed class RecordingFileStorage(bool throws = false) : IFileStorage
{
    private readonly List<string> _deleted = new();

    public IReadOnlyList<string> Deleted { get { lock (_deleted) return _deleted.ToList(); } }

    public string ProviderName => "recording";

    public Task<StoredFile> UploadAsync(ACommerce.Platform.Shared.Metering.ProviderCall call, string key, Stream content, string contentType, CancellationToken ct = default)
        => throw new NotSupportedException("لا رَفعَ في هذا الصَفّ.");

    public Task<Stream?> ReadAsync(string key, CancellationToken ct = default)
    {
        lock (_deleted)
            return Task.FromResult<Stream?>(_deleted.Contains(key) ? null : new MemoryStream(System.Text.Encoding.UTF8.GetBytes(key)));
    }

    public Task<bool> DeleteAsync(string key, CancellationToken ct = default)
    {
        if (throws) throw new FileStorageException("المَخزَنُ لا يُبلَغ.");
        lock (_deleted) _deleted.Add(key);
        return Task.FromResult(true);
    }

    public Task<bool> ExistsAsync(string key, CancellationToken ct = default) { lock (_deleted) return Task.FromResult(!_deleted.Contains(key)); }

    public Task<string> GetPublicUrlAsync(string key, TimeSpan? expiresIn = null, CancellationToken ct = default) => Task.FromResult("/uploads/" + key);
}

[Collection(LiveProofCollection.Name)]
public class LiveAccountErasureFilesProofTests(ITestOutputHelper output)
{
    private static bool Enabled
        => LiveDealConcurrencyProofTests.Enabled && !string.IsNullOrEmpty(LiveDealConcurrencyProofTests.ConnectionString);

    internal const string DeleteDoor = "/{slug}/me/delete/confirm";

    /// <summary>ما يُبذَرُ لِلصُفوف: هُوَ (صورَةٌ، وإعلانٌ قائِمٌ بِصورَتَينِ — واحِدَةٌ خارِجَ مَخزَنِ المَتجَر — وإحداثِيّاتِ التِقاطٍ وإنزال، وإعلانٌ حَذَفَه قَبلُ بِصورَتِه
    /// وإحداثِيّاتِه)، وعُضوٌ آخَرُ قائِمٌ بِصورَتِه وإعلانِه بِصورَتِه. والمَتجَرُ في الذاكِرَةِ لِلحَقيبَة.</summary>
    internal sealed record Seeded(Tenant Tenant, User Me, User Other, Guid MyListing, Guid MyOldListing, Guid OtherListing)
    {
        public string Slug => Tenant.Id;
        public string MyAvatarKey => $"tenants/{Slug}/avatars/{Me.Id}.png";
        public string OtherAvatarKey => $"tenants/{Slug}/avatars/{Other.Id}.png";
        public string MyPhotoKey => $"tenants/{Slug}/listings/{MyListing}/0.jpg";
        public string MyOldPhotoKey => $"tenants/{Slug}/listings/{MyOldListing}/0.webp";
        public string OtherPhotoKey => $"tenants/{Slug}/listings/{OtherListing}/0.jpg";
        public const string OutsideUrl = "https://cdn.elsewhere.example/shared/cover.jpg";
        public Guid[] Streams => new[] { MyListing, MyOldListing, OtherListing };
    }

    internal static Dictionary<string, string> Located(Guid owner) => new()
    {
        [ListingEditService.OwnerAttribute] = owner.ToString(),
        ["pickup_lat"] = "24.713552", ["pickup_lng"] = "46.675296",
        ["dropoff_lat"] = "24.774265", ["dropoff_lng"] = "46.738586",
        ["seats"] = "3",
    };

    internal static async Task<Seeded> SeedAsync(IDocumentStore store, string slug)
    {
        var now = DateTime.UtcNow;
        var me = new User { Id = Guid.NewGuid(), TenantSlug = slug, FullName = "عُضوٌ يُغلِقُ حِسابَه بِصورَتِه", Phone = "+966500000601", PhoneVerified = true };
        var other = new User { Id = Guid.NewGuid(), TenantSlug = slug, FullName = "عُضوٌ قائِمٌ بِصورَتِه", Phone = "+966500000602", PhoneVerified = true };
        me.AvatarUrl = $"/uploads/tenants/{slug}/avatars/{me.Id}.png";
        other.AvatarUrl = $"/uploads/tenants/{slug}/avatars/{other.Id}.png";
        var (mine, old, others) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        await using var s = store.LightweightSession(slug);
        s.Store(me, other);
        s.Events.StartStream<Listing>(mine, new ListingCreated(mine, slug, "رِحلَتُه القائِمَة", null, 40m, "c", null, null, Located(me.Id), now.AddDays(-3)),
            new ListingMediaSet(mine, new List<string> { $"/uploads/tenants/{slug}/listings/{mine}/0.jpg", Seeded.OutsideUrl }, now.AddDays(-3)));
        s.Events.StartStream<Listing>(old, new ListingCreated(old, slug, "رِحلَتُه الَّتي حَذَفَها", null, 40m, "c", null, null, Located(me.Id), now.AddDays(-9)),
            new ListingMediaSet(old, new List<string> { $"/uploads/tenants/{slug}/listings/{old}/0.webp" }, now.AddDays(-9)),
            new ListingDeleted(old, now.AddDays(-8)));
        s.Events.StartStream<Listing>(others, new ListingCreated(others, slug, "رِحلَةُ غَيرِه", null, 40m, "c", null, null, Located(other.Id), now.AddDays(-2)),
            new ListingMediaSet(others, new List<string> { $"/uploads/tenants/{slug}/listings/{others}/0.jpg" }, now.AddDays(-2)));
        await s.SaveChangesAsync();
        return new Seeded(new Tenant { Id = slug, Name = "مَتجَرُ الصُوَر", OwnerUserId = Guid.NewGuid() }, me, other, mine, old, others);
    }

    internal static async Task CleanAsync(IDocumentStore store, Seeded seeded)
    {
        await using (var s = store.LightweightSession(seeded.Slug))
        {
            s.DeleteWhere<Listing>(l => l.Id != Guid.Empty);
            s.DeleteWhere<ACommerce.Kit.Notifications.Notification>(n => n.Id != Guid.Empty);
            foreach (var id in seeded.Streams) s.Events.ArchiveStream(id);
            await s.SaveChangesAsync();
        }
        await LiveZeroRoleAdminProofTests.CleanAsync(store, seeded.Slug);
    }

    internal static async Task<(int Status, string Location)> DeleteAsync(LiveTemplateDoorHost host, Seeded seeded)
    {
        var token = ACommerce.Kit.Auth.Server.AuthHandlers.MakeToken(seeded.Me.Id, seeded.Slug, seeded.Me.SessionVersion);
        var confirm = ("confirm", ACommerce.Platform.I18n.LocaleCatalog.Text(ACommerce.Platform.I18n.LocaleCatalog.Arabic, "account.delete.confirm_word"));
        return await host.PostFormAsync(DeleteDoor, new Dictionary<string, string> { ["slug"] = seeded.Slug }, seeded.Slug, token, confirm);
    }

    internal static bool CarriesLocation(Listing l) => l.Attributes.Keys.Any(k => k.EndsWith("_lat", StringComparison.Ordinal) || k.EndsWith("_lng", StringComparison.Ordinal));

    /// <summary>بابُ الحَذفِ بِجِسمِه ومَخزَنٍ يُسَجِّل: 302، ورابِطُ صورَتِه مَمحُوّ، وإعلانُه القائِمُ مَسحوب؛ ولا يُحذَفُ مِلَفُّ العُضوِ الآخَرِ ولا تُمَسُّ صُوَرُ إعلانِه.
    /// ويُطبَعُ: ما حُذِفَ مِن المَخزَن، وصُوَرُ إعلانَيهِ وإحداثِيّاتُهُما بَعدَ الحَذف.</summary>
    [Fact]
    public async Task The_door_erases_him_withdraws_his_listing_and_leaves_another_members_files_on_Postgres()
    {
        if (!Enabled) return;
        var storage = new RecordingFileStorage();
        await using var host = LiveTemplateDoorHost.Build(services: s => s.AddSingleton<IFileStorage>(storage));
        var seeded = await SeedAsync(host.Store, "zz-erafile-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            var (status, at) = await DeleteAsync(host, seeded);
            await using var q = host.Store.QuerySession(seeded.Slug);
            var me = (await q.LoadAsync<User>(seeded.Me.Id))!;
            var other = (await q.LoadAsync<User>(seeded.Other.Id))!;
            var mine = (await q.LoadAsync<Listing>(seeded.MyListing))!;
            var old = (await q.LoadAsync<Listing>(seeded.MyOldListing))!;
            var others = (await q.LoadAsync<Listing>(seeded.OtherListing))!;
            output.WriteLine($"البابُ: {status} → {at} · ما حُذِفَ مِن المَخزَن: {storage.Deleted.Count} [{string.Join(", ", storage.Deleted)}] · "
                + $"إعلانُه القائِم: مَحذوف {mine.IsDeleted} · صُوَر {mine.MediaUrls.Count} · إحداثِيّات {CarriesLocation(mine)} · "
                + $"إعلانُه المَحذوفُ قَبلُ: صُوَر {old.MediaUrls.Count} · إحداثِيّات {CarriesLocation(old)} · إعلانُ غَيرِه: صُوَر {others.MediaUrls.Count}");
            Assert.Equal((302, $"/{seeded.Slug}?deleted=1"), (status, at));
            Assert.NotNull(me.DeletedAt);
            Assert.Null(me.AvatarUrl);
            Assert.True(mine.IsDeleted);
            Assert.DoesNotContain(storage.Deleted, k => k.Contains(seeded.Other.Id.ToString(), StringComparison.Ordinal)
                                                        || k.Contains(seeded.OtherListing.ToString(), StringComparison.Ordinal));
            Assert.Equal(seeded.Other.AvatarUrl, other.AvatarUrl);
            Assert.False(others.IsDeleted);
            Assert.Single(others.MediaUrls);
            Assert.True(CarriesLocation(others));
        }
        finally { await CleanAsync(host.Store, seeded); }
    }

    /// <summary>حَقيبَةُ تَخارُجِ المَتجَرِ بَعدَ الحَذفِ بِالبابِ: صورَةُ إعلانِ العُضوِ القائِمِ فيها. ويُطبَعُ: هَل تَشحَنُ صُوَرَ إعلانَيهِ وصورَتَه، وهَل يَحمِلُ صَفّا
    /// إعلانَيهِ إحداثِيّات.</summary>
    [Fact]
    public async Task The_store_export_after_the_door_ships_the_live_members_listing_photo_on_Postgres()
    {
        if (!Enabled) return;
        var storage = new RecordingFileStorage();
        await using var host = LiveTemplateDoorHost.Build(services: s => s.AddSingleton<IFileStorage>(storage));
        var seeded = await SeedAsync(host.Store, "zz-eraexp-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            var (status, _) = await DeleteAsync(host, seeded);
            var content = await new TenantExportService(host.Store, storage, new AuditWriter(host.Store)).CollectAsync(seeded.Tenant, seeded.Tenant.OwnerUserId);
            var keys = content.Files.Select(f => f.Key).ToHashSet(StringComparer.Ordinal);
            var listings = Assert.Single(content.Tables, t => t.TypeName == nameof(Listing));
            Assert.Null(listings.ReadErrorAr);
            string RowOf(Guid id) => listings.Rows.Single(r => r["id"]?.ToString() == id.ToString()).ToJsonString(TenantExportRedaction.Json);
            output.WriteLine($"البابُ: {status} · مِلَفّاتُ الحَقيبَة {keys.Count} · صورَتُه: {keys.Contains(seeded.MyAvatarKey)} · صورَةُ إعلانِه المَسحوب: {keys.Contains(seeded.MyPhotoKey)} · "
                + $"صورَةُ إعلانِه المَحذوفِ قَبلُ: {keys.Contains(seeded.MyOldPhotoKey)} · صورَةُ إعلانِ غَيرِه: {keys.Contains(seeded.OtherPhotoKey)} · "
                + $"إحداثِيّاتٌ في صَفِّ المَسحوب: {RowOf(seeded.MyListing).Contains("pickup_lat", StringComparison.Ordinal)} · "
                + $"في صَفِّ المَحذوفِ قَبلُ: {RowOf(seeded.MyOldListing).Contains("pickup_lat", StringComparison.Ordinal)}");
            Assert.Equal(302, status);
            Assert.Contains(seeded.OtherPhotoKey, keys);
            Assert.Contains(seeded.OtherAvatarKey, keys);
            Assert.Contains("pickup_lat", RowOf(seeded.OtherListing), StringComparison.Ordinal);
        }
        finally { await CleanAsync(host.Store, seeded); }
    }
}
