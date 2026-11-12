using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using ACommerce.Platform.I18n;
using ACommerce.Platform.Shared.Metering;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ G2 — مُعجَمُ الوَحَداتِ المَضمون: لا سِعرَ فيه، ولا مِفتاحَ مَجهولٌ يَمُرّ، ولِكُلِّ وَحدَةٍ مِفتاحُ قامُوسٍ ومِيزَةٌ مِن مُعجَمٍ قائِم ═══
// (‏2026-11-12، الموجَة ١٣ W13-6·٣ — ADR-051 §١١؛ التَصميم §2 و§3). كُلُّ صَفٍّ يَطبَعُ ما عَدَّه ويَفشَلُ عَلى الصِفر (القاعِدَة ١٠).

public class MeterUnitCatalogTests(ITestOutputHelper output)
{
    /// <summary>كَلِماتُ السِعرِ المَمنوعَةُ في الاسمِ وفي المِفتاح — بِحَرفِ التَصميم §2 (القاعِدَة ١٦: هذه المَوجَةُ عَدٌّ وَحدَه).</summary>
    internal static readonly Regex PriceIsh = new("(?i)price|amount|cost|fee|currency|sar", RegexOptions.Compiled);

    private static string UnitsFile() => Path.Combine(ThemeZeroEquivalenceTests.RepoRoot,
        "libs", "core", "ACommerce.Platform.Shared", "Metering", "Definitions", "meter-units.json");

    [Fact]
    public void The_embedded_lexicon_loads_and_carries_the_skeleton_unit()
    {
        var catalog = MeterUnitCatalog.Default;
        output.WriteLine($"[G2] وَحَداتٌ مُحَمَّلَة: {catalog.Units.Count}");
        Assert.True(catalog.Units.Count > 0, "أَداةٌ عَمياء: صِفرُ وَحدَة.");
        var deal = catalog.Find("deal.create");
        Assert.NotNull(deal);
        Assert.Equal((MeterSides.Operation, MeterBoundaries.Persistence, MeterMeasures.Count, "deal_status", MeterReconcilers.DealIds),
                     (deal.Side, deal.Boundary, deal.Measure, deal.Feature, deal.Reconcile));
        Assert.Contains("DealsService.StartAsync", deal.Source, StringComparison.Ordinal);
    }

    /// <summary><b>لا عُضوَ يُسَمّي سِعراً</b> في أَيِّ نَوعٍ مِن أَنواعِ المُجَلَّد — ولا مِفتاحَ في المِلَفّ. والأَداةُ تُقاسُ أَوَّلاً: الكَلِمَةُ تُمسَكُ
    /// في <c>AmountSar</c> و<c>UnitPrice</c> و<c>FeeCode</c>، ولا تُمسَكُ في أَسماءِ المُجَلَّدِ الحَقيقِيَّة.</summary>
    [Fact]
    public void No_member_or_key_of_the_lexicon_names_a_price()
    {
        foreach (var hit in new[] { "AmountSar", "UnitPrice", "FeeCode", "currency", "Cost" }) Assert.Matches(PriceIsh, hit);

        var types = typeof(MeterUnit).Assembly.GetTypes()
            .Where(t => t.Namespace == typeof(MeterUnit).Namespace && !t.Name.StartsWith('<')).ToList();
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        var members = types.SelectMany(t => t.GetMembers(all).Select(m => $"{t.Name}.{m.Name}")).ToList();
        var priced = members.Concat(types.Select(t => t.Name)).Where(n => PriceIsh.IsMatch(n)).ToList();
        output.WriteLine($"[G2] أَنواعٌ مَفحوصَة {types.Count} · أَعضاء {members.Count} · تُسَمّي سِعراً {priced.Count}");
        Assert.True(types.Count >= 8 && members.Count > 50, $"أَداةٌ عَمياء: {types.Count} نَوعاً و{members.Count} عُضواً.");
        Assert.True(priced.Count == 0, "عُضوٌ يُسَمّي سِعراً في مُعجَمِ العَدّ: " + string.Join(", ", priced));

        using var doc = JsonDocument.Parse(File.ReadAllText(UnitsFile()));
        var keys = new List<string>();
        void Walk(JsonElement e)
        {
            if (e.ValueKind == JsonValueKind.Object)
                foreach (var p in e.EnumerateObject()) { keys.Add(p.Name); Walk(p.Value); }
            else if (e.ValueKind == JsonValueKind.Array)
                foreach (var x in e.EnumerateArray()) Walk(x);
        }
        Walk(doc.RootElement);
        output.WriteLine($"[G2] مَفاتيحُ المِلَفّ {keys.Count}: {string.Join(", ", keys.Distinct())}");
        Assert.True(keys.Count > 0, "أَداةٌ عَمياء: صِفرُ مِفتاح.");
        Assert.DoesNotContain(keys, k => PriceIsh.IsMatch(k));
    }

