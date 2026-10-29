using System.Text.RegularExpressions;
using ACommerce.Platform.I18n;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ اسمُ البائِعِ الَّذي يَراه المُشتَري «عُضو جَديد» — واسمُ نَشاطِه الإلزامِيُّ لا يُقرَأُ في أَيِّ مَوضِع ═══
//
// قَرارُ المالِك د40 «ج» (‏2026-10-29): «يُطلب الاسم عند أوّل دخول (وحقل تعديل الاسم موجود اليوم في «حسابي»)، ويُقدَّم اسم النشاط حيث وُجد في الإشعار
// وصفحة الصفقة وصفحة البائع.» **تَوصيفُ «اليَوم» قَبلَ التَبديل** (القاعِدَة ٣) — ما قاسَه `RELEASE-READINESS.md` §٧·١ البَند ٥: الدُخولُ بِالهاتِفِ أَو
// البَريدِ يُنشِئُ الحِسابَ بِـ«عُضو جَديد» ويُحَوِّلُ إلى مَسارِ ما بَعدَ الدُخولِ بِلا خُطوَةِ اسم؛ و`business_name` (إلزامِيٌّ في `vendor.role.json`) يُحفَظُ
// في `RoleAttributesJson` ولا يَقرَؤُه كودٌ واحِد؛ واسمُ الفاعِلِ في السِجِلّاتِ (`AccountQueries.ActorNameAsync`) واسمُ البائِعِ المَربوطِ عِندَ الوِلادَة
// (`DealsService.BindListingOwnerAsync`) وعُنوانُ صَفحَةِ البائِعِ (`VendorProfile.razor`) كُلُّها `FullName`. صُفوفُ «Today_» تُقلَبُ بِأَسمائِها في كوميتِ التَبديل.
public class SellerDisplayNameTests(ITestOutputHelper output)
{
    private const string Template = "ACommerce.Templates.Customer.Marketplace";

    internal static string Code(params string[] parts) => UpgradePromptPriceTests.Code(new[] { "libs", "templates", Template }.Concat(parts).ToArray());

