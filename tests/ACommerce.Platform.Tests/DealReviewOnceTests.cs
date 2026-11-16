using ACommerce.Kit.Payments;
using ACommerce.Kit.Reviews;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using Marten;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ تَقييمٌ واحِدٌ لِكُلِّ كاتِبٍ في كُلِّ صَفقَة — ولَو نَقَرَ مَرَّتَينِ مَعاً (‏ADR-036 §٣‑٥؛ مُراجَعَةُ المَوجَة 3a، G4 (iv)) ═══
//
// **المَقيسُ قَبلَ اللَمس** (‏2026-10-13): `DealReviews.SubmitAsync` تَسأَلُ `ReviewsService.HasReviewedAsync` ثُمَّ تَكتُبُ بِـ`SubmitAsync`
// (مُعَرِّفٌ عَشوائِيٌّ و`Store`) — بِلا قَيدٍ فَريد. ونَقرَتانِ مُتَداخِلَتانِ تَمُرّانِ كِلتاهُما بِالسُؤالِ قَبلَ أَن تُحفَظَ أولاهُما — وتَعليقُ
// `DealReviews` نَفسُه يَقولُها «قائِمٌ قَبلَ هذا ويُقال»، و‏ADR-036 §٣‑٥ «نقرتانِ متزامنتانِ قد تكتبانِ تقييمَين — وثيقةُ `Review` لا `Deal`».
//
// **والقِياسُ حَتمِيٌّ لا نِسبَة**: `SaveGate` (`DealConcurrencyTests.cs`) يُوقِفُ حِفظَ نَقرَةٍ مُعَلَّمَةٍ بَعدَ أَن سَأَلَت وكَتَبَت في جَلسَتِها،
// فَتَمضي الثانِيَةُ كُلُّها، ثُمَّ يُستَأنَفُ حِفظُ الأولى. عَلى Postgres المَحَلِّيّ (‏WASAYEL_LIVE_PROOF=1)؛ وفي CI يَعودُ الصَفُّ بِلا قِياس.
//
// **التَوصيفُ قَبلَ التَبديل** (القاعِدَة ٣): صَفُّ «اليَوم» يُودَعُ وَحدَه ويُقلَبُ بِاسمِه في كوميتِ الإصلاح.

[Collection(LiveProofCollection.Name)]
public class LiveDealReviewOnceProofTests(ITestOutputHelper output)
{
    private static bool Enabled => LiveDealConcurrencyProofTests.Enabled;
    private static string? ConnectionString => LiveDealConcurrencyProofTests.ConnectionString;

    private static DocumentStore BuildStore() => DocumentStore.For(o =>
    {
        o.Connection(ConnectionString!.TrimEnd(';') + ";Timeout=60;Command Timeout=120");
        o.DatabaseSchemaName = "platform";
        o.Policies.AllDocumentsAreMultiTenanted();
        o.Events.TenancyStyle = global::JasperFx.MultiTenancy.TenancyStyle.Conjoined;
        o.Schema.For<ACommerce.Kit.Tenants.Tenant>().SingleTenanted().Identity(x => x.Id);
        DealsService.RegisterDocument(o);
        o.AutoCreateSchemaObjects = JasperFx.AutoCreate.All;
    });

    private sealed class ForbiddenPaymentProvider : IPaymentProvider
    {
        private static InvalidOperationException Touched() => new("صَفقَةُ النَقدِ والتَقييمُ لا يَمَسّانِ المال — نودِيَ مُزَوِّدُ الدَفع.");
        public string ProviderName => "forbidden";
        public Task<PaymentResult> AuthorizeAsync(PaymentRequest req, string idempotencyKey, CancellationToken ct = default) => throw Touched();
        public Task<PaymentResult> CaptureAsync(string paymentId, decimal? amount = null, CancellationToken ct = default) => throw Touched();
        public Task<PaymentResult> RefundAsync(string paymentId, decimal amount, string reason, CancellationToken ct = default) => throw Touched();
        public Task<SubscriptionResult> CreateSubscriptionAsync(SubscriptionRequest req, string idempotencyKey, CancellationToken ct = default) => throw Touched();
        public Task<bool> CancelSubscriptionAsync(string subscriptionId, CancellationToken ct = default) => throw Touched();
        public Task<Invoice?> GetInvoiceAsync(string paymentId, CancellationToken ct = default) => throw Touched();
    }

