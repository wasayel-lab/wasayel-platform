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

    /// <summary><b>قُلِبَ</b> (‏2026-10-13) — كانَ <c>Today_a_fault_inside_the_closer_escapes_the_cancel_after_it_was_saved</c> (‏<c>2ce51fa</c>): عُطلُ تَحميلِ
    /// الأَخَواتِ داخِلَ المُلغي لا يَصعَد — الإلغاءُ «ok» والصَفقَةُ مُلغاة، والرَمزُ <c>gateway_invoice_close_faulted</c> تَحذيراً بِالاستِثناءِ نَفسِه.</summary>
    [Fact]
    public async Task A_fault_inside_the_closer_after_the_save_is_logged_as_a_warning_and_the_cancel_stands()
    {
        var (w, deals, log) = await LastOnInvoice();
        w.Moyasar.Then(HttpStatusCode.OK, PaidInvoice(w, "initiated"));
        var (saved, loadsAfter) = (false, 0);
        w.Docs.BeforeMember = name =>
        {
            if (name == "SaveChangesAsync") saved = true;
            else if (saved && name == "LoadAsync" && ++loadsAfter == 2) throw new InvalidOperationException("عُطلُ قاعِدَةٍ مَصنوع");
        };

        var res = await deals.CancelAsync(Slug, w.Ids[0], Vendor, "نَفِدَت");

        output.WriteLine($"الإلغاء: {(res.Ok ? "ok" : res.Violation!.Code)} · الحالَة: {Load(w, w.Ids[0]).Status} · اللوغ: {log.Entries[^1].Level} {log.Entries[^1].Line}");
        Assert.True(res.Ok, res.Violation?.Code);
        Assert.Equal(DealStatus.Cancelled, res.Deal!.Status);
        Assert.Equal(DealStatus.Cancelled, Load(w, w.Ids[0]).Status);
        var (level, line) = Assert.Single(log.Entries);
        Assert.Equal(LogLevel.Warning, level);
        Assert.Contains(GatewayInvoiceCloser.Faulted, line, StringComparison.Ordinal);
    }

    /// <summary><b>والعُطلُ في كُلِّ خُطوَةٍ بَعدَ الحِفظ</b>: ثالِثُ تَحميلٍ (الأُختُ الثانِيَة) — والخِزانَةُ تَرمي ما لَيسَ <c>CryptographicException</c> (رَبطُ دَفعٍ
    /// مَختومٌ بِنُسخَةِ مِفتاحٍ غَيرِ المُحَمَّلَة: <c>InvalidOperationException</c> «لا تَدويرَ مَبنِيٌّ بَعد») — كُلٌّ رَمزُه تَحذيراً والإلغاءُ قائِم.</summary>
    [Theory]
    [InlineData("second_sibling_load")]
    [InlineData("vault_kek_version")]
    public async Task Every_fault_after_the_save_leaves_the_cancel_standing(string where)
    {
        var (w, deals, log) = await LastOnInvoice();
        w.Moyasar.Then(HttpStatusCode.OK, PaidInvoice(w, "initiated"));
        if (where == "vault_kek_version")
        {
            var binding = w.Docs.Read<TenantProviderBinding>(ProviderCapabilities.Payments)!;
            var sealedValues = binding.Values.Values.Where(v => v.IsSealed).ToList();
            Assert.NotEmpty(sealedValues);
            foreach (var v in sealedValues) v.KekVersion += 1;
        }
        else
        {
            var (saved, loadsAfter) = (false, 0);
            w.Docs.BeforeMember = name =>
            {
                if (name == "SaveChangesAsync") saved = true;
                else if (saved && name == "LoadAsync" && ++loadsAfter == 3) throw new InvalidOperationException("عُطلُ قاعِدَةٍ مَصنوع");
            };
        }

        var res = await deals.CancelAsync(Slug, w.Ids[0], Vendor, "نَفِدَت");

        output.WriteLine($"{where}: {(res.Ok ? "ok" : res.Violation!.Code)} · {string.Join(" | ", log.Entries.Select(e => $"{e.Level} {e.Line}"))}");
        Assert.True(res.Ok, res.Violation?.Code);
        Assert.Equal(DealStatus.Cancelled, Load(w, w.Ids[0]).Status);
        Assert.Equal(LogLevel.Warning, log.Entries[^1].Level);
    }

    /// <summary><b>قُلِبَ</b> (‏2026-10-13) — كانَ <c>Today_a_cancel_answered_200_with_a_paid_status_is_counted_as_cancelled</c>: ‏200 بِحالِ <c>paid</c>
    /// نَتيجَةٌ بِرَمزِها <c>gateway_invoice_not_cancelled</c> وحالِها، تَحذيراً — لا «أُلغِيَت».</summary>
    [Fact]
    public async Task A_cancel_answered_200_with_a_paid_status_is_its_own_outcome_logged_as_a_warning()
    {
        var (w, deals, log) = await LastOnInvoice();
        w.Moyasar.Then(HttpStatusCode.OK, PaidInvoice(w, "initiated")).Then(HttpStatusCode.OK, PaidInvoice(w, "paid"));

        var res = await deals.CancelAsync(Slug, w.Ids[0], Vendor, "نَفِدَت");

        output.WriteLine($"الإلغاء: {(res.Ok ? "ok" : res.Violation!.Code)} · اللوغ: {log.Entries[^1].Level} {log.Entries[^1].Line}");
        Assert.True(res.Ok);
        Assert.Equal(LogLevel.Warning, log.Entries[^1].Level);
        Assert.Contains($"{GatewayInvoiceCloser.NotCancelled} (status paid)", log.Entries[^1].Line, StringComparison.Ordinal);
        Assert.DoesNotContain("cancelled at the gateway", log.Entries[^1].Line, StringComparison.Ordinal);
    }

    /// <summary><b>والسالِب</b>: ‏200 بِحالِ <c>canceled</c> «أُلغِيَت عِندَ البَوّابَة» مَعلومَةً كَما كان.</summary>
    [Fact]
    public async Task A_cancel_answered_200_with_a_canceled_status_is_cancelled_at_information()
    {
        var (w, deals, log) = await LastOnInvoice();
        w.Moyasar.Then(HttpStatusCode.OK, PaidInvoice(w, "initiated")).Then(HttpStatusCode.OK, PaidInvoice(w, MoyasarClient.CanceledStatus));

        Assert.True((await deals.CancelAsync(Slug, w.Ids[0], Vendor, "نَفِدَت")).Ok);
        Assert.Equal(LogLevel.Information, log.Entries[^1].Level);
        Assert.Contains("cancelled at the gateway (status canceled)", log.Entries[^1].Line, StringComparison.Ordinal);
    }

    /// <summary><b>قُلِبَ</b> (‏2026-10-13) — كانَ <c>Today_an_outcome_that_leaves_a_payable_invoice_is_logged_at_information</c>: كُلُّ نَتيجَةٍ تَتَرُكُ فاتورَةً قَد
    /// تُدفَعُ ثَمَناً لِما أُلغِيَ تَحذير — رَدُّ البَوّابَة، وانقِطاعُها، ولا مِفتاح، وفاتورَةٌ لَيسَت «initiated» — و«أُختٌ قائِمَة» مَعلومَةٌ كَما كانَت.</summary>
    [Theory]
    [InlineData("refused", "gateway_refused", LogLevel.Warning)]
    [InlineData("unreachable", "gateway_unreachable", LogLevel.Warning)]
    [InlineData("not_open", GatewayInvoiceClosing.NotOpen, LogLevel.Warning)]
    [InlineData("still_owed", GatewayInvoiceClosing.StillOwed, LogLevel.Information)]
    public async Task Every_outcome_that_leaves_a_payable_invoice_is_a_warning_and_a_sibling_still_owed_is_not(string how, string code, LogLevel expected)
    {
        ProviderVault? sealedWith = null;
        var w = await PaidCart(v => { sealedWith = v; return PaymentsBinding(v); }, withSeller: true, invoice: true);
        var log = new LevelCapturingCloserLogger();
        var deals = new DealsService(w.Docs.Store, w.Platform, new GatewayInvoiceCloser(new TenantProviderService(w.Docs.Store), sealedWith!,
            new MoyasarClient(new HttpClient(w.Moyasar), NullLogger<MoyasarClient>.Instance), log));
        if (how != "still_owed") await w.Deals.CancelAsync(Slug, w.Ids[1], Vendor, "نَفِدَت");
        _ = how switch
        {
            "refused"     => w.Moyasar.Then(HttpStatusCode.NotFound, """{"type":"record_not_found","message":"The <object type> record you were looking for was not found.","errors":null}"""),
            "unreachable" => w.Moyasar.Then(HttpStatusCode.ServiceUnavailable, ""),
            "not_open"    => w.Moyasar.Then(HttpStatusCode.OK, PaidInvoice(w, "paid")),
            _             => w.Moyasar.Then(HttpStatusCode.OK, PaidInvoice(w, "initiated")),
        };

        Assert.True((await deals.CancelAsync(Slug, w.Ids[0], Vendor, "نَفِدَت")).Ok);

        var (level, line) = Assert.Single(log.Entries);
        output.WriteLine($"{how}: {level} {line}");
        Assert.Equal(expected, level);
        Assert.Contains(code, line, StringComparison.Ordinal);
    }
}
