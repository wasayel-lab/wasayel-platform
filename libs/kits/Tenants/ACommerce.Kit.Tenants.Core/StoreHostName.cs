using ACommerce.Kit.Roles;

namespace ACommerce.Kit.Tenants;

/// <summary>خَرقُ اسمِ مُضيفِ المَتجَر — رَمزٌ مِن مُعجَمٍ مُغلَقٍ ونَصٌّ عَرَبِيّ (شَكلُ مُصادِقاتِ المُستَودَع، ADR-038).</summary>
public sealed record StoreHostNameViolation(string Code, string MessageAr);

/// <summary>
/// <para><b>هل يَصلُحُ مُعَرِّفُ المَتجَرِ مُضيفاً لَه ولِأَدوارِه؟</b> (‏2026-11-07، ADR-050 §٣، W13-4·C2a·2). المَتجَرُ <c>{slug}.R</c> وتَطبيقُ
/// دَورِه <c>{slug}-{rolehost}.R</c> مِلصَقانِ في المُستَوى الأَوَّل — فَالمُعَرِّفُ يَجِبُ أَن يَكونَ مِلصَقَ DNS (‏RFC 1123) يَتَّسِعُ بَعدَه
/// <c>-{أَطوَلُ اسمِ دَور}</c>، ولا يُشبِهُ مُضيفَ دَورِ مَتجَرٍ آخَر.</para>
///
/// <para><b>مُصادِقُ بَياناتٍ يُجيبُ بِكُلِّ خُروقِه</b> (ADR-038): يَسأَلُه بابُ المُعَرِّفِ الواحِد (<c>TenantFromAnalysisFactory.SlugRefusal</c>) فَيُترجِمُ
/// خُروقَه إلى رُموزِ الواجِهَةِ القائِمَة، ويَسأَلُه حَلُّ المُضيفِ فَلا يُعطى مَتجَرٌ قائِمٌ لا يَجتازُه مُضيفاً (يَبقى بِمَسارِه).</para>
///
/// <para><b>والعِلَّةُ المَقيسَة</b> (عِندَ <c>f81840c</c>): الشَكلُ الواحِدُ <c>^[a-z0-9][a-z0-9-]{0,62}$</c> يَقبَلُ <c>shop-</c> (شَرطَةٌ في الآخِر،
/// لَيسَت مِلصَقاً)، و<c>xn--…</c> و<c>ab--cd</c> (‏RFC 5891 يَحجُزُ <c>--</c> في المَوضِعَينِ ٣–٤)، وطولاً بَينَ 54 و63 لا يَتَّسِعُ مَعَه
/// <c>-organizer</c>، و<c>best-vendor</c> الَّذي هُوَ مُضيفُ دَورِ البائِعِ لِمَتجَرِ <c>best</c>.</para>
/// </summary>
public static class StoreHostName
{
    // ─── رُموزُ الخَرق — مُعجَمٌ مُغلَقٌ، ولِكُلٍّ اختِبارٌ مُوجَبٌ وسالِب ───
    public const string Empty        = "host_name_empty";
    public const string TooLong      = "host_name_too_long";
    public const string Charset      = "host_name_charset";
    public const string EdgeHyphen   = "host_name_edge_hyphen";
    public const string DoubleHyphen = "host_name_double_hyphen";
    public const string Reserved     = "host_name_reserved";
    public const string RoleSuffix   = "host_name_role_suffix";

    public static readonly IReadOnlyList<string> Codes =
        [Empty, TooLong, Charset, EdgeHyphen, DoubleHyphen, Reserved, RoleSuffix];

    /// <summary>حَدُّ مِلصَقِ DNS الواحِد (‏RFC 1035).</summary>
    public const int DnsLabelMax = 63;

    /// <summary><b>سَقفُ المُعَرِّفِ مُشتَقٌّ لا مَكتوب</b>: ‏63 − 1 (الشَرطَة) − أَطوَلُ قيمَةٍ في <see cref="RoleHostNames"/> — فَيَتَّسِعُ كُلُّ مُضيفِ دَورٍ
    /// لِكُلِّ مَتجَرٍ في مِلصَقٍ واحِد. ودَورٌ أَطوَلُ يَدخُلُ الكَتالوجَ غَداً يُنقِصُه بِلا لَمسِ هذا السَطر.</summary>
    public static readonly int MaxLength = DnsLabelMax - 1 - RoleHostNames.LongestLength;

    /// <summary>كُلُّ خُروقِ المُعَرِّف — فارِغَةً إن صَلَحَ مُضيفاً.</summary>
    public static IReadOnlyList<StoreHostNameViolation> Validate(string? slug)
    {
        if (string.IsNullOrWhiteSpace(slug))
            return [new(Empty, "مُعَرِّفُ المَتجَرِ مَطلوب.")];

        var found = new List<StoreHostNameViolation>();

        if (slug.Length > MaxLength)
            found.Add(new(TooLong, $"مُعَرِّفُ المَتجَرِ أَطوَلُ مِن {MaxLength} مِحرَفاً — لا يَتَّسِعُ مَعَه اسمُ دَورٍ في مِلصَقِ المُضيف."));

        if (!slug.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-'))
            found.Add(new(Charset, "مُعَرِّفُ المَتجَرِ حُروفٌ لاتينِيَّةٌ صَغيرَةٌ وأَرقامٌ وشَرطَةٌ فَقَط."));

        if (slug[0] == '-' || slug[^1] == '-')
            found.Add(new(EdgeHyphen, "مُعَرِّفُ المَتجَرِ لا يَبدَأُ بِشَرطَةٍ ولا يَنتَهي بِها."));

        if (slug.Contains("--", StringComparison.Ordinal))
            found.Add(new(DoubleHyphen, "لا شَرطَتانِ مُتَتالِيَتانِ في مُعَرِّفِ المَتجَر (ومِنها البادِئَةُ xn-- لِلأَسماءِ الدَولِيَّة)."));

        if (ReservedTenantSlugs.Contains(slug))
            found.Add(new(Reserved, "هذا المُعَرِّفُ مَحجوزٌ لِلمَنَصَّة."));

        if (RoleSuffixOf(slug) is { } role)
            found.Add(new(RoleSuffix, $"مُعَرِّفُ المَتجَرِ يَنتَهي بِـ«-{role}» — وهُوَ مُضيفُ دَورٍ لِمَتجَرٍ آخَر."));

        return found;
    }

    public static bool IsValid(string? slug) => Validate(slug).Count == 0;

    /// <summary>اسمُ مُضيفِ الدَورِ الَّذي يَنتَهي بِه المُعَرِّفُ بَعدَ شَرطَة (وقَبلَها شَيء)، أَو <c>null</c>.</summary>
    private static string? RoleSuffixOf(string slug)
    {
        foreach (var role in RoleHostNames.Values)
            if (slug.Length > role.Length + 1 && slug.EndsWith("-" + role, StringComparison.Ordinal))
                return role;
        return null;
    }
}
