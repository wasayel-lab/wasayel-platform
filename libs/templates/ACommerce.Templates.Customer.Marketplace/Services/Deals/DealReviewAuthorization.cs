namespace ACommerce.Templates.Customer.Marketplace.Services.Deals;

/// <summary>خَرقٌ واحِدٌ في تَخويلِ تَقييمِ الطَرَفِ الآخَر. <c>Code</c> مِفتاحٌ ثابِتٌ لِلاختِباراتِ
/// واللوغ، و<c>MessageAr</c> لِلمُراجِعِ البَشَريّ. نَفسُ شَكلِ <c>DealCancelViolation</c> و
/// <c>DealAcceptViolation</c> — القالِبُ المَرجِعيُّ في القاعِدَة ٤.</summary>
public sealed record DealReviewViolation(string Code, string MessageAr);

/// <summary>
/// <para><b>مَن يُقَيِّم.</b> الحارِسُ في <b>تَوقيعِ</b> <c>DealReviews.SubmitAsync</c> (القاعِدَة ٦)
/// عَلى نَسَقِ <see cref="DealAcceptor"/> حَرفاً: لا يُمكِنُ نِداءُ التَقييمِ بِلا هذا السِجِلّ، فَلا
/// يوجَدُ مَسارٌ «يَنسى» الحارِسَ لِأَنّ المُتَرجِمَ يَمنَعُه.</para>
///
/// <para><b>ولِماذا هُوِيَّةٌ وَحدَها بِلا رايَةِ سُلطَة</b>: السُلطَةُ مَكتوبَةٌ عَلى الصَفقَةِ نَفسِها —
/// طَرَفاها — فَالقَرارُ مُقارَنَةُ هُوِيَّة. ومالِكُ التَطبيقِ يُقَيِّمُ «نِيابَةً عَن» أَحَدِ الطَرَفَينِ
/// مِن مَسارِه الإداريِّ المُنفَصِل (<c>/studio/apps/{slug}/deals/{id}/review</c>) ولا يَمُرُّ مِن هُنا.</para>
/// </summary>
/// <param name="ActorId">المُستَخدِمُ الكاتِب — مُؤَلِّفُ التَقييم.</param>
/// <param name="ActorName">اسمُه كَما يُكتَبُ في التَقييم.</param>
public sealed record DealReviewer(Guid ActorId, string ActorName);

