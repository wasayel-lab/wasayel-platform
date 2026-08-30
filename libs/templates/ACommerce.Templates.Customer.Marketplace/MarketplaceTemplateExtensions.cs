using ACommerce.Kit.Auth;
using ACommerce.Kit.Auth.Server;
using ACommerce.Kit.Chat;
using ACommerce.Kit.Favorites;
using ACommerce.Kit.Listings;
using ACommerce.Platform.I18n;
using ACommerce.Platform.Shared;
using ACommerce.Templates.Customer.Marketplace.Api;
using ACommerce.Templates.Customer.Marketplace.Billing;
using ACommerce.Templates.Customer.Marketplace.Gates;
using ACommerce.Templates.Customer.Marketplace.Services.TenantConfig;
using Marten;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
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
        // قِراءَةُ إعلانٍ واحِد — يَسأَلُها **مُستَهلِكان**: شاشَةُ
        // التَحرير (فَلا تَفتَح جَلسَةً بِيَدِها، والطَبَقَة الثامِنَة
        // تَبقى عِندَ ‏55) والمُرَشِّح `ListingOwnerFilter`.
        services.AddScoped<ACommerce.Templates.Customer.Marketplace.Services.ListingLookupService>();
        // ═══ خِدماتُ الاستِعلام — المَوجَة ٥ ══════════════════════════
        // كُلُّ واحِدَةٍ مِنها تَنزِع صَفحَةً (أَو أَكثَر) مِن سِجِلّ
        // الطَبَقَة الثامِنَة: الصَفحَةُ تَحقِن الخِدمَةَ ولا تَعرِف
        // `IDocumentStore`. وهذا بِعَينِه شَرطُ تَشغيلِها في MAUI
        // Blazor Hybrid حَيثُ لا قاعِدَةَ بَيانات على الهاتِف.
        services.AddScoped<Services.Queries.TenantDirectory>();
        services.AddScoped<Services.Queries.AuditLogQueries>();
        services.AddScoped<Services.Queries.ShellQueries>();
        services.AddScoped<Services.Queries.AccountQueries>();
        services.AddScoped<Services.Queries.StorefrontQueries>();
        services.AddScoped<Services.Queries.StudioQueries>();
        services.AddScoped<Services.Queries.PlatformStatsQueries>();
        services.AddScoped<Services.Queries.TenantAdminQueries>();
        services.AddScoped<Services.Queries.PlatformBillingQueries>();
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

        // مُزَوِّدو المُستَأجِرِ وَقتَ التَشغيل — Singleton بِنَفس
        // المُبَرِّرِ حَرفاً: الكاشُ بِمِفتاحِ المُستَأجِرِ يَجِب أَن
        // يَعبُرَ الطَلَبات، والعَزلُ يَقَع في جَلسَةِ السلاجِ لا في
        // عُمرِ الخِدمَة.
        services.AddSingleton<ACommerce.Templates.Customer.Marketplace.Services.TenantProviderService>();

        // ─── الاستِحقاق ──────────────────────────────────────────────────
        // Scoped لا Singleton: بِلا كاش — الرَصيد حالَة تَتَغَيَّر بِكُلّ
        // عَمَلِيَّة، وكاشُها هو بِعَينِه ما يَجعَل الفَحص يَكذِب. والقِراءَة
        // تُفتَح بِجَلسَة سلاج المُستَأجِر كَغَيرِها.
        services.AddScoped<ACommerce.Kit.Subscriptions.IEntitlements,
                           ACommerce.Kit.Subscriptions.SubscriptionEntitlements>();

        // وتَنفيذٌ ثانٍ بِمَصدَرِ حَقيقَةٍ آخَر: وَثيقَةُ باقَةِ المُستَأجِر
        // (‏ADR-003). يُقرَآنِ بِـ`http.Entitlements(capability)` الَّذي
        // يَسأَل `Handles` — لا بِـ`GetRequiredService` الَّذي يُعيد آخِرَ
        // مُسَجَّلٍ صامِتاً.
        services.AddScoped<ACommerce.Kit.Subscriptions.IEntitlements,
                           ACommerce.Kit.Subscriptions.TenantPlanEntitlements>();

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

        // ─── التَخارُج — الحَقيبَةُ الَّتي يَخرُجُ بِها العَميل ──────────
        // ‏Scoped كَجاراتِها: تَقرَأُ حالَةً تَتَغَيَّر، ولا كاشَ لَها.
        // ومُستَهلِكاها قائِمانِ: نُقطَةُ `‏/studio/apps/{slug}/export.zip`
        // وشاشَةُ `‏/studio/apps/{slug}/export` الَّتي تَعرِضُ الأَعدادَ
        // **مِن هذِه الخِدمَةِ نَفسِها** — وإلّا انجَرَفَ المَعروضُ عَن
        // المُسَلَّم.
        services.AddScoped<Services.Export.TenantExportService>();

        // ─── سَطحُ الـAPI — المَوجَةُ الأولى ─────────────────────────────
        // ثَلاثُ خِدماتٍ لِثَلاثَةِ مُستَهلِكينَ قائِمين: المُرَشِّح،
        // ونُقطَتا الاستوديو، وشاشَةُ المَفاتيح. ولا رابِعَة تُبنى
        // «لِأَنَّها قَد تَلزَم».
        services.AddScoped<Services.Api.ApiKeyService>();
        services.AddScoped<Api.ApiIdempotencyService>();
        // وِعاءُ العَرضِ مَرَّةً واحِدَة — Singleton لِأَنَّه يَعبُر
        // طَلَبَين (‏POST ثُمَّ تَحويلٌ ثُمَّ تَصيير)، والكاش تَحتَه
        // هُوَ نَفسُ `IMemoryCache` المُسَجَّل في المِنَصَّة.
        services.AddSingleton<Services.Api.ApiKeyRevealStore>();

        // ─── شَكلُ وَثيقَتَي الـAPI في Marten ────────────────────────────
        // <b>هُنا لا في `HostingExtensions`</b>: الاعتِمادُ يَمشي مِن
        // القالَب إلى النَواة لا العَكس، فَتَسجيلُ وَثيقَةٍ يَملِكُها
        // القالَبُ في مِلَفّ النَواة يَقلِبُ الاتِّجاه.
        services.ConfigureMarten(opts =>
        {
            // مِفتاحُ الـAPI <b>عامّ</b> — يُحمَّل قَبلَ أَن يُعرَف
            // المُستَأجِر، فَلا سَبيلَ إلى حَصرِه بِـ`tenant_id`.
            // نَفسُ استِثناء وَثيقَة `Tenant` ولِنَفس السَبَب.
            opts.Schema.For<Services.Api.ApiKeyDocument>()
                .SingleTenanted()
                .Identity(x => x.Id);

            // وسِجِلُّ مَرَّة-واحِدَة <b>مَحصورٌ بِالمُستَأجِر</b>
            // بِالسِياسَة العامَّة — والعَزلُ مَجّانيّ: مِفتاحُ
            // مُستَأجِرٍ لا يُصادِف مِفتاحَ آخَر.
            opts.Schema.For<Api.ApiIdempotencyRecord>().Identity(x => x.Id);

            // ورَبطُ المُزَوِّدِ <b>مَحصورٌ بِالمُستَأجِر</b>
            // بِالسِياسَةِ العامَّة (`AllDocumentsAreMultiTenanted`)،
            // فَالعَزلُ مَجّانِيٌّ بِصِفرِ سَطر: لا استِعلامَ عابِراً
            // لِلمُستَأجِرينَ مُمكِنٌ أَصلاً. ومُعَرِّفُه <b>هُوَ
            // القُدرَة</b> — رَبطٌ فَعّالٌ واحِدٌ لِكُلّ قُدرَة.
            opts.Schema.For<ACommerce.Platform.Providers.TenantProviderBinding>()
                .Identity(x => x.Id);
        });

        return services;
    }

    public static IEndpointRouteBuilder MapCustomerMarketplaceTemplate(this IEndpointRouteBuilder app)
    {
        // ─── Phone OTP ──────────────────────────────────────────────────
        // ‏`IOtpChannel?` **اختِياريَّةٌ في التَوقيع** لا مَفحوصَةٌ في الجِسم:
        // خارِجَ التَطوير بِلا `Auth__Sms__Provider` لا تُسَجَّلُ قَناةٌ
        // أَصلاً (‏`AuthChannelSelection`)، فَلَو بَقِيَت إلزامِيَّةً
        // لَارتَدَّ الطَلَبُ **‏500** — عُطلاً يَبدو خَلَلاً لا سِياسَة.
        // والفَشَلُ المُغلَقُ يُقال بِرِسالَةٍ مِن القامُوس.
        app.MapPost("/{slug}/auth/phone/login",
            async (string slug, HttpRequest req, IDocumentStore store,
                   ITenantContext tenant,
                   [FromServices] IOtpChannel? channel) =>
        {
            if (!tenant.IsResolved) return Results.NotFound();
            var phone = req.Form["phone"].ToString().Trim();
            var asRole = req.Form["as"].ToString().Trim();
            if (channel is null)
                return Results.Redirect(Link(req, slug,
                    $"login?err=phone_unavailable" +
                    (string.IsNullOrEmpty(asRole) ? "" : $"&as={Uri.EscapeDataString(asRole)}")));
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
                   ITenantContext tenant,
                   [FromServices] IEmailOtpChannel? channel) =>
        {
            if (!tenant.IsResolved) return Results.NotFound();
            var email = EmailAddress.Normalize(req.Form["email"].ToString());
            var asRole = req.Form["as"].ToString().Trim();
            var asParam = string.IsNullOrEmpty(asRole) ? "" : $"&as={Uri.EscapeDataString(asRole)}";
            // نَفسُ الفَشَلِ المُغلَقِ — راجِع مَسارَ الهاتِف أَعلاه.
            if (channel is null)
                return Results.Redirect(Link(req, slug, $"login?err=email_unavailable{asParam}"));
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
            async (string slug, HttpRequest req, ITenantContext tenant,
                   [FromServices] INafathChannel? channel) =>
        {
            if (!tenant.IsResolved) return Results.NotFound();
            var nid = req.Form["nid"].ToString().Trim();
            // نَفسُ الفَشَلِ المُغلَقِ — راجِع مَسارَ الهاتِف أَعلاه.
            if (channel is null)
                return Results.Redirect(Link(req, slug, $"login?err=nafath_unavailable"));
            if (string.IsNullOrEmpty(nid) || nid.Length != 10)
                return Results.Redirect(Link(req, slug, $"login?err=nid_required"));
            var pending = await AuthHandlers.RequestNafathHandler(new RequestNafath(nid), tenant, channel, default);
            return Results.Redirect(Link(req, slug,
                $"login?stage=verify&nid={Uri.EscapeDataString(nid)}" +
                $"&attempt={pending.AttemptId}&code={pending.DisplayCode}"));
        }).DisableAntiforgery();

        app.MapPost("/{slug}/auth/nafath/verify",
            async (string slug, HttpRequest req, HttpResponse res,
                   ITenantContext tenant, [FromServices] INafathChannel? channel,
                   IDocumentStore store) =>
        {
            if (!tenant.IsResolved) return Results.NotFound();
            var nid = req.Form["nid"].ToString().Trim();
            var attempt = req.Form["attempt"].ToString();
            if (channel is null)
                return Results.Redirect(Link(req, slug, $"login?err=nafath_unavailable"));
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
        }).DisableAntiforgery()
          .RequireStoreWritable();

        // ─── عَدّادُ المُشاهَدَة — أَثَرٌ جانِبيّ خارِجَ مَسار العَرض ────
        // كانَ `TenantListingDetail.razor` يُلحِق `ListingViewed` ويُودِع
        // في طَلَب `GET`، فَكانَت **فَتحَتانِ مُتَتالِيَتانِ تُعطِيانِ
        // صَفحَتَينِ مُختَلِفَتَين**. ومِن هُنا جاءَت قاعِدَةُ «لا تُفتَح
        // صَفَحاتُ تَفصيل إعلانات ashare».
        //
        // **ولا حارِسَ هُنا عَمداً، والصَراحَةُ أَولى مِن الإيهام**:
        // مُشاهَدَةُ الزائِر المَجهول هي أَغلَبُ المُشاهَدات، فَحَصرُ
        // العَدّاد في المُوَثَّقين يُبَدِّل مَعناه. و`ResolveToken` أَدناه
        // تُقرَأ **لِنِسبَة المُشاهَدَة لا لِمَنعِها** — وهي رَمزٌ
        // يَعُدُّه `WriteEndpointGuardTests` حارِساً، وذاكَ حَدٌّ مُعلَنٌ
        // في تَوثيقِه («لا يُثبِت أَنّ الحارِس يَرُدّ»). والحارِسُ
        // الحَقيقيّ هُنا على **المَفعول بِه**: إعلانٌ غَير مَوجود أَو
        // مَحذوف يُرَدّ ‏404 قَبلَ أَيّ كِتابَة.
        app.MapPost("/{slug}/listings/{id:guid}/view",
            async (string slug, Guid id, HttpRequest req, IDocumentStore store) =>
        {
            var parsed = AuthHandlers.ParseToken(AuthSession.ResolveToken(req, slug));
            var viewerId = parsed is { } p && p.TenantSlug == slug
                ? p.UserId : (Guid?)null;

            await using var s = store.LightweightSession(slug);
            var listing = await s.Events.AggregateStreamAsync<Listing>(id);
            if (listing is null || listing.IsDeleted) return Results.NotFound();

            s.Events.Append(id, new ListingViewed(id, viewerId, DateTime.UtcNow));
            await s.SaveChangesAsync();
            return Results.NoContent();
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
        }).DisableAntiforgery()
          .RequireStoreWritable();

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
        }).DisableAntiforgery()
          .RequireStoreWritable();

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
        }).DisableAntiforgery()
          .RequireStoreWritable();

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
                    // فَشَلُ الصورَةِ لا يُسقِط حِفظَ الاسم — **نَفسُ ما
                    // يَفعَلُه جِسمُ إنشاءِ الإعلانِ مُنذُ كُتِب**، وكانَ
                    // هذا المَوضِعُ وَحدَه بِلا الْتِقاطٍ فَيَرُدُّ ‏500
                    // على تَعديلِ اسمٍ لا عَلاقَةَ لَه بِالصورَة. صارَ
                    // لَه مَعنىً حَقيقيٌّ مُنذُ ADR-017: خارِجَ التَطويرِ
                    // بِلا مَخزَنٍ دائِمٍ تَرمي `UnavailableFileStorage`
                    // عَمداً — والرَفضُ عِندَ الكِتابَةِ هُوَ ما يَمنَع
                    // رابِطاً مُعَلَّقاً مِن الوُجود.
                    try
                    {
                        var ext = ct.Split('/')[1].Replace("jpeg", "jpg");
                        var key = $"tenants/{slug}/avatars/{userId}.{ext}";
                        await using var stream = avatar.OpenReadStream();
                        var stored = await files.UploadAsync(key, stream, ct);
                        newAvatarUrl = stored.PublicUrl;
                    }
                    catch (ACommerce.Kit.Files.FileStorageException) { /* الاسمُ يُحفَظ، والصورَةُ لا */ }
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
        }).DisableAntiforgery()
          .RequireStoreWritable();

        // ─── Plans subscribe ────────────────────────────────────────────
        // **الباقَةُ بِسِعرٍ لا تُباعُ مِن هُنا إطلاقاً.** كانَ الجِسمُ
        // يُحَمِّل الباقَةَ **ويَتَجاهَل `Price`** فَيَمنَح الحِصَّةَ
        // بِنَقرَة (تَسريبُ إيراد)، ثُمَّ صارَ يَفتَح طَلَباً مُعَلَّقاً
        // بِتَعليمات حَوالَةٍ إلى **حِساب التاجِر** — وذاكَ جَوابٌ صَحيحٌ
        // عَن سُؤالٍ خَطَأ: قَرارُ المالِك (‏2026-08-23) «لا تَسمَح
        // لِلتاجِر بِاستِلام حَوالات». فَالمَدفوعَةُ تُرَدّ بِرَمز خَرقٍ
        // مِن `PlanPurchasePolicy` حَتّى يوجَدَ لِلمَتجَر مُزَوِّدُ
        // دَفعٍ خاصٌّ بِه.
        //
        // **والمَجّانِيَّة لَم تُمَسّ**: `Price == 0` تُمنَح ذاتِيّاً
        // بِنَفس الحَدَث ونَفس المَجرى.
        app.MapPost("/{slug}/plans/{planId}/subscribe",
            async (string slug, string planId, HttpRequest req, IDocumentStore store,
                   Services.Queries.TenantDirectory tenants) =>
        {
            var token = AuthSession.ResolveToken(req, slug);
            var parsed = AuthHandlers.ParseToken(token);
            if (parsed is null) return Results.Redirect(Link(req, slug, $"login?returnUrl=/{slug}/plans"));
            var (userId, _, _) = parsed.Value;
            var configured = (await tenants.FindAsync(slug))?.PaymentProviderConfigured ?? false;

            await using var s = store.LightweightSession(slug);
            var outcome = await Services.Subscriptions.PlanSubscribeService.SubscribeAsync(
                s, planId, userId, configured, DateTime.UtcNow, Guid.NewGuid());
            if (outcome.Refusal is not null)
                return Results.Redirect(Link(req, slug, $"plans?err={outcome.Refusal}"));
            if (outcome.PayAtProvider)
                return Results.Redirect(Link(req, slug, $"plans/{planId}/pay"));
            await s.SaveChangesAsync();
            return Results.Redirect(Link(req, slug, $"me"));
        }).DisableAntiforgery()
          .RequireStoreWritable();

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
        }).DisableAntiforgery()
          .RequireStoreWritable();

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
        }).DisableAntiforgery()
          .RequireStoreWritable();

        // ─── Create listing — gates: auth + terms + permission ──────────
        // الـ filters تَتَكَفَّل بِالتَّوثيق وَالشُروط وَ "listing.create".
        app.MapPost("/{slug}/listings/create",
            async (string slug, HttpContext http, HttpRequest req, IDocumentStore store, L l,
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
                        Title = l.Format("notifications.saved_search.title", ss.Label),
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
                    l["notifications.saved_search.push_title"],
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
              redirectPath: "create-listing", errCode: "quota")
          .RequireStoreWritable();

        // ─── Edit listing — الفَجوَة الَّتي كَشَفَها إغلاقُ الثَغرَة ─────
        //
        // ‏`POST /{slug}/api/listings/{id}/edit` حُذِفَت في `a7e0352d`
        // لِأَنَّها كانَت تَقبَل أَيّ طَلَبٍ مَجهول، فَصارَ
        // `ListingEdited` يَتيماً — أَي أَنّ المُنتَج **بِلا شاشَةِ
        // تَحرير إعلانٍ إطلاقاً**، وكانَت الثَغرَةُ تَستُر الفَجوَة.
        // وهذِه هي النُقطَة الشَرعِيَّة: نَفسُ المَسار، وهُوِيَّةُ
        // الفاعِل مِن **الجَلسَة** لا مِن جِسم الطَلَب، وحارِسانِ
        // مُعلَنانِ في التَوقيع — تَوثيقٌ ثُمَّ مِلكِيَّة.
        //
        // والجِسمُ يَقرَأ النَموذَجَ ويَعرِض، والمَنطِقُ في
        // `ListingEditService` (تَأخُذ الجَلسَة ولا تَفتَحُها)،
        // والحَدَثُ يُلحَق في **نَفس** الجَلسَة الَّتي تُودِع.
        app.MapPost("/{slug}/listings/{id:guid}/edit",
            async (string slug, Guid id, HttpContext http, HttpRequest req, IDocumentStore store) =>
        {
            await using var s = store.LightweightSession(slug);
            var result = await Services.Listings.ListingEditService.EditAsync(s,
                new Services.Listings.ListingEditRequest(
                    id, http.UserId(),
                    req.Form["title"].ToString(),
                    req.Form["description"].ToString(),
                    req.Form["price"].ToString(),
                    req.Form["city"].ToString(),
                    req.Form["district"].ToString()),
                http.RequestAborted);

            if (result.Status == Services.Listings.ListingEditStatus.Missing)
                return Results.Redirect(Link(req, slug, "me/listings"));
            if (!result.Ok)
                return Results.Redirect(Link(req, slug, $"edit-listing/{id}?err={result.Code}"));

            await s.SaveChangesAsync();
            return Results.Redirect(Link(req, slug, $"listings/{id}?saved=1"));
        }).DisableAntiforgery().RequireAuth().RequireListingOwner()
          .RequireStoreWritable();

        // ─── Delete listing — نَفسُ الحارِسَين ونَفسُ الخِدمَة ──────────
        // الحَذفُ لَيِّن (`IsDeleted`)، فَهو قابِلٌ لِلعَكس بِحَدَثٍ
        // مُقابِل. و`ListingDeleted` لَه باعِثٌ مَحروسٌ سَلَفاً في
        // إشراف الاستوديو — فَهذا **سَطحُ المالِك** لا باعِثٌ أَوَّل.
        app.MapPost("/{slug}/listings/{id:guid}/delete",
            async (string slug, Guid id, HttpContext http, HttpRequest req, IDocumentStore store) =>
        {
            await using var s = store.LightweightSession(slug);
            var result = await Services.Listings.ListingEditService.DeleteAsync(s,
                new Services.Listings.ListingDeleteRequest(id, http.UserId()),
                http.RequestAborted);

            if (result.Status == Services.Listings.ListingEditStatus.Rejected)
                return Results.Redirect(Link(req, slug, $"edit-listing/{id}?err={result.Code}"));

            if (result.Ok) await s.SaveChangesAsync();
            return Results.Redirect(Link(req, slug, "me/listings"));
        }).DisableAntiforgery().RequireAuth().RequireListingOwner()
          .RequireStoreWritable();

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
        }).DisableAntiforgery()
          .RequireStoreWritable();

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
        }).DisableAntiforgery()
          .RequireStoreWritable();

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
        }).DisableAntiforgery()
          .RequireStoreWritable();

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
        }).DisableAntiforgery()
          .RequireStoreWritable();

        // ─── Accept an offer (listing owner) ────────────────────────────
        app.MapPost("/{slug}/offers/{id:guid}/accept",
            async (string slug, Guid id, HttpRequest req, IDocumentStore store, L l,
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
                Subject = l.Format("chats.offer.subject", offer.Price),
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
                Title = l["notifications.offer_accepted.title"],
                Body  = l.Format("notifications.offer_accepted.body", acceptorName, offer.Price),
                RelatedUrl = $"/{slug}/chats/{conv.Id}",
                At = now
            });

            await s.SaveChangesAsync();
            // أَخطِر السائِق فَوراً — الإشعار + المُحادَثَة ظَهَرا.
            await NudgeAsync(hub, slug, offer.OffererId);
            await push.SendAsync(store, slug, offer.OffererId,
                l["notifications.offer_accepted.title"],
                l.Format("notifications.offer_accepted.push_body", acceptorName, offer.Price),
                url: $"/{slug}/chats/{conv.Id}",
                tag: $"offer-{id}");
            return Results.Redirect(Link(req, slug, $"chats/{conv.Id}"));
        }).DisableAntiforgery()
          .RequireStoreWritable();

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
        }).DisableAntiforgery()
          .RequireStoreWritable();

        // ─── Trip lifecycle — driver marks "arrived at pickup" ───────────
        // فَحص قُرب: السائِق يُرسِل مَوقِعَه الحاليّ، نُقارِنه مَع
        // pickup_lat/pickup_lng. لَو > 1 كم يُرفَض الادِّعاء.
        app.MapPost("/{slug}/trips/{listingId:guid}/arrived",
            async (string slug, Guid listingId, HttpRequest req, IDocumentStore store, L l,
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
                Title = l["notifications.driver_arrived.title"],
                Body  = l.Format("notifications.driver_arrived.body", match.OffererName),
                RelatedUrl = $"/{slug}/listings/{listingId}",
                At = DateTime.UtcNow
            });

            await s.SaveChangesAsync();
            var ownerGuid = ParseListingOwnerId(listing);
            if (ownerGuid.HasValue)
            {
                await NudgeAsync(hub, slug, ownerGuid.Value);
                await push.SendAsync(store, slug, ownerGuid.Value,
                    l["notifications.driver_arrived.title"],
                    l.Format("notifications.driver_arrived.body", match.OffererName),
                    url: $"/{slug}/listings/{listingId}",
                    tag: $"arrived-{listingId}");
            }
            return Results.Redirect(Link(req, slug, $"listings/{listingId}?trip=arrived"));
        }).DisableAntiforgery()
          .RequireStoreWritable();

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
        }).DisableAntiforgery()
          .RequireStoreWritable();

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
        }).DisableAntiforgery()
          .RequireStoreWritable();

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
        }).DisableAntiforgery()
          .RequireStoreWritable();

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
        }).DisableAntiforgery()
          .RequireStoreWritable();

        // ─── Start direct chat with another user ────────────────────────
        // مُستَخدَم في صَفحَة /{slug}/drivers — العَميل يَفتَح مُحادَثَة
        // مُباشَرَة مَع سائِق بِلا حاجَة لِنَشر طَلَب مِشوار.
        app.MapPost("/{slug}/users/{userId:guid}/chat",
            async (string slug, Guid userId, HttpRequest req, IDocumentStore store, L l) =>
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
                Subject = l.Format("chats.direct.subject", partner.FullName),
                ListingId = null,
                LastAt = DateTime.UtcNow,
                // مُحادَثَة عامَّة بِلا TTL — لَيسَت مُؤَقَّتَة كَالمَشوار.
                ExpiresAt = null
            };
            s.Store(conv);
            await s.SaveChangesAsync();
            return Results.Redirect(Link(req, slug, $"chats/{conv.Id}"));
        }).DisableAntiforgery()
          .RequireStoreWritable();

        // ─── تَعليمُ مُحادَثَةٍ مَقروءَة — أَمرٌ صَريح لا أَثَرٌ في عَرض ──
        // كانَ هذا التَصفيرُ يَقَع داخِلَ `ChatRoom.razor` في طَلَب `GET`:
        // كِتابَةٌ خارِجَ المُعامَلَة وخارِجَ الصُندوق الصادِر، **ويَرفَعُها
        // كُلُّ مَن يَفتَح الصَفحَة بِلا قِراءَة** — زاحِفٌ أَو جالِبٌ
        // مُسبَق أَو أَداةُ تَحَقُّقٍ بَصَريّ. وصارَ نُقطَةً يُنادِيها
        // العَميلُ **بَعدَ** التَصيير (`realtime-nav.js`).
        //
        // و`RequireAuthApi` لا `RequireAuth`: المُنادي `fetch` لا نَموذَج،
        // فَالرَدُّ ‏401 أَصدَقُ مِن تَحويلٍ إلى صَفحَة دُخول لا يَراها أَحَد.
        app.MapPost("/{slug}/chats/{conversationId:guid}/read",
            async (string slug, Guid conversationId, HttpContext http,
                   IDocumentStore store) =>
        {
            var userId = http.UserId();

            await using var s = store.LightweightSession(slug);
            var conv = await s.LoadAsync<Conversation>(conversationId);
            if (conv is null) return Results.NotFound();
            if (conv.OwnerId != userId && conv.PartnerId != userId) return Forbidden();

            // نَفسُ الشَرطَين حَرفاً كَما كانا في الصَفحَة — والإيداعُ
            // مَشروطٌ بِتَبَدُّلٍ فِعليّ، فَنِداءٌ مُكَرَّرٌ لا يَكتُب.
            var changed = false;
            if (conv.OwnerId   == userId && conv.OwnerUnread   > 0) { conv.OwnerUnread   = 0; changed = true; }
            if (conv.PartnerId == userId && conv.PartnerUnread > 0) { conv.PartnerUnread = 0; changed = true; }
            if (changed) { s.Store(conv); await s.SaveChangesAsync(); }
            return Results.NoContent();
        }).DisableAntiforgery().RequireAuthApi();

        // ─── تَعليمُ الإشعارات مَقروءَة — أَمرٌ صَريح لا أَثَرٌ في عَرض ──
        // نَفسُ عِلَّةِ نُقطَةِ المُحادَثَة أَعلاه. و**نَفسُ الخَمسين
        // حَرفاً**: تُقرَأ بِنَفس التَرتيب والسَقف الَّذي تَعرِضُه
        // الصَفحَة، فَما يُعَلَّم مَقروءاً هُوَ بِالضَبط ما رَآه
        // صاحِبُه — لا كُلُّ ما في المَخزَن.
        app.MapPost("/{slug}/notifications/read",
            async (string slug, HttpContext http, IDocumentStore store) =>
        {
            var userId = http.UserId();

            await using var s = store.LightweightSession(slug);
            var shown = await s.Query<ACommerce.Kit.Notifications.Notification>()
                .Where(n => n.UserId == userId)
                .OrderByDescending(n => n.At).Take(50).ToListAsync();

            var unread = shown.Where(n => !n.IsRead).ToList();
            if (unread.Count == 0) return Results.NoContent();

            foreach (var n in unread) { n.IsRead = true; s.Store(n); }
            await s.SaveChangesAsync();
            return Results.NoContent();
        }).DisableAntiforgery().RequireAuthApi();

        // ─── Send chat message — gates: auth + terms ─────────────────────
        // boilerplate التَّوثيق المُتَكَرِّر استُبدِل بِـ .RequireAuth().RequireTerms()
        // — الـ filter يَكتُب userId إلى HttpContext.Items وَنَقرَأها هُنا.
        app.MapPost("/{slug}/chats/{conversationId:guid}/send",
            async (string slug, Guid conversationId, HttpContext http, HttpRequest req,
                   IDocumentStore store, L l,
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
                    Title = l.Format("notifications.chat_message.title", senderName),
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
                    l.Format("notifications.chat_message.title", senderName),
                    conv.LastMessage ?? "—",
                    url: $"/{slug}/chats/{conversationId}",
                    tag: $"chat-{conversationId}");
            return Results.Redirect(Link(req, slug, $"chats/{conversationId}"));
        }).DisableAntiforgery().RequireAuth().RequireTerms()
          .RequireStoreWritable();

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
                $"/admin/tenants/{slug}/roles", $"/admin/tenants/{slug}/roles", "/admin");
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

        // ─── Admin: باقَةُ المُستَأجِر في وَسايِل ───────────────────────
        // **الحارِسُ حارِسُ مُشرِفِ المَنَصَّة** — بِخِلاف نُقطَةِ
        // الاشتِراك المَحذوفَة أَمس (‏ADR-002 §٤ عَلَّلَت `TenantAdminGuard`
        // بِأَنّ «مَن يَقبِض هُوَ مَن يُقِرّ»). والقابِضُ الآنَ **وَسايِل**،
        // فَالحارِسُ عادَ إلى جارَتَيه: تَعريفُ الدَور والثيم. ولا
        // انحِرافَ يُعلَن لِأَنَّه لَم يَعُد.
        //
        // ولا دَورَةَ اعتِمادٍ هُنا: المُشرِفُ لا يَطلُب مِن نَفسِه
        // ويَعتَمِد لِنَفسِه. تَعيينٌ واحِدٌ يَخدِم التَمديدَ أَيضاً —
        // فِعلانِ لِشَيءٍ واحِدٍ يَنجَرِفانِ في التَحَقُّق.
        app.MapPost("/admin/tenants/{slug}/plan/save",
            async (string slug, HttpRequest req, IDocumentStore store,
                   Services.Incubator.StudioAuth auth,
                   Services.Audit.AuditWriter audit) =>
        {
            var decision = await Services.PlatformAdminGuard.EvaluateAsync(store, auth);
            if (!decision.Allowed) return Forbidden();
            await LogTenantConfigChangeAsync(audit, req, slug, auth,
                Services.Subscriptions.TenantPlanAdminService.SetAuditAction);

            var by = decision.User is { } pu ? $"studio · {pu.Id}" : "platform-admin";
            var f = ACommerce.Kit.Subscriptions.TenantPlanPolicy.ReadSetting(
                req.Form["plan"], req.Form["starts"], req.Form["expires"],
                req.Form["grace"], req.Form["price"], DateTime.UtcNow);

            await using var s = store.LightweightSession();
            var violations = Services.Subscriptions.TenantPlanAdminService.Set(
                s, slug, f.PlanId, f.StartsAt, f.ExpiresAt,
                f.GraceDays, f.Price, by, DateTime.UtcNow);
            if (violations.Count > 0)
                return Results.Redirect($"/admin/tenants/{slug}/plan" +
                    $"?err={Uri.EscapeDataString(violations[0].Code)}");
            await s.SaveChangesAsync();
            return Results.Redirect($"/admin/tenants/{slug}/plan?saved=1");
        }).DisableAntiforgery();

        app.MapPost("/admin/tenants/{slug}/plan/status",
            async (string slug, HttpRequest req, IDocumentStore store,
                   Services.Incubator.StudioAuth auth,
                   Services.Audit.AuditWriter audit) =>
        {
            var decision = await Services.PlatformAdminGuard.EvaluateAsync(store, auth);
            if (!decision.Allowed) return Forbidden();
            await LogTenantConfigChangeAsync(audit, req, slug, auth,
                Services.Subscriptions.TenantPlanAdminService.StopAuditAction);

            var by = decision.User is { } pu ? $"studio · {pu.Id}" : "platform-admin";
            var resume = req.Form["action"].ToString().Trim() == "resume";

            await using var s = store.LightweightSession();
            var plan = await s.LoadAsync<ACommerce.Kit.Subscriptions.TenantPlan>(slug);
            var ok = resume
                ? Services.Subscriptions.TenantPlanAdminService.Resume(s, plan, by, DateTime.UtcNow)
                : Services.Subscriptions.TenantPlanAdminService.Stop(s, plan, by, DateTime.UtcNow);
            if (!ok) return Results.Redirect($"/admin/tenants/{slug}/plan?err=tenant_plan_missing");
            await s.SaveChangesAsync();
            return Results.Redirect($"/admin/tenants/{slug}/plan?saved=1");
        }).DisableAntiforgery();

        // وتَعليماتُ التَحويلِ إلى وَسايِل — نَصٌّ واحِدٌ لِلمَنَصَّة
        // كُلِّها، يَراه رائِدُ الأَعمال في الاستوديو. حَرفِيَّةٌ في
        // الكود كانَت سَتَجعَل تَغييرَ آيبانٍ إصداراً.
        app.MapPost("/admin/transfer/save",
            async (HttpRequest req, IDocumentStore store,
                   Services.Incubator.StudioAuth auth,
                   Services.Audit.AuditWriter audit) =>
        {
            var decision = await Services.PlatformAdminGuard.EvaluateAsync(store, auth);
            if (!decision.Allowed) return Forbidden();
            await LogTenantConfigChangeAsync(audit, req, "-", auth,
                Services.Subscriptions.TenantPlanAdminService.SettingsAuditAction);

            var by = decision.User is { } pu ? $"studio · {pu.Id}" : "platform-admin";
            await using var s = store.LightweightSession();
            var settings = await s.LoadAsync<ACommerce.Kit.Subscriptions.PlatformSettings>(
                                ACommerce.Kit.Subscriptions.PlatformSettings.SingletonId)
                           ?? new ACommerce.Kit.Subscriptions.PlatformSettings();
            Services.Subscriptions.TenantPlanAdminService.SaveTransferInstructions(
                s, settings, req.Form["instructions"].ToString(), by, DateTime.UtcNow);
            await s.SaveChangesAsync();
            return Results.Redirect("/admin/transfer?saved=1");
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
                $"/admin/tenants/{slug}", $"/admin/tenants/{slug}/categories", "/admin");
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
                $"/admin/tenants/{slug}", $"/admin/tenants/{slug}/branding", "/admin");
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
            await LogTenantConfigChangeAsync(audit, req, slug, auth, PwaSaveService.AuditAction);

            await using var s = store.LightweightSession();
            var result = await PwaSaveService.SaveAsync(s, slug, TenantConfigSurface.ReadPwa(req));
            if (result.Ok) await s.SaveChangesAsync();

            return TenantConfigSurface.Outcome(result,
                $"/admin/tenants/{slug}/pwa", $"/admin/tenants/{slug}/pwa", "/admin");
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
            await LogTenantConfigChangeAsync(audit, req, slug, auth, RegionsSaveService.AuditAction);

            // جَلسَةٌ مَحصورَةٌ بِالمُستَأجِر: الوَثيقَةُ مُتَعَدِّدَةُ
            // الإيجار، والحَصرُ هُنا لا في نِداء الخِدمَة.
            await using var s = store.LightweightSession(slug);
            var result = await RegionsSaveService.SaveAsync(s, TenantConfigSurface.ReadRegions(req));
            if (result.Ok) await s.SaveChangesAsync();

            return TenantConfigSurface.Outcome(result,
                $"/admin/tenants/{slug}/regions", $"/admin/tenants/{slug}/regions", "/admin");
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
            await LogTenantConfigChangeAsync(audit, req, slug, auth, AttributesSaveService.AuditAction);

            var request = TenantConfigSurface.ReadAttributes(req);
            await using var s = store.LightweightSession(slug);
            var result = await AttributesSaveService.SaveAsync(s, request);
            if (result.Ok) await s.SaveChangesAsync();

            // النِطاقُ يَدخُل رابِطَ العَودَة حينَ يُحَلَّل، ويَسقُط
            // مِنه حينَ لا يُحَلَّل — وهذا هُوَ فَرقُ no_scope عَن
            // بَقِيَّة الرُموز.
            var page = $"/admin/tenants/{slug}/attributes" +
                       (request.Scope is { } id ? $"?scope={id}" : "");
            return TenantConfigSurface.Outcome(result, page, page, "/admin");
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
                res.Cookies.Append(StudioPromptCookie, Uri.EscapeDataString(prompt),
                    new CookieOptions { IsEssential = true, Path = "/",
                        Expires = DateTimeOffset.UtcNow.AddHours(2) });
            return Results.Redirect("/studio/auth");
        }).DisableAntiforgery();

        // ─── بابُ الاستوديو — عَبر القَناة المُسَجَّلَة، والغِيابُ إغلاق ──
        // ‏`IOtpChannel?` **اختِياريَّةٌ في التَوقيع** — نَفسُ شَكل نِقاط
        // المُستَأجِرين: خارِجَ التَطوير بِلا `Auth__Sms__Provider` لا
        // تُسَجَّلُ قَناةٌ أَصلاً، فَالإلزامُ يَرتَدُّ **500**. والرَفضُ
        // يُقال بِرِسالَةٍ مِن القامُوس.
        app.MapPost("/studio/auth/login", async (
            HttpRequest req, [FromServices] IOtpChannel? channel, CancellationToken ct) =>
        {
            var phone = req.Form["phone"].ToString().Trim();
            if (channel is null) return Results.Redirect(StudioDoorClosed(Services.Incubator.StudioAuthMethod.Phone));
            if (string.IsNullOrEmpty(phone)) return Results.Redirect("/studio/auth?err=phone");
            try { await Services.Incubator.StudioAuth.SendPhoneCodeAsync(channel, phone, ct); }
            catch (Exception) { return Results.Redirect("/studio/auth?err=send_failed"); }
            return Results.Redirect(
                $"/studio/auth?stage=verify&method=phone&phone={Uri.EscapeDataString(phone)}");
        }).DisableAntiforgery();

        // نَظيرُه لِلبَريد — وهُوَ ما لَم يَكُن مَوجوداً إطلاقاً: المَمنوحُ
        // بِـ`PLATFORM_ADMIN_EMAIL` كانَ مُهَيَّأً بِلا بابٍ يَبلُغُه.
        app.MapPost("/studio/auth/email/login", async (
            HttpRequest req, [FromServices] IEmailOtpChannel? channel, CancellationToken ct) =>
        {
            var email = EmailAddress.Normalize(req.Form["email"].ToString());
            if (channel is null) return Results.Redirect(StudioDoorClosed(Services.Incubator.StudioAuthMethod.Email));
            if (string.IsNullOrEmpty(email)) return Results.Redirect("/studio/auth?err=email");
            if (!EmailAddress.IsValid(email))
                return Results.Redirect("/studio/auth?err=email_invalid");
            try { await Services.Incubator.StudioAuth.SendEmailCodeAsync(channel, email, ct); }
            catch (Exception) { return Results.Redirect("/studio/auth?err=send_failed"); }
            return Results.Redirect(
                $"/studio/auth?stage=verify&method=email&email={Uri.EscapeDataString(email)}");
        }).DisableAntiforgery();

        app.MapPost("/studio/auth/verify", async (
            HttpRequest req, HttpResponse res, IDocumentStore store,
            IServiceScopeFactory scopeFactory,
            Services.Incubator.FeasibilityAnalysisService incubator) =>
        {
            var method = Services.Incubator.StudioAuthDoor.Parse(req.Form["method"].ToString())
                         ?? Services.Incubator.StudioAuthMethod.Phone;
            var field = Services.Incubator.StudioAuthDoor.Slug(method);
            var subject = Services.Incubator.StudioAuth.NormalizeSubject(
                method, req.Form[field].ToString());
            var code  = req.Form["code"].ToString().Trim();
            var back  = $"/studio/auth?stage=verify&method={field}" +
                        $"&{field}={Uri.EscapeDataString(subject)}";
            var user = await Services.Incubator.StudioAuth.VerifyAsync(
                store, res, method, subject, code);
            if (user is null) return Results.Redirect($"{back}&err=code");

            // اِربِط المَتاجِر اليَتيمَة بِأَوَّل مُستَخدِم (لَو هذا أَوَّل تَسجيل).
            await Services.Incubator.StudioOwnershipSeeder.RunAsync(store);

            // مُطالَبَة مُعَلَّقَة؟ أَنشِئ جَلسَة وشَغِّل التَّحليل في الخَلفِيَّة.
            // الجِسمُ في `ResumeStudioPromptAsync` — ويُنادى مِن نُقطَةِ
            // قَبولِ الشُروطِ أَيضاً، وذاكَ ما رَفَعَ الـ405.
            return await ResumeStudioPromptAsync(
                       req, res, user.Id, user.FullName,
                       store, scopeFactory, incubator, checkConsent: true)
                   ?? Results.Redirect("/studio");
        }).DisableAntiforgery();

        app.MapPost("/studio/logout", (HttpResponse res) =>
        {
            Services.Incubator.StudioAuth.DeleteCookie(res);
            return Results.Redirect("/");
        }).DisableAntiforgery();

        // قَبول الشُروط — يَحفَظ ConsentRecord، ثُمَّ **يَستَأنِف
        // المُطالَبَةَ المُعَلَّقَةَ إن وُجِدَت**، وإلّا حَوَّلَ لِـ
        // returnUrl.
        //
        // ‏**والاستِئنافُ هُنا لا إعادَةُ تَحويل**: كانَ
        // `/studio/auth/verify` يُرسِل إلى هذِه الصَفحَةِ بِـ
        // `returnUrl=/studio/auth/verify`، وتِلكَ النُقطَةُ `POST` فَقَط —
        // فَكانَ القَبولُ يَنتَهي بِـ**‏405** لِكُلّ مُستَخدِمٍ جَديدٍ
        // جاءَ بِفِكرَةٍ مِن صَفحَة الهُبوط.
        app.MapPost("/studio/consent/accept", async (
            HttpRequest req, HttpResponse res, IDocumentStore store,
            IServiceScopeFactory scopeFactory,
            Services.Incubator.FeasibilityAnalysisService incubator,
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

            // الشُروطُ قُبِلَت الآن، فَلا يُعادُ فَحصُها
            // (`checkConsent: false`) — وإلّا دارَت الصَفحَةُ على نَفسِها.
            return await ResumeStudioPromptAsync(
                       req, res, auth.UserId!.Value, auth.UserName ?? "",
                       store, scopeFactory, incubator, checkConsent: false)
                   ?? Results.Redirect(returnUrl);
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

        // ─── اختِيارُ دَرَجَة — والمَدفوعَةُ لا تُمنَح ذاتِيّاً ──────
        //
        // **ما كانَ هُنا (‏حَتّى 2026-08-30)**: يُنادي
        // `payments.CreateSubscriptionAsync` — وجَوابُ المُحاكي «نَجَحَ»
        // دائِماً — ثُمَّ يَكتُب `u.Tier = tier` ويَحفَظ. أَي **تَرقِيَةٌ
        // إلى `scale` (‏999 ريالاً) بِنَقرَةٍ وبِلا دَفع**، ومَعَها
        // حُدودُ `int.MaxValue` عَلى مِفتاحِ نَموذَجِ لُغَةِ المالِك.
        //
        // **والقَرارُ لَيسَ جَديداً**: هُوَ بِحَرفِه ما عالَجَته ‏ADR-003
        // في `POST /{slug}/plans/{planId}/subscribe` — «باقَةٌ
        // `Price > 0` لا تُمنَح ذاتِيّاً، والمَجّانِيَّةُ تَبقى
        // ذاتِيَّة». فَنَفسُ الدالَّةِ ونَفسُ رَمزِ الخَرقِ ونَفسُ
        // المَعجَم، لا أُنبوبٌ ثانٍ (القاعِدَة ٨).
        //
        // **والحارِسُ أَوَّلُ سَطرٍ بَعدَ الهُوِيَّة، قَبلَ أَيّ
        // تَحميلٍ أَو نِداء** (القاعِدَة ٦): التَخويلُ يَسبِق تَحَقُّقَ
        // الحُقول، وإلّا صارَ خَطَأُ التَحَقُّقِ قِناعاً لِلثَغرَة.
        //
        // **ولا مُزَوِّدَ دَفعٍ في هذا الجِسمِ إطلاقاً**: ما يُمنَح هُنا
        // مَجّانيٌّ بِتَعريفِه، ولا يُقبَض عَلَيه شَيء. ونِداءُ مُزَوِّدٍ
        // لا يُقارَن جَوابُه بِقَبضٍ حَقيقيٍّ **هُوَ الثَغرَةُ نَفسُها**.
        app.MapPost("/studio/billing/select", async (
            string tier, HttpRequest req, IDocumentStore store,
            Services.Incubator.StudioAuth auth,
            Services.Audit.AuditWriter audit) =>
        {
            auth.Load();
            if (!auth.IsAuthenticated) return Results.Redirect("/studio/auth");

            var refusal = Services.Incubator.StudioTierPurchase.Refuse(tier);
            if (refusal is not null)
                return Results.Redirect($"/studio/billing?err={refusal}");

            var limits = Services.Incubator.TierCatalog.For(tier);

            await using var s = store.LightweightSession(Services.Incubator.StudioAuth.Tenant);
            var u = await s.LoadAsync<Services.Incubator.StudioUser>(auth.UserId!.Value);
            if (u is null) return Results.Redirect("/studio/billing");

            u.Tier = tier;
            s.Store(u);
            await s.SaveChangesAsync();

            await audit.WriteAsync(Services.Audit.AuditWriter.PlatformScope,
                u.Id, u.FullName,
                "billing.tier.self_grant", "studio_tier", tier,
                note: $"tier={tier} amount={limits.MonthlyPriceSar}",
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

            // ‏`Export` لا `refine`: هذا **حَجبُ ميزَةٍ لا خَرقُ حِصَّة**،
            // وكانَت الشاشَةُ تَقول «بَلَغتَ حَدَّ التَحسينات» لِمَن لَم
            // يَبلُغه — سَطرٌ يَكذِب.
            var (_, limits) = await tier.ReadWithLimitsAsync(auth.UserId!.Value);
            if (!limits.AllowExport)
                return Results.Redirect(
                    $"/studio/s/{id}?upgrade={Services.Incubator.StudioUpgradeReason.Export}");

            var bytes = exporter.Export(session);
            return Results.File(bytes,
                contentType: "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                fileDownloadName: $"feasibility-{id:N}.xlsx");
        });

        // ═══ التَخارُج — حَقيبَةُ بَياناتِ المَتجَرِ كامِلَةً (‏ADR-020) ═══
        //
        // **‏`POST` لا `GET`، والفَرقُ مَفروضٌ بِفاحِصٍ قائِم**:
        // ‏`WriteEndpointGuardTests.No_get_endpoint_writes_state` يُحمِرُّ
        // أَيَّ `MapGet` فيه رَمزُ كِتابَة — والتَصديرُ يَكتُبُ قَيدَ
        // تَدقيق. والفِعلُ المُعلَنُ يُطابِقُ الواقِع.
        //
        // **والتَخويلُ أَوَّلُ سَطرٍ بَعدَ الهُوِيَّة** (القاعِدَة ٦)،
        // ودالَّتُه هي نَفسُها الَّتي تَقرَؤُها الشاشَةُ والخِدمَة —
        // فَثَلاثَةُ مَواضِعَ لا تَنجَرِف.
        // **والمَنطِقُ كُلُّه في الخِدمَة**: الجِسمُ يَحُلُّ الهُوِيَّةَ
        // والمُستَأجِرَ ويَرُدّ، ولا يَجمَعُ ولا يُدَقِّقُ ولا يَبني
        // أَرشيفاً — فَما يَقَع في جِسمِ نُقطَةٍ لا يُختَبَرُ إلّا
        // بِطَلَبِ HTTP كامِل.
        app.MapPost("/studio/apps/{slug}/export.zip", async (
            string slug, HttpRequest req,
            Services.Incubator.StudioAuth auth,
            Services.Queries.TenantDirectory tenants,
            Services.Export.TenantExportService export,
            CancellationToken ct) =>
        {
            auth.Load();
            if (!auth.IsAuthenticated) return Results.Redirect("/studio/auth");

            var result = await export.ProduceAsync(
                slug, await tenants.FindAsync(slug, ct), auth.UserId, auth.UserName,
                req.HttpContext.Connection.RemoteIpAddress?.ToString(), ct);

            return result.Refusal != Services.Export.TenantExportRefusal.None
                ? Results.Redirect($"/studio/apps/{slug}/export?err=" +
                      Services.Export.TenantExportAuthorization.Code(result.Refusal))
                : Results.File(result.Zip!, "application/zip", result.FileName);
        }).DisableAntiforgery();

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
                return Results.Redirect($"/studio/s/{id}?upgrade={gate.BreachCode}");
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
        }).DisableAntiforgery()
          .RequireStoreWritable();

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
        }).DisableAntiforgery()
          .RequireStoreWritable();

        app.MapPost("/{slug}/cart/clear", async (string slug, HttpRequest req, IDocumentStore store) =>
        {
            var parsed = AuthHandlers.ParseToken(AuthSession.ResolveToken(req, slug));
            if (parsed is null) return Results.Redirect(Link(req, slug, "login"));
            var (userId, _, _) = parsed.Value;
            await using var s = store.LightweightSession(slug);
            s.Delete<ACommerce.Kit.Cart.Cart>(userId);
            await s.SaveChangesAsync();
            return Results.Redirect(Link(req, slug, "cart"));
        }).DisableAntiforgery()
          .RequireStoreWritable();

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

            // ─── طَريقَةُ الدَفع — قَرارٌ واحِدٌ تَقرَؤُه الشاشَةُ أَيضاً ──
            // كانَ الشَرطُ هُنا `pay != «cod»` والاستِمارَةُ تُرسِل
            // `card|cash` — فَلا يَكذِبُ أَبَداً، وكُلُّ طَلَبٍ يَستَدعي
            // المُزَوِّد، **والدَفعُ عِندَ الاستِلامِ أَوَّلُها**.
            //
            // وبِطاقَةٌ في مَتجَرٍ لا يَقبِضُ **تُرَدُّ ولا تُبَدَّلُ
            // صامِتَةً**: تَحويلُ طَلَبِ بِطاقَةٍ إلى نَقدٍ بِلا كَلِمَةٍ
            // تَبديلُ عَقدٍ مِن تَحتِ المُشتَري. والسَلَّةُ تَبقى.
            var (payMethod, payRefusal) = Services.Deals.CheckoutPaymentPolicy.Decide(
                pay, tenant?.PaymentProviderConfigured ?? false);
            if (payRefusal is not null)
                return Results.Redirect(Link(req, slug, Services.Deals.CheckoutPaymentPolicy
                    .RefusalPath(payRefusal, name, phone, addr)));

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
                        // المُطَبَّعَةُ لا الخام — فَما يُقرَأُ مِن
                        // الصَفقَةِ بَعدَ سَنَةٍ قيمَةٌ مِن المَعجَم.
                        ["pay_method"] = payMethod
                    });
                if (firstDealId == Guid.Empty) firstDealId = deal.Id;
                if (listing.Attributes.TryGetValue("owner_id", out var oid))
                    await deals.AttachRefAsync(slug, deal.Id, "listing_owner", oid);

                // اِحجِز المَبلَغ عَبر مُزَوِّد الدَّفع (Authorize، لا
                // capture بَعد — يَتِمّ الـ capture عِندَ تَأكيد البائِع).
                // **والنَقدُ عِندَ الاستِلامِ لا يَستَدعي المُزَوِّد** —
                // والقَرارُ مِن الدالَّةِ النَقِيَّةِ لا مِن حَرفِيَّةٍ
                // في الجِسم.
                // idempotency-key بِـ dealId يَمنَع تَكرار الـ authorize
                // لَو أَعادَ المُستَخدِم submit بِنَفس النَّموذَج.
                if (Services.Deals.CheckoutPaymentPolicy.CallsProvider(payMethod))
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
        }).DisableAntiforgery()
          .RequireStoreWritable();

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
        }).DisableAntiforgery()
          .RequireStoreWritable();

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
        }).DisableAntiforgery()
          .RequireStoreWritable();

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
        }).DisableAntiforgery()
          .RequireStoreWritable();

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
        }).DisableAntiforgery()
          .RequireStoreWritable();

        app.MapPost("/{slug}/deals/{id:guid}/cancel",
            async (string slug, Guid id, HttpRequest req, IDocumentStore store,
                   Services.Deals.DealsService deals) =>
        {
            var parsed = AuthHandlers.ParseToken(AuthSession.ResolveToken(req, slug));
            if (parsed is null) return Results.Redirect(Link(req, slug, "login"));
            var (userId, _, _) = parsed.Value;
            var userName = AuthSession.ResolveUserName(req, slug) ?? "مُستَخدِم";
            var reason = req.Form["reason"].ToString().Trim();
            // ‏§١١٫٦ أُغلِقَت: الجَلسَةُ الصالِحَةُ لَم تَعُد كافِيَةً —
            // الحارِسُ في تَوقيعِ الخِدمَة، وهذا المَسارُ يُصَرِّح
            // بِأَنَّه **لَيسَ** مُشرِفَ مَتجَر. مُستَخدِمُ المَتجَر
            // يُلغي صَفقَتَه هُوَ لا صَفقَةَ غَيرِه.
            var res = await deals.CancelAsync(slug, id, new Services.Deals.DealCanceller(userId, userName, IsStoreAdmin: false),
                string.IsNullOrEmpty(reason) ? "إلغاء" : reason);
            return Results.Redirect(Link(req, slug, $"deals/{id}" + (res.Ok ? "" : $"?err={res.Violation!.Code}")));
        }).DisableAntiforgery()
          .RequireStoreWritable();

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
        }).DisableAntiforgery()
          .RequireStoreWritable();

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
            // ‏`StudioOwnsAsync` أَعلاه أَثبَتَ مِلكِيَّةَ المَتجَر —
            // فَالسُلطَةُ تُقال هُنا صَراحَةً بَدَلَ أَن تُستَنتَج.
            var res = await deals.CancelAsync(slug, id,
                new Services.Deals.DealCanceller(auth.UserId!.Value, "مالِك التَّطبيق", IsStoreAdmin: true), reason);
            if (res.Ok) await audit.WriteAsync(slug, auth.UserId, "مالِك التَّطبيق",
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

        // ─── أَوَّلُ كاتِبٍ لِـ`Tenant.PaymentProviderConfigured` ─────────
        //
        // كانَ الحَقلُ بِصِفرِ كاتِبٍ وقارِئَين مُنذُ ‏ADR-003 — مُستَهلِكٌ
        // مَبنِيٌّ يَنتَظِرُ كاتِبَه. والكاتِبُ **لَيسَ زِرَّ إدارَةٍ
        // يَقلِبُه**، بَل سُؤالٌ يُجابُ مِن الرَبطِ نَفسِه: أَيُوجَدُ
        // رابِطُ دَفعٍ مَملوءٌ يَنقُرُه الزَبون؟ فَرَبطٌ فَعّالٌ بِلا
        // رابِطٍ **لا يَقلِبُه**، وسَحبُ الرَبطِ يُعيدُه `false` فَتَختَفي
        // الباقاتُ المَدفوعَةُ في نَفسِ اللَحظَة.
        static async Task SyncPaymentProviderFlagAsync(
            IDocumentStore store, Services.TenantProviderService providers, string slug)
        {
            var collects = (await providers.ForAsync(slug)).CollectsMoney;

            await using var s = store.LightweightSession();
            var t = await s.LoadAsync<ACommerce.Kit.Tenants.Tenant>(slug);
            if (t is null || t.PaymentProviderConfigured == collects) return;

            t.PaymentProviderConfigured = collects;
            s.Store(t);
            await s.SaveChangesAsync();
        }


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
                $"/studio/apps/{slug}/branding", $"/studio/apps/{slug}/branding", "/studio");
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
                $"/studio/apps/{slug}/categories", $"/studio/apps/{slug}/categories", "/studio");
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
                $"/studio/apps/{slug}/roles", $"/studio/apps/{slug}/roles", "/studio");
        }).DisableAntiforgery();

        app.MapPost("/studio/apps/{slug}/regions/save", async (
            string slug, HttpRequest req, IDocumentStore store,
            Services.Incubator.StudioAuth auth,
            Services.Audit.AuditWriter audit) =>
        {
            if (!await StudioOwnsAsync(store, auth, slug)) return Results.Redirect("/studio");
            await LogTenantConfigChangeAsync(audit, req, slug, auth, RegionsSaveService.AuditAction);

            await using var s = store.LightweightSession(slug);
            var result = await RegionsSaveService.SaveAsync(s, TenantConfigSurface.ReadRegions(req));
            if (result.Ok) await s.SaveChangesAsync();

            return TenantConfigSurface.Outcome(result,
                $"/studio/apps/{slug}/regions", $"/studio/apps/{slug}/regions", "/studio");
        }).DisableAntiforgery();

        // ─── Studio: save PWA apps (per-role name + icon) ───────────────
        // نَفس مَنطِق /admin/tenants/{slug}/pwa/save لَكِن داخِل واجِهَة الـ
        // Studio بِحارِس المِلكِيَّة، والتَّوجيهات إلى مَسارات /studio/apps/.
        app.MapPost("/studio/apps/{slug}/pwa/save", async (
            string slug, HttpRequest req, IDocumentStore store,
            Services.Incubator.StudioAuth auth,
            Services.Audit.AuditWriter audit) =>
        {
            if (!await StudioOwnsAsync(store, auth, slug)) return Results.Redirect("/studio");
            await LogTenantConfigChangeAsync(audit, req, slug, auth, PwaSaveService.AuditAction);

            await using var s = store.LightweightSession();
            var result = await PwaSaveService.SaveAsync(s, slug, TenantConfigSurface.ReadPwa(req));
            if (result.Ok) await s.SaveChangesAsync();

            return TenantConfigSurface.Outcome(result,
                $"/studio/apps/{slug}/pwa", $"/studio/apps/{slug}/pwa", "/studio");
        }).DisableAntiforgery();

        // ─── Studio: save attribute definitions for a scope ─────────────
        // نَفس مَنطِق /admin/tenants/{slug}/attributes/save لَكِن داخِل واجِهَة
        // الـ Studio بِحارِس المِلكِيَّة، والتَّوجيهات إلى مَسارات /studio/apps/.
        app.MapPost("/studio/apps/{slug}/attributes/save", async (
            string slug, HttpRequest req, IDocumentStore store,
            Services.Incubator.StudioAuth auth,
            Services.Audit.AuditWriter audit) =>
        {
            if (!await StudioOwnsAsync(store, auth, slug)) return Results.Redirect("/studio");
            await LogTenantConfigChangeAsync(audit, req, slug, auth, AttributesSaveService.AuditAction);

            var request = TenantConfigSurface.ReadAttributes(req);
            await using var s = store.LightweightSession(slug);
            var result = await AttributesSaveService.SaveAsync(s, request);
            if (result.Ok) await s.SaveChangesAsync();

            var page = $"/studio/apps/{slug}/attributes" +
                       (request.Scope is { } id ? $"?scope={id}" : "");
            return TenantConfigSurface.Outcome(result, page, page, "/studio");
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
                return Results.Redirect($"/studio/s/{id}?upgrade={gate.BreachCode}");

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

            // القَناةُ **تُشتَقُّ مِن المُهَيَّإ في هذِه النُسخَة**، ولا
            // تُكتَبُ ثابِتَةً. كانَت `"phone"` في المَصنَع، وعَلى نُسخَةٍ
            // بِالبَريدِ وَحدَه (تَوصِيَة `docs/DEPLOY.md` §٢·ب) كانَ ذلكَ
            // يُنتِج مَتجَراً لا يَدخُلُه أَحَد. ولا قَناةَ ⇒ **لا مَتجَرَ
            // بِبابٍ مُغلَق**: رِسالَةٌ صَريحَةٌ يَقرَؤُها العَميل.
            var channel = Services.Incubator.TenantAuthChannelDoor.Default(
                Services.Incubator.TenantAuthChannelDoor.OfferedIn(http.RequestServices));
            if (channel is null)
                return Results.Redirect(
                    $"/studio/s/{id}?build_err={Services.Incubator.TenantAuthChannelDoor.NoChannel}");

            var sector = session.Answers.TryGetValue("sector", out var sec) ? sec : "";
            await factory.CreateAsync(slug, name, color, tagLine, city,
                session.SuggestedPattern, sector, channel, ownerId, id);
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
        //
        // ═══ ‏2026-08-30 — والحارِسُ الأَوَّلُ لَم يَكُنِ الحارِسَ كُلَّه ═══
        //
        // أُضيفَت جَلسَةُ الاستوديو والمِلكِيَّةُ ولَم تُضَف <b>بَوّابَةُ
        // الحِصَّة</b>: قائِمَةُ الوُسَطاءِ لا تَحوي
        // <c>StudioTierService</c> إطلاقاً. فَصاحِبُ دِراسَةٍ عَلى
        // <c>spark</c> (تَحليلٌ واحِدٌ شَهرِيّاً) يَنقُر «إعادَة»
        // بِلا سَقف، وكُلُّ نَقرَةٍ <b>نِداءا</b> نَموذَجِ لُغَةٍ بِـ
        // <c>MaxTokens: 8000</c> عَلى <b>مِفتاحِ المالِك</b>.
        //
        // والعِلاجُ بَندان لا بَند، لِأَنّ البَوّابَةَ وَحدَها فَحصٌ
        // ثُمَّ إطلاقٌ بِنافِذَةٍ بَينَهُما:
        //   ‏١. <c>CheckAnalyzeAsync</c> — نَفسُ بَوّابَةِ
        //      `ResumeStudioPromptAsync`، ونَفسُ شَكلِ `/refine`.
        //   ‏٢. <c>TryClaimAnalysisAsync</c> — حَجزٌ ذَرِّيٌّ بِمِفتاحِ
        //      فَرادَةٍ في Postgres، فَخَمسونَ طَلَباً عَلى نَفسِ
        //      المُعَرِّفِ يَفوزُ مِنها واحِد.
        //
        // <b>والتَرتيبُ جُزءٌ مِنَ العِلاج</b>: البَوّابَةُ تَسبِقُ
        // الحَجز (فَلا يُقلَبُ شَيءٌ لِمَن تَجاوَزَ حِصَّتَه)، والحَجزُ
        // يَسبِقُ العَدّ (فَخاسِرُ السِباقِ لا يُنفِقُ حِصَّةً)،
        // والعَدُّ يَسبِقُ الإطلاق.
        app.MapPost("/studio/s/{id:guid}/analyze", async (
            Guid id, IServiceScopeFactory scopeFactory,
            Services.Incubator.StudioAuth auth,
            Services.Incubator.StudioTierService tier,
            Services.Incubator.FeasibilityAnalysisService svc) =>
        {
            auth.Load();
            if (!auth.IsAuthenticated) return Results.Redirect("/studio/auth");

            var session = await svc.LoadAsync(id);
            if (session is null || session.OwnerUserId != auth.UserId!.Value)
                return Results.Redirect("/studio");

            var gate = await tier.CheckAnalyzeAsync(auth.UserId!.Value);
            if (!gate.Allowed)
                return Results.Redirect($"/studio/s/{id}?upgrade={gate.BreachCode}");

            // الحَجزُ والتَشغيلُ لا يَفتَرِقان. وكُلُّ ما لَيسَ
            // `Claimed` يَعني «لا تُطلِق» — والوِجهَةُ صَفحَةُ
            // الدِراسَةِ نَفسُها، وهي تَقولُ «التَحليلُ جارٍ» بِحالَتِها
            // لا بِرِسالَةٍ ثانِيَة. فَالرَفضُ يُرى ولا يُبتلَع.
            var claim = await svc.TryClaimAnalysisAsync(id);
            if (claim != Services.Incubator.FeasibilityAnalysisService.ClaimOutcome.Claimed)
                return Results.Redirect($"/studio/s/{id}");

            await tier.RecordAnalysisAsync(auth.UserId!.Value);
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

            // الحَجزُ يَقلِبُ الحالَةَ فَوراً (مُتَزامِناً) لِتَعرِضَ
            // صَفحَةُ الدِراسَةِ المُؤَشِّر، ثُمَّ يُشَغَّلُ التَحليلُ
            // الطَويلُ في الخَلفِيَّةِ بِنِطاقِ DI جَديد.
            //
            // <b>ولِماذا يُحجَزُ مَسارُ المُشرِفِ أَيضاً وهُوَ
            // مِفتاحُه</b>: المِفتاحُ مِفتاحُه، والفاتورَةُ فاتورَتُه —
            // ولِذلك لا بَوّابَةَ حِصَّةٍ هُنا. لكِنَّ نَقرَتَينِ
            // مُتَتالِيَتَينِ عَلى زِرٍّ لا يَستَجيبُ فَوراً كانَتا
            // تُشَغِّلانِ تَحليلَينِ عَلى نَفسِ الدِراسَة — وذاكَ
            // إنفاقٌ مُضاعَفٌ بِلا خَصمٍ يَستَفيدُ مِنه.
            if (await svc.TryClaimAnalysisAsync(id)
                == Services.Incubator.FeasibilityAnalysisService.ClaimOutcome.Claimed)
            {
                _ = Task.Run(async () =>
                {
                    using var scope = scopeFactory.CreateScope();
                    var bg = scope.ServiceProvider
                        .GetRequiredService<Services.Incubator.FeasibilityAnalysisService>();
                    try { await bg.RunAnalysisAsync(id); }
                    catch { /* الحالة تبقى Analyzing؛ تظهر مهلة في الواجهة */ }
                });
            }
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

        // ─── مَفاتيحُ الـAPI — الإصدارُ والإبطالُ مِن الاستوديو ──────────
        //
        // <b>ولِماذا هُنا لا تَحتَ `/api/v1/keys`</b> كَما وَصَفَت
        // ‏§٩: مِفتاحٌ يُصدِرُ مِفتاحاً يَفتَرِض مِفتاحاً قائِماً،
        // والأَوَّلُ لا يُوجَد. فَالإصدارُ يَلزَمُه اعتِمادٌ مِن
        // صِنفٍ آخَر — جَلسَةُ الاستوديو ومِلكِيَّةُ المَتجَر — ونُقطَةٌ
        // بِحارِسٍ مُختَلِف تَحتَ `/api/v1` تَكسِر العَقدَ الَّذي
        // يَحرُسُه الفاحِصان (‏حارِسٌ واحِد، JSON، بِلا تَحويل).
        // والانحِرافُ مَكتوبٌ في `docs/API-SURFACE-DESIGN.md`.
        //
        // <b>ويُبلَغ بِالنَقر</b> (القاعِدَة ١٢):
        //   /studio ← لَوحَةُ التَطبيق ← «مَفاتيح الـAPI» ← إصدار.
        app.MapPost("/studio/apps/{slug}/keys/create", async (
            string slug, HttpRequest req, IDocumentStore store,
            Services.Incubator.StudioAuth auth,
            Services.Api.ApiKeyService keys,
            Services.Api.ApiKeyRevealStore reveal,
            Services.Audit.AuditWriter audit) =>
        {
            if (!await StudioOwnsAsync(store, auth, slug)) return Results.Redirect("/studio");

            var request = Services.Api.ApiKeySurface.Read(req);
            var violations = Services.Api.ApiKeyValidator.Validate(request);
            if (violations.Count > 0)
                return Results.Redirect($"/studio/apps/{slug}/keys?err={violations[0].Code}");

            var issued = await keys.IssueAsync(slug, request, auth.UserId!.Value);
            // السِرُّ يُعرَض مَرَّةً — في وِعاءٍ لا في المَسار: الطَلَبُ
            // يُكتَب في اللوغ كامِلاً، وسِرٌّ في المَسار سِرٌّ في مِلَفّ.
            reveal.Stash(slug, issued.Key.Id, issued.Presented);
            await audit.WriteAsync(slug, auth.UserId, "مالِك التَّطبيق",
                "apikey.issue", "apikey", issued.Key.Id, note: issued.Key.Name,
                ip: req.HttpContext.Connection.RemoteIpAddress?.ToString());
            return Results.Redirect($"/studio/apps/{slug}/keys?issued={issued.Key.Id}");
        }).DisableAntiforgery();

        app.MapPost("/studio/apps/{slug}/keys/{keyId}/revoke", async (
            string slug, string keyId, HttpRequest req, IDocumentStore store,
            Services.Incubator.StudioAuth auth,
            Services.Api.ApiKeyService keys,
            Services.Audit.AuditWriter audit) =>
        {
            if (!await StudioOwnsAsync(store, auth, slug)) return Results.Redirect("/studio");

            var ok = await keys.RevokeAsync(slug, keyId);
            await audit.WriteAsync(slug, auth.UserId, "مالِك التَّطبيق",
                "apikey.revoke", "apikey", keyId,
                ip: req.HttpContext.Connection.RemoteIpAddress?.ToString());
            return Results.Redirect(
                $"/studio/apps/{slug}/keys" + (ok ? "" : "?err=key_not_found"));
        }).DisableAntiforgery();


        // ─── تَكامُلاتُ المَتجَر — رَبطُ مُزَوِّدٍ وسَحبُه ────────────────
        //
        // <b>يُبلَغ بِالنَقر</b> (القاعِدَة ١٢):
        //   /studio ← بِطاقَةُ التَطبيق ← «التَكامُلات» ← اختَر مُزَوِّداً ← رَبط.
        //
        // <b>والحارِسُ يَسبِقُ قِراءَةَ أَوَّلِ حَقل</b> (القاعِدَة ٦):
        // التَخويلُ قَبلَ التَحَقُّق، وإلّا صارَ خَطَأُ التَحَقُّقِ قِناعاً
        // لِلثَغرَة. و`TenantAdminGuard` لا `StudioOwnsAsync` لِأَنّ
        // التَكامُلَ إعدادُ مَتجَرٍ يُدارُ أَيضاً مِن داخِلِه بِصَلاحِيَّةِ
        // `tenant.manage`.
        //
        // <b>و`platform_key` يُعلِنُ حارِسَه فَوقَ الحارِس</b>: مُزَوِّدٌ
        // يَصرِفُ مِن جَيبِنا لا يَربِطُه صاحِبُ مَتجَر، والسُؤالُ يَقَع
        // قَبلَ الرَدّ.
        //
        // والقَرارُ نَفسُه في `ProviderBindSurface` — نَظيرُ
        // `ApiKeySurface` حَرفاً: جِسمُ النُقطَةِ لا يُختَبَر.
        app.MapPost("/studio/apps/{slug}/providers/bind", async (
            string slug, HttpRequest req, IDocumentStore store,
            Services.Incubator.StudioAuth auth,
            Services.TenantProviderService providers,
            Services.Audit.AuditWriter audit) =>
        {
            if (!await Services.TenantAdminGuard.CanAdministerAsync(store, auth, req, slug))
                return Results.Redirect("/studio");

            var read = Services.Providers.ProviderBindSurface.FromForm(req);
            if (read.NeedsPlatformAdmin &&
                !(await Services.PlatformAdminGuard.EvaluateAsync(store, auth)).Allowed)
                return Forbidden();
            if (read.RefusalCode is not null)
                return Results.Redirect($"/studio/apps/{slug}/providers?err={read.RefusalCode}");

            var before = Services.Providers.ProviderBindSurface.AuditLine(
                await providers.CurrentAsync(slug, read.Capability));
            var bound = await providers.BindProviderAsync(
                slug, read.Capability, read.ProviderSlug, read.Values,
                auth.UserId?.ToString() ?? "");
            await SyncPaymentProviderFlagAsync(store, providers, slug);

            await audit.WriteAsync(slug, auth.UserId, "مالِك التَّطبيق",
                "provider.bind", "provider", read.Capability, note: read.ProviderSlug,
                ip: req.HttpContext.Connection.RemoteIpAddress?.ToString(),
                before: before,
                after: Services.Providers.ProviderBindSurface.AuditLine(bound));

            return Results.Redirect($"/studio/apps/{slug}/providers?bound={read.Capability}");
        }).DisableAntiforgery();

        // السَحبُ يُوقِفُ **الآن** — ولِذلك لا يَمُرّ بِدَورَةِ اعتِماد.
        app.MapPost("/studio/apps/{slug}/providers/{capability}/revoke", async (
            string slug, string capability, HttpRequest req, IDocumentStore store,
            Services.Incubator.StudioAuth auth,
            Services.TenantProviderService providers,
            Services.Audit.AuditWriter audit) =>
        {
            if (!await Services.TenantAdminGuard.CanAdministerAsync(store, auth, req, slug))
                return Results.Redirect("/studio");

            var before = Services.Providers.ProviderBindSurface.AuditLine(
                await providers.CurrentAsync(slug, capability));
            var revoked = await providers.RevokeAsync(slug, capability);
            if (revoked is null)
                return Results.Redirect($"/studio/apps/{slug}/providers?err=binding_not_found");

            await SyncPaymentProviderFlagAsync(store, providers, slug);

            await audit.WriteAsync(slug, auth.UserId, "مالِك التَّطبيق",
                "provider.revoke", "provider", capability, note: revoked.ProviderSlug,
                ip: req.HttpContext.Connection.RemoteIpAddress?.ToString(),
                before: before,
                after: Services.Providers.ProviderBindSurface.AuditLine(revoked));

            return Results.Redirect($"/studio/apps/{slug}/providers?revoked={capability}");
        }).DisableAntiforgery();

        // ─── سَطحُ الـAPI ────────────────────────────────────────────────
        // مِلَفٌّ مُنفَصِل — نِطاقُ الفاحِصَين ٩ و‏١٠ نَصِّيّ بِلا لَبس.
        app.MapApiV1();

        // ─── فَوتَرَةُ PayPal ────────────────────────────────────────────
        // مِلَفٌّ مُنفَصِلٌ لِنَفس السَبَب: نِطاقُ المُراجَعَةِ وسَقفُ
        // النَزيفِ يُقاسانِ لَه وَحدَه. ونُقطَةُ الرِسالَةِ **بِلا
        // مُستَأجِر** — `api` مَحجوزَةٌ في وَسيطِ المُستَأجِر.
        app.MapPayPalBilling();

        // ─── فَوتَرَةُ Paddle ───────────────────────────────────────────
        // **مُزَوِّدٌ ثانٍ بِجِوارِ الأَوَّلِ لا بَدَلاً مِنه**: ‏PayPal
        // تَطلُب مِن الدافِعِ حِسابَ PayPal، وPaddle تاجِرُ تَسجيلٍ
        // تَقبَل البِطاقَةَ مُباشَرَةً. ومِلَفٌّ مُنفَصِلٌ لِنَفس
        // السَبَبِ حَرفاً، ونُقطَةُ الرِسالَةِ **بِلا مُستَأجِر**.
        app.MapPaddleBilling();

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

    /// <summary>رابِطُ الرَفض حينَ لا قَناةَ مُسَجَّلَةٌ لِطَريقَةِ دُخولِ
    /// الاستوديو. <b>الرَمزُ مِن الجَدوَلِ لا مِن حَرفِيَّةٍ مَنسوخَة</b>:
    /// الصَفحَةُ تَقرَأُ نَفسَ الرَمزِ لِتَختار رِسالَةَ القامُوس،
    /// فَانحِرافُ حَرفٍ بَينَ المَوضِعَينِ يُعطي صَفحَةً بِلا
    /// رِسالَة.</summary>
    private static string StudioDoorClosed(Services.Incubator.StudioAuthMethod method)
        => $"/studio/auth?err={Services.Incubator.StudioAuthDoor.UnavailableError(method)}";

    /// <summary>كوكي المُطالَبَة المُعَلَّقَة — يُكتَب في
    /// <c>/studio/begin</c> ويُستَهلَك مَرَّةً واحِدَة.</summary>
    private const string StudioPromptCookie = "ac.studio.prompt";

    /// <summary>
    /// <para><b>استِئنافُ المُطالَبَةِ المُعَلَّقَة — مَوضِعٌ واحِد،
    /// نُقطَتانِ تُنادِيانِه.</b> صاحِبُ الجَلسَةِ كَتَبَ فِكرَتَه في
    /// صَفحَةِ الهُبوطِ قَبلَ أَن يَملِكَ حِساباً، فَحُفِظَت في كوكي.
    /// وتُستَهلَك في مَوضِعَين: بَعدَ التَحَقُّقِ مُباشَرَةً لِمَن سَبَقَ
    /// أَن وافَقَ على الشُروط، وبَعدَ القَبولِ لِمَن لَم يُوافِق.</para>
    ///
    /// <para><b>ولِماذا استُخرِجَت (‏2026-08-23)</b>: كانَ المَوضِعُ
    /// الثاني يُنجَز بِإعادَةِ التَحويلِ إلى النُقطَةِ الأولى نَفسِها —
    /// <c>/studio/consent?returnUrl=/studio/auth/verify</c> — وتِلكَ
    /// النُقطَةُ <c>POST</c> فَقَط، فَكانَ التَحويلُ يَنتَهي إلى
    /// <b>‏405</b> بَعدَ قَبولِ الشُروطِ مُباشَرَةً. أَي أَنّ أَوَّلَ
    /// دُخولٍ لِمُستَخدِمٍ جَديدٍ بِفِكرَةٍ مَكتوبَة كانَ يَنتَهي بِخَطَإ
    /// بروتوكول. والحَلُّ لَيسَ تَبديلَ الوِجهَة وَحدَه: وِجهَةٌ تُصَيَّر
    /// بِـ<c>GET</c> تَرفَع الـ405 <b>وتُسقِط الفِكرَةَ صامِتَةً</b> —
    /// لِأَنّ لا قارِئَ لِلكوكي غَيرَ هذا الجِسم. فَاستُخرِجَ الجِسمُ
    /// ليُنادى مِن حَيثُ يَقَع القَرار.</para>
    ///
    /// <para>يُعيد <c>null</c> إن لَم تَكُن ثَمَّةَ مُطالَبَة — فَيُكمِل
    /// المُنادي بِوِجهَتِه الطَبيعِيَّة.</para>
    /// </summary>
    private static async Task<IResult?> ResumeStudioPromptAsync(
        HttpRequest req, HttpResponse res, Guid userId, string userName,
        IDocumentStore store, IServiceScopeFactory scopeFactory,
        Services.Incubator.FeasibilityAnalysisService incubator,
        bool checkConsent)
    {
        var promptCookie = req.Cookies[StudioPromptCookie];
        if (string.IsNullOrEmpty(promptCookie)) return null;

        if (checkConsent)
        {
            await using var consentQs = store.QuerySession(Services.Incubator.StudioAuth.Tenant);
            var consents = await consentQs.Query<Services.Incubator.ConsentRecord>()
                .Where(c => c.UserId == userId &&
                            c.Version == Services.Incubator.ConsentPolicy.CurrentVersion)
                .ToListAsync();
            if (consents.Count == 0)
                // ‏**وِجهَةُ العَودَةِ صَفحَةٌ تُصَيَّر بِـ`GET`**، ولا
                // تَحمِل استِئنافاً: نُقطَةُ القَبولِ نَفسُها تَستَأنِف
                // بِالنِداءِ أَدناه.
                return Results.Redirect("/studio/consent?returnUrl=/studio");
        }

        res.Cookies.Delete(StudioPromptCookie);
        var prompt = Uri.UnescapeDataString(promptCookie);

        // tier gate — هَل بَلَغ المُستَخدِم حَدّ تَحاليلِه؟
        using var checkScope = scopeFactory.CreateScope();
        var tier = checkScope.ServiceProvider
            .GetRequiredService<Services.Incubator.StudioTierService>();
        var gate = await tier.CheckAnalyzeAsync(userId);
        if (!gate.Allowed) return Results.Redirect($"/studio?upgrade={gate.BreachCode}");

        var s = await incubator.StartAsync(userId, userName);
        await incubator.SaveAnswerAsync(s.Id, "description", prompt);
        await incubator.MarkAnalyzingAsync(s.Id);
        await tier.RecordAnalysisAsync(userId);
        _ = Task.Run(async () =>
        {
            using var scope = scopeFactory.CreateScope();
            var bg = scope.ServiceProvider
                .GetRequiredService<Services.Incubator.FeasibilityAnalysisService>();
            try { await bg.RunAnalysisAsync(s.Id); } catch { }
        });
        return Results.Redirect($"/studio/s/{s.Id}");
    }

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
