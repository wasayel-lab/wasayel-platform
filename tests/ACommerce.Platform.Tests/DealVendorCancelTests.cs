using ACommerce.Kit.Notifications;
using ACommerce.Kit.Payments;
using ACommerce.Platform.I18n;
using ACommerce.Templates.Customer.Marketplace.Services;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ACommerce.Platform.Tests;

// ═══ البائِعُ المَربوطُ يُلغي عِندَ `Offered` — يُبقى ويُثَبَّت، والمُشتَري يُخبَر ═══════════════
//
// **المَقيسُ قَبلَ هذا الكوميت** (مُراجَعَةُ الموجَةِ الثانِيَة الثانِيَة، ‏2026-10-11): الرَبطُ عِندَ
// الوِلادَةِ (‏ADR-033 §٦‑ب‑١) جَعَلَ مالِكَ إعلانِ صَفقَةِ السَلَّةِ `CounterpartyId` مِن أَوَّلِ لَحظَة —
// و`DealCancelAuthorization.Validate` تُجيزُ الإلغاءَ لِأَيِّ طَرَف، فَالبائِعُ يُلغي الطَلَبَ عِندَ
// `Offered` قَبلَ أَن يَقبَلَه (وهو ما لَم يَكُن مُمكِناً لَه قَبلَ الرَبط: كانَ غَيرَ طَرَف). ولا صَفَّ
// يُثَبِّتُ ذلك، ونُقطَةُ `/cancel` لا تُشعِرُ أَحَداً (صِفرُ `notices.` في مَقطَعِها) — فَالمُشتَري لا
// يَعلَمُ أَنّ طَلَبَه رُفِضَ إلّا إن فَتَحَ صَفقَتَه.
//
// **والقَرار** (‏ADR-033 §٦‑ج مُلحَقاً): الإلغاءُ عِندَ `Offered` هُوَ رَفضُ البائِعِ لِلطَلَب — **يُبقى**،
// ويُثَبَّتُ بِمُوجِبٍ وسالِب، والمُشتَري يُخبَرُ بِالأُنبوبِ القائِم (`DealNotices`). ومَن يُلغي طَلَبَه هُوَ
// لا يُخبَرُ بِما فَعَل.
//
// **ولِماذا عالَمُ الوَثائِقِ في الذاكِرَة** (`EndpointDocWorld.cs`؛ القاعِدَة ٨): الخِدمَتانِ هُما هُما —
// `DealsService` بِتَسَلسُلِ `/checkout/submit` و`DealNotices` بِأُنبوبِها — فَيُقاسُ ما يُكتَبُ لا ما يُقالُ
// عَنه. الجَرَسُ مِن حاوِيَةِ SignalR بِلا خادِم، والدَفعُ الفَوريُّ يَقرَأُ مُستَخدِماً غَيرَ مَوجودٍ فَلا يُرسِل.

public class DealVendorCancelTests
{
    private const string Slug = "zz-vendor-cancel";
    private static readonly Guid Buyer    = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
    private static readonly Guid Seller   = Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb");
    private static readonly Guid Stranger = Guid.Parse("cccccccc-cccc-4ccc-8ccc-cccccccccccc");

    /// <summary>مُزَوِّدٌ يَرمي عِندَ أَيِّ نِداء — صَفقَةُ النَقدِ بِلا <c>payment_id</c> لا يَمَسُّ إلغاؤُها
    /// المال، فَإن مَسَّه أَحمَرَّ الصَفُّ بَدَلَ أَن يُحاكى.</summary>
    private sealed class ForbiddenPaymentProvider : IPaymentProvider
    {
        private static InvalidOperationException Touched() => new("إلغاءُ صَفقَةِ نَقدٍ لا يَمَسُّ المال — نودِيَ مُزَوِّدُ الدَفع.");
        public string ProviderName => "forbidden";
        public Task<PaymentResult> AuthorizeAsync(PaymentRequest req, string idempotencyKey, CancellationToken ct = default) => throw Touched();
        public Task<PaymentResult> CaptureAsync(string paymentId, decimal? amount = null, CancellationToken ct = default) => throw Touched();
        public Task<PaymentResult> RefundAsync(string paymentId, decimal amount, string reason, CancellationToken ct = default) => throw Touched();
        public Task<SubscriptionResult> CreateSubscriptionAsync(SubscriptionRequest req, string idempotencyKey, CancellationToken ct = default) => throw Touched();
        public Task<bool> CancelSubscriptionAsync(string subscriptionId, CancellationToken ct = default) => throw Touched();
        public Task<Invoice?> GetInvoiceAsync(string paymentId, CancellationToken ct = default) => throw Touched();
    }

