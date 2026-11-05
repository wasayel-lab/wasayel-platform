using ACommerce.Kit.Auth;
using ACommerce.Kit.Listings;
using ACommerce.Templates.Customer.Marketplace.Services.Identity;
using Marten;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ سكربتُ «ما حُذِفَ قَبلَ السَحب» بِلا مَخزَنٍ مَضبوط — تَوصيفٌ قَبلَ التَبديل (‏2026-11-05، الموجَة ١٢ G1b — مُراجَعَةُ الإصلاح ١، G1b-R1-3) ════════════════════
//
// `fa038ee` يُتِمُّ المَحوَ بِدَوالِّ البابِ نَفسِها: `EraseCopiesAsync` تُلحِقُ لِكُلِّ إعلانٍ لَه صورَةٌ `ListingMediaSet(id, [])` (`ScrubEvents`)، و`FileKeys` تَقرَأُ مَفاتيحَ
// الصُوَرِ مِن `MediaUrls` القائِمَةِ وَحدَها. فَتَنفيذٌ بِلا `Files__S3__*` — والطَريقُ المُوَثَّقُ يُسَمّيه «اختِيارِيّاً» — يَمحو الروابِطَ والمِلَفّاتُ باقِيَةٌ عامَّة، ثُمَّ لا يَراها
// تَشغيلٌ لاحِق: مَفاتيحُها في بَيانِ المُشَغِّلِ وَحدَه.
//
// **ما يُثَبَّتُ هُنا لِيَبقى** (القاعِدَة ٣): قَبلَ المَحوِ مَفاتيحُه صورَتُه بِامتِداداتِها الثَلاثَةِ وصورَتا إعلانَيهِ في مَجَلَّدَيهِما، والرابِطُ الخارِجُ عَن مَجَلَّدِه يُعَدُّ ولا يُحذَف؛
// وبَعدَ المَحوِ إعلاناهُ بِلا صُوَر، وتاريخُ مَجرَيَيهِما يَحمِلُ روابِطَ الصُوَرِ كَما رُفِعَت (الأَحداثُ لا تُعَدَّل).
// **وما يُطبَعُ ولا يُثَبَّت**: اليَومَ بَعدَ المَحوِ تَقرَأُ `FileKeys` صورَتَه وَحدَها (صُوَرُ الإعلاناتِ 0)، والسكربتُ يُنَفِّذُ بِلا مَخزَنٍ ولا يَرُدّ، ويَقولُ «لا شَيءَ لِيُفعَل»
// وفي يَدِه مَفاتيحُ لَم تُسأَل، ولا يَقرَأُ التاريخ. وهذا المِلَفُّ يَبقى بِبَصمَتِه.

public class ErasedOwnerListingScriptStorageTests(ITestOutputHelper output)
{
    /// <summary>يُطبَعُ ما يَقولُه السكربتُ اليَومَ بِلا مَخزَن — ويُثَبَّتُ عَقدُه الَّذي لا يَتَغَيَّر: المَخزَنُ بِمَفاتيحِ الإعدادِ الخَمسَةِ لا بِنُسخَة، والعَرضُ قَبلَ التَأكيدِ والبَيانِ والحِفظ.</summary>
    [Fact]
    public void The_script_reads_storage_from_the_five_config_keys_and_shows_before_it_confirms_writes_its_manifest_and_saves()
    {
        var code = WriteEndpointGuardTests.StripComments(ErasedOwnerListingScriptTests.ScriptText());
        int At(string what) => code.IndexOf(what, StringComparison.Ordinal);
        Assert.Contains("FileStorageSelection.ConfigKeys.All(", code, StringComparison.Ordinal);
        var show = At("if (!apply)");
        var gate = At("if (confirm is null || confirm != changes)");
        var manifest = At("File.WriteAllTextAsync(manifestPath");
        var save = At("await s.SaveChangesAsync(); fresh = counts; break;");
        Assert.True(show > 0 && gate > show && manifest > gate && save > manifest, "التَرتيب: العَرضُ ثُمَّ التَأكيدُ ثُمَّ البَيانُ ثُمَّ الحِفظ.");
        var nothing = At("Console.WriteLine(\"لا شَيءَ لِيُفعَل.\");");
        var nothingGate = code.LastIndexOf("if (", nothing, StringComparison.Ordinal);
        output.WriteLine($"يَرُدُّ التَنفيذَ بِلا مَخزَن: {code[show..gate].Contains("files is null", StringComparison.Ordinal) || code[gate..manifest].Contains("files is null", StringComparison.Ordinal)} · "
            + $"«لا شَيءَ لِيُفعَل» يَسأَلُ المَفاتيحَ الَّتي لَم تُسأَل: {code[nothingGate..nothing].Contains("keysUnchecked", StringComparison.Ordinal)} · "
            + $"يَقرَأُ تاريخَ صُوَرِ الإعلانات: {code.Contains("ListingMediaSet", StringComparison.Ordinal) || code.Contains("FileKeysEver", StringComparison.Ordinal)}");
    }
}

