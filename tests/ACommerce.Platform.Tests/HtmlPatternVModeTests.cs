using System.Text.RegularExpressions;
using ACommerce.Templates.Customer.Marketplace.Services.Incubator;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ سِمَةُ `pattern` تُجَمَّعُ بِالعَلَمِ v في المُتَصَفِّح — ونَمَطٌ لا يُجَمَّعُ لا يَرُدُّ شَيئاً (‏2026-11-07، مُراجَعَةُ عَدَساتِ C1 الجَولَةُ الثانِيَة · W13-1·R2D-1) ═══
//
// **المَقيسُ قَبل** (تَقريرُ العَدَسَة، Chrome 153؛ وأُعيدَ هُنا بِـnode 22 `new RegExp('^(?:'+p+')$','v')` عَلى كُلِّ سِمَةٍ في المُستَودَع):
// المُتَصَفِّحُ يُجَمِّعُ `pattern` بِالعَلَمِ `v`، وفيه الشَرطَةُ غَيرُ المُهَرَّبَةِ داخِلَ صِنفٍ خَطَأُ صِياغَةٍ إلّا رابِطَةَ مَدى. فَـ`^[a-z0-9][a-z0-9-]*$`
// (استِمارَةُ الإنشاء) و`[a-z0-9][a-z0-9-]*` (استِمارَةُ المُشرِف) «Invalid character class» — والمُتَصَفِّحُ يَطبَعُها في الطَرَفِيَّةِ ويَتَجاهَلُ
// السِمَة: «Bad_Slug» و«-lead» `checkValidity()=true`. فَالرَفضُ المُضَمَّنُ الَّذي قيلَ في C1D-8/SEC-C1-2 كانَ في الخادِمِ وَحدَه، وكُلُّ حَرفٍ
// كَبيرٍ أَو «_» أَو شَرطَةٍ أُولى رِحلَةٌ كامِلَةٌ إلى `?err=slug_format`. ولا اختِبارَ كانَ يَقرَأُ السِمَة.
// **والحارِسُ**: كُلُّ `pattern="…"` في كُلِّ `.razor` يُفحَصُ بِقَواعِدِ الصِنفِ في العَلَمِ v (لا `( ) [ { } / |` غَيرُ مُهَرَّبٍ داخِلَ صِنف، ولا شَرطَةٌ
// غَيرُ مُهَرَّبَةٍ إلّا بَينَ طَرَفَي مَدى)، ويَطبَعُ عَدَدَ ما فَحَص ويَفشَلُ عَلى الصِفر (القاعِدَة ١٠)، ويُقاسُ بِالسِمَتَينِ القَديمَتَينِ نَفسَيهِما.

public class HtmlPatternVModeTests(ITestOutputHelper output)
{
    private static readonly Regex Attr = new(@"\bpattern=""(?<p>[^""@]*)""", RegexOptions.Compiled);

