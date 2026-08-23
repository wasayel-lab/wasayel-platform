using ACommerce.Kit.Auth;
using Xunit;

namespace ACommerce.Platform.Tests;

// ═══ قَرارُ قَنَوات الدُخول — الجَدوَلُ الَّذي يَفصِل بَينَ مَنصَّةٍ
//     مُؤَمَّنَةٍ وأُخرى تَقبَل `123456` لِأَيّ هاتِف ══════════════════
//
// **المَقيسُ قَبل (‏2026-08-23)**: ‏`Program.cs:58-59` يُسَجِّل
// `AddMockSmsChannel()` و`AddMockNafathChannel()` بِلا شَرطِ بيئَة،
// و`AuthHandlers` يَجعَل الرَمزَ `channel.DevHintCode ?? random`. فَفي
// الإنتاج: رَمزٌ ثابِتٌ لِلجَميع، مَعروضٌ في الواجِهَة، ومُشرِفُ المَنصَّة
// يُمنَح بِالهاتِف — أَي أَنّ مَن يَعرِف رَقمَ المالِك يَدخُل مُشرِفاً.
//
// هذِه الاختِبارات هي **الحَدُّ المَقيس** (القاعِدَة ٢): سَطرُ تَسجيلٍ
// في مِلَفّ الإقلاع لا يُختَبَر، أَمّا الجَدوَلُ فَيُختَبَر.

public class AuthChannelSelectionTests
{
    // ─── الإنتاج بِلا تَهيئَة: لا قَناة ───────────────────────────────

    [Theory]
    [InlineData(AuthChannelKind.Sms)]
    [InlineData(AuthChannelKind.Email)]
    [InlineData(AuthChannelKind.Nafath)]
    public void Production_WithoutConfiguration_SelectsNothing(AuthChannelKind kind)
        => Assert.Equal(AuthChannelProvider.None,
            AuthChannelSelection.Decide(kind, configured: null, isDevelopment: false));

    [Theory]
    [InlineData(AuthChannelKind.Sms)]
    [InlineData(AuthChannelKind.Email)]
    [InlineData(AuthChannelKind.Nafath)]
    public void Production_WithEmptyConfiguration_SelectsNothing(AuthChannelKind kind)
        => Assert.Equal(AuthChannelProvider.None,
            AuthChannelSelection.Decide(kind, configured: "   ", isDevelopment: false));

    /// <summary>‏`mock` مَكتوبَةً صَراحَةً خارِجَ التَطوير **لا تُصَدَّق**.
    /// وهذا هو الشَقُّ الَّذي كانَ يَنسى: القاعِدَةُ لَو كانَت «الافتِراضيّ
    /// عِندَ الغِياب» وَحدَها لَبَقِيَ سَطرٌ واحِدٌ في الـSpace كافِياً
    /// لِإعادَة فَتحِ البابِ كامِلاً.</summary>
    [Theory]
    [InlineData(AuthChannelKind.Sms)]
    [InlineData(AuthChannelKind.Email)]
    [InlineData(AuthChannelKind.Nafath)]
    public void Production_WithExplicitMock_StillSelectsNothing(AuthChannelKind kind)
        => Assert.Equal(AuthChannelProvider.None,
            AuthChannelSelection.Decide(kind, AuthChannelSelection.MockValue, isDevelopment: false));

    /// <summary>قيمَةٌ مَجهولَة (خَطَأ مَطبَعيّ: `twillio`) لا تَرتَدّ إلى
    /// مُحاكٍ — تُغلِق. الارتِدادُ الصامِتُ هو ما جَعَل التَعدادَ
    /// يَنكَسِر في `AuthChannels` مِن قَبل.</summary>
    [Theory]
    [InlineData(AuthChannelKind.Sms, "twillio")]
    [InlineData(AuthChannelKind.Email, "sendgrid")]
    [InlineData(AuthChannelKind.Nafath, "yakeen")]
    public void Production_WithUnknownValue_SelectsNothing(AuthChannelKind kind, string value)
        => Assert.Equal(AuthChannelProvider.None,
            AuthChannelSelection.Decide(kind, value, isDevelopment: false));

