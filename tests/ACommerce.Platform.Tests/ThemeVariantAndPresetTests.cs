using ACommerce.Kit.Theme;
using Xunit;

namespace ACommerce.Platform.Tests;

// ─── المُتَغايِرات والحُزَم — مُوجَب وسالِب ─────────────────────────────
//
// ثَلاث دَعاوى تُفحَص هُنا، ولِكُلٍّ مِنها ثَمَن لَو سَقَطَت:
//
//   ١. **الافتِراضيّ لا يُنتِج بايتاً جَديداً.** لَيسَ «يَبدو كَما كان»
//      بَل: مُعَدِّل القيمَة الافتِراضيَّة فارِغ، فَلاحِقَة الصَنف
//      فارِغَة، فَالوَسم هُوَ هُوَ. والرِباط بَينَ الكاتالوج وبَينَ
//      اللَقطَة المُوصَّفَة في الكوميت السابِق مَفحوص صَراحَةً — فَلا
//      يَنجَرِف صَنف أَساس عَن الصَفحَة الَّتي يُصَيَّر فيها.
//
//   ٢. **الحُزَم الثَلاث تُصادَق عِندَ الإقلاع.** تُقرَأ مِن المَوارِد
//      المَضمونَة وتُصادَق بِبَوّابَة المُستَأجِر — وهي بِالضَبط ما
//      تَصيرُه عِندَ التَطبيق. حُزمَة فاسِدَة تَرمي هُنا وفي تَسجيل
//      الخِدمات، لا عِندَ أَوَّل نَقرَة «تَطبيق».
//
//   ٣. **الحُزَم مُتَمايِزَة قِياساً لا ذَوقاً.** «يَراها غَير المُصَمِّم
//      مِن ثَلاثَة أَمتار» جُملَة لا تُفحَص؛ أَمّا «لا تَشتَرِك حُزمَتان
//      في لَون خَلفِيَّة ولا في نَصّ ولا في قيمَة مُتَغايِر واحِدَة»
//      فَتُفحَص. الثانِيَة أَضعَف مِن الأُولى وأَصدَق مِنها.

public class ThemeVariantAndPresetTests
{
    // ─── ١. المَعجَم والتَكافُؤ الصِفريّ ───────────────────────────────

    [Fact]
    public void EveryDefaultValue_HasAnEmptyModifier_WhichIsWhyNothingMoves()
    {
        // هذا هو **مَوضِع** التَكافُؤ الصِفريّ لِلمُتَغايِرات كُلِّه.
        // لَو حَمَلَ افتِراضيٌّ واحِد صَنفاً خاصّاً بِه لَتَغَيَّرَ وَسم
        // كُلّ صَفحَة في المَنصَّة يَومَ دُخول المَوجَة.
        foreach (var slot in ThemeVariantCatalog.All)
        {
            Assert.True(slot.Contains(slot.DefaultValue),
                $"الفَتحَة «{slot.Key}» تُعلِن افتِراضيّاً «{slot.DefaultValue}» ليسَ في قِيَمِها.");
            Assert.Equal("", slot.Default.CssModifier);
            Assert.Equal("", slot.Default.ClassSuffix);
        }
    }

    [Fact]
    public void TheCatalogBaseClasses_AreExactlyTheOnesCharacterizedBeforeTheChange()
    {
        // الرِباط بَينَ المَعجَم واللَقطَة. بِدونِه يُمكِن أَن يُعاد
        // تَسمِيَة صَنف أَساس في الكاتالوج فَيَبقى التَوصيف أَخضَرَ
        // (يَقرَأ لَقطَةً ساكِنَة) والصَفحَة تُصَيَّر صَنفاً آخَر.
        Assert.Equal(ComponentVariantCharacterizationTests.PortalRoleCardsBaseClass,
            ThemeVariantCatalog.Find(ThemeVariantCatalog.PortalRoleCards)!.BaseClass);
        Assert.Equal(ComponentVariantCharacterizationTests.ListingCardBaseClass,
            ThemeVariantCatalog.Find(ThemeVariantCatalog.ListingCard)!.BaseClass);
        Assert.Equal(ComponentVariantCharacterizationTests.HeaderBarBaseClass,
            ThemeVariantCatalog.Find(ThemeVariantCatalog.HeaderBar)!.BaseClass);
    }

