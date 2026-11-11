using System.Net;
using ACommerce.Kit.Tenants;
using ACommerce.Platform.MultiTenancy;
using ACommerce.Platform.Shared;
using Marten;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ تَوصيفُ مَنطِقَةِ الإشرافِ عَلى مُضيفِ المَتجَرِ لِما يَنجو (‏2026-11-07، مُراجَعَةُ تَيّارِ C2a الجَولَةُ الأُولى · C2a-P-01 — القاعِدَة ٣) ═══
//
// **المَقيسُ قَبل** (عِندَ `14c6f29`، خادِمٌ حَيٌّ بِـ`MultiTenancy__BaseDomain=localhost`): صاحِبُ مَتجَرِ الرِحلَةِ `rewalk-111201` — وقَد مُنِحَ `tenant_admin`
// فيها — دَخَلَ عَلى `rewalk-111201.localhost:5060` فَكوكيُّه `.acommerce.auth.rewalk-111201` مَقصورٌ عَلى ذلك المُضيف؛ و`GET /manage` ← 200 بِسِتِّ بِطاقاتٍ إلى
// `/admin/tenants/rewalk-111201/…?from=tenant`؛ وبِطاقَةُ الفِئاتِ ← 301 إلى `http://localhost:5060/admin/tenants/rewalk-111201/categories?from=tenant` حَيثُ لا
// يُرسَلُ الكوكي ← «غَير مُصَرَّح». **وما يُثَبِّتُه هذا المِلَفُّ — وهُوَ ما يَجِبُ أَن يَنجوَ مِن الإصلاح، ولا يُعادُ حَرفٌ مِنه بَعدَه**: كُلُّ مَسارٍ في مَنطِقَةِ
// المَنَصَّةِ غَيرُ `/admin/tenants/{مَتجَرِ المُضيف}/…` يَبقى 301 إلى الجَذرِ لِـGET و404 لِلكِتابَة — ومِنه `/admin/tenants/{مَتجَرٍ آخَر}/…` وفِخاخُ البادِئَة
// (`ashare-x`، `ashar`، `tenantsx`) وصَفَحاتُ الاستوديو لِلمَتجَرِ نَفسِه — والجَذرُ يَمُرُّ كَما هُوَ بِلا سِياقِ مَتجَر. **وما يَتَغَيَّرُ عَمداً لا يَرِد**.

file sealed class AdminAreaProbe : IAsyncDisposable
{
    private readonly WebApplication _app;
    public HttpClient Client { get; }
    private AdminAreaProbe(WebApplication app, HttpClient client) { _app = app; Client = client; }

    public static async Task<AdminAreaProbe> StartAsync(string baseDomain, IDocumentStore store)
    {
        for (var attempt = 1; ; attempt++)
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.Logging.ClearProviders();
            builder.Services.AddSingleton(new SubdomainTenancyOptions { BaseDomain = baseDomain });
            builder.Services.AddSingleton(store);
            builder.Services.AddMemoryCache();
            using var l = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
            l.Start(); var port = ((IPEndPoint)l.LocalEndpoint).Port; l.Stop();
            var app = builder.Build();
            app.Urls.Add($"http://127.0.0.1:{port}");
            app.UseSubdomainTenantPath();
            app.UsePlatformMultiTenancy();
            app.Run(ctx => ctx.Response.WriteAsync($"{ctx.Request.Path}{ctx.Request.QueryString}|tenant={ctx.Items[TenantKeys.Slug]}"));
            try { await app.StartAsync(); }
            catch (IOException) when (attempt < 5) { await app.DisposeAsync(); continue; }
            return new AdminAreaProbe(app, new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
            {
                BaseAddress = new Uri($"http://127.0.0.1:{port}"),
                Timeout = TimeSpan.FromSeconds(20),
            });
        }
    }

    public async Task<(int Status, string Location, string Body)> SendAsync(string method, string host, string path)
    {
        using var req = new HttpRequestMessage(new HttpMethod(method), path);
        req.Headers.Host = host;
        if (method == "POST") req.Content = new FormUrlEncodedContent([]);
        using var res = await Client.SendAsync(req);
        return ((int)res.StatusCode, res.Headers.Location?.OriginalString ?? "", await res.Content.ReadAsStringAsync());
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}

public class StoreHostAdminAreaCharacterizationTests(ITestOutputHelper output)
{
    private const string Base = "example.test";

    private static string Show(StoreHostAnswer a) => $"{a.Verdict}|{a.Slug}|{a.Path ?? a.Location}";

