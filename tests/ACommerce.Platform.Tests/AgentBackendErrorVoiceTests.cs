using System.Net;
using ACommerce.Templates.Customer.Marketplace.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Xunit;

namespace ACommerce.Platform.Tests;

// ═══ صَوتُ خَطَأِ الوَكيل — يَقولُ الخادِمَ الَّذي رَدَّ، لا الصَنفَ ═════
//
// **العِلَّةُ المَقيسَة (‏2026-08-31)**: رِحلَةُ عَميلٍ حَيَّةٍ سَقَطَت
// عِندَ الخُطوَةِ الثالِثَةِ بِـ:
//
//     OpenAI 401: {"message":"Invalid API Key","code":"invalid_api_key"}
//
// وذلكَ الجِسمُ **لَيسَ جِسمَ OpenAI** (‏تِلكَ تُغَلِّفُ بِـ`error`
// وتُعيدُ صَدى المِفتاحِ مُقَنَّعاً) — رِسالَتُه ورَمزُه **يُطابِقانِ
// Groq حَرفاً**. فَبادِئَةُ «OpenAI» كانَت **اسمَ الصَنفِ**
// (`OpenAIBackend`) لا اسمَ الخادِم؛ والصَنفُ نَفسُه يَخدِمُ Groq
// وCerebras وOpenRouter وOllama وأَيَّ مُتَوافِق. فَشُخِّصَ العَطَبُ
// في الجِهَةِ الخَطَأ ساعاتٍ، **وكانَ الجَوابُ في الرِسالَةِ لَو
// نَطَقَت بِالعُنوان**.
//
// ‏`82200f1f` أَضافَ `IAgentBackend.Endpoint` وسَطرَ الإقلاع — وذاكَ
// يُقرَأُ في **سِجِلِّ الحاوِيَة**، ولا يَبلُغُه مَن يَرى الشاشَة.
// وهذا المِلَفُّ يُلزِمُ الطَرَفَ الثاني: **الرِسالَةُ الَّتي يَراها
// صاحِبُ الدِراسَةِ تَحمِلُ المُزَوِّدَ والعُنوانَ الفِعلِيَّين**.
//
// **ولِماذا خادِمٌ حَقيقِيٌّ لا فَحصُ نَصِّ المَصدَر**: النَصُّ يَمُرُّ
// وإن قُلِبَ الشَرط (سابِقَةُ `PayPalEndpointBehaviourTests`). هُنا
// يُشَغَّلُ `CallAsync` نَحوَ خادِمٍ يَرُدُّ ‏401 بِجِسمِ العِلَّةِ
// نَفسِه، ويُقاسُ ما رَجَعَ — **وصِفرُ نِداءٍ نَحوَ الشَبَكَة**
// (‏العُنوانُ حَلقَةٌ مَحَلِّيَّة).

public class AgentBackendErrorVoiceTests
{
    /// <summary>جِسمُ ‏401 كَما رَدَّهُ الخادِمُ الحَيُّ حَرفاً.</summary>
    private const string GroqUnauthorizedBody =
        "{\"message\":\"Invalid API Key\",\"code\":\"invalid_api_key\"}";

    private static AgentRequest AnyRequest() => new(
        SystemPrompt: "s",
        Messages: new[] { new AgentMessage("user", "hi", null, null) },
        Tools: Array.Empty<AgentToolDef>(),
        Model: "any-model",
        MaxTokens: 16);

    // ═══ 1) الرِسالَةُ تَقولُ الخادِمَ ═══════════════════════════════
    [Fact]
    public async Task Unauthorized_NamesTheServerThatAnswered_NotTheClassThatAsked()
    {
        await using var server = await FakeLlmServer.StartAsync(
            HttpStatusCode.Unauthorized, GroqUnauthorizedBody);

        var backend = new OpenAIBackend(new AgentProfile(
            Name:          AgentNames.Analysis,
            Provider:      "openai",
            BaseUrl:       server.BaseUrl,
            ApiKey:        "irrelevant-for-a-401-fixture",
            Model:         "llama-3.3-70b-versatile",
            ProviderLabel: "groq"));

        var resp = await backend.CallAsync(AnyRequest(), CancellationToken.None);

        Assert.NotNull(resp.Error);

        // المُزَوِّدُ المَحلولُ — لا اسمُ الصَنف.
        Assert.Contains("groq", resp.Error!, StringComparison.Ordinal);
        Assert.DoesNotContain("OpenAI", resp.Error!, StringComparison.Ordinal);

        // والعُنوانُ الَّذي نودِيَ فِعلاً — وهُوَ نِصفُ التَشخيصِ الغائِب.
        Assert.Contains(server.Authority, resp.Error!, StringComparison.Ordinal);

        // ولا يَضيعُ ما كانَ يُقال: الرَمزُ وجِسمُ الخادِمِ يَبقَيان.
        Assert.Contains("401", resp.Error!, StringComparison.Ordinal);
        Assert.Contains("invalid_api_key", resp.Error!, StringComparison.Ordinal);

        // ولا يُطبَعُ المِفتاحُ ولا جُزءٌ مِنه.
        Assert.DoesNotContain("irrelevant-for-a-401-fixture", resp.Error!, StringComparison.Ordinal);
    }

