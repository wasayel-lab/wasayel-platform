using Xunit;

namespace ACommerce.Platform.Tests;

// ═══ عَلامَةُ التَجرِبَةِ تَبقى على الطَلَبِ بَعدَ النَقرَة ════════════
//
// **الفَجوَةُ الَّتي سَدَّها هذا المِلَفّ (‏2026-08-30)**: ‏ADR-025 وَعَدَ
// بِأَنّ وَضعَ التَجرِبَةِ «يُعلَنُ حَيثُ يُنقَر»، وأَوفى —
// `CheckoutPage.razor` تَرسُمُ الشارَةَ على خِيارِ البِطاقَةِ وفي
// المُلَخَّصِ قَبلَ زِرِّ الإرسال. لكِنَّ **صَفحَةَ الطَلَبِ نَفسَها**
// (`MyDealDetail.razor`) كانَت صامِتَةً تَماماً: لا طَريقَةَ دَفعٍ، ولا
// حالَة، ولا عَلامَةَ تَجرِبَة. فَالعائِدُ إلى طَلَبِه بَعدَ ساعَةٍ
// يَرى مَبلَغاً وطَرَفاً **بِلا أَيِّ مُذَكِّرٍ** بِأَنَّ الدَفعَ لَم
// يَقَع — وهُوَ بِالضَبطِ المَوضِعُ الَّذي يُراجَعُ فيه الطَلَبُ لاحِقاً.
//
// **والحَدُّ الَّذي يَحرُسُه هذا المِلَفُّ هُوَ حَدُّ صاحِبِ المَشروع
// نَفسُه**: «لا يُخطَأُ بِه عَنِ الحَقيقيّ». وشارَةٌ تَظهَرُ قَبلَ
// النَقرَةِ وتَختَفي بَعدَها تَفي بِنِصفِ الشَرط.
//
// **ولِماذا فَحصُ نَصٍّ هُنا وقَد كانَ يُمكِنُ تَصييرُ المُكَوِّن**:
// المِفتاحُ والقيمَةُ **ثابِتانِ مُتَرجَمان** (`ModeRefKey`,
// `ConfiguredValue`) لا سَلاسِلُ مَكتوبَة — فَانجِرافُ الاسمِ مُستَحيلٌ
// بِالبِناءِ أَصلاً. الخَطَرُ الباقي واحِدٌ فَقَط: أَن تُحذَفَ الكُتلَةُ
// مِنَ الصَفحَة. وذلكَ ما يُقاسُ هُنا، ولا يُدَّعى أَكثَرُ مِنه.
public class OrderSimulationBadgeTests
{
    private static string RepoRoot => ThemeZeroEquivalenceTests.RepoRoot;

    private static string PageText(params string[] parts)
        => File.ReadAllText(Path.Combine(new[] { RepoRoot }.Concat(parts).ToArray()));

    /// <summary>
    /// <para><b>صَفحَةُ الطَلَبِ تَقرَأُ المَرجِعَ الَّذي تَكتُبُهُ
    /// نُقطَةُ الشِراء</b> — طَرَفا الجِسرِ يُقاسانِ مَعاً، فَلا يُكتَبُ
    /// مَرجِعٌ لا يَقرَؤُهُ أَحَد ولا تُقرَأُ صَفحَةٌ مَرجِعاً لا
    /// يُكتَب.</para>
    /// </summary>
    [Fact]
    public void The_order_page_reads_the_same_payment_mode_reference_the_checkout_writes()
    {
        var endpoint = PageText("libs", "templates",
            "ACommerce.Templates.Customer.Marketplace", "MarketplaceTemplateExtensions.cs");
        var orderPage = PageText("libs", "templates",
            "ACommerce.Templates.Customer.Marketplace", "Components", "Pages", "MyDealDetail.razor");

        Assert.True(endpoint.Length > 5000, "أَداةٌ عَمياء: مِلَفُّ النُقاطِ لَم يُقرَأ.");
        Assert.True(orderPage.Length > 1000, "أَداةٌ عَمياء: `MyDealDetail.razor` لَم يُقرَأ.");

        // الكاتِب: نُقطَةُ الشِراء.
        Assert.Contains("SimulatedPaymentProvider.ModeRefKey", endpoint, StringComparison.Ordinal);
        Assert.Contains("SimulatedPaymentProvider.ConfiguredValue", endpoint, StringComparison.Ordinal);

        // والقارِئ: صَفحَةُ الطَلَب — بِالثابِتِ نَفسِه لا بِسِلسِلَةٍ
        // مَكتوبَة، وإلّا انجَرَفَ الطَرَفانِ بِتَعديلِ حَرف.
        Assert.Contains("SimulatedPaymentProvider.ModeRefKey", orderPage, StringComparison.Ordinal);
        Assert.Contains("SimulatedPaymentProvider.ConfiguredValue", orderPage, StringComparison.Ordinal);

        Assert.False(orderPage.Contains("\"payment_mode\"", StringComparison.Ordinal),
            "صَفحَةُ الطَلَبِ تَكتُبُ المِفتاحَ سِلسِلَةً — والمَعجَمُ المُغلَقُ لَه تَعريفٌ واحِد.");
    }

    /// <summary>
    /// <para><b>وتَرسُمُ نَصَّ التَجرِبَةِ مِنَ القامُوسِ لا حَرفِيّاً</b>
    /// (القاعِدَة ١١) — وبِنَفسِ مِفتاحَي شاشَةِ الدَفعِ، فَلا نَصَّانِ
    /// يَقولانِ الشَيءَ نَفسَه بِصِياغَتَين.</para>
    /// </summary>
    [Fact]
    public void The_order_page_uses_the_same_dictionary_keys_as_the_checkout_screen()
    {
        var orderPage = PageText("libs", "templates",
            "ACommerce.Templates.Customer.Marketplace", "Components", "Pages", "MyDealDetail.razor");
        var checkout = PageText("libs", "templates",
            "ACommerce.Templates.Customer.Marketplace", "Components", "Pages", "CheckoutPage.razor");

        foreach (var key in new[] { "checkout.payment.simulation_badge", "checkout.payment.simulation_hint" })
        {
            Assert.Contains(key, checkout, StringComparison.Ordinal);
            Assert.Contains(key, orderPage, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// <para><b>والمِفتاحانِ مَوجودانِ في القامُوسِ العَرَبيّ</b> — مِفتاحٌ
    /// يُرسَمُ ولا يوجَدُ يَظهَرُ خاماً لِلمُشتَري، وهذا أَسوَأُ مِن
    /// صَمتٍ لِأَنَّه يَكشِفُ داخِلَ النِظامِ في شاشَةِ دَفع.</para>
    /// </summary>
    [Fact]
    public void The_simulation_strings_exist_in_the_arabic_dictionary()
    {
        var ar = PageText("libs", "core", "ACommerce.Platform.I18n", "Locales", "ar.json");
        Assert.True(ar.Length > 10_000, "أَداةٌ عَمياء: `ar.json` لَم يُقرَأ.");

        Assert.Contains("\"checkout.payment.simulation_badge\"", ar, StringComparison.Ordinal);
        Assert.Contains("\"checkout.payment.simulation_hint\"", ar, StringComparison.Ordinal);

        // والتَلميحُ يَقولُ صَراحَةً إنَّه لا خَصمَ ولا فاتورَة — وهُوَ
        // حَدُّ صاحِبِ المَشروعِ بِنَصِّه، لا تَحسينُ صِياغَة.
        Assert.Contains("لا تُصدَرُ فاتورَة", ar, StringComparison.Ordinal);
    }
}
