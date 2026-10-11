namespace ACommerce.Kit.Auth;

// ═══ سِرُّ تَوقيعِ الجَلسَة — قَرارٌ نَقِيّ، وفَشَلٌ مُغلَقٌ عِندَ الإقلاع ═══════
//
// **العِلَّة المَقيسَة (‏2026-10-11، موجَزُ الفَجَوات P0-2)**: ‏`AuthHandlers.TokenSecret`
// كانَ `Environment.GetEnvironmentVariable("ACOMMERCE_AUTH_SECRET") ?? "<حَرفِيَّةٌ
// في الكود>"` — بِلا حارِسِ إقلاع، ولا صَفٍّ في `DEPLOY.md`، ولا فَحصٍ في
// `verify-production-boot.sh`. والتَوقيعُ HMAC-SHA256 فَوقَ `{userId}|{tenantSlug}|{exp}`
// بِهذا السِرّ، وكوكي الاستوديو (مُشرِفُ المَنَصَّة) يُصنَعُ بِالدالَّةِ نَفسِها.
// فَلَو غابَ المُتَغَيِّرُ في الـSpace لَكانَ كُلُّ كوكي — وجَلسَةُ المُشرِفِ ذاتُها —
// قابِلاً لِلتَزوير بِمَعرِفَةِ `userId` و`slug` وحَرفِيَّةٍ في مُستودَعٍ يُقرَأ.
// **وحالُ المُتَغَيِّرِ في الـSpace غَيرُ مَقيسٍ مِن هُنا؛ المَقيسُ غِيابُ الحارِس.**
//
// **ولِماذا القَرارُ هُنا لا في `Program.cs`**: نَفسُ حُجَّةِ `AuthChannelSelection`
// حَرفاً — سَطرٌ في مِلَفِّ الإقلاعِ لا يُختَبَر، والجَدوَلُ يُختَبَر (القاعِدَة ٢).
// والحِراسَةُ في التَركيبِ لا في جِسمِ `Sign` (القاعِدَة ٦): يُرمى مَرَّةً قَبلَ
// أَوَّلِ طَلَب، لا يُفحَصُ عِندَ كُلِّ تَوقيع. **ولا أُنبوبَ رابِع** (القاعِدَة ٨):
// نَفسُ شَكلِ `StubViolations`/`AssertNoStubsOutsideDevelopment` القائِمِ لِلقَنَواتِ
// والدَفعِ والمَلَفّات.
//
// **ولِماذا المُقارَنَةُ بِالقيمَةِ لا بِالطولِ وَحدَه**: الحَرفِيَّةُ ‏66 مِحرَفاً،
// فَفَحصُ «٣٢+» الَّذي كانَ تَعليقُها يَطلُبُه كانَ سَيُمَرِّرُها — مَقيسٌ في
// `AuthTokenSecretCharacterizationTests` (وكانَ قَد قُدِّرَ ‏68 بِالعَين فَأَحمَرَ
// الاختِبار؛ القاعِدَة ١٠).

/// <summary>خَرقٌ واحِدٌ في سِرِّ التَوقيع. نَفسُ شَكلِ
/// <see cref="AccountDeletionViolation"/> حَرفاً (القاعِدَة ٤). **ولا تَحمِلُ
/// <c>MessageAr</c> قيمَةَ السِرِّ أَبَداً** — الطولَ عِندَ الحاجَة، لا النَصّ.</summary>
public sealed record AuthTokenSecretViolation(string Code, string MessageAr);

public static class AuthTokenSecretPolicy
{
    /// <summary>اسمُ المُتَغَيِّرِ بِحَرفِه — يَكتُبُه المالِكُ في الـSpace،
    /// ويَقرَؤُه <c>AuthHandlers.TokenSecret</c>، ويَفحَصُه
    /// <c>verify-production-boot.sh</c>. مَوضِعٌ واحِدٌ لِلاسم.</summary>
    public const string EnvVarName = "ACOMMERCE_AUTH_SECRET";

