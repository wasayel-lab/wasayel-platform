using System.Net;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using Xunit;
using Xunit.Abstractions;
using static ACommerce.Platform.Tests.TenantGatewayEndpointsTests;

namespace ACommerce.Platform.Tests;

// ═══ فاتورَةٌ أُغلِقَت عِندَ البَوّابَة — رَمزُ التَحَقُّقِ ورابِطُ «أَكمِلِ الدَّفعَ» بَعدَه — تَوصيفُ «اليَوم» ═══════════════════════════════
//
// مُراجَعَةُ بَوّابَةِ المَوجَة 3b الثانِيَة (‏2026-10-13، G5 (xvii-g)): `GatewayPaymentProof` يُطلِقُ `gateway_invoice_unpaid` لِكُلِّ حالٍ سِوى «paid» —
// `initiated` و`failed` و`on_hold` (تُدفَعُ بَعد) و`canceled` و`expired` و`voided` و`refunded` (أُغلِقَت) سَواء — وصَفحَةُ العَودَةِ تَبقى تَرسُمُ
// «أَكمِلِ الدَّفعَ عَلى صَفحَةِ البَوّابَة» لِفاتورَةٍ مَيِّتَةٍ ولو رَأى التَحَقُّقُ إغلاقَها.
//
// **ما يُقلَبُ بِاسمِه في كوميتِ الإصلاح**: صَفُّ «Today_» أَدناه، وصَفُّ `GatewayPaymentProofTests.InvoiceUnpaid_fires_alone_for_every_status_but_paid`.
//
// **قُلِبا** (‏2026-10-13، كوميتُ «fix(payments)» بَعدَ ‏5c777d7): `gateway_invoice_closed` لِلمُغلَقَةِ الأَربَعِ بِنَصٍّ خاصّ، والتَحَقُّقُ يَكتُبُ حالَها
// عَلى صَفَقاتِها مَرَّةً (`gateway_invoice_closed`) فَلا يُرسَمُ رابِطُ «أَكمِلِ الدَّفع» بَعدَه، وتَقولُ صَفحَةُ العَودَةِ رَمزَها بِلا `?err=`.

public class GatewayClosedInvoiceTests(ITestOutputHelper output)
{
    private const string PayUrl = "https://checkout.moyasar.com/invoices/efebc231-7795-4cfd-b69c-980fe4c02c49";

    /// <summary><b>قُلِبَ</b> (‏2026-10-13) — كانَ <c>Today_a_verify_that_sees_a_canceled_invoice_answers_unpaid_and_the_pay_link_stays</c> (‏<c>5c777d7</c>):
    /// تَحَقُّقٌ يَرى فاتورَةً مُغلَقَةً يُجيبُ بِرَمزِها، ويَكتُبُ حالَها عَلى صَفَقاتِها مَرَّةً، فَلا رابِطَ بَعدَه — والتَحَقُّقُ الثاني لا يَكتُبُ ثانِيَة.</summary>
    /// <remarks>(‏2026-10-29، قَرارُ المالِك د5 (٤) «ب» — ولا يُعادُ حَرفٌ فَوقَه: صَفُّ <c>expired</c> خَرَجَ مِن هذِه النَظَرِيَّة — المُنتَهِيَةُ لَم تَعُد «تُجيبُ مُغلَقَةً
    /// والصَفقَةُ قائِمَة»: يُلغى طَلَبُها كُلُّه بِاسمِ المَنصَّةِ ويُجيبُ <c>gateway_invoice_expired</c> — يَقيسُه <c>GatewayInvoiceExpiryTests</c>
    /// و<c>TenantGatewayEndpointsTests.An_expired_invoice_seen_at_verify_cancels_the_whole_order_in_the_platform_name</c> (مَرجِعُ الإغلاقِ مَكتوبٌ فيه كَما هُنا).
    /// والثَلاثُ الباقِيَةُ بِحَرفِها.)</remarks>
    [Theory]
    [InlineData("canceled")]
    [InlineData("voided")]
    [InlineData("refunded")]
    public async Task A_verify_that_sees_a_closed_invoice_answers_closed_records_it_once_and_the_pay_link_goes(string status)
    {
        var w = await PaidCart();
        foreach (var id in w.Ids) await w.Deals.AttachRefAsync(Slug, id, GatewayPayments.InvoiceUrlRef, PayUrl);
        w.Moyasar.Then(HttpStatusCode.OK, PaidInvoice(w, status)).Then(HttpStatusCode.OK, PaidInvoice(w, status));

        var res = await w.Gateway.VerifyAsync(Slug, new DealPayer(Buyer), w.Ids[0].ToString("N"));
        var saves = w.Docs.SaveCalls;
        var again = await w.Gateway.VerifyAsync(Slug, new DealPayer(Buyer), w.Ids[0].ToString("N"));
        var (deal, sibling) = (w.Docs.Read<Deal>(w.Ids[0].ToString())!, w.Docs.Read<Deal>(w.Ids[1].ToString())!);
        output.WriteLine($"{status} ⇒ الرَمز: {res.Code} · المُسَجَّل: {deal.Refs.GetValueOrDefault(GatewayPayments.InvoiceClosedRef)} · الرابِط: {GatewayReturn.PayUrlOf(deal) ?? "لا"} · " +
                         $"الثاني: {again.Code} · حِفظاتُه: {w.Docs.SaveCalls - saves}");

        Assert.Equal(GatewayPaymentProof.InvoiceClosed, res.Code);
        Assert.Equal(GatewayPaymentProof.InvoiceClosed, again.Code);
        Assert.Equal(status, deal.Refs[GatewayPayments.InvoiceClosedRef]);
        Assert.Equal(status, sibling.Refs[GatewayPayments.InvoiceClosedRef]);
        Assert.True(GatewayReturn.IsClosed(deal));
        Assert.Null(GatewayReturn.PayUrlOf(deal));
        Assert.Null(GatewayReturn.PayUrlOf(sibling));
        Assert.Equal(0, w.Docs.SaveCalls - saves);
        Assert.Equal(DealStatus.Active, deal.Status);
    }

