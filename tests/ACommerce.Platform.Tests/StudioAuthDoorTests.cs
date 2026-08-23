using ACommerce.Kit.Auth;
using ACommerce.Kit.Auth.Server;
using ACommerce.Templates.Customer.Marketplace.Services.Incubator;
using Xunit;

namespace ACommerce.Platform.Tests;

// ═══ بابُ جَلسَةِ الاستوديو — الباب الوَحيد الَّذي يُنتِج جَلسَةَ
//     مُشرِفِ مَنَصَّة ════════════════════════════════════════════════
//
// **المَقيسُ قَبل (‏2026-08-23)**: ‏`cd43b366` جَعَلَ قَنَواتِ
// المُستَأجِرينَ قَراراً بِالتَهيئَةِ وأَغلَقَ غِيابَها، لكِنّ بابَ
// الاستوديو **لَم يَمُرّ بِأَيّ قَناة**: صِفرُ إشارَةٍ إلى `IOtpChannel`
// أَو `IEmailOtpChannel` في مُجَلَّد `Incubator` كُلِّه، والتَحَقُّقُ
// `code.Trim() != StudioAuth.DevCode` حَيثُ `DevCode = "123456"` — بِلا
// شَرطِ بيئَة. فَمَن يَعرِف هاتِفَ المالِكِ (‏`PLATFORM_ADMIN_PHONE`)
// كانَ يَدخُل مُشرِفَ مَنَصَّةٍ في الإنتاج.
//
// و`3c298f2c` جَعَلَ المَنحَ مُمكِناً بِالبَريدِ وقالَ صَراحَةً إنّ
// المَمنوحَ بِالبَريدِ «مُهَيَّأٌ ولا يُبلَغ بِنَقرَةٍ بَعد» — لَم يَكُن
// لِلاستوديو بابُ بَريدٍ إطلاقاً.
//
// هذِه الاختِبارات هي **الحَدُّ المَقيس** (القاعِدَة ٢).

public class StudioAuthDoorTests
{
    // ─── جَدوَلُ القَرار: ما يَعرِضُه البابُ في كُلّ بيئَة ─────────────
    //
    // التَسجيلُ أَثَرُ `AuthChannelSelection.Decide`، فَالجَدوَلُ يُركِّب
    // الدالَّتَينِ مَعاً بَدَلَ أَن يَنسَخَ نَتيجَةَ الأولى نَصّاً.

    private static IReadOnlyList<StudioAuthMethod> DoorIn(
        bool isDevelopment, string? smsProvider, string? emailProvider)
        => StudioAuthDoor.Offered(
            AuthChannelSelection.Decide(AuthChannelKind.Sms, smsProvider, isDevelopment)
                != AuthChannelProvider.None,
            AuthChannelSelection.Decide(AuthChannelKind.Email, emailProvider, isDevelopment)
                != AuthChannelProvider.None);

    /// <summary>الإنتاجُ بِلا تَهيئَة: <b>لا بابَ إطلاقاً</b> — وهذا هُوَ
    /// السَطرُ الَّذي كانَ كاذِباً قَبلَ اليَوم.</summary>
    [Fact]
    public void Production_WithoutConfiguration_OffersNothing()
        => Assert.Empty(DoorIn(isDevelopment: false, null, null));

    /// <summary>حالَةُ المالِك المَنصوصَة: ‏SMTP وَحدَها مَضبوطَة ⇒
    /// البَريدُ يَعمَل، <b>والهاتِفُ مَرفوض</b> ولا يُعرَض زِرُّه.</summary>
    [Fact]
    public void Production_WithSmtpOnly_OffersEmailAndRefusesPhone()
    {
        var door = DoorIn(isDevelopment: false, smsProvider: null, emailProvider: "smtp");
        Assert.Equal(new[] { StudioAuthMethod.Email }, door);
        Assert.DoesNotContain(StudioAuthMethod.Phone, door);
    }

    [Fact]
    public void Production_WithTwilioOnly_OffersPhoneAndRefusesEmail()
    {
        var door = DoorIn(isDevelopment: false, smsProvider: "twilio", emailProvider: null);
        Assert.Equal(new[] { StudioAuthMethod.Phone }, door);
    }

