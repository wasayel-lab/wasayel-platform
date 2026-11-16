using ACommerce.Kit.Payments;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ زِرُّ «→ التالي» في تَفصيلِ صَفقَةِ الاستوديو — يُرسَمُ حَيثُ يَرُدُّه المُحَرِّك (القاعِدَة ١٢) ═══
//
// مُراجَعَةُ تَيّارِ S6a الأولى (‏2026-10-12): صَفحَةُ الاستوديو (`StudioAppDealDetail.razor`) تَرسُمُ استِمارَةَ
// `POST /studio/apps/{slug}/deals/{id}/advance` لِكُلِّ صَفقَةٍ فَعّالَةٍ لَها تالٍ — `@if (DealsPolicy.Next(deal) is { } next)` —
// بِلا سُؤالٍ عَن الفاعِل؛ والنُقطَةُ تُحَرِّكُ بِجانِبِ المَتجَرِ وَحدَه (‏ADR-033 §٦‑ط) وتَعودُ إلى الصَفحَةِ بِلا رِسالَة. فَمُنذُ
// `9c97285` يُرَدُّ الزِرُّ عِندَ خُطوَتَي المُشتَري (دُخولِ `Paid` و`Received`)، ومُنذُ `11a8d4d` عِندَ صَفقَةِ البَذرَةِ التَجريبِيَّةِ
// كُلِّها (مُبادِرُها مالِكُ التَطبيقِ فَلا يُعَيَّنُ طَرَفاً). بابٌ يَقولُ «لا» بِلا كَلِمَة — والخِدمَةُ والشاشَةُ تَفتَرِقان، وهو عَينُ
// ما أُغلِقَ لِصَفحَةِ العُضوِ بِدالَّةٍ واحِدَة (`MyDealDetail.canAdvance` ← `RequiredActorToLeave`).
//
// **والقِياسُ بِالمُحَرِّكِ الحَقيقِيّ** (`DealsService` في عالَمِ الوَثائِق، `EndpointDocWorld.cs`؛ القاعِدَة ٨): لِكُلِّ نَمَطٍ
// وطَريقَةِ دَفعٍ ومَرحَلَةٍ وتَشكيلَةِ أَطرافٍ وحالَة — أَتَرسُمُ الصَفحَةُ الزِرّ؟ وأَيُحَرِّكُ تَسَلسُلُ النُقطَةِ الصَفقَة؟ وأَيَكتُبُ
// شَيئاً حينَ يَرُدّ؟ والعَدّاداتُ تُطبَعُ دائِماً وتَفشَلُ عِندَ الصِفر (القاعِدَة ١٠).
//
// **التَوصيفُ قَبلَ التَبديل** (القاعِدَة ٣): صَفُّ «اليَوم» أَدناه يُودَعُ وَحدَه، ويُقلَبُ بِاسمِه في الكوميتِ الَّذي يَجعَلُ
// الصَفحَةَ والنُقطَةَ تَقرَآنِ دالَّةً واحِدَة.

public class DealStudioAdvanceDoorTests(ITestOutputHelper output)
{
    private const string Slug = "zz-studio-advance-door";
    private static readonly Guid Buyer    = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
    private static readonly Guid Seller   = Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb");
    private static readonly Guid AppOwner = Guid.Parse("cccccccc-cccc-4ccc-8ccc-cccccccccccc");

    private static string RepoRoot => ThemeZeroEquivalenceTests.RepoRoot;
    private const string Template = "libs/templates/ACommerce.Templates.Customer.Marketplace";

    /// <summary>مُزَوِّدٌ يَرمي عِندَ أَيِّ نِداء — لا مُعَرِّفَ دَفعٍ عَلى صَفَقاتِ هذا المِلَفّ، فَإن مَسَّ المَشيُ المالَ
    /// أَحمَرَّ الصَفُّ بَدَلَ أَن يُحاكى.</summary>
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

    private static string Source(params string[] parts)
    {
        var path = Path.Combine(RepoRoot, Path.Combine(parts));
        Assert.True(File.Exists(path), $"مَصدَرٌ مَفقود: {path} — الأَداةُ عَمياء.");
        var text = File.ReadAllText(path);
        Assert.True(text.Length > 500, $"أَداةٌ عَمياء: {path} طولُه {text.Length} مِحرَفاً — لَم يُقرَأ.");
        return text;
    }

