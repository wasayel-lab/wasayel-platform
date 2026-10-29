using ACommerce.Platform.I18n;
using ACommerce.Platform.Providers;
using ACommerce.Templates.Customer.Marketplace.Services.Providers;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ رَبطُ مُزَوِّدِ دَفعٍ آخَرَ يَحِلُّ مَحَلَّ القائِم — ويُقالُ قَبلَ أَن يَقَع (قَرارُ المالِك د1، ‏2026-10-29) ═══════════════════════════════
//
// **القَرار (د1 «ج»، والتَنفيذُ المُعَدَّل)**: «نصّ واحد في القاموس يظهر في صفحة التكاملات فوق نماذج الربط متى وُجد ربط قائم: «ربط مزوّد دفع آخر يحلّ محلّ
// الربط الحاليّ — من يربط المفتاح السرّيّ يفقد رابط الدفع، ولا تُعرض باقاته المدفوعة.» ولا شيء غيره الآن». ولا يُبنى دَفعُ الباقاتِ بِالمِفتاحِ الآن — يُكتَبُ
// (ج) قَراراً في ‏ADR-032 بِشَرطِ بِنائِه. **ما فُحِصَ في الكود**: `StudioProviders.razor` يَعرِضُ الرَبطَ الحاليَّ ثُمَّ نَماذِجَ الرَبطِ بِلا نَصٍّ يَقولُ إنّ الجَديدَ
// يَحِلُّ مَحَلَّ القائِم (‏`BindProviderAsync` يَكتُبُ فَوقَه بِمُعَرِّفِ القُدرَة). **التَوصيفُ قَبلَ التَبديل** (القاعِدَة ٣): صَفُّ «Today_» يُودَعُ وَحدَه ويُقلَبُ بِاسمِه.
public class ProviderReplacementWarningTests(ITestOutputHelper output)
{
    private static string StudioProviders()
        => WriteEndpointGuardTests.StripMarkupComments(File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot,
            "libs", "templates", "ACommerce.Templates.Customer.Marketplace", "Components", "Pages", "StudioProviders.razor")));

    /// <summary><b>قُلِبَ بِاسمِه</b> (‏2026-10-29، قَرارُ المالِك د1؛ تَوصيفُه <c>0e20acb</c> — كانَ
    /// <c>Today_the_integrations_page_never_says_a_new_payment_binding_replaces_the_current_one</c>): الصَفحَةُ تَقولُ النَصَّ الواحِدَ مِن القامُوسِ فَوقَ نَماذِجِ الرَبطِ
    /// وبَعدَ بِطاقَةِ الرَبطِ الحاليّ، بِشَرطِ <see cref="ProviderBindSurface.WarnsReplacement"/> لا بِسَطرٍ في الصَفحَة؛ والنَصُّ بِحَرفِ القَرار.</summary>
    [Fact]
    public void The_integrations_page_says_once_above_the_forms_that_a_new_payment_binding_replaces_the_current_one()
    {
        var razor = StudioProviders();
        output.WriteLine($"الصَفحَة: {razor.Length} مِحرَفاً");
        Assert.True(razor.Length > 2000, "أَداةٌ عَمياء: صَفحَةٌ قَصيرَة.");
        var none = razor.IndexOf("studio.providers.none_bound", StringComparison.Ordinal);
        var warn = razor.IndexOf("@if (Services.Providers.ProviderBindSurface.WarnsReplacement(capability, current, offered.Count))", StringComparison.Ordinal);
        var forms = razor.IndexOf("@foreach (var def in offered)", StringComparison.Ordinal);
        Assert.True(none > 0 && warn > none && forms > warn, $"الحاليّ {none} ← التَنبيه {warn} ← النَماذِج {forms}");
        Assert.Equal(1, razor.Split("studio.providers.replaces_binding").Length - 1);
        Assert.Equal("رَبطُ مُزَوِّدِ دَفعٍ آخَرَ يَحِلُّ مَحَلَّ الرَّبطِ الحالِيّ — مَن يَربِطُ المِفتاحَ السِّرِّيَّ يَفقِدُ رابِطَ الدَّفع، ولا تُعرَضُ باقاتُه المَدفوعَة.",
            LocaleCatalog.Text(LocaleCatalog.Arabic, "studio.providers.replaces_binding"));
    }

    private static TenantProviderBinding Bound(string status) => new() { Id = ProviderCapabilities.Payments, Slug = ProviderCapabilities.Payments, Status = status };

    /// <summary><b>المُوجِب</b>: قُدرَةُ الدَفعِ ورَبطٌ فَعّالٌ ونَموذَجٌ مَعروض.</summary>
    [Fact]
    public void The_warning_shows_for_an_active_payment_binding_above_at_least_one_form()
        => Assert.True(ProviderBindSurface.WarnsReplacement(ProviderCapabilities.Payments, Bound(TenantProviderBinding.StatusActive), 1));

    /// <summary><b>السالِب</b>: لا رَبط، أَو رَبطٌ مَسحوب، أَو لا نَموذَجَ تَحتَه، أَو قُدرَةٌ غَيرُ الدَفع.</summary>
    [Fact]
    public void The_warning_hides_without_an_active_binding_without_a_form_or_outside_payments()
    {
        Assert.False(ProviderBindSurface.WarnsReplacement(ProviderCapabilities.Payments, null, 2));
        Assert.False(ProviderBindSurface.WarnsReplacement(ProviderCapabilities.Payments, Bound(TenantProviderBinding.StatusRevoked), 2));
        Assert.False(ProviderBindSurface.WarnsReplacement(ProviderCapabilities.Payments, Bound(TenantProviderBinding.StatusActive), 0));
        Assert.False(ProviderBindSurface.WarnsReplacement(ProviderCapabilities.Delivery, Bound(TenantProviderBinding.StatusActive), 2));
    }
}
