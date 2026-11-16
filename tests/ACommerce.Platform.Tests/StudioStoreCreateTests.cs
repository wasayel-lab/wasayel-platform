using System.Text.RegularExpressions;
using ACommerce.Kit.Auth;
using ACommerce.Kit.Auth.Server;
using ACommerce.Kit.Tenants;
using ACommerce.Templates.Customer.Marketplace.Services.Incubator;
using ACommerce.Templates.Customer.Marketplace.Services.Patterns;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Primitives;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ إنشاءُ المَتجَرِ بِاليَد — البابُ والاستِمارَةُ والطَريقُ إلَيها (‏2026-11-07، الموجَة ١٣ W13-1·٣ — ADR-049 §٤ و§٥) ═══
//
// **المَقيسُ قَبل**: لا بابَ يَملِكُ مِنه رائِدُ الأَعمالِ مَتجَراً إلّا `/studio/s/{id}/build`، وشَرطُه تَحليلٌ `Completed` بِمِفتاحٍ — والإنتاجُ بِلا
// مِفتاح (‏401). والروابِطُ السِتَّةُ إلى `/studio/new` داخِلَ بابِ التَحليل، فَلا تُرسَمُ بِلا مِفتاح (‏`StudioAnalysisDoorTests` كانَ يُثَبِّتُ ذلك).
// **وما يُثَبِّتُه هذا المِلَفّ**: (١) تَرتيبُ حَرَسِ `POST /studio/apps/create` وأَنّ التَخويلَ يَسبِقُ قِراءَةَ الحُقول (القاعِدَة ٦)؛ (٢) لِكُلِّ رَمزِ
// رَفضٍ صَفٌّ موجَبٌ وصَفٌّ سالِب (القاعِدَة ٤)، ونَصُّه في القامُوسِ العَرَبيّ؛ (٣) المُوافَقَةُ قَبلَ الاستِمارَةِ وقَبلَ الإنشاء؛ (٤) كُلُّ رابِطٍ إلى
// `/studio/new` مَرسومٌ بِلا باب (القاعِدَة ١٢)؛ وعَلى Postgres: المَتجَرُ الَّذي يَكتُبُه البابُ هُوَ بَصمَةُ التَوصيفِ بِعَينِها، والصَفحَةُ تَرسُمُ
// الاستِمارَةَ لِلمُوافِقِ وتُحَوِّلُ غَيرَه إلى المُوافَقَة.

public class StudioStoreCreateTests(ITestOutputHelper output)
{
    private const string Template = "libs/templates/ACommerce.Templates.Customer.Marketplace";

