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

    /// <summary><b>قُلِبَ بِاسمِه</b> (‏2026-10-29، قَرارُ المالِك د8 (٢) «أ»؛ تَوصيفُه <c>05e2e13</c>) — كانَ
    /// <c>Today_a_hold_failure_at_checkout_marks_the_deal_and_the_cart_goes_on_while_only_the_gateway_path_cancels</c>: فَرعُ الرَميِ يَكتُبُ <c>payment_error</c>
    /// ويَمضي، وثَلاثَةُ مَساراتِ رَدّ. <b>والآن سُلوكٌ واحِدٌ في الطَريقَين</b>: الفَرعُ يَرفَعُ عَلَمَه، والحَلقَةُ تَقِفُ قَبلَ رَبطِ البائِع، وبَعدَها — قَبلَ الفاتورَةِ وقَبلَ
    /// أَيِّ إشعار — تُلغى صَفَقاتُ السَلَّةِ بِـ<see cref="GatewayPayments.CancelUnsecuredAsync"/>، والسَلَّةُ تُحفَظُ بِنُسخَةٍ جَديدَة، والرُجوعُ بِـ<see cref="CheckoutPayMethods.HoldFailed"/>
    /// (أَربَعَةُ مَساراتِ رَدّ).</summary>
    [Fact]
    public void A_hold_failure_at_checkout_stops_the_loop_cancels_the_cart_deals_keeps_the_cart_and_answers_its_code()
    {
        var src = File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, "libs", "templates",
            "ACommerce.Templates.Customer.Marketplace", "MarketplaceTemplateExtensions.cs"));
        var start = src.IndexOf("\"/{slug}/checkout/submit\"", StringComparison.Ordinal);
        Assert.True(start > 0, "أَداةٌ عَمياء: النُقطَةُ غَيرُ مَوجودَة.");
        var body = WriteEndpointGuardTests.StripComments(src[start..src.IndexOf(".RequireStoreWritable();", start, StringComparison.Ordinal)]);
        Assert.True(body.Length > 2000, $"أَداةٌ عَمياء: جِسمُ النُقطَةِ {body.Length} مِحرَفاً.");

        const string mark = "await deals.AttachRefAsync(slug, deal.Id, \"payment_error\", ex.Message);";
        var catchAt = body.IndexOf("catch (Exception ex)", StringComparison.Ordinal);
        var markAt = body.IndexOf(mark, catchAt, StringComparison.Ordinal);
        var after = body[(markAt + mark.Length)..].TrimStart();
        var stop = body.IndexOf("if (holdFailed) break;", StringComparison.Ordinal);
        var bind = body.IndexOf("await deals.BindListingOwnerAsync(slug, deal.Id, oid);", StringComparison.Ordinal);
        var cancel = body.IndexOf("await gateway.CancelUnsecuredAsync(slug, new Services.Deals.DealPayer(userId), created, l[\"checkout.payment.cancel_hold_failed\"]);", StringComparison.Ordinal);
        var keep = body.IndexOf("cart.UpdatedAt = DateTime.UtcNow; s.Store(cart); await s.SaveChangesAsync();", cancel, StringComparison.Ordinal);
        var back = body.IndexOf("RefusalPath(Services.Deals.CheckoutPayMethods.HoldFailed)", StringComparison.Ordinal);
        var open = body.IndexOf("await gateway.OpenInvoiceAsync(", StringComparison.Ordinal);
        var notify = body.IndexOf("await notices.NotifyNewDealsAsync(slug, born, l);", StringComparison.Ordinal);
        output.WriteLine($"catch={catchAt} · payment_error={markAt} · ما يَليه: «{after[..18]}» · وُقوف={stop} · رَبط={bind} · إلغاء={cancel} · سَلَّة={keep} · رَدّ={back} · فاتورَة={open} · إشعار={notify} · RefusalPath: {Count(body, "RefusalPath(")}");

        Assert.True(catchAt > 0 && markAt > catchAt, $"فَرعُ الرَميِ: catch={catchAt} mark={markAt}");
        Assert.StartsWith("holdFailed = true;", after, StringComparison.Ordinal);
        Assert.True(stop > markAt && bind > stop, $"الحَلقَةُ تَقِفُ قَبلَ رَبطِ البائِع: stop={stop} bind={bind}");
        Assert.True(cancel > bind && keep > cancel && back > keep && open > back && notify > open,
            $"بَعدَ الحَلقَة: إلغاء={cancel} سَلَّة={keep} رَدّ={back} فاتورَة={open} إشعار={notify}");
        Assert.Equal(1, Count(body, "break;"));
        Assert.Equal(4, Count(body, "RefusalPath("));
    }

    /// <summary><b>المُوجِب — صَفَقاتُ السَلَّةِ تُلغى بِاسمِ المُشتَري وبِسَبَبِها، وما نَجَحَ حَجزُه يُفَكُّ مَرَّةً بِالكاتِبِ القائِم، ولا يُخبَرُ أَحَد</b>
    /// (‏2026-10-29، قَرارُ المالِك د8 (٢)): صَفقَةٌ حَجَزَت (<c>payment_id</c>) وصَفقَةٌ رَمى حَجزُها (<c>payment_error</c>) — كِلتاهُما «مُلغاة»، ونِداءُ مُزَوِّدٍ واحِد:
    /// استِرجاعُ المَحجوزَةِ وَحدَها؛ وصِفرُ وَثيقَةِ إشعار.</summary>
    [Fact]
    public async Task CancelUnsecuredAsync_cancels_the_buyers_cart_deals_releases_the_held_amount_once_and_tells_nobody()
    {
        var (w, deals, pay, held) = LegacyCardDeal(withPaymentId: true);
        var failed = Guid.NewGuid();
        var other = w.Read<Deal>(held.ToString())!;
        w.Put(new Deal
        {
            Id = failed, TenantSlug = Slug, Pattern = "marketplace", Stage = DealStage.Offered, AmountSar = 40,
            InitiatorId = Buyer, InitiatorName = "مُشتَرٍ", Attributes = { [CheckoutPayMethods.AttributeKey] = CheckoutPayMethods.Card },
            Refs = { ["payment_error"] = "provider unavailable" },
        });
        var gateway = new GatewayPayments(deals, new ACommerce.Templates.Customer.Marketplace.Services.TenantProviderService(w.Store),
            TenantGatewayEndpointsTests.NewVault());
        var reason = new L(new Microsoft.AspNetCore.Http.HttpContextAccessor())["checkout.payment.cancel_hold_failed"];
        Assert.NotEqual("checkout.payment.cancel_hold_failed", reason);

        await gateway.CancelUnsecuredAsync(Slug, new DealPayer(Buyer), new[] { other.Id, failed }, reason);

        foreach (var id in new[] { held, failed })
        {
            var d = w.Read<Deal>(id.ToString())!;
            output.WriteLine($"{id:N}: {d.Status} · «{d.CancelReason}» · {d.Timeline[^1].Action}");
            Assert.Equal(DealStatus.Cancelled, d.Status);
            Assert.Equal(reason, d.CancelReason);
            Assert.Contains(d.Timeline, e => e.Action == "cancelled" && e.ActorId == Buyer);
        }
        Assert.Equal(new[] { "refund pay_1" }, pay.Calls);
        Assert.Equal("ref_pay_1", w.Read<Deal>(held.ToString())!.Refs["refund_id"]);
        Assert.Equal(0, w.Wrote<ACommerce.Kit.Notifications.Notification>());
    }

    /// <summary><b>السالِب — صَفقَةُ غَيرِ المُشتَري، وما لَم يَعُد قائِماً، لا يُمَسّ</b>: مُعَرِّفُ صَفقَةِ مُشتَرٍ آخَرَ في القائِمَةِ يَبقى قائِماً، وصَفقَةٌ مُلغاةٌ مِن قَبلُ لا يُكتَبُ
    /// عَلَيها سَطرٌ ثانٍ، ومُعَرِّفٌ لا صَفقَةَ لَه يُتَجاوَز — صِفرُ نِداءِ مُزَوِّد.</summary>
    [Fact]
    public async Task CancelUnsecuredAsync_leaves_another_buyers_deal_and_an_already_cancelled_deal_untouched()
    {
        var (w, deals, pay, id) = LegacyCardDeal(withPaymentId: true);
        var theirs = w.Read<Deal>(id.ToString())!;
        theirs.InitiatorId = Seller;
        w.Put(theirs);
        var gone = Guid.NewGuid();
        w.Put(new Deal
        {
            Id = gone, TenantSlug = Slug, Pattern = "marketplace", Stage = DealStage.Offered, AmountSar = 10, Status = DealStatus.Cancelled,
            InitiatorId = Buyer, InitiatorName = "مُشتَرٍ",
        });
        var gateway = new GatewayPayments(deals, new ACommerce.Templates.Customer.Marketplace.Services.TenantProviderService(w.Store),
            TenantGatewayEndpointsTests.NewVault());

        await gateway.CancelUnsecuredAsync(Slug, new DealPayer(Buyer), new[] { id, gone, Guid.NewGuid() }, "سَبَب");

        Assert.Equal(DealStatus.Active, w.Read<Deal>(id.ToString())!.Status);
        Assert.DoesNotContain(w.Read<Deal>(gone.ToString())!.Timeline, e => e.Action == "cancelled");
        Assert.Empty(pay.Calls);
    }

    /// <summary><b>ولِلرَمزِ نَصٌّ حَيٌّ في صَفحَةِ الإتمام</b> (القاعِدَة ١١): <see cref="CheckoutPayMethods.HoldFailed"/> يُقرَأُ في <c>CheckoutPage.razor</c> بِمِفتاحِه،
    /// والنَصّانِ في القامُوسِ لا عَلامَةَ نائِبَةٍ فيهِما ويَقولانِ إنّ السَلَّةَ كَما هي وإنّ ما حُجِزَ فُكّ.</summary>
    [Fact]
    public void The_hold_failure_code_has_its_checkout_text_and_its_cancel_reason_in_the_dictionary()
    {
        var page = File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, "libs", "templates",
            "ACommerce.Templates.Customer.Marketplace", "Components", "Pages", "CheckoutPage.razor"));
        Assert.Contains("CheckoutPayMethods.HoldFailed => L[\"checkout.payment.err_hold_failed\"],", page, StringComparison.Ordinal);
        Assert.Equal("pay_hold_failed", CheckoutPayMethods.HoldFailed);
        foreach (var key in new[] { "checkout.payment.err_hold_failed", "checkout.payment.cancel_hold_failed" })
        {
            var text = LocaleCatalog.Text(LocaleCatalog.Arabic, key);
            output.WriteLine($"{key}: {text}");
            Assert.NotEqual(key, text);
            Assert.False(LocaleCatalog.IsPlaceholder(text));
            Assert.Contains("فُكَّ ما حُجِز", text, StringComparison.Ordinal);
        }
        Assert.Contains("سَلَّتُكَ كَما هي", LocaleCatalog.Text(LocaleCatalog.Arabic, "checkout.payment.err_hold_failed"), StringComparison.Ordinal);
    }

    private static int Count(string text, string needle)
    {
        var n = 0;
        for (var i = text.IndexOf(needle, StringComparison.Ordinal); i >= 0; i = text.IndexOf(needle, i + needle.Length, StringComparison.Ordinal)) n++;
        return n;
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
