using System.Text.Json;
using ACommerce.Kit.Auth;

namespace ACommerce.Templates.Customer.Marketplace.Services;

/// <summary>
/// <para><b>بابُ اشتِراكِ الإشعارات — ما يُقبَلُ ويُحفَظُ ويُطرَق</b> (‏2026-10-24، تَدقيقُ الإطلاق lens-security-12). كانَ الشَرطُ الوَحيدُ في
/// <c>POST /api/{slug}/push/subscribe</c> «عُنوانٌ غَيرُ فارِغ»، و<see cref="WebPushService.SendAsync"/> يَبني <c>POST</c> إلى كُلِّ صَفٍّ مَحفوظ —
/// فَمُستَخدِمٌ مُوَثَّقٌ واحِدٌ يَجعَلُ الخادِمَ يَطرُقُ ما شاءَ مِن داخِلِ شَبَكَتِه عِندَ كُلِّ إشعار، ويُنمي وَثيقَتَه بِلا حَدّ.</para>
///
/// <para><b>قَرارٌ واحِدٌ ومُستَهلِكان</b>: البابُ يَقبَلُ بِه، والمُرسِلُ لا يَطرُقُ صَفّاً قَديماً لا يَقبَلُه ويَمحوه كَالمُنتَهي. فَما حُفِظَ قَبلَ
/// اليَومِ لا يَبقى ثَغرَةً بَعدَه.</para>
///
/// <para><b>والمُضيفونَ حَقيقَةٌ تِقنِيَّةٌ لا رَقَمُ مُنتَج</b> — مَن يُعطي المُتَصَفِّحُ عُنوانَه في <c>pushManager.subscribe</c>:
/// <c>fcm.googleapis.com</c> (Chrome وما بُنِيَ عَلى Chromium)، و<c>*.push.services.mozilla.com</c> (Firefox)، و<c>*.push.apple.com</c> (Safari —
/// ونَصُّ Apple نَفسُه «اسمَح بِـ<c>*.push.apple.com</c>»)، و<c>*.notify.windows.com</c> (Edge، WNS). ومُتَصَفِّحٌ بِخِدمَةٍ أُخرى يُرَدُّ ‏400
/// ولا يُحفَظ — **يُقال**: الإضافَةُ سَطرٌ هُنا («أ» W8-2). ولَيسَت بَياناتِ مُستَأجِر: مُستَأجِرٌ يُوَسِّعُها يَفتَحُ الثَغرَةَ نَفسَها.</para>
/// </summary>
public static class PushSubscriptionDoor
{
    /// <summary>مُضيفٌ يُطابَقُ بِعَينِه.</summary>
    public static readonly IReadOnlyList<string> ExactHosts = ["fcm.googleapis.com"];

    /// <summary>مُضيفٌ يُطابَقُ بِذَيلِه، والنُقطَةُ الأُولى جُزءٌ مِنه: <c>evilpush.apple.com</c> لا يُطابِقُ <c>.push.apple.com</c>.</summary>
    public static readonly IReadOnlyList<string> HostSuffixes = [".push.services.mozilla.com", ".push.apple.com", ".notify.windows.com"];

    /// <summary>
    /// <para><b>أَحدَثُ عَشرَةِ اشتِراكاتٍ لِكُلِّ مُستَخدِم</b> — والأَقدَمُ يَسقُطُ ولا يُرَدُّ الجَديد، فَجِهازٌ جَديدٌ يَدخُلُ دائِماً. **والعَدَدُ
    /// حِصَّةُ السّاعَةِ في عُدَّةِ الدُخولِ نَفسُها** (‏<c>AuthHandlers.MaxRequestPerHour</c> = ‏10) لا رَقَمٌ جَديد؛ وعَدَدُ الأَجهِزَةِ لِكُلِّ مُستَخدِمٍ
    /// قَرارُ المالِكِ يُبَدِّلُه هُنا («ب» W8-2).</para>
    /// </summary>
    public const int MaxPerUser = 10;

