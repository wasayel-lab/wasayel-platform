using System.Text.RegularExpressions;
using ACommerce.Kit.Subscriptions;
using ACommerce.Kit.Tenants;
using ACommerce.Templates.Customer.Marketplace.Services;
using ACommerce.Templates.Customer.Marketplace.Services.Incubator;
using Marten;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ بِطاقَةُ «الباقات» في «حِسابي» تُرسَمُ في كُلِّ مَتجَر — ومَتجَرُ الاستوديو يَفتَحُها عَلى «لا توجَد باقات بَعد» ═══
//
// **تَدقيقُ الإطلاقِ عِندَ `dace7cd`، تَيّارُ W8-7**: ‏`lens-dead-doors-5` (كُبرى). أُعيدَ قِياسُه عِندَ الرَأسِ `9ca4e06` (‏2026-10-25):
// - ‏`Me.razor` يَرسُمُ `AcActionCard` «الباقات» إلى `plans` لِكُلِّ صاحِبِ جَلسَة، بِلا سُؤالٍ عَمّا تَعرِضُه الصَفحَة.
// - ‏`Plans.razor` تَعرِضُ `PlanPurchasePolicy.Visible(VisiblePlansAsync, PlansPayable)`، وحينَ تَكونُ فارِغَةً تَرسُمُ «لا توجَد باقات بَعد» ونَصّاً
//   لِلمُشَغِّل «يُسَجِّل المَتجَر باقاته مِن لوحَة التَحَكُّم.» — و`TenantFromAnalysisFactory.CreateAsync` لا يَكتُبُ `Plan` (يُخَزِّنُ `Tenant` وَحدَه).
// - **حَيّاً** (قاعِدَةٌ مَحَلِّيَّة `wasayel_w87`، الرَأسُ `7b8de7f`): `/owner-test/me` يَربِطُ إلى `/owner-test/plans`، وهي ‏200 بِالحالَةِ الفارِغَة؛
//   و`/ejar/plans` شَبَكَة.
// - ورابِطٌ ثانٍ إلى الصَفحَةِ لا يَسأَلُها: مَخرَجُ «عَرض الباقات» في رِسالَةِ نَفادِ الحِصَّةِ (`CreateListing.razor`، `?err=quota`).
//
// هذا المِلَفُّ يُوَصِّفُ «اليَوم» (القاعِدَة ٣) قَبلَ أَيِّ تَبديل؛ صُفوفُ `Today_` تُقلَبُ بِأَسمائِها في كوميتِ الإصلاح، والباقي يَبقى بِلا تَعديلِ حَرف.
public class PlansDoorTests(ITestOutputHelper output)
{
    private const string TemplateRoot = "libs/templates/ACommerce.Templates.Customer.Marketplace";
    private const string PlansPage = TemplateRoot + "/Components/Pages/Plans.razor";
    private const string MePage = TemplateRoot + "/Components/Pages/Me.razor";

