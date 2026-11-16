using System.Net;
using ACommerce.Kit.Notifications;
using ACommerce.Kit.Payments;
using ACommerce.Platform.I18n;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Wolverine;
using Xunit;
using Xunit.Abstractions;
using static ACommerce.Platform.Tests.TenantGatewayEndpointsTests;

namespace ACommerce.Platform.Tests;

// ═══ فاتورَةٌ مَفتوحَةٌ عَلى صَفقَتَينِ إحداهُما في «نِزاع» — الكُلُّ أَو لا شَيء (‏2026-11-05، الموجَة ١٢ G3 · ٢ — C-PAY-EXP-2؛ تَوصيفُه `7076249`) ═══
//
// **القَرار** (د2 «مختلطة بفاتورة واحدة يُلغى فيها قبل الدفع الكل أو لا شيء»؛ د38؛ د5 «ب»): أُختٌ في «نِزاع» لَم تُدفَع تُمسِكُ إلغاءَ أُختِها وَحدَها بِرَمزٍ ثابِت
// `deal_invoice_disputed`، ولا «طَلَبَ كُلَّه» لِلطَرَفِ مَعَها (لا يُلغيها)؛ وصاحِبُ المَتجَرِ يُلغي الطَلَبَ كُلَّه فَتُحسَمُ إلغاءً بِبَوّابَةِ الحَسمِ وأَثَرِ الإلغاءِ الماليِّ القائِم؛ وحَسمُ
// مُتَنازَعٍ عَلَيها بِالإلغاءِ وأُختُها في «نِزاع» يُرَدُّ بِرَمزِه القائِم؛ واستِئنافٌ بَعدَ انقِضاءِ المُهلَةِ يُعيدُ رِسالَتَها الآن فَيُلغي مُعالِجُها القائِمُ الطَلَبَ بِسَطرِها وإشعاراتِها.
// ومِلَفُّ التَوصيفِ `DealDisputedSiblingOrderTests.cs` بِبَصمَتِه قَبلُ وبَعدُ.

public class DealDisputedSiblingOrderSwitchTests(ITestOutputHelper output)
{
    private static Deal Load(World w, Guid id) => w.Docs.Read<Deal>(id.ToString())!;
    private static readonly DealCanceller BuyerCancels = new(Buyer, "مُشتَرٍ", IsStoreAdmin: false);
    private static string[] Sent(World w) => w.Moyasar.Requests.Select(r => r.Method.Method).ToArray();

    private static async Task<(World W, GatewayPayments Gateway, Guid[] Ids, DealCanceller Owner)> DisputedSibling(params string[] replies)
    {
        var (w, gateway, ids) = await GatewayExpiryRaceTests.ExpiredCart();
        var owner = new DealCanceller(w.Docs.Read<ACommerce.Kit.Tenants.Tenant>(Slug)!.OwnerUserId, "مالِك التَّطبيق", IsStoreAdmin: true);
        Assert.True((await w.Deals.DisputeAsync(Slug, ids[1], owner.ActorId, owner.ActorName, "لَم تَصِل")).Ok);
        w.Moyasar.Requests.Clear();
        foreach (var status in replies) w.Moyasar.Then(HttpStatusCode.OK, GatewayExpiryRaceTests.Invoice(ids, status));
        return (w, gateway, ids, owner);
    }

    // ═══ الطَرَف: لا وَحدَها ولا كُلَّه ═══════════════════════════════════════════════════════════════════════

