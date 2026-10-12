using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using Xunit;
using Xunit.Abstractions;
using static ACommerce.Platform.Tests.TenantGatewayEndpointsTests;

namespace ACommerce.Platform.Tests;

// ═══ إلغاءُ الطَلَبِ وفاتورَتُه المَفتوحَةُ عِندَ البَوّابَة (‏ADR-032 الخُطوَة 10، §٨‑هـ) ═══════════════════════════════
//
// **تَوصيفُ «اليَوم» — مُثَبَّتاً قَبلَ قَلبِه** (القاعِدَة ٣؛ ‏2026-10-12، مُراجَعَةُ تَيّارِ S9 الثانِيَة — البَندُ الكَبير):
// البائِعُ المَربوطُ عِندَ الوِلادَةِ يُشعَرُ بِالطَلَبِ في إتمامِ الشِراءِ بَعدَ فَتحِ الفاتورَةِ وقَبلَ أَيِّ دَفع، والمُشتَري ساعَتَها عَلى
// صَفحَةِ البَوّابَةِ المُستَضافَة؛ ويُلغي البائِعُ (أَو المُشتَري أَو صاحِبُ المَتجَر) أَيَّ صَفقَةٍ قائِمَة (`DealCancelAuthorization`).
// و`DealsService.CancelAsync` لا تَسأَلُ البَوّابَةَ عَن الفاتورَةِ ولا تُلغيها — العَميلُ عَمَلِيَّتانِ لا ثالِثَة (§٨‑أ) — فَتَبقى
// الفاتورَةُ قابِلَةً لِلدَّفعِ عِندَ البَوّابَةِ وَلَو أُلغِيَت كُلُّ صَفقَةٍ تَدفَعُ لَها: يَدفَعُها المُشتَري مِن الصَفحَةِ المَفتوحَةِ أَمامَه،
// فَيُؤخَذُ مالُه عَن طَلَبٍ مُلغًى ويُستَرَدُّ يَدَوِيّاً (§٨‑ج.6). ووَثيقَةُ البَوّابَةِ تَعرِفُ عَمَلِيَّةً لِذلك — «Cancel invoice»،
// `PUT /invoices/:id/cancel` (‏`docs.moyasar.com/api/invoices/06-cancel-invoice`، مَقروءَةً ‏2026-10-12).
//
// **ولا مِفتاحَ حَقيقِيّاً** — عالَمُ `TenantGatewayEndpointsTests` بِحَرفِه: صَفقَتانِ لِلمُشتَري عَلى فاتورَةٍ واحِدَة، وبائِعٌ مَربوط،
// ورَبطُ دَفعٍ بِمِفتاحٍ مَختوم، ومُعالِجٌ يَلتَقِطُ كُلَّ طَلَبٍ إلى البَوّابَة.

public class GatewayInvoiceCancelTests(ITestOutputHelper output)
{
    private static Deal Load(World w, Guid id) => w.Docs.Read<Deal>(id.ToString())!;
    private const string VendorReason = "نَفِدَت الكَمِّيَّة";

    /// <summary><b>اليَوم</b>: يُلغي البائِعُ صَفقَتَي الفاتورَةِ واحِدَةً بَعدَ أُخرى — فَلا يُسأَلُ عَن الفاتورَةِ ولا تُلغى بَعدَ الأولى ولا بَعدَ
    /// الأَخيرَة: صِفرُ طَلَبٍ إلى البَوّابَة، ومَرجِعُها باقٍ عَلى الصَفقَتَين، ولا نِداءَ لِمُزَوِّدِ النُسخَة.</summary>
    [Fact]
    public async Task Today_cancelling_every_deal_on_an_open_invoice_asks_the_gateway_nothing_and_leaves_the_invoice_payable()
    {
        var w = await PaidCart(withSeller: true);

        foreach (var id in w.Ids)
        {
            var res = await w.Deals.CancelAsync(Slug, id, new DealCanceller(Seller, "بائِع", IsStoreAdmin: false), VendorReason);
            Assert.True(res.Ok, res.Violation?.Code);
            Assert.Empty(w.Moyasar.Requests);   // ← لا سُؤالَ عَن الفاتورَةِ ولا إلغاءَ لَها عِندَ البَوّابَة
        }

        Assert.All(w.Ids, id => Assert.Equal(DealStatus.Cancelled, Load(w, id).Status));
        Assert.All(w.Ids, id => Assert.True(GatewayPayments.HasInvoice(Load(w, id))));
        Assert.Empty(w.Platform.Calls);
        output.WriteLine($"· إلغاءُ صَفقَتَي الفاتورَة: نِداءاتُ البَوّابَة={w.Moyasar.Requests.Count} · مُزَوِّدُ النُسخَة={w.Platform.Calls.Count}" +
                         " · الفاتورَةُ عِندَ البَوّابَة: لَم تُمَسّ");
    }
}
