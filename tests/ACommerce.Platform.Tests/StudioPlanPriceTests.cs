using System.Text.RegularExpressions;
using ACommerce.Platform.I18n;
using ACommerce.Templates.Customer.Marketplace.Services.Incubator;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ البَندُ ٤ مِن مُوافَقَةِ الاستوديو يَعِدُ بِأَنّ اشتِراكَ المَتجَرِ «يُعرَضُ لَكَ في الاستوديو قَبلَ أَيِّ دَفع» — والسِعرُ لا يُعرَضُ لِرائِدِ الأَعمال ═══
//
// تَدقيقُ الإطلاق ‏brief-P2-1 (كُبرى، ‏2026-10-24 عِندَ `dace7cd`؛ أُعيدَ قِياسُه عِندَ `85ee4d1`): `TenantPlan.Price` — «السِعرُ المُتَّفَقُ عَلَيه»
// يَملَؤُه المُشرِفُ لِكُلِّ مَتجَر — لا يُقرَأُ إلّا في `Admin/TenantPlanAdmin.razor`؛ و`StudioHome.razor` بِصِفرِ `.Price`، فَسَطرُ الباقَةِ السارِيَةِ
// اسمٌ وتاريخ، ولافِتَةُ التَجديدِ حالٌ وتاريخُ إخفاءٍ وتَعليماتُ تَحويلٍ عامَّةٌ لِلمَنَصَّة، وزِرُّ PayPal بِلا مَبلَغ. **ومَقيسٌ حَيّاً** (قاعِدَةٌ
// مَحَلِّيَّة `wasayel_w84`، مُشرِفُ المَنَصَّةِ مالِكُ `owner-test`): `/studio` بِباقَةٍ سارِيَةٍ سِعرُها 0 وبِسِعرِ 150 بايتاً بِبايتٍ واحِدَة (‏39487)،
// وكَذلِكَ في السَماح (‏40103) — السِعرُ لا يَصِلُ الصَفحَةَ في أَيِّ حال.
//
// **يُثَبَّتُ قَبلَ التَبديلِ** (القاعِدَة ٣): الصَفُّ الأَوَّلُ (الوَعدُ بِحَرفِه وإصدارُ المُوافَقَة) يَبقى بِلا تَعديلِ حَرف — فَالعِلاجُ
// الافتِراضِيُّ عَرضُ السِعرِ لا إعادَةُ صِياغَةِ البَند (قَرارُ المالِكِ «ب» W8-4 · ٣)؛ والثاني (`Today_…`) يَنقَلِبُ بِاسمِه في كوميتِ العِلاج.
public class StudioPlanPriceTests(ITestOutputHelper output)
{
    internal const string ConsentItem4Key = "studio.consent.item4_body";
    internal const string ShownBeforePayment = "ويُعرَضُ لَكَ في الاستوديو قَبلَ أَيِّ دَفع";

    private static string PagesDir => Path.Combine(ThemeZeroEquivalenceTests.RepoRoot,
        "libs", "templates", "ACommerce.Templates.Customer.Marketplace", "Components", "Pages");

    /// <summary>كُلُّ صَفحَةٍ تَعرِفُ <c>TenantPlan</c> — وعَدَدُ قِراءاتِ <c>.Price</c> فيها (الكودُ لا التَعليق).</summary>
    internal static IReadOnlyDictionary<string, int> PlanPriceReaders()
        => Directory.GetFiles(PagesDir, "*.razor", SearchOption.AllDirectories)
            .Select(f => (File: Path.GetRelativePath(PagesDir, f).Replace('\\', '/'),
                          Code: WriteEndpointGuardTests.StripComments(WriteEndpointGuardTests.StripMarkupComments(File.ReadAllText(f)))))
            .Where(x => Regex.IsMatch(x.Code, @"\bTenantPlan\b"))
            .ToDictionary(x => x.File, x => Regex.Matches(x.Code, @"\.Price\b").Count, StringComparer.Ordinal);