    /// <summary><b>المُوجِب — المُشتَري يُلغي «أ» وَحدَها و«ب» في «نِزاع»</b> ⇒ يُرَدُّ بِـ<c>deal_invoice_disputed</c> قَبلَ أَيِّ كِتابَة: لا حِفظَ ولا نِداءَ لِلبَوّابَة، و«أ» قائِمَة.</summary>
    [Fact]
    public async Task A_buyer_cancelling_one_deal_while_its_invoice_sibling_is_disputed_is_refused_before_any_write()
    {
        var (w, _, ids, _) = await DisputedSibling();
        var saves = w.Docs.SaveCalls;

        var res = await w.Deals.CancelAsync(Slug, ids[0], BuyerCancels, "عَدَلتُ");

        output.WriteLine($"المُشتَري يُلغي «أ» وَحدَها: {(res.Ok ? "ok" : res.Violation!.Code)} · أ={Load(w, ids[0]).Status} ب={Load(w, ids[1]).Status} · ميسر [{string.Join(" ", Sent(w))}]");
        Assert.Equal(GatewayInvoiceShare.Disputed, res.Violation?.Code);
        Assert.Equal((DealStatus.Active, DealStatus.Disputed), (Load(w, ids[0]).Status, Load(w, ids[1]).Status));
        Assert.Equal(saves, w.Docs.SaveCalls);
        Assert.Empty(w.Moyasar.Requests);
    }

    /// <summary><b>المُوجِب — المُشتَري يُلغي «الطَلَبَ كامِلاً» و«ب» في «نِزاع»</b> ⇒ يُرَدُّ بِالرَمزِ نَفسِه، ولا يُلغى شَيء.</summary>
    [Fact]
    public async Task A_buyer_cancelling_the_whole_order_while_one_deal_is_disputed_is_refused_and_cancels_nothing()
    {
        var (w, _, ids, _) = await DisputedSibling();

        var order = await w.Deals.CancelWholeInvoiceAsync(Slug, ids[0], BuyerCancels, "عَدَلتُ");

        output.WriteLine($"المُشتَري يُلغي الطَلَبَ كامِلاً: {(order.Result.Ok ? "ok" : order.Result.Violation!.Code)} أُلغِيَ {order.Cancelled.Count} · ميسر [{string.Join(" ", Sent(w))}]");
        Assert.Equal(GatewayInvoiceShare.Disputed, order.Result.Violation?.Code);
        Assert.Empty(order.Cancelled);
        Assert.Equal((DealStatus.Active, DealStatus.Disputed), (Load(w, ids[0]).Status, Load(w, ids[1]).Status));
        Assert.Empty(w.Moyasar.Requests);
    }

    /// <summary><b>السالِب — أُختٌ في «نِزاع» دُفِعَت</b> (مَقبوضَة) لا تُبقي الفاتورَة: إلغاءُ «أ» يَمضي كَما كانَ لا يَرُدُّه هذا الرَمز.</summary>
    [Fact]
    public async Task A_disputed_sibling_already_paid_does_not_hold_the_single_cancel()
    {
        var (w, _, ids, _) = await DisputedSibling("initiated");
        await w.Deals.AttachRefAsync(Slug, ids[1], "payment_status", nameof(PaymentStatus.Captured));
        var siblings = await w.Deals.InvoiceSiblingsAsync(Slug, Load(w, ids[0]));
        Assert.Empty(GatewayInvoiceShare.DisputedOnInvoice(Load(w, ids[0]), siblings));
        Assert.True(GatewayInvoiceShare.IsValid(Load(w, ids[0]), siblings));
    }

    // ═══ صاحِبُ المَتجَر: «إلغاء الطَّلَب كامِلاً» يُلغي الكُلّ ═══════════════════════════════════════════════════

