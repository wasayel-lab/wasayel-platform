using System.Text;
using ACommerce.Kit.Roles;
using Xunit;

namespace ACommerce.Platform.Tests;

// ─── تَوصيف التَّكافُؤ الصِفريّ (Characterization) ─────────────────────
// المَوجَة الثالِثَة-أ مِن «الأَدوار مِلَفّات»: أَن تُقرَأ تَعريفات
// الأَدوار مِن وَثائِق Marten لِكُلّ مُستَأجِر وَقتَ التَّشغيل، فَوق
// الكاتالوج المَضمون.
//
// **نَفس مَنهَجِيَّة المَوجَتَين قَبلَها**: يُكتَب هذا المِلَفّ ويَخضَرّ
// **قَبل** أَن يُبَدَّل مَوضِع التِقاط واحِد، ثُمَّ **لا يُمَسّ سَطر واحِد
// مِنه** بَعدَ التَّبديل. فَمُرورُه عَلى الحالَتَين هو بُرهان أَنّ
// مُستَأجِراً **بِلا وَثيقَة واحِدَة** لَم يَتَغَيَّر سُلوكُه بِحَرف.
//
// الدَعوى المَفحوصَة، مُصاغَةً بِدِقَّة:
//
//   TenantRoleSet.Platform  ≡  RoleCatalog
//
// أَي أَنّ طَبَقَة القَرار الجَديدَة، حينَ لا تَجِد وَثيقَة مُستَأجِر
// واحِدَة، تُعيد **حَرفِيّاً** ما يُعيدُه الكاتالوج الساكِن: العَشَرَة
// تَعريفاً تَعريفاً وحَقلاً حَقلاً، والقَوالِب المُسقَطَة، وجَوابَي
// البَحث بِحَساسِيَّة الحالَة، والتَّركيب بِفَتَحاتِه السِتّ، ونَمَط
// الصَفقَة بِتَرتيب غَلَبَتِه.
//
// ولِذلك تُقارَن **المَسارات** لا اللَقطات المَحفوظَة: لَقطَة ذَهَبِيَّة
// مَكتوبَة بِاليَد كانَت سَتُثبِت أَنّ الجَديد يُطابِق نَصّاً كَتَبتُه
// أَنا؛ والمُقارَنَة المُباشَرَة تُثبِت أَنَّه يُطابِق **المَسار القَديم
// نَفسَه** — وهو ما يَعني الشَيء. (اللَقطَتانِ الذَّهَبِيَّتانِ
// القائِمَتانِ — RoleCatalogCharacterizationTests و
// RoleCompositionCharacterizationTests — تَحرُسانِ المَسار القَديم مِن
// الانحِراف، فَالحَلقَة مُغلَقَة بَينَهُما وبَين هذا المِلَفّ.)

public class TenantRoleZeroEquivalenceTests
{
    private static readonly TenantRoleSet Zero = TenantRoleSet.Platform;

    /// <summary>السلاجات المَفحوصَة في البَحث والتَّركيب — العَشَرَة،
    /// ثُمَّ الحالات الحَدِّيَّة الَّتي يُغَطّيها فَرع السُقوط: مَجهول،
    /// واختِلاف حالَة حَرف، وفارِغ، وnull.</summary>
    private static readonly string?[] ProbedSlugs =
    {
        "customer", "rider", "vendor", "driver", "host", "shipper", "tenant_admin",
        "broker", "mover", "organizer",
        "slug_from_the_future", "Customer", "", null,
    };

    /// <summary>تَركيبات أَدوار لِفَحص اشتِقاق نَمَط الصَفقَة — تَشمَل
    /// الأَولَوِيَّة (trip قَبل rental) وعَدَم التَّماثُل المُوَثَّق
    /// (shipper وَحدَه ليسَ trip) وتَركيبَة المُستَأجِر التَّجريبيّ.</summary>
    private static readonly string?[][] ProbedTenants =
    {
        new string?[] { },
        new string?[] { "customer" },
        new string?[] { "rider", "driver" },
        new string?[] { "host" },
        new string?[] { "rider", "driver", "host" },
        new string?[] { "shipper" },
        new string?[] { "vendor", "customer" },
        new string?[] { "broker", "mover", "organizer", "customer" },
        new string?[] { "slug_from_the_future", null, "" },
    };

    // ─── ١. التَّعريفات: العَشَرَة تَعريفاً تَعريفاً ────────────────────

