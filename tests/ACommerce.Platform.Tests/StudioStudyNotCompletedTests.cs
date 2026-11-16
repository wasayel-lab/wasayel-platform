using ACommerce.Templates.Customer.Marketplace.Services.Incubator;
using Xunit;
using Xunit.Abstractions;
using static ACommerce.Platform.Tests.LiveStudioStudyReadOnlyCharacterizationTests;

namespace ACommerce.Platform.Tests;

// ═══ الدِراسَةُ القَديمَةُ الَّتي لَم يَكتَمِل تَحليلُها تَقولُ إنّه لَن يَكتَمِل، وتَدُلُّ عَلى استِمارَةِ الإنشاء (‏2026-11-07، مُراجَعَةُ C1 · W13-1·F7) ═══
//
// **المَقيسُ قَبل** (عِندَ `5ae1a92`، الصَفحَةُ مُصَيَّرَةً عَلى Postgres لِجَلسَةٍ `Analyzing`): «جارٍ تَحليل فِكرَتِكَ…» و«تُحَدَّث الصَفحَة تِلقائيّاً» — ولا
// تَحديثَ في الصَفحَةِ ولا تَحليلَ يَجري خَلفَها ولا بابَ يُخرِجُها مِن `Analyzing`. فَالوَعدُ لا يَتَحَقَّقُ أَبَداً (القاعِدَة ١٢: مَدخَلٌ يُجيبُ بِـ«قَريباً»).
// **والآن**: `studio.study.not_completed` — لَم يَكتَمِل ولَن يَكتَمِل — ورابِطٌ إلى `/studio/new` بِنَصِّ لَوحَةِ الاستوديو نَفسِه (`studio.home.start_store`).
// وما يَنجو مُثَبَّتٌ في `LiveStudioStudyReadOnlyCharacterizationTests` (لا يُمَسّ).

public class StudioStudyNotCompletedTests(ITestOutputHelper output)
{
    private const string Page = "libs/templates/ACommerce.Templates.Customer.Marketplace/Components/Pages/StudioStudy.razor";

    /// <summary>ما لا يَقولُه نَصٌّ في صَفحَةٍ لا تُحَدِّثُ نَفسَها — مُعَرّىً مِن التَشكيلِ كَماسِحِ العِباراتِ المَحظورَة.</summary>
    internal static readonly string[] SelfRefreshClaims = ["تلقائيا", "تلقائي", "تحدث الصفحة"];

    internal static List<string> RefreshClaimsDrawnBy(string page)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, "libs/core/ACommerce.Platform.I18n/Locales/ar.json")));
        var hits = new List<string>();
        foreach (var k in StudioEntryCopyTests.KeysDrawnBy(page))
            if (doc.RootElement.TryGetProperty(k, out var v))
                hits.AddRange(SelfRefreshClaims.Where(c => StudioEntryCopyTests.Bare(v.GetString() ?? "").Contains(c, StringComparison.Ordinal)).Select(c => $"{k} ⇐ «{c}»"));
        return hits;
    }

    [Fact]
    public void The_read_only_study_page_draws_no_text_that_promises_a_refresh()
    {
        var page = File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, Page));
        var keys = StudioEntryCopyTests.KeysDrawnBy(page);
        var hits = RefreshClaimsDrawnBy(page);
        output.WriteLine($"StudioStudy: مَفاتيحُ مَفحوصَة {keys.Count} · وُعودُ تَحديث {hits.Count} {string.Join("، ", hits)}");
        Assert.True(keys.Count >= 10, $"أَداةٌ عَمياء: {keys.Count} مِفتاحاً.");
        Assert.Empty(hits);
        Assert.Contains("studio.study.not_completed", keys);
    }

    /// <summary>والماسِحُ يُقاسُ (القاعِدَة ١٠): نَصٌّ يَعِدُ بِتَحديثٍ يُصاب، ونَصُّ الدِراسَةِ الَّتي لا تَحليلَ فيها لا يُصاب. (نَصُّ الوَعدِ القَديمِ بِحَرفِه
    /// مَحقونٌ في مِفتاحٍ لا وُجودَ لَه، فَلا يَعتَمِدُ الصَفُّ عَلى بَقائِه في القامُوس.)</summary>
    [Theory]
    [InlineData("نَجمَع أَرقام السوق، نُقارِن بِالمنافِسين، ونَستَنبِط المَخاطِر. تُحَدَّث الصَفحَة تِلقائيّاً.", true)]
    [InlineData("لا تَحليل بَعد لِهذه الجَلسَة.", false)]
    public void The_refresh_claim_matcher_sees_a_refresh_promise_with_any_diacritics(string text, bool hit)
        => Assert.Equal(hit, SelfRefreshClaims.Any(c => StudioEntryCopyTests.Bare(text).Contains(c, StringComparison.Ordinal)));
}

[Collection(LiveProofCollection.Name)]
public class LiveStudioStudyNotCompletedProofTests(ITestOutputHelper output)
{
    private static bool Enabled => LiveDealConcurrencyProofTests.Enabled && !string.IsNullOrEmpty(LiveDealConcurrencyProofTests.ConnectionString);

    [Fact]
    public async Task An_analysing_study_says_it_will_not_complete_and_leads_to_the_create_form_on_Postgres()
    {
        if (!Enabled) return;
        await using var host = LiveTemplateDoorHost.Build();
        var (id, owner, token) = await SeedAsync(host, IncubatorStatus.Analyzing);
        try
        {
            var html = await RenderAsync(host, id, token);
            var bare = StudioEntryCopyTests.Bare(html);
            output.WriteLine($"[live] Analyzing ⇒ {html.Length} حَرفاً · «لَم يَكتَمِل» {html.Contains(Ar("studio.study.not_completed"), StringComparison.Ordinal)}"
                + $" · وُعودُ تَحديث {StudioStudyNotCompletedTests.SelfRefreshClaims.Count(c => bare.Contains(c, StringComparison.Ordinal))}");
            Assert.Contains(Ar("studio.study.not_completed"), html, StringComparison.Ordinal);
            Assert.Contains($"href=\"/studio/new\">{Ar("studio.home.start_store")}</a>", html, StringComparison.Ordinal);
            Assert.All(StudioStudyNotCompletedTests.SelfRefreshClaims, c => Assert.DoesNotContain(c, bare, StringComparison.Ordinal));
            ReadOnly(html);
        }
        finally { await CleanAsync(host, id, owner); }
    }
}
