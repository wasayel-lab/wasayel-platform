using System.Net;
using System.Text.Json;
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

// ═══ تَأكيدُ دَفعِ البَوّابَةِ وإتمامُ الشِراءِ بِالبِطاقَة — ‏ADR-032 §٢‑هـ الخُطوَة 10 ═════════════════════════════
//
// **ما يُقاسُ هُنا**: (١) الكاتِبُ `ConfirmGatewayPaymentAsync` — مَراجِعُ الدَفعِ وسَطرٌ لِكُلِّ صَفقَة، ومَرَّةً لا مَرَّتَين، ولا يَدهَسُ دَفعاً
// قُبِض؛ (٢) القَبضُ عِندَ «دَفع» يَتَجاوَزُ ما قُبِض؛ (٣) صَفقَةُ فاتورَةٍ لَم تُدفَع لا تَدخُلُ «دَفع» (`deal_awaits_payment`) بِأَيِّ باب؛
// (٤) التَحَقُّقُ يُدخِلُ «دَفع» ما يَقِفُ قَبلَها بِمُشتَريها فاعِلاً — لا قَفزَ ولا تَجاوُزَ لِلفاعِل؛ (٥) إلغاءُ ما قَبَضَته البَوّابَةُ لا يُنادي
// مُزَوِّدَ النُسخَة؛ (٦) الفاتورَةُ الواحِدَةُ لِلسَلَّة — وما لَم تُفتَح يُلغى بِلا خَصم؛ (٧) إشعارُ البائِعِ مَرَّة؛ (٨) النُصوصُ والصَفَحاتُ والنُقطَة.
// **ولا مِفتاحَ حَقيقِيّاً** — مُعالِجُ `MoyasarClientTests` بِرُدودِ الوَثيقَة، والبُرهانُ الحَيُّ في سِجِلِّ المالِكِ «ج».

public class GatewayPaymentConfirmTests(ITestOutputHelper output)
{
    private static string Root => ThemeZeroEquivalenceTests.RepoRoot;
    private static string Template(params string[] parts)
        => File.ReadAllText(Path.Combine(new[] { Root, "libs", "templates", "ACommerce.Templates.Customer.Marketplace" }.Concat(parts).ToArray()));
    private static string Code(params string[] parts)
        => WriteEndpointGuardTests.StripComments(WriteEndpointGuardTests.StripMarkupComments(Template(parts)));

    private static Deal Load(World w, Guid id) => w.Docs.Read<Deal>(id.ToString())!;

    /// <summary>دَفعٌ ثَبَتَ لِصَفَقاتِ العالَم — مِن البَوّابَةِ نَفسِها لا مَبنِيّاً بِاليَد (المُنشِئُ داخِلِيّ).</summary>
    private static ProvenGatewayPayment Proven(World w)
    {
        var deals = w.Ids.Select(id => Load(w, id)).ToArray();
        var admitted = GatewayPaymentProof.Gate(
            new MoyasarInvoice(InvoiceId, "paid", 15000, "SAR", null, GatewayPaymentProof.MetadataFor(w.Ids)), deals);
        Assert.Null(admitted.Violation);
        return admitted.Payment!;
    }

    /// <summary>صَفقَةٌ عِندَ «تأكيد»: يَقبَلُها البائِعُ ثُمَّ يُؤَكِّدُها — المُحَرِّكُ نَفسُه، لا كِتابَةٌ بِاليَد.</summary>
    private static async Task ToConfirmed(World w, Guid id)
    {
        Assert.True((await w.Deals.AcceptAsync(Slug, id, new DealAcceptor(Seller, "بائِع"))).Ok);
        Assert.True((await w.Deals.AdvanceAsync(Slug, id, Seller, "بائِع")).Ok);
        Assert.Equal(DealStage.Confirmed, Load(w, id).Stage);
        Assert.Equal(DealStage.Paid, DealsPolicy.Next(Load(w, id)));
    }

    // ═══ ١) الكاتِب ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Confirm_writes_the_payment_and_one_line_on_every_deal_and_a_second_confirm_writes_nothing()
    {
        var w = await PaidCart();
        var proven = Proven(w);

        var first = await w.Deals.ConfirmGatewayPaymentAsync(Slug, proven);

        Assert.Equal(w.Ids.OrderBy(x => x), first.Select(d => d.Id).OrderBy(x => x));
        foreach (var id in w.Ids)
        {
            var deal = Load(w, id);
            Assert.Equal((InvoiceId, nameof(PaymentStatus.Captured)), (deal.Refs["payment_id"], deal.Refs["payment_status"]));
            var line = Assert.Single(deal.Timeline, e => e.Action == DealEvent.GatewayPaid);
            Assert.Equal((Buyer, InvoiceId), (line.ActorId!.Value, line.Note));
            Assert.DoesNotContain(deal.Timeline, e => e.Action == DealEvent.GatewayRefundDue);
            Assert.True(DealsService.IsCaptured(deal));
            Assert.True(GatewayPayments.CollectedByGateway(deal));
        }

        var (saves, stored) = (w.Docs.SaveCalls, w.Docs.Stored.Count);
        var second = await w.Deals.ConfirmGatewayPaymentAsync(Slug, proven);
        Assert.Empty(second);
        Assert.Equal((saves, stored), (w.Docs.SaveCalls, w.Docs.Stored.Count));
        Assert.Empty(w.Platform.Calls);
    }

