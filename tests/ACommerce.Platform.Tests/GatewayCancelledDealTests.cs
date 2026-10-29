using System.Net;
using System.Text.Json;
using ACommerce.Kit.Notifications;
using ACommerce.Kit.Payments;
using ACommerce.Platform.I18n;
using ACommerce.Templates.Customer.Marketplace.Services;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.Abstractions;
using static ACommerce.Platform.Tests.TenantGatewayEndpointsTests;

namespace ACommerce.Platform.Tests;

// ═══ صَفقَةٌ أُلغِيَت وفاتورَتُها مَفتوحَة — صَفحَةُ العَودَةِ وإشعارُ الدَفعِ وفَتحُ الفاتورَة (‏ADR-032 الخُطوَتان 9 و10) ═════════════
//
// **تَوصيفُ «اليَوم» — مُثَبَّتاً قَبلَ قَلبِه** (القاعِدَة ٣؛ ‏2026-10-12، مُراجَعَةُ تَيّارِ S9 الأولى — البَندُ المانِع):
// البائِعُ المَربوطُ عِندَ الوِلادَةِ يُشعَرُ بِالطَلَبِ في إتمامِ الشِراءِ قَبلَ أَيِّ دَفع، ويَرفُضُه عِندَ «عرض» بِبابٍ تَرسُمُه صَفحَتُه
// (`MyDealDetail.razor`)؛ والعَميلُ بِلا عَمَلِيَّةِ إبطالٍ (عَمَلِيَّتانِ لا ثالِثَة، §٨‑أ) فَالفاتورَةُ تَبقى قابِلَةً لِلدَّفع. فَثَلاثَةُ صُفوف:
//
// (١) صَفحَةُ العَودَةِ لِلمُلغاةِ الَّتي لَم تُدفَع تَرسُمُ زِرَّ التَحَقُّقِ ورابِطَ «أَكمِلِ الدَّفعَ عَلى صَفحَةِ البَوّابَة» ونَصَّ «طَلَبُك قائِمٌ» —
//     مُدخَلاتُها الثَلاثَةُ بِالدَوالِّ النَقِيَّةِ الَّتي تُناديها، وصِفرُ قِراءَةٍ لِلحالِ في كودِها؛
// (٢) دَفعٌ يُتَحَقَّقُ مِنه عَلى المُلغاةِ يُكتَبُ ومَعَه «استِرجاعُه مُستَحَقّ» — ثُمَّ يُخبَرُ بائِعُها «دَفَعَ المُشتَري ثَمَنَ طَلَبِه ✓ … افتَحِ
//     الصَّفقَة لِمُتابَعَتِها»، وتَقولُ صَفحَتُها «ثَبَتَ دَفعُك … يُتابِعُه البائِعُ مِن هُنا»؛
// (٣) صَفقَةٌ أَلغاها بائِعُها بَينَ وِلادَتِها وفَتحِ الفاتورَةِ تَدخُلُ الفاتورَةَ بِمَبلَغِها ويُكتَبُ عَلَيها رابِطُها.
//
// **ولا مِفتاحَ حَقيقِيّاً** — مُعالِجُ `MoyasarClientTests` بِرُدودِ الوَثيقَةِ وعالَمُ `TenantGatewayEndpointsTests` بِحَرفِه.
//
// **وقُلِبَت الثَلاثَةُ بِأَسمائِها في كوميتِ الإصلاح** (‏2026-10-12 — ولا يُعادُ حَرفٌ فَوقَ ما سَبَق): `Today_…` ⇐ ما صارَ — (١) الزِرُّ باقٍ
// ولا رابِطَ والنَصُّ «أُلغِيَ» (`GatewayReturn.PayUrlOf` لِلقائِمَةِ وَحدَها، و`GatewayReturn.StateKeyOf`)؛ (٢) الدَفعُ يُكتَبُ ومَعَه سَطرُه
// ولا يُخبَرُ بائِعُ المُلغاة (`DealNotices.PaidRecipient`)، والصَفحَةُ تَقولُ «مُلغًى، واستِرجاعُه عَلى المَتجَر»؛ (٣) لا تُحَمَّلُ إلّا القائِمَة،
// فَيُرَدُّ الفَتحُ بِلا نِداءٍ ولا خَصمٍ وتُلغى الباقِيَة. ومَعَها صُفوفُ الدَوالِّ الثَلاثِ بِمُوجَبٍ وسالِبٍ لِكُلِّ حال، ونَصّاها حَيَّين، ومَصدَرُ الصَفحَة.

