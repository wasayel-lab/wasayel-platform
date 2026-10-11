using ACommerce.Kit.Tenants;
using ACommerce.Templates.Customer.Marketplace.Services.Incubator;
using ACommerce.Templates.Customer.Marketplace.Services.Patterns;
using ACommerce.Templates.Customer.Marketplace.Services.TenantConfig;
using Marten;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ مَتجَرُ الاستوديو يولَدُ بِسَلَّة — الكاتِبُ لا يَكتُبُ كِنداً يَعني عَقاراً (‏ADR-034) ═══
//
// **المَقيسُ قَبل** (مُثَبَّتٌ في `PatternOfCharacterizationTests`، ولا يُمَسُّ حَرفٌ مِنه):
// القارِئُ `PatternOf` يَقرَأُ `commercial` إيجاراً بِلا سَلَّة، ولا يَرى إلّا الكِند.
// والكاتِبُ `CategoriesForSector("ecommerce")` كانَ يَكتُبُ `commercial` لِفِئَتَي
// `products`/`deals` — كَلِمَةَ العَقارِ التِجارِيِّ في مُعجَمِ المُستَودَع — فَكُلُّ مَتجَرٍ
// مِن `/studio/begin` كانَ يولَدُ إيجاراً: لا استِمارَةَ إلى `cart/add`، ولا تَبويبَ سَلَّة،
// ولا `/checkout` يُبلَغ. وقيسَ أَحمَرَ عَلى الكاتِبِ القَديمِ قَبلَ الإصلاح:
// `Expected: Marketplace — Actual: Rental`.
//
// **ما يَحرُسُه هذا المِلَفّ**: (١) المَصنَعُ الحَقيقيُّ بِمُدخَلاتِ مَسارِ العَميلِ يُخرِجُ
// مَتجَراً بِسَلَّة؛ (٢) كُلُّ طَريقٍ إلى قِطاعِ `ecommerce` كَذلك؛ (٣) الإصلاحُ مَحصورٌ في
// ذلكَ القِطاع — العَقارُ يَبقى إيجاراً والمَشاويرُ مَشاوير (القاعِدَة ١٣)؛ (٤) ما يَبقى
// لِلمالِك، مَقيساً لا مَوعوداً: مُحَرِّرُ الفِئاتِ يُبقي الكِندَ الفارِغَ ذَهاباً وإياباً،
// ومالِكٌ يَكتُبُ `commercial` في حَقلِ «التَّجميع» يُعيدُ مَتجَرَه إيجاراً ولا شَيءَ يَرُدُّه —
// فَمُصادِقُ الكِندِ ونَصُّ التَنبيهِ مُؤَجَّلانِ لِكَلِمَةِ المالِكِ عَلى ل‑١، لا مَنسِيّان.

public class StudioStorePatternTests
{
    private const string Slug = "probe-store";

    /// <summary>المَصنَعُ الحَقيقيُّ عَلى عالَمِ وَثائِقٍ في الذاكِرَة — <c>CreateAsync</c>
    /// بِحَرفِه: يُسَوّي النَمَط، ويَكتُبُ الفِئاتِ والأَدوار، ويُخَزِّنُ مَرَّةً واحِدَة.</summary>
    private static async Task<(Tenant Tenant, DocWorld World)> BuildAsync(string pattern, string sector)
    {
        var world = new DocWorld();
        var tenant = await new TenantFromAnalysisFactory(world.Store).CreateAsync(
            Slug, "مَتجَرُ الفَحص", "#2563eb", tagLine: "", city: "",
            pattern, sector, authChannel: "phone",
            ownerId: Guid.NewGuid(), sourceAnalysisId: Guid.NewGuid());
        return (tenant, world);
    }

    // ─── (١) مَسارُ العَميل: الكاتِبُ يُخرِجُ مَتجَراً بِسَلَّة ─────────────────

