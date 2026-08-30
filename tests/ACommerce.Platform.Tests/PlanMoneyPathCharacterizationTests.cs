using ACommerce.Kit.Subscriptions;
using Xunit;

namespace ACommerce.Platform.Tests;

// ═══ تَوصيفُ مَسارِ المالِ اليَوم — قَبلَ أَن يُبَدَّلَ حَرف ═══
//
// **يُكتَب ويَخضَرّ ويُودَع وَحدَه، ثُمَّ لا يُمَسّ** (القاعِدَة ٣).
// فَمُرورُه بَعدَ إضافَةِ مَسارِ «الدَفعُ عِندَ المُزَوِّد» هُوَ بُرهانُ
// أَنّ **كُلَّ مَتجَرٍ قائِمٍ اليَوم لا يَتَغَيَّر سُلوكُه بِحَرف** —
// وكُلُّها اليَومَ بِـ`PaymentProviderConfigured == false`، إذ لَم يَكُن
// لِلحَقلِ كاتِبٌ واحِد.
//
// **ولِماذا هذا الجَدوَلُ بِالذاتِ خَطير**: هذِه الدالَّةُ هي الفَرقُ
// بَينَ «حِصَّةُ إعلاناتٍ تُمنَح بِنَقرَة» و«تُباع». وADR-002 وثَّقَ
// أَنّ النُقطَةَ كانَت تُحَمِّلُ الباقَةَ **وتَتَجاهَل `Price`**،
// فَتَفتَح الاشتِراكَ لِأَيّ داخِل. فَالجَدوَلُ أَدناه هُوَ الحارِسُ
// عَلى أَلّا يَعودَ ذلكَ مِن بابٍ آخَر.
public class PlanMoneyPathCharacterizationTests
{
    private static Plan At(decimal price) => new()
    {
        Id = "p", Name = "باقَة", Price = price, ListingsQuota = 5, DaysPeriod = 30,
    };

    // ─── ١. القَرارُ الذَرِّيّ ───────────────────────────────────────

    [Theory]
    [InlineData(0.01)]
    [InlineData(1)]
    [InlineData(50)]
    [InlineData(9999)]
    public void A_priced_plan_is_refused_while_the_store_has_no_payment_provider(decimal price)
    {
        Assert.False(PlanPurchasePolicy.IsPurchasable(At(price), false));
        Assert.Equal(PlanPurchasePolicy.PaidUnavailable,
            PlanPurchasePolicy.Refuse(At(price), false));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_free_plan_is_always_self_service(decimal price)
    {
        Assert.True(PlanPurchasePolicy.IsPurchasable(At(price), false));
        Assert.True(PlanPurchasePolicy.IsPurchasable(At(price), true));
        Assert.Null(PlanPurchasePolicy.Refuse(At(price), false));
        Assert.Null(PlanPurchasePolicy.Refuse(At(price), true));
    }

    [Fact]
    public void A_missing_plan_is_refused_by_its_own_code_either_way()
    {
        Assert.Equal(PlanPurchasePolicy.PlanNotFound, PlanPurchasePolicy.Refuse(null, false));
        Assert.Equal(PlanPurchasePolicy.PlanNotFound, PlanPurchasePolicy.Refuse(null, true));
    }

    [Fact]
    public void The_refusal_vocabulary_is_exactly_two_codes()
    {
        Assert.Equal(new[] { "plan_not_found", "plan_paid_unavailable" },
            PlanPurchasePolicy.Codes.ToArray());
    }

    // ─── ٢. ما يُرسَم في `/{slug}/plans` ─────────────────────────────

    [Fact]
    public void Without_a_provider_only_the_free_plans_are_drawn()
    {
        var all = new[] { At(0), At(50), At(120) };
        var visible = PlanPurchasePolicy.Visible(all, false);

        Assert.Single(visible);
        Assert.Equal(0m, visible[0].Price);
    }

    [Fact]
    public void With_a_provider_every_plan_is_drawn_by_the_same_reference()
    {
        var all = new[] { At(0), At(50) };
        Assert.Same(all, PlanPurchasePolicy.Visible(all, true));
    }

    [Fact]
    public void An_all_free_catalogue_is_returned_by_the_same_reference_either_way()
    {
        // تَكافُؤٌ صِفريٌّ بِالهُوِيَّة: بِلا باقَةٍ مَدفوعَةٍ لا تُبنى
        // قائِمَةٌ جَديدَةٌ أَصلاً.
        var all = new[] { At(0), At(0) };
        Assert.Same(all, PlanPurchasePolicy.Visible(all, false));
        Assert.Same(all, PlanPurchasePolicy.Visible(all, true));
    }

    [Fact]
    public void An_empty_catalogue_stays_empty_and_never_throws()
    {
        var none = Array.Empty<Plan>();
        Assert.Same(none, PlanPurchasePolicy.Visible(none, false));
        Assert.Same(none, PlanPurchasePolicy.Visible(none, true));
    }

    // ─── ٣. الباعِثُ واحِدٌ ولا يَتَعَدَّد ───────────────────────────

    [Fact]
    public void The_grant_lives_in_exactly_one_file_and_that_file_is_the_service()
    {
        // ‏`AppliedEventEmitterTests` يَحرُسُ هذا مِن جِهَةِ الأَحداث؛
        // ويُعادُ هُنا في إطارِ مَسارِ المال، لِأَنّ الباعِثَ الثانِيَ
        // لِـ`SubscriptionCreated` هُوَ بِعَينِه شَكلُ ثَغرَةِ ‏ADR-002.
        var root = ThemeZeroEquivalenceTests.RepoRoot;
        var emitters = Directory
            .EnumerateFiles(Path.Combine(root, "libs"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Replace('\\', '/').Contains("/obj/", StringComparison.Ordinal))
            .Where(f => !f.Replace('\\', '/').Contains("/bin/", StringComparison.Ordinal))
            .Where(f => File.ReadAllText(f).Contains("new SubscriptionCreated(", StringComparison.Ordinal))
            .Select(f => Path.GetFileName(f))
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(new[] { "PlanSubscribeService.cs" }, emitters);
    }
}
