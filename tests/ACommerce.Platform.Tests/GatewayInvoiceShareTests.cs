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
    internal static async Task<(World W, DealsService Deals, CloserCapturingLogger Log)> OpenedTwoDealInvoice(Guid? secondSeller = null)
    {
        ProviderVault? sealedWith = null;
        var w = await PaidCart(v => { sealedWith = v; return PaymentsBinding(v); }, withSeller: secondSeller is null, invoice: false);
        if (secondSeller is { } other)
            foreach (var (id, seller) in new[] { (w.Ids[0], Seller), (w.Ids[1], other) })
            {
                await w.Deals.AttachRefAsync(Slug, id, DealsService.ListingOwnerRef, seller.ToString());
                await w.Deals.BindListingOwnerAsync(Slug, id, seller.ToString());
            }
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

    /// <summary><b>قُلِبَ</b> (‏2026-10-13، G5 M4) — كانَ <c>Today_a_vendor_cancels_one_deal_of_an_open_two_deal_invoice_and_the_invoice_stays_payable_for_both</c>
    /// (‏<c>411e120</c>): إلغاءُ البائِعِ صَفقَةَ الخَمسينَ وَحدَها يُرَدُّ بِـ<c>deal_invoice_shared</c> <b>قَبلَ أَيِّ كِتابَة</b> — الصَفقَتانِ قائِمَتان،
    /// ولا حِفظَ ولا سُؤالَ لِلبَوّابَة؛ فَالفاتورَةُ تُساوي صَفَقاتِها القائِمَة.</summary>
    [Fact]
    public async Task A_vendors_single_cancel_of_an_open_two_deal_invoice_is_refused_before_any_write()
    {
        var (w, deals, _) = await OpenedTwoDealInvoice();
        var saves = w.Docs.SaveCalls;
        var requests = w.Moyasar.Requests.Count;

        var res = await deals.CancelAsync(Slug, w.Ids[1], Vendor, VendorReason);

        output.WriteLine($"الإلغاء: {(res.Ok ? "ok" : res.Violation!.Code)} · حِفظات: {w.Docs.SaveCalls - saves} · البَوّابَة: +{w.Moyasar.Requests.Count - requests}");
        Assert.False(res.Ok);
        Assert.Equal(GatewayInvoiceShare.Shared, res.Violation!.Code);
        Assert.Equal((DealStatus.Active, DealStatus.Active), (Load(w, w.Ids[0]).Status, Load(w, w.Ids[1]).Status));
        Assert.Equal(saves, w.Docs.SaveCalls);
        Assert.Equal(requests, w.Moyasar.Requests.Count);
    }

    /// <summary><b>وكُلُّ مَن يُلغي صَفقَةً واحِدَةً مِنها يُرَدُّ كَذلِك</b> — المُشتَري، والبائِعُ، وصاحِبُ المَتجَر — كُلٌّ عَلى أَيِّ الصَفقَتَين.</summary>
    [Theory]
    [InlineData("buyer", 0)]
    [InlineData("buyer", 1)]
    [InlineData("vendor", 0)]
    [InlineData("store_admin", 1)]
    public async Task Every_canceller_of_one_deal_of_an_open_shared_invoice_is_refused(string who, int which)
    {
        var (w, deals, _) = await OpenedTwoDealInvoice();
        var by = who switch
        {
            "buyer" => new DealCanceller(Buyer, "مُشتَرٍ", IsStoreAdmin: false),
            "vendor" => Vendor,
            _ => new DealCanceller(Other, "مالِك التَّطبيق", IsStoreAdmin: true),
        };

        var res = await deals.CancelAsync(Slug, w.Ids[which], by, VendorReason);

        Assert.Equal(GatewayInvoiceShare.Shared, res.Violation?.Code);
        Assert.All(w.Ids, id => Assert.Equal(DealStatus.Active, Load(w, id).Status));
    }

    /// <summary><b>والطَلَبُ كُلُّه يُلغى</b>: إلغاءُ الطَلَبِ مِن صَفقَةِ المُشتَري يُلغي الصَفقَتَين — المَطلوبَةُ آخِرَها — فَيَسأَلُ المُلغي البَوّابَةَ بَعدَ
    /// الأولى فَيَجِدُ أُختَها قائِمَةً فَيُبقي، ثُمَّ بَعدَ الأَخيرَةِ فَيُلغي الفاتورَةَ عِندَ البَوّابَة (‏PUT …/cancel).</summary>
    [Fact]
    public async Task The_buyer_cancels_the_whole_order_and_the_last_cancel_closes_the_invoice_at_the_gateway()
    {
        var (w, deals, log) = await OpenedTwoDealInvoice();
        var named = GatewayPaymentProof.MetadataFor(w.Ids);
        w.Moyasar.Then(HttpStatusCode.OK, MoyasarClientTests.InvoiceJson("initiated", 15000, metadata: named))
                 .Then(HttpStatusCode.OK, MoyasarClientTests.InvoiceJson("initiated", 15000, metadata: named))
                 .Then(HttpStatusCode.OK, MoyasarClientTests.InvoiceJson("canceled", 15000, metadata: named));

        var order = await deals.CancelWholeInvoiceAsync(Slug, w.Ids[0], new DealCanceller(Buyer, "مُشتَرٍ", IsStoreAdmin: false), "عَدَلتُ");

        output.WriteLine($"الطَلَب: {(order.Result.Ok ? "ok" : order.Result.Violation!.Code)} · أُلغِيَ: {order.Cancelled.Count} · البَوّابَة: [{string.Join(" · ", Sent(w.Moyasar))}]");
        Assert.True(order.Result.Ok, order.Result.Violation?.Code);
        Assert.Equal(w.Ids[0], order.Result.Deal!.Id);
        Assert.Equal(new[] { w.Ids[1], w.Ids[0] }, order.Cancelled.Select(d => d.Id));
        Assert.All(w.Ids, id => Assert.Equal(DealStatus.Cancelled, Load(w, id).Status));
        Assert.Equal(new[] { $"GET /v1/invoices/{InvoiceId}", $"GET /v1/invoices/{InvoiceId}", $"PUT /v1/invoices/{InvoiceId}{MoyasarClient.CancelSuffix}" },
            Sent(w.Moyasar)[^3..]);
        Assert.Contains(GatewayInvoiceClosing.StillOwed, log.Lines[^2], StringComparison.Ordinal);
        Assert.Contains("cancelled at the gateway", log.Lines[^1], StringComparison.Ordinal);
    }

    /// <summary><b>ولا يُلغي بائِعٌ صَفقَةَ غَيرِه بِإلغاءِ الطَلَب</b>: سَلَّةُ بائِعَين — إلغاءُ الطَلَبِ مِن صَفقَةِ الأَوَّلِ بِاسمِه يُرَدُّ بِـ<c>actor_not_party</c>
    /// <b>قَبلَ أَيِّ كِتابَة</b> (لا صَفقَتُه ولا صَفقَةُ الآخَر)؛ والمُشتَري يُلغيه كُلَّه.</summary>
    [Fact]
    public async Task A_vendor_cannot_cancel_the_whole_order_when_another_vendors_deal_is_on_the_invoice()
    {
        var (w, deals, _) = await OpenedTwoDealInvoice(secondSeller: Other);
        var saves = w.Docs.SaveCalls;

        var order = await deals.CancelWholeInvoiceAsync(Slug, w.Ids[0], Vendor, VendorReason);

        Assert.Equal(DealCancelAuthorization.ActorNotParty, order.Result.Violation?.Code);
        Assert.Empty(order.Cancelled);
        Assert.Equal(saves, w.Docs.SaveCalls);
        Assert.All(w.Ids, id => Assert.Equal(DealStatus.Active, Load(w, id).Status));
    }

    /// <summary><b>وبَعدَ الدَفعِ تُلغى وَحدَها</b>: الصَفقَتانِ مَدفوعَتانِ بِالفاتورَة (‏<c>payment_status = Captured</c>) فَلا تَدفَعُ الفاتورَةُ لِأَيٍّ مِنهُما —
    /// إلغاءُ واحِدَةٍ يُقبَلُ بِسَطرِ «استِرجاعُه مُستَحَقٌّ عِندَ البَوّابَة» القائِم، والأُخرى قائِمَة.</summary>
    [Fact]
    public async Task After_payment_one_deal_of_the_invoice_is_cancelled_alone_with_its_refund_due_line()
    {
        var (w, deals, _) = await OpenedTwoDealInvoice();
        foreach (var id in w.Ids)
        {
            await w.Deals.AttachRefAsync(Slug, id, "payment_id", InvoiceId);
            await w.Deals.AttachRefAsync(Slug, id, "payment_status", nameof(PaymentStatus.Captured));
        }

        var res = await deals.CancelAsync(Slug, w.Ids[1], Vendor, VendorReason);

        Assert.True(res.Ok, res.Violation?.Code);
        Assert.Equal((DealStatus.Active, DealStatus.Cancelled), (Load(w, w.Ids[0]).Status, Load(w, w.Ids[1]).Status));
        Assert.Contains(Load(w, w.Ids[1]).Timeline, e => e.Action == DealEvent.GatewayRefundDue);
    }

    /// <summary><b>وصَفقَةٌ فُتِحَت فاتورَتُها قَبلَ مَرجِعِ الأَخَوات</b> (بِلا <c>gateway_invoice_deals</c>) تُلغى وَحدَها كَما كانَت — الباقي المُعلَن.</summary>
    [Fact]
    public async Task A_deal_whose_invoice_predates_the_siblings_ref_is_cancelled_alone_as_before()
    {
        ProviderVault? sealedWith = null;
        var w = await PaidCart(v => { sealedWith = v; return PaymentsBinding(v); }, withSeller: true, invoice: true);
        var closer = new GatewayInvoiceCloser(new TenantProviderService(w.Docs.Store), sealedWith!,
            new MoyasarClient(new HttpClient(w.Moyasar), NullLogger<MoyasarClient>.Instance), new CloserCapturingLogger());
        w.Moyasar.Then(HttpStatusCode.OK, MoyasarClientTests.InvoiceJson("initiated", 15000, metadata: GatewayPaymentProof.MetadataFor(w.Ids)));

        var res = await new DealsService(w.Docs.Store, w.Platform, closer).CancelAsync(Slug, w.Ids[1], Vendor, VendorReason);

        Assert.Null(GatewayInvoiceShare.DealsOn(Load(w, w.Ids[1])));
        Assert.True(res.Ok, res.Violation?.Code);
    }

    // ═══ البَوّابَةُ النَقِيَّة — مُوجِبٌ وسالِبٌ لِرَمزِها ═══════════════════════════════════════════════════

    private static Deal Owed(Guid id, string invoice = InvoiceId, IReadOnlyList<Guid>? listed = null) => new()
    {
        Id = id, TenantSlug = Slug, Status = DealStatus.Active, AmountSar = 10m,
        Refs = new Dictionary<string, string>
        {
            [GatewayPaymentProof.InvoiceRef] = invoice,
            [GatewayPayments.InvoiceDealsRef] = GatewayInvoiceShare.ValueFor(listed ?? Array.Empty<Guid>()),
        },
    };

    private static (Deal Deal, Deal Sibling) Pair()
    {
        var (a, b) = (Guid.NewGuid(), Guid.NewGuid());
        return (Owed(a, listed: new[] { a, b }), Owed(b, listed: new[] { a, b }));
    }

    /// <summary><b>المُوجِب</b>: الصَفقَةُ تَدفَعُ لِفاتورَتِها وأُختٌ مُسَمّاةٌ عَلى الفاتورَةِ نَفسِها تَدفَعُ لَها.</summary>
    [Fact]
    public void Shared_fires_while_the_deal_and_a_listed_sibling_on_the_same_invoice_are_both_owed()
    {
        var (deal, sibling) = Pair();
        var v = Assert.Single(GatewayInvoiceShare.Validate(deal, new[] { sibling }));
        Assert.Equal(GatewayInvoiceShare.Shared, v.Code);
        Assert.Equal(new[] { GatewayInvoiceShare.Shared }, GatewayInvoiceShare.All);
        Assert.Equal(new[] { deal.Id, sibling.Id }, GatewayInvoiceShare.OwedOnInvoice(deal, new[] { sibling }).Select(d => d.Id));
    }

    /// <summary><b>السالِب</b>: كُلُّ شَرطٍ مَنقوصاً وَحدَه — لا يُرَدُّ الإلغاء.</summary>
    [Theory]
    [InlineData("deal_cancelled")]
    [InlineData("deal_captured")]
    [InlineData("deal_without_invoice")]
    [InlineData("sibling_cancelled")]
    [InlineData("sibling_captured")]
    [InlineData("sibling_on_another_invoice")]
    [InlineData("sibling_not_listed")]
    [InlineData("sibling_not_loaded")]
    [InlineData("no_list")]
    [InlineData("malformed_list")]
    [InlineData("duplicate_in_list")]
    public void Shared_never_fires_when_one_condition_is_missing(string why)
    {
        var (deal, sibling) = Pair();
        var siblings = new List<Deal> { sibling };
        switch (why)
        {
            case "deal_cancelled": deal.Status = DealStatus.Cancelled; break;
            case "deal_captured": deal.Refs["payment_status"] = nameof(PaymentStatus.Captured); break;
            case "deal_without_invoice": deal.Refs.Remove(GatewayPaymentProof.InvoiceRef); break;
            case "sibling_cancelled": sibling.Status = DealStatus.Cancelled; break;
            case "sibling_captured": sibling.Refs["payment_status"] = nameof(PaymentStatus.Captured); break;
            case "sibling_on_another_invoice": sibling.Refs[GatewayPaymentProof.InvoiceRef] = Guid.NewGuid().ToString(); break;
            case "sibling_not_listed": deal.Refs[GatewayPayments.InvoiceDealsRef] = GatewayInvoiceShare.ValueFor(new[] { deal.Id, Guid.NewGuid() }); break;
            case "sibling_not_loaded": siblings.Clear(); break;
            case "no_list": deal.Refs.Remove(GatewayPayments.InvoiceDealsRef); break;
            case "malformed_list": deal.Refs[GatewayPayments.InvoiceDealsRef] = $"{deal.Id},not-a-guid"; break;
            case "duplicate_in_list": deal.Refs[GatewayPayments.InvoiceDealsRef] = $"{deal.Id},{sibling.Id},{sibling.Id}"; break;
        }
        Assert.Empty(GatewayInvoiceShare.Validate(deal, siblings));
        Assert.True(GatewayInvoiceShare.IsValid(deal, siblings));
    }

    /// <summary>القائِمَةُ أَطوَلُ مِن فاتورَةٍ واحِدَةٍ لا تُقرَأ — حَدُّ الوَثيقَة.</summary>
    [Fact]
    public void A_list_longer_than_one_invoice_is_not_read()
    {
        var ids = Enumerable.Range(0, GatewayPaymentProof.MaxDealsPerInvoice + 1).Select(_ => Guid.NewGuid()).ToArray();
        Assert.Null(GatewayInvoiceShare.DealsOn(Owed(ids[0], listed: ids)));
        Assert.Equal(ids[..GatewayPaymentProof.MaxDealsPerInvoice], GatewayInvoiceShare.DealsOn(Owed(ids[0], listed: ids[..GatewayPaymentProof.MaxDealsPerInvoice])));
    }

    // ═══ الأَبواب: الصَفحَتانِ تَسأَلانِ الدالَّةَ نَفسَها، والنِقاطُ تَقرَأُ `scope=order`، والـAPI بِرَمزِه ═══════════════════════════

    private static string Code(params string[] parts)
        => WriteEndpointGuardTests.StripComments(WriteEndpointGuardTests.StripMarkupComments(
            File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, "libs", "templates", "ACommerce.Templates.Customer.Marketplace", Path.Combine(parts)))));

    [Fact]
    public void Both_deal_pages_hide_the_single_cancel_of_a_shared_invoice_and_draw_the_whole_order_door_through_the_same_predicate()
    {
        var member = Code("Components", "Pages", "MyDealDetail.razor");
        Assert.Contains("cancelShared = canCancel && !GatewayInvoiceShare.IsValid(deal, siblings);", member, StringComparison.Ordinal);
        Assert.Contains("canCancel = canCancel && !cancelShared;", member, StringComparison.Ordinal);
        Assert.Contains("else if (canCancelOrder)", member, StringComparison.Ordinal);
        Assert.Contains("<input type=\"hidden\" name=\"scope\" value=\"order\" />", member, StringComparison.Ordinal);
        Assert.Contains("L[\"deals.detail.cancel_order_action\"]", member, StringComparison.Ordinal);

        var studio = Code("Components", "Pages", "StudioAppDealDetail.razor");
        Assert.Contains("cancelShared = !GatewayInvoiceShare.IsValid(deal, await Deals.InvoiceSiblingsAsync(Slug, deal));", studio, StringComparison.Ordinal);
        Assert.Contains("<input type=\"hidden\" name=\"scope\" value=\"order\" />", studio, StringComparison.Ordinal);

        var endpoints = Code("MarketplaceTemplateExtensions.cs");
        Assert.Equal(2, endpoints.Split("req.Form[\"scope\"].ToString() == \"order\"").Length - 1);
        Assert.Equal(2, endpoints.Split("await deals.CancelWholeInvoiceAsync(slug, id, by,").Length - 1);
        foreach (var key in new[] { "deals.detail.cancel_order_action", "deals.detail.cancel_order_hint", "deals.detail.cancel_shared_hint",
                                    "deals.detail.err_deal_invoice_shared", "studio.deal.cancel_order", "studio.deal.err_deal_invoice_shared" })
        {
            var text = ACommerce.Platform.I18n.LocaleCatalog.Text(ACommerce.Platform.I18n.LocaleCatalog.Arabic, key);
            Assert.NotEqual(key, text);
            Assert.False(ACommerce.Platform.I18n.LocaleCatalog.IsPlaceholder(text), $"‏{key} نائِب.");
        }
    }

    [Fact]
    public void The_api_answers_a_shared_invoice_cancel_with_its_own_409_code()
    {
        var outcome = ACommerce.Templates.Customer.Marketplace.Api.DealApi.FromCancel(
            DealCancelResult.Refused(new DealCancelViolation(GatewayInvoiceShare.Shared, "…")));
        Assert.Equal(409, outcome.Status);
        Assert.Contains("\"deal_invoice_shared\"", ACommerce.Templates.Customer.Marketplace.Api.ApiJson.Serialize(outcome.Payload), StringComparison.Ordinal);
        Assert.Equal(GatewayInvoiceShare.Shared, ACommerce.Templates.Customer.Marketplace.Api.ApiErrorCatalog.DealInvoiceShared);
    }
}

