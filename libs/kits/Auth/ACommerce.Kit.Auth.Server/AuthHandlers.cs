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

    // ── Phone OTP ─────────────────────────────────────────────────────
    [WolverinePost("/{slug}/auth/phone/request")]
    public static async Task<OtpRequestResult> RequestPhoneOtpHandler(
        RequestPhoneOtp cmd, ITenantContext tenantCtx,
        IOtpChannel channel, CancellationToken ct)
    {
        if (!tenantCtx.IsResolved) throw new InvalidOperationException("tenant_required");
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

    [WolverinePost("/{slug}/auth/phone/verify")]
    public static async Task<AuthResult?> VerifyPhoneOtpHandler(
        VerifyPhoneOtp cmd, ITenantContext tenantCtx, IDocumentStore store)
    {
        if (!tenantCtx.IsResolved) return null;
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
