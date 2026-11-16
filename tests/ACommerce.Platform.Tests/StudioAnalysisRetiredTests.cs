using System.Text.RegularExpressions;
using ACommerce.Templates.Customer.Marketplace.Services.Export;
using ACommerce.Templates.Customer.Marketplace.Services.Incubator;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ التَحليلُ خَرَجَ مِن رِحلَةِ رائِدِ الأَعمال — وكُلُّ حَدٍّ عَبَرَه يَحمَرّ (‏2026-11-07، الموجَة ١٣ W13-1·٥ — ADR-049 §١–§٣ و§٩) ═══
//
// **ما خَرَج**: `POST /studio/begin` و`/studio/s/{id}/analyze` و`/refine` و`/feedback` و`/build`، والاستِئنافُ `ResumeStudioPromptAsync` وكَعكَتُه
// `ac.studio.prompt`، و`StudioAnalysisDoor` وسَطرُ إقلاعِه وكُتلَتُه في بَوّابَةِ الإنتاج، واستِمارَةُ الفِكرَةِ عَلى `/`، و`/studio/ideas` وقِسمُ الأَفكار.
// وخَرَجَت مَعَها ثَمانِيَةٌ وخَمسونَ حالَةً كانَت تَحرُسُ ما زال (`StudioAnalysisDoorTests` ‏22، `StudioRefineOwnershipTests` ‏17، `StudioRefineWaitTests` ‏12،
// `StudioBeginResumeTests` ‏3، وبُرهانُ التَعاقُبِ وصِنوُه الحَيّ، وبُرهانُ السِباقِ الحَيّ، وبُرهانُ `/build` الحَيّ).
// **وما يَحِلُّ مَحَلَّها هُنا — حُدودٌ تَحمَرُّ عِندَ العُبور، لا ذِكرى**: لا نُقطَةَ تَعودُ بِمَسارٍ خَرَج، ولا مَصدَرَ يَرسُمُ إلَيه، ولا نُقطَةَ ولا صَفحَةَ
// استوديو تَبلُغُ خِدمَةَ نَموذَجِ لُغَة (بِمُستَثنىً مُسَمّى: صَفحَةُ الوَكيلِ خَلفَ بابِها)، والدِراسَةُ القَديمَةُ قِراءَةٌ لا يَبلُغُها إلّا شَريطُ مَتجَرِها،
// ووَثائِقُ البَياناتِ باقِيَةٌ بِحُكمِها في حَقيبَةِ التَصدير، وحاضِنَةُ المُشرِفِ وَحدَها تَحقِنُ الخِدمَة، وما تَيَتَّمَ مُثَبَّتٌ بِشَرطِ رَفعِه.

public class StudioAnalysisRetiredTests(ITestOutputHelper output)
{
    private const string Template = "libs/templates/ACommerce.Templates.Customer.Marketplace";

    private static string Root => ThemeZeroEquivalenceTests.RepoRoot;
    private static string Read(string rel) => File.ReadAllText(Path.Combine(Root, rel));

    /// <summary>المَساراتُ الخَمسَةُ الَّتي خَرَجَت — ونَمَطُ كُلٍّ كَما يُكتَبُ في مَصدَرٍ يَرسُمُ إلَيه (رابِطاً أَو استِمارَةً أَو تَحويلاً، بِمُعَرِّفٍ مُدرَجٍ أَو حَرفِيّ).</summary>
    internal static readonly (string Route, Regex Spelling)[] Retired =
    [
        ("/studio/begin",                  new(@"/studio/begin(?![\w-])")),
        ("/studio/s/{id:guid}/analyze",    new(@"/studio/s/(?:\{[^}]+\}|[0-9a-fA-F-]{8,})/analyze(?![\w-])")),
        ("/studio/s/{id:guid}/refine",     new(@"/studio/s/(?:\{[^}]+\}|[0-9a-fA-F-]{8,})/refine(?![\w-])")),
        ("/studio/s/{id:guid}/feedback",   new(@"/studio/s/(?:\{[^}]+\}|[0-9a-fA-F-]{8,})/feedback(?![\w-])")),
        ("/studio/s/{id:guid}/build",      new(@"/studio/s/(?:\{[^}]+\}|[0-9a-fA-F-]{8,})/build(?![\w-])")),
    ];

