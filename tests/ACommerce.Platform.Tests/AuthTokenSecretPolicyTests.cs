using ACommerce.Kit.Auth;
using ACommerce.Kit.Auth.Server;
using Xunit;

namespace ACommerce.Platform.Tests;

// ═══ سِرُّ تَوقيعِ الجَلسَة — الجَدوَلُ الَّذي يَفصِلُ بَينَ كوكي يُوَقَّعُ بِسِرٍّ
//     وآخَرَ يُوَقَّعُ بِحَرفِيَّةٍ في مُستودَعٍ يُقرَأ ════════════════════════════
//
// **المَقيسُ قَبل (‏2026-10-11)**: مُثَبَّتٌ في `AuthTokenSecretCharacterizationTests`
// — `TokenSecret` يَرتَدُّ إلى حَرفِيَّةٍ عَلَنِيَّةٍ مِن ‏66 مِحرَفاً، وكوكي مُزَوَّرٌ
// بِها يَمُرُّ مِن `ParseToken`، ولا حارِسَ ولا وَثيقَةَ ولا فَحص.
//
// هذِه الاختِباراتُ هي **الحَدُّ المَقيس** (القاعِدَة ٢) لِلحارِسِ الَّذي يُنادى في
// `Program.cs` بِجِوارِ `AuthChannelSelection.AssertNoStubsOutsideDevelopment`.
// ولِكُلِّ رَمزِ خَرقٍ اختِبارٌ مُوجِبٌ وآخَرُ سالِب (القاعِدَة ٤)، ولا يُمَسُّ
// `AuthHandlers.TokenSecret` كِتابَةً (حالَةٌ عامَّةٌ وxunit يُوازي).

public class AuthTokenSecretPolicyTests
{
    private static string RepoRoot => ThemeZeroEquivalenceTests.RepoRoot;

    /// <summary>سِرٌّ مِن ‏48 مِحرَفاً — طولُه هُوَ المَقصود، لا قيمَتُه.</summary>
    private static readonly string Strong = new('s', 48);

    /// <summary>سِرٌّ قَصيرٌ مُمَيَّزُ النَصِّ كَي يُقاسَ أَنّ الرِسالَةَ لا تَطبَعُه.</summary>
    private const string ShortAndDistinctive = "p@ss-only-sixteen";

    private const string Fallback = AuthTokenSecretPolicy.DevelopmentFallback;

    // ─── الإنتاج: المُوجِبُ لِكُلِّ رَمز ─────────────────────────────────

    /// <summary>الحالَةُ الَّتي كَتَبَت البَند: المُتَغَيِّرُ غائِبٌ فَارتَدَّ
    /// `TokenSecret` إلى الحَرفِيَّة — خارِجَ التَطويرِ رَمزُ خَرقٍ لا قيمَةٌ صالِحَة.</summary>
    [Fact]
    public void Production_WithTheDevelopmentFallback_FiresDefault()
    {
        var v = Assert.Single(AuthTokenSecretPolicy.Validate(isDevelopment: false, Fallback));
        Assert.Equal(AuthTokenSecretPolicy.Default, v.Code);
        Assert.Contains(AuthTokenSecretPolicy.EnvVarName, v.MessageAr);
    }

    /// <summary>‏`ACOMMERCE_AUTH_SECRET=""` يَمُرُّ مِن `??` لِأَنَّه لَيسَ `null` —
    /// فَلَولا هذا الرَمزُ لَصارَ مِفتاحُ HMAC فارِغاً بِصَمت.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public void Production_WithAnEmptyOrWhitespaceSecret_FiresMissing(string? secret)
    {
        var v = Assert.Single(AuthTokenSecretPolicy.Validate(isDevelopment: false, secret));
        Assert.Equal(AuthTokenSecretPolicy.Missing, v.Code);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(8)]
    [InlineData(31)]
    public void Production_WithASecretShorterThanThirtyTwo_FiresShort(int length)
    {
        var v = Assert.Single(AuthTokenSecretPolicy.Validate(isDevelopment: false, new string('x', length)));
        Assert.Equal(AuthTokenSecretPolicy.Short, v.Code);
        // الرِسالَةُ تَقولُ الطولَ المَقيسَ والحَدَّ — لا «غَيرُ صالِح» وَحدَها.
        Assert.Contains(length.ToString(), v.MessageAr);
        Assert.Contains("32", v.MessageAr);
    }

