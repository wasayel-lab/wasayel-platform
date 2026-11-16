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

// ═══ طَلَبٌ دُفِعَ عِندَ ميسر ولَم يُسَجَّل، ثُمَّ أُلغِيَ كَما يَأمُرُ إشعارُ «طَلَبٌ انتَهَت مُهلَتُه لَم يُلغَ» — تَوصيفُ «اليَوم» (‏2026-11-05، الموجَة ١٢ G3 · ١ — C-PAY-EXP-1) ═══
//
// **السِلسِلَةُ المَقيسَة** (القاعِدَة ٣ — يُثَبَّتُ ما يَبقى، ويُطبَعُ ما يَتَغَيَّر): مِفتاحُ ميسر يُبَدَّلُ فَيُجيبُ كُلُّ جَلبٍ ‏401 — إشعارُ الدَفعِ يُرَدُّ ‏200
// (`gateway_refused` لَيسَ مِن `RetriedConfirmCodes`) فَلا تُعيدُه ميسر، ورِسالَةُ المُهلَةِ تَموتُ بَعدَ أَربَعٍ ويُقالُ لِصاحِبِ المَتجَرِ «أَلغِه». فَيُعيدُ الرَبطَ ويُلغي:
// المُلغي يَجلِبُ الفاتورَةَ فَيَجِدُها `paid` ⇒ `gateway_invoice_not_open`، و`LeavesPayable` كاذِبَة، والصَفقَةُ «مُلغاة» بِلا سَطرِ `gateway_refund_due` ولا إشعارٍ لِصاحِبِ
// المَتجَر — والمالُ عِندَه. وقَرارُ المالِك د3 «د» يَقولُ «إشعار لصاحب المتجر بكل استرجاع مستحقّ».
// **وبابٌ ثانٍ يَسقُطُ**: صَفقَةٌ أُلغِيَت وفاتورَتُها تُرِكَت مَفتوحَة (`gateway_invoice_left_open`) ثُمَّ يَفشَلُ جَلبُها ساعَةَ تَحينُ المُهلَة ⇒ المُعالِجُ يَفرُغُ ولا يُعيد —
// `OwedOnInvoiceAsync` يَعُدُّ القائِمَةَ وَحدَها — فَدَفعٌ وَقَعَ عَلَيها لا تَبلُغُه إعادَة.
// **وما يَبقى في الحالَين**: الإلغاءُ يُقبَلُ والصَفَقاتُ مُلغاة، ولا `PUT …/cancel` عَلى فاتورَةٍ مَدفوعَة، ولا نِداءَ لِمُزَوِّدِ النُسخَة؛ وفاتورَةٌ لَم تُدفَع تُلغى عِندَ البَوّابَةِ كَما كانَت.

public class GatewayPaidUnrecordedCancelTests(ITestOutputHelper output)
{
    private static Deal Load(World w, Guid id) => w.Docs.Read<Deal>(id.ToString())!;
    private static Guid OwnerOf(World w) => w.Docs.Read<ACommerce.Kit.Tenants.Tenant>(Slug)!.OwnerUserId;
    private static readonly DealCanceller BuyerCancels = new(Buyer, "مُشتَرٍ", IsStoreAdmin: false);
    private static string[] Sent(World w) => w.Moyasar.Requests.Select(r => r.Method.Method).ToArray();

    /// <summary>صاحِبُ المَتجَرِ يُلغي الطَلَبَ كُلَّه مِن الاستوديو — كَما تُناديه نُقطَتُه — ثُمَّ إشعاراتُ كُلِّ صَفقَةٍ أُلغِيَت بِنَصِّ «إدارَةِ المَتجَر».</summary>
    private static async Task<(DealOrderCancelResult Order, int OwnerNotices)> StoreCancelsWholeOrderAsync(World w, Guid[] ids)
    {
        var owner = OwnerOf(w);
        var l = new L(new HttpContextAccessor());
        var before = w.Docs.Stored.OfType<Notification>().Count(n => n.UserId == owner);
        var order = await w.Deals.CancelWholeInvoiceAsync(Slug, ids[0], new DealCanceller(owner, "مالِك التَّطبيق", IsStoreAdmin: true), "إلغاء إداريّ");
        var notices = GatewayOwnerMoneyNoticeTests.Notices(w.Docs);
        foreach (var gone in order.Cancelled) await notices.NotifyCancelledByStoreAsync(Slug, gone, owner, l);
        return (order, w.Docs.Stored.OfType<Notification>().Count(n => n.UserId == owner) - before);
    }

