using System.Text.RegularExpressions;
using ACommerce.Kit.Theme;
using Xunit;

namespace ACommerce.Platform.Tests;

// ─── تَوصيف التَّكافُؤ الصِفريّ لِلمَظهَر ───────────────────────────────
//
// الدَعوى المَفحوصَة، مُصاغَةً بِدِقَّة:
//
//   قيمَة كُلّ رَمز يَبُثُّه الثيم الافتِراضيّ  ≡  الحَرفِيَّة الَّتي
//   كانَت مَكتوبَة في CSS مَكانَه **قَبل** التَّحويل
//
// و**لا تُقارَن بِجَدوَل كَتَبتُه بِيَدي**. لَو كَتَبتُ الجَدوَل لَكانَ
// الاختِبار يُثبِت أَنّ الرُموز تُطابِق ما أَعتَقِدُه عَن CSS، وهو
// بِالضَبط ما يُخطِئ فيه المَرء. بَدَلَ ذلك يُقرَأ **مِلَفّ CSS
// الحَقيقيّ كَما كانَ**، مِن لَقطَة الأَساس المُودَعَة في كوميت سابِق
// مُستَقِلّ (‏tests/characterization/appearance/baseline/css/‎)، ويُحسَب
// مِنه **التَصريحَة الغالِبَة** لِكُلّ مُتَغَيِّر بِتَرتيب تَحميل
// الأَوراق — ثُمَّ تُقارَن بِالمَبثوث.
//
// هذا هو نَظير «تُقارَن المَسارات لا اللَقطات المَكتوبَة بِاليَد» في
// TenantRoleZeroEquivalenceTests: هُناكَ استُدعِيَ المَسار القَديم،
// وهُنا يُقرَأ المِلَفّ القَديم — والمُشتَرَك أَنّ الطَرَف المُقارَن
// **لَيسَ مِن تَأليفي**.

public class ThemeZeroEquivalenceTests
{
    /// <summary>تَرتيب تَحميل الأَوراق في <c>App.razor</c> — والأَخير
    /// يَغلِب. (‏site/studio/templates-* لا تُعَرِّف أَيّاً مِن هذه
    /// المُتَغَيِّرات، فَحَذفُها لا يُغَيِّر جَواباً؛ وتُركَت خارِج
    /// القائِمَة لِأَنّ الاختِبار يَفشَل لَو ظَهَرَ فيها تَعريف
    /// لاحِقاً.)</summary>
    private static readonly string[] LoadOrder =
        { "widgets.css", "app.css", "premium.css" };

    /// <summary><b>الخَريطَة الوَحيدَة</b> بَين مُتَغَيِّر الإطار
    /// ورَمز الثيم. كُلّ سَطر فيها دَعوى: «هذا الرَمز حَلَّ مَحَلّ ذاك
    /// المُتَغَيِّر» — وهي ما يَفحَصُه هذا المِلَفّ.</summary>
    private static readonly (string AcVariable, string TokenKey)[] Correspondence =
    {
        ("--ac-primary",           "color.primary"),
        ("--ac-primary-dark",      "color.primaryDark"),
        ("--ac-primary-light",     "color.primaryLight"),
        ("--ac-primary-hover",     "color.primaryHover"),
        ("--ac-secondary",         "color.secondary"),
        ("--ac-secondary-hover",   "color.secondaryHover"),
        ("--ac-bg",                "color.bg"),
        ("--ac-bg-alt",            "color.bgAlt"),
        ("--ac-surface",           "color.surface"),
        ("--ac-surface-2",         "color.surface2"),
        ("--ac-border",            "color.border"),
        ("--ac-border-strong",     "color.borderStrong"),
        ("--ac-text",              "color.text"),
        ("--ac-text-muted",        "color.textMuted"),
        ("--ac-text-soft",         "color.textSoft"),
        ("--ac-success",           "color.success"),
        ("--ac-danger",            "color.danger"),
        ("--ac-warning",           "color.warning"),
        ("--ac-info",              "color.info"),
        ("--ac-radius-sm",         "radius.sm"),
        ("--ac-radius-md",         "radius.md"),
        ("--ac-radius-lg",         "radius.lg"),
        ("--ac-radius-xl",         "radius.xl"),
        ("--ac-radius-pill",       "radius.pill"),
        ("--ac-space-xs",          "space.xs"),
        ("--ac-space-sm",          "space.sm"),
        ("--ac-space-md",          "space.md"),
        ("--ac-space-lg",          "space.lg"),
        ("--ac-space-xl",          "space.xl"),
        ("--ac-font-size-sm",      "fontSize.sm"),
        ("--ac-font-size-base",    "fontSize.base"),
        ("--ac-font-size-lg",      "fontSize.lg"),
        ("--ac-font-size-xl",      "fontSize.xl"),
        ("--ac-font-weight-normal", "fontWeight.normal"),
        ("--ac-font-weight-bold",  "fontWeight.bold"),
        ("--ac-line-height-base",  "lineHeight.base"),
    };

