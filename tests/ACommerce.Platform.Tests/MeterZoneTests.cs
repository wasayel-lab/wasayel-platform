using ACommerce.Kit.Culture;
using ACommerce.Platform.Shared.Metering;
using ACommerce.Templates.Customer.Marketplace.Services.Metering;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ حَدُّ يَومِ العَدِّ مِنطَقَةُ المَنَصَّة — في اللَقطَةِ والمُجَمِّعِ والصَفحَةِ نَفسِها (‏2026-11-13، الموجَة ١٣ W13-6·١٩ — ‏ADR-051 §٢٣؛ التَصميم §8، س٥) ═══
// ‏«اليَوم» في لَقطَةِ الحُضورِ (أَيُّ يَومٍ تُطالِبُ بِه، ومَتى يَحينُ غَدُها، و`occurred_at` سُطورِها) هُوَ يَومُ `CultureTime.DefaultTimeZone` (`CultureContext.cs:49`)،
// ويَومُ المُجَمِّعِ (`MeterSql.DayZone`، نُسخَةٌ يَحرُسُها G10) هُوَ هُوَ — فَسَطرُ حُضورِ يَومٍ يَقَعُ في دَلوِ ذلكَ اليَومِ عِندَ المُجَمِّعِ والصَفحَة. مِنطَقَةٌ أُخرى في أَحَدِها
// تَجعَلُ لَقطَةَ مُنتَصَفِ لَيلِ الرِياضِ في يَومِ أَمسِ عِندَ الآخَر. وكُلُّ صَفٍّ لَه ضابِطٌ يَرى المِنطَقَةَ الخاطِئَة.

public class MeterZoneTests(ITestOutputHelper output)
{
    [Fact]
    public void The_meters_zone_is_the_platforms_default_time_zone_everywhere_a_day_is_cut()
    {
        output.WriteLine($"[zone] PresenceSnapshot.Zone = {PresenceSnapshot.Zone} · MeterSql.DayZone = {MeterSql.DayZone} · CultureTime.DefaultTimeZone = {CultureTime.DefaultTimeZone}");
        Assert.Equal(CultureTime.DefaultTimeZone, PresenceSnapshot.Zone);
        Assert.Equal(CultureTime.DefaultTimeZone, MeterSql.DayZone);
        Assert.Equal("Asia/Riyadh", PresenceSnapshot.Zone);
        // ‏سُطورُ الحُضورِ تُكتَبُ عِندَ بِدايَةِ يَومِها بِتِلكَ المِنطَقَة، والمُجَمِّعُ يُقَسِّمُ بِها.
        Assert.Contains($"(@day::timestamp at time zone '{MeterSql.DayZone}')", MeterSql.PresenceInsert, StringComparison.Ordinal);
        Assert.Contains($"at time zone '{MeterSql.DayZone}'", MeterSql.AggUpsertRange, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("2026-11-12T20:59:59Z", "2026-11-12")]
    [InlineData("2026-11-12T21:00:00Z", "2026-11-13")]
    [InlineData("2026-11-13T20:59:59Z", "2026-11-13")]
    [InlineData("2026-12-31T21:00:00Z", "2027-01-01")]
    public void A_day_turns_at_midnight_in_riyadh_not_at_midnight_utc(string instant, string day)
    {
        var at = DateTimeOffset.Parse(instant, System.Globalization.CultureInfo.InvariantCulture);
        var measured = PresenceReader.DayKey(PresenceSnapshot.DayOf(at));
        var utc = PresenceReader.DayKey(DateOnly.FromDateTime(at.UtcDateTime));
        output.WriteLine($"[zone] {instant} ⇐ يَومُ العَدّ {measured} · يَومُ UTC (الضابِطُ السالِب) {utc}");
        Assert.Equal(day, measured);
        // ‏الضابِطُ السالِب: ما بَعدَ التاسِعَةِ مَساءً UTC يَومٌ آخَرُ لَو قُطِعَ بِـUTC — فَالصَفُّ يَرى المِنطَقَةَ الخاطِئَة.
        if (at.UtcDateTime.Hour >= 21) Assert.NotEqual(utc, measured);
    }

    [Fact]
    public void A_days_start_is_midnight_in_riyadh_and_maps_back_to_that_day()
    {
        var day = new DateOnly(2026, 11, 13);
        var start = PresenceSnapshot.StartOf(day);
        output.WriteLine($"[zone] بِدايَةُ {PresenceReader.DayKey(day)} = {start:O} · وقَبلَها بِثانِيَةٍ يَومُ {PresenceReader.DayKey(PresenceSnapshot.DayOf(start.AddSeconds(-1)))}");
        Assert.Equal(DateTimeOffset.Parse("2026-11-12T21:00:00Z", System.Globalization.CultureInfo.InvariantCulture), start);
        Assert.Equal(TimeSpan.Zero, start.Offset);
        Assert.Equal(day, PresenceSnapshot.DayOf(start));
        Assert.Equal(day.AddDays(-1), PresenceSnapshot.DayOf(start.AddSeconds(-1)));
        Assert.Equal(TimeSpan.FromDays(1), PresenceSnapshot.StartOf(day.AddDays(1)) - start);
    }
}
