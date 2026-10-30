using ACommerce.Kit.Notifications;
using ACommerce.Kit.Payments;
using ACommerce.Platform.I18n;
using ACommerce.Templates.Customer.Marketplace.Services;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using Microsoft.AspNetCore.Http;
using Xunit;
using Xunit.Abstractions;
using static ACommerce.Platform.Tests.TenantGatewayEndpointsTests;

namespace ACommerce.Platform.Tests;

// ═══ دَفعٌ سُجِّلَ أَثناءَ النِزاع ثُمَّ استُؤنِفَت الصَفقَة (‏2026-10-30، تَدقيقُ الموجَة ١١ C-PAY-3 + E-deals-2) ═══════════════════════════════════
//
// **الحالُ المَقيسَة**: صَفقَةُ بِطاقَةٍ عِندَ «تأكيد» (تاليها «دَفع») يَفتَحُ صاحِبُ المَتجَرِ عَلَيها نِزاعاً والفاتورَةُ المُستَضافَةُ مَفتوحَة؛ يَدفَعُ المُشتَري، فَيَكتُبُ
// التَأكيدُ «Captured» و`gateway_paid` بِلا سُؤالٍ عَن الحال؛ و`EnterPaidOnRecordAsync` يَرُدُّ (لَيسَت `Active`)، و`NotifyPaidAsync` يَتَجاوَزُ البائِع (`PaidRecipient`).
// ثُمَّ يَستَأنِفُ صاحِبُ المَتجَر: الصَفقَةُ `Active` عِندَ «تأكيد» مَقبوضَةٌ وتاليها «دَفع» — تَنتَظِرُ «تأكيد: دفع» مِن المُشتَري، الحالُ الَّتي أَزالَها د4 «ب»
// («فلا ينتظر البائع نقرة من المشتري»)، والبائِعُ لا يَسمَعُ «دُفِع» أَبَداً. ومَواضِعُ `EnterPaidOnRecordAsync` ثَلاثَة (التَقَدُّم، والتَسوِيَة، والإشعار) — لا الاستِئناف.

public class GatewayDisputedPaymentResumeTests(ITestOutputHelper output)
{
    private static readonly Guid AppOwner = Guid.Parse("eeeeeeee-eeee-4eee-8eee-eeeeeeeeeeee");
    private static readonly DealCanceller Owner = new(AppOwner, "مالِك التَّطبيق", IsStoreAdmin: true);

    private static Deal Load(World w, Guid id) => w.Docs.Read<Deal>(id.ToString())!;

    /// <summary>صَفقَتا الفاتورَةِ عِندَ «تأكيد» بِالبائِعِ نَفسِه — المُحَرِّكُ لا الكِتابَةُ بِاليَد.</summary>
    private static async Task<World> ConfirmedCart()
    {
        var w = await PaidCart(withSeller: true);
        foreach (var id in w.Ids)
        {
            Assert.True((await w.Deals.AcceptAsync(Slug, id, new DealAcceptor(Seller, "بائِع"))).Ok);
            Assert.True((await w.Deals.AdvanceAsync(Slug, id, Seller, "بائِع")).Ok);
            Assert.Equal(DealStage.Confirmed, Load(w, id).Stage);
        }
        w.Docs.Put(new ACommerce.Kit.Tenants.Tenant { Id = Slug, Name = "مَتجَر", OwnerUserId = AppOwner });
        return w;
    }

    /// <summary>الدَفعُ يَثبُتُ ويُكتَبُ كَما تَكتُبُه التَسوِيَة (<c>SettleAsync</c>): الكاتِبُ ثُمَّ «دَفع» بِاسمِ المَنصَّةِ لِكُلِّ صَفقَة، ثُمَّ إشعارُ بائِعِ ما كُتِبَ الآن.</summary>
    private static async Task PayAsync(World w, DealNotices notices, L l)
    {
        var deals = w.Ids.Select(id => Load(w, id)).ToArray();
        var proven = GatewayPaymentProof.Gate(new MoyasarInvoice(InvoiceId, "paid", 15000, "SAR", null, GatewayPaymentProof.MetadataFor(w.Ids)), deals).Payment!;
        var confirmed = await w.Deals.ConfirmGatewayPaymentAsync(Slug, proven);
        foreach (var id in proven.DealIds) await w.Deals.EnterPaidOnRecordAsync(Slug, id);
        await notices.NotifyPaidAsync(Slug, confirmed.Select(d => d.Id).ToArray(), l);
    }

    private static int PaidNoticesFor(World w, Guid dealId)
        => w.Docs.Stored.OfType<Notification>().Count(n => n.Type == DealNotices.PaidType && n.UserId == Seller && n.RelatedUrl!.EndsWith(dealId.ToString(), StringComparison.Ordinal));

    /// <summary>
    /// <para><b>تَوصيفُ «اليَوم»</b> (القاعِدَة ٣): دُفِعَت الفاتورَةُ والصَفقَةُ الأولى في «نِزاع» — كُتِبَ دَفعُها، والثانِيَةُ دَخَلَت «دَفع» وأُخبِرَ بائِعُها؛ ثُمَّ استُؤنِفَت الأولى
    /// ⇒ <b>عِندَ «تأكيد» مَقبوضَةً وتاليها «دَفع»</b>، ولا إشعارَ «دُفِع» لِبائِعِها بَعدَ إشعارِ الحَسم.</para>
    /// </summary>
    [Fact]
    public async Task Today_a_deal_paid_during_its_dispute_is_resumed_at_Confirmed_waiting_for_the_buyer_and_its_vendor_never_hears_paid()
    {
        var w = await ConfirmedCart();
        var notices = GatewayOwnerMoneyNoticeTests.Notices(w.Docs);
        var l = new L(new HttpContextAccessor());
        Assert.True((await w.Deals.DisputeAsync(Slug, w.Ids[0], AppOwner, "مالِك التَّطبيق", "شَكوى")).Ok);
        await PayAsync(w, notices, l);
        Assert.Equal(DealStage.Paid, Load(w, w.Ids[1]).Stage);
        Assert.Equal(1, PaidNoticesFor(w, w.Ids[1]));

        var res = await w.Deals.ResolveDisputeAsync(Slug, w.Ids[0], Owner, DealDisputeResolution.Resume, "حُسِم");
        await notices.NotifyDisputeResolvedAsync(Slug, res.Deal!, AppOwner, res.Outcome!, l);

        var deal = Load(w, w.Ids[0]);
        output.WriteLine($"الاستِئناف: ok={res.Ok} · {deal.Status}/{deal.Stage} · مَقبوضَة={DealsService.IsCaptured(deal)} · التالي={DealsPolicy.Next(deal)} · «دُفِع» لِلبائِع={PaidNoticesFor(w, w.Ids[0])}");
        Assert.True(res.Ok);
        Assert.Equal((DealStatus.Active, DealStage.Confirmed), (deal.Status, deal.Stage));
        Assert.True(DealsService.IsCaptured(deal));
        Assert.Equal(DealStage.Paid, DealsPolicy.Next(deal));
        Assert.True(GatewayPayments.EntersPaidOnRecord(deal));
        Assert.Equal(0, PaidNoticesFor(w, w.Ids[0]));
    }
}
