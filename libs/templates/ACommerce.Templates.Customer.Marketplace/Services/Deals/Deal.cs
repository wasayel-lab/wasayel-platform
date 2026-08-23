namespace ACommerce.Templates.Customer.Marketplace.Services.Deals;

/// <summary>
/// <para><b>الـ Deal</b> — كِيان مُوَحَّد يَمُرّ بِتَدَفُّق عَمَليَّات
/// مُتَتالِيَة (عَرض → حَجز → تَأكيد → دَفع → شَحن → استِلام → تَقييم).
/// كُلّ نَمَط (marketplace, rental, trip, service) يَستَخدِم مَجموعَة
/// فَرعيَّة مِن المَراحِل عَبر <see cref="DealsPolicy"/>.</para>
///
/// <para>الإِنفِراد: لا أَحد يَكتُب الحالَة مُباشَرَةً — كُلّ تَغيير يَمُرّ
/// عَبر <see cref="DealsService"/> الَّذي يَفحَص الـ state machine ويَكتُب
/// في الـ Timeline (للـ audit). إِشعار الأَطراف يَخرُج تِلقائيّاً.</para>
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

    /// <summary>
    /// <para><b>هَل هذا الفاعِلُ طَرَفٌ في الصَفقَة؟</b> — التَعريفُ
    /// <b>الواحِد</b> لِـ«طَرَف»: المُبادِرُ أَو الطَرَفُ الثاني. دالَّةٌ
    /// نَقِيَّة على الوَثيقَة نَفسِها، لا تُكتَب ولا تُسَلسَل.</para>
    ///
    /// <para><b>ولِماذا هُنا وعَلى الوَثيقَة</b> (القاعِدَة ١: ثَلاثَةُ
    /// مُستَهلِكينَ قَبلَ الاستِخراج — وقَد وُجِدوا): كانَ الشَرطُ
    /// نَفسُه مَكتوباً <b>ثَلاثَ مَرّات</b> بِثَلاثِ صِيَغ —
    /// <c>DealCancelAuthorization.Validate</c> (مَن يُلغي)،
    /// و<c>DealsService.IsActorAllowed("either")</c> (مَن يُحَرِّك)،
    /// و<c>DealApi.IsParty</c> (مَن يَرى) — وثَلاثُ نُسَخٍ لِسُؤالٍ
    /// واحِدٍ تَنجَرِف. الثَلاثَةُ الآنَ تَقرَأُ هذا السَطر.</para>
    ///
    /// <para><b>والاستِثناءُ المَعلَن</b>: <c>ListForUserAsync</c> تَكتُب
    /// الشَرطَ في تَعبيرِ LINQ لِأَنّ Marten لا يُتَرجِم نِداءَ دالَّة
    /// إلى SQL — فَتِلكَ نُسخَةٌ رابِعَةٌ <b>مَقيسَةٌ ومُثَبَّتَة</b> في
    /// <c>DealCancelAuthorizationTests</c>، لا صامِتَة.</para>
    /// </summary>
    public bool IsParty(Guid actorId) =>
        InitiatorId == actorId || CounterpartyId == actorId;

    // ─── المال ──────────────────────────────────────────────────────
    public decimal AmountSar { get; set; }
    public decimal? CommissionSar { get; set; }    // عَلى المَنصَّة

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
    string Action,          // advanced | cancelled | disputed | assigned | note | refund
    Guid? ActorId,
    string? ActorName,
    string? Note,
    DateTime At);
