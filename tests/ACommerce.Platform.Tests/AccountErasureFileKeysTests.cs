using ACommerce.Kit.Auth;
using ACommerce.Kit.Files;
using ACommerce.Kit.Listings;
using ACommerce.Templates.Customer.Marketplace.Services.Audit;
using ACommerce.Templates.Customer.Marketplace.Services.Export;
using ACommerce.Templates.Customer.Marketplace.Services.Identity;
using ACommerce.Templates.Customer.Marketplace.Services.Listings;
using Marten;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ مِلَفّاتُ مَن حَذَفَ حِسابَه تُحذَف، وإعلاناتُه بِلا صُوَرٍ ولا مَوقِع (‏2026-11-04، الموجَة ١٢ G1 · ٣ — H-SEC-W12-1 + B-5؛ تَوصيفُه 7f2f776) ════════
//
// قَبلَه: صِفرُ مِلَفٍّ يُحذَف، والإعلانُ المَسحوبُ يُبقي صُوَرَه وإحداثِيّاتِه، والحَقيبَةُ تَشحَنُ صُوَرَ كُلِّ إعلانٍ ولو حُذِف (`LiveAccountErasureFilesProofTests`
// يَطبَعُها ويَبقى بِبَصمَتِه). والآن: `AccountErasure.FileKeys` و`ScrubEvents` و`DeleteFilesAsync` نَقِيَّةٌ أَو بِمَخزَنٍ مُمَرَّر — صَفٌّ مُوجَبٌ وسالِبٌ لِكُلٍّ —
// وبابُ الحَذفِ بِجِسمِه عَلى Postgres.

public class AccountErasureFileKeysTests
{
    private const string Slug = "zz";
    private static readonly Guid Me = Guid.NewGuid();
    private static readonly Guid Other = Guid.NewGuid();

    private static Listing ListingOf(Guid owner, Dictionary<string, string> attributes, params string[] media)
    {
        var id = Guid.NewGuid();
        return new Listing
        {
            Id = id, TenantSlug = Slug, Title = "رِحلَة",
            Attributes = new(attributes) { [ListingEditService.OwnerAttribute] = owner.ToString() },
            MediaUrls = media.Select(m => m.Replace("{id}", id.ToString())).ToList(),
        };
    }

    [Theory]
    [InlineData("pickup_lat", true)]
    [InlineData("dropoff_lng", true)]
    [InlineData("lat", true)]
    [InlineData("lng", true)]
    [InlineData("anchor_lat", true)]
    [InlineData("owner_id", false)]
    [InlineData("seats", false)]
    [InlineData("flat", false)]
    [InlineData("pickup_latitude_note", false)]
    public void A_location_attribute_is_lat_lng_or_a_named_lat_lng_and_nothing_else(string key, bool location)
        => Assert.Equal(location, AccountErasure.IsLocationAttribute(key));

