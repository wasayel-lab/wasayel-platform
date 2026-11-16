using ACommerce.Kit.Notifications;
using ACommerce.Kit.Payments;
using ACommerce.Platform.I18n;
using ACommerce.Templates.Customer.Marketplace.Services;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.Abstractions;
using static ACommerce.Platform.Tests.TenantGatewayEndpointsTests;

namespace ACommerce.Platform.Tests;

// ═══ طَلَبٌ مَدفوعٌ أُلغِيَ، وفاتورَةٌ تَعَذَّرَ إلغاؤها — مَن يَعلَمُ أَنّ عَلَيه أَن يَفعَل؟ (قَرارُ المالِك د3، ‏2026-10-29) ═══════════════════
//
// **القَرار (د3 «أ» (١) و«د» (٢))**: الاستِرجاعُ يَبقى يَدَوِيّاً مِن لَوحَةِ ميسر — «سطر في سجلّ الطلب يراه المشتري وصاحب المتجر، ومعه إشعار لصاحب
// المتجر بكل استرجاع مستحقّ وبكل فاتورة تعذّر إلغاؤها». **ما فُحِصَ في الكود**: `DealsService` يَكتُبُ سَطرَ `gateway_refund_due` عَلى الطَلَبِ المَدفوعِ
// المُلغى (الإلغاءُ بَعدَ الدَفع، والتَأكيدُ بَعدَ الإلغاء)، و`GatewayInvoiceCloser` لا يَفعَلُ عِندَ الفَشَلِ إلّا سَطرَ لوغ، وإشعاراتُ الإلغاءِ لِلمُشتَري
// (ولِلبائِعِ مُنذُ د8) لا لِصاحِبِ المَتجَر.
//
// **التَوصيفُ قَبلَ التَبديل** (القاعِدَة ٣): صُفوفُ «Today_» أَدناه تُودَعُ وَحدَها، وتُقلَبُ بِأَسمائِها في كوميتِ التَبديل. وصاحِبُ المَتجَرِ هُنا
// `Tenant.OwnerUserId` — مَن رَبَطَ مِفتاحَ ميسر في الاستوديو، ولِلوَحَتِه يُحالُ الاستِرجاع.

public class GatewayOwnerMoneyNoticeTests(ITestOutputHelper output)
{
    internal static readonly Guid Owner = Guid.Parse("dddddddd-dddd-4ddd-8ddd-dddddddddddd");

    private static Deal Load(World w, Guid id) => w.Docs.Read<Deal>(id.ToString())!;

