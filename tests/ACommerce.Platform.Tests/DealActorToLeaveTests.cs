using ACommerce.Kit.Payments;
using ACommerce.Templates.Customer.Marketplace.Api;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using ACommerce.Templates.Customer.Marketplace.Services.Ux;
using Xunit;

namespace ACommerce.Platform.Tests;

// ═══ مَن يُخرِجُ الصَفقَةَ مِن مَرحَلَتِها — فاعِلُ المَرحَلَةِ الَّتي تَدخُلُها (‏ADR-033 §٦‑ط) ═══
//
// **المَعنى المَوثَّقُ لِعَمودِ الفاعِل** — «مَن يُؤَدّي المَرحَلَة»: تَعليقاتُ الأَنماطِ الخَمسَةِ في
// `DealPatternCatalog` («بائِع يُؤَكِّد»، «سائِق يَقبَل»، «مالِك يَقبَل»، «عَرض سِعر» — كُلُّها `Booked`
// بِفاعِلِها `counterparty`)، وشَرحُ التَدَفُّقِ لِكُلِّ مَرحَلَةٍ «يُحَرِّكها: <فاعِلُها>»، وصَفُّ
// `Reviewed = either` الَّذي لا يُقرَأُ إلّا كَذلك (آخِرُ مَرحَلَةٍ في كُلِّ نَمَطٍ تَبلُغُها). وكانَت
// الخِدمَةُ والشاشَةُ و`DealTurnView` تَسأَلُ فاعِلَ المَرحَلَةِ **الحالِيَّة** — إزاحَةُ مَرحَلَةٍ مَقيسَةٌ
// بِمِجَسٍّ عَلى الخِدمَةِ الحَقيقِيَّةِ قَبلَ التَبديل: في كُلِّ نَمَطٍ يُخرِجُ المُشتَري `Offered` بِنَفسِه
// والبائِعُ المَربوطُ مَردود، ويُعلِنُ المُشتَري «شَحن» مِن `Paid` والبائِعُ لا يَشحَنُ صَفقَةَ بِطاقَة،
// ويُعلِنُ المُستَأجِرُ «تَسليم» مِن `Paid` في الإيجار، ولا يَبلُغُ المَسارُ الإداريُّ ما بَعدَ `Paid`.
//
// **والتَوصيفُ قَبلَه مُثَبَّتٌ في المَوجَةِ الثانِيَة — يُستَشهَدُ ولا يُكَرَّر** (القاعِدَة ٣):
// `DealCounterpartyCharacterizationTests` (فاعِلُ `AdvanceAsync`، والمَسارُ الإداريّ)، و`DealCashPolicyTests`
// («دَورُكَ الآن … تَأكيد: شَحن» لِمُشتَري النَقد)، و`DealCashPathCharacterizationTests` (الدَورُ عِندَ `Paid`)،
// و`DealOwnListingCharacterizationTests` (يَقِفُ عِندَ `Booked`)، و`DealTurnViewTests`، و`DealPatternFlowBridgeTests`
// — وقُلِبَت صُفوفُها الَّتي رَمَّزَت الإزاحَةَ بِأَسمائِها في كوميتِ هذا المِلَفّ.
//
// **وهذا المِلَفُّ يَحرُسُ القَرار**: لِكُلِّ انتِقالٍ في كُلِّ نَمَطٍ — بِطاقَةً ونَقداً — مُوجِبٌ (مَن
// يَدخُلُها يُحَرِّكُها عَلى `DealsService` الحَقيقِيَّة) وسالِبٌ (مَن سِواه يُرَدُّ ولا يُكتَب)؛ والدالَّةُ
// الواحِدَةُ في الخِدمَةِ والقَبولِ والواجِهَةِ والشاشَةِ والـAPI؛ وفاحِصٌ يَعُدُّ أَلّا يَبقى مَوضِعٌ
// يَسأَلُ فاعِلَ مَرحَلَةِ صَفقَةٍ قائِمَة؛ والمَسارُ الإداريُّ يَمضي بِجانِبِ المَتجَرِ ويَقِفُ عِندَ خُطوَةِ المُشتَري.