    /// <summary><b>المُوجِب — صاحِبُ المَتجَرِ يُلغي الطَلَبَ كُلَّه مِن «أ»</b> ⇒ تُلغى الاثنَتان، و«ب» حَسماً إلغاءً بِاسمِه، والأَخيرَةُ تُلغي الفاتورَةَ عِندَ البَوّابَة (جَلبٌ، جَلبٌ، إلغاء)؛
    /// والمُشتَري يُخبَرُ بِكُلِّ صَفقَةٍ أُلغِيَت بِنَصِّ «إدارَةِ المَتجَر».</summary>
    [Fact]
    public async Task A_store_cancelling_the_whole_order_resolves_the_disputed_sibling_as_a_cancel_and_closes_the_invoice()
    {
        var (w, _, ids, owner) = await DisputedSibling("initiated", "initiated", MoyasarClient.CanceledStatus);
        var l = new L(new HttpContextAccessor());

        var order = await w.Deals.CancelWholeInvoiceAsync(Slug, ids[0], owner, "إلغاء إداريّ");
        var notices = GatewayOwnerMoneyNoticeTests.Notices(w.Docs);
        foreach (var gone in order.Cancelled) await notices.NotifyCancelledByStoreAsync(Slug, gone, owner.ActorId, l);

        var toBuyer = w.Docs.Stored.OfType<Notification>().Count(n => n.UserId == Buyer);
        output.WriteLine($"صاحِبُ المَتجَرِ يُلغي الطَلَبَ كامِلاً: {(order.Result.Ok ? "ok" : order.Result.Violation!.Code)} أُلغِيَ {order.Cancelled.Count} · " +
                         $"أ={Load(w, ids[0]).Status} ب={Load(w, ids[1]).Status} · ميسر [{string.Join(" ", Sent(w))}] · لِلمُشتَري: {toBuyer}");
        Assert.True(order.Result.Ok, order.Result.Violation?.Code);
        Assert.Equal(ids.OrderBy(x => x), order.Cancelled.Select(d => d.Id).OrderBy(x => x));
        Assert.All(ids, id => Assert.Equal(DealStatus.Cancelled, Load(w, id).Status));
        var line = Load(w, ids[1]).Timeline.Last(e => e.Action == "cancelled");
        Assert.Equal((owner.ActorId, DealStage.Offered), (line.ActorId!.Value, line.StageAfter));
        Assert.Equal(new[] { "GET", "GET", "PUT" }, Sent(w));
        Assert.Equal(2, toBuyer);
    }

    /// <summary><b>السالِب — إلغاءُ المُهلَةِ لا يَحسِمُ نِزاعاً</b> (‏ADR-046 §٧‑3 كَما هو): بِشَرطِه (<c>stillDue</c>) يُلغي القائِمَةَ وَحدَها، و«ب» في «نِزاع».</summary>
    [Fact]
    public async Task The_expiry_cancel_still_leaves_a_disputed_sibling_disputed()
    {
        var (w, _, ids, _) = await DisputedSibling("initiated");

        var order = await w.Deals.CancelWholeInvoiceAsync(Slug, ids[0], GatewayPayments.ExpiryCanceller, "مُهلَة", stillDue: GatewayInvoiceClosing.Owed);

        Assert.Equal(new[] { ids[0] }, order.Cancelled.Select(d => d.Id).ToArray());
        Assert.Equal(DealStatus.Disputed, Load(w, ids[1]).Status);
    }

    /// <summary><b>المُوجِب — حَسمُ «أ» بِالإلغاءِ و«ب» في «نِزاع» أَيضاً</b> ⇒ يُرَدُّ بِـ<c>deal_invoice_shared</c> (تُستَأنَفُ ثُمَّ يُلغى الطَلَبُ كُلُّه) قَبلَ أَيِّ كِتابَة.</summary>
    [Fact]
    public async Task A_dispute_cancel_while_the_invoice_sibling_is_also_disputed_is_refused_before_any_write()
    {
        var (w, _, ids, owner) = await DisputedSibling();
        Assert.True((await w.Deals.DisputeAsync(Slug, ids[0], owner.ActorId, owner.ActorName, "لَم تَصِل")).Ok);
        var saves = w.Docs.SaveCalls;

        var res = await w.Deals.ResolveDisputeAsync(Slug, ids[0], owner, DealDisputeResolution.Cancel, "حَسم");

        output.WriteLine($"حَسمُ «أ» بِالإلغاءِ و«ب» في نِزاع: {(res.Ok ? "ok" : res.Violation!.Code)} · أ={Load(w, ids[0]).Status} ب={Load(w, ids[1]).Status}");
        Assert.Equal(GatewayInvoiceShare.Shared, res.Violation?.Code);
        Assert.Equal((DealStatus.Disputed, DealStatus.Disputed), (Load(w, ids[0]).Status, Load(w, ids[1]).Status));
        Assert.Equal(saves, w.Docs.SaveCalls);
    }

