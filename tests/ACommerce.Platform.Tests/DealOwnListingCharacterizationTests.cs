using ACommerce.Kit.Payments;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using Xunit;

namespace ACommerce.Platform.Tests;

// ═══ مُشتَرٍ يَشتَري إعلانَه هُوَ — تَوصيفُ «اليَوم» قَبلَ أَن يُمنَعَ رَبطُه طَرَفاً لِنَفسِه ═══
//
// مُراجَعَةُ ‏2026-10-11 الثالِثَةُ عَلى `8769fa4` (البَندُ الكَبير): `DealsService.BindListingOwnerAsync`
// تَربُطُ مالِكَ الإعلانِ طَرَفاً مُقابِلاً **ولَو كانَ هُوَ المُبادِرَ نَفسَه** — فَتولَدُ صَفقَةٌ
// `InitiatorId == CounterpartyId`، وهُوَ ما يَنفيه رَمزُ `DealAcceptAuthorization.ActorIsInitiator`
// بِحَرفِه: «ولا يَكونُ المَرءُ طَرَفَي صَفقَتِه». وبَوّابَةُ الفاعِلِ (`IsActorAllowed`) تُجيزُه في
// `initiator` و`counterparty` و`either` جَميعاً، فَيَمشي صَفقَتَه وَحدَه إلى `Completed` — ثُمَّ
// يُقَيِّمُ نَفسَه (نُقطَةُ التَقييمِ تَختارُ `CounterpartyId` هَدَفاً لِلمُبادِر). وقَبلَ `8769fa4`
// كانَت الصَفقَةُ نَفسُها بِلا طَرَفٍ فَتَقِفُ عِندَ `Booked`.
//
// **والبابُ إلَيها — مَقيسٌ بِالقِراءَة**: `POST /{slug}/listings/{id}/cart/add` لا يَسأَلُ عَن
// المالِكِ أَصلاً؛ والشاشَةُ تُخفي زِرَّ السَلَّةِ عَن المالِكِ (`TenantListingDetail.razor`:
// `@if (!isOwner && !isOfferBased)`) و`curl` لا يَرى الشاشَة؛ ومَسارُ العَرضِ وَحدَه يَرُدُّ المالِكَ
// (`?err=self`).
//
// **ولِماذا عالَمُ الوَثائِقِ في الذاكِرَة** (`EndpointDocWorld.cs`؛ القاعِدَة ٨ — الأَداةُ القائِمَة):
// الخِدمَةُ هي هي (`DealsService` الحَقيقِيَّة) بِتَسَلسُلِ `/checkout/submit` نَفسِه — `StartAsync` ثُمَّ
// مَرجِعُ المالِكِ ثُمَّ `BindListingOwnerAsync` — والمَشيُ بِبابِه الحَقيقِيّ (`AdvanceAsync`)؛ فَيُقاسُ
// ما يُكتَبُ لا ما يُقالُ عَنه.
//
// **وما يُقلَبُ وما لا يُقلَب** (القاعِدَة ٣): صَفّا «اليَوم» الأَوَّلانِ يُقلَبانِ بِاسمِهِما في كوميتِ
// الإصلاح؛ وصَفُّ المالِكِ الآخَرِ يَبقى بِحَرفِه — الرَبطُ لِبائِعٍ غَيرِ المُشتَري لا يَتَغَيَّر. وصَفُّ
// `cart/add` يَبقى «اليَوم» بَعدَ الإصلاح: لا يَمَسُّه، ويُقلَبُ بِاسمِه يَومَ يُقَرَّرُ بابُه.

public class DealOwnListingCharacterizationTests
{
    private const string Slug = "zz-own-listing";
    private static readonly Guid Buyer  = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
    private static readonly Guid Seller = Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb");

    private static string RepoRoot => ThemeZeroEquivalenceTests.RepoRoot;
    private const string Template = "libs/templates/ACommerce.Templates.Customer.Marketplace";

    private static string Source(params string[] parts)
    {
        var path = Path.Combine(RepoRoot, Path.Combine(parts));
        Assert.True(File.Exists(path), $"مَصدَرٌ مَفقود: {path} — الأَداةُ عَمياءُ بِلا طَرَفٍ مَقروء.");
        var text = File.ReadAllText(path);
        Assert.True(text.Length > 500, $"أَداةٌ عَمياء: {path} طولُه {text.Length} مِحرَفاً — لَم يُقرَأ.");
        return text;
    }