    private static string Read(string rel) => File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, rel));

    private static WriteEndpointGuardTests.Endpoint CreateEndpoint()
        => WriteEndpointGuardTests.AllMinimalApiEndpoints().Single(e => e.Route == StudioStoreCreate.Endpoint);

    // ─── (١) البابُ — تَرتيبُ حَرَسِه في أَوَّلِ أَسطُرِه ───────────────────────────────────────────────

    /// <summary>الهُوِيَّة ← المُوافَقَةُ بِالإصدارِ القائِم ← الحِصَّة ← قِراءَةُ الحُقول ← المُعَرِّف ← الاسم ← اللَون ← القَناة ← الإنشاء ← العَدّ
    /// ← التَحويلُ إلى لَوحَةِ المَتجَر. و<c>req.Form</c> لا يُقرَأُ قَبلَ الحِصَّة: خَطَأُ حَقلٍ لا يَكونُ قِناعاً لِبابٍ مَفتوح.
    /// ‏(‏2026-11-07، مُراجَعَةُ عَدَساتِ C1 · W13-1·SEC-C1-1 — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه) عَلامَتا الإنشاءِ والعَدِّ تَبِعَتا الكود: كانَ «الإنشاء» حِفظاً
    /// و«العَدّ» <c>tier.RecordStoreBuiltAsync(ownerId)</c> حِفظاً ثانِياً؛ وصارَ المَصنَعُ يُعيدُ الوَثيقَةَ (<c>write: false</c>) ويُدرِجُها العَدُّ في حِفظِه
    /// (<c>tier.RecordStoreBuiltAsync(ownerId, s => s.Insert(tenant))</c>). والتَرتيبُ المَفحوصُ هُوَ هُوَ.</summary>
    [Fact]
    public void The_create_door_asks_identity_consent_and_quota_before_it_reads_a_single_field()
    {
        var body = CreateEndpoint().Body;
        string[] order =
        [
            "auth.IsAuthenticated",
            "studio.HasConsentedAsync(ownerId, Services.Incubator.ConsentPolicy.CurrentVersion)",
            "tier.CheckBuildAsync(ownerId)",
            "req.Form",
            "factory.ValidateSlugAsync(form.Slug)",
            "StudioStoreCreate.NameRequired",
            "StudioStoreCreate.ColorInvalid",
            "TenantAuthChannelDoor.Default(",
            "factory.CreateAsync(",
            "write: false",
            "tier.RecordStoreBuiltAsync(ownerId, s => s.Insert(tenant))",
            "/studio/apps/{form.Slug}?built=1",
        ];
        var at = order.Select(o => body.IndexOf(o, StringComparison.Ordinal)).ToArray();
        output.WriteLine(string.Join(" ← ", order.Zip(at, (o, p) => $"{o}@{p}")));
        Assert.All(at, p => Assert.True(p >= 0));
        Assert.True(at.Zip(at.Skip(1)).All(p => p.First < p.Second), "حَرَسُ بابِ الإنشاءِ مَقلوب.");
        Assert.Equal(1, Regex.Matches(body, Regex.Escape("req.Form")).Count);
        var brace = body.IndexOf('{', body.IndexOf("=>", StringComparison.Ordinal));
        Assert.StartsWith("auth.Load();", body[(brace + 1)..].TrimStart(), StringComparison.Ordinal);
    }

    /// <summary>المَصنَعُ يُنادى بِنَمَطٍ وقِطاعٍ ومَدينَةٍ فارِغَةٍ وبِلا مَصدَر — مُدخَلاتُ <c>/build</c> لِجَلسَةٍ بِالوَصفِ وَحدَه، فَالمَتجَرُ بَصمَةُ التَوصيف.</summary>
    [Fact]
    public void The_create_door_calls_the_one_factory_with_the_description_only_inputs()
    {
        var body = CreateEndpoint().Body;
        var call = Regex.Match(body, @"factory\.CreateAsync\((?<args>[^;]*)\);").Groups["args"].Value;
        output.WriteLine(call);
        foreach (var arg in new[] { "city: \"\"", "pattern: \"\"", "sector: \"\"", "authChannel: channel", "ownerId: ownerId", "sourceAnalysisId: null" })
            Assert.Contains(arg, call, StringComparison.Ordinal);
        Assert.DoesNotContain("IDocumentStore", body[..body.IndexOf("=>", StringComparison.Ordinal)], StringComparison.Ordinal);
    }

    /// <summary>المَساراتُ مِن ثابِتٍ واحِد: البابُ حَرفِيٌّ لِلماسِحات ويُطابِقُ الثابِت، والاستِمارَةُ تُرسِلُ إلَيه، و<c>@page</c> هُوَ <see cref="StudioStoreCreate.Page"/>.</summary>
    [Fact]
    public void The_form_posts_to_the_door_and_the_page_route_is_the_one_constant()
    {
        Assert.Equal("/studio/apps/create", StudioStoreCreate.Endpoint);
        Assert.Equal("/studio/new", StudioStoreCreate.Page);
        var page = Read($"{Template}/Components/Pages/StudioNewStore.razor");
        Assert.Contains("@page \"/studio/new\"", page, StringComparison.Ordinal);
        Assert.Contains("<form method=\"post\" action=\"@StudioStoreCreate.Endpoint\"", page, StringComparison.Ordinal);
        Assert.Equal(1, Regex.Matches(page, "<form").Count);
    }

    // ─── (٢) رُموزُ الرَفض — موجَبٌ وسالِبٌ لِكُلِّ رَمز ────────────────────────────────────────────────

    [Fact]
    public void The_refusal_lexicon_is_closed_and_every_code_has_arabic_text()
    {
        using var doc = System.Text.Json.JsonDocument.Parse(Read("libs/core/ACommerce.Platform.I18n/Locales/ar.json"));
        var missing = StudioStoreCreate.Refusals.Where(c => !doc.RootElement.TryGetProperty(StudioStoreCreate.MessageKey(c), out var v)
                                                        || string.IsNullOrWhiteSpace(v.GetString())).ToList();
        output.WriteLine($"رُموزُ الرَفض: {StudioStoreCreate.Refusals.Count} — " + string.Join(" · ", StudioStoreCreate.Refusals));
        // ‏(‏2026-11-07، W13-1·SEC-C1-3 — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه) سَبعَةٌ ← تِسعَة: `name_too_long` و`tagline_too_long` — حَدّا الاستِمارَةِ صارا في الخادِم.
        Assert.Equal(9, StudioStoreCreate.Refusals.Count);
        Assert.Equal(StudioStoreCreate.Refusals.Count, StudioStoreCreate.Refusals.Distinct().Count());
        Assert.Empty(missing);
    }

    private static TenantFromAnalysisFactory FactoryWithTaken(string taken)
    {
        var world = new DocWorld();
        world.Put(new Tenant { Id = taken, Name = "قائِم" });
        return new TenantFromAnalysisFactory(world.Store);
    }

    [Theory]
    [InlineData("", TenantFromAnalysisFactory.SlugRequired)]
    [InlineData("Bad Slug!", TenantFromAnalysisFactory.SlugFormat)]
    [InlineData("pricing", TenantFromAnalysisFactory.SlugReserved)]
    [InlineData("taken-store", TenantFromAnalysisFactory.SlugTaken)]
    // ‏(‏2026-11-07، مُراجَعَةُ عَدَساتِ C1 · W13-1·SEC-C1-2 — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه) «a_1» كانَ صَفّاً موجَباً في الاختِبارِ التالي: الشَكلُ
    // `^[a-z0-9_-]+$` يَقبَلُ «_» وهُوَ لَيسَ مِلصَقَ مُضيف. والشَكلُ الواحِدُ لِلأَبوابِ الثَلاثَةِ يَرُدُّه — فَانتَقَلَ إلى هُنا بِرَمزِه.
    [InlineData("a_1", TenantFromAnalysisFactory.SlugFormat)]
    public async Task A_slug_breach_is_refused_with_its_code(string slug, string code)
        => Assert.Equal(code, await FactoryWithTaken("taken-store").ValidateSlugAsync(slug));

    [Theory]
    [InlineData("my-store")]
    [InlineData("pricing-house")]
    [InlineData("taken-store-2")]
    [InlineData("a-1")]
    public async Task A_valid_free_slug_passes_the_one_slug_door(string slug)
        => Assert.Null(await FactoryWithTaken("taken-store").ValidateSlugAsync(slug));

    // ─── (‏2026-11-07، W13-1·SEC-C1-3) حَدّا الطولِ في الخادِم: موجَبٌ وسالِبٌ لِكُلِّ رَمز، والرَفضُ لا يَنسَخُ ما فاتَ حَدَّه ────────────────

    [Fact]
    public void The_door_bounds_the_name_and_the_tagline_after_the_name_and_colour_and_before_the_channel()
    {
        var body = WriteEndpointGuardTests.StripComments(CreateEndpoint().Body);
        string[] order = ["StudioStoreCreate.NameRequired", "StudioStoreCreate.ColorInvalid", "StudioStoreCreate.LengthRefusal(form)", "TenantAuthChannelDoor.Default("];
        var at = order.Select(o => body.IndexOf(o, StringComparison.Ordinal)).ToArray();
        output.WriteLine(string.Join(" ← ", order.Zip(at, (o, p) => $"{o}@{p}")));
        Assert.True(at.All(p => p > 0) && at.Zip(at.Skip(1)).All(p => p.First < p.Second), "حَدّا الطولِ غائِبانِ أَو في غَيرِ مَوضِعِهِما.");
    }

    [Theory]
    [InlineData(StudioStoreCreate.NameMax, StudioStoreCreate.TagLineMax, null)]
    [InlineData(StudioStoreCreate.NameMax + 1, 0, StudioStoreCreate.NameTooLong)]
    [InlineData(1, StudioStoreCreate.TagLineMax + 1, StudioStoreCreate.TagLineTooLong)]
    [InlineData(StudioStoreCreate.NameMax + 1, StudioStoreCreate.TagLineMax + 1, StudioStoreCreate.NameTooLong)]
    public void The_length_refusal_names_the_first_field_past_its_limit(int name, int tagline, string? code)
        => Assert.Equal(code, StudioStoreCreate.LengthRefusal(new StudioStoreCreate.Form("s", new string('ب', name), "#2563eb", new string('س', tagline))));

    [Fact]
    public void The_form_limits_are_the_numbers_the_form_states()
    {
        var page = Read($"{Template}/Components/Pages/StudioNewStore.razor");
        Assert.Contains($"name=\"name\" required maxlength=\"{StudioStoreCreate.NameMax}\"", page, StringComparison.Ordinal);
        Assert.Contains($"name=\"tagline\" maxlength=\"{StudioStoreCreate.TagLineMax}\"", page, StringComparison.Ordinal);
    }

    [Fact]
    public void A_refusal_copies_only_fields_within_their_limits_so_its_location_stays_bounded()
    {
        var huge = StudioStoreCreate.Refused(StudioStoreCreate.NameTooLong, new StudioStoreCreate.Form(new string('a', 9000), new string('ب', 200_000), "#2563eb", new string('س', 500)));
        var fine = StudioStoreCreate.Refused(StudioStoreCreate.NameTooLong, new StudioStoreCreate.Form("my-store", "مَتجَري", "#2563eb", "سَطر"));
        output.WriteLine($"الرَفضُ بِحُقولٍ فَوقَ حُدودِها: {huge.Length} حَرفاً — {huge}");
        Assert.Equal("/studio/new?err=name_too_long&slug=&name=&color=%232563eb&tagline=", huge);
        Assert.Contains("&slug=my-store&name=", fine, StringComparison.Ordinal);
        Assert.Contains(Uri.EscapeDataString("مَتجَري"), fine, StringComparison.Ordinal);
    }

    private static StudioStoreCreate.Form FormOf(string slug, string name, string color, string tagline)
        => StudioStoreCreate.Read(new FormCollection(new Dictionary<string, StringValues>
        { ["slug"] = slug, ["name"] = name, ["color"] = color, ["tagline"] = tagline }));

    [Theory]
    [InlineData("", true)]
    [InlineData("   ", true)]
    [InlineData("مَتجَري", false)]
    public void An_empty_name_is_name_required_and_a_named_store_is_not(string name, bool refused)
        => Assert.Equal(refused, string.IsNullOrEmpty(FormOf("s", name, "#000000", "").Name));

    [Theory]
    [InlineData("blue", false)]
    [InlineData("#12345", false)]
    [InlineData("#12345g", false)]
    [InlineData("", false)]
    [InlineData("#2563eb", true)]
    [InlineData("#ABCDEF", true)]
    public void Only_a_six_digit_hex_colour_passes(string color, bool valid)
        => Assert.Equal(valid, StudioStoreCreate.ColorValid(color));

    [Theory]
    [InlineData(false, false, false, null)]
    [InlineData(true, false, false, "phone")]
    [InlineData(false, false, true, "email")]
    public void No_registered_sign_in_channel_is_no_auth_channel_and_one_channel_is_chosen(bool phone, bool nafath, bool email, string? chosen)
        => Assert.Equal(chosen, TenantAuthChannelDoor.Default(TenantAuthChannelDoor.Offered(phone, nafath, email)));

    /// <summary>القِراءَةُ كَما كانَ <c>/build</c> يَقرَأ (المُعَرِّفُ بِحُروفٍ صَغيرَة، والفَراغُ الحافُّ يَسقُط)، والرَفضُ يَعودُ بِرَمزِه وبِما كُتِبَ مُرَمَّزاً.</summary>
    [Fact]
    public void The_form_is_read_like_build_read_it_and_a_refusal_returns_what_was_typed()
    {
        var form = FormOf("  My-Store ", " مَتجَري ", " #2563EB ", " سَطرٌ & وَصف ");
        Assert.Equal(new StudioStoreCreate.Form("my-store", "مَتجَري", "#2563EB", "سَطرٌ & وَصف"), form);
        var url = StudioStoreCreate.Refused(StudioStoreCreate.ColorInvalid, form);
        output.WriteLine(url);
        Assert.StartsWith("/studio/new?err=color_invalid&slug=my-store&name=", url, StringComparison.Ordinal);
        Assert.Contains("&tagline=" + Uri.EscapeDataString("سَطرٌ & وَصف"), url, StringComparison.Ordinal);
        Assert.DoesNotContain(" ", url, StringComparison.Ordinal);
    }

    // ─── (٣) المُوافَقَةُ قَبلَ الاستِمارَةِ وقَبلَ الإنشاء ───────────────────────────────────────────────

    /// <summary>الصَفحَةُ تَسأَلُ المُسنَدَ الواحِدَ (<c>HasConsentedAsync</c> بِـ<c>ConsentPolicy.CurrentVersion</c>) وتُحَوِّلُ غَيرَ المُوافِقِ إلى المُوافَقَةِ
    /// بِوِجهَةِ عَودَةٍ إلَيها، والاستِمارَةُ في الفَرعِ الآخَرِ وَحدَه؛ ونُقطَةُ القَبولِ تُعيدُه إلى <c>returnUrl</c>.</summary>
    /// <remarks>‏(‏2026-11-07، مُراجَعَةُ عَدَساتِ C1 · W13-1·C1D-1 — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه) كانَ التَحويلُ <c>&lt;script&gt;location.replace(…)</c> في
    /// فَرعِ <c>@if (Auth.IsAuthenticated &amp;&amp; !consented)</c> وهذا الصَفُّ يَقرَؤُه بِحَرفِه — ولا يَجري تَحتَ التَنَقُّلِ المُحَسَّن (صَفحَةٌ فارِغَة). وصارَ
    /// <c>Nav.NavigateTo</c> في <c>OnInitializedAsync</c> لِلزائِرِ ولِغَيرِ المُوافِق، والاستِمارَةُ في فَرعِ <c>@if (consented)</c>، ولا سكربتَ في الصَفحَة.
    /// ‏(‏2026-11-11، W13-4·C2f-R2·1 — R2-SEC-1 — يُلحَق) والوِجهَتانِ مُطلَقَتانِ بِمُخَطَّطٍ مِن {http, https} (<c>OnThisOrigin</c>) — التَرتيبُ نَفسُه، والإبرَتانِ
    /// تَتبَعانِ النِداءَ؛ وبايتاتُ http وhttps يُثَبِّتُها <c>StudioNewRedirectCharacterizationTests</c>.</remarks>
    [Fact]
    public void The_page_sends_an_unconsented_user_to_consent_and_back_before_it_draws_the_form()
    {
        var page = WriteEndpointGuardTests.StripMarkupComments(Read($"{Template}/Components/Pages/StudioNewStore.razor"));
        var code = page[page.IndexOf("@code", StringComparison.Ordinal)..];
        var (anonymous, asks, unconsented) = (code.IndexOf("if (!Auth.IsAuthenticated) { Nav.NavigateTo(OnThisOrigin(StudioAuthPage)); return; }", StringComparison.Ordinal),
            code.IndexOf("consented = await Studio.HasConsentedAsync(uid, ConsentPolicy.CurrentVersion);", StringComparison.Ordinal),
            code.IndexOf("if (!consented) { Nav.NavigateTo(OnThisOrigin(StudioStoreCreate.ConsentPage)); return; }", StringComparison.Ordinal));
        Assert.True(anonymous > 0 && asks > anonymous && unconsented > asks, "التَحويلُ مِن الخادِمِ ناقِصٌ أَو مَقلوب.");
        var branch = page.IndexOf("@if (consented)", StringComparison.Ordinal);
        var form = page.IndexOf("<form", StringComparison.Ordinal);
        Assert.True(branch > 0 && form > branch, "الاستِمارَةُ خارِجَ فَرعِ المُوافِق.");
        Assert.DoesNotContain("<script", page, StringComparison.Ordinal);
        Assert.Equal("/studio/consent?returnUrl=/studio/new", StudioStoreCreate.ConsentPage);
        // ونُقطَةُ القَبولِ تُعيدُ إلى الوِجهَةِ المَحلِّيَّةِ كَما هي (`LocalRedirect` يَقبَلُها).
        Assert.Equal("/studio/new", ACommerce.Templates.Customer.Marketplace.Services.LocalRedirect.Resolve("/studio/new", "/studio"));
    }

    // ─── (٤) القاعِدَة ١٢ — كُلُّ رابِطٍ إلى الاستِمارَةِ مَرسومٌ بِلا باب ───────────────────────────────

    private static readonly Regex CreatePageToken = new(@"[""']/studio/new(?=[""'?#/])", RegexOptions.Compiled);
    private static readonly Regex AnyDoorBlock = new(@"@if\s*\((?:Services\.Incubator\.)?Studio\w+Door\.Opens\(", RegexOptions.Compiled);

    /// <summary>كُلُّ ذِكرٍ لِصَفحَةِ الإنشاءِ في مَصادِرِ <c>libs</c>/<c>apps</c> (عَدا <c>@page</c> والتَعليقات) — وهَل هُوَ داخِلَ كُتلَةِ بابٍ (<c>@if (Studio…Door.Opens(</c>).</summary>
    internal static List<(string File, bool InsideDoor)> CreatePageLinksIn(IEnumerable<(string File, string Text)> sources)
    {
        var links = new List<(string, bool)>();
        foreach (var (file, text) in sources)
        {
            var rel = Path.IsPathRooted(file) ? Path.GetRelativePath(ThemeZeroEquivalenceTests.RepoRoot, file).Replace('\\', '/') : file;
            var razor = rel.EndsWith(".razor", StringComparison.OrdinalIgnoreCase);
            var code = razor ? WriteEndpointGuardTests.StripMarkupComments(text) : WriteEndpointGuardTests.StripComments(text);
            if (!code.Contains("/studio/new", StringComparison.Ordinal)) continue;
            var blocks = new List<(int Open, int Close)>();
            if (razor)
                foreach (Match d in AnyDoorBlock.Matches(code))
                {
                    var open = code.IndexOf('{', d.Index);
                    int depth = 0, i = open;
                    for (; i < code.Length; i++) { if (code[i] == '{') depth++; else if (code[i] == '}' && --depth == 0) break; }
                    blocks.Add((open, i));
                }
            foreach (Match m in CreatePageToken.Matches(code))
            {
                if (m.Index >= 6 && string.CompareOrdinal(code, m.Index - 6, "@page ", 0, 6) == 0) continue;
                links.Add((rel, blocks.Any(b => b.Open < m.Index && m.Index < b.Close)));
            }
        }
        return links;
    }

    [Fact]
    public void Every_link_to_the_create_page_is_drawn_without_a_door()
    {
        var links = CreatePageLinksIn(EntitlementContractTests.SourceFiles());
        var inside = links.Where(l => l.InsideDoor).ToList();
        output.WriteLine($"روابِطُ /studio/new: {links.Count} · داخِلَ بابٍ: {inside.Count} ("
            + string.Join(" · ", links.GroupBy(l => l.File).Select(g => $"{g.Key}: {g.Count()}")) + ")");
        Assert.True(links.Count >= 6, $"أَداةٌ عَمياء: {links.Count} رابِطاً — والمَقيسُ 6.");
        Assert.Empty(inside);
    }

    /// <summary>والماسِحُ يُقاسُ (القاعِدَة ١٠): رابِطٌ داخِلَ بابِ التَحليلِ أَو بابِ الوَكيلِ يُعَدُّ داخِلاً، وخارِجَهُما خارِجاً، و<c>@page</c> والتَعليقُ لا يُعَدّان.</summary>
    [Fact]
    public void The_create_link_scanner_sees_a_link_hidden_behind_any_door()
    {
        var sources = new[]
        {
            ("x/A.razor", "@page \"/studio/new\"\n@if (StudioAnalysisDoor.Opens(Incubator.IsConfigured))\n{\n    <a href=\"/studio/new\">a</a>\n}\n<a href=\"/studio/new?x=1\">b</a>\n@* <a href=\"/studio/new\">c</a> *@\n"),
            ("x/B.razor", "@if (StudioAgentDoor.Opens(Agent.IsConfigured, isPlatformAdmin))\n{\n    <a href='/studio/new'>d</a>\n}\n"),
            ("x/C.cs", "class C { object R() => Results.Redirect(\"/studio/new\"); }"),
        };
        var links = CreatePageLinksIn(sources);
        Assert.Equal(4, links.Count);
        Assert.Equal(2, links.Count(l => l.InsideDoor));
    }
}

