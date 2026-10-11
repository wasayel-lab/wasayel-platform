using ACommerce.Kit.Payments;
using ACommerce.Kit.Reviews;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using Xunit;

namespace ACommerce.Platform.Tests;

// ═══ تَقييمُ الطَرَفِ الآخَر — تَوصيفُ «اليَوم» قَبلَ أَن تَصيرَ لِلنُقطَةِ بَوّابَة ═══════════
//
// المُراجَعَةُ الثالِثَةُ عَلى `8769fa4` (بَندُها المانِع): `8769fa4` غَيَّرَ مَعنى
// `Deal.CounterpartyId` لِصَفقَةِ السَلَّة — كانَ مَن قَبِلَ وصارَ مالِكَ الإعلانِ مِن الوِلادَة —
// ولَم تُراجَع قُرّاؤُه. ونُقطَةُ `POST /{slug}/deals/{id}/review` أَحَدُهُم: تَختارُ الهَدَفَ
// `CounterpartyId` لِلمُبادِرِ و`InitiatorId` لِكُلِّ مَن سِواه، وتَكتُبُ ما دامَ الهَدَفُ مَوجوداً
// ولَم يُقَيِّم الكاتِبُ هذِه الصَفقَةَ مِن قَبل — **بِلا سُؤالٍ عَن الاكتِمالِ ولا عَن العُضوِيَّةِ
// ولا عَن «لَستَ أَنتَ»**. فَقَبلَ `8769fa4` كانَ هَدَفُ مُشتَري السَلَّةِ فارِغاً حَتّى يَقبَلَ
// البائِعُ فَلا يُكتَبُ شَيء؛ وبَعدَه يُكتَبُ مِن لَحظَةِ الطَلَب: مُسَجَّلٌ في المَتجَرِ يَدفَعُ
// نَقداً — بِلا مال — ويُقَيِّمُ البائِعَ فَوراً، بِلا قَبولٍ ولا اكتِمال. وكُلُّ طَلَبٍ صَفقَةٌ
// جَديدَة، و«مَرَّةً لِكُلِّ كاتِبٍ في كُلِّ صَفقَة» لا يَحُدُّ العَدَد. والتَقييمُ المَربوطُ
// بِصَفقَةٍ يُوسَمُ «مُوَثَّقاً» (`VerifiedTransaction`) ويُعَدُّ في مُلَخَّصِ البائِعِ المَرسومِ عَلى
// صَفحَةِ كُلِّ إعلان. وقَبلَ `8769fa4` أَيضاً — ولا يَتَعَلَّقُ بِه — كانَ غَيرُ الطَرَفِ يُقَيِّمُ
// مُبادِرَ أَيِّ صَفقَة.
//
// **ولِماذا نَصُّ النُقطَةِ لا تَشغيلُها** (القاعِدَة ٨): لا مُضيفَ في `tests/` يُشَغِّلُ نِقاطَ
// القالِب — نِقاطُه ‏129 تُسَجَّلُ بِدالَّةٍ واحِدَةٍ تَحقِنُ أَغلَبَ الحاوِيَة — وبِناءُ أُنبوبِ
// تَشغيلٍ لَها هُنا أُنبوبٌ رابِع. فَيُثَبَّتُ القَرارُ مِن نَصِّ الجِسمِ بِلا تَعليقات (نَسَقُ
// `DealCashPolicyTests.Endpoint`)، والحالَةُ الَّتي يَقرَؤُها مِن `DealsService` الحَقيقِيَّةِ فَوقَ
// عالَمِ الوَثائِقِ في الذاكِرَة (`EndpointDocWorld.cs`) بِتَسَلسُلِ `/checkout/submit` نَفسِه.
//
// **وما يُقلَبُ وما لا يُقلَب** (القاعِدَة ٣): صُفوفُ «اليَوم» الثَلاثَةُ تُقلَبُ بِاسمِها في كوميتِ
// البَوّابَة — فَالمُثَبَّتُ فيها هُوَ ما يُغَيِّرُه؛ وصَفّا القِسمِ الثاني بِحَرفِهِما: ما يَكتُبُه
// كيتُ التَقييماتِ وما يَعُدُّه، وما تُرسِلُه الاستِمارَةُ وتَقرَؤُه النُقطَة — لا يَمَسُّها شَيء.
// والمَسارُ الإداريُّ (`/studio/apps/{slug}/deals/{id}/review`، تَقييمٌ «نِيابَةً عَن» أَحَدِ
// الطَرَفَينِ خَلفَ `StudioOwnsAsync`) خارِجَ هذا المِلَفّ.

