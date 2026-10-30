using System.Collections.Concurrent;
using System.Net;
using ACommerce.Kit.Notifications;
using ACommerce.Kit.Payments;
using ACommerce.Platform.I18n;
using ACommerce.Templates.Customer.Marketplace.Services;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using ACommerce.Templates.Customer.Marketplace.Services.Incubator;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Wolverine;
using Xunit;
using Xunit.Abstractions;
using static ACommerce.Platform.Tests.TenantGatewayEndpointsTests;

namespace ACommerce.Platform.Tests;

// ═══ رِسالَةُ المُهلَةِ عَلى فاتورَةٍ لا يُطالَبُ عَلَيها بِشَيء (‏2026-10-30، جَولَةُ الإصلاحِ الثانِيَةُ لِلمَسار F3 — مُراجَعَةُ `cc71027`، الحاجِبَة) ═══════════════════
//
// **الحالُ المَقيسَة**: رِسالَةُ المُهلَةِ تُجَدوَلُ مَرَّةً لِكُلِّ فاتورَةِ بِطاقَةٍ ساعَةَ تُفتَح (`OpenInvoiceAsync`) ولا يُلغي جَدوَلَتَها شَيء — فَتَحينُ أَيضاً لِطَلَبٍ دُفِعَ
// أَو أُلغِيَ قَبلَها بِساعات. و`ExpireAsync` يَجلِبُ الفاتورَةَ ولا يَسأَلُ أَبَقِيَ عَلَيها ما يُطالَبُ بِه. ومُنذُ `cc71027` كُلُّ جَلبٍ فَشِلَ (‏503 أَو انقِطاع، ‏429، ‏401، مِفتاحٌ
// لا يُفَكّ) يَرمي `GatewayInvoiceExpiryFetchFailed` ⇒ أَربَعُ مُحاوَلاتٍ ثُمَّ الرَسائِلُ الميتَة، و`GaveUpAsync` يُخبِرُ صاحِبَ المَتجَرِ — في الاستوديو وبِالبَريد (CO3) —
// «تَعَذَّرَ إلغاءُ طَلَبِ «…» آلِيّاً … افتَحِ الطَّلَبَ وأَلغِه» **عَن طَلَبٍ مَدفوع**، وسَطرُ الخَطَإِ يَقولُ «the unpaid order stays». وقَبلَ `cc71027` كانَت الحالُ نَفسُها تَفرُغُ صامِتَة.
// والمُحَرِّكُ عادِيّ: مالِكٌ يُبَدِّلُ مِفتاحَ ميسر أَو يُزيلُه، أَو انقِطاعٌ عِندَ ميسر أَطوَلُ مِن ‏21 ثانِيَة — فَكُلُّ طَلَبٍ مَدفوعٍ تَنتَهي نافِذَتُه في أَثنائِه يَقولُ لِصاحِبِه «أَلغِه».
// **وبابٌ ثانٍ إلى `GaveUpAsync` بِلا جَلبٍ فاشِل**: المُهلَةُ أَلغَت الطَلَبَ ثُمَّ رَمى حِفظُ الإشعاراتِ في كُلِّ مُحاوَلَة ⇒ يُقالُ لِصاحِبِه «تَعَذَّرَ الإلغاء» عَن طَلَبٍ أُلغِي.
// ومُجيبُ الحالِ المُوجَبَة — طَلَبٌ قائِمٌ لَم يُدفَع ⇒ تُعادُ ثُمَّ تَموتُ ويُخبَرُ صاحِبُه — صُفوفُ `GatewayExpiryFetchFailureTests` بِحَرفِها.

public class GatewayExpiryNothingOwedTests(ITestOutputHelper output)
{
    private const string OwnerEmail = "owner@store.example";

    private static Deal Load(World w, Guid id) => w.Docs.Read<Deal>(id.ToString())!;

