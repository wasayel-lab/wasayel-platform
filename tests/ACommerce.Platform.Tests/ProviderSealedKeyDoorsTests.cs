using System.Security.Cryptography;
using ACommerce.Kit.Payments;
using ACommerce.Kit.Subscriptions;
using ACommerce.Platform.Providers;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using ACommerce.Templates.Customer.Marketplace.Services.Providers;
using Xunit;

namespace ACommerce.Platform.Tests;

// ═══ مِفتاحٌ مَختومٌ بِلا عَميلٍ يَطلُبُ بِه — لا بابَ يَنفَتِحُ بِه قَبلَ الخُطوَة 8 ═══
//
// **ما يُثَبَّتُ هُنا (‏2026-10-12، ‏ADR-032 الخُطوَة 7 و§٧‑ج)**: صاحِبُ مَتجَرٍ رَبَطَ
// مِفتاحَ البَوّابَةِ السِرِّيَّ وَحدَه (الخِزانَةُ خَتَمَته، ولا رابِطَ مُستَضافاً) —
// **ولا عَميلَ بَوّابَةٍ في الكودِ يَطلُبُ بِه** (الخُطوات 8–10، المَوجَةُ التالِيَة).
// فَلا يَجوزُ أَن يُفتَحَ بِه بابٌ يُفضي إلى لا شَيء (القاعِدَة ١٢):
//
//   • **البِطاقَةُ في إتمامِ الشِراء** (`CheckoutPaymentPolicy.CardOffered`، وقَد جَعَلَتها
//     المَوجَةُ الأولى «مُزَوِّدٌ يَستَطيعُ القَبضَ فِعلاً») — تَبقى مُغلَقَةً **في الإنتاجِ
//     وفي التَطويرِ مَعاً**، لا لِأَنّ مُزَوِّدَ النُسخَةِ يَعجِزُ فَحَسب.
//   • **الباقاتُ المَدفوعَة** — لا تَظهَرُ ولا يُحالُ زِرُّها إلى `PlanPay` بِلا رابِط.
//
// **ولِماذا يَبقى `CollectsMoney` رابِطاً مَملوءاً وَحدَه** — خِلافاً لِحَرفِ الصَفّ 7
// («أَو حَقلٌ سِرِّيٌّ مَختوم»): المَقيسُ في الصَفِّ الثاني أَدناه بِالعَكس — لَو عُدَّ المَختومُ
// لَقُلِبَ `PaymentProviderConfigured` بِلا قابِض، فَظَهَرَت الباقاتُ المَدفوعَةُ وأَحالَ
// زِرُّها إلى صَفحَةٍ لا رابِطَ فيها («غَيرُ مُتاح»)، وعُرِضَت البِطاقَةُ في التَطويرِ عَلى
// المُحاكي لا عَلى مِفتاحِ التاجِر. **يَنقَلِبُ الصَفُّ الأَوَّلُ مَعَ عَميلِ البَوّابَة**.
public class ProviderSealedKeyDoorsTests
{
    private const string Key = "sk_live_9f3b7c2d41aa58e6";

