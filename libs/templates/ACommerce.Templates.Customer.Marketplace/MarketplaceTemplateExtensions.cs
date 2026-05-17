using ACommerce.Kit.Auth;
using ACommerce.Kit.Auth.Server;
using ACommerce.Kit.Chat;
using ACommerce.Kit.Favorites;
using ACommerce.Kit.Listings;
using ACommerce.Platform.Shared;
using Marten;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace ACommerce.Templates.Customer.Marketplace;

/// <summary>
/// نُقطَة دُخول واحِدَة للتَطبيق: <c>services.AddCustomerMarketplaceTemplate()</c>
/// + <c>app.MapCustomerMarketplaceTemplate()</c>. يَجمَع AuthSession +
/// كلّ form endpoints (auth/logout/chat send/listing-chat-start) في
/// مَكان واحِد. التَطبيق لا يَكتُب أيّ منها.
/// </summary>
public static class MarketplaceTemplateExtensions
{
    public static IServiceCollection AddCustomerMarketplaceTemplate(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<AuthSession>();
        services.AddScoped<L>();
        services.AddScoped<ACommerce.Kit.Realtime.Client.RealtimeClient>();
        return services;
    }

    public static IEndpointRouteBuilder MapCustomerMarketplaceTemplate(this IEndpointRouteBuilder app)
    {
        // ─── Phone OTP ──────────────────────────────────────────────────
        app.MapPost("/{slug}/auth/phone/login",
            async (string slug, HttpRequest req, IDocumentStore store,
                   ITenantContext tenant, IOtpChannel channel) =>
        {
            if (!tenant.IsResolved) return Results.NotFound();
            var phone = req.Form["phone"].ToString().Trim();
            if (string.IsNullOrEmpty(phone))
                return Results.Redirect($"/{slug}/login?err=phone_required");
            await AuthHandlers.RequestPhoneOtpHandler(new RequestPhoneOtp(phone), tenant, channel, default);
            return Results.Redirect($"/{slug}/login?stage=verify&phone={Uri.EscapeDataString(phone)}");
        }).DisableAntiforgery();

        app.MapPost("/{slug}/auth/phone/verify",
            async (string slug, HttpRequest req, HttpResponse res, IDocumentStore store, ITenantContext tenant) =>
        {
            if (!tenant.IsResolved) return Results.NotFound();
            var phone = req.Form["phone"].ToString().Trim();
            var code = req.Form["code"].ToString().Trim();
            var result = await AuthHandlers.VerifyPhoneOtpHandler(new VerifyPhoneOtp(phone, code), tenant, store);
            if (result is null)
                return Results.Redirect(
                    $"/{slug}/login?stage=verify&phone={Uri.EscapeDataString(phone)}&err=code_invalid");
            AuthSession.WriteCookie(res, slug, result);
            return Results.Redirect($"/{slug}");
        }).DisableAntiforgery();

        // ─── Nafath ─────────────────────────────────────────────────────
        app.MapPost("/{slug}/auth/nafath/login",
            async (string slug, HttpRequest req, ITenantContext tenant, INafathChannel channel) =>
        {
            if (!tenant.IsResolved) return Results.NotFound();
            var nid = req.Form["nid"].ToString().Trim();
            if (string.IsNullOrEmpty(nid) || nid.Length != 10)
                return Results.Redirect($"/{slug}/login?err=nid_required");
            var pending = await AuthHandlers.RequestNafathHandler(new RequestNafath(nid), tenant, channel, default);
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
                    $"/{slug}/login?stage=verify&nid={Uri.EscapeDataString(nid)}" +
                    $"&attempt={attempt}&code=00&err=not_approved");
            AuthSession.WriteCookie(res, slug, result);
            return Results.Redirect($"/{slug}");
        }).DisableAntiforgery();

        // ─── Language toggle ─────────────────────────────────────────────
        app.MapPost("/lang/{lang}", (string lang, HttpRequest req, HttpResponse res) =>
        {
            var l = lang == "en" ? "en" : "ar";
            res.Cookies.Append(L.CookieName, l, new CookieOptions
            {
                Expires = DateTimeOffset.UtcNow.AddYears(1),
                IsEssential = true, Path = "/", SameSite = SameSiteMode.Lax
            });
            var ret = req.Form["return"].ToString();
            return Results.Redirect(string.IsNullOrEmpty(ret) ? "/" : ret);
        }).DisableAntiforgery();

