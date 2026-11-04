using ACommerce.Kit.Auth;
using ACommerce.Kit.Notifications;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using ACommerce.Templates.Customer.Marketplace.Services.Listings;
using Marten;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ إشعارُ مُشتَري العَرضِ الَّذي أَلغاه إغلاقُ البائِعِ حِسابَه — تَوصيفٌ قَبلَ التَبديل (‏2026-11-04، الموجَة ١٢ G1 · ٢، الفَحصُ (د) عَلى f4e1296) ═══
//
// **ما يُثَبَّتُ هُنا لِيَبقى** (القاعِدَة ٣): حَذفُ بائِعٍ لَه عَرضٌ وارِدٌ لَم يَقبَلْه أَحَدٌ يُلغي الصَفقَةَ بِاسمِ المَنصَّة، ويُخبِرُ مُشتَريها **مَرَّةً واحِدَة**
// بِإشعارٍ مِن نَوعِ الإلغاءِ يُحيلُ إلى صَفقَتِه، ولا يَكتُبُ لِلمَحذوفِ شَيئاً؛ وإلغاءُ بائِعٍ قائِمٍ طَلَباً يُسَمّي المُلغي بِاسمِه. وهذا ما لا يَمَسُّه التَبديل.
// **وما يُطبَعُ ولا يُثَبَّت**: نَصُّ الإشعارِ اليَوم — عُنوانُه «أَلغى البائِعُ طَلَبَكَ» ومَتنُه «حِسابٌ مَحذوف أَلغى طَلَبَكَ عَلى «…»» — والسَطرُ في صَفحَةِ الصَفقَةِ
// يَقولُ إنّ **المَنصَّةَ** أَلغَته لِأَنّ البائِعَ أَغلَقَ حِسابَه (`AccountErasure.ClosingCanceller`، `account.delete.cancel_reason`). فَالنَصُّ يَتَغَيَّرُ في كوميتِ التَبديل،
// وصُفوفُه في `AccountErasureTests`؛ وهذا المِلَفُّ يَبقى بِبَصمَتِه.

public class AccountErasureClosingNoticeTests
{
    /// <summary>إلغاءُ بائِعٍ قائِمٍ طَلَبَ مُشتَرٍ يُسَمّيه في الإشعار — مَسارُ <c>/cancel</c> العادِيُّ لا يَمَسُّه تَبديلُ إشعارِ الحَذف.</summary>
    [Fact]
    public void A_live_vendors_cancel_notice_names_him_and_points_at_the_deal()
    {
        var deal = new Deal { Id = Guid.NewGuid(), InitiatorId = Guid.NewGuid(), ListingTitle = "سِلعَةٌ قائِمَة", Status = DealStatus.Cancelled };
        var l = new ACommerce.Platform.I18n.L(new Microsoft.AspNetCore.Http.HttpContextAccessor());
        var n = DealNotices.CancelledFor("zz", deal, "بائِعٌ قائِم", l, DateTime.UtcNow);
        Assert.Equal((deal.InitiatorId, DealNotices.CancelledType, $"/zz/deals/{deal.Id}"), (n.UserId, n.Type, n.RelatedUrl));
        Assert.StartsWith("بائِعٌ قائِم", n.Body, StringComparison.Ordinal);
        Assert.Contains("«سِلعَةٌ قائِمَة»", n.Body, StringComparison.Ordinal);
    }
}

[Collection(LiveProofCollection.Name)]
public class LiveAccountErasureClosingNoticeProofTests(ITestOutputHelper output)
{
    private static bool Enabled
        => LiveDealConcurrencyProofTests.Enabled && !string.IsNullOrEmpty(LiveDealConcurrencyProofTests.ConnectionString);

    private const string DeleteDoor = "/{slug}/me/delete/confirm";

