namespace ACommerce.Templates.Customer.Marketplace.Services.Deals;

/// <summary>
/// <para><b>رَمزُ الرَفضِ → مِفتاحُ القامُوس</b> — دالَّةٌ نَقِيَّةٌ تَقرَؤُها صَفحَةُ
/// الصَفقَة. كانَت <c>/accept</c> و<c>/cancel</c> تَرتَدّانِ إلى <c>deals/{id}?err=&lt;code&gt;</c>
/// والصَفحَةُ بِلا مُعامِلِ <c>err</c> — رَفضٌ يُرسَلُ ولا يُقرَأ، فَتَعودُ النَقرَةُ
/// المَرفوضَةُ بِصَفحَةٍ كَأَنَّ شَيئاً لَم يَكُن (مُراجَعَةُ المَوجَة ١). النَسَقُ نَسَقُ
/// <c>store.locked.write_denied</c> في <c>TenantHome.razor</c>: الرَمزُ مِن الجَدوَلِ،
/// والكَلامُ في المَعجَم (القاعِدَة ١١).</para>
///
/// <para><b>المَعجَمُ مُغلَق</b>: رُموزُ <see cref="DealAcceptAuthorization"/> و
/// <see cref="DealCancelAuthorization"/> (والرَمزانِ المُشتَرَكانِ <c>deal_not_found</c> و
/// <c>deal_not_active</c> مِفتاحٌ واحِدٌ لِكُلٍّ) ورَمزُ أُنبوبِ المُحادَثَة
/// <see cref="ChatStartReturn.PartnerMissing"/>؛ وما خَرَجَ عَنها <c>null</c> — فَلا يُرسَمُ
/// شَريطٌ لِرَمزٍ لا نَصَّ لَه، ولا يُرسَمُ المِفتاحُ خاماً. وفاحِصٌ يُثبِتُ أَنّ لِكُلِّ
/// رَمزٍ مِفتاحاً حَيّاً في <c>ar.json</c>.</para>
///
/// <para><b>وشَكلُ المِفتاحِ <c>deals.detail.err_&lt;code&gt;</c></b> لا <c>…err.&lt;code&gt;</c>:
/// اصطِلاحُ القامُوسِ ثَلاثَةُ مَقاطِعَ بِالضَبط (<c>LocaleValidator.KeyPattern</c>)، فَالرَمزُ
/// يُلحَقُ بِالمَقطَعِ الثالِثِ لا مَقطَعاً رابِعاً.</para>
/// </summary>
public static class DealRefusalTexts
{
    public const string Prefix = "deals.detail.err_";

    /// <summary>كُلُّ رَمزٍ قَد يَصِلُ الصَفحَةَ في <c>?err=</c> — المُشتَرَكُ مَرَّةً.</summary>
    public static readonly IReadOnlyList<string> Codes =
        DealAcceptAuthorization.All
            .Concat(DealCancelAuthorization.All)
            .Append(ChatStartReturn.PartnerMissing)
            // ورَمزُ تَعارُضِ النُسخَة (‏ADR-036، ‏2026-10-12) — تُرجِعُه `/accept` و`/cancel` و`/advance` جَميعاً.
            .Append(DealsService.ChangedMeanwhile)
            // ورُموزُ نُقطَةِ الشَحن (‏ADR-033 الخُطوَة 3، ‏2026-10-12): بَوّابَتُها — والمُشتَرَكُ مَعَ القَبولِ
            // (`deal_not_found`، `listing_owner_missing`، `deal_not_active`) مِفتاحٌ واحِد — ومَعجَمُ فَشَلِ
            // عَميلِ OTO الخُماسيّ، وكُلُّها تَرتَدُّ إلى هذِه الصَفحَةِ بِـ`?err=`.
            .Concat(DealShipAuthorization.All)
            .Concat(ACommerce.Kit.Delivery.OtoFailures.All)
            // ورَمزُ «لَم تُثبِت OTO الشُحنَة» (مُراجَعَةُ تَيّارِ S7، ‏2026-10-12): الطَلَبُ عِندَها ومَرجِعاه مَكتوبان، فَلا يُعادُ
            // رَمزُ فَشَلٍ نَصُّه «ولَم يُكتَب في الصَّفقَة شَيء».
            .Append(DealShipping.ShipmentUnconfirmed)
            // ورَمزُ «طَلَبُها عِندَ OTO» (مُراجَعَةُ تَيّارِ S7 الثانِيَة، ‏2026-10-12): تُرجِعُه `AdvanceAsync` لِبابٍ عامٍّ لا يَحمِلُ جَوابَ OTO،
            // فَيَبلُغُ الصَفحَةَ مِن `/advance` بِـ`?err=` — والصَفحَةُ لا تَرسُمُ ذلك الزِرَّ، فَلا يَصِلُه إلّا سِباقٌ أَو طَلَبٌ مَصنوع.
            .Append(DealShipping.DealAwaitsCarrier)
            // ورَمزُ «فاتورَتُها لَم تُدفَع» (‏ADR-032 الخُطوَة 10، تَيّارُ S9، ‏2026-10-12): تُرجِعُه `AdvanceAsync` لِصَفقَةٍ عَلَيها فاتورَةُ بَوّابَةٍ
            // لَم يَثبُت دَفعُها وتاليها «دَفع»، فَيَبلُغُ الصَفحَةَ مِن `/advance` بِـ`?err=` — والصَفحَةُ تَرسُمُ «أَكمِل الدَّفع» مَكانَ ذلكَ الزِرّ.
            .Append(GatewayPayments.DealAwaitsPayment)
            .Concat(GatewayInvoiceShare.All)   // (‏2026-10-13، G5 M4): رَدُّ إلغاءِ صَفقَةٍ واحِدَةٍ مِن فاتورَةٍ مُشتَرَكَةٍ مَفتوحَة.
            .Append(DealsService.PaymentMissing)   // (‏2026-10-13، G5 (xv-g)): صَفقَةُ بِطاقَةٍ بِلا مَرجِعِ دَفعٍ لا تَدخُلُ «دَفع».
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    /// <summary>مِفتاحُ النَصِّ لِرَمزٍ مَعروف، و<c>null</c> لِما سِواه (ومِنه الغِياب).</summary>
    public static string? KeyFor(string? code)
        => code is not null && Codes.Contains(code, StringComparer.Ordinal) ? Prefix + code : null;

