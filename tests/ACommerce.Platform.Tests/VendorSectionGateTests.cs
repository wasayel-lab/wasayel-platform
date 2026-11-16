using ACommerce.Templates.Customer.Marketplace.Services.Shipping;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ قِسمُ «حِسابِ الشَّحن» في «إعلاناتي» وبَوّابَةُ النُقطَة — تَكافُؤٌ يُقاسُ قَبلَ التَبديل (القاعِدَة ٣) ═════════════════════════
//
// مُراجَعَةُ المَوجَة 3b (‏2026-10-13، G5 (iv)): الصَفحَةُ تُقَرِّرُ رَسمَ القِسمِ بِـ`myListings.Count > 0`، و`ListingVendorGate.IsAllowed` بِلا مُستَهلِكٍ
// وَقتَ التَشغيل (القاعِدَة ١) — والنُقطَتانِ (`/{slug}/me/shipping/oto` و`…/register`) تَسأَلانِ `ListingVendorGate.Admit` بِالعَدِّ نَفسِه
// (`AccountQueries.MyListingsAsync(...).Count`). هذا الصَفُّ يُثبِتُ أَنّ البَوّابَةَ تُساوي الشَرطَ الَّذي سَتَحُلُّ مَحَلَّه عَلى نِطاقِ الصَفحَة
// (جَلسَةٌ بِمُعَرِّفِ مُستَخدِمٍ غَيرِ فارِغ — ما يَكتُبُه الدُخول) — فَتَبديلُ الصَفحَةِ إلَيها لا يُغَيِّرُ ما يَراه أَحَد. والفَرقُ الوَحيدُ مَقصود: جَلسَةٌ
// بِمُعَرِّفٍ فارِغٍ (`AuthSession.IsAuthenticated` تَقبَلُها) تَرُدُّها النُقطَةُ `vendor_signed_out`، فَلا يُرسَمُ لَها بابٌ لا يُفتَح (القاعِدَة ١٢).
// ويَبقى الصَفُّ بِحَرفِه بَعدَ التَبديل.

public class VendorSectionGateTests(ITestOutputHelper output)
{
    [Fact]
    public void On_the_pages_domain_the_vendor_gate_equals_the_listing_count_check_it_replaces()
    {
        var cases = 0;
        foreach (var user in new[] { DealShipTests.Seller, Guid.NewGuid() })
        foreach (var count in new[] { int.MinValue, -1, 0, 1, 2, 50, int.MaxValue })
        {
            cases++;
            Assert.Equal(count > 0, ListingVendorGate.IsAllowed(user, count));
            Assert.Equal(count > 0, ListingVendorGate.Admit(user, null, count).Vendor is not null);
        }

        // وخارِجَ نِطاقِ الصَفحَة — لا جَلسَة، أَو مُعَرِّفٌ فارِغ — تَرُدُّ البَوّابَةُ ما تَرُدُّه النُقطَة (`vendor_signed_out`)، والشَرطُ القَديمُ لا يَسأَلُ عَنه.
        Assert.False(ListingVendorGate.IsAllowed(null, 1));
        Assert.False(ListingVendorGate.IsAllowed(Guid.Empty, 1));
        output.WriteLine($"حالاتٌ عَلى نِطاقِ الصَفحَة: {cases} · البَوّابَةُ = «العَدُّ > 0» في كُلِّها");
        Assert.Equal(14, cases);
    }
}
