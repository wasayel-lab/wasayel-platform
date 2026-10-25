using ACommerce.Kit.Payments;
using ACommerce.Templates.Customer.Marketplace.Api;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ تَقَدُّمُ الـAPI يُخبِرُ غَيرَ الطَرَفِ بِمَرحَلَةِ صَفقَةِ غَيرِه — وبابُ القِراءَةِ بِجِوارِه يُجيبُه ‏404 (مُراجَعَةُ تَيّارِ W8-10 الجَولَة ١، كُبرى؛ القاعِدَة ٦) ═══
//
// **المَقيسُ عِندَ الرَأس** (`e2fcf8c`): `POST /api/v1/deals/{id}/advance` يَقرَأُ الجِسمَ ثُمَّ يُنادي `DealsService.AdvanceAsync` **بِلا سُؤالٍ عَن العُضوِيَّة**،
// والخِدمَةُ تَرُدُّ غَيرَ الطَرَفِ بِرَفضِ الفاعِل (`IsActorAllowed`) وتُرفِقُ الصَفقَةَ المَقروءَة — فَيُتَرجِمُها `DealApi.FromAdvance` إلى ‏403
// `actor_not_allowed` بِجِسمٍ `{stage, status, required_actor}`، أَو ‏409 `deal_not_active` بِحالِها إن خَرَجَت مِن `Active`. فَمِفتاحٌ في المَتجَرِ نَفسِه
// يَعرِفُ مُعَرِّفَ صَفقَةٍ لا يَملِكُها يَسمَعُ: أَنَّها مَوجودَة، ومَرحَلَتَها، وحالَها، ومَن يُنتَظَر — بَينَما `GET /api/v1/deals/{id}` و`/cancel` يُجيبانِه
// ‏404 «لا شَيءَ هُنا» (`DealApi.IsParty`؛ كُتلَةُ «أ» في حَقيبَةِ المُطابَقَة). وصَفُّ ‏`ADR-036` §١٣‑٦ «ما لا يُغلِقُه» يُسَمّيه بِحَرفِه.
//
// **التَوصيفُ قَبلَ التَبديل** (القاعِدَة ٣): صَفُّ «اليَوم» يُودَعُ وَحدَه، ويُقلَبُ بِاسمِه في كوميتِ الإصلاح.

public class ApiDealAdvancePartyTests(ITestOutputHelper output)
{
    private const string Slug = "zz-api-advance-party";
    private static readonly Guid Buyer    = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
    private static readonly Guid Seller   = Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb");
    private static readonly Guid Stranger = Guid.Parse("cccccccc-cccc-4ccc-8ccc-cccccccccccc");

    private static string RepoRoot => ThemeZeroEquivalenceTests.RepoRoot;
    private const string Template = "libs/templates/ACommerce.Templates.Customer.Marketplace";

    /// <summary>مُزَوِّدٌ يَرمي عِندَ أَيِّ نِداء — صَفَقاتُ هذا المِلَفِّ نَقدٌ بِلا مُعَرِّفِ دَفع، والرَفضُ لا يَمَسُّ المال.</summary>
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

    /// <summary>عالَمُ الوَثائِقِ يُتِمُّ كُلَّ انتِظارٍ في مَكانِه — وإن لَم يَكتَمِل فَالأَداةُ تَكذِب.</summary>
    private static T Now<T>(Task<T> task)
    {
        Assert.True(task.IsCompleted, "نِداءٌ لَم يَكتَمِل في عالَمٍ مُتَزامِن — الأَداةُ تَكذِب.");
        return task.GetAwaiter().GetResult();
    }

