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

    /// <summary><para><b>المُشتَقُّ مِن <c>InZone</c> مُنَسَّقاً في مَوضِعٍ آخَر</b> (‏2026-10-25، مُراجَعَةُ تَيّارِ W8-7 الجَولَة ١): الماسِحُ أَعلاه لا يَعُدُّ
    /// <c>var local = Culture.InZone(…);</c> — ولا ما يَفعَلُه السَطرُ التالي بِـ<c>local</c>. وهذا يَعُدُّه: مُتَغَيِّرٌ يُعلَنُ أَو يُسنَدُ مِن تَعبيرٍ فيه
    /// <c>InZone(</c> أَو مُتَغَيِّرٌ مُشتَقٌّ (حَتّى الثَبات)، ومُعامِلٌ مِن نَوعِ <c>DateTime</c> في دالَّةٍ في المِلَفِّ نَفسِه يَتَسَلَّمُ مُشتَقّاً بِمَوضِعِه —
    /// ثُمَّ كُلُّ <c>x.ToString(</c> (و<c>x.Date.ToString(</c>/<c>x.Value.ToString(</c>) وكُلُّ ثُقبِ إقحامٍ <c>{x:…}</c> عَلى مُشتَقّ.</para>
    /// <para><b>وما لا يَراه</b>: الاشتِقاقُ عَبرَ مِلَفَّين أَو حَقلٍ أَو مُجَمَّعَة، والاسمُ يُعامَلُ عَلى مُستَوى المِلَفِّ لا النِطاق (أَحوَطُ لا أَضيَق).</para></summary>
    internal static List<Site> DerivedInZoneFormatsIn(IEnumerable<(string File, string Text)> sources)
    {
        var sites = new List<Site>();
        foreach (var (file, text) in sources)
        {
            var rel = Path.IsPathRooted(file) ? Path.GetRelativePath(ThemeZeroEquivalenceTests.RepoRoot, file).Replace('\\', '/') : file;
            if (rel.EndsWith("/CultureContext.cs", StringComparison.Ordinal)) continue;
            var razor = rel.EndsWith(".razor", StringComparison.OrdinalIgnoreCase);
            var code = razor ? WriteEndpointGuardTests.StripComments(WriteEndpointGuardTests.StripMarkupComments(text)) : WriteEndpointGuardTests.StripComments(text);
            if (!code.Contains("InZone(", StringComparison.Ordinal)) continue;

            var tainted = new HashSet<string>(StringComparer.Ordinal);
            bool Mentions(string rhs) => rhs.Contains("InZone(", StringComparison.Ordinal)
                || tainted.Any(t => Regex.IsMatch(rhs, $@"(?<![\w.]){Regex.Escape(t)}(?![\w(])"));
            var assignments = Regex.Matches(code, @"(?:\b(?:var|DateTime\??)\s+|(?<=[;{}]\s*))(?<v>[A-Za-z_]\w*)\s*=(?!=)\s*(?<rhs>[^;""]*);")
                .Select(m => (V: m.Groups["v"].Value, Rhs: m.Groups["rhs"].Value)).ToList();
            var methods = Regex.Matches(code, @"\b[A-Za-z_][\w<>?]*\s+(?<name>[A-Za-z_]\w*)\s*\((?<ps>[^()]*)\)\s*(?:=>|\{|\r?\n\s*\{)")
                .Select(m => (Name: m.Groups["name"].Value, Params: m.Groups["ps"].Value.Split(',').Select(p => p.Trim()).ToArray())).ToList();
            for (var changed = true; changed;)
            {
                changed = false;
                foreach (var (v, rhs) in assignments)
                    if (!tainted.Contains(v) && Mentions(rhs)) changed |= tainted.Add(v);
                foreach (var (name, ps) in methods)
                    foreach (Match call in Regex.Matches(code, $@"(?<![\w.]){Regex.Escape(name)}\s*\((?<args>[^()]*)\)"))
                    {
                        var args = call.Groups["args"].Value.Split(',').Select(a => a.Trim()).ToArray();
                        for (var k = 0; k < args.Length && k < ps.Length; k++)
                        {
                            var p = Regex.Match(ps[k], @"^DateTime\??\s+(?<p>[A-Za-z_]\w*)$");
                            if (p.Success && tainted.Contains(args[k]) && tainted.Add(p.Groups["p"].Value)) changed = true;
                        }
                    }
            }
            foreach (var t in tainted)
                foreach (Match m in Regex.Matches(code, $@"(?<![\w.]){Regex.Escape(t)}(?:\.(?:Value|Date))*\.ToString\(|\{{{Regex.Escape(t)}(?:\.(?:Value|Date))*:[^{{}}""]*\}}"))
                {
                    var lineStart = code.LastIndexOf('\n', Math.Max(0, m.Index - 1)) + 1;
                    var lineEnd = code.IndexOf('\n', m.Index);
                    sites.Add(new Site(rel, code[..m.Index].Count(c => c == '\n') + 1, code[lineStart..(lineEnd < 0 ? code.Length : lineEnd)].Trim()));
                }
        }
        return sites.OrderBy(s => s.File, StringComparer.Ordinal).ThenBy(s => s.Line).ToList();
    }

    internal static List<Site> DerivedInZoneFormats() => DerivedInZoneFormatsIn(EntitlementContractTests.SourceFiles());

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
        // ‏(‏2026-10-25، مُراجَعَةُ تَيّارِ W8-7 الجَولَة ١): عَدُّ المِلَفّاتِ (24) انتَقَلَ إلى صَفِّ «اليَوم» لِلمُشتَقِّ أَدناه — هُوَ ما يَتَبَدَّلُ بِتَبديلِ المُحادَثات.
    }

    /// <summary>مِلَفّاتُ نِداءاتِ <c>FormatTime(</c> خارِجَ <c>CultureContext.cs</c> وعَدَدُها — ما يَعُدُّه الصَفُّ أَعلاه.</summary>
    private static List<(string File, int N)> FormatTimeCallers()
        => EntitlementContractTests.SourceFiles()
            .Where(f => !f.File.Replace('\\', '/').EndsWith("/CultureContext.cs", StringComparison.Ordinal))
            .Select(f => (f.File, Code: f.File.EndsWith(".razor", StringComparison.OrdinalIgnoreCase)
                ? WriteEndpointGuardTests.StripComments(WriteEndpointGuardTests.StripMarkupComments(f.Text)) : WriteEndpointGuardTests.StripComments(f.Text)))
            .Select(f => (f.File, N: Regex.Matches(f.Code, @"(?<![\w])FormatTime\(").Count))
            .Where(f => f.N > 0).ToList();

    /// <summary><b>قُلِبَ «Today_» بِاسمِه (‏2026-10-25، كوميتُ التَبديل؛ مُراجَعَةُ تَيّارِ W8-7 الجَولَة ١ — صُغرى)</b> — كانَ
    /// <c>Today_seven_chat_sites_format_a_local_derived_from_InZone_so_an_unset_instant_would_draw_year_one</c> (‏<c>07e5af1</c>): سَبعَةُ مَواضِعَ في
    /// <c>ChatRoom</c> و<c>Chats</c> تُنَسِّقُ مُتَغَيِّراً مُشتَقّاً مِن <c>InZone</c>. والآن صِفرٌ في الشَجَرَة: الفُقاعَةُ وعُنوانُ اليَومِ و«متى» تُنَسِّقُ
    /// بِـ<c>FormatTime</c> مِن اللَحظَةِ المُخَزَّنَة (واسمُ اليَومِ بِـ<c>CultureTime.FormatTime(…, Culture.TimeZone, ar)</c>)، والتاريخُ المَحَلِّيُّ لِلمُقارَنَةِ
    /// وَحدَها. و<c>Chats.razor</c> صارَ مِن مِلَفّاتِ <c>FormatTime</c> — ‏25.</summary>
    [Fact]
    public void No_chat_site_formats_a_local_derived_from_InZone_every_branch_asks_FormatTime()
    {
        var sites = DerivedInZoneFormats();
        var callers = FormatTimeCallers();
        var room = WriteEndpointGuardTests.StripComments(WriteEndpointGuardTests.StripMarkupComments(File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot,
            "libs/templates/ACommerce.Templates.Customer.Marketplace/Components/Pages/ChatRoom.razor".Replace('/', Path.DirectorySeparatorChar)))));
        var list = WriteEndpointGuardTests.StripComments(WriteEndpointGuardTests.StripMarkupComments(File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot,
            "libs/templates/ACommerce.Templates.Customer.Marketplace/Components/Pages/Chats.razor".Replace('/', Path.DirectorySeparatorChar)))));
        output.WriteLine($"مَواضِعُ تُنَسِّقُ مُشتَقّاً مِن InZone: {sites.Count} · مِلَفّاتُ FormatTime: {callers.Count}"
            + (sites.Count == 0 ? "" : "\n  " + string.Join("\n  ", sites.Select(x => $"{Path.GetFileName(x.File)}:{x.Line} {x.Text}"))));

        Assert.True(sites.Count == 0, "مَوضِعُ عَرضٍ يُنَسِّقُ مُتَغَيِّراً مُشتَقّاً مِن InZone — فَلَحظَةٌ لَم تُضبَط تُرسَمُ سَنَةً أولى:\n  "
            + string.Join("\n  ", sites.Select(x => $"{x.File}:{x.Line} {x.Text}")) + "\nنَسِّق بِـCulture.FormatTime مِن اللَحظَةِ المُخَزَّنَة.");
        Assert.Contains("Time=\"@Culture.FormatTime(m.SentAt, \"HH:mm\")\"", room, StringComparison.Ordinal);
        Assert.Contains("@FormatDay(m.SentAt)", room, StringComparison.Ordinal);
        Assert.Contains("return Culture.FormatTime(sentUtc, \"yyyy/MM/dd\");", room, StringComparison.Ordinal);
        Assert.Contains("return Culture.FormatTime(at, \"yyyy/MM/dd\");", list, StringComparison.Ordinal);
        Assert.Contains("CultureTime.FormatTime(at, \"dddd\", Culture.TimeZone, new System.Globalization.CultureInfo(\"ar\"))", list, StringComparison.Ordinal);
        // ‏2026-10-29 (قَرارُ المالِك د53): ‏25 ← 27 — صَفحَةُ «بَياناتي» (`MyData.razor` بِـ`Culture.FormatTime`) واسمُ مِلَفِّ تَنزيلِها
        // (`UserDataExport.FileName` بِـ`CultureTime.FormatTime` ومِنطَقَةِ الثَقافَة) — وكِلاهُما مِن اللَحظَةِ المُخَزَّنَة.
        Assert.Equal(27, callers.Count);
        Assert.Contains(callers, c => c.File.EndsWith("Chats.razor", StringComparison.Ordinal));
    }

    /// <summary><b>وكُلُّ لَحظَةٍ مَضبوطَةٍ في المُحادَثاتِ بايتاً ما كانَت</b>: لِكُلِّ صيغَةٍ تُنَسِّقُها الفُقاعَةُ وعُنوانُ اليَومِ و«متى» — ومَعَها اسمُ اليَومِ بِثَقافَةِ
    /// <c>ar</c> — <c>CultureTime.FormatTime(x, f, tz, provider)</c> هِيَ <c>InZone(x, tz).ToString(f, provider)</c> حَرفاً، وبِثَقافَةِ الخَيطِ هِيَ ذاتُ الثَلاثَة؛
    /// ولَحظَةٌ لَم تُضبَط نَصٌّ فارِغ — لا «0001/01/01».</summary>
    [Theory]
    [InlineData("HH:mm", false)]
    [InlineData("dd/MM", false)]
    [InlineData("yyyy/MM/dd", false)]
    [InlineData("dddd", true)]
    public void A_set_chat_instant_is_formatted_exactly_as_before_and_an_unset_one_is_empty(string format, bool arabic)
    {
        var ar = new System.Globalization.CultureInfo("ar");
        var zone = new MutableCultureContext().TimeZone;
        string After(DateTime x) => arabic ? CultureTime.FormatTime(x, format, zone, ar) : new MutableCultureContext().FormatTime(x, format);
        foreach (var x in new[] { FixedUtc, DateTime.SpecifyKind(FixedUtc, DateTimeKind.Unspecified), FixedUtc.AddDays(-3), DateTime.MinValue.AddDays(1) })
        {
            var before = arabic ? CultureTime.InZone(x, zone).ToString(format, ar) : CultureTime.InZone(x, zone).ToString(format);
            Assert.Equal(before, After(x));
        }
        Assert.Equal("", After(DateTime.MinValue));
        Assert.Equal(CultureTime.FormatTime(FixedUtc, format, zone), CultureTime.FormatTime(FixedUtc, format, zone, System.Globalization.CultureInfo.CurrentCulture));
    }

    /// <summary><b>ماسِحُ المُشتَقِّ يُقاسُ قَبلَ أَن يُوثَقَ بِه</b> (القاعِدَة ١٠): مُتَغَيِّرٌ مِن <c>InZone</c> مُنَسَّق، ومُشتَقٌّ مِنه بِـ<c>.Date</c> مُنَسَّق، ومُعامِلُ
    /// دالَّةٍ يَتَسَلَّمُه ثُمَّ يُنَسَّق، وثُقبُ إقحامٍ عَلَيه — أَربَعَتُها تُعَدّ؛ ومُقارَنَتُه بِتاريخ، ولَحظَةٌ لَيسَت مِنه، وتَعليقٌ يُنَسِّقُه،
    /// و<c>FormatTime</c> — لا تُعَدّ.</summary>
    [Fact]
    public void The_derived_scanner_counts_a_formatted_local_a_derived_date_a_parameter_and_a_hole_but_not_a_comparison()
    {
        var sources = new[]
        {
            ("x/Injected.razor",
                "@{ var local = Culture.InZone(m.SentAt); var day = local.Date; var other = DateTime.UtcNow; }\n"
              + "<span>@local.ToString(\"HH:mm\")</span>\n"
              + "<span>@day.ToString(\"yyyy\")</span>\n"
              + "<span>@FormatDay(local)</span>\n"
              + "<span>@($\"{local:dd/MM}\")</span>\n"
              + "@if (local.Date == today) { <b>اليوم</b> }\n"
              + "<span>@other.ToString(\"HH:mm\")</span>\n"
              + "@* <span>@local.ToString(\"HH:mm\")</span> *@\n"
              + "<span>@Culture.FormatTime(m.SentAt, \"HH:mm\")</span>\n"
              + "@code {\n    string FormatDay(DateTime localAt)\n    {\n        return localAt.ToString(\"dddd\");\n    }\n}\n"),
        };
        var sites = DerivedInZoneFormatsIn(sources);
        output.WriteLine($"مَحقونَة: {sites.Count} · " + string.Join(" · ", sites.Select(x => $"{x.File}:{x.Line}")));

        Assert.Equal(new[] { "x/Injected.razor:2", "x/Injected.razor:3", "x/Injected.razor:5", "x/Injected.razor:13" },
            sites.Select(x => $"{x.File}:{x.Line}").ToArray());
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
