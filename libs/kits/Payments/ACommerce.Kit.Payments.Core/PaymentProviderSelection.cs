namespace ACommerce.Kit.Payments;

// ═══ اختِيار مُزَوِّد الدَفع — قَرارٌ بِالبيئَة، وفَشَلٌ مُغلَق ════════
//
// **العِلَّة المَقيسَة (‏2026-08-30)**: ‏`Program.cs` كانَ يُسَجِّل
// `AddMockPayments()` **بِلا شَرطِ بيئَة**، و`MockPaymentProvider` يُرجِع
// `IsActive = true` و`PaymentStatus.Authorized` **دائِماً** ما لَم يَكُنِ
// المَبلَغُ ≤ 0. فَكانَ الجَوابُ في الإنتاج «نَجَحَ الدَفع» لِكُلّ نِداء:
// ‏`/studio/billing/select` تَقرَؤُه فَتَكتُب `u.Tier = "scale"`،
// و`/{slug}/checkout/submit` تَقرَؤُه فَتُعَلِّق على الصَفقَةِ مَرجِعَ
// دَفعٍ لَم يَقَع.
//
// **وهذا العَطَبُ بِعَينِه سَبَقَ في قَنَواتِ الدُخول** (‏2026-08-23):
// سَطرُ `AddMockSmsChannel()` مَنسِيٌّ بِلا شَرطٍ جَعَلَ رَمزَ الدُخولِ
// `123456` ثابِتاً في الإنتاج. فَالعِلاجُ هُوَ العِلاجُ نَفسُه حَرفاً،
// ولا أُنبوبَ رابِع (القاعِدَة ٨): عَلامَةٌ على المُحاكي، ودالَّةُ قَرارٍ
// نَقِيَّةٌ تُقاس بِجَدوَل، وحارِسُ إقلاعٍ يَرمي **قَبلَ أَوَّلِ طَلَب**.
//
// **ولِماذا القَرارُ هُنا لا في `Program.cs`**: سَطرُ تَسجيلٍ في مِلَفِّ
// الإقلاعِ لا يُختَبَر — والحَدُّ الَّذي لا يُقاس آلِيّاً يَنهار
// (القاعِدَة ٢).

/// <summary>
/// عَلامَةٌ يَحمِلُها مُزَوِّدُ الدَفعِ التَطويريّ. **نَظيرَةُ
/// <c>IDevelopmentStubChannel</c> في عُدَّةِ الدُخول، ولَيسَت تَجريداً
/// سابِقاً لِمُستَهلِكِه**: يُنَفِّذُها <see cref="MockPaymentProvider"/>،
/// ويَقرَؤُها حارِسُ الإقلاع
/// (<see cref="PaymentProviderSelection.StubViolations"/>).
///
/// <para><b>ولِماذا عَلامَةٌ لا فَحصُ الاسم</b>: ‏<c>ProviderName ==
/// "mock"</c> نَصٌّ يُبَدَّل بِإعادَةِ تَسمِيَةٍ صامِتَة، والعَلامَةُ
/// جُزءٌ مِن نَوعِ الصِنف — تَسقُط بِحَذفٍ مَرئيٍّ في مُراجَعَةٍ لا
/// بِتَحريرِ سِلسِلَة.</para>
/// </summary>
public interface IDevelopmentStubPaymentProvider { }

/// <summary>المُزَوِّدُ المُختار.</summary>
public enum PaymentProviderChoice
{
    /// <summary>
    /// <para>لا مُزَوِّدَ فِعليّ — يُسَجَّل
    /// <see cref="UnavailablePaymentProvider"/>، فَيُرَدُّ كُلُّ نِداءٍ
    /// بِفَشَلٍ صَريحٍ ذي سَبَب. <b>هذا هُوَ الفَشَلُ المُغلَق.</b></para>
    ///
    /// <para><b>ولِماذا يُسَجَّلُ بَديلٌ ولا يُترَكُ الوِعاءُ فارِغاً</b>:
    /// ‏<c>IPaymentProvider</c> مَطلوبٌ في باني <c>DealsService</c>
    /// (‏<c>AddScoped</c>) وفي جِسمِ <c>POST /{slug}/checkout/submit</c>.
    /// فَغِيابُ التَسجيلِ يَعني انفِجارَ حَلٍّ عِندَ أَوَّلِ طَلَبِ صَفقَة
    /// — <b>عُطلٌ عامٌّ بَدَلَ رَفضٍ مَقروء</b>. نَفسُ حُجَّةِ
    /// <c>PayPalGateway</c> و<c>PaddleGateway</c>: غِلافٌ مُسَجَّلٌ دائِماً
    /// يَقول «لا» بِلا انفِجار.</para>
    /// </summary>
    Unavailable,

