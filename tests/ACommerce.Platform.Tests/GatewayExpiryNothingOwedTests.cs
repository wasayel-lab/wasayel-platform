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
    internal const string OwnerEmail = "owner@store.example";

    internal static Deal Load(World w, Guid id) => w.Docs.Read<Deal>(id.ToString())!;

    /// <summary>دَفعُ الفاتورَةِ يُكتَبُ بِالكاتِبِ القائِمِ وبِبُرهانِ البَوّابَة — كَما يَكتُبُه إشعارُ ميسر أَو زِرُّ «تَحَقَّق» قَبلَ أَن تَحينَ المُهلَة.</summary>
    internal static async Task CaptureAsync(World w, Guid[] ids)
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
    internal static (DealNotices Notices, DealOwnerEmailTests.Capture Mail, Guid Owner) OwnerWithMail(World w)
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
    internal static async Task<(LogLevel Level, string Text)[]> OnWolverineAsync(
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

    internal static string[] GaveUp((LogLevel Level, string Text)[] logs)
        => logs.Where(x => x.Level == LogLevel.Error && x.Text.Contains("gave up", StringComparison.Ordinal)).Select(x => x.Text).ToArray();

    internal static Notification[] OwnerNotices(World w, Guid owner)
        => w.Docs.Stored.OfType<Notification>().Where(n => n.UserId == owner).ToArray();

    /// <summary>
    /// <para><b>قُلِبَ بِاسمِه</b> (تَوصيفُه <c>0c2cd09</c> — كانَ <c>Today_a_fetch_that_fails_at_expiry_on_an_invoice_nothing_is_owed_on_fails_the_message</c>: يَرمي
    /// <see cref="GatewayInvoiceExpiryFetchFailed"/> فَتَعمَلُ سِياسَةُ الفَشَلِ عَلى طَلَبٍ لا يُطالَبُ عَلَيه بِشَيء): فاتورَةٌ دُفِعَت (الصَفقَتانِ مَقبوضَتان) أَو أَلغاها المُشتَري قَبلَ
    /// المُهلَة، ثُمَّ يَفشَلُ جَلبُها ساعَةَ تَحين (‏503 أَو ‏401) ⇒ <b>يَفرُغُ <see cref="GatewayInvoiceExpiryHandler.Handle"/> بِلا خَطَأ</b> — لا إعادَةَ ولا رِسالَةَ ميتَةَ ولا إشعار —
    /// والرَمزُ ما زالَ رَمزَ جَلبٍ فاشِل (<see cref="GatewayInvoiceExpiry.FailsTheMessage"/> كَما هي)، والصَفقَتانِ كَما كانَتا. فَالفاصِلُ ما يُطالَبُ بِه، لا الرَمز.</para>
    /// </summary>
    [Theory]
    [InlineData("captured", "503", MoyasarFailures.Unreachable)]
    [InlineData("captured", "401", MoyasarFailures.Refused)]
    [InlineData("cancelled", "401", MoyasarFailures.Refused)]
    public async Task A_fetch_that_fails_at_expiry_on_an_invoice_nothing_is_owed_on_completes_the_message(string state, string kind, string code)
    {
        var (w, gateway, ids) = await GatewayExpiryRaceTests.ExpiredCart();
        await SetUpAsync(w, ids, state);
        var before = ids.Select(id => (Load(w, id).Status, DealsService.IsCaptured(Load(w, id)))).ToArray();
        var notes = w.Docs.Stored.OfType<Notification>().Count();
        GatewayExpiryFetchFailureTests.Script(w, kind);
        var calls = w.Moyasar.Requests.Count;
        var l = new L(new HttpContextAccessor());

        var thrown = await Record.ExceptionAsync(() =>
            GatewayInvoiceExpiryHandler.Handle(new GatewayInvoiceExpiryDue(Slug, ids[0]), gateway, GatewayOwnerMoneyNoticeTests.Notices(w.Docs), l, CancellationToken.None));

        var after = ids.Select(id => (Load(w, id).Status, DealsService.IsCaptured(Load(w, id)))).ToArray();
        output.WriteLine($"{state} ثُمَّ {kind}: رَمى {thrown?.GetType().Name ?? "لا شَيء"} · جَلب: {w.Moyasar.Requests.Count - calls} · " +
                         $"الحال: [{string.Join(", ", after.Select(a => $"{a.Status}/مَقبوضَة={a.Item2}"))}] · إشعاراتٌ جَديدَة: {w.Docs.Stored.OfType<Notification>().Count() - notes}");
        Assert.Null(thrown);
        Assert.True(GatewayInvoiceExpiry.FailsTheMessage(code));
        Assert.Equal(calls + 1, w.Moyasar.Requests.Count);
        Assert.Equal(before, after);
        Assert.Equal(notes, w.Docs.Stored.OfType<Notification>().Count());
        Assert.Empty(await gateway.OwedOnInvoiceAsync(Slug, ids[0]));
    }

    /// <summary><b>و«نِزاعٌ» لَم يُقبَض لا يُطالَبُ بِه عِندَ المُهلَة</b> (قَرارُ «أ» F3-R2·١ تَحتَ P1): الصَفقَتانِ في «نِزاع» ثُمَّ يَفشَلُ الجَلب (‏503) ⇒ يَفرُغُ المُعالِجُ بِلا خَطَأ —
    /// المُهلَةُ لا تُلغي «نِزاعاً» (<c>Active</c> وَحدَها)، وصاحِبُ المَتجَرِ يَحسِمُه بِبابِ الحَسمِ لا بِالإلغاءِ العامّ، فَلا يُقالُ لَه «أَلغِه». والسالِبُ المُقابِل: أُختٌ قائِمَةٌ
    /// لَم تُدفَع مَعَ «نِزاعٍ» ⇒ يَرمي.</summary>
    [Fact]
    public async Task A_disputed_unpaid_invoice_whose_fetch_fails_at_expiry_completes_and_one_active_unpaid_sibling_fails_it()
    {
        var (w, gateway, ids) = await GatewayExpiryRaceTests.ExpiredCart();
        var l = new L(new HttpContextAccessor());
        foreach (var id in ids) Load(w, id).Status = DealStatus.Disputed;
        GatewayExpiryFetchFailureTests.Script(w, "503");
        var bothDisputed = await Record.ExceptionAsync(() =>
            GatewayInvoiceExpiryHandler.Handle(new GatewayInvoiceExpiryDue(Slug, ids[0]), gateway, GatewayOwnerMoneyNoticeTests.Notices(w.Docs), l, CancellationToken.None));

        Load(w, ids[1]).Status = DealStatus.Active;
        GatewayExpiryFetchFailureTests.Script(w, "503");
        var oneActive = await Record.ExceptionAsync(() =>
            GatewayInvoiceExpiryHandler.Handle(new GatewayInvoiceExpiryDue(Slug, ids[0]), gateway, GatewayOwnerMoneyNoticeTests.Notices(w.Docs), l, CancellationToken.None));

        output.WriteLine($"نِزاعان: رَمى {bothDisputed?.GetType().Name ?? "لا شَيء"} · نِزاعٌ وقائِمَة: رَمى {oneActive?.GetType().Name ?? "لا شَيء"} " +
                         $"· يُطالَبُ بِه: [{string.Join(", ", (await gateway.OwedOnInvoiceAsync(Slug, ids[0])).Select(d => d.Id == ids[1] ? "الثانِيَة" : "الأولى"))}]");
        Assert.Null(bothDisputed);
        Assert.Equal(MoyasarFailures.Unreachable, Assert.IsType<GatewayInvoiceExpiryFetchFailed>(oneActive).Code);
        Assert.Equal(new[] { ids[1] }, (await gateway.OwedOnInvoiceAsync(Slug, ids[0])).Select(d => d.Id).ToArray());
    }

    /// <summary>
    /// <para><b>قُلِبَ بِاسمِه</b> (تَوصيفُه <c>0c2cd09</c> — كانَ <c>Today_a_paid_invoice_whose_fetch_fails_at_expiry_dies_after_four_attempts_and_tells_the_store_owner_to_cancel_it</c>:
    /// أَربَعُ مُحاوَلاتٍ وسَطرُ «the unpaid order stays» وإشعارٌ وبَريدٌ «أَلغِه» عَن طَلَبٍ مَدفوع): فاتورَةٌ دُفِعَت ثُمَّ تُجيبُ البَوّابَةُ ‏503 في كُلِّ جَلبٍ ساعَةَ تَحينُ المُهلَة ⇒
    /// <b>مُحاوَلَةٌ واحِدَة</b> تَفرُغُ بِلا خَطَأ، <b>ولا سَطرَ يَأسٍ ولا إشعارَ ولا بَريد</b>، والطَلَبُ مَدفوعٌ قائِم — بِـWolverine الحَقيقِيِّ في الذاكِرَةِ وسِياسَتِه.</para>
    /// </summary>
    [Fact]
    public async Task A_paid_invoice_whose_fetch_fails_at_expiry_completes_after_one_attempt_and_tells_nobody()
    {
        var (w, gateway, ids) = await GatewayExpiryRaceTests.ExpiredCart();
        await CaptureAsync(w, ids);
        var (notices, mail, owner) = OwnerWithMail(w);
        var at = new ConcurrentQueue<DateTimeOffset>();
        for (var i = 0; i < 8; i++)
            w.Moyasar.ThenDoing(() => at.Enqueue(DateTimeOffset.UtcNow), HttpStatusCode.ServiceUnavailable, "<html>upstream</html>");

        var logs = await OnWolverineAsync(w, gateway, notices, ids[0], () => at.Count, untilGaveUp: false, settle: TimeSpan.FromSeconds(8));

        var told = OwnerNotices(w, owner);
        var gaveUp = GaveUp(logs);
        output.WriteLine($"مُحاوَلات: {at.Count} · سَطرُ اليَأس: {gaveUp.Length} · إشعاراتُ المالِك: {told.Length} · بَريد: {mail.Notices.Count} · " +
                         $"الحال: [{string.Join(", ", ids.Select(id => $"{Load(w, id).Status}/مَقبوضَة={DealsService.IsCaptured(Load(w, id))}"))}]");
        Assert.Single(at);
        Assert.Empty(gaveUp);
        Assert.Empty(told);
        Assert.Empty(mail.Notices);
        Assert.All(ids, id => Assert.True(Load(w, id).Status == DealStatus.Active && DealsService.IsCaptured(Load(w, id))));
    }

    /// <summary>
    /// <para><b>قُلِبَ بِاسمِه</b> (تَوصيفُه <c>0c2cd09</c> — كانَ <c>Today_an_expiry_whose_notices_fail_at_every_attempt_after_the_cancel_tells_the_store_owner_it_could_not_cancel</c>):
    /// المُهلَةُ تُلغي الصَفقَتَين ثُمَّ يَرمي حِفظُ الإشعارِ في كُلِّ مُحاوَلَة (أَربَعاً) ⇒ يَأسٌ واحِد، و<b>سَطرُه يَقولُ إنّ لا شَيءَ يُطالَبُ بِه</b> بِلا «the unpaid order stays»،
    /// <b>ولا إشعارَ لِصاحِبِ المَتجَرِ ولا بَريد</b> «تَعَذَّرَ الإلغاء» عَن طَلَبٍ أُلغِي — والرِسالَةُ الميتَةُ يَقرَؤُها المُشَغِّلُ بِسَطرِها.</para>
    /// </summary>
    [Fact]
    public async Task An_expiry_whose_notices_fail_at_every_attempt_after_the_cancel_gives_up_without_telling_the_store_owner_to_cancel()
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
        output.WriteLine($"حِفظٌ رَمى: {failed()} · سَطرُ اليَأس: {gaveUp.Length} «{gaveUp.FirstOrDefault()}» · إشعاراتُ المالِك: {told.Length} · بَريد: {mail.Notices.Count} · " +
                         $"الحال: [{string.Join(", ", ids.Select(id => Load(w, id).Status))}]");
        Assert.Equal(GatewayInvoiceExpiry.RetryCooldowns.Length + 1, failed());
        var line = Assert.Single(gaveUp);
        Assert.Contains($"{Slug}/{ids[0]:N}", line, StringComparison.Ordinal);
        Assert.Contains("nothing on its invoice is still owed", line, StringComparison.Ordinal);
        Assert.DoesNotContain("the unpaid order stays", line, StringComparison.Ordinal);
        Assert.Empty(told);
        Assert.Empty(mail.Notices);
        Assert.All(ids, id => Assert.Equal(DealStatus.Cancelled, Load(w, id).Status));
    }
}