    private string Print(World w, Guid[] ids, string label, int ownerNotices)
    {
        var line = $"{label}: ميسر [{string.Join(" ", Sent(w))}] · " + string.Join(" · ", ids.Select((id, i) =>
        {
            var d = Load(w, id);
            return $"#{i + 1} {d.Status}/مَقبوضَة={DealsService.IsCaptured(d)} استِرجاعٌ مُستَحَقّ={d.Timeline.Count(e => e.Action == DealEvent.GatewayRefundDue)}" +
                   $" تُرِكَت مَفتوحَة={d.Timeline.Count(e => e.Action == DealEvent.GatewayInvoiceLeftOpen)}";
        })) + $" · لِصاحِبِ المَتجَر: {ownerNotices}";
        output.WriteLine(line);
        return line;
    }

    /// <summary><b>دُفِعَت عِندَ ميسر ولَم تُسَجَّل، ثُمَّ يُلغي صاحِبُ المَتجَرِ الطَلَبَ كُلَّه</b>: ما يَبقى — الإلغاءُ يُقبَلُ والصَفقَتانِ مُلغاتان، وكُلُّ نِداءٍ إلى ميسر جَلبٌ لا إلغاء،
    /// ولا نِداءَ لِمُزَوِّدِ النُسخَة. ويُطبَعُ ما يَتَغَيَّر: القَبضُ، وسَطرُ الاستِرجاعِ المُستَحَقّ، وإشعاراتُ صاحِبِ المَتجَر، وعَدَدُ الجَلبات.</summary>
    [Fact]
    public async Task A_store_cancelling_a_whole_order_paid_at_the_gateway_but_unrecorded_cancels_both_and_never_cancels_the_paid_invoice()
    {
        var (w, _, ids) = await GatewayExpiryRaceTests.ExpiredCart();
        w.Moyasar.Requests.Clear();
        w.Moyasar.Then(HttpStatusCode.OK, GatewayExpiryRaceTests.Invoice(ids, MoyasarClient.PaidStatus))
                 .Then(HttpStatusCode.OK, GatewayExpiryRaceTests.Invoice(ids, MoyasarClient.PaidStatus));

        var (order, toOwner) = await StoreCancelsWholeOrderAsync(w, ids);

        Print(w, ids, "مَدفوعَةٌ لَم تُسَجَّل · الطَلَبُ كُلُّه مِن الاستوديو", toOwner);
        Assert.True(order.Result.Ok, order.Result.Violation?.Code);
        Assert.Equal(ids.OrderBy(x => x), order.Cancelled.Select(d => d.Id).OrderBy(x => x));
        Assert.All(ids, id => Assert.Equal(DealStatus.Cancelled, Load(w, id).Status));
        Assert.NotEmpty(w.Moyasar.Requests);
        Assert.All(w.Moyasar.Requests, r => Assert.Equal(HttpMethod.Get, r.Method));
        Assert.Empty(w.Platform.Calls);
    }

    /// <summary><b>والمُشتَري يُلغي صَفقَتَه الوَحيدَةَ عَلى فاتورَتِها</b> — والفاتورَةُ دُفِعَت ولَم تُسَجَّل: ما يَبقى كَما أَعلاه (جَلبٌ واحِدٌ لا إلغاء)، ويُطبَعُ ما يَتَغَيَّر.</summary>
    [Fact]
    public async Task A_buyer_cancelling_the_only_deal_on_an_invoice_paid_at_the_gateway_but_unrecorded_never_cancels_the_paid_invoice()
    {
        var (w, _, ids) = await GatewayExpiryRaceTests.ExpiredCart(onInvoice: 1);
        w.Moyasar.Requests.Clear();
        w.Moyasar.Then(HttpStatusCode.OK, GatewayExpiryRaceTests.Invoice(ids, MoyasarClient.PaidStatus, amount: 10000));
        var owner = OwnerOf(w);

        var res = await w.Deals.CancelAsync(Slug, ids[0], BuyerCancels, "عَدَلتُ");
        await GatewayOwnerMoneyNoticeTests.Notices(w.Docs).NotifyCancelledAsync(Slug, res.Deal!, Buyer, "مُشتَرٍ", new L(new HttpContextAccessor()));

        Print(w, ids, "مَدفوعَةٌ لَم تُسَجَّل · المُشتَري يُلغي الوَحيدَة", w.Docs.Stored.OfType<Notification>().Count(n => n.UserId == owner));
        Assert.True(res.Ok, res.Violation?.Code);
        Assert.Equal(DealStatus.Cancelled, Load(w, ids[0]).Status);
        Assert.Equal(new[] { "GET" }, Sent(w));
        Assert.Empty(w.Platform.Calls);
    }

