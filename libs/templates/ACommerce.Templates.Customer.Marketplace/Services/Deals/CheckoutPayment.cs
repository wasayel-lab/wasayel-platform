namespace ACommerce.Templates.Customer.Marketplace.Services.Deals;

// ═══ طَريقَةُ الدَفعِ عِندَ إتمامِ الشِراء — مَعجَمٌ مُغلَقٌ بِتَعريفٍ واحِد ══
//
// **العِلَّةُ المَقيسَة (‏2026-08-30)**: جِسمُ
// `POST /{slug}/checkout/submit` كانَ يَشتَرِط `pay != «cod»` قَبلَ
// نِداءِ مُزَوِّدِ الدَفع، **والاستِمارَةُ تُرسِل `card` أَو `cash`**
// (‏`CheckoutPage.razor` السَطران ‏92 و‏99). والرَمزُ «cod» وَرَدَ
// **مَرَّةً واحِدَةً في المُستَودَعِ كُلِّه**: في ذلكَ الشَرطِ نَفسِه.
// (وهُوَ مَكتوبٌ هُنا بِعَلامَتَي اقتِباسٍ عَرَبِيَّتَينِ عَمداً:
// الفاحِصُ يَمسَحُ التَعليقاتِ أَيضاً، وسِجِلُّ العِلَّةِ لا يُعيدُ
// زَرعَ الرَمزِ الَّذي يَمنَعُه.)
// فَالشَرطُ **لا يَكذِبُ أَبَداً** — أَي أَنّ كُلَّ طَلَبٍ يَستَدعي
// `AuthorizeAsync`، **والدَفعُ عِندَ الاستِلامِ أَوَّلُها**؛ وفي الإنتاجِ
// يَرُدُّ `UnavailablePaymentProvider` بِـ`Failed` فَيُعَلَّق
// `payment_status = Failed` عَلى صَفقَةِ نَقدٍ عِندَ الباب.
//
// **وهذا هُوَ بِعَينِه الشَكلُ الَّذي كَتَبَه `StudioUpgradeReason`
// بَعدَ كَلفَتِه**: مَعجَمٌ مُغلَقٌ مَكتوبٌ حَرفِيّاً في طَرَفَينِ
// يَنجَرِف، وخَطَأُ حَرفٍ في أَحَدِهِما يَجعَلُ الشَرطَ صامِتاً. فَلا
// حَرفِيَّةَ بَعدَ اليَوم: الاستِمارَةُ والنُقطَةُ تَقرَآنِ **هذا
// المِلَفَّ** (القاعِدَة ٨: لا مَعجَمَ رابِع).
//
// **ولا أُنبوبَ ثانٍ لِلقَرار**: `PlanPurchasePolicy` تَحرُسُ **باقَةً
// يَشتَريها مُستَخدِمُ المَتجَر**، وهذِه تَحرُسُ **عَرَبونَ صَفقَةٍ
// يَدفَعُه مُشتَرٍ**. تَدَفُّقانِ يَفصِلُهُما ‏ADR-009 بِالبِناء، ولِكُلٍّ
// مُدخَلُه: تِلكَ تَقرَأُ `Plan.Price`، وهذِه تَقرَأُ اختِيارَ المُشتَري.
// والمُشتَرَكُ بَينَهُما مُدخَلٌ واحِدٌ هُوَ
// `Tenant.PaymentProviderConfigured` — **ودَلالَتُه واحِدَة**: «هذا
// المَتجَرُ يَقبِض».
//
// **ومُدخَلٌ ثانٍ مُنذُ ‏2026-10-11 (‏ADR-032 §٢‑د)**: «هذا المَتجَرُ
// يَقبِض» كانَ يُجابُ مِن رابِطٍ مُستَضافٍ لَصَقَه المُستَأجِرُ وَحدَه،
// بَينَما البِطاقَةُ تُنادي `IPaymentProvider` **العامَّ** — وهُوَ في
// الإنتاجِ `UnavailablePaymentProvider` يَرُدُّ `Failed` لِكُلِّ حَجز. فَكانَ
// يُعرَضُ عَلى المُشتَري بابٌ يُفضي إلى فَشَلٍ مُؤَكَّد (موجَزُ الفَجَوات
// P0-1). فَصارَ القَرارُ يَطلُبُ الطَرَفَين: المَتجَرُ يَقبِض **ومُزَوِّدُ
// النُسخَةِ يَستَطيعُ القَبضَ فِعلاً** (`PaymentChargeSurface.CanCharge`).
// وإلى أَن يوجَدَ مُزَوِّدُ مَفاتيحٍ لِلمُستَأجِر، لا تُعرَضُ البِطاقَةُ
// في الإنتاجِ لِمَن لَصَقَ رابِطاً — ولا «قَريباً».
//
// **ووُجِدَ (‏2026-10-12، ‏ADR-032 الخُطوَة 8 — ولا يُعادُ حَرفٌ فَوقَ ما سَبَق)**: مَتجَرٌ رَبَطَ مِفتاحَ بَوّابَتِه مَختوماً تُعرَضُ لَه البِطاقَةُ
// بِه — `CheckoutCardFacts` أَدناه، مُدخَلاتُها تُحسَبُ مَرَّةً ويَقرَؤُها الطَرَفان — ومَن لَصَقَ رابِطاً وَحدَه عَلى ما كان.