    // ─── الإنتاج: السالِبُ لِكُلِّ رَمز ──────────────────────────────────

    /// <summary>الحَدُّ ‏32 **شامِل**: اثنانِ وثَلاثونَ تَمُرّ. والحَدُّ مَأخوذٌ مِن
    /// تَعليقِ `TokenSecret` القَديم («٣٢+») لا مُختَرَع.</summary>
    [Fact]
    public void Production_WithExactlyThirtyTwoChars_PassesTheBoundary()
    {
        Assert.Empty(AuthTokenSecretPolicy.Validate(isDevelopment: false, new string('x', 32)));
        Assert.True(AuthTokenSecretPolicy.IsValid(isDevelopment: false, new string('x', 32)));
    }

    [Fact]
    public void Production_WithAStrongSecret_IsValid()
    {
        Assert.Empty(AuthTokenSecretPolicy.Validate(isDevelopment: false, Strong));
        Assert.True(AuthTokenSecretPolicy.IsValid(isDevelopment: false, Strong));
        Assert.False(AuthTokenSecretPolicy.IsValid(isDevelopment: false, Fallback));
    }

    /// <summary>كُلُّ رَمزٍ **لا** يُطلَقُ لِمُدخَلٍ يَخُصُّ رَمزاً آخَر — المُدخَلُ
    /// يُعطي أَخَصَّ ما يَصِفُه، واحِداً لا قائِمَة.</summary>
    [Theory]
    [InlineData(Fallback, AuthTokenSecretPolicy.Short)]
    [InlineData(Fallback, AuthTokenSecretPolicy.Missing)]
    [InlineData(ShortAndDistinctive, AuthTokenSecretPolicy.Default)]
    [InlineData(ShortAndDistinctive, AuthTokenSecretPolicy.Missing)]
    [InlineData("", AuthTokenSecretPolicy.Default)]
    [InlineData("", AuthTokenSecretPolicy.Short)]
    public void Codes_DoNotFire_ForInputsThatBelongToAnotherCode(string secret, string codeThatMustNotFire)
    {
        var codes = AuthTokenSecretPolicy.Validate(isDevelopment: false, secret).Select(v => v.Code);
        Assert.DoesNotContain(codeThatMustNotFire, codes);
    }

    // ─── التَطوير: كَما كان ──────────────────────────────────────────────

    /// <summary>الطَبَقَةُ الحَيَّةُ و`dotnet run` بِلا مُتَغَيِّرٍ يَعتَمِدانِ عَلى
    /// الحَرفِيَّة. سُقوطُ هذا الاختِبارِ يَعني جَلسَةَ تَطويرٍ كامِلَةً بِلا دُخول.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(Fallback)]
    [InlineData(ShortAndDistinctive)]
    public void Development_IsSilent_ForEveryValue(string? secret)
    {
        Assert.Empty(AuthTokenSecretPolicy.Validate(isDevelopment: true, secret));
        Assert.True(AuthTokenSecretPolicy.IsValid(isDevelopment: true, secret));
        AuthTokenSecretPolicy.AssertSafeOutsideDevelopment(isDevelopment: true, secret);
    }

    // ─── المَعجَمُ مُغلَق ─────────────────────────────────────────────────

    [Fact]
    public void Lexicon_HasExactlyThreeCodes()
        => Assert.Equal(
            new[] { "auth_secret_missing", "auth_secret_default", "auth_secret_short" },
            AuthTokenSecretPolicy.All);

