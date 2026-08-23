using Marten;

namespace ACommerce.Kit.Subscriptions;

/// <summary>
/// <para><b>الاستِحقاقُ على مُستَوى المَتجَر</b> — يَخدِم قُدرَةً واحِدَةً
/// (<see cref="CapabilityCatalog.TenantWrite"/>) ومَصدَرُ حَقيقَتِه
/// وَثيقَةُ <see cref="TenantPlan"/>.</para>
///
/// <para><b>ولِماذا تَنفيذٌ ثانٍ لا سَطرٌ في الأَوَّل</b>: تَعليقُ
/// <see cref="SubscriptionEntitlements"/> يَقول بِنَفسِه إنّ «حُدود
/// الاستوديو مَصدَرُها وَثيقَةٌ لا تَيار، وتَنفيذُها مُنفَصِل» —
/// فَالفَصلُ بِمَصدَر الحَقيقَة اصطِلاحٌ قائِمٌ لا اختِراع. وذاكَ
/// يَقرَأ تَيارَ أَحداثِ اشتِراكِ <b>مُستَخدِم</b>، وهذا يَقرَأ وَثيقَةَ
/// باقَةِ <b>مُستَأجِر</b> — ودَمجُهُما كانَ سَيَجعَل رَمياً واحِداً
/// يَبتَلِع الصِنفَين.</para>
///
/// <para><b>والتَكافُؤُ الصِفريّ هُوَ العَقد</b>: لا وَثيقَةَ باقَةٍ ⇒
/// <see cref="TenantPlanState.None"/> ⇒ <b>سَماحٌ بِلا حَدّ</b>، وهو
/// جَوابُ كُلّ مَتجَرٍ قائِمٍ اليَوم. <b>وكَذلك عِندَ فَشَل
/// القِراءَة</b>: قاعِدَةٌ لَم تُهاجَر بَعد لا يَجوز أَن تُغلِق
/// المَتاجِرَ كُلَّها — <b>وذاكَ فَشَلٌ مَفتوحٌ بِقَصد</b>، مُقابِلُه
/// أَنّ العَطَبَ يُوَسِّع لا يُضَيِّق، وهو الصَحيحُ لِرايَةٍ
/// تِجارِيَّة لا لِحارِسِ هُوِيَّة.</para>
/// </summary>
public sealed class TenantPlanEntitlements : IEntitlements
{
    private readonly IDocumentStore _store;

    public TenantPlanEntitlements(IDocumentStore store) => _store = store;

    public IReadOnlyCollection<string> Handles { get; } =
        new[] { CapabilityCatalog.TenantWrite };

    /// <summary>رِسالَةُ المَنع — لِلوغ ولِلمُنادي. والنَصُّ الَّذي
    /// يَراه المُستَخدِم يُختار عِندَ التَصيير مِن قامُوس المَفاتيح
    /// (القاعِدَة ١١).</summary>
    public const string ReasonLockedAr =
        "انتَهَت باقَةُ هذا المَتجَر — لا تُقبَل الكِتابَةُ حَتّى تُجَدَّد.";

    public async Task<EntitlementResult> PeekAsync(
        string tenantSlug, Guid userId, string capability,
        CancellationToken ct = default)
    {
        Guard(capability);
        return Decide(await ReadAsync(tenantSlug, ct), capability, DateTime.UtcNow);
    }

    /// <summary>
    /// <para><b>رايَةٌ لا تُستَهلَك</b>: نَفسُ جَواب <see cref="PeekAsync"/>
    /// بِلا حَدَثٍ يُلحَق. و<c>QuotaConsumed</c> على رايَةٍ يَكتُب في
    /// السِجِلّ استِهلاكاً لَم يَقَع — نَفسُ قَرار
    /// <see cref="SubscriptionEntitlements"/> حَرفاً.</para>
    ///
    /// <para><b>والجَلسَةُ المُمَرَّرَة لا تُستَعمَل هُنا بِقَصد</b>:
    /// وَثيقَةُ الباقَة <c>SingleTenanted</c>، وجَلسَةُ العَمَلِيَّة
    /// مَحصورَةٌ بِسلاج المُستَأجِر — فَالقِراءَةُ مِنها تُعطي
    /// <c>null</c> دائِماً، <b>وحارِسٌ يَقرَأ null يَسمَح دائِماً</b>.</para>
    /// </summary>
    public async Task<EntitlementResult> ConsumeAsync(
        IDocumentSession session, string tenantSlug, Guid userId,
        string capability, int amount = 1, CancellationToken ct = default)
    {
        Guard(capability);
        return Decide(await ReadAsync(tenantSlug, ct), capability, DateTime.UtcNow);
    }

    // ─── الدَوالّ المُشتَرَكَة ─────────────────────────────────────────

    private void Guard(string capability)
    {
        CapabilityCatalog.Require(capability);
        if (!Handles.Contains(capability))
            throw new NotSupportedException(
                $"‏{nameof(TenantPlanEntitlements)} لا يَخدِم القُدرَة «{capability}» — " +
                $"يَخدِم: {string.Join("، ", Handles)}.");
    }

    private async Task<TenantPlan?> ReadAsync(string tenantSlug, CancellationToken ct)
    {
        try
        {
            await using var s = _store.QuerySession();
            return await s.LoadAsync<TenantPlan>(tenantSlug, ct);
        }
        catch { return null; }
    }

    /// <summary><b>القَرارُ دالَّةٌ نَقِيَّة</b> — الوَقتُ يُمَرَّر،
    /// فَيُختَبَر بِلا قاعِدَةِ بَيانات.</summary>
    public static EntitlementResult Decide(TenantPlan? plan, string capability, DateTime now)
    {
        var state = TenantPlanPolicy.Derive(plan, now);
        var allowed = TenantPlanPolicy.AllowsWrite(state);
        return new EntitlementResult(
            allowed, capability, Entitlements.Unlimited,
            allowed ? null : ReasonLockedAr);
    }
}