    internal static IReadOnlyList<string> BusinessNameReaders()
    {
        var hits = new List<string>();
        foreach (var root in new[] { "libs", "apps" })
            foreach (var f in Directory.EnumerateFiles(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, root), "*.*", SearchOption.AllDirectories))
            {
                var sep = Path.DirectorySeparatorChar;
                if (f.Contains($"{sep}bin{sep}") || f.Contains($"{sep}obj{sep}") || !(f.EndsWith(".cs") || f.EndsWith(".razor"))) continue;
                var code = WriteEndpointGuardTests.StripComments(WriteEndpointGuardTests.StripMarkupComments(File.ReadAllText(f)));
                if (code.Contains("business_name", StringComparison.Ordinal)) hits.Add(Path.GetFileName(f));
            }
        return hits;
    }

    /// <summary><b>قُلِبَ «Today_» بِاسمِه (‏2026-10-29، كوميتُ التَبديل)</b> — كانَ <c>Today_the_business_name_is_read_nowhere_and_every_seller_surface_reads_the_person_name</c>
    /// (‏<c>5cdc461</c>): «قُرّاءُ business_name: 0 · مَواضِعُ الاسمِ الشَخصِيِّ في صَفحَةِ البائِع: 4». والآن قارِئٌ واحِدٌ (<c>ActorName.cs</c>) تَسأَلُه
    /// السُطوحُ الثَلاث — كاتِبُ اسمِ الفاعِل، ورَبطُ البائِعِ عِندَ الوِلادَة، وصَفحَةُ البائِع — بِلا نُسخَةٍ ثانِيَة.</summary>
    [Fact]
    public void One_rule_reads_the_business_name_and_every_seller_surface_asks_it()
    {
        var readers = BusinessNameReaders();
        var accounts = Code("Services", "Queries", "AccountQueries.cs");
        var deals = Code("Services", "Deals", "DealsService.cs");
        var vendor = Code("Components", "Pages", "VendorProfile.razor");
        var vendorNames = Regex.Matches(vendor, @"vendor\.FullName").Count;
        output.WriteLine($"قُرّاءُ business_name: {readers.Count} ({string.Join(", ", readers)}) · مَواضِعُ الاسمِ الشَخصِيِّ في صَفحَةِ البائِع: {vendorNames}");
        Assert.Equal(new[] { "ActorName.cs" }, readers);
        Assert.Contains("=> ActorName.Of(await ProfileAsync(tenantSlug, userId, ct));", accounts, StringComparison.Ordinal);
        Assert.Contains("ownerId, Queries.ActorName.Of(owner), ct, DealEvent.BoundAtBirth);", deals, StringComparison.Ordinal);
        Assert.Contains("shownName = Services.Queries.ActorName.Of(vendor) ?? \"\";", vendor, StringComparison.Ordinal);
        Assert.Equal(0, vendorNames);
    }

    /// <summary><b>قُلِبَ «Today_» بِاسمِه</b> — كانَ <c>Today_the_first_phone_or_email_login_goes_straight_to_the_post_login_route</c>: «تَحويلٌ مُباشِرٌ بَعدَ
    /// الدُخول: 3 · صَفحَةُ خُطوَةِ الاسم: 0». والآن الهاتِفُ والبَريدُ يَسأَلانِ <c>NeedsName</c> ويُحَوِّلانِ عَبرَ <c>MemberNameStep</c>، ونَفاذٌ كَما كان (اسمُه
    /// مُشتَقٌّ مِن هُوِيَّتِه)، وصَفحَةُ الخُطوَةِ تُرسِلُ إلى نُقطَةِ «تَعديل المَلَف» نَفسِها.</summary>
    [Fact]
    public void A_first_phone_or_email_login_asks_for_the_name_before_the_post_login_route()
    {
        var ext = Code("MarketplaceTemplateExtensions.cs");
        var straight = Regex.Matches(ext, Regex.Escape("return Results.Redirect(await PostLoginRouteAsync(slug, result.UserId, asRole, store));")).Count;
        var stepped = Regex.Matches(ext, Regex.Escape("return Results.Redirect(Services.Queries.MemberNameStep.Route(needsName, await PostLoginRouteAsync(slug, result.UserId, asRole, store), p => Link(req, slug, p)));")).Count;
        var asks = Regex.Matches(ext, Regex.Escape("needsName = user is not null && Services.Queries.ActorName.NeedsName(user);")).Count;
        var page = Code("Components", "Pages", "MemberName.razor");
        output.WriteLine($"تَحويلٌ مُباشِرٌ بَعدَ الدُخول: {straight} · عَبرَ خُطوَةِ الاسم: {stepped} · سُؤالُ NeedsName: {asks}");
        Assert.Equal(1, straight);   // نَفاذ
        Assert.Equal(2, stepped);    // الهاتِفُ والبَريد
        Assert.Equal(2, asks);
        Assert.Contains("@page \"/{slug}/me/name\"", page, StringComparison.Ordinal);
        // ‏(2026-10-29، مُراجَعَةُ S2 الجَولَة ١) والدَورُ يَبلُغُ الحارِسَ وسُقوطَ «لاحِقاً» — كانا `<GatedPage Slug="@Slug" RequireAuth>` و`$"/{Slug}/me"`.
        Assert.Contains("<GatedPage Slug=\"@Slug\" Role=\"@Role\" RequireAuth>", page, StringComparison.Ordinal);
        Assert.Contains("action=\"@($\"/{Slug}/me/save\")\"", page, StringComparison.Ordinal);
        Assert.Contains("name=\"fullName\"", page, StringComparison.Ordinal);
        Assert.Contains("Services.LocalRedirect.Resolve(Next, AuthSession.LinkFor(Slug, Role, \"me\"))", page, StringComparison.Ordinal);
        foreach (var key in new[] { "me.name.title", "me.name.lead", "me.name.label", "me.name.submit", "me.name.later" })
            Assert.False(string.IsNullOrWhiteSpace(LocaleCatalog.Find(LocaleCatalog.Arabic, key)), $"«{key}» بِلا قيمَة.");
    }

    /// <summary>
    /// <para><b>قُلِبَ «Today_» بِاسمِه (‏2026-10-29، مُراجَعَةُ تَيّارِ S2 الجَولَة ١ — كوميتُ التَبديل)</b> — كانَ
    /// <c>Today_a_first_login_on_a_role_page_is_sent_to_a_name_step_no_page_declares</c> (‏<c>6a9639f</c>): «قَوالِبُ GET المُعلَنَة: 148 ·
    /// /zz-name/me/name?… ← True · /zz-name/r/vendor/me/name?… ← False» — أَوَّلُ دُخولٍ مِن صَفحَةِ دُخولِ دَورٍ يَنتَهي بِـ‏404 (مَقيسٌ حَيّاً عَلى
    /// <c>wasayel_s2r1_pre_20261029</c>: «فُحِصَ 72 · فَشِلَ 1»). والآن كُلُّ مَسارٍ تُنتِجُه <c>MemberNameStep.Route</c> — بِلا دَورٍ وبِكُلِّ دَورٍ
    /// مِن العَيِّنَة، بِـ<c>AuthSession.LinkFor</c> الَّتي يُنادِيها <c>Link(req, slug, p)</c> — يَحُلُّ قالِباً مُعلَناً، والصَفحَةُ تُعلِنُ المَسارَين.</para>
    /// </summary>
    [Fact]
    public void Every_route_the_name_step_can_produce_resolves_to_a_declared_page()
    {
        var gets = RouteResolutionTests.Declared("GET");
        var produced = new List<string>();
        foreach (var role in new string?[] { null, "vendor", "customer", "host" })
        {
            var next = ACommerce.Templates.Customer.Marketplace.AuthSession.LinkFor("zz-name", role, "me/role/onboarding");
            produced.Add(ACommerce.Templates.Customer.Marketplace.Services.Queries.MemberNameStep.Route(true, next,
                p => ACommerce.Templates.Customer.Marketplace.AuthSession.LinkFor("zz-name", role, p)));
        }
        var dead = produced.Where(r => !RouteResolutionTests.Resolves(r, gets)).ToList();
        output.WriteLine($"قَوالِبُ GET المُعلَنَة: {gets.Count} · مَساراتُ الخُطوَةِ المُنتَجَة: {produced.Count} · لا تَحُلُّ قالِباً: {dead.Count}");
        Assert.True(gets.Count > 0, "صِفرُ قالِبِ GET — الأَداةُ عَمياء (القاعِدَة ١٠).");
        Assert.Equal(4, produced.Count);
        Assert.Contains("/zz-name/r/vendor/me/name?next=%2Fzz-name%2Fr%2Fvendor%2Fme%2Frole%2Fonboarding", produced);
        Assert.Empty(dead);
        // والسالِب: الأَداةُ تَرى المَسارَ المَيِّتَ — قالِبٌ لا تُعلِنُه صَفحَةٌ لا يَحُلّ.
        Assert.False(RouteResolutionTests.Resolves("/zz-name/r/vendor/me/no-such-step?next=%2Fzz-name%2Fme", gets));
        var page = Code("Components", "Pages", "MemberName.razor");
        Assert.Contains("@page \"/{slug}/me/name\"", page, StringComparison.Ordinal);
        Assert.Contains("@page \"/{slug}/r/{role}/me/name\"", page, StringComparison.Ordinal);
    }

    // ─── القاعِدَة: مُوجَبٌ وسالِب ──────────────────────────────────────────────────────────────

    private static ACommerce.Kit.Auth.User Member(string name, string? business = null, string role = "vendor")
    {
        var u = new ACommerce.Kit.Auth.User { Id = Guid.NewGuid(), TenantSlug = "zz-name", FullName = name };
        if (business is not null) u.RoleAttributesJson[role] = new() { ["business_name"] = business };
        return u;
    }

    [Fact]
    public void The_business_name_comes_first_where_it_exists_and_the_person_name_otherwise()
    {
        Assert.Equal("مَتجَرُ الوَرد", ACommerce.Templates.Customer.Marketplace.Services.Queries.ActorName.Of(Member("سارَة", "  مَتجَرُ الوَرد ")));
        Assert.Equal("سارَة", ACommerce.Templates.Customer.Marketplace.Services.Queries.ActorName.Of(Member(" سارَة ")));
        Assert.Equal("سارَة", ACommerce.Templates.Customer.Marketplace.Services.Queries.ActorName.Of(Member("سارَة", "   ")));
        Assert.Equal("نَشاطٌ في دَورٍ آخَر", ACommerce.Templates.Customer.Marketplace.Services.Queries.ActorName.Of(Member("سارَة", "نَشاطٌ في دَورٍ آخَر", role: "provider")));
        Assert.Null(ACommerce.Templates.Customer.Marketplace.Services.Queries.ActorName.Of((ACommerce.Kit.Auth.User?)null));
        Assert.Null(ACommerce.Templates.Customer.Marketplace.Services.Queries.ActorName.Of(Member("  ")));
        Assert.Equal(new[] { "سارَة", "مَتجَرُ الوَرد" }, ACommerce.Templates.Customer.Marketplace.Services.Queries.ActorName.NamesOf(Member("سارَة", "مَتجَرُ الوَرد")));
        Assert.Equal(new[] { "سارَة" }, ACommerce.Templates.Customer.Marketplace.Services.Queries.ActorName.NamesOf(Member("سارَة", "سارَة")));
    }

    [Fact]
    public void The_name_step_is_asked_only_while_the_name_is_the_new_member_default_or_blank()
    {
        var placeholder = LocaleCatalog.Find(LocaleCatalog.Arabic, ACommerce.Kit.Auth.Server.AuthHandlers.NewMemberNameKey)!;
        Assert.True(ACommerce.Templates.Customer.Marketplace.Services.Queries.ActorName.NeedsName(Member(placeholder)));
        Assert.True(ACommerce.Templates.Customer.Marketplace.Services.Queries.ActorName.NeedsName(Member(" ")));
        Assert.False(ACommerce.Templates.Customer.Marketplace.Services.Queries.ActorName.NeedsName(Member("سارَة")));
        Assert.False(ACommerce.Templates.Customer.Marketplace.Services.Queries.ActorName.NeedsName(Member(ACommerce.Kit.Auth.NafathNames.For("1234567890"))));

        string Link(string p) => $"/zz-name/{p}";
        Assert.Equal("/zz-name/me/name?next=%2Fzz-name%2Fme%2Frole", ACommerce.Templates.Customer.Marketplace.Services.Queries.MemberNameStep.Route(true, "/zz-name/me/role", Link));
        Assert.Equal("/zz-name/me/role", ACommerce.Templates.Customer.Marketplace.Services.Queries.MemberNameStep.Route(false, "/zz-name/me/role", Link));
    }

    /// <summary>والحَذفُ يَمحو اسمَ النَشاطِ أَيضاً — إشعارُ القَبولِ الَّذي يُسَمّي البائِعَ بِنَشاطِه في صُندوقِ المُشتَري.</summary>
    [Fact]
    public void Deletion_erases_the_business_name_from_the_buyers_acceptance_notice()
    {
        var seller = Guid.NewGuid();
        var deal = Guid.NewGuid();
        var n = new ACommerce.Kit.Notifications.Notification
        {
            Id = Guid.NewGuid(), UserId = Guid.NewGuid(), Type = "deal_accepted", RelatedUrl = $"/zz-name/deals/{deal}",
            Title = "قُبِلَ طَلَبُك", Body = "مَتجَرُ الوَرد قَبِلَ طَلَبَكَ عَلى «زَهر»",
        };
        Assert.True(ACommerce.Templates.Customer.Marketplace.Services.Identity.AccountErasure.EraseFrom(n, seller, new[] { "سارَة", "مَتجَرُ الوَرد" }, new[] { $"/deals/{deal}" }, Array.Empty<string>(), new HashSet<Guid>()));
        Assert.DoesNotContain("مَتجَرُ الوَرد", n.Body, StringComparison.Ordinal);
        Assert.Contains(ACommerce.Kit.Auth.AccountDeletion.ErasedName, n.Body, StringComparison.Ordinal);
        var other = new ACommerce.Kit.Notifications.Notification { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), Type = "deal_accepted", RelatedUrl = "/zz-name/deals/x", Title = "t", Body = "مَتجَرُ الوَرد" };
        Assert.False(ACommerce.Templates.Customer.Marketplace.Services.Identity.AccountErasure.EraseFrom(other, seller, new[] { "مَتجَرُ الوَرد" }, new[] { $"/deals/{deal}" }, Array.Empty<string>(), new HashSet<Guid>()));
    }
}
