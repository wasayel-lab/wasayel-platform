using ACommerce.Platform.I18n;
using ACommerce.Templates.Customer.Marketplace.Api;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ صَفقَةُ بِطاقَةٍ عَلى مُزَوِّدِ النُسخَةِ بِلا مَرجِعِ دَفع — تَدخُلُ «دَفع» بِلا قَبض — تَوصيفُ «اليَوم» ═══════════════════════════════
//
// مُراجَعَةُ بَوّابَةِ المَوجَة 3b (‏2026-10-13، G5 (xv-g)): `/checkout/submit` عَلى طَريقِ مُزَوِّدِ النُسخَةِ (لا بَوّابَةَ لِلمَتجَر) يَحجِزُ عِندَ
// الوِلادَة؛ وحينَ يَرمي الحَجزُ تُعَلَّمُ الصَفقَةُ `payment_error` بِلا `payment_id` «والمالِكُ يَستَطيعُ رُؤيَتَها يَدَوِيّاً». والقَبضُ عِندَ «دَفع»
// يَتَجاوَزُ صَفقَةً بِلا `payment_id`، وحارِسُ «دَفع» (`AwaitsPayment`) لِطَريقِ البَوّابَةِ وَحدَه — فَتَدخُلُ صَفقَةُ البِطاقَةِ هذِه «دَفع» بِلا مال.
// ولا يَقولُ ‏ADR غَيرَ ذلك (مَقيسٌ بِـ`grep payment_error docs/`: ‏ADR-036 §٨ يَصِفُ تَرتيبَ الكِتابَةِ وَحدَه).
//
// **ما يُقلَبُ بِاسمِه في كوميتِ الإصلاح**: صَفُّ «Today_» أَدناه.
// **قُلِبَ** (‏2026-10-13، كوميتُ «fix(deals)» بَعدَ ‏e1e74ab): `DealsService.MissesPayment` — صَفقَةُ بِطاقَةٍ تاليها «دَفع» ولا `payment_id` عَلَيها —
// تَرُدُّها `AdvanceAsync` بِـ`deal_payment_missing` قَبلَ أَيِّ كِتابَة، بَعدَ حارِسِ البَوّابَة؛ والمَسارُ الإداريُّ والـAPI والصَفحَةُ بِالدالَّةِ نَفسِها.
// وما سِواها بِحَرفِه: صَفقَةٌ عَلَيها حَجزُها تَدخُلُ ويُقبَضُ ما حُجِز، والنَقدُ والعَرضُ بِلا طَريقَةِ دَفعٍ خارِجَها، وصَفقَةُ البَوّابَةِ بِرَمزِها.

public class DealCardWithoutPaymentTests(ITestOutputHelper output)
{
    private const string Slug = "zz-card-nopay";
    private static readonly Guid Buyer  = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
    private static readonly Guid Seller = Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb");

    /// <summary>صَفقَةُ بِطاقَةٍ عِندَ «تَأكيد» — تاليها «دَفع» وفاعِلُه المُشتَري — رَمى حَجزُها عِندَ الوِلادَة: <c>payment_error</c> بِلا <c>payment_id</c>.</summary>
    internal static (DocWorld W, DealsService Deals, DealMoneyClaimFirstTests.RecordingPaymentProvider Pay, Guid Id) LegacyCardDeal(
        bool withPaymentId = false, string? method = CheckoutPayMethods.Card)
    {
        var world = new DocWorld();
        var pay = new DealMoneyClaimFirstTests.RecordingPaymentProvider();
        var deals = new DealsService(world.Store, pay);
        var deal = new Deal
        {
            Id = Guid.NewGuid(), TenantSlug = Slug, Pattern = "marketplace", Stage = DealStage.Confirmed, AmountSar = 100,
            InitiatorId = Buyer, InitiatorName = "مُشتَرٍ", CounterpartyId = Seller, CounterpartyName = "بائِع",
        };
        if (method is not null) deal.Attributes[CheckoutPayMethods.AttributeKey] = method;
        if (withPaymentId) { deal.Refs["payment_id"] = "pay_1"; deal.Refs["payment_status"] = "Authorized"; }
        else deal.Refs["payment_error"] = "provider unavailable";
        world.Put(deal);
        return (world, deals, pay, deal.Id);
    }

