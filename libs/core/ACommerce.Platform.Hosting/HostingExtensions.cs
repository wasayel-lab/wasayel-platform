using System.Reflection;
using ACommerce.Platform.MultiTenancy;
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
                opts.Events.TenancyStyle = global::Marten.Storage.TenancyStyle.Conjoined;

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
        app.UseStaticFiles();
        app.UseRouting();
        app.UsePlatformMultiTenancy();
        app.UseAntiforgery();

        // Wolverine.Http يُسَجِّل كلّ [WolverinePost]/[WolverineGet]/etc.
        app.MapWolverineEndpoints();

        return app;
    }
}