    private static string Segment(string source, string from, string to)
    {
        var start = source.IndexOf(from, StringComparison.Ordinal);
        Assert.True(start >= 0, $"‏`{from}` غَيرُ مَوجود — الأَداةُ عَمياء.");
        var end = source.IndexOf(to, start + from.Length, StringComparison.Ordinal);
        Assert.True(end > start, $"‏`{to}` لا يَتلو `{from}` — الأَداةُ عَمياء.");
        return source.Substring(start, end - start);
    }

    /// <summary>صَفحَةُ الاستوديو بِلا تَعليقاتِ عَلامات.</summary>
    private static string StudioPage()
        => WriteEndpointGuardTests.StripMarkupComments(Source(Template, "Components", "Pages", "StudioAppDealDetail.razor"));

    /// <summary>جِسمُ النُقطَةِ الإدارِيَّةِ بِلا تَعليقات.</summary>
    private static string AdvanceEndpoint()
        => WriteEndpointGuardTests.StripComments(Segment(Source(Template, "MarketplaceTemplateExtensions.cs"),
            "\"/studio/apps/{slug}/deals/{id:guid}/advance\"", ".DisableAntiforgery();"));

    // ─── المَصفوفَة ─────────────────────────────────────────────────────────────

    private static readonly string[] Patterns = { "marketplace", "rental", "trip", "service", "classifieds", "pattern_from_the_future" };
    private static readonly string?[] Pays = { null, CheckoutPayMethods.Card, CheckoutPayMethods.CashOnDelivery };
    private static readonly DealStatus[] Statuses = { DealStatus.Active, DealStatus.Completed, DealStatus.Cancelled, DealStatus.Disputed };

    /// <summary>تَشكيلاتُ الأَطراف — كُلٌّ بِاسمِه: صَفقَةُ سَلَّةٍ مَربوطَة، وعَرضٌ لَم يُقبَل (أَو صَفقَةٌ قَديمَةٌ بِلا طَرَف)،
    /// وبَذرَةُ الاستوديو، وطَرَفٌ عَيَّنَه المَسارُ الإداريُّ قَبلُ، ومالِكُ التَطبيقِ مُشتَرِياً في مَتجَرِه، وبَذرَةٌ مُشِيَت
    /// قَبلَ <c>11a8d4d</c> ومالِكُ التَطبيقِ طَرَفاها.</summary>
    private static readonly (string Name, Guid Initiator, Guid? Counterparty)[] Parties =
    {
        ("cart_bound",          Buyer,    Seller),
        ("unbound_offer",       Buyer,    null),
        ("studio_seed",         AppOwner, null),
        ("admin_assigned",      Buyer,    AppOwner),
        ("owner_buys_in_store", AppOwner, Seller),
        ("legacy_seed_walked",  AppOwner, AppOwner),
    };

    private sealed record Case(string Pattern, string? Pay, DealStage Stage, string Parties, DealStatus Status,
        bool PageDraws, bool Moves, bool Wrote, Deal Before);

    private static (DealsService Deals, DocWorld World, Deal Deal) At(
        string pattern, string? pay, DealStage stage, Guid initiator, Guid? counterparty, DealStatus status)
    {
        var world = new DocWorld();
        var deals = new DealsService(world.Store, new ForbiddenPaymentProvider());
        var deal = new Deal
        {
            Id = Guid.NewGuid(), TenantSlug = Slug, Pattern = pattern, Stage = stage, Status = status, AmountSar = 100,
            InitiatorId = initiator, InitiatorName = "مُبادِر", CounterpartyId = counterparty,
        };
        if (pay is not null) deal.Attributes[CheckoutPayMethods.AttributeKey] = pay;
        world.Put(deal);
        return (deals, world, deal);
    }

    /// <summary>نَسخٌ لِما يُقرَأُ قَبلَ التَسَلسُل — عالَمُ الوَثائِقِ يُعيدُ الكائِنَ نَفسَه لا نُسخَة، والتَعيينُ يُغَيِّرُه.</summary>
    private static Deal Copy(Deal d)
    {
        var c = new Deal
        {
            Id = d.Id, TenantSlug = d.TenantSlug, Pattern = d.Pattern, Stage = d.Stage, Status = d.Status, AmountSar = d.AmountSar,
            InitiatorId = d.InitiatorId, InitiatorName = d.InitiatorName, CounterpartyId = d.CounterpartyId, CounterpartyName = d.CounterpartyName,
        };
        foreach (var (k, v) in d.Attributes) c.Attributes[k] = v;
        foreach (var (k, v) in d.Refs) c.Refs[k] = v;
        return c;
    }

