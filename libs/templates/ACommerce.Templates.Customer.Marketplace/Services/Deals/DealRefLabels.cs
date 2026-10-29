namespace ACommerce.Templates.Customer.Marketplace.Services.Deals;

/// <summary>
/// <para><b>سَطرُ المَرجِعِ في سِجِلِّ الصَفقَةِ لِلطَرَفَين: تَسمِيَةٌ مِن القامُوس لا نَصُّ الكاتِب</b> (‏2026-10-13، مُراجَعَةُ المَوجَتَين 2–3،
/// G4 (xxvi)): <see cref="DealsService.AttachRefAsync"/> تَكتُبُ لِكُلِّ مَرجِعٍ سَطرَ <see cref="DealEvent.RefNote"/> نَصُّه
/// <c>attach {key}={value}</c>، وكانَت صَفحَةُ العُضوِ (<c>MyDealDetail</c>) تَرسُمُه كَما هُوَ لِلمُشتَري والبائِع: مَفاتيحَ داخِلِيَّةً
/// بِالإنكليزِيَّة، ومُعَرِّفَ مُستَخدِمٍ (<c>listing_owner</c>)، ونَصَّ استِثناءٍ (<c>payment_error</c>)، ورَوابِط.</para>
///
/// <para><b>فَصارَ مِفتاحُ المَرجِعِ يُقرَأُ مِن السَطرِ ويُحالُ بِمَعجَمٍ مُغلَقٍ (<see cref="Labels"/>) إلى مِفتاحِ قامُوس</b> (القاعِدَتانِ ٤
/// و١١)، <b>والقيمَةُ لا تُرسَمُ لَهُما أَبَداً</b> — مُعَرِّفاتٌ وروابِطُ ونُصوصُ أَخطاء؛ ورابِطا البوليصَةِ والتَتَبُّعِ يُرسَمانِ في صَفِّهِما
/// بِـ<c>href</c> (<c>DealShipping.LinksOf</c>). وما لَيسَ في المَعجَمِ — داخِلِيٌّ أَو مَجهول — <b>لا يُرسَمُ</b> (<see cref="IsShown"/>):
/// مَرجِعٌ يُضافُ غَداً لا يَظهَرُ لَهُما حَتّى يُسَمّى هُنا.</para>
///
/// <para><b>ولا يَمَسُّ صَفحَةَ الاستوديو</b>: صاحِبُ المَتجَرِ يَرى السَطرَ ونَصَّه لِلمُراجَعَة (وقائِمَةَ «المَراجِع»)، كَما كان.</para>
/// </summary>
public static class DealRefLabels
{
    /// <summary>بادِئَةُ سَطرِ الإلحاق — يَكتُبُها <see cref="DealsService.AttachRefAsync"/> ويَقرَؤُها <see cref="RefKeyOf"/>: حَرفٌ واحِدٌ لِلاثنَين.</summary>
    public const string AttachPrefix = "attach ";

    /// <summary>
    /// <para>المَعجَمُ المُغلَق: مِفتاحُ المَرجِعِ ← مِفتاحُ قامُوسٍ يُسَمّي ما وَقَعَ لِلطَرَفَين. وما لا يُسَمّى هُنا لا يُرسَمُ لَهُما:
    /// <c>payment_status</c> (قيمَةُ تِعدادٍ بِالإنكليزِيَّة يُرافِقُ <c>payment_id</c>)، و<c>payment_mode</c> (شارَةُ التَجرِبَةِ تُعلِنُه — ADR-025)،
    /// و<c>listing_owner</c> (مُعَرِّفُ مُستَخدِم)، و<c>gateway_invoice_url</c> (بابُ الدَفعِ يُرسَمُ زِرّاً)، و<c>oto_id</c> (مُعَرِّفُ OTO
    /// يُرافِقُ <c>oto_order_id</c>).</para>
    /// <para>و<c>review_cp</c>/<c>review_init</c> يَكتُبُهُما بابُ تَقييمِ العُضوِ بِحَرفِهِما (<c>$"review_{…}"</c>) — لا ثابِتَ لَهُما هُناك.</para>
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> Labels = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["payment_id"]                   = "deals.detail.ref_payment_recorded",
        ["payment_error"]                = "deals.detail.ref_payment_failed",
        ["review_cp"]                    = "deals.detail.ref_review_written",
        ["review_init"]                  = "deals.detail.ref_review_written",
        [GatewayPaymentProof.InvoiceRef] = "deals.detail.ref_gateway_invoice_issued",
        [DealShipping.OrderRef]          = "deals.detail.ref_oto_order_created",
        [DealShipping.LabelRef]          = "deals.detail.ref_oto_label_issued",
        [DealShipping.TrackingRef]       = "deals.detail.ref_oto_tracking_ready",
        [DealShipping.ClosedRef]         = "deals.detail.ref_oto_order_closed",
        [GatewayPayments.InvoiceClosedRef] = "deals.detail.ref_gateway_invoice_closed",   // ‏2026-10-13، G5 (xvii-g)
        [DealShipping.DeliveredAfterCancelRef] = "deals.detail.ref_oto_delivered_after_cancel",   // ‏2026-10-24، تَدقيقُ الإطلاق lens-money-integrity-4
    };

    /// <summary>مِفتاحُ المَرجِعِ مِن سَطرِ الإلحاق — ما بَينَ البادِئَةِ وأَوَّلِ «=» (والقيمَةُ قَد تَحمِلُ «=») — أَو <c>null</c>
    /// لِما لَيسَ سَطرَ إلحاق.</summary>
    public static string? RefKeyOf(string? note)
    {
        if (note is null || !note.StartsWith(AttachPrefix, StringComparison.Ordinal)) return null;
        var eq = note.IndexOf('=', AttachPrefix.Length);
        return eq > AttachPrefix.Length ? note[AttachPrefix.Length..eq] : null;
    }

    /// <summary>مِفتاحُ القامُوسِ لِسَطرِ إلحاقٍ مَعروف — أَو <c>null</c> فَلا يُرسَمُ السَطر.</summary>
    public static string? LabelKeyFor(string? note)
        => RefKeyOf(note) is { } key && Labels.TryGetValue(key, out var label) ? label : null;

    /// <summary>أَيُرسَمُ السَطرُ لِلطَرَفَين؟ كُلُّ فِعلٍ غَيرِ <see cref="DealEvent.RefNote"/> كَما كان؛ وسَطرُ المَرجِعِ إن سُمِّيَ وَحدَه.</summary>
    public static bool IsShown(DealEvent e)
        => !string.Equals(e.Action, DealEvent.RefNote, StringComparison.Ordinal) || LabelKeyFor(e.Note) is not null;
}

