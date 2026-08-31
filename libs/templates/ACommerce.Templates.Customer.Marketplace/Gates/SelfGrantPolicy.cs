using ACommerce.Kit.Roles;

namespace ACommerce.Templates.Customer.Marketplace.Gates;

/// <summary>
/// <para><b>أَيُّ دَورٍ يُسَكِّنُه المُستَخدِمُ لِنَفسِه؟</b> دالَّتانِ
/// نَقِيَّتانِ ورَمزُ خَرقٍ ثابِت — بِلا قاعِدَةِ بَياناتٍ ولا HTTP ولا
/// وَقت. نَفسُ شَكلِ المُصادِقاتِ في هذا المُستودَع (القاعِدَة ٤):
/// اختِيارٌ مِن مَجموعَةٍ مُنتَهِيَة، ورَمزٌ ثابِتٌ لِلاختِبارِ
/// واللوغ.</para>
///
/// <para><b>لِماذا وُجِدَت — ولَم تُخترَع</b>: القاعِدَةُ نَفسُها كانَت
/// مَكتوبَةً في المُستودَعِ <b>أَربَعَ مَرّاتٍ بِأَربَعِ صِيَغ</b>:
/// <c>picked.CatalogSlug == "tenant_admin"</c> في
/// <c>POST /{slug}/me/role/save</c>، و<c>roleSlug == "tenant_admin"</c>
/// في <c>AssignRoleAsync</c>، و<c>IsAdminRole(catalogSlug)</c> خاصَّةً
/// بِـ<c>RolePicker.razor</c>، و<c>AdminRole</c> خاصَّةً
/// بِـ<see cref="EffectiveRole"/>. وأَربَعُ نُسَخٍ لِقاعِدَةٍ واحِدَةٍ
/// تَعني أَنَّ <b>مَوضِعاً خامِساً سَيُكتَبُ بِلا نُسخَة</b> — وهُوَ
/// ما وَقَعَ حَرفاً في <c>POST /{slug}/me/save</c>. فَهذا استِخراجٌ
/// بَعدَ أَربَعَةِ مُستَهلِكينَ لا قَبلَهُم (القاعِدَة ١).</para>
///
/// <para><b>الكِلفَةُ المَقيسَة (‏2026-08-31)</b>: عُضوٌ عادِيٌّ رَفَعَ
/// نَفسَه إلى <c>tenant_admin</c> بِطَلَبٍ واحِدٍ إلى
/// <c>/me/save</c>، ثُمَّ قَرَأَ صَفحَةَ الأَعضاءِ وفيها رَقمُ هاتِف،
/// وكَتَبَ في هُوِيَّةِ المَتجَرِ بِنَجاح. والأَثَرُ يَطالُ <b>كُلَّ
/// مَتجَرٍ يُعَرِّفُ <c>tenant_admin</c></b>. القَرارُ:
/// <c>docs/ADR-028-THE-ADMIN-ROLE-IS-NEVER-SELF-GRANTED.md</c>.</para>
///
/// <para><b>ولِماذا السلاجانِ مَعاً</b>: الأُختانِ اختَلَفَتا في اسمِ
/// الحَقلِ لا في القَرار — إحداهُما تَفحَصُ <c>CatalogSlug</c> والأُخرى
/// <c>Slug</c>. والاتِّحادُ بَينَهُما لا يَمنَعُ دَوراً مَشروعاً واحِداً:
/// <c>RoleCatalog.InstantiateRole</c> يَضَعُ الحَقلَينِ مِن نَفسِ
/// القالِب، ووَثيقَةُ مُستَأجِرٍ تُظَلِّلُ سلاجَ الكاتالوجِ تُرفَضُ
/// عِندَ المُصادَقَة (<c>slug_shadows_platform_catalog</c>). فَهُوَ
/// جَمعُ الحارِسَينِ القائِمَينِ لا حارِسٌ ثالِثٌ يُختَرَع.</para>
///
/// <para><b>وما لا تَقولُه</b>: لا تَعرِفُ الصَلاحِيّات. دَورٌ يَحمِلُ
/// <c>tenant.manage</c> وسلاجُه غَيرُ إداريٍّ يَمُرُّ مِن هُنا — وذاكَ
/// تَعريفٌ يَختارُه صاحِبُ المَتجَرِ لِمَتجَرِه، ومَنعُه قَرارُ مُنتَجٍ
/// لا حارِسُ ثَغرَة. مُدَوَّنٌ في ‏ADR-028 §«ما لَم يُغلَق».</para>
/// </summary>
public static class SelfGrantPolicy
{
    /// <summary>الدَورُ الوَحيدُ غَيرُ القابِلِ لِلاختِيارِ الذاتيّ —
    /// يُمنَحُ مِن <c>/admin/tenants/{slug}/users</c> بِفِعلٍ إداريٍّ
    /// مُسَجَّلٍ في التَدقيق، أَو مِن بَذرَةِ قاعِدَةِ بَيانات.</summary>
    public const string AdminSlug = "tenant_admin";

    /// <summary>رَمزُ الخَرقِ الثابِت — نَفسُ الرَمزِ الَّذي تَرُدُّ بِه
    /// الأُختُ <c>POST /{slug}/me/role/save</c> مُنذُ كُتِبَت.</summary>
    public const string RefusalCode = "admin_self_grant";

    /// <summary>سلاجٌ خامٌّ كَما وَصَلَ مِن الاستِمارَةِ أَو الـURL —
    /// يُفحَصُ <b>قَبلَ</b> أَيِّ تَحميلٍ مِن القاعِدَة، فَتَعَذُّرُ
    /// تَحميلِ المُستَأجِرِ لا يَصيرُ ثَغرَة.</summary>
    public static bool IsAdminSlug(string? slug) =>
        string.Equals(slug, AdminSlug, StringComparison.Ordinal);

    /// <summary>دَورٌ مُحَمَّل — إداريٌّ بِأَيِّ اسمَيه.</summary>
    public static bool IsAdminRole(Role? role) =>
        role is not null && (IsAdminSlug(role.CatalogSlug) || IsAdminSlug(role.Slug));

    /// <summary>القَرارُ الكامِلُ عِندَ نُقطَةِ كِتابَة: السلاجُ الخامُّ
    /// كَما طُلِب، والدَورُ المُحَمَّلُ إن وُجِد. <c>true</c> = تُرَدُّ
    /// بِـ<see cref="RefusalCode"/> ولا تُكتَب.</summary>
    public static bool RefusesSelfGrant(string? requestedSlug, Role? resolved) =>
        IsAdminSlug(requestedSlug) || IsAdminRole(resolved);

    /// <summary>ما يُعرَضُ في شاشَةِ اختِيارِ دَور. <b>الشاشَةُ
    /// والنُقطَةُ يُنادِيانِ نَفسَ السِياسَة</b> — فَحارِسٌ بِلا إخفاءٍ
    /// يَترُكُ الخِيارَ مُغرِياً، وإخفاءٌ بِلا حارِسٍ لا يَمنَعُ
    /// <c>curl</c>.</summary>
    public static IEnumerable<Role> SelfGrantable(IEnumerable<Role> roles) =>
        roles.Where(r => !IsAdminRole(r));
}