    /// <summary>‏<c>https</c> مُطلَقٌ بِمَنفَذِه الافتِراضِيِّ، بِلا مُستَخدِمٍ في العُنوان، ومُضيفُه اسمٌ (لا IP) مِن خِدماتِ الإشعار.</summary>
    public static bool IsPushServiceEndpoint(string? endpoint)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme != Uri.UriSchemeHttps || !uri.IsDefaultPort || uri.UserInfo.Length > 0) return false;
        if (uri.HostNameType != UriHostNameType.Dns) return false;
        var host = uri.IdnHost.ToLowerInvariant();
        return ExactHosts.Contains(host) || HostSuffixes.Any(s => host.EndsWith(s, StringComparison.Ordinal));
    }

    /// <summary>مَفتاحا المُتَصَفِّحِ بِشَكلِهِما: <c>p256dh</c> نُقطَةُ P-256 غَيرُ مَضغوطَة (‏65 بايتاً تَبدَأُ بِـ<c>0x04</c>)، و<c>auth</c> ‏16 بايتاً —
    /// ‏base64url كَما يُعطيهِما <c>PushSubscription.toJSON()</c>، أَو base64 بِحَشوِه.</summary>
    public static bool AreBrowserKeys(string? p256dh, string? auth)
        => Decoded(p256dh) is { Length: 65 } point && point[0] == 0x04 && Decoded(auth) is { Length: 16 };

    /// <summary>القَرارُ الواحِدُ عَلى صَفٍّ كامِل — يَسأَلُه البابُ قَبلَ الحِفظِ والمُرسِلُ قَبلَ الطَرق.</summary>
    public static bool Admits(PushSubscription sub)
        => IsPushServiceEndpoint(sub.Endpoint) && AreBrowserKeys(sub.P256dh, sub.Auth);

    /// <summary>يَقرَأُ جِسمَ الطَلَبِ اشتِراكاً يَقبَلُه البابُ، أَو <c>null</c> — لِـJSON مُعطوبٍ أَو حَقلٍ ناقِصٍ أَو مِن غَيرِ نَوعِه (كانَ ‏500) ولِما
    /// لا يَقبَلُه <see cref="Admits"/>.</summary>
    public static async Task<PushSubscription?> ReadAsync(Stream body, DateTime now, CancellationToken ct = default)
    {
        try
        {
            using var doc = await JsonDocument.ParseAsync(body, cancellationToken: ct);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("keys", out var keys) || keys.ValueKind != JsonValueKind.Object)
                return null;
            var sub = new PushSubscription
            {
                Endpoint = Text(root, "endpoint") ?? "", P256dh = Text(keys, "p256dh") ?? "", Auth = Text(keys, "auth") ?? "",
                CreatedAt = now,
            };
            return Admits(sub) ? sub : null;
        }
        catch (JsonException) { return null; }
    }

    /// <summary>يَضَعُ الاشتِراكَ في قائِمَةِ المُستَخدِم: يَحِلُّ مَحَلَّ صَفِّ العُنوانِ نَفسِه (الجِهازُ نَفسُه) ويُلحَقُ آخِراً، ثُمَّ يُبقي أَحدَثَ
    /// <see cref="MaxPerUser"/>. والقائِمَةُ مُرَتَّبَةٌ بِالإلحاقِ — كاتِبُها الوَحيدُ هذا، والمُرسِلُ يَمحو ولا يُعيدُ التَرتيب — فَأَوَّلُها أَقدَمُها.</summary>
    public static void Keep(List<PushSubscription> list, PushSubscription sub)
    {
        list.RemoveAll(p => p.Endpoint == sub.Endpoint);
        list.Add(sub);
        if (list.Count > MaxPerUser) list.RemoveRange(0, list.Count - MaxPerUser);
    }

    private static string? Text(JsonElement o, string name)
        => o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static byte[]? Decoded(string? b64)
    {
        if (string.IsNullOrEmpty(b64)) return null;
        var s = b64.Replace('-', '+').Replace('_', '/');
        s = s.PadRight(s.Length + (4 - s.Length % 4) % 4, '=');
        try { return Convert.FromBase64String(s); }
        catch (FormatException) { return null; }
    }
}
