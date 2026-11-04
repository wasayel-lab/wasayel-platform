using ACommerce.Kit.Auth;
using ACommerce.Kit.Chat;
using ACommerce.Kit.Reviews;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using ACommerce.Templates.Customer.Marketplace.Services.Identity;
using Marten;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ سكربتُ «ما حُذِفَ قَبلَ السَحب» يُتِمُّ الحَذفَ كُلَّه بِدَوالِّ البابِ نَفسِها (‏2026-11-04، الموجَة ١٢ G1 · ١١ — B-1 + B-4 + G-DOCS-4؛ تَوصيفُه 443d914) ════
//
// قَبلَه («يُنادي EraseCopiesAsync: False · يَكتُبُ إشعاراً: False · هَدَفُ التَدقيق: User · يَحذِفُ مِلَفّات: False» — `ErasedOwnerListingScriptTests` يَطبَعُه ويَبقى بِبَصمَتِه).
// والآن: `NamesOnCopiesAsync` و`EraseCopiesAsync` بِجَلسَةٍ لا تُحفَظُ لِلعَرضِ وبِحِفظٍ واحِدٍ لِلتَنفيذ، وإشعارُ جَرَسٍ بِـ`ClosedByErasureFor`، وسَطرُ تَدقيقٍ هَدَفُه المَتجَر،
// ومِلَفّاتٌ بِمَخزَنٍ مَضبوط؛ وعُنوانُ صَفقَةٍ جارِيَةٍ يَبقى (د52). والبُرهانُ بِتَشغيلِ السكربتِ نَفسِه في رِسالَةِ الكوميت.

public class ErasedOwnerListingScriptSwitchTests(ITestOutputHelper output)
{
    /// <summary>الشَكل: العَرضُ هُوَ الدالَّةُ نَفسُها في جَلسَةٍ لا تُحفَظُ قَبلَ بَوّابَةِ التَأكيد، والتَنفيذُ بِها في حِفظٍ واحِدٍ بَعدَ البَيان؛ والإشعارُ بِبانِي البابِ، والتَدقيقُ عَلى المَتجَر،
    /// والمِلَفّاتُ بِمَفاتيحِ الإعدادِ الخَمسَةِ لا بِنُسخَةٍ مِن أَسمائِها؛ والرَأسُ لا يَحتَجُّ بِـSignalR.</summary>
    [Fact]
    public void The_script_previews_with_the_doors_eraser_unsaved_then_applies_it_notifies_audits_the_store_and_deletes_files()
    {
        var text = ErasedOwnerListingScriptTests.ScriptText();
        var code = WriteEndpointGuardTests.StripComments(text);
        int At(string what) => code.IndexOf(what, StringComparison.Ordinal);
        var dryOpen = At("await using (var dry = store.LightweightSession(slug))");
        var dryErase = At("counts = await AccountErasure.EraseCopiesAsync(dry, slug, id, await AccountErasure.NamesOnCopiesAsync(dry, id), Array.Empty<string>());");
        var gate = At("if (confirm is null || confirm != changes)");
        var manifest = At("File.WriteAllTextAsync(manifestPath");
        var applyErase = At("var counts = await AccountErasure.EraseCopiesAsync(s, slug, owner, await AccountErasure.NamesOnCopiesAsync(s, owner), Array.Empty<string>());");
        var save = At("await s.SaveChangesAsync(); fresh = counts; break;");
        var close = At("AccountErasure.CloseIncomingAsync(dealsService, slug, owner, fresh.IncomingDeals, reason)");
        var notice = At("n.Store(DealNotices.ClosedByErasureFor(slug, closed, l, DateTime.UtcNow));");
        var files = At("AccountErasure.DeleteFilesAsync(files, present, NullLogger.Instance)");
        output.WriteLine($"العَرض {dryOpen}/{dryErase} · التَأكيد {gate} · البَيان {manifest} · التَنفيذ {applyErase} · الحِفظ {save} · الإلغاء {close} · الإشعار {notice} · المِلَفّات {files}");
        Assert.True(dryOpen > 0 && dryErase > dryOpen && gate > dryErase && manifest > gate && applyErase > manifest && save > applyErase && close > save && notice > close && files > close,
            "التَرتيب: العَرضُ بِلا حِفظٍ ثُمَّ التَأكيدُ ثُمَّ البَيانُ ثُمَّ التَنفيذُ بِحِفظٍ ثُمَّ الإلغاءُ ثُمَّ الإشعارُ والمِلَفّات.");
        Assert.DoesNotContain("SaveChangesAsync", code[dryOpen..gate], StringComparison.Ordinal);
        Assert.Contains("\"listing.withdraw_erased_owner\", \"tenant\", slug,", code, StringComparison.Ordinal);
        Assert.Contains("FileStorageSelection.ConfigKeys.All(", code, StringComparison.Ordinal);
        Assert.Contains("var changes = work.Sum(w => w.Counts.Changes + w.Files.Count);", code, StringComparison.Ordinal);
        Assert.Contains("وَسيلَتا الاتِّصال: مَمحُوَّتانِ مِن قَبل", code, StringComparison.Ordinal);
        Assert.DoesNotContain("SignalR", text, StringComparison.Ordinal);
        Assert.DoesNotContain("\"ashare\"", text, StringComparison.Ordinal);
    }

