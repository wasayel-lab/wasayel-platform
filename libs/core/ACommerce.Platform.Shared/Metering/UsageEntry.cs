using System.Security.Cryptography;
using System.Text;

namespace ACommerce.Platform.Shared.Metering;

/// <summary>
/// <para><b>سَطرُ الدَفتَرِ كَما يَكتُبُه الكاتِب</b> — الأَعمِدَةُ الَّتي تُمَرَّر؛ و<c>occurred_at</c> و<c>writer_xid</c> افتِراضُ القاعِدَة،
/// و<c>generation</c> ما خَبَّأَه الإقلاع (<see cref="MeterSql"/>). وكُلُّ حَقلٍ قيمَةٌ أَو قيمَةٌ حارِسَة (<see cref="MeterSentinels"/>) لا <c>null</c>.</para>
/// <para><b>والسَطرُ لا يَتَغَيَّر</b>: يُكتَبُ بِـ<c>INSERT … ON CONFLICT (id) DO NOTHING</c> ولا يُحَدَّثُ أَبَداً.</para>
/// </summary>
public sealed record UsageEntry(
    string Id, string Unit, long Quantity, string App, string Entrepreneur, string Role,
    string Feature, string Provider, string SourceType, string SourceId);

/// <summary>
/// <para><b>مُعَرِّفُ السَطرِ الحَتمِيّ</b> (التَصميم §2): الحِفظُ <c>{unit}:{app}:{source_id}</c> — فَالإعادَةُ بِالمَصدَرِ نَفسِه لا تُضاعِف.</para>
/// <para><b>ومُعَرِّفُ الشَخصِ لا يَدخُلُ الدَفتَرَ إلّا اسماً مُستَعاراً</b> (<see cref="Pseudonym"/>): السِتَّةَ عَشَرَ بايتاً الأولى مِن SHA-256 فَوقَ
/// <c>meter:{unit}:{app}:{raw}</c> بِالسِتّ عَشرِيّ. <b>بِلا مِفتاحٍ عَن قَصد</b>: مِفتاحٌ يُدَوَّرُ يَكسِرُ مَنعَ التَكرارِ والمُطابَقَة، والمُعَرِّفاتُ
/// الخامُ <c>Guid</c> عَشوائِيَّة فَلا تُعَدَّد. ومُستَهلِكُه الأَوَّلُ في وَقتِ التَشغيلِ <c>member.join</c> (البَند ٨)؛ ومُعَرِّفُ رائِدِ الأَعمالِ
/// يُحفَظُ صَريحاً — مِفتاحُ عَميلِ المَنَصَّةِ نَفسِه (س٩).</para>
/// </summary>
public static class UsageEntryId
{
    public static string For(string unit, string app, string sourceId) => $"{unit}:{app}:{sourceId}";

    public static string Pseudonym(string unit, string app, string raw)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"meter:{unit}:{app}:{raw}"));
        return Convert.ToHexStringLower(hash.AsSpan(0, 16));
    }
}
