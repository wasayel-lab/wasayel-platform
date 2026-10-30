using System.Net;
using System.Security.Cryptography;
using ACommerce.Kit.Payments;
using ACommerce.Platform.Providers;
using ACommerce.Templates.Customer.Marketplace;
using ACommerce.Templates.Customer.Marketplace.Services;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.Abstractions;
using static ACommerce.Platform.Tests.TenantGatewayEndpointsTests;

namespace ACommerce.Platform.Tests;

// ═══ إلغاءُ الطَلَبِ وفاتورَتُه المَفتوحَةُ عِندَ البَوّابَة (‏ADR-032 الخُطوَة 10، §٨‑هـ) ═══════════════════════════════
//
// **تَوصيفُ «اليَوم» — مُثَبَّتاً قَبلَ قَلبِه** (القاعِدَة ٣؛ ‏2026-10-12، مُراجَعَةُ تَيّارِ S9 الثانِيَة — البَندُ الكَبير):
// البائِعُ المَربوطُ عِندَ الوِلادَةِ يُشعَرُ بِالطَلَبِ في إتمامِ الشِراءِ بَعدَ فَتحِ الفاتورَةِ وقَبلَ أَيِّ دَفع، والمُشتَري ساعَتَها عَلى
// صَفحَةِ البَوّابَةِ المُستَضافَة؛ ويُلغي البائِعُ (أَو المُشتَري أَو صاحِبُ المَتجَر) أَيَّ صَفقَةٍ قائِمَة (`DealCancelAuthorization`).
// و`DealsService.CancelAsync` لا تَسأَلُ البَوّابَةَ عَن الفاتورَةِ ولا تُلغيها — العَميلُ عَمَلِيَّتانِ لا ثالِثَة (§٨‑أ) — فَتَبقى
// الفاتورَةُ قابِلَةً لِلدَّفعِ عِندَ البَوّابَةِ وَلَو أُلغِيَت كُلُّ صَفقَةٍ تَدفَعُ لَها: يَدفَعُها المُشتَري مِن الصَفحَةِ المَفتوحَةِ أَمامَه،
// فَيُؤخَذُ مالُه عَن طَلَبٍ مُلغًى ويُستَرَدُّ يَدَوِيّاً (§٨‑ج.6). ووَثيقَةُ البَوّابَةِ تَعرِفُ عَمَلِيَّةً لِذلك — «Cancel invoice»،
// `PUT /invoices/:id/cancel` (‏`docs.moyasar.com/api/invoices/06-cancel-invoice`، مَقروءَةً ‏2026-10-12).
//
// **ولا مِفتاحَ حَقيقِيّاً** — عالَمُ `TenantGatewayEndpointsTests` بِحَرفِه: صَفقَتانِ لِلمُشتَري عَلى فاتورَةٍ واحِدَة، وبائِعٌ مَربوط،
// ورَبطُ دَفعٍ بِمِفتاحٍ مَختوم، ومُعالِجٌ يَلتَقِطُ كُلَّ طَلَبٍ إلى البَوّابَة.
//
// **وقُلِبَ بِاسمِه في كوميتِ الإصلاح** (‏2026-10-12 — ولا يُعادُ حَرفٌ فَوقَ ما سَبَق): `Today_…` ⇐ إلغاءُ الأولى يَسأَلُ البَوّابَةَ فَيَجِدُ أُختَها
// قائِمَةً فَيُبقيها، وإلغاءُ الأَخيرَةِ يَسأَلُ ثُمَّ يُلغيها (`GatewayInvoiceCloser` مِن `CancelAsync` بَعدَ حِفظِه). ومَعَه صُفوفُ البَوّابَةِ
// النَقِيَّةِ `GatewayInvoiceClosing` — مُوجَبٌ وسالِبٌ لِكُلِّ رَمزٍ مِن الثَلاثَة — وصُفوفُ المُلغي: كُلُّ مُلغٍ، والمَدفوعَةُ عِندَ البَوّابَة، وبِلا
// مِفتاح، وفَشَلُ البَوّابَةِ لا يَرُدُّ الإلغاء، والتَعارُضُ لا يَبلُغُ البَوّابَة، والمَقبوضَةُ سَطرُ استِرجاعِها كَما كانَ، وبِلا فاتورَة؛ وصَفُّ
// التَسجيلِ الحَقيقيِّ في القالِب؛ والبُرهانُ الحَيُّ عَلى Postgres.

/// <summary>لوغُ المُلغي يُلتَقَطُ نَصّاً — فَيُقرَأُ الرَمزُ الَّذي عادَ مِن بابِ الإلغاء، ويُقاسُ أَنّ المِفتاحَ لا يَبلُغُه.</summary>
internal sealed class CloserCapturingLogger : ILogger<GatewayInvoiceCloser>
{
    public List<string> Lines { get; } = new();
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter) => Lines.Add(formatter(state, exception));
}

public class GatewayInvoiceCancelTests(ITestOutputHelper output)
{
    private static Deal Load(World w, Guid id) => w.Docs.Read<Deal>(id.ToString())!;
    private const string VendorReason = "نَفِدَت الكَمِّيَّة";
    private static readonly DealCanceller Vendor = new(Seller, "بائِع", IsStoreAdmin: false);