// ═══ عَلى Postgres — البابُ المُسَجَّلُ بِحَرَسِه، والصَفحَةُ بِخِدماتِها ═══════════════════════════════════════════════
//
// يَحِلّانِ مَحَلَّ بُرهانَي التَحليلِ الحَيَّين (‏`LiveAnalysisClaimRaceProofTests`، `LiveRefineInterleaveProofTests`) حينَ يَخرُجانِ في W13-1·٥،
// وأَرضِيَّةُ CI ‏292 لا تَنزِل. قَناةُ الهاتِفِ بَديلٌ لا يُرسِل، فَيَشتَقُّ البابُ `phone` كَما عَلى نُسخَةٍ بِـSMS.

[Collection(LiveProofCollection.Name)]
public class LiveStudioStoreCreateProofTests(ITestOutputHelper output)
{
    private static bool Enabled => LiveDealConcurrencyProofTests.Enabled && !string.IsNullOrEmpty(LiveDealConcurrencyProofTests.ConnectionString);

    private sealed record Probe(StudioUser User, string Token);

    private static async Task<Probe> UserAsync(LiveTemplateDoorHost host, bool consented, int storesBuilt = 0)
    {
        var user = new StudioUser { Id = Guid.NewGuid(), Phone = "zz-w13c-" + Guid.NewGuid().ToString("N")[..8], FullName = "صاحِبُ المَتجَر", StoresBuilt = storesBuilt };
        await using var s = host.Store.LightweightSession(StudioAuth.Tenant);
        s.Store(user);
        if (consented)
            s.Store(new ConsentRecord { Id = Guid.NewGuid(), UserId = user.Id, Version = ConsentPolicy.CurrentVersion, At = DateTime.UtcNow });
        await s.SaveChangesAsync();
        return new Probe(user, AuthHandlers.MakeToken(user.Id, StudioAuth.Tenant, user.SessionVersion));
    }

