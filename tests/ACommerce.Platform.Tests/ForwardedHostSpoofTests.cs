using System.Net;
using ACommerce.Platform.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace ACommerce.Platform.Tests;

// ═══ رَأسٌ يَكتُبُه الزائِرُ لا يُصَدَّق ═══════════════════════════════
//
// **العَطَبُ مَقيسٌ عَلى الإنتاجِ الحَيِّ (‏2026-08-30)**، بِـ`curl`
// واحِدٍ عَلى `acommerceecommerce-acommerce-ecommerce.hf.space/theme-demo`
// وبِلا أَيِّ نِطاقٍ فَرعِيّ:
//
//   | الطَلَب                      | `<link rel="canonical">`                  |
//   |------------------------------|-------------------------------------------|
//   | بِلا رَأس                    | `https://acommerce…hf.space/theme-demo`   |
//   | `X-Forwarded-Host: evil.example` | **`https://evil.example/theme-demo`**  |
//
// ومَعَه `og:url` و`og:image` و`ld+json` — كُلُّها تُشتَقُّ مِن
// `Request.Host` في `App.razor` و`SeoHandlers`. أَي أَنَّ **أَيَّ زائِرٍ
// يَجعَلُ الصَفحَةَ تُعلِنُ عَن نَفسِها بِنِطاقٍ يَملِكُه هُو** — فَتُفهرَسُ
// وتُشارَكُ وتُجلَبُ صُوَرُها مِن عِندِه.
//
// **والسَبَبُ**: `ForwardedHeaders.XForwardedHost` مُفَعَّلٌ و
// `KnownProxies`/`KnownNetworks` مُفَرَّغانِ — فَالوَسيطُ يُصَدِّقُ
// **كُلَّ** نِدٍّ، والنِدُّ خَلفَ HF هُوَ الزائِرُ نَفسُه.
//
// ─── وتَصحيحٌ كَتَبَه المِجَسُّ لا التَخمين ────────────────────────────
//
// كُتِبَ هُنا أَوَّلاً أَنَّ `X-Forwarded-Host` وَحدَه **لا يُطَبَّق**
// ما دامَ `XForwardedProto` مُفَعَّلاً وغائِباً — أَي أَنَّ الاستِغلالَ
// يَحتاجُ الوَسيطَ شَريكاً يُرسِلُ `Proto`. **وأَحمَرَّ القِياسُ**:
// ‏`X-Forwarded-Host: evil.example` وَحدَه أَعطى
// `http://evil.example`. فَالثَمَنُ أَرخَصُ مِمّا ظُنّ: **رَأسٌ واحِدٌ
// مِن زائِرٍ واحِد، بِلا تَعاوُنِ أَيِّ وَسيط**. (القاعِدَة ١٠.)
//
// ─── والمِجَسُّ يُقاسُ قَبلَ أَن يُوثَقَ بِه (القاعِدَة ١٠) ────────────
//
// ‏`The_probe_is_not_blind…` يُشَغِّلُ **نَفسَ الأُنبوبِ** بِالتَهيئَةِ
// المَشحونَةِ اليَومَ ويَتَوَقَّعُ التَسَرُّبَ **حاصِلاً**. فَإن صارَ
// أَخضَرَ بِلا تَسَرُّبٍ يَوماً، فَالمِجَسُّ هُوَ الَّذي عَمِيَ لا
// المَنَصَّةُ الَّتي شُفِيَت.

