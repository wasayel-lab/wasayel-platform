using ACommerce.Kit.Auth;
using ACommerce.Templates.Customer.Marketplace.Services.Incubator;
using Xunit;

namespace ACommerce.Platform.Tests;

// ═══ قَرارُ مَنحِ مُشرِفِ المَنصَّة — مَن يُمنَح، وبِأَيّ تَطبيع ══════════
//
// **المَقيسُ قَبل (‏2026-08-23)**: ‏`PlatformAdminSeeder` يَمنَح
// **بِالهاتِفِ حَصراً** (`PLATFORM_ADMIN_PHONE` ← `StudioUser.Phone`).
// وبَعدَ `cd43b366` تُغلَق قَناةُ الرَسائِلِ في الإنتاجِ بِلا
// `Auth__Sms__Provider`، والمالِكُ يَضبُط **SMTP وَحدَه**. فَالنَتيجَةُ
// المُرَكَّبَة: دُخولُ المالِكِ بِالبَريدِ يُنتِج مُستَخدِماً آخَرَ بِلا
// صَلاحِيَّة — يُحبَس خارِجَ إدارَتِه بَينَما البابُ الَّذي يَملِكُه مُغلَق.
//
// وهذِه الاختِبارات هي **الحَدُّ المَقيس** (القاعِدَة ٢): سَطرُ بَذرٍ في
// مِلَفِّ الإقلاعِ لا يُختَبَر، أَمّا الجَدوَلُ فَيُختَبَر.

public class PlatformAdminGrantTests
{
    private const string Boot = PlatformAdminGrant.BootstrapValue;

    // ─── ١) مَنحٌ بِالبَريد ───────────────────────────────────────────

    [Fact]
    public void Email_Alone_Grants()
    {
        var r = PlatformAdminGrant.Decide(
            phoneVar: null, emailVar: "owner@example.com",
            isDevelopment: true, bootstrapVar: null);
        Assert.Equal("owner@example.com", r.Email);
        Assert.Null(r.Phone);
        Assert.False(r.IsEmpty);
        Assert.False(r.EmailRejected);
    }

    // ─── ٢) الهاتِفُ كَما كان — لا انحِدار ────────────────────────────

    [Fact]
    public void Phone_Alone_Grants_Unchanged()
    {
        var r = PlatformAdminGrant.Decide(
            phoneVar: "0555000111", emailVar: null,
            isDevelopment: true, bootstrapVar: null);
        Assert.Equal("0555000111", r.Phone);
        Assert.Null(r.Email);
    }

    /// <summary>الهاتِفُ يُشَذَّب كَما كانَ يُشَذَّب في البَذّارَة.</summary>
    [Fact]
    public void Phone_IsTrimmed()
        => Assert.Equal("0555000111", PlatformAdminGrant.Decide(
            "  0555000111  ", null, isDevelopment: true, bootstrapVar: null).Phone);

    /// <summary>هاتِفٌ فارِغٌ أَو مَسافاتٌ = كَالغِياب، لا مُستَخدِمَ بِمُعَرِّفٍ فارِغ.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Phone_Blank_IsAbsent(string value)
        => Assert.True(PlatformAdminGrant.Decide(
            value, null, isDevelopment: true, bootstrapVar: null).IsEmpty);

    // ─── ٣) بِكِلَيهِما — مُعَرِّفان مُستَقِلّان ───────────────────────

    [Fact]
    public void Both_GrantBoth()
    {
        var r = PlatformAdminGrant.Decide(
            "0555000111", "Owner@Example.com",
            isDevelopment: true, bootstrapVar: null);
        Assert.Equal("0555000111", r.Phone);
        Assert.Equal("owner@example.com", r.Email);
    }

    // ─── ٤) الغِياب = لا عَمَل ────────────────────────────────────────

    [Fact]
    public void Neither_IsEmpty()
    {
        var r = PlatformAdminGrant.Decide(null, null, isDevelopment: true, bootstrapVar: null);
        Assert.True(r.IsEmpty);
        Assert.False(r.EmailRejected);
        Assert.Equal(PlatformAdminGrantRequest.None, r);
    }

    /// <summary>حَتّى في الإنتاجِ مَعَ الإقرار: بِلا مُعَرِّفٍ لا شَيء.
    /// البَوّابَةُ الثانِيَةُ وَحدَها لا تَمنَح أَحَداً.</summary>
    [Fact]
    public void Neither_WithBootstrap_StillEmpty()
        => Assert.True(PlatformAdminGrant.Decide(
            null, null, isDevelopment: false, bootstrapVar: Boot).IsEmpty);

    // ─── ٥) تَطبيعُ الحالَة — وهُوَ الرَبطُ نَفسُه ────────────────────

    [Theory]
    [InlineData("Owner@Example.com")]
    [InlineData("OWNER@EXAMPLE.COM")]
    [InlineData("  owner@Example.COM  ")]
    public void Email_IsNormalized_LowerAndTrimmed(string raw)
        => Assert.Equal("owner@example.com", PlatformAdminGrant.Decide(
            null, raw, isDevelopment: true, bootstrapVar: null).Email);

