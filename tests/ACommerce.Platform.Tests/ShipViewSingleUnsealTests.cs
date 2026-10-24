using System.Net;
using ACommerce.Kit.Delivery;
using ACommerce.Platform.Providers;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using ACommerce.Templates.Customer.Marketplace.Services.Shipping;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ صَفحَةُ الصَفقَةِ تَفُكُّ لِلبائِعِ سِرَّينِ لِتَسأَلَ «أَأَرسُمُ بابَ الشَحن؟» — تَوصيفُ «اليَوم» (تَيّارُ S15 البَند I(2)، ‏2026-10-24) ═══
//
// `CarrierReadyForViewerAsync` (‏G5 (ix)، ‏ADR-033 §٧‑ص‑١٠) تَسأَلُ الجاهِزِيَّةَ بِلا فَكٍّ ثُمَّ — لِلبائِعِ وَحدَه — `CarrierForAsync`: تَفُكُّ
// رَمزَ التَحديثِ **وسِرَّ الوارِد** وتَرميهِما، في كُلِّ عَرضٍ لِلصَفحَة. والصَفحَةُ لا تَستَعمِلُ شَيئاً مِنهُما؛ والسُؤالُ «أَيَنفَكُّ المَدخَل؟»
// يُجيبُه فَكُّ قيمَةٍ واحِدَة.
//
// **العَدّادُ فَخّ**: مَدخَلٌ رَمزُه يَنفَكُّ وسِرُّه مَختومٌ بِخِزانَةٍ أُخرى — فَأَيُّ مُحاوَلَةٍ لِفَكِّ السِرِّ تَكتُبُ سَطرَ «does not open» القائِم،
// وفَكُّ الرَمزِ النّاجِحُ لا يَكتُبُ شَيئاً. فَعَدَدُ الأَسطُرِ عَدَدُ ما فُكَّ مِن السِرّ.
//
// **صَفُّ «Today_» يُقلَبُ بِاسمِه** في كوميتِ الإصلاح (القاعِدَة ٣)، والصَفُّ الآخَرُ يَبقى بِحَرفِه.
public class ShipViewSingleUnsealTests(ITestOutputHelper output)
{
    private const string Slug = DealShipTests.Slug;

    /// <summary>صَفقَةُ نَقدٍ عِندَ «تأكيد»، ومَدخَلُ بائِعِها رَمزُه مَختومٌ بِخِزانَةِ العالَمِ و<paramref name="trapSecret"/> سِرُّه بِخِزانَةٍ أُخرى.</summary>
    private static async Task<(DealShipTests.World W, DealShipping Shipping, ShipCapturingLogger Log)> WorldAsync(bool trapSecret)
    {
        var w = await DealShipTests.ConfirmedCashDeal(vendorEntry: false);
        var entry = DealShipTests.VendorEntry(w.Vault, Slug, DealShipTests.Seller);
        if (trapSecret)
        {
            var webhook = VendorShippingSurface.WebhookField!;
            entry.Values[webhook] = StoredValue.Sealed(DealShipTests.NewVault(), CredentialKinds.IssuedSecret,
                DealShipTests.WebhookSecret, VendorShippingSurface.AadFor(Slug, webhook, DealShipTests.Seller));
        }
        w.Docs.Put(entry);
        var log = new ShipCapturingLogger();
        var options = Options.Create(new OtoOptions { Environment = OtoEnvironment.Staging, RefreshToken = DealShipTests.ConfigToken });
        var shipping = new DealShipping(w.Deals,
            new OtoClient(new HttpClient(w.Oto), options, new OtoTokenCache(), NullLogger<OtoClient>.Instance),
            options, log, w.Providers, w.Vault, new FixedClock(DealShipTests.Now));
        return (w, shipping, log);
    }

    private static int SecretOpens(ShipCapturingLogger log)
        => log.Lines.Count(l => l.Contains("does not open", StringComparison.Ordinal));

