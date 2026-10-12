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

    // ═══ اليَوم ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// <para><b>اليَوم — يُقلَبُ بِاسمِه في كوميتِ الإصلاح</b>: الصَفحَةُ تَرسُمُ الاستِمارَةَ لِكُلِّ صَفقَةٍ فَعّالَةٍ لَها تالٍ
    /// (شَرطُها <c>deal.Status == Active</c> ثُمَّ <c>DealsPolicy.Next(deal) is { } next</c> بِلا ثالِث — مُثَبَّتٌ مِن المَصدَر)،
    /// والنُقطَةُ تَرُدُّ بَعضَها بِلا رِسالَة: صَفقَةَ البَذرَةِ عِندَ <c>Offered</c>، وخُطوَتَي المُشتَري في كُلِّ نَمَط. <b>وفي
    /// بَعضِ ما تَرُدُّه تَكتُبُ قَبلَ أَن تَرُدّ</b>: صَفقَةٌ بِلا طَرَفٍ عِندَ خُطوَةِ المُشتَري يُعَيَّنُ مالِكُ التَطبيقِ طَرَفَها
    /// (حِفظٌ)، ثُمَّ يُرَدُّ التَحريك. ولا عَكس: لا صَفقَةَ تُخفي الصَفحَةُ زِرَّها والنُقطَةُ تُحَرِّكُها.</para>
    /// </summary>
    [Fact]
    public void Today_the_studio_page_draws_the_advance_form_for_every_active_deal_with_a_next_stage_and_the_endpoint_silently_refuses_some()
    {
        // الشَرطُ كَما في المَصدَر: الحالَةُ ثُمَّ التالي — ولا شَرطَ ثالِثَ بَينَهُما وبَينَ الاستِمارَة.
        var page = StudioPage();
        var actions = Segment(page, "@if (deal.Status == DealStatus.Active)", "/advance\")");
        Assert.Contains("@if (DealsPolicy.Next(deal) is { } next)", actions, StringComparison.Ordinal);
        Assert.Equal(2, actions.Split("@if", StringSplitOptions.None).Length - 1);

        // والنُقطَةُ كَما هِيَ: حارِسُ المِلكِيَّةِ ثُمَّ التَحميلُ ثُمَّ التَعيينُ ثُمَّ التَحريك — بِلا سُؤالٍ قَبلَ الكِتابَة.
        var endpoint = AdvanceEndpoint();
        var load   = endpoint.IndexOf("var deal = await deals.LoadAsync(slug, id);", StringComparison.Ordinal);
        var assign = endpoint.IndexOf("await deals.AssignCounterpartyAsync(slug, id,", StringComparison.Ordinal);
        var move   = endpoint.IndexOf("var result = await deals.AdvanceAsync(slug, id, actorId,", StringComparison.Ordinal);
        Assert.True(load > 0 && assign > load && move > assign, $"تَسَلسُلُ النُقطَةِ تَغَيَّر: load={load} assign={assign} move={move}");

        var cases = Measure(
            d => d.Status == DealStatus.Active && DealsPolicy.Next(d) is not null,
            (deals, d) => EndpointSequence(deals, d.Id));

        var drawnButRefused = cases.Where(c => c.PageDraws && !c.Moves).ToList();
        var hiddenButMoves  = cases.Where(c => !c.PageDraws && c.Moves).ToList();
        var refusedAfterWriting = cases.Where(c => !c.Moves && c.Wrote).ToList();
        var line = $"فُحِصَ {cases.Count} حالَة: تَرسُمُ الصَفحَةُ {cases.Count(c => c.PageDraws)}، يَتَحَرَّكُ مِنها {cases.Count(c => c.Moves)}؛ " +
                   $"زِرٌّ يُرَدُّ {drawnButRefused.Count}، ومَخفِيٌّ يَتَحَرَّكُ {hiddenButMoves.Count}، ومَردودٌ بَعدَ كِتابَةٍ {refusedAfterWriting.Count}.";
        output.WriteLine(line);

        Assert.True(cases.Count > 1000, line);
        Assert.True(drawnButRefused.Count > 0, line);
        Assert.Empty(hiddenButMoves);
        Assert.True(refusedAfterWriting.Count > 0, line);

        // بِأَسمائِها — ما وَصَفَته المُراجَعَة:
        Assert.Contains(drawnButRefused, c => Name(c) == "marketplace/—/Offered/studio_seed/Active");          // البَذرَةُ لا تَتَحَرَّك
        Assert.Contains(drawnButRefused, c => Name(c) == "marketplace/card/Confirmed/cart_bound/Active");       // إلى Paid — المُشتَري يَدفَع
        Assert.Contains(drawnButRefused, c => Name(c) == "rental/card/Delivered/cart_bound/Active");            // إلى Received — المُستَأجِرُ يَستَلِم
        Assert.Contains(refusedAfterWriting, c => Name(c) == "marketplace/card/Confirmed/unbound_offer/Active"); // يُعَيَّنُ ثُمَّ يُرَدّ
        Assert.DoesNotContain(drawnButRefused, c => Name(c) == "marketplace/cash/Confirmed/cart_bound/Active");  // إلى Shipping — يَمضي
    }
}
