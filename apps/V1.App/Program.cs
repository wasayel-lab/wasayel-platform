using ACommerce.Kit.Realtime.Server;
using ACommerce.Platform.Hosting;
using ACommerce.V1.App.Auth;
using ACommerce.V1.App.Components;
using ACommerce.V1.App.Seed;

var builder = WebApplication.CreateBuilder(args);

builder.AddPlatformHost(host => host
    .AddKitAssembly(typeof(ACommerce.Kit.Tenants.Server.TenantHandlers).Assembly)
    .AddKitAssembly(typeof(ACommerce.Kit.Listings.Server.ListingHandlers).Assembly)
    .AddKitAssembly(typeof(ACommerce.Kit.Auth.Server.AuthHandlers).Assembly)
    .AddKitAssembly(typeof(ACommerce.Kit.Notifications.Server.NotificationHandlers).Assembly)
    .AddKitAssembly(typeof(ACommerce.Kit.Chat.Server.ChatHandlers).Assembly)
    .AddKitAssembly(typeof(ACommerce.Kit.Realtime.Server.RealtimeBroadcastHandler).Assembly));

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<AuthSession>();

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    await PlatformSeed.RunAsync(scope.ServiceProvider);
}

app.UsePlatformHost();

// Logout endpoint (POST /{slug}/auth/logout) — يَمسَح الـ cookies ويُعيد توجيه
app.MapPost("/{slug}/auth/logout", (string slug, HttpContext http) =>
{
    http.Response.Cookies.Delete(AuthSession.CookieName(slug), new CookieOptions { Path = $"/{slug}" });
    http.Response.Cookies.Delete(AuthSession.CookieName(slug) + ".name", new CookieOptions { Path = $"/{slug}" });
    return Results.Redirect($"/{slug}");
});

// SignalR hub لِبَثّ realtime — مُسَجَّل بَعد UsePlatformHost لِأَنّ UseRouting يَجب أن يَكون قَد جَرى
app.MapHub<RealtimeHub>("/realtime");

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