    public static TheoryData<string> RetiredRoutes() => new(Retired.Select(r => r.Route));

    // ─── (١) لا نُقطَةَ تَعودُ بِمَسارٍ خَرَج ──────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(RetiredRoutes))]
    public void A_retired_route_is_mapped_by_no_endpoint(string route)
    {
        var all = WriteEndpointGuardTests.AllMinimalApiEndpoints().ToList();
        Assert.True(all.Count > 100, $"أَداةٌ عَمياء: {all.Count} نُقطَة.");
        Assert.DoesNotContain(all, e => e.Route == route);
    }

    // ─── (٢) ولا مَصدَرَ يَرسُمُ إلَيه — رابِطاً أَو استِمارَةً أَو تَحويلاً ────────────────────────────────

    /// <summary>مَصادِرُ <c>libs</c>/<c>apps</c> (‏<c>.cs</c>، <c>.razor</c>، <c>.js</c>) بِلا تَعليقات.</summary>
    internal static IEnumerable<(string File, string Code)> Sources()
    {
        foreach (var (file, text) in EntitlementContractTests.SourceFiles())
        {
            var rel = Path.GetRelativePath(Root, file).Replace('\\', '/');
            yield return (rel, rel.EndsWith(".razor", StringComparison.Ordinal)
                ? WriteEndpointGuardTests.StripMarkupComments(text) : WriteEndpointGuardTests.StripComments(text));
        }
        foreach (var dir in new[] { "libs", "apps" })
            foreach (var f in Directory.EnumerateFiles(Path.Combine(Root, dir), "*.js", SearchOption.AllDirectories))
            {
                var n = f.Replace('\\', '/');
                if (n.Contains("/obj/", StringComparison.Ordinal) || n.Contains("/bin/", StringComparison.Ordinal) || n.Contains("/node_modules/", StringComparison.Ordinal)) continue;
                yield return (Path.GetRelativePath(Root, f).Replace('\\', '/'), WriteEndpointGuardTests.StripComments(File.ReadAllText(f)));
            }
    }

    internal static List<string> MentionsOf(Regex spelling, IEnumerable<(string File, string Code)> sources)
        => sources.SelectMany(s => spelling.Matches(s.Code).Select(_ => s.File)).ToList();

    [Theory]
    [MemberData(nameof(RetiredRoutes))]
    public void No_source_draws_a_link_a_form_or_a_redirect_to_a_retired_route(string route)
    {
        var sources = Sources().ToList();
        var spelling = Retired.Single(r => r.Route == route).Spelling;
        var hits = MentionsOf(spelling, sources);
        output.WriteLine($"{route}: مَصادِرُ مَفحوصَة {sources.Count} · ذِكر {hits.Count}");
        Assert.True(sources.Count > 300, $"أَداةٌ عَمياء: {sources.Count} مَصدَراً.");
        Assert.True(hits.Count == 0, $"{route} مَرسومٌ في: " + string.Join("، ", hits));
    }