    /// <summary><b>المُوجَب</b>: إعلانٌ بِصُوَرٍ وإحداثِيّاتٍ ⇐ حَدَثان — صُوَرٌ فارِغَة، وخَصائِصُ بِلا مَوقِعٍ يَبقى فيها المالِكُ وما سِواه — وتَطبيقُهُما يُفرِغُ الإعلان.
    /// <b>والسالِب</b>: إعلانٌ بِلا صورَةٍ ولا مَوقِعٍ لا حَدَثَ لَه.</summary>
    [Fact]
    public void A_listing_with_photos_and_a_location_gets_an_empty_media_set_and_its_attributes_without_the_location()
    {
        var at = DateTime.UtcNow;
        var l = ListingOf(Me, LiveAccountErasureFilesProofTests.Located(Me), $"/uploads/tenants/{Slug}/listings/{{id}}/0.jpg");
        var events = AccountErasure.ScrubEvents(l, at);
        Assert.Equal(2, events.Count);
        var media = Assert.IsType<ListingMediaSet>(events[0]);
        var edited = Assert.IsType<ListingEdited>(events[1]);
        Assert.Equal((l.Id, 0), (media.Id, media.MediaUrls.Count));
        Assert.Equal(l.Id, edited.Id);
        Assert.Equal(new[] { ListingEditService.OwnerAttribute, "seats" }, edited.Attributes!.Keys.OrderBy(k => k, StringComparer.Ordinal));
        Assert.Null(edited.Title);
        l.Apply(media);
        l.Apply(edited);
        Assert.Empty(l.MediaUrls);
        Assert.DoesNotContain(l.Attributes.Keys, AccountErasure.IsLocationAttribute);
        Assert.Equal(Me.ToString(), l.Attributes[ListingEditService.OwnerAttribute]);

        Assert.Empty(AccountErasure.ScrubEvents(ListingOf(Me, new() { ["seats"] = "2" }), at));
        Assert.IsType<ListingMediaSet>(Assert.Single(AccountErasure.ScrubEvents(ListingOf(Me, new(), "/uploads/x.jpg"), at)));
        Assert.IsType<ListingEdited>(Assert.Single(AccountErasure.ScrubEvents(ListingOf(Me, new() { ["lat"] = "1", ["lng"] = "2" }), at)));
    }

    /// <summary><b>المُوجَب</b>: صورَتُه بِامتِداداتِها الثَلاثَة، وصُوَرُ إعلاناتِه في مَجَلَّدِ كُلٍّ مِنها (رابِطٌ مَحَلِّيٌّ ورابِطُ مَخزَنٍ عامٍّ سَواء). <b>والسالِب</b>: رابِطٌ في مَخزَنٍ
    /// آخَر، ورابِطٌ في مَجَلَّدِ إعلانٍ غَيرِه، ورابِطٌ لِمَتجَرٍ آخَر — تُترَكُ وتُعَدُّ ثَلاثاً؛ ولا مِفتاحَ يَحمِلُ مُعَرِّفَ العُضوِ الآخَر.</summary>
    [Fact]
    public void His_file_keys_are_his_avatar_and_the_photos_inside_each_of_his_listings_folder_and_anything_else_is_counted_and_left()
    {
        var foreign = Guid.NewGuid();
        var mine = ListingOf(Me, new(), $"/uploads/tenants/{Slug}/listings/{{id}}/0.jpg", $"https://files.example.com/tenants/{Slug}/listings/{{id}}/1.png?v=2",
            "https://cdn.elsewhere.example/shared/cover.jpg", $"/uploads/tenants/{Slug}/listings/{foreign}/0.jpg", $"/uploads/tenants/other-store/listings/{{id}}/0.jpg");
        var files = AccountErasure.FileKeys(Slug, Me, new[] { mine });
        Assert.Equal(new[]
        {
            $"tenants/{Slug}/avatars/{Me}.png", $"tenants/{Slug}/avatars/{Me}.jpg", $"tenants/{Slug}/avatars/{Me}.webp",
            $"tenants/{Slug}/listings/{mine.Id}/0.jpg", $"tenants/{Slug}/listings/{mine.Id}/1.png",
        }, files.Keys);
        Assert.Equal(3, files.Outside);
        Assert.DoesNotContain(files.Keys, k => k.Contains(Other.ToString(), StringComparison.Ordinal) || k.Contains(foreign.ToString(), StringComparison.Ordinal));
        Assert.Equal((3, 0), (AccountErasure.FileKeys(Slug, Me, Array.Empty<Listing>()).Keys.Count, AccountErasure.FileKeys(Slug, Me, Array.Empty<Listing>()).Outside));
    }

