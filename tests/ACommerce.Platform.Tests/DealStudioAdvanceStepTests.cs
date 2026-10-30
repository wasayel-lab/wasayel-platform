using ACommerce.Kit.Payments;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ «→ التالي» مِن الاستوديو يُكتَبُ بِمُعَرِّفِ الطَرَفِ المُقابِل — أَخُطوَتُه هُوَ؟ (‏2026-10-30، تَدقيقُ الموجَة ١١ CO11) ══════════════════════════
//
// **الحالُ المَقيسَة**: بابُ الاستوديو `POST /studio/apps/{slug}/deals/{id}/advance` يُحَرِّكُ «بِهُوِيَّةِ جانِبِ المَتجَر» — `deal.CounterpartyId ?? auth.UserId`
// — بِاسمِ «مالِك التَّطبيق (إداريّ)» (‏ADR-033 §٦‑ط)، فَيُكتَبُ سَطرُ `advanced` فاعِلُه مُعَرِّفُ البائِعِ وإن لَم يَنقُر البائِعُ شَيئاً. و`TookAStep` (قَرارُ المالِك
// د33 «٣»: «وسم «موثّق» لا يُعطى إلا إن قام الطرف المقيَّم نفسه بخطوة») يَعُدُّ كُلَّ `advanced` بِمُعَرِّفِه — فَتَقييمُ المُشتَري لِبائِعٍ لَم يَخطُ يُوسَمُ «مَوثوقاً».

public class DealStudioAdvanceStepTests(ITestOutputHelper output)
{
    private const string Slug = "studio-step";
    private static readonly Guid Buyer = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
    private static readonly Guid Seller = Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb");

    /// <summary>اسمُ الفاعِلِ كَما يُمَرِّرُه بابُ الاستوديو بِحَرفِه اليَوم.</summary>
    internal const string StudioName = "مالِك التَّطبيق (إداريّ)";

    private sealed class NoPayments : IPaymentProvider
    {
        private static InvalidOperationException Touched() => new("صَفقَةُ النَقدِ لا تَمَسُّ المال.");
        public string ProviderName => "none";
        public Task<PaymentResult> AuthorizeAsync(PaymentRequest req, string idempotencyKey, CancellationToken ct = default) => throw Touched();
        public Task<PaymentResult> CaptureAsync(string paymentId, decimal? amount = null, CancellationToken ct = default) => throw Touched();
        public Task<PaymentResult> RefundAsync(string paymentId, decimal amount, string reason, CancellationToken ct = default) => throw Touched();
        public Task<SubscriptionResult> CreateSubscriptionAsync(SubscriptionRequest req, string idempotencyKey, CancellationToken ct = default) => throw Touched();
        public Task<bool> CancelSubscriptionAsync(string subscriptionId, CancellationToken ct = default) => throw Touched();
        public Task<Invoice?> GetInvoiceAsync(string paymentId, CancellationToken ct = default) => throw Touched();
    }

    /// <summary>صَفقَةُ سَلَّةٍ نَقدِيَّةٌ بِبائِعٍ مَربوطٍ كَما يُولِدُها <c>/checkout/submit</c>.</summary>
    private static async Task<(DocWorld World, DealsService Deals, Guid Id)> CashCart()
    {
        var world = new DocWorld();
        var deals = new DealsService(world.Store, new NoPayments());
        var deal = await deals.StartAsync(Slug, "marketplace", Buyer, "مُشتَرٍ", null, "سِلعَة", 100m,
            new() { [CheckoutPayMethods.AttributeKey] = CheckoutPayMethods.CashOnDelivery });
        await deals.AttachRefAsync(Slug, deal.Id, DealsService.ListingOwnerRef, Seller.ToString());
        await deals.BindListingOwnerAsync(Slug, deal.Id, Seller.ToString());
        return (world, deals, deal.Id);
    }

    private static string StudioAdvanceCall()
        => WriteEndpointGuardTests.StripComments(File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, "libs", "templates",
            "ACommerce.Templates.Customer.Marketplace", "MarketplaceTemplateExtensions.cs")));

    /// <summary><b>قُلِبَ بِاسمِه</b> (تَوصيفُه <c>5fddc97</c> — كانَ <c>Today_a_studio_advance_counts_as_the_vendors_own_step</c>: صادِق): صاحِبُ المَتجَرِ يَقبَلُ مِن
    /// الاستوديو طَلَبَ بائِعٍ مَربوطٍ بِالنِداءِ الَّذي يُنادي بِه البابُ حَرفاً — السَطرُ فاعِلُه البائِعُ بِاسمِ <see cref="DealEvent.StudioActor"/> كَما كان ⇒
    /// <c>TookAStep(deal, Seller)</c> <b>كاذِب</b>. <b>والسالِب</b>: البائِعُ يَخطو الخُطوَةَ التالِيَةَ بِنَفسِه ⇒ صادِق. والبابُ يُمَرِّرُ الاسمَ بِحَرفِ الثابِت.</summary>
    [Fact]
    public async Task A_studio_advance_is_not_the_vendors_own_step_and_his_own_click_still_is()
    {
        Assert.Equal(StudioName, DealEvent.StudioActor);
        Assert.Contains($"var actorId = deal.CounterpartyId ?? auth.UserId!.Value;", StudioAdvanceCall(), StringComparison.Ordinal);
        Assert.Contains($"deals.AdvanceAsync(slug, id, actorId, \"{DealEvent.StudioActor}\", note,", StudioAdvanceCall(), StringComparison.Ordinal);
        var (world, deals, id) = await CashCart();
        var before = world.Read<Deal>(id.ToString())!;

        var moved = await deals.AdvanceAsync(Slug, id, before.CounterpartyId!.Value, StudioName, "");

        var deal = world.Read<Deal>(id.ToString())!;
        var line = deal.Timeline[^1];
        output.WriteLine($"ok={moved.Ok} · {line.Action} {line.StageBefore}→{line.StageAfter} فاعِلُه {line.ActorId} «{line.ActorName}» · خَطا البائِع؟ {DealReviewAuthorization.TookAStep(deal, Seller)}");
        Assert.True(moved.Ok, moved.Reason);
        Assert.Equal(("advanced", (Guid?)Seller, StudioName), (line.Action, line.ActorId, line.ActorName));
        Assert.True(DealEvent.IsByStudio(line));
        Assert.False(DealReviewAuthorization.TookAStep(deal, Seller));

        var own = await deals.AdvanceAsync(Slug, id, Seller, "بائِع", "");
        Assert.True(own.Ok, own.Reason);
        deal = world.Read<Deal>(id.ToString())!;
        Assert.False(DealEvent.IsByStudio(deal.Timeline[^1]));
        Assert.True(DealReviewAuthorization.TookAStep(deal, Seller));
    }
}
