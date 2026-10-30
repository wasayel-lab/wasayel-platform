namespace ACommerce.Templates.Customer.Marketplace.Services.Deals;

/// <summary>خَرق واحِد في تَخويلِ الإلغاء. <c>Code</c> مِفتاحٌ ثابِتٌ
/// لِلاختِبارات واللوغ، و<c>MessageAr</c> لِلمُراجِع البَشَريّ. نَفس
/// شَكل <c>DealPatternViolation</c> و<c>ApiKeyViolation</c> — القالِب
/// المَرجِعيّ في القاعِدَة ٤.</summary>
public sealed record DealCancelViolation(string Code, string MessageAr);

/// <summary>
/// <para><b>مَن يُلغي، وبِأَيِّ سُلطَة.</b> هذا هُوَ الحارِسُ نَفسُه
/// مَنقولاً إلى <b>تَوقيع</b> <c>DealsService.CancelAsync</c>
/// (القاعِدَة ٦): لا يُمكِن نِداءُ الإلغاء بِلا تَصريحٍ صَريحٍ عَن
/// السُلطَة، فَلا يوجَد مَسارٌ «يَنسى» الحارِسَ لِأَنّ المُتَرجِمَ
/// يَمنَعُه.</para>
///
/// <para><b>ولِماذا رايَةٌ لا دَورٌ نَصِّيّ</b>: «مُشرِفُ المَتجَر»
/// يُثبَت في ثَلاثَة مَواضِعَ مُختَلِفَة بِثَلاثِ آلِيّات
/// (<c>StudioOwnsAsync</c> لِلاستوديو، وجَلسَةُ المُستَخدِم لِلواجِهَة،
/// ووَثيقَةُ المِفتاح لِلـAPI). فَالخِدمَةُ لا تُعيدُ إثباتَ ما
/// أَثبَتَه الحارِسُ الأَعلى — تَطلُبُ مِنه أَن <b>يَقولَه</b>.
/// وذاكَ الفَرقُ بَينَ تَكرارِ مَنطِقٍ وبَينَ عَقدٍ مُعلَن.</para>
/// </summary>
/// <param name="ActorId">المُستَخدِم الَّذي يُنسَب إلَيه السَطر في
/// <c>Timeline</c>.</param>
/// <param name="ActorName">اسمُه كَما يُكتَب في السِجِلّ.</param>
/// <param name="IsStoreAdmin">أَثبَتَ الحارِسُ الأَعلى أَنَّه مُشرِفُ
/// المَتجَر. <b>الافتِراضُ لا يوجَد</b>: القيمَةُ إلزامِيَّة.</param>
public sealed record DealCanceller(Guid ActorId, string ActorName, bool IsStoreAdmin);

/// <summary>
/// <para><b>بَوّابَةُ الإلغاء — دالَّةٌ نَقِيَّة.</b> لا قاعِدَةَ
/// بَيانات، ولا وَقتَ، ولا عَشوائيَّة: صَفقَةٌ وفاعِلٌ، ورَمزُ خَرقٍ
/// مِن مَعجَمٍ مُغلَق أَو <c>null</c>.</para>
///
/// <para><b>الثَغرَةُ الَّتي أَغلَقَتها</b> (‏§١١٫٦ في
/// <c>docs/API-SURFACE-DESIGN.md</c>): كانَت
/// <c>DealsService.CancelAsync</c> <b>لا تَفحَصُ الفاعِلَ إطلاقاً</b> —
/// بِخِلاف <c>AdvanceAsync</c> — و<c>POST /{slug}/deals/{id}/cancel</c>
/// تَكتَفي بِجَلسَةٍ صالِحَة. أَي أَنّ <b>أَيَّ مُستَخدِمٍ في المَتجَر
/// يُلغي صَفقَةَ أَيّ أَحَد</b>. والإلغاءُ لَيسَ تَغييرَ حالَةٍ
/// وَحسب: <c>RefundAsync</c> تَقَع مَعَه — فَالثَغرَةُ تَمَسُّ المالَ
/// لا السِجِلَّ فَقَط.</para>
///
/// <para><b>ولِماذا التَخويلُ يَسبِقُ فَحصَ الحالَة</b> (القاعِدَة ٦):
/// لَو رُتِّبَ العَكسُ لَصارَ «الصَفقَةُ لَيسَت فَعّالَة» جَواباً
/// يَتَسَرَّب إلى غَيرِ الطَرَف — أَي أَنّ خَطَأَ التَحَقُّق يَصيرُ
/// قِناعاً لِلثَغرَة. الغِيابُ وَحدَه يَسبِق، لِأَنَّه لا يُفشي
/// شَيئاً.</para>
/// </summary>
public static class DealCancelAuthorization
{
    /// <summary>لا صَفقَةَ بِهذا المُعَرِّف في هذا المُستَأجِر.</summary>
    public const string DealNotFound = "deal_not_found";