    /// <summary>كُلُّ سِمَةِ <c>pattern</c> حَرفِيَّةٍ في صَفَحاتِ المُستَودَع — المِلَفُّ والسَطرُ والنَمَط.</summary>
    internal static IReadOnlyList<(string File, int Line, string Pattern)> Patterns()
    {
        var root = ThemeZeroEquivalenceTests.RepoRoot;
        var found = new List<(string, int, string)>();
        foreach (var dir in new[] { "libs", "apps" })
        foreach (var file in Directory.EnumerateFiles(Path.Combine(root, dir), "*.razor", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(root, file).Replace('\\', '/');
            if (rel.Contains("/bin/") || rel.Contains("/obj/")) continue;
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
                foreach (Match m in Attr.Matches(lines[i]))
                    found.Add((rel, i + 1, m.Groups["p"].Value));
        }
        return found;
    }

    /// <summary><b>أَوَّلُ خَرقٍ لِقَواعِدِ الصِنفِ في العَلَمِ v</b> أَو <c>null</c>. داخِلَ <c>[…]</c>: <c>( ) [ { } / |</c> لا تَأتي إلّا مُهَرَّبَة، والشَرطَةُ
    /// غَيرُ المُهَرَّبَةِ رابِطَةُ مَدى بَينَ طَرَفَينِ أَو خَطَأ (في أَوَّلِ الصِنفِ أَو آخِرِه أَو بَعدَ مَدى). والهُروبُ <c>\uXXXX</c> طَرَفٌ واحِد.</summary>
    internal static string? VModeBreach(string pattern)
    {
        var inClass = false;
        var tokens = new List<(bool Escaped, string Atom)>();
        for (var i = 0; i < pattern.Length; i++)
        {
            var c = pattern[i];
            if (!inClass)
            {
                if (c == '\\') { i++; continue; }
                if (c == '[') { inClass = true; tokens.Clear(); if (i + 1 < pattern.Length && pattern[i + 1] == '^') i++; }
                continue;
            }
            if (c == '\\')
            {
                if (i + 1 >= pattern.Length) return "هُروبٌ في آخِرِ النَمَط";
                var len = pattern[i + 1] == 'u' && i + 5 < pattern.Length && Regex.IsMatch(pattern.Substring(i + 2, 4), "^[0-9A-Fa-f]{4}$") ? 5 : 1;
                tokens.Add((true, pattern.Substring(i + 1, len)));
                i += len;
                continue;
            }
            if (c == ']')
            {
                for (var j = 0; j < tokens.Count;)
                {
                    bool Dash(int k) => k < tokens.Count && tokens[k] is (false, "-");
                    if (Dash(j)) return $"شَرطَةٌ غَيرُ مُهَرَّبَةٍ لَيسَت رابِطَةَ مَدى في «{pattern}»";
                    j += j + 2 < tokens.Count && Dash(j + 1) && !Dash(j + 2) ? 3 : 1;
                }
                inClass = false;
                continue;
            }
            if ("()[{}/|".Contains(c)) return $"«{c}» غَيرُ مُهَرَّبٍ داخِلَ صِنفٍ في «{pattern}»";
            tokens.Add((false, c.ToString()));
        }
        return inClass ? $"صِنفٌ بِلا «]» في «{pattern}»" : null;
    }

    [Fact]
    public void Every_pattern_attribute_compiles_under_the_v_flag_that_browsers_use()
    {
        var all = Patterns();
        foreach (var (file, line, p) in all) output.WriteLine($"{file}:{line}  {p}  ⇒ {VModeBreach(p) ?? "يُجَمَّع"}");
        output.WriteLine($"السِماتُ المَفحوصَة: {all.Count}");
        Assert.True(all.Count > 0, "أَداةٌ عَمياء: صِفرُ سِمَةِ pattern مَفحوصَة.");
        var broken = all.Where(x => VModeBreach(x.Pattern) is not null).Select(x => $"{x.File}:{x.Line} — {VModeBreach(x.Pattern)}").ToList();
        Assert.True(broken.Count == 0, "سِمَةُ pattern لا يُجَمِّعُها المُتَصَفِّحُ فَيَتَجاهَلُها:\n  " + string.Join("\n  ", broken));
    }

    /// <summary><b>الفاحِصُ يُقاسُ قَبلَ أَن يُوثَقَ بِه</b> (القاعِدَة ١٠): السِمَتانِ اللَتانِ قاسَتهُما العَدَسَةُ حَمراوان، وكُلُّ ما يُجَمِّعُه node بِالعَلَمِ v أَخضَر.</summary>
    [Theory]
    [InlineData("^[a-z0-9][a-z0-9-]*$", false)]      // استِمارَةُ الإنشاءِ قَبلَ الإصلاح — node: Invalid character class
    [InlineData("[a-z0-9][a-z0-9-]*", false)]        // استِمارَةُ المُشرِفِ قَبلَ الإصلاح — كَذلك
    [InlineData("[a-z0-9][a-z0-9\\-]{0,62}", true)]  // بَعدَه — node: يُجَمَّع
    [InlineData("[-a]", false)]
    [InlineData("[a-z-0]", false)]
    [InlineData("[a(b]", false)]
    [InlineData("[a|b]", false)]
    [InlineData("[a{2}]", false)]
    [InlineData("[a/b]", false)]
    [InlineData("[0-9\\u0660-\\u0669\\u06F0-\\u06F9]{10}", true)]
    [InlineData("[3\\u0663\\u06F3][0-9]{14}", true)]
    [InlineData("[A-Za-z]{3}", true)]
    [InlineData("a-b[0-9]{4,6}", true)]
    public void The_v_flag_scanner_goes_red_on_the_measured_breaches(string pattern, bool compiles)
        => Assert.Equal(compiles, VModeBreach(pattern) is null);

    /// <summary>واستِمارَتا المَتجَرِ تَسأَلانِ شَكلَ الخادِمِ نَفسَه (<see cref="TenantFromAnalysisFactory.SlugRefusal"/>): النَمَطُ كَما يُجَمِّعُه المُتَصَفِّحُ
    /// (<c>^(?:p)$</c>) يَقبَلُ ما يَقبَلُه الشَكلُ ويَرُدُّ ما يَرُدُّه — والمَحجوزُ والتَفَرُّدُ في الخادِمِ وَحدَه.</summary>
    [Theory]
    [InlineData("libs/templates/ACommerce.Templates.Customer.Marketplace/Components/Pages/StudioNewStore.razor")]
    [InlineData("libs/templates/ACommerce.Templates.Customer.Marketplace/Components/Pages/Admin/CreateTenant.razor")]
    public void The_store_forms_slug_pattern_is_the_server_slug_shape(string file)
    {
        var p = Patterns().Single(x => x.File == file && x.Pattern.Contains("a-z0-9", StringComparison.Ordinal)).Pattern;
        var browser = new Regex("^(?:" + p + ")$");
        string[] samples = ["a", "shop-1", "0abc", new string('a', 63), new string('a', 64), "Bad_Slug", "-lead", "a_b", "UPPER", "", "a b"];
        foreach (var s in samples)
        {
            var server = TenantFromAnalysisFactory.SlugRefusal(s) is not (TenantFromAnalysisFactory.SlugFormat or TenantFromAnalysisFactory.SlugRequired);
            output.WriteLine($"{file.Split('/')[^1]} «{(s.Length > 12 ? s[..12] + "…" + s.Length : s)}»: المُتَصَفِّح {browser.IsMatch(s)} · الخادِم {server}");
            Assert.Equal(server, browser.IsMatch(s));
        }
        Assert.Null(VModeBreach(p));
    }
}
