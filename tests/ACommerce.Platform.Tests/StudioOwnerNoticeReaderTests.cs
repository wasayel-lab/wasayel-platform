using ACommerce.Kit.Culture;
using ACommerce.Kit.Notifications;
using ACommerce.Platform.I18n;
using ACommerce.Templates.Customer.Marketplace.Components;
using ACommerce.Templates.Customer.Marketplace.Services;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using ACommerce.Templates.Customer.Marketplace.Services.Queries;
using Marten;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ إشعارُ صاحِبِ المَتجَرِ بِالاستِرجاعِ والفاتورَةِ المَفتوحَة — مَن يَقرَؤُه؟ (مُراجَعَةُ تَيّارِ S3 الجَولَة ٢، ‏2026-10-29، كُبرى عَلى `d85bc0f`) ═══
//
// **قَرارُ المالِك د3 (٢) «د»**: «ومعه إشعار لصاحب المتجر بكل استرجاع مستحقّ وبكل فاتورة تعذّر إلغاؤها» — وسَبَبُه بِنَصِّه: «أن يُنبَّه من عليه
// الفعل لحظة وجوبه». **والمَقيس**: الإشعارُ يُكتَبُ في مُستَأجِرِ المَتجَرِ بِاسمِ `Tenant.OwnerUserId` — وهُوَ مُعَرِّفُ `StudioUser` في مُستَأجِرِ
// `_studio` (‏`StudioAuth.Tenant`، ويُقارِنُه `TenantAdminGuard` بِمُعَرِّفِ جَلسَةِ الاستوديو) — ولا صَندوقَ يَقرَأُ ذلكَ المُعَرِّفَ هُناك: صَفحَةُ
// «الإشعارات» في الواجِهَةِ تَقرَأُ بِمُعَرِّفِ جَلسَةِ العُضو (`AuthSession`)، والدَفعُ الفَوريُّ يُحَمِّلُ `User` المَتجَرِ بِذلكَ المُعَرِّفِ فَلا يَجِدُه،
// والجَرَسُ لا تَسمَعُه صَفحَةُ استوديو؛ ولَوحَةُ التَشغيلِ تَسرُدُ أَحدَثَ ثَمانِيَةِ إشعاراتٍ لِكُلِّ المُستَخدِمينَ عُنواناً ونَوعاً — بِلا رابِطٍ ولا مَتن.
// فَالإشعارُ مَكتوبٌ ولا يَبلُغُ مَن عَلَيه الفِعل (القاعِدَة ١٢).
//
// **التَوصيفُ قَبلَ التَبديل** (القاعِدَة ٣): صَفُّ «Today_» أُودِعَ وَحدَه (`63ad0de`)، وقُلِبَ بِاسمِه في كوميتِ التَبديل.
//
// **والتَبديل** (القاعِدَة ٨: لا أُنبوبَ رابِع): صَفحَةُ «العَمَلِيّات» في الاستوديو (`StudioAppDeals`) تَقرَأُ إشعاراتِ صاحِبِ المَتجَرِ بِالقارِئِ القائِمِ
// نَفسِه (`AccountQueries.NotificationsAsync`) بِمُعَرِّفِ جَلسَةِ الاستوديو بَعدَ فَحصِ المِلكِيَّة، ويَرسُمُها المُكَوِّنُ `StudioOwnerNotices` صُفوفاً تُنقَرُ إلى
// صَفحَةِ الصَفقَةِ بِعُنوانِها ومَتنِها، وما يُرسَمُ تَختارُه الدالَّةُ النَقِيَّةُ `DealNotices.OwnerNoticesOf`.
public class StudioOwnerNoticeReaderTests(ITestOutputHelper output)
{
    internal const string Slug = "s";