    /// <summary>الفاعِلُ لَيسَ طَرَفاً في الصَفقَةِ ولا مُشرِفَ
    /// المَتجَر.</summary>
    public const string ActorNotParty = "actor_not_party";

    /// <summary>الصَفقَةُ خَرَجَت مِن <c>Active</c> — لا شَيءَ
    /// يُلغى.</summary>
    public const string DealNotActive = "deal_not_active";

    /// <summary>
    /// <para><b>الصَفقَةُ بَلَغَت مَرحَلَةً لا يُلغيها فيها طَرَف</b> (‏2026-10-24، تَدقيقُ الإطلاق lens-money-integrity-4؛ ‏ADR-033 §٦‑ج مُلحَقاً):
    /// مَرحَلَتُها خارِجَ <see cref="PartyCancelStages"/> — «شَحن» فَما بَعدَه — والفاعِلُ طَرَفٌ لا مُشرِفُ المَتجَر. كانَ طَرَفٌ يُلغي صَفقَةً
    /// شُحِنَت أَو سُلِّمَت: بَيعُ نَقدٍ قُبِضَ ثَمَنُه يَصيرُ «مُلغاة»، وشُحنَةُ OTO حَيَّة، و«استِرجاعُه مُستَحَقّ» عَلى بِضاعَةٍ سُلِّمَت
    /// (تَوصيفُه <c>818ae9e</c>). <b>افتِراضٌ قابِلٌ لِلقَلبِ</b> حَتّى يُقَرِّرَ المالِكُ المَراحِل (سِجِلُّ «ب» W8-1·٢).</para>
    /// </summary>
    public const string DealPastCancelStage = "deal_past_cancel_stage";

    /// <summary>
    /// <para><b>المَراحِلُ الَّتي يُلغي فيها طَرَفٌ صَفقَتَه — افتِراضُ ‏2026-10-24 لا قَرارُ مُنتَج</b> (سِجِلُّ «ب» W8-1·٢): ما قَبلَ أَن
    /// تَخرُجَ البِضاعَةُ أَو الخِدمَةُ مِن يَدِ البائِع — عَرض، حَجز، تَأكيد، دَفع. وما بَعدَها («شَحن»، «تَسليم»، «استِلام»، «تَقييم») يُلغيه
    /// صاحِبُ المَتجَرِ وَحدَه مِن الاستوديو (<see cref="DealCanceller.IsStoreAdmin"/>) — المَخرَجُ اليَدَوِيُّ لِلمُرتَجَعِ والنِزاع.</para>
    /// <para><b>ويُقلَبُ بِسَطر</b>: إضافَةُ مَرحَلَةٍ هُنا أَو حَذفُها؛ والصَفحَةُ تَتبَعُه بِلا لَمس (تَرسُمُ بِـ<see cref="IsAllowed"/>).</para>
    /// <para>(‏2026-10-29، قَرارُ المالِك د37 «أ» — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه): <b>صارَ قَراراً لا افتِراضاً</b> — «تبقى المراحل كما هي، وهذا هو الافتراض
    /// الذي تشحنه المنصّة»، ومَعَه: صَفقَةٌ لَها طَلَبٌ قائِمٌ عِندَ OTO (<see cref="DealShipping.HoldsLiveOrder"/>) تُعامَلُ كَالمَشحونَة. ولا إعدادَ لِلمَتجَرِ الآن (القاعِدَة ١):
    /// يَومَ يَطلُبُ أَوَّلُ مَتجَرٍ سِياسَةً أُخرى تَصيرُ المَراحِلُ اختِياراً مِن قائِمَةٍ مُغلَقَةٍ بِمُصادِقٍ ورُموزِ خَرق (القاعِدَة ٤)، وهذِه قيمَتُها الأولى.</para>
    /// </summary>
    /// <summary>
    /// <para><b>الصَفقَةُ في طَلَبِ شَحنٍ واحِدٍ حَيٍّ عِندَ OTO مَعَ أُختٍ لَم تُسَلَّم</b> (‏2026-10-30، تَدقيقُ الموجَة ١١ F1؛ تَوصيفُه <c>8029031</c>): الطَلَبُ يُنشَأُ
    /// لِصَفَقاتِ بائِعٍ مِن سَلَّةٍ واحِدَةٍ بِمَبلَغِ تَحصيلٍ هُوَ مَجموعُها (د13 «ب»)، والشُحنَةُ المُسَلَّمَةُ لِلمَندوبِ وَحدَةٌ واحِدَة — فَإلغاءُ إحداها وَحدَها يَترُكُ
    /// المَندوبَ يُحَصِّلُ ثَمَنَها، أَو يُميتُ شُحنَةَ أُختِها إن أَلغى البائِعُ الطَلَب. يَرُدُّه <see cref="ValidateWaybill"/> قَبلَ أَيِّ كِتابَة، والطَريقُ الَّذي يُفضي
    /// «إلغاءُ الطَلَبِ كامِلاً» (<c>DealsService.CancelWholeInvoiceAsync</c>) أَو الإلغاءُ بَعدَ التَسليم (فَيُكتَبُ <see cref="DealEvent.CashRefundDue"/>).</para>
    /// </summary>
    public const string DealInSharedWaybill = "deal_in_shared_waybill";

