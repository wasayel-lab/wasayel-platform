using System.Reflection;
using System.Runtime.ExceptionServices;
using ACommerce.Kit.Payments;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using Marten;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ كِتابَتانِ عَلى صَفقَةٍ واحِدَة — الأَخيرَةُ تَمحو الأولى؟ (‏ADR-036) ═══════════════════════
//
// **المَقيسُ قَبلَ هذا المِلَفّ** (‏ADR-033 §٦‑ب‑٢، مِجَسٌّ خارِجَ المُستَودَعِ عَلى Postgres المَحَلِّيّ، ‏100 مُحاوَلَةٍ
// مُتَزامِنَةٍ لِكُلِّ مَشهَد): إلغاءُ المُشتَري يَضيعُ كُلُّه في 84/100 قَبلَ إصلاحِ `7bceeb7` و70–76/100 بَعدَه، وقَبولانِ
// مُتَداخِلانِ يُرجِعانِ `Accepted` مَرَّتَين 99/100 — لِأَنّ `UseOptimisticConcurrency` صِفرُ مَوضِعٍ في `libs/` و`apps/`،
// فَحِفظُ وَثيقَةٍ قُرِئَت قَبلَ كِتابَةِ غَيرِها يَكتُبُ فَوقَها بِلا سُؤال.
//
// **ولِماذا بَوّابَةٌ لا حَظّ**: السِباقُ بِمِئَةِ مُحاوَلَةٍ يَقيسُ نِسبَةً تَتَغَيَّرُ بِالآلَة؛ والمَطلوبُ هُنا حُكمٌ لا نِسبَة.
// فَيُغَلَّفُ مَخزَنُ Marten الحَقيقِيُّ بِوَكيلٍ (<see cref="SaveGate"/>) يُمَرِّرُ كُلَّ نِداءٍ كَما هُوَ ويُوقِفُ
// `SaveChangesAsync` لِنِداءٍ واحِدٍ مُعَلَّمٍ حَتّى يُؤذَن لَه — فَتُقرَأُ الصَفقَةُ في كِتابَةِ الإلغاءِ، ثُمَّ يَتِمُّ القَبولُ كُلُّه
// ويُحفَظ، ثُمَّ يُستَأنَفُ حِفظُ الإلغاء. تَرتيبٌ حَتمِيٌّ في كُلِّ تَشغيل، والخِدمَةُ هي هي (`DealsService` الحَقيقِيَّة).
//
// **والبُرهانُ مُبَوَّبٌ** بِـWASAYEL_LIVE_PROOF=1 وConnectionStrings__Postgres، بِعَقدِ `LiveDealAcceptSequenceProofTests` حَرفاً.

/// <summary>
/// <para><b>مَخزَنُ Marten حَقيقِيٌّ بِبَوّابَةٍ واحِدَةٍ قَبلَ الحِفظ.</b> كُلُّ عُضوٍ يُمَرَّرُ إلى الهَدَفِ كَما هُوَ،
/// والجَلسَةُ الخَفيفَةُ تُغَلَّفُ بِالوَكيلِ نَفسِه؛ و<c>SaveChangesAsync</c> وَحدَه يَقِفُ — مَرَّةً واحِدَة — إن نودِيَ
/// مِن تَدَفُّقٍ مُعَلَّمٍ (<see cref="Gate.Marked"/>، ‏<c>AsyncLocal</c> لا يَعبُرُ إلى تَدَفُّقٍ آخَر).</para>
/// </summary>
internal class SaveGate : DispatchProxy
{
    private object _target = null!;
    private Gate _gate = null!;

    internal sealed class Gate
    {
        public readonly AsyncLocal<bool> Marked = new();
        public readonly TaskCompletionSource Reached = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Armed = 1;
    }

    public static IDocumentStore Wrap(IDocumentStore store, Gate gate) => Proxy<IDocumentStore>(store, gate);

    private static T Proxy<T>(object target, Gate gate) where T : class
    {
        var proxy = Create<T, SaveGate>();
        var self = (SaveGate)(object)proxy;
        self._target = target;
        self._gate = gate;
        return proxy;
    }

    protected override object? Invoke(MethodInfo? method, object?[]? args)
    {
        var name = method!.Name;
        if (name == "SaveChangesAsync" && _gate.Marked.Value && Interlocked.Exchange(ref _gate.Armed, 0) == 1)
            return HeldSave((CancellationToken)(args?[0] ?? CancellationToken.None));

        object? result;
        try { result = method.Invoke(_target, args); }
        catch (TargetInvocationException e) { ExceptionDispatchInfo.Capture(e.InnerException!).Throw(); throw; }

        return name == "LightweightSession" && result is IDocumentSession session
            ? Proxy<IDocumentSession>(session, _gate)
            : result;
    }

    private async Task HeldSave(CancellationToken ct)
    {
        _gate.Reached.TrySetResult();
        await _gate.Release.Task;
        await ((IDocumentSession)_target).SaveChangesAsync(ct);
    }
}

