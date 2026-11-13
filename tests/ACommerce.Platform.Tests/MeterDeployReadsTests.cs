using System.Text.RegularExpressions;
using ACommerce.Templates.Customer.Marketplace.Services.Metering;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

/// <summary>
/// <para><b>الصورَةُ تَنشُرُ ما تَدفَعُه القائِمَة، والدَليلُ يَقرَأُ ما يَطبَعُه الكود</b> (‏2026-11-13، الموجَة ١٣ W13-6·٢٢ — ADR-051 §٢٥؛ التَصميم §10.2 و§13 P13).</para>
///
/// <para><b>المَقيسُ قَبل</b> (`fd77819`): الـ<c>Dockerfile</c> يُرَمِّمُ ويَنشُرُ <c>apps/V1.App</c> وَحدَه، و<c>scripts/deploy-manifest.sh</c> يَبدَأُ مِن
/// جَذرٍ واحِدٍ هُوَ إيّاه — فَالمُجَمِّعُ <c>apps/V1.Meter</c> (عَمَلِيَّةٌ ثانِيَةٌ لا يُشيرُ إلَيها التَطبيق، G10) لا يَبلُغُ الصورَةَ ولا الـSpace، ولا شَيءَ يَحمَرّ.
/// و<c>docs/DEPLOY.md</c> لا يَذكُرُ سَطرَ <c>[meter] boot …</c> ولا سَطرَ <c>[meter-agg] …</c> ولا أَمرَ المُجَمِّع. والحَدّانِ هُنا اسمانِ يَتَباعَدانِ صامِتَينِ
/// إن لَم يُقَسا (القاعِدَة ٢): نَشرٌ يُضافُ ولا تَدفَعُه القائِمَةُ يَكسِرُ <c>restore</c> عَلى الـSpace بَعدَ الدَمج، وسَطرٌ يَتَغَيَّرُ في الكودِ يَترُكُ الدَليلَ
/// يَقولُ لِلمالِكِ «اقرَأ» ما لا يُطبَع.</para>
///
/// <para><b>وصارَ</b>: (أ) ما يُرَمِّمُه الـ<c>Dockerfile</c> = ما يَنشُرُه = <c>ROOT_PROJECTS</c> في السكربت؛ (ب) نُقطَةُ الدُخولِ التَطبيقُ وَحدَه، والأَمرُ
/// <c>V1.Meter/V1.Meter.dll</c> في الدَليلِ هُوَ مُجَلَّدُ نَشرِ المُجَمِّعِ تَحتَ <c>/publish</c> المَنسوخِ إلى <c>WORKDIR</c>؛ (ج) كُلُّ سَطرٍ يَقتَبِسُه الدَليلُ
/// أَوَّلُه <c>[meter] boot</c> يُطابِقُ <see cref="MeterBootResult.Line"/> حَرفاً، وكُلُّ سَطرٍ أَوَّلُه <c>[meter-agg]</c> تَرِدُ أَجزاؤُه الثابِتَةُ بِتَرتيبِها في
/// سَطرٍ واحِدٍ يَكتُبُه المُجَمِّع. كُلٌّ يَطبَعُ عَدَّه ويَفشَلُ عَلى الصِفر، ويَحمَرُّ عَلى عَيبٍ مَحقون.</para>
/// </summary>
public class MeterDeployReadsTests(ITestOutputHelper output)
{
    private static string RepoRoot => ThemeZeroEquivalenceTests.RepoRoot;
    private static string Read(params string[] rel) => File.ReadAllText(Path.Combine([RepoRoot, .. rel])).Replace("\r\n", "\n", StringComparison.Ordinal);

    // ═══ (أ) مَشاريعُ الصورَةِ = جُذورُ القائِمَة ═══════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>رُموزُ الخَرق — مُعجَمٌ مُغلَق: <c>published_not_shipped</c> (الصورَةُ تَنشُرُ مَشروعاً لَيسَ جَذراً في القائِمَة) ·
    /// <c>shipped_not_published</c> (جَذرٌ في القائِمَةِ لا تَنشُرُه الصورَة) · <c>published_not_restored</c> (يُنشَرُ بِـ<c>--no-restore</c> ولا يُرَمَّم).</summary>
    internal static readonly IReadOnlyList<string> ImageCodes = ["published_not_shipped", "shipped_not_published", "published_not_restored"];

