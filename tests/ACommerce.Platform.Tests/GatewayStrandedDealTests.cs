using System.Text.Json;
using ACommerce.Kit.Payments;
using ACommerce.Platform.I18n;
using ACommerce.Templates.Customer.Marketplace.Services;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;
using static ACommerce.Platform.Tests.TenantGatewayEndpointsTests;

namespace ACommerce.Platform.Tests;

// ═══ صَفقَةُ بِطاقَةٍ عَلى طَريقِ بَوّابَةِ المَتجَرِ تَبقى قائِمَةً بِلا فاتورَة — ما يَمنَعُها مِن «دَفع» — تَوصيفُ «اليَوم» ═══
//
// مُراجَعَةُ بَوّابَةِ المَوجَة 3b (‏2026-10-13، G5 (xvi-g) — خَطَرُ مالٍ أَوَّلاً): `GatewayPayments.OpenInvoiceAsync` تُلغي صَفَقاتِ السَلَّةِ
// حينَ لا تُفتَحُ الفاتورَة **وتُهمِلُ نَتيجَةَ كُلِّ إلغاء** — فَإلغاءٌ يَرُدُّه `deal_changed_meanwhile` (البائِعُ المَربوطُ عِندَ الوِلادَةِ قَبِلَ
// بَينَ قِراءَةِ الإلغاءِ وحِفظِه) يَترُكُ صَفقَةَ بِطاقَةٍ قائِمَةً بِلا فاتورَةٍ ولا `payment_id`. وحارِسُ «دَفع» (`AwaitsPayment`) يَسأَلُ
// مَرجِعَ الفاتورَةِ وَحدَه، والقَبضُ عِندَ «دَفع» يَتَجاوَزُ صَفقَةً بِلا مَرجِعِ دَفع — فَتَدخُلُ «دَفع» ثُمَّ «شَحن» بِلا مال. والحالُ نَفسُها
// بِعُطلٍ (انقِطاعِ العَمَلِيَّة) بَينَ وِلادَةِ الصَفقَةِ وإلحاقِ `gateway_invoice_id` بِها.
//
// **عالَمُ الوَثائِقِ يَحفَظُ عِندَ `Store` لا عِندَ الحِفظ**، فَالتَعارُضُ يُحاكى كَما يَقَعُ في Marten: الوَثيقَةُ المُخَزَّنَةُ عِندَ الحِفظِ المَردودِ
// هي ما كَتَبَه البائِعُ (قَبولٌ إلى «حَجز»)، لا الإلغاءُ الَّذي لَم يُحفَظ.
//
// **ما يُقلَبُ بِاسمِه في كوميتِ الإصلاح**: صَفُّ «Today_» أَدناه.

/// <summary>لوغُ خِدمَةِ البَوّابَةِ بِمُستَواه ونَصِّه — «يُكتَبُ بِصَوت» يُقاسُ بِالمُستَوى لا بِالنَصّ.</summary>
internal sealed class LevelCapturingGatewayLogger : ILogger<GatewayPayments>
{
    public List<(LogLevel Level, string Line)> Entries { get; } = new();
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter) => Entries.Add((logLevel, formatter(state, exception)));
}

public class GatewayStrandedDealTests(ITestOutputHelper output)
{
    private const string ReturnUrl = "https://store.example/zz-gateway/pay/return?deal=1";
    private static Deal Load(World w, Guid id) => w.Docs.Read<Deal>(id.ToString())!;
    private static L Fresh() => new(new HttpContextAccessor());
    private static Deal Copy(Deal d) => JsonSerializer.Deserialize<Deal>(JsonSerializer.Serialize(d))!;

