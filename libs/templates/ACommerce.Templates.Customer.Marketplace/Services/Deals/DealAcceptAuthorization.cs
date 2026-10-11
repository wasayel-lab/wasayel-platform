namespace ACommerce.Templates.Customer.Marketplace.Services.Deals;

/// <summary>خَرقٌ واحِدٌ في تَخويلِ قَبولِ العَرض. <c>Code</c> مِفتاحٌ ثابِتٌ
/// لِلاختِباراتِ واللوغ، و<c>MessageAr</c> لِلمُراجِعِ البَشَريّ. نَفسُ شَكلِ
/// <c>DealCancelViolation</c> — القالِبُ المَرجِعيُّ في القاعِدَة ٤.</summary>
public sealed record DealAcceptViolation(string Code, string MessageAr);

/// <summary>
/// <para><b>مَن يَقبَلُ العَرض.</b> الحارِسُ نَفسُه مَنقولاً إلى <b>تَوقيع</b>
/// <c>DealsService.AcceptAsync</c> (القاعِدَة ٦)، عَلى نَسَقِ
/// <see cref="DealCanceller"/> حَرفاً: لا يُمكِنُ نِداءُ القَبولِ بِلا هذا
/// السِجِلّ، فَلا يوجَدُ مَسارٌ «يَنسى» الحارِسَ لِأَنّ المُتَرجِمَ يَمنَعُه.</para>
///
/// <para><b>ولِماذا هُوِيَّةٌ وَحدَها بِلا رايَةِ سُلطَة</b> (بِخِلاف
/// <c>DealCanceller.IsStoreAdmin</c>): السُلطَةُ هُنا <b>مَكتوبَةٌ عَلى الصَفقَةِ
/// نَفسِها</b> — <c>Refs["listing_owner"]</c> يُلحِقُه مَسارُ إنشاءِ العَرضِ
/// والسَلَّةِ مِن <c>owner_id</c> الإعلان — فَالقَرارُ مُقارَنَةُ هُوِيَّةٍ
/// بِمَرجِع، لا إثباتٌ يُعادُ مِن حارِسٍ أَعلى. ومالِكُ التَطبيقِ لَه مَسارُه
/// الإداريُّ المُنفَصِل (<c>/studio/apps/{slug}/deals/{id}/advance</c>).</para>
/// </summary>
/// <param name="ActorId">المُستَخدِمُ الَّذي سَيَصيرُ الطَرَفَ المُقابِلَ ويُنسَبُ
/// إلَيه سَطرُ <c>assigned</c>.</param>
/// <param name="ActorName">اسمُه كَما يُكتَبُ في السِجِلّ.</param>
public sealed record DealAcceptor(Guid ActorId, string ActorName);

/// <summary>
/// <para><b>بَوّابَةُ قَبولِ العَرض — دالَّةٌ نَقِيَّة.</b> لا قاعِدَةَ بَيانات،
/// ولا وَقتَ، ولا عَشوائِيَّة: صَفقَةٌ وفاعِلٌ، ورَمزُ خَرقٍ مِن مَعجَمٍ مُغلَقٍ
/// أَو <c>null</c>.</para>
///
/// <para><b>الثَغرَةُ الَّتي أَغلَقَتها</b> (موجَزُ الفَجَوات P0-3، ‏2026-10-10؛
/// ‏ADR-033 §٢‑ج‑أ): كانَت <c>POST /{slug}/deals/{id}/accept</c> تَفحَصُ الجَلسَةَ
/// وَحدَها ثُمَّ <c>AssignCounterpartyAsync(slug, id, userId, …)</c> — أَي أَنّ
/// <b>أَيَّ مُسَجَّلٍ في المَتجَرِ يَصيرُ الطَرَفَ المُقابِلَ لِأَيِّ صَفقَة</b>،
/// وهُوَ مَن «يُؤَكِّد» و«يَشحَن» ويُدفَعُ لَه نَقداً عِندَ الاستِلام. الشاشَةُ
/// كانَت تُخفي الزِرَّ عَن غَيرِ المالِك (<c>isListingOwner</c>) — و<c>curl</c> لا
/// يَرى الشاشَة. وفاحِصُ القاعِدَة ٦ يَقبَلُ <c>AuthSession.ResolveToken</c>
/// حارِساً فَلَم يَرَ الفَرق.</para>
///
/// <para><b>والقَديمُ يَبقى حَيثُ لا مَرجِعَ مالِك — بِقَرارٍ مَكتوبٍ لا
/// بِالسُقوط</b>: <c>listing_owner</c> يُلحَقُ في مَسارَي العَرضِ والسَلَّةِ
/// <b>فَقَط حينَ يَحمِلُ الإعلانُ <c>owner_id</c></b>
/// (<c>MarketplaceTemplateExtensions.cs</c> — <c>/listings/{id}/deal</c> و
/// <c>/checkout/submit</c>)، وكُلُّ إعلانٍ يُنشَأُ مِن <c>/listings/create</c>
/// يَحمِلُه (‏<c>:950</c>)؛ فَما يَخلو مِنه إعلاناتُ البَذرِ والعَيِّناتِ
/// القَديمَة. لِهذِه وَحدَها يُبقى السُلوكُ القَديم — مُسَمّىً في
/// <see cref="IsLegacyUnowned"/> — ويُسَجَّلُ في سِجِلِّ أَسئِلَةِ المالِك.</para>
///
/// <para><b>ولِماذا التَخويلُ يَسبِقُ فَحصَ الحالَة</b> (القاعِدَة ٦): لَو رُتِّبَ
/// العَكسُ لَصارَ «الصَفقَةُ مَقبولَةٌ أَصلاً» جَواباً يَتَسَرَّبُ إلى غَيرِ
/// المالِك. الغِيابُ وَحدَه يَسبِق، لِأَنَّه لا يُفشي شَيئاً.</para>
/// </summary>
public static class DealAcceptAuthorization
{
    /// <summary>لا صَفقَةَ بِهذا المُعَرِّفِ في هذا المُستَأجِر.</summary>
    public const string DealNotFound = "deal_not_found";

