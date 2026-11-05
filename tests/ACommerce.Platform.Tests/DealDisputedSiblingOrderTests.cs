using System.Net;
using ACommerce.Kit.Payments;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using Xunit;
using Xunit.Abstractions;
using static ACommerce.Platform.Tests.TenantGatewayEndpointsTests;

namespace ACommerce.Platform.Tests;

// ═══ فاتورَةٌ مَفتوحَةٌ عَلى صَفقَتَين إحداهُما في «نِزاع» — ما يُلغى وَحدَه، وما يُلغى كُلُّه، وما يُستَأنَفُ بَعدَ المُهلَة — تَوصيفُ «اليَوم» (‏2026-11-05، الموجَة ١٢ G3 · ٢ — C-PAY-EXP-2) ═══
//
// **المَقيس** (القاعِدَة ٣ — يُثَبَّتُ ما يَبقى، ويُطبَعُ ما يَتَغَيَّر): `GatewayInvoiceShare.Validate` يَعُدُّ `Owed` (قائِمَةٌ لَم تُقبَض) وَحدَها، فَأُختٌ في «نِزاع» لا تَمنَعُ
// إلغاءَ «أ» وَحدَها؛ وأَهدافُ «الطَلَبِ كُلِّه» مِن `OwedOnInvoice` أَيضاً — فَتُلغى «أ» وَحدَها والمُلغي يُبقي الفاتورَةَ مَفتوحَةً بِمَبلَغِها كامِلاً (`KeepsOpen` «ب»). فَإن استُؤنِفَت «ب»
// دَفَعَ المُشتَري بِـ«أَكمِل الدَّفع» ثَمَنَ «أ» المُلغاةِ مَعَها ويَنتَظِرُ استِرجاعاً يَدَوِيّاً — يُناقِضُ د2 «مختلطة … الكل أو لا شيء» قَبلَ الدَفع. وصاحِبُ المَتجَرِ يُلغي الطَلَبَ
// كُلَّه فَتَبقى «ب» في «نِزاع» والشَريحَةُ تَقولُ «إلغاء الطَّلَب كامِلاً». وحَسمُ «أ» بِالإلغاءِ وأُختُها في «نِزاع» يَترُكُ الفاتورَةَ كَذلك. واستِئنافٌ بَعدَ `expired_at` يُعيدُ
// صَفقَةً قائِمَةً عَلى فاتورَةٍ أَغلَقَتها البَوّابَة، ولا شَيءَ يُلغيها بَعدَه (رِسالَةُ المُهلَةِ حانَت أَثناءَ النِزاعِ فَتَجاوَزَتها).
// **وما يَبقى**: «ب» لا يُلغيها الطَرَفُ ولا تُلغى بِإلغاءٍ لا يَقصِدُها، ولا `PUT …/cancel` وما زالَت صَفقَةٌ تَدفَعُ لِلفاتورَة، والاستِئنافُ يُرجِعُ «Active» ساعَتَه، وصِفرُ نِداءٍ لِمُزَوِّدِ النُسخَة.

public class DealDisputedSiblingOrderTests(ITestOutputHelper output)
{
    private static Deal Load(World w, Guid id) => w.Docs.Read<Deal>(id.ToString())!;
    private static readonly DealCanceller BuyerCancels = new(Buyer, "مُشتَرٍ", IsStoreAdmin: false);
    private static string[] Sent(World w) => w.Moyasar.Requests.Select(r => r.Method.Method).ToArray();

    /// <summary>سَلَّةُ الصَفقَتَينِ عَلى فاتورَةٍ مَفتوحَةٍ (‏`ExpiredCart` — مُهلَتُها انقَضَت بِتاريخِها المَكتوب)، و«ب» في «نِزاع» بِبابِ صاحِبِ المَتجَر — ثُمَّ ما يُرَدُّ بِه كُلُّ جَلب.</summary>
    private static async Task<(World W, GatewayPayments Gateway, Guid[] Ids, DealCanceller Owner)> DisputedSibling(params string[] replies)
    {
        var (w, gateway, ids) = await GatewayExpiryRaceTests.ExpiredCart();
        var owner = new DealCanceller(w.Docs.Read<ACommerce.Kit.Tenants.Tenant>(Slug)!.OwnerUserId, "مالِك التَّطبيق", IsStoreAdmin: true);
        Assert.True((await w.Deals.DisputeAsync(Slug, ids[1], owner.ActorId, owner.ActorName, "لَم تَصِل")).Ok);
        w.Moyasar.Requests.Clear();
        foreach (var status in replies) w.Moyasar.Then(HttpStatusCode.OK, GatewayExpiryRaceTests.Invoice(ids, status));
        return (w, gateway, ids, owner);
    }

    private string Print(World w, Guid[] ids, string label, string result)
    {
        var line = $"{label}: {result} · أ={Load(w, ids[0]).Status} ب={Load(w, ids[1]).Status} · ميسر [{string.Join(" ", Sent(w))}]";
        output.WriteLine(line);
        return line;
    }

    private static string Of(DealCancelResult r) => r.Ok ? "ok" : r.Violation!.Code;

