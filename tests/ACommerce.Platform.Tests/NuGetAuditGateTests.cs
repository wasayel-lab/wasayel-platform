using System.Xml.Linq;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ ثَغرَةٌ عالِيَةٌ أَو حَرِجَةٌ في حُزمَةٍ تُبنى = بِناءٌ أَحمَر (‏2026-10-25، تَدقيقُ الإطلاق lens-checkers-ci-1، حاجِبَة) ═══════════════════
//
// **المَقيسُ قَبل** (`e7cb90d`): `dotnet build PlatformV1.slnx -c Release` يَطبَعُ `NU1903` لِـ`System.Security.Cryptography.Pkcs` ‏10.0.0
// (‏GHSA-846f-f764-76mx، مِن MimeKit 4.17.0) ولِـ`Microsoft.AspNetCore.SignalR.Common` ‏10.0.10 (‏GHSA-334f-vvp4-7g6p) — ثُمَّ «0 Error(s)».
// فَالتَحذيرُ يُطبَعُ في كُلِّ بِناءٍ ولا يَقرَؤُه أَحَد، ولا ذِكرَ لَه في docs ولا في السِجِلّ. **وصارَ** `Directory.Build.props` يَجعَلُ NU1903
// وNU1904 خَطَأَين ويَكتُبُ الفَحصَ ووَضعَه ومُستَواه، و`Directory.Packages.props` يَرفَعُ الحُزمَتَينِ إلى ‏10.0.13.
//
// **والحَدُّ لا يَحفَظُه اسمُه (القاعِدَة ٢)**: سَطرٌ واحِدٌ يُطفِئُه — `NuGetAudit=false`، أَو `NuGetAuditMode=direct` (فَتَغيبُ Pkcs لِأَنَّها
// مُتَعَدِّيَة)، أَو `NuGetAuditLevel=critical` (فَلا يُطبَعُ NU1903 أَصلاً)، أَو `NoWarn`/`WarningsNotAsErrors`، أَو csproj يُعيدُ
// `WarningsAsErrors` بِلا `$(WarningsAsErrors)` فَيُسقِطُ الموروث. فَهذِه الصُفوفُ تَقرَأُ المِلَفَّ وكُلَّ csproj، ولِكُلِّ رَمزٍ عَيِّنَةٌ مَحقونَة.
// **والحَقنُ الحَيُّ مَقيسٌ في رِسالَةِ الكوميت**: هذا المِلَفُّ بِحُزَمِ `e7cb90d` ⇒ `dotnet restore` رَمزُ 1 بِخَمسَةِ `error NU1903`.

public class NuGetAuditGateTests(ITestOutputHelper output)
{
    private static string RepoRoot => ThemeZeroEquivalenceTests.RepoRoot;

    private static readonly string[] Codes = ["NU1903", "NU1904"];

    /// <summary>قيمَةُ الخاصِّيَّةِ <paramref name="name"/> كَما يَراها MSBuild في المِلَفِّ وَحدَه: آخِرُ تَعريفٍ يَغلِب.</summary>
    private static string? Property(XDocument doc, string name) =>
        doc.Descendants().Where(e => e.Name.LocalName == name && e.Parent?.Name.LocalName == "PropertyGroup").Select(e => e.Value.Trim()).LastOrDefault();

