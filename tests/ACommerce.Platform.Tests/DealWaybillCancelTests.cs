using ACommerce.Kit.Delivery;
using ACommerce.Kit.Notifications;
using ACommerce.Platform.I18n;
using ACommerce.Templates.Customer.Marketplace.Services;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using Marten;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ إلغاءُ صَفقَةٍ واحِدَةٍ مِن طَلَبِ شَحنٍ مُشتَرَكٍ عِندَ OTO (‏2026-10-30، تَدقيقُ الموجَة ١١ F1 + G-3) ═════════════════════════════
//
// **الحالُ المَقيسَة** (تَوصيفُها `8029031`): مُشتَرٍ أَتَمَّ «أ» (‏100) و«ب» (‏150) مِن بائِعٍ واحِدٍ نَقداً عِندَ الاستِلام؛ شَحَنَ البائِعُ فَأَنشَأَ `ShipAsync` طَلَباً
// واحِداً عِندَ OTO بِمُعَرِّفِ «أ» ومَبلَغِ تَحصيلٍ ‏250 وبَندَي «أ · ب»، والصَفقَتانِ عِندَ «شَحن» (د13 «ب»). الطَرَفانِ لا يُلغِيانِ (د37: `HoldsLiveOrder`)، وكانَ صاحِبُ
// المَتجَرِ يُلغي «ب» وَحدَها مِن الاستوديو — والطَلَبُ عِندَ OTO يَحمِلُ «ب» ومَبلَغَها: فَإمّا تَموتُ شُحنَةُ «أ» مَعَ إلغاءِ البائِعِ الطَلَب، وإمّا يُحَصِّلُ المَندوبُ ‏250
// و«ب» لا يَنالُها إلّا سَطرُ «سُلِّمَت بَعدَ الإلغاء» — بِلا «رَدٌّ نَقدِيٌّ مُستَحَقّ» ولا إشعار (G-3، ولِلصَفقَةِ الواحِدَةِ أَيضاً).
//
// **القَرار** («أ» F3·٢ بِأَفضَلِ المُمارَسات): الشُحنَةُ المُسَلَّمَةُ لِلمَندوبِ وَحدَةٌ واحِدَة، والثابِتُ في الكود لا في تَعليمَةٍ تَتَوَقَّفُ عَلى تَعديلِ مَبلَغٍ عِندَ OTO لَم يُقَس:
// (١) إلغاءُ صَفقَةٍ وَحدَها مِن شُحنَةٍ سائِرَةٍ مَعَ أُختٍ لَم تُسَلَّم مَردودٌ بِـ`deal_in_shared_waybill`؛ (٢) «إلغاء الطَّلَب كامِلاً» يَشمَلُ أَخَواتِ الشُحنَة، والشَريحَةُ
// تُسَمّيهِنّ ومَبلَغَ التَحصيل؛ (٣) «سُلِّمَت بَعدَ الإلغاء» عَلى صَفقَةِ نَقدٍ يَكتُبُ `cash_refund_due` في الكِتابَةِ نَفسِها ويُخبِرُ صاحِبَ المَتجَر.

public class DealWaybillCancelTests(ITestOutputHelper output)
{
    private const string Slug = DealShipTests.Slug;
    private static readonly Guid AppOwner = Guid.Parse("dddddddd-dddd-4ddd-8ddd-dddddddddddd");

    /// <summary>مُلغي الاستوديو — صاحِبُ المَتجَرِ بِسُلطَةِ الإدارَة، كَما يَبنيه بابُ <c>/studio/apps/{slug}/deals/{id}/cancel</c>.</summary>
    internal static readonly DealCanceller StoreOwner = new(Guid.Parse("dddddddd-dddd-4ddd-8ddd-dddddddddddd"), "صاحِبُ المَتجَر", IsStoreAdmin: true);

    private static Deal Stored(DealShipGroupTests.Pair w, Guid id) => w.Docs.Read<Deal>(id.ToString())!;