    /// <summary>شَكلُ مِفتاحِ الصورَةِ مِفتاحُ بابِ تَعديلِ المِلَفِّ بِعَينِه (القاعِدَة ٢): البابُ يَكتُبُ <c>tenants/{slug}/avatars/{userId}.{ext}</c>، وأَنواعُه المَقبولَةُ
    /// ثَلاثَةٌ تُصَيَّرُ امتِداداتِ <see cref="AccountErasure.AvatarExtensions"/> — فَتَغييرُ أَحَدِهِما دونَ الآخَرِ يُحمِرُّ هذا الصَفّ.</summary>
    [Fact]
    public void The_avatar_keys_are_the_ones_the_profile_door_writes()
    {
        var mte = File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, "libs", "templates", "ACommerce.Templates.Customer.Marketplace", "MarketplaceTemplateExtensions.cs"));
        Assert.Contains("var key = $\"tenants/{slug}/avatars/{userId}.{ext}\";", mte, StringComparison.Ordinal);
        Assert.Contains("if (ct is \"image/png\" or \"image/jpeg\" or \"image/webp\" && avatar.Length <= 2 * 1024 * 1024)", mte, StringComparison.Ordinal);
        Assert.Contains("var ext = ct.Split('/')[1].Replace(\"jpeg\", \"jpg\");", mte, StringComparison.Ordinal);
        var produced = new[] { "image/png", "image/jpeg", "image/webp" }.Select(ct => ct.Split('/')[1].Replace("jpeg", "jpg"));
        Assert.Equal(produced, AccountErasure.AvatarExtensions);
    }

    /// <summary><b>بِأَفضَلِ جُهد</b>: مَخزَنٌ يَرمي لا يُسقِطُ الحَذف — كُلُّ مِفتاحٍ يُعَدُّ مُتَعَذِّراً ولا يُرمى شَيء؛ ومَخزَنٌ يَعمَلُ يَحذِفُ كُلَّ مِفتاحٍ مَرَّة.</summary>
    [Fact]
    public async Task Deleting_his_files_counts_what_a_throwing_store_refuses_and_never_throws()
    {
        var keys = new[] { "tenants/zz/avatars/a.png", "tenants/zz/listings/b/0.jpg" };
        var log = Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
        Assert.Equal((0, 2), await AccountErasure.DeleteFilesAsync(new RecordingFileStorage(throws: true), keys, log));
        var working = new RecordingFileStorage();
        Assert.Equal((2, 0), await AccountErasure.DeleteFilesAsync(working, keys, log));
        Assert.Equal(keys, working.Deleted);
    }

    /// <summary>الشَكل: البابُ يَحقِنُ المَخزَنَ ويَحذِفُ المِلَفّاتِ <b>بَعدَ</b> حِفظِ الحَذفِ بِالمَفاتيحِ الَّتي أَعادَها المَحو — لا قَبلَه (مِلَفٌّ يُحذَفُ لِحِسابٍ رُدَّ حَذفُه).</summary>
    [Fact]
    public void The_door_deletes_his_files_after_the_deletion_save_with_the_keys_the_erasure_returned()
    {
        var mte = File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, "libs", "templates", "ACommerce.Templates.Customer.Marketplace", "MarketplaceTemplateExtensions.cs"));
        var start = mte.IndexOf("app.MapPost(\"/{slug}/me/delete/confirm\"", StringComparison.Ordinal);
        var door = mte[start..mte.IndexOf("app.MapGet(\"/{slug}/me/data/download\"", start, StringComparison.Ordinal)];
        var save = door.IndexOf("await session.SaveChangesAsync(); done = erased; break;", StringComparison.Ordinal);
        var delete = door.IndexOf("AccountErasure.DeleteFilesAsync(files, done.Files.Keys,", StringComparison.Ordinal);
        Assert.True(save > 0 && delete > save, $"الحِفظ {save} · الحَذفُ مِن المَخزَن {delete}");
        Assert.Contains("ACommerce.Kit.Files.IFileStorage files", door, StringComparison.Ordinal);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(door, @"DeleteFilesAsync\("));
    }
}

