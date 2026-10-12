using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.RegularExpressions;
using ACommerce.Platform.Hosting;
using ACommerce.Platform.I18n;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace ACommerce.Platform.Tests;

// ═══ صَفحَةُ خَطَإٍ واحِدَةٌ بِنُصوصِ القامُوسِ وهُوِيَّةِ البِناء (‏2026-10-12، موجزُ الفجوات P3-4) ══════════════════════
//
// **المَقيسُ قَبل** — ‏V1.App مَنشوراً بِوَضعِ Production عَلى قاعِدَةٍ مَحَلِّيَّة: `/x/y/z/w` ⇒ ‏404 بِـ‏0 بايت؛ ثُمَّ القاعِدَةُ
// تُغلَقُ تَحتَه (`allow_connections false` + `pg_terminate_backend`): `/` ⇒ ‏500 بِـ‏0 بايت، و`/ejar` ⇒ ‏500 بِـ‏0 بايت،
// و`/health` ⇒ ‏200. و`grep UseExceptionHandler|UseStatusCodePages` في `libs/` و`apps/` = صِفر.
//
// **وما يُقاسُ هُنا عَلى HTTP حَقيقيّ** (نَمَطُ `BuildIdentityEndpointTests` — بِلا `Mvc.Testing`): الصَفحَةُ لِلمُتَصَفِّح،
// ولا شَيءَ مِن الاستِثناءِ فيها، وما يَطلُبُه غَيرُ المُتَصَفِّحِ لا يَتَغَيَّرُ بايتاً، والرَدُّ الَّذي يَحمِلُ جِسمَه لا يُمَسّ،
// وDevelopment يَحتَفِظُ بِصَفحَةِ المُطَوِّر. **وما يُقاسُ بِقِراءَةِ المَصدَر**: المَفاتيحُ في القامُوس، والمُعالِجانِ
// مُرَكَّبانِ في `Program.cs` بِالتَرتيبِ الَّذي يَلُفُّ الأُنبوبَ كُلَّه.