    /// <summary><b>ولا يُدهَسُ دَفعٌ قُبِض</b>: صَفقَةٌ «Captured» بِمَرجِعٍ آخَرَ لا يُكتَبُ عَلَيها شَيء ولا تُعادُ؛ والأُخرى تُكتَب.</summary>
    [Fact]
    public async Task Confirm_never_overwrites_a_deal_already_captured()
    {
        var w = await PaidCart();
        var proven = Proven(w);
        var captured = Load(w, w.Ids[0]);
        captured.Refs["payment_id"] = "pay_other";
        captured.Refs["payment_status"] = nameof(PaymentStatus.Captured);

        var written = await w.Deals.ConfirmGatewayPaymentAsync(Slug, proven);

        Assert.Equal(new[] { w.Ids[1] }, written.Select(d => d.Id).ToArray());
        Assert.Equal("pay_other", Load(w, w.Ids[0]).Refs["payment_id"]);
        Assert.DoesNotContain(Load(w, w.Ids[0]).Timeline, e => e.Action == DealEvent.GatewayPaid);
        Assert.False(GatewayPayments.CollectedByGateway(Load(w, w.Ids[0])));
    }

    /// <summary><b>ودَفعٌ وَقَعَ عَلى صَفقَةٍ أُلغِيَت يُكتَبُ ولا يُبتلَع</b> — ومَعَه سَطرُ «استِرجاعُه مُستَحَقٌّ عِندَ البَوّابَة».</summary>
    [Fact]
    public async Task Confirm_on_a_cancelled_deal_records_the_payment_and_the_refund_due()
    {
        var w = await PaidCart();
        var proven = Proven(w);
        Assert.True((await w.Deals.CancelAsync(Slug, w.Ids[0], new DealCanceller(Buyer, "مُشتَرٍ", IsStoreAdmin: false), "تَغَيَّرَ رَأيي")).Ok);

        await w.Deals.ConfirmGatewayPaymentAsync(Slug, proven);

        var deal = Load(w, w.Ids[0]);
        Assert.Equal(DealStatus.Cancelled, deal.Status);
        Assert.Equal(InvoiceId, deal.Refs["payment_id"]);
        Assert.Single(deal.Timeline, e => e.Action == DealEvent.GatewayPaid);
        Assert.Single(deal.Timeline, e => e.Action == DealEvent.GatewayRefundDue);
        Assert.DoesNotContain(Load(w, w.Ids[1]).Timeline, e => e.Action == DealEvent.GatewayRefundDue);
        Assert.Empty(w.Platform.Calls);
    }

    // ═══ ٢) القَبضُ يَتَجاوَزُ ما قُبِض — وحارِسُ «دَفع» ═══════════════════════════════════════════

    /// <summary><b>مُوجِبٌ وسالِب</b>: صَفقَةُ بَوّابَةٍ قُبِضَت تَدخُلُ «دَفع» بِالمُشتَري <b>بِصِفرِ نِداءِ قَبض</b>؛ وصَفقَةُ إذنٍ مِن مُزَوِّدِ النُسخَةِ
    /// (‏Authorized) تُقبَضُ مَرَّةً كَما كانَت.</summary>
    [Fact]
    public async Task A_captured_deal_enters_Paid_without_a_capture_and_an_authorized_one_is_captured_once()
    {
        var w = await PaidCart(withSeller: true);
        await w.Deals.ConfirmGatewayPaymentAsync(Slug, Proven(w));
        await ToConfirmed(w, w.Ids[0]);

        var paid = await w.Deals.AdvanceAsync(Slug, w.Ids[0], Buyer, "مُشتَرٍ");
        Assert.True(paid.Ok, paid.Reason);
        Assert.Equal(DealStage.Paid, Load(w, w.Ids[0]).Stage);
        Assert.Empty(w.Platform.Calls);

        // والسالِب: صَفقَةٌ بِإذنِ مُزَوِّدِ النُسخَةِ بِلا فاتورَة — تُقبَضُ كَما كانَت.
        var other = Load(w, w.Ids[1]);
        other.Refs.Remove(GatewayPaymentProof.InvoiceRef);
        other.Refs["payment_id"] = "pay_1";
        other.Refs["payment_status"] = nameof(PaymentStatus.Authorized);
        await ToConfirmed(w, w.Ids[1]);
        Assert.True((await w.Deals.AdvanceAsync(Slug, w.Ids[1], Buyer, "مُشتَرٍ")).Ok);
        Assert.Equal(new[] { "capture pay_1" }, w.Platform.Calls);
    }

