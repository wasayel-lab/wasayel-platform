using ACommerce.Kit.Auth;
using ACommerce.Kit.Auth.Server;
using ACommerce.Kit.Chat;
using ACommerce.Kit.Favorites;
using ACommerce.Kit.Listings;
using ACommerce.Platform.Shared;
using ACommerce.Templates.Customer.Marketplace.Gates;
using ACommerce.Templates.Customer.Marketplace.Services.TenantConfig;
using Marten;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.SignalR;
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
        // مُزَوِّد الخَلفيّات المُسَمّى: كُلّ وَكيل مَنطِقيّ (Studio / Analysis)
        // يَطلُب مِلَفَّه بِاسمِه، والمُزَوِّد يَحُلّه ويُخَزِّن خَلفيَّة واحِدَة
        // لِكُلّ مِلَفّ مُتَمايِز. تَسجيل واحِد بَدَل الخَلفيَّة المُشتَرَكَة.
        services.AddSingleton<
            ACommerce.Templates.Customer.Marketplace.Services.IAgentBackendProvider,
            ACommerce.Templates.Customer.Marketplace.Services.AgentBackendProvider>();
        services.AddSingleton<ACommerce.Templates.Customer.Marketplace.Services.AgentService>();
        services.AddSingleton<ACommerce.Templates.Customer.Marketplace.Services.AgentToolExecutor>();

        // أَدوار المُستَأجِر وَقتَ التَّشغيل — Singleton لِأَنّ الكاش
        // فيه (بِمِفتاح المُستَأجِر) يَجِب أَن يَعبُر الطَلَبات، وإلّا
        // لَما كانَ كاشاً. والعَزل لا يَعتَمِد عَلى عُمر الخِدمَة بَل
        // عَلى أَنّ كُلّ قِراءَة تُفتَح بِجَلسَة سلاج المُستَأجِر.
        services.AddSingleton<ACommerce.Templates.Customer.Marketplace.Services.TenantRoleService>();

        // ثيم المُستَأجِر وَقتَ التَّشغيل — Singleton بِنَفس المُبَرِّر
        // حَرفاً: الكاش بِمِفتاح المُستَأجِر يَجِب أَن يَعبُر الطَلَبات،
        // والعَزل يَقَع في جَلسَة السلاج لا في عُمر الخِدمَة.
        services.AddSingleton<ACommerce.Templates.Customer.Marketplace.Services.TenantThemeService>();

        // باقات المُستَأجِر وَقتَ التَّشغيل — Singleton بِنَفس المُبَرِّر
        // حَرفاً: الكاش بِمِفتاح المُستَأجِر يَجِب أَن يَعبُر الطَلَبات.
        services.AddSingleton<ACommerce.Templates.Customer.Marketplace.Services.TenantPlanService>();

        // ─── الاستِحقاق ──────────────────────────────────────────────────
        // Scoped لا Singleton: بِلا كاش — الرَصيد حالَة تَتَغَيَّر بِكُلّ
        // عَمَلِيَّة، وكاشُها هو بِعَينِه ما يَجعَل الفَحص يَكذِب. والقِراءَة
        // تُفتَح بِجَلسَة سلاج المُستَأجِر كَغَيرِها.
        services.AddScoped<ACommerce.Kit.Subscriptions.IEntitlements,
                           ACommerce.Kit.Subscriptions.SubscriptionEntitlements>();

        // ─── مُصادَقَة عُدَّة المَظهَر عِندَ الإقلاع ──────────────────────
        // مَسّ صَريح لِلكاتالوجَين هُنا — لا انتِظاراً لِأَوَّل طَلَب.
        // الثيم الافتِراضيّ يُصادَق مُكتَمِلاً، والحُزَم الثَلاث تُقرَأ
        // وتُصادَق بِبَوّابَة المُستَأجِر (وهي بِالضَبط ما تَصيرُه عِندَ
        // التَطبيق). خَرقٌ في أَيٍّ مِنها يَرمي **قَبل أَن يَستَمِع
        // الخادِم**، فَلا يُكتَشَف بِنَقرَة «تَطبيق» في عَرضٍ أَمامَ
        // مُستَثمِر.
        _ = ACommerce.Kit.Theme.ThemeCatalog.Default;
        _ = ACommerce.Kit.Theme.ThemePresetCatalog.Preload();
        services.AddSingleton<ACommerce.Templates.Customer.Marketplace.Services.WebPushService>();
        services.AddScoped<Gates.GatePipeline>();
        services.AddScoped<Commands.AcceptTermsHandler>();

        // بَوّابَة إدارَة المَتجَر — تَعريف واحِد يَقرَؤُه طَرَفا القِراءَة
        // (صَفَحات /admin/tenants/{slug}/*) وَالكِتابَة (نِقاط الـ POST).
        services.AddScoped<Services.TenantAdminGuard>();
        services.AddScoped<Services.PlatformAdminGuard>();

        // ─── طبقة التحليل الاستثماري (الحاضنة) ──────────────────────────
        services.AddSingleton<Services.Incubator.SaudiDataProvider>();
        services.AddSingleton<Services.Incubator.FeasibilityPromptBuilder>();
        services.AddScoped<Services.Incubator.FeasibilityAnalysisService>();
        services.AddScoped<Services.Incubator.StudioAuth>();
        services.AddScoped<Services.Incubator.TenantFromAnalysisFactory>();
        services.AddScoped<Services.Incubator.StudioTierService>();
        services.AddSingleton<Services.Incubator.FeasibilityExcelExporter>();

        // خَدَمات الـ Deals (تَدَفُّق العَمَلِيّات المُوَحَّد).
        services.AddScoped<Services.Deals.DealsService>();

        // التَّقييمات (تَقييم مُتَبادَل بَعد اكتِمال صَفقَة).
        services.AddScoped<ACommerce.Kit.Reviews.ReviewsService>();

        // سِجِلّ التَّدقيق (مَن فَعَل ماذا، مَتى).
        services.AddScoped<Services.Audit.AuditWriter>();

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
            var asRole = req.Form["as"].ToString().Trim();
            if (string.IsNullOrEmpty(phone))
                return Results.Redirect(Link(req, slug,
                    $"login?err=phone_required" +
                    (string.IsNullOrEmpty(asRole) ? "" : $"&as={Uri.EscapeDataString(asRole)}")));
            await AuthHandlers.RequestPhoneOtpHandler(new RequestPhoneOtp(phone), tenant, channel, default);
            var asParam = string.IsNullOrEmpty(asRole) ? "" : $"&as={Uri.EscapeDataString(asRole)}";
            return Results.Redirect(Link(req, slug, $"login?stage=verify&phone={Uri.EscapeDataString(phone)}{asParam}"));
        }).DisableAntiforgery();

        app.MapPost("/{slug}/auth/phone/verify",
            async (string slug, HttpRequest req, HttpResponse res, IDocumentStore store, ITenantContext tenant) =>
        {
            if (!tenant.IsResolved) return Results.NotFound();
            // اقبَل HTML form (واجِهَة المُستَخدِم) أَو JSON (الـ APIs والفُحوصات).
            string phone = "", code = "", asRoleEarly = "";
            if (req.HasFormContentType)
            {
                phone = req.Form["phone"].ToString().Trim();
                code  = req.Form["code"].ToString().Trim();
                asRoleEarly = req.Form["as"].ToString().Trim();
            }
            else
            {
                try
                {
                    var body = await req.ReadFromJsonAsync<Dictionary<string, string>>();
                    if (body is not null)
                    {
                        body.TryGetValue("Phone", out phone!); phone = (phone ?? "").Trim();
                        body.TryGetValue("Code",  out code!);  code  = (code  ?? "").Trim();
                        body.TryGetValue("As",    out asRoleEarly!); asRoleEarly = (asRoleEarly ?? "").Trim();
                    }
                } catch { /* بِنيَة غَير مُتَوَقَّعَة → نَترُك الحُقول فارِغَة، يَفشَل verify بِشَكل صَريح */ }
            }
            var result = await AuthHandlers.VerifyPhoneOtpHandler(new VerifyPhoneOtp(phone, code), tenant, store);
            if (result is null)
                return Results.Redirect(Link(req, slug,
                    $"login?stage=verify&phone={Uri.EscapeDataString(phone)}&err=code_invalid"));
            var asRole = (asRoleEarly ?? "").ToLowerInvariant();
            // كَتابَة cookie باسم يَتَضَمَّن الدَور — يَسمَح بِجَلَسات مُتَوازِيَة
            // (راكِب في تَبويب، سائِق في آخَر) في نَفس المُتَصَفِّح.
            AuthSession.WriteCookie(res, slug, result,
                role: string.IsNullOrEmpty(asRole) ? null : asRole);
            if (!string.IsNullOrEmpty(asRole))
                await AssignRoleAsync(slug, result.UserId, asRole, store);
            // إن كانَ المُستَخدِم أُنشِئ تَوّاً، أَخطِر مُديري المَتجَر.
            await using (var qs = store.QuerySession(slug))
            {
                var user = await qs.LoadAsync<User>(result.UserId);
                if (user is not null && (DateTime.UtcNow - user.CreatedAt).TotalMinutes < 1)
                    await NotifyAdminsAsync(store, slug, "new_user",
                        "مُستَخدِم جَديد سَجَّل",
                        $"{user.FullName} · {user.Phone}",
                        $"/admin/tenants/{slug}/users");
            }
            return Results.Redirect(await PostLoginRouteAsync(slug, result.UserId, asRole, store));
        }).DisableAntiforgery();

        // ─── Email OTP ──────────────────────────────────────────────────
        // نَفس بِنيَة مَساري الهاتِف حَرفيّاً (نَفس الـ redirects، نَفس
        // أَسماء الـ query، نَفس كَتابَة الـ cookie) — المُختَلِف الحَقل
        // والقَناة المَحقونَة فَقَط.
        app.MapPost("/{slug}/auth/email/login",
            async (string slug, HttpRequest req, IDocumentStore store,
                   ITenantContext tenant, IEmailOtpChannel channel) =>
        {
            if (!tenant.IsResolved) return Results.NotFound();
            var email = EmailAddress.Normalize(req.Form["email"].ToString());
            var asRole = req.Form["as"].ToString().Trim();
            var asParam = string.IsNullOrEmpty(asRole) ? "" : $"&as={Uri.EscapeDataString(asRole)}";
            if (string.IsNullOrEmpty(email))
                return Results.Redirect(Link(req, slug, $"login?err=email_required{asParam}"));
            if (!EmailAddress.IsValid(email))
                return Results.Redirect(Link(req, slug,
                    $"login?err=email_invalid&email={Uri.EscapeDataString(email)}{asParam}"));
            try
            {
                await AuthHandlers.RequestEmailOtpHandler(new RequestEmailOtp(email), tenant, channel, default);
            }
            catch (InvalidOperationException)
            {
                // فَشَل الإرسال أَو تَجاوُز حَدّ المُعَدَّل — لا نَدفَع
                // المُستَخدِم إلى شاشَة كود لَن يَصِلَه أَبَداً.
                return Results.Redirect(Link(req, slug,
                    $"login?err=send_failed&email={Uri.EscapeDataString(email)}{asParam}"));
            }
            return Results.Redirect(Link(req, slug,
                $"login?stage=verify&email={Uri.EscapeDataString(email)}{asParam}"));
        }).DisableAntiforgery();

        app.MapPost("/{slug}/auth/email/verify",
            async (string slug, HttpRequest req, HttpResponse res, IDocumentStore store, ITenantContext tenant) =>
        {
            if (!tenant.IsResolved) return Results.NotFound();
            // اقبَل HTML form (واجِهَة المُستَخدِم) أَو JSON (الـ APIs والفُحوصات).
            string email = "", code = "", asRoleEarly = "";
            if (req.HasFormContentType)
            {
                email = req.Form["email"].ToString();
                code  = req.Form["code"].ToString().Trim();
                asRoleEarly = req.Form["as"].ToString().Trim();
            }
            else
            {
                try
                {
                    var body = await req.ReadFromJsonAsync<Dictionary<string, string>>();
                    if (body is not null)
                    {
                        body.TryGetValue("Email", out email!); email = email ?? "";
                        body.TryGetValue("Code",  out code!);  code  = (code  ?? "").Trim();
                        body.TryGetValue("As",    out asRoleEarly!); asRoleEarly = (asRoleEarly ?? "").Trim();
                    }
                } catch { /* بِنيَة غَير مُتَوَقَّعَة → نَترُك الحُقول فارِغَة، يَفشَل verify بِشَكل صَريح */ }
            }
            email = EmailAddress.Normalize(email);
            var result = await AuthHandlers.VerifyEmailOtpHandler(new VerifyEmailOtp(email, code), tenant, store);
            if (result is null)
                return Results.Redirect(Link(req, slug,
                    $"login?stage=verify&email={Uri.EscapeDataString(email)}&err=code_invalid"));
            var asRole = (asRoleEarly ?? "").ToLowerInvariant();
            AuthSession.WriteCookie(res, slug, result,
                role: string.IsNullOrEmpty(asRole) ? null : asRole);
            if (!string.IsNullOrEmpty(asRole))
                await AssignRoleAsync(slug, result.UserId, asRole, store);
            // إن كانَ المُستَخدِم أُنشِئ تَوّاً، أَخطِر مُديري المَتجَر.
            await using (var qs = store.QuerySession(slug))
            {
                var user = await qs.LoadAsync<User>(result.UserId);
                if (user is not null && (DateTime.UtcNow - user.CreatedAt).TotalMinutes < 1)
                    await NotifyAdminsAsync(store, slug, "new_user",
                        "مُستَخدِم جَديد سَجَّل",
                        $"{user.FullName} · {user.Email}",
                        $"/admin/tenants/{slug}/users");
            }
            return Results.Redirect(await PostLoginRouteAsync(slug, result.UserId, asRole, store));
        }).DisableAntiforgery();

        // ─── Nafath ─────────────────────────────────────────────────────
        app.MapPost("/{slug}/auth/nafath/login",
            async (string slug, HttpRequest req, ITenantContext tenant, INafathChannel channel) =>
        {
            if (!tenant.IsResolved) return Results.NotFound();
            var nid = req.Form["nid"].ToString().Trim();
            if (string.IsNullOrEmpty(nid) || nid.Length != 10)
                return Results.Redirect(Link(req, slug, $"login?err=nid_required"));
            var pending = await AuthHandlers.RequestNafathHandler(new RequestNafath(nid), tenant, channel, default);
            return Results.Redirect(Link(req, slug,
                $"login?stage=verify&nid={Uri.EscapeDataString(nid)}" +
                $"&attempt={pending.AttemptId}&code={pending.DisplayCode}"));
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
                return Results.Redirect(Link(req, slug,
                    $"login?stage=verify&nid={Uri.EscapeDataString(nid)}" +
                    $"&attempt={attempt}&code=00&err=not_approved"));
            var asRole = req.Form["as"].ToString().Trim().ToLowerInvariant();
            AuthSession.WriteCookie(res, slug, result,
                role: string.IsNullOrEmpty(asRole) ? null : asRole);
            if (!string.IsNullOrEmpty(asRole))
                await AssignRoleAsync(slug, result.UserId, asRole, store);
            return Results.Redirect(await PostLoginRouteAsync(slug, result.UserId, asRole, store));
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
            // الوِجهَة تَأتي مِن الفورم — فَتَمُرّ بِـ Services.LocalRedirect
            // لا بِشَرط مَحَلِّيّ. كانَت تُمَرَّر كَما هي: تَحويل مَفتوح.
            return Results.Redirect(
                Services.LocalRedirect.Resolve(req.Form["return"].ToString(), "/"));
        }).DisableAntiforgery();

        // ─── Logout ─────────────────────────────────────────────────────
        app.MapPost("/{slug}/auth/logout",
            async (string slug, HttpContext http, IDocumentStore store) =>
        {
            // اِجلِب أَدوار المَتجَر لِنَمسَح cookies كُلّ الأَدوار المُمكِنَة
            // (المُستَخدِم قَد يَكون مَفتوحاً بِأَكثَر مِن دَور). كانَ يَمسَح
            // cookie واحِد فَيَتَسَرَّب /r/{role}/.
            await using var qs = store.QuerySession();
            var tenant = await qs.LoadAsync<ACommerce.Kit.Tenants.Tenant>(slug);
            var roles = tenant?.Roles.Select(r => r.Slug) ?? Array.Empty<string>();
            AuthSession.ClearAllCookiesForTenant(http.Response, slug, roles);
            return Results.Redirect($"/{slug}");
        }).DisableAntiforgery();

        // ─── عَدّادات الـ unread لِـ realtime-nav.js ───────────────────────
        // الـ JS عَلى المُتَصَفِّح يُنادي هذا عِندَ كُلّ <c>unread_changed</c>
        // مِن SignalR ويُحَدِّث الـ DOM badges بِلا full reload.
        app.MapGet("/{slug}/api/me/unread",
            async (string slug, HttpRequest req, IDocumentStore store) =>
        {
            var parsed = AuthHandlers.ParseToken(AuthSession.ResolveToken(req, slug));
            if (parsed is null || parsed.Value.TenantSlug != slug)
                return Results.Json(new { messages = 0, notifications = 0 });
            var uid = parsed.Value.UserId;

            await using var s = store.QuerySession(slug);
            var convs = await s.Query<ACommerce.Kit.Chat.Conversation>()
                .Where(c => c.OwnerId == uid || c.PartnerId == uid)
                .ToListAsync();
            var msgs = convs.Count(c =>
                (c.OwnerId   == uid && c.OwnerUnread   > 0) ||
                (c.PartnerId == uid && c.PartnerUnread > 0));
            var notifs = await s.Query<ACommerce.Kit.Notifications.Notification>()
                .CountAsync(n => n.UserId == uid && !n.IsRead);
            return Results.Json(new { messages = msgs, notifications = notifs });
        });

        // ─── Favorite toggle ────────────────────────────────────────────
        app.MapPost("/{slug}/listings/{id:guid}/favorite",
            async (string slug, Guid id, HttpRequest req, IDocumentStore store) =>
        {
            var token = AuthSession.ResolveToken(req, slug);
            var parsed = AuthHandlers.ParseToken(token);
            if (parsed is null) return Results.Redirect(Link(req, slug, $"login"));
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
            return Results.Redirect(Services.LocalRedirect.Resolve(
                req.Form["return"].ToString(), $"/{slug}/listings/{id}"));
        }).DisableAntiforgery();

        // ─── Start chat from listing ────────────────────────────────────
        app.MapPost("/{slug}/listings/{id:guid}/chat",
            async (string slug, Guid id, HttpRequest req, IDocumentStore store) =>
        {
            var token = AuthSession.ResolveToken(req, slug);
            var parsed = AuthHandlers.ParseToken(token);
            if (parsed is null) return Results.Redirect(Link(req, slug, $"login?returnUrl=/{slug}/listings/{id}"));
            var (userId, tenantSlug, _) = parsed.Value;
            if (tenantSlug != slug) return Results.Redirect(Link(req, slug, $"login"));
            var userName = AuthSession.ResolveUserName(req, slug) ?? "أنا";

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
            return Results.Redirect(Link(req, slug, $"chats/{convId}"));
        }).DisableAntiforgery();

        // ─── Pick role (after first login or via switch) ────────────────
        app.MapPost("/{slug}/me/role/save",
            async (string slug, HttpRequest req, IDocumentStore store) =>
        {
            var token = AuthSession.ResolveToken(req, slug);
            var parsed = AuthHandlers.ParseToken(token);
            if (parsed is null) return Results.Redirect(Link(req, slug, $"login"));
            var (userId, _, _) = parsed.Value;

            var role = req.Form["role"].ToString().Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(role))
                return Results.Redirect(Link(req, slug, $"me/role"));

            // مَوضِع الالتِقاط (التَسجيل): الدَور المُؤَلَّف المُعتَمَد
            // قابِل لِلاختِيار كَدَور الكاتالوج تَماماً.
            var tenant = await LoadTenantWithRolesAsync(store, slug);
            if (tenant is null) return Results.Redirect("/admin");
            var picked = tenant.Roles.FirstOrDefault(r => r.Slug == role);
            if (picked is null) return Results.Redirect(Link(req, slug, $"me/role?err=invalid_role"));
            // أَدوار إداريَّة لا يُمكِن مَنحُها ذاتيّاً — يُجَهَّز التَّعيين
            // مِن قِبَل إداريّ آخَر أَو DB seed.
            if (picked.CatalogSlug == "tenant_admin")
                return Results.Redirect(Link(req, slug, $"me/role?err=admin_self_grant"));

            await using var s = store.LightweightSession(slug);
            var user = await s.LoadAsync<User>(userId);
            if (user is null) return Results.Redirect(Link(req, slug, $"me"));
            user.ActiveRole = role;
            user.UpdatedAt = DateTime.UtcNow;
            s.Store(user);
            await s.SaveChangesAsync();

            // إن كانَ لِلدَور حُقول بَيانات مَطلوبَة غَير مَملوءَة، حَوِّل
            // إلى onboarding. لَو البَيانات مَوجودَة (مَثَلاً المُستَخدِم
            // عَبَّأَها سابِقاً ثُمّ بَدَّلَ الدَور)، اِذهَب مُباشَرَة إلى
            // HomeRoute بِلا إعادَة طَلَب.
            var roleValues = user.RoleAttributesJson.TryGetValue(picked.Slug, out var rv)
                ? rv : new Dictionary<string, string>();
            var needsOnboarding = picked.Fields
                .Where(f => f.IsRequired)
                .Any(f => !roleValues.TryGetValue(f.Code, out var v) || string.IsNullOrEmpty(v));
            if (needsOnboarding)
                return Results.Redirect(Link(req, slug, $"me/role/onboarding"));
            return Results.Redirect(string.IsNullOrEmpty(picked.HomeRoute)
                ? $"/{slug}" : $"/{slug}{picked.HomeRoute}");
        }).DisableAntiforgery();

        app.MapPost("/{slug}/me/role/onboarding/save",
            async (string slug, HttpRequest req, IDocumentStore store) =>
        {
            var token = AuthSession.ResolveToken(req, slug);
            var parsed = AuthHandlers.ParseToken(token);
            if (parsed is null) return Results.Redirect(Link(req, slug, $"login"));
            var (userId, _, _) = parsed.Value;

            // مَوضِع الالتِقاط (onboarding): مَسار الدَور المُؤَلَّف بَعد
            // الحِفظ يُقرَأ مِن تَعريفِه.
            var tenant = await LoadTenantWithRolesAsync(store, slug);
            if (tenant is null) return Results.Redirect($"/{slug}");

            await using var s = store.LightweightSession(slug);
            var user = await s.LoadAsync<User>(userId);
            if (user is null) return Results.Redirect(Link(req, slug, $"me"));

            foreach (var (key, vals) in req.Form)
            {
                if (!key.StartsWith("role_", StringComparison.Ordinal)) continue;
                var rest = key["role_".Length..];
                var attrIdx = rest.IndexOf("_attr_", StringComparison.Ordinal);
                if (attrIdx <= 0) continue;
                var roleSlug = rest[..attrIdx];
                var attrCode = rest[(attrIdx + "_attr_".Length)..];
                if (!user.RoleAttributesJson.TryGetValue(roleSlug, out var dict))
                {
                    dict = new Dictionary<string, string>();
                    user.RoleAttributesJson[roleSlug] = dict;
                }
                dict[attrCode] = vals.ToString();
            }
            user.UpdatedAt = DateTime.UtcNow;
            s.Store(user);
            await s.SaveChangesAsync();

            var active = tenant.Roles.FirstOrDefault(r => r.Slug == user.ActiveRole);
            return Results.Redirect(string.IsNullOrEmpty(active?.HomeRoute)
                ? $"/{slug}" : $"/{slug}{active.HomeRoute}");
        }).DisableAntiforgery();

        // ─── Profile save ───────────────────────────────────────────────
        app.MapPost("/{slug}/me/save",
            async (string slug, HttpRequest req, IDocumentStore store,
                   ACommerce.Kit.Files.IFileStorage files) =>
        {
            var token = AuthSession.ResolveToken(req, slug);
            var parsed = AuthHandlers.ParseToken(token);
            if (parsed is null) return Results.Redirect(Link(req, slug, $"login"));
            var (userId, _, _) = parsed.Value;
            var fullName = req.Form["fullName"].ToString().Trim();
            if (fullName.Length == 0) return Results.Redirect(Link(req, slug, $"me/edit"));

            // رَفع صورَة المَلَفّ الشَخصيّ إن أُرسِلَت (≤ ٢ MB، صُوَر فَقَط).
            string? newAvatarUrl = null;
            var avatar = req.Form.Files["avatar"];
            if (avatar is { Length: > 0 })
            {
                var ct = avatar.ContentType.ToLowerInvariant();
                if (ct is "image/png" or "image/jpeg" or "image/webp" && avatar.Length <= 2 * 1024 * 1024)
                {
                    var ext = ct.Split('/')[1].Replace("jpeg", "jpg");
                    var key = $"tenants/{slug}/avatars/{userId}.{ext}";
                    await using var stream = avatar.OpenReadStream();
                    var stored = await files.UploadAsync(key, stream, ct);
                    newAvatarUrl = stored.PublicUrl;
                }
            }

            // الخَصائِص الديناميكِيَّة: كُلّ حَقل بِالـ form بِالبادِئَة
            // attr_<Code> يُحَدِّث user.AttributesJson. لا نَمسَح المَفاتيح
            // غَير المَوجودَة (سَلوك upsert: نُحَدِّث المُمَرَّر، نَتُرك الباقي).
            await using var s = store.LightweightSession(slug);
            var user = await s.LoadAsync<User>(userId);
            if (user is null) return Results.Redirect(Link(req, slug, $"me"));
            user.FullName = fullName;
            if (newAvatarUrl is not null) user.AvatarUrl = newAvatarUrl;
            user.UpdatedAt = DateTime.UtcNow;

            // الدَور النَّشِط (يَظهَر فَقَط لَو المَتجَر يُعَرِّف أَدواراً).
            var activeRole = req.Form["activeRole"].ToString().Trim();
            if (!string.IsNullOrEmpty(activeRole)) user.ActiveRole = activeRole;

            // خَصائِص ديناميكِيَّة: attr_<Code> = بروفايل عامّ،
            // role_<roleSlug>_attr_<Code> = خاصّ بِدَور.
            foreach (var (key, vals) in req.Form)
            {
                if (key.StartsWith("attr_", StringComparison.Ordinal))
                {
                    user.AttributesJson[key["attr_".Length..]] = vals.ToString();
                }
                else if (key.StartsWith("role_", StringComparison.Ordinal))
                {
                    // role_{slug}_attr_{code}
                    var rest = key["role_".Length..];
                    var attrIdx = rest.IndexOf("_attr_", StringComparison.Ordinal);
                    if (attrIdx <= 0) continue;
                    var roleSlug = rest[..attrIdx];
                    var attrCode = rest[(attrIdx + "_attr_".Length)..];
                    if (string.IsNullOrEmpty(roleSlug) || string.IsNullOrEmpty(attrCode)) continue;
                    if (!user.RoleAttributesJson.TryGetValue(roleSlug, out var dict))
                    {
                        dict = new Dictionary<string, string>();
                        user.RoleAttributesJson[roleSlug] = dict;
                    }
                    dict[attrCode] = vals.ToString();
                }
            }
            s.Store(user);
            await s.SaveChangesAsync();

            AuthSession.UpdateNameCookie(req.HttpContext.Response, slug, fullName);
            // عُد إلى returnUrl إن أُرسِل (يَحفَظ سِياق الدَّور /r/{role}/me).
            // كانَ الشَرط StartsWith("/") وَحدَه — يُمَرِّر //evil.com.
            return Results.Redirect(Services.LocalRedirect.Resolve(
                req.Form["returnUrl"].ToString(), Link(req, slug, $"me")));
        }).DisableAntiforgery();

        // ─── Plans subscribe ────────────────────────────────────────────
        app.MapPost("/{slug}/plans/{planId}/subscribe",
            async (string slug, string planId, HttpRequest req, IDocumentStore store) =>
        {
            var token = AuthSession.ResolveToken(req, slug);
            var parsed = AuthHandlers.ParseToken(token);
            if (parsed is null) return Results.Redirect(Link(req, slug, $"login?returnUrl=/{slug}/plans"));
            var (userId, _, _) = parsed.Value;

            await using var s = store.LightweightSession(slug);
            var plan = await s.LoadAsync<ACommerce.Kit.Subscriptions.Plan>(planId);
            if (plan is null) return Results.Redirect(Link(req, slug, $"plans"));
            var ev = new ACommerce.Kit.Subscriptions.SubscriptionCreated(
                Guid.NewGuid(), userId, planId, plan.ListingsQuota, plan.DaysPeriod, DateTime.UtcNow);
            s.Events.StartStream<ACommerce.Kit.Subscriptions.Subscription>(ev.Id, ev);
            await s.SaveChangesAsync();
            return Results.Redirect(Link(req, slug, $"me"));
        }).DisableAntiforgery();

        // ─── Support open ticket ────────────────────────────────────────
        app.MapPost("/{slug}/support/open",
            async (string slug, HttpRequest req, IDocumentStore store) =>
        {
            var token = AuthSession.ResolveToken(req, slug);
            var parsed = AuthHandlers.ParseToken(token);
            if (parsed is null) return Results.Redirect(Link(req, slug, $"login"));
            var (userId, _, _) = parsed.Value;
            var userName = AuthSession.ResolveUserName(req, slug) ?? "—";
            var subject = req.Form["subject"].ToString().Trim();
            var body    = req.Form["body"].ToString().Trim();
            if (subject.Length == 0 || body.Length == 0) return Results.Redirect(Link(req, slug, $"support"));

            await using var s = store.LightweightSession(slug);
            var ev = new ACommerce.Kit.Support.TicketCreated(
                Guid.NewGuid(), userId, userName, subject, body, DateTime.UtcNow);
            s.Events.StartStream<ACommerce.Kit.Support.Ticket>(ev.Id, ev);
            await s.SaveChangesAsync();
            return Results.Redirect(Link(req, slug, $"support"));
        }).DisableAntiforgery();

        // ─── Report listing — يَفتَح طَلَب دَعم مُسبَق التَعبِئَة ─────────
        app.MapPost("/{slug}/listings/{id:guid}/report",
            async (string slug, Guid id, HttpRequest req, IDocumentStore store) =>
        {
            var token = AuthSession.ResolveToken(req, slug);
            var parsed = AuthHandlers.ParseToken(token);
            if (parsed is null) return Results.Redirect(Link(req, slug, $"login?returnUrl=/{slug}/listings/{id}"));
            var (userId, tenantSlug, _) = parsed.Value;
            if (tenantSlug != slug) return Results.Redirect(Link(req, slug, $"login"));
            var userName = AuthSession.ResolveUserName(req, slug) ?? "—";

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
            await NotifyAdminsAsync(store, slug, "report",
                $"بَلاغ: {reason}",
                $"{userName} بَلَّغَ عَن إعلان",
                $"/admin/tenants/{slug}/tickets");
            return Results.Redirect(Link(req, slug, $"listings/{id}?reported=1"));
        }).DisableAntiforgery();

        // ─── Create listing — gates: auth + terms + permission ──────────
        // الـ filters تَتَكَفَّل بِالتَّوثيق وَالشُروط وَ "listing.create".
        app.MapPost("/{slug}/listings/create",
            async (string slug, HttpContext http, HttpRequest req, IDocumentStore store,
                   Microsoft.AspNetCore.SignalR.IHubContext<ACommerce.Kit.Realtime.Server.RealtimeHub> hub,
                   ACommerce.Templates.Customer.Marketplace.Services.WebPushService push,
                   ACommerce.Kit.Files.IFileStorage files,
                   ACommerce.Kit.Subscriptions.IEntitlements ents) =>
        {
            var userId = http.UserId();

            var title       = req.Form["title"].ToString().Trim();
            var description = req.Form["description"].ToString().Trim();
            var category    = req.Form["category"].ToString().Trim();
            var city        = req.Form["city"].ToString().Trim();
            var district    = req.Form["district"].ToString().Trim();
            var priceStr    = req.Form["price"].ToString().Trim();
            var acceptsOffers = req.Form["attr_accepts_offers"].ToString()
                .Equals("true", StringComparison.OrdinalIgnoreCase);

            // الحَدّ الأَدنَى لِلسِعر = ١، إلّا لَو الإعلان طَلَب مَفتوح لِلعُروض
            // (الراكِب يَترُك السِعر صِفراً، السائِق يُحَدِّدُه في عَرضِه).
            decimal.TryParse(priceStr, out var price);
            var priceOk = acceptsOffers ? price >= 0 : price > 0;
            if (title.Length < 3 || string.IsNullOrEmpty(category) || !priceOk)
            {
                return Results.Redirect(Link(req, slug, $"create-listing?err=invalid"));
            }

            // الخَصائِص الديناميكِيَّة: كُلّ حَقل بِالـ form بِالبادِئَة
            // attr_<Code> يَدخُل في Listing.Attributes.
            var dynAttrs = req.Form
                .Where(kv => kv.Key.StartsWith("attr_", StringComparison.Ordinal))
                .ToDictionary(
                    kv => kv.Key["attr_".Length..],
                    kv => kv.Value.ToString());
            // اِحفَظ مالِك الإعلان كَخاصِّيَّة لِأَنّ Listing event مازال
            // بِلا OwnerId مُهَيكَل. صَفحَة /me/listings تَستَعمِلها لِلفَلتَرَة.
            dynAttrs["owner_id"] = userId.ToString();

            // ـ رَفع الصُّوَر (إن وُجِدَت) — حَتَّى ٦، ٥ MB لِكُلّ واحِدَة،
            //   أَنواع آمِنَة فَقَط. الـ URLs تُخزَّن JSON-array في
            //   Attributes["photos"] لِيَقرَأها كُلّ مُستَهلِك (بِطاقات
            //   البَحث، صَفحَة التَّفصيل، إلخ). فَشَل رَفع صورَة واحِدَة
            //   لا يَكسِر الإعلان — يُتَجاهَل ويُتابِع.
            var id = Guid.NewGuid();
            var photoUrls = new List<string>(6);
            var photoFiles = req.Form.Files.GetFiles("photos");
            var allowed = new[] { "image/jpeg", "image/png", "image/webp" };
            foreach (var f in photoFiles.Take(6))
            {
                if (f.Length == 0 || f.Length > 5 * 1024 * 1024) continue;
                if (Array.IndexOf(allowed, f.ContentType) < 0) continue;
                var ext = f.ContentType switch
                {
                    "image/png"  => "png",
                    "image/webp" => "webp",
                    _            => "jpg"
                };
                try
                {
                    await using var stream = f.OpenReadStream();
                    var key = $"tenants/{slug}/listings/{id}/{photoUrls.Count}.{ext}";
                    var stored = await files.UploadAsync(key, stream, f.ContentType);
                    photoUrls.Add(stored.PublicUrl);
                }
                catch { /* صَورَة فاشِلَة لا تَكسِر الإعلان */ }
            }
            // ─── الكِتابَة: الإعلان والحِصَّة في جَلسَة واحِدَة ──────────
            //
            // الاستِهلاك يَقَع <b>داخِل</b> هذه الجَلسَة وبِـ
            // SaveChangesAsync واحِدَة تَكتُب تَيار الإعلان وإشعارات
            // البَحث المَحفوظ وحَدَث QuotaConsumed مَعاً. فَإمّا يُنشَر
            // الإعلان وتُستَهلَك الحِصَّة، أَو لا يَقَع أَيُّهُما — بِلا
            // نِداء ثانٍ يُمكِن أَن يَفشَل بَعدَ نَجاح الأَوَّل.
            //
            // ومُحاوَلَة واحِدَة لا حَلقَة: مُستَخدِمانِ يَستَهلِكانِ آخِر
            // وَحدَة يَجعَل أَحَدَهُما يَفشَل عِندَ الحِفظ بِتَضارُب نُسخَة
            // (الإلحاق بِنُسخَة مُتَوَقَّعَة). الخاسِر يُعيد القِراءَة
            // مَرَّةً واحِدَة، فَإن نَفِدَ الرَصيد فَالجَواب <b>مَنع
            // صَريح بِرِسالَتِه</b> لا فَشَل غامِض ولا خَمسُمِئَة.
            var nudged = new HashSet<Guid>();
            var quotaExhausted = false;

            async Task<bool> AttemptAsync()
            {
                await using var s = store.LightweightSession(slug);

                var gate = await ents.ConsumeAsync(
                    s, slug, userId,
                    ACommerce.Kit.Subscriptions.CapabilityCatalog.ListingCreate,
                    ct: http.RequestAborted);
                if (!gate.Allowed) { quotaExhausted = true; return true; }

                var ev = new ListingCreated(
                    id, slug, title,
                    string.IsNullOrEmpty(description) ? null : description,
                    price, category,
                    string.IsNullOrEmpty(city) ? null : city,
                    string.IsNullOrEmpty(district) ? null : district,
                    dynAttrs,
                    DateTime.UtcNow);
                if (photoUrls.Count > 0)
                {
                    // Stream يَبدَأ بِـ Created + Media مَعاً، فَيُسَجَّل
                    // الإعلان كامِلاً بِصُوَرِه في كِتابَة واحِدَة.
                    s.Events.StartStream<Listing>(id, ev,
                        new ListingMediaSet(id, photoUrls, DateTime.UtcNow));
                }
                else
                {
                    s.Events.StartStream<Listing>(id, ev);
                }

                // مُطابَقَة البَحوث المَحفوظَة — لِكُلّ SavedSearch مَفعَّل
                // يَنطَبِق عَلى هذا الإعلان، أَنشِئ Notification لِصاحِبه.
                // المُطابَقَة في الذاكِرَة (مِئات الـ searches لِلتَّينَنت كَحَدّ
                // أَعلى مَعقول).
                var newListing = new Listing
                {
                    Id = id, TenantSlug = slug, Title = title, Description = description,
                    Price = price, CategorySlug = category, City = city, District = district,
                    Attributes = new(dynAttrs), MediaUrls = new(photoUrls), CreatedAt = ev.At
                };
                var savedSearches = await s.Query<ACommerce.Kit.SavedSearches.SavedSearch>()
                    .Where(ss => ss.IsEnabled).ToListAsync();
                nudged.Clear();
                foreach (var ss in savedSearches)
                {
                    if (!ss.Matches(newListing)) continue;
                    s.Store(new ACommerce.Kit.Notifications.Notification
                    {
                        Id = Guid.NewGuid(),
                        UserId = ss.UserId,
                        Type = "saved_search_match",
                        Title = $"إعلان جَديد يُطابِق «{ss.Label}»",
                        Body = title,
                        RelatedUrl = $"/{slug}/listings/{id}",
                        At = DateTime.UtcNow
                    });
                    nudged.Add(ss.UserId);
                }

                try
                {
                    await s.SaveChangesAsync();
                    return true;
                }
                catch (Exception ex) when (IsStreamVersionConflict(ex))
                {
                    // خَسِرَ السِباق — ولَم يُكتَب إعلانُه. المُعامَلَة
                    // ارتَدَّت كامِلَةً، فَلا إعلان يَتيم بِلا حِصَّة.
                    return false;
                }
            }

            if (!await AttemptAsync() && !await AttemptAsync())
            {
                // تَضارَبَ مَرَّتَين — يُرفَع بِرِسالَتِه ولا يُبتَلَع.
                return Results.Redirect(Link(req, slug, $"create-listing?err=busy"));
            }

            if (quotaExhausted)
                return Results.Redirect(Link(req, slug, $"create-listing?err=quota"));

            foreach (var uid in nudged)
            {
                await NudgeAsync(hub, slug, uid);
                await push.SendAsync(store, slug, uid,
                    "إعلان جَديد يُطابِق بَحثكَ",
                    title,
                    url: $"/{slug}/listings/{id}",
                    tag: $"ss-{id}");
            }
            await NotifyAdminsAsync(store, slug, "new_listing",
                "إعلان جَديد",
                title,
                $"/{slug}/listings/{id}", hub);
            return Results.Redirect(Link(req, slug, $"listings/{id}"));
        }).DisableAntiforgery().RequireAuth().RequireTerms()
          .RequirePermission("listing.create")
          // الحارِس مُعلَن في التَوقيع لا في الجِسم (القاعِدَة ٦): يَرُدّ
          // قَبل رَفع الصُوَر، والحَسم الذَرِّيّ في ConsumeAsync داخِل
          // الجَلسَة. الفَحص الآليّ يَربِط الطَرَفَين فَلا يَفتَرِقان.
          .RequireEntitlement(
              ACommerce.Kit.Subscriptions.CapabilityCatalog.ListingCreate,
              redirectPath: "create-listing", errCode: "quota");

        // ─── Saved Searches — create/delete/toggle ──────────────────────
        app.MapPost("/{slug}/searches/save",
            async (string slug, HttpRequest req, IDocumentStore store) =>
        {
            var token = AuthSession.ResolveToken(req, slug);
            var parsed = AuthHandlers.ParseToken(token);
            if (parsed is null) return Results.Redirect(Link(req, slug, $"login"));
            var (userId, _, _) = parsed.Value;

            var label = req.Form["label"].ToString().Trim();
            if (string.IsNullOrEmpty(label)) label = "بَحث جَديد";

            var ss = new ACommerce.Kit.SavedSearches.SavedSearch
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Label = label,
                CategorySlug = NullIfEmpty(req.Form["category"].ToString()),
                City         = NullIfEmpty(req.Form["city"].ToString()),
                District     = NullIfEmpty(req.Form["district"].ToString())
            };
            if (decimal.TryParse(req.Form["min"].ToString(), out var min) && min > 0) ss.MinPrice = min;
            if (decimal.TryParse(req.Form["max"].ToString(), out var max) && max > 0) ss.MaxPrice = max;

            foreach (var (key, vals) in req.Form)
            {
                if (!key.StartsWith("attr_", StringComparison.Ordinal)) continue;
                var v = vals.ToString();
                if (!string.IsNullOrEmpty(v)) ss.Criteria[key["attr_".Length..]] = v;
            }

            await using var s = store.LightweightSession(slug);
            s.Store(ss);
            await s.SaveChangesAsync();
            return Results.Redirect(Link(req, slug, $"me/searches?saved=1"));

            static string? NullIfEmpty(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
        }).DisableAntiforgery();

        app.MapPost("/{slug}/searches/{id:guid}/delete",
            async (string slug, Guid id, HttpRequest req, IDocumentStore store) =>
        {
            var token = AuthSession.ResolveToken(req, slug);
            var parsed = AuthHandlers.ParseToken(token);
            if (parsed is null) return Results.Redirect(Link(req, slug, $"login"));
            var (userId, _, _) = parsed.Value;

            await using var s = store.LightweightSession(slug);
            var ss = await s.LoadAsync<ACommerce.Kit.SavedSearches.SavedSearch>(id);
            if (ss is null || ss.UserId != userId)
                return Results.Redirect(Link(req, slug, $"me/searches"));
            s.Delete(ss);
            await s.SaveChangesAsync();
            return Results.Redirect(Link(req, slug, $"me/searches"));
        }).DisableAntiforgery();

        app.MapPost("/{slug}/searches/{id:guid}/toggle",
            async (string slug, Guid id, HttpRequest req, IDocumentStore store) =>
        {
            var token = AuthSession.ResolveToken(req, slug);
            var parsed = AuthHandlers.ParseToken(token);
            if (parsed is null) return Results.Redirect(Link(req, slug, $"login"));
            var (userId, _, _) = parsed.Value;

            await using var s = store.LightweightSession(slug);
            var ss = await s.LoadAsync<ACommerce.Kit.SavedSearches.SavedSearch>(id);
            if (ss is null || ss.UserId != userId)
                return Results.Redirect(Link(req, slug, $"me/searches"));
            ss.IsEnabled = !ss.IsEnabled;
            s.Store(ss);
            await s.SaveChangesAsync();
            return Results.Redirect(Link(req, slug, $"me/searches"));
        }).DisableAntiforgery();

        // ─── Submit offer on a listing ──────────────────────────────────
        app.MapPost("/{slug}/listings/{id:guid}/offers",
            async (string slug, Guid id, HttpRequest req, IDocumentStore store) =>
        {
            var token = AuthSession.ResolveToken(req, slug);
            var parsed = AuthHandlers.ParseToken(token);
            if (parsed is null) return Results.Redirect(Link(req, slug, $"login?returnUrl=/{slug}/listings/{id}"));
            var (userId, tenantSlug, _) = parsed.Value;
            if (tenantSlug != slug) return Results.Redirect(Link(req, slug, $"login"));

            if (!await HasPermissionAsync(req.HttpContext, slug, userId, "offer.submit", store))
                return Results.Redirect(Link(req, slug, $"listings/{id}?err=forbidden"));
            var userName = AuthSession.ResolveUserName(req, slug) ?? "—";

            var priceStr = req.Form["price"].ToString().Trim();
            var message  = req.Form["message"].ToString().Trim();
            var latStr   = req.Form["lat"].ToString().Trim();
            var lngStr   = req.Form["lng"].ToString().Trim();
            var ttlStr   = req.Form["ttl_minutes"].ToString().Trim();

            // فَلتَرَة صارِمَة: سِعر مَوجَب فَقَط، وَ مَوقِع غَير-صِفر مَطلوب.
            if (!decimal.TryParse(priceStr, out var price) || price <= 0)
                return Results.Redirect(Link(req, slug, $"listings/{id}?err=offer_price"));
            _ = double.TryParse(latStr, System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out var lat);
            _ = double.TryParse(lngStr, System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out var lng);
            if (lat == 0 && lng == 0)
                return Results.Redirect(Link(req, slug, $"listings/{id}?err=offer_geo"));
            _ = int.TryParse(ttlStr, out var ttl);
            if (ttl <= 0) ttl = 15;

            await using var s = store.LightweightSession(slug);
            var listing = await s.Events.AggregateStreamAsync<Listing>(id);
            if (listing is null) return Results.Redirect($"/{slug}");
            // مَنع صاحِب الإعلان مِن تَقديم عَرض عَلى نَفسه.
            if (listing.Attributes.TryGetValue("owner_id", out var ownerStr2) &&
                ownerStr2 == userId.ToString())
                return Results.Redirect(Link(req, slug, $"listings/{id}?err=self_offer"));

            // مَنع تَقديم عَرض جَديد إن كانَ السائِق في رِحلَة نَشِطَة، أَو
            // إن قَطَع رِحلَة في آخِر ٥ دَقائِق (تَهدِئَة لِمَنع الاستِغلال).
            var matches = await s.Query<ACommerce.Kit.Offers.ListingMatch>().ToListAsync();
            var active = matches.FirstOrDefault(m =>
                m.OffererId == userId &&
                m.Status == ACommerce.Kit.Offers.TripStatus.Active);
            if (active is not null)
                return Results.Redirect(Link(req, slug, $"listings/{active.Id}?err=active_trip"));
            var lastAbort = matches
                .Where(m => m.OffererId == userId &&
                            m.Status == ACommerce.Kit.Offers.TripStatus.Aborted &&
                            m.ResolvedBy == "offerer" &&
                            m.ResolvedAt.HasValue)
                .OrderByDescending(m => m.ResolvedAt).FirstOrDefault();
            if (lastAbort is not null &&
                (DateTime.UtcNow - lastAbort.ResolvedAt!.Value).TotalMinutes < 5)
                return Results.Redirect(Link(req, slug, $"listings/{id}?err=cooldown"));

            // اِجمَع خَصائِص العَرض الديناميكِيَّة مِن أَيّ حَقل بِالبادِئَة
            // attr_ (مَثَلاً attr_seats=4 أَو attr_eta_minutes=8).
            var offerAttrs = req.Form
                .Where(kv => kv.Key.StartsWith("attr_", StringComparison.Ordinal))
                .ToDictionary(kv => kv.Key["attr_".Length..], kv => kv.Value.ToString());

            var oid = Guid.NewGuid();
            var ev = new ACommerce.Kit.Offers.OfferSubmitted(
                oid, id, userId, userName, price,
                string.IsNullOrEmpty(message) ? null : message,
                lat, lng,
                DateTime.UtcNow.AddMinutes(ttl), DateTime.UtcNow,
                offerAttrs.Count > 0 ? offerAttrs : null);
            s.Events.StartStream<ACommerce.Kit.Offers.Offer>(oid, ev);
            await s.SaveChangesAsync();
            return Results.Redirect(Link(req, slug, $"listings/{id}?offer=submitted"));
        }).DisableAntiforgery();

        // ─── Accept an offer (listing owner) ────────────────────────────
        app.MapPost("/{slug}/offers/{id:guid}/accept",
            async (string slug, Guid id, HttpRequest req, IDocumentStore store,
                   Microsoft.AspNetCore.SignalR.IHubContext<ACommerce.Kit.Realtime.Server.RealtimeHub> hub,
                   ACommerce.Templates.Customer.Marketplace.Services.WebPushService push) =>
        {
            var token = AuthSession.ResolveToken(req, slug);
            var parsed = AuthHandlers.ParseToken(token);
            if (parsed is null) return Results.Redirect(Link(req, slug, $"login"));
            var (acceptorId, tenantSlug, _) = parsed.Value;
            if (tenantSlug != slug) return Results.Redirect(Link(req, slug, $"login"));

            await using var s = store.LightweightSession(slug);
            var offer = await s.Events.AggregateStreamAsync<ACommerce.Kit.Offers.Offer>(id);
            if (offer is null || offer.Status != ACommerce.Kit.Offers.OfferStatus.Pending)
                return Results.Redirect(Link(req, slug, $"listings/{(offer?.ListingId ?? Guid.Empty)}"));

            // مالِك الإعلان فَقَط يَقبَل العَرض — التَّحَقُّق عَبر خاصِّيَّة
            // owner_id المَحفوظَة عِندَ الإنشاء. الـ UI أَيضاً يُخفي زِرّ
            // القَبول عَن غَير المالِك لكِنّ التَّحَقُّق هُنا هو الحاجِز
            // الفِعليّ.
            var listing = await s.Events.AggregateStreamAsync<Listing>(offer.ListingId);
            if (listing is null) return Results.Redirect($"/{slug}");
            if (!listing.Attributes.TryGetValue("owner_id", out var ownerStr) ||
                ownerStr != acceptorId.ToString())
                return Results.Redirect(Link(req, slug, $"listings/{offer.ListingId}?err=not_owner"));

            var now = DateTime.UtcNow;
            s.Events.Append(id, new ACommerce.Kit.Offers.OfferAccepted(id, now));

            // اِكتُب ListingMatch لِيَعرِف الواجِهَة أَنّ الإعلان مُتَطابِق.
            s.Store(new ACommerce.Kit.Offers.ListingMatch
            {
                Id = offer.ListingId,
                AcceptedOfferId = id,
                OffererId = offer.OffererId,
                OffererName = offer.OffererName,
                AcceptedPrice = offer.Price,
                OffererLat = offer.Lat,
                OffererLng = offer.Lng,
                MatchedAt = now
            });

            // أَغلِق العُروض الأُخرى عَلى نَفس الإعلان تِلقائيّاً.
            var siblings = await s.Query<ACommerce.Kit.Offers.Offer>()
                .Where(o => o.ListingId == offer.ListingId
                         && o.Id != id
                         && o.Status == ACommerce.Kit.Offers.OfferStatus.Pending)
                .ToListAsync();
            foreach (var sib in siblings)
                s.Events.Append(sib.Id, new ACommerce.Kit.Offers.OfferRejected(sib.Id, now));

            // افتَح مُحادَثَة مُؤَقَّتَة لِلتَنسيق — تَنتَهي بَعد 24 ساعَة.
            // الـ Owner هُنا = المُتَّصِل (مالِك الإعلان)، Partner = مُقَدِّم العَرض.
            var acceptorName = AuthSession.ResolveUserName(req, slug) ?? "أنا";
            var conv = new Conversation
            {
                Id = Guid.NewGuid(),
                OwnerId = acceptorId, OwnerName = acceptorName,
                PartnerId = offer.OffererId, PartnerName = offer.OffererName,
                Subject = $"تَنسيق عَرض بِـ {offer.Price:N0} ريال",
                ListingId = offer.ListingId,
                LastAt = now,
                ExpiresAt = now.AddHours(24),
                LinkedOfferId = id
            };
            s.Store(conv);

            // إشعار لِلسائِق بِأَنّ عَرضَه قُبِلَ.
            s.Store(new ACommerce.Kit.Notifications.Notification
            {
                Id = Guid.NewGuid(),
                UserId = offer.OffererId,
                Type = "offer_accepted",
                Title = "تَمّ قَبول عَرضكَ ✓",
                Body  = $"{acceptorName} قَبِلَ عَرضكَ بِـ {offer.Price:N0} ريال. افتَح المُحادَثَة لِلتَنسيق.",
                RelatedUrl = $"/{slug}/chats/{conv.Id}",
                At = now
            });

            await s.SaveChangesAsync();
            // أَخطِر السائِق فَوراً — الإشعار + المُحادَثَة ظَهَرا.
            await NudgeAsync(hub, slug, offer.OffererId);
            await push.SendAsync(store, slug, offer.OffererId,
                "تَمّ قَبول عَرضكَ ✓",
                $"{acceptorName} قَبِلَ عَرضكَ بِـ {offer.Price:N0} ريال.",
                url: $"/{slug}/chats/{conv.Id}",
                tag: $"offer-{id}");
            return Results.Redirect(Link(req, slug, $"chats/{conv.Id}"));
        }).DisableAntiforgery();

        // ─── Reject / Withdraw offer ────────────────────────────────────
        app.MapPost("/{slug}/offers/{id:guid}/reject",
            async (string slug, Guid id, HttpRequest req, IDocumentStore store) =>
        {
            var token = AuthSession.ResolveToken(req, slug);
            var parsed = AuthHandlers.ParseToken(token);
            if (parsed is null) return Results.Redirect(Link(req, slug, $"login"));
            var (rejectorId, _, _) = parsed.Value;

            await using var s = store.LightweightSession(slug);
            var offer = await s.Events.AggregateStreamAsync<ACommerce.Kit.Offers.Offer>(id);
            if (offer is null || offer.Status != ACommerce.Kit.Offers.OfferStatus.Pending)
                return Results.Redirect($"/{slug}");

            // فَقَط مالِك الإعلان يَستَطيع رَفض عَرض (مَن أَرادَ سَحب
            // عَرضِه يَستَخدِم /withdraw).
            var listing = await s.Events.AggregateStreamAsync<Listing>(offer.ListingId);
            if (listing is null ||
                !listing.Attributes.TryGetValue("owner_id", out var ownerStr) ||
                ownerStr != rejectorId.ToString())
                return Results.Redirect(Link(req, slug, $"listings/{offer.ListingId}?err=not_owner"));

            s.Events.Append(id, new ACommerce.Kit.Offers.OfferRejected(id, DateTime.UtcNow));
            await s.SaveChangesAsync();
            return Results.Redirect(Link(req, slug, $"listings/{offer.ListingId}"));
        }).DisableAntiforgery();

        // ─── Trip lifecycle — driver marks "arrived at pickup" ───────────
        // فَحص قُرب: السائِق يُرسِل مَوقِعَه الحاليّ، نُقارِنه مَع
        // pickup_lat/pickup_lng. لَو > 1 كم يُرفَض الادِّعاء.
        app.MapPost("/{slug}/trips/{listingId:guid}/arrived",
            async (string slug, Guid listingId, HttpRequest req, IDocumentStore store,
                   Microsoft.AspNetCore.SignalR.IHubContext<ACommerce.Kit.Realtime.Server.RealtimeHub> hub,
                   ACommerce.Templates.Customer.Marketplace.Services.WebPushService push) =>
        {
            var token = AuthSession.ResolveToken(req, slug);
            var parsed = AuthHandlers.ParseToken(token);
            if (parsed is null) return Results.Redirect(Link(req, slug, $"login"));
            var (userId, _, _) = parsed.Value;

            _ = double.TryParse(req.Form["lat"].ToString(),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var lat);
            _ = double.TryParse(req.Form["lng"].ToString(),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var lng);
            if (lat == 0 && lng == 0)
                return Results.Redirect(Link(req, slug, $"listings/{listingId}?err=arrived_geo"));

            await using var s = store.LightweightSession(slug);
            var match = await s.LoadAsync<ACommerce.Kit.Offers.ListingMatch>(listingId);
            if (match is null || match.Status != ACommerce.Kit.Offers.TripStatus.Active)
                return Results.Redirect(Link(req, slug, $"listings/{listingId}"));
            if (match.OffererId != userId)
                return Results.Redirect(Link(req, slug, $"listings/{listingId}?err=not_driver"));

            var listing = await s.Events.AggregateStreamAsync<Listing>(listingId);
            if (listing is null) return Results.Redirect($"/{slug}");

            // قارِن المَسافَة بَين مَوقِع السائِق وَ نُقطَة الانطِلاق.
            if (listing.Attributes.TryGetValue("pickup_lat", out var plat) &&
                listing.Attributes.TryGetValue("pickup_lng", out var plng) &&
                double.TryParse(plat, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var pLat) &&
                double.TryParse(plng, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var pLng))
            {
                var dKm = ACommerce.Kit.Offers.OfferHelpers.DistanceKm(lat, lng, pLat, pLng);
                if (dKm > 1.0)   // عَتَبَة 1 كم — يُمكِن جَعلُها قابِلَة لِلتَكوين
                    return Results.Redirect(
                        $"/{slug}/listings/{listingId}?err=too_far&dist={dKm:0.#}");
            }

            match.ArrivedAt = DateTime.UtcNow;
            match.ArrivedLat = lat;
            match.ArrivedLng = lng;
            s.Store(match);

            // أَخطِر الراكِب — السائِق وَصَل.
            s.Store(new ACommerce.Kit.Notifications.Notification
            {
                Id = Guid.NewGuid(),
                UserId = ParseListingOwnerId(listing) ?? Guid.Empty,
                Type = "driver_arrived",
                Title = "السائِق وَصَل ✓",
                Body  = $"{match.OffererName} في نُقطَة الانطِلاق.",
                RelatedUrl = $"/{slug}/listings/{listingId}",
                At = DateTime.UtcNow
            });

            await s.SaveChangesAsync();
            var ownerGuid = ParseListingOwnerId(listing);
            if (ownerGuid.HasValue)
            {
                await NudgeAsync(hub, slug, ownerGuid.Value);
                await push.SendAsync(store, slug, ownerGuid.Value,
                    "السائِق وَصَل ✓",
                    $"{match.OffererName} في نُقطَة الانطِلاق.",
                    url: $"/{slug}/listings/{listingId}",
                    tag: $"arrived-{listingId}");
            }
            return Results.Redirect(Link(req, slug, $"listings/{listingId}?trip=arrived"));
        }).DisableAntiforgery();

        // ─── Trip lifecycle — complete / abort ──────────────────────────
        // كِلاهُما عَلى مُستَوى الإعلان (ListingId)، لِأَنّ ListingMatch
        // doc بِالـ Id = ListingId. مَن يُؤَكِّد: owner (الراكِب) أَو
        // offerer (السائِق المَقبول).
        app.MapPost("/{slug}/trips/{listingId:guid}/complete",
            async (string slug, Guid listingId, HttpRequest req, IDocumentStore store) =>
        {
            var token = AuthSession.ResolveToken(req, slug);
            var parsed = AuthHandlers.ParseToken(token);
            if (parsed is null) return Results.Redirect(Link(req, slug, $"login"));
            var (userId, _, _) = parsed.Value;

            await using var s = store.LightweightSession(slug);
            var match = await s.LoadAsync<ACommerce.Kit.Offers.ListingMatch>(listingId);
            if (match is null || match.Status != ACommerce.Kit.Offers.TripStatus.Active)
                return Results.Redirect(Link(req, slug, $"listings/{listingId}"));

            var listing = await s.Events.AggregateStreamAsync<Listing>(listingId);
            var isOwner = listing is not null &&
                          listing.Attributes.TryGetValue("owner_id", out var oid) &&
                          oid == userId.ToString();
            var isOfferer = match.OffererId == userId;
            if (!isOwner && !isOfferer)
                return Results.Redirect(Link(req, slug, $"listings/{listingId}?err=not_party"));

            match.Status = ACommerce.Kit.Offers.TripStatus.Completed;
            match.ResolvedAt = DateTime.UtcNow;
            match.ResolvedBy = isOwner ? "owner" : "offerer";
            s.Store(match);

            // أَنهِ المُحادَثَة المُؤَقَّتَة المُرتَبِطَة بِالعَرض المَقبول.
            var conv = (await s.Query<Conversation>()
                .Where(c => c.LinkedOfferId == match.AcceptedOfferId).ToListAsync())
                .FirstOrDefault();
            if (conv is not null)
            {
                conv.ExpiresAt = DateTime.UtcNow;   // = انتَهَت فَوراً
                s.Store(conv);
            }
            await s.SaveChangesAsync();
            return Results.Redirect(Link(req, slug, $"listings/{listingId}?trip=completed"));
        }).DisableAntiforgery();

        app.MapPost("/{slug}/trips/{listingId:guid}/abort",
            async (string slug, Guid listingId, HttpRequest req, IDocumentStore store) =>
        {
            var token = AuthSession.ResolveToken(req, slug);
            var parsed = AuthHandlers.ParseToken(token);
            if (parsed is null) return Results.Redirect(Link(req, slug, $"login"));
            var (userId, _, _) = parsed.Value;
            var reason = req.Form["reason"].ToString().Trim();

            await using var s = store.LightweightSession(slug);
            var match = await s.LoadAsync<ACommerce.Kit.Offers.ListingMatch>(listingId);
            if (match is null || match.Status != ACommerce.Kit.Offers.TripStatus.Active)
                return Results.Redirect(Link(req, slug, $"listings/{listingId}"));

            var listing = await s.Events.AggregateStreamAsync<Listing>(listingId);
            var isOwner = listing is not null &&
                          listing.Attributes.TryGetValue("owner_id", out var oid) &&
                          oid == userId.ToString();
            var isOfferer = match.OffererId == userId;
            if (!isOwner && !isOfferer)
                return Results.Redirect(Link(req, slug, $"listings/{listingId}?err=not_party"));

            match.Status = ACommerce.Kit.Offers.TripStatus.Aborted;
            match.ResolvedAt = DateTime.UtcNow;
            match.ResolvedBy = isOwner ? "owner" : "offerer";
            match.AbortReason = string.IsNullOrEmpty(reason) ? null : reason;
            s.Store(match);

            var conv = (await s.Query<Conversation>()
                .Where(c => c.LinkedOfferId == match.AcceptedOfferId).ToListAsync())
                .FirstOrDefault();
            if (conv is not null)
            {
                conv.ExpiresAt = DateTime.UtcNow;
                s.Store(conv);
            }
            await s.SaveChangesAsync();
            return Results.Redirect(Link(req, slug, $"listings/{listingId}?trip=aborted"));
        }).DisableAntiforgery();

        app.MapPost("/{slug}/offers/{id:guid}/withdraw",
            async (string slug, Guid id, HttpRequest req, IDocumentStore store) =>
        {
            var token = AuthSession.ResolveToken(req, slug);
            var parsed = AuthHandlers.ParseToken(token);
            if (parsed is null) return Results.Redirect(Link(req, slug, $"login"));
            var (offererId, _, _) = parsed.Value;

            await using var s = store.LightweightSession(slug);
            var offer = await s.Events.AggregateStreamAsync<ACommerce.Kit.Offers.Offer>(id);
            if (offer is null || offer.Status != ACommerce.Kit.Offers.OfferStatus.Pending)
                return Results.Redirect($"/{slug}");
            // فَقَط مُقَدِّم العَرض يَسحَب عَرضَه.
            if (offer.OffererId != offererId)
                return Results.Redirect(Link(req, slug, $"me/offers"));
            s.Events.Append(id, new ACommerce.Kit.Offers.OfferWithdrawn(id, DateTime.UtcNow));
            await s.SaveChangesAsync();
            return Results.Redirect(Link(req, slug, $"me/offers"));
        }).DisableAntiforgery();

        // ─── PWA — Service Worker على الجَذر ──────────────────────────
        // الـ wwwroot لِلمَكتَبَة يُقَدَّم تَحت /_content/<lib>/، لكِنّ SW
        // scope مَحدود تَحت مَسار المَلَفّ نَفسه. فَلِيَستَطيع تَسجيله بِـ
        // scope /{slug}/ يَجِب تَقديمه مِن جَذر المَوقِع.
        // نَستَخدِم WebRootFileProvider الَّذي يَجمَع static assets كُلّ
        // المَكتَبات؛ ابحَث أَوَّلاً في /sw.js لِلتَطبيق المُستَهلِك (تَجاوُز
        // اختِياريّ)، ثُمَّ في /_content/<this-lib>/sw.js.
        app.MapGet("/sw.js", (HttpResponse res, Microsoft.AspNetCore.Hosting.IWebHostEnvironment env) =>
        {
            var fp = env.WebRootFileProvider;
            var candidates = new[]
            {
                "/sw.js",
                "/_content/ACommerce.Templates.Customer.Marketplace/sw.js"
            };
            foreach (var path in candidates)
            {
                var fi = fp.GetFileInfo(path);
                if (fi.Exists)
                {
                    using var s = fi.CreateReadStream();
                    using var ms = new MemoryStream();
                    s.CopyTo(ms);
                    // Service-Worker-Allowed يُوَسِّع الـ scope المَسموح بِه
                    // فَوق مَسار المَلَفّ — نَسمَح بِالجَذر "/".
                    res.Headers["Service-Worker-Allowed"] = "/";
                    return Results.File(ms.ToArray(), "application/javascript",
                        lastModified: fi.LastModified);
                }
            }
            return Results.NotFound();
        });

        // offline.html عَلى الجَذر أَيضاً (لِيَستَطيع SW الوُصول إلَيها).
        app.MapGet("/offline.html", (Microsoft.AspNetCore.Hosting.IWebHostEnvironment env) =>
        {
            var fp = env.WebRootFileProvider;
            foreach (var path in new[] { "/offline.html",
                "/_content/ACommerce.Templates.Customer.Marketplace/offline.html" })
            {
                var fi = fp.GetFileInfo(path);
                if (fi.Exists)
                {
                    using var s = fi.CreateReadStream();
                    using var ms = new MemoryStream();
                    s.CopyTo(ms);
                    return Results.File(ms.ToArray(), "text/html; charset=utf-8");
                }
            }
            return Results.NotFound();
        });

        // ─── PWA — manifest + icons لِكُلّ تَطبيق فَرعيّ ──────────────────
        // كُلّ (slug, role) لَه manifest مُستَقِلّ بِاسم وَلَون وَأَيقونَة
        // مُلائِمَة. الـ scope يُحدِّد حَدّ الـ PWA — تَنَقُّل المُستَخدِم
        // خارِجَه يَفتَحه المُتَصَفِّح كَ صَفحَة عاديَّة. لِمَتاجِر بِلا
        // أَدوار (ashare/ejar) نَعرِض manifest عَلى /{slug} بِلا role.
        app.MapGet("/api/{slug}/manifest.json", async (
            string slug, IDocumentStore store) =>
            await BuildManifestAsync(slug, role: null, store));

        app.MapGet("/api/{slug}/r/{role}/manifest.json", async (
            string slug, string role, IDocumentStore store) =>
            await BuildManifestAsync(slug, role, store));

        // أَيقونَة تِلقائيَّة SVG — تَستَخدِم لَون المَتجَر + الحَرف الأَوَّل
        // مِن اسم الدَور (أَو إيموجي الدَور إن كانَ مَضبوطاً). إذا كانَ
        // المُصَمِّم رَفَعَ أَيقونَة مُخَصَّصَة (Role.PwaIconUrl) نُحَوِّل لَها.
        app.MapGet("/api/{slug}/icon.svg", async (
            string slug, IDocumentStore store) =>
            await BuildIconAsync(slug, role: null, store));

        app.MapGet("/api/{slug}/r/{role}/icon.svg", async (
            string slug, string role, IDocumentStore store) =>
            await BuildIconAsync(slug, role, store));

        // ─── بِطاقَة المُشارَكَة — PNG نُقَطِيّ ──────────────────────────
        // أَيقونَة الـ PWA أَعلاه SVG، وأَكثَر مِنَصّات المُشارَكَة
        // (واتساب، فيسبوك، تويتر، لينكدإن) تَرفُض SVG في og:image فَتَعرِض
        // الرابِط بِلا صورَة. هذه النُقطَة تُعطي نَفس الهُوِيَّة نُقَطِيَّةً
        // بِالمَقاس العُرفيّ 1200×630، بِلا أَيّ حُزمَة رُسوم (المُرَمِّز
        // في ACommerce.Kit.Tenants.Png فَوق ZLibStream المَكتَبيّ).
        app.MapGet("/api/{slug}/og.png", async (
            string slug, IDocumentStore store) =>
        {
            await using var s = store.QuerySession();
            var tenant = await s.LoadAsync<ACommerce.Kit.Tenants.Tenant>(slug);
            if (tenant is null) return Results.NotFound();

            var png = ACommerce.Kit.Tenants.SocialCard.RenderPng(tenant.BrandColor);
            // الرَسم حَتميّ مِن لَون واحِد، فَالكاش الطَويل آمِن: تَغيير
            // لَون المَتجَر نادِر، وزَواحِف المُشارَكَة تُعيد الجَلب بِنَفسِها.
            return Results.File(png, "image/png", lastModified: null, entityTag: null);
        });

        // ─── PWA — VAPID public key (لِـ JS لِبَناء PushSubscription) ─────
        // الـ public key لَيس سِرّاً — يَكفي أَن يَكون مُتاحاً لِأَيّ client.
        // الـ private key يَبقى فَقَط في السيرفر.
        app.MapGet("/api/push/vapid-key",
            (ACommerce.Templates.Customer.Marketplace.Services.WebPushService push)
                => Results.Text(push.PublicKey, "text/plain"));

        // ─── PWA — Web Push subscribe endpoint ───────────────────────────
        // الـ client (sw.js) يَستَلِم رِسالَة Push مِن السيرفر. هذا الـ
        // endpoint يَحفَظ subscription المُستَخدِم لِيَستَطيع السيرفر
        // إرسال push لاحِقاً.
        app.MapPost("/api/{slug}/push/subscribe",
            async (string slug, HttpRequest req, IDocumentStore store) =>
        {
            // مَسار الـ push تَحتَ /api/{slug}/… — بادِئَتُه "api" فَـ
            // ExtractRoleFromPath تُعيد null دائِماً هُنا، وَالسُقوط اليَدَويّ
            // القَديم عَلى الدَور كانَ مَيتاً. ResolveToken يُغَطّيه: عامّ ثُمَّ
            // أَيّ دَور.
            var token = AuthSession.ResolveToken(req, slug);
            var parsed = AuthHandlers.ParseToken(token);
            if (parsed is null) return Results.Unauthorized();
            var (userId, _, _) = parsed.Value;

            using var doc = await System.Text.Json.JsonDocument.ParseAsync(req.Body);
            var root = doc.RootElement;
            var endpoint = root.GetProperty("endpoint").GetString() ?? "";
            var keys = root.GetProperty("keys");
            var p256dh = keys.GetProperty("p256dh").GetString() ?? "";
            var auth   = keys.GetProperty("auth").GetString() ?? "";
            if (string.IsNullOrEmpty(endpoint)) return Results.BadRequest();

            await using var s = store.LightweightSession(slug);
            var user = await s.LoadAsync<User>(userId);
            if (user is null) return Results.NotFound();
            // اِستَبدِل subscription بِنَفس الـ endpoint (نَفس الجِهاز/المُتَصَفِّح)
            user.PushSubscriptions.RemoveAll(p => p.Endpoint == endpoint);
            user.PushSubscriptions.Add(new ACommerce.Kit.Auth.PushSubscription
            {
                Endpoint = endpoint, P256dh = p256dh, Auth = auth,
                CreatedAt = DateTime.UtcNow
            });
            s.Store(user);
            await s.SaveChangesAsync();
            return Results.Ok();
        }).DisableAntiforgery();

        // ─── Terms acceptance — Phase 2 demo: command + pipeline pattern ───
        // الـ adapter يَجمَع المُدخَلات مِن HTTP، يُنشِئ command، يُمَرِّره
        // لِلـ pipeline. الـ pipeline يَفحَص IRequireAuth + IRequireTenant
        // ثُمَّ يَستَدعي الـ handler. لا boilerplate cookie هُنا.
        app.MapPost("/{slug}/terms/accept", async (
            string slug, HttpRequest req, HttpContext http,
            Gates.GatePipeline pipeline, Commands.AcceptTermsHandler handler) =>
        {
            var userId = http.UserId();
            var role   = http.Role();
            // نَفس القَرار الواحِد — كانَ StartsWith("/") وَحدَه هُنا أَيضاً.
            var returnUrl = Services.LocalRedirect.Resolve(
                req.Query["returnUrl"].ToString(), AuthSession.LinkFor(slug, role, ""));

            var cmd = new Commands.AcceptTermsCommand(userId, slug, TermsPolicy.CurrentVersion);
            try
            {
                await pipeline.ExecuteAsync(cmd, () => handler.HandleAsync(cmd));
            }
            catch (Gates.GateDeniedException ex)
            {
                return Results.Redirect(AuthSession.LinkFor(slug, role, $"login?err={ex.GateName}"));
            }
            return Results.Redirect(returnUrl);
        }).DisableAntiforgery().RequireAuth();

        // ─── Live unread counts — polled by JS in App.razor كُلّ ٢٠ ث ─────
        // يُحَدِّث الـ badges في الـ nav بِلا إعادَة تَحميل. مَنطِق العَدّ:
        //   - الرَسائِل: عَدَد المُحادَثات الَّتي فيها OwnerUnread/PartnerUnread
        //     لِلطَّرَف الَّذي = userId. الرَسائِل الَّتي أَرسَلَها المُستَخدِم
        //     لا تُحسَب لِأَنّ /send يَزيد عَدّاد الطَّرَف الآخَر فَقَط.
        //   - الإشعارات: عَدَد Notification بِـ IsRead=false.
        app.MapGet("/api/{slug}/unread-counts",
            async (string slug, HttpRequest req, IDocumentStore store) =>
        {
            var token = AuthSession.ResolveToken(req, slug);
            var parsed = AuthHandlers.ParseToken(token);
            if (parsed is null) return Results.Json(new { messages = 0, notifications = 0 });
            var (userId, tenantSlug, _) = parsed.Value;
            if (tenantSlug != slug) return Results.Json(new { messages = 0, notifications = 0 });

            await using var s = store.QuerySession(slug);
            var convs = await s.Query<Conversation>()
                .Where(c => c.OwnerId == userId || c.PartnerId == userId).ToListAsync();
            var messages = convs.Count(c =>
                (c.OwnerId == userId && c.OwnerUnread > 0) ||
                (c.PartnerId == userId && c.PartnerUnread > 0));
            var notifications = await s.Query<ACommerce.Kit.Notifications.Notification>()
                .CountAsync(n => n.UserId == userId && !n.IsRead);
            return Results.Json(new { messages, notifications });
        });

        // ─── Save driver area (anchor + radius) ─────────────────────────
        app.MapPost("/{slug}/me/area/save",
            async (string slug, HttpRequest req, IDocumentStore store) =>
        {
            var token = AuthSession.ResolveToken(req, slug);
            var parsed = AuthHandlers.ParseToken(token);
            if (parsed is null) return Results.Redirect(Link(req, slug, $"login"));
            var (userId, _, _) = parsed.Value;

            _ = double.TryParse(req.Form["anchor_lat"].ToString(),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var lat);
            _ = double.TryParse(req.Form["anchor_lng"].ToString(),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var lng);
            _ = int.TryParse(req.Form["radius"].ToString(), out var radius);
            if (radius < 0) radius = 0;
            if (radius > 500) radius = 500;

            await using var s = store.LightweightSession(slug);
            var user = await s.LoadAsync<User>(userId);
            if (user is null) return Results.Redirect(Link(req, slug, $"me"));
            user.AnchorLat = lat;
            user.AnchorLng = lng;
            user.RadiusKm  = radius;
            user.UpdatedAt = DateTime.UtcNow;
            s.Store(user);
            await s.SaveChangesAsync();
            return Results.Redirect(Link(req, slug, $"me/area?saved=1"));
        }).DisableAntiforgery();

        // ─── Start direct chat with another user ────────────────────────
        // مُستَخدَم في صَفحَة /{slug}/drivers — العَميل يَفتَح مُحادَثَة
        // مُباشَرَة مَع سائِق بِلا حاجَة لِنَشر طَلَب مِشوار.
        app.MapPost("/{slug}/users/{userId:guid}/chat",
            async (string slug, Guid userId, HttpRequest req, IDocumentStore store) =>
        {
            var token = AuthSession.ResolveToken(req, slug);
            var parsed = AuthHandlers.ParseToken(token);
            if (parsed is null) return Results.Redirect(Link(req, slug, $"login?returnUrl=/{slug}/drivers"));
            var (meId, tenantSlug, _) = parsed.Value;
            if (tenantSlug != slug) return Results.Redirect(Link(req, slug, $"login"));
            if (meId == userId) return Results.Redirect(Link(req, slug, $"drivers"));
            var meName = AuthSession.ResolveUserName(req, slug) ?? "أنا";

            await using var s = store.LightweightSession(slug);
            var partner = await s.LoadAsync<User>(userId);
            if (partner is null) return Results.Redirect(Link(req, slug, $"drivers"));

            // ابحَث عَن مُحادَثَة قائِمَة بَين الاثنَين (بِلا ListingId).
            var existing = (await s.Query<Conversation>()
                .Where(c => c.ListingId == null &&
                            ((c.OwnerId == meId && c.PartnerId == userId) ||
                             (c.OwnerId == userId && c.PartnerId == meId)))
                .ToListAsync()).FirstOrDefault();
            if (existing is not null)
                return Results.Redirect(Link(req, slug, $"chats/{existing.Id}"));

            var conv = new Conversation
            {
                Id = Guid.NewGuid(),
                OwnerId = meId, OwnerName = meName,
                PartnerId = partner.Id, PartnerName = partner.FullName,
                Subject = $"تَواصُل مَع {partner.FullName}",
                ListingId = null,
                LastAt = DateTime.UtcNow,
                // مُحادَثَة عامَّة بِلا TTL — لَيسَت مُؤَقَّتَة كَالمَشوار.
                ExpiresAt = null
            };
            s.Store(conv);
            await s.SaveChangesAsync();
            return Results.Redirect(Link(req, slug, $"chats/{conv.Id}"));
        }).DisableAntiforgery();

        // ─── Send chat message — gates: auth + terms ─────────────────────
        // boilerplate التَّوثيق المُتَكَرِّر استُبدِل بِـ .RequireAuth().RequireTerms()
        // — الـ filter يَكتُب userId إلى HttpContext.Items وَنَقرَأها هُنا.
        app.MapPost("/{slug}/chats/{conversationId:guid}/send",
            async (string slug, Guid conversationId, HttpContext http, HttpRequest req,
                   IDocumentStore store,
                   Microsoft.AspNetCore.SignalR.IHubContext<ACommerce.Kit.Realtime.Server.RealtimeHub> hub,
                   ACommerce.Templates.Customer.Marketplace.Services.WebPushService push) =>
        {
            var userId = http.UserId();

            var body = req.Form["body"].ToString().Trim();
            if (string.IsNullOrEmpty(body)) return Results.Redirect(Link(req, slug, $"chats/{conversationId}"));

            await using var s = store.LightweightSession(slug);
            var conv = await s.LoadAsync<Conversation>(conversationId);
            if (conv is null) return Results.Redirect(Link(req, slug, $"chats"));
            if (conv.OwnerId != userId && conv.PartnerId != userId) return Forbidden();
            if (conv.IsExpired) return Results.Redirect(Link(req, slug, $"chats/{conversationId}?err=expired"));

            var msg = new Message
            {
                Id = Guid.NewGuid(), ConversationId = conversationId,
                SenderId = userId, Body = body, SentAt = DateTime.UtcNow
            };
            s.Store(msg);
            conv.LastMessage = body.Length > 100 ? body[..100] : body;
            conv.LastAt = msg.SentAt;
            // أَنشِئ إشعاراً لِلطَّرَف الآخَر — يَظهَر في /notifications +
            // عَلى جَرَس الـ topnav. تَحَقُّق سَريع: لا تُكَرِّر إشعاراً عَلى
            // نَفس المُحادَثَة في آخِر ٣٠ ثانِيَة لِتَفادي السپام لَو أَرسَل
            // المُستَخدِم رَسائِل مُتَتالِيَة.
            var recipientId = userId == conv.OwnerId ? conv.PartnerId : conv.OwnerId;
            var senderName  = userId == conv.OwnerId ? conv.OwnerName  : conv.PartnerName;
            var since = DateTime.UtcNow.AddSeconds(-30);
            var hasRecent = await s.Query<ACommerce.Kit.Notifications.Notification>()
                .AnyAsync(n => n.UserId == recipientId &&
                               n.Type == "chat_message" &&
                               n.RelatedUrl == $"/{slug}/chats/{conversationId}" &&
                               n.At > since);
            if (!hasRecent)
            {
                s.Store(new ACommerce.Kit.Notifications.Notification
                {
                    Id = Guid.NewGuid(),
                    UserId = recipientId,
                    Type = "chat_message",
                    Title = $"رِسالَة مِن {senderName}",
                    Body = conv.LastMessage ?? "—",
                    RelatedUrl = $"/{slug}/chats/{conversationId}",
                    At = msg.SentAt
                });
            }
            if (userId == conv.OwnerId) conv.PartnerUnread++;
            else if (userId == conv.PartnerId) conv.OwnerUnread++;
            s.Store(conv);
            await s.SaveChangesAsync();
            await NudgeAsync(hub, slug, recipientId);
            if (!hasRecent)
                await push.SendAsync(store, slug, recipientId,
                    $"رِسالَة مِن {senderName}",
                    conv.LastMessage ?? "—",
                    url: $"/{slug}/chats/{conversationId}",
                    tag: $"chat-{conversationId}");
            return Results.Redirect(Link(req, slug, $"chats/{conversationId}"));
        }).DisableAntiforgery().RequireAuth().RequireTerms();

        // ─── Admin: create tenant ───────────────────────────────────────
        // نَموذَج SSR على /admin/tenants/new يُرسِل لِهُنا. عَلى الفَشَل نُعيد
        // إلى نَفس الصَفحَة مَع ?err=X و القِيَم المُدخَلَة لِيَحفَظها الـ form.
        app.MapPost("/admin/tenants/create",
            async (HttpRequest req, IDocumentStore store,
                   Services.Incubator.StudioAuth auth) =>
        {
            // التَّخويل قَبل قِراءَة الحُقول: كانَ الطَّلَب المَجهول يَصِل
            // إلى الفَلتَرَة فَيَرتَدّ بِـ 302 عَن حَقل ناقِص — فَبَدا
            // مَحروساً وَهُوَ مَكشوف. بِجِسم صَحيح كانَ يُنشِئ مُستَأجِراً.
            var creator = await Services.PlatformAdminGuard.EvaluateAsync(store, auth);
            if (!creator.Allowed)
                return Forbidden();

            var f = req.Form;
            var slug    = f["slug"].ToString().Trim().ToLowerInvariant();
            var name    = f["name"].ToString().Trim();
            var tagline = f["tagline"].ToString().Trim();
            var color   = f["color"].ToString().Trim();
            var city    = f["city"].ToString().Trim();
            var channel = f["channel"].ToString().Trim();
            channel = AuthChannels.NormalizeOrDefault(channel);
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
                CreatedAt   = DateTime.UtcNow,
                // المُنشِئ هُوَ المالِك: كانَ المُستَأجِر يولَد يَتيماً
                // (OwnerUserId = Guid.Empty)، فَيَرُدّ TenantAdminGuard
                // مُنشِئَه نَفسَه بِـ 403 عَن إعداد ما أَنشَأ، ثُمَّ يَتَبَنّاه
                // StudioOwnershipSeeder لِأَوَّل مُستَخدِم — أَيّاً كانَ.
                OwnerUserId = creator.User!.Id
            });
            await s.SaveChangesAsync();
            return Results.Redirect($"/admin");
        }).DisableAntiforgery();

        // ─── Admin: grant / revoke tenant_admin to a user ──────────────
        app.MapPost("/admin/tenants/{slug}/users/{userId:guid}/grant-admin",
            async (string slug, Guid userId, HttpRequest req, IDocumentStore store,
                   Services.Incubator.StudioAuth auth,
                   Services.Audit.AuditWriter audit) =>
        {
            if (!await Services.TenantAdminGuard.CanAdministerAsync(store, auth, req, slug))
                return Forbidden();
            await using var g = store.QuerySession();
            var tenant = await g.LoadAsync<ACommerce.Kit.Tenants.Tenant>(slug);
            if (tenant is null ||
                !tenant.Roles.Any(r => r.CatalogSlug == "tenant_admin"))
                return Results.Redirect($"/admin/tenants/{slug}/users");
            await using var s = store.LightweightSession(slug);
            var user = await s.LoadAsync<User>(userId);
            if (user is null) return Results.Redirect($"/admin/tenants/{slug}/users");
            var before = user.ActiveRole;
            user.ActiveRole = "tenant_admin";
            user.UpdatedAt = DateTime.UtcNow;
            s.Store(user);
            await s.SaveChangesAsync();
            // تَصعيد صَلاحِيّات — يُسَجَّل دائِماً (نَفس scope الـ tenant).
            await audit.WriteAsync(slug, auth.UserId, auth.UserName ?? "admin",
                "user.grant_admin", "User", userId.ToString(),
                note: user.FullName, ip: req.HttpContext.Connection.RemoteIpAddress?.ToString(),
                before: before, after: "tenant_admin");
            return Results.Redirect($"/admin/tenants/{slug}/users?saved=1");
        }).DisableAntiforgery();

        app.MapPost("/admin/tenants/{slug}/users/{userId:guid}/revoke-admin",
            async (string slug, Guid userId, HttpRequest req, IDocumentStore store,
                   Services.Incubator.StudioAuth auth,
                   Services.Audit.AuditWriter audit) =>
        {
            if (!await Services.TenantAdminGuard.CanAdministerAsync(store, auth, req, slug))
                return Forbidden();
            await using var g = store.QuerySession();
            var tenant = await g.LoadAsync<ACommerce.Kit.Tenants.Tenant>(slug);
            if (tenant is null) return Results.Redirect($"/admin/tenants/{slug}/users");
            await using var s = store.LightweightSession(slug);
            var user = await s.LoadAsync<User>(userId);
            if (user is null) return Results.Redirect($"/admin/tenants/{slug}/users");
            // اِرجِع لِأَوَّل دَور غَير-إداريّ كَ افتراضي.
            var fallback = tenant.Roles.FirstOrDefault(r => r.CatalogSlug != "tenant_admin");
            var before = user.ActiveRole;
            user.ActiveRole = fallback?.Slug ?? "";
            user.UpdatedAt = DateTime.UtcNow;
            s.Store(user);
            await s.SaveChangesAsync();
            await audit.WriteAsync(slug, auth.UserId, auth.UserName ?? "admin",
                "user.revoke_admin", "User", userId.ToString(),
                note: user.FullName, ip: req.HttpContext.Connection.RemoteIpAddress?.ToString(),
                before: before, after: user.ActiveRole);
            return Results.Redirect($"/admin/tenants/{slug}/users?saved=1");
        }).DisableAntiforgery();

        // ─── Admin: save roles ──────────────────────────────────────────
        // الـ form يُرسِل role_{catalogSlug}=1 لِكُلّ دَور مَختار + default_role
        // لِتَحديد الافتراضي. الـ Role يُنشَأ بِنَسخ القالِب مِن RoleCatalog
        // (Label/Icon/Permissions/Fields). إذا كانَ الدَور مَوجوداً مُسبَقاً
        // نَحتَفِظ بِالتَخصيصات (Label/Icon) لكِنّ نُحَدِّث Permissions/Fields
        // مِن الكاتالوج (لِيَستَفيد المَتجَر مِن تَحديثات الكاتالوج).
        app.MapPost("/admin/tenants/{slug}/roles/save",
            async (string slug, HttpRequest req, IDocumentStore store,
                   Services.Incubator.StudioAuth auth,
                   Services.Audit.AuditWriter audit) =>
        {
            if (!await Services.TenantAdminGuard.CanAdministerAsync(store, auth, req, slug))
                return Forbidden();
            await LogTenantConfigChangeAsync(audit, req, slug, auth, RolesSaveService.AuditAction);

            await using var s = store.LightweightSession();
            var result = await RolesSaveService.SaveAsync(s, slug, TenantConfigSurface.ReadRoles(req));
            if (result.Ok) await s.SaveChangesAsync();

            return TenantConfigSurface.Outcome(result,
                $"/admin/tenants/{slug}/roles?saved=1", $"/admin/tenants/{slug}/roles", "/admin");
        }).DisableAntiforgery();

        // ─── Admin: decide a tenant-authored role definition ────────────
        // القَرار البَشَريّ الَّذي يُحيي تَعريفاً أَلَّفَه الوَكيل. مُعَلَّق
        // ← مُعتَمَد أَو مَرفوض، ولا ثالِث.
        //
        // **البَوّابَة هي بَوّابَة مُشرِف المَنصَّة** لا مُشرِف المَتجَر،
        // وذلك مَقصود ومُعلَن: التَعريف يُضيف دَوراً <b>خارِج كاتالوج
        // المَنصَّة</b> بِصَلاحِيّاتِه وتَركيبِه، وهو قَرار مُستَوى مَنصَّة.
        // مُشرِف المَتجَر يَرى التَعريفات المُعَلَّقَة في صَفحَة أَدوارِه
        // (قِراءَةً) ولا يَعتَمِدُها.
        app.MapPost("/admin/tenants/{slug}/roles/definitions/{roleSlug}/decide",
            async (string slug, string roleSlug, HttpRequest req, IDocumentStore store,
                   Services.Incubator.StudioAuth auth,
                   Services.TenantRoleService roles,
                   Services.Audit.AuditWriter audit) =>
        {
            var decision = await Services.PlatformAdminGuard.EvaluateAsync(store, auth);
            if (!decision.Allowed) return Forbidden();

            var verdict = req.Form["decision"].ToString().Trim() == "approve"
                ? ACommerce.Kit.Roles.TenantRoleStatuses.Approved
                : ACommerce.Kit.Roles.TenantRoleStatuses.Rejected;

            var by = decision.User is { } u ? $"{u.FullName} · {u.Phone}" : "platform-admin";
            var (ok, msg) = await roles.DecideAsync(slug, roleSlug, verdict, by);

            await LogTenantConfigChangeAsync(audit, req, slug, auth, "tenant.role_definition_decide");

            return Results.Redirect(ok
                ? $"/admin/tenants/{slug}/roles?saved=1"
                : $"/admin/tenants/{slug}/roles?err={Uri.EscapeDataString(msg)}");
        }).DisableAntiforgery();

        // ─── Admin: propose / decide a tenant theme ─────────────────────
        // نَفس عَقد تَعريفات الأَدوار حَرفاً: اقتِراح يَكتُب **مُعَلَّقاً**،
        // ثُمَّ قَرار بَشَريّ يُحييه أَو يَرفُضُه — ولا ثالِث. والبَوّابَة
        // بَوّابَة **مُشرِف المَنصَّة**، لِلسَبَب نَفسِه: الثيم يُبَثّ في
        // <head> لِكُلّ زائِر، فَهو قَرار مُستَوى مَنصَّة لا تَفضيل
        // مَتجَر.
        //
        // **ولِماذا نُقطَتان في مَوجَة بِلا واجِهَة**: بِدونِهِما تَبقى
        // طَبَقَة البَيانات كامِلَةً و**لا سَبيل إلى تَفعيلِها في خادِم
        // يَعمَل** — وثيمٌ لا يُبلَغ لا يُثبِت شَيئاً. لا سَطح لاعِب هُنا
        // ولا مُبَدِّل: ذلك المَوجَة التالِيَة.
        app.MapPost("/admin/tenants/{slug}/theme/propose",
            async (string slug, HttpRequest req, IDocumentStore store,
                   Services.Incubator.StudioAuth auth,
                   Services.TenantThemeService themes,
                   Services.Audit.AuditWriter audit) =>
        {
            var decision = await Services.PlatformAdminGuard.EvaluateAsync(store, auth);
            if (!decision.Allowed) return Forbidden();

            var themeSlug = req.Form["theme_slug"].ToString().Trim();
            var json      = req.Form["definition"].ToString();
            var by = decision.User is { } u ? $"{u.FullName} · {u.Phone}" : "platform-admin";

            var (ok, msg) = await themes.ProposeAsync(slug, themeSlug, json, by);
            await LogTenantConfigChangeAsync(audit, req, slug, auth, "tenant.theme_propose");
            return ok ? Results.Ok(msg) : Results.BadRequest(msg);
        }).DisableAntiforgery();

        app.MapPost("/admin/tenants/{slug}/theme/{themeSlug}/decide",
            async (string slug, string themeSlug, HttpRequest req, IDocumentStore store,
                   Services.Incubator.StudioAuth auth,
                   Services.TenantThemeService themes,
                   Services.Audit.AuditWriter audit) =>
        {
            var decision = await Services.PlatformAdminGuard.EvaluateAsync(store, auth);
            if (!decision.Allowed) return Forbidden();

            var verdict = req.Form["decision"].ToString().Trim() == "approve"
                ? ACommerce.Kit.Theme.TenantThemeStatuses.Approved
                : ACommerce.Kit.Theme.TenantThemeStatuses.Rejected;

            var by = decision.User is { } u ? $"{u.FullName} · {u.Phone}" : "platform-admin";
            var (ok, msg) = await themes.DecideAsync(slug, themeSlug, verdict, by);

            await LogTenantConfigChangeAsync(audit, req, slug, auth, "tenant.theme_decide");
            return ok ? Results.Ok(msg) : Results.BadRequest(msg);
        }).DisableAntiforgery();

        // ─── Admin: apply a curated preset (the live switcher) ──────────
        // نُقطَة الزِرّ الواحِد في /admin/tenants/{slug}/theme. لا تُضيف
        // عَقداً جَديداً: تُنادي ProposeAsync ثُمَّ DecideAsync — نَفس
        // المُصادِق مَرَّتَين ونَفس الإبطال — والجَديد الوَحيد أَنّ
        // **جِسم التَعريف يُقرَأ مِن كاتالوج المَنصَّة لا مِن الطَلَب**.
        // فَما يَصِل قاعِدَة البَيانات مَكتوب في هذا المُستودَع.
        //
        // والبَوّابَة بَوّابَة مُشرِف المَنصَّة، لِأَنّ العَمَلِيَّة
        // تَنتَهي بِـ«اعتِماد» — وهي القَرار الَّذي يَجعَل الثيم مَبثوثاً
        // لِكُلّ زائِر. لا يُخَفَّف الحارِس لِأَنّ الواجِهَة صارَت زِرّاً.
        app.MapPost("/admin/tenants/{slug}/theme/apply",
            async (string slug, HttpRequest req, IDocumentStore store,
                   Services.Incubator.StudioAuth auth,
                   Services.TenantThemeService themes,
                   Services.Audit.AuditWriter audit) =>
        {
            var decision = await Services.PlatformAdminGuard.EvaluateAsync(store, auth);
            if (!decision.Allowed) return Forbidden();

            var presetSlug = req.Form["preset"].ToString().Trim();
            var by = decision.User is { } u ? $"{u.FullName} · {u.Phone}" : "platform-admin";

            var (ok, msg) = await themes.ApplyPresetAsync(slug, presetSlug, by);
            await LogTenantConfigChangeAsync(audit, req, slug, auth, "tenant.theme_apply_preset");

            return Results.Redirect(ok
                ? $"/admin/tenants/{slug}/theme?saved={Uri.EscapeDataString(msg)}"
                : $"/admin/tenants/{slug}/theme?err={Uri.EscapeDataString(msg)}");
        }).DisableAntiforgery();

        // ─── Admin: save categories ─────────────────────────────────────
        // إعادَة كِتابَة قائِمَة الفِئات بِالكامِل (overwrite). الإعلانات
        // المَوجودَة بِفِئَة مَحذوفَة تَبقى في الـ events لكِن تَختَفي مِن
        // الواجِهَة — هذا قَرار صَريح في النَّص التَوضيحي.
        app.MapPost("/admin/tenants/{slug}/categories/save",
            async (string slug, HttpRequest req, IDocumentStore store,
                   Services.Incubator.StudioAuth auth,
                   Services.Audit.AuditWriter audit) =>
        {
            if (!await Services.TenantAdminGuard.CanAdministerAsync(store, auth, req, slug))
                return Forbidden();
            await LogTenantConfigChangeAsync(audit, req, slug, auth, CategoriesSaveService.AuditAction);

            await using var s = store.LightweightSession();
            var result = await CategoriesSaveService.SaveAsync(s, slug, TenantConfigSurface.ReadCategories(req));
            if (result.Ok) await s.SaveChangesAsync();

            return TenantConfigSurface.Outcome(result,
                $"/admin/tenants/{slug}?saved=1", $"/admin/tenants/{slug}/categories", "/admin");
        }).DisableAntiforgery();

        // ─── Admin: save branding ───────────────────────────────────────
        // المَنطِق في BrandingSaveService — تَعريفٌ واحِد يُنادِيه هذا
        // المَسار ونَظيرُه في /studio. وما بَقِيَ هُنا أَربَعَة أَشياء
        // لا خامِسَ لَها: الحارِس، والتَدقيق، والمُعامَلَة، والعَرض.
        app.MapPost("/admin/tenants/{slug}/branding/save",
            async (string slug, HttpRequest req, IDocumentStore store,
                   Services.Incubator.StudioAuth auth,
                   Services.Audit.AuditWriter audit) =>
        {
            if (!await Services.TenantAdminGuard.CanAdministerAsync(store, auth, req, slug))
                return Forbidden();
            await LogTenantConfigChangeAsync(audit, req, slug, auth, BrandingSaveService.AuditAction);

            await using var s = store.LightweightSession();
            var result = await BrandingSaveService.SaveAsync(s, slug, TenantConfigSurface.ReadBranding(req));
            if (result.Ok) await s.SaveChangesAsync();

            return TenantConfigSurface.Outcome(result,
                $"/admin/tenants/{slug}?saved=1", $"/admin/tenants/{slug}/branding", "/admin");
        }).DisableAntiforgery();

        // ─── Admin: save PWA per-role (name + custom icon) ──────────────
        // مُتَعَدِّد الـ parts (file upload). لِكُلّ دَور: name_{slug} +
        // icon_{slug} (مَلَفّ) + clear_{slug} (checkbox). الأَيقونَة تُحَوَّل
        // لِـ data: URL وَتُخزَّن مَعَ الدَور. سَقف ٢٥٦ كيلوبايت لِلحِفاظ
        // عَلى حَجم Tenant doc مَعقولاً.
        app.MapPost("/admin/tenants/{slug}/pwa/save",
            async (string slug, HttpRequest req, IDocumentStore store,
                   Services.Incubator.StudioAuth auth,
                   Services.Audit.AuditWriter audit) =>
        {
            if (!await Services.TenantAdminGuard.CanAdministerAsync(store, auth, req, slug))
                return Forbidden();
            await LogTenantConfigChangeAsync(audit, req, slug, auth, "tenant.pwa_save");
            await using var s = store.LightweightSession();
            var t = await s.LoadAsync<ACommerce.Kit.Tenants.Tenant>(slug);
            if (t is null) return Results.Redirect("/admin");

            const long maxBytes = 256 * 1024;
            var allowed = new[] { "image/png", "image/svg+xml", "image/webp" };

            foreach (var r in t.Roles)
            {
                var nameInput = req.Form[$"name_{r.Slug}"].ToString().Trim();
                r.PwaName = string.IsNullOrEmpty(nameInput) ? null : nameInput;

                if (req.Form[$"clear_{r.Slug}"].ToString() == "1")
                    r.PwaIconDataUrl = null;

                var file = req.Form.Files[$"icon_{r.Slug}"];
                if (file is { Length: > 0 })
                {
                    if (file.Length > maxBytes)
                        return Results.Redirect($"/admin/tenants/{slug}/pwa?err=icon_too_large");
                    var ct = file.ContentType.ToLowerInvariant();
                    if (!allowed.Contains(ct))
                        return Results.Redirect($"/admin/tenants/{slug}/pwa?err=icon_bad_type");
                    using var ms = new MemoryStream();
                    await file.CopyToAsync(ms);
                    var b64 = Convert.ToBase64String(ms.ToArray());
                    r.PwaIconDataUrl = $"data:{ct};base64,{b64}";
                }
            }

            s.Store(t);
            await s.SaveChangesAsync();
            return Results.Redirect($"/admin/tenants/{slug}/pwa?saved=1");
        }).DisableAntiforgery();

        // ─── Admin: save regions ────────────────────────────────────────
        // اِحذِف كُلّ DiscoveryRegions الحالِيَّة لِلتَّينَنت ثُمّ أَعِد البِناء.
        // المَدينَة Level=1 (ParentId=null)، الحَيّ Level=2 (ParentId=cityId).
        app.MapPost("/admin/tenants/{slug}/regions/save",
            async (string slug, HttpRequest req, IDocumentStore store,
                   Services.Incubator.StudioAuth auth,
                   Services.Audit.AuditWriter audit) =>
        {
            if (!await Services.TenantAdminGuard.CanAdministerAsync(store, auth, req, slug))
                return Forbidden();
            await LogTenantConfigChangeAsync(audit, req, slug, auth, "tenant.regions_save");
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
            async (string slug, HttpRequest req, IDocumentStore store,
                   Services.Incubator.StudioAuth auth,
                   Services.Audit.AuditWriter audit) =>
        {
            if (!await Services.TenantAdminGuard.CanAdministerAsync(store, auth, req, slug))
                return Forbidden();
            await LogTenantConfigChangeAsync(audit, req, slug, auth, "tenant.attributes_save");
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

        // ─── Admin: Agent — ask ─────────────────────────────────────────
        app.MapPost("/admin/agent/ask",
            async (HttpRequest req, IDocumentStore store,
                   Services.Incubator.StudioAuth auth,
                   ACommerce.Templates.Customer.Marketplace.Services.AgentService agent) =>
        {
            if (!(await Services.PlatformAdminGuard.EvaluateAsync(store, auth)).Allowed)
                return Forbidden();
            var msg = req.Form["message"].ToString().Trim();
            if (string.IsNullOrEmpty(msg))
                return Results.Redirect("/admin/agent?err=empty#composer");
            await agent.AskAsync(msg);
            return Results.Redirect("/admin/agent#latest");
        }).DisableAntiforgery();

        // ─── Admin: Agent — apply a pending tool call ───────────────────
        app.MapPost("/admin/agent/tool/{toolId}/apply",
            async (string toolId, IDocumentStore store,
                   Services.Incubator.StudioAuth auth,
                   ACommerce.Templates.Customer.Marketplace.Services.AgentService agent,
                   ACommerce.Templates.Customer.Marketplace.Services.AgentToolExecutor exec) =>
        {
            if (!(await Services.PlatformAdminGuard.EvaluateAsync(store, auth)).Allowed)
                return Forbidden();
            var session = await agent.LoadSessionAsync();
            var turn = session.Turns.LastOrDefault(t => t.Tool?.Id == toolId);
            if (turn?.Tool is null)
                return Results.Redirect("/admin/agent?err=tool_missing#latest");

            var (ok, msg) = await exec.ExecuteAsync(turn.Tool.Name, turn.Tool.InputJson);
            await agent.UpdateToolStatusAsync(toolId, ok ? "applied" : "error", msg);
            await agent.ContinueAfterToolAsync();
            return Results.Redirect("/admin/agent#latest");
        }).DisableAntiforgery();

        // ─── Admin: Agent — reject a pending tool call ──────────────────
        app.MapPost("/admin/agent/tool/{toolId}/reject",
            async (string toolId, IDocumentStore store,
                   Services.Incubator.StudioAuth auth,
                   ACommerce.Templates.Customer.Marketplace.Services.AgentService agent) =>
        {
            if (!(await Services.PlatformAdminGuard.EvaluateAsync(store, auth)).Allowed)
                return Forbidden();
            await agent.UpdateToolStatusAsync(toolId, "rejected", null);
            await agent.ContinueAfterToolAsync();
            return Results.Redirect("/admin/agent#latest");
        }).DisableAntiforgery();

        // ─── Admin: Agent — reset conversation ──────────────────────────
        app.MapPost("/admin/agent/reset",
            async (IDocumentStore store, Services.Incubator.StudioAuth auth,
                   ACommerce.Templates.Customer.Marketplace.Services.AgentService agent) =>
        {
            if (!(await Services.PlatformAdminGuard.EvaluateAsync(store, auth)).Allowed)
                return Forbidden();
            await agent.ResetAsync();
            return Results.Redirect("/admin/agent");
        }).DisableAntiforgery();

        // ─── Studio — مُصادَقَة وهميَّة + بَدء مِن صَفحَة الهبوط ──────────
        // صَفحَة الهبوط تُرسِل المُطالَبَة هُنا؛ نَحفَظها في cookie مُؤَقَّت
        // ثُمَّ نُحَوِّل لِلدُخول. بَعد الدُخول الناجِح نُنشِئ جَلسَة تَحليل
        // بِالمُطالَبَة ونُشَغِّلها.
        app.MapPost("/studio/begin", (HttpRequest req, HttpResponse res) =>
        {
            var prompt = req.Form["prompt"].ToString().Trim();
            if (!string.IsNullOrEmpty(prompt))
                res.Cookies.Append("ac.studio.prompt", Uri.EscapeDataString(prompt),
                    new CookieOptions { IsEssential = true, Path = "/",
                        Expires = DateTimeOffset.UtcNow.AddHours(2) });
            return Results.Redirect("/studio/auth");
        }).DisableAntiforgery();

        app.MapPost("/studio/auth/login", (HttpRequest req) =>
        {
            // وهميّ: لا إرسال SMS — نَنتَقِل مُباشَرَةً لِمَرحَلَة الرَّمز.
            var phone = req.Form["phone"].ToString().Trim();
            if (string.IsNullOrEmpty(phone))
                return Results.Redirect("/studio/auth?err=phone");
            return Results.Redirect($"/studio/auth?stage=verify&phone={Uri.EscapeDataString(phone)}");
        }).DisableAntiforgery();

        app.MapPost("/studio/auth/verify", async (
            HttpRequest req, HttpResponse res, IDocumentStore store,
            IServiceScopeFactory scopeFactory,
            Services.Incubator.FeasibilityAnalysisService incubator) =>
        {
            var phone = req.Form["phone"].ToString().Trim();
            var code  = req.Form["code"].ToString().Trim();
            var user = await Services.Incubator.StudioAuth.VerifyAsync(store, res, phone, code);
            if (user is null)
                return Results.Redirect(
                    $"/studio/auth?stage=verify&phone={Uri.EscapeDataString(phone)}&err=code");

            // اِربِط المَتاجِر اليَتيمَة بِأَوَّل مُستَخدِم (لَو هذا أَوَّل تَسجيل).
            await Services.Incubator.StudioOwnershipSeeder.RunAsync(store);

            // مُطالَبَة مُعَلَّقَة؟ أَنشِئ جَلسَة وشَغِّل التَّحليل في الخَلفِيَّة.
            var promptCookie = req.Cookies["ac.studio.prompt"];
            if (!string.IsNullOrEmpty(promptCookie))
            {
                // قَبول الشُروط مَطلوب قَبل أَوَّل تَحليل.
                await using (var consentQs = store.QuerySession(Services.Incubator.StudioAuth.Tenant))
                {
                    var consents = await consentQs.Query<Services.Incubator.ConsentRecord>()
                        .Where(c => c.UserId == user.Id && c.Version == Services.Incubator.ConsentPolicy.CurrentVersion)
                        .ToListAsync();
                    if (consents.Count == 0)
                        return Results.Redirect($"/studio/consent?returnUrl=/studio/auth/verify");
                }

                res.Cookies.Delete("ac.studio.prompt");
                var prompt = Uri.UnescapeDataString(promptCookie);

                // tier gate — هَل بَلَغ المُستَخدِم حَدّ تَحاليلِه؟
                using var checkScope = scopeFactory.CreateScope();
                var tier = checkScope.ServiceProvider
                    .GetRequiredService<Services.Incubator.StudioTierService>();
                var gate = await tier.CheckAnalyzeAsync(user.Id);
                if (!gate.Allowed)
                    return Results.Redirect($"/studio?upgrade=analyze");

                var s = await incubator.StartAsync(user.Id, user.FullName);
                await incubator.SaveAnswerAsync(s.Id, "description", prompt);
                await incubator.MarkAnalyzingAsync(s.Id);
                await tier.RecordAnalysisAsync(user.Id);
                _ = Task.Run(async () =>
                {
                    using var scope = scopeFactory.CreateScope();
                    var bg = scope.ServiceProvider
                        .GetRequiredService<Services.Incubator.FeasibilityAnalysisService>();
                    try { await bg.RunAnalysisAsync(s.Id); } catch { }
                });
                return Results.Redirect($"/studio/s/{s.Id}");
            }
            return Results.Redirect("/studio");
        }).DisableAntiforgery();

        app.MapPost("/studio/logout", (HttpResponse res) =>
        {
            Services.Incubator.StudioAuth.DeleteCookie(res);
            return Results.Redirect("/");
        }).DisableAntiforgery();

        // قَبول الشُروط — يَحفَظ ConsentRecord ويُحَوِّل لِـ returnUrl.
        app.MapPost("/studio/consent/accept", async (
            HttpRequest req, IDocumentStore store,
            Services.Incubator.StudioAuth auth) =>
        {
            auth.Load();
            if (!auth.IsAuthenticated) return Results.Redirect("/studio/auth");
            var returnUrl = Services.LocalRedirect.Resolve(
                req.Form["returnUrl"].ToString(), "/studio");

            await using var s = store.LightweightSession(Services.Incubator.StudioAuth.Tenant);
            s.Store(new Services.Incubator.ConsentRecord
            {
                Id = Guid.NewGuid(),
                UserId = auth.UserId!.Value,
                Version = Services.Incubator.ConsentPolicy.CurrentVersion,
                At = DateTime.UtcNow,
                Ip = req.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "",
                UserAgent = req.Headers.UserAgent.ToString()
            });
            await s.SaveChangesAsync();
            return Results.Redirect(returnUrl);
        }).DisableAntiforgery();

        // تَقييم قِسم في دِراسَة (👍/👎) — لِتَحسين الـ prompt لاحِقاً.
        app.MapPost("/studio/s/{id:guid}/feedback", async (
            Guid id, HttpRequest req, IDocumentStore store,
            Services.Incubator.StudioAuth auth) =>
        {
            auth.Load();
            if (!auth.IsAuthenticated) return Results.Redirect("/studio/auth");
            var section = req.Form["section"].ToString().Trim();
            var rating  = req.Form["rating"].ToString().Trim();
            if (rating is not ("up" or "down") || string.IsNullOrEmpty(section))
                return Results.Redirect($"/studio/s/{id}");

            await using var s = store.LightweightSession(
                Services.Incubator.FeasibilityAnalysisService.IncubatorTenant);
            var session = await s.LoadAsync<Services.Incubator.IncubatorSession>(id);
            if (session is null || session.OwnerUserId != auth.UserId!.Value)
                return Results.Redirect("/studio");
            session.SectionFeedback[section] = rating;
            s.Store(session);
            await s.SaveChangesAsync();
            return Results.Redirect($"/studio/s/{id}#section-{section}");
        }).DisableAntiforgery();

        // اختِيار باقَة — حاليّاً لا تَكامُل دَفع، يُسَجِّل النِيَّة فَقَط
        // (يُحَدِّث الـ Tier مُباشَرَةً في الـ MVP).
        app.MapPost("/studio/billing/select", async (
            string tier, HttpRequest req, IDocumentStore store,
            Services.Incubator.StudioAuth auth,
            ACommerce.Kit.Payments.IPaymentProvider payments,
            Services.Audit.AuditWriter audit) =>
        {
            auth.Load();
            if (!auth.IsAuthenticated) return Results.Redirect("/studio/auth");
            if (!Services.Incubator.TierCatalog.All.TryGetValue(tier, out var limits))
                return Results.Redirect("/studio/billing?err=tier");

            await using var s = store.LightweightSession(Services.Incubator.StudioAuth.Tenant);
            var u = await s.LoadAsync<Services.Incubator.StudioUser>(auth.UserId!.Value);
            if (u is null) return Results.Redirect("/studio/billing");

            // أَنشِئ اشتِراكاً مُتَكَرِّراً عَبر مُزَوِّد الدَّفع (mock الآن).
            // idempotency: نَفس المُستَخدِم + الباقَة + الشَّهر = نَفس النَّتيجَة.
            var idem = $"sub_{u.Id}_{tier}_{DateTime.UtcNow:yyyyMM}";
            var result = await payments.CreateSubscriptionAsync(new(
                CustomerId: u.Id.ToString(), PlanId: tier,
                MonthlyAmountSar: limits.MonthlyPriceSar,
                CustomerPhone: u.Phone), idem);

            if (!result.IsActive)
                return Results.Redirect($"/studio/billing?err=payment");

            u.Tier = tier;
            s.Store(u);
            await s.SaveChangesAsync();

            await audit.WriteAsync(Services.Audit.AuditWriter.PlatformScope,
                u.Id, u.FullName,
                "billing.subscription.create", "subscription", result.SubscriptionId,
                note: $"tier={tier} amount={limits.MonthlyPriceSar} period_end={result.CurrentPeriodEnd:yyyy-MM-dd}",
                ip: req.HttpContext.Connection.RemoteIpAddress?.ToString());

            return Results.Redirect("/studio/billing?selected=1");
        }).DisableAntiforgery();

        // تَصدير دِراسَة Excel — يَتَطَلَّب tier فيه AllowExport.
        app.MapGet("/studio/s/{id:guid}/export.xlsx", async (
            Guid id,
            Services.Incubator.StudioAuth auth,
            Services.Incubator.FeasibilityAnalysisService svc,
            Services.Incubator.StudioTierService tier,
            Services.Incubator.FeasibilityExcelExporter exporter) =>
        {
            auth.Load();
            if (!auth.IsAuthenticated) return Results.Redirect("/studio/auth");
            var session = await svc.LoadAsync(id);
            if (session is null || session.OwnerUserId != auth.UserId!.Value)
                return Results.NotFound();
            if (session.AnalysisJson is null)
                return Results.Redirect($"/studio/s/{id}");

            var (_, limits) = await tier.ReadWithLimitsAsync(auth.UserId!.Value);
            if (!limits.AllowExport)
                return Results.Redirect($"/studio/s/{id}?upgrade=refine");

            var bytes = exporter.Export(session);
            return Results.File(bytes,
                contentType: "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                fileDownloadName: $"feasibility-{id:N}.xlsx");
        });

        // إعادَة تَوليد قِسم واحِد مِن الدِراسَة (refine) بِناءً عَلى مُلاحَظَة.
        app.MapPost("/studio/s/{id:guid}/refine", async (
            Guid id, HttpRequest req, IServiceScopeFactory scopeFactory,
            Services.Incubator.StudioAuth auth,
            Services.Incubator.StudioTierService tier,
            Services.Incubator.FeasibilityAnalysisService svc) =>
        {
            auth.Load();
            if (!auth.IsAuthenticated) return Results.Redirect("/studio/auth");
            var section = req.Form["section"].ToString().Trim();
            var feedback = req.Form["feedback"].ToString().Trim();
            if (string.IsNullOrEmpty(section) || string.IsNullOrEmpty(feedback))
                return Results.Redirect($"/studio/s/{id}");

            var gate = await tier.CheckRefineAsync(auth.UserId!.Value);
            if (!gate.Allowed)
                return Results.Redirect($"/studio/s/{id}?upgrade=refine");
            await tier.RecordRefineAsync(auth.UserId!.Value);

            // شَغِّل في الخَلفِيَّة، لا نُعَلِّق الـ POST عَلى الـ LLM.
            _ = Task.Run(async () =>
            {
                using var scope = scopeFactory.CreateScope();
                var bg = scope.ServiceProvider
                    .GetRequiredService<Services.Incubator.FeasibilityAnalysisService>();
                try { await bg.RefineSectionAsync(id, section, feedback); }
                catch { /* تُعرَض الدِراسَة كَما هي عَلى الفَشَل */ }
            });
            return Results.Redirect($"/studio/s/{id}?refining={section}");
        }).DisableAntiforgery();

        // ═══ Cart + Checkout (نَمَط Order.V2) ══════════════════════════════
        // POST /{slug}/listings/{id}/cart/add — أَضِف إلى السَّلَّة.
        app.MapPost("/{slug}/listings/{id:guid}/cart/add",
            async (string slug, Guid id, HttpRequest req, IDocumentStore store) =>
        {
            var parsed = AuthHandlers.ParseToken(AuthSession.ResolveToken(req, slug));
            if (parsed is null) return Results.Redirect(Link(req, slug, $"login?returnUrl=/{slug}/listings/{id}"));
            var (userId, _, _) = parsed.Value;
            await using var s = store.LightweightSession(slug);
            var listing = await s.LoadAsync<Listing>(id);
            if (listing is null) return Results.Redirect(Link(req, slug, "explore"));
            int.TryParse(req.Form["qty"].ToString(), out var qty);
            if (qty <= 0) qty = 1;
            var cart = await s.LoadAsync<ACommerce.Kit.Cart.Cart>(userId)
                ?? new ACommerce.Kit.Cart.Cart { Id = userId };
            var existing = cart.Items.FirstOrDefault(i => i.ListingId == id);
            if (existing is not null) existing.Quantity += qty;
            else cart.Items.Add(new ACommerce.Kit.Cart.CartItem
            {
                ListingId = id, Title = listing.Title,
                UnitPriceSar = listing.Price, Quantity = qty
            });
            cart.UpdatedAt = DateTime.UtcNow;
            s.Store(cart);
            await s.SaveChangesAsync();
            return Results.Redirect(Link(req, slug, "cart"));
        }).DisableAntiforgery();

        // POST /{slug}/cart/{listingId}/qty — تَعديل كَمِّيَّة.
        app.MapPost("/{slug}/cart/{listingId:guid}/qty",
            async (string slug, Guid listingId, HttpRequest req, IDocumentStore store) =>
        {
            var parsed = AuthHandlers.ParseToken(AuthSession.ResolveToken(req, slug));
            if (parsed is null) return Results.Redirect(Link(req, slug, "login"));
            var (userId, _, _) = parsed.Value;
            int.TryParse(req.Form["qty"].ToString(), out var qty);
            await using var s = store.LightweightSession(slug);
            var cart = await s.LoadAsync<ACommerce.Kit.Cart.Cart>(userId);
            if (cart is null) return Results.Redirect(Link(req, slug, "cart"));
            var item = cart.Items.FirstOrDefault(i => i.ListingId == listingId);
            if (item is null) return Results.Redirect(Link(req, slug, "cart"));
            if (qty <= 0) cart.Items.Remove(item); else item.Quantity = qty;
            cart.UpdatedAt = DateTime.UtcNow;
            s.Store(cart);
            await s.SaveChangesAsync();
            return Results.Redirect(Link(req, slug, "cart"));
        }).DisableAntiforgery();

        app.MapPost("/{slug}/cart/clear", async (string slug, HttpRequest req, IDocumentStore store) =>
        {
            var parsed = AuthHandlers.ParseToken(AuthSession.ResolveToken(req, slug));
            if (parsed is null) return Results.Redirect(Link(req, slug, "login"));
            var (userId, _, _) = parsed.Value;
            await using var s = store.LightweightSession(slug);
            s.Delete<ACommerce.Kit.Cart.Cart>(userId);
            await s.SaveChangesAsync();
            return Results.Redirect(Link(req, slug, "cart"));
        }).DisableAntiforgery();

        // POST /{slug}/checkout/submit — تَحويل السَّلَّة إلى صَفقَة + إفراغ.
        // كُلّ بَند في السَّلَّة → Deal مُنفَصِل (لِأَنّ الـ Deal فيه الإعلان
        // وكُلّ بَند قَد يَكون مالِكُه مُختَلِف). يَدخُل Deals بِمَرحَلَة Booked
        // مُباشَرَةً (السَّلَّة تَعني أَنّ المُشتَري ثَبَّتَ النِيَّة).
        app.MapPost("/{slug}/checkout/submit",
            async (string slug, HttpRequest req, IDocumentStore store,
                   Services.Deals.DealsService deals,
                   ACommerce.Kit.Payments.IPaymentProvider payments) =>
        {
            var parsed = AuthHandlers.ParseToken(AuthSession.ResolveToken(req, slug));
            if (parsed is null) return Results.Redirect(Link(req, slug, "login"));
            var (userId, _, _) = parsed.Value;
            var name  = req.Form["name"].ToString().Trim();
            var phone = req.Form["phone"].ToString().Trim();
            var addr  = req.Form["addr"].ToString().Trim();
            var pay   = req.Form["pay"].ToString().Trim();

            await using var s = store.LightweightSession(slug);
            var cart = await s.LoadAsync<ACommerce.Kit.Cart.Cart>(userId);
            if (cart is null || cart.Items.Count == 0) return Results.Redirect(Link(req, slug, "cart"));

            var tenant = await s.LoadAsync<ACommerce.Kit.Tenants.Tenant>(slug);
            var pattern = PatternFromTenant(tenant);
            Guid firstDealId = Guid.Empty;
            foreach (var item in cart.Items)
            {
                var listing = await s.LoadAsync<Listing>(item.ListingId);
                if (listing is null) continue;

                var amount = item.UnitPriceSar * item.Quantity;
                var deal = await deals.StartAsync(slug, pattern,
                    initiatorId: userId, initiatorName: name,
                    listingId: item.ListingId, listingTitle: item.Title,
                    amountSar: amount,
                    attributes: new()
                    {
                        ["qty"] = item.Quantity.ToString(),
                        ["addr"] = addr,
                        ["phone"] = phone,
                        ["pay_method"] = pay
                    });
                if (firstDealId == Guid.Empty) firstDealId = deal.Id;
                if (listing.Attributes.TryGetValue("owner_id", out var oid))
                    await deals.AttachRefAsync(slug, deal.Id, "listing_owner", oid);

                // اِحجِز المَبلَغ عَبر مُزَوِّد الدَّفع (Authorize، لا
                // capture بَعد — يَتِمّ الـ capture عِندَ تَأكيد البائِع).
                // COD (دَفع عِندَ التَّسَلُّم) لا يَستَدعي المُزَوِّد.
                // idempotency-key بِـ dealId يَمنَع تَكرار الـ authorize
                // لَو أَعادَ المُستَخدِم submit بِنَفس النَّموذَج.
                if (pay != "cod")
                {
                    try
                    {
                        var pr = await payments.AuthorizeAsync(new(
                            AmountSar: amount,
                            Description: $"{tenant?.Name ?? slug} — {item.Title}",
                            CustomerId: userId.ToString(),
                            CustomerPhone: phone,
                            Metadata: new() { ["deal_id"] = deal.Id.ToString() }),
                            idempotencyKey: $"deal_{deal.Id}");
                        await deals.AttachRefAsync(slug, deal.Id, "payment_id",     pr.PaymentId);
                        await deals.AttachRefAsync(slug, deal.Id, "payment_status", pr.Status.ToString());
                    }
                    catch (Exception ex)
                    {
                        // فَشَل الدَّفع لا يَكسِر الـ POST بِأَكمَلِه — الصَّفقَة
                        // تَبقى في حالَة Booked لكِن مَعلَّمَة بِخَطَأ، والمالِك
                        // يَستَطيع رُؤيَتها يَدَويّاً.
                        await deals.AttachRefAsync(slug, deal.Id, "payment_error", ex.Message);
                    }
                }
            }

            s.Delete<ACommerce.Kit.Cart.Cart>(userId);
            await s.SaveChangesAsync();

            return Results.Redirect(Link(req, slug, firstDealId == Guid.Empty ? "deals" : $"deals/{firstDealId}"));
        }).DisableAntiforgery();

        // POST /{slug}/vendor/{vendorId}/chat — اِبدَأ مُحادَثَة مَع بائِع.
        app.MapPost("/{slug}/vendor/{vendorId:guid}/chat",
            async (string slug, Guid vendorId, HttpRequest req, IDocumentStore store) =>
        {
            var parsed = AuthHandlers.ParseToken(AuthSession.ResolveToken(req, slug));
            if (parsed is null) return Results.Redirect(Link(req, slug, "login"));
            var (userId, _, _) = parsed.Value;
            await using var s = store.LightweightSession(slug);
            var me = await s.LoadAsync<User>(userId);
            var vendor = await s.LoadAsync<User>(vendorId);
            // اِبحَث مُحادَثَة قائِمَة بَين الطَّرَفَين (بِأَيّ اتِّجاه).
            var existing = await s.Query<ACommerce.Kit.Chat.Conversation>()
                .Where(c => (c.OwnerId == userId && c.PartnerId == vendorId)
                         || (c.OwnerId == vendorId && c.PartnerId == userId))
                .FirstOrDefaultAsync();
            if (existing is not null)
                return Results.Redirect(Link(req, slug, $"chat/{existing.Id}"));
            var convo = new ACommerce.Kit.Chat.Conversation
            {
                Id = Guid.NewGuid(),
                OwnerId = userId, OwnerName = me?.FullName ?? "أَنا",
                PartnerId = vendorId, PartnerName = vendor?.FullName ?? "البائِع",
                CreatedAt = DateTime.UtcNow, LastAt = DateTime.UtcNow
            };
            s.Store(convo);
            await s.SaveChangesAsync();
            return Results.Redirect(Link(req, slug, $"chat/{convo.Id}"));
        }).DisableAntiforgery();

        // ─── Studio Reviews (تَقييم مُتَبادَل لِصَفقَة مُكتَمِلَة) ─────────
        app.MapPost("/studio/apps/{slug}/deals/{id:guid}/review",
            async (string slug, Guid id, HttpRequest req, IDocumentStore store,
                   Services.Incubator.StudioAuth auth,
                   Services.Deals.DealsService deals,
                   ACommerce.Kit.Reviews.ReviewsService reviews) =>
        {
            if (!await StudioOwnsAsync(store, auth, slug)) return Results.Redirect("/studio");
            if (!int.TryParse(req.Form["rating"].ToString(), out var rating)) rating = 5;
            var body = req.Form["body"].ToString().Trim();
            var targetIsCounterparty = req.Form["target"].ToString() == "counterparty";
            var deal = await deals.LoadAsync(slug, id);
            if (deal is null) return Results.Redirect($"/studio/apps/{slug}/deals");

            var (target, targetName, author, authorName) = targetIsCounterparty
                ? (deal.CounterpartyId ?? Guid.Empty, deal.CounterpartyName ?? "—",
                   deal.InitiatorId, deal.InitiatorName)
                : (deal.InitiatorId, deal.InitiatorName,
                   deal.CounterpartyId ?? Guid.Empty, deal.CounterpartyName ?? "—");
            if (target == Guid.Empty)
                return Results.Redirect($"/studio/apps/{slug}/deals/{id}?err=no-target");

            var r = await reviews.SubmitAsync(slug, target, targetName, author, authorName,
                rating, body, dealId: id, dealPattern: deal.Pattern);
            await deals.AttachRefAsync(slug, id, $"review_{(targetIsCounterparty ? "cp" : "init")}", r.Id.ToString("N"));
            return Results.Redirect($"/studio/apps/{slug}/deals/{id}");
        }).DisableAntiforgery();

        // ─── Studio Listings moderation (إخفاء/إظهار/حَذف إشرافيّ) ───────
        app.MapPost("/studio/apps/{slug}/listings/{id:guid}/moderate",
            async (string slug, Guid id, HttpRequest req, IDocumentStore store,
                   Services.Incubator.StudioAuth auth,
                   Services.Audit.AuditWriter audit) =>
        {
            if (!await StudioOwnsAsync(store, auth, slug)) return Results.Redirect("/studio");
            var action = req.Form["action"].ToString().Trim();   // hide | unhide | delete
            var reason = req.Form["reason"].ToString().Trim();
            await using var s = store.LightweightSession(slug);
            var modId = auth.UserId!.Value;
            switch (action)
            {
                case "hide":
                    s.Events.Append(id, new ACommerce.Kit.Listings.ListingModerated(
                        id, Hidden: true, Reason: string.IsNullOrEmpty(reason) ? "إشرافيّ" : reason,
                        ModeratorId: modId, At: DateTime.UtcNow));
                    break;
                case "unhide":
                    s.Events.Append(id, new ACommerce.Kit.Listings.ListingModerated(
                        id, Hidden: false, Reason: "", ModeratorId: modId, At: DateTime.UtcNow));
                    break;
                case "delete":
                    s.Events.Append(id, new ACommerce.Kit.Listings.ListingDeleted(id, DateTime.UtcNow));
                    break;
            }
            await s.SaveChangesAsync();
            await audit.WriteAsync(slug, modId, "مالِك التَّطبيق",
                $"listing.{action}", "listing", id.ToString(), note: reason,
                ip: req.HttpContext.Connection.RemoteIpAddress?.ToString());
            return Results.Redirect($"/studio/apps/{slug}/listings");
        }).DisableAntiforgery();

        // ─── Studio Tickets (دَعم فَنّيّ — رَدّ + إغلاق) ──────────────────
        app.MapPost("/studio/apps/{slug}/tickets/{id:guid}/reply",
            async (string slug, Guid id, HttpRequest req, IDocumentStore store,
                   Services.Incubator.StudioAuth auth,
                   Services.Audit.AuditWriter audit) =>
        {
            if (!await StudioOwnsAsync(store, auth, slug)) return Results.Redirect("/studio");
            var body = req.Form["body"].ToString().Trim();
            if (string.IsNullOrEmpty(body))
                return Results.Redirect($"/studio/apps/{slug}/tickets/{id}?err=empty");
            await using var s = store.LightweightSession(slug);
            var evt = new ACommerce.Kit.Support.TicketReplied(
                TicketId: id, ReplyId: Guid.NewGuid(),
                AuthorName: "دَعم التَّطبيق",
                FromStaff: true, Body: body, At: DateTime.UtcNow);
            s.Events.Append(id, evt);
            await s.SaveChangesAsync();
            await audit.WriteAsync(slug, auth.UserId, "مالِك التَّطبيق",
                "ticket.reply", "ticket", id.ToString(),
                note: body.Length > 80 ? body[..80] + "…" : body,
                ip: req.HttpContext.Connection.RemoteIpAddress?.ToString());
            return Results.Redirect($"/studio/apps/{slug}/tickets/{id}?replied=1");
        }).DisableAntiforgery();

        app.MapPost("/studio/apps/{slug}/tickets/{id:guid}/close",
            async (string slug, Guid id, HttpRequest req, IDocumentStore store,
                   Services.Incubator.StudioAuth auth,
                   Services.Audit.AuditWriter audit) =>
        {
            if (!await StudioOwnsAsync(store, auth, slug)) return Results.Redirect("/studio");
            await using var s = store.LightweightSession(slug);
            s.Events.Append(id, new ACommerce.Kit.Support.TicketClosed(id, DateTime.UtcNow));
            await s.SaveChangesAsync();
            await audit.WriteAsync(slug, auth.UserId, "مالِك التَّطبيق",
                "ticket.close", "ticket", id.ToString(),
                ip: req.HttpContext.Connection.RemoteIpAddress?.ToString());
            return Results.Redirect($"/studio/apps/{slug}/tickets/{id}?closed=1");
        }).DisableAntiforgery();

        // ═══ النَّمَط العامّ: عُروض → صَفقات بَين عِدَّة أَدوار (عميل) ═══
        // عميل يُقَدِّم عَرضاً على إعلان → يُنشِئ Deal(Offered).
        app.MapPost("/{slug}/listings/{id:guid}/deal",
            async (string slug, Guid id, HttpRequest req, IDocumentStore store,
                   Services.Deals.DealsService deals) =>
        {
            var parsed = AuthHandlers.ParseToken(AuthSession.ResolveToken(req, slug));
            if (parsed is null) return Results.Redirect(Link(req, slug, $"login?returnUrl=/{slug}/listings/{id}"));
            var (userId, tenantSlug, _) = parsed.Value;
            if (tenantSlug != slug) return Results.Redirect(Link(req, slug, "login"));
            var userName = AuthSession.ResolveUserName(req, slug) ?? "عميل";

            decimal.TryParse(req.Form["amount"].ToString(), out var amount);
            var note = req.Form["note"].ToString().Trim();

            await using var qs = store.QuerySession(slug);
            var listing = await qs.LoadAsync<Listing>(id);
            if (listing is null || listing.IsDeleted) return Results.Redirect(Link(req, slug, "explore"));
            if (listing.Attributes.TryGetValue("owner_id", out var oid) && oid == userId.ToString())
                return Results.Redirect(Link(req, slug, $"listings/{id}?err=self"));

            var tenantDoc = await qs.LoadAsync<ACommerce.Kit.Tenants.Tenant>(slug);
            var pattern = PatternFromTenant(tenantDoc);
            var deal = await deals.StartAsync(slug, pattern,
                initiatorId: userId, initiatorName: userName,
                listingId: id, listingTitle: listing.Title,
                amountSar: amount > 0 ? amount : listing.Price,
                attributes: string.IsNullOrEmpty(note) ? null : new() { ["note"] = note });

            if (oid is not null && Guid.TryParse(oid, out var ownerGuid))
                await deals.AttachRefAsync(slug, deal.Id, "listing_owner", ownerGuid.ToString());

            return Results.Redirect(Link(req, slug, $"deals/{deal.Id}"));
        }).DisableAntiforgery();

        // مالِك الإعلان يَقبَل عَرضاً → Booked + يُصبِح الطَّرَف الثاني.
        app.MapPost("/{slug}/deals/{id:guid}/accept",
            async (string slug, Guid id, HttpRequest req, IDocumentStore store,
                   Services.Deals.DealsService deals) =>
        {
            var parsed = AuthHandlers.ParseToken(AuthSession.ResolveToken(req, slug));
            if (parsed is null) return Results.Redirect(Link(req, slug, "login"));
            var (userId, _, _) = parsed.Value;
            var userName = AuthSession.ResolveUserName(req, slug) ?? "المالِك";
            var deal = await deals.LoadAsync(slug, id);
            if (deal is null) return Results.Redirect(Link(req, slug, "deals"));
            await deals.AssignCounterpartyAsync(slug, id, userId, userName);
            await deals.AdvanceAsync(slug, id, deal.InitiatorId, userName, "قُبِلَ العَرض");
            return Results.Redirect(Link(req, slug, $"deals/{id}"));
        }).DisableAntiforgery();

        // أَيّ طَرَف يُحَرِّك المَرحَلَة التالِيَة بِحَسَب دَورِه.
        app.MapPost("/{slug}/deals/{id:guid}/advance",
            async (string slug, Guid id, HttpRequest req, IDocumentStore store,
                   Services.Deals.DealsService deals) =>
        {
            var parsed = AuthHandlers.ParseToken(AuthSession.ResolveToken(req, slug));
            if (parsed is null) return Results.Redirect(Link(req, slug, "login"));
            var (userId, _, _) = parsed.Value;
            var userName = AuthSession.ResolveUserName(req, slug) ?? "مُستَخدِم";
            var note = req.Form["note"].ToString().Trim();
            await deals.AdvanceAsync(slug, id, userId, userName, note);
            return Results.Redirect(Link(req, slug, $"deals/{id}"));
        }).DisableAntiforgery();

        app.MapPost("/{slug}/deals/{id:guid}/cancel",
            async (string slug, Guid id, HttpRequest req, IDocumentStore store,
                   Services.Deals.DealsService deals) =>
        {
            var parsed = AuthHandlers.ParseToken(AuthSession.ResolveToken(req, slug));
            if (parsed is null) return Results.Redirect(Link(req, slug, "login"));
            var (userId, _, _) = parsed.Value;
            var userName = AuthSession.ResolveUserName(req, slug) ?? "مُستَخدِم";
            var reason = req.Form["reason"].ToString().Trim();
            await deals.CancelAsync(slug, id, userId, userName, string.IsNullOrEmpty(reason) ? "إلغاء" : reason);
            return Results.Redirect(Link(req, slug, $"deals/{id}"));
        }).DisableAntiforgery();

        // تَقييم الطَّرَف الآخَر بَعد اكتِمال الصَّفقَة.
        app.MapPost("/{slug}/deals/{id:guid}/review",
            async (string slug, Guid id, HttpRequest req, IDocumentStore store,
                   Services.Deals.DealsService deals,
                   ACommerce.Kit.Reviews.ReviewsService reviews) =>
        {
            var parsed = AuthHandlers.ParseToken(AuthSession.ResolveToken(req, slug));
            if (parsed is null) return Results.Redirect(Link(req, slug, "login"));
            var (userId, _, _) = parsed.Value;
            var userName = AuthSession.ResolveUserName(req, slug) ?? "مُستَخدِم";
            if (!int.TryParse(req.Form["rating"].ToString(), out var rating)) rating = 5;
            var body = req.Form["body"].ToString().Trim();

            var deal = await deals.LoadAsync(slug, id);
            if (deal is null) return Results.Redirect(Link(req, slug, "deals"));
            var (target, targetName) = deal.InitiatorId == userId
                ? (deal.CounterpartyId ?? Guid.Empty, deal.CounterpartyName ?? "—")
                : (deal.InitiatorId, deal.InitiatorName);
            if (target != Guid.Empty && !await reviews.HasReviewedAsync(slug, userId, id))
                await reviews.SubmitAsync(slug, target, targetName, userId, userName,
                    rating, body, dealId: id, dealPattern: deal.Pattern);
            return Results.Redirect(Link(req, slug, $"deals/{id}"));
        }).DisableAntiforgery();

        // ─── Admin: تَعليق/تَفعيل مُستَأجِر (إجراء مَنصَّة) ───────────────
        app.MapPost("/admin/tenants/{slug}/suspend",
            async (string slug, HttpRequest req, IDocumentStore store,
                   Services.Incubator.StudioAuth auth, Services.Audit.AuditWriter audit) =>
        {
            // كانَ القَرار مَكتوباً هُنا حَرفاً بِحَرف — وَهُوَ النُّسخَة
            // الثانِيَة الَّتي انجَرَفَت عَنها بَقِيَّة نِقاط المَنصَّة.
            // صارَ في Services.PlatformAdminGuard، وَالمَجهول يُرَدّ الآن
            // بِـ 403 كَبَقِيَّة نِقاط الكِتابَة لا بِتَحويل إلى صَفحَة دُخول
            // تَضيع مَعَها حُقول الـ form أَصلاً.
            var decision = await Services.PlatformAdminGuard.EvaluateAsync(store, auth);
            if (!decision.Allowed) return Forbidden();
            var u = decision.User!;

            var reason = req.Form["reason"].ToString().Trim();
            var action = req.Form["action"].ToString().Trim();   // suspend | reactivate
            await using var s = store.LightweightSession();
            var t = await s.LoadAsync<ACommerce.Kit.Tenants.Tenant>(slug);
            if (t is null) return Results.Redirect("/admin");
            t.IsSuspended = action == "suspend";
            t.SuspensionReason = t.IsSuspended ? (string.IsNullOrEmpty(reason) ? "تَعليق إداريّ" : reason) : null;
            s.Store(t);
            await s.SaveChangesAsync();
            await audit.WriteAsync(Services.Audit.AuditWriter.PlatformScope,
                u.Id, u.FullName,
                t.IsSuspended ? "tenant.suspend" : "tenant.reactivate",
                "tenant", slug, note: reason,
                ip: req.HttpContext.Connection.RemoteIpAddress?.ToString());
            return Results.Redirect("/admin");
        }).DisableAntiforgery();

        // ─── Studio Deals (تَدَفُّق العَمَلِيّات: المالِك يَتَدَخَّل أَو يُنفِّذ) ─
        // كُلّ الإجراءات تَفحَص مِلكِيَّة المُستَأجِر قَبل العَمَل.
        app.MapPost("/studio/apps/{slug}/deals/seed",
            async (string slug, HttpRequest req, IDocumentStore store,
                   Services.Incubator.StudioAuth auth, Services.Deals.DealsService deals) =>
        {
            if (!await StudioOwnsAsync(store, auth, slug)) return Results.Redirect("/studio");
            var pattern = req.Form["pattern"].ToString().Trim();
            if (string.IsNullOrEmpty(pattern)) pattern = "marketplace";
            // أَنشِئ Deal تَجريبيَّة لِعَرض التَّدَفُّق في الواجِهَة.
            var owner = auth.UserId!.Value;
            await deals.StartAsync(slug, pattern,
                initiatorId: owner, initiatorName: "صاحِب الفِكرَة",
                listingId: null, listingTitle: $"عَمَلِيَّة تَجريبيَّة — {pattern}",
                amountSar: 250m);
            return Results.Redirect($"/studio/apps/{slug}/deals");
        }).DisableAntiforgery();

        app.MapPost("/studio/apps/{slug}/deals/{id:guid}/advance",
            async (string slug, Guid id, HttpRequest req, IDocumentStore store,
                   Services.Incubator.StudioAuth auth, Services.Deals.DealsService deals,
                   Services.Audit.AuditWriter audit) =>
        {
            if (!await StudioOwnsAsync(store, auth, slug)) return Results.Redirect("/studio");
            var note = req.Form["note"].ToString().Trim();
            var deal = await deals.LoadAsync(slug, id);
            if (deal is null) return Results.Redirect($"/studio/apps/{slug}/deals");

            if (deal.CounterpartyId is null)
                await deals.AssignCounterpartyAsync(slug, id,
                    auth.UserId!.Value, "مالِك التَّطبيق");

            var actorId = deal.Stage == Services.Deals.DealStage.Offered
                ? deal.InitiatorId
                : (deal.CounterpartyId ?? auth.UserId!.Value);
            var result = await deals.AdvanceAsync(slug, id, actorId, "مالِك التَّطبيق (إداريّ)", note);
            if (result.Ok && result.Deal is not null)
                await audit.WriteAsync(slug, auth.UserId, "مالِك التَّطبيق",
                    "deal.advance", "deal", id.ToString(),
                    note: $"→ {result.Deal.Stage}" + (string.IsNullOrEmpty(note) ? "" : $" · {note}"),
                    ip: req.HttpContext.Connection.RemoteIpAddress?.ToString());
            return Results.Redirect($"/studio/apps/{slug}/deals/{id}");
        }).DisableAntiforgery();

        app.MapPost("/studio/apps/{slug}/deals/{id:guid}/cancel",
            async (string slug, Guid id, HttpRequest req, IDocumentStore store,
                   Services.Incubator.StudioAuth auth, Services.Deals.DealsService deals,
                   Services.Audit.AuditWriter audit) =>
        {
            if (!await StudioOwnsAsync(store, auth, slug)) return Results.Redirect("/studio");
            var reason = req.Form["reason"].ToString().Trim();
            if (string.IsNullOrEmpty(reason)) reason = "إلغاء إداريّ";
            await deals.CancelAsync(slug, id, auth.UserId!.Value, "مالِك التَّطبيق", reason);
            await audit.WriteAsync(slug, auth.UserId, "مالِك التَّطبيق",
                "deal.cancel", "deal", id.ToString(), note: reason,
                ip: req.HttpContext.Connection.RemoteIpAddress?.ToString());
            return Results.Redirect($"/studio/apps/{slug}/deals/{id}");
        }).DisableAntiforgery();

        app.MapPost("/studio/apps/{slug}/deals/{id:guid}/dispute",
            async (string slug, Guid id, HttpRequest req, IDocumentStore store,
                   Services.Incubator.StudioAuth auth, Services.Deals.DealsService deals,
                   Services.Audit.AuditWriter audit) =>
        {
            if (!await StudioOwnsAsync(store, auth, slug)) return Results.Redirect("/studio");
            var reason = req.Form["reason"].ToString().Trim();
            if (string.IsNullOrEmpty(reason)) reason = "نِزاع";
            await deals.DisputeAsync(slug, id, auth.UserId!.Value, "مالِك التَّطبيق", reason);
            await audit.WriteAsync(slug, auth.UserId, "مالِك التَّطبيق",
                "deal.dispute", "deal", id.ToString(), note: reason,
                ip: req.HttpContext.Connection.RemoteIpAddress?.ToString());
            return Results.Redirect($"/studio/apps/{slug}/deals/{id}");
        }).DisableAntiforgery();

        // ─── Studio per-app config saves (داخِل studio، redirect مَحَلّيّ) ─
        // مُحَقِّق المِلكِيَّة: المُستَخدِم يَجِب أَن يَكون مالِك الـ tenant.
        async Task<bool> StudioOwnsAsync(IDocumentStore docStore, Services.Incubator.StudioAuth auth, string slug)
        {
            auth.Load();
            if (!auth.IsAuthenticated) return false;
            await using var qs = docStore.QuerySession();
            var t = await qs.LoadAsync<ACommerce.Kit.Tenants.Tenant>(slug);
            return t is not null && t.OwnerUserId == auth.UserId!.Value;
        }

        // ─── حارِس ادمن المَتجَر — لِكُلّ /admin/tenants/{slug}/* ─────────
        // القَرار سَكَنَ في Services.TenantAdminGuard لِيَقرَأَه طَرَفا
        // القِراءَة وَالكِتابَة مَعاً. كانَ دالَّةً مَحَلِّيَّة هُنا لا تَراها
        // صَفَحات Razor، فَحُرِسَت نِقاط الـ POST وَبَقِيَت الصَفَحات
        // تُصَيَّر كامِلَةً لِأَيّ طَلَب مَجهول. نِقاط الـ POST أَعلاه
        // تَقصِد التَّعريف الواحِد مُباشَرَةً — بِلا اسم وَسيط هُنا يُغري
        // بِنُسخَة ثانِيَة تَنجَرِف. وَأَيّ endpoint جَديد تَحتَ هذا المَسار
        // يَبدَأ بِـ Services.TenantAdminGuard.CanAdministerAsync.

        // مُخرَج 403 مُوَحَّد بَدَلاً مِن تَكرارِه في كُلّ endpoint.
        static IResult Forbidden() => Results.StatusCode(StatusCodes.Status403Forbidden);

        // سَطر audit واحِد لِكُلّ admin POST. الفاعِل قَد يَكون مالِك Studio
        // أَو مُستَخدِم داخِل المَتجَر بِـ tenant.manage — نَفس الشَّيء في الـ
        // log (إجراء إداريّ). الـ before/after للـ form يُجَمَّع كَ key=value
        // بَسيط — يَكفي لاحِقاً لِفَهم مَن غَيَّر ماذا.
        async Task LogTenantConfigChangeAsync(
            Services.Audit.AuditWriter audit, HttpRequest req, string slug,
            Services.Incubator.StudioAuth studioAuth, string action)
        {
            var (actorId, actorName) = await ResolveActorAsync(req, slug, studioAuth);
            var formSnapshot = string.Join("; ", req.Form
                .Where(kv => !kv.Key.StartsWith("password", StringComparison.OrdinalIgnoreCase))
                .Select(kv => $"{kv.Key}={Truncate(kv.Value.ToString(), 60)}"));
            await audit.WriteAsync(slug, actorId, actorName,
                action, "Tenant", slug,
                note: null, ip: req.HttpContext.Connection.RemoteIpAddress?.ToString(),
                userAgent: req.Headers["User-Agent"].ToString(),
                after: Truncate(formSnapshot, 2000));
        }

        async Task<(Guid? Id, string Name)> ResolveActorAsync(
            HttpRequest req, string slug, Services.Incubator.StudioAuth studioAuth)
        {
            studioAuth.Load();
            if (studioAuth.IsAuthenticated)
                return (studioAuth.UserId, studioAuth.UserName ?? "studio");
            var parsed = AuthHandlers.ParseToken(AuthSession.ResolveToken(req, slug));
            if (parsed is not null) return (parsed.Value.UserId, "tenant_admin");
            return (null, "anonymous");
        }

        static string Truncate(string s, int max)
            => s.Length <= max ? s : s[..max] + "…";

        // نَفس خِدمَة /admin — والفَرقُ الباقي ثَلاثَة: أَيّ حارِسٍ
        // يُسأَل (مالِك التَطبيق لا مُشرِف المَتجَر)، وإلى أَين يَعود
        // المُتَصَفِّح، وأَنّ الرَفض هُنا ‏302 لا ‏403. والتَدقيق لَم
        // يَعُد فَرقاً: يُكتَب في المَسارَين.
        app.MapPost("/studio/apps/{slug}/branding/save", async (
            string slug, HttpRequest req, IDocumentStore store,
            Services.Incubator.StudioAuth auth,
            Services.Audit.AuditWriter audit) =>
        {
            if (!await StudioOwnsAsync(store, auth, slug)) return Results.Redirect("/studio");
            await LogTenantConfigChangeAsync(audit, req, slug, auth, BrandingSaveService.AuditAction);

            await using var s = store.LightweightSession();
            var result = await BrandingSaveService.SaveAsync(s, slug, TenantConfigSurface.ReadBranding(req));
            if (result.Ok) await s.SaveChangesAsync();

            return TenantConfigSurface.Outcome(result,
                $"/studio/apps/{slug}/branding?saved=1", $"/studio/apps/{slug}/branding", "/studio");
        }).DisableAntiforgery();

        app.MapPost("/studio/apps/{slug}/categories/save", async (
            string slug, HttpRequest req, IDocumentStore store,
            Services.Incubator.StudioAuth auth,
            Services.Audit.AuditWriter audit) =>
        {
            if (!await StudioOwnsAsync(store, auth, slug)) return Results.Redirect("/studio");
            await LogTenantConfigChangeAsync(audit, req, slug, auth, CategoriesSaveService.AuditAction);

            await using var s = store.LightweightSession();
            var result = await CategoriesSaveService.SaveAsync(s, slug, TenantConfigSurface.ReadCategories(req));
            if (result.Ok) await s.SaveChangesAsync();

            return TenantConfigSurface.Outcome(result,
                $"/studio/apps/{slug}/categories?saved=1", $"/studio/apps/{slug}/categories", "/studio");
        }).DisableAntiforgery();

        app.MapPost("/studio/apps/{slug}/roles/save", async (
            string slug, HttpRequest req, IDocumentStore store,
            Services.Incubator.StudioAuth auth,
            Services.Audit.AuditWriter audit) =>
        {
            if (!await StudioOwnsAsync(store, auth, slug)) return Results.Redirect("/studio");
            await LogTenantConfigChangeAsync(audit, req, slug, auth, RolesSaveService.AuditAction);

            await using var s = store.LightweightSession();
            var result = await RolesSaveService.SaveAsync(s, slug, TenantConfigSurface.ReadRoles(req));
            if (result.Ok) await s.SaveChangesAsync();

            return TenantConfigSurface.Outcome(result,
                $"/studio/apps/{slug}/roles?saved=1", $"/studio/apps/{slug}/roles", "/studio");
        }).DisableAntiforgery();

        app.MapPost("/studio/apps/{slug}/regions/save", async (
            string slug, HttpRequest req, IDocumentStore store,
            Services.Incubator.StudioAuth auth) =>
        {
            if (!await StudioOwnsAsync(store, auth, slug)) return Results.Redirect("/studio");
            var raw = req.Form["regions"].ToString();
            if (string.IsNullOrWhiteSpace(raw))
                return Results.Redirect($"/studio/apps/{slug}/regions?err=empty");

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
                        return Results.Redirect($"/studio/apps/{slug}/regions?err=format");
                    var districts = parts[1]
                        .Split(new[] { '،', ',' },
                            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Where(d => !string.IsNullOrEmpty(d)).ToList();
                    cities.Add((cityName, districts));
                }
                else cities.Add((l, new List<string>()));
            }
            if (cities.Count == 0) return Results.Redirect($"/studio/apps/{slug}/regions?err=empty");

            await using var s = store.LightweightSession(slug);
            var existing = await s.Query<ImportedRecord>()
                .Where(r => r.Table == "DiscoveryRegions").ToListAsync();
            foreach (var r in existing) s.Delete(r);

            var now = DateTime.UtcNow;
            var cityOrder = 0;
            foreach (var (cityName, districts) in cities)
            {
                var cityId = Guid.NewGuid();
                s.Store(new ImportedRecord
                {
                    Id = cityId.ToString(), Table = "DiscoveryRegions",
                    Data = new Dictionary<string, object?>
                    {
                        ["Id"] = cityId, ["Name"] = cityName, ["Level"] = 1,
                        ["ParentId"] = null, ["SortOrder"] = cityOrder++,
                    }, ImportedAt = now
                });
                var distOrder = 0;
                foreach (var d in districts)
                {
                    var dId = Guid.NewGuid();
                    s.Store(new ImportedRecord
                    {
                        Id = dId.ToString(), Table = "DiscoveryRegions",
                        Data = new Dictionary<string, object?>
                        {
                            ["Id"] = dId, ["Name"] = d, ["Level"] = 2,
                            ["ParentId"] = cityId, ["SortOrder"] = distOrder++,
                        }, ImportedAt = now
                    });
                }
            }
            await s.SaveChangesAsync();
            return Results.Redirect($"/studio/apps/{slug}/regions?saved=1");
        }).DisableAntiforgery();

        // ─── Studio: save PWA apps (per-role name + icon) ───────────────
        // نَفس مَنطِق /admin/tenants/{slug}/pwa/save لَكِن داخِل واجِهَة الـ
        // Studio بِحارِس المِلكِيَّة، والتَّوجيهات إلى مَسارات /studio/apps/.
        app.MapPost("/studio/apps/{slug}/pwa/save", async (
            string slug, HttpRequest req, IDocumentStore store,
            Services.Incubator.StudioAuth auth) =>
        {
            if (!await StudioOwnsAsync(store, auth, slug)) return Results.Redirect("/studio");
            await using var s = store.LightweightSession();
            var t = await s.LoadAsync<ACommerce.Kit.Tenants.Tenant>(slug);
            if (t is null) return Results.Redirect("/studio");

            const long maxBytes = 256 * 1024;
            var allowed = new[] { "image/png", "image/svg+xml", "image/webp" };

            foreach (var r in t.Roles)
            {
                var nameInput = req.Form[$"name_{r.Slug}"].ToString().Trim();
                r.PwaName = string.IsNullOrEmpty(nameInput) ? null : nameInput;

                if (req.Form[$"clear_{r.Slug}"].ToString() == "1")
                    r.PwaIconDataUrl = null;

                var file = req.Form.Files[$"icon_{r.Slug}"];
                if (file is { Length: > 0 })
                {
                    if (file.Length > maxBytes)
                        return Results.Redirect($"/studio/apps/{slug}/pwa?err=icon_too_large");
                    var ct = file.ContentType.ToLowerInvariant();
                    if (!allowed.Contains(ct))
                        return Results.Redirect($"/studio/apps/{slug}/pwa?err=icon_bad_type");
                    using var ms = new MemoryStream();
                    await file.CopyToAsync(ms);
                    var b64 = Convert.ToBase64String(ms.ToArray());
                    r.PwaIconDataUrl = $"data:{ct};base64,{b64}";
                }
            }

            s.Store(t);
            await s.SaveChangesAsync();
            return Results.Redirect($"/studio/apps/{slug}/pwa?saved=1");
        }).DisableAntiforgery();

        // ─── Studio: save attribute definitions for a scope ─────────────
        // نَفس مَنطِق /admin/tenants/{slug}/attributes/save لَكِن داخِل واجِهَة
        // الـ Studio بِحارِس المِلكِيَّة، والتَّوجيهات إلى مَسارات /studio/apps/.
        app.MapPost("/studio/apps/{slug}/attributes/save", async (
            string slug, HttpRequest req, IDocumentStore store,
            Services.Incubator.StudioAuth auth) =>
        {
            if (!await StudioOwnsAsync(store, auth, slug)) return Results.Redirect("/studio");
            var scopeStr = req.Form["scope"].ToString().Trim();
            var defsRaw  = req.Form["defs"].ToString();

            if (!Guid.TryParse(scopeStr, out var scopeId))
                return Results.Redirect($"/studio/apps/{slug}/attributes?err=no_scope");

            string Back(string err) =>
                $"/studio/apps/{slug}/attributes?scope={scopeId}&err={err}";

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
            return Results.Redirect($"/studio/apps/{slug}/attributes?scope={scopeId}&saved=1");
        }).DisableAntiforgery();

        // بِناء Tenant فِعليّ مِن جَلسَة تَحليل (الجِسر بَين الفِكرَة والتَّطبيق).
        app.MapPost("/studio/s/{id:guid}/build", async (
            Guid id, HttpRequest req, HttpContext http,
            Services.Incubator.FeasibilityAnalysisService incubator,
            Services.Incubator.TenantFromAnalysisFactory factory,
            Services.Incubator.StudioTierService tier,
            Services.Incubator.StudioAuth auth) =>
        {
            auth.Load();
            if (!auth.IsAuthenticated) return Results.Redirect("/studio/auth");
            var ownerId = auth.UserId!.Value;

            var session = await incubator.LoadAsync(id);
            if (session is null || session.OwnerUserId != ownerId)
                return Results.Redirect("/studio");
            if (session.Status != Services.Incubator.IncubatorStatus.Completed)
                return Results.Redirect($"/studio/s/{id}");

            var gate = await tier.CheckBuildAsync(ownerId);
            if (!gate.Allowed)
                return Results.Redirect($"/studio/s/{id}?upgrade=build");

            var slug    = req.Form["slug"].ToString().Trim().ToLowerInvariant();
            var name    = req.Form["name"].ToString().Trim();
            var color   = req.Form["color"].ToString().Trim();
            var tagLine = req.Form["tagline"].ToString().Trim();
            var city    = req.Form["city"].ToString().Trim();

            var err = await factory.ValidateSlugAsync(slug);
            if (err is not null)
                return Results.Redirect($"/studio/s/{id}?build_err={Uri.EscapeDataString(err)}");
            if (string.IsNullOrEmpty(name))
                return Results.Redirect($"/studio/s/{id}?build_err=name_required");
            if (!System.Text.RegularExpressions.Regex.IsMatch(color, "^#[0-9A-Fa-f]{6}$"))
                return Results.Redirect($"/studio/s/{id}?build_err=color_invalid");

            var sector = session.Answers.TryGetValue("sector", out var sec) ? sec : "";
            await factory.CreateAsync(slug, name, color, tagLine, city,
                session.SuggestedPattern, sector, ownerId, id);
            await tier.RecordStoreBuiltAsync(ownerId);
            return Results.Redirect($"/studio/apps/{slug}?built=1");
        }).DisableAntiforgery();

        // إعادَة تَحليل مِن داخِل لوحَة العميل (تُبقيه في مَساحَة /studio).
        //
        // كانَت هذه النُقطَة <b>بِلا حارِس واحِد</b> بَينَ أُختَيها
        // المَحروسَتَين (‏/refine و/build) — مَجهول يَقلِب حالَة أَيّ دِراسَة
        // إلى Analyzing ويُطلِق تَحليل LLM في الخَلفِيَّة بِتَكلِفَتِه.
        // القياس الحَيّ فَضَحَها: أُختاها تَرُدّان 302 إلى /studio/auth
        // وهي تَرُدّ 302 إلى صَفحَة النَجاح. والحارِس هُنا هُوَ
        // <b>قَرارُهُما نَفسُه حَرفِيّاً</b> — جَلسَة studio صالِحَة، ثُمَّ
        // مِلكِيَّة الدِراسَة — لا حارِسٌ ثانٍ مَكتوب بِيَدٍ ثانِيَة.
        app.MapPost("/studio/s/{id:guid}/analyze", async (
            Guid id, IServiceScopeFactory scopeFactory,
            Services.Incubator.StudioAuth auth,
            Services.Incubator.FeasibilityAnalysisService svc) =>
        {
            auth.Load();
            if (!auth.IsAuthenticated) return Results.Redirect("/studio/auth");

            var session = await svc.LoadAsync(id);
            if (session is null || session.OwnerUserId != auth.UserId!.Value)
                return Results.Redirect("/studio");

            await svc.MarkAnalyzingAsync(id);
            _ = Task.Run(async () =>
            {
                using var scope = scopeFactory.CreateScope();
                var bg = scope.ServiceProvider
                    .GetRequiredService<Services.Incubator.FeasibilityAnalysisService>();
                try { await bg.RunAnalysisAsync(id); } catch { }
            });
            return Results.Redirect($"/studio/s/{id}");
        }).DisableAntiforgery();

        // ─── Incubator — طبقة التحليل الاستثماري ─────────────────────────
        // نُسخَة الـ admin مِن الحاضِنَة: أَداة مَنصَّة لا أَداة عُمَلاء
        // (لِلعُمَلاء /studio/…)، فَهِيَ خَلف بَوّابَة مُشرِف المَنصَّة.
        // المالِك يَبقى Guid.Empty — هذه جَلسات المَنصَّة نَفسِها، مُنفَصِلَة
        // عَن جَلسات المُستَخدِمين. الاكتشاف SSR (POST لكل إجابة)، والتحليل
        // يُطلَق في الخلفية وصفحة الدراسة تَستطلِع حتى يكتمل.
        app.MapPost("/admin/incubator/start",
            async (IDocumentStore store, Services.Incubator.StudioAuth auth,
                   Services.Incubator.FeasibilityAnalysisService svc) =>
        {
            if (!(await Services.PlatformAdminGuard.EvaluateAsync(store, auth)).Allowed)
                return Forbidden();
            var s = await svc.StartAsync(Guid.Empty, "صاحِب المَشروع");
            return Results.Redirect($"/admin/incubator/{s.Id}");
        }).DisableAntiforgery();

        app.MapPost("/admin/incubator/{id:guid}/answer",
            async (Guid id, HttpRequest req, IDocumentStore store,
                   Services.Incubator.StudioAuth auth,
                   Services.Incubator.FeasibilityAnalysisService svc) =>
        {
            if (!(await Services.PlatformAdminGuard.EvaluateAsync(store, auth)).Allowed)
                return Forbidden();
            var qid = req.Form["questionId"].ToString().Trim();
            var answer = req.Form["answer"].ToString().Trim();
            if (!string.IsNullOrEmpty(qid))
                await svc.SaveAnswerAsync(id, qid, answer);
            return Results.Redirect($"/admin/incubator/{id}");
        }).DisableAntiforgery();

        app.MapPost("/admin/incubator/{id:guid}/analyze",
            async (Guid id, IServiceScopeFactory scopeFactory, IDocumentStore store,
                   Services.Incubator.StudioAuth auth,
                   Services.Incubator.FeasibilityAnalysisService svc) =>
        {
            if (!(await Services.PlatformAdminGuard.EvaluateAsync(store, auth)).Allowed)
                return Forbidden();
            // عيّن الحالة فوراً (متزامن) لتعرض صفحة الدراسة المؤشّر،
            // ثم شغّل التحليل الطويل في الخلفية بنطاق DI جديد.
            await svc.MarkAnalyzingAsync(id);
            _ = Task.Run(async () =>
            {
                using var scope = scopeFactory.CreateScope();
                var bg = scope.ServiceProvider
                    .GetRequiredService<Services.Incubator.FeasibilityAnalysisService>();
                try { await bg.RunAnalysisAsync(id); }
                catch { /* الحالة تبقى Analyzing؛ تظهر مهلة في الواجهة */ }
            });
            return Results.Redirect($"/admin/incubator/{id}/study");
        }).DisableAntiforgery();

        // إعادة البدء = جلسة جديدة فارغة (الجلسة القديمة تبقى محفوظة).
        app.MapPost("/admin/incubator/restart",
            async (IDocumentStore store, Services.Incubator.StudioAuth auth,
                   Services.Incubator.FeasibilityAnalysisService svc) =>
        {
            if (!(await Services.PlatformAdminGuard.EvaluateAsync(store, auth)).Allowed)
                return Forbidden();
            var s = await svc.StartAsync(Guid.Empty, "صاحِب المَشروع");
            return Results.Redirect($"/admin/incubator/{s.Id}");
        }).DisableAntiforgery();

        return app;
    }

    // اِستِخراج الدَور مِن Referer لِلطَلَبات POST الَّتي تَأتي مِن صَفحَة
    // داخِل /{slug}/r/{role}/... — نَستَخدِمه لِبِناء redirect role-aware.
    private static string? RoleFromReferer(HttpRequest req)
    {
        var referer = req.Headers["Referer"].ToString();
        if (string.IsNullOrEmpty(referer)) return null;
        try
        {
            var uri = new Uri(referer);
            return AuthSession.ExtractRoleFromPath(new PathString(uri.AbsolutePath));
        }
        catch { return null; }
    }

    private static string Link(HttpRequest req, string slug, string path)
        => AuthSession.LinkFor(slug, RoleFromReferer(req), path);

    /// <summary>
    /// <para><b>هَل هذا الفَشَل تَضارُبُ نُسخَة تَيار؟</b> — أَي: خَسِرَ
    /// هذا الطَلَبُ سِباقاً عَلى آخِر وَحدَة حِصَّة، فَارتَدَّت
    /// مُعامَلَتُه كامِلَةً ولَم يُكتَب إعلانُه.</para>
    ///
    /// <para><b>ولِماذا يُفحَص بِالاسم لا بِنَوعٍ واحِد</b>: Marten
    /// يُعبِّر عَن التَضارُب بِأَكثَر مِن شَكل حَسَبَ مَوضِع كَشفِه —
    /// فَحصُ النُسخَة المُتَوَقَّعَة عِندَ الإلحاق
    /// (<c>EventStreamUnexpectedMaxEventIdException</c>)، أَو
    /// <c>ConcurrencyException</c>، أَو خَرقُ فَرادَة
    /// <c>(stream_id, version)</c> في Postgres (‏<c>23505</c>) حينَ
    /// يَتَسابَق طَلَبانِ داخِلَ نافِذَةٍ أَضيَق مِن الفَحص. وقَد يَصِل
    /// أَيُّها مُغَلَّفاً — فَالبَحث يَنزِل في سِلسِلَة
    /// <see cref="Exception.InnerException"/>.</para>
    ///
    /// <para><b>وما لا يَبتَلِعُه</b>: أَيّ فَشَل آخَر يُرفَع كَما هو.
    /// مُرَشِّحٌ يَبتَلِع ما لا يَفهَم هو بِعَينِه العَطَب الَّذي جَعَلَ
    /// الرَصيدَ صِفراً دائِماً في المُستودَع القَديم.</para>
    /// </summary>
    private static bool IsStreamVersionConflict(Exception? ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
        {
            var name = e.GetType().Name;
            if (name is "EventStreamUnexpectedMaxEventIdException"
                     or "ConcurrencyException"
                     or "StreamLockedException")
                return true;

            // خَرق فَرادَة (stream_id, version) — 23505 في Postgres.
            if (e is Npgsql.PostgresException { SqlState: "23505" })
                return true;
        }
        return false;
    }

    // ─── PWA — manifest builder ──────────────────────────────────────
    private static async Task<IResult> BuildManifestAsync(
        string slug, string? role, IDocumentStore store)
    {
        // مَوضِع الالتِقاط: الأَدوار مُجَسَّدَة واللَقطَة مَعَها. دَور
        // مُعَرَّف وَقتَ التَّشغيل (وَثيقَة Marten) لا يوجَد في
        // Tenant.Roles المُخَزَّنَة، فَقِراءَة خام كانَت تُعطي r = null
        // فَيَسقُط الاسم والأَيقونَة والمُختَصَرات كُلُّها عَلى الافتِراضيّ.
        var (tenant, roleSet) = await LoadTenantAndRolesAsync(store, slug);
        if (tenant is null) return Results.NotFound();

        ACommerce.Kit.Roles.Role? r = null;
        if (!string.IsNullOrEmpty(role))
            r = tenant.Roles.FirstOrDefault(x => x.Slug == role);

        var prefix    = string.IsNullOrEmpty(role) ? $"/{slug}" : $"/{slug}/r/{role}";
        // الـ icon endpoint مُسَجَّل تَحت /api/… لا تَحت scope الـ PWA،
        // فَنُشير إليه بِالمَسار المُطلَق الصَحيح. تَركه تَحت prefix يُسَبِّب
        // 404 وَيُفشِل installability check (لا أَيقونات صالِحَة).
        var iconUrl   = string.IsNullOrEmpty(role)
            ? $"/api/{slug}/icon.svg"
            : $"/api/{slug}/r/{role}/icon.svg";
        var appName   = !string.IsNullOrEmpty(r?.PwaName) ? r!.PwaName!
                      : r is not null            ? $"{tenant.Name} — {r.Label}"
                                                 : tenant.Name;
        var shortName = r?.Label ?? tenant.Name;
        var shortcuts = BuildShortcuts(slug, role, r, iconUrl, roleSet);

        return Results.Json(new
        {
            name = appName,
            short_name = shortName,
            description = tenant.TagLine,
            lang = "ar",
            dir = "rtl",
            id = $"{prefix}/",
            start_url = $"{prefix}/",
            scope = $"{prefix}/",
            display = "standalone",
            display_override = new[] { "window-controls-overlay", "standalone", "minimal-ui" },
            orientation = "any",
            background_color = "#f4f4f5",
            theme_color = tenant.BrandColor,
            launch_handler = new { client_mode = "navigate-existing" },
            // handle_links: "preferred" يُخبِر النِظام أَنّ هذه الـ PWA هي
            // المُعالِج المُفَضَّل لِلـ URLs داخِل scope. Chrome/Edge يَعرِضان
            // أَيقونَة "اِفتَح في التَّطبيق" في شَريط العُنوان عِندَ تَصَفُّح
            // عاديّ + يَفتَحان رَوابِط هذه النِطاق في الـ PWA إذا أَمكَن.
            handle_links = "preferred",
            icons = new object[]
            {
                // Chrome's installability checklist يَتَطَلَّب maskable + at-least
                // واحِد ≥ 192x192. SVG واحِدَة تُغَطّي كُلّ الأَحجام لكِنّ
                // نَذكُرها بِأَحجام مُحَدَّدَة لِيَقتَنِع المُتَصَفِّح.
                new { src = iconUrl, sizes = "192x192", type = "image/svg+xml", purpose = "any" },
                new { src = iconUrl, sizes = "512x512", type = "image/svg+xml", purpose = "any" },
                new { src = iconUrl + "?mask=1", sizes = "192x192", type = "image/svg+xml", purpose = "maskable" },
                new { src = iconUrl + "?mask=1", sizes = "512x512", type = "image/svg+xml", purpose = "maskable" },
                new { src = iconUrl, sizes = "any", type = "image/svg+xml", purpose = "any" }
            },
            shortcuts,
            categories = new[] { "business", "lifestyle", "productivity" },
            prefer_related_applications = false
        }, contentType: "application/manifest+json");
    }

    private static object[] BuildShortcuts(string slug, string? role,
        ACommerce.Kit.Roles.Role? r, string iconUrl,
        ACommerce.Kit.Roles.TenantRoleSet roleSet)
    {
        // shortcuts حَسَب الدَور — مُختَصَرَات تَظهَر في long-press عَلى
        // الأَيقونَة (Android + Edge).
        var prefix = string.IsNullOrEmpty(role) ? $"/{slug}" : $"/{slug}/r/{role}";
        var icons  = new[] { new { src = iconUrl, sizes = "any", type = "image/svg+xml" } };

        // المُختَصَرات مِرآة الـ nav لا مَعجَم ثالِث: كُلّ مُختَصَر أَساسيّ
        // هُنا يُقابِل تَبويباً في نَفس عائِلَة التَّنَقُّل (نَفس المَسار
        // ونَفس التَّسمِيَة)، والزائِد عَنه هو <c>extras</c> — السَطح الَّذي
        // يَبلُغُه هذا الدَور تَحديداً. فَالفَتحَتانِ تَكفِيانِ، ولا يَلزَم
        // فَتحَة سادِسَة لِلمُختَصَرات.
        // اللَقطَة لا المُحَلِّل السّاكِن: RoleCompositionResolver يَرى
        // كاتالوج المَنصَّة وَحدَه، فَدَور مُعَرَّف وَقتَ التَّشغيل كانَ
        // يَسقُط عَلى Fallback فَيَأخُذ المُختَصَرات الافتِراضيَّة.
        // ResolveComposition مِرآتُه بِنَفس الحالات الحَدِّيَّة، وبِبَحث
        // يَرى أَدوار هذا المُستَأجِر — ومُستَأجِر بِلا تَأليف يُجيب
        // بِنَفس جَواب اليَوم حَرفاً (TenantRoleSet.Platform).
        var composition = roleSet.ResolveComposition(r?.CatalogSlug);

        object[] DefaultShortcuts() => new object[]
        {
            new { name = "اِستِكشاف",      short_name = "تَصَفُّح",url = $"{prefix}/explore",      icons },
            new { name = "حِسابي",        short_name = "حِسابي",  url = $"{prefix}/me",           icons }
        };

        // قاموس مُغلَق: قيمَة فَتحَة nav ← المُختَصَرات الأَساسيَّة.
        var navTable = new Dictionary<string, Func<object[]>>(StringComparer.Ordinal)
        {
            [ACommerce.Kit.Roles.RoleComponents.RiderNav] = () => new object[]
            {
                new { name = "اِنشُر مِشواراً", short_name = "مِشوار", url = $"{prefix}/create-listing", icons },
                new { name = "طَلَباتي",      short_name = "طَلَباتي", url = $"{prefix}/me/listings",   icons }
            },
            [ACommerce.Kit.Roles.RoleComponents.DriverNav] = () => new object[]
            {
                new { name = "مَشاوير مُتاحَة", short_name = "مَشاوير", url = $"{prefix}/explore",      icons },
                new { name = "عُروضي",          short_name = "عُروضي",  url = $"{prefix}/me/offers",   icons }
            },
            [ACommerce.Kit.Roles.RoleComponents.VendorNav] = () => new object[]
            {
                new { name = "إعلان جَديد",    short_name = "إعلان",   url = $"{prefix}/create-listing", icons },
                new { name = "إعلاناتي",       short_name = "إعلاناتي",url = $"{prefix}/me/listings",   icons },
                new { name = "المُحادَثات",     short_name = "رَسائِل", url = $"{prefix}/chats",          icons }
            },
            [ACommerce.Kit.Roles.RoleComponents.AdminNav] = () => new object[]
            {
                new { name = "لَوحَة الإدارَة", short_name = "إدارَة",  url = $"{prefix}/manage", icons }
            },
            [ACommerce.Kit.Roles.RoleComponents.DefaultNav] = DefaultShortcuts,
        };

        // قاموس مُغلَق: قيمَة في <c>extras</c> ← مُختَصَر إضافيّ (أَو لا شَيء).
        // <c>roleHomeHero</c> مُسَجَّل بِلا مُختَصَر: مُكَوِّن يَتيم مُوَثَّق
        // لا سَطح لَه يُبلَغ، ولا يُسنَد إلى دَور أَصلاً.
        var extraTable = new Dictionary<string, Func<object?>>(StringComparer.Ordinal)
        {
            [ACommerce.Kit.Roles.RoleComponents.DriversList] = () =>
                new { name = "السائِقون",     short_name = "سائِقون",  url = $"{prefix}/drivers",       icons },
            [ACommerce.Kit.Roles.RoleComponents.DriverArea] = () =>
                new { name = "مَنطِقَتي",       short_name = "مَنطِقَتي",url = $"{prefix}/me/area",    icons },
            [ACommerce.Kit.Roles.RoleComponents.RoleHomeHero] = () => null,
        };

        var baseShortcuts = ACommerce.Kit.Roles.RoleComponentMap.Map(
            navTable, composition.Nav, DefaultShortcuts, "مُختَصَرات التَّنَقُّل")();

        var extraShortcuts = composition.Extras
            .Select(e => ACommerce.Kit.Roles.RoleComponentMap.Map(
                extraTable, e, () => (object?)null, "المُختَصَرات الإضافيَّة")())
            .Where(x => x is not null)
            .Select(x => x!);

        return baseShortcuts.Concat(extraShortcuts).ToArray();
    }

    // ─── PWA — icon builder (SVG ديناميكيّ) ───────────────────────────
    private static async Task<IResult> BuildIconAsync(
        string slug, string? role, IDocumentStore store)
    {
        // نَفس مَوضِع الالتِقاط: الـ manifest يُشير إلى هذه النُقطَة في
        // icons، فَقِراءَة خام هُنا كانَت تُرَكِّب أَيقونَة المَتجَر
        // لِدَور مُعَرَّف وَقتَ التَّشغيل بَدَل أَيقونَتِه هو.
        var tenant = await LoadTenantWithRolesAsync(store, slug);
        if (tenant is null) return Results.NotFound();

        ACommerce.Kit.Roles.Role? r = null;
        if (!string.IsNullOrEmpty(role))
            r = tenant.Roles.FirstOrDefault(x => x.Slug == role);

        // أَيقونَة مُخَصَّصَة (data URL) → نَفُكّ الـ base64 وَنُقَدِّمها كَ صورَة.
        var custom = r?.PwaIconDataUrl;
        if (!string.IsNullOrEmpty(custom) && custom.StartsWith("data:"))
        {
            var comma = custom.IndexOf(',');
            if (comma > 0)
            {
                var meta = custom.Substring(5, comma - 5);   // "image/png;base64"
                var b64  = custom[(comma + 1)..];
                var contentType = meta.Split(';')[0];
                try { return Results.File(Convert.FromBase64String(b64), contentType); }
                catch { /* fall through to generated */ }
            }
        }

        // أَيقونَة مَولَّدَة: مُرَبَّع 512x512 بِلَون المَتجَر + الإيموجي/الحَرف.
        var color    = tenant.BrandColor;
        var emoji    = r?.Icon ?? tenant.Categories.FirstOrDefault()?.Icon ?? "";
        var initial  = (r?.Label ?? tenant.Name).FirstOrDefault().ToString();
        var label    = !string.IsNullOrEmpty(emoji) ? emoji : initial;
        var svg = $@"<svg xmlns=""http://www.w3.org/2000/svg"" viewBox=""0 0 512 512"">
  <rect width=""512"" height=""512"" rx=""96"" fill=""{color}""/>
  <text x=""256"" y=""335"" text-anchor=""middle""
        font-family=""Cairo, Segoe UI Emoji, system-ui, sans-serif""
        font-size=""280"" font-weight=""700"" fill=""#ffffff"">{System.Net.WebUtility.HtmlEncode(label)}</text>
</svg>";
        return Results.Content(svg, "image/svg+xml; charset=utf-8");
    }

    // إشعار live بِأَنّ عَدّاد الغَير-مَقروء تَغَيَّر لِمُستَخدِم مُعَيَّن.
    // الـ client (JS في App.razor) يَستَمِع لِـ "unread_changed" عَلى hub
    // /realtime ويُحَدِّث الـ badges. آمِنَة لِلاستِدعاء حَتَّى لَو الـ hub
    // غَير مُتاح — اِلتِقاط الاستِثناء بِصَمت.
    private static async Task NudgeAsync(
        Microsoft.AspNetCore.SignalR.IHubContext<ACommerce.Kit.Realtime.Server.RealtimeHub> hub,
        string slug, Guid userId)
    {
        try
        {
            await hub.Clients
                .Group(ACommerce.Kit.Realtime.Server.RealtimeHub.GroupName(slug, userId))
                .SendAsync("unread_changed");
        }
        catch { /* لا نَكسِر تَدَفُّق الـ POST لَو SignalR فَشِل */ }
    }

    /// <summary>إنشاء إشعار لِكُلّ مُستَخدِم لَه دَور tenant_admin في هذا
    /// المَتجَر. يُستَدعَى عَلى أَحداث رَئيسيَّة (تَسجيل مُستَخدِم جَديد،
    /// إعلان جَديد، بَلاغ، …). لَو لا يُوجَد admin، يُتَجاهَل بِصَمت.</summary>
    private static async Task NotifyAdminsAsync(
        IDocumentStore store, string slug, string type,
        string title, string body, string relatedUrl,
        Microsoft.AspNetCore.SignalR.IHubContext<ACommerce.Kit.Realtime.Server.RealtimeHub>? hub = null,
        ACommerce.Templates.Customer.Marketplace.Services.WebPushService? push = null)
    {
        await using var s = store.LightweightSession(slug);
        var admins = await s.Query<User>()
            .Where(u => u.ActiveRole == "tenant_admin").ToListAsync();
        if (admins.Count == 0) return;

        var now = DateTime.UtcNow;
        foreach (var admin in admins)
        {
            s.Store(new ACommerce.Kit.Notifications.Notification
            {
                Id = Guid.NewGuid(),
                UserId = admin.Id,
                Type = type,
                Title = title,
                Body = body,
                RelatedUrl = relatedUrl,
                At = now
            });
        }
        await s.SaveChangesAsync();

        if (hub is not null)
            foreach (var admin in admins) await NudgeAsync(hub, slug, admin.Id);
        if (push is not null)
            foreach (var admin in admins)
                await push.SendAsync(store, slug, admin.Id, title, body,
                    url: relatedUrl, tag: $"admin-{type}-{Guid.NewGuid():N}");
    }

    /// <summary>
    /// اِشتِقاق نَمَط الـ Deal مِن أَدوار المُستَأجِر — لِتَحديد مَراحِل
    /// تَدَفُّق العَمَلِيّات. مَتجَر بِأَدوار rider/driver → trip، …إلخ.
    ///
    /// <para><c>public</c> لِأَنّ الواجِهَة تَشرَح التَدَفُّق قَبل بَدئِه
    /// (<c>FlowExplainer</c> في صَفحَة الإعلان)، وَيَجِب أَن تَشرَح
    /// <b>نَفس</b> النَّمَط الَّذي سَيُنشِئُه هذا الكود عِندَ بَدء
    /// الصَّفقَة. اِشتِقاق ثانٍ في الواجِهَة كانَ سَيَنفَصِل صامِتاً.</para>
    ///
    /// <para><b>والشُروط صارَت بَيانات</b>: كانَت هُنا أَسماء أَدوار
    /// بِأَعيانِها (<c>rider</c>/<c>driver</c>/<c>host</c>) في شُروط
    /// مُتَناثِرَة؛ وصارَ الانجِذاب حَقلاً في مِلَفّ كُلّ دَور
    /// (<c>dealPatternAffinity</c>) تَجمَعُه
    /// <see cref="ACommerce.Kit.Roles.RoleDealPatternAffinity.Resolve"/>
    /// بِتَرتيب غَلَبَة مُعلَن. الاشتِقاق قائِم عَلى <b>أَدوار مُفرَدَة</b>
    /// لا تَركيبات مَجموعات — ولِذلك حَقل في المِلَفّ لا جَدوَل قَواعِد.
    /// السُلوك مُطابِق، مَحروساً بِتَوصيف عَلى إحدى وعِشرينَ تَركيبَة.</para>
    /// </summary>
    public static string PatternFromTenant(ACommerce.Kit.Tenants.Tenant? t)
        => t is null
            ? ACommerce.Kit.Roles.RoleDealPatternAffinity.Fallback
            : ACommerce.Kit.Roles.RoleDealPatternAffinity.Resolve(
                  t.Roles.Select(r => r.CatalogSlug));

    // اِستِخراج owner_id مِن listing.Attributes كَ Guid.
    private static Guid? ParseListingOwnerId(Listing listing)
    {
        if (!listing.Attributes.TryGetValue("owner_id", out var s)) return null;
        return Guid.TryParse(s, out var g) ? g : null;
    }

    // فَحص صَلاحِيَّة لِلمُستَخدِم الحاليّ — يَجلِب tenant + user وَيُفَوِّض
    // إلى <see cref="ACommerce.Kit.Roles.RolePermissions.Has"/>.
    private static async Task<bool> HasPermissionAsync(
        HttpContext http, string slug, Guid userId, string permission, IDocumentStore store)
    {
        var tenant = await LoadTenantWithRolesAsync(store, slug);
        if (tenant is null) return false;
        if (tenant.Roles.Count == 0) return true;   // legacy mode

        await using var t = store.QuerySession(slug);
        var user = await t.LoadAsync<ACommerce.Kit.Auth.User>(userId);
        if (user is null) return false;
        // الدَور الفَعّال (as الصَّريح لِمَن يَملِكُه ثُمَّ URL ثُمَّ المُخَزَّن)
        // بَدَل ActiveRole وَحدَه — يَعمَل المُستَخدِم بِدَورَينِ مُتَزامِنَينِ
        // عَلى نُقطَة كِتابَة بِلا دَور. راجِع Gates.EffectiveRole.
        var effectiveRole = await Gates.EffectiveRole.ResolveAsync(
            http, slug, userId, user.ActiveRole);
        return ACommerce.Kit.Roles.RolePermissions.Has(
            tenant.Roles, effectiveRole, permission);
    }

    /// <summary>
    /// <para><b>مَوضِع الالتِقاط في مَسارات القِراءَة السّاكِنَة</b> —
    /// يُحَمِّل المُستَأجِر ويُجَسِّد فَوق <c>Roles</c> أَدوارَه المُؤَلَّفَة
    /// المُعتَمَدَة. كُلّ مَن كانَ يَكتُب سَطرَي
    /// <c>QuerySession() + LoadAsync&lt;Tenant&gt;</c> ثُمَّ يَقرَأ
    /// <c>tenant.Roles</c> يَستَدعي هذه بَدَلَهُما.</para>
    ///
    /// <para><b>ولا تُستَخدَم في مَسار يَحفَظ المُستَأجِر</b> — التَجسيد
    /// يَعيش في الذاكِرَة، وحِفظُ وَثيقَة مُجَسَّدَة كانَ سَيَنسَخ
    /// التَعريفات داخِل <c>Tenant</c> فَيَصير لِلحَقيقَة مَصدَران. لِذلك
    /// <c>/admin/tenants/{slug}/roles/save</c> يَبقى عَلى تَحميلِه
    /// المُباشِر بِـ <c>LightweightSession</c>.</para>
    /// </summary>
    private static async Task<ACommerce.Kit.Tenants.Tenant?> LoadTenantWithRolesAsync(
        IDocumentStore store, string slug)
        => (await LoadTenantAndRolesAsync(store, slug)).Tenant;

    /// <summary>
    /// <para><b>نَفس القِراءَة، ومَعَها اللَقطَة</b> — لِمَن لا يَكفيه
    /// <c>Tenant.Roles</c> مُجَسَّداً بَل يَحتاج أَن <b>يُرَكِّب</b> فَوق
    /// تَعريف دَور (‏<see cref="ACommerce.Kit.Roles.TenantRoleSet.ResolveComposition"/>).
    /// التَجسيد يُعطي الدَور نَفسَه، واللَقطَة وَحدَها تُعطي فَتَحاتِه.</para>
    ///
    /// <para>و<see cref="LoadTenantWithRolesAsync"/> يُفَوِّض إلَيها —
    /// فَمَسار القِراءَة واحِد لا يَنحَرِف أَحَدُ فَرعَيه عَن الآخَر.</para>
    /// </summary>
    private static async Task<(ACommerce.Kit.Tenants.Tenant? Tenant,
                               ACommerce.Kit.Roles.TenantRoleSet Roles)>
        LoadTenantAndRolesAsync(IDocumentStore store, string slug)
    {
        await using var g = store.QuerySession();
        var tenant = await g.LoadAsync<ACommerce.Kit.Tenants.Tenant>(slug);
        if (tenant is null)
            return (null, ACommerce.Kit.Roles.TenantRoleSet.Platform);

        var set = await Services.TenantRoleService.ReadUncachedAsync(store, slug);
        var merged = set.Materialize(tenant.Roles);
        if (!ReferenceEquals(merged, tenant.Roles)) tenant.Roles = merged.ToList();
        return (tenant, set);
    }

    // تَسكين دَور لِمُستَخدِم بَعد تَوثيقِه — يُستَدعَى مِن /verify عِندَ
    // وُجود ?as=role مِن صَفحَة الدُخول. tenant_admin مَمنوع: يَجِب أَن
    // يُمنَح يَدَويّاً مِن /admin/tenants/{slug}/users.
    private static async Task AssignRoleAsync(
        string slug, Guid userId, string roleSlug, IDocumentStore store)
    {
        if (roleSlug == "tenant_admin") return;
        var tenant = await LoadTenantWithRolesAsync(store, slug);
        if (tenant is null) return;
        var picked = tenant.Roles.FirstOrDefault(r => r.Slug == roleSlug);
        if (picked is null) return;

        await using var s = store.LightweightSession(slug);
        var user = await s.LoadAsync<ACommerce.Kit.Auth.User>(userId);
        if (user is null) return;
        user.ActiveRole = roleSlug;
        user.UpdatedAt = DateTime.UtcNow;
        s.Store(user);
        await s.SaveChangesAsync();
    }

    // قَرار التَّوجيه بَعد دُخول ناجِح:
    //  1) مَتجَر بِلا أَدوار → الصَفحَة الرَّئيسِيَّة (سُلوك قَديم لِـ ashare/ejar).
    //  2) إن وُجِدَ <paramref name="asRole"/> (مِن ?as= أَو /r/role/login)
    //     → URL مَفروع تَحت /r/{role}/ لِيَفصِل الـ session.
    //  3) خِلاف ذلك: نَتَّبِع ActiveRole مِن user doc (legacy/no-prefix).
    private static async Task<string> PostLoginRouteAsync(
        string slug, Guid userId, string? asRole, IDocumentStore store)
    {
        var tenant = await LoadTenantWithRolesAsync(store, slug);
        if (tenant is null || tenant.Roles.Count == 0)
            return $"/{slug}";

        await using var t = store.LightweightSession(slug);
        var user = await t.LoadAsync<ACommerce.Kit.Auth.User>(userId);
        if (user is null) return $"/{slug}";

        // إن لَم يُعطَ asRole + لا ActiveRole + دَور واحِد → اِضبِطه تِلقائيّاً.
        if (string.IsNullOrEmpty(asRole) &&
            tenant.Roles.Count == 1 && string.IsNullOrEmpty(user.ActiveRole))
        {
            user.ActiveRole = tenant.Roles[0].Slug;
            t.Store(user);
            await t.SaveChangesAsync();
        }

        // الدَور الفِعليّ الَّذي سَنَستَخدِمُه لِلـ URL: asRole إن وُجِدَ، أَو
        // ActiveRole كَ احتِياط.
        var effectiveRoleSlug = !string.IsNullOrEmpty(asRole) ? asRole : user.ActiveRole;
        if (string.IsNullOrEmpty(effectiveRoleSlug))
            return $"/{slug}/me/role";

        var role = tenant.Roles.FirstOrDefault(r => r.Slug == effectiveRoleSlug);
        if (role is null) return $"/{slug}/me/role";

        // الـ onboarding مَطلوب لَو دَور لَه حُقول مَطلوبَة لَم تُملَأ بَعد.
        var roleValues = user.RoleAttributesJson.TryGetValue(role.Slug, out var rv)
            ? rv : new Dictionary<string, string>();
        var needsOnboarding = role.Fields
            .Where(f => f.IsRequired)
            .Any(f => !roleValues.ContainsKey(f.Code) || string.IsNullOrEmpty(roleValues[f.Code]));

        // عِندَ asRole نَبني URL مَفروع تَحت /r/{role}/ — يَضمَن أَنَّ
        // المُتَصَفِّح في هذا التَّبويب يَستَخدِم الـ cookie role-scoped.
        if (!string.IsNullOrEmpty(asRole))
        {
            if (needsOnboarding) return $"/{slug}/r/{asRole}/me/role/onboarding";
            return string.IsNullOrEmpty(role.HomeRoute)
                ? $"/{slug}/r/{asRole}"
                : $"/{slug}/r/{asRole}{role.HomeRoute}";
        }

        if (needsOnboarding) return $"/{slug}/me/role/onboarding";
        return string.IsNullOrEmpty(role.HomeRoute)
            ? $"/{slug}" : $"/{slug}{role.HomeRoute}";
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