    /// <summary>
    /// <para><b>تَسَلسُلُ النُقطَةِ بَعدَ حارِسِ المِلكِيَّةِ — كَما هُوَ اليَوم حَرفاً</b> (مُثَبَّتٌ في
    /// <c>DealCounterpartyCharacterizationTests.The_admin_path_makes_the_app_owner_the_counterparty_…</c>): تَحميلٌ، ثُمَّ
    /// تَعيينُ مالِكِ التَطبيقِ حَيثُ لا طَرَف، ثُمَّ التَحريكُ بِـ<c>CounterpartyId</c> اللَقطَةِ أَو مالِكِ التَطبيق.
    /// واللَقطَةُ تُقرَأُ قَبلَ التَعيين — كَما في Marten حَيثُ <c>LoadAsync</c> نُسخَةٌ مُستَقِلَّة.</para>
    /// </summary>
    private static DealAdvanceResult EndpointSequence(DealsService deals, Guid id)
    {
        var deal = Now(deals.LoadAsync(Slug, id))!;
        var snapshotCounterparty = deal.CounterpartyId;
        if (snapshotCounterparty is null)
            Now(deals.AssignCounterpartyAsync(Slug, id, AppOwner, "مالِك التَّطبيق"));
        var actorId = snapshotCounterparty ?? AppOwner;
        return Now(deals.AdvanceAsync(Slug, id, actorId, "مالِك التَّطبيق (إداريّ)", ""));
    }

    /// <summary>كُلُّ حالَةٍ في المَصفوفَة: ما تَرسُمُه الصَفحَةُ بِشَرطِها <paramref name="pageDraws"/>، وما يَفعَلُه تَسَلسُلُ
    /// النُقطَة <paramref name="endpoint"/> عَلى خِدمَةٍ حَقيقِيَّةٍ في عالَمٍ جَديد، وهَل حَفِظَ شَيئاً.</summary>
    private static List<Case> Measure(Func<Deal, bool> pageDraws, Func<DealsService, Deal, DealAdvanceResult?> endpoint)
    {
        var cases = new List<Case>();
        foreach (var pattern in Patterns)
        foreach (var pay in Pays)
        foreach (var stage in DealsPolicy.StagesFor(pattern))
        foreach (var (name, initiator, counterparty) in Parties)
        foreach (var status in Statuses)
        {
            var (deals, world, deal) = At(pattern, pay, stage, initiator, counterparty, status);
            var before = Copy(deal);
            var draws = pageDraws(before);
            var saves = world.SaveCalls;
            var res = endpoint(deals, before);
            cases.Add(new Case(pattern, pay, stage, name, status, draws, res?.Ok ?? false, world.SaveCalls > saves, before));
        }
        return cases;
    }

    private static string Name(Case c) => $"{c.Pattern}/{c.Pay ?? "—"}/{c.Stage}/{c.Parties}/{c.Status}";

    /// <summary>النُقطَةُ كَما صارَت: الدالَّةُ أَوَّلاً عَلى ما قُرِئ — فَإن رَدَّت عادَت بِلا كِتابَة — ثُمَّ التَسَلسُلُ نَفسُه.</summary>
    private static DealAdvanceResult? GatedEndpoint(DealsService deals, Deal before)
        => DealsService.AdminPathCanMove(before, AppOwner) ? EndpointSequence(deals, before.Id) : null;

    // ═══ ١) الصَفحَةُ والنُقطَةُ تَقرَآنِ دالَّةً واحِدَة ═════════════════════════════════════