    /// <summary>أَسطُرُ الـ<c>Dockerfile</c> بِلا تَعليقاتِها (<c>#</c> في أَوَّلِ السَطر) — فَشَرحُ أَمرٍ لا يُقرَأُ أَمراً.</summary>
    internal static string DockerCode(string dockerfile) => Regex.Replace(dockerfile, @"(?m)^[ \t]*#.*$", "");

    internal static IReadOnlyList<string> Restored(string dockerfile) =>
        Regex.Matches(DockerCode(dockerfile), @"\bdotnet restore\s+(?<p>\S+\.csproj)").Select(m => m.Groups["p"].Value).ToList();

    internal static IReadOnlyList<string> Published(string dockerfile) =>
        Regex.Matches(DockerCode(dockerfile), @"\bdotnet publish\s+(?<p>\S+\.csproj)").Select(m => m.Groups["p"].Value).ToList();

    /// <summary>عَناصِرُ <c>ROOT_PROJECTS=( … )</c> في السكربتِ بِلا تَعليقاتِه.</summary>
    internal static IReadOnlyList<string> Roots(string manifest)
    {
        var code = Regex.Replace(manifest, @"(?m)^[ \t]*#.*$", "");
        var m = Regex.Match(code, @"(?s)\bROOT_PROJECTS=\((?<body>.*?)\)");
        return m.Success ? Regex.Matches(m.Groups["body"].Value, "\"(?<p>[^\"]+)\"").Select(x => x.Groups["p"].Value).ToList() : [];
    }

    internal static IReadOnlyList<string> ImageBreaches(string dockerfile, string manifest)
    {
        var restored = Restored(dockerfile).ToHashSet(StringComparer.Ordinal);
        var published = Published(dockerfile);
        var roots = Roots(manifest).ToHashSet(StringComparer.Ordinal);
        var breaches = new List<string>();
        foreach (var p in published)
        {
            if (!roots.Contains(p)) breaches.Add("published_not_shipped");
            if (!restored.Contains(p)) breaches.Add("published_not_restored");
        }
        foreach (var r in roots)
            if (!published.Contains(r, StringComparer.Ordinal)) breaches.Add("shipped_not_published");
        return breaches.Distinct().ToList();
    }

    [Fact]
    public void The_image_restores_and_publishes_exactly_the_projects_the_manifest_ships()
    {
        var docker = Read("Dockerfile");
        var manifest = Read("scripts", "deploy-manifest.sh");
        var restored = Restored(docker);
        var published = Published(docker);
        var roots = Roots(manifest);
        output.WriteLine($"يُرَمِّمُ الـDockerfile {restored.Count} ({string.Join(", ", restored)}) · يَنشُرُ {published.Count} ({string.Join(", ", published)}) · "
            + $"جُذورُ القائِمَة {roots.Count} ({string.Join(", ", roots)})");
        Assert.True(published.Count >= 2 && roots.Count >= 2, $"أَداةٌ عَمياء: يَنشُرُ {published.Count} وجُذور {roots.Count}.");
        Assert.Contains("apps/V1.App/V1.App.csproj", published);
        Assert.Contains("apps/V1.Meter/V1.Meter.csproj", published);
        Assert.Empty(ImageBreaches(docker, manifest));
    }