    /// <summary><b>قُلِبَ «Today_» بِاسمِه (‏2026-10-24، S15 I(2))</b> — كانَ <c>Today_the_vendor_view_opens_the_token_and_the_secret</c>
    /// (‏<c>8f92f94</c>): «جاهِز=False · فَكُّ السِرّ=1». والآن العَرضُ يَفُكُّ رَمزَ التَحديثِ وَحدَه: الفَخُّ لا يُلمَس (صِفرُ سَطر) والمَدخَلُ «جاهِز».
    /// <b>وما لا يُغلِقُه — مَقيساً لا مَوعوداً</b>: النَقرَةُ تَفُكُّ الاثنَينِ كَما كانَت، فَتَلمَسُ الفَخَّ (سَطرٌ واحِد) وتُجابُ <c>carrier_unavailable</c>.</summary>
    [Fact]
    public async Task The_vendor_view_opens_the_token_alone_and_leaves_the_secret_to_the_click()
    {
        var (w, shipping, log) = await WorldAsync(trapSecret: true);
        var deal = (await w.Deals.LoadAsync(Slug, w.Id))!;

        var ready = await shipping.CarrierReadyForViewerAsync(Slug, deal, DealShipTests.Vendor);
        var viewOpens = SecretOpens(log);
        var click = await shipping.ShipAsync(Slug, w.Id, DealShipTests.Vendor, "Riyadh");
        output.WriteLine($"العَرض: جاهِز={ready} · فَكُّ السِرّ={viewOpens} — النَقرَة: {click.Code ?? "ok"} · فَكُّ السِرّ={SecretOpens(log) - viewOpens}");

        Assert.True(ready);
        Assert.Equal(0, viewOpens);
        Assert.Equal(DealShipAuthorization.CarrierUnavailable, click.Code);
        Assert.Equal(1, SecretOpens(log) - viewOpens);
        Assert.Empty(w.Oto.Calls);
    }