    /// <summary><b>قُلِبَ</b> (‏2026-10-13) — كانَ <c>Today_a_card_deal_whose_hold_failed_enters_Paid_without_a_capture</c> (‏<c>e1e74ab</c>):
    /// ««دَفع»: ok=True · المَرحَلَة: Paid · نِداءاتُ المُزَوِّد: 0 · payment_id: False». والآن يُرَدُّ بِرَمزِه قَبلَ أَيِّ كِتابَة، والمَرحَلَةُ «تَأكيد».</summary>
    [Fact]
    public async Task A_card_deal_whose_hold_failed_is_refused_Paid_with_deal_payment_missing_and_nothing_is_written()
    {
        var (w, deals, pay, id) = LegacyCardDeal();
        var res = await deals.AdvanceAsync(Slug, id, Buyer, "مُشتَرٍ");
        var deal = w.Read<Deal>(id.ToString())!;
        output.WriteLine($"«دَفع»: ok={res.Ok} code={res.Code ?? "-"} · المَرحَلَة: {deal.Stage} · نِداءاتُ المُزَوِّد: {pay.Calls.Count} · كِتابَة: {w.Wrote<Deal>()}");

        Assert.False(res.Ok);
        Assert.Equal(DealsService.PaymentMissing, res.Code);
        Assert.Equal(DealStage.Confirmed, deal.Stage);
        Assert.Empty(pay.Calls);
        Assert.Equal(0, w.Wrote<Deal>());
        Assert.Equal(0, w.SaveCalls);
        Assert.False(deal.Refs.ContainsKey("payment_id"));
    }

    /// <summary><b>والسالِبُ بِحَرفِه</b>: صَفقَةُ البِطاقَةِ الَّتي عَلَيها حَجزُها (<c>payment_id</c>) تَدخُلُ «دَفع» ويُقبَضُ ما حُجِزَ مَرَّة — كَما كانَت.</summary>
    [Fact]
    public async Task A_card_deal_carrying_its_hold_still_enters_Paid_and_the_hold_is_captured_once()
    {
        var (w, deals, pay, id) = LegacyCardDeal(withPaymentId: true);
        var res = await deals.AdvanceAsync(Slug, id, Buyer, "مُشتَرٍ");
        var deal = w.Read<Deal>(id.ToString())!;
        output.WriteLine($"«دَفع»: ok={res.Ok} · المَرحَلَة: {deal.Stage} · النِداءات: {string.Join(", ", pay.Calls)} · الحال: {deal.Refs["payment_status"]}");

        Assert.True(res.Ok, res.Reason);
        Assert.Null(res.Code);
        Assert.Equal(DealStage.Paid, deal.Stage);
        Assert.Equal(new[] { "capture pay_1" }, pay.Calls);
        Assert.Equal("Captured", deal.Refs["payment_status"]);
    }

    /// <summary><b>الدالَّةُ النَقِيَّةُ مُوجِباً وسالِباً</b>: تَصدُقُ لِبِطاقَةٍ تاليها «دَفع» ولا مَرجِعَ دَفعٍ عَلَيها (والمَرجِعُ الأَبيضُ غِياب) — ولا تَصدُقُ
    /// لِبِطاقَةٍ بِمَرجِعِها، ولا لِبِطاقَةٍ تاليها غَيرُ «دَفع»، ولا لِلنَقدِ (لا «دَفع» في طَريقِه)، ولا لِصَفقَةٍ بِلا طَريقَةِ دَفع (عَرضٌ عَلى إعلان، بَذرَة).</summary>
    [Theory]
    [InlineData(CheckoutPayMethods.Card,           DealStage.Confirmed, null,    true)]
    [InlineData(CheckoutPayMethods.Card,           DealStage.Confirmed, "  ",    true)]
    [InlineData(CheckoutPayMethods.Card,           DealStage.Confirmed, "pay_1", false)]
    [InlineData(CheckoutPayMethods.Card,           DealStage.Booked,    null,    false)]
    [InlineData(CheckoutPayMethods.Card,           DealStage.Paid,      null,    false)]
    [InlineData(CheckoutPayMethods.CashOnDelivery, DealStage.Confirmed, null,    false)]
    [InlineData(null,                              DealStage.Confirmed, null,    false)]
    public void MissesPayment_holds_only_for_a_card_deal_entering_Paid_without_a_payment_reference(
        string? method, DealStage stage, string? paymentId, bool expected)
    {
        var deal = new Deal { Id = Guid.NewGuid(), Pattern = "marketplace", Stage = stage, InitiatorId = Buyer, CounterpartyId = Seller };
        if (method is not null) deal.Attributes[CheckoutPayMethods.AttributeKey] = method;
        if (paymentId is not null) deal.Refs["payment_id"] = paymentId;
        output.WriteLine($"{method ?? "-"} · {stage} → {DealsPolicy.Next(deal)?.ToString() ?? "-"} · payment_id=«{paymentId ?? "-"}» ⇒ {DealsService.MissesPayment(deal)}");
        Assert.Equal(expected, DealsService.MissesPayment(deal));
    }