    /// <summary>كُلُّ رَمزٍ بِصَفٍّ سالِبٍ (الحالُ الحَقيقِيّ) ومُوجِبٍ (عَيبٌ مَحقونٌ في المِلَفِّ الحَقيقِيّ): نَشرُ المُجَمِّعِ مَحذوفٌ مِن الصورَة،
    /// وجَذرُه مَحذوفٌ مِن القائِمَة، وتَرميمُه مَحذوف — ونَشرُ التَطبيقِ في سَطرِ تَعليقٍ لا يُعَدّ.</summary>
    [Fact]
    public void Every_image_breach_code_has_a_negative_and_an_injected_positive_row()
    {
        var docker = Read("Dockerfile");
        var manifest = Read("scripts", "deploy-manifest.sh");
        const string MeterRestore = "RUN dotnet restore apps/V1.Meter/V1.Meter.csproj --nologo";
        const string MeterPublish = "dotnet publish apps/V1.Meter/V1.Meter.csproj";
        const string MeterRoot = "    \"apps/V1.Meter/V1.Meter.csproj\"\n";
        Assert.Contains(MeterRestore, docker, StringComparison.Ordinal);
        Assert.Contains(MeterPublish, docker, StringComparison.Ordinal);
        Assert.Contains(MeterRoot, manifest, StringComparison.Ordinal);

        var cases = new (string Name, string Docker, string Manifest, string[] Codes)[]
        {
            ("الحالُ الحَقيقِيّ", docker, manifest, []),
            ("الصورَةُ لا تَنشُرُ المُجَمِّع", docker.Replace(MeterPublish, "true apps/V1.Meter/V1.Meter.csproj", StringComparison.Ordinal), manifest, ["shipped_not_published"]),
            ("القائِمَةُ بِلا جَذرِ المُجَمِّع", docker, manifest.Replace(MeterRoot, "", StringComparison.Ordinal), ["published_not_shipped"]),
            ("المُجَمِّعُ يُنشَرُ ولا يُرَمَّم", docker.Replace(MeterRestore, "", StringComparison.Ordinal), manifest, ["published_not_restored"]),
            ("التَرميمُ في تَعليقٍ لا يُرَمِّم", docker.Replace(MeterRestore, "# " + MeterRestore, StringComparison.Ordinal), manifest, ["published_not_restored"]),
        };
        var produced = new HashSet<string>(StringComparer.Ordinal);
        foreach (var c in cases)
        {
            var got = ImageBreaches(c.Docker, c.Manifest);
            output.WriteLine($"«{c.Name}» → {(got.Count == 0 ? "لا خَرق" : string.Join(", ", got))}");
            Assert.Equal(c.Codes, got);
            produced.UnionWith(got);
        }
        Assert.Equal(ImageCodes.Order(StringComparer.Ordinal), produced.Order(StringComparer.Ordinal));
    }

    // ═══ (ب) نُقطَةُ الدُخولِ التَطبيق، وأَمرُ المُجَمِّعِ في الدَليلِ مُجَلَّدُ نَشرِه ═══════════════════════════════════════════════════════

    /// <summary>رُموزُ الخَرق: <c>entrypoint_not_the_app</c> (نُقطَةُ دُخولٍ غَيرُ <c>["dotnet", "V1.App.dll"]</c> وَحدَها) ·
    /// <c>meter_not_in_image</c> (لا <c>-o /publish/…</c> لِلمُجَمِّع، أَو لا <c>WORKDIR /app</c> و<c>COPY --from=build /publish ./</c> في مَرحَلَةِ التَشغيل) ·
    /// <c>command_not_the_published_path</c> (أَمرُ المُجَمِّعِ في الدَليلِ يُسَمّي غَيرَ المَسارِ المَنشور).</summary>
    internal static readonly IReadOnlyList<string> CommandCodes = ["entrypoint_not_the_app", "meter_not_in_image", "command_not_the_published_path"];

    /// <summary>مُجَلَّدُ نَشرِ المُجَمِّعِ تَحتَ <c>/publish</c> (<c>-o /publish/X</c> في أَمرِ نَشرِه) — <c>null</c> إن لَم يوجَد.</summary>
    internal static string? MeterPublishDir(string dockerfile)
    {
        var code = DockerCode(dockerfile);
        var at = code.IndexOf("dotnet publish apps/V1.Meter/V1.Meter.csproj", StringComparison.Ordinal);
        if (at < 0) return null;
        var m = Regex.Match(code[at..], @"-o\s+/publish/(?<d>[\w./-]+)");
        return m.Success ? m.Groups["d"].Value : null;
    }

    internal static IReadOnlyList<string> Entrypoints(string dockerfile) =>
        Regex.Matches(DockerCode(dockerfile), @"(?m)^ENTRYPOINT\s+(?<e>.+)$").Select(m => m.Groups["e"].Value.Trim()).ToList();

