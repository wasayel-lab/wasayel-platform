using ACommerce.Kit.Tenants;
using ACommerce.Platform.MultiTenancy;
using ACommerce.Platform.Shared.Metering;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using ACommerce.Templates.Customer.Marketplace.Services.Metering;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ G16 — حامِلُ النِطاق: يَعبُرُ الدالَّةَ المُساعِدَةَ المُنتَظَرَة، ولا يُكتَبُ فَوقَ مَتجَرِه، وحامِلُ البَذرِ لِكُلِّ مَتجَر، ولا يَنسِبُ أ إلى ب ═══
// (‏2026-11-12، الموجَة ١٣ W13-6·٣ — ADR-051 §١١؛ التَصميم §5.4). والضابِطُ السالِبُ قيمَةٌ ثابِتَةٌ عَلى AsyncLocal تَضيعُ كَما في R1
// (`role at save = _none_yet`) — فَإن صارَ الحامِلُ قيمَةً يَوماً، احمَرَّ الصَفُّ الأَوَّلُ وبَقِيَ السالِبُ أَخضَر.

public class MeterScopeTests(ITestOutputHelper output)
{
    private static Deal DealOf(string slug) => new() { Id = Guid.NewGuid(), TenantSlug = slug };

    private static UsageEntry SaveNow(string slug)
        => Assert.Single(MeterRules.Classify(new PendingSnapshot(slug, [new PendingDocument(DealOf(slug), true)]),
                                             MeterScope.View(), MeterUnitCatalog.Default));

    /// <summary>شَكلُ <c>HasPermissionAsync</c>: دالَّةٌ مُساعِدَةٌ مُنتَظَرَةٌ تَحسِبُ الدَورَ بَعدَ <c>await</c> ثُمَّ تَعود — وما كَتَبَتهُ يُرى عِندَ الحِفظ.</summary>
    private static async Task<bool> HelperFillsRoleAsync(string slug, string role)
    {
        await Task.Delay(1);
        return MeterScope.Current!.Fill(slug, role: role);
    }

    [Fact]
    public async Task The_holder_keeps_what_an_awaited_helper_wrote_until_the_save()
    {
        string role = "";
        await Task.Run(async () =>
        {
            MeterScope.Open().Fill("store-a", entrepreneur: "owner-a");
            Assert.True(await HelperFillsRoleAsync("store-a", "seller"));
            role = SaveNow("store-a").Role;
        });
        output.WriteLine($"[G16] الحامِل: role at save = {role}");
        Assert.Equal("seller", role);
    }

    private sealed record ImmutableScope(string App, string Role);
    private static readonly AsyncLocal<ImmutableScope?> Immutable = new();

    /// <summary><b>الضابِطُ السالِب</b> (R1): القيمَةُ الثابِتَةُ الَّتي تُستَبدَلُ داخِلَ الدالَّةِ المُنتَظَرَةِ لا تَعودُ إلى المُنادي — فَالدَورُ يَبقى ما كانَ.</summary>
    [Fact]
    public async Task Negative_control_an_immutable_async_local_value_loses_the_helpers_role()
    {
        string role = "";
        await Task.Run(async () =>
        {
            Immutable.Value = new ImmutableScope("store-a", "_none_yet");
            await ReplaceAsync();
            role = Immutable.Value!.Role;
        });
        output.WriteLine($"[G16] الضابِطُ السالِب: role at save = {role}");
        Assert.Equal("_none_yet", role);

        static async Task ReplaceAsync()
        {
            await Task.Delay(1);
            Immutable.Value = Immutable.Value! with { Role = "seller" };
        }
    }

    [Fact]
    public void The_app_is_never_overwritten_and_a_field_is_written_once()
    {
        var h = MeterScope.Open();
        Assert.True(h.Fill("store-a", entrepreneur: "owner-a"));
        Assert.False(h.Fill("store-b", entrepreneur: "owner-b", role: "seller"));
        Assert.True(h.Fill("STORE-A", entrepreneur: "intruder", role: "_none"));
        Assert.True(h.Fill("store-a", role: "seller"));
        Assert.Equal(new MeterScopeView("store-a", "owner-a", "_none", false), h.View());
    }

