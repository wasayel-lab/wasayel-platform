using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using ACommerce.Kit.Culture;
using ACommerce.Platform.Shared.Metering;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ G10 — المُجَمِّعُ عَمَلِيَّةٌ مُستَقِلَّة: لا يُشيرُ إلَيه أَحَد، ولا يُشيرُ إلّا إلى Shared وNpgsql، ولا يَكتُبُ جَدوَلَيه غَيرُه ═══
// (‏2026-11-12، الموجَة ١٣ W13-6·٤ — ADR-051 §١٢؛ التَصميم §10.2 و§12). الاستِقلالُ لا يَحفَظُه اسمُ المَجَلَّد (القاعِدَة ٢): مَشروعٌ تَحتَ libs/
// يُشيرُ إلى `V1.Meter` يَجعَلُه مَكتَبَةً يَدفَعُ إلَيها التَطبيق، ومُجَمِّعٌ يُشيرُ إلى القالِبِ يَجُرُّ Marten وWolverine إلى آلَةِ العَدّ، وجُملَةُ كِتابَةٍ
// عَلى `meter.agg_*` في التَطبيقِ تَكسِرُ «المَجاميعُ والمُؤَشِّرُ يُودَعانِ مَعاً» الَّذي يَقومُ عَلَيه «مَرَّةً واحِدَة». فَكُلُّ صَفٍّ يَطبَعُ ما فَحَصَ
// ويَفشَلُ عَلى الصِفر، وكاشِفُه يُقاسُ عَلى عَيِّنَةٍ مَحقونَةٍ قَبلَ أَن يُوثَقَ بِه (القاعِدَة ١٠).

public class MeterIndependenceTests(ITestOutputHelper output)
{
    private static string Root => ThemeZeroEquivalenceTests.RepoRoot;

    private static readonly string Sep = Path.DirectorySeparatorChar.ToString();

    private static bool Built(string path)
        => path.Contains($"{Sep}bin{Sep}", StringComparison.Ordinal) || path.Contains($"{Sep}obj{Sep}", StringComparison.Ordinal);

    private static string Rel(string path) => Path.GetRelativePath(Root, path).Replace('\\', '/');

    private static IEnumerable<string> Files(string dir, params string[] patterns)
        => patterns.SelectMany(p => Directory.EnumerateFiles(Path.Combine(Root, dir), p, SearchOption.AllDirectories)).Where(p => !Built(p));

    // ─── (١) لا مَشروعَ تَحتَ libs/ ولا التَطبيقُ يُشيرُ إلى المُجَمِّع ──────────────────────────────────────────────

