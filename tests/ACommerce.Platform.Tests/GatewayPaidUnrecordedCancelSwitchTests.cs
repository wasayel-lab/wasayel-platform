using System.Collections.Concurrent;
using System.Net;
using ACommerce.Kit.Notifications;
using ACommerce.Kit.Payments;
using ACommerce.Platform.I18n;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using Microsoft.AspNetCore.Http;
using Xunit;
using Xunit.Abstractions;
using static ACommerce.Platform.Tests.TenantGatewayEndpointsTests;

namespace ACommerce.Platform.Tests;

// ═══ طَلَبٌ دُفِعَ عِندَ ميسر ولَم يُسَجَّل ثُمَّ أُلغِيَ — يُكتَبُ دَفعُه واستِرجاعُه ويُخبَرُ صاحِبُ المَتجَر (‏2026-11-05، الموجَة ١٢ G3 · ١ — C-PAY-EXP-1؛ تَوصيفُه `6409b4a`) ═══
//
// **القَرار** (د3 «د»: «إشعار لصاحب المتجر بكل استرجاع مستحقّ»؛ وP1): المُلغي يَجلِبُ الفاتورَةَ فَيَجِدُها مَدفوعَة ⇒ رَمزٌ مُسَمّى `gateway_invoice_paid` ومَعَه بُرهانُ
// `GatewayPaymentProof.Gate` عَلى الصَفَقاتِ الَّتي تُسَمّيها، فَيَكتُبُه الإلغاءُ بِالكاتِبِ القائِم (`ConfirmGatewayPaymentAsync` — «مَدفوع» وعَلى المُلغاةِ `gateway_refund_due`)،
// ويَبلُغُ الإشعارُ صاحِبَ المَتجَرِ بِالأُنبوبِ القائِم (`OwnerActionOf` ⇐ `RefundDue`). وما لا يُثبِتُه البُرهانُ يُبقيها (`LeavesPayable`) فَيُخبَرُ أَن يَنظُرَ في لَوحَةِ ميسر.
// ومُعالِجُ المُهلَةِ يَعُدُّ المُلغاةَ الَّتي تُرِكَت فاتورَتُها مَفتوحَةً فيما يُطالَبُ بِه، فَجَلبٌ فَشِلَ عَلَيها يُعادُ؛ ويَأسُه يَقولُ ذلكَ بِسَطرِه.
// ومِلَفُّ التَوصيفِ `GatewayPaidUnrecordedCancelTests.cs` بِبَصمَتِه قَبلُ وبَعدُ.

public class GatewayPaidUnrecordedCancelSwitchTests(ITestOutputHelper output)
{
    private static Deal Load(World w, Guid id) => w.Docs.Read<Deal>(id.ToString())!;
    private static Guid OwnerOf(World w) => w.Docs.Read<ACommerce.Kit.Tenants.Tenant>(Slug)!.OwnerUserId;
    private static readonly DealCanceller BuyerCancels = new(Buyer, "مُشتَرٍ", IsStoreAdmin: false);
    private static string[] Sent(World w) => w.Moyasar.Requests.Select(r => r.Method.Method).ToArray();
    private static Notification[] ToOwner(World w) => w.Docs.Stored.OfType<Notification>().Where(n => n.UserId == OwnerOf(w)).ToArray();

