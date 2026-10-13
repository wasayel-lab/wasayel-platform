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

    [Fact]
    public async Task Today_the_studio_revoke_with_a_vendor_entry_id_revokes_the_vendors_sealed_entry()
    {
        var w = StoreWithVendorEntry();
        var revoked = await new TenantProviderService(w.Store).RevokeAsync(Slug, EntryId);
        var entry = w.Read<TenantProviderBinding>(EntryId)!;
        output.WriteLine($"السَحب: {(revoked is null ? "مَردود" : revoked.Id)} · المَدخَل: {entry.Status} · حِفظات: {w.SaveCalls}");
        Assert.NotNull(revoked);
        Assert.Equal(TenantProviderBinding.StatusRevoked, entry.Status);
        Assert.Equal(1, w.SaveCalls);
        Assert.Equal(TenantProviderBinding.StatusActive, w.Read<TenantProviderBinding>(ProviderCapabilities.Delivery)!.Status);
    }

    [Fact]
    public async Task Today_the_current_binding_read_with_a_vendor_entry_id_returns_the_vendors_entry()
    {
        var w = StoreWithVendorEntry();
        var current = await new TenantProviderService(w.Store).CurrentAsync(Slug, EntryId);
        output.WriteLine($"القِراءَة: {current?.Id ?? "لا شَيء"}");
        Assert.Equal(EntryId, current?.Id);
    }
}