    /// <summary><b>المُوجَب</b>: صَفقَةٌ مُنتَهِيَةٌ بادَرَ بِها يُمحى عُنوانُها وهاتِفُها مَعَ اسمِه. <b>والسالِب</b>: صَفقَةٌ جارِيَةٌ — فَعّالَةٌ أَو في نِزاع — يُمحى اسمُه ويَبقى عُنوانُها
    /// وهاتِفُها لِيُسَلِّمَ البائِع (د52) — ولا يَبلُغُها بابُ الحَذفِ أَصلاً (يَرُدُّها <c>open_deal</c>).</summary>
    [Theory]
    [InlineData(DealStatus.Completed, true)]
    [InlineData(DealStatus.Cancelled, true)]
    [InlineData(DealStatus.Active, false)]
    [InlineData(DealStatus.Disputed, false)]
    public void A_finished_deal_loses_his_address_and_phone_and_an_open_one_keeps_them_for_delivery(DealStatus status, bool stripped)
    {
        var me = Guid.NewGuid();
        var d = new Deal { Id = Guid.NewGuid(), InitiatorId = me, InitiatorName = "مُشتَرٍ يُغلِقُ حِسابَه", Status = status,
            Attributes = new() { [Deal.DeliveryAddressAttribute] = "حَيُّ العُليا", [Deal.DeliveryPhoneAttribute] = "+966500000661" } };
        Assert.True(AccountErasure.EraseFrom(d, me));
        Assert.Equal(AccountDeletion.ErasedName, d.InitiatorName);
        Assert.Equal(!stripped, d.Attributes.ContainsKey(Deal.DeliveryAddressAttribute));
        Assert.Equal(!stripped, d.Attributes.ContainsKey(Deal.DeliveryPhoneAttribute));
    }

    /// <summary><b>عَدُّ التَغييرات</b>: كُلُّ ما يَكتُبُه المَحوُ في القاعِدَةِ مَرَّةً — والمِلَفّاتُ خارِجَه.</summary>
    [Fact]
    public void Changes_count_every_database_change_once_and_no_file()
    {
        var k = new AccountErasure.Counts(2, 1, 1, 0, 0, 0, 0, 1)
        {
            Listings = 2, RejectedOffers = 1, IncomingDeals = new[] { Guid.NewGuid() }, Scrubbed = 3, SavedSearches = 1, Favorites = 1, ImportedRows = 1, CarrierBindings = 1,
            Files = new AccountErasure.ErasedFiles(new[] { "a", "b", "c" }, 0),
        };
        Assert.Equal((5, 4, 16), (k.Total, k.Removed, k.Changes));
    }
}

