using ACommerce.Kit.Subscriptions;
using Marten;

namespace ACommerce.Templates.Customer.Marketplace.Services.Queries;

/// <summary>
/// <para><b>قِراءَةُ باقَةِ المُستَأجِر وإعداداتِ المَنَصَّة</b> —
/// لِشاشَتَي الإدارَة، ولِلافِتَةِ الاستوديو، ولِلحارِس.</para>
///
/// <para><b>ولِماذا جَلسَةٌ بِلا سلاجٍ هُنا كَما في
/// <see cref="TenantDirectory"/></b>: ‏<see cref="TenantPlan"/> و
/// <see cref="PlatformSettings"/> مُسَجَّلَتانِ <c>SingleTenanted()</c>
/// صَراحَةً — الأولى <b>عَلاقَةُ المَتجَرِ بِالمَنَصَّة</b> لا بَيانٌ
/// داخِلَه، والثانِيَةُ إعدادُ المَنَصَّةِ نَفسِها. وجَلسَةٌ بِسلاجٍ
/// عَلَيهِما تَبحَث في مُستَأجِرٍ لا وُجودَ لَه فَتُعطي <c>null</c>
/// دائِماً — <b>وذاكَ أَسوَأُ مِن خَطَأ: حارِسٌ يَقرَأ null يَسمَح
/// دائِماً</b>. فَالمِلَفُّ مُثَبَّتٌ بِاسمِه في
/// <c>TenantConfigServiceShapeTests</c> بِالاتِّجاهَين.</para>
///
/// <para><b>والسُقوطُ عِندَ الخَطَأ «لا باقَة» لا انفِجار</b>: جَدوَلُ
/// الباقات لا يوجَد في قاعِدَةٍ لَم تُهاجَر بَعد، و<c>null</c> تَعني
/// <see cref="TenantPlanState.None"/> — أَي سُلوكَ اليَومِ حَرفاً. وهذا
/// هُوَ التَكافُؤُ الصِفريّ: <b>الحارِسُ الجَديدُ لا يُغلِق مَتجَراً
/// بِسَبَب عَطَبٍ في قِراءَتِه</b>.</para>
/// </summary>
public sealed class PlatformBillingQueries
{
    private readonly IDocumentStore _store;

    public PlatformBillingQueries(IDocumentStore store) => _store = store;

    /// <summary>باقَةُ مُستَأجِرٍ واحِد، أَو <c>null</c>.</summary>
    public async Task<TenantPlan?> PlanAsync(string tenantSlug, CancellationToken ct = default)
    {
        try
        {
            await using var s = _store.QuerySession();
            return await s.LoadAsync<TenantPlan>(tenantSlug, ct);
        }
        catch { return null; }
    }

    /// <summary>باقاتُ كُلّ المَتاجِر — لِلَوحَةِ المُشرِف.</summary>
    public async Task<IReadOnlyDictionary<string, TenantPlan>> AllPlansAsync(
        CancellationToken ct = default)
    {
        try
        {
            await using var s = _store.QuerySession();
            var all = await s.Query<TenantPlan>().ToListAsync(ct);
            return all.ToDictionary(p => p.Id, StringComparer.Ordinal);
        }
        catch { return new Dictionary<string, TenantPlan>(StringComparer.Ordinal); }
    }

    /// <summary>إعداداتُ المَنَصَّة — وَثيقَةٌ جَديدَةٌ فارِغَةٌ حينَ لا
    /// تُخَزَّن بَعد، فَالشاشَةُ تَرسِم حَقلاً فارِغاً لا خَطَأً.</summary>
    public async Task<PlatformSettings> SettingsAsync(CancellationToken ct = default)
    {
        try
        {
            await using var s = _store.QuerySession();
            return await s.LoadAsync<PlatformSettings>(PlatformSettings.SingletonId, ct)
                   ?? new PlatformSettings();
        }
        catch { return new PlatformSettings(); }
    }
}