public class DealActorToLeaveTests
{
    private const string Slug = "zz-actor-to-leave";
    private static readonly Guid Buyer    = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
    private static readonly Guid Seller   = Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb");
    private static readonly Guid AppOwner = Guid.Parse("cccccccc-cccc-4ccc-8ccc-cccccccccccc");
    private static readonly Guid Stranger = Guid.Parse("dddddddd-dddd-4ddd-8ddd-dddddddddddd");

    private static string RepoRoot => ThemeZeroEquivalenceTests.RepoRoot;
    private const string Template = "libs/templates/ACommerce.Templates.Customer.Marketplace";

    /// <summary>مُزَوِّدٌ يَرمي عِندَ أَيِّ نِداء — لا مُعَرِّفَ دَفعٍ عَلى صَفَقاتِ هذا المِلَفّ، فَإن مَسَّ
    /// المَشيُ المالَ أَحمَرَّ الصَفُّ بَدَلَ أَن يُحاكى.</summary>
    private sealed class ForbiddenPaymentProvider : IPaymentProvider
    {
        private static InvalidOperationException Touched() => new("المَشيُ بِلا مُعَرِّفِ دَفعٍ لا يَمَسُّ المال — نودِيَ مُزَوِّدُ الدَفع.");
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

    /// <summary>صَفقَةٌ في <paramref name="stage"/> مُبادِرُها المُشتَري وطَرَفُها <paramref name="counterparty"/>،
    /// في عالَمِ وَثائِقَ جَديدٍ تَمشيه <c>DealsService</c> الحَقيقِيَّة.</summary>
    private static (DealsService Deals, DocWorld World, Deal Deal) At(
        string pattern, string? payMethod, DealStage stage, Guid? counterparty)
    {
        var world = new DocWorld();
        var deals = new DealsService(world.Store, new ForbiddenPaymentProvider());
        var deal = new Deal
        {
            Id = Guid.NewGuid(), TenantSlug = Slug, Pattern = pattern, Stage = stage, AmountSar = 100,
            InitiatorId = Buyer, InitiatorName = "مُشتَرٍ", CounterpartyId = counterparty,
        };
        if (payMethod is not null) deal.Attributes[CheckoutPayMethods.AttributeKey] = payMethod;
        world.Put(deal);
        return (deals, world, deal);
    }

    private static Guid Id(string side) => side switch
    {
        "initiator"    => Buyer,
        "counterparty" => Seller,
        _              => Stranger,
    };

    // ═══ ١) كُلُّ انتِقالٍ في كُلِّ نَمَط: مَن يَدخُلُها يُحَرِّكُها، ومَن سِواه يُرَدّ ═══════════

