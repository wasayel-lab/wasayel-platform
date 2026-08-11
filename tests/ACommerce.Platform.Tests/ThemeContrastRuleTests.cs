using ACommerce.Kit.Theme;
using Xunit;

namespace ACommerce.Platform.Tests;

// ─── قاعِدَة التَباين — مُوجَب وسالِب، وعَدّاد يَمنَع الأَداةَ العَمياء ───
//
// الدَعوى المَفحوصَة: **مِعيار AA يُفرَض عِندَ البَوّابَة** — لا
// بِتَحرير مِلَفّ حُزمَة يَنسَخُه المُستَأجِر مَرَّةً ثُمَّ يَتَجَمَّد.
//
// وكُلّ اختِبار هُنا يُثَبِّت رَقماً لا انطِباعاً: النِسبَة المَحسوبَة
// تُقارَن بِقيمَة مَكتوبَة، والقيمَة نَفسُها تُقارَن بِمَصدَرَين
// مُستَقِلَّين (‏WCAG 2.x بِاليَد، ثُمَّ المُصادِق).
public class ThemeContrastRuleTests
{
    private static ThemeDefinition Theme(params (string Key, string Value)[] tokens) => new()
    {
        Slug   = "probe",
        Label  = new ThemeLabel("مِسبار"),
        Tokens = tokens.ToDictionary(t => t.Key, t => t.Value, StringComparer.Ordinal)
    };

    private static string[] Codes(IReadOnlyList<ThemeDefinitionViolation> v) =>
        v.Select(x => x.Code).ToArray();

    // ─── ١. الرِياضِيّات نَفسُها ───────────────────────────────────────

    [Theory]
    // قِيَم مَرجِعِيَّة مِن WCAG 2.x — الطَرَفان الحَدّيّان أَوَّلاً.
    [InlineData("#000000", "#ffffff", 21.000)]
    [InlineData("#ffffff", "#ffffff", 1.000)]
    // ثُمَّ القِيَم الَّتي يَعيش عَلَيها هذا المُستودَع.
    [InlineData("#18181b", "#fafafa", 16.974)]
    [InlineData("#6f6f78", "#f4f4f5", 4.526)]
    [InlineData("#71717a", "#f4f4f5", 4.397)]
    [InlineData("#a1a1aa", "#fafafa", 2.455)]
    [InlineData("#96826a", "#241D16", 4.514)]
    public void TheRatioIsWcagRelativeLuminance(string fg, string bg, double expected)
    {
        Assert.True(ThemeContrastRule.TryParse(fg, out var f));
        Assert.True(ThemeContrastRule.TryParse(bg, out var b));
        var ratio = ThemeContrastRule.Ratio(
            ThemeContrastRule.Luminance(f), ThemeContrastRule.Luminance(b));
        Assert.Equal(expected, ratio, 3);
    }

    [Fact]
    public void ATranslucentTextIsCompositedOverItsSurface_NotJudgedRaw()
    {
        // ‏#000000 بِشَفافِيَّة 0x80 فَوقَ أَبيَض = ‏255 − 128 = 127 في كُلّ
        // قَناة، ونِسبَتُه 4.004 لا 21. الفَرق هُوَ الفَرق بَينَ قِياس ما
        // يُرسَم وقِياس ما كُتِبَ — وهُنا يَقلِب الحُكم: ‏21 تَجتاز و4.004
        // تَرسُب.
        Assert.True(ThemeContrastRule.TryParse("#00000080", out var fg));
        Assert.True(ThemeContrastRule.TryParse("#ffffff", out var bg));
        var mixed = ThemeContrastRule.Composite(fg, bg);
        Assert.Equal(127.0, mixed.R, 3);
        Assert.Equal(1.0, mixed.A);

        var ratio = ThemeContrastRule.Ratio(
            ThemeContrastRule.Luminance(mixed), ThemeContrastRule.Luminance(bg));
        Assert.Equal(4.004, ratio, 3);
    }