    [Fact]
    public void ZeroDocuments_Definitions_areTheSameInstanceAsCatalog()
    {
        // ليسَ «مُتَساوِيانِ» بَل «هُما هُما» — أَقوى ما يُقال، وأَرخَص
        // مِن أَيّ مُقارَنَة: بِلا وَثائِق لا تُبنى قائِمَة جَديدَة أَصلاً.
        Assert.Same(RoleCatalog.Definitions, Zero.Definitions);
        Assert.Same(RoleCatalog.All, Zero.All);
    }

    [Fact]
    public void ZeroDocuments_EveryDefinition_MatchesCatalogFieldByField()
    {
        Assert.Equal(RoleCatalog.Definitions.Count, Zero.Definitions.Count);
        Assert.Equal(10, Zero.Definitions.Count);

        for (var i = 0; i < RoleCatalog.Definitions.Count; i++)
        {
            var old = RoleCatalog.Definitions[i];
            var neu = Zero.Definitions[i];

            Assert.Equal(old.Slug,      neu.Slug);
            Assert.Equal(old.Icon,      neu.Icon);
            Assert.Equal(old.HomeRoute, neu.HomeRoute);
            Assert.Equal(old.Label.Ar,       neu.Label.Ar);
            Assert.Equal(old.Label.En,       neu.Label.En);
            Assert.Equal(old.Description.Ar, neu.Description.Ar);
            Assert.Equal(old.Description.En, neu.Description.En);
            Assert.Equal(old.Permissions.ToArray(), neu.Permissions.ToArray());
            Assert.Equal(old.DealPatternAffinity, neu.DealPatternAffinity);

            Assert.Equal(old.Fields.Count, neu.Fields.Count);
            for (var f = 0; f < old.Fields.Count; f++)
            {
                var of = old.Fields[f];
                var nf = neu.Fields[f];
                Assert.Equal(of.Code,       nf.Code);
                Assert.Equal(of.Label.Ar,   nf.Label.Ar);
                Assert.Equal(of.Type,       nf.Type);
                Assert.Equal(of.IsRequired, nf.IsRequired);
                Assert.Equal(
                    of.Options.Select(o => $"{o.Value}|{o.Label.Ar}").ToArray(),
                    nf.Options.Select(o => $"{o.Value}|{o.Label.Ar}").ToArray());
            }

            Assert.Equal(old.Composition.Home,          neu.Composition.Home);
            Assert.Equal(old.Composition.CreateListing, neu.Composition.CreateListing);
            Assert.Equal(old.Composition.Nav,           neu.Composition.Nav);
            Assert.Equal(old.Composition.Explore,       neu.Composition.Explore);
            Assert.Equal(old.Composition.PublicProfile, neu.Composition.PublicProfile);
            Assert.Equal(old.Composition.Extras.ToArray(), neu.Composition.Extras.ToArray());
        }
    }

    // ─── ٢. القَوالِب المُسقَطَة ─────────────────────────────────────────

    [Fact]
    public void ZeroDocuments_EveryTemplate_MatchesCatalogFieldByField()
    {
        Assert.Equal(RoleCatalog.All.Count, Zero.All.Count);

        for (var i = 0; i < RoleCatalog.All.Count; i++)
        {
            var old = RoleCatalog.All[i];
            var neu = Zero.All[i];

            Assert.Equal(old.Slug,        neu.Slug);
            Assert.Equal(old.Label,       neu.Label);
            Assert.Equal(old.Icon,        neu.Icon);
            Assert.Equal(old.Description, neu.Description);
            Assert.Equal(old.HomeRoute,   neu.HomeRoute);
            Assert.Equal(old.Permissions.ToArray(), neu.Permissions.ToArray());
            Assert.Equal(
                old.Fields.Select(Describe).ToArray(),
                neu.Fields.Select(Describe).ToArray());
        }
    }

    // ─── ٣. البَحث — بِنَفس حَساسِيَّة الحالَة ────────────────────────

    [Fact]
    public void ZeroDocuments_Find_AnswersExactlyLikeCatalog()
    {
        foreach (var slug in ProbedSlugs)
        {
            if (slug is null) continue;   // Find لا يَقبَل null في المَسارَين

            Assert.Equal(RoleCatalog.Find(slug)?.Slug, Zero.Find(slug)?.Slug);
            Assert.Equal(
                RoleCatalog.FindDefinition(slug)?.Slug,
                Zero.FindDefinition(slug)?.Slug);
        }
    }

    // ─── ٤. التَّركيب — الفَتَحات السِتّ لِكُلّ سلاج ──────────────────

