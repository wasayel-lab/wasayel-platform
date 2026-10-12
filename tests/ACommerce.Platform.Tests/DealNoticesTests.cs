using System.Text;
using ACommerce.Kit.Notifications;
using ACommerce.Kit.Payments;
using ACommerce.Platform.I18n;
using ACommerce.Templates.Customer.Marketplace.Services;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using Marten;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ إشعاراتُ الصَفقَةِ عَلى الأُنبوبِ القائِم — P1-5 ═══════════════════════════
//
// **المَقيسُ قَبلَ هذا الكوميت** (موجَزُ الفَجَوات P1-5؛ مُثَبَّتٌ في
// `DealCashPathCharacterizationTests` ‏5554975): `/checkout/submit` و`/accept` بِصِفرِ
// `Notification`/`NudgeAsync`/`push.`؛ `DealsService` لا تَذكُرُ `Notification` حَرفاً؛
// `Notifications.razor` يَرسُمُ أَيقونَةً لِنَوعَي `deal`/`deal_advanced` بِصِفرِ مُنتِج؛ و
// `Deal.cs` يَدَّعي «إِشعار الأَطراف يَخرُج تِلقائيّاً». فَالبائِعُ لا يَعلَمُ بِالطَلَبِ إلّا
// إن فَتَحَ «صَفقات» بِنَفسِه، والمُشتَري لا يَعلَمُ أَنّ طَلَبَه قُبِل.
//
// **والقَرار** (القاعِدَة ٨ — الأُنبوبُ القائِمُ لا رابِع): `DealNotices` تُرَكِّبُ الوَثيقَةَ
// بِدَوالٍّ نَقِيَّةٍ مِن القامُوس، وتَمشي الأُنبوبَ نَفسَه الَّذي تَمشيه `chats/{id}/send`
// و`offer_accepted`: تَخزينٌ ثُمَّ حَفظٌ ثُمَّ جَرَسٌ ثُمَّ دَفعٌ فَوريّ. النُصوصُ تُثَبَّتُ
// كَما تُثَبَّتُ في `NotificationTextTests` — بِمُقابَلَةِ `l.Format` بِالتَعبيرِ الصَريح،
// فَيَمُرُّ الطَرَفانِ بِنَفسِ المُنَسِّق.

public class DealNoticesTests
{
    private static L Fresh() => new(new HttpContextAccessor());

    private static readonly Guid Buyer  = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
    private static readonly Guid Seller = Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb");

    private static Deal CartDeal(decimal amount = 150m, string title = "سِلعَة")
    {
        var d = new Deal
        {
            Id = Guid.NewGuid(), Pattern = "marketplace", AmountSar = amount, ListingTitle = title,
            InitiatorId = Buyer, InitiatorName = "مُشتَرٍ", CounterpartyId = Seller, CounterpartyName = "بائِع",
        };
        d.Refs[DealsService.ListingOwnerRef] = Seller.ToString();
        d.Attributes[CheckoutPayMethods.AttributeKey] = CheckoutPayMethods.CashOnDelivery;
        return d;
    }

    // ─── التَركيبُ النَقِيّ ──────────────────────────────────────────────────

    [Fact]
    public void NewDealFor_targets_the_owner_with_type_deal_and_links_to_the_deal()
    {
        var l = Fresh();
        var deal = CartDeal();
        var at = new DateTime(2026, 10, 11, 9, 0, 0, DateTimeKind.Utc);
        var n = DealNotices.NewDealFor("shop", deal, Seller, l, at);

        Assert.Equal(Seller, n.UserId);
        Assert.Equal(DealNotices.NewDealType, n.Type);
        Assert.Equal("deal", n.Type);
        Assert.Equal($"/shop/deals/{deal.Id}", n.RelatedUrl);
        Assert.Equal(at, n.At);
        Assert.False(n.IsRead);
        Assert.NotEqual(Guid.Empty, n.Id);
        Assert.Equal(l["notifications.deal_new.title"], n.Title);
        Assert.Equal(l.Format("notifications.deal_new.body", deal.ListingTitle, deal.AmountSar), n.Body);
    }

    [Theory]
    [InlineData(150)]
    [InlineData(0)]
    [InlineData(12.5)]
    [InlineData(350000)]
    public void NewDeal_texts_carry_the_listing_title_and_the_amount(decimal amount)
    {
        var l = Fresh();
        var title = "ساعَة يَد";
        Assert.Equal("طَلَبٌ جَديدٌ عَلى إعلانِكَ", l["notifications.deal_new.title"]);
        Assert.Equal($"طَلَبٌ عَلى «{title}» بِـ {amount:N0} ريال — افتَح الصَّفقَة لِقَبولِه.",
            l.Format("notifications.deal_new.body", title, amount));
        Assert.Equal($"طَلَبٌ عَلى «{title}» بِـ {amount:N0} ريال.",
            DealNotices.NewDealPushBody(CartDeal(amount, title), l));
    }