    [Fact]
    public void The_consent_promises_the_store_subscription_is_shown_in_the_studio_before_any_payment_at_version_two()
    {
        var item4 = LocaleCatalog.Find(LocaleCatalog.Arabic, ConsentItem4Key);
        output.WriteLine($"{ConsentItem4Key}: {item4} · ConsentPolicy.CurrentVersion = {ConsentPolicy.CurrentVersion}");
        Assert.Contains(ShownBeforePayment, item4, StringComparison.Ordinal);
        Assert.Equal(2, ConsentPolicy.CurrentVersion);
    }

    /// <summary>كُتلَةُ النَصِّ مِن <paramref name="marker"/> إلى إغلاقِ الكُتلَةِ الَّتي تَليه — بِمُوازَنَةِ الأَقواس.</summary>
    private static string BlockAfter(string code, string marker)
    {
        var at = code.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(at > 0, $"لا «{marker}» — الأَداةُ عَمياء.");
        var open = code.IndexOf('{', at + marker.Length);
        var depth = 0;
        for (var i = open; i < code.Length; i++)
        {
            if (code[i] == '{') depth++;
            else if (code[i] == '}' && --depth == 0) return code[open..(i + 1)];
        }
        Assert.Fail($"«{marker}» بِلا قَوسٍ مُقابِل.");
        return "";
    }

    /// <summary><b>قُلِبَ «Today_» بِاسمِه (‏2026-10-25، كوميتُ العِلاج — الافتِراضُ الآمِنُ مِن brief-P2-1)</b> — كانَ
    /// <c>Today_the_agreed_store_price_is_read_only_by_the_admin_plan_page_and_never_by_the_studio_home</c> (‏<c>92cdeae</c>): قارِئُ
    /// <c>.Price</c> صَفحَةُ المُشرِفِ وَحدَها و<c>StudioHome.razor</c> ‏0. <b>والآن</b> يَقرَؤُه <c>StudioHome</c> مَرَّةً واحِدَةً عَبرَ
    /// <c>TenantPlanPolicy.ShownPrice</c> في <c>PriceLine</c>، ويُرسَمُ <c>PriceLine</c> في المَوضِعَينِ اللَّذَينِ يَقولُهُما البَند: لافِتَةِ
    /// التَجديدِ قَبلَ أَزرارِ الدَفع، وسَطرِ السارِيَة — ومَقيسٌ حَيّاً: بِسِعرِ 0 الصَفحَةُ بايتاً كَما كانَت، وبِسِعرِ 150 عُنصُرٌ واحِدٌ زائِد.</summary>
    [Fact]
    public void The_agreed_store_price_is_read_by_the_studio_banner_before_payment_and_by_the_active_row_through_one_policy()
    {
        var readers = PlanPriceReaders();
        output.WriteLine($"صَفَحاتٌ تَعرِفُ TenantPlan: {readers.Count} · {string.Join(" · ", readers.Select(r => $"{r.Key}={r.Value}"))}");
        Assert.True(readers.Count >= 3, $"أَداةٌ عَمياء: {readers.Count} صَفحَة.");
        Assert.Equal(new[] { "Admin/TenantPlanAdmin.razor" }, readers.Where(r => r.Value > 0).Select(r => r.Key).ToArray());
        Assert.Equal(0, readers["StudioHome.razor"]);   // ‏لا قِراءَةَ مُباشِرَة: السِعرُ يَمُرُّ بِالسِياسَةِ وَحدَها

        var home = UpgradePromptPriceTests.Code("libs", "templates", "ACommerce.Templates.Customer.Marketplace",
            "Components", "Pages", "StudioHome.razor");
        var fn = Regex.Match(home, @"string\? PriceLine\(string slug\)\s*\{(?<body>.*?)\n    \}", RegexOptions.Singleline);
        Assert.True(fn.Success, "لا `PriceLine` — الأَداةُ عَمياء.");
        Assert.Contains("TenantPlanPolicy.ShownPrice(p) is { } price", fn.Groups["body"].Value, StringComparison.Ordinal);
        Assert.Contains("L.Format(\"studio.plan.price_line\", ACommerce.Kit.Culture.Money.Format(price))", fn.Groups["body"].Value, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(home, @"ShownPrice\("));

        // المَوضِعُ الأَوَّل: اللافِتَة — بَعدَ تاريخِ الإخفاءِ وقَبلَ التَعليماتِ وأَيِّ زِرِّ دَفع.
        var banner = BlockAfter(home, "@foreach (var t in myApps.Where(a => Renewable(a.Slug)))");
        var price = banner.IndexOf("@if (PriceLine(t.Slug) is { } priceLine)", StringComparison.Ordinal);
        var firstPay = new[] { "studio.plan.transfer_missing", "studio.plan.paddle_pay", "studio.plan.paypal_order", "studio.plan.paypal_pay" }
            .Select(k => banner.IndexOf(k, StringComparison.Ordinal)).Where(i => i >= 0).ToList();
        Assert.True(price > 0 && firstPay.Count == 4 && firstPay.All(i => i > price),
            $"السِعرُ في اللافِتَةِ عِندَ {price}، وأَوَّلُ مَوضِعِ دَفعٍ {(firstPay.Count == 0 ? -1 : firstPay.Min())}.");

        // والثاني: سَطرُ السارِيَةِ في صَفِّ المَتجَر.
        var row = BlockAfter(home, "@if (ActivePlanLine(t.Slug) is { } planLine)");
        Assert.Contains("@if (PriceLine(t.Slug) is { } rowPrice)", row, StringComparison.Ordinal);
        Assert.Equal(2, Regex.Matches(home, Regex.Escape("PriceLine(t.Slug)")).Count);

        // والوَعدُ لَم يُمَسّ — وصارَ صادِقاً لِما سَجَّلَه المُشرِف.
        Assert.Contains(ShownBeforePayment, LocaleCatalog.Find(LocaleCatalog.Arabic, ConsentItem4Key), StringComparison.Ordinal);
    }