public class DealReviewCharacterizationTests
{
    private const string Slug = "zz-review";
    private static readonly Guid Buyer  = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
    private static readonly Guid Seller = Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb");

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

    private static string Segment(string source, string from, string to)
    {
        var start = source.IndexOf(from, StringComparison.Ordinal);
        Assert.True(start > 0, $"‏`{from}` غَيرُ مَوجود — الأَداةُ عَمياء.");
        var end = source.IndexOf(to, start + from.Length, StringComparison.Ordinal);
        Assert.True(end > start, $"‏`{to}` لا يَتلو `{from}` — الأَداةُ عَمياء.");
        return WriteEndpointGuardTests.StripComments(source.Substring(start, end - start));
    }

    private static int Count(string text, string needle)
    {
        var n = 0;
        for (var i = text.IndexOf(needle, StringComparison.Ordinal); i >= 0;
             i = text.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
            n++;
        return n;
    }

    private static string ReviewEndpoint()
        => Segment(Source(Template, "MarketplaceTemplateExtensions.cs"), "\"/{slug}/deals/{id:guid}/review\"", ".RequireStoreWritable();");

    private static string DealPage()
        => WriteEndpointGuardTests.StripMarkupComments(Source(Template, "Components", "Pages", "MyDealDetail.razor"));

    private static string ReviewsKit()
        => WriteEndpointGuardTests.StripComments(Source("libs", "kits", "Reviews", "ACommerce.Kit.Reviews.Core", "ReviewsService.cs"));

    /// <summary>مُزَوِّدٌ يَرمي عِندَ أَيِّ نِداء — صَفقَةُ النَقدِ لا تَمَسُّ المالَ في الوِلادَة.</summary>
    private sealed class ForbiddenPaymentProvider : IPaymentProvider
    {
        private static InvalidOperationException Touched() => new("صَفقَةُ النَقدِ لا تَمَسُّ المال — نودِيَ مُزَوِّدُ الدَفع.");
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

    /// <summary>صَفقَةُ سَلَّةٍ نَقدِيَّةٌ كَما يُولِدُها <c>/checkout/submit</c> — <c>StartAsync</c> ثُمَّ
    /// مَرجِعُ المالِكِ ثُمَّ رَبطُه طَرَفاً — أَو بِلا رَبطٍ (<paramref name="bound"/> = <c>false</c>)
    /// كَما كانَت كُلُّ صَفقَةِ سَلَّةٍ قَبلَ <c>8769fa4</c>.</summary>
    private static Deal CashCartDeal(bool bound = true)
    {
        var world = new DocWorld();
        var deals = new DealsService(world.Store, new ForbiddenPaymentProvider());
        var deal = Now(deals.StartAsync(Slug, "marketplace", Buyer, "مُشتَرٍ", null, "سِلعَة", 100m,
            new() { [CheckoutPayMethods.AttributeKey] = CheckoutPayMethods.CashOnDelivery }));
        Now(deals.AttachRefAsync(Slug, deal.Id, DealsService.ListingOwnerRef, Seller.ToString()));
        if (bound) Now(deals.BindListingOwnerAsync(Slug, deal.Id, Seller.ToString()));
        return world.Read<Deal>(deal.Id.ToString())!;
    }

    // ═══ ١) «اليَوم» — تُقلَبُ بِاسمِها في كوميتِ البَوّابَة ═══════════════════════════════

    /// <summary><b>اليَوم</b>: قاعِدَةٌ واحِدَةٌ تَختارُ الهَدَف — المُبادِرُ يُقَيِّمُ الطَرَفَ المُقابِل،
    /// وكُلُّ مَن سِواه يُقَيِّمُ المُبادِر — وشَرطا كِتابَةٍ لا ثالِثَ لَهُما: هَدَفٌ مَوجودٌ، ولَم يُقَيِّم
    /// الكاتِبُ هذِه الصَفقَة. ولا حَرفَ في الجِسمِ عَن الحالَة، ولا عَن عُضوِيَّةِ الكاتِب، ولا عَن أَن
    /// يَكونَ الهَدَفُ هُوَ الكاتِب.</summary>
    [Fact]
    public void Today_the_review_endpoint_picks_a_target_by_one_rule_and_writes_whenever_it_exists_asking_neither_completion_nor_party_nor_self()
    {
        var review = ReviewEndpoint();

        Assert.Contains("var deal = await deals.LoadAsync(slug, id);", review, StringComparison.Ordinal);
        Assert.Contains("var (target, targetName) = deal.InitiatorId == userId", review, StringComparison.Ordinal);
        Assert.Contains("? (deal.CounterpartyId ?? Guid.Empty, deal.CounterpartyName ?? \"—\")", review, StringComparison.Ordinal);
        Assert.Contains(": (deal.InitiatorId, deal.InitiatorName);", review, StringComparison.Ordinal);
        Assert.Contains("if (target != Guid.Empty && !await reviews.HasReviewedAsync(slug, userId, id))", review, StringComparison.Ordinal);
        Assert.Equal(1, Count(review, "await reviews.SubmitAsync(slug, target, targetName, userId, userName,"));

        foreach (var absent in new[] { "Status", "Completed", "Stage", "CounterpartyId == userId", "CounterpartyId != userId",
                                       "target == userId", "target != userId", "Validate(", "IsAllowed(" })
            Assert.DoesNotContain(absent, review, StringComparison.Ordinal);
    }

    /// <summary><b>اليَوم</b>: صَفقَةُ السَلَّةِ كَما يُولِدُها <c>/checkout/submit</c> بَعدَ <c>8769fa4</c>
    /// تَحمِلُ البائِعَ في <c>CounterpartyId</c> وهي في <c>Offered</c> فَعّالَةٌ لَم يُقبَل مِنها شَيء —
    /// فَشَرطُ الكِتابَةِ أَعلاه («هَدَفٌ مَوجود») يَصدُقُ لِمُشتَريها مِن لَحظَةِ الطَلَب، والهَدَفُ
    /// البائِع. وصَفقَةُ السَلَّةِ قَبلَ الرَبطِ كانَت بِلا طَرَف — فَالهَدَفُ فارِغٌ ولا كِتابَة.</summary>
    [Fact]
    public void Today_a_cart_deal_bound_at_birth_hands_its_buyer_the_seller_as_target_at_Offered_before_any_acceptance()
    {
        var deal = CashCartDeal();
        Assert.Equal(DealStage.Offered, deal.Stage);
        Assert.Equal(DealStatus.Active, deal.Status);
        Assert.Equal(Buyer, deal.InitiatorId);
        Assert.Equal(Seller, deal.CounterpartyId);
        Assert.DoesNotContain(deal.Timeline, e => e.Action is "assigned" or "advanced");

        Assert.Null(CashCartDeal(bound: false).CounterpartyId);

        var review = ReviewEndpoint();
        Assert.Contains("? (deal.CounterpartyId ?? Guid.Empty,", review, StringComparison.Ordinal);
        Assert.Contains("if (target != Guid.Empty &&", review, StringComparison.Ordinal);
    }

    /// <summary><b>اليَوم</b>: الشاشَةُ تَرسُمُ الاستِمارَةَ بِشَرطِها هِيَ — الصَفقَةُ <c>Completed</c> ولَها
    /// طَرَفٌ ولَم يُقَيِّم الناظِرُ بَعد، وغَيرُ الطَرَفِ تُغلَقُ عَلَيه الصَفحَة — والنُقطَةُ لا تَقرَأُ
    /// شَيئاً مِن ذلك: الشَرطُ في الشاشَةِ وَحدَها، و<c>curl</c> لا يَرى الشاشَة.</summary>
    [Fact]
    public void Today_the_page_draws_the_review_form_from_its_own_condition_and_the_endpoint_reads_none_of_it()
    {
        var page = DealPage();
        var completed = page.IndexOf("else if (deal.Status == DealStatus.Completed)", StringComparison.Ordinal);
        var form      = page.IndexOf("@if (!alreadyReviewed && deal.CounterpartyId is not null)", StringComparison.Ordinal);
        var post      = page.IndexOf("/deals/{deal.Id}/review\")", StringComparison.Ordinal);
        Assert.True(completed > 0 && form > completed && post > form,
            $"الاستِمارَةُ لا تَقَعُ تَحتَ شَرطِ الشاشَة: completed={completed} form={form} post={post}");
        Assert.Contains("if (!isInitiator && !isListingOwner) { deal = null; return; }", page, StringComparison.Ordinal);

        var review = ReviewEndpoint();
        Assert.DoesNotContain("alreadyReviewed", review, StringComparison.Ordinal);
        Assert.DoesNotContain("DealStatus", review, StringComparison.Ordinal);
    }

    // ═══ ٢) ما لا تَمَسُّه البَوّابَة — بِحَرفِه ════════════════════════════════════════

    /// <summary><b>ما يَكتُبُه الكيتُ وما يَعُدُّه — ولِماذا تَهُمُّ البَوّابَة</b>: التَقييمُ المَربوطُ
    /// بِصَفقَةٍ يُوسَمُ «مُوَثَّقاً» بِمُجَرَّدِ وُجودِ مُعَرِّفِها، والتَكرارُ يُمنَعُ لِكُلِّ (كاتِب، صَفقَة)
    /// لا لِكُلِّ كاتِب، والمُلَخَّصُ يَعُدُّ كُلَّ ما لَيسَ مَخفِيّاً مِن تَقييماتِ الهَدَف — فَلا شَيءَ في
    /// الكيتِ يَسأَلُ عَن حالَةِ الصَفقَة. والدَرَجَةُ تُحصَرُ بَينَ ١ و٥.</summary>
    [Fact]
    public void A_deal_review_is_marked_verified_once_per_author_per_deal_and_every_visible_one_counts_in_the_target_summary()
    {
        var kit = ReviewsKit();
        Assert.Contains("VerifiedTransaction = dealId is not null,", kit, StringComparison.Ordinal);
        Assert.Contains(".AnyAsync(r => r.AuthorId == authorId && r.DealId == dealId, ct);", kit, StringComparison.Ordinal);
        Assert.Contains(".Where(r => r.TargetUserId == userId).ToListAsync(ct);", kit, StringComparison.Ordinal);
        Assert.Contains("if (rating < 1) rating = 1;", kit, StringComparison.Ordinal);
        Assert.Contains("if (rating > 5) rating = 5;", kit, StringComparison.Ordinal);
        Assert.DoesNotContain("Deal", kit.Replace("DealId", "").Replace("DealPattern", "").Replace("dealId", "").Replace("dealPattern", ""),
            StringComparison.Ordinal);

        var target = Guid.NewGuid();
        var summary = ReviewSummary.From(target, new[]
        {
            new Review { TargetUserId = target, Rating = 5, DealId = Guid.NewGuid(), VerifiedTransaction = true },
            new Review { TargetUserId = target, Rating = 1, DealId = Guid.NewGuid(), VerifiedTransaction = true },
            new Review { TargetUserId = target, Rating = 1, Hidden = true },
        });
        Assert.Equal(2, summary.Count);
        Assert.Equal(3.0, summary.AverageRating);
    }

    /// <summary><b>الاستِمارَةُ والنُقطَةُ تَتَّفِقانِ عَلى الحُقول</b>: تُرسِلُ <c>rating</c> و<c>body</c> إلى
    /// نُقطَةِ العُضو، والنُقطَةُ تَطلُبُ جَلسَةً قَبلَ كُلِّ شَيءٍ وتَقرَأُ الدَرَجَةَ بِافتِراضِ ٥ والنَصَّ
    /// مُشَذَّباً، وتَحمِلُ حارِسَ المَتجَرِ القابِلِ لِلكِتابَة.</summary>
    [Fact]
    public void The_review_form_posts_rating_and_body_and_the_endpoint_requires_a_session_and_reads_them_as_posted()
    {
        var page = DealPage();
        Assert.Contains("<form method=\"post\" action=\"@($\"/{Slug}/deals/{deal.Id}/review\")\">", page, StringComparison.Ordinal);
        Assert.Contains("<select name=\"rating\" class=\"ac-input\">", page, StringComparison.Ordinal);
        Assert.Contains("<textarea name=\"body\" class=\"ac-input\" rows=\"2\"", page, StringComparison.Ordinal);

        var review = ReviewEndpoint();
        var session = review.IndexOf("var parsed = AuthHandlers.ParseToken(AuthSession.ResolveToken(req, slug));", StringComparison.Ordinal);
        var login   = review.IndexOf("if (parsed is null) return Results.Redirect(Link(req, slug, \"login\"));", StringComparison.Ordinal);
        var rating  = review.IndexOf("if (!int.TryParse(req.Form[\"rating\"].ToString(), out var rating)) rating = 5;", StringComparison.Ordinal);
        var body    = review.IndexOf("var body = req.Form[\"body\"].ToString().Trim();", StringComparison.Ordinal);
        Assert.True(session > 0 && login > session && rating > login && body > rating,
            $"تَرويسَةُ النُقطَةِ تَغَيَّرَت: session={session} login={login} rating={rating} body={body}");
        Assert.Contains("}).DisableAntiforgery()", review, StringComparison.Ordinal);
    }
}