    /// <summary><b>الحَدُّ الَّذي يَمنَع الثَغرَةَ الصامِتَة</b>: البَذّارَةُ
    /// تُطَبِّع بِـ<b>نَفسِ الدالَّة</b> الَّتي يُطَبِّعُ بِها مَسارُ
    /// الدُخول (‏<c>auth/email/login</c>، <c>auth/email/verify</c>،
    /// <c>AuthHandlers.RequestEmailOtpHandler</c>،
    /// <c>VerifyEmailOtpHandler</c>) — لا بِنَظيرٍ مَكتوبٍ مَرَّتَين. لَو
    /// انحَرَفَ أَحَدُ المَوضِعَينِ لَمُنِحَت الصَلاحِيَّةُ لِعُنوانٍ لا
    /// يُطابِقُه الدُخول، ولَما قالَ ذلكَ أَيُّ خَطَإٍ ولا أَيُّ لوغ.</summary>
    [Theory]
    [InlineData("Owner@Example.com")]
    [InlineData("  MALIK@Wasayel.SA ")]
    [InlineData("a.b+tag@sub.domain.example")]
    public void Email_UsesTheSameNormalizer_AsTheLoginPath(string raw)
        => Assert.Equal(
            EmailAddress.Normalize(raw),
            PlatformAdminGrant.Decide(null, raw, isDevelopment: true, bootstrapVar: null).Email);

    // ─── البَوّابَةُ الثانِيَة — واحِدَةٌ لِلمُعَرِّفَين ──────────────

    /// <summary>البَريدُ لا يَفتَح باباً أَوسَعَ مِمّا يَفتَحُه الهاتِف:
    /// خارِجَ التَطويرِ بِلا إقرارٍ لا يُمنَح أَيٌّ مِنهُما.</summary>
    [Theory]
    [InlineData("0555000111", null)]
    [InlineData(null, "owner@example.com")]
    [InlineData("0555000111", "owner@example.com")]
    public void Production_WithoutBootstrap_GrantsNothing(string? phone, string? email)
        => Assert.True(PlatformAdminGrant.Decide(
            phone, email, isDevelopment: false, bootstrapVar: null).IsEmpty);

    [Theory]
    [InlineData("0")]
    [InlineData("true")]
    [InlineData("yes")]
    [InlineData("")]
    public void Production_WithWrongBootstrapValue_GrantsNothing(string value)
        => Assert.True(PlatformAdminGrant.Decide(
            "0555000111", "owner@example.com", isDevelopment: false, bootstrapVar: value).IsEmpty);

    [Fact]
    public void Production_WithBootstrap_GrantsBoth()
    {
        var r = PlatformAdminGrant.Decide(
            "0555000111", "owner@example.com", isDevelopment: false, bootstrapVar: Boot);
        Assert.Equal("0555000111", r.Phone);
        Assert.Equal("owner@example.com", r.Email);
    }

    // ─── صيغَةٌ غَير صالِحَة: تُغلِق، ولا تَرتَدّ صامِتَة ─────────────

    /// <summary>خَطَأُ حَرفٍ في العُنوانِ لا يُنشِئ مُستَخدِماً بِعُنوانٍ
    /// مُشَوَّهٍ لا يَبلُغُه بَريد — يُغلِق ويُقال.</summary>
    [Theory]
    [InlineData("owner")]
    [InlineData("owner@")]
    [InlineData("@example.com")]
    [InlineData("owner@example")]
    [InlineData("own er@example.com")]
    [InlineData("owner@@example.com")]
    public void Email_Invalid_IsRejected_NotGranted(string raw)
    {
        var r = PlatformAdminGrant.Decide(null, raw, isDevelopment: true, bootstrapVar: null);
        Assert.Null(r.Email);
        Assert.True(r.EmailRejected);
        Assert.True(r.IsEmpty);
    }

    /// <summary>وبَريدٌ مُشَوَّهٌ لا يُسقِط مَنحَ الهاتِفِ مَعَه — القَراران
    /// مُستَقِلّان، تَماماً كَاستِقلالِ قَراراتِ القَنَوات في
    /// <c>AuthChannelSelection</c>.</summary>
    [Fact]
    public void Email_Invalid_DoesNotCancel_PhoneGrant()
    {
        var r = PlatformAdminGrant.Decide(
            "0555000111", "owner@", isDevelopment: true, bootstrapVar: null);
        Assert.Equal("0555000111", r.Phone);
        Assert.Null(r.Email);
        Assert.True(r.EmailRejected);
        Assert.False(r.IsEmpty);
    }

    /// <summary>الرَفضُ نَفسُه لا يَتَسَرَّب عَبرَ البَوّابَةِ الثانِيَة:
    /// في الإنتاجِ بِلا إقرارٍ لا شَيءَ يُقال، لِأَنّ لا شَيءَ كانَ
    /// سَيُمنَح.</summary>
    [Fact]
    public void Email_Invalid_Production_WithoutBootstrap_IsSilent()
    {
        var r = PlatformAdminGrant.Decide(
            null, "owner@", isDevelopment: false, bootstrapVar: null);
        Assert.True(r.IsEmpty);
        Assert.False(r.EmailRejected);
    }

    // ─── أَسماءُ المُتَغَيِّرات — تُقرَأ مِن الكودِ لا مِن وَثيقَة ─────

    /// <summary>‏`docs/DEPLOY.md` §٢·ب يَذكُرُها بِحَرفِها؛ فَتَغييرُ اسمٍ
    /// هُنا بِلا تَحديثِ الوَثيقَةِ يَترُك المالِكَ يَضبُط مُتَغَيِّراً
    /// لا يَقرَؤُه أَحَد.</summary>
    [Fact]
    public void VariableNames_ArePinned()
    {
        Assert.Equal("PLATFORM_ADMIN_PHONE",     PlatformAdminGrant.PhoneVar);
        Assert.Equal("PLATFORM_ADMIN_EMAIL",     PlatformAdminGrant.EmailVar);
        Assert.Equal("PLATFORM_ADMIN_BOOTSTRAP", PlatformAdminGrant.BootstrapVar);
        Assert.Equal("1",                        PlatformAdminGrant.BootstrapValue);
    }
}
