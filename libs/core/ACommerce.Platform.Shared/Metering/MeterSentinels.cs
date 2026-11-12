namespace ACommerce.Platform.Shared.Metering;

/// <summary>
/// <para><b>القِيَمُ الحارِسَة — مُعجَمٌ مُغلَقٌ يَبدَأُ بِـ<c>_</c></b>: كُلُّ عَمودٍ في السَطرِ لا يَقبَلُ <c>null</c> (‏<c>NOT NULL</c>)، فَما لا يُعرَفُ
/// وَقتَ الكِتابَةِ يُكتَبُ بِقيمَةٍ مُسَمّاةٍ لا يُصادِفُها اسمُ مَتجَرٍ (<c>[a-z0-9-]</c>، <c>StoreHostName</c>) ولا اسمُ دَور
/// (<c>^[a-z][a-z0-9_]*$</c>، <c>RoleDefinitionValidator</c>) — فَلا تَصادُم.</para>
/// <para><b>ولِكُلِّ قيمَةٍ مَوضِعٌ يَضَعُها</b> (القاعِدَة ١؛ <c>MeterSentinelSetterTests</c> يَعُدُّه ويَحمَرُّ عَلى قيمَةٍ بِلا واضِع): القيمَةُ تَدخُلُ
/// هذا المُعجَمَ مَعَ أَوَّلِ مَوضِعٍ يَضَعُها لا قَبلَه. والتَصميمُ (§2) يُسَمّي تِسعاً؛ أَربَعٌ مِنها لَها واضِعٌ اليَوم (البَند ٣)، والخَمسُ
/// الباقِيَةُ تَدخُلُ مَعَ واضِعِها: <c>_platform</c> و<c>_entrepreneur</c> و<c>_platform_admin</c> (البَند ٨: حِسابُ الاستوديو وحُرّاسُ
/// الاستوديو والإدارَة)، و<c>_provider</c> و<c>_system</c> (البَند ١٤: خَطاطيفُ المُزَوِّدينَ ومُعالِجُ انتِهاءِ الفاتورَة).</para>
/// <para>‏(‏2026-11-12، W13-6·٨ — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه) <b>ودَخَلَت الثَلاثُ مَعَ واضِعيها</b>: <see cref="Platform"/> يَضَعُه المُصَنِّفُ مَتجَراً لِسَطرِ
/// <c>studio.account.create</c>؛ و<see cref="Entrepreneur"/> يَضَعُه <c>StudioOwnsAsync</c> و<c>TenantAdminGuard</c> حينَ يُثبِتانِ أَنّ الطالِبَ مالِكُ المَتجَر؛
/// و<see cref="PlatformAdmin"/> يَضَعُه <c>PlatformAdminGuard</c> حينَ يَقبَلُ مُشرِفَ المَنَصَّة. فَهِيَ سَبعٌ، والباقِيَتانِ لِلبَند ١٤.</para>
/// <para>‏(‏2026-11-12، W13-6·١٤ — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه) <b>ودَخَلَت الباقِيَتانِ مَعَ واضِعيهِما</b>: <see cref="Provider"/> تَضَعُه نِقاطُ الإشعارِ الثَلاثُ لِبَوّابَةِ المَتجَرِ
/// وPayPal وPaddle، و<see cref="SystemJob"/> يَضَعُه مُعالِجُ انتِهاءِ الفاتورَة. فَهِيَ تِسعٌ كَما يُسَمّيها التَصميم.</para>
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

    /// <summary>‏(‏2026-11-12، W13-6·٨) المَنَصَّةُ نَفسُها تَطبيقاً: مَتجَرُ سَطرِ <c>studio.account.create</c> — حِسابُ رائِدِ الأَعمالِ يولَدُ في الاستوديو
    /// لا في مَتجَر (يَضَعُه <c>MeterRules</c>).</summary>
    public const string Platform = "_platform";

    /// <summary>‏(‏2026-11-12، W13-6·٨) رائِدُ الأَعمالِ نَفسُه يَعمَلُ عَلى مَتجَرِه: دَورُ الحامِلِ حينَ يُثبِتُ <c>StudioOwnsAsync</c> مِلكِيَّتَه، أَو يَقبَلُه
    /// <c>TenantAdminGuard</c> مالِكاً مِن الاستوديو.</summary>
    public const string Entrepreneur = "_entrepreneur";

    /// <summary>‏(‏2026-11-12، W13-6·٨) مُشرِفُ المَنَصَّة: دَورُ فاعِلٍ عَلى مُستَوى المَنَصَّةِ يَضَعُه <c>PlatformAdminGuard</c> حينَ يَقبَل
    /// (<see cref="MeterScopeHolder.FillPlatformRole"/>).</summary>
    public const string PlatformAdmin = "_platform_admin";

    /// <summary>‏(‏2026-11-12، W13-6·١٤) المُزَوِّدُ نَفسُه فاعِلاً: دَورُ الحامِلِ في نِقاطِ الإشعارِ الوارِد — بَوّابَةُ المَتجَر (<c>/{slug}/payments/notify</c>)
    /// وPayPal (<c>/api/billing/paypal</c>) وPaddle (<c>/api/billing/paddle</c>) — لا جَلسَةَ ولا دَور، والمَسارُ يَقولُ مَن يُنادي.</summary>
    public const string Provider = "_provider";

    /// <summary>‏(‏2026-11-12، W13-6·١٤) عَمَلٌ مُجَدوَلٌ تُنَفِّذُه المَنَصَّةُ بِلا طالِب: دَورُ الحامِلِ الَّذي يَفتَحُه مُعالِجُ انتِهاءِ الفاتورَة
    /// (<c>GatewayInvoiceExpiryHandler.Handle</c>) لِمَتجَرِ رِسالَتِه.</summary>
    public const string SystemJob = "_system";

    public static readonly IReadOnlyList<string> All = [Unowned, None, Seed, Unresolved, Platform, Entrepreneur, PlatformAdmin, Provider, SystemJob];

    public static bool IsSentinel(string value) => All.Contains(value, StringComparer.Ordinal);
}
