using ACommerce.Kit.Notifications;
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

// ═══ طَلَبُ الاستِرجاعِ والبائِعُ أَغلَقَ حِسابَه: تُخبَرُ إدارَةُ المَتجَر، ويَقولُ السِجِلُّ ذلك، ولا يُرسَمُ بابٌ يُرَدّ (‏2026-11-04، الموجَة ١٢ G1 · ٤؛ تَوصيفُه 77b8471) ═══
//
// قَبلَه (`LiveChatPartnerGoneReturnProofTests` يَطبَعُه ويَبقى بِبَصمَتِه): «لِلمَمحُوّ 1 · لِصاحِبِ المَتجَر 0 · المَراجِع [listing_owner]»، و«صَفحَةُ المَمحُوّ: بابُ المُحادَثَة True ·
// نَصُّ الغِياب False». والقَرارُ «أ» (P1/P2): صاحِبُ المَتجَرِ يُدَبِّرُ مَتجَرَه — لا «النافِذَةُ صَفقَةٌ جارِيَة» (يُوَسِّعُ «الجارِيَةَ» في د52 فَلا يُؤخَذ).

public class ChatPartnerGoneReturnRowsTests(ITestOutputHelper output)
{
    private static Deal WithTold(string? told)
    {
        var d = new Deal { Id = Guid.NewGuid(), ListingTitle = "سِلعَةٌ تُستَرجَع", AmountSar = 150m, InitiatorId = Guid.NewGuid(), InitiatorName = "مُشتَرٍ بِاسمِه" };
        if (told is not null) d.Refs[DealNotices.ReturnToldRef] = told;
        return d;
    }

    /// <summary>‏(‏2026-11-05، G1b-R1-1) `ReturnToldStoreOf` صارَت `ReturnToldOf` بِمَعجَمٍ مُغلَقٍ ثُلاثِيّ — والصَفُّ نَفسُه عَلى بَديلِ المَتجَر؛ وبَديلُ «لا أَحَد» صُفوفُه
    /// في `ChatPartnerGoneOwnerlessStoreRowsTests`.</summary>
    [Fact]
    public void The_store_was_told_only_when_the_deal_carries_return_told_store()
    {
        Assert.Equal(DealNotices.ReturnTold.Store, DealNotices.ReturnToldOf(WithTold(DealNotices.ReturnToldStore)));
        Assert.Equal(DealNotices.ReturnTold.Vendor, DealNotices.ReturnToldOf(WithTold(null)));
        Assert.Equal(DealNotices.ReturnTold.Vendor, DealNotices.ReturnToldOf(WithTold("vendor")));
        Assert.Equal(DealNotices.ReturnTold.Vendor, DealNotices.ReturnToldOf(WithTold("Store")));
    }

    /// <summary><b>المُوجَب</b>: كُلُّ مِفتاحٍ في العائِلَةِ بِبَديلِه حينَ المُخبَرُ إدارَةُ المَتجَر. <b>والسالِب</b>: بِلا بَديلٍ حينَ البائِع، ومِفتاحٌ مِن خارِجِها كَما هُوَ.</summary>
    [Fact]
    public void A_return_key_takes_the_store_variant_only_when_the_store_is_told_and_only_inside_its_family()
    {
        Assert.Equal(4, DealNotices.ReturnKeys.Count);
        foreach (var key in DealNotices.ReturnKeys)
        {
            Assert.Equal(key + DealNotices.StoreVariant, DealNotices.ReturnKey(key, DealNotices.ReturnTold.Store));   // ‏(‏2026-11-05، G1b-R1-1) بِمَن أُخبِرَ لا بِـbool
            Assert.Equal(key, DealNotices.ReturnKey(key, DealNotices.ReturnTold.Vendor));
        }
        Assert.Equal("deals.detail.err_deal_not_found", DealNotices.ReturnKey("deals.detail.err_deal_not_found", DealNotices.ReturnTold.Store));
        Assert.Equal("deals.detail.event_return_requested_store", DealNotices.ReturnEventKey(WithTold(DealNotices.ReturnToldStore), studio: false));
        Assert.Equal("studio.deal.event_return_requested_store", DealNotices.ReturnEventKey(WithTold(DealNotices.ReturnToldStore), studio: true));
        Assert.Equal("deals.detail.event_return_requested", DealNotices.ReturnEventKey(WithTold(null), studio: false));
        Assert.Equal("studio.deal.event_return_requested", DealNotices.ReturnEventKey(WithTold(null), studio: true));
        Assert.Equal("deals.detail.err_return_already_requested_store", DealNotices.RefusalKeyFor("deals.detail.err_return_already_requested", WithTold(DealNotices.ReturnToldStore)));
        Assert.Equal("deals.detail.err_return_already_requested", DealNotices.RefusalKeyFor("deals.detail.err_return_already_requested", WithTold(null)));
        Assert.Equal("deals.detail.err_return_window_closed", DealNotices.RefusalKeyFor("deals.detail.err_return_window_closed", WithTold(DealNotices.ReturnToldStore)));
    }