    internal static readonly Regex ReferencesAggregator =
        new(@"<ProjectReference\s+Include\s*=\s*""[^""]*V1\.Meter[\\/]+V1\.Meter\.csproj""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    [Fact]
    public void No_project_under_libs_or_the_app_references_the_aggregator()
    {
        // ‏الكاشِفُ يُقاسُ أَوَّلاً: الإشارَةُ بِشَرطَتَي المِنَصَّتَينِ تُمسَك، ومَشروعٌ آخَرُ اسمُه يَحمِلُ «Meter» لا يُمسَك.
        Assert.Matches(ReferencesAggregator, @"<ProjectReference Include=""..\..\apps\V1.Meter\V1.Meter.csproj"" />");
        Assert.Matches(ReferencesAggregator, @"<ProjectReference Include=""../../apps/V1.Meter/V1.Meter.csproj""/>");
        Assert.DoesNotMatch(ReferencesAggregator, @"<ProjectReference Include=""..\..\libs\core\ACommerce.Platform.Shared\ACommerce.Platform.Shared.csproj"" />");

        var projects = Files("libs", "*.csproj").Concat(Files(Path.Combine("apps", "V1.App"), "*.csproj")).ToList();
        var referrers = projects.Where(p => ReferencesAggregator.IsMatch(File.ReadAllText(p))).Select(Rel).ToList();
        output.WriteLine($"[G10] مَشاريعُ مَفحوصَة {projects.Count} (libs/ وapps/V1.App) · تُشيرُ إلى V1.Meter {referrers.Count}");
        Assert.True(projects.Count >= 40, $"أَداةٌ عَمياء: {projects.Count} مَشروعاً مَقروءاً.");
        Assert.True(referrers.Count == 0, "مَشروعٌ يُشيرُ إلى المُجَمِّع — صارَ مَكتَبَةً يُدفَعُ إلَيها: " + string.Join(", ", referrers));
    }

    // ─── (٢) المُجَمِّعُ لا يُشيرُ إلّا إلى Shared وNpgsql، ولا يَستَمِع ─────────────────────────────────────────────

    /// <summary>ما يُخالِفُ «Shared وNpgsql وَحدَهُما، وبِلا Web» في مِلَفِّ مَشروع — فارِغٌ = سَليم.</summary>
    internal static IReadOnlyList<string> ProjectViolations(string csproj)
    {
        var doc = XDocument.Parse(csproj);
        var violations = new List<string>();
        var sdk = (string?)doc.Root?.Attribute("Sdk");
        if (sdk != "Microsoft.NET.Sdk") violations.Add($"Sdk={sdk}");
        foreach (var e in doc.Descendants().Where(e => e.Name.LocalName == "ProjectReference"))
        {
            var file = Path.GetFileName(((string?)e.Attribute("Include") ?? "").Replace('\\', '/'));
            if (file != "ACommerce.Platform.Shared.csproj") violations.Add($"ProjectReference {file}");
        }
        foreach (var e in doc.Descendants().Where(e => e.Name.LocalName == "PackageReference"))
        {
            var id = (string?)e.Attribute("Include");
            if (id != "Npgsql") violations.Add($"PackageReference {id}");
        }
        foreach (var e in doc.Descendants().Where(e => e.Name.LocalName == "FrameworkReference"))
            violations.Add($"FrameworkReference {(string?)e.Attribute("Include")}");
        return violations;
    }

    /// <summary>ما يَجعَلُ المُجَمِّعَ يَستَمِعُ أَو يَجُرُّ طَبَقَةَ التَطبيق: مُضيفُ ويب، أَو مَنفَذ، أَو Marten وWolverine، أَو القالِبُ وعُدَدُه.</summary>
    internal static readonly Regex ListensOrDrags = new(
        @"\b(WebApplication|Kestrel|HttpListener|TcpListener|UseUrls|MapGet|MapPost|ASPNETCORE_URLS|Marten|Wolverine|Weasel)\b|ACommerce\.(Templates|Kit|Platform\.(Hosting|MultiTenancy))\b",
        RegexOptions.CultureInvariant);

    [Fact]
    public void The_aggregator_references_only_Shared_and_Npgsql_and_listens_on_nothing()
    {
        var csprojPath = Path.Combine(Root, "apps", "V1.Meter", "V1.Meter.csproj");
        Assert.True(File.Exists(csprojPath), "apps/V1.Meter/V1.Meter.csproj غائِب.");
        var csproj = File.ReadAllText(csprojPath);

        // ‏الكاشِفُ يُقاسُ عَلى حَقنَين: إشارَةٌ إلى القالِب، ومُضيفُ ويب.
        var injected = csproj.Replace("</Project>",
            "<ItemGroup><ProjectReference Include=\"..\\..\\libs\\templates\\ACommerce.Templates.Customer.Marketplace\\ACommerce.Templates.Customer.Marketplace.csproj\" />"
            + "<PackageReference Include=\"Marten\" /></ItemGroup></Project>", StringComparison.Ordinal);
        Assert.Equal(new[] { "ProjectReference ACommerce.Templates.Customer.Marketplace.csproj", "PackageReference Marten" }, ProjectViolations(injected));
        Assert.Equal(new[] { "Sdk=Microsoft.NET.Sdk.Web" }, ProjectViolations(csproj.Replace("Sdk=\"Microsoft.NET.Sdk\"", "Sdk=\"Microsoft.NET.Sdk.Web\"", StringComparison.Ordinal)));
        Assert.Matches(ListensOrDrags, "var app = WebApplication.CreateBuilder(args).Build();");
        Assert.Matches(ListensOrDrags, "using ACommerce.Templates.Customer.Marketplace.Services.Metering;");
        Assert.DoesNotMatch(ListensOrDrags, "using ACommerce.Platform.Shared.Metering;");

        var doc = XDocument.Parse(csproj);
        var references = doc.Descendants().Where(e => e.Name.LocalName is "ProjectReference" or "PackageReference")
            .Select(e => Path.GetFileName(((string?)e.Attribute("Include") ?? "").Replace('\\', '/'))).ToList();
        var violations = ProjectViolations(csproj);

        var sources = Files(Path.Combine("apps", "V1.Meter"), "*.cs").ToList();
        var listening = sources.SelectMany(p => ListensOrDrags.Matches(File.ReadAllText(p)).Select(m => $"{Rel(p)}: {m.Value}")).ToList();
        output.WriteLine($"[G10] V1.Meter يُشيرُ إلى {references.Count}: {string.Join(", ", references)} · خُروق {violations.Count} · "
            + $"مِلَفّاتُ مَصدَرٍ {sources.Count} · مُستَمِعٌ أَو طَبَقَةُ تَطبيق {listening.Count}");

        Assert.Equal(new[] { "ACommerce.Platform.Shared.csproj", "Npgsql" }, references);
        Assert.True(violations.Count == 0, "المُجَمِّعُ يُشيرُ إلى ما سِوى Shared وNpgsql: " + string.Join(", ", violations));
        Assert.True(sources.Count >= 2, $"أَداةٌ عَمياء: {sources.Count} مِلَفَّ مَصدَر.");
        Assert.True(listening.Count == 0, "المُجَمِّعُ يَستَمِعُ أَو يَجُرُّ طَبَقَةَ التَطبيق: " + string.Join(" · ", listening));
    }

    // ─── (٣) لا مَسارَ كِتابَةٍ خارِجَ المُجَمِّعِ يَمَسُّ جَدوَلَيه ──────────────────────────────────────────────────────

    /// <summary>جُملَةُ كِتابَةٍ عَلى جَدوَلٍ لِلمُجَمِّع.</summary>
    internal static readonly Regex WritesAgg = new(
        @"(?i)\b(insert\s+into|update|delete\s+from|truncate(\s+table)?)\s+(only\s+)?meter\.agg_", RegexOptions.CultureInvariant);

    // ‏(‏2026-11-12، M6A-CNT-R2-1) `AggCursorSet` انقَسَمَ وِلادَةً وتَقَدُّماً — فَالجُمَلُ أَربَع.
    /// <summary>جُمَلُ <see cref="MeterSql"/> الَّتي تَكتُبُ جَدوَلَيِ المُجَمِّع — بِأَسمائِها، والصَفُّ يُثبِتُ أَنّ القائِمَةَ هِيَ ما يَكتُبُ فِعلاً.</summary>
    internal static readonly string[] AggWriters =
        [nameof(MeterSql.AggUpsertRange), nameof(MeterSql.AggCursorBirth), nameof(MeterSql.AggCursorAdvance), nameof(MeterSql.AggRebuild)];

    private static readonly Regex WriterReference = new(@"\b(" + string.Join("|", AggWriters) + @")\b", RegexOptions.CultureInvariant);

    /// <summary>مِلَفٌّ يُسَجِّلُ نُقطَةَ كِتابَة (وَسائِلُ ASP.NET وWolverine غَيرُ الآمِنَة).</summary>
    private static readonly Regex WriteEndpoint = new(
        @"\.Map(Post|Put|Delete|Patch|Methods)\s*\(|\[Wolverine(Post|Put|Delete|Patch)\b", RegexOptions.CultureInvariant);

    private static readonly Regex MentionsAgg = new(@"agg_|\bAgg(Daily|Cursor|UpsertRange|CursorBirth|CursorAdvance|Rebuild)\b", RegexOptions.CultureInvariant);

    [Fact]
    public void No_write_path_outside_the_aggregator_touches_its_tables()
    {
        // ‏الكاشِفُ يُقاسُ أَوَّلاً: الكِتاباتُ الأَربَعُ تُمسَك، والقِراءَةُ وعَمودُ التَراكُمِ في «do update set» لا.
        foreach (var write in new[] { "insert into meter.agg_daily (day) values (now())", "UPDATE meter.agg_cursor SET after_id = ''",
                                      "delete from meter.agg_daily", "truncate meter.agg_daily", "TRUNCATE TABLE meter.agg_cursor" })
            Assert.Matches(WritesAgg, write);
        foreach (var read in new[] { "select * from meter.agg_daily", "do update set entries = meter.agg_daily.entries + excluded.entries" })
            Assert.DoesNotMatch(WritesAgg, read);

        // ‏القائِمَةُ المُسَمّاةُ هِيَ ما يَكتُبُ في العَقدِ فِعلاً — لا أَقَلَّ ولا أَكثَر.
        var writers = typeof(MeterSql).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string) && WritesAgg.IsMatch((string)f.GetRawConstantValue()!))
            .Select(f => f.Name).Order(StringComparer.Ordinal).ToList();
        Assert.Equal(AggWriters.Order(StringComparer.Ordinal), writers);

