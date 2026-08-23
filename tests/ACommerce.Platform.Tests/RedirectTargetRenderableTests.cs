using System.Text.RegularExpressions;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

/// <summary>
/// <para><b>كُلُّ وِجهَةِ تَحويلٍ يَجِبُ أَن تُصَيَّرَ بِـ<c>GET</c>.</b>
/// ‏<c>Results.Redirect</c> يُنتِج <c>302</c>، والمُتَصَفِّحُ يَتبَعُه
/// بِـ<c>GET</c> دائِماً. فَتَحويلٌ إلى مَسارٍ مُسَجَّلٍ
/// <c>POST</c>-فَقَط يَنتَهي بِـ<b>‏405 Method Not Allowed</b> في
/// وَجهِ المُستَخدِم — لا بِخَطَإٍ في السِجِلّ.</para>
///
/// <para><b>الكِلفَةُ الَّتي كَتَبَت هذا الفَحص (‏2026-08-23)</b>:
/// ‏<c>POST /studio/auth/verify</c> كانَ يُحَوِّل إلى
/// <c>/studio/consent?returnUrl=/studio/auth/verify</c>، و
/// <c>/studio/consent/accept</c> يُحَوِّل إلى ذلك الـ<c>returnUrl</c> —
/// أَي إلى نُقطَةٍ <c>POST</c> فَقَط. فَكانَ **أَوَّلُ دُخولٍ
/// لِمُستَخدِمٍ جَديدٍ جاءَ بِفِكرَةٍ مِن صَفحَةِ الهُبوط** يَنتَهي
/// بِـ405 بَعدَ قَبولِ الشُروطِ مُباشَرَةً. لَم يُمسِكهُ شَيء لِأَنّ
/// المَسارَ نَصٌّ داخِلَ سِلسِلَة، ولا مُصَرِّفَ يَقرَؤُه.</para>
///
/// <para><b>ويَفحَص التَوقيعَ لا النِيَّة</b> (القاعِدَة ٢): يَجمَع
/// المَساراتِ المُصَيَّرَةَ بِـ<c>GET</c> مِن مَصدَرَين — تَوجيهات
/// <c>@@page</c> في ملَفّات Razor، ونِداءات <c>MapGet</c> — ثُمَّ
/// يُقابِلُ بِها كُلَّ وِجهَةِ تَحويلٍ <b>حَرفِيَّة</b> في المُستَودَع.</para>
///
/// <para><b>وحُدودُه مُعلَنَة</b>: لا يَرى وِجهَةً تُبنى مِن
/// مُتَغَيِّرٍ بِالكامِل (لا جُزءَ حَرفيَّ فيها)، ولا وِجهَةً تُكتَب في
/// JavaScript. وهذا مَقبول: العَطَبُ المَقيسُ كانَ حَرفِيّاً،
/// و<b>عَدَّادُ ما فُحِص يُطبَع ويُشتَرَط أَلّا يَكونَ صِفراً</b>
/// (القاعِدَة ١٠) — فَإن انتَقَلَت الشَجَرَةُ يَحمَرّ الفَحصُ بَدَلَ
/// أَن يُبارِكَ الفَراغ.</para>
/// </summary>
public class RedirectTargetRenderableTests(ITestOutputHelper output)
{
    /// <summary>وِجهاتٌ حَرفِيَّةٌ مَقصودَةٌ خارِجَ التَصيير — ولا
    /// واحِدَةَ فيها اليَوم. السِجِلُّ مَوجودٌ لِيُقالَ الاستِثناءُ
    /// بِاسمِه لا لِيُوَسَّع.</summary>
    private static readonly string[] Pinned = Array.Empty<string>();

    [Fact]
    public void EveryLiteralRedirectTarget_IsRenderableByGet()
    {
        var root = ThemeZeroEquivalenceTests.RepoRoot;
        var libs = Path.Combine(root, "libs");
        var apps = Path.Combine(root, "apps");

        var gettable = CollectGetRoutes(libs).Concat(CollectGetRoutes(apps)).ToList();
        Assert.True(gettable.Count > 0,
            "صِفرُ مَسارٍ يُصَيَّر بِـGET — الفاحِصُ أَعمى، لا المُستَودَعُ فارِغ.");

        var offenders = new List<string>();
        var inspected = 0;

        foreach (var file in SourceFiles(libs).Concat(SourceFiles(apps)))
        {
            var text = StripComments(File.ReadAllText(file));
            foreach (var target in RedirectTargets(text))
            {
                inspected++;
                if (Pinned.Contains(target, StringComparer.Ordinal)) continue;
                if (Matches(target, gettable)) continue;
                offenders.Add($"{Rel(root, file)} → {target}");
            }
        }

        // عَدّادُ ما فُحِص يُطبَع في النَجاحِ أَيضاً لا في الفَشَلِ وَحدَه
        // (القاعِدَة ١٠): «صِفرُ مُخالَفَة» بِلا عَدّادٍ لا يُمَيَّز عَن
        // أَداةٍ عَمياء.
        output.WriteLine(
            $"فُحِصَ {inspected} وِجهَةَ تَحويلٍ حَرفِيَّة مُقابِلَ " +
            $"{gettable.Count} مَسارٍ يُصَيَّر بِـGET.");

        Assert.True(inspected > 0,
            "صِفرُ وِجهَةِ تَحويلٍ فُحِصَت — الفاحِصُ أَعمى (القاعِدَة ١٠).");

        Assert.True(offenders.Count == 0,
            $"وِجهاتُ تَحويلٍ لا تُصَيَّر بِـGET (‏{offenders.Count} مِن {inspected} فُحِصَت):\n  " +
            string.Join("\n  ", offenders));
    }

