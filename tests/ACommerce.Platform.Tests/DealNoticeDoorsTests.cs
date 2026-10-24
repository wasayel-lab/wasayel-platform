using ACommerce.Templates.Customer.Marketplace.Services;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ أَيُّ بابٍ يُحَرِّكُ الصَفقَةَ يُخبِرُ المُشتَري؟ — الاستوديو والـAPI بِجِوارِ أَبوابِ العُضو (مُراجَعَةُ المَوجَة 3a، G4 (iii)) ═══
//
// **المَقيسُ قَبلَ اللَمس** (‏2026-10-13): أَبوابُ العُضوِ تَمُرُّ بِـ`DealNotices` — `/accept` يُخبِرُ المُشتَري بِالقَبول، و`/cancel` بِالإلغاءِ حينَ
// يُلغي غَيرُه (`TellsTheBuyer`). أَمّا نُقطَةُ الإلغاءِ الإدارِيَّةُ في الاستوديو فَتَكتُبُ التَدقيقَ ولا تُخبِرُ أَحَداً، ونُقطَتا الـAPI (`advance` و`cancel`)
// لا تَعرِفانِ `DealNotices` أَصلاً — والبائِعُ المَربوطُ يُخرِجُ `Offered` إلى `Booked` بِمِفتاحِه (‏ADR-033 §٦‑ط‑٤: «يُعادِلُ القَبولَ بِلا إشعارِ
// المُشتَري»، و§٦‑ط‑٦ تَرَكَه سُؤالاً). فَالمُشتَري يَعلَمُ بِإلغاءِ طَلَبِه أَو قَبولِه بِحَسَبِ البابِ الَّذي سَلَكَه الطَرَفُ الآخَر.
//
// **التَوصيفُ قَبلَ التَبديل** (القاعِدَة ٣): صَفُّ «اليَوم» يُودَعُ وَحدَه، ويُقلَبُ بِاسمِه في كوميتِ الأُنبوب.

public class DealNoticeDoorsTests(ITestOutputHelper output)
{
    private static string RepoRoot => ThemeZeroEquivalenceTests.RepoRoot;
    private const string Template = "libs/templates/ACommerce.Templates.Customer.Marketplace";

    private static readonly Guid Buyer    = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
    private static readonly Guid Seller   = Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb");
    private static readonly Guid AppOwner = Guid.Parse("cccccccc-cccc-4ccc-8ccc-cccccccccccc");

    private static string Source(params string[] parts)
    {
        var path = Path.Combine(RepoRoot, Path.Combine(parts));
        Assert.True(File.Exists(path), $"مَصدَرٌ مَفقود: {path} — الأَداةُ عَمياء.");
        var text = File.ReadAllText(path);
        Assert.True(text.Length > 500, $"أَداةٌ عَمياء: {path} طولُه {text.Length} مِحرَفاً — لَم يُقرَأ.");
        return text;
    }

    private static string Segment(string source, string from, string to)
    {
        var start = source.IndexOf(from, StringComparison.Ordinal);
        Assert.True(start >= 0, $"‏`{from}` غَيرُ مَوجود — الأَداةُ عَمياء.");
        var end = source.IndexOf(to, start + from.Length, StringComparison.Ordinal);
        Assert.True(end > start, $"‏`{to}` لا يَتلو `{from}` — الأَداةُ عَمياء.");
        return source.Substring(start, end - start);
    }

    private static string Endpoints() => WriteEndpointGuardTests.StripComments(Source(Template, "MarketplaceTemplateExtensions.cs"));
    private static string Api() => WriteEndpointGuardTests.StripComments(Source(Template, "Api", "ApiV1Endpoints.cs"));

    private static Deal CartDeal() => new()
    {
        Id = Guid.NewGuid(), Pattern = "marketplace", AmountSar = 150m, ListingTitle = "سِلعَة",
        InitiatorId = Buyer, InitiatorName = "مُشتَرٍ", CounterpartyId = Seller, CounterpartyName = "بائِع",
    };

