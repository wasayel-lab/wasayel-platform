using System.Text.RegularExpressions;
using ACommerce.Templates.Customer.Marketplace.Services.Incubator;
using Xunit;

namespace ACommerce.Platform.Tests;

// ═══ تَوصيفُ بابِ الزائِرِ عَلى `/{slug}` — قَبلَ رابِطِ الاستِكشاف (P1-2) ═══════════
//
// **المَقيسُ** (موجَزُ الفَجَوات P1-2؛ ‏ADR-032 §٢‑أ «مَوقِعُ وَيبٍ … عَلى طَريقَةِ
// سَلَّة»؛ ‏`RELEASE-READINESS.md:58-61` قاسَهُ ولَم يُقَرِّرْه): الزائِرُ غَيرُ المُسَجَّلِ
// لِمَتجَرٍ ذي أَدوار — وكُلُّ مَتجَرٍ مِن الاستوديو ذو أَدوار — يَرى عَلى `/{slug}`
// `RoleLanding` وَحدَها: بِطاقاتُ أَدوارٍ تَفتَحُ `r/{role}/login`، ورابِطُ `login`،
// **ولا رابِطَ مُحتَوىً واحِد**؛ والشَريطُ مَخفِيٌّ عَلى ذلكَ المَسارِ بِعَينِه، فَلا
// تَبويبَ `explore` يُنقَر. **والقُدرَةُ قائِمَة**: `TenantExplore` بِلا بَوّابَةِ دُخول،
// والشَريطُ يَظهَرُ عَلى `/{slug}/explore` — فَالفَجوَةُ في البابِ لا في الصَفحَة.
// (‏2026-10-24، تَيّارُ S14 البَند J — ولا يُعادُ حَرفٌ فَوقَه: `RELEASE-READINESS.md:58-61` أَعلاه أَسطُرُ نُسخَتِه قَبلَ ‏2026-10-13 —
// `git show 8508795:docs/RELEASE-READINESS.md` ‏:58-61؛ والنَصُّ نَفسُه اليَومَ في قِسمِه المَحفوظِ «ما قيس قبل التحوّل — ‏2026-08-17»، الرِحلَةُ ١ «زائِرٌ يَتَصَفَّح».)
//
// **ما يُقلَبُ في كوميتِ الرابِطِ ويُقالُ في جِسمِه**: الفَحصُ الأَوَّلُ وَحدَه
// (`Today_…`، وقُلِبَ ‏2026-10-11 إلى `The_role_landing_carries_one_link_…`) — فَالسُلوكُ
// المُثَبَّتُ فيه كانَ الفَجوَةَ نَفسَها. **وما يَبقى بِحَرفِه**:
// الفُحوصُ الأَربَعَةُ بَعدَه — فَرعُ `TenantHome` الَّذي يُرسِلُ الزائِرَ إلى البَوّابَة،
// وإخفاءُ الشَريطِ عَلى `/{slug}` وَحدَه، وغِيابُ بَوّابَةِ الدُخولِ عَن الاستِكشاف،
// وأَنّ مَتجَرَ الاستوديو يولَدُ بِأَدوار. ولا يَمَسُّ الكوميتُ ذاكَ الفَرعَ ولا الإخفاء —
// رَسمُ مُحتَوىً عَلى `/{slug}` أَو إظهارُ الشَريطِ هُناكَ خِيارا المالِكِ الأَكبَران.

public class RoleLandingVisitorDoorCharacterizationTests
{
    private static string RepoRoot => ThemeZeroEquivalenceTests.RepoRoot;

    private static string Template(params string[] parts)
    {
        var path = Path.Combine(new[] { RepoRoot, "libs", "templates", "ACommerce.Templates.Customer.Marketplace" }
            .Concat(parts).ToArray());
        Assert.True(File.Exists(path), $"مَصدَرٌ مَفقود: {path} — الأَداةُ عَمياءُ بِلا طَرَفٍ مَقروء.");
        var text = File.ReadAllText(path);
        Assert.True(text.Length > 500, $"أَداةٌ عَمياء: {path} طولُه {text.Length} مِحرَفاً — لَم يُقرَأ.");
        return text;
    }

    /// <summary>المَصدَرُ بِمَسافاتٍ مَطوِيَّة — فَالفَحصُ عَلى الكودِ لا عَلى التَنسيق.</summary>
    private static string Flat(string text) => Regex.Replace(text, @"\s+", " ");

    private static int Count(string haystack, string needle)
        => Regex.Matches(haystack, Regex.Escape(needle)).Count;

    internal static string RoleLanding() => Template("Components", "RoleLanding.razor");

    // ─── (١) الفَجوَة: البَوّابَةُ بِلا رابِطِ مُحتَوى — قُلِبَ في كوميتِ الرابِط ───

