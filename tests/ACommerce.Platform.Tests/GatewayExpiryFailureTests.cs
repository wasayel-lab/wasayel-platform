using System.Net;
using ACommerce.Kit.Notifications;
using ACommerce.Platform.I18n;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using Microsoft.AspNetCore.Http;
using Xunit;
using Xunit.Abstractions;
using static ACommerce.Platform.Tests.TenantGatewayEndpointsTests;

namespace ACommerce.Platform.Tests;

// ═══ رِسالَةُ المُهلَةِ حينَ تَفشَلُ أَو تُعادُ (‏2026-10-30، تَدقيقُ الموجَة ١١ CO2 — المُرَحَّلُ [S3·١]) ═════════════════════════════════════════
//
// **المُرَحَّلُ بِقَرارِه** (P1): إعاداتٌ مَحدودَةٌ بِتَأَنٍّ، ثُمَّ الرِسالَةُ الميتَةُ مَعَ سَطرِ خَطَأٍ وإشعارٍ لِصاحِبِ المَتجَر؛ والمُعالِجُ لا يُكَرِّرُ إشعاراتِه ولا يُضَيِّعُها
// إن أُعيدَت الرِسالَةُ بَعدَ انقِطاعٍ بَينَ الإلغاءِ والإشعار. **والمَقيسُ اليَوم**: ‏ADR-046 §٤‑1 «مُعالِجٌ يَرمي لا يُعاد» (نِداءٌ واحِدٌ ثُمَّ صَفٌّ ميتٌ)، و§٤‑2
// «رَمزٌ في اللوغِ لا إعادَة»؛ والإشعاراتُ بِلا عَلامَة — رِسالَةٌ تُعادُ بَعدَ الإلغاءِ لا تَجِدُ ما «أُلغِيَ الآن» فَلا تُخبِرُ أَحَداً.

public class GatewayExpiryFailureTests(ITestOutputHelper output)
{
    private static Deal Load(World w, Guid id) => w.Docs.Read<Deal>(id.ToString())!;

    private static string ExpirySource()
        => WriteEndpointGuardTests.StripComments(File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, "libs", "templates",
            "ACommerce.Templates.Customer.Marketplace", "Services", "Deals", "GatewayInvoiceExpiry.cs")));

    /// <summary><b>تَوصيفُ «اليَوم»</b> (القاعِدَة ٣): تَركيبُ الرِسالَةِ ثَلاثَةُ أَسطُرٍ بِلا سِياسَةِ فَشَل — لا إعادَةَ ولا تَأَنٍّ ولا ما يَقَعُ عِندَ اليَأس.</summary>
    [Fact]
    public void Today_the_expiry_message_is_composed_without_a_failure_policy()
    {
        var code = ExpirySource();
        var configure = code[code.IndexOf("public static void Configure(WolverineOptions opts)", StringComparison.Ordinal)..];
        configure = configure[..(configure.IndexOf('}') + 1)];
        output.WriteLine(configure);
        Assert.Equal(3, configure.Split(';', StringSplitOptions.RemoveEmptyEntries).Length - 1);
        foreach (var policy in new[] { "OnAnyException", "OnException", "RetryWithCooldown", "ScheduleRetry", "MoveToErrorQueue", "Policies.Add" })
            Assert.DoesNotContain(policy, code, StringComparison.Ordinal);
    }

    /// <summary>
    /// <para><b>تَوصيفُ «اليَوم»</b> (القاعِدَة ٣): انقِطاعٌ بَعدَ أَن أَلغى المُعالِجُ الصَفقَتَينِ وقَبلَ أَن يُخبِر — ثُمَّ يُعيدُ الوَكيلُ الرِسالَة: الفاتورَةُ «canceled» والصَفقَتانِ
    /// مُلغاتان، فَلا شَيءَ «أُلغِيَ الآن» — <b>ولا إشعارَ لِأَحَد</b>: المُشتَري والبائِعُ لا يُخبَرانِ أَبَداً.</para>
    /// </summary>
    [Fact]
    public async Task Today_a_redelivery_after_a_crash_between_cancel_and_notify_tells_nobody()
    {
        var (w, gateway, ids) = await GatewayExpiryRaceTests.ExpiredCart();
        var l = new L(new HttpContextAccessor());
        var reason = l[GatewayPayments.ExpiryReasonKey];
        w.Moyasar.Then(HttpStatusCode.OK, GatewayExpiryRaceTests.Invoice(ids, "initiated")).Then(HttpStatusCode.OK, GatewayExpiryRaceTests.Invoice(ids, "canceled"));
        // ما كَتَبَه المُعالِجُ قَبلَ الانقِطاع: الطَلَبُ كُلُّه بِاسمِ المَنصَّةِ وبِسَبَبِ المُهلَة.
        Assert.Equal(2, (await w.Deals.CancelWholeInvoiceAsync(Slug, ids[0], GatewayPayments.ExpiryCanceller, reason)).Cancelled.Count);
        w.Moyasar.Then(HttpStatusCode.OK, GatewayExpiryRaceTests.Invoice(ids, "canceled"));

        await GatewayInvoiceExpiryHandler.Handle(new GatewayInvoiceExpiryDue(Slug, ids[0]), gateway, GatewayOwnerMoneyNoticeTests.Notices(w.Docs), l, CancellationToken.None);

        var notes = w.Docs.Stored.OfType<Notification>().ToArray();
        output.WriteLine($"بَعدَ الإعادَة: إشعارات={notes.Length} · {string.Join(" · ", ids.Select(id => Load(w, id).Status))}");
        Assert.All(ids, id => Assert.Equal(DealStatus.Cancelled, Load(w, id).Status));
        Assert.Empty(notes);
    }
}
