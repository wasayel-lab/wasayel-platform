using ACommerce.Kit.Auth;
using ACommerce.Kit.Notifications;
using ACommerce.Kit.Tenants;
using ACommerce.Platform.I18n;
using ACommerce.Templates.Customer.Marketplace.Components.Pages;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using Marten;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ طَلَبُ الاستِرجاعِ في مَتجَرٍ بِلا صاحِبٍ مُعَيَّن — تَوصيفُ السِلسِلَةِ قَبلَ التَبديل (‏2026-11-05، الموجَة ١٢ G1b — مُراجَعَةُ الإصلاح ١، G1b-R1-1) ═══
//
// المَتجَرُ بِلا صاحِبٍ مُعَيَّن (`Tenant.OwnerUserId` فارِغ) واقِعٌ لا افتِراض: عَلى `wasayel_w12_seed` ثَلاثَةُ مَتاجِرَ كَذلك (ashare وejar وorder)، والمُستَورِدُ
// لا يُعَيِّنُ صاحِباً. و`573f6d4` (G1 · ٤) أَخبَرَ صاحِبَ المَتجَرِ حينَ يَغيبُ البائِع — فَحينَ لا صاحِبَ يَرُدُّ `NotifyStoreOwnerAsync` بِـfalse
// ويَعودُ `NotifyReturnRequestedAsync` بِلا كِتابَة.
//
// **ما يُثَبَّتُ هُنا لِيَبقى** (القاعِدَة ٣): بابُ الاستِرجاعِ بِجِسمِه يَعودُ إلى الصَفقَةِ بِلا رَمز، ويَكتُبُ سَطرَ «طَلَبَ المُشتَري الاستِرجاع» مَرَّةً، ولا يُخبِرُ المُشتَريَ
// نَفسَه ولا يَكتُبُ في صُندوقِ البائِعِ المَمحُوّ؛ وصَفحَةُ الصَفقَةِ تَرسُمُ استِمارَةَ الاستِرجاعِ ولا تَرسُمُ لِلمَمحُوِّ بابَ مُحادَثَة. **والسالِب**: في المَتجَرِ نَفسِه بِلا صاحِب
// والبائِعُ قائِمٌ يُخبَرُ البائِعُ مَرَّةً، ولا يُلحَقُ مَرجِعُ «مَن أُخبِر»، والسِجِلُّ «أُخبِرَ البائِع»، وبابُ المُحادَثَةِ مَرسوم. وهذا ما لا يَمَسُّه التَبديل.
// **وما يُطبَعُ ولا يُثَبَّت**: اليَومَ — والبائِعُ مَمحُوٌّ ولا صاحِب — لا يُكتَبُ إشعارٌ لِأَحَد، ولا يُلحَقُ `return_told`، والسِجِلُّ يَقولُ «أُخبِرَ البائِع»، والتَلميحُ قَبلَ
// الطَلَبِ يَقولُ «فَتُخبَرُ إدارَةُ المَتجَرِ بِطَلَبِك» (`vendorGone` وَحدَه)، ورَدُّ التَكرارِ «أُخبِرَ البائِع — تابِع مَعَه». وهذا المِلَفُّ يَبقى بِبَصمَتِه.

[Collection(LiveProofCollection.Name)]
public class LiveChatPartnerGoneOwnerlessStoreProofTests(ITestOutputHelper output)
{
    private static bool Enabled
        => LiveDealConcurrencyProofTests.Enabled && !string.IsNullOrEmpty(LiveDealConcurrencyProofTests.ConnectionString);

