using ACommerce.Kit.Delivery;
using ACommerce.Platform.Providers;
using ACommerce.Templates.Customer.Marketplace.Services;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using ACommerce.Templates.Customer.Marketplace.Services.Shipping;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ وارِدُ OTO يَفُكُّ رَمزَ تَحديثِ البائِعِ قَبلَ أَن يَتَحَقَّقَ مِن التَوقيع — تَوصيفُ «اليَوم» ═══════════════════════════════
//
// مُراجَعَةُ المَوجَة 3b (‏2026-10-13، G5 (viii)): `ReceiveAsync` تُنادي `CarrierForAsync` — وهي تَفُكُّ رَمزَ التَحديثِ الدائِمَ **وسِرَّ الوارِدِ مَعاً** —
// ثُمَّ `OtoOrderStatusGate.Verify`. فَطَلَبُ `POST` بِلا مُصادَقَةٍ يُسَمّي صَفقَةً في المَتجَرِ يُطلِقُ فَكَّ الرَمزِ الَّذي لا يَحتاجُه إلّا سُؤالُ OTO بَعدَ التَحَقُّق.
//
// **العَدّاد** (القاعِدَة ١٠): الخِزانَةُ صِنفٌ مُغلَقٌ بِلا خُطّاف؛ فَيُختَمُ رَمزُ التَحديثِ في المَدخَلِ بِمِفتاحٍ غَريب — كُلُّ مُحاوَلَةٍ لِفَكِّه تَرمي فَيَكتُبُ
// `CarrierForAsync` سَطرَ «does not open» — وسِرُّ الوارِدِ مَختومٌ بِمِفتاحِ العالَمِ فَيَنفَكّ. فَعَدَدُ ذلك السَطرِ = عَدَدُ مُحاوَلاتِ فَكِّ الرَمز.
//
// **ما يُقلَبُ بِاسمِه في كوميتِ الإصلاح**: صَفُّ «Today_» وَحدَه؛ وصَفُّ الإشعارِ الصادِقِ يَبقى بِحَرفِه (الرَمزُ يُفَكُّ بَعدَ التَحَقُّقِ في الحالَين).
// **قُلِبَ** (‏2026-10-13، كوميتُ «fix(shipping)» بَعدَ ‏9dbc6f9؛ ‏ADR-033 §٧‑ص‑٩): الوارِدُ يُحِلُّ الناقِلَ بِلا فَكٍّ (`ResolveCarrierAsync`)، ويَفُكُّ السِرَّ
// وَحدَه لِلتَحَقُّق (`TryOpenSecret`)، ورَمزَ التَحديثِ بَعدَ أَن يَمُرَّ التَوقيع (`TryOpenToken`) — فَالمُزَوَّرُ لا يُطلِقُ فَكَّ الرَمزِ أَبَداً.

public class InboundUnsealOrderTests(ITestOutputHelper output)
{
    private const string Slug = DealShipTests.Slug;
    private const string DoesNotOpen = "does not open";

    /// <summary>صَفقَةٌ شُحِنَت بِرَمزِ البائِع، ثُمَّ يُستَبدَلُ مَدخَلُه بِمَدخَلٍ رَمزُه مَختومٌ بِمِفتاحٍ غَريبٍ (يَرمي إن فُكّ) وسِرُّه بِمِفتاحِ العالَم (يَنفَكّ).</summary>
    internal static async Task<(DealShipTests.World World, DealShipping Shipping, ShipCapturingLogger Log)> TripwiredAsync()
    {
        var w = await DealShipTests.ConfirmedCashDeal(vendorEntry: true);
        Assert.True((await w.Shipping.ShipAsync(Slug, w.Id, DealShipTests.Vendor, "Riyadh")).Ok);

        var refresh = VendorShippingSurface.RefreshField!;
        var webhook = VendorShippingSurface.WebhookField!;
        var entry = DealShipTests.VendorEntry(w.Vault, Slug, DealShipTests.Seller);
        entry.Values[refresh] = StoredValue.Sealed(DealShipTests.NewVault(), CredentialKinds.SecretKey, DealShipTests.VendorToken,
            VendorShippingSurface.AadFor(Slug, refresh, DealShipTests.Seller));
        Assert.True(entry.Values.ContainsKey(webhook));
        w.Docs.Put(entry);

        var log = new ShipCapturingLogger();
        var options = Options.Create(new OtoOptions { Environment = OtoEnvironment.Staging, RefreshToken = DealShipTests.ConfigToken });
        var shipping = new DealShipping(w.Deals,
            new OtoClient(new HttpClient(w.Oto), options, new OtoTokenCache(), NullLogger<OtoClient>.Instance),
            options, log, w.Providers, w.Vault, new FixedClock(DealShipTests.Now));
        return (w, shipping, log);
    }