    [Fact]
    public void ATranslucentSurfaceIsSkipped_NotGuessed()
    {
        // ما خَلفَ السَطح غَير مَعلوم لِهذه الطَبَقَة. الصَواب أَن
        // يُتَخَطّى ويُعَدّ، لا أَن يُحكَم عَلَيه بِرَقم مُختَلَق.
        var r = ThemeContrastRule.Evaluate("probe", k => k switch
        {
            "color.text"    => "#000000",
            "color.bg"      => "rgba(255,255,255,0.5)",
            _               => null
        });
        Assert.Empty(r.Violations);
        Assert.Equal(0, r.Evaluated);
        Assert.Equal(ThemeContrastRule.Pairs.Count, r.Skipped);
    }

    // ─── ٢. السالِب — الرَمز يَظهَر ────────────────────────────────────

    [Fact]
    public void TextBelowAA_OnItsOwnBackground_IsRejectedWithTheCode()
    {
        // ‏#a1a1aa على #fafafa = 2.455 — وهو بِعَينِه الخَرق الَّذي
        // شَحَنَته الجَولَة السابِقَة إلى قاعِدَة البَيانات.
        var d = Theme(("color.text", "#a1a1aa"), ("color.bg", "#fafafa"));
        var codes = Codes(ThemeDefinitionValidator.ValidateTenantDefinition(d));
        Assert.Contains(ThemeContrastRule.ViolationCode, codes);
    }

    [Fact]
    public void ATenantThatDarkensOnlyTheBackground_IsCaughtAgainstInheritedText()
    {
        // **أَخطَر مَدخَل**: الوَثيقَة لا تَذكُر لَونَ نَصّ إطلاقاً —
        // تُغَمِّق السَطح وَحدَه، والنَصّ يَسقُط عَلى الافتِراضيّ. لَو قيسَ
        // المُعلَن وَحدَه لَمَرَّ صامِتاً.
        var d = Theme(("color.surface", "#5a5a5a"));
        var violations = ThemeDefinitionValidator.ValidateTenantDefinition(d);
        Assert.Contains(ThemeContrastRule.ViolationCode, Codes(violations));
        Assert.Contains("color.surface", violations
            .First(v => v.Code == ThemeContrastRule.ViolationCode).MessageAr);
    }

    [Fact]
    public void TheViolationNamesThePairAndTheRatioAndThePlace()
    {
        // رِسالَة تَقول «التَباين ضَعيف» لا تُصلِح شَيئاً. الرِسالَة
        // هُنا تَحمِل: أَيّ رَمزَين، وكَم بَلَغَ، وأَينَ يُرسَم.
        var d = Theme(("color.text", "#a1a1aa"), ("color.bg", "#fafafa"));
        var v = ThemeDefinitionValidator.ValidateTenantDefinition(d)
            .First(x => x.Code == ThemeContrastRule.ViolationCode);

        Assert.Contains("color.text", v.MessageAr);
        Assert.Contains("color.bg", v.MessageAr);
        Assert.Contains("2.455", v.MessageAr);
        Assert.Contains("body", v.MessageAr);
    }

    [Fact]
    public void AMalformedColorGivesTheSyntaxCodeOnce_NotTwice()
    {
        // لَون فاسِد خَرقٌ واحِد اسمُه color_malformed. مُضاعَفَتُه
        // بِـcontrast_below_aa تُغرِق المُراجِع بِرَمزَين لِعِلَّة واحِدَة.
        var d = Theme(("color.text", "crimson"), ("color.bg", "#ffffff"));
        var codes = Codes(ThemeDefinitionValidator.ValidateTenantDefinition(d));
        Assert.Contains("color_malformed", codes);
        Assert.DoesNotContain(ThemeContrastRule.ViolationCode, codes);
    }

