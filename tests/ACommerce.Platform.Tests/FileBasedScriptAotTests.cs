using System.Text.RegularExpressions;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ كُلُّ سكربتِ مِلَفٍّ واحِدٍ يُطفِئُ AOT بِسَطرِه — لا سَبعَةٌ مِن اثنَي عَشَر (‏2026-11-04، الموجَة ١٢ G1 · ١) ═══
//
// **المَقيسُ قَبل**: تَطبيقُ المِلَفِّ الواحِدِ في .NET 10 يَفتَرِضُ `PublishAot=true`، فَيَستَعيدُ `Microsoft.DotNet.ILCompiler` حَتّى لِـ`dotnet build`.
// ويَومَ أُعلِنَ GHSA-p3jp-9v83-76w9 (عالٍ) عَلى ‏[10.0.0، 10.0.14) صارَ `Directory.Build.props` (‏NU1903 خَطَأٌ عَمداً) يُسقِطُ بِناءَ كُلِّ سكربتٍ
// لا يُطفِئُه: خَمسَةٌ مِن اثنَي عَشَر (`dump-computed` و`verify-runtime` و`marten-schema` و`repair-role-floor` و`bench-search`) خَرَجَت 1 بِـSDK ‏10.0.402،
// وصَفّا `DumpComputedTargetGuardTests` أَحمَران (‏«Expected 2, Actual 1»). والسَبعَةُ الأُخرى كانَت تَحمِلُ السَطرَ — بِلا حاجِزٍ يَقولُ إنّ الجَديدَ يَحمِلُه.
// فَالحَدُّ هُنا آلِيٌّ (القاعِدَة ٢): كُلُّ مِلَفٍّ في `scripts/` يَبدَأُ بِـ`#!/usr/bin/env dotnet` يَحمِلُ `#:property PublishAot=false` بَينَ أَسطُرِ `#:`
// في رَأسِه، ويُطبَعُ العَدَدُ ويَفشَلُ عَلى الصِفر (القاعِدَة ١٠). **ولا يُضعَفُ NU1903 ولا يُكتَمُ الإعلان** — السكربتُ لا يُنشَرُ AOT أَصلاً.

public class FileBasedScriptAotTests(ITestOutputHelper output)
{
    private static readonly Regex Directive = new(@"^#:property\s+PublishAot\s*=\s*false\s*$");

    /// <summary><c>null</c> إن كانَ النَصُّ سكربتاً يُطفِئُ AOT في رَأسِه (أَسطُرُ <c>#:</c> والتَعليقاتُ والفَراغُ قَبلَ أَوَّلِ سَطرِ كود)، وإلّا رَمزُ الخَرق:
    /// <c>not_a_file_based_app</c> (لا <c>#!/usr/bin/env dotnet</c> في أَوَّلِه) أَو <c>aot_not_disabled</c>.</summary>
    internal static string? AotBreach(string text)
    {
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        if (lines.Length == 0 || lines[0].Trim() != "#!/usr/bin/env dotnet") return "not_a_file_based_app";
        foreach (var raw in lines.Skip(1))
        {
            var line = raw.Trim();
            if (Directive.IsMatch(line)) return null;
            if (line.Length == 0 || line.StartsWith("//", StringComparison.Ordinal) || line.StartsWith("#:", StringComparison.Ordinal)) continue;
            break;   // ‏أَوَّلُ سَطرِ كود — والسَطرُ بَعدَه لا يُقرَأُ تَوجيهاً
        }
        return "aot_not_disabled";
    }

    [Fact]
    public void Every_file_based_script_disables_aot_in_its_header()
    {
        var dir = Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, "scripts");
        var scripts = Directory.EnumerateFiles(dir, "*.cs").OrderBy(f => f, StringComparer.Ordinal).ToList();
        var wrong = new List<string>();
        foreach (var f in scripts)
        {
            var breach = AotBreach(File.ReadAllText(f));
            if (breach is not null) wrong.Add($"{Path.GetFileName(f)}: {breach}");
        }
        output.WriteLine($"سكربتاتُ المِلَفِّ الواحِدِ المَفحوصَة {scripts.Count} · خُروق {wrong.Count}");
        Assert.True(scripts.Count >= 12, $"أَداةٌ عَمياء: {scripts.Count} سكربتاً مَقروءاً في scripts/ (المَقيسُ ‏2026-11-04: ‏12).");
        Assert.True(wrong.Count == 0, string.Join("\n", wrong));
    }

    /// <summary>مُوجَب وسالِب لِكُلِّ رَمز: السَطرُ بَعدَ تَعليقٍ أَو تَوجيهٍ آخَرَ يُقبَل، وغِيابُه أَو مَجيئُه بَعدَ أَوَّلِ كودٍ أَو بِـ<c>true</c> يُرَدّ.</summary>
    [Theory]
    [InlineData("#!/usr/bin/env dotnet\n#:property PublishAot=false\nConsole.WriteLine(1);\n", null)]
    [InlineData("#!/usr/bin/env dotnet\n// سَبَب\n#:property X=true\n#:property PublishAot=false\n#:package Npgsql@9.0.5\nusing System;\n", null)]
    [InlineData("#!/usr/bin/env dotnet\r\n#:property PublishAot=false\r\nusing System;\r\n", null)]
    [InlineData("#!/usr/bin/env dotnet\n#:property JsonSerializerIsReflectionEnabledByDefault=true\nusing System;\n", "aot_not_disabled")]
    [InlineData("#!/usr/bin/env dotnet\n#:property PublishAot=true\nusing System;\n", "aot_not_disabled")]
    [InlineData("#!/usr/bin/env dotnet\nusing System;\n// #:property PublishAot=false\n#:property PublishAot=false\n", "aot_not_disabled")]
    [InlineData("// #:property PublishAot=false\nusing System;\n", "not_a_file_based_app")]
    public void The_aot_checker_reads_the_header_only(string text, string? expected) =>
        Assert.Equal(expected, AotBreach(text));
}