    /// <summary>صَفقَتا السَلَّةِ مَشحونَتانِ بِطَلَبٍ واحِدٍ عِندَ OTO — مَبلَغُ تَحصيلِه مَجموعُهُما. و<paramref name="told"/> يُركِّبُ أُنبوبَ الإشعارِ ومالِكَ المَتجَر.</summary>
    internal static async Task<DealShipGroupTests.Pair> ShippedGroup(bool told = false)
    {
        var w = await DealShipGroupTests.Grouped(await DealShipGroupTests.TwoConfirmedCashDeals(notices: told ? GatewayOwnerMoneyNoticeTests.Notices : null));
        if (told) w.Docs.Put(new ACommerce.Kit.Tenants.Tenant { Id = Slug, Name = "مَتجَر", OwnerUserId = AppOwner });
        Assert.True((await w.Shipping.ShipAsync(Slug, w.Second, DealShipTests.Vendor, "Riyadh")).Ok);
        return w;
    }

    private static void OtoSays(DealShipGroupTests.Pair w, string status)
        => w.Oto.Routes[OtoClient.OrderStatusPath] = body => OtoRouteHandler.Tracking(OtoRouteHandler.OrderIdIn(body), status: status);

    // ═══ (١) لا تُلغى وَحدَها ════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <para><b>قُلِبَ بِاسمِه</b> (تَوصيفُه <c>8029031</c> — كانَ <c>Today_the_store_owner_cancels_one_deal_of_a_live_grouped_waybill_alone_and_the_order_keeps_the_summed_amount</c>:
    /// يُقبَلُ إلغاءُ «ب» وَحدَها): صاحِبُ المَتجَرِ يُلغي «ب» وَحدَها مِن طَلَبٍ مُشتَرَكٍ حَيٍّ عِندَ OTO ⇒ <c>deal_in_shared_waybill</c> قَبلَ أَيِّ كِتابَة، والصَفقَتانِ
    /// قائِمَتانِ عِندَ «شَحن»، والطَلَبُ الواحِدُ كَما هو. ومِن «أ» كَذلك.</para>
    /// </summary>
    [Fact]
    public async Task The_store_owner_cannot_cancel_one_deal_of_a_live_grouped_waybill_alone()
    {
        var w = await ShippedGroup();
        var saves = w.Docs.SaveCalls;

        var second = await w.Deals.CancelAsync(Slug, w.Second, StoreOwner, "طَلَبَه المُشتَري");
        var first = await w.Deals.CancelAsync(Slug, w.First, StoreOwner, "طَلَبَه المُشتَري");

        var (a, b) = (Stored(w, w.First), Stored(w, w.Second));
        output.WriteLine($"«ب»: {second.Violation?.Code} · «أ»: {first.Violation?.Code} · كُتِب={w.Docs.SaveCalls - saves} · «أ» {a.Status}/{a.Stage} · «ب» {b.Status}/{b.Stage}");
        Assert.Equal(DealCancelAuthorization.DealInSharedWaybill, second.Violation?.Code);
        Assert.Equal(DealCancelAuthorization.DealInSharedWaybill, first.Violation?.Code);
        Assert.Equal(saves, w.Docs.SaveCalls);
        Assert.All(new[] { a, b }, d => Assert.Equal((DealStatus.Active, DealStage.Shipping), (d.Status, d.Stage)));
        Assert.Equal(250m, Assert.Single(DealShipGroupTests.Orders(w.Oto)).AmountDue);
    }

