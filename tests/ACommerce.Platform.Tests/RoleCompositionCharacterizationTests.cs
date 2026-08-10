using System.Text;
using ACommerce.Kit.Roles;
using ACommerce.Kit.Tenants;
using ACommerce.Templates.Customer.Marketplace;
using Xunit;

namespace ACommerce.Platform.Tests;

// ─── اختِبار تَوصيف قَرار التَّصيير (Characterization) ────────────────
// المَوجَة الثانِيَة مِن «الأَدوار مِلَفّات»: أَن يَقرَأ التَّصيير قِسم
// composition مِن مِلَفّات التَّعريف بَدَل الـ switch المُجَمَّعَة عَلى
// أَسماء الأَدوار — بِسُلوك مُطابِق حَرفاً بِحَرف.
//
// **مَنهَجِيَّة المَوجَة الأولى نَفسُها**: يُكتَب هذا المِلَفّ ويَخضَرّ
// **قَبل** أَيّ تَبديل، عَلى الكود الَّذي ما يَزال يُقَرِّر بِالـ switch
// (المُستَخرَجَة إلى `RoleCompositionResolver.Resolve` بِلا تَغيير
// سُلوك)، ثُمَّ **لا يُمَسّ سَطر واحِد مِنه** بَعدَ أَن يَصير مَصدَر
// الجَواب مِلَفّات التَّعريف. فَمُرورُه عَلى الحالَتَين هو بُرهان
// التَّطابُق، لا دَعوى التَّطابُق.
//
// ما يَلتَقِطُه:
//   • الفَتحات السِتّ لِكُلّ دَور مِن السَبعَة — بِأَسماء المُكَوِّنات
//     حَرفِيّاً، وبِتَرتيب `extras` لا كَمَجموعَة.
//   • الحالات الحَدِّيَّة الَّتي كانَ يُغَطّيها فَرع `default` في كُلّ
//     `switch`: null، وسِلسِلَة فارِغَة، وslug مَجهول، وslug بِحالَة
//     حَرف مُختَلِفَة (`Customer`) — والمُقارَنَة حَسّاسَة لِلحالَة.
//   • `PatternFromTenant` عَلى تَركيبات الأَدوار الواقِعِيَّة — بِما
//     فيها التَّركيبات الَّتي تُظهِر الأَولَوِيَّة (trip قَبل rental)
//     وعَدَم التَّماثُل المُوَثَّق (shipper وَحدَه ليسَ trip).
//
// ─── تَوسِعَة مَوجَة «تَأليف الأَدوار» (2026-08-10) ────────────────────
// ثَلاثَة أَدوار جَديدَة أُلِّفَت **مِلَفّاتٍ خالِصَة** (`broker`,
// `mover`, `organizer`)، ولِذلك كانَ توسيع هذا المِلَفّ **إضافَة
// خالِصَة**: صِفر سَطر مَحذوف وصِفر سَطر مُعَدَّل و‏31 سَطراً مُضافاً،
// مَقيسَة بِـ diff. وهو المَوضِع الَّذي يُبَرهِن الدَعوى المَركَزِيَّة:
// فَتَحات الأَدوار الثَّلاثَة السِتّ تُقرَأ مِن مِلَفّاتِها، ولَم يُضَف
// سَطر واحِد إلى أَيّ مِن مَواضِع التَّصيير السِتَّة لِتَعرِفَها.
// وصَفّ `demo-four` (‏broker+mover+organizer+customer) هو **نَفس تَركيبَة
// المُستَأجِر التَّجريبيّ الحَيّ** — يُشتَقّ `trip` مِن انجِذاب `mover`
// وَحدَه، فَالبُرهان الحَيّ والبُرهان الوَحدَويّ يَقيسان الشَيء نَفسَه.

public class RoleCompositionCharacterizationTests
{
    /// <summary>الأَدوار المَفحوصَة — السَبعَة بِتَرتيب الفِهرِس، ثُمَّ
    /// المَجهول والفارِغ وnull واختِلاف الحالَة.
    ///
    /// <para><b>وأُلحِقَت بِها الثَّلاثَة المُؤَلَّفَة مِلَفّاتٍ</b> في
    /// مَوجَة «تَأليف الأَدوار» — في الذَّيل لا في الوَسَط، لِتَبقى أَسطُر
    /// السَبعَة والحالات الحَدِّيَّة في اللَّقطَة الذَّهَبيَّة عَلى
    /// مَواضِعِها ونَصِّها حَرفاً بِحَرف. وهذا هو <b>تَثبيت التَّصيير</b>
    /// لِلأَدوار الجَديدَة: أَنّ فَتَحاتِها السِتّ تُقرَأ مِن مِلَفّاتِها
    /// بِلا سَطر كود واحِد أُضيفَ إلى أَيّ مَوضِع تَصيير.</para></summary>
    private static readonly string?[] ProbedSlugs =
    {
        "customer", "rider", "vendor", "driver", "host", "shipper", "tenant_admin",
        "slug_from_the_future", "Customer", "", null,
        "broker", "mover", "organizer"
    };

