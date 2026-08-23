using ACommerce.Kit.Auth;

namespace ACommerce.Templates.Customer.Marketplace.Services.Incubator;

/// <summary>مَن يُمنَح صَلاحِيَّةَ مُشرِف المَنصَّة في هذا الإقلاع — بَعدَ
/// البَوّابَتَين والتَطبيع. <c>null</c> في الحَقلَين = لا عَمَل إطلاقاً.</summary>
/// <param name="Phone">الهاتِف مُشَذَّباً، أَو <c>null</c>.</param>
/// <param name="Email">البَريد **مُطَبَّعاً بِـ<see cref="EmailAddress.Normalize"/>**
/// (قَصّ + تَصغير)، أَو <c>null</c>.</param>
/// <param name="EmailRejected">‏<c>PLATFORM_ADMIN_EMAIL</c> مَضبوطٌ بِصيغَةٍ
/// غَير صالِحَة. يُقال في اللوغ ولا يُمنَح — راجِع
/// <see cref="PlatformAdminGrant"/>.</param>
public readonly record struct PlatformAdminGrantRequest(
    string? Phone, string? Email, bool EmailRejected)
{
    public bool IsEmpty => Phone is null && Email is null;

    public static PlatformAdminGrantRequest None => new(null, null, false);
}

/// <summary>
/// قَرارُ مَنح صَلاحِيَّة مُشرِف المَنصَّة — <b>جَدوَلٌ نَقِيّ بِلا I/O</b>،
/// عَلى غِرار <see cref="AuthChannelSelection"/>: سَطرُ بَذرٍ في مِلَفّ
/// الإقلاع لا يُختَبَر، أَمّا الجَدوَلُ فَيُختَبَر (القاعِدَة ٢).
///
/// <para><b>البَوّابَتان — بِنَفسِهِما لِلمُعَرِّفَين:</b>
/// ‏(أ) <c>PLATFORM_ADMIN_PHONE</c> و/أَو <c>PLATFORM_ADMIN_EMAIL</c> — مَن.
/// غِيابُهُما مَعاً = لا عَمَل إطلاقاً.
/// ‏(ب) بيئَة <c>Development</c>، أَو <c>PLATFORM_ADMIN_BOOTSTRAP=1</c>
/// خارِجَها. البَوّابَةُ الثانِيَةُ **واحِدَة لِلاثنَين**: البَريدُ لا
/// يَفتَح باباً أَوسَعَ مِمّا يَفتَحُه الهاتِف.</para>
///
/// <para><b>لِماذا البَريدُ أَصلاً (المَقيسُ ‏2026-08-23):</b> بَعدَ
/// <c>cd43b366</c> صارَت قَناةُ الرَسائِل القَصيرَة تُغلَق في الإنتاج بِلا
/// <c>Auth__Sms__Provider</c>. والمالِكُ سَيَضبُط <b>SMTP وَحدَه</b>. فَلَو
/// بَقِيَ المُعَرِّفُ هاتِفاً حَصريّاً لَكانَ كُلُّ دُخولٍ بِالبَريد
/// مُستَخدِماً آخَرَ بِلا صَلاحِيَّة.</para>
///
/// <para><b>والتَطبيعُ لَيسَ تَفصيلاً — هُوَ الرَبطُ نَفسُه:</b> البَريد
/// يُطَبَّع هُنا بِـ<see cref="EmailAddress.Normalize"/> — **نَفسُ الدالَّة
/// بِعَينِها** الَّتي يُطَبِّع بِها مَسارُ الدُخول (<c>auth/email/login</c>
/// و<c>auth/email/verify</c>) و<c>AuthHandlers.RequestEmailOtpHandler</c>
/// و<c>VerifyEmailOtpHandler</c> قَبلَ البَحث. فَلَو طُبِّعَ هُنا بِطَريقَةٍ
/// أُخرى لَصارَت الصَلاحِيَّةُ مَمنوحَةً لِعُنوانٍ **لا يُطابِقُه الدُخول** —
/// ثَغرَةٌ صامِتَةٌ لا تُقال في أَيّ رِسالَةِ خَطَإ. مَوضِعُ التَطبيعِ
/// واحِدٌ عَمداً، وهذا الجَدوَلُ يُحيلُ إلَيه ولا يُعيدُ كِتابَتَه.</para>
///
/// <para><b>وصيغَةٌ غَير صالِحَةٍ تُغلِق ولا تَرتَدّ:</b> بِنَفس مَنطِق
/// «قيمَةٌ مَجهولَةٌ لا تَرتَدّ إلى مُحاكٍ» في <see cref="AuthChannelSelection"/>.
/// خَطَأُ حَرفٍ في العُنوان يَعني <b>لا مَنح</b> — لا مُستَخدِماً مَخلوقاً
/// بِعُنوانٍ مُشَوَّهٍ لا يَبلُغُه بَريد. ويُقال في اللوغ بِاسم المُتَغَيِّر.</para>
/// </summary>
public static class PlatformAdminGrant
{
    public const string PhoneVar       = "PLATFORM_ADMIN_PHONE";
    public const string EmailVar       = "PLATFORM_ADMIN_EMAIL";
    public const string BootstrapVar   = "PLATFORM_ADMIN_BOOTSTRAP";
    public const string BootstrapValue = "1";

    public static PlatformAdminGrantRequest Decide(
        string? phoneVar, string? emailVar, bool isDevelopment, string? bootstrapVar)
    {
        var phone = phoneVar?.Trim();
        if (string.IsNullOrEmpty(phone)) phone = null;

        string? email = null;
        var emailRejected = false;
        if (!string.IsNullOrWhiteSpace(emailVar))
        {
            // مَوضِعُ التَطبيعِ الواحِد — نَفسُ دالَّةِ مَسارِ الدُخول.
            var normalized = EmailAddress.Normalize(emailVar);
            if (EmailAddress.IsValid(normalized)) email = normalized;
            else emailRejected = true;
        }

        // البَوّابَةُ الأولى: طَلَبٌ صَريحٌ بِمُعَرِّف. غِيابُهُما = صَمت.
        if (phone is null && email is null && !emailRejected)
            return PlatformAdminGrantRequest.None;

        // البَوّابَةُ الثانِيَة: خارِجَ التَطوير يَلزَم إقرارٌ مُنفَصِل.
        if (!isDevelopment && bootstrapVar != BootstrapValue)
            return PlatformAdminGrantRequest.None;

        return new(phone, email, emailRejected);
    }
}