    /// <summary>سِرُّ التَطويرِ الافتِراضيّ — **عَلَنِيٌّ بِطَبيعَتِه** لِأَنَّه في
    /// الكود. مَسموحٌ في Development وَحدَها؛ وخارِجَها رَمزُ خَرقٍ لا قيمَةٌ
    /// صالِحَة. النَصُّ هُوَ ما كانَ في <c>AuthHandlers.cs</c> **بِلا تَغييرِ حَرف**
    /// — فَكُلُّ جَلسَةِ تَطويرٍ قائِمَةٍ تَبقى صالِحَة.</summary>
    public const string DevelopmentFallback =
        "dev-only-secret-please-set-ACOMMERCE_AUTH_SECRET-in-production-32+";

    /// <summary>أَدنى طولٍ خارِجَ التَطوير — الرَقَمُ الَّذي كانَ تَعليقُ
    /// <c>TokenSecret</c> يَطلُبُه («٣٢+ حَرفاً عَشوائِيّاً») ولا يَفرِضُه.
    /// ‏HMAC-SHA256 يَقبَلُ أَيَّ طول؛ والحَدُّ هُنا يَمنَعُ كَلِمَةَ مُرورٍ قَصيرَةً
    /// تُخَمَّن. رَقَمٌ مِن الكودِ القائِم لا مُختَرَع (القاعِدَة ١٦).</summary>
    public const int MinimumLength = 32;

    // ─── مَعجَمُ الخَرقِ المُغلَق — ثَلاثَةٌ لا رابِعَ لَها ─────────────────

    /// <summary>فارِغٌ أَو بَياضٌ خارِجَ التَطوير. <c>ACOMMERCE_AUTH_SECRET=""</c>
    /// يَمُرُّ مِن <c>??</c> لِأَنَّه لَيسَ <c>null</c>، فَيَصيرُ مِفتاحُ HMAC
    /// فارِغاً — حالَةٌ لا يَراها فَحصُ الحَرفِيَّةِ ولا فَحصُ الطولِ بِاسمِها.</summary>
    public const string Missing = "auth_secret_missing";

    /// <summary>يُساوي <see cref="DevelopmentFallback"/> خارِجَ التَطوير — وهُوَ ما
    /// يَقَعُ بِمُجَرَّدِ **غِيابِ** المُتَغَيِّرِ في الـSpace.</summary>
    public const string Default = "auth_secret_default";

    /// <summary>أَقصَرُ مِن <see cref="MinimumLength"/> خارِجَ التَطوير.</summary>
    public const string Short = "auth_secret_short";

    /// <summary>المَعجَمُ المُغلَق — ثَلاثَةٌ لا رابِعَ لَها. يَقرَؤُه
    /// <c>verify-production-boot.sh</c> بِحَرفِه (‏<c>auth_secret_(missing|default|short)</c>)
    /// لِيُمَيِّزَ تَهيئَةً ناقِصَةً عَن كَسرٍ في الكود.</summary>
    public static readonly IReadOnlyList<string> All = new[] { Missing, Default, Short };

