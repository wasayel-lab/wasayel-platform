using ACommerce.Kit.Cart;
using Microsoft.AspNetCore.Http;

namespace ACommerce.Templates.Customer.Marketplace.Services.Deals;

/// <summary>
/// <para><b>بَياناتُ المُشتَري بَينَ خُطُواتِ الإتمامِ في سَلَّتِه لا في الرابِط</b> (‏2026-10-28، قَرارُ المالِك د60 «ب»؛ مَرجِعُه OWASP وCWE-598).
/// كانَت استِمارَتا الخُطوَتَينِ ١ و٢ في <c>CheckoutPage</c> بِـ<c>method="get"</c>، فَالاسمُ والجَوّالُ والعُنوانُ في عُنوانِ الصَفحَة — في سِجِلِّ
/// المُتَصَفِّحِ وسِجِلّاتِ الخَوادِم — ورابِطُ «رُجوع» ومَسارُ رَفضِ الإتمامِ يُعيدانِها فيه (تَوصيفُه <c>bddc7ae</c>). والآن تُرسِلُ الخُطوَتانِ إلى
/// <c>POST /{slug}/checkout/details</c> فَتُحفَظُ في <see cref="Cart.Checkout"/>، والرابِطُ لا يَحمِلُ إلّا رَقمَ الخُطوَة.</para>
/// <para>والقَرارُ كُلُّه هُنا نَقِيّ (القاعِدَة ٦: جِسمُ النُقطَةِ نِداءٌ ورَدّ): ما تَقبَلُه كُلُّ خُطوَة، وإلى أَينَ تَمضي، ومَتى تُفتَحُ الخُطوَةُ الثانِيَة.</para>
/// </summary>
public static class CheckoutDetails
{
    /// <summary>الحَقلُ الَّذي تُسَمّي بِه الاستِمارَةُ خُطوَتَها — <c>1</c> العُنوان، و<c>2</c> الدَفع.</summary>
    public const string FromField = "from";
    public const string FromAddress = "1";
    public const string FromPayment = "2";

    /// <summary>
    /// <para>يُطَبِّقُ ما أَرسَلَتهُ خُطوَةٌ عَلى <paramref name="cart"/> ويُعيدُ الخُطوَةَ التالِيَة. العُنوانُ يُقبَلُ بِحُقولِه الثَلاثَةِ غَيرَ فارِغَةٍ (بَعدَ القَصّ)
    /// فَيَمضي إلى ٢؛ والدَفعُ يُقبَلُ بَعدَ عُنوانٍ مَحفوظٍ فَيَمضي إلى ٣. وما سِوى ذلك لا يَمَسُّ السَلَّةَ ويَعودُ إلى ١.</para>
    /// <para>وطَريقَةُ الدَفعِ تُحفَظُ خاماً: تُطَبَّعُ حَيثُ تُقرَأ (<see cref="CheckoutPaymentPolicy.Decide"/>) كَما كانَت تُقرَأُ مِن الاستِعلام.</para>
    /// </summary>
    public static int Apply(Cart cart, IFormCollection form)
    {
        var from = form[FromField].ToString();
        if (from == FromAddress)
        {
            var name = form["name"].ToString().Trim();
            var phone = form["phone"].ToString().Trim();
            var addr = form["addr"].ToString().Trim();
            if (name.Length == 0 || phone.Length == 0 || addr.Length == 0) return 1;
            cart.Checkout ??= new CartCheckout();
            cart.Checkout.RecipientName = name;
            cart.Checkout.Phone = phone;
            cart.Checkout.Address = addr;
            return 2;
        }
        if (from == FromPayment && HasAddress(cart))
        {
            cart.Checkout!.Pay = form["pay"].ToString().Trim();
            return 3;
        }
        return 1;
    }

    /// <summary>أَمَحفوظٌ عُنوانُ التَوصيلِ بِحُقولِه الثَلاثَة؟ — شَرطُ فَتحِ الخُطوَتَينِ ٢ و٣: رابِطٌ قَديمٌ يَحمِلُ البَياناتِ (أَو خُطوَةٌ تُطلَبُ مُباشَرَةً)
    /// لا يَفتَحُ مُلَخَّصاً بِلا عُنوانٍ يُرسَلُ مِنه طَلَبٌ بِلا عُنوان.</summary>
    public static bool HasAddress(Cart? cart)
        => cart?.Checkout is { } c && c.RecipientName.Length > 0 && c.Phone.Length > 0 && c.Address.Length > 0;

    /// <summary>الخُطوَةُ الَّتي تُعرَضُ فِعلاً لِما طُلِب: خارِجَ ١..٣ أَو بِلا عُنوانٍ مَحفوظٍ ← ١.</summary>
    public static int VisibleStep(int requested, Cart? cart)
        => requested is < 1 or > 3 ? 1 : requested > 1 && !HasAddress(cart) ? 1 : requested;

    /// <summary>مَسارُ الخُطوَةِ النِسبِيّ — رَقمُها وَحدَه.</summary>
    public static string StepPath(int step) => step <= 1 ? "checkout" : $"checkout?step={step}";
}
