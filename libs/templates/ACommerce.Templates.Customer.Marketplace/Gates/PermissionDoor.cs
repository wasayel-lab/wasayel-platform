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
///
/// <para><b>والمُستَهلِكُ الثالِث (‏2026-10-25، W8-7 — <c>lens-dead-doors-7</c>؛ يُلحَقُ ولا يُعادُ حَرفٌ فَوقَ ما سَبَق)</b>: استِمارَةُ العَرضِ في
/// <c>TenantListingDetail</c> تَسأَلُ <c>Opens(…, "offer.submit")</c> — وحارِسُ نُقطَتِها <c>HasPermissionAsync</c> يَقرَأُ الأَدوارَ <b>المُجَسَّدَة</b>
/// (<c>LoadTenantWithRolesAsync</c>)، فَتَقرَؤُها الصَفحَةُ مُجَسَّدَةً (<c>TenantRoleService.LoadAsync</c>). القاعِدَةُ واحِدَة: البابُ يَقرَأُ ما يَقرَؤُه حارِسُ هَدَفِه.</para>
///
/// <para><b>وعَدُّ المُستَهلِكينَ بِصِدق (‏2026-10-25، مُراجَعَةُ تَيّارِ W8-7 الجَولَة ١؛ يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه)</b>: «مُستَوفىً قَبلَها» أَعلاه
/// يَعُدُّ <b>نُسَخَ القَرار</b> لا مُستَهلِكي الدالَّة — والاستِخراجُ (<c>9ca4e06</c>) نَزَلَ بِمُستَهلِكَينِ في وَقتِ التَشغيلِ وَحدَهُما (<c>GatedPage</c>
/// و<c>MyListings</c>)، والنُسَخُ الثَلاثُ الأُخرى بَقِيَت بِجِوارِه. فَالقاعِدَةُ ١ (ثَلاثَةُ مُستَهلِكينَ قَبلَ الاستِخراج) لَم تُستَوفَ يَومَ الاستِخراجِ
/// بَل بَعدَه بِكوميت: الثالِثُ <c>TenantListingDetail</c> في <c>3f21e19</c>، والرابِعُ <c>TenantHome</c> في <c>f5f5703</c> (تَوصيفُه <c>57af82e</c>).
/// وما زالَ <c>Me</c> و<c>TenantManage</c> نُسخَتَينِ بِيَدِهِما، وقائِمَةُ المُستَهلِكينَ يُثَبِّتُها
/// <c>CreateListingDoorTests.The_home_page_asks_the_door_with_its_target_gates_stored_reads_so_an_authored_role_sees_no_link_its_page_refuses</c>.</para>
/// </summary>
public static class PermissionDoor
{
    /// <summary>هَل يُفتَحُ البابُ لِهذا الدَور؟ — <c>RolePermissions.Has</c> عَلى دَورِ المَسارِ إن وُجِدَ وإلّا المُخَزَّن. مَتجَرٌ بِلا أَدوارٍ مَفتوح
    /// (الوَضعُ المَوروث)، ودَورٌ غائِبٌ مُغلَق. (‏2026-10-28، ‏ADR-040: مَفتوحٌ لِصَلاحِيّاتِ العُضوِ وَحدَها — الإدارِيَّةُ مُغلَقَةٌ فيه لِكُلِّ دَور.)</summary>
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