/// <summary>
/// <para><b>أَسطُرُ الدَفعِ الأَربَعَةُ في سِجِلِّ الصَفقَة: تُسَمّى بِالحالِ المُسَجَّلَةِ في السَطرِ لا بِفِعلِه وَحدَه</b> (‏2026-10-29، قَرارُ المالِك
/// د22 (١) «ب»). <see cref="DealsService"/> يَكتُبُ <c>captured</c> بِمُلاحَظَةِ <c>capture {PaymentStatus} {المَبلَغ} SAR</c> كُلَّما عادَ
/// <c>CaptureAsync</c> بِلا استِثناء — ولو بِحالٍ غَيرِ <see cref="ACommerce.Kit.Payments.PaymentStatus.Captured"/> — و<c>refunded</c> بِـ
/// <c>refund {PaymentStatus} …</c>؛ وكانَت صَفحَةُ العُضوِ تَرسُمُ اسمَ الفِعلِ بِالإنكليزِيَّة. فَالتَسمِيَةُ بِالفِعلِ وَحدَه قَد تُخبِرُ المُشتَري
/// بِتَحصيلٍ لَم يَقَع: <b>«تَمَّ» لا تُقالُ إلّا إذا قالَتها الحال</b>.</para>
/// <para>مَعجَمٌ مُغلَقٌ مِن خَمسَةِ مَفاتيح، والحالُ تُقرَأُ بِحَرفِ التِعدادِ (<see cref="Enum.TryParse{TEnum}(string?, bool, out TEnum)"/> بِلا تَجاهُلِ حالَةِ
/// الحُروف) — فَحالٌ مَجهولَةٌ أَو مُلاحَظَةٌ مَشوَّهَةٌ تُسَمّى «تَعَذَّر»، لا «تَمّ».</para>
/// </summary>
public static class DealPaymentLines
{
    public const string Captured      = "captured";
    public const string CaptureFailed = "capture_failed";
    public const string Refunded      = "refunded";
    public const string RefundFailed  = "refund_failed";

    public const string CapturedKey        = "deals.detail.event_payment_captured";
    public const string CaptureFailedKey   = "deals.detail.event_payment_capture_failed";
    public const string RefundedKey        = "deals.detail.event_payment_refunded";
    public const string PartlyRefundedKey  = "deals.detail.event_payment_partly_refunded";
    public const string RefundFailedKey    = "deals.detail.event_payment_refund_failed";

    /// <summary>المَفاتيحُ الخَمسَة — لِلاختِبارِ الَّذي يَمنَعُ نُمُوَّها صامِتَة.</summary>
    public static readonly IReadOnlyList<string> Keys = [CapturedKey, CaptureFailedKey, RefundedKey, PartlyRefundedKey, RefundFailedKey];

    /// <summary>الحالُ المُسَجَّلَةُ في مُلاحَظَةِ <paramref name="verb"/> (‏<c>capture</c>/<c>refund</c>) — الكَلِمَةُ الثانِيَة — أَو <c>null</c>.</summary>
    public static ACommerce.Kit.Payments.PaymentStatus? StatusOf(string? note, string verb)
    {
        if (note is null) return null;
        var parts = note.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 && parts[0] == verb
               && Enum.TryParse<ACommerce.Kit.Payments.PaymentStatus>(parts[1], ignoreCase: false, out var status)
               && Enum.IsDefined(status)
            ? status : null;
    }

    /// <summary>مِفتاحُ القامُوسِ لِسَطرٍ مِن السِجِلّ — والصَفحَةُ تُمَرِّرُ السَطرَ كُلَّه فَلا تَلمِسُ مُلاحَظَتَه (‏<c>e.Note</c> في صَفحَةِ العُضوِ
    /// وَسيطُ <see cref="DealRefLabels.LabelKeyFor"/> وَحدَه — <c>DealTimelineRefLabelsTests</c>).</summary>
    public static string? LabelKeyFor(DealEvent e) => LabelKeyFor(e.Action, e.Note);

    /// <summary>مِفتاحُ القامُوسِ لِسَطرِ دَفعٍ — أَو <c>null</c> لِما لَيسَ سَطرَ دَفع.</summary>
    public static string? LabelKeyFor(string action, string? note) => action switch
    {
        Captured      => StatusOf(note, "capture") == ACommerce.Kit.Payments.PaymentStatus.Captured ? CapturedKey : CaptureFailedKey,
        CaptureFailed => CaptureFailedKey,
        Refunded      => StatusOf(note, "refund") switch
        {
            ACommerce.Kit.Payments.PaymentStatus.Refunded          => RefundedKey,
            ACommerce.Kit.Payments.PaymentStatus.PartiallyRefunded => PartlyRefundedKey,
            _                                                      => RefundFailedKey,
        },
        RefundFailed  => RefundFailedKey,
        _             => null,
    };
}
