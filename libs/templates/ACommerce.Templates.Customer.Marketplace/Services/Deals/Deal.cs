namespace ACommerce.Templates.Customer.Marketplace.Services.Deals;

/// <summary>
/// <para><b>الـ Deal</b> — كِيان مُوَحَّد يَمُرّ بِتَدَفُّق عَمَليَّات
/// مُتَتالِيَة (عَرض → حَجز → تَأكيد → دَفع → شَحن → استِلام → تَقييم).
/// كُلّ نَمَط (marketplace, rental, trip, service) يَستَخدِم مَجموعَة
/// فَرعيَّة مِن المَراحِل عَبر <see cref="DealsPolicy"/>.</para>
///
/// <para>الإِنفِراد: لا أَحد يَكتُب الحالَة مُباشَرَةً — كُلّ تَغيير يَمُرّ
/// عَبر <see cref="DealsService"/> الَّذي يَفحَص الـ state machine ويَكتُب
/// في الـ Timeline (للـ audit). <b>ولا إشعارَ يَخرُجُ مِن الخِدمَةِ نَفسِها</b>: إشعارُ
/// الأَطرافِ يُرَكَّبُ في <see cref="DealNotices"/> عَلى الأُنبوبِ القائِمِ ويُنادى مِن
/// نُقطَتَي <c>/checkout/submit</c> (لِمالِكِ الإعلان) و<c>/accept</c> (لِلمُشتَري)
/// وَحدَهُما — وكانَ هذا التَعليقُ يَقول «يَخرُج تِلقائيّاً» بِصِفرِ مُنتِج (P1-5).</para>
///
/// <para>Marten doc تَحت tenant الـ slug (conjoined).</para>
/// </summary>
public sealed class Deal
{
    public Guid Id { get; set; }
    public string TenantSlug { get; set; } = "";
    public string Pattern { get; set; } = "marketplace";  // marketplace | rental | trip | service | classifieds

    // ─── الشَّيء المَتداوَل ──────────────────────────────────────────
    public Guid? ListingId { get; set; }
    public string ListingTitle { get; set; } = "";

    // ─── الأَطراف ───────────────────────────────────────────────────
    public Guid InitiatorId { get; set; }
    public string InitiatorName { get; set; } = "";
    public Guid? CounterpartyId { get; set; }
    public string? CounterpartyName { get; set; }

    // ─── المال ──────────────────────────────────────────────────────
    // ولا حَقلَ عَمولَةٍ لِلمَنصَّة: كانَ هُنا `CommissionSar` يُكتَبُ عِندَ
    // Paid بِنِسبَةِ ‏2.5% ويُعرَضُ لِلمُشتَري، وأُسقِطَ بِـADR-032 §٢‑ب
    // (‏2026-10-11) — إيرادُ المَنصَّةِ اشتِراكُ المُستَأجِرِ وَحدَه، ولا
    // تَمُرُّ بِها أَموالُ الصَفَقات. (الوَثائِقُ القَديمَةُ الَّتي تَحمِلُ
    // الحَقلَ تُقرَأُ بِلا خَطَأ: الخاصِّيَّةُ المَجهولَةُ تُهمَلُ عِندَ
    // فَكِّ JSON — مَقيسٌ مُنذُ ‏2026-10-12 في `DealStoredCommissionFieldTests`:
    // تُحَمَّلُ وتُستَعلَمُ وتُكتَب، ويَزولُ الحَقلُ بِأَوَّلِ كِتابَة؛ DEPLOY.md §٤‑د.)
    public decimal AmountSar { get; set; }

    // ─── الحالَة ────────────────────────────────────────────────────
    public DealStage Stage { get; set; } = DealStage.Offered;
    public DealStatus Status { get; set; } = DealStatus.Active;
    public string? CancelReason { get; set; }

    /// <summary>سِجِلّ مَراجِع لِكائِنات خارِجيَّة مَرتَبِطَة (PaymentId،
    /// ShipmentId، ReviewId، ChatId، BookingId، …).</summary>
    public Dictionary<string, string> Refs { get; set; } = new();

    /// <summary>خَصائِص ديناميكيَّة لِكُلّ نَمَط (تَواريخ الحَجز، عُنوان
    /// التَّسليم، تَفاصيل ضامِنَة، …).</summary>
    public Dictionary<string, string> Attributes { get; set; } = new();

    /// <summary>مِفتاحا عُنوانِ التَوصيلِ وهاتِفِ المُشتَري في <see cref="Attributes"/> —
    /// يَكتُبُهُما <c>/checkout/submit</c> ويَقرَؤُهُما تَفصيلُ الصَفقَةِ <b>لِلبائِعِ
    /// وَحدَه</b> (P1-4؛ ‏PDPL في <c>PRODUCTION-PLAN.md:229</c>). ثابِتانِ هُنا لِأَنّ
    /// الكاتِبَ والقارِئَ يَجِبُ أَن يَقرَآ نَفسَ الحَرف — نَسَقُ
    /// <c>DealsService.ListingOwnerRef</c>.</summary>
    public const string DeliveryAddressAttribute = "addr";
    public const string DeliveryPhoneAttribute = "phone";

