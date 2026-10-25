using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ مَرجِعُ cref يُسَمّي ما لا وُجودَ لَه = بِناءٌ أَحمَر — المُتَرجِمُ نَفسُه لا مُحَلِّلٌ ثانٍ (‏2026-10-25، مُراجَعَةُ تَيّارِ W8-9 الجَولَة ١) ═══
//
// **المَقيسُ قَبل** (عِندَ `fefc6c8`): ‏`RazorPageEntryPointsTests` كَتَبَ «المُتَرجِمُ لا يَفحَصُ cref» وبَنى مُحَلِّلاً بِالانعِكاسِ لِصَفَحاتِ razor
// وَحدَها يَتَجاهَلُ التَوقيع — فَمَرجِعٌ إلى حِملٍ حُذِفَ يُحَلُّ، ومَرجِعٌ مَكسورٌ في `.cs` لا يُرى. والمُتَرجِمُ يَفحَصُه حينَ يُطلَبُ مِنه مِلَفُّ
// التَوثيق: بِـ`-p:GenerateDocumentationFile=true` ‏11 مَرجِعاً لا يُحَلّ (CS1574 — `FlowDefinition.cs` و`User.cs` و`ObligationDefinition.cs`
// وسَبعَةٌ في `SharedModels.cs` وواحِدٌ في `DealMoneyEffectTokenTests.cs`)، ويَفحَصُ `@code` في razor (حَقنُ عَيبٍ مُؤَقَّتٍ في `CheckoutPage.razor:246`
// ⇒ CS1574 بِسَطرِه). **وصارَ**: `Directory.Build.props` يُوَلِّدُ المِلَفَّ ويَجعَلُ CS1574/CS1580 خَطَأَين، والأَحَدَ عَشَرَ صُحِّحَت.
// **وهُنا الحارِس**: يَحمَرُّ إن أُطفِئَ التَوليدُ أَو سَقَطَ أَحَدُ الرَمزَينِ أَو صارَ المِلَفُّ يُنشَر، ويُشَغِّلُ مُتَرجِمَ الـSDK نَفسَه (‏`csc.dll`)
// بِالرَمزَينِ كَما يَقرَؤُهُما مِن الـprops عَلى عَيِّنَةٍ مَحقونَة — فَيُثبِتُ أَنّ ما في الـprops يُحمِرُّ مَرجِعاً مَكسوراً ولا يُحمِرُّ صَحيحاً (القاعِدَة ١٠).
public class CrefCompilerGateTests(ITestOutputHelper output)
{
    private static string RepoRoot => ThemeZeroEquivalenceTests.RepoRoot;

    private static string Props() => File.ReadAllText(Path.Combine(RepoRoot, "Directory.Build.props"));

    /// <summary>رُموزُ التَحذيرِ الَّتي يَجعَلُها الـprops أَخطاءً — مِن كُلِّ <c>&lt;WarningsAsErrors&gt;</c> فيه.</summary>
    internal static IReadOnlyList<string> WarningsAsErrors(string props)
        => Regex.Matches(props, "<WarningsAsErrors>(?<v>[^<]*)</WarningsAsErrors>").SelectMany(m => m.Groups["v"].Value.Split(';'))
            .Select(c => c.Trim()).Where(c => c.Length > 0 && !c.StartsWith("$(", StringComparison.Ordinal)).Distinct().ToList();

