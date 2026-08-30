using Marten;

namespace ACommerce.Templates.Customer.Marketplace.Services.Incubator;

/// <summary>
/// <para>حُدود الباقَة لِمُدَّة ٣٠ يَوم. <b>وكُلُّ حَدٍّ مُنتَهٍ — لا
/// <c>int.MaxValue</c> بَعدَ اليَوم.</b></para>
///
/// <para><b>العِلَّةُ المَقيسَة (‏2026-08-30)</b>: كانَت <c>scale</c>
/// تَحمِل <c>int.MaxValue</c> في الحُدودِ الثَلاثَة، فَشَرطُ البَوّابَةِ
/// <c>u.AnalysesUsed &gt;= l.AnalysesPerMonth</c> <b>لا يَصدُق أَبَداً</b>
/// — أَي بَوّابَةٌ مَكتوبَةٌ لا تُغلَق. وكُلُّ تَحليلٍ نِداءُ نَموذَجِ
/// لُغَةٍ على <b>مِفتاحِ المالِك</b>: فَالحَدُّ اللانِهائيُّ لَيسَ
/// كَرَماً في باقَةٍ بَل <b>فاتورَةً مَفتوحَةً على حِسابِه</b>. وقَد
/// اجتَمَعَ ذلك مَعَ تَرقِيَةٍ ذاتِيَّةٍ بِلا دَفعٍ في
/// <c>/studio/billing/select</c>، فَصارَ أَيُّ زائِرٍ يَملِك سَقفاً
/// لا نِهائِيّاً على مِفتاحٍ لَيسَ لَه.</para>
///
/// <para><b>سُحِبَ <c>AllowCustomPattern</c></b> (كانَ <c>false</c> في
/// spark و lite، و<c>true</c> في growth و scale). كانَ يُعرَض ميزَةً
/// مَدفوعَة في صَفحَة الباقات وفي نافِذَة التَرقِيَة، و<b>لَم يَفحَصه
/// مَوضِع واحِد</b> — سَبع إصاباتٍ كُلُّها تَعريف أَو عَرض.</para>
///
/// <para>ولَم يُفرَض لِأَنّ المَسار الَّذي يَحرُسُه <b>غَير مَوجود</b>:
/// نَمَط التَطبيق تَستَنبِطُه قَواعِد
/// <see cref="PatternMatcher"/> مِن إجابات الاكتِشاف، ويُخَزَّن في
/// <c>IncubatorSession.SuggestedPattern</c>، ويَقرَؤُه
/// <c>/studio/s/{id}/build</c> مُباشَرَةً — واستِمارَة البِناء تُرسِل
/// الاسم والسلاج واللَون والشِعار والمَدينَة **ولا تُرسِل نَمَطاً**.
/// فَلا اختِيار لِلمُستَخدِم ولا تَعديل بَعد الإنشاء. حِراسَة مَعدومٍ
/// شَرطٌ لا يَكذِب أَبَداً — وذلك أَسوَأ مِن غِيابِه، لِأَنَّه يُوهِم
/// أَنّ المَنع قائِم.</para>
///
/// <para>القاعِدَة المُطَبَّقَة: تُباع الميزَة حينَ توجَد. فَحينَ يُبنى
/// اختِيار النَمَط، يَعود الحَقل ويَعود سَطراه في
/// <c>StudioBilling.razor</c> و<c>UpgradePrompt.razor</c> — ومَعَهُما
/// فَحصٌ حَقيقيّ عِندَ البِناء.</para>
/// </summary>
public sealed record TierLimits(
    string Tier, string LabelAr, int MonthlyPriceSar,
    int AnalysesPerMonth, int RefinesPerMonth, int StoresMax,
    bool AllowExport);

