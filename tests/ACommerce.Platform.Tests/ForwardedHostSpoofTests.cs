using System.Net;
using ACommerce.Platform.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
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

    /// <summary>التَهيئَةُ الافتِراضِيَّةُ — <b>لا قائِمَةَ مُضيفاتٍ
    /// مُهَيَّأَة</b>، وهي حالُ الإنتاجِ اليَوم.</summary>
    private static void Closed(ForwardedHeadersOptions opts)
        => new ForwardedHeadersPolicy().ApplyTo(opts);

    /// <summary>وقائِمَةٌ مُسَمّاةٌ — كَما يَقرَؤُها
    /// <c>FromConfiguration</c> مِن <c>ForwardedHeaders:AllowedHosts</c>.</summary>
    private static Action<ForwardedHeadersOptions> Naming(params string[] hosts)
        => opts => new ForwardedHeadersPolicy
        {
            AllowedHosts = ForwardedHeadersPolicy.ParseAllowedHosts(hosts)
        }.ApplyTo(opts);

    /// <summary>التَهيئَةُ الَّتي كانَت مَشحونَةً — تُكتَبُ هُنا
    /// صَراحَةً لِيَبقى المِجَسُّ قادِراً عَلى رُؤيَةِ التَسَرُّبِ بَعدَ
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
        var probe = await ProbeAsync(Closed,
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
        var probe = await ProbeAsync(Closed,
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
        var probe = await ProbeAsync(Closed,
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
        var probe = await ProbeAsync(Closed,
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
        var probe = await ProbeAsync(Closed,
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
        var probe = await ProbeAsync(Closed,
            ("X-Forwarded-Proto", "https"),
            ("X-Forwarded-For", "203.0.113.9"));

        Assert.EndsWith("|203.0.113.9", probe);
    }

    // ═══ ٥) والمُضيفُ يُصَدَّقُ إذا سُمِّي — القُفلُ لَيسَ باباً مَسدوداً ═══

    /// <summary><b>ولِماذا قائِمَةٌ لا نَزعٌ نِهائيّ لِلعَلَم</b>:
    /// المُستَضيفُ اليَومَ (‏HF) لا يُرسِلُ الرَأسَ أَصلاً — مَقيسٌ —
    /// لكِنَّ وَسيطاً يُعيدُ كِتابَةَ <c>Host</c> يَحتاجُه، ونَقلَةُ
    /// النِطاقاتِ الفَرعِيَّةِ (‏ADR-022) تَقرَأُ <c>Request.Host</c>.
    /// فَالقُفلُ يُبقي البابَ مَوجوداً ويَشتَرِطُ أَن يُسَمّى مَن
    /// يَدخُلُ مِنه.</summary>
    [Fact]
    public async Task A_named_host_is_trusted()
    {
        var probe = await ProbeAsync(Naming("shop.example"),
            ("X-Forwarded-Proto", "https"),
            ("X-Forwarded-Host", "shop.example"));

        Assert.StartsWith("https://shop.example", probe);
    }

    [Fact]
    public async Task And_only_it__an_unnamed_host_does_not_pass_the_named_list()
    {
        var probe = await ProbeAsync(Naming("shop.example"),
            ("X-Forwarded-Proto", "https"),
            ("X-Forwarded-Host", Evil));

        Assert.DoesNotContain(Evil, probe);
        Assert.Contains("127.0.0.1", probe);
    }

    /// <summary><b>و«‏<c>*</c>‏» لَيسَت تَسمِيَة</b> — يَقرَؤُها
    /// <c>ForwardedHeadersMiddleware</c> «‏اِقبَل كُلَّ مُضيف‏»، أَي
    /// العَطَبَ نَفسَه مَكتوباً بِيَدِ المُهَيِّئ. فَتُسقَطُ عِندَ
    /// القِراءَةِ ويَبقى البابُ مُغلَقاً.</summary>
    [Fact]
    public async Task A_wildcard_is_not_a_name__it_closes_instead_of_opening_everything()
    {
        var probe = await ProbeAsync(Naming("*"),
            ("X-Forwarded-Proto", "https"),
            ("X-Forwarded-Host", Evil));

        Assert.DoesNotContain(Evil, probe);
    }

    // ═══ ٦) والقِراءَةُ نَفسُها دالَّةٌ نَقِيَّةٌ تُقاسُ بِلا مُضيف ═══

    [Fact]
    public void With_nothing_configured_the_forwarded_host_flag_is_not_set()
    {
        var opts = new ForwardedHeadersOptions();
        new ForwardedHeadersPolicy().ApplyTo(opts);

        Assert.False(opts.ForwardedHeaders.HasFlag(ForwardedHeaders.XForwardedHost));
        Assert.True(opts.ForwardedHeaders.HasFlag(ForwardedHeaders.XForwardedProto));
        Assert.True(opts.ForwardedHeaders.HasFlag(ForwardedHeaders.XForwardedFor));
        Assert.Empty(opts.AllowedHosts);
    }

    [Fact]
    public void With_a_named_list_the_flag_is_set_and_the_names_reach_the_options()
    {
        var opts = new ForwardedHeadersOptions();
        new ForwardedHeadersPolicy
        {
            AllowedHosts = ForwardedHeadersPolicy.ParseAllowedHosts("a.example, b.example")
        }.ApplyTo(opts);

        Assert.True(opts.ForwardedHeaders.HasFlag(ForwardedHeaders.XForwardedHost));
        Assert.Equal(["a.example", "b.example"], opts.AllowedHosts);
    }

    /// <summary>ونَفسُ التَفريغَينِ في الحالَتَين — عُنوانُ حافَّةِ HF
    /// غَيرُ مَعلومٍ ولا ثابِت، وقائِمَةُ وُكَلاءَ بِالتَخمينِ تُسقِطُ
    /// الرُؤوسَ كُلَّها فَتَكسِرُ الكوكي.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("a.example")]
    public void The_known_proxies_stay_empty_either_way(string configured)
    {
        var opts = new ForwardedHeadersOptions();
        opts.KnownProxies.Add(System.Net.IPAddress.Loopback);

        new ForwardedHeadersPolicy
        {
            AllowedHosts = ForwardedHeadersPolicy.ParseAllowedHosts(configured)
        }.ApplyTo(opts);

        Assert.Empty(opts.KnownProxies);
        Assert.Empty(opts.KnownNetworks);
    }

    [Theory]
    [InlineData("  A.Example  ", "a.example")]
    [InlineData("a.example.", "a.example")]
    [InlineData("a.example;b.example", "a.example|b.example")]
    [InlineData("a.example a.example", "a.example")]
    [InlineData("*.example.com", "*.example.com")]
    public void The_named_list_is_normalised(string raw, string expected)
        => Assert.Equal(expected,
            string.Join('|', ForwardedHeadersPolicy.ParseAllowedHosts(raw)));

    [Theory]
    [InlineData("*")]
    [InlineData("[::]")]
    [InlineData("0.0.0.0")]
    [InlineData(null)]
    [InlineData("   ")]
    public void A_wildcard_or_a_blank_names_nothing(string? raw)
        => Assert.Empty(ForwardedHeadersPolicy.ParseAllowedHosts(raw));

    /// <summary><b>والشَكلُ المَصفوفيُّ يُقرَأُ كَما يُقرَأُ النَصّ</b>:
    /// ‏<c>config["…AllowedHosts"]</c> يُرجِعُ <c>null</c> عَلى مَصفوفَةِ
    /// JSON، فَتَبدو التَهيئَةُ غائِبَةً وهي مَكتوبَة.</summary>
    [Fact]
    public void The_configuration_is_read_in_both_shapes()
    {
        var scalar = ConfigurationPath
            .Combine("ForwardedHeaders", "AllowedHosts");

        var fromText = ForwardedHeadersPolicy.FromConfiguration(
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                    { [scalar] = "a.example, b.example" })
                .Build());

        var fromArray = ForwardedHeadersPolicy.FromConfiguration(
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    [$"{scalar}:0"] = "a.example",
                    [$"{scalar}:1"] = "b.example",
                })
                .Build());

        Assert.Equal(["a.example", "b.example"], fromText.AllowedHosts);
        Assert.Equal(["a.example", "b.example"], fromArray.AllowedHosts);
        Assert.True(fromText.TrustsForwardedHost);
    }

    [Fact]
    public void An_empty_configuration_trusts_no_forwarded_host()
    {
        var policy = ForwardedHeadersPolicy.FromConfiguration(
            new ConfigurationBuilder().Build());

        Assert.False(policy.TrustsForwardedHost);
        Assert.Empty(policy.AllowedHosts);
    }
}