    /// <summary>
    /// <para><b>قُلِبَ بِاسمِه</b> (مُراجَعَةُ تَيّارِ S6a الأولى، ‏2026-10-12؛ ‏ADR-033 §٦‑ط مُلحَقاً): كانَ
    /// <c>Today_the_studio_page_draws_the_advance_form_for_every_active_deal_with_a_next_stage_and_the_endpoint_silently_refuses_some</c>
    /// (مُودَعٌ في <c>2194c88</c>) — الصَفحَةُ تَرسُمُ الاستِمارَةَ لِكُلِّ صَفقَةٍ فَعّالَةٍ لَها تالٍ، والنُقطَةُ تَرُدُّ بَعضَها بِلا
    /// رِسالَة: «فُحِصَ ‏2304 حالَة: تَرسُمُ الصَفحَةُ ‏468، يَتَحَرَّكُ مِنها ‏396؛ زِرٌّ يُرَدُّ ‏72، ومَخفِيٌّ يَتَحَرَّكُ ‏0، ومَردودٌ بَعدَ
    /// كِتابَةٍ ‏27». <b>وصارَت الصَفحَةُ تَرسُمُه والنُقطَةُ تَكتُبُ بِـ<see cref="DealsService.AdminPathCanMove"/> وَحدَها</b>:
    /// صِفرُ زِرٍّ يُرَدّ، وصِفرُ مَخفِيٍّ يَتَحَرَّك، وصِفرُ رَفضٍ بَعدَ كِتابَة — والمَصفوفَةُ نَفسُها.</para>
    /// </summary>
    [Fact]
    public void The_studio_page_draws_the_advance_form_exactly_where_the_admin_path_moves_the_deal_and_a_refused_click_writes_nothing()
    {
        // الشَرطُ كَما في المَصدَر: الحالَةُ ثُمَّ الدالَّةُ بِهُوِيَّةِ الناظِر (مالِكِ التَطبيق — `OnInitializedAsync` يَرُدُّ غَيرَه) ثُمَّ التالي.
        var page = StudioPage();
        var actions = Segment(page, "@if (deal.Status == DealStatus.Active)", "/advance\")");
        Assert.Contains("@if (DealsService.AdminPathCanMove(deal, Auth.UserId!.Value) && DealsPolicy.Next(deal) is { } next)", actions, StringComparison.Ordinal);
        Assert.Equal(2, actions.Split("@if", StringSplitOptions.None).Length - 1);
        Assert.Contains("if (t is null || t.OwnerUserId != Auth.UserId!.Value) return;", page, StringComparison.Ordinal);

        var cases = Measure(
            d => d.Status == DealStatus.Active && DealsService.AdminPathCanMove(d, AppOwner) && DealsPolicy.Next(d) is not null,
            GatedEndpoint);

        var drawnButRefused = cases.Where(c => c.PageDraws && !c.Moves).ToList();
        var hiddenButMoves  = cases.Where(c => !c.PageDraws && c.Moves).ToList();
        var refusedAfterWriting = cases.Where(c => !c.Moves && c.Wrote).ToList();
        var line = $"فُحِصَ {cases.Count} حالَة: تَرسُمُ الصَفحَةُ {cases.Count(c => c.PageDraws)}، يَتَحَرَّكُ مِنها {cases.Count(c => c.Moves)}؛ " +
                   $"زِرٌّ يُرَدُّ {drawnButRefused.Count}، ومَخفِيٌّ يَتَحَرَّكُ {hiddenButMoves.Count}، ومَردودٌ بَعدَ كِتابَةٍ {refusedAfterWriting.Count}.";
        output.WriteLine(line);

        Assert.True(cases.Count > 1000, line);
        Assert.True(cases.Count(c => c.PageDraws) > 0 && cases.Count(c => !c.PageDraws) > 0, line);
        Assert.True(drawnButRefused.Count == 0, line + " — " + string.Join("، ", drawnButRefused.Take(5).Select(Name)));
        Assert.True(hiddenButMoves.Count == 0, line + " — " + string.Join("، ", hiddenButMoves.Take(5).Select(Name)));
        Assert.True(refusedAfterWriting.Count == 0, line + " — " + string.Join("، ", refusedAfterWriting.Take(5).Select(Name)));
    }

    /// <summary><b>الدالَّةُ تُجيبُ بِما يَفعَلُه التَسَلسُلُ — حالَةً حالَة</b>: لِكُلِّ حالَةٍ في المَصفوفَة، <c>AdminPathCanMove</c>
    /// عَلى ما قُرِئَ تُساوي نَتيجَةَ تَسَلسُلِ النُقطَةِ (التَعيينُ ثُمَّ <c>AdvanceAsync</c>) عَلى خِدمَةٍ حَقيقِيَّة — فَإن غَيَّرَ أَحَدٌ
    /// بَوّابَةَ الفاعِلِ أَو ثابِتَ التَعيينِ أَو التالي ولَم يُغَيِّر الدالَّة أَحمَرَّ هذا بِأَسماءِ الحالات. والمُوجِبُ والسالِبُ
    /// كِلاهُما يُعَدّانِ ويُشتَرَطُ أَن لا يَكونَ أَيٌّ مِنهُما صِفراً.</summary>
    [Fact]
    public void The_predicate_answers_what_the_endpoint_sequence_does_in_every_case()
    {
        var cases = Measure(d => DealsService.AdminPathCanMove(d, AppOwner), (deals, d) => EndpointSequence(deals, d.Id));
        var disagree = cases.Where(c => c.PageDraws != c.Moves).ToList();
        var line = $"فُحِصَ {cases.Count} حالَة: تُجيزُ الدالَّةُ {cases.Count(c => c.PageDraws)} وتَرُدُّ {cases.Count(c => !c.PageDraws)}؛ " +
                   $"خِلافُها والتَسَلسُلِ {disagree.Count}.";
        output.WriteLine(line);
        Assert.True(cases.Count > 1000 && cases.Count(c => c.PageDraws) > 0 && cases.Count(c => !c.PageDraws) > 0, line);
        Assert.True(disagree.Count == 0, line + " — " + string.Join("، ", disagree.Take(8).Select(Name)));
    }

