using ACommerce.Kit.Auth;
using ACommerce.Kit.Roles;
using ACommerce.Kit.Tenants;
using ACommerce.Templates.Customer.Marketplace.Services;
using Xunit;

namespace ACommerce.Platform.Tests;

/// <summary>
/// بَوّابَة إدارَة المَتجَر — نِصفا القَرار النَّقِيّان.
///
/// <para><b>العَيب المُلتَقَط:</b> القَرار كانَ دالَّةً مَحَلِّيَّة داخِل
/// <c>MarketplaceTemplateExtensions</c>، فَحَرَسَ نِقاط الـ POST وَحدَها؛
/// وَصَفَحات Razor المُقابِلَة (‏roles، categories، branding، regions،
/// attributes، pwa، users، edit) لَم تَكُن تَراه أَصلاً وَكانَت تُصَيَّر
/// كامِلَةً لِطَلَب مَجهول. نَقلُ القَرار إلى <see cref="TenantAdminGuard"/>
/// جَعَلَ الطَّرَفَين يَقرَآن تَعريفاً واحِداً — وَهذِه الوَحَدات تَحرُس
/// أَن يَبقَى ذلكَ التَّعريف عَلى حالِه.</para>
///
/// <para>المَنطِق المُتَبَقّي في <c>CanAdministerAsync</c> هُوَ I/O مَحض
/// (‏تَحميل المَتجَر وَالمُستَخدِم مِن Marten وَقِراءَة الـ cookie)، وَحَلّ
/// الـ cookie نَفسُه مُغَطّى في
/// <see cref="AuthSessionCookieResolutionTests"/> — فَلا نُكَرِّرُه هُنا.</para>
/// </summary>
public class TenantAdminGuardTests
{
    private const string Slug = "ashare";

    private static Tenant TenantWith(Guid ownerId, params Role[] roles)
        => new() { Id = Slug, Name = "عَشير", OwnerUserId = ownerId, Roles = roles.ToList() };

    private static Role RoleWith(string slug, params string[] permissions)
        => new() { Slug = slug, Label = slug, Permissions = permissions.ToList() };

    private static User UserWith(string activeRole)
        => new() { Id = Guid.NewGuid(), TenantSlug = Slug, ActiveRole = activeRole };

    // ─── (أ) مالِك المَتجَر عَبر جَلسَة Studio ────────────────────────────

    [Fact]
    public void StudioOwner_IsAllowed()
    {
        var owner = Guid.NewGuid();
        Assert.True(TenantAdminGuard.IsStudioOwner(TenantWith(owner), owner));
    }

    [Fact]
    public void AnonymousStudioSession_IsRejected()
    {
        // لا جَلسَة studio = null. هذا هُوَ المَسار الَّذي كانَت تَسلُكُه
        // طَلَبات curl المَجهولَة الَّتي رَسَمَت الصَفَحات كامِلَةً.
        Assert.False(TenantAdminGuard.IsStudioOwner(TenantWith(Guid.NewGuid()), null));
    }

    [Fact]
    public void OtherStudioUser_IsRejected()
    {
        // مُسَجَّل دُخولاً في الاستوديو لكِنَّه لا يَملِك هذا المَتجَر.
        Assert.False(TenantAdminGuard.IsStudioOwner(TenantWith(Guid.NewGuid()), Guid.NewGuid()));
    }

    [Fact]
    public void OrphanTenant_IsNotOwnedByAnonymous()
    {
        // مَتجَر بِلا مالِك (OwnerUserId = Guid.Empty، قَبل أَن يَربِطه
        // StudioOwnershipSeeder) لا يَصير مِلكاً لِطَلَب بِلا جَلسَة.
        Assert.False(TenantAdminGuard.IsStudioOwner(TenantWith(Guid.Empty), null));
    }

    // ─── (ب) مُستَخدِم داخِل المَتجَر بِدَور يَحمِل tenant.manage ──────────

    [Fact]
    public void TenantUser_WithManagePermission_IsAllowed()
    {
        var tenant = TenantWith(Guid.NewGuid(), RoleWith("tenant_admin", "chat.respond", "tenant.manage"));
        Assert.True(TenantAdminGuard.HasTenantManage(tenant, UserWith("tenant_admin")));
    }

