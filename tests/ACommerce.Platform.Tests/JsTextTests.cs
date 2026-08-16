using System.Text.RegularExpressions;
using ACommerce.Templates.Customer.Marketplace.I18n;
using Xunit;

namespace ACommerce.Platform.Tests;

// ─── طَبَقَةُ الهُروب إلى JS — مُوجَبٌ وسالِبٌ لِكُلّ حالَةِ هُروب ──────
//
// نَفس مَنهَج <c>LocaleValidatorTests</c>، ولكِنَّ المِحوَرَ هُنا
// **ثُنائيّ لِكُلّ مَحرَف** لا ثُنائيّ لِكُلّ رَمزِ خَرق:
//
//   · **السالِب** — المَحرَفُ خاماً **يَكسِر** السِياق. ويُبرهَن
//     بِفاحِصٍ يَسأَل ما يَسأَلُه القارِئ (‏`BreaksOut`)، لا بِدَعوى في
//     تَعليق.
//   · **المُوجَب** — المَحرَفُ بَعدَ الهُروب لا يَكسِرُه، والنَصُّ
//     العَرَبيُّ حَولَه لا يَضيع.
//
// ولِماذا الشَكلُ هذا بِالذات: «هَرَبتُ الاقتِباسَ» دَعوى تَمُرّ
// بِاختِبارٍ يُقارِن سِلسِلَةً بِسِلسِلَة **ولَو كانَ الهُروبُ خَطَأً**.
// فَالفاحِصُ يَسأَل: هَل بَقِيَ في المُخرَج مَحرَفٌ يُنهي الحَرفِيَّة أَو
// يُنهي العُنصُر؟
//
// **وكُلُّ مَحرَفٍ خَطِرٍ يُكتَب هُنا بِـ`\uXXXX`** لا خاماً: ‏U+2028
// وU+2029 **فاصِلا سُطورٍ في مَصدَر C# نَفسِه**، فَكِتابَتُهُما خاماً
// تَكسِر المِلَفّ قَبلَ أَن تَختَبِر شَيئاً.

public class JsTextTests
{
    private const char LineSep = '\u2028';
    private const char ParaSep = '\u2029';

    // ── الفاحِص: هَل يَخرُج المُخرَجُ مِن سِياقِه؟ ────────────────────
    //
    // ثَلاثَةُ أَبواب، وهي عَينُ الثَلاثَة في تَوثيق <c>JsText</c>:
    //   ١. اقتِباسٌ (أَو شَرطَةٌ خَلفِيَّة، أَو `${`) غَيرُ مَهروب
    //      يُنهي الحَرفِيَّة.
    //   ٢. مَحرَفُ سَطرٍ يَكسِر الجُملَة — بِما فيه فاصِلا JS وَحدَه.
    //   ٣. `</script` أَو `<!--` يُنهي العُنصُر عِندَ مُحَلِّل HTML.
    private static bool BreaksOut(string emitted)
    {
        // مَحرَفُ السَطر ووَسمُ الخُروج لا يُهرَبان بِتَخَطٍّ، فَيُفحَصان
        // على النَصّ كُلِّه.
        if (emitted.Any(c => c is '\n' or '\r' || c == LineSep || c == ParaSep))
            return true;
        if (emitted.Contains("</script", StringComparison.OrdinalIgnoreCase)
            || emitted.Contains("<!--", StringComparison.Ordinal))
            return true;

        for (var i = 0; i < emitted.Length; i++)
        {
            var c = emitted[i];

            if (c == '\\') { i++; continue; }        // مَهروب — يُتَخَطّى التالي

            if (c is '\'' or '"' or '`') return true;
            if (c == '$' && i + 1 < emitted.Length && emitted[i + 1] == '{') return true;
        }

        // شَرطَةٌ خَلفِيَّةٌ فَردِيَّةٌ في الذَيل تَهرُبُ **الاقتِباسَ
        // المُغلِق** — فَلا تُغلَق الحَرفِيَّةُ أَصلاً. وهذا هو وَجهُ
        // خَطَرِ الشَرطَة، لا أَنَّها تَخرُج بِنَفسِها.
        var trailing = 0;
        for (var i = emitted.Length - 1; i >= 0 && emitted[i] == '\\'; i--) trailing++;
        return trailing % 2 == 1;
    }

    // كُلُّ مَحرَفٍ لَه بابٌ في التَوثيق، وسِياقُه عَرَبيّ لِيَكونَ
    // المِثالُ مِن جِنسِ ما يُرَحَّل فِعلاً.
    public static TheoryData<string, string> DangerousValues => new()
    {
        { "quote_single",  "لا يُمكِن' الآن" },
        { "quote_double",  "لا يُمكِن\" الآن" },
        { "backtick",      "لا يُمكِن` الآن" },
        // الشَرطَةُ في **الذَيل** لا في الوَسَط: هُناكَ تَهرُبُ الاقتِباسَ
        // المُغلِق فَلا تُغلَق الحَرفِيَّة. ومِثالٌ في الوَسَط كانَ
        // سَيَمُرّ سالِبُه كاذِباً — قيسَ، لا ظُنّ.
        { "backslash_tail", "لا يُمكِن\\" },
        { "template_expr", "لا يُمكِن ${x} الآن" },
        { "newline_lf",    "سَطر\nثانٍ" },
        { "newline_cr",    "سَطر\rثانٍ" },
        { "line_separator", "سَطر\u2028ثانٍ" },
        { "para_separator", "سَطر\u2029ثانٍ" },
        { "close_script",  "خُروج</script>هُنا" },
        { "html_comment",  "خُروج<!--هُنا" },
    };