    /// <summary>السالِب: فاتورَةٌ تُدفَعُ بَعدُ — <c>initiated</c> و<c>failed</c> و<c>on_hold</c> — تُجيبُ «لَم يُدفَع» بِلا كِتابَةٍ والرابِطُ باقٍ.</summary>
    [Theory]
    [InlineData("initiated")]
    [InlineData("failed")]
    [InlineData("on_hold")]
    public async Task A_verify_that_sees_an_open_invoice_answers_unpaid_writes_nothing_and_keeps_the_link(string status)
    {
        var w = await PaidCart();
        foreach (var id in w.Ids) await w.Deals.AttachRefAsync(Slug, id, GatewayPayments.InvoiceUrlRef, PayUrl);
        var saves = w.Docs.SaveCalls;
        w.Moyasar.Then(HttpStatusCode.OK, PaidInvoice(w, status));

        var res = await w.Gateway.VerifyAsync(Slug, new DealPayer(Buyer), w.Ids[0].ToString("N"));
        var deal = w.Docs.Read<Deal>(w.Ids[0].ToString())!;

        Assert.Equal(GatewayPaymentProof.InvoiceUnpaid, res.Code);
        Assert.Equal(0, w.Docs.SaveCalls - saves);
        Assert.False(GatewayReturn.IsClosed(deal));
        Assert.Equal(PayUrl, GatewayReturn.PayUrlOf(deal));
    }

    /// <summary>الصَفحَةُ تَقولُ رَمزَ المُغلَقَةِ بِلا <c>?err=</c> بِالدالَّةِ نَفسِها، والسِجِلُّ يُسَمّيه لِلطَرَفَين، والنَصّانِ حَيّانِ ومُختَلِفان.</summary>
    [Fact]
    public void The_return_page_says_closed_without_err_and_the_two_texts_differ()
    {
        var page = WriteEndpointGuardTests.StripComments(WriteEndpointGuardTests.StripMarkupComments(File.ReadAllText(Path.Combine(
            ThemeZeroEquivalenceTests.RepoRoot, "libs", "templates", "ACommerce.Templates.Customer.Marketplace", "Components", "Pages", "GatewayPayReturn.razor"))));
        Assert.Contains("@if (GatewayReturn.KeyFor(Err) is { } errKey)", page, StringComparison.Ordinal);
        Assert.Contains("else if (GatewayReturn.KeyFor(closedCode) is { } closedKey)", page, StringComparison.Ordinal);
        Assert.Contains("closedCode = GatewayReturn.IsClosed(deal) ? GatewayPaymentProof.InvoiceClosed : null;", page, StringComparison.Ordinal);

        var closed = ACommerce.Platform.I18n.LocaleCatalog.Text(ACommerce.Platform.I18n.LocaleCatalog.Arabic, "pay.return.err_gateway_invoice_closed");
        var unpaid = ACommerce.Platform.I18n.LocaleCatalog.Text(ACommerce.Platform.I18n.LocaleCatalog.Arabic, "pay.return.err_gateway_invoice_unpaid");
        Assert.NotEqual("pay.return.err_gateway_invoice_closed", closed);
        Assert.NotEqual(unpaid, closed);
        Assert.Equal("deals.detail.ref_gateway_invoice_closed", DealRefLabels.Labels[GatewayPayments.InvoiceClosedRef]);
        Assert.NotEqual("deals.detail.ref_gateway_invoice_closed",
            ACommerce.Platform.I18n.LocaleCatalog.Text(ACommerce.Platform.I18n.LocaleCatalog.Arabic, "deals.detail.ref_gateway_invoice_closed"));
    }
}

