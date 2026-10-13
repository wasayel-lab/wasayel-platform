using System.Reflection;
using ACommerce.Kit.Payments;
using ACommerce.Kit.Reviews;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using Marten;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ بَوّابَةُ تَقييمِ الطَرَفِ الآخَر — ‏ADR-033 §٦‑ح ═════════════════════════════════════
//
// بَعدَ تَوصيفِ `DealReviewCharacterizationTests` (`1f2036c`). كانَت `POST /{slug}/deals/{id}/review`
// تَكتُبُ ما دامَ لِقاعِدَتِها هَدَف — بِلا سُؤالٍ عَن الاكتِمالِ ولا العُضوِيَّةِ ولا النَفس — والرَبطُ
// عِندَ الوِلادَةِ (‏ADR-033 §٦‑ب‑١) أَعطاها هَدَفاً مِن لَحظَةِ الطَلَب. فَصارَ القَرارُ دالَّةً نَقِيَّةً
// (`DealReviewAuthorization.Validate`) بِمَعجَمٍ مُغلَقٍ مِن خَمسَةِ رُموز، لِكُلٍّ مُوجِبٌ وسالِب
// (القاعِدَة ٤)، يُطَبِّقُها بابٌ واحِدٌ بِسِجِلِّ كاتِبٍ في تَوقيعِه (`DealReviews.SubmitAsync`؛
// القاعِدَة ٦)، وتَقرَؤُها الشاشَةُ نَفسُها.
//
// **وما يُقاسُ أَينَ**: البَوّابَةُ نَقِيَّةٌ فَتُقاسُ بِلا قاعِدَة؛ والرَفضُ عَبرَ البابِ الحَقيقِيِّ فَوقَ
// عالَمِ الوَثائِقِ في الذاكِرَة (`EndpointDocWorld.cs`) — يُعَدُّ أَنّه لا يَكتُبُ تَقييماً ولا يَسأَلُ
// «قَيَّمَ مِن قَبل» (ذاكَ استِعلامٌ لا يُنَفِّذُه العالَم، فَإن نودِيَ أَحمَرَّ الصَفُّ بِصَوت)؛ والطَريقُ
// المَقبولُ كُلُّه — تَقييمٌ يُكتَبُ مَرَّةً ويُعَدُّ في المُلَخَّص — حَيّاً عَلى Postgres في
// `LiveDealReviewProofTests` أَدناه.

public class DealReviewAuthorizationTests
{
    private const string Slug = "zz-review-gate";
    private static readonly Guid Buyer    = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
    private static readonly Guid Seller   = Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb");
    private static readonly Guid Stranger = Guid.Parse("dddddddd-dddd-4ddd-8ddd-dddddddddddd");

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

    private static Deal Make(DealStatus status, DealStage stage, Guid initiator, Guid? counterparty,
        string? counterpartyName = "بائِع") => new()
    {
        Id = Guid.NewGuid(), TenantSlug = Slug, Pattern = "marketplace", AmountSar = 100,
        InitiatorId = initiator, InitiatorName = "مُشتَرٍ",
        CounterpartyId = counterparty, CounterpartyName = counterparty is null ? null : counterpartyName,
        Status = status, Stage = stage,
    };

    /// <summary>صَفقَةٌ مُكتَمِلَةٌ كَما يَترُكُها <c>MoveAsync</c> عِندَ <c>Reviewed</c>: المُشتَري مُبادِرٌ والبائِعُ طَرَفُها.</summary>
    private static Deal Done() => Make(DealStatus.Completed, DealStage.Reviewed, Buyer, Seller);

    private static DealReviewer As(Guid id) => new(id, "كاتِب");

    private static string? CodeOf(Deal? deal, Guid reviewer) => DealReviewAuthorization.Validate(deal, As(reviewer))?.Code;

    // ═══ ١) المَعجَمُ مُغلَق ═══════════════════════════════════════════════════════

