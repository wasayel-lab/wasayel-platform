using System.Reflection;
using ACommerce.Kit.Payments;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using Marten;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ عَميلٌ يَنصَرِفُ بَعدَ المُطالَبَة — يَقَعُ أَثَرُ المالِ ولا تُكتَبُ نَتيجَتُه (تَدقيقُ الإطلاق ‏2026-10-24، lens-money-integrity-7؛ ‏ADR-036 §١٧) ═══
//
// **المَقيسُ عِندَ الرَأس** (`7bf8faa`، وهُوَ ما كانَ عِندَ `dace7cd`): نُقطَتا `/api/v1/deals/{id}/advance` و`/cancel` تُمَرِّرانِ `http.RequestAborted`
// إلى `AdvanceAsync`/`CancelAsync`، والخِدمَةُ تَحمِلُه بَعدَ المُطالَبَةِ (الحِفظُ المَفحوص، ‏ADR-036 §١١) إلى نِداءِ المُزَوِّدِ (`CaptureAsync`/`RefundAsync`)
// وإلى كِتابَةِ نَتيجَتِه (`RecordAsync`). فَعَميلٌ يَقطَعُ الاتِّصالَ وهُوَ يَنتَظِرُ الجَوابَ — بِإرادَتِه، أَو بِمُهلَةِ عَميلِه — يُحَوِّلُ نافِذَةَ §١١‑٥(ب)
// («عَمَلِيَّةٌ تَسقُطُ بَينَ نِداءِ المُزَوِّدِ وتَسجيلِ نَتيجَتِه») مِن عُطلٍ نادِرٍ إلى فِعلٍ يَقدِرُ عَلَيه حامِلُ المِفتاح. و`/ship` يَقولُ قاعِدَتَه بِحَرفِها
// («بَعدَ الأَثَرِ لا إلغاء» — `DealShipping.ShipAsync`) وأَبوابُ العُضوِ لا تُمَرِّرُ رَمزاً أَصلاً.
//
// **والأَداةُ** (القاعِدَة ١٠): `TokenHonouringStore` يَلُفُّ عالَمَ الوَثائِق — كُلُّ نِداءٍ عَلى المَخزَنِ أَو الجَلسَةِ يَحمِلُ رَمزَ إلغاءٍ مُلغىً يُرمى
// بِـ`OperationCanceledException` كَما يَرميه Marten/Npgsql — وصَفُّه يُثبِتُ أَنّه يَرمي ويَمُرّ؛ والصَفّانِ الحَيّانِ عَلى Postgres يَقيسانِ ما يَدَّعيه.
//
// **التَوصيفُ قَبلَ التَبديل** (القاعِدَة ٣): صُفوفُ «اليَوم» تُودَعُ وَحدَها، وتُقلَبُ بِأَسمائِها في كوميتِ الإصلاح.
//
// **ثُمَّ قُلِبَت الأَربَعَةُ بِأَسمائِها** (‏2026-10-24، ‏ADR-036 §١٧ — ولا يُعادُ حَرفٌ فَوقَه؛ التَوصيفُ `c385ae3`): ما يَلي المُطالَبَةَ في `DealsService` —
// `CaptureClaimedAsync` مِن `AdvanceAsync`، والاستِرجاعُ وسَطرُ «استِرجاعُه مُستَحَقّ» وكِتابَتاهُما في `CancelCoreAsync` — يَجري بِـ`CancellationToken.None`،
// فَعَميلٌ يَنصَرِفُ بَعدَ الحِفظِ المَفحوصِ لا يُسقِطُ نَتيجَةَ أَثَرٍ وَقَع. وصَفُّ الأَداةِ وصَفُّ الضَبطِ بِلا تَعديلِ حَرف.

public class DealMoneyEffectTokenTests(ITestOutputHelper output)
{
    internal const string Slug = "zz-money-token";
    internal static readonly Guid Buyer  = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
    internal static readonly Guid Seller = Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb");

    /// <summary>
    /// <para><b>مُزَوِّدٌ يُجيبُ ثُمَّ يَنصَرِفُ العَميل</b>: يَحمِلُ الرَمزَ عِندَ الدُخولِ كَما يَحمِلُه <c>HttpClient</c>، ثُمَّ يَقَعُ الأَثَرُ عِندَه (يُسَجَّلُ
    /// النِداء)، ثُمَّ يُلغى رَمزُ العَميلِ قَبلَ أَن يَعودَ الجَواب — أَي انقَطَعَ الطالِبُ والمالُ قَد تَحَرَّك. و<see cref="Leaves"/> <c>false</c> صَفُّ الضَبط.</para>
    /// </summary>
    internal sealed class LeavingClientProvider(CancellationTokenSource client) : IPaymentProvider
    {
        public List<string> Calls { get; } = new();
        public bool Leaves { get; init; } = true;
        public string ProviderName => "leaving-client";

