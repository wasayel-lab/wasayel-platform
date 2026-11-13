using System.Text.RegularExpressions;

namespace ACommerce.Platform.Shared.Metering;

// ═══ العَدُّ الداخِلِيّ — مُعجَمُ الوَحَداتِ وأَدَواتُه المُشتَرَكَة (‏2026-11-12، الموجَة ١٣ W13-6·٣ — ADR-051) ═══════════════════════
//
// **ما هذا المُجَلَّد**: الوَحدَةُ (رَمزٌ مِن مُعجَمٍ مُغلَقٍ مَضمونٍ في هذه المَكتَبَة: `Definitions/meter-units.json`) ومُصادِقُها بِشَكلِ
// القاعِدَة ٤، ومِفتاحُ قامُوسِها، وسَطرُ الدَفتَرِ ومُعَرِّفُه الحَتمِيّ، وعَقدُ الجَدوَلِ الواحِد (`MeterSql`)، وحامِلُ النِطاقِ لِكُلِّ طَلَب
// (`MeterScope`) وقِيَمُه الحارِسَة (`MeterSentinels`). **ولا سِعرَ في شَيءٍ مِنه** (القاعِدَة ١٦): لا حَقلَ ولا عَمودَ ولا مِفتاحَ يُسَمّي
// سِعراً أَو مَبلَغاً أَو كُلفَةً أَو رَسماً أَو عُملَة — يَحرُسُه `MeterUnitCatalogTests` بِالانعِكاسِ وبِمِفتاحٍ مَجهولٍ يَرمي.
// ‏(‏2026-11-12، مُراجَعَةُ المَرحَلَةِ أ الجَولَةُ الثالِثَة · M6A-RULES-R3-1 — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه) **وكانَ «ولا عَمود» دَعوى بِلا حارِس**: الانعِكاسُ
// عَلى فَضاءِ هذا المُجَلَّدِ وَحدَه، فَعَمودُ `amount_sar` في `meter.agg_daily` و`const FeeSar` في المُجَمِّعِ وعُنوانُ صَفحَةٍ بِالريالِ تَمُرُّ خَضراء. وصارَ
// `MeterUnitCatalogTests` يَقرَأُ أَيضاً جَداوِلَ `UsageLedgerFeature` وأَعمِدَتَها، وكُلَّ نَصٍّ ثابِتٍ في `MeterSql`، وأَعضاءَ `apps/V1.Meter` المَبنِيِّ وأَنواعِ
// العَدِّ في القالِب؛ و`MeterAdminReachabilityTests` (G11) قِيَمَ مَفاتيحِ العَدِّ في القامُوسِ بِمُعجَمٍ عَرَبِيٍّ مَعَ الإنجليزِيّ — ويَطبَعُ كُلٌّ ما عَدّ.
//
// **لِماذا هُنا لا في مَكتَبَةٍ جَديدَة** (RULES-19): `ACommerce.Platform.Shared` يُشيرُ إلَيها القالِبُ (المُستَمِعُ) ووَسيطُ المُستَأجِرِ
// (`ACommerce.Platform.MultiTenancy`، فاتِحُ الحامِل) أَصلاً، وتَحمِلُ `Microsoft.AspNetCore.App` الَّذي يَحتاجُه الصادِرُ لاحِقاً.
//
// **شَرطُ الاستِخراجِ — مَكتوبٌ هُنا** (القاعِدَة ١): هذا المُجَلَّدُ لا يُستَخرَجُ مَكتَبَةً مُستَقِلَّةً قَبلَ ثَلاثَةِ مُستَهلِكينَ في وَقتِ
// التَشغيل. مُستَهلِكوه اليَوم (البَند ٣) **اثنان**:
//   ١. مُستَمِعُ جَلسَةِ القالِب `UsageListener` (+ `MeterRules` و`MeterBoot`) — يَقرَأُ المُعجَمَ والحامِلَ ويَكتُبُ السَطرَ بِـ`MeterSql`؛
//   ٢. وَسيطُ المُستَأجِرِ `TenantResolverMiddleware` — يَفتَحُ الحامِلَ في أَوَّلِ سَطرٍ ويَملَؤُه مِن وَثيقَةِ المَتجَر.
// والثالِثُ `apps/V1.Meter` (البَند ٤)، ثُمَّ العُدَدُ الصادِرَةُ واحِدَةً بَعدَ أُخرى (البُنودُ ١١–١٥). يُحَدَّثُ هذا الرَأسُ مَعَ كُلِّ مُستَهلِك.
// ‏(‏2026-11-12، W13-6·٤ — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه) **وصاروا ثَلاثَة**:
//   ٣. المُجَمِّعُ المُستَقِلّ `apps/V1.Meter` — يَقرَأُ عَقدَ الجَدوَلِ (`MeterSql`: المُؤَشِّرُ والصَفحَةُ والمَجاميع) والمُعجَمَ (جانِبُ كُلِّ وَحدَة،
//      فَيُطبَعُ الحُضورُ بَعيداً عَن التَشغيل)، ولا يُشيرُ إلّا إلى هذه المَكتَبَةِ وNpgsql (G10، `MeterIndependenceTests`).
// فَبَلَغَ الشَرطُ عَدَدَه، **والبَقاءُ هُنا قَرارٌ لا تَأجيل**: المُستَهلِكونَ الثَلاثَةُ يُشيرونَ إلى `ACommerce.Platform.Shared` أَصلاً، ولا Marten فيها،
// فَمَكتَبَةٌ مُستَخرَجَةٌ تُضيفُ مَشروعاً يُشيرُ إلَيه الثَلاثَةُ أَنفُسُهُم ولا تَعزِلُ شَيئاً جَديداً — والصادِرُ (البَند ١١) يَحتاجُ إطارَ ASP.NET الَّذي تَحمِلُه.
// ‏(‏2026-11-12، W13-6·١١ — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه) **والرابِعُ الصادِر**: `ProviderCall`/`ProviderCalls`/`ProviderHttp` (`AddProviderHttp` يَبني عَميلَه مِن
//   `IHttpClientFactory` في الإطارِ نَفسِه) — مُستَهلِكُها الأَوَّلُ في وَقتِ التَشغيلِ قَناةُ Twilio (`ACommerce.Kit.Auth.Providers.Twilio`) ومُنادوها: بابُ الرَمزِ
//   (`AuthHandlers`، ولِأَجلِه صارَت `ACommerce.Kit.Auth.Core` تُشيرُ إلى هذه المَكتَبَة) والاستوديو و`AccountContacts`، وكاتِبُها `UsageLedgerWriter` في القالِب.
//   والعُدَدُ الباقِيَةُ تَدخُلُ واحِدَةً بَعدَ أُخرى (البُنودُ ١٢–١٥).
// ‏(‏2026-11-13، W13-6·١٧ — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه) **وبَوّابَةُ إقلاعِ الأَدوارِ تَقرَأُ المُعجَم**: `RoleMeterValidator` (في `ACommerce.Kit.Roles.Core`، ولِأَجلِه
//   صارَت تُشيرُ إلى هذه المَكتَبَة) يَسأَلُ `MeterUnitCatalog.Default` عِندَ تَحميلِ مِلَفّاتِ الأَدوار: أَوَحدَةٌ مِترُ الدَورِ أَم لا، وأَحُضورٌ هِيَ أَم عَمَلِيَّة.

