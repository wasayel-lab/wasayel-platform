using ACommerce.Kit.Payments;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ أَيُلغي طَرَفٌ صَفقَةً بَعدَ أَن شُحِنَت أَو سُلِّمَت؟ (تَدقيقُ الإطلاق ‏2026-10-24، lens-money-integrity-4، حاجِبَة؛ قَرارُ مالِك) ═══
//
// **المَقيسُ قَبلَ اللَمس** (عِندَ `dace7cd`): `DealCancelAuthorization.Validate` تَسأَلُ الغِيابَ ثُمَّ الطَرَفَ ثُمَّ الحالَة — ولا تَسأَلُ المَرحَلَة.
// فَالمُشتَري أَو البائِعُ يُلغي صَفقَةً عِندَ «شَحن» أَو «تَسليم»: بَيعُ نَقدٍ قُبِضَ ثَمَنُه (سَطرُ «استُلِمَ النَقد») يَصيرُ «مُلغاة» بِلا إشعارٍ لِلبائِع،
// وشُحنَةُ OTO حَيَّةٌ (لا نِداءَ إلغاءٍ عِندَها)، وعَلى طَريقِ البَوّابَةِ يُكتَبُ «استِرجاعُه مُستَحَقّ» عَلى بِضاعَةٍ سُلِّمَت. والصَفحَةُ تَرسُمُ الاستِمارَةَ
// بِالدالَّةِ نَفسِها (`IsAllowed`) في كُلِّ مَرحَلَةٍ فَعّالَة. ولا مَدخَلَ «ب» يُحَدِّدُ مَراحِلَ الإلغاء.
//
// **التَوصيفُ قَبلَ التَبديل** (القاعِدَة ٣): صَفّا «اليَوم» أَدناه يُودَعانِ وَحدَهُما، ويُقلَبانِ بِاسمَيهِما في كوميتِ حَدِّ المَرحَلَة.

public class DealCancelStageTests(ITestOutputHelper output)
{
    private const string Slug = "zz-cancel-stage";
    private static readonly Guid Buyer    = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
    private static readonly Guid Seller   = Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb");

    /// <summary>مُزَوِّدٌ يَرمي عِندَ أَيِّ نِداء — صَفَقاتُ هذا المِلَفِّ نَقدٌ بِلا مُعَرِّفِ دَفع.</summary>
    private sealed class ForbiddenPaymentProvider : IPaymentProvider
    {
        private static InvalidOperationException Touched() => new("صَفقَةُ نَقدٍ لا تَمَسُّ المال — نودِيَ مُزَوِّدُ الدَفع.");
        public string ProviderName => "forbidden";
        public Task<PaymentResult> AuthorizeAsync(PaymentRequest req, string idempotencyKey, CancellationToken ct = default) => throw Touched();
        public Task<PaymentResult> CaptureAsync(string paymentId, decimal? amount = null, CancellationToken ct = default) => throw Touched();
        public Task<PaymentResult> RefundAsync(string paymentId, decimal amount, string reason, CancellationToken ct = default) => throw Touched();
        public Task<SubscriptionResult> CreateSubscriptionAsync(SubscriptionRequest req, string idempotencyKey, CancellationToken ct = default) => throw Touched();
        public Task<bool> CancelSubscriptionAsync(string subscriptionId, CancellationToken ct = default) => throw Touched();
        public Task<Invoice?> GetInvoiceAsync(string paymentId, CancellationToken ct = default) => throw Touched();
    }

    private static T Now<T>(Task<T> task)
    {
        Assert.True(task.IsCompleted, "نِداءٌ لَم يَكتَمِل في عالَمٍ مُتَزامِن — الأَداةُ تَكذِب.");
        return task.GetAwaiter().GetResult();
    }

    private static Deal CashAt(DealStage stage, string pattern = "marketplace")
    {
        var deal = new Deal
        {
            Id = Guid.NewGuid(), TenantSlug = Slug, Pattern = pattern, Stage = stage, AmountSar = 90m,
            ListingTitle = "سِلعَة", InitiatorId = Buyer, InitiatorName = "مُشتَرٍ", CounterpartyId = Seller, CounterpartyName = "بائِع",
        };
        deal.Attributes[CheckoutPayMethods.AttributeKey] = CheckoutPayMethods.CashOnDelivery;
        return deal;
    }

    private static DealCanceller Party(Guid id) => new(id, "طَرَف", IsStoreAdmin: false);

    /// <summary>
    /// <para><b>«اليَوم»</b>: لِكُلِّ مَرحَلَةٍ فَعّالَةٍ في كُلِّ نَمَطٍ يَرِدُ فيه «شَحن» أَو «تَسليم» أَو «استِلام»، المُشتَري والبائِعُ مُخَوَّلانِ
    /// بِالإلغاء — والصَفحَةُ تَرسُمُ الاستِمارَةَ بِالدالَّةِ نَفسِها.</para>
    /// </summary>
    [Theory]
    [InlineData("marketplace", DealStage.Shipping)]
    [InlineData("marketplace", DealStage.Delivered)]
    [InlineData("rental",      DealStage.Delivered)]
    [InlineData("rental",      DealStage.Received)]
    [InlineData("trip",        DealStage.Delivered)]
    public void Today_either_party_may_cancel_after_shipping_or_delivery(string pattern, DealStage stage)
    {
        var deal = CashAt(stage, pattern);
        Assert.Null(DealCancelAuthorization.Validate(deal, Party(Buyer)));
        Assert.Null(DealCancelAuthorization.Validate(deal, Party(Seller)));
        Assert.True(DealCancelAuthorization.IsAllowed(deal, Party(Buyer)));
        output.WriteLine($"{pattern}/{stage}: المُشتَري والبائِعُ مُخَوَّلانِ بِالإلغاء");
    }

    /// <summary>
    /// <para><b>«اليَوم» عَلى الخِدمَةِ الحَقيقِيَّة</b>: بَيعُ نَقدٍ عِندَ «تَسليم» بِسَطرِ «استُلِمَ النَقد» — المُشتَري يُلغيه فَيَصيرُ «مُلغاة» وسَطرُ
    /// النَقدِ باقٍ، والإشعارُ لا يُخبِرُ أَحَداً (المُلغي هُوَ المُشتَري، ولا إشعارَ إلغاءٍ لِبائِعٍ في الأُنبوب).</para>
    /// </summary>
    [Fact]
    public void Today_the_buyer_cancels_a_cash_collected_sale_and_nobody_is_told()
    {
        var world = new DocWorld();
        var deals = new DealsService(world.Store, new ForbiddenPaymentProvider());
        var deal = CashAt(DealStage.Delivered);
        deal.Timeline.Add(new(DealStage.Delivered, DealStage.Delivered, DealEvent.CashOnDelivery, Seller, "بائِع", null, DateTime.UtcNow));
        world.Put(deal);

        var res = Now(deals.CancelAsync(Slug, deal.Id, Party(Buyer), "إلغاء"));

        Assert.True(res.Ok);
        var stored = world.Read<Deal>(deal.Id.ToString())!;
        Assert.Equal(DealStatus.Cancelled, stored.Status);
        Assert.Equal(1, stored.Timeline.Count(e => e.Action == DealEvent.CashOnDelivery));
        Assert.False(DealNotices.TellsTheBuyer(stored, Buyer));
        output.WriteLine($"buyer cancel at Delivered: ok={res.Ok} status={stored.Status} cod_line=1 notified=False");
    }
}