    private static string Segment(string source, string from, string to)
    {
        var start = source.IndexOf(from, StringComparison.Ordinal);
        Assert.True(start > 0, $"‏`{from}` غَيرُ مَوجود — الأَداةُ عَمياء.");
        var end = source.IndexOf(to, start + from.Length, StringComparison.Ordinal);
        Assert.True(end > start, $"‏`{to}` لا يَتلو `{from}` — الأَداةُ عَمياء.");
        return WriteEndpointGuardTests.StripComments(source.Substring(start, end - start));
    }

    private static string Endpoint(string route)
        => Segment(Source(Template, "MarketplaceTemplateExtensions.cs"), "\"" + route + "\"", ".RequireStoreWritable();");

    private static string ListingPage()
        => WriteEndpointGuardTests.StripMarkupComments(Source(Template, "Components", "Pages", "TenantListingDetail.razor"));

    /// <summary>مُزَوِّدٌ يَرمي عِندَ أَيِّ نِداء — صَفقَةُ النَقدِ لا تَمَسُّ المالَ في الوِلادَةِ ولا في
    /// المَشي، فَإن مَسَّته أَحمَرَّ الصَفُّ بَدَلَ أَن يُحاكى.</summary>
    private sealed class ForbiddenPaymentProvider : IPaymentProvider
    {
        private static InvalidOperationException Touched() => new("صَفقَةُ النَقدِ لا تَمَسُّ المال — نودِيَ مُزَوِّدُ الدَفع.");
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

    /// <summary>صَفقَةُ سَلَّةٍ نَقدِيَّةٌ كَما يُولِدُها <c>/checkout/submit</c> لِبَندٍ مالِكُ إعلانِه
    /// <paramref name="owner"/> ومُشتَريه <paramref name="buyer"/> — بِالخِدمَةِ نَفسِها وبِتَرتيبِ
    /// النُقطَة: <c>StartAsync</c> ثُمَّ مَرجِعُ المالِكِ ثُمَّ <c>BindListingOwnerAsync</c>.</summary>
    private static (DealsService Deals, DocWorld World, Guid Id) CheckoutDeal(Guid buyer, Guid owner)
    {
        var world = new DocWorld();
        var deals = new DealsService(world.Store, new ForbiddenPaymentProvider());
        var deal = Now(deals.StartAsync(Slug, "marketplace", buyer, "مُشتَرٍ", null, "سِلعَة", 100m,
            new() { [CheckoutPayMethods.AttributeKey] = CheckoutPayMethods.CashOnDelivery }));
        Now(deals.AttachRefAsync(Slug, deal.Id, DealsService.ListingOwnerRef, owner.ToString()));
        Now(deals.BindListingOwnerAsync(Slug, deal.Id, owner.ToString()));
        return (deals, world, deal.Id);
    }

    // ═══ ١) «اليَوم» — يُقلَبانِ بِاسمِهِما في كوميتِ الإصلاح ═══════════════════════════

    /// <summary><b>اليَوم</b>: مُشتَرٍ أَضافَ إعلانَه هُوَ إلى سَلَّتِه يُربَطُ طَرَفاً مُقابِلاً لِنَفسِه عِندَ
    /// الوِلادَة — المُبادِرُ والطَرَفُ واحِد، وسَطرُ <c>bound</c> بِاسمِه.</summary>
    [Fact]
    public void Today_a_cart_deal_on_the_buyers_own_listing_binds_him_as_his_own_counterparty()
    {
        var (_, world, id) = CheckoutDeal(Buyer, Buyer);

        var deal = world.Read<Deal>(id.ToString())!;
        Assert.Equal(Buyer, deal.InitiatorId);
        Assert.Equal(Buyer, deal.CounterpartyId);
        Assert.Equal(new[] { "created", "note", DealEvent.BoundAtBirth },
            deal.Timeline.Select(e => e.Action).ToArray());
        Assert.Equal(Buyer, deal.Timeline[^1].ActorId);
    }

