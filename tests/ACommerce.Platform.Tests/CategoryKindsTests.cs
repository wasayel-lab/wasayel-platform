using System.Text.RegularExpressions;
using ACommerce.Kit.Tenants;
using ACommerce.Platform.I18n;
using ACommerce.Templates.Customer.Marketplace.Services;
using ACommerce.Templates.Customer.Marketplace.Services.Incubator;
using ACommerce.Templates.Customer.Marketplace.Services.Patterns;
using ACommerce.Templates.Customer.Marketplace.Services.TenantConfig;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ تَجميعُ الفِئَة — مَعجَمٌ مُغلَقٌ بِمُصادِقٍ يَمُرُّ بِه الكُتّابُ الثَلاثَة (قَرارُ المالِك د25، ويُغَطّي د49) ═══
//
// **ما كانَ** (مُثَبَّتاً في `StudioStorePatternTests` قَبلَ قَلبِه): المُحَرِّرُ والوَكيلُ وصَفحَةُ الإنشاءِ الإدارِيَّةُ يَكتُبونَ أَيَّ نَصٍّ
// تَجميعاً، و`PatternOf` يَقرَأُ «تِجاريّ»/«سَكَنيّ» إيجاراً بِلا سَلَّة. **وما صارَ**: `CategoryKinds.All` عَشرُ قيَمٍ مِمّا يَكتُبُه الكودُ
// ويَقرَؤُه (نَصُّ القَرار)، و`CategoryKinds.Validate` يَرُدُّ ما سِواها بِرَمزٍ ثابِت (`category_kind_unknown`) — في `CategoriesSaveService.Parse`
// (المُحَرِّرانِ وصَفحَةُ الإنشاء) وفي الوَكيلِ قَبلَ الكِتابَة؛ والمُحَرِّرُ يَعرِضُ القيَمَ اختِياراً بِأَثَرِ كُلٍّ مِنها مَقروءاً مِن `PatternOf` نَفسِه.

public class CategoryKindsTests(ITestOutputHelper output)
{
    private static string Root => ThemeZeroEquivalenceTests.RepoRoot;

    /// <summary>القائِمَةُ بِحَرفِ نَصِّ القَرار — لا قيمَةٌ مُختَرَعَةٌ ولا ناقِصَة.</summary>
    [Fact]
    public void The_lexicon_is_exactly_the_ten_values_the_owner_listed()
    {
        Assert.Equal(new[] { "", "menu", "residential", "commercial", "roommate", "fnb", "services", "transport", "events", "vehicles" },
            CategoryKinds.All.ToArray());
        Assert.Equal(new[] { CategoryKinds.Unknown }, CategoryKinds.Codes.ToArray());
        Assert.Equal("category_kind_unknown", CategoryKinds.Unknown);
        Assert.Contains(TenantConfigCodes.CategoryKindUnknown, TenantConfigCodes.All);
        Assert.Equal(CategoryKinds.Unknown, TenantConfigCodes.CategoryKindUnknown);
    }

    // ─── الرَمزُ الواحِد: مُوجَبٌ لِكُلِّ قيمَة، وسالِبٌ لِما سِواها ─────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("menu")]
    [InlineData("residential")]
    [InlineData("commercial")]
    [InlineData("roommate")]
    [InlineData("fnb")]
    [InlineData("services")]
    [InlineData("transport")]
    [InlineData("events")]
    [InlineData("vehicles")]
    public void A_kind_in_the_lexicon_passes_the_validator_and_the_editor_parser(string kind)
    {
        Assert.True(CategoryKinds.IsKnown(kind));
        Assert.Empty(CategoryKinds.Validate(new[] { new Category { Slug = "a", Label = "أ", Kind = kind } }));

        var (cats, code) = CategoriesSaveService.Parse($"a | أ | 🏷️ | {kind}");
        Assert.Null(code);
        Assert.Equal(kind, Assert.Single(cats!).Kind);
    }