    private static string Get(string id = InvoiceId) => $"GET /v1/invoices/{id}";
    private static string Put(string id = InvoiceId) => $"PUT /v1/invoices/{id}{MoyasarClient.CancelSuffix}";
    private static string[] Sent(MoyasarScriptedHandler h)
        => h.Requests.Select(r => $"{r.Method} {r.RequestUri!.AbsolutePath}").ToArray();

    /// <summary>
    /// <para><b>عالَمُ السَلَّةِ نَفسُه، وخِدمَةُ صَفَقاتٍ يَحقِنُ فيها المُلغي</b> — كَما يَحقِنُه القالِب: خِدمَةُ المُزَوِّدينَ عَلى وَثائِقِ العالَم،
    /// والخِزانَةُ الَّتي خَتَمَت رَبطَه (تُلتَقَطُ مِن وَسيطِ <c>PaidCart</c> نَفسِه فَلا يُعادُ بِناؤه)، وعَميلُ البَوّابَةِ عَلى مُعالِجِه. وخِدمَةُ
    /// العالَمِ <c>w.Deals</c> بِلا مُلغٍ كَما كانَت — فَلا يَتَغَيَّرُ ما تَقيسُه صُفوفُ غَيرِ هذا المِلَفّ.</para>
    /// </summary>
    private static async Task<(World W, DealsService Deals, CloserCapturingLogger Log)> ClosingCart(
        Func<ProviderVault, TenantProviderBinding?>? binding = null, ProviderVault? closerVault = null,
        bool withClient = true, bool invoice = true)
    {
        ProviderVault? sealedWith = null;
        var w = await PaidCart(v => { sealedWith = v; return (binding ?? (x => PaymentsBinding(x)))(v); },
            withSeller: true, invoice: invoice);
        var log = new CloserCapturingLogger();
        var closer = new GatewayInvoiceCloser(new TenantProviderService(w.Docs.Store), closerVault ?? sealedWith!,
            withClient ? new MoyasarClient(new HttpClient(w.Moyasar), NullLogger<MoyasarClient>.Instance) : null, log);
        return (w, new DealsService(w.Docs.Store, w.Platform, closer), log);
    }

    private static string Invoice(World w, string status) => PaidInvoice(w, status);

    // ═══ (١) القَلب — إلغاءُ آخِرِ قائِمَةٍ يُلغي الفاتورَة، وإلغاءُ أُختِها قَبلَها يَسأَلُ ويُبقي ═══════════════════════

    /// <summary><b>قُلِبَ بِاسمِه ‏2026-10-12</b> — كانَ <c>Today_cancelling_every_deal_on_an_open_invoice_asks_the_gateway_nothing_and_leaves_the_invoice_payable</c>:
    /// صِفرُ طَلَبٍ إلى البَوّابَةِ بَعدَ الإلغاءَين. <b>وصارَ</b>: إلغاءُ الأولى يَجلِبُ الفاتورَةَ (‏GET) فَيَجِدُ أُختَها قائِمَةً فَيُبقيها
    /// (<c>gateway_invoice_still_owed</c>)؛ وإلغاءُ الأَخيرَةِ يَجلِبُ ثُمَّ يُلغي (‏PUT ‏…/cancel بِلا جِسم) — وكُلُّها بِـ<c>Basic</c> مِن المِفتاحِ
    /// المَفكوكِ نَفسِه. ومَرجِعُ الفاتورَةِ باقٍ عَلى الصَفقَتَين، ولا نِداءَ لِمُزَوِّدِ النُسخَة، ولا مِفتاحَ في اللوغ.</summary>
    [Fact]
    public async Task Cancelling_the_last_active_deal_on_an_open_invoice_cancels_it_at_the_gateway_and_a_sibling_cancelled_first_only_asks()
    {
        var (w, deals, log) = await ClosingCart();
        w.Moyasar.Then(HttpStatusCode.OK, Invoice(w, "initiated")).Then(HttpStatusCode.OK, Invoice(w, "initiated"))
                 .Then(HttpStatusCode.OK, Invoice(w, "canceled"));

        var first = await deals.CancelAsync(Slug, w.Ids[0], Vendor, VendorReason);
        Assert.True(first.Ok, first.Violation?.Code);
        Assert.Equal(new[] { Get() }, Sent(w.Moyasar));                              // ← سُئِلَت، وأُختُها قائِمَةٌ فَبَقِيَت
        Assert.Contains(GatewayInvoiceClosing.StillOwed, log.Lines[^1], StringComparison.Ordinal);

        var last = await deals.CancelAsync(Slug, w.Ids[1], Vendor, VendorReason);
        Assert.True(last.Ok, last.Violation?.Code);
        Assert.Equal(new[] { Get(), Get(), Put() }, Sent(w.Moyasar));                // ← سُئِلَت ثُمَّ أُلغِيَت
        Assert.Null(w.Moyasar.Bodies[2]);
        Assert.All(w.Moyasar.Requests, r => Assert.Equal(("Basic", BasicOf(Key)),
            (r.Headers.Authorization!.Scheme, r.Headers.Authorization.Parameter)));
        Assert.Contains("cancelled at the gateway (status canceled)", log.Lines[^1], StringComparison.Ordinal);

        Assert.All(w.Ids, id => Assert.Equal(DealStatus.Cancelled, Load(w, id).Status));
        Assert.All(w.Ids, id => Assert.True(GatewayPayments.HasInvoice(Load(w, id))));
        Assert.Empty(w.Platform.Calls);
        Assert.DoesNotContain(log.Lines, l => l.Contains(Key, StringComparison.Ordinal) || l.Contains(Key[^4..], StringComparison.Ordinal));
        output.WriteLine($"· إلغاءُ صَفقَتَي الفاتورَة: [{string.Join(" · ", Sent(w.Moyasar))}] · مُزَوِّدُ النُسخَة={w.Platform.Calls.Count}" +
                         $" · اللوغ: {log.Lines.Count} سَطراً");
    }