        public Task<PaymentResult> CaptureAsync(string paymentId, decimal? amount = null, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            Calls.Add($"capture {paymentId}");
            if (Leaves) client.Cancel();
            return Task.FromResult(new PaymentResult(paymentId, PaymentStatus.Captured, amount ?? 0m));
        }

        public Task<PaymentResult> RefundAsync(string paymentId, decimal amount, string reason, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            Calls.Add($"refund {paymentId}");
            if (Leaves) client.Cancel();
            return Task.FromResult(new PaymentResult($"ref_{paymentId}", PaymentStatus.Refunded, amount));
        }

        private static InvalidOperationException Untouched() => new("لا حَجزَ ولا اشتِراكَ في مَسارِ القَبضِ والاستِرجاع.");
        public Task<PaymentResult> AuthorizeAsync(PaymentRequest req, string idempotencyKey, CancellationToken ct = default) => throw Untouched();
        public Task<SubscriptionResult> CreateSubscriptionAsync(SubscriptionRequest req, string idempotencyKey, CancellationToken ct = default) => throw Untouched();
        public Task<bool> CancelSubscriptionAsync(string subscriptionId, CancellationToken ct = default) => throw Untouched();
        public Task<Invoice?> GetInvoiceAsync(string paymentId, CancellationToken ct = default) => throw Untouched();
    }

    /// <summary>
    /// <para><b>عالَمُ الوَثائِقِ بِرَمزِ إلغاءٍ يُحتَرَم</b>: <c>MartenProxy</c> يَتَجاهَلُ الرَمز، وMarten لا يَتَجاهَلُه — فَهذا الغِلافُ يَرمي
    /// <see cref="OperationCanceledException"/> لِكُلِّ نِداءٍ عَلى المَخزَنِ أَو جَلسَتِه يَحمِلُ رَمزاً مُلغىً، ويُحيلُ ما سِواه إلى العالَمِ بِحَرفِه.
    /// <see cref="_refused"/> يَعُدُّ ما رَدّ — فَـ«رُدَّ نِداء» تُقاس.</para>
    /// </summary>
    internal class TokenHonouringStore : DispatchProxy
    {
        private object _inner = null!;
        private int[] _refused = null!;

