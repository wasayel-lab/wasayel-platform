using System.Net;
using ACommerce.Kit.Payments;
using ACommerce.Platform.I18n;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using Xunit;
using Xunit.Abstractions;
using static ACommerce.Platform.Tests.TenantGatewayEndpointsTests;

namespace ACommerce.Platform.Tests;

// ═══ حَسمُ نِزاعٍ بِالإلغاءِ عَلى صَفقَةٍ مِن فاتورَةِ بَوّابَةٍ مَفتوحَةٍ مُشتَرَكَة — تَوصيفُ «اليَوم» (‏2026-10-29، مُراجَعَةُ تَيّارِ S5 الأولى عَلى `c961e95`) ═══
//
// قاعِدَةُ G5 M4 (‏2026-10-13، ‏ADR-032 §٨‑ز مُلحَقاً): «لا تُلغى وَحدَها صَفقَةٌ عَلى فاتورَةِ بَوّابَةٍ مَفتوحَةٍ تَدفَعُ لِأُختٍ قائِمَة» — فَتَبقى الفاتورَةُ
// مُساوِيَةً لِصَفَقاتِها القائِمَة. **والمَقيسُ عِندَ الرَأس**: حَسمُ النِزاعِ بِالإلغاءِ يَمُرُّ بِـ`CancelCoreAsync` نَفسِها، لَكِنّ `GatewayInvoiceShare.Validate`
// يَسأَلُ «أَتَدفَعُ الفاتورَةُ لِهذِه الصَفقَة؟» بِـ`GatewayInvoiceClosing.Owed` — `Active` وَحدَها — فَصَفقَةٌ في «نِزاع» لا تُحسَبُ عَلَيها، فَلا رَدّ: تُلغى وَحدَها،
// ويَسأَلُ المُلغي البَوّابَةَ فَيَجِدُ الأُختَ قائِمَةً فَيُبقي الفاتورَةَ (`gateway_invoice_still_owed`) — قابِلَةً لِلدَّفعِ بِمَبلَغِها كامِلاً ومِنه المُلغاة —
// و`LeavesPayable` لا يَعُدُّ هذا الرَمز، فَلا سَطرَ «فاتورَةٌ لَم تُلغَ» ولا إشعارَ لِصاحِبِ المَتجَر. وسِجِلُّ المالِك «أ» S5·٤ قالَ العَكس.
//
// **ما يُقلَبُ بِاسمِه في كوميتِ الإصلاح**: الصَفُّ أَدناه.
//
// **وقُلِبَ بِاسمِه** (‏2026-10-29، مُراجَعَةُ تَيّارِ S5 الأولى؛ ‏ADR-047 §٢‑ب مُلحَقاً): بَوّابَةُ الحَسمِ تَسأَلُ `GatewayInvoiceShare.ValidateDisputed` — المُتَنازَعُ
// عَلَيها تُحسَبُ عَلى فاتورَتِها ما لَم يُكتَب دَفعُها — فَيُرَدُّ الإلغاءُ بِـ`deal_invoice_shared` قَبلَ أَيِّ كِتابَة، والصَفحَةُ لا تَرسُمُ بَديلَ الإلغاءِ وتُسَمّي
// الطَريقَ الَّذي يُفضي: الاستِئنافُ ثُمَّ إلغاءُ الطَلَبِ كُلِّه، أَو الإلغاءُ بَعدَ الدَفع — وكِلاهُما مَقيسٌ أَدناه.

public class DealDisputeSharedInvoiceTests(ITestOutputHelper output)
{
    private static readonly DealCanceller StoreOwner = new(Other, "مالِك التَّطبيق", IsStoreAdmin: true);

    private static Deal Load(World w, Guid id) => w.Docs.Read<Deal>(id.ToString())!;
    private static string[] Sent(MoyasarScriptedHandler h)
        => h.Requests.Select(r => $"{r.Method} {r.RequestUri!.AbsolutePath}").ToArray();