    /// <summary>أَوامِرُ المُجَمِّعِ في الدَليل — المَسارُ الَّذي يَسبِقُ <c>--interval</c>.</summary>
    internal static IReadOnlyList<string> GuideCommands(string deploy) =>
        Regex.Matches(deploy, @"(?<p>[\w./-]*V1\.Meter\.dll)\s+--interval\s+\d+").Select(m => m.Groups["p"].Value).ToList();

    internal static IReadOnlyList<string> CommandBreaches(string dockerfile, string deploy)
    {
        var breaches = new List<string>();
        if (!Entrypoints(dockerfile).SequenceEqual(["[\"dotnet\", \"V1.App.dll\"]"])) breaches.Add("entrypoint_not_the_app");
        var code = DockerCode(dockerfile);
        var runtime = code[Math.Max(0, code.LastIndexOf("FROM ", StringComparison.Ordinal))..];
        var dir = MeterPublishDir(dockerfile);
        if (dir is null || !runtime.Contains("WORKDIR /app", StringComparison.Ordinal) || !runtime.Contains("COPY --from=build /publish ./", StringComparison.Ordinal))
            breaches.Add("meter_not_in_image");
        if (GuideCommands(deploy).Any(p => p != $"{dir}/V1.Meter.dll")) breaches.Add("command_not_the_published_path");
        return breaches;
    }

    [Fact]
    public void The_entrypoint_stays_the_app_and_the_guides_aggregator_command_is_the_published_path()
    {
        var docker = Read("Dockerfile");
        var deploy = Read("docs", "DEPLOY.md");
        var commands = GuideCommands(deploy);
        output.WriteLine($"نُقطَةُ الدُخول: {string.Join(" · ", Entrypoints(docker))} · مُجَلَّدُ المُجَمِّع: /publish/{MeterPublishDir(docker)} ← /app/{MeterPublishDir(docker)} · "
            + $"أَوامِرُ الدَليل: {commands.Count} ({string.Join(", ", commands.Distinct())})");
        Assert.True(commands.Count >= 2, $"أَداةٌ عَمياء: {commands.Count} أَمراً لِلمُجَمِّعِ في الدَليل.");
        Assert.Empty(CommandBreaches(docker, deploy));
    }

    /// <summary>الحَقن: نُقطَةُ الدُخولِ تَصيرُ المُجَمِّع، والنَشرُ إلى مُجَلَّدٍ آخَر، والنَسخُ إلى غَيرِ <c>WORKDIR</c>، والدَليلُ يُسقِطُ المُجَلَّد.</summary>
    [Fact]
    public void Every_command_breach_code_has_a_negative_and_an_injected_positive_row()
    {
        var docker = Read("Dockerfile");
        var deploy = Read("docs", "DEPLOY.md");
        const string Entry = "ENTRYPOINT [\"dotnet\", \"V1.App.dll\"]";
        Assert.Contains(Entry, docker, StringComparison.Ordinal);
        Assert.Contains("dotnet V1.Meter/V1.Meter.dll --interval 60", deploy, StringComparison.Ordinal);
        var cases = new (string Name, string Docker, string Deploy, string[] Codes)[]
        {
            ("الحالُ الحَقيقِيّ", docker, deploy, []),
            ("نُقطَةُ الدُخولِ المُجَمِّع", docker.Replace(Entry, "ENTRYPOINT [\"dotnet\", \"V1.Meter/V1.Meter.dll\"]", StringComparison.Ordinal), deploy, ["entrypoint_not_the_app"]),
            ("نُقطَتا دُخول", docker + "\nENTRYPOINT [\"dotnet\", \"V1.App.dll\"]\n", deploy, ["entrypoint_not_the_app"]),
            ("النَشرُ إلى مُجَلَّدٍ آخَر", docker.Replace("-o /publish/V1.Meter", "-o /publish/meter", StringComparison.Ordinal), deploy, ["command_not_the_published_path"]),
            ("النَسخُ إلى غَيرِ مُجَلَّدِ العَمَل", docker.Replace("COPY --from=build /publish ./", "COPY --from=build /publish/V1.App.dll ./", StringComparison.Ordinal), deploy, ["meter_not_in_image"]),
            ("الدَليلُ يُسقِطُ المُجَلَّد", docker, deploy.Replace("dotnet V1.Meter/V1.Meter.dll --interval 60", "dotnet V1.Meter.dll --interval 60", StringComparison.Ordinal), ["command_not_the_published_path"]),
        };
        var produced = new HashSet<string>(StringComparer.Ordinal);
        foreach (var c in cases)
        {
            var got = CommandBreaches(c.Docker, c.Deploy);
            output.WriteLine($"«{c.Name}» → {(got.Count == 0 ? "لا خَرق" : string.Join(", ", got))}");
            Assert.Equal(c.Codes, got);
            produced.UnionWith(got);
        }
        Assert.Equal(CommandCodes.Order(StringComparer.Ordinal), produced.Order(StringComparer.Ordinal));
    }

