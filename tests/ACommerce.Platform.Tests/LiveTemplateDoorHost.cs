using Marten;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ACommerce.Platform.Tests;

// ═══ بابُ القالِبِ بِعَينِه عَلى Postgres — جِسمُه المُسَجَّلُ لا نُسخَةٌ مِنه ═══════════════════════════════════════
//
// ‏(‏2026-10-30، الموجَة ١٢ F2 · ١ — مُراجَعَةُ 8357c95 (أ)) كانَت صُفوفُ الحَذفِ الحَيَّةُ تُعيدُ كِتابَةَ تَسلسُلِ البابِ في الاختِبار (`EraseLikeTheDoorAsync`)،
// وإعادَةُ البابِ عِندَ `ConcurrencyException` لا يَقيسُها إلّا فاحِصُ شَكلٍ يَبحَثُ عَن نَصِّ الحَلقَة — فَحَلقَةٌ لا تُعيدُ فِعلاً تَبقى خَضراء (القاعِدَة ١٠).
// فَهُنا **المُضيفُ بِتَركيبِه** (`AddPlatformHost` + `AddCustomerMarketplaceTemplate`، تَركيبُ `LiveAccountConcurrencyProofTests.HostApp` نَفسُه) و**نِقاطُ القالِبِ
// كَما يُسَجِّلُها `MapCustomerMarketplaceTemplate`**، ويُنادى `RequestDelegate` البابِ المُسَمّى بِمُرَشِّحاتِه — جِسمُه الَّذي يُشَغَّل، بِلا خادِمٍ يَستَمِعُ ولا
// خِدمَةٍ في الخَلفِيَّة. وما لا يُرَكِّبُه المُضيفانِ هُنا ويُرَكِّبُه `Program.cs` (مُزَوِّدُ الدَفعِ وSignalR) يُعطى بَديلاً لا يَكتُب: مُزَوِّدُ نُسخَةٍ مُسَجِّل.
// و`marten` اختِيارِيٌّ لِمُستَمِعِ جَلسَةٍ يُدخِلُ كاتِباً مُوازِياً في مَوضِعٍ مُسَمّى — السِباقُ تَرتيبٌ حَتمِيٌّ لا حَظّ.