    /// <summary><b>والسالِبُ — لا يَتَغَيَّرُ بِحَرف</b>: فاتورَةٌ لَم تُدفَع (<c>initiated</c>) والطَلَبُ كُلُّه يُلغى ⇒ الأولى تُسأَلُ فَتُبقيها أُختُها، والأَخيرَةُ تُسأَلُ ثُمَّ تُلغيها عِندَ
    /// البَوّابَة (جَلبٌ، جَلبٌ، إلغاء)؛ لا قَبضَ ولا سَطرَ استِرجاعٍ ولا «تُرِكَت مَفتوحَة» ولا إشعارَ لِصاحِبِ المَتجَر.</summary>
    [Fact]
    public async Task An_unpaid_invoice_is_still_cancelled_at_the_gateway_with_the_last_deal_and_writes_no_money_line()
    {
        var (w, _, ids) = await GatewayExpiryRaceTests.ExpiredCart();
        w.Moyasar.Requests.Clear();
        w.Moyasar.Then(HttpStatusCode.OK, GatewayExpiryRaceTests.Invoice(ids, "initiated"))
                 .Then(HttpStatusCode.OK, GatewayExpiryRaceTests.Invoice(ids, "initiated"))
                 .Then(HttpStatusCode.OK, GatewayExpiryRaceTests.Invoice(ids, MoyasarClient.CanceledStatus));

        var (order, toOwner) = await StoreCancelsWholeOrderAsync(w, ids);

        Print(w, ids, "لَم تُدفَع · الطَلَبُ كُلُّه مِن الاستوديو", toOwner);
        Assert.True(order.Result.Ok, order.Result.Violation?.Code);
        Assert.Equal(new[] { "GET", "GET", "PUT" }, Sent(w));
        Assert.All(ids, id =>
        {
            var d = Load(w, id);
            Assert.Equal(DealStatus.Cancelled, d.Status);
            Assert.False(DealsService.IsCaptured(d));
            Assert.DoesNotContain(d.Timeline, e => e.Action is DealEvent.GatewayRefundDue or DealEvent.GatewayInvoiceLeftOpen or DealEvent.GatewayPaid);
        });
        Assert.Equal(0, toOwner);
    }

    /// <summary><b>صَفقَةٌ أُلغِيَت وفاتورَتُها تُرِكَت مَفتوحَة (‏401 عِندَ المُلغي)، ثُمَّ يَفشَلُ جَلبُها ساعَةَ تَحينُ المُهلَة</b>: ما يَبقى — الصَفقَتانِ مُلغاتانِ بِسَطرِ «تُرِكَت مَفتوحَة»،
    /// والرَمزُ رَمزُ جَلبٍ فاشِل (<see cref="GatewayInvoiceExpiry.FailsTheMessage"/>)، ولا يُطالَبُ عَلى الفاتورَةِ بِقائِمَة، ولا إشعارَ جَديد. ويُطبَعُ ما يَتَغَيَّر: أَيَرمي المُعالِجُ فَتُعادُ الرِسالَة.</summary>
    [Fact]
    public async Task A_cancelled_order_whose_invoice_was_left_open_and_whose_fetch_fails_at_expiry_keeps_its_lines()
    {
        var (w, gateway, ids) = await GatewayExpiryRaceTests.ExpiredCart();
        GatewayExpiryFetchFailureTests.Script(w, "401");
        GatewayExpiryFetchFailureTests.Script(w, "401");
        var order = await w.Deals.CancelWholeInvoiceAsync(Slug, ids[0], BuyerCancels, "عَدَلتُ");
        Assert.True(order.Result.Ok, order.Result.Violation?.Code);
        var notes = w.Docs.Stored.OfType<Notification>().Count();
        GatewayExpiryFetchFailureTests.Script(w, "401");

        var thrown = await Record.ExceptionAsync(() => GatewayInvoiceExpiryHandler.Handle(new GatewayInvoiceExpiryDue(Slug, ids[0]), gateway,
            GatewayOwnerMoneyNoticeTests.Notices(w.Docs), new L(new HttpContextAccessor()), CancellationToken.None));

        output.WriteLine($"تُرِكَت مَفتوحَة ثُمَّ ‏401 عِندَ المُهلَة: رَمى {thrown?.GetType().Name ?? "لا شَيء"}" +
                         (thrown is GatewayInvoiceExpiryFetchFailed f ? $" {f.Code}" : ""));
        Assert.All(ids, id =>
        {
            var d = Load(w, id);
            Assert.Equal(DealStatus.Cancelled, d.Status);
            Assert.Equal((DealEvent.GatewayInvoiceLeftOpen, MoyasarFailures.Refused), (d.Timeline[^1].Action, d.Timeline[^1].Note));
        });
        Assert.True(GatewayInvoiceExpiry.FailsTheMessage(MoyasarFailures.Refused));
        Assert.Empty(await gateway.OwedOnInvoiceAsync(Slug, ids[0]));
        Assert.Equal(notes, w.Docs.Stored.OfType<Notification>().Count());
    }
}
