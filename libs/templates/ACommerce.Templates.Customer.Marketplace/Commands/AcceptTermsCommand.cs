using ACommerce.Kit.Auth;
using ACommerce.Templates.Customer.Marketplace.Gates;
using Marten;

namespace ACommerce.Templates.Customer.Marketplace.Commands;

/// <summary>
/// مِثال command يَستَخدِم نَمَط الـ marker interfaces. الـ command يُعلِن
/// مُتَطَلَّباته (<see cref="IRequireAuth"/> + <see cref="IRequireTenant"/>)
/// — <see cref="GatePipeline"/> يَلتَقِطها ويُشَغِّل الفُحوصات قَبل
/// تَنفيذ الـ handler.
///
/// <para>لا تَوثيق هُنا، لا cookie parsing، لا قِراءَة HttpContext —
/// الـ command نَقيّ. الـ adapter (endpoint) يَجمَع الـ parameters مِن
/// HTTP وَيُمَرِّر لِلـ pipeline.</para>
/// </summary>
public sealed record AcceptTermsCommand(
    Guid UserId,
    string TenantSlug,
    int Version
) : IRequireAuth, IRequireTenant
{
    Guid? IRequireAuth.UserId => UserId;
}

public sealed class AcceptTermsHandler
{
    private readonly IDocumentStore _store;
    public AcceptTermsHandler(IDocumentStore store) => _store = store;

    public async Task HandleAsync(AcceptTermsCommand cmd, CancellationToken ct = default)
    {
        await using var s = _store.LightweightSession(cmd.TenantSlug);
        var user = await s.LoadAsync<User>(cmd.UserId, ct);
        if (user is null) throw new InvalidOperationException("user not found");

        user.AcceptedTermsAt = DateTime.UtcNow;
        user.AcceptedTermsVersion = cmd.Version;
        user.UpdatedAt = DateTime.UtcNow;
        s.Store(user);
        await s.SaveChangesAsync(ct);
    }
}