/// <summary>
/// <para><b>المُوجِبُ — ما يُطالَبُ بِه ما زالَ يُعادُ ثُمَّ يَموتُ ويُخبَرُ صاحِبُه</b> (‏2026-10-30، جَولَةُ الإصلاحِ الثانِيَةُ لِلمَسار F3): صَفّانِ بِـWolverine الحَقيقِيِّ في الذاكِرَة،
/// في صِنفٍ مُستَقِلٍّ فَيَجريانِ بِالتَوازي مَعَ صُفوفِ <see cref="GatewayExpiryNothingOwedTests"/>.</para>
/// </summary>
public class GatewayExpiryStillOwedTests(ITestOutputHelper output)
{
    private static Deal Load(World w, Guid id) => GatewayExpiryNothingOwedTests.Load(w, id);

    /// <summary>طَلَبٌ قائِمٌ لَم يُدفَع، ومِفتاحُ ميسر يُرفَضُ (‏401 — بُدِّلَ أَو أُزيلَ) في كُلِّ جَلب ⇒ <b>أَربَعُ مُحاوَلاتٍ ثُمَّ سَطرُ «the unpaid order stays»</b> وإشعارٌ واحِدٌ لِصاحِبِ
    /// المَتجَرِ بِرابِطِ صَفقَتِه وبَريدٌ واحِد — كَما قَبلَ الجَولَة. نَسَقُ <c>GatewayExpiryFetchFailureTests.Four_gateway_503s_at_expiry_…</c> بِالرَفضِ الدائِمِ والبَريد.</summary>
    [Fact]
    public async Task An_unpaid_invoice_whose_key_is_refused_at_every_attempt_gives_up_and_tells_the_store_owner_by_notice_and_email()
    {
        var (w, gateway, ids) = await GatewayExpiryRaceTests.ExpiredCart();
        var (notices, mail, owner) = GatewayExpiryNothingOwedTests.OwnerWithMail(w);
        var at = new ConcurrentQueue<DateTimeOffset>();
        for (var i = 0; i < 8; i++)
            w.Moyasar.ThenDoing(() => at.Enqueue(DateTimeOffset.UtcNow), HttpStatusCode.Unauthorized, "{\"type\":\"authentication_error\"}");

        var logs = await GatewayExpiryNothingOwedTests.OnWolverineAsync(w, gateway, notices, ids[0], () => at.Count, untilGaveUp: true, settle: TimeSpan.FromSeconds(2));

        var told = GatewayExpiryNothingOwedTests.OwnerNotices(w, owner);
        var gaveUp = GatewayExpiryNothingOwedTests.GaveUp(logs);
        output.WriteLine($"مُحاوَلات: {at.Count} · سَطرُ اليَأس: {gaveUp.Length} «{gaveUp.FirstOrDefault()}» · إشعاراتُ المالِك: {told.Length} «{told.FirstOrDefault()?.Title}» → " +
                         $"{told.FirstOrDefault()?.RelatedUrl} · بَريد: {mail.Notices.Count} · الحال: [{string.Join(", ", ids.Select(id => Load(w, id).Status))}]");
        Assert.Equal(GatewayInvoiceExpiry.RetryCooldowns.Length + 1, at.Count);
        var line = Assert.Single(gaveUp);
        Assert.Contains($"{nameof(GatewayInvoiceExpiryFetchFailed)} {MoyasarFailures.Refused}", line, StringComparison.Ordinal);
        Assert.Contains("the unpaid order stays until the store owner cancels it", line, StringComparison.Ordinal);
        var note = Assert.Single(told);
        Assert.Equal(LocaleCatalog.Text(LocaleCatalog.Arabic, "notifications.deal_expiry_failed.title"), note.Title);
        Assert.Equal(DealNotices.StudioDealUrl(Slug, ids[0]), note.RelatedUrl);
        Assert.Equal(GatewayExpiryNothingOwedTests.OwnerEmail, Assert.Single(mail.Notices).To);
        Assert.All(ids, id => Assert.Equal(DealStatus.Active, Load(w, id).Status));
    }