    [Fact]
    public void TenantUser_WithoutManagePermission_IsRejected()
    {
        var tenant = TenantWith(Guid.NewGuid(),
            RoleWith("customer", "listing.browse", "offer.submit"),
            RoleWith("tenant_admin", "tenant.manage"));
        // الدَور الفَعّال هُوَ ما يُحكَم بِه — لا مُجَرَّد وُجود دَور إداريّ
        // في المَتجَر.
        Assert.False(TenantAdminGuard.HasTenantManage(tenant, UserWith("customer")));
    }

    [Fact]
    public void UnknownUser_IsRejected()
    {
        // لا مُستَخدِم بِهذا المُعَرِّف داخِل المَتجَر (توكِن لِمَتجَر آخَر،
        // أَو مُستَخدِم مَحذوف).
        var tenant = TenantWith(Guid.NewGuid(), RoleWith("tenant_admin", "tenant.manage"));
        Assert.False(TenantAdminGuard.HasTenantManage(tenant, null));
    }

    [Fact]
    public void EmptyActiveRole_IsRejected()
    {
        var tenant = TenantWith(Guid.NewGuid(), RoleWith("tenant_admin", "tenant.manage"));
        Assert.False(TenantAdminGuard.HasTenantManage(tenant, UserWith("")));
    }

    [Fact]
    public void UnknownActiveRole_IsRejected()
    {
        // دَور فَعّال لا يُقابِلُه تَعريف في المَتجَر (بَقايا دَور مَحذوف).
        var tenant = TenantWith(Guid.NewGuid(), RoleWith("tenant_admin", "tenant.manage"));
        Assert.False(TenantAdminGuard.HasTenantManage(tenant, UserWith("ghost")));
    }

    [Fact]
    public void TenantWithNoRoles_GrantsEveryMember_LegacyMode()
    {
        // تَوصيف لا استِحسان: RolePermissions.Has تَمنَح كُلّ الصَلاحِيّات
        // لِمَتجَر بِلا أَدوار (نَمَط user-فَرد القَديم). فَأَيّ مُستَخدِم
        // مُسَجَّل دُخولاً في مِثل هذا المَتجَر يَملِك tenant.manage ضِمناً.
        //
        // ═══ تَصحيحٌ بِالقياس — ‏2026-08-31 ═══
        //
        // كانَ هُنا: «لا مَتجَر كَذلكَ في قاعِدَة البَيانات اليَوم
        // (أَقَلُّها ثَلاثَة أَدوار)». **والقياسُ كَذَّبَه**: أَربَعَةُ
        // مَتاجِرَ كَذلكَ يَومَ قيسَ، اثنانِ مِنها بَناهُما عُملاءُ
        // حَقيقِيّونَ مِنَ الاستوديو — وكُلُّ مَن سَجَّلَ فيها رَقمَ
        // هاتِفٍ كانَ مُديرَها.
        //
        // والدَرسُ هُوَ القاعِدَةُ ٢: **اختِبارٌ يَحرُسُ سَطراً
        // بِافتِراضٍ عَنِ البَياناتِ لَيسَ حارِساً** — يَلزَمُ فَحصٌ
        // يَفشَلُ إن وُجِدَ مَتجَرٌ بِصِفرِ أَدوار، لا تَعليقٌ يَظُنُّ
        // أَنَّه لا يوجَد. وذلكَ الفَحصُ صارَ في
        // `StudioBuildRoleFloorTests`، ومَنشَؤُهُ سُدَّ في
        // `TenantFromAnalysisFactory.NormalizePattern` (‏ADR-027).
        //
        // ويَبقى هذا التَوصيفُ هُنا لِأَنّ السَطرَ **لَم يُحذَف عَمداً**:
        // لَه مُستَهلِكٌ حَيٌّ مَقصود (`theme-demo` يُبذَرُ بِـ
        // `Roles = new()` لِلَقطَةِ المَظهَر)، وحَذفُهُ قَرارٌ مُنتَجِيٌّ
        // مَوقوفٌ لِصاحِبِ المَشروع.
        var tenant = TenantWith(Guid.NewGuid());
        Assert.True(TenantAdminGuard.HasTenantManage(tenant, UserWith("")));
    }
}
