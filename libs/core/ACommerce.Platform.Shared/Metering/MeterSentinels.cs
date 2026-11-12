namespace ACommerce.Platform.Shared.Metering;

/// <summary>
/// <para><b>القِيَمُ الحارِسَة — مُعجَمٌ مُغلَقٌ يَبدَأُ بِـ<c>_</c></b>: كُلُّ عَمودٍ في السَطرِ لا يَقبَلُ <c>null</c> (‏<c>NOT NULL</c>)، فَما لا يُعرَفُ
/// وَقتَ الكِتابَةِ يُكتَبُ بِقيمَةٍ مُسَمّاةٍ لا يُصادِفُها اسمُ مَتجَرٍ (<c>[a-z0-9-]</c>، <c>StoreHostName</c>) ولا اسمُ دَور
/// (<c>^[a-z][a-z0-9_]*$</c>، <c>RoleDefinitionValidator</c>) — فَلا تَصادُم.</para>
/// <para><b>ولِكُلِّ قيمَةٍ مَوضِعٌ يَضَعُها</b> (القاعِدَة ١؛ <c>MeterSentinelSetterTests</c> يَعُدُّه ويَحمَرُّ عَلى قيمَةٍ بِلا واضِع): القيمَةُ تَدخُلُ
/// هذا المُعجَمَ مَعَ أَوَّلِ مَوضِعٍ يَضَعُها لا قَبلَه. والتَصميمُ (§2) يُسَمّي تِسعاً؛ أَربَعٌ مِنها لَها واضِعٌ اليَوم (البَند ٣)، والخَمسُ
/// الباقِيَةُ تَدخُلُ مَعَ واضِعِها: <c>_platform</c> و<c>_entrepreneur</c> و<c>_platform_admin</c> (البَند ٨: حِسابُ الاستوديو وحُرّاسُ
/// الاستوديو والإدارَة)، و<c>_provider</c> و<c>_system</c> (البَند ١٤: خَطاطيفُ المُزَوِّدينَ ومُعالِجُ انتِهاءِ الفاتورَة).</para>
/// </summary>
public static class MeterSentinels
{
    /// <summary>مَتجَرٌ بِلا مالِك (<c>Tenant.OwnerUserId == Guid.Empty</c>) — يَضَعُه وَسيطُ المُستَأجِر.</summary>
    public const string Unowned = "_unowned";

    /// <summary>لا قيمَةَ لِلبُعدِ أَصلاً: مَتجَرٌ بِلا أَدوار (وَسيطُ المُستَأجِر)، أَو وَحدَةُ حِفظٍ بِلا مُزَوِّد (<c>MeterRules</c>).</summary>
    public const string None = "_none";

    /// <summary>كِتابَةُ البَذرِ عِندَ الإقلاع — دَورُها ورائِدُ أَعمالِها، لِكُلِّ مَتجَر (<see cref="MeterScope.Seed"/>).</summary>
    public const string Seed = "_seed";

    /// <summary>فَجوَةُ تَصنيف: لا يَرمي المُستَمِعُ بِسَبَبِها بَل يَكتُبُ هذه، فَتُعَدُّ وتُعرَضُ ويَحُدُّها سَقفٌ يَتَقَلَّص (<c>MeterRules</c>).</summary>
    public const string Unresolved = "_unresolved";

    public static readonly IReadOnlyList<string> All = [Unowned, None, Seed, Unresolved];

    public static bool IsSentinel(string value) => All.Contains(value, StringComparer.Ordinal);
}