    /// <summary>
    /// <para><b>الانحِرافات المَقصودَة عَن لَقطَة الأَساس</b> — ولِماذا
    /// تُعلَن هُنا بَدَل أَن تُكتَب في اللَقطَة.</para>
    ///
    /// <para>حينَ يَتَغَيَّر رَمز عَن قيمَتِه التاريخِيَّة، الطَريق
    /// السَهل أَن تُحَرَّر لَقطَة الأَساس فَيَسكُت الاختِبار. وذلك
    /// <b>تَزوير لِلطَرَف المُقارَن</b>: اللَقطَة قيمَتُها كُلُّها في
    /// أَنَّها «ما كان» لا «ما أُريدُه أَن يَكون» — ومَن يُحَرِّرُها
    /// يَهدِم الحارِس لِكُلّ رَمز آخَر لِيَمُرَّ رَمزٌ واحِد.</para>
    ///
    /// <para>فَاللَقطَة تَبقى كَما هي حَرفاً، ويُعلَن الانحِراف هُنا
    /// <b>مُثَبَّتاً مِن طَرَفَيه</b>: القيمَة القَديمَة والجَديدَة
    /// مَعاً. فَإن تَحَرَّكَت اللَقطَة، أَو تَحَرَّكَت القيمَة
    /// الجَديدَة، سَقَطَ التَثبيت وعادَ الاختِبار يَصرُخ — وهذا هو
    /// الفَرق بَين استِثناءٍ مُعلَن وثُقبٍ مَفتوح.</para>
    /// </summary>
    private static readonly (string AcVariable, string Before, string After, string Why)[]
        DeliberateDivergences =
    {
        ("--ac-text-soft", "#a1a1aa", "#6f6f78",
         "تَصحيح تَباين WCAG AA على مَرحَلَتَين. الأولى: ‏#a1a1aa على " +
         "‏#fafafa نِسبَتُه 2.46 — وتَسميات شَريط التَنَقُّل السُفلِيّ " +
         "تَقرَؤُه — فَصارَ ‏#71717a (‏4.63). والثانِيَة: القِياس كانَ " +
         "يَنظُر إلى color.bg وَحدَها، والخافِت يُرسَم أَيضاً فَوق " +
         "color.bgAlt ‏#f4f4f5 (‏.ac-space-placeholder فَوق .ac-space-media، " +
         "و.ac-gallery-placeholder) حَيثُ ‏#71717a = 4.397 — دونَ العَتَبَة. " +
         "‏#6f6f78 يَبلُغ 4.526 على bgAlt و4.767 على bg و4.975 على surface."),

        ("--ac-text-muted", "#71717a", "#6f6f78",
         "تَصحيح تَباين WCAG AA على **السَطح الأَغمَق** لا على الخَلفِيَّة " +
         "وَحدَها: ‏#71717a على color.bgAlt ‏#f4f4f5 نِسبَتُه 4.397 — دونَ " +
         "4.5 — والمَكتوم يُرسَم هُناك فِعلاً (‏.ac-badge-muted و" +
         ".alert-secondary و.acs-empty-icon-line و.acs-auth-hint). " +
         "‏#6f6f78 يَبلُغ 4.526 على bgAlt و4.767 على bg و4.975 على " +
         "surface، بِحِفظ الصِبغَة والإشباع وتَحريك الإضاءَة وَحدَها " +
         "(‏−0.8% لا أَكثَر — أَصغَر خُطوَة تَبلُغ العَتَبَة). " +
         "والمَكتوم والخافِت يَلتَقِيان عِندَ هذه القيمَة **بِقِياس لا " +
         "بِسَهو** — انظُر TheDefaultThemeHasTwoTextLayersNotThree."),
    };

