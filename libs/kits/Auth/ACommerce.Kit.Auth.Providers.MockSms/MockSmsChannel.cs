using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ACommerce.Kit.Auth.Providers.MockSms;

/// <summary>
/// مُزَوِّد SMS وَهميّ — يَطبَع الكود في الـ console بَدَل إرسال SMS فعليّ.
/// الكود الصَحيح دائماً "123456" في وَضع التَطوير.
///
/// <para>للاستِبدال في الإنتاج: نَفِّذ <see cref="IOtpChannel"/> في مَكتَبَة
/// أُخرى (Twilio، Unifonic، …) وسَجِّلها بَدَله:</para>
/// <code>
/// services.AddSingleton&lt;IOtpChannel, UnifonicSmsChannel&gt;();
/// </code>
/// </summary>
public sealed class MockSmsChannel : IOtpChannel
{
    public const string FixedCode = "123456";

    private readonly ILogger<MockSmsChannel> _logger;
    public MockSmsChannel(ILogger<MockSmsChannel> logger) => _logger = logger;

    public string ChannelName => "MockSms";
    public string? DevHintCode => FixedCode;

    public Task SendOtpAsync(string phone, string code, CancellationToken ct)
    {
        _logger.LogInformation("[MockSms] أَرسَلنا الكود {Code} إلى {Phone}", code, phone);
        return Task.CompletedTask;
    }
}

public static class MockSmsExtensions
{
    public static IServiceCollection AddMockSmsChannel(this IServiceCollection services)
    {
        services.AddSingleton<IOtpChannel, MockSmsChannel>();
        return services;
    }
}
