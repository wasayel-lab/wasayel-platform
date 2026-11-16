using System.Diagnostics;
using ACommerce.Kit.Listings;
using ACommerce.Templates.Customer.Marketplace.Services.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ سكربتُ «ما حُذِفَ قَبلَ السَحب»: المَخزَنُ شَرطُ تَنفيذٍ في خُطَّتِه صُوَرُ إعلانات، والمَفاتيحُ مِن تاريخِ المَجاري (‏2026-11-05، الموجَة ١٢ G1b — مُراجَعَةُ الإصلاح ١، G1b-R1-3) ═══
//
// قَبلَه (`ErasedOwnerListingScriptStorageTests` يَطبَعُه ويَبقى بِبَصمَتِه، تَوصيفُه 81a8e8e): «تَشغيلٌ لاحِق: مَفاتيح 3 (صُوَرُ إعلانات 0) · تَغييرات 0» بَعدَ تَنفيذٍ بِلا مَخزَن،
// و«يَرُدُّ التَنفيذَ بِلا مَخزَن: False · «لا شَيءَ لِيُفعَل» يَسأَلُ المَفاتيحَ الَّتي لَم تُسأَل: False · يَقرَأُ تاريخَ صُوَرِ الإعلانات: False». والقَرارُ «أ» (P1 — خُصوصِيَّةُ
// المَنصَّة): `AccountErasure.FileKeysEverAsync` بِكُلِّ رابِطٍ في مَجرى إعلانِه، و`--apply` بِلا مَخزَنٍ وفي الخُطَّةِ صورَةُ إعلانٍ ⇐ 1 بِلا كِتابَة، ولا «لا شَيءَ لِيُفعَل» ومَفاتيحُ لَم تُسأَل.

public class ErasedOwnerListingScriptStorageRowsTests(ITestOutputHelper output)
{
    /// <summary><b>المُوجَب</b>: روابِطُ كُلِّ <c>ListingMediaSet</c> في المَجرى — ما استُبدِلَ وما بَقِيَ — بِلا تَكرار. <b>والسالِب</b>: حَدَثٌ آخَرُ لا رابِطَ فيه، ومَجرىً بِلا صُوَرٍ فارِغ.</summary>
    [Fact]
    public void The_media_history_is_every_url_of_every_media_set_in_the_stream_and_nothing_else()
    {
        var id = Guid.NewGuid();
        var at = DateTime.UtcNow;
        var history = AccountErasure.MediaHistory(new object[]
        {
            new ListingCreated(id, "zz", "إعلان", null, 1m, "c", null, null, new() { ["photo"] = "/uploads/not-a-media-set.jpg" }, at),
            new ListingMediaSet(id, new List<string> { "/uploads/tenants/zz/listings/a/0.jpg", "/uploads/tenants/zz/listings/a/1.jpg" }, at),
            new ListingMediaSet(id, new List<string> { "/uploads/tenants/zz/listings/a/1.jpg" }, at),
            new ListingMediaSet(id, new List<string>(), at),
            new ListingDeleted(id, at),
        });
        Assert.Equal(new[] { "/uploads/tenants/zz/listings/a/0.jpg", "/uploads/tenants/zz/listings/a/1.jpg" }, history);
        Assert.Empty(AccountErasure.MediaHistory(new object[] { new ListingDeleted(id, at) }));
        Assert.Empty(AccountErasure.MediaHistory(Array.Empty<object>()));
    }