    /// <summary><b>قُلِبَ بِاسمِه</b> (كانَ <c>Today_a_dispute_cancel_of_one_unpaid_deal_on_an_open_shared_invoice_cancels_it_alone_and_leaves_the_invoice_payable_unannounced</c>،
    /// ‏<c>fcffb9b</c>): حَسمُ النِزاعِ بِإلغاءِ صَفقَةٍ لَم تُدفَع فاتورَتُها المَفتوحَةُ وأُختُها قائِمَةٌ يُرَدُّ بِـ<c>deal_invoice_shared</c> <b>قَبلَ أَيِّ كِتابَة</b> —
    /// الصَفقَتانِ كَما هُما، ولا حِفظَ ولا سُؤالَ لِلبَوّابَة.</summary>
    [Fact]
    public async Task A_dispute_cancel_of_one_unpaid_deal_on_an_open_shared_invoice_is_refused_before_any_write()
    {
        var (w, deals, _) = await GatewayInvoiceShareTests.OpenedTwoDealInvoice();
        Assert.True((await deals.DisputeAsync(Slug, w.Ids[1], Other, "مالِك التَّطبيق", "لَم تَصِل")).Ok);
        var disputed = Load(w, w.Ids[1]);
        Assert.True(GatewayInvoiceShare.IsValid(disputed, await deals.InvoiceSiblingsAsync(Slug, disputed)));
        Assert.False(GatewayInvoiceShare.IsValidDisputed(disputed, await deals.InvoiceSiblingsAsync(Slug, disputed)));
        var (saves, requests) = (w.Docs.SaveCalls, w.Moyasar.Requests.Count);

        var res = await deals.ResolveDisputeAsync(Slug, w.Ids[1], StoreOwner, DealDisputeResolution.Cancel, "حَسم");

        output.WriteLine($"الحَسم: {(res.Ok ? "ok" : res.Violation!.Code)} · {Load(w, w.Ids[0]).Status}/{Load(w, w.Ids[1]).Status} · " +
                         $"حِفظات: {w.Docs.SaveCalls - saves} · البَوّابَة: +{w.Moyasar.Requests.Count - requests}");
        Assert.False(res.Ok);
        Assert.Equal(GatewayInvoiceShare.Shared, res.Violation!.Code);
        Assert.Equal((DealStatus.Active, DealStatus.Disputed), (Load(w, w.Ids[0]).Status, Load(w, w.Ids[1]).Status));
        Assert.Equal(saves, w.Docs.SaveCalls);
        Assert.Equal(requests, w.Moyasar.Requests.Count);
        Assert.Contains(GatewayInvoiceShare.Shared, DealRefusalTexts.StudioCodes);
    }

    /// <summary><b>والطَريقُ الأَوَّلُ الَّذي تُسَمّيه الصَفحَةُ يُفضي</b>: الاستِئنافُ يُعيدُها <c>Active</c>، ثُمَّ إلغاءُ صاحِبِ المَتجَرِ الطَلَبَ كُلَّه (بابُ الاستوديو
    /// بِـ<c>scope=order</c>) يُلغي الصَفقَتَين ويُلغي الأَخيرَةُ الفاتورَةَ عِندَ البَوّابَة (‏PUT …/cancel).</summary>
    [Fact]
    public async Task Resuming_then_cancelling_the_whole_order_cancels_both_deals_and_closes_the_invoice()
    {
        var (w, deals, log) = await GatewayInvoiceShareTests.OpenedTwoDealInvoice();
        Assert.True((await deals.DisputeAsync(Slug, w.Ids[1], Other, "مالِك التَّطبيق", "لَم تَصِل")).Ok);
        Assert.Equal(GatewayInvoiceShare.Shared,
            (await deals.ResolveDisputeAsync(Slug, w.Ids[1], StoreOwner, DealDisputeResolution.Cancel, "حَسم")).Violation?.Code);
        Assert.True((await deals.ResolveDisputeAsync(Slug, w.Ids[1], StoreOwner, DealDisputeResolution.Resume, "يُلغى الطَلَب")).Ok);
        var named = GatewayPaymentProof.MetadataFor(w.Ids);
        w.Moyasar.Then(HttpStatusCode.OK, MoyasarClientTests.InvoiceJson("initiated", 15000, metadata: named))
                 .Then(HttpStatusCode.OK, MoyasarClientTests.InvoiceJson("initiated", 15000, metadata: named))
                 .Then(HttpStatusCode.OK, MoyasarClientTests.InvoiceJson("canceled", 15000, metadata: named));

        var order = await deals.CancelWholeInvoiceAsync(Slug, w.Ids[1], StoreOwner, "حَسمُ نِزاع");

        output.WriteLine($"الطَلَب: {(order.Result.Ok ? "ok" : order.Result.Violation!.Code)} · أُلغِيَ: {order.Cancelled.Count} · البَوّابَة: [{string.Join(" · ", Sent(w.Moyasar)[^3..])}]");
        Assert.True(order.Result.Ok, order.Result.Violation?.Code);
        Assert.All(w.Ids, id => Assert.Equal(DealStatus.Cancelled, Load(w, id).Status));
        Assert.EndsWith(MoyasarClient.CancelSuffix, Sent(w.Moyasar)[^1], StringComparison.Ordinal);
        Assert.Contains("cancelled at the gateway", log.Lines[^1], StringComparison.Ordinal);
    }

