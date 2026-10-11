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
/// <para><b>ولا استِثناءَ لِصَفقَةٍ بِلا مَرجِعِ مالِك — تُغلَقُ لا تُفتَح</b>:
/// النُسخَةُ الأولى مِن هذا المِلَفّ (‏2d8cf5f، ‏2026-10-11) أَبقَت لَها
/// السُلوكَ القَديمَ (أَيُّ مُسَجَّلٍ غَيرِ المُبادِرِ يَقبَل) بِحُجَّةِ إعلاناتِ
/// البَذر. والمَقيسُ في المُراجَعَةِ نَفسِ اليَوم: <b>كُلُّ مَسارِ إنشاءِ
/// إعلانٍ يَكتُبُ <c>owner_id</c></b> (<c>MarketplaceTemplateExtensions.cs:950</c>،
/// <c>TestDataSeeder.cs:132</c>، <c>AppearanceBaselineSeeder.cs:254,274</c>)،
/// فَالاستِثناءُ لا يَحرُسُ مَساراً حَيّاً بَل وَثائِقَ قَديمَةً غَيرَ مَعدودَة —
/// وهُوَ عَينُ الثَغرَةِ مَفتوحَةً لَها، وخُروجٌ عَن ‏ADR-033 §٢‑ج‑أ («لِمالِكِ
/// الإعلانِ وَحدَه») بِلا ADR (القاعِدَة ١٥). فَصارَ الغِيابُ رَمزاً سادِساً
/// <see cref="ListingOwnerMissing"/>، والصَفقَةُ اليَتيمَةُ تُحَرَّكُ مِن المَسارِ
/// الإداريِّ وَحدَه (يُعَيِّنُ مالِكَ التَطبيقِ طَرَفاً — <c>:3332</c>). والشاشَةُ
/// كانَت أَصلاً لا تَرسُمُ زِرَّ القَبولِ لِهذِه (<c>isListingOwner</c> يَقرَأُ
/// المَرجِعَ نَفسَه) — فَالنُقطَةُ الآنَ تَقولُ ما تَقولُه الشاشَة.</para>
///
/// <para><b>ولِماذا التَخويلُ يَسبِقُ فَحصَ الحالَة</b> (القاعِدَة ٦): لَو رُتِّبَ
/// العَكسُ لَصارَ «الصَفقَةُ مَقبولَةٌ أَصلاً» جَواباً يَتَسَرَّبُ إلى غَيرِ
/// المالِك. الغِيابُ وَحدَه يَسبِق، لِأَنَّه لا يُفشي شَيئاً. وغِيابُ المَرجِعِ
/// يَسبِقُ هُوِيَّةَ الفاعِلِ لِأَنَّه صِفَةُ الصَفقَةِ لا الفاعِل: لا مالِكَ
/// يُقارَنُ بِه، فَلا مَعنى لِسُؤالِ «أَأَنتَ هُوَ».</para>
/// </summary>
public static class DealAcceptAuthorization
{
    /// <summary>لا صَفقَةَ بِهذا المُعَرِّفِ في هذا المُستَأجِر.</summary>
    public const string DealNotFound = "deal_not_found";

    /// <summary>الصَفقَةُ لا تَحمِلُ مَرجِعَ مالِكِ إعلانٍ أَصلاً (أَو تَحمِلُه
    /// فَراغاً أَو بِغَيرِ صيغَةِ مُعَرِّف) — فَلا أَحَدَ يَملِكُ قَبولَها مِن هُنا؛
    /// مَسارُها الإداريُّ وَحدَه.</summary>
    public const string ListingOwnerMissing = "listing_owner_missing";

    /// <summary>الصَفقَةُ تَحمِلُ مَرجِعَ مالِكِ إعلانٍ، والفاعِلُ لَيسَ
    /// هُوَ.</summary>
    public const string ActorNotListingOwner = "actor_not_listing_owner";

    /// <summary>الفاعِلُ هُوَ مَن بادَرَ بِالعَرض — ولا يَكونُ المَرءُ
    /// طَرَفَي صَفقَتِه.</summary>
    public const string ActorIsInitiator = "actor_is_initiator";

    /// <summary>الصَفقَةُ خَرَجَت مِن <c>Active</c> — لا شَيءَ يُقبَل.</summary>
    public const string DealNotActive = "deal_not_active";

    /// <summary>القَبولُ وَقَعَ مَرَّةً ولا يَقَعُ ثانِيَة: الصَفقَةُ تَجاوَزَت
    /// <c>Offered</c>، أَو لَها طَرَفٌ مُقابِلٌ غَيرُ القابِل. (كانَ المِعيارُ «لَها
    /// طَرَفٌ» وَحدَه — قَبلَ أَن يُربَطَ البائِعُ عِندَ الوِلادَة، ‏ADR-033 §٦.)</summary>
    public const string DealAlreadyAccepted = "deal_already_accepted";

    /// <summary>المَعجَمُ المُغلَق — سِتَّةٌ لا سابِعَ لَها. (خَمسَةٌ في
    /// ‏2d8cf5f؛ والسادِسُ <see cref="ListingOwnerMissing"/> بِقَرارٍ مَرئِيٍّ
    /// في المُراجَعَةِ — بَدَلَ استِثناءٍ كانَ يُبقي الثَغرَةَ لِلصَفقَةِ
    /// اليَتيمَة.)</summary>
    public static readonly IReadOnlyList<string> All =
        new[] { DealNotFound, ListingOwnerMissing, ActorNotListingOwner, ActorIsInitiator, DealNotActive, DealAlreadyAccepted };

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

