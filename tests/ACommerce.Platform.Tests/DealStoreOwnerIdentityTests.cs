using ACommerce.Kit.Auth;
using ACommerce.Kit.Cart;
using ACommerce.Kit.Listings;
using ACommerce.Kit.Notifications;
using ACommerce.Kit.Tenants;
using ACommerce.Platform.I18n;
using ACommerce.Templates.Customer.Marketplace.Services;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using ACommerce.Templates.Customer.Marketplace.Services.Incubator;
using ACommerce.Templates.Customer.Marketplace.Services.Listings;
using ACommerce.Templates.Customer.Marketplace.Services.Patterns;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ هُوِيَّتانِ لِشَخصٍ واحِد، وسَلَّةٌ تُسأَلُ عَن بَندِها الأَوَّلِ وَحدَه (‏2026-10-30، تَدقيقُ الموجَة ١١ CO4) ═══════════════════════════════
//
// **الحالُ المَقيسَة (١)**: إلغاءُ الاستوديو يُمَرِّرُ `auth.UserId` — مُعَرِّفَ `StudioUser` — مُلغِياً، و`DealNotices.VendorToTell` يُقارِنُه بِمُعَرِّفِ العُضوِ
// البائِعِ في المَتجَر. وقُيِّسَت الصِلَةُ بَينَهُما: `TenantAdminGuard` يَقبَلُ هُوِيَّتَينِ مُنفَصِلَتَين — (أ) `Tenant.OwnerUserId == StudioUser.Id`، و(ب) عُضوٌ
// في المَتجَرِ بِدَورٍ لَه `tenant.manage` — ولا شَيءَ في الكودِ يَربِطُ إحداهُما بِالأُخرى إلّا الوَسيلَة: هاتِفٌ أَو بَريدٌ واحِدٌ يَدخُلُ بِه الاستوديو والمَتجَر.
// فَصاحِبُ المَتجَرِ يَبيعُ في مَتجَرِه بِحِسابِ عُضوٍ بِهاتِفِه نَفسِه ويُلغي طَلَبَه مِن الاستوديو ⇒ «أُلغِيَ طَلَبٌ عِندَك» لَه هُوَ.
// **الحالُ المَقيسَة (٢)**: `CartModes.RefusalAsync` في «لِكُلِّ بائِعٍ سَلَّة» يَقرَأُ إعلانَ أَوَّلِ بَندٍ غَيرِ المُضاف — وسَلَّةٌ مِن عَهدِ «المُختَلِطَة» قَبلَ أَن يُقلَبَ النَمَط
// تَحمِلُ بائِعَين، فَيُقبَلُ بائِعُ البَندِ الأَوَّلِ والثاني فيها؛ وبَندٌ أَوَّلُ حُذِفَ إعلانُه لا يُقرَأُ فَلا يُرَدُّ شَيء.

public class DealStoreOwnerIdentityTests(ITestOutputHelper output)
{
    private const string Slug = "same-person";
    private static readonly Guid StudioOwner = Guid.Parse("eeeeeeee-eeee-4eee-8eee-eeeeeeeeeeee");
    private static readonly Guid VendorMember = Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb");
    private static readonly Guid Buyer = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");

