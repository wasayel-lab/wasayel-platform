using ACommerce.Platform.Providers;
using ACommerce.Templates.Customer.Marketplace.Services;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Template;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ مَدخَلُ البائِعِ في مُجموعَةِ رَبطِ المَتجَر — ما يَبلُغُه بابُ السَحبِ في الاستوديو بِمُعَرِّفِه — تَوصيفُ «اليَوم» ═══
//
// مُراجَعَةُ المَوجَة 3b (‏2026-10-13، G5 (i)): مَداخِلُ البائِعينَ تَسكُنُ مَجموعَةَ `TenantProviderBinding` بِمُعَرِّفِ `delivery:{owner}`
// (‏ADR-033 §٢‑ج‑٢)، وبابُ السَحبِ `/studio/apps/{slug}/providers/{capability}/revoke` يُمَرِّرُ `{capability}` مِن العُنوانِ مُعَرِّفَ وَثيقَةٍ
// إلى `RevokeAsync` و`CurrentAsync` — فَصاحِبُ المَتجَرِ (‏`TenantAdminGuard`) يَسحَبُ مَدخَلَ بائِعٍ مَختوماً بِـ`capability=delivery:<vendorGuid>`
// (ويُكتَبُ أَثَرُه `provider.revoke`)، و§٢‑ج‑٣ يُبقي مَداخِلَ البائِعينَ خارِجَ الاستوديو.
//
// **ما يُقلَبُ بِاسمِه في كوميتِ الإصلاح**: صَفّا «Today_» أَدناه. وصَفُّ المَسارِ يَبقى بِحَرفِه: العُنوانُ يَربِطُ المُعَرِّفَ — والحارِسُ في الخِدمَة.
//
// **قُلِبا** (‏2026-10-13، كوميتُ «fix(providers)» بَعدَ ‏308937b): `RevokeAsync` و`CurrentAsync` تُجيبانِ `null` قَبلَ أَيِّ جَلسَةٍ لِما لَيسَ
// قُدرَةً مِن المَعجَم — والبابُ يَرُدُّ `binding_not_found` بِلا أَثَرٍ ولا كِتابَة. ومَعَهُما صَفٌّ مُوجَبٌ (قُدرَةُ المَتجَرِ تُسحَبُ كَما كانَت) وسالِب.

public class ProviderRevokeVendorEntryTests(ITestOutputHelper output)
{
    private const string Slug = "revoke-shop";
    private static readonly Guid Vendor = Guid.Parse("6f0e3c55-1d2b-4a7e-9c41-2b8d5e7f9a10");
    private static string EntryId => TenantProviderBinding.VendorEntryId(ProviderCapabilities.Delivery, Vendor);

    /// <summary>مَتجَرٌ بِرَبطِ تَوصيلٍ لَه، ومَدخَلُ بائِعٍ فَعّالٌ لِلقُدرَةِ نَفسِها — كَما يَكتُبُهُما <c>BindProviderAsync</c> و<c>BindVendorAsync</c>.</summary>
    private static DocWorld StoreWithVendorEntry()
    {
        var w = new DocWorld();
        w.Put(new TenantProviderBinding
        {
            Id = ProviderCapabilities.Delivery, Slug = ProviderCapabilities.Delivery, TenantSlug = Slug,
            ProviderSlug = "oto", Status = TenantProviderBinding.StatusActive, BoundBy = "owner", BoundAt = DateTime.UtcNow,
        });
        w.Put(new TenantProviderBinding
        {
            Id = EntryId, Slug = ProviderCapabilities.Delivery, TenantSlug = Slug,
            ProviderSlug = "oto", Status = TenantProviderBinding.StatusActive, BoundBy = Vendor.ToString(), BoundAt = DateTime.UtcNow,
        });
        return w;
    }

    [Fact]
    public void The_studio_revoke_route_binds_a_vendor_entry_id_as_its_capability()
    {
        var values = new RouteValueDictionary();
        var matcher = new TemplateMatcher(TemplateParser.Parse("studio/apps/{slug}/providers/{capability}/revoke"), new RouteValueDictionary());
        var matched = matcher.TryMatch($"/studio/apps/{Slug}/providers/{EntryId}/revoke", values);
        output.WriteLine($"تَطابَق: {matched} · capability = {values["capability"]}");
        Assert.True(matched);
        Assert.Equal(EntryId, values["capability"]);
        Assert.True(TenantProviderBinding.IsVendorEntryId((string)values["capability"]!, out _, out var owner));
        Assert.Equal(Vendor, owner);
    }

