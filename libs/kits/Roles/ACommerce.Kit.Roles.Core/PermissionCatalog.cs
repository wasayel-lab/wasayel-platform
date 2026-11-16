namespace ACommerce.Kit.Roles;

/// <summary>
/// <para><b>مَعجَم الصَلاحِيّات المُغلَق — مَوضِع واحِد</b>. كانَت
/// الصَلاحِيّات سَلاسِل نَصِّيَّة مَنثورَة: تُكتَب في قَوالِب الأَدوار،
/// وتُفحَص في البَوّابات (<c>RequirePermission</c>, <c>GatedPage</c>,
/// <c>PermissionFilter</c>, <c>GatePipeline</c>) وفي الصَفحات
/// (<c>Me</c>, <c>TenantHome</c>, <c>TenantManage</c>) — ولا شَيء
/// يَربِط الطَرَفَين، فَخَطأ إملائيّ واحِد يُنتِج صَلاحِيَّة لا يَملِكُها
/// أَحَد ولا يَشتَكي مِنها مُتَرجِم.</para>
///
/// <para>هذا المَعجَم يُغلِق الحَلقَة مِن جِهَة <b>التَّعريف</b>:
/// <see cref="RoleDefinitionValidator"/> يَرفُض أَيّ تَعريف دَور يَمنَح
/// صَلاحِيَّة خارِج هذه القائِمَة، فَلا يَدخُل الكاتالوج مِفتاح مَجهول.</para>
///
/// <para><b>ما لا يَفعَلُه، مُعلَناً</b>: لا يَفحَص <i>مَواضِع الفَحص</i>.
/// <c>RequirePermission("…")</c> يَقبَل أَيّ سِلسِلَة اليَوم كَما كانَ —
/// إغلاق ذلك الطَرَف تَغيير سُلوكيّ (رَفض عِند التَّشغيل أَو عِند البِناء)
/// وهذه المَوجَة شَرطُها ألّا يَتَغَيَّر السُلوك بَتّاً. الطَرَف الآخَر
/// مَوجَة لاحِقَة.</para>
///
/// <para><b>وما ليسَ مِنه، بِقَصد</b>:</para>
/// <list type="bullet">
///   <item><c>offer.accept</c> — يَظهَر مِثالاً في تَعليق XML عَلى
///   <c>GateExtensions.RequirePermission</c> فَقَط. لا يَمنَحُه قالِب،
///   ولا يَفحَصُه مَسار. مِثال تَوضيحيّ لا صَلاحِيَّة قائِمَة.</item>
///   <item><c>tenant.roles_save</c>, <c>tenant.branding_save</c>,
///   <c>tenant.suspend</c>, <c>user.grant_admin</c>, <c>deal.advance</c>,
///   <c>deal.cancel</c>, <c>deal.dispute</c>, <c>listing.{action}</c> —
///   هذه <b>أَسماء إجراءات في سِجِلّ التَّدقيق</b> (Audit) لا صَلاحِيّات
///   أَدوار. تُشبِهُها شَكلاً وتَختَلِف عَنها مَوضِعاً ومَعنىً، وخَلطُهما
///   كانَ الخَطَر الفِعليّ الَّذي أَغلَقَه هذا المَعجَم.</item>
/// </list>
/// </summary>
public static class PermissionCatalog
{
    /// <summary>الصَلاحِيّات الثَّمانِ — كُلّ ما يَمنَحُه قالِب دَور اليَوم،
    /// مُرَتَّبَة أَبجَدِيّاً. الزِّيادَة عَلَيها قَرار مُعلَن: سَطر هُنا
    /// + سَطر في التَّعريف الَّذي يَمنَحُها.</summary>
    public static readonly IReadOnlyList<string> All = new[]
    {
        "chat.respond",
        "chat.start",
        "listing.browse",
        "listing.create",
        "listing.delete",
        "listing.edit",
        "offer.submit",
        "tenant.manage",
    };

    private static readonly HashSet<string> Set = new(All, StringComparer.Ordinal);

    /// <summary>
    /// <para><b>الصَلاحِيّاتُ الإدارِيَّة — ما لا يُعطيه مَتجَرٌ بِلا أَدوارٍ لِأَحَد</b> (‏2026-10-28، ‏ADR-040، قَرارُ المالِك د62). والمِعيارُ
    /// مَقيسٌ لا مُسَمّى: صَلاحِيَّةٌ يَمنَحُها في الكاتالوجِ دَورُ <c>tenant_admin</c> وَحدَه — اليَومَ <c>tenant.manage</c> وَحدَها (تَفتَحُ
    /// صَفَحاتِ <c>/admin/tenants/{slug}/*</c> وقائِمَةَ المُستَخدِمينَ بِهَواتِفِهِم). والسَبعُ الباقِيَةُ يَحمِلُها دَورُ عُضوٍ واحِدٌ عَلى الأَقَلّ
    /// فَهي صَلاحِيّاتُ عُضو. و<c>RoleCatalogAdministrativeTests</c> يَحمَرُّ إن مَنَحَ الكاتالوجُ صَلاحِيَّةً لِـ<c>tenant_admin</c> وَحدَه
    /// ولَم تُسَمَّ هُنا، أَو سُمِّيَت هُنا ويَحمِلُها دَورُ عُضو.</para>
    /// </summary>
    public static readonly IReadOnlyList<string> Administrative = new[]
    {
        "tenant.manage",
    };

    private static readonly HashSet<string> AdministrativeSet = new(Administrative, StringComparer.Ordinal);

    /// <summary>هَل هذه الصَلاحِيَّةُ إدارِيَّة؟ (‏ADR-040) — المُقارَنَةُ حَسّاسَةٌ لِلحالَةِ كَـ<see cref="Contains"/>.</summary>
    public static bool IsAdministrative(string permission) => AdministrativeSet.Contains(permission);

    /// <summary>هَل هذه السِلسِلَة صَلاحِيَّة مُعتَرَف بِها؟ المُقارَنَة
    /// <b>حَسّاسَة لِلحالَة</b> (Ordinal) كَما هي في <see cref="RolePermissions.Has"/>.</summary>
    public static bool Contains(string permission) => Set.Contains(permission);
}
