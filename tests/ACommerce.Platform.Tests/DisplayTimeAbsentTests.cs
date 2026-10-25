using System.Text.RegularExpressions;
using ACommerce.Kit.Culture;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ لَحظَةٌ لَم تُضبَط (`DateTime.MinValue`) تُرسَمُ «0001-01-01 03:06» — ساعَةُ الرياضِ في السَنَةِ الأولى ═══
//
// **تَيّارُ W8-7، بَندُ المُنَسِّق** (‏2026-10-25): التَذاكِرُ — وكُلُّ سِجِلٍّ وَقتُه `DateTime.MinValue` — تُرسَمُ «0001-01-01 03:06»
// (‏`docs/RELEASE-READINESS.md`، جَدوَلُ لَقطاتِ المَظهَر: `studio-app-console` و`studio-app-tickets` و`studio-study-print` «0001-01-01 00:00» ←
// «0001-01-01 03:06»). والسَبَب: دالَّةُ الوَقتِ المَعروضِ (`CultureTime.InZone`، تَيّارُ S15 البَند B) تُحَوِّلُ كُلَّ لَحظَةٍ إلى ساعَةِ الرياض،
// والرياضُ في السَنَةِ الأولى بِتَوقيتِها المَحَلِّيِّ الشَمسِيّ (+03:06:52 في tzdata) — وكُلُّ مَوضِعِ عَرضٍ يُنَسِّقُ ما أَعادَتهُ بِنَفسِه
// (`Culture.InZone(x).ToString("…")`)، فَلا مَوضِعَ يَسأَلُ «أَضُبِطَت اللَحظَةُ أَصلاً؟».
//
// هذا المِلَفُّ يُوَصِّفُ «اليَوم» (القاعِدَة ٣) قَبلَ أَيِّ تَبديل؛ صَفّا `Today_` يُقلَبانِ بِاسمَيهِما في كوميتِ الإصلاح، والباقي يَبقى بِلا تَعديلِ حَرف.
public class DisplayTimeAbsentTests(ITestOutputHelper output)
{
    /// <summary>‏21:30 بِـUTC — ‏00:30 مِن اليَومِ التالي في الرياض (اللَحظَةُ الثابِتَةُ في <c>DisplayTimeZoneTests</c>).</summary>
    private static readonly DateTime FixedUtc = DisplayTimeZoneTests.FixedUtc;

    // ─── الماسِح: مَوضِعُ عَرضٍ يُنَسِّقُ ما أَعادَتهُ InZone بِنَفسِه ─────────────────────────────────

    /// <summary>مَوضِعُ عَرضٍ واحِد: <c>InZone(…)</c> يَليه <c>.ToString(</c> بَعدَ قَوسِه المُقابِل، أَو داخِلَ ثُقبِ إقحامٍ بِمُحَدِّدِ صيغَة
    /// (<c>{…InZone(…):HH:mm}</c>).</summary>
    internal sealed record Site(string File, int Line, string Text);

    /// <summary>كُلُّ مَوضِعِ عَرضٍ يُنَسِّقُ نَتيجَةَ <c>InZone</c> في المَصادِر — بِلا تَعليقات (تُبَيَّضُ ويَبقى الطول)، وبِلا مِلَفِّ الدالَّةِ نَفسِها
    /// (<c>CultureContext.cs</c>). و<c>var local = Culture.InZone(…);</c> لا يُعَدّ: لا يُنَسِّقُ في مَوضِعِه.</summary>
    internal static List<Site> FormattedInZoneSitesIn(IEnumerable<(string File, string Text)> sources)
    {
        var sites = new List<Site>();
        foreach (var (file, text) in sources)
        {
            var rel = Path.IsPathRooted(file) ? Path.GetRelativePath(ThemeZeroEquivalenceTests.RepoRoot, file).Replace('\\', '/') : file;
            if (rel.EndsWith("/CultureContext.cs", StringComparison.Ordinal)) continue;
            var razor = rel.EndsWith(".razor", StringComparison.OrdinalIgnoreCase);
            var code = razor ? WriteEndpointGuardTests.StripComments(WriteEndpointGuardTests.StripMarkupComments(text)) : WriteEndpointGuardTests.StripComments(text);
            foreach (Match m in Regex.Matches(code, @"(?<![\w])InZone\("))
            {
                var open = m.Index + m.Length - 1;
                var close = Closing(code, open);
                if (close < 0) continue;
                var tail = code[(close + 1)..];
                var formatted = tail.StartsWith(".ToString(", StringComparison.Ordinal)
                                || Regex.IsMatch(tail, @"\A:[^{}""]*\}");
                if (!formatted) continue;
                var lineStart = code.LastIndexOf('\n', m.Index) + 1;
                var lineEnd = code.IndexOf('\n', m.Index);
                sites.Add(new Site(rel, code[..m.Index].Count(c => c == '\n') + 1, code[lineStart..(lineEnd < 0 ? code.Length : lineEnd)].Trim()));
            }
        }
        return sites;
    }

    internal static List<Site> FormattedInZoneSites() => FormattedInZoneSitesIn(EntitlementContractTests.SourceFiles());

    private static int Closing(string s, int open)
    {
        var depth = 0;
        for (var k = open; k < s.Length; k++)
        {
            if (s[k] == '(') depth++;
            else if (s[k] == ')' && --depth == 0) return k;
        }
        return -1;
    }

    // ─── «اليَوم» ───────────────────────────────────────────────────────────────────────────

