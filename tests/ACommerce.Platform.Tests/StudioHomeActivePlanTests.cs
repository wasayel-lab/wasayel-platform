using System.Text.RegularExpressions;
using ACommerce.Kit.Subscriptions;
using Xunit;

namespace ACommerce.Platform.Tests;

// ═══ رائِدُ الأَعمالِ لا يَرى باقَتَه السارِيَةَ ولا تاريخَ انتِهائِها (موجزُ الفجوات P2-4) ═══
//
// **المَقيس** (‏2026-10-13): `StudioHome.razor` يَرسُمُ لافِتَةَ الباقَةِ لِلمَتاجِرِ الَّتي `Renewable(slug)` وَحدَها —
// `PlanState is Grace or Suspended || PendingOrder is not null || PendingPaddle is not null` — فَالباقَةُ السارِيَةُ بِلا دَفعٍ
// مُعَلَّقٍ لا تُرسَمُ في أَيِّ مَوضِع، وصَفُّ المَتجَرِ في القائِمَةِ لا يَذكُرُ باقَتَه. و‏ADR-003 §٤ يَعِدُ: «‏/studio ← بِطاقَةُ «باقَتُك»
// بِحالَتِها وتاريخِ انتِهائِها».

public class StudioHomeActivePlanTests
{
    private static string Home()
    {
        var path = Path.Combine(ThemeZeroEquivalenceTests.RepoRoot,
            "libs", "templates", "ACommerce.Templates.Customer.Marketplace", "Components", "Pages", "StudioHome.razor");
        var text = File.ReadAllText(path);
        Assert.True(text.Length > 2000, $"أَداةٌ عَمياء: {path} لَم يُقرَأ.");
        return text;
    }

    /// <summary>جِسمُ حَلقَةِ صُفوفِ المَتاجِر — مِن <c>&lt;div class="studio-list"&gt;</c> الأَوَّلِ إلى إغلاقِ حَلقَتِه.</summary>
    private static string AppRowsLoop()
    {
        var home = Home();
        var at = home.IndexOf("<div class=\"studio-list\">", StringComparison.Ordinal);
        Assert.True(at > 0, "لا قائِمَةَ مَتاجِر — الأَداةُ عَمياء.");
        var loop = home.IndexOf("@foreach (var t in myApps)", at, StringComparison.Ordinal);
        Assert.True(loop > at, "لا حَلقَةَ صُفوفٍ لِلمَتاجِر — الأَداةُ عَمياء.");
        var open = home.IndexOf('{', loop);
        var depth = 0;
        for (var i = open; i < home.Length; i++)
        {
            if (home[i] == '{') depth++;
            else if (home[i] == '}' && --depth == 0) return home[open..(i + 1)];
        }
        Assert.Fail("حَلقَةٌ بِلا قَوسٍ مُقابِل.");
        return "";
    }

    /// <summary><b>اليَوم: الباقَةُ السارِيَةُ لا تُرسَم</b> — اللافِتَةُ لِـ<c>Renewable</c> وَحدَه، و<c>Renewable</c> لا يَعُدُّ
    /// <c>Active</c>، وصَفُّ المَتجَرِ لا يَقرَأُ الباقَة. (‏تَوصيفٌ يَنقَلِبُ شَطرُه الثالِثُ بِاسمِه في كوميتِ السَطر.)</summary>
    [Fact]
    public void Today_an_active_plan_renders_nowhere_on_the_studio_home()
    {
        var home = Home();
        Assert.Equal(1, Regex.Matches(home, Regex.Escape("myApps.Where(a => Renewable(a.Slug))")).Count);

        var renewable = Regex.Match(home, @"bool Renewable\(string slug\)\s*=>(?<body>[^;]*);");
        Assert.True(renewable.Success, "لا `Renewable` — الأَداةُ عَمياء.");
        Assert.Contains("TenantPlanState.Grace", renewable.Groups["body"].Value, StringComparison.Ordinal);
        Assert.Contains("TenantPlanState.Suspended", renewable.Groups["body"].Value, StringComparison.Ordinal);
        Assert.DoesNotContain("TenantPlanState.Active", renewable.Groups["body"].Value, StringComparison.Ordinal);

        var now = new DateTime(2026, 10, 13, 12, 0, 0, DateTimeKind.Utc);
        var active = new TenantPlan { Id = "probe", PlanId = "manual", StartsAt = now.AddDays(-1), ExpiresAt = now.AddDays(29) };
        Assert.Equal(TenantPlanState.Active, TenantPlanPolicy.Derive(active, now));

        var rows = AppRowsLoop();
        Assert.DoesNotContain("PlanState(", rows, StringComparison.Ordinal);
        Assert.DoesNotContain("ExpiresAt", rows, StringComparison.Ordinal);
    }
}