    [Fact]
    public void AcceptedFor_targets_the_initiator_with_type_deal_advanced_and_names_the_acceptor()
    {
        var l = Fresh();
        var deal = CartDeal(title: "ساعَة يَد");
        var n = DealNotices.AcceptedFor("shop", deal, "أَبو خالِد", l, DateTime.UtcNow);

        Assert.Equal(Buyer, n.UserId);
        Assert.Equal(DealNotices.AcceptedType, n.Type);
        Assert.Equal("deal_advanced", n.Type);
        Assert.Equal($"/shop/deals/{deal.Id}", n.RelatedUrl);
        Assert.Equal("قَبِلَ البائِعُ طَلَبَكَ ✓", n.Title);
        Assert.Equal("أَبو خالِد قَبِلَ طَلَبَكَ عَلى «ساعَة يَد» — افتَح الصَّفقَة لِمُتابَعَتِها.", n.Body);
        Assert.Equal("أَبو خالِد قَبِلَ طَلَبَكَ عَلى «ساعَة يَد».", DealNotices.AcceptedPushBody(deal, "أَبو خالِد", l));
    }

    /// <summary>إشعارُ الإلغاء (‏ADR-033 §٦‑ج مُلحَقاً، ‏2026-10-11): لِلمُبادِرِ، بِنَوعِ <c>deal_cancelled</c>،
    /// بِاسمِ المُلغي وعُنوانِ الإعلان — ونَصُّه مِن القامُوسِ بِحَرفِه.</summary>
    [Fact]
    public void CancelledFor_targets_the_initiator_with_type_deal_cancelled_and_names_the_canceller()
    {
        var l = Fresh();
        var deal = CartDeal(title: "ساعَة يَد");
        var n = DealNotices.CancelledFor("shop", deal, "أَبو خالِد", l, DateTime.UtcNow);

        Assert.Equal(Buyer, n.UserId);
        Assert.Equal(DealNotices.CancelledType, n.Type);
        Assert.Equal("deal_cancelled", n.Type);
        Assert.Equal($"/shop/deals/{deal.Id}", n.RelatedUrl);
        Assert.Equal("أَلغى البائِعُ طَلَبَكَ", n.Title);
        Assert.Equal("أَبو خالِد أَلغى طَلَبَكَ عَلى «ساعَة يَد» — افتَح الصَّفقَة لِلتَفاصيل.", n.Body);
        Assert.Equal("أَبو خالِد أَلغى طَلَبَكَ عَلى «ساعَة يَد».", DealNotices.CancelledPushBody(deal, "أَبو خالِد", l));
    }

    /// <summary>مَن يُخبَرُ بِالإلغاء: المُشتَري ما لَم يَكُن هُوَ المُلغي — بِالطَرَفِ أَو بِغَيرِه.</summary>
    [Fact]
    public void Only_a_cancel_by_someone_other_than_the_buyer_tells_the_buyer()
    {
        var deal = CartDeal();
        Assert.True(DealNotices.TellsTheBuyer(deal, Seller));
        Assert.True(DealNotices.TellsTheBuyer(deal, Guid.NewGuid()));
        Assert.False(DealNotices.TellsTheBuyer(deal, Buyer));
    }

    /// <summary><b>مالِكُ الإعلانِ الَّذي هو المُشتَري لا يُشعَرُ بِطَلَبِه</b> (‏2026-10-11، مُراجَعَةُ الموجَةِ الثانِيَةِ
    /// الرابِعَة): الإشعارُ يَقولُ «افتَح الصَّفقَة لِقَبولِه» والقَبولُ يَرُدُّه بِـ<c>actor_is_initiator</c> — بابٌ لا يُفتَح.
    /// المُوجِب: مالِكٌ غَيرُ المُشتَري هو المُستَلِم؛ والسالِب: مالِكٌ هو المُشتَري، أَو لا مالِك — لا أَحَد.</summary>
    [Fact]
    public void The_new_deal_recipient_is_the_listing_owner_unless_he_is_the_buyer()
    {
        Assert.Equal(Seller, DealNotices.NewDealRecipient(CartDeal()));

        var own = CartDeal();
        own.Refs[DealsService.ListingOwnerRef] = Buyer.ToString();
        own.CounterpartyId = null;
        Assert.Null(DealNotices.NewDealRecipient(own));

        var orphan = CartDeal();
        orphan.Refs.Remove(DealsService.ListingOwnerRef);
        Assert.Null(DealNotices.NewDealRecipient(orphan));
    }

