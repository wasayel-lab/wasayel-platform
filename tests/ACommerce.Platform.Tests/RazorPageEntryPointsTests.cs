using System.Reflection;
using System.Text.RegularExpressions;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ مَداخِلُ صَفحَةِ razor الَّتي لا يَفحَصُها المُتَرجِم (‏2026-10-25، تَدقيقُ الإطلاق brief-P1b-4) ═══
//
// **المَقيسُ قَبل** (عِندَ `b2fde50`): ‏`@inject IDocumentStore Store` في ‏`MyDeals.razor:3` و`RolePicker.razor:3` و`Admin/CreateTenant.razor:2`
// — و`grep -cw Store` ‏1 في كُلٍّ: حَقنٌ لا يُقرَأ. وهُوَ النَسَقُ الَّذي نَظَّفَه ‏ADR-033 §٢‑هـ (القاعِدَة ٩) في ‏`MyDealDetail.razor` وَحدَها
// «فَلا يُنسَخُ مَدخَلٌ إلى جَلسَة» — والصَفُّ الَّذي يَحرُسُه هُناكَ (`DealDetailDeliveryDisclosureTests`) يَقرَأُ تِلكَ الصَفحَةَ بِاسمِها، فَبَقِيَت
// أَخَواتُها الثَلاثُ. **والعِلَّةُ أَنَّ الحارِسَ صَفحَةٌ لا مُستَودَع** (القاعِدَة ٢). ‏و`<see cref="DealShipping.CarrierReady"/>` في
// ‏`MyDealDetail.razor:479` يُحيلُ إلى دالَّةٍ حُذِفَت (‏`DealShipping.cs:288` يَقولُ ذلك) — **والمُتَرجِمُ لا يَفحَصُ cref**: ‏`GenerateDocumentationFile`
// غَيرُ مَضبوطٍ في `Directory.Build.props`، فَمَرجِعٌ إلى المَحذوفِ يُبنى أَخضَرَ ويُقرَأُ صادِقاً.
// **وصارَ**: الحَقنُ الثَلاثُ مَحذوفَة، والمَرجِعُ إلى ‏`CarrierReadyForViewerAsync` الَّتي تُناديها الصَفحَةُ (`:580`)، وفاحِصانِ عَلى كُلِّ `.razor`:
//   (١) مَخزَنُ Marten أَو جَلسَتُه لا يُحقَنُ في صَفحَةٍ إلّا لِيُقرَأَ فيها — الطَبَقَةُ ٨ (`verify-architecture.sh`) تَعُدُّ فَتحَ الجَلسَةِ صِفراً،
//       والحَقنُ مَدخَلُه؛ والحَيُّ الوَحيدُ (`StudioAgent.razor`) يُمَرِّرُه إلى حارِسِ البابِ ولا يَفتَح.
//   (٢) كُلُّ cref في صَفحَةٍ يُسَمّي نَوعاً أَو عُضواً قائِماً في تَجميعاتِ ‏ACommerce — بِالانعِكاسِ لا بِالنَصّ.
// وكُلٌّ يَطبَعُ ما عَدَّ ويَفشَلُ عَلى الصِفر، ولِكُلٍّ عَيِّناتٌ مَحقونَةٌ تُثبِتُ أَنّه يَحمَرّ (القاعِدَة ١٠).
// **وما لَم يُمَسّ ويُقال**: حَقنٌ لا يُقرَأُ لِغَيرِ المَخزَن — مَقيسٌ عِندَ `b2fde50` ‏10 (‏`NavigationManager Nav` في ثَمانِ صَفَحات، و`Billing` في
// ‏`PayPalReturn.razor`، و`Auth` في `StudioNewIdea.razor`) — لا مَدخَلَ فيه إلى القاعِدَة، فَلَيسَ مِن هذا البَند («أ» W8-9 · ١).
public class RazorPageEntryPointsTests(ITestOutputHelper output)
{
    private static string RepoRoot => ThemeZeroEquivalenceTests.RepoRoot;

    /// <summary>كُلُّ <c>.razor</c> تَحتَ <c>libs/</c> و<c>apps/</c> بِلا <c>bin</c> ولا <c>obj</c>.</summary>
    private static IReadOnlyList<string> RazorFiles()
        => new[] { "libs", "apps" }
            .SelectMany(d => Directory.EnumerateFiles(Path.Combine(RepoRoot, d), "*.razor", SearchOption.AllDirectories))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();

    private static string Rel(string f) => Path.GetRelativePath(RepoRoot, f).Replace(Path.DirectorySeparatorChar, '/');

    // ─── (١) لا صَفحَةَ تَحقِنُ مَخزَنَ Marten أَو جَلسَتَه ثُمَّ لا تَقرَؤُه ──────────────────────────────

