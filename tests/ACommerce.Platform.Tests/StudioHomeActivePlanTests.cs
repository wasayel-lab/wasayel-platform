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

    /// <summary><b>اللافِتَةُ كَما كانَت، والسارِيَةُ سَطرٌ في صَفِّ مَتجَرِها</b> (‏2026-10-13 — انقَلَبَ بِقَرارٍ مَرئِيٍّ مِن
    /// <c>Today_an_active_plan_renders_nowhere_on_the_studio_home</c>، شَطرُه الأَخيرُ وَحدَه): اللافِتَةُ لِـ<c>Renewable</c> و
    /// <c>Renewable</c> لا يَعُدُّ <c>Active</c> — لَم يُمَسّ — وصَفُّ المَتجَرِ يَرسُمُ <c>ActivePlanLine</c> حَيثُ تُعيدُ سَطراً.</summary>
    [Fact]
    public void The_banner_is_unchanged_and_an_active_plan_renders_one_line_in_its_store_row()
    {
        var home = Home();
        Assert.Single(Regex.Matches(home, Regex.Escape("myApps.Where(a => Renewable(a.Slug))")));

        var renewable = Regex.Match(home, @"bool Renewable\(string slug\)\s*=>(?<body>[^;]*);");
        Assert.True(renewable.Success, "لا `Renewable` — الأَداةُ عَمياء.");
        Assert.Contains("TenantPlanState.Grace", renewable.Groups["body"].Value, StringComparison.Ordinal);
        Assert.Contains("TenantPlanState.Suspended", renewable.Groups["body"].Value, StringComparison.Ordinal);
        Assert.DoesNotContain("TenantPlanState.Active", renewable.Groups["body"].Value, StringComparison.Ordinal);

        var now = new DateTime(2026, 10, 13, 12, 0, 0, DateTimeKind.Utc);
        var active = new TenantPlan { Id = "probe", PlanId = "manual", StartsAt = now.AddDays(-1), ExpiresAt = now.AddDays(29) };
        Assert.Equal(TenantPlanState.Active, TenantPlanPolicy.Derive(active, now));

        var rows = AppRowsLoop();
        Assert.Contains("@if (ActivePlanLine(t.Slug) is { } planLine)", rows, StringComparison.Ordinal);
        Assert.Contains("@planLine", rows, StringComparison.Ordinal);
    }

    /// <summary><b>السَطرُ مِن بَياناتٍ مُخَزَّنَةٍ لا مِن رَقَمٍ مُختَرَع، ولِلسارِيَةِ وَحدَها</b>: الاسمُ <c>LabelAr</c> باقَةِ الكاتالوج
    /// (‏<c>manual</c> = «اشتِراك يَدَوِيّ» مِن مِلَفِّها)، والتاريخُ <c>ExpiresAt</c> الوَثيقَة، والقالِبُ مِفتاحٌ بِالعَرَبِيَّةِ بِوَسيطَين
    /// وبِلا رَقَم؛ ولا سَطرَ لِغَيرِ <c>Active</c>.</summary>
    [Fact]
    public void The_line_is_built_from_the_stored_plan_and_the_catalog_and_only_for_an_active_plan()
    {
        var home = Home();
        var fn = Regex.Match(home, @"string\? ActivePlanLine\(string slug\)\s*\{(?<body>.*?)\n    \}", RegexOptions.Singleline);
        Assert.True(fn.Success, "لا `ActivePlanLine` — الأَداةُ عَمياء.");
        var body = fn.Groups["body"].Value;
        Assert.Contains("if (PlanState(slug) != ACommerce.Kit.Subscriptions.TenantPlanState.Active) return null;", body, StringComparison.Ordinal);
        Assert.Contains("PlatformPlanCatalog.Find(p.PlanId)?.LabelAr", body, StringComparison.Ordinal);
        Assert.Contains("p.ExpiresAt.ToString(\"yyyy-MM-dd\")", body, StringComparison.Ordinal);
        Assert.Contains("L.Format(\"studio.plan.active_line\"", body, StringComparison.Ordinal);

        var template = ACommerce.Platform.I18n.LocaleCatalog.Find(ACommerce.Platform.I18n.LocaleCatalog.Arabic, "studio.plan.active_line");
        Assert.False(string.IsNullOrWhiteSpace(template));
        Assert.Contains("{0}", template, StringComparison.Ordinal);
        Assert.Contains("{1}", template, StringComparison.Ordinal);
        Assert.DoesNotMatch("[0-9٠-٩](?![}])", template.Replace("{0}", "").Replace("{1}", ""));
        // ‏(2026-10-25، تَدقيقُ الإطلاق brief-P2-4 — قَلبٌ مُعلَنٌ في a214307): «سارِيَةٌ حَتّى» صارَت «تَنتَهي في» تَسمِيَةَ المُشرِف.
        Assert.Equal("الباقَة: اشتِراك يَدَوِيّ — تَنتَهي في 2026-11-11",
            string.Format(template, PlatformPlanCatalog.Find("manual")!.LabelAr, new DateTime(2026, 11, 11).ToString("yyyy-MM-dd")));

        var now = new DateTime(2026, 10, 13, 12, 0, 0, DateTimeKind.Utc);
        foreach (var (plan, state) in new (TenantPlan? Plan, TenantPlanState State)[]
                 {
                     (null, TenantPlanState.None),
                     (new TenantPlan { PlanId = "manual", StartsAt = now.AddDays(-40), ExpiresAt = now.AddDays(-3), GraceDays = 14 }, TenantPlanState.Grace),
                     (new TenantPlan { PlanId = "manual", StartsAt = now.AddDays(-40), ExpiresAt = now.AddDays(-20), GraceDays = 14 }, TenantPlanState.Suspended),
                 })
            Assert.Equal(state, TenantPlanPolicy.Derive(plan, now));   // ‏الثَلاثُ الأُخرى لا تَعبُرُ شَرطَ السَطر
    }

    // ─── «سارِيَةٌ حَتّى X» وX يَومٌ لا تُقبَلُ فيه الكِتابَة (تَدقيقُ الإطلاق brief-P2-4، ‏2026-10-25) ─────────────────────────────
    //
    // **المَقيس** (‏2026-10-24 عِندَ `dace7cd`؛ أُعيدَ عِندَ `1e2cecf`): المُشرِفُ يَكتُبُ تاريخَ الانتِهاءِ يَوماً («2026-11-30») فَيُقرَأُ بِـ
    // `AssumeUniversal` مُنتَصَفَ لَيلِ ذلكَ اليَومِ UTC، و`Derive` سارِيَةٌ ما دامَ `now <= ExpiresAt` — فَمِن الدَقيقَةِ الأولى في X (الثالِثَةِ فَجراً
    // بِتَوقيتِ الرِياض) المَتجَرُ في السَماحِ ولا يَقبَلُ كِتابَة، والسَطرُ يَقولُ «سارِيَةٌ حَتّى X». يُثَبَّتُ «اليَوم» هُنا (القاعِدَة ٣) ويُقلَبُ بِاسمِه
    // في كوميتِ الصِياغَة؛ والصَفّانِ أَعلاه يَبقَيانِ إلّا السَطرَ الَّذي يُثَبِّتُ القالِبَ بِحَرفِه (يُقلَبُ مَعَه، مُعلَنٌ هُنا سَلَفاً).

    /// <summary>ما يَكتُبُه المُشرِفُ يَوماً يُقرَأُ مُنتَصَفَ لَيلِه UTC، وفي التاسِعَةِ صَباحاً بِتَوقيتِ الرِياضِ مِن ذلكَ اليَومِ لا كِتابَة.</summary>
    [Fact]
    public void An_expiry_day_written_by_the_admin_ends_the_plan_at_its_first_minute_utc()
    {
        var now = new DateTime(2026, 10, 25, 12, 0, 0, DateTimeKind.Utc);
        var f = TenantPlanPolicy.ReadSetting("manual", "2026-09-01", "2026-11-30", "14", "0", now);
        Assert.Equal(new DateTime(2026, 11, 30, 0, 0, 0, DateTimeKind.Utc), f.ExpiresAt);
        var plan = new TenantPlan { PlanId = f.PlanId, StartsAt = f.StartsAt, ExpiresAt = f.ExpiresAt, GraceDays = f.GraceDays };

        var nineRiyadh = new DateTime(2026, 11, 30, 6, 0, 0, DateTimeKind.Utc);     // ‏09:00 بِتَوقيتِ الرِياض (‏UTC+3)
        Assert.Equal(TenantPlanState.Grace, TenantPlanPolicy.Derive(plan, nineRiyadh));
        Assert.False(TenantPlanPolicy.AllowsWrite(TenantPlanPolicy.Derive(plan, nineRiyadh)));
        Assert.Equal(TenantPlanState.Active, TenantPlanPolicy.Derive(plan, new DateTime(2026, 11, 29, 20, 59, 0, DateTimeKind.Utc)));
        Assert.Equal("2026-11-30", plan.ExpiresAt.ToString("yyyy-MM-dd"));   // ‏ما يَطبَعُه السَطر
    }

    /// <summary><b>قُلِبَ «Today_» بِاسمِه (‏2026-10-25، كوميتُ الصِياغَة)</b> — كانَ
    /// <c>Today_the_active_line_says_valid_until_the_day_on_which_writes_are_already_refused</c> (‏<c>a214307</c>): «سارِيَةٌ حَتّى {1}». <b>والآن</b>
    /// «تَنتَهي في {1}» — تَسمِيَةُ صَفحَةِ المُشرِفِ (<c>admin.tenant_plan.expires_at</c>) بِحَرفِها، فَاليَومُ المَطبوعُ يَومُ الانتِهاءِ لا آخِرُ يَومٍ سارٍ،
    /// والتاريخُ نَفسُه (<c>ExpiresAt</c>) كَما كان؛ والفَرقُ في القالِبِ بَعدَ «الباقَة: {0} — » وَحدَه.</summary>
    [Fact]
    public void The_active_line_says_the_plan_ends_on_its_expiry_day_with_the_admin_label()
    {
        var template = ACommerce.Platform.I18n.LocaleCatalog.Find(ACommerce.Platform.I18n.LocaleCatalog.Arabic, "studio.plan.active_line")!;
        var label = ACommerce.Platform.I18n.LocaleCatalog.Find(ACommerce.Platform.I18n.LocaleCatalog.Arabic, "admin.tenant_plan.expires_at")!;
        Assert.Equal($"الباقَة: {{0}} — {label} {{1}}", template);
        Assert.DoesNotContain("حَتّى", template, StringComparison.Ordinal);
        Assert.StartsWith("الباقَة: {0} — ", template, StringComparison.Ordinal);   // ‏الصَدرُ كَما كانَ في a214307
    }
}