    internal static string Pages => Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, "libs", "templates",
        "ACommerce.Templates.Customer.Marketplace", "Components", "Pages");

    internal static string Code(string path)
        => WriteEndpointGuardTests.StripComments(WriteEndpointGuardTests.StripMarkupComments(File.ReadAllText(path)));

    /// <summary>صَفَحاتٌ تَقرَأُ إشعاراتِ صاحِبِها بِالقارِئِ الواحِد <c>AccountQueries.NotificationsAsync</c> — بِأَسمائِها.</summary>
    internal static List<string> NoticeReaders()
        => Directory.GetFiles(Pages, "*.razor")
            .Where(p => Code(p).Contains("NotificationsAsync(", StringComparison.Ordinal))
            .Select(p => Path.GetFileName(p)!).OrderBy(n => n, StringComparer.Ordinal).ToList();

    internal static Notification Notice(string? url, string title = "عُنوان", string body = "مَتن") => new()
    {
        Id = Guid.NewGuid(), UserId = Guid.NewGuid(), Type = DealNotices.CancelledType, Title = title, Body = body, RelatedUrl = url,
        At = new DateTime(2026, 10, 29, 9, 0, 0, DateTimeKind.Utc),
    };

    /// <summary>يَرسُمُ المُكَوِّنَ بِخَدَماتِه: سِياقُ الثَقافَةِ والقامُوس — بِلا قاعِدَةٍ ولا خادِم.</summary>
    internal static async Task<string> RenderAsync(string slug, IReadOnlyList<Notification> mine)
    {
        await using var services = new ServiceCollection()
            .AddSingleton<ICultureContext>(new MutableCultureContext())
            .AddSingleton(new L(new HttpContextAccessor()))
            .BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);
        return await renderer.Dispatcher.InvokeAsync(async () =>
            (await renderer.RenderComponentAsync<StudioOwnerNotices>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(StudioOwnerNotices.Slug)] = slug,
                [nameof(StudioOwnerNotices.Mine)] = mine,
            }))).ToHtmlString());
    }

    internal static int Links(string html) => System.Text.RegularExpressions.Regex.Matches(html, "<a class=\"studio-list-row\" href=\"").Count;

    /// <summary>
    /// <para><b>قُلِبَ «Today_» بِاسمِه</b> — كانَ <c>Today_the_store_owner_notice_is_written_for_the_studio_identity_and_no_studio_page_draws_it_as_a_link_with_its_body</c>
    /// (‏<c>63ad0de</c>: قُرّاءُ «إشعاراتي» <c>Notifications.razor</c> وَحدَها، وصَفَحاتُ الاستوديو تَرسُمُ رابِطاً صِفراً ومَتناً صِفراً). والكاتِبُ كَما كان: لِمُعَرِّفِ صاحِبِ
    /// المَتجَرِ في الاستوديو ورابِطُه صَفحَةُ الصَفقَةِ في الاستوديو. والآن قارِئانِ بِالدالَّةِ الواحِدَة: صَفحَةُ الواجِهَةِ بِجَلسَةِ العُضو، وصَفحَةُ «العَمَلِيّات» في
    /// الاستوديو بِجَلسَةِ الاستوديو بَعدَ فَحصِ المِلكِيَّة — ومُكَوِّنُها وَحدَه يَرسُمُ الرابِطَ والمَتن؛ ولَوحَةُ التَشغيلِ كَما كانَت.</para>
    /// </summary>
    [Fact]
    public void The_store_owner_notice_is_written_for_the_studio_identity_and_the_studio_deals_page_reads_it_with_the_studio_session_and_draws_it_as_a_link_with_its_body()
    {
        var l = new L(new HttpContextAccessor());
        var owner = Guid.NewGuid();
        var deal = new Deal { Id = Guid.NewGuid(), ListingTitle = "سِلعَة", AmountSar = 100m, Status = DealStatus.Cancelled };
        var refund = DealNotices.RefundDueFor(Slug, deal, owner, l, DateTime.UtcNow);
        var open = DealNotices.InvoiceLeftOpenFor(Slug, deal, owner, l, DateTime.UtcNow);
        Assert.Equal(owner, DealNotices.StoreOwnerOf(new ACommerce.Kit.Tenants.Tenant { Id = Slug, OwnerUserId = owner }));
        Assert.All(new[] { refund, open }, n =>
        {
            Assert.Equal(owner, n.UserId);
            Assert.Equal($"/studio/apps/{Slug}/deals/{deal.Id}", n.RelatedUrl);
        });

        var readers = NoticeReaders();
        var storefront = Code(Path.Combine(Pages, "Notifications.razor"));
        var deals = Code(Path.Combine(Pages, "StudioAppDeals.razor"));
        var component = Code(Path.Combine(Pages, "..", "StudioOwnerNotices.razor"));
        var studio = Directory.GetFiles(Pages, "Studio*.razor");
        var studioLinks = studio.Count(p => Code(p).Contains("RelatedUrl", StringComparison.Ordinal));
        var studioBodies = studio.Count(p => Code(p).Contains("n.Body", StringComparison.Ordinal));
        var console = Code(Path.Combine(Pages, "StudioAppConsole.razor"));
        output.WriteLine($"قُرّاءُ «إشعاراتي»: {string.Join(", ", readers)} · صَفَحاتُ استوديو مَفحوصَة: {studio.Length} · تَرسُمُ رابِطاً: {studioLinks} · مَتناً: {studioBodies}");

        Assert.True(studio.Length >= 20, $"أَداةٌ عَمياء: {studio.Length} صَفحَةَ استوديو");
        Assert.Equal(new[] { "Notifications.razor", "StudioAppDeals.razor" }, readers);
        Assert.Contains("@inject AuthSession Auth", storefront, StringComparison.Ordinal);
        Assert.Contains("await LoadAsync(Auth.UserId!.Value);", storefront, StringComparison.Ordinal);

        Assert.Contains("@inject Services.Incubator.StudioAuth Auth", deals, StringComparison.Ordinal);
        var guard = deals.IndexOf("if (tenant is not null && tenant.OwnerUserId != Auth.UserId!.Value) { tenant = null; return; }", StringComparison.Ordinal);
        var read = deals.IndexOf("ownerNotices = await Account.NotificationsAsync(Slug, Auth.UserId!.Value);", StringComparison.Ordinal);
        Assert.True(guard > 0 && read > guard, $"القِراءَةُ بَعدَ فَحصِ المِلكِيَّة: guard={guard} read={read}");
        Assert.Contains("@if (ownerNotices.Count > 0)", deals, StringComparison.Ordinal);
        Assert.Contains("<StudioOwnerNotices Slug=\"@Slug\" Mine=\"@ownerNotices\" />", deals, StringComparison.Ordinal);

        Assert.Equal(0, studioLinks);
        Assert.Equal(0, studioBodies);
        Assert.Contains("DealNotices.OwnerNoticesOf(Mine, Slug)", component, StringComparison.Ordinal);
        Assert.Contains("<a class=\"studio-list-row\" href=\"@n.RelatedUrl\">", component, StringComparison.Ordinal);
        Assert.Contains("<span>@n.Body · @Culture.FormatTime(n.At, \"yyyy-MM-dd HH:mm\")</span>", component, StringComparison.Ordinal);

        Assert.Contains("<div class=\"studio-list-row\" style=\"cursor:default;\">", console, StringComparison.Ordinal);
        Assert.Contains("<strong>@n.Title</strong>", console, StringComparison.Ordinal);
        Assert.Contains("<span>@n.Type · @Culture.FormatTime(n.At, \"yyyy-MM-dd HH:mm\")</span>", console, StringComparison.Ordinal);
    }

    /// <summary><b>رابِطُ الكاتِبِ وبادِئَةُ القارِئِ تَعريفٌ واحِد</b>: صَفحَةُ الصَفقَةِ في الاستوديو بِحَرفِها الَّذي كانَ يُكتَب.</summary>
    [Fact]
    public void StudioDealUrl_is_the_studio_deal_page_both_owner_notices_carry()
    {
        var id = Guid.NewGuid();
        Assert.Equal($"/studio/apps/{Slug}/deals/{id}", DealNotices.StudioDealUrl(Slug, id));
        var deal = new Deal { Id = id, ListingTitle = "سِلعَة", AmountSar = 5m, Status = DealStatus.Cancelled };
        var l = new L(new HttpContextAccessor());
        Assert.Equal(DealNotices.StudioDealUrl(Slug, id), DealNotices.RefundDueFor(Slug, deal, Guid.NewGuid(), l, DateTime.UtcNow).RelatedUrl);
        Assert.Equal(DealNotices.StudioDealUrl(Slug, id), DealNotices.InvoiceLeftOpenFor(Slug, deal, Guid.NewGuid(), l, DateTime.UtcNow).RelatedUrl);
    }

    /// <summary><b>المُوجِب</b>: ما رابِطُه صَفحَةُ صَفقَةٍ في هذا المَتجَرِ في الاستوديو يُبقى، بِتَرتيبِ ما قُرِئ.</summary>
    [Fact]
    public void OwnerNoticesOf_keeps_this_stores_studio_deal_links_in_the_order_read()
    {
        var a = Notice(DealNotices.StudioDealUrl(Slug, Guid.NewGuid()));
        var b = Notice(DealNotices.StudioDealUrl(Slug, Guid.NewGuid()));
        Assert.Equal(new[] { a.Id, b.Id }, DealNotices.OwnerNoticesOf(new[] { a, b }, Slug).Select(n => n.Id));
        Assert.Equal(new[] { b.Id, a.Id }, DealNotices.OwnerNoticesOf(new[] { b, a }, Slug).Select(n => n.Id));
    }

    /// <summary><b>السالِب</b>: مَتجَرٌ آخَر (ومَتجَرٌ يَبدَأُ اسمُه بِاسمِ هذا)، وصَفحَةُ الصَفقَةِ في الواجِهَة، وبادِئَةٌ بِلا صَفقَة، وصَفحَةُ استوديو أُخرى، وبِلا رابِط،
    /// وقائِمَةٌ غائِبَة — لا شَيءَ مِنها يُرسَم.</summary>
    [Theory]
    [InlineData("/studio/apps/other/deals/0f8fad5b-d9cb-469f-a165-70867728950e")]
    [InlineData("/studio/apps/s2/deals/0f8fad5b-d9cb-469f-a165-70867728950e")]
    [InlineData("/s/deals/0f8fad5b-d9cb-469f-a165-70867728950e")]
    [InlineData("/studio/apps/s/deals/")]
    [InlineData("/studio/apps/s/console")]
    [InlineData("/x/studio/apps/s/deals/0f8fad5b-d9cb-469f-a165-70867728950e")]
    [InlineData("")]
    [InlineData(null)]
    public void OwnerNoticesOf_draws_nothing_outside_this_stores_studio_deal_pages(string? url)
    {
        Assert.Empty(DealNotices.OwnerNoticesOf(new[] { Notice(url) }, Slug));
        Assert.Empty(DealNotices.OwnerNoticesOf(null, Slug));
    }

    /// <summary><b>المُكَوِّنُ مَرسوماً</b>: إشعارا «استِرجاعٌ مُستَحَقّ» و«فاتورَةٌ لَم تُلغَ» صَفّانِ يُنقَرانِ إلى صَفحَتَي صَفقَتَيهِما في الاستوديو بِعُنوانَيهِما ومَتنَيهِما
    /// (المُنتَجُ والمَبلَغُ والعُملَة) وتَحتَ عُنوانِ القِسمِ مِن القامُوس؛ وإشعارُ مَتجَرٍ آخَرَ وإشعارُ الواجِهَةِ لا يُرسَمان.</summary>
    [Fact]
    public async Task The_owner_notices_section_draws_the_refund_due_and_the_open_invoice_as_links_to_their_studio_deals_with_their_bodies()
    {
        var l = new L(new HttpContextAccessor());
        var owner = Guid.NewGuid();
        var paid = new Deal { Id = Guid.NewGuid(), ListingTitle = "سِلعَةٌ مَدفوعَة", AmountSar = 100m, Status = DealStatus.Cancelled };
        paid.Refs[DealsService.PaymentCurrencyRef] = "SAR";
        var left = new Deal { Id = Guid.NewGuid(), ListingTitle = "سِلعَةٌ بِفاتورَة", AmountSar = 40m, Status = DealStatus.Cancelled };
        var refund = DealNotices.RefundDueFor(Slug, paid, owner, l, DateTime.UtcNow);
        var open = DealNotices.InvoiceLeftOpenFor(Slug, left, owner, l, DateTime.UtcNow);
        var foreign = Notice(DealNotices.StudioDealUrl("other", Guid.NewGuid()), "غَريب", "مَتنٌ غَريب");
        var storefront = Notice($"/{Slug}/deals/{Guid.NewGuid()}", "واجِهَة", "مَتنُ الواجِهَة");

        var html = await RenderAsync(Slug, new[] { refund, foreign, open, storefront });
        var text = System.Net.WebUtility.HtmlDecode(html);
        output.WriteLine($"صُفوفٌ تُنقَر: {Links(html)} · {html.Length} حَرفاً");

        Assert.Equal(2, Links(html));
        Assert.Contains($"href=\"/studio/apps/{Slug}/deals/{paid.Id}\"", html, StringComparison.Ordinal);
        Assert.Contains($"href=\"/studio/apps/{Slug}/deals/{left.Id}\"", html, StringComparison.Ordinal);
        Assert.True(html.IndexOf(paid.Id.ToString(), StringComparison.Ordinal) < html.IndexOf(left.Id.ToString(), StringComparison.Ordinal), "بِتَرتيبِ ما قُرِئ");
        Assert.Contains(LocaleCatalog.Text(LocaleCatalog.Arabic, "studio.app_deals.owner_notices"), text, StringComparison.Ordinal);
        foreach (var n in new[] { refund, open })
        {
            Assert.Contains(n.Title, text, StringComparison.Ordinal);
            Assert.Contains(n.Body, text, StringComparison.Ordinal);
        }
        Assert.Contains("SAR", text, StringComparison.Ordinal);
        Assert.Contains("ميسر", text, StringComparison.Ordinal);
        Assert.DoesNotContain("/studio/apps/other/", html, StringComparison.Ordinal);
        Assert.DoesNotContain(foreign.Body, text, StringComparison.Ordinal);
        Assert.DoesNotContain(storefront.Body, text, StringComparison.Ordinal);
    }

    /// <summary><b>وبِلا إشعارٍ لِهذا المَتجَرِ لا يُرسَمُ حَرف</b>: القائِمَةُ الفارِغَة، وإشعاراتٌ كُلُّها خارِجَه — فَلا عُنوانَ قِسمٍ يُعَلَّقُ عَلى فَراغ.</summary>
    [Fact]
    public async Task The_owner_notices_section_draws_nothing_without_a_notice_of_this_store()
    {
        Assert.Equal("", await RenderAsync(Slug, Array.Empty<Notification>()));
        Assert.Equal("", await RenderAsync(Slug, new[] { Notice(DealNotices.StudioDealUrl("other", Guid.NewGuid())), Notice($"/{Slug}/deals/{Guid.NewGuid()}"), Notice(null) }));
    }
}

