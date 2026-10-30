using ACommerce.Kit.Auth;
using Microsoft.Extensions.DependencyInjection;

namespace ACommerce.Templates.Customer.Marketplace.Services;

/// <summary>
/// <para><b>أَيُّ القَنَواتِ تَعرِضُها صَفحَةُ دُخولِ المَتجَر</b> (‏2026-10-29، تَدقيقُ الموجَة ١١ A2؛ تَوصيفُه <c>beacb18</c>). كانَت الصَفحَةُ تَرسُمُ
/// استِمارَةً واحِدَةً مِن <c>Tenant.AuthChannel</c>، و«حِسابي» يَعرِضُ «أَضِف بَريدَك» ونَصُّه يَعِدُ «فَتَدخُلُ بَعدَها بِجَوّالِكَ أَو بِبَريدِك» —
/// فَالوَسيلَةُ المَربوطَةُ لا تُبلَغُ بِنَقرَة (القاعِدَة ١٢)، وأَبوابُ الدُخولِ بِالبَريدِ والجَوّالِ تَقبَلُ أَيَّ مَتجَرٍ بِطَلَبٍ مَصنوع (بابُ A3 الخَفِيّ).</para>
///
/// <para><b>القَرار</b> (د27 بِنَصِّه لِمَتاجِرِ الجَوّالِ والبَريد: «ويَستَطيعُ بَعدَها الدُخولَ بِأَيِّهِما»؛ وP2 لِقَناةٍ اختارَها صاحِبُ المَتجَر):
/// مَتجَرُ الجَوّالِ يَعرِضُ الجَوّالَ والبَريدَ إن سُجِّلَت قَناتُه؛ ومَتجَرُ البَريدِ البَريدَ والجَوّالَ إن سُجِّلَت قَناتُه؛ <b>ومَتجَرُ نَفاذٍ نَفاذاً وَحدَه</b> —
/// اختارَ صاحِبُه الدُخولَ بِالهُوِيَّة. والأُولى قَناةُ المَتجَرِ دائِماً (وإن لَم تُسَجَّل: تَرسُمُ الصَفحَةُ لافِتَتَها كَما كانَت).
/// <b>ومُستَهلِكوه ثَلاثَة</b>: <c>Login.razor</c> (الاستِمارَةُ والمِفتاحُ إلى الأُخرى)، و<c>Me.razor</c> (لا «أَضِف» لِنَوعٍ لا يُدخِل)، وأَبوابُ الدُخولِ
/// الأَربَعَة وبابُ الرَبط (تَرُدُّ ما لا يُعرَضُ بِرَمزٍ ثابِت) — فَالنُصوصُ صادِقَةٌ بِالبِناء.</para>
///
/// <para><b>‏(‏2026-10-30، مُراجَعَةُ الموجَة ١١ لِلمَسار F1؛ تَوصيفُه <c>c3aa888</c> — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه) والثانِيَةُ تُدخِلُ ولا تُسَجِّل</b>
/// (<see cref="Registers"/>): كانَ بابُ تَأكيدِها يُنادي المُعالِجَ الَّذي يُنشِئ، فَبَريدٌ لا يَحمِلُه حِسابٌ في مَتجَرِ جَوّالٍ صارَ حِساباً ثانِياً بِلا جَوّال —
/// المُشكِلَةُ الَّتي قَرَّرَ د27 «ج» عِلاجَها بِالرَبط، وتَجاوُزٌ لِقَناةٍ اختارَها صاحِبُ المَتجَر (P2). فَقَناةُ المَتجَرِ وَحدَها تُسَجِّل، والثانِيَةُ تُدخِلُ مَن رَبَطَها
/// مِن «حِسابي» مُتَحَقَّقاً مِنها (<c>AuthHandlers.VerifyEmailOtpAsync</c>/<c>VerifyPhoneOtpAsync</c>) وتَرُدُّ ما سِواه بِـ<c>AuthHandlers.NotLinked</c>
/// بَعدَ الرَمزِ لا قَبلَه. <b>ومُستَهلِكوه ثَلاثَة</b>: بابا التَأكيدِ بِالجَوّالِ والبَريد، و<c>Login.razor</c> (سَطرُ «لِمَن رَبَطَها» تَحتَ استِمارَتِها).</para>
/// </summary>
public static class LoginChannels
{
    /// <summary>رَمزُ رَدِّ بابِ دُخولٍ بِقَناةٍ لا يَعرِضُها المَتجَر — يُتَرجِمُه القامُوس (<c>auth.login.err_channel_not_offered</c>).</summary>
    public const string NotOffered = "channel_not_offered";