    /// <summary>والأُنبوبُ يَقرَأُ الدالَّةَ نَفسَها — بِالخِدمَةِ في عالَمِ الوَثائِق: صَفقَةٌ مالِكُها المُشتَري لا تُكتَبُ لَها
    /// وَثيقَةُ إشعارٍ ولا يُحفَظُ شَيء، وصَفقَةٌ مالِكُها غَيرُه تُكتَبُ لِمالِكِها مَرَّة.</summary>
    [Fact]
    public void NotifyNewDealsAsync_skips_an_owner_who_is_the_buyer_and_tells_any_other_owner_once()
    {
        var world = new DocWorld();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSignalR();
        var hub = services.BuildServiceProvider().GetRequiredService<IHubContext<ACommerce.Kit.Realtime.Server.RealtimeHub>>();
        var push = new WebPushService(new ConfigurationBuilder().Build(), NullLogger<WebPushService>.Instance);
        var notices = new DealNotices(world.Store, hub, push);
        var l = Fresh();

        var own = CartDeal();
        own.Refs[DealsService.ListingOwnerRef] = Buyer.ToString();
        own.CounterpartyId = null;
        var other = CartDeal();
        world.Put(own).Put(other);
        var savesBefore = world.SaveCalls;

        var none = notices.NotifyNewDealsAsync("shop", new[] { own.Id }, l);
        Assert.True(none.IsCompleted);
        Assert.Equal(0, none.Result);
        Assert.Equal(0, world.Wrote<Notification>());
        Assert.Equal(savesBefore, world.SaveCalls);

        var one = notices.NotifyNewDealsAsync("shop", new[] { own.Id, other.Id }, l);
        Assert.True(one.IsCompleted);
        Assert.Equal(1, one.Result);
        var told = Assert.Single(world.Stored.OfType<Notification>());
        Assert.Equal(Seller, told.UserId);
        Assert.Equal($"/shop/deals/{other.Id}", told.RelatedUrl);
    }

    // ─── المَصدَر — الأُنبوبُ والنَوعانِ ومَوضِعا النِداء ─────────────────────────

    private static string RepoRoot => ThemeZeroEquivalenceTests.RepoRoot;
    private const string Template = "libs/templates/ACommerce.Templates.Customer.Marketplace";

    private static string Source(params string[] parts)
    {
        var path = Path.Combine(RepoRoot, Path.Combine(parts));
        Assert.True(File.Exists(path), $"مَصدَرٌ مَفقود: {path} — الأَداةُ عَمياء.");
        var text = File.ReadAllText(path);
        Assert.True(text.Length > 500, $"أَداةٌ عَمياء: {path} لَم يُقرَأ.");
        return text;
    }

    private static string Segment(string source, string from, string to)
    {
        var start = source.IndexOf(from, StringComparison.Ordinal);
        Assert.True(start > 0, $"‏`{from}` غَيرُ مَوجود — الأَداةُ عَمياء.");
        var end = source.IndexOf(to, start + from.Length, StringComparison.Ordinal);
        Assert.True(end > start, $"‏`{to}` لا يَتلو `{from}` — الأَداةُ عَمياء.");
        return WriteEndpointGuardTests.StripComments(source.Substring(start, end - start));
    }