/// <summary>
/// <para><b>طُرُقُ الدَفعِ المَعروضَةُ في إتمامِ الشِراء</b> — قيمَتانِ
/// لا ثالِثَة، وهُما بِحَرفِهِما ما تُرسِلُه الاستِمارَةُ اليَوم.</para>
/// </summary>
public static class CheckoutPayMethods
{
    /// <summary>بِطاقَة — <b>وهي وَحدَها الَّتي تَستَدعي
    /// المُزَوِّد</b>.</summary>
    public const string Card = "card";

    /// <summary>نَقدٌ عِندَ الاستِلام — <b>لا يَمُرُّ بِمُزَوِّدٍ
    /// إطلاقاً</b>. وكانَ يَمُرّ.</summary>
    public const string CashOnDelivery = "cash";

    /// <summary><b>مِفتاحُ طَريقَةِ الدَفعِ في <c>Deal.Attributes</c></b> — يَكتُبُه
    /// <c>POST /{slug}/checkout/submit</c> بِهذا الثابِتِ نَفسِه (كانَ بِحَرفِيَّةٍ تُطابِقُه — صارَ الثابِتَ
    /// ‏2026-10-11؛ مُثَبَّتٌ في <c>DealCashPolicyTests</c>) ويَقرَؤُه <c>DealsPolicy</c>
    /// لِيُسقِطَ مَرحَلَةَ <c>Paid</c> عَن النَقد (‏ADR-033 §٦). الكاتِبُ والقارِئُ
    /// يَقرَآنِ نَفسَ الحَرف — نَسَقُ <c>DealsService.ListingOwnerRef</c>.</summary>
    public const string AttributeKey = "pay_method";

    public static readonly IReadOnlyList<string> All = new[] { Card, CashOnDelivery };

    /// <summary>رَمزُ الخَرقِ حينَ تُطلَبُ البِطاقَةُ في مَتجَرٍ لا
    /// يَقبِض — <b>يُقرَأُ في الشاشَةِ لِيُختارَ نَصُّ القامُوس</b>،
    /// فَلا رَفضَ مُبتلَع.</summary>
    public const string CardUnavailable = "pay_card_unavailable";

    /// <summary>رَمزُ الرَدِّ حينَ تَعَذَّرَ فَتحُ فاتورَةِ بَوّابَةِ المَتجَرِ بَعدَ أَن وُلِدَت الصَفَقات (‏ADR-032 الخُطوَة 10، ‏2026-10-12):
    /// أَلغَتها الخِدمَةُ بِاسمِ المُشتَري ولَم يُخصَم شَيء، والسَلَّةُ كَما هي. <b>يُقرَأُ في الشاشَةِ لِيُختارَ نَصُّ القامُوس</b>؛ ورَمزُ
    /// البَوّابَةِ نَفسُه في اللوغِ لِلمُراجَعَة.</summary>
    public const string GatewayOpenFailed = "pay_gateway_open_failed";