    // ─── ٣. المُوجَب — والعَدّاد الَّذي يَمنَع «صِفر مُخالَفَة» الكاذِبَة ──

    [Fact]
    public void TheDefaultTheme_PassesEveryPair_AndEveryPairWasActuallyEvaluated()
    {
        var d = ThemeCatalog.Definition;
        Assert.Empty(ThemeDefinitionValidator.ValidateDefault(d));

        var r = ThemeContrastRule.Evaluate(d.Slug,
            k => d.Tokens.TryGetValue(k, out var v) ? v : null);

        // القاعِدَة ١٠: أَداةٌ تَقول «صِفر» ولا تَقول «مِن كَم» لا
        // تُميَّز عَن أَداةٍ عَمياء.
        Assert.Empty(r.Violations);
        Assert.Equal(ThemeContrastRule.Pairs.Count, r.Evaluated);
        Assert.Equal(0, r.Skipped);
    }

    [Fact]
    public void EveryPreset_PassesEveryPair_AndTheCountIsPrintedNotAssumed()
    {
        Assert.Equal(3, ThemePresetCatalog.Count);

        foreach (var preset in ThemePresetCatalog.All)
        {
            Assert.Empty(ThemeDefinitionValidator.ValidateTenantDefinition(preset.Definition));

            var r = ThemeContrastRule.Evaluate(preset.Slug,
                k => preset.Definition.Tokens.TryGetValue(k, out var v) ? v : null);
            Assert.True(r.Violations.Count == 0,
                $"الحُزمَة «{preset.Slug}»: " +
                string.Join(" | ", r.Violations.Select(x => x.MessageAr)));
            Assert.Equal(ThemeContrastRule.Pairs.Count, r.Evaluated);
        }
    }

    [Fact]
    public void ThePairCatalogIsClosed_AndEveryPairNamesItsSelector()
    {
        // القائِمَة مُشتَقَّة بِالعَدّ مِن أَوراق الأَنماط لا مِن الخَيال —
        // فَكُلّ زَوج يَحمِل مُنتَقِيَه، ولا زَوجَ مُكَرَّراً، ولا مِفتاحَ
        // خارِج مَعجَم الرُموز.
        Assert.Equal(10, ThemeContrastRule.Pairs.Count);

        foreach (var p in ThemeContrastRule.Pairs)
        {
            Assert.True(ThemeTokenCatalog.Contains(p.TextKey), p.TextKey);
            Assert.True(ThemeTokenCatalog.Contains(p.SurfaceKey), p.SurfaceKey);
            Assert.False(string.IsNullOrWhiteSpace(p.EvidenceAr),
                $"{p.TextKey}/{p.SurfaceKey}: زَوج بِلا مُنتَقٍ يُثبِتُه.");
        }

        var keys = ThemeContrastRule.Pairs
            .Select(p => $"{p.TextKey}|{p.SurfaceKey}").ToArray();
        Assert.Equal(keys.Length, keys.Distinct(StringComparer.Ordinal).Count());

        // وما لَيسَ فيها عَمداً — مُثَبَّت لِيَبقى قَراراً لا سَهواً.
        Assert.DoesNotContain("color.textMuted|color.surface2", keys);
        Assert.DoesNotContain("color.textSoft|color.surface2", keys);
    }

    // ─── ٤. البَند ٣ — الطَبَقَتان لا الثَلاث، مُثَبَّتَتان بِالرَقم ────────