    /// <summary>
    /// <para><b>الجَدوَلُ كَما يُكتَبُ في ‏ADR-033 §٦‑ط</b>: إلى <c>Booked</c> الطَرَفُ المُقابِل (البائِعُ يَقبَل،
    /// السائِقُ يَأخُذ — <b>المُشتَري لا يَحجِزُ طَلَبَه</b>)؛ إلى <c>Confirmed</c> أَيُّهُما؛ إلى <c>Paid</c> المُبادِر
    /// (<b>البائِعُ لا «يَدفَع»</b>)؛ إلى <c>Shipping</c> الطَرَف (<b>البائِعُ يَشحَن</b>)؛ إلى <c>Delivered</c> الطَرَف؛
    /// إلى <c>Received</c> المُبادِر (<b>المُستَأجِرُ يُؤَكِّدُ الاستِلام</b>)؛ إلى <c>Reviewed</c> أَيُّهُما.</para>
    ///
    /// <para>لِكُلِّ صَفٍّ مُوجِبٌ وسالِب: كُلُّ هُوِيَّةٍ في <paramref name="allowed"/> تُحَرِّكُ الصَفقَةَ إلى
    /// <paramref name="next"/> عَلى الخِدمَةِ الحَقيقِيَّة، وكُلُّ هُوِيَّةٍ سِواها مِن الثَلاث (المُشتَري، والبائِعُ
    /// الطَرَف، والغَريب) تُرَدُّ بِرَمزِ الفاعِلِ المَطلوب ولا يُكتَبُ شَيء. والنَقدُ يَتَخَطّى <c>Paid</c>.</para>
    /// </summary>
    [Theory]
    // marketplace — بِطاقَة
    [InlineData("marketplace", "card", DealStage.Offered,   DealStage.Booked,    "counterparty")]
    [InlineData("marketplace", "card", DealStage.Booked,    DealStage.Confirmed, "initiator,counterparty")]
    [InlineData("marketplace", "card", DealStage.Confirmed, DealStage.Paid,      "initiator")]
    [InlineData("marketplace", "card", DealStage.Paid,      DealStage.Shipping,  "counterparty")]
    [InlineData("marketplace", "card", DealStage.Shipping,  DealStage.Delivered, "counterparty")]
    [InlineData("marketplace", "card", DealStage.Delivered, DealStage.Reviewed,  "initiator,counterparty")]
    // marketplace — نَقدٌ عِندَ الاستِلام (‏Paid مُسقَطَة)
    [InlineData("marketplace", "cash", DealStage.Offered,   DealStage.Booked,    "counterparty")]
    [InlineData("marketplace", "cash", DealStage.Booked,    DealStage.Confirmed, "initiator,counterparty")]
    [InlineData("marketplace", "cash", DealStage.Confirmed, DealStage.Shipping,  "counterparty")]
    [InlineData("marketplace", "cash", DealStage.Shipping,  DealStage.Delivered, "counterparty")]
    [InlineData("marketplace", "cash", DealStage.Delivered, DealStage.Reviewed,  "initiator,counterparty")]
    // rental — بِطاقَة
    [InlineData("rental",      "card", DealStage.Offered,   DealStage.Booked,    "counterparty")]
    [InlineData("rental",      "card", DealStage.Booked,    DealStage.Confirmed, "initiator,counterparty")]
    [InlineData("rental",      "card", DealStage.Confirmed, DealStage.Paid,      "initiator")]
    [InlineData("rental",      "card", DealStage.Paid,      DealStage.Delivered, "counterparty")]
    [InlineData("rental",      "card", DealStage.Delivered, DealStage.Received,  "initiator")]
    [InlineData("rental",      "card", DealStage.Received,  DealStage.Reviewed,  "initiator,counterparty")]
    // rental — نَقد
    [InlineData("rental",      "cash", DealStage.Confirmed, DealStage.Delivered, "counterparty")]
    // trip (مَتاجِرُ الطَلَبِ عِندَ الحاجَة: سائِقٌ وراكِب) — لا Paid في النَمَط
    [InlineData("trip",        null,   DealStage.Offered,   DealStage.Booked,    "counterparty")]
    [InlineData("trip",        null,   DealStage.Booked,    DealStage.Confirmed, "initiator,counterparty")]
    [InlineData("trip",        null,   DealStage.Confirmed, DealStage.Delivered, "counterparty")]
    [InlineData("trip",        null,   DealStage.Delivered, DealStage.Reviewed,  "initiator,counterparty")]
    // service
    [InlineData("service",     "card", DealStage.Offered,   DealStage.Booked,    "counterparty")]
    [InlineData("service",     "card", DealStage.Confirmed, DealStage.Paid,      "initiator")]
    [InlineData("service",     "card", DealStage.Paid,      DealStage.Delivered, "counterparty")]
    [InlineData("service",     "cash", DealStage.Confirmed, DealStage.Delivered, "counterparty")]
    // classifieds والاحتِياطيّ
    [InlineData("classifieds", null,   DealStage.Offered,   DealStage.Booked,    "counterparty")]
    [InlineData("classifieds", null,   DealStage.Booked,    DealStage.Confirmed, "initiator,counterparty")]
    [InlineData("default",     null,   DealStage.Confirmed, DealStage.Reviewed,  "initiator,counterparty")]
    public void Whoever_performs_the_next_stage_moves_the_deal_and_nobody_else_does(
        string pattern, string? payMethod, DealStage from, DealStage next, string allowed)
    {
        var movers = allowed.Split(',');
        Assert.Equal(next, DealsPolicy.Next(pattern, from, payMethod));
        Assert.Equal(DealsPolicy.Actor(next), DealsPolicy.RequiredActorToLeave(pattern, from, payMethod));

        var checkedSides = 0;
        foreach (var side in new[] { "initiator", "counterparty", "stranger" })
        {
            var (deals, world, deal) = At(pattern, payMethod, from, counterparty: Seller);
            var res = Now(deals.AdvanceAsync(Slug, deal.Id, Id(side), side));
            checkedSides++;
            if (movers.Contains(side))
            {
                Assert.True(res.Ok, $"{pattern}/{payMethod}: «{side}» يُحَرِّكُ {from} → {next} — {res.Reason}");
                Assert.Equal(next, world.Read<Deal>(deal.Id.ToString())!.Stage);
                Assert.Equal(Id(side), deal.Timeline.Last(e => e.Action == "advanced").ActorId);
            }
            else
            {
                Assert.False(res.Ok, $"{pattern}/{payMethod}: «{side}» حَرَّكَ {from} → {next} وهُوَ لَيسَ فاعِلَها.");
                Assert.Contains(DealsPolicy.Actor(next), res.Reason ?? "", StringComparison.Ordinal);
                Assert.Equal(from, world.Read<Deal>(deal.Id.ToString())!.Stage);
                Assert.Equal(0, world.Wrote<Deal>());
            }
        }
        Assert.Equal(3, checkedSides);
    }