// ═══ البُرهانُ الحَيّ — Postgres (‏WASAYEL_LIVE_PROOF=1 وConnectionStrings__Postgres) ═══════════════════════

[Collection(LiveProofCollection.Name)]
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

    /// <summary><b>قُلِبَ</b> (‏2026-10-13، G5 M4) — كانَ <c>Today_a_vendor_cancels_one_deal_of_an_open_two_deal_invoice_and_it_stays_payable_on_Postgres</c>:
    /// إلغاءُ البائِعِ صَفقَةً واحِدَةً يُرَدُّ بِـ<c>deal_invoice_shared</c> والصَفقَتانِ قائِمَتان؛ ثُمَّ يُلغي المُشتَري الطَلَبَ كُلَّه فَتُلغى الصَفقَتانِ
    /// وتُلغى الفاتورَةُ عِندَ البَوّابَة.</summary>
    [Fact]
    public async Task A_single_cancel_of_a_shared_invoice_is_refused_and_the_whole_order_cancel_closes_it_on_Postgres()
    {
        if (!Enabled) return;

        await using var store = LiveDealConcurrencyProofTests.BuildStore();
        var slug = "zz-gws-" + Guid.NewGuid().ToString("N")[..8];
        try
        {
            var (deals, moyasar, ids, buyer, seller) = await OpenAsync(store, slug);
            var named = GatewayPaymentProof.MetadataFor(ids);
            moyasar.Then(HttpStatusCode.OK, MoyasarClientTests.InvoiceJson("initiated", 15000, metadata: named))
                   .Then(HttpStatusCode.OK, MoyasarClientTests.InvoiceJson("initiated", 15000, metadata: named))
                   .Then(HttpStatusCode.OK, MoyasarClientTests.InvoiceJson("canceled", 15000, metadata: named));

            var single = await deals.CancelAsync(slug, ids[1], new DealCanceller(seller, "بائِع", IsStoreAdmin: false), GatewayInvoiceShareTests.VendorReason);
            var afterSingle = new List<DealStatus>();
            foreach (var id in ids) afterSingle.Add((await deals.LoadAsync(slug, id))!.Status);
            var requestsAfterSingle = moyasar.Requests.Count;

            var order = await deals.CancelWholeInvoiceAsync(slug, ids[0], new DealCanceller(buyer, "مُشتَرٍ", IsStoreAdmin: false), "عَدَلتُ");
            var afterOrder = new List<DealStatus>();
            foreach (var id in ids) afterOrder.Add((await deals.LoadAsync(slug, id))!.Status);
            var sent = moyasar.Requests.Select(r => $"{r.Method} {r.RequestUri!.AbsolutePath}").ToArray();
            output.WriteLine($"الواحِدَة: {single.Violation?.Code ?? "ok"} · {string.Join("/", afterSingle)} · " +
                             $"الطَلَب: {(order.Result.Ok ? "ok" : order.Result.Violation!.Code)} · {string.Join("/", afterOrder)} · البَوّابَة: [{string.Join(" · ", sent[2..])}]");

            Assert.Equal(GatewayInvoiceShare.Shared, single.Violation?.Code);
            Assert.Equal(new[] { DealStatus.Active, DealStatus.Active }, afterSingle);
            Assert.Equal(2, requestsAfterSingle);
            Assert.True(order.Result.Ok, order.Result.Violation?.Code);
            Assert.Equal(new[] { DealStatus.Cancelled, DealStatus.Cancelled }, afterOrder);
            Assert.EndsWith(MoyasarClient.CancelSuffix, sent[^1], StringComparison.Ordinal);
        }
        finally { await CleanAsync(store, slug); }
    }
}
