using System.Net;
using ACommerce.Kit.Notifications;
using ACommerce.Kit.Payments;
using ACommerce.Platform.I18n;
using ACommerce.Platform.Providers;
using ACommerce.Templates.Customer.Marketplace.Services;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using Microsoft.AspNetCore.Http;
using Xunit;
using Xunit.Abstractions;
using static ACommerce.Platform.Tests.TenantGatewayEndpointsTests;

namespace ACommerce.Platform.Tests;

// ═══ جَلبُ الفاتورَةِ يَفشَلُ ساعَةَ تَحينُ المُهلَة (‏2026-10-30، جَولَةُ الإصلاحِ الأولى لِلمَسار F3 — مُراجَعَةُ CO2 / المُرَحَّلُ [S3·١]) ═══════════════════
//
// **الحالُ المَقيسَة**: سِياسَةُ فَشَلِ الرِسالَةِ (`GatewayInvoiceExpiryFailures`، `f97e6c7`) تَعمَلُ عَلى ما **يَرميه** المُعالِج — و`GatewayPayments.ExpireAsync`
// لا يَرمي حينَ يَفشَلُ الجَلب: عَميلُ ميسر يُحَوِّلُ ‏5xx والانقِطاعَ والمُهلَةَ إلى `gateway_unreachable`، و‏429 إلى `gateway_rate_limited`، والجَوابَ الَّذي لا يُقرَأُ إلى
// `gateway_unreadable`، ومِفتاحاً لا يُفَكُّ إلى `gateway_not_configured`، و‏401 إلى `gateway_refused` — رَمزاً يُرجِعُه. فَيَفرُغُ المُعالِجُ بِلا خَطَأ: لا إعادَةَ ولا
// رِسالَةَ ميتَةً ولا سَطرَ خَطَأٍ يُسَمّي المَتجَرَ والصَفقَةَ ولا إشعارَ لِصاحِبِ المَتجَر، والطَلَبُ غَيرُ المَدفوعِ قائِمٌ بِلا نِهايَة — الحالُ الَّتي كَتَبَت د5 (٤)
// («ألّا يبقى طلب غير مدفوع معلّقًا يحجز البائعين بلا نهاية») وسَمّاها [S3·١] بِنَصِّها («ورفضُ ميسر أو انقطاعُها لا يرمي … فلا يُعادُ أيضًا»).
// والبُرهانُ الحَيُّ في `f97e6c7` قاسَ عَطَباً لا تُنتِجُه الأَعطالُ الحَقيقِيَّة: `InvalidOperationException` مِن مُعالِجِ HTTP، يَعبُرُ العَميلَ لِأَنّه لا يَلتَقِطُه.

public class GatewayExpiryFetchFailureTests(ITestOutputHelper output)
{
    private static Deal Load(World w, Guid id) => w.Docs.Read<Deal>(id.ToString())!;

    /// <summary>رَدُّ البَوّابَةِ عِندَ جَلبِ المُهلَة بِحَسَبِ العَطَب — كُلٌّ بِشَكلِه مِن الوَثيقَة أَو مِن الشَبَكَة، لا اِستِثناءٌ مَصنوعٌ يَعبُرُ العَميل.</summary>
    internal static void Script(World w, string kind)
    {
        switch (kind)
        {
            case "503":        w.Moyasar.Then(HttpStatusCode.ServiceUnavailable, "<html>upstream</html>"); break;
            case "drop":       w.Moyasar.ThenThrow(); break;
            case "429":        w.Moyasar.Then((HttpStatusCode)MoyasarFailures.RateLimitedStatus, "{\"type\":\"rate_limit_error\"}"); break;
            case "unreadable": w.Moyasar.Then(HttpStatusCode.OK, "not json"); break;
            case "401":        w.Moyasar.Then(HttpStatusCode.Unauthorized, "{\"type\":\"authentication_error\"}"); break;
            case "no_key":
                Assert.True(w.Docs.Docs.Remove(DocWorld.KeyOf(typeof(TenantProviderBinding), ProviderCapabilities.Payments)));
                break;
            default: throw new ArgumentOutOfRangeException(nameof(kind));
        }
    }

    /// <summary>
    /// <para><b>«اليَوم»</b>: البَوّابَةُ لا تُجيبُ عِندَ المُهلَة (‏503، انقِطاع، ‏429، جَوابٌ لا يُقرَأ، مِفتاحٌ لا يُفَكّ، ‏401) ⇒ <see cref="GatewayPayments.ExpireAsync"/> يُرجِعُ
    /// رَمزَ العَميل، و<see cref="GatewayInvoiceExpiryHandler.Handle"/> يَفرُغُ <b>بِلا خَطَأ</b> — فَلا تَعمَلُ سِياسَةُ الفَشَل؛ والصَفقَتانِ قائِمَتانِ ولا إشعارَ لِأَحَد.</para>
    /// </summary>
    [Theory]
    [InlineData("503", MoyasarFailures.Unreachable)]
    [InlineData("drop", MoyasarFailures.Unreachable)]
    [InlineData("429", MoyasarFailures.RateLimited)]
    [InlineData("unreadable", MoyasarFailures.Unreadable)]
    [InlineData("no_key", MoyasarFailures.NotConfigured)]
    [InlineData("401", MoyasarFailures.Refused)]
    public async Task Today_a_fetch_that_fails_at_expiry_completes_the_message_and_leaves_the_order_active(string kind, string code)
    {
        var (w, gateway, ids) = await GatewayExpiryRaceTests.ExpiredCart();
        var l = new L(new HttpContextAccessor());
        Script(w, kind);
        var asked = await gateway.ExpireAsync(Slug, ids[0], l);
        if (kind != "no_key") Script(w, kind);   // المِفتاحُ أُزيلَ مَرَّةً ولا نِداءَ بِلا مِفتاح

        var thrown = await Record.ExceptionAsync(() =>
            GatewayInvoiceExpiryHandler.Handle(new GatewayInvoiceExpiryDue(Slug, ids[0]), gateway, GatewayOwnerMoneyNoticeTests.Notices(w.Docs), l, CancellationToken.None));

        output.WriteLine($"{kind}: الرَمز {asked.Code} · المُعالِجُ رَمى: {thrown?.GetType().Name ?? "لا شَيء"} · الحال: [{string.Join(", ", ids.Select(id => Load(w, id).Status))}] · " +
                         $"إشعارات: {w.Docs.Stored.OfType<Notification>().Count()}");
        Assert.Equal(code, asked.Code);
        Assert.Null(thrown);
        Assert.All(ids, id => Assert.Equal(DealStatus.Active, Load(w, id).Status));
        Assert.Empty(w.Docs.Stored.OfType<Notification>());
    }
}
