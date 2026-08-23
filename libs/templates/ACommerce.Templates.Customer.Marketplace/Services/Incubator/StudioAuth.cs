using ACommerce.Kit.Auth;
using ACommerce.Kit.Auth.Server;
using Marten;
using Microsoft.AspNetCore.Http;

namespace ACommerce.Templates.Customer.Marketplace.Services.Incubator;

/// <summary>
/// مُصادَقَة studio على مُستَوى المَنصَّة (مُنفَصِلَة عَن مُصادَقَة المَتاجِر).
/// تُخَزِّن <see cref="StudioUser"/> تَحت tenant "_studio" وَتَكتُب cookie
/// ".acommerce.studio". تُعيد استِخدام <c>AuthHandlers</c> بِـ tenant = "_studio":
/// نَفسُ تَوليدِ الرَمزِ وتَجزئَتِه ومُهلَتِه وحُدودِ مُعَدَّلِه.
///
/// <para><b>ما كانَ وما صار (‏2026-08-23)</b>: كانَ التَحَقُّقُ
/// <c>code.Trim() != DevCode</c> حَيثُ <c>DevCode = "123456"</c> ثابِتٌ
/// <b>بِلا شَرطِ بيئَة</b> — وهذا البابُ هُوَ المَوضِعُ الوَحيدُ الَّذي
/// يُنتِج جَلسَةَ مُشرِفِ مَنَصَّة. صارَ الرَمزُ يُطلَب عَبر قَناةٍ
/// مُسَجَّلَة (‏<see cref="AuthChannelSelection"/>)، عَشوائِيّاً مُجَزَّأً
/// بِمُهلَة؛ والثابِتُ <b>حُذِف</b>، فَلا سَبيلَ لَه إلى الإنتاجِ ولا إلى
/// التَطويرِ إلّا مِن <c>DevHintCode</c> لِقَناةٍ مُحاكيَة.</para>
/// </summary>
public sealed class StudioAuth
{
    public const string Tenant = "_studio";
    public const string CookieName = ".acommerce.studio";

    private readonly IHttpContextAccessor _http;
    public StudioAuth(IHttpContextAccessor http) => _http = http;

    public Guid? UserId { get; private set; }
    public string? UserName { get; private set; }
    public bool IsAuthenticated => UserId.HasValue;

    /// <summary>يُحَمِّل الحالَة مِن cookie الطَّلَب الحاليّ.</summary>
    public void Load()
    {
        var ctx = _http.HttpContext;
        if (ctx is null) return;
        var token = ctx.Request.Cookies[CookieName];
        var parsed = AuthHandlers.ParseToken(token);
        if (parsed is null || parsed.Value.TenantSlug != Tenant) { Clear(); return; }
        UserId = parsed.Value.UserId;
        UserName = ctx.Request.Cookies[CookieName + ".name"];
    }

    public void Clear() { UserId = null; UserName = null; }

    // ─── طَلَبُ الرَمز — عَبر القَناة المُسَجَّلَة وَحدَها ──────────────

    /// <summary>يُطَبِّع المُعَرِّف بِحَسَب طَريقَتِه. البَريد بِـ
    /// <see cref="EmailAddress.Normalize"/> — <b>نَفسِ الدالَّةِ بِعَينِها</b>
    /// الَّتي يُطَبِّعُ بِها <see cref="PlatformAdminGrant"/> قَبلَ المَنح.
    /// انحِرافُ المَوضِعَينِ يَمنَح الصَلاحِيَّةَ لِعُنوانٍ لا يُطابِقُه
    /// الدُخول — ثَغرَةً صامِتَةً بِلا رِسالَةِ خَطَإ.</summary>
    public static string NormalizeSubject(StudioAuthMethod method, string? subject)
        => method == StudioAuthMethod.Email
            ? EmailAddress.Normalize(subject ?? "")
            : (subject ?? "").Trim();

    /// <summary>يُصدِر رَمزاً ويُرسِلُه بِالهاتِف. يُعيد تَلميحَ المُحاكي
    /// (‏<c>null</c> مَع مُزَوِّدٍ فِعليّ). يَرمي <see cref="InvalidOperationException"/>
    /// عِندَ تَجاوُزِ الحَدّ، ويُمَرِّرُ ما تَرميه القَناةُ عِندَ فَشَلِ
    /// الإرسال.</summary>
    public static async Task<string?> SendPhoneCodeAsync(
        IOtpChannel channel, string phone, CancellationToken ct = default)
        => await SendAsync(StudioAuthMethod.Phone, phone, channel.DevHintCode,
            (code) => channel.SendOtpAsync(phone, code, ct));

