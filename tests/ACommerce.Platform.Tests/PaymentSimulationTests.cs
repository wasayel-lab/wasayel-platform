using ACommerce.Kit.Payments;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ACommerce.Platform.Tests;

// ═══ وَضعُ التَجرِبَة — يُطلَب ولا يَقَعُ بِالغِياب ═══════════════════
//
// **الطَلَب**: مُزَوِّدُ دَفعٍ مُعلَّمٌ يُختارُ صَراحَةً، يُظهِرُ
// لِلمُستَخدِمِ أَنَّه تَجرِبَة، **ولا يُنشِئُ فاتورَةً تَبدو
// حَقيقِيَّة**، **ولا يُنتَقى صامِتاً عِندَ غِيابِ التَهيئَة**.
//
// **والخَطَرُ بِعَينِه هُوَ الشَرطُ الأَخير**، فَلَه في هذا المِلَفِّ
// **خَمسَةُ فُحوص** لا واحِد: جَدوَلُ القَرارِ بِطَرَفَيه، والأَثَرُ
// الفِعليُّ مِن وِعاءِ خِدَماتٍ حَقيقيّ، والحارِسُ المَعكوسُ
// بِطَرَفَيه، ونِداؤُه في `Program.cs`.
//
// **والحُرّاسُ القائِمَةُ لا تُمَسّ** (ADR-014):
// `AssertNoStubsOutsideDevelopment` بِحَرفِه، و`MockPaymentProvider`
// يَحتَفِظُ بِعَلامَتِه، و`PaymentProviderSelectionTests` يَبقى أَخضَرَ
// **بِلا تَعديلِ حَرف** — وهذا الأَخيرُ مَقيسٌ هُنا صَراحَةً بِنِداءِ
// الحِملِ القَديم.
public class PaymentSimulationTests
{
    private static string RepoRoot => ThemeZeroEquivalenceTests.RepoRoot;

    private static string Source(params string[] parts)
    {
        var path = Path.Combine(RepoRoot, Path.Combine(parts));
        Assert.True(File.Exists(path), $"مَصدَرٌ مَفقود: {path} — الأَداةُ عَمياء.");
        var text = File.ReadAllText(path);
        Assert.True(text.Length > 500, $"أَداةٌ عَمياء: {path} طولُه {text.Length} مِحرَفاً.");
        return text;
    }

    private const string Sim = SimulatedPaymentProvider.ConfiguredValue;

    // ═══ ١) الجَدوَل — والخَطَرُ في صَفِّه الأَخير ═════════════════════

    /// <summary><b>القيمَةُ الصَريحَةُ تُنتِجُ التَجرِبَةَ في أَيِّ
    /// بيئَة</b> — لِأَنَّها اختِيارٌ لا سُقوط.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void An_explicit_simulation_key_selects_simulation_in_any_environment(bool isDevelopment)
        => Assert.Equal(PaymentProviderChoice.Simulation,
            PaymentProviderSelection.Decide(isDevelopment, Sim));