    /// <summary><b>المُوجِب — الطَلَبُ كُلُّه مِن الاستوديو</b>: جَلبٌ واحِدٌ يَجِدُها مَدفوعَةً فَيُكتَبُ دَفعُها عَلى الصَفقَتَين (‏<c>payment_id</c> الفاتورَة، <c>Captured</c>، وعُملَتُها) ولِكُلٍّ
    /// سَطرُ استِرجاعٍ مُستَحَقٍّ واحِد — والثانِيَةُ تُلغى بِفَرعِ المَقبوضَةِ بِلا جَلب — وصاحِبُ المَتجَرِ يُخبَرُ بِاستِرجاعَين بِنَصِّ القَرار، ولا إلغاءَ عِندَ البَوّابَة.</summary>
    [Fact]
    public async Task A_whole_order_paid_at_the_gateway_but_unrecorded_is_recorded_at_the_cancel_with_a_refund_due_line_and_an_owner_notice_each()
    {
        var (w, _, ids) = await GatewayExpiryRaceTests.ExpiredCart();
        w.Moyasar.Requests.Clear();
        w.Moyasar.Then(HttpStatusCode.OK, GatewayExpiryRaceTests.Invoice(ids, MoyasarClient.PaidStatus))
                 .Then(HttpStatusCode.OK, GatewayExpiryRaceTests.Invoice(ids, MoyasarClient.PaidStatus));
        var owner = OwnerOf(w);
        var l = new L(new HttpContextAccessor());

        var order = await w.Deals.CancelWholeInvoiceAsync(Slug, ids[0], new DealCanceller(owner, "مالِك التَّطبيق", IsStoreAdmin: true), "إلغاء إداريّ");
        var notices = GatewayOwnerMoneyNoticeTests.Notices(w.Docs);
        foreach (var gone in order.Cancelled) await notices.NotifyCancelledByStoreAsync(Slug, gone, owner, l);

        var told = ToOwner(w);
        output.WriteLine($"ميسر [{string.Join(" ", Sent(w))}] · " + string.Join(" · ", ids.Select((id, i) =>
            $"#{i + 1} {Load(w, id).Status}/مَقبوضَة={DealsService.IsCaptured(Load(w, id))} استِرجاعٌ مُستَحَقّ={Load(w, id).Timeline.Count(e => e.Action == DealEvent.GatewayRefundDue)}")) +
            $" · لِصاحِبِ المَتجَر: {told.Length} «{told.FirstOrDefault()?.Body}»");
        Assert.True(order.Result.Ok, order.Result.Violation?.Code);
        Assert.Equal(new[] { "GET" }, Sent(w));
        Assert.All(ids, id =>
        {
            var d = Load(w, id);
            Assert.Equal(DealStatus.Cancelled, d.Status);
            Assert.Equal((InvoiceId, "Captured", "SAR"), (d.Refs["payment_id"], d.Refs["payment_status"], d.Refs[DealsService.PaymentCurrencyRef]));
            Assert.Single(d.Timeline, e => e.Action == DealEvent.GatewayPaid);
            Assert.Single(d.Timeline, e => e.Action == DealEvent.GatewayRefundDue);
            Assert.DoesNotContain(d.Timeline, e => e.Action == DealEvent.GatewayInvoiceLeftOpen);
        });
        Assert.All(order.Cancelled, d => Assert.Equal(DealNotices.OwnerAction.RefundDue, DealNotices.OwnerActionOf(d)));
        Assert.Equal(2, told.Length);
        Assert.Equal(ids.Select(id => l.Format("notifications.deal_refund_due.body", Load(w, id).ListingTitle, Load(w, id).AmountSar, "SAR")).OrderBy(x => x),
            told.Select(n => n.Body).OrderBy(x => x));
        Assert.Empty(w.Platform.Calls);
    }

    /// <summary><b>المُوجِب — المُشتَري يُلغي صَفقَتَه الوَحيدَة</b>: جَلبٌ واحِد، والصَفقَةُ مُلغاةٌ مَقبوضَةٌ بِسَطرِ استِرجاعِها، والنَتيجَةُ الَّتي يَقرَؤُها بابُ الإشعارِ تَحمِلُه —
    /// فَصاحِبُ المَتجَرِ يُخبَرُ مَرَّة. وسَطرُ لوغِ المُلغي يُسَمّي الرَمزَ ولا يَحمِلُ المِفتاح.</summary>
    [Fact]
    public async Task A_buyer_cancelling_the_only_deal_on_an_invoice_paid_but_unrecorded_records_it_and_the_store_owner_is_told_once()
    {
        var (w, _, ids) = await GatewayExpiryRaceTests.ExpiredCart(onInvoice: 1);
        w.Moyasar.Requests.Clear();
        w.Moyasar.Then(HttpStatusCode.OK, GatewayExpiryRaceTests.Invoice(ids, MoyasarClient.PaidStatus, amount: 10000));

        var res = await w.Deals.CancelAsync(Slug, ids[0], BuyerCancels, "عَدَلتُ");
        await GatewayOwnerMoneyNoticeTests.Notices(w.Docs).NotifyCancelledAsync(Slug, res.Deal!, Buyer, "مُشتَرٍ", new L(new HttpContextAccessor()));

        output.WriteLine($"ميسر [{string.Join(" ", Sent(w))}] · السِجِلّ: {string.Join(" · ", res.Deal!.Timeline.Select(e => e.Action))} · لِصاحِبِ المَتجَر: {ToOwner(w).Length}");
        Assert.True(res.Ok, res.Violation?.Code);
        Assert.Equal(new[] { "GET" }, Sent(w));
        Assert.True(DealsService.IsCaptured(res.Deal));
        Assert.Equal(DealEvent.GatewayRefundDue, res.Deal.Timeline[^1].Action);
        Assert.Equal(DealNotices.OwnerAction.RefundDue, DealNotices.OwnerActionOf(res.Deal));
        Assert.Single(ToOwner(w));
    }