    private static int Count(string text, string needle)
    {
        var n = 0;
        for (var i = text.IndexOf(needle, StringComparison.Ordinal); i >= 0;
             i = text.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
            n++;
        return n;
    }

    /// <summary>الأَيقونَةُ اليَتيمَةُ صارَ لَها مُنتِج: النَوعانِ هُما بِحَرفِهِما ما تَرسُمُه
    /// شاشَةُ الإشعارات.</summary>
    [Fact]
    public void The_types_are_the_ones_the_notifications_screen_already_draws()
    {
        var screen = Source(Template, "Components", "Pages", "Notifications.razor");
        Assert.Contains("\"deal\" or \"deal_advanced\"", screen, StringComparison.Ordinal);
        Assert.Equal("deal", DealNotices.NewDealType);
        Assert.Equal("deal_advanced", DealNotices.AcceptedType);
        // والثالِثُ (‏2026-10-11) في مِفتاحِ الأَيقونَةِ نَفسِه — لا يَسقُطُ إلى الجَرَسِ العامّ.
        // والرابِعُ (‏2026-10-12، ‏ADR-032 الخُطوَة 10) في المِفتاحِ نَفسِه — ثَبَتَ دَفعُ الطَلَبِ عِندَ بَوّابَةِ المَتجَر؛ والسَطرُ يُقرَأُ بِحَرفِه الجَديد.
        Assert.Contains("\"deal\" or \"deal_advanced\" or \"deal_cancelled\" or \"deal_paid\" => \"shopping-bag\"", screen, StringComparison.Ordinal);
        Assert.Equal("deal_cancelled", DealNotices.CancelledType);
        Assert.Equal("deal_paid", DealNotices.PaidType);
    }

    /// <summary>الأُنبوبُ بِتَرتيبِه في كُلٍّ مِن الدالَّتَين: تَخزينٌ، ثُمَّ حَفظٌ، ثُمَّ جَرَسٌ
    /// (<c>NudgeAsync</c> نَفسُها لا نُسخَةٌ ثانِيَة)، ثُمَّ دَفعٌ فَوريّ.</summary>
    [Fact]
    public void Both_methods_walk_the_existing_pipe_in_order_store_save_nudge_push()
    {
        var src = Source(Template, "Services", "Deals", "DealNotices.cs");
        foreach (var (from, to) in new[]
        {
            ("public async Task<int> NotifyNewDealsAsync(", "public async Task NotifyAcceptedAsync("),
            ("public async Task NotifyAcceptedAsync(", "public async Task<bool> NotifyCancelledAsync("),
            // والثالِثَةُ (‏2026-10-11) بِالتَرتيبِ نَفسِه — وكانَت الثانِيَةُ تَمتَدُّ إلى آخِرِ الصِنف.
            // والرابِعَةُ (‏2026-10-12، ‏ADR-032 الخُطوَة 10) بِالتَرتيبِ نَفسِه؛ والثالِثَةُ صارَت تَنتَهي عِندَها لا عِندَ آخِرِ الصِنف.
            ("public async Task<bool> NotifyCancelledAsync(", "public async Task<int> NotifyPaidAsync("),
            ("public async Task<int> NotifyPaidAsync(", "\n}"),
        })
        {
            var body = Segment(src, from, to);
            var store = body.IndexOf("s.Store(n);", StringComparison.Ordinal);
            var save  = body.IndexOf("SaveChangesAsync(ct);", StringComparison.Ordinal);
            var nudge = body.IndexOf("MarketplaceTemplateExtensions.NudgeAsync(_hub, slug,", StringComparison.Ordinal);
            var push  = body.IndexOf("_push.SendAsync(_store, slug,", StringComparison.Ordinal);
            Assert.True(store > 0 && save > store && nudge > save && push > nudge,
                $"تَرتيبُ الأُنبوبِ في `{from}`: store={store} save={save} nudge={nudge} push={push}");
        }

        var endpoints = Source(Template, "MarketplaceTemplateExtensions.cs");
        Assert.Contains("internal static async Task NudgeAsync(", endpoints, StringComparison.Ordinal);
        // ثَلاثَةٌ لا اثنان (‏2026-10-11): لِكُلِّ دالَّةِ أُنبوبٍ جَرَسٌ واحِد، بِالدالَّةِ نَفسِها.
        // وأَربَعَةٌ مُنذُ ‏2026-10-12 (‏ADR-032 الخُطوَة 10): `NotifyPaidAsync` رابِعَةُ دَوالِّ الأُنبوب — جَرَسٌ واحِدٌ بِالدالَّةِ نَفسِها.
        Assert.Equal(4, Count(WriteEndpointGuardTests.StripComments(src), "MarketplaceTemplateExtensions.NudgeAsync(_hub, slug,"));
    }

    /// <summary><c>/checkout/submit</c>: الصَفَقاتُ ذَواتُ المالِكِ تُجمَعُ في الحَلقَةِ،
    /// والإشعارُ <b>بَعدَ الحَفظ</b>؛ و<c>/accept</c>: إشعارُ المُشتَري بَعدَ قَبولٍ ناجِحٍ
    /// وَحدَه. والتَركيبُ في الخِدمَةِ لا في الجِسم.</summary>
    [Fact]
    public void Checkout_submit_notifies_owners_after_the_save_and_accept_notifies_the_buyer_on_success()
    {
        var endpoints = Source(Template, "MarketplaceTemplateExtensions.cs");
        var submit = Segment(endpoints, "\"/{slug}/checkout/submit\"", ".RequireStoreWritable();");
        var born   = submit.IndexOf("var born = new List<Guid>();", StringComparison.Ordinal);
        var bind   = submit.IndexOf("await deals.BindListingOwnerAsync(slug, deal.Id, oid);", StringComparison.Ordinal);
        var add    = submit.IndexOf("born.Add(deal.Id);", StringComparison.Ordinal);
        var save   = submit.LastIndexOf("await s.SaveChangesAsync();", StringComparison.Ordinal);
        var notify = submit.IndexOf("await notices.NotifyNewDealsAsync(slug, born, l);", StringComparison.Ordinal);
        Assert.True(born > 0 && bind > born && add > bind && save > add && notify > save,
            $"تَرتيبُ الإشعارِ في النُقطَة: born={born} bind={bind} add={add} save={save} notify={notify}");
        Assert.Equal(0, Count(submit, "new ACommerce.Kit.Notifications.Notification"));   // التَركيبُ في الخِدمَة

        var accept = Segment(endpoints, "\"/{slug}/deals/{id:guid}/accept\"", ".RequireStoreWritable();");
        var call   = accept.IndexOf("deals.AcceptAsync(", StringComparison.Ordinal);
        var tell   = accept.IndexOf("if (res.Ok) await notices.NotifyAcceptedAsync(slug, res.Deal!, userName, l);", StringComparison.Ordinal);
        Assert.True(call > 0 && tell > call, $"إشعارُ المُشتَري لا يَتلو القَبول: call={call} tell={tell}");
    }

    /// <summary><b>ومَسارُ العَرضِ عَلى إعلان</b> (<c>/{slug}/listings/{id}/deal</c>) — كانَ يُولِدُ الصَفقَةَ
    /// بِصِفرِ إشعارٍ و<c>/checkout/submit</c> يُشعِر (فَجوَةٌ تَرَكَتها P1-5)، فَالبائِعُ لا يَعلَمُ بِعَرضٍ
    /// يَنتَظِرُ قَبولَه. صارَ يُنادي الأُنبوبَ نَفسَه <b>بَعدَ</b> أَن حُفِظَت الصَفقَةُ ومَرجِعُ مالِكِها، والتَركيبُ
    /// في الخِدمَةِ لا في الجِسم. (قيسَ أَحمَرَ عَلى الكودِ قَبلَ النِداء: لا نِداءَ في المَقطَع.)</summary>
    [Fact]
    public void The_offer_path_notifies_the_listing_owner_once_the_deal_and_its_owner_ref_are_saved()
    {
        var endpoints = Source(Template, "MarketplaceTemplateExtensions.cs");
        var offer  = Segment(endpoints, "\"/{slug}/listings/{id:guid}/deal\"", ".RequireStoreWritable();");
        var start  = offer.IndexOf("var deal = await deals.StartAsync(slug, pattern,", StringComparison.Ordinal);
        var attach = offer.IndexOf("await deals.AttachRefAsync(slug, deal.Id, \"listing_owner\", ownerGuid.ToString());", StringComparison.Ordinal);
        var notify = offer.IndexOf("await notices.NotifyNewDealsAsync(slug, new[] { deal.Id }, l);", StringComparison.Ordinal);
        var back   = offer.IndexOf("return Results.Redirect(Link(req, slug, $\"deals/{deal.Id}\"));", StringComparison.Ordinal);
        Assert.True(start > 0 && attach > start && notify > attach && back > notify,
            $"تَرتيبُ الإشعارِ في مَسارِ العَرض: start={start} attach={attach} notify={notify} back={back}");
        Assert.Equal(1, Count(offer, "NotifyNewDealsAsync("));
        Assert.Contains("Services.Deals.DealNotices notices, L l) =>", offer, StringComparison.Ordinal);
        Assert.Equal(0, Count(offer, "new ACommerce.Kit.Notifications.Notification"));   // التَركيبُ في الخِدمَة
        Assert.Equal(0, Count(offer, "NudgeAsync("));
        Assert.Equal(0, Count(offer, "push."));
    }

    /// <summary><b>و<c>/cancel</c> يُخبِرُ المُشتَريَ بَعدَ إلغاءٍ ناجِحٍ وَحدَه</b> (‏ADR-033 §٦‑ج مُلحَقاً،
    /// ‏2026-10-11): كانَ صِفرُ <c>notices.</c> في مَقطَعِه — فَرَفضُ البائِعِ المَربوطِ لِلطَلَبِ عِندَ
    /// <c>Offered</c> صامِت. والقَرارُ «مَن يُخبَر» في الخِدمَة (<see cref="DealNotices.TellsTheBuyer"/>)
    /// لا في الجِسم، والتَركيبُ كَذلك.</summary>
    [Fact]
    public void Cancel_tells_the_buyer_after_a_successful_cancel_only()
    {
        var endpoints = Source(Template, "MarketplaceTemplateExtensions.cs");
        var cancel = Segment(endpoints, "\"/{slug}/deals/{id:guid}/cancel\"", ".RequireStoreWritable();");
        var call = cancel.IndexOf("var res = await deals.CancelAsync(slug, id,", StringComparison.Ordinal);
        var tell = cancel.IndexOf("if (res.Ok) await notices.NotifyCancelledAsync(slug, res.Deal!, userId, userName, l);", StringComparison.Ordinal);
        var back = cancel.IndexOf("return Results.Redirect(Link(req, slug, $\"deals/{id}\"", StringComparison.Ordinal);
        Assert.True(call > 0 && tell > call && back > tell, $"إخبارُ المُشتَري لا يَتلو الإلغاء: call={call} tell={tell} back={back}");
        Assert.Equal(1, Count(cancel, "NotifyCancelledAsync("));
        Assert.Contains("Services.Deals.DealNotices notices, L l) =>", cancel, StringComparison.Ordinal);
        Assert.Equal(0, Count(cancel, "new ACommerce.Kit.Notifications.Notification"));
        Assert.Equal(0, Count(cancel, "NudgeAsync("));
        Assert.Equal(0, Count(cancel, "InitiatorId"));   // «مَن يُخبَر» في الخِدمَة لا في الجِسم
    }

    /// <summary><b>والمَسارُ الإداريُّ يَبقى كَما هُو</b>: بَذرَةُ الاستوديو تُولِدُ صَفقَةَ عَرضٍ تَجريبِيَّةً
    /// مُبادِرُها مالِكُ التَطبيقِ نَفسُه وبِلا إعلانٍ ولا مَرجِعِ مالِك — فَلا أَحَدَ يُشعَر، ولا نِداءَ هُناك.
    /// ونُقطَةُ التَقَدُّمِ الإدارِيَّة لا تُولِدُ صَفقَة.</summary>
    [Fact]
    public void The_studio_seed_path_births_a_demo_deal_without_an_owner_and_notifies_nobody()
    {
        var endpoints = Source(Template, "MarketplaceTemplateExtensions.cs");
        var seed = Segment(endpoints, "\"/studio/apps/{slug}/deals/seed\"", ".DisableAntiforgery();");
        Assert.Equal(1, Count(seed, "deals.StartAsync(slug, pattern,"));
        Assert.Contains("listingId: null,", seed, StringComparison.Ordinal);
        Assert.Equal(0, Count(seed, "AttachRefAsync("));
        Assert.Equal(0, Count(seed, "notices."));
        Assert.Equal(0, Count(seed, "Notification"));
    }

    /// <summary>الخِدمَةُ نَفسُها ما زالَت لا تُشعِر — المُنتِجُ <c>DealNotices</c> وَحدَها،
    /// وتَعليقُ <c>Deal.cs</c> يُسَمّيها بَدَلَ دَعوى «يَخرُج تِلقائيّاً».</summary>
    [Fact]
    public void DealsService_still_notifies_nobody_and_Deal_cs_names_the_real_producer()
    {
        Assert.Equal(0, Count(Source(Template, "Services", "Deals", "DealsService.cs"), "Notification"));
        var deal = Source(Template, "Services", "Deals", "Deal.cs");
        Assert.DoesNotContain("إِشعار الأَطراف يَخرُج تِلقائيّاً", deal, StringComparison.Ordinal);
        Assert.Contains("DealNotices", deal, StringComparison.Ordinal);
    }
}

// ─── البُرهانُ الحَيّ — الإشعارُ يُكتَبُ فِعلاً عَلى Postgres ───────────────────
//
// يَتَخَطّى نَفسَه ما لَم تُضبَط WASAYEL_LIVE_PROOF=1 و ConnectionStrings__Postgres، بِنَفسِ
// عَقدِ `LiveDealAcceptSequenceProofTests` حَرفاً. الجَرَسُ مِن حاوِيَةِ SignalR بِلا
// خادِم (يَبتَلِعُ `NudgeAsync` فَشَلَه)، والدَفعُ الفَوريُّ بِمَفاتيحٍ مُوَلَّدَةٍ ولا
// اشتِراكاتَ لِلمُستَخدِمِ فَلا يُرسَلُ شَيء — المَقيسُ هُنا **الوَثيقَةُ**: لِمَن كُتِبَت
// وبِأَيِّ نَوع، وأَنّ صَفقَةً بِلا مالِكٍ أَو غَيرَ مَوجودَةٍ لا تُشعِرُ أَحَداً.

public class LiveDealNoticesProofTests(ITestOutputHelper output)
{
    private static bool Enabled =>
        Environment.GetEnvironmentVariable("WASAYEL_LIVE_PROOF") == "1";

