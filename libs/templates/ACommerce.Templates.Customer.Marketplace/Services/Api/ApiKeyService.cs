using System.Security.Cryptography;
using System.Text;
using Marten;

namespace ACommerce.Templates.Customer.Marketplace.Services.Api;

/// <summary>ناتِجُ الإصدار: الوَثيقَةُ المَحفوظَة، والسِرُّ
/// <b>المَعروضُ مَرَّةً واحِدَة</b>. ولا مَوضِعَ آخَر في المُستَودَع
/// يَحمِل <see cref="Presented"/> بَعدَ هذا النِداء.</summary>
public sealed record ApiKeyIssued(ApiKeyDocument Key, string Presented);

/// <summary>سَبَبُ رَفضِ الاعتِماد — <b>لِلوغ ولِلاختِبار لا
/// لِلعَميل</b>. المُرَشِّحُ يَرُدّ رَمزاً واحِداً لِكُلّ هذِه
/// (‏<c>auth_invalid</c>) كَي لا يُفشِيَ حالَةَ المِفتاح لِمَن لا
/// يَملِكُه.</summary>
public enum ApiKeyRejection
{
    None = 0,
    Missing,
    Malformed,
    Unknown,
    SecretMismatch,
    Revoked,
    Expired,
    TenantGone,
}

/// <summary>هُوِيَّةُ الطَلَب بَعدَ اعتِمادٍ ناجِح.</summary>
public sealed record ApiKeyPrincipal(
    string KeyId, string TenantSlug, Guid ActorUserId, string ActorName,
    IReadOnlyList<string> Scopes)
{
    public bool HasScope(string scope) => Scopes.Contains(scope, StringComparer.Ordinal);
}

public sealed record ApiKeyAuthResult(ApiKeyPrincipal? Principal, ApiKeyRejection Rejection)
{
    public bool Ok => Principal is not null;
}

/// <summary>
/// <para><b>خِدمَةُ مَفاتيح API</b> — الإصدارُ والاعتِمادُ والإبطالُ
/// والسَرد. المُستَهلِكونَ ثَلاثَة: <c>ApiKeyFilter</c> (اعتِماد)،
/// ونُقطَتا الاستوديو (إصدار/إبطال)، وشاشَةُ المَفاتيح (سَرد).</para>
///
/// <para><b>والمُقارَنَةُ ثابِتَةُ الزَمَن</b>: تَجزئَةُ المَعروضِ
/// تُقارَن بِـ<c>CryptographicOperations.FixedTimeEquals</c> — نَفس
/// ما يَفعَلُه <c>AuthHandlers.ParseToken</c> بِالتَوقيع
/// (‏<c>AuthHandlers.cs:272</c>)، ولِنَفس السَبَب.</para>
/// </summary>
/// <remarks>
/// <para><b>غَيرُ <c>sealed</c>، و<see cref="AuthenticateAsync"/>
/// <c>virtual</c> — بِسَبَبٍ واحِدٍ مُعلَن</b>: قَرارُ
/// <c>ApiKeyFilter</c> (‏401 · 403 نِطاق · 403 استِحقاق · مُرور)
/// لا يُقاس إلّا بِنِداءٍ حَقيقيّ، وقاعِدَةُ البَيانات لَيسَت
/// شَرطاً لِقياسِه. فَبِمَعبَرٍ واحِدٍ صَغير تَصير أَربَعُ حالاتِ
/// الحارِسِ <b>مُختَبَرَةً بِمُوجِبٍ وسالِب</b> بَدَلَ أَن تَبقى
/// دَعوىً في تَعليق — والقاعِدَة ٢: الحَدُّ الَّذي لا يُقاس آلِيّاً
/// يَنهار.</para>
/// </remarks>
public class ApiKeyService
{
    private readonly IDocumentStore _store;

    public ApiKeyService(IDocumentStore store) => _store = store;

    // ─── الإصدار ──────────────────────────────────────────────────────

    /// <summary>
    /// <para>يُنشِئ مِفتاحاً ويَحفَظُ <b>تَجزِئَتَه</b>، ويُعيدُ
    /// المَعروضَ مَرَّةً واحِدَة. الوَثيقَةُ عامَّة، فَالجَلسَةُ بِلا
    /// سلاج — وهذا هُوَ السَبَبُ نَفسُه الَّذي يَفتَح بِه مَسارُ
    /// الإدارَة جَلسَةً بِلا سلاج لِوَثيقَة <c>Tenant</c>.</para>
    /// </summary>
    public async Task<ApiKeyIssued> IssueAsync(
        string tenantSlug, ApiKeyValidator.IssueRequest request,
        Guid issuedByStudioUserId, CancellationToken ct = default)
    {
        var violations = ApiKeyValidator.Validate(request);
        if (violations.Count > 0)
            throw new ArgumentException(
                string.Join(" | ", violations.Select(x => $"{x.Code}: {x.MessageAr}")),
                nameof(request));

        var keyId  = RandomHex(ApiKeyFormat.KeyIdBytes);
        var secret = RandomHex(ApiKeyFormat.SecretBytes);

        var doc = new ApiKeyDocument
        {
            Id           = keyId,
            TenantSlug   = tenantSlug,
            Name         = request.Name.Trim(),
            SecretHash   = Sha256Hex(secret),
            Scopes       = request.Scopes.Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList(),
            ActorUserId  = request.ActorUserId,
            ActorName    = string.IsNullOrWhiteSpace(request.ActorName) ? request.Name.Trim() : request.ActorName.Trim(),
            Status       = ApiKeyDocument.StatusActive,
            CreatedAt    = DateTime.UtcNow,
            ExpiresAt    = request.ExpiresInDays is { } d ? DateTime.UtcNow.AddDays(d) : null,
            IssuedByStudioUserId = issuedByStudioUserId,
        };

        await using var s = _store.LightweightSession();
        s.Store(doc);
        await s.SaveChangesAsync(ct);

        return new ApiKeyIssued(doc, $"{ApiKeyFormat.Prefix}_{keyId}_{secret}");
    }

