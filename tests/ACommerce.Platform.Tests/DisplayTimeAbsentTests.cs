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

    /// <summary><b>قُلِبَ «Today_» بِاسمِه (‏2026-10-25، كوميتُ الإصلاح)</b> — كانَ <c>Today_an_unset_instant_is_drawn_as_a_clock_in_year_one</c>
    /// (‏<c>865acb3</c>): «0001-01-01 03:06». والآن دالَّةُ العَرضِ المُنَسِّقَةُ (<c>CultureTime.FormatTime</c> والسِياقُ) لا تَرسُمُ لِلَحظَةٍ لَم تُضبَط
    /// شَيئاً بِأَيِّ صيغَةٍ ومِنطَقَة — و<c>InZone</c> نَفسُها لَم تَتَبَدَّل (تُعطي الساعَةَ نَفسَها لِمَن يُقارِنُ بِها التاريخ).</summary>
    [Theory]
    [InlineData("yyyy-MM-dd HH:mm")]
    [InlineData("HH:mm")]
    [InlineData("MM-dd HH:mm")]
    [InlineData("yyyy-MM-dd")]
    public void An_unset_instant_is_drawn_as_nothing(string format)
    {
        var unspecified = DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Unspecified);
        var utc = DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc);
        output.WriteLine($"«{format}»: FormatTime(MinValue) = «{CultureTime.FormatTime(DateTime.MinValue, format)}» · وكانَت «{CultureTime.InZone(DateTime.MinValue).ToString(format)}»");

        Assert.Equal("", CultureTime.FormatTime(DateTime.MinValue, format));
        Assert.Equal("", CultureTime.FormatTime(default, format, "UTC"));
        Assert.Equal("", CultureTime.FormatTime(unspecified, format));
        Assert.Equal("", CultureTime.FormatTime(utc, format, "Mars/Olympus_Mons"));
        Assert.Equal("", new MutableCultureContext().FormatTime(DateTime.MinValue, format));
        Assert.StartsWith("0001-01-01", CultureTime.InZone(DateTime.MinValue).ToString("yyyy-MM-dd"), StringComparison.Ordinal);
    }

    /// <summary><b>قُلِبَ «Today_» بِاسمِه (‏2026-10-25، كوميتُ الإصلاح)</b> — كانَ <c>Today_every_display_site_formats_the_InZone_result_itself</c>
    /// (‏<c>865acb3</c>): ‏36 مَوضِعاً في ‏24 مِلَفّاً. والآن <b>صِفرٌ</b> — المَواضِعُ نَفسُها تُنادي <c>FormatTime</c> (‏36)، بِالماسِحِ لا بِقائِمَة،
    /// فَمَوضِعٌ يُضافُ غَداً يُنَسِّقُ نَتيجَةَ <c>InZone</c> بِيَدِه يُحمِرُّ هُنا بِمِلَفِّه وسَطرِه.</summary>
    [Fact]
    public void No_display_site_formats_the_InZone_result_itself_every_one_asks_FormatTime()
    {
        var sites = FormattedInZoneSites();
        var callers = EntitlementContractTests.SourceFiles()
            .Where(f => !f.File.Replace('\\', '/').EndsWith("/CultureContext.cs", StringComparison.Ordinal))
            .Select(f => (f.File, Code: f.File.EndsWith(".razor", StringComparison.OrdinalIgnoreCase)
                ? WriteEndpointGuardTests.StripComments(WriteEndpointGuardTests.StripMarkupComments(f.Text)) : WriteEndpointGuardTests.StripComments(f.Text)))
            .Select(f => (f.File, N: Regex.Matches(f.Code, @"(?<![\w])FormatTime\(").Count))
            .Where(f => f.N > 0).ToList();
        output.WriteLine($"مَواضِعُ تُنَسِّقُ نَتيجَةَ InZone: {sites.Count} · نِداءاتُ FormatTime: {callers.Sum(c => c.N)} في {callers.Count} مِلَفّاً");

        Assert.True(sites.Count == 0, "مَوضِعُ عَرضٍ يُنَسِّقُ نَتيجَةَ InZone بِيَدِه — فَلَحظَةٌ لَم تُضبَط تُرسَمُ «0001-01-01»:\n  "
            + string.Join("\n  ", sites.Select(s => $"{s.File}:{s.Line} {s.Text}")) + "\nاِستَعمِل Culture.FormatTime(x, \"…\").");
        Assert.True(callers.Sum(c => c.N) >= 36, $"أَداةٌ عَمياء: {callers.Sum(c => c.N)} نِداءً لِـFormatTime — والمَقيسُ 36.");
        Assert.Equal(24, callers.Count);
    }

    /// <summary><b>والمَضبوطُ بايتاً ما كانَ</b>: لِكُلِّ صيغَةٍ في الجَدوَلِ أَدناه وكُلِّ لَحظَةٍ فيه — بِأَنواعِ <c>Kind</c> الثَلاثَةِ ويَومٍ بَعدَ MinValue —
    /// <c>FormatTime(x, f)</c> هِيَ <c>InZone(x).ToString(f)</c> حَرفاً، بِالدالَّةِ والسِياق.</summary>
    [Theory]
    [InlineData("HH:mm")]
    [InlineData("yyyy-MM-dd")]
    [InlineData("MM-dd HH:mm")]
    [InlineData("yyyy-MM-dd HH:mm:ss")]
    public void A_set_instant_is_formatted_exactly_as_InZone_then_ToString(string format)
    {
        var instants = new[]
        {
            FixedUtc, DateTime.SpecifyKind(FixedUtc, DateTimeKind.Unspecified), FixedUtc.ToLocalTime(),
            DateTime.MinValue.AddDays(1), new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
        };
        foreach (var x in instants)
        {
            Assert.Equal(CultureTime.InZone(x).ToString(format), CultureTime.FormatTime(x, format));
            Assert.Equal(CultureTime.InZone(x, "UTC").ToString(format), CultureTime.FormatTime(x, format, "UTC"));
            Assert.Equal(new MutableCultureContext().InZone(x).ToString(format), new MutableCultureContext().FormatTime(x, format));
        }
    }

    /// <summary><b>وماسِحُ راسِمي الوَقتِ يَعُدُّ FormatTime مِنطَقِيّاً</b> (القاعِدَة ١٠ — <c>DisplayTimeZoneTests.TimeRendererVerdict</c> مُلحَقاً):
    /// سَطرٌ يُنادي <c>FormatTime</c> «zoned»، وسَطرُ تَعليقٍ يَذكُرُها لَيسَ راسِماً.</summary>
    [Fact]
    public void The_time_renderer_verdict_counts_FormatTime_as_zoned()
    {
        const string Page = "Components/Pages/Injected.razor";
        Assert.Equal("zoned", DisplayTimeZoneTests.TimeRendererVerdict(Page, "<span>@Culture.FormatTime(n.At, \"HH:mm\")</span>"));
        Assert.Equal("zoned", DisplayTimeZoneTests.TimeRendererVerdict(Page, "(\"x\", ACommerce.Kit.Culture.CultureTime.FormatTime(s.UpdatedAt, \"yyyy-MM-dd HH:mm\")),"));
        Assert.Null(DisplayTimeZoneTests.TimeRendererVerdict(Page, "// ‏مِثالٌ: Culture.FormatTime(n.At, \"HH:mm\")"));
        Assert.Equal("bad", DisplayTimeZoneTests.TimeRendererVerdict(Page, "<span>@n.At.ToString(\"HH:mm\")</span>"));
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