        public static T Wrap<T>(T inner, int[] refused) where T : class
        {
            var proxy = Create<T, TokenHonouringStore>();
            var self = (TokenHonouringStore)(object)proxy;
            self._inner = inner;
            self._refused = refused;
            return proxy;
        }

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            foreach (var a in args ?? Array.Empty<object?>())
                if (a is CancellationToken { IsCancellationRequested: true } t)
                {
                    _refused[0]++;
                    throw new OperationCanceledException(t);
                }
            var result = method!.Invoke(_inner, args);
            if (result is not null && method.ReturnType is { IsInterface: true } rt
                && (rt == typeof(IDocumentSession) || rt == typeof(IQuerySession)))
                return typeof(TokenHonouringStore).GetMethod(nameof(Wrap))!.MakeGenericMethod(rt).Invoke(null, new[] { result, _refused });
            return result;
        }
    }

    internal static Deal CardDeal(DealStage stage, string paymentStatus)
    {
        var deal = new Deal
        {
            Id = Guid.NewGuid(), TenantSlug = Slug, Pattern = "marketplace", Stage = stage, AmountSar = 100,
            ListingTitle = "سِلعَة", InitiatorId = Buyer, InitiatorName = "مُشتَرٍ", CounterpartyId = Seller, CounterpartyName = "بائِع",
        };
        deal.Attributes[CheckoutPayMethods.AttributeKey] = CheckoutPayMethods.Card;
        deal.Refs["payment_id"] = "pay_1";
        deal.Refs["payment_status"] = paymentStatus;
        return deal;
    }

    private static (DocWorld World, DealsService Deals, LeavingClientProvider Pay, CancellationTokenSource Client, int[] Refused, Guid Id) World(
        DealStage stage, string paymentStatus, bool leaves = true)
    {
        var world = new DocWorld();
        var client = new CancellationTokenSource();
        var pay = new LeavingClientProvider(client) { Leaves = leaves };
        var refused = new int[1];
        var deals = new DealsService(TokenHonouringStore.Wrap(world.Store, refused), pay);
        var deal = CardDeal(stage, paymentStatus);
        world.Put(deal);
        return (world, deals, pay, client, refused, deal.Id);
    }

    private static Exception? Thrown<T>(Task<T> task)
    {
        Assert.True(task.IsCompleted, "نِداءٌ لَم يَكتَمِل في عالَمٍ مُتَزامِن — الأَداةُ تَكذِب.");
        try { task.GetAwaiter().GetResult(); return null; }
        catch (Exception ex) { return ex; }
    }

    private static string Lines(Deal d) => string.Join(",", d.Timeline.Select(e => e.Action));

    /// <summary>
    /// <para><b>الأَداةُ مَقيسَةٌ قَبلَ أَن يوثَقَ بِها</b> (القاعِدَة ١٠): رَمزٌ مُلغىً يُرَدُّ عِندَ القِراءَةِ ويُعَدّ، ورَمزٌ حَيٌّ يَمُرُّ إلى العالَم؛ وصَفُّ
    /// الضَبطِ — العَميلُ لا يَنصَرِف — يَقبِضُ ويُسَجِّلُ بِالغِلافِ نَفسِه، فَما يَسقُطُ في صَفَّي «اليَوم» سَبَبُه الرَمزُ لا الغِلاف.</para>
    /// </summary>
    [Fact]
    public void The_wrapper_refuses_a_cancelled_token_passes_a_live_one_and_the_control_records()
    {
        var world = new DocWorld();
        var refused = new int[1];
        var store = TokenHonouringStore.Wrap(world.Store, refused);
        var deal = CardDeal(DealStage.Confirmed, nameof(PaymentStatus.Authorized));
        world.Put(deal);
        using var gone = new CancellationTokenSource();
        gone.Cancel();
        using (var s = store.LightweightSession(Slug))
        {
            Assert.Same(deal, s.LoadAsync<Deal>(deal.Id, CancellationToken.None).GetAwaiter().GetResult());
            Assert.Throws<OperationCanceledException>(() => s.LoadAsync<Deal>(deal.Id, gone.Token).GetAwaiter().GetResult());
            Assert.Throws<OperationCanceledException>(() => s.SaveChangesAsync(gone.Token).GetAwaiter().GetResult());
        }
        Assert.Equal(2, refused[0]);

        var (cw, deals, pay, client, cRefused, id) = World(DealStage.Confirmed, nameof(PaymentStatus.Authorized), leaves: false);
        var res = deals.AdvanceAsync(Slug, id, Buyer, "مُشتَرٍ", null, client.Token).GetAwaiter().GetResult();
        Assert.True(res.Ok);
        var stored = cw.Read<Deal>(id.ToString())!;
        Assert.Equal(nameof(PaymentStatus.Captured), stored.Refs["payment_status"]);
        Assert.Contains(stored.Timeline, e => e.Action == "captured");
        Assert.Equal(0, cRefused[0]);
        output.WriteLine($"الغِلاف: رُدَّ {refused[0]} مِن 3 (القِراءَةُ الحَيَّةُ مَرَّت) · الضَبط: {stored.Stage} payment_status={stored.Refs["payment_status"]} · {Lines(stored)} · المُزَوِّد: [{string.Join(", ", pay.Calls)}]");
    }

    /// <summary>
    /// <para><b>قُلِبَ بِاسمِه</b> (‏2026-10-24، ‏ADR-036 §١٧): كانَ <c>Today_a_client_that_leaves_while_the_capture_answers_leaves_Paid_captured_and_unrecorded</c>
    /// (<c>c385ae3</c>) — <see cref="OperationCanceledException"/> والصَفقَةُ «دَفع» بِـ<c>payment_status=Authorized</c> بِلا سَطرِ <c>captured</c>. <b>وصارَ</b>:
    /// يَنصَرِفُ العَميلُ والمُزَوِّدُ يُجيب، فَيُكتَبُ القَبضُ كَما يُكتَبُ لِعَميلٍ باقٍ — <c>Captured</c> وسَطرُ <c>captured</c> — وصِفرُ نِداءٍ مَردود.</para>
    /// </summary>
    [Fact]
    public void A_client_that_leaves_while_the_capture_answers_still_has_the_capture_recorded()
    {
        var (world, deals, pay, client, refused, id) = World(DealStage.Confirmed, nameof(PaymentStatus.Authorized));

        var thrown = Thrown(deals.AdvanceAsync(Slug, id, Buyer, "مُشتَرٍ", null, client.Token));

        Assert.Null(thrown);
        Assert.True(client.IsCancellationRequested);
        Assert.Equal(new[] { "capture pay_1" }, pay.Calls);
        var stored = world.Read<Deal>(id.ToString())!;
        Assert.Equal(DealStage.Paid, stored.Stage);
        Assert.Equal(nameof(PaymentStatus.Captured), stored.Refs["payment_status"]);
        Assert.Single(stored.Timeline, e => e.Action == "captured");
        Assert.Equal(0, refused[0]);
        output.WriteLine($"القَبض: {thrown?.GetType().Name ?? "لا استِثناء"} · {stored.Stage} payment_status={stored.Refs["payment_status"]} · {Lines(stored)} · المُزَوِّد: [{string.Join(", ", pay.Calls)}] · رُدَّ {refused[0]}");
    }

    /// <summary>
    /// <para><b>قُلِبَ بِاسمِه</b> (‏2026-10-24، ‏ADR-036 §١٧): كانَ <c>Today_a_client_that_leaves_while_the_refund_answers_leaves_Cancelled_refunded_and_unrecorded</c>
    /// (<c>c385ae3</c>) — مُلغاةٌ مُستَرجَعَةٌ بِلا <c>refund_id</c> ولا سَطرِ <c>refunded</c>. <b>وصارَ</b>: يُكتَبُ الاستِرجاعُ (<c>ref_pay_1</c>، <c>Refunded</c>،
    /// وسَطرُ <c>refunded</c> بَعدَ <c>cancelled</c>) ولَو انصَرَفَ العَميل، وصِفرُ نِداءٍ مَردود.</para>
    /// </summary>
    [Fact]
    public void A_client_that_leaves_while_the_refund_answers_still_has_the_refund_recorded()
    {
        var (world, deals, pay, client, refused, id) = World(DealStage.Paid, nameof(PaymentStatus.Captured));

        var thrown = Thrown(deals.CancelAsync(Slug, id, new DealCanceller(Buyer, "مُشتَرٍ", IsStoreAdmin: false), "عَدَلتُ", client.Token));

        Assert.Null(thrown);
        Assert.True(client.IsCancellationRequested);
        Assert.Equal(new[] { "refund pay_1" }, pay.Calls);
        var stored = world.Read<Deal>(id.ToString())!;
        Assert.Equal(DealStatus.Cancelled, stored.Status);
        Assert.Equal("ref_pay_1", stored.Refs["refund_id"]);
        Assert.Equal(nameof(PaymentStatus.Refunded), stored.Refs["refund_status"]);
        Assert.Equal(new[] { "cancelled", "refunded" }, stored.Timeline.Select(e => e.Action).Where(a => a is "cancelled" or "refunded"));
        Assert.Equal(0, refused[0]);
        output.WriteLine($"الاستِرجاع: {thrown?.GetType().Name ?? "لا استِثناء"} · {stored.Status}/{stored.Stage} refund_id={(stored.Refs.ContainsKey("refund_id") ? stored.Refs["refund_id"] : "—")} · {Lines(stored)} · المُزَوِّد: [{string.Join(", ", pay.Calls)}] · رُدَّ {refused[0]}");
    }
}