    /// <summary><b>السالِب — مَدفوعَةٌ لا يُثبِتُها البُرهان</b> (مَبلَغُها غَيرُ مَجموعِ صَفَقاتِها): لا يُكتَبُ «مَدفوع»، ويَعودُ رَمزُ خَرقِه فَيُبقيها — سَطرُ «تُرِكَت مَفتوحَة»
    /// بِذلكَ الرَمزِ وإشعارٌ لِصاحِبِ المَتجَرِ أَن يَنظُرَ في لَوحَةِ ميسر، لا صَمتٌ ولا «مَدفوعٌ» بِلا بُرهان.</summary>
    [Fact]
    public async Task A_paid_invoice_the_proof_refuses_is_not_recorded_and_tells_the_store_owner_it_was_left_open()
    {
        var (w, _, ids) = await GatewayExpiryRaceTests.ExpiredCart(onInvoice: 1);
        w.Moyasar.Requests.Clear();
        w.Moyasar.Then(HttpStatusCode.OK, GatewayExpiryRaceTests.Invoice(ids, MoyasarClient.PaidStatus, amount: 9000));

        var res = await w.Deals.CancelAsync(Slug, ids[0], BuyerCancels, "عَدَلتُ");
        await GatewayOwnerMoneyNoticeTests.Notices(w.Docs).NotifyCancelledAsync(Slug, res.Deal!, Buyer, "مُشتَرٍ", new L(new HttpContextAccessor()));

        var d = Load(w, ids[0]);
        output.WriteLine($"ميسر [{string.Join(" ", Sent(w))}] · آخِرُ سَطر: {d.Timeline[^1].Action} {d.Timeline[^1].Note} · لِصاحِبِ المَتجَر: {ToOwner(w).Length}");
        Assert.True(res.Ok);
        Assert.False(DealsService.IsCaptured(d));
        Assert.Equal((DealEvent.GatewayInvoiceLeftOpen, GatewayPaymentProof.AmountMismatch), (d.Timeline[^1].Action, d.Timeline[^1].Note));
        Assert.DoesNotContain(d.Timeline, e => e.Action is DealEvent.GatewayPaid or DealEvent.GatewayRefundDue);
        Assert.Equal(new[] { "GET" }, Sent(w));
        Assert.Single(ToOwner(w));
    }

    /// <summary><b>رَمزُ المُلغي الجَديدُ لا يُبقي الفاتورَة</b>: <see cref="GatewayInvoiceCloser.Paid"/> خارِجَ <see cref="GatewayInvoiceCloser.LeavesPayable"/> — دُفِعَت فَلا تُدفَعُ ثانِيَة —
    /// وخَرقُ البُرهانِ داخِلَه؛ والرَمزُ مُسَمّىً بِحَرفِه ولَيسَ مِن مَعجَمِ البَوّابَةِ النَقِيَّةِ الثُلاثِيّ (‏<see cref="GatewayInvoiceClosing.All"/> كَما هو).</summary>
    [Fact]
    public void The_paid_outcome_leaves_nothing_payable_and_a_refused_proof_does()
    {
        Assert.Equal("gateway_invoice_paid", GatewayInvoiceCloser.Paid);
        Assert.False(GatewayInvoiceCloser.LeavesPayable(GatewayInvoiceCloser.Paid));
        Assert.True(GatewayInvoiceCloser.LeavesPayable(GatewayPaymentProof.AmountMismatch));
        Assert.DoesNotContain(GatewayInvoiceCloser.Paid, GatewayInvoiceClosing.All);
    }

