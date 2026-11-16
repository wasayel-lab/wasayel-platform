using ACommerce.Kit.Culture;
using ACommerce.Kit.Subscriptions;
using Xunit;

namespace ACommerce.Platform.Tests;

// ═══ «تَنتَهي يَومَ X» = آخِرُ X بِتَوقيتِ المَنَصَّة — دالَّةٌ واحِدَة (قَرارُ المالِك د51، ‏ADR-039 §مُلحَق د51) ═══
//
// `CultureTime.EndOfDayUtc` تُحَوِّلُ يَوماً بِلا ساعَةٍ إلى آخِرِ لَحظَةٍ مِنه بِساعَةِ المِنطَقَة، بِـUTC؛ و`TenantPlanPolicy.ReadSetting`
// تَمُرُّ بِها لِكُلِّ حَقلِ تاريخٍ بِصيغَةِ `yyyy-MM-dd` مِن استِمارَةِ المُشرِف. وما يَبقى كَما كانَ مُثَبَّتٌ في `TenantPlanExpiryDayTests`
// (مِلَفُّ التَوصيف، بِلا حَرفٍ مُبَدَّل)، والصَفُّ المَقلوبُ في `StudioHomeActivePlanTests`.

public class PlanExpiryEndOfDayTests
{
    private static readonly DateTime Now = new(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void The_end_of_a_day_in_riyadh_is_its_last_tick_three_hours_before_utc_midnight()
    {
        var end = CultureTime.EndOfDayUtc(new DateOnly(2026, 11, 30));

        Assert.Equal(new DateTime(2026, 11, 30, 21, 0, 0, DateTimeKind.Utc).AddTicks(-1), end);
        Assert.Equal(DateTimeKind.Utc, end.Kind);
        Assert.Equal(new DateTime(2026, 11, 30, 23, 59, 59).AddTicks(9_999_999), CultureTime.InZone(end));
        // والتالي لَها أَوَّلُ الغَدِ بِتَوقيتِ الرِياض — لا فَجوَةَ ولا تَداخُل.
        Assert.Equal(new DateTime(2026, 12, 1, 0, 0, 0), CultureTime.InZone(end.AddTicks(1)));
    }

    /// <summary>مِنطَقَةٌ لا تُعرَفُ تَسقُطُ إلى مِنطَقَةِ المَنَصَّةِ لا إلى UTC — حُكمُ <see cref="CultureTime.Zone"/> نَفسُه.</summary>
    [Fact]
    public void An_unknown_zone_falls_back_to_the_platform_zone_not_utc()
    {
        var day = new DateOnly(2026, 11, 30);
        Assert.Equal(CultureTime.EndOfDayUtc(day), CultureTime.EndOfDayUtc(day, "Mars/Olympus_Mons"));
        Assert.NotEqual(CultureTime.EndOfDayUtc(day, "UTC"), CultureTime.EndOfDayUtc(day, "Mars/Olympus_Mons"));
    }

    /// <summary>والمِنطَقَةُ المُسَمّاةُ تُحتَرَم — فَحَقلُ مِنطَقَةٍ لِكُلِّ مَتجَرٍ يَومَ يُطلَبُ يُمَرِّرُ مُعَرِّفَه هُنا بِلا تَوقيعٍ جَديد.</summary>
    [Fact]
    public void A_named_zone_is_honoured()
    {
        Assert.Equal(new DateTime(2026, 11, 30, 23, 59, 59, DateTimeKind.Utc).AddTicks(9_999_999),
            CultureTime.EndOfDayUtc(new DateOnly(2026, 11, 30), "UTC"));
    }

    /// <summary>سالِب: لَحظَةٌ كُتِبَت بِساعَتِها لا تُعادُ قِراءَتُها آخِرَ يَومِها — تُقرَأُ كَما كُتِبَت، كَما كانَت.</summary>
    [Fact]
    public void An_expiry_written_with_its_hour_is_read_as_written()
    {
        var f = TenantPlanPolicy.ReadSetting("manual", "2026-09-01T08:00:00Z", "2026-11-30T10:00:00Z", "14", "0", Now);
        Assert.Equal(new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc), f.StartsAt);
        Assert.Equal(new DateTime(2026, 11, 30, 10, 0, 0, DateTimeKind.Utc), f.ExpiresAt);
    }

    /// <summary>سالِب: نَصٌّ لا يُقرَأُ تاريخاً يَسقُطُ إلى ما يَرُدُّه المُصادِق (<see cref="TenantPlanPolicy.PeriodInvalid"/>) — لا يَومٌ يُخترَع.</summary>
    [Fact]
    public void An_unreadable_expiry_still_falls_to_what_the_validator_refuses()
    {
        var f = TenantPlanPolicy.ReadSetting("manual", "2026-09-01", "ليس تاريخاً", "14", "0", Now);
        Assert.Equal(DateTime.MinValue, f.ExpiresAt);
        var plan = new TenantPlan { Id = "demo", PlanId = "manual", StartsAt = f.StartsAt, ExpiresAt = f.ExpiresAt, GraceDays = 14 };
        Assert.Contains(TenantPlanPolicy.Validate(plan), v => v.Code == TenantPlanPolicy.PeriodInvalid);
    }

    /// <summary>بَدءٌ غائِبٌ = «اليَوم» بِتَوقيتِ الرِياضِ مَقروءاً بِالدالَّةِ نَفسِها: في الحادِيَةِ والعِشرينَ UTC مِن 29 اليَومُ في الرِياضِ 30.</summary>
    [Fact]
    public void A_missing_start_is_today_in_riyadh_read_the_same_way()
    {
        var lateUtc = new DateTime(2026, 10, 29, 22, 0, 0, DateTimeKind.Utc);   // ‏01:00 مِن 30 بِتَوقيتِ الرِياض
        var f = TenantPlanPolicy.ReadSetting("manual", null, "2026-12-30", "14", "0", lateUtc);
        Assert.Equal(CultureTime.EndOfDayUtc(new DateOnly(2026, 10, 30)), f.StartsAt);
        Assert.Equal(TimeSpan.FromDays(61), f.ExpiresAt - f.StartsAt);
    }
}