    /// <summary>طَلَبٌ لَم يُدفَع، والجَلبُ يَفشَلُ (‏503)، ثُمَّ يَكتُبُ إشعارُ الدَفعِ الدَفعَ قَبلَ الإعادَةِ الأولى ⇒ <b>مُحاوَلَتانِ</b> والثانِيَةُ تَفرُغُ بِلا خَطَأ — ما يُطالَبُ بِه يُسأَلُ
    /// بَعدَ كُلِّ جَلبٍ فاشِل — ولا سَطرَ يَأسٍ ولا إشعارَ ولا بَريد.</summary>
    [Fact]
    public async Task A_payment_written_between_two_failed_fetches_ends_the_message_at_the_second_attempt()
    {
        var (w, gateway, ids) = await GatewayExpiryRaceTests.ExpiredCart();
        var (notices, mail, owner) = GatewayExpiryNothingOwedTests.OwnerWithMail(w);
        var at = new ConcurrentQueue<DateTimeOffset>();
        w.Moyasar.ThenDoing(() => at.Enqueue(DateTimeOffset.UtcNow), HttpStatusCode.ServiceUnavailable, "<html>upstream</html>");
        w.Moyasar.ThenDoing(() => { GatewayExpiryNothingOwedTests.CaptureAsync(w, ids).GetAwaiter().GetResult(); at.Enqueue(DateTimeOffset.UtcNow); },
            HttpStatusCode.ServiceUnavailable, "<html>upstream</html>");
        for (var i = 0; i < 6; i++)
            w.Moyasar.ThenDoing(() => at.Enqueue(DateTimeOffset.UtcNow), HttpStatusCode.ServiceUnavailable, "<html>upstream</html>");

        var logs = await GatewayExpiryNothingOwedTests.OnWolverineAsync(w, gateway, notices, ids[0], () => at.Count, untilGaveUp: false, settle: TimeSpan.FromSeconds(9));

        var told = GatewayExpiryNothingOwedTests.OwnerNotices(w, owner);
        var gaveUp = GatewayExpiryNothingOwedTests.GaveUp(logs);
        output.WriteLine($"مُحاوَلات: {at.Count} · سَطرُ اليَأس: {gaveUp.Length} · إشعاراتُ المالِك: {told.Length} · بَريد: {mail.Notices.Count} · " +
                         $"الحال: [{string.Join(", ", ids.Select(id => $"{Load(w, id).Status}/مَقبوضَة={DealsService.IsCaptured(Load(w, id))}"))}]");
        Assert.Equal(2, at.Count);
        Assert.Empty(gaveUp);
        Assert.Empty(told);
        Assert.Empty(mail.Notices);
        Assert.All(ids, id => Assert.True(Load(w, id).Status == DealStatus.Active && DealsService.IsCaptured(Load(w, id))));
    }
}