public class GatewayCancelledDealTests(ITestOutputHelper output)
{
    private static string Root => ThemeZeroEquivalenceTests.RepoRoot;
    private static string Page()
        => WriteEndpointGuardTests.StripComments(WriteEndpointGuardTests.StripMarkupComments(File.ReadAllText(Path.Combine(
            Root, "libs", "templates", "ACommerce.Templates.Customer.Marketplace", "Components", "Pages", "GatewayPayReturn.razor"))));

    private static Deal Load(World w, Guid id) => w.Docs.Read<Deal>(id.ToString())!;
    private static L Fresh() => new(new HttpContextAccessor());
    private static string Ar(string key) => LocaleCatalog.Text(LocaleCatalog.Arabic, key);

    /// <summary>رابِطُ الصَفحَةِ المُستَضافَةِ كَما يَكتُبُه إتمامُ الشِراء — بِشَكلِ مِثالِ الوَثيقَة.</summary>
    private const string PayUrl = "https://checkout.moyasar.com/invoices/efebc231-7795-4cfd-b69c-980fe4c02c49";
    private const string ReturnUrl = "https://store.example/zz-gateway/pay/return?deal=1";
    private const string VendorReason = "نَفِدَت الكَمِّيَّة";

    /// <summary>البائِعُ المَربوطُ عِندَ الوِلادَةِ يَرفُضُ الطَلَبَ عِندَ «عرض» — البابُ نَفسُه الَّذي تَرسُمُه صَفحَتُه، لا كِتابَةٌ بِاليَد.</summary>
    private static async Task VendorRejects(World w, Guid id)
    {
        var res = await w.Deals.CancelAsync(Slug, id, new DealCanceller(Seller, "بائِع", IsStoreAdmin: false), VendorReason);
        Assert.True(res.Ok);
        Assert.Equal((DealStatus.Cancelled, DealStage.Offered), (Load(w, id).Status, Load(w, id).Stage));
    }

