using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ تَعليقُ التَوثيقِ يَقَعُ عَلى عُضوِه — حارِسٌ يَحمَرُّ لا وَصفٌ يُكَرَّر (‏2026-11-11، الإغلاقُ الأَخيرُ لِـW13-4 · W13-4·C2f·8) ═══
//
// **الكُلفَةُ المَقيسَة (القاعِدَة ٢)**: أَصلَحَ `b92a388` (G8) وُثُقَ `StoreHostEntries`/`TenantEntries` في `Seo.cs` يَدَوِيّاً، ثُمَّ عادَ الصِنفُ نَفسُه
// أَربَعَ مَرّاتٍ في التَيّارِ نَفسِه (SEC-C2z-R1-6، C2z-PATHS-R1-06، C2z-PATHS-R2-06، C2z-PATHS-R3-04): عُضوٌ جَديدٌ يُدرَجُ بَينَ `<summary>` جارِه وجارِه،
// فَيَحمِلُ الجَديدُ مُلَخَّصَين والجارُ لا شَيء — ولا يُنَبِّهُ المُتَرجِم. وعِندَ الرَأسِ قَبلَ هذا الحارِسِ تِسعٌ (سَبعُ مُلَخَّصاتٍ مُكَدَّسَة — مِنها ثَلاثٌ أَقدَمُ
// مِن التَيّار — وكُتلَةُ `//` فُصِلَت عَن عُضوِها، ووُثُقٌ فَصَلَتها كُتلَةُ `//` عَن عُضوِها). **والقاعِدَة** عَلى كُلِّ مِلَفِّ C# تَحتَ `libs/` و`apps/`:
// (١) مَجموعَةُ أَسطُرِ `///` المُتَجاوِرَةِ (والأَسطُرُ الفارِغَةُ بَينَها لا تَفصِلُها — المُتَرجِمُ يُلحِقُها بِالعُضوِ نَفسِه) فيها `<summary>` واحِدٌ عَلى الأَكثَر؛
// (٢) ولا يَلي المَجموعَةَ سَطرُ `//` — فَتَنفَصِلَ عَن عُضوِها؛ (٣) ولا تَلي كُتلَةُ `//` (لا تَبدَأُ بِخَطِّ قِسمٍ «──» أَو «══») مَجموعَةَ `///` مُباشَرَةً —
// فَتَكونَ تَعليقَ عُضوٍ آخَرَ فَصَلَته عَنه وُثُقُ جارِه. ويَطبَعُ ما فَحَصَه ويَفشَلُ عَلى الصِفر (القاعِدَة ١٠).

public class DocCommentPlacementTests(ITestOutputHelper output)
{
    private static readonly string[] SectionRule = ["──", "══", "━━"];

    public sealed record Misplacement(string Kind, string File, int Line);

    /// <summary>يَفحَصُ نَصَّ مِلَفٍّ واحِدٍ ويُعيدُ عَدَدَ مَجموعاتِ التَوثيقِ (أَعضاءٌ مُوَثَّقَة) وما خَرَقَ القاعِدَة.</summary>
    public static (int Groups, IReadOnlyList<Misplacement> Found) Scan(string file, string text)
    {
        var t = text.Replace("\r", "", StringComparison.Ordinal).Split('\n').Select(l => l.Trim()).ToArray();
        var found = new List<Misplacement>();
        var groups = 0;
        static bool Doc(string l) => l.StartsWith("///", StringComparison.Ordinal);
        static bool Slash(string l) => l.StartsWith("//", StringComparison.Ordinal) && !Doc(l);
        var i = 0;
        while (i < t.Length)
        {
            if (Doc(t[i]))
            {
                var last = i;
                var j = i;
                while (j < t.Length && (Doc(t[j]) || t[j].Length == 0))
                {
                    if (Doc(t[j])) last = j;
                    j++;
                }
                groups++;
                var summaries = 0;
                for (var k = i; k <= last; k++) summaries += CountOf(t[k], "<summary>");
                if (summaries > 1) found.Add(new("مُلَخَّصانِ عَلى عُضوٍ واحِد", file, i + 1));
                var next = last + 1;
                while (next < t.Length && t[next].Length == 0) next++;
                if (next < t.Length && Slash(t[next])) found.Add(new("وُثُقٌ تَفصِلُها كُتلَةُ // عَن عُضوِها", file, next + 1));
                i = last + 1;
            }
            else if (Slash(t[i]))
            {
                var j = i;
                while (j < t.Length && Slash(t[j])) j++;
                if (j < t.Length && Doc(t[j]) && !SectionRule.Any(r => t[i].Contains(r, StringComparison.Ordinal)))
                    found.Add(new("كُتلَةُ // تَفصِلُها عَن عُضوِها وُثُقُ عُضوٍ آخَر", file, i + 1));
                i = j;
            }
            else i++;
        }
        return (groups, found);
    }

