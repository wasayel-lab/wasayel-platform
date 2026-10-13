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
///
/// <para><b>وأُغلِقَ «قَد تَكتُبانِ تَقييمَين» ‏2026-10-13 — ولا يُعادُ حَرفٌ فَوقَه</b> (مُراجَعَةُ المَوجَة 3a — G4 (iv)؛ ‏ADR-036 §١٢‑٤): التَقييمُ
/// يُكتَبُ بِمُعَرِّفٍ حَتمِيٍّ مِن (الصَفقَة، الكاتِب) — <see cref="ReviewIdFor"/> — و<c>Insert</c> لا <c>Store</c>، فَالثانِيَةُ تَرتَطِمُ بِالمِفتاحِ
/// الأَساسِيِّ في القاعِدَةِ ولا تُكتَب، وتُجابُ «قَيَّمَ مِن قَبل» كَما يُجابُ مَن سُئِلَ فَوُجِد. مَقيسٌ حَيّاً بِـ<c>SaveGate</c>: ٢ ← ١
/// (<c>LiveDealReviewOnceProofTests</c>). ولا فِهرِسَ ولا تَرحيلَ في المُخَطَّط — المِفتاحُ قائِم؛ ولا يَمَسُّ تَقييمَ الاستوديو الإداريّ (مُعَرِّفُه عَشوائِيّ).</para>
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
        // ونَقرَةٌ ثانِيَةٌ مَرَّت بِالسُؤالِ قَبلَ أَن تُحفَظَ الأولى تَرتَطِمُ بِالمُعَرِّفِ الحَتمِيِّ عِندَ الحِفظ — فَتُجابُ كَما يُجابُ مَن سُئِلَ
        // فَوُجِد (‏2026-10-13، G4 (iv)؛ ‏ADR-036 §١٢‑٤).
        try
        {
            var review = await _reviews.SubmitAsync(tenantSlug, targetId, targetName, by.ActorId, by.ActorName,
                rating, body, dealId: dealId, dealPattern: deal!.Pattern, ct: ct, id: ReviewIdFor(dealId, by.ActorId));
            return DealReviewResult.Reviewed(deal, review);
        }
        catch (JasperFx.DocumentAlreadyExistsException)
        {
            return DealReviewResult.AlreadyReviewed(deal!);
        }
    }

    /// <summary>
    /// <para><b>مُعَرِّفُ تَقييمِ (الصَفقَة، الكاتِب) — حَتمِيٌّ</b> (‏2026-10-13، G4 (iv)): أَوَّلُ ‏16 بايتاً مِن SHA-256 عَلى بادِئَةٍ ثابِتَةٍ ومُعَرِّفَيهِما،
    /// بِعَلامَةِ الإصدارِ ‏8 (مُعَرِّفٌ مُخَصَّص — RFC 9562) والمُتَغَيِّرِ ‏10. دالَّةٌ نَقِيَّة: الطَرَفانِ في صَفقَةٍ واحِدَةٍ مُعَرِّفانِ لا واحِد، وكاتِبٌ واحِدٌ
    /// في صَفقَتَينِ مُعَرِّفانِ لا واحِد. ومُعَرِّفُ الصَفقَةِ عالَمِيٌّ (‏<c>Guid.NewGuid</c> عِندَ الوِلادَة) فَلا يَدخُلُ المُستَأجِرُ في البَصمَة.</para>
    /// </summary>
    public static Guid ReviewIdFor(Guid dealId, Guid authorId)
    {
        Span<byte> input = stackalloc byte[Prefix.Length + 32];
        Prefix.CopyTo(input);
        dealId.TryWriteBytes(input[Prefix.Length..]);
        authorId.TryWriteBytes(input[(Prefix.Length + 16)..]);
        Span<byte> hash = stackalloc byte[32];
        System.Security.Cryptography.SHA256.HashData(input, hash);
        var b = hash[..16].ToArray();
        b[7] = (byte)((b[7] & 0x0F) | 0x80);
        b[8] = (byte)((b[8] & 0x3F) | 0x80);
        return new Guid(b);
    }

    private static ReadOnlySpan<byte> Prefix => "wasayel:deal-review:"u8;
}
