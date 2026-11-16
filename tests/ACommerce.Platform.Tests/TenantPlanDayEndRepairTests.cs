using ACommerce.Kit.Culture;
using ACommerce.Kit.Subscriptions;
using ACommerce.Templates.Customer.Marketplace.Services.Subscriptions;
using Xunit;

namespace ACommerce.Platform.Tests;

// ═══ تَرميمُ باقاتٍ حُفِظَت قَبلَ د51 — آخِرُ اليَومِ بِتَوقيتِ المَنَصَّة، والتاريخانِ وَحدَهُما (تَدقيقُ الموجَة ١١ F-SDC-1) ═══
//
// `scripts/repair-plan-day-end.cs` بَديلُ «أَعِد حِفظَ باقَةِ كُلٍّ» في فِعلِ المالِكِ د51: الحِفظُ يُعيدُ الحالَةَ سارِيَةً ويُظهِرُ مَتجَراً
// أَوقَفَه المُشرِف. والقَرارُ في `TenantPlanDayEndRepair` — يُقاسُ هُنا بِطَرَفَيه (مُرَشَّحٌ وغَيرُ مُرَشَّح، وما يُمَسُّ وما لا يُمَسّ)؛
// وشَكلُ السكربتِ نَصّاً كَجارَيه (`StudioTierRepairTests`، `CommercialCategoryRepairTests`). والبُرهانُ الحَيُّ في رِسالَةِ الكوميت.

