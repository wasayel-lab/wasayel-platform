using System.Text.RegularExpressions;
using ACommerce.Kit.Roles;
using ACommerce.Kit.Tenants;
using ACommerce.Templates.Customer.Marketplace.Services.Incubator;
using ACommerce.Templates.Customer.Marketplace.Services.Patterns;
using Xunit;

namespace ACommerce.Platform.Tests;

// ═══ تَوصيفُ قارِئِ نَمَطِ الواجِهَة — قَبلَ إصلاحِ الكاتِبِ في FLGAP-04 ═══════════
//
// **المَقيسُ** (موجَزُ الفَجَوات P1-1؛ ‏ADR-032 §٦‑ج الصَفُّ 2؛ `21-HOW-MANY-FLOWS.md`
// §٣‑ز و`FLGAP-04`): كُلُّ مَتجَرٍ يولَدُ مِن `/studio/begin` يَمُرُّ بِمَسارِ العَميلِ
// الَّذي يَحفَظُ `description` وَحدَه، فَـ`Answers` فارِغَةٌ والقِطاعُ `""` والنَمَطُ `""`،
// فَـ`NormalizePattern` ← `marketplace` ← `PatternToSector` ← `"ecommerce"` ←
// فِئَتانِ `products`/`deals` **كِندُهُما `"commercial"`** — والقارِئُ
// `PatternProfileResolver.PatternOf` يَقرَأُ `commercial` إيجاراً: `AppPattern.Rental`
// ⇒ `UsesCart: false` ⇒ لا استِمارَةَ إلى `cart/add` ولا تَبويبَ سَلَّة، فَالمُشتَري لا
// يَبلُغُ `/checkout` أَصلاً.
//
// **ولا اختِبارَ يُسَمّي `PatternOf` قَبلَ هذا المِلَفّ** (‏`grep -rl PatternOf tests --include=*.cs`
// = صِفر، و`PatternProfile` و`AppPattern` صِفرٌ كَذلك؛ ‏2026-10-11).
//
// **ما يُثَبِّتُه هذا المِلَفُّ ويَبقى أَخضَرَ بِلا تَعديلِ حَرفٍ بَعدَ الإصلاح (القاعِدَة ٣)**:
// **القارِئ** كَما هُوَ — لِأَنّ الإصلاحَ عِندَ **الكاتِب** (ما يَكتُبُه
// `CategoriesForSector("ecommerce")` كِنداً) لا عِندَ القارِئ. والسَبَبُ مُثَبَّتٌ هُنا
// بِعَينِه: القارِئُ لا يَرى إلّا الكِند، و`commercial` في مُعجَمِ المُستَودَعِ **عَقارٌ
// تِجارِيّ** (‏`ejar`: مَكتَبٌ ومَحَلٌّ ومَخزَن؛ وقِطاعُ `realestate` في المَصنَعِ نَفسِه:
// «مَحَلّات») — فَمَتجَرُ المُنتَجاتِ ومَتجَرُ المَحَلّاتِ المُؤَجَّرَةِ بِنَفسِ الأَدوارِ
// وبِنَفسِ الكِندِ يَقَعانِ في نَمَطٍ واحِد. والقارِئُ **اثنان لا واحِد**: نُسخَةٌ ثانِيَةٌ
// مِن الاشتِقاقِ في `MainLayout.razor` تُخفي تَبويبَ السَلَّةِ بِالقاعِدَةِ نَفسِها —
// فَإصلاحٌ عِندَ القارِئِ يَمَسُّ مَوضِعَين، وعِندَ الكاتِبِ سَطرَين يَمُرّانِ إلى القارِئَينِ
// مَعاً. وأَمّا ما يَكتُبُه المَصنَعُ فَيُقاسُ في مِلَفِّ الإصلاح، لِأَنَّه هُوَ ما يَتَبَدَّل.

public class PatternOfCharacterizationTests
{
    private static string RepoRoot => ThemeZeroEquivalenceTests.RepoRoot;