        // ─── Logout ─────────────────────────────────────────────────────
        app.MapPost("/{slug}/auth/logout", (string slug, HttpContext http) =>
        {
            AuthSession.ClearCookie(http.Response, slug);
            return Results.Redirect($"/{slug}");
        }).DisableAntiforgery();

        // ─── Favorite toggle ────────────────────────────────────────────
        app.MapPost("/{slug}/listings/{id:guid}/favorite",
            async (string slug, Guid id, HttpRequest req, IDocumentStore store) =>
        {
            var token = req.Cookies[AuthSession.CookieName(slug)];
            var parsed = AuthHandlers.ParseToken(token);
            if (parsed is null) return Results.Redirect($"/{slug}/login");
            var (userId, _, _) = parsed.Value;

            await using var s = store.LightweightSession(slug);
            var favId = Favorite.MakeId(userId, id);
            var existing = await s.LoadAsync<Favorite>(favId);
            if (existing is null)
            {
                s.Store(new Favorite { Id = favId, UserId = userId, ListingId = id });
            }
            else
            {
                s.Delete(existing);
            }
            await s.SaveChangesAsync();
            var ret = req.Form["return"].ToString();
            return Results.Redirect(string.IsNullOrEmpty(ret) ? $"/{slug}/listings/{id}" : ret);
        }).DisableAntiforgery();

        // ─── Start chat from listing ────────────────────────────────────
        app.MapPost("/{slug}/listings/{id:guid}/chat",
            async (string slug, Guid id, HttpRequest req, IDocumentStore store) =>
        {
            var token = req.Cookies[AuthSession.CookieName(slug)];
            var parsed = AuthHandlers.ParseToken(token);
            if (parsed is null) return Results.Redirect($"/{slug}/login?returnUrl=/{slug}/listings/{id}");
            var (userId, tenantSlug, _) = parsed.Value;
            if (tenantSlug != slug) return Results.Redirect($"/{slug}/login");
            var userName = req.Cookies[AuthSession.CookieName(slug) + ".name"] ?? "أنا";

            await using var s = store.LightweightSession(slug);
            var listing = await s.Events.AggregateStreamAsync<Listing>(id);
            if (listing is null) return Results.Redirect($"/{slug}");

            var existing = await s.Query<Conversation>()
                .Where(c => c.ListingId == id && (c.OwnerId == userId || c.PartnerId == userId))
                .FirstOrDefaultAsync();
            Guid convId;
            if (existing is not null) convId = existing.Id;
            else
            {
                var conv = new Conversation
                {
                    Id = Guid.NewGuid(),
                    OwnerId = userId, OwnerName = userName,
                    PartnerId = Guid.NewGuid(), PartnerName = "صاحِب الإعلان",
                    Subject = listing.Title, ListingId = id, LastAt = DateTime.UtcNow
                };
                s.Store(conv);
                await s.SaveChangesAsync();
                convId = conv.Id;
            }
            return Results.Redirect($"/{slug}/chats/{convId}");
        }).DisableAntiforgery();

        // ─── Profile save ───────────────────────────────────────────────
        app.MapPost("/{slug}/me/save",
            async (string slug, HttpRequest req, IDocumentStore store) =>
        {
            var token = req.Cookies[AuthSession.CookieName(slug)];
            var parsed = AuthHandlers.ParseToken(token);
            if (parsed is null) return Results.Redirect($"/{slug}/login");
            var (userId, _, _) = parsed.Value;
            var fullName = req.Form["fullName"].ToString().Trim();
            if (fullName.Length == 0) return Results.Redirect($"/{slug}/me/edit");

            await using var s = store.LightweightSession(slug);
            var user = await s.LoadAsync<User>(userId);
            if (user is null) return Results.Redirect($"/{slug}/me");
            user.FullName = fullName;
            user.UpdatedAt = DateTime.UtcNow;
            s.Store(user);
            await s.SaveChangesAsync();

            AuthSession.UpdateNameCookie(req.HttpContext.Response, slug, fullName);
            return Results.Redirect($"/{slug}/me");
        }).DisableAntiforgery();