    // ═══ ١) الأَبوابُ كُلُّها تَمُرُّ بِالأُنبوب ════════════════════════════════════════════════════════════════

    /// <summary>
    /// <para><b>قُلِبَ بِاسمِه</b> (‏2026-10-13، مُراجَعَةُ المَوجَة 3a — G4 (iii)؛ ‏ADR-036 §١٢‑٣): كانَ
    /// <c>Today_the_studio_cancel_and_both_api_deal_writers_tell_the_buyer_nothing_while_the_member_doors_do</c> (مُودَعٌ في <c>a8cecae</c>).
    /// وصارَ إلغاءُ الاستوديو يُخبِرُ المُشتَري بِنَصِّ «إدارَةِ المَتجَر» (<see cref="DealNotices.NotifyCancelledByStoreAsync"/>)، وإلغاءُ الـAPI
    /// بِإلغاءِ العُضوِ نَفسِه (<see cref="DealNotices.NotifyCancelledAsync"/>)، وتَقَدُّمُ الـAPI الَّذي يُخرِجُ <c>Offered</c> بِقَبولِ العُضوِ نَفسِه
    /// (<see cref="DealNotices.NotifyAcceptedAsync"/> حينَ <see cref="DealNotices.IsAcceptance"/>).</para>
    /// </summary>
    [Fact]
    public void The_studio_cancel_and_both_api_deal_writers_route_the_buyers_notice_through_DealNotices_like_the_member_doors()
    {
        var endpoints = Endpoints();

        // أَبوابُ العُضوِ كَما كانَت.
        var memberAccept = Segment(endpoints, "\"/{slug}/deals/{id:guid}/accept\"", ".RequireStoreWritable();");
        var memberCancel = Segment(endpoints, "\"/{slug}/deals/{id:guid}/cancel\"", ".RequireStoreWritable();");
        Assert.Contains("await notices.NotifyAcceptedAsync(", memberAccept, StringComparison.Ordinal);
        Assert.Contains("await notices.NotifyCancelledAsync(", memberCancel, StringComparison.Ordinal);

        // وإلغاءُ الاستوديو الإداريُّ يُخبِرُ بَعدَ إلغاءٍ ناجِحٍ — بَعدَ سَطرِ التَدقيق.
        var studioCancel = Segment(endpoints, "\"/studio/apps/{slug}/deals/{id:guid}/cancel\"", ".DisableAntiforgery();");
        Assert.Contains("Services.Deals.DealNotices notices, L l", studioCancel, StringComparison.Ordinal);
        // (‏2026-10-13، G5 M4): حَلقَةٌ عَلى ما أُلغِيَ فِعلاً — الصَفقَةُ بَعدَ إلغاءٍ ناجِح، أَو كُلُّ صَفقَةٍ أَلغاها إلغاءُ الطَلَبِ كُلِّه.
        Assert.Contains("foreach (var gone in order?.Cancelled ?? (res.Ok ? new[] { res.Deal! } : Array.Empty<Services.Deals.Deal>()))", studioCancel, StringComparison.Ordinal);
        Assert.Contains("await notices.NotifyCancelledByStoreAsync(slug, gone, auth.UserId!.Value, l);", studioCancel, StringComparison.Ordinal);
        Assert.True(studioCancel.IndexOf("\"deal.cancel\"", StringComparison.Ordinal) < studioCancel.IndexOf("NotifyCancelledByStoreAsync(", StringComparison.Ordinal));

        // والـAPI: الإلغاءُ بِإلغاءِ العُضو، والتَقَدُّمُ الَّذي يَقبَلُ بِقَبولِه.
        var api = Api();
        var apiAdvance = Segment(api, "\"/api/v1/deals/{id:guid}/advance\"", ".RequireApiKey(ApiScopeCatalog.DealsWrite);");
        var apiCancel = Segment(api, "\"/api/v1/deals/{id:guid}/cancel\"", ".RequireApiKey(ApiScopeCatalog.DealsWrite);");
        Assert.Contains("if (DealNotices.IsAcceptance(res)) await notices.NotifyAcceptedAsync(p.TenantSlug, res.Deal!, p.ActorName, l, http.RequestAborted);",
            apiAdvance, StringComparison.Ordinal);
        Assert.Contains("if (res.Ok) await notices.NotifyCancelledAsync(p.TenantSlug, res.Deal!, p.ActorUserId, p.ActorName, l, http.RequestAborted);",
            apiCancel, StringComparison.Ordinal);
        // داخِلَ كُتلَةِ مَرَّةٍ‑واحِدَة: الجَوابُ المُعادُ لِمِفتاحٍ مُستَعمَلٍ لا يُشغِّلُها ثانِيَةً، فَلا إشعارَ ثانٍ.
        Assert.True(apiAdvance.IndexOf("ApiWrite.OnceAsync(", StringComparison.Ordinal) < apiAdvance.IndexOf("NotifyAcceptedAsync(", StringComparison.Ordinal));
        Assert.True(apiCancel.IndexOf("ApiWrite.OnceAsync(", StringComparison.Ordinal) < apiCancel.IndexOf("NotifyCancelledAsync(", StringComparison.Ordinal));
        output.WriteLine("العُضو والاستوديو والـAPI: كُلُّ إلغاءٍ وقَبولٍ ناجِحٍ يَمُرُّ بِـDealNotices");
    }