    // ─── ١. المُقارَنَة بِلَقطَة الأَساس ───────────────────────────────

    [Fact]
    public void EveryEmittedTokenEqualsTheLiteralItReplaced()
    {
        var winning = WinningRootDeclarations();
        var theme   = ThemeCatalog.Default;

        var mismatches = new List<string>();
        foreach (var (acVar, key) in Correspondence)
        {
            if (!winning.TryGetValue(acVar, out var before))
            {
                mismatches.Add($"{acVar}: لا تَصريحَة في لَقطَة الأَساس — الخَريطَة كاذِبَة.");
                continue;
            }
            var after = theme[key];
            if (string.Equals(before, after, StringComparison.Ordinal)) continue;

            // انحِراف مُعلَن ومُثَبَّت مِن طَرَفَيه؟ يَمُرّ. وما عَداه يَسقُط.
            bool declared = DeliberateDivergences.Any(d =>
                string.Equals(d.AcVariable, acVar,   StringComparison.Ordinal) &&
                string.Equals(d.Before,     before,  StringComparison.Ordinal) &&
                string.Equals(d.After,      after,   StringComparison.Ordinal));
            if (declared) continue;

            mismatches.Add($"{acVar} ({key}): قَبل «{before}» ≠ بَعد «{after}»");
        }

        Assert.True(mismatches.Count == 0,
            "التَكافُؤ الصِفريّ مَكسور:\n" + string.Join("\n", mismatches));
    }

    [Fact]
    public void EveryDeclaredDivergence_IsStillRealAndStillNeeded()
    {
        // اِستِثناء بَقِيَ بَعدَ زَوال سَبَبِه أَسوَأ مِن لا استِثناء:
        // يُعَلِّم القارِئ أَنّ الرَمز مُنحَرِف وهو لَيسَ كَذلك. فَكُلّ
        // مَدخَل هُنا يَجِب أَن يَكون **مُطابِقاً لِلواقِع** طَرَفَيه.
        var winning = WinningRootDeclarations();
        var theme   = ThemeCatalog.Default;
        var byVar   = Correspondence.ToDictionary(c => c.AcVariable, c => c.TokenKey,
                                                  StringComparer.Ordinal);

        foreach (var d in DeliberateDivergences)
        {
            Assert.True(byVar.ContainsKey(d.AcVariable),
                $"{d.AcVariable}: انحِراف مُعلَن لِمُتَغَيِّر خارِج الخَريطَة.");
            Assert.True(winning.TryGetValue(d.AcVariable, out var before),
                $"{d.AcVariable}: لا تَصريحَة في لَقطَة الأَساس.");
            Assert.Equal(d.Before, before);
            Assert.Equal(d.After, theme[byVar[d.AcVariable]]);
            Assert.NotEqual(d.Before, d.After);
            Assert.False(string.IsNullOrWhiteSpace(d.Why),
                $"{d.AcVariable}: انحِراف بِلا سَبَب مَكتوب.");
        }

        // والقائِمَة نَفسُها مُثَبَّتَة: إضافَة انحِراف ثالِث تُسقِط هذا
        // السَطر، فَلا يَنمو الاستِثناء صامِتاً إلى قائِمَة مَنسِيَّة.
        // (كانَ العُنصُر واحِداً؛ صارَ اثنَين بِقَرار مَرئيّ في مُراجَعَة،
        // وكِلاهُما رَمز نَصّ حَرَّكَه قِياس تَباين — لا نَمَط ثالِث.)
        Assert.Equal(new[] { "--ac-text-soft", "--ac-text-muted" },
                     DeliberateDivergences.Select(d => d.AcVariable).ToArray());
    }

