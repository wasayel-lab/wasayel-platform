namespace ACommerce.Kit.Auth;

// ═══ اختِيار قَنَوات الدُخول — قَرارٌ بِالتَهيئَة، وفَشَلٌ مُغلَق ═════
//
// **العِلَّة المَقيسَة (‏2026-08-23)**: ‏`Program.cs` كانَ يُسَجِّل
// `AddMockSmsChannel()` و`AddMockNafathChannel()` **بِلا شَرطِ بيئَة**،
// و`AuthHandlers` يَجعَل الرَمزَ `channel.DevHintCode ?? random` — فَالرَمزُ
// في الإنتاج **ثابِتٌ `123456`** ويُعرَض في الواجِهَة. ومُشرِفُ المَنصَّة
// يُمنَح بِالهاتِف، فَمَن يَعرِف رَقمَه يَدخُل مُشرِفاً. أَي أَنّ
// **تَركيبَ الخِدمات وَحدَه** كانَ الفَرقَ بَينَ مَنصَّةٍ مُؤَمَّنَة
// وأُخرى مَفتوحَة لِأَيّ زائِر.
//
// **ولِماذا القَرارُ هُنا لا في `Program.cs`**: سَطرُ تَسجيلٍ في مِلَفّ
// الإقلاع لا يُختَبَر — والحَدُّ الَّذي لا يُقاس آليّاً يَنهار (القاعِدَة ٢).
// فَالقَرار دالَّةٌ خالِصَة تُقاس بِجَدوَل، والتَسجيلُ أَثَرُها.

/// <summary>نَوعُ قَناة الدُخول. ثَلاثَةٌ لا رابِع — نَفسُ تَعداد
/// <see cref="AuthChannels"/> الَّذي يُخَزَّن في وَثيقَة المُستَأجِر.</summary>
public enum AuthChannelKind { Sms, Email, Nafath }

/// <summary>المُزَوِّد المُختار لِنَوعِ قَناة.</summary>
public enum AuthChannelProvider
{
    /// <summary>لا مُزَوِّد — لا تُسَجَّل قَناة، ويُرفَض طَلَبُ الرَمز
    /// بِرِسالَةٍ مِن القامُوس. هذا هو **الفَشَلُ المُغلَق**.</summary>
    None,

    /// <summary>مُحاكٍ تَطويريّ (رَمزٌ ثابِت). مَسموحٌ في Development
    /// وَحدَها.</summary>
    Mock,

    /// <summary>‏Twilio — رَسائِل قَصيرَة فِعليَّة.</summary>
    Twilio,

    /// <summary>‏SMTP — بَريدٌ فِعليّ.</summary>
    Smtp,

    /// <summary>نَفاذ الفِعليَّة (يَقين/هَيئَة الاتِّصالات).</summary>
    Nafath
}

/// <summary>
/// عَلامَةٌ تَحمِلُها قَناةُ المُحاكاة التَطويريَّة. **لَيسَت تَجريداً
/// سابِقاً لِمُستَهلِكِه**: تُنَفِّذُها القَنَواتُ الثَلاثُ الوَهميَّة،
/// ويَقرَؤُها حارِسُ الإقلاع (<see cref="AuthChannelSelection.StubViolations"/>)
/// وشاشَةُ الدُخول (لِتَحجُبَ لَوحَةَ «وَضع التَطوير»).
///
/// <para><b>ولِماذا عَلامَةٌ لا <c>DevHintCode</c> وَحدَه</b>: مُحاكي نَفاذ
/// لا يَملِك رَمزاً مَعروضاً — يَملِك ما هو أَخطَر: <b>مُوافَقَةً
/// تِلقائِيَّة</b> عَلى أَيّ رَقم هُوِيَّة بَعدَ خَمسِ ثَوانٍ. فَفَحصُ
/// الرَمز وَحدَه كانَ سَيُمَرِّرُه.</para>
/// </summary>
public interface IDevelopmentStubChannel { }

/// <summary>قَناةٌ مُسَجَّلَةٌ فِعلاً في وِعاء الخِدَمات — كَما يَراها
/// حارِسُ الإقلاع.</summary>
/// <param name="Kind">نَوعُ القَناة.</param>
/// <param name="ChannelName">اسمُ المُزَوِّد (لِلتَشخيص والرِسالَة).</param>
/// <param name="IsDevelopmentStub">أَتَحمِلُ عَلامَةَ المُحاكاة أَو
/// رَمزاً ثابِتاً مَعروضاً؟</param>
public sealed record RegisteredAuthChannel(
    AuthChannelKind Kind, string ChannelName, bool IsDevelopmentStub);

public static class AuthChannelSelection
{
    // ─── مَفاتيحُ التَهيئَة ───────────────────────────────────────────
    // نَفسُ نَمَط `Auth:Email:Provider` القائِم مُنذُ مَوجَةِ البَريد —
    // لا نَمَطٌ جَديد، والقاعِدَةُ ٨ تَقول: استَعمِل الأُنبوبَ القائِم.
    public const string SmsProviderKey    = "Auth:Sms:Provider";
    public const string EmailProviderKey  = "Auth:Email:Provider";
    public const string NafathProviderKey = "Auth:Nafath:Provider";