    /// <summary>مَقطَعُ النُقطَةِ بِلا تَعليقات — مِن مَسارِها إلى حارِسِ المِفتاحِ بَعدَها.</summary>
    internal static string ApiAdvance()
    {
        var path = Path.Combine(RepoRoot, Template, "Api", "ApiV1Endpoints.cs");
        Assert.True(File.Exists(path), $"مَصدَرٌ مَفقود: {path} — الأَداةُ عَمياء.");
        var text = WriteEndpointGuardTests.StripComments(File.ReadAllText(path));
        Assert.True(text.Length > 500, $"أَداةٌ عَمياء: {path} طولُه {text.Length} مِحرَفاً — لَم يُقرَأ.");
        const string from = "\"/api/v1/deals/{id:guid}/advance\"";
        const string to = ".RequireApiKey(ApiScopeCatalog.DealsWrite);";
        var start = text.IndexOf(from, StringComparison.Ordinal);
        Assert.True(start >= 0, $"‏`{from}` غَيرُ مَوجود — الأَداةُ عَمياء.");
        var end = text.IndexOf(to, start + from.Length, StringComparison.Ordinal);
        Assert.True(end > start, $"‏`{to}` لا يَتلو `{from}` — الأَداةُ عَمياء.");
        return text.Substring(start, end - start);
    }

    /// <summary>صَفقَةُ سَلَّةٍ نَقداً عِندَ <paramref name="stage"/> بِحالِ <paramref name="status"/> — مُشتَرٍ وبائِعٌ مَربوطٌ مِن الوِلادَة — في عالَمِ وَثائِقَ
    /// جَديدٍ تَمشيه <see cref="DealsService"/> الحَقيقِيَّة.</summary>
    private static (DealsService Deals, DocWorld World, Deal Deal) CashAt(DealStage stage, DealStatus status = DealStatus.Active)
    {
        var world = new DocWorld();
        var deals = new DealsService(world.Store, new ForbiddenPaymentProvider());
        var deal = new Deal
        {
            Id = Guid.NewGuid(), TenantSlug = Slug, Pattern = "marketplace", Stage = stage, Status = status, AmountSar = 120m,
            ListingTitle = "سِلعَة", InitiatorId = Buyer, InitiatorName = "مُشتَرٍ", CounterpartyId = Seller, CounterpartyName = "بائِع",
        };
        deal.Attributes[CheckoutPayMethods.AttributeKey] = CheckoutPayMethods.CashOnDelivery;
        world.Put(deal);
        return (deals, world, deal);
    }

    /// <summary>جَوابُ <c>GET /api/v1/deals/{id}</c> بِتَسَلسُلِ نُقطَتِه: <c>LoadAsync</c> ثُمَّ <c>deal is null || !IsParty</c> ⇒ ‏404.</summary>
    private static ApiOutcome ReadDoor(DealsService deals, Guid id, Guid actor)
    {
        var deal = Now(deals.LoadAsync(Slug, id));
        return deal is null || !DealApi.IsParty(deal, actor)
            ? ApiOutcome.Error(ApiErrorCatalog.NotFound)
            : ApiOutcome.Ok(DealApi.ToDto(deal));
    }

    /// <summary>جَوابُ <c>POST /api/v1/deals/{id}/advance</c> بِتَسَلسُلِ نُقطَتِه عِندَ <c>e2fcf8c</c>: <c>AdvanceAsync</c> بِلا سُؤالِ عُضوِيَّة، ثُمَّ
    /// <c>DealApi.FromAdvance</c>.</summary>
    private static ApiOutcome AdvanceDoorToday(DealsService deals, Guid id, Guid actor)
        => DealApi.FromAdvance(Now(deals.AdvanceAsync(Slug, id, actor, "غَريب", null, CancellationToken.None, from: DealsService.ExpectedStage(null))));

