using ACommerce.Kit.Auth;
using ACommerce.Kit.Auth.Server;
using Marten;

namespace ACommerce.Templates.Customer.Marketplace.Services;

/// <summary>
/// <para><b>«أَضِف بَريدَك» و«أَضِف جَوّالَك» مِن «حِسابي»</b> (‏2026-10-28، قَرارُ المالِك د27 «ج»؛ NIST 800-63B). القَرارُ النَقِيُّ في
/// <see cref="ContactLink"/> (النَواة)، وهُنا ما يَحتاجُ مَخزَناً وقَناة: قِراءَةُ الحِسابِ في جَلسَةِ مُستَأجِرِه، وإرسالُ الرَمزِ بِآلِيَّةِ الدُخولِ نَفسِها
/// (<see cref="AuthHandlers.RequestContactCodeAsync"/>)، واستِهلاكُه، والبَحثُ عَمَّن يَحمِلُ الوَسيلَةَ، والحِفظ. فَجِسما النُقطَتَينِ نِداءٌ ورَدّ
/// (‏<c>EndpointBodyBleedTests</c>)، والصَفحَةُ تَسأَلُ <see cref="Pending"/> نَفسَها عَن المُحاوَلَةِ الحَيَّة.</para>
/// </summary>
public static class AccountContacts
{
    /// <summary>رُموزُ الرَدِّ خارِجَ مَعجَمِ <see cref="ContactLink"/> — ما يَخُصُّ القَناةَ والرَمز. والصَفحَةُ تُتَرجِمُها بِمَفاتيحِ القامُوس.</summary>
    public const string Unavailable = "contact_unavailable";
    public const string SendFailed = "contact_send_failed";
    public const string CodeInvalid = "contact_code_invalid";

    public static readonly IReadOnlyList<string> Codes =
        ContactLink.All.Concat(new[] { Unavailable, SendFailed, CodeInvalid }).ToList();

    public sealed record StartOutcome(string? AttemptId, string? Refusal);

    /// <summary>يَبدَأُ الرَبط: يَقرَأُ الحِساب، ويَسأَلُ <see cref="ContactLink.Validate"/> بِلا حامِلين، ثُمَّ يُرسِلُ الرَمزَ بِقَناةِ النَوع — وقَناةٌ غَيرُ مُسَجَّلَةٍ تُرَدُّ
    /// بِـ<see cref="Unavailable"/> (والصَفحَةُ لا تَرسُمُ استِمارَتَها أَصلاً)، وفَشَلُ الإرسالِ أَو الحِصَّةُ بِـ<see cref="SendFailed"/>.</summary>
    public static async Task<StartOutcome> StartAsync(
        IDocumentStore store, string slug, Guid userId, string? kind, string? raw,
        IOtpChannel? phoneChannel, IEmailOtpChannel? emailChannel, CancellationToken ct)
    {
        var value = ContactLink.Normalize(kind, raw);
        User? user;
        await using (var q = store.QuerySession(slug)) user = await q.LoadAsync<User>(userId, ct);
        if (ContactLink.Validate(user, kind, value) is { } refused) return new(null, refused.Code);
        try
        {
            if (kind == ContactLink.Email)
                return emailChannel is null ? new(null, Unavailable)
                    : new(await AuthHandlers.RequestContactCodeAsync(slug, AuthHandlers.AuthKind.AddEmail, ContactLink.Subject(userId, value),
                        value, emailChannel.DevHintCode, emailChannel.SendOtpAsync, ct), null);
            return phoneChannel is null ? new(null, Unavailable)
                : new(await AuthHandlers.RequestContactCodeAsync(slug, AuthHandlers.AuthKind.AddPhone, ContactLink.Subject(userId, value),
                    AuthHandlers.PhoneDestination(value), phoneChannel.DevHintCode, phoneChannel.SendOtpAsync, ct), null);
        }
        catch (Exception) { return new(null, SendFailed); }
    }