    [Fact]
    public void TheDensityTokenIsTheOnlyOneWithoutAPredecessor()
    {
        // ‏density رَمز **أُدخِلَ** لا وُجِدَ — فَلا حَرفِيَّة سابِقَة لَه.
        // وقيمَتُه 1 تَجعَل calc(x · 1) ≡ x، فَدُخولُه بِتَكافُؤ صِفريّ.
        // وهو الوَحيد كَذلك: كُلّ رَمز آخَر في المَعجَم لَه سَلَف في
        // الخَريطَة أَعلاه، وهذا الاختِبار يَقفِل الباب عَلى إضافَة
        // رَمز بِلا مُستَهلِك.
        var mapped = Correspondence.Select(c => c.TokenKey).ToHashSet(StringComparer.Ordinal);
        var unmapped = ThemeTokenCatalog.All
            .Select(t => t.Key)
            .Where(k => !mapped.Contains(k))
            .ToArray();

        Assert.Equal(new[] { "density" }, unmapped);
        Assert.Equal("1", ThemeCatalog.Default["density"]);
    }

    [Fact]
    public void TheSpacingScaleActuallyConsumesDensity()
    {
        // الدَعوى: رَمز الكَثافَة لَه **مُستَهلِك حَقيقيّ** في CSS، لا
        // مُجَرَّد مَدخَل في المَعجَم. يُقرَأ المِلَفّ الحَيّ (لا لَقطَة
        // الأَساس) لِأَنّ المَفحوص هو الحاضِر.
        var css = File.ReadAllText(Path.Combine(RepoRoot,
            "libs", "templates", "ACommerce.Templates.Customer.Marketplace",
            "wwwroot", "css", "widgets.css"));

        foreach (var size in new[] { "xs", "sm", "md", "lg", "xl" })
            Assert.Contains(
                $"--ac-space-{size}: calc(var(--wsl-space-{size}) * var(--wsl-density));",
                css, StringComparison.Ordinal);
    }

    // ─── ٢. التَكافُؤ الصِفريّ في طَبَقَة القَرار ──────────────────────

    [Fact]
    public void ATenantWithoutADocument_GetsTheVerySameObject()
    {
        // بِالهُوِيَّة لا بِالمُقارَنَة — نَفس عَقد TenantRoleSet.
        Assert.Same(ThemeCatalog.Default, TenantThemeSet.Platform.Theme);

        var empty = TenantThemeSet.FromDocuments("adwar-demo", Array.Empty<TenantThemeDefinition>());
        Assert.Same(ThemeCatalog.Default, empty.Theme);
        Assert.Same(ThemeCatalog.Default.Css, empty.Theme.Css);
        Assert.Null(empty.TenantAuthored);
    }

    [Fact]
    public void PendingAndRejectedDocuments_NeverReachTheHead()
    {
        var docs = new[]
        {
            Doc("adwar_green", TenantThemeStatuses.Pending),
            Doc("adwar_night", TenantThemeStatuses.Rejected),
        };

        var set = TenantThemeSet.FromDocuments("adwar-demo", docs);
        Assert.Same(ThemeCatalog.Default, set.Theme);
    }

    [Fact]
    public void AnApprovedDocument_OverridesOnlyWhatItDeclares()
    {
        var set = TenantThemeSet.FromDocuments("adwar-demo",
            new[] { Doc("adwar_green", TenantThemeStatuses.Approved) });

        // المُعلَن تَغَيَّر…
        Assert.Equal("#14532D", set.Theme["color.primary"]);
        Assert.Equal("#166534", set.Theme["color.primaryHover"]);
        Assert.Equal("4px",     set.Theme["radius.md"]);
        // …وكُلّ ما عَداه سَقَطَ عَلى الافتِراضيّ حَرفاً.
        foreach (var t in ThemeTokenCatalog.All)
            if (t.Key is not ("color.primary" or "color.primaryHover" or "radius.md"))
                Assert.Equal(ThemeCatalog.Default[t.Key], set.Theme[t.Key]);
    }

