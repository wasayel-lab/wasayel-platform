using System.Text.RegularExpressions;
using ACommerce.Platform.I18n;
using ACommerce.Platform.Providers;
using ACommerce.Templates.Customer.Marketplace.Services.Audit;
using ACommerce.Templates.Customer.Marketplace.Services.Export;
using ACommerce.Templates.Customer.Marketplace.Services.Identity;
using Marten;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ مَدخَلُ البائِعِ عِندَ OTO يُحذَفُ بِحَذفِ حِسابِه (‏2026-11-04، الموجَة ١٢ G1 · ١٠ — B-8؛ تَوصيفُه 519731b) ═══════════════════════════════════════════
//
// قَبلَه («مَدخَلُه active» — `LiveAccountErasureCarrierBindingProofTests` يَطبَعُه ويَبقى بِبَصمَتِه). والآن `EraseCopiesAsync` يَحذِفُ `AccountErasure.CarrierEntryId(id)`
// في المُعامَلَةِ نَفسِها، ولا نِداءَ إلى OTO؛ وصَفحَةُ الحَذفِ تَقولُ إنّ حِسابَه عِندَها يَبقى عِندَها.

public class AccountErasureCarrierBindingRowsTests(ITestOutputHelper output)
{
    /// <summary>المُعَرِّفُ مِن دالَّةِ المَدخَلِ نَفسِها لا نُسخَةٌ مِن صيغَتِه — ويُقرَأُ مَدخَلَ بائِعٍ بِقُدرَةِ الشَحنِ ومالِكِه.</summary>
    [Fact]
    public void The_carrier_entry_id_is_the_vendor_entry_id_of_the_delivery_capability()
    {
        var me = Guid.NewGuid();
        Assert.Equal(TenantProviderBinding.VendorEntryId(ProviderCapabilities.Delivery, me), AccountErasure.CarrierEntryId(me));
        Assert.True(TenantProviderBinding.IsVendorEntryId(AccountErasure.CarrierEntryId(me), out var capability, out var owner));
        Assert.Equal((ProviderCapabilities.Delivery, me), (capability, owner));
        Assert.NotEqual(AccountErasure.CarrierEntryId(Guid.NewGuid()), AccountErasure.CarrierEntryId(me));
    }

    /// <summary>القاعِدَة ٢: كُلُّ بابٍ يَختِمُ مَدخَلَ بائِعٍ يَختِمُه بِقُدرَةِ الشَحن — فَالمَحوُ بِقُدرَةٍ واحِدَةٍ يَبلُغُ كُلَّ مَدخَل. وخَتمٌ بِقُدرَةٍ أُخرى يُحمِرُّ هُنا حَتّى تَدخُلَ المَحو.</summary>
    [Fact]
    public void Every_vendor_entry_writer_seals_it_under_the_delivery_capability_the_erasure_deletes()
    {
        var calls = EntitlementContractTests.SourceFiles()
            .Where(f => f.File.EndsWith(".cs", StringComparison.Ordinal))
            .SelectMany(f => Regex.Matches(WriteEndpointGuardTests.StripComments(f.Text), @"\.BindVendorAsync\(\s*\w+\s*,\s*(?<cap>[\w.]+)\s*,").Select(m => (Path.GetFileName(f.File), m.Groups["cap"].Value)))
            .ToList();
        output.WriteLine($"مَواضِعُ خَتمِ مَدخَلِ البائِع: {calls.Count} [{string.Join(", ", calls.Select(c => $"{c.Item1}: {c.Item2}"))}]");
        Assert.True(calls.Count >= 1, "أَداةٌ عَمياء: صِفرُ مَوضِع.");
        Assert.All(calls, c => Assert.Equal("ProviderCapabilities.Delivery", c.Item2));
    }