    /// <summary>صَفقَةُ نَقدٍ مُكتَمِلَةٌ بِطَرَفَيها — بِأَبوابِ الخِدمَةِ الحَقيقِيَّة: وِلادَةٌ ورَبطٌ ثُمَّ المَراحِلُ إلى «تَقييم».</summary>
    private static async Task<Guid> CompletedCashDeal(DealsService deals, string slug, Guid buyer, Guid seller)
    {
        var deal = await deals.StartAsync(slug, "marketplace", buyer, "مُشتَرٍ", null, "سِلعَة", 100m,
            new() { [CheckoutPayMethods.AttributeKey] = CheckoutPayMethods.CashOnDelivery });
        await deals.AttachRefAsync(slug, deal.Id, DealsService.ListingOwnerRef, seller.ToString());
        await deals.BindListingOwnerAsync(slug, deal.Id, seller.ToString());
        for (var i = 0; i < 6; i++)
        {
            var d = (await deals.LoadAsync(slug, deal.Id))!;
            if (d.Status != DealStatus.Active) break;
            var actor = DealsPolicy.RequiredActorToLeave(d) == "initiator" ? buyer : seller;
            var res = await deals.AdvanceAsync(slug, deal.Id, actor, "طَرَف");
            Assert.True(res.Ok, $"المَرحَلَة {d.Stage}: {res.Reason}");
        }
        Assert.Equal(DealStatus.Completed, (await deals.LoadAsync(slug, deal.Id))!.Status);
        return deal.Id;
    }

    private static async Task<int> ReviewsBy(IDocumentStore store, string slug, Guid author, Guid dealId)
    {
        await using var q = store.QuerySession(slug);
        return await q.Query<Review>().CountAsync(r => r.AuthorId == author && r.DealId == dealId);
    }

    /// <summary>
    /// <para><b>قُلِبَ بِاسمِه</b> (‏2026-10-13، مُراجَعَةُ المَوجَة 3a — G4 (iv)؛ ‏ADR-036 §١٢‑٤): كانَ
    /// <c>Today_two_overlapping_review_clicks_by_one_author_write_two_reviews</c> (مُودَعٌ في <c>3111224</c>) — «تَقييماتُ الكاتِبِ عَلى الصَفقَة: 2».
    /// وصارَ التَقييمُ يُكتَبُ بِمُعَرِّفٍ حَتمِيٍّ مِن (الصَفقَة، الكاتِب) و<c>Insert</c>: الثانِيَةُ تَكتُبُ، والأولى المُعَلَّقَةُ تَرتَطِمُ عِندَ حِفظِها
    /// بِالمِفتاحِ الأَساسِيِّ فَتُجابُ «قَيَّمَ مِن قَبل» (<c>Ok</c> بِلا <c>Review</c>) — والقاعِدَةُ تَحمِلُ تَقييماً واحِداً.</para>
    /// </summary>
    [Fact]
    public async Task Two_overlapping_review_clicks_by_one_author_write_one_review_and_the_held_one_is_already_reviewed()
    {
        if (!Enabled || string.IsNullOrEmpty(ConnectionString)) return;

        await using var store = BuildStore();
        var gate = new SaveGate.Gate();
        var gated = SaveGate.Wrap(store, gate);
        var deals = new DealsService(store, new ForbiddenPaymentProvider());
        var door = new DealReviews(new DealsService(gated, new ForbiddenPaymentProvider()), new ReviewsService(gated));

        var slug = "zz-proof-" + Guid.NewGuid().ToString("N")[..8];
        var buyer = Guid.NewGuid();
        var seller = Guid.NewGuid();
        var dealId = await CompletedCashDeal(deals, slug, buyer, seller);

        // الأولى: تَسأَلُ ثُمَّ تَكتُبُ في جَلسَتِها ويَقِفُ حِفظُها.
        var first = Task.Run(async () =>
        {
            gate.Marked.Value = true;
            return await door.SubmitAsync(slug, dealId, new DealReviewer(buyer, "مُشتَرٍ"), 5, "ممتاز");
        });
        await gate.Reached.Task.WaitAsync(TimeSpan.FromSeconds(30));

        // والثانِيَةُ كُلُّها بَينَهُما.
        var second = await door.SubmitAsync(slug, dealId, new DealReviewer(buyer, "مُشتَرٍ"), 4, "جَيِّد");

        gate.Release.TrySetResult();
        var firstResult = await first.WaitAsync(TimeSpan.FromSeconds(30));

        var count = await ReviewsBy(store, slug, buyer, dealId);
        output.WriteLine($"الأولى: ok={firstResult.Ok} كَتَبَت={firstResult.Review is not null} · الثانِيَة: ok={second.Ok} كَتَبَت={second.Review is not null} · تَقييماتُ الكاتِبِ عَلى الصَفقَة: {count}");

        Assert.True(firstResult.Ok && firstResult.Review is null);       // «قَيَّمَ مِن قَبل» — كَما يُجابُ مَن سُئِلَ فَوُجِد
        Assert.True(second.Ok && second.Review is not null);
        Assert.Equal(DealReviews.ReviewIdFor(dealId, buyer), second.Review!.Id);
        Assert.Equal(1, count);
    }

