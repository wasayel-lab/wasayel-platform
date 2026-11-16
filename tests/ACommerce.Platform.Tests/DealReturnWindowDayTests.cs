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

    /// <summary><b>قُلِبَ بِاسمِه</b> (تَوصيفُه <c>7b17822</c> — كانَ <c>Today_the_window_is_168_hours_from_the_delivery_click</c>: تَنتَهي ‏09:00 يَومَ 8): التَسليمُ ‏2026-11-01
    /// ‏09:00 بِالرِياض ⇒ المُدَّةُ تَنتَهي <b>‏2026-11-08 ‏23:59:59 بِالرِياض</b> — طَلَبٌ ‏10:00 يَومَ 8 وطَلَبٌ ‏23:59:59 يُقبَلان (المُوجِب)، وبَعدَ آخِرِ ثانِيَةٍ بِثانِيَةٍ يُرَدُّ
    /// <c>return_window_closed</c> (السالِب) — فَنَصُّ الرَدِّ «مَضَت سَبعَةُ الأَيّام» صادِقٌ بِالبِناء.</summary>
    [Fact]
    public void The_window_closes_at_the_end_of_the_seventh_riyadh_day_after_delivery()
    {
        var deal = DeliveredCartDeal(DeliveredUtc);
        var ends = DealReturnRequest.WindowEndsAt(deal)!.Value;
        var tenAm = new DateTime(2026, 11, 8, 7, 0, 0, DateTimeKind.Utc);          // ‏10:00 بِالرِياض
        var lastSecond = new DateTime(2026, 11, 8, 20, 59, 59, DateTimeKind.Utc);  // ‏23:59:59 بِالرِياض
        output.WriteLine($"تَنتَهي: {CultureTime.InZone(ends):yyyy-MM-dd HH:mm:ss} بِالرِياض · 10:00: {DealReturnRequest.Validate(deal, AsBuyer, tenAm)?.Code ?? "يُقبَل"} · " +
                         $"23:59:59: {DealReturnRequest.Validate(deal, AsBuyer, lastSecond)?.Code ?? "يُقبَل"} · بَعدَها بِثانِيَة: {DealReturnRequest.Validate(deal, AsBuyer, lastSecond.AddSeconds(1))?.Code}");
        Assert.Equal(CultureTime.EndOfDayUtc(new DateOnly(2026, 11, 8)), ends);
        Assert.Equal(new DateTime(2026, 11, 8, 23, 59, 59), CultureTime.InZone(ends).AddTicks(-(CultureTime.InZone(ends).Ticks % TimeSpan.TicksPerSecond)));
        Assert.Null(DealReturnRequest.Validate(deal, AsBuyer, tenAm));
        Assert.Null(DealReturnRequest.Validate(deal, AsBuyer, lastSecond));
        Assert.Equal(DealReturnRequest.WindowClosed, DealReturnRequest.Validate(deal, AsBuyer, lastSecond.AddSeconds(1))?.Code);
    }

    /// <summary>وتَسليمٌ قُبَيلَ مُنتَصَفِ لَيلِ الرِياضِ (‏23:30 = ‏20:30 UTC) يُعَدُّ مِن يَومِه بِتَوقيتِ الرِياضِ لا مِن يَومِ UTC — والحَدُّ آخِرُ اليَومِ السابِعِ بَعدَه.</summary>
    [Fact]
    public void The_delivery_day_is_the_riyadh_day_not_the_utc_day()
    {
        var lateNight = new DateTime(2026, 11, 1, 21, 30, 0, DateTimeKind.Utc);   // ‏2026-11-02 ‏00:30 بِالرِياض
        Assert.Equal(CultureTime.EndOfDayUtc(new DateOnly(2026, 11, 9)), DealReturnRequest.WindowEndsAt(DeliveredCartDeal(lateNight)));
        var evening = new DateTime(2026, 11, 1, 20, 30, 0, DateTimeKind.Utc);     // ‏2026-11-01 ‏23:30 بِالرِياض
        Assert.Equal(CultureTime.EndOfDayUtc(new DateOnly(2026, 11, 8)), DealReturnRequest.WindowEndsAt(DeliveredCartDeal(evening)));
    }
}
