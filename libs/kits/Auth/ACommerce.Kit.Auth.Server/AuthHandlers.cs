using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using ACommerce.Platform.Shared;
using Marten;
using Wolverine.Http;

namespace ACommerce.Kit.Auth.Server;

/// <summary>
/// Handlers الـ Auth يَستَهلِكون مُزَوِّدين عَبر <see cref="IOtpChannel"/> و
/// <see cref="INafathChannel"/>. التَطبيق يَختار التَنفيذ (Mock، Twilio،
/// Unifonic، Nafath الحَقيقيّ، …) ويُسَجِّله في DI.
/// </summary>
public static class AuthHandlers
{
    public static readonly ConcurrentDictionary<string, AuthAttempt> Attempts = new();

    public sealed record AuthAttempt(
        string Id, string TenantSlug, string Subject, string CodeHash,
        DateTime ExpiresAt, AuthKind Kind);

    public enum AuthKind { PhoneOtp, Nafath }

    // ─── Rate-limit OTP ───────────────────────────────────────────────────
    // كانَ بِلا حُدود — مُهاجِم يَستَطيع تَجريب ١٠ٰ٠٠٠ OTP في الثانِيَة على
    // نَفس الرَقم. الآن: ٥ مُحاوَلات/دَقيقَة لِكُلّ phone+tenant، و١٠
    // طَلَبات/ساعَة لِلإرسال. الـ counters تُمسَح تِلقائيّاً بَعد النافِذَة.
    private sealed record RateBucket(int Count, DateTime ResetAt);
    private static readonly ConcurrentDictionary<string, RateBucket> _verifyRl = new();
    private static readonly ConcurrentDictionary<string, RateBucket> _requestRl = new();
    private const int MaxVerifyPerMinute = 5;
    private const int MaxRequestPerHour = 10;

    /// <summary>هَل تُسمَح المُحاوَلَة؟ يُحَدِّث العَدّاد ذَرّيّاً.</summary>
    private static bool TryConsume(ConcurrentDictionary<string, RateBucket> rl,
        string key, int max, TimeSpan window)
    {
        var now = DateTime.UtcNow;
        while (true)
        {
            var current = rl.GetOrAdd(key, _ => new RateBucket(0, now.Add(window)));
            // النافِذَة انتَهَت — أَعِد بَدء العَدّاد.
            if (current.ResetAt < now)
            {
                var fresh = new RateBucket(1, now.Add(window));
                if (rl.TryUpdate(key, fresh, current)) return true;
                continue;
            }
            if (current.Count >= max) return false;
            var next = current with { Count = current.Count + 1 };
            if (rl.TryUpdate(key, next, current)) return true;
        }
    }

    /// <summary>سِرّ تَوقيع الـ token — يُحَمَّل مِن ENV عِندَ بَدء التَّطبيق.
    /// لَو فارِغ نَستَخدِم سِرّ افتِراضيّ (تَطوير فَقَط)؛ في الإنتاج
    /// ضَع <c>ACOMMERCE_AUTH_SECRET</c> بِـ ٣٢+ حَرفاً عَشوائِيّاً.</summary>
    public static string TokenSecret { get; set; } =
        Environment.GetEnvironmentVariable("ACOMMERCE_AUTH_SECRET")
        ?? "dev-only-secret-please-set-ACOMMERCE_AUTH_SECRET-in-production-32+";

