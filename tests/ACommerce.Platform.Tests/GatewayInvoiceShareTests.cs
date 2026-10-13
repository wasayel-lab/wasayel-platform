using System.Net;
using ACommerce.Kit.Payments;
using ACommerce.Platform.Providers;
using ACommerce.Templates.Customer.Marketplace.Services;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.Abstractions;
using static ACommerce.Platform.Tests.TenantGatewayEndpointsTests;

namespace ACommerce.Platform.Tests;

// ═══ فاتورَةُ بَوّابَةٍ واحِدَةٌ لِصَفَقاتِ السَلَّةِ كُلِّها، وإلغاءُ صَفقَةٍ واحِدَةٍ مِنها — تَوصيفُ «اليَوم» ═══
//
// مُراجَعَةُ بَوّابَةِ المَوجَة 3b (‏2026-10-13، G5 M4 — مال): إتمامُ الشِراءِ يُولِدُ صَفقَةً لِكُلِّ بَندٍ ويَضَعُها كُلَّها عَلى فاتورَةِ Moyasar
// **واحِدَة** (`GatewayPayments.OpenInvoiceAsync`)، والطَرَفُ المُقابِلُ يُلغي أَيَّ صَفقَةٍ قائِمَةٍ وَحدَها. ومُلغي الفاتورَةِ (`7634b11`،
// `GatewayInvoiceCloser`) لا يُلغيها ما دامَت أُختٌ قائِمَةٌ تَدفَعُ لَها — فَفاتورَةُ صَفقَتَينِ أُلغِيَت إحداهُما تَبقى قابِلَةً لِلدَّفعِ **بِالمَبلَغِ
// كامِلاً** (سَلَّةٌ مُتَعَدِّدَةُ البائِعين، أَو بائِعٌ واحِدٌ نَفِدَ أَحَدُ بُنودِه): وثيقَةُ Moyasar تُغَيِّرُ بِالتَحديثِ البَياناتِ وَحدَها، والإلغاءُ يُلغي
// الفاتورَةَ كُلَّها. فَيَدفَعُ المُشتَري ثَمَنَ ما أُلغِيَ.
//
// **ما يُقلَبُ بِاسمِه في كوميتِ الإصلاح**: صَفّا «اليَوم» أَدناه (CI وحَيّ).

public class GatewayInvoiceShareTests(ITestOutputHelper output)
{
    internal const string ReturnUrl = "https://store.example/zz-gateway/pay/return?deal=1";
    internal static readonly DealCanceller Vendor = new(Seller, "بائِع", IsStoreAdmin: false);
    internal const string VendorReason = "نَفِدَت الكَمِّيَّة";

    private static Deal Load(World w, Guid id) => w.Docs.Read<Deal>(id.ToString())!;
    private static string[] Sent(MoyasarScriptedHandler h)
        => h.Requests.Select(r => $"{r.Method} {r.RequestUri!.AbsolutePath}").ToArray();

    /// <summary>
    /// <para><b>سَلَّةُ صَفقَتَينِ (‏100 و‏50) فُتِحَت فاتورَتُها بِالبابِ نَفسِه</b> — <c>OpenInvoiceAsync</c> عَلى البَوّابَةِ المُبَرمَجَة (إنشاءٌ ثُمَّ وَسمُ
    /// البَيانات)، ثُمَّ خِدمَةُ صَفَقاتٍ يَحقِنُ فيها مُلغي الفاتورَةِ كَما يَحقِنُه القالِب، بِالخِزانَةِ الَّتي خَتَمَت رَبطَ المَتجَر.</para>
    /// </summary>
    internal static async Task<(World W, DealsService Deals, CloserCapturingLogger Log)> OpenedTwoDealInvoice()
    {
        ProviderVault? sealedWith = null;
        var w = await PaidCart(v => { sealedWith = v; return PaymentsBinding(v); }, withSeller: true, invoice: false);
        var metadata = GatewayPaymentProof.MetadataFor(w.Ids);
        w.Moyasar.Then(HttpStatusCode.Created, MoyasarClientTests.InvoiceJson("initiated", 15000, metadata: new Dictionary<string, string>()))
                 .Then(HttpStatusCode.OK, MoyasarClientTests.InvoiceJson("initiated", 15000, metadata: metadata));
        var opened = await w.Gateway.OpenInvoiceAsync(Slug, new DealPayer(Buyer), w.Ids, "مَتجَر", ReturnUrl,
            new ACommerce.Platform.I18n.L(new HttpContextAccessor()));
        Assert.True(opened.Ok, opened.Code);

        var log = new CloserCapturingLogger();
        var closer = new GatewayInvoiceCloser(new TenantProviderService(w.Docs.Store), sealedWith!,
            new MoyasarClient(new HttpClient(w.Moyasar), NullLogger<MoyasarClient>.Instance), log);
        return (w, new DealsService(w.Docs.Store, w.Platform, closer), log);
    }