    /// <summary><b>وحارِسُ البَوّابَةِ قَبلَه</b>: صَفقَةُ بِطاقَةٍ وُلِدَت لِفاتورَةِ البَوّابَةِ ولا مَرجِعَ دَفعٍ عَلَيها تُجابُ بِرَمزِ البَوّابَة
    /// (<c>deal_awaits_payment</c> — بابُها «أَكمِل الدَّفع») لا بِهذا؛ والصَفحَةُ تَحسُبُ <c>paymentMissing</c> بَعدَ <c>awaitsPayment</c> لا مَعَه.</summary>
    [Fact]
    public async Task A_deal_born_for_the_store_gateway_answers_the_gateway_code_first()
    {
        var (w, deals, pay, id) = LegacyCardDeal();
        var deal = w.Read<Deal>(id.ToString())!;
        deal.Attributes[GatewayPayments.PayViaAttribute] = GatewayPayments.PayViaStoreGateway;
        Assert.True(GatewayPayments.AwaitsPayment(deal));
        Assert.True(DealsService.MissesPayment(deal));

        var res = await deals.AdvanceAsync(Slug, id, Buyer, "مُشتَرٍ");
        output.WriteLine($"«دَفع»: ok={res.Ok} code={res.Code}");
        Assert.Equal(GatewayPayments.DealAwaitsPayment, res.Code);
        Assert.Empty(pay.Calls);
        Assert.Equal(0, w.Wrote<Deal>());
    }

    /// <summary><b>المَسارُ الإداريُّ بِالدالَّةِ نَفسِها</b>: لا يَبلُغُ «دَفع» إلّا ومالِكُ التَطبيقِ مُبادِرُها — فَيُجابُ بِالرَمزِ ويُخفى زِرُّه؛ ولِلرَمزِ نَصٌّ
    /// حَيٌّ في مَعجَمِ الاستوديو. <b>والسالِب</b>: البائِعُ طَرَفُها ومالِكُ التَطبيقِ غَيرُ المُبادِر — «دَفع» خُطوَةُ المُشتَري، رَمزُها كَما كانَ.</summary>
    [Fact]
    public void The_studio_admin_path_answers_deal_payment_missing_and_its_studio_text_lives()
    {
        var owner = Guid.Parse("cccccccc-cccc-4ccc-8ccc-cccccccccccc");
        var ownBuy = new Deal
        {
            Id = Guid.NewGuid(), Pattern = "marketplace", Stage = DealStage.Confirmed, InitiatorId = owner,
            Attributes = { [CheckoutPayMethods.AttributeKey] = CheckoutPayMethods.Card },
        };
        Assert.Equal(DealsService.PaymentMissing, DealsService.AdminPathRefusal(ownBuy, owner));
        Assert.False(DealsService.AdminPathCanMove(ownBuy, owner));
        ownBuy.Refs["payment_id"] = "pay_1";
        Assert.Null(DealsService.AdminPathRefusal(ownBuy, owner));
        // وصَفقَةُ البَوّابَةِ بِرَمزِها قَبلَه — تَرتيبُ `AdvanceAsync` نَفسُه.
        ownBuy.Refs.Remove("payment_id");
        ownBuy.Attributes[GatewayPayments.PayViaAttribute] = GatewayPayments.PayViaStoreGateway;
        Assert.Equal(GatewayPayments.DealAwaitsPayment, DealsService.AdminPathRefusal(ownBuy, owner));

        var (w, _, _, id) = LegacyCardDeal();
        Assert.Equal(DealsService.AdminPathCannotMove, DealsService.AdminPathRefusal(w.Read<Deal>(id.ToString())!, owner));

        var key = DealRefusalTexts.StudioKeyFor(DealsService.PaymentMissing);
        Assert.Equal("studio.deal.err_deal_payment_missing", key);
        var text = LocaleCatalog.Text(LocaleCatalog.Arabic, key!);
        output.WriteLine(text);
        Assert.NotEqual(key, text);
        Assert.False(LocaleCatalog.IsPlaceholder(text));
        Assert.Contains("«دَفع»", text, StringComparison.Ordinal);
    }