    /// <summary>
    /// <para><b>قُلِبَ ‏2026-10-11 (‏ADR-035)</b> — وكانَ «اليَوم»: رابِطانِ لا ثالِثَ
    /// لَهُما ولا كَلِمَةَ <c>explore</c> في المِلَفّ. <b>والآن ثَلاثَة</b>: بِطاقَةُ كُلِّ
    /// دَورٍ إلى <c>r/{role}/login</c>، و<b>رابِطٌ واحِدٌ إلى <c>explore</c> مِن جَذرِ
    /// المُستَأجِر</b> بِمِفتاحِ قامُوس، ثُمَّ سَطرُ «لَدَيكَ حِساب» إلى <c>login</c> تَحتَه.</para>
    /// </summary>
    [Fact]
    public void The_role_landing_carries_one_link_to_explore_above_the_have_account_line()
    {
        var razor = RoleLanding();

        const string browse = "<a href=\"@AuthSession.LinkFor(Slug, null, \"explore\")\">@L.Markup(\"roles.landing.browse\")</a>";
        Assert.Equal(1, Count(razor, browse));
        Assert.Equal(1, Count(razor, "\"explore\""));

        Assert.Equal(3, Count(razor, "AuthSession.LinkFor("));
        Assert.Contains("AuthSession.LinkFor(Slug, null, $\"r/{r.Slug}/login\")", razor, StringComparison.Ordinal);
        Assert.Contains("AuthSession.LinkFor(Slug, Role, $\"login\")", razor, StringComparison.Ordinal);
        Assert.Equal(3, Count(razor, "<a "));

        // فَوقَ سَطرِ «لَدَيكَ حِساب» وبَعدَ بِطاقاتِ الأَدوار — لا قَبلَها ولا بَدَلَها.
        var cards = razor.IndexOf("acm-role-landing-cards", StringComparison.Ordinal);
        var link  = razor.IndexOf(browse, StringComparison.Ordinal);
        var login = razor.IndexOf("@L.Markup(\"roles.landing.have_account\")", StringComparison.Ordinal);
        Assert.True(cards >= 0 && link > cards && login > link,
            $"تَرتيبٌ غَيرُ المَقصود: البِطاقات {cards}، الاستِكشاف {link}، الدُخول {login}.");
    }

    // ─── (٢) ما يَبقى بِحَرفِه بَعدَ الرابِط ─────────────────────────────────

    /// <summary>
    /// <para><b>الزائِرُ غَيرُ المُسَجَّلِ لِمَتجَرٍ ذي أَدوارٍ يَرى البَوّابَةَ عَلى
    /// <c>/{slug}</c></b> — فَرعُ <c>TenantHome</c> بِحَرفِه. ولا يُمَسُّ: رَسمُ رَئيسِيَّةِ
    /// الدَورِ الافتِراضيِّ لِلزائِرِ خِيارُ المالِكِ الأَكبَر.</para>
    /// </summary>
    [Fact]
    public void A_visitor_of_a_store_with_roles_gets_the_role_landing_on_the_store_root()
    {
        var home = Flat(Template("Components", "Pages", "TenantHome.razor"));

        Assert.Contains(
            "else if (loaded.Roles.Count > 0 && (!Auth.IsAuthenticated || activeRole is null))",
            home, StringComparison.Ordinal);
        Assert.Contains("<RoleLanding Slug=\"@Slug\" Tenant=\"loaded\" Role=\"@Role\"/>", home, StringComparison.Ordinal);
        Assert.Equal(1, Count(home, "<RoleLanding "));
    }

    /// <summary>
    /// <para><b>الشَريطُ مَخفِيٌّ عَلى <c>/{slug}</c> العاري وَحدَه</b> لِلزائِرِ في مَتجَرٍ
    /// ذي أَدوار — فَـ<c>/{slug}/explore</c> لَيسَ «المَدخَل» ويَظهَرُ عَلَيهِ الشَريطُ
    /// بِتَبويبِ الاستِكشافِ في التَبويباتِ الافتِراضِيَّة. ولا يُمَسُّ الإخفاءُ عَلى
    /// <c>/{slug}</c>: إظهارُه هُناكَ خِيارٌ لِلمالِك.</para>
    /// </summary>
    [Fact]
    public void The_chrome_is_hidden_only_on_the_bare_store_root_so_explore_shows_the_nav()
    {
        var layout = Flat(Template("Components", "Layout", "MainLayout.razor"));

        Assert.Contains(
            "var isEntry = trimmed.Equals($\"/{slug}\", StringComparison.OrdinalIgnoreCase) && tenantHasRoles && !Auth.IsAuthenticated;",
            layout, StringComparison.Ordinal);
        Assert.Contains("var chromeHidden = isLogin || isEntry;", layout, StringComparison.Ordinal);
        Assert.Contains("Add(\"explore\", L[\"shell.nav.explore\"], \"search\");", layout, StringComparison.Ordinal);
    }