    private static string Source(params string[] parts)
    {
        var path = Path.Combine(RepoRoot, Path.Combine(parts));
        Assert.True(File.Exists(path), $"مَصدَرٌ مَفقود: {path} — الأَداةُ عَمياءُ بِلا طَرَفٍ مَقروء.");
        var text = File.ReadAllText(path);
        Assert.True(text.Length > 500, $"أَداةٌ عَمياء: {path} طولُه {text.Length} مِحرَفاً — لَم يُقرَأ.");
        return text;
    }

    /// <summary>المَصدَرُ بِمَسافاتٍ مَطوِيَّة — فَالفَحصُ عَلى الكودِ لا عَلى التَنسيق.</summary>
    private static string Flat(string text) => Regex.Replace(text, @"\s+", " ");

    private static string Template(params string[] parts)
        => Source(new[] { "libs", "templates", "ACommerce.Templates.Customer.Marketplace" }.Concat(parts).ToArray());

    /// <summary>أَدوارُ كُلِّ مَتجَرٍ يولَدُ مِن الاستوديو — بِحَرفِ ما يَكتُبُه المَصنَعُ
    /// لِلنَمَطِ الَّذي يَسقُطُ إلَيهِ الفارِغُ والمَجهول (‏ADR-027).</summary>
    private static List<Role> StudioStoreRoles()
        => TenantFromAnalysisFactory.RolesFor(TenantFromAnalysisFactory.FallbackPattern).ToList();

    private static Category Cat(string slug, string kind, int order)
        => new() { Slug = slug, Label = slug, Icon = "tag", Kind = kind, SortOrder = order };

    private static Tenant Store(List<Role> roles, params Category[] categories) => new()
    {
        Id = "pattern-probe",
        Name = "مَتجَرُ الفَحص",
        Roles = roles,
        Categories = categories.ToList(),
    };

    // ─── (١) العَطَب: أَدوارُ الاستوديو + كِندُ commercial ⇐ إيجارٌ بِلا سَلَّة ───

    /// <summary>
    /// <para><b>أَدوارُ المَتجَرِ المَولودِ مِن الاستوديو وفِئَتاه كَما يَكتُبُهُما
    /// المَصنَعُ اليَوم ⇐ <c>Rental</c></b>: لا سَلَّة، وزِرُّه «اطلُب مُعايَنَة»،
    /// وتَحذيرُ احتِيالٍ عَن تَأمينِ عَقدِ إيجار — عَلى مَتجَرِ بَيع.</para>
    /// </summary>
    [Fact]
    public void Studio_store_roles_with_commercial_kinds_resolve_to_Rental_without_a_cart()
    {
        var roles = StudioStoreRoles();
        Assert.Equal(new[] { "customer", "vendor", "tenant_admin" }, roles.Select(r => r.CatalogSlug).ToArray());

        var store = Store(roles, Cat("products", "commercial", 0), Cat("deals", "commercial", 1));

        Assert.Equal(AppPattern.Rental, PatternProfileResolver.PatternOf(store));

        var profile = PatternProfileResolver.Of(store);
        Assert.False(profile.UsesCart);
        Assert.Equal("اطلُب مُعايَنَة", profile.PrimaryCtaLabel);
        Assert.Equal("لا تَدفَع التَأمين قَبل تَوقيع العَقد ومُعايَنَة العَقار.", profile.FraudWarning);
    }

