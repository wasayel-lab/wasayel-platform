using System.Globalization;

namespace ACommerce.Kit.Theme;

/// <summary>زَوج فَحص واحِد: رَمز نَصّ، ورَمز السَطح الَّذي يُرسَم فَوقَه.
/// <paramref name="EvidenceAr"/> هو <b>المُنتَقي في CSS الَّذي أَثبَتَ
/// الزَوج</b> — لا شَرح ولا تَبرير. زَوجٌ بِلا مُنتَقٍ لا يَدخُل
/// القائِمَة.</summary>
public sealed record ThemeContrastPair(string TextKey, string SurfaceKey, string EvidenceAr);

/// <summary>حَصيلَة تَقييم التَباين. <b>العَدّادات جُزء مِن العَقد لا
/// زينَة</b>: «صِفر مُخالَفَة» مِن أَداةٍ لا تَقول كَم فَحَصَت لا
/// يُميَّز عَن «صِفر مُخالَفَة» مِن أَداةٍ عَمياء — وهذا وَقَعَ في هذا
/// المُستودَع ثَلاث مَرّات (القاعِدَة ١٠).</summary>
/// <param name="Violations">الخُروقات بِرَمزِها.</param>
/// <param name="Evaluated">أَزواج حُسِبَت نِسبَتُها فِعلاً.</param>
/// <param name="Skipped">أَزواج تَعَذَّرَ الحُكم عَلَيها (رَمز غائِب، أَو
/// سَطح شِبه شَفّاف لا خَلفِيَّة خَلفَه مَعروفَة).</param>
public sealed record ThemeContrastReport(
    IReadOnlyList<ThemeDefinitionViolation> Violations, int Evaluated, int Skipped);

/// <summary>
/// <para><b>قاعِدَة تَباين WCAG AA فَوقَ رُموز الثيم</b> — رِياضِيّات
/// نَقِيَّة: لا قاعِدَة بَيانات، ولا CSS يُقرَأ وَقتَ التَشغيل، ولا
/// مُتَصَفِّح. مِثل بَقِيَّة <see cref="ThemeDefinitionValidator"/>
/// حَرفاً.</para>
///
/// <para><b>الكلفة الَّتي كَتَبَت هذه القاعِدَة</b>: جَولَة سابِقَة
/// صَحَّحَت أَلوان الحُزَم في المِلَفّات، فَلَم يَبلُغ التَصحيح
/// المُستَأجِرينَ المُعتَمَدين إطلاقاً — لِأَنّ وَثيقَة ثيم المُستَأجِر
/// <b>نُسخَة مُجَمَّدَة</b> مِن بايتات الحُزمَة يَومَ طُبِّقَت، لا إحالَة
/// إلى مِلَفّها. أَي أَنّ معيار AA <b>لا يُفرَض بِتَحرير المِلَفّات
/// أَصلاً</b>: يُفرَض عِندَ البَوّابَة أَو لا يُفرَض. ولِذلك مَوضِعُها
/// هُنا — في المُصادِق الَّذي يَمُرّ بِه الاقتِراح <b>والاعتِماد</b>
/// وقِراءَة الوَثيقَة المُعتَمَدَة.</para>
///
/// <para><b>ولِماذا قائِمَة أَزواج مُغلَقَة لا حاصِل ضَربٍ عام</b>: حاصِل
/// الضَرب (كُلّ نَصّ × كُلّ سَطح) يَفرِض تَباينات <b>لا تَقَع في
/// الصَفحَة</b> فَيَرُدّ ثيمات سَليمَة؛ والقائِمَة هُنا <b>مُشتَقَّة
/// بِالعَدّ مِن أَوراق الأَنماط السَبع</b>: قُرِئَت 1,644 قاعِدَة،
/// واستُخرِجَ مِنها كُلّ اقتِران بَينَ <c>color: var(--ac-text*)</c>
/// وخَلفِيَّة العُنصُر نَفسِه أَو حاوِيه. وكُلّ زَوج أَدناه يَحمِل
/// مُنتَقِيَه.</para>
///
/// <para><b>وما لَيسَ في القائِمَة عَمداً</b>: ‏<c>color.textMuted</c>
/// و<c>color.textSoft</c> فَوقَ <c>color.surface2</c> — ‏<c>surface2</c>
/// خَلفِيَّة في قاعِدَتَين اثنَتَين فَقَط (<c>.ac-attr-chip</c>
/// و<c>.ac-attr-multi-opt:hover</c>) وكِلتاهُما تَضبِط لَونَ نَصِّها
/// بِنَفسِها، فَلا مَكتوم ولا خافِت يَقَع هُناك. وإدراجُهُما كانَ
/// سَيَكون <b>خَيالاً</b> — وهو ما يُرَدّ بِه ثيمٌ صَحيح.</para>
///
/// <para><b>والأَلوان المُشتَقَّة خارِج المَدى</b>: ما يُبنى بِـ
/// <c>color-mix</c> أَو <c>--ac-tint-*</c> لَيسَ رَمزاً في المَعجَم
/// (<see cref="ThemeTokenCatalog"/>)، فَلا يُحسَب هُنا. حُدودُه معروفَة
/// ومَكتوبَة، لا مَسكوت عَنها.</para>
/// </summary>
public static class ThemeContrastRule
{
    /// <summary>عَتَبَة WCAG 2.x لِلنَصّ العاديّ. النَصّ الكَبير (‏AA
    /// عِندَ 3.0) لا يُميَّز هُنا: الرَمز واحِد يَخدِم المَقاسَين،
    /// فَالأَصغَر هُوَ المُلزِم.</summary>
    public const double MinimumRatio = 4.5;

