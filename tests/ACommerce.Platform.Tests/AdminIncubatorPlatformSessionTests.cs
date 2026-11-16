using ACommerce.Templates.Customer.Marketplace.Components.Pages.Admin;
using ACommerce.Templates.Customer.Marketplace.Services.Incubator;
using Xunit;
using Xunit.Abstractions;
using static ACommerce.Platform.Tests.LiveAdminIncubatorPlatformSessionCharacterizationTests;
using static ACommerce.Platform.Tests.LiveStoreBirthOnceCharacterizationTests;

namespace ACommerce.Platform.Tests;

// ═══ حاضِنَةُ المُشرِفِ تَبلُغُ جَلساتِ المَنَصَّةِ وَحدَها (‏2026-11-07، مُراجَعَةُ عَدَساتِ C1 · W13-1·DD-3) ═══
//
// **المَقيسُ قَبل** (عِندَ `d48cec4`، بُرهانُ هذا المِلَفِّ عَلى الكودِ القَديم): جَلسَةُ عَميلٍ (`Failed`) — بابُ الإجابَةِ ‏302 وكَتَبَ `sector`، وبابُ «حَلِّل» ‏302
// وحَجَزَ تَحليلاً (المُحاوَلات 1)، والدِراسَةُ رَسَمَت زِرَّ «أَعِد» إلى `/analyze`. **والآن** `FeasibilityAnalysisService.LoadPlatformAsync` يَسأَلُه البابانِ قَبلَ
// الكِتابَةِ والحَجز، وتَقرَأُ بِه الصَفحَتان: جَلسَةُ العَميلِ ‏404 بِلا كِتابَةٍ ولا حَجز، والصَفحَتانِ «لا توجَد». وجَلسَةُ المَنَصَّةِ كَما كانَت
// (‏`LiveAdminIncubatorPlatformSessionCharacterizationTests` — لا يُمَسّ).

public class AdminIncubatorPlatformSessionTests(ITestOutputHelper output)
{
    [Fact]
    public void Both_admin_incubator_doors_ask_for_a_platform_session_before_they_write_or_claim()
    {
        var all = WriteEndpointGuardTests.AllMinimalApiEndpoints();
        foreach (var (route, write) in new[] { ("/admin/incubator/{id:guid}/answer", "svc.SaveAnswerAsync("), ("/admin/incubator/{id:guid}/analyze", "svc.TryClaimAnalysisAsync(") })
        {
            var body = WriteEndpointGuardTests.StripComments(all.Single(e => e.Route == route).Body);
            var (guard, asks, writes) = (body.IndexOf("PlatformAdminGuard.EvaluateAsync(", StringComparison.Ordinal),
                body.IndexOf("svc.LoadPlatformAsync(id) is null) return Results.NotFound();", StringComparison.Ordinal), body.IndexOf(write, StringComparison.Ordinal));
            output.WriteLine($"{route}: المُشرِف@{guard} ← جَلسَةُ المَنَصَّة@{asks} ← الكِتابَة@{writes}");
            Assert.True(guard > 0 && asks > guard && writes > asks, $"{route}: يَكتُبُ قَبلَ أَن يَسأَلَ مالِكَ الجَلسَة.");
        }
    }

    [Theory]
    [InlineData("IncubatorWizard.razor")]
    [InlineData("IncubatorStudy.razor")]
    public void Both_admin_incubator_pages_read_the_platform_session_only(string page)
    {
        var code = WriteEndpointGuardTests.StripMarkupComments(File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot,
            "libs/templates/ACommerce.Templates.Customer.Marketplace/Components/Pages/Admin", page)));
        Assert.Contains("Incubator.LoadPlatformAsync(Id)", code, StringComparison.Ordinal);
        Assert.DoesNotContain("Incubator.LoadAsync(", code, StringComparison.Ordinal);
    }
}

// ─── البُرهانُ عَلى Postgres ────────────────────────────────────────────────────────────────────────────────

[Collection(LiveProofCollection.Name)]
public class LiveAdminIncubatorCustomerSessionProofTests(ITestOutputHelper output)
{
    private static bool Enabled => LiveDealConcurrencyProofTests.Enabled && !string.IsNullOrEmpty(LiveDealConcurrencyProofTests.ConnectionString);

    [Fact]
    public async Task The_admin_doors_answer_404_on_a_customer_session_and_write_and_claim_nothing_on_Postgres()
    {
        if (!Enabled) return;
        await using var host = HostWithPhone();
        var admin = await UserAsync(host, consented: false, platformAdmin: true);
        var theirs = await PlantAsync(host, Guid.NewGuid(), IncubatorStatus.Failed);
        try
        {
            var route = new Dictionary<string, string> { ["id"] = theirs.Id.ToString() };
            var answered = await host.PostStudioFormAsync("/admin/incubator/{id:guid}/answer", route, admin.Token, ("questionId", "sector"), ("answer", "written-by-admin"));
            var analyzed = await host.PostStudioFormAsync("/admin/incubator/{id:guid}/analyze", route, admin.Token);
            var after = await ReadAsync(host, theirs.Id);
            output.WriteLine($"[live] جَلسَةُ عَميل ⇒ الإجابَة {answered.Status} · «حَلِّل» {analyzed.Status} · sector={after!.Answers.GetValueOrDefault("sector") ?? "—"} · المُحاوَلات {after.AnalysisRuns} · الحالَة {after.Status}");
            Assert.Equal(404, answered.Status);
            Assert.Equal(404, analyzed.Status);
            Assert.False(after.Answers.ContainsKey("sector"));
            Assert.Equal((0, IncubatorStatus.Failed), (after.AnalysisRuns, after.Status));
        }
        finally { await DropAsync(host, theirs.Id); await CleanAsync(host, [admin]); }
    }

    [Fact]
    public async Task The_wizard_and_the_study_say_not_found_for_a_customer_session_and_draw_no_analyze_door_on_Postgres()
    {
        if (!Enabled) return;
        await using var host = HostWithPhone();
        var admin = await UserAsync(host, consented: false, platformAdmin: true);
        var theirs = await PlantAsync(host, Guid.NewGuid(), IncubatorStatus.Failed);
        try
        {
            var wizard = await RenderAsync<IncubatorWizard>(host, admin, theirs.Id, $"/admin/incubator/{theirs.Id}");
            var study = await RenderAsync<IncubatorStudy>(host, admin, theirs.Id, $"/admin/incubator/{theirs.Id}/study");
            var notFound = LocaleCatalogText("admin.study.not_found");
            output.WriteLine($"[live] جَلسَةُ عَميل ⇒ المُعالِج «لا توجَد» {wizard.Contains(notFound)} · الدِراسَة «لا توجَد» {study.Contains(notFound)} · /analyze مَرسوم {wizard.Contains("/analyze") || study.Contains("/analyze")}");
            Assert.Contains(notFound, wizard, StringComparison.Ordinal);
            Assert.Contains(notFound, study, StringComparison.Ordinal);
            Assert.DoesNotContain($"/admin/incubator/{theirs.Id}/analyze", wizard + study, StringComparison.Ordinal);
            Assert.DoesNotContain("provider timeout (fixture)", study, StringComparison.Ordinal);
        }
        finally { await DropAsync(host, theirs.Id); await CleanAsync(host, [admin]); }
    }
}
