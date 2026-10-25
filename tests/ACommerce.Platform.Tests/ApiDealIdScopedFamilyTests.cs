using System.Text.RegularExpressions;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ كُلُّ نُقطَةٍ تَحتَ `/api/v1/deals/{id:guid}` تَسأَلُ `DealApi.IsParty` وتُجيبُ ‏404 قَبلَ أَيِّ نِداءِ خِدمَة (مُراجَعَةُ تَيّارِ W8-10 الجَولَة ٢، صُغرى؛ القاعِدَة ٦) ═══
//
// **المَقيسُ عِندَ الرَأس** (`2642072`): القاعِدَةُ — «كِتابَةُ صَفقَةٍ بِمُعَرِّفِها في الـAPI تَسأَلُ العُضوِيَّةَ وتُجيبُ ‏404 قَبلَ أَيِّ نِداءِ خِدمَة» — مُثَبَّتَةٌ
// **نُقطَةً نُقطَةً بِمَقاطِعَ نَصِّيَّة**: `ApiDealAdvancePartyTests.Steps` لِـ`/advance`، و`ApiDealCancelGuardOrderTests.Steps` لِـ`/cancel`. ولا شَيءَ يَعُدُّ
// **العائِلَة**: نُقطَةٌ رابِعَةٌ تُضافُ غَداً (`/rate`، `/dispute`…) تَمُرُّ بِلا سُؤال — وهذا بِحَرفِه ما تَصِفُه القاعِدَة ٦: الحِراسَةُ سَطرٌ في الجِسمِ فَلا يُرى
// نِسيانُه. وكانَ `/advance` نَفسُه هذِه الحالَة حَتّى `4c59a37` (تَوصيفُه `e716f46`).
//
// **ما يُفحَصُ هُنا**: كُلُّ `MapGet`/`MapPost`/`MapPut`/`MapDelete`/`MapPatch` بِمَسارٍ يَبدَأُ `/api/v1/deals/{id:guid}` في `ApiV1Endpoints.cs` — ولِكُلٍّ:
// اسمُ مُعامِلِ `DealsService` في تَوقيعِه، وأَوَّلُ نِداءٍ عَلَيه غَيرِ `LoadAsync`، و`DealApi.IsParty(` و`ApiErrorCatalog.NotFound` **قَبلَه**. ويَحمَرُّ إن عَدَّ
// صِفرَ نُقطَة (القاعِدَة ١٠)، ويُقاسُ بِعَيِّناتٍ مَحقونَة — مِنها مَقطَعُ `/advance` عِندَ `e2fcf8c` بِحَرفِه.
//
// **وحُدودُه — تُقال**: يَقرَأُ مِلَفّاً واحِداً، فَيَشتَرِطُ أَلّا يَظهَرَ المَسارُ حَرفِيّاً في غَيرِه مِن `libs/` و`apps/`، وأَلّا يَكونَ في المِلَفِّ `MapGroup`
// (مَسارٌ نِسبِيٌّ تَحتَ مَجموعَةٍ لا يَراه)؛ ويَرى التَرتيبَ في النَصِّ لا في التَنفيذ — فَرعٌ يَقفِزُ `IsParty` بِشَرطٍ لا يَراه، وهو ما تَقيسُه صُفوفُ السُلوكِ
// في المِلَفَّينِ المُجاوِرَين.

public class ApiDealIdScopedFamilyTests(ITestOutputHelper output)
{
    private static string RepoRoot => ThemeZeroEquivalenceTests.RepoRoot;
    private const string Template = "libs/templates/ACommerce.Templates.Customer.Marketplace";

    /// <summary>بادِئَةُ العائِلَة — المَسارُ بِمُعَرِّفِ صَفقَة.</summary>
    internal const string FamilyPrefix = "/api/v1/deals/{id:guid}";

    /// <summary>نُقطَةٌ مِن العائِلَة: الفِعلُ والمَسارُ ومَقطَعُها في المَصدَرِ (مِن تَسجيلِها إلى التَسجيلِ الَّذي يَليها).</summary>
    internal sealed record Route(string Verb, string Path, string Segment);

    private static readonly Regex MapCall = new(
        @"\.Map(?<verb>Get|Post|Put|Delete|Patch)\(\s*""(?<path>" + Regex.Escape(FamilyPrefix) + @"[^""]*)""",
        RegexOptions.CultureInvariant);

    private static readonly Regex AnyMap = new(@"\.Map(?:Get|Post|Put|Delete|Patch|Methods)\(", RegexOptions.CultureInvariant);