public class LiveDealConcurrencyProofTests(ITestOutputHelper output)
{
    // ‏`internal` لا `private` (‏2026-10-12، ‏ADR-036 الخُطوَة 3): صَفّا وارِدِ OTO الحَيّانِ (`LiveOtoOrderStatusProofTests`)
    // يُرَكِّبانِ المَخزَنَ نَفسَه بِالبَوّابَةِ نَفسِها — تَركيبٌ ثانٍ يَنجَرِفُ عَمّا يُشَغِّلُه المُضيف.
    internal static bool Enabled =>
        Environment.GetEnvironmentVariable("WASAYEL_LIVE_PROOF") == "1";

    internal static string? ConnectionString =>
        Environment.GetEnvironmentVariable("ConnectionStrings__Postgres");

    private static string ResilientConnection =>
        ConnectionString!.TrimEnd(';') + ";Timeout=60;Command Timeout=120";

    /// <summary>تَركيبُ المَخزَنِ كَما يُرَكِّبُه المُضيف — <b>ووَثيقَةُ <see cref="Deal"/> بِالدالَّةِ الَّتي يُناديها تَركيبُ
    /// القالِب</b> (<see cref="DealsService.RegisterDocument"/>، ‏ADR-036): فَما يُقاسُ هُنا هُوَ ما يُشَغَّل. (كانَ بِلا تَسجيلٍ
    /// يَومَ وُلِدَ المِلَفُّ في <c>bf01f30</c> — ذاكَ ما كانَ يُشَغَّل.)</summary>
    internal static DocumentStore BuildStore() => DocumentStore.For(o =>
    {
        o.Connection(ResilientConnection);
        o.DatabaseSchemaName = "platform";
        o.Policies.AllDocumentsAreMultiTenanted();
        o.Events.TenancyStyle = global::JasperFx.MultiTenancy.TenancyStyle.Conjoined;
        o.Schema.For<ACommerce.Kit.Tenants.Tenant>().SingleTenanted().Identity(x => x.Id);
        DealsService.RegisterDocument(o);
        o.AutoCreateSchemaObjects = JasperFx.AutoCreate.All;
    });

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

    /// <summary><b>قُلِبَ بِاسمِه</b> (‏ADR-036، ‏2026-10-12): كانَ
    /// <c>Today_a_cancel_saved_after_an_accept_overwrites_it_and_both_report_success</c> — الإلغاءُ قَرَأَ الصَفقَةَ قَبلَ القَبولِ
    /// وحَفِظَ بَعدَه فَكَتَبَ فَوقَه ونَجَحَ الاثنان (مَقيسٌ في <c>bf01f30</c>: «Cancelled/Offered · سِجِلّ: created,note,bound,
    /// cancelled»). <b>وصارَ جِلسَتانِ قَرَأَتا الصَفقَةَ نَفسَها وحَفِظَتا: نَجاحٌ واحِدٌ و<c>deal_changed_meanwhile</c> واحِد</b> —
    /// القَبولُ قائِمٌ (<c>Booked</c> بِسَطرِ <c>advanced</c>)، والإلغاءُ لَم يُكتَب؛ وإعادَتُه تَقرَأُ ما كُتِبَ فَتَمضي.</summary>
    [Fact]
    public async Task A_cancel_saved_after_an_accept_is_refused_deal_changed_meanwhile_and_the_accept_stands()
    {
        if (!Enabled || string.IsNullOrEmpty(ConnectionString)) return;

        await using var store = BuildStore();
        var slug = "zz-occ-" + Guid.NewGuid().ToString("N")[..8];
        var buyer = Guid.NewGuid();
        var seller = Guid.NewGuid();

        try
        {
            var plain = new DealsService(store, new ForbiddenPaymentProvider());
            var born = await plain.StartAsync(slug, "marketplace", buyer, "مُشتَرٍ", null, "سِلعَة", 150m,
                new() { [CheckoutPayMethods.AttributeKey] = CheckoutPayMethods.CashOnDelivery });
            await plain.AttachRefAsync(slug, born.Id, DealsService.ListingOwnerRef, seller.ToString());
            await plain.BindListingOwnerAsync(slug, born.Id, seller.ToString());

            var gate = new SaveGate.Gate();
            var deals = new DealsService(SaveGate.Wrap(store, gate), new ForbiddenPaymentProvider());

            var cancelling = Task.Run(async () =>
            {
                gate.Marked.Value = true;
                return await deals.CancelAsync(slug, born.Id, new DealCanceller(buyer, "مُشتَرٍ", IsStoreAdmin: false), "عَدَلتُ");
            });
            await gate.Reached.Task.WaitAsync(TimeSpan.FromSeconds(60));

            var accept = await deals.AcceptAsync(slug, born.Id, new DealAcceptor(seller, "بائِع"));
            gate.Release.TrySetResult();
            var cancel = await cancelling.WaitAsync(TimeSpan.FromSeconds(60));

            var final = (await plain.LoadAsync(slug, born.Id))!;
            output.WriteLine($"قَبول: ok={accept.Ok} · إلغاء: ok={cancel.Ok} code={cancel.Violation?.Code} · النِهائِيَّة: " +
                             $"{final.Status}/{final.Stage} · سِجِلّ: {string.Join(",", final.Timeline.Select(e => e.Action))}");

            Assert.Equal(1, new[] { accept.Ok, cancel.Ok }.Count(ok => ok));
            Assert.True(accept.Ok, accept.Violation?.MessageAr);
            Assert.False(cancel.Ok);
            Assert.Equal(DealsService.ChangedMeanwhile, cancel.Violation!.Code);
            Assert.Equal(DealStatus.Active, cancel.Deal!.Status);          // ما في المَخزَنِ بَعدَ التَعارُض
            Assert.Equal(DealStatus.Active, final.Status);
            Assert.Equal(DealStage.Booked, final.Stage);
            Assert.Single(final.Timeline, e => e.Action == "advanced");
            Assert.DoesNotContain(final.Timeline, e => e.Action == "cancelled");

            // «أَعِد المُحاوَلَة»: الإعادَةُ تَقرَأُ ما كُتِبَ فَتَمضي — إلغاءٌ عِندَ `Booked`.
            var retry = await plain.CancelAsync(slug, born.Id, new DealCanceller(buyer, "مُشتَرٍ", IsStoreAdmin: false), "عَدَلتُ");
            Assert.True(retry.Ok, retry.Violation?.MessageAr);
            Assert.Equal((DealStatus.Cancelled, DealStage.Booked), (retry.Deal!.Status, retry.Deal.Stage));
        }
        finally
        {
            await using var s = store.LightweightSession(slug);
            foreach (var d in await s.Query<Deal>().ToListAsync()) s.Delete(d);
            await s.SaveChangesAsync();
        }
    }