    [Theory]
    [InlineData("leisure")]
    [InlineData("shop")]
    [InlineData("rental")]
    [InlineData("commerical")]
    public void A_kind_outside_the_lexicon_is_refused_with_the_fixed_code(string kind)
    {
        Assert.False(CategoryKinds.IsKnown(kind));
        var v = Assert.Single(CategoryKinds.Validate(new[] { new Category { Slug = "a", Label = "أ", Kind = kind } }));
        Assert.Equal(CategoryKinds.Unknown, v.Code);
        Assert.Contains(kind, v.MessageAr, StringComparison.Ordinal);

        var (cats, code) = CategoriesSaveService.Parse($"a | أ | 🏷️ | {kind}");
        Assert.Null(cats);
        Assert.Equal(TenantConfigCodes.CategoryKindUnknown, code);
    }

    /// <summary>والمُقارَنَةُ بِحَرفِها: الكُتّابُ يُصَغِّرونَ قَبلَ السُؤال، فَـ«Commercial» يَعبُرُ المُحَلِّلَ «commercial» ولا يَعبُرُ الدالَّةَ خاماً.</summary>
    [Fact]
    public void The_check_is_ordinal_and_the_writers_lower_case_before_asking()
    {
        Assert.False(CategoryKinds.IsKnown("Commercial"));
        Assert.False(CategoryKinds.IsKnown(null));
        var (cats, code) = CategoriesSaveService.Parse("a | أ | 🏷️ | Commercial");
        Assert.Null(code);
        Assert.Equal("commercial", Assert.Single(cats!).Kind);
    }

    /// <summary>المُصادِقُ يُجيبُ بِكُلِّ خُروقِه لا بِأَوَّلِها (‏ADR-038)، لِكُلِّ فِئَةٍ بِاسمِها.</summary>
    [Fact]
    public void The_validator_answers_every_violation_by_category()
    {
        var v = CategoryKinds.Validate(new[]
        {
            new Category { Slug = "a", Label = "أ", Kind = "leisure" },
            new Category { Slug = "b", Label = "ب", Kind = "commercial" },
            new Category { Slug = "c", Label = "ج", Kind = "shop" },
        });
        Assert.Equal(2, v.Count);
        Assert.All(v, x => Assert.Equal(CategoryKinds.Unknown, x.Code));
        Assert.Contains("«a»", v[0].MessageAr, StringComparison.Ordinal);
        Assert.Contains("«c»", v[1].MessageAr, StringComparison.Ordinal);
    }

    // ─── الكُتّابُ الثَلاثَة ─────────────────────────────────────────────────────────────────

    /// <summary>(١) المُحَرِّر: بابُ الحِفظِ يَرُدُّ بِالرَمزِ ولا يَكتُبُ حَرفاً.</summary>
    [Fact]
    public async Task The_editor_save_door_refuses_an_unknown_kind_before_writing()
    {
        var world = new DocWorld().Put(new Tenant
        {
            Id = "kinds-probe",
            Categories = new() { new Category { Slug = "general", Label = "عام", Kind = "" } },
        });

        var refused = await CategoriesSaveService.SaveAsync(world.Session, "kinds-probe",
            new CategoriesSaveRequest("products | مُنتَجات | 🛍️ | leisure"));
        Assert.Equal(TenantConfigStatus.Rejected, refused.Status);
        Assert.Equal(TenantConfigCodes.CategoryKindUnknown, refused.Code);
        Assert.Empty(world.Stored);

        var saved = await CategoriesSaveService.SaveAsync(world.Session, "kinds-probe",
            new CategoriesSaveRequest("shops | مَحَلّات | 🏪 | commercial"));
        Assert.True(saved.Ok);
        Assert.Equal("commercial", Assert.Single(Assert.Single(world.Stored.OfType<Tenant>()).Categories).Kind);
    }

