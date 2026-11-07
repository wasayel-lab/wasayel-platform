namespace ACommerce.Kit.Tenants;

/// <summary>
/// <para><b>قَوالِبُ صَفَحاتِ الدَورِ — ما يُحقَنُ فيه دَورُ المُضيف</b> (‏2026-11-07، ADR-050 §٤ المَرحَلَةُ الثالِثَة · W13-4·C2b·4). عَلى
/// <c>{slug}-{rolehost}.R</c> يوضَعُ <c>/r/{role}</c> في المَسارِ <b>لِطَلَباتِ GET/HEAD الَّتي تُطابِقُ أَحَدَ هذِه القَوالِبِ وَحدَها</b> — فَيَبقى دَعوى كَما
/// يَبقى <c>/r/{role}</c> في المَسارِ اليَوم، ولا يَبلُغُ نُقطَةَ كِتابَةٍ ولا API (صِفرُ نُقطَةِ كِتابَةٍ بِـ<c>/r/{role}</c>، ويَحرُسُه G7).</para>
///
/// <para><b>والقائِمَةُ لا تُكتَبُ بِيَد</b>: يَجمَعُها القالِبُ مِن <c>@page</c> صَفَحاتِه (<c>/{slug}/r/{role}/…</c>) عِندَ الإقلاع، فَصَفحَةُ دَورٍ جَديدَةٌ
/// تَدخُلُ بِلا لَمسِ هذا المِلَفّ، وتَوأَمُها بِلا دَورٍ مَشروطٌ بِـG5. والحاقِنُ (<c>Platform.MultiTenancy</c>) لا يَرى القالِب — يَسأَلُ هذا الصِنفَ
/// مِن الحاوِيَة إن سُجِّل، وبِدونِه لا يُحقَنُ دَورٌ (مُضيفُ الدَورِ يُخدَمُ كَمُضيفِ مَتجَرِه).</para>
///
/// <para><b>والمُطابَقَةُ نَقِيَّةٌ ومُغلَقَة</b>: مَقطَعٌ حَرفِيٌّ بِلا حالَةِ أَحرُف، ومُعامِلٌ <c>{x}</c> يُطابِقُ أَيَّ مَقطَعٍ غَيرِ فارِغ، و<c>{x:guid}</c> مُعَرِّفاً
/// صالِحاً — وهي أَشكالُ القَوالِبِ الخَمسَةِ والثَلاثينَ كُلُّها (لا اختِيارِيَّ ولا التِقاطاً شامِلاً؛ والحارِسُ يَحمَرُّ إن ظَهَرَ شَكلٌ آخَر).</para>
/// </summary>
public sealed class RolePageTemplates
{
    /// <summary>بادِئَةُ قالِبِ صَفحَةِ الدَور — <c>/{slug}/r/{role}</c> بِأَيِّ حالَةِ أَحرُفٍ لِاسمَي المُعامِلَين.</summary>
    public const string Prefix = "/{slug}/r/{role}";

    private readonly string[][] _rests;

    /// <summary>القَوالِبُ كَما هي (<c>/{slug}/r/{role}/…</c>) — ما لا يَبدَأُ بِالبادِئَةِ يُتَجاهَل.</summary>
    public IReadOnlyList<string> Templates { get; }

    public RolePageTemplates(IEnumerable<string> routes)
    {
        Templates = routes.Where(IsRoleTemplate).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(t => t, StringComparer.Ordinal).ToArray();
        _rests = Templates.Select(t => t[Prefix.Length..].Split('/', StringSplitOptions.RemoveEmptyEntries)).ToArray();
    }

    /// <summary>أَقالِبُ صَفحَةِ دَورٍ هذا؟</summary>
    public static bool IsRoleTemplate(string? route)
        => route is not null && route.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)
           && (route.Length == Prefix.Length || route[Prefix.Length] == '/');

    /// <summary>
    /// <para>أَيُطابِقُ <paramref name="rest"/> — المَسارُ بَعدَ <c>/{slug}/r/{role}</c> (<c>/</c> لِجَذرِ الدَور، <c>/me/listings</c>…) — قالِبَ صَفحَةِ دَور؟</para>
    /// </summary>
    public bool Matches(string? rest)
    {
        var segments = (rest ?? "/").Split('/', StringSplitOptions.RemoveEmptyEntries);
        foreach (var template in _rests)
        {
            if (template.Length != segments.Length) continue;
            var all = true;
            for (var i = 0; i < template.Length && all; i++) all = SegmentMatches(template[i], segments[i]);
            if (all) return true;
        }
        return false;
    }

    private static bool SegmentMatches(string template, string segment)
    {
        if (template.Length > 1 && template[0] == '{' && template[^1] == '}')
        {
            var colon = template.IndexOf(':');
            var constraint = colon < 0 ? "" : template[(colon + 1)..^1];
            return segment.Length > 0
                   && (!constraint.Equals("guid", StringComparison.OrdinalIgnoreCase) || Guid.TryParse(segment, out _));
        }
        return string.Equals(template, segment, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>أَشكالُ المَقاطِعِ المَقبولَة — حَرفِيٌّ، أَو <c>{name}</c>، أَو <c>{name:guid}</c>. يَسأَلُه الحارِسُ عَن كُلِّ قالِب.</summary>
    public static bool IsSupportedSegment(string segment)
        => !segment.Contains('{') && !segment.Contains('}') && !segment.Contains('*')
           || System.Text.RegularExpressions.Regex.IsMatch(segment, @"^\{[A-Za-z_][A-Za-z0-9_]*(:guid)?\}$");
}
