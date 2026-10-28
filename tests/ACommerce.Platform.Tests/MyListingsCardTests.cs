using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ─── د41: بِطاقَةُ «إعلاناتي» في «حِسابي» لِمَن لا يَنشُرُ ولا إعلانَ لَه ────────────────────
//
// قَرارُ المالِك د41 «أ» (‏2026-10-28): «تُخفى البِطاقَةُ في هذِه الحالَةِ وَحدَها: دَورٌ لا يَنشُرُ ولَيسَ لِصاحِبِه إعلانٌ واحِد. وتَبقى لِكُلِّ مَن
// يَستَطيعُ النَشرَ أَو لَه إعلاناتٌ سابِقَة، وتَبقى كَما هِيَ في المَتاجِرِ الَّتي بِلا أَدوار.» وتَنفيذُه يَبدَأُ «بِاختِبارٍ يُثَبِّتُ صَفحَةَ «حِسابي» كَما هِيَ
// اليَوم» (القاعِدَة ٣) — وهذا هُو: البِطاقَةُ تَحتَ الشُروطِ نَفسِها الَّتي تَحتَها بِطاقَةُ «المُفَضَّلَة» غَيرُ المَشروطَة، أَي لِكُلِّ صاحِبِ جَلسَة.
//
// ‏(‏2026-10-28، كوميتُ تَبديلِ د41؛ يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه): قُلِبَ صَفُّ «اليَوم» بِاسمِه — البِطاقَةُ تَحتَ <c>showMyListings</c> وَحدَه فَوقَ
// ‏شُروطِ «المُفَضَّلَة»، و«يَستَطيعُ النَشرَ» يُسأَلُ بِدالَّةِ حارِسِ النَشرِ نَفسِها (‏<c>PermissionDoor.Opens</c>) عَلى قِراءَةِ صَفحَتِه.

public class MyListingsCardTests(ITestOutputHelper output)
{
    internal static string MePath => Path.Combine(ThemeZeroEquivalenceTests.RepoRoot,
        "libs", "templates", "ACommerce.Templates.Customer.Marketplace", "Components", "Pages", "Me.razor");

    internal const string ListingsCard = "<AcActionCard Label=\"@(L[\"me.action.my_listings\"])\"";
    internal const string FavoritesCard = "<AcActionCard Label=\"@(L[\"me.action.favorites\"])\"";

    internal static (IReadOnlyList<string> Listings, IReadOnlyList<string> Favorites) CardConditions()
    {
        var page = WriteEndpointGuardTests.StripMarkupComments(File.ReadAllText(MePath));
        var markup = page[..page.IndexOf("@code {", StringComparison.Ordinal)];
        return (UpgradePromptPriceTests.EnclosingIfConditions(markup, markup.IndexOf(ListingsCard, StringComparison.Ordinal)),
                UpgradePromptPriceTests.EnclosingIfConditions(markup, markup.IndexOf(FavoritesCard, StringComparison.Ordinal)));
    }

    /// <summary><b>قُلِبَ «Today_» بِاسمِه (‏2026-10-28، كوميتُ تَبديلِ د41 «أ»)</b> — كانَ
    /// <c>Today_the_my_listings_card_is_drawn_under_the_favorites_cards_conditions_alone</c> (‏<c>d1f789b</c>): شُروطُ «إعلاناتي» هِيَ شُروطُ
    /// «المُفَضَّلَة» بِحَرفِها، فَيَراها مَن دَورُه لا يَنشُرُ ولا إعلانَ لَه. والآن شُروطُها شُروطُ «المُفَضَّلَة» ومَعَها <c>showMyListings</c> وَحدَه —
    /// شَرطٌ واحِدٌ يَخُصُّها (قَرارُ المالِك: «ثُمَّ شَرطٌ واحِد»).</summary>
    [Fact]
    public void The_my_listings_card_is_drawn_under_the_favorites_cards_conditions_and_showMyListings_alone()
    {
        var (listings, favorites) = CardConditions();
        output.WriteLine($"شُروطُ «إعلاناتي»: [{string.Join(" | ", listings)}] · شُروطُ «المُفَضَّلَة»: [{string.Join(" | ", favorites)}]");
        Assert.Equal(favorites.Append("showMyListings"), listings);
        Assert.Single(listings, c => c.Contains("istings", StringComparison.Ordinal));
    }

