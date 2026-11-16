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

    /// <summary><b>قُلِبَ بِاسمِه</b> (‏2026-10-30، CO2؛ تَوصيفُه <c>1e65236</c> — كانَ <c>Today_the_expiry_message_is_composed_without_a_failure_policy</c>: ثَلاثَةُ أَسطُرٍ بِلا سِياسَة):
    /// التَركيبُ أَربَعَةُ أَسطُر، رابِعُها سِياسَةُ فَشَلٍ عَلى سِلسِلَةِ الرِسالَةِ وَحدَها — ثَلاثُ إعاداتٍ بِتَأَنٍّ (ثانِيَة، خَمس، خَمسَ عَشرَة) ثُمَّ الرَسائِلُ الميتَةُ ومَعَها
    /// ما يَقَعُ عِندَ اليَأس (سَطرُ خَطَأٍ وإشعارُ صاحِبِ المَتجَرِ بِالأُنبوبِ القائِم). والبُرهانُ الحَيُّ في <c>LiveScheduledExpiryProofTests</c>.</summary>
    [Fact]
    public void The_expiry_message_is_composed_with_bounded_retries_then_the_dead_letters_and_an_owner_notice()
    {
        var code = ExpirySource();
        var configure = code[code.IndexOf("public static void Configure(WolverineOptions opts)", StringComparison.Ordinal)..];
        configure = configure[..(configure.IndexOf('}') + 1)];
        output.WriteLine(configure);
        Assert.Equal(4, configure.Split(';', StringSplitOptions.RemoveEmptyEntries).Length - 1);
        Assert.Contains("opts.Policies.Add(new GatewayInvoiceExpiryFailures());", configure, StringComparison.Ordinal);
        Assert.Equal(new[] { 1d, 5d, 15d }, GatewayInvoiceExpiry.RetryCooldowns.Select(t => t.TotalSeconds).ToArray());
        var policy = code[code.IndexOf("public sealed class GatewayInvoiceExpiryFailures", StringComparison.Ordinal)..];
        var chain = new[] { "c.MessageType == typeof(GatewayInvoiceExpiryDue)", ".OnAnyException()", ".RetryWithCooldown(GatewayInvoiceExpiry.RetryCooldowns)",
                            ".Then.MoveToErrorQueue()", ".And(GatewayInvoiceExpiry.GaveUpAsync," }.Select(p => policy.IndexOf(p, StringComparison.Ordinal)).ToArray();
        Assert.True(chain.All(i => i > 0) && chain.Zip(chain.Skip(1)).All(p => p.First < p.Second), string.Join(" ← ", chain));
        var gaveUp = code[code.IndexOf("public static async ValueTask GaveUpAsync", StringComparison.Ordinal)..code.IndexOf("public sealed class GatewayInvoiceExpiryFailures", StringComparison.Ordinal)];
        Assert.Contains("log.LogError(error,", gaveUp, StringComparison.Ordinal);
        Assert.Contains("NotifyStoreOwnerAsync(due.TenantSlug, deal, DealNotices.OwnerAction.ExpiryFailed,", gaveUp, StringComparison.Ordinal);
    }

    /// <summary>
    /// <para><b>قُلِبَ بِاسمِه</b> (‏2026-10-30، CO2؛ تَوصيفُه <c>1e65236</c> — كانَ <c>Today_a_redelivery_after_a_crash_between_cancel_and_notify_tells_nobody</c>): انقِطاعٌ بَعدَ أَن
    /// أَلغى المُعالِجُ الصَفقَتَينِ وقَبلَ أَن يُخبِر، ثُمَّ يُعيدُ الوَكيلُ الرِسالَة ⇒ <b>يُخبَرُ المُشتَري وكُلُّ بائِعٍ مَرَّةً</b> (المُطالَبَةُ <c>gateway_expiry_told</c> تُكتَبُ قَبلَ
    /// الإشعار)، وإعادَةٌ ثالِثَةٌ لا تُخبِرُ أَحَداً.</para>
    /// <para>(‏2026-10-30، جَولَةُ الإصلاحِ الأولى لِلمَسار F3 — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه): «تُكتَبُ قَبلَ الإشعار» صارَ «بَعدَه» — والصَفُّ بِحَرفِه أَخضَر؛ وانقِطاعٌ
    /// <b>بَعدَ</b> أَوَّلِ إشعارٍ يَقيسُه <c>GatewayExpiryNoticeRetryTests</c>.</para>
    /// </summary>
    [Fact]
    public async Task A_redelivery_after_a_crash_between_cancel_and_notify_tells_the_buyer_and_every_vendor_once()
    {
        var (w, gateway, ids) = await GatewayExpiryRaceTests.ExpiredCart();
        var l = new L(new HttpContextAccessor());
        var reason = l[GatewayPayments.ExpiryReasonKey];
        w.Moyasar.Then(HttpStatusCode.OK, GatewayExpiryRaceTests.Invoice(ids, "initiated")).Then(HttpStatusCode.OK, GatewayExpiryRaceTests.Invoice(ids, "canceled"));
        // ما كَتَبَه المُعالِجُ قَبلَ الانقِطاع: الطَلَبُ كُلُّه بِاسمِ المَنصَّةِ وبِسَبَبِ المُهلَة.
        Assert.Equal(2, (await w.Deals.CancelWholeInvoiceAsync(Slug, ids[0], GatewayPayments.ExpiryCanceller, reason)).Cancelled.Count);
        w.Moyasar.Then(HttpStatusCode.OK, GatewayExpiryRaceTests.Invoice(ids, "canceled")).Then(HttpStatusCode.OK, GatewayExpiryRaceTests.Invoice(ids, "canceled"));
        var notices = GatewayOwnerMoneyNoticeTests.Notices(w.Docs);

        await GatewayInvoiceExpiryHandler.Handle(new GatewayInvoiceExpiryDue(Slug, ids[0]), gateway, notices, l, CancellationToken.None);
        var first = w.Docs.Stored.OfType<Notification>().ToArray();
        await GatewayInvoiceExpiryHandler.Handle(new GatewayInvoiceExpiryDue(Slug, ids[0]), gateway, notices, l, CancellationToken.None);
        var again = w.Docs.Stored.OfType<Notification>().ToArray();

        output.WriteLine($"بَعدَ الإعادَة: إشعارات={first.Length} (المُشتَري {first.Count(n => n.UserId == Buyer)}، البائِع {first.Count(n => n.UserId == Seller)}) · بَعدَ الثالِثَة: {again.Length}");
        Assert.All(ids, id => Assert.Equal("1", Load(w, id).Refs[GatewayPayments.ExpiryToldRef]));
        Assert.Equal(2, first.Count(n => n.UserId == Buyer && n.Title == l["notifications.deal_payment_expired.title"]));
        Assert.Equal(2, first.Count(n => n.UserId == Seller && n.Title == l["notifications.deal_cancelled_for_vendor.title"]));
        Assert.Equal(first.Length, again.Length);
    }

    /// <summary><b>والطَريقانِ لا يُكَرِّرانِ بَعضَهُما</b>: التَحَقُّقُ يُلغي عِندَ المُهلَةِ ويُطالِبُ بِإشعارِه فَيُخبِرُ مُناديه، ثُمَّ تَحينُ الرِسالَةُ المُجَدوَلَة — لا إشعارَ ثانٍ.
    /// <b>والسالِب</b>: إلغاءٌ بِغَيرِ المُهلَة (صاحِبُ المَتجَر) لا تُطالِبُ بِه الرِسالَة.
    /// (‏2026-10-30، جَولَةُ الإصلاحِ الأولى لِلمَسار F3؛ تَوصيفُه <c>8e19697</c> — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه): «يُطالِبُ بِإشعارِه» صارَ «يُخبِرُ مُناديه ثُمَّ يَسِمُ» — فَبَينَ التَحَقُّقِ
    /// والرِسالَةِ خُطوَةُ النُقطَةِ بِسَطرِها (<c>NotifyPaymentExpiredAsync(…, told: …)</c>)، وبِدونِها تُخبِرُ الرِسالَةُ بِما لَم يُخبَر بِه. وصَفُّ الإشعارِ الَّذي يَرمي في
    /// <c>GatewayExpiryNoticeRetryTests</c>.</summary>
    [Fact]
    public async Task A_verify_that_cancelled_at_expiry_leaves_nothing_for_the_scheduled_message_and_other_cancels_are_never_claimed()
    {
        var (w, gateway, ids) = await GatewayExpiryRaceTests.ExpiredCart();
        var l = new L(new HttpContextAccessor());
        w.Moyasar.Then(HttpStatusCode.OK, GatewayExpiryRaceTests.Invoice(ids, "initiated"))
                 .Then(HttpStatusCode.OK, GatewayExpiryRaceTests.Invoice(ids, "initiated")).Then(HttpStatusCode.OK, GatewayExpiryRaceTests.Invoice(ids, "initiated"))
                 .Then(HttpStatusCode.OK, GatewayExpiryRaceTests.Invoice(ids, "canceled"));
        var verified = await gateway.VerifyAsync(Slug, new DealPayer(Buyer), ids[0].ToString("N"), l);
        Assert.Equal(ids.OrderBy(i => i), verified.Expired.OrderBy(i => i));
        Assert.Equal(2, await GatewayOwnerMoneyNoticeTests.Notices(w.Docs).NotifyPaymentExpiredAsync(Slug, verified.Expired, l, told: id => gateway.MarkExpiryToldAsync(Slug, id)));
        w.Moyasar.Then(HttpStatusCode.OK, GatewayExpiryRaceTests.Invoice(ids, "canceled"));
        var scheduled = await gateway.ExpireAsync(Slug, ids[0], l);
        Assert.Empty(scheduled.Expired);

        var other = Load(w, ids[0]);
        other.Refs.Remove(GatewayPayments.ExpiryToldRef);
        other.CancelReason = "إلغاء إداريّ";
        Assert.False(GatewayPayments.CancelledByExpiry(other));
        Assert.False(GatewayPayments.ExpiredUntold(other));
        other.CancelReason = LocaleCatalog.Text(LocaleCatalog.Arabic, GatewayPayments.ExpiryReasonKey);
        Assert.True(GatewayPayments.ExpiredUntold(other));
        other.Timeline.Add(new(other.Stage, other.Stage, "cancelled", Guid.NewGuid(), "صاحِبُ المَتجَر", null, DateTime.UtcNow));
        Assert.False(GatewayPayments.CancelledByExpiry(other));
    }
}
