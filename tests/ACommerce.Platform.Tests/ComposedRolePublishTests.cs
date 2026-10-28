using ACommerce.Kit.Roles;
using ACommerce.Templates.Customer.Marketplace.Gates;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ─── د42: دَورٌ مُؤَلَّفٌ مُعتَمَدٌ يَقولُ «يَنشُر» — أَيَنشُر؟ ─────────────────────────────
//
// قَرارُ المالِك د42 «أ» (‏2026-10-28): «حارِسا النَشرِ يَقرَآنِ الأَدوارَ نَفسَها الَّتي تَقرَؤُها بَقِيَّةُ المَنَصَّة، ومِنها الأَدوارُ المُؤَلَّفَةُ المُعتَمَدَة.
// فَالدَورُ الَّذي يَقولُ «يَنشُر» يَنشُر، ويَعودُ لَه بابُ النَشرِ في الرَئيسِيَّة.» وتَنفيذُه: «اختِبارٌ يُثَبِّتُ أَنَّ الدَورَ المُؤَلَّفَ يُرفَضُ اليَوم، ثُمَّ…».
//
// وهذا المِلَفُّ ذلكَ الاختِبار: صَفٌّ دائِمٌ يُسَمّي الفَجوَةَ بِالأَرقام (المُخَزَّنَةُ وَحدَها تُغلِق، والمُجَسَّدَةُ تَفتَح)، وصَفّا «اليَوم» لِقِراءَتَي
// الحارِسَين (‏<c>PermissionFilter</c> لِنُقطَةِ <c>POST /{slug}/listings/create</c>، و<c>GatedPage</c> لِصَفحَةِ النَشر)، وبُرهانٌ حَيٌّ عَلى Postgres
// يُشَغِّلُ المُرَشِّحَ نَفسَه لِعُضوٍ دَورُه «خَيّاط» المُؤَلَّفُ المُعتَمَد. وكُلُّ «Today_» يُقلَبُ بِاسمِه في كوميتِ التَبديل.

public class ComposedRolePublishTests(ITestOutputHelper output)
{
    internal const string Permission = "listing.create";
    internal const string AuthoredSlug = "khayyat";