/// <summary>
/// <para><b>الصَفّانِ عَلى Postgres المَحَلِّيّ</b> — Marten الحَقيقِيُّ يَحتَرِمُ الرَمز، فَما يَدَّعيه الغِلافُ أَعلاه يُقاسُ هُنا بِالمَخزَنِ الَّذي يُشَغِّلُه المُضيف
/// (<see cref="LiveDealConcurrencyProofTests.BuildStore"/>). مُبَوَّبانِ بِـWASAYEL_LIVE_PROOF=1 وConnectionStrings__Postgres.</para>
/// </summary>
[Collection(LiveProofCollection.Name)]
public class LiveDealMoneyEffectTokenProofTests(ITestOutputHelper output)
{
    private static async Task<(DocumentStore Store, string Slug, Guid Id)> SeedAsync(DealStage stage, string paymentStatus)
    {
        var store = LiveDealConcurrencyProofTests.BuildStore();
        var slug = "zz-token-" + Guid.NewGuid().ToString("N")[..8];
        var deal = DealMoneyEffectTokenTests.CardDeal(stage, paymentStatus);
        deal.TenantSlug = slug;
        await using (var s = store.LightweightSession(slug))
        {
            s.Store(deal);
            await s.SaveChangesAsync();
        }
        return (store, slug, deal.Id);
    }