    /// <summary><b>السالِبُ لِلرَمز</b>: صَفقَةٌ بِلا أَخَواتٍ في طَلَبِها (سَلَّتان) تُلغى وَحدَها كَما كانَت؛ وأُختٌ أُلغِيَت قَبلُ لا تُمسِكُ أُختَها؛ وبَعدَ «تَسليم» تُلغى الواحِدَةُ
    /// (فَيُكتَبُ رَدُّ ثَمَنِها مُستَحَقّاً، ebccd21).</summary>
    [Fact]
    public async Task A_deal_alone_on_its_order_or_after_delivery_is_cancelled_as_before()
    {
        var single = await DealShipGroupTests.TwoConfirmedCashDeals();
        Assert.True((await single.Shipping.ShipAsync(Slug, single.First, DealShipTests.Vendor, "Riyadh")).Ok);
        Assert.True((await single.Deals.CancelAsync(Slug, single.First, StoreOwner, "طَلَبَه المُشتَري")).Ok);

        var delivered = await ShippedGroup();
        OtoSays(delivered, "delivered");
        Assert.Equal(OtoOrderStatusGate.Delivered, (await delivered.Shipping.ReceiveAsync(Slug, OtoOrderStatusTests.Notice(delivered.First))).Code);
        var after = await delivered.Deals.CancelAsync(Slug, delivered.Second, StoreOwner, "مُرتَجَع");
        output.WriteLine($"بَعدَ التَسليم: ok={after.Ok} · أَسطُرُ «ب» الأَخيرَة: {string.Join("، ", Stored(delivered, delivered.Second).Timeline.TakeLast(2).Select(e => e.Action))}");
        Assert.True(after.Ok, after.Violation?.Code);
        Assert.Equal(DealEvent.CashRefundDue, Stored(delivered, delivered.Second).Timeline[^1].Action);
    }

    /// <summary>الدالَّةُ النَقِيَّة — مُوجِبٌ وسالِب: أُختٌ فَعّالَةٌ أَو في «نِزاع» لَم تُسَلَّم في الطَلَبِ نَفسِه ⇐ الرَمز؛ ومُلغاة، ومُسَلَّمَة، وطَلَبٌ آخَر، وطَلَبٌ أَغلَقَته OTO،
    /// وصَفقَةٌ بِلا طَلَب ⇐ لا شَيء.</summary>
    [Fact]
    public void ValidateWaybill_positive_and_negative()
    {
        static Deal Member(Guid lead, Action<Deal>? tweak = null)
        {
            var d = new Deal { Id = Guid.NewGuid(), Status = DealStatus.Active, Stage = DealStage.Shipping };
            d.Attributes[CheckoutPayMethods.AttributeKey] = CheckoutPayMethods.CashOnDelivery;
            d.Refs[DealShipping.OrderRef] = lead.ToString("N");
            tweak?.Invoke(d);
            return d;
        }
        var lead = Guid.NewGuid();
        var deal = Member(lead);
        Assert.Equal(DealCancelAuthorization.DealInSharedWaybill, DealCancelAuthorization.ValidateWaybill(deal, new[] { Member(lead) })?.Code);
        Assert.Equal(DealCancelAuthorization.DealInSharedWaybill,
            DealCancelAuthorization.ValidateWaybill(deal, new[] { Member(lead, d => d.Status = DealStatus.Disputed) })?.Code);

        Assert.True(DealCancelAuthorization.IsWaybillAllowed(deal, Array.Empty<Deal>()));
        Assert.True(DealCancelAuthorization.IsWaybillAllowed(deal, new[] { Member(lead, d => d.Status = DealStatus.Cancelled) }));
        Assert.True(DealCancelAuthorization.IsWaybillAllowed(deal, new[] { Member(lead, d => d.Stage = DealStage.Delivered) }));
        Assert.True(DealCancelAuthorization.IsWaybillAllowed(deal, new[] { Member(Guid.NewGuid()) }));
        Assert.True(DealCancelAuthorization.IsWaybillAllowed(Member(lead, d => d.Refs[DealShipping.ClosedRef] = "canceled"), new[] { Member(lead) }));
        Assert.True(DealCancelAuthorization.IsWaybillAllowed(Member(lead, d => d.Stage = DealStage.Delivered), new[] { Member(lead) }));
        var unplaced = Member(lead, d => d.Refs.Remove(DealShipping.OrderRef));
        Assert.True(DealCancelAuthorization.IsWaybillAllowed(unplaced, new[] { Member(lead) }));
    }