/// <summary>
/// <para><b>بُرهانٌ حَيٌّ عَلى Postgres — مِن الكاتِبِ إلى القارِئِ إلى الرَسم</b>: مَتجَرٌ بِصاحِبٍ مُعَيَّن، وصَفقَةٌ مُلغاةٌ عَلَيها سَطرُ «استِرجاعٌ مُستَحَقّ»؛ يَكتُبُ
/// <see cref="DealNotices.NotifyStoreOwnerAsync"/> الإشعارَ، ويَقرَؤُه القارِئُ القائِمُ (<see cref="AccountQueries.NotificationsAsync"/>) بِمُعَرِّفِ صاحِبِ المَتجَرِ — ما
/// تُناديه صَفحَةُ «العَمَلِيّات» بِمُعَرِّفِ جَلسَةِ الاستوديو — ويُرسَمُ صَفّاً يُنقَرُ إلى صَفحَةِ الصَفقَة. وإشعارُ عُضوٍ في المَتجَرِ نَفسِه لا يُقرَأُ بِذلكَ المُعَرِّف.</para>
/// <para>يَتَخَطّى نَفسَه ما لَم تُضبَط <c>WASAYEL_LIVE_PROOF=1</c> و<c>ConnectionStrings__Postgres</c>، ويُنشِئُ مَتجَرَه ويَحذِفُه.</para>
/// </summary>
[Collection(LiveProofCollection.Name)]
public class LiveStudioOwnerNoticeReaderProofTests(ITestOutputHelper output)
{
    private static bool Enabled
        => LiveDealConcurrencyProofTests.Enabled && !string.IsNullOrEmpty(LiveDealConcurrencyProofTests.ConnectionString);

