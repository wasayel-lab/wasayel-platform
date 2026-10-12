using ACommerce.Kit.Payments;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using Marten;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ أَثَرا المالِ والحِفظُ المَفحوص — أَيُّهُما أَوَّلاً؟ (‏ADR-036 §٣‑١، الخُطوَة 2) ═══════════════════════════
//
// **المَقيسُ قَبلَ هذا المِلَفّ** (‏ADR-036 §٣‑١ بِحَرفِه): «`CancelAsync` تُنادي `RefundAsync` و`MoveAsync` تُنادي
// `CaptureAsync` عِندَ `Paid` **قَبلَ** `SaveChangesAsync`. فَكِتابَةٌ تُرَدُّ بِالتَعارُضِ قَد تَتلو استِرجاعاً أَو قَبضاً وَقَعَ ولَم
// يُسَجَّل — و«أَعِد المُحاوَلَة» حينَها تَطلُبُ الاستِرجاعَ ثانِيَةً». وكانَ قَولاً بِلا صَفّ: صُفوفُ التَعارُضِ في
// `DealConcurrencyTests` كُلُّها نَقدٌ بِمُزَوِّدٍ يَرمي عِندَ أَيِّ نِداء، فَلا تَرى المالَ أَصلاً.
//
// **تَوصيفُ «اليَوم» يُودَعُ وَحدَه** (القاعِدَة ٣) ثُمَّ يُقلَبُ بِاسمِه في كوميتِ التَرتيب: صَفّانِ بِلا قاعِدَة (عالَمُ الوَثائِقِ
// ومُزَوِّدٌ يُسَجِّلُ ما نودِيَ بِه، والتَعارُضُ يُحقَنُ بِاستِثناءِ Marten نَفسِه عِندَ الحِفظ) وصَفّانِ حَيّانِ عَلى Postgres
// بِبَوّابَةِ الحِفظِ نَفسِها (`SaveGate`، ‏DealConcurrencyTests) — يَقرَأُ الكاتِبُ الصَفقَةَ ويَقِفُ قَبلَ حِفظِه، ثُمَّ يُلحَقُ مَرجِعٌ
// ويُحفَظ، ثُمَّ يُستَأنَف فَيَرُدُّه Marten؛ ثُمَّ تُعادُ المُحاوَلَةُ كَما يُعيدُها صاحِبُ النَقرَة.

public class DealMoneyClaimFirstTests
{
    private const string Slug = "zz-money-claim";
    private static readonly Guid Buyer  = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
    private static readonly Guid Seller = Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb");

    /// <summary>مُزَوِّدٌ يُسَجِّلُ كُلَّ نِداءٍ بِتَرتيبِه ويَنجَح: يَقبِضُ ويَستَرجِع؛ و<see cref="During"/> يُنادى <b>داخِلَ</b> النِداءِ —
    /// كِتابَةٌ أُخرى تَقَعُ وهو جارٍ. وما سِواهُما يَرمي: لا حَجزَ ولا اشتِراكَ في هذا المِلَفّ.</summary>
    internal sealed class RecordingPaymentProvider : IPaymentProvider
    {
        public List<string> Calls { get; } = new();
        public Func<Task>? During { get; set; }
        public string ProviderName => "recording";

        public async Task<PaymentResult> CaptureAsync(string paymentId, decimal? amount = null, CancellationToken ct = default)
        {
            Calls.Add($"capture {paymentId}");
            if (During is { } other) await other();
            return new PaymentResult(paymentId, PaymentStatus.Captured, amount ?? 0m);
        }

        public async Task<PaymentResult> RefundAsync(string paymentId, decimal amount, string reason, CancellationToken ct = default)
        {
            Calls.Add($"refund {paymentId}");
            if (During is { } other) await other();
            return new PaymentResult($"ref_{paymentId}", PaymentStatus.Refunded, amount);
        }

        private static InvalidOperationException Untouched() => new("لا حَجزَ ولا اشتِراكَ في مَسارِ الإلغاءِ والقَبض.");
        public Task<PaymentResult> AuthorizeAsync(PaymentRequest req, string idempotencyKey, CancellationToken ct = default) => throw Untouched();
        public Task<SubscriptionResult> CreateSubscriptionAsync(SubscriptionRequest req, string idempotencyKey, CancellationToken ct = default) => throw Untouched();
        public Task<bool> CancelSubscriptionAsync(string subscriptionId, CancellationToken ct = default) => throw Untouched();
        public Task<Invoice?> GetInvoiceAsync(string paymentId, CancellationToken ct = default) => throw Untouched();
    }

    private static T Now<T>(Task<T> task)
    {
        Assert.True(task.IsCompleted, "نِداءٌ لَم يَكتَمِل في عالَمٍ مُتَزامِن — الأَداةُ تَكذِب.");
        return task.GetAwaiter().GetResult();
    }

