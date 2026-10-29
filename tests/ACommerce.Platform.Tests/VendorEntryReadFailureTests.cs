using ACommerce.Kit.Delivery;
using ACommerce.Platform.Providers;
using ACommerce.Templates.Customer.Marketplace.Services;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using ACommerce.Templates.Customer.Marketplace.Services.Shipping;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ قِراءَةُ مَدخَلِ البائِعِ تَرمي — فَيُشحَنُ بِرَمزِ التَهيئَةِ الاحتِياطِيّ — تَوصيفُ «اليَوم» ═══════════════════════════════════
//
// مُراجَعَةُ المَوجَة 3b (‏2026-10-13، G5 (iii)): `TenantProviderService.VendorEntryAsync` تَلتَقِطُ كُلَّ استِثناءٍ وتُعيدُ `null` — وهو جَوابُ «لا مَدخَل».
// و`CarrierForAsync`/`CarrierReadyForAsync` تَسقُطانِ عِندَ «لا مَدخَل» إلى رَمزِ التَهيئَةِ الاحتِياطِيِّ لِمَتجَرِ العَرض — فَبائِعٌ لَه مَدخَلٌ مَختومٌ
// تَعَثَّرَت قِراءَتُه (عُطلُ قاعِدَة) يُشحَنُ طَلَبُه بِحِسابِ مَتجَرِ العَرضِ لا بِحِسابِه: فَشَلٌ مَفتوحٌ لا مُغلَق.
//
// **ما يُقلَبُ بِاسمِه في كوميتِ الإصلاح**: صَفُّ «Today_» أَدناه.
//
// **ووُسِّعَ التَوصيفُ قَبلَ الإصلاح (‏2026-10-13) — ولا يُعادُ حَرفٌ فَوقَه**: الجَذرُ واحِدٌ وقُرّاؤُه أَربَعَة، فَكُلُّ قارِئٍ يَحسِبُ العُطلَ «لا مَدخَل»
// يُقاسُ بِصَفِّه قَبلَ أَن يُبَدَّل: الوارِدُ (`ReceiveAsync` ← `CarrierForAsync`) يَسقُطُ إلى الاحتِياطِ بِلا سِرٍّ فَيُرَدُّ الإشعارُ «لا يُتَحَقَّقُ مِنه»؛
// والتَسجيلُ (`RegisterAsync`) يَفتَحُ لِبائِعٍ لَه حِسابٌ يَعمَلُ حِساباً ثانِياً عِندَ OTO بِالرَمزِ السوقِيِّ ويَكتُبُ رَمزَه فَوقَ رَمزِ الأَوَّل؛ واللَصقُ
// (`SaveTokenAsync`) يُسقِطُ تَسجيلَ الوارِدِ القائِمَ فَيُنشِئُ عِندَ OTO تَسجيلاً ثانِياً (`POST`) لا تَحديثاً (`PUT`). وتُقلَبُ الثَلاثَةُ بِأَسمائِها مَعَه.
//
// **قُلِبَت الأَربَعَة** (‏2026-10-13، كوميتُ «fix(shipping)» بَعدَ ‏6830eb9؛ ‏ADR-033 §٧‑ص‑٦): `TenantProviderService.ReadVendorEntryAsync` تُفَرِّقُ العُطلَ
// مِن الغِياب (`VendorEntryRead.Failed`) — فَالناقِلُ «لا ناقِل» بِلا احتِياط (`carrier_unavailable` لِلشَحنِ ولِلوارِد)، والتَسجيلُ واللَصقُ يُرَدّانِ
// بِـ`vendor_entry_read_failed` بِلا نِداءِ OTO بَعدَ القِراءَةِ ولا كِتابَة. ولِكُلِّ صَفٍّ مَقلوبٍ سالِبُه: القِراءَةُ نَفسُها حينَ لا تَرمي تَمضي كَما كانَت.

public class VendorEntryReadFailureTests(ITestOutputHelper output)
{
    private const string Slug = DealShipTests.Slug;

