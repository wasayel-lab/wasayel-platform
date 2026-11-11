using System.Net;
using ACommerce.Platform.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

/// <summary>
/// <para><b>الجَذرُ يُسَمّى بِاسمِه: <c>ForwardedHeaders__AllowedHosts=R,*.R</c></b> (‏2026-11-11، W13-4·C2z·1 — مُراجَعَةُ C2c · C2c-P3-01 وC2c-P-02،
/// ومُراجَعَةُ C2a · C2a-P2-05 وC2a-P3-04؛ DEPLOY §٢·ي الخُطوَةُ ٦ بِنُسخَتَيها، ADR-023 §٥، ADR-050 §٧).</para>
///
/// <para><b>العِلَّةُ المَقيسَة</b>: الدَليلُ قالَ خَلفَ عامِلٍ يُعيدُ كِتابَةَ <c>Host</c> «<c>*.R</c> أَيضاً»، والـwildcard في مُطابَقَةِ ASP.NET لِلرُؤوسِ المُمَرَّرَةِ
/// <b>مُضيفاتٌ فَرعِيَّةٌ وَحدَها</b> — فَـ<c>X-Forwarded-Host: R</c> يُسقَطُ ويَبقى <c>Request.Host</c> لِلجَذرِ اسمَ الأَصلِ الداخِلِيّ. ومُنذُ فَحصِ <c>Origin</c>
/// (‏<c>2c4c6fa</c>) يُقارَنُ أَصلُ المُتَصَفِّحِ بِه: كُلُّ كِتابَةٍ عَلى الجَذرِ 403 (دُخولُ الاستوديو والإشرافِ واستِماراتُ وَضعِ المَسار — مَقيسٌ عِندَ المُراجِع)،
/// والـcanonical وخَريطَةُ المَوقِعِ وعَناوينُ عَودَةِ PayPal مِن الاسمِ الداخِلِيّ. <b>والحَدُّ يُقاسُ عَلى الوَسيطِ الحَقيقيّ</b> (القاعِدَة ٢): مُضيفٌ مُصَغَّرٌ عَلى
/// الحَلقَةِ بِـ<c>UseForwardedHeaders</c> وسِياسَةِ المِنَصَّةِ نَفسِها.</para>
/// </summary>
public class ForwardedApexNamedTests(ITestOutputHelper output)
{
    private const string Internal = "space-owner.hf.example";

    private static async Task<string> ProbeAsync(string allowed, string forwardedHost)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.Services.Configure<ForwardedHeadersOptions>(opts => new ForwardedHeadersPolicy
        {
            AllowedHosts = ForwardedHeadersPolicy.ParseAllowedHosts(allowed),
        }.ApplyTo(opts));
        int port;
        using (var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0))
        {
            listener.Start();
            port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
        }
        await using var app = builder.Build();
        app.Urls.Add($"http://127.0.0.1:{port}");
        app.UseForwardedHeaders();
        app.MapGet("/probe", (HttpContext ctx) => Results.Text($"{ctx.Request.Scheme}://{ctx.Request.Host}"));
        await app.StartAsync();
        var address = $"http://127.0.0.1:{port}";
        using var client = new HttpClient { BaseAddress = new Uri(address), Timeout = TimeSpan.FromSeconds(20) };
        using var request = new HttpRequestMessage(HttpMethod.Get, "/probe");
        request.Headers.Host = Internal;
        request.Headers.TryAddWithoutValidation("X-Forwarded-Proto", "https");
        request.Headers.TryAddWithoutValidation("X-Forwarded-Host", forwardedHost);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        await app.StopAsync();
        return body;
    }

    [Theory]
    // الـwildcard وَحدَه: المُضيفاتُ تُقبَل، والجَذرُ لا — يَبقى الاسمُ الداخِلِيّ
    [InlineData("*.example.test", "example.test", "https://" + Internal)]
    [InlineData("*.example.test", "ashare.example.test", "https://ashare.example.test")]
    [InlineData("*.example.test", "ashare-vendor.example.test", "https://ashare-vendor.example.test")]
    // والجَذرُ بِاسمِه مَعَه (الفاصِلَةُ أَو الفاصِلَةُ المَنقوطَة): الثَلاثَةُ تُقبَل
    [InlineData("example.test,*.example.test", "example.test", "https://example.test")]
    [InlineData("example.test;*.example.test", "example.test", "https://example.test")]
    [InlineData("example.test,*.example.test", "ashare.example.test", "https://ashare.example.test")]
    // وما خارِجَ النِطاقِ لا يُقبَلُ بِأَيِّهِما
    [InlineData("example.test,*.example.test", "evil.example", "https://" + Internal)]
    [InlineData("example.test,*.example.test", "example.test.evil.example", "https://" + Internal)]
    public async Task The_apex_must_be_named_beside_its_wildcard(string allowed, string forwardedHost, string expected)
    {
        var got = await ProbeAsync(allowed, forwardedHost);
        output.WriteLine($"AllowedHosts={allowed} · X-Forwarded-Host={forwardedHost} ⇒ {got}");
        Assert.Equal(expected, got);
    }

    /// <summary>والدَليلُ يَقولُ ما يُقاسُ هُنا: خُطوَتا ٦ بِتَصحيحٍ مُؤَرَّخٍ يُسَمّي <c>R,*.R</c>، ولا يُكتَبُ <c>ForwardedHeaders__AllowedHosts=*.R</c> وَحدَه بَعدَهُما.</summary>
    [Fact]
    public void The_deploy_guide_names_the_apex_after_every_wildcard_only_instruction()
    {
        var doc = File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, "docs/DEPLOY.md"));
        var bare = AllIndexes(doc, "`ForwardedHeaders__AllowedHosts=*.R`").ToList();
        var fixedLines = AllIndexes(doc, "`ForwardedHeaders__AllowedHosts=R,*.R`").ToList();
        output.WriteLine($"«*.R» وَحدَه: {bare.Count} مَوضِعاً · «R,*.R»: {fixedLines.Count}");
        Assert.NotEmpty(bare);                       // المَوضِعانِ القَديمانِ باقِيانِ بِحَرفِهِما (يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه)
        Assert.All(bare, i => Assert.Contains(fixedLines, f => f > i && f - i < 1500));
    }

    private static IEnumerable<int> AllIndexes(string s, string needle)
    {
        for (var i = s.IndexOf(needle, StringComparison.Ordinal); i >= 0; i = s.IndexOf(needle, i + 1, StringComparison.Ordinal)) yield return i;
    }
}
