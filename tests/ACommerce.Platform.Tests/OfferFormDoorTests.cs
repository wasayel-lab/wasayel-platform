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

    /// <summary><b>«اليَوم»</b>: شُروطُ استِمارَةِ العَرضِ <c>showOffers</c> ثُمَّ <c>Auth.IsAuthenticated &amp;&amp; !isOwner</c> — لا شِقَّ فيها يَسأَلُ
    /// <c>offer.submit</c>، والصَفحَةُ لا تَذكُرُ الصَلاحِيَّةَ أَصلاً.</summary>
    [Fact]
    public void Today_the_offer_form_is_drawn_for_every_signed_in_non_owner_whatever_the_role()
    {
        var page = Page();
        var form = page.IndexOf("id=\"offerForm\"", StringComparison.Ordinal);
        var conditions = UpgradePromptPriceTests.EnclosingIfConditions(page, form);
        output.WriteLine($"استِمارَةُ العَرضِ عِندَ {form} · شُروطُها: {string.Join(" | ", conditions)} · ذِكرُ offer.submit في الصَفحَة: "
            + Regex.Matches(page, Regex.Escape("\"offer.submit\"")).Count);

        Assert.True(form > 0, "استِمارَةُ العَرضِ غَيرُ مَوجودَة — الأَداةُ عَمياء.");
        Assert.Equal(new[] { "showOffers", "Auth.IsAuthenticated && !isOwner" }, conditions);
        Assert.DoesNotContain("\"offer.submit\"", page, StringComparison.Ordinal);
    }

    /// <summary><b>«اليَوم»</b>: النُقطَةُ تَكتُبُ <c>listings/{id}?err=forbidden</c> والصَفحَةُ لا تَذكُرُ <c>"forbidden"</c> — رَدٌّ لا يُقرَأ.</summary>
    [Fact]
    public void Today_the_endpoints_forbidden_redirect_lands_on_a_page_that_reads_no_forbidden_code()
    {
        var head = OfferEndpointHead();
        var page = Page();
        output.WriteLine($"كُتّابُ ?err=forbidden في نُقطَةِ العَرض: {Regex.Matches(head, Regex.Escape("?err=forbidden")).Count} · "
            + $"ذِكرُ \"forbidden\" في الصَفحَة: {Regex.Matches(page, Regex.Escape("\"forbidden\"")).Count}");

        Assert.Single(Regex.Matches(head, Regex.Escape("listings/{id}?err=forbidden")));
        Assert.DoesNotContain("\"forbidden\"", page, StringComparison.Ordinal);
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