    /// <summary>رَبطٌ كَما تَكتُبُه نُقطَةُ الرَبطِ لِمِفتاحٍ سِرِّيٍّ وَحدَه — مِن
    /// <c>ProviderBindSurface.FromForm</c> نَفسِها لا مَبنِيّاً بِاليَد. ويَقرَؤُه
    /// <c>ProviderVaultDeploymentTests</c> سَبَباً لِتَنبيهِ بَوّابَةِ الإقلاع.</summary>
    internal static TenantProviderSet SealedOnly()
    {
        var vault = ProviderVault.FromKek(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        var read = ProviderBindSurface.FromForm("moyasar_api", _ => Key, "t", vault);
        Assert.Null(read.RefusalCode);
        Assert.True(Assert.Single(read.Values).Value.IsSealed);

        return TenantProviderSet.FromDocuments("t", new[]
        {
            new TenantProviderBinding
            {
                Id = read.Capability,
                Slug = read.Capability,
                TenantSlug = "t",
                ProviderSlug = read.ProviderSlug,
                Status = TenantProviderBinding.StatusActive,
                Values = new Dictionary<string, StoredValue>(read.Values, StringComparer.Ordinal),
                BoundBy = "owner",
                BoundAt = new DateTime(2026, 10, 12, 0, 0, 0, DateTimeKind.Utc),
            },
        });
    }

    private static readonly Plan Paid = new() { Id = "pro", Name = "pro", Price = 99m, IsActive = true };
    private static readonly Plan Free = new() { Id = "free", Name = "free", Price = 0m, IsActive = true };

    [Fact]
    public void A_sealed_key_alone_collects_nothing_yet_so_no_card_and_no_paid_plan_opens()
    {
        var set = SealedOnly();

        // الرَبطُ قائِمٌ ومَحلول — مُزَوِّدٌ مَعروفٌ لِقُدرَتِه، لا لَقطَةُ المَنَصَّة.
        Assert.NotSame(TenantProviderSet.Platform, set);
        var p = set.For(ProviderCapabilities.Payments);
        Assert.NotNull(p);
        Assert.Equal("moyasar_api", p!.Slug);

        // ولا يَقبِضُ بَعد: لا رابِطَ يُنقَر، ولا عَميلَ يَطلُبُ بِالمِفتاح.
        Assert.Null(p.PaymentLink);
        Assert.False(p.CollectsMoney);
        Assert.False(set.CollectsMoney);

        // ‏(١) البِطاقَة — مُغلَقَةٌ في الإنتاجِ (الفَشَلُ المُغلَق) **وفي التَطويرِ** (المُحاكي).
        var production = PaymentChargeSurface.CanCharge(new UnavailablePaymentProvider());
        var development = PaymentChargeSurface.CanCharge(new MockPaymentProvider());
        Assert.False(production);
        Assert.True(development);
        Assert.False(CheckoutPaymentPolicy.CardOffered(set.CollectsMoney, production));
        Assert.False(CheckoutPaymentPolicy.CardOffered(set.CollectsMoney, development));

        foreach (var canCharge in new[] { production, development })
        {
            var (method, refusal) = CheckoutPaymentPolicy.Decide("card", set.CollectsMoney, canCharge);
            Assert.Equal(CheckoutPayMethods.CashOnDelivery, method);
            Assert.Equal(CheckoutPayMethods.CardUnavailable, refusal);
            Assert.False(CheckoutPaymentPolicy.CallsProvider(method));
        }

        // ‏(٢) الباقاتُ المَدفوعَة — لا تُعرَض، وطَلَبُها مُباشَرَةً يُرَدُّ بِرَمزِه لا يُحال.
        var visible = PlanPurchasePolicy.Visible(new[] { Free, Paid }, set.CollectsMoney);
        Assert.Equal(new[] { "free" }, visible.Select(x => x.Id).ToArray());
        var (route, code) = PlanPurchasePolicy.Decide(Paid, set.CollectsMoney);
        Assert.Equal(PlanPurchasePolicy.PlanPurchaseRoute.Refuse, route);
        Assert.Equal(PlanPurchasePolicy.PaidUnavailable, code);
    }

    /// <summary><b>المَقيسُ بِالعَكس — لِماذا لا يُعَدُّ المَختومُ قابِضاً بَعد</b>: لَو قالَ
    /// <c>CollectsMoney</c> «نَعَم» لِهذا الرَبطِ (حَرفُ الصَفِّ 7) لَأُحيلَت الباقَةُ المَدفوعَةُ
    /// إلى «ادفَع عِندَ مُزَوِّدِ التاجِر» — وصَفحَتُه (<c>PlanPay.razor</c>) لا تُصَيِّرُ إلّا
    /// <c>PaymentLink</c>، وهُوَ فارِغٌ هُنا؛ ولَعُرِضَت البِطاقَةُ في التَطويرِ عَلى المُحاكي
    /// لا عَلى مِفتاحِ التاجِر. بابانِ إلى لا شَيء.</summary>
    [Fact]
    public void Were_a_sealed_key_counted_as_collecting_the_paid_plan_would_lead_to_a_page_with_no_link()
    {
        var set = SealedOnly();
        const bool countedAsCollecting = true;   // حَرفُ الصَفّ 7 — ما لَم يُنَفَّذ

        Assert.Equal(PlanPurchasePolicy.PlanPurchaseRoute.PayAtProvider,
            PlanPurchasePolicy.Decide(Paid, countedAsCollecting).Route);
        Assert.Contains(Paid, PlanPurchasePolicy.Visible(new[] { Free, Paid }, countedAsCollecting));
        Assert.Null(set.For(ProviderCapabilities.Payments)!.PaymentLink);

        // وصَفحَةُ الدَفعِ تَقرَأُ الرابِطَ وَحدَه — فَبِلا رابِطٍ تَرسُمُ «غَيرُ مُتاح».
        var planPay = File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, "libs", "templates",
            "ACommerce.Templates.Customer.Marketplace", "Components", "Pages", "PlanPay.razor"));
        Assert.Contains("link = set.For(ProviderCapabilities.Payments)?.PaymentLink;", planPay, StringComparison.Ordinal);
        Assert.Contains("else if (link is null)", planPay, StringComparison.Ordinal);

