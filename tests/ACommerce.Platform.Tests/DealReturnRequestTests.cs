using ACommerce.Kit.Notifications;
using ACommerce.Platform.I18n;
using ACommerce.Templates.Customer.Marketplace.Services;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ «طَلَب استِرجاع» — قَرارُ المالِك د30 «١» (‏2026-10-29؛ ‏ADR-047 §٥) ═══════════════════════════════════════════════════
//
// «في متاجر بيع السلع زرّ «طلب استرجاع» على صفحة الصفقة، يظهر للمشتري بعد التسليم ولمدّة سبعة أيام منه، وهي المدّة التي تنصّ عليها المادّة ١٣. الزرّ
// يُشعر البائع ويُسجَّل في سجلّ الصفقة، ولا يحرّك مالًا». و«الوقت يُمرَّر إلى القاعدة، فتبقى قابلة للقياس بلا ساعة» — فَكُلُّ صَفٍّ هُنا يُمَرِّرُ «الآن» بِيَدِه.
// وشاهِدُ المادَّةِ ١٣ الأَحمَرُ عَن قَصد (`return_action_reachable`) قُلِبَ بِاسمِه في `ReturnsPolicyTextTests`.

public class DealReturnRequestTests(ITestOutputHelper output)
{
    private const string Slug = "zz-return-request";
    private static readonly Guid Buyer  = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
    private static readonly Guid Seller = Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb");
    private static readonly DealReturnRequester AsBuyer = new(Buyer, "مُشتَرٍ");
    private static readonly DateTime DeliveredOn = new(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc);
    private const string Template = "libs/templates/ACommerce.Templates.Customer.Marketplace";

    private static T Now<T>(Task<T> task)
    {
        Assert.True(task.IsCompleted, "نِداءٌ لَم يَكتَمِل في عالَمٍ مُتَزامِن — الأَداةُ تَكذِب.");
        return task.GetAwaiter().GetResult();
    }

    /// <summary>صَفقَةُ بَيعِ سِلعَةٍ سُلِّمَت في <see cref="DeliveredOn"/> — سَطرُ «شَحن ← تَسليم» بِفاعِلِه البائِع، كَما يَكتُبُه <c>MoveAsync</c> أَو وارِدُ OTO.</summary>
    private static Deal Delivered(string pattern = "marketplace", DealStatus status = DealStatus.Active)
    {
        var deal = new Deal
        {
            Id = Guid.NewGuid(), TenantSlug = Slug, Pattern = pattern, Stage = DealStage.Delivered, Status = status, AmountSar = 120m,
            ListingTitle = "سِلعَة", InitiatorId = Buyer, InitiatorName = "مُشتَرٍ", CounterpartyId = Seller, CounterpartyName = "بائِع",
        };
        // (‏2026-10-30، تَدقيقُ الموجَة ١١ E-deals-1 — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه): **وُلِدَت مِن سَلَّة** كَما تَلِدُ `checkout/submit` صَفقَةَ بَيعِ السِلَعِ الحَيَّة —
        // كانَت الصَفقَةُ هُنا بِلا `pay_method`، أَي صَفقَةَ عَرضٍ تُعَدُّ «بَيعاً» بِنَمَطِها وَحدَه (المُقدِّمَةُ الكاذِبَةُ؛ صُفوفُها في `DealReturnGoodsGateTests`).
        deal.Attributes[CheckoutPayMethods.AttributeKey] = CheckoutPayMethods.CashOnDelivery;
        deal.Timeline.Add(new(DealStage.Offered, DealStage.Offered, "created", Buyer, "مُشتَرٍ", null, DeliveredOn.AddDays(-3)));
        deal.Timeline.Add(new(DealStage.Shipping, DealStage.Delivered, "advanced", Seller, "بائِع", null, DeliveredOn));
        return deal;
    }

    // ═══ ١) البَوّابَةُ النَقِيَّة — مُوجِبٌ وسالِبٌ لِكُلِّ رَمز ═══════════════════════════════════════════