    /// <summary><b>كُلُّ مُلغٍ — والأَبوابُ الثَلاثَةُ تَمُرُّ بِالكاتِبِ نَفسِه</b>: المُشتَري (بابُ المَتجَرِ والـAPI)، والبائِعُ المَربوط، وصاحِبُ
    /// المَتجَر (بابُ الاستوديو) — كُلٌّ يُلغي الصَفقَتَينِ فَيَسأَلُ مَرَّتَينِ ويُلغي الفاتورَةَ مَرَّةً بَعدَ الأَخيرَة.</summary>
    [Theory]
    [InlineData("buyer")]
    [InlineData("vendor")]
    [InlineData("store_admin")]
    public async Task Every_canceller_closes_the_invoice_with_the_last_active_deal_on_it(string who)
    {
        var (w, deals, _) = await ClosingCart();
        var by = who switch
        {
            "buyer" => new DealCanceller(Buyer, "مُشتَرٍ", IsStoreAdmin: false),
            "vendor" => Vendor,
            _ => new DealCanceller(Other, "مالِك التَّطبيق", IsStoreAdmin: true),
        };
        w.Moyasar.Then(HttpStatusCode.OK, Invoice(w, "initiated")).Then(HttpStatusCode.OK, Invoice(w, "initiated"))
                 .Then(HttpStatusCode.OK, Invoice(w, "canceled"));

        foreach (var id in w.Ids)
            Assert.True((await deals.CancelAsync(Slug, id, by, VendorReason)).Ok);

        Assert.Equal(new[] { Get(), Get(), Put() }, Sent(w.Moyasar));
    }

    // ═══ (٢) ما لا يُلغي الفاتورَة — ويُسأَلُ قَبلَه ═══════════════════════════════════════════════════════

    /// <summary><b>فاتورَةٌ دَفَعَها المُشتَري ولَم يَضغَط «تَحَقَّق»</b>: الإلغاءُ يَجلِبُها فَيَجِدُها <c>paid</c> فَلا يُلغيها
    /// (<c>gateway_invoice_not_open</c>) — ودَفعُها يُسَجَّلُ بِبابِ التَحَقُّقِ ومَعَه سَطرُ استِرجاعِه (§٨‑هـ)، لا هُنا.</summary>
    [Fact]
    public async Task An_invoice_already_paid_at_the_gateway_is_not_cancelled()
    {
        var (w, deals, log) = await ClosingCart();
        w.Moyasar.Then(HttpStatusCode.OK, Invoice(w, "paid")).Then(HttpStatusCode.OK, Invoice(w, "paid"));

        foreach (var id in w.Ids)
            Assert.True((await deals.CancelAsync(Slug, id, Vendor, VendorReason)).Ok);

        Assert.Equal(new[] { Get(), Get() }, Sent(w.Moyasar));
        Assert.All(log.Lines, l => Assert.Contains(GatewayInvoiceClosing.NotOpen, l, StringComparison.Ordinal));
        Assert.All(w.Ids, id => Assert.False(DealsService.IsCaptured(Load(w, id))));
    }

    /// <summary><b>بِلا مِفتاحٍ يُفَكُّ أَو بِلا عَميل — لا نِداء</b>: لا رَبط، أَو رَبطٌ مَسحوب، أَو خِزانَةٌ بِلا مِفتاحٍ رَئيس، أَو ظَرفٌ لا يُفتَحُ بِخِزانَةٍ
    /// أُخرى، أَو عَميلٌ لا يُسَجِّلُه المُضيف — <c>gateway_not_configured</c> في اللوغ، والإلغاءُ ماضٍ.</summary>
    [Theory]
    [InlineData("no_binding")]
    [InlineData("revoked")]
    [InlineData("vault_unavailable")]
    [InlineData("another_master_key")]
    [InlineData("no_client")]
    public async Task Without_a_key_that_opens_or_a_client_the_cancel_asks_the_gateway_nothing(string why)
    {
        var (w, deals, log) = why switch
        {
            "no_binding"         => await ClosingCart(_ => null),
            "revoked"            => await ClosingCart(v => PaymentsBinding(v, status: TenantProviderBinding.StatusRevoked)),
            "vault_unavailable"  => await ClosingCart(closerVault: ProviderVault.FromKek(null)),
            "another_master_key" => await ClosingCart(closerVault: NewVault()),
            _                    => await ClosingCart(withClient: false),
        };

        foreach (var id in w.Ids)
            Assert.True((await deals.CancelAsync(Slug, id, Vendor, VendorReason)).Ok);

        Assert.Empty(w.Moyasar.Requests);
        Assert.Equal(2, log.Lines.Count(l => l.Contains(MoyasarFailures.NotConfigured, StringComparison.Ordinal)));
        Assert.All(w.Ids, id => Assert.Equal(DealStatus.Cancelled, Load(w, id).Status));
    }