    [Fact]
    public void ZeroDocuments_ResolveComposition_MatchesStaticResolverForEverySlug()
    {
        foreach (var slug in ProbedSlugs)
        {
            var old = RoleCompositionResolver.Resolve(slug);
            var neu = Zero.ResolveComposition(slug);

            // المَجهول والفارِغ وnull: نَفس كائِن السُقوط نَفسِه.
            Assert.Same(old, neu);
        }
    }

    [Fact]
    public void ZeroDocuments_CompositionSurface_MatchesStaticResolverTextually()
    {
        // لَقطَة نَصِّيَّة لِلمَسارَين تُقارَن ببَعضِها — تُظهِر الفَرق
        // كامِلاً في رِسالَة الفَشَل إن وَقَعَ، بِلا تَنقيب.
        var oldText = Capture(RoleCompositionResolver.Resolve);
        var newText = Capture(Zero.ResolveComposition);

        Assert.True(
            string.Equals(oldText, newText, StringComparison.Ordinal),
            $"انحَرَفَ المَسار الجَديد عَن القَديم بِلا وَثائِق.\n\n" +
            $"=== القَديم (RoleCatalog) ===\n{oldText}\n\n" +
            $"=== الجَديد (TenantRoleSet.Platform) ===\n{newText}");
    }

    // ─── ٥. نَمَط الصَفقَة — بِتَرتيب الغَلَبَة ────────────────────────

    [Fact]
    public void ZeroDocuments_DealPattern_MatchesStaticAffinityResolver()
    {
        foreach (var roles in ProbedTenants)
        {
            Assert.Equal(
                RoleDealPatternAffinity.Resolve(roles),
                Zero.DealPattern(roles));
        }
    }

    // ─── ٦. التَجسيد — بِلا وَثائِق لا يَلمَس القائِمَة ────────────────

    [Fact]
    public void ZeroDocuments_Materialize_ReturnsTheSameListInstance()
    {
        var roles = new List<Role>
        {
            RoleCatalog.InstantiateRole(RoleCatalog.Find("customer")!, 0),
            RoleCatalog.InstantiateRole(RoleCatalog.Find("vendor")!,   1),
        };

        Assert.Same(roles, TenantRoleSet.Platform.Materialize(roles));
        Assert.Empty(TenantRoleSet.Platform.TenantAuthored);
        Assert.Null(TenantRoleSet.Platform.TenantSlug);
    }

    [Fact]
    public void FromDocuments_WithNoDocuments_IsTheZeroSet()
    {
        var built = TenantRoleSet.FromDocuments("any-tenant", Array.Empty<TenantRoleDefinition>());

        Assert.Same(RoleCatalog.Definitions, built.Definitions);
        Assert.Same(RoleCatalog.All, built.All);
        Assert.Empty(built.TenantAuthored);
    }

    [Fact]
    public void FromDocuments_IgnoresEverythingThatIsNotApproved()
    {
        // مُعَلَّق ومَرفوض لا يَبلُغان طَبَقَة القَرار — وهذا هو ما
        // يَجعَل «قَبل الاعتِماد: البَوّابَة بِلا الدَور» صَحيحاً
        // بِالبِناء لا بِالتَّوقيت.
        var docs = new[]
        {
            new TenantRoleDefinition
            {
                Id = "tailor", Slug = "tailor", Status = TenantRoleStatuses.Pending,
                DefinitionJson = "{\"slug\":\"tailor\"}"
            },
            new TenantRoleDefinition
            {
                Id = "smith", Slug = "smith", Status = TenantRoleStatuses.Rejected,
                DefinitionJson = "{\"slug\":\"smith\"}"
            },
        };

        var built = TenantRoleSet.FromDocuments("any-tenant", docs);

        Assert.Empty(built.TenantAuthored);
        Assert.Same(RoleCatalog.Definitions, built.Definitions);
    }

    // ─── أَدَوات ─────────────────────────────────────────────────────────

    private static string Capture(Func<string?, RoleComposition> resolve)
    {
        var sb = new StringBuilder();
        foreach (var slug in ProbedSlugs)
        {
            var c = resolve(slug);
            sb.Append(slug is null ? "(null)" : slug.Length == 0 ? "(empty)" : slug)
              .Append(" => ")
              .Append($"home={c.Home};create={c.CreateListing};nav={c.Nav};")
              .Append($"explore={c.Explore};profile={c.PublicProfile ?? "(null)"};")
              .Append($"extras=[{string.Join(",", c.Extras)}]\n");
        }
        return sb.ToString();
    }

    private static string Describe(RoleField f) =>
        $"{f.Code}|{f.Label}|{f.Type}|{f.IsRequired}|" +
        string.Join("~", f.Options.Select(o => $"{o.Value}={o.Label}"));
}
