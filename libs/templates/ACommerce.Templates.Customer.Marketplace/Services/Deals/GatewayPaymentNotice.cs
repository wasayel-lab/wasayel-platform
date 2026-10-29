using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ACommerce.Templates.Customer.Marketplace.Services.Deals;

// ═══ إشعارُ الدَفعِ الآليُّ مِن بَوّابَةِ المَتجَر (‏2026-10-29، قَرارُ المالِك د5 (٢) «ج») ═══════════════════════════════
//
// **القَرار**: «إشعار آليّ من ميسر إلى المنصّة عند كل دفع، محميّ بسرّ مشترك يُتحقَّق منه في كل إشعار. ويبقى زرّ «تحقّق» للمشتري احتياطًا» —
// لِأَنّ المُتَصَفِّحَ قَد يُغلَقُ بَعدَ الدَفعِ مُباشَرَة. **والشَكلُ مِن الوَثيقَةِ لا مُخَمَّن** (مَقروءاً ‏2026-10-29): «Webhook Payload» — <c>id</c>
// و<c>type</c> و<c>created_at</c> و<c>secret_token</c> («assigned by the consumer») و<c>account_name</c> و<c>live</c> و<c>data</c> (الدَفعُ نَفسُه لِأَحداثِ
// <c>payment_*</c>، وفيه <c>invoice_id</c> «Invoice ID that this payment is used to pay»)؛ ولا تَوقيعَ في رَأس. فَالحارِسُ سِرٌّ نُصدِرُه ونُسَجِّلُه عِندَ
// البَوّابَةِ ونُقارِنُه بِزَمَنٍ ثابِت — **والإشعارُ إشارَةٌ لا بُرهان** كَوارِدِ OTO: الدَفعُ يُثبَتُ بِجَلبِ الفاتورَةِ بِمِفتاحِ المَتجَرِ وعَرضِها عَلى
// <see cref="GatewayPaymentProof.Gate"/> نَفسِها، لا بِما في الجِسم.

/// <summary>خَرقٌ واحِدٌ في إشعارِ الدَفع — نَفسُ شَكلِ <see cref="OtoNoticeViolation"/> (القاعِدَة ٤). و<c>MessageAr</c> لِلوغِ ولِلمُراجِع: الرَدُّ آلَةٌ لِآلَة.</summary>
public sealed record GatewayNoticeViolation(string Code, string MessageAr);

/// <summary>
/// <para><b>إشعارُ دَفعٍ كَما وَصَل — بَعدَ أَن طابَقَ سِرُّه</b>: مُعَرِّفُ الفاتورَةِ الَّتي دُفِعَت بِه (والقَرارُ عَلى ما تَقولُه البَوّابَةُ حينَ نَسأَلُها عَنها)،
/// ومُعَرِّفُ الدَفعِ لِلوغِ وَحدَه.</para>
/// <para><b>ولا يُبنى إلّا بِبَوّابَتِه</b>: المُنشِئُ داخِلِيٌّ و<see cref="GatewayNoticeGate.Admit"/> بابُه الوَحيد، و<see cref="GatewayPayments.ConfirmNoticeAsync"/> يَطلُبُه
/// في تَوقيعِه — فَلا يُنادى التَأكيدُ بِلا الحارِس (القاعِدَة ٦)، عَلى نَسَقِ <see cref="OtoOrderStatusNotice"/>.</para>
/// </summary>
public sealed class GatewayPaymentNotice
{
    internal GatewayPaymentNotice(string invoiceId, string? paymentId)
    {
        InvoiceId = invoiceId;
        PaymentId = paymentId;
    }

    /// <summary>الفاتورَةُ الَّتي دُفِعَت — <c>data.invoice_id</c> بِصيغَةِ uuid.</summary>
    public string InvoiceId { get; }

    /// <summary>مُعَرِّفُ الدَفعِ كَما في الإشعار (<c>data.id</c>) — لِلوغِ وَحدَه، ولا يُبنى عَلَيه شَيء.</summary>
    public string? PaymentId { get; }
}

/// <summary>ما خَرَجَ مِن البَوّابَة: إشعارٌ مَقبولٌ أَو خَرقٌ — واحِدٌ لا اثنان.</summary>
public sealed record GatewayNoticeAdmission(GatewayPaymentNotice? Notice, GatewayNoticeViolation? Violation);