    /// <summary>
    /// <para><b>هُوِيَّةُ مالِكِ الإعلانِ عَلى الصَفقَةِ بِقيمَةِ <c>Guid</c></b> — أَو
    /// <c>null</c> حَيثُ لا مَرجِعَ، أَو مَرجِعٌ لَيسَ مُعَرِّفاً. كانَت المُقارَنَةُ
    /// نَصّاً (<c>owner != by.ActorId.ToString()</c>) فَصيغَةُ <c>N</c> أَو الأَحرُفُ
    /// الكَبيرَةُ لِنَفسِ المُعَرِّفِ «لَيسَت المالِك» (مُراجَعَةُ ‏2026-10-11؛ ‏ADR-033
    /// §٦) وصارَت بِالقيمَة. <b>والشاشَةُ تَقرَأُ هذِه الدالَّةَ نَفسَها</b>
    /// (<c>MyDealDetail.isListingOwner</c>) و<c>DealsService.IsIncomingOfferFor</c>
    /// كَذلك — فَلا يَفتَرِقُ ما يُرسَمُ عَمّا يُقبَل: حَقيقَةٌ واحِدَةٌ لِلشاشَةِ
    /// والنُقطَةِ والقائِمَة.</para>
    /// </summary>
    public static Guid? OwnerIdOf(Deal deal)
        => deal.Refs.TryGetValue(DealsService.ListingOwnerRef, out var owner) && Guid.TryParse(owner, out var id)
            ? id : null;

    /// <summary><c>null</c> يَعني مُخَوَّل. والتَرتيبُ مَقصود: الغِياب، ثُمَّ
    /// التَخويل (مَرجِعُ المالِكِ مَوجودٌ ومُعَرِّفٌ، ثُمَّ هُوَ الفاعِل، ثُمَّ لَيسَ
    /// المُبادِر)، ثُمَّ الحالَة (فَعّالَة، ثُمَّ غَيرُ مَقبولَةٍ بَعد). ومَرجِعٌ
    /// فارِغٌ أَو لَيسَ مُعَرِّفاً كَغِيابِه — لا يُقرَأُ فَراغٌ مالِكاً.</summary>
    public static DealAcceptViolation? Validate(Deal? deal, DealAcceptor by)
    {
        if (deal is null)
            return new(DealNotFound, "لا صَفقَةَ بِهذا المُعَرِّف.");

        var ownerId = OwnerIdOf(deal);
        if (ownerId is null)
            return new(ListingOwnerMissing,
                "الصَفقَةُ لا تَحمِلُ مَرجِعَ مالِكِ إعلان (أَو تَحمِلُه بِغَيرِ صيغَةِ مُعَرِّف) — " +
                "فَلا أَحَدَ يَملِكُ قَبولَها مِن هُنا؛ تُحَرَّكُ مِن المَسارِ الإداريِّ لِمالِكِ التَطبيقِ وَحدَه.");

        if (ownerId != by.ActorId)
            return new(ActorNotListingOwner,
                "الفاعِلُ لَيسَ مالِكَ الإعلانِ الَّذي عُرِضَ عَلَيه — ومَن يَقبَلُ العَرضَ " +
                "يَصيرُ الطَرَفَ الَّذي يُؤَكِّدُ ويَشحَنُ ويُدفَعُ لَه، فَلا يَملِكُه غَيرُ المالِك.");

        if (deal.InitiatorId == by.ActorId)
            return new(ActorIsInitiator,
                "الفاعِلُ هُوَ مَن بادَرَ بِالعَرض — ولا يَكونُ المَرءُ طَرَفَي صَفقَتِه.");

        if (deal.Status != DealStatus.Active)
            return new(DealNotActive,
                $"الصَفقَةُ في حالَة {deal.Status} — لا شَيءَ يُقبَل.");

        // «قُبِلَت أَصلاً» — بِمِعيارَينِ لا واحِد (‏ADR-033 §٦، ‏2026-10-11): الصَفقَةُ
        // تَجاوَزَت `Offered` (فَالقَبولُ هُوَ ما يُخرِجُها مِنها، ولا يَقَعُ ثانِيَة)،
        // أَو طَرَفُها المُقابِلُ غَيرُ القابِلِ (المَسارُ الإداريُّ عَيَّنَ مالِكَ
        // التَطبيق). أَمّا صَفقَةُ السَلَّةِ المَربوطَةُ بِمالِكِ إعلانِها عِندَ الوِلادَةِ
        // فَطَرَفُها هُوَ القابِلُ نَفسُه وهي في `Offered` — فَتَمُرُّ لِيَقبَلَها. وكانَ
        // المِعيارُ «لَها طَرَفٌ» وَحدَه، يَومَ كانَ الطَرَفُ لا يُكتَبُ إلّا بِالقَبول.
        if (deal.Stage != DealStage.Offered || (deal.CounterpartyId is { } other && other != by.ActorId))
            return new(DealAlreadyAccepted,
                "القَبولُ وَقَعَ مَرَّةً ولا يَقَعُ ثانِيَة — الصَفقَةُ تَجاوَزَت مَرحَلَةَ العَرضِ، " +
                "أَو لَها طَرَفٌ مُقابِلٌ غَيرُك.");

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