    /// <summary>بَعدَ <paramref name="afterLoads"/> تَحميلاً (تَحميلُ الصَفقَةِ في <c>ShipAsync</c>) يَرمي التَحميلانِ التالِيانِ — قِراءَتا المَدخَلِ في
    /// <c>CarrierReadyForAsync</c> ثُمَّ <c>CarrierForAsync</c> — بِعُطلٍ كَعُطلِ القاعِدَة، وما بَعدَهُما يَمضي. يُرجِعُ عَدّادَ ما رَمى.</summary>
    internal static Func<int> FailEntryReads(DocWorld docs, int afterLoads = 1, int failures = 2)
    {
        var loads = 0;
        var thrown = 0;
        docs.BeforeMember = name =>
        {
            if (name != "LoadAsync") return;
            loads++;
            if (loads <= afterLoads || loads > afterLoads + failures) return;
            thrown++;
            throw new InvalidOperationException("عُطلُ قاعِدَةٍ مَصنوعٌ في قِراءَةِ مَدخَلِ البائِع");
        };
        return () => thrown;
    }

    /// <summary><b>قُلِبَ</b> (‏2026-10-13) — كانَ <c>Today_a_throwing_vendor_entry_read_ships_with_the_demo_stores_configured_token</c> (‏<c>bd48a66</c>):
    /// «الشَحن: ok · قِراءاتٌ رَمَت: 2 · رَمزُ التَهيئَة: True · رَمزُ البائِع: False · نِداءاتُ OTO: 3». والآن أَيُّ قِراءَةٍ رَمَت — قِراءَةُ البَوّابَةِ
    /// (<c>CarrierReadyForAsync</c>) أَو قِراءَةُ الفَكِّ بَعدَها (<c>CarrierForAsync</c>) أَو كِلتاهُما — تُجابُ <c>carrier_unavailable</c> بِلا نِداءٍ واحِدٍ
    /// إلى OTO، فَلا يُطلَبُ بِرَمزِ التَهيئَةِ أَبَداً؛ والصَفقَةُ بِحالِها. <b>والسالِب</b>: العالَمُ نَفسُه بِلا عُطلٍ يُشحَنُ بَعدَها بِرَمزِ البائِع.</summary>
    [Theory]
    [InlineData(1, 2)]   // البَوّابَةُ تَرمي (وكانَت الثانِيَةُ سَتَرمي)
    [InlineData(1, 1)]   // البَوّابَةُ وَحدَها
    [InlineData(2, 1)]   // البَوّابَةُ تَقرَأ، والفَكُّ بَعدَها يَرمي
    public async Task A_throwing_vendor_entry_read_never_ships_with_the_fallback_token_and_is_answered_carrier_unavailable(int afterLoads, int failures)
    {
        var w = await DealShipTests.ConfirmedCashDeal(vendorEntry: true);
        var thrown = FailEntryReads(w.Docs, afterLoads, failures);

        var res = await w.Shipping.ShipAsync(Slug, w.Id, DealShipTests.Vendor, "Riyadh");
        var threw = thrown();
        w.Docs.BeforeMember = null;   // ما بَقِيَ مِن العُطلِ المُسَلَّحِ لا يَبلُغُ قِراءَةَ الصَفِّ نَفسِه
        var deal = (await w.Deals.LoadAsync(Slug, w.Id))!;
        output.WriteLine($"[{afterLoads}/{failures}] الشَحن: {res.Code ?? "ok"} · قِراءاتٌ رَمَت: {threw} · نِداءاتُ OTO: {w.Oto.Calls.Count} · " +
                         $"المَرحَلَة: {deal.Stage}");

        Assert.Equal(DealShipAuthorization.CarrierUnavailable, res.Code);
        Assert.Equal(1, threw);
        Assert.Empty(w.Oto.Calls);
        Assert.Equal(DealStage.Confirmed, deal.Stage);
        Assert.False(DealShipping.IsPlaced(deal));

        var shipped = await w.Shipping.ShipAsync(Slug, w.Id, DealShipTests.Vendor, "Riyadh");
        var refresh = w.Oto.Calls.First(c => c.Path == OtoClient.RefreshTokenPath).Body ?? "";
        Assert.True(shipped.Ok, shipped.Code);
        Assert.Contains(DealShipTests.VendorToken, refresh, StringComparison.Ordinal);
        Assert.DoesNotContain(w.Oto.Calls, c => (c.Body ?? "").Contains(DealShipTests.ConfigToken, StringComparison.Ordinal));
    }