    /// <summary>الشَكل: العَرضُ يَقرَأُ المَفاتيحَ بِالتاريخِ في جَلسَتِه الَّتي لا تُحفَظ، وصُوَرُ الإعلاناتِ ما سِوى صورَتِه؛ و«لا شَيءَ لِيُفعَل» بِلا تَغييرٍ ولا مِفتاحٍ لَم يُسأَل؛ ورَدُّ التَنفيذِ
    /// بِلا مَخزَنٍ بَعدَ العَرضِ وقَبلَ التَأكيدِ والبَيانِ وأَوَّلِ حِفظ، يَخرُجُ 1 ويُسَمّي <c>Files__S3__*</c>؛ والبَيانُ بِمَفاتيحِ التاريخ.</summary>
    [Fact]
    public void The_script_reads_keys_from_history_refuses_a_storage_less_apply_with_photos_and_never_says_nothing_to_do_with_keys_unchecked()
    {
        var code = WriteEndpointGuardTests.StripComments(ErasedOwnerListingScriptTests.ScriptText());
        int At(string what, int from = 0) => code.IndexOf(what, from, StringComparison.Ordinal);
        var dry = At("await using (var dry = store.LightweightSession(slug))");
        var history = At("keys = await AccountErasure.FileKeysEverAsync(dry, slug, id);", dry);
        var photos = At("var photos = keys.Keys.Except(AccountErasure.AvatarKeys(slug, id), StringComparer.Ordinal).Count();", history);
        var nothing = At("if (changes == 0 && keysUnchecked == 0)");
        var show = At("if (!apply)");
        var refuse = At("if (files is null && planPhotos > 0)", At("return 2;", show));
        var gate = At("if (confirm is null || confirm != changes)");
        var manifest = At("File.WriteAllTextAsync(manifestPath");
        var firstSave = At("SaveChangesAsync");
        output.WriteLine($"العَرض {dry} · التاريخ {history} · صُوَرُ الإعلانات {photos} · «لا شَيءَ» {nothing} · العَرضُ فَقَط {show} · الرَدّ {refuse} · التَأكيد {gate} · البَيان {manifest} · أَوَّلُ حِفظ {firstSave}");
        Assert.True(dry > 0 && history > dry && photos > history, "المَفاتيحُ لا تُقرَأُ بِالتاريخِ في جَلسَةِ العَرض.");
        Assert.True(nothing > photos && show > nothing && refuse > show && gate > refuse && manifest > gate && firstSave > manifest,
            "التَرتيب: «لا شَيءَ» ثُمَّ العَرضُ ثُمَّ الرَدُّ ثُمَّ التَأكيدُ ثُمَّ البَيانُ ثُمَّ الحِفظ.");
        var refusal = code[refuse..gate];
        Assert.Contains("return 1;", refusal, StringComparison.Ordinal);
        Assert.Contains("Files__S3__*", refusal, StringComparison.Ordinal);
        Assert.DoesNotContain("SaveChangesAsync", code[dry..gate], StringComparison.Ordinal);
        Assert.Contains("return apply ? 1 : 2;", code[nothing..show], StringComparison.Ordinal);
        Assert.Contains("fileKeys = keys.Keys, filesOutside = keys.Outside, filesPresent = present,", code, StringComparison.Ordinal);
    }
}

[Collection(LiveProofCollection.Name)]
public class LiveErasedOwnerListingScriptStorageSwitchProofTests(ITestOutputHelper output)
{
    private static bool Enabled
        => LiveDealConcurrencyProofTests.Enabled && !string.IsNullOrEmpty(LiveDealConcurrencyProofTests.ConnectionString);

    private static bool IsPhoto(string key) => key.Contains("/listings/", StringComparison.Ordinal);