    /// <summary>والماسِحُ يُقاسُ بِالتَهجِئاتِ الَّتي كانَت في الكودِ نَفسِه قَبلَ الإزالَة — كُلٌّ يُصاب، والتَعليقُ لا يُعَدّ، والمَسارُ القَريبُ لا يُخلَط.</summary>
    [Theory]
    [InlineData("/studio/begin", "<form method=\"post\" action=\"/studio/begin\" class=\"landing-prompt\">")]
    [InlineData("/studio/s/{id:guid}/analyze", "<form method=\"post\" action=\"@($\"/studio/s/{session.Id}/analyze\")\">")]
    [InlineData("/studio/s/{id:guid}/refine", "return Results.Redirect($\"/studio/s/{id}/refine\");")]
    [InlineData("/studio/s/{id:guid}/feedback", "<form method=\"post\" action=\"@($\"/studio/s/{session.Id}/feedback\")\" style=\"margin:0;\">")]
    [InlineData("/studio/s/{id:guid}/build", "location.href = '/studio/s/0b5e9c1a-1111-2222-3333-444455556666/build';")]
    public void The_retired_route_scanner_sees_each_spelling_the_code_used(string route, string sample)
    {
        var spelling = Retired.Single(r => r.Route == route).Spelling;
        Assert.Single(MentionsOf(spelling, [("x/Injected.razor", sample)]));
        Assert.Empty(MentionsOf(spelling, [("x/Injected.cs", WriteEndpointGuardTests.StripComments("// " + sample + "\nvar ok = \"/studio/beginner\";"))]));
    }

    // ─── (٣) لا نُقطَةَ ولا صَفحَةَ استوديو تَبلُغُ خِدمَةَ نَموذَج ────────────────────────────────────────

    /// <summary>رُموزُ ما يَبلُغُ نَموذَجَ لُغَة: خِدمَةُ التَحليلِ والخَلفِيَّةُ ومُزَوِّدُها.</summary>
    internal static readonly Regex LlmService = new(@"\b(?:FeasibilityAnalysisService|IAgentBackend|IAgentBackendProvider)\b", RegexOptions.Compiled);

    public static TheoryData<string> StudioEndpoints()
        => new(WriteEndpointGuardTests.AllMinimalApiEndpoints().Where(e => e.Route.StartsWith("/studio", StringComparison.Ordinal))
            .Select(e => e.Route).Distinct().Order(StringComparer.Ordinal));

    [Theory]
    [MemberData(nameof(StudioEndpoints))]
    public void A_studio_endpoint_reaches_no_language_model_service(string route)
    {
        var bodies = WriteEndpointGuardTests.AllMinimalApiEndpoints().Where(e => e.Route == route).ToList();
        Assert.NotEmpty(bodies);
        Assert.All(bodies, e => Assert.False(LlmService.IsMatch(e.Body), $"{route} يَحقِنُ خِدمَةً تَبلُغُ نَموذَجاً: {LlmService.Match(e.Body).Value}"));
    }

    [Fact]
    public void The_studio_endpoint_list_is_not_blind()
    {
        var n = StudioEndpoints().Count;
        output.WriteLine($"نِقاطُ /studio المَفحوصَة: {n}");
        Assert.True(n >= 25, $"أَداةٌ عَمياء: {n} نُقطَة /studio — والمَقيسُ 29.");
        Assert.True(LlmService.IsMatch("Services.Incubator.FeasibilityAnalysisService svc) =>"), "المُطابِقُ لا يَرى الخِدمَةَ في تَوقيع.");
    }

    /// <summary>صَفَحاتُ <c>/studio…</c> (كُلُّ <c>.razor</c> بِتَوجيهِ <c>@page "/studio</c>).</summary>
    internal static IReadOnlyList<string> StudioPagePaths()
        => Directory.EnumerateFiles(Path.Combine(Root, Template, "Components"), "*.razor", SearchOption.AllDirectories)
            .Where(f => Regex.IsMatch(File.ReadAllText(f), @"@page\s+""/studio"))
            .Select(f => Path.GetRelativePath(Root, f).Replace('\\', '/')).Order(StringComparer.Ordinal).ToList();

    public static TheoryData<string> StudioPages() => new(StudioPagePaths());

    /// <summary><b>المُستَثنى بِاسمِه</b>: صَفحَةُ الوَكيلِ (<c>StudioAgent.razor</c>) — تَحقِنُ <c>AgentService</c> خَلفَ بابِها ومِفتاحِه (‏ADR-049 §٦)؛
    /// ولا صَفحَةَ تَحقِنُ خِدمَةَ التَحليلِ أَو الخَلفِيَّةَ مُباشَرَةً.</summary>
    [Theory]
    [MemberData(nameof(StudioPages))]
    public void A_studio_page_injects_no_language_model_service(string page)
    {
        var code = WriteEndpointGuardTests.StripMarkupComments(Read(page));
        Assert.False(LlmService.IsMatch(code), $"{page} يَبلُغُ خِدمَةَ نَموذَج: {LlmService.Match(code).Value}");
    }

