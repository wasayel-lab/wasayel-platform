namespace ACommerce.Kit.Auth;

/// <summary>
/// <para><b>رَقَمٌ واحِدٌ هُوِيَّةٌ واحِدَة</b> (‏2026-10-28، قَرارُ المالِك د57 «أ»؛ ‏ADR-044). كانَ الجَوّالُ يُخَزَّنُ ويُطابَقُ <b>كَما كُتِب</b> في أَربَعَةِ
/// أَبواب — دُخولُ العُضو، ودُخولُ الاستوديو، ومَنحُ مُشرِفِ المَنَصَّة، و«أَضِف جَوّالَك» — فَـ<c>05…</c> و<c>+966…</c> لِرَقَمٍ واحِدٍ حِسابان
/// (تَوصيفُه <c>d72e34e</c>). والصيغَةُ المُوَحَّدَةُ (E.164) كانَت مَوجودَةً لِغَيرِ الهُوِيَّة: <c>AuthHandlers.PhoneDestination</c> — وِجهَةُ الرَمزِ ومِفتاحُ
/// حِصَّتِه. فَصارَت هِيَ هذِه الدالَّةَ بِعَينِها (تُنادى مِن هُناك) ولا تَعريفَ ثانٍ يَنجَرِف.</para>
///
/// <para><b>والقاعِدَتان</b>: (١) <b>يُكتَبُ الجَديدُ مُوَحَّداً</b> (<see cref="Canonical"/>) — حِسابٌ يُنشِئُه دُخولٌ أَو جَوّالٌ يُربَط؛ (٢) <b>ويُبحَثُ
/// كَما كُتِبَ أَوَّلاً ثُمَّ مُوَحَّداً</b> (<see cref="LookupForms"/>، <see cref="Pick{T}"/>) — فَلا يَتَغَيَّرُ دُخولُ حِسابٍ قائِمٍ لَم يُرَحَّل، ومَن رُحِّلَ
/// (‏<c>scripts/merge-duplicate-phones.cs</c>) يَجِدُه دُخولُه بِأَيِّ كِتابَة. وما لَيسَ جَوّالاً سُعودِيّاً (رَقَمٌ خارِجَها، أَو <c>NID-…</c>، أَو نَصّ) يَبقى
/// كَما كُتِبَ مَقصوصاً — فَصيغَتاه واحِدَة.</para>
/// </summary>
public static class PhoneNumber
{
    /// <summary>
    /// <para>رَقَمٌ سُعودِيٌّ (<c>05…</c> أَو <c>5…</c> أَو <c>966…</c> أَو <c>00966…</c> أَو <c>+966…</c>، بِمَسافاتٍ أَو شَرطاتٍ أَو أَقواسٍ أَو بِدونِها)
    /// يَصيرُ <c>+9665XXXXXXXX</c>؛ وما سِواه يَبقى كَما كُتِبَ مَقصوصاً. (نُقِلَت مِن <c>AuthHandlers.PhoneDestination</c> بِلا تَغييرِ حَرفٍ في مَنطِقِها.)</para>
    /// </summary>
    public static string Canonical(string? typed)
    {
        var raw = (typed ?? "").Trim();
        var compact = new System.Text.StringBuilder(raw.Length);
        foreach (var c in raw)
            if (c is not (' ' or '-' or '(' or ')')) compact.Append(c);
        var s = compact.ToString();
        var plus = s.StartsWith('+');
        var digits = plus ? s[1..] : s;
        if (digits.Length == 0 || !digits.All(char.IsAsciiDigit)) return raw;
        if (!plus && digits.StartsWith("00", StringComparison.Ordinal)) digits = digits[2..];
        else if (!plus && digits.Length == 10 && digits.StartsWith("05", StringComparison.Ordinal)) digits = "966" + digits[1..];
        else if (!plus && digits.Length == 9 && digits[0] == '5') digits = "966" + digits;
        return digits.Length == 12 && digits.StartsWith("9665", StringComparison.Ordinal) ? "+" + digits : raw;
    }

    /// <summary>صِيَغُ البَحثِ بِالتَرتيب: كَما كُتِبَ (مَقصوصاً) ثُمَّ المُوَحَّدَة — بِلا تَكرار، وفارِغٌ لِلفارِغ.</summary>
    public static IReadOnlyList<string> LookupForms(string? typed)
    {
        var asTyped = (typed ?? "").Trim();
        if (asTyped.Length == 0) return Array.Empty<string>();
        var canonical = Canonical(asTyped);
        return canonical == asTyped ? new[] { asTyped } : new[] { asTyped, canonical };
    }

    /// <summary>مِن حامِلي الصِيَغ (نَتيجَةِ استِعلامٍ بِـ<see cref="LookupForms"/>): مَن يَحمِلُ الرَقَمَ كَما كُتِبَ أَوَّلاً، ثُمَّ مَن يَحمِلُه مُوَحَّداً.</summary>
    public static T? Pick<T>(IEnumerable<T> holders, Func<T, string?> phoneOf, string? typed) where T : class
    {
        var list = holders as IList<T> ?? holders.ToList();
        foreach (var form in LookupForms(typed))
            if (list.FirstOrDefault(h => string.Equals(phoneOf(h), form, StringComparison.Ordinal)) is { } hit)
                return hit;
        return null;
    }
}