    /// <summary><b>فَشَلُ البَوّابَةِ رَمزٌ في اللوغِ لا يَرُدُّ الإلغاء</b>: الجَلبُ بِلا جَواب (‏503) أَو مَرفوضٌ (‏404) فَلا إلغاءَ عِندَها؛ أَو الإلغاءُ
    /// نَفسُه مَرفوضٌ (‏400 — حالٌ لا تَقبَلُه، لا تَقولُه الوَثيقَة) أَو بِلا جَواب — والصَفقَةُ مُلغاةٌ في كُلِّها، والنَتيجَةُ «أُلغِيَت».</summary>
    [Theory]
    [InlineData("fetch_unreachable", 1, "gateway_unreachable")]
    [InlineData("fetch_refused", 1, "gateway_refused")]
    [InlineData("cancel_refused", 2, "gateway_refused")]
    [InlineData("cancel_unreachable", 2, "gateway_unreachable")]
    public async Task A_gateway_that_fails_is_logged_by_its_code_and_the_cancel_stands(string how, int calls, string code)
    {
        var (w, deals, log) = await ClosingCart();
        await w.Deals.CancelAsync(Slug, w.Ids[1], Vendor, VendorReason);   // أُختُها أُلغِيَت قَبلُ بِخِدمَةٍ بِلا مُلغٍ — فَهذِه الأَخيرَة
        const string notFound = """{"type":"record_not_found","message":"The <object type> record you were looking for was not found.","errors":null}""";
        _ = how switch
        {
            "fetch_unreachable" => w.Moyasar.Then(HttpStatusCode.ServiceUnavailable, ""),
            "fetch_refused"     => w.Moyasar.Then(HttpStatusCode.NotFound, notFound),
            "cancel_refused"    => w.Moyasar.Then(HttpStatusCode.OK, Invoice(w, "initiated"))
                                            .Then(HttpStatusCode.BadRequest, """{"type":"invalid_request_error","message":"Validation Failed","errors":null}"""),
            _                   => w.Moyasar.Then(HttpStatusCode.OK, Invoice(w, "initiated")).ThenThrow(),
        };

        var res = await deals.CancelAsync(Slug, w.Ids[0], Vendor, VendorReason);

        Assert.True(res.Ok, res.Violation?.Code);
        Assert.Equal(DealStatus.Cancelled, Load(w, w.Ids[0]).Status);
        Assert.Equal(calls, w.Moyasar.Requests.Count);
        Assert.Contains(code, Assert.Single(log.Lines), StringComparison.Ordinal);
    }

    /// <summary><b>المُطالَبَةُ أَوَّلاً</b> (‏ADR-036 الخُطوَة 2): إلغاءٌ يَرُدُّه تَعارُضُ النُسخَةِ بِـ<c>deal_changed_meanwhile</c> لَم يُكتَب، فَلا يَبلُغُ
    /// البَوّابَةَ أَصلاً — والفاتورَةُ تَدفَعُ لِصَفقَةٍ ما زالَت قائِمَة.</summary>
    [Fact]
    public async Task A_cancel_refused_by_a_version_conflict_asks_the_gateway_nothing()
    {
        var (w, deals, log) = await ClosingCart();
        await w.Deals.CancelAsync(Slug, w.Ids[1], Vendor, VendorReason);
        var armed = true;
        w.Docs.BeforeMember = name =>
        {
            if (name != "SaveChangesAsync" || !armed) return;
            armed = false;
            throw new JasperFx.ConcurrencyException(typeof(Deal), w.Ids[0]);
        };

        var res = await deals.CancelAsync(Slug, w.Ids[0], Vendor, VendorReason);

        Assert.False(res.Ok);
        Assert.Equal(DealsService.ChangedMeanwhile, res.Violation!.Code);
        Assert.Empty(w.Moyasar.Requests);
        Assert.Empty(log.Lines);
    }

    /// <summary><b>صَفقَةٌ قَبَضَت البَوّابَةُ ثَمَنَها</b>: سَطرُ «استِرجاعُه مُستَحَقّ» كَما كانَ (§٨‑ج.6) ولا يُسأَلُ عَن الفاتورَة — هي مَدفوعَة؛ <b>وصَفقَةٌ
    /// بِلا فاتورَة</b> (طَريقُ مُزَوِّدِ النُسخَةِ أَو النَقد) لا تَبلُغُ المُلغي.</summary>
    [Fact]
    public async Task A_deal_collected_by_the_gateway_keeps_its_refund_due_line_and_a_deal_without_an_invoice_asks_nothing()
    {
        var (w, deals, log) = await ClosingCart();
        foreach (var id in w.Ids)
        {
            await w.Deals.AttachRefAsync(Slug, id, "payment_id", InvoiceId);
            await w.Deals.AttachRefAsync(Slug, id, "payment_status", nameof(PaymentStatus.Captured));
        }
        Assert.True((await deals.CancelAsync(Slug, w.Ids[0], Vendor, VendorReason)).Ok);
        Assert.Single(Load(w, w.Ids[0]).Timeline, e => e.Action == DealEvent.GatewayRefundDue);

        var (bare, bareDeals, bareLog) = await ClosingCart(invoice: false);
        Assert.True((await bareDeals.CancelAsync(Slug, bare.Ids[0], Vendor, VendorReason)).Ok);

        Assert.Empty(w.Moyasar.Requests);
        Assert.Empty(bare.Moyasar.Requests);
        Assert.Empty(log.Lines);
        Assert.Empty(bareLog.Lines);
    }