    private static string? ConnectionString =>
        Environment.GetEnvironmentVariable("ConnectionStrings__Postgres");

    private static string ResilientConnection =>
        ConnectionString!.TrimEnd(';') + ";Timeout=60;Command Timeout=120";

    private static DocumentStore BuildStore() => DocumentStore.For(o =>
    {
        o.Connection(ResilientConnection);
        o.DatabaseSchemaName = "platform";
        o.Policies.AllDocumentsAreMultiTenanted();
        o.Events.TenancyStyle = global::JasperFx.MultiTenancy.TenancyStyle.Conjoined;
        o.Schema.For<ACommerce.Kit.Tenants.Tenant>().SingleTenanted().Identity(x => x.Id);
        o.AutoCreateSchemaObjects = JasperFx.AutoCreate.All;
    });

    private sealed class ForbiddenPaymentProvider : IPaymentProvider
    {
        private static InvalidOperationException Touched() => new("الإشعارُ لا يَمَسُّ المال — نودِيَ مُزَوِّدُ الدَفع.");
        public string ProviderName => "forbidden";
        public Task<PaymentResult> AuthorizeAsync(PaymentRequest req, string idempotencyKey, CancellationToken ct = default) => throw Touched();
        public Task<PaymentResult> CaptureAsync(string paymentId, decimal? amount = null, CancellationToken ct = default) => throw Touched();
        public Task<PaymentResult> RefundAsync(string paymentId, decimal amount, string reason, CancellationToken ct = default) => throw Touched();
        public Task<SubscriptionResult> CreateSubscriptionAsync(SubscriptionRequest req, string idempotencyKey, CancellationToken ct = default) => throw Touched();
        public Task<bool> CancelSubscriptionAsync(string subscriptionId, CancellationToken ct = default) => throw Touched();
        public Task<Invoice?> GetInvoiceAsync(string paymentId, CancellationToken ct = default) => throw Touched();
    }