    /// <summary>
    /// <para><b>المَتجَرُ كَما يَبنيه <c>/studio/s/{id}/build</c> عَلى مَسارِ العَميلِ
    /// حَرفاً</b>: <c>session.SuggestedPattern</c> فارِغٌ، و<c>Answers</c> فارِغَةٌ فَالقِطاعُ
    /// <c>""</c>. يَخرُجُ بِفِئَتَي <c>products</c>/<c>deals</c> بِكِندٍ فارِغ، فَيَقرَؤُه
    /// <c>PatternOf</c> سوقاً: سَلَّةٌ وزِرُّ «أَضِف لِلسَلَّة» ولا تَحذيرَ عَقد.</para>
    /// </summary>
    [Fact]
    public async Task A_store_born_from_the_client_path_resolves_to_Marketplace_and_keeps_its_cart()
    {
        var (_, world) = await BuildAsync(pattern: new IncubatorSession().SuggestedPattern, sector: "");

        Assert.Equal(1, world.Wrote<Tenant>());
        Assert.Equal(1, world.SaveCalls);
        var stored = world.Read<Tenant>(Slug);
        Assert.NotNull(stored);

        Assert.Equal(new[] { "products", "deals" }, stored!.Categories.Select(c => c.Slug).ToArray());
        Assert.All(stored.Categories, c => Assert.Equal("", c.Kind));

        Assert.Equal(AppPattern.Marketplace, PatternProfileResolver.PatternOf(stored));
        var profile = PatternProfileResolver.Of(stored);
        Assert.True(profile.UsesCart);
        Assert.Equal("أَضِف لِلسَلَّة", profile.PrimaryCtaLabel);
        Assert.Null(profile.FraudWarning);
    }

    /// <summary><b>وكُلُّ طَريقٍ إلى قِطاعِ <c>ecommerce</c></b> — الفارِغُ والمَجهولُ
    /// و<c>marketplace</c> صَريحاً، والقِطاعُ مُعلَناً مِن المَسارِ الإداريِّ المُكتَمِل.</summary>
    [Theory]
    [InlineData("", "")]
    [InlineData("marketplace", "")]
    [InlineData("custom", "")]
    [InlineData("", "ecommerce")]
    [InlineData("marketplace", "ecommerce")]
    public async Task Every_road_to_the_ecommerce_sector_gives_a_store_with_a_cart(string pattern, string sector)
    {
        var (tenant, _) = await BuildAsync(pattern, sector);

        Assert.Equal(new[] { "products", "deals" }, tenant.Categories.Select(c => c.Slug).ToArray());
        Assert.Equal(AppPattern.Marketplace, PatternProfileResolver.PatternOf(tenant));
        Assert.True(PatternProfileResolver.Of(tenant).UsesCart);
    }

    // ─── (٢) الإصلاحُ مَحصورٌ في قِطاعٍ واحِد (القاعِدَة ١٣) ────────────────────

    /// <summary>
    /// <para><b>ما سِوى <c>ecommerce</c> يَخرُجُ كَما كان</b>: العَقارُ يَبقى إيجاراً
    /// (و<c>commercial</c> فيه بِمَعناه — «مَحَلّات»)، والمَشاويرُ مَشاوير، والمَطاعِمُ
    /// والخَدَماتُ وما لا يُعرَفُ قِطاعُه سوقٌ بِسَلَّة.</para>
    /// </summary>
    [Theory]
    [InlineData("rental", "", AppPattern.Rental)]
    [InlineData("classifieds", "", AppPattern.Rental)]
    [InlineData("marketplace", "realestate", AppPattern.Rental)]
    [InlineData("ondemand", "", AppPattern.Trip)]
    [InlineData("marketplace", "fnb", AppPattern.Marketplace)]
    [InlineData("marketplace", "services", AppPattern.Marketplace)]
    [InlineData("marketplace", "other", AppPattern.Marketplace)]
    public async Task The_fix_is_confined_to_the_ecommerce_sector(string pattern, string sector, AppPattern expected)
    {
        var (tenant, _) = await BuildAsync(pattern, sector);

        Assert.Equal(expected, PatternProfileResolver.PatternOf(tenant));

        // و`commercial` باقٍ حَيثُ يَعني عَقاراً تِجارِيّاً: مَحَلّاتُ قِطاعِ العَقار.
        if (tenant.Categories.Any(c => c.Slug == "shops"))
            Assert.Equal("commercial", tenant.Categories.Single(c => c.Slug == "shops").Kind);
    }

