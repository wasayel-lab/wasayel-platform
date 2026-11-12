using System.Reflection;
using System.Text.RegularExpressions;
using ACommerce.Platform.Shared.Metering;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ جُزءٌ مِن G15 — لِكُلِّ قيمَةٍ حارِسَةٍ مَوضِعٌ يَضَعُها (القاعِدَة ١)، ولا تُصادِفُ اسمَ مَتجَرٍ ولا اسمَ دَور ═══
// (‏2026-11-12، الموجَة ١٣ W13-6·٣ — ADR-051 §١١؛ التَصميم §2). القيمَةُ بِلا واضِعٍ تَنمو بِالخَيال؛ فَيُعَدُّ كُلُّ مَرجِعٍ إلَيها في كودِ
// `libs/` و`apps/` (بِلا التَعليقاتِ وبِلا مِلَفِّ تَعريفِها) ويَحمَرُّ ما كانَ صِفراً.
//
// **والمَرجِعُ واضِعٌ بِشَكلِه لا بِاسمِه** (‏2026-11-12، مُراجَعَةُ المَرحَلَةِ أ · R-A-1): كانَ يُعَدُّ كُلُّ مَرجِع، فَلَمّا قَرَأَت صَفحَةُ العَدِّ (`d414817`)
// `r.Role == MeterSentinels.Seed` صارَ القارِئُ «واضِعاً» — مَقيس: حَذفُ واضِعِ `_seed` الوَحيد (`MeterScope.cs:23`) طَبَعَ «Seed 1» والصَفُّ أَخضَر. فَالآنَ
// لا يُعَدُّ مَرجِعٌ يُقارَن (`==`/`!=` قَبلَه أَو بَعدَه)، ولا نَمَطٌ (`is`/`not`/`or`/`and`/`case`، أَو `=>` بَعدَه ذِراعَ `switch`)، ولا وُصولٌ إلى عُضوٍ
// (`.` بَعدَه)، ولا وَسيطٌ لِنِداءِ مُقارَنَة (`Equals`/`Contains`/`StartsWith`/`EndsWith`/`IndexOf`/`Compare`/`IsSentinel`)، ولا ما في مُجَلَّدَي القُرّاء
// (`Services/Queries` و`Components/Pages`). وما بَقِيَ — إسنادٌ، وذِراعُ شَرطٍ ثُلاثِيّ، ووَسيطُ نِداءٍ يَكتُب، و`return`/`=>` تُعيدُه — واضِع.

