using System.Text.RegularExpressions;
using ACommerce.Kit.Auth.Server;
using ACommerce.Templates.Customer.Marketplace.Services.Incubator;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ تَوصيفُ صَفحَةِ الدِراسَةِ القَديمَةِ — ما يَبقى مِنها قَبلَ أَن تَكُفَّ عَن وَعدِ «تُحَدَّث تِلقائيّاً» (‏2026-11-07، الموجَة ١٣ — مُراجَعَةُ C1 · W13-1·F7) ═══
//
// **المَقيسُ قَبل** (عِندَ `71ca286`): `5ba4cac` أَزالَ تَحديثَ الصَفحَةِ كُلَّ أَربَعِ ثَوانٍ وكُلَّ بابٍ يُخرِجُ جَلسَةً مِن `Analyzing` (`/analyze` وإعادَةُ
// `Failed`)؛ وكانَ `/analyze` القَديمُ يَقبَلُ أَيَّ حالٍ — ومِنها `Completed` الَّتي بُنِيَ مِنها مَتجَر — وفَشَلُ الخَلفِيَّةِ يَترُكُ الحالَ `Analyzing`.
// فَدِراسَةٌ كَهذِه، تُفتَحُ مِن شَريطِ «مَبنيّ عَلى فِكرَة» في لَوحَةِ المَتجَر، تَقولُ «جارٍ تَحليل فِكرَتِكَ… تُحَدَّث الصَفحَة تِلقائيّاً» إلى الأَبَد.
// **وما يُثَبِّتُه هذا المِلَفُّ — ما يَنجو مِن التَبديل، ولا يُعادُ حَرفٌ مِنه بَعدَه** (القاعِدَة ٣): الفُروعُ الأُخرى لِلصَفحَةِ نَفسِها مُصَيَّرَةً بِخِدماتِ
// المُضيفِ عَلى Postgres — `Failed` بِعُنوانِه ونَصِّ خَطَئِه، والجَلسَةُ بِلا تَحليلٍ بِنَصِّها، ودِراسَةُ غَيرِ صاحِبِها «غَير مَوجودَة» — وكُلُّها بِلا استِمارَةٍ
// ولا تَحديثٍ تِلقائيّ. وفَرعُ `Completed` تُثَبِّتُه لَقطَةُ المَظهَرِ `studio-study` بايتاً.

[Collection(LiveProofCollection.Name)]
public class LiveStudioStudyReadOnlyCharacterizationTests(ITestOutputHelper output)
{
    private static bool Enabled => LiveDealConcurrencyProofTests.Enabled && !string.IsNullOrEmpty(LiveDealConcurrencyProofTests.ConnectionString);

    internal static async Task<(Guid Session, Guid Owner, string Token)> SeedAsync(LiveTemplateDoorHost host, IncubatorStatus status, string? error = null)
    {
        var owner = new StudioUser { Id = Guid.NewGuid(), Phone = "zz-w13s-" + Guid.NewGuid().ToString("N")[..8], FullName = "صاحِبُ الدِراسَة" };
        await using (var s = host.Store.LightweightSession(StudioAuth.Tenant)) { s.Store(owner); await s.SaveChangesAsync(); }
        var session = new IncubatorSession
        {
            Id = Guid.NewGuid(), OwnerUserId = owner.Id, Status = status, AnalysisError = error,
            ProjectDescription = "سوقٌ لِبائِعينَ كُثُر",
        };
        await using (var s = host.Store.LightweightSession(FeasibilityAnalysisService.IncubatorTenant)) { s.Store(session); await s.SaveChangesAsync(); }
        return (session.Id, owner.Id, AuthHandlers.MakeToken(owner.Id, StudioAuth.Tenant, owner.SessionVersion));
    }

    internal static async Task CleanAsync(LiveTemplateDoorHost host, Guid session, params Guid[] owners)
    {
        await using (var s = host.Store.LightweightSession(FeasibilityAnalysisService.IncubatorTenant)) { s.Delete<IncubatorSession>(session); await s.SaveChangesAsync(); }
        await using (var s = host.Store.LightweightSession(StudioAuth.Tenant)) { foreach (var o in owners) s.Delete<StudioUser>(o); await s.SaveChangesAsync(); }
    }

