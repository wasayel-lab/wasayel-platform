using System.Security.Cryptography;
using System.Text;

namespace ACommerce.Kit.Payments.Providers.Paddle;

// ═══ التَوقيعُ يُكتَبُ بِاليَد — ولا SDK رَسميّاً لِـ.NET ═══════════════
//
// **الفَرقُ البِنيَويُّ عَن PayPal، ولِذلك مِلَفٌّ ثانٍ لا فَرعٌ في
// الأَوَّل**: ‏PayPal تُتَحَقَّقُ **بِنِداءِ شَبَكَةٍ** إلى
// `/v1/notifications/verify-webhook-signature`، فَبَوّابَتُها `async`
// وتَعتَمِد على مُضيفٍ خارِجيّ. و‏Paddle تُتَحَقَّقُ **مَحَلِّيّاً**
// بِـ`HMAC-SHA256` — دالَّةٌ نَقِيَّةٌ بِلا شَبَكَةٍ إطلاقاً. وهذا
// **أَقوى** لا أَضعَف: لا يُمكِن أَن يَمُرَّ تَوقيعٌ لِأَنّ مُضيفَ
// المُزَوِّدِ رَدَّ ‏200 على شَيءٍ آخَر.
//
// **والمُوَقَّعُ هُوَ الجِسمُ الخامُّ كَما وَصَل** — بِلا تَحليلٍ ولا
// إعادَةِ تَسلسُل. وهذا لَيسَ تَفصيلاً: `JsonSerializer` يُعيد
// تَرتيبَ الحُقولِ ويَحذِفُ المَسافاتِ ويُطَبِّع الأَرقام، فَبَصمَةُ
// نَصٍّ أُعيدَ تَسلسُلُه **لا تُطابِق شَيئاً أَبَداً** — فَتُرفَض كُلُّ
// رِسالَةٍ صَحيحَة، ويَبدو العَطَبُ «سِرٌّ خاطِئ». ولِذلك تَقرَأُ
// النُقطَةُ الجِسمَ نَصّاً **قَبلَ** أَيِّ `JsonDocument.Parse`.

/// <summary>
/// <para><b>رَأسُ <c>Paddle-Signature</c> مَقروءاً</b> —
/// <c>ts=&lt;unix&gt;;h1=&lt;hex&gt;</c>.</para>
///
/// <para><b>و<c>null</c> مِن <see cref="Parse"/> تَعني «لَيسَ رَأسَ
/// تَوقيعٍ صالِحاً»</b> — لا «لَم يَصِل»: الفَرقُ بَينَهُما يُقالُ في
/// اللوغ بِرَمزَينِ مُختَلِفَين، لِأَنّ عِلاجَهُما مُختَلِف (وِجهَةٌ
/// غَيرُ مَضبوطَةٍ مُقابِلَ وَسيطٍ يَقُصُّ الرُؤوس).</para>
/// </summary>
public sealed record PaddleSignature(long Timestamp, string Hash)
{
    /// <summary>اسمُ الرَأسِ كَما تُرسِلُه Paddle — <b>مَوضِعٌ
    /// واحِد</b> تَقرَؤُه النُقطَةُ والاختِبارُ ووَثيقَةُ النَشر.</summary>
    public const string Header = "Paddle-Signature";

    public const string TimestampKey = "ts";
    public const string HashKey      = "h1";

    /// <summary>
    /// <para><b>قِراءَةُ الرَأسِ — تُعطي <c>null</c> ولا تَرمي.</b>
    /// والصيغَةُ أَزواجٌ مَفصولَةٌ بِفاصِلَةٍ مَنقوطَة، ويُقبَلُ
    /// تَرتيبُها كَيفَما جاء: <b>تَرتيبُ أَزواجٍ عِندَ طَرَفٍ ثالِثٍ
    /// لَيسَ عَقداً</b>.</para>
    ///
    /// <para><b>وتُقرَأُ <c>h1</c> وَحدَها</b>: هي وَحدَها المُوَثَّقَةُ
    /// اليَوم، و<c>h2</c> افتِراضِيَّةٌ لا وُجودَ لَها — ومَن قَرَأَ
    /// «أَيَّ زَوجٍ يَبدَأ بِـ<c>h</c>» فَتَحَ البابَ لِخَوارِزمِيَّةٍ
    /// لا يَعرِفُها (القاعِدَة ١٦).</para>
    /// </summary>
    public static PaddleSignature? Parse(string? header)
    {
        if (string.IsNullOrWhiteSpace(header)) return null;

        long? ts = null;
        string? h1 = null;

        foreach (var pair in header.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var cut = pair.IndexOf('=');
            if (cut <= 0 || cut >= pair.Length - 1) continue;

            var key   = pair[..cut].Trim();
            var value = pair[(cut + 1)..].Trim();

            if (string.Equals(key, TimestampKey, StringComparison.Ordinal)
                && long.TryParse(value, out var parsed))
                ts = parsed;
            else if (string.Equals(key, HashKey, StringComparison.Ordinal) && value.Length > 0)
                h1 = value;
        }

        return ts is { } t && h1 is { Length: > 0 } ? new PaddleSignature(t, h1) : null;
    }
}