    /// <summary><b>والطَريقُ الثاني</b>: بَعدَ أَن يُكتَبَ دَفعُ الفاتورَةِ لا تَدفَعُ لِأَيٍّ مِنهُما — فَيُقبَلُ الحَسمُ بِإلغاءِ المُتَنازَعِ عَلَيها وَحدَها بِسَطرِ
    /// «استِرجاعُه مُستَحَقٌّ عِندَ البَوّابَة» القائِم، والأُختُ قائِمَة.</summary>
    [Fact]
    public async Task After_payment_the_disputed_deal_is_cancelled_alone_with_its_refund_due_line()
    {
        var (w, deals, _) = await GatewayInvoiceShareTests.OpenedTwoDealInvoice();
        foreach (var id in w.Ids)
        {
            await w.Deals.AttachRefAsync(Slug, id, "payment_id", InvoiceId);
            await w.Deals.AttachRefAsync(Slug, id, "payment_status", nameof(PaymentStatus.Captured));
        }
        Assert.True((await deals.DisputeAsync(Slug, w.Ids[1], Other, "مالِك التَّطبيق", "مَعيبَة")).Ok);

        var res = await deals.ResolveDisputeAsync(Slug, w.Ids[1], StoreOwner, DealDisputeResolution.Cancel, "حَسم");

        Assert.True(res.Ok, res.Violation?.Code);
        Assert.Equal((DealStatus.Active, DealStatus.Cancelled), (Load(w, w.Ids[0]).Status, Load(w, w.Ids[1]).Status));
        Assert.Single(Load(w, w.Ids[1]).Timeline, e => e.Action == DealEvent.GatewayRefundDue);
        Assert.Equal(DealNotices.OwnerAction.RefundDue, DealNotices.OwnerActionOf(Load(w, w.Ids[1])));
    }

    // ═══ البَوّابَةُ النَقِيَّة — مُوجِبٌ وسالِبٌ لِرَمزِها في طَريقِ الحَسم ═══════════════════════════════════════

    private static Deal OnInvoice(Guid id, DealStatus status, IReadOnlyList<Guid> listed, string invoice = InvoiceId) => new()
    {
        Id = id, TenantSlug = Slug, Status = status, AmountSar = 10m,
        Refs = new Dictionary<string, string>
        {
            [GatewayPaymentProof.InvoiceRef] = invoice,
            [GatewayPayments.InvoiceDealsRef] = GatewayInvoiceShare.ValueFor(listed),
        },
    };

    /// <summary><b>المُوجِب</b>: مُتَنازَعٌ عَلَيها لَم يُكتَب دَفعُها، وأُختٌ مُسَمّاةٌ عَلى الفاتورَةِ نَفسِها قائِمَةٌ لَم تُدفَع — والفاحِصُ العامُّ لا يَراها.</summary>
    [Fact]
    public void ValidateDisputed_fires_for_an_unpaid_disputed_deal_whose_open_invoice_still_pays_for_an_active_sibling()
    {
        var (a, b) = (Guid.NewGuid(), Guid.NewGuid());
        var (deal, sibling) = (OnInvoice(a, DealStatus.Disputed, new[] { a, b }), OnInvoice(b, DealStatus.Active, new[] { a, b }));
        Assert.Equal(GatewayInvoiceShare.Shared, Assert.Single(GatewayInvoiceShare.ValidateDisputed(deal, new[] { sibling })).Code);
        Assert.False(GatewayInvoiceShare.IsValidDisputed(deal, new[] { sibling }));
        Assert.True(GatewayInvoiceShare.IsValid(deal, new[] { sibling }));
    }

