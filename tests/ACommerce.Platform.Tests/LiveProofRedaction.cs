namespace ACommerce.Platform.Tests;

/// <summary>
/// <para><b>خَرجُ البَراهينِ الحَيَّةِ بِلا حَقلٍ شَخصِيّ</b> (‏2026-10-25، بَندُ المُنَسِّقِ في تَيّارِ W8-2). البَراهينُ تَعمَلُ عَلى قاعِدَةٍ حَيَّةٍ
/// بِالتَصميم، وكانَت ثَلاثَتُها تَكتُبُ إلى <c>Console</c> ومِلَفِّ الأَدِلَّةِ جَوّالَ مُشرِفِ المَنَصَّةِ واسمَه، أَو <c>CreatedBy</c>/<c>DecidedBy</c>
/// وبابُ الثيمِ والأَدوارِ يَكتُبُ فيهِما «<c>{FullName} · {Phone}</c>».</para>
///
/// <para><b>والقاعِدَةُ في نِداءِ الكاتِبِ لا في القِيمَة</b>: الحَقلُ يُحجَبُ في سَطرٍ قَبلَ النِداء (<see cref="Of"/>) فَلا يَظهَرُ في وُسَطاءِ
/// <c>Log</c> — وذاكَ ما يَقيسُه <c>TestOutputPersonalDataTests</c>. ثُمَّ <see cref="AssertAbsent"/> يَقيسُ الخَرجَ نَفسَه بَعدَ كِتابَتِه.</para>
///
/// <para><b>ثَلاثَةُ مُستَهلِكينَ يَومَ وُلِد</b> (القاعِدَة ١): <c>LiveThemeProofTests</c> و<c>LiveRoleDefinitionProofTests</c>
/// و<c>LiveApprovalFlowProofTests</c>.</para>
/// </summary>
internal static class LiveProofRedaction
{
    public const string Mask = "‹مَحجوب›";

    /// <summary>«-» لِلفارِغ، وإلّا القِناع — لا طولَ ولا طَرَفَ مِن القيمَة.</summary>
    public static string Of(string? value) => string.IsNullOrWhiteSpace(value) ? "-" : Mask;

    /// <summary>ما كُتِبَ لا يَحمِلُ أَيّاً مِن القيَمِ الحَيَّة — ورِسالَةُ الفَشَلِ لا تُعيدُ القيمَة (وإلّا صارَ الفاحِصُ نَفسُه تَسريباً). القيمَةُ دونَ
    /// ثَلاثَةِ أَحرُفٍ لا تُقاسُ (تُطابِقُ أَيَّ نَصّ)، ويُعادُ عَدَدُ ما قيس.</summary>
    public static int AssertAbsent(string evidence, IEnumerable<string?> values)
    {
        var measured = 0;
        foreach (var raw in values)
        {
            var v = raw?.Trim();
            if (string.IsNullOrEmpty(v) || v.Length < 3) continue;
            measured++;
            Xunit.Assert.False(evidence.Contains(v, StringComparison.Ordinal),
                $"حَقلٌ حَيٌّ (القيمَةُ رَقم {measured} بِطولِ {v.Length}) ظَهَرَ في خَرجِ البُرهان.");
        }
        return measured;
    }
}
