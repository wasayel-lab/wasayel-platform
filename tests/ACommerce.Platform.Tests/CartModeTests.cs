using ACommerce.Kit.Cart;
using ACommerce.Kit.Listings;
using ACommerce.Kit.Tenants;
using ACommerce.Platform.I18n;
using ACommerce.Templates.Customer.Marketplace.Services.Listings;
using ACommerce.Templates.Customer.Marketplace.Services.Patterns;
using ACommerce.Templates.Customer.Marketplace.Services.TenantConfig;
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

    /// <summary><b>قُلِبَ بِاسمِه</b> (‏2026-10-29، قَرارُ المالِك د2؛ تَوصيفُه <c>0dbb2dd</c> — كانَ
    /// <c>Today_every_cart_is_mixed_and_cart_add_never_asks_which_vendor_is_already_in_it</c>): لِوَثيقَةِ المَتجَرِ نَمَطُ سَلَّةٍ بِافتِراضٍ «مُختَلِطَة»، وجِسمُ
    /// <c>cart/add</c> يَسأَلُ <see cref="CartModes.RefusalAsync"/> بَعدَ تَحميلِ السَلَّةِ وقَبلَ الإضافَةِ والكِتابَة، ويَرتَدُّ بِرَمزِه إلى صَفحَةِ الإعلان؛ والإضافَةُ مَوضِعٌ واحِدٌ كَما كانَت.</summary>
    [Fact]
    public void Cart_add_asks_the_store_cart_mode_after_loading_the_cart_and_before_any_write()
    {
        Assert.NotNull(typeof(Tenant).GetProperty("CartMode"));
        Assert.Null(new Tenant().CartMode);
        var body = CartAdd();
        output.WriteLine($"جِسمُ cart/add: {body.Length} مِحرَفاً");
        Assert.True(body.Length > 500, "أَداةٌ عَمياء: جِسمٌ قَصير.");
        var cart = body.IndexOf("LoadAsync<ACommerce.Kit.Cart.Cart>(userId)", StringComparison.Ordinal);
        var mode = body.IndexOf("Services.Patterns.CartModes.RefusalAsync(await tenants.FindAsync(slug), s, cart, listing)", StringComparison.Ordinal);
        var back = body.IndexOf("return Results.Redirect(Link(req, slug, $\"listings/{id}?err={mixedOut}\"));", StringComparison.Ordinal);
        var add = body.IndexOf("cart.Items.Add(", StringComparison.Ordinal);
        var write = body.IndexOf("s.Store(cart);", StringComparison.Ordinal);
        Assert.True(cart > 0 && mode > cart && back > mode && add > back && write > add, $"سَلَّة {cart} ← نَمَط {mode} ← رَدّ {back} ← إضافَة {add} ← كِتابَة {write}");
        Assert.Equal(1, body.Split("cart.Items.Add(").Length - 1);
        Assert.Equal(1, body.Split("CartModes.RefusalAsync(").Length - 1);
    }

    // ═══ المَعجَمُ والمُصادِق — المُوجِبُ والسالِب ════════════════════════════════════════════════════════════

    [Fact]
    public void The_cart_mode_lexicon_is_closed_at_two_and_mixed_is_the_default()
    {
        Assert.Equal(new[] { "mixed", "per_vendor" }, CartModes.All.ToArray());
        Assert.Equal(CartModes.Mixed, CartModes.Default);
        Assert.All(CartModes.All, m => Assert.True(CartModes.IsValid(m)));
        Assert.Empty(CartModes.Validate(CartModes.PerVendor));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Mixed")]
    [InlineData("per-vendor")]
    [InlineData("per_vendor ")]
    [InlineData("by_vendor")]
    public void Anything_outside_the_lexicon_is_refused_with_its_code(string? mode)
    {
        var v = Assert.Single(CartModes.Validate(mode));
        Assert.Equal(CartModes.Unknown, v.Code);
        Assert.False(CartModes.IsValid(mode));
        Assert.Contains(TenantConfigCodes.CartModeUnknown, TenantConfigCodes.All);
        // وحِفظُ الهُوِيَّةِ يَرُدُّ النَمَطَ المُرسَلَ خارِجَ المَعجَم — و`null` «لا تُغَيِّر» لا رَفض.
        var expected = mode is null ? null : CartModes.Unknown;
        Assert.Equal(expected, BrandingSaveService.WhyInvalid(new BrandingSaveRequest("مَتجَر", "", "", "#112233", null, mode)));
    }

    /// <summary>النَمَطُ كَما يُعمَلُ بِه: وَثيقَةٌ بِلا قيمَةٍ — حالُ كُلِّ مَتجَرٍ قَبلَه — أَو بِقيمَةٍ تالِفَةٍ أَو غائِبَةٍ ⇒ مُختَلِطَة (التَكافُؤُ الصِفرِيّ)؛ وقيمَةٌ صَحيحَةٌ كَما هي.</summary>
    [Fact]
    public void A_store_without_a_cart_mode_is_mixed_as_it_was()
    {
        Assert.Equal(CartModes.Mixed, CartModes.Of(null));
        Assert.Equal(CartModes.Mixed, CartModes.Of(new Tenant()));
        Assert.Equal(CartModes.Mixed, CartModes.Of(new Tenant { CartMode = "garbage" }));
        Assert.Equal(CartModes.PerVendor, CartModes.Of(new Tenant { CartMode = CartModes.PerVendor }));
    }

    /// <summary>الدالَّةُ النَقِيَّةُ بِجَدوَلِها: تَرُدُّ في «لِكُلِّ بائِعٍ سَلَّة» بائِعاً غَيرَ بائِعِ السَلَّةِ وَحدَه.</summary>
    [Theory]
    [InlineData("per_vendor", "a", "b", true)]
    [InlineData("per_vendor", "a", null, true)]
    [InlineData("per_vendor", "a", "a", false)]
    [InlineData("per_vendor", null, "b", false)]
    [InlineData("mixed", "a", "b", false)]
    [InlineData("mixed", null, null, false)]
    public void Per_vendor_refuses_only_a_second_vendor(string mode, string? cartVendor, string? added, bool refused)
    {
        static Guid? G(string? k) => k is null ? null : k == "a" ? Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001") : Guid.Parse("bbbbbbbb-0000-4000-8000-000000000002");
        Assert.Equal(refused ? CartModes.OtherVendorCode : null, CartModes.Refuses(mode, G(cartVendor), G(added)));
    }

    // ═══ سُؤالُ cart/add عَلى وَثائِق — والمُختَلِطَةُ لا تَقرَأُ شَيئاً ═══════════════════════════════════════════

    private static Listing ListingOf(Guid id, Guid owner)
        => new() { Id = id, Title = "سِلعَة", Price = 10m, Attributes = new() { [ListingEditService.OwnerAttribute] = owner.ToString() } };

    [Fact]
    public async Task The_cart_add_question_reads_nothing_when_mixed_and_refuses_a_second_vendor_when_per_vendor()
    {
        var docs = new DocWorld();
        var (vendorA, vendorB) = (Guid.NewGuid(), Guid.NewGuid());
        var inCart = ListingOf(Guid.NewGuid(), vendorA);
        docs.Put(inCart);
        var cart = new Cart { Id = Guid.NewGuid() };
        cart.Items.Add(new CartItem { ListingId = inCart.Id, Title = inCart.Title, UnitPriceSar = 10m, Quantity = 1 });
        var fromB = ListingOf(Guid.NewGuid(), vendorB);
        var fromA = ListingOf(Guid.NewGuid(), vendorA);

        await using (var s = docs.Store.LightweightSession("zz"))
        {
            Assert.Null(await CartModes.RefusalAsync(new Tenant(), s, cart, fromB));
            Assert.Null(await CartModes.RefusalAsync(new Tenant { CartMode = CartModes.Mixed }, s, cart, fromB));
            Assert.Equal(0, docs.Members.Count(m => m == "LoadAsync"));

            var perVendor = new Tenant { CartMode = CartModes.PerVendor };
            Assert.Equal(CartModes.OtherVendorCode, await CartModes.RefusalAsync(perVendor, s, cart, fromB));
            Assert.Null(await CartModes.RefusalAsync(perVendor, s, cart, fromA));
            Assert.Null(await CartModes.RefusalAsync(perVendor, s, cart, inCart));
            Assert.Null(await CartModes.RefusalAsync(perVendor, s, new Cart { Id = Guid.NewGuid() }, fromB));
        }
        output.WriteLine($"قِراءاتُ «لِكُلِّ بائِعٍ سَلَّة»: {docs.Members.Count(m => m == "LoadAsync")}");
    }

    // ═══ الحِفظُ والسَطحان ═══════════════════════════════════════════════════════════════════════════════

    /// <summary>الحِفظُ بِخِدمَةِ الهُوِيَّةِ القائِمَة (‏<see cref="BrandingSaveService"/>، القاعِدَة ٨): نَمَطٌ مُرسَلٌ يُكتَبُ عَلى وَثيقَةِ المَتجَر، وغائِبٌ «لا تُغَيِّر» —
    /// فَحِفظُ الإدارَةِ الَّتي لا تُرسِلُه لا يَمَسُّه — وخارِجَ المَعجَمِ يُرَدُّ بِرَمزِه ولا يُكتَبُ شَيء.</summary>
    [Fact]
    public async Task The_identity_save_writes_a_sent_cart_mode_keeps_an_absent_one_and_refuses_an_unknown_one()
    {
        var docs = new DocWorld();
        docs.Put(new Tenant { Id = "zz", Name = "مَتجَر", BrandColor = "#112233" });
        BrandingSaveRequest Req(string? mode) => new("مَتجَر", "", "", "#112233", null, mode);

        await using (var s = docs.Store.LightweightSession())
        {
            Assert.True((await BrandingSaveService.SaveAsync(s, "zz", Req(CartModes.PerVendor))).Ok);
            await s.SaveChangesAsync();
        }
        Assert.Equal(CartModes.PerVendor, docs.Read<Tenant>("zz")!.CartMode);

        await using (var s = docs.Store.LightweightSession())
        {
            Assert.True((await BrandingSaveService.SaveAsync(s, "zz", Req(null))).Ok);
            await s.SaveChangesAsync();
        }
        Assert.Equal(CartModes.PerVendor, docs.Read<Tenant>("zz")!.CartMode);

        await using (var s = docs.Store.LightweightSession())
        {
            var bad = await BrandingSaveService.SaveAsync(s, "zz", Req("by_vendor"));
            Assert.Equal((TenantConfigStatus.Rejected, CartModes.Unknown), (bad.Status, bad.Code));
        }
        Assert.Equal(CartModes.PerVendor, docs.Read<Tenant>("zz")!.CartMode);
    }

    /// <summary>السَطح: نَموذَجُ الهُوِيَّةِ يُقرَأُ حَقلُه بِعَقدِ القَناة (غِيابُه <c>null</c>)؛ وصَفحَةُ الاستوديو تَرسُمُ النَمَطَينِ بِبابِ السَلَّةِ نَفسِه ومِن المَعجَم
    /// وتَقولُ رَمزَ الرَفض؛ وصَفحَةُ الإعلانِ تَقولُ رَمزَ <c>cart/add</c> وتَعرِضُ الطَريقَين؛ والنُصوصُ حَيَّة.</summary>
    [Fact]
    public void The_studio_identity_page_offers_both_modes_and_the_listing_page_offers_both_ways_out()
    {
        var surface = Source($"{Template}/Services/TenantConfig/TenantConfigSurface.cs");
        Assert.Contains("req.Form.ContainsKey(\"cart_mode\") ? req.Form[\"cart_mode\"].ToString().Trim() : null", surface, StringComparison.Ordinal);

        var studio = WriteEndpointGuardTests.StripMarkupComments(Source($"{Template}/Components/Pages/StudioAppBranding.razor"));
        Assert.Contains("@if (Services.Patterns.CartDoor.Opens(Services.Patterns.PatternProfileResolver.Of(tenant)))", studio, StringComparison.Ordinal);
        Assert.Contains("@foreach (var m in Services.Patterns.CartModes.All)", studio, StringComparison.Ordinal);
        Assert.Contains("name=\"cart_mode\"", studio, StringComparison.Ordinal);
        Assert.Contains("@if (Err == Services.Patterns.CartModes.Unknown)", studio, StringComparison.Ordinal);

        var listing = WriteEndpointGuardTests.StripMarkupComments(Source($"{Template}/Components/Pages/TenantListingDetail.razor"));
        Assert.Contains("else if (Err == ACommerce.Templates.Customer.Marketplace.Services.Patterns.CartModes.OtherVendorCode)", listing, StringComparison.Ordinal);
        Assert.Contains("action=\"@($\"/{Slug}/cart/clear\")\"", listing, StringComparison.Ordinal);

        var keys = new[] { "listings.detail.err_cart_other_vendor", "listings.detail.cart_other_vendor_complete", "listings.detail.cart_other_vendor_empty",
            "studio.app_branding.section_cart_mode", "studio.app_branding.cart_mode_mixed", "studio.app_branding.cart_mode_mixed_hint",
            "studio.app_branding.cart_mode_per_vendor", "studio.app_branding.cart_mode_per_vendor_hint", "studio.app_branding.err_cart_mode" };
        foreach (var key in keys)
        {
            var text = LocaleCatalog.Text(LocaleCatalog.Arabic, key);
            Assert.NotEqual(key, text);
            Assert.False(LocaleCatalog.IsPlaceholder(text), key);
        }
        output.WriteLine($"نُصوصٌ حَيَّة: {keys.Length}");
    }
}