    /// <summary>دَفعُ الفاتورَةِ يُكتَبُ بِالكاتِبِ القائِمِ وبِبُرهانِ البَوّابَة — كَما يَكتُبُه إشعارُ ميسر أَو زِرُّ «تَحَقَّق» قَبلَ أَن تَحينَ المُهلَة.</summary>
    private static async Task CaptureAsync(World w, Guid[] ids)
    {
        var proven = GatewayPaymentProof.Gate(new MoyasarInvoice(InvoiceId, "paid", 15000, "SAR", null, GatewayPaymentProof.MetadataFor(ids)),
            ids.Select(id => Load(w, id)).ToArray()).Payment!;
        await w.Deals.ConfirmGatewayPaymentAsync(Slug, proven);
    }

    /// <summary>المُشتَري يُلغي الطَلَبَ كُلَّه قَبلَ المُهلَة — بِالكاتِبِ القائِم، وفاتورَتُه تُلغى عِندَ البَوّابَةِ بِإلغاءِ آخِرِ صَفقَة (جَلبٌ ثُمَّ جَلبٌ ثُمَّ إلغاء).</summary>
    private static async Task CancelByBuyerAsync(World w, Guid[] ids)
    {
        w.Moyasar.Then(HttpStatusCode.OK, GatewayExpiryRaceTests.Invoice(ids, "initiated")).Then(HttpStatusCode.OK, GatewayExpiryRaceTests.Invoice(ids, "initiated"))
                 .Then(HttpStatusCode.OK, GatewayExpiryRaceTests.Invoice(ids, "canceled"));
        var order = await w.Deals.CancelWholeInvoiceAsync(Slug, ids[0], new DealCanceller(Buyer, "مُشتَرٍ", IsStoreAdmin: false), "عَدَلتُ");
        Assert.Equal(ids.Length, order.Cancelled.Count);
    }

    private static async Task SetUpAsync(World w, Guid[] ids, string state)
    {
        switch (state)
        {
            case "captured": await CaptureAsync(w, ids); break;
            case "cancelled": await CancelByBuyerAsync(w, ids); break;
            default: throw new ArgumentOutOfRangeException(nameof(state));
        }
    }

    /// <summary>صاحِبُ المَتجَرِ في الاستوديو بِبَريدٍ يَدخُلُ بِه — وإشعاراتُ الصَفقَةِ بِقَناةِ بَريدٍ تَلتَقِطُ ما يُرسَل (CO3).</summary>
    private static (DealNotices Notices, DealOwnerEmailTests.Capture Mail, Guid Owner) OwnerWithMail(World w)
    {
        var owner = w.Docs.Read<ACommerce.Kit.Tenants.Tenant>(Slug)!.OwnerUserId;
        w.Docs.Put(new StudioUser { Id = owner, Email = OwnerEmail, Phone = "+966511111111", FullName = "صاحِبُ المَتجَر" });
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSignalR();
        var hub = services.BuildServiceProvider().GetRequiredService<IHubContext<ACommerce.Kit.Realtime.Server.RealtimeHub>>();
        var mail = new DealOwnerEmailTests.Capture();
        return (new DealNotices(w.Docs.Store, hub, new WebPushService(new ConfigurationBuilder().Build(), NullLogger<WebPushService>.Instance), mail), mail, owner);
    }

    /// <summary>حِفظُ الإشعارِ يَرمي <paramref name="times"/> مَرّاتٍ ثُمَّ يَمضي — كَما يَرُدُّه Marten (يُرفَعُ مِن عالَمِ الذاكِرَةِ قَبلَ الرَمي): انقِطاعٌ في جَدوَلِ الإشعاراتِ أَطوَلُ مِن
    /// إعاداتِ الرِسالَة. نَسَقُ <see cref="GatewayExpiryNoticeRetryTests.FailFirstNotificationSave"/> بِعَدّاد. تُرجِعُ ما رَمى.</summary>
    private static Func<int> FailNotificationSaves(World w, int times)
    {
        var left = times;
        w.Docs.BeforeMember = name =>
        {
            if (left == 0 || name != "SaveChangesAsync" || w.Docs.Stored.LastOrDefault() is not Notification n) return;
            left--;
            w.Docs.Stored.RemoveAt(w.Docs.Stored.Count - 1);
            w.Docs.Docs.Remove(DocWorld.KeyOf(typeof(Notification), DocWorld.IdOf(n)));
            throw new InvalidOperationException("انقِطاعٌ في القاعِدَةِ عِندَ حِفظِ الإشعار");
        };
        return () => times - left;
    }