    /// <summary><b>فاتورَةٌ لَم تُدفَع لا تُدخِلُ «دَفع»</b> — بابُ <c>/advance</c> نَفسُه يُرَدُّ بِرَمزِه قَبلَ أَيِّ كِتابَة، ولِلرَمزِ نَصٌّ حَيّ؛
    /// وكانَ القَبضُ يَتَجاوَزُ صَفقَةً بِلا مَرجِعِ دَفعٍ فَتَدخُلُها بِلا مال.</summary>
    [Fact]
    public async Task A_card_deal_whose_invoice_is_unpaid_cannot_enter_Paid_by_any_door()
    {
        var w = await PaidCart(withSeller: true);
        await ToConfirmed(w, w.Ids[0]);
        Assert.True(GatewayPayments.AwaitsPayment(Load(w, w.Ids[0])));
        var saves = w.Docs.SaveCalls;

        var res = await w.Deals.AdvanceAsync(Slug, w.Ids[0], Buyer, "مُشتَرٍ");

        Assert.False(res.Ok);
        Assert.Equal(GatewayPayments.DealAwaitsPayment, res.Code);
        Assert.Equal("deals.detail.err_deal_awaits_payment", DealRefusalTexts.KeyFor(res.Code));
        Assert.Equal(DealStage.Confirmed, Load(w, w.Ids[0]).Stage);
        Assert.Equal(saves, w.Docs.SaveCalls);
        Assert.Empty(w.Platform.Calls);
    }

    /// <summary>جَدوَلُ <see cref="GatewayPayments.AwaitsPayment"/> — فاتورَةٌ × قَبضٌ × تالٍ «دَفع»: لا يَصدُقُ إلّا بِالأَوَّلِ والثالِثِ وغِيابِ الثاني.</summary>
    [Theory]
    [InlineData(true,  false, DealStage.Confirmed, true)]
    [InlineData(false, false, DealStage.Confirmed, false)]
    [InlineData(true,  true,  DealStage.Confirmed, false)]
    [InlineData(true,  false, DealStage.Booked,    false)]
    [InlineData(true,  false, DealStage.Paid,      false)]
    public void Awaiting_payment_is_an_invoice_an_uncaptured_deal_and_Paid_next(bool invoice, bool captured, DealStage stage, bool awaits)
    {
        var deal = new Deal { Pattern = "marketplace", Stage = stage, Attributes = { [CheckoutPayMethods.AttributeKey] = CheckoutPayMethods.Card } };
        if (invoice) deal.Refs[GatewayPaymentProof.InvoiceRef] = InvoiceId;
        if (captured) deal.Refs["payment_status"] = nameof(PaymentStatus.Captured);
        Assert.Equal(awaits, GatewayPayments.AwaitsPayment(deal));
    }

    // ═══ ٣) التَحَقُّقُ يُدخِلُ «دَفع» بِالمُشتَري — لا قَفز ═══════════════════════════════════════════

    [Fact]
    public async Task Verify_moves_a_deal_standing_before_Paid_into_Paid_as_its_buyer_and_leaves_earlier_ones()
    {
        var w = await PaidCart(withSeller: true);
        await ToConfirmed(w, w.Ids[0]);
        w.Moyasar.Then(HttpStatusCode.OK, PaidInvoice(w));

        var res = await w.Gateway.VerifyAsync(Slug, new DealPayer(Buyer), w.Ids[1].ToString("N"));

        Assert.True(res.Ok, res.Code);
        var paid = Load(w, w.Ids[0]);
        Assert.Equal(DealStage.Paid, paid.Stage);
        var move = Assert.Single(paid.Timeline, e => e.Action == "advanced" && e.StageAfter == DealStage.Paid);
        Assert.Equal((Buyer, DealStage.Confirmed), (move.ActorId!.Value, move.StageBefore));
        Assert.Equal(DealStage.Offered, Load(w, w.Ids[1]).Stage);   // رُبِطَ بائِعُها ولَم يَقبَل — تَبقى حَتّى تَبلُغَ ما قَبلَ «دَفع».
        Assert.True(DealsService.IsCaptured(Load(w, w.Ids[1])));      // ودَفعُها مَكتوب: يُدخِلُها المُشتَري حينَ تَبلُغُ «تأكيد».
        Assert.Empty(w.Platform.Calls);
    }

    // ═══ ٤) الإلغاءُ لا يُنادي مُزَوِّدَ النُسخَةِ عَن مالِ البَوّابَة ═══════════════════════════════════════