    private static string Read(string relative)
    {
        var path = Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, relative.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(path), $"مَصدَرٌ مَفقود: {relative} — الأَداةُ عَمياءُ بِلا طَرَفٍ مَقروء.");
        return File.ReadAllText(path);
    }

    // ─── الماسِح ────────────────────────────────────────────────────────────────────────────

    /// <summary>ذِكرٌ لِصَفحَةِ باقاتِ المَتجَر: <c>plans</c> يَسبِقُه تَنصيصٌ أَو <c>/</c> ويَليه تَنصيصٌ أَو <c>?</c> أَو <c>#</c> — فَـ<c>"plans.list.title"</c>
    /// (مِفتاحُ قامُوس) لا يُعَدّ. وصَفحَةُ الاستوديو <c>/studio/apps/{Slug}/plans</c> لَيسَت هذِه الصَفحَة فَلا تُعَدّ.</summary>
    private static readonly Regex PlansToken = new(@"(?<=[""'/])plans(?=[""?#])", RegexOptions.Compiled);

    /// <summary><c>Shelf</c> داخِلَ <c>if</c> شِقٌّ في شَرطِه مُتَغَيِّرٌ مِن <c>PlanShelf.ShownAsync(…)).Count &gt; 0</c>؛ و<c>Self</c> الصَفحَةُ نَفسُها؛
    /// و<c>Named</c> مِلَفٌّ مُسَمّىً بِسَبَبِه (<see cref="NamedFiles"/>)؛ و<c>Outside</c> ما سِواه.</summary>
    internal enum Door { Shelf, Self, Named, Outside }

    internal sealed record Link(string File, int Line, Door Door);

    /// <summary><b>مَلَفّاتٌ لا تُفضي إلى الصَفحَةِ إلّا مِن داخِلِها</b>، مُسَمّاةً بِسَبَبِها — والقائِمَةُ تَتَقَلَّصُ ولا تَنمو إلّا بِقَرارٍ مَرئِيّ.</summary>
    internal static readonly IReadOnlyDictionary<string, string> NamedFiles = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [TemplateRoot + "/Components/Pages/PlanPay.razor"] = "صَفحَةُ دَفعِ باقَةٍ بِعَينِها (`plans/{id}/pay`) — لا يَبلُغُها إلّا مَن نَقَرَ باقَةً عَرَضَتها الصَفحَة",
    };

    internal static List<Link> PlansLinksIn(IEnumerable<(string File, string Text)> sources)
    {
        var links = new List<Link>();
        foreach (var (file, text) in sources)
        {
            var rel = Path.IsPathRooted(file) ? Path.GetRelativePath(ThemeZeroEquivalenceTests.RepoRoot, file).Replace('\\', '/') : file;
            if (!rel.EndsWith(".razor", StringComparison.OrdinalIgnoreCase)) continue;
            var code = WriteEndpointGuardTests.StripMarkupComments(text);
            if (!PlansToken.IsMatch(code)) continue;

            var shelfVars = ShelfVariables(code);
            foreach (Match m in PlansToken.Matches(code))
            {
                var lineStart = code.LastIndexOf('\n', Math.Max(0, m.Index - 1)) + 1;
                var head = code[lineStart..m.Index];
                if (head.TrimStart().StartsWith("@page", StringComparison.Ordinal)) continue;
                if (head.Contains("/studio/", StringComparison.Ordinal)) continue;
                var line = code[..m.Index].Count(c => c == '\n') + 1;

                Door door;
                if (rel == PlansPage) door = Door.Self;
                else if (UpgradePromptPriceTests.EnclosingIfConditions(code, m.Index).Any(c => Conjuncts(c).Any(shelfVars.Contains))) door = Door.Shelf;
                else if (NamedFiles.ContainsKey(rel)) door = Door.Named;
                else door = Door.Outside;
                links.Add(new Link(rel, line, door));
            }
        }
        return links;
    }

    internal static List<Link> PlansLinks() => PlansLinksIn(EntitlementContractTests.SourceFiles());

    /// <summary>مُتَغَيِّراتُ الرَفّ: <c>x = (await PlanShelf.ShownAsync(…)).Count &gt; 0</c> بِحَرفِ الشَرط.</summary>
    private static HashSet<string> ShelfVariables(string code)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match m in Regex.Matches(code,
                     @"(?<var>[A-Za-z_]\w*)\s*=\s*\(\s*await\s+(?:[\w.]*\.)?PlanShelf\.ShownAsync\([^;]*?\)\s*\)\s*\.Count\s*>\s*0\s*;", RegexOptions.Singleline))
            set.Add(m.Groups["var"].Value);
        return set;
    }

    /// <summary>شُقوقُ <c>&amp;&amp;</c> العُليا في شَرط — ولا شَيءَ إن كانَ فيه <c>||</c> أَو <c>?</c> في مُستَواه الأَعلى.</summary>
    private static IEnumerable<string> Conjuncts(string condition)
    {
        var parts = new List<string>();
        var depth = 0;
        var last = 0;
        for (var k = 0; k < condition.Length; k++)
        {
            var c = condition[k];
            if (c is '(' or '[' or '{') depth++;
            else if (c is ')' or ']' or '}') depth--;
            else if (depth == 0 && (c == '?' || (c == '|' && k + 1 < condition.Length && condition[k + 1] == '|'))) return Array.Empty<string>();
            else if (depth == 0 && c == '&' && k + 1 < condition.Length && condition[k + 1] == '&')
            {
                parts.Add(condition[last..k].Trim());
                last = k + 2;
                k++;
            }
        }
        parts.Add(condition[last..].Trim());
        return parts;
    }

    private static string Summary(IEnumerable<Link> links)
        => string.Join(" · ", links.GroupBy(l => l.Door).OrderBy(g => g.Key).Select(g => $"{g.Key}: {g.Count()}"));

    // ─── «اليَوم» ───────────────────────────────────────────────────────────────────────────

    /// <summary><b>قُلِبَ «Today_» بِاسمِه (‏2026-10-25، كوميتُ إصلاحِ <c>lens-dead-doors-5</c>)</b> — كانَ
    /// <c>Today_the_account_page_draws_the_plans_card_without_asking_what_the_plans_page_shows</c> (‏<c>1c89a64</c>): لا شَرطَ فَوقَ البِطاقَة. والآن
    /// شَرطُها الوَحيدُ <c>plansShown</c>، وهُوَ <c>(await PlanShelf.ShownAsync(PlanSvc, Providers, Slug)).Count &gt; 0</c> — ما تَرسُمُه الصَفحَةُ بِعَينِه.</summary>
    [Fact]
    public void The_account_page_draws_the_plans_card_only_when_the_plans_shelf_shows_a_plan()
    {
        var me = WriteEndpointGuardTests.StripMarkupComments(Read(MePage));
        var card = me.IndexOf("L[\"me.action.plans\"]", StringComparison.Ordinal);
        var conditions = UpgradePromptPriceTests.EnclosingIfConditions(me, card);
        output.WriteLine($"بِطاقَةُ الباقات عِندَ {card} · شُروطُها: {(conditions.Count == 0 ? "لا شَيء" : string.Join(" | ", conditions))}");

        Assert.True(card > 0, "بِطاقَةُ الباقات غَيرُ مَوجودَة — الأَداةُ عَمياء.");
        Assert.Equal(new[] { "plansShown" }, conditions);
        Assert.Contains("plansShown = (await ACommerce.Templates.Customer.Marketplace.Services.PlanShelf.ShownAsync(PlanSvc, Providers, Slug)).Count > 0;",
            me, StringComparison.Ordinal);
    }

    /// <summary><b>قُلِبَ «Today_» بِاسمِه (‏2026-10-25، كوميتُ إصلاحِ <c>lens-dead-doors-5</c>)</b> — كانَ
    /// <c>Today_two_store_links_to_the_plans_page_are_drawn_outside_its_shelf</c> (‏<c>1c89a64</c>): ‏7 روابِطَ، اثنانِ خارِجَ كُلِّ باب. والآن بِطاقَةُ
    /// «حِسابي» ومَخرَجُ نَفادِ الحِصَّةِ خَلفَ الرَفّ — <b>صِفرٌ خارِجَ باب</b>، بِالماسِحِ لا بِقائِمَة؛ والرَفُّ لا يُسأَلُ إلّا مِن مُستَهلِكيه الثَلاثَةِ المُسَمَّين
    /// في رَأسِه (شَرطُ الاستِخراج، القاعِدَة ١).</summary>
    [Fact]
    public void Every_store_link_to_the_plans_page_sits_behind_its_shelf_or_inside_it()
    {
        var links = PlansLinks();
        var outside = links.Where(l => l.Door == Door.Outside).ToList();
        var consumers = EntitlementContractTests.SourceFiles()
            .Where(f => !f.File.Replace('\\', '/').EndsWith("/Services/PlanShelf.cs", StringComparison.Ordinal))
            .Where(f => WriteEndpointGuardTests.StripComments(WriteEndpointGuardTests.StripMarkupComments(f.Text)).Contains("PlanShelf.ShownAsync(", StringComparison.Ordinal))
            .Select(f => Path.GetFileName(f.File)).OrderBy(f => f, StringComparer.Ordinal).ToArray();
        output.WriteLine($"روابِطُ plans: {links.Count} في {links.Select(l => l.File).Distinct().Count()} مِلَفّات · {Summary(links)} · خارِجَ بابٍ: "
            + (outside.Count == 0 ? "لا شَيء" : string.Join(" · ", outside.Select(l => $"{Path.GetFileName(l.File)}:{l.Line}")))
            + $" · مُستَهلِكو PlanShelf: {string.Join(", ", consumers)}");

        Assert.True(links.Count >= 7, $"أَداةٌ عَمياء: {links.Count} رابِطاً — والمَقيسُ 7.");
        Assert.True(outside.Count == 0, "رابِطٌ إلى صَفحَةِ الباقاتِ خارِجَ رَفِّها:\n  " + string.Join("\n  ", outside.Select(l => $"{l.File}:{l.Line}"))
            + "\nضَعه داخِلَ @if (x) و x = (await PlanShelf.ShownAsync(…)).Count > 0.");
        Assert.Equal(new[] { "CreateListing.razor", "Me.razor" },
            links.Where(l => l.Door == Door.Shelf).Select(l => Path.GetFileName(l.File)).OrderBy(f => f, StringComparer.Ordinal).ToArray());
        Assert.Equal(new[] { "CreateListing.razor", "Me.razor", "Plans.razor" }, consumers);
    }

    // ─── ما لا يَتَبَدَّل ───────────────────────────────────────────────────────────────────

    /// <summary>الصَفحَةُ تَرسُمُ شَبَكَتَها حينَ تَعرِضُ باقاتٍ وحالَتَها الفارِغَةَ (العُنوانُ ونَصُّ المُشَغِّل) حينَ لا — والإصلاحُ في الأَبوابِ إلَيها لا فيها:
    /// مَن كَتَبَ العُنوانَ بِيَدِه يَرى ما كانَ يَراه.</summary>
    [Fact]
    public void The_plans_page_draws_its_grid_only_when_plans_are_shown_and_otherwise_its_empty_state()
    {
        var page = WriteEndpointGuardTests.StripMarkupComments(Read(PlansPage));
        var empty = page.IndexOf("L[\"plans.list.empty_title\"]", StringComparison.Ordinal);
        var grid = page.IndexOf("acm-plans-grid", StringComparison.Ordinal);
        Assert.Equal(new[] { "plans.Count == 0" }, UpgradePromptPriceTests.EnclosingIfConditions(page, empty).Where(c => c != "plans is null"));
        Assert.Contains("L[\"plans.list.empty_message\"]", page, StringComparison.Ordinal);
        Assert.True(grid > empty, "الشَبَكَةُ لا تَلي الحالَةَ الفارِغَة.");
        Assert.StartsWith("else", page[(page.IndexOf("Message=\"@(L[\"plans.list.empty_message\"])\" />", StringComparison.Ordinal) + "Message=\"@(L[\"plans.list.empty_message\"])\" />".Length)..].TrimStart()[1..].TrimStart(), StringComparison.Ordinal);
    }

    /// <summary><b>الماسِحُ يُقاسُ قَبلَ أَن يُوثَقَ بِه</b> (القاعِدَة ١٠): صَفحَةٌ مَحقونَةٌ فيها رابِطٌ خَلفَ مُتَغَيِّرِ الرَفّ، ورابِطٌ خَلفَ نَفيِه، ورابِطٌ خَلفَ
    /// <c>|| true</c>، ورابِطٌ بِلا شَرط، ورابِطُ دُخولٍ بِـ<c>returnUrl=/{Slug}/plans</c> — الأَوَّلُ خَلفَ الباب والأَربَعَةُ خارِجَه؛ وتَوجيهُ <c>@page</c>
    /// والتَعليقُ ومِفتاحُ القامُوسِ ورابِطُ صَفحَةِ الاستوديو لا تُعَدّ.</summary>
    [Fact]
    public void The_plans_link_scanner_counts_a_negated_an_or_and_a_return_url_as_outside_and_ignores_the_studio_page()
    {
        const string L = "@AuthSession.LinkFor(Slug, Role, \"plans\")";
        var sources = new[]
        {
            ("x/Injected.razor",
                "@page \"/{slug}/plans\"\n"
              + "<h1>@L.Markup(\"plans.list.title\")</h1>\n"
              + "@if (shown)\n{\n    <a href=\"" + L + "\">a</a>\n}\n"
              + "@if (!shown)\n{\n    <a href=\"" + L + "\">b</a>\n}\n"
              + "@if (shown || true)\n{\n    <a href=\"" + L + "\">c</a>\n}\n"
              + "<a href=\"" + L + "\">d</a>\n"
              + "<a href=\"@AuthSession.LinkFor(Slug, Role, $\"login?returnUrl=/{Slug}/plans\")\">e</a>\n"
              + "<a href=\"@($\"/studio/apps/{Slug}/plans\")\">f</a>\n"
              + "@* <a href=\"" + L + "\">g</a> *@\n"
              + "@code {\n    bool shown;\n    async Task M() { shown = (await PlanShelf.ShownAsync(PlanSvc, Providers, Slug)).Count > 0; }\n}\n"),
        };
        var links = PlansLinksIn(sources);
        output.WriteLine($"مَحقونَة: مَعدودَة {links.Count} · {Summary(links)}");

        Assert.Equal(new[] { Door.Shelf, Door.Outside, Door.Outside, Door.Outside, Door.Outside }, links.Select(l => l.Door).ToArray());
    }
}

