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
        services.AddScoped<ACommerce.Templates.Customer.Marketplace.Services.DynamicAttributesService>();
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

            // الخَصائِص الديناميكِيَّة: كُلّ حَقل بِالـ form بِالبادِئَة
            // attr_<Code> يُحَدِّث user.AttributesJson. لا نَمسَح المَفاتيح
            // غَير المَوجودَة (سَلوك upsert: نُحَدِّث المُمَرَّر، نَتُرك الباقي).
            await using var s = store.LightweightSession(slug);
            var user = await s.LoadAsync<User>(userId);
            if (user is null) return Results.Redirect($"/{slug}/me");
            user.FullName = fullName;
            user.UpdatedAt = DateTime.UtcNow;
            foreach (var (key, vals) in req.Form)
            {
                if (!key.StartsWith("attr_", StringComparison.Ordinal)) continue;
                user.AttributesJson[key["attr_".Length..]] = vals.ToString();
            }
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

            var reason = req.Form["reason"].ToString().Trim();
            var note   = req.Form["note"].ToString().Trim();
            if (string.IsNullOrEmpty(reason)) reason = "غَير مُحَدَّد";

            await using var s = store.LightweightSession(slug);
            var ev = new ACommerce.Kit.Support.TicketCreated(
                Guid.NewGuid(), userId, userName,
                Subject: $"تَبليغ: {reason}",
                Body:    $"الإعلان: /{slug}/listings/{id}\nالسَّبَب: {reason}\n\n{note}",
                At:      DateTime.UtcNow);
            s.Events.StartStream<ACommerce.Kit.Support.Ticket>(ev.Id, ev);
            await s.SaveChangesAsync();
            return Results.Redirect($"/{slug}/listings/{id}?reported=1");
        }).DisableAntiforgery();

        // ─── Create listing ─────────────────────────────────────────────
        app.MapPost("/{slug}/listings/create",
            async (string slug, HttpRequest req, IDocumentStore store) =>
        {
            var token = req.Cookies[AuthSession.CookieName(slug)];
            var parsed = AuthHandlers.ParseToken(token);
            if (parsed is null) return Results.Redirect($"/{slug}/login?returnUrl=/{slug}/create-listing");
            var (_, tenantSlug, _) = parsed.Value;
            if (tenantSlug != slug) return Results.Redirect($"/{slug}/login");

            var title       = req.Form["title"].ToString().Trim();
            var description = req.Form["description"].ToString().Trim();
            var category    = req.Form["category"].ToString().Trim();
            var city        = req.Form["city"].ToString().Trim();
            var district    = req.Form["district"].ToString().Trim();
            var priceStr    = req.Form["price"].ToString().Trim();

            if (title.Length < 3 || string.IsNullOrEmpty(category) ||
                !decimal.TryParse(priceStr, out var price) || price <= 0)
            {
                return Results.Redirect($"/{slug}/create-listing?err=invalid");
            }

            // الخَصائِص الديناميكِيَّة: كُلّ حَقل بِالـ form بِالبادِئَة
            // attr_<Code> يَدخُل في Listing.Attributes.
            var dynAttrs = req.Form
                .Where(kv => kv.Key.StartsWith("attr_", StringComparison.Ordinal))
                .ToDictionary(
                    kv => kv.Key["attr_".Length..],
                    kv => kv.Value.ToString());

            await using var s = store.LightweightSession(slug);
            var id = Guid.NewGuid();
            var ev = new ListingCreated(
                id, slug, title,
                string.IsNullOrEmpty(description) ? null : description,
                price, category,
                string.IsNullOrEmpty(city) ? null : city,
                string.IsNullOrEmpty(district) ? null : district,
                dynAttrs,
                DateTime.UtcNow);
            s.Events.StartStream<Listing>(id, ev);
            await s.SaveChangesAsync();
            return Results.Redirect($"/{slug}/listings/{id}");
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

        // ─── Admin: create tenant ───────────────────────────────────────
        // نَموذَج SSR على /admin/tenants/new يُرسِل لِهُنا. عَلى الفَشَل نُعيد
        // إلى نَفس الصَفحَة مَع ?err=X و القِيَم المُدخَلَة لِيَحفَظها الـ form.
        app.MapPost("/admin/tenants/create",
            async (HttpRequest req, IDocumentStore store) =>
        {
            var f = req.Form;
            var slug    = f["slug"].ToString().Trim().ToLowerInvariant();
            var name    = f["name"].ToString().Trim();
            var tagline = f["tagline"].ToString().Trim();
            var color   = f["color"].ToString().Trim();
            var city    = f["city"].ToString().Trim();
            var channel = f["channel"].ToString().Trim();
            if (channel != "phone" && channel != "nafath") channel = "phone";
            var catsRaw = f["categories"].ToString();

            // ── سَلاسِل الإعادَة في حالَة الخَطَأ ──
            string Back(string err) => "/admin/tenants/new" + "?err=" + err
                + "&slug="     + Uri.EscapeDataString(slug)
                + "&name="     + Uri.EscapeDataString(name)
                + "&tagline="  + Uri.EscapeDataString(tagline)
                + "&color="    + Uri.EscapeDataString(color)
                + "&city="     + Uri.EscapeDataString(city)
                + "&channel="  + Uri.EscapeDataString(channel)
                + "&categories=" + Uri.EscapeDataString(catsRaw);

            // ── فَلتَرَة ──
            if (string.IsNullOrEmpty(slug) ||
                !System.Text.RegularExpressions.Regex.IsMatch(slug, "^[a-z0-9_-]+$"))
                return Results.Redirect(Back("slug_required"));
            if (string.IsNullOrEmpty(name))   return Results.Redirect(Back("name_required"));
            if (!System.Text.RegularExpressions.Regex.IsMatch(color, "^#[0-9A-Fa-f]{6}$"))
                return Results.Redirect(Back("color_invalid"));

            // ── الفِئات: كُلّ صَفّ "slug | label | icon | kind" ──
            var categories = new List<ACommerce.Kit.Tenants.Category>();
            var idx = 0;
            foreach (var line in catsRaw.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = line.Split('|', StringSplitOptions.TrimEntries);
                if (parts.Length < 2) return Results.Redirect(Back("bad_categories"));
                var cslug = parts[0].Trim().ToLowerInvariant();
                var clabel = parts[1].Trim();
                if (string.IsNullOrEmpty(cslug) || string.IsNullOrEmpty(clabel))
                    return Results.Redirect(Back("bad_categories"));
                categories.Add(new ACommerce.Kit.Tenants.Category
                {
                    Slug = cslug,
                    Label = clabel,
                    Icon  = parts.Length > 2 ? parts[2].Trim() : "🏠",
                    Kind  = parts.Length > 3 ? parts[3].Trim().ToLowerInvariant() : "",
                    SortOrder = idx++
                });
            }
            if (categories.Count == 0) return Results.Redirect(Back("no_categories"));

            // ── تَحَقُّق مِن عَدَم تَكرار الـ slug ──
            await using var s = store.LightweightSession();
            var existing = await s.LoadAsync<ACommerce.Kit.Tenants.Tenant>(slug);
            if (existing is not null) return Results.Redirect(Back("slug_taken"));

            // ── إنشاء ──
            s.Store(new ACommerce.Kit.Tenants.Tenant
            {
                Id          = slug,
                Name        = name,
                BrandColor  = color,
                TagLine     = tagline,
                City        = city,
                AuthChannel = channel,
                Categories  = categories,
                CreatedAt   = DateTime.UtcNow
            });
            await s.SaveChangesAsync();
            return Results.Redirect($"/admin");
        }).DisableAntiforgery();

        // ─── Admin: save categories ─────────────────────────────────────
        // إعادَة كِتابَة قائِمَة الفِئات بِالكامِل (overwrite). الإعلانات
        // المَوجودَة بِفِئَة مَحذوفَة تَبقى في الـ events لكِن تَختَفي مِن
        // الواجِهَة — هذا قَرار صَريح في النَّص التَوضيحي.
        app.MapPost("/admin/tenants/{slug}/categories/save",
            async (string slug, HttpRequest req, IDocumentStore store) =>
        {
            var catsRaw = req.Form["categories"].ToString();
            string Back(string err) => $"/admin/tenants/{slug}/categories?err={err}";

            var categories = new List<ACommerce.Kit.Tenants.Category>();
            var idx = 0;
            foreach (var line in catsRaw.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var l = line.Trim();
                if (l.Length == 0) continue;
                var parts = l.Split('|', StringSplitOptions.TrimEntries);
                if (parts.Length < 2) return Results.Redirect(Back("bad_categories"));
                var cslug = parts[0].Trim().ToLowerInvariant();
                var clabel = parts[1].Trim();
                if (string.IsNullOrEmpty(cslug) || string.IsNullOrEmpty(clabel))
                    return Results.Redirect(Back("bad_categories"));
                categories.Add(new ACommerce.Kit.Tenants.Category
                {
                    Slug = cslug,
                    Label = clabel,
                    Icon  = parts.Length > 2 && !string.IsNullOrEmpty(parts[2]) ? parts[2].Trim() : "🏠",
                    Kind  = parts.Length > 3 ? parts[3].Trim().ToLowerInvariant() : "",
                    SortOrder = idx++
                });
            }
            if (categories.Count == 0) return Results.Redirect(Back("no_categories"));

            await using var s = store.LightweightSession();
            var t = await s.LoadAsync<ACommerce.Kit.Tenants.Tenant>(slug);
            if (t is null) return Results.Redirect("/admin");
            t.Categories = categories;
            s.Store(t);
            await s.SaveChangesAsync();
            return Results.Redirect($"/admin/tenants/{slug}?saved=1");
        }).DisableAntiforgery();

        // ─── Admin: save branding ───────────────────────────────────────
        app.MapPost("/admin/tenants/{slug}/branding/save",
            async (string slug, HttpRequest req, IDocumentStore store) =>
        {
            var name    = req.Form["name"].ToString().Trim();
            var tagline = req.Form["tagline"].ToString().Trim();
            var city    = req.Form["city"].ToString().Trim();
            var color   = req.Form["color"].ToString().Trim();
            var channel = req.Form["channel"].ToString().Trim();
            if (channel != "phone" && channel != "nafath") channel = "phone";

            if (string.IsNullOrEmpty(name))
                return Results.Redirect($"/admin/tenants/{slug}/branding?err=name_required");
            if (!System.Text.RegularExpressions.Regex.IsMatch(color, "^#[0-9A-Fa-f]{6}$"))
                return Results.Redirect($"/admin/tenants/{slug}/branding?err=color_invalid");

            await using var s = store.LightweightSession();
            var t = await s.LoadAsync<ACommerce.Kit.Tenants.Tenant>(slug);
            if (t is null) return Results.Redirect("/admin");
            t.Name = name;
            t.TagLine = tagline;
            t.City = city;
            t.BrandColor = color;
            t.AuthChannel = channel;
            s.Store(t);
            await s.SaveChangesAsync();
            return Results.Redirect($"/admin/tenants/{slug}?saved=1");
        }).DisableAntiforgery();

        // ─── Admin: save regions ────────────────────────────────────────
        // اِحذِف كُلّ DiscoveryRegions الحالِيَّة لِلتَّينَنت ثُمّ أَعِد البِناء.
        // المَدينَة Level=1 (ParentId=null)، الحَيّ Level=2 (ParentId=cityId).
        app.MapPost("/admin/tenants/{slug}/regions/save",
            async (string slug, HttpRequest req, IDocumentStore store) =>
        {
            var raw = req.Form["regions"].ToString();
            if (string.IsNullOrWhiteSpace(raw))
                return Results.Redirect($"/admin/tenants/{slug}/regions?err=empty");

            var cities = new List<(string Name, List<string> Districts)>();
            foreach (var line in raw.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var l = line.Trim();
                if (l.Length == 0) continue;
                if (l.Contains('>'))
                {
                    var parts = l.Split('>', 2);
                    var cityName = parts[0].Trim();
                    if (string.IsNullOrEmpty(cityName))
                        return Results.Redirect($"/admin/tenants/{slug}/regions?err=bad_format");
                    var districts = parts[1]
                        .Split(new[] { '،', ',' },
                               StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Where(d => !string.IsNullOrEmpty(d))
                        .ToList();
                    cities.Add((cityName, districts));
                }
                else
                {
                    cities.Add((l, new List<string>()));
                }
            }
            if (cities.Count == 0)
                return Results.Redirect($"/admin/tenants/{slug}/regions?err=empty");

            await using var s = store.LightweightSession(slug);
            var existing = await s.Query<ImportedRecord>()
                .Where(r => r.Table == "DiscoveryRegions").ToListAsync();
            foreach (var r in existing) s.Delete(r);

            var now = DateTime.UtcNow;
            foreach (var (cityName, districts) in cities)
            {
                var cityId = Guid.NewGuid().ToString();
                s.Store(new ImportedRecord
                {
                    Id = $"DiscoveryRegions/{cityId}",
                    Table = "DiscoveryRegions",
                    SourceId = cityId,
                    ImportedAt = now,
                    Data = new Dictionary<string, object?>
                    {
                        ["Name"]     = cityName,
                        ["ParentId"] = null,
                        ["Level"]    = "1"
                    }
                });
                foreach (var d in districts)
                {
                    var distId = Guid.NewGuid().ToString();
                    s.Store(new ImportedRecord
                    {
                        Id = $"DiscoveryRegions/{distId}",
                        Table = "DiscoveryRegions",
                        SourceId = distId,
                        ImportedAt = now,
                        Data = new Dictionary<string, object?>
                        {
                            ["Name"]     = d,
                            ["ParentId"] = cityId,
                            ["Level"]    = "2"
                        }
                    });
                }
            }
            await s.SaveChangesAsync();
            return Results.Redirect($"/admin/tenants/{slug}/regions?saved=1");
        }).DisableAntiforgery();

        // ─── Admin: save attribute definitions for a scope ──────────────
        // الـ scope إمّا CategoryId (لِإعلانات تِلك الفِئَة) أَو
        // 00000000-0000-0000-0000-000000000F01 (sentinel البروفايل).
        // نُعيد كِتابَة CategoryAttributeMappings لِهذا الـ scope كامِلَة،
        // ونَنشُر AttributeDefinitions + AttributeValues جَديدَة. الـ defs
        // اليَتيمَة (لا scope آخَر يَستَخدِمها) تُحذَف لِتَنظيف الجَدول.
        app.MapPost("/admin/tenants/{slug}/attributes/save",
            async (string slug, HttpRequest req, IDocumentStore store) =>
        {
            var scopeStr = req.Form["scope"].ToString().Trim();
            var defsRaw  = req.Form["defs"].ToString();

            if (!Guid.TryParse(scopeStr, out var scopeId))
                return Results.Redirect($"/admin/tenants/{slug}/attributes?err=no_scope");

            string Back(string err) =>
                $"/admin/tenants/{slug}/attributes?scope={scopeId}&err={err}";

            var rows = new List<(string Code, string Name, string Type, bool Req,
                                 List<(string Val, string Label)> Opts)>();
            foreach (var line in defsRaw.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var l = line.Trim();
                if (l.Length == 0) continue;
                var parts = l.Split('|', StringSplitOptions.TrimEntries);
                if (parts.Length < 4) return Results.Redirect(Back("bad_format"));
                var code = parts[0];
                var name = parts[1];
                var type = parts[2];
                var req2 = parts[3].Equals("req", StringComparison.OrdinalIgnoreCase);
                if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(name) ||
                    string.IsNullOrEmpty(type))
                    return Results.Redirect(Back("bad_format"));
                var opts = new List<(string Val, string Label)>();
                if (parts.Length >= 5 && !string.IsNullOrEmpty(parts[4]))
                {
                    foreach (var pair in parts[4].Split(
                                 new[] { '،', ',' },
                                 StringSplitOptions.RemoveEmptyEntries |
                                 StringSplitOptions.TrimEntries))
                    {
                        var kv = pair.Split('=', 2);
                        if (kv.Length != 2) return Results.Redirect(Back("bad_format"));
                        opts.Add((kv[0].Trim(), kv[1].Trim()));
                    }
                }
                rows.Add((code, name, type, req2, opts));
            }

            await using var s = store.LightweightSession(slug);

            // اِجلِب كُلّ الـ Mappings والـ defs الحالِيَّة في الذاكِرَة
            // مَرَّة واحِدَة — أَسهَل لِفَلتَرَة الـ JsonElement يَدَويّاً.
            var allMappings = await s.Query<ImportedRecord>()
                .Where(r => r.Table == "CategoryAttributeMappings").ToListAsync();
            var allDefs = await s.Query<ImportedRecord>()
                .Where(r => r.Table == "AttributeDefinitions").ToListAsync();
            var allValues = await s.Query<ImportedRecord>()
                .Where(r => r.Table == "AttributeValues").ToListAsync();

            var scopeMappings = allMappings
                .Where(m => GuidFromData(m, "CategoryId") == scopeId).ToList();
            var defIdsInScope = scopeMappings
                .Select(m => GuidFromData(m, "AttributeDefinitionId"))
                .Where(g => g != Guid.Empty).Distinct().ToList();
            foreach (var m in scopeMappings) s.Delete(m);

            var stillUsedDefs = allMappings
                .Where(m => GuidFromData(m, "CategoryId") != scopeId)
                .Select(m => GuidFromData(m, "AttributeDefinitionId"))
                .ToHashSet();
            var orphans = defIdsInScope.Where(id => !stillUsedDefs.Contains(id)).ToHashSet();
            if (orphans.Count > 0)
            {
                foreach (var d in allDefs)
                    if (orphans.Contains(GuidFromData(d, "Id"))) s.Delete(d);
                foreach (var v in allValues)
                    if (orphans.Contains(GuidFromData(v, "AttributeDefinitionId"))) s.Delete(v);
            }

            var now = DateTime.UtcNow;
            var order = 0;
            foreach (var (code, name, type, req2, opts) in rows)
            {
                var defId = Guid.NewGuid();
                s.Store(new ImportedRecord
                {
                    Id = $"AttributeDefinitions/{defId}",
                    Table = "AttributeDefinitions",
                    SourceId = defId.ToString(),
                    ImportedAt = now,
                    Data = new Dictionary<string, object?>
                    {
                        ["Id"]         = defId.ToString(),
                        ["Code"]       = code,
                        ["Name"]       = name,
                        ["Type"]       = type,
                        ["IsRequired"] = req2 ? "true" : "false"
                    }
                });
                s.Store(new ImportedRecord
                {
                    Id = $"CategoryAttributeMappings/{defId}-{scopeId}",
                    Table = "CategoryAttributeMappings",
                    SourceId = $"{defId}-{scopeId}",
                    ImportedAt = now,
                    Data = new Dictionary<string, object?>
                    {
                        ["CategoryId"]            = scopeId.ToString(),
                        ["AttributeDefinitionId"] = defId.ToString(),
                        ["SortOrder"]             = order.ToString()
                    }
                });
                var voi = 0;
                foreach (var (val, label) in opts)
                {
                    var vid = Guid.NewGuid();
                    s.Store(new ImportedRecord
                    {
                        Id = $"AttributeValues/{vid}",
                        Table = "AttributeValues",
                        SourceId = vid.ToString(),
                        ImportedAt = now,
                        Data = new Dictionary<string, object?>
                        {
                            ["Id"]                    = vid.ToString(),
                            ["AttributeDefinitionId"] = defId.ToString(),
                            ["Value"]                 = val,
                            ["DisplayName"]           = label,
                            ["SortOrder"]             = voi.ToString()
                        }
                    });
                    voi++;
                }
                order++;
            }
            await s.SaveChangesAsync();
            return Results.Redirect($"/admin/tenants/{slug}/attributes?scope={scopeId}&saved=1");
        }).DisableAntiforgery();

        return app;
    }

    // قِراءَة قِيمَة Guid مِن Dictionary مَع التَّعامُل مَع JsonElement
    // (Marten يَفُكّ التَسلسُل إلى JsonElement لِلقِيَم العامَّة).
    private static Guid GuidFromData(ImportedRecord r, string key)
    {
        if (!r.Data.TryGetValue(key, out var v) || v is null) return Guid.Empty;
        string? str = v is System.Text.Json.JsonElement el
            ? (el.ValueKind == System.Text.Json.JsonValueKind.String ? el.GetString() : el.ToString())
            : v.ToString();
        return Guid.TryParse(str, out var g) ? g : Guid.Empty;
    }
}
