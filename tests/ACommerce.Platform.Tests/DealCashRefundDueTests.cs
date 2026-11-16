using ACommerce.Kit.Notifications;
using ACommerce.Platform.I18n;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using Microsoft.AspNetCore.Http;
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
//
// **وقُلِبا بِاسمَيهِما** (‏2026-10-29، قَرارُ المالِك د38 (٢) «ج»؛ ‏ADR-047 §٢ مُلحَقاً): فَرعٌ رابِعٌ في `CancelCoreAsync` — صَفقَةٌ قُبِضَ ثَمَنُها نَقداً
// (`DealsPolicy.CollectedInCash`) يُكتَبُ لَها بَعدَ حِفظِ الإلغاءِ سَطرُ `cash_refund_due` بِحَلقَةِ التَسجيلِ نَفسِها بِلا نِداء — «رَدُّه إلى المُشتَري عَلى
// البائِعِ الَّذي قَبَضَه» — و`OwnerActionOf` يُسَمّيه لِصاحِبِ المَتجَرِ فَيُخبَرُ بِه بِالأُنبوبِ نَفسِه (`NotifyStoreOwnerAsync`). وما سِواهُما صُفوفُ مُوجِبٍ وسالِب.

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

    /// <summary><b>قُلِبَ بِاسمِه</b> (كانَ <c>Today_a_dispute_cancel_of_a_delivered_cash_deal_writes_cancelled_alone_and_names_no_refund_due</c>، ‏<c>b7fa68b</c>):
    /// حَسمُ النِزاعِ بِالإلغاءِ عَلى صَفقَةِ نَقدٍ بَعدَ «تَسليم» يَكتُبُ «أُلغِيَت» ثُمَّ «رَدٌّ نَقدِيٌّ مُستَحَقّ» بِاسمِ مَن حَسَم — مَرَّةً، بِلا مُزَوِّد — ويُسَمّيه
    /// <c>OwnerActionOf</c> لِصاحِبِ المَتجَر.</summary>
    [Fact]
    public void A_dispute_cancel_of_a_delivered_cash_deal_writes_one_cash_refund_due_line_and_names_it_for_the_store_owner()
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
        Assert.Equal(new[] { "cancelled", DealEvent.CashRefundDue }, WrittenFrom(stored, before));
        var line = stored.Timeline[^1];
        Assert.Equal((DealStage.Delivered, DealStage.Delivered, (Guid?)AppOwner, (string?)null), (line.StageBefore, line.StageAfter, line.ActorId, line.Note));
        Assert.Equal(DealNotices.OwnerAction.CashRefundDue, DealNotices.OwnerActionOf(stored));
    }

    /// <summary><b>قُلِبَ بِاسمِه</b> (كانَ <c>Today_the_store_owners_cancel_of_a_delivered_cash_deal_writes_cancelled_alone_and_names_no_refund_due</c>،
    /// ‏<c>b7fa68b</c>): وإلغاءُ صاحِبِ المَتجَرِ مِن الاستوديو بِالجِسمِ نَفسِه — الأَثَرُ الماليُّ واحِدٌ أَيّاً كانَ البابُ («بِأَثَرِ الإلغاءِ الماليِّ القائِم»).</summary>
    [Fact]
    public void The_store_owners_cancel_of_a_delivered_cash_deal_writes_one_cash_refund_due_line()
    {
        var (world, deals, id) = CashDealAt(DealStage.Delivered);
        var before = world.Read<Deal>(id.ToString())!.Timeline.Count;

        var res = Now(deals.CancelAsync(Slug, id, Owner, "مُرتَجَع"));

        Assert.True(res.Ok, res.Violation?.MessageAr);
        var stored = world.Read<Deal>(id.ToString())!;
        output.WriteLine($"إلغاءُ صاحِبِ المَتجَر: {stored.Status} · كُتِب: [{string.Join(", ", WrittenFrom(stored, before))}]");
        Assert.Equal(DealStatus.Cancelled, stored.Status);
        Assert.Equal(new[] { "cancelled", DealEvent.CashRefundDue }, WrittenFrom(stored, before));
        Assert.Equal(DealNotices.OwnerAction.CashRefundDue, DealNotices.OwnerActionOf(stored));
    }

    // ═══ السالِب — نَقدٌ لَم يُقبَض، وما لَيسَ نَقداً ═══════════════════════════════════════════════════

    /// <summary>صَفقَةُ نَقدٍ أُلغِيَت قَبلَ «تَسليم» لَم يُقبَض ثَمَنُها — لا سَطرَ رَدّ: صاحِبُ المَتجَرِ عِندَ «شَحن»، والمُشتَري عِندَ «حَجز».</summary>
    [Theory]
    [InlineData(DealStage.Shipping, true)]
    [InlineData(DealStage.Booked, false)]
    public void A_cash_deal_cancelled_before_delivery_writes_no_cash_refund_due(DealStage at, bool byStoreOwner)
    {
        var (world, deals, id) = CashDealAt(at);
        var before = world.Read<Deal>(id.ToString())!.Timeline.Count;

        var res = Now(deals.CancelAsync(Slug, id, byStoreOwner ? Owner : new DealCanceller(Buyer, "مُشتَرٍ", IsStoreAdmin: false), "عَدَلتُ"));

        Assert.True(res.Ok, res.Violation?.MessageAr);
        var stored = world.Read<Deal>(id.ToString())!;
        Assert.Equal(new[] { "cancelled" }, WrittenFrom(stored, before));
        Assert.Null(DealNotices.OwnerActionOf(stored));
    }

    private static Deal With(string? payMethod, DealStatus status, params string[] actions)
    {
        var d = new Deal { Id = Guid.NewGuid(), Pattern = "marketplace", Status = status, Stage = DealStage.Delivered, InitiatorId = Buyer, CounterpartyId = Seller };
        if (payMethod is not null) d.Attributes[CheckoutPayMethods.AttributeKey] = payMethod;
        foreach (var a in actions) d.Timeline.Add(new(DealStage.Delivered, DealStage.Delivered, a, Seller, "بائِع", null, DateTime.UtcNow));
        return d;
    }

    /// <summary><c>CollectedInCash</c> — المُوجِب: صَفقَةُ نَقدٍ بِسَطرِ قَبضِها؛ والسالِب: نَقدٌ بِلا سَطر، وسَطرٌ عَلى صَفقَةِ بِطاقَة، وصَفقَةٌ بِلا طَريقَة.</summary>
    [Fact]
    public void CollectedInCash_is_a_cash_deal_holding_its_cash_line_and_nothing_else()
    {
        Assert.True(DealsPolicy.CollectedInCash(With(CheckoutPayMethods.CashOnDelivery, DealStatus.Active, "advanced", DealEvent.CashOnDelivery)));
        Assert.False(DealsPolicy.CollectedInCash(With(CheckoutPayMethods.CashOnDelivery, DealStatus.Active, "advanced")));
        Assert.False(DealsPolicy.CollectedInCash(With(CheckoutPayMethods.Card, DealStatus.Active, DealEvent.CashOnDelivery)));
        Assert.False(DealsPolicy.CollectedInCash(With(null, DealStatus.Active, DealEvent.CashOnDelivery)));
    }

    /// <summary><c>OwnerActionOf</c> — المُوجِب: سَطرُ الرَدِّ عَلى صَفقَةٍ مُلغاة؛ والسالِب: عَلى صَفقَةٍ قائِمَة، والمُلغاةُ بِلا سَطر.</summary>
    [Fact]
    public void The_store_owner_is_named_for_a_cash_refund_due_on_a_cancelled_deal_alone()
    {
        Assert.Equal(DealNotices.OwnerAction.CashRefundDue,
            DealNotices.OwnerActionOf(With(CheckoutPayMethods.CashOnDelivery, DealStatus.Cancelled, DealEvent.CashOnDelivery, "cancelled", DealEvent.CashRefundDue)));
        Assert.Null(DealNotices.OwnerActionOf(With(CheckoutPayMethods.CashOnDelivery, DealStatus.Active, DealEvent.CashRefundDue)));
        Assert.Null(DealNotices.OwnerActionOf(With(CheckoutPayMethods.CashOnDelivery, DealStatus.Cancelled, DealEvent.CashOnDelivery, "cancelled")));
    }

    // ═══ الإشعارُ والصَفحَتان ═══════════════════════════════════════════════════════════════════════

    /// <summary>بَعدَ حَسمِ النِزاعِ بِالإلغاءِ عَلى صَفقَةِ نَقدٍ مَقبوضَة: الطَرَفانِ بِإشعارِ الحَسم كَما كانا، وصاحِبُ المَتجَرِ بِإشعارِ «رَدٌّ نَقدِيٌّ مُستَحَقّ» —
    /// مَرَّةً، إلى صَفحَتِها في الاستوديو، بِالمَبلَغِ وبِلا «ميسر».</summary>
    [Fact]
    public void After_a_dispute_cancel_the_store_owner_is_told_once_that_the_seller_owes_the_cash_back()
    {
        var (world, deals, id) = CashDealAt(DealStage.Delivered);
        world.Put(new ACommerce.Kit.Tenants.Tenant { Id = Slug, Name = "مَتجَر", OwnerUserId = AppOwner });
        Assert.True(Now(deals.DisputeAsync(Slug, id, AppOwner, "مالِك التَّطبيق", "بِضاعَةٌ مَعيبَة")).Ok);
        var res = Now(deals.ResolveDisputeAsync(Slug, id, Owner, DealDisputeResolution.Cancel, "بِضاعَةٌ مَعيبَة"));
        Assert.True(res.Ok, res.Violation?.MessageAr);
        var l = new L(new HttpContextAccessor());

        var told = Now(GatewayOwnerMoneyNoticeTests.Notices(world).NotifyDisputeResolvedAsync(Slug, res.Deal!, AppOwner, DealDisputeResolution.Cancel, l));

        Assert.Equal(2, told);
        var written = world.Stored.OfType<Notification>().ToList();
        var toOwner = Assert.Single(written, n => n.UserId == AppOwner);
        output.WriteLine($"لِلمالِك: «{toOwner.Title}» · «{toOwner.Body}» · {toOwner.RelatedUrl}");
        Assert.Equal(new[] { Buyer, Seller, AppOwner }, written.Select(n => n.UserId).ToArray());
        Assert.Equal(l["notifications.deal_cash_refund_due.title"], toOwner.Title);
        Assert.Equal(l.Format("notifications.deal_cash_refund_due.body", "سِلعَة", 120m), toOwner.Body);
        Assert.Equal(DealNotices.StudioDealUrl(Slug, id), toOwner.RelatedUrl);
        Assert.Equal(DealNotices.CancelledType, toOwner.Type);
        Assert.Contains("نَقداً", toOwner.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("ميسر", toOwner.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void Both_deal_pages_name_the_cash_refund_due_line_from_the_dictionary()
    {
        static string Page(string file) => File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot,
            "libs", "templates", "ACommerce.Templates.Customer.Marketplace", "Components", "Pages", file));
        Assert.Contains("DealEvent.CashRefundDue => L[\"deals.detail.event_cash_refund_due\"]", Page("MyDealDetail.razor"), StringComparison.Ordinal);
        Assert.Contains("DealEvent.CashRefundDue => L[\"studio.deal.event_cash_refund_due\"]", Page("StudioAppDealDetail.razor"), StringComparison.Ordinal);
        foreach (var key in new[] { "deals.detail.event_cash_refund_due", "studio.deal.event_cash_refund_due",
                                    "notifications.deal_cash_refund_due.title", "notifications.deal_cash_refund_due.body" })
        {
            var text = LocaleCatalog.Text(LocaleCatalog.Arabic, key);
            Assert.NotEqual(key, text);
            Assert.False(LocaleCatalog.IsPlaceholder(text), $"‏{key} نائِب.");
        }
    }
}
