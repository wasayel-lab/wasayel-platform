using ACommerce.Kit.Payments;
using Marten;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ إرسالانِ مُتَداخِلانِ لِسَلَّةٍ واحِدَة — أَيَلِدانِ مَجموعَتَينِ مِن الطَلَبات؟ (تَدقيقُ الإطلاق ‏2026-10-24، lens-money-integrity-1) ═══
//
// **المَقيسُ قَبلَ اللَمس** (عِندَ `dace7cd`): `POST /{slug}/checkout/submit` يَقرَأُ السَلَّةَ أَوَّلاً، ويُودِعُ كُلَّ صَفقَةٍ فَوراً (`StartAsync` بِمُعَرِّفٍ جَديدٍ
// في كُلِّ نِداء)، ولا يَحذِفُ السَلَّةَ إلّا في آخِرِه — و`UseOptimisticConcurrency` عَلى `Deal` وَحدَها. فَإرسالانِ مُتَداخِلانِ يَقرَآنِ السَلَّةَ نَفسَها
// ويَلِدانِ مَجموعَتَين (مِجَسُّ الشَكّاك: «trials with a duplicate order: 5/5 · deal notifications +10»، وبِفاصِلِ ‏150 ms ‏0 مِن 3)؛ وعَلى البِطاقَةِ
// فاتورَتَين. وتَعليقٌ في الكودِ يَقولُ «idempotency-key بِـ dealId يَمنَع تَكرار الـ authorize لَو أَعادَ المُستَخدِم submit بِنَفس النَّموذَج» —
// والمُعَرِّفُ جَديدٌ في كُلِّ إرسال، فَالمِفتاحُ لا يَمنَعُ شَيئاً.
//
// **التَوصيفُ قَبلَ التَبديل** (القاعِدَة ٣): صَفّا «اليَوم» أَدناه يُودَعانِ وَحدَهُما، ويُقلَبانِ بِاسمَيهِما في كوميتِ المُطالَبَة.
//
// **وقُلِبا بِاسمَيهِما** (‏2026-10-24؛ تَوصيفُهُما `94a7ef0`؛ ‏ADR-036 §١٤): مُعَرِّفُ صَفقَةِ كُلِّ بَندٍ مِن نُسخَةِ السَلَّةِ الَّتي قُرِئَت
// (`CheckoutClaim.DealIdFor`)، و`Store` صَفقَةٍ جَديدَةٍ بِمُعَرِّفٍ قائِمٍ يَرُدُّه التَزامُنُ التَفاؤُلِيُّ نَفسُه — فَالإرسالُ الثاني يُرَدُّ عِندَ بَندِه الأَوَّلِ
// قَبلَ أَيِّ حَجزٍ أَو فاتورَةٍ أَو إشعار. والبُرهانُ الحَيُّ عَلى Postgres في `LiveCheckoutClaimProofTests` أَدناه.

public class CheckoutClaimTests(ITestOutputHelper output)
{
    private const string Slug = "zz-checkout-claim";
    private static readonly Guid Buyer = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");

    private static string RepoRoot => ThemeZeroEquivalenceTests.RepoRoot;
    private const string Template = "libs/templates/ACommerce.Templates.Customer.Marketplace";

    private sealed class ForbiddenPaymentProvider : IPaymentProvider
    {
        private static InvalidOperationException Touched() => new("الوِلادَةُ لا تَمَسُّ المال — نودِيَ مُزَوِّدُ الدَفع.");
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

    /// <summary>جِسمُ <c>/checkout/submit</c> بِتَعليقاتِه (التَعليقُ الكاذِبُ جُزءٌ مِمّا يُثَبَّت).</summary>
    private static string Submit()
        => Segment(Source(Template, "MarketplaceTemplateExtensions.cs"), "app.MapPost(\"/{slug}/checkout/submit\"", ".RequireStoreWritable();");

    private static readonly DateTime CartVersion = new(2026, 10, 24, 9, 30, 0, 123, DateTimeKind.Utc);