    // ─── (٣) ما يَبقى لِلمالِك — مَقيساً لا مَوعوداً ────────────────────────────

    /// <summary>
    /// <para><b>مُحَرِّرُ الفِئاتِ في الاستوديو يُبقي الكِندَ الفارِغَ ذَهاباً وإياباً</b>:
    /// الصَفحَةُ تَكتُبُ كُلَّ فِئَةٍ <c>{Slug} | {Label} | {Icon} | {Kind}</c> (‏
    /// <c>StudioAppCategories.razor</c> و<c>admin-editors.js</c> بِالصيغَةِ نَفسِها)،
    /// و<c>CategoriesSaveService.Parse</c> يُعيدُها — فَمالِكٌ يَحفَظُ فِئاتِه بِلا لَمسِ
    /// حَقلِ «التَّجميع» لا يَعودُ بِمَتجَرِه إيجاراً.</para>
    /// </summary>
    [Fact]
    public async Task The_category_editor_round_trip_keeps_the_empty_kind_and_the_cart()
    {
        var (tenant, _) = await BuildAsync(pattern: "", sector: "");

        var text = string.Join("\n", tenant.Categories.OrderBy(c => c.SortOrder)
            .Select(c => $"{c.Slug} | {c.Label} | {c.Icon ?? "tag"} | {c.Kind}"));
        var (saved, code) = CategoriesSaveService.Parse(text);

        Assert.Null(code);
        Assert.All(saved!, c => Assert.Equal("", c.Kind));

        tenant.Categories = saved!;
        Assert.Equal(AppPattern.Marketplace, PatternProfileResolver.PatternOf(tenant));
        Assert.True(PatternProfileResolver.Of(tenant).UsesCart);
    }

    /// <summary>
    /// <para><b>والمَخرَجُ الخَفِيُّ بِاتِّجاهِه المُعاكِس قائِم</b>: مالِكٌ يَكتُبُ
    /// <c>commercial</c> في حَقلِ «التَّجميع» — وهي أَوَّلُ اقتِراحَينِ في
    /// <c>datalist</c> الصَفحَة — يُعيدُ مَتجَرَه إيجاراً بِلا سَلَّة، ولا رَمزَ خَرقٍ
    /// يَرُدُّه ولا نَصَّ يُنَبِّهُه. مُثَبَّتٌ هُنا لِأَنّ ADR-034 يَقولُه «باقِياً لِلمالِك»:
    /// مُصادِقُ قيَمِ الكِندِ ونَصُّ التَنبيهِ خِياران مُؤَجَّلانِ بِحَرفِ ل‑١ (ج)، ومَن
    /// يَبني أَحَدَهُما يُحمِرُّ هذا الفَحصَ بِقَصدٍ ويَقلِبُه في كوميتِه.</para>
    /// </summary>
    [Fact]
    public void An_owner_typing_commercial_in_the_grouping_field_turns_the_store_back_to_Rental_and_nothing_refuses_it()
    {
        var (saved, code) = CategoriesSaveService.Parse(
            "products | مُنتَجات | 🛍️ | commercial\ndeals | عُروض | 🏷️ | Commercial");

        Assert.Null(code);
        Assert.All(saved!, c => Assert.Equal("commercial", c.Kind));

        var store = new Tenant
        {
            Id = Slug,
            Roles = TenantFromAnalysisFactory.RolesFor("").ToList(),
            Categories = saved!,
        };
        Assert.Equal(AppPattern.Rental, PatternProfileResolver.PatternOf(store));
        Assert.False(PatternProfileResolver.Of(store).UsesCart);
    }