        // ─── Plans subscribe ────────────────────────────────────────────
        app.MapPost("/{slug}/plans/{planId}/subscribe",
            async (string slug, string planId, HttpRequest req, IDocumentStore store) =>
        {
            var token = req.Cookies[AuthSession.CookieName(slug)];
            var parsed = AuthHandlers.ParseToken(token);
            if (parsed is null) return Results.Redirect($"/{slug}/login?returnUrl=/{slug}/plans");
            var (userId, _, _) = parsed.Value;

            await using var s = store.LightweightSession(slug);
            var plan = await s.LoadAsync<ACommerce.Kit.Subscriptions.Plan>(planId);
            if (plan is null) return Results.Redirect($"/{slug}/plans");
            var ev = new ACommerce.Kit.Subscriptions.SubscriptionCreated(
                Guid.NewGuid(), userId, planId, plan.ListingsQuota, plan.DaysPeriod, DateTime.UtcNow);
            s.Events.StartStream<ACommerce.Kit.Subscriptions.Subscription>(ev.Id, ev);
            await s.SaveChangesAsync();
            return Results.Redirect($"/{slug}/me");
        }).DisableAntiforgery();

        // ─── Support open ticket ────────────────────────────────────────
        app.MapPost("/{slug}/support/open",
            async (string slug, HttpRequest req, IDocumentStore store) =>
        {
            var token = req.Cookies[AuthSession.CookieName(slug)];
            var parsed = AuthHandlers.ParseToken(token);
            if (parsed is null) return Results.Redirect($"/{slug}/login");
            var (userId, _, _) = parsed.Value;
            var userName = req.Cookies[AuthSession.CookieName(slug) + ".name"] ?? "—";
            var subject = req.Form["subject"].ToString().Trim();
            var body    = req.Form["body"].ToString().Trim();
            if (subject.Length == 0 || body.Length == 0) return Results.Redirect($"/{slug}/support");

            await using var s = store.LightweightSession(slug);
            var ev = new ACommerce.Kit.Support.TicketCreated(
                Guid.NewGuid(), userId, userName, subject, body, DateTime.UtcNow);
            s.Events.StartStream<ACommerce.Kit.Support.Ticket>(ev.Id, ev);
            await s.SaveChangesAsync();
            return Results.Redirect($"/{slug}/support");
        }).DisableAntiforgery();

        // ─── Report listing — يَفتَح طَلَب دَعم مُسبَق التَعبِئَة ─────────
        app.MapPost("/{slug}/listings/{id:guid}/report",
            async (string slug, Guid id, HttpRequest req, IDocumentStore store) =>
        {
            var token = req.Cookies[AuthSession.CookieName(slug)];
            var parsed = AuthHandlers.ParseToken(token);
            if (parsed is null) return Results.Redirect($"/{slug}/login?returnUrl=/{slug}/listings/{id}");
            var (userId, tenantSlug, _) = parsed.Value;
            if (tenantSlug != slug) return Results.Redirect($"/{slug}/login");
            var userName = req.Cookies[AuthSession.CookieName(slug) + ".name"] ?? "—";

            await using var s = store.LightweightSession(slug);
            var ev = new ACommerce.Kit.Support.TicketCreated(
                Guid.NewGuid(), userId, userName,
                Subject: $"تَبليغ عَن إعلان {id:N}",
                Body:    $"الإعلان: /{slug}/listings/{id}\nالمُبَلِّغ: {userName}",
                At:      DateTime.UtcNow);
            s.Events.StartStream<ACommerce.Kit.Support.Ticket>(ev.Id, ev);
            await s.SaveChangesAsync();
            return Results.Redirect($"/{slug}/support");
        }).DisableAntiforgery();

        // ─── Send chat message ──────────────────────────────────────────
        app.MapPost("/{slug}/chats/{conversationId:guid}/send",
            async (string slug, Guid conversationId, HttpRequest req, IDocumentStore store) =>
        {
            var token = req.Cookies[AuthSession.CookieName(slug)];
            var parsed = AuthHandlers.ParseToken(token);
            if (parsed is null) return Results.Redirect($"/{slug}/login");
            var (userId, tenantSlug, _) = parsed.Value;
            if (tenantSlug != slug) return Results.Redirect($"/{slug}/login");

            var body = req.Form["body"].ToString().Trim();
            if (string.IsNullOrEmpty(body)) return Results.Redirect($"/{slug}/chats/{conversationId}");

            await using var s = store.LightweightSession(slug);
            var conv = await s.LoadAsync<Conversation>(conversationId);
            if (conv is null) return Results.Redirect($"/{slug}/chats");
            if (conv.OwnerId != userId && conv.PartnerId != userId) return Results.Forbid();

            var msg = new Message
            {
                Id = Guid.NewGuid(), ConversationId = conversationId,
                SenderId = userId, Body = body, SentAt = DateTime.UtcNow
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

        return app;
    }
}
