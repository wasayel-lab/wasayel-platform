using ACommerce.Platform.I18n;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using Microsoft.AspNetCore.Http;
using Xunit;
using Xunit.Abstractions;
using static ACommerce.Platform.Tests.TenantGatewayEndpointsTests;

namespace ACommerce.Platform.Tests;

// ═══ «تَحَقَّق» يُعيدُ إخبارَ ما أَلغَته المُهلَةُ ولَم يُخبَر بِه — مَرَّة (‏2026-11-05، الموجَة ١٢ G3 · ٩ — مُراجِعُ F3 عَلى 21557d1؛ تَوصيفُه `21fe53d`) ═══
//
// **القَرار** (د8 — إشعارُ البائِعِ عَلى كُلِّ إلغاء؛ P1): `VerifyAsync` يَضُمُّ إلى «أُلغِيَ الآن» ما تَصدُقُ عَلَيه `ExpiredUntold` مِن صَفَقاتِ الفاتورَة — بَعدَ السُلطَةِ وقَبلَ الجَلب،
// بِنَسَقِ `ExpireAsync` (`With(...)`) — فَالنُقطَةُ تُخبِرُ بِه وتَسِمُه، و«تَحَقَّق» تالٍ لا يُعيدُه. ومِلَفُّ التَوصيفِ `GatewayVerifyUntoldTests.cs` بِبَصمَتِه قَبلُ وبَعدُ.

public class GatewayVerifyUntoldSwitchTests(ITestOutputHelper output)
{
    private static Deal Load(World w, Guid id) => w.Docs.Read<Deal>(id.ToString())!;

    /// <summary><b>المُوجِب</b>: الإشعارُ الأَوَّلُ رَمى ⇒ «تَحَقَّق» الثاني يُرجِعُ الاثنَتَينِ فَيُخبَرُ المُشتَري وكُلُّ بائِعٍ مَرَّةً وتُوسَمان؛ و«تَحَقَّق» ثالِثٌ لا يُرجِعُ شَيئاً ولا يُخبِرُ أَحَداً.</summary>
    [Fact]
    public async Task A_second_verify_tells_once_what_the_first_cancelled_when_its_notice_threw()
    {
        var (w, gateway, ids) = await GatewayVerifyUntoldTests.ExpiredCartForVerify();
        var l = new L(new HttpContextAccessor());
        var notices = GatewayOwnerMoneyNoticeTests.Notices(w.Docs);

        var first = await gateway.VerifyAsync(Slug, new DealPayer(Buyer), ids[0].ToString("N"), l);
        var fired = GatewayExpiryNoticeRetryTests.FailFirstNotificationSave(w);
        var thrown = await Record.ExceptionAsync(() => notices.NotifyPaymentExpiredAsync(Slug, first.Expired, l, told: id => gateway.MarkExpiryToldAsync(Slug, id)));
        var second = await gateway.VerifyAsync(Slug, new DealPayer(Buyer), ids[0].ToString("N"), l);
        await notices.NotifyPaymentExpiredAsync(Slug, second.Expired, l, told: id => gateway.MarkExpiryToldAsync(Slug, id));
        var afterSecond = GatewayVerifyUntoldTests.Told(w, l);
        var third = await gateway.VerifyAsync(Slug, new DealPayer(Buyer), ids[0].ToString("N"), l);
        await notices.NotifyPaymentExpiredAsync(Slug, third.Expired, l, told: id => gateway.MarkExpiryToldAsync(Slug, id));

        output.WriteLine($"الأَوَّل: {first.Code} أُلغِيَ {first.Expired.Count} · الإشعارُ رَمى: {thrown?.GetType().Name} · الثاني: {second.Code} أُلغِيَ {second.Expired.Count} · " +
                         $"بَعدَه المُشتَري/البائِع {afterSecond} · مَوسومَة {ids.Count(id => Load(w, id).Refs.ContainsKey(GatewayPayments.ExpiryToldRef))} · " +
                         $"الثالِث: أُلغِيَ {third.Expired.Count} · بَعدَه {GatewayVerifyUntoldTests.Told(w, l)}");
        Assert.True(fired());
        Assert.IsType<InvalidOperationException>(thrown);
        Assert.Equal(ids.OrderBy(x => x), second.Expired.OrderBy(x => x));
        Assert.Equal((2, 2), afterSecond);
        Assert.All(ids, id => Assert.Equal("1", Load(w, id).Refs[GatewayPayments.ExpiryToldRef]));
        Assert.Empty(third.Expired);
        Assert.Equal((2, 2), GatewayVerifyUntoldTests.Told(w, l));
    }

    /// <summary><b>والسُلطَةُ قَبلَه</b>: «تَحَقَّق» مِن غَيرِ المُشتَري عَلى الصَفقَةِ نَفسِها يُرَدُّ <c>actor_not_payer</c> ولا يُرجِعُ ما لَم يُخبَر بِه — فَلا يُخبِرُ أَحَداً بِطَلَبِ غَيرِه.</summary>
    [Fact]
    public async Task A_verify_by_someone_else_is_refused_before_the_untold_are_read()
    {
        var (w, gateway, ids) = await GatewayVerifyUntoldTests.ExpiredCartForVerify();
        var l = new L(new HttpContextAccessor());
        var notices = GatewayOwnerMoneyNoticeTests.Notices(w.Docs);
        var first = await gateway.VerifyAsync(Slug, new DealPayer(Buyer), ids[0].ToString("N"), l);
        GatewayExpiryNoticeRetryTests.FailFirstNotificationSave(w);
        await Record.ExceptionAsync(() => notices.NotifyPaymentExpiredAsync(Slug, first.Expired, l, told: id => gateway.MarkExpiryToldAsync(Slug, id)));

        var stranger = await gateway.VerifyAsync(Slug, new DealPayer(Guid.NewGuid()), ids[0].ToString("N"), l);

        Assert.Equal(DealPayAuthorization.ActorNotPayer, stranger.Code);
        Assert.Empty(stranger.Expired);
        Assert.All(ids, id => Assert.True(GatewayPayments.ExpiredUntold(Load(w, id))));
    }
}