    /// <summary>عالَمُ الوَثائِقِ يُتِمُّ كُلَّ انتِظارٍ في مَكانِه — وإن لَم يَكتَمِل فَالأَداةُ تَكذِب.</summary>
    private static T Now<T>(Task<T> task)
    {
        Assert.True(task.IsCompleted, "نِداءٌ لَم يَكتَمِل في عالَمٍ مُتَزامِن — الأَداةُ تَكذِب.");
        return task.GetAwaiter().GetResult();
    }

    private sealed record World(DocWorld Docs, DealsService Deals, DealNotices Notices, L L);

    private static World NewWorld()
    {
        var docs = new DocWorld();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSignalR();
        var hub = services.BuildServiceProvider().GetRequiredService<IHubContext<ACommerce.Kit.Realtime.Server.RealtimeHub>>();
        var push = new WebPushService(new ConfigurationBuilder().Build(), NullLogger<WebPushService>.Instance);
        return new World(docs, new DealsService(docs.Store, new ForbiddenPaymentProvider()),
            new DealNotices(docs.Store, hub, push), new L(new HttpContextAccessor()));
    }

    /// <summary>صَفقَةُ سَلَّةٍ نَقدِيَّةٌ كَما يُولِدُها <c>/checkout/submit</c> — <c>StartAsync</c> ثُمَّ مَرجِعُ
    /// المالِكِ ثُمَّ <c>BindListingOwnerAsync</c>: البائِعُ طَرَفٌ مِن الوِلادَةِ والصَفقَةُ في <c>Offered</c>.</summary>
    private static Guid CartDeal(World w)
    {
        var deal = Now(w.Deals.StartAsync(Slug, "marketplace", Buyer, "مُشتَرٍ", null, "سِلعَة", 100m,
            new() { [CheckoutPayMethods.AttributeKey] = CheckoutPayMethods.CashOnDelivery }));
        Now(w.Deals.AttachRefAsync(Slug, deal.Id, DealsService.ListingOwnerRef, Seller.ToString()));
        Now(w.Deals.BindListingOwnerAsync(Slug, deal.Id, Seller.ToString()));
        return deal.Id;
    }

    /// <summary>صَفقَةُ عَرضٍ عَلى إعلانٍ كَما يُولِدُها <c>/{slug}/listings/{id}/deal</c> — <c>StartAsync</c> ثُمَّ
    /// مَرجِعُ المالِكِ <b>بِلا رَبط</b>: المالِكُ مَعروفٌ ولَيسَ طَرَفاً حَتّى يَقبَل.</summary>
    private static Guid OfferDeal(World w)
    {
        var deal = Now(w.Deals.StartAsync(Slug, "marketplace", Buyer, "مُشتَرٍ", Guid.NewGuid(), "سِلعَة", 100m));
        Now(w.Deals.AttachRefAsync(Slug, deal.Id, "listing_owner", Seller.ToString()));
        return deal.Id;
    }

    private static int NotificationsFor(World w, Guid user)
        => w.Docs.Stored.OfType<Notification>().Count(n => n.UserId == user);

    // ─── المُوجِب — البائِعُ المَربوطُ يُلغي عِندَ `Offered`، والمُشتَري يُخبَرُ مَرَّة ─────────────

    [Fact]
    public void A_bound_vendor_cancelling_at_Offered_is_allowed_and_the_buyer_is_told_once()
    {
        var w = NewWorld();
        var id = CartDeal(w);
        Assert.Equal(Seller, w.Docs.Read<Deal>(id.ToString())!.CounterpartyId);
        Assert.Equal(DealStage.Offered, w.Docs.Read<Deal>(id.ToString())!.Stage);

        var res = Now(w.Deals.CancelAsync(Slug, id, new DealCanceller(Seller, "بائِع", IsStoreAdmin: false), "نَفِدَت الكَمِّيَّة"));
        Assert.True(res.Ok, res.Violation?.MessageAr);
        var deal = w.Docs.Read<Deal>(id.ToString())!;
        Assert.Equal(DealStatus.Cancelled, deal.Status);
        Assert.Equal(DealStage.Offered, deal.Stage);
        var last = deal.Timeline[^1];
        Assert.Equal(("cancelled", (Guid?)Seller, "نَفِدَت الكَمِّيَّة"), (last.Action, last.ActorId, last.Note));

        Assert.True(Now(w.Notices.NotifyCancelledAsync(Slug, res.Deal!, Seller, "بائِع", w.L)));
        var told = w.Docs.Stored.OfType<Notification>().ToList();
        Assert.Single(told);
        Assert.Equal(Buyer, told[0].UserId);
        Assert.Equal(DealNotices.CancelledType, told[0].Type);
        Assert.Equal($"/{Slug}/deals/{id}", told[0].RelatedUrl);
        Assert.Equal(0, NotificationsFor(w, Seller));
    }

