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
//
// **ثُمَّ قُلِبَت الأَربَعَةُ بِأَسمائِها** (‏ADR-036 الخُطوَة 2، ‏2026-10-12 — ولا يُعادُ حَرفٌ فَوقَه): المُطالَبَةُ أَوَّلاً — الحالَةُ تُحفَظُ
// بِفَحصِ النُسخَةِ (`CancelAsync` حِفظُها، و`MoveAsync` حِفظُها)، ثُمَّ الأَثَرُ لِمَن كَسَبَ الحِفظ، ثُمَّ نَتيجَتُه في كِتابَةٍ ثانِيَةٍ تُقرَأُ
// فيها الصَفقَةُ وتُعادُ عَلى ما كُتِب (`RecordAsync` — حَلقَةُ الإلحاقِ نَفسُها، ‏§٧). والمَردودُ لا يَمَسُّ المال، و«أَعِد المُحاوَلَة»
// تَمَسُّه مَرَّة. وصُفوفٌ بَعدَها: النَتيجَةُ في حِفظٍ ثانٍ، وتُعادُ إن سَبَقَتها كِتابَة، والفَشَلُ يُسَجَّلُ ولا يَكسِرُ الحالَة،
// والتَعارُضُ الغالِبُ عَلى النَتيجَةِ يَصعَدُ بِصَوتٍ ولا يُقالُ «لَم يُكتَب»، والقَبولُ لا يُدخِلُ «دَفع» في أَيِّ نَمَط.

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

        /// <summary>يَرمي بَعدَ أَن يُسَجِّلَ النِداء — مُزَوِّدٌ رَفَضَ أَو انقَطَع.</summary>
        public bool Fail { get; set; }
        public string ProviderName => "recording";

        public async Task<PaymentResult> CaptureAsync(string paymentId, decimal? amount = null, CancellationToken ct = default)
        {
            Calls.Add($"capture {paymentId}");
            if (During is { } other) await other();
            if (Fail) throw new InvalidOperationException("capture refused");
            return new PaymentResult(paymentId, PaymentStatus.Captured, amount ?? 0m);
        }

        public async Task<PaymentResult> RefundAsync(string paymentId, decimal amount, string reason, CancellationToken ct = default)
        {
            Calls.Add($"refund {paymentId}");
            if (During is { } other) await other();
            if (Fail) throw new InvalidOperationException("refund refused");
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

    /// <summary><b>قُلِبَ بِاسمِه</b> (‏ADR-036 الخُطوَة 2، ‏2026-10-12): كانَ
    /// <c>Today_a_cancel_refused_by_a_version_conflict_has_already_refunded_and_records_none_of_it</c> — المَردودُ قَد استَرجَع
    /// (<c>refund pay_1</c>) بِلا حِفظ. <b>وصارَ</b>: الحِفظُ المَفحوصُ هُوَ المُطالَبَة، فَإلغاءٌ يَرُدُّه التَعارُضُ لا يَبلُغُ المُزَوِّدَ أَصلاً.</summary>
    [Fact]
    public void A_cancel_refused_by_a_version_conflict_refunds_nothing()
    {
        var (world, deals, pay, id) = CardDeal(DealStage.Booked);
        ConflictOnNextSave(world, id);
        var saves = world.SaveCalls;

        var res = Now(deals.CancelAsync(Slug, id, new DealCanceller(Buyer, "مُشتَرٍ", IsStoreAdmin: false), "عَدَلتُ"));

        Assert.False(res.Ok);
        Assert.Equal(DealsService.ChangedMeanwhile, res.Violation!.Code);
        Assert.Empty(pay.Calls);
        Assert.Equal(saves, world.SaveCalls);
    }

    /// <summary><b>قُلِبَ بِاسمِه</b> (‏ADR-036 الخُطوَة 2، ‏2026-10-12): كانَ
    /// <c>Today_an_advance_into_Paid_refused_by_a_version_conflict_has_already_captured_and_records_none_of_it</c> — المَردودُ قَد قَبَض
    /// (<c>capture pay_1</c>) بِلا حِفظ. <b>وصارَ</b>: دُخولُ <c>Paid</c> يُحفَظُ أَوَّلاً، والمَردودُ لا يَقبِضُ شَيئاً.</summary>
    [Fact]
    public void An_advance_into_Paid_refused_by_a_version_conflict_captures_nothing()
    {
        var (world, deals, pay, id) = CardDeal(DealStage.Confirmed);
        ConflictOnNextSave(world, id);
        var saves = world.SaveCalls;

        var res = Now(deals.AdvanceAsync(Slug, id, Buyer, "مُشتَرٍ"));

        Assert.False(res.Ok);
        Assert.Equal(DealsService.ChangedMeanwhile, res.Code);
        Assert.Empty(pay.Calls);
        Assert.Equal(saves, world.SaveCalls);
    }

    /// <summary><b>المُطالَبَةُ قَبلَ الأَثَر، والنَتيجَةُ في حِفظٍ ثانٍ</b> — الإلغاء: حينَ يُنادى الاستِرجاعُ يَكونُ حِفظُ الإلغاءِ قَد
    /// وَقَع (الحِفظُ الأَوَّل)، ثُمَّ تُكتَبُ نَتيجَتُه (الثاني): <c>refund_id</c> و<c>refund_status</c> وسَطرُ <c>refunded</c> بَعدَ <c>cancelled</c>.</summary>
    [Fact]
    public void A_cancel_saves_its_claim_before_the_refund_and_its_outcome_in_a_second_save()
    {
        var (world, deals, pay, id) = CardDeal(DealStage.Booked);
        var saves = world.SaveCalls;
        int? savedWhenRefunding = null;
        pay.During = () => { savedWhenRefunding = world.SaveCalls - saves; return Task.CompletedTask; };

        var res = Now(deals.CancelAsync(Slug, id, new DealCanceller(Buyer, "مُشتَرٍ", IsStoreAdmin: false), "عَدَلتُ"));

        Assert.True(res.Ok, res.Violation?.Code);
        Assert.Equal(1, savedWhenRefunding);
        Assert.Equal(saves + 2, world.SaveCalls);
        Assert.Equal(new[] { "refund pay_1" }, pay.Calls);
        var deal = world.Read<Deal>(id.ToString())!;
        Assert.Equal(DealStatus.Cancelled, deal.Status);
        Assert.Equal("ref_pay_1", deal.Refs["refund_id"]);
        Assert.Equal(nameof(PaymentStatus.Refunded), deal.Refs["refund_status"]);
        Assert.Equal(new[] { "cancelled", "refunded" },
            deal.Timeline.Select(e => e.Action).Where(a => a is "cancelled" or "refunded").ToArray());
        Assert.Same(deal, res.Deal);
    }

    /// <summary>والقَبض: حينَ يُنادى يَكونُ دُخولُ <c>Paid</c> قَد حُفِظ، ثُمَّ تُكتَبُ نَتيجَتُه في حِفظٍ ثانٍ — <c>payment_status</c> وسَطرُ
    /// <c>captured</c> بَعدَ <c>advanced→Paid</c>.</summary>
    [Fact]
    public void An_advance_into_Paid_saves_its_claim_before_the_capture_and_its_outcome_in_a_second_save()
    {
        var (world, deals, pay, id) = CardDeal(DealStage.Confirmed);
        var saves = world.SaveCalls;
        int? savedWhenCapturing = null;
        pay.During = () => { savedWhenCapturing = world.SaveCalls - saves; return Task.CompletedTask; };

        var res = Now(deals.AdvanceAsync(Slug, id, Buyer, "مُشتَرٍ"));

        Assert.True(res.Ok, res.Reason);
        Assert.Equal(1, savedWhenCapturing);
        Assert.Equal(saves + 2, world.SaveCalls);
        Assert.Equal(new[] { "capture pay_1" }, pay.Calls);
        var deal = world.Read<Deal>(id.ToString())!;
        Assert.Equal(DealStage.Paid, deal.Stage);
        Assert.Equal(nameof(PaymentStatus.Captured), deal.Refs["payment_status"]);
        Assert.Equal(new[] { "advanced", "captured" },
            deal.Timeline.Select(e => e.Action).Where(a => a is "advanced" or "captured").ToArray());
        Assert.Same(deal, res.Deal);
    }

    /// <summary>كِتابَةٌ أُخرى تُحفَظُ بَينَ المُطالَبَةِ ونَتيجَتِها ⇒ أَوَّلُ حِفظٍ لِلنَتيجَةِ يُرَدّ، فَتُقرَأُ الصَفقَةُ وتُعادُ — والمَرجِعانِ
    /// قائِمانِ والمُزَوِّدُ نودِيَ مَرَّة. (والعالَمُ يُعيدُ الكائِنَ نَفسَه، فَعَدُّ سَطرِ النَتيجَةِ في البُرهانِ الحَيِّ أَدناه لا هُنا.)</summary>
    [Theory]
    [InlineData("cancel")]
    [InlineData("capture")]
    public void An_outcome_whose_first_save_loses_to_another_write_is_re_applied_on_what_was_written(string effect)
    {
        var (world, deals, pay, id) = CardDeal(effect == "cancel" ? DealStage.Booked : DealStage.Confirmed);
        var saves = world.SaveCalls;
        pay.During = () =>
        {
            var armed = true;
            world.BeforeMember = name =>
            {
                if (name != "SaveChangesAsync" || !armed) return;
                armed = false;
                Now(deals.AttachRefAsync(Slug, id, "probe_note", "ci"));
                throw new JasperFx.ConcurrencyException(typeof(Deal), id);
            };
            return Task.CompletedTask;
        };

        var ok = effect == "cancel"
            ? Now(deals.CancelAsync(Slug, id, new DealCanceller(Buyer, "مُشتَرٍ", IsStoreAdmin: false), "عَدَلتُ")).Ok
            : Now(deals.AdvanceAsync(Slug, id, Buyer, "مُشتَرٍ")).Ok;

        Assert.True(ok);
        Assert.Single(pay.Calls);
        var deal = world.Read<Deal>(id.ToString())!;
        Assert.Equal("ci", deal.Refs["probe_note"]);
        if (effect == "cancel") Assert.Equal("ref_pay_1", deal.Refs["refund_id"]);
        else Assert.Equal(nameof(PaymentStatus.Captured), deal.Refs["payment_status"]);
        Assert.Equal(saves + 3, world.SaveCalls);   // المُطالَبَة، والكاتِبُ الآخَر، والنَتيجَةُ المُعادَة — والمَردودُ لا يُعَدّ
    }

    /// <summary><b>مُزَوِّدٌ رَفَضَ أَو انقَطَع لا يَكسِرُ الحالَةَ المَحفوظَة</b>: الإلغاءُ قائِمٌ و<c>refund_error</c> وسَطرُ <c>refund_failed</c>؛
    /// والدُخولُ قائِمٌ و<c>capture_error</c> وسَطرُ <c>capture_failed</c> — كَما كانا قَبلَ التَرتيب، في الحِفظِ الثاني الآن.</summary>
    [Theory]
    [InlineData("cancel")]
    [InlineData("capture")]
    public void A_provider_failure_after_the_claim_is_recorded_and_the_saved_state_stands(string effect)
    {
        var (world, deals, pay, id) = CardDeal(effect == "cancel" ? DealStage.Booked : DealStage.Confirmed);
        pay.Fail = true;
        var saves = world.SaveCalls;

        var ok = effect == "cancel"
            ? Now(deals.CancelAsync(Slug, id, new DealCanceller(Buyer, "مُشتَرٍ", IsStoreAdmin: false), "عَدَلتُ")).Ok
            : Now(deals.AdvanceAsync(Slug, id, Buyer, "مُشتَرٍ")).Ok;

        Assert.True(ok);
        Assert.Equal(saves + 2, world.SaveCalls);
        var deal = world.Read<Deal>(id.ToString())!;
        if (effect == "cancel")
        {
            Assert.Equal(DealStatus.Cancelled, deal.Status);
            Assert.Equal("refund refused", deal.Refs["refund_error"]);
            Assert.Single(deal.Timeline, e => e.Action == "refund_failed");
            Assert.False(deal.Refs.ContainsKey("refund_id"));
        }
        else
        {
            Assert.Equal(DealStage.Paid, deal.Stage);
            Assert.Equal("capture refused", deal.Refs["capture_error"]);
            Assert.Single(deal.Timeline, e => e.Action == "capture_failed");
            Assert.Equal(nameof(PaymentStatus.Authorized), deal.Refs["payment_status"]);
        }
    }

    /// <summary><b>والحَدّ — بِصَوتٍ لا بِرَمزٍ يَكذِب</b>: نَتيجَةٌ يَغلِبُها التَعارُضُ ثَلاثاً تَصعَدُ بِاستِثناءِ Marten إلى المُنادي (‏500)،
    /// <b>لا</b> <c>deal_changed_meanwhile</c> — فَذاكَ يَقولُ «لَم يُكتَب شَيء» والحالَةُ كُتِبَت والمالُ تَحَرَّك. والمُزَوِّدُ نودِيَ مَرَّة.</summary>
    [Theory]
    [InlineData("cancel")]
    [InlineData("capture")]
    public void An_outcome_that_keeps_losing_gives_up_loudly_and_is_never_reported_as_a_refused_write(string effect)
    {
        var (world, deals, pay, id) = CardDeal(effect == "cancel" ? DealStage.Booked : DealStage.Confirmed);
        var conflicts = 0;
        pay.During = () =>
        {
            world.BeforeMember = name =>
            {
                if (name != "SaveChangesAsync") return;
                conflicts++;
                throw new JasperFx.ConcurrencyException(typeof(Deal), id);
            };
            return Task.CompletedTask;
        };

        Assert.Throws<JasperFx.ConcurrencyException>(() =>
        {
            if (effect == "cancel")
                Now(deals.CancelAsync(Slug, id, new DealCanceller(Buyer, "مُشتَرٍ", IsStoreAdmin: false), "عَدَلتُ"));
            else
                Now(deals.AdvanceAsync(Slug, id, Buyer, "مُشتَرٍ"));
        });
        Assert.Equal(3, conflicts);
        Assert.Single(pay.Calls);
    }

    /// <summary><b>القَبولُ لا يُدخِلُ «دَفع» في أَيِّ نَمَط</b> — فَلا يُناديه القَبضُ بَعدَ المُطالَبَة (تُناديه <c>AdvanceAsync</c> وَحدَها).
    /// يَعُدُّ الأَنماطَ ومَعَها الاحتِياطِيّ، ويَفشَلُ إن كانَ العَدُّ صِفراً؛ ونَمَطٌ يُدخِلُ <c>Paid</c> بَعدَ <c>Offered</c> يُحمِرُّه — فَيُقرَّرُ
    /// يَومَها أَن يُناديه القَبولُ أَيضاً.</summary>
    [Fact]
    public void Accepting_never_enters_Paid_in_any_pattern_so_only_advances_capture_after_their_claim()
    {
        var definitions = DealPatternCatalog.Patterns.Values.Append(DealPatternCatalog.Fallback).ToList();
        var entering = definitions.Where(d => d.Next(DealStage.Offered) == DealStage.Paid).Select(d => d.Pattern).ToList();
        Assert.True(definitions.Count >= 6 && entering.Count == 0,
            $"فُحِصَ {definitions.Count} نَمَطاً؛ يُدخِلُ «دَفع» بَعدَ «عَرض»: {string.Join("، ", entering)}");
    }
}