    /// <summary><b>والصَفحَةُ تَقرَأُ البَوّابَةَ نَفسَها</b> (<c>canShip</c> في <c>MyDealDetail</c>): قِراءَةٌ تَرمي ⇒ لا «إنشاء بوليصَة» — ومَدخَلٌ يُقرَأُ ⇒ تُرسَم؛
    /// ومَتجَرُ العَرضِ بِلا مَدخَلٍ لِلبائِعِ يَبقى عَلى احتِياطِه — الغِيابُ غَيرُ العُطل.</summary>
    [Fact]
    public async Task The_page_gate_hides_the_ship_form_on_a_throwing_read_and_keeps_the_fallback_for_a_real_absence()
    {
        var w = await DealShipTests.ConfirmedCashDeal(vendorEntry: true);
        var deal = (await w.Deals.LoadAsync(Slug, w.Id))!;

        FailEntryReads(w.Docs, afterLoads: 0, failures: 1);
        var onFailure = await w.Shipping.CarrierReadyForAsync(Slug, deal);
        var onRead = await w.Shipping.CarrierReadyForAsync(Slug, deal);

        var absent = await DealShipTests.ConfirmedCashDeal(vendorEntry: false);
        var onAbsence = await absent.Shipping.CarrierReadyForAsync(Slug, (await absent.Deals.LoadAsync(Slug, absent.Id))!);
        output.WriteLine($"«إنشاء بوليصَة» — عُطل: {onFailure} · قِراءَة: {onRead} · غِيابٌ في مَتجَرِ العَرض: {onAbsence}");

        Assert.False(onFailure);
        Assert.True(onRead);
        Assert.True(onAbsence);
    }

    private const string Inbound = "https://wasayel.example/theme-demo/shipping/oto/order-status";

    /// <summary><b>قُلِبَ</b> (‏2026-10-13) — كانَ <c>Today_a_throwing_vendor_entry_read_on_the_inbound_falls_back_and_the_signed_notice_is_answered_unverifiable</c>
    /// (‏<c>6830eb9</c>): «الوارِد: notice_unverifiable (401) · قِراءاتٌ رَمَت: 1 · نِداءاتُ OTO بَعدَ الشَحن: 0». والآن لا يُسقَطُ إلى رَمزِ التَهيئَةِ بِلا سِرّ:
    /// «لا ناقِل» (‏404) — والإشعارُ نَفسُه بِلا عُطلٍ يُتَحَقَّقُ مِنه ويُكتَب (السالِب).
    /// <para><b>وقُلِبَ ثانِيَةً بِاسمِه</b> (‏2026-10-29، مُراجَعَةُ تَيّارِ S4 الأولى — إتمامُ قَرارِ المالِك د9 (٢) «ب»؛ تَوصيفُه <c>61612d1</c>؛ كانَ
    /// <c>A_throwing_vendor_entry_read_on_the_inbound_is_answered_carrier_unavailable_without_the_fallback</c>): العُطلُ لَيسَ «لا ناقِل» بَعدَ اليَوم
    /// بَل <c>carrier_unreadable</c> ‏503 «أَعِد المُحاوَلَة» — و‏404 «لا نُقطَة» قَد يُكِفُّ المُرسِلَ عَن الإرسال. ولا احتِياطَ ولا نِداءَ كَما كان.</para></summary>
    [Fact]
    public async Task A_throwing_vendor_entry_read_on_the_inbound_is_answered_carrier_unreadable_503_without_the_fallback()
    {
        var w = await DealShipTests.ConfirmedCashDeal(vendorEntry: true);
        Assert.True((await w.Shipping.ShipAsync(Slug, w.Id, DealShipTests.Vendor, "Riyadh")).Ok);
        var callsBefore = w.Oto.Calls.Count;
        var thrown = FailEntryReads(w.Docs, afterLoads: 1, failures: 1);

        var outcome = await w.Shipping.ReceiveAsync(Slug, OtoOrderStatusTests.Notice(w.Id));
        output.WriteLine($"الوارِد: {outcome.Code} ({OtoOrderStatusGate.StatusFor(outcome.Code)}) · قِراءاتٌ رَمَت: {thrown()} · " +
                         $"نِداءاتُ OTO بَعدَ الشَحن: {w.Oto.Calls.Count - callsBefore}");

        Assert.Equal(1, thrown());
        Assert.Equal(OtoOrderStatusGate.CarrierUnreadable, outcome.Code);
        Assert.Equal(503, OtoOrderStatusGate.StatusFor(outcome.Code));
        Assert.Equal(callsBefore, w.Oto.Calls.Count);

        w.Docs.BeforeMember = null;
        w.Oto.Routes[OtoClient.OrderStatusPath] = body => OtoRouteHandler.Tracking(OtoRouteHandler.OrderIdIn(body), status: "delivered");
        var verified = await w.Shipping.ReceiveAsync(Slug, OtoOrderStatusTests.Notice(w.Id));
        Assert.Equal(OtoOrderStatusGate.Delivered, verified.Code);
    }

