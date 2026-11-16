using System.Diagnostics.CodeAnalysis;
using ACommerce.Kit.Auth;
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

    /// <summary>رَمزُ رَدِّ بابَي السَلَّةِ والعَرضِ حينَ يَكونُ مالِكُ الإعلانِ قَد أَغلَقَ حِسابَه — تَقرَؤُه صَفحَةُ الإعلانِ (<c>listings.detail.err_offer_owner_gone</c>).</summary>
    public const string OwnerGoneCode = "owner_gone";

    /// <summary>
    /// <para><b>أَغلَقَ مالِكُ الإعلانِ حِسابَه؟</b> (‏2026-10-30، تَدقيقُ الموجَة ١١ B-PRIV-1؛ تَوصيفُه <c>0e95601</c>) — دالَّةٌ نَقِيَّة: الإعلانُ يُسَمّي مالِكاً
    /// (<c>owner_id</c> بِصيغَةِ مُعَرِّف) و<paramref name="owner"/> — وَثيقَتُه كَما قُرِئَت — غائِبَةٌ أَو مَحذوفَة (<c>AccountErasure.OffererGone</c>: القاعِدَةُ نَفسُها
    /// لِصاحِبِ العَرض). وإعلانٌ بِلا مالِكٍ (إعلاناتُ البَذر) لا يُعَدُّ كَذلك: يُشتَرى كَما كان.</para>
    /// <para>الحَذفُ صارَ يَسحَبُ إعلاناتِ صاحِبِه (<c>AccountErasure.EraseCopiesAsync</c>)، فَهذِه لِحِسابٍ حُذِفَ قَبلَ ذلك: إعلانُه قائِمٌ ولا يَنبَغي أَن يولَدَ عَلَيه
    /// طَلَبٌ أَو عَرضٌ مَربوطٌ بِحِسابٍ مَمحُوّ.</para>
    /// </summary>
    public static bool OwnerGone(Listing listing, User? owner)
        => OwnerOf(listing) is { } id && (owner is null || owner.Id != id || ACommerce.Templates.Customer.Marketplace.Services.Identity.AccountErasure.OffererGone(owner));

    /// <summary>مُعَرِّفُ مالِكِ الإعلانِ مِن <c>owner_id</c> بِالمِفتاحِ الَّذي يَقرَؤُه كُلُّ بابٍ يَسأَلُ المِلكِيَّة، أَو <c>null</c>.</summary>
    public static Guid? OwnerOf(Listing listing)
        => listing.Attributes.TryGetValue(ACommerce.Templates.Customer.Marketplace.Services.Listings.ListingEditService.OwnerAttribute, out var raw)
           && Guid.TryParse(raw, out var id) ? id : null;

    /// <summary><see cref="OwnerGone"/> عَلى جَلسَةِ البابِ — تَقرَأُ وَثيقَةَ المالِكِ حينَ يُسَمّي الإعلانُ مالِكاً، ولا تَقرَأُ شَيئاً حينَ لا.</summary>
    public static async Task<bool> OwnerGoneAsync(IQuerySession session, Listing listing, CancellationToken ct = default)
        => OwnerOf(listing) is { } id && OwnerGone(listing, await session.LoadAsync<User>(id, ct));

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
        // ‏(‏2026-10-30، B-PRIV-1) وإعلانٌ أَغلَقَ مالِكُه حِسابَه كَالغائِب (<see cref="OwnerGone"/>) — يُحذَفُ بَندُه ويَعودُ المُشتَري بِـ<see cref="Changed"/>.
        foreach (var item in cart.Items)
            listings[item.ListingId] = await session.LoadAsync<Listing>(item.ListingId, ct) is { } listing && !await OwnerGoneAsync(session, listing, ct)
                ? listing : null;
        if (!Reconcile(cart, listings)) return false;
        cart.UpdatedAt = DateTime.UtcNow;
        session.Store(cart);
        await session.SaveChangesAsync(ct);
        return true;
    }
}