    // ═══ 2) والرَدُّ المُشَوَّهُ يَقولُه أَيضاً ═══════════════════════
    [Fact]
    public async Task EmptyChoices_NamesTheServerToo()
    {
        await using var server = await FakeLlmServer.StartAsync(
            HttpStatusCode.OK, "{\"choices\":[]}");

        var backend = new OpenAIBackend(new AgentProfile(
            AgentNames.Analysis, "openai", server.BaseUrl, "k", "m", "cerebras"));

        var resp = await backend.CallAsync(AnyRequest(), CancellationToken.None);

        Assert.NotNull(resp.Error);
        Assert.Contains("cerebras", resp.Error!, StringComparison.Ordinal);
        Assert.Contains(server.Authority, resp.Error!, StringComparison.Ordinal);
        Assert.DoesNotContain("OpenAI", resp.Error!, StringComparison.Ordinal);
    }

    // ═══ 3) والفَخُّ المُوَثَّقُ يُنطَقُ: عُنوانٌ غائِبٌ ⇒ OpenAI ═══════
    // `docs/AGENT-KEYS.md` §٢: مِفتاحُ Groq بِلا `Agent__BaseUrl`
    // يُرسَلُ إلى خَوادِمِ OpenAI فَتَرُدُّ ‏401. هُنا يُقاسُ الطَرَفانِ
    // بِلا شَبَكَة: التَسميَةُ والعُنوانُ المُعلَنان.
    [Fact]
    public void MissingBaseUrl_FallsBackToOpenAiAndSaysSo()
    {
        var backend = new OpenAIBackend(new AgentProfile(
            AgentNames.Analysis, "openai", null, "gsk-shaped-but-openai-bound", null, null));

        Assert.Equal("openai", backend.ProviderName);
        Assert.Equal("https://api.openai.com/", backend.Endpoint);
    }

    [Fact]
    public void ExplicitLabel_WinsOverInference()
    {
        var backend = new OpenAIBackend(new AgentProfile(
            AgentNames.Analysis, "openai", "https://api.example.test/v1", "k", null, "groq"));

        Assert.Equal("groq", backend.ProviderName);
    }
}

// ─── خادِمُ LLM وَهمِيّ — رَدٌّ واحِدٌ ثابِتٌ لِأَيِّ مَسار ────────────
// نَفسُ نَمَطِ `BuildIdentityEndpointTests.HealthHost` حَرفاً (القاعِدَة ٨:
// لا مُضيفَ رابِع): مِنفَذٌ حُرّ، حَلقَةُ إعادَةٍ عِندَ سِباقِ المِنفَذ،
// وتَنظيفٌ بِـ`IAsyncDisposable`.
internal sealed class FakeLlmServer : IAsyncDisposable
{
    private readonly WebApplication _app;

    public string BaseUrl { get; }
    public string Authority { get; }

    private FakeLlmServer(WebApplication app, int port)
    {
        _app = app;
        Authority = $"127.0.0.1:{port}";
        BaseUrl = $"http://{Authority}/";
    }

    private static int FreePort()
    {
        using var l = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        l.Start();
        return ((IPEndPoint)l.LocalEndpoint).Port;
    }

    public static async Task<FakeLlmServer> StartAsync(HttpStatusCode status, string body)
    {
        for (var attempt = 1; ; attempt++)
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.Logging.ClearProviders();

            var port = FreePort();
            var app = builder.Build();
            app.Urls.Add($"http://127.0.0.1:{port}");
            app.Run(async ctx =>
            {
                ctx.Response.StatusCode = (int)status;
                ctx.Response.ContentType = "application/json";
                await ctx.Response.WriteAsync(body);
            });

            try { await app.StartAsync(); }
            catch (IOException) when (attempt < 5)
            {
                await app.DisposeAsync();
                continue;
            }

            return new FakeLlmServer(app, port);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}