/// <summary>
/// <para><b>بَوّابَةُ تَقييمِ الطَرَفِ الآخَر — دالَّةٌ نَقِيَّة.</b> لا قاعِدَةَ بَيانات، ولا وَقتَ، ولا
/// عَشوائِيَّة: صَفقَةٌ وكاتِبٌ، ورَمزُ خَرقٍ مِن مَعجَمٍ مُغلَقٍ أَو <c>null</c>.</para>
///
/// <para><b>الثَغرَةُ الَّتي أَغلَقَتها</b> (المُراجَعَةُ الثالِثَةُ عَلى <c>8769fa4</c>، ‏2026-10-11؛
/// ‏ADR-033 §٦‑ح): كانَت <c>POST /{slug}/deals/{id}/review</c> تَختارُ الهَدَفَ —
/// <c>CounterpartyId</c> لِلمُبادِرِ و<c>InitiatorId</c> لِكُلِّ مَن سِواه — وتَكتُبُ ما دامَ مَوجوداً
/// <b>بِلا سُؤالٍ عَن الاكتِمالِ ولا عَن العُضوِيَّةِ ولا عَن أَن يَكونَ الهَدَفُ هُوَ الكاتِب</b>. فَلَمّا
/// رُبِطَ البائِعُ طَرَفاً عِندَ وِلادَةِ صَفقَةِ السَلَّةِ (‏ADR-033 §٦‑ب‑١) صارَ لِمُشتَريها هَدَفٌ مِن
/// لَحظَةِ الطَلَب: طَلَبٌ نَقدِيٌّ بِلا مالٍ ولا قَبولٍ يَكتُبُ تَقييماً «مُوَثَّقاً»
/// (<c>VerifiedTransaction</c>) يَعُدُّه مُلَخَّصُ البائِعِ عَلى كُلِّ إعلان، وكُلُّ طَلَبٍ صَفقَةٌ جَديدَة.
/// وغَيرُ الطَرَفِ كانَ يُقَيِّمُ مُبادِرَ أَيِّ صَفقَة (قائِمٌ قَبلَ <c>8769fa4</c>). والشاشَةُ كانَت تُخفي
/// الاستِمارَةَ إلّا عِندَ <c>Completed</c> — و<c>curl</c> لا يَرى الشاشَة.</para>
///
/// <para><b>ولِماذا التَخويلُ يَسبِقُ فَحصَ الحالَة</b> (القاعِدَة ٦): لَو رُتِّبَ العَكسُ لَصارَ
/// «الصَفقَةُ لَم تَكتَمِل» جَواباً يُفشي حالَةَ صَفقَةٍ لِغَيرِ طَرَفِها. الغِيابُ وَحدَه يَسبِق، لِأَنَّه
/// لا يُفشي شَيئاً. و«الهَدَفُ هُوَ أَنتَ» يَلي العُضوِيَّةَ لِأَنَّه سُؤالُ هُوِيَّةٍ لا حالَة: طَرَفا
/// الصَفقَةِ واحِدٌ — وهذِه صَفقَةٌ لا يَنبَغي أَن توجَد (‏ADR-033 §٦‑ب‑١ مُلحَقاً) — فَلا يُقَيِّمُ
/// المَرءُ نَفسَه في أَيِّ مَرحَلَة.</para>
/// </summary>
public static class DealReviewAuthorization
{
    /// <summary>لا صَفقَةَ بِهذا المُعَرِّفِ في هذا المُستَأجِر.</summary>
    public const string DealNotFound = "deal_not_found";

    /// <summary>الكاتِبُ لَيسَ مُبادِرَ الصَفقَةِ ولا طَرَفَها المُقابِل.</summary>
    public const string ActorNotParty = "actor_not_party";

    /// <summary>الهَدَفُ هُوَ الكاتِبُ نَفسُه — طَرَفا الصَفقَةِ واحِد.</summary>
    public const string SelfReview = "self_review";

    /// <summary>الصَفقَةُ لَم تَبلُغ <c>Completed</c> — التَقييمُ بَعدَ الاكتِمالِ وَحدَه.</summary>
    public const string DealNotCompleted = "deal_not_completed";

    /// <summary>صَفقَةٌ مُكتَمِلَةٌ بِلا طَرَفٍ مُقابِل — فَلا هَدَفَ لِتَقييمِ مُبادِرِها. لا يُبلَغُ بِمَسارٍ
    /// حَيٍّ اليَوم (<c>Booked</c> تَطلُبُ طَرَفاً قَبلَ أَيِّ اكتِمال)؛ وكانَ الجِسمُ القَديمُ يَتَخَطّاه
    /// صامِتاً بِـ<c>target != Guid.Empty</c> — فَصارَ رَمزاً لا صَمتاً.</summary>
    public const string CounterpartyMissing = "counterparty_missing";

    /// <summary>المَعجَمُ المُغلَق — خَمسَةٌ لا سادِسَ لَها.</summary>
    public static readonly IReadOnlyList<string> All =
        new[] { DealNotFound, ActorNotParty, SelfReview, DealNotCompleted, CounterpartyMissing };

    public static bool Contains(string code) => All.Contains(code, StringComparer.Ordinal);

    /// <summary>يَرمي عِندَ الخَرق — لِمَواضِعِ التَركيب. نَفسُ حيلَةِ
    /// <c>DealAcceptAuthorization.Require</c> حَرفاً.</summary>
    public static string Require(string code)
    {
        if (!Contains(code))
            throw new ArgumentException(
                $"الرَمز «{code}» خارِج مَعجَم DealReviewAuthorization. " +
                $"المَعجَم: {string.Join("، ", All)}.", nameof(code));
        return code;
    }