    /// <summary>لِكُلِّ مِفتاحٍ في العائِلَةِ ولِبَديلِه نَصٌّ في القامُوسِ العَرَبِيّ؛ والبَديلُ يُسَمّي إدارَةَ المَتجَرِ ولا يَقولُ «أُخبِرَ البائِع». يَطبَعُ ما فَحَصَه ويَفشَلُ عَلى صِفر.</summary>
    [Fact]
    public void Every_return_key_and_its_store_variant_has_an_arabic_text_and_the_variant_names_the_store()
    {
        var checkedKeys = 0;
        foreach (var key in DealNotices.ReturnKeys)
        {
            var base_ = LocaleCatalog.Find(LocaleCatalog.Arabic, key);
            var variant = LocaleCatalog.Find(LocaleCatalog.Arabic, key + DealNotices.StoreVariant);
            Assert.False(string.IsNullOrWhiteSpace(base_), key);
            Assert.False(string.IsNullOrWhiteSpace(variant), key + DealNotices.StoreVariant);
            Assert.Contains("إدارَةُ المَتجَر", variant!, StringComparison.Ordinal);
            Assert.DoesNotContain("أُخبِرَ البائِع", variant!, StringComparison.Ordinal);
            checkedKeys += 2;
        }
        output.WriteLine($"مَفاتيحُ مَفحوصَة: {checkedKeys}");
        Assert.Equal(8, checkedKeys);
    }