[Collection(LiveProofCollection.Name)]
public class LiveErasedOwnerListingScriptStorageProofTests(ITestOutputHelper output)
{
    private static bool Enabled
        => LiveDealConcurrencyProofTests.Enabled && !string.IsNullOrEmpty(LiveDealConcurrencyProofTests.ConnectionString);

    /// <summary>حِسابٌ حُذِفَ قَبلَ مَحوِ النُسَخ: وَثيقَتُه مَمحُوَّةٌ بِـ<c>Erase</c> وَحدَه وإعلاناهُ بِصُوَرِهِما — بَذرَةُ <see cref="LiveAccountErasureFilesProofTests.SeedAsync"/> بِحَرفِها.</summary>
    internal static async Task<LiveAccountErasureFilesProofTests.Seeded> SeedErasedAsync(IDocumentStore store, string slug)
    {
        var seeded = await LiveAccountErasureFilesProofTests.SeedAsync(store, slug);
        await using var s = store.LightweightSession(slug);
        s.Store(AccountDeletion.Erase((await s.LoadAsync<User>(seeded.Me.Id))!, DateTime.UtcNow.AddDays(-1)));
        await s.SaveChangesAsync();
        return seeded;
    }

    /// <summary>ما يَفعَلُه تَنفيذُ السكربتِ بِلا مَخزَنٍ في القاعِدَة: <c>EraseCopiesAsync</c> بِأَسمائِه مِن النُسَخِ وبِلا وَسيلَتَين، ثُمَّ حِفظٌ واحِد — السَطرانِ بِحَرفِهِما في السكربت.</summary>
    internal static async Task<AccountErasure.Counts> ApplyWithoutStorageAsync(IDocumentStore store, LiveAccountErasureFilesProofTests.Seeded seeded)
    {
        await using var s = store.LightweightSession(seeded.Slug);
        var counts = await AccountErasure.EraseCopiesAsync(s, seeded.Slug, seeded.Me.Id, await AccountErasure.NamesOnCopiesAsync(s, seeded.Me.Id), Array.Empty<string>());
        await s.SaveChangesAsync();
        return counts;
    }

    /// <summary>ما يَعرِضُه تَشغيلٌ لاحِق: الدالَّةُ نَفسُها في جَلسَةٍ لا تُحفَظ.</summary>
    internal static async Task<AccountErasure.Counts> PreviewAsync(IDocumentStore store, LiveAccountErasureFilesProofTests.Seeded seeded)
    {
        await using var dry = store.LightweightSession(seeded.Slug);
        return await AccountErasure.EraseCopiesAsync(dry, seeded.Slug, seeded.Me.Id, await AccountErasure.NamesOnCopiesAsync(dry, seeded.Me.Id), Array.Empty<string>());
    }