    /// <summary>
    /// <para><b>قُلِبَ بِاسمِه</b> (‏2026-10-24، تَدقيقُ الإطلاق brief-P1a-1؛ ‏ADR-036 §١٦): كانَ
    /// <c>Today_the_studio_admin_path_accepts_a_cart_order_and_tells_the_buyer_nothing</c> (مُودَعٌ في <c>727bc9f</c>) — المَسارُ الإداريُّ في الاستوديو يُخرِجُ
    /// <c>Offered</c> إلى <c>Booked</c> بِهُوِيَّةِ البائِعِ المَربوطِ ومَقطَعُه بِلا <c>notices.</c> واحِدَة. وصارَ قَبولُه يُخبِرُ المُشتَري بِنَصِّ «إدارَةِ المَتجَر»
    /// (<see cref="DealNotices.NotifyAcceptedByStoreAsync"/> حينَ <see cref="DealNotices.IsAcceptance"/>) بَعدَ سَطرِ التَدقيق — فَيَصدُقُ سَطرُ خَرجِ الصَفِّ أَعلاه
    /// عَلى الاستوديو كُلِّه.</para>
    /// </summary>
    [Fact]
    public void The_studio_admin_path_accepting_a_cart_order_tells_the_buyer_through_DealNotices()
    {
        var studioAdvance = Segment(Endpoints(), "\"/studio/apps/{slug}/deals/{id:guid}/advance\"", ".DisableAntiforgery();");
        Assert.Contains("Services.Deals.DealNotices notices, L l", studioAdvance, StringComparison.Ordinal);
        Assert.Contains("if (Services.Deals.DealNotices.IsAcceptance(result)) await notices.NotifyAcceptedByStoreAsync(slug, result.Deal!, auth.UserId!.Value, l);",
            studioAdvance, StringComparison.Ordinal);
        Assert.Equal(1, studioAdvance.Split("notices.").Length - 1);
        var auditAt = studioAdvance.IndexOf("\"deal.advance\"", StringComparison.Ordinal);
        var noticeAt = studioAdvance.IndexOf("NotifyAcceptedByStoreAsync(", StringComparison.Ordinal);
        var redirectAt = studioAdvance.IndexOf("return Results.Redirect($\"/studio/apps/{slug}/deals/{id}\" + (result.Code", StringComparison.Ordinal);
        Assert.True(auditAt > 0 && noticeAt > auditAt && redirectAt > noticeAt, $"تَرتيبُ التَدقيقِ ثُمَّ الإشعارِ ثُمَّ الرَدّ: audit={auditAt} notice={noticeAt} redirect={redirectAt}");

        var l = new ACommerce.Platform.I18n.L(new Microsoft.AspNetCore.Http.HttpContextAccessor());
        var world = new DocWorld();
        var deals = new DealsService(world.Store, new NoPayments());
        var deal = CartDeal();
        world.Put(deal);
        Assert.Null(DealsService.AdminPathRefusal(deal, AppOwner));
        var moved = deals.AdvanceAsync("shop", deal.Id, deal.CounterpartyId ?? AppOwner, "مالِك التَّطبيق (إداريّ)", "").GetAwaiter().GetResult();
        Assert.True(DealNotices.IsAcceptance(moved));
        if (DealNotices.IsAcceptance(moved))
            Assert.True(Notices(world).NotifyAcceptedByStoreAsync("shop", moved.Deal!, AppOwner, l).GetAwaiter().GetResult());
        var told = Assert.Single(world.Stored.OfType<ACommerce.Kit.Notifications.Notification>());
        Assert.Equal(Buyer, told.UserId);
        Assert.Equal(DealNotices.AcceptedType, told.Type);
        Assert.Equal(l["notifications.deal_accepted_by_store.title"], told.Title);
        output.WriteLine($"الاستوديو: Offered → {moved.Deal!.Stage} · IsAcceptance={DealNotices.IsAcceptance(moved)} · notices. في المَقطَع: 1 · إشعاراتُ المُشتَري: 1");
    }

