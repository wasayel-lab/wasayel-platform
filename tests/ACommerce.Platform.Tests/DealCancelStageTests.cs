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
//
// **وقُلِبا بِاسمَيهِما** (‏2026-10-24؛ تَوصيفُهُما `818ae9e`؛ ‏ADR-033 §٦‑ج مُلحَقاً): صارَت `Validate` تَسأَلُ بَعدَ الحالَةِ مَرحَلَةَ الطَرَف —
// `DealCancelAuthorization.PartyCancelStages` (عَرض، حَجز، تَأكيد، دَفع) افتِراضاً قابِلاً لِلقَلبِ حَتّى يُقَرِّرَ المالِك («ب» W8-1·٢) — وما بَعدَها
// `deal_past_cancel_stage` لِلطَرَف، ومُشرِفُ المَتجَرِ يُلغي كَما كان. والصَفحَةُ لا تَرسُمُ الاستِمارَةَ بِالدالَّةِ نَفسِها.

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

    private static DealCanceller StoreAdmin() => new(Guid.Parse("cccccccc-cccc-4ccc-8ccc-cccccccccccc"), "مالِك التَّطبيق", IsStoreAdmin: true);

    /// <summary>
    /// <para><b>قُلِبَ بِاسمِه</b> (‏2026-10-24؛ كانَ <c>Today_either_party_may_cancel_after_shipping_or_delivery</c>، مُودَعٌ في <c>818ae9e</c>): لِكُلِّ
    /// مَرحَلَةٍ فَعّالَةٍ بَعدَ الدَفعِ في كُلِّ نَمَطٍ يَرِدُ فيه «شَحن» أَو «تَسليم» أَو «استِلام» — المُشتَري والبائِعُ مَردودانِ بِـ<c>deal_past_cancel_stage</c>،
    /// والصَفحَةُ لا تَرسُمُ الاستِمارَة (<c>IsAllowed</c> نَفسُها)، وصاحِبُ المَتجَرِ يُلغي كَما كان (سالِبُ الرَمز).</para>
    /// </summary>
    [Theory]
    [InlineData("marketplace", DealStage.Shipping)]
    [InlineData("marketplace", DealStage.Delivered)]
    [InlineData("rental",      DealStage.Delivered)]
    [InlineData("rental",      DealStage.Received)]
    [InlineData("trip",        DealStage.Delivered)]
    public void No_party_cancels_after_shipping_or_delivery_and_the_page_hides_the_door(string pattern, DealStage stage)
    {
        var deal = CashAt(stage, pattern);
        Assert.Equal(DealCancelAuthorization.DealPastCancelStage, DealCancelAuthorization.Validate(deal, Party(Buyer))!.Code);
        Assert.Equal(DealCancelAuthorization.DealPastCancelStage, DealCancelAuthorization.Validate(deal, Party(Seller))!.Code);
        Assert.False(DealCancelAuthorization.IsAllowed(deal, Party(Buyer)));
        Assert.False(DealCancelAuthorization.IsAllowed(deal, Party(Seller)));
        Assert.Null(DealCancelAuthorization.Validate(deal, StoreAdmin()));
        output.WriteLine($"{pattern}/{stage}: الطَرَفانِ مَردودانِ بِـdeal_past_cancel_stage، وصاحِبُ المَتجَرِ يُلغي");
    }

    /// <summary>مُوجِبٌ لِلحَدّ: في كُلِّ مَرحَلَةٍ مِن <see cref="DealCancelAuthorization.PartyCancelStages"/> يُلغي الطَرَفانِ كَما كانا — والمَجموعَةُ
    /// أَربَعٌ بِحَرفِها، فَمَرحَلَةٌ تُضافُ أَو تُحذَفُ تُحمِرُ هذا الصَفَّ باسمِها.</summary>
    [Theory]
    [InlineData(DealStage.Offered)]
    [InlineData(DealStage.Booked)]
    [InlineData(DealStage.Confirmed)]
    [InlineData(DealStage.Paid)]
    public void Either_party_still_cancels_before_the_goods_leave_the_vendor(DealStage stage)
    {
        var deal = CashAt(stage);
        Assert.Null(DealCancelAuthorization.Validate(deal, Party(Buyer)));
        Assert.Null(DealCancelAuthorization.Validate(deal, Party(Seller)));
        Assert.Equal(new[] { DealStage.Offered, DealStage.Booked, DealStage.Confirmed, DealStage.Paid },
            DealCancelAuthorization.PartyCancelStages.OrderBy(s => s));
    }

    /// <summary>والتَرتيبُ (القاعِدَة ٦): الغِيابُ ثُمَّ التَخويلُ ثُمَّ الحالَةُ ثُمَّ المَرحَلَة — غَريبٌ عِندَ «شَحن» يُقالُ لَه «لَستَ طَرَفاً» لا مَرحَلَتُها،
    /// وصَفقَةٌ مُكتَمِلَةٌ عِندَ «تَقييم» «لَم تَعُد نَشِطَة».</summary>
    [Fact]
    public void The_stage_is_asked_after_authorization_and_state()
    {
        var stranger = Guid.Parse("dddddddd-dddd-4ddd-8ddd-dddddddddddd");
        Assert.Equal(DealCancelAuthorization.ActorNotParty, DealCancelAuthorization.Validate(CashAt(DealStage.Shipping), Party(stranger))!.Code);
        var done = CashAt(DealStage.Reviewed);
        done.Status = DealStatus.Completed;
        Assert.Equal(DealCancelAuthorization.DealNotActive, DealCancelAuthorization.Validate(done, Party(Buyer))!.Code);
    }

    /// <summary>
    /// <para><b>قُلِبَ بِاسمِه</b> (‏2026-10-24؛ كانَ <c>Today_the_buyer_cancels_a_cash_collected_sale_and_nobody_is_told</c>، مُودَعٌ في <c>818ae9e</c>):
    /// بَيعُ نَقدٍ عِندَ «تَسليم» بِسَطرِ «استُلِمَ النَقد» — المُشتَري يُرَدُّ بِرَمزِه ولا يُكتَبُ شَيء، والصَفقَةُ فَعّالَة. والطَلَبُ كُلُّه
    /// (<c>CancelWholeInvoiceAsync</c>) يُرَدُّ بِالرَمزِ نَفسِه، والـAPI ‏409 بِرَمزِ الحالَةِ القائِمِ ومَعَه المَرحَلَة.</para>
    /// </summary>
    [Fact]
    public void The_buyer_cannot_cancel_a_cash_collected_sale_and_nothing_is_written()
    {
        var world = new DocWorld();
        var deals = new DealsService(world.Store, new ForbiddenPaymentProvider());
        var deal = CashAt(DealStage.Delivered);
        deal.Timeline.Add(new(DealStage.Delivered, DealStage.Delivered, DealEvent.CashOnDelivery, Seller, "بائِع", null, DateTime.UtcNow));
        world.Put(deal);
        var saves = world.SaveCalls;

        var res = Now(deals.CancelAsync(Slug, deal.Id, Party(Buyer), "إلغاء"));
        var order = Now(deals.CancelWholeInvoiceAsync(Slug, deal.Id, Party(Buyer), "إلغاء"));

        Assert.False(res.Ok);
        Assert.Equal(DealCancelAuthorization.DealPastCancelStage, res.Violation!.Code);
        Assert.Equal(DealCancelAuthorization.DealPastCancelStage, order.Result.Violation!.Code);
        Assert.Empty(order.Cancelled);
        Assert.Equal(saves, world.SaveCalls);
        var stored = world.Read<Deal>(deal.Id.ToString())!;
        Assert.Equal(DealStatus.Active, stored.Status);
        Assert.Equal(1, stored.Timeline.Count(e => e.Action == DealEvent.CashOnDelivery));

        var api = ACommerce.Templates.Customer.Marketplace.Api.DealApi.FromCancel(res);
        Assert.Equal(409, api.Status);
        var json = ACommerce.Templates.Customer.Marketplace.Api.ApiJson.Serialize(api.Payload);
        Assert.Contains("\"code\":\"deal_not_active\"", json, StringComparison.Ordinal);
        Assert.Contains("\"stage\":\"Delivered\"", json, StringComparison.Ordinal);
        output.WriteLine($"buyer cancel at Delivered: ok={res.Ok} code={res.Violation.Code} status={stored.Status} saves +{world.SaveCalls - saves} · api {api.Status}");
    }

    // ─── توصيفُ «اليَوم» قَبلَ قَرارِ المالِك د37 (‏2026-10-29) — صَفقَةٌ لَها طَلَبٌ عِندَ OTO يُلغيها طَرَف ───────────────────
    //
    // **المَقيس**: `Validate` تَسأَلُ المَرحَلَةَ وَحدَها، و«تَأكيد»/«دَفع» في `PartyCancelStages` — فَصَفقَةٌ عَلَيها `oto_order_id` (طَلَبٌ أُنشِئَ عِندَ
    // OTO ولَم تَدخُل «شَحن» بَعدُ: `ShipmentUnconfirmed`، أَو الوارِدُ لَم يَصِل) يُلغيها المُشتَري أَو البائِعُ مِن صَفحَتِها، والشُحنَةُ عَلى حِسابِ البائِعِ
    // عِندَ OTO باقِيَةٌ (لا نِداءَ إلغاءٍ لَدَيها). ورِسالَةُ القاعِدَةِ «بَعدَ الشَحنِ أَو التَسليمِ» لا تَصدُقُ في الرِحلَةِ والخِدمَةِ والإيجار، وصَفحَةُ
    // الاستوديو لا تَقولُ لِصاحِبِ المَتجَرِ حينَ يُلغي إنّ لِلصَفقَةِ طَلَباً عِندَ OTO. يَنقَلِبُ كُلُّ صَفٍّ بِاسمِه في كوميتِ القَرار.

    private static Deal PlacedAt(DealStage stage)
    {
        var deal = CashAt(stage);
        deal.Refs[DealShipping.OrderRef] = "wasayel-oto-order-1";
        return deal;
    }

    [Theory]
    [InlineData(DealStage.Confirmed)]
    [InlineData(DealStage.Paid)]
    public void Today_a_party_cancels_a_deal_whose_order_is_placed_at_oto(DealStage stage)
    {
        var world = new DocWorld();
        var deals = new DealsService(world.Store, new ForbiddenPaymentProvider());
        var deal = PlacedAt(stage);
        Assert.True(DealShipping.IsPlaced(deal));
        Assert.Null(DealCancelAuthorization.Validate(deal, Party(Buyer)));
        Assert.Null(DealCancelAuthorization.Validate(deal, Party(Seller)));
        world.Put(deal);

        var res = Now(deals.CancelAsync(Slug, deal.Id, Party(Seller), "إلغاء"));
        Assert.True(res.Ok);
        Assert.Equal(DealStatus.Cancelled, world.Read<Deal>(deal.Id.ToString())!.Status);
        output.WriteLine($"{stage} + oto_order_id: البائِعُ يُلغي — {res.Ok}");
    }

    [Fact]
    public void Today_the_rule_message_says_after_shipping_or_delivery_and_the_studio_page_says_nothing_of_oto()
    {
        var v = DealCancelAuthorization.Validate(CashAt(DealStage.Shipping), Party(Buyer))!;
        Assert.Contains("بَعدَ الشَحنِ أَو التَسليمِ لا يُلغيها طَرَف", v.MessageAr, StringComparison.Ordinal);
        var page = File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot,
            "libs/templates/ACommerce.Templates.Customer.Marketplace/Components/Pages/StudioAppDealDetail.razor"));
        Assert.True(page.Length > 1000, "أَداةٌ عَمياء: صَفحَةُ صَفقَةِ الاستوديو لَم تُقرَأ.");
        Assert.DoesNotContain("IsPlaced", page, StringComparison.Ordinal);
    }

    /// <summary>ونَصُّ الرَمزِ لِلصَفحَةِ مِن القامُوسِ بِحَرفِه (القاعِدَة ١١) — يَبلُغُها طَلَبٌ لَم تَرسُمه أَو لِسانٌ قَديم.</summary>
    [Fact]
    public void The_past_cancel_stage_code_reads_its_dictionary_text_verbatim()
    {
        var key = DealRefusalTexts.KeyFor(DealCancelAuthorization.DealPastCancelStage);
        Assert.Equal("deals.detail.err_deal_past_cancel_stage", key);
        Assert.Equal("شُحِنَت هذِه الصَّفقَة أَو سُلِّمَت — فَلا يُلغيها طَرَفٌ مِن صَفحَتِها؛ يُلغيها صاحِبُ المَتجَر وَحدَه.",
            ACommerce.Platform.I18n.LocaleCatalog.Text(ACommerce.Platform.I18n.LocaleCatalog.Arabic, key!));
    }
}
