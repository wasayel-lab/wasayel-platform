using ACommerce.Kit.Payments;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using Xunit;

namespace ACommerce.Platform.Tests;

// ═══ مَرجِعُ إذنِ الدَفعِ قَبلَ أَن يَرى البائِعُ الطَلَب — تَرتيبُ `/checkout/submit` (‏ADR-036 §٨) ═══════════
//
// مُراجَعَةُ تَيّارِ S6a الأولى (‏2026-10-12): نُقطَةُ إتمامِ الشِراءِ تُولِدُ الصَفقَة، ثُمَّ تُلحِقُ مَرجِعَ مالِكِ الإعلانِ وتَربُطُه
// طَرَفاً (‏ADR-033 §٦‑ب‑١)، **ثُمَّ** تَحجِزُ المَبلَغَ عِندَ المُزَوِّدِ وتُلحِقُ `payment_id` و`payment_status`. وبَعدَ الرَبطِ الصَفقَةُ في
// قائِمَةِ البائِعِ وفي الـAPI، وهو طَرَفٌ يَملِكُ الإلغاءَ عِندَ `Offered` (أُبقِيَ وثُبِّتَ في `c6dc056`). فَإلغاؤُه أَثناءَ نِداءِ
// المُزَوِّدِ يَجِدُ صَفقَةً بِلا مَرجِعِ دَفعٍ فَلا يُستَرجَعُ شَيء (`CancelAsync` تَستَرجِعُ ما في `Refs["payment_id"]` وَحدَه)، ثُمَّ يُلحَقُ
// المَرجِعُ — ولا يَضيعُ مُنذُ `dfa4871` — عَلى صَفقَةٍ مُلغاة: إذنٌ مَحجوزٌ عِندَ المُزَوِّدِ لا يُقبَضُ ولا يُطلِقُه أَحَد. تَرتيبٌ لا تَعارُض.
//
// **والقِياسُ بِالخِدمَةِ الحَقيقِيَّةِ** في عالَمِ الوَثائِقِ (`EndpointDocWorld.cs`) ومُزَوِّدٍ يُسَجِّلُ ما نودِيَ بِه — بِتَرتيبِ النُقطَةِ
// مُثَبَّتاً مِن مَصدَرِها، فَلا يَقيسُ الصَفُّ نَموذَجاً انجَرَف. صَفُّ «اليَوم» يُودَعُ وَحدَه (القاعِدَة ٣) ويُقلَبُ بِاسمِه في كوميتِ
// التَرتيب.
//
// **ثُمَّ قُلِبَ بِاسمِه** (‏ADR-036 §٨، ‏2026-10-12 — ولا يُعادُ حَرفٌ فَوقَه): الحَجزُ ومَرجِعاه قَبلَ مَرجِعِ المالِكِ والرَبط، فَأَوَّلُ ما
// يَستَطيعُه البائِعُ — إلغاءً أَو قَبولاً — يَجِدُ مَرجِعَ الدَفعِ عَلى الصَفقَة.

public class DealCheckoutPaymentOrderTests
{
    private const string Slug = "zz-checkout-order";
    private static readonly Guid Buyer  = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
    private static readonly Guid Seller = Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb");

    private static string RepoRoot => ThemeZeroEquivalenceTests.RepoRoot;
    private const string Template = "libs/templates/ACommerce.Templates.Customer.Marketplace";

    /// <summary>مُزَوِّدٌ يُسَجِّلُ كُلَّ نِداءٍ بِتَرتيبِه: يَحجِزُ بِمُعَرِّفٍ مُتَسَلسِل ويَستَرجِع؛ وما سِواهُما يَرمي — فَإن مَسَّه المَشيُ
    /// أَحمَرَّ الصَفّ.</summary>
    private sealed class RecordingPaymentProvider : IPaymentProvider
    {
        public List<string> Calls { get; } = new();
        public string ProviderName => "recording";

        public Task<PaymentResult> AuthorizeAsync(PaymentRequest req, string idempotencyKey, CancellationToken ct = default)
        {
            Calls.Add($"authorize {idempotencyKey}");
            return Task.FromResult(new PaymentResult($"pay_{Calls.Count}", PaymentStatus.Authorized, req.AmountSar));
        }

        public Task<PaymentResult> RefundAsync(string paymentId, decimal amount, string reason, CancellationToken ct = default)
        {
            Calls.Add($"refund {paymentId}");
            return Task.FromResult(new PaymentResult($"ref_{paymentId}", PaymentStatus.Refunded, amount));
        }