    /// <summary>اسمُ مِفتاحِ الاستِعلامِ الَّذي يَحمِلُ القَناةَ الثانِيَةَ في رابِطِ الصَفحَة — وتُبقيه مَرحَلَةُ الرَمز.</summary>
    public const string ViaParam = "via";

    /// <summary>قَناةُ المَتجَرِ كَما تَرسُمُها الصَفحَة: نَفاذ، أَو بَريد، والجَوّالُ لِما سِواهُما (<see cref="AuthChannels.NormalizeOrDefault"/>).</summary>
    public static string Primary(string? authChannel) => AuthChannels.NormalizeOrDefault(authChannel);

    /// <summary>القَنَواتُ المَعروضَةُ بِتَرتيبِها — قَناةُ المَتجَرِ أَوَّلاً، ثُمَّ الأُخرى إن سُجِّلَت قَناتُها. نَقِيَّة.</summary>
    public static IReadOnlyList<string> Offered(string? authChannel, bool phoneRegistered, bool emailRegistered)
        => Primary(authChannel) switch
        {
            AuthChannels.Phone => emailRegistered ? [AuthChannels.Phone, AuthChannels.Email] : [AuthChannels.Phone],
            AuthChannels.Email => phoneRegistered ? [AuthChannels.Email, AuthChannels.Phone] : [AuthChannels.Email],
            _ => [AuthChannels.Nafath],
        };

    /// <summary>الحافَّةُ الَّتي تَقرَأُ الوِعاء — ما سَجَّلَه <c>AuthChannelSelection</c> في <c>Program.cs</c> (نَسَقُ <c>TenantAuthChannelDoor.OfferedIn</c>).</summary>
    public static IReadOnlyList<string> OfferedIn(string? authChannel, IServiceProvider services)
        => Offered(authChannel, services.GetService<IOtpChannel>() is not null, services.GetService<IEmailOtpChannel>() is not null);

    /// <summary>أَيَعرِضُ المَتجَرُ <paramref name="kind"/>؟ <paramref name="kindRegistered"/> قَناةُ النَوعِ نَفسِه — وتُسأَلُ لِلثانِيَةِ وَحدَها: الأُولى
    /// مَعروضَةٌ دائِماً، وغِيابُ قَناتِها رَمزُه <c>*_unavailable</c> كَما كان.</summary>
    public static bool Offers(string? authChannel, string kind, bool kindRegistered)
        => Offered(authChannel,
                phoneRegistered: kind == AuthChannels.Phone && kindRegistered,
                emailRegistered: kind == AuthChannels.Email && kindRegistered)
            .Contains(kind);

    /// <summary>أَتُسَجِّلُ <paramref name="kind"/> حِساباً جَديداً في هذا المَتجَر؟ قَناةُ المَتجَرِ وَحدَها (<see cref="Primary"/>)؛ والثانِيَةُ تُدخِلُ مَن يَحمِلُ
    /// الوَسيلَةَ مُتَحَقَّقاً مِنها ولا تُنشِئ (‏2026-10-30). نَقِيَّة.</summary>
    public static bool Registers(string? authChannel, string kind) => kind == Primary(authChannel);

    /// <summary>الاستِمارَةُ الَّتي تُرسَم: <paramref name="via"/> إن عُرِضَت، وإلّا قَناةُ المَتجَر.</summary>
    public static string Active(string? authChannel, string? via, IReadOnlyList<string> offered)
        => via is not null && offered.Contains(via) ? via : Primary(authChannel);

    /// <summary>ذَيلُ الاستِعلامِ الَّذي يُبقي القَناةَ الثانِيَةَ في تَحويلاتِ بابِها (<c>&amp;via=email</c>)، وفارِغٌ لِقَناةِ المَتجَر — فَرابِطُ القَناةِ
    /// الأُولى بايتاتُه كَما كانَت.</summary>
    public static string ViaSuffix(string? authChannel, string kind)
        => kind == Primary(authChannel) ? "" : $"&{ViaParam}={kind}";
}
