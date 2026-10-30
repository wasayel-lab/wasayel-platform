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
// والصَفحَةُ تَرسُمُ استِمارَتَه واستِمارَةَ السَلَّةِ في قِسمِ التَواصُلِ بِلا سُؤال (`ownerGone` يُحسَبُ لِقِسمِ العُروضِ وَحدَه) — تَوصيفُه `557b8e2`. والتَبديلُ:
// البابُ يَسأَلُ `OwnerGoneAsync` قَبلَ `StartAsync` ويَرُدُّ بِـ`owner_gone`، وقِسمُ التَواصُلِ يَسأَلُ `ownerGone` قَبلَ الاستِمارَتَين؛ وصُفوفُ «Today_» قُلِبَت بِأَسمائِها.

/// <summary>صَفحَةُ الإعلان — شَكلُ قِسمِ التَواصُل.</summary>
public class ErasedOwnerListingSourceTests(ITestOutputHelper output)
{
    private static string Page() => WriteEndpointGuardTests.StripMarkupComments(File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot,
        "libs", "templates", "ACommerce.Templates.Customer.Marketplace", "Components", "Pages", "TenantListingDetail.razor")));

    /// <summary><b>قُلِبَ «Today_» بِاسمِه</b> — كانَ <c>Today_the_listing_page_draws_the_cart_and_deal_forms_without_asking_whether_the_owner_is_gone</c>
    /// (‏<c>557b8e2</c>). والآن قِسمُ التَواصُلِ يَسأَلُ <c>ownerGone</c> قَبلَ الاستِمارَتَين ويَرسُمُ سَطرَ <c>err_offer_owner_gone</c> مَكانَهُما، و<c>ownerGone</c> يُحسَبُ
    /// لِلقِسمِ أَيضاً بِسُؤالِ الأَبوابِ نَفسِه (<c>CheckoutCart.OwnerGone</c>) عَلى حِسابٍ يُقرَأُ مَرَّة.</summary>
    [Fact]
    public void The_listing_page_asks_the_doors_owner_gone_question_before_the_cart_and_deal_forms()
    {
        var page = Page();
        var section = page.IndexOf("@if (!isOwner && !isOfferBased)", StringComparison.Ordinal);
        var asked = page.IndexOf("@if (Auth.IsAuthenticated && ownerGone)", section, StringComparison.Ordinal);
        var line = page.IndexOf("@L.Markup(\"listings.detail.err_offer_owner_gone\")", asked, StringComparison.Ordinal);
        var cart = page.IndexOf("/cart/add\")\"", section, StringComparison.Ordinal);
        var deal = page.IndexOf("/deal\")\"", section, StringComparison.Ordinal);
        output.WriteLine($"القِسم {section} · السُؤال {asked} · السَطر {line} · السَلَّة {cart} · الصَفقَة {deal}");
        Assert.True(section > 0 && asked > section && line > asked && cart > line && deal > line);
        Assert.Contains("else if (Auth.IsAuthenticated)", page[asked..cart], StringComparison.Ordinal);
        Assert.Contains("if ((showOffers || contact) && ownerId is not null)", page, StringComparison.Ordinal);
        Assert.Contains("ownerGone = ACommerce.Templates.Customer.Marketplace.Services.Deals.CheckoutCart.OwnerGone(listing, ownerAccount);", page, StringComparison.Ordinal);
    }

    /// <summary><b>السكربتُ</b> (‏<c>scripts/withdraw-erased-owner-listings.cs</c>): يَعرِضُ افتِراضاً ويَخرُجُ بِـ2 بِالأَمرِ الدَقيق، ولا يَكتُبُ إلّا بِـ<c>--apply</c> وعَدَدٍ يُطابِقُ
    /// العَرض، ويَكتُبُ بَيانَ ما سيُمَسُّ قَبلَ أَوَّلِ حِفظ، ويَسأَلُ دَوالَّ <c>AccountErasure</c> الَّتي يُنادِيها بابُ الحَذفِ لا نُسخَةً مِنها، ويُسَجِّلُ نُسخَتَي
    /// <c>Listing</c> و<c>Offer</c> وتَزامُنَ الصَفقَة، ويَفشَلُ عَلى صِفرِ مَتجَر، ولا يَمَسُّ المُخَطَّط — ولا اسمَ مَتجَرٍ مُستَثنى، ولا مُرَشِّحَ واجِهَة.</summary>
    [Fact]
    public void The_withdraw_script_shows_by_default_backs_up_first_and_reuses_the_deletion_doors_functions()
    {
        var path = Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, "scripts", "withdraw-erased-owner-listings.cs");
        var text = File.ReadAllText(path);
        Assert.True(text.Length > 3000, $"أَداةٌ عَمياء: {path} طولُه {text.Length}.");
        var needles = new[]
        {
            "var apply      = args.Contains(\"--apply\");", "if (!apply)", "return 2;", "if (confirm is null || confirm != changes)",
            "--apply --confirm {changes}", "AutoCreateSchemaObjects = AutoCreate.None", "✗ BLIND CHECK",
            "AccountErasure.WithdrawalPlanAsync(", "AccountErasure.AppendWithdrawal(", "AccountErasure.CloseIncomingAsync(",
            "o.Projections.Snapshot<Listing>(SnapshotLifecycle.Inline);", "o.Projections.Snapshot<Offer>(SnapshotLifecycle.Inline);", "DealsService.RegisterDocument(o);",
            "File.WriteAllTextAsync(manifestPath", "\"listing.withdraw_erased_owner\"",
        };
        foreach (var n in needles) Assert.Contains(n, text, StringComparison.Ordinal);
        var gate = text.IndexOf("if (confirm is null || confirm != changes)", StringComparison.Ordinal);
        var manifest = text.IndexOf("File.WriteAllTextAsync(manifestPath", StringComparison.Ordinal);
        var firstSave = text.IndexOf("SaveChangesAsync", StringComparison.Ordinal);
        output.WriteLine($"عِباراتٌ مَطلوبَة {needles.Length} · العَرض {text.IndexOf("if (!apply)", StringComparison.Ordinal)} · التَأكيد {gate} · البَيان {manifest} · أَوَّلُ حِفظ {firstSave}");
        Assert.True(gate > text.IndexOf("if (!apply)", StringComparison.Ordinal) && manifest > gate && firstSave > manifest, "كِتابَةٌ قَبلَ العَرضِ أَو التَأكيدِ أَو البَيان.");
        Assert.DoesNotContain("\"ashare\"", text, StringComparison.Ordinal);
        // ‏ولا مُرَشِّحَ في الواجِهَة: السَحبُ يَحِلُّ مَحَلَّه — قاعِدَةٌ واحِدَة.
        var storefront = File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, "libs", "templates", "ACommerce.Templates.Customer.Marketplace", "Services", "Queries", "StorefrontQueries.cs"));
        Assert.DoesNotContain("OwnerGone", storefront, StringComparison.Ordinal);
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

    /// <summary><b>قُلِبَ «Today_» بِاسمِه</b> — كانَ <c>Today_the_offer_deal_door_opens_a_deal_on_a_listing_whose_owner_erased_his_account_on_Postgres</c>
    /// (‏<c>557b8e2</c>: صَفقَةٌ مَربوطَةٌ بِالمَمحُوّ). والآن البابُ يَعودُ إلى الإعلانِ بِـ<c>owner_gone</c> ولا يَكتُبُ صَفقَة.</summary>
    [Fact]
    public async Task The_offer_deal_door_refuses_a_listing_whose_owner_erased_his_account_with_owner_gone_and_writes_no_deal_on_Postgres()
    {
        if (!Enabled) return;
        var slug = "zz-ownergone-" + Guid.NewGuid().ToString("N")[..8];
        await using var host = LiveTemplateDoorHost.Build();
        var (_, _, listing, token) = await SeedAsync(host.Store, slug, erased: true);
        try
        {
            var (status, location) = await host.PostFormAsync(DealDoor, new Dictionary<string, string> { ["slug"] = slug, ["id"] = listing.ToString() },
                slug, token, ("amount", "100"), ("note", "عَرضٌ"));
            await using var q = host.Store.QuerySession(slug);
            var deals = (await q.Query<Deal>().ToListAsync()).Count;
            output.WriteLine($"البابُ: {status} → {location} · صَفَقات {deals}");
            Assert.Equal((302, $"/{slug}/listings/{listing}?err={CheckoutCart.OwnerGoneCode}"), (status, location));
            Assert.Equal(0, deals);
        }
        finally { await CleanAsync(host.Store, slug, listing); }
    }

    /// <summary><b>السالِب</b>: مالِكٌ قائِم — الصَفقَةُ تولَدُ كَما كانَت ومالِكُها مَربوط.</summary>
    [Fact]
    public async Task The_offer_deal_door_still_opens_a_deal_for_a_live_owner_on_Postgres()
    {
        if (!Enabled) return;
        var slug = "zz-ownergone-" + Guid.NewGuid().ToString("N")[..8];
        await using var host = LiveTemplateDoorHost.Build();
        var (buyer, owner, listing, token) = await SeedAsync(host.Store, slug, erased: false);
        try
        {
            var (status, location) = await host.PostFormAsync(DealDoor, new Dictionary<string, string> { ["slug"] = slug, ["id"] = listing.ToString() },
                slug, token, ("amount", "100"), ("note", "عَرضٌ"));
            await using var q = host.Store.QuerySession(slug);
            var deal = Assert.Single(await q.Query<Deal>().ToListAsync());
            output.WriteLine($"البابُ: {status} → {location} · مالِكُها {deal.Refs.GetValueOrDefault("listing_owner")}");
            Assert.Equal((302, $"/{slug}/deals/{deal.Id}"), (status, location));
            Assert.Equal((buyer.Id, owner.Id.ToString()), (deal.InitiatorId, deal.Refs["listing_owner"]));
        }
        finally { await CleanAsync(host.Store, slug, listing); }
    }
}
