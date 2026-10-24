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

    /// <summary><b>توصيفُ اليَوم</b>: عَرضُ البائِعِ يُحاوِلُ فَكَّ السِرِّ (سَطرٌ واحِد)، فَمَدخَلٌ رَمزُه يَنفَكُّ «غَيرُ جاهِز».</summary>
    [Fact]
    public async Task Today_the_vendor_view_opens_the_token_and_the_secret()
    {
        var (w, shipping, log) = await WorldAsync(trapSecret: true);
        var deal = (await w.Deals.LoadAsync(Slug, w.Id))!;

        var ready = await shipping.CarrierReadyForViewerAsync(Slug, deal, DealShipTests.Vendor);
        output.WriteLine($"العَرض: جاهِز={ready} · فَكُّ السِرّ={SecretOpens(log)}");

        Assert.False(ready);
        Assert.Equal(1, SecretOpens(log));
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
}