    /// <summary><b>قَبولانِ مُتَداخِلانِ مِن البائِعِ نَفسِه</b> (نَقرَتانِ؛ ‏ADR-033 §٦‑ب‑٢ قاسَ «‏`Accepted` مَرَّتَين 99/100» فَإشعارَين):
    /// الأَوَّلُ يَقرَأُ ويَقِفُ قَبلَ الحِفظ، والثاني يَتِمّ، ثُمَّ يُستَأنَفُ الأَوَّل — <b>قَبولٌ واحِدٌ و<c>deal_changed_meanwhile</c>
    /// واحِد</b>، وسَطرُ <c>advanced</c> واحِدٌ في السِجِلّ.</summary>
    [Fact]
    public async Task Two_overlapping_accepts_yield_one_acceptance_and_one_deal_changed_meanwhile()
    {
        if (!Enabled || string.IsNullOrEmpty(ConnectionString)) return;

        await using var store = BuildStore();
        var slug = "zz-occ-" + Guid.NewGuid().ToString("N")[..8];
        var buyer = Guid.NewGuid();
        var seller = Guid.NewGuid();

        try
        {
            var plain = new DealsService(store, new ForbiddenPaymentProvider());
            var born = await plain.StartAsync(slug, "marketplace", buyer, "مُشتَرٍ", null, "سِلعَة", 150m,
                new() { [CheckoutPayMethods.AttributeKey] = CheckoutPayMethods.CashOnDelivery });
            await plain.AttachRefAsync(slug, born.Id, DealsService.ListingOwnerRef, seller.ToString());
            await plain.BindListingOwnerAsync(slug, born.Id, seller.ToString());

            var gate = new SaveGate.Gate();
            var deals = new DealsService(SaveGate.Wrap(store, gate), new ForbiddenPaymentProvider());

            var first = Task.Run(async () =>
            {
                gate.Marked.Value = true;
                return await deals.AcceptAsync(slug, born.Id, new DealAcceptor(seller, "بائِع"));
            });
            await gate.Reached.Task.WaitAsync(TimeSpan.FromSeconds(60));
            var second = await deals.AcceptAsync(slug, born.Id, new DealAcceptor(seller, "بائِع"));
            gate.Release.TrySetResult();
            var held = await first.WaitAsync(TimeSpan.FromSeconds(60));

            var final = (await plain.LoadAsync(slug, born.Id))!;
            output.WriteLine($"الثاني: ok={second.Ok} · الأَوَّل: ok={held.Ok} code={held.Violation?.Code} · النِهائِيَّة: " +
                             $"{final.Status}/{final.Stage} · advanced={final.Timeline.Count(e => e.Action == "advanced")}");

            Assert.True(second.Ok, second.Violation?.MessageAr);
            Assert.False(held.Ok);
            Assert.Equal(DealsService.ChangedMeanwhile, held.Violation!.Code);
            Assert.Equal(DealStage.Booked, final.Stage);
            Assert.Single(final.Timeline, e => e.Action == "advanced");
        }
        finally
        {
            await using var s = store.LightweightSession(slug);
            foreach (var d in await s.Query<Deal>().ToListAsync()) s.Delete(d);
            await s.SaveChangesAsync();
        }
    }