    /// <summary>إشعارُ صاحِبِ المَتجَر: لِمُعَرِّفِه، بِنَوعِ الطَلَبِ الجَديد، وإلى صَفحَةِ الصَفقَةِ في الاستوديو (فَيَقرَؤُه قارِئُ «العَمَلِيّات»)، بِعُنوانِ الإعلان — ولا اسمَ
    /// لِلمُشتَري ولا مَبلَغ. والمَعجَمُ المُغلَقُ خَمسَةُ أَفعال، والأُنبوبُ يَبني لِلخامِسِ بانِيَه.</summary>
    [Fact]
    public void The_store_owner_notice_names_the_listing_and_points_at_the_studio_deal_page_and_the_lexicon_has_five_actions()
    {
        var l = new L(new HttpContextAccessor());
        var deal = WithTold(null);
        var owner = Guid.NewGuid();
        var n = DealNotices.ReturnVendorGoneFor("zz", deal, owner, l, DateTime.UtcNow);
        Assert.Equal((owner, DealNotices.NewDealType, DealNotices.StudioDealUrl("zz", deal.Id)), (n.UserId, n.Type, n.RelatedUrl));
        Assert.Contains("«سِلعَةٌ تُستَرجَع»", n.Body, StringComparison.Ordinal);
        Assert.DoesNotContain(deal.InitiatorName, n.Body, StringComparison.Ordinal);
        Assert.Single(DealNotices.OwnerNoticesOf(new[] { n }, "zz"));
        Assert.Equal(5, Enum.GetValues<DealNotices.OwnerAction>().Length);
        var src = WriteEndpointGuardTests.StripComments(File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, "libs", "templates",
            "ACommerce.Templates.Customer.Marketplace", "Services", "Deals", "DealNotices.cs")));
        Assert.Contains("OwnerAction.ReturnVendorGone => ReturnVendorGoneFor(slug, deal, ownerId, l, DateTime.UtcNow),", src, StringComparison.Ordinal);
    }

    /// <summary>الشَكل: الأُنبوبُ يَسأَلُ <c>PartnerGone</c> عَلى وَثيقَةِ البائِعِ قَبلَ أَن يَكتُبَ إشعارَه، ويُلحِقُ المَرجِعَ بَعدَ أَن يُخبِرَ صاحِبَ المَتجَر؛ والصَفحَةُ تَسأَلُ
    /// السُؤالَ نَفسَه وتَرسُمُ استِمارَةَ المُحادَثَةِ تَحتَ <c>!vendorGone</c> ونَصَّ الغِيابِ في بَديلِه.</summary>
    [Fact]
    public void The_pipe_and_the_page_ask_the_partner_gone_question_before_the_vendor_notice_and_the_chat_form()
    {
        var root = Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, "libs", "templates", "ACommerce.Templates.Customer.Marketplace");
        var src = WriteEndpointGuardTests.StripComments(File.ReadAllText(Path.Combine(root, "Services", "Deals", "DealNotices.cs")));
        var pipe = src[src.IndexOf("public async Task<bool> NotifyReturnRequestedAsync(", StringComparison.Ordinal)..];
        pipe = pipe[..pipe.IndexOf("return true;\n    }", StringComparison.Ordinal)];
        int At(string s, string what) => s.IndexOf(what, StringComparison.Ordinal);
        // ‏(‏2026-11-05، G1b-R1-1) والسُؤالُ صارَ داخِلَ القَرارِ الواحِد `ReturnToldFor` (ومَعَه صاحِبُ المَتجَر)، والمَرجِعُ قيمَتُه مِن المَعجَمِ لا ثابِتُ «المَتجَر» وَحدَه.
        var gone = At(pipe, "told = ReturnToldFor(await q.LoadAsync<ACommerce.Kit.Auth.User>(vendorId, ct), StoreOwnerOf(await q.LoadAsync<ACommerce.Kit.Tenants.Tenant>(slug, ct)));");
        var owner = At(pipe, "NotifyStoreOwnerAsync(slug, deal, OwnerAction.ReturnVendorGone, l, ct)");
        var told = At(pipe, "deals.AttachRefAsync(slug, deal.Id, ReturnToldRef, value, ct)");
        var vendor = At(pipe, "ReturnRequestedFor(slug, deal, vendorId, l, DateTime.UtcNow)");
        output.WriteLine($"السُؤال {gone} · صاحِبُ المَتجَر {owner} · المَرجِع {told} · إشعارُ البائِع {vendor}");
        Assert.True(gone > 0 && owner > gone && told > owner && vendor > told, "التَرتيب: السُؤالُ ثُمَّ صاحِبُ المَتجَرِ ثُمَّ المَرجِعُ، وإشعارُ البائِعِ بَعدَها كُلِّها.");

        var page = WriteEndpointGuardTests.StripMarkupComments(File.ReadAllText(Path.Combine(root, "Components", "Pages", "MyDealDetail.razor")));
        Assert.Contains("&& Services.Identity.AccountErasure.PartnerGone(await Accounts.ProfileAsync(Slug, seller));", page, StringComparison.Ordinal);
        var form = At(page, "<form method=\"post\" action=\"@($\"/{Slug}/users/{vid}/chat\")\"");
        Assert.True(form > 0 && page.LastIndexOf("@if (isInitiator && vendorId is { } vid && !vendorGone)", form, StringComparison.Ordinal) > 0, "الاستِمارَةُ خارِجَ شَرطِ الغِياب.");
        var gap = At(page, "else if (isInitiator && vendorId is not null)");
        Assert.True(gap > form && At(page, "@L.Markup(\"vendors.profile.contact_gone\")") > gap, "لا سَطرَ غِيابٍ في البَديل.");
        Assert.Contains("@L.Format(DealNotices.ReturnKey(\"deals.detail.return_hint\", returnTold),", page, StringComparison.Ordinal);   // ‏(‏2026-11-05، G1b-R1-1) قَرارُ الأُنبوب
        Assert.Contains("Message=\"@L[DealNotices.RefusalKeyFor(errKey, deal)]\"", page, StringComparison.Ordinal);
    }
}

