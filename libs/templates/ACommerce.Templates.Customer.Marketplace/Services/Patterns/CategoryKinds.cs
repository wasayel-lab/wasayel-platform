using ACommerce.Kit.Tenants;

namespace ACommerce.Templates.Customer.Marketplace.Services.Patterns;

/// <summary>خَرقٌ واحِدٌ في تَجميعِ فِئَة — شَكلُ المُصادِقِ المُوَحَّد (القاعِدَة ٤): رَمزٌ ثابِتٌ ونَصٌّ لِلمُراجِع.</summary>
public sealed record CategoryKindViolation(string Code, string MessageAr);

/// <summary>
/// <para><b>تَجميعُ الفِئَة (<see cref="Category.Kind"/>) — مَعجَمٌ مُغلَق</b> (‏2026-10-29، قَرارُ المالِك د25 ويُغَطّي د49؛ ‏ADR-034 §٣‑٢ مُلحَق د25).</para>
///
/// <para><b>العِلَّةُ المَقيسَة</b>: الكُتّابُ الثَلاثَة (مُحَرِّرُ الفِئاتِ في الاستوديو والإدارَة، ووَكيلُ الاستوديو، وصَفحَةُ الإنشاءِ الإدارِيَّة) كانوا
/// يَكتُبونَ أَيَّ نَصٍّ تَجميعاً بِحُروفٍ صَغيرَة، و<see cref="PatternProfileResolver.PatternOf"/> يَقرَأُ <c>residential</c>/<c>commercial</c> إيجاراً بِلا سَلَّة
/// و<c>roommate</c> سَكَناً مُشتَرَكاً — فَخَطَأُ حَرفٍ أَو اقتِراحُ وَكيلٍ يُعيدُ مَتجَرَ مُنتَجاتٍ «إيجاراً» بِلا شَيءٍ يَرُدّ.</para>
///
/// <para><b>والقائِمَةُ مِمّا يَكتُبُه الكودُ ويَقرَؤُه فِعلاً، بِلا قيمَةٍ مُختَرَعَة</b> (نَصُّ القَرار): الفارِغُ و<c>fnb</c> و<c>services</c> و<c>transport</c>
/// يَكتُبُها مَصنَعُ الاستوديو (<c>TenantFromAnalysisFactory</c>)، و<c>menu</c> و<c>events</c> و<c>vehicles</c> تَبذُرُها <c>PlatformSeed</c>، و<c>residential</c>
/// و<c>commercial</c> و<c>roommate</c> يَكتُبُها الاثنانِ ويَقرَؤُها <c>PatternOf</c>. <b>و«تِجاريّ» و«سَكَنيّ» مَسموحَتان</b>: مَتجَرُ تَأجيرِ العَقارِ يَختارُهُما
/// عَن قَصد — فَلا يُمنَعُ اختِيارٌ صَحيح، بَل يُرَدُّ الخَطَأُ ويُعلَنُ الأَثَر (<see cref="EffectOf"/>).</para>
///
/// <para><b>والمُصادِقُ في مَوضِعٍ واحِدٍ يَمُرُّ بِه الكُتّابُ الثَلاثَة</b>: <c>CategoriesSaveService.Parse</c> (المُحَرِّرانِ وصَفحَةُ الإنشاءِ بَعدَ التَوحيدِ
/// في <c>0cfc033</c>) ووَكيلُ الاستوديو بَعدَ قِراءَةِ حُمولَتِه. وقائِمَةُ الوَكيلِ في رِسالَةِ نِظامِه تُقرَأُ مِن <see cref="All"/>.</para>
/// </summary>
public static class CategoryKinds
{
    /// <summary>فِئَةٌ بِلا تَجميع — لا تُغَيِّرُ نَمَطَ المَتجَر.</summary>
    public const string None = "";
    public const string Menu = "menu";
    public const string Residential = "residential";
    public const string Commercial = "commercial";
    public const string Roommate = "roommate";
    public const string Fnb = "fnb";
    public const string Services = "services";
    public const string Transport = "transport";
    public const string Events = "events";
    public const string Vehicles = "vehicles";

    /// <summary>المَعجَمُ المُغلَق — عَشرٌ بِتَرتيبِ نَصِّ القَرار.</summary>
    public static readonly IReadOnlyList<string> All = new[]
    {
        None, Menu, Residential, Commercial, Roommate, Fnb, Services, Transport, Events, Vehicles,
    };

    /// <summary>تَجميعٌ خارِجَ المَعجَم.</summary>
    public const string Unknown = "category_kind_unknown";

    /// <summary>رُموزُ الخَرقِ كُلُّها — واحِدٌ لا ثانِيَ لَه.</summary>
    public static readonly IReadOnlyList<string> Codes = new[] { Unknown };

    /// <summary>أَهذا التَجميعُ في المَعجَم؟ (يُقارَنُ بِحَرفِه: الكُتّابُ يُصَغِّرونَ قَبلَ السُؤال.)</summary>
    public static bool IsKnown(string? kind) => kind is not null && All.Contains(kind, StringComparer.Ordinal);

    /// <summary>
    /// <para><b>المُصادِق</b>: خَرقٌ لِكُلِّ فِئَةٍ تَجميعُها خارِجَ المَعجَم، بِاسمِها وقيمَتِها — كُلُّ الخُروقِ لا أَوَّلُها (‏ADR-038).</para>
    /// </summary>
    public static IReadOnlyList<CategoryKindViolation> Validate(IEnumerable<Category> categories)
        => categories
            .Where(c => !IsKnown(c.Kind))
            .Select(c => new CategoryKindViolation(Unknown,
                $"تَجميعُ الفِئَةِ «{c.Slug}» = «{c.Kind}» خارِجَ المَعجَم ({string.Join("، ", All.Select(k => k.Length == 0 ? "فارِغ" : k))})."))
            .ToList();

    public static bool IsValid(IEnumerable<Category> categories) => Validate(categories).Count == 0;

    /// <summary>
    /// <para><b>أَثَرُ التَجميعِ عَلى نَمَطِ المَتجَر — مَقروءٌ مِن القارِئِ نَفسِه لا مَكتوب</b>: نَمَطُ مَتجَرٍ بِلا أَدوارٍ فِئَتُه بِهذا التَجميع
    /// (<see cref="PatternProfileResolver.PatternOf(IEnumerable{string}, IEnumerable{string})"/>). فَلَو تَغَيَّرَ القارِئُ تَغَيَّرَ الأَثَرُ المُعلَنُ في المُحَرِّرِ
    /// مَعَه — لا نَصٌّ يَكذِبُ عَلى كود.</para>
    /// </summary>
    public static AppPattern EffectOf(string kind)
        => PatternProfileResolver.PatternOf(new[] { kind }, Array.Empty<string>());

    /// <summary>مِفتاحُ اسمِ التَجميعِ في القامُوس — <c>categories.kind.{kind}</c>، والفارِغُ <c>categories.kind.none</c>.</summary>
    public static string NameKey(string kind) => "categories.kind." + (kind.Length == 0 ? "none" : kind);

    /// <summary>مِفتاحُ نَصِّ الأَثَرِ في القامُوس — بِالنَمَطِ الَّذي يُعطيه <see cref="EffectOf"/>.</summary>
    public static string EffectKey(string kind) => EffectOf(kind) switch
    {
        AppPattern.Rental   => "categories.kind_effect.rental",
        AppPattern.Roommate => "categories.kind_effect.roommate",
        _                   => "categories.kind_effect.none",
    };
}