    /// <summary><b>قُلِبَ</b> (‏2026-10-13) — كانَ <c>Today_a_throwing_vendor_entry_read_at_register_opens_a_second_OTO_account_over_the_working_entry</c>
    /// (‏<c>6830eb9</c>): «التَسجيل: ok · قِراءاتٌ رَمَت: 1 · «قَبلُ»: لا مَدخَل · Register: 1 · الرَمزُ المَحفوظ: حِسابٌ ثانٍ». والآن يُرَدُّ
    /// <c>vendor_entry_read_failed</c> قَبلَ الحِصَّةِ والأَثَرِ والنِداء: لا «Register» ولا حِفظَ، والرَمزُ المَحفوظُ رَمزُ الحِسابِ الأَوَّل.
    /// <b>والسالِب</b>: القِراءَةُ نَفسُها بِلا عُطلٍ تَجِدُ المَدخَلَ فَلا يُسَجَّلُ حِسابٌ ثانٍ (يُعادُ تَسجيلُ الوارِدِ بِرَمزِه).</summary>
    [Fact]
    public async Task A_throwing_vendor_entry_read_at_register_is_refused_before_the_quota_the_audit_and_OTO()
    {
        var owner = Guid.NewGuid();
        var w = await DealShipTests.ConfirmedCashDeal(vendorEntry: false);
        w.Docs.Put(DealShipTests.VendorEntry(w.Vault, Slug, owner));
        var thrown = FailEntryReads(w.Docs, afterLoads: 0, failures: 1);
        var saves = w.Docs.SaveCalls;

        var res = await VendorRegisterBucketTests.Surface(w).RegisterAsync(
            Slug, VendorShippingTests.Registrant(owner), VendorShippingTests.Form(), Inbound);
        var token = TokenOf(w, owner);
        output.WriteLine($"التَسجيل: {res.Code ?? "ok"} · قِراءاتٌ رَمَت: {thrown()} · حِفظات: {w.Docs.SaveCalls - saves} · " +
                         $"نِداءاتُ OTO: {w.Oto.Calls.Count} · الرَمزُ المَحفوظ: {(token == DealShipTests.VendorToken ? "الأَوَّل" : "حِسابٌ ثانٍ")}");

        Assert.Equal(VendorShippingService.EntryReadFailed, res.Code);
        Assert.Null(res.Before);
        Assert.Null(res.After);
        Assert.Equal(1, thrown());
        Assert.Equal(saves, w.Docs.SaveCalls);
        Assert.Empty(w.Oto.Calls);
        Assert.Equal(DealShipTests.VendorToken, token);

        w.Docs.BeforeMember = null;
        var again = await VendorRegisterBucketTests.Surface(w).RegisterAsync(
            Slug, VendorShippingTests.Registrant(owner), VendorShippingTests.Form(), Inbound);
        Assert.True(again.Ok, again.Code);
        Assert.NotNull(again.Before);
        Assert.Equal(0, w.Oto.CallsTo(OtoClient.RegisterPath));
        Assert.Equal(DealShipTests.VendorToken, TokenOf(w, owner));
    }

