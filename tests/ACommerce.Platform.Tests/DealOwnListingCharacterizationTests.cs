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
//
// **ثُمَّ قُلِبَ صَفّا «اليَوم» في الكوميتِ التالي** (‏ADR-033 §٦‑ب‑١ مُلحَقاً، ‏2026-10-11):
// `BindListingOwnerAsync` لا تَربُطُ مالِكاً هُوَ المُبادِر، فَتولَدُ الصَفقَةُ بِلا طَرَفٍ ويَقِفُ
// مُستَخدِمُها عِندَ `Booked` ويَرُدُّه القَبولُ بِـ`actor_is_initiator` — كُلٌّ بِاسمٍ جَديدٍ يَقولُ
// النَقيضَ ويَحمِلُ «قُلِبَ» في شَرحِه، لا يُحذَفُ ولا يُسكَت. وقِسمُ ما لا يُقلَبُ بِحَرفِه: بَصمَتُه
// قَبلَ الإصلاحِ وبَعدَه واحِدَة.
//
// **ثُمَّ في تَيّارِ S6a (‏2026-10-11) — ولا يُعادُ حَرفٌ فَوقَه**: قُلِبَ صَفُّ «اليَوم» الباقي بِاسمِه —
// `cart/add` يَرُدُّ المالِكَ بِـ`?err=self` وتَقولُه صَفحَةُ الإعلان؛ وقُلِبَ «يَقِفُ عِندَ Booked» إلى
// «لا يُخرِجُها مِن Offered» حينَ صارَ الخُروجُ يَسأَلُ فاعِلَ المَرحَلَةِ المَدخولَة (‏ADR-033 §٦‑ط).

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

    // ═══ ١) ما كانَ «اليَوم» — قُلِبا بِاسمِهِما (‏ADR-033 §٦‑ب‑١ مُلحَقاً) ═══════════════

    /// <summary><b>قُلِبَ</b> (المُراجَعَةُ الثالِثَةُ عَلى <c>8769fa4</c>، ‏2026-10-11؛ ‏ADR-033 §٦‑ب‑١
    /// مُلحَقاً): كانَ مُشتَرٍ أَضافَ إعلانَه هُوَ إلى سَلَّتِه يُربَطُ طَرَفاً مُقابِلاً لِنَفسِه عِندَ
    /// الوِلادَة — المُبادِرُ والطَرَفُ واحِد، وسَطرُ <c>bound</c> بِاسمِه. وصارَ لا يُربَطُ أَحَد: الصَفقَةُ
    /// تولَدُ بِلا طَرَفٍ ولا سَطرِ <c>bound</c> كَما كانَت قَبلَ <c>8769fa4</c>، ومَرجِعُ المالِكِ باقٍ
    /// عَلَيها — المالِكُ مَعروفٌ ولَم يُربَط.</summary>
    [Fact]
    public void A_cart_deal_on_the_buyers_own_listing_is_not_bound_and_is_born_without_a_counterparty()
    {
        var (_, world, id) = CheckoutDeal(Buyer, Buyer);

        var deal = world.Read<Deal>(id.ToString())!;
        Assert.Equal(Buyer, deal.InitiatorId);
        Assert.Null(deal.CounterpartyId);
        Assert.Null(deal.CounterpartyName);
        Assert.Equal(new[] { "created", "note" },
            deal.Timeline.Select(e => e.Action).ToArray());
        Assert.Equal(Buyer, DealAcceptAuthorization.OwnerIdOf(deal));
        Assert.Equal(DealStage.Offered, deal.Stage);
        Assert.Equal(DealStatus.Active, deal.Status);
    }

    /// <summary><b>قُلِبَ</b> (المُراجَعَةُ الثالِثَةُ عَلى <c>8769fa4</c>، ‏2026-10-11؛ ‏ADR-033 §٦‑ب‑١
    /// مُلحَقاً): كانَ مُستَخدِمُها الوَحيدُ يَمشيها وَحدَه بِبابِ المَشيِ العامّ — كُلُّ صَفٍّ في الكاتالوجِ
    /// يُسَمّيه — مِن <c>Offered</c> إلى <c>Reviewed</c> فَـ<c>Completed</c> بِخَمسِ نَقَرات. وصارَ يُحَرِّكُ
    /// <c>Offered → Booked</c> وَحدَها (صَفُّ <c>initiator</c> — سُلوكٌ قائِمٌ يُقالُ في §٦‑ج) ثُمَّ يُرَدُّ:
    /// <c>Booked</c> تَطلُبُ <c>counterparty</c> ولا طَرَف، والقَبولُ يَرُدُّه بِـ<c>actor_is_initiator</c>
    /// — فَلا تَبلُغُ <c>Completed</c> ولا يَكونُ لَها هَدَفُ تَقييم.
    /// <para><b>وقُلِبَ ثانِيَةً بِاسمِه</b> (‏ADR-033 §٦‑ط، ‏2026-10-11): كانَ
    /// <c>The_buyer_of_his_own_listing_stops_at_Booked_and_cannot_accept_his_own_order</c> — يُحَرِّكُ
    /// <c>Offered → Booked</c> بِصَفِّ <c>initiator</c> (فاعِلِ المَرحَلَةِ الحالِيَّة). وصارَ الخُروجُ مِن <c>Offered</c>
    /// دُخولَ <c>Booked</c> وفاعِلُها الطَرَفُ المُقابِل — ولا طَرَفَ لِصَفقَتِه — فَلا يُحَرِّكُها خُطوَةً: تَبقى
    /// في <c>Offered</c> بِلا سَطرِ <c>advanced</c>، والقَبولُ يَرُدُّه كَما كان.</para></summary>
    [Fact]
    public void The_buyer_of_his_own_listing_cannot_move_it_out_of_Offered_and_cannot_accept_his_own_order()
    {
        var (deals, world, id) = CheckoutDeal(Buyer, Buyer);

        var booked = Now(deals.AdvanceAsync(Slug, id, Buyer, "مُشتَرٍ"));
        Assert.False(booked.Ok);
        Assert.Contains("counterparty", booked.Reason ?? "", StringComparison.Ordinal);

        var accept = Now(deals.AcceptAsync(Slug, id, new DealAcceptor(Buyer, "مُشتَرٍ")));
        Assert.False(accept.Ok);
        Assert.Equal(DealAcceptAuthorization.ActorIsInitiator, accept.Violation!.Code);

        var after = world.Read<Deal>(id.ToString())!;
        Assert.Equal(DealStage.Offered, after.Stage);
        Assert.Equal(DealStatus.Active, after.Status);
        Assert.Null(after.CounterpartyId);
        Assert.DoesNotContain(after.Timeline, e => e.Action == "advanced");
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
    /// ويُقلَبُ هذا الصَفُّ بِاسمِه يَومَ يُقَرَّرُ بابُه.
    /// <para><b>قُلِبَ بِاسمِه — قُرِّرَ بابُه</b> (‏ADR-033 §٦‑ب‑١ مُلحَقاً، ‏2026-10-11): كانَ
    /// <c>Today_cart_add_takes_any_listing_and_only_the_listing_page_hides_the_button_from_its_owner</c>. وصارَ
    /// <c>cart/add</c> يَرُدُّ مالِكَ الإعلانِ قَبلَ أَيِّ كِتابَةٍ بِرَمزِ مَسارِ العَرضِ نَفسِه، وصَفحَةُ الإعلانِ —
    /// الَّتي لَم تَكُن تَقرَأُ <c>self</c> — تَقولُه مِن القامُوس لِلبابَين. والزِرُّ ما زالَ مَخفِيّاً عَن المالِك.</para></summary>
    [Fact]
    public void Cart_add_refuses_the_owners_own_listing_with_err_self_before_any_write_and_the_listing_page_says_why()
    {
        var cartAdd = Endpoint("/{slug}/listings/{id:guid}/cart/add");
        var load   = cartAdd.IndexOf("var listing = await s.LoadAsync<Listing>(id);", StringComparison.Ordinal);
        var refuse = cartAdd.IndexOf("if (ParseListingOwnerId(listing) == userId) return Results.Redirect(Link(req, slug, $\"listings/{id}?err=self\"));", StringComparison.Ordinal);
        var write  = cartAdd.IndexOf("s.Store(cart);", StringComparison.Ordinal);
        Assert.True(load > 0 && refuse > load && write > refuse, $"رَدُّ المالِكِ لا يَسبِقُ الكِتابَة: load={load} refuse={refuse} write={write}");

        Assert.Contains("?err=self", Endpoint("/{slug}/listings/{id:guid}/deal"), StringComparison.Ordinal);

        var page = ListingPage();
        var hide = page.IndexOf("@if (!isOwner && !isOfferBased)", StringComparison.Ordinal);
        var cart = page.IndexOf("/cart/add\")", StringComparison.Ordinal);
        Assert.True(hide > 0 && cart > hide, $"زِرُّ السَلَّةِ لا يَقَعُ تَحتَ حارِسِ المالِك: hide={hide} cart={cart}");
        Assert.Contains("else if (Err == \"self\")", page, StringComparison.Ordinal);
        Assert.Contains("<AcAlert Variant=\"danger\" Message=\"@L[\"listings.detail.err_self\"]\" />", page, StringComparison.Ordinal);
        var text = ACommerce.Platform.I18n.LocaleCatalog.Text(ACommerce.Platform.I18n.LocaleCatalog.Arabic, "listings.detail.err_self");
        Assert.NotEqual("listings.detail.err_self", text);
        Assert.False(ACommerce.Platform.I18n.LocaleCatalog.IsPlaceholder(text));
    }

    /// <summary><b>المُوجِبُ والسالِبُ لِلهُوِيَّةِ الَّتي يَرُدُّ بِها البابُ</b> — المُحَلِّلُ القائِمُ
    /// (<c>ParseListingOwnerId</c>) بِقيمَةِ المُعَرِّفِ: المالِكُ بِأَيِّ صيغَةٍ لِمُعَرِّفِه يُرَدّ، وغَيرُه
    /// يُضيف، وإعلانٌ بِلا <c>owner_id</c> (بَذرَةٌ بِلا مالِك) لا يَرُدُّ أَحَداً.</summary>
    [Fact]
    public void The_cart_add_owner_check_compares_the_guid_value_and_lets_everyone_else_through()
    {
        var parse = typeof(ACommerce.Templates.Customer.Marketplace.MarketplaceTemplateExtensions)
            .GetMethod("ParseListingOwnerId", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
            ?? throw new InvalidOperationException("‏`ParseListingOwnerId` غَيرُ مَوجودَة — الأَداةُ عَمياء.");
        Guid? OwnerOf(string? ownerId)
        {
            var l = new ACommerce.Kit.Listings.Listing { Id = Guid.NewGuid(), Title = "سِلعَة" };
            if (ownerId is not null) l.Attributes["owner_id"] = ownerId;
            return (Guid?)parse.Invoke(null, new object[] { l });
        }

        foreach (var format in new[] { Buyer.ToString(), Buyer.ToString("D").ToUpperInvariant(), Buyer.ToString("N") })
            Assert.True(OwnerOf(format) == Buyer, $"المالِكُ بِصيغَةِ «{format}» لَم يُرَدّ.");
        Assert.False(OwnerOf(Seller.ToString()) == Buyer);
        Assert.Null(OwnerOf(null));
        Assert.Null(OwnerOf("not-a-guid"));
    }
}