    /// <summary><b>قُلِبَ بِاسمِه</b> (‏ADR-036 §٧، ‏2026-10-12): كانَ
    /// <c>Today_an_attach_held_past_an_accept_returns_the_stored_deal_without_its_ref_and_the_ref_is_lost</c> (مُودَعٌ في <c>f9f9711</c>،
    /// مَقيسٌ ثَلاثاً: «قَبول: ok=True · إلحاق: — · النِهائِيَّة: Active/Booked · سِجِلّ: created,note,bound,advanced»). <b>وصارَ</b>: الإلحاقُ —
    /// مَرجِعُ إذنِ دَفعٍ كَما يُلحِقُه <c>/checkout/submit</c> — يَقرَأُ الصَفقَةَ ويَقِفُ قَبلَ الحِفظ، ثُمَّ يَتِمُّ قَبولُ البائِعِ المَربوطِ
    /// ويُحفَظ، ثُمَّ يُستَأنَف: يَرُدُّه Marten، فَيُعادُ عَلى ما كُتِب — <b>يَبقى الاثنان</b>: <c>Booked</c> بِسَطرِ <c>advanced</c> واحِد،
    /// والمَرجِعُ عَلَيها بِسَطرِ إلحاقٍ واحِد (الإعادَةُ تَقرَأُ نُسخَةً لا تَحمِلُ المُحاوَلَةَ المَردودَة).</summary>
    [Fact]
    public async Task An_attach_held_past_an_accept_is_re_applied_and_both_writes_stand()
    {
        if (!Enabled || string.IsNullOrEmpty(ConnectionString)) return;

        await using var store = BuildStore();
        var slug = "zz-occ-" + Guid.NewGuid().ToString("N")[..8];
        var buyer = Guid.NewGuid();
        var seller = Guid.NewGuid();

        try
        {
            var plain = new DealsService(store, new ForbiddenPaymentProvider());
            var born = await plain.StartAsync(slug, "marketplace", buyer, "مُشتَرٍ", null, "سِلعَة", 150m,
                new() { [CheckoutPayMethods.AttributeKey] = CheckoutPayMethods.CashOnDelivery });
            await plain.AttachRefAsync(slug, born.Id, DealsService.ListingOwnerRef, seller.ToString());
            await plain.BindListingOwnerAsync(slug, born.Id, seller.ToString());

            var gate = new SaveGate.Gate();
            var deals = new DealsService(SaveGate.Wrap(store, gate), new ForbiddenPaymentProvider());

            var attaching = Task.Run(async () =>
            {
                gate.Marked.Value = true;
                return await deals.AttachRefAsync(slug, born.Id, "payment_id", "auth_live");
            });
            await gate.Reached.Task.WaitAsync(TimeSpan.FromSeconds(60));

            var accept = await deals.AcceptAsync(slug, born.Id, new DealAcceptor(seller, "بائِع"));
            gate.Release.TrySetResult();
            var attached = await attaching.WaitAsync(TimeSpan.FromSeconds(60));

            var final = (await plain.LoadAsync(slug, born.Id))!;
            output.WriteLine($"قَبول: ok={accept.Ok} · إلحاق: {attached?.Refs.GetValueOrDefault("payment_id")} · النِهائِيَّة: " +
                             $"{final.Status}/{final.Stage} · سِجِلّ: {string.Join(",", final.Timeline.Select(e => e.Action))}");

            Assert.True(accept.Ok, accept.Violation?.MessageAr);
            Assert.Equal("auth_live", attached!.Refs["payment_id"]);
            Assert.Equal((DealStatus.Active, DealStage.Booked), (final.Status, final.Stage));
            Assert.Equal("auth_live", final.Refs["payment_id"]);
            Assert.Single(final.Timeline, e => e.Action == "advanced");
            Assert.Single(final.Timeline, e => e.Action == "note" && e.Note == "attach payment_id=auth_live");
        }
        finally
        {
            await using var s = store.LightweightSession(slug);
            foreach (var d in await s.Query<Deal>().ToListAsync()) s.Delete(d);
            await s.SaveChangesAsync();
        }
    }
}

// ─── بِلا قاعِدَة: كُلُّ بابِ كِتابَةٍ يُحَوِّلُ التَعارُضَ رَمزاً لا ‏500 (‏ADR-036) ─────────────────────────────
//
// عالَمُ الوَثائِقِ في الذاكِرَة (`EndpointDocWorld.cs`) والخِدمَةُ الحَقيقِيَّة؛ والتَعارُضُ يُحقَنُ بِخُطّافِ العالَمِ
// (`BeforeMember`) عِندَ أَوَّلِ `SaveChangesAsync` بِاستِثناءِ Marten نَفسِه (`JasperFx.ConcurrencyException`، مَقيسٌ نَوعُه
// عَلى Postgres: «Optimistic concurrency check failed for …Deal #…») — فَيُقاسُ أَنّ كُلَّ بابٍ يَلتَقِطُه، لا أَنّ Marten يَرميه
// (ذاكَ في البُرهانِ الحَيِّ أَعلاه). والعالَمُ يُعيدُ الكائِنَ نَفسَه لا نُسخَة، فَما «لَم يُكتَب» يُقاسُ بِعَدّادِ الحِفظِ لا بِالحالَة.

public class DealConcurrencyRefusalTests
{
    private const string Slug = "zz-occ-world";
    private static readonly Guid Buyer  = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
    private static readonly Guid Seller = Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb");

