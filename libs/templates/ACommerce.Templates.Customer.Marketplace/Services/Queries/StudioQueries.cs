using ACommerce.Templates.Customer.Marketplace.Services.Incubator;
using Marten;

namespace ACommerce.Templates.Customer.Marketplace.Services.Queries;

/// <summary>
/// <para><b>لَقطَةُ لَوحَةِ مُراقَبَةِ الجَودَة</b> — كُلُّ عَدّادٍ
/// تَعرِضُه الشاشَةُ مَحسوبٌ هُنا، لا في الصَفحَة. و<c>RecentFailures</c>
/// و<c>RecentCompleted</c> تَبقَيانِ جَلَساتٍ كامِلَة لِأَنّ الصَفحَةَ
/// تَعرِض مِنها عُنواناً وتاريخاً ودَرَجَة — واختِراعُ سِجِلّ عَرضٍ
/// ثالِثٍ لَها يُبَدِّل البايتات ولا يُبَدِّل شَيئاً في المِعمارِيَّة.</para>
/// </summary>
public sealed record MonitorSnapshot(
    int Total, int Completed, int Analyzing, int Failed, int AvgQuality,
    IReadOnlyDictionary<string, int> PromptVersions,
    IReadOnlyList<IncubatorSession> RecentFailures,
    IReadOnlyList<IncubatorSession> RecentCompleted,
    int Users,
    IReadOnlyDictionary<string, int> TierCounts);

/// <summary>
/// <para><b>قِراءاتُ الاستوديو — والمُستَأجِرُ فيها ثابِتٌ لا وَسيط.</b>
/// جَلَساتُ الحاضِنَة تَقَع في <c>FeasibilityAnalysisService.IncubatorTenant</c>،
/// ومُستَخدِمو الاستوديو وسِجِلّاتُ الموافَقَة في
/// <c>StudioAuth.Tenant</c>. فَالسلاجُ مَكتوبٌ بِثابِتٍ مُسَمّىً لا
/// بِسِلسِلَةٍ عابِرَة — و<b>الجَلسَةُ مُسَلَّجَة في الحالَتَين</b>،
/// فَيَمُرُّ فاحِصُ الشَكل.</para>
///
/// <para><b>ولا تُخلَط الجَلستان</b>: هُما مُستَأجِرانِ مُختَلِفان
/// فِعلاً، وقِراءَةُ أَحَدِهِما مِن جَلسَة الآخَر تُعطي فَراغاً صامِتاً.
/// ولِذلك تَبقى القِراءَتانِ مُنفَصِلَتَين هُنا كَما كانَتا في
/// الصَفحَة حَرفاً.</para>
/// </summary>
public sealed class StudioQueries
{
    private readonly IDocumentStore _store;

    public StudioQueries(IDocumentStore store) => _store = store;

    /// <summary>هَل وَقَّعَ صاحِبُ الاستوديو على هذِه النُسخَة مِن
    /// الشُروط؟ — <c>ConsentRecord</c> في مُستَأجِر الاستوديو.</summary>
    public async Task<bool> HasConsentedAsync(
        Guid userId, int version, CancellationToken ct = default)
    {
        await using var s = _store.QuerySession(StudioAuth.Tenant);
        var existing = await s.Query<ConsentRecord>()
            .Where(c => c.UserId == userId && c.Version == version)
            .ToListAsync(ct);
        return existing.Count > 0;
    }

    /// <summary>
    /// <para>لَوحَةُ المُراقَبَة كامِلَةً. <b>والحِسابُ انتَقَلَ مَعَ
    /// القِراءَة</b>: كُلُّ عَدّادٍ هُنا مُشتَقٌّ مِن نَفس القائِمَة
    /// المَجلوبَة، فَإبقاؤُه في الصَفحَة كانَ يَعني تَمريرَ ‏كُلّ
    /// الجَلَسات و‏كُلّ المُستَخدِمين إلى التَخطيط لِيَعُدَّهُم.</para>
    ///
    /// <para><c>Stores</c> ليسَ هُنا عَمداً — مَصدَرُه سِجِلُّ
    /// المُستَأجِرين العامّ، و<see cref="TenantDirectory"/> تَملِكُه
    /// وَحدَها.</para>
    /// </summary>
    public async Task<MonitorSnapshot> MonitorAsync(CancellationToken ct = default)
    {
        await using var qs = _store.QuerySession(FeasibilityAnalysisService.IncubatorTenant);
        var sessions = (await qs.Query<IncubatorSession>().ToListAsync(ct)).ToList();

        var completed = sessions.Count(s => s.Status == IncubatorStatus.Completed);

        var snapshot = new MonitorSnapshot(
            Total:     sessions.Count,
            Completed: completed,
            Analyzing: sessions.Count(s => s.Status == IncubatorStatus.Analyzing),
            Failed:    sessions.Count(s => s.Status == IncubatorStatus.Failed),
            AvgQuality: completed > 0
                ? (int)sessions.Where(s => s.Status == IncubatorStatus.Completed)
                               .Average(s => s.AnalysisQualityScore)
                : 0,
            PromptVersions: sessions
                .Where(s => !string.IsNullOrEmpty(s.PromptVersion))
                .GroupBy(s => s.PromptVersion)
                .ToDictionary(g => g.Key, g => g.Count()),
            RecentFailures: sessions
                .Where(s => s.Status == IncubatorStatus.Failed)
                .OrderByDescending(s => s.UpdatedAt).Take(5).ToList(),
            RecentCompleted: sessions
                .Where(s => s.Status == IncubatorStatus.Completed)
                .OrderByDescending(s => s.UpdatedAt).Take(8).ToList(),
            Users: 0,
            TierCounts: new Dictionary<string, int>());

        await using var us = _store.QuerySession(StudioAuth.Tenant);
        var allUsers = (await us.Query<StudioUser>().ToListAsync(ct)).ToList();

        return snapshot with
        {
            Users = allUsers.Count,
            TierCounts = allUsers.GroupBy(u => u.Tier).ToDictionary(g => g.Key, g => g.Count()),
        };
    }
}
