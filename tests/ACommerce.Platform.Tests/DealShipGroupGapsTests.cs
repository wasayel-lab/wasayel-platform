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

    /// <summary><b>قُلِبَ بِاسمِه</b> (تَوصيفُه <c>48ad0da</c> — كانَ <c>Today_a_disputed_sibling_does_not_hold_the_ship_click</c>: تَنتَظِرُ 0 وطَلَبٌ بِـ100): أُختانِ مِن سَلَّةٍ
    /// واحِدَةٍ عِندَ «تأكيد»، الثانِيَةُ في «نِزاع» ⇒ <c>WaitingSiblings</c> لِلأولى تُسَمّيها، و«إنشاءُ البوليصَة» يُرَدُّ بِـ<c>siblings_not_ready</c> بِلا نِداءٍ لِـOTO؛ ثُمَّ يُستَأنَفُ
    /// النِزاعُ فَتُشحَنانِ في طَلَبٍ واحِدٍ بِمَجموعِهِما (‏250). <b>والسالِب</b>: يُحسَمُ بِالإلغاءِ فَلا تَنتَظِرُها الأولى وتُشحَنُ وَحدَها (‏100)، وأُختُ «نِزاع» لِبائِعٍ آخَرَ لا تَنتَظِر.</summary>
    [Fact]
    public async Task A_disputed_sibling_holds_the_ship_click_until_its_dispute_is_resolved()
    {
        var w = await Grouped(await TwoConfirmedCashDeals());
        Assert.True((await w.Deals.DisputeAsync(Slug, w.Second, Guid.NewGuid(), "مالِك التَّطبيق", "شَكوى")).Ok);

        var waiting = DealShipAuthorization.WaitingSiblings(await w.Shipping.SiblingsOfAsync(Slug, Stored(w, w.First)), DealShipTests.Vendor);
        var shipped = await w.Shipping.ShipAsync(Slug, w.First, DealShipTests.Vendor, "Riyadh");

        var orders = Orders(w.Oto);
        output.WriteLine($"تَنتَظِر: {waiting.Count} · الشَحن: ok={shipped.Ok} {shipped.Code} · طَلَبات: {orders.Length}");
        Assert.Equal(w.Second, Assert.Single(waiting).Id);
        Assert.Equal((false, DealShipAuthorization.SiblingsNotReady), (shipped.Ok, shipped.Code));
        Assert.Empty(orders);
        Assert.True(DealShipAuthorization.MayResume(Stored(w, w.Second), DealShipTests.Vendor));
        Assert.False(DealShipAuthorization.MayResume(Stored(w, w.Second), new DealShipper(Guid.NewGuid(), "بائِعٌ آخَر")));

        var owner = new DealCanceller(Guid.NewGuid(), "مالِك التَّطبيق", IsStoreAdmin: true);
        Assert.True((await w.Deals.ResolveDisputeAsync(Slug, w.Second, owner, DealDisputeResolution.Resume, "حُسِم")).Ok);
        var resumed = await w.Shipping.ShipAsync(Slug, w.First, DealShipTests.Vendor, "Riyadh");
        Assert.True(resumed.Ok, resumed.Code);
        Assert.Equal(250m, Assert.Single(Orders(w.Oto)).AmountDue);

        var c = await Grouped(await TwoConfirmedCashDeals());
        Assert.True((await c.Deals.DisputeAsync(Slug, c.Second, Guid.NewGuid(), "مالِك التَّطبيق", "شَكوى")).Ok);
        Assert.True((await c.Deals.ResolveDisputeAsync(Slug, c.Second, owner, DealDisputeResolution.Cancel, "حُسِم")).Ok);
        Assert.Empty(DealShipAuthorization.WaitingSiblings(await c.Shipping.SiblingsOfAsync(Slug, Stored(c, c.First)), DealShipTests.Vendor));
        var alone = await c.Shipping.ShipAsync(Slug, c.First, DealShipTests.Vendor, "Riyadh");
        Assert.True(alone.Ok, alone.Code);
        Assert.Equal(100m, Assert.Single(Orders(c.Oto)).AmountDue);
    }

    /// <summary><b>قُلِبَ بِاسمِه</b> (تَوصيفُه <c>48ad0da</c> — كانَ <c>Today_a_hand_shipped_sibling_leaves_the_waybill_and_neither_text_says_so</c>): <b>السُلوكُ كَما كان</b> —
    /// البائِعُ يَشحَنُ الثانِيَةَ بِيَدِه فَتَخرُجُ مِن الطَلَب، والبوليصَةُ لِلأولى (‏100) — <b>والنَصّانِ يَقولانِه</b> (قَرارُ «أ» F3·١٠: لا رَدَّ في المُحَرِّك): إفصاحُ الدَفعِ
    /// «حينَ يَشحَنُ عَبرَ شَرِكَةِ الشَّحن … وما يُسَلِّمُه بِنَفسِه يَصِلُ وَحدَه»، وتَلميحُ البائِعِ تَحتَ الاستِمارَةِ «وما تُؤَكِّدُ شَحنَه بِنَفسِكَ («تأكيد: شحن») يَخرُجُ مِنها».</summary>
    [Fact]
    public async Task A_hand_shipped_sibling_leaves_the_waybill_and_both_texts_say_so()
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
        Assert.StartsWith("شُحنَةٌ واحِدَةٌ لِكُلِّ بائِعٍ في سَلَّتِك حينَ يَشحَنُ عَبرَ شَرِكَةِ الشَّحن", note, StringComparison.Ordinal);
        Assert.Contains("وما يُسَلِّمُه بِنَفسِه يَصِلُ وَحدَه", note, StringComparison.Ordinal);
        Assert.DoesNotContain("OTO", note, StringComparison.Ordinal);
        Assert.Contains("وما تُؤَكِّدُ شَحنَه بِنَفسِكَ («تأكيد: شحن») يَخرُجُ مِنها", hint, StringComparison.Ordinal);
        foreach (var k in new[] { "deals.detail.ship_group_waiting", "deals.detail.err_siblings_not_ready" })
            Assert.Contains("«نِزاع»", LocaleCatalog.Text(LocaleCatalog.Arabic, k), StringComparison.Ordinal);
    }
}