    [Fact]
    public void The_lexicon_is_closed_at_six_codes_and_the_window_is_seven_days_of_the_goods_pattern()
    {
        Assert.Equal(new[] { "deal_not_found", "actor_not_buyer", "pattern_not_goods", "deal_not_delivered", "return_window_closed", "return_already_requested" },
            DealReturnRequest.All.ToArray());
        Assert.Equal(TimeSpan.FromDays(7), DealReturnRequest.Window);
        Assert.Equal("marketplace", DealReturnRequest.GoodsPattern);
        Assert.True(DealPatternCatalog.Patterns.ContainsKey(DealReturnRequest.GoodsPattern));
    }

    [Fact]
    public void DealNotFound_negative_no_deal_and_positive_a_delivered_deal()
    {
        Assert.Equal(DealReturnRequest.DealNotFound, DealReturnRequest.Validate(null, AsBuyer, DeliveredOn)!.Code);
        Assert.Null(DealReturnRequest.Validate(Delivered(), AsBuyer, DeliveredOn.AddDays(1)));
    }

    [Theory]
    [InlineData("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb")]   // البائِع
    [InlineData("cccccccc-cccc-4ccc-8ccc-cccccccccccc")]   // غَريب
    public void ActorNotBuyer_negative_the_seller_or_a_stranger_and_positive_the_buyer(string actor)
    {
        var deal = Delivered();
        Assert.Equal(DealReturnRequest.ActorNotBuyer,
            DealReturnRequest.Validate(deal, new DealReturnRequester(Guid.Parse(actor), "فاعِل"), DeliveredOn.AddDays(1))!.Code);
        Assert.True(DealReturnRequest.IsAllowed(deal, AsBuyer, DeliveredOn.AddDays(1)));
    }

    [Theory]
    [InlineData("trip")]
    [InlineData("rental")]
    [InlineData("service")]
    [InlineData("classifieds")]
    [InlineData("default")]
    public void PatternNotGoods_negative_every_other_pattern_and_positive_marketplace(string pattern)
    {
        Assert.Equal(DealReturnRequest.PatternNotGoods, DealReturnRequest.Validate(Delivered(pattern), AsBuyer, DeliveredOn.AddDays(1))!.Code);
        Assert.Null(DealReturnRequest.Validate(Delivered("marketplace"), AsBuyer, DeliveredOn.AddDays(1)));
    }

    /// <summary>السالِب: لا انتِقالَ إلى «تَسليم» بَعد (عِندَ «شَحن»)، أَو صَفقَةٌ مُلغاةٌ أَو في نِزاع؛ والمُوجِب: فَعّالَةٌ عِندَ «تَسليم» ومُكتَمِلَةٌ بَعدَ التَقييم.</summary>
    [Fact]
    public void DealNotDelivered_negative_before_delivery_or_cancelled_or_disputed_and_positive_active_or_completed()
    {
        var shipping = Delivered();
        shipping.Timeline.RemoveAt(1);
        shipping.Stage = DealStage.Shipping;
        Assert.Null(DealReturnRequest.DeliveredAt(shipping));
        Assert.Equal(DealReturnRequest.DealNotDelivered, DealReturnRequest.Validate(shipping, AsBuyer, DeliveredOn)!.Code);
        Assert.Equal(DealReturnRequest.DealNotDelivered, DealReturnRequest.Validate(Delivered(status: DealStatus.Cancelled), AsBuyer, DeliveredOn)!.Code);
        Assert.Equal(DealReturnRequest.DealNotDelivered, DealReturnRequest.Validate(Delivered(status: DealStatus.Disputed), AsBuyer, DeliveredOn)!.Code);

        Assert.Null(DealReturnRequest.Validate(Delivered(status: DealStatus.Active), AsBuyer, DeliveredOn));
        var reviewed = Delivered(status: DealStatus.Completed);
        reviewed.Timeline.Add(new(DealStage.Delivered, DealStage.Reviewed, "advanced", Buyer, "مُشتَرٍ", null, DeliveredOn.AddDays(2)));
        reviewed.Stage = DealStage.Reviewed;
        Assert.Equal(DeliveredOn, DealReturnRequest.DeliveredAt(reviewed));
        Assert.Null(DealReturnRequest.Validate(reviewed, AsBuyer, DeliveredOn.AddDays(3)));
    }