    [Fact]
    public void The_lexicon_is_closed_at_five_codes_and_Require_refuses_anything_else()
    {
        Assert.Equal(new[] { "deal_not_found", "actor_not_party", "self_review", "deal_not_completed", "counterparty_missing" },
            DealReviewAuthorization.All.ToArray());
        Assert.Equal(DealReviewAuthorization.All.Count, DealReviewAuthorization.All.Distinct(StringComparer.Ordinal).Count());
        foreach (var code in DealReviewAuthorization.All)
        {
            Assert.True(DealReviewAuthorization.Contains(code));
            Assert.Equal(code, DealReviewAuthorization.Require(code));
        }
        foreach (var outside in new[] { "", "deal_not_active", "DEAL_NOT_COMPLETED", "self_review ", "already_reviewed" })
        {
            Assert.False(DealReviewAuthorization.Contains(outside));
            Assert.Throws<ArgumentException>(() => DealReviewAuthorization.Require(outside));
        }
    }

    // ═══ ٢) لِكُلِّ رَمزٍ سالِبٌ ومُوجِب ═════════════════════════════════════════════

    [Fact]
    public void Deal_not_found_is_refused_and_an_existing_completed_deal_is_not()
    {
        Assert.Equal(DealReviewAuthorization.DealNotFound, CodeOf(null, Buyer));
        Assert.Null(CodeOf(Done(), Buyer));
    }

    /// <summary><b>السالِب</b>: مَن لَيسَ مُبادِراً ولا طَرَفاً — ومِنهُم كانَ يُقَيِّمُ مُبادِرَ أَيِّ صَفقَةٍ
    /// قَبلَ هذا. <b>والمُوجِب</b>: المُبادِرُ والطَرَفُ كِلاهُما.</summary>
    [Fact]
    public void A_stranger_is_refused_as_not_a_party_and_both_parties_pass()
    {
        Assert.Equal(DealReviewAuthorization.ActorNotParty, CodeOf(Done(), Stranger));
        Assert.Null(CodeOf(Done(), Buyer));
        Assert.Null(CodeOf(Done(), Seller));
    }

    /// <summary><b>السالِب</b>: صَفقَةٌ طَرَفاها واحِد — كانَ <c>/checkout/submit</c> يُولِدُها لِمُشتَري
    /// إعلانِه حَتّى <c>3a92617</c> — فَهَدَفُ مُبادِرِها هُوَ نَفسُه. <b>والمُوجِب</b>: طَرَفانِ مُختَلِفان.</summary>
    [Fact]
    public void A_deal_whose_two_sides_are_the_reviewer_is_refused_as_self_review_and_distinct_sides_pass()
    {
        var self = Make(DealStatus.Completed, DealStage.Reviewed, Buyer, Buyer);
        Assert.Equal(DealReviewAuthorization.SelfReview, CodeOf(self, Buyer));
        Assert.Null(CodeOf(Done(), Buyer));
    }

    /// <summary><b>السالِب</b>: كُلُّ حالَةٍ غَيرُ <c>Completed</c> — الفَعّالَةُ في كُلِّ مَرحَلَةٍ (ومِنها
    /// <c>Offered</c> المَربوطَةُ عِندَ الوِلادَة)، والمُلغاةُ، والمُتَنازَعُ فيها — لِلطَرَفَينِ كِلَيهِما.
    /// <b>والمُوجِب</b>: <c>Completed</c>.</summary>
    [Theory]
    [InlineData(DealStatus.Active, DealStage.Offered)]
    [InlineData(DealStatus.Active, DealStage.Booked)]
    [InlineData(DealStatus.Active, DealStage.Confirmed)]
    [InlineData(DealStatus.Active, DealStage.Shipping)]
    [InlineData(DealStatus.Active, DealStage.Delivered)]
    [InlineData(DealStatus.Active, DealStage.Reviewed)]
    [InlineData(DealStatus.Cancelled, DealStage.Offered)]
    [InlineData(DealStatus.Disputed, DealStage.Delivered)]
    public void A_deal_that_has_not_completed_is_refused_to_both_parties_and_a_completed_one_is_not(DealStatus status, DealStage stage)
    {
        var open = Make(status, stage, Buyer, Seller);
        Assert.Equal(DealReviewAuthorization.DealNotCompleted, CodeOf(open, Buyer));
        Assert.Equal(DealReviewAuthorization.DealNotCompleted, CodeOf(open, Seller));
        Assert.Null(CodeOf(Make(DealStatus.Completed, stage, Buyer, Seller), Buyer));
    }

