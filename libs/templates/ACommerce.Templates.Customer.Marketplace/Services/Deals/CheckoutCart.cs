using System.Diagnostics.CodeAnalysis;
using ACommerce.Kit.Listings;
using Marten;

namespace ACommerce.Templates.Customer.Marketplace.Services.Deals;

/// <summary>
/// <para><b>ما في السَلَّةِ يُشتَرى بِحالِ إعلانِه الآن لا بِحالِه يَومَ وُضِع</b> (‏2026-10-24، تَدقيقُ الإطلاق lens-money-integrity-3؛ ‏ADR-032 §٨‑ح‑١ مُلحَقاً).
/// الحَذفُ ناعِم (<c>Listing.Apply(ListingDeleted)</c> يَضبِطُ <c>IsDeleted</c> والوَثيقَةُ باقِيَة) والإخفاءُ الإشرافِيُّ <c>IsHiddenByModerator</c>، فَكانَ شَرطُ
/// <c>listing is null</c> في <c>/checkout/submit</c> و<c>cart/add</c> لا يُمسِكُ أَيّاً مِنهُما؛ والمَبلَغُ سِعرُ السَلَّةِ لا سِعرُ الإعلان (تَوصيفُه <c>bdd6611</c>).</para>
///
/// <para><b>والمُراجَعَةُ تَرفُضُ عِندَ الاختِلافِ ولا تَشحَنُ بِسِعرٍ لَم يُعرَض</b>: صَفحَةُ الإتمامِ تَعرِضُ أَسعارَ السَلَّة، فَسِعرٌ تَبَدَّلَ بَعدَها يُحَدَّثُ في السَلَّةِ
/// ويَعودُ المُشتَري إلى الخُطوَةِ بِرَمزِ <see cref="Changed"/> لِيَرى المَجموعَ الجَديدَ قَبلَ أَن يُطلَبَ شَيء — ومِثلُه بَندٌ لَم يَعُد مُتاحاً يُحذَفُ مِنها.</para>
/// </summary>
public static class CheckoutCart
{
    /// <summary>رَمزُ الرُجوعِ إلى خُطوَةِ الدَفعِ — مِن مَعجَمِ رَدِّ الإتمامِ (<see cref="CheckoutPaymentPolicy.RefusalPath"/>)، ونَصُّه
    /// <c>checkout.payment.err_cart_changed</c>.</summary>
    public const string Changed = "cart_changed";

    /// <summary><b>أَيُشتَرى هذا الإعلان؟</b> — مَوجودٌ ولا محذوفٌ ولا مُخفىً إشرافِيّاً. دالَّةٌ نَقِيَّةٌ يَسأَلُها <c>cart/add</c> وحَلقَةُ الإتمامِ ومُراجَعَةُ السَلَّة.</summary>
    public static bool Buyable([NotNullWhen(true)] Listing? listing)
        => listing is { IsDeleted: false, IsHiddenByModerator: false };

    /// <summary>
    /// <para><b>المُراجَعَةُ النَقِيَّة</b>: يُحذَفُ مِن <paramref name="cart"/> كُلُّ بَندٍ لا يُشتَرى إعلانُه (أَو غابَ مِن <paramref name="listings"/>)، ويُضبَطُ سِعرُ
    /// الوَحدَةِ عَلى سِعرِ الإعلانِ حَيثُ اختَلَف. تُرجِعُ «أَتَغَيَّرَ شَيء» — والسَلَّةُ كَما هي حينَ لا.</para>
    /// </summary>
    public static bool Reconcile(ACommerce.Kit.Cart.Cart cart, IReadOnlyDictionary<Guid, Listing?> listings)
    {
        var changed = cart.Items.RemoveAll(i => !Buyable(listings.GetValueOrDefault(i.ListingId))) > 0;
        foreach (var item in cart.Items)
        {
            var price = listings[item.ListingId]!.Price;
            if (item.UnitPriceSar == price) continue;
            item.UnitPriceSar = price;
            changed = true;
        }
        return changed;
    }

    /// <summary>
    /// <para><b>المُراجَعَةُ عَلى جَلسَةِ النُقطَة</b> — تَقرَأُ إعلانَ كُلِّ بَندٍ ثُمَّ <see cref="Reconcile"/>، وحينَ يَتَغَيَّرُ شَيءٌ تَكتُبُ السَلَّةَ بِنُسخَةٍ جَديدَة
    /// (<c>UpdatedAt</c> — فَمُعَرِّفاتُ <see cref="CheckoutClaim"/> لِلإرسالِ التالي مِنها) وتُرجِعُ <c>true</c> فَتَعودُ النُقطَةُ قَبلَ قَرارِ البِطاقَةِ وقَبلَ أَيِّ وِلادَة.
    /// تَأخُذُ الجَلسَةَ وَسيطاً ولا تَملِكُها.</para>
    /// </summary>
    public static async Task<bool> RefreshAsync(IDocumentSession session, ACommerce.Kit.Cart.Cart cart, CancellationToken ct = default)
    {
        var listings = new Dictionary<Guid, Listing?>();
        foreach (var item in cart.Items)
            listings[item.ListingId] = await session.LoadAsync<Listing>(item.ListingId, ct);
        if (!Reconcile(cart, listings)) return false;
        cart.UpdatedAt = DateTime.UtcNow;
        session.Store(cart);
        await session.SaveChangesAsync(ct);
        return true;
    }
}