    /// <summary>بابُ الحَذفِ بِجِسمِه: بائِعٌ لَه عَرضٌ وارِدٌ لَم يَقبَلْه أَحَد («عَرض» بِلا طَرَف) يَحذِفُ حِسابَه — الصَفقَةُ مُلغاةٌ بِسَطرِ المَنصَّة، ولِمُشتَريها إشعارٌ واحِدٌ
    /// مِن نَوعِ الإلغاءِ إلى صَفقَتِه، ولا شَيءَ في صُندوقِ المَحذوف. ويُطبَعُ نَصُّ الإشعارِ كَما خُزِّن.</summary>
    [Fact]
    public async Task The_buyer_of_an_offer_closed_by_the_sellers_deletion_is_told_once_and_the_seller_nothing_on_Postgres()
    {
        if (!Enabled) return;
        var slug = "zz-close-" + Guid.NewGuid().ToString("N")[..8];
        await using var host = LiveTemplateDoorHost.Build();
        var seller = new User { Id = Guid.NewGuid(), TenantSlug = slug, FullName = "بائِعٌ يُغلِقُ حِسابَه", Phone = "+966500000591", PhoneVerified = true };
        var buyer = new User { Id = Guid.NewGuid(), TenantSlug = slug, FullName = "مُشتَري العَرض", Phone = "+966500000592", PhoneVerified = true };
        var listing = Guid.NewGuid();
        var deal = new Deal
        {
            Id = Guid.NewGuid(), TenantSlug = slug, ListingId = listing, ListingTitle = "سِلعَةُ البائِع", InitiatorId = buyer.Id, InitiatorName = buyer.FullName,
            AmountSar = 120m, Status = DealStatus.Active, Stage = DealStage.Offered, Refs = new() { [DealsService.ListingOwnerRef] = seller.Id.ToString() },
        };
        await using (var s = host.Store.LightweightSession(slug))
        {
            s.Store(seller, buyer);
            s.Events.StartStream<ACommerce.Kit.Listings.Listing>(listing, new ACommerce.Kit.Listings.ListingCreated(listing, slug, "سِلعَةُ البائِع", null, 120m, "c",
                null, null, new Dictionary<string, string> { [ListingEditService.OwnerAttribute] = seller.Id.ToString() }, DateTime.UtcNow));
            s.Store(deal);
            await s.SaveChangesAsync();
        }
        try
        {
            var token = ACommerce.Kit.Auth.Server.AuthHandlers.MakeToken(seller.Id, slug, seller.SessionVersion);
            var confirm = ("confirm", ACommerce.Platform.I18n.LocaleCatalog.Text(ACommerce.Platform.I18n.LocaleCatalog.Arabic, "account.delete.confirm_word"));
            var (status, at) = await host.PostFormAsync(DeleteDoor, new Dictionary<string, string> { ["slug"] = slug }, slug, token, confirm);
            await using var q = host.Store.QuerySession(slug);
            var closed = (await q.LoadAsync<Deal>(deal.Id))!;
            var toBuyer = await Marten.QueryableExtensions.ToListAsync(q.Query<Notification>().Where(n => n.UserId == buyer.Id));
            var toSeller = await Marten.QueryableExtensions.ToListAsync(q.Query<Notification>().Where(n => n.UserId == seller.Id));
            output.WriteLine($"حَذفُ البائِع: {status} → {at} · الصَفقَة: {closed.Status} بِسَطرِ المَنصَّة {DealEvent.IsByPlatform(closed.Timeline[^1])} · "
                + $"إشعاراتُ المُشتَري {toBuyer.Count} · إشعاراتُ المَحذوف {toSeller.Count} · العُنوان «{toBuyer.FirstOrDefault()?.Title}» · المَتن «{toBuyer.FirstOrDefault()?.Body}»");
            Assert.Equal((302, $"/{slug}?deleted=1"), (status, at));
            Assert.Equal(DealStatus.Cancelled, closed.Status);
            Assert.True(DealEvent.IsByPlatform(closed.Timeline[^1]));
            var told = Assert.Single(toBuyer);
            Assert.Equal((DealNotices.CancelledType, $"/{slug}/deals/{deal.Id}"), (told.Type, told.RelatedUrl));
            Assert.Contains("«سِلعَةُ البائِع»", told.Body, StringComparison.Ordinal);
            Assert.Empty(toSeller);
        }
        finally
        {
            await using (var s = host.Store.LightweightSession(slug))
            {
                s.DeleteWhere<Deal>(d => d.Id != Guid.Empty);
                s.DeleteWhere<Notification>(n => n.Id != Guid.Empty);
                s.DeleteWhere<ACommerce.Kit.Listings.Listing>(x => x.Id != Guid.Empty);
                s.Events.ArchiveStream(listing);
                await s.SaveChangesAsync();
            }
            await LiveZeroRoleAdminProofTests.CleanAsync(host.Store, slug);
        }
    }
}