    /// <summary>مُحاوَلَةُ رَبطٍ حَيَّةٌ بِمُعَرِّفِها لِهذا الحِسابِ في هذا المَتجَر — نَوعُها ومَوضوعُها والوَسيلَة؛ وإلّا <c>null</c>.</summary>
    public static (AuthHandlers.AuthKind Kind, string Subject, string Value)? Pending(string slug, string? attemptId, Guid userId)
    {
        foreach (var k in new[] { AuthHandlers.AuthKind.AddEmail, AuthHandlers.AuthKind.AddPhone })
            if (AuthHandlers.SubjectOf(slug, attemptId, k) is { } subject && ContactLink.ValueFor(subject, userId) is { } value)
                return (k, subject, value);
        return null;
    }

    public static string KindName(AuthHandlers.AuthKind kind) => kind == AuthHandlers.AuthKind.AddEmail ? ContactLink.Email : ContactLink.Phone;

    /// <summary>
    /// <para>يُتِمُّ الرَبط: المُحاوَلَةُ لِهذا الحِساب، وحِصَّةُ التَحَقُّق، واستِهلاكُ الرَمز — ثُمَّ مَن يَحمِلُ الوَسيلَةَ في المَتجَر (البَريدُ بِتَطبيعِه، والجَوّالُ
    /// كَما كُتِبَ وبِصيغَتِه الدَولِيَّة) و<see cref="ContactLink.Validate"/> بِهِم، ثُمَّ <see cref="ContactLink.Link"/> والحِفظ. <c>null</c> = رُبِطَت؛ وإلّا رَمزُ الرَفض.</para>
    /// </summary>
    public static async Task<string?> VerifyAsync(
        IDocumentStore store, string slug, Guid userId, string? attemptId, string? code, DateTime now, CancellationToken ct)
    {
        if (Pending(slug, attemptId, userId) is not { } p) return CodeInvalid;
        if (!AuthHandlers.TryConsumeVerifyQuota($"{slug}|{p.Subject}")
            || !AuthHandlers.ConsumeAttempt(slug, p.Subject, (code ?? "").Trim(), p.Kind)) return CodeInvalid;

        var kind = KindName(p.Kind);
        var dest = AuthHandlers.PhoneDestination(p.Value);
        List<User> holders;
        await using (var q = store.QuerySession(slug))
            holders = kind == ContactLink.Email
                ? (await q.Query<User>().Where(u => u.Email == p.Value).ToListAsync(ct)).ToList()
                : (await q.Query<User>().Where(u => u.Phone == p.Value || u.Phone == dest).ToListAsync(ct)).ToList();

        // ‏(‏2026-10-29، تَدقيقُ الموجَة ١١ G-1) القَرارُ والرَبطُ عَلى قِراءَةٍ جَديدَةٍ لِلحِساب (`AccountWrites.UpdateAsync`)، ويُعادانِ مَرَّةً إن سَبَقَه
        // كاتِب — كانَ حِفظُه الكامِلُ يُعيدُ إصدارَ جَلسَةٍ رُفِعَ بَينَ القِراءَةِ والحِفظ. وحِسابٌ لا وَثيقَةَ لَه يُرَدُّ بِرَمزِه كَما كان.
        string? refusal = null;
        var user = await AccountWrites.UpdateAsync<User>(store, slug, userId, u =>
        {
            refusal = ContactLink.Validate(u, kind, p.Value, holders)?.Code;
            if (refusal is not null) return false;
            ContactLink.Link(u, kind, p.Value, now);
            return true;
        }, ct);
        return user is null ? ContactLink.Validate(null, kind, p.Value, holders)!.Code : refusal;
    }

    /// <summary>مَسارُ «حِسابي» بَعدَ التَحَقُّق — نَقِيَّةٌ عَلى حالِ المُحاوَلَة: رُبِطَت ← <c>contact=linked</c>؛ رَمزٌ خاطِئٌ ومُحاوَلَتُه حَيَّة ← مَرحَلَةُ الرَمزِ
    /// بِمُعَرِّفِها ورَمزِ الخَطَإ؛ وما سِواهُما ← رَمزُ الرَفضِ وَحدَه. ولا وَسيلَةَ في رابِطٍ أَبَداً.</summary>
    public static string AfterVerify(string slug, Guid userId, string? attemptId, string? refusal)
        => refusal is null ? "me?contact=linked"
         : refusal == CodeInvalid && Pending(slug, attemptId, userId) is not null
            ? $"me?contact=verify&attempt={Uri.EscapeDataString(attemptId!)}&err={refusal}"
            : $"me?err={refusal}";
}