    private static string RepoRoot => ThemeZeroEquivalenceTests.RepoRoot;
    private const string Template = "libs/templates/ACommerce.Templates.Customer.Marketplace";

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

    /// <summary>صَفقَةُ سَلَّةٍ نَقدِيَّةٌ مَربوطٌ بائِعُها عِندَ الوِلادَة — بِتَسَلسُلِ <c>/checkout/submit</c>.</summary>
    private static (DocWorld World, DealsService Deals, Guid Id) CartDeal()
    {
        var world = new DocWorld();
        var deals = new DealsService(world.Store, new ForbiddenPaymentProvider());
        var deal = Now(deals.StartAsync(Slug, "marketplace", Buyer, "مُشتَرٍ", null, "سِلعَة", 100m,
            new() { [CheckoutPayMethods.AttributeKey] = CheckoutPayMethods.CashOnDelivery }));
        Now(deals.AttachRefAsync(Slug, deal.Id, DealsService.ListingOwnerRef, Seller.ToString()));
        Now(deals.BindListingOwnerAsync(Slug, deal.Id, Seller.ToString()));
        return (world, deals, deal.Id);
    }

    /// <summary>الحِفظُ التالي يَرمي ما يَرميه Marten حينَ كَتَبَ غَيرُ الجَلسَةِ الصَفقَةَ بَعدَ قِراءَتِها — مَرَّةً واحِدَة.</summary>
    private static void ConflictOnNextSave(DocWorld world, Guid id)
    {
        var armed = true;
        world.BeforeMember = name =>
        {
            if (name != "SaveChangesAsync" || !armed) return;
            armed = false;
            throw new JasperFx.ConcurrencyException(typeof(Deal), id);
        };
    }

    [Fact]
    public void Advance_turns_a_version_conflict_into_deal_changed_meanwhile()
    {
        var (world, deals, id) = CartDeal();
        ConflictOnNextSave(world, id);
        var saves = world.SaveCalls;

        var res = Now(deals.AdvanceAsync(Slug, id, Seller, "بائِع"));
        Assert.False(res.Ok);
        Assert.Equal(DealsService.ChangedMeanwhile, res.Code);
        Assert.NotNull(res.Deal);
        Assert.Equal(saves, world.SaveCalls);
    }

    [Fact]
    public void Accept_turns_a_version_conflict_into_deal_changed_meanwhile()
    {
        var (world, deals, id) = CartDeal();
        ConflictOnNextSave(world, id);
        var saves = world.SaveCalls;

        var res = Now(deals.AcceptAsync(Slug, id, new DealAcceptor(Seller, "بائِع")));
        Assert.False(res.Ok);
        Assert.Equal(DealsService.ChangedMeanwhile, res.Violation!.Code);
        Assert.NotNull(res.Deal);
        Assert.Equal(saves, world.SaveCalls);
    }

    [Fact]
    public void Cancel_turns_a_version_conflict_into_deal_changed_meanwhile()
    {
        var (world, deals, id) = CartDeal();
        ConflictOnNextSave(world, id);
        var saves = world.SaveCalls;

        var res = Now(deals.CancelAsync(Slug, id, new DealCanceller(Buyer, "مُشتَرٍ", IsStoreAdmin: false), "عَدَلتُ"));
        Assert.False(res.Ok);
        Assert.Equal(DealsService.ChangedMeanwhile, res.Violation!.Code);
        Assert.NotNull(res.Deal);
        Assert.Equal(saves, world.SaveCalls);
    }

    /// <summary>والأَبوابُ بِلا قَناةِ رَفضٍ (لا مُنادِيَ يَقرَأُ ما تُرجِعُه) لا تَرمي: تُعيدُ ما في المَخزَنِ ولا تَحفَظ.
    /// <para><b>قُلِبَ شَطرُ الإلحاقِ بِاسمِه</b> (‏ADR-036 §٧، ‏2026-10-12): كانَ
    /// <c>Assign_dispute_and_attach_return_the_stored_deal_on_a_version_conflict_and_do_not_throw</c> — والإلحاقُ مِنها يُسقِطُ مَرجِعَه
    /// عِندَ التَعارُضِ عَلى ظَنٍّ لَم يَصدُق («صَفقَةٌ وُلِدَت في الطَلَبِ نَفسِه»). وصارَ يُعادُ تَطبيقُه — صُفوفُه أَدناه؛ والتَعيينُ
    /// والنِزاعُ كَما كانا بِحَرفِهِما.</para></summary>
    [Fact]
    public void Assign_and_dispute_return_the_stored_deal_on_a_version_conflict_and_do_not_throw()
    {
        var (world, deals, id) = CartDeal();
        var offer = Now(deals.StartAsync(Slug, "marketplace", Buyer, "مُشتَرٍ", Guid.NewGuid(), "عَرض", 50m));
        var saves = world.SaveCalls;

        ConflictOnNextSave(world, offer.Id);
        Assert.NotNull(Now(deals.AssignCounterpartyAsync(Slug, offer.Id, Seller, "بائِع")));
        ConflictOnNextSave(world, id);
        Assert.NotNull(Now(deals.DisputeAsync(Slug, id, Seller, "بائِع", "نِزاع")));
        Assert.Equal(saves, world.SaveCalls);
    }

