using System.Text.RegularExpressions;

namespace ACommerce.Platform.Shared.Metering;

// ═══ العَدُّ الداخِلِيّ — مُعجَمُ الوَحَداتِ وأَدَواتُه المُشتَرَكَة (‏2026-11-12، الموجَة ١٣ W13-6·٣ — ADR-051) ═══════════════════════
//
// **ما هذا المُجَلَّد**: الوَحدَةُ (رَمزٌ مِن مُعجَمٍ مُغلَقٍ مَضمونٍ في هذه المَكتَبَة: `Definitions/meter-units.json`) ومُصادِقُها بِشَكلِ
// القاعِدَة ٤، ومِفتاحُ قامُوسِها، وسَطرُ الدَفتَرِ ومُعَرِّفُه الحَتمِيّ، وعَقدُ الجَدوَلِ الواحِد (`MeterSql`)، وحامِلُ النِطاقِ لِكُلِّ طَلَب
// (`MeterScope`) وقِيَمُه الحارِسَة (`MeterSentinels`). **ولا سِعرَ في شَيءٍ مِنه** (القاعِدَة ١٦): لا حَقلَ ولا عَمودَ ولا مِفتاحَ يُسَمّي
// سِعراً أَو مَبلَغاً أَو كُلفَةً أَو رَسماً أَو عُملَة — يَحرُسُه `MeterUnitCatalogTests` بِالانعِكاسِ وبِمِفتاحٍ مَجهولٍ يَرمي.
//
// **لِماذا هُنا لا في مَكتَبَةٍ جَديدَة** (RULES-19): `ACommerce.Platform.Shared` يُشيرُ إلَيها القالِبُ (المُستَمِعُ) ووَسيطُ المُستَأجِرِ
// (`ACommerce.Platform.MultiTenancy`، فاتِحُ الحامِل) أَصلاً، وتَحمِلُ `Microsoft.AspNetCore.App` الَّذي يَحتاجُه الصادِرُ لاحِقاً.
//
// **شَرطُ الاستِخراجِ — مَكتوبٌ هُنا** (القاعِدَة ١): هذا المُجَلَّدُ لا يُستَخرَجُ مَكتَبَةً مُستَقِلَّةً قَبلَ ثَلاثَةِ مُستَهلِكينَ في وَقتِ
// التَشغيل. مُستَهلِكوه اليَوم (البَند ٣) **اثنان**:
//   ١. مُستَمِعُ جَلسَةِ القالِب `UsageListener` (+ `MeterRules` و`MeterBoot`) — يَقرَأُ المُعجَمَ والحامِلَ ويَكتُبُ السَطرَ بِـ`MeterSql`؛
//   ٢. وَسيطُ المُستَأجِرِ `TenantResolverMiddleware` — يَفتَحُ الحامِلَ في أَوَّلِ سَطرٍ ويَملَؤُه مِن وَثيقَةِ المَتجَر.
// والثالِثُ `apps/V1.Meter` (البَند ٤)، ثُمَّ العُدَدُ الصادِرَةُ واحِدَةً بَعدَ أُخرى (البُنودُ ١١–١٥). يُحَدَّثُ هذا الرَأسُ مَعَ كُلِّ مُستَهلِك.

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

    public static readonly IReadOnlyList<string> Fixed = [DealIds, TenantIds, ReviewIds, UserIds, StudioUserIds, MessageIds, Intents];

    private static readonly Regex EventIds = new("^event_ids:[A-Z][A-Za-z0-9]*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static bool IsKnown(string? reconcile)
        => reconcile is not null && (Fixed.Contains(reconcile, StringComparer.Ordinal) || EventIds.IsMatch(reconcile));
}
