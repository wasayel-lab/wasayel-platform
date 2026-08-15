using System.Reflection;
using ACommerce.Platform.MultiTenancy;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events.Projections;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Wolverine;
using Wolverine.Http;
using Wolverine.Marten;

namespace ACommerce.Platform.Hosting;

/// <summary>
/// نُقطَة دُخول واحِدَة لِكُلّ تَطبيقات المنصّة. تَجمَع
/// Marten + Wolverine + Serilog + MultiTenancy + Wolverine.Http
/// في extension واحِد. التَطبيق يَستَدعيها مَرَّة في Program.cs
/// ثُمّ يُضيف ApplicationParts/Assemblies الكيتات الخاصّة به فقط.
/// </summary>
public sealed class PlatformHostBuilder
{
    private readonly WebApplicationBuilder _builder;
    private readonly List<Assembly> _kitAssemblies = new();

    public PlatformHostBuilder(WebApplicationBuilder builder) => _builder = builder;

    public PlatformHostBuilder AddKitAssembly(Assembly assembly)
    {
        if (!_kitAssemblies.Contains(assembly)) _kitAssemblies.Add(assembly);
        return this;
    }

    public PlatformHostBuilder AddKitAssemblyOf<T>()
        => AddKitAssembly(typeof(T).Assembly);

    internal IReadOnlyList<Assembly> KitAssemblies => _kitAssemblies;
    internal WebApplicationBuilder Builder => _builder;
}

