using System.Text.RegularExpressions;
using Xunit;

namespace ACommerce.Platform.Tests;

// ─── الوَضع الداكِن — الآلِيَّة قائِمَة، والمَدخَل مَعدوم ───────────────
//
// هذا المِلَفّ يُثَبِّت **قِياساً**، لا نِيَّةً. والقِياس مُزعِج:
// أَوراق الأَنماط فيها كُتلَتا `body.ac-dark` تُعيدان تَعريف أَحَدَ عَشَرَ
// رَمزاً، **ولا سَطر واحِد في المُستَودَع كُلِّه يَضَع ذلك الصَنف على
// `<body>`**. لا razor، ولا C#‎، ولا JavaScript، ولا `prefers-color-scheme`.
// و`AcThemeToggle` — الزِرّ المَبنيّ لِهذا الغَرَض — **صِفر مُستَهلِك**.
//
// أَي أَنّ الوَضع الداكِن **مِثال حَيّ على القاعِدَة ١** (لا تَجريد قَبلَ
// مُستَهلِكِه): بُنِيَت الطَبَقَة، ولَم يُبنَ ما يُشَغِّلُها. والقاعِدَة ١٢
// تَقول الباقي: ميزَةٌ لا تُبلَغ بِالنَقر غَير مَوجودَة.
//
// **ولِماذا يُثَبَّت الغِياب بِاختِبار بَدَل أَن يُكتَب في وَثيقَة**:
// لِأَنّ الغِياب يَنقَلِب. يَوم يُضاف الزِرّ (أَو الكوكي، أَو استِعلام
// الوَسَط) يَسقُط هذا المِلَفّ **بِصَوت عالٍ** ويَقول لِمَن أَضافَه:
// «انظُر سِجِلّ الأَسطُح الحَرفِيَّة قَبلَ أَن تَفتَح الباب». وَثيقَةٌ
// تَقول الشَيء نَفسَه لا تُقرَأ في تِلكَ اللَحظَة.

public class DarkModeReachabilityTests
{
    private static string Root => ThemeZeroEquivalenceTests.RepoRoot;