    [Fact]
    public async Task A_refund_due_written_for_the_store_owner_is_read_back_by_his_studio_id_and_drawn_as_a_link_on_Postgres()
    {
        if (!Enabled) return;

        await using var store = LiveDealConcurrencyProofTests.BuildStore();
        var slug = "zz-owner-" + Guid.NewGuid().ToString("N")[..8];
        var owner = Guid.NewGuid();
        var member = Guid.NewGuid();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSignalR();
        await using var sp = services.BuildServiceProvider();
        var notices = new DealNotices(store, sp.GetRequiredService<IHubContext<ACommerce.Kit.Realtime.Server.RealtimeHub>>(),
            new WebPushService(new ConfigurationBuilder().Build(), NullLogger<WebPushService>.Instance));
        var l = new L(new HttpContextAccessor());
        try
        {
            await using (var g = store.LightweightSession())
            {
                g.Store(new ACommerce.Kit.Tenants.Tenant { Id = slug, Name = "مَتجَرُ بُرهانِ قارِئِ صاحِبِه", OwnerUserId = owner });
                await g.SaveChangesAsync();
            }
            await using (var s = store.LightweightSession(slug))
            {
                s.Store(new Notification { Id = Guid.NewGuid(), UserId = member, Type = DealNotices.NewDealType, Title = "لِلعُضو", Body = "لِلعُضو",
                    RelatedUrl = $"/{slug}/deals/{Guid.NewGuid()}" });
                await s.SaveChangesAsync();
            }
            var deal = new Deal { Id = Guid.NewGuid(), ListingTitle = "سِلعَة", AmountSar = 100m, Status = DealStatus.Cancelled };
            deal.Refs[DealsService.PaymentCurrencyRef] = "SAR";
            deal.Timeline.Add(new(DealStage.Paid, DealStage.Paid, DealEvent.GatewayRefundDue, null, null, null, DateTime.UtcNow));

            Assert.True(await notices.NotifyStoreOwnerAsync(slug, deal, l));
            var mine = await new AccountQueries(store).NotificationsAsync(slug, owner);
            var html = await StudioOwnerNoticeReaderTests.RenderAsync(slug, mine);
            output.WriteLine($"قُرِئَ لِصاحِبِ المَتجَر: {mine.Count} · صُفوفٌ تُنقَر: {StudioOwnerNoticeReaderTests.Links(html)} · «{mine.Single().Body}»");

            var told = Assert.Single(mine);
            Assert.Equal(owner, told.UserId);
            Assert.Equal(DealNotices.StudioDealUrl(slug, deal.Id), told.RelatedUrl);
            Assert.Equal(1, StudioOwnerNoticeReaderTests.Links(html));
            Assert.Contains($"href=\"/studio/apps/{slug}/deals/{deal.Id}\"", html, StringComparison.Ordinal);
            Assert.Contains(told.Body, System.Net.WebUtility.HtmlDecode(html), StringComparison.Ordinal);
            Assert.Contains("SAR", told.Body, StringComparison.Ordinal);
            Assert.DoesNotContain("لِلعُضو", System.Net.WebUtility.HtmlDecode(html), StringComparison.Ordinal);
        }
        finally
        {
            await using (var s = store.LightweightSession(slug))
            {
                foreach (var n in await s.Query<Notification>().ToListAsync()) s.Delete(n);
                await s.SaveChangesAsync();
            }
            await using (var g = store.LightweightSession())
            {
                g.Delete<ACommerce.Kit.Tenants.Tenant>(slug);
                await g.SaveChangesAsync();
            }
        }
    }
}
