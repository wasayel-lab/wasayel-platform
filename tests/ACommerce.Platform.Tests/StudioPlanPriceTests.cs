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

    [Fact]
    public void Today_the_agreed_store_price_is_read_only_by_the_admin_plan_page_and_never_by_the_studio_home()
    {
        var readers = PlanPriceReaders();
        output.WriteLine($"صَفَحاتٌ تَعرِفُ TenantPlan: {readers.Count} · {string.Join(" · ", readers.Select(r => $"{r.Key}={r.Value}"))}");

        Assert.True(readers.Count >= 3, $"أَداةٌ عَمياء: {readers.Count} صَفحَة.");
        Assert.Equal(0, readers["StudioHome.razor"]);
        Assert.Equal(new[] { "Admin/TenantPlanAdmin.razor" }, readers.Where(r => r.Value > 0).Select(r => r.Key).ToArray());
    }
}
