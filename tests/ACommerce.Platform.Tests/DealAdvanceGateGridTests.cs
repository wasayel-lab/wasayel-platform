using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ `CanMove` و`AdvanceAsync` — شَبَكَةٌ تَقيسُ التَكافُؤَ حالَةً حالَة ═════════════════════════════════════════════════════
//
// مُراجَعَةُ المَوجَة 3a (G4 (xxviii)) ومُراجَعَةُ الشَحنِ الأَخيرَة (G4 (xxxi))، ‏2026-10-13: `DealsService.CanMove` مَوصوفَةٌ «بَوّابَةُ
// `AdvanceAsync` نَفسُها بِلا كِتابَة» و`AdvanceAsync` لا تُناديها — تُعيدُ الشُروطَ الثَلاثَةَ بِسُطورِها (فَعّالَة، لَها تالٍ، فاعِلُ المَدخولَة)
// ثُمَّ تَرُدُّ بَعدَها ثَلاثَةَ مُمسِكاتٍ لا تَسأَلُها `CanMove` (طَلَبٌ عِندَ OTO بِلا جَواب، فاتورَةُ بَوّابَةٍ لَم تُدفَع، بِطاقَةٌ بِلا مَرجِعِ دَفع).
// فَوَصفٌ لا يَحرُسُه شَيء: لَو انجَرَفَ أَحَدُهُما ما احمَرَّ صَفّ.
//
// **والقَرار**: صَفٌّ يَمشي الشَبَكَةَ كُلَّها عَلى الخِدمَةِ الحَقيقِيَّة — الأَنماطُ × المَراحِلُ × الحالاتُ × طُرُقُ الدَفعِ × المال × الطَلَبُ عِندَ OTO ×
// الطَرَف × الفاعِل — ويَشتَرِط: `AdvanceAsync` تُحَرِّكُ ⇔ `CanMove ∧ ¬AwaitsCarrier ∧ ¬AwaitsPayment ∧ ¬MissesPayment`. لا تَوحيدَ في الكود:
// `CanMove` يُناديها مَسارُ الشَحنِ قَبلَ سُؤالِ OTO، فَلَو سَأَلَت `AwaitsCarrier` لَرُدَّ استِئنافُ طَلَبٍ قائِم؛ والمُمسِكاتُ تَقَعُ في الانتِقالِ نَفسِه.
// فَيَبقى اسمُ `CanMove_is_the_advance_gate_without_writing` (`DealShipTests`) صادِقاً عَلى شَطرِه، وهذا الصَفُّ يَقيسُ الباقي.

public class DealAdvanceGateGridTests(ITestOutputHelper output)
{
    private const string Slug = "zz-advance-grid";
    private static readonly Guid Buyer    = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
    private static readonly Guid Seller   = Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb");
    private static readonly Guid Stranger = Guid.Parse("cccccccc-cccc-4ccc-8ccc-cccccccccccc");

    /// <summary>مالُ الصَفقَة: لا شَيء، أَو حَجزٌ عَلَيه مَرجِعُه، أَو وِلادَةٌ لِفاتورَةِ البَوّابَةِ بِلا فاتورَةٍ ولا دَفع.</summary>
    private static void Money(Deal d, string money)
    {
        switch (money)
        {
            case "held":
                d.Refs["payment_id"] = "pay_held";
                d.Refs["payment_status"] = "Authorized";
                break;
            case "gateway":
                d.Attributes[GatewayPayments.PayViaAttribute] = GatewayPayments.PayViaStoreGateway;
                break;
        }
    }