[Collection(LiveProofCollection.Name)]
public class LiveErasedOwnerListingScriptSwitchProofTests(ITestOutputHelper output)
{
    private static bool Enabled
        => LiveDealConcurrencyProofTests.Enabled && !string.IsNullOrEmpty(LiveDealConcurrencyProofTests.ConnectionString);

    /// <summary><b>المُوجَب</b>: لِحِسابٍ حُذِفَ بِـ<c>Erase</c> وَحدَه، أَسماؤُه مِن نُسَخٍ تَحمِلُ مُعَرِّفَه — طَرَفاً في صَفقَةٍ وسَطراً فَعَلَه، وطَرَفاً في مُحادَثَة، ومُقَيَّماً — بِلا تَكرار.
    /// <b>والسالِب</b>: اسمُ غَيرِه عَلى النُسَخِ نَفسِها لا يُعَدّ، و«حِسابٌ مَحذوف» لا يُعَدُّ اسماً، وحِسابٌ لا نُسخَةَ لَه لا اسمَ لَه.</summary>
    [Fact]
    public async Task His_names_come_from_the_copies_that_carry_his_id_and_no_one_elses_on_Postgres()
    {
        if (!Enabled) return;
        await using var host = LiveTemplateDoorHost.Build();   // ← إعدادُ المَخزَنِ الحَيُّ نَفسُه: العَرضُ والتَذكِرَةُ لَقطَتانِ بِإصدارٍ رَقمِيّ
        var store = host.Store;
        var slug = "zz-namescp-" + Guid.NewGuid().ToString("N")[..8];
        var (me, other, nobody) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        const string mine = "مالِكٌ حُذِفَ قَبلَ المَحو";
        const string theirs = "مُشتَرٍ قائِم";
        var deal = new Deal { Id = Guid.NewGuid(), TenantSlug = slug, InitiatorId = other, InitiatorName = theirs, CounterpartyId = me, CounterpartyName = mine, Status = DealStatus.Completed };
        deal.Timeline.Add(new(DealStage.Offered, DealStage.Booked, "advanced", me, mine, null, DateTime.UtcNow));
        deal.Timeline.Add(new(DealStage.Booked, DealStage.Delivered, "advanced", other, theirs, null, DateTime.UtcNow));
        var erasedAlready = new Deal { Id = Guid.NewGuid(), TenantSlug = slug, InitiatorId = me, InitiatorName = AccountDeletion.ErasedName, Status = DealStatus.Completed };
        try
        {
            await using (var s = store.LightweightSession(slug))
            {
                s.Store(deal, erasedAlready);
                s.Store(new Conversation { Id = Guid.NewGuid(), OwnerId = other, OwnerName = theirs, PartnerId = me, PartnerName = mine });
                s.Store(new Review { Id = Guid.NewGuid(), AuthorId = other, AuthorName = theirs, TargetUserId = me, TargetUserName = mine });
                await s.SaveChangesAsync();
            }
            await using var q = store.QuerySession(slug);
            var names = await AccountErasure.NamesOnCopiesAsync(q, me);
            var none = await AccountErasure.NamesOnCopiesAsync(q, nobody);
            output.WriteLine($"أَسماؤُه مِن النُسَخ: [{string.Join(", ", names)}] · اسمُ غَيرِه بَينَها: {names.Contains(theirs)} · لِحِسابٍ بِلا نُسخَة: {none.Count}");
            Assert.Equal(new[] { mine }, names);
            Assert.Empty(none);
        }
        finally
        {
            await using var s = store.LightweightSession(slug);
            s.DeleteWhere<Deal>(d => d.Id != Guid.Empty);
            s.DeleteWhere<Conversation>(c => c.Id != Guid.Empty);
            s.DeleteWhere<Review>(r => r.Id != Guid.Empty);
            await s.SaveChangesAsync();
        }
    }
}
