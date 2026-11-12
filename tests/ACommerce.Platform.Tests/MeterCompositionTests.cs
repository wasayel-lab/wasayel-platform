using ACommerce.Kit.Auth;
using ACommerce.Kit.Auth.Providers.Twilio;
using ACommerce.Platform.Hosting;
using ACommerce.Platform.Shared.Metering;
using ACommerce.Templates.Customer.Marketplace;
using ACommerce.Templates.Customer.Marketplace.Services.Metering;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ G6d — كُلُّ عَميلٍ صادِرٍ في المُضيفِ المُرَكَّبِ يُسَجِّل (‏2026-11-12، W13-6·١١ — ADR-051 §١٨؛ التَصميم §6.3) ═════════════════════════════
//
// `ProviderCalls` يَسأَلُ الحاوِيَةَ عَن كاتِبِه، وتَركيبٌ بِلا القالِبِ يَبقى بِلا كاتِب (`IsRecording` = false) — وهذا ما يَجعَلُ تَوصيفَ العُدَّةِ يُرَكَّبُ وَحدَه.
// فَالخَطَرُ مُضيفُ إنتاجٍ يُرسِلُ بِلا عَدٍّ لِأَنّ كاتِبَه لَم يُسَجَّل. وهذا الصَفُّ يُرَكِّبُ المُضيفَ كَما يُرَكِّبُه `Program.cs` (المُضيفُ ثُمَّ القالِبُ ثُمَّ فَرعُ Twilio)،
// ويَحُلُّ **كُلَّ** `ProviderHttp<…>` مُسَجَّل، ويَحمَرُّ عَلى صِفرٍ أَو عَلى واحِدٍ لا يُسَجِّل — والضابِطُ السالِبُ (بِلا القالِب) يَرى `false` فَالأَداةُ لَيسَت عَمياء.

public class MeterCompositionTests(ITestOutputHelper output)
{
    private static (WebApplication App, IServiceCollection Services) Compose(bool template)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Production,
            ContentRootPath = AppContext.BaseDirectory,
        });
        builder.Configuration["ConnectionStrings:Postgres"] = "Host=localhost;Database=compose_only;Username=u;Password=p";
        builder.Host.UseDefaultServiceProvider(o => { o.ValidateOnBuild = false; o.ValidateScopes = false; });
        builder.AddPlatformHost();
        if (template) builder.Services.AddCustomerMarketplaceTemplate();
        // فَرعُ `AuthChannelProvider.Twilio` في Program.cs بِحَرفِ نِدائِه (`AddTwilioSmsChannel`)، والإعدادُ وَهمِيّ.
        builder.Services.AddTwilioSmsChannel(o => { o.AccountSid = "AC0"; o.AuthToken = "t"; o.FromNumber = "+15005550006"; });
        var services = builder.Services;
        return (builder.Build(), services);
    }

    /// <summary>كُلُّ <c>ProviderHttp&lt;…&gt;</c> مُسَجَّلٍ في الحاوِيَة — بِنَوعِه المُغلَق، ويُحَلُّ ويُقرَأُ <c>IsRecording</c>.</summary>
    private static IReadOnlyList<(string Kit, bool Recording)> OutboundClients(IServiceProvider services, IServiceCollection descriptors)
        => descriptors.Select(d => d.ServiceType)
            .Where(t => t.IsGenericType && t.GetGenericTypeDefinition() == typeof(ProviderHttp<>)).Distinct()
            .Select(t => (t.GetGenericArguments()[0].Name, (bool)t.GetProperty(nameof(ProviderHttp<object>.IsRecording))!.GetValue(services.GetRequiredService(t))!))
            .ToList();

    [Fact]
    public async Task Every_outbound_client_resolved_from_the_composed_host_is_recording()
    {
        var program = File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, "apps", "V1.App", "Program.cs"));
        Assert.Contains("builder.Services.AddTwilioSmsChannel(", program, StringComparison.Ordinal);
        Assert.Contains("AddCustomerMarketplaceTemplate", program, StringComparison.Ordinal);

        var (app, collection) = Compose(template: true);
        await using var _ = app;
        var descriptors = app.Services.GetRequiredService<IServiceProviderIsService>();
        var clients = OutboundClients(app.Services, collection);
        var calls = app.Services.GetRequiredService<ProviderCalls>();
        var recorder = app.Services.GetService<OutboundRecorder>();
        output.WriteLine($"[G6d] عُمَلاءُ صادِرونَ في المُضيفِ المُرَكَّب {clients.Count}: {string.Join(", ", clients.Select(c => $"{c.Kit}={c.Recording}"))} · "
                         + $"الكاتِب {recorder?.GetType().Name ?? "—"} · ProviderCalls.IsRecording={calls.IsRecording}");
        Assert.True(descriptors.IsService(typeof(IOtpChannel)));
        Assert.True(clients.Count > 0, "أَداةٌ عَمياء: صِفرُ عَميلٍ صادِرٍ في المُضيفِ المُرَكَّب.");
        Assert.IsType<UsageLedgerWriter>(recorder);
        Assert.True(clients.All(c => c.Recording), "عَميلٌ صادِرٌ لا يُسَجِّلُ في المُضيفِ المُرَكَّب: " + string.Join(", ", clients.Where(c => !c.Recording).Select(c => c.Kit)));
    }

    /// <summary><b>الضابِطُ السالِب</b>: المُضيفُ نَفسُه بِلا القالِبِ لا كاتِبَ لَه — فَيَقولُ العَميلُ <c>false</c> ويَراه الصَفُّ أَعلاه لَو وَقَع.</summary>
    [Fact]
    public async Task Without_the_template_the_same_client_says_it_is_not_recording()
    {
        var (app, collection) = Compose(template: false);
        await using var _ = app;
        var clients = OutboundClients(app.Services, collection);
        output.WriteLine($"[G6d] بِلا القالِب: {string.Join(", ", clients.Select(c => $"{c.Kit}={c.Recording}"))}");
        Assert.NotEmpty(clients);
        Assert.All(clients, c => Assert.False(c.Recording));
    }
}
