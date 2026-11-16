using ACommerce.Kit.Culture;
using ACommerce.Kit.Subscriptions;
using ACommerce.Templates.Customer.Marketplace.Services.Subscriptions;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ اشتِراكٌ قائِمٌ يَملِكُ لَحظَتَيه، والاستِمارَةُ تَحمِلُ يَوماً — التَبديل (‏2026-11-05، مُراجَعَةُ G4 الجَولَة ١ · G4R1-1؛ تَوصيفُه `3a9a358` بِبَصمَتِه) ═══
//
// **ما صارَ**: (١) `TenantPlanPolicy.ValidateChange` يُقارِنُ البَدءَ والانتِهاءَ بِـ`FormDay` — اليَومِ الَّذي تَرسُمُه الصَفحَةُ `yyyy-MM-dd` — لا لَحظَةً بِلَحظَة؛
// (٢) و`SetAsync` يَكتُبُ لَحظَةَ المَحفوظِ حينَ يُرسَلُ يَومُها (`KeepStoredInstant`) ما دامَ اشتِراكٌ قائِماً، فَلا يُنقَلُ مَوعِدُ PayPal إلى آخِرِ اليَوم.
// **ولِكُلِّ حُكمٍ مُوجَبٌ وسالِب** (القاعِدَة ٤): حِفظُ أَيّامِ السَماحِ عَبرَ الاستِمارَةِ يُقبَلُ ويُبقي 10:00Z، ويَومٌ غَيرُ المَحفوظِ يُرَدُّ بِـ
// `tenant_plan_paypal_subscription_live`؛ وبِلا اشتِراكٍ قائِمٍ يُكتَبُ ما تَقرَؤُه الاستِمارَةُ كَما كان.

public class PayPalGraceOnlySaveSwitchTests(ITestOutputHelper output)
{
    private const string Slug = "plan-demo";
    private static readonly DateTime PayPalNext = PayPalGraceOnlySaveCharacterizationTests.PayPalNext;
    private static readonly DateTime DayEnd = CultureTime.EndOfDayUtc(new DateOnly(2026, 12, 5));

    // ─── (١) عَبرَ الاستِمارَةِ كَما تُرسَم ────────────────────────────────────────────────────────────────────

    /// <summary><b>المُوجَب</b>: اشتِراكٌ فَعَّلَه PayPal بِمَوعِدِه، وأَيّامُ السَماحِ وَحدَها مِن الاستِمارَةِ كَما تُرسَم ⇐ لا خَرق، ووَثيقَةٌ واحِدَةٌ تُخَزَّن،
    /// والسَماحُ 7، ولَحظَةُ PayPal بِساعَتِها، والبَدءُ والمَرجِعُ كَما هُما.</summary>
    [Fact]
    public async Task A_grace_only_save_through_the_rendered_form_over_paypals_expiry_is_accepted_and_keeps_its_instant()
    {
        var w = await PayPalGraceOnlySaveCharacterizationTests.ActivatedWorldAsync();
        var startsBefore = w.Read<TenantPlan>(Slug)!.StartsAt;
        var before = w.Wrote<TenantPlan>();

        var (v, posted) = await PayPalGraceOnlySaveCharacterizationTests.SaveAsRenderedAsync(w, grace: "7");
        var after = w.Read<TenantPlan>(Slug)!;
        output.WriteLine($"السَماحُ 14 ← 7 عَبرَ الاستِمارَة: خُروق=[{string.Join(",", v.Select(x => x.Code))}] · مُخَزَّن={w.Wrote<TenantPlan>() - before} · "
                       + $"السَماح={after.GraceDays} · المُرسَل={posted:O} · الانتِهاءُ بَعدَه={after.ExpiresAt:O}");

        Assert.Empty(v);
        Assert.Equal(1, w.Wrote<TenantPlan>() - before);
        Assert.Equal(7, after.GraceDays);
        Assert.Equal(DayEnd, posted);
        Assert.Equal(PayPalNext, after.ExpiresAt);
        Assert.Equal(startsBefore, after.StartsAt);
        Assert.Equal("I-SUB-1", after.PayPalSubscriptionId);
        Assert.True(TenantPlanPolicy.HoldsLiveSubscription(after));
    }

