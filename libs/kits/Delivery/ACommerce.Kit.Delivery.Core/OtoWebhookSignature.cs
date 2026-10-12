using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace ACommerce.Kit.Delivery;

/// <summary>
/// <para><b>تَوقيعُ وارِدِ OTO — دالّاتٌ نَقِيَّة</b> (تَيّارُ S8، ‏2026-10-12؛ ‏ADR-033 الخُطوَة 7 و§٧‑م). مِن قِسمِ «Webhook» في
/// الوَثائِقِ العامَّةِ (<c>https://apis.tryoto.com/</c>، مُصَيَّراً ‏2026-10-12) بِحَرفِه: «‏Signed "orderId:status:timestamp" with
/// "HmacSHA256" method and Base64 encoded string. Secret key for the signature you can provide during webhook configuration.»،
/// و«‏timestamp : Unix epoch timestamp (in milliseconds)»، و«‏timestamp value belongs to UTC time zone». فَالحَقلانِ
/// <c>signature</c> و<c>timestamp</c> في <b>جِسمِ</b> الإشعارِ لا في رَأس — والوَثيقَةُ لا تَذكُرُ رَأساً لَهُما.</para>
///
/// <para><b>والسِرُّ نُصدِرُه نَحن</b> (<see cref="NewSecret"/>): ‏32 بايتاً عَشوائِيّاً سِتَّ عَشَرِيّاً (‏64 مِحرَفاً، تَحتَ حَدِّ ‏255)،
/// يُسَلَّمُ لِـOTO في تَسجيلِ الوارِدِ ويُختَمُ عِندَنا. ومِفتاحُ ‏HMAC بايتاتُ نَصِّه ‏UTF-8 — كَما تَحمِلُه OTO نَصّاً.</para>
///
/// <para><b>ولا HTTP ولا ساعَة</b>: الوَقتُ يُمَرَّرُ، فَتُقاسُ النافِذَةُ بِطَرَفَيها — نَسَقُ <c>PaddleWebhookGuard</c> حَرفاً.</para>
/// </summary>
public static class OtoWebhookSignature
{
    /// <summary>طولُ السِرِّ المُصدَرِ بِالبايت — ‏256 بِتّاً، طولُ مُخرَجِ ‏HMAC-SHA256 نَفسِه.</summary>
    public const int SecretBytes = 32;

    /// <summary>
    /// <para><b>نافِذَةُ الزَمَنِ خَمسُ ثَوانٍ — رَقَمُ الجارِ لا رَقَمُ OTO</b>: الوَثيقَةُ لا تَنشُرُ نافِذَة، والرَقَمُ الوَحيدُ في المُستَودَعِ
    /// نافِذَةُ <c>PaddleWebhookGuard.ToleranceSeconds</c>. <b>ويُقالُ ما يُكَلِّف</b>: <c>timestamp</c> عِندَ OTO «وَقتُ وُقوعِ الحَدَث» لا
    /// وَقتُ الإرسال، فَإشعارٌ تَأَخَّرَ إرسالُه أَكثَرَ مِن خَمسٍ يُرَدُّ بِرَمزِه (<c>signature_stale</c>) ويُطبَعُ انحِرافُه في اللوغ — والرَقَمُ
    /// يُضبَطُ بِالمَقيسِ عَلى staging (سِجِلُّ المالِكِ «ب»). والانحِرافُ بِالقيمَةِ المُطلَقَة: ساعَتُنا قَد تَتَقَدَّمُ كَما تَتَأَخَّر.</para>
    /// <para><b>وما يَحمي إن ضاقَت النافِذَةُ أَو اتَّسَعَت</b>: الإشعارُ إشارَةٌ لا بُرهان — حالُ الطَلَبِ يُسأَلُ عَنه OTO قَبلَ أَيِّ كِتابَة،
    /// فَإشعارٌ أُعيدَ لَعِبُه بِتَوقيعِه لا يَكتُبُ ما لَم تَقُلْه OTO الآن.</para>
    /// </summary>
    public const int ToleranceSeconds = 5;

    /// <summary>سِرٌّ جَديدٌ لِبائِع: ‏32 بايتاً مِن مُوَلِّدِ التَعمِيَةِ سِتَّ عَشَرِيّاً بِأَحرُفٍ صَغيرَة.</summary>
    public static string NewSecret() => Convert.ToHexString(RandomNumberGenerator.GetBytes(SecretBytes)).ToLowerInvariant();

    /// <summary>النَصُّ المُوَقَّعُ بِحَرفِ الوَثيقَة: <c>orderId:status:timestamp</c>.</summary>
    public static string SignedText(string orderId, string status, string timestamp) => $"{orderId}:{status}:{timestamp}";

    /// <summary>بَصمَةُ ‏HMAC-SHA256 بِالسِرِّ عَلى النَصِّ المُوَقَّع — بايتاتٌ لا نَصّ.</summary>
    public static byte[] Sign(string secret, string orderId, string status, string timestamp)
        => HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret),
            Encoding.UTF8.GetBytes(SignedText(orderId, status, timestamp)));

    /// <summary>التَوقيعُ كَما تُرسِلُه OTO — ‏Base64 لِلبَصمَة.</summary>
    public static string Compute(string secret, string orderId, string status, string timestamp)
        => Convert.ToBase64String(Sign(secret, orderId, status, timestamp));

    /// <summary>
    /// <para><b>أَيُطابِقُ التَوقيعُ؟ — مُقارَنَةٌ ثابِتَةُ الزَمَن</b> (<see cref="CryptographicOperations.FixedTimeEquals"/>) عَلى البايتاتِ
    /// بَعدَ فَكِّ ‏Base64. وتَوقيعٌ غائِبٌ أَو لَيسَ Base64 لا يُطابِق — بِلا استِثناء.</para>
    /// </summary>
    public static bool Matches(string secret, string orderId, string status, string timestamp, string? signature)
    {
        if (string.IsNullOrWhiteSpace(signature)) return false;

        byte[] received;
        try { received = Convert.FromBase64String(signature.Trim()); }
        catch (FormatException) { return false; }

        return CryptographicOperations.FixedTimeEquals(Sign(secret, orderId, status, timestamp), received);
    }

    /// <summary>انحِرافُ <paramref name="timestamp"/> (مِلّي ثانِيَةٍ مُنذُ عَهدِ يونِكس) عَن <paramref name="now"/> بِالقيمَةِ المُطلَقَة —
    /// و<c>null</c> لِخَتمٍ لا يُقرَأُ رَقَماً.</summary>
    public static long? DriftMilliseconds(string? timestamp, DateTimeOffset now)
        => long.TryParse(timestamp, NumberStyles.None, CultureInfo.InvariantCulture, out var ms)
            ? Math.Abs(now.ToUnixTimeMilliseconds() - ms)
            : null;

    /// <summary>داخِلَ النافِذَة؟ (<see cref="ToleranceSeconds"/>).</summary>
    public static bool IsFresh(long driftMilliseconds) => driftMilliseconds <= ToleranceSeconds * 1000L;
}
