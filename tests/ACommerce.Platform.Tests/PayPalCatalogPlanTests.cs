using System.Net;
using System.Text.Json;
using ACommerce.Kit.Payments.Providers.PayPal;
using ACommerce.Kit.Subscriptions;
using ACommerce.Templates.Customer.Marketplace.Services.Subscriptions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace ACommerce.Platform.Tests;

// ═══ إنشاءُ خُطَّةِ PayPal مِن شاشَةِ المُشرِف ═════════════════════════
//
// **العِلَّةُ الَّتي كَتَبَت هذا المِلَفّ**: خُطُواتُ `docs/DEPLOY.md`
// ‏§٢·ج كانَت تَفتَرِض صَفحَةَ المُنتَجات/الخُطَط في لَوحَةِ PayPal،
// **وقَد تَعَذَّرَ على المالِكِ فَتحُها**. فَبُنِيَ المَسارُ بِالواجِهَةِ
// REST — واللَوحَةُ تَصير طَريقاً أَوَّلَ لا شَرطاً.
//
// **وما يُقاسُ هُنا هُوَ ما يَنكَسِر صامِتاً**: تَرتيبُ النِداءَين،
// وشَكلُ الجِسمَين، ورَأسُ مَرَّة-واحِدَة وحَتمِيَّتُه، ورِسالَةُ
// الفَشَل، وأَنّ الحارِسَ يَسبِقُ أَوَّلَ كِتابَة، وأَنّ النَموذَجَ
// لا يُرسَم بِلا تَهيئَة.
//
// **والدَينُ المُعلَن**: لا حِسابَ PayPal في هذا المُستودَع ولا يُطلَب.
// فَلَم يُنشَأ مُنتَجٌ حَقيقيٌّ ولا خُطَّةٌ حَقيقِيَّة، والمُعامَلَةُ
// الحَقيقِيَّةُ (‏Marten) لا تُقاس هُنا. المُبرهَنُ: **ما كُنّا
// سَنُرسِلُه**، والقَرارُ كامِلاً بِدَوالَّ نَقِيَّة.

/// <summary>مُعالِجٌ يَلتَقِط الطَلَباتِ ويَرُدُّ رُدوداً مُرَتَّبَة —
/// نَفسُ شَكلِ نَظيرِه في <c>PayPalProviderTests</c> بِاسمٍ آخَر.
/// و<c>internal</c> لا <c>file</c> لِأَنَّه يَظهَر في تَوقيعِ
/// مُساعِدٍ داخِلَ صِنفِ الاختِبار.</summary>
internal sealed class CatalogHandler : HttpMessageHandler
{
    private readonly Queue<(HttpStatusCode Status, string Body)> _replies = new();

    public List<HttpRequestMessage> Requests { get; } = new();
    public List<string?> Bodies { get; } = new();

    public CatalogHandler Then(HttpStatusCode status, string body)
    {
        _replies.Enqueue((status, body));
        return this;
    }

    public CatalogHandler ThenToken()
        => Then(HttpStatusCode.OK,
            "{\"access_token\":\"A21AA\",\"token_type\":\"Bearer\",\"expires_in\":32400}");

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        Bodies.Add(request.Content is null
            ? null
            : await request.Content.ReadAsStringAsync(cancellationToken));

        var (status, body) = _replies.Count > 0 ? _replies.Dequeue() : (HttpStatusCode.OK, "{}");
        return new HttpResponseMessage(status) { Content = new StringContent(body) };
    }
}

public class PayPalCatalogPlanTests
{
    private static readonly DateTime Now = new(2026, 08, 24, 12, 00, 00, DateTimeKind.Utc);

    private static PayPalOptions Opts(string secret = "very-secret") => new()
    {
        ClientId = "AY-client", ClientSecret = secret,
        Environment = PayPalEnvironment.Live, WebhookId = "WH-TEST", TimeoutSeconds = 5,
    };

    private static PayPalPaymentProvider Provider(HttpMessageHandler handler, PayPalOptions? opts = null)
        => new(Options.Create(opts ?? Opts()), new HttpClient(handler),
               new PayPalTokenCache(), NullLogger<PayPalPaymentProvider>.Instance);

    private static PayPalGateway Gateway(HttpMessageHandler handler, PayPalOptions? opts = null)
        => new(opts ?? Opts(), Provider(handler, opts));

    private static PayPalPlanDraft Draft(
        string slug = "manual", string name = "Wasayel Premium",
        decimal amount = 9.99m, string currency = "USD",
        string interval = PayPalPlanIntervals.Month)
        => new(slug, name, amount, currency, interval);

    private static CatalogHandler Happy() => new CatalogHandler()
        .ThenToken()
        .Then(HttpStatusCode.Created, "{\"id\":\"PROD-XXCD1234QWER65782\"}")
        .Then(HttpStatusCode.Created, "{\"id\":\"P-5ML4271244454362WXNWU5NQ\"}");

    // ═══ ١. مُنتَجٌ ثُمَّ خُطَّة — بِالجِسمَينِ الصَحيحَينِ وبِالرُؤوس ══

    /// <summary>
    /// <para><b>التَرتيبُ مُلزِمٌ لا تَفضيليّ</b>: مُنشِئُ الخُطَّةِ
    /// يَشتَرِط <c>product_id</c> قائِماً، فَلا خُطَّةَ بِلا مُنتَج.
    /// والعَدّادُ هُوَ البُرهان: ثَلاثَةُ طَلَبات — رَمزٌ، فَمُنتَجٌ،
    /// فَخُطَّة.</para>
    /// </summary>
    [Fact]
    public async Task CatalogPlan_CreatesTheProductThenThePlan_OnTheDocumentedPaths()
    {
        var handler = Happy();

        var created = await Gateway(handler).CreateCatalogPlanAsync(Draft());

        Assert.Equal("PROD-XXCD1234QWER65782", created.ProductId);
        Assert.Equal("P-5ML4271244454362WXNWU5NQ", created.PlanId);

        Assert.Equal(3, handler.Requests.Count);
        Assert.Equal(PayPalPaymentProvider.TokenPath, handler.Requests[0].RequestUri!.AbsolutePath);
        Assert.Equal(PayPalEnvironment.LiveBaseUrl + PayPalPaymentProvider.ProductsPath,
            handler.Requests[1].RequestUri!.ToString());
        Assert.Equal(PayPalEnvironment.LiveBaseUrl + PayPalPaymentProvider.PlansPath,
            handler.Requests[2].RequestUri!.ToString());

        foreach (var i in new[] { 1, 2 })
        {
            Assert.Equal(HttpMethod.Post, handler.Requests[i].Method);
            Assert.Equal("Bearer", handler.Requests[i].Headers.Authorization!.Scheme);
            Assert.Single(handler.Requests[i].Headers.GetValues(PayPalPaymentProvider.RequestIdHeader));
        }
    }