    /// <summary><b>والـAPI ‏409 بِرَمزِه</b> — نَسَقُ <c>deal_awaits_payment</c>: رَمزُ الخِدمَةِ يُقرَأُ قَبلَ تَصنيفِ الحالَة، وإلّا قيلَ «فاعِلٌ غَيرُ مُخَوَّل» (‏403)
    /// عَن المُشتَري الَّذي يُدخِلُها «دَفع». <b>والسالِب</b>: رَفضُ فاعِلٍ بِلا رَمزٍ يَبقى ‏403. ولِلرَمزِ نَصٌّ حَيٌّ في صَفحَةِ العُضو.</summary>
    [Fact]
    public void The_API_answers_409_deal_payment_missing_and_the_member_page_has_its_text()
    {
        var (w, _, _, id) = LegacyCardDeal();
        var deal = w.Read<Deal>(id.ToString())!;
        var missing = DealApi.FromAdvance(new DealAdvanceResult(false, deal, "…", DealsService.PaymentMissing));
        Assert.Equal(409, missing.Status);
        Assert.Contains("\"deal_payment_missing\"", ApiJson.Serialize(missing.Payload), StringComparison.Ordinal);
        Assert.DoesNotContain("actor_not_allowed", ApiJson.Serialize(missing.Payload), StringComparison.Ordinal);
        Assert.Equal(DealsService.PaymentMissing, ApiErrorCatalog.DealPaymentMissing);

        var actor = DealApi.FromAdvance(new DealAdvanceResult(false, deal, "يَتَطَلَّب فاعِلاً مِن نَوع: initiator"));
        Assert.Equal(403, actor.Status);

        var key = DealRefusalTexts.KeyFor(DealsService.PaymentMissing);
        Assert.Equal("deals.detail.err_deal_payment_missing", key);
        var text = LocaleCatalog.Text(LocaleCatalog.Arabic, key!);
        output.WriteLine(text);
        Assert.NotEqual(key, text);
        Assert.False(LocaleCatalog.IsPlaceholder(text));
        Assert.Contains("«دَفع»", text, StringComparison.Ordinal);
    }

    /// <summary><b>الصَفحَةُ بِالدالَّةِ الَّتي يَرُدُّ بِها المُحَرِّك</b> (القاعِدَة ١٢): لا بِطاقَةَ «دَورُكَ الآن» ولا «تَأكيد: دَفع» ولا «بِانتِظار الطَّرَف الآخَر» —
    /// فَرعُها قَبلَ فَرعِ الزِرِّ والانتِظار، ونَصُّه مِن <c>DealRefusalTexts.KeyFor</c> لا حَرفٌ في الصَفحَة، ولا يُكَرَّرُ تَحتَ شَريطِ <c>?err=</c>.</summary>
    [Fact]
    public void The_member_page_hides_the_turn_card_and_both_advance_lines_and_says_the_code_to_both_parties()
    {
        var page = File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, "libs", "templates",
            "ACommerce.Templates.Customer.Marketplace", "Components", "Pages", "MyDealDetail.razor"));
        var code = WriteEndpointGuardTests.StripComments(WriteEndpointGuardTests.StripMarkupComments(page));
        Assert.True(code.Length > 1000, "أَداةٌ عَمياء: `MyDealDetail.razor` لَم يُقرَأ.");

        Assert.Contains("@if (deal.Status == DealStatus.Active && shipHeldKey is null && !paymentMissing)", code, StringComparison.Ordinal);
        Assert.Contains("paymentMissing = !awaitsPayment && DealsService.MissesPayment(deal);", code, StringComparison.Ordinal);
        Assert.Contains("@if (DealRefusalTexts.KeyFor(DealsService.PaymentMissing) is { } missingKey && missingKey != DealRefusalTexts.KeyFor(Err))", code, StringComparison.Ordinal);

        var branch = code.IndexOf("else if (paymentMissing)", StringComparison.Ordinal);
        Assert.True(branch > 0, "لا فَرعَ لِصَفقَةِ البِطاقَةِ بِلا مَرجِعِ دَفع.");
        Assert.True(branch > code.IndexOf("else if (canAdvance && awaitsPayment)", StringComparison.Ordinal));
        Assert.True(branch < code.IndexOf("else if (canAdvance && nextStage is { } ns)", StringComparison.Ordinal));
        Assert.True(branch < code.IndexOf("else if (nextStage is { } waiting)", StringComparison.Ordinal));
    }
}
