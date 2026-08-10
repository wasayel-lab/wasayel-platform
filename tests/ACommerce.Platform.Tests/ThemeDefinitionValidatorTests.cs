using ACommerce.Kit.Theme;
using Xunit;

namespace ACommerce.Platform.Tests;

// ─── بَوّابَة تَعريفات الثيم — مُوجَب وسالِب ──────────────────────────
//
// نَفس مَنهَج <c>RoleDefinitionValidatorTests</c>: كُلّ رَمز خَرق لَه
// حالَة تُنتِجُه، ولا حالَة تُنتِج رَمزاً لَم يُقصَد. والفَحص يَمُرّ
// **مِن النَّصّ** حَيثُ أَمكَن (‏ParseDefinition ← Validate) لا مِن
// كائِن يُبنى في الاختِبار ويَتَخَطّى التَسَلسُل.
//
// ولِهذه البَوّابَة عِبء لا تَحمِلُه بَوّابَة الأَدوار: قيمَتُها
// **تُبَثّ داخِل وَسم <style>**. لِذلك السالِب هُنا لَيسَ «قيمَة شاذَّة»
// فَحَسب، بَل مُحاوَلَة خُروج مِن التَصريحَة إلى المُستَند — وهي
// مَفحوصَة صَراحَةً.

public class ThemeDefinitionValidatorTests
{
    /// <summary>تَعريف جُزئيّ صالِح — لَونان ونِصف قُطر. الجُزئيَّة
    /// **مَقصودَة**: ثيم مُستَأجِر لا يَلزَمُه اكتِمال، والباقي يَسقُط
    /// عَلى الافتِراضيّ.</summary>
    public const string GreenJson = """
    {
      "slug": "adwar_green",
      "label": { "ar": "أَخضَر أَدوار", "en": null },
      "tokens": {
        "color.primary": "#14532D",
        "color.primaryHover": "#166534",
        "radius.md": "4px"
      }
    }
    """;

    private static ThemeDefinition Parse(string json) =>
        ThemeDefinitionLoader.ParseDefinition(json);

    private static ThemeDefinition Token(string key, string value) => new()
    {
        Slug   = "probe",
        Label  = new ThemeLabel("مِسبار"),
        Tokens = new Dictionary<string, string>(StringComparer.Ordinal) { [key] = value }
    };

    private static string[] Codes(IReadOnlyList<ThemeDefinitionViolation> v) =>
        v.Select(x => x.Code).ToArray();

    // ─── المُوجَب ─────────────────────────────────────────────────────

    [Fact]
    public void PartialTenantTheme_FromText_PassesTheGate()
    {
        var d = Parse(GreenJson);
        Assert.Empty(ThemeDefinitionValidator.ValidateTenantDefinition(d));
        Assert.True(ThemeDefinitionValidator.IsValidTenantDefinition(d));
        Assert.Equal("adwar_green", d.Slug);
        Assert.Equal(3, d.Tokens.Count);
    }

    [Fact]
    public void TheEmbeddedDefault_IsCompleteAndValid()
    {
        var d = ThemeCatalog.Definition;
        Assert.Empty(ThemeDefinitionValidator.ValidateDefault(d));
        Assert.Equal(ThemeTokenCatalog.Count, d.Tokens.Count);
    }

    [Theory]
    // كُلّ شَكل لَون مَقبول — والثَلاثَة الأَخيرَة هي شَكل الحُدود
    // القائِم فِعلاً في premium.css، ولِذلك بَقِيَ rgb()/rgba() في
    // النَّحو بَدَل قَسر HEX (‏.07×255 لَيسَ عَدَداً صَحيحاً، فَالتَحويل
    // كانَ سَيُغَيِّر القيمَة).
    [InlineData("#fff")]
    [InlineData("#1D4ED8")]
    [InlineData("#11182712")]
    [InlineData("rgb(17,24,39)")]
    [InlineData("rgba(17,24,39,.07)")]
    [InlineData("rgba(17, 24, 39, 0.5)")]
    public void AcceptedColorForms(string value) =>
        Assert.Empty(ThemeDefinitionValidator.Validate(Token("color.primary", value)));

