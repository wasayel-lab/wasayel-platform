using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using ACommerce.Templates.Customer.Marketplace.Services.Incubator;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ تَحسينُ الدِراسَةِ يَكتُبُ في دِراسَةِ غَيرِ صاحِبِها — تَوصيفُ «اليَوم» (‏2026-10-24، مُراجَعَةُ تَيّارِ S15 الجَولَة 2) ═══
//
// **المَقيسُ حَيّاً** (قاعِدَةٌ مَحَلِّيَّةٌ جَديدَة `wasayel_s15r2a`، Development، الرَأسُ `428fc59`، ومُزَوِّدُ التَحليلِ بَديلٌ مَحَلِّيٌّ عَلى
// السَطحِ المُوَثَّق — `analysis-stand-in.py` بِجِوارِ سِجِلِّ المالِك «أ» S12·٧ — يُعيدُ JSON مُعَدّاً لا يُوَلِّدُ شَيئاً): A مالِكُ دِراسَةِ
// العَيِّنَةِ المُكتَمِلَة (‏`INCUBATOR_SAMPLE_SEED=1`)، وB مُستَخدِمُ استوديو آخَر دَخَلَ بِرَمزِه. ثُمَّ مِن جَلسَةِ B:
//
//   | الطَلَب                                                   | الرَدّ                       | حِصَّةُ B | دِراسَةُ A                                          | نِداءُ المُزَوِّد            |
//   |------------------------------------------------------------|------------------------------|-----------|------------------------------------------------------|------------------------------|
//   | `POST /studio/s/{عَيِّنَةُ A}/refine` ‏`section=risks`        | ‏302 ‏`?refining=risks`        | 0 → 1     | بَصمَةُ `AnalysisJson` ‏`b4622a…` → `59bc2d…`، والخَطَرُ الأَوَّلُ ما رَدَّه البَديل | 1، مَحسوبٌ عَلى A لا B |
//   | نَفسُه ‏`section=injected_by_b`                              | ‏302 ‏`?refining=injected_by_b` | 1 → 2     | مَفاتيحُها 13 → **14**: مِفتاحٌ لَيسَ في المُخَطَّط     | 2، عَلى A                    |
//   | `POST /studio/s/{دِراسَتُه الفاشِلَة}/refine` ‏`section=risks` | ‏302 ‏`?refining=risks`        | 2 → **3** | —                                                    | **لا نِداء**: وَحدَةٌ أُنفِقَت بِلا شَيء |
//
// **والجَذرُ مَقروء**: النُقطَةُ تَسأَلُ «مَن أَنت» (`auth.IsAuthenticated`) ولا تَسأَلُ «أَدِراسَتُك؟» — وأَخَواتُها الأَربَعُ عَلى
// `/studio/s/{id}/…` (‏`feedback`، `export.xlsx`، `build`، `analyze`) تُقارِنُ `OwnerUserId` بِصاحِبِ الجَلسَة. و`WriteEndpointGuardTests`
// يَعُدُّها مَحروسَةً لِأَنّ `auth.IsAuthenticated` في `BodyGuards` — الهُوِيَّةُ حارِسٌ لا يَرى المَفعولَ بِه، وهذا حَدُّه المُعلَنُ هُناك.
// ومِفتاحُ القِسمِ يُؤخَذُ مِن الاستِمارَةِ بِلا مَعجَمٍ إلى المُطالَبَةِ وإلى `MergeSection` الَّتي تُضيفُ ما لَم يَكُن. والدِراسَةُ غَيرُ
// المُكتَمِلَةِ تَرُدُّها الخِدمَةُ صامِتَةً في الخَلفِيَّة — بَعدَ أَن عُدَّت الوَحدَة.
//
// هذا المِلَفُّ يُوَصِّفُ «اليَوم» (القاعِدَة ٣) قَبلَ أَيِّ تَبديل. وصُفوفُ «Today_» الثَلاثَةُ تُقلَبُ بِأَسمائِها في كوميتِ الإصلاح،
// والصَفّانِ الآخَرانِ يَبقَيانِ بِحَرفِهِما: ما تَعرِضُه الصَفحَةُ، وأَنّ الدَمجَ نَفسَه لا يَرُدُّ مِفتاحاً.
public class StudioRefineOwnershipTests(ITestOutputHelper output)
{
    private const string TemplateRoot = "libs/templates/ACommerce.Templates.Customer.Marketplace";
    private const string StudyPrefix = "/studio/s/{id:guid}/";
    private const string Refine = "/studio/s/{id:guid}/refine";

