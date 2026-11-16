using System.Text.RegularExpressions;
using ACommerce.Kit.Roles;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ نَموذَجُ «قَدِّم عَرضاً» يُرسَمُ لِدَورٍ لا يُقَدِّم — ورَدُّ النُقطَةِ `?err=forbidden` لا تَقرَؤُه الصَفحَة ═══
//
// **تَدقيقُ الإطلاقِ عِندَ `dace7cd`، تَيّارُ W8-7**: ‏`lens-dead-doors-7` (صُغرى). أُعيدَ قِياسُه عِندَ الرَأسِ `2201bd4` (‏2026-10-25):
// - ‏`TenantListingDetail.razor` يَرسُمُ استِمارَةَ `/{slug}/listings/{id}/offers` لِكُلِّ صاحِبِ جَلسَةٍ لَيسَ مالِكَ الإعلان، بِلا سُؤالِ `offer.submit`.
// - ونُقطَتُها تَسأَلُ `HasPermissionAsync(…, "offer.submit", …)` قَبلَ أَيِّ حَقلٍ وتَرُدُّ إلى `listings/{id}?err=forbidden` — والصَفحَةُ لا فَرعَ لَها
//   بِهذا الرَمز، فَيَعودُ المُستَخدِمُ إلى الصَفحَةِ نَفسِها والاستِمارَةُ مَرسومَةٌ بِلا كَلِمَة.
// - خَمسَةُ أَدوارٍ في الكاتالوجِ بِلا `offer.submit`: broker وhost وorganizer وrider وvendor.
// - **حَيّاً** (قاعِدَةٌ مَحَلِّيَّة `wasayel_w87`، الرَأسُ `7b8de7f`، إعلانٌ يَقبَلُ العُروضَ في `ejar`): المُضيفُ يَرى `id="offerForm"`،
//   و`?err=forbidden` يَرسُمُ الصَفحَةَ نَفسَها بايتاً (بَعدَ نَزعِ حالَةِ Blazor المُشَفَّرَة).
//
// هذا المِلَفُّ يُوَصِّفُ «اليَوم» (القاعِدَة ٣) قَبلَ أَيِّ تَبديل؛ صَفّا `Today_` يُقلَبانِ بِاسمَيهِما في كوميتِ الإصلاح، والباقي يَبقى بِلا تَعديلِ حَرف.
public class OfferFormDoorTests(ITestOutputHelper output)
{
    private const string TemplateRoot = "libs/templates/ACommerce.Templates.Customer.Marketplace";
    private const string DetailPage = TemplateRoot + "/Components/Pages/TenantListingDetail.razor";
    private const string Permission = "offer.submit";