    /// <summary>‏Development كَما كان: البابانِ مَفتوحان بِمُحاكٍ.</summary>
    [Fact]
    public void Development_OffersBoth_AsBefore()
        => Assert.Equal(
            new[] { StudioAuthMethod.Phone, StudioAuthMethod.Email },
            DoorIn(isDevelopment: true, null, null));

    /// <summary>‏`mock` مَكتوبَةً صَراحَةً خارِجَ التَطوير لا تَفتَح
    /// البابَ — نَفسُ قاعِدَةِ أَبوابِ المُستَأجِرين.</summary>
    [Fact]
    public void Production_WithExplicitMock_OffersNothing()
        => Assert.Empty(DoorIn(isDevelopment: false,
            AuthChannelSelection.MockValue, AuthChannelSelection.MockValue));

    // ─── الطَريقَةُ المُفَعَّلَة: لا زِرَّ يَقود إلى رَفض ────────────────

    /// <summary>طَلَبُ طَريقَةٍ غَيرِ مَعروضَةٍ لا يُصَيِّر نَموذَجَها —
    /// يَرتَدُّ إلى المَعروضَة. القاعِدَة ١٢: مَدخَلٌ يَرُدُّ «غَير
    /// مُتاح» لَيسَ مَدخَلاً.</summary>
    [Fact]
    public void RequestingAnUnofferedMethod_FallsBackToAnOfferedOne()
    {
        var door = DoorIn(isDevelopment: false, smsProvider: null, emailProvider: "smtp");
        Assert.Equal(StudioAuthMethod.Email, StudioAuthDoor.Active("phone", door));
    }

    [Fact]
    public void WithNoChannel_ThereIsNoActiveMethod_SoNoFormIsRendered()
        => Assert.Null(StudioAuthDoor.Active("email", DoorIn(false, null, null)));

    [Fact]
    public void RequestedMethod_WinsWhenOffered()
    {
        var door = DoorIn(isDevelopment: true, null, null);
        Assert.Equal(StudioAuthMethod.Email, StudioAuthDoor.Active("email", door));
        Assert.Equal(StudioAuthMethod.Phone, StudioAuthDoor.Active("phone", door));
    }

    /// <summary>قيمَةٌ مَجهولَةٌ لا تَفتَح باباً — تَرتَدُّ إلى المَعروضِ
    /// الأَوَّل. نَفسُ مَبدَإ «قيمَةٌ مَجهولَةٌ لا تَرتَدُّ إلى
    /// مُحاكٍ».</summary>
    [Theory]
    [InlineData("nafath")]
    [InlineData("")]
    [InlineData(null)]
    public void UnknownMethod_DoesNotParse(string? value)
        => Assert.Null(StudioAuthDoor.Parse(value));

    // ─── رَمزُ الرَفض يُقال، ولا يُترَك لِـ500 ──────────────────────────

    [Fact]
    public void UnavailableError_MatchesTheTenantDoorSuffix()
    {
        Assert.Equal("phone_unavailable", StudioAuthDoor.UnavailableError(StudioAuthMethod.Phone));
        Assert.Equal("email_unavailable", StudioAuthDoor.UnavailableError(StudioAuthMethod.Email));
    }

    // ─── آليَّةُ الرَمز: عَشوائيٌّ مُجَزَّأٌ بِمُهلَة ────────────────────
    //
    // المَخزَنُ ساكِنٌ في `AuthHandlers`، فَهذِه الاختِبارات تَمُرّ
    // بِالآليَّةِ نَفسِها الَّتي يَمُرّ بِها البابُ الحَيّ — بِلا قاعِدَةِ
    // بَيانات. والمَوضوعُ (`subject`) فَريدٌ لِكُلّ اختِبار كَي لا
    // تَتَداخَلَ الحالَةُ الساكِنَة.

    private static string Subject() => $"door-{Guid.NewGuid():N}";