    /// <summary>روابِطُ الصُوَرِ في تاريخِ مَجرى الإعلان — كُلُّ <c>ListingMediaSet</c> فيه.</summary>
    internal static async Task<IReadOnlyList<string>> MediaHistoryAsync(IDocumentStore store, string slug, Guid listing)
    {
        await using var q = store.QuerySession(slug);
        return (await q.Events.FetchStreamAsync(listing)).Select(e => e.Data).OfType<ListingMediaSet>().SelectMany(m => m.MediaUrls).ToList();
    }

    private static bool IsPhoto(string key) => key.Contains("/listings/", StringComparison.Ordinal);

    /// <summary><b>يُثَبَّت</b>: قَبلَ المَحوِ خَمسَةُ مَفاتيح (ثَلاثٌ لِصورَتِه واثنانِ لِصورَتَي إعلانَيه) ورابِطٌ خارِجٌ واحِد؛ وبَعدَ التَنفيذِ إعلاناهُ بِلا صُوَر، وتاريخُ مَجرَيَيهِما
    /// يَحمِلُ روابِطَ الصُوَرِ الثَلاثَة. <b>ويُطبَعُ</b>: ما يَعرِضُه تَشغيلٌ لاحِقٌ مِن المَفاتيحِ وصُوَرِ الإعلاناتِ والتَغييرات.</summary>
    [Fact]
    public async Task Applying_without_storage_empties_his_listings_photos_and_their_event_history_still_carries_them_on_Postgres()
    {
        if (!Enabled) return;
        await using var host = LiveTemplateDoorHost.Build();
        var seeded = await SeedErasedAsync(host.Store, "zz-wdkeys-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            var before = await PreviewAsync(host.Store, seeded);
            var applied = await ApplyWithoutStorageAsync(host.Store, seeded);
            var after = await PreviewAsync(host.Store, seeded);
            await using var q = host.Store.QuerySession(seeded.Slug);
            var mine = (await q.LoadAsync<Listing>(seeded.MyListing))!;
            var old = (await q.LoadAsync<Listing>(seeded.MyOldListing))!;
            var history = (await MediaHistoryAsync(host.Store, seeded.Slug, seeded.MyListing)).Concat(await MediaHistoryAsync(host.Store, seeded.Slug, seeded.MyOldListing)).ToList();
            output.WriteLine($"قَبلَ التَنفيذ: مَفاتيح {before.Files.Keys.Count} (صُوَرُ إعلانات {before.Files.Keys.Count(IsPhoto)}) · خارِجَ مَجَلَّدِه {before.Files.Outside} · تَغييرات {before.Changes} | "
                + $"نُفِّذَ بِلا مَخزَن: تَغييرات {applied.Changes} | تَشغيلٌ لاحِق: مَفاتيح {after.Files.Keys.Count} (صُوَرُ إعلانات {after.Files.Keys.Count(IsPhoto)}) · تَغييرات {after.Changes} | "
                + $"صُوَرُ إعلانَيهِ الآن {mine.MediaUrls.Count + old.MediaUrls.Count} · روابِطُ في تاريخِ مَجرَيَيهِما {history.Count}");
            Assert.Equal(5, before.Files.Keys.Count);
            Assert.Contains(seeded.MyPhotoKey, before.Files.Keys);
            Assert.Contains(seeded.MyOldPhotoKey, before.Files.Keys);
            Assert.Equal(1, before.Files.Outside);
            Assert.True(applied.Changes > 0);
            Assert.Empty(mine.MediaUrls);
            Assert.Empty(old.MediaUrls);
            Assert.Equal(3, history.Count);
            Assert.Contains($"/uploads/{seeded.MyPhotoKey}", history);
            Assert.Contains($"/uploads/{seeded.MyOldPhotoKey}", history);
        }
        finally { await LiveAccountErasureFilesProofTests.CleanAsync(host.Store, seeded); }
    }
}