    /// <summary>(٢) الوَكيل: <c>set_categories</c> و<c>create_tenant</c> يُرَدّانِ بِالرَمزِ قَبلَ أَيِّ قِراءَةٍ أَو كِتابَة، و«تِجاريّ» يُكتَب.</summary>
    [Fact]
    public async Task The_studio_agent_refuses_an_unknown_kind_at_set_categories_and_create_tenant()
    {
        var owner = Guid.NewGuid();
        var world = new DocWorld().Put(new Tenant
        {
            Id = "kinds-probe",
            OwnerUserId = owner,
            Categories = new() { new Category { Slug = "general", Label = "عام", Kind = "" } },
        });
        var executor = new AgentToolExecutor(world.Store, new TenantRoleService(world.Store));

        var (ok, message) = await executor.ExecuteAsync("set_categories",
            """{"slug":"kinds-probe","categories":[{"slug":"x","label":"س","kind":"Leisure"}]}""", owner);
        Assert.False(ok);
        Assert.Contains(CategoryKinds.Unknown, message, StringComparison.Ordinal);
        Assert.Equal(0, world.SaveCalls);

        var (created, why) = await executor.ExecuteAsync("create_tenant",
            """{"slug":"kinds-new","name":"تَجرِبَة","color":"#123456","channel":"phone","categories":[{"slug":"x","label":"س","kind":"shop"}]}""", owner);
        Assert.False(created);
        Assert.Contains(CategoryKinds.Unknown, why, StringComparison.Ordinal);
        Assert.Equal(0, world.SaveCalls);

        var (fine, _) = await executor.ExecuteAsync("set_categories",
            """{"slug":"kinds-probe","categories":[{"slug":"x","label":"س","kind":"commercial"}]}""", owner);
        Assert.True(fine);
        Assert.Equal(1, world.SaveCalls);
    }

    /// <summary>(٣) صَفحَةُ الإنشاءِ الإدارِيَّة: تَمُرُّ بِالمُحَلِّلِ نَفسِه (‏<c>0cfc033</c>) وتُعيدُ رَمزَه، وصَفحَتُها تَقولُه — والمُحَرِّرانِ كَذلك.</summary>
    [Fact]
    public void The_three_pages_say_the_refusal_code_from_the_dictionary()
    {
        var body = AdminCreateCategoryParserCharacterizationTests.EndpointCallingTheParser();
        Assert.Contains("return Results.Redirect(Back(catsCode));", body, StringComparison.Ordinal);

        var pages = Path.Combine(Root, "libs", "templates", "ACommerce.Templates.Customer.Marketplace", "Components", "Pages");
        var checkedPages = 0;
        foreach (var rel in new[] { "StudioAppCategories.razor", Path.Combine("Admin", "TenantCategories.razor"), Path.Combine("Admin", "CreateTenant.razor") })
        {
            var text = File.ReadAllText(Path.Combine(pages, rel));
            Assert.Contains("Services.Patterns.CategoryKinds.Unknown", text, StringComparison.Ordinal);
            Assert.Contains("categories.kind.err_unknown", text, StringComparison.Ordinal);
            Assert.Contains("<CategoryKindSelect", text, StringComparison.Ordinal);
            Assert.DoesNotContain("list=\"kind-list\"", text, StringComparison.Ordinal);
            Assert.DoesNotContain("value=\"leisure\"", text, StringComparison.Ordinal);
            checkedPages++;
        }
        Assert.Equal(3, checkedPages);
        Assert.NotEqual("categories.kind.err_unknown", LocaleCatalog.Text(LocaleCatalog.Arabic, "categories.kind.err_unknown"));
    }

    // ─── الأَثَرُ المُعلَن: مِن القارِئِ لا مِن نَصّ ──────────────────────────────────────────

