using ACommerce.Platform.I18n;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ شَريحَةُ OTO في صَفحَةِ صَفقَةِ الاستوديو تَقولُ ما يَقَعُ الآن (‏2026-11-05، الموجَة ١٢ G3 · ٦ — D-4؛ تَوصيفُه `2be2464`) ═══
//
// **القَرار** (القاعِدَة ١٢ وP1 — صِدقُ ما تَقولُه المَنصَّة): الشَريحَةُ العامَّةُ ما دامَت الصَفقَةُ تَسيرُ في شُحنَة (`OnLiveWaybill`)، ونَقدٌ قُبِضَ عَلى طَلَبٍ عِندَ OTO سَطرٌ واحِدٌ
// يَقولُ إنّ الإلغاءَ يَكتُبُ رَدَّ ثَمَنِه مُستَحَقّاً عَلى البائِع (`studio.deal.oto_order_live_delivered`) — بِدالَّةٍ نَقِيَّةٍ تَقرَؤُها الصَفحَة (`DealCancelAuthorization.StudioOtoChipOf`).
// ومِلَفُّ التَوصيفِ `StudioOtoChipTests.cs` بِبَصمَتِه قَبلُ وبَعدُ.

public class StudioOtoChipSwitchTests(ITestOutputHelper output)
{
    /// <summary><b>صُفوفُ الرَسم</b> — مُوجَبٌ وسالِبٌ لِكُلِّ شَريحَة: سُلِّمَت نَقداً (بِالوارِدِ أَو أَثناءَ «نِزاع») ⇒ سَطرُ الرَدّ؛ في الطَريقِ ⇒ «قائِم» وَحدَها أَو بِأَخَواتِها، وبَعدَ إلغائِها
    /// أَيضاً؛ ومُلغاةٌ سُلِّمَت، وطَلَبٌ أَغلَقَته OTO، وبِلا طَلَب، وبِطاقَةٌ سُلِّمَت ⇒ لا شَيء.</summary>
    [Theory]
    [InlineData("delivered_cash", 0, StudioOtoChip.DeliveredCash)]
    [InlineData("delivered_while_disputed", 0, StudioOtoChip.DeliveredCash)]
    [InlineData("in_transit", 0, StudioOtoChip.Live)]
    [InlineData("in_transit", 1, StudioOtoChip.LiveGroup)]
    [InlineData("in_transit_cancelled", 0, StudioOtoChip.Live)]
    [InlineData("delivered_cancelled", 0, StudioOtoChip.None)]
    [InlineData("closed_at_oto", 0, StudioOtoChip.None)]
    [InlineData("unplaced", 0, StudioOtoChip.None)]
    [InlineData("delivered_card", 0, StudioOtoChip.None)]
    public void The_studio_chip_says_live_only_while_the_deal_rides_a_waybill_and_names_the_cash_refund_once_delivered(string how, int liveSiblings, StudioOtoChip expected)
    {
        var d = new Deal { Id = Guid.NewGuid(), Status = DealStatus.Active, Stage = DealStage.Shipping, Pattern = "marketplace" };
        d.Attributes[CheckoutPayMethods.AttributeKey] = how == "delivered_card" ? CheckoutPayMethods.Card : CheckoutPayMethods.CashOnDelivery;
        if (how != "unplaced")
        {
            d.Refs[DealShipping.OrderRef] = d.Id.ToString("N");
            d.Refs[DealShipping.OtoIdRef] = "540789";
        }
        void Delivered()
        {
            d.Timeline.Add(new(DealStage.Shipping, DealStage.Delivered, "advanced", null, null, null, DateTime.UtcNow));
            d.Stage = DealStage.Delivered;
            d.Timeline.Add(new(DealStage.Delivered, DealStage.Delivered, DealEvent.CashOnDelivery, null, null, null, DateTime.UtcNow));
        }
        switch (how)
        {
            case "delivered_cash" or "delivered_card": Delivered(); break;
            case "delivered_cancelled": Delivered(); d.Status = DealStatus.Cancelled; break;
            case "delivered_while_disputed": d.Status = DealStatus.Disputed; d.Refs[DealShipping.DeliveredWhileDisputedRef] = "delivered"; break;
            case "in_transit_cancelled": d.Status = DealStatus.Cancelled; break;
            case "closed_at_oto": d.Refs[DealShipping.ClosedRef] = "canceled"; break;
        }

        var chip = DealCancelAuthorization.StudioOtoChipOf(d, liveSiblings);

        output.WriteLine($"{how} (أَخَوات {liveSiblings}): {chip}");
        Assert.Equal(expected, chip);
    }

    /// <summary><b>والصَفحَةُ تَرسُمُ كُلَّ شَريحَةٍ بِفَرعِها ونَصِّها</b> — بِالدالَّةِ نَفسِها، ولا تَسأَلُ <c>HoldsLiveOrder</c> لِلشَريحَة؛ ونَصُّ سَطرِ الرَدِّ حَيٌّ يُسَمّي البائِعَ والرَدَّ المُستَحَقّ.</summary>
    [Fact]
    public void The_studio_page_draws_each_chip_from_the_pure_choice_and_the_delivered_text_names_the_vendors_refund()
    {
        var page = WriteEndpointGuardTests.StripComments(WriteEndpointGuardTests.StripMarkupComments(StudioOtoChipTests.StudioPage()));
        Assert.Contains("var otoChip = DealCancelAuthorization.StudioOtoChipOf(deal, waybillLive.Count);", page, StringComparison.Ordinal);
        Assert.DoesNotContain("@if (DealShipping.HoldsLiveOrder(deal)", page, StringComparison.Ordinal);
        foreach (var (branch, key) in new[]
                 {
                     ("otoChip == StudioOtoChip.Live)", "@L.Markup(\"studio.deal.oto_order_live\")"),
                     ("otoChip == StudioOtoChip.LiveGroup)", "@L.Format(\"studio.deal.oto_order_live_group\""),
                     ("otoChip == StudioOtoChip.DeliveredCash)", "@L.Markup(\"studio.deal.oto_order_live_delivered\")"),
                 })
        {
            var at = page.IndexOf(branch, StringComparison.Ordinal);
            Assert.True(at > 0, branch);
            Assert.Contains(key, page[at..(at + 300)], StringComparison.Ordinal);
        }
        var text = LocaleCatalog.Text(LocaleCatalog.Arabic, "studio.deal.oto_order_live_delivered");
        output.WriteLine(text);
        Assert.Contains("رَدَّ ثَمَنِها مُستَحَقّاً عَلى البائِع", text, StringComparison.Ordinal);
        Assert.Contains("لا شُحنَةَ قائِمَةً", text, StringComparison.Ordinal);
    }
}
