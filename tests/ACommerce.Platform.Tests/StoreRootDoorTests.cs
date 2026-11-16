using System.Text.RegularExpressions;
using ACommerce.Kit.Roles;
using ACommerce.Templates.Customer.Marketplace.Gates;
using Xunit;

namespace ACommerce.Platform.Tests;

// ═══ جَذرُ المَتجَرِ لِلزائِر: البِضاعَةُ أَوَّلاً حينَ يَكونُ في المَتجَرِ دَورُ مُشتَرٍ (قَرارُ المالِك د34، ‏ADR-048) ═══
//
// `StoreRootDoor.GoodsFirst` دالَّةٌ نَقِيَّة — لِكُلِّ شَرطٍ فيها صَفٌّ مُوجَبٌ وصَفٌّ سالِب — ومُستَهلِكاها `TenantHome` (أَيُّ فَرعٍ يُرسَم)
// و`MainLayout` (الشَريط). والتَوصيفُ السُلوكِيُّ قَبلَ التَبديلِ وبَعدَه عَلى اللَقَطاتِ الحَيَّة: `StoreRootVisitorCharacterizationTests`.

public class StoreRootDoorTests
{
    private static Role Catalog(string slug)
        => RoleCatalog.InstantiateRole(RoleCatalog.Find(slug) ?? throw new InvalidOperationException($"دَورٌ مَفقودٌ في الكاتالوج: {slug}"));

    private static IReadOnlyCollection<Role> Roles(params string[] slugs) => slugs.Select(Catalog).ToList();

    private static readonly TenantRoleSet Set = TenantRoleSet.Platform;

    // ─── المُوجَب ─────────────────────────────────────────────────────────

    /// <summary>مُوجَب: أَدوارُ <c>ashare</c> (‏<c>TestDataSeeder.cs:29</c>) — زائِرٌ غَيرُ مُسَجَّلٍ عَلى الجَذر يَرى البِضاعَةَ أَوَّلاً.</summary>
    [Fact]
    public void A_visitor_of_the_root_of_a_store_with_a_customer_role_sees_the_goods_first()
    {
        Assert.True(StoreRootDoor.GoodsFirst(Roles("customer", "host", "vendor"), Set, authenticated: false, urlRole: null, memberRole: null));
        Assert.True(StoreRootDoor.GoodsFirst(Roles("customer", "host", "vendor"), Set, authenticated: false, urlRole: "", memberRole: null));
        // ومَتجَرُ الاستوديو بِسُقوطِ `RolesFor` (‏customer وvendor وtenant_admin) كَذلك.
        Assert.True(StoreRootDoor.GoodsFirst(Roles("customer", "vendor", "tenant_admin"), Set, false, null, memberRole: null));
    }

    /// <summary>مُوجَب: «دَورُ المُشتَري» يُقرَأُ مِن التَركيبِ لا مِن الاسم — دَورٌ مُخَصَّصٌ بِلا كاتالوج (تَركيبُه السُقوطُ الافتِراضِيّ،
    /// ‏<c>RoleCompositionResolver.Fallback</c>) يَتَصَفَّحُ يُعَدُّ مُشتَرِياً.</summary>
    [Fact]
    public void A_custom_role_whose_home_is_the_default_and_that_browses_counts_as_a_buyer()
    {
        var custom = new Role { Slug = "visitor_plus", Label = "زائِر", CatalogSlug = "", Permissions = { "listing.browse" } };
        Assert.True(StoreRootDoor.IsBuyerRole(custom, Set));
        Assert.True(StoreRootDoor.GoodsFirst(new[] { custom, Catalog("vendor") }, Set, false, null, memberRole: null));
    }

    // ─── السالِب ─────────────────────────────────────────────────────────

    /// <summary>سالِب: مَتجَرٌ بِلا دَورِ مُشتَرٍ يَبقى عَلى البَوّابَة — <c>injez</c> (‏rider وdriver) ومَتجَرُ بائِعينَ وَحدَهُم (host وvendor).</summary>
    [Fact]
    public void A_store_without_a_buyer_role_keeps_the_gateway()
    {
        Assert.False(StoreRootDoor.GoodsFirst(Roles("rider", "driver"), Set, false, null, memberRole: null));
        Assert.False(StoreRootDoor.GoodsFirst(Roles("host", "vendor"), Set, false, null, memberRole: null));
        Assert.False(StoreRootDoor.GoodsFirst(Roles("broker", "organizer", "mover", "shipper"), Set, false, null, memberRole: null));
    }