    [Fact]
    public void The_seed_holder_applies_to_every_store_cannot_be_filled_and_is_restored_on_dispose()
    {
        var before = MeterScope.Open();
        using (MeterScope.Seed())
        {
            Assert.True(MeterScope.Current!.IsSeed);
            Assert.False(MeterScope.Current.Fill("ejar", entrepreneur: "x", role: "y"));
            var a = SaveNow("ejar");
            var b = SaveNow("theme-demo");
            output.WriteLine($"[G16] البَذر: {a.App}={a.Entrepreneur}/{a.Role} · {b.App}={b.Entrepreneur}/{b.Role}");
            Assert.All(new[] { a, b }, e => Assert.Equal((MeterSentinels.Seed, MeterSentinels.Seed), (e.Entrepreneur, e.Role)));
        }
        Assert.Same(before, MeterScope.Current);
    }

    [Fact]
    public void A_scope_for_store_a_never_attributes_store_bs_write()
    {
        MeterScope.Open().Fill("store-a", entrepreneur: "owner-a", role: MeterSentinels.None);
        var b = SaveNow("store-b");
        var a = SaveNow("store-a");
        output.WriteLine($"[G16] أ ← {a.Entrepreneur}/{a.Role} · ب ← {b.Entrepreneur}/{b.Role}");
        Assert.Equal(("owner-a", MeterSentinels.None), (a.Entrepreneur, a.Role));
        Assert.Equal((MeterSentinels.Unresolved, MeterSentinels.Unresolved), (b.Entrepreneur, b.Role));
    }

    // ─── الفاتِحُ الحَقيقِيّ: `TenantResolverMiddleware` بِعَينِه، والمَتجَرُ مِن ذاكِرَتِه (صِفرُ قاعِدَة) ──────────────────────────────

