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

// ═══ طَلَبٌ مَدفوعٌ أُلغِيَ، وفاتورَةٌ تَعَذَّرَ إلغاؤها — مَن يَعلَمُ أَنّ عَلَيه أَن يَفعَل؟ (قَرارُ المالِك د3، ‏2026-10-29) ═══════════════════
//
// **القَرار (د3 «أ» (١) و«د» (٢))**: الاستِرجاعُ يَبقى يَدَوِيّاً مِن لَوحَةِ ميسر — «سطر في سجلّ الطلب يراه المشتري وصاحب المتجر، ومعه إشعار لصاحب
// المتجر بكل استرجاع مستحقّ وبكل فاتورة تعذّر إلغاؤها». **ما فُحِصَ في الكود**: `DealsService` يَكتُبُ سَطرَ `gateway_refund_due` عَلى الطَلَبِ المَدفوعِ
// المُلغى (الإلغاءُ بَعدَ الدَفع، والتَأكيدُ بَعدَ الإلغاء)، و`GatewayInvoiceCloser` لا يَفعَلُ عِندَ الفَشَلِ إلّا سَطرَ لوغ، وإشعاراتُ الإلغاءِ لِلمُشتَري
// (ولِلبائِعِ مُنذُ د8) لا لِصاحِبِ المَتجَر.
//
// **التَوصيفُ قَبلَ التَبديل** (القاعِدَة ٣): صُفوفُ «Today_» أَدناه تُودَعُ وَحدَها، وتُقلَبُ بِأَسمائِها في كوميتِ التَبديل. وصاحِبُ المَتجَرِ هُنا
// `Tenant.OwnerUserId` — مَن رَبَطَ مِفتاحَ ميسر في الاستوديو، ولِلوَحَتِه يُحالُ الاستِرجاع.

public class GatewayOwnerMoneyNoticeTests(ITestOutputHelper output)
{
    internal static readonly Guid Owner = Guid.Parse("dddddddd-dddd-4ddd-8ddd-dddddddddddd");

    private static Deal Load(World w, Guid id) => w.Docs.Read<Deal>(id.ToString())!;