    // ─── السالِب — خاماً يَكسِر، وهذا سَبَبُ وُجودِ الطَبَقَة ─────────
    [Theory]
    [MemberData(nameof(DangerousValues))]
    public void Raw_BreaksOutOfTheContext(string name, string raw)
    {
        Assert.True(BreaksOut(raw),
            $"«{name}»: القيمَةُ الخام لا تَكسِر السِياق — فَالمِثالُ لا يَحرُس شَيئاً.");

        // وهي مَردودَةٌ عِندَ البَوّابَة أَيضاً: البُرهانُ البايتيّ
        // يَسقُط قَبلَ أَن يَسقُطَ الأَمان.
        Assert.False(JsText.IsVerbatim(raw));
    }

    // ─── المُوجَب — بَعدَ الهُروب لا يَكسِر، والمَعروضُ لا يَضيع ──────
    [Theory]
    [MemberData(nameof(DangerousValues))]
    public void Escaped_IsInertAndLosesNothing(string name, string raw)
    {
        var escaped = JsText.Escape(raw);

        Assert.False(BreaksOut(escaped),
            $"«{name}»: المُخرَجُ ما زالَ يَكسِر السِياق — هُروبٌ نِصفيّ.");

        // والحَرفُ العَرَبيّ لا يُمَسّ: الهُروبُ يَحرُس البِنيَة ولا
        // يَبتَلِع النَصّ.
        foreach (var c in raw.Where(c => c >= '؀' && c <= 'ۿ'))
            Assert.Contains(c.ToString(), escaped, StringComparison.Ordinal);
    }

    /// <summary>مَحارِفُ التَحَكُّم لا «تَكسِر» حَرفِيَّةَ JS نَحوِيّاً،
    /// ولِذلك هي **خارِجَ** جَدوَل السالِب أَعلاه — وتُختَبَر وَحدَها.
    /// وتُهرَب رَغم ذلك لِسَبَبَين: تَشويهُ النَصّ المَعروض، وأَنّ
    /// وُجودَها في قيمَةٍ عَلامَةُ عَطَبٍ في المَصدَر لا نَصٌّ
    /// مَقصود.</summary>
    [Theory]
    [InlineData('\u0000')]
    [InlineData('\u0001')]
    [InlineData('\u0008')]
    [InlineData('	')]
    [InlineData('\u001F')]
    [InlineData('\u007F')]
    public void ControlCharacters_AreEscapedToUnicodeForm(char control)
    {
        var raw = "تَحَكُّم" + control + "هُنا";

        Assert.False(JsText.IsVerbatim(raw));
        Assert.Contains($"\\u{(int)control:X4}", JsText.Escape(raw), StringComparison.Ordinal);
        Assert.DoesNotContain(control.ToString(), JsText.Escape(raw), StringComparison.Ordinal);
    }

    // ─── المُوجَب الأَهَمّ: النَصُّ العَرَبيّ يَمُرّ **بايتاً بِبايت** ──
    //
    // هذا هو الشَرطُ الَّذي يُبقي البُرهانَ البايتيَّ على المِئَة
    // والثَمانِ والعِشرين صَفحَة قائِماً: لَو هَرَبَت الطَبَقَةُ شَيئاً
    // في قيمَةٍ عَرَبِيَّةٍ خالِصَة لَتَبَدَّلَت بايتاتُ الصَفحَة بِلا أَن
    // يَتَبَدَّلَ حَرفٌ مِمّا يُرى.
    [Theory]
    [InlineData("تَثبيت التَّطبيق")]
    [InlineData("جارٍ التِقاط مَوقِعكَ…")]
    [InlineData("اِضغَط ⋮ في أَعلى Chrome")]
    [InlineData("أَو افتَح القائِمَة (⋮) واختَر «تَثبيت…»")]
    [InlineData("عَلى iPhone / iPad:")]
    [InlineData("مُتَصَفِّحك لا يَدعَم تَثبيت PWA. جَرِّب Chrome أَو Edge أَو Safari (iOS).")]
    public void ArabicUiText_PassesThroughUnchanged(string value)
    {
        Assert.Equal(value, JsText.Escape(value));
        Assert.True(JsText.IsVerbatim(value));
    }

    [Fact]
    public void EmptyString_IsLeftAlone()
    {
        Assert.Equal("", JsText.Escape(""));
        Assert.True(JsText.IsVerbatim(""));
    }

