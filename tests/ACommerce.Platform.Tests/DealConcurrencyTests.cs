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
    private static bool Enabled =>
        Environment.GetEnvironmentVariable("WASAYEL_LIVE_PROOF") == "1";

    private static string? ConnectionString =>
        Environment.GetEnvironmentVariable("ConnectionStrings__Postgres");

    private static string ResilientConnection =>
        ConnectionString!.TrimEnd(';') + ";Timeout=60;Command Timeout=120";

    /// <summary>تَركيبُ المَخزَنِ كَما يُرَكِّبُه المُضيف — بِلا تَسجيلٍ لِوَثيقَةِ <see cref="Deal"/> بَعد: هذا ما يُشَغَّلُ
    /// اليَوم.</summary>
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
        private static InvalidOperationException Touched() => new("صَفقَةُ النَقدِ لا تَمَسُّ المال — نودِيَ مُزَوِّدُ الدَفع.");
        public string ProviderName => "forbidden";
        public Task<PaymentResult> AuthorizeAsync(PaymentRequest req, string idempotencyKey, CancellationToken ct = default) => throw Touched();
        public Task<PaymentResult> CaptureAsync(string paymentId, decimal? amount = null, CancellationToken ct = default) => throw Touched();
        public Task<PaymentResult> RefundAsync(string paymentId, decimal amount, string reason, CancellationToken ct = default) => throw Touched();
        public Task<SubscriptionResult> CreateSubscriptionAsync(SubscriptionRequest req, string idempotencyKey, CancellationToken ct = default) => throw Touched();
        public Task<bool> CancelSubscriptionAsync(string subscriptionId, CancellationToken ct = default) => throw Touched();
        public Task<Invoice?> GetInvoiceAsync(string paymentId, CancellationToken ct = default) => throw Touched();
    }

    /// <summary><b>اليَوم</b> (يُقلَبُ بِاسمِه في كوميتِ ‏ADR-036): المُشتَري يُلغي صَفقَةَ سَلَّتِه النَقدِيَّةَ والبائِعُ المَربوطُ
    /// يَقبَلُها في الأَثناء — الإلغاءُ قَرَأَ الصَفقَةَ قَبلَ القَبولِ ويَحفَظُ بَعدَه. <b>فَيَنجَحُ الاثنان</b>: القَبولُ يُرجِعُ
    /// <c>Accepted</c> (والنُقطَةُ تُشعِرُ المُشتَريَ بِقَبولِ ما أَلغاه)، والإلغاءُ يُرجِعُ <c>Cancelled</c> ويَكتُبُ وَثيقَتَه
    /// فَوقَ القَبول: الصَفقَةُ مُلغاةٌ عِندَ <c>Offered</c> وسَطرُ <c>advanced</c> الَّذي كَتَبَه القَبولُ زالَ مِن السِجِلّ.</summary>
    [Fact]
    public async Task Today_a_cancel_saved_after_an_accept_overwrites_it_and_both_report_success()
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
            output.WriteLine($"قَبول: ok={accept.Ok} · إلغاء: ok={cancel.Ok} · النِهائِيَّة: {final.Status}/{final.Stage} · " +
                             $"سِجِلّ: {string.Join(",", final.Timeline.Select(e => e.Action))}");

            Assert.True(accept.Ok, accept.Violation?.MessageAr);
            Assert.Equal(DealStage.Booked, accept.Deal!.Stage);
            Assert.True(cancel.Ok, cancel.Violation?.MessageAr);
            Assert.Equal(DealStatus.Cancelled, final.Status);
            Assert.Equal(DealStage.Offered, final.Stage);
            Assert.DoesNotContain(final.Timeline, e => e.Action == "advanced");
        }
        finally
        {
            await using var s = store.LightweightSession(slug);
            foreach (var d in await s.Query<Deal>().ToListAsync()) s.Delete(d);
            await s.SaveChangesAsync();
        }
    }
}