/// <summary>
/// <para><b>وَحدَةُ عَدٍّ واحِدَة</b> — صَفٌّ في <c>meter-units.json</c>. تُعلِنُ <b>جانِبَها</b> (<see cref="MeterSides"/>: تَشغيلٌ أَو تِقنِيَة)
/// و<b>حَدَّها</b> (<see cref="MeterBoundaries"/>) و<b>مِقياسَها</b> (<see cref="MeterMeasures"/>) و<b>مِيزَتَها</b> و<b>قارِئَ مُطابَقَتِها</b>
/// و<b>مَوضِعَ عَدِّها</b> (<see cref="Source"/> — دَرسُ <c>CapabilityCatalog.SourceRef</c>: وَحدَةٌ بِلا مَوضِعٍ يَعُدُّها لا تَدخُل).</para>
/// <para>والجانِبانِ لا يَختَلِطان: التِقنِيَةُ حُضورٌ لِكُلِّ مَتجَرٍ في اليَوم، والتَشغيلُ عَمَلِيَّة — مَبدَأُ المالِكِ في ADR-051 §٢.</para>
/// </summary>
public sealed record MeterUnit
{
    public string Code { get; init; } = "";
    public string Side { get; init; } = "";
    public string Boundary { get; init; } = "";
    public string Measure { get; init; } = "";
    /// <summary>رَمزُ مِيزَةٍ ثابِت (اسمٌ في أَحَدِ المَعاجِمِ الأَربَعَةِ القائِمَة — التَصميم §3.2)، أَو مِرساةٌ يُختارُ بِها وَقتَ الكِتابَة
    /// (<see cref="MeterFeatureAnchors"/>).</summary>
    public string? Feature { get; init; }
    /// <summary>قارِئُ المُطابَقَة — مِن <see cref="MeterReconcilers"/>.</summary>
    public string? Reconcile { get; init; }
    public string Source { get; init; } = "";
}