    /// <summary><b>قُلِبَ بِاسمِه</b> (‏2026-10-30، تَدقيقُ الموجَة ١١ E-deals-3؛ تَوصيفُه <c>7b17822</c> — كانَ
    /// <c>WindowClosed_negative_a_second_past_seven_days_and_positive_up_to_the_seventh_day</c>: الحَدُّ <c>DeliveredOn + 7 أَيّام</c> بِالثانِيَة): التَسليمُ
    /// ‏2026-10-20 ‏15:00 بِالرِياض ⇒ الحَدُّ <b>آخِرُ يَومِ 2026-10-27 بِتَوقيتِ الرِياض</b> — ‏23:59:59 مَقبولٌ، وبَعدَه بِثانِيَةٍ مَردود؛ والوَقتُ مُمَرَّرٌ لا مَقروءٌ مِن ساعَة.</summary>
    [Fact]
    public void WindowClosed_negative_a_second_past_the_seventh_riyadh_day_and_positive_up_to_its_last_second()
    {
        var deal = Delivered();
        var lastSecond = new DateTime(2026, 10, 27, 20, 59, 59, DateTimeKind.Utc);   // ‏23:59:59 بِالرِياض
        Assert.Equal(ACommerce.Kit.Culture.CultureTime.EndOfDayUtc(new DateOnly(2026, 10, 27)), DealReturnRequest.WindowEndsAt(deal));
        foreach (var inside in new[] { DeliveredOn, DeliveredOn.AddDays(3.5), DeliveredOn.AddDays(7), DeliveredOn.AddDays(7).AddSeconds(1), lastSecond })
            Assert.True(DealReturnRequest.IsAllowed(deal, AsBuyer, inside), $"{inside:O} داخِلَ المُدَّة.");
        Assert.Equal(DealReturnRequest.WindowClosed, DealReturnRequest.Validate(deal, AsBuyer, lastSecond.AddSeconds(1))!.Code);
        Assert.Equal(DealReturnRequest.WindowClosed, DealReturnRequest.Validate(deal, AsBuyer, DeliveredOn.AddDays(30))!.Code);
    }

    [Fact]
    public void AlreadyRequested_negative_a_second_request_and_positive_the_first()
    {
        var deal = Delivered();
        Assert.Null(DealReturnRequest.Validate(deal, AsBuyer, DeliveredOn.AddDays(1)));
        deal.Timeline.Add(new(DealStage.Delivered, DealStage.Delivered, DealEvent.ReturnRequested, Buyer, "مُشتَرٍ", "مَقاسٌ خاطِئ", DeliveredOn.AddDays(1)));
        Assert.Equal(DealReturnRequest.AlreadyRequested, DealReturnRequest.Validate(deal, AsBuyer, DeliveredOn.AddDays(2))!.Code);
    }

    /// <summary>التَرتيب: الهُوِيَّةُ قَبلَ كُلِّ حالَة (فَالبائِعُ لا يَعرِفُ بِالرَدِّ أَمُدَّةٌ أَم نَمَط)، والنَمَطُ قَبلَ التَسليم، والتَسليمُ قَبلَ المُدَّة، والمُدَّةُ قَبلَ التَكرار.</summary>
    [Fact]
    public void The_gate_answers_absence_identity_pattern_delivery_window_then_repetition()
    {
        var late = Delivered("trip", DealStatus.Cancelled);
        late.Timeline.Add(new(DealStage.Delivered, DealStage.Delivered, DealEvent.ReturnRequested, Buyer, "مُشتَرٍ", null, DeliveredOn));
        var seller = new DealReturnRequester(Seller, "بائِع");
        var far = DeliveredOn.AddDays(30);
        Assert.Equal(DealReturnRequest.ActorNotBuyer, DealReturnRequest.Validate(late, seller, far)!.Code);
        Assert.Equal(DealReturnRequest.PatternNotGoods, DealReturnRequest.Validate(late, AsBuyer, far)!.Code);
        late.Pattern = "marketplace";
        Assert.Equal(DealReturnRequest.DealNotDelivered, DealReturnRequest.Validate(late, AsBuyer, far)!.Code);
        late.Status = DealStatus.Active;
        Assert.Equal(DealReturnRequest.WindowClosed, DealReturnRequest.Validate(late, AsBuyer, far)!.Code);
        Assert.Equal(DealReturnRequest.AlreadyRequested, DealReturnRequest.Validate(late, AsBuyer, DeliveredOn)!.Code);
    }