    [Fact]
    public void AnApprovedDocumentThatChangesNothing_IsNoDocumentAtAll()
    {
        // ثيم يُعيد كِتابَة القِيَم الافتِراضيَّة نَفسِها لا يُنتِج
        // بايتاً مُختَلِفاً — ولا حَتّى كائِناً مُختَلِفاً.
        var same = new ThemeDefinition
        {
            Slug   = "adwar_same",
            Label  = new ThemeLabel("نَفسُه"),
            Tokens = new(StringComparer.Ordinal)
            {
                ["color.primary"] = ThemeCatalog.Default["color.primary"],
                ["radius.md"]     = ThemeCatalog.Default["radius.md"],
            }
        };
        Assert.Same(ThemeCatalog.Default, EffectiveTheme.Compose(ThemeCatalog.Default, same));
    }

    [Fact]
    public void CorruptAndShadowingDocuments_AreIgnoredNotFatal()
    {
        var docs = new[]
        {
            new TenantThemeDefinition
            {
                Id = "broken", Slug = "broken", Status = TenantThemeStatuses.Approved,
                DefinitionJson = "{ this is not json",
                DecidedAt = new DateTime(2026, 8, 10, 12, 0, 0, DateTimeKind.Utc)
            },
            // سلاج يُظَلِّل كاتالوج المَنصَّة — يُرفَض عِندَ الكِتابَة،
            // وهذا حِزام الأَمان الثاني لَو نَجا مِن تَرحيل.
            new TenantThemeDefinition
            {
                Id = "default", Slug = "default", Status = TenantThemeStatuses.Approved,
                DefinitionJson = """
                { "slug": "default", "label": { "ar": "مُنتَحِل" },
                  "tokens": { "color.primary": "#000000" } }
                """,
                DecidedAt = new DateTime(2026, 8, 10, 11, 0, 0, DateTimeKind.Utc)
            },
        };

        var set = TenantThemeSet.FromDocuments("adwar-demo", docs);
        Assert.Same(ThemeCatalog.Default, set.Theme);
    }

    [Fact]
    public void TwoApprovedThemes_ResolveByTheDeclaredOrder_NotByRowOrder()
    {
        // آخِر قَرار يَغلِب — تَرتيب كامِل، فَلا يَتَغَيَّر المَظهَر بَين
        // طَلَبَين بِتَرتيب صُفوف قاعِدَة البَيانات.
        var older = Doc("adwar_green", TenantThemeStatuses.Approved,
            decidedAt: new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc));
        var newer = new TenantThemeDefinition
        {
            Id = "adwar_night", Slug = "adwar_night", Status = TenantThemeStatuses.Approved,
            DecidedAt = new DateTime(2026, 8, 9, 0, 0, 0, DateTimeKind.Utc),
            DefinitionJson = """
            { "slug": "adwar_night", "label": { "ar": "لَيليّ" },
              "tokens": { "color.primary": "#0B3D2E" } }
            """
        };

