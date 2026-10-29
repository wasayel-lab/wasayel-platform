using ACommerce.Kit.Culture;
using ACommerce.Kit.Payments.Providers.PayPal;
using ACommerce.Kit.Subscriptions;
using Xunit;

namespace ACommerce.Platform.Tests;

// ═══ يَومُ انتِهاءِ الباقَة — ما لا يَتَغَيَّرُ حينَ يَصيرُ «آخِرَ ذلكَ اليَومِ بِتَوقيتِ الرِياض» (قَرارُ المالِك د51) ═══
//
// **ما يُقلَبُ** (القاعِدَة ٣): صَفٌّ واحِدٌ قائِمٌ سَلَفاً — `StudioHomeActivePlanTests.
// An_expiry_day_written_by_the_admin_ends_the_plan_at_its_first_minute_utc` (‏`a214307`): المُشرِفُ يَكتُبُ يَوماً («2026-11-30»)
// فَيُقرَأُ بِـ`AssumeUniversal` مُنتَصَفَ لَيلِه UTC — الثالِثَةَ فَجراً بِتَوقيتِ الرِياض — فَمِن تِلكَ الدَقيقَةِ المَتجَرُ في السَماح.
// يُقلَبُ بِاسمِه في كوميتِ التَبديل.
//
// **وما يُثَبَّتُ هُنا لِيَبقى أَخضَرَ بِلا حَرفٍ مُبَدَّل** — أَربَعَةُ صُفوفٍ تَحرُسُ ما لا يَجوزُ أَن يَمَسَّه التَبديل:
//   (١) الباقَةُ المَحفوظَةُ قَبلَه تُقرَأُ بِلَحظَتِها المُخَزَّنَة — القِراءَةُ عِندَ الحِفظِ لا عِندَ العَرض (نَصُّ القَرار)؛
//   (٢) وتَمديدُ PayPal بِطولِ المُدَّةِ يَحتَفِظُ بِساعَتِها (نَصُّ القَرار)؛
//   (٣) واليَومُ الَّذي تَعرِضُه صَفحَةُ المُشرِفِ ويَعودُ في استِمارَتِها هُوَ اليَومُ الَّذي كُتِب — فَإعادَةُ الحِفظِ لا تُزيحُه يَوماً؛
//   (٤) وطولُ المُدَّةِ الَّتي يَضبُطُها المُشرِفُ أَيّامٌ كامِلَة — وهو ما يَقرَؤُه تَمديدُ PayPal في (٢).

