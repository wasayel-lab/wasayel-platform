using ACommerce.Kit.Auth;
using ACommerce.Kit.Auth.Providers.MockNafath;
using ACommerce.Kit.Auth.Providers.MockSms;
using ACommerce.Kit.Auth.Server;
using ACommerce.Kit.Realtime.Server;
using ACommerce.Platform.Hosting;
using ACommerce.Platform.Shared;
using ACommerce.V1.App.Auth;
using ACommerce.V1.App.Components;
using ACommerce.V1.App.Seed;
using Marten;

var builder = WebApplication.CreateBuilder(args);

builder.AddPlatformHost(host => host
    .AddKitAssembly(typeof(ACommerce.Kit.Tenants.Server.TenantHandlers).Assembly)
    .AddKitAssembly(typeof(ACommerce.Kit.Listings.Server.ListingHandlers).Assembly)
    .AddKitAssembly(typeof(AuthHandlers).Assembly)
    .AddKitAssembly(typeof(ACommerce.Kit.Notifications.Server.NotificationHandlers).Assembly)
    .AddKitAssembly(typeof(ACommerce.Kit.Chat.Server.ChatHandlers).Assembly)
    .AddKitAssembly(typeof(RealtimeBroadcastHandler).Assembly));

// مُزَوِّدو الـ Auth — مَفصولون. التَطبيق يَختار التَنفيذ:
//   مَكتَبَة Auth.Server تَستَخدِم IOtpChannel/INafathChannel كَ contracts
//   وهذا التَطبيق يَربُط Mock إليهما. الإنتاج يَستَبدِل المَكتَبَة فقط.
builder.Services.AddMockSmsChannel();
builder.Services.AddMockNafathChannel(opts =>
{
    opts.DisplayCode = "00";
    opts.AutoApproveSeconds = 5;
});

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<AuthSession>();

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    await PlatformSeed.RunAsync(scope.ServiceProvider);
}

app.UsePlatformHost();

// ─── SSR form endpoints — لا Blazor interactivity مَطلوب ─────────────
//
// Phone flow:
//   POST /{slug}/auth/phone/login   → يَطلُب OTP، يُعيد توجيه إلى Stage=verify
//   POST /{slug}/auth/phone/verify  → يَتَحَقَّق، يَضَع cookie، يُعيد للـ /{slug}

app.MapPost("/{slug}/auth/phone/login",
    async (string slug, HttpRequest req, HttpResponse res, IDocumentStore store,
           ITenantContext tenant, IOtpChannel channel) =>
{
    if (!tenant.IsResolved) return Results.NotFound();
    var phone = req.Form["phone"].ToString().Trim();
    if (string.IsNullOrEmpty(phone))
        return Results.Redirect($"/{slug}/login?err=phone_required");

    await AuthHandlers.RequestPhoneOtpHandler(
        new RequestPhoneOtp(phone), tenant, channel, default);
    return Results.Redirect($"/{slug}/login?stage=verify&phone={Uri.EscapeDataString(phone)}");
}).DisableAntiforgery();

app.MapPost("/{slug}/auth/phone/verify",
    async (string slug, HttpRequest req, HttpResponse res, IDocumentStore store,
           ITenantContext tenant) =>
{
    if (!tenant.IsResolved) return Results.NotFound();
    var phone = req.Form["phone"].ToString().Trim();
    var code = req.Form["code"].ToString().Trim();
    var result = await AuthHandlers.VerifyPhoneOtpHandler(
        new VerifyPhoneOtp(phone, code), tenant, store);
    if (result is null)
        return Results.Redirect(
            $"/{slug}/login?stage=verify&phone={Uri.EscapeDataString(phone)}&err=code_invalid");

    AuthSession.WriteCookie(res, slug, result);
    return Results.Redirect($"/{slug}");
}).DisableAntiforgery();

// Nafath flow:
//   POST /{slug}/auth/nafath/login   → يَبدَأ، يُعيد توجيه إلى Stage=verify
//   POST /{slug}/auth/nafath/verify  → يَفحَص الموافَقَة (Mock=5s)، cookie + redirect

app.MapPost("/{slug}/auth/nafath/login",
    async (string slug, HttpRequest req, HttpResponse res,
           ITenantContext tenant, INafathChannel channel) =>
{
    if (!tenant.IsResolved) return Results.NotFound();
    var nid = req.Form["nid"].ToString().Trim();
    if (string.IsNullOrEmpty(nid) || nid.Length != 10)
        return Results.Redirect($"/{slug}/login?err=nid_required");

    var pending = await AuthHandlers.RequestNafathHandler(
        new RequestNafath(nid), tenant, channel, default);
    return Results.Redirect(
        $"/{slug}/login?stage=verify&nid={Uri.EscapeDataString(nid)}" +
        $"&attempt={pending.AttemptId}&code={pending.DisplayCode}");
}).DisableAntiforgery();