    [Fact]
    public void The_effect_of_each_kind_is_what_the_pattern_reader_does_and_each_has_its_texts()
    {
        foreach (var k in CategoryKinds.All)
        {
            var expected = k is "residential" or "commercial" ? AppPattern.Rental
                         : k is "roommate" ? AppPattern.Roommate
                         : AppPattern.Marketplace;
            Assert.Equal(expected, CategoryKinds.EffectOf(k));

            foreach (var key in new[] { CategoryKinds.NameKey(k), CategoryKinds.EffectKey(k) })
                Assert.NotEqual(key, LocaleCatalog.Text(LocaleCatalog.Arabic, key));
        }
        Assert.Equal("categories.kind_effect.rental", CategoryKinds.EffectKey("commercial"));
        Assert.Equal("categories.kind_effect.roommate", CategoryKinds.EffectKey("roommate"));
        Assert.Equal("categories.kind_effect.none", CategoryKinds.EffectKey("menu"));
        Assert.Equal("categories.kind.none", CategoryKinds.NameKey(""));
    }

    /// <summary>المُحَرِّرُ يَعرِضُ القيَمَ العَشرَ اختِياراً بِأَثَرِها، والمُخَزَّنُ مُختار؛ وقيمَةٌ مُخَزَّنَةٌ خارِجَ المَعجَمِ تُرسَمُ مُختارَةً بِوَسمِها لا تُبَدَّلُ صامِتَة.</summary>
    [Fact]
    public async Task The_editor_select_offers_every_kind_with_its_effect_and_marks_a_stored_unknown()
    {
        await using var services = new ServiceCollection()
            .AddSingleton(new L(new Microsoft.AspNetCore.Http.HttpContextAccessor()))
            .BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance);
        // ‏المُصَيِّرُ يُرَمِّزُ العَرَبِيَّةَ (`&#x...;`) — فَيُفَكُّ قَبلَ المُطابَقَة.
        Task<string> Render(string? value) => renderer.Dispatcher.InvokeAsync(async () =>
            System.Net.WebUtility.HtmlDecode((await renderer.RenderComponentAsync<ACommerce.Templates.Customer.Marketplace.Components.CategoryKindSelect>(
                ParameterView.FromDictionary(new Dictionary<string, object?> { ["Value"] = value }))).ToHtmlString()));

        static int Options(string html) => Regex.Matches(html, "<option ").Count;
        static string? Selected(string html) => Regex.Match(html, "<option value=\"([^\"]*)\" selected").Groups[1] is { Success: true } g ? g.Value : null;

        var fresh = await Render(null);
        Assert.Contains("class=\"ac-input ac-cat-kind\"", fresh, StringComparison.Ordinal);
        Assert.Equal(10, Options(fresh));
        Assert.Equal("", Selected(fresh));

        var commercial = await Render("commercial");
        Assert.Equal(10, Options(commercial));
        Assert.Equal("commercial", Selected(commercial));
        var rentalEffect = LocaleCatalog.Text(LocaleCatalog.Arabic, "categories.kind_effect.rental");
        Assert.Equal(2, Regex.Matches(commercial, Regex.Escape(rentalEffect)).Count);   // ‏سَكَنيّ وتِجاريّ

