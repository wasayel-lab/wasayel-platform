using Marten;

namespace ACommerce.Kit.Subscriptions;

/// <summary>
/// <para><b>الباعِث الغائِب</b> — أَوَّل مُصدِر لِـ<c>QuotaConsumed</c>.
/// نَموذَج الحِصَّة كانَ مَكتوباً كامِلاً (‏<c>Apply</c> لِخَمسَة أَحداث)
/// <b>وبِلا باعِث واحِد</b>؛ وهذا الصَنف لا يَبني نَموذَجاً بَديلاً بَل
/// يُعطي القائِمَ مُصدِرَه.</para>
///
/// <para><b>والذَرِّيَّة مِن البِنيَة القائِمَة لا مِن آلِيَّة
/// جَديدَة</b>: إسقاط <c>Subscription</c> مُسَجَّل
/// <c>SnapshotLifecycle.Inline</c> (‏<c>HostingExtensions.cs</c>)، أَي
/// أَنّ اللَقطَة تُحدَّث في <b>نَفس مُعامَلَة</b> إلحاق الحَدَث. ومَسار
/// إنشاء الإعلان جَلسَة واحِدَة بِـ<c>SaveChangesAsync</c> واحِدَة.
/// فَإلحاق <c>QuotaConsumed</c> في تِلكَ الجَلسَة بِعَينِها يُعطي: إمّا
/// يُكتَب الإعلان وتُستَهلَك الحِصَّة مَعاً، أَو لا يُكتَب شَيء.</para>
/// </summary>
public sealed class SubscriptionEntitlements : IEntitlements
{
    private readonly IDocumentStore _store;

    public SubscriptionEntitlements(IDocumentStore store) => _store = store;

    /// <summary>
    /// <para><b>قُدرَة واحِدَة، ولا يُسمَح بِما سِواها صامِتاً.</b>
    /// مَصدَر حَقيقَة هذا التَنفيذ تَيارُ أَحداث <c>Subscription</c>،
    /// وهو لا يَعرِف عَن حُدود الاستوديو شَيئاً — تِلكَ عَدّاداتُها في
    /// وَثيقَة <c>StudioUser</c> وتَنفيذُها مَوجَة تالِيَة.</para>
    ///
    /// <para><b>ولِماذا يَرمي بَدَل أَن يُمَرِّر</b>: «سَمَحتُ لِأَنّي لا
    /// أَعرِف هذه القُدرَة» هو بِعَينِه شَكل العَطَب الَّذي قَتَلَ
    /// <c>OperationEngine</c> في المُستودَع القَديم — يَمُرّ كُلّ شَيء
    /// بِصَمت، ويَخضَرّ كُلّ اختِبار مُوجِب. الرَمي يَجعَل سوءَ التَركيب
    /// عَطَباً <b>مَسموعاً</b>.</para>
    /// </summary>
    /// <remarks>
    /// <para><b>وصارَت قُدرَتَين لا واحِدَة</b> —
    /// <c>api.call</c> انضَمَّت. وهي <b>رايَة</b> لا حِصَّة، فَلا
    /// تَمُرّ بِعَدّاد التَيار: <see cref="Decide"/> يَفصِل
    /// الصِنفَين بِـ<c>CapabilityCatalog.IsQuota</c>، و
    /// <see cref="ConsumeAsync"/> لا يُلحِق <c>QuotaConsumed</c>
    /// لِرايَة — فَحَدَثُ استِهلاكٍ بِلا رَصيدٍ يَنقُص عَبَثٌ
    /// يَكذِب على السِجِلّ.</para>
    /// </remarks>
    public IReadOnlyCollection<string> Handles { get; } =
        new[] { CapabilityCatalog.ApiCall, CapabilityCatalog.ListingCreate };

    /// <summary>رِسالَة النَفاد — لِلوغ ولِلمُنادي. النَصّ الَّذي
    /// <b>يَراه المُستَخدِم</b> يُختار عِندَ التَصيير مِن قامُوس
    /// المَفاتيح، لا مِن هُنا (القاعِدَة ١١): مَسار الإعلان يَرُدّ
    /// بِـ<c>err=quota</c> وتَقرَؤُها الصَفحَة.</summary>
    public const string ReasonExhaustedAr =
        "نَفِدَت حِصَّة الإعلانات في باقَتِكَ الحاليَّة.";

    // ─── السُؤال بِلا أَثَر ────────────────────────────────────────────

    public async Task<EntitlementResult> PeekAsync(
        string tenantSlug, Guid userId, string capability,
        CancellationToken ct = default)
    {
        Guard(capability);

        await using var s = _store.QuerySession(tenantSlug);
        var sub = await ActiveSubscriptionAsync(s, userId, ct);
        return Decide(sub, capability, amount: 1);
    }

    // ─── الفَحص والاستِهلاك مَعاً ──────────────────────────────────────