    /// <summary><b>السالِب</b>: صَفقَةٌ مُكتَمِلَةٌ بِلا طَرَفٍ مُقابِل — مُبادِرُها لا هَدَفَ لَه، وكانَ الجِسمُ
    /// القَديمُ يَتَخَطّاه صامِتاً. <b>والمُوجِب</b>: مُكتَمِلَةٌ لَها طَرَف.</summary>
    [Fact]
    public void A_completed_deal_without_a_counterparty_is_refused_to_its_initiator_and_one_with_a_counterparty_is_not()
    {
        var alone = Make(DealStatus.Completed, DealStage.Reviewed, Buyer, null);
        Assert.Equal(DealReviewAuthorization.CounterpartyMissing, CodeOf(alone, Buyer));
        Assert.Null(CodeOf(Done(), Buyer));
    }

    // ═══ ٣) التَرتيب — التَخويلُ قَبلَ الحالَة ════════════════════════════════════

    /// <summary>غَيرُ الطَرَفِ يُجابُ «لَستَ طَرَفاً» أَيّاً كانَت حالَةُ الصَفقَة — فَلا يُفشى لَه أَنّها لَم
    /// تَكتَمِل أَو أُلغِيَت (القاعِدَة ٦)؛ والنَفسُ تُرَدُّ في كُلِّ مَرحَلَةٍ قَبلَ سُؤالِ الاكتِمال؛ والغِيابُ
    /// قَبلَ الكُلّ.</summary>
    [Fact]
    public void Authorization_comes_before_state_so_a_stranger_learns_nothing_about_the_deal()
    {
        foreach (var status in Enum.GetValues<DealStatus>())
            Assert.Equal(DealReviewAuthorization.ActorNotParty, CodeOf(Make(status, DealStage.Booked, Buyer, Seller), Stranger));
        Assert.Equal(DealReviewAuthorization.ActorNotParty, CodeOf(Make(DealStatus.Completed, DealStage.Reviewed, Buyer, null), Stranger));
        Assert.Equal(DealReviewAuthorization.SelfReview, CodeOf(Make(DealStatus.Active, DealStage.Offered, Buyer, Buyer), Buyer));
        Assert.Equal(DealReviewAuthorization.DealNotFound, CodeOf(null, Stranger));
    }

    // ═══ ٤) الهَدَفُ — الطَرَفُ الآخَر ═══════════════════════════════════════════════

    [Fact]
    public void The_target_is_the_other_party_and_nobody_for_a_stranger()
    {
        Assert.Equal((Seller, "بائِع"), DealReviewAuthorization.TargetOf(Done(), Buyer));
        Assert.Equal((Buyer, "مُشتَرٍ"), DealReviewAuthorization.TargetOf(Done(), Seller));
        Assert.Null(DealReviewAuthorization.TargetOf(Done(), Stranger));
        Assert.Null(DealReviewAuthorization.TargetOf(Make(DealStatus.Completed, DealStage.Reviewed, Buyer, null), Buyer));
        // الاسمُ الغائِبُ «—» كَما كانَ يَكتُبُه جِسمُ النُقطَة.
        Assert.Equal((Seller, "—"), DealReviewAuthorization.TargetOf(Make(DealStatus.Completed, DealStage.Reviewed, Buyer, Seller, counterpartyName: null), Buyer));
    }

    // ═══ ٥) البابُ الحَقيقِيُّ يَرُدُّ قَبلَ أَيِّ قِراءَةٍ لِلتَقييماتِ أَو كِتابَة ═══════════════

    private sealed class ForbiddenPaymentProvider : IPaymentProvider
    {
        private static InvalidOperationException Touched() => new("التَقييمُ لا يَمَسُّ المال — نودِيَ مُزَوِّدُ الدَفع.");
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

