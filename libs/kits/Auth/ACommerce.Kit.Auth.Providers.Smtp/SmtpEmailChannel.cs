using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace ACommerce.Kit.Auth.Providers.Smtp;

/// <summary>
/// إعدادات SMTP — تُقرَأ مِن التَهيئَة حَصراً تَحت المِفتاح
/// <c>Auth:Email</c> (‏<c>Host</c>، <c>Port</c>، <c>Username</c>،
/// <c>Password</c>، <c>From</c>). لا قيمَة سِرِّيَّة مَكتوبَة في الكود
/// ولا افتِراض لِمُزَوِّد بِعَينِه: يَعمَل مَع أَيّ SMTP قِياسيّ — بِما
/// فيه Azure Communication Services وAmazon SES وGoogle Workspace — لِأَنّ
/// الفَرق بَينَها مُضيف ومَنفَذ واعتِماد فَقَط.
/// </summary>
public sealed class SmtpEmailOptions
{
    public string Host { get; set; } = "";
    /// <summary>٥٨٧ = STARTTLS (الشائِع)، ٤٦٥ = TLS ضِمنيّ، ٢٥ = بِلا تَشفير.</summary>
    public int Port { get; set; } = 587;
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    /// <summary>عُنوان المُرسِل. يَقبَل <c>اسم &lt;bريد@نِطاق&gt;</c> أَو
    /// عُنواناً مُجَرَّداً.</summary>
    public string From { get; set; } = "";
    /// <summary>اسم المُرسِل الظاهِر حينَ يَكون <see cref="From"/> عُنواناً
    /// مُجَرَّداً.</summary>
    public string FromName { get; set; } = "";
}

/// <summary>
/// قَناة OTP بَريديَّة فِعليَّة عَبر MailKit. تُرسِل رِسالَة نَصّ + HTML
/// بِسيطَة تَحمِل الرَمز، ولا تَعرِف شَيئاً عَن تَوليده أَو تَخزينه —
/// ذلِك كُلُّه في <c>AuthHandlers</c> المُشتَرَك مَع قَناة الهاتِف.
/// </summary>
public sealed class SmtpEmailChannel : IEmailOtpChannel
{
    private readonly SmtpEmailOptions _opts;
    private readonly ILogger<SmtpEmailChannel> _logger;

    public SmtpEmailChannel(IOptions<SmtpEmailOptions> opts, ILogger<SmtpEmailChannel> logger)
    {
        _opts = opts.Value;
        _logger = logger;
        if (string.IsNullOrWhiteSpace(_opts.Host))
            throw new InvalidOperationException("SMTP Host غَير مُعَرَّف (Auth:Email:Host).");
        if (string.IsNullOrWhiteSpace(_opts.From))
            throw new InvalidOperationException("SMTP From غَير مُعَرَّف (Auth:Email:From).");
    }

    public string ChannelName => "Smtp";

    /// <summary><c>null</c> — قَناة إنتاج: لا كود مَعروض في الواجِهَة.</summary>
    public string? DevHintCode => null;

    public async Task SendOtpAsync(string email, string code, CancellationToken ct)
    {
        var msg = new MimeMessage();
        msg.From.Add(ParseFrom());
        msg.To.Add(MailboxAddress.Parse(email));
        msg.Subject = "رَمز التَّحَقُّق";
        msg.Body = new BodyBuilder
        {
            TextBody = $"رَمز التَّحَقُّق: {code}\nصالِح لِعَشر دَقائِق. إن لَم تَطلُبه فَتَجاهَل هذِه الرِّسالَة.",
            HtmlBody = $"""
                <html dir="rtl"><body style="font-family:Tahoma,Arial;line-height:1.6;">
                <p>رَمز التَّحَقُّق الخاصّ بِك:</p>
                <p style="font-size:28px;font-weight:700;letter-spacing:.3em;direction:ltr;">{code}</p>
                <p style="color:#6b7280;font-size:13px;">صالِح لِعَشر دَقائِق. إن لَم تَطلُبه فَتَجاهَل هذِه الرِّسالَة.</p>
                </body></html>
                """
        }.ToMessageBody();

        // المَنفَذ ٤٦٥ تَشفير ضِمنيّ؛ ما دونه STARTTLS متى دَعَمَه الخادِم.
        var security = _opts.Port == 465
            ? SecureSocketOptions.SslOnConnect
            : SecureSocketOptions.StartTlsWhenAvailable;

        using var client = new SmtpClient();
        try
        {
            await client.ConnectAsync(_opts.Host, _opts.Port, security, ct);
            if (!string.IsNullOrEmpty(_opts.Username))
                await client.AuthenticateAsync(_opts.Username, _opts.Password, ct);
            await client.SendAsync(msg, ct);
            await client.DisconnectAsync(quit: true, ct);
            _logger.LogInformation("[Smtp] أُرسِلَ كود لِـ {Email}", email);
        }
        catch (Exception ex)
        {
            // نَرمي (لا نَبتَلِع) — بِخِلاف الإشعارات، فَشَل إرسال OTP
            // يَعني أَنّ المُستَخدِم لَن يَستَطيع الدُّخول، فَيَجِب أَن
            // يَظهَر لَه خَطَأ لا شاشَة انتِظار كاذِبَة.
            _logger.LogError(ex, "[Smtp] فَشِل إرسال كود لِـ {Email}", email);
            throw new InvalidOperationException("SMTP فَشِل الإرسال: " + ex.Message, ex);
        }
    }

    private MailboxAddress ParseFrom()
        => MailboxAddress.TryParse(_opts.From, out var parsed) && string.IsNullOrEmpty(_opts.FromName)
            ? parsed
            : new MailboxAddress(
                string.IsNullOrEmpty(_opts.FromName) ? "" : _opts.FromName,
                MailboxAddress.Parse(_opts.From).Address);
}

public static class SmtpEmailExtensions
{
    /// <summary>يُسَجِّل قَناة SMTP كَتَنفيذ لِـ <see cref="IEmailOtpChannel"/>.
    /// الإعدادات تُمَرَّر مِن التَهيئَة في التَّطبيق المُضيف — نَفس نَمَط
    /// <c>AddUnifonicSmsChannel</c>.</summary>
    public static IServiceCollection AddSmtpEmailChannel(
        this IServiceCollection services, Action<SmtpEmailOptions> configure)
    {
        services.Configure(configure);
        services.AddSingleton<IEmailOtpChannel, SmtpEmailChannel>();
        return services;
    }
}