    // ─── الاعتِماد ────────────────────────────────────────────────────

    /// <summary>
    /// <para>يَقرَأُ رَأسَ <c>Authorization: Bearer wsl_…</c>
    /// ويُعيدُ الهُوِيَّة أَو سَبَبَ الرَفض. <b>ولا يَقرَأُ المُستَأجِرَ
    /// مِن الطَلَب</b>: يُعيدُه مِن الوَثيقَة.</para>
    /// </summary>
    public virtual async Task<ApiKeyAuthResult> AuthenticateAsync(
        string? presented, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(presented))
            return new(null, ApiKeyRejection.Missing);

        var parsed = ApiKeyValidator.ParsePresented(presented);
        if (parsed is null) return new(null, ApiKeyRejection.Malformed);

        var (keyId, secret) = parsed.Value;

        await using var s = _store.QuerySession();
        var doc = await s.LoadAsync<ApiKeyDocument>(keyId, ct);
        if (doc is null) return new(null, ApiKeyRejection.Unknown);

        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(doc.SecretHash),
                Encoding.UTF8.GetBytes(Sha256Hex(secret))))
            return new(null, ApiKeyRejection.SecretMismatch);

        if (!string.Equals(doc.Status, ApiKeyDocument.StatusActive, StringComparison.Ordinal))
            return new(null, ApiKeyRejection.Revoked);

        if (doc.ExpiresAt is { } exp && exp <= DateTime.UtcNow)
            return new(null, ApiKeyRejection.Expired);

        if (string.IsNullOrWhiteSpace(doc.TenantSlug))
            return new(null, ApiKeyRejection.TenantGone);

        return new(new ApiKeyPrincipal(
            doc.Id, doc.TenantSlug, doc.ActorUserId, doc.ActorName, doc.Scopes.ToArray()),
            ApiKeyRejection.None);
    }

    /// <summary><b>الرَأسُ إلى المَعروض</b> — دالَّةٌ نَقِيَّة،
    /// فَتُختَبَر بِمُوجِبٍ وسالِبٍ بِلا خادِم. تَقبَل
    /// <c>Bearer x</c> بِأَيّ حالَةِ أَحرُفٍ في الكَلِمَة، وتَرُدّ
    /// <c>null</c> لِما سِواها.</summary>
    public static string? BearerFrom(string? authorizationHeader)
    {
        if (string.IsNullOrWhiteSpace(authorizationHeader)) return null;
        var h = authorizationHeader.Trim();
        const string scheme = "Bearer ";
        if (!h.StartsWith(scheme, StringComparison.OrdinalIgnoreCase)) return null;
        var value = h[scheme.Length..].Trim();
        return string.IsNullOrEmpty(value) ? null : value;
    }

    // ─── السَردُ والإبطال ─────────────────────────────────────────────

    /// <summary>مَفاتيحُ مُستَأجِرٍ واحِد — لِلشاشَة. <b>ولا سِرَّ
    /// فيها</b>: <see cref="ApiKeyDocument.SecretHash"/> يُقرَأ ولا
    /// يُعرَض، والشاشَةُ لا تَذكُرُه.</summary>
    public async Task<IReadOnlyList<ApiKeyDocument>> ListAsync(
        string tenantSlug, CancellationToken ct = default)
    {
        await using var s = _store.QuerySession();
        var all = await s.Query<ApiKeyDocument>()
            .Where(k => k.TenantSlug == tenantSlug)
            .ToListAsync(ct);
        return all.OrderByDescending(k => k.CreatedAt).ToList();
    }

    /// <summary>
    /// <para><b>الإبطالُ تَعطيلُ صَفّ</b> — لا تَدويرُ سِرٍّ عامّ.
    /// ويُشتَرَط تَطابُقُ المُستَأجِر: مالِكُ مَتجَرٍ لا يُبطِل مِفتاحَ
    /// مَتجَرٍ آخَر ولَو عَرَفَ مُعَرِّفَه.</para>
    /// </summary>
    public async Task<bool> RevokeAsync(
        string tenantSlug, string keyId, CancellationToken ct = default)
    {
        await using var s = _store.LightweightSession();
        var doc = await s.LoadAsync<ApiKeyDocument>(keyId, ct);
        if (doc is null || !string.Equals(doc.TenantSlug, tenantSlug, StringComparison.Ordinal))
            return false;
        if (string.Equals(doc.Status, ApiKeyDocument.StatusRevoked, StringComparison.Ordinal))
            return true;

        doc.Status    = ApiKeyDocument.StatusRevoked;
        doc.RevokedAt = DateTime.UtcNow;
        s.Store(doc);
        await s.SaveChangesAsync(ct);
        return true;
    }

    // ─── الأَدَوات ────────────────────────────────────────────────────

    /// <summary>‏<c>SHA-256</c> بِست عَشرِيٍّ صَغير. نَفسُ نَمَط
    /// <c>AuthHandlers.Hash</c> — وهُوَ <c>private</c> هُناك، فَلا
    /// يُبلَغ مِن هُنا؛ والنَسخُ سَطرانِ لا تَجريد.</summary>
    public static string Sha256Hex(string s) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s))).ToLowerInvariant();

    private static string RandomHex(int bytes) =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(bytes)).ToLowerInvariant();
}