    /// <summary>
    /// <para><b>ولا يُمَرَّرُ <c>id</c> في جِسمِ المُنتَج</b> — وهذا
    /// فَخٌّ حَقيقيّ: مُخَطَّطُ المُنتَجِ يَقبَل ‏6..50 مِحرَفاً،
    /// ومُنشِئُ الخُطَّةِ يَشتَرِط ‏22 بِالضَبط. فَتَمريرُ SKU خاصٍّ
    /// <b>يَنجَح ثُمَّ تُرفَض الخُطَّة</b> — ويَبقى مُنتَجٌ يَتيمٌ
    /// عِندَ PayPal.</para>
    /// </summary>
    [Fact]
    public async Task ProductBody_CarriesNameTypeCategory_AndNeverAnId()
    {
        var handler = Happy();
        await Gateway(handler).CreateCatalogPlanAsync(Draft(name: "Wasayel Premium"));

        using var body = JsonDocument.Parse(handler.Bodies[1]!);
        Assert.Equal("Wasayel Premium", body.RootElement.GetProperty("name").GetString());
        Assert.Equal(PayPalCatalogPolicy.ProductType, body.RootElement.GetProperty("type").GetString());
        Assert.Equal(PayPalCatalogPolicy.ProductCategory,
            body.RootElement.GetProperty("category").GetString());
        Assert.False(body.RootElement.TryGetProperty("id", out _));
    }

    /// <summary>
    /// <para><b>جِسمُ الخُطَّة — والمَبلَغُ سِلسِلَةٌ نَصِّيَّةٌ لا
    /// رَقَم</b> (نَمَطُ PayPal يَشتَرِطُه حَرفاً)، و<c>total_cycles</c>
    /// صِفرٌ أَي لا نِهائِيَّة.</para>
    ///
    /// <para><b>والافتِراضانِ القاسِيانِ يُضبَطانِ صَراحَةً</b>:
    /// <c>setup_fee_failure_action</c> افتِراضُها <c>CANCEL</c> و
    /// <c>payment_failure_threshold</c> افتِراضُها صِفر — أَي إلغاءُ
    /// اشتِراكِ مَتجَرٍ عِندَ **أَوَّلِ** تَعَثُّرِ بِطاقَة.</para>
    /// </summary>
    [Fact]
    public async Task PlanBody_CarriesTheProductPriceAndFrequency_WithTheHarshDefaultsOverridden()
    {
        var handler = Happy();
        await Gateway(handler).CreateCatalogPlanAsync(
            Draft(name: "Premium Monthly", amount: 9.99m, currency: "USD"));

        using var body = JsonDocument.Parse(handler.Bodies[2]!);
        var root = body.RootElement;

        Assert.Equal("PROD-XXCD1234QWER65782", root.GetProperty("product_id").GetString());
        Assert.Equal("Premium Monthly", root.GetProperty("name").GetString());
        Assert.Equal(PayPalCatalogPolicy.PlanStatusActive, root.GetProperty("status").GetString());

        var cycle = Assert.Single(root.GetProperty("billing_cycles").EnumerateArray().ToArray());
        Assert.Equal(PayPalCatalogPolicy.TenureRegular, cycle.GetProperty("tenure_type").GetString());
        Assert.Equal(1, cycle.GetProperty("sequence").GetInt32());
        Assert.Equal(PayPalCatalogPolicy.InfiniteCycles, cycle.GetProperty("total_cycles").GetInt32());
        Assert.Equal(PayPalPlanIntervals.Month,
            cycle.GetProperty("frequency").GetProperty("interval_unit").GetString());
        Assert.Equal(1, cycle.GetProperty("frequency").GetProperty("interval_count").GetInt32());

        var price = cycle.GetProperty("pricing_scheme").GetProperty("fixed_price");
        Assert.Equal(JsonValueKind.String, price.GetProperty("value").ValueKind);
        Assert.Equal("9.99", price.GetProperty("value").GetString());
        Assert.Equal("USD", price.GetProperty("currency_code").GetString());

        var prefs = root.GetProperty("payment_preferences");
        Assert.True(prefs.GetProperty("auto_bill_outstanding").GetBoolean());
        Assert.Equal(PayPalCatalogPolicy.SetupFeeFailureAction,
            prefs.GetProperty("setup_fee_failure_action").GetString());
        Assert.Equal(PayPalCatalogPolicy.PaymentFailureThreshold,
            prefs.GetProperty("payment_failure_threshold").GetInt32());
    }

    // ═══ ٢. رَأسُ مَرَّة-واحِدَة — حَتميٌّ مِن المُدخَلات ═══════════════

    /// <summary>
    /// <para><b>نَفسُ المُدخَلاتِ ⇒ نَفسُ المِفتاح، دائِماً.</b> ولَو
    /// كانَ مُشتَقّاً مِن زَمَنٍ أَو عَشوائيَّةٍ لَأَنشَأَت نَقرَتانِ
    /// مُتَتالِيَتانِ <b>خُطَّتَين</b> — والثانِيَةُ لا تُحذَف مِن
    /// PayPal.</para>
    /// </summary>
    [Fact]
    public void RequestIds_AreDeterministic_ForTheSameInputs()
    {
        Assert.Equal(PayPalCatalogPolicy.ProductRequestId(Draft()),
                     PayPalCatalogPolicy.ProductRequestId(Draft()));
        Assert.Equal(PayPalCatalogPolicy.PlanRequestId("PROD-1", Draft()),
                     PayPalCatalogPolicy.PlanRequestId("PROD-1", Draft()));
    }

