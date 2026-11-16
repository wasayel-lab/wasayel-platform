using ACommerce.Platform.Shared.Metering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ACommerce.Kit.Auth.Providers.MockEmail;

/// <summary>
/// مُزَوِّد بَريد وَهميّ — يَطبَع الكود في الـ console بَدَل إرسال بَريد
/// فِعليّ، بِنَفس سُلوك <c>MockSmsChannel</c> حَرفيّاً. الكود الصَحيح
/// دائماً "123456" في وَضع التَطوير.
///
/// <para>للاستِبدال في الإنتاج: نَفِّذ <see cref="IEmailOtpChannel"/> في
/// مَكتَبَة أُخرى (SMTP، Azure Communication Services، …) وسَجِّلها بَدَله:</para>
/// <code>
/// services.AddSmtpEmailChannel(o =&gt; config.GetSection("Auth:Email").Bind(o));
/// </code>
/// </summary>
public sealed class MockEmailChannel : IEmailOtpChannel, IDevelopmentStubChannel
{
    public const string FixedCode = "123456";

    private readonly ILogger<MockEmailChannel> _logger;
    public MockEmailChannel(ILogger<MockEmailChannel> logger) => _logger = logger;

    public string ChannelName => "MockEmail";
    public string? DevHintCode => FixedCode;

    // ‏(‏2026-11-12، W13-6·١٢ — ADR-051 §١٩) التَوقيعُ يَحمِلُ `ProviderCall` كَكُلِّ قَناة، والمُحاكي لا يُغادِرُ العَمَلِيَّةَ فَلا يَعُدّ: النِداءُ يُقبَلُ ولا يُستَعمَل.
    public Task SendOtpAsync(ProviderCall call, string email, string code, CancellationToken ct)
    {
        _logger.LogInformation("[MockEmail] أَرسَلنا الكود {Code} إلى {Email}", code, email);
        return Task.CompletedTask;
    }

    /// <summary>‏(‏2026-10-29، A3) الإشعارُ يُكتَبُ في السِجِلِّ كَالرَمز — لا بَريدَ حَقيقِيَّ في التَطوير.</summary>
    public Task SendNoticeAsync(ProviderCall call, string email, string subject, string text, CancellationToken ct)
    {
        _logger.LogInformation("[MockEmail] إشعارٌ إلى {Email}: {Subject} — {Text}", email, subject, text);
        return Task.CompletedTask;
    }
}

public static class MockEmailExtensions
{
    public static IServiceCollection AddMockEmailChannel(this IServiceCollection services)
    {
        services.AddSingleton<IEmailOtpChannel, MockEmailChannel>();
        return services;
    }
}