    /// <summary>الصَفقَةُ تَحمِلُ مَرجِعَ مالِكِ إعلانٍ، والفاعِلُ لَيسَ
    /// هُوَ.</summary>
    public const string ActorNotListingOwner = "actor_not_listing_owner";

    /// <summary>الفاعِلُ هُوَ مَن بادَرَ بِالعَرض — ولا يَكونُ المَرءُ
    /// طَرَفَي صَفقَتِه.</summary>
    public const string ActorIsInitiator = "actor_is_initiator";

    /// <summary>الصَفقَةُ خَرَجَت مِن <c>Active</c> — لا شَيءَ يُقبَل.</summary>
    public const string DealNotActive = "deal_not_active";

    /// <summary>لِلصَفقَةِ طَرَفٌ مُقابِلٌ أَصلاً — القَبولُ وَقَعَ مَرَّةً، ولا
    /// يَقَعُ ثانِيَة.</summary>
    public const string DealAlreadyAccepted = "deal_already_accepted";

    /// <summary>المَعجَمُ المُغلَق — خَمسَةٌ لا سادِسَ لَها.</summary>
    public static readonly IReadOnlyList<string> All =
        new[] { DealNotFound, ActorNotListingOwner, ActorIsInitiator, DealNotActive, DealAlreadyAccepted };

    public static bool Contains(string code) => All.Contains(code, StringComparer.Ordinal);

    /// <summary>يَرمي عِندَ الخَرق — لِمَواضِعِ التَركيب. نَفسُ حيلَةِ
    /// <c>DealCancelAuthorization.Require</c> حَرفاً.</summary>
    public static string Require(string code)
    {
        if (!Contains(code))
            throw new ArgumentException(
                $"الرَمز «{code}» خارِج مَعجَم DealAcceptAuthorization. " +
                $"المَعجَم: {string.Join("، ", All)}.", nameof(code));
        return code;
    }

    /// <summary><b>صَفقَةٌ بِلا مَرجِعِ مالِكِ إعلان</b> — الحالَةُ الوَحيدَةُ
    /// الَّتي يُبقى فيها السُلوكُ القَديم (أَيُّ مُسَجَّلٍ غَيرِ المُبادِرِ
    /// يَقبَل)، وهي حالَةُ إعلاناتِ البَذرِ والعَيِّناتِ لا إعلاناتِ
    /// المُستَخدِمين.</summary>
    public static bool IsLegacyUnowned(Deal deal) =>
        !deal.Refs.TryGetValue(DealsService.ListingOwnerRef, out var lo) || string.IsNullOrWhiteSpace(lo);

    /// <summary><c>null</c> يَعني مُخَوَّل. والتَرتيبُ مَقصود: الغِياب، ثُمَّ
    /// التَخويل (المالِك، ثُمَّ المُبادِر)، ثُمَّ الحالَة (فَعّالَة، ثُمَّ
    /// غَيرُ مَقبولَةٍ بَعد).</summary>
    public static DealAcceptViolation? Validate(Deal? deal, DealAcceptor by)
    {
        if (deal is null)
            return new(DealNotFound, "لا صَفقَةَ بِهذا المُعَرِّف.");

        if (!IsLegacyUnowned(deal) && deal.Refs[DealsService.ListingOwnerRef] != by.ActorId.ToString())
            return new(ActorNotListingOwner,
                "الفاعِلُ لَيسَ مالِكَ الإعلانِ الَّذي عُرِضَ عَلَيه — ومَن يَقبَلُ العَرضَ " +
                "يَصيرُ الطَرَفَ الَّذي يُؤَكِّدُ ويَشحَنُ ويُدفَعُ لَه، فَلا يَملِكُه غَيرُ المالِك.");

        if (deal.InitiatorId == by.ActorId)
            return new(ActorIsInitiator,
                "الفاعِلُ هُوَ مَن بادَرَ بِالعَرض — ولا يَكونُ المَرءُ طَرَفَي صَفقَتِه.");

        if (deal.Status != DealStatus.Active)
            return new(DealNotActive,
                $"الصَفقَةُ في حالَة {deal.Status} — لا شَيءَ يُقبَل.");

        if (deal.CounterpartyId is not null)
            return new(DealAlreadyAccepted,
                "لِلصَفقَةِ طَرَفٌ مُقابِلٌ أَصلاً — القَبولُ وَقَعَ مَرَّةً ولا يَقَعُ ثانِيَة.");

        return null;
    }

    public static bool IsAllowed(Deal? deal, DealAcceptor by) => Validate(deal, by) is null;
}

/// <summary>ناتِجُ القَبول — نَفسُ شَكلِ <c>DealCancelResult</c>: الرَفضُ يَصِلُ
/// المُنادي بِرَمزِه، لا بِقِراءَةِ الحالَةِ بَعدَ النِداء.</summary>
public sealed record DealAcceptResult(bool Ok, Deal? Deal, DealAcceptViolation? Violation)
{
    public static DealAcceptResult Refused(DealAcceptViolation v, Deal? deal = null)
        => new(false, deal, v);

    public static DealAcceptResult Accepted(Deal deal) => new(true, deal, null);
}