    private static int CountOf(string line, string what)
    {
        var n = 0;
        for (var at = line.IndexOf(what, StringComparison.Ordinal); at >= 0; at = line.IndexOf(what, at + what.Length, StringComparison.Ordinal)) n++;
        return n;
    }

    private static IEnumerable<string> Sources()
        => new[] { "libs", "apps" }
            .Select(d => Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, d))
            .SelectMany(d => Directory.EnumerateFiles(d, "*.cs", SearchOption.AllDirectories))
            .Where(f => !f.Split(Path.DirectorySeparatorChar).Any(s => s is "bin" or "obj"));

    [Fact]
    public void Every_xml_doc_in_libs_and_apps_sits_on_its_own_member()
    {
        var files = 0;
        var groups = 0;
        var found = new List<Misplacement>();
        foreach (var f in Sources())
        {
            files++;
            var (g, m) = Scan(Path.GetRelativePath(ThemeZeroEquivalenceTests.RepoRoot, f), File.ReadAllText(f));
            groups += g;
            found.AddRange(m);
        }
        output.WriteLine($"مِلَفّاتُ C# مَقروءَة: {files} · أَعضاءٌ مُوَثَّقَةٌ مَفحوصَة: {groups} · في غَيرِ مَوضِعِها: {found.Count}");
        foreach (var m in found) output.WriteLine($"  ✗ {m.Kind} — {m.File}:{m.Line}");
        Assert.True(files > 0 && groups > 0, "صِفرُ مِلَفٍّ أَو صِفرُ عُضوٍ مُوَثَّق — الماسِحُ أَعمى (القاعِدَة ١٠)");
        Assert.Empty(found);
    }

    [Theory]
    [InlineData("    /// <summary>أ</summary>\n    /// <summary>ب</summary>\n    public const int X = 1;\n", "مُلَخَّصانِ عَلى عُضوٍ واحِد")]
    [InlineData("    /// <summary>أ</summary>\n\n    /// <summary>ب</summary>\n    public int Y() => 1;\n", "مُلَخَّصانِ عَلى عُضوٍ واحِد")]
    [InlineData("    /// <summary>أ</summary>\n    // تَعليقٌ عَلى ما يَلي\n    public int Z() => 1;\n", "وُثُقٌ تَفصِلُها كُتلَةُ // عَن عُضوِها")]
    [InlineData("    // قَرارُ ما بَعدَ الدُخول\n    /// <summary>أ</summary>\n    private static int A() => 1;\n\n    private static int B() => 2;\n", "كُتلَةُ // تَفصِلُها عَن عُضوِها وُثُقُ عُضوٍ آخَر")]
    public void The_scanner_sees_each_misplacement(string source, string kind)
    {
        var (_, found) = Scan("x.cs", source);
        Assert.Equal(kind, Assert.Single(found).Kind);
    }

    [Theory]
    [InlineData("    /// <summary>أ</summary>\n    /// <remarks>ب</remarks>\n    public int A() => 1;\n")]
    [InlineData("    // ─── قِسم ───\n    /// <summary>أ</summary>\n    public int A() => 1;\n")]
    [InlineData("    /// <summary>أ</summary>\n    [Fact]\n    public void A() { }\n")]
    [InlineData("    // تَعليق\n\n    /// <summary>أ</summary>\n    public int A() => 1;\n")]
    public void And_passes_what_is_in_place(string source)
    {
        var (groups, found) = Scan("x.cs", source);
        Assert.Equal(1, groups);
        Assert.Empty(found);
    }
}