    /// <summary>سالِب: دَورُ الإدارَةِ رَئيسِيَّتُه الافتِراضِيَّةُ ويَتَصَفَّح — لكِنَّ البَوّابَةَ لا تَرسُمُه بِطاقَةً، فَلا يُعَدّ.</summary>
    [Fact]
    public void The_admin_role_is_not_a_buyer_although_its_home_is_the_default_and_it_browses()
    {
        var admin = Catalog("tenant_admin");
        Assert.Contains("listing.browse", admin.Permissions);
        Assert.Equal(RoleComponents.DefaultHome, Set.ResolveComposition(admin.CatalogSlug).Home);

        Assert.False(StoreRootDoor.IsBuyerRole(admin, Set));
        Assert.False(StoreRootDoor.GoodsFirst(new[] { admin }, Set, false, null, memberRole: null));
        Assert.False(StoreRootDoor.GoodsFirst(new[] { admin, Catalog("vendor") }, Set, false, null, memberRole: null));
    }

    /// <summary>سالِب: التَركيبُ الافتِراضِيُّ وَحدَه لا يَكفي — دَورٌ مُخَصَّصٌ بِلا <c>listing.browse</c> لا يُعَدُّ مُشتَرِياً.</summary>
    [Fact]
    public void A_role_with_the_default_home_that_does_not_browse_is_not_a_buyer()
    {
        var publisher = new Role { Slug = "poster", Label = "ناشِر", CatalogSlug = "", Permissions = { "listing.create" } };
        Assert.False(StoreRootDoor.IsBuyerRole(publisher, Set));
        Assert.False(StoreRootDoor.GoodsFirst(new[] { publisher }, Set, false, null, memberRole: null));
    }

    /// <summary>
    /// <para><b>مَقلوبٌ بِاسمِه ‏2026-10-30 (تَدقيقُ الموجَة ١١ CO5)</b> — وكانَ <c>A_signed_in_member_is_not_asked</c>: «المُسَجَّلُ لا يَسأَلُ هذا الباب،
    /// وبِلا دَورٍ يَبقى عَلى البَوّابَة». <b>والآن</b> المُسَجَّلُ الَّذي لا يُسَمّي دَورُه الفَعّالُ دَوراً مِن أَدوارِ المَتجَرِ — لا دَورَ، أَو دَورٌ زالَ مِن
    /// المَتجَر — يَرى البِضاعَةَ أَوَّلاً كَالزائِر.</para>
    /// </summary>
    [Fact]
    public void A_signed_in_member_without_an_active_role_in_the_store_sees_the_goods_first()
    {
        Assert.True(StoreRootDoor.GoodsFirst(Roles("customer", "host", "vendor"), Set, authenticated: true, urlRole: null, memberRole: null));
        Assert.True(StoreRootDoor.GoodsFirst(Roles("customer", "host", "vendor"), Set, authenticated: true, urlRole: null, memberRole: ""));
        Assert.True(StoreRootDoor.GoodsFirst(Roles("customer", "host", "vendor"), Set, authenticated: true, urlRole: null, memberRole: "rider"));
    }

    /// <summary>سالِب: المُسَجَّلُ بِدَورٍ فَعّالٍ يَحكُمُه دَورُه — دَورُ بائِعٍ (رَئيسِيَّةُ البائِع)، ودَورُ المُشتَري نَفسُه (رَئيسِيَّتُه بِلا قِسمِ البِطاقات)،
    /// مِن المَسارِ كانَ أَو مِن المُخَزَّن؛ ومَتجَرٌ بِلا دَورِ مُشتَرٍ يُبقي مُسَجَّلَه بِلا دَورٍ عَلى البَوّابَة.</summary>
    [Fact]
    public void A_signed_in_member_with_an_active_role_is_ruled_by_it()
    {
        foreach (var role in new[] { "host", "vendor", "customer" })
        {
            Assert.False(StoreRootDoor.GoodsFirst(Roles("customer", "host", "vendor"), Set, authenticated: true, urlRole: null, memberRole: role));
            Assert.False(StoreRootDoor.GoodsFirst(Roles("customer", "host", "vendor"), Set, authenticated: true, urlRole: role, memberRole: role));
        }
        Assert.False(StoreRootDoor.GoodsFirst(Roles("rider", "driver"), Set, authenticated: true, urlRole: null, memberRole: null));
    }