    [Fact]
    public void IssuedCode_VerifiesOnce_ThenIsSpent()
    {
        var s = Subject();
        AuthHandlers.IssueAttempt(StudioAuth.Tenant, s, "424242", AuthHandlers.AuthKind.EmailOtp);
        Assert.True(AuthHandlers.ConsumeAttempt(
            StudioAuth.Tenant, s, "424242", AuthHandlers.AuthKind.EmailOtp));
        // مَرَّةً واحِدَة: إعادَةُ اللَعِب مَرفوضَة.
        Assert.False(AuthHandlers.ConsumeAttempt(
            StudioAuth.Tenant, s, "424242", AuthHandlers.AuthKind.EmailOtp));
    }

    [Fact]
    public void WrongCode_IsRejected()
    {
        var s = Subject();
        AuthHandlers.IssueAttempt(StudioAuth.Tenant, s, "424242", AuthHandlers.AuthKind.PhoneOtp);
        Assert.False(AuthHandlers.ConsumeAttempt(
            StudioAuth.Tenant, s, "424243", AuthHandlers.AuthKind.PhoneOtp));
    }

    [Fact]
    public void ExpiredCode_IsRejected()
    {
        var s = Subject();
        AuthHandlers.IssueAttempt(StudioAuth.Tenant, s, "424242",
            AuthHandlers.AuthKind.EmailOtp, lifetime: TimeSpan.FromSeconds(-1));
        Assert.False(AuthHandlers.ConsumeAttempt(
            StudioAuth.Tenant, s, "424242", AuthHandlers.AuthKind.EmailOtp));
    }

    /// <summary>رَمزُ البَريدِ لا يَفتَح بابَ الهاتِفِ ولا العَكس —
    /// المُحاوَلَةُ مُقَيَّدَةٌ بِنَوعِها.</summary>
    [Fact]
    public void CodeOfOneMethod_DoesNotOpenTheOther()
    {
        var s = Subject();
        AuthHandlers.IssueAttempt(StudioAuth.Tenant, s, "424242", AuthHandlers.AuthKind.EmailOtp);
        Assert.False(AuthHandlers.ConsumeAttempt(
            StudioAuth.Tenant, s, "424242", AuthHandlers.AuthKind.PhoneOtp));
    }

    /// <summary>ورَمزُ الاستوديو لا يَفتَح بابَ مُستَأجِر — المُحاوَلَةُ
    /// مُقَيَّدَةٌ بِالسلاج <c>_studio</c> أَيضاً.</summary>
    [Fact]
    public void StudioCode_DoesNotOpenATenantDoor()
    {
        var s = Subject();
        AuthHandlers.IssueAttempt(StudioAuth.Tenant, s, "424242", AuthHandlers.AuthKind.EmailOtp);
        Assert.False(AuthHandlers.ConsumeAttempt(
            "ejar", s, "424242", AuthHandlers.AuthKind.EmailOtp));
    }

    // ─── الاختِبارُ السالِبُ الصَريح: `123456` لا تَصِل الإنتاج ─────────