    /// <summary>رَمزُ البِطاقَةِ عَلى طَريقِ بَوّابَةِ المَتجَرِ لِسَلَّةٍ صَفَقاتُها أَكثَرُ مِمّا تَحمِلُه فاتورَةٌ واحِدَة
    /// (<see cref="GatewayPaymentProof.MaxDealsPerInvoice"/>) — يُقرَّرُ قَبلَ أَن تولَدَ صَفقَة (‏2026-10-13، G5 (xvi-g)؛ <see cref="CheckoutCardFacts.ForCart"/>).</summary>
    public const string CardCartTooMany = "pay_card_cart_too_many";

    /// <summary>رَمزُ البِطاقَةِ عَلى طَريقِ بَوّابَةِ المَتجَرِ لِسَلَّةٍ مَجموعُها لا يَصيرُ مَبلَغَ فاتورَة — دونَ <see cref="ACommerce.Kit.Payments.MoyasarClient.MinimumAmount"/>
    /// هَلَلَةً أَو بِكَسرِ هَلَلَة (<see cref="ACommerce.Kit.Payments.MoyasarClient.ToSmallestUnit"/>) — يُقرَّرُ قَبلَ أَن تولَدَ صَفقَة (‏2026-10-13، G5 (xvi-g)).</summary>
    public const string CardAmountUnbillable = "pay_card_amount_unbillable";

    /// <summary>رَمزُ البِطاقَةِ عَلى طَريقِ بَوّابَةِ المَتجَرِ في مَتجَرٍ نَمَطُ صَفَقاتِه بِلا مَرحَلَةِ «دَفع» (‏«trip» مِن دَورٍ انجِذابُه إلَيه) — لا حارِسَ
    /// يُمسِكُ صَفقَتَه حَتّى يَثبُتَ دَفعُ فاتورَتِها، فَلا تُعرَضُ (‏2026-10-13، G5 (xvii-g)؛ <see cref="CheckoutCardFacts.ForPattern"/>).</summary>
    public const string CardPatternUnpaid = "pay_card_pattern_unpaid";

    public static bool Contains(string? value)
        => value is not null && All.Contains(value, StringComparer.Ordinal);
}