[Collection(LiveProofCollection.Name)]
public class LiveAccountErasureFileKeysProofTests(ITestOutputHelper output)
{
    private static bool Enabled
        => LiveDealConcurrencyProofTests.Enabled && !string.IsNullOrEmpty(LiveDealConcurrencyProofTests.ConnectionString);

    /// <summary>قُلِبَ ما طَبَعَه التَوصيفُ (<c>7f2f776</c>: «ما حُذِفَ مِن المَخزَن: 0 · صُوَر 2 · إحداثِيّات True · المَحذوفُ قَبلُ: صُوَر 1»).
    /// <b>المُوجَب</b>: يُحذَفُ مِن المَخزَنِ صورَتُه بِامتِداداتِها الثَلاثَة وصورَتا إعلانَيه، والرابِطُ الخارِجُ عَن مَجَلَّدِ إعلانِه لا يُمَسّ؛ وإعلاناهُ — المَسحوبُ والمَحذوفُ قَبلُ —
    /// بِلا صُوَرٍ ولا إحداثِيّاتٍ وبِمالِكِهِما. <b>والسالِب</b>: لا مِفتاحَ لِلعُضوِ الآخَرِ ولا لِإعلانِه، وإعلانُه بِصورَتِه وإحداثِيّاتِه.</summary>
    [Fact]
    public async Task The_door_deletes_his_avatar_and_his_listing_photos_and_empties_their_photos_and_locations_on_Postgres()
    {
        if (!Enabled) return;
        var storage = new RecordingFileStorage();
        await using var host = LiveTemplateDoorHost.Build(services: s => s.AddSingleton<IFileStorage>(storage));
        var seeded = await LiveAccountErasureFilesProofTests.SeedAsync(host.Store, "zz-erakeys-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            var (status, at) = await LiveAccountErasureFilesProofTests.DeleteAsync(host, seeded);
            await using var q = host.Store.QuerySession(seeded.Slug);
            var mine = (await q.LoadAsync<Listing>(seeded.MyListing))!;
            var old = (await q.LoadAsync<Listing>(seeded.MyOldListing))!;
            var others = (await q.LoadAsync<Listing>(seeded.OtherListing))!;
            output.WriteLine($"البابُ: {status} → {at} · ما حُذِفَ مِن المَخزَن: {storage.Deleted.Count} · إعلانُه القائِم: مَحذوف {mine.IsDeleted} · صُوَر {mine.MediaUrls.Count} · "
                + $"إحداثِيّات {LiveAccountErasureFilesProofTests.CarriesLocation(mine)} · المَحذوفُ قَبلُ: صُوَر {old.MediaUrls.Count} · إحداثِيّات {LiveAccountErasureFilesProofTests.CarriesLocation(old)} · "
                + $"إعلانُ غَيرِه: صُوَر {others.MediaUrls.Count} · إحداثِيّات {LiveAccountErasureFilesProofTests.CarriesLocation(others)}");
            Assert.Equal((302, $"/{seeded.Slug}?deleted=1"), (status, at));
            Assert.Equal(new[]
            {
                seeded.MyAvatarKey, $"tenants/{seeded.Slug}/avatars/{seeded.Me.Id}.jpg", $"tenants/{seeded.Slug}/avatars/{seeded.Me.Id}.webp",
                seeded.MyPhotoKey, seeded.MyOldPhotoKey,
            }.OrderBy(k => k, StringComparer.Ordinal), storage.Deleted.OrderBy(k => k, StringComparer.Ordinal));
            foreach (var l in new[] { mine, old })
            {
                Assert.True(l.IsDeleted);
                Assert.Empty(l.MediaUrls);
                Assert.False(LiveAccountErasureFilesProofTests.CarriesLocation(l));
                Assert.Equal((seeded.Me.Id.ToString(), "3"), (l.Attributes[ListingEditService.OwnerAttribute], l.Attributes["seats"]));
            }
            Assert.DoesNotContain(storage.Deleted, k => k.Contains(seeded.Other.Id.ToString(), StringComparison.Ordinal) || k.Contains(seeded.OtherListing.ToString(), StringComparison.Ordinal));
            Assert.Single(others.MediaUrls);
            Assert.True(LiveAccountErasureFilesProofTests.CarriesLocation(others));
        }
        finally { await LiveAccountErasureFilesProofTests.CleanAsync(host.Store, seeded); }
    }