    /// <summary>
    /// <para><b>وصَفقَةٌ طَلَبُها عِندَ OTO — الدالَّةُ والتَسَلسُلُ يَتَّفِقانِ حالَةً حالَة، ولا يَدخُلُ «شَحن» مِنها شَيء</b> (مُراجَعَةُ
    /// تَيّارِ S7 الثانِيَة، ‏2026-10-12؛ ‏ADR-033 §٧‑ح): المَصفوفَةُ نَفسُها وكُلُّ صَفقَةٍ فيها بِـ<c>oto_order_id</c>. صارَت
    /// <c>AdvanceAsync</c> تَرُدُّ بِـ<c>deal_awaits_carrier</c> دُخولَ «شَحن» بِلا جَوابِ OTO (<see cref="DealShipping.AwaitsCarrier"/>)،
    /// و<see cref="DealsService.AdminPathCanMove"/> تَسأَلُها — وكانَت تُجيزُ «→ شَحن» فَيَمضي التَسَلسُلُ بِلا بوليصَة (تَوصيفُه
    /// <c>e52ba8f</c>). يُقاسُ التَسَلسُلُ عارِياً (الدالَّةُ تُجيبُ بِما يَفعَل) ومَحروساً بِالدالَّة (لا زِرَّ يُرَدّ، ولا مَخفِيَّ يَتَحَرَّك، ولا
    /// رَفضَ بَعدَ كِتابَة)؛ وما تاليه «شَحن» يُعَدُّ ويُشتَرَطُ أَن لا يَكونَ صِفراً (القاعِدَة ١٠).</para>
    /// </summary>
    [Fact]
    public void For_deals_whose_order_is_at_OTO_the_predicate_answers_the_sequence_and_no_door_enters_Shipping()
    {
        var bare = new List<Case>();
        var gated = new List<Case>();
        var awaiting = 0;
        foreach (var pattern in Patterns)
        foreach (var pay in Pays)
        foreach (var stage in DealsPolicy.StagesFor(pattern))
        foreach (var (name, initiator, counterparty) in Parties)
        foreach (var status in Statuses)
        {
            foreach (var (list, sequence) in new (List<Case>, Func<DealsService, Deal, DealAdvanceResult?>)[]
                     {
                         (bare, (deals, d) => EndpointSequence(deals, d.Id)),
                         (gated, GatedEndpoint),
                     })
            {
                var (deals, world, deal) = At(pattern, pay, stage, initiator, counterparty, status);
                deal.Refs[DealShipping.OrderRef] = deal.Id.ToString("N");
                var before = Copy(deal);
                var draws = DealsService.AdminPathCanMove(before, AppOwner);
                var saves = world.SaveCalls;
                var res = sequence(deals, before);
                list.Add(new Case(pattern, pay, stage, name, status, draws, res?.Ok ?? false, world.SaveCalls > saves, before));
            }
            if (status == DealStatus.Active && DealsPolicy.Next(pattern, stage, pay) == DealStage.Shipping) awaiting++;
        }

        var disagree = bare.Where(c => c.PageDraws != c.Moves).ToList();
        var drawnButRefused = gated.Where(c => c.PageDraws && !c.Moves).ToList();
        var hiddenButMoves = gated.Where(c => !c.PageDraws && c.Moves).ToList();
        var refusedAfterWriting = gated.Where(c => !c.Moves && c.Wrote).ToList();
        var enteredShipping = bare.Concat(gated)
            .Where(c => c.Moves && DealsPolicy.Next(c.Pattern, c.Stage, c.Pay) == DealStage.Shipping).ToList();
        var line = $"فُحِصَ {bare.Count} حالَة بِطَلَبٍ عِندَ OTO (مَرَّتَين): تُجيزُ الدالَّةُ {bare.Count(c => c.PageDraws)}؛ خِلافُها والتَسَلسُلِ " +
                   $"{disagree.Count}؛ زِرٌّ يُرَدُّ {drawnButRefused.Count}، ومَخفِيٌّ يَتَحَرَّكُ {hiddenButMoves.Count}، ومَردودٌ بَعدَ كِتابَةٍ " +
                   $"{refusedAfterWriting.Count}؛ فَعّالَةٌ تاليها «شَحن» {awaiting}، دَخَلَتها {enteredShipping.Count}.";
        output.WriteLine(line);

        Assert.True(bare.Count > 1000 && awaiting > 0 && bare.Count(c => c.PageDraws) > 0, line);
        Assert.True(disagree.Count == 0, line + " — " + string.Join("، ", disagree.Take(8).Select(Name)));
        Assert.True(drawnButRefused.Count == 0, line + " — " + string.Join("، ", drawnButRefused.Take(5).Select(Name)));
        Assert.True(hiddenButMoves.Count == 0, line + " — " + string.Join("، ", hiddenButMoves.Take(5).Select(Name)));
        Assert.True(refusedAfterWriting.Count == 0, line + " — " + string.Join("، ", refusedAfterWriting.Take(5).Select(Name)));
        Assert.True(enteredShipping.Count == 0, line + " — " + string.Join("، ", enteredShipping.Take(5).Select(Name)));
    }