    /// <summary>سَطرُ حَقن — <c>@inject &lt;النَوع&gt; &lt;الاسم&gt;</c> في أَوَّلِ السَطر.</summary>
    private static readonly Regex InjectLine = new(@"^[ \t]*@inject[ \t]+(?<type>[\w.<>,]+)[ \t]+(?<name>\w+)[ \t]*\r?$", RegexOptions.Multiline);

    /// <summary>نَوعٌ يَفتَحُ جَلسَةً أَو هُوَ جَلسَة — بِحَدِّ كَلِمَةٍ وبِبادِئَةِ مَجالٍ أَو بِدونِها.</summary>
    private static readonly Regex MartenEntry = new(@"^(?:[\w]+\.)*(?:IDocumentStore|IDocumentSession|IQuerySession)$");

    /// <summary>نَصُّ الصَفحَةِ بِلا تَعليقاتِ razor وHTML وC# — فَسَطرٌ شارِحٌ لَيسَ حَقناً ولا قِراءَة.</summary>
    private static string Code(string razor)
        => WriteEndpointGuardTests.StripComments(WriteEndpointGuardTests.StripMarkupComments(razor));

    /// <summary>كُلُّ حَقنٍ لِمَخزَنِ Marten أَو جَلسَتِه في نَصِّ صَفحَة، ومَعَه: أَيُقرَأُ اسمُه في غَيرِ سَطرِ حَقنِه؟</summary>
    internal static IReadOnlyList<(string Injection, bool Read)> MartenInjections(string razor)
    {
        var code = Code(razor);
        var body = InjectLine.Replace(code, "");
        return InjectLine.Matches(code)
            .Where(m => MartenEntry.IsMatch(m.Groups["type"].Value))
            .Select(m => ($"{m.Groups["type"].Value} {m.Groups["name"].Value}",
                          Regex.IsMatch(body, $@"(?<![\w.]){Regex.Escape(m.Groups["name"].Value)}\b")))
            .ToList();
    }

    /// <summary><b>مَخزَنٌ يُحقَنُ في صَفحَةٍ يُقرَأُ فيها</b> — والحَيُّ اليَومَ واحِد: ‏<c>StudioAgent.razor</c> يُمَرِّرُه إلى
    /// <c>PlatformAdminGuard.EvaluateAsync(Store, …)</c> ولا يَفتَحُ جَلسَة (الطَبَقَةُ ٨ تَبقى صِفراً). وحَقنٌ لا يُقرَأُ مَدخَلٌ إلى جَلسَةٍ
    /// يُنسَخُ ولا يَخدِمُ شَيئاً — والثَلاثَةُ الَّتي كانَت (‏<c>MyDeals</c> و<c>RolePicker</c> و<c>Admin/CreateTenant</c>) حُذِفَت في هذا البَند.</summary>
    [Fact]
    public void Every_Marten_store_a_razor_page_injects_is_read_in_that_page()
    {
        var files = RazorFiles();
        int injects = 0, marten = 0;
        var dead = new List<string>();
        foreach (var f in files)
        {
            var text = File.ReadAllText(f);
            injects += InjectLine.Matches(Code(text)).Count;
            foreach (var (injection, read) in MartenInjections(text))
            {
                marten++;
                if (!read) dead.Add($"{Rel(f)}: @inject {injection}");
            }
        }
        output.WriteLine($"مَفحوص: {files.Count} صَفحَةَ razor · {injects} سَطرَ @inject · مِنها مَخزَنُ Marten أَو جَلسَتُه: {marten}"
            + $" · لا يُقرَأُ في صَفحَتِه: {dead.Count}");
        Assert.True(files.Count >= 150, $"أَداةٌ عَمياء: {files.Count} صَفحَةً فَقَط — والمَقيسُ ‏167.");
        Assert.True(injects >= 300, $"أَداةٌ عَمياء: {injects} سَطرَ حَقنٍ فَقَط — والمَقيسُ ‏413.");
        Assert.True(dead.Count == 0,
            "صَفحَةٌ تَحقِنُ مَخزَنَ Marten أَو جَلسَتَه ولا تَقرَؤُه — مَدخَلٌ إلى جَلسَةٍ بِلا مُستَهلِك (القاعِدَة ١)؛ اِحذِفه:\n  "
            + string.Join("\n  ", dead));
    }

