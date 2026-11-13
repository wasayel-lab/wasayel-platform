using System.Reflection;
using ACommerce.Kit.Auth;
using ACommerce.Kit.Auth.Providers.Brevo;
using ACommerce.Kit.Auth.Providers.Nafath;
using ACommerce.Kit.Files;
using ACommerce.Kit.Files.Providers.S3;
using ACommerce.Kit.Auth.Providers.Smtp;
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
//
// ‏(‏2026-11-12، W13-6·١٢ — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه) **وفَرعا البَريد**: `AddBrevoEmailChannel` (عَميلُه `ProviderHttp`) و`AddSmtpEmailChannel` (عُدَّةُ SDK
// تَحمِلُ `ProviderCalls` لا `ProviderHttp`، فَلا يَراها حَلُّ `ProviderHttp<…>`). والقَناةُ الواحِدَةُ يَختارُها الإعداد، فَيُرَكَّبُ كُلُّ فَرعٍ في مُضيفِه؛ ومَعَ
// العُمَلاءِ **تُحَلُّ القَناتانِ نَفسُهُما** (`IOtpChannel` و`IEmailOtpChannel`) ويُقرَأُ كُلُّ مُستَدعٍ مَعدودٍ في حُقولِهِما — وعُدَّةٌ بِلا مُستَدعٍ أَو بِمُستَدعٍ
// لا يُسَجِّلُ حَمراء.
//
// ‏(‏2026-11-12، W13-6·١٣ — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه) **ونَفاذٌ ومَخزَنُ الكائِناتِ وخِدمَةُ الإشعار**: فَرعُ `AuthChannelProvider.Nafath` (`AddNafathChannel`،
// عَميلُه `ProviderHttp`) وفَرعُ `FileStorageChoice.S3` (`AddS3FileStorage`، عُدَّةُ SDK تَحمِلُ `ProviderCalls`) في `Program.cs`، و`WebPushService` في القالِب —
// والثَلاثُ تُسَجَّلُ بِبانٍ أَطوَلَ تَختارُه الحاوِيَةُ لا بِمَصنَع، فَهذا الصَفُّ هُوَ ما يَرى أَنّها اختارَت الباني الَّذي يُسَجِّل (لا مَقعَدَ الاختِبار).
//
// ‏(‏2026-11-12، W13-6·١٥ — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه) **وOTO وخَلفِيّاتُ النَماذِج**: `AddOtoShipping` بِحَرفِ نِدائِه في `Program.cs` (عُدَّةٌ تُحَلُّ بِنَوعِها)، و`ProviderHttp<IAgentBackend>`
// يُسَجِّلُه القالِب — والخَلفِيَّةُ لا تُحَلُّ بِنَوعِها بَل يَبنيها المُزَوِّدُ المُسَمّى (`IAgentBackendProvider.For`)، فَتُقرَأُ خَلفِيَّةُ كُلِّ وَكيلٍ كَما تُقرَأُ العُدَّة.

public class MeterCompositionTests(ITestOutputHelper output)
{
    /// <summary>فَرعا <c>AuthChannelProvider.Smtp</c>/<c>.Brevo</c> في <c>Program.cs</c> بِحَرفِ نِدائَيهِما، والإعدادُ وَهمِيّ.</summary>
    internal static readonly string[] EmailBranches = ["brevo", "smtp"];