public static class TierCatalog
{
    public static readonly IReadOnlyDictionary<string, TierLimits> All = new Dictionary<string, TierLimits>
    {
        ["spark"]  = new("spark",  "Spark",   99,  AnalysesPerMonth: 1, RefinesPerMonth: 3,
                         StoresMax: 1, AllowExport: false),
        ["lite"]   = new("lite",   "Lite",    199, AnalysesPerMonth: 3, RefinesPerMonth: 10,
                         StoresMax: 3, AllowExport: true),
        ["growth"] = new("growth", "Growth",  399, AnalysesPerMonth: 10, RefinesPerMonth: 50,
                         StoresMax: 10, AllowExport: true),
        // ‏`scale` — أَرقامٌ **مُنتَهِيَة**، مَصدَرُها تَكليفُ المالِكِ
        // يَومَ ‏2026-08-30 (‏40 تَحليلاً · 200 تَحسيناً · 40 مَتجَراً)
        // لا اجتِهادُ الكود (القاعِدَة ١٦). وهي أَربَعَةُ أَضعافِ
        // `growth` في التَحاليلِ والمَتاجِر — فَالسَقفُ يَبقى بَعيداً
        // عَن مُستَخدِمٍ حَقيقيّ، وقَريباً بِما يَكفي لِيُغلِقَ البابَ
        // في وَجهِ حَلقَةٍ آلِيَّة.
        ["scale"]  = new("scale",  "Scale",   999, AnalysesPerMonth: 40,
                         RefinesPerMonth: 200, StoresMax: 40,
                         AllowExport: true),
    };

    public static TierLimits For(string tier)
        => All.TryGetValue(tier, out var t) ? t : All["spark"];
}

/// <summary>
/// <para><b>سَبَبُ دَعوَةِ التَرقِيَة — مَعجَمٌ مُغلَقٌ بِتَعريفٍ
/// واحِد.</b> أَربَعَةٌ لا خامِس، وهي بِعَينِها قيَمُ <c>?upgrade=</c>
/// في العُنوان.</para>
///
/// <para><b>ولِماذا صارَت ثَوابِتَ</b> (‏2026-08-30): كانَت مَكتوبَةً
/// <b>حَرفِيّاً في ثَمانِيَةِ مَواضِع</b> — أَربَعٍ تَكتُبُها في
/// العُنوان وثَلاثٍ تُطابِقُها في <c>UpgradePrompt.razor</c>. ومَعجَمٌ
/// مُغلَقٌ بِلا تَعريفٍ واحِدٍ يَنجَرِف: خَطَأُ إملاءٍ في طَرَفٍ يَجعَل
/// الرِسالَةَ <b>تَصمُت</b> — أَي رَفضاً واقِعاً وشاشَةً لا تَقول
/// شَيئاً، وهُوَ بِعَينِه «الرَفضُ المُبتلَع».</para>
///
/// <para><b>وانجِرافٌ وُجِدَ عِندَ العَدّ ويُقالُ ولا يُبتلَع</b>:
/// نُقطَةُ التَصديرِ كانَت تَرُدُّ بِـ<c>?upgrade=refine</c> —
/// فَتَقولُ الشاشَةُ «بَلَغتَ حَدَّ التَحسينات» لِمَن لَم يَبلُغه،
/// و<b>سَطرٌ يَكذِب أَسوَأُ مِن سَطرٍ غائِب</b>. صارَ لَها
/// <see cref="Export"/> بِنَصِّها.</para>
/// </summary>
public static class StudioUpgradeReason
{
    /// <summary>بَلَغَ حَدَّ التَحاليلِ الشَهريّ.</summary>
    public const string Analyses = "analyze";

    /// <summary>بَلَغَ حَدَّ التَحسينات.</summary>
    public const string Refines = "refine";

    /// <summary>بَلَغَ حَدَّ التَطبيقاتِ المَبنِيَّة.</summary>
    public const string Stores = "build";

    /// <summary>ميزَةُ التَصديرِ لَيسَت في باقَتِه — <b>حَجبُ ميزَةٍ
    /// لا خَرقُ حِصَّة</b>، ولِذلك رَمزٌ رابِعٌ لا إعادَةُ استِعمالِ
    /// ثالِث.</summary>
    public const string Export = "export";

    /// <summary>رُموزُ خَرقِ الحِصَّةِ وَحدَها — ما تُصدِرُه
    /// <see cref="StudioTierService.GateCheck"/>.</summary>
    public static readonly IReadOnlyList<string> QuotaCodes =
        new[] { Analyses, Refines, Stores };

    public static readonly IReadOnlyList<string> All =
        new[] { Analyses, Refines, Stores, Export };
}

