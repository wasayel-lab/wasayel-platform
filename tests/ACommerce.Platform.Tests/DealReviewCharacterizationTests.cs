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
//
// **ثُمَّ قُلِبَت صُفوفُ «اليَوم» الثَلاثَةُ في الكوميتِ التالي** (‏ADR-033 §٦‑ح، ‏2026-10-11): النُقطَةُ
// تُنادي `DealReviews.SubmitAsync` بِسِجِلِّ كاتِبٍ ولا تَحمِلُ قَراراً، والبَوّابَةُ النَقِيَّةُ
// `DealReviewAuthorization.Validate` تَرُدُّ غَيرَ الطَرَفِ والنَفسَ وغَيرَ المُكتَمِلَةِ قَبلَ أَيِّ قِراءَةٍ
// لِلتَقييماتِ أَو كِتابَة، والشاشَةُ تَرسُمُ الاستِمارَةَ مِن البَوّابَةِ نَفسِها — كُلٌّ بِاسمٍ جَديدٍ يَقولُ
// النَقيضَ ويَحمِلُ «قُلِبَ» في شَرحِه. وقِسمُ ما لا يُقلَبُ بِحَرفِه: بَصمَتُه قَبلَ البَوّابَةِ وبَعدَها واحِدَة.

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

    /// <summary>صَفقَةُ السَلَّةِ نَفسُها وعالَمُها — لِيُبنى عَلَيه بابُ التَقييمِ الحَقيقِيّ
    /// (<see cref="DealReviews"/> فَوقَ <see cref="ReviewsService"/> عَلى المَخزَنِ نَفسِه).</summary>
    private static (DocWorld World, Guid Id) CashCartWorld()
    {
        var world = new DocWorld();
        var deals = new DealsService(world.Store, new ForbiddenPaymentProvider());
        var deal = Now(deals.StartAsync(Slug, "marketplace", Buyer, "مُشتَرٍ", null, "سِلعَة", 100m,
            new() { [CheckoutPayMethods.AttributeKey] = CheckoutPayMethods.CashOnDelivery }));
        Now(deals.AttachRefAsync(Slug, deal.Id, DealsService.ListingOwnerRef, Seller.ToString()));
        Now(deals.BindListingOwnerAsync(Slug, deal.Id, Seller.ToString()));
        return (world, deal.Id);
    }

    private static string DealReviewsService()
        => WriteEndpointGuardTests.StripComments(Source(Template, "Services", "Deals", "DealReviews.cs"));

    // ═══ ١) ما كانَ «اليَوم» — قُلِبَت بِاسمِها (‏ADR-033 §٦‑ح) ═════════════════════════════

    /// <summary><b>قُلِبَ</b> (المُراجَعَةُ الثالِثَةُ عَلى <c>8769fa4</c>، ‏2026-10-11؛ ‏ADR-033 §٦‑ح): كانَ
    /// الجِسمُ يَختارُ الهَدَفَ بِقاعِدَةٍ واحِدَةٍ ويَكتُبُ بِشَرطَينِ لا ثالِثَ لَهُما — هَدَفٌ مَوجودٌ ولَم
    /// يُقَيِّم الكاتِب — بِلا حَرفٍ عَن الحالَةِ ولا العُضوِيَّةِ ولا النَفس. وصارَ نِداءً واحِداً لِـ
    /// <c>DealReviews.SubmitAsync</c> بِسِجِلِّ كاتِبٍ في التَوقيع، والخِدمَةُ تُطَبِّقُ
    /// <c>DealReviewAuthorization.Validate</c> قَبلَ «قَيَّمَ مِن قَبل» وقَبلَ الكِتابَة — ولا قاعِدَةَ
    /// هَدَفٍ ولا شَرطَ كِتابَةٍ في الجِسم.</summary>
    [Fact]
    public void The_review_endpoint_applies_the_gate_through_DealReviews_and_carries_no_decision_of_its_own()
    {
        var review = ReviewEndpoint();
        Assert.Contains("Services.Deals.DealReviews reviews) =>", review, StringComparison.Ordinal);
        Assert.Contains("var res = await reviews.SubmitAsync(slug, id, new Services.Deals.DealReviewer(userId, userName), rating, body);",
            review, StringComparison.Ordinal);
        Assert.Contains("return Results.Redirect(Link(req, slug, res.Deal is null ? \"deals\" : $\"deals/{id}\"));",
            review, StringComparison.Ordinal);
        Assert.Equal(1, Count(review, "SubmitAsync("));
        foreach (var gone in new[] { "deals.LoadAsync(", "deal.InitiatorId == userId", "CounterpartyId", "Guid.Empty",
                                     "HasReviewedAsync(", "ReviewsService" })
            Assert.DoesNotContain(gone, review, StringComparison.Ordinal);

        var service  = DealReviewsService();
        var load     = service.IndexOf("var deal = await _deals.LoadAsync(tenantSlug, dealId, ct);", StringComparison.Ordinal);
        var validate = service.IndexOf("var violation = DealReviewAuthorization.Validate(deal, by);", StringComparison.Ordinal);
        var refuse   = service.IndexOf("if (violation is not null) return DealReviewResult.Refused(violation, deal);", StringComparison.Ordinal);
        var once     = service.IndexOf("if (await _reviews.HasReviewedAsync(tenantSlug, by.ActorId, dealId, ct))", StringComparison.Ordinal);
        var write    = service.IndexOf("var review = await _reviews.SubmitAsync(tenantSlug, targetId, targetName, by.ActorId, by.ActorName,", StringComparison.Ordinal);
        Assert.True(load > 0 && validate > load && refuse > validate && once > refuse && write > once,
            $"سَلسَلَةُ التَقييمِ تَغَيَّرَت: load={load} validate={validate} refuse={refuse} once={once} write={write}");
        Assert.Equal(1, Count(service, "DealReviewAuthorization.Validate("));
        Assert.Equal(1, Count(service, "_reviews.SubmitAsync("));
    }

    /// <summary><b>قُلِبَ</b> (المُراجَعَةُ الثالِثَةُ عَلى <c>8769fa4</c>، ‏2026-10-11؛ ‏ADR-033 §٦‑ح): كانَت
    /// صَفقَةُ السَلَّةِ المَربوطَةُ عِندَ الوِلادَةِ تُعطي مُشتَريها البائِعَ هَدَفاً في <c>Offered</c> قَبلَ
    /// أَيِّ قَبول، وشَرطُ الكِتابَةِ «هَدَفٌ مَوجود» يَصدُق. وصارَت البَوّابَةُ تَرُدُّه — وتَرُدُّ البائِعَ —
    /// بِـ<c>deal_not_completed</c>، والبابُ الحَقيقِيُّ فَوقَ عالَمِ الوَثائِقِ لا يَكتُبُ تَقييماً ولا يَسأَلُ
    /// «قَيَّمَ مِن قَبل» (يُعَدّ). وصَفقَةُ السَلَّةِ بِلا رَبطٍ كَذلك.</summary>
    [Fact]
    public void A_cart_deal_bound_at_birth_is_refused_a_review_until_it_completes()
    {
        var deal = CashCartDeal();
        Assert.Equal(DealStage.Offered, deal.Stage);
        Assert.Equal(DealStatus.Active, deal.Status);
        Assert.Equal(Seller, deal.CounterpartyId);
        Assert.Equal(DealReviewAuthorization.DealNotCompleted,
            DealReviewAuthorization.Validate(deal, new DealReviewer(Buyer, "مُشتَرٍ"))!.Code);
        Assert.Equal(DealReviewAuthorization.DealNotCompleted,
            DealReviewAuthorization.Validate(deal, new DealReviewer(Seller, "بائِع"))!.Code);
        Assert.Equal(DealReviewAuthorization.DealNotCompleted,
            DealReviewAuthorization.Validate(CashCartDeal(bound: false), new DealReviewer(Buyer, "مُشتَرٍ"))!.Code);

        var (world, id) = CashCartWorld();
        var reviews = new DealReviews(new DealsService(world.Store, new ForbiddenPaymentProvider()), new ReviewsService(world.Store));
        var res = Now(reviews.SubmitAsync(Slug, id, new DealReviewer(Buyer, "مُشتَرٍ"), 1, "تَقييمٌ قَبلَ أَيِّ قَبول"));
        Assert.False(res.Ok);
        Assert.Equal(DealReviewAuthorization.DealNotCompleted, res.Violation!.Code);
        Assert.Null(res.Review);
        Assert.Equal(0, world.Wrote<Review>());
        Assert.DoesNotContain("Query", world.Touches);
    }

    /// <summary><b>قُلِبَ</b> (المُراجَعَةُ الثالِثَةُ عَلى <c>8769fa4</c>، ‏2026-10-11؛ ‏ADR-033 §٦‑ح): كانَت
    /// الشاشَةُ تَرسُمُ الاستِمارَةَ بِشَرطِها هِيَ — <c>Completed</c> ولَها طَرَف — والنُقطَةُ لا تَقرَأُ مِنه
    /// شَيئاً. وصارَت الشاشَةُ تَقرَأُ <c>DealReviewAuthorization.IsAllowed</c> — البَوّابَةَ الَّتي يُطَبِّقُها
    /// البابُ خَلفَ النُقطَة — فَما يُرسَمُ هُوَ ما يُقبَل، والصَفقَةُ الَّتي طَرَفاها واحِدٌ لا تُرسَمُ لَها
    /// استِمارَة.</summary>
    [Fact]
    public void The_page_draws_the_review_form_from_the_same_gate_the_endpoint_applies()
    {
        var page = DealPage();
        var completed = page.IndexOf("else if (deal.Status == DealStatus.Completed)", StringComparison.Ordinal);
        var form      = page.IndexOf("@if (!alreadyReviewed && canReview)", StringComparison.Ordinal);
        var post      = page.IndexOf("/deals/{deal.Id}/review\")", StringComparison.Ordinal);
        Assert.True(completed > 0 && form > completed && post > form,
            $"الاستِمارَةُ لا تَقَعُ تَحتَ البَوّابَة: completed={completed} form={form} post={post}");
        Assert.Contains("canReview = DealReviewAuthorization.IsAllowed(deal, new DealReviewer(uid, Auth.UserName ?? \"\"));",
            page, StringComparison.Ordinal);
        Assert.DoesNotContain("@if (!alreadyReviewed && deal.CounterpartyId is not null)", page, StringComparison.Ordinal);
        Assert.Contains("if (!isInitiator && !isListingOwner) { deal = null; return; }", page, StringComparison.Ordinal);
        Assert.Contains("DealReviewAuthorization.Validate(deal, by)", DealReviewsService(), StringComparison.Ordinal);
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

    // ═══ ٣) «اليَوم» — البائِعُ وَحدَه يُكمِلُ صَفقَةَ النَقدِ ويُقَيِّمُ مُشتَريها «مُوَثَّقاً» ═══════════════════
    //
    // مُراجَعَةُ الموجَةِ الثانِيَةِ الرابِعَة (‏2026-10-12): النَقدُ يَتَخَطّى `Paid` (‏ADR-033 §٦‑ب‑٣)، ونَمَطُ
    // `marketplace` بِلا `Received`، وفاعِلُ كُلِّ مَرحَلَةٍ مَدخولَةٍ بَعدَها الطَرَفُ أَو أَيٌّ مِنهُما (§٦‑ط) — فَلا خُطوَةَ
    // لِلمُشتَري في الطَريقِ كُلِّه. **يُثَبَّتُ ولا يُقَرَّر**: سُؤالٌ لِلمالِكِ «ب» (هَل يَتَطَلَّبُ إكمالُ صَفقَةِ النَقدِ، أَو
    // تَقييمُ البائِعِ لِلمُشتَري «مُوَثَّقاً»، خُطوَةً مِن المُشتَري؟) وسَطرٌ مُؤَرَّخٌ في ‏ADR-033 §٦‑ج.

    /// <summary><b>اليَوم</b>: البائِعُ المَربوطُ عِندَ الوِلادَةِ يَقبَلُ صَفقَةَ النَقدِ ثُمَّ يَمشيها وَحدَه — حَجز ← تَأكيد
    /// ← شَحن ← تَسليم ← تَقييم — إلى <c>Completed</c> بِلا نَقرَةٍ واحِدَةٍ مِن المُشتَري، ثُمَّ تُجيزُ بَوّابَةُ التَقييمِ
    /// (‏ADR-033 §٦‑ح) تَقييمَه لِلمُشتَري، والبابُ يُمَرِّرُ مُعَرِّفَ الصَفقَةِ فَيَسِمُه الكيتُ «مُوَثَّقاً»
    /// (<c>VerifiedTransaction</c>) — لِطَلَبٍ رُبَّما لَم يُسَلَّم. يُقلَبُ بِاسمِه يَومَ يُجيبُ المالِك.</summary>
    [Fact]
    public void Today_the_seller_alone_walks_a_cash_deal_to_Completed_and_his_review_of_the_buyer_is_verified()
    {
        var (world, id) = CashCartWorld();
        var deals = new DealsService(world.Store, new ForbiddenPaymentProvider());

        var accepted = Now(deals.AcceptAsync(Slug, id, new DealAcceptor(Seller, "بائِع")));
        Assert.True(accepted.Ok, accepted.Violation?.MessageAr);
        var stages = new List<DealStage> { accepted.Deal!.Stage };
        var clicks = 1;
        while (world.Read<Deal>(id.ToString())!.Status == DealStatus.Active && clicks < 10)
        {
            var r = Now(deals.AdvanceAsync(Slug, id, Seller, "بائِع"));
            Assert.True(r.Ok, $"النَقرَةُ {clicks + 1} رُدَّت: {r.Reason}");
            stages.Add(r.Deal!.Stage);
            clicks++;
        }

        var deal = world.Read<Deal>(id.ToString())!;
        Assert.Equal(DealStatus.Completed, deal.Status);
        Assert.Equal(new[] { DealStage.Booked, DealStage.Confirmed, DealStage.Shipping, DealStage.Delivered, DealStage.Reviewed },
            stages.ToArray());
        Assert.All(deal.Timeline.Where(e => e.Action == "advanced"), e => Assert.Equal(Seller, e.ActorId));
        Assert.Contains(deal.Timeline, e => e.Action == DealEvent.CashOnDelivery && e.ActorId == Seller);

        Assert.Null(DealReviewAuthorization.Validate(deal, new DealReviewer(Seller, "بائِع")));
        Assert.Equal((Buyer, "مُشتَرٍ"), DealReviewAuthorization.TargetOf(deal, Seller));
        Assert.Contains("dealId: dealId, dealPattern: deal!.Pattern", DealReviewsService(), StringComparison.Ordinal);
        var review = Now(new ReviewsService(world.Store).SubmitAsync(Slug, Buyer, "مُشتَرٍ", Seller, "بائِع", 1, "لَم يَستَلِم",
            dealId: id, dealPattern: deal.Pattern));
        Assert.True(review.VerifiedTransaction);
    }
}