    /// <summary>
    /// <para><b>وصَفقَةٌ عَلَيها فاتورَةُ بَوّابَةٍ لَم تُدفَع — الدالَّةُ والتَسَلسُلُ يَتَّفِقانِ حالَةً حالَة، ولا يَدخُلُ «دَفع» مِنها شَيء</b>
    /// (‏ADR-032 الخُطوَة 10، تَيّارُ S9، ‏2026-10-12): المَصفوفَةُ نَفسُها وكُلُّ صَفقَةٍ فيها بِـ<c>gateway_invoice_id</c> ولا «Captured». صارَت
    /// <c>AdvanceAsync</c> تَرُدُّ بِـ<c>deal_awaits_payment</c> دُخولَ «دَفع» قَبلَ أَن يَثبُتَ الدَفعُ (<see cref="GatewayPayments.AwaitsPayment"/>)،
    /// و<see cref="DealsService.AdminPathCanMove"/> تَسأَلُها — نَسَقُ صَفِّ OTO أَعلاه حَرفاً. وبِلا سُؤالِها رَسَمَ الاستوديو «→ دَفع» يُرَدُّ
    /// بِلا كَلِمَةٍ حَيثُ مالِكُ التَطبيقِ هُوَ المُبادِر (مَقيسٌ قَبلَ سَطرِ الدالَّة — §٨‑ج). وما تاليه «دَفع» يُعَدُّ ويُشتَرَطُ أَن لا يَكونَ
    /// صِفراً (القاعِدَة ١٠).</para>
    /// </summary>
    [Fact]
    public void For_deals_whose_gateway_invoice_is_unpaid_the_predicate_answers_the_sequence_and_no_door_enters_Paid()
    {
        var bare = new List<Case>();
        var gated = new List<Case>();
        var awaiting = 0;
        foreach (var pattern in Patterns)
        foreach (var pay in Pays)
        foreach (var stage in DealsPolicy.StagesFor(pattern))
        foreach (var (name, initiator, counterparty) in Parties)
        foreach (var status in Statuses)
        {
            foreach (var (list, sequence) in new (List<Case>, Func<DealsService, Deal, DealAdvanceResult?>)[]
                     {
                         (bare, (deals, d) => EndpointSequence(deals, d.Id)),
                         (gated, GatedEndpoint),
                     })
            {
                var (deals, world, deal) = At(pattern, pay, stage, initiator, counterparty, status);
                deal.Refs[GatewayPaymentProof.InvoiceRef] = "inv_" + deal.Id.ToString("N");
                var before = Copy(deal);
                var draws = DealsService.AdminPathCanMove(before, AppOwner);
                var saves = world.SaveCalls;
                var res = sequence(deals, before);
                list.Add(new Case(pattern, pay, stage, name, status, draws, res?.Ok ?? false, world.SaveCalls > saves, before));
            }
            if (status == DealStatus.Active && DealsPolicy.Next(pattern, stage, pay) == DealStage.Paid) awaiting++;
        }

        var disagree = bare.Where(c => c.PageDraws != c.Moves).ToList();
        var drawnButRefused = gated.Where(c => c.PageDraws && !c.Moves).ToList();
        var hiddenButMoves = gated.Where(c => !c.PageDraws && c.Moves).ToList();
        var refusedAfterWriting = gated.Where(c => !c.Moves && c.Wrote).ToList();
        var enteredPaid = bare.Concat(gated)
            .Where(c => c.Moves && DealsPolicy.Next(c.Pattern, c.Stage, c.Pay) == DealStage.Paid).ToList();
        var line = $"فُحِصَ {bare.Count} حالَة بِفاتورَةِ بَوّابَةٍ لَم تُدفَع (مَرَّتَين): تُجيزُ الدالَّةُ {bare.Count(c => c.PageDraws)}؛ خِلافُها والتَسَلسُلِ " +
                   $"{disagree.Count}؛ زِرٌّ يُرَدُّ {drawnButRefused.Count}، ومَخفِيٌّ يَتَحَرَّكُ {hiddenButMoves.Count}، ومَردودٌ بَعدَ كِتابَةٍ " +
                   $"{refusedAfterWriting.Count}؛ فَعّالَةٌ تاليها «دَفع» {awaiting}، دَخَلَتها {enteredPaid.Count}.";
        output.WriteLine(line);

        Assert.True(bare.Count > 1000 && awaiting > 0 && bare.Count(c => c.PageDraws) > 0, line);
        Assert.True(disagree.Count == 0, line + " — " + string.Join("، ", disagree.Take(8).Select(Name)));
        Assert.True(drawnButRefused.Count == 0, line + " — " + string.Join("، ", drawnButRefused.Take(5).Select(Name)));
        Assert.True(hiddenButMoves.Count == 0, line + " — " + string.Join("، ", hiddenButMoves.Take(5).Select(Name)));
        Assert.True(refusedAfterWriting.Count == 0, line + " — " + string.Join("، ", refusedAfterWriting.Take(5).Select(Name)));
        Assert.True(enteredPaid.Count == 0, line + " — " + string.Join("، ", enteredPaid.Take(5).Select(Name)));
    }