    /// <summary><b>والمُلغي نَفسُه يَحرُسُ مُدخَلَه</b>: صَفقَةٌ بِلا مَرجِعِ فاتورَةٍ تُرَدُّ بِـ<c>gateway_invoice_missing</c> بِلا فَكِّ مِفتاحٍ ولا نِداء —
    /// وإن لَم يُنادِه الإلغاءُ لَها.</summary>
    [Fact]
    public async Task The_closer_called_on_a_deal_without_an_invoice_returns_its_code_and_calls_nothing()
    {
        var w = await PaidCart(withSeller: true, invoice: false);
        var log = new CloserCapturingLogger();
        var closer = new GatewayInvoiceCloser(new TenantProviderService(w.Docs.Store), NewVault(),
            new MoyasarClient(new HttpClient(w.Moyasar), NullLogger<MoyasarClient>.Instance), log);

        var code = await closer.CloseIfLastAsync(Slug, Load(w, w.Ids[0]), id => w.Deals.LoadAsync(Slug, id));

        Assert.Equal(DealPayAuthorization.InvoiceMissing, code);
        Assert.Empty(w.Moyasar.Requests);
        Assert.Contains(DealPayAuthorization.InvoiceMissing, Assert.Single(log.Lines), StringComparison.Ordinal);
    }

    // ═══ (٣) البَوّابَةُ النَقِيَّة — ثَلاثَةُ رُموز، مُوجَبٌ وسالِبٌ لِكُلٍّ ═══════════════════════════════════════

    private static Deal OnInvoice(DealStatus status = DealStatus.Cancelled, bool paid = false, string invoice = InvoiceId)
    {
        var d = new Deal { Id = Guid.NewGuid(), Pattern = "marketplace", Status = status, InitiatorId = Buyer, AmountSar = 100m };
        d.Refs[GatewayPaymentProof.InvoiceRef] = invoice;
        if (paid)
        {
            d.Refs["payment_id"] = invoice;
            d.Refs["payment_status"] = nameof(PaymentStatus.Captured);
        }
        return d;
    }

    private static MoyasarInvoice InvoiceOf(string status, params Deal[] listed)
        => new(InvoiceId, status, 20000, "SAR", null, GatewayPaymentProof.MetadataFor(listed.Select(d => d.Id).ToArray()));

    private static string[] Codes(MoyasarInvoice invoice, Deal cancelled, params Deal[] named)
        => GatewayInvoiceClosing.Validate(invoice, cancelled, named).Select(v => v.Code).ToArray();

    /// <summary>المَعجَمُ مُغلَقٌ بِثَلاثَة، ولا يَبلُغُ رَمزٌ مِنه شاشَة: لَيسَ في رُموزِ صَفحَةِ العَودَةِ ولا مِفتاحَ قامُوسٍ لَه، ولا يُصادِمُ مَعجَمَ العَميل.</summary>
    [Fact]
    public void The_closing_vocabulary_is_closed_at_three_and_none_of_its_codes_reaches_a_screen()
    {
        Assert.Equal(new[] { "gateway_invoice_not_listed", "gateway_invoice_not_open", "gateway_invoice_still_owed" }, GatewayInvoiceClosing.All);
        Assert.Equal(3, GatewayInvoiceClosing.All.Distinct(StringComparer.Ordinal).Count());
        var checkedCodes = 0;
        foreach (var code in GatewayInvoiceClosing.All)
        {
            Assert.DoesNotContain(code, GatewayReturn.Codes);
            Assert.Null(GatewayReturn.KeyFor(code));
            Assert.False(MoyasarFailures.Contains(code));
            Assert.False(GatewayPaymentProof.Contains(code));
            checkedCodes++;
        }
        output.WriteLine($"· رُموزُ الإبقاء: {checkedCodes} — لا شاشَةَ تَبلُغُها.");
        Assert.Equal(3, checkedCodes);
    }

    /// <summary><b>المُوجِبُ كامِلاً</b>: فاتورَتُها، مَفتوحَة، ولا قائِمَةَ عَلَيها لَم تُدفَع ⇒ لا سَبَبَ لِإبقائِها — <c>IsValid</c>.</summary>
    [Fact]
    public void An_open_invoice_of_the_cancelled_deal_whose_named_deals_are_all_cancelled_is_closed()
    {
        var (cancelled, sibling) = (OnInvoice(), OnInvoice());
        var invoice = InvoiceOf(MoyasarClient.OpenStatus, cancelled, sibling);
        Assert.Empty(Codes(invoice, cancelled, cancelled, sibling));
        Assert.True(GatewayInvoiceClosing.IsValid(invoice, cancelled, new[] { cancelled, sibling }));
    }