// ═══ البُرهانُ عَلى Postgres — مَتجَرُ المَصنَعِ الحَقيقيِّ بِلا باقَة، ومَتجَرٌ بِباقَةٍ مَجّانِيَّةٍ تُعرَضُ لَه ═══
//
// يَتَخَطّى نَفسَه ما لَم تُضبَط WASAYEL_LIVE_PROOF=1 و ConnectionStrings__Postgres (عَقدُ `LiveStudioStorePatternProofTests`). المَقيسُ ما تَعرِضُه
// صَفحَةُ الباقاتِ بِقِراءَتَيها نَفسَيهِما — `TenantPlanService.VisiblePlansAsync` و`TenantProviderService.ForAsync(…).PlansPayable` ثُمَّ
// `PlanPurchasePolicy.Visible` — لِمَتجَرٍ يَكتُبُه `TenantFromAnalysisFactory.CreateAsync` (مَسارُ `/studio/s/{id}/build`)، ثُمَّ لَه بَعدَ باقَةٍ مَجّانِيَّة.
// ويُحذَفُ ما كُتِبَ بَعدَه.
[Collection(LiveProofCollection.Name)]
public class LivePlansShelfProofTests(ITestOutputHelper output)
{
    private static bool Enabled =>
        Environment.GetEnvironmentVariable("WASAYEL_LIVE_PROOF") == "1";

