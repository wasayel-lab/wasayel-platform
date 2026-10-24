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
//
// ‏(‏2026-10-24، كوميتُ الإصلاح — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه): قُلِبَت الثَلاثَةُ بِأَسمائِها — النُقطَةُ تُقارِنُ المالِكَ بَعدَ الهُوِيَّةِ
// وتَرُدُّ غَيرَ المُكتَمِلَةِ وتَسأَلُ `StudioRefineSection` قَبلَ الحِصَّة، والخِدمَةُ تَسأَلُه قَبلَ النِداء — وأُضيفَ صَفُّ التَرتيبِ وصُفوفُ
// المَعجَم. والصَفّانِ الباقِيانِ بِحَرفِهِما.
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
    /// <para><b>قُلِبَ «Today_» بِاسمِه (‏2026-10-24، مُراجَعَةُ تَيّارِ S15 الجَولَة 2)</b> — كانَ
    /// <c>Today_refine_is_the_one_study_endpoint_that_never_compares_the_study_owner</c> (‏<c>65d0b6a</c>): «نِقاطُ الدِراسَة: 5 · بِلا مُقارَنَةِ
    /// مالِك: 1 (/studio/s/{id:guid}/refine)». والآن كُلُّ نُقطَةٍ عَلى <c>/studio/s/{id}/…</c> تُقارِنُ المالِكَ <b>قَبلَ</b> أَن تَسأَلَ البابَ أَو
    /// تَعُدَّ أَو تُطلِقَ أَو تَكتُبَ أَو تُسَلِّمَ مِلَفّاً — بِالماسِحِ القائِم، فَنُقطَةٌ سادِسَةٌ تُضافُ غَداً تُحمِرُّ هُنا بِاسمِها. ويَطبَعُ ما عَدَّه
    /// ويَفشَلُ عَلى ما دونَ المَقيس (القاعِدَة ١٠).</para>
    /// <para><b>وقِراءَةُ الاستِمارَةِ لَيسَت هُنا عَمداً</b>: <c>/feedback</c> تَقرَأُ حَقلَيها قَبلَ المِلكِيَّةِ (قائِمٌ قَبلَ التَيّار، ولا تَكتُبُ
    /// لِغَيرِ المالِك) — مُسَجَّلٌ في التَقرير لا مُصلَحٌ هُنا. وتَرتيبُ <c>/refine</c> كُلُّه — الحُقولُ مِنه — في الصَفِّ التالي.</para>
    /// </summary>
    [Fact]
    public void Every_study_endpoint_compares_the_study_owner_before_it_asks_the_door_spends_runs_or_writes()
    {
        var all = StudyEndpoints();
        var without = all.Where(e => !OwnerCompare.IsMatch(e.Body)).Select(e => e.Route).ToArray();
        output.WriteLine($"نِقاطُ الدِراسَة: {all.Count} ({string.Join(" · ", all.Select(e => e.Route))}) · بِلا مُقارَنَةِ مالِك: "
            + $"{without.Length} ({string.Join(" · ", without)})");

        Assert.True(all.Count >= 5, $"أَداةٌ عَمياء: {all.Count} نُقطَة — والمَقيسُ 5.");
        Assert.Empty(without);
        foreach (var e in all)
        {
            var owner = OwnerCompare.Match(e.Body).Index;
            foreach (var later in new[] { "StudioAnalysisDoor.Opens(", "tier.Check", "tier.Record", "Task.Run(", ".Store(", "SaveChangesAsync(", "Results.File(" })
            {
                var at = e.Body.IndexOf(later, StringComparison.Ordinal);
                Assert.True(at < 0 || owner < at, $"{e.Route}: مُقارَنَةُ المالِكِ بَعدَ {later}.");
            }
        }
    }

    /// <summary><c>/studio/s/{id}/refine</c> بِتَرتيبِه كُلِّه (القاعِدَة ٦: التَخويلُ قَبلَ الحُقول): الهُوِيَّةُ ← المِلكِيَّةُ ← بابُ التَحليلِ ←
    /// حالُ الدِراسَةِ ← الحُقولُ والمَعجَمُ ← بَوّابَةُ الحِصَّةِ ← عَدُّها ← الإطلاق. ورَدُّ غَيرِ المالِكِ إلى <c>/studio</c> كَأَخَواتِه.</summary>
    [Fact]
    public void Refine_asks_identity_then_ownership_then_the_door_then_the_study_state_then_the_section_then_the_quota()
    {
        var body = RefineBody();
        string[] order =
        {
            "auth.IsAuthenticated",
            "session.OwnerUserId != auth.UserId!.Value",
            "StudioAnalysisDoor.Opens(svc.IsConfigured)",
            "session.Status != Services.Incubator.IncubatorStatus.Completed || session.AnalysisJson is null",
            "req.Form[\"section\"]",
            "StudioRefineSection.IsKnown(section)",
            "tier.CheckRefineAsync",
            "tier.RecordRefineAsync",
            "RefineSectionAsync(",
        };
        var at = order.Select(n =>
        {
            var i = body.IndexOf(n, StringComparison.Ordinal);
            Assert.True(i >= 0, $"غائِب: {n}");
            Assert.Equal(i, body.LastIndexOf(n, StringComparison.Ordinal));
            return i;
        }).ToArray();
        for (var k = 1; k < at.Length; k++)
            Assert.True(at[k - 1] < at[k], $"«{order[k - 1]}» لا يَسبِقُ «{order[k]}».");
        var owner = body.IndexOf("session.OwnerUserId != auth.UserId!.Value", StringComparison.Ordinal);
        Assert.StartsWith("session.OwnerUserId != auth.UserId!.Value)\n                return Results.Redirect(\"/studio\");",
            body[owner..].Replace("\r\n", "\n"), StringComparison.Ordinal);
    }

    /// <summary><b>قُلِبَ «Today_» بِاسمِه (‏2026-10-24، مُراجَعَةُ تَيّارِ S15 الجَولَة 2)</b> — كانَ
    /// <c>Today_refine_takes_the_section_key_from_the_form_without_a_closed_set</c> (‏<c>65d0b6a</c>). والآن النُقطَةُ تَسأَلُ
    /// <see cref="StudioRefineSection.IsKnown"/> قَبلَ الحِصَّة، والخِدمَةُ تَسأَلُه قَبلَ المُطالَبَةِ والنِداءِ والدَمج — بَدَلَ فَحصِ الفَراغ.</summary>
    [Fact]
    public void Refine_refuses_a_section_outside_the_closed_set_before_the_quota_and_the_service_refuses_it_before_the_call()
    {
        var body = RefineBody();
        Assert.Contains("var section = req.Form[\"section\"].ToString().Trim();", body, StringComparison.Ordinal);
        var known = body.IndexOf("if (!Services.Incubator.StudioRefineSection.IsKnown(section) || string.IsNullOrEmpty(feedback))", StringComparison.Ordinal);
        Assert.True(known > 0, "النُقطَةُ لا تَسأَلُ المَعجَم.");
        Assert.True(known < body.IndexOf("CheckRefineAsync", StringComparison.Ordinal));
        Assert.DoesNotContain("string.IsNullOrEmpty(section)", body, StringComparison.Ordinal);

        var svc = RefineService();
        var guard = svc.IndexOf("if (!StudioRefineSection.IsKnown(sectionKey)) return;", StringComparison.Ordinal);
        Assert.True(guard > 0, "الخِدمَةُ لا تَسأَلُ المَعجَم.");
        Assert.DoesNotContain("string.IsNullOrWhiteSpace(sectionKey)", svc, StringComparison.Ordinal);
        foreach (var later in new[] { "LoadAsync(id, ct)", "var systemPrompt", "_backend.CallAsync(req, ct)", "MergeSection(s.AnalysisJson, newJson, sectionKey)" })
            Assert.True(guard < svc.IndexOf(later, StringComparison.Ordinal), $"المَعجَمُ بَعدَ {later}.");
    }

    /// <summary><b>قُلِبَ «Today_» بِاسمِه (‏2026-10-24، مُراجَعَةُ تَيّارِ S15 الجَولَة 2)</b> — كانَ
    /// <c>Today_refine_spends_a_unit_on_a_study_the_service_will_not_refine</c> (‏<c>65d0b6a</c>؛ مَقيسٌ: حِصَّةُ B ‏2 → 3 بِلا نِداء). والآن
    /// النُقطَةُ تَقرَأُ الدِراسَةَ وتَرُدُّ غَيرَ المُكتَمِلَةِ قَبلَ أَن تُعَدَّ وَحدَة — شَرطُ الخِدمَةِ نَفسُه، والخِدمَةُ تُبقيه بِحَرفِه.</summary>
    [Fact]
    public void Refine_refuses_a_study_that_is_not_completed_before_the_quota()
    {
        var body = RefineBody();
        var state = body.IndexOf("if (session.Status != Services.Incubator.IncubatorStatus.Completed || session.AnalysisJson is null)", StringComparison.Ordinal);
        Assert.True(state > 0, "النُقطَةُ لا تَسأَلُ حالَ الدِراسَة.");
        Assert.True(body.IndexOf("var session = await svc.LoadAsync(id);", StringComparison.Ordinal) < state);
        Assert.True(state < body.IndexOf("CheckRefineAsync", StringComparison.Ordinal));
        Assert.True(state < body.IndexOf("RecordRefineAsync", StringComparison.Ordinal));

        Assert.Contains("if (s is null || s.Status != IncubatorStatus.Completed || s.AnalysisJson is null) return;",
            RefineService(), StringComparison.Ordinal);
    }

    // ─── المَعجَمُ المُغلَق — مُوجِبٌ وسالِبٌ وتَطابُقٌ في الاتِّجاهَين ─────────────────────────────────

    /// <summary>المَعجَمُ هو ما تَعرِضُه الصَفحَةُ بِحَرفِه وبِتَرتيبِه — فَخِيارٌ يُزادُ في أَحَدِهِما دونَ الآخَرِ يُحمِرُّ هُنا (القاعِدَة ٢).</summary>
    [Fact]
    public void The_closed_set_is_exactly_what_the_study_page_offers()
    {
        var offered = OfferedSections();
        output.WriteLine($"المَعجَم: {StudioRefineSection.All.Count} · الاستِمارَة: {offered.Length}");
        Assert.Equal(10, StudioRefineSection.All.Count);
        Assert.Equal(StudioRefineSection.All.Count, StudioRefineSection.All.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(offered, StudioRefineSection.All);
    }

    [Fact]
    public void Every_section_the_page_offers_is_known()
    {
        var offered = OfferedSections();
        Assert.NotEmpty(offered);
        Assert.All(offered, s => Assert.True(StudioRefineSection.IsKnown(s), $"«{s}» مَعروضٌ ولا يَعرِفُه المَعجَم."));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("injected_by_b")]
    [InlineData("summary")]
    [InlineData("kpis")]
    [InlineData("recommendations")]
    [InlineData("Risks")]
    [InlineData(" risks")]
    [InlineData("risks ")]
    public void A_section_outside_the_closed_set_is_not_known(string? section)
        => Assert.False(StudioRefineSection.IsKnown(section));

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