    /// <summary>رَمز الخَرق. ثابِت — تُصَحِّح عَلَيه الأَدَوات
    /// والوَكيل.</summary>
    public const string ViolationCode = "contrast_below_aa";

    /// <summary><b>الأَزواج المَقيسَة</b> — عَشَرَة، ولِكُلٍّ مُنتَقيه.</summary>
    public static readonly IReadOnlyList<ThemeContrastPair> Pairs = new ThemeContrastPair[]
    {
        new("color.text", "color.bg",
            "html, body (app.css) و body (premium.css)"),
        new("color.text", "color.bgAlt",
            ".btn-light و .acs-bubble-other و .acs-legal-body و .acm-details-amenity"),
        new("color.text", "color.surface",
            ".ac-input/.ac-select/.ac-textarea و .list-group-item و .acs-cat-pick"),
        new("color.text", "color.surface2",
            ".ac-attr-chip"),

        new("color.textMuted", "color.bg",
            ".text-muted و .form-text و .ac-empty — نَصّ بِلا خَلفِيَّة مَحَلِّيَّة في جِسم الصَفحَة"),
        new("color.textMuted", "color.bgAlt",
            ".ac-badge-muted و .alert-secondary و .acs-empty-icon-line و .acs-auth-hint"),
        new("color.textMuted", "color.surface",
            ".ac-icon-btn و .acm-taxo-chip و .acm-explore-filterbtn و .acs-chat-attach-btn"),

        new("color.textSoft", "color.bg",
            ".ac-gallery-placeholder — تَدَرُّجُها يَنتَهي عِندَ var(--ac-bg)"),
        new("color.textSoft", "color.bgAlt",
            ".ac-space-placeholder فَوقَ .ac-space-media، و .ac-gallery-placeholder"),
        new("color.textSoft", "color.surface",
            ".acs-bottom-nav-item و .ac-listrow-time و .ac-search-icon و ::placeholder"),
    };

    /// <summary>
    /// <para>يُقَيِّم كُلّ زَوج فَوقَ القِيَم الَّتي يُرجِعُها
    /// <paramref name="resolve"/>. ‏<c>null</c> مِنه يَعني «هذا الرَمز
    /// غَير مَعروف في هذا السِياق» فَيُعَدّ الزَوج <b>مُتَخَطّىً</b> لا
    /// ناجِحاً — والفَرق مَحسوب في
    /// <see cref="ThemeContrastReport.Skipped"/>.</para>
    ///
    /// <para><b>والشَفافِيَّة</b>: لَونُ نَصّ بِشَفافِيَّة يُرَكَّب فَوقَ
    /// سَطحِه (عَمَلِيَّة مُعَرَّفَة تَماماً، فَالخَلفِيَّة مَعلومَة)؛ أَمّا
    /// سَطحٌ شِبه شَفّاف فَما خَلفَه غَير مَعلوم لِهذه الطَبَقَة
    /// فَيُتَخَطّى بَدَل أَن يُحكَم عَلَيه بِرَقم مُختَلَق. وهذا الحَدّ
    /// مَكتوب لِأَنَّه حَدّ، لا لِأَنَّه واقِع اليَوم: الأَسطُح الأَربَعَة
    /// في التَعريفات الأَربَعَة كُلُّها صَلبَة.</para>
    /// </summary>
    public static ThemeContrastReport Evaluate(string slug, Func<string, string?> resolve)
    {
        var violations = new List<ThemeDefinitionViolation>();
        int evaluated = 0, skipped = 0;

        foreach (var pair in Pairs)
        {
            var textRaw    = resolve(pair.TextKey);
            var surfaceRaw = resolve(pair.SurfaceKey);
            if (string.IsNullOrWhiteSpace(textRaw) || string.IsNullOrWhiteSpace(surfaceRaw))
            {
                skipped++;
                continue;
            }

            if (!TryParse(textRaw, out var text) || !TryParse(surfaceRaw, out var surface))
            {
                // نَحوٌ فاسِد — يُبَلِّغ عَنه color_malformed في المُصادِق
                // نَفسِه. لا يُضاعَف الخَرق هُنا بِرَمز ثانٍ.
                skipped++;
                continue;
            }

            if (surface.A < 1.0) { skipped++; continue; }

            var fg = text.A < 1.0 ? Composite(text, surface) : text;
            var ratio = Ratio(Luminance(fg), Luminance(surface));
            evaluated++;

            if (ratio + 5e-4 < MinimumRatio)
                violations.Add(new ThemeDefinitionViolation(ViolationCode,
                    $"الثيم «{slug}»: ‏{pair.TextKey} ‏«{textRaw.Trim()}» فَوقَ " +
                    $"‏{pair.SurfaceKey} ‏«{surfaceRaw.Trim()}» نِسبَتُه " +
                    $"{ratio.ToString("F3", CultureInfo.InvariantCulture)} — " +
                    $"والمَطلوب {MinimumRatio.ToString("F1", CultureInfo.InvariantCulture)} " +
                    $"(‏WCAG AA، نَصّ عاديّ). المَوضِع: {pair.EvidenceAr}."));
        }

        return new ThemeContrastReport(violations, evaluated, skipped);
    }