/// <summary>
/// <para><b>مُدخَلاتُ قَرارِ البِطاقَةِ الخَمسَة — تُحسَبُ مَرَّةً، والقَرارُ نَقِيٌّ هُنا</b> (‏ADR-032 الخُطوَة 8 — شَطرُ <c>CardOffered</c>، تَيّارُ S9،
/// ‏2026-10-12؛ القاعِدَة ٧): يَحسُبُها <c>GatewayPayments.CardFactsAsync</c> مِن المُخَزَّنِ مَرَّةً لِكُلِّ طَلَب، ويَقرَؤُها طَرَفا إتمامِ الشِراءِ
/// — الشاشَةُ تَرسُمُ بِها البِطاقَةَ والنُقطَةُ تَقبَلُ بِها وتَختارُ الطَريق — فَلا يَعرِضُ أَحَدُهُما ما يَرُدُّه الآخَر.</para>
///
/// <para><b>البِطاقَةُ تُعرَضُ بِمِفتاحِ المَتجَرِ حينَ تَجتَمِعُ الثَلاثَة</b>: (أ) <see cref="SealedKeyBound"/> رَبطُ دَفعٍ بِمِفتاحٍ سِرِّيٍّ مَختوم،
/// (ب) <see cref="VaultCanUnseal"/> خِزانَةٌ تَفُكّ، (ج) <see cref="GatewayClientRegistered"/> عَميلُ البَوّابَةِ مُسَجَّل — <b>في كُلِّ بيئَة</b>،
/// ومُزَوِّدُ النُسخَةِ لا يُسأَلُ لَها: مَتجَرٌ رَبَطَ مِفتاحَه لا يَقبِضُ بِغَيرِه أَبَداً (لا مُحاكيَ التَطويرِ ولا وَضعَ التَجرِبَة). وإن غابَت
/// (ب) أَو (ج) لَم تُعرَض — ولا تَسقُطُ إلى مُزَوِّدِ النُسخَة.</para>
///
/// <para><b>ومَتجَرٌ بِلا مِفتاحٍ عَلى ما كانَ بِحَرفِه</b> (‏ADR-032 §٢‑د، الموجَةُ الأولى): عَلَمُ المَتجَرِ ويَقبِضُ مُزَوِّدُ النُسخَةِ فِعلاً —
/// <b>وفي الإنتاجِ لا يَقبِضُ</b> (<c>UnavailablePaymentProvider</c>) فَلا بِطاقَةَ لَه أَبَداً؛ ويَبقى في التَطويرِ ووَضعِ التَجرِبَةِ (‏ADR-025)
/// كَما كان، وهُوَ ما يُثَبِّتُه صَفُّ الموجَةِ الأولى <c>A_pasted_link_alone_no_longer_offers_a_card_when_the_production_provider_cannot_charge</c>
/// بِلا تَعديلِ حَرف. فَفي الإنتاجِ: البِطاقَةُ ⇔ (أ) ∧ (ب) ∧ (ج) — مَقيسٌ في <c>CheckoutCardFactsTests</c>.</para>
///
/// <para><b>ولا قَرارَ ثانٍ</b>: <see cref="CardOffered"/> هِيَ <see cref="CheckoutPaymentPolicy.CardOffered"/> القائِمَةُ بِمُدخَلَيها — «يَقبِضُ
/// المَتجَر» و«يَستَطيعُ القَبضَ مَن سَيُنادى» — والطَرَفانِ يُمَرِّرانِ <see cref="Collects"/> و<see cref="ChargerCanCharge"/> إلى <c>Decide</c>.</para>
/// </summary>
public sealed record CheckoutCardFacts(
    bool StoreCollects, bool SealedKeyBound, bool VaultCanUnseal, bool GatewayClientRegistered, bool InstanceCanCharge)
{
    /// <summary>
    /// <para><b>ما يَمنَعُ هذِه السَلَّةَ مِن فاتورَةٍ واحِدَة — يُقرَّرُ قَبلَ أَن تولَدَ صَفقَة</b> (‏2026-10-13، مُراجَعَةُ بَوّابَةِ المَوجَة 3b، G5 (xvi-g)؛
    /// تَوصيفُ «اليَوم» <c>a135d25</c>): <see cref="CheckoutPayMethods.CardCartTooMany"/> أَو <see cref="CheckoutPayMethods.CardAmountUnbillable"/>،
    /// و<c>null</c> حينَ تَصلُح — أَو حينَ لا يَمُرُّ الطَريقُ بِالبَوّابَة (حَجزُ مُزَوِّدِ النُسخَةِ لِكُلِّ صَفقَةٍ وَحدَها، فَلا حَدَّ فاتورَة). كانَت البِطاقَةُ
    /// تُرسَمُ لِسَلَّةٍ لا تُفَوتَرُ أَبَداً، فَيولَدُ كُلُّ بَندٍ صَفقَةً مَربوطَةً بِبائِعِها ثُمَّ يُلغيها فَتحُ الفاتورَة — والبائِعُ لا يُخبَر.</para>
    /// <para>تَسأَلُه <see cref="ChargerCanCharge"/> فَيَرُدُّ <see cref="CheckoutPaymentPolicy.Decide"/> القائِمَةُ البِطاقَةَ بِمُدخَلَيها كَما هِيَ، ويَقرَؤُه
    /// الطَرَفانِ لِلنَصّ: مَسارُ الرَدِّ في النُقطَةِ (<c>card.CartRefusal ?? payRefusal</c>) وسَطرُ الشاشَةِ مَكانَ البِطاقَة.
    /// (‏2026-10-13، G5 (xvii-g): والقارِئانِ يَقرَآنِ <see cref="GatewayRefusal"/> — هذا والنَمَطُ مَعاً.)</para>
    /// </summary>
    public string? CartRefusal { get; init; }

    /// <summary>الحَقائِقُ نَفسُها ومَعَها <see cref="CartRefusal"/> لِسَلَّةِ المُشتَري — بِمُدخَلَي الفاتورَةِ نَفسَيهِما: عَدَدُ البُنودِ (صَفقَةٌ لِكُلِّ بَند)
    /// ومَجموعُها (<c>Cart.SubtotalSar</c> = مَجموعُ مَبالِغِ الصَفَقات). سَلَّةٌ غائِبَةٌ أَو فارِغَةٌ لا تُسأَل — لا شَيءَ يولَد.</summary>
    /// <summary>
    /// <para><b>نَمَطُ صَفَقاتِ المَتجَرِ بِلا «دَفع» يَرُدُّ بِطاقَةَ البَوّابَة</b> (‏2026-10-13، مُراجَعَةُ بَوّابَةِ المَوجَة 3b الثانِيَة، G5 (xvii-g)؛ تَوصيفُ
    /// «اليَوم» <c>0473a70</c>): <see cref="CheckoutPayMethods.CardPatternUnpaid"/> لِطَريقِ البَوّابَةِ وَحدَه حينَ لا يَحمِلُ النَمَطُ <c>Paid</c> — كانَت
    /// صَفقَةُ «trip» تولَدُ عَلى فاتورَةٍ و<see cref="GatewayPayments.AwaitsPayment"/> (تاليها «دَفع») لا يَصدُقُ أَبَداً، فَتُسَلَّمُ وتُقَيَّمُ بِلا مال.</para>
    /// </summary>
    public string? PatternRefusal { get; init; }

    /// <summary>ما يَمنَعُ بِطاقَةَ البَوّابَةِ في هذا الإتمام — النَمَطُ قَبلَ السَلَّة (مَتجَرٌ بِلا «دَفع» يَرُدُّ كُلَّ سَلَّة) — و<c>null</c> حينَ لا مانِع.
    /// تَسأَلُه <see cref="ChargerCanCharge"/>، ويَقرَؤُه الطَرَفانِ لِلنَصّ.</summary>
    public string? GatewayRefusal => PatternRefusal ?? CartRefusal;

    /// <summary>الحَقائِقُ نَفسُها ومَعَها <see cref="PatternRefusal"/> لِنَمَطِ صَفَقاتِ المَتجَر (<c>PatternFromTenant</c>)، بِسُؤالِ الكاتالوجِ نَفسِه
    /// (<see cref="DealPatternCatalog.For"/>). نَمَطٌ غائِبٌ لا يُسأَل.</summary>
    public CheckoutCardFacts ForPattern(string? pattern)
        => pattern is not null && ThroughGateway
            ? this with { PatternRefusal = DealPatternCatalog.For(pattern).Includes(DealStage.Paid) ? null : CheckoutPayMethods.CardPatternUnpaid }
            : this;

    public CheckoutCardFacts ForCart(ACommerce.Kit.Cart.Cart? cart)
        => cart is { Items.Count: > 0 } && ThroughGateway
            ? this with
            {
                CartRefusal = !GatewayPaymentProof.FitsOneInvoice(cart.Items.Count) ? CheckoutPayMethods.CardCartTooMany
                            : ACommerce.Kit.Payments.MoyasarClient.ToSmallestUnit(cart.SubtotalSar) is null ? CheckoutPayMethods.CardAmountUnbillable
                            : null,
            }
            : this;

    /// <summary><b>البِطاقَةُ تَمُرُّ بِبَوّابَةِ المَتجَرِ بِمِفتاحِه</b> — كُلَّما رُبِطَ مِفتاحٌ مَختوم. تَقرَؤُه النُقطَةُ فَتَفتَحُ الفاتورَةَ ولا تَحجِزُ
    /// عِندَ مُزَوِّدِ النُسخَة، والشاشَةُ فَتَقولُ أَينَ يُدفَعُ ولا تَرسُمُ شارَةَ تَجرِبَةٍ لا تَعنيه.</summary>
    public bool ThroughGateway => SealedKeyBound;

    /// <summary>«يَقبِضُ هذا المَتجَر» — بِمِفتاحٍ مَربوطٍ أَو بِعَلَمِه (<c>Tenant.PaymentProviderConfigured</c>). والمِفتاحُ يُقرَأُ مِن الرَبطِ
    /// الحَيِّ لا مِن العَلَمِ المُخَزَّن: رَبطٌ خُتِمَ قَبلَ أَن يَعُدَّه <c>CollectsMoney</c> لا يَنتَظِرُ مُزامَنَةَ العَلَم.</summary>
    public bool Collects => SealedKeyBound || StoreCollects;

    /// <summary>«يَستَطيعُ القَبضَ مَن سَيُنادى» — بَوّابَةُ المَتجَرِ بِخِزانَةٍ تَفُكُّ وعَميلٍ مُسَجَّل، أَو مُزَوِّدُ النُسخَةِ لِمَتجَرٍ بِلا مِفتاح.</summary>
    /// <remarks>وبِالبَوّابَةِ سَلَّةٌ تَحمِلُها فاتورَةٌ واحِدَة (<see cref="CartRefusal"/>، ‏2026-10-13).</remarks>
    /// <remarks>(‏2026-10-13، G5 (xvii-g) — ولا يُعادُ حَرفٌ فَوقَ ما سَبَق: ونَمَطٌ بِـ«دَفع» — <see cref="GatewayRefusal"/> يَجمَعُ المانِعَين.)</remarks>
    public bool ChargerCanCharge => SealedKeyBound ? VaultCanUnseal && GatewayClientRegistered && GatewayRefusal is null : InstanceCanCharge;

    /// <summary>القَرار — بِالدالَّةِ القائِمَةِ نَفسِها.</summary>
    public bool CardOffered => CheckoutPaymentPolicy.CardOffered(Collects, ChargerCanCharge);
}