    // ═══ ٢) الخِدمَة — سَطرٌ في السِجِلِّ بِلا مال ═══════════════════════════════════════════════════════

    [Fact]
    public void The_request_writes_one_dated_line_moves_no_stage_and_touches_no_money_and_a_second_writes_nothing()
    {
        var world = new DocWorld();
        var pay = new DealMoneyClaimFirstTests.RecordingPaymentProvider();
        var deals = new DealsService(world.Store, pay);
        var deal = Delivered();
        world.Put(deal);
        var at = DeliveredOn.AddDays(2);

        var res = Now(deals.RequestReturnAsync(Slug, deal.Id, AsBuyer, "لا يُناسِب", at));
        Assert.True(res.Ok, res.Violation?.MessageAr);
        var stored = world.Read<Deal>(deal.Id.ToString())!;
        Assert.Equal((DealStatus.Active, DealStage.Delivered), (stored.Status, stored.Stage));
        var line = stored.Timeline[^1];
        Assert.Equal((DealEvent.ReturnRequested, (Guid?)Buyer, "لا يُناسِب", at), (line.Action, line.ActorId, line.Note, line.At));
        Assert.Empty(pay.Calls);
        Assert.Equal(deal.Refs.Count, stored.Refs.Count);

        var saves = world.SaveCalls;
        var again = Now(deals.RequestReturnAsync(Slug, deal.Id, AsBuyer, "ثانِيَة", at.AddHours(1)));
        Assert.Equal(DealReturnRequest.AlreadyRequested, again.Violation!.Code);
        var late = Now(deals.RequestReturnAsync(Slug, deal.Id, new DealReturnRequester(Seller, "بائِع"), "x", at));
        Assert.Equal(DealReturnRequest.ActorNotBuyer, late.Violation!.Code);
        Assert.Equal(saves, world.SaveCalls);
        output.WriteLine($"طَلَبُ استِرجاع: {line.Action} عِندَ {stored.Stage} · نِداءاتُ المال {pay.Calls.Count} · الثانِيَة {again.Violation.Code}");
    }

    // ═══ ٣) الإشعار — «الزرّ يُشعر البائع» ═══════════════════════════════════════════════════════════

    [Fact]
    public void The_vendor_is_told_once_and_never_the_buyer_himself()
    {
        Assert.Equal(Seller, DealNotices.ReturnRecipient(Delivered()));
        var unbound = Delivered();
        unbound.CounterpartyId = null;
        unbound.Refs[DealsService.ListingOwnerRef] = Seller.ToString();
        Assert.Equal(Seller, DealNotices.ReturnRecipient(unbound));
        var own = Delivered();
        own.CounterpartyId = Buyer;
        Assert.Null(DealNotices.ReturnRecipient(own));

        var docs = new DocWorld();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSignalR();
        var hub = services.BuildServiceProvider().GetRequiredService<IHubContext<ACommerce.Kit.Realtime.Server.RealtimeHub>>();
        var notices = new DealNotices(docs.Store, hub, new WebPushService(new ConfigurationBuilder().Build(), NullLogger<WebPushService>.Instance));
        var l = new L(new HttpContextAccessor());
        var deal = Delivered();
        Assert.True(Now(notices.NotifyReturnRequestedAsync(Slug, deal, l)));
        var n = Assert.Single(docs.Stored.OfType<Notification>());
        Assert.Equal((Seller, DealNotices.NewDealType, $"/{Slug}/deals/{deal.Id}"), (n.UserId, n.Type, n.RelatedUrl));
        Assert.Equal("طَلَبَ المُشتَري استِرجاعَ «سِلعَة» بَعدَ تَسليمِه — تَواصَل مَعَه لِتَرتيبِ الرَّدِّ والاستِرداد، وافتَحِ الصَّفقَة لِلتَفاصيل.", n.Body);
        Assert.False(Now(notices.NotifyReturnRequestedAsync(Slug, own, l)));
    }

