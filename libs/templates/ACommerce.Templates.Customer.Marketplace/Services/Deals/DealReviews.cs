using ACommerce.Kit.Reviews;

namespace ACommerce.Templates.Customer.Marketplace.Services.Deals;

/// <summary>
/// <para><b>تَقييمُ الطَرَفِ الآخَرِ في صَفقَة — البابُ الوَحيدُ لِنُقطَةِ العُضو</b>
/// (<c>POST /{slug}/deals/{id}/review</c>). تَحميلٌ، ثُمَّ <see cref="DealReviewAuthorization.Validate"/>
/// النَقِيَّة، ثُمَّ «مَرَّةً لِكُلِّ كاتِبٍ في كُلِّ صَفقَة»، ثُمَّ الكِتابَةُ عَلى الكيتِ القائِم
/// (<see cref="ReviewsService"/>) — لا مَخزَنَ ثانٍ لِلتَقييماتِ ولا أُنبوبَ رابِع (القاعِدَة ٨).</para>
///
/// <para><b>ولِماذا خِدمَةٌ لا أَسطُرٌ في النُقطَة</b> (المُراجَعَةُ الثالِثَةُ عَلى <c>8769fa4</c>،
/// ‏2026-10-11؛ ‏ADR-033 §٦‑ح): كانَ القَرارُ كُلُّه في جِسمِ النُقطَة — قاعِدَةُ الهَدَفِ وشَرطا الكِتابَة —
/// ولا حَرفَ فيه عَن الاكتِمالِ ولا العُضوِيَّةِ ولا النَفس، فَلَمّا تَغَيَّرَ مَعنى <c>CounterpartyId</c>
/// بِالرَبطِ عِندَ الوِلادَةِ لَم يَحمَرَّ شَيء. والحارِسُ الآنَ في التَوقيع (<see cref="DealReviewer"/>
/// إلزامِيّ، القاعِدَة ٦) والجِسمُ مُثَبَّتُ السَقف (<c>EndpointBodyBleedTests</c>). ومُستَهلِكٌ واحِدٌ في
/// وَقتِ التَشغيلِ لا ثَلاثَة (القاعِدَة ١): لَيسَت تَجريداً بَل نَقلُ قَرارٍ مِن جِسمٍ إلى خِدمَةٍ —
/// نَسَقُ <see cref="DealNotices"/>.</para>
///
/// <para><b>ولَقطَةُ القِراءَةِ لا تَتَقادَمُ هُنا — بِخِلافِ القَبول</b> (‏ADR-033 §٦‑ب‑٢): البَوّابَةُ تَمُرُّ
/// بِـ<c>Completed</c> وَحدَها، و<c>Completed</c> نِهائِيَّة — كُلُّ كاتِبٍ يُغَيِّرُ الحالَةَ أَو الطَرَفَ
/// (<c>AdvanceAsync</c>، <c>CancelAsync</c>، <c>DisputeAsync</c>، <c>AssignCounterpartyAsync</c>) يَشتَرِطُ
/// <c>Active</c>. فَما مَرَّ بِالبَوّابَةِ في اللَقطَةِ هُوَ ما في القاعِدَة. أَمّا نَقرَتانِ مُتَزامِنَتانِ
/// فَقَد تَكتُبانِ تَقييمَين (<c>HasReviewedAsync</c> ثُمَّ <c>SubmitAsync</c> بِلا قَيدٍ فَريد) — قائِمٌ قَبلَ
/// هذا ويُقال.</para>
/// </summary>
public sealed class DealReviews
{
    private readonly DealsService _deals;
    private readonly ReviewsService _reviews;

    public DealReviews(DealsService deals, ReviewsService reviews)
    {
        _deals = deals;
        _reviews = reviews;
    }

    public async Task<DealReviewResult> SubmitAsync(
        string tenantSlug, Guid dealId, DealReviewer by, int rating, string body,
        CancellationToken ct = default)
    {
        var deal = await _deals.LoadAsync(tenantSlug, dealId, ct);

        var violation = DealReviewAuthorization.Validate(deal, by);
        if (violation is not null) return DealReviewResult.Refused(violation, deal);

        // مَرَّةً لِكُلِّ كاتِبٍ في كُلِّ صَفقَة — الثانِيَةُ لا تُكتَبُ ولا تُرَدّ، كَما كانَ في الجِسم.
        if (await _reviews.HasReviewedAsync(tenantSlug, by.ActorId, dealId, ct))
            return DealReviewResult.AlreadyReviewed(deal!);

        // ‏`Validate` رَدَّت العَدَمَ وغَيرَ الطَرَفِ والنَفسَ وغَيرَ المُكتَمِلَةِ وما لا هَدَفَ لَه —
        // فَالهَدَفُ هُنا مَوجودٌ وغَيرُ الكاتِب.
        var (targetId, targetName) = DealReviewAuthorization.TargetOf(deal!, by.ActorId)!.Value;
        var review = await _reviews.SubmitAsync(tenantSlug, targetId, targetName, by.ActorId, by.ActorName,
            rating, body, dealId: dealId, dealPattern: deal!.Pattern, ct: ct);
        return DealReviewResult.Reviewed(deal, review);
    }
}