// ═══ بابُ «سَجِّل الدُّخول» في صَفحَةِ العَودَةِ يُضَيِّعُ الصَفقَةَ — تَوصيفُ «اليَوم» (‏2026-10-13، G5 (xvii-g)) ═══════════════════════
//
// مُشتَرٍ انتَهَت جَلسَتُه يَعودُ مِن البَوّابَةِ إلى `/{slug}/pay/return?deal={N}` فَيَرى «سَجِّل الدُّخولَ لِتَأكيدِ دَفعِ طَلَبِك» — وبابُه `login`
// بِلا `returnUrl` (جيرانُه كُلُّهُم يُمَرِّرونَه: `CartPage` و`MyDeals` و`GatedPage`)، فَيَعودُ بَعدَ الدُخولِ إلى بِدايَةِ المَتجَرِ و`?deal=` ضاع.
// **ما يُقلَبُ بِاسمِه في كوميتِ الإصلاح**: الصَفُّ أَدناه.
// **قُلِبَ** (‏2026-10-13، كوميتُ «fix(payments)» بَعدَ ‏d066a26): البابُ `GatewayReturn.LoginPathFor(Slug, DealRef)` — الوِجهَةُ مُرَمَّزَةً بِالصَفقَةِ المَقروءَة.

public class GatewayReturnLoginDoorTests(ITestOutputHelper output)
{
    private static string Page() => WriteEndpointGuardTests.StripComments(WriteEndpointGuardTests.StripMarkupComments(File.ReadAllText(Path.Combine(
        ThemeZeroEquivalenceTests.RepoRoot, "libs", "templates", "ACommerce.Templates.Customer.Marketplace", "Components", "Pages", "GatewayPayReturn.razor"))));

    /// <summary><b>قُلِبَ</b> (‏2026-10-13) — كانَ <c>Today_the_return_pages_login_door_carries_no_return_url</c> (‏<c>d066a26</c>): البابُ يَحمِلُ وِجهَتَه.</summary>
    [Fact]
    public void The_return_pages_login_door_carries_the_return_url_with_the_deal()
    {
        var page = Page();
        var door = page[page.IndexOf("@if (!Auth.IsAuthenticated)", StringComparison.Ordinal)..page.IndexOf("else if (refusalKey is not null)", StringComparison.Ordinal)];
        output.WriteLine(door.Trim());
        Assert.Contains("ActionHref=\"@AuthSession.LinkFor(Slug, Role, GatewayReturn.LoginPathFor(Slug, DealRef))\"", door, StringComparison.Ordinal);
        Assert.DoesNotContain("\"login\")", door, StringComparison.Ordinal);
    }

    /// <summary>المُوجِبُ والسالِب: صَفقَةٌ مَقروءَةٌ ⇒ وِجهَتُها بِها، مُرَمَّزَةً، ومَحَلِّيَّةً بَعدَ فَكِّها؛ وقيمَةٌ لا تُقرَأُ صَفقَةً — فارِغَةٌ أَو مَصنوعَةٌ لِلتَحويلِ
    /// خارِجاً — ⇒ صَفحَةُ العَودَةِ بِلا صَفقَة، لا أَكثَر.</summary>
    [Theory]
    [InlineData("11111111222243338444555555555555", "/zz/pay/return?deal=11111111222243338444555555555555")]
    [InlineData("11111111222243338444555555555555?id=7&status=paid", "/zz/pay/return?deal=11111111222243338444555555555555")]
    [InlineData(null, "/zz/pay/return")]
    [InlineData("//evil.example/x", "/zz/pay/return")]
    [InlineData("javascript:alert(1)", "/zz/pay/return")]
    public void The_login_return_url_is_the_return_page_with_the_read_deal_only(string? dealRef, string expected)
    {
        var path = GatewayReturn.LoginPathFor("zz", dealRef);
        Assert.StartsWith("login?returnUrl=", path, StringComparison.Ordinal);
        var encoded = path["login?returnUrl=".Length..];
        Assert.DoesNotContain("?", encoded, StringComparison.Ordinal);
        Assert.DoesNotContain("&", encoded, StringComparison.Ordinal);
        var decoded = Uri.UnescapeDataString(encoded);
        Assert.Equal(expected, decoded);
        Assert.True(ACommerce.Templates.Customer.Marketplace.Services.LocalRedirect.IsLocal(decoded));
    }
}