public class MeterSentinelSetterTests(ITestOutputHelper output)
{
    private static IReadOnlyList<(string Name, string Value)> Declared()
        => typeof(MeterSentinels).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (f.Name, (string)f.GetRawConstantValue()!)).ToList();

    /// <summary>مُجَلَّدا القُرّاء: صَفَحاتٌ واستِعلاماتٌ تَقرَأُ الدَفتَرَ ولا تَكتُبُ سَطراً — فَلا يُعَدُّ فيهِما واضِع.</summary>
    internal static readonly IReadOnlyList<string> ReaderDirectories = ["/Services/Queries/", "/Components/Pages/"];

    /// <summary>نِداءاتٌ تُقارِنُ وَسيطَها ولا تُخَزِّنُه.</summary>
    internal static readonly IReadOnlySet<string> ComparingCalls =
        new HashSet<string>(["Equals", "Contains", "StartsWith", "EndsWith", "IndexOf", "Compare", "CompareOrdinal", "IsSentinel"], StringComparer.Ordinal);

    private static readonly Regex ReaderBefore = new(@"(?:==|!=|\b(?:is|not|or|and|case))\s*$", RegexOptions.CultureInvariant);
    private static readonly Regex ReaderAfter = new(@"^\s*(?:==|!=|=>|\.)", RegexOptions.CultureInvariant);

    internal static bool IsReaderPath(string path)
    {
        var p = path.Replace('\\', '/');
        return ReaderDirectories.Any(d => p.Contains(d, StringComparison.Ordinal));
    }

    /// <summary>أَقرَبُ نِداءٍ يَحوي المَوضِع (بِعُمقِ الأَقواس)، أَو <c>null</c> إن بَلَغَ حَدَّ جُملَةٍ قَبلَه.</summary>
    internal static string? EnclosingCall(string code, int at)
    {
        var depth = 0;
        for (var i = at - 1; i >= 0; i--)
        {
            var c = code[i];
            if (c is ')' or ']') depth++;
            else if (c is '(' or '[')
            {
                if (depth-- > 0) continue;
                if (c == '[') return null;
                var j = i - 1;
                while (j >= 0 && char.IsWhiteSpace(code[j])) j--;
                var k = j;
                while (k >= 0 && (char.IsLetterOrDigit(code[k]) || code[k] == '_')) k--;
                return code[(k + 1)..(j + 1)];
            }
            else if (depth == 0 && c is ';' or '{' or '}') return null;
        }
        return null;
    }

    /// <summary>مَرجِعٌ يَقرَأُ القيمَةَ ولا يَضَعُها: مُقارَنَةٌ، أَو نَمَط، أَو وُصولٌ إلى عُضو، أَو وَسيطُ نِداءِ مُقارَنَة.</summary>
    internal static bool IsReaderUse(string code, int start, int end)
        => ReaderBefore.IsMatch(code[..start]) || ReaderAfter.IsMatch(code[end..])
           || (EnclosingCall(code, start) is { } call && ComparingCalls.Contains(call));

    /// <summary>مَواضِعُ كُلِّ اسمٍ في الكود — <c>MeterSentinels.{اسم}</c> خارِجَ مِلَفِّ التَعريف، بِلا تَعليقات: <b>الواضِعُ بِشَكلِه</b> (<see cref="Sites"/>).</summary>
    internal static IReadOnlyDictionary<string, int> SetterSites(IEnumerable<string> names, IEnumerable<(string Path, string Text)> files)
        => Sites(names, files).ToDictionary(s => s.Key, s => s.Value.Setters, StringComparer.Ordinal);

    /// <summary>لِكُلِّ اسم: كَم مَرجِعاً في الكودِ كُلِّه، وكَم مِنها واضِع.</summary>
    internal static IReadOnlyDictionary<string, (int References, int Setters)> Sites(IEnumerable<string> names, IEnumerable<(string Path, string Text)> files)
    {
        var counts = names.ToDictionary(n => n, _ => (References: 0, Setters: 0), StringComparer.Ordinal);
        foreach (var (path, text) in files)
        {
            var code = MeterListenerShapeTests.CodeOf(text);
            var readerFile = IsReaderPath(path);
            foreach (var n in counts.Keys.ToList())
                foreach (Match m in Regex.Matches(code, $@"\bMeterSentinels\.{n}\b"))
                {
                    var (refs, setters) = counts[n];
                    counts[n] = (refs + 1, setters + (readerFile || IsReaderUse(code, m.Index, m.Index + m.Length) ? 0 : 1));
                }
        }
        return counts;
    }

    private static IEnumerable<(string Path, string Text)> CodeFiles()
        => new[] { "libs", "apps" }
            .SelectMany(d => Directory.EnumerateFiles(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, d), "*.*", SearchOption.AllDirectories))
            .Where(f => (f.EndsWith(".cs", StringComparison.Ordinal) || f.EndsWith(".razor", StringComparison.Ordinal))
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && Path.GetFileName(f) != "MeterSentinels.cs")
            .Select(f => (f, File.ReadAllText(f)));

    [Fact]
    public void Every_sentinel_has_at_least_one_setter_site_in_libs_or_apps()
    {
        var declared = Declared();
        var files = CodeFiles().ToList();
        var all = Sites(declared.Select(d => d.Name), files);
        var sites = all.ToDictionary(s => s.Key, s => s.Value.Setters, StringComparer.Ordinal);
        output.WriteLine($"[G15] مِلَفّاتٌ مَقروءَة {files.Count} · قِيَمٌ حارِسَة {declared.Count}: "
            + string.Join(" · ", sites.Select(s => $"{s.Key} {s.Value}")));
        output.WriteLine("[G15] مَراجِعُ ← واضِعون: " + string.Join(" · ", all.Select(s => $"{s.Key} {s.Value.References} ← {s.Value.Setters}")));
        Assert.True(files.Count > 400 && declared.Count > 0, $"أَداةٌ عَمياء: {files.Count} مِلَفّاً و{declared.Count} قيمَة.");
        var orphans = sites.Where(s => s.Value == 0).Select(s => s.Key).ToList();
        Assert.True(orphans.Count == 0, "قيمَةٌ حارِسَةٌ بِلا مَوضِعٍ يَضَعُها — تَدخُلُ المُعجَمَ مَعَ واضِعِها: " + string.Join(", ", orphans));
    }

    /// <summary><b>حَقنُ العَيب</b>: قيمَةٌ تُعلَنُ ولا يَضَعُها شَيء — العَدّادُ يَراها صِفراً.</summary>
    [Fact]
    public void An_injected_sentinel_without_a_setter_counts_zero()
    {
        var sites = SetterSites(["None", "Injected"], [("a.cs", "x = MeterSentinels.None; // MeterSentinels.Injected\ny = MeterSentinels.NoneOther;")]);
        Assert.Equal(1, sites["None"]);
        Assert.Equal(0, sites["Injected"]);
    }

    /// <summary><b>حَقنُ العَيبِ الَّذي أَعماه</b> (R-A-1): قارِئٌ وَحدَه — النَصُّ بِحَرفِ المُلاحَظَة — لا يُعَدُّ واضِعاً.</summary>
    [Fact]
    public void A_reader_only_reference_counts_zero_setter_sites()
    {
        var sites = Sites(["Seed"], [("Writers/Any.cs", "x = r.Role == MeterSentinels.Seed;")]);
        Assert.Equal((1, 0), sites["Seed"]);
    }

    /// <summary>كُلُّ شَكلِ قِراءَةٍ صِفر، وكُلُّ شَكلِ وَضعٍ واحِد — في مِلَفٍّ خارِجَ مُجَلَّدَي القُرّاء.</summary>
    [Theory]
    [InlineData("ok = x != MeterSentinels.Seed;", 0)]
    [InlineData("ok = MeterSentinels.Seed == r.Role;", 0)]
    [InlineData("ok = MeterSentinels.Seed != r.Role;", 0)]
    [InlineData("ok = r.Role is MeterSentinels.Seed;", 0)]
    [InlineData("ok = r.Role is not MeterSentinels.Seed;", 0)]
    [InlineData("ok = r.Role is MeterSentinels.None or MeterSentinels.Seed;", 0)]
    [InlineData("switch (r.Role) { case MeterSentinels.Seed: break; }", 0)]
    [InlineData("var t = r.Role switch { MeterSentinels.Seed => 1, _ => 0 };", 0)]
    [InlineData("ok = string.Equals(r.Role, MeterSentinels.Seed, StringComparison.Ordinal);", 0)]
    [InlineData("ok = string.Equals(Trim(r.Role), MeterSentinels.Seed);", 0)]
    [InlineData("ok = MeterSentinels.Seed.Equals(r.Role);", 0)]
    [InlineData("ok = roles.Contains(MeterSentinels.Seed);", 0)]
    [InlineData("n = rows.Where(r => r.Role == MeterSentinels.Seed).Sum(r => r.Entries);", 0)]
    [InlineData("Role = MeterSentinels.Seed;", 1)]
    [InlineData("var role = seed ? MeterSentinels.Seed : null;", 1)]
    [InlineData("var role = known ? r.Role : MeterSentinels.Seed;", 1)]
    [InlineData("holder.Fill(app, MeterSentinels.Seed, null);", 1)]
    [InlineData("holder.Fill(app, entrepreneur: MeterSentinels.Seed);", 1)]
    [InlineData("return MeterSentinels.Seed;", 1)]
    [InlineData("string Role() => MeterSentinels.Seed;", 1)]
    [InlineData("var e = new UsageEntry(id, unit, 1, app, MeterSentinels.Seed, role);", 1)]
    [InlineData("var e = Entry(Map(app), MeterSentinels.Seed);", 1)]
    public void Each_reader_shape_counts_zero_and_each_setter_shape_counts_one(string code, int setters)
    {
        var sites = Sites(["Seed"], [("libs/x/Services/Metering/Any.cs", code)]);
        Assert.Equal((1, setters), sites["Seed"]);
    }

    /// <summary>مُجَلَّدا القُرّاء: الشَكلُ نَفسُه لا يُعَدُّ واضِعاً فيهِما، ويُعَدُّ خارِجَهُما.</summary>
    [Theory]
    [InlineData("libs/templates/T/Services/Queries/MeterQueries.cs", 0)]
    [InlineData("libs\\templates\\T\\Components\\Pages\\AdminMeter.razor", 0)]
    [InlineData("libs/templates/T/Services/Metering/MeterRules.cs", 1)]
    public void A_setter_shape_in_a_reader_directory_is_not_counted(string path, int setters)
    {
        var sites = Sites(["Seed"], [(path, "var role = seed ? MeterSentinels.Seed : null;")]);
        Assert.Equal((1, setters), sites["Seed"]);
    }

    [Fact]
    public void The_sentinels_are_a_closed_lexicon_that_no_store_or_role_slug_can_collide_with()
    {
        var declared = Declared();
        Assert.Equal(declared.Select(d => d.Value).Order(StringComparer.Ordinal), MeterSentinels.All.Order(StringComparer.Ordinal));
        Assert.Equal(declared.Count, MeterSentinels.All.Distinct(StringComparer.Ordinal).Count());
        var storeSlug = new Regex("^[a-z0-9-]+$");
        var roleSlug = new Regex("^[a-z][a-z0-9_]*$");
        Assert.All(MeterSentinels.All, s =>
        {
            Assert.StartsWith("_", s, StringComparison.Ordinal);
            Assert.DoesNotMatch(storeSlug, s);
            Assert.DoesNotMatch(roleSlug, s);
        });
    }
}