        var contract = Path.Combine(Root, "libs", "core", "ACommerce.Platform.Shared", "Metering", "MeterSql.cs");
        var scanned = Files("libs", "*.cs", "*.razor", "*.sql", "*.json", "*.js")
            .Concat(Files(Path.Combine("apps", "V1.App"), "*.cs", "*.razor", "*.sql", "*.json", "*.js"))
            .Where(p => !string.Equals(p, contract, StringComparison.Ordinal))
            .Select(p => (Path: Rel(p), Text: File.ReadAllText(p))).ToList();
        var literal = scanned.Where(f => WritesAgg.IsMatch(f.Text)).Select(f => f.Path).ToList();
        var calls = scanned.Where(f => WriterReference.IsMatch(f.Text)).Select(f => f.Path).ToList();
        var endpoints = scanned.Where(f => WriteEndpoint.IsMatch(f.Text)).ToList();
        var endpointsMentioning = endpoints.Where(f => MentionsAgg.IsMatch(f.Text)).Select(f => f.Path).ToList();

        // ‏والكاتِبُ الوَحيدُ يُناديها كُلَّها (القاعِدَة ١: جُملَةُ كِتابَةٍ بِلا مُستَهلِكٍ لا تَبقى في العَقد).
        var aggregator = Files(Path.Combine("apps", "V1.Meter"), "*.cs").Select(File.ReadAllText).ToList();
        var used = AggWriters.Where(w => aggregator.Any(t => Regex.IsMatch(t, $@"\bMeterSql\.{w}\b"))).ToList();