    /// <summary><b>«يَستَطيعُ النَشرَ» يُسأَلُ بِالدالَّةِ نَفسِها الَّتي يَسأَلُها حارِسُ النَشرِ وعَلى قِراءَتَيه</b> (قَرارُ المالِك د41): <c>PermissionDoor.Opens</c>
    /// عَلى أَدوارِ <c>TenantRoleService.LoadAsync</c> المُجَسَّدَة — قِراءَةُ <c>GatedPage</c> بِحَرفِها بَعدَ د42 — والدَورُ المُخَزَّنُ مِن وَثيقَةِ
    /// المُستَخدِمِ نَفسِها الَّتي يَقرَؤُها <c>AccountQueries.ActiveRoleAsync</c>؛ ثُمَّ «لَه إعلانٌ» بِـ<c>MyListingsAsync</c> الَّتي تَرسُمُ «إعلاناتي»،
    /// ولا تُسأَلُ إلّا إن أُغلِقَ البابُ (‏<c>||</c>). ولا <c>RolePermissions.Has</c> بِيَدِ الصَفحَةِ لِـ<c>listing.create</c>.</summary>
    [Fact]
    public void ShowMyListings_asks_the_publish_guards_own_door_on_the_publish_pages_reads_then_the_members_own_listings()
    {
        var code = WriteEndpointGuardTests.StripComments(WriteEndpointGuardTests.StripMarkupComments(File.ReadAllText(MePath)));
        var gate = WriteEndpointGuardTests.StripMarkupComments(File.ReadAllText(Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(MePath))!, "GatedPage.razor")));
        var sites = System.Text.RegularExpressions.Regex.Matches(code, @"showMyListings\s*=").Count;
        output.WriteLine($"إسناداتُ showMyListings: {sites} · بابُ النَشرِ في GatedPage: {gate.Contains("PermissionDoor.Opens(tenant.Roles, Role, storedRole, Permission)", StringComparison.Ordinal)}");

        Assert.Equal(1, sites);
        Assert.Contains("@inject ACommerce.Templates.Customer.Marketplace.Services.TenantRoleService Roles", code, StringComparison.Ordinal);
        Assert.Contains("var publishRoles = (await Roles.LoadAsync(Slug)).Tenant?.Roles ?? new List<ACommerce.Kit.Roles.Role>();", code, StringComparison.Ordinal);
        Assert.Contains("showMyListings = ACommerce.Templates.Customer.Marketplace.Gates.PermissionDoor.Opens(publishRoles, Role, me?.ActiveRole, \"listing.create\")\n"
            + "            || (await Accounts.MyListingsAsync(Slug, Auth.UserId.Value)).Count > 0;", code.Replace("\r\n", "\n", StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.DoesNotContain("RolePermissions.Has(tenantRoles, Role ?? me?.ActiveRole, \"listing.create\")", code, StringComparison.Ordinal);
        // ‏قِراءَتا حارِسِ الهَدَفِ بِحَرفِهِما: الأَدوارُ المُجَسَّدَة، والدَورُ المُخَزَّنُ مِن وَثيقَةِ المُستَخدِم.
        Assert.Contains("var tenant = (await Roles.LoadAsync(Slug)).Tenant;", gate, StringComparison.Ordinal);
        Assert.Contains("PermissionDoor.Opens(tenant.Roles, Role, storedRole, Permission)", gate, StringComparison.Ordinal);
    }

    /// <summary><b>الجَوابُ لِكُلِّ حالَةٍ يُسَمّيها القَرار</b> — مُوجَبٌ وسالِب: مَتجَرٌ بِلا أَدوارٍ تَبقى فيه البِطاقَة (‏<c>listing.create</c> صَلاحِيَّةُ عُضوٍ
    /// فَيَنشُرُ فيه الجَميعُ بَعدَ ‏ADR-040)؛ ودَورٌ يَنشُرُ (‏<c>vendor</c>) تَبقى لَه؛ ودَورٌ لا يَنشُرُ (‏<c>customer</c>) يُغلَقُ بابُه — فَلا تَبقى لَه إلّا بِإعلانٍ لَه؛
    /// ودَورٌ غائِبٌ في مَتجَرٍ ذي أَدوارٍ مُغلَق. يُطبَعُ ما عُدَّ ويَفشَلُ عَلى الصِفر.</summary>
    [Fact]
    public void The_door_keeps_the_card_for_a_zero_role_store_and_a_publishing_role_and_closes_it_for_a_role_that_does_not_publish()
    {
        var roles = new[] { "vendor", "customer" }
            .Select((slug, i) => ACommerce.Kit.Roles.RoleCatalog.InstantiateRole(ACommerce.Kit.Roles.RoleCatalog.Find(slug)!, i)).ToList();
        var none = Array.Empty<ACommerce.Kit.Roles.Role>();
        bool Door(IReadOnlyCollection<ACommerce.Kit.Roles.Role> r, string? stored) =>
            ACommerce.Templates.Customer.Marketplace.Gates.PermissionDoor.Opens(r, null, stored, "listing.create");
        bool Shown(bool door, int listings) => door || listings > 0; // ‏تَعبيرُ Me.razor بِحَرفِه (يُثَبِّتُه الصَفُّ السابِق)
        var rows = new (string Case, bool Door, int Listings, bool Expected)[]
        {
            ("صِفرُ أَدوار · بِلا دَور", Door(none, null), 0, true),
            ("vendor · بِلا إعلان", Door(roles, "vendor"), 0, true),
            ("customer · بِلا إعلان", Door(roles, "customer"), 0, false),
            ("customer · لَه إعلان", Door(roles, "customer"), 1, true),
            ("بِلا دَورٍ في مَتجَرٍ ذي أَدوار · بِلا إعلان", Door(roles, null), 0, false),
        };
        output.WriteLine($"حالات: {rows.Length} · " + string.Join(" · ", rows.Select(r => $"{r.Case}: بابٌ {r.Door} ⇒ بِطاقَة {Shown(r.Door, r.Listings)}")));

        Assert.Equal(5, rows.Length);
        Assert.Equal(2, roles.Count);
        Assert.All(rows, r => Assert.Equal(r.Expected, Shown(r.Door, r.Listings)));
        Assert.True(roles.Single(r => r.Slug == "vendor").Permissions.Contains("listing.create"));
        Assert.DoesNotContain("listing.create", roles.Single(r => r.Slug == "customer").Permissions);
    }
}