    private static DealNotices Notices(DocWorld docs)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSignalR();
        var hub = services.BuildServiceProvider().GetRequiredService<IHubContext<ACommerce.Kit.Realtime.Server.RealtimeHub>>();
        return new DealNotices(docs.Store, hub, new WebPushService(new ConfigurationBuilder().Build(), NullLogger<WebPushService>.Instance));
    }

    /// <summary>مَتجَرٌ صاحِبُه في الاستوديو بِهاتِفٍ، وعُضوٌ بائِعٌ فيه بِهاتِفٍ مُتَحَقَّقٍ مِنه (<paramref name="vendorPhone"/>)، وطَلَبُ سَلَّةٍ عِندَ العُضوِ أَلغَته الإدارَة.</summary>
    private static (DocWorld Docs, Deal Deal) World(string vendorPhone, bool verified = true)
    {
        var docs = new DocWorld();
        docs.Put(new Tenant { Id = Slug, Name = "مَتجَر", OwnerUserId = StudioOwner });
        docs.Put(new StudioUser { Id = StudioOwner, Phone = "0501234567", FullName = "صاحِبُ المَتجَر" });
        docs.Put(new User { Id = VendorMember, TenantSlug = Slug, Phone = vendorPhone, PhoneVerified = verified, FullName = "بائِع" });
        var deal = new Deal
        {
            Id = Guid.NewGuid(), Pattern = "marketplace", Status = DealStatus.Cancelled, InitiatorId = Buyer, CounterpartyId = VendorMember,
            ListingTitle = "سِلعَة", AmountSar = 100m,
        };
        deal.Refs[DealsService.ListingOwnerRef] = VendorMember.ToString();
        docs.Put(deal);
        return (docs, deal);
    }

    private static int To(DocWorld docs, Guid user) => docs.Stored.OfType<Notification>().Count(n => n.UserId == user);

    /// <summary><b>تَوصيفُ «اليَوم» (١)</b> (القاعِدَة ٣): صاحِبُ المَتجَرِ في الاستوديو (‏<c>0501234567</c>) وعُضوٌ بائِعٌ بِالهاتِفِ نَفسِه مَكتوباً
    /// <c>+966501234567</c> — يُلغي مِن الاستوديو طَلَبَه ⇒ <b>يُخبَرُ بائِعاً بِما فَعَلَه هُوَ</b>، والمُشتَري مَرَّة.</summary>
    [Fact]
    public async Task Today_the_studio_owner_cancelling_an_order_on_his_own_member_listing_is_told_as_its_vendor()
    {
        var (docs, deal) = World("+966501234567");
        var l = new L(new HttpContextAccessor());

        Assert.True(await Notices(docs).NotifyCancelledByStoreAsync(Slug, deal, StudioOwner, l));

        output.WriteLine($"البائِعُ (صاحِبُ المَتجَرِ نَفسُه): {To(docs, VendorMember)} · المُشتَري: {To(docs, Buyer)}");
        Assert.Equal(1, To(docs, VendorMember));
        Assert.Equal(1, To(docs, Buyer));
        Assert.Equal(VendorMember, DealNotices.VendorToTell(deal, StudioOwner));
    }

    private static Listing ListingOf(Guid owner)
        => new() { Id = Guid.NewGuid(), Title = "سِلعَة", Price = 10m, Attributes = new() { [ListingEditService.OwnerAttribute] = owner.ToString() } };

    private static Cart CartOf(params Listing[] lines)
    {
        var cart = new Cart { Id = Guid.NewGuid() };
        foreach (var x in lines) cart.Items.Add(new CartItem { ListingId = x.Id, Title = x.Title, UnitPriceSar = 10m, Quantity = 1 });
        return cart;
    }

    /// <summary><b>تَوصيفُ «اليَوم» (٢)</b>: سَلَّةٌ فيها بائِعانِ (أ ثُمَّ ب) في «لِكُلِّ بائِعٍ سَلَّة» — إضافَةُ إعلانٍ لِـ«أ» تُقبَل (البَندُ الأَوَّلُ لِـ«أ»)؛ وسَلَّةٌ بَندُها الأَوَّلُ
    /// حُذِفَ إعلانُه والثاني لِـ«ب» — إضافَةُ «أ» تُقبَلُ كَذلِك.</summary>
    [Fact]
    public async Task Today_a_per_vendor_cart_asks_only_its_first_other_line()
    {
        var docs = new DocWorld();
        var (a, b) = (Guid.NewGuid(), Guid.NewGuid());
        var (a1, b1, gone) = (ListingOf(a), ListingOf(b), ListingOf(b));
        docs.Put(a1); docs.Put(b1);
        var perVendor = new Tenant { CartMode = CartModes.PerVendor };

        await using var s = docs.Store.LightweightSession("zz");
        var twoVendors = await CartModes.RefusalAsync(perVendor, s, CartOf(a1, b1), ListingOf(a));
        var firstGone = await CartModes.RefusalAsync(perVendor, s, CartOf(gone, b1), ListingOf(a));

        output.WriteLine($"سَلَّةُ «أ+ب» ← «أ»: {twoVendors ?? "تُقبَل"} · سَلَّةُ «مَحذوف+ب» ← «أ»: {firstGone ?? "تُقبَل"}");
        Assert.Null(twoVendors);
        Assert.Null(firstGone);
    }
}
