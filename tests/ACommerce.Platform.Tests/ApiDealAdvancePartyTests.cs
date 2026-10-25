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
//
// **ثُمَّ قُلِبَ بِاسمِه** (‏2026-10-25 — ولا يُعادُ حَرفٌ فَوقَه؛ التَوصيفُ `e716f46`): التَحميلُ و`IsParty` و‏404 قَبلَ `AdvanceAsync`، بِنَسَقِ `/cancel` —
// فَغَيرُ الطَرَفِ يَسمَعُ ما يَسمَعُه عَن مُعَرِّفٍ لا صَفقَةَ لَه بايتاً بِبايت، والطَرَفُ يُجابُ كَما كان؛ ولا مَرحَلَةَ في الكاتالوجِ يَطلُبُ فاعِلُها
// `platform`، فَلا يَرُدُّ السُؤالُ أَحَداً كانَت الخِدمَةُ سَتُجيزُه.

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

    /// <summary>جَوابُ <c>POST /api/v1/deals/{id}/advance</c> بِتَسَلسُلِ نُقطَتِه: <c>LoadAsync</c> ثُمَّ <c>found is null || !IsParty</c> ⇒ ‏404، ثُمَّ
    /// <c>AdvanceAsync</c> ثُمَّ <c>DealApi.FromAdvance</c> — يُثَبِّتُ التَسَلسُلَ في المَصدَرِ <see cref="Steps"/>.
    /// (‏2026-10-25 — ولا يُعادُ حَرفٌ فَوقَه: كانَ <c>AdvanceDoorToday</c> بِتَسَلسُلِها عِندَ <c>e2fcf8c</c> — <c>AdvanceAsync</c> بِلا سُؤالِ عُضوِيَّة.)</summary>
    private static ApiOutcome AdvanceDoor(DealsService deals, Guid id, Guid actor, string? expectedStage = null)
    {
        var found = Now(deals.LoadAsync(Slug, id));
        if (found is null || !DealApi.IsParty(found, actor))
            return ApiOutcome.Error(ApiErrorCatalog.NotFound);
        return DealApi.FromAdvance(Now(deals.AdvanceAsync(Slug, id, actor, "سائِل", null, CancellationToken.None, from: DealsService.ExpectedStage(expectedStage))));
    }

    /// <summary>مَواضِعُ الخُطُواتِ الأَربَعِ في المَقطَع — وكُلٌّ مَوجودٌ مَرَّةً واحِدَة، وإلّا فَالأَداةُ عَمياء.</summary>
    internal static (int Load, int Party, int PartyRefusal, int Advance) Steps(string advance)
    {
        int Once(string needle)
        {
            var at = advance.IndexOf(needle, StringComparison.Ordinal);
            Assert.True(at > 0, $"‏`{needle}` غَيرُ مَوجودٍ في مَقطَعِ التَقَدُّم — الأَداةُ عَمياء.");
            Assert.Equal(-1, advance.IndexOf(needle, at + needle.Length, StringComparison.Ordinal));
            return at;
        }
        return (Once("await deals.LoadAsync(p.TenantSlug, id, http.RequestAborted)"),
                Once("!DealApi.IsParty(found, p.ActorUserId)"),
                Once("ApiOutcome.Error(ApiErrorCatalog.NotFound)"),
                Once("await deals.AdvanceAsync("));
    }

    /// <summary>
    /// <para><b>قُلِبَ بِاسمِه</b> (‏2026-10-25، مُراجَعَةُ تَيّارِ W8-10 الجَولَة ١، كُبرى؛ القاعِدَة ٦): كانَ
    /// <c>Today_a_non_party_key_on_the_api_advance_door_hears_the_deals_stage_and_status_where_the_read_door_answers_404</c> (<c>e716f46</c>) — ‏403 بِمَرحَلَةِ
    /// «شَحن» ثُمَّ بِمَرحَلَةِ «حَجز»، و‏409 بِحالِ المُلغاة، و‏404 لِمُعَرِّفٍ بِلا صَفقَة. <b>وصارَ</b>: التَحميلُ و<c>IsParty</c> و‏404 قَبلَ <c>AdvanceAsync</c> في
    /// المَقطَع، فَغَيرُ الطَرَفِ يَسمَعُ في الحالاتِ الأَربَعِ جَواباً واحِداً بايتاً بِبايت — هُوَ جَوابُ بابِ القِراءَةِ لَه — بِلا <c>stage</c> ولا <c>status</c>؛
    /// ولا يُكتَبُ شَيء.</para>
    /// </summary>
    [Fact]
    public void A_non_party_key_on_the_api_advance_door_hears_404_byte_for_byte_as_for_a_missing_deal_and_as_the_read_door_answers()
    {
        var (load, party, partyRefusal, advanceCall) = Steps(ApiAdvance());
        Assert.True(load < party && party < partyRefusal && partyRefusal < advanceCall,
            $"load={load} party={party} 404={partyRefusal} advance={advanceCall}");

        var (shipDeals, shipWorld, shipping) = CashAt(DealStage.Shipping);
        var (bookDeals, bookWorld, booked) = CashAt(DealStage.Booked);
        var (goneDeals, goneWorld, cancelled) = CashAt(DealStage.Confirmed, DealStatus.Cancelled);
        var saves = (shipWorld.SaveCalls, bookWorld.SaveCalls, goneWorld.SaveCalls);

        var answers = new[]
        {
            AdvanceDoor(shipDeals, shipping.Id, Stranger),
            AdvanceDoor(bookDeals, booked.Id, Stranger, "Booked"),
            AdvanceDoor(goneDeals, cancelled.Id, Stranger),
            AdvanceDoor(shipDeals, Guid.NewGuid(), Stranger),
        };
        var read = ApiJson.Serialize(ReadDoor(shipDeals, shipping.Id, Stranger).Payload);
        foreach (var a in answers)
        {
            var json = ApiJson.Serialize(a.Payload);
            Assert.Equal(404, a.Status);
            Assert.Equal(read, json);
            Assert.DoesNotContain("\"stage\"", json, StringComparison.Ordinal);
            Assert.DoesNotContain("\"status\"", json, StringComparison.Ordinal);
        }
        Assert.Equal(saves, (shipWorld.SaveCalls, bookWorld.SaveCalls, goneWorld.SaveCalls));
        Assert.Equal(DealStage.Shipping, shipWorld.Read<Deal>(shipping.Id.ToString())!.Stage);

        output.WriteLine($"غَيرُ الطَرَفِ عَلى تَقَدُّمِ الـAPI: {string.Join(" · ", answers.Select(a => a.Status))} — جَوابٌ واحِد ({System.Text.Encoding.UTF8.GetByteCount(read)} بايتاً) هُوَ جَوابُ بابِ القِراءَة؛" +
                         $" والتَرتيب: التَحميل@{load} → IsParty@{party} → ‏404@{partyRefusal} → AdvanceAsync@{advanceCall}");
    }

    /// <summary><b>والطَرَفُ يُجابُ كَما كان</b> (‏2026-10-25): المُشتَري عِندَ «شَحن» لَيسَ فاعِلَ «تَسليم» فَيَسمَعُ ‏403 <c>actor_not_allowed</c> بِالمَرحَلَةِ الَّتي
    /// يَقرَؤُها مِن بابِ القِراءَةِ أَصلاً (‏200)، وعِندَ المُلغاةِ ‏409 <c>deal_not_active</c>، والبائِعُ يُحَرِّكُها إلى «تَسليم» (‏200) — فَالـ‏404 لِغَيرِ الطَرَفِ وَحدَه.</summary>
    [Fact]
    public void A_party_on_the_api_advance_door_still_hears_what_it_can_read_and_the_required_actor_still_moves_the_deal()
    {
        var (deals, world, deal) = CashAt(DealStage.Shipping);
        var buyer = AdvanceDoor(deals, deal.Id, Buyer);
        var buyerJson = ApiJson.Serialize(buyer.Payload);
        Assert.Equal(403, buyer.Status);
        Assert.Contains("\"code\":\"actor_not_allowed\"", buyerJson, StringComparison.Ordinal);
        Assert.Contains("\"stage\":\"Shipping\"", buyerJson, StringComparison.Ordinal);
        Assert.Equal(200, ReadDoor(deals, deal.Id, Buyer).Status);
        Assert.Equal(DealStage.Shipping, world.Read<Deal>(deal.Id.ToString())!.Stage);

        var seller = AdvanceDoor(deals, deal.Id, Seller, "Shipping");
        Assert.Equal(200, seller.Status);
        Assert.Contains("\"stage\":\"Delivered\"", ApiJson.Serialize(seller.Payload), StringComparison.Ordinal);
        Assert.Equal(DealStage.Delivered, world.Read<Deal>(deal.Id.ToString())!.Stage);

        var (goneDeals, _, cancelled) = CashAt(DealStage.Confirmed, DealStatus.Cancelled);
        var gone = AdvanceDoor(goneDeals, cancelled.Id, Buyer);
        Assert.Equal(409, gone.Status);
        Assert.Contains("\"code\":\"deal_not_active\"", ApiJson.Serialize(gone.Payload), StringComparison.Ordinal);
        output.WriteLine($"الطَرَف: المُشتَري عِندَ «شَحن» {buyer.Status} · البائِعُ {seller.Status} ← Delivered · المُشتَري عَلى المُلغاة {gone.Status}");
    }

    /// <summary>المَراحِلُ الَّتي يَطلُبُ فاعِلُها <c>platform</c> بِحَسَبِ <paramref name="actorOf"/> — الفاحِصُ النَقِيّ؛ فارِغُه «لا مَرحَلَة».</summary>
    internal static IReadOnlyList<DealStage> PlatformStages(IEnumerable<DealStage> stages, Func<DealStage, string> actorOf)
        => stages.Where(s => actorOf(s) == DealPatternCatalog.PlatformActor).ToList();

    /// <summary>
    /// <para><b>ولا يَرُدُّ السُؤالُ أَحَداً كانَت الخِدمَةُ سَتُجيزُه</b> (‏2026-10-25): <c>IsActorAllowed</c> يُجيزُ غَيرَ الطَرَفِ في فَرعٍ واحِد —
    /// فاعِلُ المَرحَلَةِ <c>platform</c> — والخِدمَةُ تَسأَلُ <c>DealsPolicy.Actor</c> لِلمَرحَلَةِ التالِيَة (<c>RequiredActorToLeave</c>). فَيُعَدُّ كُلُّ
    /// <see cref="DealStage"/> وكُلُّ صَفٍّ في أَنماطِ الكاتالوجِ والاحتِياطيّ: صِفرٌ يَطلُبُ <c>platform</c>. ويُحمِرُه يَومَ تُضافُ مَرحَلَةٌ لِلمَنصَّة — فَيُسأَلُ
    /// يَومَها أَيَعبُرُ مِفتاحُ الـAPI إلَيها. والفاحِصُ يُقاسُ بِعَيِّنَةٍ مَحقونَة (القاعِدَة ١٠).</para>
    /// </summary>
    [Fact]
    public void No_catalog_stage_asks_for_the_platform_actor_so_the_party_check_refuses_no_one_the_service_lets_through()
    {
        var stages = Enum.GetValues<DealStage>();
        var rows = DealPatternCatalog.Patterns.Values.Append(DealPatternCatalog.Fallback).SelectMany(d => d.Stages).ToList();
        Assert.True(stages.Length > 0 && rows.Count > 0, $"أَداةٌ عَمياء: {stages.Length} مَرحَلَة، {rows.Count} صَفّاً.");

        Assert.Empty(PlatformStages(stages, DealsPolicy.Actor));
        Assert.DoesNotContain(rows, r => r.Actor == DealPatternCatalog.PlatformActor);

        var injected = PlatformStages(stages, s => s == DealStage.Paid ? DealPatternCatalog.PlatformActor : "initiator");
        Assert.Equal(new[] { DealStage.Paid }, injected);
        output.WriteLine($"فُحِصَت {stages.Length} مَراحِل و{rows.Count} صَفّاً في {DealPatternCatalog.Patterns.Count + 1} أَنماط: صِفرٌ يَطلُبُ platform · العَيِّنَةُ المَحقونَة: {injected.Count}");
    }
}