    [Fact]
    public async Task Cancelling_a_deal_paid_at_the_gateway_calls_no_platform_refund_and_records_the_refund_due()
    {
        var w = await PaidCart();
        await w.Deals.ConfirmGatewayPaymentAsync(Slug, Proven(w));

        var res = await w.Deals.CancelAsync(Slug, w.Ids[0], new DealCanceller(Buyer, "مُشتَرٍ", IsStoreAdmin: false), "لا أُريد");

        Assert.True(res.Ok);
        Assert.Empty(w.Platform.Calls);
        var deal = Load(w, w.Ids[0]);
        Assert.Single(deal.Timeline, e => e.Action == DealEvent.GatewayRefundDue);
        Assert.False(deal.Refs.ContainsKey("refund_id"));

        // والسالِب: مَرجِعُ مُزَوِّدِ النُسخَةِ بِلا فاتورَةٍ يُستَرجَعُ كَما كان.
        var platformPaid = Load(w, w.Ids[1]);
        platformPaid.Refs.Remove(GatewayPaymentProof.InvoiceRef);
        platformPaid.Refs["payment_id"] = "pay_1";
        Assert.True((await w.Deals.CancelAsync(Slug, w.Ids[1], new DealCanceller(Buyer, "مُشتَرٍ", IsStoreAdmin: false), "لا")).Ok);
        Assert.Equal(new[] { "refund pay_1" }, w.Platform.Calls);
    }

    // ═══ ٥) الفاتورَةُ الواحِدَةُ لِلسَلَّة ═════════════════════════════════════════════════════════════

    private static L Fresh() => new(new HttpContextAccessor());
    private const string ReturnUrl = "https://store.example/zz-gateway/pay/return?deal=1";

    [Fact]
    public async Task Opening_writes_one_invoice_for_the_cart_total_with_every_deal_and_its_links_on_each_deal()
    {
        var w = await PaidCart(invoice: false);
        var metadata = GatewayPaymentProof.MetadataFor(w.Ids);
        w.Moyasar.Then(HttpStatusCode.Created, MoyasarClientTests.InvoiceJson("initiated", 15000, metadata: new Dictionary<string, string>()))
                 .Then(HttpStatusCode.OK, MoyasarClientTests.InvoiceJson("initiated", 15000, metadata: metadata));
        var l = Fresh();

        var opened = await w.Gateway.OpenInvoiceAsync(Slug, new DealPayer(Buyer), w.Ids, "مَتجَرُ الاختِبار", ReturnUrl, l);

        Assert.True(opened.Ok, opened.Code);
        Assert.StartsWith("http://checkout.moyasar.com/invoices/", opened.Url);
        Assert.Equal(2, w.Moyasar.Requests.Count);
        using var created = JsonDocument.Parse(w.Moyasar.Bodies[0]!);
        Assert.Equal(15000, created.RootElement.GetProperty("amount").GetInt64());
        Assert.Equal((ReturnUrl, ReturnUrl), (created.RootElement.GetProperty("success_url").GetString(), created.RootElement.GetProperty("back_url").GetString()));
        Assert.Equal(l.Format("pay.invoice.description", "مَتجَرُ الاختِبار"), created.RootElement.GetProperty("description").GetString());
        using var tagged = JsonDocument.Parse(w.Moyasar.Bodies[1]!);
        Assert.Equal(metadata.OrderBy(kv => kv.Key), tagged.RootElement.GetProperty("metadata").EnumerateObject()
            .Select(p => new KeyValuePair<string, string>(p.Name, p.Value.GetString()!)).OrderBy(kv => kv.Key));
        foreach (var id in w.Ids)
        {
            var deal = Load(w, id);
            Assert.Equal(InvoiceId, deal.Refs[GatewayPaymentProof.InvoiceRef]);
            Assert.Equal(opened.Url, deal.Refs[GatewayPayments.InvoiceUrlRef]);
            Assert.Equal(DealStatus.Active, deal.Status);
            Assert.True(GatewayPayments.HasInvoice(deal));
        }
        Assert.Empty(w.Platform.Calls);
    }

    /// <summary><b>وما لَم تُفتَح فاتورَتُه يُلغى بِاسمِ مُشتَريه ولا يُخصَمُ شَيء</b> — رَفضُ البَوّابَة (‏401)، أَو مَبلَغٌ بِكَسرِ هَلَلَة
    /// (بِلا نِداء)، أَو لا مِفتاح (بِلا نِداء)، أَو صَفقَةُ غَيرِه في القائِمَة (بِلا نِداء): رَمزُه، والصَفَقاتُ مُلغاةٌ بِسَبَبٍ مِن القامُوس بِلا
    /// مَرجِعِ فاتورَة.</summary>
    [Theory]
    [InlineData("gateway_401", MoyasarFailures.Refused, 1)]
    [InlineData("fractional_halala", MoyasarFailures.Invalid, 0)]
    [InlineData("no_key", MoyasarFailures.NotConfigured, 0)]
    [InlineData("foreign_deal", MoyasarFailures.Invalid, 0)]
    public async Task A_cart_whose_invoice_does_not_open_is_cancelled_with_nothing_charged(string kind, string code, int calls)
    {
        var w = kind == "no_key" ? await PaidCart(_ => null, invoice: false) : await PaidCart(invoice: false);
        var ids = w.Ids.ToList();
        switch (kind)
        {
            case "gateway_401":
                w.Moyasar.Then(HttpStatusCode.Unauthorized, """{"type":"authentication_error","message":"Invalid authorization credentials"}""");
                break;
            case "fractional_halala": Load(w, w.Ids[0]).AmountSar = 100.005m; break;
            case "foreign_deal":
                ids.Add((await w.Deals.StartAsync(Slug, "marketplace", Other, "غَيرُه", null, "سِلعَة", 10m)).Id);
                break;
        }
        var l = Fresh();

        var opened = await w.Gateway.OpenInvoiceAsync(Slug, new DealPayer(Buyer), ids, "مَتجَر", ReturnUrl, l);

        Assert.False(opened.Ok);
        Assert.Equal(code, opened.Code);
        Assert.Equal(calls, w.Moyasar.Requests.Count);
        foreach (var id in w.Ids)
        {
            var deal = Load(w, id);
            Assert.Equal(DealStatus.Cancelled, deal.Status);
            Assert.Equal(l["checkout.payment.cancel_gateway_open_failed"], deal.CancelReason);
            Assert.False(GatewayPayments.HasInvoice(deal));
        }
        Assert.Empty(w.Platform.Calls);
    }