    /// <summary><b>قُلِبَ</b> (‏2026-10-13) — كانَ <c>Today_a_throwing_vendor_entry_read_at_paste_registers_a_second_webhook_instead_of_updating_the_kept_one</c>
    /// (‏<c>6830eb9</c>): «اللَصق: ok · قِراءاتٌ رَمَت: 1 · «قَبلُ»: لا مَدخَل · تَسجيلُ الوارِد: POST». والآن يُرَدُّ <c>vendor_entry_read_failed</c> بِلا تَسجيلِ
    /// وارِدٍ ولا كِتابَة، والمَدخَلُ القائِمُ بِحالِه — ونِداءٌ واحِدٌ قَبلَه: مُبادَلَةُ الرَمزِ (‏G5 (ii)) تَسبِقُ القِراءَة. <b>والسالِب</b>: اللَصقُ نَفسُه بِلا عُطلٍ
    /// يُحَدِّثُ الوارِدَ القائِمَ (<c>PUT</c>) لا يُنشِئُ ثانِياً.</summary>
    [Fact]
    public async Task A_throwing_vendor_entry_read_at_paste_is_refused_and_the_kept_entry_and_webhook_stay()
    {
        var w = await DealShipTests.ConfirmedCashDeal(vendorEntry: true);
        var kept = VendorTokenRefusedTests.Entry(w)!;
        var thrown = FailEntryReads(w.Docs, afterLoads: 0, failures: 1);

        var res = await VendorTokenRefusedTests.PasteSurface(w).SaveTokenAsync(Slug, VendorTokenRefusedTests.Vendor(), "RT-pasted-7c7c", Inbound);
        output.WriteLine($"اللَصق: {res.Code ?? "ok"} · قِراءاتٌ رَمَت: {thrown()} · كِتابَةُ مَداخِل: {w.Docs.Wrote<TenantProviderBinding>()} · " +
                         $"نِداءاتُ الرَمز: {w.Oto.CallsTo(OtoClient.RefreshTokenPath)} · webhook: {w.Oto.CallsTo(OtoClient.WebhookPath)}");

        Assert.Equal(VendorShippingService.EntryReadFailed, res.Code);
        Assert.Equal(1, thrown());
        Assert.Same(kept, VendorTokenRefusedTests.Entry(w));
        Assert.Equal(0, w.Docs.Wrote<TenantProviderBinding>());
        Assert.Equal(1, w.Oto.CallsTo(OtoClient.RefreshTokenPath));
        Assert.Equal(0, w.Oto.CallsTo(OtoClient.WebhookPath));

        w.Docs.BeforeMember = null;
        var again = await VendorTokenRefusedTests.PasteSurface(w).SaveTokenAsync(Slug, VendorTokenRefusedTests.Vendor(), "RT-pasted-7c7c", Inbound);
        Assert.True(again.Ok, again.Code);
        var webhook = w.Oto.Calls.Select((c, i) => (c.Path, Method: w.Oto.Methods[i])).Single(c => c.Path == OtoClient.WebhookPath).Method;
        Assert.Equal(HttpMethod.Put, webhook);
    }

    private static string TokenOf(DealShipTests.World w, Guid owner)
        => w.Docs.Read<TenantProviderBinding>(TenantProviderBinding.VendorEntryId(ProviderCapabilities.Delivery, owner))!
            .Values[VendorShippingSurface.RefreshField!]
            .Unseal(w.Vault, VendorShippingSurface.AadFor(Slug, VendorShippingSurface.RefreshField!, owner));
}
