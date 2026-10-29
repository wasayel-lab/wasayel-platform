using ACommerce.Kit.Tenants;
using ACommerce.Templates.Customer.Marketplace.Services.Incubator;
using ACommerce.Templates.Customer.Marketplace.Services.Patterns;
using ACommerce.Templates.Customer.Marketplace.Services.TenantConfig;
using Xunit;

namespace ACommerce.Platform.Tests;

// ═══ تَرميمُ مَتاجِرِ المُنتَجاتِ الَّتي وُلِدَت «إيجاراً» (قَرارُ المالِك د24؛ ‏ADR-034 مُلحَق د24) ═══
//
// `scripts/repair-commercial-categories.cs` يُعيدُ فِئَتَي `products`/`deals` في مَتجَرٍ بُنِيَ مِن الاستوديو قَبلَ `c4278c0` مِن «تِجاريّ» إلى
// بِلا تَجميع. والقَرارُ في `CommercialCategoryRepair` — يُقاسُ هُنا بِطَرَفَيه؛ وشَكلُ السكربتِ نَصّاً. والبُرهانُ الحَيُّ في رِسالَةِ الكوميت.

public class CommercialCategoryRepairTests
{
    private static readonly Guid Analysis = Guid.Parse("5a3e1d00-0000-4000-8000-0000000000aa");

    private static Tenant Store(Guid? sourceAnalysis, params (string Slug, string Kind)[] cats) => new()
    {
        Id = "probe",
        SourceAnalysisId = sourceAnalysis,
        Categories = cats.Select((c, i) => new Category { Slug = c.Slug, Label = c.Slug, Kind = c.Kind, SortOrder = i }).ToList(),
    };

    // ─── د24: مَتاجِرُ المُنتَجاتِ الَّتي وُلِدَت «إيجاراً» ──────────────────────────────────────

    [Fact]
    public void A_studio_store_born_with_commercial_products_and_deals_is_repaired_to_no_grouping_and_gets_its_cart()
    {
        var t = Store(Analysis, ("products", "commercial"), ("deals", "commercial"));
        Assert.Equal(AppPattern.Rental, PatternProfileResolver.PatternOf(t));
        Assert.Equal(new[] { "products", "deals" }, CommercialCategoryRepair.Targets(t).ToArray());

        Assert.Equal(2, CommercialCategoryRepair.Apply(t));
        Assert.All(t.Categories, c => Assert.Equal(CategoryKinds.None, c.Kind));
        Assert.Equal(AppPattern.Marketplace, PatternProfileResolver.PatternOf(t));
        Assert.True(PatternProfileResolver.Of(t).UsesCart);

        Assert.Equal(0, CommercialCategoryRepair.Apply(t));   // ‏إعادَةُ التَشغيلِ لا تَجِدُ ما تُغَيِّرُه
    }

    [Fact]
    public void A_store_not_built_from_the_studio_is_never_touched()
    {
        var t = Store(null, ("products", "commercial"), ("deals", "commercial"));
        Assert.Empty(CommercialCategoryRepair.Targets(t));
        Assert.Equal(0, CommercialCategoryRepair.Apply(t));
        Assert.All(t.Categories, c => Assert.Equal("commercial", c.Kind));
    }

    [Theory]
    [InlineData("shops", "commercial")]      // ‏مَحَلّاتُ مَتجَرِ العَقارِ «تِجاريّ» عَن قَصد
    [InlineData("products", "residential")]   // ‏غَيرُ «تِجاريّ» بِالضَبط
    [InlineData("products", "Commercial")]    // ‏بِالضَبطِ حَرفاً
    [InlineData("deals", "")]                 // ‏مُرَمَّمٌ أَصلاً
    public void Only_products_and_deals_carrying_exactly_commercial_are_targets(string slug, string kind)
    {
        var t = Store(Analysis, (slug, kind));
        Assert.Empty(CommercialCategoryRepair.Targets(t));
        Assert.Equal(0, CommercialCategoryRepair.Apply(t));
        Assert.Equal(kind, Assert.Single(t.Categories).Kind);
    }

    [Fact]
    public void A_mixed_store_has_only_its_commercial_products_and_deals_repaired()
    {
        var t = Store(Analysis, ("products", "commercial"), ("shops", "commercial"), ("deals", "events"));
        Assert.Equal(new[] { "products" }, CommercialCategoryRepair.Targets(t).ToArray());
        Assert.Equal(1, CommercialCategoryRepair.Apply(t));
        Assert.Equal(new[] { "", "commercial", "events" }, t.Categories.Select(c => c.Kind).ToArray());
        Assert.Equal(new[] { "products", "deals" }, CommercialCategoryRepair.Slugs.ToArray());
    }

    /// <summary>السكربتُ يَعرِضُ افتِراضاً، ولا يَكتُبُ إلّا بِـ<c>--apply</c> وعَدَدٍ يُطابِقُ العَرض، ويَسأَلُ الدالَّةَ النَقِيَّة، ويَفشَلُ عَلى الصِفر،
    /// ولا يَمَسُّ المُخَطَّط — ولا اسمَ مَتجَرٍ مُستَثنى.</summary>
    [Fact]
    public void The_repair_script_shows_by_default_and_applies_only_on_a_matching_confirm()
    {
        const string file = "repair-commercial-categories.cs", decide = "CommercialCategoryRepair.Targets(", effect = "CommercialCategoryRepair.Apply(", audit = "CommercialCategoryRepair.AuditAction";
        var path = Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, "scripts", file);
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
                     decide, effect, audit,
                 })
            Assert.Contains(needle, text, StringComparison.Ordinal);

        // ‏ولا كِتابَةَ قَبلَ بَوّابَةِ التَأكيد: أَوَّلُ `SaveChangesAsync` بَعدَها.
        var gate = text.IndexOf("if (confirm is null || confirm != plan.Count)", StringComparison.Ordinal);
        var firstSave = text.IndexOf("SaveChangesAsync", StringComparison.Ordinal);
        Assert.True(firstSave > gate && gate > text.IndexOf("if (!apply)", StringComparison.Ordinal),
            "كِتابَةٌ قَبلَ بَوّابَةِ العَرضِ أَو التَأكيد.");
        // ‏ولا اسمَ مَتجَرٍ مُستَثنى (`ashare` كَأَيِّ مَتجَر — قَرارُ المالِك).
        Assert.DoesNotContain("\"ashare\"", text, StringComparison.Ordinal);
    }
}