[Collection(LiveProofCollection.Name)]
public class LiveChatPartnerGoneReturnSwitchProofTests(ITestOutputHelper output)
{
    private static bool Enabled
        => LiveDealConcurrencyProofTests.Enabled && !string.IsNullOrEmpty(LiveDealConcurrencyProofTests.ConnectionString);

    /// <summary>صَفحَةُ الصَفقَةِ بِرَمزِ رَدٍّ في العُنوان (<c>?err=</c>) — نَسَقُ <c>LiveChatPartnerGoneReturnProofTests.RenderDealPageAsync</c> بِحَرفِه وعُنوانُه وَحدَه يَزيد.</summary>
    private static async Task<string> RenderAsync(LiveTemplateDoorHost host, LiveChatPartnerGoneReturnProofTests.Seeded seeded, string? err)
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

    private static string Text(string key) => LocaleCatalog.Find(LocaleCatalog.Arabic, key)!;

    /// <summary>قُلِبَ ما طَبَعَه التَوصيف («لِلمَمحُوّ 1 · لِصاحِبِ المَتجَر 0 · المَراجِع [listing_owner]» و«بابُ المُحادَثَة True · نَصُّ الغِياب False»).
    /// <b>المُوجَب</b> — والبائِعُ مَمحُوّ: الصَفحَةُ قَبلَ الطَلَبِ بِلا بابِ مُحادَثَةٍ وبِسَطرِ الغِيابِ وتَلميحِ إدارَةِ المَتجَر؛ والبابُ يُخبِرُ صاحِبَ المَتجَرِ مَرَّةً إلى صَفحَةِ الاستوديو
    /// ولا يَكتُبُ لِلمَمحُوِّ شَيئاً، ويُلحِقُ <c>return_told=store</c>؛ والسِجِلُّ ورَدُّ التَكرارِ بِبَديلِهِما.</summary>
    [Fact]
    public async Task With_the_vendor_gone_the_store_owner_is_told_the_deal_says_so_and_no_chat_door_is_drawn_on_Postgres()
    {
        if (!Enabled) return;
        await using var host = LiveTemplateDoorHost.Build();
        var gone = await LiveChatPartnerGoneReturnProofTests.SeedAsync(host.Store, vendorGone: true);
        try
        {
            var before = await RenderAsync(host, gone, null);
            var (status, at) = await LiveChatPartnerGoneReturnProofTests.ReturnAsync(host, gone);
            await using var q = host.Store.QuerySession(gone.Slug);
            var deal = (await q.LoadAsync<Deal>(gone.Deal.Id))!;
            var notices = await Marten.QueryableExtensions.ToListAsync(q.Query<Notification>());
            var after = await RenderAsync(host, gone, DealReturnRequest.AlreadyRequested);
            var toOwner = notices.Where(n => n.UserId == gone.Owner).ToList();
            output.WriteLine($"قَبلَ الطَلَب: بابُ المُحادَثَة {before.Contains(LiveChatPartnerGoneReturnProofTests.ChatAction(gone))} · نَصُّ الغِياب {before.Contains(Text("vendors.profile.contact_gone"))} · "
                + $"تَلميحُ المَتجَر {before.Contains("فَتُخبَرُ إدارَةُ المَتجَرِ بِطَلَبِك")} · البابُ: {status} → {at} · لِصاحِبِ المَتجَر {toOwner.Count} ({toOwner.FirstOrDefault()?.RelatedUrl}) · "
                + $"لِلمَمحُوّ {notices.Count(n => n.UserId == gone.Vendor.Id)} · المَراجِع [{string.Join(", ", deal.Refs.OrderBy(r => r.Key, StringComparer.Ordinal).Select(r => $"{r.Key}={r.Value}"))}] · "
                + $"السِجِلّ «{(after.Contains(Text("deals.detail.event_return_requested_store")) ? Text("deals.detail.event_return_requested_store") : "؟")}» · "
                + $"رَدُّ التَكرار {after.Contains(Text("deals.detail.err_return_already_requested_store"))}");
            Assert.DoesNotContain(LiveChatPartnerGoneReturnProofTests.ChatAction(gone), before, StringComparison.Ordinal);
            Assert.Contains(Text("vendors.profile.contact_gone"), before, StringComparison.Ordinal);
            Assert.Contains("فَتُخبَرُ إدارَةُ المَتجَرِ بِطَلَبِك", before, StringComparison.Ordinal);
            Assert.Equal((302, $"/{gone.Slug}/deals/{gone.Deal.Id}"), (status, at));
            var told = Assert.Single(toOwner);
            Assert.Equal((DealNotices.NewDealType, DealNotices.StudioDealUrl(gone.Slug, gone.Deal.Id)), (told.Type, told.RelatedUrl));
            Assert.Equal(Text("notifications.deal_return_vendor_gone.title"), told.Title);
            Assert.Equal(0, notices.Count(n => n.UserId == gone.Vendor.Id));
            Assert.Equal(DealNotices.ReturnToldStore, deal.Refs[DealNotices.ReturnToldRef]);
            Assert.Contains(Text("deals.detail.event_return_requested_store"), after, StringComparison.Ordinal);
            Assert.Contains(Text("deals.detail.err_return_already_requested_store"), after, StringComparison.Ordinal);
            Assert.DoesNotContain(Text("deals.detail.event_return_requested") + "<", after, StringComparison.Ordinal);
        }
        finally { await LiveChatPartnerGoneReturnProofTests.CleanAsync(host.Store, gone.Slug); }
    }