    [Fact]
    public void The_deletion_page_says_the_oto_account_stays_with_oto_and_how_to_unlink_it()
    {
        var note = AccountDeletionRetentionTests.Bare(LocaleCatalog.Find(LocaleCatalog.Arabic, "account.delete.retained_note")!);
        Assert.Contains("OTO", note, StringComparison.Ordinal);
        Assert.Contains("يحذف ربطه من المتجر", note, StringComparison.Ordinal);
        Assert.Contains("يبقى عندها", note, StringComparison.Ordinal);
        Assert.Contains("ألغ ربطه من لوحتها", note, StringComparison.Ordinal);
    }
}

[Collection(LiveProofCollection.Name)]
public class LiveAccountErasureCarrierBindingSwitchProofTests(ITestOutputHelper output)
{
    private static bool Enabled
        => LiveDealConcurrencyProofTests.Enabled && !string.IsNullOrEmpty(LiveDealConcurrencyProofTests.ConnectionString);

    /// <summary>قُلِبَ ما طَبَعَه التَوصيف («مَدخَلُه active»). <b>المُوجَب</b>: بَعدَ البابِ لا مَدخَلَ لَه، ولا صَفَّ لَه في جَدوَلِ الرَبطِ في حَقيبَةِ التَخارُج. <b>والسالِب</b>:
    /// مَدخَلُ البائِعِ الآخَرِ فَعّالٌ في المَخزَنِ وفي الحَقيبَة.</summary>
    [Fact]
    public async Task The_door_deletes_the_vendors_carrier_binding_and_keeps_another_vendors_on_Postgres()
    {
        if (!Enabled) return;
        await using var host = LiveTemplateDoorHost.Build();
        var seeded = await LiveAccountErasureCarrierBindingProofTests.SeedAsync(host.Store, "zz-eraotosw-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            var token = ACommerce.Kit.Auth.Server.AuthHandlers.MakeToken(seeded.Vendor.Id, seeded.Slug, seeded.Vendor.SessionVersion);
            var confirm = ("confirm", LocaleCatalog.Text(LocaleCatalog.Arabic, "account.delete.confirm_word"));
            var (status, at) = await host.PostFormAsync(LiveAccountErasureFilesProofTests.DeleteDoor, new Dictionary<string, string> { ["slug"] = seeded.Slug }, seeded.Slug, token, confirm);
            await using var q = host.Store.QuerySession(seeded.Slug);
            var mine = await q.LoadAsync<TenantProviderBinding>(seeded.VendorEntry);
            var others = await q.LoadAsync<TenantProviderBinding>(seeded.OtherEntry);
            var tenant = new ACommerce.Kit.Tenants.Tenant { Id = seeded.Slug, Name = "مَتجَرُ الشَحن", OwnerUserId = Guid.NewGuid() };
            var content = await new TenantExportService(host.Store, new ACommerce.Kit.Files.UnavailableFileStorage(), new AuditWriter(host.Store)).CollectAsync(tenant, tenant.OwnerUserId);
            var exported = Assert.Single(content.Tables, t => t.TypeName == nameof(TenantProviderBinding)).Rows.Select(r => r["id"]?.ToString()).ToHashSet(StringComparer.Ordinal);
            output.WriteLine($"البابُ: {status} → {at.Replace(seeded.Slug, "{slug}")} · مَدخَلُه {(mine is null ? "مَحذوف" : mine.Status)} · مَدخَلُ الآخَر {(others is null ? "مَحذوف" : others.Status)} · "
                + $"الحَقيبَة: مَدخَلُه {exported.Contains(seeded.VendorEntry)} · الآخَر {exported.Contains(seeded.OtherEntry)}");
            Assert.Equal((302, $"/{seeded.Slug}?deleted=1"), (status, at));
            Assert.Null(mine);
            Assert.Equal(TenantProviderBinding.StatusActive, others?.Status);
            Assert.DoesNotContain(seeded.VendorEntry, exported);
            Assert.Contains(seeded.OtherEntry, exported);
        }
        finally { await LiveAccountErasureCarrierBindingProofTests.CleanAsync(host.Store, seeded.Slug); }
    }
}
