using System.Globalization;
using ACommerce.Kit.Subscriptions;

namespace ACommerce.Templates.Customer.Marketplace.Services.Subscriptions;

// ═══ تَأليفُ باقَةِ مَتجَرٍ — قِراءَةُ النَموذَجِ دالَّةٌ نَقِيَّة ══════
//
// **العِلَّةُ المَقيسَة (‏2026-08-30)**: ‏`TenantPlanDefinition` وَثيقَةٌ
// **بِصِفرِ كاتِبٍ في المُستَودَعِ كُلِّه**. مُعَرَّفَةٌ
// (`PlanDefinition.cs`)، والخِدمَةُ تَرِثُ `Propose/Decide`،
// و`/{slug}/plans` تَعرِضُها، و`TenantExportLedger` يُصَدِّرُها — ولا
// نُقطَةَ `POST` ولا صَفحَةَ Razor تَكتُبُ واحِدَة. فَالتاجِرُ **لا
// يُؤَلِّفُ باقَةً ولا يُسَعِّرُها**، ويَرى زُوّارُه كاتالوجَ المَنَصَّةِ
// وَحدَه. وذلكَ لَيسَ عَمَلاً يَدَوِيّاً بَل مُتَعَذِّراً.
//
// **ولِماذا دالَّةٌ نَقِيَّةٌ لِقِراءَةِ النَموذَج** (نَفسُ حُجَّةِ
// `TenantPlanPolicy.ReadSetting` حَرفاً): الباقَةُ **مالٌ وحِصَّة** لا
// تَسمِيَةٌ ولَون. حِصَّةٌ سالِبَةٌ تُعطي رَصيداً سالِباً مِن أَوَّلِ
// يَوم، ومُدَّةٌ صِفرِيَّةٌ اشتِراكاً يَنتَهي قَبلَ أَن يَبدَأ —
// وتَحويلُ سِلسِلَةِ نَموذَجٍ إلى عَدَدٍ هُوَ بِالضَبطِ حَيثُ تَقَعُ
// هذِه الأَخطاء. فَلا نَوعَ طَلَبٍ في هذا المِلَفّ إطلاقاً — ولا اسمُه
// حَتّى في تَعليق، لِأَنّ الفاحِصَ نَصِّيّ: تَأخُذُ سَلاسِلَ وتُعطي
// تَعريفاً وقائِمَةَ خُروق، وتُقاسُ بِلا طَلَب.

/// <summary>
/// <para><b>قِراءَةُ استِمارَةِ باقَةٍ يُؤَلِّفُها صاحِبُ المَتجَر</b>
/// — سَلاسِلُ داخِلَةٌ، تَعريفٌ ورُموزُ خَرقٍ خارِجَة.</para>
/// </summary>
public static class TenantPlanAuthoring
{
    /// <summary>اسمُ فِعلِ التَدقيق — يَسكُنُ مَعَ المَنطِقِ فَلا
    /// يَخترِعُه سَطحٌ ولا يَنجَرِف. نَفسُ اصطِلاحِ
    /// <see cref="TenantPlanAdminService"/>.</summary>
    public const string AuditAction = "tenant.plan_definition_author";

    /// <summary>
    /// <para><b>يَبني التَعريفَ ويُصادِقُه بِبَوّابَةِ المُستَأجِر</b>
    /// (<see cref="PlanDefinitionValidator.ValidateTenantDefinition"/>)
    /// — أَي بِكُلِّ ما في المُصادِقِ العامّ، <b>وزِيادَةً</b>: أَن لا
    /// يُظَلِّلَ سلاجَ باقَةٍ مَبذورَة.</para>
    ///
    /// <para><b>والسُقوطُ عِندَ كُلِّ حَقلٍ مَقصود</b>: سِعرٌ غَيرُ
    /// مَقروءٍ = صِفر (باقَةٌ مَجّانِيَّة — وهي الحالَةُ الآمِنَة، إذ
    /// المَجّانِيَّةُ وَحدَها تُمنَحُ ذاتِيّاً)، وحِصَّةٌ غَيرُ
    /// مَقروءَةٍ = صِفر (عَرضٌ بِلا نَشر)، و<b>مُدَّةٌ غَيرُ مَقروءَةٍ =
    /// صِفر فَتُرَدُّ بِـ<c>period_not_positive</c></b> — لا «سَنَةٌ
    /// افتِراضِيَّة» تُخترَع (القاعِدَة ١٦).</para>
    ///
    /// <para><b>والسلاجُ يُطَبَّعُ قَبلَ أَن يَصيرَ مِفتاحَ
    /// وَثيقَة</b>: قَصٌّ وتَصغير. مِسافَةٌ زائِدَةٌ أَو حَرفٌ كَبيرٌ
    /// كانا سَيُنتِجانِ باقَتَينِ يَظُنُّ صاحِبُهُما أَنَّهُما
    /// واحِدَة.</para>
    /// </summary>
    public static (PlanDefinition Definition, IReadOnlyList<PlanDefinitionViolation> Violations)
        ReadDefinition(
            string? slug, string? labelAr, string? descriptionAr,
            string? price, string? listingsQuota, string? daysPeriod, bool isActive)
    {
        var definition = new PlanDefinition(
            Slug:          (slug ?? "").Trim().ToLowerInvariant(),
            Label:         new PlanText((labelAr ?? "").Trim()),
            Description:   new PlanText((descriptionAr ?? "").Trim()),
            Price:         ReadDecimal(price),
            ListingsQuota: ReadInt(listingsQuota),
            DaysPeriod:    ReadInt(daysPeriod),
            IsActive:      isActive);

        return (definition, PlanDefinitionValidator.ValidateTenantDefinition(definition));
    }

    private static decimal ReadDecimal(string? raw)
        => decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var v)
            ? v : 0m;

    private static int ReadInt(string? raw)
        => int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)
            ? v : 0;
}
