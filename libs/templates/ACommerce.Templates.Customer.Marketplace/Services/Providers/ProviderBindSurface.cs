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

    /// <summary>رَمزُ الخِزانَةِ نَفسُه (<c>ProviderVault.Unavailable</c>) لا نَسخَةٌ
    /// تَنجَرِف: مُزَوِّدٌ حَقلُه سِرٌّ تَختِمُه الخِزانَة، والخِزانَةُ لا تَبلُغُ هذا
    /// السَطح.</summary>
    public const string VaultUnavailable = ProviderVault.Unavailable;

    /// <summary>أَيَحمِلُ هذا التَعريفُ حَقلاً تَختِمُه الخِزانَة؟ — مُشتَقٌّ مِن
    /// <c>ProviderVault.Seals</c> لا مِن قائِمَةِ سلاجات (الكاتالوجُ بَيانات).</summary>
    public static bool NeedsVault(ProviderDefinition d)
        => d.Credential.Fields.Any(f => ProviderVault.Seals(f.Kind));

    /// <summary>
    /// <para><b>ما تَعرِضُه الشاشَةُ لِلرَبط</b> — ما يَربِطُه مُستَأجِرٌ في
    /// الكاتالوج، <b>إلّا</b> ما يَحتاجُ خِزانَةً لا تَبلُغُ هذا السَطح. فَلا تُرسَمُ
    /// بِطاقَةٌ يَرُدُّها <see cref="FromForm(string, Func{string, string?})"/> —
    /// ولا «قَريباً» (القاعِدَة ١٢). والشاشَةُ والنُقطَةُ تَقرَآنِ المُسنَدَ نَفسَه
    /// (<see cref="NeedsVault"/>).</para>
    /// <para><b>ولِماذا هُنا لا في <c>ProviderCatalog.TenantBindable</c></b>: ذاكَ
    /// سُؤالُ البَيانات («أَيَملَؤُه مُستَأجِر؟»)، وهذا سُؤالُ السَطح («أَيَستَطيعُ
    /// هذا السَطحُ خَتمَ ما يُملَأ؟»). خَلطُهُما يُغَيِّرُ جَوابَ الكاتالوجِ لِكُلِّ
    /// قارِئٍ بِحالِ شاشَة.</para>
    /// </summary>
    public static IReadOnlyList<ProviderDefinition> Offered(string capability)
        => ProviderCatalog.TenantBindable(capability).Where(d => !NeedsVault(d)).ToArray();

    /// <summary>القُدُراتُ الَّتي تُرسَم — بِتَرتيبِ المَعجَمِ، وقُدرَةٌ بِلا مُزَوِّدٍ
    /// مَعروضٍ لا تُرسَمُ إطلاقاً.</summary>
    public static IReadOnlyList<string> OfferedCapabilities =>
        ProviderCapabilities.All.Where(c => Offered(c).Count > 0).ToArray();

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

        // ومُزَوِّدٌ حَقلُه سِرٌّ تَختِمُه الخِزانَة يُرَدُّ **قَبلَ قِراءَةِ أَوَّلِ
        // حَقل** ما دامَت الخِزانَةُ لا تَبلُغُ هذا السَطح — فَلا يَبلُغُ السِرُّ
        // `StoredValue.Explicit` الَّتي تَرميه، ولا يُكتَبُ صَريحاً بِحال.
        if (NeedsVault(def)) return new(VaultUnavailable, def, NoValues);

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