    private static int TokenOpens(ShipCapturingLogger log) => log.Lines.Count(l => l.Contains(DoesNotOpen, StringComparison.Ordinal));

    /// <summary><b>قُلِبَ</b> (‏2026-10-13) — كانَ <c>Today_a_forged_notice_opens_the_refresh_token_before_the_signature_is_checked</c> (‏<c>9dbc6f9</c>):
    /// «الإشعارُ المُزَوَّر: carrier_unavailable (404) · فَكُّ الرَمز: 1 · نِداءاتُ OTO: 0». والآن يُرَدُّ المُزَوَّرُ عَلى تَوقيعِه (‏401) ولا مُحاوَلَةَ لِفَكِّ الرَمز —
    /// وفي العَدّادِ أَيضاً: لا سَطرَ «does not open» مِن السِرِّ ولا مِن الرَمز. وصَفُّ الإشعارِ الصادِقِ أَدناه بِحَرفِه يُثبِتُ أَنّ الرَمزَ يُفَكُّ بَعدَ التَحَقُّق.</summary>
    [Fact]
    public async Task A_forged_notice_is_answered_on_its_signature_and_never_opens_the_refresh_token()
    {
        var (w, shipping, log) = await TripwiredAsync();
        var calls = w.Oto.Calls.Count;

        var outcome = await shipping.ReceiveAsync(Slug, OtoOrderStatusTests.Notice(w.Id, secret: "whsec-forged-0000"));
        output.WriteLine($"الإشعارُ المُزَوَّر: {outcome.Code} ({OtoOrderStatusGate.StatusFor(outcome.Code)}) · فَكُّ الرَمز: {TokenOpens(log)} · " +
                         $"نِداءاتُ OTO: {w.Oto.Calls.Count - calls}");

        Assert.Equal(OtoOrderStatusGate.SignatureMismatch, outcome.Code);
        Assert.Equal(401, OtoOrderStatusGate.StatusFor(outcome.Code));
        Assert.Equal(0, TokenOpens(log));
        Assert.Equal(calls, w.Oto.Calls.Count);
    }

    /// <summary>والإشعارُ الصادِقُ (مُوَقَّعٌ بِسِرِّ البائِع) يَبلُغُ فَكَّ الرَمزِ فَيَرمي — «لا ناقِل» بِلا سُؤالِ OTO. يَبقى بِحَرفِه بَعدَ الإصلاح.</summary>
    [Fact]
    public async Task A_genuine_notice_reaches_the_refresh_token_and_a_token_that_does_not_open_answers_carrier_unavailable()
    {
        var (w, shipping, log) = await TripwiredAsync();
        var calls = w.Oto.Calls.Count;

        var outcome = await shipping.ReceiveAsync(Slug, OtoOrderStatusTests.Notice(w.Id));
        output.WriteLine($"الإشعارُ الصادِق: {outcome.Code} · فَكُّ الرَمز: {TokenOpens(log)} · نِداءاتُ OTO: {w.Oto.Calls.Count - calls}");

        Assert.Equal(OtoOrderStatusGate.CarrierUnavailable, outcome.Code);
        Assert.Equal(1, TokenOpens(log));
        Assert.Equal(calls, w.Oto.Calls.Count);
    }
}