    /// <summary><b>«اليَوم»</b>: لَحظَةٌ لَم تُضبَط تَخرُجُ مِن دالَّةِ العَرضِ ساعَةً في السَنَةِ الأولى — والسِياقُ يُعطي ما تُعطيه الدالَّة.</summary>
    [Fact]
    public void Today_an_unset_instant_is_drawn_as_a_clock_in_year_one()
    {
        var drawn = CultureTime.InZone(DateTime.MinValue).ToString("yyyy-MM-dd HH:mm");
        var viaContext = new MutableCultureContext().InZone(DateTime.MinValue).ToString("yyyy-MM-dd HH:mm");
        output.WriteLine($"DateTime.MinValue بِساعَةِ {CultureTime.DefaultTimeZone}: «{drawn}» · بِالسِياق: «{viaContext}» · default(DateTime) == MinValue: {default(DateTime) == DateTime.MinValue}");

        Assert.StartsWith("0001-01-01 ", drawn, StringComparison.Ordinal);
        Assert.Equal(drawn, viaContext);
        Assert.Equal(DateTime.MinValue, default(DateTime));
    }

    /// <summary><b>«اليَوم»</b>: كُلُّ مَوضِعِ عَرضٍ يُنَسِّقُ نَتيجَةَ <c>InZone</c> بِنَفسِه — ‏36 مَوضِعاً في ‏24 مِلَفّاً — فَلا واحِدَ مِنها يَسأَلُ أَضُبِطَت اللَحظَة.</summary>
    [Fact]
    public void Today_every_display_site_formats_the_InZone_result_itself()
    {
        var sites = FormattedInZoneSites();
        output.WriteLine($"مَواضِعُ تُنَسِّقُ نَتيجَةَ InZone: {sites.Count} في {sites.Select(s => s.File).Distinct().Count()} مِلَفّاً · "
            + string.Join(" · ", sites.GroupBy(s => Path.GetFileName(s.File)).Select(g => $"{g.Key}: {g.Count()}")));

        Assert.True(sites.Count >= 36, $"أَداةٌ عَمياء: {sites.Count} مَوضِعاً — والمَقيسُ 36.");
        Assert.Equal(36, sites.Count);
    }

    // ─── ما لا يَتَبَدَّل ───────────────────────────────────────────────────────────────────

    /// <summary><b>لَحظَةٌ مَضبوطَةٌ تُرسَمُ ساعَتَها في مِنطَقَةِ الثَقافَة</b> — بِأَربَعِ صيَغٍ تَستَعمِلُها الصَفَحات، ولَحظَةٌ عِندَ حافَّةِ السَنَةِ الأولى
    /// لَيسَت «غَيرَ مَضبوطَة» (يَومٌ بَعدَ MinValue يُرسَمُ). هذا ما يَجِبُ أَن يَبقى بايتاً بَعدَ أَيِّ تَبديل.</summary>
    [Theory]
    [InlineData("HH:mm", "00:30")]
    [InlineData("yyyy-MM-dd", "2026-10-25")]
    [InlineData("MM-dd HH:mm", "10-25 00:30")]
    [InlineData("yyyy-MM-dd HH:mm:ss", "2026-10-25 00:30:00")]
    public void A_set_instant_is_drawn_as_its_clock_in_the_culture_zone(string format, string expected)
    {
        Assert.Equal(expected, CultureTime.InZone(FixedUtc).ToString(format));
        Assert.Equal(expected, new MutableCultureContext().InZone(FixedUtc).ToString(format));
        Assert.StartsWith("0001-01-02", CultureTime.InZone(DateTime.MinValue.AddDays(1)).ToString("yyyy-MM-dd"), StringComparison.Ordinal);
    }

    /// <summary><b>الماسِحُ يُقاسُ قَبلَ أَن يُوثَقَ بِه</b> (القاعِدَة ١٠): مِلَفّاتٌ مَحقونَةٌ فيها تَنسيقٌ بَعدَ <c>InZone</c> بِقَوسَينِ مُتَداخِلَين، وثُقبُ إقحام،
    /// ومَوضِعٌ في <c>.cs</c> — ثَلاثَتُها تُعَدّ؛ و<c>var local = Culture.InZone(…)</c> والتَعليقُ ومِلَفُّ الدالَّةِ نَفسُه لا تُعَدّ.</summary>
    [Fact]
    public void The_scanner_counts_a_nested_call_an_interpolation_and_a_cs_site_but_not_a_kept_local()
    {
        var sources = new[]
        {
            ("x/Injected.razor",
                "<span>@Culture.InZone(listingMatch.ArrivedAt!.Value).ToString(\"HH:mm\")</span>\n"
              + "<span>@($\"{Culture.InZone(n.At):HH:mm}\")</span>\n"
              + "@{ var local = Culture.InZone(m.SentAt); }\n"
              + "@* <span>@Culture.InZone(n.At).ToString(\"HH:mm\")</span> *@\n"),
            ("x/Injected.cs", "class X { string M(S s) => ACommerce.Kit.Culture.CultureTime.InZone(s.UpdatedAt).ToString(\"yyyy-MM-dd\"); // InZone(a).ToString(\n}"),
            ("libs/kits/Culture/ACommerce.Kit.Culture.Core/CultureContext.cs", "public DateTime NowInZone => CultureTime.InZone(DateTime.UtcNow, TimeZone).ToString(\"x\");"),
        };
        var sites = FormattedInZoneSitesIn(sources);
        output.WriteLine($"مَحقونَة: {sites.Count} · " + string.Join(" · ", sites.Select(s => $"{s.File}:{s.Line}")));

        Assert.Equal(new[] { "x/Injected.razor:1", "x/Injected.razor:2", "x/Injected.cs:1" }, sites.Select(s => $"{s.File}:{s.Line}").ToArray());
    }
}
