namespace ACommerce.Templates.Customer.Marketplace.Services.Deals;

/// <summary>خَرق واحِد في تَخويلِ الإلغاء. <c>Code</c> مِفتاحٌ ثابِتٌ
/// لِلاختِبارات واللوغ، و<c>MessageAr</c> لِلمُراجِع البَشَريّ. نَفس
/// شَكل <c>DealPatternViolation</c> و<c>ApiKeyViolation</c> — القالِب
/// المَرجِعيّ في القاعِدَة ٤.</summary>
public sealed record DealCancelViolation(string Code, string MessageAr);

/// <summary>
/// <para><b>مَن يُلغي، وبِأَيِّ سُلطَة.</b> هذا هُوَ الحارِسُ نَفسُه
/// مَنقولاً إلى <b>تَوقيع</b> <c>DealsService.CancelAsync</c>
/// (القاعِدَة ٦): لا يُمكِن نِداءُ الإلغاء بِلا تَصريحٍ صَريحٍ عَن
/// السُلطَة، فَلا يوجَد مَسارٌ «يَنسى» الحارِسَ لِأَنّ المُتَرجِمَ
/// يَمنَعُه.</para>
///
/// <para><b>ولِماذا رايَةٌ لا دَورٌ نَصِّيّ</b>: «مُشرِفُ المَتجَر»
/// يُثبَت في ثَلاثَة مَواضِعَ مُختَلِفَة بِثَلاثِ آلِيّات
/// (<c>StudioOwnsAsync</c> لِلاستوديو، وجَلسَةُ المُستَخدِم لِلواجِهَة،
/// ووَثيقَةُ المِفتاح لِلـAPI). فَالخِدمَةُ لا تُعيدُ إثباتَ ما
/// أَثبَتَه الحارِسُ الأَعلى — تَطلُبُ مِنه أَن <b>يَقولَه</b>.
/// وذاكَ الفَرقُ بَينَ تَكرارِ مَنطِقٍ وبَينَ عَقدٍ مُعلَن.</para>
/// </summary>
/// <param name="ActorId">المُستَخدِم الَّذي يُنسَب إلَيه السَطر في
/// <c>Timeline</c>.</param>
/// <param name="ActorName">اسمُه كَما يُكتَب في السِجِلّ.</param>
/// <param name="IsStoreAdmin">أَثبَتَ الحارِسُ الأَعلى أَنَّه مُشرِفُ
/// المَتجَر. <b>الافتِراضُ لا يوجَد</b>: القيمَةُ إلزامِيَّة.</param>
public sealed record DealCanceller(Guid ActorId, string ActorName, bool IsStoreAdmin);

/// <summary>
/// <para><b>بَوّابَةُ الإلغاء — دالَّةٌ نَقِيَّة.</b> لا قاعِدَةَ
/// بَيانات، ولا وَقتَ، ولا عَشوائيَّة: صَفقَةٌ وفاعِلٌ، ورَمزُ خَرقٍ
/// مِن مَعجَمٍ مُغلَق أَو <c>null</c>.</para>
///
/// <para><b>الثَغرَةُ الَّتي أَغلَقَتها</b> (‏§١١٫٦ في
/// <c>docs/API-SURFACE-DESIGN.md</c>): كانَت
/// <c>DealsService.CancelAsync</c> <b>لا تَفحَصُ الفاعِلَ إطلاقاً</b> —
/// بِخِلاف <c>AdvanceAsync</c> — و<c>POST /{slug}/deals/{id}/cancel</c>
/// تَكتَفي بِجَلسَةٍ صالِحَة. أَي أَنّ <b>أَيَّ مُستَخدِمٍ في المَتجَر
/// يُلغي صَفقَةَ أَيّ أَحَد</b>. والإلغاءُ لَيسَ تَغييرَ حالَةٍ
/// وَحسب: <c>RefundAsync</c> تَقَع مَعَه — فَالثَغرَةُ تَمَسُّ المالَ
/// لا السِجِلَّ فَقَط.</para>
///
/// <para><b>ولِماذا التَخويلُ يَسبِقُ فَحصَ الحالَة</b> (القاعِدَة ٦):
/// لَو رُتِّبَ العَكسُ لَصارَ «الصَفقَةُ لَيسَت فَعّالَة» جَواباً
/// يَتَسَرَّب إلى غَيرِ الطَرَف — أَي أَنّ خَطَأَ التَحَقُّق يَصيرُ
/// قِناعاً لِلثَغرَة. الغِيابُ وَحدَه يَسبِق، لِأَنَّه لا يُفشي
/// شَيئاً.</para>
/// </summary>
public static class DealCancelAuthorization
{
    /// <summary>لا صَفقَةَ بِهذا المُعَرِّف في هذا المُستَأجِر.</summary>
    public const string DealNotFound = "deal_not_found";

