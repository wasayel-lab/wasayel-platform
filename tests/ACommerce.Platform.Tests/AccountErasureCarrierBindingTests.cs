using ACommerce.Kit.Auth;
using ACommerce.Platform.Providers;
using Marten;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ رَبطُ البائِعِ بِـOTO بَعدَ حَذفِ حِسابِه — تَوصيفٌ قَبلَ التَبديل (‏2026-11-04، الموجَة ١٢ G1 · ١٠؛ B-8) ═══════════════════════════════════════════════
//
// **ما يُثَبَّتُ هُنا لِيَبقى** (القاعِدَة ٣): بابُ الحَذفِ بِجِسمِه يَعودُ إلى `?deleted=1` ويَمحو حِسابَه، ولا يَمَسُّ مَدخَلَ بائِعٍ آخَرَ في المَتجَر (`delivery:{other}`).
// **وما يُطبَعُ ولا يُثَبَّت**: اليَومَ يَبقى مَدخَلُه (`TenantProviderBinding` بِمُعَرِّفِ `TenantProviderBinding.VendorEntryId(Delivery, id)`) — رَمزُ تَحديثِه عِندَ OTO مَختوماً
// وتَسجيلُ الوارِد — فَعّالاً بِلا صاحِب؛ ولا `Delete<TenantProviderBinding>` ولا `IsActive=false` خارِجَ مُزَوِّدَي الدَفعِ الوَهمِيَّين. وهذا يَتَغَيَّرُ في كوميتِ التَبديل؛ والمِلَفُّ يَبقى بِبَصمَتِه.

[Collection(LiveProofCollection.Name)]
public class LiveAccountErasureCarrierBindingProofTests(ITestOutputHelper output)
{
    private static bool Enabled
        => LiveDealConcurrencyProofTests.Enabled && !string.IsNullOrEmpty(LiveDealConcurrencyProofTests.ConnectionString);

    internal sealed record Seeded(string Slug, User Vendor, User Other)
    {
        public string VendorEntry => TenantProviderBinding.VendorEntryId(ProviderCapabilities.Delivery, Vendor.Id);
        public string OtherEntry => TenantProviderBinding.VendorEntryId(ProviderCapabilities.Delivery, Other.Id);
    }

    /// <summary>بائِعانِ لِكُلٍّ مَدخَلُ OTO فَعّالٌ بِقيمَةٍ مَختومَةٍ (بِلا خِزانَة: القيمَةُ لا تُقرَأُ هُنا، والمَدخَلُ يُقرَأُ بِمُعَرِّفِه).</summary>
    internal static async Task<Seeded> SeedAsync(IDocumentStore store, string slug)
    {
        var vendor = new User { Id = Guid.NewGuid(), TenantSlug = slug, FullName = "بائِعٌ مَربوطٌ بِـOTO", Phone = "+966500000651", PhoneVerified = true };
        var other = new User { Id = Guid.NewGuid(), TenantSlug = slug, FullName = "بائِعٌ آخَرُ مَربوط", Phone = "+966500000652", PhoneVerified = true };
        await using var s = store.LightweightSession(slug);
        s.Store(vendor, other);
        foreach (var u in new[] { vendor, other })
            s.Store(new TenantProviderBinding
            {
                Id = TenantProviderBinding.VendorEntryId(ProviderCapabilities.Delivery, u.Id), Slug = ProviderCapabilities.Delivery, TenantSlug = slug,
                ProviderSlug = "oto", Status = TenantProviderBinding.StatusActive, BoundBy = u.Id.ToString("D"), WebhookId = "wh-" + u.Id.ToString("N")[..6],
            });
        await s.SaveChangesAsync();
        return new Seeded(slug, vendor, other);
    }

    internal static async Task CleanAsync(IDocumentStore store, string slug)
    {
        await using (var s = store.LightweightSession(slug))
        {
            s.DeleteWhere<TenantProviderBinding>(b => b.Id != "");
            await s.SaveChangesAsync();
        }
        await LiveZeroRoleAdminProofTests.CleanAsync(store, slug);
    }

    /// <summary>بابُ الحَذفِ بِجِسمِه: 302 إلى <c>?deleted=1</c> والحِسابُ مَمحُوّ، ومَدخَلُ البائِعِ الآخَرِ باقٍ فَعّالاً. ويُطبَعُ مَدخَلُه بَعدَ الحَذف.</summary>
    [Fact]
    public async Task The_door_erases_the_vendor_and_leaves_another_vendors_carrier_binding_on_Postgres()
    {
        if (!Enabled) return;
        await using var host = LiveTemplateDoorHost.Build();
        var seeded = await SeedAsync(host.Store, "zz-eraoto-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            var token = ACommerce.Kit.Auth.Server.AuthHandlers.MakeToken(seeded.Vendor.Id, seeded.Slug, seeded.Vendor.SessionVersion);
            var confirm = ("confirm", ACommerce.Platform.I18n.LocaleCatalog.Text(ACommerce.Platform.I18n.LocaleCatalog.Arabic, "account.delete.confirm_word"));
            var (status, at) = await host.PostFormAsync(LiveAccountErasureFilesProofTests.DeleteDoor, new Dictionary<string, string> { ["slug"] = seeded.Slug }, seeded.Slug, token, confirm);
            await using var q = host.Store.QuerySession(seeded.Slug);
            var mine = await q.LoadAsync<TenantProviderBinding>(seeded.VendorEntry);
            var others = await q.LoadAsync<TenantProviderBinding>(seeded.OtherEntry);
            var me = (await q.LoadAsync<User>(seeded.Vendor.Id))!;
            output.WriteLine($"البابُ: {status} → {at.Replace(seeded.Slug, "{slug}")} · مَدخَلُه {(mine is null ? "مَحذوف" : mine.Status)} · مَدخَلُ الآخَر {(others is null ? "مَحذوف" : others.Status)}");
            Assert.Equal((302, $"/{seeded.Slug}?deleted=1"), (status, at));
            Assert.NotNull(me.DeletedAt);
            Assert.Equal(TenantProviderBinding.StatusActive, others?.Status);
        }
        finally { await CleanAsync(host.Store, seeded.Slug); }
    }
}