/// <summary>الجانِبان — <b>مَبدَأُ المالِك</b>: التِقنِيَةُ والتَشغيلُ يُعَدّانِ مُنفَصِلَين.</summary>
public static class MeterSides
{
    public const string Operation = "operation";
    public const string Technology = "technology";
    public static readonly IReadOnlyList<string> All = [Operation, Technology];
}

/// <summary>الحُدودُ الثَلاثَة الَّتي تُعَدُّ عِندَها الوَحدَة. والتِقنِيَةُ ⇔ الحُضور (<c>unit_side_contradicts_boundary</c>).</summary>
public static class MeterBoundaries
{
    public const string Persistence = "persistence";
    public const string Outbound = "outbound";
    public const string Presence = "presence";
    public static readonly IReadOnlyList<string> All = [Persistence, Outbound, Presence];
}

/// <summary>ما يَحمِلُه عَمودُ الكَمِّيَّة: عَدٌّ (1 لِكُلِّ عَمَلِيَّة) أَو بايتات.</summary>
public static class MeterMeasures
{
    public const string Count = "count";
    public const string Bytes = "bytes";
    public static readonly IReadOnlyList<string> All = [Count, Bytes];
}

/// <summary>مِراسي المِيزَةِ المُختارَةِ وَقتَ الكِتابَة — تَبدَأُ بِـ<c>@</c>، ومِرساةٌ خارِجَها <c>unit_feature_anchor_unknown</c>.</summary>
public static class MeterFeatureAnchors
{
    public const string ProviderCapability = "@provider_capability";
    public const string Flow = "@flow";
    public static readonly IReadOnlyList<string> All = [ProviderCapability, Flow];
    public static bool IsAnchor(string feature) => feature.StartsWith('@');
}

/// <summary>
/// <para><b>قُرّاءُ المُطابَقَة — قائِمَةٌ مُغلَقَة</b> (التَصميم §9): لِكُلِّ قيمَةٍ قارِئُ مَصدَرٍ يُقارِنُ ما وُلِدَ بِما عُدّ. والقُرّاءُ أَنفُسُهُم
/// يَلِدونَ في البَند ٢٠؛ والقائِمَةُ هُنا مُعجَمُ المُصادِقِ لا كودُ القِراءَة.</para>
/// </summary>
public static class MeterReconcilers
{
    public const string DealIds = "deal_ids";
    public const string TenantIds = "tenant_ids";
    public const string ReviewIds = "review_ids";
    public const string UserIds = "user_ids";
    public const string StudioUserIds = "studio_user_ids";
    public const string MessageIds = "message_ids";
    public const string Intents = "intents";
    /// <summary>بادِئَةُ قارِئِ الأَحداث: <c>event_ids:{نَوعُ الحَدَث}</c>.</summary>
    public const string EventIdsPrefix = "event_ids:";
    /// <summary>‏(‏2026-11-13، W13-6·١٩ — ADR-051 §٢٣؛ يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه) <b>قارِئُ وَحَداتِ الحُضورِ التِسع</b>: مُطالَبَةُ اليَومِ (<c>meter.presence_claim</c>)
    /// مَعَ سُطورِه — يَومٌ مُكتَمِلٌ بِسُطورِه، ويَومٌ بِلا مُطالَبَةٍ «لَقطَةٌ غائِبَة». المُصادِقُ يَطلُبُ قارِئاً لِكُلِّ وَحدَة (<c>unit_reconcile_unknown</c>)، وقائِمَةُ
    /// التَصميمِ (§9) قُرّاءُ مَصادِرِ الحِفظِ والنِيّاتُ وَحدَها؛ ويَبنيه البَند ٢٠ مَعَ إخوَتِه.</summary>
    public const string PresenceClaims = "presence_claims";

    public static readonly IReadOnlyList<string> Fixed = [DealIds, TenantIds, ReviewIds, UserIds, StudioUserIds, MessageIds, Intents, PresenceClaims];

    private static readonly Regex EventIds = new("^event_ids:[A-Z][A-Za-z0-9]*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static bool IsKnown(string? reconcile)
        => reconcile is not null && (Fixed.Contains(reconcile, StringComparer.Ordinal) || EventIds.IsMatch(reconcile));
}