    /// <summary>سَجلّ الزَّمَن لِكُلّ تَحَوُّل — مَصدَر الحَقيقَة لِلـ audit.</summary>
    public List<DealEvent> Timeline { get; set; } = new();

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>مَراحِل تَدَفُّق العَمَلِيّات. كُلّ نَمَط يُستَخدِم سُبسِت مِنها.</summary>
public enum DealStage
{
    Offered,        // ١. عَرض/طَلَب أَوَّليّ
    Booked,         // ٢. اختِيار طَرَف ثانٍ (وكيل/سائق/مالِك أَصل)
    Confirmed,      // ٣. تَأكيد مُتَبادَل
    Paid,           // ٤. دَفع مُحَجوز/مَخصوم
    Shipping,       // ٥. شَحن/تَوصيل (إن وُجِد)
    Delivered,      // ٦. تَسليم بَدَنيّ
    Received,       // ٧. تَأكيد العَميل بِالاستِلام
    Reviewed,       // ٨. تَقييم مُتَبادَل (الـ Deal مُكتَمِل)
}

public enum DealStatus
{
    Active,        // مَفتوحَة
    Completed,     // اكتَمَلَت بِنَجاح (وَصَلَت Reviewed)
    Cancelled,     // أُلغِيَت قَبل الاكتِمال
    Disputed       // نِزاع — يَنتَظِر تَدَخُّل المَنصَّة
}

public sealed record DealEvent(
    DealStage StageBefore,
    DealStage StageAfter,
    string Action,          // created | assigned | bound | advanced | captured | capture_failed | cash_on_delivery | cancelled | refunded | refund_failed | disputed | note | gateway_paid | gateway_refund_due
    Guid? ActorId,
    string? ActorName,
    string? Note,
    DateTime At)
{
    /// <summary>البائِعُ رُبِطَ طَرَفاً مُقابِلاً عِندَ وِلادَةِ صَفقَةِ السَلَّة — لا
    /// قَبولاً (‏ADR-033 §٦). يُمَيَّزُ عَن <c>assigned</c> لِأَنّ الشاشَةَ تُسَمّي تِلكَ
    /// «قُبِلَ العَرض»، وهذا لَم يُقبَل بَعد. الثابِتُ هُنا لِأَنّ الكاتِبَ
    /// (<c>DealsService</c>) والقارِئَ (<c>MyDealDetail</c>) يَقرَآنِ نَفسَ الحَرف.</summary>
    public const string BoundAtBirth = "bound";

    /// <summary>بُلوغُ <c>Delivered</c> في صَفقَةِ نَقدٍ هُوَ لَحظَةُ القَبضِ عِندَ
    /// البائِع (‏ADR-033 §٦) — والنَصُّ مِن القامُوسِ عِندَ العَرض، لا هُنا.</summary>
    public const string CashOnDelivery = "cash_on_delivery";

    /// <summary>دَفعٌ أَثبَتَته بَوّابَةُ المَتجَرِ مِن الخادِمِ وكُتِبَ عَلى الصَفقَة (‏ADR-032 الخُطوَة 10، تَيّارُ S9، ‏2026-10-12) — يَكتُبُه
    /// <c>DealsService.ConfirmGatewayPaymentAsync</c> وَحدَه، والنَصُّ مِن القامُوسِ عِندَ العَرض؛ و<c>Note</c> مُعَرِّفُ الفاتورَةِ لِلمُراجَعَة.</summary>
    public const string GatewayPaid = "gateway_paid";

    /// <summary>مالٌ قَبَضَته بَوّابَةُ المَتجَرِ عَن صَفقَةٍ أُلغِيَت — استِرجاعُه مُستَحَقٌّ عِندَ البَوّابَةِ نَفسِها، يَستَرجِعُه صاحِبُ المَتجَرِ
    /// مِن لَوحَتِها؛ ولا نِداءَ عِندَنا (مُزَوِّدُ النُسخَةِ لا يَعرِفُ المَرجِع). يَكتُبُه الإلغاءُ بَعدَ الدَفع، والتَأكيدُ بَعدَ الإلغاء.</summary>
    public const string GatewayRefundDue = "gateway_refund_due";

    /// <summary>سَطرُ مَرجِعٍ أُلحِق — يَكتُبُه <c>DealsService.AttachRefAsync</c> وَحدَه بِنَصِّ <c>attach {key}={value}</c>، ويَقرَؤُه
    /// <c>DealRefLabels</c> لِصَفحَةِ الطَرَفَين (‏2026-10-13، G4 (xxvi)). الثابِتُ هُنا لِأَنّ الكاتِبَ والقارِئَ يَقرَآنِ نَفسَ الحَرف.</summary>
    public const string RefNote = "note";
}
