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

    /// <summary><b>قُلِبَ «Today_» بِاسمِه (‏2026-10-28، د60)</b> — كانَ <c>Today_steps_one_and_two_submit_by_get_and_carry_the_buyers_details_in_the_query</c>
    /// (‏<c>bddc7ae</c>: «استِماراتُ GET: 2 · حُقولٌ مَخفِيَّة: 3 · رابِطُ «رُجوع» بِالبَيانات · مِن الاستِعلام: 3»). والآن الخُطوَتانِ تُرسِلانِ بِـPOST إلى
    /// <c>/checkout/details</c> كُلٌّ بِحَقلِ خُطوَتِه، ولا حَقلَ مَخفِيٌّ لِلبَياناتِ خارِجَ استِمارَةِ الإرسالِ الأَخيرَة، و«رُجوع» رَقمُ الخُطوَة، ولا خاصِّيَّةَ
    /// تُقرَأُ مِن الاستِعلامِ إلّا الخُطوَةُ ورَمزُ الرَفض.</summary>
    [Fact]
    public void Steps_one_and_two_post_to_the_details_door_and_no_link_carries_the_buyers_details()
    {
        var page = Page();
        var gets = Regex.Matches(page, @"<form method=""get""").Count;
        // الحُقولُ المَخفِيَّةُ لِلبَياناتِ باقِيَةٌ في استِمارَةِ الإرسالِ الأَخيرَةِ وَحدَها — جِسمُ POST لا رابِط؛ فَتُعَدُّ قَبلَها.
        var steps = page[..page.IndexOf("/checkout/submit", StringComparison.Ordinal)];
        var posts = Regex.Matches(page, @"<form method=""post"" action=""@\(\$""/\{Slug\}/checkout/details""\)"">").Count;
        var froms = new[] { "CheckoutDetails.FromAddress", "CheckoutDetails.FromPayment" }
            .Count(f => page.Contains($"<input type=\"hidden\" name=\"@CheckoutDetails.FromField\" value=\"@{f}\" />", StringComparison.Ordinal));
        var hidden = new[] { "name=\"name\" value=\"@Name\"", "name=\"phone\" value=\"@Phone\"", "name=\"addr\" value=\"@Addr\"" }
            .Count(h => Regex.IsMatch(steps, @"<input type=""hidden"" " + Regex.Escape(h)));
        var fromQuery = Regex.Matches(page, @"\[SupplyParameterFromQuery(?:\(Name = ""(?<n>\w+)""\))?\] public \w+\?? (?<p>\w+)")
            .Select(m => m.Groups["p"].Value).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        output.WriteLine($"استِماراتُ GET: {gets} · إلى بابِ البَيانات: {posts} (بِحَقلِ الخُطوَة {froms}) · حُقولٌ مَخفِيَّةٌ لِلبَيانات: {hidden} · مِن الاستِعلام: {string.Join(", ", fromQuery)}");
        Assert.Equal(0, gets);
        Assert.Equal(2, posts);
        Assert.Equal(2, froms);
        Assert.Equal(0, hidden);
        Assert.Contains("AuthSession.LinkFor(Slug, Role, CheckoutDetails.StepPath(1))", page, StringComparison.Ordinal);
        Assert.DoesNotContain("Uri.EscapeDataString(Name)", page, StringComparison.Ordinal);
        Assert.Equal(new[] { "Err", "Step" }, fromQuery);
        Assert.Contains("Step = CheckoutDetails.VisibleStep(Step, cart);", page, StringComparison.Ordinal);
    }

    /// <summary><b>قُلِبَ «Today_» بِاسمِه (‏2026-10-28، د60)</b> — كانَ <c>Today_the_refusal_path_returns_to_step_two_with_the_buyers_details_in_the_query</c>.
    /// والآن رَمزُ الرَفضِ وَحدَه؛ وما كَتَبَه المُشتَري في سَلَّتِه فَيَعودُ إلَيه في الخُطوَة.</summary>
    [Fact]
    public void The_refusal_path_returns_to_step_two_with_its_code_alone()
    {
        var path = CheckoutPaymentPolicy.RefusalPath(CheckoutPayMethods.CardUnavailable);
        output.WriteLine($"مَسارُ الرَفض: {path}");
        Assert.Equal("checkout?step=2&err=pay_card_unavailable", path);
    }

    /// <summary><b>قُلِبَ «Today_» بِاسمِه (‏2026-10-28، د60)</b> — كانَ <c>Today_the_cart_holds_none_of_the_buyers_checkout_details</c>. والآن
    /// <c>Cart.Checkout</c> يَحمِلُ الاسمَ والجَوّالَ والعُنوانَ وطَريقَةَ الدَفع.</summary>
    [Fact]
    public void The_cart_holds_the_buyers_checkout_details_between_steps()
    {
        var props = typeof(Cart).GetProperties().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        var details = typeof(CartCheckout).GetProperties().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        output.WriteLine($"خَصائِصُ السَلَّة: {string.Join(", ", props)} · بَياناتُ الإتمام: {string.Join(", ", details)}");
        Assert.Equal(new[] { "Checkout", "Id", "Items", "SubtotalSar", "TotalQuantity", "UpdatedAt" }, props);
        Assert.Equal(new[] { "Address", "Pay", "Phone", "RecipientName" }, details);
    }

    private static Microsoft.AspNetCore.Http.FormCollection Form(params (string Key, string Value)[] fields)
        => new(fields.ToDictionary(f => f.Key, f => new Microsoft.Extensions.Primitives.StringValues(f.Value)));

    private static Cart CartWithItem() => new() { Id = Guid.NewGuid(), Items = { new CartItem { ListingId = Guid.NewGuid(), Title = "x", UnitPriceSar = 10 } } };

    [Fact]
    public void The_address_step_saves_its_three_fields_trimmed_and_moves_to_payment()
    {
        var cart = CartWithItem();
        var at = cart.UpdatedAt;
        var next = CheckoutDetails.Apply(cart, Form(("from", "1"), ("name", " سارة "), ("phone", " 0512345678"), ("addr", "حي النخيل ")));
        Assert.Equal(2, next);
        Assert.Equal(("سارة", "0512345678", "حي النخيل"), (cart.Checkout!.RecipientName, cart.Checkout.Phone, cart.Checkout.Address));
        Assert.Equal(at, cart.UpdatedAt);   // نُسخَةُ البُنودِ لا تَتَحَرَّك
    }

    [Theory]
    [InlineData("", "0512345678", "حي")]
    [InlineData("سارة", " ", "حي")]
    [InlineData("سارة", "0512345678", "")]
    public void The_address_step_with_a_missing_field_saves_nothing_and_stays(string name, string phone, string addr)
    {
        var cart = CartWithItem();
        Assert.Equal(1, CheckoutDetails.Apply(cart, Form(("from", "1"), ("name", name), ("phone", phone), ("addr", addr))));
        Assert.Null(cart.Checkout);
    }

    [Fact]
    public void The_payment_step_saves_the_method_only_after_an_address_and_moves_to_the_summary()
    {
        var cart = CartWithItem();
        Assert.Equal(1, CheckoutDetails.Apply(cart, Form(("from", "2"), ("pay", "cash"))));
        Assert.Null(cart.Checkout);
        CheckoutDetails.Apply(cart, Form(("from", "1"), ("name", "سارة"), ("phone", "0512345678"), ("addr", "حي")));
        Assert.Equal(3, CheckoutDetails.Apply(cart, Form(("from", "2"), ("pay", "cash"))));
        Assert.Equal("cash", cart.Checkout!.Pay);
        Assert.Equal(1, CheckoutDetails.Apply(cart, Form(("from", "9"), ("pay", "card"))));
        Assert.Equal("cash", cart.Checkout.Pay);
    }

    [Theory]
    [InlineData(1, false, 1)]
    [InlineData(2, false, 1)]
    [InlineData(3, false, 1)]
    [InlineData(2, true, 2)]
    [InlineData(3, true, 3)]
    [InlineData(0, true, 1)]
    [InlineData(4, true, 1)]
    public void A_later_step_opens_only_over_a_saved_address(int requested, bool saved, int shown)
    {
        var cart = CartWithItem();
        if (saved) CheckoutDetails.Apply(cart, Form(("from", "1"), ("name", "سارة"), ("phone", "0512345678"), ("addr", "حي")));
        Assert.Equal(shown, CheckoutDetails.VisibleStep(requested, cart));
        Assert.Equal(1, CheckoutDetails.VisibleStep(requested == 1 ? 1 : 2, null));
    }

    [Fact]
    public void The_step_path_carries_the_step_number_alone()
    {
        Assert.Equal("checkout", CheckoutDetails.StepPath(1));
        Assert.Equal("checkout?step=2", CheckoutDetails.StepPath(2));
        Assert.Equal("checkout?step=3", CheckoutDetails.StepPath(3));
    }

    /// <summary>بابُ البَيانات: يُحَمِّلُ السَلَّةَ ويَسأَلُ <c>CheckoutDetails.Apply</c> ويَحفَظُ ويَمضي بِرَقمِ الخُطوَة — بِلا قَرارٍ في الجِسم.</summary>
    [Fact]
    public void The_details_door_asks_the_pure_function_and_redirects_by_step_number()
    {
        var mte = WriteEndpointGuardTests.StripComments(File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot,
            "libs", "templates", "ACommerce.Templates.Customer.Marketplace", "MarketplaceTemplateExtensions.cs")));
        var at = mte.IndexOf("app.MapPost(\"/{slug}/checkout/details\"", StringComparison.Ordinal);
        Assert.True(at > 0, "لا بابَ بَيانات — الأَداةُ عَمياء.");
        var body = mte[at..mte.IndexOf(".RequireStoreWritable();", at, StringComparison.Ordinal)];
        Assert.Contains("var next = Services.Deals.CheckoutDetails.Apply(cart, req.Form);", body, StringComparison.Ordinal);
        Assert.Contains("return Results.Redirect(Link(req, slug, Services.Deals.CheckoutDetails.StepPath(next)));", body, StringComparison.Ordinal);
        Assert.Equal(0, Regex.Matches(body, @"req\.Form\[").Count);
        Assert.Contains(".RequireFormBody()", body, StringComparison.Ordinal);
    }

    /// <summary>والسَلَّةُ الَّتي صارَت تَحمِلُ الاسمَ والجَوّالَ والعُنوانَ تُحذَفُ بِحَذفِ الحِسابِ في مُعامَلَتِه — قَبلَ حِفظِه الواحِد؛ والتَصنيفُ الوَحيدُ
    /// بِـ<c>DeletedWithAccount</c> اسمُ مُستَلِمِها.</summary>
    [Fact]
    public void Account_deletion_deletes_the_cart_that_holds_the_checkout_details_in_its_one_save()
    {
        var body = AccountDeletionRetentionTests.DeleteEndpointBody();
        var del = body.IndexOf("session.Delete<ACommerce.Kit.Cart.Cart>(userId);", StringComparison.Ordinal);
        var save = body.IndexOf("await session.SaveChangesAsync();", StringComparison.Ordinal);
        output.WriteLine($"حَذفُ السَلَّةِ {del} < الحِفظ {save}");
        Assert.True(del > 0 && save > del);
        Assert.Equal(new[] { "CartCheckout.RecipientName" },
            AccountDeletionRetentionTests.PersonNameFields.Where(f => f.Value.Kind == AccountDeletionRetentionTests.NameKind.DeletedWithAccount).Select(f => f.Key));
    }
}
