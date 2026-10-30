using ACommerce.Kit.Auth;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using ACommerce.Templates.Customer.Marketplace.Services.Listings;
using Marten;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ إعلانُ حِسابٍ حُذِفَ قَبلَ سَحبِ الإعلانات — بابُ عَرضِ الصَفقَةِ وصَفحَتُه (‏2026-10-30، الموجَة ١٢ F2 · ٨) ══════════════════════════════════════
//
// الحَذفُ صارَ يَسحَبُ إعلاناتِ صاحِبِه (`AccountErasure.EraseCopiesAsync`، `8357c95`)، فَلا يَبلُغُ ما هُنا إلّا إعلانُ حِسابٍ حُذِفَ قَبلَ ذلك: قائِمٌ ومالِكُه مَمحُوّ.
// وبابا السَلَّةِ والعَرضِ عَلى العُروضِ يَسأَلانِ `CheckoutCart.OwnerGoneAsync` (B-PRIV-1)، وبابُ «قَدِّم عَرضاً» عَلى الصَفقَة `POST /{slug}/listings/{id}/deal` لا يَسأَلُه —
// والصَفحَةُ تَرسُمُ استِمارَتَه واستِمارَةَ السَلَّةِ في قِسمِ التَواصُلِ بِلا سُؤال (`ownerGone` يُحسَبُ لِقِسمِ العُروضِ وَحدَه).

/// <summary>صَفحَةُ الإعلان — شَكلُ قِسمِ التَواصُل.</summary>
public class ErasedOwnerListingSourceTests(ITestOutputHelper output)
{
    private static string Page() => WriteEndpointGuardTests.StripMarkupComments(File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot,
        "libs", "templates", "ACommerce.Templates.Customer.Marketplace", "Components", "Pages", "TenantListingDetail.razor")));

    /// <summary><b>تَوصيف</b>: <c>ownerGone</c> يُحسَبُ حينَ يُرسَمُ قِسمُ العُروضِ وَحدَه (<c>showOffers</c>)، واستِمارَتا السَلَّةِ والصَفقَةِ في قِسمِ التَواصُلِ
    /// لا يَسبِقُهُما سُؤالُه.</summary>
    [Fact]
    public void Today_the_listing_page_draws_the_cart_and_deal_forms_without_asking_whether_the_owner_is_gone()
    {
        var page = Page();
        var section = page.IndexOf("@if (!isOwner && !isOfferBased)", StringComparison.Ordinal);
        var cart = page.IndexOf("/cart/add\")\"", section, StringComparison.Ordinal);
        var deal = page.IndexOf("/deal\")\"", section, StringComparison.Ordinal);
        var asked = page.IndexOf("ownerGone", section, StringComparison.Ordinal);
        output.WriteLine($"القِسم {section} · السَلَّة {cart} · الصَفقَة {deal} · أَوَّلُ ownerGone بَعدَ القِسم {asked}");
        Assert.True(section > 0 && cart > section && deal > section);
        Assert.True(asked < 0 || asked > Math.Max(cart, deal));
        Assert.Contains("if (showOffers && ownerId is { } seller)", page, StringComparison.Ordinal);
    }
}

[Collection(LiveProofCollection.Name)]
public class LiveErasedOwnerListingProofTests(ITestOutputHelper output)
{
    private static bool Enabled
        => LiveDealConcurrencyProofTests.Enabled && !string.IsNullOrEmpty(LiveDealConcurrencyProofTests.ConnectionString);

    private const string DealDoor = "/{slug}/listings/{id:guid}/deal";

    /// <summary>مُشتَرٍ بِرَمزِ جَلسَة، ومالِكٌ — مَمحُوٌّ حينَ يُطلَب — وإعلانُه القائِمُ (حِسابٌ حُذِفَ قَبلَ سَحبِ الإعلانات).</summary>
    internal static async Task<(User Buyer, User Owner, Guid Listing, string Token)> SeedAsync(IDocumentStore store, string slug, bool erased)
    {
        var buyer = new User { Id = Guid.NewGuid(), TenantSlug = slug, FullName = "مُشتَرٍ يَعرِض", Phone = "+966500000591", PhoneVerified = true };
        var owner = new User { Id = Guid.NewGuid(), TenantSlug = slug, FullName = "مالِكٌ قَديم", Phone = "+966500000592", PhoneVerified = true };
        if (erased) owner = AccountDeletion.Erase(owner, DateTime.UtcNow);
        var listing = Guid.NewGuid();
        await using var s = store.LightweightSession(slug);
        s.Store(buyer, owner);
        s.Events.StartStream<ACommerce.Kit.Listings.Listing>(listing, new ACommerce.Kit.Listings.ListingCreated(listing, slug, "سِلعَةٌ قائِمَة", null, 120m, "c",
            null, null, new Dictionary<string, string> { [ListingEditService.OwnerAttribute] = owner.Id.ToString() }, DateTime.UtcNow));
        await s.SaveChangesAsync();
        return (buyer, owner, listing, ACommerce.Kit.Auth.Server.AuthHandlers.MakeToken(buyer.Id, slug, buyer.SessionVersion));
    }

    internal static async Task CleanAsync(IDocumentStore store, string slug, params Guid[] streams)
    {
        await using (var s = store.LightweightSession(slug))
        {
            s.DeleteWhere<Deal>(d => d.Id != Guid.Empty);
            s.DeleteWhere<ACommerce.Kit.Notifications.Notification>(n => n.Id != Guid.Empty);
            s.DeleteWhere<ACommerce.Kit.Listings.Listing>(l => l.Id != Guid.Empty);
            foreach (var id in streams) s.Events.ArchiveStream(id);
            await s.SaveChangesAsync();
        }
        await LiveZeroRoleAdminProofTests.CleanAsync(store, slug);
    }

    /// <summary><b>تَوصيف</b>: بابُ «قَدِّم عَرضاً» يُولِدُ صَفقَةً عَلى إعلانِ حِسابٍ مَمحُوٍّ ويَربِطُ مالِكَها بِمُعَرِّفِه — صَفقَةٌ لا يُنَفِّذُها أَحَد.</summary>
    [Fact]
    public async Task Today_the_offer_deal_door_opens_a_deal_on_a_listing_whose_owner_erased_his_account_on_Postgres()
    {
        if (!Enabled) return;
        var slug = "zz-ownergone-" + Guid.NewGuid().ToString("N")[..8];
        await using var host = LiveTemplateDoorHost.Build();
        var (buyer, owner, listing, token) = await SeedAsync(host.Store, slug, erased: true);
        try
        {
            var (status, location) = await host.PostFormAsync(DealDoor, new Dictionary<string, string> { ["slug"] = slug, ["id"] = listing.ToString() },
                slug, token, ("amount", "100"), ("note", "عَرضٌ"));
            await using var q = host.Store.QuerySession(slug);
            var deals = (await q.Query<Deal>().ToListAsync()).ToList();
            output.WriteLine($"البابُ: {status} → {location} · صَفَقات {deals.Count} · مَربوطَةٌ بِالمَمحُوّ {deals.Count(d => d.Refs.GetValueOrDefault("listing_owner") == owner.Id.ToString())}");
            var deal = Assert.Single(deals);
            Assert.Equal((302, $"/{slug}/deals/{deal.Id}"), (status, location));
            Assert.Equal((buyer.Id, owner.Id.ToString()), (deal.InitiatorId, deal.Refs["listing_owner"]));
        }
        finally { await CleanAsync(host.Store, slug, listing); }
    }
}