        var legacy = await Render("leisure");
        Assert.Equal(11, Options(legacy));
        Assert.Equal("leisure", Selected(legacy));
        Assert.Contains(LocaleCatalog.Text(LocaleCatalog.Arabic, "categories.kind.outside"), legacy, StringComparison.Ordinal);
    }

    // ─── قائِمَةُ الوَكيلِ تُقرَأُ مِن المَعجَم ─────────────────────────────────────────────────

    [Fact]
    public void The_agent_system_prompt_lists_exactly_the_lexicon()
    {
        var build = typeof(AgentService).GetMethod("BuildSystemPrompt",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(build);
        var prompt = (string)build!.Invoke(null, new object[] { "" })!;

        var listed = Regex.Matches(prompt, @"^   - (\S+?)(?: \(فارِغ\))?: ", RegexOptions.Multiline)
            .Select(m => m.Groups[1].Value == "\"\"" ? "" : m.Groups[1].Value)
            .Where(v => v.Length == 0 || CategoryKinds.All.Contains(v) || Regex.IsMatch(v, "^[a-z_]+$"))
            .ToList();
        var kinds = listed.Where(v => CategoryKinds.All.Contains(v)).ToList();
        output.WriteLine($"قيَمُ التَجميعِ في رِسالَةِ النِظام: {kinds.Count} — {string.Join("، ", kinds.Select(k => k.Length == 0 ? "\"\"" : k))}");
        Assert.Equal(CategoryKinds.All.ToArray(), kinds.ToArray());
        Assert.DoesNotContain("residential, commercial, events, vehicles", prompt, StringComparison.Ordinal);
    }

    // ─── كُتّابُ الكودِ الثابِتونَ لا يَكتُبونَ خارِجَ المَعجَم ─────────────────────────────────

    /// <summary>مَصنَعُ الاستوديو بِكُلِّ قِطاعاتِه، وبُذورُ المَنَصَّةِ بِكُلِّ صُفوفِها — كُلُّ تَجميعٍ يَكتُبانِه في المَعجَم.
    /// والعَدّادُ يُطبَعُ ويَحمَرُّ عِندَ الصِفر، وعَيِّنَةٌ مَحقونَةٌ تُثبِتُ أَنّ الماسِحَ يَرى الصَفّ (القاعِدَة ١٠).</summary>
    [Fact]
    public void Every_code_writer_writes_only_kinds_in_the_lexicon()
    {
        var sectors = typeof(TenantFromAnalysisFactory).GetMethod("CategoriesForSector",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(sectors);
        var factoryKinds = new[] { "ecommerce", "fnb", "realestate", "services", "transport", "", "unknown-sector" }
            .SelectMany(s => (IReadOnlyList<Category>)sectors!.Invoke(null, new object[] { s, "marketplace" })!)
            .Select(c => c.Kind).ToList();
        Assert.True(factoryKinds.Count >= 10, $"أَداةٌ عَمياء: {factoryKinds.Count} فِئَةً مِن المَصنَع.");
        Assert.All(factoryKinds, k => Assert.True(CategoryKinds.IsKnown(k), $"المَصنَعُ يَكتُبُ «{k}» خارِجَ المَعجَم."));

        var tuple = new Regex("\\(\"[a-z_]+\",\\s*\"[^\"]*\",\\s*\"[^\"]*\",\\s*\"([^\"]*)\"\\)");
        static List<string> Scan(Regex r, string text) => r.Matches(text).Select(m => m.Groups[1].Value).ToList();

        var seed = File.ReadAllText(Path.Combine(Root, "apps", "V1.App", "Seed", "PlatformSeed.cs"));
        var seedKinds = Scan(tuple, seed);
        output.WriteLine($"المَصنَع: {factoryKinds.Count} فِئَة · البُذور: {seedKinds.Count} صَفّاً — {string.Join("، ", seedKinds.Distinct())}");
        Assert.True(seedKinds.Count >= 10, $"أَداةٌ عَمياء: {seedKinds.Count} صَفَّ فِئَةٍ في PlatformSeed.");
        Assert.All(seedKinds, k => Assert.True(CategoryKinds.IsKnown(k), $"البُذورُ تَكتُبُ «{k}» خارِجَ المَعجَم."));

        // العَيِّنَةُ المَحقونَة: صَفٌّ بِتَجميعٍ خارِجَ المَعجَمِ يُرى ويُرَدّ.
        var injected = Scan(tuple, seed + "\n(\"park\", \"حَديقَة\", \"🌳\", \"leisure\"),");
        Assert.Equal(seedKinds.Count + 1, injected.Count);
        Assert.False(CategoryKinds.IsKnown(injected[^1]));
    }
}
