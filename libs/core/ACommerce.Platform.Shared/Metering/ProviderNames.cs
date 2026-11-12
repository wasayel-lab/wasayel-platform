namespace ACommerce.Platform.Shared.Metering;

/// <summary>
/// <para><b>أَسماءُ المُزَوِّدينَ والوَحَداتِ الصادِرَة</b> — أَسماءٌ مِن الكود، لا أَرقام (القاعِدَة ١٦). والوَحدَةُ نَفسُها صَفٌّ في <c>meter-units.json</c>
/// (حَدُّه <c>outbound</c>، وقارِئُ مُطابَقَتِه <c>intents</c>)، والثابِتُ هُنا رَمزُها الَّذي يَحمِلُه المُنادي.</para>
/// </summary>
public static class ProviderNames
{
    /// <summary>قَناةُ الرَسائِلِ القَصيرَةِ عَبرَ Twilio (اعتِمادُ المَنَصَّة).</summary>
    public const string Twilio = "twilio";

    /// <summary>‏(‏2026-11-12، W13-6·١٢) قَناةُ البَريدِ عَبرَ واجِهَةِ Brevo عَلى ‏443.</summary>
    public const string Brevo = "brevo";

    /// <summary>‏(‏2026-11-12، W13-6·١٢) قَناةُ البَريدِ عَبرَ خادِمِ SMTP المَضبوط (MailKit).</summary>
    public const string Smtp = "smtp";

    /// <summary>‏(‏2026-11-12، W13-6·١٣) قَناةُ نَفاذ (يَقين) — بَدءُ التَحَقُّق.</summary>
    public const string Nafath = "nafath";

    /// <summary>‏(‏2026-11-12، W13-6·١٣) مَخزَنُ الكائِناتِ المُتَوافِقُ مَع S3 — اسمُ العُدَّةِ نَفسِها (<c>S3FileStorage.ProviderName</c> بِحُروفٍ صَغيرَة)، <b>لا سلاجُ
    /// تَعريفِه في كاتالوجِ المُزَوِّدين</b>: الكاتالوجُ يُمنَعُ سلاجُه في الكود (<c>ProviderCatalogTests.No_source_file_outside_the_catalogue_names_a_provider_slug</c>)
    /// كَي يَكفِيَ المُزَوِّدَ التالي مِلَفُّ بَيانات — والعَمودُ «اسمُ المُزَوِّدِ أَو القَناة» (التَصميم §2).</summary>
    public const string S3 = "s3";

    /// <summary>‏(‏2026-11-12، W13-6·١٣) خِدمَةُ إشعارِ المُتَصَفِّح (Web Push بِمَفاتيحِ VAPID).</summary>
    public const string WebPush = "webpush";
}

/// <summary>رُموزُ الوَحَداتِ الصادِرَةِ الَّتي يَبنيها مُنادٍ في وَقتِ التَشغيل — بِحَرفِ صُفوفِها في المُعجَم.</summary>
public static class OutboundUnits
{
    /// <summary>رَمزُ دُخولٍ بِالرَسائِلِ القَصيرَة — <c>IOtpChannel.SendOtpAsync</c>.</summary>
    public const string OtpSmsSend = "otp.sms.send";

    /// <summary>إشعارٌ بِالرَسائِلِ القَصيرَة — <c>IOtpChannel.SendNoticeAsync</c>.</summary>
    public const string NoticeSmsSend = "notice.sms.send";

    /// <summary>‏(‏2026-11-12، W13-6·١٢) رَمزُ دُخولٍ بِالبَريد — <c>IEmailOtpChannel.SendOtpAsync</c>.</summary>
    public const string OtpEmailSend = "otp.email.send";

    /// <summary>‏(‏2026-11-12، W13-6·١٢) إشعارٌ بِالبَريد — <c>IEmailOtpChannel.SendNoticeAsync</c>.</summary>
    public const string NoticeEmailSend = "notice.email.send";

    /// <summary>‏(‏2026-11-12، W13-6·١٣) بَدءُ تَحَقُّقِ نَفاذ — <c>INafathChannel.StartAsync</c> (الاستِطلاعُ والرَمزُ مُساعِدانِ لا يُعَدّان).</summary>
    public const string IdentityNafathStart = "identity.nafath.start";

    /// <summary>‏(‏2026-11-12، W13-6·١٣) رَفعُ مِلَفٍّ إلى المَخزَنِ الدائِم — <c>IFileStorage.UploadAsync</c>؛ الكَمِّيَّةُ بايتاتُه.</summary>
    public const string FileUpload = "file.upload";

    /// <summary>‏(‏2026-11-12، W13-6·١٣) إشعارٌ فَوريٌّ إلى اشتِراكِ مُتَصَفِّح — <c>WebPushService.SendAsync</c>، سَطرٌ لِكُلِّ اشتِراكٍ قَبِلَته الخِدمَة.</summary>
    public const string PushSend = "push.send";
}