/// <summary>
/// <para><b>قَرارُ طَريقَةِ الدَفع — دَوالُّ نَقِيَّة.</b> لا Marten،
/// ولا HTTP، ولا وَقت: تُقاسُ بِجَدوَلٍ بِلا قاعِدَةِ بَيانات.</para>
/// </summary>
public static class CheckoutPaymentPolicy
{
    /// <summary>
    /// <para><b>أَتُعرَضُ البِطاقَةُ أَصلاً؟</b> — <c>true</c> فَقَط
    /// حينَ يَقبِضُ هذا المَتجَر <b>ويَستَطيعُ مُزَوِّدُ النُسخَةِ
    /// القَبضَ فِعلاً</b>.</para>
    ///
    /// <para><b>والشاشَةُ والنُقطَةُ تَقرَآنِ هذِه الدالَّةَ
    /// نَفسَها</b> — سابِقَةُ <c>Plans.razor</c> و
    /// <c>PlanPurchasePolicy.Visible</c> حَرفاً: فَلا تَعرِضُ الشاشَةُ
    /// خِياراً سَتَرُدُّه النُقطَة، ولا يُرسَمُ زِرٌّ يَقودُ إلى فَشَلٍ
    /// مُبتلَع (القاعِدَة ١٢).</para>
    ///
    /// <para><b>ولِماذا وَسيطانِ لا واحِد</b> (‏ADR-032 §٢‑د): العَلَمُ
    /// الأَوَّلُ رابِطٌ لَصَقَه المُستَأجِر، والثاني قُدرَةُ المُزَوِّدِ
    /// العامِّ الَّذي سَتُنادِيه البِطاقَةُ فِعلاً — وفي الإنتاجِ هُوَ
    /// الفَشَلُ المُغلَق. ولا حِملَ زائِداً بِوَسيطٍ واحِد: دالَّةٌ
    /// تُجيبُ «نَعَم» بِلا أَن تَسأَلَ المُزَوِّدَ هي البابُ الَّذي
    /// أُغلِقَ هُنا.</para>
    /// </summary>
    public static bool CardOffered(bool paymentProviderConfigured, bool providerCanCharge)
        => paymentProviderConfigured && providerCanCharge;

