using ACommerce.Kit.Auth;
using ACommerce.Kit.Favorites;
using ACommerce.Kit.SavedSearches;
using ACommerce.Templates.Customer.Marketplace.Services.Audit;
using ACommerce.Templates.Customer.Marketplace.Services.Export;
using Marten;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ بَحثُه المَحفوظُ ومُفَضَّلاتُه تُحذَفُ مَعَه (‏2026-11-04، الموجَة ١٢ G1 · ٧ — B-7؛ تَوصيفُه 1ba85e0) ══════════════════════════════════════════════
//
// قَبلَه («بَحثُه المَحفوظ 1 · مُفَضَّلَتُه 1» — `LiveAccountErasureOwnRowsProofTests` يَطبَعُه ويَبقى بِبَصمَتِه). والآن `EraseCopiesAsync` يَحذِفُهُما في المُعامَلَةِ نَفسِها
// ويَعُدُّهُما في `Counts`؛ والشَكلُ يُسأَلُ هُنا، والحَيُّ بِالبابِ نَفسِه وبِحَقيبَةِ التَخارُجِ بَعدَه.

public class AccountErasureOwnRowsShapeTests
{
    /// <summary>المَحوُ يَحذِفُ بَحثَه ومُفَضَّلاتِه بِمُعَرِّفِه (لا بِالاسم)، قَبلَ الحِفظِ الواحِدِ لِلبابِ لا بَعدَه، ويَعُدُّهُما.</summary>
    [Fact]
    public void The_erasure_deletes_his_saved_searches_and_favourites_by_his_id_inside_the_doors_one_save()
    {
        var src = WriteEndpointGuardTests.StripComments(File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, "libs", "templates",
            "ACommerce.Templates.Customer.Marketplace", "Services", "Identity", "AccountErasure.cs")));
        var copies = src[src.IndexOf("public static async Task<Counts> EraseCopiesAsync(", StringComparison.Ordinal)..];
        copies = copies[..copies.IndexOf("public sealed record WithdrawalPlan(", StringComparison.Ordinal)];
        Assert.Contains("s.Query<ACommerce.Kit.SavedSearches.SavedSearch>().Where(x => x.UserId == userId)", copies, StringComparison.Ordinal);
        Assert.Contains("s.Query<ACommerce.Kit.Favorites.Favorite>().Where(x => x.UserId == userId)", copies, StringComparison.Ordinal);
        Assert.Contains("SavedSearches = searches.Count,", copies, StringComparison.Ordinal);
        Assert.Contains("Favorites = favorites.Count,", copies, StringComparison.Ordinal);
        Assert.DoesNotContain("SaveChangesAsync", copies, StringComparison.Ordinal);
    }
}

[Collection(LiveProofCollection.Name)]
public class LiveAccountErasureOwnRowsSwitchProofTests(ITestOutputHelper output)
{
    private static bool Enabled
        => LiveDealConcurrencyProofTests.Enabled && !string.IsNullOrEmpty(LiveDealConcurrencyProofTests.ConnectionString);

    /// <summary>قُلِبَ ما طَبَعَه التَوصيف. <b>المُوجَب</b>: بَعدَ البابِ لا بَحثَ مَحفوظاً لَه ولا مُفَضَّلَة، وحَقيبَةُ التَخارُجِ لا تَحمِلُ صَفّاً بِمُعَرِّفِه في الجَدوَلَين.
    /// <b>والسالِب</b>: بَحثُ العُضوِ الآخَرِ ومُفَضَّلَتُه في المَخزَنِ وفي الحَقيبَة.</summary>
    [Fact]
    public async Task The_door_deletes_his_saved_search_and_favourite_and_the_export_ships_only_the_other_members_on_Postgres()
    {
        if (!Enabled) return;
        await using var host = LiveTemplateDoorHost.Build();
        var seeded = await LiveAccountErasureOwnRowsProofTests.SeedAsync(host.Store, "zz-eraownsw-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            var token = ACommerce.Kit.Auth.Server.AuthHandlers.MakeToken(seeded.Me.Id, seeded.Slug, seeded.Me.SessionVersion);
            var confirm = ("confirm", ACommerce.Platform.I18n.LocaleCatalog.Text(ACommerce.Platform.I18n.LocaleCatalog.Arabic, "account.delete.confirm_word"));
            var (status, at) = await host.PostFormAsync(LiveAccountErasureFilesProofTests.DeleteDoor, new Dictionary<string, string> { ["slug"] = seeded.Slug }, seeded.Slug, token, confirm);
            var (mine, myFavs, others, otherFavs) = await LiveAccountErasureOwnRowsProofTests.CountAsync(host.Store, seeded);
            var tenant = new ACommerce.Kit.Tenants.Tenant { Id = seeded.Slug, Name = "مَتجَرُ البَحث", OwnerUserId = Guid.NewGuid() };
            var content = await new TenantExportService(host.Store, new ACommerce.Kit.Files.UnavailableFileStorage(), new AuditWriter(host.Store)).CollectAsync(tenant, tenant.OwnerUserId);
            int Rows(string type, Guid user) => Assert.Single(content.Tables, t => t.TypeName == type).Rows.Count(r => r["userId"]?.ToString() == user.ToString());
            output.WriteLine($"البابُ: {status} → {at.Replace(seeded.Slug, "{slug}")} · بَحثُه المَحفوظ {mine} · مُفَضَّلَتُه {myFavs} · بَحثُ الآخَر {others} · مُفَضَّلَتُه {otherFavs} · "
                + $"الحَقيبَة: بَحثُه {Rows(nameof(SavedSearch), seeded.Me.Id)} · مُفَضَّلَتُه {Rows(nameof(Favorite), seeded.Me.Id)} · لِلآخَر {Rows(nameof(SavedSearch), seeded.Other.Id)}/{Rows(nameof(Favorite), seeded.Other.Id)}");
            Assert.Equal((302, $"/{seeded.Slug}?deleted=1"), (status, at));
            Assert.Equal((0, 0, 1, 1), (mine, myFavs, others, otherFavs));
            Assert.Equal((0, 0), (Rows(nameof(SavedSearch), seeded.Me.Id), Rows(nameof(Favorite), seeded.Me.Id)));
            Assert.Equal((1, 1), (Rows(nameof(SavedSearch), seeded.Other.Id), Rows(nameof(Favorite), seeded.Other.Id)));
        }
        finally { await LiveAccountErasureOwnRowsProofTests.CleanAsync(host.Store, seeded.Slug); }
    }
}