    /// <summary><b>حَقنُ عَيب</b>: حَقنٌ بِاسمِه وبِبادِئَةِ مَجالِه ولِلجَلسَتَينِ لا يُقرَأُ يُرى — ولَو ذُكِرَ اسمُه في تَعليقٍ أَو عُضواً لِغَيرِه
    /// (<c>x.Store</c>)؛ وحَقنٌ يُمَرَّرُ إلى حارِسٍ لا يُرى، ونَوعٌ يَبدَأُ بِالاسمِ وحَقنٌ في تَعليقٍ وخِدمَةٌ لَيسَت مَخزَناً.</summary>
    [Fact]
    public void The_Marten_injection_scanner_can_actually_go_red()
    {
        static int Dead(string page) => MartenInjections(page).Count(i => !i.Read);
        var red = new[]
        {
            "@inject IDocumentStore Store\n<p>صَفحَة</p>",
            "@inject Marten.IDocumentStore Docs\n@code { // Docs.QuerySession(slug)\n }",
            "@inject IQuerySession Query\n@* Query.Load<X>(id) *@",
            "  @inject IDocumentSession Session\r\n@code { var n = other.Session; }",
        };
        var green = new[]
        {
            "@inject Marten.IDocumentStore Store\n@code { var ok = await PlatformAdminGuard.EvaluateAsync(Store, StudioAuth); }",
            "@inject IDocumentStoreFacade Store",
            "@* @inject IDocumentStore Store *@",
            "<!-- @inject IDocumentStore Store -->",
            "@inject Services.Deals.DealsService Deals",
        };
        var wrong = red.Where(p => Dead(p) != 1).Select(p => "لَم يُرَ: " + p)
            .Concat(green.Where(p => Dead(p) != 0).Select(p => "رُئِيَ: " + p)).ToList();
        output.WriteLine($"حُقِنَ {red.Length} حَقناً لا يُقرَأ و{green.Length} سَليماً · أَخطَأَ الفاحِصُ في {wrong.Count}");
        Assert.True(wrong.Count == 0, string.Join("\n", wrong));
        // ‏والحَيُّ قَبلَ هذا البَندِ بِحَرفِه (رَأسُ `MyDeals.razor` عِندَ `b2fde50`، و`grep -cw Store` فيه ‏1):
        Assert.Equal(1, Dead("@page \"/{slug}/deals\"\n@page \"/{slug}/r/{role}/deals\"\n@inject IDocumentStore Store\n@inject AuthSession Auth\n"));
    }

    // ─── (٢) كُلُّ cref في صَفحَةٍ يُسَمّي ما هُوَ قائِم ─────────────────────────────────────────────────

    private static readonly Regex Cref = new(@"cref=""(?<c>[^""]+)""");

    /// <summary>أَنواعُ تَجميعاتِ <c>ACommerce*</c> بِجِوارِ الاختِبارِ عَدا تَجميعِه — نَسَقُ <c>MartenSchemaVerbTests</c>؛ وما لا يُحَمَّلُ يُعَدّ.</summary>
    private static (IReadOnlyList<Type> Types, int Assemblies, int Holes) LoadedTypes()
    {
        var holes = 0;
        var assemblies = Directory.EnumerateFiles(AppContext.BaseDirectory, "ACommerce*.dll")
            .Where(p => Path.GetFileNameWithoutExtension(p) != typeof(RazorPageEntryPointsTests).Assembly.GetName().Name)
            .Select(Assembly.LoadFrom).ToList();
        var types = new List<Type>();
        foreach (var a in assemblies)
        {
            try { types.AddRange(a.GetTypes()); }
            catch (ReflectionTypeLoadException ex) { holes += ex.Types.Count(t => t is null); types.AddRange(ex.Types.OfType<Type>()); }
        }
        return (types, assemblies.Count, holes);
    }

    private const BindingFlags AnyMember = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                                         | BindingFlags.Static | BindingFlags.FlattenHierarchy;

    /// <summary>اسمُ النَوعِ بِنُقَطٍ لا بِـ<c>+</c> المُتَداخِل ولا بِـ<c>`N</c> العامّ.</summary>
    private static string DottedName(Type t) => Regex.Replace((t.FullName ?? t.Name).Replace('+', '.'), @"`\d+", "");

    /// <summary><b>أَيُسَمّي المَرجِعُ ما هُوَ قائِم؟</b> — أَطوَلُ بادِئَةٍ مِن مَقاطِعِه اسمُ نَوعٍ (بِآخِرِ مَقاطِعِ اسمِه الكامِل)، والباقي لا شَيءَ أَو
    /// عُضوٌ واحِدٌ فيه؛ ومَرجِعٌ بِمَقطَعٍ واحِدٍ لا نَوعَ بِه عُضوٌ في صِنفِ الصَفحَةِ نَفسِها (‏<paramref name="page"/>). والتَوقيعُ <c>(…)</c> لا يُقارَن.</summary>
    internal static bool Resolves(string cref, string page, IReadOnlyList<Type> types)
    {
        var name = Regex.Replace(cref, @"\(.*\)$", "");
        name = Regex.Replace(name, @"\{[^{}]*\}", "");
        var parts = name.Split('.');
        for (var k = parts.Length; k >= 1; k--)
        {
            var typeName = string.Join('.', parts[..k]);
            var candidates = types.Where(t => DottedName(t) == typeName || DottedName(t).EndsWith("." + typeName, StringComparison.Ordinal)).ToList();
            if (candidates.Count == 0) continue;
            var rest = parts[k..];
            if (rest.Length == 0) return true;
            if (rest.Length == 1 && candidates.Any(t => t.GetMember(rest[0], AnyMember).Length > 0)) return true;
        }
        if (parts.Length != 1) return false;
        var component = Path.GetFileNameWithoutExtension(page);
        return types.Where(t => t.Name == component).Any(t => t.GetMember(parts[0], AnyMember).Length > 0);
    }