    /// <summary><b>ومُدخَلٌ يَتَغَيَّر ⇒ مِفتاحٌ آخَر</b> — وإلّا لَما
    /// أَمكَنَ إنشاءُ خُطَّةٍ بِسِعرٍ جَديدٍ أَبَداً خِلالَ ‏72
    /// ساعَة.</summary>
    [Theory]
    [InlineData("other", "Wasayel Premium", 9.99, "USD", PayPalPlanIntervals.Month)]
    [InlineData("manual", "Wasayel Basic", 9.99, "USD", PayPalPlanIntervals.Month)]
    [InlineData("manual", "Wasayel Premium", 19.99, "USD", PayPalPlanIntervals.Month)]
    [InlineData("manual", "Wasayel Premium", 9.99, "EUR", PayPalPlanIntervals.Month)]
    [InlineData("manual", "Wasayel Premium", 9.99, "USD", PayPalPlanIntervals.Year)]
    public void PlanRequestId_Differs_WhenAnyInputDiffers(
        string slug, string name, double amount, string currency, string interval)
    {
        var changed = new PayPalPlanDraft(slug, name, (decimal)amount, currency, interval);
        Assert.NotEqual(PayPalCatalogPolicy.PlanRequestId("PROD-1", Draft()),
                        PayPalCatalogPolicy.PlanRequestId("PROD-1", changed));
    }

    /// <summary>والمُنتَجُ لا يَتَبَدَّل بِتَبَدُّلِ السِعر: تَغييرُ
    /// السِعرِ يُنشِئ خُطَّةً جَديدَةً على <b>نَفسِ المُنتَج</b>، لا
    /// مُنتَجاً ثانِياً بِنَفسِ الاسم.</summary>
    [Fact]
    public void ProductRequestId_IgnoresPriceAndPeriod_ButNotSlugOrName()
    {
        Assert.Equal(
            PayPalCatalogPolicy.ProductRequestId(Draft()),
            PayPalCatalogPolicy.ProductRequestId(Draft(amount: 49m, interval: PayPalPlanIntervals.Year)));

        Assert.NotEqual(PayPalCatalogPolicy.ProductRequestId(Draft()),
                        PayPalCatalogPolicy.ProductRequestId(Draft(name: "Another")));
    }

    /// <summary>والمِفتاحُ المُرسَل هُوَ المَحسوب — لا نُسخَةٌ ثانِيَةٌ
    /// تُبنى في جِسمِ النِداء.</summary>
    [Fact]
    public async Task TheSentRequestIds_AreTheOnesThePolicyComputes()
    {
        var handler = Happy();
        var draft = Draft();
        await Gateway(handler).CreateCatalogPlanAsync(draft);

        Assert.Equal(PayPalCatalogPolicy.ProductRequestId(draft),
            handler.Requests[1].Headers.GetValues(PayPalPaymentProvider.RequestIdHeader).Single());
        Assert.Equal(PayPalCatalogPolicy.PlanRequestId("PROD-XXCD1234QWER65782", draft),
            handler.Requests[2].Headers.GetValues(PayPalPaymentProvider.RequestIdHeader).Single());
    }

    // ═══ ٣. الفَشَل: يَرمي بِرَمزِ PayPal ونَصِّه، وبِلا سِرّ ═══════════

    [Fact]
    public async Task Failure_ThrowsCarryingPayPalsCodeAndText()
    {
        var handler = new CatalogHandler()
            .ThenToken()
            .Then(HttpStatusCode.UnprocessableEntity,
                """
                {"name":"UNPROCESSABLE_ENTITY","message":"The requested action could not be performed.",
                 "details":[{"issue":"INVALID_PARAMETER_SYNTAX","description":"bad value"}]}
                """);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Gateway(handler).CreateCatalogPlanAsync(Draft()));

