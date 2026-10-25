using System.Text.RegularExpressions;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ لا اختِبارَ يَكتُبُ حَقلاً شَخصِيّاً إلى خَرجِه — تَوصيفُ «اليَوم» (بَندُ المُنَسِّقِ في تَيّارِ W8-2، ‏2026-10-25) ═══
//
// **المَقيس** (عِندَ `f99f79a`): `LiveThemeProofTests.ReadThemeDocumentsAndNamePlatformAdmins` يَكتُبُ إلى `Console` ومِلَفِّ الأَدِلَّةِ
// «‏[platform-admin] phone={a.Phone} name={a.FullName}» لِكُلِّ مُشرِفِ مَنَصَّةٍ في القاعِدَةِ الَّتي يُشَغَّلُ عَلَيها — وهِيَ قاعِدَةٌ حَيَّةٌ بِالتَصميم
// (`WASAYEL_LIVE_PROOF=1`) — و«by={d.CreatedBy}» لِكُلِّ وَثيقَةِ ثيم، وبابُ الثيمِ يَكتُبُ فيه «{u.FullName} · {u.Phone}»؛ وبِصِفرِ `Assert`:
// يَخضَرُّ ما لَم يَرمِ شَيء. **والماسِحُ وَجَدَ اثنَينِ آخَرَين**: `LiveRoleDefinitionProofTests` يَكتُبُ السَطرَينِ نَفسَيهِما (نَسخٌ مِن البُرهانِ
// الأَوَّل)، و`LiveApprovalFlowProofTests` يَكتُبُ `decidedBy={doc.DecidedBy}` مَرَّتَين — وبابُ القَرارِ يَكتُبُ فيه الصيغَةَ نَفسَها. وفيهِما
// `Assert`ات. فَخَرجُ الاختِبارِ (سِجِلُّ CI، مِلَفُّ الأَدِلَّة) يَحمِلُ جَوّالاتِ المُشرِفينَ وأَسماءَهُم.
//
// **الماسِح**: كُلُّ `.cs` تَحتَ `tests/` (بِلا تَعليقات): نِداءُ `WriteLine`/`Write` — ومِن ثَمَّ كُلُّ دالَّةٍ مَحَلِّيَّةٍ في المِلَفِّ يَكتُبُ جِسمُها
// إلى `Console` أَو `.WriteLine(` (كَـ`Log` هُناك) — تَحمِلُ وُسَطاؤُه `.Phone` أَو `.Email` أَو `.FullName` أَو `.NationalId`. ويَطبَعُ ما فَحَصَه
// ويَفشَلُ إن كانَ صِفراً (القاعِدَة ١٠). وهذا المِلَفُّ نَفسُه خارِجَ المَسحِ لِأَنّ عَيِّناتِه نُصوص — وتُقاسُ بِالدالَّةِ نَفسِها أَدناه.
//
// **يُثَبَّتُ قَبلَ العِلاجِ** (القاعِدَة ٣): الصَفّانِ `Today_…` يَنقَلِبانِ بِاسمَيهِما، وصَفُّ العَيِّناتِ المَحقونَةِ يَبقى حَرفاً. وأَوَّلُ مَسحٍ وَجَدَ
// أَربَعَةَ مِلَفّاتٍ بِالحُقولِ الأَربَعَة: اثنانِ مِنها كاذِبان — `GetType().FullName` اسمُ نَوع، وعَيِّنَةُ `ListingOwnerCountScriptTests` سِلسِلَةٌ لِماسِحِه — فَصارَ
// النَمَطُ يَستَثني اسمَ النَوعِ والماسِحُ يُفَرِّغُ السِلسِلَةَ العادِيَّة، وكِلاهُما بِصَفٍّ هُنا.
public class TestOutputPersonalDataTests(ITestOutputHelper output)
{
    /// <summary>الحُقولُ الأَربَعَةُ الَّتي سَمّاها التَكليف، و<c>CreatedBy</c>/<c>DecidedBy</c> لِأَنّ قيمَتَهُما مَقيسَة: بابُ الثيمِ والأَدوارِ يَكتُبُ
    /// فيهِما «<c>{u.FullName} · {u.Phone}</c>» (`MarketplaceTemplateExtensions.cs`). و<c>GetType().FullName</c>/<c>typeof(…).FullName</c> اسمُ نَوعٍ لا إنسان.</summary>
    private const string PersonalField =
        @"(?<!GetType\(\))(?<!typeof\([\w.<>]*\))\.(?:Phone|Email|FullName|NationalId|CreatedBy|DecidedBy)\b";