    private static (WebApplication App, IServiceCollection Services) Compose(bool template, string email = "brevo")
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
        if (email == "brevo") builder.Services.AddBrevoEmailChannel(o => { o.ApiKey = "k"; o.From = "no-reply@wasayel.test"; });
        else builder.Services.AddSmtpEmailChannel(o => { o.Host = "127.0.0.1"; o.From = "no-reply@wasayel.test"; });
        // ‏(‏2026-11-12، W13-6·١٣) فَرعا `AuthChannelProvider.Nafath` و`FileStorageChoice.S3` بِحَرفِ نِدائَيهِما، والإعدادُ وَهمِيّ.
        builder.Services.AddNafathChannel(o => { o.ClientId = "c"; o.ClientSecret = "s"; });
        builder.Services.AddS3FileStorage(o => { o.Endpoint = "http://127.0.0.1:1"; o.Bucket = "b"; o.AccessKeyId = "k"; o.SecretAccessKey = "s"; o.PublicBaseUrl = "https://files.wasayel.test"; });
        // ‏(‏2026-11-12، W13-6·١٤) فَرعا PayPal وPaddle في Program.cs بِحَرفِ نِدائَيهِما، والإعدادُ وَهمِيٌّ يَضبِطُهُما — وميسّر يُسَجِّلُه القالِب.
        builder.Configuration[ACommerce.Kit.Payments.Providers.PayPal.PayPalEnvironment.ClientIdKey] = "c";
        builder.Configuration[ACommerce.Kit.Payments.Providers.PayPal.PayPalEnvironment.ClientSecretKey] = "s";
        builder.Configuration[ACommerce.Kit.Payments.Providers.PayPal.PayPalEnvironment.EnvironmentKey] = "live";
        builder.Configuration[ACommerce.Kit.Payments.Providers.Paddle.PaddleEnvironment.ApiKeyKey] = "k";
        builder.Configuration[ACommerce.Kit.Payments.Providers.Paddle.PaddleEnvironment.EnvironmentKey] = "live";
        ACommerce.Kit.Payments.Providers.PayPal.PayPalExtensions.AddPayPalSubscriptions(builder.Services, builder.Configuration);
        ACommerce.Kit.Payments.Providers.Paddle.PaddleExtensions.AddPaddleBilling(builder.Services, builder.Configuration);
        // ‏(‏2026-11-12، W13-6·١٥) فَرعُ OTO في Program.cs بِحَرفِ نِدائِه — يُسَجَّلُ دائِماً.
        ACommerce.Kit.Delivery.OtoShippingExtensions.AddOtoShipping(builder.Services, builder.Configuration);
        var services = builder.Services;
        return (builder.Build(), services);
    }

    /// <summary>كُلُّ <c>ProviderHttp&lt;…&gt;</c> مُسَجَّلٍ في الحاوِيَة — بِنَوعِه المُغلَق، ويُحَلُّ ويُقرَأُ <c>IsRecording</c>.</summary>
    private static IReadOnlyList<(string Kit, bool Recording)> OutboundClients(IServiceProvider services, IServiceCollection descriptors)
        => descriptors.Select(d => d.ServiceType)
            .Where(t => t.IsGenericType && t.GetGenericTypeDefinition() == typeof(ProviderHttp<>)).Distinct()
            .Select(t => (t.GetGenericArguments()[0].Name, (bool)t.GetProperty(nameof(ProviderHttp<object>.IsRecording))!.GetValue(services.GetRequiredService(t))!))
            .ToList();

    /// <summary>‏(‏2026-11-12، W13-6·١٢) <b>العُدَدُ الصادِرَةُ نَفسُها</b>: القَناتانِ مَحلولَتَين، وكُلُّ مُستَدعٍ مَعدودٍ في حُقولِ كُلٍّ مِنهُما (<c>ProviderCalls</c> أَو
    /// <c>ProviderHttp&lt;…&gt;</c>) — فَعُدَّةُ SDK تُرى كَعُدَّةِ HTTP. والعُدَّةُ تُسَجِّلُ إن كانَ لَها مُستَدعٍ واحِدٌ عَلى الأَقَلِّ وكُلُّ مُستَدعِياتِها تُسَجِّل.</summary>
    private static IReadOnlyList<(string Kit, int Invokers, bool Recording)> OutboundKits(IServiceProvider services)
        => new[] { typeof(IOtpChannel), typeof(IEmailOtpChannel), typeof(INafathChannel), typeof(IFileStorage),
                   typeof(ACommerce.Templates.Customer.Marketplace.Services.WebPushService),
                   // ‏(‏2026-11-12، W13-6·١٤) عُدَدُ الدَفعِ الثَلاث — ميسّر مِن القالِب، وPayPal وPaddle حينَ يُضبَطُ اعتِمادُهُما.
                   typeof(ACommerce.Kit.Payments.MoyasarClient), typeof(ACommerce.Kit.Payments.Providers.PayPal.PayPalPaymentProvider),
                   typeof(ACommerce.Kit.Payments.Providers.Paddle.PaddlePaymentProvider),
                   // ‏(‏2026-11-12، W13-6·١٥) وعَميلُ OTO.
                   typeof(ACommerce.Kit.Delivery.OtoClient) }.Select(services.GetService).OfType<object>()
            // ‏(W13-6·١٥) وخَلفِيَّةُ كُلِّ وَكيلٍ كَما يَبنيها المُزَوِّدُ المُسَمّى — حينَ يُسَجِّلُه القالِب.
            .Concat(services.GetService<ACommerce.Templates.Customer.Marketplace.Services.IAgentBackendProvider>() is { } agents
                ? new object[] { agents.For(ACommerce.Templates.Customer.Marketplace.Services.AgentNames.Studio), agents.For(ACommerce.Templates.Customer.Marketplace.Services.AgentNames.Analysis) }
                : Array.Empty<object>())
            .Select(kit =>
            {
                var invokers = kit.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .Where(f => f.FieldType == typeof(ProviderCalls) || (f.FieldType.IsGenericType && f.FieldType.GetGenericTypeDefinition() == typeof(ProviderHttp<>)))
                    .Select(f => f.GetValue(kit) is { } v && (bool)v.GetType().GetProperty(nameof(ProviderCalls.IsRecording))!.GetValue(v)!).ToList();
                return (kit.GetType().Name, invokers.Count, invokers.Count > 0 && invokers.All(r => r));
            }).ToList();

    [Fact]
    public async Task Every_outbound_client_resolved_from_the_composed_host_is_recording()
    {
        var program = File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, "apps", "V1.App", "Program.cs"));
        Assert.Contains("builder.Services.AddTwilioSmsChannel(", program, StringComparison.Ordinal);
        Assert.Contains("builder.Services.AddBrevoEmailChannel(", program, StringComparison.Ordinal);
        Assert.Contains("builder.Services.AddSmtpEmailChannel(", program, StringComparison.Ordinal);
        Assert.Contains("builder.Services.AddNafathChannel(", program, StringComparison.Ordinal);
        Assert.Contains("builder.Services.AddS3FileStorage(", program, StringComparison.Ordinal);
        Assert.Contains("builder.Services.AddPayPalSubscriptions(builder.Configuration);", program, StringComparison.Ordinal);
        Assert.Contains("builder.Services.AddPaddleBilling(builder.Configuration);", program, StringComparison.Ordinal);
        Assert.Contains("builder.Services.AddOtoShipping(builder.Configuration);", program, StringComparison.Ordinal);
        Assert.Contains("AddCustomerMarketplaceTemplate", program, StringComparison.Ordinal);

        foreach (var email in EmailBranches)
        {
            var (app, collection) = Compose(template: true, email);
            await using var _ = app;
            var descriptors = app.Services.GetRequiredService<IServiceProviderIsService>();
            var clients = OutboundClients(app.Services, collection);
            var kits = OutboundKits(app.Services);
            var calls = app.Services.GetRequiredService<ProviderCalls>();
            var recorder = app.Services.GetService<OutboundRecorder>();
            output.WriteLine($"[G6d] ({email}) عُمَلاءُ صادِرونَ في المُضيفِ المُرَكَّب {clients.Count}: {string.Join(", ", clients.Select(c => $"{c.Kit}={c.Recording}"))} · "
                             + $"عُدَد {kits.Count}: {string.Join(", ", kits.Select(k => $"{k.Kit}({k.Invokers})={k.Recording}"))} · "
                             + $"الكاتِب {recorder?.GetType().Name ?? "—"} · ProviderCalls.IsRecording={calls.IsRecording}");
            Assert.True(descriptors.IsService(typeof(IOtpChannel)));
            Assert.True(descriptors.IsService(typeof(IEmailOtpChannel)));
            Assert.True(clients.Count > 0, "أَداةٌ عَمياء: صِفرُ عَميلٍ صادِرٍ في المُضيفِ المُرَكَّب.");
            // ‏(W13-6·١٣) خَمسٌ لا اثنَتان: والنَفاذُ والمَخزَنُ وخِدمَةُ الإشعار. ‏(W13-6·١٤) ثَمانٍ: وعُدَدُ الدَفعِ الثَلاث.
            // ‏(W13-6·١٥) إحدى عَشرَة: وOTO وخَلفِيَّتا الوَكيلَين (Studio وAnalysis).
            Assert.True(kits.Count == 11, $"أَداةٌ عَمياء: {kits.Count} عُدَّةً صادِرَةً لا 11 ({email}).");
            Assert.Contains(kits, k => k.Kit == "OtoClient");
            Assert.Equal(2, kits.Count(k => k.Kit is "AnthropicBackend" or "GeminiBackend" or "OpenAIBackend"));
            Assert.Contains(clients, c => c.Kit is "OtoClient");
            Assert.Contains(clients, c => c.Kit is "IAgentBackend");
            Assert.Equal(new[] { "MoyasarClient", "PayPalPaymentProvider", "PaddlePaymentProvider" },
                         kits.Select(k => k.Kit).Where(k => k is "MoyasarClient" or "PayPalPaymentProvider" or "PaddlePaymentProvider"));
            Assert.Equal(new[] { "MoyasarClient", "PaddlePaymentProvider", "PayPalPaymentProvider" },
                         clients.Select(c => c.Kit).Where(k => k is "MoyasarClient" or "PayPalPaymentProvider" or "PaddlePaymentProvider").Order(StringComparer.Ordinal));
            Assert.Contains(clients, c => c.Kit == nameof(NafathChannel));
            Assert.Equal(new[] { "NafathChannel", "S3FileStorage", "WebPushService" },
                         kits.Select(k => k.Kit).Where(k => k is "NafathChannel" or "S3FileStorage" or "WebPushService"));
            Assert.IsType<UsageLedgerWriter>(recorder);
            Assert.True(clients.All(c => c.Recording), "عَميلٌ صادِرٌ لا يُسَجِّلُ في المُضيفِ المُرَكَّب: " + string.Join(", ", clients.Where(c => !c.Recording).Select(c => c.Kit)));
            Assert.True(kits.All(k => k.Recording), "عُدَّةٌ صادِرَةٌ بِلا مُستَدعٍ يُسَجِّلُ في المُضيفِ المُرَكَّب: " + string.Join(", ", kits.Where(k => !k.Recording).Select(k => k.Kit)));
        }
    }

    /// <summary><b>الضابِطُ السالِب</b>: المُضيفُ نَفسُه بِلا القالِبِ لا كاتِبَ لَه — فَيَقولُ العَميلُ <c>false</c> ويَراه الصَفُّ أَعلاه لَو وَقَع.</summary>
    /// <remarks>‏(‏2026-11-12، W13-6·١٢) وفي فَرعَيِ البَريد، والعُدَّتانِ مَعَ العُمَلاء.</remarks>
    [Fact]
    public async Task Without_the_template_the_same_client_says_it_is_not_recording()
    {
        foreach (var email in EmailBranches)
        {
            var (app, collection) = Compose(template: false, email);
            await using var _ = app;
            var clients = OutboundClients(app.Services, collection);
            var kits = OutboundKits(app.Services);
            output.WriteLine($"[G6d] ({email}) بِلا القالِب: {string.Join(", ", clients.Select(c => $"{c.Kit}={c.Recording}"))} · "
                             + string.Join(", ", kits.Select(k => $"{k.Kit}({k.Invokers})={k.Recording}")));
            Assert.NotEmpty(clients);
            Assert.All(clients, c => Assert.False(c.Recording));
            // ‏(W13-6·١٣) أَربَعٌ لا اثنَتان: والنَفاذُ والمَخزَن — وخِدمَةُ الإشعارِ يُسَجِّلُها القالِبُ فَلا تُرى بِدونِه.
            // ‏(W13-6·١٤) سِتٌّ: وPayPal وPaddle — وميسّر يُسَجِّلُه القالِبُ فَلا يُرى بِدونِه.
            // ‏(W13-6·١٥) سَبعٌ: وOTO — وخَلفِيّاتُ النَماذِجِ يُسَجِّلُها القالِبُ فَلا تُرى بِدونِه.
            Assert.Equal(7, kits.Count);
            Assert.All(kits, k => Assert.Equal((1, false), (k.Invokers, k.Recording)));
        }
    }
}