    /// <summary><b>مَخزَنٌ لا يُبلَغ</b> (كُلُّ حَذفٍ يَرمي): البابُ يُجيبُ 302 إلى <c>?deleted=1</c>، والحِسابُ مَمحُوٌّ وإعلانُه مَسحوبٌ بِلا صُوَرٍ ولا مَوقِع — ما في القاعِدَةِ وَقَعَ
    /// قَبلَ المَخزَن، والمَخزَنُ لا يُعيدُه.</summary>
    [Fact]
    public async Task A_store_that_cannot_be_reached_still_answers_deleted_and_the_database_part_is_done_on_Postgres()
    {
        if (!Enabled) return;
        await using var host = LiveTemplateDoorHost.Build(services: s => s.AddSingleton<IFileStorage>(new RecordingFileStorage(throws: true)));
        var seeded = await LiveAccountErasureFilesProofTests.SeedAsync(host.Store, "zz-eradown-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            var (status, at) = await LiveAccountErasureFilesProofTests.DeleteAsync(host, seeded);
            await using var q = host.Store.QuerySession(seeded.Slug);
            var me = (await q.LoadAsync<User>(seeded.Me.Id))!;
            var mine = (await q.LoadAsync<Listing>(seeded.MyListing))!;
            output.WriteLine($"مَخزَنٌ يَرمي: {status} → {at} · DeletedAt {(me.DeletedAt is null ? "فارِغ" : "قائِم")} · مَحذوف {mine.IsDeleted} · صُوَر {mine.MediaUrls.Count}");
            Assert.Equal((302, $"/{seeded.Slug}?deleted=1"), (status, at));
            Assert.NotNull(me.DeletedAt);
            Assert.True(mine.IsDeleted);
            Assert.Empty(mine.MediaUrls);
        }
        finally { await LiveAccountErasureFilesProofTests.CleanAsync(host.Store, seeded); }
    }