    // ═══ ٢) الدالَّةُ الواحِدَة ═════════════════════════════════════════════════════

    /// <summary><b>المُوجِب</b>: لِكُلِّ نَمَطٍ وطَريقَةِ دَفعٍ ومَرحَلَةٍ لَها تالٍ — فاعِلُ الخُروجِ فاعِلُ التالي.
    /// <b>والسالِب</b>: آخِرُ مَرحَلَةٍ لا يُخرِجُها أَحَد (<c>null</c>)، وصَفُّ <c>Reviewed = either</c> لا يُسأَلُ إلّا
    /// دُخولاً. والعَدّادُ يُطبَعُ في رِسالَةِ الفَشَلِ ويَفشَلُ إن كانَ صِفراً (القاعِدَة ١٠).</summary>
    [Fact]
    public void The_actor_to_leave_is_the_next_stages_actor_and_nobody_leaves_the_last_stage()
    {
        var checkedRows = 0;
        var finals = 0;
        foreach (var pattern in new[] { "trip", "rental", "marketplace", "classifieds", "service", "pattern_from_the_future" })
        foreach (var pay in new string?[] { null, CheckoutPayMethods.Card, CheckoutPayMethods.CashOnDelivery })
        foreach (var stage in DealsPolicy.StagesFor(pattern, pay))
        {
            checkedRows++;
            var next = DealsPolicy.Next(pattern, stage, pay);
            var required = DealsPolicy.RequiredActorToLeave(pattern, stage, pay);
            if (next is null) { finals++; Assert.Null(required); continue; }
            Assert.Equal(DealsPolicy.Actor(next.Value), required);
        }
        Assert.True(checkedRows > 0 && finals == 18, $"فُحِصَ {checkedRows} صَفّاً، مِنها {finals} آخِرَ مَرحَلَة.");

        var cash = new Deal { Pattern = "marketplace", Stage = DealStage.Confirmed };
        cash.Attributes[CheckoutPayMethods.AttributeKey] = CheckoutPayMethods.CashOnDelivery;
        Assert.Equal("counterparty", DealsPolicy.RequiredActorToLeave(cash));
        Assert.NotEqual(DealsPolicy.Actor(cash.Stage), DealsPolicy.RequiredActorToLeave(cash));
    }

