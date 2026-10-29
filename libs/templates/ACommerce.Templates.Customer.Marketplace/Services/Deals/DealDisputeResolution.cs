namespace ACommerce.Templates.Customer.Marketplace.Services.Deals;

/// <summary>خَرقٌ واحِدٌ في تَخويلِ حَسمِ النِزاع — <c>Code</c> مِن <see cref="DealDisputeResolution.All"/>، و<c>MessageAr</c> لِلمُراجِعِ البَشَريّ.
/// شَكلُ <see cref="DealCancelViolation"/> بِحَرفِه (القاعِدَة ٤؛ عائِلَةُ «first» في ‏ADR-038).</summary>
public sealed record DealResolveViolation(string Code, string MessageAr);

/// <summary>
/// <para><b>مَن يَحسِمُ نِزاعاً عَلى صَفقَة، وإلى أَين</b> (‏2026-10-29، قَرارُ المالِك د38): «صاحب المتجر» يَحسِمُه — «مبدأ الحياد: وسايل أداة لا
/// طرف في البيع» — بِأَحَدِ بَديلَين يَختارُهُما هُوَ: <see cref="Cancel"/> «إلغاء مع تسجيل استرجاع مستحقّ إن كانت مدفوعة» (بِكاتِبِ الإلغاءِ القائِمِ وأَثَرِه
/// الماليّ)، أَو <see cref="Resume"/> «استئناف من المرحلة نفسها». كانَت «نِزاع» طَريقاً بِلا مَخرَج: كُلُّ كاتِبٍ لِلحالَةِ يَشتَرِطُ <c>Active</c>
/// (تَوصيفُه <c>DealDisputeDeadEndTests</c>).</para>
/// <para><b>بَوّابَةٌ عَلى حالَةٍ مَخزونَة — تُجيبُ بِأَوَّلِ خَرقٍ بِتَرتيبٍ ثابِت</b> (‏ADR-038، القاعِدَة ٦): الغِياب، ثُمَّ الهُوِيَّة (صاحِبُ المَتجَرِ وَحدَه —
/// <see cref="DealCanceller.IsStoreAdmin"/> يُثبِتُه الحارِسُ الأَعلى ويَقولُه)، ثُمَّ الحالَة (<c>Disputed</c>)، ثُمَّ الحَقلُ (البَديلُ مِن المَعجَم). فَغَيرُ
/// المُخَوَّلِ لا يَعرِفُ حالَ صَفقَةٍ لَيسَت لَه، ولا يَكونُ خَطَأُ الحَقلِ قِناعاً لِثَغرَة. دالَّةٌ نَقِيَّة: لا قاعِدَةَ ولا وَقت.</para>
/// </summary>
public static class DealDisputeResolution
{
    /// <summary>البَديلُ الأَوَّل: إلغاءُ الصَفقَةِ بِأَثَرِ الإلغاءِ الماليِّ القائِم (استِرجاعٌ آلِيٌّ، أَو سَطرُ «استِرجاعُه مُستَحَقٌّ عِندَ بَوّابَةِ المَتجَر»).
    /// (‏2026-10-29، مُراجَعَةُ تَيّارِ S5 الأولى — ولا يُعادُ حَرفٌ فَوقَه: أَو سَطرُ «رَدٌّ نَقدِيٌّ مُستَحَقٌّ عَلى البائِع» لِنَقدٍ قُبِضَ عِندَ التَسليم.)</summary>
    public const string Cancel = "cancel";

    /// <summary>البَديلُ الثاني: استِئنافُ الصَفقَةِ مِن مَرحَلَتِها نَفسِها — تَعودُ <c>Active</c> ولا تَتَحَرَّكُ مَرحَلَتُها.</summary>
    public const string Resume = "resume";

    /// <summary>مَعجَمُ البَديلَينِ المُغلَق — اثنانِ لا ثالِثَ لَهُما.</summary>
    public static readonly IReadOnlyList<string> Outcomes = new[] { Cancel, Resume };

    /// <summary>لا صَفقَةَ بِهذا المُعَرِّف في هذا المُستَأجِر — الرَمزُ نَفسُه في <see cref="DealCancelAuthorization"/>.</summary>
    public const string DealNotFound = DealCancelAuthorization.DealNotFound;

    /// <summary>الفاعِلُ لَيسَ صاحِبَ المَتجَر — والطَرَفانِ لا يَحسِمانِ نِزاعَهُما.</summary>
    public const string ActorNotStoreAdmin = "actor_not_store_admin";

    /// <summary>الصَفقَةُ لَيسَت في «نِزاع» — لا شَيءَ يُحسَم (ومِنه نَقرَةٌ ثانِيَةٌ بَعدَ حَسمٍ وَقَع).</summary>
    public const string DealNotDisputed = "deal_not_disputed";

    /// <summary>البَديلُ المَطلوبُ خارِجَ <see cref="Outcomes"/>.</summary>
    public const string OutcomeUnknown = "outcome_unknown";

    /// <summary>المَعجَمُ المُغلَق — أَربَعَةٌ لا خامِسَ لَها.</summary>
    public static readonly IReadOnlyList<string> All = new[] { DealNotFound, ActorNotStoreAdmin, DealNotDisputed, OutcomeUnknown };

    public static bool IsOutcome(string? outcome) => outcome is not null && Outcomes.Contains(outcome, StringComparer.Ordinal);

    /// <summary><c>null</c> يَعني مُخَوَّل. والتَرتيبُ مَقصود: الغِياب، ثُمَّ الهُوِيَّة، ثُمَّ الحالَة، ثُمَّ البَديل.</summary>
    public static DealResolveViolation? Validate(Deal? deal, DealCanceller by, string? outcome)
    {
        if (deal is null)
            return new(DealNotFound, "لا صَفقَةَ بِهذا المُعَرِّف.");

        if (!by.IsStoreAdmin)
            return new(ActorNotStoreAdmin,
                "لا يَحسِمُ النِزاعَ إلّا صاحِبُ المَتجَر — والنِزاعُ بَينَ المَتجَرِ وعُمَلائِه شَأنُه ومَسؤولِيَّتُه.");

        if (deal.Status != DealStatus.Disputed)
            return new(DealNotDisputed, $"الصَفقَةُ في حالَة {deal.Status} لا «نِزاع» — لا شَيءَ يُحسَم.");

        if (!IsOutcome(outcome))
            return new(OutcomeUnknown, $"البَديلُ «{outcome}» خارِجَ المَعجَم: {string.Join("، ", Outcomes)}.");

        return null;
    }

    public static bool IsAllowed(Deal? deal, DealCanceller by, string? outcome) => Validate(deal, by, outcome) is null;
}

/// <summary>ناتِجُ حَسمِ النِزاع — شَكلُ <see cref="DealCancelResult"/>: ما كُتِب، أَو أَوَّلُ رَدٍّ أَوقَفَه (ومِنه رُموزُ كاتِبِ الإلغاءِ القائِمِ وتَعارُضُ النُسخَة).
/// <see cref="Outcome"/> البَديلُ الَّذي وَقَعَ — يَقرَؤُه الإشعارُ وسَطرُ التَدقيق.</summary>
public sealed record DealResolveResult(bool Ok, Deal? Deal, DealResolveViolation? Violation, string? Outcome)
{
    public static DealResolveResult Refused(DealResolveViolation v, Deal? deal = null) => new(false, deal, v, null);

    public static DealResolveResult Resolved(Deal deal, string outcome) => new(true, deal, null, outcome);
}