    // ── Phone OTP ─────────────────────────────────────────────────────
    // ملاحَظَة: لا [WolverinePost] هُنا. القالِب (MarketplaceTemplate) يُسَجِّل
    // المَسار عَبر MapPost ويَستَدعي هذِه الدالَّة، ثُمَّ يَكتُب الـ cookie
    // ويُعيد التَّوجيه. تَسجيل Wolverine لِنَفس المَسار كانَ يَلتَقِط طَلَبات
    // JSON ويُرجِع AuthResult بِلا cookie — فَكَسَرَ تَسجيل الدُّخول بِالكامِل.
    public static async Task<OtpRequestResult> RequestPhoneOtpHandler(
        RequestPhoneOtp cmd, ITenantContext tenantCtx,
        IOtpChannel channel, CancellationToken ct)
    {
        if (!tenantCtx.IsResolved) throw new InvalidOperationException("tenant_required");
        // حِماية مِن سپام الإرسال (تَكلِفَة SMS فِعلِيَّة).
        var rlKey = $"{tenantCtx.Slug}|{cmd.Phone}";
        if (!TryConsume(_requestRl, rlKey, MaxRequestPerHour, TimeSpan.FromHours(1)))
            throw new InvalidOperationException("rate_limited: try again in an hour");

        var code = channel.DevHintCode ?? Random.Shared.Next(100000, 999999).ToString();
        var attemptId = Guid.NewGuid().ToString("N");
        Attempts[attemptId] = new AuthAttempt(
            attemptId, tenantCtx.Slug, cmd.Phone, Hash(code),
            DateTime.UtcNow.AddMinutes(10), AuthKind.PhoneOtp);
        await channel.SendOtpAsync(cmd.Phone, code, ct);
        return new OtpRequestResult(
            AttemptId: attemptId,
            DisplayCode: channel.DevHintCode ?? "",
            Hint: channel.DevHintCode is null
                ? $"أَرسَلنا الكود إلى {cmd.Phone}"
                : $"وَضع التَطوير ({channel.ChannelName}) — الكود: {code}");
    }

    // لا [WolverinePost] — راجِع التَّعليق على RequestPhoneOtpHandler.
    public static async Task<AuthResult?> VerifyPhoneOtpHandler(
        VerifyPhoneOtp cmd, ITenantContext tenantCtx, IDocumentStore store)
    {
        if (!tenantCtx.IsResolved) return null;
        // حِماية مِن brute-force OTP (٥/دَقيقَة لِكُلّ phone+tenant).
        var rlKey = $"{tenantCtx.Slug}|{cmd.Phone}";
        if (!TryConsume(_verifyRl, rlKey, MaxVerifyPerMinute, TimeSpan.FromMinutes(1)))
            return null;
        var attempt = Attempts.Values
            .FirstOrDefault(a => a.TenantSlug == tenantCtx.Slug
                              && a.Subject == cmd.Phone
                              && a.Kind == AuthKind.PhoneOtp);
        if (attempt is null || attempt.ExpiresAt < DateTime.UtcNow) return null;
        if (Hash(cmd.Code) != attempt.CodeHash) return null;
        Attempts.TryRemove(attempt.Id, out _);
        return await GetOrCreateUserAsync(store, tenantCtx.Slug, cmd.Phone, nationalId: null);
    }

    // ── Nafath ────────────────────────────────────────────────────────
    [WolverinePost("/{slug}/auth/nafath/request")]
    public static async Task<NafathPending> RequestNafathHandler(
        RequestNafath cmd, ITenantContext tenantCtx,
        INafathChannel channel, CancellationToken ct)
    {
        if (!tenantCtx.IsResolved) throw new InvalidOperationException("tenant_required");
        var result = await channel.StartAsync(cmd.NationalId, ct);
        Attempts[result.AttemptId] = new AuthAttempt(
            result.AttemptId, tenantCtx.Slug, cmd.NationalId, "",
            DateTime.UtcNow.AddMinutes(2), AuthKind.Nafath);
        return new NafathPending(result.AttemptId, result.DisplayCode, result.AutoApproveInSeconds);
    }

