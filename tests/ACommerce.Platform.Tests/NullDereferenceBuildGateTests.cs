using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ إلغاءُ مَرجِعٍ قَد يَكونُ null في مِلَفِّ نِقاطِ القالِبِ = بِناءٌ أَحمَر — المُتَرجِمُ بِالإعدادِ نَفسِه (‏2026-11-07، مُراجَعَةُ C2b الجَولَةُ الأُولى · SEC-C2b-1) ═══
//
// **المَقيسُ قَبل** (عِندَ a9bc2cf): `MarketplaceTemplateExtensions.cs(825,39): warning CS8602` أَحَدُ 65 تَحذيراً في البِناء، وجَوابُه حَيّاً
// `NullReferenceException` عِندَ السَطرِ 824 بَعدَ أَن كُتِبَت حُقولُ العُضو. **وصارَ**: `.editorconfig` القالِبِ يَجعَلُ CS8602 خَطَأً في ذلكَ المِلَفِّ وَحدَه.
// **وهُنا الحارِس** (القاعِدَتانِ ٢ و١٠، نَسَقُ `CrefCompilerGateTests`): يَقرَأُ الإعدادَ مِن مَوضِعِه، ثُمَّ يُشَغِّلُ مُتَرجِمَ الـSDK نَفسَه بِذلكَ المِلَفِّ بِحَرفِه
// (`-analyzerconfig`) عَلى عَيِّنَةٍ مَحقونَة: إلغاءُ مَرجِعٍ قَد يَكونُ null بِاسمِ المِلَفِّ ⇒ خَطَأُ CS8602، وبِاسمٍ آخَرَ ⇒ تَحذيرٌ فَقَط، وبِلا إلغاءٍ ⇒ يُبنى.
// فَالحارِسُ يُفَرِّقُ لا يُوافِق، ويُثبِتُ أَنّ القِسمَ يُطابِقُ المِلَفَّ الَّذي يَحرُسُه.
public class NullDereferenceBuildGateTests(ITestOutputHelper output)
{
    private const string Guarded = "MarketplaceTemplateExtensions.cs";

    private static string ConfigPath => Path.Combine(ThemeZeroEquivalenceTests.RepoRoot,
        "libs", "templates", "ACommerce.Templates.Customer.Marketplace", ".editorconfig");

    [Fact]
    public void The_template_config_makes_CS8602_an_error_in_the_endpoints_file_and_that_file_exists_beside_it()
    {
        var text = File.ReadAllText(ConfigPath);
        var section = Regex.Match(text, @"^\[" + Regex.Escape(Guarded) + @"\]\s*$(?<body>(?:\r?\n(?!\[).*)*)", RegexOptions.Multiline);
        Assert.True(section.Success, $"لا قِسمَ [{Guarded}] في {ConfigPath}");
        Assert.Matches(@"(?m)^\s*dotnet_diagnostic\.CS8602\.severity\s*=\s*error\s*$", section.Groups["body"].Value);
        Assert.True(File.Exists(Path.Combine(Path.GetDirectoryName(ConfigPath)!, Guarded)), $"القِسمُ يُسَمّي مِلَفّاً غائِباً: {Guarded}");
        output.WriteLine($"[{Guarded}] ⇒ CS8602 = error");
    }

    private (int Code, string Text) Compile(string fileName, string source)
    {
        var c = CrefCompilerGateTests.Compiler();
        Assert.True(c is not null, "أَداةٌ غائِبَة: مُتَرجِمُ الـSDK (sdk/*/Roslyn/bincore/csc.dll) بِجِوارِ وَقتِ التَشغيل.");
        var (dotnet, csc, rt) = c!.Value;
        var dir = Directory.CreateTempSubdirectory("wasayel-cs8602-");
        try
        {
            var config = Path.Combine(dir.FullName, ".editorconfig");
            File.Copy(ConfigPath, config);
            var file = Path.Combine(dir.FullName, fileName);
            File.WriteAllText(file, source, new UTF8Encoding(false));
            var psi = new ProcessStartInfo(dotnet)
            {
                RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
            };
            foreach (var a in new[]
                     {
                         "exec", csc, "-nologo", "-noconfig", "-nostdlib", "-t:library", "-langversion:latest", "-nullable:enable",
                         "-r:" + Path.Combine(rt, "System.Private.CoreLib.dll"), "-r:" + Path.Combine(rt, "System.Runtime.dll"),
                         "-analyzerconfig:" + config, "-out:" + Path.Combine(dir.FullName, "Sample.dll"), file,
                     })
                psi.ArgumentList.Add(a);
            using var p = Process.Start(psi)!;
            var outText = p.StandardOutput.ReadToEndAsync();
            var err = p.StandardError.ReadToEndAsync();
            Assert.True(p.WaitForExit(60_000), "المُتَرجِمُ لَم يَنتَهِ في دَقيقَة.");
            var text = (outText.Result + err.Result).Trim();
            output.WriteLine($"csc {fileName} → {p.ExitCode} · {text}");
            return (p.ExitCode, text);
        }
        finally { dir.Delete(recursive: true); }
    }

    private const string Deref = "namespace Probe;\npublic static class Sample\n{\n    public static int Length(string? s) => s.Length;\n}\n";
    private const string Guard = "namespace Probe;\npublic static class Sample\n{\n    public static int Length(string? s) => s is null ? 0 : s.Length;\n}\n";

    /// <summary><b>حَقنُ عَيب</b>: العَيِّنَةُ نَفسُها بِاسمِ المِلَفِّ المَحروسِ تَحمَرّ (<c>error CS8602</c>)، وبِاسمٍ آخَرَ تُبنى بِتَحذير — والمَحروسَةُ تُبنى.</summary>
    [Fact]
    public void The_sdk_compiler_with_the_template_config_goes_red_on_a_null_dereference_in_that_file_only()
    {
        var (bad, badText) = Compile(Guarded, Deref);
        Assert.NotEqual(0, bad);
        Assert.Matches(@"error CS8602", badText);
        var (elsewhere, elsewhereText) = Compile("Other.cs", Deref);
        Assert.True(elsewhere == 0, "القِسمُ أَوسَعُ مِن مِلَفِّه: " + elsewhereText);
        Assert.Matches(@"warning CS8602", elsewhereText);
        var (good, goodText) = Compile(Guarded, Guard);
        Assert.True(good == 0, "عَيِّنَةٌ بِلا إلغاءٍ حَمِرَت: " + goodText);
    }
}