    /// <summary>مُزَوِّدٌ يَرمي — صَفقَةُ السَلَّةِ هُنا بِلا مُعَرِّفِ دَفع.</summary>
    private sealed class NoPayments : ACommerce.Kit.Payments.IPaymentProvider
    {
        private static InvalidOperationException Touched() => new("لا مالَ في هذا الصَفّ — نودِيَ مُزَوِّدُ الدَفع.");
        public string ProviderName => "forbidden";
        public Task<ACommerce.Kit.Payments.PaymentResult> AuthorizeAsync(ACommerce.Kit.Payments.PaymentRequest req, string idempotencyKey, CancellationToken ct = default) => throw Touched();
        public Task<ACommerce.Kit.Payments.PaymentResult> CaptureAsync(string paymentId, decimal? amount = null, CancellationToken ct = default) => throw Touched();
        public Task<ACommerce.Kit.Payments.PaymentResult> RefundAsync(string paymentId, decimal amount, string reason, CancellationToken ct = default) => throw Touched();
        public Task<ACommerce.Kit.Payments.SubscriptionResult> CreateSubscriptionAsync(ACommerce.Kit.Payments.SubscriptionRequest req, string idempotencyKey, CancellationToken ct = default) => throw Touched();
        public Task<bool> CancelSubscriptionAsync(string subscriptionId, CancellationToken ct = default) => throw Touched();
        public Task<ACommerce.Kit.Payments.Invoice?> GetInvoiceAsync(string paymentId, CancellationToken ct = default) => throw Touched();
    }

    // ═══ ٢) الدَوالُّ النَقِيَّة ═════════════════════════════════════════════════════════════════════════

    private static Deal Moved(DealStage before, DealStage after)
    {
        var d = CartDeal();
        d.Stage = after;
        d.Timeline.Add(new(before, after, "advanced", Seller, "بائِع", null, DateTime.UtcNow));
        return d;
    }

