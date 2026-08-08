using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ACommerce.Kit.Notifications.Providers.Smtp;

/// <summary>إعدادات SMTP.</summary>
public sealed class SmtpOptions
{
    public string Host { get; set; } = "";
    public int Port { get; set; } = 587;
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string FromAddress { get; set; } = "";
    public string FromName { get; set; } = "ACommerce";
    public bool EnableSsl { get; set; } = true;
}

/// <summary>قَناة إشعار عَبر بَريد SMTP. تَجلِب بَريد المُستَخدِم مِن
/// <see cref="IUserEmailLookup"/> ثُمَّ تُرسِل HTML. تَتَجاهَل لَو لا
/// يوجد بَريد لِلمُستَخدِم.</summary>
public sealed class SmtpNotificationChannel : INotificationChannel
{
    private readonly SmtpOptions _opts;
    private readonly IUserEmailLookup _lookup;
    private readonly ILogger<SmtpNotificationChannel> _logger;

    public SmtpNotificationChannel(IOptions<SmtpOptions> opts, IUserEmailLookup lookup, ILogger<SmtpNotificationChannel> logger)
    {
        _opts = opts.Value;
        _lookup = lookup;
        _logger = logger;
        if (string.IsNullOrEmpty(_opts.Host) || string.IsNullOrEmpty(_opts.FromAddress))
            throw new InvalidOperationException("SMTP Host/FromAddress مَطلوبَة.");
    }

    public string ChannelName => "Smtp";

    public async Task SendAsync(NotificationPayload p, CancellationToken ct)
    {
        var email = await _lookup.GetEmailAsync(p.UserId, ct);
        if (string.IsNullOrEmpty(email)) return;

        using var client = new SmtpClient(_opts.Host, _opts.Port)
        {
            EnableSsl = _opts.EnableSsl,
            Credentials = string.IsNullOrEmpty(_opts.Username) ? null
                : new NetworkCredential(_opts.Username, _opts.Password)
        };
        using var msg = new MailMessage(new MailAddress(_opts.FromAddress, _opts.FromName), new MailAddress(email))
        {
            Subject = p.Title,
            IsBodyHtml = true,
            Body = BuildHtml(p)
        };
        try { await client.SendMailAsync(msg, ct); }
        catch (Exception ex) { _logger.LogError(ex, "[SMTP] فَشِل إرسال بَريد لِـ {Email}", email); }
    }

    private static string BuildHtml(NotificationPayload p)
    {
        var link = p.RelatedUrl is null ? "" :
            $"<p><a href=\"{p.RelatedUrl}\" style=\"background:#2563eb;color:#fff;padding:10px 16px;border-radius:6px;text-decoration:none;\">عَرض</a></p>";
        return $"<html dir=\"rtl\"><body style=\"font-family:Tahoma,Arial;line-height:1.6;\">" +
               $"<h2>{p.Title}</h2><p>{p.Body}</p>{link}</body></html>";
    }
}

/// <summary>اِستِخراج بَريد مُستَخدِم. يُنَفَّذ في التَّطبيق (يَقرَأ مِن
/// Marten/EF/أَيّاً كان مَصدَر المُستَخدِمين).</summary>
public interface IUserEmailLookup
{
    Task<string?> GetEmailAsync(Guid userId, CancellationToken ct);
}

public static class SmtpExtensions
{
    public static IServiceCollection AddSmtpNotifications(
        this IServiceCollection services, Action<SmtpOptions> configure)
    {
        services.Configure(configure);
        services.AddSingleton<INotificationChannel, SmtpNotificationChannel>();
        return services;
    }
}