    /// <summary>الصَفحَةُ مُصَيَّرَةً بِخِدماتِ المُضيفِ وكوكيِّ الاستوديو — كَما يُصَيِّرُ <c>LiveStudioStoreCreateProofTests</c> استِمارَةَ الإنشاء.</summary>
    internal static async Task<string> RenderAsync(LiveTemplateDoorHost host, Guid id, string token)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var path = $"/studio/s/{id}";
        var ctx = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        ctx.Request.Scheme = "http";
        ctx.Request.Host = new HostString("localhost");
        ctx.Request.Path = path;
        ctx.Request.Headers.Cookie = $"{StudioAuth.CookieName}={token}";
        if (scope.ServiceProvider.GetService<IHttpContextAccessor>() is { } accessor) accessor.HttpContext = ctx;
        if (scope.ServiceProvider.GetService<NavigationManager>() is Microsoft.AspNetCore.Components.Routing.IHostEnvironmentNavigationManager nav)
            nav.Initialize("http://localhost/", "http://localhost" + path);
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, NullLoggerFactory.Instance);
        return System.Net.WebUtility.HtmlDecode(await renderer.Dispatcher.InvokeAsync(async () =>
            (await renderer.RenderComponentAsync<ACommerce.Templates.Customer.Marketplace.Components.Pages.StudioStudy>(
                ParameterView.FromDictionary(new Dictionary<string, object?> { ["Id"] = id }))).ToHtmlString()));
    }

    internal static string Ar(string key) => System.Net.WebUtility.HtmlDecode(ACommerce.Platform.I18n.LocaleCatalog.Find(ACommerce.Platform.I18n.LocaleCatalog.Arabic, key)!);

    /// <summary>ما لا تَرسُمُه صَفحَةُ قِراءَةٍ أَبَداً: استِمارَةٌ تَكتُب (عَدا خُروجِ قِشرَةِ الاستوديو)، وتَحديثٌ تِلقائيّ.</summary>
    internal static void ReadOnly(string html)
    {
        var forms = Regex.Matches(html, @"<form [^>]*action=""(?<a>[^""]*)""").Select(m => m.Groups["a"].Value).ToList();
        Assert.Equal(new[] { "/studio/logout" }, forms);
        Assert.DoesNotMatch(new Regex(@"http-equiv=""refresh""", RegexOptions.IgnoreCase), html);
    }

    [Fact]
    public async Task A_failed_study_draws_its_title_and_error_and_nothing_that_writes_on_Postgres()
    {
        if (!Enabled) return;
        await using var host = LiveTemplateDoorHost.Build();
        var (id, owner, token) = await SeedAsync(host, IncubatorStatus.Failed, "انقَطَعَ الاتِّصالُ بِالمُزَوِّد");
        try
        {
            var html = await RenderAsync(host, id, token);
            output.WriteLine($"[live] Failed ⇒ {html.Length} حَرفاً");
            Assert.Contains(Ar("studio.study.failed_title"), html, StringComparison.Ordinal);
            Assert.Contains("انقَطَعَ الاتِّصالُ بِالمُزَوِّد", html, StringComparison.Ordinal);
            ReadOnly(html);
        }
        finally { await CleanAsync(host, id, owner); }
    }

    [Fact]
    public async Task A_study_without_an_analysis_says_so_and_draws_nothing_that_writes_on_Postgres()
    {
        if (!Enabled) return;
        await using var host = LiveTemplateDoorHost.Build();
        var (id, owner, token) = await SeedAsync(host, IncubatorStatus.Discovery);
        try
        {
            var html = await RenderAsync(host, id, token);
            output.WriteLine($"[live] Discovery ⇒ {html.Length} حَرفاً");
            Assert.Contains(Ar("studio.study.no_analysis"), html, StringComparison.Ordinal);
            ReadOnly(html);
        }
        finally { await CleanAsync(host, id, owner); }
    }

    [Fact]
    public async Task Another_owners_study_is_not_found_on_Postgres()
    {
        if (!Enabled) return;
        await using var host = LiveTemplateDoorHost.Build();
        var (id, owner, _) = await SeedAsync(host, IncubatorStatus.Failed, "سِرُّ صاحِبِها");
        var stranger = new StudioUser { Id = Guid.NewGuid(), Phone = "zz-w13s-" + Guid.NewGuid().ToString("N")[..8], FullName = "غَريب" };
        await using (var s = host.Store.LightweightSession(StudioAuth.Tenant)) { s.Store(stranger); await s.SaveChangesAsync(); }
        try
        {
            var html = await RenderAsync(host, id, AuthHandlers.MakeToken(stranger.Id, StudioAuth.Tenant, stranger.SessionVersion));
            output.WriteLine($"[live] غَيرُ صاحِبِها ⇒ {html.Length} حَرفاً");
            Assert.Contains(Ar("studio.study.not_found"), html, StringComparison.Ordinal);
            Assert.DoesNotContain("سِرُّ صاحِبِها", html, StringComparison.Ordinal);
            ReadOnly(html);
        }
        finally { await CleanAsync(host, id, owner, stranger.Id); }
    }
}