// ─── البُرهانُ الحَيّ — عَلى Postgres، بِبَوّابَةِ الحِفظِ نَفسِها ─────────────────────────────────────────────
//
// مُبَوَّبٌ بِـWASAYEL_LIVE_PROOF=1 وConnectionStrings__Postgres (عَقدُ `LiveDealConcurrencyProofTests` حَرفاً، ومِنه تَركيبُ المَخزَن
// بِتَسجيلِ الصَفقَةِ الَّذي يُناديه القالِب)، ومُستَأجِرٌ مُؤَقَّتٌ يُمسَحُ بَعدَه — لا مَتجَرٌ مَبذور (‏2026-10-29: كانَ «لا `ashare`»، وسَبَبُه العَرضُ رُفِع؛ والعَزلُ تِقنِيّ). الكاتِبُ الآخَرُ إلحاقُ مَرجِعٍ يُحفَظُ
// بَينَ قِراءَةِ الكاتِبِ المُعَلَّمِ وحِفظِه؛ ثُمَّ تُعادُ المُحاوَلَةُ بِالخِدمَةِ نَفسِها كَما يُعيدُها صاحِبُ النَقرَة.

[Collection(LiveProofCollection.Name)]
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

    /// <summary><b>قُلِبَ بِاسمِه</b> (‏ADR-036 الخُطوَة 2، ‏2026-10-12): كانَ
    /// <c>Today_a_cancel_refused_after_an_attach_has_refunded_and_its_retry_refunds_again</c> — مَقيساً ثَلاثاً عِندَ <c>5e3c156</c>:
    /// «الإلغاء: ok=False code=deal_changed_meanwhile · Active/Booked · المُزَوِّد: [refund auth_live]» ثُمَّ «الإعادَة: ok=True ·
    /// Cancelled/Booked · [refund auth_live, refund auth_live]». <b>وصارَ</b>: المَردودُ لا يَبلُغُ المُزَوِّد، والإعادَةُ تَستَرجِعُ مَرَّةً
    /// ويُكتَبُ <c>refund_id</c> بِسَطرٍ واحِدٍ بَعدَ <c>cancelled</c>.</summary>
    [Fact]
    public async Task A_cancel_refused_after_an_attach_refunds_nothing_and_its_retry_refunds_once()
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
            Assert.Empty(pay.Calls);
            Assert.Equal(DealStatus.Active, between.Status);
            Assert.False(between.Refs.ContainsKey("refund_id"));

            var retry = await new DealsService(store, pay).CancelAsync(slug, deal.Id, by, "عَدَلتُ");
            var final = (await plain.LoadAsync(slug, deal.Id))!;
            Print($"الإعادَة: ok={retry.Ok}", final, pay);

            Assert.True(retry.Ok, retry.Violation?.Code);
            Assert.Equal(new[] { "refund auth_live" }, pay.Calls);
            Assert.Equal(DealStatus.Cancelled, final.Status);
            Assert.Equal("ref_auth_live", final.Refs["refund_id"]);
            Assert.Single(final.Timeline, e => e.Action == "refunded");
            Assert.Equal(new[] { "cancelled", "refunded" },
                final.Timeline.Select(e => e.Action).Where(a => a is "cancelled" or "refunded").ToArray());
        }
        finally { await CleanAsync(store, slug); }
    }

    /// <summary><b>قُلِبَ بِاسمِه</b> (‏ADR-036 الخُطوَة 2، ‏2026-10-12): كانَ
    /// <c>Today_an_advance_into_Paid_refused_after_an_attach_has_captured_and_its_retry_captures_again</c> — مَقيساً ثَلاثاً عِندَ
    /// <c>5e3c156</c>: «الدَفع: ok=False code=deal_changed_meanwhile · Active/Confirmed · [capture auth_live]» ثُمَّ «الإعادَة: ok=True ·
    /// Active/Paid · [capture auth_live, capture auth_live]». <b>وصارَ</b>: المَردودُ لا يَقبِض، والإعادَةُ تَقبِضُ مَرَّةً بِسَطرٍ واحِد.</summary>
    [Fact]
    public async Task An_advance_into_Paid_refused_after_an_attach_captures_nothing_and_its_retry_captures_once()
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
            Assert.Empty(pay.Calls);
            Assert.Equal((DealStatus.Active, DealStage.Confirmed), (between.Status, between.Stage));
            Assert.DoesNotContain(between.Timeline, e => e.Action == "captured");

            var retry = await new DealsService(store, pay).AdvanceAsync(slug, deal.Id, buyer, "مُشتَرٍ");
            var final = (await plain.LoadAsync(slug, deal.Id))!;
            Print($"الإعادَة: ok={retry.Ok}", final, pay);

            Assert.True(retry.Ok, retry.Reason);
            Assert.Equal(new[] { "capture auth_live" }, pay.Calls);
            Assert.Equal(DealStage.Paid, final.Stage);
            Assert.Equal(nameof(PaymentStatus.Captured), final.Refs["payment_status"]);
            Assert.Single(final.Timeline, e => e.Action == "captured");
        }
        finally { await CleanAsync(store, slug); }
    }

    /// <summary><b>النَتيجَةُ تُعادُ عَلى ما كُتِب — حَيّاً</b>: أَثناءَ نِداءِ المُزَوِّدِ (بَعدَ المُطالَبَة) يُلحَقُ مَرجِعٌ ويُحفَظ، فَيَرُدُّ
    /// Marten أَوَّلَ حِفظٍ لِلنَتيجَة؛ تُقرَأُ الصَفقَةُ وتُعادُ — الكِتابَتانِ قائِمَتان، وسَطرُ النَتيجَةِ واحِد، والمُزَوِّدُ نودِيَ مَرَّة.</summary>
    [Theory]
    [InlineData("cancel")]
    [InlineData("capture")]
    public async Task An_outcome_whose_first_save_loses_to_a_write_made_during_the_provider_call_lands_once(string effect)
    {
        if (!Enabled) return;

        await using var store = LiveDealConcurrencyProofTests.BuildStore();
        var slug = "zz-claim-" + Guid.NewGuid().ToString("N")[..8];
        var (buyer, seller) = (Guid.NewGuid(), Guid.NewGuid());
        try
        {
            var plain = new DealsService(store, new DealMoneyClaimFirstTests.RecordingPaymentProvider());
            var deal = await CardDealAsync(plain, slug, buyer, seller, effect == "cancel" ? DealStage.Booked : DealStage.Confirmed);
            var pay = new DealMoneyClaimFirstTests.RecordingPaymentProvider();
            pay.During = () => plain.AttachRefAsync(slug, deal.Id, "probe_note", "live");
            var deals = new DealsService(store, pay);

            var ok = effect == "cancel"
                ? (await deals.CancelAsync(slug, deal.Id, new DealCanceller(buyer, "مُشتَرٍ", IsStoreAdmin: false), "عَدَلتُ")).Ok
                : (await deals.AdvanceAsync(slug, deal.Id, buyer, "مُشتَرٍ")).Ok;
            var final = (await plain.LoadAsync(slug, deal.Id))!;
            Print($"{effect}: ok={ok}", final, pay);

            Assert.True(ok);
            Assert.Single(pay.Calls);
            Assert.Equal("live", final.Refs["probe_note"]);
            Assert.Single(final.Timeline, e => e.Action == (effect == "cancel" ? "refunded" : "captured"));
            if (effect == "cancel")
                Assert.Equal((DealStatus.Cancelled, "ref_auth_live"), (final.Status, final.Refs["refund_id"]));
            else
                Assert.Equal((DealStage.Paid, nameof(PaymentStatus.Captured)), (final.Stage, final.Refs["payment_status"]));
        }
        finally { await CleanAsync(store, slug); }
    }
}
