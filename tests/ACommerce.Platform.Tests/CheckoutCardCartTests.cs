using System.Net;
using ACommerce.Kit.Payments;
using ACommerce.Platform.I18n;
using ACommerce.Templates.Customer.Marketplace.Services;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.Abstractions;
using static ACommerce.Platform.Tests.TenantGatewayEndpointsTests;

namespace ACommerce.Platform.Tests;

// ═══ بِطاقَةُ بَوّابَةِ المَتجَرِ وسَلَّةٌ لا تَحمِلُها فاتورَةٌ واحِدَة — تَوصيفُ «اليَوم» ═══════════════════════════════════════
//
// مُراجَعَةُ بَوّابَةِ المَوجَة 3b (‏2026-10-13، G5 (xvi-g)): `CheckoutCardFacts` لا تَسأَلُ عَن السَلَّة — والفاتورَةُ الواحِدَةُ تَحمِلُ مِن صَفقَةٍ
// إلى ‏30 (`GatewayPaymentProof.FitsOneInvoice`، حَدُّ مَفاتيحِ `metadata` في الوَثيقَة) ومَبلَغاً بِهَلَلاتٍ صَحيحَةٍ لا يَقِلُّ عَن ‏100
// (`MoyasarClient.ToSmallestUnit`). فَتُرسَمُ البِطاقَةُ لِسَلَّةٍ لا تُفَوتَرُ أَبَداً، وكُلُّ إرسالٍ يُولِدُ صَفَقاتٍ مَربوطَةً بِبائِعيها ثُمَّ
// يُلغيها `OpenInvoiceAsync` بِـ`gateway_invalid` — وبائِعوها لا يُخبَرونَ بِوِلادَةٍ ولا إلغاء.
//
// **ما يُقلَبُ بِاسمِه في كوميتِ الإصلاح**: صَفُّ «Today_» أَدناه. وصَفُّ الخِدمَةِ يَبقى بِحَرفِه: دِفاعُها الأَخيرُ قائِم.

public class CheckoutCardCartTests(ITestOutputHelper output)
{
    private const string ReturnUrl = "https://store.example/zz-gateway/pay/return?deal=1";
    private static readonly CheckoutCardFacts Gateway = new(StoreCollects: true, SealedKeyBound: true, VaultCanUnseal: true,
        GatewayClientRegistered: true, InstanceCanCharge: false);

    [Fact]
    public void Today_a_gateway_card_is_offered_whatever_the_carts_size_or_total()
    {
        Assert.False(GatewayPaymentProof.FitsOneInvoice(GatewayPaymentProof.MaxDealsPerInvoice + 1));
        Assert.Null(MoyasarClient.ToSmallestUnit(0.5m));
        Assert.Null(MoyasarClient.ToSmallestUnit(10.005m));
        output.WriteLine($"الحَدّ: {GatewayPaymentProof.MaxDealsPerInvoice} صَفقَة · أَدنى مَبلَغ: {MoyasarClient.MinimumAmount} هَلَلَة · " +
                         $"البِطاقَة: {Gateway.CardOffered} · Decide: {CheckoutPaymentPolicy.Decide("card", Gateway.Collects, Gateway.ChargerCanCharge)}");
        Assert.True(Gateway.CardOffered);
        Assert.Equal((CheckoutPayMethods.Card, (string?)null), CheckoutPaymentPolicy.Decide("card", Gateway.Collects, Gateway.ChargerCanCharge));
    }

    /// <summary><b>دِفاعُ الخِدمَةِ الأَخيرُ</b> — ثابِتٌ قَبلَ الإصلاحِ وبَعدَه: سَلَّةٌ أَطوَلُ مِن فاتورَةٍ واحِدَةٍ أَو بِمَجموعٍ لا تَقبَلُه البَوّابَةُ لا تَبلُغُ
    /// البَوّابَةَ بِنِداء، وتُلغى صَفَقاتُها بِـ<see cref="MoyasarFailures.Invalid"/> ولا يُخصَمُ شَيء.</summary>
    [Theory]
    [InlineData(31, 1.00)]
    [InlineData(2, 0.25)]
    [InlineData(2, 5.0025)]
    public async Task The_invoice_opener_refuses_a_cart_one_invoice_cannot_carry_and_cancels_what_was_born(int count, double each)
    {
        var docs = new DocWorld();
        var platform = new DealMoneyClaimFirstTests.RecordingPaymentProvider();
        var deals = new DealsService(docs.Store, platform);
        var ids = new List<Guid>();
        for (var i = 0; i < count; i++)
            ids.Add((await deals.StartAsync(Slug, "marketplace", Buyer, "مُشتَرٍ", null, "سِلعَة", (decimal)each,
                GatewayPayments.MarkedAtBirth(new() { [CheckoutPayMethods.AttributeKey] = CheckoutPayMethods.Card }, viaGateway: true))).Id);
        var vault = NewVault();
        docs.Put(PaymentsBinding(vault));
        var moyasar = new MoyasarScriptedHandler();
        var gateway = new GatewayPayments(deals, new TenantProviderService(docs.Store), vault,
            new MoyasarClient(new HttpClient(moyasar), NullLogger<MoyasarClient>.Instance));

        var opened = await gateway.OpenInvoiceAsync(Slug, new DealPayer(Buyer), ids, "مَتجَر", ReturnUrl, new L(new HttpContextAccessor()));
        var cancelled = ids.Count(id => docs.Read<Deal>(id.ToString())!.Status == DealStatus.Cancelled);
        output.WriteLine($"{count} × {each} ⇒ الفَتح: {opened.Code} · مُلغاة: {cancelled}/{count} · نِداءاتُ البَوّابَة: {moyasar.Requests.Count}");

        Assert.Equal(MoyasarFailures.Invalid, opened.Code);
        Assert.Equal(count, cancelled);
        Assert.Empty(moyasar.Requests);
        Assert.Empty(platform.Calls);
    }
}