    /// <summary>
    /// <para><b>ومَخرَجٌ مُعاكِسٌ ثانٍ — كاتِبٌ ثانٍ يَبلُغُه المالِك</b> (‏2026-10-11، مُراجَعَةُ
    /// <c>c4278c0</c>): وَكيلُ الاستوديو (<c>/studio/agent</c>) يَعرِضُ في مُوَجِّهِ نِظامِه
    /// <c>commercial</c> تَجميعاً لِلفِئات، ومُخَطَّطُ الفِئَةِ يَقبَلُ أَيَّ نَصٍّ في <c>kind</c>، و
    /// <c>ParseCategories</c> يَكتُبُه بِحُروفٍ صَغيرَةٍ بِلا فَحص — في <c>create_tenant</c> و
    /// <c>set_categories</c> كِلَيهِما، بِهُوِيَّةِ المالِكِ مِن الصَفحَة. فَمَتجَرُ مُنتَجاتٍ يُبنى أَو
    /// يُعَدَّلُ بِالوَكيلِ قَد يولَدُ إيجاراً بِلا سَلَّة.</para>
    /// <para>مُثَبَّتٌ بِجِوارِ المُحَرِّرِ لِأَنَّه قَرارُ ل‑١ المُؤَجَّلُ نَفسُه (مُصادِقُ قيَمِ الكِند):
    /// مَن يَبنيه يُحمِرُّ الفَحصَينِ مَعاً بِقَصدٍ ويَقلِبُهُما في كوميتِه (‏ADR-034 §٣‑٢ مُلحَق).</para>
    /// </summary>
    [Fact]
    public void The_studio_agent_offers_commercial_as_a_grouping_and_writes_any_kind_so_its_product_store_turns_Rental()
    {
        var template = Path.Combine(ThemeZeroEquivalenceTests.RepoRoot,
            "libs", "templates", "ACommerce.Templates.Customer.Marketplace");
        var agent = File.ReadAllText(Path.Combine(template, "Services", "AgentService.cs"));
        Assert.Contains("الفِئات تُجَمَّع بِـ \"kind\": residential, commercial, events, vehicles,",
            agent, StringComparison.Ordinal);
        Assert.Contains("Kind  = TryStr(c, \"kind\", out var k) ? k.ToLowerInvariant() : \"\",",
            agent, StringComparison.Ordinal);
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(
            agent, System.Text.RegularExpressions.Regex.Escape("var cats = ParseCategories(root);")).Count);

        var page = File.ReadAllText(Path.Combine(template, "Components", "Pages", "StudioAgent.razor"));
        Assert.Contains("await Executor.ExecuteAsync(turn.Tool.Name, turn.Tool.InputJson, _userId);",
            page, StringComparison.Ordinal);

        const string payload = """{"slug":"probe-store","categories":[{"slug":"products","label":"مُنتَجات","icon":"🛍️","kind":"Commercial"},{"slug":"deals","label":"عُروض","icon":"🏷️","kind":"commercial"}]}""";
        var verdict = ACommerce.Templates.Customer.Marketplace.Services.AgentToolValidator.Validate("set_categories", payload);
        Assert.True(verdict.IsValid, string.Join(" | ", verdict.Errors));

        var store = new Tenant
        {
            Id = Slug,
            Roles = TenantFromAnalysisFactory.RolesFor("").ToList(),
            Categories = new()
            {
                new Category { Slug = "products", Label = "مُنتَجات", Kind = "commercial", SortOrder = 0 },
                new Category { Slug = "deals",    Label = "عُروض",    Kind = "commercial", SortOrder = 1 },
            },
        };
        Assert.Equal(AppPattern.Rental, PatternProfileResolver.PatternOf(store));
        Assert.False(PatternProfileResolver.Of(store).UsesCart);
    }
}

