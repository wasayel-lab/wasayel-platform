using System.Collections.Concurrent;
using ACommerce.Platform.Flows;
using Marten;

namespace ACommerce.Templates.Customer.Marketplace.Services;

/// <summary>
/// <para><b>مَكانيكا تَعريفات المُستَأجِر — مَرَّةً واحِدَة.</b> الكاش
/// بِمِفتاح المُستَأجِر، والقِراءَة بِجَلسَة سلاجِه، والسُقوط الآمِن،
/// والسَرد، والاقتِراح، وحارِس القَرار، والإبطال. ما يَبقى في كُلّ
/// خِدمَة هو <b>مُفرَداتُها</b>: نَوع الوَثيقَة، ومُصادِقُها، وكَيفَ
/// تُبنى لَقطَتُها، ورَسائِلُها العَرَبيَّة.</para>
///
/// <para><b>ولِماذا الآن ولَم يَكُن قَبلَ اليَوم</b>: القاعِدَة ١ تَشتَرِط
/// <b>ثَلاثَة مُستَهلِكين قَبل الاستِخراج</b>. كانَ الأَدوار والمَظهَر
/// اثنَين — والاستِخراج حينَها كانَ سَيَكون تَجريداً يَسبِق مُستَهلِكَه،
/// أَي العَطَب الَّذي تُعالِجُه هذه المَوجَة نَفسُها. الباقات ثالِثُهُما،
/// فَصارَ الشَرط مُستَوفىً.</para>
///
/// <para><b>والقِياس الَّذي بَرَّرَه</b>: بَعدَ تَطبيع أَسماء الأَنواع،
/// ‏<b>104 مِن 124</b> سَطراً مَنطِقِيّاً في <c>TenantRoleService</c>
/// تَظهَر حَرفِيّاً في <c>TenantThemeService</c> (‏83.9%). والمُختَلِف
/// لَيسَ بِنيَةً بَل <b>سَبع رَسائِل عَرَبيَّة</b> وفَرقٌ سُلوكيّ واحِد
/// في <c>ProposeAsync</c> (المَظهَر يُصادِق قَبل التَخزين، والأَدوار
/// يُصادِق في المُنَفِّذ قَبلَه). ولِذلك <b>صَنفٌ قاعِدَة بِأَعضاء
/// مُجَرَّدَة لا قالِبٌ بِأَحَدَ عَشَرَ مُفَوَّضاً</b>: المُفرَدات
/// تَبقى مَقروءَةً في مَوضِعِها، والمَكانيكا لا تُنسَخ.</para>
///
/// <para><b>وشَرطُ التَبديل</b> (القاعِدَة ٣): تَوصيفا الأَدوار والمَظهَر
/// القائِمان يَبقَيانِ خَضراوَين <b>بِلا تَعديل حَرف</b> — لا رِسالَة
/// تَتَغَيَّر، ولا تَرتيب، ولا سُلوك سُقوط.</para>
/// </summary>
public abstract class TenantDefinitionService<TDoc, TSet>
    where TDoc : class, ITenantDefinitionDocument, new()
    where TSet : class
{
    protected readonly IDocumentStore Store;

    /// <summary>مِفتاحُه سلاج المُستَأجِر — وهذا شَرط لا تَحسين.</summary>
    private readonly ConcurrentDictionary<string, TSet> _cache = new(StringComparer.Ordinal);

    protected TenantDefinitionService(IDocumentStore store) => Store = store;

    // ─── المُفرَدات الَّتي تُعَرِّفُها كُلّ خِدمَة ─────────────────────

    /// <summary>اللَقطَة بِلا أَيّ وَثيقَة — قاعِدَة المَنصَّة.</summary>
    protected abstract TSet PlatformSet { get; }

    /// <summary>سلاج المُستَأجِر داخِل اللَقطَة، أَو <c>null</c> —
    /// وهو ما يُمَيِّز اللَقطَة الناجِحَة مِن السُقوط.</summary>
    protected abstract string? SlugOf(TSet set);

    /// <summary>يَبني اللَقطَة مِن الوَثائِق. المَنطِق مُختَلِف بِحَقّ:
    /// الأَدوار تُلحِق قائِمَةً، والمَظهَر يَختار واحِداً يَغلِب.</summary>
    protected abstract TSet Build(string tenantSlug, IReadOnlyList<TDoc> docs);

    /// <summary>وَسم اللوغ (‏<c>roles</c>، <c>theme</c>، <c>plans</c>).</summary>
    protected abstract string LogTag { get; }

    /// <summary>رِسالَة سُقوط السَرد — كامِلَةً بِوَسمِها.</summary>
    protected abstract string ListFailureAr(string tenantSlug, string error);

    /// <summary>مُصادَقَة ما قَبل التَخزين. الأَدوار تُرجِع
    /// <c>(true, "")</c> — مُصادِقُها يَسبِقُها في المُنَفِّذ — والمَظهَر
    /// يُصادِق هُنا. <b>الفَرق مُعلَن في التَوقيع لا مَخفيّ في وَسيط.</b></summary>
    protected abstract (bool Ok, string Message) ValidateBeforeStore(string definitionJson, string slug);

    /// <summary>مُصادَقَة الاعتِماد — تُعاد عَلى النَصّ المُخَزَّن، لا
    /// يُوثَق بِأَنَّها جَرَت عِندَ الكِتابَة.</summary>
    protected abstract (bool Ok, string Message) ValidateBeforeApprove(TDoc doc);

    protected abstract string AlreadyApprovedAr(string slug, string tenantSlug);
    protected abstract string ProposedAr(string slug);
    protected abstract string NotFoundAr(string slug, string tenantSlug);
    protected abstract string DecidedAr(string slug, string tenantSlug, bool approved);

    // ─── المَكانيكا — مَرَّةً واحِدَة لِلثَلاثَة ───────────────────────

    /// <summary>لَقطَة المُستَأجِر. سلاج فارِغ أَو <c>null</c> (سِياق بِلا
    /// مُستَأجِر) ← قاعِدَة المَنصَّة.</summary>
    public async Task<TSet> ForAsync(string? tenantSlug, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(tenantSlug)) return PlatformSet;
        if (_cache.TryGetValue(tenantSlug, out var hit)) return hit;

        var set = await ReadUncachedCoreAsync(tenantSlug, ct);

        // الفَشَل لا يُخَزَّن — يُعرَف بِأَنَّه سَقَطَ إلى لَقطَة المَنصَّة
        // بِلا سلاج، وتَخزينُه كانَ سَيُجَمِّد خَلَلاً عابِراً.
        if (SlugOf(set) is not null) _cache[tenantSlug] = set;
        return set;
    }

    /// <summary>
    /// <para>نَفس القِراءَة بِلا كاش. كُلّ خِدمَة تُحيلُها إلى دالَّتِها
    /// <b>الساكِنَة</b> — لِأَنّ مَسارات <c>minimal-API</c> تَملِك
    /// <c>IDocumentStore</c> ولا تَملِك الخِدمَة، فَتَحتاج مَدخَلاً
    /// ساكِناً. والإحالَة تَضمَن أَنّ المَسارَين <b>جِسمٌ واحِد</b> لا
    /// جِسمانِ يَنحَرِفان.</para>
    /// </summary>
    protected abstract Task<TSet> ReadUncachedCoreAsync(string tenantSlug, CancellationToken ct);

    /// <summary>
    /// <para><b>الاستِعلام نَفسُه — مَرَّةً واحِدَة لِلثَلاثَة.</b>
    /// ساكِنَة لِأَنّ المَداخِل الساكِنَة تُناديها.</para>
    ///
    /// <para><b>والعَزل بُنيَويّ لا اتِّفاقيّ</b>: الجَلسَة تُفتَح بِـ
    /// <c>QuerySession(tenantSlug)</c>، والوَثيقَة مُتَعَدِّدَة الإيجار
    /// بِسِياسَة <c>AllDocumentsAreMultiTenanted</c> — فَـMarten يَضَع
    /// <c>tenant_id</c> في الاستِعلام. لا سَطر شَرط مَكتوب بِاليَد
    /// يُمكِن نِسيانُه، ولا استِعلام عابِر لِلمُستَأجِرين مُمكِن أَصلاً
    /// مِن هُنا.</para>
    ///
    /// <para><b>وسُقوط آمِن عِندَ تَعَذُّر القِراءَة</b>: يُرجَع
    /// <paramref name="fallback"/> — أَي سُلوك اليَوم حَرفاً. مَتجَر
    /// يَفقِد تَعريفاً لِثَوانٍ أَهوَن مِن مَتجَر يَسقُط بِـ500.</para>
    /// </summary>
    protected static async Task<TSet> QueryApprovedAsync(
        IDocumentStore store, string tenantSlug,
        Func<string, IReadOnlyList<TDoc>, TSet> build,
        TSet fallback, Func<Exception, string> onError,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(tenantSlug)) return fallback;

        try
        {
            await using var s = store.QuerySession(tenantSlug);
            var docs = await s.Query<TDoc>()
                .Where(d => d.Status == ApprovalFlow.Approved)
                .ToListAsync(ct);
            return build(tenantSlug, docs);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(onError(ex));
            return fallback;
        }
    }

    /// <summary>كُلّ تَعريفات المُستَأجِر بِكُلّ حالاتِها — لِسَطح
    /// الإدارَة وَحدَه. المَقروء في السُطوح هو المُعتَمَد فَقَط.</summary>
    public async Task<IReadOnlyList<TDoc>> ListAllAsync(
        string tenantSlug, CancellationToken ct = default)
    {
        try
        {
            await using var s = Store.QuerySession(tenantSlug);
            var all = await s.Query<TDoc>().ToListAsync(ct);
            return all.OrderBy(d => d.CreatedAt)
                      .ThenBy(d => d.Slug, StringComparer.Ordinal).ToList();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ListFailureAr(tenantSlug, ex.Message));
            return Array.Empty<TDoc>();
        }
    }

    /// <summary><b>يَكتُب تَعريفاً مُعَلَّقاً</b> — لا حالَة أُخرى مِن
    /// هُنا.</summary>
    public async Task<(bool Ok, string Message)> ProposeAsync(
        string tenantSlug, string slug, string definitionJson,
        string by, CancellationToken ct = default)
    {
        var pre = ValidateBeforeStore(definitionJson, slug);
        if (!pre.Ok) return pre;

        await using var s = Store.LightweightSession(tenantSlug);

        var existing = await s.LoadAsync<TDoc>(slug, ct);
        if (existing is { Status: ApprovalFlow.Approved })
            return (false, AlreadyApprovedAr(slug, tenantSlug));

        s.Store(new TDoc
        {
            Id             = slug,
            Slug           = slug,
            DefinitionJson = definitionJson,
            Status         = ApprovalFlow.Pending,
            CreatedBy      = by,
            CreatedAt      = DateTime.UtcNow
        });
        await s.SaveChangesAsync(ct);
        Invalidate(tenantSlug);
        return (true, ProposedAr(slug));
    }

    /// <summary>قَرار بَشَريّ: اعتِماد أَو رَفض. <b>هُنا وَحدَه</b> يَصير
    /// تَعريف حَيّاً، وهُنا وَحدَه يُبطَل الكاش.</summary>
    public async Task<(bool Ok, string Message)> DecideAsync(
        string tenantSlug, string slug, string status,
        string by, CancellationToken ct = default)
    {
        // الحارِس يَسأَل تَعريف التَدَفُّق، لا شَرطاً مَكتوباً بِاليَد.
        if (!ApprovalFlow.IsDecision(status))
            return (false, $"قَرار غَير مَعروف: «{status}».");

        await using var s = Store.LightweightSession(tenantSlug);
        var doc = await s.LoadAsync<TDoc>(slug, ct);
        if (doc is null) return (false, NotFoundAr(slug, tenantSlug));

        if (status == ApprovalFlow.Approved)
        {
            var check = ValidateBeforeApprove(doc);
            if (!check.Ok) return check;
        }

        doc.Status    = status;
        doc.DecidedBy = by;
        doc.DecidedAt = DateTime.UtcNow;
        s.Store(doc);
        await s.SaveChangesAsync(ct);
        Invalidate(tenantSlug);

        return (true, DecidedAr(slug, tenantSlug, status == ApprovalFlow.Approved));
    }

    /// <summary>إبطال لَقطَة مُستَأجِر واحِد. مِفتاح واحِد لا مَسح شامِل —
    /// مَتجَر يُعَدِّل تَعريفاتِه لا يُكَلِّف بَقِيَّة المَتاجِر
    /// قِراءَةً.</summary>
    public void Invalidate(string tenantSlug) => _cache.TryRemove(tenantSlug, out _);
}