    /// <summary><b>اليَوم</b>: مُستَخدِمُها الوَحيدُ يَمشيها وَحدَه بِبابِ المَشيِ العامّ — كُلُّ صَفٍّ في
    /// الكاتالوجِ يُسَمّيه (<c>initiator</c> و<c>counterparty</c> و<c>either</c>) — مِن <c>Offered</c> إلى
    /// <c>Reviewed</c> فَـ<c>Completed</c>، بِلا قَبولٍ ولا طَرَفٍ ثانٍ.</summary>
    [Fact]
    public void Today_the_single_user_of_such_a_deal_walks_it_alone_from_Offered_to_Completed()
    {
        var (deals, world, id) = CheckoutDeal(Buyer, Buyer);

        var path = new List<string>();
        for (var step = 0; step < 5; step++)
        {
            var moved = Now(deals.AdvanceAsync(Slug, id, Buyer, "مُشتَرٍ"));
            Assert.True(moved.Ok, moved.Reason);
            path.Add($"{moved.Deal!.Stage}/{moved.Deal.Status}");
        }

        Assert.Equal(new[] { "Booked/Active", "Confirmed/Active", "Shipping/Active", "Delivered/Active", "Reviewed/Completed" },
            path.ToArray());
        var after = world.Read<Deal>(id.ToString())!;
        Assert.Equal(DealStatus.Completed, after.Status);
        Assert.All(after.Timeline.Where(e => e.Action == "advanced"), e => Assert.Equal(Buyer, e.ActorId));
    }

    // ═══ ٢) ما لا يَمَسُّه الإصلاح ══════════════════════════════════════════════════

    /// <summary><b>المُوجِب، ويَبقى بِحَرفِه</b>: بَندٌ مالِكُه غَيرُ المُشتَري يُربَطُ مالِكُه طَرَفاً
    /// عِندَ الوِلادَةِ بِسَطرِ <c>bound</c> بِاسمِه، والصَفقَةُ في <c>Offered</c> تَنتَظِرُ قَبولَه.</summary>
    [Fact]
    public void A_cart_deal_on_another_owners_listing_binds_that_owner_at_birth_with_his_own_line()
    {
        var (_, world, id) = CheckoutDeal(Buyer, Seller);

        var deal = world.Read<Deal>(id.ToString())!;
        Assert.Equal(Buyer, deal.InitiatorId);
        Assert.Equal(Seller, deal.CounterpartyId);
        Assert.Equal(DealStage.Offered, deal.Stage);
        Assert.Equal(DealStatus.Active, deal.Status);
        var bound = deal.Timeline.Single(e => e.Action == DealEvent.BoundAtBirth);
        Assert.Equal(Seller, bound.ActorId);
    }

    /// <summary><b>اليَوم، ويَبقى بَعدَ الإصلاح</b>: <c>cart/add</c> يَأخُذُ أَيَّ إعلانٍ — لا يَقرَأُ
    /// <c>owner_id</c> ولا يَرُدُّ — والشاشَةُ وَحدَها تُخفي زِرَّ السَلَّةِ عَن مالِكِه؛ ومَسارُ العَرضِ
    /// يَرُدُّه بِـ<c>?err=self</c>. فَالبابُ إلى صَفقَةٍ عَلى إعلانِ المُشتَري نَفسِه طَلَبٌ مَصنوعٌ لا نَقرَة —
    /// ويُقلَبُ هذا الصَفُّ بِاسمِه يَومَ يُقَرَّرُ بابُه.</summary>
    [Fact]
    public void Today_cart_add_takes_any_listing_and_only_the_listing_page_hides_the_button_from_its_owner()
    {
        var cartAdd = Endpoint("/{slug}/listings/{id:guid}/cart/add");
        Assert.Contains("var listing = await s.LoadAsync<Listing>(id);", cartAdd, StringComparison.Ordinal);
        Assert.DoesNotContain("owner_id", cartAdd, StringComparison.Ordinal);
        Assert.DoesNotContain("err=self", cartAdd, StringComparison.Ordinal);

        Assert.Contains("?err=self", Endpoint("/{slug}/listings/{id:guid}/deal"), StringComparison.Ordinal);

        var page = ListingPage();
        var hide = page.IndexOf("@if (!isOwner && !isOfferBased)", StringComparison.Ordinal);
        var cart = page.IndexOf("/cart/add\")", StringComparison.Ordinal);
        Assert.True(hide > 0 && cart > hide, $"زِرُّ السَلَّةِ لا يَقَعُ تَحتَ حارِسِ المالِك: hide={hide} cart={cart}");
    }
}
