using ACommerce.Platform.Hosting;
using ACommerce.V1.App.Components;
using ACommerce.V1.App.Seed;

var builder = WebApplication.CreateBuilder(args);

builder.AddPlatformHost(host => host
    .AddKitAssembly(typeof(ACommerce.Kit.Tenants.Server.TenantHandlers).Assembly)
    .AddKitAssembly(typeof(ACommerce.Kit.Listings.Server.ListingHandlers).Assembly));

var app = builder.Build();

// بَذر بَيانات أَوّليّة (Ashare + Ejar) لو الجَداوِل فارِغَة.
await using (var scope = app.Services.CreateAsyncScope())
{
    await PlatformSeed.RunAsync(scope.ServiceProvider);
}

app.UsePlatformHost();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
