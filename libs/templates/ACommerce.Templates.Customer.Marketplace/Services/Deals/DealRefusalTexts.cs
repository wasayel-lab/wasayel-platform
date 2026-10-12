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
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    /// <summary>مِفتاحُ النَصِّ لِرَمزٍ مَعروف، و<c>null</c> لِما سِواه (ومِنه الغِياب).</summary>
    public static string? KeyFor(string? code)
        => code is not null && Codes.Contains(code, StringComparer.Ordinal) ? Prefix + code : null;
}