    /// <summary>بِالدالَّةِ النَقِيَّة: مَنطِقَةُ المَنَصَّةِ عَلى مُضيفِ مَتجَرٍ لِغَيرِ مَتجَرِه — 301 لِـGET و404 لِلكِتابَة؛ والجَذرُ يَمُرّ.</summary>
    [Theory]
    [InlineData("ashare.example.test", "GET", "/admin", "Redirect|ashare|https://example.test/admin")]
    [InlineData("ashare.example.test", "GET", "/admin/tenants", "Redirect|ashare|https://example.test/admin/tenants")]
    [InlineData("ashare.example.test", "GET", "/admin/tenants/", "Redirect|ashare|https://example.test/admin/tenants/")]
    [InlineData("ashare.example.test", "GET", "/admin/tenants/new", "Redirect|ashare|https://example.test/admin/tenants/new")]
    [InlineData("ashare.example.test", "GET", "/admin/tenants/ejar/categories", "Redirect|ashare|https://example.test/admin/tenants/ejar/categories")]
    [InlineData("ashare.example.test", "GET", "/admin/tenants/ashare-x/categories", "Redirect|ashare|https://example.test/admin/tenants/ashare-x/categories")]
    [InlineData("ashare.example.test", "GET", "/admin/tenants/ashar/categories", "Redirect|ashare|https://example.test/admin/tenants/ashar/categories")]
    [InlineData("ashare.example.test", "GET", "/admin/tenantsx/ashare", "Redirect|ashare|https://example.test/admin/tenantsx/ashare")]
    [InlineData("ashare.example.test", "GET", "/admin/ashare/categories", "Redirect|ashare|https://example.test/admin/ashare/categories")]
    [InlineData("ashare.example.test", "GET", "/admin/agent", "Redirect|ashare|https://example.test/admin/agent")]
    [InlineData("ashare.example.test", "GET", "/_admin/tenants/ashare/categories", "Redirect|ashare|https://example.test/_admin/tenants/ashare/categories")]
    [InlineData("ashare.example.test", "GET", "/studio/apps/ashare/pwa", "Redirect|ashare|https://example.test/studio/apps/ashare/pwa")]
    [InlineData("ashare.example.test", "POST", "/admin/tenants/ejar/categories/save", "NotFound||")]
    [InlineData("ashare.example.test", "POST", "/admin/tenants/create", "NotFound||")]
    [InlineData("ashare.example.test", "POST", "/admin/tenants/ashare-x/categories/save", "NotFound||")]
    [InlineData("ashare.example.test", "POST", "/studio/apps/ashare/pwa/save", "NotFound||")]
    [InlineData("ejar.example.test", "GET", "/admin/tenants/ashare/categories", "Redirect|ejar|https://example.test/admin/tenants/ashare/categories")]
    [InlineData("ejar.example.test", "POST", "/admin/tenants/ashare/categories/save", "NotFound||")]
    [InlineData("example.test", "GET", "/admin/tenants/ashare/categories", "Pass||")]
    [InlineData("example.test", "POST", "/admin/tenants/ashare/categories/save", "Pass||")]
    public void A_platform_area_path_that_is_not_the_host_store_admin_keeps_its_answer(string host, string method, string path, string expected)
    {
        var a = TenantResolverMiddleware.AnswerStoreHost(host, Base, method, path, "", "https");
        output.WriteLine($"{method} {host}{path} ⇒ {Show(a)}");
        Assert.Equal(expected, Show(a));
    }

    /// <summary>بِالوَسيطَينِ الحَقيقِيَّين: مَنطِقَةُ الإشرافِ لِغَيرِ مَتجَرِ المُضيفِ 301/404 عَلى المُضيف، والجَذرُ يَمُرُّ إلى الطَرَفِ بِلا سِياقِ مَتجَر.</summary>
    [Fact]
    public async Task On_the_wire_the_admin_area_of_another_store_leaves_the_store_host_and_the_apex_carries_no_store_context()
    {
        var world = new DocWorld().Put(new Tenant { Id = "ashare", Name = "عَشير" }).Put(new Tenant { Id = "ejar", Name = "إيجار" });
        await using var p = await AdminAreaProbe.StartAsync(Base, world.Store);
        var rows = new (string Method, string Host, string Path, int Status, string Location, string Body)[]
        {
            ("GET",  "ashare.example.test", "/admin/tenants/ejar/categories?from=tenant", 302, "http://example.test/admin/tenants/ejar/categories?from=tenant", ""),
            ("GET",  "ashare.example.test", "/admin/tenants/ashare-x/users", 302, "http://example.test/admin/tenants/ashare-x/users", ""),
            ("GET",  "ashare.example.test", "/admin", 302, "http://example.test/admin", ""),
            ("POST", "ashare.example.test", "/admin/tenants/ejar/categories/save", 404, "", ""),
            ("GET",  "example.test", "/admin/tenants/ashare/categories?from=tenant", 200, "", "/admin/tenants/ashare/categories?from=tenant|tenant="),
            ("POST", "example.test", "/admin/tenants/ashare/categories/save", 200, "", "/admin/tenants/ashare/categories/save|tenant="),
        };
        foreach (var r in rows)
        {
            var (status, location, body) = await p.SendAsync(r.Method, r.Host, r.Path);
            output.WriteLine($"{r.Method} {r.Host}{r.Path} ⇒ {status} {location}{body}");
            Assert.Equal((r.Status, r.Location, r.Body), (status, location, body));
        }
        output.WriteLine($"الصُفوف: {rows.Length}");
    }
}
