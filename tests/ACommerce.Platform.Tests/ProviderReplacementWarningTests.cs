using ACommerce.Platform.I18n;
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

    /// <summary><b>اليَوم</b> (‏2026-10-29، تَوصيفٌ قَبلَ قَرارِ المالِك د1): صَفحَةُ التَكامُلاتِ لا تَقولُ إنّ رَبطاً جَديداً يَحِلُّ مَحَلَّ القائِم — لا مِفتاحَ
    /// قامُوسٍ لِذلك، والصَفحَةُ تَرسُمُ نَماذِجَ الرَبطِ بَعدَ بِطاقَةِ الرَبطِ الحاليِّ بِلا سَطرٍ بَينَهُما. يُقلَبُ بِاسمِه.</summary>
    [Fact]
    public void Today_the_integrations_page_never_says_a_new_payment_binding_replaces_the_current_one()
    {
        var razor = StudioProviders();
        output.WriteLine($"الصَفحَة: {razor.Length} مِحرَفاً");
        Assert.True(razor.Length > 2000, "أَداةٌ عَمياء: صَفحَةٌ قَصيرَة.");
        Assert.DoesNotContain("replaces_binding", razor, StringComparison.Ordinal);
        Assert.Equal("studio.providers.replaces_binding", LocaleCatalog.Text(LocaleCatalog.Arabic, "studio.providers.replaces_binding"));
    }
}