    /// <summary><b>المُوجِب — مُلغاةٌ تُرِكَت فاتورَتُها مَفتوحَة، ويَفشَلُ جَلبُها عِندَ المُهلَة</b> ⇒ المُعالِجُ يَرمي <see cref="GatewayInvoiceExpiryFetchFailed"/> بِرَمزِ العَميل فَتُعادُ
    /// الرِسالَة؛ <b>والسالِب</b>: فاتورَتُها رآها تَحَقُّقٌ مُغلَقَة (<see cref="GatewayPayments.InvoiceClosedRef"/>) ⇒ يَفرُغُ كَما كانَ — لا شَيءَ يُطالَبُ بِه.</summary>
    [Fact]
    public async Task A_left_open_cancelled_invoice_whose_fetch_fails_at_expiry_fails_the_message_and_a_closed_one_completes()
    {
        var (w, gateway, ids) = await GatewayExpiryRaceTests.ExpiredCart();
        GatewayExpiryFetchFailureTests.Script(w, "401");
        GatewayExpiryFetchFailureTests.Script(w, "401");
        Assert.True((await w.Deals.CancelWholeInvoiceAsync(Slug, ids[0], BuyerCancels, "عَدَلتُ")).Result.Ok);
        var l = new L(new HttpContextAccessor());
        Task Handle() => GatewayInvoiceExpiryHandler.Handle(new GatewayInvoiceExpiryDue(Slug, ids[0]), gateway, GatewayOwnerMoneyNoticeTests.Notices(w.Docs), l, CancellationToken.None);

        GatewayExpiryFetchFailureTests.Script(w, "401");
        var leftOpen = await Record.ExceptionAsync(Handle);
        foreach (var id in ids) await w.Deals.AttachRefAsync(Slug, id, GatewayPayments.InvoiceClosedRef, MoyasarClient.CanceledStatus);
        GatewayExpiryFetchFailureTests.Script(w, "401");
        var closed = await Record.ExceptionAsync(Handle);

        output.WriteLine($"تُرِكَت مَفتوحَة ثُمَّ ‏401: رَمى {leftOpen?.GetType().Name ?? "لا شَيء"} · رُئِيَت مُغلَقَة ثُمَّ ‏401: رَمى {closed?.GetType().Name ?? "لا شَيء"}");
        Assert.Equal(MoyasarFailures.Refused, Assert.IsType<GatewayInvoiceExpiryFetchFailed>(leftOpen).Code);
        Assert.Null(closed);
        Assert.Empty(await gateway.LeftOpenOnInvoiceAsync(Slug, ids[0]));
    }

    /// <summary><b>الدالَّةُ النَقِيَّة — مُوجَبٌ وسالِبٌ لِكُلِّ شَرط</b>: مُلغاةٌ غَيرُ مَقبوضَةٍ بِسَطرِ «تُرِكَت مَفتوحَة» ولَم تُرَ مُغلَقَة ⇒ تُعَدّ؛ وقائِمَةٌ، ومَقبوضَةٌ، وبِلا السَطر، ومَرئِيَّةٌ مُغلَقَة ⇒ لا.</summary>
    [Theory]
    [InlineData("left_open", true)]
    [InlineData("active", false)]
    [InlineData("captured", false)]
    [InlineData("no_line", false)]
    [InlineData("seen_closed", false)]
    public void LeftOpenUnpaid_is_a_cancelled_uncaptured_deal_with_its_left_open_line_and_no_closed_sighting(string how, bool expected)
    {
        var d = new Deal { Id = Guid.NewGuid(), Status = how == "active" ? DealStatus.Active : DealStatus.Cancelled };
        if (how != "no_line") d.Timeline.Add(new(d.Stage, d.Stage, DealEvent.GatewayInvoiceLeftOpen, null, null, MoyasarFailures.Refused, DateTime.UtcNow));
        if (how == "captured") d.Refs["payment_status"] = nameof(PaymentStatus.Captured);
        if (how == "seen_closed") d.Refs[GatewayPayments.InvoiceClosedRef] = MoyasarClient.CanceledStatus;
        Assert.Equal(expected, GatewayPayments.LeftOpenUnpaid(d));
    }