    /// <summary>
    /// <para><b>التَكافُؤُ عَلى الشَبَكَةِ كُلِّها</b>: لِكُلِّ حالَةٍ يُحسَبُ المُتَوَقَّعُ مِن الدَوالِّ النَقِيَّةِ عَلى الصَفقَةِ <b>قَبلَ</b> النِداء، ثُمَّ تُنادى
    /// <c>AdvanceAsync</c> الحَقيقِيَّةُ عَلى عالَمِ وَثائِقَ جَديد. والعَدّاداتُ تُطبَع، والصَفُّ يَفشَلُ إن لَم يَمُرَّ بِكُلِّ فَرعٍ — تَحريكٍ ومُمسِكٍ مُمسِكٍ
    /// ورَفضِ فاعِل — فَلا تَكافُؤَ يُدَّعى عَلى شَبَكَةٍ لا تَبلُغُ فَرعاً (القاعِدَة ١٠).</para>
    /// </summary>
    [Fact]
    public async Task AdvanceAsync_moves_exactly_when_CanMove_holds_and_no_carrier_or_payment_hold_applies()
    {
        int cases = 0, moved = 0, refusedActor = 0, heldCarrier = 0, heldPayment = 0, heldMissing = 0;
        foreach (var pattern in new[] { "marketplace", "rental", "service", "trip", "classifieds", "pattern_from_the_future" })
        foreach (var stage in Enum.GetValues<DealStage>())
        foreach (var status in Enum.GetValues<DealStatus>())
        foreach (var pay in new string?[] { CheckoutPayMethods.Card, CheckoutPayMethods.CashOnDelivery, null })
        foreach (var money in new[] { "none", "held", "gateway" })
        foreach (var placed in new[] { false, true })
        foreach (var counterparty in new Guid?[] { Seller, null })
        foreach (var actor in new[] { Buyer, Seller, Stranger })
        {
            var world = new DocWorld();
            var deals = new DealsService(world.Store, new DealMoneyClaimFirstTests.RecordingPaymentProvider());
            var deal = new Deal
            {
                Id = Guid.NewGuid(), TenantSlug = Slug, Pattern = pattern, Stage = stage, Status = status, AmountSar = 100,
                InitiatorId = Buyer, InitiatorName = "مُشتَرٍ", CounterpartyId = counterparty, CounterpartyName = counterparty is null ? null : "بائِع",
            };
            if (pay is not null) deal.Attributes[CheckoutPayMethods.AttributeKey] = pay;
            Money(deal, money);
            if (placed) deal.Refs[DealShipping.OrderRef] = deal.Id.ToString("N");
            world.Put(deal);

            var canMove  = DealsService.CanMove(deal, actor);
            var carrier  = DealShipping.AwaitsCarrier(deal);
            var awaits   = GatewayPayments.AwaitsPayment(deal);
            var missing  = DealsService.MissesPayment(deal);
            var expected = canMove && !carrier && !awaits && !missing;

            var res = await deals.AdvanceAsync(Slug, deal.Id, actor, "فاعِل");
            Assert.True(expected == res.Ok,
                $"{pattern}/{stage}/{status}/{pay ?? "-"}/{money}/طَلَب={placed}/طَرَف={counterparty is not null}/{actor}: " +
                $"المُتَوَقَّع {expected} (CanMove={canMove} carrier={carrier} awaits={awaits} missing={missing}) والمَقيس {res.Ok} {res.Code} {res.Reason}");

            cases++;
            if (res.Ok) moved++;
            else if (!canMove) refusedActor++;
            else if (carrier) { heldCarrier++; Assert.Equal(DealShipping.DealAwaitsCarrier, res.Code); }
            else if (awaits) { heldPayment++; Assert.Equal(GatewayPayments.DealAwaitsPayment, res.Code); }
            else if (missing) { heldMissing++; Assert.Equal(DealsService.PaymentMissing, res.Code); }
        }

        output.WriteLine($"الحالات: {cases} · تَحَرَّكَت: {moved} · رَفضُ الحالِ أَو الفاعِل: {refusedActor} · " +
                         $"مُمسِكُ OTO: {heldCarrier} · مُمسِكُ الفاتورَة: {heldPayment} · مُمسِكُ المَرجِع: {heldMissing}");
        // ‏6 أَنماطٍ × 8 مَراحِلَ × 4 حالاتٍ × 3 طُرُقِ دَفعٍ × 3 أَموالٍ × 2 × 2 × 3 فاعِلين.
        Assert.Equal(6 * 8 * 4 * 3 * 3 * 2 * 2 * 3, cases);
        Assert.True(moved > 0 && refusedActor > 0 && heldCarrier > 0 && heldPayment > 0 && heldMissing > 0,
            "أَداةٌ عَمياء: فَرعٌ لَم تَبلُغه الشَبَكَة.");
    }
}