// ═══ البُرهانُ عَلى Postgres — الكاتِبُ الحَقيقيُّ ثُمَّ القارِئُ عَلى ما قُرِئَ مِن القاعِدَة ═══
//
// يَتَخَطّى نَفسَه ما لَم تُضبَط WASAYEL_LIVE_PROOF=1 و ConnectionStrings__Postgres، بِنَفسِ
// عَقدِ `LiveDealNoticesProofTests`. المَقيسُ **ذَهابُ الوَثيقَةِ وإيابُها**: المَصنَعُ يَكتُبُ
// المَتجَرَ بِمُدخَلاتِ مَسارِ العَميل، وجَلسَةٌ أُخرى تَقرَؤُه مِن القاعِدَة، و`PatternOf`
// يُحكَمُ عَلى المَقروءِ لا عَلى ما في الذاكِرَة. ويُحذَفُ المَتجَرُ بَعدَه.
// وما لا يُبَرهِنُه — ويُقال: النَقرَةُ عَلى زِرِّ «أَضِف لِلسَلَّة» في مُتَصَفِّحٍ عَلى
// خادِمٍ حَيّ (‏FLGAP-04 بِحَرفِه) — ذاكَ يَحتاجُ التَطبيقَ كُلَّه، وأَمرُه في سِجِلِّ المالِك «ب».

public class LiveStudioStorePatternProofTests(ITestOutputHelper output)
{
    private static bool Enabled =>
        Environment.GetEnvironmentVariable("WASAYEL_LIVE_PROOF") == "1";

    private static string? ConnectionString =>
        Environment.GetEnvironmentVariable("ConnectionStrings__Postgres");

    private static string ResilientConnection =>
        ConnectionString!.TrimEnd(';') + ";Timeout=60;Command Timeout=120";

    private static DocumentStore BuildStore() => DocumentStore.For(o =>
    {
        o.Connection(ResilientConnection);
        o.DatabaseSchemaName = "platform";
        o.Policies.AllDocumentsAreMultiTenanted();
        o.Events.TenancyStyle = global::JasperFx.MultiTenancy.TenancyStyle.Conjoined;
        o.Schema.For<Tenant>().SingleTenanted().Identity(x => x.Id);
        o.AutoCreateSchemaObjects = JasperFx.AutoCreate.All;
    });

    [Fact]
    public async Task A_store_written_by_the_real_factory_reads_back_from_Postgres_as_Marketplace_with_a_cart()
    {
        if (!Enabled || string.IsNullOrEmpty(ConnectionString)) return;

        await using var store = BuildStore();
        var factory = new TenantFromAnalysisFactory(store);
        var slug = $"flgap04-{Guid.NewGuid():N}"[..20];

        Assert.Null(await factory.ValidateSlugAsync(slug));
        await factory.CreateAsync(slug, "بُرهانُ FLGAP-04", "#2563eb", tagLine: "", city: "",
            pattern: "", sector: "", authChannel: "phone",
            ownerId: Guid.NewGuid(), sourceAnalysisId: Guid.NewGuid());

        try
        {
            await using var read = store.QuerySession();
            var tenant = await read.LoadAsync<Tenant>(slug);
            Assert.NotNull(tenant);

            var kinds = string.Join(",", tenant!.Categories.Select(c => $"{c.Slug}:'{c.Kind}'"));
            var roles = string.Join(",", tenant.Roles.Select(r => r.CatalogSlug));
            var pattern = PatternProfileResolver.PatternOf(tenant);
            var profile = PatternProfileResolver.Of(tenant);
            output.WriteLine($"[live] slug={slug} categories={kinds} roles={roles}");
            output.WriteLine($"[live] PatternOf={pattern} UsesCart={profile.UsesCart} cta=«{profile.PrimaryCtaLabel}»");

            Assert.Equal(AppPattern.Marketplace, pattern);
            Assert.True(profile.UsesCart);
        }
        finally
        {
            await using var ws = store.LightweightSession();
            ws.Delete<Tenant>(slug);
            await ws.SaveChangesAsync();
        }
    }
}