    /// <summary>والسالِب: الطَرَفانِ في صَفقَةٍ واحِدَةٍ يُقَيِّمُ كُلٌّ الآخَرَ — مُعَرِّفانِ لا واحِد، فَتَقييمان؛ وتَقييمُ الاستوديو الإداريُّ (بِمُعَرِّفٍ
    /// عَشوائِيٍّ مِن <see cref="ReviewsService.SubmitAsync"/> بِلا <c>id</c>) لا يَمَسُّه القَيد.</summary>
    [Fact]
    public async Task Both_parties_still_review_each_other_and_the_studio_path_is_untouched()
    {
        if (!Enabled || string.IsNullOrEmpty(ConnectionString)) return;

        await using var store = BuildStore();
        var deals = new DealsService(store, new ForbiddenPaymentProvider());
        var reviews = new ReviewsService(store);
        var door = new DealReviews(deals, reviews);

        var slug = "zz-proof-" + Guid.NewGuid().ToString("N")[..8];
        var buyer = Guid.NewGuid();
        var seller = Guid.NewGuid();
        var dealId = await CompletedCashDeal(deals, slug, buyer, seller);

        var byBuyer = await door.SubmitAsync(slug, dealId, new DealReviewer(buyer, "مُشتَرٍ"), 5, "ممتاز");
        var bySeller = await door.SubmitAsync(slug, dealId, new DealReviewer(seller, "بائِع"), 5, "مُشتَرٍ جَيِّد");
        Assert.NotNull(byBuyer.Review);
        Assert.NotNull(bySeller.Review);
        Assert.NotEqual(byBuyer.Review!.Id, bySeller.Review!.Id);

        // الاستوديو: نِداءُ الكيتِ بِلا `id` — عَشوائِيٌّ كَما كان، ومَرَّتانِ تَكتُبانِ اثنَين.
        await reviews.SubmitAsync(slug, seller, "بائِع", buyer, "مُشتَرٍ", 4, "مِن الاستوديو", dealId: dealId, dealPattern: "marketplace");
        await reviews.SubmitAsync(slug, seller, "بائِع", buyer, "مُشتَرٍ", 3, "مِن الاستوديو ثانِيَةً", dealId: dealId, dealPattern: "marketplace");

        var buyerCount = await ReviewsBy(store, slug, buyer, dealId);
        var sellerCount = await ReviewsBy(store, slug, seller, dealId);
        output.WriteLine($"المُشتَري: {buyerCount} (واحِدٌ مِن صَفحَتِه واثنانِ مِن الاستوديو) · البائِع: {sellerCount}");
        Assert.Equal(3, buyerCount);
        Assert.Equal(1, sellerCount);
    }
}

// ═══ المُعَرِّفُ الحَتمِيّ — دالَّةٌ نَقِيَّةٌ تُقاسُ في CI ════════════════════════════════════════════════════════

public class DealReviewIdTests
{
    private static readonly Guid DealA = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid DealB = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid Buyer = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
    private static readonly Guid Seller = Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb");

    [Fact]
    public void ReviewIdFor_is_stable_per_deal_and_author_and_distinct_across_either()
    {
        var id = DealReviews.ReviewIdFor(DealA, Buyer);
        Assert.Equal(id, DealReviews.ReviewIdFor(DealA, Buyer));             // حَتمِيّ
        Assert.NotEqual(id, DealReviews.ReviewIdFor(DealA, Seller));         // الطَرَفُ الآخَر
        Assert.NotEqual(id, DealReviews.ReviewIdFor(DealB, Buyer));          // صَفقَةٌ أُخرى
        Assert.NotEqual(DealReviews.ReviewIdFor(Buyer, DealA), id);          // التَرتيبُ جُزءٌ مِن البَصمَة
        Assert.NotEqual(Guid.Empty, id);
        Assert.NotEqual(DealA, id);
        Assert.NotEqual(Buyer, id);
        Assert.Equal(8, id.Version);                                         // RFC 9562 — مُعَرِّفٌ مُخَصَّص
        Assert.Equal(0x8, (id.ToByteArray()[8] >> 4) & 0xC);                 // المُتَغَيِّرُ 10xx
    }

    [Fact]
    public void The_member_review_door_writes_with_the_deterministic_id_and_answers_a_duplicate_as_already_reviewed()
    {
        var src = WriteEndpointGuardTests.StripComments(File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot,
            "libs", "templates", "ACommerce.Templates.Customer.Marketplace", "Services", "Deals", "DealReviews.cs")));
        Assert.Contains("id: ReviewIdFor(dealId, by.ActorId)", src, StringComparison.Ordinal);
        Assert.Contains("catch (JasperFx.DocumentAlreadyExistsException)", src, StringComparison.Ordinal);
        Assert.Contains("return DealReviewResult.AlreadyReviewed(deal!);", src, StringComparison.Ordinal);

        var kit = WriteEndpointGuardTests.StripComments(File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot,
            "libs", "kits", "Reviews", "ACommerce.Kit.Reviews.Core", "ReviewsService.cs")));
        var submit = kit[kit.IndexOf("public async Task<Review> SubmitAsync(", StringComparison.Ordinal)..kit.IndexOf("public async Task<Review?> RespondAsync(", StringComparison.Ordinal)];
        Assert.Contains("Id = id ?? Guid.NewGuid()", submit, StringComparison.Ordinal);
        Assert.Contains("s.Insert(r);", submit, StringComparison.Ordinal);
        Assert.DoesNotContain("s.Store(r);", submit, StringComparison.Ordinal);
    }
}