    /// <summary>تَركيبات أَدوار المُستَأجِر المَفحوصَة في اشتِقاق
    /// النَّمَط. كُلّ صَفّ: تَسمِيَة لِلقِراءَة ثُمَّ CatalogSlugs.</summary>
    private static readonly (string Name, string[] Roles)[] ProbedTenants =
    {
        ("(no-roles)",            new string[0]),
        ("customer",              new[] { "customer" }),
        ("rider",                 new[] { "rider" }),
        ("vendor",                new[] { "vendor" }),
        ("driver",                new[] { "driver" }),
        ("host",                  new[] { "host" }),
        ("shipper",               new[] { "shipper" }),
        ("tenant_admin",          new[] { "tenant_admin" }),
        ("rider+driver",          new[] { "rider", "driver" }),
        ("rider+vendor",          new[] { "rider", "vendor" }),
        ("driver+host",           new[] { "driver", "host" }),
        ("host+vendor",           new[] { "host", "vendor" }),
        ("shipper+host",          new[] { "shipper", "host" }),
        ("shipper+vendor",        new[] { "shipper", "vendor" }),
        ("customer+vendor+host",  new[] { "customer", "vendor", "host" }),
        ("customer+vendor",       new[] { "customer", "vendor" }),
        ("all-seven",             new[] { "customer", "rider", "vendor", "driver",
                                          "host", "shipper", "tenant_admin" }),
        ("unknown-only",          new[] { "slug_from_the_future" }),
        ("unknown+host",          new[] { "slug_from_the_future", "host" }),
        ("wrong-case-Rider",      new[] { "Rider" }),
        // ─── مَوجَة «تَأليف الأَدوار» — مُلحَقَة في الذَّيل ───────────────
        // الصُفوف الَّتي تُبَرهِن أَنّ الانجِذاب صارَ بَياناً: مُستَأجِر
        // بِـ mover وَحدَه يُشتَقّ trip مِن مِلَفّ الدَور لا مِن شَرط في
        // كود، وbroker/organizer بِلا انجِذاب فَيَبقَيان marketplace،
        // والغَلَبَة المُعلَنَة تَعمَل عَلَيها كَما تَعمَل عَلى القَديمَة.
        ("broker",                new[] { "broker" }),
        ("mover",                 new[] { "mover" }),
        ("organizer",             new[] { "organizer" }),
        ("broker+organizer",      new[] { "broker", "organizer" }),
        ("mover+host",            new[] { "mover", "host" }),
        ("mover+broker+customer", new[] { "mover", "broker", "customer" }),
        ("demo-four",             new[] { "broker", "mover", "organizer", "customer" }),
    };

    /// <summary>اللَّقطَة الذَّهَبيَّة — نَصّ حَرفيّ لا يُشتَقّ مِن الكود
    /// المَفحوص بِأَيّ حال (وإلّا صارَ الاختِبار دَورَة فارِغَة).</summary>
    private const string Golden =
"""
# Role composition / render-decision characterization snapshot

[composition]
--- customer
home          = defaultHome
createListing = defaultCreateForm
nav           = defaultNav
explore       = defaultExplore
publicProfile = (null)
extras        = (none)
--- rider
home          = riderHome
createListing = riderCreateRequest
nav           = riderNav
explore       = defaultExplore
publicProfile = (null)
extras        = driversList
--- vendor
home          = sellerHome
createListing = defaultCreateForm
nav           = vendorNav
explore       = defaultExplore
publicProfile = vendorProfile
extras        = (none)
--- driver
home          = driverHome
createListing = defaultCreateForm
nav           = driverNav
explore       = driverExplore
publicProfile = (null)
extras        = driverArea
--- host
home          = sellerHome
createListing = defaultCreateForm
nav           = vendorNav
explore       = defaultExplore
publicProfile = vendorProfile
extras        = (none)
--- shipper
home          = driverHome
createListing = defaultCreateForm
nav           = driverNav
explore       = driverExplore
publicProfile = (null)
extras        = driverArea
--- tenant_admin
home          = defaultHome
createListing = defaultCreateForm
nav           = adminNav
explore       = defaultExplore
publicProfile = (null)
extras        = (none)
--- slug_from_the_future
home          = defaultHome
createListing = defaultCreateForm
nav           = defaultNav
explore       = defaultExplore
publicProfile = (null)
extras        = (none)
--- Customer
home          = defaultHome
createListing = defaultCreateForm
nav           = defaultNav
explore       = defaultExplore
publicProfile = (null)
extras        = (none)
--- (empty)
home          = defaultHome
createListing = defaultCreateForm
nav           = defaultNav
explore       = defaultExplore
publicProfile = (null)
extras        = (none)
--- (null)
home          = defaultHome
createListing = defaultCreateForm
nav           = defaultNav
explore       = defaultExplore
publicProfile = (null)
extras        = (none)
--- broker
home          = sellerHome
createListing = defaultCreateForm
nav           = vendorNav
explore       = defaultExplore
publicProfile = vendorProfile
extras        = (none)
--- mover
home          = driverHome
createListing = defaultCreateForm
nav           = driverNav
explore       = driverExplore
publicProfile = (null)
extras        = driverArea
--- organizer
home          = sellerHome
createListing = defaultCreateForm
nav           = vendorNav
explore       = defaultExplore
publicProfile = vendorProfile
extras        = driversList

[composition-values-are-in-vocabulary]
customer = True
rider = True
vendor = True
driver = True
host = True
shipper = True
tenant_admin = True
slug_from_the_future = True
Customer = True
(empty) = True
(null) = True
broker = True
mover = True
organizer = True

[fallback]
home          = defaultHome
createListing = defaultCreateForm
nav           = defaultNav
explore       = defaultExplore
publicProfile = (null)
extras        = (none)

[pattern-from-tenant]
(null-tenant) -> marketplace
(no-roles) -> marketplace
customer -> marketplace
rider -> trip
vendor -> marketplace
driver -> trip
host -> rental
shipper -> marketplace
tenant_admin -> marketplace
rider+driver -> trip
rider+vendor -> trip
driver+host -> trip
host+vendor -> rental
shipper+host -> rental
shipper+vendor -> marketplace
customer+vendor+host -> rental
customer+vendor -> marketplace
all-seven -> trip
unknown-only -> marketplace
unknown+host -> rental
wrong-case-Rider -> marketplace
broker -> marketplace
mover -> trip
organizer -> marketplace
broker+organizer -> marketplace
mover+host -> trip
mover+broker+customer -> trip
demo-four -> trip
""";