    /// <summary><b>المُوجَب</b>: بَعدَ تَنفيذٍ بِلا مَخزَنٍ مَحا روابِطَ صُوَرِه، تَقرَأُ <c>FileKeysEverAsync</c> مِن التاريخِ صورَتَي إعلانَيهِ وصورَتَه بِامتِداداتِها — خَمسَة — والرابِطُ
    /// الخارِجُ يُعَدُّ ولا يُحذَف، و<c>DeleteFilesAsync</c> يَحذِفُها كُلَّها مِن مَخزَنٍ يُسَجِّل. <b>والسالِب</b>: لا مِفتاحَ لِلعُضوِ الآخَرِ ولا لِإعلانِه، وحِسابٌ بِلا إعلانٍ صورَتُه وَحدَها.</summary>
    [Fact]
    public async Task After_a_storage_less_apply_his_photo_keys_still_come_back_from_the_stream_history_and_are_deleted_with_storage_on_Postgres()
    {
        if (!Enabled) return;
        await using var host = LiveTemplateDoorHost.Build();
        var seeded = await LiveErasedOwnerListingScriptStorageProofTests.SeedErasedAsync(host.Store, "zz-wdever-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            await LiveErasedOwnerListingScriptStorageProofTests.ApplyWithoutStorageAsync(host.Store, seeded);
            await using var q = host.Store.QuerySession(seeded.Slug);
            var ever = await AccountErasure.FileKeysEverAsync(q, seeded.Slug, seeded.Me.Id);
            var others = await AccountErasure.FileKeysEverAsync(q, seeded.Slug, seeded.Other.Id);
            var nobody = Guid.NewGuid();
            var none = await AccountErasure.FileKeysEverAsync(q, seeded.Slug, nobody);
            var storage = new RecordingFileStorage();
            var (deleted, failed) = await AccountErasure.DeleteFilesAsync(storage, ever.Keys, NullLogger.Instance);
            output.WriteLine($"بَعدَ تَنفيذٍ بِلا مَخزَن: مَفاتيحُه بِالتاريخ {ever.Keys.Count} (صُوَرُ إعلانات {ever.Keys.Count(IsPhoto)}) · خارِجَ مَجَلَّدِه {ever.Outside} · "
                + $"حُذِفَ بِمَخزَنٍ يُسَجِّل {deleted} (تَعَذَّر {failed}) · مَفاتيحُ الآخَر {others.Keys.Count} (صُوَر {others.Keys.Count(IsPhoto)}) · حِسابٌ بِلا إعلان {none.Keys.Count}");
            Assert.Equal(new[]
            {
                seeded.MyAvatarKey, $"tenants/{seeded.Slug}/avatars/{seeded.Me.Id}.jpg", $"tenants/{seeded.Slug}/avatars/{seeded.Me.Id}.webp",
                seeded.MyPhotoKey, seeded.MyOldPhotoKey,
            }.OrderBy(k => k, StringComparer.Ordinal), ever.Keys.OrderBy(k => k, StringComparer.Ordinal));
            Assert.Equal(1, ever.Outside);
            Assert.Equal((5, 0), (deleted, failed));
            Assert.DoesNotContain(ever.Keys, k => k.Contains(seeded.Other.Id.ToString(), StringComparison.Ordinal) || k.Contains(seeded.OtherListing.ToString(), StringComparison.Ordinal));
            Assert.Contains(seeded.OtherPhotoKey, others.Keys);
            Assert.DoesNotContain(seeded.MyPhotoKey, others.Keys);
            Assert.Equal(AccountErasure.AvatarKeys(seeded.Slug, nobody).OrderBy(k => k, StringComparer.Ordinal), none.Keys.OrderBy(k => k, StringComparer.Ordinal));
        }
        finally { await LiveAccountErasureFilesProofTests.CleanAsync(host.Store, seeded); }
    }