public class TenantPlanDayEndRepairTests
{
    private static TenantPlan Midnight(string status = PlatformPlanStatuses.Active) => new()
    {
        Id = "plan-demo",
        PlanId = "manual",
        Status = status,
        StartsAt = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
        ExpiresAt = new DateTime(2026, 11, 30, 0, 0, 0, DateTimeKind.Utc),
        GraceDays = 14,
        Price = 99m,
        SetBy = "studio · admin",
        SetAt = new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc),
        PayPalSubscriptionId = "I-SUB-42",
        PayPalApproveUrl = "https://www.paypal.com/webapps/billing/subscriptions?ba_token=BA-42",
        RenewalCancelledAt = new DateTime(2026, 10, 15, 7, 0, 0, DateTimeKind.Utc),
    };

    /// <summary>مُنتَصَفُ لَيلِ UTC ⇐ آخِرُ ذلكَ اليَومِ بِتَوقيتِ الرِياض — الدالَّةُ الَّتي يَقرَأُ بِها الحِفظُ اليَوم، لِلتاريخَينِ مَعاً.</summary>
    [Fact]
    public void A_midnight_plan_ends_at_the_last_moment_of_its_day_in_riyadh_and_its_start_moves_with_it()
    {
        var p = Midnight();
        Assert.True(TenantPlanDayEndRepair.IsCandidate(p));

        Assert.Equal(2, TenantPlanDayEndRepair.Apply(p));

        Assert.Equal(CultureTime.EndOfDayUtc(new DateOnly(2026, 11, 30)), p.ExpiresAt);
        Assert.Equal(new DateTime(2026, 11, 30, 20, 59, 59, DateTimeKind.Utc).AddTicks(9_999_999), p.ExpiresAt);
        Assert.Equal(CultureTime.EndOfDayUtc(new DateOnly(2026, 9, 1)), p.StartsAt);
        Assert.Equal(TimeSpan.FromDays(90), p.ExpiresAt - p.StartsAt);   // ‏أَيّامٌ كامِلَةٌ كَما يَكتُبُها الحِفظ
        // ‏التاسِعَةُ صَباحاً بِالرِياضِ مِن يَومِ الانتِهاءِ سارِيَةٌ الآن، وكانَت سَماحاً.
        Assert.Equal(TenantPlanState.Active, TenantPlanPolicy.Derive(p, new DateTime(2026, 11, 30, 6, 0, 0, DateTimeKind.Utc)));
        Assert.Equal(TenantPlanState.Grace, TenantPlanPolicy.Derive(p, p.ExpiresAt.AddTicks(1)));
    }

    /// <summary>والمَوقوفَةُ تَبقى مَوقوفَةً، ولا يُمَسُّ غَيرُ التاريخَين — والسَبَبُ كُلُّه هُنا: إعادَةُ الحِفظِ كانَت تُعيدُها سارِيَة.</summary>
    [Fact]
    public void A_stopped_plan_stays_stopped_and_nothing_but_the_two_dates_is_touched()
    {
        var p = Midnight(PlatformPlanStatuses.Stopped);
        var was = Midnight(PlatformPlanStatuses.Stopped);

        Assert.Equal(2, TenantPlanDayEndRepair.Apply(p));

        Assert.Equal(PlatformPlanStatuses.Stopped, p.Status);
        Assert.Equal(TenantPlanState.Suspended, TenantPlanPolicy.Derive(p, new DateTime(2026, 10, 30, 9, 0, 0, DateTimeKind.Utc)));
        Assert.Equal(was.PayPalSubscriptionId, p.PayPalSubscriptionId);
        Assert.Equal(was.PayPalApproveUrl, p.PayPalApproveUrl);
        Assert.Equal(was.RenewalCancelledAt, p.RenewalCancelledAt);
        Assert.Equal((was.Id, was.PlanId, was.GraceDays, was.Price, was.SetBy, was.SetAt),
                     (p.Id, p.PlanId, p.GraceDays, p.Price, p.SetBy, p.SetAt));
    }

    /// <summary>السالِب: باقَةٌ حُفِظَت بَعدَ د51 (آخِرُ اليَوم) أَو مَدَّدَها PayPal بِمَوعِدِه لا تُمَسّ — وإعادَةُ التَشغيلِ لا تَجِدُ شَيئاً.</summary>
    [Fact]
    public void A_plan_whose_expiry_is_not_midnight_is_untouched_and_a_second_run_finds_nothing()
    {
        var after = Midnight();
        after.StartsAt = CultureTime.EndOfDayUtc(new DateOnly(2026, 10, 29));
        after.ExpiresAt = CultureTime.EndOfDayUtc(new DateOnly(2026, 11, 28));
        var paypal = Midnight();
        paypal.ExpiresAt = new DateTime(2026, 12, 1, 10, 0, 0, DateTimeKind.Utc);   // ‏next_billing_time مِن PayPal

        foreach (var p in new[] { after, paypal })
        {
            var (starts, expires) = (p.StartsAt, p.ExpiresAt);
            Assert.False(TenantPlanDayEndRepair.IsCandidate(p));
            Assert.Equal(0, TenantPlanDayEndRepair.Apply(p));
            Assert.Equal((starts, expires), (p.StartsAt, p.ExpiresAt));
        }
        // ‏بَدءٌ وَحدَه عَلى مُنتَصَفِ اللَيلِ لا يُمَسّ: Derive لا يَقرَؤُه، ونَقلُه وَحدَه يُقَصِّرُ مُدَّةَ تَمديدِ PayPal.
        Assert.Equal(new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc), paypal.StartsAt);

        var once = Midnight();
        Assert.Equal(2, TenantPlanDayEndRepair.Apply(once));
        Assert.Equal(0, TenantPlanDayEndRepair.Apply(once));
        Assert.False(TenantPlanDayEndRepair.IsMidnightUtc(DateTime.MinValue));
    }

    /// <summary>وانتِهاءٌ عَلى مُنتَصَفِ اللَيلِ وبَدءٌ بِساعَتِه: يُكتَبُ الانتِهاءُ وَحدَه.</summary>
    [Fact]
    public void A_midnight_expiry_with_a_timed_start_rewrites_the_expiry_alone()
    {
        var p = Midnight();
        p.StartsAt = new DateTime(2026, 9, 1, 13, 45, 0, DateTimeKind.Utc);

        Assert.Equal(1, TenantPlanDayEndRepair.Apply(p));

        Assert.Equal(new DateTime(2026, 9, 1, 13, 45, 0, DateTimeKind.Utc), p.StartsAt);
        Assert.Equal(CultureTime.EndOfDayUtc(new DateOnly(2026, 11, 30)), p.ExpiresAt);
    }

    /// <summary>السكربتُ يَعرِضُ افتِراضاً، ولا يَكتُبُ إلّا بِـ<c>--apply</c> وعَدَدٍ يُطابِقُ العَرضَ وبَعدَ أَمرِ الاستِرجاع، ويَسأَلُ الدالَّةَ النَقِيَّة،
    /// ويَفشَلُ عَلى الصِفر، ولا يَمَسُّ المُخَطَّط، ولا يَبني باقَةً جَديدَةً ولا يُنادي بابَ التَعيين — ولا اسمَ مَتجَرٍ مُستَثنى.</summary>
    [Fact]
    public void The_repair_script_shows_by_default_and_rewrites_only_the_dates_on_a_matching_confirm()
    {
        var path = Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, "scripts", "repair-plan-day-end.cs");
        var text = File.ReadAllText(path);
        Assert.True(text.Length > 3000, $"أَداةٌ عَمياء: {path} طولُه {text.Length}.");

        foreach (var needle in new[]
                 {
                     "var apply      = args.Contains(\"--apply\");",
                     "if (!apply)",
                     "return 2;",
                     "if (confirm is null || confirm != plan.Count)",
                     "AutoCreateSchemaObjects = AutoCreate.None",
                     "✗ BLIND CHECK",
                     "DEPLOY.md §٤‑د",
                     "TenantPlanDayEndRepair.IsCandidate",
                     "TenantPlanDayEndRepair.Apply(live)",
                     "TenantPlanDayEndRepair.AuditAction",
                 })
            Assert.Contains(needle, text, StringComparison.Ordinal);

        // ‏ولا كِتابَةَ قَبلَ بَوّابَةِ التَأكيدِ ولا قَبلَ أَمرِ الاستِرجاع.
        var gate = text.IndexOf("if (confirm is null || confirm != plan.Count)", StringComparison.Ordinal);
        var restore = text.IndexOf("File.WriteAllTextAsync(sqlPath", StringComparison.Ordinal);
        var firstSave = text.IndexOf("SaveChangesAsync", StringComparison.Ordinal);
        Assert.True(firstSave > restore && restore > gate && gate > text.IndexOf("if (!apply)", StringComparison.Ordinal),
            "كِتابَةٌ قَبلَ بَوّابَةِ العَرضِ أَو التَأكيدِ أَو أَمرِ الاستِرجاع.");
        // ‏ولا يَبني باقَةً ولا يَمُرُّ بِالتَعيين (فَلا تَعودُ الحالَةُ سارِيَةً ولا يَضيعُ رَبطُ PayPal)، ولا يَكتُبُ الحالَة.
        Assert.DoesNotContain("new TenantPlan", text, StringComparison.Ordinal);
        Assert.DoesNotContain("SetAsync(", text, StringComparison.Ordinal);
        Assert.DoesNotContain(".Status =", text, StringComparison.Ordinal);
        Assert.DoesNotContain("\"ashare\"", text, StringComparison.Ordinal);
    }
}
