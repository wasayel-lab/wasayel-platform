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

    /// <summary><b>اليَوم</b> (‏2026-10-29، تَوصيفٌ قَبلَ قَرارِ المالِك د8 «أ»: «إشعارٌ واحِدٌ لِلبائِعِ حينَ يُلغى طَلَبٌ عِندَه، أَيّاً كانَ مَن أَلغاه
    /// وأَيّاً كانَ المَسار، ما لَم يَكُن هُوَ المُلغي»): إدارَةُ المَتجَرِ تُلغي مِن الاستوديو طَلَباً بائِعُه غَيرُها — المُشتَري يُخبَرُ مَرَّة، والبائِعُ لا
    /// يُخبَرُ بِشَيءٍ فَيُجَهِّزُ طَلَباً مُلغًى. ومَعَه صَفُّ إلغاءِ المُشتَري أَدناه (<c>The_buyer_cancelling_his_own_order_tells_nobody</c>) — يُقلَبانِ
    /// بِاسمَيهِما في كوميتِ إشعارِ البائِع.</summary>
    [Fact]
    public void Today_a_store_cancel_of_a_vendors_order_tells_the_buyer_and_never_the_vendor()
    {
        var w = NewWorld();
        var id = CartDeal(w);
        var res = Now(w.Deals.CancelAsync(Slug, id, new DealCanceller(Stranger, "مالِك التَّطبيق", IsStoreAdmin: true), "إلغاء إداريّ"));
        Assert.True(res.Ok, res.Violation?.MessageAr);

        Assert.True(Now(w.Notices.NotifyCancelledByStoreAsync(Slug, res.Deal!, Stranger, w.L)));
        Assert.Equal(1, NotificationsFor(w, Buyer));
        Assert.Equal(0, NotificationsFor(w, Seller));
        Assert.Equal(1, w.Docs.Wrote<Notification>());
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

    // ─── الشاشَة: استِمارَةُ الإلغاءِ في صَفحَةِ الصَفقَة — لِمَن تُرسَم؟ (القاعِدَة ١٢) ─────────────────────
    //
    // مُراجَعَةُ تَيّارِ S6a الأولى (‏2026-10-12): صَفحَةُ الصَفقَةِ (`MyDealDetail.razor`) تُفتَحُ لِلمُبادِرِ ولِمالِكِ الإعلان
    // (`isInitiator || isListingOwner`) وتَرسُمُ استِمارَةَ `/cancel` لِكُلِّ صَفقَةٍ فَعّالَةٍ بِلا شَرطٍ ثانٍ؛ والنُقطَةُ تَسأَلُ
    // `DealCancelAuthorization.Validate` (طَرَفٌ أَو مُشرِف) — فَمالِكُ إعلانِ العَرضِ قَبلَ أَن يَقبَلَ يَرى استِمارَةً تُرَدُّ
    // بِـ`actor_not_party`: بابٌ يَقولُ «لا». وصَفحَةُ التَقييمِ نَفسُها تَرسُمُ استِمارَتَها مِن بَوّابَتِها (`canReview`).

    private static string MemberDealPage()
    {
        var path = Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, "libs", "templates", "ACommerce.Templates.Customer.Marketplace",
            "Components", "Pages", "MyDealDetail.razor");
        Assert.True(File.Exists(path), $"مَصدَرٌ مَفقود: {path} — الأَداةُ عَمياء.");
        var text = WriteEndpointGuardTests.StripMarkupComments(File.ReadAllText(path));
        Assert.True(text.Length > 1000, "أَداةٌ عَمياء: `MyDealDetail.razor` لَم يُقرَأ.");
        return text;
    }

    private const string CancelForm = "<form method=\"post\" action=\"@($\"/{Slug}/deals/{deal.Id}/cancel\")\"";

    /// <summary>مَن يَفتَحُ الصَفحَةَ في كُلِّ صَفقَةٍ ومَوقِعُه — المُبادِر، والبائِعُ المَربوطُ في صَفقَةِ السَلَّة، ومالِكُ إعلانِ العَرضِ
    /// قَبلَ القَبول — وحارِسُ الصَفحَةِ كَما في مَصدَرِها: مُبادِرٌ أَو مالِكُ الإعلانِ (بِالمَرجِعِ أَو الطَرَف).</summary>
    private static IEnumerable<(string Name, Deal Deal, Guid Viewer)> Viewers()
    {
        var w = NewWorld();
        var cart = w.Docs.Read<Deal>(CartDeal(w).ToString())!;
        var offer = w.Docs.Read<Deal>(OfferDeal(w).ToString())!;
        foreach (var (name, deal) in new[] { ("cart", cart), ("offer", offer) })
        foreach (var viewer in new[] { Buyer, Seller })
        {
            var pageOpens = deal.InitiatorId == viewer || DealAcceptAuthorization.OwnerIdOf(deal) == viewer || deal.CounterpartyId == viewer;
            Assert.True(pageOpens, $"{name}: الناظِرُ لا يَفتَحُ الصَفحَة — الأَداةُ تَقيسُ غَيرَ ما تُريد.");
            yield return ($"{name}/{(viewer == Buyer ? "buyer" : "owner")}", deal, viewer);
        }
    }

    /// <summary><b>قُلِبَ بِاسمِه</b> (مُراجَعَةُ تَيّارِ S6a الأولى، ‏2026-10-12): كانَ
    /// <c>Today_the_member_deal_page_draws_the_cancel_form_to_the_unbound_listing_owner_whom_the_endpoint_refuses</c> (مُودَعٌ في
    /// <c>8b891ed</c>) — الاستِمارَةُ تَلي سِلسِلَةَ الإجراءاتِ بِلا حارِسٍ خاصّ، فَتُرسَمُ لِلناظِرينَ الأَربَعَةِ وتَرُدُّ النُقطَةُ «offer/owner».
    /// <b>وصارَت تُرسَمُ بِـ<c>canCancel</c> = <see cref="DealCancelAuthorization.IsAllowed"/> بِسِجِلِّ السُلطَةِ الَّذي تَبنيه النُقطَةُ
    /// نَفسُه</b> (<c>IsStoreAdmin: false</c>): ثَلاثَةٌ تُرسَمُ لَهُم ويُلغونَ، ومالِكُ العَرضِ قَبلَ القَبولِ لا تُرسَمُ لَه — وصِفرُ بابٍ يُرَدّ.
    /// (وسُؤالُ «أَيَرفُضُ المالِكُ العَرض؟» باقٍ لِلمالِكِ في «ب» S6a·٨: إن صارَ نَعَم فَفي <c>Validate</c>، والصَفحَةُ تَتبَعُها.)</summary>
    [Fact]
    public void The_member_deal_page_draws_the_cancel_form_exactly_for_whom_the_endpoint_cancels()
    {
        var page = MemberDealPage();
        var form = page.IndexOf(CancelForm, StringComparison.Ordinal);
        Assert.True(form > 0, "استِمارَةُ الإلغاءِ غَيرُ مَوجودَة — الأَداةُ عَمياء.");
        var head = page[..form].TrimEnd();
        Assert.EndsWith("{", head, StringComparison.Ordinal);
        Assert.EndsWith("@if (canCancel)", head[..^1].TrimEnd(), StringComparison.Ordinal);
        Assert.Contains("canCancel = DealCancelAuthorization.IsAllowed(deal, new DealCanceller(uid, Auth.UserName ?? \"\", IsStoreAdmin: false));",
            page, StringComparison.Ordinal);

        // والنُقطَةُ تَبني السِجِلَّ نَفسَه — `IsStoreAdmin: false` — فَما تُجيزُه الشاشَةُ تُجيزُه هِيَ.
        var endpoints = File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, "libs", "templates",
            "ACommerce.Templates.Customer.Marketplace", "MarketplaceTemplateExtensions.cs"));
        var at = endpoints.IndexOf("\"/{slug}/deals/{id:guid}/cancel\"", StringComparison.Ordinal);
        Assert.True(at > 0, "نُقطَةُ `/cancel` غَيرُ مَوجودَة — الأَداةُ عَمياء.");
        var cancel = endpoints[at..endpoints.IndexOf(".RequireStoreWritable();", at, StringComparison.Ordinal)];
        Assert.Contains("new Services.Deals.DealCanceller(userId, userName, IsStoreAdmin: false)", cancel, StringComparison.Ordinal);

        var drawnButRefused = new List<string>();
        var drawn = 0;
        var checkedViewers = 0;
        foreach (var (name, deal, viewer) in Viewers())
        {
            checkedViewers++;
            var draws = deal.Status == DealStatus.Active
                        && DealCancelAuthorization.IsAllowed(deal, new DealCanceller(viewer, "ناظِر", IsStoreAdmin: false));
            var refused = DealCancelAuthorization.Validate(deal, new DealCanceller(viewer, "ناظِر", IsStoreAdmin: false)) is not null;
            if (draws) drawn++;
            if (draws && refused) drawnButRefused.Add(name);
        }
        Assert.Equal(4, checkedViewers);
        Assert.Equal(3, drawn);
        Assert.Empty(drawnButRefused);
        Assert.False(DealCancelAuthorization.IsAllowed(Viewers().Single(v => v.Name == "offer/owner").Deal,
            new DealCanceller(Seller, "بائِع", IsStoreAdmin: false)));
    }
}