    /// <summary><b>هَدَفُ التَقييمِ — الطَرَفُ الآخَر</b>: لِلمُبادِرِ الطَرَفُ المُقابِلُ (إن وُجِد)، ولِلطَرَفِ
    /// المُقابِلِ المُبادِر، ولِمَن لَيسَ طَرَفاً لا شَيء. والاسمُ الغائِبُ «—» كَما كانَ يَكتُبُه جِسمُ
    /// النُقطَةِ بِحَرفِه.</summary>
    public static (Guid Id, string Name)? TargetOf(Deal deal, Guid reviewerId)
    {
        if (deal.InitiatorId == reviewerId)
            return deal.CounterpartyId is { } counterparty
                ? (counterparty, deal.CounterpartyName ?? "—")
                : null;
        if (deal.CounterpartyId == reviewerId)
            return (deal.InitiatorId, deal.InitiatorName);
        return null;
    }

    /// <summary><c>null</c> يَعني مُخَوَّل. والتَرتيبُ مَقصود: الغِياب، ثُمَّ العُضوِيَّة، ثُمَّ «الهَدَفُ هُوَ
    /// أَنتَ»، ثُمَّ الحالَة (مُكتَمِلَة، ثُمَّ لَها طَرَفٌ يُقَيَّم).</summary>
    public static DealReviewViolation? Validate(Deal? deal, DealReviewer by)
    {
        if (deal is null)
            return new(DealNotFound, "لا صَفقَةَ بِهذا المُعَرِّف.");

        if (deal.InitiatorId != by.ActorId && deal.CounterpartyId != by.ActorId)
            return new(ActorNotParty,
                "الكاتِبُ لَيسَ طَرَفاً في هذِه الصَفقَة — والتَقييمُ يُوسَمُ «مُوَثَّقاً» بِمُعَرِّفِها، " +
                "فَلا يَكتُبُه إلّا شَريكٌ فيها.");

        if (TargetOf(deal, by.ActorId) is { } target && target.Id == by.ActorId)
            return new(SelfReview,
                "طَرَفا الصَفقَةِ واحِدٌ هُوَ الكاتِب — ولا يُقَيِّمُ المَرءُ نَفسَه.");

        if (deal.Status != DealStatus.Completed)
            return new(DealNotCompleted,
                $"الصَفقَةُ في حالَة {deal.Status} — التَقييمُ بَعدَ الاكتِمالِ وَحدَه.");

        if (TargetOf(deal, by.ActorId) is null)
            return new(CounterpartyMissing,
                "الصَفقَةُ مُكتَمِلَةٌ بِلا طَرَفٍ مُقابِل — لا هَدَفَ لِتَقييمِ مُبادِرِها.");

        return null;
    }

    public static bool IsAllowed(Deal? deal, DealReviewer by) => Validate(deal, by) is null;
}

/// <summary>
/// <para>ناتِجُ التَقييم — نَفسُ شَكلِ <c>DealAcceptResult</c>: الرَفضُ يَصِلُ المُنادي بِرَمزِه.</para>
///
/// <para><b>و«قَيَّمَ مِن قَبل» لَيسَ رَفضاً</b>: النَقرَةُ الثانِيَةُ — أَو الاستِمارَةُ المَفتوحَةُ في
/// تَبويبَين — لا تَكتُبُ ثانِيَةً ولا تُرَدُّ، كَما كانَ في جِسمِ النُقطَة؛ و<c>Review</c> فيها
/// <c>null</c> لِأَنَّ الأولى هي الباقِيَة.</para>
/// </summary>
public sealed record DealReviewResult(bool Ok, Deal? Deal, ACommerce.Kit.Reviews.Review? Review, DealReviewViolation? Violation)
{
    public static DealReviewResult Refused(DealReviewViolation v, Deal? deal = null)
        => new(false, deal, null, v);

    public static DealReviewResult Reviewed(Deal deal, ACommerce.Kit.Reviews.Review review)
        => new(true, deal, review, null);

    public static DealReviewResult AlreadyReviewed(Deal deal)
        => new(true, deal, null, null);
}
