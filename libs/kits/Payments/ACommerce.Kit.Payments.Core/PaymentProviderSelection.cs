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
    Mock,

    /// <summary>
    /// <para><b>وَضعُ التَجرِبَة</b> — يَنجَح، <b>ويُعلِنُ أَنَّه
    /// تَجرِبَة</b>: في اسمِه، وفي مَرجِعِ الصَفقَةِ المُخَزَّن، وعَلى
    /// الشاشَةِ قَبلَ النَقرَةِ الأَخيرَة. ولا فاتورَةَ ولا رَقمَ
    /// ضَريبِيّاً ولا رابِطَ مُستَنَد.</para>
    ///
    /// <para><b>ولا يُنتَقى إلّا بِكِتابَةٍ صَريحَة</b>
    /// (<c>Payments:Provider = simulation</c>) — <b>الغِيابُ لا
    /// يُنتِجُ تَجرِبَةً أَبَداً</b>، وذلكَ بِعَينِه مَعنى «فَوقَ
    /// الحُرّاسِ لا حَولَها».</para>
    /// </summary>
    Simulation
}

/// <summary>مُزَوِّدُ دَفعٍ مُسَجَّلٌ فِعلاً في وِعاءِ الخِدَمات — كَما
/// يَراهُ حارِسُ الإقلاع.</summary>
/// <param name="ProviderName">اسمُ المُزَوِّد (لِلتَشخيصِ والرِسالَة).</param>
/// <param name="IsDevelopmentStub">أَيَحمِلُ عَلامَةَ المُحاكاة؟</param>
/// <param name="IsSimulated">أَيَحمِلُ عَلامَةَ <b>التَجرِبَة</b>؟ —
/// عَلامَةٌ أُخرى غَيرُ الأُولى، ويَقرَؤُها الحارِسُ المَعكوس
/// <see cref="PaymentProviderSelection.AssertSimulationIsExplicit"/>.
/// <b>ولَها قيمَةٌ افتِراضِيَّة</b> فَتَبقى كُلُّ نِداءاتِ
/// <c>RegisteredPaymentProvider</c> القائِمَةِ صَحيحَةً بِلا تَعديلِ
/// حَرف (القاعِدَة ٣).</param>
public sealed record RegisteredPaymentProvider(
    string ProviderName, bool IsDevelopmentStub, bool IsSimulated = false);

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
    ///
    /// <para><b>وقَد وُجِدَ المُزَوِّدُ الَّذي يَختارُه</b> (‏2026-08-30،
    /// ‏ADR-025): <see cref="SimulatedPaymentProvider"/>. فَالمِفتاحُ
    /// أُضيفَ <b>مَعَه لا قَبلَه</b>، وشَرطُ ‏ADR-014 §٢-ج مَحفوظٌ
    /// بِحَرفِه. وهذا الحِملُ يَبقى مُفَوِّضاً إلى الحِملِ ذي
    /// المُدخَلَينِ بِـ<c>null</c> — فَجَدوَلُ
    /// <c>PaymentProviderSelectionTests</c> يَبقى أَخضَرَ <b>بِلا
    /// تَعديلِ حَرف</b> (القاعِدَة ٣)، ونَظيرُه القائِمُ
    /// <c>PlanPurchasePolicy.IsPurchasable</c> بِحِملَيه.</para>
    /// </summary>
    public static PaymentProviderChoice Decide(bool isDevelopment)
        => Decide(isDevelopment, configured: null);

    /// <summary>مِفتاحُ التَهيئَة — بِنَفسِ شَكلِ
    /// <c>Auth:Sms:Provider</c>، والمُتَغَيِّر
    /// <c>Payments__Provider</c>.</summary>
    public const string ProviderKey = "Payments:Provider";

    /// <summary>
    /// <para><b>القَرار — جَدوَلٌ بِنَفسِ تَرتيبِ
    /// <c>AuthChannelSelection.Decide</c> حَرفاً</b>:</para>
    /// <list type="number">
    ///   <item>قيمَةٌ فِعلِيَّةٌ مَكتوبَةٌ صَراحَةً ⇒ مُزَوِّدُها،
    ///   <b>في أَيِّ بيئَة</b>. وهي اليَومَ <c>simulation</c>
    ///   وَحدَها.</item>
    ///   <item>وإلّا في <c>Development</c> ⇒ المُحاكي — <b>كَما هُوَ
    ///   بِلا حَرف</b>.</item>
    ///   <item>وإلّا ⇒ <see cref="PaymentProviderChoice.Unavailable"/>:
    ///   الفَشَلُ المُغلَق — <b>كَما هُوَ بِلا حَرف</b>.</item>
    /// </list>
    ///
    /// <para><b>والفَرعُ الأَوَّلُ هُوَ الخَطَرُ بِعَينِه فَيُقاسُ
    /// بِطَرَفَيه</b>: قيمَةٌ مَجهولَةٌ، أَو غائِبَةٌ، أَو
    /// <c>"mock"</c> مَكتوبَةٌ بِاليَد — <b>لا تُنتِجُ تَجرِبَةً في
    /// الإنتاجِ أَبَداً</b>. التَجرِبَةُ تَقَعُ لِأَنّ أَحَداً
    /// كَتَبَها، لا لِأَنّ تَهيئَةً غابَت.</para>
    /// </summary>
    public static PaymentProviderChoice Decide(bool isDevelopment, string? configured)
    {
        // ‏١) قيمَةٌ فِعلِيَّةٌ مَكتوبَةٌ صَراحَةً — في أَيِّ بيئَة.
        //    والمُقارَنَةُ بِالثابِتِ لا بِحَرفِيَّةٍ مَنسوخَة، وبِـ
        //    `Ordinal` بَعدَ قَصٍّ: قيمَةُ تَهيئَةٍ فيها مِسافَةٌ
        //    تُقبَل، وقيمَةٌ تُشبِهُها ولا تُطابِقُها **تُرَدّ**.
        if (string.Equals(configured?.Trim(), SimulatedPaymentProvider.ConfiguredValue,
                StringComparison.OrdinalIgnoreCase))
            return PaymentProviderChoice.Simulation;

        // ‏٢) وإلّا في التَطوير: المُحاكي — كَما كانَ.
        // ‏٣) وإلّا: الفَشَلُ المُغلَق — كَما كان.
        return isDevelopment ? PaymentProviderChoice.Mock : PaymentProviderChoice.Unavailable;
    }

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

    // ─── الحارِسُ المَعكوس — التَجرِبَةُ لا تَقَعُ بِالغِياب ──────────

    /// <summary>
    /// <para><b>مُزَوِّدُ تَجرِبَةٍ حُلَّ بِلا أَن يَكتُبَه أَحَد</b> —
    /// وهذا هُوَ الخَطَرُ الوَحيدُ الَّذي يُنشِئُه وَضعُ التَجرِبَة،
    /// فَلَه فاحِصُه.</para>
    ///
    /// <para><b>وهُوَ عَكسُ الحارِسِ الأَوَّلِ لا تَخفيفٌ لَه</b>: ذاكَ
    /// يَمنَعُ مُحاكِياً تَسَرَّبَ، وهذا يَمنَعُ تَجرِبَةً وَقَعَت
    /// صامِتَةً. والاثنانِ يُنادَيانِ مَعاً في الإقلاع.</para>
    /// </summary>
    public static IReadOnlyList<string> SilentSimulationViolations(
        string? configured, IEnumerable<RegisteredPaymentProvider> registered)
    {
        var explicitly = string.Equals(
            configured?.Trim(), SimulatedPaymentProvider.ConfiguredValue,
            StringComparison.OrdinalIgnoreCase);
        if (explicitly) return Array.Empty<string>();

        return registered.Where(p => p.IsSimulated).Select(p => p.ProviderName).ToList();
    }

    /// <summary>يَرمي إن حُلَّ مُزَوِّدُ تَجرِبَةٍ بِلا كِتابَةٍ
    /// صَريحَةٍ في التَهيئَة. يُستَدعى بِجِوارِ
    /// <see cref="AssertNoStubsOutsideDevelopment"/> — <b>بَعدَ بِناءِ
    /// المُضيفِ وقَبلَ أَوَّلِ طَلَب</b>.</summary>
    public static void AssertSimulationIsExplicit(
        string? configured, IEnumerable<RegisteredPaymentProvider> registered)
    {
        var violations = SilentSimulationViolations(configured, registered);
        if (violations.Count == 0) return;
        throw new InvalidOperationException(
            "مُزَوِّدُ دَفعٍ في وَضعِ التَجرِبَةِ مُسَجَّلٌ بِلا اختِيارٍ صَريح: "
            + string.Join("، ", violations)
            + $". وَضعُ التَجرِبَةِ يُطلَب ولا يَقَع بِالغِياب — اضبِط "
            + $"«{ProviderKey}» بِـ«{SimulatedPaymentProvider.ConfiguredValue}» "
            + "إن كُنتَ تَقصِدُه، وإلّا فَالتَسجيلُ خَطَأ.");
    }

    /// <summary>وَصفُ ما حُلَّ فِعلاً مِن الوِعاء — <c>null</c> إن لَم
    /// يُسَجَّل شَيء.</summary>
    public static RegisteredPaymentProvider? Describe(IPaymentProvider? provider)
        => provider is null
            ? null
            : new(provider.ProviderName,
                  provider is IDevelopmentStubPaymentProvider,
                  PaymentSimulationSurface.IsSimulated(provider));
}