    private static async Task CleanAsync(LiveTemplateDoorHost host, Probe p, params string[] slugs)
    {
        await using (var s = host.Store.LightweightSession()) { foreach (var slug in slugs) s.Delete<Tenant>(slug); await s.SaveChangesAsync(); }
        await using (var s = host.Store.LightweightSession(StudioAuth.Tenant))
        {
            s.DeleteWhere<ConsentRecord>(c => c.UserId == p.User.Id);
            s.Delete<StudioUser>(p.User.Id);
            await s.SaveChangesAsync();
        }
    }

    private static (string, string)[] Form(string slug, string? name = null, string? color = null) =>
    [
        ("slug", slug), ("name", name ?? StudioStoreBirthCharacterizationTests.RefName), ("color", color ?? StudioStoreBirthCharacterizationTests.RefColor),
        ("tagline", StudioStoreBirthCharacterizationTests.RefTagLine),
    ];

    private static LiveTemplateDoorHost HostWithPhone() => LiveTemplateDoorHost.Build(services: s => s.AddSingleton<IOtpChannel>(new OtpSendQuotaTests.RecordingSms()));

    private static Task<(int Status, string Location)> CreateAsync(LiveTemplateDoorHost host, string? token, params (string, string)[] form)
        => host.PostStudioFormAsync(StudioStoreCreate.Endpoint, new Dictionary<string, string>(), token, form);

