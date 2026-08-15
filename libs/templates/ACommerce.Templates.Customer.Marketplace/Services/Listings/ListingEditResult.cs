namespace ACommerce.Templates.Customer.Marketplace.Services.Listings;

/// <summary>
/// <para><b>ماذا حَدَثَ لِعَمَلِيَّةٍ على إعلانٍ قائِم — بِثَلاث حالاتٍ
/// لا رابِعَة.</b> ونَفسُ عِلَّة <c>TenantConfigStatus</c> حَرفاً:
/// الرَفضُ لِمُدخَلٍ غَير صالِح <b>نَتيجَةٌ مُتَوَقَّعَة</b> لا
/// استِثناء، ورَميُه يَجعَل مَسارَ الخَطَأ الشائِع يَمُرّ بِآلَةٍ
/// صُمِّمَت لِلنادِر.</para>
///
/// <para><b>ولِماذا نَوعٌ ثانٍ ولَيسَ <c>TenantConfigResult</c></b>:
/// المُعجَمُ جُزءٌ مِن النَوع — <c>TenantConfigCodes</c> مُعجَمُ
/// <b>إعدادِ مُستَأجِر</b> (<c>no_scope</c>، <c>icon_too_large</c>…)،
/// وحَشرُ <c>not_owner</c> فيه يَفتَحُه لِكُلّ مَجال فَيَبطُل
/// انغِلاقُه — وانغِلاقُه هُوَ كُلّ قيمَتِه. فَالشَكل يُعاد ولا يُعاد
/// المُعجَم، و<c>TenantConfigServiceShapeTests</c> وُسِّعَ لِيَفرِضَ
/// الشَكلَ على المُجَلَّدَين مَعاً.</para>
/// </summary>
public enum ListingEditStatus
{
    /// <summary>المُدخَل صالِح وأُلحِقَ الحَدَثُ بِالجَلسَة — والإيداع
    /// على النُقطَة، لا هُنا.</summary>
    Applied,

    /// <summary>مُدخَلٌ أَو فاعِلٌ مَرفوض — <c>Code</c> عُضوٌ في
    /// <see cref="ListingEditCodes"/> ولا شَيءَ سِواه.</summary>
    Rejected,

    /// <summary>لا إعلانَ بِهذا المُعَرِّف في هذا المُستَأجِر.
    /// مُتَمَيِّزَةٌ عَن الرَفض لِأَنّ عَرضَها يَختَلِف: لا رِسالَةَ
    /// حَقلٍ في صَفحَةٍ لا إعلانَ لَها.</summary>
    Missing,
}

/// <summary>نَتيجَةُ عَمَلِيَّةٍ واحِدَة على إعلان. <c>Code</c> غَير
/// فارِغٍ إلّا مَع <see cref="ListingEditStatus.Rejected"/>.</summary>
public sealed record ListingEditResult(ListingEditStatus Status, string? Code)
{
    public static readonly ListingEditResult Applied = new(ListingEditStatus.Applied, null);
    public static readonly ListingEditResult Missing = new(ListingEditStatus.Missing, null);

    /// <summary>رَفضٌ بِرَمزٍ مِن المُعجَم المُغلَق. يُلقي إن كانَ
    /// الرَمز خارِجَه — فَالمُعجَم يَنمو بِقَرارٍ في
    /// <see cref="ListingEditCodes"/> لا بِسِلسِلَةٍ عابِرَة.</summary>
    public static ListingEditResult Reject(string code)
    {
        if (!ListingEditCodes.All.Contains(code))
            throw new ArgumentOutOfRangeException(nameof(code), code,
                "رَمزُ رَفضٍ خارِجَ المُعجَم المُغلَق — أَضِفه إلى ListingEditCodes أَوَّلاً.");
        return new ListingEditResult(ListingEditStatus.Rejected, code);
    }

    public bool Ok => Status == ListingEditStatus.Applied;
}

/// <summary>
/// <para><b>المُعجَم المُغلَق لِرُموز رَفض تَحرير الإعلان</b> —
/// تَعريفٌ واحِد تَقرَؤُه الخِدمَةُ والصَفحَةُ والنُقطَة. والرَمز
/// يَقول <b>العِلَّة</b> لا الحَقل، على نَفس قاعِدَة
/// <c>name_required</c> في مُعجَم إعداد المُستَأجِر.</para>
/// </summary>
public static class ListingEditCodes
{
    /// <summary>الفاعِلُ لَيسَ مالِكَ الإعلان. <b>وهذا هُوَ التَخويل</b>
    /// — يُفحَص قَبلَ أَيّ حَقل، وإلّا صارَ خَطَأُ التَحَقُّق قِناعاً
    /// لِلثَغرَة (القاعِدَة ٦).</summary>
    public const string NotOwner = "not_owner";

    /// <summary>العُنوان أَقصَرُ مِن الحَدّ — نَفس حَدّ الإنشاء (‏3).</summary>
    public const string TitleShort = "title_short";

    /// <summary>السِعر لَيسَ رَقماً، أَو لَيسَ مَوجَباً في إعلانٍ لا
    /// يَقبَل العُروض.</summary>
    public const string PriceInvalid = "price_invalid";

    /// <summary>لا حَقلَ تَبَدَّل. حَدَثٌ بِلا فَرقٍ يُلَوِّث التَيار
    /// ويَكذِب على <c>UpdatedAt</c>، فَيُرَدّ بِرِسالَتِه لا يُبتَلَع.</summary>
    public const string NoChange = "no_change";

    /// <summary>
    /// <para><b>ورَمزٌ خامِسٌ كُتِبَ ثُمَّ حُذِفَ — والقِياسُ الحَيّ
    /// هُوَ الَّذي حَذَفَه.</b> كانَ <c>already_deleted</c> لِحَذفٍ
    /// ثانٍ، ومِفتاحُ رِسالَتِه مَكتوبٌ في القامُوس. ثُمَّ أَعطى
    /// <c>curl</c> على حَذفٍ مُكَرَّر <c>err=not_owner</c> لا
    /// <c>already_deleted</c>: <c>ListingOwnerFilter</c> يَقرَأ
    /// بِـ<c>LoadOwnedAsync</c> وهي تُسقِط المَحذوف، فَتَرُدّ قَبلَ
    /// أَن تَبلُغَ الخِدمَة. أَي أَنّ الرَمزَ <b>لا يَبلُغُه أَحَد
    /// مِن السَطح الوَحيد القائِم</b>.</para>
    ///
    /// <para>فَجُمِعَ مَع <c>Missing</c>: جَوابٌ واحِد لِـ«لا إعلانَ
    /// حَيّاً هُنا». والقاعِدَة ١ تَقول التَجريدُ لا يَسبِق
    /// مُستَهلِكَه؛ ورَمزُ رَفضٍ بِصِفر مَوضِعِ بُلوغ هُوَ
    /// <c>state_unreachable</c> بِعَينِه — يُقرَأ ويُراجَع
    /// ولا يُنَفَّذ.</para>
    /// </summary>
    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        NotOwner, TitleShort, PriceInvalid, NoChange,
    };
}