        Assert.Contains("422", ex.Message);
        Assert.Contains("UNPROCESSABLE_ENTITY", ex.Message);
        Assert.Contains("INVALID_PARAMETER_SYNTAX", ex.Message);
        Assert.Contains("could not be performed", ex.Message);
    }

    /// <summary>ورِسالَةُ الخَطَإ <b>لا تَحمِل السِرّ</b> — رِسالَةٌ
    /// تُعرَض على شاشَةٍ وتُكتَب في لوغٍ فيها سِرٌّ هي تَسريب.</summary>
    [Fact]
    public async Task FailureMessage_NeverCarriesTheClientSecret()
    {
        var handler = new CatalogHandler()
            .ThenToken()
            .Then(HttpStatusCode.BadRequest, "{\"name\":\"INVALID_REQUEST\"}");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Gateway(handler, Opts(secret: "xsecret-super")).CreateCatalogPlanAsync(Draft()));

        Assert.DoesNotContain("xsecret-super", ex.Message);
        Assert.DoesNotContain("AY-client", ex.Message);
    }

    /// <summary>ورَدٌّ ناجِحٌ بِلا مُعَرِّفٍ <b>يَرمي ولا يُخَزَّن</b>:
    /// «‏P-» فارِغَةٌ في الوَثيقَةِ تَكسِر الدَفعَ بَعدَ أَيّام، وتُقرَأ
    /// حينَئِذٍ كَعُطلٍ في PayPal لا كَحَقلٍ فارِغٍ عِندَنا.</summary>
    [Fact]
    public async Task SuccessWithoutAnId_Throws_RatherThanStoringAnEmptyPlanId()
    {
        var handler = new CatalogHandler()
            .ThenToken()
            .Then(HttpStatusCode.Created, "{\"id\":\"PROD-1\"}")
            .Then(HttpStatusCode.Created, "{\"status\":\"ACTIVE\"}");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Gateway(handler).CreateCatalogPlanAsync(Draft()));
    }

    /// <summary>والبابُ بِلا مُزَوِّدٍ يَرمي بِرِسالَةٍ تُسَمّي السَبَب
    /// ولا يُنشِئ شَيئاً — <b>صِفرُ طَلَبٍ</b> هُوَ البُرهان.</summary>
    [Fact]
    public async Task Gateway_WithoutAProvider_Throws_AndSendsNothing()
    {
        var gateway = new PayPalGateway(new PayPalOptions(), provider: null);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => gateway.CreateCatalogPlanAsync(Draft()));

        Assert.Contains("PayPal", ex.Message);
        Assert.False(gateway.IsConfigured);
    }

    // ═══ ٤. البَوّابَة: حُقولٌ فارِغَةٌ تُرَدُّ بِرَمزٍ يُسَمّيها ═══════

    [Fact]
    public void ValidDraft_PassesTheGate() => Assert.True(PayPalCatalogPolicy.IsValid(Draft()));

    [Theory]
    [InlineData("", "n", 1.0, "USD", PayPalPlanIntervals.Month, PayPalCatalogPolicy.PlanSlugEmpty)]
    [InlineData("manual", "  ", 1.0, "USD", PayPalPlanIntervals.Month, PayPalCatalogPolicy.NameEmpty)]
    [InlineData("manual", "n", 0.0, "USD", PayPalPlanIntervals.Month, PayPalCatalogPolicy.AmountNotPositive)]
    [InlineData("manual", "n", -1.0, "USD", PayPalPlanIntervals.Month, PayPalCatalogPolicy.AmountNotPositive)]
    [InlineData("manual", "n", 1.0, "SAR", PayPalPlanIntervals.Month, PayPalCatalogPolicy.CurrencyUnsupported)]
    [InlineData("manual", "n", 1.0, "USD", "DAY", PayPalCatalogPolicy.IntervalUnknown)]
    [InlineData("manual", "n", 1.0, "USD", "", PayPalCatalogPolicy.IntervalUnknown)]
    public void BrokenDraft_IsRejected_WithItsOwnCode(
        string slug, string name, double amount, string currency, string interval, string code)
    {
        var violations = PayPalCatalogPolicy.Validate(
            new PayPalPlanDraft(slug, name, (decimal)amount, currency, interval));
        Assert.Contains(violations, v => v.Code == code);
    }

    /// <summary><b>والريالُ مَرفوضٌ بِاسمِه</b>: لَيسَ عُملَةَ
    /// مُعامَلَةٍ في PayPal إطلاقاً — أَربَعَةُ مَصادِرَ رَسمِيَّةٍ
    /// مُتَطابِقَة. وقَبولُه كانَ يُنتِج ‏422 غامِضَةً بَعدَ
    /// نَشر.</summary>
    [Fact]
    public void Currencies_ExcludeTheSaudiRiyal_AndDefaultToTheDollar()
    {
        Assert.False(PayPalCurrencies.Contains("SAR"));
        Assert.True(PayPalCurrencies.Contains("usd"));
        Assert.Equal("USD", PayPalCurrencies.Default);
        Assert.Equal(25, PayPalCurrencies.Supported.Count);
        Assert.Contains(PayPalCurrencies.Default, PayPalCurrencies.Supported);
    }

    /// <summary>
    /// <para><b>و«لا» وَحدَها لا تَكفي — الرَفضُ يَقولُ لِماذا
    /// ويَقتَرِحُ البَديل.</b> مُشرِفٌ يَختار الريالَ **يَظُنُّ القَيدَ
    /// على حِسابِه** ويَذهَب يُفَتِّشُ اللَوحَة، والعِلَّةُ أَنّ الريالَ
    /// لَيسَ عُملَةَ مُعامَلَةٍ عِندَ PayPal أَصلاً. فَنَصُّ القامُوسِ
    /// يَحمِل ثَلاثَةً: الرَمزَ المَرفوضَ بِاسمِه، والسَبَبَ،
    /// و<c>USD</c> بَديلاً.</para>
    ///
    /// <para><b>ويُقاسُ النَصُّ لا وُجودُ المِفتاح</b>: مِفتاحٌ مَوجودٌ
    /// بِنَصٍّ يَقول «العُملَةُ غَيرُ مَدعومَة» يَمُرُّ مِن فَحصِ
    /// الوُجودِ ويَترُك المالِكَ حَيثُ كان.</para>
    /// </summary>
    [Fact]
    public void TheRejectionOfTheRiyal_NamesItAndOffersTheDollar()
    {
        // ١) البَوّابَةُ تَرُدُّ الريالَ بِرَمزٍ يُسَمّيه.
        var violation = Assert.Single(
            PayPalCatalogPolicy.Validate(Draft(currency: "SAR"))
                .Where(v => v.Code == PayPalCatalogPolicy.CurrencyUnsupported).ToArray());
        Assert.Contains("SAR", violation.MessageAr, StringComparison.Ordinal);

        // ٢) ونَصُّ الشاشَةِ يَقولُ العِلَّةَ ويَقتَرِحُ البَديل.
        var text = ACommerce.Platform.I18n.LocaleCatalog.Find(
            "ar", "admin.tenant_plan.err_paypal_plan_currency");

        Assert.False(string.IsNullOrWhiteSpace(text), "لا نَصَّ عَرَبيّاً لِرَفضِ العُملَة.");
        Assert.Contains("SAR", text!, StringComparison.Ordinal);
        Assert.Contains("USD", text!, StringComparison.Ordinal);
    }

    /// <summary>وعُملاتٌ بِلا كُسورٍ عَشرِيَّة تُصاغُ صَحيحَةً —
    /// «‏9.99» فيها تَرتَدُّ مِن PayPal.</summary>
    [Theory]
    [InlineData(9.99, "USD", "9.99")]
    [InlineData(10, "USD", "10.00")]
    [InlineData(1200, "JPY", "1200")]
    [InlineData(1200.4, "HUF", "1200")]
    public void Money_IsAStringInPayPalsFormat(double amount, string currency, string expected)
        => Assert.Equal(expected, PayPalCurrencies.Money((decimal)amount, currency));

    /// <summary>وقِراءَةُ النَموذَجِ دالَّةٌ نَقِيَّة: العُملَةُ
    /// الغائِبَةُ تَسقُط إلى الافتِراضِ المَقيس، والدَورِيَّةُ
    /// الغائِبَةُ <b>لا تَسقُط إلى شَهر</b> بَل تُرَدُّ بِخَرق.</summary>
    [Fact]
    public void ReadDraft_FallsBackOnlyWhereTheFallbackIsMeasured()
    {
        var d = PayPalCatalogPolicy.ReadDraft("manual", "  X  ", "12.50", null, "month");

        Assert.Equal("manual", d.PlanSlug);
        Assert.Equal("X", d.TrimmedName);
        Assert.Equal(12.50m, d.Amount);
        Assert.Equal(PayPalCurrencies.Default, d.NormalizedCurrency);
        Assert.Equal(PayPalPlanIntervals.Month, d.NormalizedInterval);

        Assert.Equal(0m, PayPalCatalogPolicy.ReadDraft("manual", "X", "لا رَقَم", "USD", "MONTH").Amount);
        Assert.Contains(PayPalCatalogPolicy.Validate(
                PayPalCatalogPolicy.ReadDraft("manual", "X", "9", "USD", null)),
            v => v.Code == PayPalCatalogPolicy.IntervalUnknown);
    }

    // ═══ ٥. المُعَرِّفُ يُكتَب في الوَثيقَة — مَرَّةً واحِدَة ═══════════

    /// <summary>
    /// <para><b>مُعَرِّفُ الوَثيقَةِ سلاجُ الباقَة</b> — فَباقَةٌ
    /// واحِدَةٌ لَها <b>رِباطٌ واحِدٌ</b> مَهما تَكَرَّرَ النِداء،
    /// والتَفَرُّدُ مِن مِفتاحِ الوَثيقَةِ نَفسِه لا مِن فَحصِ وُجودٍ
    /// في التَطبيق (نَفسُ حُجَّةِ <c>RecordFor</c>).</para>
    /// </summary>
    [Fact]
    public void Binding_IsKeyedByThePlanSlug_SoOnePlanNeverGetsTwoDocuments()
    {
        var first  = PayPalCatalogPolicy.BindingFor(
            Draft(), new PayPalCatalogPlan("PROD-1", "P-1"), "studio · u1", Now);
        var second = PayPalCatalogPolicy.BindingFor(
            Draft(amount: 19.99m), new PayPalCatalogPlan("PROD-1", "P-2"), "studio · u1", Now.AddDays(1));

        Assert.Equal("manual", first.Id);
        Assert.Equal(first.Id, second.Id);

        Assert.Equal("P-1", first.PlanId);
        Assert.Equal("PROD-1", first.ProductId);
        Assert.Equal("USD", first.Currency);
        Assert.Equal(PayPalPlanIntervals.Month, first.IntervalUnit);
        Assert.Equal(Now, first.CreatedAt);
        Assert.Equal("studio · u1", first.CreatedBy);
    }

    /// <summary><b>وصِفرُ كِتابَةٍ عِندَ كُلّ حالَةٍ لا تَكتُب</b> —
    /// والجَلسَةُ تُمَرَّرُ <c>null</c>، فَلَو لُمِسَت لَانفَجَرَت.
    /// (نَفسُ تِقنِيَةِ <c>PayPalBillingPolicyTests</c>.)</summary>
    [Fact]
    public void BindCatalogPlan_WritesNothing_WithoutASlugOrAPlanId()
    {
        Assert.False(PayPalBillingService.BindCatalogPlan(null!, null));
        Assert.False(PayPalBillingService.BindCatalogPlan(null!, new PlatformPlanPayPal()));
        Assert.False(PayPalBillingService.BindCatalogPlan(
            null!, new PlatformPlanPayPal { Id = "manual", PlanId = "  " }));
        Assert.False(PayPalBillingService.BindCatalogPlan(
            null!, new PlatformPlanPayPal { Id = "", PlanId = "P-1" }));
    }

    /// <summary>
    /// <para><b>وقاعِدَةُ التَرجيحِ واحِدَةٌ تَقرَؤُها الشاشَةُ
    /// والنُقطَةُ مَعاً</b>: الوَثيقَةُ تَفوز (وهي الأَحدَثُ قَصداً)،
    /// ثُمَّ المِلَفّ، ثُمَّ <c>null</c> — و<c>null</c> تَعني <b>لا
    /// بِطاقَةَ إطلاقاً</b> لا زِرّاً يَقول «قَريباً».</para>
    /// </summary>
    [Fact]
    public void PlanIdResolution_PrefersTheWrittenDocument_ThenTheDefinitionFile()
    {
        var file = PlatformPlanCatalog.ParseDefinition(
            """{"slug":"manual","labelAr":"ت","descriptionAr":"و","defaultGraceDays":14,"paypalPlanId":"P-FILE"}""");
        var doc = new PlatformPlanPayPal { Id = "manual", PlanId = "P-DOC" };

        Assert.Equal("P-DOC", PlatformPlanPayPalBinding.Resolve(file, doc));
        Assert.Equal("P-FILE", PlatformPlanPayPalBinding.Resolve(file, null));
        Assert.Equal("P-DOC", PlatformPlanPayPalBinding.Resolve(null, doc));
        Assert.Null(PlatformPlanPayPalBinding.Resolve(null, null));
        Assert.Null(PlatformPlanPayPalBinding.Resolve(
            PlatformPlanCatalog.Find("manual"), new PlatformPlanPayPal { Id = "manual" }));
    }

    /// <summary>وعُنوانُ المَورِدِ يَتبَع البيئَة، و<c>null</c> لِبيئَةٍ
    /// خارِجَ المَعجَم — فَلا يُعرَض عُنوانُ إنتاجٍ لِخُطَّةِ
    /// اختِبار.</summary>
    [Fact]
    public void PlanResourceUrl_FollowsTheConfiguredEnvironment()
    {
        Assert.Equal(PayPalEnvironment.LiveBaseUrl + "/v1/billing/plans/P-1",
            PayPalEnvironment.PlanResourceUrl(PayPalEnvironment.Live, "P-1"));
        Assert.Equal(PayPalEnvironment.SandboxBaseUrl + "/v1/billing/plans/P-1",
            PayPalEnvironment.PlanResourceUrl(PayPalEnvironment.Sandbox, "P-1"));
        Assert.Null(PayPalEnvironment.PlanResourceUrl("production", "P-1"));
        Assert.Null(PayPalEnvironment.PlanResourceUrl(PayPalEnvironment.Live, " "));
    }

    // ═══ ٥·ب. خَطَأُ الاستِحقاقِ يُسَمّى ولا يُبتلَع ═══════════════════
    //
    // **الخَطَرُ المَقيس**: الاشتِراكاتُ تَقوم على **Reference
    // Transactions**، وPayPal تُصَنِّفُها *limited-release*، وتُوَثِّق
    // خَطَأً بِاسمِه: `Merchant not enabled for reference transaction`.
    // **والسيناريو المُتَوَقَّع**: الخُطَّةُ تُنشَأُ بِنَجاح ثُمَّ يَفشَل
    // تَفعيلُ أَوَّلِ اشتِراك — أَي أَنّ الخَطَأَ يَقَع على مَسارِ
    // رابِطِ الدَفع، وهُوَ المَسارُ الَّذي كانَ يَرُدُّ «تَعَذَّرَ
    // إنشاءُ رابِطِ الدَفع — راجِع سِجِلَّ الخادِم».

    /// <summary>
    /// <para><b>العِبارَةُ تُطابَقُ لا الرَمز</b> — والمَقروءُ مِن
    /// تَوثيقِ PayPal نَصُّ الرِسالَةِ حَرفاً، ورَمزُ العَطَبِ
    /// <b>لَم يُقرَأ مِن مَصدَرٍ رَسميّ</b> ولا يُخترَع
    /// (القاعِدَة ١٦). فَالتَطبيعُ يَجعَل الشُرطَةَ مَسافَةً،
    /// فَتُلتَقَط الرِسالَةُ المُوَثَّقَةُ وأَيُّ رَمزٍ مِن
    /// عائِلَتِها بِقاعِدَةٍ واحِدَة.</para>
    /// </summary>
    [Theory]
    // الرِسالَةُ المُوَثَّقَةُ حَرفاً — وهي المَقيسَة.
    [InlineData("PayPal فَشِل إنشاءُ الاشتِراك: 422 — UNPROCESSABLE_ENTITY — Merchant not enabled for reference transaction", true)]
    [InlineData("Merchant not enabled for reference transaction", true)]
    // ورَمزٌ مِن عائِلَتِها لَو رَدَّت بِه — يُلتَقَط بِنَفسِ القاعِدَة.
    [InlineData("422 [REFERENCE_TRANSACTIONS_NOT_ENABLED]", true)]
    [InlineData("422 [NOT_ENABLED_FOR_REFERENCE_TRANSACTIONS]", true)]
    // وما لَيسَ مِنها لا يُلتَقَط — وإلّا صارَ كُلُّ خَطَإٍ «راسِل الدَعم».
    [InlineData("PayPal فَشِل إنشاءُ الاشتِراك: 422 — UNIT_AMOUNT_NOT_ALLOWED", false)]
    [InlineData("INVALID_REQUEST [MISSING_REQUIRED_PARAMETER]", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void TheEntitlementFailure_IsRecognisedByItsDocumentedWording(string? text, bool expected)
        => Assert.Equal(expected, PayPalFailure.IsReferenceTransactionsDisabled(text));

    /// <summary>
    /// <para><b>ورَمزُ الشاشَةِ مُغلَقٌ لِخَطَإ الاستِحقاقِ وَحدَه</b>،
    /// وما عَداهُ <b>يُعطي نَفسَه حَرفاً</b> — فَلا تُبتلَع رِسالَةُ
    /// PayPal تَحتَ رَمزٍ عامّ، ولا يُترجَم ما لا تُعرَف تَرجَمَتُه.</para>
    /// </summary>
    [Fact]
    public void TheScreenCode_TranslatesOnlyTheEntitlementFailure()
    {
        Assert.Equal(PayPalFailure.ReferenceTransactionsDisabled,
            PayPalFailure.ScreenCode("Merchant not enabled for reference transaction"));

        // ونَصُّ PayPal يَمُرُّ كَما هُوَ — «‏422» وَحدَها تُرسِل
        // المُشرِفَ يُخَمِّن، والرَمزُ يَقولُ لَه أَينَ يُصلِح.
        Assert.Equal("422 — UNIT_AMOUNT_NOT_ALLOWED",
            PayPalFailure.ScreenCode("422 — UNIT_AMOUNT_NOT_ALLOWED"));
        Assert.Equal("", PayPalFailure.ScreenCode(null));
    }

    /// <summary>
    /// <para><b>ونَصُّ القامُوسِ يَقولُ ما يُفعَل، لا ما وَقَعَ
    /// فَقَط.</b> «غَيرُ مُفَعَّل» وَحدَها تَترُك المالِكَ يُفَتِّشُ
    /// اللَوحَةَ عَن إعدادٍ **لا وُجودَ لَه** — والعِلاجُ الوَحيدُ
    /// المُوَثَّق مُراسَلَةُ دَعمِ PayPal بِطَلَبٍ مُسَمّى. فَيُقاسُ
    /// حُضورُ الطَلَبِ في النَصِّ نَفسِه.</para>
    /// </summary>
    [Fact]
    public void TheEntitlementMessage_CarriesTheRemedy_NotJustTheDiagnosis()
    {
        var text = ACommerce.Platform.I18n.LocaleCatalog.Find(
            "ar", "admin.tenant_plan.err_paypal_reference_transactions");

        Assert.False(string.IsNullOrWhiteSpace(text), "لا نَصَّ عَرَبيّاً لِخَطَإ الاستِحقاق.");
        Assert.Contains("Reference Transactions", text!, StringComparison.Ordinal);
        Assert.Contains("Billing Agreements", text!, StringComparison.Ordinal);
        Assert.Contains("دَعم", text!, StringComparison.Ordinal);

        // والشاشَةُ تُنادي المِفتاحَ بِرَمزِه المُغلَق — ومِفتاحٌ في
        // القامُوسِ لا تَقرَؤُه شاشَةٌ نَصٌّ لا يَراهُ أَحَد.
        var razor = Read(RazorFile);
        Assert.Contains("PayPalFailure.ReferenceTransactionsDisabled", razor, StringComparison.Ordinal);
        Assert.Contains("admin.tenant_plan.err_paypal_reference_transactions", razor, StringComparison.Ordinal);
    }

    /// <summary>
    /// <para><b>وفَشَلُ الاشتِراكِ يَحمِل رَمزَ PayPal ونَصَّه، لا
    /// رَقَمَ الحالَةِ وَحدَه.</b> «‏422» وَحدَها كانَت **تَبتَلِع**
    /// رِسالَةَ الاستِحقاقِ بِعَينِها — فَيَصِل الشاشَةَ رَقَمٌ لا
    /// يُصلِحُه أَحَد.</para>
    /// </summary>
    [Fact]
    public async Task ASubscriptionFailure_CarriesPayPalsCodeAndText_SoTheScreenCanNameIt()
    {
        var handler = new CatalogHandler()
            .ThenToken()
            .Then(HttpStatusCode.UnprocessableEntity,
                """{"name":"UNPROCESSABLE_ENTITY","message":"Merchant not enabled for reference transaction"}""");

        var result = await Gateway(handler).CreateSubscriptionAsync("P-1", "acme", "key-1");

        Assert.False(string.IsNullOrWhiteSpace(result.FailureReason));
        Assert.Contains("UNPROCESSABLE_ENTITY", result.FailureReason!, StringComparison.Ordinal);
        Assert.Contains("reference transaction", result.FailureReason!, StringComparison.OrdinalIgnoreCase);

        // وهُوَ ما يَصِل الشاشَةَ رَمزاً مُغلَقاً لَه نَصٌّ عَرَبيّ.
        Assert.Equal(PayPalFailure.ReferenceTransactionsDisabled,
            PayPalFailure.ScreenCode(result.FailureReason));
    }

    /// <summary>ورِسالَةُ فَشَلِ الاشتِراكِ <b>لا تَحمِل سِرّاً</b> —
    /// نَفسُ الاختِبارِ السالِبِ الَّذي يَحرُسُ مَسارَ الخُطَّة، لِأَنّ
    /// المَسارَ الجَديدَ صارَ يَعرِضُها على شاشَة.</summary>
    [Fact]
    public async Task ASubscriptionFailureMessage_NeverCarriesTheClientSecret()
    {
        var handler = new CatalogHandler()
            .ThenToken()
            .Then(HttpStatusCode.BadRequest, "{\"name\":\"INVALID_REQUEST\"}");

        var result = await Gateway(handler, Opts(secret: "xsecret-super"))
            .CreateSubscriptionAsync("P-1", "acme", "key-1");

        Assert.DoesNotContain("xsecret-super", result.FailureReason ?? "");
        Assert.DoesNotContain("AY-client", result.FailureReason ?? "");
    }

    // ═══ ٦. الشاشَةُ والنُقطَة — نَصّاً، لِأَنّ الغِيابَ هُوَ المَقيس ══

    private const string RazorFile =
        "libs/templates/ACommerce.Templates.Customer.Marketplace/Components/Pages/Admin/TenantPlanAdmin.razor";

    private const string EndpointsFile =
        "libs/templates/ACommerce.Templates.Customer.Marketplace/Billing/PayPalEndpoints.cs";

    /// <summary>المَسارُ في مَوضِعٍ واحِدٍ يَقرَؤُه النَموذَجُ
    /// والتَسجيلُ والاختِبار.</summary>
    public const string CreatePlanRoute = "/admin/tenants/{slug}/plan/paypal-plan";

    private static string Read(string relative)
        => File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, relative));

    /// <summary>
    /// <para><b>بِلا تَهيئَةٍ لا يُرسَم النَموذَجُ أَصلاً</b> — لا
    /// حَقلٌ يُملَأُ ثُمَّ يُقال «‏PayPal غَير مُهَيَّأ» بَعدَ النَقر
    /// (القاعِدَة ١٢). والقِياسُ نَصِّيّ: النَموذَجُ يَقَع
    /// <b>داخِلَ</b> شَرطِ التَهيئَة، ولا يَقَع خارِجَه مَرَّةً
    /// أُخرى.</para>
    /// </summary>
    [Fact]
    public void TheCreatePlanForm_IsRenderedOnlyInsideTheConfiguredGuard()
    {
        var razor = WriteEndpointGuardTests.StripMarkupComments(Read(RazorFile));

        var form = razor.IndexOf("plan/paypal-plan", StringComparison.Ordinal);
        Assert.True(form > 0, "أَداة عَمياء: لا نَموذَجَ إنشاءِ خُطَّةٍ في الشاشَة.");
        Assert.Equal(form, razor.LastIndexOf("plan/paypal-plan", StringComparison.Ordinal));

        var guard = razor.IndexOf("@if (PayPal.IsConfigured)", StringComparison.Ordinal);
        Assert.True(guard > 0 && guard < form,
            "نَموذَجُ إنشاءِ الخُطَّةِ خارِجَ شَرطِ التَهيئَة — حَقلٌ يُملَأ ثُمَّ يُقال «غَير مُهَيَّأ».");
    }

    /// <summary><b>وكُلُّ نَصٍّ يَراهُ المُشرِفُ مِن القامُوس</b>
    /// (القاعِدَة ١١): الشاشَةُ تُنادي المِفتاحَ، والقامُوسُ يَحمِلُه.
    /// ومِفتاحٌ بِلا قيمَةٍ يُرسَم كَمِفتاح.</summary>
    [Fact]
    public void EveryLabelOfTheCreatePlanForm_ComesFromTheLexicon()
    {
        var razor = Read(RazorFile);
        var keys = new[]
        {
            "admin.tenant_plan.paypal_plan_title", "admin.tenant_plan.paypal_plan_hint",
            "admin.tenant_plan.paypal_plan_name", "admin.tenant_plan.paypal_plan_price",
            "admin.tenant_plan.paypal_plan_period", "admin.tenant_plan.paypal_plan_monthly",
            "admin.tenant_plan.paypal_plan_yearly", "admin.tenant_plan.paypal_plan_currency",
            "admin.tenant_plan.paypal_plan_currency_hint", "admin.tenant_plan.paypal_plan_create",
            "admin.tenant_plan.paypal_plan_resource",
            "admin.tenant_plan.err_paypal_plan_slug", "admin.tenant_plan.err_paypal_plan_name",
            "admin.tenant_plan.err_paypal_plan_name_long", "admin.tenant_plan.err_paypal_plan_amount",
            "admin.tenant_plan.err_paypal_plan_currency", "admin.tenant_plan.err_paypal_plan_period",
        };

        foreach (var key in keys)
        {
            Assert.Contains(key, razor, StringComparison.Ordinal);
            Assert.False(string.IsNullOrWhiteSpace(
                ACommerce.Platform.I18n.LocaleCatalog.Find("ar", key)),
                $"المِفتاح «{key}» بِلا نَصٍّ عَرَبيّ في القامُوس.");
        }
    }

    /// <summary>
    /// <para><b>غَيرُ المُشرِفِ يُرَدُّ قَبلَ أَيِّ كِتابَة</b>
    /// (القاعِدَة ٦): التَخويلُ يَسبِق تَحَقُّقَ الحُقول، وإلّا صارَ
    /// خَطَأُ التَحَقُّقِ قِناعاً لِلثَغرَة. والقِياسُ بِتَرتيبِ
    /// المَواضِعِ في جِسمِ النُقطَةِ نَفسِه.</para>
    /// </summary>
    [Fact]
    public void TheCreatePlanEndpoint_GuardsBeforeItReadsAFieldOrWritesADocument()
    {
        var endpoint = Assert.Single(WriteEndpointGuardTests.AllMinimalApiEndpoints()
            .Where(e => e.Route == CreatePlanRoute).ToArray());

        var guard = endpoint.Body.IndexOf("PlatformAdminGuard.EvaluateAsync", StringComparison.Ordinal);
        Assert.True(guard >= 0, "نُقطَةُ إنشاءِ الخُطَّةِ بِلا حارِسِ مُشرِفِ المَنَصَّة.");

        // وكُلُّ مَوضِعٍ يُفحَص **يَجِب أَن يوجَد** — وإلّا مَرَّ
        // الفَحصُ لِأَنَّه لَم يَجِد شَيئاً (القاعِدَة ١٠: أَداةٌ
        // تَفحَص صِفراً أَداةٌ عَمياء).
        var after = new[]
        {
            "req.Form", "BindCatalogPlan", "SaveChangesAsync", "CreateCatalogPlanAsync"
        };

        foreach (var token in after)
        {
            var at = endpoint.Body.IndexOf(token, StringComparison.Ordinal);
            Assert.True(at >= 0, $"أَداة عَمياء: «{token}» غَير مَوجودٍ في جِسمِ النُقطَة.");
            Assert.True(at > guard, $"«{token}» يَسبِق الحارِسَ في جِسمِ النُقطَة.");
        }
    }

    /// <summary>والمَسارُ المُسَجَّلُ هُوَ الثابِتُ — نَفسُ حُجَّةِ
    /// <c>PayPalRouteTests</c> حَرفاً: عُنوانٌ يَنجَرِف يَعني نَموذَجاً
    /// يَذهَب إلى ‏404 بِصَمت.</summary>
    [Fact]
    public void TheRegisteredLiteral_MatchesTheRouteUnderTest()
        => Assert.Contains($"MapPost(\"{CreatePlanRoute}\"", Read(EndpointsFile));

    /// <summary>مَسارُ رابِطِ الدَفع — وهُوَ **المَوضِعُ الَّذي
    /// يُنتَظَر فيه خَطَأُ الاستِحقاق**.</summary>
    public const string PayLinkRoute = "/admin/tenants/{slug}/plan/paypal-link";

    /// <summary>
    /// <para><b>وفَشَلُ PayPal يُصَنَّفُ قَبلَ أَن يُبتلَع.</b> كانَ
    /// كُلُّ فَشَلٍ يَسقُط في <c>SaveApproveLink</c> فَيَرُدُّ
    /// <c>LinkRefused</c> = «راجِع سِجِلَّ الخادِم» — وهُوَ **بِالذاتِ**
    /// ما كانَ سَيَبتَلِعُ `Merchant not enabled for reference
    /// transaction`، وعِلاجُه رِسالَةٌ إلى دَعمِ PayPal لا سَطرٌ في
    /// لوغ.</para>
    ///
    /// <para><b>والقِياسُ بِالتَرتيبِ في جِسمِ النُقطَة</b>: التَصنيفُ
    /// يَسبِق الحِفظَ، وإلّا لَم يَبلُغهُ فَشَلٌ أَصلاً.</para>
    /// </summary>
    [Fact]
    public void ThePayLinkEndpoint_NamesPayPalsFailure_BeforeItFallsBackToRefused()
    {
        var endpoint = Assert.Single(WriteEndpointGuardTests.AllMinimalApiEndpoints()
            .Where(e => e.Route == PayLinkRoute).ToArray());

        var classify = endpoint.Body.IndexOf("PayPalFailure.ScreenCode", StringComparison.Ordinal);
        Assert.True(classify >= 0,
            "فَشَلُ PayPal يُبتلَع في «تَعَذَّرَ إنشاءُ رابِطِ الدَفع» — بِلا تَصنيفٍ يُسَمّيه.");

        var save = endpoint.Body.IndexOf("SaveApproveLink", StringComparison.Ordinal);
        Assert.True(save > classify,
            "التَصنيفُ بَعدَ الحِفظِ — أَي أَنّ الفَشَلَ يَسقُط في `LinkRefused` قَبلَ أَن يُقرَأ.");
    }
}
