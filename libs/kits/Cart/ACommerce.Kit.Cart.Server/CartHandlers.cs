using ACommerce.Platform.Shared;
using Marten;

namespace ACommerce.Kit.Cart.Server;

/// <summary>
/// <para>مُعالِجات رَسائِل السَلَّة — <b>لا نِقاط HTTP</b>. الواجِهَة
/// تَستَدعيها عَبر مَسارات القالَب المَحروسَة
/// (<c>/{slug}/listings/{id}/cart/add</c> وأَخَواتها).</para>
///
/// <para><b>ما زال مِن هُنا:</b> <c>GET /{slug}/api/cart/{userId}</c> —
/// نُقطَة <b>بِلا حارِس</b> كانَت تُعيد سَلَّة أَيّ مُستَخدِم لِمَجهول
/// يَعرِف مُعَرِّفَه، وبِصِفر مُستَهلِك مَقيس.</para>
/// </summary>
public static class CartHandlers
{
    public static async Task Handle(AddToCart cmd, IDocumentStore store, ITenantContext ctx)
    {
        if (!ctx.IsResolved) return;
        await using var s = store.LightweightSession(ctx.Slug);
        var cart = await s.LoadAsync<Cart>(cmd.UserId) ?? new Cart { Id = cmd.UserId };
        var existing = cart.Items.FirstOrDefault(i => i.ListingId == cmd.ListingId);
        if (existing is not null) existing.Quantity += cmd.Quantity;
        else cart.Items.Add(new CartItem
        {
            ListingId = cmd.ListingId, Title = cmd.Title,
            UnitPriceSar = cmd.UnitPriceSar, Quantity = cmd.Quantity,
            Options = cmd.Options ?? new()
        });
        cart.UpdatedAt = DateTime.UtcNow;
        s.Store(cart);
        await s.SaveChangesAsync();
    }

    public static async Task Handle(UpdateCartQuantity cmd, IDocumentStore store, ITenantContext ctx)
    {
        if (!ctx.IsResolved) return;
        await using var s = store.LightweightSession(ctx.Slug);
        var cart = await s.LoadAsync<Cart>(cmd.UserId);
        if (cart is null) return;
        var item = cart.Items.FirstOrDefault(i => i.ListingId == cmd.ListingId);
        if (item is null) return;
        if (cmd.NewQuantity <= 0) cart.Items.Remove(item);
        else item.Quantity = cmd.NewQuantity;
        cart.UpdatedAt = DateTime.UtcNow;
        s.Store(cart);
        await s.SaveChangesAsync();
    }

    public static async Task Handle(RemoveFromCart cmd, IDocumentStore store, ITenantContext ctx)
    {
        if (!ctx.IsResolved) return;
        await using var s = store.LightweightSession(ctx.Slug);
        var cart = await s.LoadAsync<Cart>(cmd.UserId);
        if (cart is null) return;
        cart.Items.RemoveAll(i => i.ListingId == cmd.ListingId);
        cart.UpdatedAt = DateTime.UtcNow;
        s.Store(cart);
        await s.SaveChangesAsync();
    }

    public static async Task Handle(ClearCart cmd, IDocumentStore store, ITenantContext ctx)
    {
        if (!ctx.IsResolved) return;
        await using var s = store.LightweightSession(ctx.Slug);
        s.Delete<Cart>(cmd.UserId);
        await s.SaveChangesAsync();
    }
}