    /// <summary><b>الموجَب</b>: صاحِبُ جَلسَةٍ وافَقَ عَلى الإصدارِ القائِمِ يُنشِئُ مَتجَراً — والمَكتوبُ في Postgres هُوَ بَصمَةُ التَوصيفِ بِعَينِها،
    /// مالِكُه هُوَ، وبِلا مَصدَر، وعَدّادُ مَتاجِرِه زادَ واحِداً.</summary>
    [Fact]
    public async Task A_consented_owner_creates_the_pinned_store_by_hand_on_Postgres()
    {
        if (!Enabled) return;
        await using var host = HostWithPhone();
        var p = await UserAsync(host, consented: true);
        var slug = ("w13c-" + Guid.NewGuid().ToString("N"))[..18];
        try
        {
            var (status, location) = await CreateAsync(host, p.Token, Form(slug));
            output.WriteLine($"[live] create ⇒ {status} {location}");
            Assert.Equal((302, $"/studio/apps/{slug}?built=1"), (status, location));

            await using var q = host.Store.QuerySession();
            var tenant = await q.LoadAsync<Tenant>(slug);
            Assert.NotNull(tenant);
            var sha = StudioStoreBirthCharacterizationTests.Sha256(StudioStoreBirthCharacterizationTests.Canonical(tenant!));
            output.WriteLine($"[live] SHA-256 = {sha} · مالِك={tenant!.OwnerUserId == p.User.Id} · مَصدَر={tenant.SourceAnalysisId?.ToString() ?? "null"}");
            Assert.Equal(StudioStoreBirthCharacterizationTests.GoldenSha256, sha);
            Assert.Equal(p.User.Id, tenant.OwnerUserId);
            Assert.Null(tenant.SourceAnalysisId);
            Assert.Equal(AppPattern.Marketplace, PatternProfileResolver.PatternOf(tenant));

            await using var sq = host.Store.QuerySession(StudioAuth.Tenant);
            Assert.Equal(1, (await sq.LoadAsync<StudioUser>(p.User.Id))!.StoresBuilt);
        }
        finally { await CleanAsync(host, p, slug); }
    }