/// <summary>
/// <para><b>بَوّابَةُ إشعارِ الدَفع — دالَّةٌ نَقِيَّة</b>، بِالتَرتيبِ ولا يُقلَب: (١) لِلمَتجَرِ سِرُّ إشعارٍ — وإلّا لا يُقرَأُ الجِسمُ أَصلاً؛ (٢) الجِسمُ JSON بِكائِن؛
/// (٣) <b>السِرُّ يُطابِقُ بِزَمَنٍ ثابِتٍ قَبلَ أَيِّ حَقلٍ آخَر</b> (القاعِدَة ٦: التَخويلُ قَبلَ الحُقول — فَلا يَصيرُ «حَدَثٌ غَيرُ الدَفع» جَواباً لِمَن لا يَملِكُ السِرّ)؛
/// (٤) الحَدَثُ <see cref="ACommerce.Kit.Payments.MoyasarClient.PaymentPaidEvent"/>؛ (٥) الدَفعُ لِفاتورَة (<c>data.invoice_id</c> uuid).</para>
/// <para><b>والرَدُّ لِلبَوّابَة</b> (<see cref="StatusFor"/>): «لا سِرّ» ‏404، و«لا يُقرَأ» ‏400، و«السِرُّ لا يُطابِق» ‏401 — فَتُعيدُ البَوّابَةُ المُحاوَلَةَ كَما تَقولُ الوَثيقَة؛
/// و«حَدَثٌ آخَر» و«دَفعٌ بِلا فاتورَة» ‏200 — لا عَمَلَ لَنا فيهِما فَلا يُعادانِ عَبَثاً؛ والمَقبولُ ‏200 أَيّاً كانَ ما أَجابَت بِه البَوّابَةُ عَن الفاتورَة.</para>
/// </summary>
public static class GatewayNoticeGate
{
    /// <summary>لا سِرَّ إشعارٍ لِهذا المَتجَر — لا رَبطَ بِمِفتاح، أَو لَم يُسَجَّل إشعارُه، أَو لا خِزانَةَ تَفُكّ. لا يُقرَأُ الجِسم.</summary>
    public const string NoticeUnavailable = "notice_unavailable";

    /// <summary>جِسمٌ فارِغٌ أَو لَيسَ كائِنَ JSON.</summary>
    public const string NoticeUnreadable = "notice_unreadable";

    /// <summary><c>secret_token</c> غائِبٌ أَو لا يُطابِقُ سِرَّ المَتجَر.</summary>
    public const string SecretMismatch = "notice_secret_mismatch";

    /// <summary>حَدَثٌ غَيرُ <c>payment_paid</c> — يُقبَلُ ولا يُعمَلُ بِه.</summary>
    public const string NotPaymentPaid = "notice_not_payment_paid";

    /// <summary>دَفعٌ بِلا <c>data.invoice_id</c> يُقرَأُ uuid — لَيسَ مِن فاتورَةٍ فَتَحناها.</summary>
    public const string InvoiceMissing = "notice_invoice_missing";

    /// <summary>المَعجَمُ المُغلَق — خَمسَةٌ لا سادِسَ لَها.</summary>
    public static readonly IReadOnlyList<string> All =
        new[] { NoticeUnavailable, NoticeUnreadable, SecretMismatch, NotPaymentPaid, InvoiceMissing };

    public static GatewayNoticeAdmission Admit(string? secret, string? rawBody)
    {
        if (string.IsNullOrEmpty(secret))
            return Refused(NoticeUnavailable, "لا سِرَّ إشعارٍ لِهذا المَتجَر — لا وارِدَ لَه.");
        if (string.IsNullOrWhiteSpace(rawBody))
            return Refused(NoticeUnreadable, "إشعارٌ بِلا جِسم.");

        try
        {
            using var doc = JsonDocument.Parse(rawBody);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return Refused(NoticeUnreadable, "جِسمٌ لَيسَ كائِناً.");

            if (Text(root, "secret_token") is not { } token || !SameSecret(secret, token))
                return Refused(SecretMismatch, "سِرُّ الإشعارِ غائِبٌ أَو لا يُطابِقُ سِرَّ المَتجَر.");

            if (!string.Equals(Text(root, "type"), ACommerce.Kit.Payments.MoyasarClient.PaymentPaidEvent, StringComparison.Ordinal))
                return Refused(NotPaymentPaid, $"حَدَثٌ غَيرُ «{ACommerce.Kit.Payments.MoyasarClient.PaymentPaidEvent}».");

            if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object
                || Text(data, "invoice_id") is not { } invoice || !Guid.TryParse(invoice, out var invoiceId))
                return Refused(InvoiceMissing, "دَفعٌ بِلا مُعَرِّفِ فاتورَةٍ يُقرَأ.");

            return new(new GatewayPaymentNotice(invoiceId.ToString("D"), Text(data, "id")), null);
        }
        catch (JsonException)
        {
            return Refused(NoticeUnreadable, "جِسمٌ لَيسَ JSON.");
        }
    }

    /// <summary>شَكلُ المُصادِقِ المُوَحَّد (القاعِدَة ٤): الخَرقُ الأَوَّلُ — أَو لا شَيء.</summary>
    public static IReadOnlyList<GatewayNoticeViolation> Validate(string? secret, string? rawBody)
        => Admit(secret, rawBody).Violation is { } v ? new[] { v } : Array.Empty<GatewayNoticeViolation>();

    public static bool IsValid(string? secret, string? rawBody) => Validate(secret, rawBody).Count == 0;

    /// <summary>رَدُّ الوارِدِ لِلبَوّابَة — <c>null</c> لِلمَقبول.</summary>
    public static int StatusFor(string? code) => code switch
    {
        NoticeUnavailable => 404,
        NoticeUnreadable  => 400,
        SecretMismatch    => 401,
        _                 => 200,
    };

    /// <summary>مُقارَنَةٌ بِزَمَنٍ ثابِت — لا يَتَسَرَّبُ طولُ ما طابَقَ مِن السِرّ.</summary>
    private static bool SameSecret(string expected, string given)
        => CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(given));

    private static string? Text(JsonElement root, string name)
        => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static GatewayNoticeAdmission Refused(string code, string messageAr) => new(null, new(code, messageAr));
}
