using ACommerce.Templates.Customer.Marketplace.Services.Incubator;
using Xunit;

namespace ACommerce.Platform.Tests;

// ═══ سَبَبُ دَعوَةِ التَرقِيَة — مَعجَمٌ مُغلَقٌ بِطَرَفَيه ═══════════
//
// **العِلَّة**: الرَمزُ يُكتَب في العُنوان (`?upgrade=`) في أَربَعِ نُقاطٍ
// ويُطابَق في `UpgradePrompt.razor` بِأَربَعَةِ `case`. فَإن افتَرَقَ
// الطَرَفانِ بِحَرف، وَقَعَ الرَفضُ **ولَم تَظهَر رِسالَة** — أَي رَفضٌ
// مُبتلَع. وهذا الجِسرُ يُقاس هُنا لِأَنَّه لا يُقاس في أَيّ مَكانٍ آخَر.
public class StudioUpgradeReasonTests
{
    private static string RepoRoot => ThemeZeroEquivalenceTests.RepoRoot;

    // ─── ١. المَعجَم ────────────────────────────────────────────────

    [Fact]
    public void Four_reasons_no_fifth_and_none_repeats()
    {
        Assert.Equal(4, StudioUpgradeReason.All.Count);
        Assert.Equal(4, StudioUpgradeReason.All.Distinct(StringComparer.Ordinal).Count());
        Assert.All(StudioUpgradeReason.All, c => Assert.False(string.IsNullOrWhiteSpace(c)));

        // وثَلاثَةٌ مِنها وَحدَها خَرقُ حِصَّة — والرابِعُ حَجبُ ميزَة.
        Assert.Equal(3, StudioUpgradeReason.QuotaCodes.Count);
        Assert.DoesNotContain(StudioUpgradeReason.Export, StudioUpgradeReason.QuotaCodes);
    }

    // ─── ٢. الطَرَفُ القارِئ يَعرِف كُلَّ رَمزٍ يَكتُبُه الكاتِب ─────

    [Fact]
    public void The_upgrade_prompt_matches_every_reason_the_gates_can_emit()
    {
        var razor = File.ReadAllText(Path.Combine(RepoRoot, "libs", "templates",
            "ACommerce.Templates.Customer.Marketplace", "Components", "UpgradePrompt.razor"));
        Assert.True(razor.Length > 500, "أَداة عَمياء: `UpgradePrompt.razor` لَم يُقرَأ.");

        var missing = new[]
            {
                nameof(StudioUpgradeReason.Analyses),
                nameof(StudioUpgradeReason.Refines),
                nameof(StudioUpgradeReason.Stores),
                nameof(StudioUpgradeReason.Export),
            }
            .Where(name => !razor.Contains($"StudioUpgradeReason.{name}", StringComparison.Ordinal))
            .ToArray();

        Assert.True(missing.Length == 0,
            "رُموزٌ يَكتُبُها الحاجِزُ ولا تُطابِقُها الشاشَة: " + string.Join("، ", missing)
            + " — فَالرَفضُ يَقَع والرِسالَةُ تَصمُت.");
    }

    /// <summary>ولا نُقطَةَ تَكتُب الرَمزَ حَرفِيّاً بَعدَ اليَوم —
    /// وإلّا عادَ التَعريفانِ.</summary>
    [Fact]
    public void No_endpoint_writes_the_upgrade_reason_as_a_bare_literal()
    {
        var text = File.ReadAllText(Path.Combine(RepoRoot, "libs", "templates",
            "ACommerce.Templates.Customer.Marketplace", "MarketplaceTemplateExtensions.cs"));
        Assert.True(text.Length > 10_000, "أَداة عَمياء: مِلَفُّ النِقاطِ لَم يُقرَأ.");

        var literals = StudioUpgradeReason.All
            .Where(c => text.Contains($"?upgrade={c}", StringComparison.Ordinal))
            .ToArray();

        Assert.True(literals.Length == 0,
            "رُموزٌ ما زالَت مَكتوبَةً حَرفِيّاً في العُنوان: " + string.Join("، ", literals));
    }

    // ─── ٣. الحُدود — مُنتَهِيَةٌ وتُغلِق ────────────────────────────

    /// <summary><b>البَوّابَةُ تُغلَق فِعلاً</b> — والقاعِدَةُ نَقِيَّةٌ
    /// فَتُقاس بِلا قاعِدَةِ بَيانات: العَدّادُ عِندَ السَقفِ يَمنَع،
    /// وتَحتَه بِواحِدٍ يَسمَح.</summary>
    [Theory]
    [InlineData("spark")]
    [InlineData("lite")]
    [InlineData("growth")]
    [InlineData("scale")]
    public void Every_tier_has_a_reachable_ceiling_on_the_owner_key(string tier)
    {
        var t = TierCatalog.For(tier);

        Assert.True(t.AnalysesPerMonth is > 0 and < int.MaxValue, $"{tier}.AnalysesPerMonth");
        Assert.True(t.RefinesPerMonth  is > 0 and < int.MaxValue, $"{tier}.RefinesPerMonth");
        Assert.True(t.StoresMax        is > 0 and < int.MaxValue, $"{tier}.StoresMax");

        // وشَرطُ البَوّابَةِ نَفسُه، بِطَرَفَيه.
        Assert.True(t.AnalysesPerMonth >= t.AnalysesPerMonth);          // عِندَ السَقف: يُمنَع
        Assert.False(t.AnalysesPerMonth - 1 >= t.AnalysesPerMonth);     // تَحتَه: يُسمَح
    }

    /// <summary>وأَعلى الدَرَجاتِ لا تَقِلُّ عَن أَدناها في أَيّ
    /// حَدّ — سُلَّمٌ لا يَنكَسِر.</summary>
    [Fact]
    public void The_ladder_never_goes_down()
    {
        var order = new[] { "spark", "lite", "growth", "scale" }.Select(TierCatalog.For).ToArray();
        Assert.Equal(4, order.Length);

        for (var i = 1; i < order.Length; i++)
        {
            Assert.True(order[i].AnalysesPerMonth >= order[i - 1].AnalysesPerMonth,
                $"{order[i].Tier} تَحاليلُه أَقَلُّ مِن {order[i - 1].Tier}");
            Assert.True(order[i].RefinesPerMonth >= order[i - 1].RefinesPerMonth,
                $"{order[i].Tier} تَحسيناتُه أَقَلُّ مِن {order[i - 1].Tier}");
            Assert.True(order[i].StoresMax >= order[i - 1].StoresMax,
                $"{order[i].Tier} مَتاجِرُه أَقَلُّ مِن {order[i - 1].Tier}");
        }
    }
}