    /// <summary><b>السالِبُ لِلحَرَسِ قَبلَ الحُقول</b>: المَجهولُ إلى الدُخول، ومَن لَم يُوافِق إلى المُوافَقَةِ بِعَودَةٍ إلى الاستِمارَة، ومَن بَلَغَ حَدَّ
    /// مَتاجِرِه إلى نافِذَةِ التَرقِيَة — بِحُقولٍ صَحيحَة، ولا مَتجَرَ يُكتَبُ ولا عَدّادَ يَزيد.</summary>
    [Theory]
    [InlineData("anonymous", "/studio/auth")]
    [InlineData("unconsented", "/studio/consent?returnUrl=/studio/new")]
    [InlineData("at-quota", "/studio/new?upgrade=" + StudioUpgradeReason.Stores)]
    public async Task The_guards_before_the_fields_refuse_and_write_nothing_on_Postgres(string who, string expected)
    {
        if (!Enabled) return;
        await using var host = HostWithPhone();
        var max = TierCatalog.For("spark").StoresMax;
        var p = await UserAsync(host, consented: who != "unconsented", storesBuilt: who == "at-quota" ? max : 0);
        var slug = ("w13g-" + Guid.NewGuid().ToString("N"))[..18];
        try
        {
            var (status, location) = await CreateAsync(host, who == "anonymous" ? null : p.Token, Form(slug));
            output.WriteLine($"[live] {who} ⇒ {status} {location}");
            Assert.Equal((302, expected), (status, location));
            await using var q = host.Store.QuerySession();
            Assert.Null(await q.LoadAsync<Tenant>(slug));
            await using var sq = host.Store.QuerySession(StudioAuth.Tenant);
            Assert.Equal(who == "at-quota" ? max : 0, (await sq.LoadAsync<StudioUser>(p.User.Id))!.StoresBuilt);
        }
        finally { await CleanAsync(host, p, slug); }
    }

