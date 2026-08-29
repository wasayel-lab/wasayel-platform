using System.Reflection;
using System.Text.Json;

namespace ACommerce.Platform.I18n;

/// <summary>مَدخَلَة واحِدَة كَما وَرَدَت في المِلَفّ — <b>بِتَرتيبِها
/// وبِتَكرارِها</b>. المُصادِق يَحتاج التَكرار (لِيَراه)، والقامُوس
/// الفَعّال لا يَحتاجُه؛ فَالقِراءَة تَحفَظ الخام والطَيّ يَأتي
/// بَعدَه.</summary>
public sealed record LocaleEntry(string Key, string Value);

/// <summary>
/// <para><b>قامُوس نُصوص الواجِهَة — بَياناً لا كوداً</b> (القاعِدَة ١١).
/// مِلَفّ JSON لِكُلّ لُغَة، بِمَفاتيح مُوَحَّدَة، مَبثوثاً كَمَورِد
/// مُضَمَّن في التَجميعَة — فَلا يَعتَمِد على مَسار قُرص ولا على نَسخ
/// مُخرَجات.</para>
///
/// <para><b>ولِماذا خَريطَة لُغات لا حَقلا <c>Ar</c>/<c>En</c></b>:
/// الحَقلان يَجعَلان اللُغَة الثالِثَة <b>تَغييرَ نَوع</b> يَمُرّ على كُلّ
/// مَوضِع يَقرَؤُه؛ والخَريطَة تَجعَلُها <b>مِلَفّاً يُضاف</b>. نَفس
/// المِعيار الفاصِل في القاعِدَة ٤: التَنَوُّع المُنتَهي المَعروف يَصير
/// بَيانات.</para>
///
/// <para><b>والعَرَبِيَّة هي المَعجَم المُغلَق</b>: مِلَفّ
/// <c>ar.json</c> يُعَرِّف مَجموعَة المَفاتيح المَشروعَة، وكُلّ لُغَة
/// أُخرى <b>اختِيارِيَّة وجُزئيَّة</b> — ومِفتاحٌ يَنقُصُها يَسقُط إلى
/// العَرَبِيَّة، لا إلى المِفتاح الخام. (‏عَرضُ المِفتاح الخام لِلمُستَخدِم
/// عَطَبٌ مَرئيّ يَتَنَكَّر في هَيئَة تَرجُمَة.)</para>
/// </summary>
public static class LocaleCatalog
{
    /// <summary>رَمز اللُغَة الإلزامِيَّة. كُلّ سُقوط يَنتَهي هُنا.</summary>
    public const string Arabic = "ar";

    // ─── العَلامَةُ النائِبَة — عَلامَةٌ صَريحَةٌ لا تَخمين ───────────

    /// <summary>فاتِحَةُ القيمَةِ النائِبَة.</summary>
    public const string PlaceholderOpen = "[[";

    /// <summary>خاتِمَتُها.</summary>
    public const string PlaceholderClose = "]]";

    /// <summary>
    /// <para><b>أَهذِه القيمَةُ نائِبَةٌ لَم تُملَأ بَعد؟</b> —
    /// <b>عَلامَةٌ صَريحَةٌ في القيمَةِ نَفسِها</b>
    /// (<c>[[ … ]]</c>)، لا تَخمينٌ مِن طولٍ أَو مُحتَوى.</para>
    ///
    /// <para><b>ولِماذا في القيمَةِ لا في قائِمَةِ مَفاتيح</b>:
    /// القائِمَةُ تَنجَرِف — يُملَأُ مِفتاحٌ ويُنسى شَطبُه مِنها
    /// فَيُخفى نَصٌّ صَحيح، أَو يُضافُ مِفتاحٌ نائِبٌ ولا يُدرَجُ
    /// فيها فَيُعرَض لِزائِر. <b>والعَلامَةُ تَسقُطُ مَعَ القيمَةِ
    /// حينَ تُملَأ</b>، فَلا شَيءَ يُنسى.</para>
    ///
    /// <para><b>وهي مُشتَرَكَةٌ عَبرَ اللُغات</b>: <c>ar.json</c>
    /// و<c>en.json</c> يَكتُبانِ نَفسَ القَوسَين، فَالفَحصُ يَقَعُ
    /// على القيمَةِ <b>في لُغَتِها هي</b> — والصَفحَةُ
    /// الإنجليزِيَّةُ تُقَرِّرُ بِـ<c>en.json</c> لا
    /// بِـ<c>ar.json</c>.</para>
    /// </summary>
    public static bool IsPlaceholder(string? value)
    {
        var v = (value ?? "").Trim();
        return v.StartsWith(PlaceholderOpen, StringComparison.Ordinal)
            && v.EndsWith(PlaceholderClose, StringComparison.Ordinal);
    }

    /// <summary>أَنائِبٌ هُوَ نَصُّ هذا المِفتاحِ في هذِه اللُغَة؟
    /// — بِنَفسِ سُقوطِ <see cref="Text"/>.</summary>
    public static bool IsPlaceholderKey(string lang, string key)
        => IsPlaceholder(Text(lang, key));

