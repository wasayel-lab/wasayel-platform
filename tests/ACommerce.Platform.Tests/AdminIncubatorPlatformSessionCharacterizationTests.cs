using ACommerce.Kit.Auth.Server;
using ACommerce.Templates.Customer.Marketplace.Components.Pages.Admin;
using ACommerce.Templates.Customer.Marketplace.Services.Incubator;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.Abstractions;
using static ACommerce.Platform.Tests.LiveStoreBirthOnceCharacterizationTests;

namespace ACommerce.Platform.Tests;

// ═══ تَوصيفُ حاضِنَةِ المُشرِفِ عَلى جَلساتِ المَنَصَّةِ قَبلَ أَن تَرُدَّ جَلساتِ العُمَلاء (‏2026-11-07، مُراجَعَةُ عَدَساتِ C1 · W13-1·DD-3) ═══
//
// **المَقيسُ قَبل** (تَقريرُ العَدَسَة، عِندَ `cd829ab`): أَبوابُ `/admin/incubator/{id}/answer` و`/analyze` وصَفحَتا الحاضِنَةِ لا تَسأَلُ مالِكَ الجَلسَة —
// `IncubatorHome` يَعرِضُ جَلساتِ `Guid.Empty` وَحدَها، والمُعَرِّفُ في الرابِطِ يَقبَلُ أَيَّ جَلسَة. فَمُشرِفُ المَنَصَّةِ كَتَبَ `Answers.sector` في دِراسَةِ عَميل،
// وصَفحَةُ الدِراسَةِ رَسَمَت لَه زِرَّ «أَعِد» إلى `/analyze` يُرسِلُ وَصفَ العَميلِ إلى نَموذَج — والخُصوصِيَّةُ أَسقَطَت مُزَوِّدَ التَحليلِ عَلى أَنّ «وَكيلَ
// Analysis لا يَبلُغُه إلّا مُشرِفُ المَنَصَّةِ في حاضِنَتِه بِنَصِّه هُوَ».
// **وما يُثَبِّتُه هذا المِلَفُّ — وهُوَ ما يَجِبُ أَن يَنجوَ، ولا يُعادُ حَرفٌ مِنه بَعدَه** (القاعِدَة ٣): **جَلسَةُ المَنَصَّةِ نَفسُها** (المالِكُ `Guid.Empty`) —
// الإجابَةُ تُكتَبُ ويَعودُ المُشرِفُ إلى المُعالِج، و«حَلِّل» يُعيدُه إلى الدِراسَة، والصَفحَتانِ تَرسُمانِ جَلسَتَه وأَزرارَها.
// يَتَخَطّى نَفسَه ما لَم تُضبَط WASAYEL_LIVE_PROOF=1 وConnectionStrings__Postgres — عَقدُ إخوَتِه، وفي مَجموعَتِهِم التَسلسُلِيَّة.

[Collection(LiveProofCollection.Name)]
public class LiveAdminIncubatorPlatformSessionCharacterizationTests(ITestOutputHelper output)
{
    private static bool Enabled => LiveDealConcurrencyProofTests.Enabled && !string.IsNullOrEmpty(LiveDealConcurrencyProofTests.ConnectionString);

    /// <summary>جَلسَةُ حاضِنَةٍ بِمالِكِها وحالَتِها، تُكتَبُ في قِسمِ الحاضِنَة.</summary>
    internal static async Task<IncubatorSession> PlantAsync(LiveTemplateDoorHost host, Guid owner, IncubatorStatus status)
    {
        var s = new IncubatorSession
        {
            Id = Guid.NewGuid(), OwnerUserId = owner, Status = status, ProjectDescription = "وَصفُ مَشروعٍ لِلتَوصيف",
            AnalysisError = status == IncubatorStatus.Failed ? "provider timeout (fixture)" : null,
        };
        await using var w = host.Store.LightweightSession(FeasibilityAnalysisService.IncubatorTenant);
        w.Store(s);
        await w.SaveChangesAsync();
        return s;
    }

    internal static async Task<IncubatorSession?> ReadAsync(LiveTemplateDoorHost host, Guid id)
    {
        await using var q = host.Store.QuerySession(FeasibilityAnalysisService.IncubatorTenant);
        return await q.LoadAsync<IncubatorSession>(id);
    }

    internal static async Task DropAsync(LiveTemplateDoorHost host, params Guid[] ids)
    {
        await using var w = host.Store.LightweightSession(FeasibilityAnalysisService.IncubatorTenant);
        foreach (var id in ids)
        {
            w.Delete<IncubatorSession>(id);
            w.DeleteWhere<AnalysisRunClaim>(c => c.SessionId == id);
        }
        await w.SaveChangesAsync();
    }

