using ACommerce.Kit.Listings;
using ACommerce.Kit.Tenants;
using ACommerce.Templates.Customer.Marketplace.Services.Listings;
using Marten;

namespace ACommerce.Templates.Customer.Marketplace.Services.Patterns;

/// <summary>خَرقٌ واحِدٌ في نَمَطِ السَلَّة — شَكلُ المُصادِقِ المُوَحَّد (القاعِدَة ٤): رَمزٌ ثابِتٌ ونَصٌّ لِلمُراجِع.</summary>
public sealed record CartModeViolation(string Code, string MessageAr);

/// <summary>
/// <para><b>نَمَطُ سَلَّةِ المَتجَر — مَعجَمٌ مُغلَقٌ مِن اثنَين</b> (‏2026-10-29، قَرارُ المالِك د2؛ تَوصيفُ «اليَوم» <c>0dbb2dd</c>): «يختار صاحب المتجر نمط سلّته: سلّة
/// منفصلة لكل بائع (فاتورة لكل بائع فلا تتشابك طلبات البائعين)، أو سلّة مختلطة بفاتورة واحدة يُلغى فيها قبل الدفع الكل أو لا شيء. والافتراض هو المختلطة كما اليوم».
/// اختِيارٌ مِن مَجموعَةٍ مُنتَهِيَةٍ فَهُوَ بَيانات (القاعِدَة ٤): المَعجَمُ والمُصادِقُ هُنا، والقيمَةُ في وَثيقَةِ المَتجَرِ (<see cref="Tenant.CartMode"/>)، ويُحَرِّرُها صاحِبُه
/// في الاستوديو؛ وغِيابُها — حالُ كُلِّ مَتجَرٍ قَبلَه — <see cref="Mixed"/> بِحَرفِ ما كان: <c>cart/add</c> لا يَقرَأُ شَيئاً زائِداً (تَكافُؤٌ صِفرِيّ).</para>
/// <para><b>والأَثَرُ كودٌ خَلفَ المِفتاح</b> (القاعِدَة ٥): في <see cref="PerVendor"/> يُرَدُّ <c>cart/add</c> لِإعلانِ بائِعٍ غَيرِ بائِعِ ما في السَلَّة (<see cref="Refuses"/>)
/// ويُعرَضُ عَلى المُشتَري إكمالُ سَلَّتِه أَو إفراغُها — فَكُلُّ سَلَّةٍ لِبائِعٍ واحِدٍ وكُلُّ فاتورَةٍ لَه.</para>
/// </summary>
public static class CartModes
{
    /// <summary>سَلَّةٌ مُختَلِطَة: بائِعونَ بِفاتورَةٍ واحِدَة، يُلغى طَلَبُها قَبلَ الدَفعِ كُلُّه أَو لا شَيء — الافتِراضُ كَما كان.</summary>
    public const string Mixed = "mixed";

    /// <summary>سَلَّةٌ لِبائِعٍ واحِد: فاتورَةٌ لِكُلِّ بائِعٍ فَلا تَتَشابَكُ طَلَباتُ البائِعين.</summary>
    public const string PerVendor = "per_vendor";

    /// <summary>المَعجَمُ المُغلَق — اثنانِ لا ثالِثَ لَهُما.</summary>
    public static readonly IReadOnlyList<string> All = new[] { Mixed, PerVendor };

    /// <summary>الافتِراض — المُختَلِطَةُ كَما كانَت «حتى يختار».</summary>
    public const string Default = Mixed;

    /// <summary>قيمَةٌ خارِجَ المَعجَم.</summary>
    public const string Unknown = "cart_mode_unknown";

    /// <summary>رَمزُ رَدِّ <c>cart/add</c> في <see cref="PerVendor"/> حينَ يَختَلِفُ البائِع — تَقرَؤُه صَفحَةُ الإعلانِ (<c>?err=</c>) فَتَعرِضُ الإكمالَ أَو الإفراغ.</summary>
    public const string OtherVendorCode = "cart_other_vendor";

    public static IReadOnlyList<CartModeViolation> Validate(string? mode)
        => mode is not null && All.Contains(mode, StringComparer.Ordinal)
            ? Array.Empty<CartModeViolation>()
            : new[] { new CartModeViolation(Unknown, $"نَمَطُ السَلَّةِ «{mode}» خارِجَ المَعجَم ({string.Join("، ", All)}).") };

