using System.Net;
using System.Text.Json;
using ACommerce.Kit.Notifications;
using ACommerce.Platform.I18n;
using ACommerce.Templates.Customer.Marketplace.Services;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.Abstractions;
using static ACommerce.Platform.Tests.TenantGatewayEndpointsTests;

namespace ACommerce.Platform.Tests;

// ═══ صَفقَةٌ أُلغِيَت وفاتورَتُها مَفتوحَة — صَفحَةُ العَودَةِ وإشعارُ الدَفعِ وفَتحُ الفاتورَة (‏ADR-032 الخُطوَتان 9 و10) ═════════════
//
// **تَوصيفُ «اليَوم» — مُثَبَّتاً قَبلَ قَلبِه** (القاعِدَة ٣؛ ‏2026-10-12، مُراجَعَةُ تَيّارِ S9 الأولى — البَندُ المانِع):
// البائِعُ المَربوطُ عِندَ الوِلادَةِ يُشعَرُ بِالطَلَبِ في إتمامِ الشِراءِ قَبلَ أَيِّ دَفع، ويَرفُضُه عِندَ «عرض» بِبابٍ تَرسُمُه صَفحَتُه
// (`MyDealDetail.razor`)؛ والعَميلُ بِلا عَمَلِيَّةِ إبطالٍ (عَمَلِيَّتانِ لا ثالِثَة، §٨‑أ) فَالفاتورَةُ تَبقى قابِلَةً لِلدَّفع. فَثَلاثَةُ صُفوف:
//
// (١) صَفحَةُ العَودَةِ لِلمُلغاةِ الَّتي لَم تُدفَع تَرسُمُ زِرَّ التَحَقُّقِ ورابِطَ «أَكمِلِ الدَّفعَ عَلى صَفحَةِ البَوّابَة» ونَصَّ «طَلَبُك قائِمٌ» —
//     مُدخَلاتُها الثَلاثَةُ بِالدَوالِّ النَقِيَّةِ الَّتي تُناديها، وصِفرُ قِراءَةٍ لِلحالِ في كودِها؛
// (٢) دَفعٌ يُتَحَقَّقُ مِنه عَلى المُلغاةِ يُكتَبُ ومَعَه «استِرجاعُه مُستَحَقّ» — ثُمَّ يُخبَرُ بائِعُها «دَفَعَ المُشتَري ثَمَنَ طَلَبِه ✓ … افتَحِ
//     الصَّفقَة لِمُتابَعَتِها»، وتَقولُ صَفحَتُها «ثَبَتَ دَفعُك … يُتابِعُه البائِعُ مِن هُنا»؛
// (٣) صَفقَةٌ أَلغاها بائِعُها بَينَ وِلادَتِها وفَتحِ الفاتورَةِ تَدخُلُ الفاتورَةَ بِمَبلَغِها ويُكتَبُ عَلَيها رابِطُها.
//
// **ولا مِفتاحَ حَقيقِيّاً** — مُعالِجُ `MoyasarClientTests` بِرُدودِ الوَثيقَةِ وعالَمُ `TenantGatewayEndpointsTests` بِحَرفِه.

public class GatewayCancelledDealTests(ITestOutputHelper output)
{
    private static string Root => ThemeZeroEquivalenceTests.RepoRoot;
    private static string Page()
        => WriteEndpointGuardTests.StripComments(WriteEndpointGuardTests.StripMarkupComments(File.ReadAllText(Path.Combine(
            Root, "libs", "templates", "ACommerce.Templates.Customer.Marketplace", "Components", "Pages", "GatewayPayReturn.razor"))));

    private static Deal Load(World w, Guid id) => w.Docs.Read<Deal>(id.ToString())!;
    private static L Fresh() => new(new HttpContextAccessor());
    private static string Ar(string key) => LocaleCatalog.Text(LocaleCatalog.Arabic, key);

    /// <summary>رابِطُ الصَفحَةِ المُستَضافَةِ كَما يَكتُبُه إتمامُ الشِراء — بِشَكلِ مِثالِ الوَثيقَة.</summary>
    private const string PayUrl = "https://checkout.moyasar.com/invoices/efebc231-7795-4cfd-b69c-980fe4c02c49";
    private const string ReturnUrl = "https://store.example/zz-gateway/pay/return?deal=1";