    /// <summary><c>gateway_invoice_not_listed</c> — مُوجَب: البَياناتُ لا تُسَمّيها، أَو المُسَجَّلُ عَلَيها فاتورَةٌ أُخرى، أَو لا مَرجِعَ عَلَيها، أَو
    /// بَياناتٌ لا تُقرَأ، أَو أَكثَرُ مِن ثَلاثينَ صَفقَة.</summary>
    [Theory]
    [InlineData("metadata_omits_the_deal")]
    [InlineData("recorded_on_another_invoice")]
    [InlineData("no_invoice_recorded")]
    [InlineData("unreadable_metadata")]
    [InlineData("over_thirty_deals")]
    public void NotListed_fires_when_the_invoice_is_not_the_cancelled_deals(string why)
    {
        var cancelled = OnInvoice();
        var other = OnInvoice();
        var invoice = why switch
        {
            "metadata_omits_the_deal"     => InvoiceOf(MoyasarClient.OpenStatus, other),
            "unreadable_metadata"         => InvoiceOf(MoyasarClient.OpenStatus, cancelled) with
                                             { Metadata = new Dictionary<string, string> { ["deal_1"] = "not-a-deal-id" } },
            "over_thirty_deals"           => InvoiceOf(MoyasarClient.OpenStatus, cancelled) with
                                             { Metadata = Enumerable.Range(0, 31).ToDictionary(i => $"deal_{i + 1}",
                                                   i => (i == 0 ? cancelled.Id : Guid.NewGuid()).ToString("N")) },
            _                             => InvoiceOf(MoyasarClient.OpenStatus, cancelled),
        };
        if (why == "recorded_on_another_invoice") cancelled.Refs[GatewayPaymentProof.InvoiceRef] = Guid.NewGuid().ToString("D");
        if (why == "no_invoice_recorded") cancelled.Refs.Remove(GatewayPaymentProof.InvoiceRef);

        Assert.Equal(new[] { GatewayInvoiceClosing.NotListed }, Codes(invoice, cancelled, cancelled, other));
    }

    /// <summary><c>gateway_invoice_not_listed</c> — سالِب: الفاتورَةُ المُسَجَّلَةُ عَلَيها (والحَرفُ لا يُفَرِّقُ في المُعَرِّف) وبَياناتُها تُسَمّيها.</summary>
    [Fact]
    public void NotListed_never_fires_for_the_invoice_recorded_on_the_deal_that_names_it()
    {
        var cancelled = OnInvoice(invoice: InvoiceId.ToUpperInvariant());
        Assert.DoesNotContain(GatewayInvoiceClosing.NotListed, Codes(InvoiceOf(MoyasarClient.OpenStatus, cancelled), cancelled, cancelled));
    }

    /// <summary><c>gateway_invoice_not_open</c> — مُوجَب: كُلُّ حالٍ في مَعجَمِ <c>InvoiceStatus</c> سِوى <c>initiated</c>، والمَعجَمُ مِن الوَثيقَةِ بِحَرفِه.</summary>
    [Theory]
    [InlineData("paid")]
    [InlineData("failed")]
    [InlineData("refunded")]
    [InlineData("canceled")]
    [InlineData("on_hold")]
    [InlineData("expired")]
    [InlineData("voided")]
    public void NotOpen_fires_for_every_documented_status_but_initiated(string status)
    {
        var cancelled = OnInvoice();
        Assert.Equal(new[] { GatewayInvoiceClosing.NotOpen }, Codes(InvoiceOf(status, cancelled), cancelled, cancelled));
    }

    /// <summary><c>gateway_invoice_not_open</c> — سالِب: <c>initiated</c>، حالُ مِثالِ رَدِّ «Create Invoice».</summary>
    [Fact]
    public void NotOpen_never_fires_for_an_initiated_invoice()
    {
        var cancelled = OnInvoice();
        Assert.Equal("initiated", MoyasarClient.OpenStatus);
        Assert.DoesNotContain(GatewayInvoiceClosing.NotOpen, Codes(InvoiceOf("initiated", cancelled), cancelled, cancelled));
    }

    /// <summary><c>gateway_invoice_still_owed</c> — مُوجَب: أُختٌ تُسَمّيها الفاتورَةُ قائِمَةٌ لَم تُدفَع، أَو الصَفقَةُ المُمَرَّرَةُ نَفسُها ما زالَت
    /// قائِمَة (مُنادٍ نادى قَبلَ الحِفظ) — والفاتورَةُ تَدفَعُ لَها في الحالَين.</summary>
    [Theory]
    [InlineData("active_sibling")]
    [InlineData("the_deal_itself_still_active")]
    public void StillOwed_fires_while_a_deal_the_invoice_pays_for_is_active_and_unpaid(string who)
    {
        var cancelled = OnInvoice(who == "the_deal_itself_still_active" ? DealStatus.Active : DealStatus.Cancelled);
        var sibling = OnInvoice(who == "active_sibling" ? DealStatus.Active : DealStatus.Cancelled);
        Assert.Equal(new[] { GatewayInvoiceClosing.StillOwed },
            Codes(InvoiceOf(MoyasarClient.OpenStatus, cancelled, sibling), cancelled, sibling));
    }