    /// <summary><b>السالِب</b>: يَومٌ غَيرُ يَومِ المَحفوظِ — بَعدَه أَو قَبلَه، لِلانتِهاءِ أَو لِلبَدء — مِن الاستِمارَةِ والاشتِراكُ قائِم ⇐ الرَمزُ أَوَّلاً، ولا وَثيقَةَ تُخَزَّن.</summary>
    [Theory]
    [InlineData("2026-12-06")]
    [InlineData("2026-12-04")]
    public async Task Another_expiry_day_through_the_rendered_form_is_still_refused(string day)
    {
        var w = await PayPalGraceOnlySaveCharacterizationTests.ActivatedWorldAsync();
        var before = w.Wrote<TenantPlan>();
        var (v, _) = await PayPalGraceOnlySaveCharacterizationTests.SaveAsRenderedAsync(w, grace: "7", expiresDay: day);
        output.WriteLine($"2026-12-05 ← {day}: خُروق=[{string.Join(",", v.Select(x => x.Code))}] · مُخَزَّن={w.Wrote<TenantPlan>() - before}");

        Assert.Equal(TenantPlanPolicy.PayPalSubscriptionLive, v[0].Code);
        Assert.Equal(0, w.Wrote<TenantPlan>() - before);
        Assert.Equal(PayPalNext, w.Read<TenantPlan>(Slug)!.ExpiresAt);
    }

    // ─── (٢) الدالَّتانِ النَقِيَّتان ────────────────────────────────────────────────────────────────────────

    private static TenantPlan PayPalWritten()
    {
        var p = PayPalArrangementChangeCharacterizationTests.LivePlan();
        p.ExpiresAt = PayPalNext;
        return p;
    }

    /// <summary><b>المُوجَبُ والسالِبُ لِـ<c>ValidateChange</c> بِاليَوم</b>: لَحظَةٌ أُخرى في اليَومِ نَفسِه — لِلانتِهاءِ أَو لِلبَدء — لا خَرق؛ ويَومٌ آخَرُ خَرق.</summary>
    [Fact]
    public void ValidateChange_compares_the_two_dates_by_the_day_the_form_carries()
    {
        var stored = PayPalWritten();

        var sameDay = PayPalWritten(); sameDay.ExpiresAt = DayEnd; sameDay.GraceDays = 7;
        Assert.Empty(TenantPlanPolicy.ValidateChange(stored, sameDay));

        var startsSameDay = PayPalWritten(); startsSameDay.StartsAt = stored.StartsAt.Date.AddHours(1);
        Assert.Empty(TenantPlanPolicy.ValidateChange(stored, startsSameDay));

        var nextDay = PayPalWritten(); nextDay.ExpiresAt = PayPalNext.AddDays(1);
        Assert.Equal(TenantPlanPolicy.PayPalSubscriptionLive, Assert.Single(TenantPlanPolicy.ValidateChange(stored, nextDay)).Code);

        var startsEarlier = PayPalWritten(); startsEarlier.StartsAt = stored.StartsAt.AddDays(-1);
        Assert.Equal(TenantPlanPolicy.PayPalSubscriptionLive, Assert.Single(TenantPlanPolicy.ValidateChange(stored, startsEarlier)).Code);

        Assert.Equal(new DateOnly(2026, 12, 5), TenantPlanPolicy.FormDay(PayPalNext));
        Assert.Equal(TenantPlanPolicy.FormDay(PayPalNext), TenantPlanPolicy.FormDay(DayEnd));
    }

