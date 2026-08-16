using ACommerce.Kit.Auth;
using ACommerce.Kit.SavedSearches;
using ACommerce.Templates.Customer.Marketplace.Gates;
using Marten;

namespace ACommerce.Templates.Customer.Marketplace.Services.Queries;

/// <summary>
/// <para><b>ما تَسأَلُه شاشاتُ الحِساب عَن صاحِبِها</b> — قَبولُ
/// الشُروط، والدَورُ النَشِط، والبَحوثُ المَحفوظَة. ثَلاثُ قِراءاتٍ
/// كانَت تَفتَح جَلسَةَ Marten مِن داخِل <c>GatedPage.razor</c> و
/// <c>MySearches.razor</c>.</para>
///
/// <para><b>والحارِسُ أَخطَرُ ما يُرَحَّل، فَلا يُدمَج</b>: كانَت
/// <c>GatedPage</c> تَجلِب <see cref="User"/> <b>مَرَّتَين</b> في
/// فَرعَين مَشروطَين — واحِدَةً لِلشُروط وأُخرى لِلصَلاحِيَّة. ودَمجُهُما
/// في نِداءٍ واحِدٍ يُحَسِّن عَدَدَ الاستِعلامات ويُغَيِّر <b>مَتى</b>
/// تُقرَأ الحالَة، وذاكَ تَعديلُ مَنطِقٍ في حارِس. فَبَقِيَتا
/// دالَّتَين، كُلٌّ بِجَلسَتِها، حَرفاً كَما كانَتا.</para>
///
/// <para><b>والعَزلُ بُنيَويّ</b>: كُلّ نِداءٍ هُنا يُفتَح بِـ
/// <c>QuerySession(tenantSlug)</c> — و<see cref="User"/> و
/// <see cref="SavedSearch"/> تَحتَ سِياسَة
/// <c>AllDocumentsAreMultiTenanted</c>. فَنَفسُ الهُوِيَّة في مَتجَرَين
/// وَثيقَتانِ مُستَقِلَّتان، ولا استِعلامَ عابِرَ لِلمُستَأجِرين مُمكِنٌ
/// مِن هُنا.</para>
/// </summary>
public sealed class AccountQueries
{
    private readonly IDocumentStore _store;

    public AccountQueries(IDocumentStore store) => _store = store;

    /// <summary>هَل قَبِلَ صاحِبُ الجَلسَةِ الإصدارَ الحاليّ مِن
    /// الشُروط في هذا المَتجَر؟ <b>والقَرارُ ليسَ هُنا</b> — هُوَ في
    /// <see cref="TermsPolicy.IsAccepted"/> الَّتي يَقرَؤُها الحارِسانِ
    /// أَيضاً، فَما تَفتَحُه الشاشَةُ هُوَ بِالضَبط ما تَقبَلُه
    /// النُقطَة.</summary>
    public async Task<bool> HasAcceptedCurrentTermsAsync(
        string tenantSlug, Guid userId, CancellationToken ct = default)
    {
        await using var s = _store.QuerySession(tenantSlug);
        return TermsPolicy.IsAccepted(await s.LoadAsync<User>(userId, ct));
    }

    /// <summary>الدَورُ المُخَزَّن لِلمُستَخدِم في هذا المَتجَر، أَو
    /// <c>null</c> إن لَم يوجَد. و<c>""</c> قيمَةٌ مَشروعَة تَعني «بِلا
    /// دَور» — فَالتَمييزُ بَينَها وبَينَ «لا مُستَخدِم» مَقصود.</summary>
    public async Task<string?> ActiveRoleAsync(
        string tenantSlug, Guid userId, CancellationToken ct = default)
    {
        await using var s = _store.QuerySession(tenantSlug);
        return (await s.LoadAsync<User>(userId, ct))?.ActiveRole;
    }

    /// <summary>بَحوثُ المُستَخدِم المَحفوظَة، الأَحدَثُ أَوَّلاً.
    /// <b>وتُرَتَّب في الذاكِرَة كَما كانَت</b> في الصَفحَة حَرفاً —
    /// نَقلُ مَوضِعٍ لا تَعديلُ مَنطِق.</summary>
    public async Task<IReadOnlyList<SavedSearch>> SavedSearchesAsync(
        string tenantSlug, Guid userId, CancellationToken ct = default)
    {
        await using var s = _store.QuerySession(tenantSlug);
        return (await s.Query<SavedSearch>()
                .Where(ss => ss.UserId == userId)
                .ToListAsync(ct))
            .OrderByDescending(ss => ss.CreatedAt).ToList();
    }
}