    /// <summary><c>gateway_invoice_still_owed</c> — سالِب: أُختٌ مُلغاةٌ أَو في نِزاعٍ أَو مُكتَمِلَة (لا «أَكمِلِ الدَّفعَ» لَها، §٨‑هـ.1)، أَو قائِمَةٌ
    /// كُتِبَ دَفعُها — «القائِمَةُ» بِحَرفِ الرابِطِ والإشعارِ والفَتح.
    /// <para>(‏2026-10-30، تَدقيقُ الموجَة ١١ CO12 — ولا يُعادُ حَرفٌ فَوقَ ما سَبَق): صَفُّ <c>(Disputed, false)</c> <b>قُلِبَ</b> — أُختٌ في «نِزاع» لَم تُقبَض تُبقي الفاتورَةَ
    /// (<see cref="GatewayInvoiceClosing.KeepsOpen"/>، صَفُّه المُوجِبُ في <c>GatewayInvoiceDisputedSiblingTests</c>)؛ ومَكانَه <c>(Disputed, true)</c>: في «نِزاع» وقُبِضَت ⇐ لا تُبقيها.</para></summary>
    [Theory]
    [InlineData(DealStatus.Cancelled, false)]
    [InlineData(DealStatus.Disputed, true)]
    [InlineData(DealStatus.Completed, false)]
    [InlineData(DealStatus.Active, true)]
    public void StillOwed_never_fires_for_a_sibling_that_is_not_active_or_is_already_paid(DealStatus status, bool paid)
    {
        var cancelled = OnInvoice();
        var sibling = OnInvoice(status, paid);
        Assert.False(GatewayInvoiceClosing.Owed(sibling));
        Assert.DoesNotContain(GatewayInvoiceClosing.StillOwed,
            Codes(InvoiceOf(MoyasarClient.OpenStatus, cancelled, sibling), cancelled, sibling));
    }

    /// <summary>وما لا تُسَمّيه البَياناتُ لا يَحبِسُها — صَفقَةٌ قائِمَةٌ مُمَرَّرَةٌ في <c>named</c> ولَيسَت في الفاتورَةِ لا تُحسَب.</summary>
    [Fact]
    public void An_active_deal_the_invoice_does_not_name_does_not_hold_it_open()
    {
        var cancelled = OnInvoice();
        var stranger = OnInvoice(DealStatus.Active);
        Assert.Empty(Codes(InvoiceOf(MoyasarClient.OpenStatus, cancelled), cancelled, cancelled, stranger));
    }

    // ═══ (٤) التَسجيلُ الحَقيقيّ — القالِبُ يَحقِنُ المُلغي في خِدمَةِ الصَفَقات ══════════════════════════════════════

    /// <summary>
    /// <para><b>مِن تَسجيلِ القالِبِ نَفسِه، لا مِن بِناءٍ بِاليَد</b>: <c>AddCustomerMarketplaceTemplate</c> بِمِفتاحٍ رَئيسٍ في التَهيئَة، ومَخزَنُ العالَم،
    /// ومُعالِجٌ أَوَّلِيٌّ لِعَميلِ البَوّابَةِ المُسَجَّل — ثُمَّ <c>DealsService</c> مِن النِطاقِ كَما تَحقِنُه أَبوابُ الإلغاءِ الثَلاثَة. فَإلغاءُ صَفقَةِ
    /// فاتورَةٍ وَحيدَةٍ يَسأَلُ البَوّابَةَ ويُلغيها بِمِفتاحِ المَتجَرِ المَختومِ بِتِلكَ الخِزانَة. ولَو سَقَطَ سَطرُ التَسجيلِ لَبَقِيَت الخِدمَةُ بِلا مُلغٍ
    /// فَحَمِرَ هذا الصَفّ.</para>
    /// </summary>
    [Fact]
    public async Task The_template_registration_hands_the_closer_to_the_deal_service_every_cancel_door_resolves()
    {
        var docs = new DocWorld();
        var handler = new MoyasarScriptedHandler();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [ProviderVault.KekConfigKey] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
        }).Build());
        services.AddLogging();
        services.AddCustomerMarketplaceTemplate();
        services.AddSingleton<Marten.IDocumentStore>(docs.Store);
        services.AddSingleton<IPaymentProvider>(new DealMoneyClaimFirstTests.RecordingPaymentProvider());
        services.Configure<HttpClientFactoryOptions>(nameof(MoyasarClient),
            o => o.HttpMessageHandlerBuilderActions.Add(b => b.PrimaryHandler = handler));
        await using var sp = services.BuildServiceProvider();
        await using var scope = sp.CreateAsyncScope();

        docs.Put(PaymentsBinding(sp.GetRequiredService<ProviderVault>()));
        var deals = scope.ServiceProvider.GetRequiredService<DealsService>();
        var deal = await deals.StartAsync(Slug, "marketplace", Buyer, "مُشتَرٍ", null, "سِلعَة", 150m,
            new() { [CheckoutPayMethods.AttributeKey] = CheckoutPayMethods.Card });
        await deals.AttachRefAsync(Slug, deal.Id, GatewayPaymentProof.InvoiceRef, InvoiceId);
        var named = GatewayPaymentProof.MetadataFor(new[] { deal.Id });
        handler.Then(HttpStatusCode.OK, MoyasarClientTests.InvoiceJson("initiated", 15000, metadata: named))
               .Then(HttpStatusCode.OK, MoyasarClientTests.InvoiceJson("canceled", 15000, metadata: named));

        var res = await deals.CancelAsync(Slug, deal.Id, new DealCanceller(Buyer, "مُشتَرٍ", IsStoreAdmin: false), "عَدَلتُ");

        Assert.True(res.Ok, res.Violation?.Code);
        Assert.Equal(new[] { Get(), Put() }, Sent(handler));
        Assert.All(handler.Requests, r => Assert.Equal(BasicOf(Key), r.Headers.Authorization!.Parameter));
        output.WriteLine($"· مِن تَسجيلِ القالِب: [{string.Join(" · ", Sent(handler))}]");
    }
}

