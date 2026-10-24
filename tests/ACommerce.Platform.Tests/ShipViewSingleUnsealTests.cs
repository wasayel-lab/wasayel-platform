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
    /// <remarks><b>وقُلِبَ ثانِيَةً بِاسمِه (‏2026-10-24، مُراجَعَةُ تَيّارِ S15 الجَولَة 1 — ولا يُعادُ حَرفٌ فَوقَه)</b>: كانَ
    /// <c>The_vendor_view_opens_the_token_alone_and_leaves_the_secret_to_the_click</c> («جاهِز=True · فَكُّ السِرّ=0»، والنَقرَةُ <c>carrier_unavailable</c> بِسَطر) —
    /// صَفٌّ ثَبَّتَه إصلاحُ <c>c1d7bb6</c> نَفسُه، وانقَلَبَ لِأَنّ القيمَةَ الواحِدَةَ صارَت السِرّ: العَرضُ يَلمَسُ الفَخَّ فَيُخفي البابَ، والنَقرَةُ تُرَدُّ كَما كانَت —
    /// فَما يُرسَمُ هُوَ ما يُقبَل.</remarks>
    [Fact]
    public async Task The_vendor_view_opens_the_secret_and_an_unopenable_secret_hides_the_door_the_click_refuses()
    {
        var (w, shipping, log) = await WorldAsync(trapSecret: true);
        var deal = (await w.Deals.LoadAsync(Slug, w.Id))!;

        var ready = await shipping.CarrierReadyForViewerAsync(Slug, deal, DealShipTests.Vendor);
        var viewOpens = SecretOpens(log);
        var click = await shipping.ShipAsync(Slug, w.Id, DealShipTests.Vendor, "Riyadh");
        output.WriteLine($"العَرض: جاهِز={ready} · فَكُّ السِرّ={viewOpens} — النَقرَة: {click.Code ?? "ok"} · فَكُّ السِرّ={SecretOpens(log) - viewOpens}");

        Assert.False(ready);
        Assert.Equal(1, viewOpens);
        Assert.Equal(DealShipAuthorization.CarrierUnavailable, click.Code);
        Assert.Equal(1, SecretOpens(log) - viewOpens);
        Assert.Empty(w.Oto.Calls);
    }

    /// <summary><b>والعَرضُ مَصدَرُه قيمَةٌ واحِدَة</b> — نَصُّ الدالَّة: المَدخَلُ المَحلولُ بِلا فَكٍّ ثُمَّ فَكُّ الرَمزِ وَحدَه، ولا <c>CarrierForAsync</c>
    /// ولا <c>TryOpenSecret</c>؛ وتِلكَ باقِيَةٌ لِلنَقرَةِ والوارِد.</summary>
    /// <remarks><b>قُلِبَ بِاسمِه (‏2026-10-24، مُراجَعَةُ تَيّارِ S15 الجَولَة 1 — ولا يُعادُ حَرفٌ فَوقَه)</b>: كانَ <c>The_view_body_resolves_then_opens_the_token_only</c>
    /// (جِسمُ العَرضِ <c>… &amp;&amp; TryOpenToken(tenantSlug, carrier, out _)</c>). والآن: المَدخَلُ المَحلولُ ثُمَّ <c>OpensForView</c> — والسِرُّ فيها قَبلَ الرَمز،
    /// والرَمزُ في الفَرعِ الَّذي لا سِرَّ فيه وَحدَه؛ ولا <c>CarrierForAsync</c> ولا فَكٌّ مُباشِرٌ في جِسمِ العَرض.</remarks>
    [Fact]
    public void The_view_body_resolves_then_opens_one_value_the_secret_when_the_entry_has_one()
    {
        var code = WriteEndpointGuardTests.StripComments(File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot,
            "libs/templates/ACommerce.Templates.Customer.Marketplace/Services/Deals/DealShipping.cs")));
        var a = code.IndexOf("public async Task<bool> CarrierReadyForViewerAsync(", StringComparison.Ordinal);
        var p = code.IndexOf("private bool OpensForView(", StringComparison.Ordinal);
        var b = code.IndexOf("public async Task<VendorCarrier?> CarrierForAsync(", StringComparison.Ordinal);
        Assert.True(a > 0 && p > a && b > p, "الدالَّتانِ ثُمَّ CarrierForAsync بِتَرتيبِها.");
        var body = code[a..p];
        Assert.Contains("ResolveCarrierAsync(tenantSlug, deal!, ct) is { } carrier && OpensForView(tenantSlug, carrier)", body, StringComparison.Ordinal);
        Assert.DoesNotContain("CarrierForAsync(", body, StringComparison.Ordinal);
        Assert.DoesNotContain("TryOpenSecret(", body, StringComparison.Ordinal);
        Assert.DoesNotContain("TryOpenToken(", body, StringComparison.Ordinal);

        var probe = string.Join(" ", code[p..b].Split((char[])[' ', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries));
        Assert.Contains("=> carrier.Entry is not null && VendorShippingSurface.WebhookField is { } webhookField && carrier.Entry.Values.ContainsKey(webhookField)"
            + " ? TryOpenSecret(tenantSlug, carrier, out _) : TryOpenToken(tenantSlug, carrier, out _);", probe, StringComparison.Ordinal);
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
    //
    // ‏(‏2026-10-24، مُراجَعَةُ تَيّارِ S15 الجَولَة 2 — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه): **قُلِبا في `9cec4a4`** — ما فَوقَ هذا السَطرِ وَصفُ ما كانَ
    // قَبلَه: العَرضُ اليَومَ يَفُكُّ سِرَّ الوارِدِ إن كانَ في المَدخَل، والرَمزَ حينَ لا قيمَةَ غَيرُه (`DealShipping.OpensForView`؛ ‏ADR-033 §٧‑ص‑١٠،
    // الدِقَّةُ الثانِيَة).

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

    /// <summary><b>قُلِبَ «Today_» بِاسمِه (‏2026-10-24، مُراجَعَةُ تَيّارِ S15 الجَولَة 1)</b> — كانَ
    /// <c>Today_the_vendor_view_opens_the_refresh_token_on_an_entry_whose_secret_opens</c> (‏<c>02aa986</c>): «جاهِز=False · فَكُّ الرَمز=1». والآن: مَدخَلٌ
    /// سِرُّه يَنفَكّ — والرَمزُ فَخٌّ مُدَّةَ العَرضِ وَحدَها — يُقرَأُ «جاهِزاً» بِصِفرِ مُحاوَلَةٍ لِفَكِّ الرَمز؛ ثُمَّ يُعادُ الرَمزُ سَليماً فَتَشحَنُ النَقرَة. أَي:
    /// <b>عَدَدُ ما يُفَكُّ مِن رَمزِ التَحديثِ في عَرضِ مَدخَلٍ سَليمٍ صِفر</b>.</summary>
    [Fact]
    public async Task The_vendor_view_never_opens_the_refresh_token_on_an_entry_whose_secret_opens()
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

        Assert.True(ready);
        Assert.Equal(0, viewOpens);
        Assert.True(click.Ok, click.Code);
        Assert.Equal(0, TokenOpens(log) - viewOpens);
    }

    /// <summary><b>قُلِبَ «Today_» بِاسمِه (‏2026-10-24، مُراجَعَةُ تَيّارِ S15 الجَولَة 1)</b> — كانَ
    /// <c>Today_a_repaste_after_a_master_key_change_whose_registration_fails_draws_a_ship_door_every_click_refuses</c> (‏<c>02aa986</c>): «جاهِز=True · أَسطُر=0 —
    /// النَقرَة: carrier_unavailable · أَسطُر=1». والآن: لَصقٌ يُسَجِّلُ وارِدَه بِالمِفتاحِ الرَئيسِ الأَوَّل، ثُمَّ يَتَبَدَّلُ المِفتاحُ ويَلصِقُ البائِعُ رَمزَه مِن جَديدٍ
    /// وتَرُدُّ OTO تَسجيلَ الوارِد — فَيُختَمُ الرَمزُ بِالجَديدِ ويَبقى السِرُّ بِالقَديم. العَرضُ يَفُكُّ السِرَّ فَلا يَنفَكُّ فَلا بابَ، والنَقرَةُ تُرَدُّ كَما كانَت.</summary>
    [Fact]
    public async Task A_repaste_after_a_master_key_change_whose_registration_fails_hides_the_ship_door_the_click_refuses()
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

        Assert.False(ready);
        Assert.Equal(1, viewOpens);
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

    /// <summary><b>وما لا يُغلِقُه — مَقيساً</b> (‏2026-10-24، مُراجَعَةُ تَيّارِ S15 الجَولَة 1): رَمزٌ لا يَنفَكُّ وسِرٌّ يَنفَكّ — العَرضُ «جاهِز» بِلا سَطرٍ
    /// (لا يَلمَسُ الرَمز)، والنَقرَةُ تَفُكُّه فَتُجابُ <c>carrier_unavailable</c> بِسَطرٍ وصِفرِ نِداء. هذا هُوَ البابُ المَيِّتُ الباقي — ولا كاتِبَ يَصنَعُه
    /// (الصَفُّ التالي).</summary>
    [Fact]
    public async Task A_token_that_does_not_open_beside_a_secret_that_does_draws_the_door_and_the_click_refuses()
    {
        var w = await DealShipTests.ConfirmedCashDeal(vendorEntry: false);
        var log = new ShipCapturingLogger();
        var shipping = ShippingOn(w, w.Vault, log);
        w.Docs.Put(EntryWith(w, trapToken: true));
        var deal = (await w.Deals.LoadAsync(Slug, w.Id))!;

        var ready = await shipping.CarrierReadyForViewerAsync(Slug, deal, DealShipTests.Vendor);
        var viewOpens = TokenOpens(log);
        var click = await shipping.ShipAsync(Slug, w.Id, DealShipTests.Vendor, "Riyadh");
        output.WriteLine($"العَرض: جاهِز={ready} · أَسطُر={viewOpens} — النَقرَة: {click.Code ?? "ok"} · أَسطُر={TokenOpens(log) - viewOpens} · نِداءاتُ OTO: {w.Oto.Calls.Count}");

        Assert.True(ready);
        Assert.Equal(0, viewOpens);
        Assert.Equal(DealShipAuthorization.CarrierUnavailable, click.Code);
        Assert.Equal(1, TokenOpens(log) - viewOpens);
        Assert.Empty(w.Oto.Calls);
    }

    /// <summary><b>ولا كاتِبَ يَختِمُ سِرّاً أَحدَثَ مِن رَمزِه</b> (‏2026-10-24): كُلُّ كِتابَةٍ لِمَدخَلِ بائِعٍ في <c>libs/</c> نِداءٌ لِـ<c>BindVendorAsync</c> داخِلَ
    /// <c>ConnectAsync</c> وَحدَها، بِالقاموسِ <c>values</c> الَّذي يُبدَأُ بِالرَمزِ مَختوماً في النِداءِ نَفسِه — وخَتمُ السِرِّ بَعدَه. فَالسِرُّ أَقدَمُ مِن رَمزِه أَو مِثلُه
    /// دائِماً، والصَفُّ السابِقُ لا يَبلُغُه إلّا عَبَثٌ بِالقاعِدَة. ويَطبَعُ ما عَدَّه ويَفشَلُ عَلى الصِفر (القاعِدَة ١٠).</summary>
    [Fact]
    public void Every_vendor_entry_write_seals_the_token_in_the_same_call_before_any_secret()
    {
        var libs = Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, "libs");
        var calls = Directory.EnumerateFiles(libs, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .SelectMany(f => System.Text.RegularExpressions.Regex.Matches(WriteEndpointGuardTests.StripComments(File.ReadAllText(f)), @"\bBindVendorAsync\(")
                .Select(m => (File: Path.GetFileName(f), m.Index)))
            .ToArray();

        var file = Path.Combine(libs, "templates/ACommerce.Templates.Customer.Marketplace/Services/Shipping/VendorShipping.cs");
        var code = WriteEndpointGuardTests.StripComments(File.ReadAllText(file));
        var at = code.IndexOf("private async Task<TenantProviderBinding> ConnectAsync(", StringComparison.Ordinal);
        Assert.True(at > 0, "ConnectAsync غائِبَة.");
        var end = code.IndexOf("\n    }\n", at, StringComparison.Ordinal);
        var body = code[at..end];
        var token = body.IndexOf("[refreshField.Code] = StoredValue.Sealed(_vault, refreshField.Kind, token,", StringComparison.Ordinal);
        var secret = body.IndexOf("values[webhookField.Code] = StoredValue.Sealed(_vault, webhookField.Kind, secret,", StringComparison.Ordinal);
        var binds = System.Text.RegularExpressions.Regex.Matches(body, @"_providers\.BindVendorAsync\(").Select(m => m.Index).ToArray();
        var withValues = System.Text.RegularExpressions.Regex.Matches(body, @"_providers\.BindVendorAsync\(tenantSlug, ProviderCapabilities\.Delivery, owner, def\.Slug,\s+values,").Count;
        output.WriteLine($"نِداءاتُ BindVendorAsync في libs (غَيرَ التَعريف): {calls.Count(c => c.File != "TenantProviderService.cs")} · في ConnectAsync: {binds.Length} · بِـvalues: {withValues} · خَتمُ الرَمز@{token} · خَتمُ السِرّ@{secret}");

        Assert.NotEmpty(binds);
        Assert.Equal(binds.Length, calls.Count(c => c.File != "TenantProviderService.cs"));
        Assert.Equal(binds.Length, withValues);
        Assert.True(token >= 0 && secret > token && binds.All(b => b > token), "الرَمزُ يُختَمُ أَوَّلاً في كُلِّ حِفظ.");
        Assert.Equal(1, System.Text.RegularExpressions.Regex.Matches(code, @"StoredValue\.Sealed\(_vault, webhookField\.Kind").Count);
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