    /// <summary>
    /// <para><b>الاستِكشافُ بِلا بَوّابَةِ دُخول</b>: الجَلسَةُ تُقرَأُ فيه مَرَّتَينِ لِلمُفَضَّلَةِ
    /// وَحدَها (إظهارُ القَلبِ ومُعَرِّفُ المُستَخدِم)، ولا تَحويلَ ولا رابِطَ دُخولٍ ولا
    /// صِفَةَ تَخويل — فَالبابُ إلَيه يَكفي، والصَفحَةُ قائِمَة.</para>
    /// </summary>
    [Fact]
    public void Explore_has_no_login_gate_and_reads_the_session_only_for_favourites()
    {
        var explore = Template("Components", "Pages", "TenantExplore.razor");

        Assert.Contains("@page \"/{slug}/explore\"", explore, StringComparison.Ordinal);
        Assert.Equal(2, Count(explore, "Auth.IsAuthenticated"));
        Assert.Contains("ShowFavorite=\"@Auth.IsAuthenticated\"", explore, StringComparison.Ordinal);
        Assert.Contains("Auth.IsAuthenticated ? Auth.UserId : null", explore, StringComparison.Ordinal);

        foreach (var gate in new[] { "Redirect", "NavigateTo", "login", "[Authorize" })
            Assert.DoesNotContain(gate, explore, StringComparison.Ordinal);
    }

    /// <summary>
    /// <para><b>وكُلُّ مَتجَرٍ مِن الاستوديو ذو أَدوار</b> — فَرعُ السُقوطِ في
    /// <c>RolesFor</c> يُخرِجُ <c>{customer, vendor, tenant_admin}</c> (‏ADR-027) — فَجَذرُه
    /// لِلزائِرِ هُوَ البَوّابَةُ دائِماً، ولَيسَ حالَةً نادِرَة.</para>
    /// </summary>
    [Fact]
    public void Every_store_born_from_the_studio_has_roles_so_its_root_is_the_role_landing()
    {
        var roles = TenantFromAnalysisFactory.RolesFor(new IncubatorSession().SuggestedPattern);

        Assert.NotEmpty(roles);
        Assert.Equal(new[] { "customer", "vendor", "tenant_admin" }, roles.Select(r => r.CatalogSlug).ToArray());
        Assert.Equal("customer", Assert.Single(roles, r => r.IsDefault).CatalogSlug);
    }

    // ─── (٣) لِمَن يُرسَمُ الرابِط — تَوصيفُ «اليَوم» (‏2026-10-13، مُراجَعَةُ الموجَةِ الثانِيَة (iii)) ───

    /// <summary>
    /// <para><b>الرابِطُ لِلزائِرِ وَحدَه</b> (‏2026-10-13 — انقَلَبَ بِقَرارٍ مَرئِيٍّ مِن
    /// <c>Today_the_browse_link_is_drawn_for_a_signed_in_user_without_an_active_role_too</c>): فَرعُ <c>TenantHome</c> كَما كانَ
    /// يُرسِلُ المُسَجَّلَ بِلا دَورٍ إلى البَوّابَة، و<c>RoleLanding.razor</c> يَحقِنُ الجَلسَةَ الآنَ ويَرسُمُ سَطرَ الاستِكشافِ داخِلَ
    /// <c>@if (!Auth.IsAuthenticated)</c> وَحدَه — فَلا يَقرَأُ مُسَجَّلٌ «دونَ تَسجيل». والنَصُّ لَم يَتَبَدَّل، وسَطرُ «لَدَيكَ حِساب»
    /// خارِجَ الشَرط.</para>
    /// </summary>
    [Fact]
    public void The_browse_link_is_drawn_for_anonymous_visitors_only()
    {
        var home = Flat(Template("Components", "Pages", "TenantHome.razor"));
        Assert.Contains("(!Auth.IsAuthenticated || activeRole is null)", home, StringComparison.Ordinal);

        var razor = RoleLanding();
        Assert.Contains("@inject AuthSession Auth", razor, StringComparison.Ordinal);
        Assert.Equal(1, Count(razor, "@if (!Auth.IsAuthenticated)"));
        Assert.Contains(
            "@if (!Auth.IsAuthenticated) { @((MarkupString)\"\\n\\n \")<div class=\"acm-role-landing-foot\"> " +
            "<a href=\"@AuthSession.LinkFor(Slug, null, \"explore\")\">@L.Markup(\"roles.landing.browse\")</a> </div> }",
            Flat(razor), StringComparison.Ordinal);

        var guarded = Flat(razor).IndexOf("@if (!Auth.IsAuthenticated)", StringComparison.Ordinal);
        var haveAccount = Flat(razor).IndexOf("@L.Markup(\"roles.landing.have_account\")", StringComparison.Ordinal);
        Assert.True(guarded > 0 && haveAccount > guarded);
        Assert.Contains("} @((MarkupString)\"\\n\\n \")<div class=\"acm-role-landing-foot\"> <a href=\"@AuthSession.LinkFor(Slug, Role, $\"login\")\">",
            Flat(razor), StringComparison.Ordinal);

        Assert.Contains("دونَ تَسجيل",
            ACommerce.Platform.I18n.LocaleCatalog.Find(ACommerce.Platform.I18n.LocaleCatalog.Arabic, "roles.landing.browse"),
            StringComparison.Ordinal);
    }
}
