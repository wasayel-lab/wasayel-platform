using System.Net;
using ACommerce.Platform.I18n;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using Microsoft.AspNetCore.Http;
using Xunit;
using Xunit.Abstractions;
using static ACommerce.Platform.Tests.TenantGatewayEndpointsTests;

namespace ACommerce.Platform.Tests;

// ═══ «تَحَقَّق» بَعدَ إلغاءٍ بِالمُهلَةِ رَمى إشعارُه — تَوصيفُ «اليَوم» (‏2026-11-05، الموجَة ١٢ G3 · ٩ — مُراجِعُ F3، صُغرى عَلى 21557d1) ═══
//
// **المَقيس** (القاعِدَة ٣ — يُثَبَّتُ ما يَبقى ويُطبَعُ ما يَتَغَيَّر): `ExpireAsync` يَضُمُّ إلى نَتيجَتِه ما أَلغَته المُهلَةُ ولَم يُخبَر بِه (`ExpiredUntoldAsync` بِنَسَقِ `With(...)`)،
// و`VerifyAsync` لا — فَتَحَقُّقٌ أَلغى عِندَ المُهلَةِ ثُمَّ رَمى إشعارُه في النُقطَةِ يَترُكُ الصَفقَتَينِ بِلا عَلامَة، و«تَحَقَّق» الثاني يُرجِعُ «أُلغِيَ الآن» فارِغاً فَلا يُخبَرُ المُشتَري
// ولا البائِع (د8: إشعارُ البائِعِ عَلى كُلِّ إلغاء) — إلّا أَن تَحينَ رِسالَةُ المُهلَة. **وما يَبقى**: الأَوَّلُ يُلغي الاثنَتَينِ ويُرجِعُهُما، والإشعارُ الرامي لا يَسِمُ شَيئاً، و«تَحَقَّق»
// بَعدَ إشعارٍ تَمَّ لا يُعيدُ أَحَداً.

public class GatewayVerifyUntoldTests(ITestOutputHelper output)
{
    private static Deal Load(World w, Guid id) => w.Docs.Read<Deal>(id.ToString())!;

    /// <summary>المُشتَري يُخبَرُ بِإلغاءِ المُهلَةِ والبائِعُ بِإلغاءِ صَفقَتِه — عَدُّ كُلٍّ بِعُنوانِه.</summary>
    internal static (int Buyer, int Seller) Told(World w, L l)
    {
        var notes = w.Docs.Stored.OfType<ACommerce.Kit.Notifications.Notification>().ToArray();
        return (notes.Count(n => n.UserId == Buyer && n.Title == l["notifications.deal_payment_expired.title"]),
                notes.Count(n => n.UserId == Seller && n.Title == l["notifications.deal_cancelled_for_vendor.title"]));
    }

    /// <summary>سَلَّةٌ مِن صَفقَتَينِ عَلى فاتورَةٍ انتَهَت مُهلَتُها — وما تُجيبُ بِه ميسر: «initiated» حَتّى تُغلِقَها المَنصَّة، ثُمَّ «canceled» لِكُلِّ جَلبٍ بَعدَه.</summary>
    internal static async Task<(World W, GatewayPayments Gateway, Guid[] Ids)> ExpiredCartForVerify()
    {
        var (w, gateway, ids) = await GatewayExpiryRaceTests.ExpiredCart();
        w.Moyasar.Then(HttpStatusCode.OK, GatewayExpiryRaceTests.Invoice(ids, "initiated")).Then(HttpStatusCode.OK, GatewayExpiryRaceTests.Invoice(ids, "initiated"))
                 .Then(HttpStatusCode.OK, GatewayExpiryRaceTests.Invoice(ids, "initiated"));
        for (var i = 0; i < 8; i++) w.Moyasar.Then(HttpStatusCode.OK, GatewayExpiryRaceTests.Invoice(ids, "canceled"));
        return (w, gateway, ids);
    }

