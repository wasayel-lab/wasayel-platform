using ACommerce.Kit.Auth;
using ACommerce.Kit.Notifications;
using ACommerce.Kit.Tenants;
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

// ═══ طَلَبُ الاستِرجاعِ بَعدَ أَن أَغلَقَ البائِعُ حِسابَه — تَوصيفُ السِلسِلَةِ قَبلَ التَبديل (‏2026-11-04، الموجَة ١٢ G1 · ٤؛ XS-2 + D-3 + B-3) ═══
//
// **ما يُثَبَّتُ هُنا لِيَبقى** (القاعِدَة ٣): بابُ الاستِرجاعِ بِجِسمِه يَكتُبُ سَطرَ «طَلَبَ المُشتَري الاستِرجاع» مَرَّةً ويَعودُ إلى صَفحَةِ الصَفقَةِ بِلا رَمز، ولا يُخبِرُ
// المُشتَريَ نَفسَه؛ وحينَ يَكونُ البائِعُ قائِماً يُخبَرُ البائِعُ مَرَّةً ولا يُخبَرُ صاحِبُ المَتجَر؛ وصَفحَةُ الصَفقَةِ لِلمُشتَري تَرسُمُ استِمارَةَ الاستِرجاعِ في الحالَين،
// وتَرسُمُ بابَ «تَواصَل مَع البائِع» حينَ يَكونُ قائِماً. وهذا ما لا يَمَسُّه التَبديل.
// **وما يُطبَعُ ولا يُثَبَّت**: اليَومَ — والبائِعُ مَمحُوٌّ (د52 يُبقي وَثيقَتَه بِـ`DeletedAt`) — يُكتَبُ الإشعارُ في صُندوقِه هُوَ ولا يُخبَرُ صاحِبُ المَتجَر، ولا يُلحَقُ
// بِالصَفقَةِ ما يَقولُ مَن أُخبِر؛ والسِجِلُّ والتَلميحُ يَقولانِ «أُخبِرَ البائِع»، والصَفحَةُ تَرسُمُ استِمارَةَ مُحادَثَةٍ يَرُدُّها البابُ بِـ`partner_missing` (القاعِدَة ١٢).
// ويُطبَعُ بَصمَةُ صَفحَةِ البائِعِ القائِمِ (SHA-256 بِبَياناتٍ ثابِتَة) لِيُقارَنَ بَعدَ التَبديلِ بايتاً بِبايت (القاعِدَة ١٣). وهذا المِلَفُّ يَبقى بِبَصمَتِه.

[Collection(LiveProofCollection.Name)]
public class LiveChatPartnerGoneReturnProofTests(ITestOutputHelper output)
{
    private static bool Enabled
        => LiveDealConcurrencyProofTests.Enabled && !string.IsNullOrEmpty(LiveDealConcurrencyProofTests.ConnectionString);

    internal const string ReturnDoor = "/{slug}/deals/{id:guid}/return";

    /// <summary>مَتجَرٌ بِصاحِبٍ مُعَيَّن، ومُشتَرٍ، وبائِعٌ (مَمحُوٌّ إن طُلِب)، وصَفقَةُ بَيعِ سِلعَةٍ مِن سَلَّةٍ سُلِّمَت أَمسِ ومُكتَمِلَة — في نافِذَةِ الأَيّامِ السَبعَة.
    /// والمُعَرِّفاتُ ثابِتَةٌ لِكُلِّ حالَة، والأَوقاتُ مِن يَومِ التَشغيلِ — فَالصَفحَةُ تُرسَمُ بِالبايتاتِ نَفسِها في اليَومِ نَفسِه.</summary>
    internal sealed record Seeded(string Slug, Guid Owner, User Buyer, User Vendor, Deal Deal)
    {
        public string Token => ACommerce.Kit.Auth.Server.AuthHandlers.MakeToken(Buyer.Id, Slug, Buyer.SessionVersion);
    }

    internal static async Task<Seeded> SeedAsync(IDocumentStore store, bool vendorGone)
    {
        var tag = vendorGone ? "gone" : "live";
        var slug = $"zz-retgone-{tag}";
        Guid Fixed(int n) => Guid.Parse($"{(vendorGone ? "0e" : "0a")}{n:D6}-0000-4000-8000-000000000412");
        var owner = Fixed(1);
        var day = DateTime.UtcNow.Date;
        var buyer = new User { Id = Fixed(2), TenantSlug = slug, FullName = "مُشتَرٍ يَطلُبُ استِرجاعاً", Phone = "+966500000611", PhoneVerified = true, CreatedAt = day.AddDays(-30) };
        var vendor = new User { Id = Fixed(3), TenantSlug = slug, FullName = "بائِعٌ لِلاستِرجاع", Phone = "+966500000612", PhoneVerified = true, CreatedAt = day.AddDays(-30) };
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
        await CleanAsync(store, slug);
        await using (var g = store.LightweightSession())
        {
            g.Store(new Tenant { Id = slug, Name = "مَتجَرُ الاستِرجاع", OwnerUserId = owner });
            await g.SaveChangesAsync();
        }
        await using (var s = store.LightweightSession(slug))
        {
            s.Store(buyer, vendor);
            s.Store(deal);
            await s.SaveChangesAsync();
        }
        return new Seeded(slug, owner, buyer, vendor, deal);
    }

