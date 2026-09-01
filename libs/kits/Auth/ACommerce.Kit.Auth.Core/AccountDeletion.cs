namespace ACommerce.Kit.Auth;

/// <summary>خَرقٌ واحِدٌ في طَلَبِ حَذفِ الحِساب. نَفسُ شَكلِ
/// <c>DealCancelViolation</c> و<c>RoleDefinitionViolation</c> حَرفاً
/// (القاعِدَة ٤).</summary>
public sealed record AccountDeletionViolation(string Code, string MessageAr);

/// <summary>
/// <para><b>حَذفُ الحِسابِ داخِلَ التَطبيق — بَوّابَةٌ وأَثَرٌ، كِلاهُما
/// دالَّةٌ نَقِيَّة.</b> لا قاعِدَةَ بَيانات، ولا وَقتٌ يُقرَأُ مِن
/// السّاعَة (الوَقتُ يُمَرَّر)، ولا عَشوائيَّة — نَفسُ عَقدِ
/// <c>DealCancelAuthorization</c>. فَالمَسارُ يُقاسُ كامِلاً بِلا
/// إقلاعِ خادِمٍ ولا كِتابَةِ صَفٍّ واحِد.</para>
///
/// <para><b>المُخالَفَةُ الَّتي كَتَبَت هذا المِلَفّ، مَقيسَةً</b>:
/// شَرطُ المَتجَر ‏5.1.1(v) يوجِبُ حَذفَ الحِسابِ <b>داخِلَ
/// التَطبيق</b>. ونَصُّ المَنَصَّةِ كانَ يَقولُ في مَوضِعَين:
/// «لِحَذف حِسابك بِالكامِل: تَواصَل عَبر صَفحَة الدَّعم»
/// (‏<c>legal.privacy.deletion</c>)، و«ويُمكِنُكَ طَلَبُ حَذفِ
/// حِسابِكَ وبَياناتِكَ بِمُراسَلَتِنا، ويُنَفَّذُ الطَلَبُ خِلالَ
/// ثَلاثينَ يَوماً» (‏<c>platform.privacy.s6_body</c>). و<c>grep</c>
/// عَلى المُستودَعِ كُلِّه عَن مَسارِ حَذفٍ أَعطى <b>مُطابَقَةً
/// واحِدَةً يَتيمَةً هي الجُملَةُ الَّتي تُحيلُ إلى الخارِج</b> — أَي
/// أَنَّ الوُرودَ الوَحيدَ لِعِبارَةِ حَذفِ الحِسابِ كانَ نَصَّ
/// المُخالَفَةِ نَفسَه.</para>
///
/// <para><b>ولِماذا إخفاءُ الهُوِيَّةِ لا مَحوُ الوَثيقَة — وهُوَ
/// قَرارٌ يُعلَنُ لا يُبتَلَع</b>: الوَثيقَةُ مُشارٌ إلَيها مِن
/// الصَفَقاتِ والفَواتيرِ بِمُعَرِّفِها، ومَحوُها يَترُكُ سِجِلّاً
/// مالِيّاً بِطَرَفٍ مَفقود. والمادَّةُ الخامِسَةُ تُجيزُ الاحتِفاظَ
/// صَراحَةً «دونَ إخلالٍ بِما يَقضي بِه نِظامٌ آخَر» — والالتِزامُ
/// المُحاسَبِيُّ مِنها. فَالَّذي يَزولُ <b>الآنَ</b> هُوَ كُلُّ ما
/// يَدُلُّ عَلى الشَخص، ويَبقى الصَفُّ بِلا اسمٍ ولا وَسيلَةِ
/// اتِّصال. <b>وهذا يُقالُ لِلمُستَخدِمِ عَلى الشاشَةِ قَبلَ أَن
/// يُؤَكِّد</b> — وحَذفٌ يُخفي ما يُبقيه يَعِدُ بِما لا يَفي.</para>
///
/// <para><b>وما لَم يُحسَم — يُوقَفُ ويُكتَب</b> (وهُوَ في
/// ‏ADR-030 وفي تَقريرِ المَوجَة): أَمَحوٌ تامٌّ بَعدَ انقِضاءِ
/// المُدَّةِ المُحاسَبِيَّة؟ ومُهلَةُ تَراجُعٍ قَبلَ النَفاذ؟
/// وماذا يَحِلُّ بِإعلاناتِه ومُحادَثاتِه المَنشورَة؟ الثَلاثَةُ
/// تَلمِسُ مالاً وسِجِلّاً، فَلا تُبَتُّ في جَولَةِ تَنفيذ.</para>
/// </summary>
public static class AccountDeletion
{
    // ─── مَعجَمُ الرَفضِ المُغلَق ───────────────────────────────────

    /// <summary>لا جَلسَةَ صالِحَة — ولا يُحذَفُ حِسابٌ بِلا صاحِبِه.</summary>
    public const string NotAuthenticated = "not_authenticated";

    /// <summary>لا وَثيقَةَ بِهذا المُعَرِّفِ في هذا المُستَأجِر.</summary>
    public const string UserNotFound = "user_not_found";

    /// <summary>الحِسابُ مَحذوفٌ سَلَفاً — والتِكرارُ لا يُبتَلَعُ
    /// صامِتاً، لِأَنَّ «نَجَحَ» عَن لا شَيءٍ يُخفي عَطَباً في
    /// المَسار.</summary>
    public const string AlreadyDeleted = "already_deleted";