    /// <summary><b>والعَرضُ مَصدَرُه قيمَةٌ واحِدَة</b> — نَصُّ الدالَّة: المَدخَلُ المَحلولُ بِلا فَكٍّ ثُمَّ فَكُّ الرَمزِ وَحدَه، ولا <c>CarrierForAsync</c>
    /// ولا <c>TryOpenSecret</c>؛ وتِلكَ باقِيَةٌ لِلنَقرَةِ والوارِد.</summary>
    [Fact]
    public void The_view_body_resolves_then_opens_the_token_only()
    {
        var code = WriteEndpointGuardTests.StripComments(File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot,
            "libs/templates/ACommerce.Templates.Customer.Marketplace/Services/Deals/DealShipping.cs")));
        var a = code.IndexOf("public async Task<bool> CarrierReadyForViewerAsync(", StringComparison.Ordinal);
        var b = code.IndexOf("public async Task<VendorCarrier?> CarrierForAsync(", StringComparison.Ordinal);
        Assert.True(a > 0 && b > a, "الدالَّتانِ بِتَرتيبِهِما بَعدَ I(1).");
        var body = code[a..b];
        Assert.Contains("ResolveCarrierAsync(tenantSlug, deal!, ct) is { } carrier && TryOpenToken(tenantSlug, carrier, out _)", body, StringComparison.Ordinal);
        Assert.DoesNotContain("CarrierForAsync(", body, StringComparison.Ordinal);
        Assert.DoesNotContain("TryOpenSecret(", body, StringComparison.Ordinal);
        Assert.Contains("if (await CarrierForAsync(tenantSlug, deal!, ct) is not { } carrier)", code, StringComparison.Ordinal);
    }

    /// <summary><b>ويَبقى بِحَرفِه</b>: مَدخَلٌ سَليمٌ كُلُّه — العَرضُ «جاهِز» بِلا سَطر، والنَقرَةُ تَشحَنُ بِرَمزِ البائِع.</summary>
    [Fact]
    public async Task A_healthy_entry_reads_ready_without_a_line_and_the_click_ships()
    {
        var (w, shipping, log) = await WorldAsync(trapSecret: false);
        var deal = (await w.Deals.LoadAsync(Slug, w.Id))!;

        var ready = await shipping.CarrierReadyForViewerAsync(Slug, deal, DealShipTests.Vendor);
        var click = await shipping.ShipAsync(Slug, w.Id, DealShipTests.Vendor, "Riyadh");
        output.WriteLine($"العَرض: جاهِز={ready} · أَسطُر={SecretOpens(log)} · النَقرَة: {click.Code ?? "ok"}");

        Assert.True(ready);
        Assert.True(click.Ok);
        Assert.Equal(0, SecretOpens(log));
    }

    // ═══ والفَكُّ الباقي رَمزُ التَحديث — تَوصيفُ «اليَوم» (‏2026-10-24، مُراجَعَةُ تَيّارِ S15 الجَولَة 1؛ ‏ADR-033 §٧‑ص‑١٠) ═══════════════════
    //
    // البَندُ I(2) طَلَبَ «فَكَّ قيمَةٍ واحِدَةٍ فَلا يُظهِرُ عَرضُ الصَفحَةِ رَمزَ التَحديث»، والإصلاحُ (`c1d7bb6`) أَبقى الرَمزَ وأَسقَطَ السِرّ — فَالرَمزُ الدائِمُ
    // لِحِسابِ البائِعِ عِندَ OTO يُفَكُّ في كُلِّ عَرض. **والعَدّادُ هُنا عَلى الرَمز**: رَمزٌ مَختومٌ بِخِزانَةٍ أُخرى، فَأَيُّ مُحاوَلَةٍ لِفَكِّه تَكتُبُ سَطرَ
    // «does not open» القائِم، والسِرُّ يَنفَكّ. والخِزانَةُ بِلا لوغٍ عَمداً (`ProviderVault.cs`: «لا لوغَ في هذا المِلَفِّ أَصلاً») فَالفَكُّ النّاجِحُ صامِت —
    // **فَالفَخُّ هُوَ العَدّادُ الوَحيدُ بِلا خَيطٍ في الكودِ المُنتِج**، والمَدخَلُ يُعادُ سَليماً بَعدَ العَرضِ فَتَشحَنُ النَقرَة.
    //
    // **وصَفّا «Today_» يُقلَبانِ بِاسمَيهِما** في كوميتِ الإصلاح، وصَفُّ «لا سِرَّ مَختوم» يَبقى بِحَرفِه.

    private static int TokenOpens(ShipCapturingLogger log) => SecretOpens(log);

    /// <summary>مَدخَلُ البائِعِ بِسِرٍّ يَنفَكُّ بِخِزانَةِ العالَمِ ورَمزٍ — إن <paramref name="trapToken"/> — مَختومٍ بِخِزانَةٍ أُخرى.</summary>
    private static TenantProviderBinding EntryWith(DealShipTests.World w, bool trapToken, bool secret = true)
    {
        var entry = DealShipTests.VendorEntry(w.Vault, Slug, DealShipTests.Seller, secret: secret ? DealShipTests.WebhookSecret : null);
        if (trapToken)
        {
            var refresh = VendorShippingSurface.RefreshField!;
            entry.Values[refresh] = StoredValue.Sealed(DealShipTests.NewVault(), CredentialKinds.SecretKey,
                DealShipTests.VendorToken, VendorShippingSurface.AadFor(Slug, refresh, DealShipTests.Seller));
        }
        return entry;
    }

    private static DealShipping ShippingOn(DealShipTests.World w, ProviderVault vault, ShipCapturingLogger log)
    {
        var options = Options.Create(new OtoOptions { Environment = OtoEnvironment.Staging, RefreshToken = DealShipTests.ConfigToken });
        return new DealShipping(w.Deals,
            new OtoClient(new HttpClient(w.Oto), options, new OtoTokenCache(), NullLogger<OtoClient>.Instance),
            options, log, w.Providers, vault, new FixedClock(DealShipTests.Now));
    }

    /// <summary><b>تَوصيفُ «اليَوم»</b>: مَدخَلٌ سِرُّه يَنفَكّ — والرَمزُ فَخٌّ مُدَّةَ العَرضِ وَحدَها. العَرضُ يُحاوِلُ فَكَّ الرَمزِ (سَطرٌ واحِد) فَيُقرَأُ «غَيرَ جاهِز»؛
    /// ثُمَّ يُعادُ الرَمزُ سَليماً فَتَشحَنُ النَقرَة. أَي: رَمزُ التَحديثِ الدائِمُ يُفَكُّ في كُلِّ عَرضٍ لِلصَفحَةِ لِبائِعِها.</summary>
    [Fact]
    public async Task Today_the_vendor_view_opens_the_refresh_token_on_an_entry_whose_secret_opens()
    {
        var w = await DealShipTests.ConfirmedCashDeal(vendorEntry: false);
        var log = new ShipCapturingLogger();
        var shipping = ShippingOn(w, w.Vault, log);
        var deal = (await w.Deals.LoadAsync(Slug, w.Id))!;

        w.Docs.Put(EntryWith(w, trapToken: true));
        var ready = await shipping.CarrierReadyForViewerAsync(Slug, deal, DealShipTests.Vendor);
        var viewOpens = TokenOpens(log);
        w.Docs.Put(EntryWith(w, trapToken: false));
        var click = await shipping.ShipAsync(Slug, w.Id, DealShipTests.Vendor, "Riyadh");
        output.WriteLine($"العَرض: جاهِز={ready} · فَكُّ الرَمز={viewOpens} — النَقرَة (الرَمزُ سَليم): {click.Code ?? "ok"} · أَسطُر={TokenOpens(log) - viewOpens}");

        Assert.False(ready);
        Assert.Equal(1, viewOpens);
        Assert.True(click.Ok, click.Code);
        Assert.Equal(0, TokenOpens(log) - viewOpens);
    }

    /// <summary><b>تَوصيفُ «اليَوم» — والنِصفُ الَّذي يَقَعُ بِالكود</b>: لَصقٌ يُسَجِّلُ وارِدَه بِالمِفتاحِ الرَئيسِ الأَوَّل، ثُمَّ يَتَبَدَّلُ المِفتاحُ ويَلصِقُ البائِعُ
    /// رَمزَه مِن جَديدٍ وتَرُدُّ OTO تَسجيلَ الوارِد — فَيُختَمُ الرَمزُ بِالجَديدِ ويَبقى السِرُّ بِالقَديم (<c>ConnectAsync</c>: «السِرُّ يُستَبدَلُ حينَ تَقبَلُه OTO
    /// وَحدَه»). العَرضُ يَفُكُّ الرَمزَ فَيَرسُمُ البابَ «جاهِزاً»، والنَقرَةُ تَفُكُّ الاثنَينِ فَتُجابُ <c>carrier_unavailable</c> — في كُلِّ نَقرَة (القاعِدَة ١٢).</summary>
    [Fact]
    public async Task Today_a_repaste_after_a_master_key_change_whose_registration_fails_draws_a_ship_door_every_click_refuses()
    {
        var w = await DealShipTests.ConfirmedCashDeal(vendorEntry: false);
        Assert.True((await Surface(w, w.Vault).SaveTokenAsync(Slug, Vendor, "RT-first-key-1a1a", Inbound)).Ok);
        var changed = DealShipTests.NewVault();
        w.Oto.Routes[OtoClient.WebhookPath] = _ => OtoRouteHandler.Json(HttpStatusCode.ServiceUnavailable, "<html/>");
        Assert.True((await Surface(w, changed).SaveTokenAsync(Slug, Vendor, "RT-second-key-2b2b", Inbound)).Ok);
        w.Oto.Calls.Clear();

        var log = new ShipCapturingLogger();
        var shipping = ShippingOn(w, changed, log);
        var deal = (await w.Deals.LoadAsync(Slug, w.Id))!;
        var ready = await shipping.CarrierReadyForViewerAsync(Slug, deal, DealShipTests.Vendor);
        var viewOpens = SecretOpens(log);
        var click = await shipping.ShipAsync(Slug, w.Id, DealShipTests.Vendor, "Riyadh");
        output.WriteLine($"العَرض: جاهِز={ready} · أَسطُر={viewOpens} — النَقرَة: {click.Code ?? "ok"} · أَسطُر={SecretOpens(log) - viewOpens} · نِداءاتُ OTO: {w.Oto.Calls.Count}");

        Assert.True(ready);
        Assert.Equal(0, viewOpens);
        Assert.Equal(DealShipAuthorization.CarrierUnavailable, click.Code);
        Assert.Equal(1, SecretOpens(log) - viewOpens);
        Assert.Empty(w.Oto.Calls);
    }

    /// <summary><b>ويَبقى بِحَرفِه</b>: مَدخَلٌ بِلا سِرٍّ مَختومٍ (تَسجيلٌ لَم يَنجَح قَطّ) لَيسَ لَه ما يُفَكُّ غَيرُ الرَمز — فَالعَرضُ يَفُكُّه: فَخٌّ ⇒ سَطرٌ واحِدٌ
    /// و«غَيرُ جاهِز»، وسَليمٌ ⇒ «جاهِز» بِلا سَطر.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Without_a_sealed_secret_the_token_is_the_one_value_the_view_opens(bool trapToken)
    {
        var w = await DealShipTests.ConfirmedCashDeal(vendorEntry: false);
        var log = new ShipCapturingLogger();
        var shipping = ShippingOn(w, w.Vault, log);
        w.Docs.Put(EntryWith(w, trapToken, secret: false));
        var deal = (await w.Deals.LoadAsync(Slug, w.Id))!;

        var ready = await shipping.CarrierReadyForViewerAsync(Slug, deal, DealShipTests.Vendor);
        output.WriteLine($"[فَخّ={trapToken}] العَرض: جاهِز={ready} · فَكُّ الرَمز={TokenOpens(log)}");

        Assert.Equal(!trapToken, ready);
        Assert.Equal(trapToken ? 1 : 0, TokenOpens(log));
    }

    private const string Inbound = "https://wasayel.example/theme-demo/shipping/oto/order-status";

    private static ListingVendor Vendor => ListingVendorGate.Admit(DealShipTests.Seller, "بائِع", 1).Vendor!;

    /// <summary>سَطحُ البائِعِ عَلى العالَمِ بِخِزانَةٍ بِعَينِها — كَما تَبنيه <c>VendorShippingTests.Surface</c>.</summary>
    private static VendorShippingService Surface(DealShipTests.World w, ProviderVault vault)
    {
        var options = Options.Create(new OtoOptions { Environment = OtoEnvironment.Staging, RefreshToken = DealShipTests.ConfigToken });
        return new VendorShippingService(w.Providers, vault,
            new OtoClient(new HttpClient(w.Oto), options, new OtoTokenCache(), NullLogger<OtoClient>.Instance), options,
            new ACommerce.Templates.Customer.Marketplace.Services.Audit.AuditWriter(w.Docs.Store),
            new ACommerce.Templates.Customer.Marketplace.Services.Api.ApiKeyRevealStore(
                new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions())));
    }
}