    private static DealNotices Notices(World w)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSignalR();
        var hub = services.BuildServiceProvider().GetRequiredService<IHubContext<ACommerce.Kit.Realtime.Server.RealtimeHub>>();
        return new DealNotices(w.Docs.Store, hub, new WebPushService(new ConfigurationBuilder().Build(), NullLogger<WebPushService>.Instance));
    }

    // ═══ (١) صَفحَةُ العَودَةِ لِلمُلغاةِ الَّتي لَم تُدفَع ═══════════════════════════════════════════════════

    /// <summary><b>قُلِبَ بِاسمِه ‏2026-10-12</b> — كانَ <c>Today_the_return_page_of_a_cancelled_deal_whose_invoice_is_unpaid_draws_the_verify_button_the_pay_link_and_the_text_that_the_order_stands</c>:
    /// الزِرُّ باقٍ (دَفعٌ وَقَعَ يُسَجَّلُ ولا يُبتلَع)، ولا «أَكمِلِ الدَّفعَ»، والنَصُّ «أُلغِيَ … واستِرجاعُه عَلى المَتجَر». والسالِبُ أُختُها القائِمَةُ
    /// بِالفاتورَةِ نَفسِها: رابِطُها ونَصُّها كَما كانا.</summary>
    [Fact]
    public async Task The_return_page_of_a_cancelled_deal_whose_invoice_is_unpaid_keeps_the_verify_button_hides_the_pay_link_and_says_it_was_cancelled()
    {
        var w = await PaidCart(withSeller: true);
        var (id, sibling) = (w.Ids[0], w.Ids[1]);
        await w.Deals.AttachRefAsync(Slug, id, GatewayPayments.InvoiceUrlRef, PayUrl);
        await w.Deals.AttachRefAsync(Slug, sibling, GatewayPayments.InvoiceUrlRef, PayUrl);
        await VendorRejects(w, id);
        var deal = Load(w, id);

        Assert.Null(DealPayAuthorization.Validate(deal, new DealPayer(Buyer)));   // ← الزِرُّ باقٍ
        Assert.False(GatewayReturn.IsConfirmed(deal));
        Assert.Null(GatewayReturn.PayUrlOf(deal));                                // ← لا «أَكمِلِ الدَّفع»
        Assert.Equal("pay.return.cancelled_pending", GatewayReturn.StateKeyOf(deal));

        // والسالِب: أُختُها القائِمَةُ بِالفاتورَةِ نَفسِها — كَما كانَت.
        Assert.Equal(PayUrl, GatewayReturn.PayUrlOf(Load(w, sibling)));
        Assert.Equal("pay.return.pending", GatewayReturn.StateKeyOf(Load(w, sibling)));
        output.WriteLine($"· مُلغاةٌ لَم تُدفَع: الزِّرّ=يُرسَم · الرابِط={GatewayReturn.PayUrlOf(deal) ?? "لا"} · النَصّ={GatewayReturn.StateKeyOf(deal)}");
    }

    // ═══ (٢) دَفعٌ يُتَحَقَّقُ مِنه عَلى المُلغاة ═════════════════════════════════════════════════════════

    /// <summary><b>قُلِبَ بِاسمِه ‏2026-10-12</b> — كانَ <c>Today_a_payment_verified_on_a_cancelled_deal_is_announced_to_its_vendor_as_paid_and_its_return_page_says_the_vendor_follows_it</c>:
    /// الكاتِبُ كَما كانَ (الدَفعُ وسَطرُ «استِرجاعُه مُستَحَقّ»، وفي <c>Confirmed</c>)، والإشعارُ لِبائِعِ القائِمَةِ وَحدَه، والصَفحَةُ «مُلغًى».</summary>
    [Fact]
    public async Task A_payment_verified_on_a_cancelled_deal_is_recorded_with_its_refund_due_tells_its_vendor_nothing_and_its_return_page_says_cancelled()
    {
        var w = await PaidCart(withSeller: true);
        var (cancelled, active) = (w.Ids[0], w.Ids[1]);
        await VendorRejects(w, cancelled);
        w.Moyasar.Then(HttpStatusCode.OK, PaidInvoice(w));   // دَفَعَ المُشتَري الفاتورَةَ المُستَضافَةَ — وهي لا تُبطَل.

        var res = await w.Gateway.VerifyAsync(Slug, new DealPayer(Buyer), cancelled.ToString("N"));

        Assert.True(res.Ok, res.Code);
        Assert.Equal(w.Ids.OrderBy(x => x), res.Confirmed.OrderBy(x => x));   // كُتِبَ عَلى الاثنَين — دَفعٌ وَقَعَ لا يُبتلَع.
        var deal = Load(w, cancelled);
        Assert.Equal(DealStatus.Cancelled, deal.Status);
        Assert.Single(deal.Timeline, e => e.Action == DealEvent.GatewayPaid);
        Assert.Single(deal.Timeline, e => e.Action == DealEvent.GatewayRefundDue);

        var told = await Notices(w).NotifyPaidAsync(Slug, res.Confirmed, Fresh());

        Assert.Equal(1, told);
        var paid = Assert.Single(w.Docs.Stored.OfType<Notification>(), n => n.Type == DealNotices.PaidType);
        Assert.Equal((Seller, $"/{Slug}/deals/{active}"), (paid.UserId, paid.RelatedUrl));

        Assert.True(GatewayReturn.IsConfirmed(deal));
        Assert.Equal("pay.return.cancelled_confirmed", GatewayReturn.StateKeyOf(deal));
        Assert.Equal("pay.return.confirmed", GatewayReturn.StateKeyOf(Load(w, active)));
        output.WriteLine($"· تَحَقُّقٌ عَلى مُلغاة: confirmed={res.Confirmed.Count} · أُشعِرَ={told} · deal_paid لِلمُلغاة=لا · النَصّ={GatewayReturn.StateKeyOf(deal)}");
    }

    // ═══ (٣) الفاتورَةُ لا تُفتَحُ عَلى صَفقَةٍ أُلغِيَت قَبلَ فَتحِها ═══════════════════════════════════════════════

    /// <summary><b>قُلِبَ بِاسمِه ‏2026-10-12</b> — كانَ <c>Today_an_invoice_opens_over_a_deal_cancelled_between_its_birth_and_the_opening_and_writes_its_pay_link_on_it</c>:
    /// لا تُحَمَّلُ إلّا القائِمَة، فَتَنقُصُ القائِمَةُ عَن المُعَرِّفاتِ ويُرَدُّ الفَتحُ بِرَمزِ صَفقَةِ غَيرِه نَفسِه — بِلا نِداءٍ ولا خَصم، والباقِيَةُ
    /// تُلغى بِسَبَبِ القامُوس، والمُلغاةُ بِسَبَبِ بائِعِها لا يُكتَبُ فَوقَه. والمُوجِبُ (سَلَّةٌ قائِمَةٌ كُلُّها تُفتَح) في
    /// <c>GatewayPaymentConfirmTests.Opening_writes_one_invoice_for_the_cart_total_with_every_deal_and_its_links_on_each_deal</c>.</summary>
    [Fact]
    public async Task A_deal_cancelled_before_its_invoice_opens_is_not_invoiced_and_the_rest_of_the_cart_is_cancelled_with_nothing_charged()
    {
        var w = await PaidCart(withSeller: true, invoice: false);
        var (cancelled, rest) = (w.Ids[0], w.Ids[1]);
        await VendorRejects(w, cancelled);
        w.Moyasar.Then(HttpStatusCode.Created, MoyasarClientTests.InvoiceJson("initiated", 15000, metadata: new Dictionary<string, string>()))
                 .Then(HttpStatusCode.OK, MoyasarClientTests.InvoiceJson("initiated", 15000, metadata: GatewayPaymentProof.MetadataFor(w.Ids)));
        var l = Fresh();

        var opened = await w.Gateway.OpenInvoiceAsync(Slug, new DealPayer(Buyer), w.Ids, "مَتجَر", ReturnUrl, l);

        Assert.False(opened.Ok);
        Assert.Equal(MoyasarFailures.Invalid, opened.Code);
        Assert.Empty(w.Moyasar.Requests);
        var deal = Load(w, cancelled);
        Assert.Equal((DealStatus.Cancelled, VendorReason), (deal.Status, deal.CancelReason));
        Assert.False(GatewayPayments.HasInvoice(deal));
        Assert.False(deal.Refs.ContainsKey(GatewayPayments.InvoiceUrlRef));
        var other = Load(w, rest);
        Assert.Equal((DealStatus.Cancelled, l["checkout.payment.cancel_gateway_open_failed"]), (other.Status, other.CancelReason));
        Assert.False(GatewayPayments.HasInvoice(other));
        Assert.Empty(w.Platform.Calls);
        output.WriteLine($"· فَتحٌ عَلى سَلَّةٍ فيها مُلغاة: ok={opened.Ok} code={opened.Code} · نِداءاتُ البَوّابَة={w.Moyasar.Requests.Count}");
    }

    // ═══ (٤) الدَوالُّ الثَلاث — مُوجَبٌ وسالِبٌ لِكُلِّ حال ═══════════════════════════════════════════════

    private static Deal OnInvoice(DealStatus status, bool paid = false)
    {
        var d = new Deal { Id = Guid.NewGuid(), Pattern = "marketplace", Status = status, InitiatorId = Buyer, AmountSar = 150m };
        d.Refs[GatewayPaymentProof.InvoiceRef] = InvoiceId;
        d.Refs[GatewayPayments.InvoiceUrlRef] = PayUrl;
        if (paid)
        {
            d.Refs["payment_id"] = InvoiceId;
            d.Refs["payment_status"] = nameof(PaymentStatus.Captured);
        }
        return d;
    }

    /// <summary><b>«أَكمِلِ الدَّفعَ» لِلقائِمَةِ وَحدَها</b>: المُوجِبُ <c>Active</c>، والسالِبُ كُلُّ حالٍ سِواها بِالرابِطِ نَفسِه عَلَيها.</summary>
    [Theory]
    [InlineData(DealStatus.Active, true)]
    [InlineData(DealStatus.Cancelled, false)]
    [InlineData(DealStatus.Disputed, false)]
    [InlineData(DealStatus.Completed, false)]
    public void The_pay_link_is_drawn_for_an_active_deal_only(DealStatus status, bool drawn)
        => Assert.Equal(drawn ? PayUrl : null, GatewayReturn.PayUrlOf(OnInvoice(status)));

    /// <summary><b>نَصُّ الحالِ بِشَرطِ الكاتِب</b>: «مُلغاة» حَيثُ يَكتُبُ التَأكيدُ سَطرَ «استِرجاعُه مُستَحَقّ» (الإلغاءُ وَحدَه)، وما سِواها نَصّاها
    /// كَما كانا — والنِزاعُ والاكتِمالُ مِنها (لا رابِطَ لَهُما بِالصَفِّ السابِق).</summary>
    [Theory]
    [InlineData(DealStatus.Active, false, "pay.return.pending")]
    [InlineData(DealStatus.Active, true, "pay.return.confirmed")]
    [InlineData(DealStatus.Cancelled, false, "pay.return.cancelled_pending")]
    [InlineData(DealStatus.Cancelled, true, "pay.return.cancelled_confirmed")]
    [InlineData(DealStatus.Disputed, false, "pay.return.pending")]
    [InlineData(DealStatus.Completed, true, "pay.return.confirmed")]
    public void The_return_page_text_follows_the_writer_cancelled_or_not_and_paid_or_not(DealStatus status, bool paid, string key)
        => Assert.Equal(key, GatewayReturn.StateKeyOf(OnInvoice(status, paid)));

    /// <summary><b>إشعارُ الدَفعِ لِبائِعِ القائِمَةِ وَحدَه</b>: المُوجِبُ <c>Active</c> بِمالِكٍ غَيرِ المُشتَري، والسالِبُ كُلُّ حالٍ سِواها بِالمالِكِ نَفسِه.</summary>
    [Theory]
    [InlineData(DealStatus.Active, true)]
    [InlineData(DealStatus.Cancelled, false)]
    [InlineData(DealStatus.Disputed, false)]
    [InlineData(DealStatus.Completed, false)]
    public void The_paid_notice_goes_to_the_vendor_of_an_active_deal_only(DealStatus status, bool told)
    {
        var deal = OnInvoice(status, paid: true);
        deal.Refs[DealsService.ListingOwnerRef] = Seller.ToString();
        deal.CounterpartyId = Seller;
        Assert.Equal(Seller, DealNotices.NewDealRecipient(deal));   // المالِكُ مَوجودٌ في كُلِّ صَفّ — الفَرقُ الحالُ وَحدَها.
        Assert.Equal(told ? Seller : null, DealNotices.PaidRecipient(deal));
    }

    // ═══ (٥) النَصّانِ والصَفحَة ═════════════════════════════════════════════════════════════════════

    /// <summary><b>النَصّانِ الجَديدانِ حَيّانِ ويَقولانِ ما يَقَع</b>: «أُلغِيَ/مُلغًى»، والاستِرجاعُ عَلى المَتجَرِ مِن لَوحَةِ بَوّابَتِه (بِلا مُدَّةٍ ولا
    /// وَعدٍ لَم يُقَرَّر — «ب» S9·٨)؛ ولا «قائِمٌ» ولا «يُتابِعُه البائِعُ». والعَدّادُ يُطبَع.</summary>
    [Fact]
    public void The_cancelled_texts_are_live_and_say_cancelled_and_the_refund_from_the_stores_gateway_dashboard()
    {
        var checkedCount = 0;
        foreach (var (key, cancelled) in new[] { ("pay.return.cancelled_pending", "أُلغِيَ"), ("pay.return.cancelled_confirmed", "مُلغًى") })
        {
            var text = Ar(key);
            Assert.NotEqual(key, text);
            Assert.False(LocaleCatalog.IsPlaceholder(text), $"‏{key} نائِب.");
            Assert.Contains(cancelled, text, StringComparison.Ordinal);
            Assert.Contains("لَوحَةِ بَوّابَتِه", text, StringComparison.Ordinal);
            Assert.DoesNotContain("قائِمٌ", text, StringComparison.Ordinal);
            Assert.DoesNotContain("يُتابِعُه البائِعُ", text, StringComparison.Ordinal);
            checkedCount++;
        }
        output.WriteLine($"· نُصوصُ المُلغاةِ عَلى صَفحَةِ العَودَة: {checkedCount} مِفتاحاً حَيّاً.");
        Assert.Equal(2, checkedCount);
        Assert.Contains("«تَحَقَّق مِن الدَّفع»", Ar("pay.return.cancelled_pending"), StringComparison.Ordinal);
        Assert.Contains(Ar("pay.return.verify"), Ar("pay.return.cancelled_pending"), StringComparison.Ordinal);
    }

    /// <summary><b>الصَفحَةُ لا تَرسُمُ نَصَّ حالٍ إلّا بِالدالَّة</b>: الفَرعانِ يَقرَآنِ <c>stateKey</c> وَحدَه، ولا مِفتاحَ حالٍ حَرفِيّاً في كودِها —
    /// فَلا يَعودُ فَرعٌ يَقولُ «قائِمٌ» لِمُلغًى؛ والرابِطُ بِشَرطِه كَما كان.</summary>
    [Fact]
    public void The_return_page_draws_every_state_text_through_one_function()
    {
        var page = Page();
        Assert.Contains("stateKey = GatewayReturn.StateKeyOf(deal);", page, StringComparison.Ordinal);
        Assert.Equal(2, page.Split("<p>@L.Markup(stateKey)</p>").Length - 1);
        foreach (var literal in new[] { "pay.return.pending", "pay.return.confirmed", "pay.return.cancelled_pending", "pay.return.cancelled_confirmed" })
            Assert.DoesNotContain($"\"{literal}\"", page, StringComparison.Ordinal);
        // (‏2026-10-29، مُراجَعَةُ تَيّارِ S3 الأولى — ولا يُعادُ حَرفٌ فَوقَه: والرابِطُ بِالسّاعَة — لا رابِطَ بَعدَ انتِهاءِ المُهلَة، `GatewayReturn.WindowClosed`.)
        Assert.Contains("payUrl = GatewayReturn.PayUrlOf(deal, now);", page, StringComparison.Ordinal);
        Assert.Contains("@if (payUrl is not null)", page, StringComparison.Ordinal);
    }
}
