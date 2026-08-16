using ACommerce.Kit.Tenants;
using Marten;

namespace ACommerce.Templates.Customer.Marketplace.Services.Queries;

/// <summary>
/// <para><b>سِجِلُّ المُستَأجِرين — القِراءَةُ الأَكثَرُ تَكراراً في
/// المُستَودَع كُلِّه.</b> ‏<c>LoadAsync&lt;Tenant&gt;(slug)</c> مَكتوبَةٌ
/// بِيَدِها في <b>‏20 صَفحَة</b>، و<c>Query&lt;Tenant&gt;()</c> في خَمسٍ
/// أُخرى — كُلُّها تَفتَح جَلسَةَ Marten مِن داخِل <c>.razor</c>. وهذا
/// أَوَّلُ ما يُسَدَّد مِن سِجِلّ الطَبَقَة الثامِنَة لِأَنَّه أَرخَص
/// نُقلَةٍ بِأَعلى تَكرار.</para>
///
/// <para><b>ولِماذا <c>QuerySession()</c> بِلا مُستَأجِر هُنا وَحدَها</b>:
/// وَثيقَةُ <see cref="Tenant"/> مُسَجَّلَة <c>SingleTenanted()</c> صَراحَةً
/// في <c>HostingExtensions</c> — هي <b>سِجِلُّ المُستَأجِرين أَنفُسِهم</b>،
/// فَلا مُستَأجِرَ تَقَع فيه. وجَلسَةٌ بِسلاجٍ عَلَيها تَبحَث في مُستَأجِرٍ
/// لا وُجودَ لَه فَتُعطي <c>null</c> دائِماً. ولِذلك هذا المِلَفّ هُوَ
/// <b>الاستِثناءُ المُعلَن الوَحيد</b> في مُجَلَّد الاستِعلامات، ومُثَبَّتٌ
/// بِاسمِه في <c>TenantConfigServiceShapeTests</c> — <b>بِالاتِّجاهَين</b>:
/// مِلَفٌّ آخَر يَفتَح جَلسَةً بِلا سلاج يَحمَرّ، وهذا المِلَفُّ إن لَم
/// يَعُد يَفتَحُها يَحمَرّ أَيضاً فَيُرفَع الاستِثناء مَع سَبَبِه.</para>
///
/// <para><b>والشَكل هُوَ شَكل <see cref="ListingLookupService"/></b>
/// (‏<c>docs/ARCHITECTURE-ENFORCEMENT.md</c> §٦.٢): الخِدمَةُ تَفتَح
/// جَلسَتَها لِأَنّ الصَفحَةَ <b>لا مُعامَلَةَ لَها</b> تَنضَمُّ إلَيها —
/// بِخِلاف <c>Services/TenantConfig</c> و<c>Services/Listings</c> حَيثُ
/// المُعامَلَةُ لِلنُقطَة. شَرطانِ مُختَلِفانِ لِأَنّ الحاجَتَينِ
/// مُختَلِفَتان، وكِلاهُما مَفروضٌ بِنَفس الفاحِص لا بِفاحِصٍ ثانٍ
/// (القاعِدَة ٨).</para>
///
/// <para><b>وما تَكسِبُه الصَفحَة</b>: لا تَعرِف <c>IDocumentStore</c>،
/// فَلا تَسقُط في تَطبيق MAUI Blazor Hybrid حَيثُ لا قاعِدَةَ بَيانات
/// على الهاتِف. تُبَدَّل هذِه الخِدمَةُ بِنَظيرَةٍ تُنادي HTTP ولا
/// يَتَغَيَّر حَرفٌ في الصَفحَة.</para>
/// </summary>
public sealed class TenantDirectory
{
    private readonly IDocumentStore _store;

    public TenantDirectory(IDocumentStore store) => _store = store;

    /// <summary>مُستَأجِرٌ بِسلاجِه، أَو <c>null</c>. السلاجُ هُوَ
    /// مُعَرِّفُ الوَثيقَة (<c>Identity(x =&gt; x.Id)</c>).</summary>
    public async Task<Tenant?> FindAsync(string slug, CancellationToken ct = default)
    {
        await using var s = _store.QuerySession();
        return await s.LoadAsync<Tenant>(slug, ct);
    }

    /// <summary>كُلُّ المُستَأجِرين بِتَرتيب الإنشاء — تَرتيبُ لَوحَة
    /// الإدارَة وسِجِلّ التَدقيق وصَفحَة الهُبوط حَرفاً.</summary>
    public async Task<IReadOnlyList<Tenant>> ListAsync(CancellationToken ct = default)
    {
        await using var s = _store.QuerySession();
        return (await s.Query<Tenant>().OrderBy(t => t.CreatedAt).ToListAsync(ct)).ToList();
    }

    /// <summary>تَطبيقاتُ صاحِبِ استوديو واحِد، بِتَرتيب الإنشاء.</summary>
    public async Task<IReadOnlyList<Tenant>> ListOwnedByAsync(
        Guid ownerUserId, CancellationToken ct = default)
    {
        await using var s = _store.QuerySession();
        return (await s.Query<Tenant>()
            .Where(t => t.OwnerUserId == ownerUserId)
            .OrderBy(t => t.CreatedAt).ToListAsync(ct)).ToList();
    }
}
