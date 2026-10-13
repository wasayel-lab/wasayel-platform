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