    [Fact]
    public void IsAcceptance_is_a_successful_move_out_of_Offered_and_nothing_else()
    {
        Assert.True(DealNotices.IsAcceptance(new DealAdvanceResult(true, Moved(DealStage.Offered, DealStage.Booked), null)));

        Assert.False(DealNotices.IsAcceptance(new DealAdvanceResult(false, Moved(DealStage.Offered, DealStage.Booked), "x")));
        Assert.False(DealNotices.IsAcceptance(new DealAdvanceResult(true, Moved(DealStage.Booked, DealStage.Confirmed), null)));
        Assert.False(DealNotices.IsAcceptance(new DealAdvanceResult(true, null, null)));
        Assert.False(DealNotices.IsAcceptance(new DealAdvanceResult(true, CartDeal(), null)));          // سِجِلٌّ فارِغ
        var captured = Moved(DealStage.Offered, DealStage.Booked);
        captured.Timeline.Add(new(DealStage.Booked, DealStage.Booked, "note", null, null, "attach x=y", DateTime.UtcNow));
        Assert.False(DealNotices.IsAcceptance(new DealAdvanceResult(true, captured, null)));           // آخِرُ سَطرٍ لَيسَ الانتِقال
    }

    [Fact]
    public void CancelledByStoreFor_tells_the_buyer_from_the_dictionary_without_a_personal_name()
    {
        var l = new ACommerce.Platform.I18n.L(new Microsoft.AspNetCore.Http.HttpContextAccessor());
        var deal = CartDeal();
        var at = new DateTime(2026, 10, 13, 9, 0, 0, DateTimeKind.Utc);
        var n = DealNotices.CancelledByStoreFor("shop", deal, l, at);
        Assert.Equal(Buyer, n.UserId);
        Assert.Equal(DealNotices.CancelledType, n.Type);
        Assert.Equal(l["notifications.deal_cancelled_by_store.title"], n.Title);
        Assert.Equal(l.Format("notifications.deal_cancelled_by_store.body", deal.ListingTitle), n.Body);
        Assert.Contains("«سِلعَة»", n.Body, StringComparison.Ordinal);
        Assert.Equal($"/shop/deals/{deal.Id}", n.RelatedUrl);
        Assert.Equal(at, n.At);
        foreach (var key in new[] { "notifications.deal_cancelled_by_store.title", "notifications.deal_cancelled_by_store.body", "notifications.deal_cancelled_by_store.push_body" })
            Assert.NotEqual(key, ACommerce.Platform.I18n.LocaleCatalog.Text(ACommerce.Platform.I18n.LocaleCatalog.Arabic, key));
        Assert.Equal(l.Format("notifications.deal_cancelled_by_store.push_body", deal.ListingTitle), DealNotices.CancelledByStorePushBody(deal, l));
    }

    /// <summary>قَبولُ إدارَةِ المَتجَرِ (‏2026-10-24، brief-P1a-1): نَسَقُ <see cref="DealNotices.CancelledByStoreFor"/> — المُشتَري، نَوعُ القَبول، ونَصٌّ مِن
    /// القامُوسِ بِلا اسمٍ شَخصِيّ.</summary>
    [Fact]
    public void AcceptedByStoreFor_tells_the_buyer_from_the_dictionary_without_a_personal_name()
    {
        var l = new ACommerce.Platform.I18n.L(new Microsoft.AspNetCore.Http.HttpContextAccessor());
        var deal = CartDeal();
        var at = new DateTime(2026, 10, 24, 9, 0, 0, DateTimeKind.Utc);
        var n = DealNotices.AcceptedByStoreFor("shop", deal, l, at);
        Assert.Equal(Buyer, n.UserId);
        Assert.Equal(DealNotices.AcceptedType, n.Type);
        Assert.Equal(l["notifications.deal_accepted_by_store.title"], n.Title);
        Assert.Equal(l.Format("notifications.deal_accepted_by_store.body", deal.ListingTitle), n.Body);
        Assert.Contains("«سِلعَة»", n.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("بائِع", n.Body, StringComparison.Ordinal);
        Assert.Equal($"/shop/deals/{deal.Id}", n.RelatedUrl);
        Assert.Equal(at, n.At);
        foreach (var key in new[] { "notifications.deal_accepted_by_store.title", "notifications.deal_accepted_by_store.body", "notifications.deal_accepted_by_store.push_body" })
            Assert.NotEqual(key, ACommerce.Platform.I18n.LocaleCatalog.Text(ACommerce.Platform.I18n.LocaleCatalog.Arabic, key));
        Assert.Equal(l.Format("notifications.deal_accepted_by_store.push_body", deal.ListingTitle), DealNotices.AcceptedByStorePushBody(deal, l));
    }

    // ═══ ٣) الأُنبوبُ في عالَمِ الوَثائِق — المُشتَري يُخبَرُ مَرَّة، ومالِكٌ هُوَ المُشتَري لا يُخبَرُ بِما فَعَل ═════════════

    private static DealNotices Notices(DocWorld world)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSignalR();
        var hub = services.BuildServiceProvider().GetRequiredService<IHubContext<ACommerce.Kit.Realtime.Server.RealtimeHub>>();
        var push = new WebPushService(new ConfigurationBuilder().Build(), NullLogger<WebPushService>.Instance);
        return new DealNotices(world.Store, hub, push);
    }

