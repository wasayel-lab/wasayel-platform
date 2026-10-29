using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ إلغاءُ صَفقَةِ نَقدٍ قُبِضَ ثَمَنُها — تَوصيفُ «اليَوم» (‏2026-10-29، مُراجَعَةُ تَيّارِ S5 الأولى عَلى `c961e95`؛ قَرارُ المالِك د38 (٢)) ═══
//
// جَوابُ المالِك: «إلغاء مع تسجيل استرجاع مستحقّ إن كانت مدفوعة». وصَفقَةُ النَقدِ تُدفَعُ عِندَ «تَسليم»: بُلوغُها يَكتُبُ سَطرَ `cash_on_delivery`
// (`MoveAsync`) — «لَحظَةُ القَبضِ عِندَ البائِع». **والمَقيسُ عِندَ الرَأس**: إلغاؤها بَعدَه — بِحَسمِ النِزاعِ أَو بِبابِ الإلغاءِ في الاستوديو، وهُما
// كُلُّ ما يَبلُغُ «تَسليم» (الطَرَفُ لا يُلغي بَعدَ «دَفع»، د37) — يَكتُبُ «أُلغِيَت» وَحدَه: فُروعُ المالِ في `CancelCoreAsync` ثَلاثَة (قَبضُ بَوّابَةِ المَتجَر،
// ومَرجِعُ دَفعِ مُزَوِّدِ النُسخَة، والفاتورَةُ المَفتوحَة) ولا يَبلُغُ النَقدُ واحِداً مِنها، و`OwnerActionOf` لا يُسَمّي شَيئاً — والطَرَفانِ يُقالُ لَهُما
// «ويُرَدُّ ما دُفِعَ إن كانَ قَد دُفِع»، والشُروطُ «بِإلغائِها مَعَ رَدِّ ما دُفِع».
//
// **ما يُقلَبُ بِاسمِه في كوميتِ الإصلاح**: الصَفّانِ أَدناه.

public class DealCashRefundDueTests(ITestOutputHelper output)
{
    private const string Slug = "zz-cash-refund-due";
    private static readonly Guid Buyer    = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
    private static readonly Guid Seller   = Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb");
    private static readonly Guid AppOwner = Guid.Parse("cccccccc-cccc-4ccc-8ccc-cccccccccccc");
    private static readonly DealCanceller Owner = new(AppOwner, "مالِك التَّطبيق", IsStoreAdmin: true);

    private static T Now<T>(Task<T> task)
    {
        Assert.True(task.IsCompleted, "نِداءٌ لَم يَكتَمِل في عالَمٍ مُتَزامِن — الأَداةُ تَكذِب.");
        return task.GetAwaiter().GetResult();
    }

    /// <summary>صَفقَةُ سَلَّةٍ نَقدِيَّةٌ كَما يُولِدُها <c>/checkout/submit</c> — ثُمَّ يَقبَلُها البائِعُ المَربوطُ ويَمشيها بِالبابِ الحَقيقِيِّ حَتّى
    /// <paramref name="until"/>؛ ومُزَوِّدُ الدَفعِ يَرمي إن مُسّ (النَقدُ لا يَمُرُّ بِه).</summary>
    private static (DocWorld World, DealsService Deals, Guid Id) CashDealAt(DealStage until)
    {
        var world = new DocWorld();
        var deals = new DealsService(world.Store, new DealShipGroupTests.ForbiddenPaymentProvider());
        var deal = Now(deals.StartAsync(Slug, "marketplace", Buyer, "مُشتَرٍ", null, "سِلعَة", 120m,
            new() { [CheckoutPayMethods.AttributeKey] = CheckoutPayMethods.CashOnDelivery }));
        Now(deals.AttachRefAsync(Slug, deal.Id, DealsService.ListingOwnerRef, Seller.ToString()));
        Now(deals.BindListingOwnerAsync(Slug, deal.Id, Seller.ToString()));
        var accepted = Now(deals.AcceptAsync(Slug, deal.Id, new DealAcceptor(Seller, "بائِع")));
        Assert.True(accepted.Ok, accepted.Violation?.MessageAr);
        var clicks = 0;
        while (world.Read<Deal>(deal.Id.ToString())!.Stage != until && clicks++ < 10)
        {
            var r = Now(deals.AdvanceAsync(Slug, deal.Id, Seller, "بائِع"));
            Assert.True(r.Ok, $"النَقرَةُ {clicks} رُدَّت: {r.Reason}");
        }
        Assert.Equal(until, world.Read<Deal>(deal.Id.ToString())!.Stage);
        return (world, deals, deal.Id);
    }

    /// <summary>أَفعالُ السِجِلِّ بَعدَ آخِرِ سَطرٍ قَبلَ الإلغاء — ما كَتَبَه الإلغاءُ وما تَلاه.</summary>
    private static string[] WrittenFrom(Deal deal, int before) => deal.Timeline.Skip(before).Select(e => e.Action).ToArray();

    [Fact]
    public void Today_a_dispute_cancel_of_a_delivered_cash_deal_writes_cancelled_alone_and_names_no_refund_due()
    {
        var (world, deals, id) = CashDealAt(DealStage.Delivered);
        Assert.Single(world.Read<Deal>(id.ToString())!.Timeline, e => e.Action == DealEvent.CashOnDelivery);
        Assert.True(Now(deals.DisputeAsync(Slug, id, AppOwner, "مالِك التَّطبيق", "بِضاعَةٌ مَعيبَة")).Ok);
        var before = world.Read<Deal>(id.ToString())!.Timeline.Count;

        var res = Now(deals.ResolveDisputeAsync(Slug, id, Owner, DealDisputeResolution.Cancel, "بِضاعَةٌ مَعيبَة"));

        Assert.True(res.Ok, res.Violation?.MessageAr);
        var stored = world.Read<Deal>(id.ToString())!;
        output.WriteLine($"حَسمٌ بِالإلغاء: {stored.Status} · كُتِب: [{string.Join(", ", WrittenFrom(stored, before))}] · ما عَلى صاحِبِ المَتجَر: {DealNotices.OwnerActionOf(stored)?.ToString() ?? "لا شَيء"}");
        Assert.Equal(DealStatus.Cancelled, stored.Status);
        Assert.Equal(new[] { "cancelled" }, WrittenFrom(stored, before));
        Assert.Null(DealNotices.OwnerActionOf(stored));
    }

    [Fact]
    public void Today_the_store_owners_cancel_of_a_delivered_cash_deal_writes_cancelled_alone_and_names_no_refund_due()
    {
        var (world, deals, id) = CashDealAt(DealStage.Delivered);
        var before = world.Read<Deal>(id.ToString())!.Timeline.Count;

        var res = Now(deals.CancelAsync(Slug, id, Owner, "مُرتَجَع"));

        Assert.True(res.Ok, res.Violation?.MessageAr);
        var stored = world.Read<Deal>(id.ToString())!;
        output.WriteLine($"إلغاءُ صاحِبِ المَتجَر: {stored.Status} · كُتِب: [{string.Join(", ", WrittenFrom(stored, before))}]");
        Assert.Equal(DealStatus.Cancelled, stored.Status);
        Assert.Equal(new[] { "cancelled" }, WrittenFrom(stored, before));
        Assert.Null(DealNotices.OwnerActionOf(stored));
    }
}