/// <summary>
/// خِدمَة الـ tier gates — تَفحَص الحُدود قَبل العَمَلِيّات وتَكتُب الـ
/// counters. كُلّ ٣٠ يَوم تُعاد الفَترَة تِلقائيّاً.
/// </summary>
public sealed class StudioTierService
{
    private readonly IDocumentStore _store;
    public StudioTierService(IDocumentStore store) => _store = store;

    public async Task<StudioUser?> GetUserAsync(Guid userId, CancellationToken ct = default)
    {
        await using var qs = _store.QuerySession(StudioAuth.Tenant);
        return await qs.LoadAsync<StudioUser>(userId, ct);
    }

    /// <summary>طول فَترَة الحِصَّة بِالأَيّام.</summary>
    public const int PeriodDays = 30;

    /// <summary><b>قاعِدَة انقِضاء الفَترَة، نَقِيَّة</b> — بِلا قاعِدَة
    /// بَيانات ولا ساعَة ضِمنِيَّة، لِتُختَبَر وَحدَها. الشَرط
    /// <c>&gt;=</c> لا <c>&gt;</c>: هو سُلوك اليَوم حَرفاً.</summary>
    public static bool PeriodElapsed(DateTime periodStart, DateTime nowUtc)
        => (nowUtc - periodStart).TotalDays >= PeriodDays;

    /// <summary>
    /// <para><b>يُطَبِّق دَوَران الفَترَة على نُسخَة في الذاكِرَة</b> —
    /// نَفس الحِساب الَّذي كانَ يُكتَب، بِلا كِتابَة. يُعيد
    /// <c>true</c> إن دارَت الفَترَة فِعلاً.</para>
    /// </summary>
    public static bool ApplyPeriodRollover(StudioUser user, DateTime nowUtc)
    {
        if (!PeriodElapsed(user.PeriodStart, nowUtc)) return false;
        user.PeriodStart  = nowUtc;
        user.AnalysesUsed = 0;
        user.RefinesUsed  = 0;
        return true;
    }

    /// <summary>
    /// <para><b>قِراءَة نَقِيَّة</b> — لا تَمَسّ قاعِدَة البَيانات
    /// بِكِتابَة. تُفتَح بِـ<c>QuerySession</c> فَلا تَملِك أَن تَكتُب
    /// أَصلاً (المَنع بُنيَويّ لا اتِّفاقيّ)، ويُطَبَّق دَوَران الفَترَة
    /// على النُسخَة المُعادَة وَحدَها — فَالمَعروض هو <b>الحالَة
    /// الفِعلِيَّة</b> كَما كانَ تَماماً.</para>
    ///
    /// <para><b>ولِماذا انفَصَلَت</b>: كانَت تُسَمّى
    /// <c>LoadWithLimitsAsync</c> وتَحوي <c>Store</c> و
    /// <c>SaveChangesAsync</c>. فَنِداءُ عَرضٍ في
    /// <c>StudioShell.razor</c> — وهو غِلاف كُلّ صَفَحات الاستوديو —
    /// كانَ يَكتُب في قاعِدَة البَيانات <b>عِندَ كُلّ رَسم</b>. اِسمٌ
    /// يَقول «حَمِّل» وفِعلٌ يَكتُب: أَسوَأ ما في العَطَب أَنّ
    /// المُنادي لا يُمكِنُه أَن يَعلَم.</para>
    /// </summary>
    public async Task<(StudioUser User, TierLimits Limits)> ReadWithLimitsAsync(
        Guid userId, CancellationToken ct = default)
    {
        await using var qs = _store.QuerySession(StudioAuth.Tenant);
        var user = await qs.LoadAsync<StudioUser>(userId, ct)
                   ?? throw new InvalidOperationException("user not found");
        // نُسخَة مُنفَصِلَة عَن أَيّ تَتَبُّع — التَعديل هُنا عَرضٌ لا حِفظ.
        ApplyPeriodRollover(user, DateTime.UtcNow);
        return (user, TierCatalog.For(user.Tier));
    }