    // ═══ (٢) الطَلَبُ كُلُّه ═══════════════════════════════════════════════════════════════════════════════

    /// <summary><b>المُوجِب — الطَريقُ الَّذي يُفضي</b>: «إلغاء الطَّلَب كامِلاً» (<c>scope=order</c> ← <c>CancelWholeInvoiceAsync</c>) يُلغي الصَفقَتَين — المَطلوبَةُ آخِرَهُما —
    /// ويُعيدُهُما لِيُخبَرَ طَرَفُ كُلٍّ مِنهُما. والشُحنَةُ بَعدَها لا تَحمِلُ صَفقَةً قائِمَة، فَشَريحَةُ الطَلَبِ الواحِدِ هي ما يُرسَم.</summary>
    [Fact]
    public async Task The_whole_order_cancel_cancels_every_deal_of_the_live_waybill()
    {
        var w = await ShippedGroup();

        var order = await w.Deals.CancelWholeInvoiceAsync(Slug, w.Second, StoreOwner, "طَلَبَه المُشتَري");

        output.WriteLine($"الطَلَبُ كُلُّه: ok={order.Result.Ok} · أُلغِيَ: {order.Cancelled.Count} · آخِرُها المَطلوبَة: {order.Cancelled[^1].Id == w.Second}");
        Assert.True(order.Result.Ok, order.Result.Violation?.Code);
        Assert.Equal(new[] { w.First, w.Second }, order.Cancelled.Select(d => d.Id).ToArray());
        Assert.All(new[] { Stored(w, w.First), Stored(w, w.Second) }, d => Assert.Equal(DealStatus.Cancelled, d.Status));
        Assert.Empty(DealCancelAuthorization.LiveOnWaybill(Stored(w, w.Second), await w.Deals.WaybillSiblingsAsync(Slug, Stored(w, w.Second))));
    }