    [Fact]
    public async Task A_new_cart_deal_notifies_its_listing_owner_once_and_acceptance_notifies_the_buyer()
    {
        if (!Enabled || string.IsNullOrEmpty(ConnectionString)) return;

        void Log(string line) => output.WriteLine(line);

        await using var store = BuildStore();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSignalR();
        await using var sp = services.BuildServiceProvider();
        var hub = sp.GetRequiredService<IHubContext<ACommerce.Kit.Realtime.Server.RealtimeHub>>();
        var push = new WebPushService(new ConfigurationBuilder().Build(), NullLogger<WebPushService>.Instance);
        var deals = new DealsService(store, new ForbiddenPaymentProvider());
        var notices = new DealNotices(store, hub, push);
        var l = new L(new HttpContextAccessor());

        var slug = "zz-proof-" + Guid.NewGuid().ToString("N")[..8];
        var buyer = Guid.NewGuid();
        var seller = Guid.NewGuid();

        try
        {
            var deal = await deals.StartAsync(slug, "marketplace", buyer, "مُشتَرٍ", null, "سِلعَة", 150m,
                new() { [CheckoutPayMethods.AttributeKey] = CheckoutPayMethods.CashOnDelivery });
            await deals.AttachRefAsync(slug, deal.Id, DealsService.ListingOwnerRef, seller.ToString());
            await deals.BindListingOwnerAsync(slug, deal.Id, seller.ToString());
            var orphan = await deals.StartAsync(slug, "marketplace", buyer, "مُشتَرٍ", null, "بِلا مالِك", 10m);

            // ١) إشعارُ المالِك: صَفقَةٌ بِمالِك، وصَفقَةٌ بِلا مالِك، ومُعَرِّفٌ لا صَفقَةَ لَه — واحِدٌ يُشعَر.
            var sent = await notices.NotifyNewDealsAsync(slug, new[] { deal.Id, orphan.Id, Guid.NewGuid() }, l);
            Assert.Equal(1, sent);
            await using (var q = store.QuerySession(slug))
            {
                var forSeller = await q.Query<Notification>().Where(n => n.UserId == seller).ToListAsync();
                Assert.Single(forSeller);
                Assert.Equal(DealNotices.NewDealType, forSeller[0].Type);
                Assert.Equal($"/{slug}/deals/{deal.Id}", forSeller[0].RelatedUrl);
                Assert.False(forSeller[0].IsRead);
                Assert.Equal(0, await q.Query<Notification>().CountAsync(n => n.UserId == buyer));
                Log($"إشعارُ المالِك: sent={sent} · type={forSeller[0].Type} · url={forSeller[0].RelatedUrl} · body={forSeller[0].Body}");
            }

            // ٢) القَبولُ ثُمَّ إشعارُ المُشتَري.
            var accepted = await deals.AcceptAsync(slug, deal.Id, new DealAcceptor(seller, "بائِع"));
            Assert.True(accepted.Ok, accepted.Violation?.MessageAr);
            await notices.NotifyAcceptedAsync(slug, accepted.Deal!, "بائِع", l);
            await using (var q = store.QuerySession(slug))
            {
                var forBuyer = await q.Query<Notification>().Where(n => n.UserId == buyer).ToListAsync();
                Assert.Single(forBuyer);
                Assert.Equal(DealNotices.AcceptedType, forBuyer[0].Type);
                Assert.Equal($"/{slug}/deals/{deal.Id}", forBuyer[0].RelatedUrl);
                Assert.Equal(1, await q.Query<Notification>().CountAsync(n => n.UserId == seller));   // لا إشعارَ ثانِياً لِلمالِك
                Log($"إشعارُ المُشتَري: type={forBuyer[0].Type} · body={forBuyer[0].Body}");
            }
        }
        finally
        {
            await using var s = store.LightweightSession(slug);
            foreach (var d in await s.Query<Deal>().ToListAsync()) s.Delete(d);
            foreach (var n in await s.Query<Notification>().ToListAsync()) s.Delete(n);
            await s.SaveChangesAsync();
        }
    }