    /// <summary>مَتجَرٌ <b>بِلا صاحِبٍ مُعَيَّن</b>، ومُشتَرٍ، وبائِعٌ (مَمحُوٌّ إن طُلِب)، وصَفقَةُ بَيعِ سِلعَةٍ مِن سَلَّةٍ سُلِّمَت أَمسِ ومُكتَمِلَة — نَسَقُ
    /// <see cref="LiveChatPartnerGoneReturnProofTests.SeedAsync"/> بِحَرفِه والمَتجَرُ وَحدَه يَختَلِف (<c>OwnerUserId = Guid.Empty</c>) وسلاجُه ومُعَرِّفاتُه.</summary>
    internal static async Task<LiveChatPartnerGoneReturnProofTests.Seeded> SeedAsync(IDocumentStore store, bool vendorGone)
    {
        var tag = vendorGone ? "gone" : "live";
        var slug = $"zz-retnoown-{tag}";
        Guid Fixed(int n) => Guid.Parse($"{(vendorGone ? "0c" : "0d")}{n:D6}-0000-4000-8000-000000000412");
        var day = DateTime.UtcNow.Date;
        var buyer = new User { Id = Fixed(2), TenantSlug = slug, FullName = "مُشتَرٍ في مَتجَرٍ بِلا صاحِب", Phone = "+966500000621", PhoneVerified = true, CreatedAt = day.AddDays(-30) };
        var vendor = new User { Id = Fixed(3), TenantSlug = slug, FullName = "بائِعٌ في مَتجَرٍ بِلا صاحِب", Phone = "+966500000622", PhoneVerified = true, CreatedAt = day.AddDays(-30) };
        if (vendorGone) vendor = AccountDeletion.Erase(vendor, day.AddHours(3));
        var deal = new Deal
        {
            Id = Fixed(4), TenantSlug = slug, ListingId = Fixed(5), ListingTitle = "سِلعَةٌ تُستَرجَع", Pattern = DealReturnRequest.GoodsPattern,
            InitiatorId = buyer.Id, InitiatorName = buyer.FullName, CounterpartyId = vendor.Id, CounterpartyName = vendorGone ? AccountDeletion.ErasedName : vendor.FullName,
            AmountSar = 150m, Status = DealStatus.Completed, Stage = DealStage.Received,
            Attributes = new() { [CheckoutPayMethods.AttributeKey] = "cash" },
            Refs = new() { [DealsService.ListingOwnerRef] = vendor.Id.ToString() },
            CreatedAt = day.AddDays(-3), UpdatedAt = day.AddDays(-1).AddHours(10),
        };
        deal.Timeline.Add(new(DealStage.Offered, DealStage.Booked, "advanced", vendor.Id, deal.CounterpartyName, null, day.AddDays(-3).AddHours(9)));
        deal.Timeline.Add(new(DealStage.Booked, DealStage.Delivered, "advanced", vendor.Id, deal.CounterpartyName, null, day.AddDays(-1).AddHours(9)));
        deal.Timeline.Add(new(DealStage.Delivered, DealStage.Received, "advanced", vendor.Id, deal.CounterpartyName, null, day.AddDays(-1).AddHours(10)));
        await LiveChatPartnerGoneReturnProofTests.CleanAsync(store, slug);
        await using (var g = store.LightweightSession())
        {
            g.Store(new Tenant { Id = slug, Name = "مَتجَرٌ بِلا صاحِب", OwnerUserId = Guid.Empty });
            await g.SaveChangesAsync();
        }
        await using (var s = store.LightweightSession(slug))
        {
            s.Store(buyer, vendor);
            s.Store(deal);
            await s.SaveChangesAsync();
        }
        return new LiveChatPartnerGoneReturnProofTests.Seeded(slug, Guid.Empty, buyer, vendor, deal);
    }