    /// <summary>مُقارَنَةُ المالِكِ بِشَكلَيها في المِلَفّ: بِصاحِبِ الجَلسَةِ مُباشَرَةً، أَو بِمُتَغَيِّرٍ أُسنِدَ إلَيه (‏<c>/build</c>:
    /// <c>var ownerId = auth.UserId!.Value</c>).</summary>
    private static readonly Regex OwnerCompare =
        new(@"\bOwnerUserId\s*!=\s*(?:auth\.UserId!\.Value|ownerId)\b", RegexOptions.Compiled);

    private static string Read(string relative)
    {
        var path = Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, relative.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(path), $"مَصدَرٌ مَفقود: {relative} — الأَداةُ عَمياءُ بِلا طَرَفٍ مَقروء.");
        var text = File.ReadAllText(path);
        Assert.True(text.Length > 300, $"أَداةٌ عَمياء: {relative} طولُه {text.Length} مِحرَفاً.");
        return text;
    }

    /// <summary>نِقاطُ الدِراسَةِ مِن الماسِحِ القائِم (‏<see cref="WriteEndpointGuardTests.AllMinimalApiEndpoints"/> — القاعِدَة ٨: لا ماسِحَ
    /// ثانٍ)، بِأَجسامِها بِلا تَعليقات.</summary>
    private static List<WriteEndpointGuardTests.Endpoint> StudyEndpoints()
        => WriteEndpointGuardTests.AllMinimalApiEndpoints()
            .Where(e => e.Route.StartsWith(StudyPrefix, StringComparison.Ordinal))
            .ToList();

    private static string RefineBody() => Assert.Single(StudyEndpoints(), e => e.Route == Refine).Body;

    private static string RefineService()
    {
        var svc = WriteEndpointGuardTests.StripComments(Read($"{TemplateRoot}/Services/Incubator/FeasibilityAnalysisService.cs"));
        var start = svc.IndexOf("public async Task RefineSectionAsync(", StringComparison.Ordinal);
        Assert.True(start > 0, "RefineSectionAsync غائِبَة.");
        var end = svc.IndexOf("internal static string ExtractSection(", start, StringComparison.Ordinal);
        Assert.True(end > start, "لَم يُعثَر عَلى ما بَعدَ RefineSectionAsync — القَصُّ فاشِل.");
        var body = svc[start..end];
        Assert.True(body.Length > 500, $"جِسمُ RefineSectionAsync {body.Length} مِحرَفاً — القَصُّ انزَلَق.");
        return body;
    }

    /// <summary>
    /// <para><b>«اليَوم»</b>: مِن نِقاطِ <c>/studio/s/{id}/…</c> تُقارِنُ كُلُّها المالِكَ إلّا <c>/refine</c>. ويَطبَعُ ما عَدَّه ويَفشَلُ
    /// عَلى ما دونَ المَقيس (القاعِدَة ١٠).</para>
    /// </summary>
    [Fact]
    public void Today_refine_is_the_one_study_endpoint_that_never_compares_the_study_owner()
    {
        var all = StudyEndpoints();
        var without = all.Where(e => !OwnerCompare.IsMatch(e.Body)).Select(e => e.Route).ToArray();
        output.WriteLine($"نِقاطُ الدِراسَة: {all.Count} ({string.Join(" · ", all.Select(e => e.Route))}) · بِلا مُقارَنَةِ مالِك: "
            + $"{without.Length} ({string.Join(" · ", without)})");

        Assert.True(all.Count >= 5, $"أَداةٌ عَمياء: {all.Count} نُقطَة — والمَقيسُ 5.");
        Assert.Equal(new[] { Refine }, without);
        Assert.Contains("auth.IsAuthenticated", RefineBody(), StringComparison.Ordinal);
    }

    /// <summary><b>«اليَوم»</b>: مِفتاحُ القِسمِ يُفحَصُ بِالفَراغِ وَحدَه — في النُقطَةِ وفي الخِدمَة — ثُمَّ يَبلُغُ المُطالَبَةَ والدَمج.</summary>
    [Fact]
    public void Today_refine_takes_the_section_key_from_the_form_without_a_closed_set()
    {
        var body = RefineBody();
        Assert.Contains("var section = req.Form[\"section\"].ToString().Trim();", body, StringComparison.Ordinal);
        Assert.Contains("if (string.IsNullOrEmpty(section) || string.IsNullOrEmpty(feedback))", body, StringComparison.Ordinal);
        Assert.DoesNotContain("IsKnown(", body, StringComparison.Ordinal);

        var svc = RefineService();
        Assert.Contains("if (string.IsNullOrWhiteSpace(sectionKey)) return;", svc, StringComparison.Ordinal);
        Assert.DoesNotContain("IsKnown(", svc, StringComparison.Ordinal);
        Assert.Contains("MergeSection(s.AnalysisJson, newJson, sectionKey)", svc, StringComparison.Ordinal);
    }

    /// <summary><b>«اليَوم»</b>: النُقطَةُ تَعُدُّ وَحدَةً قَبلَ أَن تَقرَأَ الدِراسَة، والخِدمَةُ في الخَلفِيَّةِ تَرُدُّ غَيرَ المُكتَمِلَةِ صامِتَة.</summary>
    [Fact]
    public void Today_refine_spends_a_unit_on_a_study_the_service_will_not_refine()
    {
        var body = RefineBody();
        Assert.Contains("RecordRefineAsync", body, StringComparison.Ordinal);
        Assert.DoesNotContain("IncubatorStatus.Completed", body, StringComparison.Ordinal);
        Assert.DoesNotContain("LoadAsync(", body, StringComparison.Ordinal);

        Assert.Contains("if (s is null || s.Status != IncubatorStatus.Completed || s.AnalysisJson is null) return;",
            RefineService(), StringComparison.Ordinal);
    }

    // ─── ما يَبقى بِحَرفِه بَعدَ الإصلاح ─────────────────────────────────────────────────────────

    /// <summary>أَقسامُ التَحسينِ الَّتي تَعرِضُها صَفحَةُ الدِراسَة — عَشَرَةٌ، كُلٌّ مِنها مِفتاحٌ أَعلى في مُخَطَّطِ المُخرَجات
    /// (<see cref="FeasibilityPromptBuilder.OutputSchema"/>). فَما سِواها لا تُرسِلُه الاستِمارَة.</summary>
    internal static string[] OfferedSections()
    {
        var page = Read($"{TemplateRoot}/Components/Pages/StudioStudy.razor");
        var start = page.IndexOf("<select name=\"section\"", StringComparison.Ordinal);
        Assert.True(start > 0, "قائِمَةُ أَقسامِ التَحسينِ غائِبَة.");
        Assert.Equal(start, page.LastIndexOf("<select name=\"section\"", StringComparison.Ordinal));
        var end = page.IndexOf("</select>", start, StringComparison.Ordinal);
        Assert.True(end > start);
        return Regex.Matches(page[start..end], "<option value=\"([^\"]+)\"").Select(m => m.Groups[1].Value).ToArray();
    }

    [Fact]
    public void The_study_page_offers_ten_schema_sections_to_refine()
    {
        var offered = OfferedSections();
        output.WriteLine($"أَقسامُ الاستِمارَة: {offered.Length} ({string.Join(" · ", offered)})");
        Assert.Equal(new[]
        {
            "marketSizing", "customerSegments", "competitors", "revenueModel", "costStructure",
            "financialProjection", "risks", "lessonsFromFailures", "roadmap", "recommendedPattern",
        }, offered);

        using var schema = JsonDocument.Parse(FeasibilityPromptBuilder.OutputSchema);
        var top = schema.RootElement.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        Assert.Equal(13, top.Count);
        Assert.All(offered, s => Assert.Contains(s, top));
    }

    /// <summary><b>والدَمجُ نَفسُه لا يَرُدُّ مِفتاحاً</b>: يَستَبدِلُ القائِمَ ويُضيفُ الغائِب — والإضافَةُ مَقصودَة (دِراسَةٌ أَقدَمُ بِلا
    /// <c>recommendedPattern</c> تَنالُه بِالتَحسين). فَالمَعجَمُ قَبلَه لا فيه. سُلوكٌ بِالانعِكاسِ عَلى الدالَّةِ الداخِلِيَّة.</summary>
    [Fact]
    public void The_merge_replaces_a_present_key_and_appends_an_absent_one()
    {
        var merge = typeof(FeasibilityAnalysisService).GetMethod("MergeSection", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(merge);
        string? Merge(string orig, string next, string key) => (string?)merge!.Invoke(null, new object[] { orig, next, key });

        Assert.Equal("{\"risks\":[2],\"roadmap\":[]}", Merge("{\"risks\":[1],\"roadmap\":[]}", "{\"risks\":[2]}", "risks"));
        Assert.Equal("{\"risks\":[1],\"injected\":\"x\"}", Merge("{\"risks\":[1]}", "{\"injected\":\"x\"}", "injected"));
        Assert.Null(Merge("{\"risks\":[1]}", "{\"other\":1}", "risks"));
    }
}