    // ═══ الاستِئنافُ بَعدَ انقِضاءِ المُهلَة ═══════════════════════════════════════════════════════════════════

    /// <summary><b>المُوجِب — يُرسَلُ رِسالَةُ المُهلَةِ لِلمُستَأنَفَةِ مَرَّةً الآن</b>؛ <b>والسالِبان</b>: مُهلَتُها لَم تَنقَضِ ⇒ لا رِسالَة، ودُفِعَت ⇒ لا رِسالَة.</summary>
    [Theory]
    [InlineData("window_closed", 1)]
    [InlineData("window_open", 0)]
    [InlineData("captured", 0)]
    public async Task A_dispute_resumed_after_its_invoice_window_closed_unpaid_sends_the_expiry_message_once(string how, int expected)
    {
        var (w, _, ids, owner) = await DisputedSibling();
        if (how == "window_open")
            await w.Deals.AttachRefAsync(Slug, ids[1], GatewayPayments.InvoiceExpiresAtRef, MoyasarClient.ExpiryText(DateTimeOffset.UtcNow.AddHours(3)));
        if (how == "captured") await GatewayExpiryNothingOwedTests.CaptureAsync(w, ids);
        var bus = new RecordingMessageBus();
        var deals = new DealsService(w.Docs.Store, w.Platform, bus: bus);

        var res = await deals.ResolveDisputeAsync(Slug, ids[1], owner, DealDisputeResolution.Resume, "استِئناف");

        output.WriteLine($"{how}: الحَسم {(res.Ok ? $"ok {res.Outcome}" : res.Violation!.Code)} · رَسائِل: {bus.Sent.Count}");
        Assert.True(res.Ok, res.Violation?.Code);
        Assert.Equal(expected, bus.Sent.Count);
        if (expected == 1) Assert.Equal(new GatewayInvoiceExpiryDue(Slug, ids[1]), Assert.Single(bus.Sent).Message);
    }

    /// <summary><b>ورِسالَةٌ لا تُرسَلُ لا تَرُدُّ الاستِئناف</b>: حافِلَةٌ تَرمي ⇒ الحَسمُ مَقبولٌ والصَفقَةُ قائِمَة، وسَطرٌ في اللوغِ يُسَمّيها.</summary>
    [Fact]
    public async Task A_bus_that_throws_at_the_resume_leaves_the_resume_standing()
    {
        var (w, _, ids, owner) = await DisputedSibling();
        var deals = new DealsService(w.Docs.Store, w.Platform, bus: new RecordingMessageBus { Throws = new InvalidOperationException("لا حافِلَة") });

        var res = await deals.ResolveDisputeAsync(Slug, ids[1], owner, DealDisputeResolution.Resume, "استِئناف");

        Assert.True(res.Ok, res.Violation?.Code);
        Assert.Equal(DealStatus.Active, Load(w, ids[1]).Status);
    }