        private static InvalidOperationException Untouched() => new("الإلغاءُ عِندَ `Offered` لا يَقبِضُ ولا يَشتَرِك.");
        public Task<PaymentResult> CaptureAsync(string paymentId, decimal? amount = null, CancellationToken ct = default) => throw Untouched();
        public Task<SubscriptionResult> CreateSubscriptionAsync(SubscriptionRequest req, string idempotencyKey, CancellationToken ct = default) => throw Untouched();
        public Task<bool> CancelSubscriptionAsync(string subscriptionId, CancellationToken ct = default) => throw Untouched();
        public Task<Invoice?> GetInvoiceAsync(string paymentId, CancellationToken ct = default) => throw Untouched();
    }

    /// <summary>عالَمُ الوَثائِقِ يُتِمُّ كُلَّ انتِظارٍ في مَكانِه — وإن لَم يَكتَمِل فَالأَداةُ تَكذِب.</summary>
    private static T Now<T>(Task<T> task)
    {
        Assert.True(task.IsCompleted, "نِداءٌ لَم يَكتَمِل في عالَمٍ مُتَزامِن — الأَداةُ تَكذِب.");
        return task.GetAwaiter().GetResult();
    }

    /// <summary>جِسمُ نُقطَةِ إتمامِ الشِراءِ بِلا تَعليقات.</summary>
    private static string CheckoutEndpoint()
    {
        var path = Path.Combine(RepoRoot, Template, "MarketplaceTemplateExtensions.cs");
        Assert.True(File.Exists(path), $"مَصدَرٌ مَفقود: {path} — الأَداةُ عَمياء.");
        var text = File.ReadAllText(path);
        var start = text.IndexOf("\"/{slug}/checkout/submit\"", StringComparison.Ordinal);
        Assert.True(start > 0, "نُقطَةُ إتمامِ الشِراءِ غَيرُ مَوجودَة — الأَداةُ عَمياء.");
        var end = text.IndexOf(".RequireStoreWritable();", start, StringComparison.Ordinal);
        Assert.True(end > start, "خاتِمَةُ النُقطَةِ غَيرُ مَوجودَة — الأَداةُ عَمياء.");
        return WriteEndpointGuardTests.StripComments(text.Substring(start, end - start));
    }

    /// <summary>مَواضِعُ الخُطُواتِ الخَمسِ في جِسمِ النُقطَة — كُلٌّ بِحَرفِه، وغِيابُ أَيٍّ مِنها أَداةٌ عَمياء.</summary>
    private static (int Owner, int Bind, int Authorize, int PaymentId, int PaymentStatus) Steps()
    {
        var submit = CheckoutEndpoint();
        int At(string needle)
        {
            var at = submit.IndexOf(needle, StringComparison.Ordinal);
            Assert.True(at > 0, $"‏`{needle}` غَيرُ مَوجودٍ في `/checkout/submit` — الأَداةُ عَمياء.");
            Assert.Equal(at, submit.LastIndexOf(needle, StringComparison.Ordinal));
            return at;
        }
        return (At("await deals.AttachRefAsync(slug, deal.Id, \"listing_owner\", oid);"),
                At("await deals.BindListingOwnerAsync(slug, deal.Id, oid);"),
                At("var pr = await payments.AuthorizeAsync(new("),
                At("await deals.AttachRefAsync(slug, deal.Id, \"payment_id\",     pr.PaymentId);"),
                At("await deals.AttachRefAsync(slug, deal.Id, \"payment_status\", pr.Status.ToString());"));
    }

    /// <summary>بَندُ سَلَّةٍ بِبِطاقَةٍ كَما تَمشيه النُقطَة — بِتَرتيبِها المُثَبَّتِ مِن المَصدَر: الوِلادَة، ثُمَّ إذنُ الدَفعِ ومَرجِعاه،
    /// ثُمَّ مَرجِعُ المالِكِ والرَبط. و<paramref name="vendorAfterBind"/> أَوَّلُ ما يَستَطيعُه البائِع: بَعدَ أَن صارَت الصَفقَةُ في قائِمَتِه.
    /// (وكانَ اسمُه <c>CardCheckoutToday</c> بِالتَرتيبِ القَديم — الرَبطُ قَبلَ الحَجز، والبائِعُ يَعمَلُ أَثناءَ نِداءِ المُزَوِّد.)</summary>
    private static Guid CardCheckout(DealsService deals, IPaymentProvider payments, Action<Guid> vendorAfterBind)
    {
        var deal = Now(deals.StartAsync(Slug, "marketplace", Buyer, "مُشتَرٍ", Guid.NewGuid(), "سِلعَة", 100m,
            new() { [CheckoutPayMethods.AttributeKey] = CheckoutPayMethods.Card }));
        var pr = Now(payments.AuthorizeAsync(new(100m, "سِلعَة", Buyer.ToString(), "",
            Metadata: new() { ["deal_id"] = deal.Id.ToString() }), $"deal_{deal.Id}"));
        Now(deals.AttachRefAsync(Slug, deal.Id, "payment_id", pr.PaymentId));
        Now(deals.AttachRefAsync(Slug, deal.Id, "payment_status", pr.Status.ToString()));
        Now(deals.AttachRefAsync(Slug, deal.Id, DealsService.ListingOwnerRef, Seller.ToString()));
        Now(deals.BindListingOwnerAsync(Slug, deal.Id, Seller.ToString()));
        vendorAfterBind(deal.Id);
        return deal.Id;
    }