    /// <summary>صَفقَةُ بِطاقَةٍ مَربوطٌ بائِعُها، بِمَرجِعِ إذنٍ مَحجوزٍ كَما يُلحِقُه <c>/checkout/submit</c>، عِندَ <paramref name="stage"/>.</summary>
    private static (DocWorld World, DealsService Deals, RecordingPaymentProvider Pay, Guid Id) CardDeal(DealStage stage)
    {
        var world = new DocWorld();
        var pay = new RecordingPaymentProvider();
        var deals = new DealsService(world.Store, pay);
        var deal = new Deal
        {
            Id = Guid.NewGuid(), TenantSlug = Slug, Pattern = "marketplace", Stage = stage, AmountSar = 100,
            InitiatorId = Buyer, InitiatorName = "مُشتَرٍ", CounterpartyId = Seller, CounterpartyName = "بائِع",
        };
        deal.Attributes[CheckoutPayMethods.AttributeKey] = CheckoutPayMethods.Card;
        deal.Refs["payment_id"] = "pay_1";
        deal.Refs["payment_status"] = nameof(PaymentStatus.Authorized);
        world.Put(deal);
        return (world, deals, pay, deal.Id);
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

    /// <summary><b>اليَوم</b>: إلغاءٌ يَرُدُّه تَعارُضُ النُسخَةِ بِـ<c>deal_changed_meanwhile</c> — «لَم يُكتَب شَيء» — <b>وقَد نودِيَ
    /// الاستِرجاعُ قَبلَه</b>: المُزَوِّدُ رَدَّ المالَ، ولا حِفظَ يَحمِلُ ذلك.</summary>
    [Fact]
    public void Today_a_cancel_refused_by_a_version_conflict_has_already_refunded_and_records_none_of_it()
    {
        var (world, deals, pay, id) = CardDeal(DealStage.Booked);
        ConflictOnNextSave(world, id);
        var saves = world.SaveCalls;

        var res = Now(deals.CancelAsync(Slug, id, new DealCanceller(Buyer, "مُشتَرٍ", IsStoreAdmin: false), "عَدَلتُ"));

        Assert.False(res.Ok);
        Assert.Equal(DealsService.ChangedMeanwhile, res.Violation!.Code);
        Assert.Equal(new[] { "refund pay_1" }, pay.Calls);
        Assert.Equal(saves, world.SaveCalls);
    }

    /// <summary><b>اليَوم</b>: دُخولُ <c>Paid</c> يَرُدُّه تَعارُضُ النُسخَةِ بِـ<c>deal_changed_meanwhile</c> — <b>وقَد نودِيَ القَبضُ
    /// قَبلَه</b>: الإذنُ قُبِضَ، والصَفقَةُ في المَخزَنِ لَم تَدخُل <c>Paid</c> ولا سَطرَ <c>captured</c>.</summary>
    [Fact]
    public void Today_an_advance_into_Paid_refused_by_a_version_conflict_has_already_captured_and_records_none_of_it()
    {
        var (world, deals, pay, id) = CardDeal(DealStage.Confirmed);
        ConflictOnNextSave(world, id);
        var saves = world.SaveCalls;

        var res = Now(deals.AdvanceAsync(Slug, id, Buyer, "مُشتَرٍ"));

        Assert.False(res.Ok);
        Assert.Equal(DealsService.ChangedMeanwhile, res.Code);
        Assert.Equal(new[] { "capture pay_1" }, pay.Calls);
        Assert.Equal(saves, world.SaveCalls);
    }
}

// ─── البُرهانُ الحَيّ — عَلى Postgres، بِبَوّابَةِ الحِفظِ نَفسِها ─────────────────────────────────────────────
//
// مُبَوَّبٌ بِـWASAYEL_LIVE_PROOF=1 وConnectionStrings__Postgres (عَقدُ `LiveDealConcurrencyProofTests` حَرفاً، ومِنه تَركيبُ المَخزَن
// بِتَسجيلِ الصَفقَةِ الَّذي يُناديه القالِب)، ومُستَأجِرٌ مُؤَقَّتٌ يُمسَحُ بَعدَه — لا `ashare`. الكاتِبُ الآخَرُ إلحاقُ مَرجِعٍ يُحفَظُ
// بَينَ قِراءَةِ الكاتِبِ المُعَلَّمِ وحِفظِه؛ ثُمَّ تُعادُ المُحاوَلَةُ بِالخِدمَةِ نَفسِها كَما يُعيدُها صاحِبُ النَقرَة.

public class LiveDealMoneyClaimFirstProofTests(ITestOutputHelper output)
{
    private static bool Enabled
        => LiveDealConcurrencyProofTests.Enabled && !string.IsNullOrEmpty(LiveDealConcurrencyProofTests.ConnectionString);