    /// <summary>الفاعِلُ لَيسَ طَرَفاً في الصَفقَةِ ولا مُشرِفَ
    /// المَتجَر.</summary>
    public const string ActorNotParty = "actor_not_party";

    /// <summary>الصَفقَةُ خَرَجَت مِن <c>Active</c> — لا شَيءَ
    /// يُلغى.</summary>
    public const string DealNotActive = "deal_not_active";

    /// <summary>المَعجَمُ المُغلَق — ثَلاثَةٌ لا رابِعَ لَها.</summary>
    public static readonly IReadOnlyList<string> All =
        new[] { DealNotFound, ActorNotParty, DealNotActive };

    public static bool Contains(string code) => All.Contains(code, StringComparer.Ordinal);

    /// <summary>يَرمي عِندَ الخَرق — لِمَواضِعِ التَركيب. نَفسُ حيلَة
    /// <c>ApiScopeCatalog.Require</c> و<c>CapabilityCatalog.Require</c>
    /// حَرفاً.</summary>
    public static string Require(string code)
    {
        if (!Contains(code))
            throw new ArgumentException(
                $"الرَمز «{code}» خارِج مَعجَم DealCancelAuthorization. " +
                $"المَعجَم: {string.Join("، ", All)}.", nameof(code));
        return code;
    }

    /// <summary><c>null</c> يَعني مُخَوَّل. والتَرتيبُ مَقصود:
    /// الغِياب، ثُمَّ التَخويل، ثُمَّ الحالَة.</summary>
    public static DealCancelViolation? Validate(Deal? deal, DealCanceller by)
    {
        if (deal is null)
            return new(DealNotFound, "لا صَفقَةَ بِهذا المُعَرِّف.");

        if (!by.IsStoreAdmin &&
            deal.InitiatorId != by.ActorId &&
            deal.CounterpartyId != by.ActorId)
            return new(ActorNotParty,
                "الفاعِلُ لَيسَ طَرَفاً في هذِه الصَفقَة ولا مُشرِفَ المَتجَر — " +
                "والإلغاءُ يَستَرِدُّ المالَ، فَلا يَملِكُه غَيرُ أَطرافِها.");

        if (deal.Status != DealStatus.Active)
            return new(DealNotActive,
                $"الصَفقَةُ في حالَة {deal.Status} — لا شَيءَ يُلغى.");

        return null;
    }

    public static bool IsAllowed(Deal? deal, DealCanceller by) => Validate(deal, by) is null;
}

/// <summary>
/// <para>ناتِجُ الإلغاء. <b>ولِماذا لا <c>Deal?</c> كَما كانَ</b>:
/// المُنادي كانَ يَستَحيلُ عَلَيه التَفريقُ بَينَ «أُلغِيَت» و«لَم
/// تُلغَ لِأَنَّها غَيرُ فَعّالَة» إلّا بِقِراءَةِ الحالَةِ بَعدَ
/// النِداء — ورَفضٌ لا يَراهُ المُنادي رَفضٌ يُبتَلَع. نَفسُ شَكل
/// <c>DealAdvanceResult</c>.</para>
/// </summary>
public sealed record DealCancelResult(bool Ok, Deal? Deal, DealCancelViolation? Violation)
{
    public static DealCancelResult Refused(DealCancelViolation v, Deal? deal = null)
        => new(false, deal, v);

    public static DealCancelResult Cancelled(Deal deal) => new(true, deal, null);
}
