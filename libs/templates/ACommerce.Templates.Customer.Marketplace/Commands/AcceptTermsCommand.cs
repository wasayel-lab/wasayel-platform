using ACommerce.Kit.Auth;
using ACommerce.Kit.Auth.Server;
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

    /// <summary>يَكتُبُ قَبولَ الشُروطِ عَلى حِسابِ الجَلسَة. <c>false</c> = لا حِساب، أَو رَدَّ <paramref name="admit"/> الوَثيقَةَ الجَديدَة — بِلا كِتابَة.</summary>
    /// <remarks>
    /// <para>‏(‏2026-10-30، الموجَة ١٢ F1 البَند ٦ — ثابِتُ StillOpen؛ تَوصيفُه <c>c5d124c</c>) <b>بِالكِتابَةِ الواحِدَة <c>AccountWrites.UpdateAsync</c> وشَرطِ
    /// الجَلسَةِ في تَوقيعِها</b>: كانَ يُحَمِّلُ الحِسابَ ويَحفَظُه بِيَدِه — فَجَلسَةٌ عَبَرَت البَوّابَةَ ثُمَّ أَسقَطَها «اخرج من كل الأجهزة» تَكتُبُ قَبولاً (مَقيس:
    /// «كُتِبَ القَبولُ 1 مَرَّة · الجَلسَةُ مَفتوحَة False»)، وكاتِبٌ مُتَزامِنٌ يُرَدُّ بِتَعارُضٍ بِلا إعادَة. وقَبولُ الشُروطِ سِجِلٌّ قانونيٌّ يَبقى بَعدَ الجَلسَة،
    /// فَلا يَصدُرُ إلّا مِن جَلسَةٍ مَفتوحَةٍ عَلى القِراءَةِ الجَديدَة: البابُ يُعطيه <c>SessionVersionGate.StillOpen(u, token)</c>. وحِسابٌ غائِبٌ كانَ يَرمي
    /// (<c>user not found</c> ← صَفحَةُ ‏500)، والآن <c>false</c> كَالجَلسَةِ المُغلَقَة.</para>
    /// </remarks>
    public async Task<bool> HandleAsync(AcceptTermsCommand cmd, Func<User, bool> admit, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var user = await AccountWrites.UpdateAsync<User>(_store, cmd.TenantSlug, cmd.UserId, u =>
        {
            u.AcceptedTermsAt = now;
            u.AcceptedTermsVersion = cmd.Version;
            u.UpdatedAt = now;
            return true;
        }, ct, admit);
        return user is not null;
    }
}
