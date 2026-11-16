using ACommerce.Platform.Hosting;
using ACommerce.Platform.Shared.Metering;
using ACommerce.Templates.Customer.Marketplace;
using ACommerce.Templates.Customer.Marketplace.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ عَميلُ خَلفِيّاتِ النَماذِجِ بِلا مُهلَةٍ مِن عِندِه — يُقاسُ عَلى العَميلِ المَحلولِ لا عَلى مَوضِعِ سَطرِه (‏2026-11-13، مُراجَعَةُ المَرحَلَةِ ج الأولى · M6C-RULES-R1-1 — ADR-051 §١٩) ═══
//
// **لِماذا هذا المِلَفّ** (القاعِدَة ٣): صَفُّ `AnalysisCallBoundTests.Every_backend_client_leaves_the_bound_to_the_request_and_each_call_starts_it` (تَوصيفُ ea19008) ثَبَّتَ
// «بِلا مُهلَةِ عَميل» بِنَصِّ مَوضِعِه — «Timeout =» ثَلاثاً في `AgentBackends.cs` — فَلَمّا نَقَلَ البَندُ ١٥ (cc79500) العَميلَ إلى تَسجيلِه الواحِدِ في القالِب
// (`AddProviderHttp<IAgentBackend>(…, client.Timeout = InfiniteTimeSpan)`) عُدِّلَ الصَفُّ في مَوضِعِه (‏3 ← 0 وتَأكيداتُ التَسجيل) — وصارَ السُلوكُ المَنقولُ لا يُثبِتُه إلّا
// الصَفُّ المُعَدَّل. وهذا يَقيسُ **السُلوكَ نَفسَه بِما يَبقى بَعدَ أَيِّ نَقلٍ**: العَميلُ الَّذي يُعطاه كُلُّ خَلفِيَّةٍ مِن المُضيفِ المُرَكَّب (كَما يُرَكِّبُه `Program.cs`) بِلا
// مُهلَةٍ مِن عِندِه، أَيّاً كانَ مَوضِعُ ضَبطِه — فَالمُهلَةُ مِن الطَلَبِ وَحدَه (`AgentCallBound`). والضابِطُ السالِبُ عَميلٌ مُسَمّىً بِلا ضَبطٍ يَحمِلُ مُهلَةَ الإطارِ
// الافتِراضِيَّة، فَالقِياسُ يُمَيِّزُ ولا يَعمى. وهُوَ «المِلَفُّ الحاكِمُ» لِذلكَ الصَفِّ في سِجِلِّ ADR-051 §١٩.

public class AgentBackendClientBoundTests(ITestOutputHelper output)
{
    private static WebApplication Compose()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Production,
            ContentRootPath = AppContext.BaseDirectory,
        });
        builder.Configuration["ConnectionStrings:Postgres"] = "Host=localhost;Database=compose_only;Username=u;Password=p";
        builder.Host.UseDefaultServiceProvider(o => { o.ValidateOnBuild = false; o.ValidateScopes = false; });
        builder.AddPlatformHost();
        builder.Services.AddCustomerMarketplaceTemplate();
        return builder.Build();
    }

    [Fact]
    public void The_client_every_model_backend_receives_has_no_timeout_of_its_own()
    {
        using var app = Compose();
        var factory = app.Services.GetRequiredService<IHttpClientFactory>();
        using var backends = factory.CreateClient(nameof(IAgentBackend));
        using var unconfigured = factory.CreateClient("w13-unconfigured-control");
        var recording = app.Services.GetRequiredService<ProviderHttp<IAgentBackend>>().IsRecording;
        output.WriteLine($"عَميلُ الخَلفِيّات «{nameof(IAgentBackend)}» ⇐ مُهلَة {backends.Timeout} · يُسَجِّل {recording} · الضابِطُ السالِبُ بِلا ضَبط ⇐ {unconfigured.Timeout}");
        Assert.Equal(Timeout.InfiniteTimeSpan, backends.Timeout);
        Assert.NotEqual(Timeout.InfiniteTimeSpan, unconfigured.Timeout);
        Assert.True(recording, "عَميلُ الخَلفِيّاتِ في المُضيفِ المُرَكَّبِ لا يَمُرُّ بِمُستَدعٍ مُسَجِّل.");
    }
}