    /// <summary>
    /// <para><b>قُلِبَ بِاسمِه</b> (‏2026-10-24؛ كانَ <c>Today_two_submits_of_one_cart_line_birth_two_deals_with_fresh_ids</c>، مُودَعٌ في <c>94a7ef0</c>):
    /// بَندُ السَلَّةِ نَفسُه بِنُسخَتِها نَفسِها في إرسالَين ⇒ المُعَرِّفُ واحِد، والوِلادَةُ الثانِيَةُ يَرُدُّ حِفظَها ما يَرُدُّه المَخزَنُ الحَقيقِيُّ
    /// (<c>JasperFx.ConcurrencyException</c> — مَقيسٌ حَيّاً أَدناه، ويُحقَنُ هُنا عِندَ <c>Store</c> مُعَرِّفٍ قائِم) — صَفقَةٌ واحِدَةٌ مَكتوبَة، ومِفتاحُ
    /// الحَجزِ واحِد.</para>
    /// </summary>
    [Fact]
    public void Two_submits_of_one_cart_version_birth_one_deal_and_the_second_birth_is_refused()
    {
        var world = new DocWorld();
        var deals = new DealsService(world.Store, new ForbiddenPaymentProvider());
        var listing = Guid.NewGuid();
        var id = CheckoutClaim.DealIdFor(Slug, Buyer, CartVersion, 0, listing);
        world.BeforeMember = name =>
        {
            if (name == "Store" && world.Read<Deal>(id.ToString()) is not null)
                throw new JasperFx.ConcurrencyException(typeof(Deal), id);
        };
        Deal Born() => Now(deals.StartAsync(Slug, "marketplace", Buyer, "مُشتَرٍ", listing, "سِلعَة", 100m,
            id: CheckoutClaim.DealIdFor(Slug, Buyer, CartVersion, 0, listing)));

        var first = Born();
        Assert.Throws<JasperFx.ConcurrencyException>(() => Born());

        Assert.Equal(id, first.Id);
        Assert.Equal(1, world.Wrote<Deal>());
        Assert.Equal($"deal_{id}", $"deal_{CheckoutClaim.DealIdFor(Slug, Buyer, CartVersion, 0, listing)}");
        output.WriteLine($"بَندٌ واحِدٌ وإرسالان بِنُسخَةٍ واحِدَة: صَفَقاتٌ مَكتوبَة={world.Wrote<Deal>()} · الثاني مَردود · مِفتاحُ الحَجزِ واحِد");
    }

    /// <summary>وبِلا مُعَرِّفٍ يَمضي <c>StartAsync</c> كَما كان — مُعَرِّفٌ جَديدٌ لِكُلِّ وِلادَة (عَرضٌ عَلى إعلان، والبَذرَة).</summary>
    [Fact]
    public void Without_an_id_StartAsync_births_a_fresh_id_as_before()
    {
        var world = new DocWorld();
        var deals = new DealsService(world.Store, new ForbiddenPaymentProvider());
        var a = Now(deals.StartAsync(Slug, "marketplace", Buyer, "مُشتَرٍ", null, "عَرض", 50m));
        var b = Now(deals.StartAsync(Slug, "marketplace", Buyer, "مُشتَرٍ", null, "عَرض", 50m));
        Assert.NotEqual(a.Id, b.Id);
        Assert.Equal(2, world.Wrote<Deal>());
    }

    // ═══ الدالَّةُ النَقِيَّة ═══════════════════════════════════════════════════════════════════════════════════

    /// <summary>مُوجِب: المَدخَلاتُ نَفسُها ⇒ المُعَرِّفُ نَفسُه؛ والإصدارُ 8 والمُتَغَيِّرُ RFC.</summary>
    [Fact]
    public void DealIdFor_is_deterministic_for_one_cart_version()
    {
        var listing = Guid.Parse("11111111-2222-4333-8444-555555555555");
        var a = CheckoutClaim.DealIdFor(Slug, Buyer, CartVersion, 2, listing);
        var b = CheckoutClaim.DealIdFor(Slug, Buyer, new DateTime(CartVersion.Ticks, DateTimeKind.Utc), 2, listing);
        Assert.Equal(a, b);
        var text = a.ToString("D");
        Assert.Equal('8', text[14]);
        Assert.Contains(text[19], "89ab");
    }

