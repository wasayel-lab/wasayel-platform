using System.Text.RegularExpressions;
using ACommerce.Kit.Roles;
using Xunit;

namespace ACommerce.Platform.Tests;

// ═══ تَوصيفُ ما يَراه الزائِرُ عَلى جَذرِ المَتجَرِ `/{slug}` — قَبلَ «البِضاعَةُ أَوَّلاً» (قَرارُ المالِك د34) ═══
//
// **المَقيس**: اللَقَطاتُ الحَيَّةُ المودَعَةُ في `tests/characterization/appearance/i18n-baseline/` — التِقاطُ
// `scripts/capture-appearance.sh` بِـ`curl` عَلى قاعِدَةٍ مَحَلِّيَّة (‏ADR-035 §٢‑٥) — لا نَصُّ المَصدَر: ما يُصَيَّرُ فِعلاً
// لِلزائِرِ غَيرِ المُسَجَّل. و`ashare` مَتجَرٌ تَجريبِيٌّ كَغَيرِه (قَرارُ المالِك ‏2026-10-28)، أَدوارُه `customer` و`host` و`vendor`
// (`TestDataSeeder.cs:29`) — فَفيهِ دَورُ مُشتَرٍ، وهُوَ المَقيسُ هُنا لا المُستَثنى.
//
// **ما يُقلَبُ في كوميتِ التَبديلِ ويُقالُ في جِسمِه**: الصَفُّ الأَوَّلُ وَحدَه (`Today_…`) — البَوّابَةُ وَحدَها بِلا بِضاعَةٍ ولا شَريطٍ
// عَلى جَذرِ مَتجَرٍ فيه دَورُ مُشتَرٍ؛ فَهُوَ ما قَرَّرَ المالِكُ تَبديلَه. **وما يَبقى بِحَرفِه**: الصُفوفُ بَعدَه — مَن يَفتَحُ تَطبيقَ دَورٍ
// مُباشَرَةً (`/{slug}/r/{role}`) يَرى البَوّابَةَ بِشَريطِها، ومَتجَرٌ بِلا أَدوارٍ يَرى رَئيسِيَّةَ المُشتَري بِشَريطِها، والكاتالوجُ فيه
// دَورُ عُضوٍ واحِدٌ رَئيسِيَّتُه رَئيسِيَّةُ المُشتَري ويَتَصَفَّح: `customer`.

public class StoreRootVisitorCharacterizationTests
{
    private static string RepoRoot => ThemeZeroEquivalenceTests.RepoRoot;

    internal static string Snapshot(string page)
    {
        var path = Path.Combine(RepoRoot, "tests", "characterization", "appearance", "i18n-baseline", page + ".html");
        Assert.True(File.Exists(path), $"لَقطَةٌ مَفقودَة: {path} — الأَداةُ عَمياءُ بِلا طَرَفٍ مَقروء.");
        var html = File.ReadAllText(path);
        Assert.True(html.Length > 20_000, $"أَداةٌ عَمياء: {page} طولُها {html.Length} مِحرَفاً — لَم تُقرَأ.");
        return html;
    }

    internal static int Count(string haystack, string needle)
        => Regex.Matches(haystack, Regex.Escape(needle)).Count;

    /// <summary>عَلاماتُ ما يُرسَم — كُلٌّ صَنفٌ يُصَيِّرُه مُكَوِّنٌ واحِد: الشَريطُ (`MainLayout`)، والهيرو والإجراءاتُ (فَرعُ
    /// `TenantHome` الافتِراضِيّ)، والبَوّابَةُ (`RoleLanding`).</summary>
    internal const string MobileNav = "class=\"acm-mobile-nav\"";
    internal const string TopNav    = "class=\"ac-topnav ";
    internal const string Hero      = "class=\"ac-hero ";
    internal const string Actions   = "class=\"acm-home-actions\"";
    internal const string Gateway   = "<div class=\"acs-page acm-role-landing\">";
    internal const string Card      = "class=\"acm-role-landing-card\"";

    // ─── (١) ما يُقلَب: جَذرُ مَتجَرٍ فيه دَورُ مُشتَرٍ ─────────────────────────────

    /// <summary>
    /// <para><b>اليَوم</b>: زائِرُ <c>/ashare</c> يَرى البَوّابَةَ وَحدَها — بِطاقاتُ الأَدوارِ الثَلاث إلى <c>r/{role}/login</c> وسَطرُ «لَدَيكَ حِساب»
    /// إلى <c>/ashare/login</c> — <b>ولا هيرو ولا إجراءاتِ رَئيسِيَّة ولا شَريط</b> (الشَريطُ مَخفِيٌّ عَلى الجَذرِ العاري لِمَتجَرٍ ذي أَدوار).</para>
    /// </summary>
    [Fact]
    public void Today_the_visitor_of_a_store_with_a_buyer_role_sees_the_role_gateway_and_no_goods_on_the_store_root()
    {
        var html = Snapshot("ashare-portal");

        Assert.Equal(1, Count(html, Gateway));
        Assert.Equal(3, Count(html, Card));
        foreach (var role in new[] { "customer", "host", "vendor" })
            Assert.Equal(1, Count(html, $"href=\"/ashare/r/{role}/login\""));
        Assert.Equal(1, Count(html, "<div class=\"acm-role-landing-foot\"><a href=\"/ashare/login\">"));

        Assert.Equal(0, Count(html, Hero));
        Assert.Equal(0, Count(html, Actions));
        Assert.Equal(0, Count(html, MobileNav));
        Assert.Equal(0, Count(html, TopNav));
    }