    [Theory]
    [InlineData("0")]
    [InlineData("8px")]
    [InlineData("999px")]
    [InlineData("0.25rem")]
    [InlineData("1.5rem")]
    [InlineData("100%")]
    public void AcceptedLengthForms(string value) =>
        Assert.Empty(ThemeDefinitionValidator.Validate(Token("radius.md", value)));

    // ─── السالِب: المَعجَم ────────────────────────────────────────────

    [Fact]
    public void UnknownTokenKey_IsRejectedByCode_NotByJsonException()
    {
        // مِفتاح مَجهول **داخِل** tokens يُعطي رَمز خَرق يُصَحِّح عَلَيه
        // الوَكيل — لا استِثناء قِراءَة. وهذا بِالضَبط سَبَب اختِيار
        // قامُوس مُسَطَّح + كاتالوج مَفحوص بَدَل أَصناف مُتَداخِلَة.
        var d = Parse("""
        {
          "slug": "probe",
          "label": { "ar": "مِسبار", "en": null },
          "tokens": { "color.chartreuse": "#7FFF00" }
        }
        """);
        Assert.Contains("token_key_out_of_vocabulary",
            Codes(ThemeDefinitionValidator.Validate(d)));
    }

    [Fact]
    public void UnknownTopLevelKey_IsAReadFailure_NotAViolation()
    {
        // عَلى مُستَوى الوَثيقَة الإغلاق يَقَع في القارِئ نَفسِه
        // (‏UnmappedMemberHandling.Disallow) — والفَصل مَقصود: «تَعَذَّرَت
        // القِراءَة» شَيء، و«قُرِئَ وخالَفَ» شَيء آخَر.
        Assert.ThrowsAny<Exception>(() => Parse("""
        { "slug": "probe", "label": { "ar": "م" }, "tokens": {}, "fontFamily": "Comic Sans" }
        """));
    }

    // ─── السالِب: نَحو القِيَم ─────────────────────────────────────────

    [Theory]
    [InlineData("crimson")]        // اسم لَون CSS — لَيسَ HEX ولا rgb()
    [InlineData("#12345")]         // طول شاذّ
    [InlineData("#GGGGGG")]        // خارِج السِتَّ عَشرَة
    [InlineData("var(--x)")]       // إحالَة — بابُ التِفاف
    [InlineData("hsl(120,50%,50%)")]
    public void MalformedColor_IsRejected(string value) =>
        Assert.Contains("color_malformed",
            Codes(ThemeDefinitionValidator.Validate(Token("color.primary", value))));

    [Theory]
    [InlineData("8")]              // بِلا وَحدَة
    [InlineData("8 px")]
    [InlineData("calc(8px + 2px)")]
    [InlineData("-4px")]
    public void MalformedLength_IsRejected(string value) =>
        Assert.Contains("length_malformed",
            Codes(ThemeDefinitionValidator.Validate(Token("radius.md", value))));

    [Theory]
    [InlineData("1.5rem")]         // وَحدَة عَلى عَدَد مُجَرَّد
    [InlineData("normal")]
    public void MalformedNumber_IsRejected(string value) =>
        Assert.Contains("number_malformed",
            Codes(ThemeDefinitionValidator.Validate(Token("lineHeight.base", value))));

    [Theory]
    [InlineData("450")]
    [InlineData("1000")]
    [InlineData("bold")]
    public void WeightOutOfRange_IsRejected(string value) =>
        Assert.Contains("weight_out_of_range",
            Codes(ThemeDefinitionValidator.Validate(Token("fontWeight.bold", value))));

    // ─── السالِب: الخُروج مِن التَصريحَة ───────────────────────────────

    [Theory]
    // كُلّ واحِدَة مِن هذه لَو بُثَّت لَكَتَبَت CSS لِكُلّ زائِر. تُرفَض
    // **قَبل** فَحص النَّحو، فَلا يَعتَمِد الأَمان عَلى دِقَّة تَعبير
    // نَمَطيّ واحِد.
    [InlineData("red;}body{display:none")]
    [InlineData("#fff</style><script>alert(1)</script>")]
    [InlineData("#fff/*")]
    [InlineData("#fff\n--other:x")]
    [InlineData("url('x')\";")]
    public void UnsafeCharacters_AreRejectedBeforeGrammar(string value)
    {
        var codes = Codes(ThemeDefinitionValidator.Validate(Token("color.primary", value)));
        Assert.Contains("value_unsafe_characters", codes);
        // وواحِد فَقَط: الفَحص يَقطَع، فَلا رِسالَتان عَن نَفس القيمَة.
        Assert.DoesNotContain("color_malformed", codes);
    }