    /// <summary>صَفحَةُ الصَفقَةِ لِلمُشتَري بِرَمزِ رَدٍّ في العُنوانِ أَو بِدونِه — نَسَقُ <see cref="LiveChatPartnerGoneReturnProofTests.RenderDealPageAsync"/> بِحَرفِه وعُنوانُه
    /// وَحدَه يَزيد، والنَصُّ مَفكوكُ الكِيانات.</summary>
    internal static async Task<string> RenderAsync(LiveTemplateDoorHost host, LiveChatPartnerGoneReturnProofTests.Seeded seeded, string? err)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var ctx = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        ctx.Request.Scheme = "http";
        ctx.Request.Host = new HostString("localhost");
        ctx.Request.Path = $"/{seeded.Slug}/deals/{seeded.Deal.Id}";
        ctx.Request.Headers.Cookie = $"{ACommerce.Templates.Customer.Marketplace.AuthSession.CookieName(seeded.Slug)}={seeded.Token}";
        if (scope.ServiceProvider.GetService<IHttpContextAccessor>() is { } accessor) accessor.HttpContext = ctx;
        if (scope.ServiceProvider.GetService<NavigationManager>() is Microsoft.AspNetCore.Components.Routing.IHostEnvironmentNavigationManager nav)
            nav.Initialize("http://localhost/", $"http://localhost/{seeded.Slug}/deals/{seeded.Deal.Id}" + (err is null ? "" : $"?err={err}"));
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, NullLoggerFactory.Instance);
        return System.Net.WebUtility.HtmlDecode(await renderer.Dispatcher.InvokeAsync(async () =>
            (await renderer.RenderComponentAsync<MyDealDetail>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(MyDealDetail.Slug)] = seeded.Slug,
                [nameof(MyDealDetail.Id)] = seeded.Deal.Id,
            }))).ToHtmlString()));
    }

    internal static string Text(string key) => LocaleCatalog.Find(LocaleCatalog.Arabic, key)!;

    /// <summary>ما يُرسَمُ وما يُكتَبُ في جَولَةٍ واحِدَة: الصَفحَةُ قَبلَ الطَلَب، ثُمَّ البابُ بِجِسمِه، ثُمَّ ما في القاعِدَة، ثُمَّ الصَفحَةُ بِرَدِّ التَكرار.</summary>
    internal sealed record Round(string Before, int Status, string Location, Deal Deal, IReadOnlyList<Notification> Notices, string After);

    internal static async Task<Round> RunAsync(LiveTemplateDoorHost host, LiveChatPartnerGoneReturnProofTests.Seeded seeded)
    {
        var before = await RenderAsync(host, seeded, null);
        var (status, at) = await LiveChatPartnerGoneReturnProofTests.ReturnAsync(host, seeded);
        await using var q = host.Store.QuerySession(seeded.Slug);
        var deal = (await q.LoadAsync<Deal>(seeded.Deal.Id))!;
        var notices = await Marten.QueryableExtensions.ToListAsync(q.Query<Notification>());
        var after = await RenderAsync(host, seeded, DealReturnRequest.AlreadyRequested);
        return new Round(before, status, at, deal, notices.ToList(), after);
    }

    /// <summary>البائِعُ مَمحُوٌّ ولا صاحِبَ لِلمَتجَر. <b>يُثَبَّت</b>: البابُ ⇐ الصَفقَةُ بِلا رَمز، سَطرٌ واحِد، لا شَيءَ لِلمُشتَري ولا لِلمَمحُوّ، والاستِمارَةُ مَرسومَةٌ قَبلَه وبابُ المُحادَثَةِ
    /// لا. <b>ويُطبَعُ</b>: عَدَدُ الإشعاراتِ كُلِّها، ومَرجِعُ «مَن أُخبِر»، وأَيُّ تَلميحٍ قَبلَ الطَلَب، وأَيُّ سَطرٍ في السِجِلّ، وأَيُّ رَدِّ تَكرار.</summary>
    [Fact]
    public async Task In_a_store_with_no_owner_the_return_on_an_erased_vendors_deal_writes_one_line_and_nothing_to_the_buyer_or_the_erased_vendor_on_Postgres()
    {
        if (!Enabled) return;
        await using var host = LiveTemplateDoorHost.Build();
        var gone = await SeedAsync(host.Store, vendorGone: true);
        try
        {
            var r = await RunAsync(host, gone);
            output.WriteLine($"مَتجَرٌ بِلا صاحِب · البائِعُ مَمحُوّ: قَبلَ الطَلَب: تَلميحُ المَتجَر {r.Before.Contains("فَتُخبَرُ إدارَةُ المَتجَرِ بِطَلَبِك")} · "
                + $"تَلميحُ البائِع {r.Before.Contains("يُخبَرُ البائِعُ بِطَلَبِك")} · بابُ المُحادَثَة {r.Before.Contains(LiveChatPartnerGoneReturnProofTests.ChatAction(gone))} · "
                + $"البابُ: {r.Status} → {r.Location} · الإشعاراتُ كُلُّها {r.Notices.Count} · return_told {(r.Deal.Refs.TryGetValue(DealNotices.ReturnToldRef, out var told) ? told : "غائِب")} · "
                + $"السِجِلّ «أُخبِرَ البائِع» {r.After.Contains(Text("deals.detail.event_return_requested"))} · "
                + $"رَدُّ التَكرار «أُخبِرَ البائِع» {r.After.Contains(Text("deals.detail.err_return_already_requested"))}");
            Assert.True(r.Before.Length > 2000, $"أَداةٌ عَمياء: الصَفحَةُ {r.Before.Length} بايت.");
            Assert.Contains(LiveChatPartnerGoneReturnProofTests.ReturnAction(gone), r.Before, StringComparison.Ordinal);
            Assert.DoesNotContain(LiveChatPartnerGoneReturnProofTests.ChatAction(gone), r.Before, StringComparison.Ordinal);
            Assert.Equal((302, $"/{gone.Slug}/deals/{gone.Deal.Id}"), (r.Status, r.Location));
            Assert.Single(r.Deal.Timeline, e => e.Action == DealEvent.ReturnRequested);
            Assert.Equal(0, r.Notices.Count(n => n.UserId == gone.Buyer.Id));
            Assert.Equal(0, r.Notices.Count(n => n.UserId == gone.Vendor.Id));
        }
        finally { await LiveChatPartnerGoneReturnProofTests.CleanAsync(host.Store, gone.Slug); }
    }

    /// <summary><b>السالِب</b> — المَتجَرُ نَفسُه بِلا صاحِب والبائِعُ قائِم: يُخبَرُ البائِعُ مَرَّةً ولا أَحَدَ سِواه، ولا مَرجِعَ «مَن أُخبِر»، والسِجِلُّ ورَدُّ التَكرارِ والتَلميحُ
    /// «أُخبِرَ البائِع»، وبابُ المُحادَثَةِ مَرسوم.</summary>
    [Fact]
    public async Task In_a_store_with_no_owner_a_live_vendor_is_told_once_and_the_deal_keeps_the_vendor_told_texts_on_Postgres()
    {
        if (!Enabled) return;
        await using var host = LiveTemplateDoorHost.Build();
        var live = await SeedAsync(host.Store, vendorGone: false);
        try
        {
            var r = await RunAsync(host, live);
            output.WriteLine($"مَتجَرٌ بِلا صاحِب · البائِعُ قائِم: البابُ: {r.Status} → {r.Location} · لِلبائِع {r.Notices.Count(n => n.UserId == live.Vendor.Id)} · "
                + $"الإشعاراتُ كُلُّها {r.Notices.Count} · مَرجِعُ «مَن أُخبِر» {r.Deal.Refs.ContainsKey(DealNotices.ReturnToldRef)} · "
                + $"التَلميح «يُخبَرُ البائِع» {r.Before.Contains("يُخبَرُ البائِعُ بِطَلَبِك")} · السِجِلّ «أُخبِرَ البائِع» {r.After.Contains(Text("deals.detail.event_return_requested"))} · "
                + $"بابُ المُحادَثَة {r.Before.Contains(LiveChatPartnerGoneReturnProofTests.ChatAction(live))}");
            Assert.Equal((302, $"/{live.Slug}/deals/{live.Deal.Id}"), (r.Status, r.Location));
            var n = Assert.Single(r.Notices);
            Assert.Equal((live.Vendor.Id, $"/{live.Slug}/deals/{live.Deal.Id}"), (n.UserId, n.RelatedUrl));
            Assert.False(r.Deal.Refs.ContainsKey(DealNotices.ReturnToldRef));
            Assert.Contains("يُخبَرُ البائِعُ بِطَلَبِك", r.Before, StringComparison.Ordinal);
            Assert.Contains(LiveChatPartnerGoneReturnProofTests.ChatAction(live), r.Before, StringComparison.Ordinal);
            Assert.Contains(Text("deals.detail.event_return_requested"), r.After, StringComparison.Ordinal);
            Assert.Contains(Text("deals.detail.err_return_already_requested"), r.After, StringComparison.Ordinal);
            Assert.DoesNotContain("أُخبِرَت إدارَةُ المَتجَر", r.After, StringComparison.Ordinal);
        }
        finally { await LiveChatPartnerGoneReturnProofTests.CleanAsync(host.Store, live.Slug); }
    }
}