    public static bool IsValid(string? mode) => Validate(mode).Count == 0;

    /// <summary>نَمَطُ المَتجَرِ كَما يُعمَلُ بِه: قيمَتُه إن صَحَّت، وإلّا <see cref="Default"/> — فَوَثيقَةٌ بِلا قيمَةٍ (أَو بِقيمَةٍ تالِفَة) مُختَلِطَةٌ كَما كانَت.</summary>
    public static string Of(Tenant? tenant)
        => tenant?.CartMode is { } mode && IsValid(mode) ? mode : Default;

    /// <summary>بائِعُ الإعلان — مالِكُه كَما يَكتُبُه النَشر (<see cref="ListingEditService.OwnerAttribute"/>)، و<c>null</c> لِما لا يُقرَأ.</summary>
    public static Guid? VendorOf(Listing? listing)
        => listing is not null && listing.Attributes.TryGetValue(ListingEditService.OwnerAttribute, out var owner) && Guid.TryParse(owner, out var g)
            ? g : null;

    /// <summary>
    /// <para><b>أَيُرَدُّ هذا الإعلانُ عَن هذِه السَلَّة؟ — دالَّةٌ نَقِيَّة</b>: في <see cref="PerVendor"/> وَحدَه، وسَلَّةٌ فيها بائِعٌ مَعروفٌ غَيرُ بائِعِ الإعلانِ ⇒
    /// <see cref="OtherVendorCode"/>. ولا رَدَّ لِسَلَّةٍ فارِغَةٍ (<paramref name="cartVendor"/> <c>null</c>) ولا لِلبائِعِ نَفسِه ولا في <see cref="Mixed"/>.</para>
    /// </summary>
    public static string? Refuses(string mode, Guid? cartVendor, Guid? addedVendor)
        => mode == PerVendor && cartVendor is { } inCart && inCart != addedVendor ? OtherVendorCode : null;

    /// <summary>
    /// <para><b>سُؤالُ <c>cart/add</c> قَبلَ أَيِّ كِتابَة</b>: في <see cref="Mixed"/> لا يَقرَأُ شَيئاً (التَكافُؤُ الصِفرِيّ)؛ وفي <see cref="PerVendor"/> يَقرَأُ إعلانَ أَوَّلِ
    /// بَندٍ في السَلَّةِ غَيرِ هذا الإعلانِ — والسَلَّةُ لِبائِعٍ واحِدٍ بِهذا القَيدِ نَفسِه — ويَسأَلُ <see cref="Refuses"/>. وإعلانٌ لَم يَعُد يُقرَأُ لا يَرُدّ.
    /// (‏2026-10-30، تَدقيقُ الموجَة ١١ CO4: «أَوَّلِ بَندٍ» صارَ «كُلَّ بَندٍ» — والقَيدُ لا يَصدُقُ عَلى سَلَّةٍ قَبلَه؛ انظُر الجِسم.)</para>
    /// </summary>
    public static async Task<string?> RefusalAsync(Tenant? tenant, IDocumentSession session, ACommerce.Kit.Cart.Cart cart, Listing listing)
    {
        var mode = Of(tenant);
        if (mode != PerVendor) return null;
        // (‏2026-10-30، تَدقيقُ الموجَة ١١ CO4 — ولا يُعادُ حَرفٌ فَوقَ ما سَبَق): <b>كُلُّ بَندٍ آخَرَ لا أَوَّلُه</b> — سَلَّةٌ مِن عَهدِ «المُختَلِطَة» قَبلَ أَن يُقلَبَ
        // النَمَطُ تَحمِلُ بائِعَين، وبَندٌ أَوَّلُ حُذِفَ إعلانُه لا يُقرَأ؛ فَكانَ بائِعُ البَندِ الأَوَّلِ يُقبَلُ والثاني في السَلَّة. أَوَّلُ رَدٍّ يُرجَع.
        var added = VendorOf(listing);
        foreach (var other in cart.Items.Where(i => i.ListingId != listing.Id).Select(i => i.ListingId).Distinct())
            if (Refuses(mode, VendorOf(await session.LoadAsync<Listing>(other)), added) is { } code) return code;
        return null;
    }
}
