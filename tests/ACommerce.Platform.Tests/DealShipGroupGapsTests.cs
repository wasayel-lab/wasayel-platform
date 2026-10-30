using ACommerce.Platform.I18n;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using Xunit;
using Xunit.Abstractions;
using static ACommerce.Platform.Tests.DealShipGroupTests;

namespace ACommerce.Platform.Tests;

// ═══ ثَغرَتا د13 «ب»: أُختٌ في «نِزاع» لا تُمسِكُ النَقرَة، وأُختٌ شُحِنَت بِاليَدِ تَخرُجُ مِن البوليصَة (‏2026-10-30، تَدقيقُ الموجَة ١١ CO13) ═══════════
//
// **المُرَحَّلُ [S4] بِنَصِّه** (‏ADR-033 §٧‑ش البَند 7، ‏2026-10-29): «بائعٌ يشحنُ أختًا بيدِه («تأكيد: شحن» بلا OTO) يُخرجُها من الطلب؛ وأختٌ في «نزاع» لا تُمسِك
// (ليست فعّالة) فإن عادت شُحنت وحدَها» — والمُشتَري قيلَ لَه «شُحنَةٌ واحِدَةٌ لِكُلِّ بائِع». **والمَقيسُ اليَوم** أَدناه: `WaitingSiblings` تَسأَلُ البَوّابَةَ فَتَرُدُّ
// أُختَ «النِزاع» بِـ`deal_not_active` لا `deal_not_ready_to_ship` فَلا تَنتَظِرُها؛ و«تأكيد: شحن» العامُّ يُخرِجُ الأُختَ إلى «شَحن» بِلا طَلَبٍ فَتَخرُجُ مِن المَجموعَة.

public class DealShipGroupGapsTests(ITestOutputHelper output)
{
    private const string Slug = DealShipTests.Slug;

    private static Deal Stored(Pair w, Guid id) => w.Docs.Read<Deal>(id.ToString())!;

    /// <summary><b>تَوصيفُ «اليَوم» (١)</b> (القاعِدَة ٣): أُختانِ مِن سَلَّةٍ واحِدَةٍ عِندَ «تأكيد»، الثانِيَةُ في «نِزاع» — <c>WaitingSiblings</c> لِلأولى فارِغَة، و«إنشاءُ البوليصَة»
    /// عَلَيها يُنشِئُ طَلَباً بِمَبلَغِها وَحدَها (‏100)؛ فَإن استُؤنِفَت الثانِيَةُ شُحِنَت وَحدَها بِطَلَبٍ ثانٍ.</summary>
    [Fact]
    public async Task Today_a_disputed_sibling_does_not_hold_the_ship_click()
    {
        var w = await Grouped(await TwoConfirmedCashDeals());
        Assert.True((await w.Deals.DisputeAsync(Slug, w.Second, Guid.NewGuid(), "مالِك التَّطبيق", "شَكوى")).Ok);

        var waiting = DealShipAuthorization.WaitingSiblings(await w.Shipping.SiblingsOfAsync(Slug, Stored(w, w.First)), DealShipTests.Vendor);
        var shipped = await w.Shipping.ShipAsync(Slug, w.First, DealShipTests.Vendor, "Riyadh");

        var orders = Orders(w.Oto);
        output.WriteLine($"تَنتَظِر: {waiting.Count} · الشَحن: ok={shipped.Ok} {shipped.Code} · طَلَبات: {orders.Length} بِمَبلَغ {string.Join("، ", orders.Select(o => o.AmountDue))}");
        Assert.Empty(waiting);
        Assert.True(shipped.Ok, shipped.Code);
        Assert.Equal(100m, Assert.Single(orders).AmountDue);
        Assert.Equal(DealShipAuthorization.DealNotActive, DealShipAuthorization.Validate(Stored(w, w.Second), DealShipTests.Vendor, carrierReady: true)?.Code);
    }

    /// <summary><b>تَوصيفُ «اليَوم» (٢)</b>: البائِعُ يَشحَنُ الثانِيَةَ بِيَدِه («تأكيد: شحن» العامّ) — تَخرُجُ إلى «شَحن» بِلا طَلَب، و«إنشاءُ البوليصَة» عَلى الأولى طَلَبٌ بِمَبلَغِها
    /// وَحدَها. <b>والنَصّانِ لا يَقولانِه</b>: إفصاحُ الدَفعِ «شُحنَةٌ واحِدَةٌ لِكُلِّ بائِع» بِلا شَرط، وتَلميحُ البائِعِ «إلّا ما أُلغِيَ مِنها أَو شُحِنَ مِن قَبل» ولا يَقولُ إنّ
    /// «تأكيد: شحن» نَفسَه يُخرِجُها.</summary>
    [Fact]
    public async Task Today_a_hand_shipped_sibling_leaves_the_waybill_and_neither_text_says_so()
    {
        var w = await Grouped(await TwoConfirmedCashDeals());
        var byHand = await w.Deals.AdvanceAsync(Slug, w.Second, DealShipTests.Seller, "بائِع");
        Assert.True(byHand.Ok, byHand.Reason);
        Assert.Equal(DealStage.Shipping, Stored(w, w.Second).Stage);
        Assert.False(DealShipping.IsPlaced(Stored(w, w.Second)));

        var shipped = await w.Shipping.ShipAsync(Slug, w.First, DealShipTests.Vendor, "Riyadh");
        var orders = Orders(w.Oto);
        output.WriteLine($"بِاليَد: {Stored(w, w.Second).Stage} · الشَحن: ok={shipped.Ok} · طَلَبات: {orders.Length} بِمَبلَغ {string.Join("، ", orders.Select(o => o.AmountDue))}");
        Assert.True(shipped.Ok, shipped.Code);
        Assert.Equal(100m, Assert.Single(orders).AmountDue);

        var note = LocaleCatalog.Text(LocaleCatalog.Arabic, "checkout.payment.per_item_note");
        var hint = LocaleCatalog.Text(LocaleCatalog.Arabic, "deals.detail.ship_group_hint");
        Assert.StartsWith("شُحنَةٌ واحِدَةٌ لِكُلِّ بائِعٍ في سَلَّتِك", note, StringComparison.Ordinal);
        Assert.DoesNotContain("OTO", note, StringComparison.Ordinal);
        Assert.DoesNotContain("بِنَفسِك", hint, StringComparison.Ordinal);
    }
}