    /// <summary>
    /// <para><b>البائِعُ يَقبَلُ بَينَ قِراءَةِ الإلغاءِ وحِفظِه — <paramref name="times"/> مَرّات</b>: عِندَ كُلِّ حِفظٍ مُسَلَّحٍ تَصيرُ الوَثيقَةُ
    /// المُخَزَّنَةُ ما كانَت قَبلَ الإلغاءِ وقَد قُبِلَت (‏<c>Booked</c> بِسَطرِ البائِع)، ويَرمي الحِفظُ ما يَرميه Marten لِنُسخَةٍ قَديمَة.</para>
    /// </summary>
    private static void VendorAcceptsDuringCancels(World w, Guid id, int times)
    {
        var left = times;
        w.Docs.BeforeMember = name =>
        {
            if (name != "SaveChangesAsync" || left == 0) return;
            if (Load(w, id) is not { Status: DealStatus.Cancelled } cancelled) return;
            left--;
            var accepted = Copy(cancelled);
            accepted.Status = DealStatus.Active;
            accepted.CancelReason = null;
            accepted.Timeline.RemoveAt(accepted.Timeline.Count - 1);
            if (accepted.Stage == DealStage.Offered)
            {
                accepted.Timeline.Add(new(DealStage.Offered, DealStage.Booked, "advanced", Seller, "بائِع", null, DateTime.UtcNow));
                accepted.Stage = DealStage.Booked;
            }
            w.Docs.Put(accepted);
            throw new JasperFx.ConcurrencyException(typeof(Deal), id);
        };
    }

    /// <summary>خِدمَةُ بَوّابَةٍ بِلا عَميلٍ — الفَتحُ يُرَدُّ بِـ<see cref="MoyasarFailures.NotConfigured"/> بِلا نِداء، فَيَبلُغُ فَرعَ الإلغاء.</summary>
    private static (GatewayPayments Gateway, LevelCapturingGatewayLogger Log) Unopenable(World w)
    {
        var log = new LevelCapturingGatewayLogger();
        return (new GatewayPayments(w.Deals, new TenantProviderService(w.Docs.Store), NewVault(), client: null, log), log);
    }

    [Fact]
    public async Task Today_a_cancel_refused_after_the_invoice_failed_to_open_leaves_a_card_deal_that_enters_Paid_and_Shipping_without_money()
    {
        var w = await PaidCart(withSeller: true, invoice: false);
        var (gateway, log) = Unopenable(w);
        var stranded = w.Ids[0];
        VendorAcceptsDuringCancels(w, stranded, times: 1);

        var opened = await gateway.OpenInvoiceAsync(Slug, new DealPayer(Buyer), w.Ids, "مَتجَر", ReturnUrl, Fresh());
        var deal = Load(w, stranded);
        output.WriteLine($"الفَتح: {opened.Code} · العالِقَة: {deal.Status}/{deal.Stage} · فاتورَة: {GatewayPayments.HasInvoice(deal)} · " +
                         $"payment_id: {deal.Refs.ContainsKey("payment_id")} · AwaitsPayment: {GatewayPayments.AwaitsPayment(deal)} · " +
                         $"أُختُها: {Load(w, w.Ids[1]).Status} · سُطورٌ بِصَوت: {log.Entries.Count(e => e.Level >= LogLevel.Error)}");

        Assert.Equal(MoyasarFailures.NotConfigured, opened.Code);
        Assert.Equal((DealStatus.Active, DealStage.Booked), (deal.Status, deal.Stage));
        Assert.False(GatewayPayments.HasInvoice(deal));
        Assert.False(deal.Refs.ContainsKey("payment_id"));
        Assert.False(GatewayPayments.AwaitsPayment(deal));
        Assert.Equal(DealStatus.Cancelled, Load(w, w.Ids[1]).Status);
        Assert.DoesNotContain(log.Entries, e => e.Level >= LogLevel.Error);

        Assert.True((await w.Deals.AdvanceAsync(Slug, stranded, Buyer, "مُشتَرٍ")).Ok);    // ← «تَأكيد»
        var paid = await w.Deals.AdvanceAsync(Slug, stranded, Buyer, "مُشتَرٍ");             // ← «دَفع» بِلا مال
        var shipped = await w.Deals.AdvanceAsync(Slug, stranded, Seller, "بائِع");           // ← «شَحن»
        output.WriteLine($"«دَفع»: {paid.Ok} · «شَحن»: {shipped.Ok} · المَرحَلَة: {Load(w, stranded).Stage} · نِداءاتُ الدَفع: {w.Platform.Calls.Count}");

        Assert.True(paid.Ok);
        Assert.True(shipped.Ok);
        Assert.Equal(DealStage.Shipping, Load(w, stranded).Stage);
        Assert.Empty(w.Platform.Calls);
        Assert.False(Load(w, stranded).Refs.ContainsKey("payment_id"));
    }
}
