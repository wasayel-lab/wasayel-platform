using System.Text.RegularExpressions;

namespace ACommerce.Platform.Providers;

/// <summary>
/// <para><b>المُدخَلُ يُفحَص عِندَ الكِتابَةِ لا عِندَ العَرض</b> — وإلّا
/// كانَ سِياجُ المُضيفينَ في مِلَفِّ التَعريفِ زينَةً تُوصَف ولا
/// تُفرَض (سابِقَةُ <c>AllowCustomPattern</c>، القاعِدَة ١٢).</para>
///
/// <para><b>ولِماذا الرابِطُ بِالذاتِ يُسَيَّج</b>: صَفحَةُ الدَفعِ
/// تُصَيِّرُ ما يُخَزَّن. فَرابِطٌ بِلا سياجٍ يَجعَلُ الشاشَةَ إعادَةَ
/// تَوجيهٍ مَفتوحَةً يَكتُبُها صاحِبُ المَتجَر — والزَبونُ يَقرَأ
/// نِطاقَ وَسايِلَ في شَريطِ العُنوانِ قَبلَ النَقر.</para>
///
/// <para>دالَّةٌ نَقِيَّةٌ بِلا I/O: عَلَيها تَقَعُ الوَحَدات.</para>
/// </summary>
public static class ProviderValueValidator
{
    public const string Required        = "field_required";
    public const string NotHttps        = "field_not_absolute_https";
    public const string HostNotAllowed  = "field_host_not_allowed";
    public const string PatternMismatch = "field_pattern_mismatch";

    public static readonly IReadOnlyList<string> Codes =
        new[] { Required, NotHttps, HostNotAllowed, PatternMismatch };

    /// <summary>رَمزُ الرَفض، أَو <c>null</c> إن كانَت القيمَةُ
    /// مَقبولَة.</summary>
    public static string? Refuse(ProviderFieldDefinition field, string? value)
    {
        var v = value?.Trim() ?? "";

        if (v.Length == 0)
            return field.IsRequired ? Required : null;

        if (CredentialKinds.IsLink(field.Kind))
        {
            if (!Uri.TryCreate(v, UriKind.Absolute, out var uri) ||
                !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal))
                return NotHttps;

            if (!HostAllowed(uri.Host, field.HostAllowlist))
                return HostNotAllowed;
        }

        if (!string.IsNullOrEmpty(field.Pattern) &&
            !Regex.IsMatch(v, field.Pattern, RegexOptions.CultureInvariant,
                           TimeSpan.FromMilliseconds(200)))
            return PatternMismatch;

        return null;
    }

    /// <summary>المُضيفُ مَسموحٌ إن طابَقَ إدخالَةً أَو كانَ نِطاقاً
    /// فَرعِيّاً لَها — والمُقارَنَةُ <b>بِالعُنقودِ كامِلاً</b> لا
    /// بِـ<c>EndsWith</c>: «evilmoyasar.com» يَنتَهي بِـ«moyasar.com»
    /// ولَيسَ مِنه.</summary>
    public static bool HostAllowed(string host, IReadOnlyList<string> allowlist)
    {
        if (allowlist.Count == 0) return false;

        var h = host.TrimEnd('.').ToLowerInvariant();

        foreach (var entry in allowlist)
        {
            var a = entry.Trim().TrimEnd('.').ToLowerInvariant();
            if (a.Length == 0) continue;
            if (h == a) return true;
            if (h.EndsWith("." + a, StringComparison.Ordinal)) return true;
        }

        return false;
    }
}