    /// <summary>
    /// <para>نَتيجَةُ البَوّابَة. <b>و<see cref="BreachCode"/> رَمزٌ مِن
    /// <see cref="StudioUpgradeReason"/> لا جُملَةٌ عَرَبِيَّة</b>.</para>
    ///
    /// <para><b>ولِماذا تَبَدَّلَ الحَقل</b> (‏2026-08-30): كانَ
    /// <c>Reason</c> جُملَةً مَكتوبَةً في الكودِ ولَه <b>صِفرُ
    /// مُستَهلِك</b> — المُنادونَ الثَلاثَةُ يَقرَؤونَ <c>Allowed</c>
    /// وَحدَها ثُمَّ يُعيدونَ التَوجيهَ بِنَصٍّ <b>حَرفيٍّ</b>
    /// (<c>?upgrade=analyze</c>) تُطابِقُه <c>UpgradePrompt</c> بِنَصٍّ
    /// حَرفيٍّ آخَر. فَكانَ لَدَينا مَعجَمٌ مُغلَقٌ بِلا تَعريفٍ
    /// واحِد — يَنجَرِف بِخَطَأِ إملاءٍ فَتَصمُت الرِسالَة، وجُملَةٌ
    /// عَرَبِيَّةٌ في C# لا يَراها أَحَد.</para>
    /// </summary>
    public sealed record GateCheck(bool Allowed, int Used, int Limit, string? BreachCode);

    public async Task<GateCheck> CheckAnalyzeAsync(Guid uid, CancellationToken ct = default)
    {
        var (u, l) = await ReadWithLimitsAsync(uid, ct);
        return u.AnalysesUsed >= l.AnalysesPerMonth
            ? new(false, u.AnalysesUsed, l.AnalysesPerMonth, StudioUpgradeReason.Analyses)
            : new(true,  u.AnalysesUsed, l.AnalysesPerMonth, null);
    }

    public async Task<GateCheck> CheckRefineAsync(Guid uid, CancellationToken ct = default)
    {
        var (u, l) = await ReadWithLimitsAsync(uid, ct);
        return u.RefinesUsed >= l.RefinesPerMonth
            ? new(false, u.RefinesUsed, l.RefinesPerMonth, StudioUpgradeReason.Refines)
            : new(true,  u.RefinesUsed, l.RefinesPerMonth, null);
    }

    public async Task<GateCheck> CheckBuildAsync(Guid uid, CancellationToken ct = default)
    {
        var (u, l) = await ReadWithLimitsAsync(uid, ct);
        return u.StoresBuilt >= l.StoresMax
            ? new(false, u.StoresBuilt, l.StoresMax, StudioUpgradeReason.Stores)
            : new(true,  u.StoresBuilt, l.StoresMax, null);
    }

    public Task RecordAnalysisAsync(Guid uid, CancellationToken ct = default)
        => Bump(uid, u => u.AnalysesUsed++, ct);

    public Task RecordRefineAsync(Guid uid, CancellationToken ct = default)
        => Bump(uid, u => u.RefinesUsed++, ct);

    public Task RecordStoreBuiltAsync(Guid uid, CancellationToken ct = default)
        => Bump(uid, u => u.StoresBuilt++, ct);

    /// <summary>
    /// <para><b>الكِتابَة الصَريحَة</b> — ونُقطَة المَعنى الَّتي تَقَع
    /// عِندَها: استِهلاك الحِصَّة فِعلاً. هُنا وَحدَه يُثَبَّت دَوَران
    /// الفَترَة في قاعِدَة البَيانات، لا عِندَ كُلّ رَسم.</para>
    ///
    /// <para>والتَرتيب مَقصود: يَدور أَوَّلاً ثُمَّ يَزيد — وإلّا
    /// زادَ عَدّاداً مِن فَترَةٍ مُنقَضِيَة. والدَوَران والزِيادَة في
    /// <b>حِفظٍ واحِد</b>، فَلا تَقَع إحداهُما دونَ الأُخرى.</para>
    /// </summary>
    private async Task Bump(Guid uid, Action<StudioUser> mutate, CancellationToken ct)
    {
        await using var s = _store.LightweightSession(StudioAuth.Tenant);
        var u = await s.LoadAsync<StudioUser>(uid, ct);
        if (u is null) return;
        ApplyPeriodRollover(u, DateTime.UtcNow);
        mutate(u);
        s.Store(u);
        await s.SaveChangesAsync(ct);
    }
}
