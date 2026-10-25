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

    // ‏(‏2026-10-25، تَدقيقُ الإطلاق lens-identity-rules-1 — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه) كانَ هُنا `OfferedCapabilities(vault)` — «القُدُراتُ الَّتي
    // تُرسَم» — وفَقَدَ مُنادِيَيه في الشاشَةِ حينَ صارَت الأَقسامُ `SectionCapabilities` (‏5cb891e)، فَبَقِيَ لِصَفَّينِ في الاختِباراتِ وَحدَهُما
    // (القاعِدَة ١). وجَوابُه هُوَ جَوابُ `SectionCapabilities(vault, _ => false)` بِالبِناء — `BindingCapabilities` هِيَ المَعجَمُ مُرَشَّحاً بِما
    // يَربِطُه مُستَأجِر، و`Offered` جُزءٌ مِنه — فَصارَ الصَفّانِ يَسأَلانِ الدالَّةَ الَّتي تَقرَؤُها الشاشَة. يَعودُ يَومَ يُناديه كودُ تَشغيل.

    /// <summary>
    /// <para><b>القُدُراتُ الَّتي يُقرَأُ رَبطُها القائِم</b> — ما يَربِطُه مُستَأجِرٌ في الكاتالوج، بِلا سُؤالٍ عَن الخِزانَة (‏2026-10-13، مُراجَعَةُ المَوجَة 3a —
    /// G4 (xxii)): رَبطٌ مَختومٌ قائِمٌ يُقرَأُ ولَو غابَ المِفتاحُ الرَئيس — يُرسَمُ ذَيلُه ويُسحَبُ ولا يُفَكّ.</para>
    /// </summary>
    public static IReadOnlyList<string> BindingCapabilities => ProviderCatalog.BindableCapabilities;

    /// <summary>
    /// <para><b>أَقسامُ الشاشَة</b> (‏2026-10-13، مُراجَعَةُ المَوجَة 3a — G4 (xxii)؛ تَوصيفُه <c>Today_the_studio_providers_page_draws_and_loads_…</c>):
    /// قُدرَةٌ لَها بِطاقَةٌ مَعروضَةٌ (<see cref="Offered"/>) <b>أَو</b> رَبطٌ قائِم (<paramref name="hasBinding"/>) — بِتَرتيبِ المَعجَم. كانَت الأَقسامُ
    /// <c>OfferedCapabilities</c> وَحدَها (حُذِفَت ‏2026-10-25)، فَقُدرَةٌ كُلُّ مُزَوِّديها يَحتاجونَ الخِزانَةَ تَختَفي بِرَبطِها المَختومِ وزِرِّ سَحبِه حينَ يُزالُ المِفتاحُ
    /// الرَئيس. فَالخِزانَةُ تُخفي البِطاقاتِ وَحدَها (القاعِدَة ١٢: لا بِطاقَةَ تُرَدّ)، والرَبطُ القائِمُ يَبقى مَرئِيّاً مَسحوباً بِحَقِّه. وقُدرَةٌ بِلا هذا ولا ذاك
    /// لا تُرسَمُ كَما كانَت — لا قِسمَ فارِغاً يَقولُ «قَريباً».</para>
    /// </summary>
    public static IReadOnlyList<string> SectionCapabilities(ProviderVault? vault, Func<string, bool> hasBinding)
        => Sections(BindingCapabilities, c => Offered(c, vault).Count > 0, hasBinding);

    /// <summary>القاعِدَةُ نَفسُها بِمُدخَلاتٍ صَريحَة — لِأَنّ كاتالوجَ اليَومِ لا يَحمِلُ قُدرَةً كُلُّ مُزَوِّديها مَختومون («مَدفوعات» يُبقيها الرابِطُ
    /// بِلا خِزانَة)، فَشَطرُ «أَو رَبطٌ قائِم» لا يُقاسُ بِالكاتالوجِ وَحدَه؛ يُقاسُ هُنا بِقُدرَةٍ مَصنوعَةٍ في الصَفّ (القاعِدَة ٢).</summary>
    public static IReadOnlyList<string> Sections(IEnumerable<string> capabilities, Func<string, bool> offered, Func<string, bool> bound)
        => capabilities.Where(c => offered(c) || bound(c)).ToArray();

    /// <summary><b>نَوعُ حَقلِ الإدخال</b> — <c>password</c> لِما تَختِمُه الخِزانَة،
    /// فَلا يُرى السِرُّ عَلى الشاشَةِ وهُوَ يُلصَق (‏ADR-032 الخُطوَة 7)؛ و<c>null</c>
    /// لِما سِواه فَلا يُصَيَّرُ السِمَةُ أَصلاً — وصورَةُ حَقلِ الرابِطِ بايتاً
    /// بِبايتٍ كَما كانَت.</summary>
    public static string? InputType(ProviderFieldDefinition f)
        => ProviderVault.Seals(f.Kind) ? "password" : null;

    /// <summary>ولا يُقتَرَحُ لِحَقلِ السِرِّ ما حَفِظَه المُتَصَفِّح — و<c>null</c>
    /// لِما سِواه بِنَفسِ الحُجَّة.
    /// <para><b><c>new-password</c> لا <c>off</c></b> (‏2026-10-13، مُراجَعَةُ المَوجَة 3a — G4 (xviii)؛ ولا يُعادُ حَرفٌ فَوقَ ما سَبَق): المُتَصَفِّحاتُ تَتَجاهَلُ
    /// <c>off</c> في حَقلِ <c>type="password"</c> — فَتَملَؤُه بِكَلِمَةِ مُرورٍ مَحفوظَةٍ لِأَصلِ وَسايِل (لِمالِكِ المَتجَرِ في الاستوديو)، وتَعرِضُ بَعدَ «اربط»
    /// حِفظَ مِفتاحِ التاجِرِ كَلِمَةَ مُرورٍ لِلأَصلِ نَفسِه؛ و<c>new-password</c> تَقولُ «لَيسَت كَلِمَةَ مُرورٍ مَحفوظَة». والرابِطُ بِلا سِمَةٍ بايتاً بِبايتٍ كَما كان.
    /// وما لَم يُقَس — ويُقال: سُلوكُ كُلِّ مُتَصَفِّحٍ بِعَينِه (سِجِلُّ «ب» S6·١٢ — قِياسُه بِمُتَصَفِّحٍ حَقيقِيٍّ مَكتوبٌ هُناك).</para></summary>
    public static string? InputAutocomplete(ProviderFieldDefinition f)
        => ProviderVault.Seals(f.Kind) ? "new-password" : null;

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

    // ‏(‏2026-10-25 — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه) كانَ هُنا الحِملُ الثُنائِيُّ `FromForm(slug, field)` «بِلا خِزانَة» — بِلا مُستَهلِكٍ وَقتَ
    // تَشغيلٍ مُنذُ صارَت النُقطَةُ تُنادي الرُباعِيَّةَ بِالخِزانَةِ المُسَجَّلَة (‏ADR-032 الخُطوَة 7)، وبَقِيَ بِشَرطِ حَذفٍ مَكتوبٍ هُنا: «يُحذَفُ يَومَ يُمَسُّ
    // أَوَّلُ تِلكَ الصُفوفِ لِسَبَبٍ آخَر». ومَسَّ ‏0c09278 أَوَّلَها (حَذفُ `OfferedCapabilities`)، فَحُذِفَ وصارَت صُفوفُه تُنادي الرُباعِيَّةَ
    // بِـ`tenantSlug: "", vault: null` بِحَرفِ ما كانَ يُمَرِّرُه — فَتَقيسُ الكودَ نَفسَه بِلا حِملٍ بَينَهُما.

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