        Assert.True(CheckoutPaymentPolicy.CardOffered(countedAsCollecting,
            PaymentChargeSurface.CanCharge(new MockPaymentProvider())));
    }

    /// <summary><b>والنَصُّ الَّذي يَقولُه الاستوديو لِمَتجَرٍ لا يَقبِضُ يَصدُقُ في الحالَين</b>:
    /// كانَ «ما دامَ بِلا مُزَوِّدِ دَفعٍ مَربوط» — صادِقاً يَومَ كانَ الرَبطُ رابِطاً وَحدَه، وكاذِباً
    /// لِمَتجَرٍ رَبَطَ مِفتاحاً بِلا رابِط (مُزَوِّدٌ مَربوطٌ ولا يَقبِض). فَصارَ «بِلا رابِطِ دَفعٍ
    /// مَربوط» — ما تَحتاجُه الباقاتُ فِعلاً (<c>PlanPay</c> تُصَيِّرُ الرابِطَ وَحدَه).</summary>
    [Fact]
    public void The_studio_plans_note_names_the_missing_payment_link_not_a_missing_provider()
    {
        var ar = File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot,
            "libs", "core", "ACommerce.Platform.I18n", "Locales", "ar.json"));
        Assert.Contains("\"studio.app_plans.no_collector\": \"الباقاتُ بِسِعرٍ لا تُعرَضُ لِزُوّارِ مَتجَرِكَ ما دامَ بِلا رابِطِ دَفعٍ مَربوط.",
            ar, StringComparison.Ordinal);
        Assert.DoesNotContain("ما دامَ بِلا مُزَوِّدِ دَفعٍ مَربوط", ar, StringComparison.Ordinal);

        // والمُلاحَظَةُ تُرسَمُ بِالعَلَمِ نَفسِه الَّذي يُخفي الباقات — فَمَتجَرٌ رَبَطَ مِفتاحاً وَحدَه يَراها.
        var page = File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, "libs", "templates",
            "ACommerce.Templates.Customer.Marketplace", "Components", "Pages", "StudioAppPlans.razor"));
        Assert.Contains("@if (!tenant.PaymentProviderConfigured)", page, StringComparison.Ordinal);
        Assert.Contains("studio.app_plans.no_collector", page, StringComparison.Ordinal);
        Assert.False(SealedOnly().CollectsMoney);
    }
}