    // ─── السِياسَة — موجِبٌ وسالِب ───────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(150.0, 150.0)]
    [InlineData(0.01, 0.01)]
    public void A_recorded_price_above_zero_is_shown(double stored, double shown)
        => Assert.Equal((decimal)shown, ACommerce.Kit.Subscriptions.TenantPlanPolicy.ShownPrice(
            new ACommerce.Kit.Subscriptions.TenantPlan { PlanId = "manual", Price = (decimal)stored }));

    [Theory]
    [InlineData(0.0)]
    [InlineData(-5.0)]
    public void An_unrecorded_or_invalid_price_is_not_shown(double stored)
        => Assert.Null(ACommerce.Kit.Subscriptions.TenantPlanPolicy.ShownPrice(
            new ACommerce.Kit.Subscriptions.TenantPlan { PlanId = "manual", Price = (decimal)stored }));

    [Fact]
    public void No_plan_shows_no_price()
        => Assert.Null(ACommerce.Kit.Subscriptions.TenantPlanPolicy.ShownPrice(null));

    /// <summary>القالِبُ بِالعَرَبِيَّةِ بِوَسيطٍ واحِدٍ وبِلا رَقَم — وتَسمِيَتُه تَسمِيَةُ حَقلِ المُشرِفِ نَفسِها («السِعرُ المُتَّفَقُ عَلَيه»)، فَلا
    /// صِياغَةَ جَديدَة؛ والمَبلَغُ مِن <c>Money.Format</c> لا مِن القامُوس.</summary>
    [Fact]
    public void The_price_line_names_the_admin_field_and_carries_no_number()
    {
        var template = LocaleCatalog.Find(LocaleCatalog.Arabic, "studio.plan.price_line");
        Assert.Equal(LocaleCatalog.Find(LocaleCatalog.Arabic, "admin.tenant_plan.field_price") + ": {0}", template);
        Assert.DoesNotMatch("[0-9٠-٩](?![}])", template!.Replace("{0}", ""));
        Assert.Equal("السِعرُ المُتَّفَقُ عَلَيه: 150 ر.س", string.Format(template, ACommerce.Kit.Culture.Money.Format(150m)));
    }

