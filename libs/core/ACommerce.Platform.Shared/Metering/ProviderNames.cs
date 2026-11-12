namespace ACommerce.Platform.Shared.Metering;

/// <summary>
/// <para><b>أَسماءُ المُزَوِّدينَ والوَحَداتِ الصادِرَة</b> — أَسماءٌ مِن الكود، لا أَرقام (القاعِدَة ١٦). والوَحدَةُ نَفسُها صَفٌّ في <c>meter-units.json</c>
/// (حَدُّه <c>outbound</c>، وقارِئُ مُطابَقَتِه <c>intents</c>)، والثابِتُ هُنا رَمزُها الَّذي يَحمِلُه المُنادي.</para>
/// </summary>
public static class ProviderNames
{
    /// <summary>قَناةُ الرَسائِلِ القَصيرَةِ عَبرَ Twilio (اعتِمادُ المَنَصَّة).</summary>
    public const string Twilio = "twilio";
}

/// <summary>رُموزُ الوَحَداتِ الصادِرَةِ الَّتي يَبنيها مُنادٍ في وَقتِ التَشغيل — بِحَرفِ صُفوفِها في المُعجَم.</summary>
public static class OutboundUnits
{
    /// <summary>رَمزُ دُخولٍ بِالرَسائِلِ القَصيرَة — <c>IOtpChannel.SendOtpAsync</c>.</summary>
    public const string OtpSmsSend = "otp.sms.send";

    /// <summary>إشعارٌ بِالرَسائِلِ القَصيرَة — <c>IOtpChannel.SendNoticeAsync</c>.</summary>
    public const string NoticeSmsSend = "notice.sms.send";
}