    /// <summary><b>الموجَبُ لِكُلِّ رَمزِ حَقل</b>: البابُ المُسَجَّلُ يَرُدُّ إلى الاستِمارَةِ بِرَمزِه، ولا مَتجَرَ يُكتَب. (والسالِبُ صَفُّ الإنشاءِ أَعلاه.)</summary>
    [Theory]
    [InlineData("", null, null, TenantFromAnalysisFactory.SlugRequired)]
    [InlineData("Bad Slug!", null, null, TenantFromAnalysisFactory.SlugFormat)]
    [InlineData("pricing", null, null, TenantFromAnalysisFactory.SlugReserved)]
    [InlineData("TAKEN", null, null, TenantFromAnalysisFactory.SlugTaken)]
    [InlineData("w13f-name", "   ", null, StudioStoreCreate.NameRequired)]
    [InlineData("w13f-color", null, "blue", StudioStoreCreate.ColorInvalid)]
    public async Task Every_field_refusal_returns_to_the_form_with_its_code_on_Postgres(string slug, string? name, string? color, string code)
    {
        if (!Enabled) return;
        await using var host = HostWithPhone();
        var p = await UserAsync(host, consented: true);
        var taken = ("w13t-" + Guid.NewGuid().ToString("N"))[..18];
        var probeSlug = slug == "TAKEN" ? taken : slug == "" || slug == "Bad Slug!" || slug == "pricing" ? slug : slug + "-" + Guid.NewGuid().ToString("N")[..6];
        try
        {
            if (slug == "TAKEN")
                await using (var s = host.Store.LightweightSession()) { s.Store(new Tenant { Id = taken, Name = "قائِم" }); await s.SaveChangesAsync(); }
            var (status, location) = await CreateAsync(host, p.Token, Form(probeSlug, name, color));
            output.WriteLine($"[live] {code} ⇒ {status} {location}");
            Assert.Equal(302, status);
            Assert.StartsWith($"/studio/new?err={code}&", location, StringComparison.Ordinal);
            await using var sq = host.Store.QuerySession(StudioAuth.Tenant);
            Assert.Equal(0, (await sq.LoadAsync<StudioUser>(p.User.Id))!.StoresBuilt);
            if (slug != "TAKEN" && probeSlug.Length > 0)
            {
                await using var q = host.Store.QuerySession();
                Assert.Null(await q.LoadAsync<Tenant>(probeSlug.ToLowerInvariant()));
            }
        }
        finally { await CleanAsync(host, p, taken); }
    }

