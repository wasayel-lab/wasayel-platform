using ACommerce.Kit.Payments.Providers.Paddle;
using ACommerce.Kit.Payments.Providers.PayPal;
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

    /// <summary>
    /// <para><b>رِباطُ باقَةِ المَنَصَّةِ بِخُطَّةِ PayPal، أَو
    /// <c>null</c>.</b> ومُعَرِّفُها <b>سلاجُ الباقَة</b> لا سلاجُ
    /// مَتجَر — الخُطَّةُ خَصيصَةُ الباقَة (‏ADR-004 §٣-ج).</para>
    ///
    /// <para><b>والسُقوطُ عِندَ الخَطَأ «لا رِباط» لا انفِجار</b>:
    /// جَدوَلُ الرِباطِ لا يوجَد في قاعِدَةٍ لَم تُهاجَر بَعد، و
    /// <c>null</c> تَعني رُجوعاً إلى قيمَةِ المِلَفّ — أَي سُلوكَ
    /// اليَومِ حَرفاً.</para>
    /// </summary>
    public async Task<PlatformPlanPayPal?> PayPalPlanAsync(
        string? planSlug, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(planSlug)) return null;
        try
        {
            await using var s = _store.QuerySession();
            return await s.LoadAsync<PlatformPlanPayPal>(planSlug, ct);
        }
        catch { return null; }
    }

    /// <summary>
    /// <para><b>طَلَباتُ الدَفعِ المَرِنَةِ لِمَتجَرٍ واحِد</b>
    /// (‏ADR-006) — الأَحدَثُ أَوَّلاً، لِشاشَةِ المُشرِف.</para>
    ///
    /// <para><b>والسُقوطُ عِندَ الخَطَأ «لا طَلَبات» لا انفِجار</b>:
    /// جَدوَلُ الطَلَباتِ لا يوجَد في قاعِدَةٍ لَم تُهاجَر بَعد،
    /// والقائِمَةُ الفارِغَةُ تَعني <b>سُلوكَ اليَومِ حَرفاً</b> — لا
    /// بِطاقَةَ طَلَبات.</para>
    /// </summary>
    public async Task<IReadOnlyList<PayPalOrderRecord>> OrdersForAsync(
        string? tenantSlug, int take = 20, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(tenantSlug)) return Array.Empty<PayPalOrderRecord>();
        try
        {
            await using var s = _store.QuerySession();
            var all = await s.Query<PayPalOrderRecord>()
                .Where(o => o.TenantSlug == tenantSlug)
                .OrderByDescending(o => o.CreatedAt).Take(take).ToListAsync(ct);
            return all.ToList();
        }
        catch { return Array.Empty<PayPalOrderRecord>(); }
    }

    /// <summary>
    /// <para><b>أَحدَثُ دَفعٍ مُعَلَّقٍ لِكُلّ مَتجَر</b> — لِلافِتَةِ
    /// الاستوديو، بِاستِعلامٍ واحِدٍ لا واحِدٍ لِكُلّ مَتجَر.</para>
    ///
    /// <para><b>و«مُعَلَّق» تَعني «لَم يُلتَقَط بَعد»</b>: طَلَبٌ
    /// مُلتَقَطٌ أَو مَرفوضٌ أَو مَعكوسٌ رابِطُه لا يُفضي إلى شَيء،
    /// <b>ومَدخَلٌ يَضُرّ أَسوَأُ مِن غِيابِ مَدخَل</b> (القاعِدَة ١٢).</para>
    /// </summary>
    public async Task<IReadOnlyDictionary<string, PayPalOrderRecord>> PendingOrdersAsync(
        CancellationToken ct = default)
    {
        try
        {
            await using var s = _store.QuerySession();
            var open = await s.Query<PayPalOrderRecord>()
                .Where(o => o.Status == PayPalOrderStatuses.Created
                         || o.Status == PayPalOrderStatuses.Approved)
                .ToListAsync(ct);

            return open
                .Where(o => !string.IsNullOrWhiteSpace(o.ApproveUrl))
                .GroupBy(o => o.TenantSlug, StringComparer.Ordinal)
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderByDescending(o => o.CreatedAt).First(),
                    StringComparer.Ordinal);
        }
        catch { return new Dictionary<string, PayPalOrderRecord>(StringComparer.Ordinal); }
    }

    /// <summary>
    /// <para><b>مُعامَلاتُ Paddle لِمَتجَرٍ واحِد</b> — الأَحدَثُ
    /// أَوَّلاً، لِشاشَةِ المُشرِف. ونَفسُ عادَةِ
    /// <see cref="OrdersForAsync"/> حَرفاً: <b>السُقوطُ عِندَ الخَطَأ
    /// «لا مُعامَلات» لا انفِجار</b> — جَدوَلُها لا يوجَد في قاعِدَةٍ
    /// لَم تُهاجَر بَعد، والقائِمَةُ الفارِغَةُ تَعني سُلوكَ اليَومِ
    /// حَرفاً.</para>
    /// </summary>
    public async Task<IReadOnlyList<PaddleTransactionRecord>> PaddleTransactionsForAsync(
        string? tenantSlug, int take = 20, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(tenantSlug)) return Array.Empty<PaddleTransactionRecord>();
        try
        {
            await using var s = _store.QuerySession();
            var all = await s.Query<PaddleTransactionRecord>()
                .Where(t => t.TenantSlug == tenantSlug)
                .OrderByDescending(t => t.CreatedAt).Take(take).ToListAsync(ct);
            return all.ToList();
        }
        catch { return Array.Empty<PaddleTransactionRecord>(); }
    }

    /// <summary>
    /// <para><b>أَحدَثُ مُعامَلَةٍ تَنتَظِرُ دَفعاً لِكُلّ مَتجَر</b> —
    /// لِلافِتَةِ الاستوديو، بِاستِعلامٍ واحِدٍ لا واحِدٍ لِكُلّ
    /// مَتجَر.</para>
    ///
    /// <para><b>و«تَنتَظِر» تَعني «لَم يَصِل مالُها بَعد»</b>:
    /// مُعامَلَةٌ اكتَمَلَت أَو أُلغِيَت أَو رُدَّ مالُها رابِطُها لا
    /// يُفضي إلى شَيء، <b>ومَدخَلٌ يَضُرّ أَسوَأُ مِن غِيابِ
    /// مَدخَل</b> (القاعِدَة ١٢).</para>
    /// </summary>
    public async Task<IReadOnlyDictionary<string, PaddleTransactionRecord>> PendingPaddleAsync(
        CancellationToken ct = default)
    {
        try
        {
            await using var s = _store.QuerySession();
            var open = await s.Query<PaddleTransactionRecord>()
                .Where(t => t.Status == PaddleTransactionStatuses.Created)
                .ToListAsync(ct);

            return open
                .Where(t => !string.IsNullOrWhiteSpace(t.CheckoutUrl))
                .GroupBy(t => t.TenantSlug, StringComparer.Ordinal)
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderByDescending(t => t.CreatedAt).First(),
                    StringComparer.Ordinal);
        }
        catch { return new Dictionary<string, PaddleTransactionRecord>(StringComparer.Ordinal); }
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
