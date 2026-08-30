using ACommerce.Platform.Providers;
using Microsoft.AspNetCore.Http;

namespace ACommerce.Templates.Customer.Marketplace.Services.Providers;

/// <summary>
/// <para><b>قِراءَةُ نَموذَجِ الرَبطِ وفَحصُه</b> — نَظيرُ
/// <c>ApiKeySurface</c> حَرفاً، ولِنَفسِ السَبَب: جِسمُ النُقطَةِ لا
/// يُختَبَر، والقَرارُ الَّذي يَسكُنُه يَنزِف. فَالقَرارُ هُنا،
/// والنُقطَةُ أَثَرُه.</para>
///
/// <para><b>والسِياجُ يُفرَض عِندَ الكِتابَة</b>: قيمَةُ كُلِّ حَقلٍ
/// تَمُرّ مِن <see cref="ProviderValueValidator"/> بِتَعريفِ حَقلِها —
/// فَقائِمَةُ المُضيفينَ في المِلَفِّ شَرطٌ يُرَدُّ بِه الطَلَب، لا
/// وَصفٌ يُقرَأ.</para>
/// </summary>
public static class ProviderBindSurface
{
    /// <summary>نَتيجَةُ القِراءَة: إمّا رَمزُ رَفضٍ، وإمّا رَبطٌ
    /// جاهِزٌ لِلكِتابَة.</summary>
    public sealed record Read(
        string? RefusalCode,
        ProviderDefinition? Definition,
        IReadOnlyDictionary<string, StoredValue> Values)
    {
        /// <summary>هَل يَحتاجُ هذا الطَلَبُ حارِسَ المَنَصَّةِ فَوقَ
        /// حارِسِ المَتجَر؟ — <c>platform_key</c> يَصرِفُ مِن جَيبِنا،
        /// ويُسأَلُ عَنه <b>قَبلَ</b> أَن يُقالَ إنَّ التَعريفَ
        /// غَيرُ قابِلٍ لِلرَبط.</summary>
        public bool NeedsPlatformAdmin =>
            Definition?.Credential.Kind == CredentialKinds.PlatformKey;

        public string Capability => Definition?.Capability ?? "";
        public string ProviderSlug => Definition?.Slug ?? "";
    }

    public const string ProviderUnknown = "provider_unknown";
    public const string ProviderNotBindable = "provider_not_bindable";

    private static readonly IReadOnlyDictionary<string, StoredValue> NoValues =
        new Dictionary<string, StoredValue>(0, StringComparer.Ordinal);

    public static Read FromForm(HttpRequest req)
        => FromForm(req.Form["provider"].ToString().Trim(),
                    code => req.Form[code].ToString());

    /// <summary>النِصفُ النَقِيُّ — بِلا <c>HttpRequest</c>، فَعَلَيه
    /// تَقَعُ الوَحَدات.</summary>
    public static Read FromForm(string providerSlug, Func<string, string?> field)
    {
        var def = ProviderCatalog.Find(providerSlug);
        if (def is null) return new(ProviderUnknown, null, NoValues);

        // النَوعُ يُعلَنُ قَبلَ الرَفض: نُقطَةُ الكِتابَةِ تَسأَلُ
        // `PlatformAdminGuard` عَلى هذا الأَساس.
        if (!def.IsTenantBindable) return new(ProviderNotBindable, def, NoValues);

        var values = new Dictionary<string, StoredValue>(StringComparer.Ordinal);

        foreach (var f in def.Credential.Fields)
        {
            var raw = (field(f.Code) ?? "").Trim();
            var refusal = ProviderValueValidator.Refuse(f, raw);
            if (refusal is not null) return new(refusal, def, NoValues);
            if (raw.Length == 0) continue;
            values[f.Code] = StoredValue.Explicit(f.Kind, raw);
        }

        return new(null, def, values);
    }

    /// <summary><b>سَطرُ التَدقيق — مُعَتَّمٌ بِالبِناء لا
    /// بِالنِيَّة.</b> القيمَةُ تَمُرّ مِن <c>ProviderSecrecy</c> قَبلَ
    /// أَن تَبلُغَ حَقلَ <c>Before</c>/<c>After</c>، ونَوعُها هُوَ
    /// الَّذي يُقَرِّرُ أَتُعرَض أَم تُقَنَّع.</summary>
    public static string AuditLine(TenantProviderBinding? b)
    {
        if (b is null) return "provider=-; status=-";

        return ProviderSecrecy.ForAudit(
            b.ProviderSlug, b.Status,
            b.Values.Select(kv => new KeyValuePair<string, string>(kv.Key, kv.Value.Kind)),
            code => b.Values.TryGetValue(code, out var v) ? v.Plain : null);
    }
}