    /// <summary>الخِدمَةُ والقَبولُ والواجِهَةُ والشاشَةُ والـAPI تَقرَأُ الدالَّةَ نَفسَها — <b>ولا مَوضِعَ باقٍ</b>
    /// في <c>libs/</c> و<c>apps/</c> يَسأَلُ فاعِلَ مَرحَلَةِ صَفقَةٍ قائِمَة (<c>Actor(deal.Stage)</c> وأَخَواتُها)
    /// بَعدَ التَعليقات. وشَرحُ التَدَفُّقِ يَقرَأُ <c>Actor(stage)</c> لِكُلِّ مَرحَلَةٍ — مَن يُؤَدّيها — فَهُوَ القِراءَةُ
    /// الصَحيحَةُ لا الخَطَأ، ويُثَبَّتُ هُنا أَيضاً.</summary>
    [Fact]
    public void The_service_accept_turn_view_page_and_api_read_the_one_function_and_no_place_asks_the_current_stages_actor()
    {
        static string Code(params string[] parts)
        {
            var path = Path.Combine(RepoRoot, Path.Combine(parts));
            Assert.True(File.Exists(path), $"مَصدَرٌ مَفقود: {path}");
            var text = File.ReadAllText(path);
            Assert.True(text.Length > 500, $"أَداةٌ عَمياء: {path} لَم يُقرَأ.");
            return WriteEndpointGuardTests.StripComments(WriteEndpointGuardTests.StripMarkupComments(text));
        }

        var service = Code(Template, "Services", "Deals", "DealsService.cs");
        Assert.Contains("var required = DealsPolicy.RequiredActorToLeave(deal)!;", service, StringComparison.Ordinal);
        Assert.Contains("if (!IsActorAllowed(deal, by.ActorId, DealsPolicy.RequiredActorToLeave(deal)!))", service, StringComparison.Ordinal);
        Assert.Contains("DealsPolicy.RequiredActorToLeave(pattern, stage, payMethod) ?? DealsPolicy.Actor(stage)",
            Code(Template, "Services", "Ux", "DealTurnView.cs"), StringComparison.Ordinal);
        Assert.Contains("var required = DealsPolicy.RequiredActorToLeave(deal);",
            Code(Template, "Components", "Pages", "MyDealDetail.razor"), StringComparison.Ordinal);
        var api = Code(Template, "Api", "DealApi.cs");
        Assert.Contains("next?.ToString(), DealsPolicy.RequiredActorToLeave(d) ?? DealsPolicy.Actor(d.Stage),", api, StringComparison.Ordinal);
        Assert.Contains("required_actor = DealsPolicy.RequiredActorToLeave(result.Deal) ?? DealsPolicy.Actor(result.Deal.Stage),", api, StringComparison.Ordinal);
        Assert.Contains("@L[DealTurnView.ActorLabelKey(DealsPolicy.Actor(stage), Side)]",
            Code(Template, "Components", "FlowExplainer.razor"), StringComparison.Ordinal);

        // والاستِثناءُ الوَحيدُ مُسَمّىً لا مُستَنتَج: سُقوطُ العَرضِ عِندَ آخِرِ مَرحَلَةٍ —
        // `RequiredActorToLeave(…) ?? DealsPolicy.Actor(…)` — حَيثُ لا خُروجَ ولا قَرار (المِفتاحُ كَما كانَ).
        const string Fallback = "?? DealsPolicy.";
        var scanned = 0;
        var fallbacks = 0;
        var strays = new List<string>();
        foreach (var root in new[] { "libs", "apps" })
        foreach (var f in Directory.EnumerateFiles(Path.Combine(RepoRoot, root), "*.*", SearchOption.AllDirectories))
        {
            if (!f.EndsWith(".cs", StringComparison.Ordinal) && !f.EndsWith(".razor", StringComparison.Ordinal)) continue;
            if (f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
                f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)) continue;
            scanned++;
            var code = WriteEndpointGuardTests.StripComments(WriteEndpointGuardTests.StripMarkupComments(File.ReadAllText(f)));
            foreach (var needle in new[] { "Actor(deal.Stage)", "Actor(d.Stage)", "Actor(result.Deal.Stage)", "ActorFor(deal.Stage)" })
                for (var at = code.IndexOf(needle, StringComparison.Ordinal); at >= 0; at = code.IndexOf(needle, at + needle.Length, StringComparison.Ordinal))
                {
                    var head = code[..at];
                    if (head.EndsWith(Fallback, StringComparison.Ordinal) && head.TrimEnd()[..^Fallback.Length].TrimEnd().EndsWith(")", StringComparison.Ordinal)
                        && head.LastIndexOf("RequiredActorToLeave(", StringComparison.Ordinal) > head.LastIndexOf(';'))
                        fallbacks++;
                    else
                        strays.Add($"{Path.GetRelativePath(RepoRoot, f)} ← {needle}");
                }
        }
        Assert.True(scanned > 200, $"أَداةٌ عَمياء: فُحِصَ {scanned} مِلَفّاً.");
        Assert.True(strays.Count == 0 && fallbacks == 2,
            $"فُحِصَ {scanned} مِلَفّاً، وسُقوطُ العَرضِ عِندَ آخِرِ مَرحَلَةٍ {fallbacks} (المُتَوَقَّعُ ٢ في DealApi) — " +
            "مَوضِعٌ يَسأَلُ فاعِلَ المَرحَلَةِ الحالِيَّة: " + string.Join("، ", strays));
    }