    /// <summary>مُحاكٍ تَطويريّ (يَنجَح دائِماً). مَسموحٌ في
    /// <c>Development</c> وَحدَها.</summary>
    Mock
}

/// <summary>مُزَوِّدُ دَفعٍ مُسَجَّلٌ فِعلاً في وِعاءِ الخِدَمات — كَما
/// يَراهُ حارِسُ الإقلاع.</summary>
/// <param name="ProviderName">اسمُ المُزَوِّد (لِلتَشخيصِ والرِسالَة).</param>
/// <param name="IsDevelopmentStub">أَيَحمِلُ عَلامَةَ المُحاكاة؟</param>
public sealed record RegisteredPaymentProvider(string ProviderName, bool IsDevelopmentStub);

public static class PaymentProviderSelection
{
    /// <summary>
    /// <para><b>القَرار — دالَّةٌ نَقِيَّةٌ بِمُدخَلٍ واحِد.</b></para>
    ///
    /// <para><b>ولا مِفتاحَ تَهيئَةٍ هُنا، ويُقالُ لِماذا</b> (القاعِدَة ١
    /// و١٦): ‏<c>IPaymentProvider</c> لَيسَ لَه اليَومَ تَنفيذٌ فِعليٌّ
    /// **يَبلُغُه التَطبيق** — ‏<c>Moyasar</c> و<c>Noon</c> مَشروعانِ لا
    /// يُحيلُ إلَيهِما <c>V1.App.csproj</c>، و<c>PayPal</c> و<c>Paddle</c>
    /// مُسَجَّلانِ على أَنفُسِهِما لا على هذِه الواجِهَة (‏ADR-009).
    /// فَمِفتاحٌ يَقبَل قيمَةً لا تُسَجِّل شَيئاً هُوَ <b>شَرطٌ لا
    /// يَكذِبُ أَبَداً</b> — وذلك أَسوَأُ مِن غِيابِه. يُضافُ المِفتاحُ
    /// يَومَ يوجَد لَه مُزَوِّدٌ يَختارُه.</para>
    /// </summary>
    public static PaymentProviderChoice Decide(bool isDevelopment)
        => isDevelopment ? PaymentProviderChoice.Mock : PaymentProviderChoice.Unavailable;

    // ─── حارِسُ الإقلاع ───────────────────────────────────────────────

    /// <summary>
    /// مُزَوِّدُ الدَفعِ المُحاكي مُسَجَّلاً خارِجَ التَطوير — واحِدٌ
    /// يَكفي لِيَكونَ «نَجَحَ الدَفع» جَوابَ كُلِّ نِداء.
    ///
    /// <para><b>و«خارِجَ التَطوير» لا «في الإنتاج» فَقَط</b>: بيئَةٌ
    /// ثالِثَة (‏Staging) لَيسَت Development ولا Production، وهي بِالضَبطِ
    /// الشَقُّ الَّذي يُنسى. نَفسُ حَرفِ
    /// <c>AuthChannelSelection.StubViolations</c>.</para>
    /// </summary>
    public static IReadOnlyList<string> StubViolations(
        bool isDevelopment, IEnumerable<RegisteredPaymentProvider> registered)
    {
        if (isDevelopment) return Array.Empty<string>();
        return registered
            .Where(p => p.IsDevelopmentStub)
            .Select(p => p.ProviderName)
            .ToList();
    }

    /// <summary>يَرمي إن سُجِّلَ مُزَوِّدُ دَفعٍ مُحاكٍ خارِجَ التَطوير.
    /// يُستَدعى <b>بَعدَ</b> بِناءِ المُضيفِ وقَبلَ أَوَّلِ طَلَب.</summary>
    public static void AssertNoStubsOutsideDevelopment(
        bool isDevelopment, IEnumerable<RegisteredPaymentProvider> registered)
    {
        var violations = StubViolations(isDevelopment, registered);
        if (violations.Count == 0) return;
        throw new InvalidOperationException(
            "مُزَوِّدُ دَفعٍ مُحاكٍ مُسَجَّلٌ خارِجَ بيئَة التَطوير: "
            + string.Join("، ", violations)
            + ". وهُوَ يَقول «نَجَحَ الدَفع» لِكُلّ نِداء — فَالإقلاعُ "
            + "يَتَوَقَّف هُنا بَدَلَ أَن تُمنَح باقَةٌ بِلا قَبض.");
    }

    /// <summary>وَصفُ ما حُلَّ فِعلاً مِن الوِعاء — <c>null</c> إن لَم
    /// يُسَجَّل شَيء.</summary>
    public static RegisteredPaymentProvider? Describe(IPaymentProvider? provider)
        => provider is null
            ? null
            : new(provider.ProviderName, provider is IDevelopmentStubPaymentProvider);
}