    internal static DealNotices Notices(DocWorld docs)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSignalR();
        var hub = services.BuildServiceProvider().GetRequiredService<IHubContext<ACommerce.Kit.Realtime.Server.RealtimeHub>>();
        return new DealNotices(docs.Store, hub, new WebPushService(new ConfigurationBuilder().Build(), NullLogger<WebPushService>.Instance));
    }

    /// <summary>عالَمُ السَلَّةِ المَدفوعَة ومَعَه وَثيقَةُ المَتجَرِ بِمالِكِها، وخِدمَةُ صَفَقاتٍ بِمُلغي فاتورَةٍ بِلا عَميلٍ — فَكُلُّ إغلاقٍ يَعودُ
    /// <see cref="MoyasarFailures.NotConfigured"/>: فاتورَةٌ تَعَذَّرَ إلغاؤها.</summary>
    internal static async Task<(World W, DealsService Deals)> OwnedCart()
    {
        var w = await PaidCart(withSeller: true);
        w.Docs.Put(new ACommerce.Kit.Tenants.Tenant { Id = Slug, Name = "مَتجَر", OwnerUserId = Owner });
        var closer = new GatewayInvoiceCloser(new TenantProviderService(w.Docs.Store), NewVault(), client: null);
        return (w, new DealsService(w.Docs.Store, w.Platform, closer));
    }

    private static int ToOwner(World w) => w.Docs.Stored.OfType<Notification>().Count(n => n.UserId == Owner);

    // ═══ «اليَوم» — تُقلَبُ بِأَسمائِها ═══════════════════════════════════════════════════════════════════════

    /// <summary><b>اليَوم</b>: صَفقَةٌ قَبَضَت البَوّابَةُ ثَمَنَها يُلغيها البائِعُ — سَطرُ «استِرجاعُه مُستَحَقّ» يُكتَب، وإشعارُ الإلغاءِ يَبلُغُ المُشتَري،
    /// <b>وصاحِبُ المَتجَرِ لا يُخبَرُ بِشَيء</b>؛ ونَصُّ سَطرِ المُشتَري يُحيلُه إلى «لَوحَةِ بَوّابَةِ المَتجَر» الَّتي لا يَملِكُها.</summary>
    [Fact]
    public async Task Today_a_refund_due_cancel_tells_the_store_owner_nothing_and_the_buyer_line_sends_him_to_the_store_dashboard()
    {
        var (w, deals) = await OwnedCart();
        await deals.AttachRefAsync(Slug, w.Ids[0], "payment_id", InvoiceId);
        await deals.AttachRefAsync(Slug, w.Ids[0], "payment_status", nameof(PaymentStatus.Captured));

        var res = await deals.CancelAsync(Slug, w.Ids[0], new DealCanceller(Seller, "بائِع", IsStoreAdmin: false), "نَفِدَت");
        Assert.True(res.Ok, res.Violation?.MessageAr);
        Assert.Single(Load(w, w.Ids[0]).Timeline, e => e.Action == DealEvent.GatewayRefundDue);
        await Notices(w.Docs).NotifyCancelledAsync(Slug, res.Deal!, Seller, "بائِع", new L(new HttpContextAccessor()));

        var buyerLine = LocaleCatalog.Text(LocaleCatalog.Arabic, "deals.detail.event_gateway_refund_due");
        output.WriteLine($"لِلمالِك: {ToOwner(w)} · لِلمُشتَري: {w.Docs.Stored.OfType<Notification>().Count(n => n.UserId == Buyer)} · سَطرُ المُشتَري: «{buyerLine}»");
        Assert.Equal(0, ToOwner(w));
        Assert.Equal("دُفِعَ ثَمَنُ طَلَبٍ مُلغًى: استِرجاعُه مِن لَوحَةِ بَوّابَةِ المَتجَر", buyerLine);
    }

    /// <summary><b>اليَوم</b>: آخِرُ صَفقَةٍ قائِمَةٍ عَلى فاتورَةٍ لَم تُدفَع تُلغى والفاتورَةُ لا تُلغى عِندَ ميسر (‏<c>gateway_not_configured</c>) — الإلغاءُ قائِم،
    /// <b>وسِجِلُّ الصَفقَةِ لا يَقولُ شَيئاً عَن فاتورَةٍ قَد تُدفَع</b>، وصاحِبُ المَتجَرِ لا يُخبَر.</summary>
    [Fact]
    public async Task Today_a_failed_invoice_cancel_writes_no_line_and_tells_the_store_owner_nothing()
    {
        var (w, deals) = await OwnedCart();
        await w.Deals.CancelAsync(Slug, w.Ids[1], new DealCanceller(Seller, "بائِع", IsStoreAdmin: false), "نَفِدَت");

        var res = await deals.CancelAsync(Slug, w.Ids[0], new DealCanceller(Seller, "بائِع", IsStoreAdmin: false), "نَفِدَت");
        Assert.True(res.Ok, res.Violation?.MessageAr);
        await Notices(w.Docs).NotifyCancelledAsync(Slug, res.Deal!, Seller, "بائِع", new L(new HttpContextAccessor()));

        var deal = Load(w, w.Ids[0]);
        output.WriteLine($"السِجِلّ: {string.Join(" · ", deal.Timeline.Select(e => e.Action))} · لِلمالِك: {ToOwner(w)}");
        Assert.Equal(DealStatus.Cancelled, deal.Status);
        Assert.Equal("cancelled", deal.Timeline[^1].Action);
        Assert.Equal(0, ToOwner(w));
    }

    /// <summary><b>اليَوم</b>: دَفعٌ يَثبُتُ عَلى صَفقَةٍ أُلغِيَت — التَأكيدُ يَكتُبُ سَطرَ «استِرجاعُه مُستَحَقّ» وإشعارُ الدَفعِ لا يَبلُغُ أَحَداً، وصاحِبُ المَتجَرِ لا يُخبَر.</summary>
    [Fact]
    public async Task Today_a_payment_proven_on_a_cancelled_deal_tells_the_store_owner_nothing()
    {
        var (w, deals) = await OwnedCart();
        foreach (var id in w.Ids)
            await w.Deals.CancelAsync(Slug, id, new DealCanceller(Buyer, "مُشتَرٍ", IsStoreAdmin: false), "عَدَلتُ");
        var proven = GatewayPaymentProof.Gate(new MoyasarInvoice(InvoiceId, "paid", 15000, "SAR", null, GatewayPaymentProof.MetadataFor(w.Ids)),
            w.Ids.Select(id => Load(w, id)).ToArray()).Payment!;

        var confirmed = await deals.ConfirmGatewayPaymentAsync(Slug, proven);
        var sent = await Notices(w.Docs).NotifyPaidAsync(Slug, confirmed.Select(d => d.Id).ToArray(), new L(new HttpContextAccessor()));

        output.WriteLine($"كُتِبَ عَلى {confirmed.Count} · إشعارُ الدَفع: {sent} · لِلمالِك: {ToOwner(w)}");
        Assert.Equal(2, confirmed.Count);
        Assert.All(confirmed, d => Assert.Contains(d.Timeline, e => e.Action == DealEvent.GatewayRefundDue));
        Assert.Equal(0, sent);
        Assert.Equal(0, ToOwner(w));
    }
}