    private static string? ConnectionString =>
        Environment.GetEnvironmentVariable("ConnectionStrings__Postgres");

    private static DocumentStore BuildStore() => DocumentStore.For(o =>
    {
        o.Connection(ConnectionString!.TrimEnd(';') + ";Timeout=60;Command Timeout=120");
        o.DatabaseSchemaName = "platform";
        o.Policies.AllDocumentsAreMultiTenanted();
        o.Events.TenancyStyle = global::JasperFx.MultiTenancy.TenancyStyle.Conjoined;
        o.Schema.For<Tenant>().SingleTenanted().Identity(x => x.Id);
        o.AutoCreateSchemaObjects = JasperFx.AutoCreate.All;
    });

    [Fact]
    public async Task A_store_built_by_the_studio_factory_shows_no_plan_and_shows_one_once_a_free_plan_is_stored()
    {
        if (!Enabled || string.IsNullOrEmpty(ConnectionString)) return;

        await using var store = BuildStore();
        var slug = $"w87plans-{Guid.NewGuid():N}"[..20];
        await new TenantFromAnalysisFactory(store).CreateAsync(slug, "بُرهانُ رَفِّ الباقات", "#2563eb", tagLine: "", city: "",
            pattern: "", sector: "", authChannel: "phone", ownerId: Guid.NewGuid(), sourceAnalysisId: Guid.NewGuid());
        try
        {
            async Task<IReadOnlyList<Plan>> ShownAsync() => PlanPurchasePolicy.Visible(
                await new TenantPlanService(store).VisiblePlansAsync(slug), (await new TenantProviderService(store).ForAsync(slug)).PlansPayable);

            var born = await ShownAsync();
            await using (var ws = store.LightweightSession(slug))
            {
                ws.Store(new Plan { Id = "w87-free", Name = "مَجّانِيّ", Price = 0, ListingsQuota = 1, DaysPeriod = 30 });
                await ws.SaveChangesAsync();
            }
            var withFree = await ShownAsync();
            output.WriteLine($"[live] slug={slug} · باقاتٌ تُعرَضُ لِمَتجَرِ المَصنَع: {born.Count} · بَعدَ باقَةٍ مَجّانِيَّة: {withFree.Count} ({string.Join(",", withFree.Select(p => p.Id))})");

            Assert.Empty(born);
            Assert.Equal(new[] { "w87-free" }, withFree.Select(p => p.Id).ToArray());
        }
        finally
        {
            await using var ws = store.LightweightSession(slug);
            ws.Delete<Plan>("w87-free");
            await ws.SaveChangesAsync();
            await using var gs = store.LightweightSession();
            gs.Delete<Tenant>(slug);
            await gs.SaveChangesAsync();
        }
    }

