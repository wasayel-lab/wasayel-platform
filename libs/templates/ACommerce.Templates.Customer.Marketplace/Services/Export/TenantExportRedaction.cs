using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using ACommerce.Platform.Providers;

namespace ACommerce.Templates.Customer.Marketplace.Services.Export;

/// <summary>حَقلٌ يُحذَفُ بِاسمِه — لا بِتَقدير.</summary>
public sealed record RedactedField(string TypeName, string Property, string WhyAr);

/// <summary>
/// <para><b>التَعتيمُ يَقَعُ على الصَفِّ قَبلَ أَن يُكتَب</b>، لا على
/// الاستِعلامِ ولا على العَرض. والحُقولُ تُحذَفُ <b>بِأَسمائِها
/// المُعلَنَة</b>، ولِكُلِّ اسمٍ سَبَبٌ يُقرَأ — فَقائِمَةٌ بِلا
/// أَسبابٍ تَصيرُ قائِمَةَ إسكاتٍ يَنجَرِفُ مُحتَواها.</para>
///
/// <para><b>والمِعيارُ الَّذي وَضَعَه صاحِبُ المَشروع</b>: ما لا يَنفَعُ
/// المُستَلِمَ ويَضُرُّ مُستَخدِميه إن تَسَرَّبَ — لا يَخرُج. ‏
/// <c>User.PushSubscriptions</c> مِثالُه الحَرفيّ: مِفتاحُ دَفعٍ حَيٌّ
/// <b>مَعقودٌ على زَوجِ VAPID واحِدٍ لِلمَنَصَّةِ كُلِّها</b>، فَلا
/// يَستَطيعُ المُستَلِمُ استِعمالَه أَصلاً، ويَتَضَرَّرُ مُستَخدِموه
/// إن تَسَرَّب.</para>
///
/// <para><b>وأَعمِدَةُ الظَرفِ تُحذَفُ اليَومَ وهي فارِغَة</b>
/// (‏<c>Cipher</c>, <c>Nonce</c>, <c>KekVersion</c>): يَومَ تُشحَنُ
/// الخِزانَةُ يَبدَأُ مُصَدِّرٌ قائِمٌ بِإخراجِ نَصٍّ مُعَمّىً تَحتَ
/// مِفتاحٍ رَئيسٍ تَملِكُه المَنَصَّةُ، ومَعَه رَقمُ تَدويرِ
/// مَفاتيحِنا — <b>بِلا سَطرٍ يَتَغَيَّرُ في التَصدير</b>، فَلا شَيءَ
/// يُنَبِّه. الاستِثناءُ يُكتَبُ اليَومَ لا يَومَها.</para>
/// </summary>
public static class TenantExportRedaction
{
    /// <summary>خِياراتُ التَصيير — أَسماءٌ بِحَرفٍ صَغيرٍ أَوَّلاً
    /// (‏camelCase) لِتُقرَأَ في أَيِّ أَداة، وبِلا هُروبِ محارِفَ
    /// غَيرِ لاتينِيَّةٍ فَتَبقى العَرَبِيَّةُ مَقروءَةً في المِلَفّ.</summary>
    public static JsonSerializerOptions Json { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false,
    };

    /// <summary>وَثيقَةٌ واحِدَةٌ إلى صَفٍّ خام — <b>بِنَوعِها وَقتَ
    /// التَشغيلِ لا بِـ<c>object</c></b>، وإلّا صُيِّرَت فارِغَة.</summary>
    public static JsonObject ToJson(object document)
        => JsonSerializer.SerializeToNode(document, document.GetType(), Json)!.AsObject();

    // ─── ما يُحذَفُ بِاسمِه ────────────────────────────────────────

    public static IReadOnlyList<RedactedField> Fields { get; } = new RedactedField[]
    {
        new("User", nameof(ACommerce.Kit.Auth.User.PushSubscriptions),
            "اعتِمادٌ حامِل: مَن مَلَكَه دَفَعَ إشعاراً إلى مُتَصَفِّحِ ذلكَ " +
            "الشَخصِ بِعَينِه. ومَعقودٌ على زَوجِ VAPID واحِدٍ لِلمَنَصَّة، " +
            "فَلا يَنفَعُ المُستَلِمَ ويَضُرُّ مُستَخدِميه."),

        new("AuditEntry", nameof(Audit.AuditEntry.Ip),
            "عُنوانٌ شَخصيّ، ومِنه عَناوينُ مُشرِفي المَنَصَّةِ حينَ " +
            "يَتَصَرَّفونَ على مَتجَر — أَثَرُنا نَحنُ في قِسمِ العَميل."),

        new("AuditEntry", nameof(Audit.AuditEntry.UserAgent),
            "بَصمَةُ جِهازٍ ومُتَصَفِّح — بَياناتٌ شَخصِيَّةٌ لا يَحتاجُها " +
            "المُستَلِمُ لِيَقرَأَ مَن فَعَلَ ماذا."),

        new("ConsentRecord", nameof(Incubator.ConsentRecord.Ip),
            "نَفسُ حُجَّةِ قَيدِ التَدقيقِ حَرفاً: العُنوانُ لا يُضيفُ إلى " +
            "أَثَرِ المُوافَقَةِ شَيئاً، ويُضيفُ إلى ما يُفقَدُ إن تَسَرَّب."),

        new("ConsentRecord", nameof(Incubator.ConsentRecord.UserAgent),
            "نَفسُ حُجَّةِ قَيدِ التَدقيقِ حَرفاً — بَصمَةُ جِهازٍ لا " +
            "يَحتاجُها المُستَلِم."),
    };

