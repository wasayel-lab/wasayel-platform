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

    [Fact]
    public async Task Today_a_throwing_vendor_entry_read_ships_with_the_demo_stores_configured_token()
    {
        var w = await DealShipTests.ConfirmedCashDeal(vendorEntry: true);
        var thrown = FailEntryReads(w.Docs);

        var res = await w.Shipping.ShipAsync(Slug, w.Id, DealShipTests.Vendor, "Riyadh");
        var refresh = w.Oto.Calls.FirstOrDefault(c => c.Path == OtoClient.RefreshTokenPath).Body ?? "";
        output.WriteLine($"الشَحن: {res.Code ?? "ok"} · قِراءاتٌ رَمَت: {thrown()} · رَمزُ التَهيئَة: {refresh.Contains(DealShipTests.ConfigToken)} · " +
                         $"رَمزُ البائِع: {refresh.Contains(DealShipTests.VendorToken)} · نِداءاتُ OTO: {w.Oto.Calls.Count}");

        Assert.True(res.Ok, res.Code);
        Assert.Equal(2, thrown());
        Assert.Contains(DealShipTests.ConfigToken, refresh, StringComparison.Ordinal);
        Assert.DoesNotContain(DealShipTests.VendorToken, refresh, StringComparison.Ordinal);
    }

    private const string Inbound = "https://wasayel.example/theme-demo/shipping/oto/order-status";

    /// <summary>الوارِد: صَفقَةٌ شُحِنَت، ثُمَّ تَرمي قِراءَةُ المَدخَلِ في <c>CarrierForAsync</c> (التَحميلُ الثاني بَعدَ تَحميلِ الصَفقَة) — فَيَسقُطُ إلى
    /// رَمزِ التَهيئَةِ بِلا سِرِّ وارِدٍ، ويُرَدُّ الإشعارُ المُوَقَّعُ بِسِرِّ البائِعِ «لا يُتَحَقَّقُ مِنه» (‏401) لا «لا ناقِل».</summary>
    [Fact]
    public async Task Today_a_throwing_vendor_entry_read_on_the_inbound_falls_back_and_the_signed_notice_is_answered_unverifiable()
    {
        var w = await DealShipTests.ConfirmedCashDeal(vendorEntry: true);
        Assert.True((await w.Shipping.ShipAsync(Slug, w.Id, DealShipTests.Vendor, "Riyadh")).Ok);
        var callsBefore = w.Oto.Calls.Count;
        var thrown = FailEntryReads(w.Docs, afterLoads: 1, failures: 1);

        var outcome = await w.Shipping.ReceiveAsync(Slug, OtoOrderStatusTests.Notice(w.Id));
        output.WriteLine($"الوارِد: {outcome.Code} ({OtoOrderStatusGate.StatusFor(outcome.Code)}) · قِراءاتٌ رَمَت: {thrown()} · " +
                         $"نِداءاتُ OTO بَعدَ الشَحن: {w.Oto.Calls.Count - callsBefore}");

        Assert.Equal(1, thrown());
        Assert.Equal(OtoOrderStatusGate.NoticeUnverifiable, outcome.Code);
        Assert.Equal(callsBefore, w.Oto.Calls.Count);
    }

    /// <summary>التَسجيل: بائِعٌ لَه مَدخَلٌ فَعّالٌ يَعمَل، وقِراءَتُه تَرمي — فَيُحسَبُ «لا مَدخَل»: تُدَقَّقُ الاستِمارَةُ ويُسَجَّلُ حِسابٌ ثانٍ عِندَ OTO
    /// بِالرَمزِ السوقِيِّ ويُكتَبُ رَمزُه فَوقَ رَمزِ الحِسابِ الأَوَّل.</summary>
    [Fact]
    public async Task Today_a_throwing_vendor_entry_read_at_register_opens_a_second_OTO_account_over_the_working_entry()
    {
        var owner = Guid.NewGuid();
        var w = await DealShipTests.ConfirmedCashDeal(vendorEntry: false);
        w.Docs.Put(DealShipTests.VendorEntry(w.Vault, Slug, owner));
        var thrown = FailEntryReads(w.Docs, afterLoads: 0, failures: 1);

        var res = await VendorRegisterBucketTests.Surface(w).RegisterAsync(
            Slug, VendorShippingTests.Registrant(owner), VendorShippingTests.Form(), Inbound);
        var stored = w.Docs.Read<TenantProviderBinding>(TenantProviderBinding.VendorEntryId(ProviderCapabilities.Delivery, owner))!;
        var token = stored.Values[VendorShippingSurface.RefreshField!]
            .Unseal(w.Vault, VendorShippingSurface.AadFor(Slug, VendorShippingSurface.RefreshField!, owner));
        output.WriteLine($"التَسجيل: {res.Code ?? "ok"} · قِراءاتٌ رَمَت: {thrown()} · «قَبلُ»: {(res.Before is null ? "لا مَدخَل" : "مَدخَل")} · " +
                         $"Register: {w.Oto.CallsTo(OtoClient.RegisterPath)} · الرَمزُ المَحفوظ: {(token == DealShipTests.VendorToken ? "الأَوَّل" : "حِسابٌ ثانٍ")}");

        Assert.True(res.Ok, res.Code);
        Assert.Equal(1, thrown());
        Assert.Null(res.Before);
        Assert.Equal(1, w.Oto.CallsTo(OtoClient.RegisterPath));
        Assert.Equal("_refreshToken_", token);
    }

    /// <summary>اللَصق: بائِعٌ لَه مَدخَلٌ بِتَسجيلِ وارِدٍ قائِم (‏<c>59</c>)، وقِراءَتُه تَرمي — فَيُحسَبُ «لا مَدخَل»: يُسَجَّلُ الوارِدُ عِندَ OTO تَسجيلاً
    /// جَديداً (<c>POST</c>) لا تَحديثاً لِلقائِم (<c>PUT</c>)، فَيَبقى القائِمُ عِندَها بِسِرٍّ لَم يَعُد عِندَنا.</summary>
    [Fact]
    public async Task Today_a_throwing_vendor_entry_read_at_paste_registers_a_second_webhook_instead_of_updating_the_kept_one()
    {
        var w = await DealShipTests.ConfirmedCashDeal(vendorEntry: true);
        var thrown = FailEntryReads(w.Docs, afterLoads: 0, failures: 1);

        var res = await VendorTokenRefusedTests.PasteSurface(w).SaveTokenAsync(Slug, VendorTokenRefusedTests.Vendor(), "RT-pasted-7c7c", Inbound);
        var webhook = w.Oto.Calls.Select((c, i) => (c.Path, Method: w.Oto.Methods[i])).Single(c => c.Path == OtoClient.WebhookPath).Method;
        output.WriteLine($"اللَصق: {res.Code ?? "ok"} · قِراءاتٌ رَمَت: {thrown()} · «قَبلُ»: {(res.Before is null ? "لا مَدخَل" : "مَدخَل")} · " +
                         $"تَسجيلُ الوارِد: {webhook.Method}");

        Assert.True(res.Ok, res.Code);
        Assert.Equal(1, thrown());
        Assert.Null(res.Before);
        Assert.Equal(HttpMethod.Post, webhook);
    }
}
