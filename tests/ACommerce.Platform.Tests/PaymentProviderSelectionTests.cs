using ACommerce.Kit.Payments;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ACommerce.Platform.Tests;

// ═══ حَدُّ مُزَوِّدِ الدَفع — جَدوَلٌ بِطَرَفَيه ═════════════════════
//
// نَفسُ شَكلِ `AuthChannelSelectionTests` حَرفاً، ولِنَفسِ السَبَب:
// **الحَدُّ الَّذي لا يُقاس آلِيّاً يَنهار** (القاعِدَة ٢). ولِكُلّ
// حالَةٍ مُوجِبٌ وسالِب (القاعِدَة ٤).
public class PaymentProviderSelectionTests
{
    // ─── ١. القَرار ─────────────────────────────────────────────────

    [Theory]
    [InlineData(true,  PaymentProviderChoice.Mock)]
    [InlineData(false, PaymentProviderChoice.Unavailable)]
    public void Decide_is_a_table_not_a_scattered_condition(
        bool isDevelopment, PaymentProviderChoice expected)
        => Assert.Equal(expected, PaymentProviderSelection.Decide(isDevelopment));

    // ─── ٢. الأَثَر: ماذا يُسَجَّل فِعلاً ────────────────────────────

    [Fact]
    public void Development_resolves_the_mock_and_production_resolves_the_closed_failure()
    {
        Assert.IsType<MockPaymentProvider>(Resolve(isDevelopment: true));
        Assert.IsType<UnavailablePaymentProvider>(Resolve(isDevelopment: false));
    }

    private static IPaymentProvider Resolve(bool isDevelopment)
    {
        var services = new ServiceCollection();
        services.AddPaymentProvider(isDevelopment);
        return services.BuildServiceProvider().GetRequiredService<IPaymentProvider>();
    }

    // ─── ٣. العَلامَة — سُقوطُها يُسكِتُ الحارِس ──────────────────────

    [Fact]
    public void The_mock_carries_the_development_stub_marker()
        => Assert.True(typeof(IDevelopmentStubPaymentProvider)
            .IsAssignableFrom(typeof(MockPaymentProvider)));

    /// <summary>والفَشَلُ المُغلَقُ <b>لا</b> يَحمِلُها — وإلّا أَفشَلَ
    /// الإقلاعَ في الإنتاجِ على المَضبوط.</summary>
    [Fact]
    public void The_closed_failure_provider_does_not_carry_the_marker()
        => Assert.False(typeof(IDevelopmentStubPaymentProvider)
            .IsAssignableFrom(typeof(UnavailablePaymentProvider)));

    // ─── ٤. الحارِس — بِطَرَفَيه ──────────────────────────────────────

    [Fact]
    public void A_stub_registered_outside_development_stops_the_boot()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            PaymentProviderSelection.AssertNoStubsOutsideDevelopment(
                isDevelopment: false,
                new[] { new RegisteredPaymentProvider("mock", true) }));

        Assert.Contains("mock", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_same_stub_inside_development_is_allowed()
        => PaymentProviderSelection.AssertNoStubsOutsideDevelopment(
            isDevelopment: true,
            new[] { new RegisteredPaymentProvider("mock", true) });

    [Fact]
    public void A_non_stub_provider_outside_development_passes()
        => PaymentProviderSelection.AssertNoStubsOutsideDevelopment(
            isDevelopment: false,
            new[] { new RegisteredPaymentProvider("unavailable", false) });

    /// <summary>ووِعاءٌ بِلا مُزَوِّدٍ لا يَرمي — الحارِسُ يَمنَع
    /// المُحاكيَ، لا يَفرِض وُجودَ مُزَوِّد.</summary>
    [Fact]
    public void No_registered_provider_at_all_is_not_a_violation()
    {
        Assert.Null(PaymentProviderSelection.Describe(null));
        PaymentProviderSelection.AssertNoStubsOutsideDevelopment(
            isDevelopment: false, Array.Empty<RegisteredPaymentProvider>());
    }

    // ─── ٥. الفَشَلُ المُغلَق: كُلُّ نِداءٍ يُرَدُّ بِسَبَب ──────────

    [Fact]
    public async Task Every_call_on_the_closed_failure_provider_fails_with_a_stated_reason()
    {
        var p = new UnavailablePaymentProvider();

        var auth = await p.AuthorizeAsync(
            new PaymentRequest(100m, "د", "u", "+966500000000"), "k");
        Assert.Equal(PaymentStatus.Failed, auth.Status);
        Assert.Equal(UnavailablePaymentProvider.Reason, auth.FailureReason);

        var sub = await p.CreateSubscriptionAsync(new("u", "scale", 999m, "+966500000000"), "k");
        Assert.False(sub.IsActive);
        Assert.Equal(UnavailablePaymentProvider.Reason, sub.FailureReason);

        Assert.Equal(PaymentStatus.Failed, (await p.CaptureAsync("pay_1")).Status);
        Assert.Equal(PaymentStatus.Failed, (await p.RefundAsync("pay_1", 1m, "س")).Status);
        Assert.False(await p.CancelSubscriptionAsync("sub_1"));
        Assert.Null(await p.GetInvoiceAsync("pay_1"));
    }
}