public static class HostingExtensions
{
    /// <summary>
    /// يُهَيِّئ التَطبيق بـ Marten + Wolverine + Serilog + MultiTenancy.
    /// </summary>
    public static PlatformHostBuilder AddPlatformHost(
        this WebApplicationBuilder builder,
        Action<PlatformHostBuilder>? configure = null)
    {
        var pb = new PlatformHostBuilder(builder);
        configure?.Invoke(pb);

        // Serilog إلى console
        builder.Host.UseSerilog((ctx, lc) => lc
            .MinimumLevel.Information()
            .WriteTo.Console());

        var connStr = builder.Configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("Postgres connection string missing");

        // Marten: events + documents بـ conjoined tenancy
        builder.Services.AddMarten(opts =>
            {
                opts.Connection(connStr);
                opts.DatabaseSchemaName = "platform";

                // كلّ events + documents مَحصورَة بـ tenant_id إلّا
                // ما نُسَجِّله صراحَة كَ global.
                opts.Policies.AllDocumentsAreMultiTenanted();
                opts.Events.TenancyStyle = global::JasperFx.MultiTenancy.TenancyStyle.Conjoined;

                // Tenant document = global (سِجِلّ المُستَأجِرين أَنفُسهم)
                opts.Schema.For<ACommerce.Kit.Tenants.Tenant>()
                    .SingleTenanted()
                    .Identity(x => x.Id);

                // Snapshot لِـ Listing aggregate (inline = نَفس الـ tx)
                opts.Projections.Snapshot<ACommerce.Kit.Listings.Listing>(SnapshotLifecycle.Inline);

                // Documents الإضافيّة — Marten يَكتَشِفها لكنّ ذِكرها صَريحاً
                // يَجعَل الـ schema gen أَوضَح ويَتَأكَّد من الـ identity.
                opts.Schema.For<ACommerce.Kit.Auth.User>().Identity(x => x.Id);
                opts.Schema.For<ACommerce.Kit.Notifications.Notification>().Identity(x => x.Id);
                opts.Schema.For<ACommerce.Kit.Chat.Conversation>().Identity(x => x.Id);
                opts.Schema.For<ACommerce.Kit.Chat.Message>().Identity(x => x.Id);
                opts.Schema.For<ACommerce.Kit.Favorites.Favorite>().Identity(x => x.Id);
                opts.Schema.For<ACommerce.Kit.Subscriptions.Plan>().Identity(x => x.Id);
                opts.Projections.Snapshot<ACommerce.Kit.Subscriptions.Subscription>(SnapshotLifecycle.Inline);
                opts.Projections.Snapshot<ACommerce.Kit.Support.Ticket>(SnapshotLifecycle.Inline);
                opts.Projections.Snapshot<ACommerce.Kit.Offers.Offer>(SnapshotLifecycle.Inline);
                opts.Schema.For<ACommerce.Kit.Offers.ListingMatch>().Identity(x => x.Id);
                opts.Schema.For<ACommerce.Kit.SavedSearches.SavedSearch>().Identity(x => x.Id);

                // تَعريفات أَدوار المُستَأجِر — مُتَعَدِّدَة الإيجار
                // بِالسِياسَة العامَّة أَعلاه (conjoined)، وهذا شَرط لا
                // تَحسين: تَعريف دَور مَتجَر لا يُقرَأ مِن سِياق مَتجَر
                // آخَر، والعَزل يَقَع في <c>tenant_id</c> لا في شَرط
                // مَكتوب بِاليَد. الـ Id سلاج الدَور، فَنَفس السلاج في
                // مَتجَرَين وَثيقَتان مُستَقِلَّتان.
                opts.Schema.For<ACommerce.Kit.Roles.TenantRoleDefinition>()
                    .Identity(x => x.Id);

                // ثيم المُستَأجِر — نَفس العَقد حَرفاً: مُتَعَدِّد الإيجار
                // بِالسِياسَة العامَّة (conjoined)، والـ Id سلاج الثيم.
                // والعَزل هُنا أَشَدّ إلزاماً: تَسَرُّب تَعريف دَور يُظهِر
                // خِياراً لا يَملِكُه مَتجَر؛ وتَسَرُّب ثيم يَصبُغ صَفحَة
                // مَتجَر بِلَون مَتجَر آخَر — خَطَأً يَراه كُلّ زائِر.
                opts.Schema.For<ACommerce.Kit.Theme.TenantThemeDefinition>()
                    .Identity(x => x.Id);

                // ImportedRecord: مُستَنَد عامّ يَكتُبه الـ Importer لِكُلّ
                // صَفّ مِن جَدول مَصدَر لا يَملِك typed map. الـ Id سَلسَلَة
                // "{Table}/{SourceId}" — Marten يَحتاج Identity صَريحَة.
                opts.Schema.For<ACommerce.Platform.Shared.ImportedRecord>()
                    .Identity(x => x.Id);

                // Auto-create schema في dev
                if (builder.Environment.IsDevelopment())
                {
                    opts.AutoCreateSchemaObjects = JasperFx.AutoCreate.All;
                }
            })
            .UseLightweightSessions()
            .IntegrateWithWolverine();

        // SignalR للبَثّ الفَوريّ
        builder.Services.AddSignalR();

        // Wolverine: يَكتَشِف handlers + يُولِّد HTTP endpoints
        builder.Host.UseWolverine(opts =>
        {
            // Wolverine 6 نَزَع مُصَرِّف Roslyn مِن الـ core، فَـ TypeLoadMode
            // الافتِراضيّ (Dynamic) يَرمي عِند StartAsync بِلا IAssemblyGenerator.
            // حُزمَة WolverineFx.RuntimeCompilation تُسَجِّله تِلقائيّاً، والنِداء
            // الصَريح هُنا idempotent ويَجعَل الاعتِماد ظاهِراً في الكود لا مُضمَراً.
            opts.UseRuntimeCompilation();

            foreach (var asm in pb.KitAssemblies)
                opts.Discovery.IncludeAssembly(asm);
            opts.Policies.AutoApplyTransactions();
        });

        builder.Services.AddPlatformMultiTenancy();
        builder.Services.AddWolverineHttp();

        // Razor + Blazor Server
        builder.Services.AddRazorComponents()
            .AddInteractiveServerComponents();

        return pb;
    }