    // ═══ ٤) البابُ والصَفحَة ═══════════════════════════════════════════════════════════════════════

    private static string Source(params string[] parts)
    {
        var path = Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, Path.Combine(parts));
        Assert.True(File.Exists(path), $"مَصدَرٌ مَفقود: {path} — الأَداةُ عَمياء.");
        var text = File.ReadAllText(path);
        Assert.True(text.Length > 500, $"أَداةٌ عَمياء: {path}.");
        return text;
    }

    [Fact]
    public void The_return_door_checks_the_session_first_passes_the_clock_and_tells_the_vendor_only_on_success()
    {
        var endpoints = WriteEndpointGuardTests.StripComments(Source(Template, "MarketplaceTemplateExtensions.cs"));
        var at = endpoints.IndexOf("app.MapPost(\"/{slug}/deals/{id:guid}/return\"", StringComparison.Ordinal);
        Assert.True(at > 0, "بابُ الاستِرجاعِ غَيرُ مَوجود.");
        var door = endpoints[at..endpoints.IndexOf(".RequireStoreWritable();", at, StringComparison.Ordinal)];
        var session = door.IndexOf("AuthSession.ResolveToken(req, slug)", StringComparison.Ordinal);
        Assert.True(session > 0 && session < door.IndexOf("req.Form[", StringComparison.Ordinal), "الجَلسَةُ لا تَسبِقُ قِراءَةَ الحُقول.");
        Assert.Contains("deals.RequestReturnAsync(slug, id, new Services.Deals.DealReturnRequester(userId, userName),", door, StringComparison.Ordinal);
        Assert.Contains("DateTime.UtcNow);", door, StringComparison.Ordinal);
        Assert.Contains("if (res.Ok) await notices.NotifyReturnRequestedAsync(slug, res.Deal!, l);", door, StringComparison.Ordinal);
        Assert.Contains("}).DisableAntiforgery().RequireFormBody()", door, StringComparison.Ordinal);
    }

    [Fact]
    public void The_member_page_draws_the_return_form_from_the_same_gate_at_request_time()
    {
        var page = WriteEndpointGuardTests.StripMarkupComments(Source(Template, "Components", "Pages", "MyDealDetail.razor"));
        var form = page.IndexOf("<form method=\"post\" action=\"@($\"/{Slug}/deals/{deal.Id}/return\")\">", StringComparison.Ordinal);
        Assert.True(form > 0, "استِمارَةُ الاستِرجاعِ غَيرُ مَوجودَة.");
        Assert.True(page.LastIndexOf("@if (canReturn)", form, StringComparison.Ordinal) > 0, "الاستِمارَةُ خارِجَ شَرطِ البَوّابَة.");
        Assert.Contains("canReturn = DealReturnRequest.IsAllowed(deal, new DealReturnRequester(uid, Auth.UserName ?? \"\"), DateTime.UtcNow);", page, StringComparison.Ordinal);
        Assert.Contains("DealEvent.ReturnRequested => L[\"deals.detail.event_return_requested\"]", page, StringComparison.Ordinal);
        Assert.Contains("DealEvent.ReturnRequested => L[\"studio.deal.event_return_requested\"]",
            Source(Template, "Components", "Pages", "StudioAppDealDetail.razor"), StringComparison.Ordinal);
        foreach (var code in DealReturnRequest.All)
            Assert.NotNull(DealRefusalTexts.KeyFor(code));
    }
}