    /// <summary>قُلِبَ ما طَبَعَه التَوصيف («صورَةُ إعلانِه المَسحوب: True · المَحذوفِ قَبلُ: True · إحداثِيّاتٌ في صَفِّ المَسحوب: True»). <b>المُوجَب</b>: الحَقيبَةُ
    /// بَعدَ الحَذفِ لا تَشحَنُ صورَةً لِإعلانَيه ولا إحداثِيّاتٍ في صَفَّيهِما، ولا صورَةَ إعلانٍ حَذَفَه عُضوٌ قائِمٌ بِنَفسِه (صَفٌّ مَحذوفٌ لا تُقرَأُ صُوَرُه). <b>والسالِب</b>:
    /// صورَةُ إعلانِ العُضوِ القائِمِ وصورَتُه فيها، وصَفُّه بِإحداثِيّاتِه.</summary>
    [Fact]
    public async Task The_store_export_after_the_door_ships_no_photo_or_coordinates_of_his_listings_nor_a_deleted_listings_photo_on_Postgres()
    {
        if (!Enabled) return;
        var storage = new RecordingFileStorage();
        await using var host = LiveTemplateDoorHost.Build(services: s => s.AddSingleton<IFileStorage>(storage));
        var seeded = await LiveAccountErasureFilesProofTests.SeedAsync(host.Store, "zz-eraxkey-" + Guid.NewGuid().ToString("N")[..8]);
        var gone = Guid.NewGuid();
        await using (var s = host.Store.LightweightSession(seeded.Slug))
        {
            s.Events.StartStream<Listing>(gone, new ListingCreated(gone, seeded.Slug, "إعلانٌ حَذَفَه صاحِبُه", null, 40m, "c", null, null,
                    new Dictionary<string, string> { [ListingEditService.OwnerAttribute] = seeded.Other.Id.ToString() }, DateTime.UtcNow.AddDays(-4)),
                new ListingMediaSet(gone, new List<string> { $"/uploads/tenants/{seeded.Slug}/listings/{gone}/0.jpg" }, DateTime.UtcNow.AddDays(-4)),
                new ListingDeleted(gone, DateTime.UtcNow.AddDays(-1)));
            await s.SaveChangesAsync();
        }
        try
        {
            var (status, _) = await LiveAccountErasureFilesProofTests.DeleteAsync(host, seeded);
            // ما حُذِفَ مِن المَخزَنِ لا يُقرَأُ بَعدَه — فَالسُؤالُ هُنا عَمّا تَطلُبُه الحَقيبَة: مَخزَنٌ جَديدٌ يَقرَأُ كُلَّ مِفتاح.
            var content = await new TenantExportService(host.Store, new RecordingFileStorage(), new AuditWriter(host.Store)).CollectAsync(seeded.Tenant, seeded.Tenant.OwnerUserId);
            var keys = content.Files.Select(f => f.Key).ToHashSet(StringComparer.Ordinal);
            var listings = Assert.Single(content.Tables, t => t.TypeName == nameof(Listing));
            string RowOf(Guid id) => listings.Rows.Single(r => r["id"]?.ToString() == id.ToString()).ToJsonString(TenantExportRedaction.Json);
            var goneKey = $"tenants/{seeded.Slug}/listings/{gone}/0.jpg";
            output.WriteLine($"الحَقيبَة: {status} · مِلَفّات {keys.Count} · صورَةُ إعلانِه المَسحوب: {keys.Contains(seeded.MyPhotoKey)} · المَحذوفِ قَبلُ: {keys.Contains(seeded.MyOldPhotoKey)} · "
                + $"إعلانٍ حَذَفَه قائِم: {keys.Contains(goneKey)} · إحداثِيّاتٌ في صَفِّ المَسحوب: {RowOf(seeded.MyListing).Contains("pickup_lat", StringComparison.Ordinal)} · "
                + $"في صَفِّ المَحذوفِ قَبلُ: {RowOf(seeded.MyOldListing).Contains("pickup_lat", StringComparison.Ordinal)} · صورَةُ إعلانِ غَيرِه: {keys.Contains(seeded.OtherPhotoKey)}");
            Assert.Equal(302, status);
            Assert.DoesNotContain(seeded.MyPhotoKey, keys);
            Assert.DoesNotContain(seeded.MyOldPhotoKey, keys);
            Assert.DoesNotContain(goneKey, keys);
            Assert.DoesNotContain(keys, k => k.Contains(seeded.Me.Id.ToString(), StringComparison.Ordinal));
            Assert.DoesNotContain("pickup_lat", RowOf(seeded.MyListing), StringComparison.Ordinal);
            Assert.DoesNotContain("dropoff_lng", RowOf(seeded.MyOldListing), StringComparison.Ordinal);
            Assert.DoesNotContain(LiveAccountErasureFilesProofTests.Seeded.OutsideUrl, RowOf(seeded.MyListing), StringComparison.Ordinal);
            Assert.Contains(seeded.OtherPhotoKey, keys);
            Assert.Contains(seeded.OtherAvatarKey, keys);
            Assert.Contains("pickup_lat", RowOf(seeded.OtherListing), StringComparison.Ordinal);
        }
        finally
        {
            await using (var s = host.Store.LightweightSession(seeded.Slug)) { s.Events.ArchiveStream(gone); await s.SaveChangesAsync(); }
            await LiveAccountErasureFilesProofTests.CleanAsync(host.Store, seeded);
        }
    }
}
