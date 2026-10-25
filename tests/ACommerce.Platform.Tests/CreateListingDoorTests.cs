using System.Text.RegularExpressions;
using ACommerce.Kit.Roles;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ «إعلان جَديد» بابٌ يُرسَمُ لِدَورٍ لا يَنشُر — والنَموذَجُ يُملَأُ كامِلاً ثُمَّ ‏403 بِلا رِسالَة ═══
//
// **تَدقيقُ الإطلاقِ عِندَ `dace7cd`، تَيّارُ W8-7**: ‏`lens-dead-doors-3` (كُبرى). أُعيدَ قِياسُه عِندَ الرَأسِ `7b8de7f` (‏2026-10-25):
// - ‏`customer.role.json` لا يَحمِلُ `listing.create`، وهُوَ الدَورُ الافتِراضِيُّ لِكُلِّ مَتجَرٍ يَبنيه الاستوديو (‏`TenantFromAnalysisFactory.RolesFor`:
//   ‏`customer` بِـ`isDefault: true` في ثَلاثَةِ أَنماطٍ مِن أَربَعَة).
// - ‏`TenantHome.razor` يَحرُسُ رابِطَيه بِـ`canCreateListing` (‏`RolePermissions.Has(…, "listing.create")`)، و`MyListings.razor` يَرسُمُ رابِطَيه
//   (زِرُّ الرَأسِ وزِرُّ الحالَةِ الفارِغَة) لِكُلِّ صاحِبِ جَلسَة.
// - ‏`CreateListing.razor` يَرسُمُ النَموذَجَ كامِلاً (الفِئَة، الحُقول، الصُوَر، المَوقِع) بِلا سُؤالِ الصَلاحِيَّة، ونُقطَتُه
//   ‏`.RequirePermission("listing.create")` تَرُدُّ ‏403 عارِياً (‏`PermissionFilter.cs:48-49`) فَيَرى المُشتَري صَفحَةَ الخَطَأِ العامَّة.
// - وفَرعُ `Err == "forbidden"` في الصَفحَةِ لا كاتِبَ لَه: صِفرُ `create-listing?err=forbidden` في `.cs`.
//
// هذا المِلَفُّ يُوَصِّفُ «اليَوم» (القاعِدَة ٣) قَبلَ أَيِّ تَبديل؛ صُفوفُ `Today_` تُقلَبُ بِأَسمائِها في كوميتِ الإصلاح، والباقي يَبقى بِلا تَعديلِ حَرف.
public class CreateListingDoorTests(ITestOutputHelper output)
{
    private const string TemplateRoot = "libs/templates/ACommerce.Templates.Customer.Marketplace";
    private const string Permission = "listing.create";

    // ─── الماسِح ────────────────────────────────────────────────────────────────────────────

    /// <summary>ذِكرٌ لِصَفحَةِ النَشرِ في رَابِطٍ أَو استِمارَة: <c>create-listing</c> يَسبِقُه تَنصيصٌ أَو <c>/</c> ولا يَليه حَرفٌ أَو شَرطَة —
    /// فَـ<c>acs-create-listing</c> (صِنفُ CSS) لا يُعَدّ، و<c>create-listing?category=…</c> يُعَدّ.</summary>
    private static readonly Regex CreateListingToken = new(@"(?<=[""'/])create-listing(?![\w-])", RegexOptions.Compiled);

    /// <summary>حُكمُ رابِطٍ واحِد: <c>Predicate</c> داخِلَ <c>if</c> أَحَدُ شُقوقِ <c>&amp;&amp;</c> العُليا في شَرطِه مُتَغَيِّرٌ أَو نِداءٌ يَسأَلُ
    /// <c>listing.create</c>؛ و<c>Page</c> داخِلَ <c>&lt;GatedPage … Permission="listing.create"&gt;</c> في صَفحَةِ النَشرِ نَفسِها؛ و<c>RoleHome</c>
    /// في مِلَفٍّ مُسَمّىً بِسَبَبِه (<see cref="RoleHomeFiles"/>)؛ و<c>Outside</c> ما سِواه.</summary>
    internal enum Door { Predicate, Page, RoleHome, Outside }

    internal sealed record Link(string File, int Line, Door Door);