    /// <summary>سالِب: أَيُّ مَدخَلٍ يَتَغَيَّرُ ⇒ مُعَرِّفٌ آخَر — المَتجَر، والمُشتَري، ونُسخَةُ السَلَّةِ (‏tick واحِد)، ومَوضِعُ البَند، والإعلان.</summary>
    [Fact]
    public void DealIdFor_changes_with_every_input()
    {
        var listing = Guid.Parse("11111111-2222-4333-8444-555555555555");
        var baseline = CheckoutClaim.DealIdFor(Slug, Buyer, CartVersion, 0, listing);
        var variants = new[]
        {
            CheckoutClaim.DealIdFor(Slug + "-x", Buyer, CartVersion, 0, listing),
            CheckoutClaim.DealIdFor(Slug, Guid.NewGuid(), CartVersion, 0, listing),
            CheckoutClaim.DealIdFor(Slug, Buyer, CartVersion.AddTicks(1), 0, listing),
            CheckoutClaim.DealIdFor(Slug, Buyer, CartVersion, 1, listing),
            CheckoutClaim.DealIdFor(Slug, Buyer, CartVersion, 0, Guid.NewGuid()),
        };
        Assert.All(variants, v => Assert.NotEqual(baseline, v));
        Assert.Equal(variants.Length, variants.Distinct().Count());
    }

    // ═══ البابُ في المَصدَر ═════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <para><b>الفاحِص</b>: في جِسمِ <c>/checkout/submit</c> — المُعَرِّفُ مِن <see cref="CheckoutClaim.DealIdFor"/> بِنُسخَةِ السَلَّة، والتِقاطُ
    /// تَعارُضِ البَندِ الأَوَّلِ يَعودُ إلى «صَفقاتي»، وكِلاهُما قَبلَ الحَجزِ والرَبطِ والفاتورَةِ والإشعار؛ ورَدُّ فَتحِ الفاتورَةِ يُجَدِّدُ نُسخَةَ السَلَّةِ قَبلَ
    /// أَن يَعود. يُعيدُ ما خالَف.</para>
    /// </summary>
    private static List<string> ClaimViolations(string submit)
    {
        var v = new List<string>();
        const string Id = "id: Services.Deals.CheckoutClaim.DealIdFor(slug, userId, cart.UpdatedAt, line++, item.ListingId)";
        const string Catch = "catch (JasperFx.ConcurrencyException) when (created.Count == 0)";
        var id = submit.IndexOf(Id, StringComparison.Ordinal);
        var caught = submit.IndexOf(Catch, StringComparison.Ordinal);
        if (id < 0) v.Add("الوِلادَةُ بِلا مُعَرِّفِ المُطالَبَة");
        if (caught < 0) v.Add("لا التِقاطَ لِتَعارُضِ البَندِ الأَوَّل");
        else if (!submit[caught..].TrimStart().Contains("return Results.Redirect(Link(req, slug, \"deals\"));", StringComparison.Ordinal))
            v.Add("الالتِقاطُ لا يَعودُ إلى «صَفقاتي»");
        foreach (var later in new[] { "payments.AuthorizeAsync(", "deals.BindListingOwnerAsync(", "gateway.OpenInvoiceAsync(", "notices.NotifyNewDealsAsync(" })
        {
            var at = submit.IndexOf(later, StringComparison.Ordinal);
            if (at < 0) v.Add($"{later} غائِب — الأَداةُ عَمياء");
            else if (caught > at || id > at) v.Add($"المُطالَبَةُ بَعدَ {later}");
        }
        // (‏2026-10-29، قَرارُ المالِك د8 (٢) — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه: صارَ لِتَعَذُّرِ الحَجزِ رُجوعٌ قَبلَ الفاتورَةِ يُجَدِّدُ السَلَّةَ بِالسَطرِ نَفسِه، فَالتَجديدُ
        // ورَمزُه يُطلَبانِ بَعدَ شَرطِ الفاتورَةِ لا في أَوَّلِ مَوضِع — وإلّا أَجابَ سَطرُ الرُجوعِ الآخَرِ عَن هذا. ورُجوعُ التَعَذُّرِ يُقاسُ في `DealCardWithoutPaymentTests`.)
        var failed = submit.IndexOf("if (opened is { Ok: false })", StringComparison.Ordinal);
        var touch = failed < 0 ? -1 : submit.IndexOf("cart.UpdatedAt = DateTime.UtcNow;", failed, StringComparison.Ordinal);
        var refused = touch < 0 ? -1 : submit.IndexOf("CheckoutPayMethods.GatewayOpenFailed", touch, StringComparison.Ordinal);
        if (failed < 0 || touch < failed || refused < touch) v.Add("رَدُّ فَتحِ الفاتورَةِ لا يُجَدِّدُ نُسخَةَ السَلَّةِ قَبلَ أَن يَعود");
        return v;
    }