    /// <summary>
    /// <para><b>والطَريقُ كُلُّه بِـWolverine الحَقيقِيِّ في الذاكِرَةِ وتَركيبِ <see cref="GatewayInvoiceExpiry.Configure"/> نَفسِه</b>: استِئنافُ «ب» بَعدَ المُهلَةِ ⇒ رِسالَةٌ ⇒ المُعالِجُ
    /// القائِمُ يَجلِبُ الفاتورَةَ (لَم تُدفَع) فَيُلغي الطَلَبَ كُلَّه بِاسمِ المَنصَّةِ وبِسَبَبِ المُهلَة، والأَخيرَةُ تُلغي الفاتورَةَ عِندَ البَوّابَة، ويُخبَرُ المُشتَري بِكُلِّ صَفقَةٍ
    /// «أُلغِيَ طَلَبُك — انتَهَت مُهلَةُ الدَّفع» وتُوسَمُ بَعدَ إشعارِها.</para>
    /// </summary>
    [Fact]
    public async Task A_resume_after_the_window_is_cancelled_by_the_existing_expiry_handler_with_its_line_and_notices()
    {
        var (w, gateway, ids, owner) = await DisputedSibling("initiated", "initiated", "initiated", MoyasarClient.CanceledStatus);
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(gateway);
        builder.Services.AddSingleton(GatewayOwnerMoneyNoticeTests.Notices(w.Docs));
        builder.Services.AddSingleton(w.Deals);
        builder.Services.AddSingleton(new L(new HttpContextAccessor()));
        builder.UseWolverine(opts =>
        {
            opts.UseRuntimeCompilation();
            opts.Discovery.DisableConventionalDiscovery();
            GatewayInvoiceExpiry.Configure(opts);
        });
        using var host = builder.Build();
        await host.StartAsync();
        try
        {
            using var scope = host.Services.CreateScope();
            var deals = new DealsService(w.Docs.Store, w.Platform, bus: scope.ServiceProvider.GetRequiredService<IMessageBus>());
            Assert.True((await deals.ResolveDisputeAsync(Slug, ids[1], owner, DealDisputeResolution.Resume, "استِئناف")).Ok);
            var watch = System.Diagnostics.Stopwatch.StartNew();
            while (watch.Elapsed < TimeSpan.FromSeconds(30) && !ids.All(id => Load(w, id).Refs.ContainsKey(GatewayPayments.ExpiryToldRef)))
                await Task.Delay(100);
        }
        finally { await host.StopAsync(); }

        var expired = w.Docs.Stored.OfType<Notification>().Where(n => n.UserId == Buyer
            && n.Title == LocaleCatalog.Text(LocaleCatalog.Arabic, "notifications.deal_payment_expired.title")).ToArray();
        output.WriteLine($"استِئنافٌ بَعدَ المُهلَة ⇒ أ={Load(w, ids[0]).Status} ب={Load(w, ids[1]).Status} · بِالمُهلَة: [{string.Join(", ", ids.Select(id => GatewayPayments.CancelledByExpiry(Load(w, id))))}] · " +
                         $"ميسر [{string.Join(" ", Sent(w))}] · «انتَهَت مُهلَةُ الدَّفع» لِلمُشتَري: {expired.Length}");
        Assert.All(ids, id =>
        {
            var d = Load(w, id);
            Assert.Equal(DealStatus.Cancelled, d.Status);
            Assert.True(GatewayPayments.CancelledByExpiry(d));
            Assert.True(d.Refs.ContainsKey(GatewayPayments.ExpiryToldRef));
        });
        Assert.Equal(new[] { "GET", "GET", "GET", "PUT" }, Sent(w));
        Assert.Equal(2, expired.Length);
    }

    // ═══ الأَبواب: الصَفحَةُ تَقولُ لِماذا، والـAPI بِرَمزِه ═══════════════════════════════════════════════════