    /// <summary><b>مِفتاحٌ مَجهولٌ يَرمي</b> (<c>Disallow</c>): «price» في صَفٍّ لا يُتَجاهَلُ صامِتاً — فَيُفشِلُ الإقلاع (الحَقنُ المُسَمّى في G2).</summary>
    [Fact]
    public void An_unknown_key_such_as_price_is_refused_not_ignored()
    {
        var text = File.ReadAllText(UnitsFile());
        Assert.NotEmpty(MeterUnitCatalog.Parse(text));
        var injected = text.Replace("\"measure\": \"count\",", "\"measure\": \"count\",\n      \"price\": 1,", StringComparison.Ordinal);
        Assert.NotEqual(text, injected);
        var ex = Assert.ThrowsAny<JsonException>(() => MeterUnitCatalog.Parse(injected));
        output.WriteLine($"[G2] الحَقن: {ex.Message.Split('\n')[0]}");
        Assert.Contains("price", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>صَفٌّ فاسِدٌ يُفشِلُ التَحميلَ بِرَمزِه — لا يَصِلُ الدَفتَرَ صامِتاً.</summary>
    [Fact]
    public void A_breaching_lexicon_fails_the_load_with_its_codes()
    {
        var units = MeterUnitCatalog.Parse(File.ReadAllText(UnitsFile()));
        var ex = Assert.Throws<InvalidOperationException>(() => MeterUnitCatalog.From([.. units, units[0] with { Side = "tech" }]));
        Assert.Contains("unit_side_out_of_vocabulary", ex.Message, StringComparison.Ordinal);
        Assert.Contains("units_code_duplicate", ex.Message, StringComparison.Ordinal);
        Assert.Throws<InvalidOperationException>(() => MeterUnitCatalog.From([]));
    }

    /// <summary><b>لِكُلِّ وَحدَةٍ تَسمِيَةٌ عَرَبِيَّةٌ في القامُوس</b> بِمِفتاحِ <see cref="MeterUnitCatalog.LabelKey"/> — ومِفتاحٌ بِمَقاطِعِ القامُوسِ الثَلاثَة.</summary>
    [Fact]
    public void Every_unit_label_key_is_in_the_arabic_dictionary_and_has_three_segments()
    {
        var keys = MeterUnitCatalog.Default.Units.Select(u => MeterUnitCatalog.LabelKey(u.Code)).ToList();
        var missing = keys.Where(k => !LocaleCatalog.Lexicon.Contains(k)).ToList();
        output.WriteLine($"[G2] مَفاتيحُ التَسمِيَة {keys.Count} · في القامُوس {keys.Count - missing.Count}: {string.Join(", ", keys.Select(k => $"{k}={LocaleCatalog.Find("ar", k)}"))}");
        Assert.True(keys.Count > 0, "أَداةٌ عَمياء: صِفرُ مِفتاح.");
        Assert.True(missing.Count == 0, "وَحدَةٌ بِلا تَسمِيَةٍ في ar.json: " + string.Join(", ", missing));
        Assert.All(keys, k => Assert.Matches("^[a-z][a-z0-9_]*(?:\\.[a-z][a-z0-9_]*){2}$", k));
    }

    /// <summary><b>التَصادُمُ أَحمَر</b>: رَمزانِ يَقَعانِ عَلى مِفتاحٍ واحِدٍ يُرفَضانِ عِندَ التَحميل.</summary>
    [Fact]
    public void Two_codes_on_one_dictionary_key_fail_the_load()
    {
        var unit = MeterUnitCatalog.Default.Units[0];
        var ex = Assert.Throws<InvalidOperationException>(() => MeterUnitCatalog.From([unit with { Code = "a_b.c" }, unit with { Code = "a.b_c" }]));
        Assert.Contains("units_label_key_collision", ex.Message, StringComparison.Ordinal);
    }

    /// <summary><b>المِيزَةُ اسمٌ قائِمٌ لا كَلِمَةٌ تُختَرَع</b> (التَصميم §3.2، القاعِدَة ١٦): كُلُّ مِيزَةٍ ثابِتَةٍ اسمٌ في أَحَدِ المَعاجِمِ الأَربَعَة —
    /// مِلَفّاتُ التَدَفُّق، والعُدَد، وقُدُراتُ المُزَوِّد، وقُدُراتُ الاشتِراك. والمِرساةُ (<c>@…</c>) يُصادِقُها المُصادِق.</summary>
    [Fact]
    public void Every_fixed_feature_is_a_name_in_one_of_the_four_existing_closed_sets()
    {
        var root = ThemeZeroEquivalenceTests.RepoRoot;
        var flows = Directory.EnumerateFiles(Path.Combine(root, "libs", "core", "ACommerce.Platform.Flows", "Definitions"), "*.flow.json")
            .Select(f => Path.GetFileName(f)[..^".flow.json".Length]).ToHashSet(StringComparer.Ordinal);
        var kits = Directory.EnumerateDirectories(Path.Combine(root, "libs", "kits")).Select(d => Path.GetFileName(d).ToLowerInvariant())
            .ToHashSet(StringComparer.Ordinal);
        var anchors = new Dictionary<string, IReadOnlySet<string>>
        {
            ["flow"] = flows,
            ["kit"] = kits,
            ["provider_capability"] = ACommerce.Platform.Providers.ProviderCapabilities.All.ToHashSet(StringComparer.Ordinal),
            ["capability"] = ACommerce.Kit.Subscriptions.CapabilityCatalog.Codes.ToHashSet(StringComparer.Ordinal),
        };
        output.WriteLine("[G2] المَعاجِمُ الأَربَعَة: " + string.Join(" · ", anchors.Select(a => $"{a.Key} {a.Value.Count}")));
        Assert.All(anchors, a => Assert.True(a.Value.Count > 0, $"أَداةٌ عَمياء: صِفرُ اسمٍ في {a.Key}."));

        var fixedFeatures = MeterUnitCatalog.Default.Units.Where(u => !MeterFeatureAnchors.IsAnchor(u.Feature!)).ToList();
        var resolved = fixedFeatures.Select(u => (u.Code, u.Feature, Anchor: anchors.FirstOrDefault(a => a.Value.Contains(u.Feature!)).Key)).ToList();
        output.WriteLine("[G2] مِيزاتٌ مَحلولَة: " + string.Join(", ", resolved.Select(r => $"{r.Code}→{r.Feature}@{r.Anchor ?? "؟"}")));
        Assert.True(resolved.Count > 0, "أَداةٌ عَمياء: صِفرُ مِيزَةٍ ثابِتَة.");
        Assert.All(resolved, r => Assert.True(r.Anchor is not null, $"مِيزَةُ «{r.Code}» = «{r.Feature}» لَيسَت اسماً في أَيِّ مُعجَمٍ قائِم."));
    }
}