    /// <summary>
    /// <para><b>قُلِبَ بِاسمِه</b> (‏ADR-036 §٨، ‏2026-10-12): كانَ
    /// <c>Today_a_bound_vendors_cancel_while_the_provider_authorizes_refunds_nothing_and_the_payment_lands_on_a_cancelled_deal</c>
    /// (مُودَعٌ في <c>0257551</c>) — الرَبطُ قَبلَ الحَجز، فَإلغاءُ البائِعِ أَثناءَ نِداءِ المُزَوِّدِ مَقبولٌ بِلا استِرجاع، و<c>payment_id = pay_1</c>
    /// عَلى صَفقَةٍ مُلغاة، والمُزَوِّدُ نودِيَ لِلحَجزِ وَحدَه. <b>وصارَ الحَجزُ ومَرجِعاه قَبلَ مَرجِعِ المالِكِ والرَبط</b>: أَوَّلُ لَحظَةٍ
    /// يَستَطيعُ فيها البائِعُ الإلغاءَ يَجِدُ المَرجِعَ — فَيُستَرجَعُ الإذنُ (<c>refund pay_1</c>) ويُكتَبُ <c>refund_id</c> وسَطرُ <c>refunded</c>.</para>
    /// </summary>
    [Fact]
    public void The_payment_refs_land_before_the_vendor_is_bound_so_his_first_possible_cancel_refunds_the_authorization()
    {
        var (owner, bind, authorize, paymentId, paymentStatus) = Steps();
        Assert.True(authorize < paymentId && paymentId < paymentStatus && paymentStatus < owner && owner < bind,
            $"تَرتيبُ النُقطَة: authorize={authorize} payment_id={paymentId} payment_status={paymentStatus} owner={owner} bind={bind}");

        var world = new DocWorld();
        var pay = new RecordingPaymentProvider();
        var deals = new DealsService(world.Store, pay);
        DealCancelResult? cancel = null;

        var id = CardCheckout(deals, pay, dealId =>
            cancel = Now(deals.CancelAsync(Slug, dealId, new DealCanceller(Seller, "بائِع", IsStoreAdmin: false), "نَفِدَت الكَمِّيَّة")));

        Assert.True(cancel!.Ok, cancel.Violation?.Code);
        var deal = world.Read<Deal>(id.ToString())!;
        Assert.Equal(DealStatus.Cancelled, deal.Status);
        Assert.Equal("pay_1", deal.Refs["payment_id"]);
        Assert.Equal("ref_pay_1", deal.Refs["refund_id"]);
        Assert.Equal(nameof(PaymentStatus.Refunded), deal.Refs["refund_status"]);
        Assert.Single(deal.Timeline, e => e.Action == "refunded");
        Assert.Equal(new[] { $"authorize deal_{id}", "refund pay_1" }, pay.Calls);
    }

    /// <summary><b>والمُوجِبُ الآخَر</b>: بائِعٌ يَقبَلُ في أَوَّلِ لَحظَةٍ يَستَطيعُها يَجِدُ مَرجِعَ الدَفعِ عَلى الصَفقَة — فَما يَقبِضُه
    /// المُشتَري عِندَ <c>Paid</c> مَربوطٌ بِإذنِه، ولا يَمَسُّ القَبولُ المُزَوِّد.</summary>
    [Fact]
    public void A_vendor_who_accepts_at_his_first_possible_moment_finds_the_payment_ref_already_on_the_deal()
    {
        var world = new DocWorld();
        var pay = new RecordingPaymentProvider();
        var deals = new DealsService(world.Store, pay);
        DealAcceptResult? accept = null;

        var id = CardCheckout(deals, pay, dealId => accept = Now(deals.AcceptAsync(Slug, dealId, new DealAcceptor(Seller, "بائِع"))));

        Assert.True(accept!.Ok, accept.Violation?.Code);
        var deal = world.Read<Deal>(id.ToString())!;
        Assert.Equal((DealStatus.Active, DealStage.Booked), (deal.Status, deal.Stage));
        Assert.Equal("pay_1", deal.Refs["payment_id"]);
        Assert.Equal(nameof(PaymentStatus.Authorized), deal.Refs["payment_status"]);
        Assert.Equal(new[] { $"authorize deal_{id}" }, pay.Calls);
    }
}