    // ─── المُصادِق — `value_unsafe_js` بِمُوجَبِه وسالِبِه ────────────

    private static IReadOnlyDictionary<string, IReadOnlyList<LocaleEntry>> Catalog(string json)
        => new Dictionary<string, IReadOnlyList<LocaleEntry>>(StringComparer.Ordinal)
        {
            ["ar"] = LocaleCatalog.ReadEntries(System.Text.Encoding.UTF8.GetBytes(json)),
        };

    [Fact]
    public void ValidateJs_CleanValue_PassesTheGate()
    {
        var c = Catalog("""{ "pwa.dialog.title": "تَثبيت التَّطبيق" }""");
        Assert.Empty(LocaleValidator.ValidateJs(new[] { "pwa.dialog.title" }, c));
    }

    [Fact]
    public void ValidateJs_ValueNeedingEscape_IsRejected()
    {
        var c = Catalog("""{ "pwa.dialog.title": "لا يُمكِن' الآن" }""");
        var v = LocaleValidator.ValidateJs(new[] { "pwa.dialog.title" }, c);

        Assert.Equal(new[] { "value_unsafe_js" }, v.Select(x => x.Code).ToArray());
    }

    /// <summary>المِفتاحُ الَّذي لا يُقرَأ في JS لا يَخضَع لِلشَرط —
    /// وهذا **قِياسٌ لا تَساهُل**: ‏33 مِفتاحاً في المَعجَم تَحمِل سَطراً
    /// جَديداً وسِتَّةٌ تَحمِل <c>"</c>، وكُلُّها سَليمَةٌ في مَوضِعِها
    /// (عُقَدُ نَصّ وخَصائِص). بَوّابَةٌ تَرُدُّها تُعاقِب
    /// الصَواب.</summary>
    [Fact]
    public void ValidateJs_IgnoresKeysThatAreNotReadFromJs()
    {
        var c = Catalog("""{ "admin.agent.help": "سَطر\nثانٍ" }""");
        Assert.Empty(LocaleValidator.ValidateJs(Array.Empty<string>(), c));
    }

    // ─── البَوّابَةُ على المَشحون فِعلاً ──────────────────────────────
    //
    // المَفاتيحُ تُستَخرَج **مِن مَواضِع الاستِدعاء** لا مِن قائِمَةٍ
    // تُكتَب بِاليَد: القائِمَةُ اليَدَوِيَّةُ تَشيخ، ومَوضِعُ الاستِدعاء
    // لا يَشيخ.

    private static readonly Regex JsCallSite =
        new(@"L\.Js\(""(?<key>[a-z][a-z0-9_.]*)""\)", RegexOptions.Compiled);

    private static IReadOnlyList<string> ShippedJsKeys()
    {
        var keys = new SortedSet<string>(StringComparer.Ordinal);
        var root = ThemeZeroEquivalenceTests.RepoRoot;

        foreach (var dir in new[] { "libs", "apps" })
            foreach (var path in Directory.EnumerateFiles(
                         Path.Combine(root, dir), "*.razor", SearchOption.AllDirectories))
            {
                if (path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                        StringComparison.Ordinal) ||
                    path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                        StringComparison.Ordinal))
                    continue;

                foreach (Match m in JsCallSite.Matches(File.ReadAllText(path)))
                    keys.Add(m.Groups["key"].Value);
            }

        return keys.ToList();
    }

    [Fact]
    public void ShippedJsKeys_PassTheGate()
    {
        var keys = ShippedJsKeys();

        // حارِسُ العَمى (القاعِدَة ١٠): بَوّابَةٌ فَحَصَت صِفرَ مِفتاح لا
        // تُميَّز عَن بَوّابَةٍ لا تَعمَل. والرَقمُ **حَدٌّ أَدنى** لا
        // تَثبيت — يَنمو بِالتَرحيل ولا يُحمِر.
        Assert.True(keys.Count >= 23,
            $"‏L.Js وُجِدَت لِـ{keys.Count} مِفتاحاً — أَقَلّ مِن المَقيس (23). " +
            "إمّا أَنّ المَسحَ يَقرَأ الشَجَرَة الخَطَأ، وإمّا أَنّ الاصطِلاحَ تَبَدَّل.");

        // ١. كُلُّ مِفتاحٍ يُقرَأ في JS لَه قيمَةٌ في المَعجَم العَرَبيّ
        //    (وإلّا طُبِعَ المِفتاحُ الخام داخِلَ حَرفِيَّةِ JS).
        var missing = keys.Where(k => LocaleCatalog.Find(LocaleCatalog.Arabic, k) is null)
                          .ToArray();
        Assert.True(missing.Length == 0,
            "مَفاتيحُ JS بِلا قيمَةٍ عَرَبِيَّة: " + string.Join("، ", missing));

        // ٢. ولا واحِدَةٌ مِنها تَحتاج هُروباً — فَيَبقى البُرهانُ
        //    البايتيّ على الـ128 صَفحَة قائِماً.
        Assert.Empty(LocaleValidator.ValidateJs(keys, LocaleCatalog.All));
    }
}
