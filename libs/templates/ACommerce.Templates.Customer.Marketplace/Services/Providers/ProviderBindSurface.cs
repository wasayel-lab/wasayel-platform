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
    /// السَطحَ أَو بَلَغَتهُ بِلا مِفتاح (‏<c>Providers__Kek__v1</c> غائِبٌ أَو تالِف).
    /// ويُقرَأُ في الشاشَةِ رِسالَةً مِن القامُوس.</summary>
    public const string VaultUnavailable = ProviderVault.Unavailable;

    /// <summary>أَيَحمِلُ هذا التَعريفُ حَقلاً تَختِمُه الخِزانَة؟ — مُشتَقٌّ مِن
    /// <c>ProviderVault.Seals</c> لا مِن قائِمَةِ سلاجات (الكاتالوجُ بَيانات).</summary>
    public static bool NeedsVault(ProviderDefinition d)
        => d.Credential.Fields.Any(f => ProviderVault.Seals(f.Kind));

    /// <summary>أَتَختِمُ هذِه الخِزانَةُ فِعلاً؟ — و<c>null</c> «لا خِزانَة»: الفَشَلُ
    /// المُغلَقُ هُوَ الافتِراض.</summary>
    private static bool CanSeal(ProviderVault? vault) => vault is { IsAvailable: true };

    /// <summary>
    /// <para><b>ما تَعرِضُه الشاشَةُ لِلرَبط</b> — ما يَربِطُه مُستَأجِرٌ في
    /// الكاتالوج، <b>إلّا</b> ما يَحتاجُ خِزانَةً لا تَختِمُ (غائِبَةً عَن السَطحِ أَو
    /// بِلا مِفتاحٍ رَئيس — ‏ADR-032 الخُطوَة 11: «غِيابُ KEK يُخفي بِطاقَةَ المُزَوِّد»).
    /// فَلا تُرسَمُ بِطاقَةٌ يَرُدُّها <see cref="FromForm(string, Func{string, string?}, string, ProviderVault?)"/>
    /// — ولا «قَريباً» (القاعِدَة ١٢). والشاشَةُ والنُقطَةُ تَقرَآنِ المُسنَدَ نَفسَه
    /// (<see cref="NeedsVault"/> مَعَ حالِ الخِزانَة).</para>
    /// <para><b>ولِماذا هُنا لا في <c>ProviderCatalog.TenantBindable</c></b>: ذاكَ
    /// سُؤالُ البَيانات («أَيَملَؤُه مُستَأجِر؟»)، وهذا سُؤالُ السَطح («أَيَستَطيعُ
    /// هذا السَطحُ خَتمَ ما يُملَأ؟»). خَلطُهُما يُغَيِّرُ جَوابَ الكاتالوجِ لِكُلِّ
    /// قارِئٍ بِحالِ شاشَة.</para>
    /// </summary>
    public static IReadOnlyList<ProviderDefinition> Offered(string capability, ProviderVault? vault = null)
        => ProviderCatalog.TenantBindable(capability)
            .Where(d => !NeedsVault(d) || CanSeal(vault)).ToArray();

    /// <summary>القُدُراتُ الَّتي تُرسَم — بِتَرتيبِ المَعجَمِ، وقُدرَةٌ بِلا مُزَوِّدٍ
    /// مَعروضٍ لا تُرسَمُ إطلاقاً.</summary>
    public static IReadOnlyList<string> OfferedCapabilities(ProviderVault? vault = null)
        => ProviderCapabilities.All.Where(c => Offered(c, vault).Count > 0).ToArray();

    /// <summary><b>نَوعُ حَقلِ الإدخال</b> — <c>password</c> لِما تَختِمُه الخِزانَة،
    /// فَلا يُرى السِرُّ عَلى الشاشَةِ وهُوَ يُلصَق (‏ADR-032 الخُطوَة 7)؛ و<c>null</c>
    /// لِما سِواه فَلا يُصَيَّرُ السِمَةُ أَصلاً — وصورَةُ حَقلِ الرابِطِ بايتاً
    /// بِبايتٍ كَما كانَت.</summary>
    public static string? InputType(ProviderFieldDefinition f)
        => ProviderVault.Seals(f.Kind) ? "password" : null;

    /// <summary>ولا يُقتَرَحُ لِحَقلِ السِرِّ ما حَفِظَه المُتَصَفِّح — و<c>null</c>
    /// لِما سِواه بِنَفسِ الحُجَّة.</summary>
    public static string? InputAutocomplete(ProviderFieldDefinition f)
        => ProviderVault.Seals(f.Kind) ? "off" : null;

    /// <summary><b>سِياقُ سِرِّ رَبطِ المَتجَر</b> — <c>tenant|capability|field</c>
    /// (‏ADR-032 الخُطوَة 4). يَبنيه الخاتِمُ هُنا، ويَبنيه بِالدالَّةِ نَفسِها مَن
    /// يَفُكُّ غَداً (عَميلُ البَوّابَة، الخُطوَة 8) — فَلا يَنجَرِفُ شَكلان.</summary>
    public static string AadFor(string tenantSlug, string capability, string fieldCode)
        => ProviderVault.AadOf(tenantSlug, capability, fieldCode);

    private static readonly IReadOnlyDictionary<string, StoredValue> NoValues =
        new Dictionary<string, StoredValue>(0, StringComparer.Ordinal);

    /// <summary>قِراءَةُ النَموذَجِ في النُقطَة — بِمُستَأجِرِها (سِياقُ الخَتم)
    /// وبِالخِزانَةِ المُسَجَّلَة.</summary>
    public static Read FromForm(HttpRequest req, string tenantSlug, ProviderVault vault)
        => FromForm(req.Form["provider"].ToString().Trim(),
                    code => req.Form[code].ToString(), tenantSlug, vault);

    /// <summary>النِصفُ النَقِيُّ <b>بِلا خِزانَة</b> — ما يَحتاجُها يُرَدُّ
    /// بِـ<see cref="VaultUnavailable"/>. الفَشَلُ المُغلَقُ هُوَ الافتِراض.</summary>
    public static Read FromForm(string providerSlug, Func<string, string?> field)
        => FromForm(providerSlug, field, tenantSlug: "", vault: null);

    /// <summary>النِصفُ النَقِيُّ — بِلا <c>HttpRequest</c>، فَعَلَيه
    /// تَقَعُ الوَحَدات.</summary>
    public static Read FromForm(
        string providerSlug, Func<string, string?> field, string tenantSlug, ProviderVault? vault)
    {
        var def = ProviderCatalog.Find(providerSlug);
        if (def is null) return new(ProviderUnknown, null, NoValues);

        // النَوعُ يُعلَنُ قَبلَ الرَفض: نُقطَةُ الكِتابَةِ تَسأَلُ
        // `PlatformAdminGuard` عَلى هذا الأَساس.
        if (!def.IsTenantBindable) return new(ProviderNotBindable, def, NoValues);

        // ومُزَوِّدٌ حَقلُه سِرٌّ تَختِمُه الخِزانَة يُرَدُّ **قَبلَ قِراءَةِ أَوَّلِ
        // حَقل** حينَ لا خِزانَةَ تَختِم (غائِبَةً أَو بِلا مِفتاح) — فَلا يَبلُغُ
        // السِرُّ `StoredValue.Explicit` الَّتي تَرميه، ولا يُكتَبُ صَريحاً بِحال.
        if (NeedsVault(def) && !CanSeal(vault)) return new(VaultUnavailable, def, NoValues);

        var values = new Dictionary<string, StoredValue>(StringComparer.Ordinal);

        foreach (var f in def.Credential.Fields)
        {
            var raw = (field(f.Code) ?? "").Trim();
            var refusal = ProviderValueValidator.Refuse(f, raw);
            if (refusal is not null) return new(refusal, def, NoValues);
            if (raw.Length == 0) continue;

            // السِرُّ يُختَمُ هُنا ولا يَخرُجُ مِن هذِه الحَلقَةِ صَريحاً — لا في
            // `Read` ولا في سَطرِ التَدقيقِ ولا في الرَدّ.
            values[f.Code] = ProviderVault.Seals(f.Kind)
                ? StoredValue.Sealed(vault!, f.Kind, raw, AadFor(tenantSlug, def.Capability, f.Code))
                : StoredValue.Explicit(f.Kind, raw);
        }

        return new(null, def, values);
    }

    /// <summary><b>سَطرُ التَدقيق — مُعَتَّمٌ بِالبِناء لا
    /// بِالنِيَّة.</b> القيمَةُ تَمُرّ مِن <c>ProviderSecrecy</c> قَبلَ
    /// أَن تَبلُغَ حَقلَ <c>Before</c>/<c>After</c>، ونَوعُها هُوَ
    /// الَّذي يُقَرِّرُ أَتُعرَض أَم تُقَنَّع. <b>ولِلمَختومِ ذَيلُه المَحفوظ</b>
    /// (مُنذُ ‏2026-10-12) — فَيُعرَفُ مِن السَطرِ أَيُّ مِفتاحٍ رُبِطَ أَو سُحِب،
    /// والصَريحُ بايتاً بِبايتٍ كَما كان.</summary>
    public static string AuditLine(TenantProviderBinding? b)
    {
        if (b is null) return "provider=-; status=-";

        return ProviderSecrecy.ForAudit(b.ProviderSlug, b.Status, b.Values);
    }
}