    // ═══ ٦) إشعارُ البائِعِ مَرَّة ═════════════════════════════════════════════════════════════════════

    private static DealNotices Notices(World w)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSignalR();
        var hub = services.BuildServiceProvider().GetRequiredService<IHubContext<ACommerce.Kit.Realtime.Server.RealtimeHub>>();
        return new DealNotices(w.Docs.Store, hub, new WebPushService(new ConfigurationBuilder().Build(), NullLogger<WebPushService>.Instance));
    }

    [Fact]
    public async Task The_paid_notice_tells_each_listing_owner_once_and_nobody_for_an_empty_list()
    {
        var w = await PaidCart(withSeller: true);
        var notices = Notices(w);
        var l = Fresh();

        var touches = w.Docs.Touches.Count;
        Assert.Equal(0, await notices.NotifyPaidAsync(Slug, Array.Empty<Guid>(), l));
        Assert.Equal(touches, w.Docs.Touches.Count);

        Assert.Equal(2, await notices.NotifyPaidAsync(Slug, w.Ids, l));
        var told = w.Docs.Stored.OfType<ACommerce.Kit.Notifications.Notification>().ToList();
        Assert.Equal(2, told.Count);
        Assert.All(told, n => Assert.Equal((Seller, DealNotices.PaidType), (n.UserId, n.Type)));
        Assert.Equal(w.Ids.Select(id => $"/{Slug}/deals/{id}").OrderBy(x => x), told.Select(n => n.RelatedUrl).OrderBy(x => x));

        // ومالِكٌ هُوَ المُشتَري لا يُخبَرُ بِما دَفَعَه.
        Load(w, w.Ids[0]).Refs[DealsService.ListingOwnerRef] = Buyer.ToString();
        Load(w, w.Ids[0]).CounterpartyId = null;
        Assert.Equal(1, await notices.NotifyPaidAsync(Slug, w.Ids, l));
    }

    // ═══ ٧) النُصوص ═════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Every_text_the_gateway_payment_writes_or_draws_is_live_in_the_dictionary()
    {
        var keys = new[]
        {
            "deals.detail.event_gateway_paid", "deals.detail.event_gateway_refund_due",
            "studio.deal.event_gateway_paid", "studio.deal.event_gateway_refund_due",
            "deals.detail.complete_payment", "deals.detail.err_deal_awaits_payment",
            "notifications.deal_paid.title", "notifications.deal_paid.body", "notifications.deal_paid.push_body",
            "checkout.payment.err_gateway_open_failed", "checkout.payment.cancel_gateway_open_failed",
            "pay.return.confirmed", "pay.return.open_deal", "pay.return.complete_payment", "pay.invoice.description",
        };
        var checkedCount = 0;
        foreach (var key in keys)
        {
            var text = LocaleCatalog.Text(LocaleCatalog.Arabic, key);
            Assert.NotEqual(key, text);
            Assert.False(LocaleCatalog.IsPlaceholder(text), $"‏{key} نائِب.");
            Assert.True(text.Length > 8, $"‏{key} بِنَصٍّ أَقصَرَ مِن أَن يُفهَم.");
            checkedCount++;
        }
        output.WriteLine($"· نُصوصُ الخُطوَة 10: {checkedCount} مِفتاحاً حَيّاً.");
        Assert.Equal(15, checkedCount);
        Assert.Contains("{0}", LocaleCatalog.Text(LocaleCatalog.Arabic, "pay.invoice.description"), StringComparison.Ordinal);
    }

    // ═══ ٨) الصَفَحاتُ والنُقطَة — ما يُرسَمُ هُوَ ما يُقبَل ═══════════════════════════════════════════════

    [Fact]
    public void The_pure_readers_of_the_return_page_accept_only_what_the_writers_write()
    {
        var deal = new Deal { Refs = { [GatewayPaymentProof.InvoiceRef] = InvoiceId } };
        Assert.False(GatewayReturn.IsConfirmed(deal));
        deal.Refs["payment_status"] = nameof(PaymentStatus.Captured);
        Assert.False(GatewayReturn.IsConfirmed(deal));        // «Captured» بِلا مَرجِعِ الفاتورَة: قَبضٌ آخَر.
        deal.Refs["payment_id"] = InvoiceId;
        Assert.True(GatewayReturn.IsConfirmed(deal));

        foreach (var (url, kept) in new (string, bool)[]
                 {
                     ("https://checkout.moyasar.com/invoices/1", true), ("http://checkout.moyasar.com/invoices/1", true),
                     ("javascript:alert(1)", false), ("/zz/pay/return", false), ("", false),
                 })
        {
            deal.Refs[GatewayPayments.InvoiceUrlRef] = url;
            Assert.Equal(kept ? url : null, GatewayReturn.PayUrlOf(deal));
        }
    }

    [Fact]
    public void The_pages_draw_the_payment_door_and_the_timeline_lines_from_the_same_predicates()
    {
        var member = Code("Components", "Pages", "MyDealDetail.razor");
        Assert.Contains("awaitsPayment = GatewayPayments.AwaitsPayment(deal);", member, StringComparison.Ordinal);
        var door = member.IndexOf("else if (canAdvance && awaitsPayment)", StringComparison.Ordinal);
        var held = member.IndexOf("else if (canAdvance && heldByCarrier)", StringComparison.Ordinal);
        Assert.True(door > 0 && held > door, $"بابُ الدَفعِ قَبلَ فَرعِ OTO: door={door} held={held}");
        Assert.Contains("href=\"@AuthSession.LinkFor(Slug, Role, GatewayReturn.PathFor(deal.Id, null))\"", member[door..held], StringComparison.Ordinal);
        Assert.Contains("DealEvent.GatewayPaid => L[\"deals.detail.event_gateway_paid\"]", member, StringComparison.Ordinal);
        Assert.Contains("DealEvent.GatewayRefundDue => L[\"deals.detail.event_gateway_refund_due\"]", member, StringComparison.Ordinal);

        var studio = Code("Components", "Pages", "StudioAppDealDetail.razor");
        Assert.Contains("DealEvent.GatewayPaid      => L[\"studio.deal.event_gateway_paid\"]", studio, StringComparison.Ordinal);
        Assert.Contains("DealEvent.GatewayRefundDue => L[\"studio.deal.event_gateway_refund_due\"]", studio, StringComparison.Ordinal);

        var back = Code("Components", "Pages", "GatewayPayReturn.razor");
        Assert.Contains("confirmed = GatewayReturn.IsConfirmed(deal);", back, StringComparison.Ordinal);
        Assert.Contains("payUrl = GatewayReturn.PayUrlOf(deal);", back, StringComparison.Ordinal);

        var checkout = Code("Components", "Pages", "CheckoutPage.razor");
        Assert.Contains("CheckoutPayMethods.GatewayOpenFailed => L[\"checkout.payment.err_gateway_open_failed\"]", checkout, StringComparison.Ordinal);
    }

    [Fact]
    public void The_service_skips_the_capture_of_a_captured_deal_and_gates_Paid_after_the_carrier_gate()
    {
        var service = Code("Services", "Deals", "DealsService.cs");
        Assert.Contains("if (next == DealStage.Paid && deal.AmountSar > 0 && !IsCaptured(deal))", service, StringComparison.Ordinal);
        var carrier = service.IndexOf("return new(false, deal, AwaitsCarrierAr, DealShipping.DealAwaitsCarrier);", StringComparison.Ordinal);
        var payment = service.IndexOf("if (GatewayPayments.AwaitsPayment(deal))", StringComparison.Ordinal);
        var move    = service.IndexOf("moved = await MoveAsync(s, deal, next.Value, actorId, actorName, note, ct);", StringComparison.Ordinal);
        Assert.True(carrier > 0 && payment > carrier && move > payment, $"carrier={carrier} payment={payment} move={move}");
        Assert.Contains("if (GatewayPayments.CollectedByGateway(deal))", service, StringComparison.Ordinal);
    }

    /// <summary><b>نُقطَةُ إتمامِ الشِراء</b>: «أَيَفتَحُ المَتجَرُ فاتورَة؟» مَرَّةً قَبلَ الحَلقَة، والحَجزُ القائِمُ بِحَرفِه خَلفَ <c>if (!viaGateway)</c>،
    /// والفاتورَةُ الواحِدَةُ قَبلَ حَذفِ السَلَّة، والرَدُّ بِرَمزِه والسَلَّةُ باقِيَة، والتَحويلُ إلى صَفحَةِ البَوّابَةِ أَو الصَفقَة.</summary>
    [Fact]
    public void The_checkout_asks_once_opens_one_invoice_before_deleting_the_cart_and_keeps_the_cart_on_refusal()
    {
        var text = WriteEndpointGuardTests.StripComments(Template("MarketplaceTemplateExtensions.cs"));
        var start = text.IndexOf("\"/{slug}/checkout/submit\"", StringComparison.Ordinal);
        var body = text[start..text.IndexOf(".RequireStoreWritable();", start, StringComparison.Ordinal)];

        int At(string needle)
        {
            var at = body.IndexOf(needle, StringComparison.Ordinal);
            Assert.True(at > 0, $"‏`{needle}` غَيرُ مَوجودٍ في `/checkout/submit`.");
            Assert.Equal(at, body.LastIndexOf(needle, StringComparison.Ordinal));
            return at;
        }
        // (‏2026-10-12، ‏ADR-032 الخُطوَة 8 — شَطرُ `CardOffered`: «يُسأَلُ مَرَّةً» صارَ مُدخَلَ القَرارِ نَفسَه — `CardFactsAsync` قَبلَ `Decide`
        // و`card.ThroughGateway` هُنا — لا `ReadyAsync` الَّتي كانَت تَفُكُّ المِفتاحَ لِتُجيب؛ والتَرتيبُ بَعدَه كَما كان.)
        var facts   = At("var card = await gateway.CardFactsAsync(slug,");
        var ask     = At("var viaGateway = Services.Deals.CheckoutPaymentPolicy.CallsProvider(payMethod) && card.ThroughGateway;");
        Assert.True(facts < ask, $"facts={facts} ask={ask}");
        var loop    = At("foreach (var item in cart.Items)");
        var guard   = At("if (!viaGateway)");
        var hold    = At("var pr = await payments.AuthorizeAsync(new(");
        var open    = At("var opened = viaGateway && created.Count > 0 ? await gateway.OpenInvoiceAsync(");
        var refuse  = At(".RefusalPath(Services.Deals.CheckoutPayMethods.GatewayOpenFailed, name, phone, addr)));");
        var delete  = At("s.Delete<ACommerce.Kit.Cart.Cart>(userId);");
        var leave   = At("return Results.Redirect(opened?.Url ?? Link(req, slug, firstDealId == Guid.Empty ? \"deals\" : $\"deals/{firstDealId}\"));");
        Assert.True(ask < loop && loop < guard && guard < hold && hold < open && open < refuse && refuse < delete && delete < leave,
            $"ask={ask} loop={loop} guard={guard} hold={hold} open={open} refuse={refuse} delete={delete} leave={leave}");
        Assert.True(body[guard..hold].TrimStart().StartsWith("if (!viaGateway)", StringComparison.Ordinal)
                    && body[(guard + "if (!viaGateway)".Length)..hold].Trim().StartsWith("try", StringComparison.Ordinal),
            "الحَجزُ القائِمُ لا يَتلو حارِسَه مُباشَرَة.");
    }
}