file sealed class ErrorHost : IAsyncDisposable
{
    private readonly WebApplication _app;

    public HttpClient Client { get; }

    private ErrorHost(WebApplication app, HttpClient client)
    {
        _app = app;
        Client = client;
    }

    private static int FreePort()
    {
        using var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    /// <summary>مُضيفٌ يُرَكِّبُ <c>UsePlatformErrorPages</c> قَبلَ نِقاطِه — كَما يُرَكِّبُه <c>Program.cs</c> قَبلَ
    /// <c>UsePlatformHost</c> — ومَعَه <c>/health</c> لِيُقارَنَ البِناءانِ مِن مُضيفٍ واحِد. والإعادَةُ عِندَ تَصادُمِ المَنفَذِ
    /// لِلعِلَّةِ المَقيسَةِ في <c>BuildIdentityEndpointTests</c>.</summary>
    public static async Task<ErrorHost> StartAsync(string environment, string? informationalVersion)
    {
        for (var attempt = 1; ; attempt++)
        {
            var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { EnvironmentName = environment });
            builder.Logging.ClearProviders();
            builder.Services.AddHttpContextAccessor();
            builder.Services.AddScoped<L>();

            var port = FreePort();
            var app = builder.Build();
            app.Urls.Add($"http://127.0.0.1:{port}");

            app.UsePlatformErrorPages(informationalVersion);

            app.MapBuildIdentity(informationalVersion, new DateTimeOffset(2026, 10, 12, 6, 0, 0, TimeSpan.Zero));
            app.MapGet("/boom", string () => throw new InvalidOperationException(PlatformErrorPagesTests.Secret));
            app.MapGet("/own-404", () => Results.Text("own body", "text/plain", statusCode: 404));
            app.MapGet("/empty-401", () => Results.Unauthorized());

            try
            {
                await app.StartAsync();
            }
            catch (IOException) when (attempt < 5)
            {
                await app.DisposeAsync();
                continue;
            }

            var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
            {
                BaseAddress = new Uri($"http://127.0.0.1:{port}"),
                Timeout = TimeSpan.FromSeconds(20),
            };
            return new ErrorHost(app, client);
        }
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}

public class PlatformErrorPagesTests
{
    internal const string Secret = "boom-error-page-7f3a91c2";
    private const string Sha = "1d2e3f4a5b6c7d8e9f0a1b2c3d4e5f6a7b8c9d0e";
    private static readonly string Version = "1.0.0+" + Sha;

    private static string RepoRoot => ThemeZeroEquivalenceTests.RepoRoot;

    private static string Read(params string[] parts)
    {
        var text = File.ReadAllText(Path.Combine(RepoRoot, Path.Combine(parts)));
        // ‏أَقصَرُها `Routes.razor` (‏443 بايتاً) — والحَدُّ يُمَيِّزُ «قُرِئَ» مِن «فارِغ» لا أَكثَر.
        Assert.True(text.Length > 100, $"أَداةٌ عَمياء: {Path.Combine(parts)} لَم يُقرَأ.");
        return text;
    }

    private static HttpRequestMessage Html(HttpMethod method, string path, string? langCookie = null)
    {
        var req = new HttpRequestMessage(method, path);
        // ‏ما يُرسِلُه Chrome في التَنَقُّلِ حَرفاً.
        req.Headers.Accept.ParseAdd("text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
        if (langCookie is not null)
            req.Headers.Add("Cookie", $"{L.CookieName}={langCookie}");
        return req;
    }

    private static string Text(string key) => LocaleCatalog.Text(LocaleCatalog.Arabic, key);

    // ─── ١) المَفاتيحُ في القامُوس — بِقيمَةٍ لا نائِب ────────────────────

    [Fact]
    public void Every_key_the_page_reads_is_in_the_arabic_dictionary()
    {
        Assert.True(ErrorPages.Keys.Count >= 9, $"أَداةٌ عَمياء: {ErrorPages.Keys.Count} مِفتاحاً.");
        foreach (var key in ErrorPages.Keys)
        {
            var value = LocaleCatalog.Find(LocaleCatalog.Arabic, key);
            Assert.False(string.IsNullOrWhiteSpace(value), $"المِفتاحُ «{key}» غائِبٌ عَن ar.json.");
            Assert.False(LocaleCatalog.IsPlaceholder(value), $"المِفتاحُ «{key}» نائِبٌ لا نَصّ.");
        }
    }

    /// <summary>والمِفتاحُ الَّذي يَحمِلُه <c>Routes.razor</c> لِـ«غَيرُ مَوجود» هُوَ عُنوانُ ‏404 هُنا — نَصٌّ واحِدٌ
    /// لِلحالَةِ الواحِدَة، لا اثنان يَنجَرِفان.</summary>
    [Fact]
    public void The_not_found_title_is_the_key_the_router_already_shows()
    {
        Assert.Contains("shell.error.not_found", ErrorPages.Keys);
        Assert.Contains("L.Markup(\"shell.error.not_found\")",
            Read("libs", "templates", "ACommerce.Templates.Customer.Marketplace", "Components", "Routes.razor"),
            StringComparison.Ordinal);
    }

    // ─── ٢) الصِنفُ مِن الرَمز — مَجموعَةٌ مُغلَقَة ─────────────────────────

    [Theory]
    [InlineData(404, ErrorPageKind.NotFound)]
    [InlineData(400, ErrorPageKind.Request)]
    [InlineData(401, ErrorPageKind.Request)]
    [InlineData(403, ErrorPageKind.Request)]
    [InlineData(405, ErrorPageKind.Request)]
    [InlineData(409, ErrorPageKind.Request)]
    [InlineData(500, ErrorPageKind.Server)]
    [InlineData(502, ErrorPageKind.Server)]
    [InlineData(503, ErrorPageKind.Server)]
    public void Each_status_falls_in_one_kind(int status, ErrorPageKind kind)
        => Assert.Equal(kind, ErrorPages.KindFor(status));

    // ─── ٣) الصَفحَةُ — دالَّةٌ نَقِيَّة ───────────────────────────────────

    [Fact]
    public void Render_names_the_failure_the_status_the_build_and_the_way_home()
    {
        var page = ErrorPages.Render(500, Sha, LocaleCatalog.Arabic);

        Assert.StartsWith("<!DOCTYPE html>", page, StringComparison.Ordinal);
        Assert.Contains("<html lang=\"ar\" dir=\"rtl\">", page, StringComparison.Ordinal);
        Assert.Contains($"<h1>{Text("shell.error.server_title")}</h1>", page, StringComparison.Ordinal);
        Assert.Contains(Text("shell.error.server_hint"), page, StringComparison.Ordinal);
        Assert.Contains($"<a href=\"/\">{Text("shell.error.home")}</a>", page, StringComparison.Ordinal);
        Assert.Contains($"{Text("shell.error.status")} 500", page, StringComparison.Ordinal);
        Assert.Contains($"{Text("shell.error.build")} <code dir=\"ltr\">{Sha}</code>", page, StringComparison.Ordinal);

        // ‏والعَرَبِيَّةُ تُكتَبُ حَرفاً لا مَراجِعَ رَقمِيَّة — `HtmlEncoder.Default` كانَ سَيَكتُبُها `&#x...;`.
        Assert.DoesNotContain("&#x", page, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_of_a_404_and_of_a_405_say_different_things()
    {
        var notFound = ErrorPages.Render(404, Sha, LocaleCatalog.Arabic);
        var notAllowed = ErrorPages.Render(405, Sha, LocaleCatalog.Arabic);

        Assert.Contains(Text("shell.error.not_found"), notFound, StringComparison.Ordinal);
        Assert.Contains(Text("shell.error.not_found_hint"), notFound, StringComparison.Ordinal);
        Assert.Contains(Text("shell.error.request_title"), notAllowed, StringComparison.Ordinal);
        Assert.DoesNotContain(Text("shell.error.not_found"), notAllowed, StringComparison.Ordinal);
    }

    /// <summary><b>الحَدُّ الأَوَّلُ بِالبِنيَةِ لا بِالحِرص</b>: لا دالَّةَ عامَّةٌ في <see cref="ErrorPages"/> تَأخُذُ
    /// استِثناءً — فَلا طَريقَ لِرِسالَتِه ولا لِمُكَدَّسِه إلى الصَفحَة. (عَدّادٌ: الدَوالُّ المَفحوصَةُ لا تَكونُ صِفراً.)</summary>
    [Fact]
    public void No_public_member_takes_an_exception_so_none_can_print_one()
    {
        var methods = typeof(ErrorPages).GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
        Assert.True(methods.Length >= 5, $"أَداةٌ عَمياء: {methods.Length} دالَّة.");
        foreach (var m in methods)
            Assert.DoesNotContain(m.GetParameters(), p => typeof(Exception).IsAssignableFrom(p.ParameterType));
    }

    /// <summary>لُغَةٌ لا تَحمِلُ مَفاتيحَ الصَفحَةِ كُلَّها لا تُكتَبُ بِها: نَصٌّ عَرَبِيٌّ في <c>dir="ltr"</c> هُوَ عَطَبُ
    /// زِرِّ اللُغَةِ في القاعِدَة ١٢. والمُتَوَقَّعُ يُحسَبُ مِن القامُوسِ نَفسِه — فَتَرجَمَةُ المَفاتيحِ غَداً لا تُحمِرُه.</summary>
    [Fact]
    public void A_language_missing_any_page_key_falls_back_to_arabic()
    {
        Assert.Equal(LocaleCatalog.Arabic, ErrorPages.LanguageFor(null));
        Assert.Equal(LocaleCatalog.Arabic, ErrorPages.LanguageFor("zz"));

        var enComplete = ErrorPages.Keys.All(k => LocaleCatalog.Find("en", k) is not null);
        Assert.Equal(enComplete ? "en" : LocaleCatalog.Arabic, ErrorPages.LanguageFor("en"));
    }

    // ─── ٤) عَلى HTTP — Production ────────────────────────────────────────

    [Fact]
    public async Task An_exception_renders_the_page_and_nothing_of_the_exception()
    {
        await using var host = await ErrorHost.StartAsync("Production", Version);
        using var res = await host.Client.SendAsync(Html(HttpMethod.Get, "/boom"));
        var body = await res.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, res.StatusCode);
        Assert.Equal("text/html", res.Content.Headers.ContentType?.MediaType);
        Assert.Contains(Text("shell.error.server_title"), body, StringComparison.Ordinal);
        Assert.Contains(Sha, body, StringComparison.Ordinal);

        Assert.DoesNotContain(Secret, body, StringComparison.Ordinal);
        Assert.DoesNotContain("InvalidOperationException", body, StringComparison.Ordinal);
        Assert.DoesNotMatch(new Regex(@"\bat [\w.]+\("), body);
    }

    [Fact]
    public async Task The_page_names_the_same_build_as_health()
    {
        foreach (var version in new[] { Version, null })
        {
            await using var host = await ErrorHost.StartAsync("Production", version);
            var health = await host.Client.GetStringAsync("/health");
            var commit = Regex.Match(health, "\"commit\":\"(?<c>[^\"]+)\"").Groups["c"].Value;
            Assert.False(string.IsNullOrEmpty(commit), health);

            using var res = await host.Client.SendAsync(Html(HttpMethod.Get, "/boom"));
            Assert.Contains($"<code dir=\"ltr\">{commit}</code>", await res.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task A_missing_page_renders_the_not_found_page()
    {
        await using var host = await ErrorHost.StartAsync("Production", Version);
        using var res = await host.Client.SendAsync(Html(HttpMethod.Get, "/x/y/z/w"));
        var body = await res.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        Assert.Contains($"<h1>{Text("shell.error.not_found")}</h1>", body, StringComparison.Ordinal);
        Assert.Contains($"{Text("shell.error.status")} 404", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_page_is_not_cacheable()
    {
        await using var host = await ErrorHost.StartAsync("Production", Version);
        foreach (var path in new[] { "/boom", "/x/y/z/w" })
        {
            using var res = await host.Client.SendAsync(Html(HttpMethod.Get, path));
            Assert.Contains("no-store", res.Headers.CacheControl?.ToString() ?? "", StringComparison.Ordinal);
        }
    }

    /// <summary><b>الحَدُّ الثاني</b>: <c>fetch()</c> وعُمَلاءُ الـAPI و<c>curl</c> يَأخُذونَ ما كانوا يَأخُذون — الرَمزَ بِلا
    /// جِسم. المَقيسُ قَبلَ التَغيير: ‏404 بِـ‏0 بايت و‏500 بِـ‏0 بايت.</summary>
    [Theory]
    [InlineData("/boom", 500, null)]
    [InlineData("/boom", 500, "application/json")]
    [InlineData("/boom", 500, "*/*")]
    [InlineData("/x/y/z/w", 404, null)]
    [InlineData("/x/y/z/w", 404, "application/json")]
    [InlineData("/empty-401", 401, "*/*")]
    public async Task A_client_that_does_not_read_html_gets_the_bare_status_as_before(string path, int status, string? accept)
    {
        await using var host = await ErrorHost.StartAsync("Production", Version);
        using var req = new HttpRequestMessage(HttpMethod.Get, path);
        if (accept is not null)
            req.Headers.Accept.Add(MediaTypeWithQualityHeaderValue.Parse(accept));
        using var res = await host.Client.SendAsync(req);

        Assert.Equal(status, (int)res.StatusCode);
        Assert.Empty(await res.Content.ReadAsByteArrayAsync());
    }

    /// <summary>ورَدٌّ يَحمِلُ جِسمَه لا يُمَسّ ولو طَلَبَه مُتَصَفِّح — شَرطُ <c>UseStatusCodePages</c> نَفسِه، مَقيساً.</summary>
    [Fact]
    public async Task A_response_that_carries_its_own_body_is_left_alone()
    {
        await using var host = await ErrorHost.StartAsync("Production", Version);
        using var res = await host.Client.SendAsync(Html(HttpMethod.Get, "/own-404"));

        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        Assert.Equal("own body", await res.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task An_empty_4xx_for_a_browser_gets_the_request_page_not_the_not_found_page()
    {
        await using var host = await ErrorHost.StartAsync("Production", Version);
        using var res = await host.Client.SendAsync(Html(HttpMethod.Get, "/empty-401"));
        var body = await res.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
        Assert.Contains(Text("shell.error.request_title"), body, StringComparison.Ordinal);
        Assert.DoesNotContain(Text("shell.error.not_found"), body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Head_gets_the_status_and_the_type_without_a_body()
    {
        await using var host = await ErrorHost.StartAsync("Production", Version);
        using var res = await host.Client.SendAsync(Html(HttpMethod.Head, "/x/y/z/w"));

        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        Assert.Equal("text/html", res.Content.Headers.ContentType?.MediaType);
        Assert.Empty(await res.Content.ReadAsByteArrayAsync());
    }

    /// <summary>كوكي اللُغَةِ يُقرَأُ بِـ<see cref="L"/> نَفسِه — ولُغَةٌ لا تَحمِلُ مَفاتيحَ الصَفحَةِ كُلَّها تَسقُطُ إلى العَرَبِيَّةِ
    /// اتِّجاهاً ونَصّاً مَعاً.</summary>
    [Fact]
    public async Task The_language_cookie_is_read_through_L_and_never_splits_text_from_direction()
    {
        await using var host = await ErrorHost.StartAsync("Production", Version);
        using var res = await host.Client.SendAsync(Html(HttpMethod.Get, "/x/y/z/w", langCookie: "en"));
        var body = await res.Content.ReadAsStringAsync();

        var lang = ErrorPages.LanguageFor("en");
        var dir = lang == LocaleCatalog.Arabic ? "rtl" : "ltr";
        Assert.Contains($"<html lang=\"{lang}\" dir=\"{dir}\">", body, StringComparison.Ordinal);
        Assert.Contains(LocaleCatalog.Text(lang, "shell.error.not_found"), body, StringComparison.Ordinal);
    }

    // ─── ٥) Development يَحتَفِظُ بِصَفحَةِ المُطَوِّر ─────────────────────────

    /// <summary><b>والحَدُّ في الاتِّجاهِ الآخَر</b>: في Development لا يُرَكَّبُ شَيء، فَصَفحَةُ المُطَوِّرِ الَّتي يُضيفُها
    /// <c>WebApplication</c> تَبقى تَقولُ الاستِثناءَ بِاسمِه — وهي الَّتي تُشَخِّصُ عَلى جِهازِ التَطوير.</summary>
    [Fact]
    public async Task Development_keeps_the_developer_exception_page()
    {
        await using var host = await ErrorHost.StartAsync("Development", Version);
        using var res = await host.Client.SendAsync(Html(HttpMethod.Get, "/boom"));
        var body = await res.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, res.StatusCode);
        Assert.Contains(Secret, body, StringComparison.Ordinal);
        Assert.DoesNotContain(Text("shell.error.server_title"), body, StringComparison.Ordinal);
    }

    // ─── ٦) التَركيبُ في `Program.cs` ومَصدَرُ الوَحدَة ────────────────────

    /// <summary>بَعدَ <c>UseForwardedHeaders</c> وقَبلَ <c>UsePlatformHost</c> — فَيَلُفُّ حَلَّ المُستَأجِرِ والنِقاطَ ومُصَيِّرَ
    /// Razor؛ <b>والسِلسِلَةُ نَفسُها</b> تُمَرَّرُ إلى الصَفحَةِ وإلى <c>/health</c>.</summary>
    [Fact]
    public void Program_installs_the_error_pages_around_the_whole_pipeline()
    {
        var program = Read("apps", "V1.App", "Program.cs");

        var forwarded = program.IndexOf("app.UseForwardedHeaders();", StringComparison.Ordinal);
        var pages = program.IndexOf("app.UsePlatformErrorPages(buildVersion);", StringComparison.Ordinal);
        var host = program.IndexOf("app.UsePlatformHost();", StringComparison.Ordinal);
        Assert.True(forwarded > 0 && pages > forwarded && host > pages,
            $"التَرتيب: UseForwardedHeaders@{forwarded} ثُمَّ UsePlatformErrorPages@{pages} ثُمَّ UsePlatformHost@{host}.");

        Assert.Contains("app.MapBuildIdentity(buildVersion, processStartedAt);", program, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(program, @"GetCustomAttribute<AssemblyInformationalVersionAttribute>\(\)"));
    }

    /// <summary>المُعالِجانِ مُرَكَّبانِ خارِجَ Development، <b>ولا إعادَةَ تَنفيذٍ عَبرَ Razor</b>: <c>App.razor</c> يَقرَأُ
    /// القاعِدَة، فَمَسارٌ يُعيدُ إلى صَفحَةٍ يَسقُطُ في الانقِطاعِ الَّذي وُجِدَت الصَفحَةُ لَه.</summary>
    [Fact]
    public void The_module_installs_both_handlers_outside_development_and_reexecutes_nothing()
    {
        var src = Read("libs", "core", "ACommerce.Platform.Hosting", "ErrorPages.cs");
        var code = string.Join('\n', src.Split('\n').Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal)));

        Assert.Contains("app.UseExceptionHandler(", code, StringComparison.Ordinal);
        Assert.Contains("app.UseStatusCodePages(", code, StringComparison.Ordinal);
        Assert.Contains("if (app.Environment.IsDevelopment())", code, StringComparison.Ordinal);
        Assert.DoesNotContain("WithReExecute", code, StringComparison.Ordinal);
        Assert.DoesNotContain("ExceptionHandlingPath", code, StringComparison.Ordinal);
        Assert.DoesNotContain("WithRedirects", code, StringComparison.Ordinal);

        // ‏و`App.razor` ما زالَ يَقرَأُ القاعِدَة — السَبَبُ الَّذي أَخرَجَ الصَفحَةَ مِن Razor قائِم.
        Assert.Contains("Themes.EffectiveAsync(",
            Read("libs", "templates", "ACommerce.Templates.Customer.Marketplace", "Components", "App.razor"),
            StringComparison.Ordinal);
    }
}