    /// <summary>
    /// <para><b>قُلِبَ بِاسمِه</b> (تَوصيفُه <c>8029031</c> — كانَ <c>Today_the_studio_page_names_no_other_deal_of_the_waybill_and_offers_the_single_cancel</c>): صَفحَةُ الاستوديو
    /// تَسأَلُ دالَّةَ الكاتِبِ نَفسَها — شُحنَةٌ مَعَ أُختٍ لَم تُسَلَّم ⇒ «إلغاء الطَّلَب كامِلاً» (<c>cancelShared</c>)، وحَسمُ نِزاعِها بِلا بَديلِ الإلغاءِ ونَصُّه؛ والشَريحَةُ
    /// تُسَمّي الأَخَواتِ ومَبلَغَ التَحصيلِ بِنَصٍّ مِن القامُوس.</para>
    /// </summary>
    [Fact]
    public void The_studio_page_offers_the_whole_order_and_names_the_waybill_through_the_writers_own_predicate()
    {
        var page = WriteEndpointGuardTests.StripComments(WriteEndpointGuardTests.StripMarkupComments(File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot,
            "libs", "templates", "ACommerce.Templates.Customer.Marketplace", "Components", "Pages", "StudioAppDealDetail.razor"))));
        Assert.Contains("if (deal is not null) cancelShared = !GatewayInvoiceShare.IsValid(deal, await Deals.InvoiceSiblingsAsync(Slug, deal));", page, StringComparison.Ordinal);
        Assert.Contains("waybill = await Deals.WaybillSiblingsAsync(Slug, deal);", page, StringComparison.Ordinal);
        Assert.Contains("waybillLive = DealCancelAuthorization.LiveOnWaybill(deal, waybill);", page, StringComparison.Ordinal);
        Assert.Contains("if (!DealCancelAuthorization.IsWaybillAllowed(deal, waybill))", page, StringComparison.Ordinal);
        Assert.Contains("if (deal.Status == DealStatus.Active) cancelShared = true;", page, StringComparison.Ordinal);
        Assert.Contains("else if (deal.Status == DealStatus.Disputed) resolveWaybill = true;", page, StringComparison.Ordinal);
        Assert.Contains("@L.Format(\"studio.deal.oto_order_live_group\"", page, StringComparison.Ordinal);
        Assert.Contains("@L.Markup(\"studio.deal.resolve_cancel_waybill\")", page, StringComparison.Ordinal);
        var group = LocaleCatalog.Text(LocaleCatalog.Arabic, "studio.deal.oto_order_live_group");
        Assert.Contains("{0}", group, StringComparison.Ordinal);
        Assert.Contains("{1:N2}", group, StringComparison.Ordinal);
        Assert.Contains("إلغاء الطَّلَب كامِلاً", group, StringComparison.Ordinal);
        Assert.Contains("بَعدَ التَّسليمِ", group, StringComparison.Ordinal);
        foreach (var key in new[] { "studio.deal.err_deal_in_shared_waybill", "deals.detail.err_deal_in_shared_waybill", "studio.deal.resolve_cancel_waybill" })
            Assert.False(string.IsNullOrWhiteSpace(LocaleCatalog.Text(LocaleCatalog.Arabic, key)), key);
    }

    // ═══ (٣) G-3 — سُلِّمَت بَعدَ الإلغاء ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// <para><b>قُلِبَ بِاسمِه</b> (تَوصيفُه <c>8029031</c> — كانَ <c>Today_a_delivered_notice_after_a_member_was_cancelled_writes_no_cash_refund_due_and_names_no_owner_action</c>):
    /// «ب» أُلغِيَت قَبلَ هذا المُلحَقِ وَحدَها (حالٌ مَخزونَةٌ تَبقى)، ثُمَّ يُسَلِّمُ المَندوبُ ويُحَصِّلُ ‏250 — «أ» تَصيرُ «تَسليم»، و«ب» يُكتَبُ لَها في الكِتابَةِ نَفسِها
    /// <c>oto_delivered_after_cancel</c> و<c>cash_refund_due</c> بِفاعِلِه المَنصَّة، فَيُسَمّي <c>OwnerActionOf</c> رَدّاً نَقدِيّاً مُستَحَقّاً، ويُخبَرُ صاحِبُ المَتجَرِ مَرَّةً
    /// بِنَصِّ «رَدٌّ نَقديٌّ مُستَحَقّ» ورابِطِ صَفقَتِها في الاستوديو؛ وإعادَةُ OTO لِلإشعارِ لا تَكتُبُ ولا تُخبِرُ ثانِيَة.</para>
    /// </summary>
    [Fact]
    public async Task A_delivered_notice_after_a_member_was_cancelled_writes_a_cash_refund_due_once_and_tells_the_store_owner()
    {
        var w = await ShippedGroup(told: true);
        Stored(w, w.Second).Status = DealStatus.Cancelled;   // إلغاءٌ مَخزونٌ مِن قَبلِ المُلحَق — البابُ اليَومَ يَرُدُّه
        OtoSays(w, "delivered");

        var outcome = await w.Shipping.ReceiveAsync(Slug, OtoOrderStatusTests.Notice(w.First));
        var again = await w.Shipping.ReceiveAsync(Slug, OtoOrderStatusTests.Notice(w.First));

        var (a, b) = (Stored(w, w.First), Stored(w, w.Second));
        var toOwner = w.Docs.Stored.OfType<Notification>().Where(n => n.UserId == AppOwner).ToList();
        output.WriteLine($"الوارِد: {outcome.Code}/{again.Code} · «أ» {a.Stage} · «ب» أَسطُرُها الأَخيرَة: {string.Join("، ", b.Timeline.TakeLast(2).Select(e => e.Action))} · " +
                         $"إشعاراتُ المالِك: {toOwner.Count} «{toOwner.FirstOrDefault()?.Title}»");
        Assert.Equal(DealStage.Delivered, a.Stage);
        Assert.Equal("delivered", b.Refs[DealShipping.DeliveredAfterCancelRef]);
        var due = Assert.Single(b.Timeline, e => e.Action == DealEvent.CashRefundDue);
        Assert.True(DealEvent.IsByPlatform(due));
        Assert.Equal(DealNotices.OwnerAction.CashRefundDue, DealNotices.OwnerActionOf(b));
        var note = Assert.Single(toOwner);
        var l = new L(new HttpContextAccessor());
        Assert.Equal(l["notifications.deal_cash_refund_due.title"], note.Title);
        Assert.Equal(l.Format("notifications.deal_cash_refund_due.body", "سِلعَة 2", 150m), note.Body);
        Assert.Equal(DealNotices.StudioDealUrl(Slug, w.Second), note.RelatedUrl);
    }

    /// <summary><b>قُلِبَ بِاسمِه</b> (تَوصيفُه <c>8029031</c> — كانَ <c>Today_a_single_cash_deal_delivered_after_its_cancel_writes_no_cash_refund_due</c>): G-3 لِلصَفقَةِ الواحِدَة —
    /// صَفقَةُ نَقدٍ بِلا أَخَواتٍ شُحِنَت ثُمَّ أَلغاها صاحِبُ المَتجَرِ ثُمَّ سَلَّمَها المَندوب ⇒ السَطرُ ومَعَه <c>cash_refund_due</c> مَرَّةً، وصاحِبُ المَتجَرِ يُخبَر.
    /// <b>والسالِب</b>: إشعارٌ غَيرُ «delivered» عَلى المُلغاةِ لا يَكتُبُ شَيئاً.</summary>
    [Fact]
    public async Task A_single_cash_deal_delivered_after_its_cancel_writes_a_cash_refund_due_and_tells_the_owner()
    {
        var w = await DealShipGroupTests.TwoConfirmedCashDeals(notices: GatewayOwnerMoneyNoticeTests.Notices);
        w.Docs.Put(new ACommerce.Kit.Tenants.Tenant { Id = Slug, Name = "مَتجَر", OwnerUserId = AppOwner });
        Assert.True((await w.Shipping.ShipAsync(Slug, w.First, DealShipTests.Vendor, "Riyadh")).Ok);
        Assert.True((await w.Deals.CancelAsync(Slug, w.First, StoreOwner, "طَلَبَه المُشتَري")).Ok);
        OtoSays(w, "inTransit");
        var lines = Stored(w, w.First).Timeline.Count;
        await w.Shipping.ReceiveAsync(Slug, OtoOrderStatusTests.Notice(w.First));
        Assert.Equal(lines, Stored(w, w.First).Timeline.Count);

        OtoSays(w, "delivered");
        await w.Shipping.ReceiveAsync(Slug, OtoOrderStatusTests.Notice(w.First));

        var a = Stored(w, w.First);
        Assert.Equal("delivered", a.Refs[DealShipping.DeliveredAfterCancelRef]);
        Assert.Single(a.Timeline, e => e.Action == DealEvent.CashRefundDue);
        Assert.Equal(DealNotices.OwnerAction.CashRefundDue, DealNotices.OwnerActionOf(a));
        Assert.Single(w.Docs.Stored.OfType<Notification>(), n => n.UserId == AppOwner);
    }
}