    [Fact]
    public void The_studio_page_list_is_not_blind_and_the_agent_service_has_two_named_readers()
    {
        var pages = StudioPagePaths();
        var agentReaders = pages.Where(p => Regex.IsMatch(WriteEndpointGuardTests.StripMarkupComments(Read(p)), @"@inject\s+[\w.]*AgentService\b"))
            .Select(Path.GetFileName).Order(StringComparer.Ordinal).ToList();
        output.WriteLine($"صَفَحاتُ /studio المَفحوصَة: {pages.Count} · تَحقِنُ AgentService: {string.Join("، ", agentReaders)}");
        Assert.True(pages.Count >= 25, $"أَداةٌ عَمياء: {pages.Count} صَفحَة.");
        Assert.Equal(new[] { "StudioAgent.razor", "StudioHome.razor" }, agentReaders);
    }

    // ─── (٤) الدِراسَةُ القَديمَةُ قِراءَةٌ — ولا يَبلُغُها إلّا شَريطُ مَتجَرِها ──────────────────────────────

    [Theory]
    [InlineData("Components/Pages/StudioStudy.razor")]
    [InlineData("Components/Pages/StudioStudyPrint.razor")]
    public void The_legacy_study_pages_write_nothing_and_refresh_nothing(string page)
    {
        var code = WriteEndpointGuardTests.StripMarkupComments(Read($"{Template}/{page}"));
        foreach (var token in new[] { "<form", "method=\"post\"", "@onclick", "http-equiv=\"refresh\"", "@rendermode" })
            Assert.DoesNotContain(token, code, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Studio.LoadStudyAsync(Id)", code, StringComparison.Ordinal);
        Assert.Contains("session.OwnerUserId != Auth.UserId!.Value", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Only_the_built_from_an_idea_banner_links_to_a_legacy_study()
    {
        // الروابِطُ المَرسومَةُ في الصَفَحات (`.razor`) — وتَحويلُ بابِ التَصديرِ إلى صَفحَتِه نَفسِها لَيسَ رابِطاً يَبلُغُه أَحَدٌ مِن خارِجِها.
        var links = Sources().Where(s => s.File.EndsWith(".razor", StringComparison.Ordinal))
            .Where(s => !s.File.EndsWith("/StudioStudy.razor", StringComparison.Ordinal) && !s.File.EndsWith("/StudioStudyPrint.razor", StringComparison.Ordinal))
            .SelectMany(s => Regex.Matches(s.Code, @"/studio/s/\{[^}]+\}(?![/\w])|/studio/s/@").Select(_ => s.File)).ToList();
        output.WriteLine($"روابِطُ الدِراسَةِ القَديمَة: {links.Count} — {string.Join("، ", links.Distinct())}");
        Assert.NotEmpty(links);
        Assert.All(links, f => Assert.EndsWith("/Components/Pages/StudioApp.razor", f));
    }

    // ─── (٥) ما خَرَجَ مِن الكودِ لا يَعود ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData("ac.studio.prompt")]
    [InlineData("ResumeStudioPromptAsync")]
    [InlineData("StudioAnalysisDoor")]
    [InlineData("StudioRefineWait")]
    [InlineData("@page \"/studio/ideas\"")]
    // ‏(‏2026-11-07، مُراجَعَةُ عَدَساتِ C1 الجَولَةُ الثانِيَة · W13-1·R2D-4 — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه) كانَ هُنا صَفُّ `"\"/studio/ideas\""` — «صِفرُ مَوضِع».
    // وصارَ المَسارُ تَحويلاً إلى `/studio` (كانَ 404 فارِغاً لِمَن حَفِظَه مِن شَريطِ كُلِّ صَفحَة)، فَانتَقَلَ الصَفُّ بِشَرطٍ أَضيَقَ إلى
    // `RetiredStudioRouteTests.A_retired_route_lives_in_one_source_place_and_that_place_is_its_redirect`: مَوضِعٌ واحِدٌ هُوَ التَحويل، ولا رابِطَ ولا صَفحَة.
    public void A_retired_name_lives_in_no_source(string name)
    {
        var hits = Sources().Where(s => s.Code.Contains(name, StringComparison.Ordinal)).Select(s => s.File).ToList();
        Assert.True(hits.Count == 0, $"«{name}» في: " + string.Join("، ", hits));
    }

    [Theory]
    [InlineData("apps/V1.App/Program.cs", "[studio] analysis_door=")]
    [InlineData("scripts/verify-production-boot.sh", "analysis_door()")]
    [InlineData("scripts/verify-production-boot.sh", "analysis_door \"$APP_LOG\"")]
    public void The_analysis_door_boot_line_and_its_gate_block_are_gone(string file, string token)
    {
        var text = file.EndsWith(".cs", StringComparison.Ordinal) ? WriteEndpointGuardTests.StripComments(Read(file)) : Read(file);
        if (file.EndsWith(".sh", StringComparison.Ordinal))
            text = string.Join('\n', text.Split('\n').Where(l => !l.TrimStart().StartsWith('#')));
        Assert.DoesNotContain(token, text, StringComparison.Ordinal);
    }

    /// <summary>بابُ الدُخولِ وبابُ القَبولِ لا يَستَأنِفانِ شَيئاً: الأَوَّلُ إلى وِجهَةِ ما بَعدَ الدُخول، والثاني إلى <c>returnUrl</c> — وكِلاهُما بِلا خِدمَةِ التَحليل.</summary>
    [Theory]
    [InlineData("/studio/auth/verify", "Results.Redirect(Services.Incubator.StudioStoreCreate.AfterSignIn(")]
    [InlineData("/studio/consent/accept", "return Results.Redirect(returnUrl);")]
    public void The_sign_in_and_consent_doors_resume_nothing(string route, string ending)
    {
        var body = WriteEndpointGuardTests.AllMinimalApiEndpoints().Single(e => e.Route == route).Body;
        Assert.Contains(ending, body, StringComparison.Ordinal);
        Assert.DoesNotContain("Resume", body, StringComparison.Ordinal);
        Assert.DoesNotContain("IServiceScopeFactory", body, StringComparison.Ordinal);
    }

    // ─── (٦) البَياناتُ باقِيَة — لا هِجرَةَ هَدّامَة (‏ADR-049 §٢‑١) ─────────────────────────────────────────

    [Theory]
    [InlineData(typeof(IncubatorSession), "owner_analyses", ExportDisposition.ExportOwner)]
    [InlineData(typeof(ConsentRecord), "owner_consent", ExportDisposition.ExportOwner)]
    [InlineData(typeof(AnalysisRunClaim), "-", ExportDisposition.ExcludeInternal)]
    [InlineData(typeof(ACommerce.Templates.Customer.Marketplace.Services.Metering.ModelCallRecord), "-", ExportDisposition.ExcludeInternal)]
    public void Every_analysis_document_keeps_its_place_in_the_export_ledger(Type type, string section, ExportDisposition disposition)
    {
        var row = Assert.Single(TenantExportLedger.All, r => r.ClrType == type);
        Assert.Equal((section, disposition), (row.Entry, row.Disposition));
    }

    // ─── (٧) حاضِنَةُ المُشرِفِ وَحدَها تَحقِنُ خِدمَةَ التَحليل (القاعِدَة ١: مُستَهلِكٌ حَيٌّ واحِد) ──────────────

    [Fact]
    public void Only_the_admin_incubator_reaches_the_analysis_service()
    {
        var endpoints = WriteEndpointGuardTests.AllMinimalApiEndpoints().Where(e => e.Body.Contains("FeasibilityAnalysisService", StringComparison.Ordinal)).Select(e => e.Route).Distinct().ToList();
        var pages = Directory.EnumerateFiles(Path.Combine(Root, Template, "Components"), "*.razor", SearchOption.AllDirectories)
            .Where(f => Regex.IsMatch(WriteEndpointGuardTests.StripMarkupComments(File.ReadAllText(f)), @"@inject\s+[\w.]*FeasibilityAnalysisService\b"))
            .Select(Path.GetFileName).ToList();
        output.WriteLine($"نِقاطٌ تَحقِنُ الخِدمَة: {string.Join("، ", endpoints)} · صَفَحات: {string.Join("، ", pages)}");
        Assert.NotEmpty(endpoints);
        Assert.All(endpoints, r => Assert.StartsWith("/admin/", r, StringComparison.Ordinal));
        Assert.All(pages, p => Assert.Contains(p, new[] { "IncubatorHome.razor", "IncubatorWizard.razor", "IncubatorStudy.razor", "QualityMonitor.razor", "IncubatorFixturesPage.razor" }));
    }

    // ─── (٨) ما تَيَتَّمَ — مُثَبَّتٌ بِشَرطِ رَفعِه (القاعِدَة ١) ────────────────────────────────────────────

    /// <summary><b>يَتيمانِ في وَقتِ التَشغيل</b>، وشَرطُ رَفعِ كُلٍّ مَكتوب:
    /// <c>RefineSectionAsync</c> (ومَعَه <c>StudioRefineSection</c>) — مُنادِيه الوَحيدُ كانَ <c>/studio/s/{id}/refine</c>؛ ويُحذَفُ أَو يُوصَلُ بِحاضِنَةِ المُشرِفِ
    /// يَومَ يَقولُ المالِكُ مَصيرَها («ب» W13-1·٥، ADR-049 §١٢ ‏١). و<c>DeriveSuggestion</c> — قارِئُه الوَحيدُ كانَت استِمارَةُ البِناء؛ ويُثَبِّتُه
    /// <c>StudioBuildRoleFloorTests</c> المُجَمَّدُ حَتّى آخِرِ الموجَة ١٣، فَيُحذَفُ بَعدَها مَعَ صَفِّه. <b>القائِمَةُ تَتَقَلَّصُ ولا تَنمو</b>.</summary>
    internal static readonly (string Member, string DeclaredIn, string RemoveWhen)[] OrphanedThroughWave13 =
    [
        ("RefineSectionAsync", "Services/Incubator/FeasibilityAnalysisService.cs", "قَرارُ المالِكِ عَلى حاضِنَةِ المُشرِف"),
        ("DeriveSuggestion", "Services/Incubator/TenantFromAnalysisFactory.cs", "آخِرُ الموجَة ١٣ (StudioBuildRoleFloorTests مُجَمَّدٌ إلَيها)"),
    ];

    public static TheoryData<string> Orphans() => new(OrphanedThroughWave13.Select(o => o.Member));

    [Theory]
    [MemberData(nameof(Orphans))]
    public void An_orphaned_member_has_no_runtime_caller_until_its_removal_condition(string member)
    {
        var declaredIn = OrphanedThroughWave13.Single(o => o.Member == member).DeclaredIn;
        var callers = Sources().Where(s => !s.File.EndsWith(declaredIn, StringComparison.Ordinal))
            .Where(s => Regex.IsMatch(s.Code, $@"\.{member}\(")).Select(s => s.File).ToList();
        Assert.True(Regex.IsMatch(WriteEndpointGuardTests.StripComments(Read($"{Template}/{declaredIn}")), $@"\b{member}\("), $"{member} زالَ — يُرفَعُ مِن القائِمَة.");
        Assert.True(callers.Count == 0, $"{member} صارَ لَه مُنادٍ — يُرفَعُ مِن القائِمَةِ بِسَبَبِه: " + string.Join("، ", callers));
    }
}