    /// <summary><b>السالِب</b> — والبائِعُ قائِم: لا مَرجِعَ «مَن أُخبِر»، والسِجِلُّ ورَدُّ التَكرارِ بِنَصَّيهِما «أُخبِرَ البائِع»، وبابُ المُحادَثَةِ مَرسوم.</summary>
    [Fact]
    public async Task With_a_live_vendor_the_deal_keeps_the_vendor_told_texts_and_the_chat_door_on_Postgres()
    {
        if (!Enabled) return;
        await using var host = LiveTemplateDoorHost.Build();
        var live = await LiveChatPartnerGoneReturnProofTests.SeedAsync(host.Store, vendorGone: false);
        try
        {
            var (status, _) = await LiveChatPartnerGoneReturnProofTests.ReturnAsync(host, live);
            await using var q = host.Store.QuerySession(live.Slug);
            var deal = (await q.LoadAsync<Deal>(live.Deal.Id))!;
            var after = await RenderAsync(host, live, DealReturnRequest.AlreadyRequested);
            output.WriteLine($"البائِعُ القائِم: {status} · مَرجِعُ «مَن أُخبِر» {deal.Refs.ContainsKey(DealNotices.ReturnToldRef)} · السِجِلّ «أُخبِرَ البائِع» {after.Contains(Text("deals.detail.event_return_requested"))} · "
                + $"بَديلُ المَتجَر {after.Contains("فَأُخبِرَت إدارَةُ المَتجَر")} · بابُ المُحادَثَة {after.Contains(LiveChatPartnerGoneReturnProofTests.ChatAction(live))}");
            Assert.Equal(302, status);
            Assert.False(deal.Refs.ContainsKey(DealNotices.ReturnToldRef));
            Assert.Contains(Text("deals.detail.event_return_requested"), after, StringComparison.Ordinal);
            Assert.Contains(Text("deals.detail.err_return_already_requested"), after, StringComparison.Ordinal);
            Assert.DoesNotContain("أُخبِرَت إدارَةُ المَتجَر", after, StringComparison.Ordinal);
            Assert.Contains(LiveChatPartnerGoneReturnProofTests.ChatAction(live), after, StringComparison.Ordinal);
        }
        finally { await LiveChatPartnerGoneReturnProofTests.CleanAsync(host.Store, live.Slug); }
    }
}