/// <summary><b>الشُحنَةُ المُشتَرَكَةُ عَلى Postgres</b> (‏2026-10-30، تَدقيقُ الموجَة ١١ F1): الصَفقَتانِ وأولاهُما تُقرَآنِ مِن القاعِدَةِ نُسخاً — فَإلغاءُ «ب» وَحدَها
/// مَردودٌ بِـ<c>deal_in_shared_waybill</c> بِلا كِتابَة، و«إلغاء الطَّلَب كامِلاً» يُلغيهِما مَعاً.</summary>
[Collection(LiveProofCollection.Name)]
public class LiveDealWaybillCancelProofTests(ITestOutputHelper output)
{
    private static bool Enabled
        => LiveDealConcurrencyProofTests.Enabled && !string.IsNullOrEmpty(LiveDealConcurrencyProofTests.ConnectionString);

    [Fact]
    public async Task One_deal_of_a_live_grouped_waybill_is_refused_alone_and_the_whole_order_cancels_both_on_Postgres()
    {
        if (!Enabled) return;

        await using var store = LiveDealConcurrencyProofTests.BuildStore();
        var slug = DealShipping.DemoStore + "-zz-w" + Guid.NewGuid().ToString("N")[..8];
        var (buyer, seller) = (Guid.NewGuid(), Guid.NewGuid());
        try
        {
            var plain = new DealsService(store, new DealShipGroupTests.ForbiddenPaymentProvider());
            var ids = new List<Guid>();
            for (var i = 0; i < 2; i++)
            {
                var born = await plain.StartAsync(slug, "marketplace", buyer, "مُشتَرٍ", Guid.NewGuid(), $"سِلعَة {i + 1}", 100m + 50m * i, new()
                {
                    [CheckoutPayMethods.AttributeKey] = CheckoutPayMethods.CashOnDelivery,
                    [Deal.DeliveryAddressAttribute] = "حَيُّ المِثال",
                    [Deal.DeliveryPhoneAttribute] = "0500000000",
                });
                await plain.AttachRefAsync(slug, born.Id, DealsService.ListingOwnerRef, seller.ToString());
                await plain.BindListingOwnerAsync(slug, born.Id, seller.ToString());
                Assert.True((await plain.AcceptAsync(slug, born.Id, new DealAcceptor(seller, "بائِع"))).Ok);
                Assert.True((await plain.AdvanceAsync(slug, born.Id, seller, "بائِع")).Ok);
                ids.Add(born.Id);
            }
            await DealShipGroup.WriteAsync(plain, slug, ids.Select(id => (id, seller.ToString())).ToArray());
            var oto = new OtoRouteHandler();
            var options = Options.Create(new OtoOptions { Environment = OtoEnvironment.Staging, RefreshToken = "RT-live-proof" });
            var shipping = new DealShipping(new DealsService(TenantAlias.Wrap(store, DealShipping.DemoStore, slug), new DealShipGroupTests.ForbiddenPaymentProvider()),
                new OtoClient(new HttpClient(oto), options, new OtoTokenCache(), NullLogger<OtoClient>.Instance), options);
            Assert.True((await shipping.ShipAsync(DealShipping.DemoStore, ids[1], new DealShipper(seller, "بائِع"), "Riyadh")).Ok);

            var alone = await plain.CancelAsync(slug, ids[1], DealWaybillCancelTests.StoreOwner, "طَلَبَه المُشتَري");
            var stillActive = (await plain.LoadAsync(slug, ids[1]))!.Status;
            var whole = await plain.CancelWholeInvoiceAsync(slug, ids[1], DealWaybillCancelTests.StoreOwner, "طَلَبَه المُشتَري");
            var (first, second) = ((await plain.LoadAsync(slug, ids[0]))!, (await plain.LoadAsync(slug, ids[1]))!);
            output.WriteLine($"وَحدَها: {alone.Violation?.Code} ({stillActive}) · الطَلَبُ كُلُّه: ok={whole.Result.Ok} أُلغِيَ {whole.Cancelled.Count} · {first.Status}/{second.Status}");
            Assert.Equal(DealCancelAuthorization.DealInSharedWaybill, alone.Violation?.Code);
            Assert.Equal(DealStatus.Active, stillActive);
            Assert.True(whole.Result.Ok, whole.Result.Violation?.Code);
            Assert.Equal(2, whole.Cancelled.Count);
            Assert.Equal((DealStatus.Cancelled, DealStatus.Cancelled), (first.Status, second.Status));
        }
        finally
        {
            await using var s = store.LightweightSession(slug);
            foreach (var d in await s.Query<Deal>().ToListAsync()) s.Delete(d);
            await s.SaveChangesAsync();
        }
    }
}