    /// <summary>
    /// <para><b>تَوصيفُ «اليَوم»</b> (‏2026-10-25، مُراجَعَةُ تَيّارِ W8-10 الجَولَة ١، كُبرى؛ القاعِدَة ٣ — يُقلَبُ بِاسمِه في كوميتِ الإصلاح): مَقطَعُ
    /// النُقطَةِ لا يَسأَلُ <c>DealApi.IsParty</c> ولا يُحَمِّلُ الصَفقَةَ قَبلَ <c>AdvanceAsync</c>؛ فَغَيرُ الطَرَفِ يَسمَعُ ‏403 بِمَرحَلَةِ الصَفقَةِ وحالِها
    /// وفاعِلِها المُنتَظَر — مَرحَلَتانِ مُختَلِفَتانِ جَوابانِ مُختَلِفان — و‏409 بِحالِ صَفقَةٍ أُلغِيَت، و‏404 لِمُعَرِّفٍ لا صَفقَةَ لَه: <b>عَرّافٌ لِلوُجودِ
    /// والمَرحَلَةِ والحال</b>. وبابُ القِراءَةِ لِلسائِلِ نَفسِه ‏404 في الحالاتِ كُلِّها. ولا يُكتَبُ شَيء.</para>
    /// </summary>
    [Fact]
    public void Today_a_non_party_key_on_the_api_advance_door_hears_the_deals_stage_and_status_where_the_read_door_answers_404()
    {
        var advance = ApiAdvance();
        Assert.Contains("deals.AdvanceAsync(", advance, StringComparison.Ordinal);
        Assert.DoesNotContain("DealApi.IsParty(", advance, StringComparison.Ordinal);
        Assert.DoesNotContain("deals.LoadAsync(", advance, StringComparison.Ordinal);

        var (shipDeals, shipWorld, shipping) = CashAt(DealStage.Shipping);
        var (bookDeals, bookWorld, booked) = CashAt(DealStage.Booked);
        var (goneDeals, goneWorld, cancelled) = CashAt(DealStage.Confirmed, DealStatus.Cancelled);
        var saves = (shipWorld.SaveCalls, bookWorld.SaveCalls, goneWorld.SaveCalls);

        var atShipping = AdvanceDoorToday(shipDeals, shipping.Id, Stranger);
        var atBooked   = AdvanceDoorToday(bookDeals, booked.Id, Stranger);
        var atGone     = AdvanceDoorToday(goneDeals, cancelled.Id, Stranger);
        var missing    = AdvanceDoorToday(shipDeals, Guid.NewGuid(), Stranger);

        var shipJson = ApiJson.Serialize(atShipping.Payload);
        var bookJson = ApiJson.Serialize(atBooked.Payload);
        var goneJson = ApiJson.Serialize(atGone.Payload);
        Assert.Equal(403, atShipping.Status);
        Assert.Contains("\"code\":\"actor_not_allowed\"", shipJson, StringComparison.Ordinal);
        Assert.Contains("\"stage\":\"Shipping\"", shipJson, StringComparison.Ordinal);
        Assert.Contains("\"status\":\"Active\"", shipJson, StringComparison.Ordinal);
        Assert.Contains("\"required_actor\":\"counterparty\"", shipJson, StringComparison.Ordinal);
        Assert.Equal(403, atBooked.Status);
        Assert.Contains("\"stage\":\"Booked\"", bookJson, StringComparison.Ordinal);
        Assert.NotEqual(shipJson, bookJson);
        Assert.Equal(409, atGone.Status);
        Assert.Contains("\"code\":\"deal_not_active\"", goneJson, StringComparison.Ordinal);
        Assert.Contains("\"status\":\"Cancelled\"", goneJson, StringComparison.Ordinal);
        Assert.Equal(404, missing.Status);

        foreach (var (deals, id) in new[] { (shipDeals, shipping.Id), (bookDeals, booked.Id), (goneDeals, cancelled.Id) })
            Assert.Equal(404, ReadDoor(deals, id, Stranger).Status);
        Assert.Equal(saves, (shipWorld.SaveCalls, bookWorld.SaveCalls, goneWorld.SaveCalls));
        Assert.Equal(DealStage.Shipping, shipWorld.Read<Deal>(shipping.Id.ToString())!.Stage);

        output.WriteLine($"غَيرُ الطَرَفِ عَلى تَقَدُّمِ الـAPI: «شَحن» {atShipping.Status} · «حَجز» {atBooked.Status} · مُلغاة {atGone.Status} · لا صَفقَة {missing.Status}" +
                         " — وبابُ القِراءَةِ ‏404 في الثَلاث");
    }
}