    /// <summary>قيمَةُ المُحاكي في التَهيئَة.</summary>
    public const string MockValue = "mock";

    public static string ConfigKey(AuthChannelKind kind) => kind switch
    {
        AuthChannelKind.Sms    => SmsProviderKey,
        AuthChannelKind.Email  => EmailProviderKey,
        AuthChannelKind.Nafath => NafathProviderKey,
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    /// <summary>مُتَغَيِّرُ البيئَة المُقابِل لِمِفتاح التَهيئَة (‏`:` ← `__`)
    /// — وهو ما يَكتُبُه المالِك في الـSpace.</summary>
    public static string EnvVarName(AuthChannelKind kind)
        => ConfigKey(kind).Replace(":", "__");

    /// <summary>القيمَةُ الَّتي تَختار المُزَوِّدَ الفِعليّ لِكُلّ نَوع.</summary>
    public static string RealProviderValue(AuthChannelKind kind) => kind switch
    {
        AuthChannelKind.Sms    => "twilio",
        AuthChannelKind.Email  => "smtp",
        AuthChannelKind.Nafath => "nafath",
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    /// <summary>
    /// القَرار. **جَدوَلٌ لا شَرطٌ مَنثور**:
    /// <list type="bullet">
    /// <item>القيمَةُ الفِعليَّة (`twilio`/`smtp`/`nafath`) ← المُزَوِّد
    /// الفِعليّ، في أَيّ بيئَة.</item>
    /// <item>وإلّا في <c>Development</c> ← المُحاكي (الطَبَقَةُ الحَيَّة
    /// تَعتَمِدُ عَلَيه، ولا يَتَغَيَّر شَيءٌ عَمّا كان).</item>
    /// <item>وإلّا ← <see cref="AuthChannelProvider.None"/>: **فَشَلٌ
    /// مُغلَق**. وذلِك يَشمَل `mock` المَكتوبَة صَراحَةً خارِجَ التَطوير —
    /// فَالإغلاقُ الصامِت أَرخَصُ مِن تَعطيلِ المَوقِع، والحارِسُ أَدناه
    /// يُمسِك المُحاكيَ لَو تَسَرَّبَ بِسَطرِ تَسجيلٍ مُباشِر.</item>
    /// </list>
    /// </summary>
    public static AuthChannelProvider Decide(
        AuthChannelKind kind, string? configured, bool isDevelopment)
    {
        var value = configured?.Trim();
        if (string.Equals(value, RealProviderValue(kind), StringComparison.OrdinalIgnoreCase))
            return kind switch
            {
                AuthChannelKind.Sms    => AuthChannelProvider.Twilio,
                AuthChannelKind.Email  => AuthChannelProvider.Smtp,
                AuthChannelKind.Nafath => AuthChannelProvider.Nafath,
                _ => AuthChannelProvider.None
            };
        return isDevelopment ? AuthChannelProvider.Mock : AuthChannelProvider.None;
    }

    // ─── حارِسُ الإقلاع ───────────────────────────────────────────────
    /// <summary>
    /// القَنَواتُ المُحاكيَة المُسَجَّلَةُ خارِجَ التَطوير — واحِدَةٌ
    /// تَكفي لِيَكونَ الرَمزُ ثابِتاً لِلجَميع.
    ///
    /// <para><b>ولِماذا «خارِجَ التَطوير» لا «في الإنتاج» فَقَط</b>: بيئَةٌ
    /// ثالِثَة (‏Staging مَثَلاً) لَيسَت Development ولا Production، وهي
    /// بِالضَبط الشَقُّ الَّذي يَنسى. الشَرطُ الأَوسَعُ يُغلِقُه.</para>
    /// </summary>
    public static IReadOnlyList<string> StubViolations(
        bool isDevelopment, IEnumerable<RegisteredAuthChannel> registered)
    {
        if (isDevelopment) return Array.Empty<string>();
        return registered
            .Where(c => c.IsDevelopmentStub)
            .Select(c => $"{c.Kind}:{c.ChannelName}")
            .ToList();
    }

    /// <summary>يَرمي إن سُجِّلَت قَناةُ مُحاكاةٍ خارِجَ التَطوير. يُستَدعى
    /// **بَعدَ** بِناء المُضيف وقَبلَ أَوَّلِ طَلَب.</summary>
    public static void AssertNoStubsOutsideDevelopment(
        bool isDevelopment, IEnumerable<RegisteredAuthChannel> registered)
    {
        var violations = StubViolations(isDevelopment, registered);
        if (violations.Count == 0) return;
        throw new InvalidOperationException(
            "قَناةُ دُخولٍ مُحاكيَةٌ مُسَجَّلَةٌ خارِجَ بيئَة التَطوير: "
            + string.Join("، ", violations)
            + ". اضبِط "
            + string.Join(" / ", Enum.GetValues<AuthChannelKind>().Select(EnvVarName))
            + " بِمُزَوِّدٍ فِعليّ، أَو اترُكها فارِغَةً لِتُغلَق القَناة.");
    }
}