    private static string TemplateRoot => Path.Combine(ThemeZeroEquivalenceTests.RepoRoot,
        "libs", "templates", "ACommerce.Templates.Customer.Marketplace");

    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine(new[] { TemplateRoot }.Concat(parts).ToArray()));

    /// <summary>أَدوارُ مَتجَرٍ مُخَزَّنَةٌ مِن الكاتالوج (‏<c>customer</c> و<c>vendor</c>) ودَورٌ مُؤَلَّفٌ مُعتَمَدٌ يَحمِلُ <c>listing.create</c> — «خَيّاط»
    /// (<see cref="TenantRoleDefinitionToolTests.KhayyatJson"/>) — مُجَسَّداً فَوقَها كَما يُجَسِّدُه <c>TenantRoleService.LoadAsync</c>
    /// و<c>LoadTenantWithRolesAsync</c>.</summary>
    internal static (IReadOnlyList<Role> Stored, IReadOnlyList<Role> Materialized) StoreWithAuthoredMaker()
    {
        var stored = new[] { "customer", "vendor" }
            .Select((s, i) => RoleCatalog.InstantiateRole(RoleCatalog.Find(s)!, i)).ToList();
        var set = TenantRoleSet.FromDocuments("adwar-demo", new[]
        {
            new TenantRoleDefinition
            {
                Id = AuthoredSlug, Slug = AuthoredSlug, DefinitionJson = TenantRoleDefinitionToolTests.KhayyatJson,
                Status = TenantRoleStatuses.Approved, CreatedBy = "agent:define_role",
                CreatedAt = new DateTime(2026, 8, 10, 0, 0, 0, DateTimeKind.Utc),
            },
        });
        return (stored, set.Materialize(stored));
    }

    /// <summary><b>الفَجوَةُ بِالأَرقام — صَفٌّ دائِمٌ لا يَتَبَدَّل</b>: «خَيّاط» غائِبٌ عَن الأَدوارِ المُخَزَّنَةِ فَالبابُ عَلَيها مُغلَقٌ لَه، وحاضِرٌ في
    /// المُجَسَّدَةِ بِـ<c>listing.create</c> فَالبابُ عَلَيها مَفتوح؛ والبائِعُ مَفتوحٌ عَلى الاثنَتَين، والمُشتَري مُغلَقٌ عَلى الاثنَتَين. فَالجَوابُ يَتبَعُ
    /// القِراءَةَ وَحدَها — والقَرارُ أَيُّ قِراءَةٍ يَقرَأُ الحارِس.</summary>
    [Fact]
    public void An_approved_authored_role_carrying_listing_create_opens_the_door_on_the_materialized_roles_and_not_on_the_stored_ones()
    {
        var (stored, materialized) = StoreWithAuthoredMaker();
        var authored = materialized.Single(r => r.Slug == AuthoredSlug);
        output.WriteLine($"مُخَزَّنَة: {string.Join(", ", stored.Select(r => r.Slug))} · مُجَسَّدَة: {string.Join(", ", materialized.Select(r => r.Slug))} · "
            + $"خَيّاط يَحمِل: {string.Join(", ", authored.Permissions)}");

        Assert.Equal(stored.Count + 1, materialized.Count);
        Assert.DoesNotContain(stored, r => r.Slug == AuthoredSlug);
        Assert.Contains(Permission, authored.Permissions);

        Assert.False(PermissionDoor.Opens(stored, null, AuthoredSlug, Permission));
        Assert.True(PermissionDoor.Opens(materialized, null, AuthoredSlug, Permission));
        Assert.True(PermissionDoor.Opens(stored, null, "vendor", Permission));
        Assert.True(PermissionDoor.Opens(materialized, null, "vendor", Permission));
        Assert.False(PermissionDoor.Opens(stored, null, "customer", Permission));
        Assert.False(PermissionDoor.Opens(materialized, null, "customer", Permission));
        // ودَورُ المَسارِ يَتَفَوَّقُ كَما في الحارِس.
        Assert.True(PermissionDoor.Opens(materialized, AuthoredSlug, "customer", Permission));
        Assert.False(PermissionDoor.Opens(stored, AuthoredSlug, "customer", Permission));
    }

    /// <summary><b>اليَوم</b>: مُرَشِّحُ الصَلاحِيَّةِ (حارِسُ <c>POST /{slug}/listings/create</c> بِـ<c>.RequirePermission("listing.create")</c>) يُحَمِّلُ
    /// وَثيقَةَ <c>Tenant</c> بِجَلسَةٍ عامَّةٍ ويَسأَلُ <c>RolePermissions.Has</c> عَلى <c>tenant.Roles</c> المُخَزَّنَة — ولا يُجَسِّدُ دَوراً مُؤَلَّفاً.</summary>
    [Fact]
    public void Today_the_publish_filter_reads_the_stored_tenant_roles_alone()
    {
        var filter = WriteEndpointGuardTests.StripComments(Read("Gates", "PermissionFilter.cs"));
        Assert.Contains("var tenant = await g.LoadAsync<Tenant>(slug);", filter, StringComparison.Ordinal);
        Assert.Contains("RolePermissions.Has(tenant.Roles, effectiveRole, _permission)", filter, StringComparison.Ordinal);
        Assert.DoesNotContain("Materialize", filter, StringComparison.Ordinal);
        Assert.DoesNotContain("LoadTenantWithRolesAsync", filter, StringComparison.Ordinal);
        Assert.DoesNotContain("TenantRoleService", filter, StringComparison.Ordinal);
    }

    /// <summary><b>اليَوم</b>: صَفحَةُ النَشرِ (<c>CreateListing</c> داخِلَ <c>GatedPage Permission="listing.create"</c>) تَقرَأُ المُستَأجِرَ مِن
    /// <c>TenantDirectory.FindAsync</c> — الوَثيقَةُ المُخَزَّنَة — ولا تَسأَلُ <c>TenantRoleService</c>.</summary>
    [Fact]
    public void Today_the_publish_page_gate_reads_the_stored_tenant_through_the_directory()
    {
        var gate = WriteEndpointGuardTests.StripMarkupComments(Read("Components", "GatedPage.razor"));
        Assert.Contains("var tenant = await Tenants.FindAsync(Slug);", gate, StringComparison.Ordinal);
        Assert.Contains("PermissionDoor.Opens(tenant.Roles, Role, storedRole, Permission)", gate, StringComparison.Ordinal);
        Assert.DoesNotContain("TenantRoleService", gate, StringComparison.Ordinal);
    }
}

/// <summary>
/// <para><b>بُرهانٌ حَيٌّ عَلى Postgres — المُرَشِّحُ نَفسُه</b>: مَتجَرٌ بِدَورَينِ مِن الكاتالوج، وتَعريفُ «خَيّاط» مُعتَمَدٌ في وَثائِقِه، وعُضوٌ دَورُه
/// النَشِطُ «خَيّاط». يُشَغَّلُ <see cref="PermissionFilter"/> بِـ<c>listing.create</c> عَلى طَلَبٍ يَحمِلُ هُوِيَّتَه، ويُطبَعُ مَعَه ما يَقرَؤُه
/// <c>TenantRoleService.ReadUncachedAsync</c> لِلمَتجَرِ نَفسِه — أَي أَنَّ بَقِيَّةَ المَنَصَّةِ تَرى الدَورَ ناشِراً.</para>
/// <para>يَتَخَطّى نَفسَه ما لَم تُضبَط <c>WASAYEL_LIVE_PROOF=1</c> و<c>ConnectionStrings__Postgres</c>، ويُنشِئُ مَتجَراً عابِراً ويَحذِفُه.</para>
/// </summary>
[Collection(LiveProofCollection.Name)]
public class LiveComposedRolePublishProofTests(ITestOutputHelper output)
{
    private static bool Enabled
        => LiveDealConcurrencyProofTests.Enabled && !string.IsNullOrEmpty(LiveDealConcurrencyProofTests.ConnectionString);

