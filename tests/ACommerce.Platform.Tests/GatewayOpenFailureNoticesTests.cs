using ACommerce.Platform.I18n;
using ACommerce.Templates.Customer.Marketplace.Services;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using Microsoft.AspNetCore.Http;
using Xunit;
using Xunit.Abstractions;
using static ACommerce.Platform.Tests.TenantGatewayEndpointsTests;

namespace ACommerce.Platform.Tests;

// ═══ طَلَبٌ لَم تُفتَح فاتورَتُه — لا يُشعَرُ بائِعُه بِوِلادَتِه ولا بِإلغائِه: قَرارٌ يُثَبَّت ═══════════════════════════════════════════
//
// مُراجَعَةُ بَوّابَةِ المَوجَة 3b (‏2026-10-13، G5 (xv-g)): حينَ لا تُفتَحُ فاتورَةُ البَوّابَةِ عِندَ إتمامِ الشِراءِ تُلغى الصَفَقاتُ المَربوطَةُ بِبائِعيها
// بِاسمِ المُشتَري، ولا يُشعَرُ البائِعونَ لا بِالوِلادَةِ ولا بِالإلغاء — «اجعَل الإشعاراتِ مُتَّسِقَة: أَشعِر بِالإلغاءِ، أَو أَجِّل إشعارَ الوِلادَةِ
// حَتّى تُفتَحَ الفاتورَة».
//
// **المَقيسُ قَبلَ هذا الكوميت**: إشعارُ الوِلادَةِ مُؤَجَّلٌ مُنذُ وُلِدَ الطَريق (‏`79a675e`: الرُجوعُ عِندَ فَشَلِ الفَتحِ قَبلَ `NotifyNewDealsAsync`)
// — والخِيارُ الثاني قائِمٌ بِحَرفِه — لكِن تَرتيبَ أَسطُرٍ لا يَحرُسُه صَفّ: صَفُّ `DealNoticesTests` يَشتَرِطُ أَن يَتلو الإشعارُ الحِفظ، ولا يَسأَلُ
// عَن الرُجوعِ قَبلَه. فَلَو تَقَدَّمَ سَطرُ الإشعارِ لَسَمِعَ البائِعُ بِوِلادَةٍ ثُمَّ أُلغِيَت صامِتَة.
//
// **القَرار (تَفويضُ المالِك؛ «أ» S11b·٢٣)**: الخِيارُ الثاني — لِأَنَّه قاعِدَةُ الأُنبوبِ كُلِّه لا استِثناءٌ هُنا: كُلُّ إشعارِ إلغاءٍ في `DealNotices`
// يُخاطِبُ المُشتَري وَحدَه، وإلغاءُ المُشتَري لا يُخبِرُ أَحَداً (`TellsTheBuyer`) — فَإشعارُ بائِعٍ بِإلغاءٍ هُنا وَحدَه هُوَ غَيرُ المُتَّسِق؛ وبَوّابَةٌ
// مُعَطَّلَةٌ كانَت سَتُرسِلُ لِكُلِّ بائِعٍ إشعارَ إلغاءٍ عَن كُلِّ مُحاوَلَةٍ لِطَلَبٍ لَم يَسمَع بِه. والصَفقَةُ المُلغاةُ في قائِمَتِه تَحمِلُ سَبَبَها.
// والصُفوفُ أَدناه تَجعَلُ القَرارَ يَحمَرُّ إن انكَسَر.
//
// **(‏2026-10-29، قَرارُ المالِك د8 «أ» — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه)**: «كُلُّ إشعارِ إلغاءٍ في `DealNotices` يُخاطِبُ المُشتَري وَحدَه» لَم يَعُد يَصدُق —
// صارَ لِلبائِعِ إشعارُ إلغاءٍ واحِدٌ تَمُرُّ بِه مَساراتُ الإلغاءِ كُلُّها ما لَم يَكُن هُوَ المُلغي. والقَرارُ نَفسُه أَبقى هذا الفَرعَ: «ويُستَثنى طَلَبٌ لَم يُخبَر بِه
// البائِعُ أَصلاً (فاتورَةٌ لَم تُفتَح)» — فَالإلغاءُ هُنا في خِدمَةِ البَوّابَةِ ولا يَمُرُّ بِالإشعار، والصَفُّ الثالِثُ قُلِبَ بِاسمِه عَلى القاعِدَةِ الجَديدَة.

public class GatewayOpenFailureNoticesTests(ITestOutputHelper output)
{
    private static string RepoRoot => ThemeZeroEquivalenceTests.RepoRoot;
    private const string Template = "libs/templates/ACommerce.Templates.Customer.Marketplace";