    // ─── صَفحَتا صَفَقاتِ الاستوديو (‏2026-10-13، مُراجَعَةُ المَوجَة 3a — G4 (i)) ───────────────────────────
    //
    // نِقاطُ المَسارِ الإداريِّ (`/studio/apps/{slug}/deals/{id}/advance|cancel|dispute`) كانَت تَعودُ إلى الصَفحَةِ بِلا رَمز
    // (تَوصيفُه `StudioDealRefusalTests`، ‏ADR-036 §٣‑٣). **ومَعجَمٌ ثانٍ لا الأَوَّلُ نَفسُه**: نُصوصُ `deals.detail.err_*` تُخاطِبُ
    // طَرَفَ الصَفقَةِ عَلى صَفحَتِه وتُسَمّي أَزرارَها («بِزِرِّ «أَكمِل الشَّحن» هُنا») — والاستوديو لا يَرسُمُ تِلكَ الأَزرار. فَالرُموزُ
    // الَّتي تَبلُغُ الاستوديو وَحدَها، بِبادِئَتِه، والكَلامُ لِمالِكِ المَتجَر.

    public const string StudioPrefix = "studio.deal.err_";

    /// <summary>كُلُّ رَمزٍ قَد يَصِلُ صَفحَتَي الاستوديو في <c>?err=</c> — مَعجَمٌ مُغلَق: الغِيابُ (القائِمَةُ لِصَفقَةٍ لا تُحَمَّل)، وغَيرُ الفَعّالَة،
    /// وتَعارُضُ النُسخَة، وما يَرُدُّه <see cref="DealsService.AdminPathRefusal"/> — ولا رَمزَ لِأَزرارِ صَفحَةِ العُضو.</summary>
    public static readonly IReadOnlyList<string> StudioCodes = new[]
    {
        DealCancelAuthorization.DealNotFound,
        DealCancelAuthorization.DealNotActive,
        DealsService.ChangedMeanwhile,
        DealsService.AdminPathCannotMove,
        DealShipping.DealAwaitsCarrier,
        GatewayPayments.DealAwaitsPayment,
        GatewayInvoiceShare.Shared,   // (‏2026-10-13، G5 M4)
        DealsService.PaymentMissing,  // (‏2026-10-13، G5 (xv-g))
    };

    /// <summary>مِفتاحُ نَصِّ الاستوديو لِرَمزٍ مِن <see cref="StudioCodes"/>، و<c>null</c> لِما سِواه — فَلا يُرسَمُ شَريطٌ لِرَمزٍ لا نَصَّ لَه.</summary>
    public static string? StudioKeyFor(string? code)
        => code is not null && StudioCodes.Contains(code, StringComparer.Ordinal) ? StudioPrefix + code : null;
}