    private static async Task<Deal> ReadAsync(DocumentStore store, string slug, Guid id)
    {
        await using var q = store.QuerySession(slug);
        return (await q.LoadAsync<Deal>(id))!;
    }

    /// <summary><b>قُلِبَ بِاسمِه</b> (‏2026-10-24، ‏ADR-036 §١٧): كانَ <c>Today_live_a_client_that_leaves_while_the_capture_answers_leaves_Paid_captured_and_unrecorded</c>
    /// (<c>c385ae3</c>). بِحَرفِ صَفِّ عالَمِ الوَثائِقِ المَقلوب.</summary>
    [Fact]
    public async Task Live_a_client_that_leaves_while_the_capture_answers_still_has_the_capture_recorded()
    {
        if (!LiveDealConcurrencyProofTests.Enabled || string.IsNullOrEmpty(LiveDealConcurrencyProofTests.ConnectionString)) return;
        var (store, slug, id) = await SeedAsync(DealStage.Confirmed, nameof(PaymentStatus.Authorized));
        await using var _ = store;
        using var client = new CancellationTokenSource();
        var pay = new DealMoneyEffectTokenTests.LeavingClientProvider(client);
        var deals = new DealsService(store, pay);

        Exception? thrown = null;
        try { await deals.AdvanceAsync(slug, id, DealMoneyEffectTokenTests.Buyer, "مُشتَرٍ", null, client.Token); }
        catch (Exception ex) { thrown = ex; }

        var stored = await ReadAsync(store, slug, id);
        output.WriteLine($"حَيّاً — القَبض: {thrown?.GetType().Name ?? "لا استِثناء"} · {stored.Stage} payment_status={stored.Refs["payment_status"]} · {string.Join(",", stored.Timeline.Select(e => e.Action))} · المُزَوِّد: [{string.Join(", ", pay.Calls)}]");
        Assert.Null(thrown);
        Assert.Equal(new[] { "capture pay_1" }, pay.Calls);
        Assert.Equal(DealStage.Paid, stored.Stage);
        Assert.Equal(nameof(PaymentStatus.Captured), stored.Refs["payment_status"]);
        Assert.Single(stored.Timeline, e => e.Action == "captured");
    }

    /// <summary><b>قُلِبَ بِاسمِه</b> (‏2026-10-24، ‏ADR-036 §١٧): كانَ <c>Today_live_a_client_that_leaves_while_the_refund_answers_leaves_Cancelled_refunded_and_unrecorded</c>
    /// (<c>c385ae3</c>). بِحَرفِ صَفِّ عالَمِ الوَثائِقِ المَقلوب.</summary>
    [Fact]
    public async Task Live_a_client_that_leaves_while_the_refund_answers_still_has_the_refund_recorded()
    {
        if (!LiveDealConcurrencyProofTests.Enabled || string.IsNullOrEmpty(LiveDealConcurrencyProofTests.ConnectionString)) return;
        var (store, slug, id) = await SeedAsync(DealStage.Paid, nameof(PaymentStatus.Captured));
        await using var _ = store;
        using var client = new CancellationTokenSource();
        var pay = new DealMoneyEffectTokenTests.LeavingClientProvider(client);
        var deals = new DealsService(store, pay);

        Exception? thrown = null;
        try { await deals.CancelAsync(slug, id, new DealCanceller(DealMoneyEffectTokenTests.Buyer, "مُشتَرٍ", IsStoreAdmin: false), "عَدَلتُ", client.Token); }
        catch (Exception ex) { thrown = ex; }

        var stored = await ReadAsync(store, slug, id);
        output.WriteLine($"حَيّاً — الاستِرجاع: {thrown?.GetType().Name ?? "لا استِثناء"} · {stored.Status}/{stored.Stage} refund_id={(stored.Refs.ContainsKey("refund_id") ? stored.Refs["refund_id"] : "—")} · {string.Join(",", stored.Timeline.Select(e => e.Action))} · المُزَوِّد: [{string.Join(", ", pay.Calls)}]");
        Assert.Null(thrown);
        Assert.Equal(new[] { "refund pay_1" }, pay.Calls);
        Assert.Equal(DealStatus.Cancelled, stored.Status);
        Assert.Equal("ref_pay_1", stored.Refs["refund_id"]);
        Assert.Single(stored.Timeline, e => e.Action == "refunded");
    }
}