    /// <summary>
    /// <para><b>مُحتَوى السِلسِلَةِ غَيرِ المُستَوفاةِ يُفَرَّغ</b> (عادِيَّةً أَو <c>@"…"</c> أَو خامّاً <c>"""…"""</c>) ويَبقى الطول: عَيِّنَةٌ نَصِّيَّةٌ لِماسِحٍ
    /// آخَرَ لَيسَت كِتابَةً إلى الخَرج، والحَقلُ خارِجَ السِلسِلَةِ يَبقى ظاهِراً. والمُستَوفاةُ (<c>$"…"</c>) تَبقى كَما هِيَ — ثُقوبُها هِيَ ما يُقاس.</para>
    ///
    /// <para><b>ولِماذا ماسِحٌ يَدَوِيٌّ لا تَعبيرٌ نَمَطِيّ</b>: أَوَّلُ نُسخَةٍ كانَت تَعبيراً، وعِلَّتُه مَقروءَةٌ قَبلَ القِياس — عَلامَةُ الإغلاقِ في
    /// <c>$"…"</c> تُرى بِدايَةَ سِلسِلَةٍ عادِيَّةٍ فَتَبتَلِعُ ما بَعدَها إلى أَوَّلِ عَلامَة، ومَعَه نِداءُ كاتِبٍ حَقيقِيّ. فَالماسِحُ يَتبَعُ الثُقوبَ
    /// وما فيها مِن سَلاسِلَ مُتَداخِلَة، و<c>'"'</c> حَرفٌ لا سِلسِلَة.</para>
    /// </summary>
    internal static string BlankPlainStrings(string code)
    {
        var b = code.ToCharArray();
        for (var i = 0; i < code.Length; i++)
        {
            if (code[i] == '\'') { i = EndOfChar(code, i); continue; }
            if (code[i] != '"') continue;
            var (interpolated, verbatim) = PrefixAt(code, i);
            var close = EndOfString(code, i, interpolated, verbatim, out var bodyStart, out var bodyEnd);
            if (!interpolated)
                for (var j = bodyStart; j < bodyEnd; j++) if (b[j] is not ('\n' or '\r')) b[j] = ' ';
            i = close;
        }
        return new string(b);
    }

    private static (bool Interpolated, bool Verbatim) PrefixAt(string code, int quote)
    {
        bool interpolated = false, verbatim = false;
        for (var k = quote - 1; k >= 0 && code[k] is '$' or '@'; k--)
            if (code[k] == '$') interpolated = true; else verbatim = true;
        return (interpolated, verbatim);
    }

    private static int EndOfChar(string code, int i)
    {
        for (var j = i + 1; j < code.Length; j++)
        {
            if (code[j] == '\\') { j++; continue; }
            if (code[j] == '\'' || code[j] == '\n') return j;
        }
        return code.Length - 1;
    }

    /// <summary>فِهرِسُ عَلامَةِ الإغلاق، ومَدى المُحتَوى بَينَ العَلامَتَين.</summary>
    private static int EndOfString(string code, int i, bool interpolated, bool verbatim, out int bodyStart, out int bodyEnd)
    {
        // الخامّ: ثَلاثُ عَلاماتٍ أَو أَكثَر، ويُغلِقُه العَدَدُ نَفسُه.
        var run = 0;
        while (i + run < code.Length && code[i + run] == '"') run++;
        if (run >= 3)
        {
            var fence = new string('"', run);
            var end = code.IndexOf(fence, i + run, StringComparison.Ordinal);
            bodyStart = i + run;
            bodyEnd = end < 0 ? code.Length : end;
            return end < 0 ? code.Length - 1 : end + run - 1;
        }
        bodyStart = i + 1;
        var depth = 0;
        for (var j = i + 1; j < code.Length; j++)
        {
            var ch = code[j];
            if (depth == 0)
            {
                if (!verbatim && ch == '\\') { j++; continue; }
                if (ch == '"')
                {
                    if (verbatim && j + 1 < code.Length && code[j + 1] == '"') { j++; continue; }
                    bodyEnd = j;
                    return j;
                }
                if (interpolated && ch == '{')
                {
                    if (j + 1 < code.Length && code[j + 1] == '{') { j++; continue; }
                    depth = 1;
                }
            }
            else if (ch == '{') depth++;
            else if (ch == '}') depth--;
            else if (ch == '\'') j = EndOfChar(code, j);
            else if (ch == '"')
            {
                var (ni, nv) = PrefixAt(code, j);
                j = EndOfString(code, j, ni, nv, out _, out _);
            }
        }
        bodyEnd = code.Length;
        return code.Length - 1;
    }