    /// <summary>نُقَطُ العائِلَةِ في مَصدَرٍ مَنزوعِ التَعليقات — المَقطَعُ يَنتَهي عِندَ التَسجيلِ التالي أَيّاً كانَ مَسارُه، أَو آخِرِ المَصدَر.</summary>
    internal static IReadOnlyList<Route> IdScopedRoutes(string source)
    {
        var all = AnyMap.Matches(source).Select(m => m.Index).ToList();
        var routes = new List<Route>();
        foreach (Match m in MapCall.Matches(source))
        {
            var next = all.FirstOrDefault(i => i > m.Index, -1);
            var end = next < 0 ? source.Length : next;
            routes.Add(new Route(m.Groups["verb"].Value, m.Groups["path"].Value, source.Substring(m.Index, end - m.Index)));
        }
        return routes;
    }

    /// <summary>
    /// <para><b>الفاحِصُ النَقِيّ</b> — رُموزُ خَرقِ نُقطَةٍ واحِدَة، وفارِغُها «سَليمَة»:</para>
    /// <list type="bullet">
    ///   <item><c>no_service_param</c> — لا مُعامِلَ <c>DealsService</c> في التَوقيع: لا يُعرَفُ اسمُ الخِدمَةِ فَلا يُرى نِداؤُها.</item>
    ///   <item><c>party_missing</c> / <c>not_found_missing</c> — لا <c>DealApi.IsParty(</c> أَو لا <c>ApiErrorCatalog.NotFound</c> في المَقطَع.</item>
    ///   <item><c>party_after_service</c> / <c>not_found_after_service</c> — أَحَدُهُما بَعدَ أَوَّلِ نِداءٍ عَلى الخِدمَةِ غَيرِ <c>LoadAsync</c>.</item>
    /// </list>
    /// </summary>
    internal static IReadOnlyList<string> PartyBeforeService(string segment)
    {
        var codes = new List<string>();
        var param = Regex.Match(segment, @"\bDealsService\s+(?<name>[A-Za-z_]\w*)\s*[,)]", RegexOptions.CultureInvariant);
        var party = segment.IndexOf("DealApi.IsParty(", StringComparison.Ordinal);
        var notFound = segment.IndexOf("ApiErrorCatalog.NotFound", StringComparison.Ordinal);
        if (!param.Success) codes.Add("no_service_param");
        if (party < 0) codes.Add("party_missing");
        if (notFound < 0) codes.Add("not_found_missing");
        if (!param.Success) return codes;

        var service = FirstServiceCall(segment, param.Groups["name"].Value);
        if (service >= 0 && party > service) codes.Add("party_after_service");
        if (service >= 0 && notFound > service) codes.Add("not_found_after_service");
        return codes;
    }

    /// <summary>مَوضِعُ أَوَّلِ <c>name.X(</c> حَيثُ <c>X</c> غَيرُ <c>LoadAsync</c> — والحَرفِيَّةُ <c>"deals.advance"</c> لا تُعَدُّ لِأَنَّ ما بَعدَها لَيسَ <c>(</c>؛ و‏‎-1 «لا نِداء».</summary>
    internal static int FirstServiceCall(string segment, string name)
    {
        foreach (Match c in Regex.Matches(segment, @"\b" + Regex.Escape(name) + @"\.(?<member>[A-Za-z_]\w*)\s*\(", RegexOptions.CultureInvariant))
            if (c.Groups["member"].Value != "LoadAsync") return c.Index;
        return -1;
    }

    /// <summary>مَقطَعُ <c>/advance</c> عِندَ <c>e2fcf8c</c> بِحَرفِه، مَنزوعَ التَعليقات — <c>AdvanceAsync</c> بِلا سُؤالِ عُضوِيَّة (تَوصيفُه <c>e716f46</c>).</summary>
    internal const string AdvanceAtE2fcf8c =
        "app.MapPost(\"/api/v1/deals/{id:guid}/advance\", async (\n" +
        "            Guid id, HttpContext http, DealsService deals, ApiIdempotencyService idem,\n" +
        "            DealNotices notices, ACommerce.Platform.I18n.L l) =>\n" +
        "            await ApiWrite.OnceAsync(http, idem, \"deals.advance\", async p =>\n" +
        "            {\n" +
        "                var body = await ApiBody.ReadAsync<AdvanceDealRequest>(http);\n" +
        "                var res = await deals.AdvanceAsync(\n" +
        "                    p.TenantSlug, id, p.ActorUserId, p.ActorName, body?.note, http.RequestAborted, from: DealsService.ExpectedStage(body?.expected_stage));\n" +
        "                if (DealNotices.IsAcceptance(res)) await notices.NotifyAcceptedAsync(p.TenantSlug, res.Deal!, p.ActorName, l, http.RequestAborted);\n" +
        "                return DealApi.FromAdvance(res);\n" +
        "            })\n" +
        "        ).RequireApiKey(ApiScopeCatalog.DealsWrite);\n";

