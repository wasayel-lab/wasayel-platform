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

    [Fact]
    public async Task Today_two_overlapping_review_clicks_by_one_author_write_two_reviews()
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

        Assert.True(firstResult.Ok && firstResult.Review is not null);
        Assert.True(second.Ok && second.Review is not null);
        Assert.Equal(2, count);
    }
}