    /// <summary><b>صَفحَةُ العُضوِ تَرسُمُ سَطرَ السَبَبِ لا استِمارَة</b> — بِالدالَّةِ نَفسِها الَّتي يَرُدُّ بِها الكاتِب، ونَصّاهُ حَيّانِ في القامُوس؛ ولا «الطَلَبُ كُلُّه» مَعَه.</summary>
    [Fact]
    public void The_member_page_draws_the_disputed_reason_line_through_the_writers_code_and_no_whole_order_form()
    {
        var page = WriteEndpointGuardTests.StripComments(WriteEndpointGuardTests.StripMarkupComments(File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot,
            "libs", "templates", "ACommerce.Templates.Customer.Marketplace", "Components", "Pages", "MyDealDetail.razor"))));
        Assert.Contains("cancelDisputed = cancelShared && GatewayInvoiceShare.Validate(deal, siblings).FirstOrDefault()?.Code == GatewayInvoiceShare.Disputed;", page, StringComparison.Ordinal);
        Assert.Contains("canCancelOrder = cancelShared && !cancelDisputed &&", page, StringComparison.Ordinal);
        Assert.Contains("else if (cancelDisputed)", page, StringComparison.Ordinal);
        Assert.Contains("L.Markup(\"deals.detail.cancel_disputed_hint\")", page, StringComparison.Ordinal);
        Assert.True(page.IndexOf("else if (cancelDisputed)", StringComparison.Ordinal) < page.IndexOf("else if (cancelShared)", StringComparison.Ordinal));
        foreach (var key in new[] { "deals.detail.cancel_disputed_hint", "deals.detail.err_deal_invoice_disputed" })
        {
            var text = LocaleCatalog.Text(LocaleCatalog.Arabic, key);
            Assert.NotEqual(key, text);
            Assert.Contains("نِزاع", text, StringComparison.Ordinal);
        }
        Assert.Equal("deals.detail.err_deal_invoice_disputed", DealRefusalTexts.KeyFor(GatewayInvoiceShare.Disputed));
    }

    /// <summary><b>والـAPI يُجيبُ بِرَمزِ الفاتورَةِ المُشتَرَكَةِ ‏409</b> — لا ‏422 «طَلَبٌ غَيرُ صالِح» عَن طَلَبٍ صالِح، ولا رَمزَ جَديدَ في عَقدِه.</summary>
    [Fact]
    public void The_api_answers_a_disputed_sibling_cancel_with_the_shared_invoice_409()
    {
        var outcome = ACommerce.Templates.Customer.Marketplace.Api.DealApi.FromCancel(
            DealCancelResult.Refused(new DealCancelViolation(GatewayInvoiceShare.Disputed, "…")));
        Assert.Equal(409, outcome.Status);
        Assert.Contains("\"deal_invoice_shared\"", ACommerce.Templates.Customer.Marketplace.Api.ApiJson.Serialize(outcome.Payload), StringComparison.Ordinal);
    }

    /// <summary>الدالَّةُ النَقِيَّة — مُوجَبٌ وسالِبٌ لِكُلِّ شَرط: أُختٌ مُسَمّاةٌ عَلى الفاتورَةِ نَفسِها في «نِزاع» لَم تُدفَع ⇒ تُعَدّ؛ وقائِمَةٌ، ومُلغاةٌ، ومَقبوضَةٌ، وعَلى فاتورَةٍ أُخرى، وغَيرُ مُسَمّاةٍ ⇒ لا.</summary>
    [Theory]
    [InlineData("disputed", true)]
    [InlineData("active", false)]
    [InlineData("cancelled", false)]
    [InlineData("captured", false)]
    [InlineData("another_invoice", false)]
    [InlineData("not_listed", false)]
    public void DisputedOnInvoice_counts_a_listed_unpaid_disputed_sibling_on_the_same_invoice_only(string how, bool counted)
    {
        var (a, b) = (Guid.NewGuid(), Guid.NewGuid());
        Deal On(Guid id, DealStatus status) => new()
        {
            Id = id, TenantSlug = Slug, Status = status, AmountSar = 10m,
            Refs = new Dictionary<string, string>
            {
                [GatewayPaymentProof.InvoiceRef] = InvoiceId,
                [GatewayPayments.InvoiceDealsRef] = GatewayInvoiceShare.ValueFor(new[] { a, b }),
            },
        };
        var (deal, sibling) = (On(a, DealStatus.Active), On(b, how switch { "active" => DealStatus.Active, "cancelled" => DealStatus.Cancelled, _ => DealStatus.Disputed }));
        if (how == "captured") sibling.Refs["payment_status"] = nameof(PaymentStatus.Captured);
        if (how == "another_invoice") sibling.Refs[GatewayPaymentProof.InvoiceRef] = Guid.NewGuid().ToString();
        if (how == "not_listed") deal.Refs[GatewayPayments.InvoiceDealsRef] = GatewayInvoiceShare.ValueFor(new[] { a, Guid.NewGuid() });
        Assert.Equal(counted, GatewayInvoiceShare.DisputedOnInvoice(deal, new[] { sibling }).Count == 1);
        Assert.Equal(counted ? GatewayInvoiceShare.Disputed : how == "active" ? GatewayInvoiceShare.Shared : null,
            GatewayInvoiceShare.Validate(deal, new[] { sibling }).FirstOrDefault()?.Code);
    }
}