    /// <summary><b><c>KeepStoredInstant</c></b>: اليَومُ نَفسُه ⇐ المَحفوظُ بِساعَتِه؛ ويَومٌ آخَرُ — ولَو بِساعَةٍ عَبرَ مُنتَصَفِ لَيلِ UTC — ⇐ المُرسَل.</summary>
    [Fact]
    public void KeepStoredInstant_keeps_the_stored_instant_on_its_own_day_only()
    {
        Assert.Equal(PayPalNext, TenantPlanPolicy.KeepStoredInstant(PayPalNext, DayEnd));
        Assert.Equal(PayPalNext, TenantPlanPolicy.KeepStoredInstant(PayPalNext, PayPalNext));

        var nextDay = CultureTime.EndOfDayUtc(new DateOnly(2026, 12, 6));
        Assert.Equal(nextDay, TenantPlanPolicy.KeepStoredInstant(PayPalNext, nextDay));

        var lateBefore = new DateTime(2026, 12, 4, 23, 30, 0, DateTimeKind.Utc);
        Assert.Equal(lateBefore, TenantPlanPolicy.KeepStoredInstant(PayPalNext, lateBefore));
    }

    // ─── (٣) بِلا اشتِراكٍ قائِمٍ لا يَتَغَيَّرُ حَرف ────────────────────────────────────────────────────────────

    /// <summary><b>السالِبُ لِلحَملِ نَفسِه</b>: اشتِراكٌ وَصَلَت رِسالَةُ إلغائِه، أَو لا اشتِراكَ أَصلاً (باقَةٌ قَبلَ د51 عَلى مُنتَصَفِ لَيلٍ UTC) ⇐ اليَومُ نَفسُه مِن
    /// الاستِمارَةِ يُكتَبُ آخِرَ اليَومِ كَما كان، ولا تُحمَلُ لَحظَةُ المَحفوظ.</summary>
    [Fact]
    public async Task Without_a_live_subscription_the_form_reading_is_written_as_before()
    {
        var cancelled = PayPalWritten();
        cancelled.RenewalCancelledAt = new DateTime(2026, 11, 20, 8, 0, 0, DateTimeKind.Utc);
        var w = new DocWorld().Put(cancelled);
        Assert.Empty(await TenantPlanAdminService.SetAsync(w.Session, Slug, "manual", cancelled.StartsAt, DayEnd, 7, 99m, "studio · admin", PayPalNext));
        Assert.Equal(DayEnd, w.Read<TenantPlan>(Slug)!.ExpiresAt);

        var midnight = new TenantPlan
        {
            Id = Slug, PlanId = "manual",
            StartsAt = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc), ExpiresAt = new DateTime(2026, 11, 30, 0, 0, 0, DateTimeKind.Utc),
        };
        var m = new DocWorld().Put(midnight);
        var f = TenantPlanPolicy.ReadSetting("manual", midnight.StartsAt.ToString("yyyy-MM-dd"), midnight.ExpiresAt.ToString("yyyy-MM-dd"), "14", "0", PayPalNext);
        Assert.Empty(await TenantPlanAdminService.SetAsync(m.Session, Slug, f.PlanId, f.StartsAt, f.ExpiresAt, f.GraceDays, f.Price, "studio · admin", PayPalNext));
        output.WriteLine($"بِلا اشتِراكٍ قائِم: المُلغى ← {w.Read<TenantPlan>(Slug)!.ExpiresAt:O} · قَبلَ د51 ← {m.Read<TenantPlan>(Slug)!.ExpiresAt:O}");

        Assert.Equal(CultureTime.EndOfDayUtc(new DateOnly(2026, 11, 30)), m.Read<TenantPlan>(Slug)!.ExpiresAt);
        Assert.Equal(CultureTime.EndOfDayUtc(new DateOnly(2026, 9, 1)), m.Read<TenantPlan>(Slug)!.StartsAt);
    }
}
