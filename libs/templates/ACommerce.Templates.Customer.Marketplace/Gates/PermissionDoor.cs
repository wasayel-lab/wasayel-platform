using ACommerce.Kit.Roles;

namespace ACommerce.Templates.Customer.Marketplace.Gates;

/// <summary>
/// <para><b>بابُ صَلاحِيَّةٍ في الواجِهَة</b> — ما تَسأَلُه الصَفحَةُ المَحروسَةُ بِصَلاحِيَّة (<c>GatedPage Permission="…"</c>) قَبلَ مُحتَواها،
/// <b>ويَسأَلُه بِحَرفِه كُلُّ رابِطٍ أَو استِمارَةٍ تُفضي إلَيها</b>. دالّتانِ نَقِيَّتان — بِلا قاعِدَةٍ ولا HTTP — تَأخُذانِ ما قَرَأَتهُ الصَفحَة.</para>
///
/// <para><b>لِماذا وُجِدَت (‏2026-10-25، تَدقيقُ الإطلاق W8-7 — <c>lens-dead-doors-3</c>)</b>: «إعلان جَديد» في «إعلاناتي» كانَ يُرسَمُ لِلمُشتَري
/// بِدَورِه الافتِراضِيّ (<c>customer</c> بِلا <c>listing.create</c>)، فَيَملَأُ النَموذَجَ كامِلاً بِصُوَرِه ثُمَّ يَرُدُّه
/// <c>PermissionFilter</c> بِـ‏403 عارٍ. الرابِطُ لَم يَسأَل، والصَفحَةُ لَم تَسأَل، والنُقطَةُ وَحدَها سَأَلَت — بَعدَ فَواتِ النَقرَة.</para>
///
/// <para><b>وشَرطُ الاستِخراجِ (القاعِدَة ١) مُستَوفىً قَبلَها لا بَعدَها</b>: قَرارُ «دَورُ المَسارِ ثُمَّ المُخَزَّنُ ثُمَّ <c>RolePermissions.Has</c>» مَكتوبٌ
/// في الواجِهَةِ <b>أَربَعَ مَرّاتٍ بِأَربَعِ صِيَغ</b>: <c>GatedPage</c> (‏<c>Role ?? storedRole ?? ""</c>)، و<c>TenantHome</c>
/// (‏<c>!string.IsNullOrEmpty(Role) ? Role : home.StoredActiveRole</c>)، و<c>Me</c> (‏<c>Role ?? me?.ActiveRole</c>)، و<c>TenantManage</c>
/// (‏<c>Role ?? storedRole</c>) — وكانَ «إعلاناتي» سَيَكونُ الخامِسَة. فَصارَ <c>GatedPage</c> و«إعلاناتي» يَسأَلانِ هذِه، والثَلاثُ الباقِيَةُ عَلى حالِها
/// حَتّى يُوَصَّفَ كُلٌّ قَبلَ تَبديلِه (القاعِدَة ٣) — ومُستَهلِكٌ لا يُضافُ إلّا بِصَفٍّ يُسَمّيه في <c>CreateListingDoorTests</c>.</para>
///
/// <para><b>والدَورُ هُوَ ما تَحسِمُه <see cref="EffectiveRole.Resolve"/> بِلا <c>as</c></b> — نَفسُ تَرتيبِ نُقطَةِ الكِتابَة (‏<see cref="PermissionFilter"/>)
/// ناقِصاً الحَقلَ الصَريحَ الَّذي لا تَعرِفُه الصَفحَةُ قَبلَ الإرسال. والأَدوارُ ما يَقرَؤُه حارِسُ الصَفحَةِ الهَدَف: <c>GatedPage</c> و
/// <c>PermissionFilter</c> يَقرَآنِ <c>Tenant.Roles</c> المُخَزَّنَة (لا المُجَسَّدَةَ بِالأَدوارِ المُؤَلَّفَة)، فَيَقرَؤُها البابُ إلَيهِما كَذلِك.</para>
/// </summary>
public static class PermissionDoor
{
    /// <summary>هَل يُفتَحُ البابُ لِهذا الدَور؟ — <c>RolePermissions.Has</c> عَلى دَورِ المَسارِ إن وُجِدَ وإلّا المُخَزَّن. مَتجَرٌ بِلا أَدوارٍ مَفتوح
    /// (الوَضعُ المَوروث)، ودَورٌ غائِبٌ مُغلَق.</summary>
    public static bool Opens(IReadOnlyCollection<Role> tenantRoles, string? urlRole, string? storedRole, string permission)
        => RolePermissions.Has(tenantRoles, EffectiveRole.Resolve(null, false, urlRole, storedRole), permission);

    /// <summary><para><b>هَل يَفتَحُه تَبديلُ الدَور؟</b> — شَرطُ رابِطِ <c>me/role</c> مَكانَ البابِ المُغلَق: صَفحَةُ «اِختَر دَورَك» تَعرِضُ
    /// <see cref="SelfGrantPolicy.SelfGrantable"/> بِعَينِها، فَإن لَم يَكُن فيها دَورٌ يَحمِلُ الصَلاحِيَّةَ فَالرابِطُ بابٌ ثانٍ لا يُفتَح.</para>
    /// <para><b>وفي مَسارِ <c>/r/{role}/</c> لا</b>: دَورُ المَسارِ يَتَفَوَّقُ عَلى المُخَزَّنِ (‏<see cref="Opens"/>)، فَتَبديلُه لا يُغَيِّرُ جَوابَ البابِ داخِلَ
    /// ذلكَ التَطبيق — وهُوَ سَبَبُ إخفاءِ <c>Me</c> زِرَّ التَبديلِ هُناك.</para></summary>
    public static bool SwitchOpens(IEnumerable<Role> tenantRoles, string? urlRole, string permission)
        => string.IsNullOrEmpty(urlRole)
           && SelfGrantPolicy.SelfGrantable(tenantRoles).Any(r => r.Permissions.Contains(permission));
}