    /// <summary>
    /// القَرار. **جَدوَلٌ لا شَرطٌ مَنثور**، وكُلُّ مُدخَلٍ يُعطي رَمزاً واحِداً
    /// هُوَ أَخَصُّ ما يَصِفُه:
    /// <list type="bullet">
    /// <item>في <c>Development</c> ← لا خَرق، أَيّاً كانَت القيمَة. الطَبَقَةُ
    /// الحَيَّةُ و<c>dotnet run</c> بِلا مُتَغَيِّرٍ يَعتَمِدانِ عَلَيه ولا
    /// يَتَغَيَّرُ شَيءٌ عَمّا كان.</item>
    /// <item>وإلّا فارِغٌ أَو بَياض ← <see cref="Missing"/>.</item>
    /// <item>وإلّا يُساوي الحَرفِيَّةَ ← <see cref="Default"/>.</item>
    /// <item>وإلّا أَقصَرُ مِن ‏32 ← <see cref="Short"/>.</item>
    /// <item>وإلّا ← لا خَرق.</item>
    /// </list>
    /// <para><b>و«خارِجَ التَطوير» لا «في الإنتاج» فَقَط</b>: نَفسُ حَرفِ
    /// <see cref="AuthChannelSelection.StubViolations"/> — بيئَةٌ ثالِثَة
    /// (‏Staging) هي الشَقُّ الَّذي يُنسى، والشَرطُ الأَوسَعُ يُغلِقُه.</para>
    /// </summary>
    public static IReadOnlyList<AuthTokenSecretViolation> Validate(bool isDevelopment, string? secret)
    {
        if (isDevelopment) return Array.Empty<AuthTokenSecretViolation>();

        if (string.IsNullOrWhiteSpace(secret))
            return new[]
            {
                new AuthTokenSecretViolation(Missing,
                    $"سِرُّ تَوقيعِ الجَلسَةِ فارِغ — اضبِط {EnvVarName}.")
            };

        if (string.Equals(secret, DevelopmentFallback, StringComparison.Ordinal))
            return new[]
            {
                new AuthTokenSecretViolation(Default,
                    $"سِرُّ تَوقيعِ الجَلسَةِ هُوَ سِرُّ التَطويرِ العَلَنِيُّ المَكتوبُ في الكود — اضبِط {EnvVarName}.")
            };

        if (secret.Length < MinimumLength)
            return new[]
            {
                new AuthTokenSecretViolation(Short,
                    $"سِرُّ تَوقيعِ الجَلسَةِ أَقصَرُ مِن {MinimumLength} مِحرَفاً (طولُه {secret.Length}) — اضبِط {EnvVarName} بِقيمَةٍ أَطوَل.")
            };

        return Array.Empty<AuthTokenSecretViolation>();
    }

    public static bool IsValid(bool isDevelopment, string? secret)
        => Validate(isDevelopment, secret).Count == 0;

    /// <summary>يَرمي إن كانَ السِرُّ خارِجَ التَطويرِ فارِغاً أَو عَلَنِيّاً أَو
    /// قَصيراً. يُستَدعى **بَعدَ** بِناءِ المُضيفِ وقَبلَ أَوَّلِ طَلَب، بِجِوارِ
    /// <see cref="AuthChannelSelection.AssertNoStubsOutsideDevelopment"/>، **عَلى
    /// القيمَةِ الفِعلِيَّةِ الَّتي تُوَقِّع** لا عَلى المُتَغَيِّرِ وَحدَه.
    /// **ولا يَطبَعُ القيمَةَ أَبَداً** — الرِسالَةُ تَحمِلُ الرَمزَ والاسمَ
    /// والعِلاجَ وما يَقَعُ عِندَ التَبديل.</summary>
    public static void AssertSafeOutsideDevelopment(bool isDevelopment, string? secret)
    {
        var violations = Validate(isDevelopment, secret);
        if (violations.Count == 0) return;
        throw new InvalidOperationException(
            "سِرُّ تَوقيعِ جَلسَةِ الدُخولِ غَيرُ صالِحٍ خارِجَ بيئَة التَطوير ["
            + string.Join("، ", violations.Select(v => v.Code))
            + "]: " + string.Join(" ", violations.Select(v => v.MessageAr))
            + " بِدونِه كُلُّ كوكي — وجَلسَةُ مُشرِفِ المَنَصَّةِ ذاتُها — قابِلٌ لِلتَزوير. "
            + $"اضبِط {EnvVarName} في الـSpace بِـ{MinimumLength}+ مِحرَفاً عَشوائِيّاً "
            + "(‏docs/DEPLOY.md §٢·ب)، واعلَم أَنّ تَبديلَه يُخرِجُ كُلَّ الجَلساتِ القائِمَة.");
    }
}