    // ─── الإلحاقُ يُعادُ تَطبيقُه — مُنادوه بِأَسمائِهِم (‏ADR-036 §٧) ─────────────────────────────────────
    //
    // **وكِتابَةُ الآخَرِ تَقَعُ فِعلاً بَينَ تَحميلِ الإلحاقِ وحِفظِه**: الخُطّافُ عِندَ حِفظِ الإلحاقِ الأَوَّلِ يُنادي الكاتِبَ الآخَرَ
    // (خِدمَةً حَقيقِيَّةً تَحفَظ) ثُمَّ يَرمي ما يَرميه Marten — فَيُقاسُ أَنّ الكِتابَتَينِ تَبقَيان، لا أَنّ المَرجِعَ «لَم يُكتَب».
    // (والعالَمُ يُعيدُ الكائِنَ نَفسَه، فَسَطرُ `note` يُعَدُّ في البُرهانِ الحَيِّ أَدناه لا هُنا.)

    /// <summary>الحِفظُ التالي: يُحفَظُ <paramref name="otherWrite"/> أَوَّلاً، ثُمَّ يَرمي ما يَرميه Marten — مَرَّةً واحِدَة.</summary>
    private static void OtherWriterSavesFirst(DocWorld world, Guid id, Action otherWrite)
    {
        var armed = true;
        world.BeforeMember = name =>
        {
            if (name != "SaveChangesAsync" || !armed) return;
            armed = false;
            otherWrite();
            throw new JasperFx.ConcurrencyException(typeof(Deal), id);
        };
    }

    /// <summary><b>مُنادي <c>/checkout/submit</c></b>: مَرجِعُ إذنِ الدَفعِ يُلحَقُ والبائِعُ المَربوطُ يَقبَلُ بَينَ تَحميلِه وحِفظِه —
    /// <b>يَبقى الاثنان</b>: الصَفقَةُ في <c>Booked</c> بِسَطرِ <c>advanced</c>، و<c>payment_id</c> عَلَيها فَيُقبَضُ عِندَ <c>Paid</c>.
    /// وكانَ الإلحاقُ يَعودُ بِالمَخزَّنِ بِلا المَرجِع — إذنٌ مَحجوزٌ لا يُقبَضُ ولا يُستَرجَع.</summary>
    [Fact]
    public void A_payment_ref_attached_while_the_bound_vendor_accepts_lands_and_the_acceptance_stands()
    {
        var (world, deals, id) = CartDeal();
        DealAcceptResult? accepted = null;
        OtherWriterSavesFirst(world, id, () => accepted = Now(deals.AcceptAsync(Slug, id, new DealAcceptor(Seller, "بائِع"))));
        var saves = world.SaveCalls;

        var back = Now(deals.AttachRefAsync(Slug, id, "payment_id", "auth_1"));

        Assert.True(accepted!.Ok, accepted.Violation?.Code);
        Assert.Equal("auth_1", back!.Refs["payment_id"]);
        var stored = world.Read<Deal>(id.ToString())!;
        Assert.Equal("auth_1", stored.Refs["payment_id"]);
        Assert.Equal(DealStage.Booked, stored.Stage);
        Assert.Single(stored.Timeline, e => e.Action == "advanced");
        Assert.Equal(saves + 2, world.SaveCalls);   // حِفظُ القَبولِ وحِفظُ الإلحاقِ المُعاد — والمَردودُ لا يُعَدّ
    }

    /// <summary><b>مُنادي تَقييمِ الاستوديو</b> (<c>POST /studio/apps/{slug}/deals/{id}/review</c> — يُلحِقُ <c>review_cp</c>/<c>review_init</c>
    /// بِصَفقَةٍ قائِمَة، فَعّالَةً عِندَ <c>Delivered</c> أَو <c>Received</c>): المُشتَري يَختِمُها بَينَ تَحميلِ الإلحاقِ وحِفظِه —
    /// يَبقى الاثنان: الصَفقَةُ <c>Completed</c> والمَرجِعُ عَلَيها.</summary>
    [Fact]
    public void A_studio_review_ref_lands_even_when_the_deal_is_completed_meanwhile()
    {
        var world = new DocWorld();
        var deals = new DealsService(world.Store, new ForbiddenPaymentProvider());
        var deal = new Deal
        {
            Id = Guid.NewGuid(), TenantSlug = Slug, Pattern = "marketplace", Stage = DealStage.Delivered, AmountSar = 100,
            InitiatorId = Buyer, InitiatorName = "مُشتَرٍ", CounterpartyId = Seller, CounterpartyName = "بائِع",
        };
        deal.Attributes[CheckoutPayMethods.AttributeKey] = CheckoutPayMethods.CashOnDelivery;
        world.Put(deal);
        var reviewId = Guid.NewGuid().ToString("N");
        OtherWriterSavesFirst(world, deal.Id, () => Assert.True(Now(deals.AdvanceAsync(Slug, deal.Id, Buyer, "مُشتَرٍ")).Ok));

        Now(deals.AttachRefAsync(Slug, deal.Id, "review_cp", reviewId));

        var stored = world.Read<Deal>(deal.Id.ToString())!;
        Assert.Equal(reviewId, stored.Refs["review_cp"]);
        Assert.Equal((DealStage.Reviewed, DealStatus.Completed), (stored.Stage, stored.Status));
    }

