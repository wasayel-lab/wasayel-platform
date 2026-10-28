using System.Text.RegularExpressions;
using ACommerce.Kit.Cart;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

/// <summary>
/// <para><b>اسمُ المُشتَري وجَوّالُه وعُنوانُه في رابِطِ صَفحَةِ الدَفع — قَرارُ المالِك د60 (‏2026-10-28، «ب»).</b> «تُحفَظُ بَياناتُ المُشتَري في
/// سَلَّتِه عِندَ الخادِم، ولا يَحمِلُ الرابِطُ إلّا مُعَرِّفَ السَلَّة. وتَنتَقِلُ خُطُواتُ الاستِمارَةِ بِإرسالٍ لا يَظهَرُ في عُنوانِ الصَفحَة»؛ ومَرجِعُه
/// OWASP وCWE-598: العُنوانُ يُحفَظُ في سِجِلِّ المُتَصَفِّحِ وسِجِلّاتِ الخَوادِم.</para>
///
/// <para><b>«اليَوم» قَبلَ التَبديل</b> (القاعِدَة ٣): استِمارَتا الخُطوَتَينِ ١ و٢ بِـ<c>method="get"</c>، والثانِيَةُ تَحمِلُ الاسمَ والجَوّالَ والعُنوانَ
/// حُقولاً مَخفِيَّة؛ ورابِطُ «رُجوع» يَبنيها في الاستِعلام؛ ومَسارُ رَفضِ الإتمامِ (<c>CheckoutPaymentPolicy.RefusalPath</c>) يُعيدُها فيه؛ والسَلَّةُ لا
/// تَحمِلُ مِنها شَيئاً. وكُلُّ صَفٍّ «Today_» يَنقَلِبُ بِاسمِه في كوميتِ التَبديل.</para>
/// </summary>
public class CheckoutDetailsInUrlTests(ITestOutputHelper output)
{
    internal static string Page()
        => WriteEndpointGuardTests.StripMarkupComments(File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot,
            "libs", "templates", "ACommerce.Templates.Customer.Marketplace", "Components", "Pages", "CheckoutPage.razor")));

    [Fact]
    public void Today_steps_one_and_two_submit_by_get_and_carry_the_buyers_details_in_the_query()
    {
        var page = Page();
        var gets = Regex.Matches(page, @"<form method=""get""").Count;
        var hidden = new[] { "name=\"name\" value=\"@Name\"", "name=\"phone\" value=\"@Phone\"", "name=\"addr\" value=\"@Addr\"" }
            .Count(h => Regex.IsMatch(page, @"<input type=""hidden"" " + Regex.Escape(h)));
        const string back = "checkout?step=1&name={Uri.EscapeDataString(Name)}&phone={Uri.EscapeDataString(Phone)}&addr={Uri.EscapeDataString(Addr)}";
        var fromQuery = new[] { "Name", "Phone", "Addr" }
            .Count(p => Regex.IsMatch(page, @"\[SupplyParameterFromQuery\] public string " + p + @" \{ get; set; \}"));
        output.WriteLine($"استِماراتُ GET: {gets} · حُقولٌ مَخفِيَّةٌ لِلبَيانات: {hidden} · رابِطُ «رُجوع» بِالبَيانات: {page.Contains(back, StringComparison.Ordinal)} · مِن الاستِعلام: {fromQuery}");
        Assert.Equal(2, gets);
        Assert.Equal(3, hidden);
        Assert.Contains(back, page, StringComparison.Ordinal);
        Assert.Equal(3, fromQuery);
    }

    [Fact]
    public void Today_the_refusal_path_returns_to_step_two_with_the_buyers_details_in_the_query()
    {
        var path = CheckoutPaymentPolicy.RefusalPath(CheckoutPayMethods.CardUnavailable, "سارة", "0512345678", "حي النخيل");
        output.WriteLine($"مَسارُ الرَفض: {path}");
        Assert.Equal("checkout?step=2&err=pay_card_unavailable&name=%D8%B3%D8%A7%D8%B1%D8%A9&phone=0512345678&addr=%D8%AD%D9%8A%20%D8%A7%D9%84%D9%86%D8%AE%D9%8A%D9%84", path);
    }

    [Fact]
    public void Today_the_cart_holds_none_of_the_buyers_checkout_details()
    {
        var props = typeof(Cart).GetProperties().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        output.WriteLine($"خَصائِصُ السَلَّة: {string.Join(", ", props)}");
        Assert.Equal(new[] { "Id", "Items", "SubtotalSar", "TotalQuantity", "UpdatedAt" }, props);
    }
}
