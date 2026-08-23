using ACommerce.Platform.I18n;
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

    /// <summary>
    /// مُهلَةُ الإرسالِ كامِلاً بِالثَواني — اتِّصالاً واعتِماداً وتَسليماً.
    /// الافتِراضيّ <see cref="OtpSendGuard.DefaultTimeoutSeconds"/>، والصِفرُ
    /// أَو السالِبُ يَرتَدُّ إلَيه (لا «بِلا مُهلَة»).
    ///
    /// <para><b>المَقيسُ الَّذي أَضافَها (‏2026-08-23)</b>: الـSpace يَحجُب
    /// المَنافِذَ الصادِرَةَ ‏587/465، فَـ<c>ConnectAsync</c> بِلا مُهلَةٍ
    /// عَلَّقَ الطَلَبَ أَكثَرَ مِن ‏90 ثانِيَة بِلا رَدّ — لا خَطَأً
    /// يَقرَؤُه المُستَخدِمُ ولا صَفحَةَ رَمز. التَهيئَة:
    /// <c>Auth__Email__TimeoutSeconds</c>.</para>
    /// </summary>
    public int TimeoutSeconds { get; set; } = OtpSendGuard.DefaultTimeoutSeconds;
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
        // ‏ADR-001 (أ): المَصرِفُ الوَحيدُ الَّذي يَعُدُّه فاحِصُ الطَبَقَة ٧
        // خارِجَ `.razor`. وكانَ حاجِزُه اتِّجاهَ الاعتِماد — عُدَّةٌ لا
        // تُرجِع إلى مَشروعِ القالِب حَيثُ كانَ `L` — فَسَقَطَ بِنُزول
        // المَنفَذ إلى مَشروعٍ وَرَقيّ.
        //
        // **ولِماذا `LocaleCatalog` لا `L`**: ‏`L` نِطاقُه الطَلَب،
        // ويَقرَأ لُغَتَه مِن كوكي المُتَصَفِّح. وهذِه الرِسالَةُ تُرسَل
        // في مَسارٍ قَد لا يَملِك طَلَباً، **وتُقرَأ في صُندوقِ بَريدٍ
        // لا في مُتَصَفِّح** — فَلُغَةُ الكوكي لَيسَت لُغَةَ القارِئ.
        // فَالعَرَبِيَّةُ صَراحَةً، وهي المَعجَمُ الإلزاميّ.
        //
        // **وجِسمُ الرِسالَة صارَ في `OtpEmailMessage`** — مَوضِعٌ واحِدٌ
        // تَقرَؤُه قَناتا البَريدِ الفِعليَّتان. ولِماذا هُوَ حَرفِيٌّ لا
        // في القامُوس: مَشروحٌ هُناك (‏`value_unsafe_markup`).
        msg.Subject = LocaleCatalog.Text(LocaleCatalog.Arabic, OtpEmailMessage.SubjectKey);
        msg.Body = new BodyBuilder
        {
            TextBody = OtpEmailMessage.Text(code),
            HtmlBody = OtpEmailMessage.Html(code)
        }.ToMessageBody();

        // المَنفَذ ٤٦٥ تَشفير ضِمنيّ؛ ما دونه STARTTLS متى دَعَمَه الخادِم.
        var security = _opts.Port == 465
            ? SecureSocketOptions.SslOnConnect
            : SecureSocketOptions.StartTlsWhenAvailable;

        // ── المُهلَة: رَمزٌ مَربوطٌ بِرَمزِ الطَلَب، ومُؤَقِّتٌ مَعَه ──
        // الـSpace يَحجُب ‏587/465 الصادِرَين، فَالمُصافَحَةُ تَعلَق عِندَ
        // انتِظارِ تَحيَّةِ الخادِم (‏`220 …`) الَّتي لا تَأتي أَبَداً.
        // و`SmtpClient.Timeout` وَحدَه لا يَكفي — يَحرُسُ عَمَلِيّاتِ
        // القِراءَةِ والكِتابَةِ بَعدَ الاتِّصال، لا اتِّصالَ المِقبَسِ
        // نَفسَه. فَالرَمزُ هُوَ الحارِس، والخاصِّيَّةُ تَعضُدُه.
        var window = OtpSendGuard.Timeout(_opts.TimeoutSeconds);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(window);

        // **ولِماذا ضِعفُ النافِذَةِ لا النافِذَةَ نَفسَها**: لَو تَساوَيا
        // تَسابَقَ حارِسان على المُهلَةِ نَفسِها — فَمَرَّةً يَسبِق الرَمزُ
        // فَتُقال «تَجاوُزُ مُهلَة»، ومَرَّةً تَسبِق الخاصِّيَّةُ فَيُقال
        // خَطَأُ MailKit العامّ. قيسَ حَيّاً: بابانِ مُتَتالِيانِ بِنَفسِ
        // الإعدادِ أَعطَيا رِسالَتَين. فَالرَمزُ يَسبِقُ دائِماً،
        // والخاصِّيَّةُ سَقفٌ احتِياطيٌّ لَو سَقَطَ الرَمز.
        using var client = new SmtpClient { Timeout = (int)window.TotalMilliseconds * 2 };
        try
        {
            await client.ConnectAsync(_opts.Host, _opts.Port, security, cts.Token);
            if (!string.IsNullOrEmpty(_opts.Username))
                await client.AuthenticateAsync(_opts.Username, _opts.Password, cts.Token);
            await client.SendAsync(msg, cts.Token);
            await client.DisconnectAsync(quit: true, cts.Token);
            _logger.LogInformation("[Smtp] أُرسِلَ كود لِـ {Email}", email);
        }
        // **والحُكمُ لِرَمزِنا لا لِنَوعِ الاستِثناء** — بِقياسٍ حَيّ:
        // ‏MailKit يَرمي `OperationCanceledException` حينَ يُلغى أَثناءَ
        // قِراءَةِ التَحيَّة، ويَرمي خَطَأَ مِقبَسٍ عادِيّاً حينَ يُلغى
        // أَثناءَ الاتِّصالِ نَفسِه. فَبابانِ مُتَتالِيانِ بِنَفسِ الإعدادِ
        // أَعطَيا رِسالَتَينِ مُختَلِفَتَين. والسُؤالُ الصَحيحُ لَيسَ «ماذا
        // رَمى؟» بَل **«هَل أَلغَينا نَحن؟»**.
        catch (Exception) when (cts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            // تَجاوُزُ المُهلَة — لا انقِطاعُ طَلَب. يُقال بِاسمِه، ويَصير
            // `send_failed` عِندَ النُقطَة بَدَلَ صَفحَةٍ تَدور.
            var message = OtpSendGuard.TimeoutMessage(window);
            _logger.LogError("[Smtp] {Message} — المُضيف {Host}:{Port}",
                message, _opts.Host, _opts.Port);
            throw new InvalidOperationException("SMTP " + message);
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
