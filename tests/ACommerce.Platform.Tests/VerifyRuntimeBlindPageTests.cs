using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ الطَبَقَةُ ٦ لا تُمَرِّرُ صَفحَةً فَحَصَتها صِفراً (‏2026-10-25، تَدقيقُ الإطلاق lens-checkers-ci-9) ═══════════════════════════
//
// **المَقيسُ قَبلَ** (عِندَ dace7cd): `scripts/verify-runtime.cs` يَطبَعُ لِكُلِّ صَفحَةٍ «عَناصِر: N · تَأكيدات: M» ويَضبِطُ `exitCode`
// عَلى المُخالَفاتِ وَحدَها — فَلا فَرعَ يُفشِلُه عَلى `Checks == 0` أَو `Elements == 0`، وصَفحَةٌ لَم تَستَقِرَّ أَو رَمى سِكرِبتُها تُعَدُّ
// مُخالَفَةً يَحجُبُها `--report-only`. أَي «صِفرٌ مِن صِفر» الَّذي تُحَذِّرُ مِنه `docs/VERIFICATION-RUNTIME.md` يَخرُجُ ٠.
//
// **وصارَ**: دالَّةٌ خالِصَةٌ واحِدَة (`BlindPages`) تُقاسُ بِعَيِّناتٍ مَعلومَةِ الجَوابِ قَبلَ أَن يُبحَثَ عَن Chrome — فَتَعمَلُ هُنا وفي
// كُلِّ تَشغيلٍ بِلا مُتَصَفِّح — ثُمَّ يَسأَلُها الحُكمُ بَعدَ حَلقَةِ الصَفَحات: صَفحَةٌ عَمياءُ رَمزُ ٢ لا يَحجُبُه `--report-only`.
// **والسكربتُ لا يُشَغَّلُ هُنا** (يَلزَمُه Chromium، والاختِبارُ لا يَبني مِلَفّاً مُفرَداً)، فَهذا المِلَفُّ يَحرُسُ **مَواضِعَ** الدالَّةِ
// الأَربَعَة في نَصِّ الكود؛ وعَمَلُها نَفسُه يُقاسُ بِاختِبارِها الذاتيِّ في كُلِّ تَشغيل، وطَفرَتاها في رِسالَةِ الكوميت.