    /// <summary><para><b>مَلَفّاتُ أَبوابِ الدَور، مُسَمّاةً بِسَبَبِها</b> — رابِطٌ فيها لا يَسأَلُ الصَلاحِيَّةَ بِحَرفِها بَل يُرسَمُ لِأَنّ تَركيبَ الدَور
    /// اختارَه، و<see cref="Role_home_doors_to_create_listing_are_drawn_only_for_catalog_roles_that_carry_listing_create"/> يُثبِتُ أَنّ كُلَّ دَورٍ
    /// في الكاتالوجِ يَختارُها يَحمِلُ <c>listing.create</c>. والقائِمَةُ تَتَقَلَّصُ ولا تَنمو إلّا بِقَرارٍ مَرئِيّ.</para></summary>
    internal static readonly IReadOnlyDictionary<string, string> RoleHomeFiles = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [TemplateRoot + "/Components/RoleHomes/SellerHome.razor"] = "تَركيبُ home = sellerHome",
        [TemplateRoot + "/Components/RoleHomes/RiderHome.razor"]  = "تَركيبُ home = riderHome",
        [TemplateRoot + "/Components/RoleHomeHero.razor"]         = "فَرعا rider وvendor/host في switch عَلى CatalogSlug",
        [TemplateRoot + "/Components/Pages/TenantManage.razor"]   = "خَلفَ canManage (‏tenant.manage)",
    };

    private const string PageFile = TemplateRoot + "/Components/Pages/CreateListing.razor";

    /// <summary>كُلُّ رابِطٍ إلى صَفحَةِ النَشرِ في مَصادِرِ <c>.razor</c> تَحتَ <c>libs</c>/<c>apps</c> (‏<see cref="EntitlementContractTests.SourceFiles"/>)،
    /// خارِجَ تَوجيهِ <c>@page</c> وخارِجَ تَعليقاتِ Razor/HTML. وتَحويلاتُ النُقطَةِ (<c>create-listing?err=…</c>) في <c>.cs</c> خارِجَ الماسِح:
    /// تَقَعُ بَعدَ <c>.RequirePermission("listing.create")</c> فَلا يَبلُغُها إلّا مَن عَبَرَ الحارِس.</summary>
    internal static List<Link> CreateListingLinksIn(IEnumerable<(string File, string Text)> sources)
    {
        var links = new List<Link>();
        foreach (var (file, text) in sources)
        {
            var rel = Path.IsPathRooted(file) ? Path.GetRelativePath(ThemeZeroEquivalenceTests.RepoRoot, file).Replace('\\', '/') : file;
            if (!rel.EndsWith(".razor", StringComparison.OrdinalIgnoreCase)) continue;
            var code = WriteEndpointGuardTests.StripMarkupComments(text);
            if (!CreateListingToken.IsMatch(code)) continue;

            var doorVars = DoorVariables(code);
            var gated = rel == PageFile ? GatedSpans(code) : new List<(int, int)>();
            foreach (Match m in CreateListingToken.Matches(code))
            {
                var lineStart = code.LastIndexOf('\n', Math.Max(0, m.Index - 1)) + 1;
                if (code.AsSpan(lineStart).TrimStart().StartsWith("@page", StringComparison.Ordinal)) continue;
                var line = code[..m.Index].Count(c => c == '\n') + 1;

                Door door;
                if (gated.Any(g => g.Item1 < m.Index && m.Index < g.Item2)) door = Door.Page;
                else if (UpgradePromptPriceTests.EnclosingIfConditions(code, m.Index).Any(c => AsksPermission(c, doorVars))) door = Door.Predicate;
                else if (RoleHomeFiles.ContainsKey(rel)) door = Door.RoleHome;
                else door = Door.Outside;
                links.Add(new Link(rel, line, door));
            }
        }
        return links;
    }

    internal static List<Link> CreateListingLinks() => CreateListingLinksIn(EntitlementContractTests.SourceFiles());

    /// <summary>نِداءٌ يَسأَلُ الصَلاحِيَّةَ بِحَرفِها — <c>RolePermissions.Has(…, "listing.create")</c> أَو <c>PermissionDoor.Opens(…, "listing.create")</c>.</summary>
    private static readonly Regex PermissionCall = new(
        @"(?:[\w.]*\.)?(?:RolePermissions\.Has|PermissionDoor\.Opens)\s*\([^;]*?""listing\.create""\s*\)", RegexOptions.Compiled | RegexOptions.Singleline);

    /// <summary>مُتَغَيِّراتُ البابِ في المِلَفّ: كُلُّ <c>x = …(… "listing.create")</c> بِأَحَدِ النِداءَين.</summary>
    private static HashSet<string> DoorVariables(string code)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match m in Regex.Matches(code, @"(?<var>[A-Za-z_]\w*)\s*=\s*(?<call>(?:[\w.]*\.)?(?:RolePermissions\.Has|PermissionDoor\.Opens)\s*\([^;]*?""listing\.create""\s*\))",
                     RegexOptions.Singleline))
            set.Add(m.Groups["var"].Value);
        return set;
    }

    /// <summary>أَحَدُ شُقوقِ <c>&amp;&amp;</c> العُليا في الشَرطِ مُتَغَيِّرُ بابٍ بِحَرفِه أَو نِداءٌ يَسأَلُ الصَلاحِيَّة — لا نَفيُه ولا شِقٌّ في <c>||</c>.</summary>
    private static bool AsksPermission(string condition, HashSet<string> doorVars)
    {
        var parts = new List<string>();
        var depth = 0;
        var last = 0;
        for (var k = 0; k < condition.Length; k++)
        {
            var c = condition[k];
            if (c is '(' or '[' or '{') depth++;
            else if (c is ')' or ']' or '}') depth--;
            else if (depth == 0 && c == '|' && k + 1 < condition.Length && condition[k + 1] == '|') return false;
            else if (depth == 0 && c == '?') return false;
            else if (depth == 0 && c == '&' && k + 1 < condition.Length && condition[k + 1] == '&')
            {
                parts.Add(condition[last..k]);
                last = k + 2;
                k++;
            }
        }
        parts.Add(condition[last..]);
        return parts.Select(p => p.Trim()).Any(p => doorVars.Contains(p) || PermissionCall.Match(p) is { Success: true } m && m.Length == p.Length);
    }

    /// <summary>كُتَلُ <c>&lt;GatedPage … Permission="listing.create" …&gt;</c> حَتّى <c>&lt;/GatedPage&gt;</c> المُقابِل.</summary>
    private static List<(int, int)> GatedSpans(string code)
    {
        var spans = new List<(int, int)>();
        foreach (Match m in Regex.Matches(code, @"<GatedPage\b[^>]*\bPermission=""listing\.create""[^>]*>"))
        {
            var close = code.IndexOf("</GatedPage>", m.Index, StringComparison.Ordinal);
            Assert.True(close > m.Index, "GatedPage بِلا إغلاق — القَصُّ فاشِل.");
            spans.Add((m.Index + m.Length, close));
        }
        return spans;
    }

    private static string Read(string relative)
    {
        var path = Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, relative.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(path), $"مَصدَرٌ مَفقود: {relative} — الأَداةُ عَمياءُ بِلا طَرَفٍ مَقروء.");
        return File.ReadAllText(path);
    }

    private static string Summary(IEnumerable<Link> links)
        => string.Join(" · ", links.GroupBy(l => l.Door).OrderBy(g => g.Key).Select(g => $"{g.Key}: {g.Count()}"));

    // ─── «اليَوم» ───────────────────────────────────────────────────────────────────────────

    /// <summary><b>قُلِبَ «Today_» بِاسمِه (‏2026-10-25، كوميتُ إصلاحِ <c>lens-dead-doors-3</c>)</b> — كانَ
    /// <c>Today_my_listings_and_the_page_itself_draw_six_create_listing_links_outside_the_listing_create_predicate</c> (‏<c>bde9b98</c>): ‏13 رابِطاً
    /// في ‏7 مِلَفّات، سِتَّةٌ مِنها خارِجَ كُلِّ باب. والآن زِرّا «إعلاناتي» خَلفَ <c>canCreateListing</c> (‏<c>PermissionDoor.Opens(…, "listing.create")</c>)،
    /// وروابِطُ صَفحَةِ النَشرِ الأَربَعَةُ داخِلَ <c>&lt;GatedPage … Permission="listing.create"&gt;</c> — <b>صِفرٌ خارِجَ باب</b>، بِالماسِحِ لا بِقائِمَة،
    /// فَرابِطٌ يُضافُ غَداً خارِجَ البابِ يُحمِرُّ هُنا بِمِلَفِّه وسَطرِه. ويَطبَعُ ما عَدَّه ويَفشَلُ دونَ المَقيس (القاعِدَة ١٠).</summary>
    [Fact]
    public void Every_create_listing_link_sits_behind_the_listing_create_predicate_the_page_gate_or_a_named_role_home()
    {
        var links = CreateListingLinks();
        var outside = links.Where(l => l.Door == Door.Outside).ToList();
        output.WriteLine($"روابِطُ create-listing: {links.Count} في {links.Select(l => l.File).Distinct().Count()} مِلَفّات · {Summary(links)} · خارِجَ بابٍ: "
            + (outside.Count == 0 ? "لا شَيء" : string.Join(" · ", outside.Select(l => $"{Path.GetFileName(l.File)}:{l.Line}"))));

        Assert.True(links.Count >= 13, $"أَداةٌ عَمياء: {links.Count} رابِطاً — والمَقيسُ 13.");
        Assert.True(outside.Count == 0, "رابِطٌ إلى صَفحَةِ النَشرِ خارِجَ بابِها:\n  " + string.Join("\n  ", outside.Select(l => $"{l.File}:{l.Line}"))
            + "\nضَعه داخِلَ @if (…) شِقٌّ فيه مُتَغَيِّرٌ مِن PermissionDoor.Opens(…, \"listing.create\").");
        Assert.Equal(new[] { Door.Predicate, Door.Predicate },
            links.Where(l => l.File.EndsWith("/Components/Pages/MyListings.razor", StringComparison.Ordinal)).Select(l => l.Door).ToArray());
        Assert.Equal(4, links.Count(l => l.File == PageFile && l.Door == Door.Page));
    }

    /// <summary><b>قُلِبَ «Today_» بِاسمِه (‏2026-10-25، كوميتُ إصلاحِ <c>lens-dead-doors-3</c>)</b> — كانَ
    /// <c>Today_the_create_listing_page_draws_its_form_without_asking_listing_create_and_its_forbidden_branch_has_no_writer</c> (‏<c>bde9b98</c>).
    /// والآن تَرميزُ الصَفحَةِ كُلُّه داخِلَ <c>GatedPage</c> واحِدٍ بِـ<c>Permission="listing.create"</c> ونَصِّ رَفضِها
    /// <c>listings.create.err_forbidden</c>، و<c>GatedPage</c> يَسأَلُ <c>PermissionDoor.Opens</c> لا <c>RolePermissions.Has</c> بِيَدِه؛ وفَرعُ
    /// <c>Err == "forbidden"</c> الَّذي لا كاتِبَ لَه حُذِف. والنُقطَةُ كَما هِيَ: ‏403 عارٍ لِطَلَبٍ مَصنوعٍ لا يَبلُغُه نَقر.</summary>
    [Fact]
    public void The_create_listing_page_asks_listing_create_through_GatedPage_before_its_form_and_keeps_no_branch_without_a_writer()
    {
        var page = WriteEndpointGuardTests.StripMarkupComments(Read(PageFile));
        var writers = EntitlementContractTests.SourceFiles()
            .Where(f => f.File.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            .Count(f => WriteEndpointGuardTests.StripComments(f.Text).Contains("create-listing?err=forbidden", StringComparison.Ordinal));
        var filter = WriteEndpointGuardTests.StripComments(Read(TemplateRoot + "/Gates/PermissionFilter.cs"));
        var gate = WriteEndpointGuardTests.StripMarkupComments(Read(TemplateRoot + "/Components/GatedPage.razor"));
        const string Open = "<GatedPage Slug=\"@Slug\" Role=\"@Role\" Permission=\"listing.create\" ForbiddenMessageKey=\"listings.create.err_forbidden\">";
        var markup = page[..page.IndexOf("@code {", StringComparison.Ordinal)];
        var body = markup[(markup.IndexOf("@inject L L", StringComparison.Ordinal) + "@inject L L".Length)..].Trim();
        output.WriteLine($"<GatedPage في صَفحَةِ النَشر: {Regex.Matches(page, "<GatedPage").Count} · أَوَّلُ التَرميزِ بابٌ: {body.StartsWith(Open, StringComparison.Ordinal)} · "
            + $"آخِرُه إغلاقُه: {body.EndsWith("</GatedPage>", StringComparison.Ordinal)} · فَرعُ forbidden: {page.Contains("Err == \"forbidden\"", StringComparison.Ordinal)} · "
            + $"كُتّابُ create-listing?err=forbidden: {writers}");

        Assert.Single(Regex.Matches(page, "<GatedPage"));
        Assert.StartsWith(Open, body, StringComparison.Ordinal);
        Assert.EndsWith("</GatedPage>", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Err == \"forbidden\"", page, StringComparison.Ordinal);
        Assert.Equal(0, writers);
        Assert.Contains("return Results.StatusCode(StatusCodes.Status403Forbidden);", filter, StringComparison.Ordinal);
        Assert.NotNull(ACommerce.Platform.I18n.LocaleCatalog.Find(ACommerce.Platform.I18n.LocaleCatalog.Arabic, "listings.create.err_forbidden"));

        Assert.Contains("if (!PermissionDoor.Opens(tenant.Roles, Role, storedRole, Permission))", gate, StringComparison.Ordinal);
        Assert.DoesNotContain("RolePermissions.Has(", WriteEndpointGuardTests.StripComments(gate), StringComparison.Ordinal);
        Assert.Contains("if (PermissionDoor.SwitchOpens(tenant.Roles, Role, Permission))", gate, StringComparison.Ordinal);
    }

    private const string RiderFile = TemplateRoot + "/Components/RoleHomes/RiderCreateRequest.razor";

    /// <summary>كُتّابُ <c>create-listing?err=forbidden</c> في كُلِّ مَصدَرٍ <c>.cs</c>/<c>.razor</c> خارِجَ التَعليقات — الطَريقُ الوَحيدُ إلى فَرعِ
    /// «forbidden» في صَفحَةِ النَشرِ وفي استِمارَةِ المِشوارِ الَّتي تُمَرِّرُ لَها <c>Err</c>.</summary>
    private static int CreateListingForbiddenWriters()
        => EntitlementContractTests.SourceFiles()
            .Where(f => f.File.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) || f.File.EndsWith(".razor", StringComparison.OrdinalIgnoreCase))
            .Count(f => WriteEndpointGuardTests.StripComments(WriteEndpointGuardTests.StripMarkupComments(f.Text))
                .Contains("create-listing?err=forbidden", StringComparison.Ordinal));

    /// <summary><b>«اليَوم» (‏2026-10-25، مُراجَعَةُ تَيّارِ W8-7 الجَولَة ١ — صُغرى)</b>: حُذِفَ فَرعُ <c>Err == "forbidden"</c> مِن صَفحَةِ النَشرِ
    /// لِأَنَّ لا كاتِبَ لَه، واستِمارَةُ المِشوارِ الَّتي يُصَيِّرُها المُوَزِّعُ نَفسُه (<c>Err="@Err"</c> يَمُرُّ إلَيها) تَحمِلُ الفَرعَ بِعَينِه ونَصَّه
    /// <c>rider.request.err_forbidden</c> — وصِفرُ كاتِبٍ لَه في <c>.cs</c> و<c>.razor</c>. فَالصَفحَةُ الواحِدَةُ تُعامِلُ الفَرعَينِ مُعامَلَتَين.
    /// يُقلَبُ بِاسمِه في كوميتِ الحَذف.</summary>
    [Fact]
    public void Today_the_rider_request_form_keeps_a_forbidden_branch_that_no_code_writes()
    {
        var rider = WriteEndpointGuardTests.StripMarkupComments(Read(RiderFile));
        var dispatcher = WriteEndpointGuardTests.StripMarkupComments(Read(PageFile));
        var writers = CreateListingForbiddenWriters();
        output.WriteLine($"فَرعُ forbidden في استِمارَةِ المِشوار: {rider.Contains("Err == \"forbidden\"", StringComparison.Ordinal)} · "
            + $"في صَفحَةِ النَشر: {dispatcher.Contains("Err == \"forbidden\"", StringComparison.Ordinal)} · كُتّابُ create-listing?err=forbidden: {writers}");

        Assert.Contains("else if (Err == \"forbidden\")", rider, StringComparison.Ordinal);
        Assert.Contains("L[\"rider.request.err_forbidden\"]", rider, StringComparison.Ordinal);
        Assert.NotNull(ACommerce.Platform.I18n.LocaleCatalog.Find(ACommerce.Platform.I18n.LocaleCatalog.Arabic, "rider.request.err_forbidden"));
        Assert.Contains("Slug=\"@Slug\" Tenant=\"tenant\" Err=\"@Err\" />", dispatcher, StringComparison.Ordinal);
        Assert.DoesNotContain("Err == \"forbidden\"", dispatcher, StringComparison.Ordinal);
        Assert.Equal(0, writers);
    }

    // ─── البابُ نَفسُه: دالَّةٌ واحِدَةٌ لِلرابِطِ والصَفحَة ────────────────────────────────────────

    private static Role R(string slug, params string[] permissions)
        => new() { Slug = slug, CatalogSlug = slug, Permissions = permissions.ToList() };

    private static readonly IReadOnlyList<Role> Store = new[]
    {
        R("customer", "listing.browse", "offer.submit"),
        R("vendor", "listing.create", "listing.edit"),
        R("tenant_admin", "listing.create", "tenant.manage"),
    };

    /// <summary><b>جَدوَلُ البابِ مَوجِباً وسالِباً</b> — ويُقارَنُ كُلُّ صَفٍّ بِقَرارِ <c>GatedPage</c> قَبلَ الاستِخراجِ بِحَرفِه
    /// (‏<c>RolePermissions.Has(roles, Role ?? storedRole ?? "", permission)</c>): الجَوابُ واحِدٌ لِكُلِّ دَورِ مَسارٍ يولَدُ مِن التَوجيه.</summary>
    [Theory]
    [InlineData(null, "customer", false)]   // ‏المُشتَري بِدَورِه المُخَزَّن — مُغلَق
    [InlineData(null, "vendor", true)]      // ‏البائِع — مَفتوح
    [InlineData("vendor", "customer", true)] // ‏دَورُ المَسارِ يَتَفَوَّق
    [InlineData("customer", "vendor", false)]
    [InlineData(null, null, false)]         // ‏بِلا دَور — مُغلَق
    [InlineData(null, "", false)]
    [InlineData("ghost", "vendor", false)]  // ‏دَورٌ لا يَعرِفُه المَتجَر
    [InlineData(null, "tenant_admin", true)]
    public void The_door_opens_for_a_role_carrying_the_permission_exactly_as_the_page_gate_decided(string? urlRole, string? stored, bool opens)
    {
        Assert.Equal(opens, ACommerce.Templates.Customer.Marketplace.Gates.PermissionDoor.Opens(Store, urlRole, stored, Permission));
        Assert.Equal(RolePermissions.Has(Store, urlRole ?? stored ?? "", Permission),
            ACommerce.Templates.Customer.Marketplace.Gates.PermissionDoor.Opens(Store, urlRole, stored, Permission));
        // ‏ومَتجَرٌ بِلا أَدوارٍ مَفتوحٌ لِكُلِّ دَور — الوَضعُ المَوروثُ كَما يَفتَحُه PermissionFilter.
        Assert.True(ACommerce.Templates.Customer.Marketplace.Gates.PermissionDoor.Opens(Array.Empty<Role>(), urlRole, stored, Permission));
    }

    /// <summary><b>مَخرَجُ «اِختَر دَوراً» مَوجِباً وسالِباً</b>: يُفتَحُ حينَ تَعرِضُ صَفحَةُ الأَدوارِ (‏<c>SelfGrantPolicy.SelfGrantable</c>) دَوراً
    /// يَحمِلُ الصَلاحِيَّة وَحدَه — لا حينَ يَحمِلُها الإداريُّ وَحدَه (لا يُمنَحُ ذاتِيّاً)، ولا داخِلَ <c>/r/{role}/</c> (دَورُ المَسارِ يَتَفَوَّق).</summary>
    [Fact]
    public void The_switch_exit_opens_only_when_the_role_picker_offers_a_role_carrying_the_permission_outside_a_role_app()
    {
        var door = typeof(ACommerce.Templates.Customer.Marketplace.Gates.PermissionDoor);
        Assert.True(ACommerce.Templates.Customer.Marketplace.Gates.PermissionDoor.SwitchOpens(Store, null, Permission));
        Assert.False(ACommerce.Templates.Customer.Marketplace.Gates.PermissionDoor.SwitchOpens(Store, "customer", Permission));
        Assert.False(ACommerce.Templates.Customer.Marketplace.Gates.PermissionDoor.SwitchOpens(Store, null, "tenant.manage"));
        Assert.False(ACommerce.Templates.Customer.Marketplace.Gates.PermissionDoor.SwitchOpens(
            new[] { R("customer", "offer.submit"), R("tenant_admin", "listing.create") }, null, Permission));
        Assert.False(ACommerce.Templates.Customer.Marketplace.Gates.PermissionDoor.SwitchOpens(Array.Empty<Role>(), null, Permission));
        Assert.True(door.IsAbstract && door.IsSealed, "PermissionDoor صِنفٌ ساكِنٌ نَقِيّ.");
    }

    /// <summary><b>«إعلاناتي» تَسأَلُ البابَ بِقِراءَتَي <c>GatedPage</c> نَفسَيهِما</b> — المُستَأجِرُ المُخَزَّنُ (<c>TenantDirectory.FindAsync</c>) والدَورُ
    /// المُخَزَّنُ (<c>AccountQueries.ActiveRoleAsync</c>) — ورابِطا <c>me/role</c> فيها خَلفَ <c>canSwitchToCreate</c> وَحدَه؛ والبابُ لا يُسأَلُ في الواجِهَةِ إلّا
    /// مِن مُستَهلِكيه المُسَمَّين (شَرطُ الاستِخراجِ في رَأسِ <c>PermissionDoor.cs</c>).</summary>
    [Fact]
    public void My_listings_asks_the_door_with_the_page_gates_own_reads_and_draws_the_switch_exit_only_behind_it()
    {
        var mine = WriteEndpointGuardTests.StripMarkupComments(Read(TemplateRoot + "/Components/Pages/MyListings.razor"));
        var gate = WriteEndpointGuardTests.StripMarkupComments(Read(TemplateRoot + "/Components/GatedPage.razor"));
        Assert.Contains("var roles = (await Tenants.FindAsync(Slug))?.Roles", mine, StringComparison.Ordinal);
        Assert.Contains("var tenant = await Tenants.FindAsync(Slug);", gate, StringComparison.Ordinal);
        Assert.Contains("var storedRole = await Accounts.ActiveRoleAsync(Slug, Auth.UserId!.Value);", mine, StringComparison.Ordinal);
        Assert.Contains("var storedRole = await Accounts.ActiveRoleAsync(Slug, uid2);", gate, StringComparison.Ordinal);
        Assert.Contains("canCreateListing = ACommerce.Templates.Customer.Marketplace.Gates.PermissionDoor.Opens(roles, Role, storedRole, \"listing.create\");", mine, StringComparison.Ordinal);

        var switches = Regex.Matches(mine, Regex.Escape("\"me/role\"")).Select(m => m.Index).ToList();
        var behind = switches.Count(i => UpgradePromptPriceTests.EnclosingIfConditions(mine, i).Any(c => c.Contains("canSwitchToCreate", StringComparison.Ordinal))
                                         || mine.LastIndexOf("canSwitchToCreate ?", i, StringComparison.Ordinal) is var t && t > 0 && i - t < 120);
        var consumers = EntitlementContractTests.SourceFiles()
            .Where(f => !f.File.Replace('\\', '/').EndsWith("/Gates/PermissionDoor.cs", StringComparison.Ordinal))
            .Where(f => WriteEndpointGuardTests.StripComments(WriteEndpointGuardTests.StripMarkupComments(f.Text)).Contains("PermissionDoor.", StringComparison.Ordinal))
            .Select(f => Path.GetFileName(f.File)).OrderBy(f => f, StringComparer.Ordinal).ToArray();
        output.WriteLine($"روابِطُ me/role في «إعلاناتي»: {switches.Count} · خَلفَ canSwitchToCreate: {behind} · مُستَهلِكو PermissionDoor: {string.Join(", ", consumers)}");

        Assert.Equal(2, switches.Count);
        Assert.Equal(switches.Count, behind);
        // ‏(‏2026-10-25، مُراجَعَةُ تَيّارِ W8-7 الجَولَة ١): قائِمَةُ المُستَهلِكينَ انتَقَلَت إلى صَفِّ «اليَوم» لِلرَئيسِيَّة (<see cref="PermissionDoorConsumers"/>) —
        // فَهِيَ ما يَتَبَدَّلُ بِتَبديلِ الرَئيسِيَّة، فَيُقلَبُ صَفُّ «اليَوم» بِاسمِه ولا يُمَسُّ هذا الصَفّ.
        Assert.NotEmpty(consumers);
    }

    // ─── ما لا يَتَبَدَّل ───────────────────────────────────────────────────────────────────

    /// <summary>الرَئيسِيَّةُ تَرسُمُ رابِطَيها (البِطاقَةُ والحالَةُ الفارِغَة) خَلفَ <c>canCreateListing</c> وَحدَه، وهُوَ نِداءُ الصَلاحِيَّةِ بِحَرفِها.</summary>
    [Fact]
    public void The_home_page_draws_both_its_create_listing_links_only_behind_the_listing_create_predicate()
    {
        var home = CreateListingLinks().Where(l => l.File.EndsWith("/Components/Pages/TenantHome.razor", StringComparison.Ordinal)).ToList();
        output.WriteLine($"روابِطُ TenantHome: {home.Count} · {Summary(home)}");
        Assert.Equal(2, home.Count);
        Assert.All(home, l => Assert.Equal(Door.Predicate, l.Door));
        // ‏(‏2026-10-25، مُراجَعَةُ تَيّارِ W8-7 الجَولَة ١): بَصمَةُ نِداءِ الصَلاحِيَّةِ انتَقَلَت إلى صَفِّ «اليَوم» أَدناه — هِيَ ما يَتَبَدَّل.
    }

    private const string HomeFile = TemplateRoot + "/Components/Pages/TenantHome.razor";

    /// <summary>مُستَهلِكو <c>PermissionDoor</c> في المَصادِر — كُلُّ مِلَفٍّ غَيرِ <c>PermissionDoor.cs</c> يَذكُرُ <c>PermissionDoor.</c> خارِجَ التَعليقات.</summary>
    private static string[] PermissionDoorConsumers()
        => EntitlementContractTests.SourceFiles()
            .Where(f => !f.File.Replace('\\', '/').EndsWith("/Gates/PermissionDoor.cs", StringComparison.Ordinal))
            .Where(f => WriteEndpointGuardTests.StripComments(WriteEndpointGuardTests.StripMarkupComments(f.Text)).Contains("PermissionDoor.", StringComparison.Ordinal))
            .Select(f => Path.GetFileName(f.File)).OrderBy(f => f, StringComparer.Ordinal).ToArray();

    /// <summary>دَورٌ مُؤَلَّفٌ مُعتَمَدٌ يَحمِلُ <c>listing.create</c> — تَعريفُ «خَيّاط» نَفسُه (<see cref="TenantRoleDefinitionToolTests.KhayyatJson"/>) —
    /// مُجَسَّداً فَوقَ أَدوارِ <see cref="Store"/> المُخَزَّنَةِ كَما يُجَسِّدُه <c>TenantRoleService.LoadAsync</c>.</summary>
    private static IReadOnlyList<Role> MaterializedWithAuthoredMaker()
        => TenantRoleSet.FromDocuments("adwar-demo", new[]
        {
            new TenantRoleDefinition
            {
                Id = "khayyat", Slug = "khayyat", DefinitionJson = TenantRoleDefinitionToolTests.KhayyatJson,
                Status = TenantRoleStatuses.Approved, CreatedBy = "agent:define_role",
                CreatedAt = new DateTime(2026, 8, 10, 0, 0, 0, DateTimeKind.Utc),
            },
        }).Materialize(Store);

    /// <summary><b>قُلِبَ «Today_» بِاسمِه (‏2026-10-25، كوميتُ التَبديل؛ مُراجَعَةُ تَيّارِ W8-7 الجَولَة ١ — صُغرى)</b> — كانَ
    /// <c>Today_the_home_page_asks_listing_create_on_the_materialized_roles_so_an_authored_role_sees_a_link_its_page_refuses</c> (‏<c>57af82e</c>):
    /// <c>RolePermissions.Has(loaded.Roles, …)</c> عَلى الأَدوارِ المُجَسَّدَة، فَرَسَمَت الرَئيسِيَّةُ لِـ«خَيّاط» المُؤَلَّفِ بابًا تَرُدُّه صَفحَتُه. والآن
    /// تَسأَلُ <c>PermissionDoor.Opens</c> بِقِراءَتَي حارِسِ الهَدَف: <c>Tenant.Roles</c> المُخَزَّنَة (<c>TenantDirectory.FindAsync</c> — قِراءَةُ
    /// <c>GatedPage</c> بِحَرفِها) والدَورُ المُخَزَّن (<c>home.StoredActiveRole</c> — <c>User.ActiveRole</c> في جَلسَةِ المُستَأجِرِ كَما يَقرَؤُه
    /// <c>AccountQueries.ActiveRoleAsync</c>، و«""» عِندَ <c>EffectiveRole.Resolve</c> كَـ<c>null</c>). فَالرابِطُ والصَفحَةُ جَوابٌ واحِدٌ، والرَئيسِيَّةُ
    /// المُستَهلِكُ الرابِعُ لِلباب.</summary>
    [Fact]
    public void The_home_page_asks_the_door_with_its_target_gates_stored_reads_so_an_authored_role_sees_no_link_its_page_refuses()
    {
        var home = WriteEndpointGuardTests.StripMarkupComments(Read(HomeFile)).Replace("\r\n", "\n", StringComparison.Ordinal);
        var code = WriteEndpointGuardTests.StripComments(home);
        var gate = WriteEndpointGuardTests.StripMarkupComments(Read(TemplateRoot + "/Components/GatedPage.razor"));
        var storefront = WriteEndpointGuardTests.StripComments(Read(TemplateRoot + "/Services/Queries/StorefrontQueries.cs"));
        var accounts = WriteEndpointGuardTests.StripComments(Read(TemplateRoot + "/Services/Queries/AccountQueries.cs"));
        var materialized = MaterializedWithAuthoredMaker();
        var homeDraws = ACommerce.Templates.Customer.Marketplace.Gates.PermissionDoor.Opens(Store, null, "khayyat", Permission); // ‏تَعبيرُ الرَئيسِيَّةِ الآن
        var pageOpens = ACommerce.Templates.Customer.Marketplace.Gates.PermissionDoor.Opens(Store, null, "khayyat", Permission); // ‏قَرارُ GatedPage
        var consumers = PermissionDoorConsumers();
        output.WriteLine($"أَدوارٌ مُجَسَّدَة: {materialized.Count} ({string.Join(", ", materialized.Select(r => r.Slug))}) · مُخَزَّنَة: {Store.Count} · "
            + $"الرَئيسِيَّةُ تَرسُمُ لِخَيّاط: {homeDraws} · صَفحَةُ النَشرِ تَفتَحُ لَه: {pageOpens} · مُستَهلِكو PermissionDoor: {string.Join(", ", consumers)}");

        Assert.Contains("@inject ACommerce.Templates.Customer.Marketplace.Services.Queries.TenantDirectory Tenants", home, StringComparison.Ordinal);
        Assert.Contains("var storedRoles = (await Tenants.FindAsync(Slug))?.Roles ?? new List<ACommerce.Kit.Roles.Role>();", code, StringComparison.Ordinal);
        Assert.Contains("canCreateListing = ACommerce.Templates.Customer.Marketplace.Gates.PermissionDoor.Opens(storedRoles, Role, home.StoredActiveRole, \"listing.create\");",
            code, StringComparison.Ordinal);
        Assert.DoesNotContain("RolePermissions.Has(", code, StringComparison.Ordinal);
        // ‏القِراءَتانِ هُما قِراءَتا حارِسِ الهَدَفِ بِحَرفِهِما.
        Assert.Contains("var tenant = await Tenants.FindAsync(Slug);", gate, StringComparison.Ordinal);
        Assert.Contains("(await s.LoadAsync<ACommerce.Kit.Auth.User>(uid, ct))?.ActiveRole ?? \"\"", storefront, StringComparison.Ordinal);
        Assert.Contains("return (await s.LoadAsync<User>(userId, ct))?.ActiveRole;", accounts, StringComparison.Ordinal);
        Assert.Equal(ACommerce.Templates.Customer.Marketplace.Gates.PermissionDoor.Opens(Store, null, null, Permission),
            ACommerce.Templates.Customer.Marketplace.Gates.PermissionDoor.Opens(Store, null, "", Permission));

        Assert.Equal(Store.Count + 1, materialized.Count);
        Assert.False(homeDraws, "الرَئيسِيَّةُ لا تَرسُمُ بابًا لا تَفتَحُه صَفحَتُه.");
        Assert.Equal(pageOpens, homeDraws);

        // ‏(‏2026-10-25، W8-7 — كوميتُ إصلاحِ lens-dead-doors-7): المُستَهلِكُ الثالِثُ استِمارَةُ العَرض — مُسَمّىً في رَأسِ PermissionDoor.cs.
        // ‏(‏2026-10-25، كوميتُ التَبديل): والرابِعُ الرَئيسِيَّة — هذا الصَفُّ يُسَمّيها.
        Assert.Equal(new[] { "GatedPage.razor", "MyListings.razor", "TenantHome.razor", "TenantListingDetail.razor" }, consumers);
    }

    /// <summary><b>أَبوابُ الدَورِ صادِقَةٌ لِلكاتالوج</b>: كُلُّ دَورٍ في <c>RoleCatalog.Definitions</c> يَختارُ <c>sellerHome</c> أَو <c>riderHome</c>
    /// (ومَعَهُما تَبويبا <c>vendorNav</c>/<c>riderNav</c> اللَّذانِ يَبني مِنهُما الـmanifest مُختَصَرَ «إعلان جَديد»)، وفَرعا <c>RoleHomeHero</c>
    /// (‏rider وvendor وhost)، وكُلُّ دَورٍ يَحمِلُ <c>tenant.manage</c> — يَحمِلُ <c>listing.create</c>. ويُطبَعُ ما عُدَّ ويَفشَلُ عَلى الصِفر.
    /// <para><b>وما لا يَقولُه</b>: دَورٌ مُؤَلَّفٌ (وَثيقَةُ <c>TenantRoleDefinition</c>) يَختارُ <c>sellerHome</c> بِلا الصَلاحِيَّةِ لا يَمنَعُه مُصادِق —
    /// مُدَوَّنٌ في سِجِلِّ المالِكِ «أ» W8-7.</para></summary>
    [Fact]
    public void Role_home_doors_to_create_listing_are_drawn_only_for_catalog_roles_that_carry_listing_create()
    {
        var byHome = RoleCatalog.Definitions
            .Where(d => d.Composition.Home is RoleComponents.SellerHome or RoleComponents.RiderHome
                     || d.Composition.Nav is RoleComponents.VendorNav or RoleComponents.RiderNav
                     || d.Slug is "rider" or "vendor" or "host"
                     || d.Permissions.Contains("tenant.manage"))
            .ToList();
        var lacking = byHome.Where(d => !d.Permissions.Contains(Permission)).Select(d => d.Slug).ToList();
        var files = CreateListingLinks().Where(l => l.Door == Door.RoleHome).GroupBy(l => l.File).ToList();
        output.WriteLine($"أَدوارُ كاتالوجٍ تَختارُ بابَ دَور: {byHome.Count} ({string.Join(", ", byHome.Select(d => d.Slug))}) · بِلا listing.create: {lacking.Count} · "
            + $"مِلَفّاتُ أَبوابِ الدَور: {string.Join(" · ", files.Select(g => $"{Path.GetFileName(g.Key)}: {g.Count()}"))}");

        Assert.True(byHome.Count >= 6, $"أَداةٌ عَمياء: {byHome.Count} دَوراً — والمَقيسُ 6.");
        Assert.Empty(lacking);
        // كُلُّ مِلَفٍّ مُسَمّىً فيه رابِطٌ يُبَرِّرُ اسمَه — والمِلَفُّ الَّذي فَقَدَ رابِطَه يُحذَفُ مِن القائِمَة.
        Assert.Equal(RoleHomeFiles.Keys.OrderBy(k => k, StringComparer.Ordinal), files.Select(g => g.Key).OrderBy(k => k, StringComparer.Ordinal));
        Assert.Contains("canManage = ACommerce.Kit.Roles.RolePermissions.Has(", Read(TemplateRoot + "/Components/Pages/TenantManage.razor"), StringComparison.Ordinal);
    }

    /// <summary><b>الماسِحُ يُقاسُ قَبلَ أَن يُوثَقَ بِه</b> (القاعِدَة ١٠): صَفحَةٌ مَحقونَةٌ فيها رابِطٌ خَلفَ مُتَغَيِّرِ بابٍ، ورابِطٌ خَلفَ نِداءٍ مُباشِر،
    /// ورابِطٌ خَلفَ صَلاحِيَّةٍ أُخرى، ورابِطٌ خَلفَ نَفيِ البابِ، ورابِطٌ خَلفَ <c>|| true</c>، ورابِطٌ بِلا شَرط — الأَوَّلانِ خَلفَ الباب والأَربَعَةُ خارِجَه؛
    /// وتَوجيهُ <c>@page</c> والتَعليقُ وصِنفُ <c>acs-create-listing</c> لا تُعَدّ؛ وصَفحَةُ النَشرِ المَحقونَةُ داخِلَ <c>GatedPage</c> بابٌ، وخارِجَه لا.</summary>
    [Fact]
    public void The_create_listing_link_scanner_counts_a_negated_an_or_and_another_permission_as_outside()
    {
        const string L = "@AuthSession.LinkFor(Slug, Role, $\"create-listing\")";
        var sources = new[]
        {
            ("x/Injected.razor",
                "@page \"/{slug}/create-listing\"\n"
              + "<div class=\"acs-create-listing\"></div>\n"
              + "@if (canPost)\n{\n    <a href=\"" + L + "\">a</a>\n}\n"
              + "@if (Auth.IsAuthenticated && ACommerce.Kit.Roles.RolePermissions.Has(roles, role, \"listing.create\"))\n{\n    <a href=\"" + L + "\">b</a>\n}\n"
              + "@if (canOffer)\n{\n    <a href=\"" + L + "\">c</a>\n}\n"
              + "@if (!canPost)\n{\n    <a href=\"" + L + "\">d</a>\n}\n"
              + "@if (canPost || true)\n{\n    <a href=\"" + L + "\">e</a>\n}\n"
              + "<a href=\"" + L + "\">f</a>\n"
              + "@* <a href=\"" + L + "\">g</a> *@\n"
              + "@code {\n    bool canPost; bool canOffer;\n    void M() { canPost = RolePermissions.Has(roles, role, \"listing.create\"); canOffer = RolePermissions.Has(roles, role, \"offer.submit\"); }\n}\n"),
            (PageFile,
                "<GatedPage Slug=\"@Slug\" Role=\"@Role\" Permission=\"listing.create\">\n    <a href=\"" + L + "\">h</a>\n</GatedPage>\n"
              + "<a href=\"" + L + "\">i</a>\n"),
        };
        var links = CreateListingLinksIn(sources);
        output.WriteLine($"مَحقونَة: مَعدودَة {links.Count} · {Summary(links)} · خارِجَ بابٍ: "
            + string.Join(" · ", links.Where(l => l.Door == Door.Outside).Select(l => $"{Path.GetFileName(l.File)}:{l.Line}")));

        Assert.Equal(8, links.Count);
        Assert.Equal(new[] { Door.Predicate, Door.Predicate, Door.Outside, Door.Outside, Door.Outside, Door.Outside },
            links.Where(l => l.File == "x/Injected.razor").Select(l => l.Door).ToArray());
        Assert.Equal(new[] { Door.Page, Door.Outside }, links.Where(l => l.File == PageFile).Select(l => l.Door).ToArray());
    }
}
