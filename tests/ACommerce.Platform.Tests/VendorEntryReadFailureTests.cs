using ACommerce.Kit.Delivery;
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
}