    [Fact]
    public void Every_cref_in_a_razor_page_names_something_that_exists()
    {
        var (types, assemblies, holes) = LoadedTypes();
        var files = RazorFiles();
        int crefs = 0;
        var broken = new List<string>();
        foreach (var f in files)
        {
            var text = File.ReadAllText(f);
            foreach (Match m in Cref.Matches(text))
            {
                crefs++;
                if (!Resolves(m.Groups["c"].Value, f, types)) broken.Add($"{Rel(f)}: cref=\"{m.Groups["c"].Value}\"");
            }
        }
        output.WriteLine($"مَفحوص: {files.Count} صَفحَةَ razor · {crefs} مَرجِعَ cref · {types.Count} نَوعاً مِن {assemblies} تَجميعاً"
            + $" (لا يُحَمَّلُ مِنها {holes}) · لا يُسَمّي قائِماً: {broken.Count}");
        Assert.True(assemblies >= 10 && types.Count >= 1000, $"أَداةٌ عَمياء: {assemblies} تَجميعاً و{types.Count} نَوعاً.");
        Assert.True(crefs >= 30, $"أَداةٌ عَمياء: {crefs} مَرجِعاً فَقَط — والمَقيسُ ‏34.");
        Assert.True(broken.Count == 0,
            "مَرجِعُ cref في صَفحَةٍ يُسَمّي ما لا وُجودَ لَه — والمُتَرجِمُ لا يَفحَصُه (لا GenerateDocumentationFile):\n  " + string.Join("\n  ", broken));
    }

    /// <summary><b>حَقنُ عَيب</b>: المَرجِعُ الَّذي كانَ في <c>MyDealDetail.razor</c> لا يُحَلّ، وبَديلُه يُحَلّ؛ ونَوعٌ وَحدَه، وبِمَجالٍ جُزئِيّ، وبِتَوقيع،
    /// وعُضوٌ في صِنفِ الصَفحَةِ نَفسِها يُحَلّ؛ واسمُ نَوعٍ مُختَلَق، وعُضوٌ مُختَلَقٌ في صِنفِ الصَفحَة، لا يُحَلّان.</summary>
    [Fact]
    public void The_cref_resolver_can_actually_go_red()
    {
        var (types, _, _) = LoadedTypes();
        const string Detail = "MyDealDetail.razor", Listings = "MyListings.razor";
        var resolves = new (string Cref, string Page)[]
        {
            ("DealShipping.CarrierReadyForViewerAsync", Detail),
            ("DealShipping", Detail),
            ("Services.PlatformAdminGuard.Decide", "StudioHome.razor"),
            ("Services.Deals.DealShipping.LinksOf", Detail),
            ("ProviderBindSurface.FromForm(string, Func{string, string?}, string, ProviderVault?)", Detail),
            ("ShippingErrKey", Listings),
        };
        var fails = new (string Cref, string Page)[]
        {
            ("DealShipping.CarrierReady", Detail),
            ("DealShippingX.CarrierReadyForViewerAsync", Detail),
            ("NoSuchMemberOnThePage", Listings),
        };
        var wrong = resolves.Where(r => !Resolves(r.Cref, r.Page, types)).Select(r => "لَم يُحَلّ: " + r.Cref)
            .Concat(fails.Where(r => Resolves(r.Cref, r.Page, types)).Select(r => "حُلَّ: " + r.Cref)).ToList();
        output.WriteLine($"حُقِنَ {resolves.Length} مَرجِعاً قائِماً و{fails.Length} لا وُجودَ لَه · أَخطَأَ المُحَلِّلُ في {wrong.Count}");
        Assert.True(wrong.Count == 0, string.Join("\n", wrong));
        // ‏والمَحذوفَةُ مَحذوفَةٌ فِعلاً — لا يُحَلُّ المَرجِعُ القَديمُ لِأَنّ المُحَلِّلَ أَعمى:
        Assert.Empty(typeof(DealShipping).GetMember("CarrierReady", AnyMember));
        Assert.NotEmpty(typeof(DealShipping).GetMember("CarrierReadyForViewerAsync", AnyMember));
    }
}