    /// <summary>
    /// <para><b>القارِئُ لا يَرى إلّا الكِند</b>: مَتجَرُ مُنتَجاتٍ ومَتجَرُ مَحَلّاتٍ
    /// تِجارِيَّةٍ تُؤَجَّر (‏<c>office</c>/<c>shop</c>/<c>storage</c> بِكِندِ
    /// <c>commercial</c> كَما يَبذُرُها <c>ejar</c>) بِنَفسِ الأَدوارِ يَقَعانِ في
    /// نَمَطٍ واحِد. فَـ<c>commercial</c> في مُعجَمِ المُستَودَعِ <b>عَقارٌ تِجارِيّ</b>،
    /// وفَرعُ القارِئِ صَحيحٌ لِمَن كُتِبَ لَه — والكَلِمَةُ الخاطِئَةُ عِندَ الكاتِب.</para>
    /// </summary>
    [Fact]
    public void The_reader_sees_only_the_kind_so_products_and_commercial_property_resolve_alike()
    {
        var products = Store(StudioStoreRoles(), Cat("products", "commercial", 0), Cat("deals", "commercial", 1));
        var property = Store(StudioStoreRoles(),
            Cat("office", "commercial", 0), Cat("shop", "commercial", 1), Cat("storage", "commercial", 2));

        Assert.Equal(PatternProfileResolver.PatternOf(property), PatternProfileResolver.PatternOf(products));
        Assert.Equal(AppPattern.Rental, PatternProfileResolver.PatternOf(property));

        // ومَصدَرا هذا المَعنى في المُستَودَعِ نَفسِه: بَذرُ `ejar` وقِطاعُ `realestate` في المَصنَع.
        var seed = Flat(Source("apps", "V1.App", "Seed", "PlatformSeed.cs"));
        Assert.Contains("(\"office\", \"مَكتَب\", \"💼\", \"commercial\")", seed, StringComparison.Ordinal);
        Assert.Contains("(\"shop\", \"مَحلّ\", \"🏪\", \"commercial\")", seed, StringComparison.Ordinal);

        var factory = Flat(Template("Services", "Incubator", "TenantFromAnalysisFactory.cs"));
        Assert.Contains("C(\"shops\", \"مَحَلّات\", \"🏪\", \"commercial\", 2)", factory, StringComparison.Ordinal);
    }

    // ─── (٢) ما يُعطي Marketplace اليَوم: الكِندُ الفارِغ ─────────────────────

    /// <summary>
    /// <para><b>مَتجَرٌ بِشَكلِ <c>theme-demo</c> ⇐ <c>Marketplace</c> بِسَلَّة</b>:
    /// بِلا أَدوارٍ وفِئَتانِ بِكِندٍ فارِغ — بِحَرفِ <c>AppearanceBaselineSeeder</c>
    /// الَّذي يَقولُ تَعليقُه إنّ ذلكَ «يُعطي <c>PatternOf</c> نَمَطَ <c>Marketplace</c>».</para>
    /// </summary>
    [Fact]
    public void A_theme_demo_shaped_store_with_empty_kinds_and_no_roles_resolves_to_Marketplace_with_a_cart()
    {
        var seeder = Flat(Source("apps", "V1.App", "Seed", "AppearanceBaselineSeeder.cs"));
        Assert.Contains(
            "new Category { Slug = \"general\", Label = \"عام\", Icon = \"🏠\", Kind = \"\", SortOrder = 0 }",
            seeder, StringComparison.Ordinal);
        Assert.Contains(
            "new Category { Slug = \"services\", Label = \"خَدَمات\", Icon = \"🏠\", Kind = \"\", SortOrder = 1 }",
            seeder, StringComparison.Ordinal);

        var themeDemo = new Tenant
        {
            Id = "theme-demo",
            Name = "مَتجَر التَّجرِبَة",
            Roles = new(),
            Categories = new()
            {
                new Category { Slug = "general",  Label = "عام",      Icon = "🏠", Kind = "", SortOrder = 0 },
                new Category { Slug = "services", Label = "خَدَمات", Icon = "🏠", Kind = "", SortOrder = 1 },
            },
        };

        Assert.Equal(AppPattern.Marketplace, PatternProfileResolver.PatternOf(themeDemo));

        var profile = PatternProfileResolver.Of(themeDemo);
        Assert.True(profile.UsesCart);
        Assert.Equal("أَضِف لِلسَلَّة", profile.PrimaryCtaLabel);
        Assert.Null(profile.FraudWarning);
    }

    /// <summary>
    /// <para><b>وأَدوارُ الاستوديو نَفسُها بِكِندٍ فارِغ ⇐ <c>Marketplace</c></b> —
    /// فَالقارِئُ يُعطي ما يُرادُ لِمَتجَرِ البَيعِ <b>مُنذُ الآن</b> مَتى كَتَبَ
    /// الكاتِبُ كِنداً لا يَعني عَقاراً. وهذا ما يَجعَلُ الإصلاحَ سَطرَينِ عِندَ
    /// الكاتِبِ لا فَرعاً جَديداً عِندَ القارِئ.</para>
    /// </summary>
    [Fact]
    public void Studio_store_roles_with_empty_kinds_resolve_to_Marketplace_with_a_cart()
    {
        var store = Store(StudioStoreRoles(), Cat("products", "", 0), Cat("deals", "", 1));

        Assert.Equal(AppPattern.Marketplace, PatternProfileResolver.PatternOf(store));
        Assert.True(PatternProfileResolver.Of(store).UsesCart);
    }

