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

    /// <summary>
    /// <para>وأَعلى الدَرَجاتِ لا تَقِلُّ عَن أَدناها في أَيّ حَدّ —
    /// سُلَّمٌ لا يَنكَسِر.</para>
    ///
    /// <para><b>والسُلَّمُ يُقاسُ عَلى الدَرَجاتِ المَدفوعَةِ وَحدَها
    /// ما دامَ الرَفعُ المُؤَقَّتُ قائِماً — ويُقالُ لِماذا</b>
    /// (‏2026-08-30): قَرارُ المالِكِ رَفَعَ <c>spark</c> إلى سَقفِ
    /// <c>scale</c> حَتّى يَستَقِرَّ التَسعير، فَصارَت المَجّانِيَّةُ
    /// فَوقَ <c>lite</c> عَدَداً. <b>ولا مُستَخدِمَ يَختَبِرُ هذا
    /// الانقِلاب</b>: ‏<c>SelfServiceCheckoutExists = false</c>
    /// و<c>StudioTierPurchase.Refuse</c> يَرُدُّ الدَرَجاتِ الأَربَعَ،
    /// والكاتِبُ الوَحيدُ لِـ<c>Tier</c> خَلفَ ذلكَ الرَفض — فَلا أَحَدَ
    /// عَلى <c>lite</c> أَصلاً لِيَرى نَفسَه أَدنى مِن مَجّانِيّ.
    /// السُلَّمُ يَحرُسُ سَطحَ بَيعٍ **لَم يوجَد بَعد**، وهو بِعَينِه
    /// ما يَنتَظِرُه الرَفعُ المُؤَقَّت.</para>
    ///
    /// <para><b>والأَصلُ يَعودُ بِلا تَعديلِ حَرف</b>: يَومَ يَصيرُ
    /// <see cref="TierCatalog.FreeTierTemporarilyRaised"/> بِـ
    /// <c>false</c> يَشمَلُ الفَحصُ الدَرَجاتِ الأَربَعَ مِن جَديدٍ
    /// تِلقائِيّاً. ولِذلكَ شَرطٌ لا حَذف.</para>
    /// </summary>
    [Fact]
    public void The_ladder_never_goes_down()
    {
        var rungs = TierCatalog.FreeTierTemporarilyRaised
            ? new[] { "lite", "growth", "scale" }
            : new[] { "spark", "lite", "growth", "scale" };

        var order = rungs.Select(TierCatalog.For).ToArray();
        Assert.True(order.Length >= 3, $"أَداةٌ عَمياء: فُحِصَت {order.Length} دَرَجَة.");

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

    /// <summary><b>والرَفعُ المُؤَقَّتُ لا يَتَجاوَزُ السَقفَ
    /// المُصَرَّحَ بِه</b> — الشَرطُ الَّذي يَحُلُّ مَحَلَّ دَرَجَةِ
    /// السُلَّمِ المَرفوعَةِ أَعلاه، فَلا يَبقى الحَدُّ بِلا
    /// فاحِص.</summary>
    [Fact]
    public void The_temporarily_raised_free_tier_never_exceeds_the_declared_ceiling()
    {
        if (!TierCatalog.FreeTierTemporarilyRaised) return;

        var spark = TierCatalog.For("spark");
        var scale = TierCatalog.For("scale");

        Assert.True(spark.AnalysesPerMonth <= scale.AnalysesPerMonth, "spark فَوقَ سَقفِ التَحاليل");
        Assert.True(spark.RefinesPerMonth  <= scale.RefinesPerMonth,  "spark فَوقَ سَقفِ التَحسينات");
        Assert.True(spark.StoresMax        <= scale.StoresMax,        "spark فَوقَ سَقفِ المَتاجِر");
    }
}