    /// <summary>لوغٌ يُلتَقَطُ نَصّاً لِكُلِّ فِئَة — لِيُقاسَ سَطرُ اليَأسِ بِحَرفِه.</summary>
    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public ConcurrentQueue<(LogLevel Level, string Text)> Lines { get; } = new();
        public ILogger CreateLogger(string categoryName) => new Capturing(this);
        public void Dispose() { }
        private sealed class Capturing(CapturingLoggerProvider owner) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
                => owner.Lines.Enqueue((logLevel, formatter(state, exception)));
        }
    }

    /// <summary>
    /// <para>رِسالَةُ المُهلَةِ بِـWolverine الحَقيقِيِّ في الذاكِرَةِ وتَركيبِ <see cref="GatewayInvoiceExpiry.Configure"/> نَفسِه — نَسَقُ
    /// <c>GatewayExpiryFetchFailureTests.Four_gateway_503s_at_expiry_…</c>. يَنتَظِرُ سَطرَ اليَأسِ (تِسعونَ ثانِيَةً عَلى الأَكثَر) ثُمَّ <paramref name="settle"/>؛ أَو، حينَ
    /// <paramref name="untilGaveUp"/> كاذِبَة، أَوَّلَ جَلبٍ ثُمَّ <paramref name="settle"/> — أَطوَلَ مِن الإعادَتَينِ الأولَيَينِ (‏1 ثُمَّ ‏5 ثَوانٍ)، فَإعادَةٌ لَو وَقَعَت تُرى.</para>
    /// </summary>
    private static async Task<(LogLevel Level, string Text)[]> OnWolverineAsync(
        World w, GatewayPayments gateway, DealNotices notices, Guid dealId, Func<int> attempts, bool untilGaveUp, TimeSpan settle)
    {
        var logs = new CapturingLoggerProvider();
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(logs);
        builder.Services.AddSingleton(gateway);
        builder.Services.AddSingleton(notices);
        builder.Services.AddSingleton(w.Deals);
        builder.Services.AddSingleton(new L(new HttpContextAccessor()));
        builder.UseWolverine(opts =>
        {
            opts.UseRuntimeCompilation();
            opts.Discovery.DisableConventionalDiscovery();
            GatewayInvoiceExpiry.Configure(opts);
        });
        using var host = builder.Build();
        await host.StartAsync();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            await host.Services.GetRequiredService<IMessageBus>().SendAsync(new GatewayInvoiceExpiryDue(Slug, dealId));
            while (watch.Elapsed < TimeSpan.FromSeconds(90))
            {
                if (untilGaveUp ? logs.Lines.Any(x => x.Level == LogLevel.Error && x.Text.Contains("gave up", StringComparison.Ordinal)) : attempts() > 0) break;
                await Task.Delay(200);
            }
            await Task.Delay(settle);
        }
        finally { await host.StopAsync(); }
        return logs.Lines.ToArray();
    }

    private static string[] GaveUp((LogLevel Level, string Text)[] logs)
        => logs.Where(x => x.Level == LogLevel.Error && x.Text.Contains("gave up", StringComparison.Ordinal)).Select(x => x.Text).ToArray();

    private Notification[] OwnerNotices(World w, Guid owner)
        => w.Docs.Stored.OfType<Notification>().Where(n => n.UserId == owner).ToArray();

    /// <summary>
    /// <para><b>تَوصيفُ اليَوم</b>: فاتورَةٌ دُفِعَت (الصَفقَتانِ مَقبوضَتان) أَو أَلغاها المُشتَري قَبلَ المُهلَة، ثُمَّ يَفشَلُ جَلبُها ساعَةَ تَحين (‏503 أَو ‏401) ⇒
    /// <see cref="GatewayInvoiceExpiryHandler.Handle"/> <b>يَرمي <see cref="GatewayInvoiceExpiryFetchFailed"/></b> بِرَمزِ العَميل — فَتَعمَلُ سِياسَةُ الفَشَلِ عَلى طَلَبٍ لا يُطالَبُ
    /// عَلَيه بِشَيء — والصَفقَتانِ كَما كانَتا.</para>
    /// </summary>
    [Theory]
    [InlineData("captured", "503", MoyasarFailures.Unreachable)]
    [InlineData("captured", "401", MoyasarFailures.Refused)]
    [InlineData("cancelled", "401", MoyasarFailures.Refused)]
    public async Task Today_a_fetch_that_fails_at_expiry_on_an_invoice_nothing_is_owed_on_fails_the_message(string state, string kind, string code)
    {
        var (w, gateway, ids) = await GatewayExpiryRaceTests.ExpiredCart();
        await SetUpAsync(w, ids, state);
        var before = ids.Select(id => (Load(w, id).Status, DealsService.IsCaptured(Load(w, id)))).ToArray();
        var notes = w.Docs.Stored.OfType<Notification>().Count();
        GatewayExpiryFetchFailureTests.Script(w, kind);
        var l = new L(new HttpContextAccessor());

        var thrown = await Record.ExceptionAsync(() =>
            GatewayInvoiceExpiryHandler.Handle(new GatewayInvoiceExpiryDue(Slug, ids[0]), gateway, GatewayOwnerMoneyNoticeTests.Notices(w.Docs), l, CancellationToken.None));

        var after = ids.Select(id => (Load(w, id).Status, DealsService.IsCaptured(Load(w, id)))).ToArray();
        output.WriteLine($"{state} ثُمَّ {kind}: رَمى {thrown?.GetType().Name ?? "لا شَيء"} «{(thrown as GatewayInvoiceExpiryFetchFailed)?.Code}» · " +
                         $"الحال: [{string.Join(", ", after.Select(a => $"{a.Status}/مَقبوضَة={a.Item2}"))}] · إشعاراتٌ جَديدَة: {w.Docs.Stored.OfType<Notification>().Count() - notes}");
        Assert.Equal(code, Assert.IsType<GatewayInvoiceExpiryFetchFailed>(thrown).Code);
        Assert.Equal(before, after);
        Assert.Equal(notes, w.Docs.Stored.OfType<Notification>().Count());
        Assert.Empty(GatewayInvoiceShare.OwedOnInvoice(Load(w, ids[0]), ids.Skip(1).Select(id => Load(w, id)).ToArray()));
    }

    /// <summary>
    /// <para><b>تَوصيفُ اليَوم — ما بَلَغَ صاحِبَ المَتجَر</b>: فاتورَةٌ دُفِعَت ثُمَّ تُجيبُ البَوّابَةُ ‏503 في كُلِّ جَلبٍ ساعَةَ تَحينُ المُهلَة ⇒ أَربَعُ مُحاوَلاتٍ ثُمَّ سَطرُ يَأسٍ يَقولُ
    /// «the unpaid order stays until the store owner cancels it»، و<b>إشعارٌ لِصاحِبِ المَتجَرِ وبَريدٌ</b> «طَلَبٌ انتَهَت مُهلَتُه لَم يُلغَ … أَلغِه» عَن طَلَبٍ مَدفوعٍ قائِم.</para>
    /// </summary>
    [Fact]
    public async Task Today_a_paid_invoice_whose_fetch_fails_at_expiry_dies_after_four_attempts_and_tells_the_store_owner_to_cancel_it()
    {
        var (w, gateway, ids) = await GatewayExpiryRaceTests.ExpiredCart();
        await CaptureAsync(w, ids);
        var (notices, mail, owner) = OwnerWithMail(w);
        var at = new ConcurrentQueue<DateTimeOffset>();
        for (var i = 0; i < 8; i++)
            w.Moyasar.ThenDoing(() => at.Enqueue(DateTimeOffset.UtcNow), HttpStatusCode.ServiceUnavailable, "<html>upstream</html>");

        var logs = await OnWolverineAsync(w, gateway, notices, ids[0], () => at.Count, untilGaveUp: true, settle: TimeSpan.FromSeconds(2));

        var told = OwnerNotices(w, owner);
        var gaveUp = GaveUp(logs);
        output.WriteLine($"مُحاوَلات: {at.Count} · سَطرُ اليَأس: {gaveUp.Length} «{gaveUp.FirstOrDefault()}» · إشعاراتُ المالِك: {told.Length} «{told.FirstOrDefault()?.Title}» · " +
                         $"بَريد: {mail.Notices.Count} «{mail.Notices.FirstOrDefault().Subject}» · الحال: [{string.Join(", ", ids.Select(id => $"{Load(w, id).Status}/مَقبوضَة={DealsService.IsCaptured(Load(w, id))}"))}]");
        Assert.Equal(GatewayInvoiceExpiry.RetryCooldowns.Length + 1, at.Count);
        Assert.Contains("the unpaid order stays until the store owner cancels it", Assert.Single(gaveUp), StringComparison.Ordinal);
        Assert.Equal(LocaleCatalog.Text(LocaleCatalog.Arabic, "notifications.deal_expiry_failed.title"), Assert.Single(told).Title);
        Assert.Equal(OwnerEmail, Assert.Single(mail.Notices).To);
        Assert.All(ids, id => Assert.True(Load(w, id).Status == DealStatus.Active && DealsService.IsCaptured(Load(w, id))));
    }

    /// <summary>
    /// <para><b>تَوصيفُ اليَوم — البابُ الثاني</b>: الفاتورَةُ مَفتوحَةٌ جاوَزَت تاريخَها، فَتُلغي المُهلَةُ الصَفقَتَين، ثُمَّ يَرمي حِفظُ الإشعارِ في كُلِّ مُحاوَلَة (أَربَعاً) ⇒ يَأسٌ
    /// و<b>إشعارٌ لِصاحِبِ المَتجَرِ وبَريدٌ «تَعَذَّرَ إلغاءُ طَلَبِ … آلِيّاً … أَلغِه»</b> والطَلَبُ مُلغًى.</para>
    /// </summary>
    [Fact]
    public async Task Today_an_expiry_whose_notices_fail_at_every_attempt_after_the_cancel_tells_the_store_owner_it_could_not_cancel()
    {
        var (w, gateway, ids) = await GatewayExpiryRaceTests.ExpiredCart();
        var (notices, mail, owner) = OwnerWithMail(w);
        w.Moyasar.Then(HttpStatusCode.OK, GatewayExpiryRaceTests.Invoice(ids, "initiated")).Then(HttpStatusCode.OK, GatewayExpiryRaceTests.Invoice(ids, "initiated"))
                 .Then(HttpStatusCode.OK, GatewayExpiryRaceTests.Invoice(ids, "initiated")).Then(HttpStatusCode.OK, GatewayExpiryRaceTests.Invoice(ids, "canceled"));
        for (var i = 0; i < 6; i++) w.Moyasar.Then(HttpStatusCode.OK, GatewayExpiryRaceTests.Invoice(ids, "canceled"));
        var failed = FailNotificationSaves(w, GatewayInvoiceExpiry.RetryCooldowns.Length + 1);

        var logs = await OnWolverineAsync(w, gateway, notices, ids[0], failed, untilGaveUp: true, settle: TimeSpan.FromSeconds(2));

        var told = OwnerNotices(w, owner);
        var gaveUp = GaveUp(logs);
        output.WriteLine($"حِفظٌ رَمى: {failed()} · سَطرُ اليَأس: {gaveUp.Length} «{gaveUp.FirstOrDefault()}» · إشعاراتُ المالِك: {told.Length} «{told.FirstOrDefault()?.Title}» · " +
                         $"بَريد: {mail.Notices.Count} · الحال: [{string.Join(", ", ids.Select(id => Load(w, id).Status))}]");
        Assert.Equal(GatewayInvoiceExpiry.RetryCooldowns.Length + 1, failed());
        Assert.Contains("the unpaid order stays until the store owner cancels it", Assert.Single(gaveUp), StringComparison.Ordinal);
        Assert.Equal(LocaleCatalog.Text(LocaleCatalog.Arabic, "notifications.deal_expiry_failed.title"), Assert.Single(told).Title);
        Assert.Single(mail.Notices);
        Assert.All(ids, id => Assert.Equal(DealStatus.Cancelled, Load(w, id).Status));
    }
}