    private static string Read(string relative)
    {
        var path = Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, relative.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(path), $"مَصدَرٌ مَفقود: {relative} — الأَداةُ عَمياءُ بِلا طَرَفٍ مَقروء.");
        return File.ReadAllText(path);
    }

    private static string Page() => WriteEndpointGuardTests.StripMarkupComments(Read(DetailPage));

    /// <summary>جِسمُ نُقطَةِ العَرضِ مِن <c>MapPost</c> إلى السَطرِ الَّذي يَقرَأُ أَوَّلَ عُروضِ المُطابَقَة — يَكفي لِتَرتيبِ الحارِسِ والحُقول.</summary>
    private static string OfferEndpointHead()
    {
        var mte = WriteEndpointGuardTests.StripComments(Read(TemplateRoot + "/MarketplaceTemplateExtensions.cs"));
        var at = mte.IndexOf("app.MapPost(\"/{slug}/listings/{id:guid}/offers\"", StringComparison.Ordinal);
        Assert.True(at > 0, "نُقطَةُ العَرضِ غَيرُ مَوجودَة — الأَداةُ عَمياء.");
        var end = mte.IndexOf("ListingMatch>()", at, StringComparison.Ordinal);
        Assert.True(end > at, "جِسمُ نُقطَةِ العَرضِ لا يُقَصّ.");
        return mte[at..end];
    }

    // ─── «اليَوم» ───────────────────────────────────────────────────────────────────────────

    /// <summary><b>قُلِبَ «Today_» بِاسمِه (‏2026-10-25، كوميتُ إصلاحِ <c>lens-dead-doors-7</c>)</b> — كانَ
    /// <c>Today_the_offer_form_is_drawn_for_every_signed_in_non_owner_whatever_the_role</c> (‏<c>4b74e0b</c>): شُروطُها <c>showOffers</c> ثُمَّ
    /// <c>Auth.IsAuthenticated &amp;&amp; !isOwner</c>. والآن شِقٌّ ثالِثٌ <c>canOffer</c> — <c>PermissionDoor.Opens(…, "offer.submit")</c> بِقِراءَتَي
    /// <c>HasPermissionAsync</c> (الأَدوارُ المُجَسَّدَةُ والدَورُ المُخَزَّن) — والفَرعُ الَّذي يَليها لِصاحِبِ الجَلسَةِ نَفسِه سَطرُ القامُوسِ ورابِطُ
    /// <c>me/role</c> خَلفَ <c>canSwitchToOffer</c>، بِلا استِمارَة.</summary>
    [Fact]
    public void The_offer_form_is_drawn_only_for_a_role_carrying_offer_submit_and_a_switch_hint_otherwise()
    {
        var page = Page();
        var form = page.IndexOf("id=\"offerForm\"", StringComparison.Ordinal);
        var conditions = UpgradePromptPriceTests.EnclosingIfConditions(page, form);
        var hint = page.IndexOf("@L.Markup(\"listings.detail.err_forbidden\")", StringComparison.Ordinal);
        var hintConditions = UpgradePromptPriceTests.EnclosingIfConditions(page, hint);
        var switchLink = page.IndexOf("listings.detail.offer_switch_role", StringComparison.Ordinal);
        output.WriteLine($"استِمارَةُ العَرض: {string.Join(" | ", conditions)} · السَطرُ: {string.Join(" | ", hintConditions)} · الرابِط: "
            + string.Join(" | ", UpgradePromptPriceTests.EnclosingIfConditions(page, switchLink)));

        Assert.Equal(new[] { "showOffers", "Auth.IsAuthenticated && !isOwner && canOffer" }, conditions);
        Assert.Equal(new[] { "showOffers", "Auth.IsAuthenticated && !isOwner" }, hintConditions);
        Assert.Equal(new[] { "showOffers", "Auth.IsAuthenticated && !isOwner", "canSwitchToOffer" }, UpgradePromptPriceTests.EnclosingIfConditions(page, switchLink));
        Assert.True(hint > form, "سَطرُ الرَفضِ لا يَلي الاستِمارَة.");
        Assert.StartsWith("else if (Auth.IsAuthenticated && !isOwner)",
            page[(page.LastIndexOf("</form>", hint, StringComparison.Ordinal) + "</form>".Length)..].TrimStart().TrimStart('}').TrimStart(), StringComparison.Ordinal);
        Assert.DoesNotContain("<form", page[hint..switchLink], StringComparison.Ordinal);

        Assert.Contains("var roles = (await Roles.LoadAsync(Slug)).Tenant?.Roles", page, StringComparison.Ordinal);
        Assert.Contains("var storedRole = await Accounts.ActiveRoleAsync(Slug, Auth.UserId.Value);", page, StringComparison.Ordinal);
        Assert.Contains("canOffer = ACommerce.Templates.Customer.Marketplace.Gates.PermissionDoor.Opens(roles, Role, storedRole, \"offer.submit\");", page, StringComparison.Ordinal);
        Assert.Contains("PermissionDoor.SwitchOpens(roles, Role, \"offer.submit\")", page, StringComparison.Ordinal);
        // ‏وحارِسُ النُقطَةِ يَقرَأُ الأَدوارَ المُجَسَّدَةَ ثُمَّ دَورَها الفَعّال — فَالبابُ يَقرَأُ ما يَقرَؤُه.
        var mte = WriteEndpointGuardTests.StripComments(Read(TemplateRoot + "/MarketplaceTemplateExtensions.cs"));
        var guardAt = mte.IndexOf("private static async Task<bool> HasPermissionAsync(", StringComparison.Ordinal);
        Assert.True(guardAt > 0, "HasPermissionAsync غَيرُ مَوجودَة — الأَداةُ عَمياء.");
        var guard = mte[guardAt..mte.IndexOf("tenant.Roles, effectiveRole, permission);", guardAt, StringComparison.Ordinal)];
        Assert.Contains("var tenant = await LoadTenantWithRolesAsync(store, slug);", guard, StringComparison.Ordinal);
        Assert.Contains("Gates.EffectiveRole.ResolveAsync(", guard, StringComparison.Ordinal);
    }

    /// <summary>دَورٌ مُؤَلَّفٌ مُعتَمَدٌ يَحمِلُ <c>offer.submit</c> — أَقَلُّ تَعريفٍ يَجتازُ المُصادِق (شَكلُ <c>AppearanceBaselineSeeder.AuthoredRoleJson</c>)
    /// مُجَسَّداً فَوقَ أَدوارٍ مُخَزَّنَةٍ كَما يُجَسِّدُه <c>TenantRoleService.LoadAsync</c> و<c>LoadTenantWithRolesAsync</c>.</summary>
    private static (IReadOnlyList<Role> Stored, IReadOnlyList<Role> Materialized) StoreWithAuthoredBroker()
    {
        static Role R(string slug, params string[] permissions) => new() { Slug = slug, CatalogSlug = slug, Permissions = permissions.ToList() };
        var stored = new List<Role> { R("customer", "listing.browse", "offer.submit"), R("vendor", "listing.create", "listing.edit") };
        const string json = """
            {
              "slug": "dallal",
              "icon": "🧾",
              "homeRoute": "",
              "label": { "ar": "دَلّال", "en": null },
              "description": { "ar": "دَورٌ مُؤَلَّفٌ — عَيِّنَةُ بِنيَة.", "en": null },
              "permissions": [ "listing.browse", "offer.submit" ],
              "fields": [],
              "composition": {
                "home": "defaultHome",
                "createListing": "defaultCreateForm",
                "nav": "defaultNav",
                "explore": "defaultExplore",
                "publicProfile": null,
                "extras": []
              },
              "dealPatternAffinity": null
            }
            """;
        var set = TenantRoleSet.FromDocuments("adwar-demo", new[]
        {
            new TenantRoleDefinition
            {
                Id = "dallal", Slug = "dallal", DefinitionJson = json, Status = TenantRoleStatuses.Approved,
                CreatedBy = "agent:define_role", CreatedAt = new DateTime(2026, 8, 10, 0, 0, 0, DateTimeKind.Utc),
            },
        });
        return (stored, set.Materialize(stored));
    }

    /// <summary><b>بابُ العَرضِ سُلوكاً لا بَصمَة</b> (‏2026-10-25، مُراجَعَةُ تَيّارِ W8-7 الجَولَة ١ — مَعلومَة): الصُفوفُ أَعلاه تُثَبِّتُ نَصَّ النِداء، وصُفوفُ
    /// <c>PermissionDoor</c> في <c>CreateListingDoorTests</c> تَقيسُ <c>listing.create</c> عَلى أَدوارٍ مُخَزَّنَةٍ وَحدَها. وهُنا <c>offer.submit</c> عَلى
    /// الأَدوارِ <b>المُجَسَّدَة</b> — ما يَقرَؤُه حارِسُ النُقطَةِ (<c>HasPermissionAsync</c>: <c>LoadTenantWithRolesAsync</c> ثُمَّ <c>RolePermissions.Has</c>):
    /// دَورٌ مُؤَلَّفٌ يَحمِلُها مَفتوح، والبائِعُ بِلاها مُغلَق، ودَورُ المَسارِ يَتَفَوَّق — وكُلُّ صَفٍّ يُساوي قَرارَ الحارِس. والمُخَزَّنَةُ وَحدَها تُغلِقُ عَلى
    /// المُؤَلَّف: سَبَبُ قِراءَةِ الصَفحَةِ المُجَسَّدَة.</summary>
    [Theory]
    [InlineData(null, "dallal", true)]     // ‏مُؤَلَّفٌ يَحمِلُ offer.submit — مَفتوح
    [InlineData(null, "vendor", false)]    // ‏البائِعُ بِلاها — مُغلَق
    [InlineData(null, "customer", true)]
    [InlineData("dallal", "vendor", true)] // ‏دَورُ المَسارِ يَتَفَوَّق
    [InlineData("vendor", "dallal", false)]
    [InlineData(null, null, false)]
    public void The_offer_door_opens_for_an_authored_role_carrying_offer_submit_and_stays_closed_for_a_vendor(string? urlRole, string? stored, bool opens)
    {
        var (storedRoles, materialized) = StoreWithAuthoredBroker();
        var door = ACommerce.Templates.Customer.Marketplace.Gates.PermissionDoor.Opens(materialized, urlRole, stored, "offer.submit");
        var guard = RolePermissions.Has(materialized, ACommerce.Templates.Customer.Marketplace.Gates.EffectiveRole.Resolve(null, false, urlRole, stored), "offer.submit");
        output.WriteLine($"مَسار={urlRole ?? "—"} مُخَزَّن={stored ?? "—"} · مُجَسَّدَة {materialized.Count} · البابُ {door} · الحارِسُ {guard}");

        Assert.Equal(storedRoles.Count + 1, materialized.Count);
        Assert.Equal(opens, door);
        Assert.Equal(guard, door);
        Assert.False(ACommerce.Templates.Customer.Marketplace.Gates.PermissionDoor.Opens(storedRoles, null, "dallal", "offer.submit"),
            "بِالمُخَزَّنَةِ وَحدَها يُغلَقُ عَلى المُؤَلَّف — فَالصَفحَةُ تَقرَأُ المُجَسَّدَةَ كَحارِسِ نُقطَتِها.");
    }

    /// <summary><b>قُلِبَ «Today_» بِاسمِه (‏2026-10-25، كوميتُ إصلاحِ <c>lens-dead-doors-7</c>)</b> — كانَ
    /// <c>Today_the_endpoints_forbidden_redirect_lands_on_a_page_that_reads_no_forbidden_code</c> (‏<c>4b74e0b</c>). والآن فَرعُ
    /// <c>Err == "forbidden"</c> في سِلسِلَةِ رُموزِ النُقطَةِ يَرسُمُ <c>listings.detail.err_forbidden</c> مِن القامُوس — والنُقطَةُ كاتِبُه الوَحيد.</summary>
    [Fact]
    public void The_endpoints_forbidden_redirect_draws_its_dictionary_text_on_the_page()
    {
        var head = OfferEndpointHead();
        var page = Page();
        var branch = page.IndexOf("else if (Err == \"forbidden\")", StringComparison.Ordinal);
        var text = ACommerce.Platform.I18n.LocaleCatalog.Find(ACommerce.Platform.I18n.LocaleCatalog.Arabic, "listings.detail.err_forbidden");
        output.WriteLine($"كُتّابُ ?err=forbidden في نُقطَةِ العَرض: {Regex.Matches(head, Regex.Escape("?err=forbidden")).Count} · "
            + $"فَرعُ الصَفحَة: {branch} · النَصّ: «{text}»");

        Assert.Single(Regex.Matches(head, Regex.Escape("listings/{id}?err=forbidden")));
        Assert.True(branch > 0, "لا فَرعَ forbidden في الصَفحَة.");
        Assert.Single(Regex.Matches(page, Regex.Escape("Err == \"forbidden\"")));
        var block = page[branch..page.IndexOf('}', branch)];
        Assert.Contains("<AcAlert Variant=\"danger\" Message=\"@L[\"listings.detail.err_forbidden\"]\" />", block, StringComparison.Ordinal);
        Assert.False(string.IsNullOrWhiteSpace(text));
        Assert.False(ACommerce.Platform.I18n.LocaleCatalog.IsPlaceholderKey(ACommerce.Platform.I18n.LocaleCatalog.Arabic, "listings.detail.err_forbidden"));
    }

    // ─── ما لا يَتَبَدَّل ───────────────────────────────────────────────────────────────────

    /// <summary>الكاتالوجُ كَما قاسَه التَدقيق: خَمسَةُ أَدوارٍ بِلا <c>offer.submit</c> وخَمسَةٌ بِه — وكُلُّ دَورٍ في الكاتالوجِ في إحدى القائِمَتَين.</summary>
    [Fact]
    public void Five_catalog_roles_lack_offer_submit_and_five_carry_it()
    {
        var lacking = RoleCatalog.Definitions.Where(d => !d.Permissions.Contains(Permission)).Select(d => d.Slug).OrderBy(s => s, StringComparer.Ordinal).ToArray();
        var carrying = RoleCatalog.Definitions.Where(d => d.Permissions.Contains(Permission)).Select(d => d.Slug).OrderBy(s => s, StringComparer.Ordinal).ToArray();
        output.WriteLine($"بِلا offer.submit: {string.Join(", ", lacking)} · بِه: {string.Join(", ", carrying)}");

        Assert.Equal(new[] { "broker", "host", "organizer", "rider", "vendor" }, lacking);
        Assert.Equal(new[] { "customer", "driver", "mover", "shipper", "tenant_admin" }, carrying);
    }

    /// <summary>النُقطَةُ تَسأَلُ الصَلاحِيَّةَ بَعدَ الجَلسَةِ وقَبلَ أَوَّلِ حَقل (القاعِدَة ٦: التَخويلُ يَسبِقُ تَحَقُّقَ الحُقول) — وهذا لا يَتَبَدَّلُ بِالإصلاح.</summary>
    [Fact]
    public void The_offer_endpoint_asks_offer_submit_after_the_session_and_before_any_field()
    {
        var head = OfferEndpointHead();
        var session = head.IndexOf("AuthHandlers.ParseToken(token)", StringComparison.Ordinal);
        var gate = head.IndexOf("HasPermissionAsync(req.HttpContext, slug, userId, \"offer.submit\", store)", StringComparison.Ordinal);
        var field = head.IndexOf("req.Form[", StringComparison.Ordinal);
        Assert.True(session > 0 && gate > session && field > gate, $"الجَلسَة {session} · الحارِس {gate} · أَوَّلُ حَقل {field}");
    }
}