// ─── البُرهانُ الحَيّ — Postgres بِتَزامُنٍ تَفاؤُلِيٍّ حَقيقِيّ ──────────────────────────────────────────────
//
// يَتَخَطّى نَفسَه ما لَم تُضبَط WASAYEL_LIVE_PROOF=1 و ConnectionStrings__Postgres — عَقدُ `LiveDealConcurrencyProofTests` حَرفاً، ومِنه
// تَركيبُ المَخزَنِ (بِتَسجيلِ الصَفقَةِ الَّذي يُشَغِّلُه القالِب). يَمشي التَحَقُّقَ كامِلاً عَلى قاعِدَةٍ حَقيقِيَّة: رَبطُ دَفعٍ مَختومٌ بِخِزانَةٍ
// حَقيقِيَّةٍ في وَثيقَتِه، وصَفقَتانِ — إحداهُما عِندَ «تأكيد» — وفاتورَةٌ تُعيدُها البَوّابَةُ مَدفوعَة؛ ثُمَّ تَحَقُّقٌ ثانٍ. ويَقيسُ بِنُسخَةِ
// Marten نَفسِها (‏`mt_version`) أَنّ الثانِيَ لا يَكتُب — لا بِعَدِّ سُطورٍ وَحدَه.

[Collection(LiveProofCollection.Name)]
public class LiveGatewayPaymentProofTests(ITestOutputHelper output)
{
    private static bool Enabled
        => LiveDealConcurrencyProofTests.Enabled && !string.IsNullOrEmpty(LiveDealConcurrencyProofTests.ConnectionString);