    /// <summary>مِن مَوضِعِ القَوسِ الفاتِحِ إلى مُقابِلِه — والفَتحُ والغَلقُ داخِلَ ثُقوبِ الاستيفاءِ مُتَوازِنانِ فَيُعَدّانِ كَغَيرِهِما.</summary>
    private static string Balanced(string text, int open, char o, char c)
    {
        var depth = 0;
        for (var i = open; i < text.Length; i++)
        {
            if (text[i] == o) depth++;
            else if (text[i] == c && --depth == 0) return text[open..(i + 1)];
        }
        return text[open..];
    }

    /// <summary>أَسماءُ ما يَكتُبُ إلى الخَرجِ في هذا النَصّ: <c>WriteLine</c> و<c>Write</c>، وكُلُّ دالَّةٍ مَحَلِّيَّةٍ أَو خاصَّةٍ يَكتُبُ جِسمُها إلَيه.</summary>
    internal static IReadOnlyCollection<string> Writers(string code)
    {
        var names = new HashSet<string>(StringComparer.Ordinal) { "WriteLine", "Write" };
        foreach (Match m in Regex.Matches(code, @"\b(?:void|Task|async\s+Task)\s+(\w+)\s*\("))
        {
            var sig = Balanced(code, code.IndexOf('(', m.Index), '(', ')');
            var after = m.Index + (code.IndexOf('(', m.Index) - m.Index) + sig.Length;
            var rest = code[after..].TrimStart();
            var body = rest.StartsWith("=>", StringComparison.Ordinal)
                ? rest[..Math.Max(0, rest.IndexOf(';'))]
                : rest.StartsWith('{') ? Balanced(rest, 0, '{', '}') : "";
            if (Regex.IsMatch(body, @"Console\.(?:Error\.)?Write|\.WriteLine\(")) names.Add(m.Groups[1].Value);
        }
        return names;
    }

    /// <summary>كُلُّ نِداءٍ لِكاتِبٍ تَحمِلُ وُسَطاؤُه حَقلاً شَخصِيّاً — والتَصريحُ بِالدالَّةِ نَفسِها لا يُعَدّ.</summary>
    internal static IReadOnlyList<string> Leaks(string code)
    {
        var leaks = new List<string>();
        foreach (var name in Writers(code))
            foreach (Match m in Regex.Matches(code, $@"(?<!\b(?:void|Task)\s+)\b{Regex.Escape(name)}\s*\("))
            {
                var args = Balanced(code, code.IndexOf('(', m.Index), '(', ')');
                if (Regex.IsMatch(args, PersonalField))
                    leaks.Add($"{name}{(args.Length > 90 ? args[..90] + "…" : args)}");
            }
        return leaks;
    }