/// <summary>حالَةُ بابِ رِسالَةِ Paddle — <b>مَعجَمٌ مُغلَق</b>، وخَمسٌ
/// مِن سِتٍّ رَفض.</summary>
public enum PaddleWebhookGate
{
    /// <summary>لا سِرَّ وِجهَةٍ (أَو لا مِفتاحَ API أَو لا بيئَة) —
    /// فَلا سَبيلَ إلى تَحَقُّق. <b>فَشَلٌ مُغلَق، وبِلا نِداءِ
    /// شَبَكَةٍ واحِد</b>.</summary>
    NotConfigured,

    /// <summary>لا رَأسَ <c>Paddle-Signature</c> إطلاقاً.</summary>
    HeaderMissing,

    /// <summary>الرَأسُ مَوجودٌ ولا يُقرَأ — لا <c>ts</c> أَو لا
    /// <c>h1</c>.</summary>
    HeaderMalformed,

    /// <summary><b>خارِجَ التَسامُحِ الزَمَنيّ</b> — رِسالَةٌ قَديمَةٌ
    /// أُعيدَ لَعِبُها بِتَوقيعِها الصَحيح.</summary>
    TimestampOutOfTolerance,

    /// <summary>البَصمَةُ لا تُطابِق — <b>ومُقارَنَةٌ ثابِتَةُ
    /// الزَمَن</b>.</summary>
    SignatureInvalid,

    /// <summary>تَوقيعٌ صَحيح — <b>والآنَ فَقَط</b> يُقرَأُ الجِسمُ
    /// كَبَيانات.</summary>
    Accepted
}

/// <summary>
/// <para><b>البابُ كُلُّه — دالّاتٌ نَقِيَّة.</b> لا HTTP، ولا
/// <c>DateTime.UtcNow</c>: الوَقتُ يُمَرَّر، فَيُقاسُ التَسامُحُ
/// بِطَرَفَيه بَدَلَ أَن يُنتَظَر.</para>
/// </summary>
public static class PaddleWebhookGuard
{
    /// <summary>
    /// <para><b>التَسامُحُ الزَمَنيُّ خَمسُ ثَوانٍ</b> — ضِدَّ إعادَةِ
    /// اللَعِب: رِسالَةٌ صَحيحَةُ التَوقيعِ التُقِطَت وأُعيدَ إرسالُها
    /// بَعدَ دَقيقَةٍ تُرفَض.</para>
    ///
    /// <para><b>ويُقاسُ بِالقيمَةِ المُطلَقَةِ لا بِالفَرقِ
    /// المُوَجَّه</b>: ساعَةُ خادِمِنا قَد تَتَقَدَّم كَما تَتَأَخَّر،
    /// وقِراءَةُ الطَرَفِ الواحِدِ تَجعَل انحِرافَ ثانِيَتَينِ إلى
    /// الأَمامِ يَقبَل ما يَنبَغي رَفضُه.</para>
    ///
    /// <para><b>والثَمَنُ يُقال</b>: خَمسٌ نافِذَةٌ ضَيِّقَةٌ جِدّاً،
    /// وانحِرافُ ساعَةِ المُضيفِ عَنها يَرُدُّ كُلَّ رِسالَةٍ
    /// بِـ<see cref="PaddleWebhookGate.TimestampOutOfTolerance"/>
    /// — <b>وهُوَ رَمزٌ بِاسمِه</b>، فَيُقرَأُ في اللوغ ولا يُخلَط
    /// بِسِرٍّ خاطِئ.</para>
    /// </summary>
    public const int ToleranceSeconds = 5;

    /// <summary><b>بادِئَةُ سِرِّ الوِجهَة</b> — تُقرَأُ لِلتَشخيصِ
    /// وَحدَه: سِرٌّ يَبدَأ <c>pdl_apikey_</c> بَدَلَ هذا هُوَ
    /// <b>العَطَبُ الأَوَّلُ المُتَوَقَّع</b> (المِفتاحانِ مُتَشابِهانِ
    /// شَكلاً). <b>ولا يُبنى عَلَيه رَفض</b>: بادِئَةُ سِرٍّ عِندَ
    /// طَرَفٍ ثالِثٍ لَيسَت عَقداً، وتَبَدُّلُها يَقلِبُ الرَفضَ
    /// شامِلاً.</summary>
    public const string SecretPrefix = "pdl_ntfset_";

    /// <summary>
    /// <para><b>ما يُوَقَّع: <c>"{ts}:{الجِسمُ الخامّ}"</c>.</b>
    /// والجِسمُ <b>كَما وَصَل</b> — بايتاً ببايت، بِمَسافاتِه
    /// وتَرتيبِ حُقولِه.</para>
    /// </summary>
    public static string SignedPayload(long timestamp, string rawBody)
        => $"{timestamp}:{rawBody}";