    [Fact]
    public void TheDefaultThemeHasTwoTextLayersNotThree()
    {
        // <b>هذا الاختِبار يُثَبِّت قَراراً مَقيساً، لا يَحرُس سُلوكاً.</b>
        //
        // في الثيم الافتِراضيّ ‏color.textMuted ≡ color.textSoft. وهذا
        // لَيسَ سَهواً: سَقف AA فَوقَ ‏color.bgAlt ‏#f4f4f5 هُوَ إضاءَة
        // ‏0.16228، و‏#6f6f78 إضاءَتُه 0.16104 — أَي أَنَّه <b>آخِر</b>
        // رَماديّ يَجتاز. فَلا مَوضِعَ فَوقَه لِطَبَقَة ثالِثَة أَفتَح.
        //
        // وتَغميق الخَلفِيَّة — وهو ما اقتُرِحَ — <b>يُضَيِّق</b> المَدى لا
        // يُوَسِّعُه: ‏(L_bg+.05)/(L_fg+.05) تَنقُص بِنُقصان L_bg. القِياس:
        // ‏#71717a على #fafafa = 4.630، وعلى #f7f7f8 = 4.514، وعلى
        // ‏#f5f5f5 = 4.433 — أَي أَنّ ثَلاث دَرَجات تَكسِر الطَبَقَة.
        //
        // والخِيار الوَحيد لِثَلاث طَبَقات مُتَمايِزَة هُوَ تَغميق
        // <b>الوُسطى</b> إلى ‏#52525b (‏7.032 على bgAlt) — وذلك تَغيير
        // مَحسوس في ‏105 مَواضِع تَقرَأ ‏--ac-text-muted، فَهُوَ قَرار
        // المالِك لا قَرارُنا. إن اتُّخِذَ، هذا السَطر هُوَ ما يَسقُط
        // فَيُذَكِّر بِتَحديث الانحِراف المُعلَن.
        var t = ThemeCatalog.Default;
        Assert.Equal(t["color.textMuted"], t["color.textSoft"]);
        Assert.Equal("#6f6f78", t["color.textMuted"]);

        Assert.True(ThemeContrastRule.TryParse(t["color.textMuted"], out var muted));
        Assert.True(ThemeContrastRule.TryParse(t["color.bgAlt"], out var bgAlt));
        var ceiling = (ThemeContrastRule.Luminance(bgAlt) + 0.05) / 4.5 - 0.05;
        var actual  = ThemeContrastRule.Luminance(muted);

        Assert.True(actual <= ceiling, $"{actual:F5} > {ceiling:F5}");
        // وعَلى بُعد أَقَلّ مِن دَرَجَتَي رَماديّ مِن السَقف — أَي أَنّ
        // «آخِر رَماديّ يَجتاز» دَعوى مَقيسَة لا مَجاز.
        Assert.True(ceiling - actual < 0.005, $"المَسافَة إلى السَقف {ceiling - actual:F5}");
    }

    [Fact]
    public void TheOasisThemeKeepsThreeLayers_WhichIsWhyTheRuleIsNotOverreaching()
    {
        // البُرهان المُضادّ: لَو كانَت القاعِدَة تَقسِر كُلّ ثيم عَلى
        // طَبَقَتَين لَكانَت مُبالِغَة. «أَخضَر الواحَة» يَحمِل ثَلاثاً
        // مُتَمايِزَة وتَجتاز جَميعاً — لِأَنّ وُسطاه أَغمَق أَصلاً.
        var oasis = ThemePresetCatalog.Find("akhdar_alwaha")!.Definition.Tokens;
        Assert.NotEqual(oasis["color.textMuted"], oasis["color.textSoft"]);
        Assert.NotEqual(oasis["color.text"], oasis["color.textMuted"]);

        double R(string fg, string bg)
        {
            ThemeContrastRule.TryParse(fg, out var f);
            ThemeContrastRule.TryParse(bg, out var b);
            return ThemeContrastRule.Ratio(
                ThemeContrastRule.Luminance(f), ThemeContrastRule.Luminance(b));
        }

        var alt = oasis["color.bgAlt"];
        Assert.Equal(14.585, R(oasis["color.text"],      alt), 3);
        Assert.Equal( 5.025, R(oasis["color.textMuted"], alt), 3);
        Assert.Equal( 4.528, R(oasis["color.textSoft"],  alt), 3);
    }
}