    /// <summary><b>اليَوم</b>: البائِعُ يُلغي صَفقَةَ الخَمسين — يُقبَلُ الإلغاء، والمُلغي يَجلِبُ الفاتورَةَ فَيَجِدُ أُختَها قائِمَةً فَيُبقيها: الفاتورَةُ
    /// مَفتوحَةٌ بِـ‏15000 هَلَلَةٍ والقائِمَةُ عَلَيها ‏100 رِيالٍ وَحدَها — ولا إلغاءَ عِندَ البَوّابَة.</summary>
    [Fact]
    public async Task Today_a_vendor_cancels_one_deal_of_an_open_two_deal_invoice_and_the_invoice_stays_payable_for_both()
    {
        var (w, deals, log) = await OpenedTwoDealInvoice();
        w.Moyasar.Then(HttpStatusCode.OK, MoyasarClientTests.InvoiceJson("initiated", 15000, metadata: GatewayPaymentProof.MetadataFor(w.Ids)));

        var res = await deals.CancelAsync(Slug, w.Ids[1], Vendor, VendorReason);

        var owed = w.Ids.Select(id => Load(w, id)).Where(GatewayInvoiceClosing.Owed).Sum(d => d.AmountSar);
        output.WriteLine($"الإلغاء: {(res.Ok ? "ok" : res.Violation!.Code)} · البَوّابَة: [{string.Join(" · ", Sent(w.Moyasar))}] · " +
                         $"القائِمُ عَلى الفاتورَة: {owed} رِيال · الفاتورَة: 150 رِيال · اللوغ: {log.Lines[^1]}");
        Assert.True(res.Ok, res.Violation?.Code);
        Assert.Equal((DealStatus.Active, DealStatus.Cancelled), (Load(w, w.Ids[0]).Status, Load(w, w.Ids[1]).Status));
        Assert.Equal($"GET /v1/invoices/{InvoiceId}", Sent(w.Moyasar)[^1]);
        Assert.DoesNotContain(Sent(w.Moyasar), r => r.EndsWith(MoyasarClient.CancelSuffix, StringComparison.Ordinal));
        Assert.Contains(GatewayInvoiceClosing.StillOwed, log.Lines[^1], StringComparison.Ordinal);
        Assert.Equal(100m, owed);
    }
}

// ═══ البُرهانُ الحَيّ — Postgres (‏WASAYEL_LIVE_PROOF=1 وConnectionStrings__Postgres) ═══════════════════════

public class LiveGatewayInvoiceShareProofTests(ITestOutputHelper output)
{
    private static bool Enabled
        => LiveDealConcurrencyProofTests.Enabled && !string.IsNullOrEmpty(LiveDealConcurrencyProofTests.ConnectionString);