    /// <summary>
    /// <para><b>قُلِبَ بِاسمِه</b> (‏2026-10-24؛ كانَ <c>Today_the_cart_is_read_first_each_deal_is_born_with_a_fresh_id_and_the_cart_is_deleted_last</c>،
    /// مُودَعٌ في <c>94a7ef0</c>): البابُ يُطالِبُ بِالسَلَّةِ بِوِلادَةِ بَندِها الأَوَّلِ قَبلَ أَيِّ أَثَر، و<c>StartAsync</c> يَأخُذُ المُعَرِّفَ، والتَعليقُ
    /// الكاذِبُ صُحِّحَ بِسَطرٍ مُؤَرَّخٍ تَحتَه. ويُطبَعُ ما فُحِص.</para>
    /// </summary>
    [Fact]
    public void The_submit_claims_the_cart_with_its_first_birth_before_any_effect()
    {
        var submit = Submit();
        var code = WriteEndpointGuardTests.StripComments(submit);
        var violations = ClaimViolations(code);
        Assert.True(violations.Count == 0, string.Join(" · ", violations));
        Assert.Contains("Id = id ?? Guid.NewGuid(),", Segment(Source(Template, "Services", "Deals", "DealsService.cs"),
            "public async Task<Deal> StartAsync(", "public async Task<Deal?> AssignCounterpartyAsync("), StringComparison.Ordinal);
        Assert.Contains("// idempotency-key بِـ dealId يَمنَع تَكرار الـ authorize", submit, StringComparison.Ordinal);
        Assert.Contains("لَم يَصدُق قَبلَ اليَوم — المُعَرِّفُ كانَ جَديداً في كُلِّ إرسال", submit, StringComparison.Ordinal);
        output.WriteLine($"فُحِصَ جِسمُ /checkout/submit ({code.Length} مِحرَفاً): مُعَرِّفُ المُطالَبَة، والتِقاطُها، وأَربَعَةُ آثارٍ بَعدَها، ورَدُّ الفاتورَة · مُخالَفات: {violations.Count}");
    }

