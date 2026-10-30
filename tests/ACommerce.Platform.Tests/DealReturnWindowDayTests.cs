using ACommerce.Kit.Culture;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ «الأيام السبعة التالية لتاريخ تسلّمه» — ساعاتٌ أَم أَيّام؟ (‏2026-10-30، تَدقيقُ الموجَة ١١ E-deals-3) ═════════════════════════════════════════
//
// **الحالُ المَقيسَة**: `WindowEndsAt = DeliveredAt + 7 days` — ‏168 ساعَةً مِن نَقرَةِ التَسليم. والمادَّةُ ١٣ (`tenant_returns_art13.obligation.json`): «خلال الأيام السبعة
// التالية لتاريخ تسلمه المنتج» — أَيّامٌ تَقويمِيَّةٌ تَنتَهي آخِرَ اليَومِ السابِعِ بِتَوقيتِ الرِياض؛ وقاعِدَةُ المالِكِ لِلتَواريخِ في د51 (آخِرُ اليَومِ بِتَوقيتِ الرِياض) قائِمَةٌ في
// `CultureTime.EndOfDayUtc` ولَم تُطَبَّق هُنا. فَتَسليمٌ ‏2026-11-01 ‏09:00 بِالرِياضِ يُغلِقُ ‏2026-11-08 ‏09:00، ولِنَحوِ ‏15 ساعَةً يُجابُ «مَضَت سَبعَةُ الأَيّامِ…» كَذِباً.

public class DealReturnWindowDayTests(ITestOutputHelper output)
{
    private static readonly Guid Buyer = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
    private static readonly DealReturnRequester AsBuyer = new(Buyer, "مُشتَرٍ");

    /// <summary>‏2026-11-01 ‏09:00 بِتَوقيتِ الرِياض = ‏06:00 UTC.</summary>
    internal static readonly DateTime DeliveredUtc = new(2026, 11, 1, 6, 0, 0, DateTimeKind.Utc);

    internal static Deal DeliveredCartDeal(DateTime deliveredUtc)
    {
        var deal = new Deal
        {
            Id = Guid.NewGuid(), Pattern = DealReturnRequest.GoodsPattern, Stage = DealStage.Delivered, Status = DealStatus.Active, AmountSar = 90m,
            ListingTitle = "قَميص", InitiatorId = Buyer, InitiatorName = "مُشتَرٍ", CounterpartyId = Guid.NewGuid(), CounterpartyName = "بائِع",
        };
        deal.Attributes[CheckoutPayMethods.AttributeKey] = CheckoutPayMethods.CashOnDelivery;
        deal.Timeline.Add(new(DealStage.Offered, DealStage.Offered, "created", Buyer, "مُشتَرٍ", null, deliveredUtc.AddDays(-3)));
        deal.Timeline.Add(new(DealStage.Shipping, DealStage.Delivered, "advanced", deal.CounterpartyId, "بائِع", null, deliveredUtc));
        return deal;
    }

    /// <summary><b>تَوصيفُ «اليَوم»</b> (القاعِدَة ٣): التَسليمُ ‏2026-11-01 ‏09:00 بِالرِياض — المُدَّةُ تَنتَهي ‏2026-11-08 ‏09:00 بِالرِياضِ بِالضَبط، وطَلَبٌ ‏10:00 يَومَ
    /// ‏2026-11-08 يُرَدُّ <c>return_window_closed</c>.</summary>
    [Fact]
    public void Today_the_window_is_168_hours_from_the_delivery_click()
    {
        var deal = DeliveredCartDeal(DeliveredUtc);
        var ends = DealReturnRequest.WindowEndsAt(deal)!.Value;
        var tenAm = new DateTime(2026, 11, 8, 7, 0, 0, DateTimeKind.Utc);   // ‏10:00 بِالرِياض
        output.WriteLine($"تَنتَهي: {CultureTime.InZone(ends):yyyy-MM-dd HH:mm:ss} بِالرِياض · الطَلَبُ 10:00 يَومَ 8: {DealReturnRequest.Validate(deal, AsBuyer, tenAm)?.Code ?? "يُقبَل"}");
        Assert.Equal(DeliveredUtc.AddHours(168), ends);
        Assert.Equal(DealReturnRequest.WindowClosed, DealReturnRequest.Validate(deal, AsBuyer, tenAm)?.Code);
    }
}