    /// <summary><b>والرَفُّ هُوَ ما كانَت الصَفحَةُ تَقرَؤُه قَبلَ أَن يَنتَقِل</b> (‏2026-10-25، كوميتُ إصلاحِ <c>lens-dead-doors-5</c>): عَلى المَتجَرِ
    /// نَفسِه في حالَتَيه — بِلا باقَةٍ ثُمَّ بِباقَةٍ مَجّانِيَّة — <c>PlanShelf.ShownAsync</c> يُعيدُ ما يُعيدُه التَعبيرُ القَديمُ بِحَرفِه في الصَفِّ أَعلاه.</summary>
    [Fact]
    public async Task The_shelf_returns_what_the_plans_page_read_before_it_moved()
    {
        if (!Enabled || string.IsNullOrEmpty(ConnectionString)) return;

        await using var store = BuildStore();
        var slug = $"w87shelf-{Guid.NewGuid():N}"[..20];
        await new TenantFromAnalysisFactory(store).CreateAsync(slug, "بُرهانُ تَكافُؤِ الرَفّ", "#2563eb", tagLine: "", city: "",
            pattern: "", sector: "", authChannel: "phone", ownerId: Guid.NewGuid(), sourceAnalysisId: Guid.NewGuid());
        try
        {
            async Task<(string Old, string Shelf)> BothAsync()
            {
                var old = PlanPurchasePolicy.Visible(
                    await new TenantPlanService(store).VisiblePlansAsync(slug), (await new TenantProviderService(store).ForAsync(slug)).PlansPayable);
                var shelf = await PlanShelf.ShownAsync(new TenantPlanService(store), new TenantProviderService(store), slug);
                return (string.Join(",", old.Select(p => p.Id)), string.Join(",", shelf.Select(p => p.Id)));
            }

            var born = await BothAsync();
            await using (var ws = store.LightweightSession(slug))
            {
                ws.Store(new Plan { Id = "w87-free", Name = "مَجّانِيّ", Price = 0, ListingsQuota = 1, DaysPeriod = 30 });
                ws.Store(new Plan { Id = "w87-paid", Name = "مَدفوع", Price = 49, ListingsQuota = 10, DaysPeriod = 30 });
                await ws.SaveChangesAsync();
            }
            var stocked = await BothAsync();
            output.WriteLine($"[live] slug={slug} · بِلا باقَة: «{born.Old}» = «{born.Shelf}» · بِمَجّانِيَّةٍ ومَدفوعَة: «{stocked.Old}» = «{stocked.Shelf}»");

            Assert.Equal(born.Old, born.Shelf);
            Assert.Equal("", born.Shelf);
            Assert.Equal(stocked.Old, stocked.Shelf);
            Assert.Equal("w87-free", stocked.Shelf); // ‏بِلا رابِطِ دَفعٍ لا تُعرَضُ المَدفوعَة (PlanPurchasePolicy.Visible)
        }
        finally
        {
            await using var ws = store.LightweightSession(slug);
            ws.Delete<Plan>("w87-free");
            ws.Delete<Plan>("w87-paid");
            await ws.SaveChangesAsync();
            await using var gs = store.LightweightSession();
            gs.Delete<Tenant>(slug);
            await gs.SaveChangesAsync();
        }
    }
}