    /// <summary>صَفقَةُ سَلَّةٍ بِبِطاقَةٍ بِمَرجِعِ إذنٍ ومالِكِ إعلانٍ مَربوطٍ — بِتَسَلسُلِ <c>/checkout/submit</c> — ثُمَّ إلى
    /// <paramref name="stage"/> بِأَيدي فاعِليها (‏<c>Offered</c> أَو <c>Booked</c> أَو <c>Confirmed</c>).</summary>
    private static async Task<Deal> CardDealAsync(DealsService plain, string slug, Guid buyer, Guid seller, DealStage stage)
    {
        var born = await plain.StartAsync(slug, "marketplace", buyer, "مُشتَرٍ", null, "سِلعَة", 150m,
            new() { [CheckoutPayMethods.AttributeKey] = CheckoutPayMethods.Card });
        await plain.AttachRefAsync(slug, born.Id, "payment_id", "auth_live");
        await plain.AttachRefAsync(slug, born.Id, "payment_status", nameof(PaymentStatus.Authorized));
        await plain.AttachRefAsync(slug, born.Id, DealsService.ListingOwnerRef, seller.ToString());
        await plain.BindListingOwnerAsync(slug, born.Id, seller.ToString());
        if (stage >= DealStage.Booked) Assert.True((await plain.AcceptAsync(slug, born.Id, new DealAcceptor(seller, "بائِع"))).Ok);
        if (stage >= DealStage.Confirmed) Assert.True((await plain.AdvanceAsync(slug, born.Id, seller, "بائِع")).Ok);
        var at = (await plain.LoadAsync(slug, born.Id))!;
        Assert.Equal(stage, at.Stage);
        return at;
    }

    /// <summary>سَطرُ السِجِلِّ لِلطِباعَة — والإلحاقُ بِمِفتاحِه لا بِقيمَتِه.</summary>
    private static string Line(DealEvent e) => e switch
    {
        { Action: "advanced" } => $"advanced→{e.StageAfter}",
        { Action: "note", Note: { } n } when n.StartsWith("attach ", StringComparison.Ordinal) && n.Contains('=') => n[..n.IndexOf('=')],
        _ => e.Action,
    };

    private void Print(string head, Deal final, DealMoneyClaimFirstTests.RecordingPaymentProvider pay)
        => output.WriteLine($"{head} · النِهائِيَّة: {final.Status}/{final.Stage} · المُزَوِّد: [{string.Join(", ", pay.Calls)}] · " +
                            $"سِجِلّ: {string.Join(",", final.Timeline.Select(Line))}");

    /// <summary>الكاتِبُ المُعَلَّمُ يَقِفُ قَبلَ أَوَّلِ حِفظٍ في تَدَفُّقِه، ويُحفَظُ إلحاقُ <c>probe_note</c> بَينَهُما، ثُمَّ يُستَأنَف.</summary>
    private static async Task<T> HeldPastAnAttachAsync<T>(IDocumentStore store, DealsService plain, string slug, Guid id,
        DealMoneyClaimFirstTests.RecordingPaymentProvider pay, Func<DealsService, Task<T>> write)
    {
        var gate = new SaveGate.Gate();
        var deals = new DealsService(SaveGate.Wrap(store, gate), pay);
        var held = Task.Run(async () =>
        {
            gate.Marked.Value = true;
            return await write(deals);
        });
        await gate.Reached.Task.WaitAsync(TimeSpan.FromSeconds(60));
        await plain.AttachRefAsync(slug, id, "probe_note", "live");
        gate.Release.TrySetResult();
        return await held.WaitAsync(TimeSpan.FromSeconds(60));
    }

    private static async Task CleanAsync(IDocumentStore store, string slug)
    {
        await using var s = store.LightweightSession(slug);
        foreach (var d in await s.Query<Deal>().ToListAsync()) s.Delete(d);
        await s.SaveChangesAsync();
    }