    private static IEnumerable<string> SourceFiles(params string[] extensions)
    {
        foreach (var dir in new[] { "libs", "apps" })
        foreach (var path in Directory.EnumerateFiles(
                     Path.Combine(Root, dir), "*.*", SearchOption.AllDirectories))
        {
            if (path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                    StringComparison.Ordinal) ||
                path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                    StringComparison.Ordinal))
                continue;
            if (extensions.Any(e => path.EndsWith(e, StringComparison.OrdinalIgnoreCase)))
                yield return path;
        }
    }

    [Fact]
    public void TheDarkModeStylesheetExists_SoThereIsSomethingToReach()
    {
        // حارِس العَمى (القاعِدَة ١٠): اختِبارٌ يُثبِت «لا مَدخَل» بَينَما
        // لا هَدَفَ أَصلاً لا يُميَّز عَن اختِبارٍ يَقرَأ الشَجَرَة الخَطَأ.
        // فَيُعَدّ الهَدَف أَوَّلاً.
        var css = Path.Combine(Root, "libs", "templates",
            "ACommerce.Templates.Customer.Marketplace", "wwwroot", "css");

        var blocks = 0;
        foreach (var sheet in new[] { "app.css", "widgets.css" })
            blocks += Regex.Matches(File.ReadAllText(Path.Combine(css, sheet)),
                @"body\.ac-dark\s*\{").Count;

        Assert.Equal(2, blocks);

        // وأَهَمُّ ما تُعيد الكُتلَتان تَعريفَه لِهذِه المَوجَة: السَطح.
        Assert.Matches(@"body\.ac-dark\s*\{[^}]*--ac-surface:\s*#0F172A;",
            File.ReadAllText(Path.Combine(css, "app.css")));
    }

    [Fact]
    public void NothingInTheRepositoryEverPutsTheDarkClassOnTheBody()
    {
        // ثَلاثَة مَسارات مُمكِنَة، وثَلاثَتُها صِفر:
        //   ١. صَنف يُكتَب في razor/C#/JS
        //   ٢. استِعلام وَسَط prefers-color-scheme
        //   ٣. مُستَهلِك لِـAcThemeToggle
        var offenders = new List<string>();

        foreach (var f in SourceFiles(".razor", ".cs", ".js", ".html"))
        {
            var text = File.ReadAllText(f);
            // ‏«ac-dark» في مَصدَر غَير CSS = مَن يَكتُب الصَنف.
            if (text.Contains("ac-dark", StringComparison.Ordinal))
                offenders.Add($"صَنف داكِن مَكتوب: {Path.GetRelativePath(Root, f)}");
            if (text.Contains("<AcThemeToggle", StringComparison.Ordinal))
                offenders.Add($"مُستَهلِك لِلزِرّ: {Path.GetRelativePath(Root, f)}");
        }

        foreach (var f in SourceFiles(".css"))
            if (File.ReadAllText(f).Contains("prefers-color-scheme", StringComparison.Ordinal))
                offenders.Add($"استِعلام وَسَط: {Path.GetRelativePath(Root, f)}");

        Assert.True(offenders.Count == 0,
            "الوَضع الداكِن صارَ قابِلاً لِلبُلوغ — وهذا **خَبَر سارّ يُسقِط هذا " +
            "الاختِبار عَمداً**. قَبلَ حَذف المِلَفّ: راجِع الأَسطُح الحَرفِيَّة " +
            "الباقِيَة في docs/THEME-DEBT-DECISIONS.md §C-1، فَقَد بَقِيَ مِنها ما " +
            "لَم يُصلَح لِأَنَّه كانَ نَفعاً كامِناً.\n" +
            string.Join("\n", offenders));
    }

    [Fact]
    public void TheUndefinedCardBackgroundVariableIsGone()
    {
        // ‏`var(--ac-card-bg, #fff)` كانَ **أَخبَثَ** الأَشكال الثَلاثَة:
        // يَقرَؤُه المَرء فَيَظُنُّه مَربوطاً بِالثيم، و`--ac-card-bg` غَير
        // مُعَرَّف في المُستَودَع كُلِّه — فَالبديل هو القيمَة الفِعليَّة
        // دائِماً، وهو أَبيَض حَرفيّ لا يَستَجيب لِـbody.ac-dark.
        // ثَلاثَة مَواضِع، كُلُّها صارَت var(--ac-surface).
        var offenders = SourceFiles(".css", ".razor")
            .Where(f => File.ReadAllText(f).Contains("--ac-card-bg", StringComparison.Ordinal))
            .Select(f => Path.GetRelativePath(Root, f))
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public void TheSevenSurfaceSitesReadTheTokenNotALiteral()
    {
        // المَواضِع السَبعَة الَّتي كانَت **فِعلاً** بَيضاءَ حَرفِيَّةً —
        // مَقيسَةً لا مَظنونَةً، ومُميَّزَةً عَن الواحِدِ والعِشرينَ
        // مَوضِعاً الَّتي كانَت أَصلاً `var(--ac-surface, #ffffff)` أَي
        // مَربوطَةً بِالرَمز وبَديلُها مَيِّت.
        var css = Path.Combine(Root, "libs", "templates",
            "ACommerce.Templates.Customer.Marketplace", "wwwroot", "css");

        var app      = File.ReadAllText(Path.Combine(css, "app.css"));
        var widgets  = File.ReadAllText(Path.Combine(css, "widgets.css"));
        var premium  = File.ReadAllText(Path.Combine(css, "premium.css"));

        Assert.Contains("border: 2px solid var(--ac-surface);", app, StringComparison.Ordinal);
        Assert.Contains("background: var(--ac-surface);", app, StringComparison.Ordinal);
        Assert.Contains(".ac-hero .ac-search {\n    background: var(--ac-surface);",
            widgets.Replace("\r\n", "\n"), StringComparison.Ordinal);
        Assert.Contains("color-mix(in srgb, var(--ac-surface) 82%, transparent)",
            premium, StringComparison.Ordinal);
        Assert.Contains("color-mix(in srgb, var(--ac-surface) 85%, transparent)",
            premium, StringComparison.Ordinal);
        Assert.Contains(".ac-gallery-arrow:hover { transform: translateY(-50%) scale(1.08); " +
            "background: var(--ac-surface); }", premium, StringComparison.Ordinal);

        // ولا واحِدَة مِن الصيغ الحَرفِيَّة الَّتي أُزيلَت باقِيَة.
        Assert.DoesNotContain("background: #ffffff;", widgets, StringComparison.Ordinal);
        Assert.DoesNotContain("color-mix(in srgb, #fff 8", premium, StringComparison.Ordinal);
    }
}