    /// <summary><b>والحَدّ — بِصَوت</b>: ثَلاثُ مُحاوَلاتٍ كُلُّها مَردودَةٌ ⇒ يَصعَدُ <c>JasperFx.ConcurrencyException</c> إلى المُنادي، ولا
    /// يُحفَظُ شَيء، ولا رابِعَة — لا حَلقَةٌ بِلا نِهايَةٍ ولا إسقاطٌ صامِت.</summary>
    [Fact]
    public void An_attach_that_keeps_losing_gives_up_loudly_after_three_attempts_and_saves_nothing()
    {
        var (world, deals, id) = CartDeal();
        var conflicts = 0;
        world.BeforeMember = name =>
        {
            if (name != "SaveChangesAsync") return;
            conflicts++;
            throw new JasperFx.ConcurrencyException(typeof(Deal), id);
        };
        var saves = world.SaveCalls;

        Assert.Throws<JasperFx.ConcurrencyException>(() => Now(deals.AttachRefAsync(Slug, id, "payment_id", "auth_1")));

        Assert.Equal(3, conflicts);
        Assert.Equal(saves, world.SaveCalls);
    }

    // ─── المَصدَر: تَسجيلٌ واحِد، وكُلُّ بابِ كِتابَةٍ يَلتَقِط ───────────────────────────────────────

    private static string Code(params string[] parts)
    {
        var path = Path.Combine(RepoRoot, Path.Combine(parts));
        Assert.True(File.Exists(path), $"مَصدَرٌ مَفقود: {path} — الأَداةُ عَمياء.");
        var text = File.ReadAllText(path);
        Assert.True(text.Length > 500, $"أَداةٌ عَمياء: {path} لَم يُقرَأ.");
        return WriteEndpointGuardTests.StripComments(text);
    }

    private static string Segment(string source, string from, string to)
    {
        var start = source.IndexOf(from, StringComparison.Ordinal);
        Assert.True(start >= 0, $"‏`{from}` غَيرُ مَوجود — الأَداةُ عَمياء.");
        var end = source.IndexOf(to, start + from.Length, StringComparison.Ordinal);
        Assert.True(end > start, $"‏`{to}` لا يَتلو `{from}` — الأَداةُ عَمياء.");
        return source.Substring(start, end - start);
    }

