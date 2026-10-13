using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ صَفقَةُ بِطاقَةٍ عَلى مُزَوِّدِ النُسخَةِ بِلا مَرجِعِ دَفع — تَدخُلُ «دَفع» بِلا قَبض — تَوصيفُ «اليَوم» ═══════════════════════════════
//
// مُراجَعَةُ بَوّابَةِ المَوجَة 3b (‏2026-10-13، G5 (xv-g)): `/checkout/submit` عَلى طَريقِ مُزَوِّدِ النُسخَةِ (لا بَوّابَةَ لِلمَتجَر) يَحجِزُ عِندَ
// الوِلادَة؛ وحينَ يَرمي الحَجزُ تُعَلَّمُ الصَفقَةُ `payment_error` بِلا `payment_id` «والمالِكُ يَستَطيعُ رُؤيَتَها يَدَوِيّاً». والقَبضُ عِندَ «دَفع»
// يَتَجاوَزُ صَفقَةً بِلا `payment_id`، وحارِسُ «دَفع» (`AwaitsPayment`) لِطَريقِ البَوّابَةِ وَحدَه — فَتَدخُلُ صَفقَةُ البِطاقَةِ هذِه «دَفع» بِلا مال.
// ولا يَقولُ ‏ADR غَيرَ ذلك (مَقيسٌ بِـ`grep payment_error docs/`: ‏ADR-036 §٨ يَصِفُ تَرتيبَ الكِتابَةِ وَحدَه).
//
// **ما يُقلَبُ بِاسمِه في كوميتِ الإصلاح**: صَفُّ «Today_» أَدناه.

public class DealCardWithoutPaymentTests(ITestOutputHelper output)
{
    private const string Slug = "zz-card-nopay";
    private static readonly Guid Buyer  = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
    private static readonly Guid Seller = Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb");

    /// <summary>صَفقَةُ بِطاقَةٍ عِندَ «تَأكيد» — تاليها «دَفع» وفاعِلُه المُشتَري — رَمى حَجزُها عِندَ الوِلادَة: <c>payment_error</c> بِلا <c>payment_id</c>.</summary>
    internal static (DocWorld W, DealsService Deals, DealMoneyClaimFirstTests.RecordingPaymentProvider Pay, Guid Id) LegacyCardDeal(
        bool withPaymentId = false, string? method = CheckoutPayMethods.Card)
    {
        var world = new DocWorld();
        var pay = new DealMoneyClaimFirstTests.RecordingPaymentProvider();
        var deals = new DealsService(world.Store, pay);
        var deal = new Deal
        {
            Id = Guid.NewGuid(), TenantSlug = Slug, Pattern = "marketplace", Stage = DealStage.Confirmed, AmountSar = 100,
            InitiatorId = Buyer, InitiatorName = "مُشتَرٍ", CounterpartyId = Seller, CounterpartyName = "بائِع",
        };
        if (method is not null) deal.Attributes[CheckoutPayMethods.AttributeKey] = method;
        if (withPaymentId) { deal.Refs["payment_id"] = "pay_1"; deal.Refs["payment_status"] = "Authorized"; }
        else deal.Refs["payment_error"] = "provider unavailable";
        world.Put(deal);
        return (world, deals, pay, deal.Id);
    }

    [Fact]
    public async Task Today_a_card_deal_whose_hold_failed_enters_Paid_without_a_capture()
    {
        var (w, deals, pay, id) = LegacyCardDeal();
        var res = await deals.AdvanceAsync(Slug, id, Buyer, "مُشتَرٍ");
        var deal = w.Read<Deal>(id.ToString())!;
        output.WriteLine($"«دَفع»: ok={res.Ok} code={res.Code ?? "-"} · المَرحَلَة: {deal.Stage} · نِداءاتُ المُزَوِّد: {pay.Calls.Count} · payment_id: {deal.Refs.ContainsKey("payment_id")}");

        Assert.True(res.Ok);
        Assert.Equal(DealStage.Paid, deal.Stage);
        Assert.Empty(pay.Calls);
        Assert.False(deal.Refs.ContainsKey("payment_id"));
    }
}