    [Fact]
    public void The_build_generates_documentation_and_makes_unresolved_crefs_errors_without_publishing_the_file()
    {
        var props = Props();
        Assert.Contains("<GenerateDocumentationFile>true</GenerateDocumentationFile>", props, StringComparison.Ordinal);
        var errors = WarningsAsErrors(props);
        output.WriteLine($"WarningsAsErrors: {string.Join(" · ", errors)}");
        Assert.Contains("CS1574", errors);
        Assert.Contains("CS1580", errors);
        foreach (var p in new[] { "PublishDocumentationFile", "PublishDocumentationFiles", "PublishReferencesDocumentationFiles" })
            Assert.Contains($"<{p}>false</{p}>", props, StringComparison.Ordinal);
        // ‏ولا csproj يُطفِئُه لِنَفسِه — فَيَخرُجُ مَشروعٌ مِن الفَحصِ صامِتاً.
        var csprojs = new[] { "libs", "apps", "tests" }
            .SelectMany(d => Directory.EnumerateFiles(Path.Combine(RepoRoot, d), "*.csproj", SearchOption.AllDirectories)).ToList();
        output.WriteLine($"csproj مَفحوص: {csprojs.Count}");
        Assert.True(csprojs.Count >= 30, $"أَداةٌ عَمياء: {csprojs.Count} csproj.");
        var off = csprojs.Where(f => File.ReadAllText(f).Contains("<GenerateDocumentationFile>false", StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.True(off.Count == 0, "مَشروعٌ يُطفِئُ مِلَفَّ التَوثيقِ فَتَخرُجُ crefs مِن الفَحص: " + string.Join("، ", off));
        // ‏وما يَفحَصُه المُتَرجِمُ اليَومَ — يُعَدّ.
        var crefs = new[] { "libs", "apps", "tests" }
            .SelectMany(d => Directory.EnumerateFiles(Path.Combine(RepoRoot, d), "*.*", SearchOption.AllDirectories))
            .Where(f => (f.EndsWith(".cs", StringComparison.Ordinal) || f.EndsWith(".razor", StringComparison.Ordinal))
                        && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Sum(f => Regex.Matches(File.ReadAllText(f), "cref=\"").Count);
        output.WriteLine($"مَراجِعُ cref في libs/ وapps/ وtests/ (‎.cs و‎.razor): {crefs}");
        Assert.True(crefs > 1000, $"أَداةٌ عَمياء: {crefs} مَرجِعاً.");
    }

    /// <summary>مُتَرجِمُ الـSDK الَّذي يَبني المُستَودَع — <c>sdk/*/Roslyn/bincore/csc.dll</c> بِجِوارِ وَقتِ التَشغيلِ الحاليّ، وأَحدَثُها.</summary>
    private static (string Dotnet, string Csc, string RuntimeDir)? Compiler()
    {
        var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var root = Path.GetFullPath(Path.Combine(runtimeDir, "..", "..", ".."));
        var dotnet = Path.Combine(root, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
        var sdk = Path.Combine(root, "sdk");
        if (!File.Exists(dotnet) || !Directory.Exists(sdk)) return null;
        var csc = Directory.EnumerateDirectories(sdk).Select(d => Path.Combine(d, "Roslyn", "bincore", "csc.dll")).Where(File.Exists)
            .OrderByDescending(p => p, StringComparer.Ordinal).FirstOrDefault();
        return csc is null ? null : (dotnet, csc, runtimeDir);
    }

    private (int Code, string Text) Compile(string source, IReadOnlyList<string> errorCodes)
    {
        var c = Compiler();
        Assert.True(c is not null, "أَداةٌ غائِبَة: مُتَرجِمُ الـSDK (sdk/*/Roslyn/bincore/csc.dll) بِجِوارِ وَقتِ التَشغيل.");
        var (dotnet, csc, rt) = c!.Value;
        var dir = Directory.CreateTempSubdirectory("wasayel-cref-");
        try
        {
            var file = Path.Combine(dir.FullName, "Sample.cs");
            File.WriteAllText(file, source, new UTF8Encoding(false));
            var psi = new ProcessStartInfo(dotnet)
            {
                RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
            };
            foreach (var a in new[]
                     {
                         "exec", csc, "-nologo", "-noconfig", "-nostdlib", "-t:library", "-langversion:latest",
                         "-r:" + Path.Combine(rt, "System.Private.CoreLib.dll"), "-r:" + Path.Combine(rt, "System.Runtime.dll"),
                         "-doc:" + Path.Combine(dir.FullName, "Sample.xml"), "-out:" + Path.Combine(dir.FullName, "Sample.dll"),
                         "-warnaserror+:" + string.Join(",", errorCodes), file,
                     })
                psi.ArgumentList.Add(a);
            using var p = Process.Start(psi)!;
            var outText = p.StandardOutput.ReadToEndAsync();
            var err = p.StandardError.ReadToEndAsync();
            Assert.True(p.WaitForExit(60_000), "المُتَرجِمُ لَم يَنتَهِ في دَقيقَة.");
            var text = (outText.Result + err.Result).Trim();
            output.WriteLine($"csc → {p.ExitCode} · {text}");
            return (p.ExitCode, text);
        }
        finally { dir.Delete(recursive: true); }
    }

    /// <summary><b>حَقنُ عَيب</b>: عَيِّنَةٌ فيها مَرجِعٌ إلى عُضوٍ غائِبٍ ومَرجِعٌ بِنَوعِ وَسيطٍ غائِب — المُتَرجِمُ بِرُموزِ الـprops يَخرُجُ بِغَيرِ 0
    /// بِـCS1574 وCS1580 مُسَمَّيَينِ بِمَرجِعَيهِما؛ والعَيِّنَةُ نَفسُها بِمَراجِعَ صَحيحَةٍ (ومِنها حِملٌ بِتَوقيعِه) تُبنى بِـ0. فَالحارِسُ يُفَرِّقُ لا يُوافِق.</summary>
    [Fact]
    public void The_sdk_compiler_with_the_props_codes_goes_red_on_a_broken_cref_and_green_on_a_sound_one()
    {
        var codes = WarningsAsErrors(Props()).Where(c => c.StartsWith("CS", StringComparison.Ordinal)).ToList();
        Assert.Contains("CS1574", codes);
        const string head = "namespace Probe;\n/// <summary>عَيِّنَة.</summary>\npublic static class Sample\n{\n" +
                            "    /// <summary>عُضوٌ قائِم.</summary>\n    public static void Exists() { }\n" +
                            "    /// <summary>حِملٌ بِوَسيط.</summary>\n    public static void Exists(int n) { }\n";
        var (bad, badText) = Compile(head +
            "    /// <summary>يُحيلُ إلى <see cref=\"NoSuchMemberW9\"/> وإلى <see cref=\"Exists(NoSuchTypeW9)\"/>.</summary>\n" +
            "    public static void Refers() { }\n}\n", codes);
        Assert.NotEqual(0, bad);
        Assert.Matches(@"error CS1574: [^\n]*'NoSuchMemberW9'", badText);
        Assert.Matches(@"error CS1580: [^\n]*NoSuchTypeW9", badText);
        var (good, goodText) = Compile(head +
            "    /// <summary>يُحيلُ إلى <see cref=\"Exists()\"/> وإلى <see cref=\"Exists(int)\"/>.</summary>\n" +
            "    public static void Refers() { }\n}\n", codes);
        Assert.True(good == 0, "عَيِّنَةٌ بِمَراجِعَ صَحيحَةٍ حَمِرَت: " + goodText);
    }
}