    /// <summary>
    /// <b>الثابِتُ لَم يَعُد مَوجوداً</b>. كانَ <c>StudioAuth.DevCode</c>
    /// حَقلاً عامّاً يُقارَنُ بِه الرَمزُ بِلا شَرطِ بيئَة، وكانَ
    /// <c>"123456"</c> مَكتوباً في <c>StudioAuth.razor</c> يُعرَض لِكُلّ
    /// زائِر. فَالفَحصُ هُنا <b>عَلى الغِياب نَفسِه</b>: لا حَقلَ ثابِتاً
    /// في <c>StudioAuth</c> قيمَتُه رَمزُ دُخول.
    /// </summary>
    [Fact]
    public void StudioAuth_HasNoConstantLoginCode()
    {
        var constants = typeof(StudioAuth)
            .GetFields(System.Reflection.BindingFlags.Public
                     | System.Reflection.BindingFlags.Static
                     | System.Reflection.BindingFlags.FlattenHierarchy)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string?)f.GetRawConstantValue())
            .ToList();

        // عَدّاد: أَداةٌ تَفحَص صِفراً أَداةٌ عَمياء (القاعِدَة ١٠).
        Assert.NotEmpty(constants);
        Assert.DoesNotContain("123456", constants);
        Assert.DoesNotContain(constants, c => c is { Length: 4 or 5 or 6 } && c.All(char.IsDigit));
    }

    /// <summary>
    /// <b>ومَصدَرُ الرَمزِ خارِجَ التَطوير عَشوائيّ</b>: القَناةُ الفِعليَّة
    /// (‏SMTP/Twilio) <c>DevHintCode</c> فيها <c>null</c>، وحينَئِذٍ
    /// <c>NewCode</c> يُوَلِّد. والمُحاكي وَحدَه يَحمِل تَلميحاً — وهو لا
    /// يُسَجَّل خارِجَ Development (‏<c>AuthChannelSelection</c> وحارِسُ
    /// الإقلاع، ومُثَبَّتٌ في <c>AuthChannelSelectionTests</c>).
    /// </summary>
    [Fact]
    public void WithoutADevHint_TheCodeIsRandom_NotAFixedConstant()
    {
        var drawn = Enumerable.Range(0, 200).Select(_ => AuthHandlers.NewCode(null)).ToList();
        Assert.True(drawn.Distinct().Count() > 1,
            "أَداة عَمياء أَو رَمزٌ ثابِت: ‏200 سَحبَةٍ أَعطَت قيمَةً واحِدَة.");
        // ولا يُدَّعى «لا يُساوي 123456 أَبَداً» — عَشوائيٌّ سُداسيٌّ
        // يُصيبُها مَرَّةً في ‏900 أَلف. المُدَّعى: **لَيسَ ثابِتاً**.
        Assert.True(drawn.Count(c => c == "123456") < 5,
            "الرَمزُ يَميل إلى الثابِتِ القَديم.");
        Assert.All(drawn, c => Assert.Equal(6, c.Length));
    }

    /// <summary>ومَعَ مُحاكٍ (‏Development وَحدَها) يَبقى التَلميحُ هُوَ
    /// الرَمز — <b>كَما كان</b>، فَلا تَنكَسِر الطَبَقَةُ الحَيَّة.</summary>
    [Fact]
    public void WithADevHint_TheCodeIsTheHint_AsBefore()
        => Assert.Equal("123456", AuthHandlers.NewCode("123456"));

    // ─── التَطبيعُ هُوَ الرَبط ───────────────────────────────────────────

    /// <summary>بَريدُ بابِ الاستوديو يُطَبَّع بِ<b>نَفسِ الدالَّةِ
    /// بِعَينِها</b> الَّتي يُطَبِّعُ بِها <see cref="PlatformAdminGrant"/>
    /// قَبلَ المَنح. انحِرافُ المَوضِعَينِ يَمنَح الصَلاحِيَّةَ لِعُنوانٍ
    /// لا يُطابِقُه الدُخول — ثَغرَةً صامِتَةً بِلا رِسالَةِ خَطَإ.
    /// والمُقارَنَةُ بِالدالَّةِ لا بِنَصٍّ مَنسوخ.</summary>
    [Theory]
    [InlineData("  Owner@Example.COM  ")]
    [InlineData("owner@example.com")]
    [InlineData("OWNER@EXAMPLE.COM")]
    public void StudioEmail_UsesTheSameNormalizer_AsThePlatformAdminGrant(string raw)
    {
        var atTheDoor = StudioAuth.NormalizeSubject(StudioAuthMethod.Email, raw);
        var atTheGrant = PlatformAdminGrant.Decide(
            phoneVar: null, emailVar: raw, isDevelopment: true, bootstrapVar: null).Email;
        Assert.Equal(atTheGrant, atTheDoor);
        Assert.Equal(EmailAddress.Normalize(raw), atTheDoor);
    }

    /// <summary>والهاتِفُ يُقَصّ ولا يُصَغَّر — نَفسُ ما يَفعَلُه المَنح.</summary>
    [Fact]
    public void StudioPhone_IsTrimmedOnly()
        => Assert.Equal("0500000000",
            StudioAuth.NormalizeSubject(StudioAuthMethod.Phone, "  0500000000 "));
}