    /// <summary>
    /// <para><b>وهذا هُوَ الاختِبارُ الَّذي يَحمَرُّ لَو صارَ وَضعُ
    /// التَجرِبَةِ قابِلاً لِلانتِقاءِ صامِتاً في الإنتاج.</b> كُلُّ
    /// مُدخَلٍ لَيسَ القيمَةَ الصَريحَةَ — غِيابٌ، أَو فَراغٌ، أَو
    /// <c>"mock"</c> مَكتوبَةٌ بِاليَد، أَو خَطَأُ إملاءٍ في
    /// <c>"simulate"</c> — يُعطي <b>الفَشَلَ المُغلَقَ</b> خارِجَ
    /// التَطوير.</para>
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("mock")]
    [InlineData("simulate")]
    [InlineData("sim")]
    [InlineData("true")]
    [InlineData("Simulation!")]
    public void Nothing_but_the_written_value_ever_produces_simulation_in_production(string? configured)
        => Assert.Equal(PaymentProviderChoice.Unavailable,
            PaymentProviderSelection.Decide(isDevelopment: false, configured));

    /// <summary><b>والتَطويرُ يَبقى على المُحاكي كَما كان</b> — تَكافُؤٌ
    /// صِفريّ.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("mock")]
    public void Development_without_the_key_still_resolves_the_mock(string? configured)
        => Assert.Equal(PaymentProviderChoice.Mock,
            PaymentProviderSelection.Decide(isDevelopment: true, configured));

    /// <summary><b>والحِملُ القَديمُ يُجيبُ كَما كانَ حَرفاً</b> —
    /// فَجَدوَلُ <c>PaymentProviderSelectionTests</c> يَبقى أَخضَرَ بِلا
    /// تَعديلِ حَرف (القاعِدَة ٣).</summary>
    [Fact]
    public void The_single_argument_overload_answers_exactly_as_before()
    {
        Assert.Equal(PaymentProviderChoice.Mock,        PaymentProviderSelection.Decide(true));
        Assert.Equal(PaymentProviderChoice.Unavailable, PaymentProviderSelection.Decide(false));
    }

    // ═══ ٢) الأَثَر — ماذا يُسَجَّلُ فِعلاً ════════════════════════════

    [Fact]
    public void The_explicit_key_registers_the_simulated_provider_and_nothing_else_does()
    {
        Assert.IsType<SimulatedPaymentProvider>(Resolve(false, Sim));
        Assert.IsType<SimulatedPaymentProvider>(Resolve(true,  Sim));

        Assert.IsType<UnavailablePaymentProvider>(Resolve(false, null));
        Assert.IsType<UnavailablePaymentProvider>(Resolve(false, "mock"));
        Assert.IsType<MockPaymentProvider>(Resolve(true, null));
    }

    private static IPaymentProvider Resolve(bool isDevelopment, string? configured)
    {
        var services = new ServiceCollection();
        services.AddPaymentProvider(isDevelopment, configured);
        return services.BuildServiceProvider().GetRequiredService<IPaymentProvider>();
    }

    // ═══ ٣) العَلامَتان — ودَمجُهُما كانَ سَيَكسِرُ الحارِسَ القائِم ═══

    /// <summary><b>مُزَوِّدُ التَجرِبَةِ لا يَحمِلُ عَلامَةَ المُحاكي
    /// التَطويريّ</b> — ولَو حَمَلَها لَأَفشَلَ الإقلاعَ في الإنتاجِ
    /// على مَن اختارَه عَمداً. نَفسُ حُجَّةِ
    /// <c>UnavailablePaymentProvider</c> حَرفاً.</summary>
    [Fact]
    public void The_simulated_provider_does_not_carry_the_development_stub_marker()
    {
        Assert.False(typeof(IDevelopmentStubPaymentProvider)
            .IsAssignableFrom(typeof(SimulatedPaymentProvider)));
        Assert.True(typeof(ISimulatedPaymentProvider)
            .IsAssignableFrom(typeof(SimulatedPaymentProvider)));
    }

    /// <summary><b>والمُحاكي لا يَحمِلُ عَلامَةَ التَجرِبَة</b> —
    /// وإلّا مَرَّ الحارِسُ المَعكوسُ على مُحاكٍ تَسَرَّب.</summary>
    [Fact]
    public void The_development_mock_does_not_carry_the_simulation_marker()
    {
        Assert.False(typeof(ISimulatedPaymentProvider)
            .IsAssignableFrom(typeof(MockPaymentProvider)));
        Assert.False(typeof(ISimulatedPaymentProvider)
            .IsAssignableFrom(typeof(UnavailablePaymentProvider)));
    }

    /// <summary><b>والحارِسُ القائِمُ لا يَشتَكي مِن التَجرِبَة</b> —
    /// فَإقلاعُ الإنتاجِ بِـ<c>simulation</c> يَمُرّ.</summary>
    [Fact]
    public void The_existing_stub_guard_stays_silent_about_the_simulated_provider()
        => PaymentProviderSelection.AssertNoStubsOutsideDevelopment(
            isDevelopment: false,
            new[] { PaymentProviderSelection.Describe(new SimulatedPaymentProvider())! });

    // ═══ ٤) الحارِسُ المَعكوس — بِطَرَفَيه ═════════════════════════════

    [Fact]
    public void A_simulated_provider_resolved_without_the_written_key_stops_the_boot()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            PaymentProviderSelection.AssertSimulationIsExplicit(
                configured: null,
                new[] { PaymentProviderSelection.Describe(new SimulatedPaymentProvider())! }));

        Assert.Contains(Sim, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_same_provider_with_the_written_key_passes()
        => PaymentProviderSelection.AssertSimulationIsExplicit(
            configured: Sim,
            new[] { PaymentProviderSelection.Describe(new SimulatedPaymentProvider())! });

    [Fact]
    public void A_non_simulated_provider_never_trips_the_reverse_guard()
    {
        PaymentProviderSelection.AssertSimulationIsExplicit(
            configured: null,
            new[] { PaymentProviderSelection.Describe(new UnavailablePaymentProvider())! });

        PaymentProviderSelection.AssertSimulationIsExplicit(
            configured: null,
            new[] { PaymentProviderSelection.Describe(new MockPaymentProvider())! });

        PaymentProviderSelection.AssertSimulationIsExplicit(
            configured: null, Array.Empty<RegisteredPaymentProvider>());
    }

    /// <summary><b>والحارِسانِ يُنادَيانِ مَعاً في الإقلاع</b> —
    /// وحارِسٌ بِلا مُنادٍ لا يُقاس (القاعِدَة ٢).</summary>
    [Fact]
    public void Boot_calls_both_guards_side_by_side()
    {
        var program = Source("apps", "V1.App", "Program.cs");

        Assert.Contains("PaymentProviderSelection.AssertNoStubsOutsideDevelopment",
            program, StringComparison.Ordinal);
        Assert.Contains("PaymentProviderSelection.AssertSimulationIsExplicit",
            program, StringComparison.Ordinal);
        Assert.Contains(PaymentProviderSelection.ProviderKey, program, StringComparison.Ordinal);
    }

    // ═══ ٥) لا فاتورَةَ تَبدو حَقيقِيَّة ═══════════════════════════════

    /// <summary><b>لا فاتورَة، ولا رَقمَ ضَريبيّ، ولا رابِطَ
    /// مُستَنَد.</b> نَفسُ جَوابِ <c>Mock</c> بَعدَ ‏ADR-014 §٢-د
    /// و<c>Noon</c> و<c>Unavailable</c>.</summary>
    [Fact]
    public async Task The_simulated_provider_never_issues_an_invoice()
    {
        var p = new SimulatedPaymentProvider();
        var auth = await p.AuthorizeAsync(new PaymentRequest(100m, "د", "u", "+966500000000"), "k");

        Assert.Equal(PaymentStatus.Authorized, auth.Status);
        Assert.Null(await p.GetInvoiceAsync(auth.PaymentId));
    }

    /// <summary><b>ولا رابِطَ إيصالٍ يَفتَحُ لا شَيء</b> — و<c>Mock</c>
    /// يُرجِعُ <c>/api/payments/receipt/{id}</c> ولا نُقطَةَ خَلفَه.</summary>
    [Fact]
    public async Task The_simulated_provider_returns_no_receipt_url()
    {
        var p = new SimulatedPaymentProvider();
        var auth = await p.AuthorizeAsync(new PaymentRequest(100m, "د", "u", "+966500000000"), "k");
        Assert.Null(auth.ReceiptUrl);
    }

    // ═══ ٦) المَرجِعُ يُعلِنُ نَفسَه في القاعِدَة ══════════════════════

    [Fact]
    public async Task Every_reference_it_writes_declares_that_it_is_a_simulation()
    {
        var p = new SimulatedPaymentProvider();

        var auth = await p.AuthorizeAsync(new PaymentRequest(50m, "د", "u", "+966500000000"), "k1");
        Assert.StartsWith(SimulatedPaymentProvider.PaymentIdPrefix, auth.PaymentId, StringComparison.Ordinal);

        var sub = await p.CreateSubscriptionAsync(new("u", "silver", 10m, "+966500000000"), "k2");
        Assert.StartsWith(SimulatedPaymentProvider.SubscriptionIdPrefix, sub.SubscriptionId, StringComparison.Ordinal);

        Assert.Equal(Sim, p.ProviderName);
        Assert.NotEqual("mock", p.ProviderName);
    }

    /// <summary>ومَنعُ التَكرارِ يَعمَلُ — نَفسُ المِفتاحِ نَفسُ
    /// النَتيجَة.</summary>
    [Fact]
    public async Task The_same_idempotency_key_yields_the_same_payment()
    {
        var p = new SimulatedPaymentProvider();
        var a = await p.AuthorizeAsync(new PaymentRequest(50m, "د", "u", "+966500000000"), "same");
        var b = await p.AuthorizeAsync(new PaymentRequest(50m, "د", "u", "+966500000000"), "same");
        Assert.Equal(a.PaymentId, b.PaymentId);
    }

    /// <summary>ومَبلَغٌ غَيرُ مُوجَبٍ يُرَدُّ — التَجرِبَةُ تُحاكي
    /// مُزَوِّداً لا تُلغي حِسابَه.</summary>
    [Fact]
    public async Task A_non_positive_amount_is_refused_even_in_simulation()
    {
        var p = new SimulatedPaymentProvider();
        var r = await p.AuthorizeAsync(new PaymentRequest(0m, "د", "u", "+966500000000"), "k");
        Assert.Equal(PaymentStatus.Failed, r.Status);
    }

    // ═══ ٧) العَلامَةُ المَرئِيَّةُ — والشاشَةُ والنُقطَةُ مُسنَدٌ واحِد ══

    /// <summary><b>الشاشَةُ والنُقطَةُ تَقرَآنِ الدالَّةَ نَفسَها</b> —
    /// فَلا تَعرِضُ الشاشَةُ حَقيقِيّاً ما سَيُحاكيهِ المُزَوِّد.</summary>
    [Fact]
    public void The_screen_and_the_endpoint_read_the_same_predicate()
    {
        var razor = Source("libs", "templates", "ACommerce.Templates.Customer.Marketplace",
            "Components", "Pages", "CheckoutPage.razor");
        var endpoints = Source("libs", "templates", "ACommerce.Templates.Customer.Marketplace",
            "MarketplaceTemplateExtensions.cs");

        Assert.Contains(nameof(PaymentSimulationSurface), razor, StringComparison.Ordinal);
        Assert.Contains(nameof(SimulatedPaymentProvider.ModeRefKey), endpoints, StringComparison.Ordinal);
    }

    /// <summary><b>والعَلامَةُ نَصُّها مِن القامُوس لا مِن الكود</b>
    /// (القاعِدَة ١١).</summary>
    [Fact]
    public void The_visible_badge_reads_its_text_from_the_lexicon()
    {
        var lexicon = ACommerce.Platform.I18n.LocaleCatalog.Lexicon.ToHashSet(StringComparer.Ordinal);

        foreach (var key in new[]
                 {
                     "checkout.payment.simulation_badge",
                     "checkout.payment.simulation_hint",
                 })
        {
            Assert.True(lexicon.Contains(key), $"مِفتاحٌ خارِجَ المَعجَم: {key}");
            Assert.False(ACommerce.Platform.I18n.LocaleCatalog.IsPlaceholderKey("ar", key),
                $"قيمَةٌ نائِبَة: {key}");
        }
    }

    // ═══ ٨) ماسِحٌ نَصِّيّ — لا رَقمَ ضَريبِيّاً ولا رابِطَ فاتورَة ════

    /// <summary><b>ولا مُستَنَدَ يُشبِهُ الحَقيقيّ في مِلَفِّ
    /// التَجرِبَة</b>: لا <c>SellerVatNumber</c> بِقيمَة، ولا
    /// <c>PdfUrl</c>، ولا بِناءُ <c>Invoice</c> إطلاقاً.</summary>
    [Fact]
    public void The_simulation_source_builds_no_invoice_like_document()
    {
        var text = Source("libs", "kits", "Payments", "ACommerce.Kit.Payments.Core",
            "SimulatedPaymentProvider.cs");

        foreach (var forbidden in new[] { "new Invoice(", "PdfUrl", "SellerVatNumber" })
            Assert.False(text.Contains(forbidden, StringComparison.Ordinal),
                $"مِلَفُّ التَجرِبَةِ يَبني مُستَنَداً يُشبِهُ الفاتورَة: «{forbidden}».");
    }
}