    /// <summary><b>والفاحِصُ يُحمِرُ</b> (القاعِدَة ١٠): عَيِّنَةٌ بِشَكلِ الحَلقَةِ عِندَ <c>dace7cd</c> — وِلادَةٌ بِلا مُعَرِّفٍ ولا التِقاط — تُعطي مُخالَفاتِها
    /// بِأَسمائِها، وعَيِّنَةٌ تُطالِبُ بَعدَ الحَجزِ تُعطي «المُطالَبَةُ بَعدَ».</summary>
    [Fact]
    public void The_claim_checker_goes_red_on_the_shape_the_loop_had_before()
    {
        const string Before = "var deal = await deals.StartAsync(slug, pattern, amountSar: amount); payments.AuthorizeAsync(x); "
            + "deals.BindListingOwnerAsync(slug, deal.Id, oid); gateway.OpenInvoiceAsync(slug); if (opened is { Ok: false }) return X(CheckoutPayMethods.GatewayOpenFailed); "
            + "notices.NotifyNewDealsAsync(slug, born, l);";
        var before = ClaimViolations(Before);
        Assert.Contains("الوِلادَةُ بِلا مُعَرِّفِ المُطالَبَة", before);
        Assert.Contains("لا التِقاطَ لِتَعارُضِ البَندِ الأَوَّل", before);
        Assert.Contains("رَدُّ فَتحِ الفاتورَةِ لا يُجَدِّدُ نُسخَةَ السَلَّةِ قَبلَ أَن يَعود", before);

        const string Late = "payments.AuthorizeAsync(x); id: Services.Deals.CheckoutClaim.DealIdFor(slug, userId, cart.UpdatedAt, line++, item.ListingId) "
            + "catch (JasperFx.ConcurrencyException) when (created.Count == 0) { return Results.Redirect(Link(req, slug, \"deals\")); } "
            + "deals.BindListingOwnerAsync(slug); gateway.OpenInvoiceAsync(slug); if (opened is { Ok: false }) { cart.UpdatedAt = DateTime.UtcNow; CheckoutPayMethods.GatewayOpenFailed } "
            + "notices.NotifyNewDealsAsync(slug);";
        Assert.Equal(new[] { "المُطالَبَةُ بَعدَ payments.AuthorizeAsync(" }, ClaimViolations(Late));
        output.WriteLine($"عَيِّنَةُ ما قَبلُ: {before.Count} مُخالَفات · عَيِّنَةُ المُطالَبَةِ المُتَأَخِّرَة: 1");
    }
}

// ═══ البُرهانُ الحَيُّ — إرسالانِ مُتَزامِنانِ عَلى Postgres ═══════════════════════════════════════════════════════

/// <summary>
/// <para><b>المُطالَبَةُ عَلى المَخزَنِ الحَقيقِيّ</b> — مُبَوَّبٌ بِـ<c>WASAYEL_LIVE_PROOF=1</c> و<c>ConnectionStrings__Postgres</c> (قاعِدَةٌ مَحَلِّيَّة)،
/// بِتَركيبِ <see cref="LiveDealConcurrencyProofTests.BuildStore"/> نَفسِه. وِلادَتانِ مُتَزامِنَتانِ لِبَندِ السَلَّةِ نَفسِه بِنُسخَتِها نَفسِها — كَما يَلِدُهُما
/// إرسالانِ مُتَداخِلان — فَتُكتَبُ صَفقَةٌ واحِدَةٌ ويُرَدُّ الآخَرُ بِـ<c>JasperFx.ConcurrencyException</c>؛ وبِلا مُعَرِّفٍ (ما كانَ قَبلَ اليَوم) صَفقَتان.</para>
/// </summary>
[Collection(LiveProofCollection.Name)]
public class LiveCheckoutClaimProofTests(ITestOutputHelper output)
{
    private sealed class ForbiddenPaymentProvider : IPaymentProvider
    {
        private static InvalidOperationException Touched() => new("الوِلادَةُ لا تَمَسُّ المال — نودِيَ مُزَوِّدُ الدَفع.");
        public string ProviderName => "forbidden";
        public Task<PaymentResult> AuthorizeAsync(PaymentRequest req, string idempotencyKey, CancellationToken ct = default) => throw Touched();
        public Task<PaymentResult> CaptureAsync(string paymentId, decimal? amount = null, CancellationToken ct = default) => throw Touched();
        public Task<PaymentResult> RefundAsync(string paymentId, decimal amount, string reason, CancellationToken ct = default) => throw Touched();
        public Task<SubscriptionResult> CreateSubscriptionAsync(SubscriptionRequest req, string idempotencyKey, CancellationToken ct = default) => throw Touched();
        public Task<bool> CancelSubscriptionAsync(string subscriptionId, CancellationToken ct = default) => throw Touched();
        public Task<Invoice?> GetInvoiceAsync(string paymentId, CancellationToken ct = default) => throw Touched();
    }

    [Fact]
    public async Task Two_concurrent_births_of_one_cart_version_write_one_deal()
    {
        if (!LiveDealConcurrencyProofTests.Enabled || string.IsNullOrEmpty(LiveDealConcurrencyProofTests.ConnectionString)) return;

        await using var store = LiveDealConcurrencyProofTests.BuildStore();
        var slug = "zz-claim-" + Guid.NewGuid().ToString("N")[..8];
        var buyer = Guid.NewGuid();
        var listing = Guid.NewGuid();
        var version = DateTime.UtcNow;
        var deals = new DealsService(store, new ForbiddenPaymentProvider());

        async Task<string> Birth(Guid? id)
        {
            try
            {
                await deals.StartAsync(slug, "marketplace", buyer, "مُشتَرٍ", listing, "سِلعَة", 100m, id: id);
                return "ok";
            }
            catch (JasperFx.ConcurrencyException) { return "refused"; }
        }

        var claim = CheckoutClaim.DealIdFor(slug, buyer, version, 0, listing);
        var claimed = await Task.WhenAll(Birth(claim), Birth(claim));
        var unclaimed = await Task.WhenAll(Birth(null), Birth(null));

        await using var q = store.QuerySession(slug);
        var written = await q.Query<Deal>().CountAsync();
        output.WriteLine($"بِالمُطالَبَة: {string.Join(",", claimed.OrderBy(x => x))} · بِلاها: {string.Join(",", unclaimed)} · صَفَقاتٌ مَكتوبَة: {written}");
        Assert.Equal(new[] { "ok", "refused" }, claimed.OrderBy(x => x, StringComparer.Ordinal));
        Assert.Equal(new[] { "ok", "ok" }, unclaimed);
        Assert.Equal(3, written);   // واحِدَةٌ بِالمُطالَبَة + اثنَتانِ بِلاها
        Assert.NotNull(await q.LoadAsync<Deal>(claim));
    }
}
