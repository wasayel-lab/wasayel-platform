namespace ACommerce.Kit.Compliance;

/// <summary>
/// <para><b>حاوِيَةُ التَوطين — خَريطَةٌ مَفتوحَةٌ بِمَفاتيحِ لُغات، لا
/// حَقلا <c>Ar</c>/<c>En</c></b> (القاعِدَة ١١). نَفسُ شَكل
/// <c>ProviderText</c> حَرفاً، ولا يُستَعارُ مِنه بِمَرجِعِ مَشروع:
/// هذِه العُدَّةُ بِلا مَرجِعٍ واحِدٍ عَمداً (انظُر الـcsproj).</para>
/// </summary>
public static class ComplianceText
{
    public const string Arabic = "ar";

    public static readonly IReadOnlyDictionary<string, string?> Empty =
        new Dictionary<string, string?>(0, StringComparer.Ordinal);

    /// <summary>نَصُّ اللُغَةِ المَطلوبَة، وإلّا فَالعَرَبِيَّة —
    /// والسُقوطُ إلَيها لا إلى المِفتاحِ الخام.</summary>
    public static string Get(IReadOnlyDictionary<string, string?> text, string lang)
    {
        if (text.TryGetValue(lang, out var v) && !string.IsNullOrWhiteSpace(v)) return v;
        return text.TryGetValue(Arabic, out var ar) && !string.IsNullOrWhiteSpace(ar) ? ar : "";
    }

    public static bool HasArabic(IReadOnlyDictionary<string, string?> text) =>
        text.TryGetValue(Arabic, out var ar) && !string.IsNullOrWhiteSpace(ar);
}

/// <summary>
/// <para><b>مُستَوَيا الالتِزام — والخَلطُ بَينَهُما هُوَ العَطَبُ
/// المِعمارِيُّ الَّذي كَتَبَ هذا المَعجَم.</b> المَنَصَّةُ مُوَفِّرُ
/// خِدمَةٍ لِمُشتَرِكيها، وكُلُّ مُستَأجِرٍ مُوَفِّرُ خِدمَةٍ
/// لِمُستَهلِكيه. فَالمادَّةُ الواحِدَةُ تُفحَصُ مَرَّتَينِ
/// بِشاهِدَينِ مُختَلِفَين، ونَتيجَتُها نَتيجَتان.</para>
///
/// <para><b>ولِماذا لا يُشتَقُّ المُستَوى مِن مَوضِعِ الشاهِد</b>:
/// شاهِدُ المُستَأجِرِ اليَومَ نَصٌّ <b>مُوَحَّدٌ لِكُلِّ
/// المُستَأجِرين</b> في قامُوسِ المَنَصَّةِ نَفسِه (‏`LegalHub`
/// و`Terms.razor:15-16` يَعتَرِفُ بِذلكَ صَراحَةً) — فَالمَوضِعُ
/// واحِدٌ والمُستَوى اثنان. الاشتِقاقُ كانَ سَيَجعَلَ كُلَّ التِزامِ
/// مُستَأجِرٍ التِزامَ مَنَصَّةٍ بِالخَطَأ، ويُخفي أَنَّ كُلَّ
/// مَتجَرٍ يُفحَصُ عَلى حِدَة.</para>
/// </summary>
public static class ComplianceLevels
{
    /// <summary>وَسايِلُ نَفسُها — نُسخَةٌ واحِدَةٌ تُفحَصُ مَرَّةً
    /// واحِدَة عَلى أُصولٍ ثابِتَة.</summary>
    public const string Platform = "platform";

    /// <summary>مَتجَرٌ واحِد — يُفحَصُ <b>مَرَّةً لِكُلِّ
    /// مُستَأجِر</b> عَلى وَثيقَتِه هُوَ.</summary>
    public const string Tenant = "tenant";

    public static readonly IReadOnlyList<string> All = new[] { Platform, Tenant };

    private static readonly HashSet<string> Set = new(All, StringComparer.Ordinal);

    public static bool Contains(string level) => Set.Contains(level);

    public static string Require(string level)
    {
        if (!Contains(level))
            throw new ArgumentException(
                $"المُستَوى «{level}» خارِج مَعجَم ComplianceLevels. " +
                $"المَعجَم: {string.Join("، ", All)}.", nameof(level));
        return level;
    }
}