    /// <summary>كُلُّ رَمزٍ عَبرَ البابِ الحَقيقِيّ (<see cref="DealReviews"/> عَلى <see cref="DealsService"/> و
    /// <see cref="ReviewsService"/> الحَقيقِيَّتَين فَوقَ عالَمِ الوَثائِق): رَفضٌ بِرَمزِ البَوّابَة، وصِفرُ تَقييمٍ
    /// مَكتوب، وصِفرُ حِفظ، ولا سُؤالَ «قَيَّمَ مِن قَبل».</summary>
    [Theory]
    [InlineData("deal_not_found")]
    [InlineData("actor_not_party")]
    [InlineData("self_review")]
    [InlineData("deal_not_completed")]
    [InlineData("counterparty_missing")]
    public void The_real_door_refuses_with_the_gates_code_before_reading_or_writing_any_review(string code)
    {
        var world = new DocWorld();
        var door = new DealReviews(new DealsService(world.Store, new ForbiddenPaymentProvider()), new ReviewsService(world.Store));
        var (deal, reviewer) = code switch
        {
            "deal_not_found"       => ((Deal?)null, Buyer),
            "actor_not_party"      => (Done(), Stranger),
            "self_review"          => (Make(DealStatus.Completed, DealStage.Reviewed, Buyer, Buyer), Buyer),
            "deal_not_completed"   => (Make(DealStatus.Active, DealStage.Offered, Buyer, Seller), Buyer),
            "counterparty_missing" => (Make(DealStatus.Completed, DealStage.Reviewed, Buyer, null), Buyer),
            _ => throw new ArgumentOutOfRangeException(nameof(code)),
        };
        if (deal is not null) world.Put(deal);

        var res = Now(door.SubmitAsync(Slug, deal?.Id ?? Guid.NewGuid(), As(reviewer), 1, "تَقييمٌ مَرفوض"));

        Assert.False(res.Ok);
        Assert.Equal(DealReviewAuthorization.Require(code), res.Violation!.Code);
        Assert.Null(res.Review);
        Assert.Equal(0, world.Wrote<Review>());
        Assert.Equal(0, world.SaveCalls);
        Assert.DoesNotContain("Query", world.Touches);
    }

    // ═══ ٦) الحارِسُ في التَوقيع، والبابُ مُسَجَّل ═══════════════════════════════════

    /// <summary>البابُ يَأخُذُ <see cref="DealReviewer"/> ولا حِملَ زائِداً بِمُعَرِّفٍ عارٍ — فَلا يُنادى
    /// بِلا تَصريحٍ عَن الكاتِب (القاعِدَة ٦)؛ وهُوَ مُسَجَّلٌ في الحاوِيَةِ فَتَحقِنُه النُقطَة.</summary>
    [Fact]
    public void The_door_takes_a_reviewer_record_with_no_bare_id_overload_and_is_registered()
    {
        var overloads = typeof(DealReviews).GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.Name == nameof(DealReviews.SubmitAsync)).ToArray();
        Assert.Single(overloads);
        Assert.Contains(overloads[0].GetParameters(), p => p.ParameterType == typeof(DealReviewer));
        Assert.DoesNotContain(overloads[0].GetParameters(), p => p.ParameterType == typeof(Guid) && p.Name != "dealId");

        var extensions = WriteEndpointGuardTests.StripComments(Source(Template, "MarketplaceTemplateExtensions.cs"));
        Assert.Contains("services.AddScoped<Services.Deals.DealReviews>();", extensions, StringComparison.Ordinal);
    }
}

// ─── البُرهانُ الحَيّ — التَقييمُ يُكتَبُ ويُعَدُّ فِعلاً عَلى Postgres ─────────────────────────
//
// يَتَخَطّى نَفسَه ما لَم تُضبَط WASAYEL_LIVE_PROOF=1 و ConnectionStrings__Postgres، بِنَفسِ عَقدِ
// `LiveDealAcceptSequenceProofTests` حَرفاً — ومِنه نُسِخَ تَركيبُ المَخزَن. يُولِدُ صَفقَةَ سَلَّةٍ نَقدِيَّةً
// بِتَسَلسُلِ `/checkout/submit`، ويَقيسُ أَنّ مُشتَريها يُرَدُّ قَبلَ الاكتِمالِ بِلا كِتابَة، ثُمَّ يَمشيها
// البائِعُ إلى `Completed`، فَيُرَدُّ الغَريبُ، ويُكتَبُ تَقييمُ المُشتَري «مُوَثَّقاً» مَرَّةً واحِدَةً ويُعَدُّ في
// مُلَخَّصِ البائِع، ويُقَيِّمُ البائِعُ المُشتَري؛ وأَنّ مُشتَرِيَ إعلانِه لا يُربَطُ طَرَفاً (`3a92617`)،
// وأَنّ صَفقَةً قَديمَةً طَرَفاها واحِدٌ تُرَدُّ بِـ`self_review`. مُستَأجِرٌ عَشوائِيٌّ يُنَظَّفُ بَعدَه.

[Collection(LiveProofCollection.Name)]
public class LiveDealReviewProofTests(ITestOutputHelper output)
{
    private static bool Enabled =>
        Environment.GetEnvironmentVariable("WASAYEL_LIVE_PROOF") == "1";