    // ─── الرِياضِيّات — WCAG 2.x حَرفاً ────────────────────────────────

    /// <summary>لَون بِقَنَواتِه ‏0…255 وشَفافِيَّتِه ‏0…1.</summary>
    public readonly record struct Rgba(double R, double G, double B, double A);

    /// <summary>نِسبَة التَباين بَينَ إضاءَتَين نِسبِيَّتَين.</summary>
    public static double Ratio(double lumA, double lumB) =>
        (Math.Max(lumA, lumB) + 0.05) / (Math.Min(lumA, lumB) + 0.05);

    /// <summary>الإضاءَة النِسبِيَّة (‏WCAG 2.x §relative luminance).</summary>
    public static double Luminance(Rgba c) =>
        0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);

    private static double Channel(double v)
    {
        v /= 255.0;
        return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
    }

    /// <summary>تَركيب لَون شِبه شَفّاف فَوقَ سَطح صَلب — ‏source-over.</summary>
    public static Rgba Composite(Rgba fg, Rgba bg) => new(
        fg.R * fg.A + bg.R * (1 - fg.A),
        fg.G * fg.A + bg.G * (1 - fg.A),
        fg.B * fg.A + bg.B * (1 - fg.A),
        1.0);

    /// <summary>يَقرَأ الأَشكال الَّتي يَقبَلُها المُصادِق وَحدَها:
    /// ‏<c>#RGB</c>، <c>#RRGGBB</c>، <c>#RRGGBBAA</c>،
    /// <c>rgb()</c>/<c>rgba()</c>. ‏<b>نَفس المَجموعَة بِالضَبط</b> — فَما
    /// يَجتاز النَحوَ يُحسَب، وما لا يَجتازُه يُرَدّ هُناك لا هُنا.</summary>
    public static bool TryParse(string? css, out Rgba color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(css)) return false;
        var s = css.Trim();

        if (s[0] == '#')
        {
            var hex = s[1..];
            if (hex.Length == 3)
                hex = $"{hex[0]}{hex[0]}{hex[1]}{hex[1]}{hex[2]}{hex[2]}";
            if (hex.Length is not (6 or 8)) return false;
            if (!TryHex(hex, 0, out var r) || !TryHex(hex, 2, out var g) ||
                !TryHex(hex, 4, out var b)) return false;
            double a = 1.0;
            if (hex.Length == 8)
            {
                if (!TryHex(hex, 6, out var av)) return false;
                a = av / 255.0;
            }
            color = new Rgba(r, g, b, a);
            return true;
        }

        var open = s.IndexOf('(');
        if (open < 0 || !s.EndsWith(")", StringComparison.Ordinal)) return false;
        var head = s[..open].Trim();
        if (head is not ("rgb" or "rgba" or "RGB" or "RGBA")) return false;

        var parts = s[(open + 1)..^1].Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length is not (3 or 4)) return false;
        var vals = new double[parts.Length];
        for (var i = 0; i < parts.Length; i++)
            if (!double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out vals[i]))
                return false;

        color = new Rgba(vals[0], vals[1], vals[2], parts.Length == 4 ? vals[3] : 1.0);
        return true;
    }

    private static bool TryHex(string hex, int at, out double value)
    {
        var ok = int.TryParse(hex.AsSpan(at, 2), NumberStyles.HexNumber,
                              CultureInfo.InvariantCulture, out var v);
        value = v;
        return ok;
    }
}