    // ═══ (ج) السُطورُ الَّتي يَقرَؤُها الدَليلُ هِيَ الَّتي يَطبَعُها الكود ═════════════════════════════════════════════════════════════════

    /// <summary>رُموزُ الخَرق: <c>boot_line_unprinted</c> (اقتِباسُ <c>[meter] boot</c> لا يُطابِقُ <see cref="MeterBootResult.Line"/>) ·
    /// <c>agg_line_unprinted</c> (اقتِباسُ <c>[meter-agg]</c> لا يُطابِقُ سَطراً يَكتُبُه المُجَمِّع).</summary>
    internal static readonly IReadOnlyList<string> LineCodes = ["boot_line_unprinted", "agg_line_unprinted"];

    /// <summary>الاقتِباساتُ المُضَمَّنَةُ (بَينَ عَلامَتَي <c>`</c>) الَّتي أَوَّلُها <c>[meter]</c> أَو <c>[meter-agg]</c>.</summary>
    internal static IReadOnlyList<string> Quotes(string doc) =>
        Regex.Matches(doc, @"`(?<q>\[meter(?:-agg)?\] [^`\n]+)`").Select(m => m.Groups["q"].Value).ToList();

    /// <summary>اقتِباسٌ ← تَعبيرٌ: الأَجزاءُ الثابِتَةُ حَرفِيَّةٌ بِتَرتيبِها، و<c>&lt;…&gt;</c> مَوضِعٌ مُتَغَيِّر.</summary>
    internal static Regex QuoteRegex(string quote, string hole) =>
        new("^" + string.Join(hole, Regex.Split(quote, "<[^<>]*>").Select(Regex.Escape)) + "$", RegexOptions.CultureInvariant);

    /// <summary>كُلُّ سَطرٍ يَكتُبُه المُجَمِّعُ بِـ<c>Line(…)</c>: نُصوصُه المُتَجاوِرَةُ (<c>" + $"</c>) مَوصولَةٌ، والبادِئَةُ <c>[meter-agg] </c> مِن <c>Line</c> نَفسِها.</summary>
    internal static IReadOnlyList<string> AggregatorLines(string source)
    {
        Assert.Contains("_out.WriteLine(\"[meter-agg] \" + text)", source, StringComparison.Ordinal);
        var joined = Regex.Replace(source, "\"\\s*\\+\\s*\\$?\"", "");
        return Regex.Matches(joined, "\\bLine\\(\\$?\"(?<t>.*?)\"\\);").Select(m => "[meter-agg] " + m.Groups["t"].Value).ToList();
    }

    internal static IReadOnlyList<string> LineBreaches(IEnumerable<string> quotes, IReadOnlyList<string> aggregatorLines, IReadOnlyList<string> bootLines)
    {
        var breaches = new List<string>();
        foreach (var q in quotes)
        {
            if (q.StartsWith("[meter] boot", StringComparison.Ordinal))
            {
                var rx = QuoteRegex(q, @"\d+");
                if (!bootLines.All(rx.IsMatch)) breaches.Add("boot_line_unprinted");
            }
            else
            {
                var rx = QuoteRegex(q, ".+?");
                if (!aggregatorLines.Any(rx.IsMatch)) breaches.Add("agg_line_unprinted");
            }
        }
        return breaches.Distinct().ToList();
    }