/// <summary>
/// <para><b>أَنواعُ الشاهِد — أَربَعَةٌ، وهي حَدُّ ما يُفحَصُ
/// لَفظِيّاً.</b> الالتِزامُ يَختارُ مِنها ويُعَيِّرُها، والفاحِصُ
/// يُقَيِّمُها بِمَنطِقٍ واحِدٍ لا بِمَنطِقٍ لِكُلِّ مادَّة — وهذا
/// هُوَ بِعَينِه ما يَجعَلُ الالتِزامَ <b>بَياناً يُضاف</b> لا
/// <b>كوداً يُكتَب</b>.</para>
///
/// <para><b>ولِماذا أَربَعَةٌ لا أَكثَر</b> (القاعِدَة ١): لِكُلِّ
/// واحِدٍ مُستَهلِكٌ في مِلَفّاتِ الالتِزاماتِ المَشحونَة — يُقاسُ
/// بِفَحصٍ يَعُدُّه. والخامِسُ لا يُضافُ قَبلَ أَن يوجَدَ مَن
/// يَستَعمِلُه.</para>
///
/// <para><b>وما لا يَقَعُ في هذا المَعجَمِ يُعلَنُ ولا يُبتَلَع</b>:
/// السُلوكُ (حَذفٌ عِندَ انقِضاءِ المُدَّة، إخطارٌ يُرسَل، ساعَةٌ
/// تَحسِبُ سَبعَةَ أَيّام) لا يُثبِتُه نَشرُ جُملَة. فَيُكتَبُ في
/// <c>notCheckable</c> مِن مِلَفِّ الالتِزامِ نَفسِه، وتَعرِضُه
/// اللَوحَةُ صَريحاً — <b>فَلا يُخضَرُّ بَندٌ لِأَنَّ نَصَّهُ
/// مَنشور</b>.</para>
/// </summary>
public static class EvidenceKinds
{
    /// <summary>مِفتاحٌ في القامُوسِ لَه قيمَةٌ غَيرُ فارِغَة. أَضعَفُ
    /// الشُهود، ويَكفي حَيثُ يَكونُ النَشرُ ذاتُه هُوَ المَطلوب.</summary>
    public const string TextPresent = "text_present";

    /// <summary><b>مَوجودٌ ولَيسَ نائِباً</b> (‏<c>[[ … ]]</c>). وهذا
    /// هُوَ الفَرقُ الَّذي يَجعَلُ الفاحِصَ ذا قيمَة: صَفحَةُ
    /// <c>/contact</c> مَبنِيَّةٌ ومَعروضَةٌ وحُقولُها الأَربَعَةُ
    /// <b>صِفرٌ ذو قيمَة</b> — فَفاحِصٌ يَعُدُّ الوُجودَ وَحدَه
    /// يُخضِرُّ مُخالَفَةً قائِمَة.</summary>
    public const string TextFilled = "text_filled";

    /// <summary>مَوجودٌ <b>ولا يَحوي</b> واحِدَةً مِن عِباراتٍ
    /// مَمنوعَة. الشاهِدُ الوَحيدُ الَّذي يُمسِكُ نَصّاً <b>يَنقُضُ
    /// الالتِزامَ بِوُجودِه</b>: «لِحَذفِ حِسابِكَ تَواصَل عَبرَ
    /// صَفحَةِ الدَعم» لَيسَ نَقصاً في النَشر، هُوَ إحالَةٌ إلى
    /// خارِجِ التَطبيقِ مَكتوبَةٌ بِخَطِّ اليَد.</summary>
    public const string TextFreeOf = "text_free_of";

    /// <summary>مَسارٌ مَوجودٌ في جَدوَلِ مَساراتِ التَطبيق. الشاهِدُ
    /// الوَحيدُ الَّذي يَقولُ «تُبلَغُ بِالنَقر» (القاعِدَة ١٢) بَدَلَ
    /// «مَكتوبٌ أَنَّها تُبلَغ».</summary>
    public const string RouteReachable = "route_reachable";

    public static readonly IReadOnlyList<string> All = new[]
    {
        TextPresent, TextFilled, TextFreeOf, RouteReachable,
    };

    private static readonly HashSet<string> Set = new(All, StringComparer.Ordinal);

    public static bool Contains(string kind) => Set.Contains(kind);

    /// <summary>هَل يَقرَأُ هذا النَوعُ قامُوسَ النُصوص؟ (‏الثَلاثَةُ
    /// الأولى نَعَم، والرابِعُ يَقرَأُ جَدوَلَ المَسارات.)</summary>
    public static bool ReadsText(string kind) =>
        kind is TextPresent or TextFilled or TextFreeOf;

    public static string Require(string kind)
    {
        if (!Contains(kind))
            throw new ArgumentException(
                $"نَوعُ الشاهِد «{kind}» خارِج مَعجَم EvidenceKinds. " +
                $"المَعجَم: {string.Join("، ", All)}.", nameof(kind));
        return kind;
    }
}