    public static readonly IReadOnlySet<DealStage> PartyCancelStages =
        new HashSet<DealStage> { DealStage.Offered, DealStage.Booked, DealStage.Confirmed, DealStage.Paid };

    /// <summary>المَعجَمُ المُغلَق — ثَلاثَةٌ لا رابِعَ لَها.
    /// (‏2026-10-24، تَدقيقُ الإطلاق lens-money-integrity-4 — ولا يُعادُ حَرفٌ فَوقَه: ورابِعُها <see cref="DealPastCancelStage"/>.)</summary>
    /// <remarks>(‏2026-10-30، تَدقيقُ الموجَة ١١ F1 — ولا يُعادُ حَرفٌ فَوقَه: وخامِسُها <see cref="DealInSharedWaybill"/>.)</remarks>
    public static readonly IReadOnlyList<string> All =
        new[] { DealNotFound, ActorNotParty, DealNotActive, DealPastCancelStage, DealInSharedWaybill };

    public static bool Contains(string code) => All.Contains(code, StringComparer.Ordinal);

    /// <summary>يَرمي عِندَ الخَرق — لِمَواضِعِ التَركيب. نَفسُ حيلَة
    /// <c>ApiScopeCatalog.Require</c> و<c>CapabilityCatalog.Require</c>
    /// حَرفاً.</summary>
    public static string Require(string code)
    {
        if (!Contains(code))
            throw new ArgumentException(
                $"الرَمز «{code}» خارِج مَعجَم DealCancelAuthorization. " +
                $"المَعجَم: {string.Join("، ", All)}.", nameof(code));
        return code;
    }