    // ═══ ٢) مُوجِبٌ وسالِبٌ بِأَسمائِهِما ═════════════════════════════════════════════════

    /// <summary>
    /// <para><b>المُوجِب</b>: يَقبَلُ عَن البائِعِ المَربوطِ عِندَ <c>Offered</c>، ويَمضي بِخُطُواتِ جانِبِ المَتجَرِ (إلى
    /// <c>Shipping</c> لِلنَقد، وخَتمُ الإيجار)، ويُعَيِّنُ مالِكَ التَطبيقِ طَرَفاً لِعَرضٍ بِلا طَرَفٍ فَيَحجِز.
    /// <b>والسالِب</b>: صَفقَةُ البَذرَةِ التَجريبِيَّة (لا يُعَيَّنُ المُبادِرُ طَرَفاً — <c>11a8d4d</c>)، ودُخولُ <c>Paid</c> و<c>Received</c>
    /// (خُطوَتا المُشتَري — ‏ADR-033 §٦‑ط)، وآخِرُ مَرحَلَة، وصَفقَةٌ غَيرُ فَعّالَة.</para>
    /// </summary>
    [Theory]
    [InlineData("marketplace", "card", DealStage.Offered,   "cart_bound",     DealStatus.Active,    true)]
    [InlineData("marketplace", "cash", DealStage.Confirmed, "cart_bound",     DealStatus.Active,    true)]
    [InlineData("rental",      "card", DealStage.Received,  "cart_bound",     DealStatus.Active,    true)]
    [InlineData("marketplace", null,   DealStage.Offered,   "unbound_offer",  DealStatus.Active,    true)]
    [InlineData("marketplace", null,   DealStage.Booked,    "admin_assigned", DealStatus.Active,    true)]
    [InlineData("marketplace", null,   DealStage.Offered,   "studio_seed",    DealStatus.Active,    false)]
    [InlineData("marketplace", "card", DealStage.Confirmed, "cart_bound",     DealStatus.Active,    false)]
    [InlineData("rental",      "card", DealStage.Delivered, "cart_bound",     DealStatus.Active,    false)]
    [InlineData("classifieds", null,   DealStage.Confirmed, "cart_bound",     DealStatus.Active,    false)]
    [InlineData("marketplace", "card", DealStage.Booked,    "cart_bound",     DealStatus.Cancelled, false)]
    public void The_admin_path_moves_the_store_sides_steps_and_not_the_buyers_nor_the_demo_seed(
        string pattern, string? pay, DealStage stage, string parties, DealStatus status, bool moves)
    {
        var (_, initiator, counterparty) = Parties.Single(p => p.Name == parties);
        var (deals, world, deal) = At(pattern, pay, stage, initiator, counterparty, status);
        Assert.Equal(moves, DealsService.AdminPathCanMove(Copy(deal), AppOwner));

        // والنُقطَةُ بِالدالَّةِ أَوَّلاً: ما تَرُدُّه لا يَكتُبُ شَيئاً، وما تُجيزُه يَدخُلُ التالي.
        var res = GatedEndpoint(deals, Copy(deal));
        Assert.Equal(moves, res?.Ok ?? false);
        Assert.Equal(moves ? (counterparty is null ? 2 : 1) : 0, world.SaveCalls);   // تَعيينٌ حَيثُ لا طَرَف، ثُمَّ التَحريك
        Assert.Equal(moves ? DealsPolicy.Next(pattern, stage, pay) : stage, world.Read<Deal>(deal.Id.ToString())!.Stage);
    }