    /// <summary>
    /// <para><b>مُؤَشِّراتُ اعتِمادٍ تُرفَضُ أَينَما وَقَعَت</b> —
    /// ولَو في عُمقِ الوَثيقَة. وهذِه هي الشَبَكَةُ الأَخيرَةُ تَحتَ
    /// قائِمَةِ الحُقول: حَقلٌ يُضافُ غَداً إلى نَوعٍ يَخرُجُ اليَومَ
    /// لا يَمُرُّ صامِتاً.</para>
    /// </summary>
    public static IReadOnlyList<string> ForbiddenAnywhere { get; } = new[]
    {
        "secretHash",        // تَجزئَةُ مِفتاح API
        "pushSubscriptions", // اعتِمادُ دَفعٍ حَيّ
        "p256dh",            // مِفتاحُ تَعمِيَةِ الدَفع
        "cipher", "nonce", "kekVersion", "plain",  // أَعمِدَةُ ظَرفِ الخِزانَة
    };

    /// <summary>
    /// <para><b>جَداوِلُ الاستِيرادِ المَحجوبَة</b> — الصَفُّ فيها
    /// مَنقولٌ بِـ<c>SELECT *</c> مِن قاعِدَةٍ سابِقَةٍ بِأَعمِدَةٍ لَم
    /// يَقرَأها أَحَدٌ مِنّا، وهاتانِ بِعَينِهِما تَحمِلانِ <b>رُموزَ
    /// أَجهِزَةٍ حَيَّة</b>. والحَجبُ بِالجَدوَلِ لا بِالعَمود، لِأَنّ
    /// الأَعمِدَةَ غَيرُ مَعروفَة.</para>
    /// </summary>
    public static IReadOnlyList<string> WithheldImportTables { get; } = new[]
    {
        "DeviceTokens", "UserPushTokens",
    };

    /// <summary>الفاعِلُ الَّذي يَكتُبُ أَثَرَ فَوتَرَةِ المَنَصَّةِ في
    /// قِسمِ المُستَأجِر — قَيدُه أَثَرُنا لا أَثَرُ العَميل.</summary>
    private static readonly string[] PlatformBillingActors = { "paypal ·", "paddle ·" };

    // ─── التَطبيق ─────────────────────────────────────────────────

    /// <summary>
    /// <para>الصَفُّ كَما يَجوزُ أَن يَخرُج — أَو <c>null</c> إن كانَ
    /// الصَفُّ كُلُّه يُحجَب.</para>
    /// </summary>
    public static JsonObject? Apply(string typeName, JsonObject row)
    {
        // ١) صُفوفٌ تُحجَبُ كامِلَةً.
        switch (typeName)
        {
            case "AuditEntry":
                var actor = row["actorName"]?.GetValue<string>() ?? "";
                if (PlatformBillingActors.Any(p => actor.StartsWith(p, StringComparison.Ordinal)))
                    return null;
                break;

            case "ImportedRecord":
                // المُعَرِّف «‏{Table}/{SourceId}» — الجُزءُ الأَوَّلُ اسمُ الجَدوَل.
                var id = row["id"]?.GetValue<string>() ?? "";
                var slash = id.IndexOf('/');
                var table = slash > 0 ? id[..slash] : id;
                if (WithheldImportTables.Contains(table, StringComparer.OrdinalIgnoreCase))
                    return null;
                break;
        }

        // ٢) حُقولٌ تُحذَفُ بِاسمِها.
        foreach (var f in Fields.Where(f => f.TypeName == typeName))
            RemoveIgnoringCase(row, f.Property);

        // ٣) رَبطُ المُزَوِّد: القيمَةُ تُعاد بِناؤُها مُعَتَّمَةً
        //    بِالبِناء. **ولا يُمَرَّرُ الصَفُّ خاماً**: التَصييرُ
        //    يَنسَخُ الحَقلَ كَما هُوَ ولا يَعرِف حَقلاً مِن حَقل.
        if (typeName == "TenantProviderBinding" && row["values"] is JsonObject values)
        {
            var censored = new JsonObject();
            foreach (var (field, node) in values)
            {
                var kind = node?["kind"]?.GetValue<string>() ?? CredentialKinds.None;
                var plain = node?["plain"]?.GetValue<string>();
                censored[field] = new JsonObject
                {
                    ["kind"] = kind,
                    ["value"] = ProviderSecrecy.Censor(kind, plain),
                };
            }
            row["values"] = censored;
        }

        return row;
    }

    private static void RemoveIgnoringCase(JsonObject row, string property)
    {
        var key = row.Select(p => p.Key)
            .FirstOrDefault(k => string.Equals(k, property, StringComparison.OrdinalIgnoreCase));
        if (key is not null) row.Remove(key);
    }
}