    // ── جَمعُ المَسارات ───────────────────────────────────────────────

    private static IEnumerable<string> SourceFiles(string dir) =>
        Directory.Exists(dir)
            ? Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories)
                .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") &&
                            !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            : Array.Empty<string>();

    private static List<string> CollectGetRoutes(string dir)
    {
        var routes = new List<string>();
        if (!Directory.Exists(dir)) return routes;

        foreach (var razor in Directory.EnumerateFiles(dir, "*.razor", SearchOption.AllDirectories))
            foreach (Match m in Regex.Matches(File.ReadAllText(razor), @"@page\s+""(?<r>/[^""]*)"""))
                routes.Add(m.Groups["r"].Value);

        foreach (var cs in SourceFiles(dir))
        {
            var text = File.ReadAllText(cs);
            foreach (Match m in Regex.Matches(text, @"\bMapGet\(\s*""(?<r>/[^""]*)"""))
                routes.Add(m.Groups["r"].Value);
            foreach (Match m in Regex.Matches(text, @"\bWolverineGet\(\s*""(?<r>/[^""]*)"""))
                routes.Add(m.Groups["r"].Value);
        }
        return routes;
    }

    /// <summary>وِجهاتُ التَحويل: نِداءُ <c>Results.Redirect</c> بِسِلسِلَةٍ
    /// تَبدَأُ حَرفِيّاً بِـ<c>/</c>، وقيمَةُ <c>returnUrl=</c> داخِلَ أَيّ
    /// سِلسِلَة (فَهي وِجهَةُ تَحويلٍ مُؤَجَّلَة — وهي بِعَينِها ما
    /// أَنتَجَ الـ405).</summary>
    private static IEnumerable<string> RedirectTargets(string text)
    {
        foreach (Match m in Regex.Matches(text,
                     @"Results\.Redirect\(\s*\$?@?""(?<t>/[^""]*)"""))
            yield return Normalize(m.Groups["t"].Value);

        foreach (Match m in Regex.Matches(text, @"returnUrl=(?<t>/[^""&\s\\]*)"))
            yield return Normalize(m.Groups["t"].Value);
    }

    /// <summary><b>التَعليقُ لَيسَ كوداً</b> — وهذا مَقيسٌ لا مَظنون:
    /// أَوَّلُ تَشغيلٍ لِهذا الفَحصِ احمَرَّ عَلى سَطرَينِ في تَوثيقِ
    /// الإصلاحِ نَفسِه يَذكُرانِ الوِجهَةَ المَعطوبَةَ بِاسمِها. فاحِصٌ
    /// يَتَّهِم الشَرحَ لا يُوثَقُ بِه.</summary>
    private static string StripComments(string text)
    {
        text = Regex.Replace(text, @"/\*.*?\*/", "", RegexOptions.Singleline);
        var kept = text.Split('\n')
            .Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal));
        return string.Join('\n', kept);
    }

    /// <summary>يُسقِط الاستِعلامَ والمِرساةَ وما بَعدَ أَوَّلِ
    /// إقحامٍ لا يُشكِّل مَقطَعاً كامِلاً.</summary>
    private static string Normalize(string raw)
    {
        var t = raw.Split('?')[0].Split('#')[0];
        return t.Length > 1 ? t.TrimEnd('/') : t;
    }

    // ── المُطابَقَة ──────────────────────────────────────────────────

    private static bool Matches(string target, IReadOnlyList<string> routes)
        => routes.Any(r => SegmentsMatch(target, r));

    private static bool SegmentsMatch(string target, string route)
    {
        var t = Split(target);
        var r = Split(route);
        if (t.Length != r.Length) return false;

        for (var i = 0; i < t.Length; i++)
        {
            var rs = r[i];
            var ts = t[i];
            if (rs.StartsWith('{')) continue;                 // مُعامِلُ مَسار — يَقبَل أَيَّ مَقطَع
            if (ts.StartsWith('{') || ts.Contains('{')) continue; // إقحامٌ عِندَنا — لا يُحكَم عَلَيه
            if (!string.Equals(rs, ts, StringComparison.OrdinalIgnoreCase)) return false;
        }
        return true;
    }

    private static string[] Split(string path) =>
        path.Split('/', StringSplitOptions.RemoveEmptyEntries);

    private static string Rel(string root, string path) =>
        Path.GetRelativePath(root, path).Replace('\\', '/');
}