    /// <summary>البَصمَةُ المُتَوَقَّعَة —
    /// <c>HMAC-SHA256(secret, "{ts}:{body}")</c> بِـ32 بايتاً.</summary>
    public static byte[] Sign(string secret, long timestamp, string rawBody)
        => HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(secret ?? ""),
            Encoding.UTF8.GetBytes(SignedPayload(timestamp, rawBody)));

    /// <summary>البَصمَةُ نَصّاً — لِلاختِبارِ ولِلتَشخيص، بِحُروفٍ
    /// صَغيرَةٍ كَما تُرسِلُها Paddle.</summary>
    public static string SignHex(string secret, long timestamp, string rawBody)
        => Convert.ToHexString(Sign(secret, timestamp, rawBody)).ToLowerInvariant();

    /// <summary>
    /// <para><b>مُقارَنَةٌ ثابِتَةُ الزَمَن</b> —
    /// <see cref="CryptographicOperations.FixedTimeEquals"/>. ومُقارَنَةُ
    /// السَلاسِلِ العادِيَّةُ تَقصُر عِندَ أَوَّلِ حَرفٍ مُختَلِف،
    /// فَيُسَرِّب زَمَنُها البَصمَةَ مِحرَفاً مِحرَفاً.</para>
    ///
    /// <para><b>ونَصٌّ سِتَّ عَشَرِيٌّ غَيرُ مَقروءٍ عَدَمُ تَطابُقٍ لا
    /// استِثناء</b>: طولٌ فَرديٌّ أَو مِحرَفٌ خارِجَ المَعجَمِ يَرتَدُّ
    /// <c>false</c> بِلا انفِجار — والانفِجارُ هُنا ‏500 تُعيدُه Paddle
    /// خَمسَ عَشرَةَ مَرَّة.</para>
    /// </summary>
    public static bool Matches(byte[] expected, string? receivedHex)
    {
        if (string.IsNullOrWhiteSpace(receivedHex)) return false;

        byte[] received;
        try { received = Convert.FromHexString(receivedHex.Trim()); }
        catch { return false; }

        return CryptographicOperations.FixedTimeEquals(expected, received);
    }

    /// <summary>
    /// <para><b>أَتُقرَأُ هذِه الرِسالَةُ كَبَيانات؟</b> — سِتُّ
    /// إجاباتٍ واحِدَةٌ مِنها قَبول، <b>وكُلُّها قَبلَ أَيِّ
    /// <c>JsonDocument.Parse</c></b>.</para>
    ///
    /// <para><b>والتَرتيبُ هُوَ الأَمن</b>: تَهيئَةٌ، ثُمَّ رَأسٌ، ثُمَّ
    /// صيغَةٌ، ثُمَّ زَمَن، ثُمَّ بَصمَة. فَما يُرَدُّ بِلا حِسابِ
    /// <c>HMAC</c> يُرَدُّ بِلا حِسابِه.</para>
    /// </summary>
    public static PaddleWebhookGate Gate(
        PaddleOptions? options, string? signatureHeader, string? rawBody, DateTimeOffset now)
    {
        if (!PaddleEnvironment.CanVerifyWebhooks(options)) return PaddleWebhookGate.NotConfigured;
        if (string.IsNullOrWhiteSpace(signatureHeader)) return PaddleWebhookGate.HeaderMissing;

        if (PaddleSignature.Parse(signatureHeader) is not { } sig)
            return PaddleWebhookGate.HeaderMalformed;

        var drift = Math.Abs(now.ToUnixTimeSeconds() - sig.Timestamp);
        if (drift > ToleranceSeconds) return PaddleWebhookGate.TimestampOutOfTolerance;

        return Matches(Sign(options!.WebhookSecret, sig.Timestamp, rawBody ?? ""), sig.Hash)
            ? PaddleWebhookGate.Accepted
            : PaddleWebhookGate.SignatureInvalid;
    }

    /// <summary>رَمزُ الرَفضِ كَما يُكتَبُ في اللوغ ويُرَدُّ في الجِسم
    /// — <b>خَمسَةُ أَسبابٍ لا سَبَبٌ واحِد</b>: «لا سِرّ» غَيرُ
    /// «تَوقيعٌ فاشِل»، وخَلطُهُما يُرسِل المالِكَ يُفَتِّشُ عَن سِرٍّ
    /// خاطِئٍ ومُشكِلَتُه سِرٌّ غائِب.</summary>
    public static string GateCode(PaddleWebhookGate gate) => gate switch
    {
        PaddleWebhookGate.NotConfigured           => "paddle_not_configured",
        PaddleWebhookGate.HeaderMissing           => "paddle_signature_header_missing",
        PaddleWebhookGate.HeaderMalformed         => "paddle_signature_header_malformed",
        PaddleWebhookGate.TimestampOutOfTolerance => "paddle_signature_stale",
        PaddleWebhookGate.SignatureInvalid        => "paddle_signature_invalid",
        _                                         => "paddle_accepted"
    };
}