    internal static (int Files, Dictionary<string, IReadOnlyList<string>> Leaks) Scan()
    {
        var root = Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, "tests");
        var files = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !f.EndsWith(nameof(TestOutputPersonalDataTests) + ".cs", StringComparison.Ordinal))
            .ToList();
        var found = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var f in files)
        {
            var leaks = Leaks(BlankPlainStrings(WriteEndpointGuardTests.StripComments(File.ReadAllText(f))));
            if (leaks.Count > 0) found[Path.GetFileName(f)] = leaks;
        }
        return (files.Count, found);
    }

    // ─── الصَفُّ الدائِم: الماسِحُ يَحمَرُّ عَلى سَطرِ اليَومِ حَرفاً وعَلى أَشكالِه، ولا يَعُدُّ ما لَيسَ حَقلاً شَخصِيّاً ───────────

    [Fact]
    public void The_scanner_goes_red_on_todays_line_and_its_shapes_and_not_on_counts_or_flags()
    {
        // سَطرُ اليَومِ بِدالَّتِه المَحَلِّيَّةِ كَما هُوَ هُناك.
        const string today = """
            void Log(string line)
            {
                var stamped = $"{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ss.fffZ}  {line}";
                evidence.AppendLine(stamped);
                Console.WriteLine(stamped);
            }
            foreach (var a in users.Where(u => u.IsPlatformAdmin))
                Log($"[platform-admin] phone={a.Phone} name={a.FullName}");
            """;
        Assert.Contains("Log", Writers(today));
        Assert.Single(Leaks(today));
        Assert.Single(Leaks("output.WriteLine($\"{user.Email}\");"));
        Assert.Single(Leaks("Console.WriteLine(\"nid \" + u.NationalId);"));
        Assert.Single(Leaks("private void Say(string s) => output.WriteLine(s);\nSay(x.Phone);"));

        // لا يُعَدّ: عَدَدٌ، أَو عَلَمٌ يُسَمّي الحَقلَ ولا يَحمِلُه، أَو حَقلٌ خارِجَ نِداءِ كاتِب.
        Assert.Empty(Leaks("output.WriteLine($\"admins={users.Count(u => u.IsPlatformAdmin)}\");"));
        Assert.Empty(Leaks("output.WriteLine($\"verified={u.PhoneVerified} hasEmail={hasEmail}\");"));
        Assert.Empty(Leaks("var hasPhone = !string.IsNullOrEmpty(a.Phone);\noutput.WriteLine($\"phone={hasPhone}\");"));
        Assert.Empty(Leaks(WriteEndpointGuardTests.StripComments("// output.WriteLine(a.Phone);")));
        Assert.Empty(Leaks("Log($\"[race] النَوع={loser.Conflict?.GetType().FullName} {typeof(Deal).FullName}\");"));
        Assert.Empty(Leaks(BlankPlainStrings("With(\"Console.Error.WriteLine(studioUser.Email);\");")));
        Assert.Single(Leaks(BlankPlainStrings("Console.WriteLine(\"phone \" + u.Phone);")));
        Assert.Single(Leaks("void Log(string s) => Console.WriteLine(s);\nLog($\"[doc] slug={doc.Slug} by={doc.CreatedBy}\");"));
        Assert.Equal("if (c is '\"' or '\\'') x = \"       \";", BlankPlainStrings("if (c is '\"' or '\\'') x = \"a.Phone\";"));
        // عَلامَةُ إغلاقِ المُستَوفاةِ لا تَفتَحُ سِلسِلَةً تَبتَلِعُ النِداءَ الَّذي بَعدَها، ولا سِلسِلَةٌ مُتَداخِلَةٌ في ثُقبِها.
        Assert.Single(Leaks(BlankPlainStrings("Log($\"{a}\"); output.WriteLine(u.Phone); x = \"y\";")));
        Assert.Single(Leaks(BlankPlainStrings("output.WriteLine($\"{(ok ? \"x\" : \"y\")} {u.Phone}\");")));
        Assert.Empty(Leaks(BlankPlainStrings("const string s = \"\"\"\n  output.WriteLine(u.Phone);\n  \"\"\";")));
    }

    // ─── ١. ثَلاثَةُ بَراهينَ حَيَّةٍ تَكتُبُ جَوّالَ المُشرِفِ واسمَه ومُقتَرِحَ الوَثيقَةِ أَو مُعتَمِدَها ─────────────────

    [Fact]
    public void Today_three_live_proofs_write_admin_phones_names_proposers_or_deciders_to_their_output()
    {
        var (files, leaks) = Scan();
        output.WriteLine($"مِلَفّاتٌ مَفحوصَة: {files} · مَواضِعُ الكَشف: " + string.Join(" · ", leaks.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => $"{kv.Key}={kv.Value.Count}")));
        Assert.True(files > 100, $"أَداةٌ عَمياء: {files} مِلَفّاً.");
        Assert.Equal(["LiveApprovalFlowProofTests.cs", "LiveRoleDefinitionProofTests.cs", "LiveThemeProofTests.cs"],
            leaks.Keys.Order(StringComparer.Ordinal).ToArray());
        Assert.All(leaks.Values, l => Assert.Equal(2, l.Count));
    }

    // ─── ٢. بُرهانُ الثيمِ الحَيُّ بِلا Assert ──────────────────────────────────────────────────────────

    internal static string ThemeProof() => WriteEndpointGuardTests.StripComments(
        StudioConsentCharacterizationTests.Source("tests", "ACommerce.Platform.Tests", "LiveThemeProofTests.cs"));

    [Fact]
    public void Today_the_theme_live_proof_asserts_nothing_so_it_is_green_whatever_it_reads()
    {
        var asserts = Regex.Matches(ThemeProof(), @"\bAssert\.\w+\(").Count;
        output.WriteLine($"Assert في LiveThemeProofTests: {asserts}");
        Assert.Equal(0, asserts);
    }
}