    // ─── الإنتاج مَع مُزَوِّدٍ فِعليّ: يَعمَل، وحدَه ──────────────────

    [Theory]
    [InlineData(AuthChannelKind.Sms, AuthChannelProvider.Twilio)]
    [InlineData(AuthChannelKind.Email, AuthChannelProvider.Smtp)]
    [InlineData(AuthChannelKind.Nafath, AuthChannelProvider.Nafath)]
    public void Production_WithRealProvider_SelectsIt(
        AuthChannelKind kind, AuthChannelProvider expected)
        => Assert.Equal(expected, AuthChannelSelection.Decide(
            kind, AuthChannelSelection.RealProviderValue(kind), isDevelopment: false));

    /// <summary>حالَةُ المالِك المَنصوصَة: ‏`smtp` مَضبوطَةٌ والهاتِف لا —
    /// فَالبَريدُ يَعمَل والهاتِفُ يُرفَض. القَراران **مُستَقِلّان**.</summary>
    [Fact]
    public void Production_EmailConfiguredAlone_LeavesPhoneClosed()
    {
        Assert.Equal(AuthChannelProvider.Smtp,
            AuthChannelSelection.Decide(AuthChannelKind.Email, "smtp", isDevelopment: false));
        Assert.Equal(AuthChannelProvider.None,
            AuthChannelSelection.Decide(AuthChannelKind.Sms, null, isDevelopment: false));
        Assert.Equal(AuthChannelProvider.None,
            AuthChannelSelection.Decide(AuthChannelKind.Nafath, null, isDevelopment: false));
    }

    [Theory]
    [InlineData("SMTP")]
    [InlineData("  smtp  ")]
    public void ProviderValue_IsCaseAndSpaceInsensitive(string value)
        => Assert.Equal(AuthChannelProvider.Smtp,
            AuthChannelSelection.Decide(AuthChannelKind.Email, value, isDevelopment: false));

    // ─── نَقلٌ ثانٍ لِلبَريد: `brevo` عَبر HTTPS ──────────────────────
    // **السَبَبُ مَقيسٌ لا تَفضيليّ**: الـSpace يَحجُب مَنافِذَ SMTP
    // الصادِرَة، فَـ`smtp` مَضبوطَةً ضَبطاً صَحيحاً تَفشَل. والـ443 يَعبُر.

    [Theory]
    [InlineData("brevo")]
    [InlineData("BREVO")]
    [InlineData("  brevo  ")]
    public void Production_WithBrevo_SelectsIt(string value)
        => Assert.Equal(AuthChannelProvider.Brevo,
            AuthChannelSelection.Decide(AuthChannelKind.Email, value, isDevelopment: false));

    /// <summary>و`brevo` **لِلبَريدِ وَحدَه**: القيمَةُ نَفسُها على قَناةِ
    /// الرَسائِلِ أَو نَفاذٍ تُغلِق كَأَيّ قيمَةٍ مَجهولَة. الجَدوَلُ
    /// لِكُلّ نَوعٍ مُستَقِلّ، ولا تَتَسَرَّب قيمَةٌ بَينَ الأَنواع.</summary>
    [Theory]
    [InlineData(AuthChannelKind.Sms)]
    [InlineData(AuthChannelKind.Nafath)]
    public void Brevo_IsNotAValueForOtherKinds(AuthChannelKind kind)
        => Assert.Equal(AuthChannelProvider.None,
            AuthChannelSelection.Decide(kind, "brevo", isDevelopment: false));