    [Fact]
    public void RenderDecision_Surface_MatchesGoldenSnapshot()
    {
        var actual = Capture();

        // رِسالَة الفَشَل تَطبَع اللَّقطَتَين كامِلَتَين — الفَرق يُقرَأ
        // مُباشَرَةً بِلا تَنقيب في المُخرَجات.
        Assert.True(
            string.Equals(Golden.Trim(), actual.Trim(), StringComparison.Ordinal),
            $"انحَرَفَ قَرار التَّصيير عَن اللَّقطَة المُوَصَّفَة.\n\n" +
            $"=== المُتَوَقَّع ===\n{Golden}\n\n=== الواقِع ===\n{actual}");
    }

    private static string Capture()
    {
        var sb = new StringBuilder();
        sb.Append("# Role composition / render-decision characterization snapshot\n");

        sb.Append("\n[composition]\n");
        foreach (var slug in ProbedSlugs)
        {
            sb.Append($"--- {Name(slug)}\n");
            Append(sb, RoleCompositionResolver.Resolve(slug));
        }

        // كُلّ قيمَة يُعطيها القَرار داخِلَة في المَعجَم المُغلَق — بِما
        // فيها ما يُعطيه لِلمَجهول. هذا يَمنَع أَن يَتَسَرَّب اسم حُرّ.
        sb.Append("\n[composition-values-are-in-vocabulary]\n");
        foreach (var slug in ProbedSlugs)
        {
            var c = RoleCompositionResolver.Resolve(slug);
            var ok = c.AllComponents().All(RoleComponents.Contains);
            sb.Append($"{Name(slug)} = {ok}\n");
        }

        sb.Append("\n[fallback]\n");
        Append(sb, RoleCompositionResolver.Fallback);

        sb.Append("\n[pattern-from-tenant]\n");
        sb.Append($"(null-tenant) -> {MarketplaceTemplateExtensions.PatternFromTenant(null)}\n");
        foreach (var (name, roles) in ProbedTenants)
        {
            var t = new Tenant
            {
                Id = "probe",
                Roles = roles.Select(r => new Role { Slug = r, CatalogSlug = r }).ToList()
            };
            sb.Append($"{name} -> {MarketplaceTemplateExtensions.PatternFromTenant(t)}\n");
        }

        return sb.ToString();
    }

    private static string Name(string? slug) =>
        slug is null ? "(null)" : slug.Length == 0 ? "(empty)" : slug;

    private static void Append(StringBuilder sb, RoleComposition c)
    {
        sb.Append($"home          = {c.Home}\n");
        sb.Append($"createListing = {c.CreateListing}\n");
        sb.Append($"nav           = {c.Nav}\n");
        sb.Append($"explore       = {c.Explore}\n");
        sb.Append($"publicProfile = {c.PublicProfile ?? "(null)"}\n");
        // «(none)» لا سِلسِلَة فارِغَة — لِئَلّا تَحمِل اللَّقطَة الذَّهَبيَّة
        // مَسافَة ذَيلِيَّة غَير مَرئِيَّة يُفسِدُها أَيّ مُحَرِّر.
        sb.Append($"extras        = {(c.Extras.Count == 0 ? "(none)" : string.Join(",", c.Extras))}\n");
    }
}
