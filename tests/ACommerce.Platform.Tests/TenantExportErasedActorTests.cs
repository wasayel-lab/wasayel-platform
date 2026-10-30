using ACommerce.Kit.Auth;
using ACommerce.Kit.Files;
using ACommerce.Kit.Tenants;
using ACommerce.Templates.Customer.Marketplace.Services.Audit;
using ACommerce.Templates.Customer.Marketplace.Services.Export;
using Marten;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ حَقيبَةُ تَخارُجِ المَتجَرِ وسِجِلُّ التَدقيق — اسمُ مَن حَذَفَ حِسابَه (‏2026-10-30، الموجَة ١٢ F2 · ٧، F2B-3) ═════════════════════════════
//
// سِجِلُّ التَدقيقِ تاريخٌ لا يُعادُ كَتبُه: اسمُ الفاعِلِ يَبقى في المَخزَنِ كَما كُتِب، ويُخفى عِندَ العَرضِ (`AuditLogQueries.HideErasedActors`، د52 «أ» —
// «ويُخفى الاسمُ عِندَ عَرضِ السِجِلّاتِ الَّتي لا تُعَدَّل»). وحَقيبَةُ التَخارُجِ عَرضٌ يَخرُجُ مِن المَنصَّةِ إلى صاحِبِ المَتجَر — تَقرَأُ كُلَّ صِنفٍ كامِلاً
// (`TenantExportService.ReadAllAsync`) وتُنَقّيه بِـ`TenantExportRedaction` وَحدَه. الصُفوفُ هُنا عَلى Postgres المَحَلِّيّ بِمَخزَنِ المُضيفِ المُرَكَّب.

[Collection(LiveProofCollection.Name)]
public class LiveTenantExportErasedActorProofTests(ITestOutputHelper output)
{
    private static bool Enabled
        => LiveDealConcurrencyProofTests.Enabled && !string.IsNullOrEmpty(LiveDealConcurrencyProofTests.ConnectionString);

    private sealed record Seeded(Tenant Tenant, User Erased, User Live, string ErasedNameBefore);

    /// <summary>مَتجَرٌ بِمالِكٍ (في الذاكِرَة — الحَقيبَةُ لا تَقرَأُ وَثيقَتَه)، وعُضوانِ: واحِدٌ مَمحُوٌّ بِـ<see cref="AccountDeletion.Erase"/> وواحِدٌ قائِم، ولِكُلٍّ سَطرُ
    /// تَدقيقٍ فاعِلُه هُو، وسَطرٌ ثالِثٌ هَدَفُه حِسابُ المَمحُوِّ بِحاشِيَةٍ تَحمِلُ اسمَه (كَما كانَ يَكتُبُ بابا مَنحِ الإدارَة).</summary>
    private static async Task<Seeded> SeedAsync(IDocumentStore store, string slug)
    {
        const string name = "عُضوٌ يُغلِقُ حِسابَه";
        var owner = Guid.NewGuid();
        var erased = AccountDeletion.Erase(new User { Id = Guid.NewGuid(), TenantSlug = slug, FullName = name, Phone = "+966500000581" }, DateTime.UtcNow);
        var live = new User { Id = Guid.NewGuid(), TenantSlug = slug, FullName = "عُضوٌ قائِم", Phone = "+966500000582" };
        await using var s = store.LightweightSession(slug);
        s.Store(erased, live);
        s.Store(
            new AuditEntry { Id = Guid.NewGuid(), Scope = slug, ActorId = erased.Id, ActorName = name, Action = "listing.hide", EntityType = "listing", EntityId = Guid.NewGuid().ToString() },
            new AuditEntry { Id = Guid.NewGuid(), Scope = slug, ActorId = live.Id, ActorName = live.FullName, Action = "listing.hide", EntityType = "listing", EntityId = Guid.NewGuid().ToString() },
            new AuditEntry { Id = Guid.NewGuid(), Scope = slug, ActorId = owner, ActorName = "صاحِبُ المَتجَر", Action = "user.grant_admin", EntityType = "User", EntityId = erased.Id.ToString(), Note = name });
        await s.SaveChangesAsync();
        return new Seeded(new Tenant { Id = slug, Name = "مَتجَرُ التَخارُج", OwnerUserId = owner }, erased, live, name);
    }

    private static async Task CleanAsync(IDocumentStore store, string slug)
    {
        await using (var s = store.LightweightSession(slug))
        {
            s.DeleteWhere<AuditEntry>(a => a.Id != Guid.Empty);
            await s.SaveChangesAsync();
        }
        await LiveZeroRoleAdminProofTests.CleanAsync(store, slug);
    }

    /// <summary>صُفوفُ جَدوَلِ التَدقيقِ في الحَقيبَةِ كَما يَكتُبُها الكاتِب — نَصُّ JSON لِكُلِّ صَفٍّ بِخِياراتِ الحَقيبَةِ نَفسِها (<c>TenantExportRedaction.Json</c>).</summary>
    private static async Task<IReadOnlyList<string>> AuditRowsAsync(IDocumentStore store, Tenant tenant)
    {
        var service = new TenantExportService(store, new UnavailableFileStorage(), new AuditWriter(store));
        var content = await service.CollectAsync(tenant, tenant.OwnerUserId);
        var table = Assert.Single(content.Tables, t => t.TypeName == nameof(AuditEntry));
        Assert.Null(table.ReadErrorAr);
        return table.Rows.Select(r => r.ToJsonString(TenantExportRedaction.Json)).ToList();
    }

    /// <summary><b>تَوصيفٌ قَبلَ التَبديل (القاعِدَة ٣)</b>: الحَقيبَةُ تُخرِجُ اسمَ المَمحُوِّ كَما كُتِبَ في سَطرِ فاعِلِه وفي حاشِيَةِ السَطرِ الَّذي هَدَفُه حِسابُه —
    /// وصَفحَةُ السِجِلِّ نَفسُها تُخفيهِما (<c>AuditLogQueries.RecentAsync</c>). والقائِمُ بِاسمِه.</summary>
    [Fact]
    public async Task Today_the_store_export_carries_an_erased_members_name_in_the_audit_table_on_Postgres()
    {
        if (!Enabled) return;
        var slug = "zz-experase-" + Guid.NewGuid().ToString("N")[..8];
        await using var host = LiveTemplateDoorHost.Build();
        var seeded = await SeedAsync(host.Store, slug);
        try
        {
            var rows = await AuditRowsAsync(host.Store, seeded.Tenant);
            var carrying = rows.Count(r => r.Contains(seeded.ErasedNameBefore, StringComparison.Ordinal));
            var erasedMarks = rows.Count(r => r.Contains(AccountDeletion.ErasedName, StringComparison.Ordinal));
            var liveNamed = rows.Count(r => r.Contains(seeded.Live.FullName, StringComparison.Ordinal));
            output.WriteLine($"صُفوفُ التَدقيق {rows.Count} · تَحمِلُ اسمَ المَمحُوّ {carrying} · «حِسابٌ مَحذوف» {erasedMarks} · بِاسمِ القائِم {liveNamed}");
            Assert.Equal(3, rows.Count);
            Assert.Equal((2, 0, 1), (carrying, erasedMarks, liveNamed));
        }
        finally { await CleanAsync(host.Store, slug); }
    }
}