    /// <summary>
    /// <para><b>ما اختارَهُ المُشتَري مُطَبَّعاً</b> — قيمَةٌ خارِجَ
    /// المَعجَمِ تُقرَأُ نَقداً عِندَ الاستِلام، لِأَنَّها **الجَوابُ
    /// الَّذي لا يُحَرِّكُ مالاً**. الفَشَلُ مُغلَقٌ نَحوَ عَدَمِ
    /// القَبض، لا نَحوَه.</para>
    /// </summary>
    public static string Normalize(string? raw)
    {
        var v = raw?.Trim();
        return string.Equals(v, CheckoutPayMethods.Card, StringComparison.Ordinal)
            ? CheckoutPayMethods.Card
            : CheckoutPayMethods.CashOnDelivery;
    }

    /// <summary>
    /// <para><b>القَرار — بِثَلاثَةِ مَخارِجَ لا اثنَين.</b></para>
    /// <list type="bullet">
    ///   <item><c>Refusal</c> غَيرُ فارِغ ⇒ البِطاقَةُ طُلِبَت ولا
    ///   يَقبِضُ المَتجَر: <b>يُرَدُّ الطَلَبُ ولا يُبَدَّلُ
    ///   صامِتاً</b>. وتَحويلُ طَلَبِ بِطاقَةٍ إلى نَقدٍ بِلا كَلِمَةٍ
    ///   تَبديلُ عَقدٍ مِن تَحتِ المُشتَري.</item>
    ///   <item><c>Method == Card</c> ⇒ يُنادى المُزَوِّد.</item>
    ///   <item>وإلّا ⇒ نَقدٌ عِندَ الاستِلام، <b>ولا نِداءَ
    ///   إطلاقاً</b>.</item>
    /// </list>
    /// </summary>
    public static (string Method, string? Refusal) Decide(
        string? requested, bool paymentProviderConfigured, bool providerCanCharge)
    {
        var method = Normalize(requested);

        if (method == CheckoutPayMethods.Card && !CardOffered(paymentProviderConfigured, providerCanCharge))
            return (CheckoutPayMethods.CashOnDelivery, CheckoutPayMethods.CardUnavailable);

        return (method, null);
    }

