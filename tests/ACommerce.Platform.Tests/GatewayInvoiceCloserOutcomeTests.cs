using System.Net;
using ACommerce.Kit.Payments;
using ACommerce.Platform.Providers;
using ACommerce.Templates.Customer.Marketplace.Services;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.Abstractions;
using static ACommerce.Platform.Tests.TenantGatewayEndpointsTests;

namespace ACommerce.Platform.Tests;

// ═══ مُلغي الفاتورَةِ بَعدَ إلغاءٍ حُفِظ — ما يَصعَدُ مِنه، وما يُحسَبُ «أُلغِيَت»، وبِأَيِّ مُستَوى يُكتَب — تَوصيفُ «اليَوم» ═══
//
// مُراجَعَةُ بَوّابَةِ المَوجَة 3b (‏2026-10-13، G5): (١) استِثناءٌ داخِلَ المُلغي — عُطلُ قاعِدَةٍ في تَحميلِ الأَخَوات، أَو استِثناءُ خِزانَةٍ لَيسَ
// `CryptographicException` — يَصعَدُ مِن `CancelAsync` **بَعدَ** أَن حُفِظَ الإلغاء، فَتُجيبُ النُقطَةُ ‏500 عَن إلغاءٍ وَقَع؛ (٢) ‏200 مِن ‏PUT …/cancel
// حالُه غَيرُ `canceled` (مَثَلاً `paid` إن وَقَعَ دَفعٌ بَينَ الجَلبِ والإلغاء) يُحسَبُ «أُلغِيَت»؛ (٣) كُلُّ نَتيجَةٍ تُكتَبُ بِمُستَوى المَعلومَة — ولو تَرَكَت
// فاتورَةً قابِلَةً لِلدَّفع.
//
// **ما يُقلَبُ بِاسمِه في كوميتِ الإصلاح**: الصُفوفُ الثَلاثَةُ أَدناه.

/// <summary>لوغُ المُلغي بِمُستَواه ونَصِّه.</summary>
internal sealed class LevelCapturingCloserLogger : ILogger<GatewayInvoiceCloser>
{
    public List<(LogLevel Level, string Line)> Entries { get; } = new();
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter) => Entries.Add((logLevel, formatter(state, exception)));
}

public class GatewayInvoiceCloserOutcomeTests(ITestOutputHelper output)
{
    private static readonly DealCanceller Vendor = new(Seller, "بائِع", IsStoreAdmin: false);
    private static Deal Load(World w, Guid id) => w.Docs.Read<Deal>(id.ToString())!;

    /// <summary>سَلَّةُ صَفقَتَينِ عَلى فاتورَةٍ (بِلا مَرجِعِ الأَخَوات — فَيُلغى كُلٌّ وَحدَه)، وأُختُها أُلغِيَت قَبلُ بِخِدمَةٍ بِلا مُلغٍ — فَهذِه الأَخيرَة.</summary>
    private static async Task<(World W, DealsService Deals, LevelCapturingCloserLogger Log)> LastOnInvoice()
    {
        ProviderVault? sealedWith = null;
        var w = await PaidCart(v => { sealedWith = v; return PaymentsBinding(v); }, withSeller: true, invoice: true);
        var log = new LevelCapturingCloserLogger();
        var closer = new GatewayInvoiceCloser(new TenantProviderService(w.Docs.Store), sealedWith!,
            new MoyasarClient(new HttpClient(w.Moyasar), NullLogger<MoyasarClient>.Instance), log);
        await w.Deals.CancelAsync(Slug, w.Ids[1], Vendor, "نَفِدَت");
        return (w, new DealsService(w.Docs.Store, w.Platform, closer), log);
    }

    /// <summary><b>اليَوم</b>: تَحميلُ الأَخَواتِ في المُلغي يَرمي (عُطلُ قاعِدَةٍ مَصنوعٌ في ثاني تَحميلٍ بَعدَ الحِفظ — الأَوَّلُ رَبطُ الدَفع) — فَيَصعَدُ الاستِثناءُ
    /// مِن <c>CancelAsync</c> والإلغاءُ مَحفوظ.</summary>
    [Fact]
    public async Task Today_a_fault_inside_the_closer_escapes_the_cancel_after_it_was_saved()
    {
        var (w, deals, _) = await LastOnInvoice();
        w.Moyasar.Then(HttpStatusCode.OK, PaidInvoice(w, "initiated"));
        var (saved, loadsAfter) = (false, 0);
        w.Docs.BeforeMember = name =>
        {
            if (name == "SaveChangesAsync") saved = true;
            else if (saved && name == "LoadAsync" && ++loadsAfter == 2) throw new InvalidOperationException("عُطلُ قاعِدَةٍ مَصنوع");
        };

        var error = await Record.ExceptionAsync(() => deals.CancelAsync(Slug, w.Ids[0], Vendor, "نَفِدَت"));

        output.WriteLine($"الاستِثناء: {error?.GetType().Name ?? "لا"} · الحالَة: {Load(w, w.Ids[0]).Status}");
        Assert.IsType<InvalidOperationException>(error);
        Assert.Equal(DealStatus.Cancelled, Load(w, w.Ids[0]).Status);
    }

    /// <summary><b>اليَوم</b>: ‏PUT …/cancel يُجابُ ‏200 وحالُ الفاتورَةِ <c>paid</c> (دَفعٌ وَقَعَ بَينَ الجَلبِ والإلغاء) — فَيُكتَبُ «أُلغِيَت عِندَ البَوّابَة»
    /// ويُرجَعُ <c>null</c> كَالإلغاء.</summary>
    [Fact]
    public async Task Today_a_cancel_answered_200_with_a_paid_status_is_counted_as_cancelled()
    {
        var (w, deals, log) = await LastOnInvoice();
        w.Moyasar.Then(HttpStatusCode.OK, PaidInvoice(w, "initiated")).Then(HttpStatusCode.OK, PaidInvoice(w, "paid"));

        var res = await deals.CancelAsync(Slug, w.Ids[0], Vendor, "نَفِدَت");

        output.WriteLine($"الإلغاء: {(res.Ok ? "ok" : res.Violation!.Code)} · اللوغ: {log.Entries[^1].Level} {log.Entries[^1].Line}");
        Assert.True(res.Ok);
        Assert.Contains("cancelled at the gateway (status paid)", log.Entries[^1].Line, StringComparison.Ordinal);
    }

    /// <summary><b>اليَوم</b>: نَتيجَةٌ تَتَرُكُ فاتورَةً قابِلَةً لِلدَّفعِ — البَوّابَةُ تَرُدُّ الجَلب — تُكتَبُ بِمُستَوى المَعلومَة.</summary>
    [Fact]
    public async Task Today_an_outcome_that_leaves_a_payable_invoice_is_logged_at_information()
    {
        var (w, deals, log) = await LastOnInvoice();
        w.Moyasar.Then(HttpStatusCode.NotFound, """{"type":"record_not_found","message":"The <object type> record you were looking for was not found.","errors":null}""");

        var res = await deals.CancelAsync(Slug, w.Ids[0], Vendor, "نَفِدَت");

        Assert.True(res.Ok);
        var (level, line) = Assert.Single(log.Entries);
        output.WriteLine($"{level} {line}");
        Assert.Equal(LogLevel.Information, level);
        Assert.Contains(MoyasarFailures.Refused, line, StringComparison.Ordinal);
    }
}
