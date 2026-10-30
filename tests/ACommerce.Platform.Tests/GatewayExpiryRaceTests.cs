using System.Net;
using ACommerce.Kit.Notifications;
using ACommerce.Kit.Payments;
using ACommerce.Platform.I18n;
using ACommerce.Platform.Providers;
using ACommerce.Templates.Customer.Marketplace.Services;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.Abstractions;
using static ACommerce.Platform.Tests.TenantGatewayEndpointsTests;

namespace ACommerce.Platform.Tests;

// ═══ إلغاءُ المُهلَةِ عَلى حالِ فاتورَةٍ قَديم (‏2026-10-30، تَدقيقُ الموجَة ١١ C-PAY-2) ═══════════════════════════════════════════════════
//
// **الحالُ المَقيسَة**: فَرعُ المُهلَةِ في `SettleAsync` يُلغي عَلى حالِ الفاتورَةِ كَما جُلِبَت وعَلى قائِمَةِ صَفَقاتٍ قُرِئَت قَبلَ الكِتابَة. (١) عِندَ المُهلَةِ
// يَجلِبُ المُعالِجُ «initiated» (الدَفعُ في 3DS)، ثُمَّ يَتِمُّ الدَفعُ ويَكتُبُ الإشعارُ «Captured» — فَيُلغيها المُعالِجُ بِـ`ExpiryCanceller` (سُلطَةُ الإدارَة، أَيُّ مَرحَلَة)،
// و`CancelWholeInvoiceAsync` حينَ لا يَجِدُ ما تَدفَعُ لَه الفاتورَةُ يَرجِعُ إلى «الصَفقَةِ وَحدَها»، فَيُكتَبُ `gateway_refund_due` ويُقالُ لِلمُشتَري «لَم يُخصَم مِنكَ
// شَيء» — كَذِباً، يُناقِضُ «أ» S3·١٢. (٢) فاتورَةٌ بِصَفقَةٍ واحِدَةٍ يَرُدُّ حِفظَ إلغائِها تَعارُضٌ ⇒ `deal_changed_meanwhile` في اللوغِ وَحدَه: لا إعادَةَ ولا جَدوَلَة،
// والرِسالَةُ تُستَهلَكُ والطَلَبُ غَيرُ المَدفوعِ قائِمٌ بِفاتورَةٍ مُنتَهِيَة — الحالُ الَّتي وُجِدَ د5 (٤) لِيَمنَعَها.

public class GatewayExpiryRaceTests(ITestOutputHelper output)
{
    private static readonly DateTimeOffset Opened = new(2026, 10, 29, 8, 0, 0, TimeSpan.Zero);

    private static Deal Load(World w, Guid id) => w.Docs.Read<Deal>(id.ToString())!;

    /// <summary>سَلَّةٌ تُفتَحُ فاتورَتُها لِـ<paramref name="onInvoice"/> صَفقَةً مِنها عِندَ <see cref="Opened"/>، وبَوّابَةٌ بِساعَةٍ بَعدَ المُهلَةِ بِثانِيَة — نَسَقُ
    /// <c>GatewayInvoiceExpiryTests.OpenedCart</c>، والصَفقَتانِ مَربوطَتانِ بِبائِعٍ ومُلغي فاتورَةٍ عَلى مُعالِجِ العالَم.</summary>
    private static async Task<(World W, GatewayPayments Gateway, Guid[] OnInvoice)> ExpiredCart(int onInvoice = 2)
    {
        ProviderVault? sealedWith = null;
        var w = await PaidCart(v => { sealedWith = v; return PaymentsBinding(v); }, withSeller: true, invoice: false);
        var client = new MoyasarClient(new HttpClient(w.Moyasar), NullLogger<MoyasarClient>.Instance);
        var deals = new DealsService(w.Docs.Store, w.Platform,
            new GatewayInvoiceCloser(new TenantProviderService(w.Docs.Store), sealedWith!, client));
        GatewayPayments At(DateTimeOffset now) => new(deals, new TenantProviderService(w.Docs.Store), sealedWith!, client, null, clock: new FixedClock(now));

        var ids = w.Ids.Take(onInvoice).ToArray();
        w.Moyasar.Then(HttpStatusCode.Created, MoyasarClientTests.InvoiceJson("initiated", onInvoice == 2 ? 15000 : 10000, metadata: new Dictionary<string, string>()))
                 .Then(HttpStatusCode.OK, MoyasarClientTests.InvoiceJson("initiated", onInvoice == 2 ? 15000 : 10000, metadata: GatewayPaymentProof.MetadataFor(ids)));
        var opened = await At(Opened).OpenInvoiceAsync(Slug, new DealPayer(Buyer), ids, "مَتجَر", "https://store.example/r", new L(new HttpContextAccessor()));
        Assert.True(opened.Ok, opened.Code);
        w.Docs.Put(new ACommerce.Kit.Tenants.Tenant { Id = Slug, Name = "مَتجَر", OwnerUserId = Guid.NewGuid() });
        return (w with { Deals = deals }, At(Opened + TimeSpan.FromHours(24) + TimeSpan.FromSeconds(1)), ids);
    }

    private static string Invoice(Guid[] ids, string status, long amount = 15000)
        => MoyasarClientTests.InvoiceJson(status, amount, metadata: GatewayPaymentProof.MetadataFor(ids));