        foreach (var order in new[] { new[] { older, newer }, new[] { newer, older } })
            Assert.Equal("#0B3D2E",
                TenantThemeSet.FromDocuments("adwar-demo", order).Theme["color.primary"]);
    }

    // ─── ٣. شَكل الكُتلَة المَبثوثَة ───────────────────────────────────

    [Fact]
    public void TheEmittedBlock_IsDeterministicAndComplete()
    {
        var css = ThemeCatalog.Default.Css;

        // كُلّ رَمز، مَرَّةً واحِدَة، بِتَرتيب المَعجَم.
        var cursor = 0;
        foreach (var t in ThemeTokenCatalog.All)
        {
            var decl = $"{t.CssVariable}:{ThemeCatalog.Default[t.Key]};";
            var at = css.IndexOf(decl, cursor, StringComparison.Ordinal);
            Assert.True(at >= 0, $"التَصريحَة «{decl}» مَفقودَة أَو خارِج التَرتيب.");
            cursor = at + decl.Length;
        }

        Assert.Equal(ThemeTokenCatalog.Count, css.Count(c => c == ';'));
        // نَفس النَّصّ في كُلّ نِداء — لا إعادَة بِناء ولا تَرتيب مُتَقَلِّب.
        Assert.Same(css, ThemeCatalog.Default.Css);
    }

    [Fact]
    public void EveryCssVariableNameIsUniqueAndPrefixed()
    {
        var names = ThemeTokenCatalog.All.Select(t => t.CssVariable).ToArray();
        Assert.Equal(names.Length, names.Distinct(StringComparer.Ordinal).Count());
        Assert.All(names, n => Assert.StartsWith(ThemeTokenCatalog.Prefix, n, StringComparison.Ordinal));

        var keys = ThemeTokenCatalog.All.Select(t => t.Key).ToArray();
        Assert.Equal(keys.Length, keys.Distinct(StringComparer.Ordinal).Count());
    }

    // ─── الأَداة: قِراءَة التَصريحَة الغالِبَة مِن لَقطَة الأَساس ───────

    /// <summary>يَقرَأ كُتَل <c>:root</c> مِن أَوراق اللَقطَة بِتَرتيب
    /// التَّحميل ويُرجِع، لِكُلّ مُتَغَيِّر، <b>آخِر</b> تَصريحَة —
    /// وهي الغالِبَة. كُتَل غَير <c>:root</c> (‏<c>body.ac-dark</c>
    /// مَثَلاً) لا تُقرَأ: هي انتِقاء أَشَدّ يَغلِب في سِياقِه وَحدَه،
    /// ولَم تُمَسّ في هذه المَوجَة.</summary>
    private static Dictionary<string, string> WinningRootDeclarations()
    {
        var winning = new Dictionary<string, string>(StringComparer.Ordinal);
        var baseDir = Path.Combine(RepoRoot,
            "tests", "characterization", "appearance", "baseline", "css");

        foreach (var file in LoadOrder)
        {
            var path = Path.Combine(baseDir, file);
            Assert.True(File.Exists(path),
                $"لَقطَة الأَساس مَفقودَة: {path} — البُرهان بِلا طَرَف مُقارَن.");

            foreach (Match block in Regex.Matches(
                         File.ReadAllText(path), @":root\s*\{(?<body>[^}]*)\}"))
            foreach (Match decl in Regex.Matches(
                         block.Groups["body"].Value,
                         @"(?<name>--[a-z0-9-]+)\s*:\s*(?<value>[^;]+);"))
            {
                var value = decl.Groups["value"].Value.Trim();
                // قَصّ التَعليق المُذَيَّل إن وُجِدَ (‏#1D4ED8   /* Royal Blue */).
                var comment = value.IndexOf("/*", StringComparison.Ordinal);
                if (comment >= 0) value = value[..comment].TrimEnd();
                winning[decl.Groups["name"].Value] = value;
            }
        }
        return winning;
    }

    private static TenantThemeDefinition Doc(
        string slug, string status, DateTime? decidedAt = null) => new()
    {
        Id             = slug,
        Slug           = slug,
        Status         = status,
        DecidedAt      = decidedAt,
        DefinitionJson = ThemeDefinitionValidatorTests.GreenJson.Replace(
            "\"slug\": \"adwar_green\"", $"\"slug\": \"{slug}\"", StringComparison.Ordinal)
    };

    /// <summary>جِذر المُستَودَع — يُصعَد مِن مُجَلَّد التَّجميع حَتّى
    /// يُوجَد مِلَفّ الحَلّ. لا مَسار نِسبيّ مَحسوب بِعَدّ
    /// <c>..</c>، فَذلك يَنكَسِر بِتَغيير إطار الهَدَف وَحدَه.</summary>
    internal static string RepoRoot => RepoRootLazy.Value;

    private static readonly Lazy<string> RepoRootLazy = new(() =>
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "PlatformV1.slnx")))
            dir = dir.Parent;
        return dir?.FullName
            ?? throw new InvalidOperationException("تَعَذَّرَ العُثور عَلى جِذر المُستودَع.");
    });
}