    [WolverinePost("/{slug}/auth/nafath/verify")]
    public static async Task<AuthResult?> VerifyNafathHandler(
        VerifyNafath cmd, ITenantContext tenantCtx,
        INafathChannel channel, IDocumentStore store, CancellationToken ct)
    {
        if (!tenantCtx.IsResolved) return null;
        if (!Attempts.TryGetValue(cmd.AttemptId, out var attempt)) return null;
        if (attempt.Kind != AuthKind.Nafath
         || attempt.TenantSlug != tenantCtx.Slug
         || attempt.Subject != cmd.NationalId) return null;
        if (attempt.ExpiresAt < DateTime.UtcNow) { Attempts.TryRemove(attempt.Id, out _); return null; }
        var approved = await channel.IsApprovedAsync(cmd.AttemptId, ct);
        if (!approved) return null;
        Attempts.TryRemove(attempt.Id, out _);
        return await GetOrCreateUserAsync(store, tenantCtx.Slug,
            phone: $"NID-{cmd.NationalId}", nationalId: cmd.NationalId);
    }

    private static async Task<AuthResult> GetOrCreateUserAsync(
        IDocumentStore store, string tenantSlug, string phone, string? nationalId)
    {
        await using var session = store.LightweightSession(tenantSlug);
        var existing = await session.Query<User>().FirstOrDefaultAsync(u =>
            (nationalId == null && u.Phone == phone) ||
            (nationalId != null && u.NationalId == nationalId));
        if (existing is null)
        {
            existing = new User
            {
                Id = Guid.NewGuid(),
                TenantSlug = tenantSlug,
                Phone = phone,
                NationalId = nationalId,
                PhoneVerified = nationalId is null,
                FullName = nationalId is null ? "عُضو جَديد" : "مُستَخدِم نَفاذ"
            };
            session.Store(existing);
            await session.SaveChangesAsync();
        }
        var token = MakeToken(existing.Id, tenantSlug);
        return new AuthResult(existing.Id, existing.FullName, existing.Phone, token, existing.Role);
    }

    /// <summary>
    /// Token = <c>base64({userId}|{tenantSlug}|{expiresUnix}|{hmacSig})</c>.
    /// التَوقيع HMAC-SHA256 بِـ <see cref="TokenSecret"/> فَوق
    /// <c>{userId}|{tenantSlug}|{expiresUnix}</c>. كانَ بِلا تَوقيع — قابِل
    /// لِلتَّزوير. الآن ParseToken يَفحَص التَّوقيع ويَرفُض أَيّ تَعديل.
    /// </summary>
    public static string MakeToken(Guid userId, string tenantSlug)
    {
        var exp = DateTimeOffset.UtcNow.AddDays(30).ToUnixTimeSeconds();
        var payload = $"{userId}|{tenantSlug}|{exp}";
        var sig = Sign(payload);
        return Convert.ToBase64String(Encoding.UTF8.GetBytes($"{payload}|{sig}"));
    }

    public static (Guid UserId, string TenantSlug, DateTime ExpiresAt)? ParseToken(string? token)
    {
        if (string.IsNullOrEmpty(token)) return null;
        try
        {
            var raw = Encoding.UTF8.GetString(Convert.FromBase64String(token));
            var parts = raw.Split('|');
            // ٤ حُقول إجباريّاً: userId, tenantSlug, exp, sig. أَيّ أَقَلّ
            // يُرفَض (تَوكينات قَديمَة بِلا تَوقيع لا تَمُرّ → تَسجيل دُخول مَرَّةً أُخرى).
            if (parts.Length != 4) return null;
            var payload = $"{parts[0]}|{parts[1]}|{parts[2]}";
            if (!CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(parts[3]),
                    Encoding.UTF8.GetBytes(Sign(payload))))
                return null;
            var exp = DateTimeOffset.FromUnixTimeSeconds(long.Parse(parts[2])).UtcDateTime;
            if (exp < DateTime.UtcNow) return null;
            return (Guid.Parse(parts[0]), parts[1], exp);
        }
        catch { return null; }
    }

    private static string Sign(string payload)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(TokenSecret));
        return Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload)));
    }

    private static string Hash(string s)
    {
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(s)));
    }
}