    private const string ResourcePrefix =
        "ACommerce.Platform.I18n.Locales.";

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<LocaleEntry>> RawByLang
        = LoadRaw();

    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> ByLang
        = RawByLang.ToDictionary(
            p => p.Key,
            p => (IReadOnlyDictionary<string, string>)p.Value
                    .GroupBy(e => e.Key, StringComparer.Ordinal)
                    .ToDictionary(g => g.Key, g => g.Last().Value, StringComparer.Ordinal),
            StringComparer.Ordinal);

    /// <summary>رُموز اللُغات المَوجودَة فِعلاً، والعَرَبِيَّة أَوَّلاً.
    /// «مَوجودَة» تَعني <b>لَها مِلَفّ</b> — فَإضافَة لُغَة إسقاطُ مِلَفّ
    /// لا تَعديلُ كود.</summary>
    public static IReadOnlyList<string> Languages { get; } =
        ByLang.Keys
              .OrderBy(l => l == Arabic ? 0 : 1)
              .ThenBy(l => l, StringComparer.Ordinal)
              .ToList();

    /// <summary>المَعجَم المُغلَق: مَفاتيح العَرَبِيَّة. ما خَرَجَ عَنه
    /// خَرقٌ يُمسِكُه <see cref="LocaleValidator"/>.</summary>
    public static IReadOnlyCollection<string> Lexicon { get; } =
        ByLang.TryGetValue(Arabic, out var ar)
            ? ar.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList()
            : Array.Empty<string>();

    /// <summary>هَل لِهذا الرَمز مِلَفّ؟</summary>
    public static bool Has(string lang) => ByLang.ContainsKey(lang);

    /// <summary>القيمَة في لُغَةٍ بِعَينِها، أَو <c>null</c>. لا سُقوط
    /// هُنا — السُقوط قَرار <see cref="Text"/>.</summary>
    public static string? Find(string lang, string key) =>
        ByLang.TryGetValue(lang, out var d) && d.TryGetValue(key, out var v) ? v : null;

    /// <summary>النَصّ المَعروض: اللُغَة المَطلوبَة، ثُمَّ العَرَبِيَّة،
    /// ثُمَّ المِفتاح نَفسُه. والثالِثَة لا تَقَع في بِناءٍ سَليم —
    /// يَمنَعُها المُصادِق — وتَبقى لِأَنّ صَفحَةً بِمِفتاح ظاهِر أَهوَن
    /// مِن صَفحَة بِاستِثناء.</summary>
    public static string Text(string lang, string key) =>
        Find(lang, key) ?? Find(Arabic, key) ?? key;

    /// <summary>المَدخَلات الخام لِلُغَة — <b>بِتَكرارِها</b>. لِلمُصادِق
    /// وَحدَه.</summary>
    public static IReadOnlyList<LocaleEntry> Raw(string lang) =>
        RawByLang.TryGetValue(lang, out var e) ? e : Array.Empty<LocaleEntry>();

    /// <summary>كُلّ ما قُرِئ، لُغَةً لُغَةً — مُدخَل
    /// <see cref="LocaleValidator.Validate"/>.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<LocaleEntry>> All => RawByLang;

    /// <summary>
    /// القِراءَة بِـ<see cref="Utf8JsonReader"/> لا بِـ<c>Deserialize</c>
    /// عَمداً: القامُوس يَبتَلِع المِفتاح المُكَرَّر صامِتاً (الأَخير
    /// يَفوز) — فَلَو قَرَأنا قامُوساً لَما رَأى المُصادِق التَكرار
    /// أَبَداً، وهو أَحَد الخُروق الثَلاثَة الَّتي وُجِدَ لِيُمسِكَها.
    /// </summary>
    private static IReadOnlyDictionary<string, IReadOnlyList<LocaleEntry>> LoadRaw()
    {
        var asm = typeof(LocaleCatalog).Assembly;
        var result = new Dictionary<string, IReadOnlyList<LocaleEntry>>(StringComparer.Ordinal);

        foreach (var name in asm.GetManifestResourceNames()
                                .Where(n => n.StartsWith(ResourcePrefix, StringComparison.Ordinal)
                                         && n.EndsWith(".json", StringComparison.Ordinal))
                                .OrderBy(n => n, StringComparer.Ordinal))
        {
            var lang = name[ResourcePrefix.Length..^".json".Length];
            using var stream = asm.GetManifestResourceStream(name)!;
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            result[lang] = ReadEntries(ms.ToArray());
        }

        return result;
    }

    /// <summary>قِراءَة مِلَفّ لُغَة واحِد إلى مَدخَلات خام — عامَّة
    /// لِأَنّ البَوّابَة تُختَبَر <b>مِن النَصّ</b> لا مِن قامُوس
    /// يُبنى في الاختِبار (وإلّا لَما رَأى الاختِبار خَرق
    /// التَكرار أَصلاً).</summary>
    public static IReadOnlyList<LocaleEntry> ReadEntries(ReadOnlySpan<byte> utf8)
    {
        var entries = new List<LocaleEntry>();
        var reader = new Utf8JsonReader(utf8);
        string? key = null;

        while (reader.Read())
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.PropertyName:
                    key = reader.GetString();
                    break;
                case JsonTokenType.String when key is not null:
                    entries.Add(new LocaleEntry(key, reader.GetString() ?? string.Empty));
                    key = null;
                    break;
            }
        }

        return entries;
    }
}