    /// <summary>القيَمُ الفِعليَّةُ مُثَبَّتَة — يَكتُبُها المالِكُ بِيَدِه
    /// في الـSpace، وحَرفٌ فيها يَعني باباً مُغلَقاً بِلا سَبَبٍ ظاهِر.</summary>
    [Fact]
    public void EmailKind_HasExactlyTwoRealTransports()
    {
        var values = AuthChannelSelection.RealProviders(AuthChannelKind.Email);
        Assert.Equal(new[] { "smtp", "brevo" }, values.Select(v => v.Value));
        Assert.Equal(
            new[] { AuthChannelProvider.Smtp, AuthChannelProvider.Brevo },
            values.Select(v => v.Provider));
    }

    [Theory]
    [InlineData(AuthChannelKind.Sms, "twilio")]
    [InlineData(AuthChannelKind.Nafath, "nafath")]
    public void OtherKinds_KeepASingleRealTransport(AuthChannelKind kind, string value)
    {
        var only = Assert.Single(AuthChannelSelection.RealProviders(kind));
        Assert.Equal(value, only.Value);
    }

    /// <summary>وقيمَةٌ مَجهولَةٌ تَبقى إغلاقاً بَعدَ إضافَةِ الثانِيَة —
    /// الجَدوَلُ نَما ولَم يَنفَتِح.</summary>
    [Theory]
    [InlineData("brevo2")]
    [InlineData("brev")]
    [InlineData("sendinblue")]
    public void Production_WithANearMissForBrevo_StillSelectsNothing(string value)
        => Assert.Equal(AuthChannelProvider.None,
            AuthChannelSelection.Decide(AuthChannelKind.Email, value, isDevelopment: false));

    // ─── التَطوير: كَما كان ───────────────────────────────────────────

    /// <summary>الطَبَقَةُ الحَيَّةُ والبَوّابَةُ البايتِيَّة تَعتَمِدان
    /// المُحاكي. سُقوطُه هُنا يَعني جَلسَةً كامِلَةً بِلا دُخول.</summary>
    [Theory]
    [InlineData(AuthChannelKind.Sms)]
    [InlineData(AuthChannelKind.Email)]
    [InlineData(AuthChannelKind.Nafath)]
    public void Development_WithoutConfiguration_KeepsTheMock(AuthChannelKind kind)
        => Assert.Equal(AuthChannelProvider.Mock,
            AuthChannelSelection.Decide(kind, configured: null, isDevelopment: true));

    [Fact]
    public void Development_WithRealProvider_StillHonoursIt()
        => Assert.Equal(AuthChannelProvider.Smtp,
            AuthChannelSelection.Decide(AuthChannelKind.Email, "smtp", isDevelopment: true));

    // ─── أَسماءُ المَفاتيح — يَكتُبُها المالِكُ بِيَدِه في الـSpace ────
    // خَطَأُ حَرفٍ فيها يَعني قَناةً مَقفولَةً بِلا سَبَبٍ ظاهِر، أَو
    // (‏أَسوَأ) مَقروءَةً مِن مِفتاحٍ آخَر. فَتُثَبَّت.

    [Theory]
    [InlineData(AuthChannelKind.Sms, "Auth:Sms:Provider", "Auth__Sms__Provider")]
    [InlineData(AuthChannelKind.Email, "Auth:Email:Provider", "Auth__Email__Provider")]
    [InlineData(AuthChannelKind.Nafath, "Auth:Nafath:Provider", "Auth__Nafath__Provider")]
    public void ConfigKeys_ArePinned(AuthChannelKind kind, string key, string envVar)
    {
        Assert.Equal(key, AuthChannelSelection.ConfigKey(kind));
        Assert.Equal(envVar, AuthChannelSelection.EnvVarName(kind));
    }

    /// <summary>مِفتاحُ البَريد هو **نَفسُه** الَّذي كانَ يَقرَؤُه
    /// `Program.cs` قَبلَ هذِه المَوجَة — فَالمالِكُ الَّذي ضَبَطَه لا
    /// يُعيد ضَبطَه، ولا يُبنى نَمَطٌ ثانٍ إلى جانِبِ القائِم.</summary>
    [Fact]
    public void EmailKey_IsTheOneThatAlreadyExisted()
        => Assert.Equal("Auth:Email:Provider", AuthChannelSelection.EmailProviderKey);

