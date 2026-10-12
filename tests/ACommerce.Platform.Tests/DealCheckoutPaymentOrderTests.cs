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

    /// <summary>بَندُ سَلَّةٍ بِبِطاقَةٍ كَما تَمشيه النُقطَةُ <b>اليَوم</b> — بِتَرتيبِها المُثَبَّتِ مِن المَصدَر: الوِلادَة، ثُمَّ مَرجِعُ المالِك،
    /// ثُمَّ الرَبط، ثُمَّ إذنُ الدَفعِ ومَرجِعاه. و<paramref name="vendorWhileAuthorizing"/> ما يَفعَلُه البائِعُ وقَد صارَت الصَفقَةُ في
    /// قائِمَتِه، أَثناءَ نِداءِ المُزَوِّدِ وقَبلَ أَن يُلحَقَ مَرجِعُه.</summary>
    private static Guid CardCheckoutToday(DealsService deals, IPaymentProvider payments, Action<Guid> vendorWhileAuthorizing)
    {
        var deal = Now(deals.StartAsync(Slug, "marketplace", Buyer, "مُشتَرٍ", Guid.NewGuid(), "سِلعَة", 100m,
            new() { [CheckoutPayMethods.AttributeKey] = CheckoutPayMethods.Card }));
        Now(deals.AttachRefAsync(Slug, deal.Id, DealsService.ListingOwnerRef, Seller.ToString()));
        Now(deals.BindListingOwnerAsync(Slug, deal.Id, Seller.ToString()));
        var pr = Now(payments.AuthorizeAsync(new(100m, "سِلعَة", Buyer.ToString(), "",
            Metadata: new() { ["deal_id"] = deal.Id.ToString() }), $"deal_{deal.Id}"));
        vendorWhileAuthorizing(deal.Id);
        Now(deals.AttachRefAsync(Slug, deal.Id, "payment_id", pr.PaymentId));
        Now(deals.AttachRefAsync(Slug, deal.Id, "payment_status", pr.Status.ToString()));
        return deal.Id;
    }

    /// <summary>
    /// <para><b>اليَوم — يُقلَبُ بِاسمِه في كوميتِ التَرتيب</b>: البائِعُ المَربوطُ يُلغي الطَلَبَ أَثناءَ نِداءِ المُزَوِّد — والإلغاءُ مَقبولٌ
    /// (طَرَفٌ عِندَ <c>Offered</c>) — فَلا يَجِدُ مَرجِعَ دَفعٍ ولا يُستَرجَعُ شَيء؛ ثُمَّ يُلحَقُ <c>payment_id</c> عَلى صَفقَةٍ مُلغاة. المُزَوِّدُ
    /// نودِيَ مَرَّةً واحِدَةً لِلحَجزِ وصِفراً لِلاستِرجاع: إذنٌ مَحجوزٌ لا يُطلِقُه أَحَد.</para>
    /// </summary>
    [Fact]
    public void Today_a_bound_vendors_cancel_while_the_provider_authorizes_refunds_nothing_and_the_payment_lands_on_a_cancelled_deal()
    {
        var (owner, bind, authorize, paymentId, paymentStatus) = Steps();
        Assert.True(owner < bind && bind < authorize && authorize < paymentId && paymentId < paymentStatus,
            $"تَرتيبُ النُقطَةِ تَغَيَّر: owner={owner} bind={bind} authorize={authorize} payment_id={paymentId} payment_status={paymentStatus}");

        var world = new DocWorld();
        var pay = new RecordingPaymentProvider();
        var deals = new DealsService(world.Store, pay);
        DealCancelResult? cancel = null;

        var id = CardCheckoutToday(deals, pay, dealId =>
            cancel = Now(deals.CancelAsync(Slug, dealId, new DealCanceller(Seller, "بائِع", IsStoreAdmin: false), "نَفِدَت الكَمِّيَّة")));

        Assert.True(cancel!.Ok, cancel.Violation?.Code);
        var deal = world.Read<Deal>(id.ToString())!;
        Assert.Equal(DealStatus.Cancelled, deal.Status);
        Assert.Equal("pay_1", deal.Refs["payment_id"]);
        Assert.False(deal.Refs.ContainsKey("refund_id"));
        Assert.DoesNotContain(deal.Timeline, e => e.Action is "refunded" or "refund_failed");
        Assert.Equal(new[] { $"authorize deal_{id}" }, pay.Calls);
    }
}