    /// <summary>
    /// تَمرير الـ pipeline + خَريطَة Wolverine.Http endpoints + middleware
    /// تَعَدُّد المُستَأجِرين قَبل تَوجيه الصَفحات.
    /// </summary>
    public static WebApplication UsePlatformHost(this WebApplication app)
    {
        app.UseSerilogRequestLogging();

        // UseStaticFiles بدلاً من MapStaticAssets لأنّ الأَخير يُعلِن
        // Content-Encoding: gzip ثُمّ يُرسِل صِفر بايتات حينَ لا يَكون
        // هُناك نُسخَة gzipped مُسبَقَة، ما يَكسِر تَحميل CSS في المُتَصَفِّح.
        app.UseStaticFiles();

        app.UseRouting();
        app.UseAntiforgery();
        app.UsePlatformMultiTenancy();

        // Wolverine.Http يُسَجِّل كلّ [WolverinePost]/[WolverineGet]/etc.
        app.MapWolverineEndpoints(opts =>
        {
            // ═══ حَصر المُستَأجِر خاصِّيَّةً بِنيَوِيَّة ═══════════════
            //
            // مَساراتُنا كُلُّها بِشَكل `/{slug}/…`، والحُزمَة تَشحَن
            // كَشف المُستَأجِر مِن وَسيط المَسار. فَالسَطر التالي
            // يَنقُل العَزل مِن **اتِّفاق مَكتوب بِاليَد في كُلّ جِسم**
            // إلى **خاصِّيَّة يُوَلِّدُها التَركيب**.
            //
            // <b>والرَبط مَقيس لا مَظنون</b>: وَثيقَة القَرار وَسَمَته
            // «[غَير مُثبَت]»، وأَثبَتَه `LiveOutboxTenantProofTests`
            // بِمُضيفٍ حَيّ وطَرَفَين:
            //     detect=on  → session.TenantId = وَسيط المَسار
            //     detect=off → session.TenantId = *DEFAULT*
            // والجَلسَة في الحالَتَين تَحمِل
            // `Wolverine.Marten.FlushOutgoingMessagesOnCommit` — أَي
            // أَنَّها مُنخَرِطَة في الصُندوق الصادِر.
            opts.TenantId.IsRouteArgumentNamed("slug");

            // ═══ والفَرض، لا الكَشف وَحدَه ═══════════════════════════
            //
            // الكَشف بِلا فَرض يُصلِح الحاضِر ولا يَمنَع الغَد: نُقطَة
            // جَديدَة تُكتَب بِلا `slug` تَمُرّ صامِتَةً فَتَكتُب في
            // `*DEFAULT*` — وهو بِعَينِه العَطَب الَّذي قاسَته وَثيقَة
            // القَرار في سِتّ مُعالِجات. ومَع `AssertExists()` تَرتَدّ
            // بِـ400 (‏`ProblemDetails`) بَدَل أَن تَكتُب في اللامَكان.
            //
            // <b>والاستِثناء مُعلَن ومَقيس لا صامِت</b>: نُقطَتا الزَحف
            // عَلى الجَذر (‏`/robots.txt`, `/sitemap.xml`) لا تَحمِلانِ
            // `slug` بِطَبيعَتِهِما، فَتَحمِلانِ `[NotTenanted]`
            // صَراحَةً — و`WolverineTenancyContractTests` يُثَبِّتُهُما
            // بِاسمِهِما ويَحمَرّ عَلى ثالِثَةٍ تُضاف بِلا قَرار.
            //
            // ولا يَمَسّ هذا مَسارات Minimal API ولا صَفَحات Blazor:
            // `WolverineHttpOptions` تَحكُم سَلاسِل Wolverine وَحدَها —
            // وهذا **مَقيس** بِتَوصيف ‏27 مَساراً قَبل/بَعد
            // (‏`scripts/characterize-routes.sh`)، لا مُستَنتَجاً.
            opts.TenantId.AssertExists();
        });

        return app;
    }
}