    // ─── حارِسُ الإقلاع ───────────────────────────────────────────────

    private static RegisteredAuthChannel Stub(AuthChannelKind kind, string name)
        => new(kind, name, IsDevelopmentStub: true);

    private static RegisteredAuthChannel Real(AuthChannelKind kind, string name)
        => new(kind, name, IsDevelopmentStub: false);

    [Fact]
    public void BootGuard_Throws_WhenAMockIsRegisteredOutsideDevelopment()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            AuthChannelSelection.AssertNoStubsOutsideDevelopment(
                isDevelopment: false,
                new[] { Stub(AuthChannelKind.Sms, "MockSms"), Real(AuthChannelKind.Email, "Smtp") }));
        Assert.Contains("MockSms", ex.Message);
        // الرِسالَةُ تَقول **ماذا يُضبَط**، لا «فَشِل الإقلاع» وَحدَها.
        Assert.Contains("Auth__Sms__Provider", ex.Message);
    }

    /// <summary>مُحاكي نَفاذ لا `DevHintCode` لَه — يُمسَك بِالعَلامَة.
    /// فَحصُ الرَمزِ وَحدَه كانَ سَيُمَرِّرُ مُوافَقَةً تِلقائِيَّةً على
    /// أَيّ رَقم هُوِيَّة.</summary>
    [Fact]
    public void BootGuard_CatchesTheNafathMock_WhichHasNoHintCode()
    {
        var violations = AuthChannelSelection.StubViolations(
            isDevelopment: false, new[] { Stub(AuthChannelKind.Nafath, "MockNafath") });
        Assert.Single(violations);
        Assert.Contains("MockNafath", violations[0]);
    }

    [Fact]
    public void BootGuard_Silent_WhenEveryChannelIsReal()
        => AuthChannelSelection.AssertNoStubsOutsideDevelopment(
            isDevelopment: false,
            new[] { Real(AuthChannelKind.Sms, "Twilio"), Real(AuthChannelKind.Email, "Smtp") });

    [Fact]
    public void BootGuard_Silent_WhenNothingIsRegisteredAtAll()
        => AuthChannelSelection.AssertNoStubsOutsideDevelopment(
            isDevelopment: false, Array.Empty<RegisteredAuthChannel>());

    [Fact]
    public void BootGuard_Silent_InDevelopment()
        => AuthChannelSelection.AssertNoStubsOutsideDevelopment(
            isDevelopment: true,
            new[] { Stub(AuthChannelKind.Sms, "MockSms"), Stub(AuthChannelKind.Nafath, "MockNafath") });

    // ─── العَلامَةُ على القَنَوات الوَهمِيَّة نَفسِها ─────────────────
    // الحارِسُ يَقرَأُ عَلامَةً — فَإن سَقَطَت عَن مُحاكٍ صَمَتَ الحارِس.

    [Fact]
    public void MockChannels_CarryTheStubMarker()
    {
        Assert.True(typeof(IDevelopmentStubChannel).IsAssignableFrom(
            typeof(ACommerce.Kit.Auth.Providers.MockEmail.MockEmailChannel)),
            "مُحاكي البَريد يَجِب أَن يَحمِل عَلامَةَ المُحاكاة");
    }

    /// <summary>ومُقابِلُها: المُزَوِّدُ الفِعليُّ **لا** يَحمِلُها،
    /// وإلّا لَأَغلَقَ الحارِسُ الإنتاجَ على المَضبوط.</summary>
    [Fact]
    public void RealChannels_DoNotCarryTheStubMarker()
    {
        Assert.False(typeof(IDevelopmentStubChannel).IsAssignableFrom(
            typeof(ACommerce.Kit.Auth.Providers.Smtp.SmtpEmailChannel)));
    }
}