    [Fact]
    public void EveryNonDefaultModifier_IsItsBaseClassPlusTwoDashes_AndIsUnique()
    {
        // انضِباط تَسمِيَة يُقرَأ مِن الصَفحَة: مَن رَأى
        // `ac-space--compact` عَرَفَ أَيّ مُكَوِّن وأَيّ قيمَة بِلا رُجوع
        // إلى الشيفرَة. والفَرادَة شَرط: مُعَدِّلانِ مُتَطابِقان في
        // فَتحَتَين يَجعَلانِ وَرَقَة الأَنماط تُصيب المُكَوِّنَين مَعاً.
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var slot in ThemeVariantCatalog.All)
        foreach (var v in slot.Values.Where(v => v.CssModifier.Length > 0))
        {
            Assert.StartsWith(slot.BaseClass + "--", v.CssModifier, StringComparison.Ordinal);
            Assert.Equal(slot.BaseClass + "--" + v.Value, v.CssModifier);
            Assert.True(seen.Add(v.CssModifier), $"مُعَدِّل مُكَرَّر: {v.CssModifier}");
        }
    }

    [Fact]
    public void TheClassSuffix_CarriesItsOwnSeparator_SoTheMarkupPastesItRaw()
    {
        // الوَسم يَكتُب class="ac-space@(suffix) …" بِلا مَسافَة — فَعَلى
        // اللاحِقَة أَن تَحمِل مَسافَتَها. هذا هو الشَرط الَّذي يُبقي
        // class="ac-space " كَما كان بِالضَبط.
        foreach (var slot in ThemeVariantCatalog.All)
        foreach (var v in slot.Values)
            Assert.Equal(v.CssModifier.Length == 0 ? "" : " " + v.CssModifier, v.ClassSuffix);
    }

    [Fact]
    public void TheEmbeddedDefault_DeclaresEverySlot_AndAllOfThemResolveToNoClass()
    {
        var d = ThemeCatalog.Definition;
        Assert.Empty(ThemeDefinitionValidator.ValidateDefault(d));
        Assert.Equal(ThemeVariantCatalog.Count, d.Variants.Count);

        foreach (var slot in ThemeVariantCatalog.All)
        {
            Assert.Equal(slot.DefaultValue, ThemeCatalog.Default.VariantValue(slot.Key));
            Assert.Equal("", ThemeCatalog.Default.VariantClassSuffix(slot.Key));
        }
    }

    [Fact]
    public void TheEmittedCssBlock_StillCarriesTokensOnly_NotOneVariant()
    {
        // المُتَغايِر صَنف في الوَسم لا مُتَغَيِّر في :root. لَو تَسَرَّبَ
        // إلى الكُتلَة لَزادَ عَدَد التَصريحات ولَانكَسَرَت مُقارَنَة
        // بايتِيَّة قائِمَة — وهذا الفَحص يُمسِكُه بِالعَدّ.
        var css = ThemeCatalog.Default.Css;
        Assert.Equal(ThemeTokenCatalog.Count, css.Count(c => c == ';'));
        foreach (var slot in ThemeVariantCatalog.All)
        {
            Assert.DoesNotContain(slot.Key, css, StringComparison.Ordinal);
            Assert.DoesNotContain(slot.BaseClass, css, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void AnOverlayThatRepeatsTheDefaultVariants_IsNoOverlayAtAll()
    {
        var same = new ThemeDefinition
        {
            Slug     = "probe_same",
            Label    = new ThemeLabel("نَفسُه"),
            Variants = ThemeVariantCatalog.All.ToDictionary(
                s => s.Key, s => s.DefaultValue, StringComparer.Ordinal)
        };
        Assert.Same(ThemeCatalog.Default, EffectiveTheme.Compose(ThemeCatalog.Default, same));
    }

    [Fact]
    public void AVariantOnlyOverlay_ChangesTheClassButNotOneByteOfTheRootBlock()
    {
        // الفَصل الَّذي يَجعَل المُتَغايِر رَخيصاً: شَكل يَتَبَدَّل
        // ولَون لا يُلمَس، ونَصّ :root **هُوَ هُوَ بِالمَرجِع** لا
        // بِالمُقارَنَة.
        var shapeOnly = new ThemeDefinition
        {
            Slug     = "probe_shape",
            Label    = new ThemeLabel("شَكل فَقَط"),
            Variants = new(StringComparer.Ordinal)
            {
                [ThemeVariantCatalog.ListingCard] = "compact"
            }
        };

        var composed = EffectiveTheme.Compose(ThemeCatalog.Default, shapeOnly);

        Assert.NotSame(ThemeCatalog.Default, composed);
        Assert.Same(ThemeCatalog.Default.Css, composed.Css);
        Assert.Equal(" ac-space--compact",
            composed.VariantClassSuffix(ThemeVariantCatalog.ListingCard));
        // وما لَم يُعلَن سَقَطَ عَلى الافتِراضيّ حَرفاً.
        Assert.Equal("", composed.VariantClassSuffix(ThemeVariantCatalog.HeaderBar));
        Assert.Equal("", composed.VariantClassSuffix(ThemeVariantCatalog.PortalRoleCards));
    }

    [Fact]
    public void AnUnknownSlotOrValue_IsIgnoredWhenComposing_NotFatal()
    {
        // الطَبَقَة الثالِثَة مِن الدِفاع (البَوّابَة عِندَ الكِتابَة
        // والقِراءَة، وهذه ثالِثَة) — نَفس عَقد الرُموز.
        var junk = new ThemeDefinition
        {
            Slug     = "probe_junk",
            Label    = new ThemeLabel("شاذّ"),
            Variants = new(StringComparer.Ordinal)
            {
                ["no.such.slot"]                  = "grid",
                [ThemeVariantCatalog.HeaderBar]   = "no_such_value",
            }
        };

        var composed = EffectiveTheme.Compose(ThemeCatalog.Default, junk);
        Assert.Same(ThemeCatalog.Default, composed);
    }

    // ─── ٢. البَوّابَة: سالِب المُتَغايِرات ────────────────────────────

    private static string[] Codes(IReadOnlyList<ThemeDefinitionViolation> v) =>
        v.Select(x => x.Code).ToArray();

    private static ThemeDefinition Variant(string key, string value) => new()
    {
        Slug     = "probe",
        Label    = new ThemeLabel("مِسبار"),
        Variants = new Dictionary<string, string>(StringComparer.Ordinal) { [key] = value }
    };

    [Fact]
    public void SlotOutsideTheVocabulary_IsRejectedByItsOwnCode() =>
        Assert.Contains("variant_slot_out_of_vocabulary",
            Codes(ThemeDefinitionValidator.Validate(Variant("portal.roleCard", "grid"))));

    [Theory]
    [InlineData("")]                                   // فارِغَة
    [InlineData("Grid")]                               // حالَة أَحرُف مُختَلِفَة
    [InlineData("masonry")]                            // شَكل لَم يُنَفَّذ
    [InlineData("grid;}body{display:none")]            // مُحاوَلَة خُروج
    [InlineData("grid list")]                          // قيمَتان في واحِدَة
    public void ValueOutsideItsSlotList_IsRejectedByItsOwnCode(string value) =>
        Assert.Contains("variant_value_out_of_vocabulary",
            Codes(ThemeDefinitionValidator.Validate(
                Variant(ThemeVariantCatalog.PortalRoleCards, value))));

    [Fact]
    public void AValueFromAnotherSlot_IsStillOutOfVocabulary() =>
        // ‏showcase قيمَة مَشروعَة — لِبِطاقَة الإعلان لا لِبِطاقَة الدَور.
        // المَعجَم مُغلَق **لِكُلّ فَتحَة عَلى حِدَة**، لا لِلمَجموع.
        Assert.Contains("variant_value_out_of_vocabulary",
            Codes(ThemeDefinitionValidator.Validate(
                Variant(ThemeVariantCatalog.PortalRoleCards, "showcase"))));

    [Fact]
    public void AThemeThatChangesShapeOnly_PassesTheGate()
    {
        // الشَرط القَديم «بِلا رَمز = مَرفوض» كانَ سَيَرُدّ هُوِيَّةً
        // تُبَدِّل الشَكل ولا تَمَسّ لَوناً — وهي مَشروعَة.
        var d = ThemeDefinitionLoader.ParseDefinition("""
        {
          "slug": "shape_only",
          "label": { "ar": "شَكل فَقَط" },
          "variants": { "listing.card": "compact" }
        }
        """);
        Assert.Empty(ThemeDefinitionValidator.ValidateTenantDefinition(d));
    }

    [Fact]
    public void ADefaultMissingASlot_IsRejectedOnlyByTheDefaultGate()
    {
        var partial = ThemeDefinitionLoader.ParseDefinition("""
        {
          "slug": "partial",
          "label": { "ar": "جُزئيّ" },
          "tokens": { "color.primary": "#000000" },
          "variants": { "listing.card": "compact" }
        }
        """);

        Assert.Empty(ThemeDefinitionValidator.Validate(partial));

        var codes = Codes(ThemeDefinitionValidator.ValidateDefault(partial));
        Assert.Equal(ThemeVariantCatalog.Count - 1,
            codes.Count(c => c == "default_theme_variants_incomplete"));
    }

    // ─── ٣. الحُزَم الجاهِزَة ──────────────────────────────────────────

    /// <summary>أَسماء الحُزَم الثَلاث كَما تُعتَمَد. مَكتوبَة هُنا كَي
    /// تَفشَل الحَقيبَة إن حُذِفَت واحِدَة أَو أُعيدَت تَسمِيَتُها — سَطح
    /// الإدارَة والبُرهان الحَيّ كِلاهُما يُرسِل هذه الأَسماء.</summary>
    public static readonly string[] ExpectedPresets =
        { "akhdar_alwaha", "azraq_iftiradi", "layl_ramliy" };

    [Fact]
    public void TheThreePresets_LoadAndValidate_AtTheMomentOfFirstTouch()
    {
        // هذا هو نَفس النِداء الَّذي يُنفَّذ عِندَ تَسجيل الخِدمات —
        // فَخَضرَتُه هُنا هي بِعَينِها «تُصادَق عِندَ الإقلاع».
        Assert.Equal(3, ThemePresetCatalog.Preload());
        Assert.Equal(ExpectedPresets, ThemePresetCatalog.All.Select(p => p.Slug).ToArray());

        foreach (var p in ThemePresetCatalog.All)
        {
            Assert.Empty(ThemeDefinitionValidator.ValidateTenantDefinition(p.Definition));
            Assert.False(string.IsNullOrWhiteSpace(p.Label.Ar));
            Assert.Equal(p.Slug, p.Definition.Slug);
        }
    }

    [Fact]
    public void EveryPresetSetsEverySlot_SoSwitchingNeverLeavesHalfAnIdentity()
    {
        // حُزمَة تُعلِن فَتحَتَين مِن ثَلاث تُبقي الثالِثَة عَلى ما
        // خَلَّفَته الحُزمَة السابِقَة — أَي هُوِيَّة رابِعَة لَم
        // يُنَسِّقها أَحَد. تُمنَع بِالعَدّ لا بِالمُراجَعَة.
        foreach (var p in ThemePresetCatalog.All)
        {
            Assert.Equal(ThemeVariantCatalog.Count, p.Definition.Variants.Count);
            foreach (var slot in ThemeVariantCatalog.All)
                Assert.True(p.Definition.Variants.ContainsKey(slot.Key),
                    $"الحُزمَة «{p.Slug}» لا تُعلِن الفَتحَة «{slot.Key}».");
        }
    }

    [Fact]
    public void TheDefaultPreset_ComposesToTheVerySameObject_NotAnEqualOne()
    {
        // «الأَزرَق الافتِراضيّ» ادِّعاؤُه أَنَّه الشَكل الحاليّ حَرفاً.
        // والبُرهان بِالهُوِيَّة لا بِالمُقارَنَة: تَطبيقُه يُرجِع كائِن
        // الثيم الافتِراضيّ **نَفسَه**، فَنَصّ :root نَفس السِلسِلَة
        // ولاحِقات الأَصناف كُلُّها فارِغَة.
        //
        // وهذا أَيضاً ما يُمسِك انحِراف المِلَفَّين: لَو غُيِّرَت قيمَة في
        // default.theme.json ولَم تُغَيَّر في الحُزمَة (أَو العَكس)،
        // سَقَطَ Assert.Same هُنا.
        var preset = ThemePresetCatalog.Find("azraq_iftiradi");
        Assert.NotNull(preset);
        Assert.Same(ThemeCatalog.Default,
            EffectiveTheme.Compose(ThemeCatalog.Default, preset!.Definition));
    }

    [Fact]
    public void ThePresetJsonIsTheVerbatimFile_NotAReserialization()
    {
        // التَطبيق نَسخ: هذه البايتات هي ما يُخَزَّن في وَثيقَة
        // المُستَأجِر. فَتُقرَأ بِنَفس الدالَّة الَّتي يَقرَأ بِها الخادِم
        // الوَثيقَة — لَو اختَلَفَ الشَكلانِ لَظَهَرَ هُنا.
        foreach (var p in ThemePresetCatalog.All)
        {
            var reparsed = ThemeDefinitionLoader.ParseDefinition(p.Json);
            Assert.Equal(p.Slug, reparsed.Slug);
            Assert.Equal(p.Definition.Tokens.Count,   reparsed.Tokens.Count);
            Assert.Equal(p.Definition.Variants.Count, reparsed.Variants.Count);
            // ونَصّ المِلَفّ يَحمِل تَنسيقَه الأَصليّ — سُطوراً لا سَطراً.
            Assert.Contains('\n', p.Json);
        }
    }

    [Fact]
    public void NoTwoPresets_ShareABackgroundOrATextColourOrASingleVariantValue()
    {
        // صِياغَة قابِلَة لِلفَحص لِـ«يَراها غَير المُصَمِّم مِن ثَلاثَة
        // أَمتار»: الخَلفِيَّة والنَصّ يَملَآن الشاشَة، والمُتَغايِرات
        // تُغَيِّر البِنيَة — فَاشتِراك حُزمَتَين في أَيٍّ مِنها يَعني
        // شاشَتَين تَتَشابَهان في أَبرَز ما فيهِما.
        var presets = ThemePresetCatalog.All;

        for (var i = 0; i < presets.Count; i++)
        for (var j = i + 1; j < presets.Count; j++)
        {
            var (a, b) = (presets[i], presets[j]);

            foreach (var key in new[] { "color.bg", "color.text", "color.primary" })
                Assert.False(
                    string.Equals(a.Definition.Tokens[key], b.Definition.Tokens[key],
                        StringComparison.OrdinalIgnoreCase),
                    $"«{a.Slug}» و«{b.Slug}» تَتَشارَكان «{key}».");

            foreach (var slot in ThemeVariantCatalog.All)
                Assert.False(
                    string.Equals(a.Definition.Variants[slot.Key],
                                  b.Definition.Variants[slot.Key], StringComparison.Ordinal),
                    $"«{a.Slug}» و«{b.Slug}» تَتَشارَكان قيمَة «{slot.Key}».");
        }
    }

    [Fact]
    public void EachPresetIsCompleteEnoughToStandAlone_NotAPatchOnItsPredecessor()
    {
        // وَثيقَة واحِدَة تُبَثّ في كُلّ لَحظَة (لا تَراكُم)، فَالحُزمَة
        // الَّتي تُعلِن رُبع اللَوحَة تَترُك ثَلاثَة أَرباعِها عَلى
        // الأَزرَق الافتِراضيّ وتَبدو نِصفَ هُوِيَّة. الشَرط: كُلّ رَمز
        // لَون في المَعجَم مُعلَن في كُلّ حُزمَة.
        var colourKeys = ThemeTokenCatalog.All
            .Where(t => t.Kind == ThemeTokenKind.Color)
            .Select(t => t.Key)
            .ToArray();

        foreach (var p in ThemePresetCatalog.All)
        foreach (var key in colourKeys)
            Assert.True(p.Definition.Tokens.ContainsKey(key),
                $"الحُزمَة «{p.Slug}» لا تُعلِن اللَون «{key}».");
    }

    [Fact]
    public void PresetsAreOrderedBySlug_SoTheAdminSurfaceNeverShuffles()
    {
        var slugs = ThemePresetCatalog.All.Select(p => p.Slug).ToArray();
        Assert.Equal(slugs.OrderBy(s => s, StringComparer.Ordinal).ToArray(), slugs);
    }

    [Fact]
    public void AnUnknownPresetName_IsNotFound_SoTheApplyPathHasNothingToWrite()
    {
        Assert.Null(ThemePresetCatalog.Find("azraq"));
        Assert.Null(ThemePresetCatalog.Find("default"));
        Assert.False(ThemePresetCatalog.Contains(""));
    }
}