    /// <summary>عَقدُ الـAPI يَقولُ ما سَيَسأَلُه المُحَرِّك: <c>actor_for_current_stage</c> = مَن تَنتَظِرُه المَرحَلَةُ
    /// لِيُخرِجَها (البائِعُ عِندَ <c>Paid</c>، لا «initiator» كَما كانَ)؛ وعِندَ آخِرِ مَرحَلَةٍ فاعِلُها كَما كانَ —
    /// مِفتاحُ الشاشَةِ نَفسُه؛ و<c>required_actor</c> في رَفضِ التَقَدُّمِ هُوَ نَفسُه.</summary>
    [Fact]
    public void The_api_reports_the_actor_the_engine_will_ask()
    {
        var paid = new Deal { Id = Guid.NewGuid(), Pattern = "marketplace", Stage = DealStage.Paid, InitiatorId = Buyer, CounterpartyId = Seller };
        Assert.Equal("counterparty", DealApi.ToDto(paid).actor_for_current_stage);
        Assert.NotEqual(DealsPolicy.Actor(paid.Stage), DealApi.ToDto(paid).actor_for_current_stage);

        var last = new Deal { Id = Guid.NewGuid(), Pattern = "classifieds", Stage = DealStage.Confirmed, InitiatorId = Buyer };
        Assert.Null(DealsPolicy.RequiredActorToLeave(last));
        Assert.Equal(DealsPolicy.Actor(last.Stage), DealApi.ToDto(last).actor_for_current_stage);
        Assert.Equal(DealTurnView.For(last, DealSide.Initiator).ActorKey, DealApi.ToDto(last).actor_for_current_stage);

        var refused = DealApi.FromAdvance(new DealAdvanceResult(false, paid, "يَتَطَلَّب فاعِلاً مِن نَوع: counterparty"));
        Assert.Contains("\"required_actor\":\"counterparty\"", ApiJson.Serialize(refused.Payload), StringComparison.Ordinal);
    }

    // ═══ ٣) القَبولُ والمَسارُ الإداريُّ — البَوّابَةُ نَفسُها ═════════════════════════════