app.MapPost("/{slug}/auth/nafath/verify",
    async (string slug, HttpRequest req, HttpResponse res,
           ITenantContext tenant, INafathChannel channel, IDocumentStore store) =>
{
    if (!tenant.IsResolved) return Results.NotFound();
    var nid = req.Form["nid"].ToString().Trim();
    var attempt = req.Form["attempt"].ToString();
    var result = await AuthHandlers.VerifyNafathHandler(
        new VerifyNafath(attempt, nid), tenant, channel, store, default);
    if (result is null)
        return Results.Redirect(
            $"/{slug}/login?stage=verify&nid={Uri.EscapeDataString(nid)}&attempt={attempt}&code=00&err=not_approved");

    AuthSession.WriteCookie(res, slug, result);
    return Results.Redirect($"/{slug}");
}).DisableAntiforgery();

// Logout
app.MapPost("/{slug}/auth/logout", (string slug, HttpContext http) =>
{
    AuthSession.ClearCookie(http.Response, slug);
    return Results.Redirect($"/{slug}");
}).DisableAntiforgery();

// Start chat from listing — يُنشئ مُحادَثَة (أو يَستَخدِم القائِمَة) ثُمّ يُعيد توجيه
app.MapPost("/{slug}/listings/{id:guid}/chat",
    async (string slug, Guid id, HttpRequest req, IDocumentStore store) =>
{
    // اِقرأ المُستَخدِم من cookie
    var token = req.Cookies[AuthSession.CookieName(slug)];
    var parsed = ACommerce.Kit.Auth.Server.AuthHandlers.ParseToken(token);
    if (parsed is null) return Results.Redirect($"/{slug}/login?returnUrl=/{slug}/listings/{id}");
    var (userId, tenantSlug, _) = parsed.Value;
    if (tenantSlug != slug) return Results.Redirect($"/{slug}/login");

    var userName = req.Cookies[AuthSession.CookieName(slug) + ".name"] ?? "أنا";

    await using var s = store.LightweightSession(slug);
    var listing = await s.Events.AggregateStreamAsync<ACommerce.Kit.Listings.Listing>(id);
    if (listing is null) return Results.Redirect($"/{slug}");

    var existing = await s.Query<ACommerce.Kit.Chat.Conversation>()
        .Where(c => c.ListingId == id && (c.OwnerId == userId || c.PartnerId == userId))
        .FirstOrDefaultAsync();
    Guid convId;
    if (existing is not null) convId = existing.Id;
    else
    {
        var conv = new ACommerce.Kit.Chat.Conversation
        {
            Id = Guid.NewGuid(),
            OwnerId = userId, OwnerName = userName,
            PartnerId = Guid.NewGuid(), PartnerName = "صاحِب الإعلان",
            Subject = listing.Title, ListingId = id,
            LastAt = DateTime.UtcNow
        };
        s.Store(conv);
        await s.SaveChangesAsync();
        convId = conv.Id;
    }
    return Results.Redirect($"/{slug}/chats/{convId}");
}).DisableAntiforgery();

// Send chat message
app.MapPost("/{slug}/chats/{conversationId:guid}/send",
    async (string slug, Guid conversationId, HttpRequest req, IDocumentStore store) =>
{
    var token = req.Cookies[AuthSession.CookieName(slug)];
    var parsed = ACommerce.Kit.Auth.Server.AuthHandlers.ParseToken(token);
    if (parsed is null) return Results.Redirect($"/{slug}/login");
    var (userId, tenantSlug, _) = parsed.Value;
    if (tenantSlug != slug) return Results.Redirect($"/{slug}/login");

    var body = req.Form["body"].ToString().Trim();
    if (string.IsNullOrEmpty(body)) return Results.Redirect($"/{slug}/chats/{conversationId}");

    await using var s = store.LightweightSession(slug);
    var conv = await s.LoadAsync<ACommerce.Kit.Chat.Conversation>(conversationId);
    if (conv is null) return Results.Redirect($"/{slug}/chats");
    if (conv.OwnerId != userId && conv.PartnerId != userId) return Results.Forbid();

    var msg = new ACommerce.Kit.Chat.Message
    {
        Id = Guid.NewGuid(),
        ConversationId = conversationId,
        SenderId = userId,
        Body = body,
        SentAt = DateTime.UtcNow
    };
    s.Store(msg);
    conv.LastMessage = body.Length > 100 ? body[..100] : body;
    conv.LastAt = msg.SentAt;
    if (userId == conv.OwnerId) conv.PartnerUnread++;
    else if (userId == conv.PartnerId) conv.OwnerUnread++;
    s.Store(conv);
    await s.SaveChangesAsync();

    return Results.Redirect($"/{slug}/chats/{conversationId}");
}).DisableAntiforgery();

app.MapHub<RealtimeHub>("/realtime");

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