    // ─── (٣) ما يَحرُسُه UsesCart: البابُ الوَحيدُ إلى السَلَّة ──────────────

    /// <summary>
    /// <para><b>الاستِمارَةُ الوَحيدَةُ إلى <c>cart/add</c> في الواجِهَةِ كُلِّها خَلفَ
    /// <c>profile?.UsesCart == true</c></b> في صَفحَةِ تَفصيلِ الإعلان — فَنَمَطٌ بِلا
    /// سَلَّةٍ يَعني مُشتَرِياً لا يَبلُغُ السَلَّةَ ولا <c>/checkout</c> بِالنَقر.</para>
    /// </summary>
    [Fact]
    public void The_only_form_to_cart_add_sits_behind_UsesCart_on_the_listing_page()
    {
        var detail = Template("Components", "Pages", "TenantListingDetail.razor");

        Assert.Equal(1, Regex.Matches(detail, Regex.Escape("/cart/add")).Count);

        var gate = detail.IndexOf("@if (profile?.UsesCart == true)", StringComparison.Ordinal);
        var form = detail.IndexOf("/cart/add", StringComparison.Ordinal);
        Assert.True(gate >= 0, "بَوّابَةُ UsesCart غائِبَةٌ عَن صَفحَةِ التَفصيل.");
        Assert.True(form > gate, "استِمارَةُ cart/add لَيسَت بَعدَ بَوّابَةِ UsesCart.");
        Assert.DoesNotContain("else", detail[gate..form], StringComparison.Ordinal);

        // وصَفَحاتُ الواجِهَةِ الأُخرى لا تَحمِلُ استِمارَةً ثانِيَةً إلى السَلَّة.
        var pages = Directory.GetFiles(
            Path.Combine(RepoRoot, "libs", "templates", "ACommerce.Templates.Customer.Marketplace", "Components"),
            "*.razor", SearchOption.AllDirectories);
        Assert.True(pages.Length > 50, $"أَداةٌ عَمياء: {pages.Length} صَفحَةً فَقَط.");
        var withCartAdd = pages.Where(p => File.ReadAllText(p).Contains("/cart/add", StringComparison.Ordinal))
                               .Select(Path.GetFileName).ToArray();
        Assert.Equal(new[] { "TenantListingDetail.razor" }, withCartAdd);
    }

    /// <summary>
    /// <para><b>والقارِئُ الثاني</b>: <c>MainLayout.razor</c> يَشتَقُّ النَمَطَ بِنُسخَتِه
    /// هُوَ لا بِـ<c>PatternOf</c>، ويَقرَأُ <c>commercial</c> إيجاراً كَذلك، ويُخفي
    /// تَبويبَ السَلَّةِ لِما لَيسَ <c>marketplace</c>/<c>service</c>. فَإصلاحٌ عِندَ
    /// القارِئِ مَوضِعان، وعِندَ الكاتِبِ يَمُرُّ إلى الاثنَينِ مَعاً.</para>
    /// </summary>
    [Fact]
    public void MainLayout_keeps_its_own_copy_of_the_derivation_and_it_also_reads_commercial_as_rental()
    {
        var layout = Flat(Template("Components", "Layout", "MainLayout.razor"));

        Assert.Contains(
            "roleCats.Contains(\"host\") || kinds.Contains(\"residential\") || kinds.Contains(\"commercial\") || kinds.Contains(\"roommate\") ? \"rental\" : \"marketplace\";",
            layout, StringComparison.Ordinal);
        Assert.Contains("var usesCart = listingPattern is \"marketplace\" or \"service\" or null", layout, StringComparison.Ordinal);
        Assert.Contains("if (usesCart) Add(\"cart\", L[\"shell.nav.cart\"], \"package\", bottom: false);", layout, StringComparison.Ordinal);
        Assert.DoesNotContain("PatternOf", layout, StringComparison.Ordinal);
    }
}