    internal static async Task CleanAsync(IDocumentStore store, string slug)
    {
        await using (var s = store.LightweightSession(slug))
        {
            s.DeleteWhere<Deal>(d => d.Id != Guid.Empty);
            s.DeleteWhere<Notification>(n => n.Id != Guid.Empty);
            await s.SaveChangesAsync();
        }
        await LiveZeroRoleAdminProofTests.CleanAsync(store, slug);
    }

    /// <summary>صَفحَةُ الصَفقَةِ كَما يَرسُمُها المُضيفُ لِلمُشتَري — بِخِدماتِ المُضيفِ المُرَكَّبِ وكوكيِّ جَلسَتِه، بِلا خادِم.</summary>
    internal static async Task<string> RenderDealPageAsync(LiveTemplateDoorHost host, Seeded seeded)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var ctx = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        ctx.Request.Scheme = "http";
        ctx.Request.Host = new HostString("localhost");
        ctx.Request.Path = $"/{seeded.Slug}/deals/{seeded.Deal.Id}";
        ctx.Request.Headers.Cookie = $"{ACommerce.Templates.Customer.Marketplace.AuthSession.CookieName(seeded.Slug)}={seeded.Token}";
        if (scope.ServiceProvider.GetService<IHttpContextAccessor>() is { } accessor) accessor.HttpContext = ctx;
        // ‏`[SupplyParameterFromQuery]` يَقرَأُ العُنوانَ مِن مُدَبِّرِ التَنَقُّل — يُهَيَّأُ بِعُنوانِ الصَفحَةِ كَما يُهَيِّئُه الخادِمُ لِطَلَبٍ حَقيقيّ.
        if (scope.ServiceProvider.GetService<NavigationManager>() is Microsoft.AspNetCore.Components.Routing.IHostEnvironmentNavigationManager nav)
            nav.Initialize("http://localhost/", $"http://localhost/{seeded.Slug}/deals/{seeded.Deal.Id}");
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, NullLoggerFactory.Instance);
        return await renderer.Dispatcher.InvokeAsync(async () =>
            (await renderer.RenderComponentAsync<MyDealDetail>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(MyDealDetail.Slug)] = seeded.Slug,
                [nameof(MyDealDetail.Id)] = seeded.Deal.Id,
            }))).ToHtmlString());
    }

    internal static string Sha(string html)
        => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(html))).ToLowerInvariant()[..16];

    internal static string ChatAction(Seeded seeded) => $"action=\"/{seeded.Slug}/users/{seeded.Vendor.Id}/chat\"";
    internal static string ReturnAction(Seeded seeded) => $"action=\"/{seeded.Slug}/deals/{seeded.Deal.Id}/return\"";

    internal static async Task<(int Status, string Location)> ReturnAsync(LiveTemplateDoorHost host, Seeded seeded)
        => await host.PostFormAsync(ReturnDoor, new Dictionary<string, string> { ["slug"] = seeded.Slug, ["id"] = seeded.Deal.Id.ToString() },
            seeded.Slug, seeded.Token, ("reason", "لا تُطابِقُ الوَصف"));

    /// <summary>صَفحَةُ الصَفقَةِ لِلمُشتَري قَبلَ طَلَبِ الاستِرجاع: البائِعُ القائِمُ ⇐ بابُ «تَواصَل مَع البائِع» واستِمارَةُ الاستِرجاع، والصَفحَةُ تُرسَمُ بِالبايتاتِ نَفسِها مَرَّتَين
    /// (أَداةُ البَصمَةِ مَقيسَة)؛ والبائِعُ المَمحُوُّ ⇐ استِمارَةُ الاستِرجاع. ويُطبَعُ: البَصمَة، وهَل تُرسَمُ استِمارَةُ المُحادَثَةِ ونَصُّ الغِيابِ لِلمَمحُوّ، ونَصُّ التَلميح.</summary>
    [Fact]
    public async Task The_buyers_deal_page_draws_the_return_form_and_the_live_vendors_chat_door_on_Postgres()
    {
        if (!Enabled) return;
        await using var host = LiveTemplateDoorHost.Build();
        var live = await SeedAsync(host.Store, vendorGone: false);
        var gone = await SeedAsync(host.Store, vendorGone: true);
        try
        {
            var livePage = await RenderDealPageAsync(host, live);
            var again = await RenderDealPageAsync(host, live);
            var gonePage = await RenderDealPageAsync(host, gone);
            var l = new ACommerce.Platform.I18n.L(new HttpContextAccessor());
            var goneText = System.Net.WebUtility.HtmlEncode(l["vendors.profile.contact_gone"]);
            output.WriteLine($"صَفحَةُ البائِعِ القائِم: {livePage.Length} بايت · بَصمَة {Sha(livePage)} · مُكَرَّرَة {Sha(again)} · بابُ المُحادَثَة {livePage.Contains(ChatAction(live))} · "
                + $"صَفحَةُ المَمحُوّ: بابُ المُحادَثَة {gonePage.Contains(ChatAction(gone))} · نَصُّ الغِياب {gonePage.Contains(goneText) || gonePage.Contains(l["vendors.profile.contact_gone"])} · "
                + $"استِمارَةُ الاستِرجاع {gonePage.Contains(ReturnAction(gone))}");
            Assert.True(livePage.Length > 2000, $"أَداةٌ عَمياء: الصَفحَةُ {livePage.Length} بايت.");
            Assert.Equal(livePage, again);
            Assert.Contains(ChatAction(live), livePage, StringComparison.Ordinal);
            Assert.Contains(ReturnAction(live), livePage, StringComparison.Ordinal);
            Assert.Contains(ReturnAction(gone), gonePage, StringComparison.Ordinal);
        }
        finally
        {
            await CleanAsync(host.Store, live.Slug);
            await CleanAsync(host.Store, gone.Slug);
        }
    }

    /// <summary>بابُ الاستِرجاعِ بِجِسمِه: يَعودُ إلى الصَفقَةِ بِلا رَمز، وسَطرٌ واحِدٌ في سِجِلِّها، ولا شَيءَ لِلمُشتَري؛ والبائِعُ القائِمُ يُخبَرُ مَرَّةً وصاحِبُ المَتجَرِ لا.
    /// ويُطبَعُ لِلبائِعِ المَمحُوّ: إشعاراتُه وإشعاراتُ صاحِبِ المَتجَرِ ومَراجِعُ الصَفقَة.</summary>
    [Fact]
    public async Task The_return_door_writes_one_line_and_tells_a_live_vendor_and_not_the_store_owner_on_Postgres()
    {
        if (!Enabled) return;
        await using var host = LiveTemplateDoorHost.Build();
        var live = await SeedAsync(host.Store, vendorGone: false);
        var gone = await SeedAsync(host.Store, vendorGone: true);
        try
        {
            async Task<(int, string, Deal, int Vendor, int Owner, int Buyer)> RunAsync(Seeded seeded)
            {
                var (status, at) = await ReturnAsync(host, seeded);
                await using var q = host.Store.QuerySession(seeded.Slug);
                var deal = (await q.LoadAsync<Deal>(seeded.Deal.Id))!;
                var notices = await Marten.QueryableExtensions.ToListAsync(q.Query<Notification>());
                return (status, at, deal, notices.Count(n => n.UserId == seeded.Vendor.Id), notices.Count(n => n.UserId == seeded.Owner), notices.Count(n => n.UserId == seeded.Buyer.Id));
            }
            var (ls, la, ld, lv, lo, lb) = await RunAsync(live);
            var (gs, ga, gd, gv, go, gb) = await RunAsync(gone);
            output.WriteLine($"البائِعُ القائِم: {ls} → {la} · سُطورُ الاستِرجاع {ld.Timeline.Count(e => e.Action == DealEvent.ReturnRequested)} · لِلبائِع {lv} · لِصاحِبِ المَتجَر {lo} · لِلمُشتَري {lb}");
            output.WriteLine($"البائِعُ المَمحُوّ: {gs} → {ga} · سُطورُ الاستِرجاع {gd.Timeline.Count(e => e.Action == DealEvent.ReturnRequested)} · لِلمَمحُوّ {gv} · لِصاحِبِ المَتجَر {go} · لِلمُشتَري {gb} · "
                + $"المَراجِع [{string.Join(", ", gd.Refs.Keys.OrderBy(k => k, StringComparer.Ordinal))}]");
            foreach (var (status, at, deal, buyerNotices, seeded) in new[] { (ls, la, ld, lb, live), (gs, ga, gd, gb, gone) })
            {
                Assert.Equal((302, $"/{seeded.Slug}/deals/{seeded.Deal.Id}"), (status, at));
                Assert.Single(deal.Timeline, e => e.Action == DealEvent.ReturnRequested);
                Assert.Equal(0, buyerNotices);
            }
            Assert.Equal((1, 0), (lv, lo));
        }
        finally
        {
            await CleanAsync(host.Store, live.Slug);
            await CleanAsync(host.Store, gone.Slug);
        }
    }
}