    /// <summary><b>«تَحَقَّق» يُلغي عِندَ المُهلَةِ ويَرمي إشعارُه، ثُمَّ «تَحَقَّق» ثانٍ</b>: ما يَبقى — الأَوَّلُ <c>invoice_expired</c> يُرجِعُ الاثنَتَين، والإشعارُ يَرمي ولا عَلامَة. ويُطبَعُ
    /// ما يَتَغَيَّر: ما يُرجِعُه الثاني «أُلغِيَ الآن»، ومَن يُخبَرُ بَعدَه.</summary>
    [Fact]
    public async Task A_second_verify_after_an_expiry_cancel_whose_notice_threw()
    {
        var (w, gateway, ids) = await ExpiredCartForVerify();
        var l = new L(new HttpContextAccessor());
        var notices = GatewayOwnerMoneyNoticeTests.Notices(w.Docs);

        var first = await gateway.VerifyAsync(Slug, new DealPayer(Buyer), ids[0].ToString("N"), l);
        var fired = GatewayExpiryNoticeRetryTests.FailFirstNotificationSave(w);
        var thrown = await Record.ExceptionAsync(() => notices.NotifyPaymentExpiredAsync(Slug, first.Expired, l, told: id => gateway.MarkExpiryToldAsync(Slug, id)));
        var marked = ids.Count(id => Load(w, id).Refs.ContainsKey(GatewayPayments.ExpiryToldRef));
        var second = await gateway.VerifyAsync(Slug, new DealPayer(Buyer), ids[0].ToString("N"), l);
        await notices.NotifyPaymentExpiredAsync(Slug, second.Expired, l, told: id => gateway.MarkExpiryToldAsync(Slug, id));

        output.WriteLine($"الأَوَّل: {first.Code} أُلغِيَ {first.Expired.Count} · الإشعارُ رَمى: {thrown?.GetType().Name} · مَوسومَة {marked} · الثاني: {second.Code ?? "ok"} أُلغِيَ {second.Expired.Count} · " +
                         $"بَعدَه المُشتَري/البائِع {Told(w, l)} · مَوسومَة {ids.Count(id => Load(w, id).Refs.ContainsKey(GatewayPayments.ExpiryToldRef))}");
        Assert.True(fired());
        Assert.Equal(GatewayReturn.InvoiceExpired, first.Code);
        Assert.Equal(ids.OrderBy(x => x), first.Expired.OrderBy(x => x));
        Assert.IsType<InvalidOperationException>(thrown);
        Assert.Equal(0, marked);
        Assert.All(ids, id => Assert.Equal(DealStatus.Cancelled, Load(w, id).Status));
    }

    /// <summary><b>السالِب — لا يَتَغَيَّرُ بِحَرف</b>: إشعارُ الأَوَّلِ تَمَّ فَوُسِمَت الاثنَتان ⇒ «تَحَقَّق» الثاني لا يُرجِعُ شَيئاً ولا يُخبَرُ أَحَدٌ ثانِيَة.</summary>
    [Fact]
    public async Task A_second_verify_after_a_told_expiry_cancel_tells_nobody_again()
    {
        var (w, gateway, ids) = await ExpiredCartForVerify();
        var l = new L(new HttpContextAccessor());
        var notices = GatewayOwnerMoneyNoticeTests.Notices(w.Docs);

        var first = await gateway.VerifyAsync(Slug, new DealPayer(Buyer), ids[0].ToString("N"), l);
        await notices.NotifyPaymentExpiredAsync(Slug, first.Expired, l, told: id => gateway.MarkExpiryToldAsync(Slug, id));
        var afterFirst = Told(w, l);
        var second = await gateway.VerifyAsync(Slug, new DealPayer(Buyer), ids[0].ToString("N"), l);
        await notices.NotifyPaymentExpiredAsync(Slug, second.Expired, l, told: id => gateway.MarkExpiryToldAsync(Slug, id));

        Assert.Equal((2, 2), afterFirst);
        Assert.All(ids, id => Assert.Equal("1", Load(w, id).Refs[GatewayPayments.ExpiryToldRef]));
        Assert.Empty(second.Expired);
        Assert.Equal((2, 2), Told(w, l));
    }
}