    /// <summary><b>النُقطَةُ تَسأَلُ الدالَّةَ بَعدَ حارِسِ المِلكِيَّةِ وقَبلَ أَوَّلِ كِتابَة</b> — بِالهُوِيَّةِ نَفسِها الَّتي يَسقُطُ
    /// إلَيها الفاعِل (<c>auth.UserId!.Value</c>)، ورَدُّها عَودَةٌ إلى الصَفحَةِ بِلا كِتابَة. (ولَيسَت في <c>BodyGuards</c>
    /// عَمداً: الدالَّةُ تَأخُذُ الهُوِيَّةَ وَسيطاً ولا تَتَحَقَّقُ مِنها — الحارِسُ <c>StudioOwnsAsync</c> قَبلَها.)</summary>
    [Fact]
    public void The_admin_endpoint_asks_the_predicate_after_the_ownership_guard_and_before_any_write()
    {
        var endpoint = AdvanceEndpoint();
        var owns   = endpoint.IndexOf("if (!await StudioOwnsAsync(store, auth, slug)) return Results.Redirect(\"/studio\");", StringComparison.Ordinal);
        var load   = endpoint.IndexOf("var deal = await deals.LoadAsync(slug, id);", StringComparison.Ordinal);
        // (‏2026-10-13، مُراجَعَةُ المَوجَة 3a — G4 (i)؛ ولا يُعادُ حَرفٌ فَوقَ ما سَبَق: سَطرُ البَوّابَةِ صارَ يَسأَلُ `AdminPathRefusal` — «لِماذا»
        // الدالَّةِ نَفسِها، و`AdminPathCanMove` «لا رَمز» — ويَعودُ بِـ`?err=`؛ فَتَبَدَّلَ حَرفُ السَطرِ المُثَبَّتِ وعَدُّ اسمِه، والتَرتيبُ بِحَرفِه.)
        var gate   = endpoint.IndexOf("if (Services.Deals.DealsService.AdminPathRefusal(deal, auth.UserId!.Value) is { } refused) return Results.Redirect($\"/studio/apps/{slug}/deals/{id}?err={refused}\");", StringComparison.Ordinal);
        var assign = endpoint.IndexOf("await deals.AssignCounterpartyAsync(slug, id,", StringComparison.Ordinal);
        var move   = endpoint.IndexOf("var result = await deals.AdvanceAsync(slug, id, actorId,", StringComparison.Ordinal);
        Assert.True(owns > 0 && load > owns && gate > load && assign > gate && move > assign,
            $"تَرتيبُ النُقطَة: owns={owns} load={load} gate={gate} assign={assign} move={move}");
        Assert.Contains("var actorId = deal.CounterpartyId ?? auth.UserId!.Value;", endpoint, StringComparison.Ordinal);
        Assert.Equal(1, endpoint.Split("AdminPathRefusal(", StringSplitOptions.None).Length - 1);
        Assert.Equal(0, endpoint.Split("AdminPathCanMove(", StringSplitOptions.None).Length - 1);
    }
}