    /// <summary><b>أَيُنادى مُزَوِّدُ الدَفعِ لِهذِه الطَريقَة؟</b> —
    /// المَوضِعُ الوَحيدُ الَّذي يُجيب. وكانَ الجَوابُ <c>true</c>
    /// دائِماً.</summary>
    public static bool CallsProvider(string method)
        => string.Equals(method, CheckoutPayMethods.Card, StringComparison.Ordinal);

    /// <summary>
    /// <para><b>مَسارُ العَودَةِ عِندَ الرَفض</b> — دالَّةٌ نَقِيَّةٌ
    /// تُعطي المَسارَ النِسبِيَّ لِخُطوَةِ الدَفعِ حامِلاً ما مَلَأَه
    /// المُشتَري، فَلا يُعيد كِتابَةَ عُنوانِه.</para>
    ///
    /// <para><b>ولِماذا هُنا لا في جِسمِ النُقطَة</b>: بِناءُ عُنوانٍ
    /// بِأَربَعَةِ مُعامِلاتٍ حِسابٌ يُختَبَر، وجِسمُ النُقطَةِ يَحمِلُ
    /// نِداءً ورَدّاً لا حِساباً — وذاكَ عَقدُ
    /// <c>EndpointBodyBleedTests</c>.</para>
    /// </summary>
    public static string RefusalPath(string refusal, string name, string phone, string addr)
        => $"checkout?step=2&err={Uri.EscapeDataString(refusal)}"
         + $"&name={Uri.EscapeDataString(name)}"
         + $"&phone={Uri.EscapeDataString(phone)}"
         + $"&addr={Uri.EscapeDataString(addr)}";
}
