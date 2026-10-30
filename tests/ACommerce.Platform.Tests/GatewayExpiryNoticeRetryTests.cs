using System.Net;
using ACommerce.Kit.Notifications;
using ACommerce.Platform.I18n;
using ACommerce.Templates.Customer.Marketplace.Services;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using Microsoft.AspNetCore.Http;
using Xunit;
using Xunit.Abstractions;
using static ACommerce.Platform.Tests.TenantGatewayEndpointsTests;

namespace ACommerce.Platform.Tests;

// ═══ إشعاراتُ المُهلَةِ حينَ تَفشَلُ خُطوَةُ الإشعار (‏2026-10-30، جَولَةُ الإصلاحِ الأولى لِلمَسار F3 — مُراجَعَةُ CO2 / المُرَحَّلُ [S3·١]) ═══════════════════
//
// **الحالُ المَقيسَة**: عَلامَةُ `gateway_expiry_told` تُكتَبُ **قَبلَ** الإشعار (`ClaimExpiryNoticesAsync`، `f97e6c7`) — مُطالَبَةٌ مَرَّةً عَلى الأَكثَر. فَعَطَبٌ في خُطوَةِ
// الإشعارِ نَفسِها (انقِطاعٌ في القاعِدَةِ عِندَ حِفظِ إشعارِ البائِع) يَرمي المُعالِج، فَتُعيدُه السِياسَة — والإعادَةُ تَجِدُ الصَفَقاتِ مُطالَباً بِها ولا قائِمَ يُلغى، فَتُخبِرُ
// صِفراً وتَفرُغُ بِنَجاح: لا رِسالَةَ ميتَةً ولا إشعارَ لِصاحِبِ المَتجَرِ ولا سَطرَ خَطَأٍ يُسَمّي ما ضاع. وكَذلكَ بابُ «تَحَقَّق»: يُطالِبُ قَبلَ أَن يُخبِرَ المُنادي، فَإشعارٌ يَرمي
// هُناكَ يَترُكُ الرِسالَةَ المُجَدوَلَةَ لا تُخبِرُ أَحَداً. وصَفُّ `f97e6c7` قاسَ انقِطاعاً **قَبلَ** المُطالَبَةِ وَحدَه. وقَرارُ المالِك د5 (٤): «ويُخبَر المشتري وكل بائع فيه».

public class GatewayExpiryNoticeRetryTests(ITestOutputHelper output)
{
    private static Deal Load(World w, Guid id) => w.Docs.Read<Deal>(id.ToString())!;

    /// <summary>أَوَّلُ حِفظٍ لِإشعارٍ يَرمي مَرَّةً — كَما يَرُدُّه Marten: الإشعارُ لا يُحفَظ (يُرفَعُ مِن عالَمِ الذاكِرَةِ قَبلَ الرَمي). وما بَعدَه يَمضي.</summary>
    internal static Func<bool> FailFirstNotificationSave(World w)
    {
        var armed = true;
        w.Docs.BeforeMember = name =>
        {
            if (!armed || name != "SaveChangesAsync" || w.Docs.Stored.LastOrDefault() is not Notification n) return;
            armed = false;
            w.Docs.Stored.RemoveAt(w.Docs.Stored.Count - 1);
            w.Docs.Docs.Remove(DocWorld.KeyOf(typeof(Notification), DocWorld.IdOf(n)));
            throw new InvalidOperationException("انقِطاعٌ في القاعِدَةِ عِندَ حِفظِ الإشعار");
        };
        return () => !armed;
    }

    private static (int Buyer, int Seller) Told(World w, L l)
    {
        var notes = w.Docs.Stored.OfType<Notification>().ToArray();
        return (notes.Count(n => n.UserId == Buyer && n.Title == l["notifications.deal_payment_expired.title"]),
                notes.Count(n => n.UserId == Seller && n.Title == l["notifications.deal_cancelled_for_vendor.title"]));
    }

