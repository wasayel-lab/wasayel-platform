using System.Net;
using ACommerce.Kit.Notifications;
using ACommerce.Kit.Payments;
using ACommerce.Platform.I18n;
using ACommerce.Platform.Providers;
using ACommerce.Templates.Customer.Marketplace.Services;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Wolverine;
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
    /// <para><b>قُلِبَ بِاسمِه</b> (تَوصيفُه <c>4bc251c</c> — كانَ <c>Today_a_fetch_that_fails_at_expiry_completes_the_message_and_leaves_the_order_active</c>: المُعالِجُ يَفرُغُ
    /// بِلا خَطَأ): البَوّابَةُ لا تُجيبُ عِندَ المُهلَة (‏503، انقِطاع، ‏429، جَوابٌ لا يُقرَأ، مِفتاحٌ لا يُفَكّ، ‏401) ⇒ <see cref="GatewayPayments.ExpireAsync"/> يُرجِعُ رَمزَ العَميلِ
    /// كَما كان، و<see cref="GatewayInvoiceExpiryHandler.Handle"/> <b>يَرمي <see cref="GatewayInvoiceExpiryFetchFailed"/> بِالرَمزِ نَفسِه</b> فَتَعمَلُ سِياسَةُ الفَشَل؛ والصَفقَتانِ
    /// قائِمَتانِ ولا إشعارَ لِأَحَد — لا يُلغى ما لا يُعرَفُ أَدُفِعَ أَم لا.</para>
    /// </summary>
    [Theory]
    [InlineData("503", MoyasarFailures.Unreachable)]
    [InlineData("drop", MoyasarFailures.Unreachable)]
    [InlineData("429", MoyasarFailures.RateLimited)]
    [InlineData("unreadable", MoyasarFailures.Unreadable)]
    [InlineData("no_key", MoyasarFailures.NotConfigured)]
    [InlineData("401", MoyasarFailures.Refused)]
    public async Task A_fetch_that_fails_at_expiry_fails_the_message_and_leaves_the_order_active(string kind, string code)
    {
        var (w, gateway, ids) = await GatewayExpiryRaceTests.ExpiredCart();
        var l = new L(new HttpContextAccessor());
        Script(w, kind);
        var asked = await gateway.ExpireAsync(Slug, ids[0], l);
        if (kind != "no_key") Script(w, kind);   // المِفتاحُ أُزيلَ مَرَّةً ولا نِداءَ بِلا مِفتاح

        var thrown = await Record.ExceptionAsync(() =>
            GatewayInvoiceExpiryHandler.Handle(new GatewayInvoiceExpiryDue(Slug, ids[0]), gateway, GatewayOwnerMoneyNoticeTests.Notices(w.Docs), l, CancellationToken.None));

        output.WriteLine($"{kind}: الرَمز {asked.Code} · المُعالِجُ رَمى: {thrown?.GetType().Name ?? "لا شَيء"} «{thrown?.Message}» · الحال: [{string.Join(", ", ids.Select(id => Load(w, id).Status))}] · " +
                         $"إشعارات: {w.Docs.Stored.OfType<Notification>().Count()}");
        Assert.Equal(code, asked.Code);
        var failed = Assert.IsType<GatewayInvoiceExpiryFetchFailed>(thrown);
        Assert.Equal(code, failed.Code);
        Assert.Contains(ids[0].ToString("N"), failed.Message, StringComparison.Ordinal);
        Assert.Contains(Slug, failed.Message, StringComparison.Ordinal);
        Assert.All(ids, id => Assert.Equal(DealStatus.Active, Load(w, id).Status));
        Assert.Empty(w.Docs.Stored.OfType<Notification>());
    }

    /// <summary><b>السالِب</b>: كُلُّ مُهلَةٍ أَجابَت فيها الفاتورَةُ نَفسُها أَو لا فاتورَةَ أَصلاً — «on_hold» (لا تُلغى عَمداً)، مُغلَقَة، مَدفوعَة، انتَهَت فَأُلغِيَت، صَفقَةٌ غائِبَة،
    /// صَفقَةٌ بِلا فاتورَة — <b>يَفرُغُ المُعالِجُ بِلا خَطَأ</b> كَما كان: لا إعادَةَ لِما لا تُصلِحُه الإعادَة.</summary>
    [Theory]
    [InlineData("on_hold", GatewayPaymentProof.InvoiceUnpaid)]
    [InlineData("canceled", GatewayPaymentProof.InvoiceClosed)]
    [InlineData("paid", null)]
    [InlineData("expired", GatewayReturn.InvoiceExpired)]
    [InlineData("deal_not_found", DealPayAuthorization.DealNotFound)]
    [InlineData("invoice_missing", DealPayAuthorization.InvoiceMissing)]
    public async Task Every_expiry_the_invoice_itself_answers_completes_the_message(string kind, string? code)
    {
        var (w, gateway, ids) = await GatewayExpiryRaceTests.ExpiredCart();
        var l = new L(new HttpContextAccessor());
        var target = ids[0];
        switch (kind)
        {
            case "on_hold":  w.Moyasar.Then(HttpStatusCode.OK, GatewayExpiryRaceTests.Invoice(ids, "on_hold")); break;
            case "canceled": w.Moyasar.Then(HttpStatusCode.OK, GatewayExpiryRaceTests.Invoice(ids, "canceled")); break;
            case "paid":     w.Moyasar.Then(HttpStatusCode.OK, GatewayExpiryRaceTests.Invoice(ids, "paid")); break;
            case "expired":
                w.Moyasar.Then(HttpStatusCode.OK, GatewayExpiryRaceTests.Invoice(ids, "initiated")).Then(HttpStatusCode.OK, GatewayExpiryRaceTests.Invoice(ids, "initiated"))
                         .Then(HttpStatusCode.OK, GatewayExpiryRaceTests.Invoice(ids, "initiated")).Then(HttpStatusCode.OK, GatewayExpiryRaceTests.Invoice(ids, "canceled"));
                break;
            case "deal_not_found": target = Guid.NewGuid(); break;
            case "invoice_missing": target = (await w.Deals.StartAsync(Slug, "marketplace", Buyer, "مُشتَرٍ", null, "سِلعَة", 10m, new())).Id; break;
            default: throw new ArgumentOutOfRangeException(nameof(kind));
        }
        var calls = w.Moyasar.Requests.Count;

        var thrown = await Record.ExceptionAsync(() =>
            GatewayInvoiceExpiryHandler.Handle(new GatewayInvoiceExpiryDue(Slug, target), gateway, GatewayOwnerMoneyNoticeTests.Notices(w.Docs), l, CancellationToken.None));

        output.WriteLine($"{kind}: رَمى {thrown?.GetType().Name ?? "لا شَيء"} · نِداءات {w.Moyasar.Requests.Count - calls} · الحال: [{string.Join(", ", ids.Select(id => Load(w, id).Status))}]");
        Assert.Null(thrown);
        Assert.False(GatewayInvoiceExpiry.FailsTheMessage(code));
    }

    /// <summary><b>المَعجَمُ المُغلَقُ لِما يُفشِلُ الرِسالَة</b> (القاعِدَة ٤ و١٠): رُموزُ العَميلِ السِتَّةُ كُلُّها مُوجِبَة — والعَدُّ يُطبَعُ ويَفشَلُ إن نَقَص — وكُلُّ رَمزٍ تُجيبُ بِه
    /// الفاتورَةُ أَو السُلطَةُ أَو الإلغاءُ سالِب، و«لا رَمز» (دَفعٌ ثَبَت) سالِب، <b>ورَمزٌ مَحقونٌ لَيسَ في المَعجَم سالِب</b> — فَلا تَفشَلُ الرِسالَةُ بِما لا يُسَمّيه العَميل.</summary>
    [Fact]
    public void The_message_fails_for_the_six_fetch_codes_and_for_no_other()
    {
        var positive = MoyasarFailures.All;
        var negative = GatewayPaymentProof.Codes.Concat(DealPayAuthorization.All).Append(GatewayReturn.InvoiceExpired)
            .Append(DealsService.NoLongerDue).Append("gateway_injected_sample").ToArray();
        output.WriteLine($"مُوجِبَة: {positive.Count} [{string.Join(", ", positive)}] · سالِبَة: {negative.Length} + «لا رَمز»");
        Assert.Equal(6, positive.Count);
        Assert.All(positive, c => Assert.True(GatewayInvoiceExpiry.FailsTheMessage(c), c));
        Assert.All(negative, c => Assert.False(GatewayInvoiceExpiry.FailsTheMessage(c), c));
        Assert.False(GatewayInvoiceExpiry.FailsTheMessage(null));
        // والعابِرَةُ مِنها رُموزُ C-PAY-1 بِلا «لَم تُدفَع» — مِن المَعجَمِ نَفسِه لا نُسخَة (القاعِدَة ٨).
        Assert.All(GatewayNoticeGate.RetriedConfirmCodes.Where(c => c != GatewayPaymentProof.InvoiceUnpaid), c => Assert.Contains(c, positive));
    }

    /// <summary>لوغٌ يُلتَقَطُ نَصّاً لِكُلِّ فِئَة — لِيُقاسَ سَطرُ اليَأسِ بِحَرفِه.</summary>
    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public System.Collections.Concurrent.ConcurrentQueue<(LogLevel Level, string Text)> Lines { get; } = new();
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
    /// <para><b>أَربَعُ ‏503 عِندَ المُهلَة ⇒ إعاداتٌ بِتَأَنٍّ ثُمَّ يَأسٌ واحِد</b> — بِـWolverine الحَقيقِيِّ في الذاكِرَةِ (بِلا قاعِدَة، فَيَعمَلُ في CI) وتَركيبِ
    /// <see cref="GatewayInvoiceExpiry.Configure"/> نَفسِه: البَوّابَةُ تُجيبُ ‏503 في كُلِّ جَلب ⇒ <b>أَربَعُ مُحاوَلات</b> (الأولى وثَلاثُ إعاداتٍ بِـ<see cref="GatewayInvoiceExpiry.RetryCooldowns"/>،
    /// والمُدَّةُ بَينَ الأولى والأَخيرَةِ ‏21 ثانِيَةً عَلى الأَقَلّ)، ثُمَّ <b>سَطرُ خَطَأٍ واحِدٌ</b> يُسَمّي المَتجَرَ والصَفقَةَ والرَمز، و<b>إشعارٌ واحِدٌ</b> لِصاحِبِ المَتجَرِ بِرابِطِ صَفقَتِه في
    /// الاستوديو، والصَفقَتانِ قائِمَتان. والبُرهانُ عَلى القاعِدَةِ (صَفٌّ في <c>wolverine_dead_letters</c>) حَيٌّ في <c>LiveScheduledExpiryProofTests</c>.</para>
    /// </summary>
    [Fact]
    public async Task Four_gateway_503s_at_expiry_retry_with_cooldowns_then_give_up_once_with_one_error_line_and_one_owner_notice()
    {
        var (w, gateway, ids) = await GatewayExpiryRaceTests.ExpiredCart();
        var at = new System.Collections.Concurrent.ConcurrentQueue<DateTimeOffset>();
        for (var i = 0; i < 8; i++)
            w.Moyasar.ThenDoing(() => at.Enqueue(DateTimeOffset.UtcNow), HttpStatusCode.ServiceUnavailable, "<html>upstream</html>");
        var owner = w.Docs.Read<ACommerce.Kit.Tenants.Tenant>(Slug)!.OwnerUserId;
        var logs = new CapturingLoggerProvider();

        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(logs);
        builder.Services.AddSingleton(gateway);
        builder.Services.AddSingleton(GatewayOwnerMoneyNoticeTests.Notices(w.Docs));
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
        Notification[] told = Array.Empty<Notification>();
        try
        {
            await host.Services.GetRequiredService<IMessageBus>().SendAsync(new GatewayInvoiceExpiryDue(Slug, ids[0]));
            while (watch.Elapsed < TimeSpan.FromSeconds(90))
            {
                // العالَمُ في الذاكِرَةِ لا يَحتَمِلُ قِراءَةً أَثناءَ كِتابَة — فَالقِراءَةُ تُعادُ إن صادَفَتها.
                try { told = w.Docs.Stored.OfType<Notification>().Where(n => n.UserId == owner).ToArray(); } catch (InvalidOperationException) { }
                if (told.Length > 0) break;
                await Task.Delay(200);
            }
            await Task.Delay(1500);   // ولا ثانٍ يَلحَقُ الأَوَّل
        }
        finally { await host.StopAsync(); }

        told = w.Docs.Stored.OfType<Notification>().Where(n => n.UserId == owner).ToArray();
        var fetches = at.ToArray();
        var gaveUp = logs.Lines.Where(x => x.Level == LogLevel.Error && x.Text.Contains("gave up", StringComparison.Ordinal)).ToArray();
        output.WriteLine($"مُحاوَلات: {fetches.Length} في {(fetches.Length > 1 ? (fetches[^1] - fetches[0]).TotalSeconds : 0):0.0}ث · سَطرُ اليَأس: {gaveUp.Length} «{gaveUp.FirstOrDefault().Text}» · " +
                         $"إشعاراتُ المالِك: {told.Length} «{told.FirstOrDefault()?.Title}» → {told.FirstOrDefault()?.RelatedUrl} · الحال: [{string.Join(", ", ids.Select(id => Load(w, id).Status))}]");

        Assert.Equal(GatewayInvoiceExpiry.RetryCooldowns.Length + 1, fetches.Length);
        Assert.True(fetches[^1] - fetches[0] >= TimeSpan.FromSeconds(GatewayInvoiceExpiry.RetryCooldowns.Sum(c => c.TotalSeconds)) - TimeSpan.FromMilliseconds(500),
            $"الإعاداتُ بِلا تَأَنٍّ: {(fetches[^1] - fetches[0]).TotalSeconds:0.0}ث");
        var line = Assert.Single(gaveUp).Text;
        Assert.Contains($"{Slug}/{ids[0]:N}", line, StringComparison.Ordinal);
        Assert.Contains($"{nameof(GatewayInvoiceExpiryFetchFailed)} {MoyasarFailures.Unreachable}", line, StringComparison.Ordinal);
        var note = Assert.Single(told);
        Assert.Equal(LocaleCatalog.Text(LocaleCatalog.Arabic, "notifications.deal_expiry_failed.title"), note.Title);
        Assert.Equal(DealNotices.StudioDealUrl(Slug, ids[0]), note.RelatedUrl);
        Assert.All(ids, id => Assert.Equal(DealStatus.Active, Load(w, id).Status));
    }
}