    /// <summary><b>السالِب</b>: كُلُّ شَرطٍ مَنقوصاً وَحدَه — لا يُرَدُّ الحَسم. ومِنه صَفقَةٌ قائِمَةٌ لا مُتَنازَعٌ عَلَيها: فاحِصُها <c>Validate</c> لا هذا.</summary>
    [Theory]
    [InlineData("deal_active")]
    [InlineData("deal_captured")]
    [InlineData("deal_without_invoice")]
    [InlineData("sibling_cancelled")]
    [InlineData("sibling_disputed")]
    [InlineData("sibling_captured")]
    [InlineData("sibling_on_another_invoice")]
    [InlineData("sibling_not_listed")]
    [InlineData("sibling_not_loaded")]
    [InlineData("no_list")]
    public void ValidateDisputed_never_fires_when_one_condition_is_missing(string why)
    {
        var (a, b) = (Guid.NewGuid(), Guid.NewGuid());
        var (deal, sibling) = (OnInvoice(a, DealStatus.Disputed, new[] { a, b }), OnInvoice(b, DealStatus.Active, new[] { a, b }));
        var siblings = new List<Deal> { sibling };
        switch (why)
        {
            case "deal_active": deal.Status = DealStatus.Active; break;
            case "deal_captured": deal.Refs["payment_status"] = nameof(PaymentStatus.Captured); break;
            case "deal_without_invoice": deal.Refs.Remove(GatewayPaymentProof.InvoiceRef); break;
            case "sibling_cancelled": sibling.Status = DealStatus.Cancelled; break;
            case "sibling_disputed": sibling.Status = DealStatus.Disputed; break;
            case "sibling_captured": sibling.Refs["payment_status"] = nameof(PaymentStatus.Captured); break;
            case "sibling_on_another_invoice": sibling.Refs[GatewayPaymentProof.InvoiceRef] = Guid.NewGuid().ToString(); break;
            case "sibling_not_listed": deal.Refs[GatewayPayments.InvoiceDealsRef] = GatewayInvoiceShare.ValueFor(new[] { a, Guid.NewGuid() }); break;
            case "sibling_not_loaded": siblings.Clear(); break;
            case "no_list": deal.Refs.Remove(GatewayPayments.InvoiceDealsRef); break;
        }
        Assert.Empty(GatewayInvoiceShare.ValidateDisputed(deal, siblings));
        Assert.True(GatewayInvoiceShare.IsValidDisputed(deal, siblings));
    }

    // ═══ الصَفحَةُ والكاتِبُ بِالدالَّةِ نَفسِها (القاعِدَة ١٢) ═══════════════════════════════════════════════════

    [Fact]
    public void The_studio_page_hides_the_cancel_outcome_through_the_writers_own_predicate_and_names_the_way_that_leads()
    {
        static string Code(params string[] parts) => WriteEndpointGuardTests.StripComments(WriteEndpointGuardTests.StripMarkupComments(File.ReadAllText(
            Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, "libs", "templates", "ACommerce.Templates.Customer.Marketplace", Path.Combine(parts)))));
        var page = Code("Components", "Pages", "StudioAppDealDetail.razor");
        Assert.Contains("if (deal is { Status: DealStatus.Disputed }) resolveShared = !GatewayInvoiceShare.IsValidDisputed(deal, await Deals.InvoiceSiblingsAsync(Slug, deal));",
            page, StringComparison.Ordinal);
        var block = page.IndexOf("@if (deal.Status == DealStatus.Disputed)", StringComparison.Ordinal);
        var guard = page.IndexOf("@if (!resolveShared)", block, StringComparison.Ordinal);
        var cancel = page.IndexOf("value=\"@DealDisputeResolution.Cancel\"", block, StringComparison.Ordinal);
        var hint = page.IndexOf("L.Markup(\"studio.deal.resolve_cancel_shared\")", block, StringComparison.Ordinal);
        Assert.True(block > 0 && guard > block && cancel > guard && hint > cancel, $"block={block} guard={guard} cancel={cancel} hint={hint}");
        Assert.Contains("@if (resolveShared)", page[cancel..hint], StringComparison.Ordinal);

        var service = Code("Services", "Deals", "DealsService.cs");
        Assert.Contains("(resolvingDispute ? GatewayInvoiceShare.ValidateDisputed(deal, siblings) : GatewayInvoiceShare.Validate(deal, siblings))", service, StringComparison.Ordinal);

        var text = LocaleCatalog.Text(LocaleCatalog.Arabic, "studio.deal.resolve_cancel_shared");
        Assert.NotEqual("studio.deal.resolve_cancel_shared", text);
        Assert.False(LocaleCatalog.IsPlaceholder(text));
        Assert.Contains("استَأنِفها", text, StringComparison.Ordinal);
    }
}