    /// <summary><c>null</c> يَعني مُخَوَّل. والتَرتيبُ مَقصود:
    /// الغِياب، ثُمَّ التَخويل، ثُمَّ الحالَة.
    /// (‏2026-10-24 — ولا يُعادُ حَرفٌ فَوقَه: ثُمَّ المَرحَلَةُ لِلطَرَف — <see cref="DealPastCancelStage"/> — بَعدَ الحالَة، فَلا تَتَسَرَّبُ مَرحَلَةٌ
    /// لِغَيرِ الطَرَف.)</summary>
    public static DealCancelViolation? Validate(Deal? deal, DealCanceller by)
    {
        if (deal is null)
            return new(DealNotFound, "لا صَفقَةَ بِهذا المُعَرِّف.");

        // (‏2026-10-29، قَرارُ المالِك د31 «١» — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه): **ومالِكُ الإعلانِ يَرفُضُ العَرضَ عَلَيه ما دامَ في «عَرض»** — «يُسمح لصاحب
        // الإعلان أن يرفض العرض ما دام في مرحلة «عرض»، فيظهر له زرّ الرفض تلقائيًّا، ويُخبَر المشتري». في مَسارِ العَرضِ لا رَبطَ عِندَ الوِلادَة، فَالمالِكُ
        // (`DealAcceptAuthorization.OwnerIdOf`) لَيسَ طَرَفاً حَتّى يَقبَل — وكانَ رَفضُه يُرَدُّ بِـ`actor_not_party` (تَوصيفُه
        // `DealVendorCancelTests.Today_an_unbound_listing_owner_cannot_cancel_an_offer_on_his_listing`). وتَعريفُ تَدَفُّقِ العَرضِ يُعلِنُ «Pending→Rejected»
        // بِفاعِلِه. وبَعدَ «عَرض» لا يَملِكُ إلّا ما يَملِكُه الطَرَف — والصَفحَةُ تَتبَعُ هذِه الدالَّةَ بِلا لَمس، والمُشتَري يُخبَرُ بِالأُنبوبِ القائِم.
        var rejectsOffer = deal.Stage == DealStage.Offered && DealAcceptAuthorization.OwnerIdOf(deal) == by.ActorId;
        if (!by.IsStoreAdmin &&
            deal.InitiatorId != by.ActorId &&
            deal.CounterpartyId != by.ActorId &&
            !rejectsOffer)
            return new(ActorNotParty,
                "الفاعِلُ لَيسَ طَرَفاً في هذِه الصَفقَة ولا مُشرِفَ المَتجَر — " +
                "والإلغاءُ يَستَرِدُّ المالَ، فَلا يَملِكُه غَيرُ أَطرافِها.");

        if (deal.Status != DealStatus.Active)
            return new(DealNotActive,
                $"الصَفقَةُ في حالَة {deal.Status} — لا شَيءَ يُلغى.");

        // (‏2026-10-29، قَرارُ المالِك د37 — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَ ما سَبَق): **وطَلَبٌ عِندَ OTO كَالشَحن** — «متى كان للصفقة طلب شحن
        // عند OTO تُعامَل كأنها شُحنت: لا يلغيها المشتري ولا البائع من صفحتها، ويلغيها صاحب المتجر من الاستوديو». يُقرَأُ مِن الصَفقَةِ نَفسِها
        // (`DealShipping.HoldsLiveOrder` عَلى `Refs`) فَتَبقى الدالَّةُ بِلا قاعِدَة؛ والشُحنَةُ يُلغيها البائِعُ مِن حِسابِه في OTO (لا نِداءَ إلغاءٍ لَدَيها)،
        // وطَلَبٌ كَتَبَت OTO أَنّها أَغلَقَته (`ClosedAtOto`) لا شُحنَةَ سائِرَةً لَه فَيَعودُ الحُكمُ إلى المَرحَلَة.
        // **والرِسالَةُ عامَّةٌ تَصدُقُ في كُلِّ نَمَط** (د37 (٣) «ب»): كانَت «بَعدَ الشَحنِ أَو التَسليمِ» — ولا «شَحنَ» في الرِحلَةِ والخِدمَةِ والإيجار.
        if (!by.IsStoreAdmin && (!PartyCancelStages.Contains(deal.Stage) || DealShipping.HoldsLiveOrder(deal)))
            return new(DealPastCancelStage,
                $"الصَفقَةُ عِندَ «{deal.Stage}»{(DealShipping.HoldsLiveOrder(deal) ? " ولَها طَلَبُ شَحنٍ قائِمٌ عِندَ OTO" : "")} — " +
                "تَجاوَزَت مَرحَلَةَ الإلغاءِ مِن صَفحَتِها؛ يُلغيها صاحِبُ المَتجَرِ وَحدَه مِن الاستوديو.");

        return null;
    }

    public static bool IsAllowed(Deal? deal, DealCanceller by) => Validate(deal, by) is null;

    // ─── الطَلَبُ الواحِدُ عِندَ OTO وَحدَةٌ واحِدَة (‏2026-10-30، تَدقيقُ الموجَة ١١ F1؛ قَرارُ «أ» F3·٢ بِأَفضَلِ المُمارَسات) ─────────────────────

    /// <summary>
    /// <para><b>أَتَسيرُ الصَفقَةُ في شُحنَةٍ لَم تُسَلَّم؟ — دالَّةٌ نَقِيَّة</b>: لَها طَلَبٌ قائِمٌ عِندَ OTO (<see cref="DealShipping.HoldsLiveOrder"/>) ولَم تَبلُغ «تَسليم».
    /// فَبَعدَ التَسليمِ قَبَضَ المَندوبُ وانتَهَت الشُحنَة — والإلغاءُ يَكتُبُ رَدَّ الثَمَنِ مُستَحَقّاً كَما كان.</para>
    /// </summary>
    public static bool OnLiveWaybill(Deal deal)
        => DealShipping.HoldsLiveOrder(deal) && !OtoOrderStatusGate.Reached(deal, DealStage.Delivered);