// ─── البُرهانُ الحَيّ — Postgres بِتَزامُنٍ تَفاؤُلِيٍّ حَقيقِيّ ──────────────────────────────────────────────
//
// يَتَخَطّى نَفسَه ما لَم تُضبَط WASAYEL_LIVE_PROOF=1 و ConnectionStrings__Postgres — عَقدُ `LiveDealConcurrencyProofTests` حَرفاً. صَفقَتانِ عَلى
// فاتورَةٍ واحِدَةٍ في قاعِدَةٍ حَقيقِيَّة، ورَبطُ دَفعٍ مَختومٌ في وَثيقَتِه، ومُعالِجٌ يُعيدُ رُدودَ الوَثيقَة: إلغاءُ الأولى يَسأَلُ ويُبقي، وإلغاءُ
// الأَخيرَةِ يَسأَلُ ويُلغي — والمُحَمِّلُ قِراءَةُ Marten نَفسُها بَعدَ حِفظِ الإلغاء.

[Collection(LiveProofCollection.Name)]
public class LiveGatewayInvoiceCancelProofTests(ITestOutputHelper output)
{
    private static bool Enabled
        => LiveDealConcurrencyProofTests.Enabled && !string.IsNullOrEmpty(LiveDealConcurrencyProofTests.ConnectionString);

    [Fact]
    public async Task Cancelling_both_deals_of_an_invoice_on_postgres_keeps_it_after_the_first_and_cancels_it_after_the_last()
    {
        if (!Enabled) return;

        await using var store = LiveDealConcurrencyProofTests.BuildStore();
        var slug = "zz-gwc-" + Guid.NewGuid().ToString("N")[..8];
        var (buyer, seller) = (Guid.NewGuid(), Guid.NewGuid());
        try
        {
            var vault = NewVault();
            var binding = PaymentsBinding(vault, sealedFor: slug);
            binding.TenantSlug = slug;
            await using (var s = store.LightweightSession(slug)) { s.Store(binding); await s.SaveChangesAsync(); }

            var moyasar = new MoyasarScriptedHandler();
            var log = new CloserCapturingLogger();
            var closer = new GatewayInvoiceCloser(new TenantProviderService(store), vault,
                new MoyasarClient(new HttpClient(moyasar), NullLogger<MoyasarClient>.Instance), log);
            var deals = new DealsService(store, new DealMoneyClaimFirstTests.RecordingPaymentProvider(), closer);

            var ids = new List<Guid>();
            foreach (var amount in new[] { 100m, 50m })
            {
                var born = await deals.StartAsync(slug, "marketplace", buyer, "مُشتَرٍ", null, "سِلعَة", amount,
                    new() { [CheckoutPayMethods.AttributeKey] = CheckoutPayMethods.Card });
                await deals.AttachRefAsync(slug, born.Id, GatewayPaymentProof.InvoiceRef, InvoiceId);
                await deals.AttachRefAsync(slug, born.Id, DealsService.ListingOwnerRef, seller.ToString());
                await deals.BindListingOwnerAsync(slug, born.Id, seller.ToString());
                ids.Add(born.Id);
            }
            var named = GatewayPaymentProof.MetadataFor(ids);
            moyasar.Then(HttpStatusCode.OK, MoyasarClientTests.InvoiceJson("initiated", 15000, metadata: named))
                   .Then(HttpStatusCode.OK, MoyasarClientTests.InvoiceJson("initiated", 15000, metadata: named))
                   .Then(HttpStatusCode.OK, MoyasarClientTests.InvoiceJson("canceled", 15000, metadata: named));

            var vendor = new DealCanceller(seller, "بائِع", IsStoreAdmin: false);
            Assert.True((await deals.CancelAsync(slug, ids[0], vendor, "نَفِدَت")).Ok);
            var afterFirst = moyasar.Requests.Select(r => r.Method.Method).ToArray();
            Assert.True((await deals.CancelAsync(slug, ids[1], vendor, "نَفِدَت")).Ok);
            var sent = moyasar.Requests.Select(r => $"{r.Method} {r.RequestUri!.AbsolutePath}").ToArray();
            output.WriteLine($"بَعدَ الأولى: [{string.Join(", ", afterFirst)}] · بَعدَ الأَخيرَة: [{string.Join(" · ", sent)}] · " +
                             $"اللوغ: {string.Join(" | ", log.Lines)}");

            Assert.Equal(new[] { "GET" }, afterFirst);
            Assert.Equal(new[] { $"GET /v1/invoices/{InvoiceId}", $"GET /v1/invoices/{InvoiceId}",
                                 $"PUT /v1/invoices/{InvoiceId}{MoyasarClient.CancelSuffix}" }, sent);
            foreach (var id in ids)
                Assert.Equal(DealStatus.Cancelled, (await deals.LoadAsync(slug, id))!.Status);
        }
        finally
        {
            await using var s = store.LightweightSession(slug);
            foreach (var d in await Marten.QueryableExtensions.ToListAsync(s.Query<Deal>())) s.Delete(d);
            foreach (var b in await Marten.QueryableExtensions.ToListAsync(s.Query<TenantProviderBinding>())) s.Delete(b);
            await s.SaveChangesAsync();
        }
    }
}
