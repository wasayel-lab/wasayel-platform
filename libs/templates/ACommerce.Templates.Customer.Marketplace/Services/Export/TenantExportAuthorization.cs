using System.Text.RegularExpressions;
using ACommerce.Kit.Tenants;

namespace ACommerce.Templates.Customer.Marketplace.Services.Export;

/// <summary>
/// <para><b>أَثَرُ التَخارُجِ في سِجِلِّ التَدقيق</b> — رَمزُ الفِعلِ
/// في مَوضِعٍ واحِدٍ يَقرَؤُه الكاتِبُ (النُقطَة) والقارِئُ (الشاشَة).
/// ورَمزانِ نَصِّيّانِ في مَوضِعَينِ يَنجَرِفانِ بِحَرفٍ واحِد،
/// فَتَقولُ الشاشَةُ «لَم تُصَدِّر بَعد» أَبَداً.</para>
/// </summary>
public static class TenantExportAudit
{
    public const string Action = "tenant.export";
}

/// <summary>لِمَ رُفِضَ التَخارُج — رَمزٌ مِن مَعجَمٍ مُغلَق.</summary>
public enum TenantExportRefusal
{
    /// <summary>لا رَفض.</summary>
    None,

    /// <summary>لا سلاجَ في الطَلَب.</summary>
    SlugMissing,

    /// <summary>سلاجٌ مَحجوزٌ أَو مُشَوَّهُ الشَكل — ولا مَتجَرَ لَه.</summary>
    SlugReserved,

    /// <summary>لا وَثيقَةَ <c>Tenant</c> بِهذا السلاج.</summary>
    TenantNotFound,

    /// <summary>الفاعِلُ لَيسَ مالِكَ المَتجَر.</summary>
    NotOwner,
}

/// <summary>
/// <para><b>مَن يُصَدِّر، وماذا</b> — دالَّةٌ نَقِيَّةٌ واحِدَةٌ
/// يَقرَؤُها طَرَفانِ: النُقطَةُ قَبلَ أَن تَكتُب، والخِدمَةُ قَبلَ
/// أَن تَقرَأ. وطَرَفانِ يَقرَآنِ دالَّةً واحِدَةً لا يَنجَرِفان.</para>
///
/// <para><b>ولِماذا يُرفَضُ السلاجُ قَبلَ أَن يُبحَثَ عَنه</b>:
/// <c>_platform</c> و<c>_studio</c> و<c>_incubator</c> و<c>_admin</c>
/// <b>أَقسامٌ حَقيقيَّةٌ في القاعِدَةِ ولا وَثيقَةَ مُستَأجِرٍ لَها</b> —
/// فَمُصَدِّرٌ يَقبَلُ السلاجَ نَصّاً ويُمَرِّرُه إلى جَلسَةٍ يُخرِجُ
/// سِجِلَّ تَدقيقِ المَنَصَّةِ كامِلاً بِـ<c>_platform</c>، وحِساباتِ
/// كُلِّ رُوَّادِ الأَعمالِ بِـ<c>_studio</c>. <b>بِلا خَطَإٍ ولا سَطرِ
/// لوغ.</b> وثَلاثَتُها غائِبَةٌ عَن <c>ReservedTenantSlugs.All</c>
/// اليَوم، فَلا يُتَّكَلُ عَلَيها وَحدَها: الشَرطُ هُنا
/// <b>ثَلاثِيّ</b> — شَكلٌ، ومَعجَمٌ مَحجوز، ووَثيقَةٌ مَوجودَةٌ
/// فِعلاً.</para>
///
/// <para><b>ووَثيقَةٌ مَوجودَةٌ فِعلاً تَحجُبُ بَقايا الاختِبارِ
/// الحَيَّة</b>: قيسَ في القاعِدَةِ سلاجُ <c>hissa-demo</c> بِثَمانِيَةِ
/// صُفوفٍ <b>وبِلا وَثيقَةِ مُستَأجِر</b> — ويَومَ يُسَجِّلُ عَميلٌ
/// حَقيقيٌّ هذا السلاجَ يَرِثُ تَصديرُه صُفوفَ الاختِبار.</para>
///
/// <para><b>والمالِكُ وَحدَه لا مَن يُدير</b>: صَلاحِيَّةُ إدارَةِ
/// المَتجَرِ تُمنَحُ لِمُوَظَّف، وخُروجُ قاعِدَةِ العُملاءِ كُلِّها
/// لَيسَ عَمَلاً إدارِيّاً يَومِيّاً.</para>
/// </summary>
public static class TenantExportAuthorization
{
    private static readonly Regex SlugShape = new("^[a-z0-9][a-z0-9-]*$", RegexOptions.Compiled);

    public static TenantExportRefusal Decide(string? slug, Tenant? tenant, Guid? actorUserId)
    {
        var s = (slug ?? "").Trim();
        if (s.Length == 0) return TenantExportRefusal.SlugMissing;

        if (!SlugShape.IsMatch(s) || ReservedTenantSlugs.Contains(s))
            return TenantExportRefusal.SlugReserved;

        if (tenant is null || !string.Equals(tenant.Id, s, StringComparison.Ordinal))
            return TenantExportRefusal.TenantNotFound;

        if (actorUserId is null || actorUserId == Guid.Empty
            || tenant.OwnerUserId == Guid.Empty || tenant.OwnerUserId != actorUserId)
            return TenantExportRefusal.NotOwner;

        return TenantExportRefusal.None;
    }

    /// <summary>رَمزُ الرَفضِ كَما يُمَرَّرُ في عُنوانِ الصَفحَة —
    /// مَعجَمٌ مُغلَق، فَلا يُطبَعُ مِفتاحٌ خامٌّ على الشاشَة.</summary>
    public static string Code(TenantExportRefusal r) => r switch
    {
        TenantExportRefusal.SlugMissing => "slug_missing",
        TenantExportRefusal.SlugReserved => "slug_reserved",
        TenantExportRefusal.TenantNotFound => "tenant_not_found",
        TenantExportRefusal.NotOwner => "not_owner",
        _ => "",
    };
}