public class TenantPlanExpiryDayTests
{
    private static readonly DateTime Now = new(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>(١) <b>باقَةٌ حُفِظَت قَبلَ التَبديلِ لا تَتَغَيَّرُ وَحدَها</b>: <see cref="TenantPlanPolicy.Derive"/> تَقرَأُ اللَحظَةَ المُخَزَّنَة
    /// ولا تُعيدُ تَفسيرَ يَومِها — فَمُنتَصَفُ لَيلِ UTC المُخَزَّنُ يَبقى نِهايَتَها حَتّى يُعادَ حِفظُها.</summary>
    [Fact]
    public void A_plan_saved_before_the_switch_keeps_its_stored_instant()
    {
        var stored = new TenantPlan
        {
            PlanId = "manual",
            StartsAt = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
            ExpiresAt = new DateTime(2026, 11, 30, 0, 0, 0, DateTimeKind.Utc),
            GraceDays = 14,
        };

        var nineRiyadh = new DateTime(2026, 11, 30, 6, 0, 0, DateTimeKind.Utc);
        Assert.Equal(TenantPlanState.Grace, TenantPlanPolicy.Derive(stored, nineRiyadh));
        Assert.Equal(TenantPlanState.Active, TenantPlanPolicy.Derive(stored, stored.ExpiresAt));
        Assert.Equal(TenantPlanState.Grace, TenantPlanPolicy.Derive(stored, stored.ExpiresAt.AddTicks(1)));
        Assert.Equal(new DateTime(2026, 11, 30, 3, 0, 0), CultureTime.InZone(stored.ExpiresAt));
    }

    /// <summary>(٢) <b>تَمديدُ PayPal بِطولِ المُدَّةِ يَحتَفِظُ بِالساعَةِ المُخَزَّنَة</b> — المِرساةُ <c>ExpiresAt</c> ويُضافُ إلَيها
    /// <c>ExpiresAt − StartsAt</c>، فَلا يَمَسُّ التَبديلُ باقَةً قائِمَةً عَبرَ التَجديدِ أَيضاً.</summary>
    [Fact]
    public void A_paypal_renewal_by_the_set_period_keeps_the_stored_hour()
    {
        var stored = new TenantPlan
        {
            Id = "demo",
            PlanId = "manual",
            StartsAt = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc),
            ExpiresAt = new DateTime(2026, 10, 31, 0, 0, 0, DateTimeKind.Utc),
            GraceDays = 14,
        };
        var e = new PayPalWebhookEvent("WH-1", PayPalEventTypes.PaymentSaleCompleted, "demo", "I-1", null);

        var d = PayPalBillingPolicy.Decide(e, stored, alreadySeen: false, Now);

        Assert.Equal(PayPalBillingAction.Extend, d.Action);
        Assert.Equal(new DateTime(2026, 11, 30, 0, 0, 0, DateTimeKind.Utc), d.NewExpiresAt);
        Assert.Equal(stored.ExpiresAt.TimeOfDay, d.NewExpiresAt.TimeOfDay);
    }

    /// <summary>(٣) <b>اليَومُ يَعودُ كَما كُتِب</b>: ما تَقرَؤُه <see cref="TenantPlanPolicy.ReadSetting"/> مِن «2026-09-01» و«2026-11-30»
    /// تَطبَعُه صَفحَةُ المُشرِفِ (<c>ToString("yyyy-MM-dd")</c>) وتَملَأُ بِه استِمارَتَها اليَومَينِ نَفسَيهِما — فَإعادَةُ الحِفظِ لا تُزيحُ يَوماً.
    /// وساعَةُ الرِياضِ لِلَّحظَةِ المَقروءَةِ في اليَومِ نَفسِه أَيضاً (لا يَعبُرُ إلى الغَد).</summary>
    [Fact]
    public void The_admin_form_round_trips_the_days_it_shows()
    {
        var f = TenantPlanPolicy.ReadSetting("manual", "2026-09-01", "2026-11-30", "14", "0", Now);
        Assert.Equal("2026-09-01", f.StartsAt.ToString("yyyy-MM-dd"));
        Assert.Equal("2026-11-30", f.ExpiresAt.ToString("yyyy-MM-dd"));
        Assert.Equal("2026-11-30", CultureTime.InZone(f.ExpiresAt).ToString("yyyy-MM-dd"));

        var again = TenantPlanPolicy.ReadSetting("manual",
            f.StartsAt.ToString("yyyy-MM-dd"), f.ExpiresAt.ToString("yyyy-MM-dd"), "14", "0", Now);
        Assert.Equal((f.StartsAt, f.ExpiresAt), (again.StartsAt, again.ExpiresAt));
    }

    /// <summary>(٤) <b>طولُ المُدَّةِ المَضبوطَةِ أَيّامٌ كامِلَة</b> — هو ما يُضيفُه تَمديدُ PayPal في (٢) حينَ لا تُعطي PayPal مَوعِداً،
    /// فَلَو صارَ كَسراً لَزاحَت ساعَةُ النِهايَةِ مَعَ كُلِّ تَجديد.</summary>
    [Fact]
    public void The_period_the_admin_sets_is_whole_days()
    {
        var f = TenantPlanPolicy.ReadSetting("manual", "2026-09-01", "2026-11-30", "14", "0", Now);
        Assert.Equal(TimeSpan.FromDays(90), f.ExpiresAt - f.StartsAt);
    }
}