        output.WriteLine($"[G10] جُمَلُ الكِتابَةِ في العَقد {writers.Count}: {string.Join(", ", writers)} · مِلَفّاتٌ مَفحوصَة خارِجَ العَقد {scanned.Count} · "
            + $"كِتابَةٌ حَرفِيَّةٌ عَلى agg_ {literal.Count} · نِداءُ جُمَلِ الكِتابَةِ خارِجَ المُجَمِّع {calls.Count} · "
            + $"مِلَفّاتُ نِقاطِ كِتابَة {endpoints.Count} تَذكُرُ agg_ {endpointsMentioning.Count} · يُناديها المُجَمِّع {used.Count}/{AggWriters.Length}");

        Assert.True(scanned.Count >= 300, $"أَداةٌ عَمياء: {scanned.Count} مِلَفّاً مَقروءاً.");
        Assert.True(endpoints.Count >= 5, $"أَداةٌ عَمياء: {endpoints.Count} مِلَفَّ نِقاطِ كِتابَة.");
        Assert.True(literal.Count == 0, "كِتابَةٌ عَلى جَدوَلِ المُجَمِّعِ خارِجَه: " + string.Join(", ", literal));
        Assert.True(calls.Count == 0, "جُملَةُ كِتابَةِ المُجَمِّعِ تُنادى خارِجَه: " + string.Join(", ", calls));
        Assert.True(endpointsMentioning.Count == 0, "نُقطَةُ كِتابَةٍ تَذكُرُ جَدوَلَ المُجَمِّع: " + string.Join(", ", endpointsMentioning));
        Assert.Equal(AggWriters, used);
    }

    // ─── (٤) حَدُّ اليَومِ نُسخَةٌ لا تَنجَرِف ──────────────────────────────────────────────────────────────────────

    /// <summary>المُجَمِّعُ لا يُشيرُ إلى عُدَّةِ الثَقافَة (الصَفُّ ٢)، فَحَدُّ يَومِه نُسخَةٌ مِن مِنطَقَةِ المَنَصَّة — والنُسخَةُ يَحرُسُها هذا الصَفّ (س٥).</summary>
    [Fact]
    public void The_aggregators_day_is_the_platforms_time_zone()
    {
        output.WriteLine($"[G10] حَدُّ اليَوم: MeterSql.DayZone = {MeterSql.DayZone} · CultureTime.DefaultTimeZone = {CultureTime.DefaultTimeZone}");
        Assert.Equal(CultureTime.DefaultTimeZone, MeterSql.DayZone);
        Assert.Contains($"at time zone '{MeterSql.DayZone}'", MeterSql.AggUpsertRange, StringComparison.Ordinal);
    }
}