    // ─── السالِب — غَريبٌ يُرَدُّ ولا يُكتَبُ شَيء ─────────────────────────────────────────

    [Fact]
    public void A_stranger_cannot_cancel_a_bound_cart_deal_at_Offered_and_nothing_is_written()
    {
        var w = NewWorld();
        var id = CartDeal(w);
        var savesBefore = w.Docs.SaveCalls;

        var res = Now(w.Deals.CancelAsync(Slug, id, new DealCanceller(Stranger, "غَريب", IsStoreAdmin: false), "عَبَث"));
        Assert.False(res.Ok);
        Assert.Equal(DealCancelAuthorization.ActorNotParty, res.Violation!.Code);
        Assert.Equal(savesBefore, w.Docs.SaveCalls);
        var deal = w.Docs.Read<Deal>(id.ToString())!;
        Assert.Equal(DealStatus.Active, deal.Status);
        Assert.DoesNotContain(deal.Timeline, e => e.Action == "cancelled");
        Assert.Equal(0, w.Docs.Wrote<Notification>());
    }

    /// <summary>ومَن يُلغي طَلَبَه هُوَ لا يُخبَرُ بِما فَعَل — ولا يُشعَرُ البائِعُ بِه أَيضاً (لا يُطلَبُ هُنا).</summary>
    [Fact]
    public void The_buyer_cancelling_his_own_order_tells_nobody()
    {
        var w = NewWorld();
        var id = CartDeal(w);
        var res = Now(w.Deals.CancelAsync(Slug, id, new DealCanceller(Buyer, "مُشتَرٍ", IsStoreAdmin: false), "عَدَلتُ"));
        Assert.True(res.Ok, res.Violation?.MessageAr);

        Assert.False(Now(w.Notices.NotifyCancelledAsync(Slug, res.Deal!, Buyer, "مُشتَرٍ", w.L)));
        Assert.Equal(0, w.Docs.Wrote<Notification>());
        Assert.False(DealNotices.TellsTheBuyer(res.Deal!, Buyer));
        Assert.True(DealNotices.TellsTheBuyer(res.Deal!, Seller));
    }

    // ─── «اليَوم» — مالِكُ إعلانِ العَرضِ غَيرُ المَربوطِ لا يُلغيه ────────────────────────────

    /// <summary><b>اليَوم</b> (يُقالُ ولا يُبتلَع؛ سِجِلُّ المالِكِ «ب»): في مَسارِ العَرضِ عَلى إعلانٍ لا رَبطَ
    /// عِندَ الوِلادَة، فَالمالِكُ لَيسَ طَرَفاً حَتّى يَقبَل — وإلغاؤُه (رَفضُه لِلعَرض) يُرَدُّ بِـ<c>actor_not_party</c>،
    /// وصَفحَةُ الصَفقَةِ تَرسُمُ لَه استِمارَةَ الإلغاءِ مَعَ ذلك. يُقلَبُ بِاسمِه يَومَ يُقَرَّرُ بابُ رَفضِ العَرض.</summary>
    [Fact]
    public void Today_an_unbound_listing_owner_cannot_cancel_an_offer_on_his_listing()
    {
        var w = NewWorld();
        var id = OfferDeal(w);
        Assert.Null(w.Docs.Read<Deal>(id.ToString())!.CounterpartyId);
        Assert.Equal(Seller, DealAcceptAuthorization.OwnerIdOf(w.Docs.Read<Deal>(id.ToString())!));

        var res = Now(w.Deals.CancelAsync(Slug, id, new DealCanceller(Seller, "بائِع", IsStoreAdmin: false), "لا أَبيع"));
        Assert.False(res.Ok);
        Assert.Equal(DealCancelAuthorization.ActorNotParty, res.Violation!.Code);
        Assert.Equal(DealStatus.Active, w.Docs.Read<Deal>(id.ToString())!.Status);
    }
}
