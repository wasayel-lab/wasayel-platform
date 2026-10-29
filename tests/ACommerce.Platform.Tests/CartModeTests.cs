using ACommerce.Kit.Tenants;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ نَمَطُ السَلَّة: مُختَلِطَةٌ بِفاتورَةٍ واحِدَة، أَو مُنفَصِلَةٌ لِكُلِّ بائِع (قَرارُ المالِك د2، ‏2026-10-29) ═══════════════════════════
//
// **القَرار** (ملاحظةُ المالِك): «يختار صاحب المتجر نمط سلّته: سلّة منفصلة لكل بائع (فاتورة لكل بائع فلا تتشابك طلبات البائعين)، أو سلّة مختلطة
// بفاتورة واحدة يُلغى فيها قبل الدفع الكل أو لا شيء. والافتراض هو المختلطة كما اليوم حتى يختار». **والتَنفيذ**: إعدادٌ لِكُلِّ مَتجَرٍ بِمَعجَمٍ مُغلَق
// ومُصادِق، والافتِراضُ المُختَلِطَة؛ وتَوصيفُ السُلوكِ الحاليِّ قَبلَ التَبديل (القاعِدَة ٣) — صُفوفُ «Today_» هُنا تُودَعُ وَحدَها وتُقلَبُ بِأَسمائِها.
public class CartModeTests(ITestOutputHelper output)
{
    private const string Template = "libs/templates/ACommerce.Templates.Customer.Marketplace";

    private static string Source(string rel)
    {
        var path = Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, rel);
        Assert.True(File.Exists(path), $"مَفقود: {path}");
        return WriteEndpointGuardTests.StripComments(File.ReadAllText(path));
    }

    private static string CartAdd()
    {
        var code = Source($"{Template}/MarketplaceTemplateExtensions.cs");
        var a = code.IndexOf("\"/{slug}/listings/{id:guid}/cart/add\"", StringComparison.Ordinal);
        Assert.True(a > 0, "نُقطَةُ cart/add غائِبَة.");
        var b = code.IndexOf(".RequireStoreWritable();", a, StringComparison.Ordinal);
        return code[a..b];
    }

    /// <summary><b>اليَوم</b> (‏2026-10-29، تَوصيفٌ قَبلَ قَرارِ المالِك د2): لا إعدادَ لِنَمَطِ السَلَّةِ في وَثيقَةِ المَتجَر، وجِسمُ <c>cart/add</c> لا يَسأَلُ عَن بائِعِ
    /// ما في السَلَّة — فَكُلُّ سَلَّةٍ مُختَلِطَةٌ تَحمِلُ بائِعينَ بِفاتورَةٍ واحِدَة (صُفوفُ <c>GatewayInvoiceShareTests</c> القائِمَةُ تَقيسُ الفاتورَةَ الواحِدَة). يُقلَبُ بِاسمِه.</summary>
    [Fact]
    public void Today_every_cart_is_mixed_and_cart_add_never_asks_which_vendor_is_already_in_it()
    {
        Assert.Null(typeof(Tenant).GetProperty("CartMode"));
        var body = CartAdd();
        output.WriteLine($"جِسمُ cart/add: {body.Length} مِحرَفاً");
        Assert.True(body.Length > 500, "أَداةٌ عَمياء: جِسمٌ قَصير.");
        Assert.DoesNotContain("CartMode", body, StringComparison.Ordinal);
        Assert.DoesNotContain("vendor", body, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, body.Split("cart.Items.Add(").Length - 1);
    }
}