    /// <summary>تَسجيلٌ واحِدٌ في المُستَودَعِ كُلِّه، يُناديه تَركيبُ القالِب — والبُرهانُ الحَيُّ يُنادي الدالَّةَ نَفسَها.</summary>
    [Fact]
    public void The_deal_document_is_registered_once_with_optimistic_concurrency_through_the_template()
    {
        var service = Code(Template, "Services", "Deals", "DealsService.cs");
        Assert.Contains("public static void RegisterDocument(StoreOptions opts)", service, StringComparison.Ordinal);
        Assert.Contains("=> opts.Schema.For<Deal>().UseOptimisticConcurrency(true);", service, StringComparison.Ordinal);
        Assert.Equal(1, Count(Code(Template, "MarketplaceTemplateExtensions.cs"), "Services.Deals.DealsService.RegisterDocument(opts);"));

        var scanned = 0;
        var hits = 0;
        foreach (var root in new[] { "libs", "apps" })
        foreach (var f in Directory.EnumerateFiles(Path.Combine(RepoRoot, root), "*.cs", SearchOption.AllDirectories))
        {
            if (f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
                f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)) continue;
            scanned++;
            hits += Count(WriteEndpointGuardTests.StripComments(File.ReadAllText(f)), "UseOptimisticConcurrency(");
        }
        Assert.True(scanned > 200 && hits == 1, $"فُحِصَ {scanned} مِلَفّاً و`UseOptimisticConcurrency(` {hits} — المُتَوَقَّعُ واحِد.");
    }

    /// <summary><b>كُلُّ بابِ كِتابَةٍ عامٍّ في الخِدمَةِ يَلتَقِطُ التَعارُض</b> — بِاسمِه؛ وقائِمَةُ مَن لا يَلتَقِطُ مُسَمّاةٌ بِسَبَبِها:
    /// <c>StartAsync</c> يُدرِجُ وَثيقَةً بِمُعَرِّفٍ جَديدٍ فَلا نُسخَةَ تُعارِضُه، و<c>BindListingOwnerAsync</c> يَكتُبُ عَبرَ
    /// <c>AssignCounterpartyAsync</c>، و<c>MoveAsync</c> الخاصَّةُ تَرمي إلى بابَيها اللَذَينِ يَلتَقِطان. والعَدُّ يُطبَعُ عِندَ الفَشَل.</summary>
    [Fact]
    public void Every_public_deal_writer_catches_the_version_conflict_and_the_exceptions_are_named()
    {
        var service = Code(Template, "Services", "Deals", "DealsService.cs");
        var writers = new (string Name, string From, string To)[]
        {
            ("AssignCounterpartyAsync", "public async Task<Deal?> AssignCounterpartyAsync(", "public async Task<Deal?> BindListingOwnerAsync("),
            ("AcceptAsync",             "public async Task<DealAcceptResult> AcceptAsync(",  "public async Task<DealAdvanceResult> AdvanceAsync("),
            ("AdvanceAsync",            "public async Task<DealAdvanceResult> AdvanceAsync(", "private async Task<Deal> MoveAsync("),
            // (‏2026-10-13، مُراجَعَةُ المَوجَة 3a — G4 (ii)؛ ولا يُعادُ حَرفٌ فَوقَه: `DisputeAsync` صارَت تُرجِعُ `DealDisputeResult` لا `Deal?`،
            // فَتَبَدَّلَ حَرفُ عَلامَتَي المَقطَعِ وَحدَه — والمَقطَعانِ والعَدّانِ بِحَرفِهِما.)
            ("CancelAsync",             "public async Task<DealCancelResult> CancelAsync(",  "public async Task<DealDisputeResult> DisputeAsync("),
            ("DisputeAsync",            "public async Task<DealDisputeResult> DisputeAsync(", "public async Task<Deal?> AttachRefAsync("),
            ("AttachRefAsync",          "public async Task<Deal?> AttachRefAsync(",          "public async Task<Deal?> LoadAsync("),
            // ‏ADR-032 الخُطوَة 10 (تَيّارُ S9، ‏2026-10-12): بابٌ سابِعٌ يَكتُبُ بِحَلقَةِ الإلحاقِ نَفسِها (`RecordAsync` — تَقرَأُ ثُمَّ تُعيدُ عِندَ
            // التَعارُض)، فَالتِقاطُه التِقاطُها ومَقطَعُه يَبلُغُها؛ ولا حِفظَ لَه ولا التِقاطَ خاصَّين.
            ("ConfirmGatewayPaymentAsync", "public async Task<IReadOnlyList<Deal>> ConfirmGatewayPaymentAsync(", "public async Task<Deal?> LoadAsync("),
        };
        var missing = writers.Where(w => !Segment(service, w.From, w.To).Contains("catch (JasperFx.ConcurrencyException)", StringComparison.Ordinal))
                             .Select(w => w.Name).ToArray();
        Assert.True(missing.Length == 0, $"فُحِصَ {writers.Length} باباً؛ لا يَلتَقِطُ التَعارُض: {string.Join("، ", missing)}");
        // سَبعَةُ أَبوابٍ وسِتَّةُ مَواضِعِ التِقاط (‏2026-10-12): الإلحاقُ وتَأكيدُ دَفعِ البَوّابَةِ يَشتَرِكانِ في حَلقَةِ `RecordAsync` — وكانَ
        // العَدَدانِ واحِداً (`writers.Length`) حينَ كانَ لِكُلِّ بابٍ التِقاطُه.
        Assert.Equal(6, Count(service, "catch (JasperFx.ConcurrencyException)"));
        Assert.DoesNotContain("SaveChangesAsync(", Segment(service, "public async Task<IReadOnlyList<Deal>> ConfirmGatewayPaymentAsync(", "private Task<Deal?> RecordAsync("), StringComparison.Ordinal);

        // كُلُّ `SaveChangesAsync(` في الخِدمَةِ مَحسوب: سِتَّة — الإدراجُ والتَعيينُ والانتِقالُ والإلغاءُ والنِزاعُ والإلحاق.
        Assert.Equal(6, Count(service, "SaveChangesAsync("));
        Assert.DoesNotContain("catch (JasperFx.ConcurrencyException)", Segment(service, "public async Task<Deal> StartAsync(", "public async Task<Deal?> AssignCounterpartyAsync("), StringComparison.Ordinal);
        Assert.DoesNotContain("SaveChangesAsync(", Segment(service, "public async Task<Deal?> BindListingOwnerAsync(", "public async Task<DealAcceptResult> AcceptAsync("), StringComparison.Ordinal);
    }

    /// <summary>و<c>/advance</c> يَحمِلُ رَمزَ نَتيجَتِه إلى الصَفحَة — كانَ يُسقِطُ النَتيجَةَ كُلَّها.</summary>
    [Fact]
    public void The_member_advance_endpoint_carries_the_result_code_to_the_deal_page()
    {
        var endpoints = Code(Template, "MarketplaceTemplateExtensions.cs");
        var advance = Segment(endpoints, "\"/{slug}/deals/{id:guid}/advance\"", ".RequireStoreWritable();");
        Assert.Contains("var res = await deals.AdvanceAsync(slug, id, userId, userName, note);", advance, StringComparison.Ordinal);
        Assert.Contains("return Results.Redirect(Link(req, slug, $\"deals/{id}\" + (res.Code is { } code ? $\"?err={code}\" : \"\")));",
            advance, StringComparison.Ordinal);
    }

    private static int Count(string text, string needle)
    {
        var n = 0;
        for (var i = text.IndexOf(needle, StringComparison.Ordinal); i >= 0;
             i = text.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
            n++;
        return n;
    }
}