    private static string Source(params string[] parts)
    {
        var path = Path.Combine(RepoRoot, Path.Combine(parts));
        Assert.True(File.Exists(path), $"مَصدَرٌ مَفقود: {path} — الأَداةُ عَمياء.");
        var text = File.ReadAllText(path);
        Assert.True(text.Length > 500, $"أَداةٌ عَمياء: {path} لَم يُقرَأ.");
        return text;
    }

    private static int Count(string text, string needle)
    {
        var n = 0;
        for (var i = text.IndexOf(needle, StringComparison.Ordinal); i >= 0;
             i = text.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
            n++;
        return n;
    }

    /// <summary><b>الرُجوعُ قَبلَ إشعارِ الوِلادَة، وفَرعُه لا يُشعِرُ أَحَداً</b>: في جِسمِ <c>/checkout/submit</c> (بِلا تَعليقات) نِداءُ الفَتحِ، ثُمَّ رُجوعُ
    /// الفَشَل، ثُمَّ إشعارُ الوِلادَةِ الوَحيد — ولا نِداءَ <c>notices.</c> سِواه في الجِسم.</summary>
    [Fact]
    public void The_failed_open_returns_before_the_birth_notice_and_no_other_notice_is_called_in_checkout()
    {
        var endpoints = Source(Template, "MarketplaceTemplateExtensions.cs");
        var start = endpoints.IndexOf("\"/{slug}/checkout/submit\"", StringComparison.Ordinal);
        Assert.True(start > 0, "أَداةٌ عَمياء: النُقطَةُ غَيرُ مَوجودَة.");
        var end = endpoints.IndexOf(".RequireStoreWritable();", start, StringComparison.Ordinal);
        var body = WriteEndpointGuardTests.StripComments(endpoints[start..end]);

        var open   = body.IndexOf("await gateway.OpenInvoiceAsync(", StringComparison.Ordinal);
        var fail   = body.IndexOf("if (opened is { Ok: false }) return Results.Redirect(", StringComparison.Ordinal);
        var notify = body.IndexOf("await notices.NotifyNewDealsAsync(slug, born, l);", StringComparison.Ordinal);
        output.WriteLine($"الفَتح={open} · رُجوعُ الفَشَل={fail} · إشعارُ الوِلادَة={notify} · نِداءاتُ notices.: {Count(body, "notices.")}");

        Assert.True(open > 0 && fail > open && notify > fail, $"التَرتيب: {open}/{fail}/{notify}");
        Assert.Equal(1, Count(body, "notices."));
    }

    /// <summary><b>الإلغاءُ بَعدَ فَشَلِ الفَتحِ بِاسمِ المُشتَري وبِسَبَبِه</b> — فَقاعِدَةُ الأُنبوبِ لا تُخبِرُ أَحَداً (<see cref="DealNotices.TellsTheBuyer"/>
    /// كاذِبَة)؛ <b>والسُكوتُ قَرارٌ لا غِياب</b>: البائِعُ مُستَلِمٌ لِإشعارِ الوِلادَةِ لَو أُرسِل (<see cref="DealNotices.NewDealRecipient"/>)، والصَفقَةُ
    /// المُلغاةُ في قائِمَتِه تَحمِلُ نَصَّ سَبَبِها مِن القامُوس.</summary>
    [Fact]
    public async Task The_open_failure_cancels_in_the_buyers_name_with_its_reason_and_the_vendor_is_a_would_be_recipient()
    {
        var w = await PaidCart(withSeller: true, invoice: false, bornForGateway: true);
        var log = new LevelCapturingGatewayLogger();
        var gateway = new GatewayPayments(w.Deals, new TenantProviderService(w.Docs.Store), NewVault(), client: null, log);
        var l = new L(new HttpContextAccessor());

        var opened = await gateway.OpenInvoiceAsync(Slug, new DealPayer(Buyer), w.Ids, "مَتجَر",
            "https://store.example/zz-gateway/pay/return?deal=1", l);
        Assert.False(opened.Ok);

        var reason = l["checkout.payment.cancel_gateway_open_failed"];
        Assert.NotEqual("checkout.payment.cancel_gateway_open_failed", reason);
        var checkedDeals = 0;
        foreach (var id in w.Ids)
        {
            var deal = w.Docs.Read<Deal>(id.ToString())!;
            output.WriteLine($"{id:N}: {deal.Status} · السَبَب: «{deal.CancelReason}» · المُلغي: {deal.Timeline[^1].ActorId} · مُستَلِمُ الوِلادَة: {DealNotices.NewDealRecipient(deal)}");
            Assert.Equal(DealStatus.Cancelled, deal.Status);
            Assert.Equal(reason, deal.CancelReason);
            Assert.Equal(Buyer, deal.Timeline[^1].ActorId);
            Assert.False(DealNotices.TellsTheBuyer(deal, Buyer));
            Assert.Equal(Seller, DealNotices.NewDealRecipient(deal));
            checkedDeals++;
        }
        Assert.Equal(2, checkedDeals);
    }

    /// <summary><b>قاعِدَةُ الأُنبوبِ الَّتي يَتَّسِقُ بِها القَرار</b>: كُلُّ بانٍ لِإشعارِ إلغاءٍ في <c>DealNotices</c> يُخاطِبُ المُشتَري (<c>UserId = deal.InitiatorId</c>)
    /// — فَإن وُلِدَ يَوماً إشعارُ إلغاءٍ لِبائِعٍ أَحمَرَّ هذا الصَفُّ، فَيُعادُ النَظَرُ في فَرعِ الفَشَلِ مَعَه. ولا تُشعِرُ خِدمَةُ البَوّابَةِ أَحَداً: الإشعارُ
    /// في النُقطَة. والعَدّادُ يُطبَعُ ويَفشَلُ صِفراً.</summary>
    /// <remarks><b>قُلِبَ بِاسمِه وأُعيدَ فيه النَظَر</b> (‏2026-10-29، قَرارُ المالِك د8 «أ» — «ويُعادُ النَظَرُ فيه حينَ يَحمَرُّ اختِبارُ GatewayOpenFailureNoticesTests»؛
    /// كانَ <c>Every_cancel_notice_in_the_pipe_addresses_the_buyer_and_the_gateway_service_notifies_nobody</c>): وُلِدَ إشعارُ إلغاءٍ لِلبائِعِ فَاحمَرَّ — والقَرارُ
    /// نَفسُه يَستَثني «طَلَباً لَم يُخبَر بِه البائِعُ أَصلاً (فاتورَةٌ لَم تُفتَح)». فَصارَت القاعِدَة: بُناةُ المُشتَري يُخاطِبونَه، وبانٍ واحِدٌ لِلبائِعِ
    /// (<c>CancelledToVendorFor</c>) يُخاطِبُ مَن سَمَّته <c>VendorToTell</c>، <b>ولا تُشعِرُ خِدمَةُ البَوّابَةِ أَحَداً</b> — فَإلغاءُ فَشَلِ الفَتحِ يَبقى صامِتاً لِلبائِعِ كَما قُرِّر.</remarks>
    [Fact]
    public void Cancel_notices_address_the_buyer_or_the_named_vendor_and_the_gateway_service_notifies_nobody()
    {
        var notices = WriteEndpointGuardTests.StripComments(Source(Template, "Services", "Deals", "DealNotices.cs"));
        var (buyers, vendors) = (0, 0);
        for (var i = notices.IndexOf("public static Notification Cancelled", StringComparison.Ordinal); i >= 0;
             i = notices.IndexOf("public static Notification Cancelled", i + 1, StringComparison.Ordinal))
        {
            var close = notices.IndexOf("};", i, StringComparison.Ordinal);
            var builder = notices[i..close];
            output.WriteLine(builder[..builder.IndexOf('(')]);
            if (builder.StartsWith("public static Notification CancelledToVendorFor(", StringComparison.Ordinal))
            {
                Assert.Contains("UserId = vendorId,", builder, StringComparison.Ordinal);
                vendors++;
            }
            else
            {
                Assert.Contains("UserId = deal.InitiatorId,", builder, StringComparison.Ordinal);
                buyers++;
            }
        }
        output.WriteLine($"بُناةُ المُشتَري: {buyers} · بُناةُ البائِع: {vendors}");
        Assert.True(buyers >= 2, $"أَداةٌ عَمياء: {buyers} بانٍ لِإشعارِ إلغاءِ المُشتَري.");
        Assert.Equal(1, vendors);
        Assert.Contains("if (VendorToTell(deal, cancellerId) is not { } vendorId) return false;", notices, StringComparison.Ordinal);

        var gateway = WriteEndpointGuardTests.StripComments(Source(Template, "Services", "Deals", "GatewayPayments.cs"));
        Assert.DoesNotContain("DealNotices", gateway, StringComparison.Ordinal);
        Assert.DoesNotContain("Notification", gateway, StringComparison.Ordinal);
    }
}
