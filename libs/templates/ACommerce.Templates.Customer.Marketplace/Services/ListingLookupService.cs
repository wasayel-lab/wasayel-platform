using ACommerce.Kit.Listings;
using ACommerce.Templates.Customer.Marketplace.Services.Listings;
using Marten;

namespace ACommerce.Templates.Customer.Marketplace.Services;

/// <summary>
/// <para><b>قِراءَةُ إعلانٍ واحِد لِمَن يَعرِض ولِمَن يَحرُس</b> —
/// وهذا هُوَ الطَرَفُ القارِئ مِن شاشَةِ التَحرير.</para>
///
/// <para><b>ولِماذا هُنا لا في <c>Services/Listings/</c></b>: ذاكَ
/// المُجَلَّد مَفروضٌ عَلَيه أَلّا يَفتَح جَلسَةً
/// (<c>TenantConfigServiceShapeTests</c>) — لِأَنّ ساكِنَتَه تَقَع
/// داخِلَ مُعامَلَةِ نُقطَة. وهذِه قِراءَةٌ لِصَفحَة: لا مُعامَلَةَ
/// لَها تَنضَمُّ إلَيها، فَتَفتَح جَلسَتَها بِنَفسِها كَما تَفعَل
/// <see cref="TenantRoleService"/> و<see cref="DynamicAttributesService"/>.
/// شَرطانِ مُختَلِفانِ لِأَنّ الحاجَتَينِ مُختَلِفَتان — لا
/// استِثناء.</para>
///
/// <para><b>ووُجودُها هُوَ ما يُبقي الطَبَقَةَ الثامِنَة خَضراء</b>:
/// شاشَةُ التَحرير <b>لا تَفتَح جَلسَةَ Marten بِيَدِها</b>، فَلا
/// تَدخُل سِجِلَّ ‏55 صَفحَة ولا تَرفَع سَقفَه. الصَفحَةُ تَسأَل
/// خِدمَةً ولا تَعرِف مِن أَينَ تُجيب.</para>
///
/// <para><b>والعَزل بُنيَويّ لا اتِّفاقيّ</b>: كُلّ قِراءَة تُفتَح بِـ
/// <c>QuerySession(tenantSlug)</c>، والوَثائِقُ مُتَعَدِّدَةُ الإيجار
/// بِسِياسَة <c>AllDocumentsAreMultiTenanted</c> — فَـMarten يَضَع
/// <c>tenant_id</c> في الاستِعلام، ولا استِعلامَ عابِرَ
/// لِلمُستَأجِرين مُمكِنٌ مِن هُنا أَصلاً.</para>
/// </summary>
public sealed class ListingLookupService
{
    private readonly IDocumentStore _store;

    public ListingLookupService(IDocumentStore store) => _store = store;

    /// <summary>الإعلانُ كَما هُوَ، أَو <c>null</c> إن لَم يوجَد أَو
    /// كانَ مَحذوفاً. والحَذفُ لَيِّن، فَالتَصفِيَةُ هُنا لا في
    /// القاعِدَة.</summary>
    public async Task<Listing?> LoadAsync(
        string tenantSlug, Guid listingId, CancellationToken ct = default)
    {
        await using var s = _store.QuerySession(tenantSlug);
        var listing = await s.LoadAsync<Listing>(listingId, ct);
        return listing is null || listing.IsDeleted ? null : listing;
    }

    /// <summary>الإعلانُ إن كانَ لِهذا المالِك، وإلّا <c>null</c> —
    /// و<b>بِنَفس دالَّة القَرار</b> الَّتي تَحكُم بِها الخِدمَةُ
    /// الكاتِبَة (<see cref="ListingEditService.IsOwnedBy"/>). فَما
    /// تَفتَحُه الشاشَةُ هُوَ بِالضَبط ما تَقبَلُه النُقطَة.</summary>
    public async Task<Listing?> LoadOwnedAsync(
        string tenantSlug, Guid listingId, Guid ownerId, CancellationToken ct = default)
    {
        var listing = await LoadAsync(tenantSlug, listingId, ct);
        return listing is not null && ListingEditService.IsOwnedBy(listing, ownerId) ? listing : null;
    }
}
