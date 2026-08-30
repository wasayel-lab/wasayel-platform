namespace ACommerce.Templates.Customer.Marketplace.Services.Deals;

// ═══ طَريقَةُ الدَفعِ عِندَ إتمامِ الشِراء — مَعجَمٌ مُغلَقٌ بِتَعريفٍ واحِد ══
//
// **العِلَّةُ المَقيسَة (‏2026-08-30)**: جِسمُ
// `POST /{slug}/checkout/submit` كانَ يَشتَرِط `if (pay != "cod")` قَبلَ
// نِداءِ مُزَوِّدِ الدَفع، **والاستِمارَةُ تُرسِل `card` أَو `cash`**
// (‏`CheckoutPage.razor` السَطران ‏92 و‏99). والرَمزُ `"cod"` وَرَدَ
// **مَرَّةً واحِدَةً في المُستَودَعِ كُلِّه**: في ذلكَ الشَرطِ نَفسِه.
// فَالشَرطُ **لا يَكذِبُ أَبَداً** — أَي أَنّ كُلَّ طَلَبٍ يَستَدعي
// `AuthorizeAsync`، **والدَفعُ عِندَ الاستِلامِ أَوَّلُها**؛ وفي الإنتاجِ
// يَرُدُّ `UnavailablePaymentProvider` بِـ`Failed` فَيُعَلَّق
// `payment_status = Failed` عَلى صَفقَةِ نَقدٍ عِندَ الباب.
//
// **وهذا هُوَ بِعَينِه الشَكلُ الَّذي كَتَبَه `StudioUpgradeReason`
// بَعدَ كَلفَتِه**: مَعجَمٌ مُغلَقٌ مَكتوبٌ حَرفِيّاً في طَرَفَينِ
// يَنجَرِف، وخَطَأُ حَرفٍ في أَحَدِهِما يَجعَلُ الشَرطَ صامِتاً. فَلا
// حَرفِيَّةَ بَعدَ اليَوم: الاستِمارَةُ والنُقطَةُ تَقرَآنِ **هذا
// المِلَفَّ** (القاعِدَة ٨: لا مَعجَمَ رابِع).
//
// **ولا أُنبوبَ ثانٍ لِلقَرار**: `PlanPurchasePolicy` تَحرُسُ **باقَةً
// يَشتَريها مُستَخدِمُ المَتجَر**، وهذِه تَحرُسُ **عَرَبونَ صَفقَةٍ
// يَدفَعُه مُشتَرٍ**. تَدَفُّقانِ يَفصِلُهُما ‏ADR-009 بِالبِناء، ولِكُلٍّ
// مُدخَلُه: تِلكَ تَقرَأُ `Plan.Price`، وهذِه تَقرَأُ اختِيارَ المُشتَري.
// والمُشتَرَكُ بَينَهُما مُدخَلٌ واحِدٌ هُوَ
// `Tenant.PaymentProviderConfigured` — **ودَلالَتُه واحِدَة**: «هذا
// المَتجَرُ يَقبِض».

/// <summary>
/// <para><b>طُرُقُ الدَفعِ المَعروضَةُ في إتمامِ الشِراء</b> — قيمَتانِ
/// لا ثالِثَة، وهُما بِحَرفِهِما ما تُرسِلُه الاستِمارَةُ اليَوم.</para>
/// </summary>
public static class CheckoutPayMethods
{
    /// <summary>بِطاقَة — <b>وهي وَحدَها الَّتي تَستَدعي
    /// المُزَوِّد</b>.</summary>
    public const string Card = "card";

    /// <summary>نَقدٌ عِندَ الاستِلام — <b>لا يَمُرُّ بِمُزَوِّدٍ
    /// إطلاقاً</b>. وكانَ يَمُرّ.</summary>
    public const string CashOnDelivery = "cash";

    public static readonly IReadOnlyList<string> All = new[] { Card, CashOnDelivery };

    /// <summary>رَمزُ الخَرقِ حينَ تُطلَبُ البِطاقَةُ في مَتجَرٍ لا
    /// يَقبِض — <b>يُقرَأُ في الشاشَةِ لِيُختارَ نَصُّ القامُوس</b>،
    /// فَلا رَفضَ مُبتلَع.</summary>
    public const string CardUnavailable = "pay_card_unavailable";