    /// <summary>صَفحَةُ حاضِنَةٍ مُصَيَّرَةً لِلمُشرِفِ بِكوكيِّ الاستوديو — النَصُّ مَفكوكُ الكِيانات.</summary>
    internal static async Task<string> RenderAsync<TPage>(LiveTemplateDoorHost host, Probe admin, Guid id, string path) where TPage : IComponent
    {
        await using var scope = host.Services.CreateAsyncScope();
        var ctx = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        ctx.Request.Scheme = "http";
        ctx.Request.Host = new HostString("localhost");
        ctx.Request.Path = path;
        ctx.Request.Headers.Cookie = $"{StudioAuth.CookieName}={admin.Token}";
        if (scope.ServiceProvider.GetService<IHttpContextAccessor>() is { } accessor) accessor.HttpContext = ctx;
        if (scope.ServiceProvider.GetService<NavigationManager>() is Microsoft.AspNetCore.Components.Routing.IHostEnvironmentNavigationManager nav)
            nav.Initialize("http://localhost/", "http://localhost" + path);
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, NullLoggerFactory.Instance);
        return System.Net.WebUtility.HtmlDecode(await renderer.Dispatcher.InvokeAsync(async () =>
            (await renderer.RenderComponentAsync<TPage>(ParameterView.FromDictionary(new Dictionary<string, object?> { ["Id"] = id }))).ToHtmlString()));
    }

    [Fact]
    public async Task The_admin_answers_his_own_platform_session_and_returns_to_the_wizard_on_Postgres()
    {
        if (!Enabled) return;
        await using var host = HostWithPhone();
        var admin = await UserAsync(host, consented: false, platformAdmin: true);
        var mine = await PlantAsync(host, Guid.Empty, IncubatorStatus.Discovery);
        try
        {
            var answered = await host.PostStudioFormAsync("/admin/incubator/{id:guid}/answer", new Dictionary<string, string> { ["id"] = mine.Id.ToString() },
                admin.Token, ("questionId", "sector"), ("answer", "written-by-admin"));
            var after = await ReadAsync(host, mine.Id);
            output.WriteLine($"[live] جَلسَةُ المَنَصَّة ⇒ {answered.Status} {answered.Location} · sector={after!.Answers.GetValueOrDefault("sector")}");
            Assert.Equal((302, $"/admin/incubator/{mine.Id}"), answered);
            Assert.Equal("written-by-admin", after.Answers["sector"]);
        }
        finally { await DropAsync(host, mine.Id); await CleanAsync(host, [admin]); }
    }

    [Fact]
    public async Task The_admin_analyze_door_returns_him_to_his_platform_study_on_Postgres()
    {
        if (!Enabled) return;
        await using var host = HostWithPhone();
        var admin = await UserAsync(host, consented: false, platformAdmin: true);
        var mine = await PlantAsync(host, Guid.Empty, IncubatorStatus.Failed);
        try
        {
            var analyzed = await host.PostStudioFormAsync("/admin/incubator/{id:guid}/analyze", new Dictionary<string, string> { ["id"] = mine.Id.ToString() },
                admin.Token);
            var after = await ReadAsync(host, mine.Id);
            output.WriteLine($"[live] «حَلِّل» عَلى جَلسَةِ المَنَصَّة ⇒ {analyzed.Status} {analyzed.Location} · المُحاوَلات {after!.AnalysisRuns}");
            Assert.Equal((302, $"/admin/incubator/{mine.Id}/study"), analyzed);
            Assert.Equal(1, after.AnalysisRuns);
        }
        finally { await DropAsync(host, mine.Id); await CleanAsync(host, [admin]); }
    }

    [Fact]
    public async Task The_wizard_and_the_study_draw_the_platform_session_and_its_doors_on_Postgres()
    {
        if (!Enabled) return;
        await using var host = HostWithPhone();
        var admin = await UserAsync(host, consented: false, platformAdmin: true);
        var failed = await PlantAsync(host, Guid.Empty, IncubatorStatus.Failed);
        try
        {
            var wizard = await RenderAsync<IncubatorWizard>(host, admin, failed.Id, $"/admin/incubator/{failed.Id}");
            var study = await RenderAsync<IncubatorStudy>(host, admin, failed.Id, $"/admin/incubator/{failed.Id}/study");
            var notFound = LocaleCatalogText("admin.study.not_found");
            output.WriteLine($"[live] المُعالِج {wizard.Length} حَرفاً · الدِراسَة {study.Length} حَرفاً · «لا توجَد» في أَيٍّ مِنهُما: {wizard.Contains(notFound) || study.Contains(notFound)}");
            Assert.Contains($"action=\"/admin/incubator/{failed.Id}/analyze\"", wizard, StringComparison.Ordinal);
            Assert.Contains($"action=\"/admin/incubator/{failed.Id}/analyze\"", study, StringComparison.Ordinal);
            Assert.DoesNotContain(notFound, wizard, StringComparison.Ordinal);
            Assert.DoesNotContain(notFound, study, StringComparison.Ordinal);
        }
        finally { await DropAsync(host, failed.Id); await CleanAsync(host, [admin]); }
    }

    internal static string LocaleCatalogText(string key)
        => ACommerce.Platform.I18n.LocaleCatalog.Find(ACommerce.Platform.I18n.LocaleCatalog.Arabic, key)!;
}