    private static bool Lists(string? value, string code) =>
        value is not null && value.Split([';', ',', ' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries).Any(t => t.Equals(code, StringComparison.OrdinalIgnoreCase));

    /// <summary><b>خُروقُ البَوّابَةِ في <c>Directory.Build.props</c></b> — فارِغَةٌ إن كانَ الفَحصُ مُشَغَّلاً بِكُلِّ التَبَعِيّاتِ ويُبَلِّغُ عَن
    /// العالِيَةِ والحَرِجَةِ ويُفشِلُ عَلَيهِما. الرُموز: <c>audit_off</c> · <c>transitive_unaudited</c> · <c>level_above_high</c> ·
    /// <c>high_not_error</c> · <c>critical_not_error</c> · <c>audit_silenced</c>.</summary>
    internal static IReadOnlyList<string> PropsGaps(string xml)
    {
        var doc = XDocument.Parse(xml);
        var gaps = new List<string>();
        if (!string.Equals(Property(doc, "NuGetAudit"), "true", StringComparison.OrdinalIgnoreCase)) gaps.Add("audit_off");
        if (!string.Equals(Property(doc, "NuGetAuditMode"), "all", StringComparison.OrdinalIgnoreCase)) gaps.Add("transitive_unaudited");
        if (Property(doc, "NuGetAuditLevel")?.ToLowerInvariant() is not ("low" or "moderate" or "high")) gaps.Add("level_above_high");
        var errors = Property(doc, "WarningsAsErrors");
        if (!Lists(errors, "NU1903")) gaps.Add("high_not_error");
        if (!Lists(errors, "NU1904")) gaps.Add("critical_not_error");
        if (Codes.Any(c => Lists(Property(doc, "NoWarn"), c) || Lists(Property(doc, "WarningsNotAsErrors"), c))) gaps.Add("audit_silenced");
        return gaps;
    }

    /// <summary><b>خُروقُ مَشروعٍ واحِدٍ يُضَيِّقُ ما وَرِثَه</b>: <c>audit_overridden</c> (أَيُّ <c>NuGetAudit*</c> في csproj) · <c>audit_silenced</c>
    /// (‏NU1903/NU1904 في <c>NoWarn</c> أَو <c>WarningsNotAsErrors</c>) · <c>inherited_errors_dropped</c> (<c>WarningsAsErrors</c> بِلا
    /// <c>$(WarningsAsErrors)</c>).</summary>
    internal static IReadOnlyList<string> ProjectGaps(string xml)
    {
        var doc = XDocument.Parse(xml);
        var gaps = new List<string>();
        var props = doc.Descendants().Where(e => e.Parent?.Name.LocalName == "PropertyGroup").ToList();
        if (props.Any(e => e.Name.LocalName.StartsWith("NuGetAudit", StringComparison.Ordinal))) gaps.Add("audit_overridden");
        if (props.Any(e => e.Name.LocalName is "NoWarn" or "WarningsNotAsErrors" && Codes.Any(c => Lists(e.Value, c)))) gaps.Add("audit_silenced");
        if (props.Any(e => e.Name.LocalName == "WarningsAsErrors" && !e.Value.Contains("$(WarningsAsErrors)", StringComparison.Ordinal)))
            gaps.Add("inherited_errors_dropped");
        // ‏2026-10-25 (مُراجَعَةُ تَيّارِ W8-5 الجَولَة ١، صُغرى): `NoWarn` عَلى حُزمَةٍ بِعَينِها — سِمَةً أَو عُنصُراً تَحتَ PackageReference/PackageVersion —
        // يُسكِتُ NU1903/NU1904 لَها وَحدَها، والفاحِصُ كانَ يَقرَأُ PropertyGroup وَحدَه.
        var packages = doc.Descendants().Where(e => e.Name.LocalName is "PackageReference" or "PackageVersion").ToList();
        if (packages.Any(e => Codes.Any(c => Lists(e.Attribute("NoWarn")?.Value, c))
                              || e.Elements().Any(x => x.Name.LocalName == "NoWarn" && Codes.Any(c => Lists(x.Value, c)))))
            gaps.Add("package_silenced");
        return gaps;
    }

    [Fact]
    public void The_build_props_fail_the_build_on_a_high_or_critical_advisory_in_any_package()
    {
        var path = Path.Combine(RepoRoot, "Directory.Build.props");
        var gaps = PropsGaps(File.ReadAllText(path));
        output.WriteLine($"Directory.Build.props: خُروق {gaps.Count}{(gaps.Count > 0 ? " — " + string.Join(", ", gaps) : "")}");
        Assert.Empty(gaps);
    }

    [Fact]
    public void No_project_narrows_the_audit_it_inherits()
    {
        var projects = Directory.EnumerateFiles(RepoRoot, "*.csproj", SearchOption.AllDirectories)
            .Where(p => !p.Split(Path.DirectorySeparatorChar).Any(seg => seg is "obj" or "bin" or "node_modules"))
            .ToList();
        var wrong = projects.Select(p => (Path: Path.GetRelativePath(RepoRoot, p), Gaps: ProjectGaps(File.ReadAllText(p))))
            .Where(x => x.Gaps.Count > 0).Select(x => $"{x.Path}: {string.Join(", ", x.Gaps)}").ToList();
        output.WriteLine($"مَشاريعُ مَقروءَة: {projects.Count} · تُضَيِّقُ الفَحص: {wrong.Count}");
        Assert.True(projects.Count > 20, $"أَداةٌ عَمياء: {projects.Count} csproj مَقروءاً.");
        Assert.True(wrong.Count == 0, string.Join("\n", wrong));
        // ‏ولا مِلَفَّ أَهدافٍ مُشتَرَكٌ يُعيدُ تَعريفَها بَعدَ الـprops — يَومَ يولَدُ يَمُرُّ بِالفاحِصِ نَفسِه.
        var targets = Path.Combine(RepoRoot, "Directory.Build.targets");
        if (File.Exists(targets)) Assert.Empty(ProjectGaps(File.ReadAllText(targets)));
        // ‏2026-10-25 (مُراجَعَةُ تَيّارِ W8-5 الجَولَة ١): و`Directory.Packages.props` — يَستَورِدُه CPM بَعدَ `Directory.Build.props`، فَـ`NuGetAuditMode=direct`
        // أَو `NoWarn` فيه يَغلِبُ البَوّابَةَ وهي خَضراء؛ يُقرَأُ بِالفاحِصِ نَفسِه، ويُطبَعُ ما وُجِد.
        var cpm = ProjectGaps(File.ReadAllText(Path.Combine(RepoRoot, "Directory.Packages.props")));
        output.WriteLine($"Directory.Packages.props: خُروق {cpm.Count}{(cpm.Count > 0 ? " — " + string.Join(", ", cpm) : "")}");
        Assert.Empty(cpm);
    }

    private const string Good = """
        <Project>
          <PropertyGroup>
            <NuGetAudit>true</NuGetAudit>
            <NuGetAuditMode>all</NuGetAuditMode>
            <NuGetAuditLevel>low</NuGetAuditLevel>
            <WarningsAsErrors>$(WarningsAsErrors);NU1903;NU1904</WarningsAsErrors>
          </PropertyGroup>
        </Project>
        """;

    private static string With(string xml, string from, string to)
    {
        Assert.Contains(from, xml, StringComparison.Ordinal);
        return xml.Replace(from, to, StringComparison.Ordinal);
    }

    /// <summary><b>حَقنُ عَيب — لِكُلِّ رَمزٍ عَيِّنَتُه الموجِبَة</b>، والعَيِّنَةُ السَليمَةُ (سالِبَتُها كُلِّها) بِلا خَرق.</summary>
    [Fact]
    public void Every_props_gap_code_goes_red_on_its_injected_sample_and_only_there()
    {
        Assert.Empty(PropsGaps(Good));
        var cases = new (string Name, string Xml, string Code)[]
        {
            ("الفَحصُ مُطفَأ", With(Good, "<NuGetAudit>true</NuGetAudit>", "<NuGetAudit>false</NuGetAudit>"), "audit_off"),
            ("الفَحصُ غَيرُ مُعلَن", With(Good, "<NuGetAudit>true</NuGetAudit>", ""), "audit_off"),
            ("المُباشِرَةُ وَحدَها", With(Good, "<NuGetAuditMode>all</NuGetAuditMode>", "<NuGetAuditMode>direct</NuGetAuditMode>"), "transitive_unaudited"),
            ("الحَرِجَةُ وَحدَها تُبَلَّغ", With(Good, "<NuGetAuditLevel>low</NuGetAuditLevel>", "<NuGetAuditLevel>critical</NuGetAuditLevel>"), "level_above_high"),
            ("العالِيَةُ تَحذير", With(Good, "NU1903;NU1904", "NU1904"), "high_not_error"),
            ("الحَرِجَةُ تَحذير", With(Good, "NU1903;NU1904", "NU1903"), "critical_not_error"),
            ("NoWarn يُسكِت", With(Good, "</PropertyGroup>", "<NoWarn>$(NoWarn);NU1903</NoWarn></PropertyGroup>"), "audit_silenced"),
            ("WarningsNotAsErrors يُسكِت", With(Good, "</PropertyGroup>", "<WarningsNotAsErrors>NU1904</WarningsNotAsErrors></PropertyGroup>"), "audit_silenced"),
            ("تَعريفٌ ثانٍ يَغلِب", With(Good, "</Project>", "<PropertyGroup><NuGetAuditMode>direct</NuGetAuditMode></PropertyGroup></Project>"), "transitive_unaudited"),
        };
        var missed = new List<string>();
        foreach (var c in cases)
        {
            var got = PropsGaps(c.Xml);
            output.WriteLine($"«{c.Name}» → {string.Join(", ", got)}");
            if (!got.SequenceEqual([c.Code])) missed.Add($"«{c.Name}»: [{string.Join(", ", got)}] لا [{c.Code}]");
        }
        output.WriteLine($"حُقِنَ {cases.Length} شَكلاً؛ أَخطَأَ الفاحِصُ في {missed.Count}");
        Assert.True(missed.Count == 0, string.Join("\n", missed));
    }

    [Fact]
    public void Every_project_gap_code_goes_red_on_its_injected_sample_and_only_there()
    {
        const string Plain = """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><Nullable>enable</Nullable></PropertyGroup>
            </Project>
            """;
        Assert.Empty(ProjectGaps(Plain));
        Assert.Empty(ProjectGaps(With(Plain, "</PropertyGroup>", "<WarningsAsErrors>$(WarningsAsErrors);CS8600</WarningsAsErrors></PropertyGroup>")));
        var cases = new (string Name, string Xml, string Code)[]
        {
            ("مَشروعٌ يُطفِئُ الفَحص", With(Plain, "</PropertyGroup>", "<NuGetAudit>false</NuGetAudit></PropertyGroup>"), "audit_overridden"),
            ("مَشروعٌ يُضَيِّقُ الوَضع", With(Plain, "</PropertyGroup>", "<NuGetAuditMode>direct</NuGetAuditMode></PropertyGroup>"), "audit_overridden"),
            ("مَشروعٌ يُسكِتُ العالِيَة", With(Plain, "</PropertyGroup>", "<NoWarn>$(NoWarn);NU1903</NoWarn></PropertyGroup>"), "audit_silenced"),
            ("مَشروعٌ يُسقِطُ الموروث", With(Plain, "</PropertyGroup>", "<WarningsAsErrors>CS8600</WarningsAsErrors></PropertyGroup>"), "inherited_errors_dropped"),
            ("حُزمَةٌ تُسكِتُ العالِيَةَ بِسِمَة", With(Plain, "</Project>", "<ItemGroup><PackageReference Include=\"MimeKit\" NoWarn=\"NU1903\" /></ItemGroup></Project>"), "package_silenced"),
            ("نُسخَةٌ مَركَزِيَّةٌ تُسكِتُ بِعُنصُر", With(Plain, "</Project>", "<ItemGroup><PackageVersion Include=\"MimeKit\" Version=\"4.17.0\"><NoWarn>NU1701;NU1904</NoWarn></PackageVersion></ItemGroup></Project>"), "package_silenced"),
            ("Directory.Packages.props يُضَيِّقُ الوَضع", With(Plain, "</PropertyGroup>", "<ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally><NuGetAuditMode>direct</NuGetAuditMode></PropertyGroup>"), "audit_overridden"),
        };
        // ‏وNoWarn لِرَمزٍ آخَرَ عَلى حُزمَةٍ لا يُعَدُّ إسكاتاً لِلفَحص.
        Assert.Empty(ProjectGaps(With(Plain, "</Project>", "<ItemGroup><PackageReference Include=\"X\" NoWarn=\"NU1701\" /></ItemGroup></Project>")));
        var missed = new List<string>();
        foreach (var c in cases)
        {
            var got = ProjectGaps(c.Xml);
            output.WriteLine($"«{c.Name}» → {string.Join(", ", got)}");
            if (!got.SequenceEqual([c.Code])) missed.Add($"«{c.Name}»: [{string.Join(", ", got)}] لا [{c.Code}]");
        }
        output.WriteLine($"حُقِنَ {cases.Length} شَكلاً؛ أَخطَأَ الفاحِصُ في {missed.Count}");
        Assert.True(missed.Count == 0, string.Join("\n", missed));
    }
}
