using Marten;

namespace ACommerce.Kit.Subscriptions;

/// <summary>
/// <para>جَواب الاستِحقاق — سَماح، ورَصيد، وسَبَب عِندَ المَنع.</para>
/// <para><see cref="Remaining"/> = <see cref="Entitlements.Unlimited"/>
/// (‏<c>-1</c>) تَعني «بِلا حَدّ»، وهي جَواب مَن لا اشتِراك لَه — أَي
/// سُلوك اليَوم حَرفاً.</para>
/// </summary>
public sealed record EntitlementResult(
    bool    Allowed,
    string  Capability,
    int     Remaining,
    string? ReasonAr);

/// <summary>ثَوابِت مُشتَرَكَة لِطَبَقَة الاستِحقاق.</summary>
public static class Entitlements
{
    /// <summary>رَصيد بِلا حَدّ.</summary>
    public const int Unlimited = -1;
}

/// <summary>
/// <para><b>عَقد الاستِحقاق — نِداءٌ واحِد لا نِداءان.</b></para>
///
/// <para><b>القَرار المِحوَريّ</b>: لا <c>CanAsync</c> مُنفَصِلَة عَن
/// <c>Record</c>. الفَصل بَينَهُما <b>هو</b> العَطَب المَقيس في
/// <c>StudioTierService</c>: ثَلاثَة مَواضِع يَفحَص فيها نِداءٌ ثُمَّ
/// يُسَجِّل نِداءٌ آخَر، بَينَهُما — في أَسوَأِها — إنشاءُ مُستَأجِر
/// كامِل. طَلَبانِ مُتَزامِنانِ يَجتازانِ الفَحص مَعاً ثُمَّ يَزيدانِ
/// العَدّاد، والحَدّ واحِد.</para>
///
/// <para><b>ولِماذا <see cref="IDocumentSession"/> في التَوقيع وليسَ
/// <see cref="IDocumentStore"/></b>: هذا هو الفَرق بَينَ ذَرِّيّ وغَير
/// ذَرِّيّ، <b>مَنقولاً مِن جِسم الدالَّة إلى تَوقيعِها</b> (القاعِدَة ٦).
/// مَن يَملِك <c>IDocumentStore</c> يَفتَح جَلسَةً ثانِيَة ويَخسَر
/// الذَرِّيَّة <b>صامِتاً وبِلا شَكوى مِن مُتَرجِم</b>؛ ومَن يَقبَل
/// <c>IDocumentSession</c> لا يَستَطيع ذلك. والقاعِدَة ٧ بِعَينِها:
/// مُعتَرِضٌ يَقرَأ حالَةً مُخَزَّنَة يَجِب أَن يُشارِك جَلسَة
/// العَمَلِيَّة.</para>
/// </summary>
public interface IEntitlements
{
    /// <summary>القُدُرات الَّتي يَخدِمُها هذا التَنفيذ. ما خَرَجَ عَنها
    /// يَرمي — <b>ولا يُسمَح بِه صامِتاً</b>.</summary>
    IReadOnlyCollection<string> Handles { get; }

    /// <summary>
    /// <para><b>سُؤال بِلا أَثَر</b> — لِلعَرض وَحدَه (تَعطيل زِرّ،
    /// شارَة رَصيد، رَدّ مُبَكِّر مِن مُرَشِّح نُقطَة قَبل رَفع الصُوَر).
    /// <b>لا يُبنى عَلَيه قَرار كِتابَة أَبَداً</b>: بَينَ سُؤالِه
    /// وكِتابَتِكَ نافِذَةُ سِباق.</para>
    /// </summary>
    Task<EntitlementResult> PeekAsync(
        string tenantSlug, Guid userId, string capability,
        CancellationToken ct = default);

    /// <summary>
    /// <para><b>الفَحص والاستِهلاك مَعاً، عَلى جَلسَة العَمَلِيَّة.</b>
    /// عِندَ السَماح يُلحِق <c>QuotaConsumed</c> بِتَيار الاشتِراك في
    /// <paramref name="session"/> و<b>لا يَحفَظ</b> — الحِفظ يَقَع
    /// مَرَّةً واحِدَة عِندَ <c>SaveChangesAsync</c> الَّتي تَكتُب
    /// العَمَلِيَّة نَفسَها. عِندَ المَنع لا يُلحِق شَيئاً.</para>
    ///
    /// <para><b>والإلحاق بِنُسخَة مُتَوَقَّعَة</b>: لَو تَقَدَّمَ التَيار
    /// بَينَ القِراءَة والحِفظ رَمى Marten عِندَ
    /// <c>SaveChangesAsync</c> — فَالخاسِر في السِباق يَفشَل
    /// <b>قَبل</b> أَن تُكتَب عَمَلِيَّتُه، لا بَعدَها.</para>
    /// </summary>
    Task<EntitlementResult> ConsumeAsync(
        IDocumentSession session, string tenantSlug, Guid userId,
        string capability, int amount = 1, CancellationToken ct = default);
}