    /// <summary>السكربتُ نَفسُه بِـ<c>dotnet run</c> عَلى القاعِدَةِ المُعلَنَة، بِلا <c>Files__S3__*</c>، وبَيانُه في مَجَلَّدٍ مُؤَقَّت.</summary>
    private static (int Code, string Output) RunScript(string backupDir, params string[] args)
    {
        var script = Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, "scripts", "withdraw-erased-owner-listings.cs");
        Assert.True(File.Exists(script), $"مَصدَرٌ مَفقود: {script}");
        var psi = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = ThemeZeroEquivalenceTests.RepoRoot,
        };
        foreach (var a in new[] { "run", script, "--" }.Concat(args).Concat(new[] { "--backup-dir", backupDir })) psi.ArgumentList.Add(a);
        psi.Environment["ConnectionStrings__Postgres"] = LiveDealConcurrencyProofTests.ConnectionString;
        foreach (var key in ACommerce.Kit.Files.FileStorageSelection.ConfigKeys) psi.Environment.Remove(key.Replace(":", "__"));
        psi.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        psi.Environment["DOTNET_NOLOGO"] = "1";
        // ‏ولا يَبقى بَعدَه خادِمُ بِناءٍ يَعيشُ بَعدَ الصَفّ.
        psi.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        psi.Environment["DOTNET_CLI_USE_MSBUILD_SERVER"] = "0";
        psi.Environment["UseSharedCompilation"] = "false";
        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        Assert.True(p.WaitForExit(TimeSpan.FromMinutes(6)), "السكربتُ لَم يَنتَهِ في سِتِّ دَقائِق.");
        return (p.ExitCode, stdout.Result + stderr.Result);
    }

    /// <summary>السكربتُ يَقرَأُ المَتاجِرَ مِن وَثائِقِها — فَالمَتجَرُ المَبذورُ يُخَزَّنُ لِيُفحَص (و<c>CleanAsync</c> يَحذِفُه).</summary>
    private static async Task StoreTenantAsync(Marten.IDocumentStore store, string slug)
    {
        await using var g = store.LightweightSession();
        g.Store(new ACommerce.Kit.Tenants.Tenant { Id = slug, Name = "مَتجَرُ سكربتِ السَحب", OwnerUserId = Guid.NewGuid() });
        await g.SaveChangesAsync();
    }

    private static int PhotosUncheckedOf(string output)
    {
        var m = System.Text.RegularExpressions.Regex.Match(output, @"مِنها صُوَرُ إعلاناتٍ (\d+) مِن تاريخِ مَجاريها");
        return m.Success ? int.Parse(m.Groups[1].Value) : 0;
    }

    private static int ChangesOf(string output)
    {
        var m = System.Text.RegularExpressions.Regex.Match(output, @"تَغييرات: (\d+)");
        Assert.True(m.Success, "لا سَطرَ «تَغييرات:» — الأَداةُ عَمياء.\n" + output);
        return int.Parse(m.Groups[1].Value);
    }

    /// <summary><b>صَفُّ السكربت</b> — حِسابٌ مَحذوفٌ لِإعلانَيهِ صُوَر، بِلا مَخزَن: العَرضُ ⇐ 2 بِسَطرِ «والتَنفيذُ يُرَدُّ بِلا مَخزَن» ولا أَمرَ تَنفيذ؛ و<c>--apply --confirm N</c> ⇐ 1
    /// «تَوَقُّف» ولا بَيانَ ولا كِتابَة (روابِطُ صُوَرِه قائِمَة، والعَرضُ بَعدَه بِالعَدَدِ نَفسِه). ثُمَّ — بَعدَ تَنفيذٍ سابِقٍ بِلا مَخزَنٍ بِالدالَّةِ نَفسِها — العَرضُ ⇐ 2 لا «لا شَيءَ لِيُفعَل»،
    /// وصُوَرُ الإعلاناتِ مِن التاريخ، و<c>--apply</c> ⇐ 1.</summary>
    [Fact]
    public async Task The_script_refuses_a_storage_less_apply_when_photo_keys_exist_and_never_says_nothing_to_do_with_keys_unchecked_on_Postgres()
    {
        if (!Enabled) return;
        await using var host = LiveTemplateDoorHost.Build();
        var seeded = await LiveErasedOwnerListingScriptStorageProofTests.SeedErasedAsync(host.Store, "zz-wdrefuse-" + Guid.NewGuid().ToString("N")[..8]);
        await StoreTenantAsync(host.Store, seeded.Slug);
        var backups = Path.Combine(Path.GetTempPath(), "wsl-withdraw-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(backups);
        try
        {
            var (dryCode, dry) = RunScript(backups);
            var n = ChangesOf(dry);
            var (applyCode, applied) = RunScript(backups, "--apply", "--confirm", n.ToString());
            await using (var q = host.Store.QuerySession(seeded.Slug))
            {
                var mine = (await q.LoadAsync<Listing>(seeded.MyListing))!;
                var manifests = Directory.GetFiles(backups).Length;
                var (againCode, again) = RunScript(backups);
                output.WriteLine($"العَرض: {dryCode} · تَغييرات {n} · سَطرُ الرَدّ {dry.Contains("والتَنفيذُ يُرَدُّ بِلا مَخزَن")} · أَمرُ التَنفيذ {dry.Contains("--apply --confirm")} | "
                    + $"التَنفيذ: {applyCode} · «تَوَقُّف» {applied.Contains("تَوَقُّف: لا مَخزَنَ مَضبوط")} · بَيانات {manifests} · صُوَرُ إعلانِه القائِم {mine.MediaUrls.Count} | "
                    + $"العَرضُ بَعدَه: {againCode} · تَغييرات {ChangesOf(again)}");
                Assert.Equal(2, dryCode);
                Assert.Contains("والتَنفيذُ يُرَدُّ بِلا مَخزَن", dry, StringComparison.Ordinal);
                Assert.DoesNotContain("--apply --confirm", dry, StringComparison.Ordinal);
                Assert.True(n > 0);
                Assert.Equal(1, applyCode);
                Assert.Contains("تَوَقُّف: لا مَخزَنَ مَضبوط", applied, StringComparison.Ordinal);
                Assert.Equal(0, manifests);
                Assert.Equal(2, mine.MediaUrls.Count);
                Assert.Equal((2, n), (againCode, ChangesOf(again)));
            }

            await LiveErasedOwnerListingScriptStorageProofTests.ApplyWithoutStorageAsync(host.Store, seeded);
            var (laterCode, later) = RunScript(backups);
            var (laterApplyCode, laterApplied) = RunScript(backups, "--apply", "--confirm", "0");
            output.WriteLine($"بَعدَ تَنفيذٍ سابِقٍ بِلا مَخزَن: العَرض {laterCode} · «لا شَيءَ لِيُفعَل» {later.Contains("لا شَيءَ لِيُفعَل.")} · "
                + $"صُوَرُ إعلاناتٍ مِن التاريخ {PhotosUncheckedOf(later)} · «لَم تُسأَل» {later.Contains("ومَفاتيحُ مِلَفّاتٍ لَم تُسأَل")} · التَنفيذ {laterApplyCode}");
            Assert.Equal(2, laterCode);
            Assert.DoesNotContain("لا شَيءَ لِيُفعَل.", later, StringComparison.Ordinal);
            Assert.True(PhotosUncheckedOf(later) >= 2, "صورَتا إعلانَيهِ لا تُقرَآنِ مِن التاريخ.");
            Assert.Equal(1, laterApplyCode);
        }
        finally
        {
            await LiveAccountErasureFilesProofTests.CleanAsync(host.Store, seeded);
            Directory.Delete(backups, recursive: true);
        }
    }

    /// <summary><b>السالِب</b> — حِسابٌ مَحذوفٌ بِلا صورَةِ إعلان (إعلانٌ بِلا صُوَر): العَرضُ بِلا مَخزَنٍ ⇐ 2 بِأَمرِ التَنفيذِ ولا سَطرِ رَدّ — الرَدُّ لِصُوَرِ الإعلاناتِ وَحدَها،
    /// وصورَةُ الحِسابِ مِفتاحُها حَتمِيٌّ يَجِدُه تَشغيلٌ لاحِق. (لا يُنَفَّذ: الصَفُّ لا يَكتُبُ في القاعِدَةِ المُعلَنَة.)</summary>
    [Fact]
    public async Task Without_listing_photos_the_storage_less_preview_prints_the_apply_command_on_Postgres()
    {
        if (!Enabled) return;
        await using var host = LiveTemplateDoorHost.Build();
        var slug = "zz-wdnophoto-" + Guid.NewGuid().ToString("N")[..8];
        var me = new ACommerce.Kit.Auth.User { Id = Guid.NewGuid(), TenantSlug = slug, FullName = "عُضوٌ حُذِفَ بِلا صُوَر", Phone = "+966500000641", PhoneVerified = true };
        var listing = Guid.NewGuid();
        var backups = Path.Combine(Path.GetTempPath(), "wsl-withdraw-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(backups);
        await using (var s = host.Store.LightweightSession(slug))
        {
            s.Store(ACommerce.Kit.Auth.AccountDeletion.Erase(me, DateTime.UtcNow.AddDays(-1)));
            s.Events.StartStream<Listing>(listing, new ListingCreated(listing, slug, "إعلانٌ بِلا صُوَر", null, 10m, "c", null, null,
                new() { [ACommerce.Templates.Customer.Marketplace.Services.Listings.ListingEditService.OwnerAttribute] = me.Id.ToString() }, DateTime.UtcNow.AddDays(-3)));
            await s.SaveChangesAsync();
        }
        await StoreTenantAsync(host.Store, slug);
        try
        {
            var (code, dry) = RunScript(backups);
            output.WriteLine($"بِلا صُوَرِ إعلان: العَرض {code} · سَطرُ الرَدّ {dry.Contains("والتَنفيذُ يُرَدُّ بِلا مَخزَن")} · أَمرُ التَنفيذ {dry.Contains("--apply --confirm")} · تَغييرات {ChangesOf(dry)}");
            Assert.Equal(2, code);
            Assert.DoesNotContain("والتَنفيذُ يُرَدُّ بِلا مَخزَن", dry, StringComparison.Ordinal);
            Assert.Contains("--apply --confirm", dry, StringComparison.Ordinal);
        }
        finally
        {
            await using (var s = host.Store.LightweightSession(slug))
            {
                s.DeleteWhere<Listing>(l => l.Id != Guid.Empty);
                s.Events.ArchiveStream(listing);
                await s.SaveChangesAsync();
            }
            await LiveZeroRoleAdminProofTests.CleanAsync(host.Store, slug);
            Directory.Delete(backups, recursive: true);
        }
    }
}
