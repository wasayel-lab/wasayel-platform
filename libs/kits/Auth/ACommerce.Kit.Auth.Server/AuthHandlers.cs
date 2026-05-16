using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using ACommerce.Platform.Shared;
using Marten;
using Wolverine.Http;

namespace ACommerce.Kit.Auth.Server;

/// <summary>
/// مُصادَقَة بالهاتف-OTP (mock SMS — كود "123456") + Nafath (mock — كود "00").
/// لا تَحتاج JWT حَقيقيّ في هذا الـ MVP — نَستَخدِم نَفس userId + tenant
/// مَخفِيَّيْن في cookie session.
/// </summary>
public static class AuthHandlers
{
    /// <summary>اِفتراضات تَطوير: OTP الصَحيح دائماً "123456"، Nafath "00".</summary>
    public const string DevOtpCode = "123456";
    public const string DevNafathCode = "00";

    /// <summary>صَفّ مُحاوَلات في الذاكِرَة — يُبَسِّط الـ MVP. الإنتاج يَستَخدِم events.</summary>
    public static readonly ConcurrentDictionary<string, AuthAttempt> Attempts = new();

    public sealed record AuthAttempt(
        string Id, string TenantSlug, string Subject, string CodeHash, DateTime ExpiresAt, bool IsNafath);

    [WolverinePost("/{slug}/auth/phone/request")]
    public static OtpRequestResult RequestOtp(
        RequestPhoneOtp cmd, ITenantContext tenantCtx)
    {
        if (!tenantCtx.IsResolved) throw new InvalidOperationException("tenant_required");
        var attemptId = Guid.NewGuid().ToString("N");
        var codeHash = Hash(DevOtpCode);
        Attempts[attemptId] = new AuthAttempt(
            attemptId, tenantCtx.Slug, cmd.Phone, codeHash,
            DateTime.UtcNow.AddMinutes(10), IsNafath: false);

        // Mock SMS — يَطبَع للـ console بَدَل إرسال SMS فعليّ.
        Console.WriteLine($"[Mock SMS] أَرسَلنا الكود {DevOtpCode} إلى {cmd.Phone}");

        return new OtpRequestResult(
            AttemptId: attemptId,
            DisplayCode: DevOtpCode,                       // dev only
            Hint: $"الكود وَصَل لِـ {cmd.Phone}. في وَضع التَطوير: {DevOtpCode}");
    }

    [WolverinePost("/{slug}/auth/phone/verify")]
    public static async Task<AuthResult?> VerifyOtp(
        VerifyPhoneOtp cmd, ITenantContext tenantCtx, IDocumentStore store)
    {
        if (!tenantCtx.IsResolved) return null;
        var attempt = Attempts.Values
            .FirstOrDefault(a => a.TenantSlug == tenantCtx.Slug && a.Subject == cmd.Phone && !a.IsNafath);
        if (attempt is null) return null;
        if (attempt.ExpiresAt < DateTime.UtcNow) { Attempts.TryRemove(attempt.Id, out _); return null; }
        if (Hash(cmd.Code) != attempt.CodeHash) return null;

        Attempts.TryRemove(attempt.Id, out _);
        return await GetOrCreateUserAsync(store, tenantCtx.Slug, cmd.Phone, nationalId: null);
    }

    [WolverinePost("/{slug}/auth/nafath/request")]
    public static NafathPending RequestNafathHandler(
        RequestNafath cmd, ITenantContext tenantCtx)
    {
        if (!tenantCtx.IsResolved) throw new InvalidOperationException("tenant_required");
        var attemptId = Guid.NewGuid().ToString("N");
        Attempts[attemptId] = new AuthAttempt(
            attemptId, tenantCtx.Slug, cmd.NationalId, Hash(DevNafathCode),
            DateTime.UtcNow.AddMinutes(2), IsNafath: true);

        Console.WriteLine($"[Mock Nafath] طَلَب لِـ {cmd.NationalId}، الكود: {DevNafathCode}");

        return new NafathPending(attemptId, DevNafathCode, AutoVerifyInSeconds: 5);
    }

    [WolverinePost("/{slug}/auth/nafath/verify")]
    public static async Task<AuthResult?> VerifyNafathHandler(
        VerifyNafath cmd, ITenantContext tenantCtx, IDocumentStore store)
    {
        if (!tenantCtx.IsResolved) return null;
        if (!Attempts.TryGetValue(cmd.AttemptId, out var attempt)) return null;
        if (!attempt.IsNafath) return null;
        if (attempt.TenantSlug != tenantCtx.Slug) return null;
        if (attempt.Subject != cmd.NationalId) return null;
        if (attempt.ExpiresAt < DateTime.UtcNow) { Attempts.TryRemove(attempt.Id, out _); return null; }

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

        // token = base64(userId|tenant|exp) — mock، يَكفي للـ session في cookie
        var token = MakeToken(existing.Id, tenantSlug);
        return new AuthResult(existing.Id, existing.FullName, existing.Phone, token, existing.Role);
    }

    public static string MakeToken(Guid userId, string tenantSlug)
    {
        var raw = $"{userId}|{tenantSlug}|{DateTimeOffset.UtcNow.AddDays(30).ToUnixTimeSeconds()}";
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(raw));
    }

    public static (Guid UserId, string TenantSlug, DateTime ExpiresAt)? ParseToken(string? token)
    {
        if (string.IsNullOrEmpty(token)) return null;
        try
        {
            var raw = Encoding.UTF8.GetString(Convert.FromBase64String(token));
            var parts = raw.Split('|');
            if (parts.Length != 3) return null;
            var exp = DateTimeOffset.FromUnixTimeSeconds(long.Parse(parts[2])).UtcDateTime;
            if (exp < DateTime.UtcNow) return null;
            return (Guid.Parse(parts[0]), parts[1], exp);
        }
        catch { return null; }
    }

    private static string Hash(string s)
    {
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(s)));
    }
}