    /// <summary>ما يَكتُبُه إشعارُ الدَفعِ — بِالكاتِبِ القائِمِ نَفسِه وبِبُرهانِ البَوّابَة — أَثناءَ جَلبِ المُهلَة.</summary>
    private static Action PaidMeanwhile(World w, Guid[] ids) => () =>
    {
        var proven = GatewayPaymentProof.Gate(new MoyasarInvoice(InvoiceId, "paid", 15000, "SAR", null, GatewayPaymentProof.MetadataFor(ids)),
            ids.Select(id => Load(w, id)).ToArray()).Payment!;
        w.Deals.ConfirmGatewayPaymentAsync(Slug, proven).GetAwaiter().GetResult();
    };

    private static int NothingChargedNotices(World w, L l)
        => w.Docs.Stored.OfType<Notification>().Count(n => n.UserId == Buyer && n.Title == l["notifications.deal_payment_expired.title"]);

    /// <summary>
    /// <para><b>تَوصيفُ «اليَوم»</b> (القاعِدَة ٣، المَشهَدُ ١): جَلبُ المُهلَةِ يَرى «initiated» والإشعارُ يَكتُبُ الدَفعَ في الأَثناء — <b>تُلغى الصَفقَتانِ المَقبوضَتان</b>
    /// بِاسمِ المَنصَّةِ ويُكتَبُ لَهُما «استِرجاعُه مُستَحَقّ»، ويُقالُ لِلمُشتَري مَرَّتَينِ «لَم يُخصَم مِنكَ شَيء».</para>
    /// </summary>
    [Fact]
    public async Task Today_the_expiry_cancels_deals_captured_while_it_fetched_and_tells_the_buyer_nothing_was_charged()
    {
        var (w, gateway, ids) = await ExpiredCart();
        w.Moyasar.ThenDoing(PaidMeanwhile(w, ids), HttpStatusCode.OK, Invoice(ids, "initiated"))
                 .Then(HttpStatusCode.OK, Invoice(ids, "paid")).Then(HttpStatusCode.OK, Invoice(ids, "paid"));
        var l = new L(new HttpContextAccessor());

        await GatewayInvoiceExpiryHandler.Handle(new GatewayInvoiceExpiryDue(Slug, ids[0]), gateway, GatewayOwnerMoneyNoticeTests.Notices(w.Docs), l, CancellationToken.None);

        var deals = ids.Select(id => Load(w, id)).ToArray();
        output.WriteLine($"الحال: {string.Join(" · ", deals.Select(d => $"{d.Status} مَقبوضَة={DealsService.IsCaptured(d)}"))} · «لَم يُخصَم»: {NothingChargedNotices(w, l)}");
        Assert.All(deals, d =>
        {
            Assert.Equal(DealStatus.Cancelled, d.Status);
            Assert.True(DealsService.IsCaptured(d));
            Assert.Contains(d.Timeline, e => e.Action == DealEvent.GatewayRefundDue);
        });
        Assert.Equal(2, NothingChargedNotices(w, l));
    }

    /// <summary>
    /// <para><b>تَوصيفُ «اليَوم»</b> (القاعِدَة ٣، المَشهَدُ ٢): فاتورَةٌ بِصَفقَةٍ واحِدَةٍ انتَهَت مُهلَتُها، وحِفظُ إلغائِها يَرُدُّه تَعارُضُ نُسخَة — <b>لا إعادَة</b>: الصَفقَةُ
    /// قائِمَةٌ والرِسالَةُ استُهلِكَت، ولا إشعار.</para>
    /// </summary>
    [Fact]
    public async Task Today_a_single_deal_expiry_whose_cancel_hits_a_concurrent_write_leaves_the_unpaid_order_active()
    {
        var (w, gateway, ids) = await ExpiredCart(onInvoice: 1);
        w.Moyasar.Then(HttpStatusCode.OK, Invoice(ids, "initiated", 10000)).Then(HttpStatusCode.OK, Invoice(ids, "initiated", 10000))
                 .Then(HttpStatusCode.OK, Invoice(ids, "canceled", 10000));
        var l = new L(new HttpContextAccessor());
        var armed = true;
        w.Docs.BeforeMember = name =>
        {
            if (name != "SaveChangesAsync" || !armed) return;
            armed = false;
            // عالَمُ الذاكِرَةِ يُعيدُ الكائِنَ نَفسَه: يُرَدُّ ما كَتَبَه الإلغاءُ عَلَيه قَبلَ الحِفظِ كَما يَرُدُّه Marten — ويَكتُبُ الكاتِبُ الآخَرُ سَطرَه.
            var stored = Load(w, ids[0]);
            stored.Status = DealStatus.Active;
            stored.CancelReason = null;
            stored.Timeline.RemoveAt(stored.Timeline.Count - 1);
            stored.Timeline.Add(new(stored.Stage, stored.Stage, DealEvent.RefNote, null, null, "كاتِبٌ آخَر", DateTime.UtcNow));
            throw new JasperFx.ConcurrencyException(typeof(Deal), ids[0]);
        };

        var res = await gateway.ExpireAsync(Slug, ids[0], l);

        var deal = Load(w, ids[0]);
        output.WriteLine($"المُهلَة: {res.Code} · أُلغِيَ {res.Expired.Count} · الصَفقَة {deal.Status}");
        Assert.Empty(res.Expired);
        Assert.Equal(DealStatus.Active, deal.Status);
        Assert.False(armed);
    }
}