    /// <summary>البائِعُ المَربوطُ عِندَ الوِلادَةِ يَرفُضُ الطَلَبَ عِندَ «عرض» — البابُ نَفسُه الَّذي تَرسُمُه صَفحَتُه، لا كِتابَةٌ بِاليَد.</summary>
    private static async Task VendorRejects(World w, Guid id)
    {
        var res = await w.Deals.CancelAsync(Slug, id, new DealCanceller(Seller, "بائِع", IsStoreAdmin: false), "نَفِدَت الكَمِّيَّة");
        Assert.True(res.Ok);
        Assert.Equal((DealStatus.Cancelled, DealStage.Offered), (Load(w, id).Status, Load(w, id).Stage));
    }

    private static DealNotices Notices(World w)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSignalR();
        var hub = services.BuildServiceProvider().GetRequiredService<IHubContext<ACommerce.Kit.Realtime.Server.RealtimeHub>>();
        return new DealNotices(w.Docs.Store, hub, new WebPushService(new ConfigurationBuilder().Build(), NullLogger<WebPushService>.Instance));
    }

    // ═══ (١) صَفحَةُ العَودَةِ لِلمُلغاةِ الَّتي لَم تُدفَع ═══════════════════════════════════════════════════

    [Fact]
    public async Task Today_the_return_page_of_a_cancelled_deal_whose_invoice_is_unpaid_draws_the_verify_button_the_pay_link_and_the_text_that_the_order_stands()
    {
        var w = await PaidCart(withSeller: true);
        var id = w.Ids[0];
        await w.Deals.AttachRefAsync(Slug, id, GatewayPayments.InvoiceUrlRef, PayUrl);
        await VendorRejects(w, id);
        var deal = Load(w, id);

        // مُدخَلاتُ الصَفحَةِ الثَلاثَةُ — بِالدَوالِّ الَّتي تُناديها بِحَرفِها:
        Assert.Null(DealPayAuthorization.Validate(deal, new DealPayer(Buyer)));   // ← يُرسَمُ زِرُّ «تَحَقَّق مِن الدَّفع»
        Assert.False(GatewayReturn.IsConfirmed(deal));                            // ← فَرعُ «لَم يَثبُت»
        Assert.Equal(PayUrl, GatewayReturn.PayUrlOf(deal));                       // ← يُرسَمُ «أَكمِلِ الدَّفعَ عَلى صَفحَةِ البَوّابَة»

        // وفَرعُ «لَم يَثبُت» يَرسُمُ نَصّاً واحِداً بِحَرفِه، ولا يَقرَأُ الكودُ حالَ الصَفقَةِ في أَيِّ مَوضِع:
        var page = Page();
        Assert.Contains("<p>@L.Markup(\"pay.return.pending\")</p>", page, StringComparison.Ordinal);
        Assert.Contains("@if (payUrl is not null)", page, StringComparison.Ordinal);
        Assert.DoesNotContain("DealStatus", page, StringComparison.Ordinal);
        Assert.DoesNotContain(".Status", page, StringComparison.Ordinal);
        Assert.Contains("طَلَبُك قائِمٌ في «صَفقاتي»", Ar("pay.return.pending"), StringComparison.Ordinal);
        output.WriteLine($"· مُلغاةٌ لَم تُدفَع: الزِّرّ=يُرسَم · الرابِط={GatewayReturn.PayUrlOf(deal)} · النَصّ=pay.return.pending");
    }

    // ═══ (٢) دَفعٌ يُتَحَقَّقُ مِنه عَلى المُلغاة ═════════════════════════════════════════════════════════

    [Fact]
    public async Task Today_a_payment_verified_on_a_cancelled_deal_is_announced_to_its_vendor_as_paid_and_its_return_page_says_the_vendor_follows_it()
    {
        var w = await PaidCart(withSeller: true);
        var cancelled = w.Ids[0];
        await VendorRejects(w, cancelled);
        w.Moyasar.Then(HttpStatusCode.OK, PaidInvoice(w));   // دَفَعَ المُشتَري الفاتورَةَ المُستَضافَةَ — وهي لا تُبطَل.

        var res = await w.Gateway.VerifyAsync(Slug, new DealPayer(Buyer), cancelled.ToString("N"));

        Assert.True(res.Ok, res.Code);
        Assert.Contains(cancelled, res.Confirmed);   // كُتِبَ الآن — ويُسَلَّمُ لِلإشعار.
        var deal = Load(w, cancelled);
        Assert.Equal(DealStatus.Cancelled, deal.Status);
        Assert.Single(deal.Timeline, e => e.Action == DealEvent.GatewayPaid);
        Assert.Single(deal.Timeline, e => e.Action == DealEvent.GatewayRefundDue);

        var told = await Notices(w).NotifyPaidAsync(Slug, res.Confirmed, Fresh());

        Assert.Equal(2, told);
        var paid = w.Docs.Stored.OfType<Notification>().Where(n => n.Type == DealNotices.PaidType).ToList();
        Assert.Contains(paid, n => n.UserId == Seller && n.RelatedUrl == $"/{Slug}/deals/{cancelled}");
        Assert.Contains("✓", Ar("notifications.deal_paid.title"), StringComparison.Ordinal);
        Assert.Contains("افتَحِ الصَّفقَة لِمُتابَعَتِها", Ar("notifications.deal_paid.body"), StringComparison.Ordinal);

        // وصَفحَةُ عَودَتِها: فَرعُ «ثَبَتَ» بِنَصِّه الواحِد.
        Assert.True(GatewayReturn.IsConfirmed(deal));
        Assert.Contains("<p>@L.Markup(\"pay.return.confirmed\")</p>", Page(), StringComparison.Ordinal);
        Assert.Contains("يُتابِعُه البائِعُ مِن هُنا", Ar("pay.return.confirmed"), StringComparison.Ordinal);
        output.WriteLine($"· تَحَقُّقٌ عَلى مُلغاة: confirmed={res.Confirmed.Count} · أُشعِرَ={told} · deal_paid لِلمُلغاة=نَعَم");
    }

    // ═══ (٣) الفاتورَةُ تُفتَحُ عَلى صَفقَةٍ أُلغِيَت قَبلَ فَتحِها ═══════════════════════════════════════════════

    [Fact]
    public async Task Today_an_invoice_opens_over_a_deal_cancelled_between_its_birth_and_the_opening_and_writes_its_pay_link_on_it()
    {
        var w = await PaidCart(withSeller: true, invoice: false);
        var cancelled = w.Ids[0];
        await VendorRejects(w, cancelled);
        w.Moyasar.Then(HttpStatusCode.Created, MoyasarClientTests.InvoiceJson("initiated", 15000, metadata: new Dictionary<string, string>()))
                 .Then(HttpStatusCode.OK, MoyasarClientTests.InvoiceJson("initiated", 15000, metadata: GatewayPaymentProof.MetadataFor(w.Ids)));

        var opened = await w.Gateway.OpenInvoiceAsync(Slug, new DealPayer(Buyer), w.Ids, "مَتجَر", ReturnUrl, Fresh());

        Assert.True(opened.Ok, opened.Code);
        Assert.Equal(2, w.Moyasar.Requests.Count);
        using var created = JsonDocument.Parse(w.Moyasar.Bodies[0]!);
        Assert.Equal(15000, created.RootElement.GetProperty("amount").GetInt64());   // مِئَةُ المُلغاةِ في المَجموع.
        var deal = Load(w, cancelled);
        Assert.Equal(DealStatus.Cancelled, deal.Status);
        Assert.Equal((InvoiceId, opened.Url), (deal.Refs[GatewayPaymentProof.InvoiceRef], deal.Refs[GatewayPayments.InvoiceUrlRef]));
        Assert.Empty(w.Platform.Calls);
        output.WriteLine($"· فاتورَةٌ فُتِحَت عَلى مُلغاة: amount={created.RootElement.GetProperty("amount").GetInt64()} · رابِطُها عَلَيها=نَعَم");
    }
}