    /// <summary>كُلُّ حُكمٍ يَخرُجُ مِن `Validate` داخِلَ المَعجَم — جَدوَلٌ يَمُرُّ
    /// عَلى كُلِّ صَفٍّ، وعَدّادُه يُفحَص.</summary>
    [Fact]
    public void EveryVerdict_IsInsideTheLexicon()
    {
        var inputs = new string?[] { null, "", " ", Fallback, "x", new string('x', 31), new string('x', 32), Strong };
        var verdicts = inputs.SelectMany(i => AuthTokenSecretPolicy.Validate(isDevelopment: false, i)).ToList();
        Assert.Equal(6, verdicts.Count);
        Assert.All(verdicts, v => Assert.Contains(v.Code, AuthTokenSecretPolicy.All));
    }

    // ─── حارِسُ الإقلاع ───────────────────────────────────────────────────

    [Fact]
    public void BootGuard_Throws_OutsideDevelopment_NamingTheCodeAndTheVariable()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            AuthTokenSecretPolicy.AssertSafeOutsideDevelopment(isDevelopment: false, Fallback));
        Assert.Contains("auth_secret_default", ex.Message);
        // الرِسالَةُ تَقول **ماذا يُضبَط** وأَينَ، لا «فَشِل الإقلاع» وَحدَها.
        Assert.Contains("ACOMMERCE_AUTH_SECRET", ex.Message);
        Assert.Contains("DEPLOY.md", ex.Message);
    }

    /// <summary>الرِسالَةُ **لا تَطبَعُ السِرَّ أَبَداً** — سِجِلُّ الـSpace يُقرَأ.
    /// يُقاسُ عَلى الحَرفِيَّةِ (‏66 مِحرَفاً لا تَظهَرُ كامِلَةً) وعَلى سِرٍّ قَصيرٍ
    /// مُمَيَّز.</summary>
    [Fact]
    public void BootGuard_NeverPrintsTheSecretValue()
    {
        var onDefault = Assert.Throws<InvalidOperationException>(() =>
            AuthTokenSecretPolicy.AssertSafeOutsideDevelopment(isDevelopment: false, Fallback));
        Assert.DoesNotContain(Fallback, onDefault.Message);

        var onShort = Assert.Throws<InvalidOperationException>(() =>
            AuthTokenSecretPolicy.AssertSafeOutsideDevelopment(isDevelopment: false, ShortAndDistinctive));
        Assert.Contains("auth_secret_short", onShort.Message);
        Assert.DoesNotContain(ShortAndDistinctive, onShort.Message);
        Assert.All(AuthTokenSecretPolicy.Validate(isDevelopment: false, ShortAndDistinctive),
            v => Assert.DoesNotContain(ShortAndDistinctive, v.MessageAr));
    }

    [Fact]
    public void BootGuard_Silent_OutsideDevelopment_WithAStrongSecret()
        => AuthTokenSecretPolicy.AssertSafeOutsideDevelopment(isDevelopment: false, Strong);

    // ─── المَوضِعُ الواحِد: `AuthHandlers` يَقرَأُ الاسمَ والحَرفِيَّةَ مِن هُنا ──

    /// <summary>الاسمُ والحَدُّ مُثَبَّتانِ بِحَرفِهِما — يَكتُبُهُما المالِكُ في الـSpace
    /// ويَقرَؤُهُما `verify-production-boot.sh`؛ حَرفٌ فيهِما يَعني باباً مَفتوحاً
    /// بِلا سَبَبٍ ظاهِر.</summary>
    [Fact]
    public void VariableName_AndMinimum_ArePinned()
    {
        Assert.Equal("ACOMMERCE_AUTH_SECRET", AuthTokenSecretPolicy.EnvVarName);
        Assert.Equal(32, AuthTokenSecretPolicy.MinimumLength);
    }

    /// <summary>`AuthHandlers.TokenSecret` هُوَ **نَفسُ** الصيغَةِ بِاسمِ المُتَغَيِّرِ
    /// وحَرفِيَّةِ هذِه السِياسَة — لا نُسخَةٌ ثانِيَةٌ تَنجَرِف (القاعِدَة ٨).
    /// وهُوَ عَينُ ما ثَبَّتَه التَوصيفُ بِحَرفِيَّةٍ ذَهَبِيَّةٍ مُستَقِلَّة.</summary>
    [Fact]
    public void AuthHandlers_ResolvesTheSecret_FromThisPolicysNameAndFallback()
        => Assert.Equal(
            Environment.GetEnvironmentVariable(AuthTokenSecretPolicy.EnvVarName)
                ?? AuthTokenSecretPolicy.DevelopmentFallback,
            AuthHandlers.TokenSecret);

    /// <summary>الحَرفِيَّةُ في مِلَفٍّ واحِدٍ مِن `libs/` — هذِه السِياسَة. لَو عادَت
    /// نُسخَةٌ ثانِيَةٌ في `AuthHandlers.cs` أَو سِواه لَصارَ لِلسِرِّ تَعريفانِ،
    /// وهُوَ عَينُ ما يَمنَعُه القاعِدَة ٨. والعَدّادُ يُفحَص: أَداةٌ قَرَأَت صِفرَ
    /// مِلَفٍّ لا تُميَّزُ عَن أَداةٍ وَجَدَت صِفرَ نُسخَة.</summary>
    [Fact]
    public void TheFallbackLiteral_LivesInThePolicyFileAlone()
    {
        var files = Directory.EnumerateFiles(Path.Combine(RepoRoot, "libs"), "*.cs", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                     && !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .ToList();
        Assert.True(files.Count > 100, $"أَداةٌ عَمياء: {files.Count} مِلَفّاً فَقَط تَحتَ libs/.");

        var carriers = files.Where(p => File.ReadAllText(p).Contains(Fallback, StringComparison.Ordinal))
            .Select(p => Path.GetFileName(p)).ToList();

        Assert.Equal(new[] { "AuthTokenSecretPolicy.cs" }, carriers);
    }

    // ─── التَركيب: السَطرُ في `Program.cs` مَوجودٌ وفي مَوضِعِه ───────────

    /// <summary>الحارِسُ يُنادى في `Program.cs` **بَعدَ** حارِسِ القَنَواتِ المُحاكيَةِ
    /// و**قَبلَ** حارِسِ الدَفع، وعَلى `AuthHandlers.TokenSecret` (القيمَةُ الَّتي
    /// تُوَقِّعُ فِعلاً) لا عَلى المُتَغَيِّرِ وَحدَه. سَطرُ التَركيبِ لا يُختَبَرُ
    /// بِإقلاع؛ فَيُثَبَّتُ لَفظِيّاً — نَفسُ ما تَفعَلُه `AdminSelfGrantTests`.</summary>
    [Fact]
    public void ProgramCs_CallsTheGuard_BetweenTheChannelGuardAndThePaymentGuard_OnTheEffectiveSecret()
    {
        var program = File.ReadAllText(Path.Combine(RepoRoot, "apps", "V1.App", "Program.cs"));
        Assert.True(program.Length > 1000, "أَداةٌ عَمياء: Program.cs لَم يُقرَأ.");

        var channels = program.IndexOf("AuthChannelSelection.AssertNoStubsOutsideDevelopment(", StringComparison.Ordinal);
        var secret   = program.IndexOf("AuthTokenSecretPolicy.AssertSafeOutsideDevelopment(", StringComparison.Ordinal);
        var payments = program.IndexOf("PaymentProviderSelection.AssertNoStubsOutsideDevelopment(", StringComparison.Ordinal);

        Assert.True(channels > 0, "حارِسُ القَنَواتِ غَيرُ مَوجود — المَرجِعُ نَفسُه زال.");
        Assert.True(secret > channels, "حارِسُ السِرِّ غَيرُ مَوجودٍ أَو قَبلَ حارِسِ القَنَوات.");
        Assert.True(payments > secret, "حارِسُ السِرِّ بَعدَ حارِسِ الدَفع — خَرَجَ مِن مَوضِعِه.");

        var call = program.Substring(secret, program.IndexOf(';', secret) - secret);
        Assert.Contains("app.Environment.IsDevelopment()", call);
        Assert.Contains("AuthHandlers.TokenSecret", call);
    }
}