    /// <summary>كَلِمَةُ التَأكيدِ لَم تُطابِق.</summary>
    public const string ConfirmationMismatch = "confirmation_mismatch";

    /// <summary>المَعجَمُ المُغلَق — أَربَعَةٌ لا خامِسَ لَها.</summary>
    public static readonly IReadOnlyList<string> All =
        new[] { NotAuthenticated, UserNotFound, AlreadyDeleted, ConfirmationMismatch };

    public static bool Contains(string code) => All.Contains(code, StringComparer.Ordinal);

    /// <summary>يَرمي عِندَ الخَرق — لِمَواضِعِ التَركيب. نَفسُ حيلَةِ
    /// <c>DealCancelAuthorization.Require</c> حَرفاً.</summary>
    public static string Require(string code)
    {
        if (!Contains(code))
            throw new ArgumentException(
                $"الرَمز «{code}» خارِج مَعجَم AccountDeletion. " +
                $"المَعجَم: {string.Join("، ", All)}.", nameof(code));
        return code;
    }

    /// <summary>الاسمُ الَّذي يَحِلُّ مَحَلَّ اسمِ المُستَخدِمِ بَعدَ
    /// الحَذف. قيمَةٌ مُخَزَّنَةٌ لا نَصُّ واجِهَة — بِنَفسِ دَلالَةِ
    /// <c>User.FullName</c> الافتِراضِيَّةِ المَكتوبَةِ في
    /// <c>User.cs</c> مُنذُ كُتِب.</summary>
    public const string ErasedName = "حِسابٌ مَحذوف";

    // ─── البَوّابَة ────────────────────────────────────────────────

    /// <summary>
    /// <para><c>null</c> يَعني: امضِ. والتَرتيبُ مَقصود — الغِيابُ
    /// أَوَّلاً لِأَنَّه لا يُفشي شَيئاً، ثُمَّ الحالَة، ثُمَّ
    /// التَأكيد.</para>
    ///
    /// <para><b>ولِماذا التَأكيدُ مُقارَنَةٌ نَصِّيَّةٌ لا مُرَبَّعُ
    /// اختِيار</b>: الحَذفُ لا رُجوعَ فيه، وضَغطَةٌ واحِدَةٌ تَقَعُ
    /// سَهواً. وكَلِمَةُ التَأكيدِ <b>تُمَرَّر</b> ولا تُكتَبُ هُنا،
    /// لِتَبقى نَصَّ واجِهَةٍ مِن القامُوس (القاعِدَة ١١) لا
    /// حَرفِيَّةً في بَوّابَة.</para>
    /// </summary>
    public static AccountDeletionViolation? Validate(
        User? user, string? typedConfirmation, string expectedConfirmation)
    {
        if (user is null)
            return new(UserNotFound, "لا وَثيقَةَ مُستَخدِمٍ بِهذا المُعَرِّف.");

        if (user.DeletedAt is not null)
            return new(AlreadyDeleted, "الحِسابُ مَحذوفٌ سَلَفاً — لا شَيءَ يُحذَف.");

        if (!string.Equals((typedConfirmation ?? "").Trim(), expectedConfirmation,
                StringComparison.Ordinal))
            return new(ConfirmationMismatch,
                "كَلِمَةُ التَأكيدِ لَم تُطابِق — ولَم يُحذَف شَيء.");

        return null;
    }

    public static bool IsAllowed(User? user, string? typed, string expected) =>
        Validate(user, typed, expected) is null;

    // ─── الأَثَر ───────────────────────────────────────────────────

    /// <summary>
    /// <para><b>يُزيلُ كُلَّ ما يَدُلُّ عَلى الشَخص، ويَترُكُ
    /// الصَفَّ.</b> دالَّةٌ تُعَدِّلُ الوَثيقَةَ المُمَرَّرَةَ وتُرجِعُها
    /// — والوَقتُ وَسيطٌ لا نِداءٌ لِلسّاعَة، فَالاختِبارُ حَتمِيّ.</para>
    ///
    /// <para><b>وكُلُّ حَقلٍ يُصَفَّرُ هُنا مَعدودٌ في اختِبار</b>:
    /// حَقلٌ يُضافُ إلى <see cref="User"/> ولا يُصَفَّرُ هُنا
    /// <b>يُمسَك</b> — وإلّا لَتَسَرَّبَ بَيانٌ شَخصِيٌّ جَديدٌ عَبرَ
    /// حَذفٍ يَظُنُّ صاحِبُه أَنَّه تامّ.</para>
    /// </summary>
    public static User Erase(User user, DateTime at)
    {
        user.FullName = ErasedName;
        user.Phone = "";
        user.Email = "";
        user.NationalId = null;
        user.AvatarUrl = null;
        user.PhoneVerified = false;
        user.EmailVerified = false;
        user.ActiveRole = "";
        user.AttributesJson = new Dictionary<string, string>();
        user.RoleAttributesJson = new Dictionary<string, Dictionary<string, string>>();
        user.PushSubscriptions = new List<PushSubscription>();
        user.AnchorLat = 0;
        user.AnchorLng = 0;
        user.RadiusKm = 0;
        user.DeletedAt = at;
        user.UpdatedAt = at;
        return user;
    }
}