    public static bool Contains(string? value)
        => value is not null && All.Contains(value, StringComparer.Ordinal);
}

/// <summary>
/// <para><b>قَرارُ طَريقَةِ الدَفع — دَوالُّ نَقِيَّة.</b> لا Marten،
/// ولا HTTP، ولا وَقت: تُقاسُ بِجَدوَلٍ بِلا قاعِدَةِ بَيانات.</para>
/// </summary>
public static class CheckoutPaymentPolicy
{
    /// <summary>
    /// <para><b>أَتُعرَضُ البِطاقَةُ أَصلاً؟</b> — <c>true</c> فَقَط
    /// حينَ يَقبِضُ هذا المَتجَر.</para>
    ///
    /// <para><b>والشاشَةُ والنُقطَةُ تَقرَآنِ هذِه الدالَّةَ
    /// نَفسَها</b> — سابِقَةُ <c>Plans.razor</c> و
    /// <c>PlanPurchasePolicy.Visible</c> حَرفاً: فَلا تَعرِضُ الشاشَةُ
    /// خِياراً سَتَرُدُّه النُقطَة، ولا يُرسَمُ زِرٌّ يَقودُ إلى فَشَلٍ
    /// مُبتلَع (القاعِدَة ١٢).</para>
    /// </summary>
    public static bool CardOffered(bool paymentProviderConfigured)
        => paymentProviderConfigured;

    /// <summary>
    /// <para><b>ما اختارَهُ المُشتَري مُطَبَّعاً</b> — قيمَةٌ خارِجَ
    /// المَعجَمِ تُقرَأُ نَقداً عِندَ الاستِلام، لِأَنَّها **الجَوابُ
    /// الَّذي لا يُحَرِّكُ مالاً**. الفَشَلُ مُغلَقٌ نَحوَ عَدَمِ
    /// القَبض، لا نَحوَه.</para>
    /// </summary>
    public static string Normalize(string? raw)
    {
        var v = raw?.Trim();
        return string.Equals(v, CheckoutPayMethods.Card, StringComparison.Ordinal)
            ? CheckoutPayMethods.Card
            : CheckoutPayMethods.CashOnDelivery;
    }

    /// <summary>
    /// <para><b>القَرار — بِثَلاثَةِ مَخارِجَ لا اثنَين.</b></para>
    /// <list type="bullet">
    ///   <item><c>Refusal</c> غَيرُ فارِغ ⇒ البِطاقَةُ طُلِبَت ولا
    ///   يَقبِضُ المَتجَر: <b>يُرَدُّ الطَلَبُ ولا يُبَدَّلُ
    ///   صامِتاً</b>. وتَحويلُ طَلَبِ بِطاقَةٍ إلى نَقدٍ بِلا كَلِمَةٍ
    ///   تَبديلُ عَقدٍ مِن تَحتِ المُشتَري.</item>
    ///   <item><c>Method == Card</c> ⇒ يُنادى المُزَوِّد.</item>
    ///   <item>وإلّا ⇒ نَقدٌ عِندَ الاستِلام، <b>ولا نِداءَ
    ///   إطلاقاً</b>.</item>
    /// </list>
    /// </summary>
    public static (string Method, string? Refusal) Decide(
        string? requested, bool paymentProviderConfigured)
    {
        var method = Normalize(requested);

        if (method == CheckoutPayMethods.Card && !CardOffered(paymentProviderConfigured))
            return (CheckoutPayMethods.CashOnDelivery, CheckoutPayMethods.CardUnavailable);

        return (method, null);
    }

    /// <summary><b>أَيُنادى مُزَوِّدُ الدَفعِ لِهذِه الطَريقَة؟</b> —
    /// المَوضِعُ الوَحيدُ الَّذي يُجيب. وكانَ الجَوابُ <c>true</c>
    /// دائِماً.</summary>
    public static bool CallsProvider(string method)
        => string.Equals(method, CheckoutPayMethods.Card, StringComparison.Ordinal);
}