    /// <summary><b>القَبولُ يَمُرُّ مِن البَوّابَةِ نَفسِها</b>: عَرضٌ عَلى إعلانٍ بِلا طَرَفٍ — البائِعُ يَقبَلُ فَيَصيرُ
    /// الطَرَفَ ثُمَّ يَدخُلُ <c>Booked</c> بِفاعِلِها؛ والمُشتَريُّ لا يُخرِجُها مِن <c>Offered</c> بِالمَسارِ العامّ،
    /// ولا مالِكُها قَبلَ أَن يَقبَل (لَيسَ الطَرَفَ بَعد).</summary>
    [Fact]
    public void Accepting_an_unbound_offer_makes_the_owner_the_counterparty_then_passes_the_same_gate()
    {
        var (deals, world, deal) = At("marketplace", null, DealStage.Offered, counterparty: null);
        deal.Refs[DealsService.ListingOwnerRef] = Seller.ToString();

        Assert.False(Now(deals.AdvanceAsync(Slug, deal.Id, Buyer, "مُشتَرٍ")).Ok);
        Assert.False(Now(deals.AdvanceAsync(Slug, deal.Id, Seller, "بائِع")).Ok);
        Assert.Equal(0, world.Wrote<Deal>());

        var accepted = Now(deals.AcceptAsync(Slug, deal.Id, new DealAcceptor(Seller, "بائِع")));
        Assert.True(accepted.Ok, accepted.Violation?.Code);
        var after = world.Read<Deal>(deal.Id.ToString())!;
        Assert.Equal(DealStage.Booked, after.Stage);
        Assert.Equal(Seller, after.CounterpartyId);
        Assert.Equal(Seller, after.Timeline.Last(e => e.Action == "advanced").ActorId);
    }

    /// <summary><b>المَسارُ الإداريّ</b> (<c>/studio/apps/{slug}/deals/{id}/advance</c>) يُحَرِّكُ بِـ
    /// <c>deal.CounterpartyId ?? مالِكِ التَطبيق</c> — المُحَدِّدُ مُثَبَّتٌ حَرفاً في
    /// <c>DealCounterpartyCharacterizationTests</c> — فَيَمضي بِخُطُواتِ جانِبِ المَتجَرِ (يَحجِز، يُؤَكِّد، يَشحَن،
    /// يُسَلِّم، يُقَيِّم) ويَقِفُ عِندَ خُطوَةِ المُشتَري (يَدفَع، يَستَلِم) — ولا يَستَعيرُ المُشتَري. وكانَ يَستَعيرُه عِندَ
    /// <c>Offered</c> ويَقِفُ عِندَ الخُروجِ مِن <c>Paid</c> و<c>Received</c> (مَقيسٌ قَبلَ التَبديل؛ ‏ADR-033 §٦‑ط).</summary>
    [Theory]
    [InlineData("marketplace", "card", DealStage.Offered,   true)]
    [InlineData("marketplace", "card", DealStage.Booked,    true)]
    [InlineData("marketplace", "card", DealStage.Confirmed, false)]   // إلى Paid — المُشتَري يَدفَع
    [InlineData("marketplace", "card", DealStage.Paid,      true)]
    [InlineData("marketplace", "cash", DealStage.Confirmed, true)]    // إلى Shipping
    [InlineData("rental",      "card", DealStage.Delivered, false)]   // إلى Received — المُستَأجِرُ يَستَلِم
    [InlineData("rental",      "card", DealStage.Received,  true)]
    public void The_admin_path_moves_as_the_store_side_and_stops_at_the_buyers_steps(
        string pattern, string payMethod, DealStage from, bool moves)
    {
        foreach (var counterparty in new Guid?[] { Seller, AppOwner })
        {
            var (deals, world, deal) = At(pattern, payMethod, from, counterparty);
            var actorId = deal.CounterpartyId ?? AppOwner;   // مُحَدِّدُ النُقطَةِ بِحَرفِه
            var res = Now(deals.AdvanceAsync(Slug, deal.Id, actorId, "مالِك التَّطبيق (إداريّ)"));
            Assert.Equal(moves, res.Ok);
            Assert.Equal(moves ? DealsPolicy.Next(pattern, from, payMethod) : from, world.Read<Deal>(deal.Id.ToString())!.Stage);
        }
    }
}
