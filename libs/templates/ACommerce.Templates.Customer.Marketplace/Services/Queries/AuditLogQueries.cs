using ACommerce.Templates.Customer.Marketplace.Services.Audit;
using Marten;

namespace ACommerce.Templates.Customer.Marketplace.Services.Queries;

/// <summary>
/// <para><b>قِراءَةُ سِجِلّ التَدقيق لِنِطاقٍ واحِد.</b> والنِطاقُ هُوَ
/// المُستَأجِر نَفسُه — سلاجُ مَتجَر، أَو <c>_platform</c> لِلأَفعال
/// الإدارِيَّة. فَالجَلسَةُ تُفتَح بِـ<c>QuerySession(scope)</c>
/// و<b>العَزلُ بُنيَويّ</b>: ‏<see cref="AuditEntry"/> يَقَع تَحتَ
/// سِياسَة <c>AllDocumentsAreMultiTenanted</c>، فَـMarten يَضَع
/// <c>tenant_id</c> في الاستِعلام ولا يوجَد مِن هُنا استِعلامٌ عابِرٌ
/// لِلمُستَأجِرين أَصلاً.</para>
///
/// <para><b>والسُقوطُ الآمِن يُنقَل كَما هُوَ لا يُصلَح</b>: الصَفحَةُ
/// كانَت تَلُفُّ الاستِعلامَ بِـ<c>try/catch</c> فَتُعطي قائِمَةً فارِغَة
/// إن لَم يَكُن الجَدوَلُ مُنشَأً في ذلكَ المُستَأجِر بَعد. والتَرحيلُ
/// <b>نَقلُ مَوضِعٍ لا تَعديلُ مَنطِق</b> — فَالسُلوكُ هُنا هُوَ سُلوكُ
/// الصَفحَة حَرفاً، وتَحويلُه إلى رَميٍ قَرارٌ مُنفَصِل لَه مَوجَتُه.</para>
/// </summary>
public sealed class AuditLogQueries
{
    /// <summary>سَقفُ الصَفحَة — نَفسُ الرَقَم الَّذي كانَ في
    /// <c>AuditLogPage.razor</c> حَرفاً.</summary>
    public const int DefaultTake = 300;

    private readonly IDocumentStore _store;

    public AuditLogQueries(IDocumentStore store) => _store = store;

    /// <summary>آخِرُ قُيود نِطاقٍ واحِد، الأَحدَثُ أَوَّلاً.
    /// <para><b>‏2026-10-29 (قَرارُ المالِك د52 «أ» — «ويُخفى الاسمُ عِندَ عَرضِ السِجِلّاتِ الَّتي لا تُعَدَّل»)</b>: سِجِلُّ التَدقيقِ تاريخٌ لا يُعادُ كَتبُه،
    /// فَاسمُ فاعِلٍ حَذَفَ حِسابَه يُستَبدَلُ عِندَ العَرضِ بِـ«حِسابٌ مَحذوف» (<see cref="HideErasedActors"/>) ويَبقى في المَخزَنِ كَما كُتِب.</para></summary>
    public async Task<IReadOnlyList<AuditEntry>> RecentAsync(
        string scope, int take = DefaultTake, CancellationToken ct = default)
    {
        try
        {
            await using var s = _store.QuerySession(scope);
            var rows = (await s.Query<AuditEntry>()
                .OrderByDescending(a => a.At).Take(take).ToListAsync(ct)).ToList();
            var actors = rows.Where(a => a.ActorId is not null).Select(a => a.ActorId!.Value).Distinct().ToList();
            var erased = actors.Count == 0
                ? new HashSet<Guid>()
                : (await s.Query<ACommerce.Kit.Auth.User>().Where(u => actors.Contains(u.Id) && u.DeletedAt != null)
                    .Select(u => u.Id).ToListAsync(ct)).ToHashSet();
            return HideErasedActors(rows, erased);
        }
        catch
        {
            return Array.Empty<AuditEntry>();
        }
    }

    /// <summary>نَقِيَّة: القُيودُ كَما هي، واسمُ فاعِلٍ في <paramref name="erasedActors"/> «حِسابٌ مَحذوف» — عَلى النُسخَةِ المَعروضَة، لا في المَخزَن
    /// (جَلسَةُ قِراءَةٍ لا تَحفَظ).</summary>
    public static IReadOnlyList<AuditEntry> HideErasedActors(IReadOnlyList<AuditEntry> rows, IReadOnlySet<Guid> erasedActors)
    {
        foreach (var a in rows)
            if (a.ActorId is { } id && erasedActors.Contains(id))
                a.ActorName = ACommerce.Kit.Auth.AccountDeletion.ErasedName;
        return rows;
    }

    /// <summary>
    /// <para><b>آخِرُ قَيدٍ بِفِعلٍ بِعَينِه في نِطاق</b> — تَسأَلُه
    /// شاشَةُ التَخارُج: «مَتى كانَ آخِرُ تَصدير، وبِمَن؟».</para>
    ///
    /// <para><b>ولِماذا استِعلامٌ مُوَجَّهٌ لا فَلتَرَةُ صَفحَةٍ فَوقَ
    /// <see cref="RecentAsync"/></b>: آخِرُ تَصديرٍ قَد يَسبِقُ ثَلاثَمِئَةِ
    /// قَيدٍ مِن نَشاطٍ يَوميّ، فَتَقولُ الشاشَةُ «لَم تُصَدِّر بَعد»
    /// لِمَن صَدَّرَ أَمس — <b>سَطرٌ يَكذِب</b>. ونَفسُ السُقوطِ الآمِنِ
    /// حَرفاً: جَدوَلٌ غَيرُ مُنشَأٍ يُعطي <c>null</c> لا رَمياً.</para>
    /// </summary>
    public async Task<AuditEntry?> LastAsync(
        string scope, string action, CancellationToken ct = default)
    {
        try
        {
            await using var s = _store.QuerySession(scope);
            return await s.Query<AuditEntry>()
                .Where(a => a.Action == action)
                .OrderByDescending(a => a.At)
                .FirstOrDefaultAsync(ct);
        }
        catch
        {
            return null;
        }
    }
}