internal sealed class LiveTemplateDoorHost : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly IReadOnlyList<RouteEndpoint> _endpoints;

    public IDocumentStore Store { get; }

    private LiveTemplateDoorHost(WebApplication app)
    {
        _app = app;
        Store = app.Services.GetRequiredService<IDocumentStore>();
        _endpoints = ((IEndpointRouteBuilder)app).DataSources.SelectMany(d => d.Endpoints).OfType<RouteEndpoint>().ToList();
    }

    public static LiveTemplateDoorHost Build(Action<StoreOptions>? marten = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Microsoft.Extensions.Hosting.Environments.Production,
            ContentRootPath = AppContext.BaseDirectory,
        });
        builder.Configuration["ConnectionStrings:Postgres"] = LiveDealConcurrencyProofTests.ConnectionString!.TrimEnd(';') + ";Timeout=60;Command Timeout=120";
        builder.Host.UseDefaultServiceProvider(o => { o.ValidateOnBuild = false; o.ValidateScopes = false; });
        ACommerce.Platform.Hosting.HostingExtensions.AddPlatformHost(builder);
        ACommerce.Templates.Customer.Marketplace.MarketplaceTemplateExtensions.AddCustomerMarketplaceTemplate(builder.Services);
        // ما يُرَكِّبُه `Program.cs` حَولَ المُضيفَينِ ويَطلُبُه تَوقيعُ نُقطَةٍ ما — بِلا أَيِّ قَناةٍ تُرسِل: لا SMS ولا بَريد ولا نَفاذ، والدَفعُ مُزَوِّدُ نُسخَةٍ مُسَجِّل.
        builder.Services.AddSignalR();
        ACommerce.Kit.Culture.CultureExtensions.AddCultureContext(builder.Services);
        ACommerce.Kit.Versions.VersionGateExtensions.AddVersionGate(builder.Services, o => { o.MinimumSupported = "1.0.0"; o.LatestSuggested = "1.0.0"; });
        ACommerce.Kit.Delivery.OtoShippingExtensions.AddOtoShipping(builder.Services, builder.Configuration);
        ACommerce.Kit.Payments.Providers.PayPal.PayPalExtensions.AddPayPalSubscriptions(builder.Services, builder.Configuration);
        ACommerce.Kit.Payments.Providers.Paddle.PaddleExtensions.AddPaddleBilling(builder.Services, builder.Configuration);
        ACommerce.Kit.Files.UnavailableFileStorageExtensions.AddUnavailableFileStorage(builder.Services);
        builder.Services.AddSingleton<ACommerce.Kit.Payments.IPaymentProvider>(new DealMoneyClaimFirstTests.RecordingPaymentProvider());
        if (marten is not null) builder.Services.ConfigureMarten(marten);
        var app = builder.Build();
        ACommerce.Templates.Customer.Marketplace.MarketplaceTemplateExtensions.MapCustomerMarketplaceTemplate(app);
        return new LiveTemplateDoorHost(app);
    }

    /// <summary>بابُ <c>POST</c> بِنَمَطِ مَسارِه كَما سُجِّل — ويَرمي إن لَم يَكُن واحِداً: أَداةٌ لا تَجِدُ البابَ عَمياء.</summary>
    public RouteEndpoint Door(string pattern)
    {
        var found = _endpoints.Where(e => e.RoutePattern.RawText == pattern
            && (e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains("POST") ?? false)).ToList();
        if (found.Count != 1) throw new InvalidOperationException($"بابُ «{pattern}»: {found.Count} نُقطَة — الأَداةُ عَمياء.");
        return found[0];
    }

    /// <summary>طَلَبُ استِمارَةٍ بِرَمزِ جَلسَةٍ في كوكيِّ المَتجَر — يُعيدُ الحالَةَ ووِجهَةَ التَحويل.</summary>
    public async Task<(int Status, string Location)> PostFormAsync(string pattern, IReadOnlyDictionary<string, string> route, string slug, string? token,
        params (string Key, string Value)[] form)
    {
        var door = Door(pattern);
        await using var scope = _app.Services.CreateAsyncScope();
        var ctx = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        var accessor = scope.ServiceProvider.GetService<IHttpContextAccessor>();
        if (accessor is not null) accessor.HttpContext = ctx;
        var path = "/" + string.Join('/', door.RoutePattern.PathSegments.Select(seg => string.Concat(seg.Parts.Select(p => p switch
        {
            Microsoft.AspNetCore.Routing.Patterns.RoutePatternLiteralPart lit => lit.Content,
            Microsoft.AspNetCore.Routing.Patterns.RoutePatternParameterPart par => route[par.Name],
            _ => "",
        }))));
        ctx.Request.Method = "POST";
        ctx.Request.Scheme = "http";
        ctx.Request.Host = new HostString("localhost");
        ctx.Request.Path = path;
        foreach (var (k, v) in route) ctx.Request.RouteValues[k] = v;
        ctx.Request.ContentType = "application/x-www-form-urlencoded";
        ctx.Request.Form = new FormCollection(form.ToDictionary(f => f.Key, f => new Microsoft.Extensions.Primitives.StringValues(f.Value)));
        if (token is not null) ctx.Request.Headers.Cookie = $"{ACommerce.Templates.Customer.Marketplace.AuthSession.CookieName(slug)}={token}";
        ctx.Response.Body = new MemoryStream();
        ctx.SetEndpoint(door);
        await door.RequestDelegate!(ctx);
        return (ctx.Response.StatusCode, ctx.Response.Headers.Location.ToString());
    }

    public async ValueTask DisposeAsync() => await _app.DisposeAsync();
}