    [Fact]
    public void NoUnsafeCharacterSurvivesIntoTheEmittedCss()
    {
        // البُرهان الجامِع: كُلّ قيمَة في الثيم الافتِراضيّ خالِيَة مِن
        // كُلّ مَحرَف يَفتَح بابَ خُروج، فَالكُتلَة المَبثوثَة نَصّ CSS
        // مُغلَق بِالبِناء.
        var css = ThemeCatalog.Default.Css;
        foreach (var c in new[] { '<', '>', '&', '"', '\'', '\\', '@', '\n', '\r' })
            Assert.DoesNotContain(c.ToString(), css, StringComparison.Ordinal);
        Assert.StartsWith(":root{", css, StringComparison.Ordinal);
        Assert.EndsWith("}", css, StringComparison.Ordinal);
    }

    // ─── السالِب: الهُوِيَّة وقاعِدَة عَدَم الظِلّ ──────────────────────

    [Fact]
    public void TenantThemeCannotShadowThePlatformSlug()
    {
        var d = new ThemeDefinition
        {
            Slug   = ThemeCatalog.DefaultSlug,
            Label  = new ThemeLabel("مُنتَحِل"),
            Tokens = new(StringComparer.Ordinal) { ["color.primary"] = "#000000" }
        };

        // مِن بَوّابَة المُستَأجِر: مَرفوض بِرَمزِه الخاصّ.
        Assert.Contains("slug_shadows_platform_catalog",
            Codes(ThemeDefinitionValidator.ValidateTenantDefinition(d)));
        // ومِن البَوّابَة العامَّة: صالِح — والفَصل مَقصود، وإلّا لَرَفَضَ
        // الثيم الافتِراضيّ نَفسَه.
        Assert.Empty(ThemeDefinitionValidator.Validate(d));
    }

    [Theory]
    [InlineData("", "slug_empty")]
    [InlineData("Adwar", "slug_pattern")]
    [InlineData("9green", "slug_pattern")]
    [InlineData("adwar-green", "slug_pattern")]
    public void MalformedSlug_IsRejected(string slug, string expected)
    {
        var d = new ThemeDefinition
        {
            Slug   = slug,
            Label  = new ThemeLabel("مِسبار"),
            Tokens = new(StringComparer.Ordinal) { ["color.primary"] = "#000000" }
        };
        Assert.Contains(expected, Codes(ThemeDefinitionValidator.Validate(d)));
    }

    [Fact]
    public void MissingArabicLabel_IsRejected() =>
        Assert.Contains("localized_arabic_missing", Codes(ThemeDefinitionValidator.Validate(
            new ThemeDefinition
            {
                Slug   = "probe",
                Label  = new ThemeLabel(null, "Probe"),
                Tokens = new(StringComparer.Ordinal) { ["color.primary"] = "#000000" }
            })));

    [Fact]
    public void ThemeWithoutASingleToken_IsRejected() =>
        Assert.Contains("tokens_empty", Codes(ThemeDefinitionValidator.Validate(
            new ThemeDefinition { Slug = "probe", Label = new ThemeLabel("مِسبار") })));

    [Fact]
    public void EmptyTokenValue_IsRejected() =>
        Assert.Contains("token_value_empty",
            Codes(ThemeDefinitionValidator.Validate(Token("color.primary", "  "))));

    [Fact]
    public void IncompleteDefault_IsRejectedOnlyByTheDefaultGate()
    {
        var partial = Parse(GreenJson);
        // جُزئيّ: صالِح لِمُستَأجِر…
        Assert.Empty(ThemeDefinitionValidator.Validate(partial));
        // …ومَرفوض ثيماً افتِراضيّاً، بِرَمز لِكُلّ رَمز ناقِص.
        var codes = Codes(ThemeDefinitionValidator.ValidateDefault(partial));
        Assert.Equal(ThemeTokenCatalog.Count - 3,
            codes.Count(c => c == "default_theme_incomplete"));
    }
}
