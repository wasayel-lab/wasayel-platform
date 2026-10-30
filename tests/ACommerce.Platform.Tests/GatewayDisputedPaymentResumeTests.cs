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
    /// <para><b>قُلِبَ بِاسمِه</b> (‏2026-10-30، تَدقيقُ الموجَة ١١ C-PAY-3؛ تَوصيفُه <c>dd1c5df</c> — كانَ
    /// <c>Today_a_deal_paid_during_its_dispute_is_resumed_at_Confirmed_waiting_for_the_buyer_and_its_vendor_never_hears_paid</c>): دُفِعَت الفاتورَةُ والصَفقَةُ الأولى
    /// في «نِزاع» — كُتِبَ دَفعُها، والثانِيَةُ دَخَلَت «دَفع» وأُخبِرَ بائِعُها؛ ثُمَّ استُؤنِفَت الأولى ⇒ <b>تَدخُلُ «دَفع» بِاسمِ المَنصَّةِ في الاستِئنافِ نَفسِه</b>، ويُخبَرُ
    /// بائِعُها بِالدَفعِ <b>مَرَّةً واحِدَة</b> بَعدَ إشعارِ الحَسم؛ ونِزاعٌ ثانٍ ثُمَّ استِئنافٌ ثانٍ لا يُعيدُ «دُفِع» — آخِرُ «disputed» صارَ بَعدَ الدَفع.</para>
    /// </summary>
    [Fact]
    public async Task A_deal_paid_during_its_dispute_enters_Paid_on_resume_and_its_vendor_hears_paid_once()
    {
        var w = await ConfirmedCart();
        var notices = GatewayOwnerMoneyNoticeTests.Notices(w.Docs);
        var l = new L(new HttpContextAccessor());
        Assert.True((await w.Deals.DisputeAsync(Slug, w.Ids[0], AppOwner, "مالِك التَّطبيق", "شَكوى")).Ok);
        await PayAsync(w, notices, l);
        Assert.Equal(DealStage.Paid, Load(w, w.Ids[1]).Stage);
        Assert.Equal(1, PaidNoticesFor(w, w.Ids[1]));
        Assert.Equal(0, PaidNoticesFor(w, w.Ids[0]));

        var res = await w.Deals.ResolveDisputeAsync(Slug, w.Ids[0], Owner, DealDisputeResolution.Resume, "حُسِم");
        await notices.NotifyDisputeResolvedAsync(Slug, res.Deal!, AppOwner, res.Outcome!, l);

        var deal = Load(w, w.Ids[0]);
        output.WriteLine($"الاستِئناف: ok={res.Ok} · {deal.Status}/{deal.Stage} · النَتيجَةُ تَحمِلُ {res.Deal!.Stage} · «دُفِع» لِلبائِع={PaidNoticesFor(w, w.Ids[0])}");
        Assert.True(res.Ok);
        Assert.Equal((DealStatus.Active, DealStage.Paid), (deal.Status, deal.Stage));
        Assert.Equal(DealStage.Paid, res.Deal!.Stage);
        var entry = Assert.Single(deal.Timeline, e => e.Action == "advanced" && e.StageAfter == DealStage.Paid);
        Assert.True(DealEvent.IsByPlatform(entry));
        Assert.Equal(1, PaidNoticesFor(w, w.Ids[0]));
        Assert.Equal(1, PaidNoticesFor(w, w.Ids[1]));
        Assert.Empty(w.Platform.Calls);

        // ونِزاعٌ ثانٍ ثُمَّ استِئنافٌ ثانٍ: الدَفعُ قَبلَ آخِرِ نِزاعٍ فَأُخبِرَ بِه — لا «دُفِع» ثانٍ.
        Assert.True((await w.Deals.DisputeAsync(Slug, w.Ids[0], AppOwner, "مالِك التَّطبيق", "شَكوى ثانِيَة")).Ok);
        var again = await w.Deals.ResolveDisputeAsync(Slug, w.Ids[0], Owner, DealDisputeResolution.Resume, "حُسِم");
        await notices.NotifyDisputeResolvedAsync(Slug, again.Deal!, AppOwner, again.Outcome!, l);
        Assert.Equal(1, PaidNoticesFor(w, w.Ids[0]));
    }

    /// <summary><b>السالِب</b>: صَفقَةٌ لَم تُدفَع فاتورَتُها تُستَأنَفُ عِندَ «تأكيد» كَما كانَت — لا «دَفع» ولا «دُفِع»؛ ولا تَصدُقُ <see cref="DealNotices.PaidDuringDispute"/>
    /// لِدَفعٍ قَبلَ النِزاع، ولا لِحَجزِ مُزَوِّدِ النُسخَة.</summary>
    [Fact]
    public async Task Resuming_an_unpaid_disputed_deal_enters_nothing_and_tells_nobody_of_a_payment()
    {
        var w = await ConfirmedCart();
        var notices = GatewayOwnerMoneyNoticeTests.Notices(w.Docs);
        var l = new L(new HttpContextAccessor());
        Assert.True((await w.Deals.DisputeAsync(Slug, w.Ids[0], AppOwner, "مالِك التَّطبيق", "شَكوى")).Ok);

        var res = await w.Deals.ResolveDisputeAsync(Slug, w.Ids[0], Owner, DealDisputeResolution.Resume, "حُسِم");
        await notices.NotifyDisputeResolvedAsync(Slug, res.Deal!, AppOwner, res.Outcome!, l);

        var deal = Load(w, w.Ids[0]);
        Assert.Equal((DealStatus.Active, DealStage.Confirmed), (deal.Status, deal.Stage));
        Assert.False(DealNotices.PaidDuringDispute(deal));
        Assert.Equal(0, PaidNoticesFor(w, w.Ids[0]));

        // والدالَّةُ النَقِيَّة: دَفعٌ ثُمَّ نِزاعٌ ⇐ لا؛ ونِزاعٌ ثُمَّ دَفعٌ ⇐ نَعَم؛ وبِلا فاتورَةِ بَوّابَةٍ ⇐ لا.
        static Deal With(params string[] actions)
        {
            var d = new Deal { Refs = { [GatewayPaymentProof.InvoiceRef] = InvoiceId, ["payment_id"] = InvoiceId } };
            foreach (var a in actions) d.Timeline.Add(new(DealStage.Confirmed, DealStage.Confirmed, a, null, null, null, DateTime.UtcNow));
            return d;
        }
        Assert.False(DealNotices.PaidDuringDispute(With(DealEvent.GatewayPaid, "disputed")));
        Assert.True(DealNotices.PaidDuringDispute(With("disputed", DealEvent.GatewayPaid)));
        Assert.False(DealNotices.PaidDuringDispute(With(DealEvent.GatewayPaid)));
        var card = With("disputed", DealEvent.GatewayPaid);
        card.Refs.Remove(GatewayPaymentProof.InvoiceRef);
        Assert.False(DealNotices.PaidDuringDispute(card));
    }
}