    /// <summary>وبِلا قَناةِ دُخولٍ مُسَجَّلَةٍ لا مَتجَرَ بِبابٍ مُغلَق: <c>no_auth_channel</c> ولا كِتابَة. (والموجَبُ بِقَناةٍ صَفُّ الإنشاءِ أَعلاه.)</summary>
    [Fact]
    public async Task Without_a_sign_in_channel_no_store_is_born_on_Postgres()
    {
        if (!Enabled) return;
        await using var host = LiveTemplateDoorHost.Build();
        var p = await UserAsync(host, consented: true);
        var slug = ("w13n-" + Guid.NewGuid().ToString("N"))[..18];
        try
        {
            var (status, location) = await CreateAsync(host, p.Token, Form(slug));
            output.WriteLine($"[live] no channel ⇒ {status} {location}");
            Assert.Equal(302, status);
            Assert.StartsWith($"/studio/new?err={TenantAuthChannelDoor.NoChannel}&", location, StringComparison.Ordinal);
            await using var q = host.Store.QuerySession();
            Assert.Null(await q.LoadAsync<Tenant>(slug));
        }
        finally { await CleanAsync(host, p, slug); }
    }

    /// <summary>الصَفحَةُ مُصَيَّرَةً — أَو <c>«→ {الوِجهَة}»</c> إن حَوَّلَت مِن الخادِم (‏2026-11-07، W13-1·C1D-1: <c>NavigateTo</c> في التَصييرِ الساكِنِ يُجابُ ‏302).</summary>
    private static async Task<string> RenderNewStoreAsync(LiveTemplateDoorHost host, string? token)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var ctx = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        ctx.Request.Scheme = "http";
        ctx.Request.Host = new HostString("localhost");
        ctx.Request.Path = StudioStoreCreate.Page;
        if (token is not null) ctx.Request.Headers.Cookie = $"{StudioAuth.CookieName}={token}";
        if (scope.ServiceProvider.GetService<IHttpContextAccessor>() is { } accessor) accessor.HttpContext = ctx;
        if (scope.ServiceProvider.GetService<NavigationManager>() is Microsoft.AspNetCore.Components.Routing.IHostEnvironmentNavigationManager nav)
            nav.Initialize("http://localhost/", "http://localhost" + StudioStoreCreate.Page);
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, NullLoggerFactory.Instance);
        try
        {
            return System.Net.WebUtility.HtmlDecode(await renderer.Dispatcher.InvokeAsync(async () =>
                (await renderer.RenderComponentAsync<ACommerce.Templates.Customer.Marketplace.Components.Pages.StudioNewStore>()).ToHtmlString()));
        }
        catch (Microsoft.AspNetCore.Components.NavigationException e) { return "→ " + new Uri(new Uri("http://localhost/"), e.Location).PathAndQuery; }
    }

    /// <summary><b>القاعِدَة ١٢ بِالصَفحَةِ لا بِالمَصدَر</b>: لِلمُوافِقِ تُرسَمُ الاستِمارَةُ إلى البابِ وحُقولُها الأَربَعَة؛ ولِغَيرِه لا استِمارَةَ بَل تَحويلٌ
    /// إلى المُوافَقَةِ بِعَودَةٍ إلى هُنا.</summary>
    [Fact]
    public async Task The_create_page_draws_the_form_for_a_consented_owner_and_sends_the_other_to_consent_on_Postgres()
    {
        if (!Enabled) return;
        await using var host = HostWithPhone();
        var yes = await UserAsync(host, consented: true);
        var no = await UserAsync(host, consented: false);
        try
        {
            var consented = await RenderNewStoreAsync(host, yes.Token);
            var unconsented = await RenderNewStoreAsync(host, no.Token);
            int Forms(string html) => Regex.Matches(html, "<form method=\"post\" action=\"/studio/apps/create\"").Count;
            output.WriteLine($"[live] مُوافِق: {consented.Length} بايتاً · استِمارَة {Forms(consented)} · غَيرُ مُوافِق: {unconsented.Length} بايتاً · استِمارَة {Forms(unconsented)}");
            Assert.Equal(1, Forms(consented));
            foreach (var field in new[] { "name=\"name\"", "name=\"slug\"", "name=\"color\"", "name=\"tagline\"" })
                Assert.Contains(field, consented, StringComparison.Ordinal);
            Assert.DoesNotContain(StudioStoreCreate.ConsentPage, consented, StringComparison.Ordinal);
            Assert.Equal(0, Forms(unconsented));
            // ‏(‏2026-11-07، W13-1·C1D-1 — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه) كانَ: الصَفحَةُ تَحمِلُ `location.replace('/studio/consent?returnUrl=/studio/new')` — والتَحويلُ صارَ مِن الخادِم.
            Assert.Equal("→ " + StudioStoreCreate.ConsentPage, unconsented);
            var anonymous = await RenderNewStoreAsync(host, null);
            output.WriteLine($"[live] زائِر: {anonymous}");
            Assert.Equal("→ /studio/auth", anonymous);
        }
        finally { await CleanAsync(host, yes); await CleanAsync(host, no); }
    }
}