    /// <summary>صَفقَتانِ عَلى Postgres فُتِحَت فاتورَتُهُما بِـ<c>OpenInvoiceAsync</c> عَلى بَوّابَةٍ مُبَرمَجَة — ويُرجِعُ ما يَلزَمُ الصَفّ.</summary>
    internal static async Task<(DealsService Deals, MoyasarScriptedHandler Moyasar, Guid[] Ids, Guid Buyer, Guid Seller)> OpenAsync(
        Marten.IDocumentStore store, string slug)
    {
        var (buyer, seller) = (Guid.NewGuid(), Guid.NewGuid());
        var vault = NewVault();
        var binding = PaymentsBinding(vault, sealedFor: slug);
        binding.TenantSlug = slug;
        await using (var s = store.LightweightSession(slug)) { s.Store(binding); await s.SaveChangesAsync(); }

        var moyasar = new MoyasarScriptedHandler();
        var client = new MoyasarClient(new HttpClient(moyasar), NullLogger<MoyasarClient>.Instance);
        var closer = new GatewayInvoiceCloser(new TenantProviderService(store), vault, client, new CloserCapturingLogger());
        var deals = new DealsService(store, new DealMoneyClaimFirstTests.RecordingPaymentProvider(), closer);

        var ids = new List<Guid>();
        foreach (var amount in new[] { 100m, 50m })
        {
            var born = await deals.StartAsync(slug, "marketplace", buyer, "مُشتَرٍ", null, "سِلعَة", amount,
                new() { [CheckoutPayMethods.AttributeKey] = CheckoutPayMethods.Card });
            await deals.AttachRefAsync(slug, born.Id, DealsService.ListingOwnerRef, seller.ToString());
            await deals.BindListingOwnerAsync(slug, born.Id, seller.ToString());
            ids.Add(born.Id);
        }
        var metadata = GatewayPaymentProof.MetadataFor(ids);
        moyasar.Then(HttpStatusCode.Created, MoyasarClientTests.InvoiceJson("initiated", 15000, metadata: new Dictionary<string, string>()))
               .Then(HttpStatusCode.OK, MoyasarClientTests.InvoiceJson("initiated", 15000, metadata: metadata));
        var gateway = new GatewayPayments(deals, new TenantProviderService(store), vault, client, new GatewayCapturingLogger());
        var opened = await gateway.OpenInvoiceAsync(slug, new DealPayer(buyer), ids, "مَتجَر", GatewayInvoiceShareTests.ReturnUrl,
            new ACommerce.Platform.I18n.L(new HttpContextAccessor()));
        Assert.True(opened.Ok, opened.Code);
        return (deals, moyasar, ids.ToArray(), buyer, seller);
    }

    internal static async Task CleanAsync(Marten.IDocumentStore store, string slug)
    {
        await using var s = store.LightweightSession(slug);
        foreach (var d in await Marten.QueryableExtensions.ToListAsync(s.Query<Deal>())) s.Delete(d);
        foreach (var b in await Marten.QueryableExtensions.ToListAsync(s.Query<TenantProviderBinding>())) s.Delete(b);
        await s.SaveChangesAsync();
    }

    /// <summary><b>اليَوم</b> عَلى Postgres: البائِعُ يُلغي صَفقَةَ الخَمسين، والفاتورَةُ تَبقى مَفتوحَةً بِالمَبلَغِ كامِلاً — لا إلغاءَ عِندَ البَوّابَة.</summary>
    [Fact]
    public async Task Today_a_vendor_cancels_one_deal_of_an_open_two_deal_invoice_and_it_stays_payable_on_Postgres()
    {
        if (!Enabled) return;

        await using var store = LiveDealConcurrencyProofTests.BuildStore();
        var slug = "zz-gws-" + Guid.NewGuid().ToString("N")[..8];
        try
        {
            var (deals, moyasar, ids, _, seller) = await OpenAsync(store, slug);
            moyasar.Then(HttpStatusCode.OK, MoyasarClientTests.InvoiceJson("initiated", 15000, metadata: GatewayPaymentProof.MetadataFor(ids)));

            var res = await deals.CancelAsync(slug, ids[1], new DealCanceller(seller, "بائِع", IsStoreAdmin: false), GatewayInvoiceShareTests.VendorReason);
            var statuses = new List<DealStatus>();
            foreach (var id in ids) statuses.Add((await deals.LoadAsync(slug, id))!.Status);
            var sent = moyasar.Requests.Select(r => $"{r.Method} {r.RequestUri!.AbsolutePath}").ToArray();
            output.WriteLine($"الإلغاء: {(res.Ok ? "ok" : res.Violation!.Code)} · الحالات: {string.Join("/", statuses)} · البَوّابَة: [{string.Join(" · ", sent)}]");

            Assert.True(res.Ok, res.Violation?.Code);
            Assert.Equal(new[] { DealStatus.Active, DealStatus.Cancelled }, statuses);
            Assert.DoesNotContain(sent, r => r.EndsWith(MoyasarClient.CancelSuffix, StringComparison.Ordinal));
        }
        finally { await CleanAsync(store, slug); }
    }
}