    public async Task<EntitlementResult> ConsumeAsync(
        IDocumentSession session, string tenantSlug, Guid userId,
        string capability, int amount = 1, CancellationToken ct = default)
    {
        Guard(capability);
        ArgumentOutOfRangeException.ThrowIfLessThan(amount, 1);

        var sub = await ActiveSubscriptionAsync(session, userId, ct);
        var decision = Decide(sub, capability, amount);

        // لا اشتِراك ⇒ بِلا حَدّ ⇒ لا حَدَث. ومَنعٌ ⇒ لا حَدَث.
        // الإلحاق يَقَع عِندَ السَماح <b>ومَع اشتِراك قائِم</b> فَقَط.
        if (!decision.Allowed || sub is null) return decision;

        // ورايَةٌ لا تُستَهلَك: لا رَصيدَ يَنقُص، فَلا حَدَثَ يُلحَق.
        // <c>QuotaConsumed</c> على رايَةٍ يَكتُب في السِجِلّ استِهلاكاً
        // لَم يَقَع، ويَجعَل «كَم استُهلِكَ» سُؤالاً بِجَوابَين.
        if (!CapabilityCatalog.IsQuota(capability)) return decision;

        // نُسخَة التَيار قَبل الإلحاق — ومِنها النُسخَة المُتَوَقَّعَة
        // بَعدَه. هذا هو ما يَجعَل الخاسِر في السِباق يَفشَل عِندَ
        // الحِفظ بَدَل أَن يُنقِص الرَصيد مَرَّتَين.
        var state = await session.Events.FetchStreamStateAsync(sub.Id, ct);
        var expectedAfterAppend = (state?.Version ?? 0) + 1;

        session.Events.Append(
            sub.Id,
            expectedAfterAppend,
            new QuotaConsumed(sub.Id, capability, amount, DateTime.UtcNow));

        return decision;
    }

    // ─── الدَوالّ المُشتَرَكَة ─────────────────────────────────────────

    /// <summary><b>بَوّابَتان لا واحِدَة</b>: الرَمز مِن المَعجَم
    /// المُغلَق، وهذا التَنفيذ يَخدِمُه فِعلاً.</summary>
    private void Guard(string capability)
    {
        CapabilityCatalog.Require(capability);

        if (!Handles.Contains(capability))
            throw new NotSupportedException(
                $"‏{nameof(SubscriptionEntitlements)} لا يَخدِم القُدرَة «{capability}» — " +
                $"يَخدِم: {string.Join("، ", Handles)}. " +
                "حُدود الاستوديو مَصدَرُها وَثيقَة لا تَيار، وتَنفيذُها مُنفَصِل.");
    }

    /// <summary>
    /// <para><b>التَكافُؤ الصِفريّ هو العَقد</b>: مُستَخدِم بِلا اشتِراك
    /// فَعّال يُعطي <c>Allowed</c> بِرَصيد بِلا حَدّ — أَي <b>سُلوك
    /// اليَوم حَرفاً</b>، إذ لا سَطر في مَسار إنشاء الإعلان يَذكُر
    /// اشتِراكاً ولا خُطَّة. فَهذه المَوجَة تُضيف باعِثاً غائِباً ولا
    /// تُغلِق باباً كانَ مَفتوحاً.</para>
    ///
    /// <para><b>والرايَةُ تُفصَل عَن الحِصَّة هُنا، بِسَطرٍ واحِد</b>:
    /// حِسابُ <c>QuotaRemaining</c> جَوابٌ عَن سُؤالٍ عَدَدِيّ، وطَرحُه
    /// على رايَةٍ يُعطي مَعنىً مَقلوباً — مُشتَرِكٌ نَفِدَت حِصَّةُ
    /// إعلاناتِه كانَ سَيُمنَع مِن الـAPI، وهُما حَدّانِ لا عَلاقَةَ
    /// بَينَهُما. فَالرايَةُ تُجيب بِنَعَم و<c>Unlimited</c> —
    /// <b>وهذا هُوَ التَكافُؤ الصِفريّ حَرفاً</b>: لا رَقمَ لِحِصَّةِ
    /// API في المُستَودَع ولا في <c>docs/</c>، واختِراعُه اختِراعُ
    /// بَياناتِ مُنتَج (القاعِدَة ١٦). ويَومَ يَقولُ المالِكُ «الباقَةُ
    /// الفُلانِيَّة لا تَشمَل الـAPI» يُقرَأُ ذلك هُنا، في هذا
    /// السَطر — والمُرَشِّحُ يَسأَلُ سَلَفاً ويَرُدُّ ‏403 (‏مُثبَتٌ في
    /// <c>ApiKeyFilter</c> بِاختِبارٍ سالِب)، فَالطَرَفُ الَّذي
    /// يَفرِضُ قائِمٌ مِن يَومِه.</para>
    /// </summary>
    private static EntitlementResult Decide(Subscription? sub, string capability, int amount)
    {
        if (!CapabilityCatalog.IsQuota(capability))
            return new EntitlementResult(true, capability, Entitlements.Unlimited, null);

        if (sub is null)
            return new EntitlementResult(true, capability, Entitlements.Unlimited, null);

        if (sub.QuotaRemaining < amount)
            return new EntitlementResult(
                false, capability, Math.Max(sub.QuotaRemaining, 0), ReasonExhaustedAr);

        return new EntitlementResult(true, capability, sub.QuotaRemaining - amount, null);
    }

    /// <summary>اشتِراك المُستَخدِم الفَعّال — نَفس المُرَشِّح والتَرتيب
    /// اللَذَين يَستَعمِلُهُما <c>SubscriptionHandlers.MySubscription</c>
    /// حَرفاً، فَلا يَختَلِف ما يُفحَص عَمّا يُعرَض.</summary>
    private static Task<Subscription?> ActiveSubscriptionAsync(
        IQuerySession s, Guid userId, CancellationToken ct) =>
        s.Query<Subscription>()
            .Where(x => x.UserId == userId && x.Status == "active")
            .OrderByDescending(x => x.StartsAt)
            .FirstOrDefaultAsync(ct)!;
}