    /// <summary><b>مَسارُ العَرضِ عَلى إعلانٍ بِتَسَلسُلِ نُقطَتِه</b> (‏`StartAsync` بِلا طَريقَةِ دَفع، ثُمَّ مَرجِعُ
    /// المالِكِ بِلا رَبط، ثُمَّ النِداءُ نَفسُه): مالِكُ الإعلانِ يُشعَرُ مَرَّةً بِنَوعِ `deal`، والمُبادِرُ لا؛
    /// وعَرضٌ عَلى إعلانٍ بِلا مالِكٍ لا يُشعِرُ أَحَداً.</summary>
    [Fact]
    public async Task An_offer_on_a_listing_notifies_its_owner_and_an_offer_on_an_ownerless_listing_notifies_nobody()
    {
        if (!Enabled || string.IsNullOrEmpty(ConnectionString)) return;

        void Log(string line) => output.WriteLine(line);

        await using var store = BuildStore();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSignalR();
        await using var sp = services.BuildServiceProvider();
        var hub = sp.GetRequiredService<IHubContext<ACommerce.Kit.Realtime.Server.RealtimeHub>>();
        var push = new WebPushService(new ConfigurationBuilder().Build(), NullLogger<WebPushService>.Instance);
        var deals = new DealsService(store, new ForbiddenPaymentProvider());
        var notices = new DealNotices(store, hub, push);
        var l = new L(new HttpContextAccessor());

        var slug = "zz-proof-" + Guid.NewGuid().ToString("N")[..8];
        var buyer = Guid.NewGuid();
        var seller = Guid.NewGuid();

        try
        {
            var offer = await deals.StartAsync(slug, "marketplace", buyer, "مُشتَرٍ", Guid.NewGuid(), "سِلعَة", 120m);
            await deals.AttachRefAsync(slug, offer.Id, "listing_owner", seller.ToString());
            var sent = await notices.NotifyNewDealsAsync(slug, new[] { offer.Id }, l);

            var ownerless = await deals.StartAsync(slug, "marketplace", buyer, "مُشتَرٍ", Guid.NewGuid(), "بِلا مالِك", 10m);
            var none = await notices.NotifyNewDealsAsync(slug, new[] { ownerless.Id }, l);

            Assert.Equal(1, sent);
            Assert.Equal(0, none);
            await using var q = store.QuerySession(slug);
            var forSeller = await q.Query<Notification>().Where(n => n.UserId == seller).ToListAsync();
            Assert.Single(forSeller);
            Assert.Equal(DealNotices.NewDealType, forSeller[0].Type);
            Assert.Equal($"/{slug}/deals/{offer.Id}", forSeller[0].RelatedUrl);
            Assert.Equal(0, await q.Query<Notification>().CountAsync(n => n.UserId == buyer));
            Log($"عَرضٌ عَلى إعلان: sent={sent} · type={forSeller[0].Type} · body={forSeller[0].Body} · بِلا مالِك: sent={none}");
        }
        finally
        {
            await using var s = store.LightweightSession(slug);
            foreach (var d in await s.Query<Deal>().ToListAsync()) s.Delete(d);
            foreach (var n in await s.Query<Notification>().ToListAsync()) s.Delete(n);
            await s.SaveChangesAsync();
        }
    }