// ─── مُضيفٌ مُصَغَّرٌ حَيّ — نَفسُ نَمَط `BuildIdentityEndpointTests` ───
// ولا `WebApplicationFactory`: غِيابُها قَرارٌ مُوَثَّقٌ بِسَبَبِه في
// `PayPalEndpointBehaviourTests` — تُقلِعُ `Program` كامِلاً ومَعَه
// Marten وWolverine، أَي قاعِدَةَ بَياناتٍ في بَوّابَةٍ يَجِبُ أَن
// تَخضَرَّ بِلا شَبَكَة.
file sealed class ForwardedProbeHost : IAsyncDisposable
{
    private readonly WebApplication _app;

    public HttpClient Client { get; }

    private ForwardedProbeHost(WebApplication app, HttpClient client)
    {
        _app = app; Client = client;
    }

    private static int FreePort()
    {
        using var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    /// <summary><b>حَلقَةُ الإعادَةِ لِنَفسِ عِلَّةِ `HealthHost`</b>:
    /// بَينَ إغلاقِ المُستَمِعِ وفَتحِ المَنفَذِ فُرجَةٌ يَختَطِفُ فيها
    /// صَنفٌ آخَرُ (‏xUnit يُوازي الأَصناف) نَفسَ الرَقَم.</summary>
    public static async Task<ForwardedProbeHost> StartAsync(
        Action<ForwardedHeadersOptions> configure)
    {
        for (var attempt = 1; ; attempt++)
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.Logging.ClearProviders();
            builder.Services.Configure(configure);

            var port = FreePort();
            var app = builder.Build();
            app.Urls.Add($"http://127.0.0.1:{port}");

            app.UseForwardedHeaders();

            // نَفسُ ما تَقرَؤُه `App.razor` و`SeoHandlers` حَرفاً:
            // `Scheme` و`Host`. ومَعَهُما عُنوانُ النِدِّ — الوَجهُ الثالِث.
            app.MapGet("/probe", (HttpContext ctx) => Results.Text(
                $"{ctx.Request.Scheme}://{ctx.Request.Host}|{ctx.Connection.RemoteIpAddress}"));

            try
            {
                await app.StartAsync();
            }
            catch (IOException) when (attempt < 5)
            {
                await app.DisposeAsync();
                continue;
            }

            var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
            {
                BaseAddress = new Uri($"http://127.0.0.1:{port}"),
                Timeout = TimeSpan.FromSeconds(20),
            };

            return new ForwardedProbeHost(app, client);
        }
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}

public class ForwardedHostSpoofTests
{
    private const string Evil = "evil.example";

    /// <summary>التَهيئَةُ المَشحونَةُ اليَوم — تُكتَبُ هُنا صَراحَةً
    /// لِيَبقى المِجَسُّ قادِراً عَلى رُؤيَةِ التَسَرُّبِ بَعدَ
    /// العِلاج.</summary>
    private static void ShippedConfiguration(ForwardedHeadersOptions opts)
    {
        opts.ForwardedHeaders =
            ForwardedHeaders.XForwardedFor |
            ForwardedHeaders.XForwardedProto |
            ForwardedHeaders.XForwardedHost;
        opts.KnownNetworks.Clear();
        opts.KnownProxies.Clear();
    }

    private static async Task<string> ProbeAsync(
        Action<ForwardedHeadersOptions> configure,
        params (string Name, string Value)[] headers)
    {
        await using var host = await ForwardedProbeHost.StartAsync(configure);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/probe");
        foreach (var (name, value) in headers)
            request.Headers.TryAddWithoutValidation(name, value);

        using var response = await host.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }

    // ═══ ١) القُفل — الزائِرُ لا يَختارُ المُضيفَ الَّذي تُعلِنُه الصَفحَة ═══

    [Fact]
    public async Task A_visitor_cannot_choose_the_host_the_page_advertises()
    {
        var probe = await ProbeAsync(ForwardedHeadersPolicy.Apply,
            ("X-Forwarded-Proto", "https"),
            ("X-Forwarded-Host", Evil));

        Assert.DoesNotContain(Evil, probe);
        Assert.Contains("127.0.0.1", probe);
    }

    /// <summary>وقائِمَةٌ بِقيمَتَينِ لا تَعبُر أَيضاً — الوَسيطُ
    /// يَأخُذُ الأَخيرَةَ، فَلَو مَرَّت لَمَرَّ الحَقنُ مُضاعَفاً.</summary>
    [Fact]
    public async Task A_list_of_forwarded_hosts_does_not_pass_either()
    {
        var probe = await ProbeAsync(ForwardedHeadersPolicy.Apply,
            ("X-Forwarded-Proto", "https"),
            ("X-Forwarded-Host", "aaa.example, bbb.example"));

        Assert.DoesNotContain("aaa.example", probe);
        Assert.DoesNotContain("bbb.example", probe);
        Assert.Contains("127.0.0.1", probe);
    }

    /// <summary>ورَأسانِ مُنفَصِلانِ صورَةٌ أُخرى لِنَفسِ القائِمَة —
    /// قيسَ عَلى الحَيِّ أَنَّ الأَخيرَ يَفوز.</summary>
    [Fact]
    public async Task Two_separate_forwarded_host_headers_do_not_pass_either()
    {
        var probe = await ProbeAsync(ForwardedHeadersPolicy.Apply,
            ("X-Forwarded-Proto", "https"),
            ("X-Forwarded-Host", "aaa.example"),
            ("X-Forwarded-Host", Evil));

        Assert.DoesNotContain(Evil, probe);
        Assert.DoesNotContain("aaa.example", probe);
    }

    // ═══ ٢) وما يَحتاجُه المُستَضيفُ فِعلاً يَبقى — وإلّا كُسِرَت الرَوابِطُ كُلُّها ═══

    /// <summary><b>هذا هُوَ سَبَبُ وُجودِ `UseForwardedHeaders` أَصلاً</b>:
    /// ‏HF يُنهي TLS عِندَ حافَّتِه ويُكَلِّمُ الحاوِيَةَ بِـHTTP، فَبِلا
    /// <c>XForwardedProto</c> يَحسِبُ <c>AuthSession</c> الاتِّصالَ HTTP
    /// فَيَسقُطُ كوكي <c>Secure</c>. نَزعُ العَلَمِ هُنا يَكسِرُ الدُخولَ
    /// كُلَّه — فَالحارِسُ يُثَبِّتُ بَقاءَه.</summary>
    [Fact]
    public async Task The_proxy_still_writes_the_scheme()
    {
        var probe = await ProbeAsync(ForwardedHeadersPolicy.Apply,
            ("X-Forwarded-Proto", "https"));

        Assert.StartsWith("https://", probe);
    }

    // ═══ ٣) المِجَسُّ يُقاسُ بِحَقنِ العَيبِ — لا يُوثَقُ بِه قَبلَ ذلك ═══

    [Fact]
    public async Task The_probe_is_not_blind__the_shipped_configuration_still_leaks()
    {
        var probe = await ProbeAsync(ShippedConfiguration,
            ("X-Forwarded-Proto", "https"),
            ("X-Forwarded-Host", Evil));

        Assert.Contains(Evil, probe);
    }

    /// <summary><b>ورَأسُ <c>Host</c> وَحدَه يَكفي</b> — بِلا
    /// <c>X-Forwarded-Proto</c> وبِلا تَعاوُنِ أَيِّ وَسيط. ظُنَّ
    /// خِلافُ ذلك عِندَ كِتابَةِ المِلَفّ، فَأَحمَرَّ القِياسُ الظَنَّ:
    /// المُخرَجُ كانَ <c>http://evil.example</c>. مُثَبَّتٌ هُنا لِأَنَّ
    /// <b>ثَمَنَ الهُجومِ جُزءٌ مِن تَوصيفِه</b>.</summary>
    [Fact]
    public async Task The_forwarded_host_alone_is_enough__no_proxy_cooperation_needed()
    {
        var probe = await ProbeAsync(ShippedConfiguration,
            ("X-Forwarded-Host", Evil));

        Assert.Contains(Evil, probe);
    }

    /// <summary>ومُقابِلُه بَعدَ القُفل: رَأسُ <c>Host</c> وَحدَه
    /// لا يَعبُر.</summary>
    [Fact]
    public async Task The_forwarded_host_alone_does_not_pass_the_lock()
    {
        var probe = await ProbeAsync(ForwardedHeadersPolicy.Apply,
            ("X-Forwarded-Host", Evil));

        Assert.DoesNotContain(Evil, probe);
        Assert.Contains("127.0.0.1", probe);
    }

    // ═══ ٤) الوَجهُ الثالِث — مَقيسٌ ومُصَرَّحٌ بِه، لا مَسكوتٌ عَنه ═══

    /// <summary><b>‏<c>X-Forwarded-For</c> يُؤخَذُ مِن أَيِّ عَميلٍ
    /// كَذلك</b> — نَفسُ العِلَّةِ (<c>KnownProxies</c> مُفَرَّغ)،
    /// و<c>Connection.RemoteIpAddress</c> يَبلُغُ سِجِلَّ التَدقيقِ
    /// وإشاراتِ الاحتِيالِ عِندَ PayPal وPaddle.
    ///
    /// <para><b>ولِماذا لَم يُغَيَّر مَعَ المُضيف</b>: أَثَرُه على
    /// الحَيِّ <b>غَيرُ مَقيسٍ مِن الخارِج</b> — لا نُقطَةَ تَعكِسُ
    /// عُنوانَ النِدّ. وقياسُ الشَقيقِ (<c>X-Forwarded-Proto</c>)
    /// أَعطى أَنَّ قيمَةَ HF تَغلِبُ قيمَةَ العَميل، أَي أَنَّ الوَسيطَ
    /// <b>يُلحِقُ</b> رُؤوسَه؛ فَإن أَلحَقَ عُنوانَ النِدِّ كَذلك
    /// فَالانتِحالُ لا يَقَعُ في الإنتاج، وإن مَرَّرَ رَأسَ العَميلِ
    /// كَما هُوَ فَهُوَ واقِع. <b>ونَزعُ العَلَمِ بِلا قِياسٍ يُبَدِّلُ
    /// عُنوانَ المُشتَري بِعُنوانِ حافَّةِ HF في كُلِّ عَمَلِيَّةِ
    /// دَفع</b> — انحِدارٌ مُؤَكَّدٌ ثَمَناً لِخَطَرٍ غَيرِ مَقيس.
    /// فَالحالُ مُثَبَّتَةٌ هُنا كَما هي، والقَرارُ مُؤَجَّلٌ إلى
    /// قِياسٍ (‏ADR-023 § ما لَم يُحسَم).</para></summary>
    [Fact]
    public async Task The_forwarded_for_is_still_taken_from_any_client__measured_not_assumed()
    {
        var probe = await ProbeAsync(ForwardedHeadersPolicy.Apply,
            ("X-Forwarded-Proto", "https"),
            ("X-Forwarded-For", "203.0.113.9"));

        Assert.EndsWith("|203.0.113.9", probe);
    }
}