    /// <summary><b>ويَأسُ المُعالِجِ عَلى مُلغاةٍ تُرِكَت فاتورَتُها مَفتوحَةً يَقولُ ذلك</b> — بِـWolverine الحَقيقِيِّ في الذاكِرَة وسِياسَتِه: ‏401 في كُلِّ جَلب ⇒ أَربَعُ مُحاوَلاتٍ ثُمَّ سَطرُ يَأسٍ
    /// يُسَمّي «left open» ولا يَقولُ «nothing … owed» ولا «the unpaid order stays»، ولا إشعارَ جَديدَ لِصاحِبِ المَتجَر (أُخبِرَ يَومَ أُلغِيَت).</summary>
    [Fact]
    public async Task A_left_open_cancelled_invoice_whose_fetch_fails_every_attempt_gives_up_with_its_own_line_and_tells_nobody_again()
    {
        var (w, gateway, ids) = await GatewayExpiryRaceTests.ExpiredCart();
        GatewayExpiryFetchFailureTests.Script(w, "401");
        GatewayExpiryFetchFailureTests.Script(w, "401");
        Assert.True((await w.Deals.CancelWholeInvoiceAsync(Slug, ids[0], BuyerCancels, "عَدَلتُ")).Result.Ok);
        var (notices, mail, owner) = GatewayExpiryNothingOwedTests.OwnerWithMail(w);
        var before = GatewayExpiryNothingOwedTests.OwnerNotices(w, owner).Length;
        var at = new ConcurrentQueue<DateTimeOffset>();
        for (var i = 0; i < 8; i++)
            w.Moyasar.ThenDoing(() => at.Enqueue(DateTimeOffset.UtcNow), HttpStatusCode.Unauthorized, "{\"type\":\"authentication_error\"}");

        var logs = await GatewayExpiryNothingOwedTests.OnWolverineAsync(w, gateway, notices, ids[0], () => at.Count, untilGaveUp: true, settle: TimeSpan.FromSeconds(2));

        var gaveUp = GatewayExpiryNothingOwedTests.GaveUp(logs);
        output.WriteLine($"مُحاوَلات: {at.Count} · سَطرُ اليَأس: {gaveUp.Length} «{gaveUp.FirstOrDefault()}» · إشعاراتٌ جَديدَة لِلمالِك: " +
                         $"{GatewayExpiryNothingOwedTests.OwnerNotices(w, owner).Length - before} · بَريد: {mail.Notices.Count}");
        Assert.Equal(GatewayInvoiceExpiry.RetryCooldowns.Length + 1, at.Count);
        var line = Assert.Single(gaveUp);
        Assert.Contains("invoice left open at the gateway", line, StringComparison.Ordinal);
        Assert.DoesNotContain("nothing on its invoice is still owed", line, StringComparison.Ordinal);
        Assert.DoesNotContain("the unpaid order stays", line, StringComparison.Ordinal);
        Assert.Equal(before, GatewayExpiryNothingOwedTests.OwnerNotices(w, owner).Length);
        Assert.Empty(mail.Notices);
    }

    /// <summary><b>ونَصُّ «طَلَبٌ انتَهَت مُهلَتُه لَم يُلغَ» يَقولُ «تَحَقَّق أَوَّلاً»</b> — صاحِبُ المَتجَرِ يَنظُرُ في لَوحَةِ ميسر قَبلَ أَن يُلغي، والإلغاءُ يَكتُبُ دَفعَ المَدفوعَةِ واستِرجاعَها؛
    /// والجُملَةُ نَفسُها في ‏DEPLOY §٢·ح لِلمُشَغِّل.</summary>
    [Fact]
    public void The_expiry_failed_notice_and_the_deploy_section_say_check_the_dashboard_first()
    {
        const string first = "تَحَقَّق في لَوحَةِ ميسر أَوَّلاً أَدُفِعَت";
        Assert.Contains(first, LocaleCatalog.Text(LocaleCatalog.Arabic, "notifications.deal_expiry_failed.body"), StringComparison.Ordinal);
        var deploy = File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, "docs", "DEPLOY.md"));
        var section = deploy[deploy.IndexOf("## ٢·ح.", StringComparison.Ordinal)..];
        section = section[..section.IndexOf("\n## ", 3, StringComparison.Ordinal)];
        Assert.Contains(first, section, StringComparison.Ordinal);
        Assert.Contains(GatewayInvoiceCloser.Paid, section, StringComparison.Ordinal);
    }
}