    private static string? ConnectionString =>
        Environment.GetEnvironmentVariable("ConnectionStrings__Postgres");

    private static string ResilientConnection =>
        ConnectionString!.TrimEnd(';') + ";Timeout=60;Command Timeout=120";

    private static DocumentStore BuildStore() => DocumentStore.For(o =>
    {
        o.Connection(ResilientConnection);
        o.DatabaseSchemaName = "platform";
        o.Policies.AllDocumentsAreMultiTenanted();
        o.Events.TenancyStyle = global::JasperFx.MultiTenancy.TenancyStyle.Conjoined;
        o.Schema.For<ACommerce.Kit.Tenants.Tenant>().SingleTenanted().Identity(x => x.Id);
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

    private static async Task<int> ReviewCount(IDocumentStore store, string slug)
    {
        await using var q = store.QuerySession(slug);
        return await q.Query<Review>().CountAsync();
    }

    [Fact]
    public async Task A_review_is_written_once_by_a_party_of_a_completed_deal_and_never_before_nor_about_oneself()
    {
        if (!Enabled || string.IsNullOrEmpty(ConnectionString)) return;

        void Log(string line) => output.WriteLine(line);

        await using var store = BuildStore();
        var deals = new DealsService(store, new ForbiddenPaymentProvider());
        var reviews = new ReviewsService(store);
        var door = new DealReviews(deals, reviews);

        // مُستَأجِرٌ عَشوائِيٌّ يَخُصُّ هذا التَشغيلَ وَحدَه — لا `ashare` ولا مَتجَرَ لَقطَة.
        var slug = "zz-proof-" + Guid.NewGuid().ToString("N")[..8];
        var buyer = Guid.NewGuid();
        var seller = Guid.NewGuid();
        var stranger = Guid.NewGuid();
        var cash = new Dictionary<string, string> { [CheckoutPayMethods.AttributeKey] = CheckoutPayMethods.CashOnDelivery };

        try
        {
            var deal = await deals.StartAsync(slug, "marketplace", buyer, "مُشتَرٍ", null, "سِلعَة", 100m, new(cash));
            await deals.AttachRefAsync(slug, deal.Id, DealsService.ListingOwnerRef, seller.ToString());
            await deals.BindListingOwnerAsync(slug, deal.Id, seller.ToString());

            // ١) قَبلَ أَيِّ قَبول — كانَ يُكتَبُ هُنا تَقييمٌ «مُوَثَّقٌ» لِلبائِع.
            var early = await door.SubmitAsync(slug, deal.Id, new DealReviewer(buyer, "مُشتَرٍ"), 1, "قَبلَ القَبول");
            Assert.False(early.Ok);
            Assert.Equal(DealReviewAuthorization.DealNotCompleted, early.Violation!.Code);
            Assert.Equal(0, await ReviewCount(store, slug));
            Log($"قَبلَ الاكتِمال: {early.Violation.Code} · تَقييمات={await ReviewCount(store, slug)}");

            // ٢) البائِعُ يَقبَلُ ويَمشيها إلى Completed (النَقدُ لا يَزورُ Paid).
            var accepted = await deals.AcceptAsync(slug, deal.Id, new DealAcceptor(seller, "بائِع"));
            Assert.True(accepted.Ok, accepted.Violation?.MessageAr);
            for (var step = 0; step < 4; step++)
            {
                var moved = await deals.AdvanceAsync(slug, deal.Id, seller, "بائِع");
                Assert.True(moved.Ok, moved.Reason);
            }
            var done = (await deals.LoadAsync(slug, deal.Id))!;
            Assert.Equal(DealStatus.Completed, done.Status);
            Log($"المَشي: Stage={done.Stage} · Status={done.Status}");

            // ٣) غَريبٌ يُقَيِّم — يُرَدّ.
            var foreign = await door.SubmitAsync(slug, deal.Id, new DealReviewer(stranger, "غَريب"), 1, "غَريب");
            Assert.False(foreign.Ok);
            Assert.Equal(DealReviewAuthorization.ActorNotParty, foreign.Violation!.Code);

            // ٤) المُشتَري يُقَيِّمُ البائِعَ مَرَّةً واحِدَة — والثانِيَةُ لا تُكتَبُ ولا تُرَدّ.
            var first = await door.SubmitAsync(slug, deal.Id, new DealReviewer(buyer, "مُشتَرٍ"), 5, "مُمتاز");
            Assert.True(first.Ok, first.Violation?.MessageAr);
            Assert.NotNull(first.Review);
            Assert.Equal(seller, first.Review!.TargetUserId);
            Assert.Equal(buyer, first.Review.AuthorId);
            Assert.Equal(deal.Id, first.Review.DealId);
            Assert.True(first.Review.VerifiedTransaction);
            var again = await door.SubmitAsync(slug, deal.Id, new DealReviewer(buyer, "مُشتَرٍ"), 1, "ثانِيَة");
            Assert.True(again.Ok);
            Assert.Null(again.Review);
            Assert.Equal(1, await ReviewCount(store, slug));

            // ٥) البائِعُ يُقَيِّمُ المُشتَري.
            var back = await door.SubmitAsync(slug, deal.Id, new DealReviewer(seller, "بائِع"), 4, "مُشتَرٍ جَيِّد");
            Assert.True(back.Ok, back.Violation?.MessageAr);
            Assert.Equal(buyer, back.Review!.TargetUserId);
            Assert.Equal(2, await ReviewCount(store, slug));
            var summary = await reviews.SummaryAsync(slug, seller);
            Assert.Equal(1, summary.Count);
            Log($"بَعدَ الاكتِمال: غَريب={foreign.Violation.Code} · المُشتَري يُقَيِّم=Ok · ثانِيَة=Ok بِلا كِتابَة · " +
                $"البائِعُ يُقَيِّم=Ok · تَقييمات={await ReviewCount(store, slug)} · مُلَخَّصُ البائِع={summary.Count}");

            // ٦) مُشتَري إعلانِه لا يُربَطُ طَرَفاً (‏3a92617) — ولا يُقَيِّمُ صَفقَتَه.
            var own = await deals.StartAsync(slug, "marketplace", seller, "بائِعٌ يَشتَري", null, "إعلانُه", 10m, new(cash));
            await deals.AttachRefAsync(slug, own.Id, DealsService.ListingOwnerRef, seller.ToString());
            await deals.BindListingOwnerAsync(slug, own.Id, seller.ToString());
            var ownAfter = (await deals.LoadAsync(slug, own.Id))!;
            Assert.Null(ownAfter.CounterpartyId);
            var ownReview = await door.SubmitAsync(slug, own.Id, new DealReviewer(seller, "بائِعٌ يَشتَري"), 5, "نَفسي");
            Assert.Equal(DealReviewAuthorization.DealNotCompleted, ownReview.Violation!.Code);

            // ٧) صَفقَةٌ قَديمَةٌ طَرَفاها واحِدٌ مُكتَمِلَة — كانَ يُمكِنُ أَن تولَدَ قَبلَ 3a92617.
            var legacy = new Deal
            {
                Id = Guid.NewGuid(), TenantSlug = slug, Pattern = "marketplace", ListingTitle = "قَديمَة",
                InitiatorId = seller, InitiatorName = "بائِع", CounterpartyId = seller, CounterpartyName = "بائِع",
                Stage = DealStage.Reviewed, Status = DealStatus.Completed, AmountSar = 10m,
            };
            await using (var s = store.LightweightSession(slug))
            {
                s.Store(legacy);
                await s.SaveChangesAsync();
            }
            var selfReview = await door.SubmitAsync(slug, legacy.Id, new DealReviewer(seller, "بائِع"), 5, "نَفسي");
            Assert.Equal(DealReviewAuthorization.SelfReview, selfReview.Violation!.Code);
            Assert.Equal(2, await ReviewCount(store, slug));
            Assert.Equal(1, (await reviews.SummaryAsync(slug, seller)).Count);
            Log($"مُشتَري إعلانِه: counterparty=null · {ownReview.Violation.Code} · قَديمَةٌ طَرَفاها واحِد: {selfReview.Violation.Code} · " +
                $"تَقييمات={await ReviewCount(store, slug)}");
        }
        finally
        {
            await using var s = store.LightweightSession(slug);
            foreach (var d in await s.Query<Deal>().ToListAsync()) s.Delete(d);
            foreach (var r in await s.Query<Review>().ToListAsync()) s.Delete(r);
            await s.SaveChangesAsync();
        }
    }
}
