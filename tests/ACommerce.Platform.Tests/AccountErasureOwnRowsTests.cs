using ACommerce.Kit.Auth;
using ACommerce.Kit.Favorites;
using ACommerce.Kit.SavedSearches;
using Marten;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ بَحثُه المَحفوظُ ومُفَضَّلاتُه بَعدَ حَذفِ حِسابِه — تَوصيفٌ قَبلَ التَبديل (‏2026-11-04، الموجَة ١٢ G1 · ٧؛ B-7) ══════════════════════════════════
//
// **ما يُثَبَّتُ هُنا لِيَبقى** (القاعِدَة ٣): بابُ الحَذفِ بِجِسمِه يَعودُ إلى `?deleted=1` ويَمحو حِسابَه، ولا يَمَسُّ بَحثَ عُضوٍ آخَرَ المَحفوظَ ولا مُفَضَّلَتَه.
// **وما يُطبَعُ ولا يُثَبَّت**: اليَومَ يَبقى بَحثُه المَحفوظُ مُفَعَّلاً — ومُطابَقَةُ كُلِّ إعلانٍ جَديدٍ تَكتُبُ لَه `saved_search_match` إلى الأَبَد — وتَبقى مُفَضَّلَتُه،
// وكِلاهُما يَخرُجُ في حَقيبَةِ التَخارُج (`TenantExportLedger`)؛ وصَفحَةُ الحَذفِ تَقولُ «واشتِراكاتُ الإشعارات» تُحذَف. وهذا يَتَغَيَّرُ في كوميتِ التَبديل؛ والمِلَفُّ يَبقى بِبَصمَتِه.

[Collection(LiveProofCollection.Name)]
public class LiveAccountErasureOwnRowsProofTests(ITestOutputHelper output)
{
    private static bool Enabled
        => LiveDealConcurrencyProofTests.Enabled && !string.IsNullOrEmpty(LiveDealConcurrencyProofTests.ConnectionString);

    internal sealed record Seeded(string Slug, User Me, User Other);

    /// <summary>عُضوانِ لِكُلٍّ بَحثٌ مَحفوظٌ مُفَعَّلٌ ومُفَضَّلَةٌ عَلى إعلان.</summary>
    internal static async Task<Seeded> SeedAsync(IDocumentStore store, string slug)
    {
        var me = new User { Id = Guid.NewGuid(), TenantSlug = slug, FullName = "عُضوٌ يَحفَظُ بَحثَه", Phone = "+966500000631", PhoneVerified = true };
        var other = new User { Id = Guid.NewGuid(), TenantSlug = slug, FullName = "عُضوٌ آخَرُ يَحفَظُ بَحثَه", Phone = "+966500000632", PhoneVerified = true };
        var listing = Guid.NewGuid();
        await using var s = store.LightweightSession(slug);
        s.Store(me, other);
        foreach (var u in new[] { me, other })
        {
            s.Store(new SavedSearch { Id = Guid.NewGuid(), UserId = u.Id, Label = "شُقَقٌ في الرِياض", City = "الرياض", IsEnabled = true });
            s.Store(new Favorite { Id = Favorite.MakeId(u.Id, listing), UserId = u.Id, ListingId = listing });
        }
        await s.SaveChangesAsync();
        return new Seeded(slug, me, other);
    }

    internal static async Task CleanAsync(IDocumentStore store, string slug)
    {
        await using (var s = store.LightweightSession(slug))
        {
            s.DeleteWhere<SavedSearch>(x => x.Id != Guid.Empty);
            s.DeleteWhere<Favorite>(x => x.Id != "");
            s.DeleteWhere<ACommerce.Kit.Notifications.Notification>(n => n.Id != Guid.Empty);
            await s.SaveChangesAsync();
        }
        await LiveZeroRoleAdminProofTests.CleanAsync(store, slug);
    }

    internal static async Task<(int Searches, int Favorites, int OtherSearches, int OtherFavorites)> CountAsync(IDocumentStore store, Seeded seeded)
    {
        await using var q = store.QuerySession(seeded.Slug);
        var searches = await Marten.QueryableExtensions.ToListAsync(q.Query<SavedSearch>());
        var favorites = await Marten.QueryableExtensions.ToListAsync(q.Query<Favorite>());
        return (searches.Count(x => x.UserId == seeded.Me.Id), favorites.Count(x => x.UserId == seeded.Me.Id),
            searches.Count(x => x.UserId == seeded.Other.Id), favorites.Count(x => x.UserId == seeded.Other.Id));
    }

    /// <summary>بابُ الحَذفِ بِجِسمِه: 302 إلى <c>?deleted=1</c> وحِسابُه مَمحُوّ، وبَحثُ الآخَرِ ومُفَضَّلَتُه باقِيان. ويُطبَعُ ما بَقِيَ لَه.</summary>
    [Fact]
    public async Task The_door_erases_him_and_leaves_another_members_saved_search_and_favourite_on_Postgres()
    {
        if (!Enabled) return;
        await using var host = LiveTemplateDoorHost.Build();
        var seeded = await SeedAsync(host.Store, "zz-eraown-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            var token = ACommerce.Kit.Auth.Server.AuthHandlers.MakeToken(seeded.Me.Id, seeded.Slug, seeded.Me.SessionVersion);
            var confirm = ("confirm", ACommerce.Platform.I18n.LocaleCatalog.Text(ACommerce.Platform.I18n.LocaleCatalog.Arabic, "account.delete.confirm_word"));
            var (status, at) = await host.PostFormAsync(LiveAccountErasureFilesProofTests.DeleteDoor, new Dictionary<string, string> { ["slug"] = seeded.Slug }, seeded.Slug, token, confirm);
            var (mine, myFavs, others, otherFavs) = await CountAsync(host.Store, seeded);
            await using var q = host.Store.QuerySession(seeded.Slug);
            var me = (await q.LoadAsync<User>(seeded.Me.Id))!;
            output.WriteLine($"البابُ: {status} → {at.Replace(seeded.Slug, "{slug}")} · بَحثُه المَحفوظ {mine} · مُفَضَّلَتُه {myFavs} · بَحثُ الآخَر {others} · مُفَضَّلَتُه {otherFavs}");
            Assert.Equal((302, $"/{seeded.Slug}?deleted=1"), (status, at));
            Assert.NotNull(me.DeletedAt);
            Assert.Equal((1, 1), (others, otherFavs));
        }
        finally { await CleanAsync(host.Store, seeded.Slug); }
    }
}