    /// <summary><b>البائِعُ المَربوطُ يُلغي عِندَ <c>Offered</c> والمُشتَري يُخبَرُ مَرَّة</b> (‏ADR-033 §٦‑ج مُلحَقاً،
    /// ‏2026-10-11) — بِتَسَلسُلِ النُقطَتَين: وِلادَةُ <c>/checkout/submit</c> ثُمَّ <c>CancelAsync</c> بِالبائِعِ ثُمَّ
    /// النِداءُ نَفسُه؛ ومُشتَرٍ يُلغي طَلَبَه الآخَرَ لا يُخبِرُ أَحَداً.</summary>
    [Fact]
    public async Task A_bound_vendor_cancelling_at_Offered_tells_the_buyer_once_and_a_buyer_cancelling_tells_nobody()
    {
        if (!Enabled || string.IsNullOrEmpty(ConnectionString)) return;

        void Log(string line) => output.WriteLine(line);

        await using var store = BuildStore();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSignalR();
        await using var sp = services.BuildServiceProvider();
        var hub = sp.GetRequiredService<IHubContext<ACommerce.Kit.Realtime.Server.RealtimeHub>>();
        var push = new WebPushService(new ConfigurationBuilder().Build(), NullLogger<WebPushService>.Instance);
        var deals = new DealsService(store, new ForbiddenPaymentProvider());
        var notices = new DealNotices(store, hub, push);
        var l = new L(new HttpContextAccessor());

        var slug = "zz-proof-" + Guid.NewGuid().ToString("N")[..8];
        var buyer = Guid.NewGuid();
        var seller = Guid.NewGuid();

        async Task<Deal> Born()
        {
            var d = await deals.StartAsync(slug, "marketplace", buyer, "مُشتَرٍ", null, "سِلعَة", 150m,
                new() { [CheckoutPayMethods.AttributeKey] = CheckoutPayMethods.CashOnDelivery });
            await deals.AttachRefAsync(slug, d.Id, DealsService.ListingOwnerRef, seller.ToString());
            await deals.BindListingOwnerAsync(slug, d.Id, seller.ToString());
            return d;
        }

        try
        {
            var declined = await Born();
            var byVendor = await deals.CancelAsync(slug, declined.Id, new DealCanceller(seller, "بائِع", IsStoreAdmin: false), "نَفِدَت");
            Assert.True(byVendor.Ok, byVendor.Violation?.MessageAr);
            Assert.Equal(DealStage.Offered, byVendor.Deal!.Stage);
            Assert.True(await notices.NotifyCancelledAsync(slug, byVendor.Deal!, seller, "بائِع", l));

            var withdrawn = await Born();
            var byBuyer = await deals.CancelAsync(slug, withdrawn.Id, new DealCanceller(buyer, "مُشتَرٍ", IsStoreAdmin: false), "عَدَلتُ");
            Assert.True(byBuyer.Ok, byBuyer.Violation?.MessageAr);
            Assert.False(await notices.NotifyCancelledAsync(slug, byBuyer.Deal!, buyer, "مُشتَرٍ", l));

            await using var q = store.QuerySession(slug);
            var forBuyer = await q.Query<Notification>().Where(n => n.UserId == buyer).ToListAsync();
            Assert.Single(forBuyer);
            Assert.Equal(DealNotices.CancelledType, forBuyer[0].Type);
            Assert.Equal($"/{slug}/deals/{declined.Id}", forBuyer[0].RelatedUrl);
            Assert.Equal(0, await q.Query<Notification>().CountAsync(n => n.UserId == seller));
            Log($"إلغاءُ البائِعِ عِندَ Offered: type={forBuyer[0].Type} · body={forBuyer[0].Body} · إلغاءُ المُشتَري: لا إشعار");
        }
        finally
        {
            await using var s = store.LightweightSession(slug);
            foreach (var d in await s.Query<Deal>().ToListAsync()) s.Delete(d);
            foreach (var n in await s.Query<Notification>().ToListAsync()) s.Delete(n);
            await s.SaveChangesAsync();
        }
    }
}