    private static async Task<MeterScopeView?> ThroughResolverAsync(string path, Tenant? cached)
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        if (cached is not null) cache.Set($"tenant:{cached.Id}", cached);
        MeterScopeView? seen = null;
        var resolver = new TenantResolverMiddleware(_ => { seen = MeterScope.View(); return Task.CompletedTask; },
            store: null!, cache, new SubdomainTenancyOptions());
        var ctx = new DefaultHttpContext();
        ctx.Request.Path = path;
        ctx.Request.Host = new HostString("localhost");
        // ‏حامِلٌ سابِقٌ مَملوءٌ لِمَتجَرٍ آخَر — الوَسيطُ يَفتَحُ جَديداً فَلا يَرِثُه الطَلَب.
        MeterScope.Open().Fill("stale-store", entrepreneur: "stale-owner", role: "stale-role");
        await resolver.InvokeAsync(ctx);
        return seen;
    }

    [Fact]
    public async Task The_resolver_opens_a_fresh_holder_on_every_request_and_fills_it_from_the_store_document()
    {
        var owner = Guid.NewGuid();
        var noRoles = await ThroughResolverAsync("/store-a/listings", new Tenant { Id = "store-a", OwnerUserId = owner });
        var withRoles = await ThroughResolverAsync("/store-r/listings",
            new Tenant { Id = "store-r", OwnerUserId = owner, Roles = [new ACommerce.Kit.Roles.Role { Slug = "vendor" }] });
        var unowned = await ThroughResolverAsync("/store-u/listings", new Tenant { Id = "store-u", OwnerUserId = Guid.Empty });
        var studio = await ThroughResolverAsync("/studio/apps", cached: null);
        output.WriteLine($"[G16] المُحَلِّل: {noRoles} · {withRoles} · {unowned} · {studio}");

        Assert.Equal(new MeterScopeView("store-a", owner.ToString("D"), MeterSentinels.None, false), noRoles);
        // ‏مَتجَرٌ بِأَدوار: الدَورُ يَبقى لِمَن يَحسِبُه (البَند ١٠). و«لا دَورَ المُضيف» لا يَقيسُه هذا الصَفّ — نِطاقُه فارِغٌ ومَخزَنُه غائِب، ومُضيفُ الدَورِ يُقرَأُ
        // طازَجاً مِن المَخزَن — فَمَقيسُه الصَفُّ الحَيُّ `On_store_and_role_hosts_the_resolver_fills_the_owner_and_never_the_host_role` (‏2026-11-12، M6A-SEC-R2-1).
        Assert.Equal(new MeterScopeView("store-r", owner.ToString("D"), null, false), withRoles);
        Assert.Equal(new MeterScopeView("store-u", MeterSentinels.Unowned, MeterSentinels.None, false), unowned);
        // ‏طَلَبٌ بِلا مَتجَر: حامِلٌ مَفتوحٌ فارِغ — لا يَرِثُ ما قَبلَه.
        Assert.Equal(new MeterScopeView(null, null, null, false), studio);
    }

    // ═══ ‏(‏2026-11-12، W13-6·٨) مَسارا الاستوديو والإدارَة — لا يُحَلُّ فيهِما مَتجَر، فَيَملَأُ الحامِلَ حُرّاسُهُما بَعدَ `await` قِراءَتِهِم ═══
    // (التَصميم §5.4، COR-1). الحُرّاسُ الحَقيقِيّونَ (`StudioOwnsAsync` و`TenantAdminGuard` و`PlatformAdminGuard`) يَقرَؤونَ القاعِدَة، فَصُفوفُهُم بِأَبوابِهِم
    // في `LiveMeterPersistenceProofTests`؛ وهُنا قَواعِدُ الحامِلِ الَّتي يَقومونَ عَلَيها والمُحَلِّلُ الحَقيقِيُّ عَلى مَسارِ الاستوديو.

    /// <summary><b>مَسارُ الاستوديو لا مَتجَرَ فيه</b> (<c>studio</c> مَحجوز، <c>ReservedTenantSlugs</c>) — والمَسارُ يَحمِلُ اسمَ المَتجَرِ بَعدَه: المُحَلِّلُ يَفتَحُ
    /// حامِلاً فارِغاً ولا يَأخُذُه، فَما يَملَؤُه هُوَ حارِسُ المِلكِيَّةِ بَعدَ <c>await</c> قِراءَتِه (شَكلُ <c>StudioOwnsAsync</c>): المَتجَرُ ومالِكُه و<c>_entrepreneur</c>،
    /// ويَراه الحِفظ. ومِثلُه <c>/admin/tenants/{slug}/…</c>.</summary>
    [Fact]
    public async Task On_the_studio_and_admin_paths_the_resolver_leaves_the_holder_empty_and_the_owner_guard_fills_it_after_its_await()
    {
        var owner = Guid.NewGuid();
        var studio = await ThroughResolverAsync("/studio/apps/store-a/branding/save", new Tenant { Id = "store-a", OwnerUserId = owner });
        var admin = await ThroughResolverAsync("/admin/tenants/store-a/users", new Tenant { Id = "store-a", OwnerUserId = owner });
        Assert.Equal(new MeterScopeView(null, null, null, false), studio);
        Assert.Equal(new MeterScopeView(null, null, null, false), admin);

        UsageEntry? entry = null;
        await Task.Run(async () =>
        {
            MeterScope.Open();
            Assert.True(await OwnerGuardAsync("store-a", owner));
            entry = SaveNow("store-a");
        });
        output.WriteLine($"[G16] الاستوديو: المُحَلِّل {studio} · الإدارَة {admin} · بَعدَ الحارِس {entry!.Entrepreneur}/{entry.Role}");
        Assert.Equal((owner.ToString("D"), MeterSentinels.Entrepreneur), (entry.Entrepreneur, entry.Role));

        static async Task<bool> OwnerGuardAsync(string slug, Guid ownerId)
        {
            await Task.Delay(1);
            return MeterScope.Current!.Fill(slug, entrepreneur: MeterScope.OwnerOf(ownerId), role: MeterSentinels.Entrepreneur);
        }
    }

    /// <summary><b>دَورُ فاعِلِ المَنَصَّة</b> (<see cref="MeterScopeHolder.FillPlatformRole"/>): يَضَعُ الدَورَ وَحدَه — لا مَتجَرَ ولا رائِدَ أَعمال — والكاتِبُ الأَوَّلُ
    /// يَفوز؛ ويَرى الحِفظُ بَعدَ <c>await</c> الحارِسِ دَورَه في أَيِّ مَتجَر؛ ومُساعِدٌ لِمَتجَرٍ بَعدَه يَضَعُ المَتجَرَ ورائِدَ الأَعمالِ ولا يَمَسُّ الدَور؛ وحامِلُ البَذرِ يَرفُضُه.</summary>
    [Fact]
    public async Task The_platform_admin_role_fills_only_the_role_is_seen_after_the_guards_await_and_never_claims_a_store()
    {
        UsageEntry? entry = null;
        MeterScopeView? afterGuard = null;
        await Task.Run(async () =>
        {
            MeterScope.Open();
            await Task.Delay(1);
            Assert.True(MeterScope.Current!.FillPlatformRole(MeterSentinels.PlatformAdmin));
            afterGuard = MeterScope.View();
            entry = SaveNow("store-new");
        });
        output.WriteLine($"[G16] مُشرِفُ المَنَصَّة: {afterGuard} · سَطرُ مَتجَرٍ جَديد {entry!.Entrepreneur}/{entry.Role}");
        Assert.Equal(new MeterScopeView(null, null, MeterSentinels.PlatformAdmin, false), afterGuard);
        Assert.Equal((MeterSentinels.Unresolved, MeterSentinels.PlatformAdmin), (entry.Entrepreneur, entry.Role));

        var h = MeterScope.Open();
        Assert.True(h.FillPlatformRole(MeterSentinels.PlatformAdmin));
        Assert.True(h.FillPlatformRole("seller"));
        Assert.True(h.Fill("store-a", entrepreneur: "owner-a", role: MeterSentinels.Entrepreneur));
        Assert.Equal(new MeterScopeView("store-a", "owner-a", MeterSentinels.PlatformAdmin, false), h.View());
        Assert.Equal((MeterSentinels.Unresolved, MeterSentinels.Unresolved), (SaveNow("store-b").Entrepreneur, SaveNow("store-b").Role));
        using (MeterScope.Seed())
        {
            Assert.False(MeterScope.Current!.FillPlatformRole(MeterSentinels.PlatformAdmin));
            Assert.Equal(MeterSentinels.Seed, MeterScope.Current.View().Role);
        }
        Assert.False(MeterScope.Open().FillPlatformRole(""));
    }

    // ═══ ‏(‏2026-11-12، W13-6·١٠) شَكلُ `HasPermissionAsync` الحَقيقِيّ قَبلَ `StartStream<Offer>` — صَفُّ المِجَسِّ الأَوَّل (R1) ═══
    // الحارِسُ يَنتَظِرُ قِراءَتَيه (المَتجَرُ مُجَسَّداً ثُمَّ العُضو) ثُمَّ `EffectiveRole.ResolveAsync` الحَقيقِيّ، ويَكتُبُ الدَورَ الَّذي قَبِلَ بِه في الحامِلِ بِسَطرِه
    // في `MarketplaceTemplateExtensions` بِحَرفِه؛ ثُمَّ يَبدَأُ البابُ مَجرى العَرضِ ويَحفَظ. والتَوأَمُ نَفسُه بِقيمَةٍ ثابِتَةٍ عَلى `AsyncLocal` يَفقِدُ الدَور
    // (`_none_yet`) — فَإن صارَ الحامِلُ قيمَةً يَوماً احمَرَّ الصَفّ.

    private static UsageEntry SaveOfferNow(string slug)
        => Assert.Single(MeterRules.Classify(
            new PendingSnapshot(slug, [], [new PendingStream(slug, Started: true, [new PendingEvent(Guid.NewGuid(),
                new ACommerce.Kit.Offers.OfferSubmitted(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "عارِض", 1m, null, 24.7, 46.6,
                                                        DateTime.UtcNow.AddMinutes(15), DateTime.UtcNow))])]),
            MeterScope.View(), MeterUnitCatalog.Default));

    private static readonly IReadOnlyList<ACommerce.Kit.Roles.Role> SellerStore =
        [new ACommerce.Kit.Roles.Role { Slug = "seller", CatalogSlug = "seller", Permissions = ["offer.submit"] }];

    /// <summary>شَكلُ <c>HasPermissionAsync</c> بِسَطرِ مَلئِه: قِراءَتانِ مُنتَظَرَتان، ثُمَّ الدَورُ الفَعّالُ الحَقيقِيّ، ثُمَّ القَرار، ثُمَّ الحامِلُ إن قَبِل.</summary>
    private static async Task<bool> OfferGuardShapeAsync(HttpContext http, string slug, Guid userId, string storedRole)
    {
        await Task.Delay(1);   // LoadTenantWithRolesAsync
        await Task.Delay(1);   // LoadAsync<User>
        var effectiveRole = await ACommerce.Templates.Customer.Marketplace.Gates.EffectiveRole.ResolveAsync(http, slug, userId, storedRole);
        var allowed = ACommerce.Kit.Roles.RolePermissions.Has(SellerStore, effectiveRole, "offer.submit");
        if (allowed) MeterScope.Current?.Fill(slug, role: effectiveRole);
        return allowed;
    }

    [Fact]
    public async Task The_role_the_real_offer_guard_shape_computes_after_its_awaits_is_the_offer_entrys_role_at_save_and_an_immutable_value_loses_it()
    {
        UsageEntry? offer = null;
        string immutableRole = "";
        await Task.Run(async () =>
        {
            // ‏المُحَلِّلُ لِمَتجَرٍ بِأَدوار: المَتجَرُ ومالِكُه، والدَورُ لِمَن يَحسِبُه.
            MeterScope.Open().Fill("store-a", entrepreneur: "owner-a");
            Assert.True(await OfferGuardShapeAsync(new DefaultHttpContext(), "store-a", Guid.NewGuid(), "seller"));
            offer = SaveOfferNow("store-a");

            Immutable.Value = new ImmutableScope("store-a", "_none_yet");
            await ImmutableTwinAsync();
            immutableRole = Immutable.Value!.Role;
        });
        output.WriteLine($"[G16] شَكلُ HasPermissionAsync ← StartStream<Offer>: {offer!.Unit} role at save = {offer.Role} · entrepreneur = {offer.Entrepreneur} "
                         + $"· التَوأَمُ الثابِت: role at save = {immutableRole}");
        Assert.Equal((MeterRules.OfferSubmit, "seller", "owner-a"), (offer.Unit, offer.Role, offer.Entrepreneur));
        Assert.Equal("_none_yet", immutableRole);

        // ‏والمَرفوضُ لا يَكتُب: دَورٌ لا يَحمِلُ الصَلاحِيَّةَ يَترُكُ الحامِلَ كَما فَتَحَه المُحَلِّل.
        var refused = MeterScope.Open();
        refused.Fill("store-a", entrepreneur: "owner-a");
        Assert.False(await OfferGuardShapeAsync(new DefaultHttpContext(), "store-a", Guid.NewGuid(), "buyer"));
        Assert.Equal(new MeterScopeView("store-a", "owner-a", null, false), refused.View());

        static async Task ImmutableTwinAsync()
        {
            await Task.Delay(1);
            await Task.Delay(1);
            var effectiveRole = await ACommerce.Templates.Customer.Marketplace.Gates.EffectiveRole.ResolveAsync(new DefaultHttpContext(), "store-a", Guid.NewGuid(), "seller");
            if (ACommerce.Kit.Roles.RolePermissions.Has(SellerStore, effectiveRole, "offer.submit"))
                Immutable.Value = Immutable.Value! with { Role = effectiveRole! };
        }
    }
}