    private static IReadOnlyList<string> BootLines() =>
    [
        new MeterBootResult(1, 41, 41, OpenedGeneration: true).Line,
        new MeterBootResult(1, 41, 0, OpenedGeneration: false).Line,
        new MeterBootResult(12, 9, 3, OpenedGeneration: true).Line,
    ];

    [Fact]
    public void The_boot_and_aggregator_lines_the_guide_tells_the_owner_to_read_are_the_lines_the_code_prints()
    {
        var quotes = Quotes(Read("docs", "DEPLOY.md"));
        var agg = AggregatorLines(Read("apps", "V1.Meter", "Aggregator.cs"));
        var boot = quotes.Count(q => q.StartsWith("[meter] boot", StringComparison.Ordinal));
        output.WriteLine($"اقتِباساتُ الدَليل: {quotes.Count} (الإقلاع {boot} · المُجَمِّع {quotes.Count - boot}) · سُطورُ المُجَمِّعِ في مَصدَرِه: {agg.Count}");
        foreach (var q in quotes) output.WriteLine("  " + q);
        Assert.True(boot >= 1 && quotes.Count - boot >= 3 && agg.Count >= 10, $"أَداةٌ عَمياء: {boot} اقتِباسَ إقلاعٍ و{quotes.Count - boot} لِلمُجَمِّعِ و{agg.Count} سَطراً في المَصدَر.");
        Assert.Empty(LineBreaches(quotes, agg, BootLines()));
    }

    /// <summary>الحَقن: الدَليلُ يَقتَبِسُ اسماً قَديماً لِلحَقل، وسَطرُ المُجَمِّعِ يَتَغَيَّرُ في المَصدَر — وكِلاهُما يَحمَرّ؛ وجُزءٌ ثابِتٌ في غَيرِ تَرتيبِه يَحمَرّ.</summary>
    [Fact]
    public void Every_line_breach_code_has_a_negative_and_an_injected_positive_row()
    {
        var deploy = Read("docs", "DEPLOY.md");
        var source = Read("apps", "V1.Meter", "Aggregator.cs");
        var quotes = Quotes(deploy);
        var agg = AggregatorLines(source);
        Assert.Contains("epochs_new=", deploy, StringComparison.Ordinal);
        Assert.Contains("xid regression — boot the app (it opens a new generation)", source, StringComparison.Ordinal);

        var cases = new (string Name, IReadOnlyList<string> Quotes, IReadOnlyList<string> Agg, string[] Codes)[]
        {
            ("الحالُ الحَقيقِيّ", quotes, agg, []),
            ("الدَليلُ يَقتَبِسُ «epochs=» لا «epochs_new=»", Quotes(deploy.Replace("epochs_new=", "epochs=", StringComparison.Ordinal)), agg, ["boot_line_unprinted"]),
            ("المُجَمِّعُ يُغَيِّرُ سَطرَ التَراجُع", quotes, AggregatorLines(source.Replace("xid regression — boot the app", "xid regression — start the app", StringComparison.Ordinal)), ["agg_line_unprinted"]),
            ("جُزءانِ في غَيرِ تَرتيبِهِما", ["[meter-agg] · pulled <N> gen <G>"], agg, ["agg_line_unprinted"]),
            ("رَقمٌ لا يُطابِقُ الإقلاع", ["[meter] boot generation=<G> units=x epochs_new=<K>"], agg, ["boot_line_unprinted"]),
        };
        var produced = new HashSet<string>(StringComparer.Ordinal);
        foreach (var c in cases)
        {
            var got = LineBreaches(c.Quotes, c.Agg, BootLines());
            output.WriteLine($"«{c.Name}» → {(got.Count == 0 ? "لا خَرق" : string.Join(", ", got))}");
            Assert.Equal(c.Codes, got);
            produced.UnionWith(got);
        }
        Assert.Equal(LineCodes.Order(StringComparer.Ordinal), produced.Order(StringComparer.Ordinal));
    }
}