    /// <summary>نَظيرُه لِلبَريد — نَفسُ الجِسمِ بِقَناةٍ أُخرى.</summary>
    public static async Task<string?> SendEmailCodeAsync(
        IEmailOtpChannel channel, string email, CancellationToken ct = default)
        => await SendAsync(StudioAuthMethod.Email, email, channel.DevHintCode,
            (code) => channel.SendOtpAsync(email, code, ct));

    private static async Task<string?> SendAsync(
        StudioAuthMethod method, string subject, string? devHint, Func<string, Task> send)
    {
        if (!AuthHandlers.TryConsumeSendQuota($"{Tenant}|{subject}"))
            throw new InvalidOperationException("rate_limited");
        var code = AuthHandlers.NewCode(devHint);
        AuthHandlers.IssueAttempt(Tenant, subject, code, StudioAuthDoor.Kind(method));
        await send(code);
        return devHint;
    }

    // ─── التَحَقُّق ────────────────────────────────────────────────────

    /// <summary>يَستَهلِك الرَمزَ ثُمَّ يُنشِئ أَو يُحَمِّل المُستَخدِمَ
    /// بِمُعَرِّفِه، يَكتُب الـcookie، ويُعيد الـuser. <c>null</c> = رَمزٌ
    /// خاطِئٌ أَو مُنتَهٍ أَو مُعَرِّفٌ فارِغ.
    ///
    /// <para><b>والبَريدُ مِفتاحُ هُوِيَّةٍ مُستَقِلّ</b> لا يُخلَط بِبَحثِ
    /// الهاتِف — وإلّا لَالتَقَطَ بَحثُ البَريدِ الفارِغِ أَوَّلَ مُستَخدِمِ
    /// هاتِف. نَفسُ عِلَّةِ <c>AuthHandlers.GetOrCreateUserAsync</c>
    /// و<c>PlatformAdminSeeder</c>.</para></summary>
    public static async Task<StudioUser?> VerifyAsync(
        IDocumentStore store, HttpResponse res,
        StudioAuthMethod method, string subject, string code)
    {
        subject = NormalizeSubject(method, subject);
        if (string.IsNullOrEmpty(subject)) return null;
        if (!AuthHandlers.TryConsumeVerifyQuota($"{Tenant}|{subject}")) return null;
        if (!AuthHandlers.ConsumeAttempt(
                Tenant, subject, (code ?? "").Trim(), StudioAuthDoor.Kind(method)))
            return null;

        await using var s = store.LightweightSession(Tenant);
        var user = method == StudioAuthMethod.Email
            ? (await s.Query<StudioUser>().Where(u => u.Email == subject).ToListAsync())
                .FirstOrDefault()
            : (await s.Query<StudioUser>().Where(u => u.Phone == subject).ToListAsync())
                .FirstOrDefault();
        user ??= method == StudioAuthMethod.Email
            ? new StudioUser { Id = Guid.NewGuid(), Email = subject }
            : new StudioUser { Id = Guid.NewGuid(), Phone = subject };
        user.LastLoginAt = DateTime.UtcNow;
        s.Store(user);
        await s.SaveChangesAsync();

        WriteCookie(res, user);
        return user;
    }

    public static void WriteCookie(HttpResponse res, StudioUser user)
    {
        var token = AuthHandlers.MakeToken(user.Id, Tenant);
        var opts = new CookieOptions
        {
            HttpOnly = true, IsEssential = true, SameSite = SameSiteMode.Lax,
            Path = "/", Expires = DateTimeOffset.UtcNow.AddDays(30)
        };
        res.Cookies.Append(CookieName, token, opts);
        res.Cookies.Append(CookieName + ".name", user.FullName,
            new CookieOptions { IsEssential = true, Path = "/", Expires = opts.Expires });
    }

    public static void DeleteCookie(HttpResponse res)
    {
        res.Cookies.Delete(CookieName);
        res.Cookies.Delete(CookieName + ".name");
    }
}