    // ─── (٢) ما يَبقى بِحَرفِه ───────────────────────────────────────────────

    /// <summary>
    /// <para><b>مَن يَفتَحُ تَطبيقَ دَورٍ مُباشَرَةً</b> (<c>/ashare/r/{role}</c>) بِلا جَلسَةٍ يَرى البَوّابَةَ — والشَريطُ ظاهِرٌ لِأَنّ المَسارَ لَيسَ
    /// الجَذرَ العاري. والسُؤالُ «ماذا يَرى الزائِرُ عَلى صَفحَةِ المَتجَر» لا يَمَسُّه: الدَورُ مُختارٌ في الرابِطِ نَفسِه.</para>
    /// </summary>
    [Fact]
    public void A_visitor_who_opens_a_role_app_directly_sees_the_role_gateway_with_the_chrome()
    {
        foreach (var role in new[] { "customer", "host", "vendor" })
        {
            var html = Snapshot($"ashare-role-{role}");

            Assert.Equal(1, Count(html, Gateway));
            Assert.Equal(3, Count(html, Card));
            Assert.Equal(1, Count(html, $"<div class=\"acm-role-landing-foot\"><a href=\"/ashare/r/{role}/login\">"));
            Assert.Equal(0, Count(html, Hero));
            Assert.Equal(0, Count(html, Actions));
            Assert.Equal(1, Count(html, MobileNav));
            Assert.Equal(1, Count(html, TopNav));
        }
    }

    /// <summary>
    /// <para><b>ومَتجَرٌ بِلا أَدوار</b> (<c>theme-demo</c>) يَرى زائِرُه رَئيسِيَّةَ المُشتَري: الهيرو والإجراءات، بِالشَريطِ — ولا بَوّابَة.
    /// <b>وهذِه بِعَينِها «رَئيسِيَّةُ المُشتَري»</b> الَّتي يَعنيها القَرار: الفَرعُ الافتِراضِيُّ في <c>TenantHome</c>.</para>
    /// </summary>
    [Fact]
    public void A_store_without_roles_shows_its_visitor_the_buyer_home_with_the_chrome()
    {
        var html = Snapshot("theme-demo-portal");

        Assert.Equal(1, Count(html, Hero));
        Assert.Equal(1, Count(html, Actions));
        Assert.Equal(1, Count(html, MobileNav));
        Assert.Equal(1, Count(html, TopNav));
        Assert.Equal(0, Count(html, Gateway));
        Assert.Equal(0, Count(html, Card));
    }

    /// <summary>
    /// <para><b>دَورُ المُشتَري في الكاتالوج</b>: مِن أَدوارِ الأَعضاءِ (كُلُّ ما سِوى <c>tenant_admin</c>، وهُوَ ما تَرسُمُه البَوّابَةُ بِطاقات)
    /// دَورٌ واحِدٌ تَركيبُ رَئيسِيَّتِه <c>defaultHome</c> — رَئيسِيَّةُ المُشتَري — ويَحمِلُ <c>listing.browse</c>: <c>customer</c>. والباقونَ
    /// رَئيسِيّاتُهُم لِلبائِعِ أَو السائِقِ أَو الراكِب. فَمَتجَرٌ أَدوارُه <c>rider</c> و<c>driver</c> (‏<c>injez</c>، ‏<c>TestDataSeeder.cs:32</c>) بِلا دَورِ مُشتَرٍ.</para>
    /// </summary>
    [Fact]
    public void The_catalog_has_one_member_role_whose_home_is_the_buyer_home_and_it_browses_customer()
    {
        Assert.True(RoleCatalog.Definitions.Count >= 10, $"أَداةٌ عَمياء: {RoleCatalog.Definitions.Count} تَعريفاً فَقَط.");

        var buyerHomes = RoleCatalog.Definitions
            .Where(d => d.Slug != "tenant_admin")
            .Where(d => d.Composition.Home == RoleComponents.DefaultHome && d.Permissions.Contains("listing.browse"))
            .Select(d => d.Slug)
            .ToArray();
        Assert.Equal(new[] { "customer" }, buyerHomes);

        foreach (var slug in new[] { "host", "vendor", "rider", "driver" })
            Assert.NotEqual(RoleComponents.DefaultHome, TenantRoleSet.Platform.ResolveComposition(slug).Home);
        Assert.Equal(RoleComponents.DefaultHome, TenantRoleSet.Platform.ResolveComposition("customer").Home);
    }
}