    /// <summary>يَبني المَتجَرَ والتَعريفَ والعُضو، ويُشَغِّلُ المُرَشِّح — ويُعيدُ: أَمَرَّ إلى النُقطَة، ورَمزَ الرَدِّ إن رَدّ، وهَل في الأَدوارِ المُجَسَّدَةِ «خَيّاط» بِالصَلاحِيَّة.</summary>
    internal static async Task<(bool Passed, int? Status, bool PlatformSeesPublisher)> AuthoredMemberAsksThePublishFilterAsync(
        Marten.IDocumentStore store, string slug)
    {
        var (stored, _) = ComposedRolePublishTests.StoreWithAuthoredMaker();
        var userId = Guid.NewGuid();
        await using (var g = store.LightweightSession())
        {
            g.Store(new ACommerce.Kit.Tenants.Tenant { Id = slug, Name = "مَتجَرٌ بِدَورٍ مُؤَلَّف", OwnerUserId = Guid.NewGuid(), Roles = stored.ToList() });
            await g.SaveChangesAsync();
        }
        await using (var s = store.LightweightSession(slug))
        {
            s.Store(new TenantRoleDefinition
            {
                Id = ComposedRolePublishTests.AuthoredSlug, Slug = ComposedRolePublishTests.AuthoredSlug,
                DefinitionJson = TenantRoleDefinitionToolTests.KhayyatJson, Status = TenantRoleStatuses.Approved,
                CreatedBy = "live-proof", CreatedAt = DateTime.UtcNow, DecidedBy = "live-proof", DecidedAt = DateTime.UtcNow,
            });
            s.Store(new ACommerce.Kit.Auth.User { Id = userId, Phone = "966500000042", ActiveRole = ComposedRolePublishTests.AuthoredSlug });
            await s.SaveChangesAsync();
        }

        var set = await ACommerce.Templates.Customer.Marketplace.Services.TenantRoleService.ReadUncachedAsync(store, slug);
        var sees = set.Materialize(stored).Any(r => r.Slug == ComposedRolePublishTests.AuthoredSlug
                                                   && r.Permissions.Contains(ComposedRolePublishTests.Permission));

        var http = new Microsoft.AspNetCore.Http.DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddSingleton(store).BuildServiceProvider(),
        };
        http.Request.Method = "POST";
        http.Request.Path = $"/{slug}/listings/create";
        http.Items[GateKeys.SlugItem] = slug;
        http.Items[GateKeys.UserId] = userId;
        var passed = false;
        var result = await new PermissionFilter(ComposedRolePublishTests.Permission).InvokeAsync(
            new Microsoft.AspNetCore.Http.DefaultEndpointFilterInvocationContext(http),
            _ => { passed = true; return ValueTask.FromResult<object?>(Microsoft.AspNetCore.Http.Results.Ok()); });
        return (passed, (result as Microsoft.AspNetCore.Http.IStatusCodeHttpResult)?.StatusCode, sees);
    }

    internal static async Task CleanAsync(Marten.IDocumentStore store, string slug)
    {
        await using (var s = store.LightweightSession(slug))
        {
            s.Delete<TenantRoleDefinition>(ComposedRolePublishTests.AuthoredSlug);
            await s.SaveChangesAsync();
        }
        await LiveZeroRoleAdminProofTests.CleanAsync(store, slug);
    }

    /// <summary><b>اليَوم</b>: المُرَشِّحُ يَرُدُّ «خَيّاط» بِـ‏403 — وبَقِيَّةُ المَنَصَّةِ (التَجسيدُ نَفسُه الَّذي يَقرَؤُه حارِسُ العَرضِ والرَئيسِيَّة) تَراه ناشِراً.</summary>
    [Fact]
    public async Task Today_an_approved_authored_role_carrying_listing_create_is_refused_by_the_publish_filter_on_Postgres()
    {
        if (!Enabled) return;

        await using var store = LiveDealConcurrencyProofTests.BuildStore();
        var slug = "cr-zz-" + Guid.NewGuid().ToString("N")[..8];
        try
        {
            var (passed, status, sees) = await AuthoredMemberAsksThePublishFilterAsync(store, slug);
            output.WriteLine($"عُضوٌ دَورُه «خَيّاط» المُعتَمَد · المَنَصَّةُ تَراه ناشِراً: {sees} · مُرَشِّحُ listing.create: "
                + (passed ? "مَرَّ إلى النُقطَة" : $"رَدَّ {status}"));
            Assert.True(sees);
            Assert.False(passed);
            Assert.Equal(403, status);
        }
        finally { await CleanAsync(store, slug); }
    }
}
