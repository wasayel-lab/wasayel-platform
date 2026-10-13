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
//
// **قُلِبَ** (‏2026-10-13، كوميتُ «fix(payments)» بَعدَ ‏64b6e5c): `/checkout/submit` يُولِدُ صَفقَةَ طَريقِ البَوّابَةِ بِعَلامَتِها
// (`GatewayPayments.MarkedAtBirth`) فَيَصدُقُ `AwaitsPayment` قَبلَ أَيِّ فاتورَة؛ والإلغاءُ بَعدَ فَشَلِ الفَتحِ يُقرَأُ — يُعادُ مَرَّةً عَلى
// `deal_changed_meanwhile`، وما بَقِيَ (أَو رَمى) سَطرُ خَطَأٍ يُسَمّي الصَفقَة. ومَعَه صُفوفُ العَلامَةِ والحارِسِ مُوجَباً وسالِباً، وبابُ الصَفحَة.

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

    /// <summary><b>قُلِبَ</b> (‏2026-10-13) — كانَ <c>Today_a_cancel_refused_after_the_invoice_failed_to_open_leaves_a_card_deal_that_enters_Paid_and_Shipping_without_money</c>
    /// (‏<c>64b6e5c</c>): الإلغاءُ المَردودُ بِتَعارُضٍ يُعادُ مَرَّةً عَلى ما كَتَبَه البائِعُ فَيُلغى — لا صَفقَةَ تَبقى قائِمَة، ولا سَطرَ بِصَوت،
    /// والصَفقَةُ لا تَتَقَدَّمُ إلى شَيء.</summary>
    [Fact]
    public async Task A_cancel_refused_once_after_the_invoice_failed_to_open_is_retried_and_no_card_deal_is_left_active()
    {
        var w = await PaidCart(withSeller: true, invoice: false, bornForGateway: true);
        var (gateway, log) = Unopenable(w);
        var stranded = w.Ids[0];
        VendorAcceptsDuringCancels(w, stranded, times: 1);

        var opened = await gateway.OpenInvoiceAsync(Slug, new DealPayer(Buyer), w.Ids, "مَتجَر", ReturnUrl, Fresh());
        var deal = Load(w, stranded);
        output.WriteLine($"الفَتح: {opened.Code} · الأولى: {deal.Status}/{deal.Stage} · أُختُها: {Load(w, w.Ids[1]).Status} · " +
                         $"سُطورٌ بِصَوت: {log.Entries.Count(e => e.Level >= LogLevel.Error)}");

        Assert.Equal(MoyasarFailures.NotConfigured, opened.Code);
        Assert.Equal((DealStatus.Cancelled, DealStage.Booked), (deal.Status, deal.Stage));   // أُلغِيَت بَعدَ قَبولِ البائِع
        Assert.Equal(DealStatus.Cancelled, Load(w, w.Ids[1]).Status);
        Assert.DoesNotContain(log.Entries, e => e.Level >= LogLevel.Error);
        Assert.False((await w.Deals.AdvanceAsync(Slug, stranded, Buyer, "مُشتَرٍ")).Ok);
        Assert.Empty(w.Platform.Calls);
    }

    /// <summary>تَعارُضانِ مُتَتالِيان: الإعادَةُ مَرَّةٌ لا أَكثَر، وما بَقِيَ سَطرُ خَطَأٍ يُسَمّي الصَفقَةَ ورَمزَها — <b>وعَلامَةُ الوِلادَةِ تُمسِكُها</b>:
    /// تَتَقَدَّمُ إلى «تَأكيد» ولا تَدخُلُ «دَفع» (<c>deal_awaits_payment</c>)، ولا نِداءَ دَفع. وأُختُها تُلغى.</summary>
    [Fact]
    public async Task A_cancel_refused_twice_is_logged_as_an_error_naming_the_deal_which_its_birth_mark_holds_out_of_Paid()
    {
        var w = await PaidCart(withSeller: true, invoice: false, bornForGateway: true);
        var (gateway, log) = Unopenable(w);
        var stranded = w.Ids[0];
        VendorAcceptsDuringCancels(w, stranded, times: 2);

        await gateway.OpenInvoiceAsync(Slug, new DealPayer(Buyer), w.Ids, "مَتجَر", ReturnUrl, Fresh());
        var deal = Load(w, stranded);
        var loud = log.Entries.Where(e => e.Level == LogLevel.Error).ToList();
        output.WriteLine($"العالِقَة: {deal.Status}/{deal.Stage} · AwaitsPayment: {GatewayPayments.AwaitsPayment(deal)} · أُختُها: {Load(w, w.Ids[1]).Status} · " +
                         $"خَطَأ: {loud.Count} «{loud.FirstOrDefault().Line}»");

        Assert.Equal((DealStatus.Active, DealStage.Booked), (deal.Status, deal.Stage));
        Assert.Equal(DealStatus.Cancelled, Load(w, w.Ids[1]).Status);
        var line = Assert.Single(loud).Line;
        Assert.Contains(stranded.ToString(), line, StringComparison.Ordinal);
        Assert.Contains(DealsService.ChangedMeanwhile, line, StringComparison.Ordinal);
        Assert.False(GatewayPayments.HasInvoice(deal));
        Assert.True(GatewayPayments.BornForGateway(deal));

        Assert.True((await w.Deals.AdvanceAsync(Slug, stranded, Buyer, "مُشتَرٍ")).Ok);    // ← «تَأكيد»
        Assert.True(GatewayPayments.AwaitsPayment(Load(w, stranded)));
        var paid = await w.Deals.AdvanceAsync(Slug, stranded, Buyer, "مُشتَرٍ");
        output.WriteLine($"«دَفع»: {paid.Ok} {paid.Code} · المَرحَلَة: {Load(w, stranded).Stage} · نِداءاتُ الدَفع: {w.Platform.Calls.Count}");
        Assert.False(paid.Ok);
        Assert.Equal(GatewayPayments.DealAwaitsPayment, paid.Code);
        Assert.Equal(DealStage.Confirmed, Load(w, stranded).Stage);
        Assert.Empty(w.Platform.Calls);
    }

    /// <summary><b>العُطلُ بَينَ الوِلادَةِ والإلحاق</b>: صَفقَةٌ وُلِدَت بِعَلامَتِها ولَم يَبلُغها فَتحٌ ولا إلغاء — لا تَدخُلُ «دَفع». والسالِبُ أُختُها
    /// بِلا عَلامَة: الطَريقُ الآخَرُ (حَجزُ مُزَوِّدِ النُسخَة) كَما كانَ بِحَرفِه — تَدخُلُها ويَقبِضُ القَبضُ ما حُجِز.</summary>
    /// <remarks><b>قُلِبَ بِاسمِه</b> (‏2026-10-13، G5 (xv-g)؛ ولا يُعادُ حَرفٌ فَوقَ ما سَبَق) — كانَ
    /// <c>A_deal_born_for_the_gateway_that_never_got_its_invoice_cannot_enter_Paid_and_an_unmarked_one_still_can</c>: أُختُها بِلا عَلامَةٍ لَم يَكُن عَلَيها
    /// حَجز، فَكانَ الصَفُّ يُثبِتُ أَنّ صَفقَةَ بِطاقَةٍ بِلا مَرجِعِ دَفعٍ تَدخُلُ «دَفع» — وصارَت تُرَدُّ بِـ<c>deal_payment_missing</c>
    /// (<c>DealCardWithoutPaymentTests</c>). فَالأُختُ الآنَ بِحَجزِها كَما يَكتُبُه إتمامُ الشِراءِ عَلى ذلكَ الطَريق، ويُقاسُ القَبضُ الَّذي وَصَفَه الرَأسُ ولَم يَقِسه.</remarks>
    [Fact]
    public async Task A_deal_born_for_the_gateway_that_never_got_its_invoice_cannot_enter_Paid_and_an_unmarked_one_with_its_hold_still_can()
    {
        foreach (var marked in new[] { true, false })
        {
            var w = await PaidCart(withSeller: true, invoice: false, bornForGateway: marked);
            var id = w.Ids[0];
            if (!marked)
            {
                await w.Deals.AttachRefAsync(Slug, id, "payment_id", "pay_held");
                await w.Deals.AttachRefAsync(Slug, id, "payment_status", "Authorized");
            }
            Assert.True((await w.Deals.AdvanceAsync(Slug, id, Seller, "بائِع")).Ok);    // ← «حَجز»
            Assert.True((await w.Deals.AdvanceAsync(Slug, id, Buyer, "مُشتَرٍ")).Ok);   // ← «تَأكيد»
            var paid = await w.Deals.AdvanceAsync(Slug, id, Buyer, "مُشتَرٍ");
            output.WriteLine($"عَلامَة: {marked} ⇒ «دَفع»: {paid.Ok} {paid.Code} · نِداءاتُ مُزَوِّدِ النُسخَة: {string.Join(", ", w.Platform.Calls)}");
            Assert.Equal(!marked, paid.Ok);
            Assert.Equal(marked ? GatewayPayments.DealAwaitsPayment : null, paid.Code);
            Assert.Equal(marked ? Array.Empty<string>() : new[] { "capture pay_held" }, w.Platform.Calls);
        }
    }

    /// <summary>إلغاءٌ يَرمي (عُطلُ قاعِدَةٍ عِندَ الكِتابَة) لا يَصعَدُ مِن الفَتحِ ولا يَترُكُ أُختَه: سَطرُ خَطَأٍ بِالصَفقَةِ ونَوعِ الاستِثناء، والأُختُ تُلغى.</summary>
    [Fact]
    public async Task A_cancel_that_throws_is_logged_as_an_error_and_the_rest_of_the_cart_is_still_cancelled()
    {
        var w = await PaidCart(withSeller: true, invoice: false, bornForGateway: true);
        var (gateway, log) = Unopenable(w);
        var stranded = w.Ids[0];
        var armed = true;
        w.Docs.BeforeMember = name =>
        {
            if (name != "Store" || !armed || Load(w, stranded) is not { Status: DealStatus.Cancelled } cancelled) return;
            armed = false;
            var kept = Copy(cancelled);
            kept.Status = DealStatus.Active;
            kept.CancelReason = null;
            kept.Timeline.RemoveAt(kept.Timeline.Count - 1);
            w.Docs.Put(kept);   // ‏لا كِتابَة: الحالُ ما كانَت قَبلَ الإلغاء
            throw new InvalidOperationException("db down");
        };

        var opened = await gateway.OpenInvoiceAsync(Slug, new DealPayer(Buyer), w.Ids, "مَتجَر", ReturnUrl, Fresh());
        var loud = log.Entries.Where(e => e.Level == LogLevel.Error).ToList();
        output.WriteLine($"الفَتح: {opened.Code} · الأولى: {Load(w, stranded).Status} · أُختُها: {Load(w, w.Ids[1]).Status} · خَطَأ: «{loud.FirstOrDefault().Line}»");

        Assert.Equal(MoyasarFailures.NotConfigured, opened.Code);
        Assert.Equal(DealStatus.Active, Load(w, stranded).Status);
        Assert.Equal(DealStatus.Cancelled, Load(w, w.Ids[1]).Status);
        var line = Assert.Single(loud).Line;
        Assert.Contains(stranded.ToString(), line, StringComparison.Ordinal);
        Assert.Contains(nameof(InvalidOperationException), line, StringComparison.Ordinal);
    }

    /// <summary>العَلامَةُ وحارِسُها — مُوجَبٌ وسالِب: تُضافُ بِالبَوّابَةِ وَحدَها (والقاموسُ نَفسُه يُعاد)، وتُقرَأُ بِقيمَتِها وَحدَها؛ والحارِسُ يَصدُقُ
    /// لَها وتاليها «دَفع» ولَم يُقبَض — ولا يَصدُقُ قَبلَ «تَأكيد»، ولا بَعدَ القَبض، ولا لِصَفقَةِ نَقدٍ (لا «دَفع» في طَريقِها)، ولا بِلا عَلامَةٍ ولا فاتورَة.</summary>
    [Fact]
    public void The_birth_mark_and_the_Paid_guard_positive_and_negative()
    {
        var plain = new Dictionary<string, string> { [CheckoutPayMethods.AttributeKey] = CheckoutPayMethods.Card };
        Assert.Same(plain, GatewayPayments.MarkedAtBirth(plain, viaGateway: false));
        Assert.Single(plain);
        Assert.Same(plain, GatewayPayments.MarkedAtBirth(plain, viaGateway: true));
        Assert.Equal(GatewayPayments.PayViaStoreGateway, plain[GatewayPayments.PayViaAttribute]);

        static Deal At(DealStage stage, string? via, string method = CheckoutPayMethods.Card, bool captured = false)
        {
            var d = new Deal { Id = Guid.NewGuid(), Pattern = "marketplace", Stage = stage, Status = DealStatus.Active, InitiatorId = Buyer, AmountSar = 100m };
            d.Attributes[CheckoutPayMethods.AttributeKey] = method;
            if (via is not null) d.Attributes[GatewayPayments.PayViaAttribute] = via;
            if (captured) { d.Refs["payment_id"] = "x"; d.Refs["payment_status"] = nameof(PaymentStatus.Captured); }
            return d;
        }

        Assert.True(GatewayPayments.BornForGateway(At(DealStage.Confirmed, GatewayPayments.PayViaStoreGateway)));
        Assert.False(GatewayPayments.BornForGateway(At(DealStage.Confirmed, "Store_Gateway")));
        Assert.False(GatewayPayments.BornForGateway(At(DealStage.Confirmed, "")));
        Assert.False(GatewayPayments.BornForGateway(At(DealStage.Confirmed, null)));

        Assert.True(GatewayPayments.AwaitsPayment(At(DealStage.Confirmed, GatewayPayments.PayViaStoreGateway)));
        Assert.False(GatewayPayments.AwaitsPayment(At(DealStage.Booked, GatewayPayments.PayViaStoreGateway)));
        Assert.False(GatewayPayments.AwaitsPayment(At(DealStage.Confirmed, GatewayPayments.PayViaStoreGateway, captured: true)));
        Assert.False(GatewayPayments.AwaitsPayment(At(DealStage.Confirmed, GatewayPayments.PayViaStoreGateway, CheckoutPayMethods.CashOnDelivery)));
        Assert.False(GatewayPayments.AwaitsPayment(At(DealStage.Confirmed, null)));
    }

    /// <summary>البابانِ يَقرَآنِ ما تَقرَؤُه الخِدمَة: النُقطَةُ تُولِدُ بِالعَلامَةِ بِـ<c>viaGateway</c> نَفسِه، والصَفحَةُ تَرسُمُ «أَكمِلِ الدَّفع» لِما عَلَيه فاتورَةٌ
    /// وَحدَه وسَطرَ الحالِ لِغَيرِه — ونَصُّه حَيّ.</summary>
    [Fact]
    public void The_checkout_births_with_the_mark_and_the_member_page_draws_the_pay_door_only_over_an_invoice()
    {
        string Code(params string[] parts) => WriteEndpointGuardTests.StripComments(WriteEndpointGuardTests.StripMarkupComments(File.ReadAllText(
            Path.Combine(new[] { ThemeZeroEquivalenceTests.RepoRoot, "libs", "templates", "ACommerce.Templates.Customer.Marketplace" }.Concat(parts).ToArray()))));

        var host = Code("MarketplaceTemplateExtensions.cs");
        var birth = host.IndexOf("attributes: Services.Deals.GatewayPayments.MarkedAtBirth(new()", StringComparison.Ordinal);
        var close = host.IndexOf("}, viaGateway));", birth, StringComparison.Ordinal);
        Assert.True(birth > 0 && close > birth, $"birth={birth} close={close}");
        Assert.True(host.IndexOf("var viaGateway =", StringComparison.Ordinal) is var decl && decl > 0 && decl < birth);

        var member = Code("Components", "Pages", "MyDealDetail.razor");
        var door = member.IndexOf("else if (canAdvance && awaitsPayment)", StringComparison.Ordinal);
        var held = member.IndexOf("else if (canAdvance && heldByCarrier)", StringComparison.Ordinal);
        var branch = member[door..held];
        var invoice = branch.IndexOf("@if (GatewayPayments.HasInvoice(deal))", StringComparison.Ordinal);
        var link = branch.IndexOf("deals.detail.complete_payment", StringComparison.Ordinal);
        var missing = branch.IndexOf("L[\"deals.detail.gateway_invoice_missing\"]", StringComparison.Ordinal);
        Assert.True(invoice > 0 && link > invoice && missing > link, $"invoice={invoice} link={link} missing={missing}");

        var text = LocaleCatalog.Text(LocaleCatalog.Arabic, "deals.detail.gateway_invoice_missing");
        Assert.NotEqual("deals.detail.gateway_invoice_missing", text);
        Assert.False(LocaleCatalog.IsPlaceholder(text));
        output.WriteLine($"النَصّ: {text}");
    }
}