    private static string Source()
    {
        var path = Path.Combine(RepoRoot, Template, "Api", "ApiV1Endpoints.cs");
        Assert.True(File.Exists(path), $"مَصدَرٌ مَفقود: {path} — الأَداةُ عَمياء.");
        var text = WriteEndpointGuardTests.StripComments(File.ReadAllText(path));
        Assert.True(text.Length > 500, $"أَداةٌ عَمياء: {path} طولُه {text.Length} مِحرَفاً — لَم يُقرَأ.");
        return text;
    }

    /// <summary>
    /// <para><b>العائِلَةُ كُلُّها لا نُقطَةً نُقطَة</b> (‏2026-10-25، مُراجَعَةُ تَيّارِ W8-10 الجَولَة ٢): كُلُّ نُقطَةٍ بِمُعَرِّفِ صَفقَةٍ في <c>ApiV1Endpoints.cs</c> تَسأَلُ
    /// <c>DealApi.IsParty</c> وتُجيبُ ‏404 قَبلَ أَوَّلِ نِداءٍ عَلى <c>DealsService</c> غَيرِ <c>LoadAsync</c> — والقِراءَةُ مِنها (لا نِداءَ غَيرُ التَحميل) تَسأَلُه
    /// أَيضاً. ويَحمَرُّ عَلى صِفرِ نُقطَة، ويَطبَعُ ما عَدَّه.</para>
    /// </summary>
    [Fact]
    public void Every_id_scoped_api_deal_route_asks_IsParty_and_answers_404_before_its_first_service_call()
    {
        var source = Source();
        var routes = IdScopedRoutes(source);
        Assert.True(routes.Count >= 3, $"أَداةٌ عَمياء: {routes.Count} نُقطَةً تَحتَ {FamilyPrefix} (المَقيسُ 3 يَومَ الإلحاق: GET و/advance و/cancel).");
        Assert.Equal(-1, source.IndexOf("MapGroup(", StringComparison.Ordinal));

        var bad = routes.Select(r => (r, Codes: PartyBeforeService(r.Segment))).Where(x => x.Codes.Count > 0).ToList();
        Assert.True(bad.Count == 0,
            "نُقَطٌ تُنادي الخِدمَةَ قَبلَ أَن تَسأَلَ العُضوِيَّة:\n" +
            string.Join("\n", bad.Select(x => $"{x.r.Verb} {x.r.Path} — {string.Join(", ", x.Codes)}")));

        // ‏والمَسارُ لا يُسَجَّلُ في مِلَفٍّ آخَر — وإلّا فَالفاحِصُ لا يَراه.
        var elsewhere = new[] { "libs", "apps" }
            .SelectMany(d => Directory.EnumerateFiles(Path.Combine(RepoRoot, d), "*.cs", SearchOption.AllDirectories))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToList();
        Assert.True(elsewhere.Count > 100, $"أَداةٌ عَمياء: {elsewhere.Count} مِلَفّاً في libs/ وapps/.");
        var outside = elsewhere
            .Where(f => !f.EndsWith(Path.Combine("Api", "ApiV1Endpoints.cs"), StringComparison.Ordinal))
            .Where(f => File.ReadAllText(f).Contains("\"" + FamilyPrefix, StringComparison.Ordinal))
            .Select(f => Path.GetRelativePath(RepoRoot, f))
            .ToList();
        Assert.Empty(outside);

        foreach (var r in routes)
        {
            var name = Regex.Match(r.Segment, @"\bDealsService\s+(?<name>[A-Za-z_]\w*)\s*[,)]").Groups["name"].Value;
            output.WriteLine($"{r.Verb} {r.Path}: IsParty@{r.Segment.IndexOf("DealApi.IsParty(", StringComparison.Ordinal)}" +
                             $" · 404@{r.Segment.IndexOf("ApiErrorCatalog.NotFound", StringComparison.Ordinal)}" +
                             $" · أَوَّلُ نِداءِ خِدمَة@{FirstServiceCall(r.Segment, name)}");
        }
        output.WriteLine($"نُقَطُ العائِلَة: {routes.Count} · سَليمَة: {routes.Count - bad.Count} · مِلَفّاتٌ أُخرى فُحِصَت: {elsewhere.Count - 1} · تُسَجِّلُ المَسار: {outside.Count}");
    }

    /// <summary>
    /// <para><b>والفاحِصُ يَحمَرُّ عَلى ما كُتِبَ لَه</b> (القاعِدَة ١٠): مَقطَعُ <c>/advance</c> عِندَ <c>e2fcf8c</c> بِحَرفِه ⇒ لا <c>IsParty</c> ولا ‏404؛ ونُقطَةٌ رابِعَةٌ
    /// مَصنوعَةٌ تُنادي الخِدمَةَ ثُمَّ تَسأَل ⇒ «بَعدَ الخِدمَة»؛ ومُعامِلٌ بِاسمٍ آخَرَ (<c>svc</c>) يُتبَعُ بِاسمِه؛ ونُقطَةٌ بِلا <c>DealsService</c> لا تُعَدُّ سَليمَة.
    /// والسَليمَةُ المَصنوعَةُ — والحَرفِيَّةُ <c>"deals.rate"</c> قَبلَ السُؤال — تَمُرّ.</para>
    /// </summary>
    [Fact]
    public void The_family_check_goes_red_on_the_advance_door_it_was_written_for_and_on_injected_routes()
    {
        const string late =
            "app.MapPost(\"/api/v1/deals/{id:guid}/rate\", async (Guid id, HttpContext http, DealsService deals) =>\n" +
            "{ var p = http.ApiPrincipal()!; var res = await deals.RateAsync(p.TenantSlug, id); var found = await deals.LoadAsync(p.TenantSlug, id, http.RequestAborted);\n" +
            "  if (found is null || !DealApi.IsParty(found, p.ActorUserId)) return ApiOutcome.Error(ApiErrorCatalog.NotFound).ToResult(); return res; });\n";
        const string renamed =
            "app.MapPost(\"/api/v1/deals/{id:guid}/dispute\", async (Guid id, HttpContext http, DealsService svc) =>\n" +
            "{ var p = http.ApiPrincipal()!; await svc.DisputeAsync(p.TenantSlug, id); var found = await svc.LoadAsync(p.TenantSlug, id, http.RequestAborted);\n" +
            "  return found is null || !DealApi.IsParty(found, p.ActorUserId) ? ApiOutcome.Error(ApiErrorCatalog.NotFound).ToResult() : Results.Ok(); });\n";
        const string noService =
            "app.MapGet(\"/api/v1/deals/{id:guid}/notes\", async (Guid id, HttpContext http, NotesService notes) =>\n" +
            "{ return await notes.ForDealAsync(id); });\n";
        const string good =
            "app.MapPost(\"/api/v1/deals/{id:guid}/rate\", async (Guid id, HttpContext http, DealsService deals, ApiIdempotencyService idem) =>\n" +
            "  await ApiWrite.OnceAsync(http, idem, \"deals.rate\", async p => { var found = await deals.LoadAsync(p.TenantSlug, id, http.RequestAborted);\n" +
            "  if (found is null || !DealApi.IsParty(found, p.ActorUserId)) return ApiOutcome.Error(ApiErrorCatalog.NotFound); return await deals.RateAsync(p.TenantSlug, id); }));\n";

        var injected = string.Join("\n", AdvanceAtE2fcf8c, late, renamed, noService, good);
        var routes = IdScopedRoutes(injected);
        Assert.Equal(new[] { "/advance", "/rate", "/dispute", "/notes", "/rate" },
            routes.Select(r => r.Path[FamilyPrefix.Length..]).ToArray());

        var codes = routes.Select(r => string.Join(",", PartyBeforeService(r.Segment))).ToArray();
        Assert.Equal("party_missing,not_found_missing", codes[0]);
        Assert.Equal("party_after_service,not_found_after_service", codes[1]);
        Assert.Equal("party_after_service,not_found_after_service", codes[2]);
        Assert.Equal("no_service_param,party_missing,not_found_missing", codes[3]);
        Assert.Equal("", codes[4]);

        // ‏والماسِحُ عِندَ الرَأسِ يَرى المَقاطِعَ الثَلاثَةَ سَليمَةً — والعَيِّنَةُ الأولى هِيَ نَفسُ `/advance` قَبلَ `4c59a37`.
        Assert.All(IdScopedRoutes(Source()), r => Assert.Empty(PartyBeforeService(r.Segment)));
        output.WriteLine($"عَيِّناتٌ مَحقونَة: {routes.Count} — حَمراء: {codes.Count(c => c.Length > 0)} ({string.Join(" | ", codes.Where(c => c.Length > 0))}) · سَليمَة: {codes.Count(c => c.Length == 0)}");
    }
}