    internal static DealNotices Notices(DocWorld docs)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSignalR();
        var hub = services.BuildServiceProvider().GetRequiredService<IHubContext<ACommerce.Kit.Realtime.Server.RealtimeHub>>();
        return new DealNotices(docs.Store, hub, new WebPushService(new ConfigurationBuilder().Build(), NullLogger<WebPushService>.Instance));
    }

    /// <summary>عالَمُ السَلَّةِ المَدفوعَة ومَعَه وَثيقَةُ المَتجَرِ بِمالِكِها، وخِدمَةُ صَفَقاتٍ بِمُلغي فاتورَةٍ بِلا عَميلٍ — فَكُلُّ إغلاقٍ يَعودُ
    /// <see cref="MoyasarFailures.NotConfigured"/>: فاتورَةٌ تَعَذَّرَ إلغاؤها.</summary>
    internal static async Task<(World W, DealsService Deals)> OwnedCart()
    {
        var w = await PaidCart(withSeller: true);
        w.Docs.Put(new ACommerce.Kit.Tenants.Tenant { Id = Slug, Name = "مَتجَر", OwnerUserId = Owner });
        var closer = new GatewayInvoiceCloser(new TenantProviderService(w.Docs.Store), NewVault(), client: null);
        return (w, new DealsService(w.Docs.Store, w.Platform, closer));
    }

    private static int ToOwner(World w) => w.Docs.Stored.OfType<Notification>().Count(n => n.UserId == Owner);

    // ═══ قُلِبَت بِأَسمائِها (‏2026-10-29، قَرارُ المالِك د3 «د»؛ تَوصيفُها `b55fead`) ═══════════════════════════════════════════

    /// <summary><b>قُلِبَ بِاسمِه</b> — كانَ <c>Today_a_refund_due_cancel_tells_the_store_owner_nothing_and_the_buyer_line_sends_him_to_the_store_dashboard</c> («لِلمالِك: 0»،
    /// وسَطرُ المُشتَري يُحيلُه إلى «لَوحَةِ بَوّابَةِ المَتجَر»). والآن صاحِبُ المَتجَرِ يُخبَرُ مَرَّةً بِنَصِّ القَرارِ — المُنتَجُ والمَبلَغُ وعُملَةُ الفاتورَة — ورابِطُه صَفحَةُ
    /// الصَفقَةِ في الاستوديو؛ وسَطرُ المُشتَري يُطَمئِنُه عَلى مالِه.</summary>
    [Fact]
    public async Task A_refund_due_cancel_tells_the_store_owner_once_with_the_amount_and_the_invoice_currency()
    {
        var (w, deals) = await OwnedCart();
        await deals.AttachRefAsync(Slug, w.Ids[0], "payment_id", InvoiceId);
        await deals.AttachRefAsync(Slug, w.Ids[0], "payment_status", nameof(PaymentStatus.Captured));
        await deals.AttachRefAsync(Slug, w.Ids[0], DealsService.PaymentCurrencyRef, "SAR");

        var res = await deals.CancelAsync(Slug, w.Ids[0], new DealCanceller(Seller, "بائِع", IsStoreAdmin: false), "نَفِدَت");
        Assert.True(res.Ok, res.Violation?.MessageAr);
        var l = new L(new HttpContextAccessor());
        await Notices(w.Docs).NotifyCancelledAsync(Slug, res.Deal!, Seller, "بائِع", l);

        var told = Assert.Single(w.Docs.Stored.OfType<Notification>(), n => n.UserId == Owner);
        output.WriteLine($"لِلمالِك: «{told.Title}» · «{told.Body}» · {told.RelatedUrl}");
        Assert.Equal(DealNotices.OwnerAction.RefundDue, DealNotices.OwnerActionOf(res.Deal!));
        Assert.Equal(l.Format("notifications.deal_refund_due.body", "سِلعَة", 100m, "SAR"), told.Body);
        Assert.Contains("ميسر", told.Body, StringComparison.Ordinal);
        Assert.Contains("SAR", told.Body, StringComparison.Ordinal);
        Assert.Equal($"/studio/apps/{Slug}/deals/{w.Ids[0]}", told.RelatedUrl);
        Assert.Equal("أُلغِيَ الطَّلَبُ بَعدَ دَفعِه — أُبلِغَ المَتجَرُ بِرَدِّ المَبلَغِ إلى بِطاقَتِك.",
            LocaleCatalog.Text(LocaleCatalog.Arabic, "deals.detail.event_gateway_refund_due"));
    }

    /// <summary><b>قُلِبَ بِاسمِه</b> — كانَ <c>Today_a_failed_invoice_cancel_writes_no_line_and_tells_the_store_owner_nothing</c> (السِجِلُّ يَنتَهي بِـ«cancelled»). والآن الفاتورَةُ
    /// الَّتي تَعَذَّرَ إلغاؤها سَطرٌ في السِجِلِّ بِرَمزِه (<see cref="DealEvent.GatewayInvoiceLeftOpen"/>) — يَراه المُشتَري «لا تَدفَع» والاستوديو «ألغِها» — وإشعارٌ لِصاحِبِ المَتجَر.</summary>
    [Fact]
    public async Task A_failed_invoice_cancel_writes_its_line_and_tells_the_store_owner_once()
    {
        var (w, deals) = await OwnedCart();
        await w.Deals.CancelAsync(Slug, w.Ids[1], new DealCanceller(Seller, "بائِع", IsStoreAdmin: false), "نَفِدَت");

        var res = await deals.CancelAsync(Slug, w.Ids[0], new DealCanceller(Seller, "بائِع", IsStoreAdmin: false), "نَفِدَت");
        Assert.True(res.Ok, res.Violation?.MessageAr);
        var l = new L(new HttpContextAccessor());
        await Notices(w.Docs).NotifyCancelledAsync(Slug, res.Deal!, Seller, "بائِع", l);

        var deal = Load(w, w.Ids[0]);
        output.WriteLine($"السِجِلّ: {string.Join(" · ", deal.Timeline.Select(e => e.Action))} · لِلمالِك: {ToOwner(w)}");
        Assert.Equal(DealStatus.Cancelled, deal.Status);
        var line = deal.Timeline[^1];
        Assert.Equal((DealEvent.GatewayInvoiceLeftOpen, MoyasarFailures.NotConfigured), (line.Action, line.Note));
        var told = Assert.Single(w.Docs.Stored.OfType<Notification>(), n => n.UserId == Owner);
        Assert.Equal(l.Format("notifications.deal_invoice_left_open.body", "سِلعَة"), told.Body);
        foreach (var page in new[] { "MyDealDetail.razor", "StudioAppDealDetail.razor" })
            Assert.Contains("DealEvent.GatewayInvoiceLeftOpen => L[", File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, "libs", "templates",
                "ACommerce.Templates.Customer.Marketplace", "Components", "Pages", page)), StringComparison.Ordinal);
    }

    /// <summary><b>قُلِبَ بِاسمِه</b> — كانَ <c>Today_a_payment_proven_on_a_cancelled_deal_tells_the_store_owner_nothing</c>. والآن دَفعٌ يَثبُتُ عَلى صَفقَتَينِ مُلغاتَين يُخبِرُ صاحِبَ
    /// المَتجَرِ بِاستِرجاعٍ مُستَحَقٍّ لِكُلٍّ مِنهُما — وعُملَةُ الفاتورَةِ مَكتوبَةٌ عَلى الصَفقَةِ مِن البُرهانِ نَفسِه — وإشعارُ الدَفعِ لِلبائِعِ صِفرٌ كَما كان.</summary>
    [Fact]
    public async Task A_payment_proven_on_cancelled_deals_tells_the_store_owner_of_each_refund_due()
    {
        var (w, deals) = await OwnedCart();
        foreach (var id in w.Ids)
            await w.Deals.CancelAsync(Slug, id, new DealCanceller(Buyer, "مُشتَرٍ", IsStoreAdmin: false), "عَدَلتُ");
        var proven = GatewayPaymentProof.Gate(new MoyasarInvoice(InvoiceId, "paid", 15000, "SAR", null, GatewayPaymentProof.MetadataFor(w.Ids)),
            w.Ids.Select(id => Load(w, id)).ToArray()).Payment!;

        var confirmed = await deals.ConfirmGatewayPaymentAsync(Slug, proven);
        var sent = await Notices(w.Docs).NotifyPaidAsync(Slug, confirmed.Select(d => d.Id).ToArray(), new L(new HttpContextAccessor()));

        output.WriteLine($"كُتِبَ عَلى {confirmed.Count} · إشعارُ الدَفع: {sent} · لِلمالِك: {ToOwner(w)}");
        Assert.Equal(2, confirmed.Count);
        Assert.All(confirmed, d => Assert.Equal("SAR", d.Refs[DealsService.PaymentCurrencyRef]));
        Assert.Equal(0, sent);
        Assert.Equal(2, ToOwner(w));
    }

    // ═══ المُوجِبُ والسالِبُ لِكُلِّ قاعِدَة ═══════════════════════════════════════════════════════════════════════

    /// <summary><b>ما يُبقي الفاتورَةَ قابِلَةً لِلدَّفع</b> — مَعجَمٌ مُغلَقٌ عَلى رُموزِ المُلغي: المُوجِبُ كُلُّ عُطلٍ أَو رَدٍّ لا يُغلِقُها؛ والسالِبُ «أُلغِيَت» و«تَدفَعُ لِأُختٍ»
    /// و«لَيسَت مَفتوحَة» و«لا فاتورَة».</summary>
    [Theory]
    [InlineData(MoyasarFailures.NotConfigured, true)]
    [InlineData(GatewayInvoiceCloser.Faulted, true)]
    [InlineData(GatewayInvoiceCloser.NotCancelled, true)]
    [InlineData(GatewayInvoiceClosing.NotListed, true)]
    [InlineData("gateway_unreachable", true)]
    [InlineData(null, false)]
    [InlineData(GatewayInvoiceClosing.StillOwed, false)]
    [InlineData(GatewayInvoiceClosing.NotOpen, false)]
    [InlineData(DealPayAuthorization.InvoiceMissing, false)]
    public void LeavesPayable_is_every_closer_outcome_but_cancelled_still_owed_not_open_or_missing(string? code, bool payable)
        => Assert.Equal(payable, GatewayInvoiceCloser.LeavesPayable(code));

    /// <summary><b>ما عَلى صاحِبِ المَتجَر</b> — الآخِرُ مِن السَطرَين: المُوجِب لِكُلٍّ، والسالِبُ صَفقَةٌ بِلا أَيٍّ مِنهُما، واستِرجاعٌ عَلى صَفقَةٍ لَم تُلغَ؛ وصَفقَةٌ تَعَذَّرَ إلغاءُ
    /// فاتورَتِها ثُمَّ دُفِعَت صارَ عَلَيها استِرجاع.</summary>
    [Fact]
    public void OwnerActionOf_is_the_latest_of_the_two_lines_on_a_cancelled_deal()
    {
        Deal With(DealStatus status, params string[] actions)
        {
            var d = new Deal { Status = status };
            foreach (var a in actions) d.Timeline.Add(new(DealStage.Offered, DealStage.Offered, a, null, null, null, DateTime.UtcNow));
            return d;
        }
        Assert.Equal(DealNotices.OwnerAction.RefundDue, DealNotices.OwnerActionOf(With(DealStatus.Cancelled, "cancelled", DealEvent.GatewayRefundDue)));
        Assert.Equal(DealNotices.OwnerAction.InvoiceLeftOpen, DealNotices.OwnerActionOf(With(DealStatus.Cancelled, "cancelled", DealEvent.GatewayInvoiceLeftOpen)));
        Assert.Equal(DealNotices.OwnerAction.RefundDue,
            DealNotices.OwnerActionOf(With(DealStatus.Cancelled, "cancelled", DealEvent.GatewayInvoiceLeftOpen, DealEvent.GatewayPaid, DealEvent.GatewayRefundDue)));
        Assert.Null(DealNotices.OwnerActionOf(With(DealStatus.Cancelled, "cancelled")));
        Assert.Null(DealNotices.OwnerActionOf(With(DealStatus.Active, DealEvent.GatewayRefundDue)));
    }

    /// <summary><b>صاحِبُ المَتجَرِ مَن يُسَمّيه المَتجَر</b>: المالِكُ المُعَيَّن — وسالِبُه مَتجَرٌ بِلا مالِكٍ أَو غائِبٌ فَلا يُكتَبُ شَيء.</summary>
    [Fact]
    public async Task The_store_owner_is_the_tenant_owner_and_a_store_without_one_is_told_nothing()
    {
        Assert.Equal(Owner, DealNotices.StoreOwnerOf(new ACommerce.Kit.Tenants.Tenant { Id = Slug, OwnerUserId = Owner }));
        Assert.Null(DealNotices.StoreOwnerOf(new ACommerce.Kit.Tenants.Tenant { Id = Slug, OwnerUserId = Guid.Empty }));
        Assert.Null(DealNotices.StoreOwnerOf(null));

        var (w, deals) = await OwnedCart();
        w.Docs.Put(new ACommerce.Kit.Tenants.Tenant { Id = Slug, Name = "مَتجَر", OwnerUserId = Guid.Empty });
        await w.Deals.CancelAsync(Slug, w.Ids[1], new DealCanceller(Seller, "بائِع", IsStoreAdmin: false), "نَفِدَت");
        var res = await deals.CancelAsync(Slug, w.Ids[0], new DealCanceller(Seller, "بائِع", IsStoreAdmin: false), "نَفِدَت");
        Assert.False(await Notices(w.Docs).NotifyStoreOwnerAsync(Slug, res.Deal!, new L(new HttpContextAccessor())));
        Assert.Equal(0, w.Docs.Stored.OfType<Notification>().Count(n => n.Type == DealNotices.CancelledType && n.RelatedUrl.StartsWith("/studio/", StringComparison.Ordinal)));
    }
}