    // ─── السَطرُ المَرسومُ لِسِعرٍ بِهَلَلات ────────────────────────────────────────────────────────────────
    //
    // مُراجَعَةُ تَيّارِ W8-4 (‏2026-10-25، كُبرى، عَلى `eecbb5e`): حَقلُ «السِعرُ المُتَّفَقُ عَلَيه» في `Admin/TenantPlanAdmin.razor` يَقبَلُ
    // الهَلَلات (`step="0.01"`، و`ReadSetting` يَقرَأُ أَيَّ `decimal`)، و`Money.Format` يَكتُبُ `N0` — فَالسَطرُ يَطبَعُ رَقَماً غَيرَ المُسَجَّل.
    // **ومَقيسٌ حَيّاً** (`wasayel_w84`، `POST /admin/tenants/owner-test/plan/save` ثُمَّ `/studio`): سارِيَةٌ بِـ149.99 بايتاً كَسارِيَةٍ بِـ150
    // (‏39701)، وبِـ0.01 «‏0 ر.س» (‏39699)، والسَماحُ بِـ149.99 بايتاً كَالسَماحِ بِـ150 (‏40362).

    /// <summary>السَطرُ كَما يَبنيه <c>PriceLine</c>: قالِبُ القامُوسِ بِـ<b>المُنَسِّقِ الَّذي يُسَمّيه جِسمُها</b> — يُقرَأُ اسمُه مِن الكودِ ويُستَدعى
    /// بِالانعِكاس، فَلا يَختارُ الاختِبارُ مُنَسِّقاً غَيرَ الَّذي تَرسُمُه الصَفحَة.</summary>
    internal static string RenderedPriceLine(decimal stored)
    {
        var home = UpgradePromptPriceTests.Code("libs", "templates", "ACommerce.Templates.Customer.Marketplace",
            "Components", "Pages", "StudioHome.razor");
        var body = Regex.Match(home, @"string\? PriceLine\(string slug\)\s*\{(?<body>.*?)\n    \}", RegexOptions.Singleline);
        Assert.True(body.Success, "لا `PriceLine` — الأَداةُ عَمياء.");
        var formatter = Regex.Match(body.Groups["body"].Value, @"ACommerce\.Kit\.Culture\.Money\.(?<name>\w+)\(price\)");
        Assert.True(formatter.Success, "`PriceLine` لا تُسَمّي مُنَسِّقاً مِن `Money` — الأَداةُ عَمياء.");
        var method = typeof(ACommerce.Kit.Culture.Money).GetMethod(formatter.Groups["name"].Value,
            new[] { typeof(decimal), typeof(string), typeof(string), typeof(ACommerce.Kit.Culture.MoneyUnitStyle) });
        Assert.NotNull(method);

        var shown = ACommerce.Kit.Subscriptions.TenantPlanPolicy.ShownPrice(
            new ACommerce.Kit.Subscriptions.TenantPlan { PlanId = "manual", Price = stored });
        Assert.NotNull(shown);
        var amount = (string)method!.Invoke(null, new object?[] { shown!.Value, null, null, ACommerce.Kit.Culture.MoneyUnitStyle.Symbol })!;
        return string.Format(LocaleCatalog.Find(LocaleCatalog.Arabic, "studio.plan.price_line")!, amount);
    }

    /// <summary><b>تَوصيفُ «اليَوم»</b> (القاعِدَة ٣): سِعرٌ مُسَجَّلٌ بِهَلَلاتٍ يُطبَعُ مُقَرَّباً إلى ريالٍ صَحيح — 149.99 «‏150 ر.س» و0.01
    /// «‏0 ر.س»، وهو ما تَقولُ <c>ShownPrice</c> إنّه لا يُطبَع. يُقلَبُ بِاسمِه في كوميتِ العِلاج، ومَعَه سَطرُ <c>Money.Format(price)</c> في
    /// <c>The_agreed_store_price_is_read_by_the_studio_banner_…</c> (مُعلَنٌ هُنا سَلَفاً).</summary>
    [Theory]
    [InlineData("149.99", "150")]
    [InlineData("0.01", "0")]
    public void Today_a_recorded_price_with_halalas_is_printed_rounded_to_a_whole_riyal(string stored, string printed)
    {
        var line = RenderedPriceLine(decimal.Parse(stored, System.Globalization.CultureInfo.InvariantCulture));
        output.WriteLine($"المُسَجَّل {stored} ⟵ «{line}»");
        Assert.Equal($"السِعرُ المُتَّفَقُ عَلَيه: {printed} ر.س", line);
    }
}