    /// <summary><b>اليَوم</b>: إلغاءُ المُشتَري يَقرَأُ الصَفقَةَ ويَستَرجِعُ ثُمَّ يَقِفُ قَبلَ الحِفظ؛ يُحفَظُ إلحاقٌ بَينَهُما؛ يُستَأنَفُ
    /// فَيَرُدُّه Marten بِـ<c>deal_changed_meanwhile</c> — <b>والمالُ رُدَّ</b> ولا <c>refund_id</c> عَلى الصَفقَة. و«أَعِد المُحاوَلَة»
    /// تَمضي <b>وتَستَرجِعُ ثانِيَةً</b>: نِداءا استِرجاعٍ لِإلغاءٍ واحِد.</summary>
    [Fact]
    public async Task Today_a_cancel_refused_after_an_attach_has_refunded_and_its_retry_refunds_again()
    {
        if (!Enabled) return;

        await using var store = LiveDealConcurrencyProofTests.BuildStore();
        var slug = "zz-claim-" + Guid.NewGuid().ToString("N")[..8];
        var (buyer, seller) = (Guid.NewGuid(), Guid.NewGuid());
        try
        {
            var plain = new DealsService(store, new DealMoneyClaimFirstTests.RecordingPaymentProvider());
            var deal = await CardDealAsync(plain, slug, buyer, seller, DealStage.Booked);
            var pay = new DealMoneyClaimFirstTests.RecordingPaymentProvider();
            var by = new DealCanceller(buyer, "مُشتَرٍ", IsStoreAdmin: false);

            var refused = await HeldPastAnAttachAsync(store, plain, slug, deal.Id, pay,
                d => d.CancelAsync(slug, deal.Id, by, "عَدَلتُ"));
            var between = (await plain.LoadAsync(slug, deal.Id))!;
            Print($"الإلغاء: ok={refused.Ok} code={refused.Violation?.Code}", between, pay);

            Assert.False(refused.Ok);
            Assert.Equal(DealsService.ChangedMeanwhile, refused.Violation!.Code);
            Assert.Equal(new[] { "refund auth_live" }, pay.Calls);
            Assert.Equal(DealStatus.Active, between.Status);
            Assert.False(between.Refs.ContainsKey("refund_id"));

            var retry = await new DealsService(store, pay).CancelAsync(slug, deal.Id, by, "عَدَلتُ");
            var final = (await plain.LoadAsync(slug, deal.Id))!;
            Print($"الإعادَة: ok={retry.Ok}", final, pay);

            Assert.True(retry.Ok, retry.Violation?.Code);
            Assert.Equal(new[] { "refund auth_live", "refund auth_live" }, pay.Calls);
            Assert.Equal(DealStatus.Cancelled, final.Status);
            Assert.Single(final.Timeline, e => e.Action == "refunded");
        }
        finally { await CleanAsync(store, slug); }
    }

    /// <summary><b>اليَوم</b>: المُشتَري يُدخِلُها <c>Paid</c> فَيُقبَضُ الإذنُ ثُمَّ يَقِفُ الحِفظ؛ يُحفَظُ إلحاقٌ بَينَهُما؛ يُستَأنَفُ فَيُرَدُّ
    /// بِـ<c>deal_changed_meanwhile</c> — <b>والإذنُ قُبِض</b> والصَفقَةُ عِندَ <c>Confirmed</c>. والإعادَةُ تَمضي <b>وتَقبِضُ ثانِيَةً</b>.</summary>
    [Fact]
    public async Task Today_an_advance_into_Paid_refused_after_an_attach_has_captured_and_its_retry_captures_again()
    {
        if (!Enabled) return;

        await using var store = LiveDealConcurrencyProofTests.BuildStore();
        var slug = "zz-claim-" + Guid.NewGuid().ToString("N")[..8];
        var (buyer, seller) = (Guid.NewGuid(), Guid.NewGuid());
        try
        {
            var plain = new DealsService(store, new DealMoneyClaimFirstTests.RecordingPaymentProvider());
            var deal = await CardDealAsync(plain, slug, buyer, seller, DealStage.Confirmed);
            var pay = new DealMoneyClaimFirstTests.RecordingPaymentProvider();

            var refused = await HeldPastAnAttachAsync(store, plain, slug, deal.Id, pay,
                d => d.AdvanceAsync(slug, deal.Id, buyer, "مُشتَرٍ"));
            var between = (await plain.LoadAsync(slug, deal.Id))!;
            Print($"الدَفع: ok={refused.Ok} code={refused.Code}", between, pay);

            Assert.False(refused.Ok);
            Assert.Equal(DealsService.ChangedMeanwhile, refused.Code);
            Assert.Equal(new[] { "capture auth_live" }, pay.Calls);
            Assert.Equal((DealStatus.Active, DealStage.Confirmed), (between.Status, between.Stage));
            Assert.DoesNotContain(between.Timeline, e => e.Action == "captured");

            var retry = await new DealsService(store, pay).AdvanceAsync(slug, deal.Id, buyer, "مُشتَرٍ");
            var final = (await plain.LoadAsync(slug, deal.Id))!;
            Print($"الإعادَة: ok={retry.Ok}", final, pay);

            Assert.True(retry.Ok, retry.Reason);
            Assert.Equal(new[] { "capture auth_live", "capture auth_live" }, pay.Calls);
            Assert.Equal(DealStage.Paid, final.Stage);
            Assert.Single(final.Timeline, e => e.Action == "captured");
        }
        finally { await CleanAsync(store, slug); }
    }
}