    [Fact]
    public async Task NotifyCancelledByStoreAsync_tells_the_buyer_once_and_an_owner_who_is_the_buyer_never()
    {
        var l = new ACommerce.Platform.I18n.L(new Microsoft.AspNetCore.Http.HttpContextAccessor());

        var world = new DocWorld();
        var deal = CartDeal();
        world.Put(deal);
        Assert.True(await Notices(world).NotifyCancelledByStoreAsync("shop", deal, AppOwner, l));
        var told = Assert.Single(world.Stored.OfType<ACommerce.Kit.Notifications.Notification>());
        Assert.Equal(Buyer, told.UserId);
        Assert.Equal(DealNotices.CancelledType, told.Type);

        // صَفقَةُ البَذرَةِ: مالِكُ التَطبيقِ مُبادِرُها — لا يُخبَرُ بِإلغائِه هُوَ.
        var seedWorld = new DocWorld();
        var seed = CartDeal();
        seed.InitiatorId = AppOwner;
        seedWorld.Put(seed);
        var saves = seedWorld.SaveCalls;
        Assert.False(await Notices(seedWorld).NotifyCancelledByStoreAsync("shop", seed, AppOwner, l));
        Assert.Equal(0, seedWorld.Wrote<ACommerce.Kit.Notifications.Notification>());
        Assert.Equal(saves, seedWorld.SaveCalls);
    }

    /// <summary>قَبولُ إدارَةِ المَتجَرِ في الأُنبوب (‏2026-10-24، brief-P1a-1): المُشتَري يُخبَرُ مَرَّة، ومالِكُ تَطبيقٍ هُوَ المُبادِرُ لا يُخبَرُ ولا يُكتَبُ شَيء.</summary>
    [Fact]
    public async Task NotifyAcceptedByStoreAsync_tells_the_buyer_once_and_an_owner_who_is_the_buyer_never()
    {
        var l = new ACommerce.Platform.I18n.L(new Microsoft.AspNetCore.Http.HttpContextAccessor());

        var world = new DocWorld();
        var deal = CartDeal();
        world.Put(deal);
        Assert.True(await Notices(world).NotifyAcceptedByStoreAsync("shop", deal, AppOwner, l));
        var told = Assert.Single(world.Stored.OfType<ACommerce.Kit.Notifications.Notification>());
        Assert.Equal(Buyer, told.UserId);
        Assert.Equal(DealNotices.AcceptedType, told.Type);

        var seedWorld = new DocWorld();
        var seed = CartDeal();
        seed.InitiatorId = AppOwner;
        seedWorld.Put(seed);
        var saves = seedWorld.SaveCalls;
        Assert.False(await Notices(seedWorld).NotifyAcceptedByStoreAsync("shop", seed, AppOwner, l));
        Assert.Equal(0, seedWorld.Wrote<ACommerce.Kit.Notifications.Notification>());
        Assert.Equal(saves, seedWorld.SaveCalls);
    }
}