    private static async Task<Guid> VersionAsync(Marten.IDocumentStore store, string slug, Guid id)
    {
        await using var s = store.QuerySession(slug);
        var deal = await s.LoadAsync<Deal>(id);
        return (await s.MetadataForAsync(deal!)).CurrentVersion;
    }

    [Fact]
    public async Task A_paid_invoice_is_recorded_once_moves_the_confirmed_deal_into_Paid_and_a_second_verify_moves_no_version()
    {
        if (!Enabled) return;

        await using var store = LiveDealConcurrencyProofTests.BuildStore();
        var slug = "zz-gw-" + Guid.NewGuid().ToString("N")[..8];
        var (buyer, seller) = (Guid.NewGuid(), Guid.NewGuid());
        try
        {
            var platform = new DealMoneyClaimFirstTests.RecordingPaymentProvider();
            var deals = new DealsService(store, platform);
            var ids = new List<Guid>();
            foreach (var amount in new[] { 100m, 50m })
            {
                var born = await deals.StartAsync(slug, "marketplace", buyer, "مُشتَرٍ", null, "سِلعَة", amount,
                    new() { [CheckoutPayMethods.AttributeKey] = CheckoutPayMethods.Card });
                await deals.AttachRefAsync(slug, born.Id, GatewayPaymentProof.InvoiceRef, InvoiceId);
                await deals.AttachRefAsync(slug, born.Id, DealsService.ListingOwnerRef, seller.ToString());
                await deals.BindListingOwnerAsync(slug, born.Id, seller.ToString());
                ids.Add(born.Id);
            }
            Assert.True((await deals.AcceptAsync(slug, ids[0], new DealAcceptor(seller, "بائِع"))).Ok);
            Assert.True((await deals.AdvanceAsync(slug, ids[0], seller, "بائِع")).Ok);

            // «تأكيد» ودَفعُها لَم يَثبُت: البابُ مَردودٌ بِرَمزِه قَبلَ أَيِّ كِتابَة.
            var before = await VersionAsync(store, slug, ids[0]);
            var early = await deals.AdvanceAsync(slug, ids[0], buyer, "مُشتَرٍ");
            Assert.Equal(GatewayPayments.DealAwaitsPayment, early.Code);
            Assert.Equal(before, await VersionAsync(store, slug, ids[0]));

            var vault = TenantGatewayEndpointsTests.NewVault();
            var binding = TenantGatewayEndpointsTests.PaymentsBinding(vault, sealedFor: slug);
            binding.TenantSlug = slug;
            await using (var s = store.LightweightSession(slug)) { s.Store(binding); await s.SaveChangesAsync(); }

            var moyasar = new MoyasarScriptedHandler();
            var invoice = MoyasarClientTests.InvoiceJson("paid", 15000, metadata: GatewayPaymentProof.MetadataFor(ids));
            moyasar.Then(HttpStatusCode.OK, invoice).Then(HttpStatusCode.OK, invoice);
            var gateway = new GatewayPayments(deals, new TenantProviderService(store), vault,
                new MoyasarClient(new HttpClient(moyasar), NullLogger<MoyasarClient>.Instance));

            var first = await gateway.VerifyAsync(slug, new DealPayer(buyer), ids[1].ToString("N"));
            var paid = (await deals.LoadAsync(slug, ids[0]))!;
            var waiting = (await deals.LoadAsync(slug, ids[1]))!;
            output.WriteLine($"الأَوَّل: ok={first.Ok} confirmed={first.Confirmed.Count} · [0] {paid.Stage}/{paid.Refs["payment_status"]} · " +
                             $"[1] {waiting.Stage}/{waiting.Refs["payment_status"]} · المُزَوِّد: [{string.Join(", ", platform.Calls)}] · " +
                             $"البَوّابَة: {moyasar.Requests.Count} نِداء");

            Assert.True(first.Ok, first.Code);
            Assert.Equal(2, first.Confirmed.Count);
            Assert.Equal(DealStage.Paid, paid.Stage);
            Assert.Equal(buyer, Assert.Single(paid.Timeline, e => e.Action == "advanced" && e.StageAfter == DealStage.Paid).ActorId);
            Assert.Equal((DealStage.Offered, "Captured"), (waiting.Stage, waiting.Refs["payment_status"]));
            Assert.Empty(platform.Calls);

            var versions = (await VersionAsync(store, slug, ids[0]), await VersionAsync(store, slug, ids[1]));
            var second = await gateway.VerifyAsync(slug, new DealPayer(buyer), ids[1].ToString("N"));
            var after = (await VersionAsync(store, slug, ids[0]), await VersionAsync(store, slug, ids[1]));
            output.WriteLine($"الثاني: ok={second.Ok} confirmed={second.Confirmed.Count} · النُسَخ {(versions == after ? "لَم تَتَحَرَّك" : "تَحَرَّكَت")}");

            Assert.True(second.Ok, second.Code);
            Assert.Empty(second.Confirmed);
            Assert.Equal(versions, after);
            Assert.Equal(2, moyasar.Requests.Count);
        }
        finally
        {
            await using var s = store.LightweightSession(slug);
            foreach (var d in await Marten.QueryableExtensions.ToListAsync(s.Query<Deal>())) s.Delete(d);
            foreach (var b in await Marten.QueryableExtensions.ToListAsync(s.Query<ACommerce.Platform.Providers.TenantProviderBinding>())) s.Delete(b);
            await s.SaveChangesAsync();
        }
    }
}