    /// <summary>
    /// <para><b>«اليَوم»</b>: المُعالِجُ يُلغي الصَفقَتَينِ ويُطالِبُ بِإشعارِهِما، ثُمَّ يَرمي حِفظُ أَوَّلِ إشعارٍ (البائِعُ في الصَفقَةِ الأولى) ⇒ يَرمي المُعالِج؛ والإعادَةُ — ما تَفعَلُه
    /// السِياسَة — <b>تَفرُغُ بِنَجاحٍ ولا تُخبِرُ أَحَداً</b>: المُشتَري ×0 والبائِعُ ×0 والعَلامَةُ عَلى الصَفقَتَين؛ وثالِثَةٌ كَذلك.</para>
    /// </summary>
    [Fact]
    public async Task Today_a_notify_step_that_throws_leaves_the_retry_telling_nobody()
    {
        var (w, gateway, ids) = await GatewayExpiryRaceTests.ExpiredCart();
        var l = new L(new HttpContextAccessor());
        var notices = GatewayOwnerMoneyNoticeTests.Notices(w.Docs);
        w.Moyasar.Then(HttpStatusCode.OK, GatewayExpiryRaceTests.Invoice(ids, "initiated")).Then(HttpStatusCode.OK, GatewayExpiryRaceTests.Invoice(ids, "initiated"))
                 .Then(HttpStatusCode.OK, GatewayExpiryRaceTests.Invoice(ids, "initiated")).Then(HttpStatusCode.OK, GatewayExpiryRaceTests.Invoice(ids, "canceled"))
                 .Then(HttpStatusCode.OK, GatewayExpiryRaceTests.Invoice(ids, "canceled")).Then(HttpStatusCode.OK, GatewayExpiryRaceTests.Invoice(ids, "canceled"));
        var fired = FailFirstNotificationSave(w);
        var due = new GatewayInvoiceExpiryDue(Slug, ids[0]);

        var first = await Record.ExceptionAsync(() => GatewayInvoiceExpiryHandler.Handle(due, gateway, notices, l, CancellationToken.None));
        var afterFirst = Told(w, l);
        var retry = await Record.ExceptionAsync(() => GatewayInvoiceExpiryHandler.Handle(due, gateway, notices, l, CancellationToken.None));
        var afterRetry = Told(w, l);
        var third = await Record.ExceptionAsync(() => GatewayInvoiceExpiryHandler.Handle(due, gateway, notices, l, CancellationToken.None));

        output.WriteLine($"الأولى رَمَت: {first?.GetType().Name} · بَعدَها المُشتَري/البائِع {afterFirst} · الإعادَةُ رَمَت: {retry?.GetType().Name ?? "لا شَيء"} · بَعدَها {afterRetry} · " +
                         $"الثالِثَة: {third?.GetType().Name ?? "لا شَيء"} {Told(w, l)} · الحال: [{string.Join(", ", ids.Select(id => Load(w, id).Status))}] · " +
                         $"العَلامَة: [{string.Join(", ", ids.Select(id => Load(w, id).Refs.GetValueOrDefault(GatewayPayments.ExpiryToldRef) ?? "—"))}]");
        Assert.True(fired());
        Assert.IsType<InvalidOperationException>(first);
        Assert.All(ids, id => Assert.Equal(DealStatus.Cancelled, Load(w, id).Status));
        Assert.All(ids, id => Assert.Equal("1", Load(w, id).Refs[GatewayPayments.ExpiryToldRef]));
        Assert.Equal((0, 0), afterFirst);
        Assert.Null(retry);
        Assert.Equal((0, 0), afterRetry);
        Assert.Null(third);
        Assert.Equal((0, 0), Told(w, l));
    }

    /// <summary>
    /// <para><b>«اليَوم»</b> — بابُ «تَحَقَّق»: يُلغي عِندَ المُهلَةِ ويُطالِبُ بِإشعارِه قَبلَ أَن يُعيدَ، ثُمَّ يَرمي حِفظُ أَوَّلِ إشعارٍ في خُطوَةِ النُقطَة (السَطرُ نَفسُه:
    /// <c>NotifyPaymentExpiredAsync(slug, res.Expired, l)</c>) ⇒ <b>الرِسالَةُ المُجَدوَلَةُ حينَ تَحينُ لا تَجِدُ ما تُخبِرُ بِه</b>: المُشتَري ×0 والبائِعُ ×0.</para>
    /// </summary>
    [Fact]
    public async Task Today_a_verify_whose_notice_throws_leaves_the_scheduled_message_telling_nobody()
    {
        var (w, gateway, ids) = await GatewayExpiryRaceTests.ExpiredCart();
        var l = new L(new HttpContextAccessor());
        var notices = GatewayOwnerMoneyNoticeTests.Notices(w.Docs);
        w.Moyasar.Then(HttpStatusCode.OK, GatewayExpiryRaceTests.Invoice(ids, "initiated")).Then(HttpStatusCode.OK, GatewayExpiryRaceTests.Invoice(ids, "initiated"))
                 .Then(HttpStatusCode.OK, GatewayExpiryRaceTests.Invoice(ids, "initiated")).Then(HttpStatusCode.OK, GatewayExpiryRaceTests.Invoice(ids, "canceled"))
                 .Then(HttpStatusCode.OK, GatewayExpiryRaceTests.Invoice(ids, "canceled"));
        var verified = await gateway.VerifyAsync(Slug, new DealPayer(Buyer), ids[0].ToString("N"), l);
        var fired = FailFirstNotificationSave(w);

        var thrown = await Record.ExceptionAsync(() => notices.NotifyPaymentExpiredAsync(Slug, verified.Expired, l));
        await GatewayInvoiceExpiryHandler.Handle(new GatewayInvoiceExpiryDue(Slug, ids[0]), gateway, notices, l, CancellationToken.None);

        output.WriteLine($"التَحَقُّق: {verified.Code} أُلغِيَ {verified.Expired.Count} · الإشعارُ رَمى: {thrown?.GetType().Name} · بَعدَ الرِسالَة المُشتَري/البائِع {Told(w, l)}");
        Assert.True(fired());
        Assert.Equal(GatewayReturn.InvoiceExpired, verified.Code);
        Assert.Equal(2, verified.Expired.Count);
        Assert.IsType<InvalidOperationException>(thrown);
        Assert.Equal((0, 0), Told(w, l));
    }
}
