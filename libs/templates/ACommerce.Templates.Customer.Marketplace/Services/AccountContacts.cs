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

    /// <summary>‏(‏2026-10-29، تَدقيقُ الموجَة ١١ A2) نَوعٌ لا تَعرِضُه صَفحَةُ دُخولِ المَتجَر (<see cref="LoginChannels"/>) — لا يُربَطُ وَسيلَةَ دُخول،
    /// فَنَصُّ «فَتَدخُلُ بَعدَها بِه» لا يَكذِب. مَتجَرُ نَفاذٍ لا يَعرِضُ غَيرَه.</summary>
    public const string NotOffered = "contact_not_offered";

    /// <remarks>‏(‏2026-10-29، A3) ورُموزُ فَكِّ الوَسيلَةِ المُضافَة (<see cref="ContactLink.UnlinkAll"/>) — والمُشتَرَكُ مَعَ الرَبطِ مَرَّةً واحِدَة.</remarks>
    public static readonly IReadOnlyList<string> Codes =
        ContactLink.All.Concat(new[] { Unavailable, SendFailed, CodeInvalid, NotOffered }).Concat(ContactLink.UnlinkAll).Distinct().ToList();

    /// <summary>نَوعُ الإشعارِ في التَطبيق (‏2026-10-29، A3) — أُضيفَت وَسيلَةُ دُخولٍ أَو حُذِفَت.</summary>
    public const string NoticeType = "account_contact";

    /// <summary>فِعلا سَطرِ التَدقيق — بِالمُعَرِّفِ والنَوعِ وَحدَهُما، لا بِالوَسيلَة.</summary>
    public const string AddedAction = "user.contact.add";
    public const string RemovedAction = "user.contact.remove";

    /// <summary>مَفاتيحُ نُصوصِ الإشعار — بِالعَرَبِيَّةِ صَراحَةً: تُخَزَّنُ في وَثيقَةٍ وتُقرَأُ في صُندوقِ رَسائِلَ لا في مُتَصَفِّح (نَسَقُ <c>OtpEmailMessage</c>).</summary>
    public const string AddedTitleKey = "notifications.contact_added.title";
    public const string AddedBodyKey = "notifications.contact_added.body";
    public const string RemovedTitleKey = "notifications.contact_removed.title";
    public const string RemovedBodyKey = "notifications.contact_removed.body";

    private static string Text(string key, params object[] args)
    {
        var text = ACommerce.Platform.I18n.LocaleCatalog.Find(ACommerce.Platform.I18n.LocaleCatalog.Arabic, key)
                   ?? throw new InvalidOperationException($"مِفتاحُ القامُوس «{key}» غائِبٌ في ar.json.");
        return args.Length == 0 ? text : string.Format(System.Globalization.CultureInfo.InvariantCulture, text, args);
    }

    public sealed record StartOutcome(string? AttemptId, string? Refusal);

    /// <summary>يَبدَأُ الرَبط: يَقرَأُ الحِساب، ويَسأَلُ <see cref="ContactLink.Validate"/> بِلا حامِلين، ثُمَّ يُرسِلُ الرَمزَ بِقَناةِ النَوع — وقَناةٌ غَيرُ مُسَجَّلَةٍ تُرَدُّ
    /// بِـ<see cref="Unavailable"/> (والصَفحَةُ لا تَرسُمُ استِمارَتَها أَصلاً)، وفَشَلُ الإرسالِ أَو الحِصَّةُ بِـ<see cref="SendFailed"/>.</summary>
    /// <remarks>‏(‏2026-10-29، A2) <paramref name="loginChannels"/> ما تَعرِضُه صَفحَةُ دُخولِ المَتجَر (<see cref="LoginChannels.Offered"/>) — ونَوعٌ خارِجَه
    /// يُرَدُّ بِـ<see cref="NotOffered"/> قَبلَ أَيِّ قِراءَة.</remarks>
    public static async Task<StartOutcome> StartAsync(
        IDocumentStore store, string slug, Guid userId, string? kind, string? raw,
        IOtpChannel? phoneChannel, IEmailOtpChannel? emailChannel, IReadOnlyList<string> loginChannels, CancellationToken ct)
    {
        if (kind is ContactLink.Email or ContactLink.Phone && !loginChannels.Contains(kind)) return new(null, NotOffered);
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
    /// <remarks>‏(‏2026-10-29، تَدقيقُ الموجَة ١١ A3) <b>والرَبطُ يُخطِرُ</b> (<see cref="NoticeLinkedAsync"/>): الوَسيلَةَ الأُخرى المُتَحَقَّقَ مِنها بِقَناتِها،
    /// وإشعاراً في التَطبيق، وسَطرَ تَدقيق — كانَ يَربِطُ ويَحفَظُ بِلا أَثَر.</remarks>
    /// <remarks>‏(‏2026-10-30، مُراجَعَةُ الموجَة ١١ لِلمَسار F1) <paramref name="admit"/> شَرطُ البابِ عَلى القِراءَةِ الجَديدَة (<c>SessionVersionGate.StillOpen</c>):
    /// جَلسَةٌ أُغلِقَت بَينَ البَوّابَةِ والقِراءَةِ تُرَدُّ بِـ<see cref="ContactLink.NoAccount"/> ولا يُربَطُ شَيء.</remarks>
    public static async Task<string?> VerifyAsync(
        IDocumentStore store, string slug, Guid userId, string? attemptId, string? code, DateTime now,
        IOtpChannel? phoneChannel, IEmailOtpChannel? emailChannel, CancellationToken ct, Func<User, bool>? admit = null)
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
        }, ct, admit);
        if (user is null) return ContactLink.Validate(null, kind, p.Value, holders)!.Code;
        if (refusal is not null) return refusal;
        await NoticeLinkedAsync(store, slug, user, kind, p.Value, now, phoneChannel, emailChannel, ct);
        return null;
    }

    /// <summary>
    /// <para><b>إشعارُ الرَبط</b> (‏2026-10-29، تَدقيقُ الموجَة ١١ A3؛ مَرجِعُ P1: ‏OWASP ASVS 2.2.3 وNIST 800-63B): إشعارٌ في التَطبيقِ وسَطرُ تَدقيقٍ دائِماً،
    /// ورِسالَةٌ إلى الوَسيلَةِ <b>الأُخرى المُتَحَقَّقِ مِنها</b> بِقَناتِها (<c>SendNoticeAsync</c> خَلفَ <c>OtpSendGuard</c>) — بِالوَسيلَةِ الجَديدَةِ مُقَنَّعَة.
    /// والرِسالَةُ أَثَرٌ لا شَرط: فَشَلُها لا يَرُدُّ رَبطاً تَمَّ، ويُعادُ <c>failed</c>. يُعيدُ القَناةَ الَّتي حَمَلَت الرِسالَة: <c>email</c> أَو <c>phone</c>
    /// أَو <c>none</c> (لا وَسيلَةَ أُخرى مُتَحَقَّقٌ مِنها، أَو قَناتُها غَيرُ مُسَجَّلَة) أَو <c>failed</c>.</para>
    /// </summary>
    public static async Task<string> NoticeLinkedAsync(
        IDocumentStore store, string slug, User user, string kind, string value, DateTime now,
        IOtpChannel? phoneChannel, IEmailOtpChannel? emailChannel, CancellationToken ct)
    {
        var title = Text(AddedTitleKey);
        var body = Text(AddedBodyKey, ContactLink.Mask(kind, value));
        await RecordAsync(store, slug, user.Id, title, body, AddedAction, kind, now, ct);

        var other = ContactLink.Other(kind);
        var to = ContactLink.Contact(user, other);
        var verified = other == ContactLink.Email ? user.EmailVerified : user.PhoneVerified;
        if (to is null || !verified) return "none";
        try
        {
            if (other == ContactLink.Email)
            {
                if (emailChannel is null) return "none";
                await OtpSendGuard.SendWithinAsync(t => emailChannel.SendNoticeAsync(to, title, body, t), ct);
                return ContactLink.Email;
            }
            if (phoneChannel is null) return "none";
            await OtpSendGuard.SendWithinAsync(t => phoneChannel.SendNoticeAsync(AuthHandlers.PhoneDestination(to), body, t), ct);
            return ContactLink.Phone;
        }
        catch (Exception) { return "failed"; }
    }

    /// <summary>الإشعارُ في التَطبيقِ وسَطرُ التَدقيق — لِلرَبطِ والحَذف. والتَدقيقُ بِالمُعَرِّفِ والنَوعِ وَحدَهُما (لا اسمَ ولا وَسيلَة).</summary>
    private static async Task RecordAsync(IDocumentStore store, string slug, Guid userId, string title, string body, string action, string kind,
        DateTime now, CancellationToken ct)
    {
        await using (var s = store.LightweightSession(slug))
        {
            s.Store(new ACommerce.Kit.Notifications.Notification
            {
                Id = Guid.NewGuid(), UserId = userId, Type = NoticeType, Title = title, Body = body, RelatedUrl = $"/{slug}/me", At = now,
            });
            await s.SaveChangesAsync(ct);
        }
        await new Audit.AuditWriter(store).WriteAsync(slug, userId, "", action, "User", userId.ToString(), note: kind, ct: ct);
    }

    // ─── حَذفُ الوَسيلَةِ المُضافَة (‏2026-10-29، تَدقيقُ الموجَة ١١ A3) ───────────────────────────────

    /// <summary>
    /// <para>يَبدَأُ الحَذف: <see cref="ContactLink.ValidateUnlink"/> عَلى الحِساب، ثُمَّ رَمزٌ إلى الوَسيلَةِ <b>الباقِيَة</b> بِآلِيَّةِ الدُخولِ نَفسِها
    /// (<see cref="AuthHandlers.RequestContactCodeAsync"/> بِـ<c>RemoveContact</c>)، ومَوضوعُه الحِسابُ والنَوعُ المَحذوف. قَناةٌ غَيرُ مُسَجَّلَةٍ ←
    /// <see cref="Unavailable"/>، وفَشَلُ الإرسالِ أَو الحِصَّةُ ← <see cref="SendFailed"/>.</para>
    /// </summary>
    public static async Task<StartOutcome> StartRemoveAsync(
        IDocumentStore store, string slug, Guid userId, string? kind,
        IOtpChannel? phoneChannel, IEmailOtpChannel? emailChannel, CancellationToken ct)
    {
        User? user;
        await using (var q = store.QuerySession(slug)) user = await q.LoadAsync<User>(userId, ct);
        if (ContactLink.ValidateUnlink(user, kind) is { } refused) return new(null, refused.Code);
        var remaining = ContactLink.Other(kind!);
        var to = ContactLink.Contact(user!, remaining)!;
        var subject = ContactLink.Subject(userId, kind!);
        try
        {
            if (remaining == ContactLink.Email)
                return emailChannel is null ? new(null, Unavailable)
                    : new(await AuthHandlers.RequestContactCodeAsync(slug, AuthHandlers.AuthKind.RemoveContact, subject, to,
                        emailChannel.DevHintCode, emailChannel.SendOtpAsync, ct), null);
            return phoneChannel is null ? new(null, Unavailable)
                : new(await AuthHandlers.RequestContactCodeAsync(slug, AuthHandlers.AuthKind.RemoveContact, subject, AuthHandlers.PhoneDestination(to),
                    phoneChannel.DevHintCode, phoneChannel.SendOtpAsync, ct), null);
        }
        catch (Exception) { return new(null, SendFailed); }
    }

    /// <summary>مُحاوَلَةُ حَذفٍ حَيَّةٌ بِمُعَرِّفِها لِهذا الحِساب — النَوعُ المَحذوف؛ وإلّا <c>null</c>.</summary>
    public static string? PendingRemoval(string slug, string? attemptId, Guid userId)
        => AuthHandlers.SubjectOf(slug, attemptId, AuthHandlers.AuthKind.RemoveContact) is { } subject
           && ContactLink.ValueFor(subject, userId) is { } kind && ContactLink.Kinds.Contains(kind) ? kind : null;

    /// <summary>
    /// <para>يُتِمُّ الحَذف: المُحاوَلَةُ لِهذا الحِساب، وحِصَّةُ التَحَقُّق، واستِهلاكُ الرَمز — ثُمَّ <see cref="ContactLink.ValidateUnlink"/> و<see cref="ContactLink.Unlink"/>
    /// عَلى قِراءَةٍ جَديدَة (<see cref="AccountWrites.UpdateAsync{T}"/>)، ثُمَّ إشعارٌ في التَطبيقِ وسَطرُ تَدقيق. <c>null</c> = حُذِفَت؛ وإلّا رَمزُ الرَفض.</para>
    /// </summary>
    /// <remarks>‏(‏2026-10-30) و<paramref name="admit"/> كَـ<see cref="VerifyAsync"/>.</remarks>
    public static async Task<string?> VerifyRemoveAsync(
        IDocumentStore store, string slug, Guid userId, string? attemptId, string? code, DateTime now, CancellationToken ct,
        Func<User, bool>? admit = null)
    {
        if (PendingRemoval(slug, attemptId, userId) is not { } kind) return CodeInvalid;
        var subject = ContactLink.Subject(userId, kind);
        if (!AuthHandlers.TryConsumeVerifyQuota($"{slug}|{subject}")
            || !AuthHandlers.ConsumeAttempt(slug, subject, (code ?? "").Trim(), AuthHandlers.AuthKind.RemoveContact)) return CodeInvalid;

        string? refusal = null;
        var removed = "";
        var user = await AccountWrites.UpdateAsync<User>(store, slug, userId, u =>
        {
            refusal = ContactLink.ValidateUnlink(u, kind)?.Code;
            if (refusal is not null) return false;
            removed = ContactLink.Contact(u, kind)!;
            ContactLink.Unlink(u, kind, now);
            return true;
        }, ct, admit);
        if (user is null) return ContactLink.NoAccount;
        if (refusal is not null) return refusal;
        await RecordAsync(store, slug, userId, Text(RemovedTitleKey), Text(RemovedBodyKey, ContactLink.Mask(kind, removed)), RemovedAction, kind, now, ct);
        return null;
    }

    /// <summary>مَسارُ «حِسابي» بَعدَ الحَذف — نَقِيَّة، نَسَقُ <see cref="AfterVerify"/>: حُذِفَت ← <c>contact=removed</c>؛ رَمزٌ خاطِئٌ ومُحاوَلَتُه حَيَّة ←
    /// مَرحَلَةُ الرَمزِ بِمُعَرِّفِها؛ وما سِواهُما ← رَمزُ الرَفض.</summary>
    public static string AfterRemove(string slug, Guid userId, string? attemptId, string? refusal)
        => refusal is null ? "me?contact=removed"
         : refusal == CodeInvalid && PendingRemoval(slug, attemptId, userId) is not null
            ? $"me?contact=remove&attempt={Uri.EscapeDataString(attemptId!)}&err={refusal}"
            : $"me?err={refusal}";

    /// <summary>مَسارُ «حِسابي» بَعدَ التَحَقُّق — نَقِيَّةٌ عَلى حالِ المُحاوَلَة: رُبِطَت ← <c>contact=linked</c>؛ رَمزٌ خاطِئٌ ومُحاوَلَتُه حَيَّة ← مَرحَلَةُ الرَمزِ
    /// بِمُعَرِّفِها ورَمزِ الخَطَإ؛ وما سِواهُما ← رَمزُ الرَفضِ وَحدَه. ولا وَسيلَةَ في رابِطٍ أَبَداً.</summary>
    public static string AfterVerify(string slug, Guid userId, string? attemptId, string? refusal)
        => refusal is null ? "me?contact=linked"
         : refusal == CodeInvalid && Pending(slug, attemptId, userId) is not null
            ? $"me?contact=verify&attempt={Uri.EscapeDataString(attemptId!)}&err={refusal}"
            : $"me?err={refusal}";
}