public class VerifyRuntimeBlindPageTests(ITestOutputHelper output)
{
    private static string ScriptPath => Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, "scripts", "verify-runtime.cs");

    /// <summary>الكودُ بِلا أَسطُرِ التَعليقِ الكامِلَة — الرَأسُ يَذكُرُ <c>BlindPages</c> و<c>--report-only</c> لِيَشرَحَهُما لا لِيَكونَهُما.</summary>
    private static string Code()
    {
        Assert.True(File.Exists(ScriptPath), $"مَصدَرٌ مَفقود: {ScriptPath}");
        var text = File.ReadAllText(ScriptPath).Replace("\r\n", "\n", StringComparison.Ordinal);
        Assert.True(text.Length > 20_000, $"أَداةٌ عَمياء: verify-runtime.cs طولُه {text.Length} — لَم يُقرَأ.");
        return string.Join('\n', text.Split('\n').Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal)));
    }

    /// <summary>
    /// خُروقُ الشَكلِ بِرُموزٍ ثابِتَة (القاعِدَة ٤):
    /// <c>no_blind_function</c> الدالَّةُ لا تَسأَلُ الصِفرَينِ وغَيرَ المَقيسَةِ مَعاً ·
    /// <c>self_test_after_chrome</c> الاختِبارُ الذاتيُّ غائِبٌ أَو بَعدَ البَحثِ عَن Chrome ·
    /// <c>verdict_ignores_blind</c> الحُكمُ بَعدَ الحَلقَةِ لا يَسأَلُها أَو يَحجُبُه <c>--report-only</c> ·
    /// <c>unmeasured_not_recorded</c> فَرعا «لَم تَستَقِرّ» و«عَطَبُ السِكرِبت» لا يُسَجِّلانِ الصَفحَة.
    /// </summary>
    private static List<string> Breaches(string code)
    {
        var breaches = new List<string>();

        const string Fn = "static List<string> BlindPages(IEnumerable<PageReport> measured, IEnumerable<string> unmeasured) =>";
        var fn = code.IndexOf(Fn, StringComparison.Ordinal);
        var body = fn < 0 ? "" : code[fn..Math.Min(code.Length, fn + Fn.Length + 200)];
        if (fn < 0 || !body.Contains("r.Checks == 0", StringComparison.Ordinal)
                   || !body.Contains("r.Elements == 0", StringComparison.Ordinal)
                   || !body.Contains(".Concat(unmeasured)", StringComparison.Ordinal))
            breaches.Add("no_blind_function");

        var probe = code.IndexOf("var probe = BlindPages(", StringComparison.Ordinal);
        var chrome = code.IndexOf("string? chrome =", StringComparison.Ordinal);
        var refuse = probe < 0 ? -1 : code.IndexOf("return 2;", probe, StringComparison.Ordinal);
        if (probe < 0 || chrome < 0 || probe > chrome || refuse < 0 || refuse > chrome)
            breaches.Add("self_test_after_chrome");

        var verdict = code.IndexOf("var blind = BlindPages(allReports, unmeasured);", StringComparison.Ordinal);
        var exit = code.IndexOf("exitCode = blind.Count > 0 ? 2 : (total > 0 && !reportOnly) ? 1 : 0;", StringComparison.Ordinal);
        if (verdict < 0 || exit < 0 || exit < verdict)
            breaches.Add("verdict_ignores_blind");

        var recorded = code.Split("unmeasured.Add(url);").Length - 1;
        if (recorded < 2) breaches.Add("unmeasured_not_recorded");

        return breaches;
    }

    /// <summary><b>السكربتُ الحَقيقيُّ بِلا خَرق</b>: الدالَّةُ بِصِفرَيها، والاختِبارُ الذاتيُّ قَبلَ Chrome، والحُكمُ يَسأَلُها قَبلَ
    /// <c>--report-only</c>، والفَرعانِ المُبكِرانِ يُسَجِّلانِ الصَفحَة — ويُطبَعُ ما عُدّ.</summary>
    [Fact]
    public void The_layer_six_script_fails_a_page_it_checked_zero_times()
    {
        var code = Code();
        var breaches = Breaches(code);
        var recorded = code.Split("unmeasured.Add(url);").Length - 1;
        output.WriteLine($"verify-runtime.cs: {code.Length} حَرفاً مَقروءاً · فَرعا عَدَمِ القِياسِ المُسَجِّلان {recorded} · خُروق {breaches.Count}");
        Assert.True(breaches.Count == 0, string.Join(", ", breaches));
    }

    /// <summary><b>حَقنُ عَيب — الفاحِصُ أَعلاه يُحمِرُّ فِعلاً</b>: كُلُّ طَفرَةٍ تُعيدُ شَكلاً مِن «قَبلَ» بِرَمزِها، وحدَها.</summary>
    [Fact]
    public void Every_blind_page_breach_goes_red_on_its_injected_sample_and_only_there()
    {
        var code = Code();
        Assert.Empty(Breaches(code));

        static string Once(string text, string from, string to)
        {
            var at = text.IndexOf(from, StringComparison.Ordinal);
            Assert.True(at >= 0, $"عَيِّنَةُ الحَقنِ غائِبَة: {from}");
            return text[..at] + to + text[(at + from.Length)..];
        }

        var cases = new (string Name, string Text, string Code)[]
        {
            ("الصِفرُ في التَأكيداتِ لا يُسأَل",
                Once(code, "r.Checks == 0 || r.Elements == 0", "r.Elements == 0"), "no_blind_function"),
            ("غَيرُ المَقيسَةِ لا تُضَمّ",
                Once(code, ".Concat(unmeasured)", ""), "no_blind_function"),
            ("لا اختِبارَ ذاتيّ",
                Once(code, "var probe = BlindPages(", "var probe = new List<string>(); _ = ("), "self_test_after_chrome"),
            ("الحُكمُ كَما كانَ قَبلَ اليَوم",
                Once(code, "exitCode = blind.Count > 0 ? 2 : (total > 0 && !reportOnly) ? 1 : 0;", "exitCode = (total > 0 && !reportOnly) ? 1 : 0;"),
                "verdict_ignores_blind"),
            ("--report-only يَحجُبُ العَمى",
                Once(code, "exitCode = blind.Count > 0 ? 2 : (total > 0 && !reportOnly) ? 1 : 0;", "exitCode = (blind.Count > 0 || total > 0) && !reportOnly ? 1 : 0;"),
                "verdict_ignores_blind"),
            ("صَفحَةٌ لَم تَستَقِرَّ لا تُسَجَّل",
                Once(code, "unmeasured.Add(url);", ""), "unmeasured_not_recorded"),
        };

        var missed = new List<string>();
        foreach (var c in cases)
        {
            var got = Breaches(c.Text);
            output.WriteLine($"«{c.Name}» → {string.Join(", ", got)}");
            if (got.Count != 1 || got[0] != c.Code) missed.Add($"«{c.Name}»: المُنتَظَرُ {c.Code} وَحدَه، والمَقروء ({string.Join(", ", got)})");
        }
        output.WriteLine($"حُقِنَ {cases.Length} شَكلاً؛ فاتَ الفاحِصَ {missed.Count}");
        Assert.True(missed.Count == 0, string.Join("\n", missed));
    }
}
