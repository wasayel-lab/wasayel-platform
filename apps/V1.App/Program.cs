using ACommerce.Kit.Auth.Providers.MockNafath;
using ACommerce.Kit.Auth.Providers.MockSms;
using ACommerce.Kit.Auth.Server;
using ACommerce.Kit.Realtime.Server;
using ACommerce.Platform.Hosting;
using ACommerce.Templates.Customer.Marketplace;
using ACommerce.Templates.Customer.Marketplace.Components;
using ACommerce.V1.App.Seed;

var builder = WebApplication.CreateBuilder(args);

builder.AddPlatformHost(host => host
    .AddKitAssembly(typeof(ACommerce.Kit.Tenants.Server.TenantHandlers).Assembly)
    .AddKitAssembly(typeof(ACommerce.Kit.Listings.Server.ListingHandlers).Assembly)
    .AddKitAssembly(typeof(AuthHandlers).Assembly)
    .AddKitAssembly(typeof(ACommerce.Kit.Notifications.Server.NotificationHandlers).Assembly)
    .AddKitAssembly(typeof(ACommerce.Kit.Chat.Server.ChatHandlers).Assembly)
    .AddKitAssembly(typeof(ACommerce.Kit.Favorites.Server.FavoriteHandlers).Assembly)
    .AddKitAssembly(typeof(RealtimeBroadcastHandler).Assembly));

// مُزَوِّدو الـ Auth (mock — استَبدِلهم بـ Twilio/Nafath فعليّ في الإنتاج)
builder.Services.AddMockSmsChannel();
builder.Services.AddMockNafathChannel(opts => { opts.DisplayCode = "00"; opts.AutoApproveSeconds = 5; });

// القالَب — يُسَجِّل AuthSession + HttpContextAccessor
builder.Services.AddCustomerMarketplaceTemplate();

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
    await PlatformSeed.RunAsync(scope.ServiceProvider);

app.UsePlatformHost();

// القالَب — يُسَجِّل form endpoints (auth/login/logout/chat send/favorite/...)
app.MapCustomerMarketplaceTemplate();

app.MapHub<RealtimeHub>("/realtime");

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