    /// <summary><b>المُشتَري يُلغي «أ» وَحدَها و«ب» في «نِزاع»</b>: ما يَبقى — «ب» في «نِزاع»، ولا إلغاءَ عِندَ البَوّابَة. ويُطبَعُ ما يَتَغَيَّر.</summary>
    [Fact]
    public async Task A_buyer_cancelling_one_deal_while_its_invoice_sibling_is_disputed_never_cancels_the_invoice()
    {
        var (w, _, ids, _) = await DisputedSibling("initiated");

        var res = await w.Deals.CancelAsync(Slug, ids[0], BuyerCancels, "عَدَلتُ");

        Print(w, ids, "المُشتَري يُلغي «أ» وَحدَها", Of(res));
        Assert.Equal(DealStatus.Disputed, Load(w, ids[1]).Status);
        Assert.DoesNotContain("PUT", Sent(w));
        Assert.Empty(w.Platform.Calls);
    }

    /// <summary><b>المُشتَري يُلغي «الطَلَبَ كامِلاً» و«ب» في «نِزاع»</b>: ما يَبقى — «ب» في «نِزاع» (لا يُلغيها الطَرَف)، ولا إلغاءَ عِندَ البَوّابَة. ويُطبَعُ ما يَتَغَيَّر.</summary>
    [Fact]
    public async Task A_buyer_cancelling_the_whole_order_while_one_deal_is_disputed_never_cancels_the_disputed_deal_or_the_invoice()
    {
        var (w, _, ids, _) = await DisputedSibling("initiated");

        var order = await w.Deals.CancelWholeInvoiceAsync(Slug, ids[0], BuyerCancels, "عَدَلتُ");

        Print(w, ids, "المُشتَري يُلغي الطَلَبَ كامِلاً", $"{Of(order.Result)} أُلغِيَ {order.Cancelled.Count}");
        Assert.Equal(DealStatus.Disputed, Load(w, ids[1]).Status);
        Assert.DoesNotContain("PUT", Sent(w));
    }

    /// <summary><b>صاحِبُ المَتجَرِ يُلغي «الطَلَبَ كامِلاً» مِن «أ» و«ب» في «نِزاع»</b>: ما يَبقى — الإلغاءُ يُقبَلُ و«أ» مُلغاة. ويُطبَعُ ما يَتَغَيَّر: حالُ «ب»، والفاتورَة.</summary>
    [Fact]
    public async Task A_store_cancelling_the_whole_order_from_the_active_deal_cancels_it()
    {
        var (w, _, ids, owner) = await DisputedSibling("initiated", "initiated", MoyasarClient.CanceledStatus);

        var order = await w.Deals.CancelWholeInvoiceAsync(Slug, ids[0], owner, "إلغاء إداريّ");

        Print(w, ids, "صاحِبُ المَتجَرِ يُلغي الطَلَبَ كامِلاً", $"{Of(order.Result)} أُلغِيَ {order.Cancelled.Count}");
        Assert.True(order.Result.Ok, order.Result.Violation?.Code);
        Assert.Equal(DealStatus.Cancelled, Load(w, ids[0]).Status);
    }

    /// <summary><b>حَسمُ «أ» بِالإلغاءِ و«ب» في «نِزاع» أَيضاً</b>: ما يَبقى — «ب» في «نِزاع»، ولا إلغاءَ عِندَ البَوّابَة. ويُطبَعُ ما يَتَغَيَّر.</summary>
    [Fact]
    public async Task A_dispute_cancel_of_one_deal_while_its_invoice_sibling_is_also_disputed_never_cancels_the_invoice()
    {
        var (w, _, ids, owner) = await DisputedSibling("initiated");
        Assert.True((await w.Deals.DisputeAsync(Slug, ids[0], owner.ActorId, owner.ActorName, "لَم تَصِل")).Ok);

        var res = await w.Deals.ResolveDisputeAsync(Slug, ids[0], owner, DealDisputeResolution.Cancel, "حَسم");

        Print(w, ids, "حَسمُ «أ» بِالإلغاءِ و«ب» في نِزاع", res.Ok ? "ok" : res.Violation!.Code);
        Assert.Equal(DealStatus.Disputed, Load(w, ids[1]).Status);
        Assert.DoesNotContain("PUT", Sent(w));
    }

    /// <summary><b>استِئنافُ «ب» بَعدَ انقِضاءِ مُهلَةِ فاتورَتِها ولَم تُدفَع</b>: ما يَبقى — الحَسمُ يُقبَلُ بِالاستِئناف و«ب» قائِمَةٌ ساعَتَه، وتاريخُ فاتورَتِها مَضى، ولا نِداءَ لِلبَوّابَة.
    /// ويُطبَعُ ما يَتَغَيَّر: أَيُطلَبُ إلغاؤها بِالمُهلَة.</summary>
    [Fact]
    public async Task A_dispute_resumed_after_the_invoice_window_closed_returns_the_deal_active_at_once()
    {
        var (w, _, ids, owner) = await DisputedSibling();

        var res = await w.Deals.ResolveDisputeAsync(Slug, ids[1], owner, DealDisputeResolution.Resume, "استِئناف");

        Print(w, ids, "استِئنافُ «ب» بَعدَ المُهلَة", res.Ok ? $"ok {res.Outcome}" : res.Violation!.Code);
        Assert.True(res.Ok, res.Violation?.Code);
        Assert.Equal(DealDisputeResolution.Resume, res.Outcome);
        Assert.Equal(DealStatus.Active, Load(w, ids[1]).Status);
        Assert.True(GatewayReturn.WindowClosed(Load(w, ids[1]), DateTimeOffset.UtcNow));
        Assert.False(DealsService.IsCaptured(Load(w, ids[1])));
        Assert.Empty(w.Moyasar.Requests);
    }
}