    /// <summary><b>قُلِبَ</b> (‏2026-10-13) — كانَ <c>Today_the_studio_revoke_with_a_vendor_entry_id_revokes_the_vendors_sealed_entry</c> (‏<c>308937b</c>):
    /// مُعَرِّفُ مَدخَلِ البائِعِ لا يَبلُغُ السَحب — <c>null</c> بِلا جَلسَةٍ ولا حِفظ، والمَدخَلُ فَعّالٌ بِحالِه.</summary>
    [Fact]
    public async Task The_studio_revoke_refuses_a_vendor_entry_id_and_the_entry_stays_active()
    {
        var w = StoreWithVendorEntry();
        var revoked = await new TenantProviderService(w.Store).RevokeAsync(Slug, EntryId);
        var entry = w.Read<TenantProviderBinding>(EntryId)!;
        output.WriteLine($"السَحب: {(revoked is null ? "مَردود" : revoked.Id)} · المَدخَل: {entry.Status} · حِفظات: {w.SaveCalls} · لَمَسات: {w.Touches.Count}");
        Assert.Null(revoked);
        Assert.Equal(TenantProviderBinding.StatusActive, entry.Status);
        Assert.Equal(0, w.SaveCalls);
        Assert.Empty(w.Touches);   // ‏لا جَلسَةَ تُفتَح — الرَدُّ قَبلَ القِراءَة
        Assert.Equal(TenantProviderBinding.StatusActive, w.Read<TenantProviderBinding>(ProviderCapabilities.Delivery)!.Status);
    }

    /// <summary><b>قُلِبَ</b> (‏2026-10-13) — كانَ <c>Today_the_current_binding_read_with_a_vendor_entry_id_returns_the_vendors_entry</c> (‏<c>308937b</c>):
    /// سَطرُ «قَبلُ» في أَثَرِ السَحبِ لا يَقرَأُ مَدخَلَ بائِع.</summary>
    [Fact]
    public async Task The_current_binding_read_refuses_a_vendor_entry_id()
    {
        var w = StoreWithVendorEntry();
        var current = await new TenantProviderService(w.Store).CurrentAsync(Slug, EntryId);
        output.WriteLine($"القِراءَة: {current?.Id ?? "لا شَيء"} · لَمَسات: {w.Touches.Count}");
        Assert.Null(current);
        Assert.Empty(w.Touches);
    }

    /// <summary>المُوجَب: قُدرَةُ المَتجَرِ تُقرَأُ وتُسحَبُ كَما كانَت — ومَدخَلُ البائِعِ لا يَمَسُّه سَحبُها.</summary>
    [Fact]
    public async Task The_stores_own_capability_is_still_read_and_revoked_and_the_vendor_entry_is_untouched()
    {
        var w = StoreWithVendorEntry();
        var providers = new TenantProviderService(w.Store);
        Assert.Equal(ProviderCapabilities.Delivery, (await providers.CurrentAsync(Slug, ProviderCapabilities.Delivery))?.Id);
        var revoked = await providers.RevokeAsync(Slug, ProviderCapabilities.Delivery);
        output.WriteLine($"السَحب: {revoked?.Id} · رَبطُ المَتجَر: {w.Read<TenantProviderBinding>(ProviderCapabilities.Delivery)!.Status} · المَدخَل: {w.Read<TenantProviderBinding>(EntryId)!.Status}");
        Assert.Equal(ProviderCapabilities.Delivery, revoked?.Id);
        Assert.Equal(TenantProviderBinding.StatusRevoked, w.Read<TenantProviderBinding>(ProviderCapabilities.Delivery)!.Status);
        Assert.Equal(1, w.SaveCalls);
        Assert.Equal(TenantProviderBinding.StatusActive, w.Read<TenantProviderBinding>(EntryId)!.Status);
    }

    /// <summary>السالِب: كُلُّ ما لَيسَ قُدرَةً مِن المَعجَمِ — مَدخَلُ بائِعٍ لِقُدرَةٍ أُخرى، ولاحِقَةٌ غَيرُ مُعَرِّف، وقُدرَةٌ بِحالَةِ حَرفٍ أُخرى، واسمٌ خارِجَ
    /// المَعجَم، وفارِغ — لا يُقرَأُ ولا يُسحَب.</summary>
    [Theory]
    [InlineData("payments:6f0e3c55-1d2b-4a7e-9c41-2b8d5e7f9a10")]
    [InlineData("delivery:not-a-guid")]
    [InlineData("Delivery")]
    [InlineData("oto")]
    [InlineData("")]
    public async Task Anything_outside_the_capability_lexicon_is_neither_read_nor_revoked(string id)
    {
        var w = StoreWithVendorEntry();
        w.Put(new TenantProviderBinding { Id = id, Slug = id, TenantSlug = Slug, ProviderSlug = "x", Status = TenantProviderBinding.StatusActive });
        var providers = new TenantProviderService(w.Store);
        Assert.Null(await providers.CurrentAsync(Slug, id));
        Assert.Null(await providers.RevokeAsync(Slug, id));
        Assert.Equal(TenantProviderBinding.StatusActive, w.Read<TenantProviderBinding>(id)!.Status);
        Assert.Equal(0, w.SaveCalls);
    }
}