    /// <summary>
    /// <para><b>أَخَواتُ الصَفقَةِ في شُحنَتِها السائِرَة</b>: مِن <paramref name="orderSiblings"/> (صَفَقاتُ طَلَبِها كَما سَمَّتها أولاها) ما مَرجِعُ طَلَبِه طَلَبُها، وهي فَعّالَةٌ
    /// أَو في «نِزاع» (قَد تُستَأنَف — د38)، وتَسيرُ في الشُحنَةِ بَعد (<see cref="OnLiveWaybill"/>). وفارِغَةٌ لِصَفقَةٍ لا تَسيرُ في شُحنَة. دالَّةٌ نَقِيَّة.</para>
    /// </summary>
    public static IReadOnlyList<Deal> LiveOnWaybill(Deal deal, IReadOnlyList<Deal> orderSiblings)
    {
        if (!OnLiveWaybill(deal)) return Array.Empty<Deal>();
        var order = DealShipping.OrderIdOf(deal);
        return orderSiblings.Where(s => s.Id != deal.Id
                && string.Equals(s.Refs.GetValueOrDefault(DealShipping.OrderRef), order, StringComparison.Ordinal)
                && s.Status is DealStatus.Active or DealStatus.Disputed
                && OnLiveWaybill(s))
            .ToArray();
    }

    /// <summary>
    /// <para><b>بَوّابَةُ إلغاءِ صَفقَةٍ وَحدَها مِن شُحنَةٍ مُشتَرَكَة — دالَّةٌ نَقِيَّة</b> بِشَكلِ أُختِها (<c>GatewayInvoiceShare.Validate</c>): <see cref="DealInSharedWaybill"/>
    /// حينَ تَسيرُ الصَفقَةُ في شُحنَةٍ مَعَها أُختٌ واحِدَةٌ عَلى الأَقَلّ (<see cref="LiveOnWaybill"/>)، و<c>null</c> لِما سِواه. يَسأَلُها كاتِبُ الإلغاءِ لِلصَفقَةِ الواحِدَة
    /// (صاحِبُ المَتجَرِ وحَسمُ النِزاع — الطَرَفانِ مَردودانِ قَبلَها بِـ<see cref="DealPastCancelStage"/>)، وصَفحَةُ الاستوديو فَتَرسُمُ «إلغاء الطَّلَب كامِلاً» مَكانَ «إلغاء».</para>
    /// </summary>
    public static DealCancelViolation? ValidateWaybill(Deal deal, IReadOnlyList<Deal> orderSiblings)
        => LiveOnWaybill(deal, orderSiblings).Count > 0
            ? new(DealInSharedWaybill,
                "الصَفقَةُ في طَلَبِ شَحنٍ واحِدٍ عِندَ OTO مَعَ صَفَقاتٍ لَم تُسَلَّم، والمَندوبُ يُحَصِّلُ مَبلَغَها جَميعاً — لا تُلغى وَحدَها: " +
                "يُلغى الطَلَبُ كُلُّه، أَو تُلغى بَعدَ التَسليم.")
            : null;

    public static bool IsWaybillAllowed(Deal deal, IReadOnlyList<Deal> orderSiblings) => ValidateWaybill(deal, orderSiblings) is null;

    /// <summary>عَناوينُ الصَفَقاتِ كَما تُسَمّيها شَريحَةُ الشُحنَةِ في الاستوديو — «…»، «…» — مَحفوظَةً بِحَرفِها مِن السَلَّة.</summary>
    public static string TitlesOf(IEnumerable<Deal> deals) => string.Join("، ", deals.Select(d => $"«{d.ListingTitle}»"));
}

/// <summary>
/// <para>ناتِجُ الإلغاء. <b>ولِماذا لا <c>Deal?</c> كَما كانَ</b>:
/// المُنادي كانَ يَستَحيلُ عَلَيه التَفريقُ بَينَ «أُلغِيَت» و«لَم
/// تُلغَ لِأَنَّها غَيرُ فَعّالَة» إلّا بِقِراءَةِ الحالَةِ بَعدَ
/// النِداء — ورَفضٌ لا يَراهُ المُنادي رَفضٌ يُبتَلَع. نَفسُ شَكل
/// <c>DealAdvanceResult</c>.</para>
/// </summary>
public sealed record DealCancelResult(bool Ok, Deal? Deal, DealCancelViolation? Violation)
{
    public static DealCancelResult Refused(DealCancelViolation v, Deal? deal = null)
        => new(false, deal, v);

    public static DealCancelResult Cancelled(Deal deal) => new(true, deal, null);
}

/// <summary>
/// <para>ناتِجُ إلغاءِ الطَلَبِ كُلِّه (<c>DealsService.CancelWholeInvoiceAsync</c>، ‏2026-10-13، G5 M4): نَتيجَةُ الصَفقَةِ المَطلوبَة — أَو أَوَّلُ رَدٍّ
/// أَوقَفَ الإلغاء — و<see cref="Cancelled"/> كُلُّ صَفقَةٍ أُلغِيَت فِعلاً، لِيُخبَرَ طَرَفُ كُلٍّ مِنها.</para>
/// </summary>
public sealed record DealOrderCancelResult(DealCancelResult Result, IReadOnlyList<Deal> Cancelled);
