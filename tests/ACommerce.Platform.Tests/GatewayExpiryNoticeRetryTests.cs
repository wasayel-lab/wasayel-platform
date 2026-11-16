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
//
// **والتَبديلُ** (P1): العَلامَةُ تُكتَبُ لِكُلِّ صَفقَةٍ **بَعدَ** إشعاراتِها (`GatewayPayments.MarkExpiryToldAsync` يُناديه `NotifyPaymentExpiredAsync` بِـ`told`)، و«ما يَنتَظِرُ
// الإشعار» هُوَ الإلغاءُ نَفسُه بِاسمِ المَنصَّةِ وسَبَبِ المُهلَة — يُكتَبُ مَعَه. فَالإشعارُ «مَرَّةً عَلى الأَقَلّ» بِمَنعِ تَكرارٍ لِكُلِّ صَفقَة.

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
    /// <para><b>قُلِبَ بِاسمِه</b> (تَوصيفُه <c>8e19697</c> — كانَ <c>Today_a_notify_step_that_throws_leaves_the_retry_telling_nobody</c>: الإعادَةُ تَفرُغُ بِنَجاحٍ ولا تُخبِرُ
    /// أَحَداً والعَلامَةُ [1, 1]): المُعالِجُ يُلغي الصَفقَتَين، ثُمَّ يَرمي حِفظُ أَوَّلِ إشعارٍ (البائِعُ في الصَفقَةِ الأولى) ⇒ يَرمي المُعالِجُ و<b>لا عَلامَةَ عَلى أَيٍّ مِنهُما</b>؛ والإعادَةُ
    /// — ما تَفعَلُه السِياسَة — <b>تُخبِرُ المُشتَريَ وكُلَّ بائِعٍ مَرَّةً</b> وتَسِمُ الاثنَتَين؛ والثالِثَةُ لا تُخبِرُ أَحَداً.</para>
    /// </summary>
    [Fact]
    public async Task A_notify_step_that_throws_is_retried_and_tells_the_buyer_and_every_vendor_once()
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
        var markedAfterFirst = ids.Select(id => Load(w, id).Refs.GetValueOrDefault(GatewayPayments.ExpiryToldRef)).ToArray();
        var retry = await Record.ExceptionAsync(() => GatewayInvoiceExpiryHandler.Handle(due, gateway, notices, l, CancellationToken.None));
        var afterRetry = Told(w, l);
        var third = await Record.ExceptionAsync(() => GatewayInvoiceExpiryHandler.Handle(due, gateway, notices, l, CancellationToken.None));

        output.WriteLine($"الأولى رَمَت: {first?.GetType().Name} · بَعدَها المُشتَري/البائِع {afterFirst} والعَلامَة [{string.Join(", ", markedAfterFirst.Select(m => m ?? "—"))}] · " +
                         $"الإعادَةُ رَمَت: {retry?.GetType().Name ?? "لا شَيء"} · بَعدَها {afterRetry} · الثالِثَة: {third?.GetType().Name ?? "لا شَيء"} {Told(w, l)} · " +
                         $"الحال: [{string.Join(", ", ids.Select(id => Load(w, id).Status))}] · العَلامَة: [{string.Join(", ", ids.Select(id => Load(w, id).Refs.GetValueOrDefault(GatewayPayments.ExpiryToldRef) ?? "—"))}]");
        Assert.True(fired());
        Assert.IsType<InvalidOperationException>(first);
        Assert.All(ids, id => Assert.Equal(DealStatus.Cancelled, Load(w, id).Status));
        Assert.All(markedAfterFirst, Assert.Null);
        Assert.Equal((0, 0), afterFirst);
        Assert.Null(retry);
        Assert.Equal((2, 2), afterRetry);
        Assert.All(ids, id => Assert.Equal("1", Load(w, id).Refs[GatewayPayments.ExpiryToldRef]));
        Assert.Null(third);
        Assert.Equal((2, 2), Told(w, l));
    }

    /// <summary>
    /// <para><b>قُلِبَ بِاسمِه</b> (تَوصيفُه <c>8e19697</c> — كانَ <c>Today_a_verify_whose_notice_throws_leaves_the_scheduled_message_telling_nobody</c>) — بابُ «تَحَقَّق»: يُلغي
    /// عِندَ المُهلَةِ ويُعيدُ ما أَلغاه، ثُمَّ يَرمي حِفظُ أَوَّلِ إشعارٍ في خُطوَةِ النُقطَةِ (سَطرُها بِحَرفِه: <c>NotifyPaymentExpiredAsync(slug, res.Expired, l, told: …)</c>) ⇒ لا عَلامَة،
    /// و<b>الرِسالَةُ المُجَدوَلَةُ حينَ تَحينُ تُخبِرُ المُشتَريَ وكُلَّ بائِعٍ مَرَّةً</b>.</para>
    /// </summary>
    [Fact]
    public async Task A_verify_whose_notice_throws_leaves_its_deals_for_the_scheduled_message_to_tell_once()
    {
        var (w, gateway, ids) = await GatewayExpiryRaceTests.ExpiredCart();
        var l = new L(new HttpContextAccessor());
        var notices = GatewayOwnerMoneyNoticeTests.Notices(w.Docs);
        w.Moyasar.Then(HttpStatusCode.OK, GatewayExpiryRaceTests.Invoice(ids, "initiated")).Then(HttpStatusCode.OK, GatewayExpiryRaceTests.Invoice(ids, "initiated"))
                 .Then(HttpStatusCode.OK, GatewayExpiryRaceTests.Invoice(ids, "initiated")).Then(HttpStatusCode.OK, GatewayExpiryRaceTests.Invoice(ids, "canceled"))
                 .Then(HttpStatusCode.OK, GatewayExpiryRaceTests.Invoice(ids, "canceled"));
        var verified = await gateway.VerifyAsync(Slug, new DealPayer(Buyer), ids[0].ToString("N"), l);
        var fired = FailFirstNotificationSave(w);

        var thrown = await Record.ExceptionAsync(() => notices.NotifyPaymentExpiredAsync(Slug, verified.Expired, l, told: id => gateway.MarkExpiryToldAsync(Slug, id)));
        var marked = ids.Count(id => Load(w, id).Refs.ContainsKey(GatewayPayments.ExpiryToldRef));
        await GatewayInvoiceExpiryHandler.Handle(new GatewayInvoiceExpiryDue(Slug, ids[0]), gateway, notices, l, CancellationToken.None);

        output.WriteLine($"التَحَقُّق: {verified.Code} أُلغِيَ {verified.Expired.Count} · الإشعارُ رَمى: {thrown?.GetType().Name} · مَوسومَة {marked} · بَعدَ الرِسالَة المُشتَري/البائِع {Told(w, l)}");
        Assert.True(fired());
        Assert.Equal(GatewayReturn.InvoiceExpired, verified.Code);
        Assert.Equal(2, verified.Expired.Count);
        Assert.IsType<InvalidOperationException>(thrown);
        Assert.Equal(0, marked);
        Assert.Equal((2, 2), Told(w, l));
        Assert.All(ids, id => Assert.Equal("1", Load(w, id).Refs[GatewayPayments.ExpiryToldRef]));
    }

    /// <summary><b>والسالِبُ لِلعَلامَة</b>: <see cref="GatewayPayments.MarkExpiryToldAsync"/> لا يَكتُبُ عَلى صَفقَةٍ قائِمَة، ولا عَلى مُلغاةٍ بِغَيرِ المُهلَة، ولا مَرَّتَين — شَرطُه
    /// <see cref="GatewayPayments.ExpiredUntold"/> عَلى المَخزونِ داخِلَ الكِتابَة؛ والمُوجِبُ: مُلغاةٌ بِالمُهلَةِ لَم تُوسَم تُوسَمُ بِسَطرِ مَرجِعٍ واحِد.</summary>
    [Fact]
    public async Task The_told_mark_is_written_once_and_only_on_a_deal_the_expiry_cancelled()
    {
        var (w, gateway, ids) = await GatewayExpiryRaceTests.ExpiredCart();
        await gateway.MarkExpiryToldAsync(Slug, ids[0]);
        Assert.False(Load(w, ids[0]).Refs.ContainsKey(GatewayPayments.ExpiryToldRef));   // قائِمَة

        Assert.Single((await w.Deals.CancelWholeInvoiceAsync(Slug, ids[1], new DealCanceller(Buyer, "مُشتَرٍ", false), "غَيَّرتُ رَأيي")).Cancelled, d => d.Id == ids[1]);
        await gateway.MarkExpiryToldAsync(Slug, ids[1]);
        Assert.False(Load(w, ids[1]).Refs.ContainsKey(GatewayPayments.ExpiryToldRef));   // أَلغاها المُشتَري

        var (w2, gateway2, ids2) = await GatewayExpiryRaceTests.ExpiredCart();
        w2.Moyasar.Then(HttpStatusCode.OK, GatewayExpiryRaceTests.Invoice(ids2, "initiated")).Then(HttpStatusCode.OK, GatewayExpiryRaceTests.Invoice(ids2, "canceled"));
        var l = new L(new HttpContextAccessor());
        Assert.Equal(2, (await w2.Deals.CancelWholeInvoiceAsync(Slug, ids2[0], GatewayPayments.ExpiryCanceller, l[GatewayPayments.ExpiryReasonKey])).Cancelled.Count);
        await gateway2.MarkExpiryToldAsync(Slug, ids2[0]);
        await gateway2.MarkExpiryToldAsync(Slug, ids2[0]);
        var lines = Load(w2, ids2[0]).Timeline.Count(e => e.Action == DealEvent.RefNote && e.Note == $"{DealRefLabels.AttachPrefix}{GatewayPayments.ExpiryToldRef}=1");
        output.WriteLine($"قائِمَة: لا · أَلغاها المُشتَري: لا · أَلغَتها المُهلَة: {Load(w2, ids2[0]).Refs.GetValueOrDefault(GatewayPayments.ExpiryToldRef)} بِسُطور {lines} · أُختُها بِلا عَلامَة: {!Load(w2, ids2[1]).Refs.ContainsKey(GatewayPayments.ExpiryToldRef)}");
        Assert.Equal("1", Load(w2, ids2[0]).Refs[GatewayPayments.ExpiryToldRef]);
        Assert.Equal(1, lines);
        Assert.False(Load(w2, ids2[1]).Refs.ContainsKey(GatewayPayments.ExpiryToldRef));
    }
}
