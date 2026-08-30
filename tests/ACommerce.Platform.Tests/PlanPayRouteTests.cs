using System.Text.RegularExpressions;
using ACommerce.Kit.Subscriptions;
using Xunit;

namespace ACommerce.Platform.Tests;

// ═══ المَخرَجُ الثالِث — «ادفَع عِندَ مُزَوِّدِ التاجِر» ═══
//
// **والدَعوى المَركَزِيَّة الَّتي يَحرُسُها هذا المِلَفّ**: فَتحُ
// `PaymentProviderConfigured` بِكاتِبٍ حَقيقيّ **لا يَفتَح مَنحاً
// مَجّانِيّاً لِباقَةٍ بِسِعر**. وثَغرَةُ ‏ADR-002 كانَت بِالضَبط هذا:
// النُقطَةُ تُحَمِّل الباقَةَ وتَتَجاهَل `Price` فَتَمنَح الحِصَّةَ
// بِنَقرَة.
public class PlanPayRouteTests
{
    private static Plan At(decimal price) => new()
    {
        Id = "p", Name = "باقَة", Price = price, ListingsQuota = 5, DaysPeriod = 30,
    };

    // ─── ١. التَكافُؤُ الصِفريّ: بِلا مُزَوِّدٍ لا شَيءَ تَغَيَّر ────

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(0.01)]
    [InlineData(50)]
    [InlineData(9999)]
    public void Without_a_provider_the_new_decision_matches_the_old_refusal_exactly(decimal price)
    {
        var (route, refusal) = PlanPurchasePolicy.Decide(At(price), false);

        Assert.Equal(PlanPurchasePolicy.Refuse(At(price), false), refusal);
        Assert.Equal(
            PlanPurchasePolicy.IsPurchasable(At(price), false)
                ? PlanPurchasePolicy.PlanPurchaseRoute.Grant
                : PlanPurchasePolicy.PlanPurchaseRoute.Refuse,
            route);

        // ولا مَخرَجَ ثالِثَ يُبلَغ بِلا مُزَوِّد.
        Assert.NotEqual(PlanPurchasePolicy.PlanPurchaseRoute.PayAtProvider, route);
    }

    [Fact]
    public void A_missing_plan_is_refused_by_the_same_code_either_way()
    {
        Assert.Equal((PlanPurchasePolicy.PlanPurchaseRoute.Refuse, PlanPurchasePolicy.PlanNotFound),
            PlanPurchasePolicy.Decide(null, false));
        Assert.Equal((PlanPurchasePolicy.PlanPurchaseRoute.Refuse, PlanPurchasePolicy.PlanNotFound),
            PlanPurchasePolicy.Decide(null, true));
    }

    // ─── ٢. مَعَ مُزَوِّد: تُدفَع، ولا تُمنَح ───────────────────────

    [Theory]
    [InlineData(0.01)]
    [InlineData(50)]
    [InlineData(9999)]
    public void With_a_provider_a_priced_plan_goes_to_the_provider_and_is_never_granted(decimal price)
    {
        var (route, refusal) = PlanPurchasePolicy.Decide(At(price), true);

        Assert.Equal(PlanPurchasePolicy.PlanPurchaseRoute.PayAtProvider, route);
        Assert.Null(refusal);

        // **وهذا هُوَ الحارِس**: مَعَ مُزَوِّدٍ أَو بِدونِه، الباقَةُ
        // بِسِعرٍ لا تُمنَح ذاتِيّاً أَبَداً.
        Assert.NotEqual(PlanPurchasePolicy.PlanPurchaseRoute.Grant, route);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void With_a_provider_a_free_plan_is_still_granted_on_the_spot(decimal price)
    {
        var (route, refusal) = PlanPurchasePolicy.Decide(At(price), true);
        Assert.Equal(PlanPurchasePolicy.PlanPurchaseRoute.Grant, route);
        Assert.Null(refusal);
    }

    [Fact]
    public void Grant_is_the_only_route_that_may_open_a_subscription()
    {
        var outcomes = new[]
        {
            PlanPurchasePolicy.Decide(At(0), false),
            PlanPurchasePolicy.Decide(At(0), true),
            PlanPurchasePolicy.Decide(At(50), false),
            PlanPurchasePolicy.Decide(At(50), true),
            PlanPurchasePolicy.Decide(null, false),
        };

        var granting = outcomes
            .Where(o => o.Route == PlanPurchasePolicy.PlanPurchaseRoute.Grant).ToArray();

        Assert.Equal(2, granting.Length);          // المَجّانِيَّتانِ وَحدَهُما
        Assert.All(granting, o => Assert.Null(o.Refusal));
    }

    // ─── ٣. النُقطَةُ تَتبَع القَرار، ولا تَحفَظ عِندَ التَحويل ─────

    /// <summary><b>يُقاسُ نَصّاً لِأَنّ الترتيبَ هُوَ الدَعوى</b>:
    /// <c>SaveChangesAsync</c> يَجِب أَن يَقَعَ <b>بَعدَ</b> رَدِّ
    /// «ادفَع عِندَ المُزَوِّد» — فَالتَحويلُ يَخرُج ولا يُودِع. ولَو
    /// انقَلَبا لَفُتِحَ اشتِراكٌ بِلا قَبض.</summary>
    [Fact]
    public void The_subscribe_endpoint_returns_before_it_saves_when_paying_externally()
    {
        var text = File.ReadAllText(Path.Combine(
            ThemeZeroEquivalenceTests.RepoRoot, "libs", "templates",
            "ACommerce.Templates.Customer.Marketplace", "MarketplaceTemplateExtensions.cs"));

        var body = Regex.Match(text,
            @"MapPost\(""/\{slug\}/plans/\{planId\}/subscribe"".*?RequireStoreWritable\(\);",
            RegexOptions.Singleline);

        Assert.True(body.Success, "أَداة عَمياء: لَم يُعثَر عَلى جِسمِ نُقطَةِ الاشتِراك.");

        var payAt = body.Value.IndexOf("outcome.PayAtProvider", StringComparison.Ordinal);
        var saveAt = body.Value.IndexOf("SaveChangesAsync", StringComparison.Ordinal);

        Assert.True(payAt > 0, "النُقطَةُ لا تَعرِف المَخرَجَ الثالِثَ أَصلاً.");
        Assert.True(saveAt > 0, "أَداة عَمياء: لا حِفظَ في الجِسم.");
        Assert.True(payAt < saveAt,
            "التَحويلُ إلى صَفحَةِ الدَفعِ يَقَع بَعدَ الحِفظ — فَيُفتَح اشتِراكٌ بِلا قَبض.");
    }

    // ─── ٤. صَفحَةُ الدَفعِ تُبلَغ، ولا تَقبِض ──────────────────────

    [Fact]
    public void The_pay_page_exists_renders_the_stored_link_and_opens_no_subscription()
    {
        var path = Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, "libs", "templates",
            "ACommerce.Templates.Customer.Marketplace", "Components", "Pages", "PlanPay.razor");

        Assert.True(File.Exists(path), "صَفحَةُ الدَفعِ غَيرُ مَوجودَة — والتَحويلُ إلَيها يَتيه.");
        var text = File.ReadAllText(path);

        // المَسارُ الَّذي تُحَوِّل إلَيه النُقطَة، بِفَرعَيه.
        Assert.Contains("@page \"/{slug}/plans/{planId}/pay\"", text, StringComparison.Ordinal);
        Assert.Contains("@page \"/{slug}/r/{role}/plans/{planId}/pay\"", text, StringComparison.Ordinal);

        // تُصَيِّرُ الرابِطَ المُخَزَّنَ لا رابِطاً مَكتوباً بِاليَد.
        Assert.Contains("PaymentLink", text, StringComparison.Ordinal);
        Assert.DoesNotContain("moyasar.com", text, StringComparison.Ordinal);

        // ولا تَقبِض ولا تَكتُب: صِفرُ حَدَثٍ وصِفرُ جَلسَة.
        foreach (var w in new[] { "SaveChangesAsync", "Events.Append", "Events.StartStream", "Session(" })
            Assert.DoesNotContain(w, text, StringComparison.Ordinal);
    }
}