    /// <summary>
    /// <para><b>مَقلوبٌ بِاسمِه ‏2026-10-30 (تَدقيقُ الموجَة ١١ CO5)</b> — وكانَ <c>A_role_app_path_keeps_its_gateway_even_for_the_buyer_role</c>.
    /// <b>والآن</b> تَطبيقُ دَورِ المُشتَري مَفتوحاً بِلا جَلسَة (<c>/{slug}/r/customer</c> — <c>start_url</c> تَطبيقِ المُشتَري المُثَبَّت) يَرى البِضاعَةَ
    /// أَوَّلاً، وتَطبيقُ دَورٍ سِواه (سالِب) يَبقى عَلى بَوّابَتِه، ودَورٌ لا يَعرِفُه المَتجَرُ كَذلك.</para>
    /// </summary>
    [Fact]
    public void A_role_app_path_keeps_its_gateway_except_the_buyer_role()
    {
        Assert.True(StoreRootDoor.GoodsFirst(Roles("customer", "host", "vendor"), Set, false, "customer", memberRole: null));
        foreach (var role in new[] { "host", "vendor", "rider", "nobody" })
            Assert.False(StoreRootDoor.GoodsFirst(Roles("customer", "host", "vendor"), Set, false, role, memberRole: null));
        // ‏ومَتجَرٌ بِلا دَورِ مُشتَرٍ لا يَفتَحُه مَسار.
        Assert.False(StoreRootDoor.GoodsFirst(Roles("rider", "driver"), Set, false, "rider", memberRole: null));
    }

    /// <summary>سالِب: مَتجَرٌ بِلا أَدوارٍ لا يَسأَلُه — يَرى رَئيسِيَّةَ المُشتَري أَصلاً (الفَرعُ الافتِراضِيّ) ولا بَوّابَةَ ولا قِسمَ بِطاقات.</summary>
    [Fact]
    public void A_store_without_roles_is_not_asked()
        => Assert.False(StoreRootDoor.GoodsFirst(Array.Empty<Role>(), Set, false, null, memberRole: null));

    // ─── المُستَهلِكان ────────────────────────────────────────────────────

    /// <summary>
    /// <para><b>مُستَهلِكانِ لا ثالِثَ لَهُما</b> (القاعِدَة ٨): <c>TenantHome.razor</c> و<c>MainLayout.razor</c> — كُلٌّ بِنِداءٍ واحِدٍ يُمَرِّرُ
    /// الأَدوارَ المُجَسَّدَةَ ولَقطَةَ الأَدوارِ وحالَ الجَلسَةِ ودَورَ المَسار. وقِسمُ البِطاقاتِ يُرسَمُ تَحتَ البِضاعَةِ مَرَّةً واحِدَة.</para>
    /// </summary>
    [Fact]
    public void Two_consumers_ask_the_door_the_home_page_and_its_shell()
    {
        var components = Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, "libs", "templates",
            "ACommerce.Templates.Customer.Marketplace", "Components");
        var razors = Directory.GetFiles(components, "*.razor", SearchOption.AllDirectories);
        Assert.True(razors.Length > 50, $"أَداةٌ عَمياء: {razors.Length} مِلَفّاً فَقَط.");

        var callers = razors.Where(f => File.ReadAllText(f).Contains("StoreRootDoor.GoodsFirst(", StringComparison.Ordinal))
                            .Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        Assert.Equal(new[] { "MainLayout.razor", "TenantHome.razor" }, callers);

        var home = File.ReadAllText(Path.Combine(components, "Pages", "TenantHome.razor"));
        Assert.Single(Regex.Matches(home, Regex.Escape("StoreRootDoor.GoodsFirst(")));
        Assert.Single(Regex.Matches(home, Regex.Escape("BelowGoods=\"true\"")));

        var layout = File.ReadAllText(Path.Combine(components, "Layout", "MainLayout.razor"));
        Assert.Single(Regex.Matches(layout, Regex.Escape("StoreRootDoor.GoodsFirst(tenantRoles, roleSet, Auth.IsAuthenticated, urlRole, activeRoleSlug)")));
    }
}
