using System.Net;
using ACommerce.Kit.Notifications;
using ACommerce.Kit.Payments;
using ACommerce.Platform.I18n;
using ACommerce.Platform.Providers;
using ACommerce.Templates.Customer.Marketplace.Services;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.Abstractions;
using static ACommerce.Platform.Tests.TenantGatewayEndpointsTests;

namespace ACommerce.Platform.Tests;

// ═══ طَلَبٌ لَم يُدفَع حَتّى انتَهَت مُهلَةُ فاتورَتِه — يُلغى كُلُّه بِاسمِ المَنصَّة (قَرارُ المالِك د5 (٤)، ‏2026-10-29) ═══════════════════
//
// **القَرار (د5 (٤) «ب»)**: «يُلغى الطلب كله آليًّا، وتُلغى الفاتورة عند ميسر إن بقيت مفتوحة، ويُخبَر المشتري وكل بائع فيه». و(٣): المُهلَةُ ‏24 ساعَةً
// مِن إنشاءِ الفاتورَة، يُرسَلُ تاريخُها مَعَها ويُكتَبُ عَلى صَفَقاتِها. **وكَسولٌ لا مُجَدوَل** (‏ADR-032 §٩‑٧): لا مُؤَقِّتَ ولا خِدمَةَ خَلفِيَّةَ في المَنصَّة،
// فَالإلغاءُ يَقَعُ في أَوَّلِ تَحَقُّقٍ بَعدَ الانتِهاء — وهُوَ الطَلَبُ الوَحيدُ الَّذي يَسأَلُ البَوّابَةَ عَن الفاتورَةِ أَصلاً.
//
// **التَوصيف**: <c>TenantGatewayEndpointsTests.Today_an_expired_invoice_seen_at_verify_leaves_its_deals_active</c> (`23fae63`)، قُلِبَ بِاسمِه هُناك.

public class GatewayInvoiceExpiryTests(ITestOutputHelper output)
{
    private static readonly DateTimeOffset Opened = new(2026, 10, 29, 8, 0, 0, TimeSpan.Zero);

    private static Deal Load(World w, Guid id) => w.Docs.Read<Deal>(id.ToString())!;

    private static Deal DealExpiringAt(string? at)
    {
        var deal = new Deal { Id = Guid.NewGuid(), Status = DealStatus.Active };
        if (at is not null) deal.Refs[GatewayPayments.InvoiceExpiresAtRef] = at;
        return deal;
    }

    // ═══ ١) الدالَّةُ النَقِيَّة — المُوجِبُ والسالِبُ لِكُلِّ حال ═══════════════════════════════════════════════════

    /// <summary><b>المُوجِب</b>: البَوّابَةُ تَقولُ «expired» (ولو بِلا تاريخٍ مَكتوب)، أَو الفاتورَةُ «initiated» وقَد بَلَغَ «الآنَ» تاريخَها أَو جاوَزَه.</summary>
    [Theory]
    [InlineData("expired", null, 0)]
    [InlineData("expired", "2026-10-30T08:00:00.000Z", -3600)]
    [InlineData("initiated", "2026-10-30T08:00:00.000Z", 0)]
    [InlineData("initiated", "2026-10-30T08:00:00.000Z", 1)]
    public void Expiry_is_due_when_the_gateway_says_expired_or_an_open_invoice_passed_its_written_date(string status, string? at, int secondsAfter)
        => Assert.True(GatewayPayments.ExpiryDue(status, DealExpiringAt(at), Opened.AddHours(24).AddSeconds(secondsAfter)));

    /// <summary><b>السالِب</b>: مَفتوحَةٌ قَبلَ تاريخِها أَو بِلا تاريخٍ (فُتِحَت قَبلَ ‏2026-10-29) أَو بِتاريخٍ لا يُقرَأ؛ وكُلُّ حالٍ سِوى «expired» و«initiated» ولو
    /// جاوَزَت التاريخ — المَدفوعَةُ والمُلغاةُ والمُبطَلَةُ والمُستَرجَعَةُ والمُعَلَّقَةُ والمَرفوضَة.</summary>
    [Theory]
    [InlineData("initiated", "2026-10-30T08:00:00.000Z", -1)]
    [InlineData("initiated", null, 86400)]
    [InlineData("initiated", "غَداً", 86400)]
    [InlineData("paid", "2026-10-30T08:00:00.000Z", 86400)]
    [InlineData("canceled", "2026-10-30T08:00:00.000Z", 86400)]
    [InlineData("voided", "2026-10-30T08:00:00.000Z", 86400)]
    [InlineData("refunded", "2026-10-30T08:00:00.000Z", 86400)]
    [InlineData("on_hold", "2026-10-30T08:00:00.000Z", 86400)]
    [InlineData("failed", "2026-10-30T08:00:00.000Z", 86400)]
    public void Expiry_is_not_due_before_the_date_without_a_date_or_for_any_other_status(string status, string? at, int secondsAfter)
        => Assert.False(GatewayPayments.ExpiryDue(status, DealExpiringAt(at), Opened.AddHours(24).AddSeconds(secondsAfter)));

    /// <summary>المُلغي: المَنصَّةُ بِلا مُعَرِّفٍ وبِسُلطَةِ الإدارَة — وسَطرُ إلغائِه يُسَمّى «المَنصَّة» في الصَفحَتَين؛ وسَطرُ عُضوٍ اسمُه «platform» بِمُعَرِّفِه لا.</summary>
    [Fact]
    public void The_expiry_canceller_is_the_platform_and_its_line_is_named_from_the_dictionary()
    {
        Assert.Equal((Guid.Empty, DealPatternCatalog.PlatformActor, true),
            (GatewayPayments.ExpiryCanceller.ActorId, GatewayPayments.ExpiryCanceller.ActorName, GatewayPayments.ExpiryCanceller.IsStoreAdmin));
        Assert.True(DealEvent.IsByPlatform(new(DealStage.Offered, DealStage.Offered, "cancelled", Guid.Empty, DealPatternCatalog.PlatformActor, "x", DateTime.UtcNow)));
        Assert.True(DealEvent.IsByPlatform(new(DealStage.Confirmed, DealStage.Paid, "advanced", null, DealPatternCatalog.PlatformActor, null, DateTime.UtcNow)));
        Assert.False(DealEvent.IsByPlatform(new(DealStage.Offered, DealStage.Offered, "cancelled", Guid.NewGuid(), DealPatternCatalog.PlatformActor, "x", DateTime.UtcNow)));
        Assert.False(DealEvent.IsByPlatform(new(DealStage.Offered, DealStage.Offered, "cancelled", Guid.Empty, "مُشتَرٍ", "x", DateTime.UtcNow)));
        var reason = LocaleCatalog.Text(LocaleCatalog.Arabic, GatewayPayments.ExpiryReasonKey);
        Assert.False(LocaleCatalog.IsPlaceholder(reason));
        Assert.Contains("مُهلَة", reason, StringComparison.Ordinal);
    }

    // ═══ ٢) الطَريقُ كُلُّه — فَتحٌ بِتاريخ، ثُمَّ تَحَقُّقٌ بَعدَه ═════════════════════════════════════════════════════

    /// <summary>عالَمُ سَلَّةٍ بِلا فاتورَة، وخِدمَةُ صَفَقاتٍ بِمُلغي فاتورَةٍ عَلى مُعالِجِ العالَم — وبَوّابَتانِ بِساعَتَين: واحِدَةٌ تَفتَحُ عِندَ <see cref="Opened"/>،
    /// وأُخرى تَتَحَقَّقُ بَعدَها بِـ<paramref name="verifyAfter"/>.</summary>
    private static async Task<(World W, GatewayPayments Verify)> OpenedCart(TimeSpan verifyAfter)
    {
        ProviderVault? sealedWith = null;
        var w = await PaidCart(v => { sealedWith = v; return PaymentsBinding(v); }, withSeller: true, invoice: false);
        var client = new MoyasarClient(new HttpClient(w.Moyasar), NullLogger<MoyasarClient>.Instance);
        var deals = new DealsService(w.Docs.Store, w.Platform,
            new GatewayInvoiceCloser(new TenantProviderService(w.Docs.Store), sealedWith!, client));
        GatewayPayments At(DateTimeOffset now) => new(deals, new TenantProviderService(w.Docs.Store), sealedWith!, client, clock: new FixedClock(now));

        var metadata = GatewayPaymentProof.MetadataFor(w.Ids);
        w.Moyasar.Then(HttpStatusCode.Created, MoyasarClientTests.InvoiceJson("initiated", 15000, metadata: new Dictionary<string, string>()))
                 .Then(HttpStatusCode.OK, MoyasarClientTests.InvoiceJson("initiated", 15000, metadata: metadata));
        var opened = await At(Opened).OpenInvoiceAsync(Slug, new DealPayer(Buyer), w.Ids, "مَتجَر", "https://store.example/r", new L(new HttpContextAccessor()));
        Assert.True(opened.Ok, opened.Code);
        return (w with { Deals = deals }, At(Opened + verifyAfter));
    }

    private static string[] Sent(World w) => w.Moyasar.Requests.Select(r => $"{r.Method} {r.RequestUri!.AbsolutePath}").ToArray();

    /// <summary><b>المُوجِبُ كامِلاً</b>: فاتورَةٌ ما زالَت «initiated» عِندَ البَوّابَةِ وقَد مَضَت ‏24 ساعَةً وثانِيَة — التَحَقُّقُ يُلغي الصَفقَتَينِ بِاسمِ المَنصَّة،
    /// وإلغاءُ الأَخيرَةِ يُلغي الفاتورَةَ عِندَ البَوّابَة (‏PUT …/cancel) «إن بقيت مفتوحة»؛ ثُمَّ يُخبَرُ المُشتَري (مَرَّةً لِكُلِّ صَفقَةٍ بِنَصِّ المُهلَة) وكُلُّ بائِعٍ
    /// بِالإشعارِ الواحِد — ولا يُخبَرُ صاحِبُ المَتجَرِ: الفاتورَةُ أُلغِيَت.</summary>
    [Fact]
    public async Task An_open_invoice_past_its_date_cancels_the_order_closes_the_invoice_and_tells_the_buyer_and_every_vendor()
    {
        var (w, verify) = await OpenedCart(TimeSpan.FromHours(24) + TimeSpan.FromSeconds(1));
        var invoice = PaidInvoice(w, "initiated");
        w.Moyasar.Then(HttpStatusCode.OK, invoice).Then(HttpStatusCode.OK, invoice).Then(HttpStatusCode.OK, invoice)
                 .Then(HttpStatusCode.OK, PaidInvoice(w, "canceled"));
        var l = new L(new HttpContextAccessor());

        var res = await verify.VerifyAsync(Slug, new DealPayer(Buyer), w.Ids[0].ToString("N"), l);
        output.WriteLine($"الرَمز: {res.Code} · أُلغِيَت: {res.Expired.Count} · البَوّابَة: [{string.Join(", ", Sent(w))}]");

        Assert.Equal(GatewayReturn.InvoiceExpired, res.Code);
        Assert.Equal(w.Ids.OrderBy(i => i), res.Expired.OrderBy(i => i));
        Assert.Equal(new[] { "POST /v1/invoices", $"PUT /v1/invoices/{InvoiceId}", $"GET /v1/invoices/{InvoiceId}",
            $"GET /v1/invoices/{InvoiceId}", $"GET /v1/invoices/{InvoiceId}", $"PUT /v1/invoices/{InvoiceId}{MoyasarClient.CancelSuffix}" }, Sent(w));
        foreach (var id in w.Ids)
        {
            var d = Load(w, id);
            Assert.Equal(DealStatus.Cancelled, d.Status);
            Assert.Equal(l[GatewayPayments.ExpiryReasonKey], d.CancelReason);
            Assert.True(DealEvent.IsByPlatform(Assert.Single(d.Timeline, e => e.Action == "cancelled")));
            Assert.DoesNotContain(d.Timeline, e => e.Action == DealEvent.GatewayInvoiceLeftOpen);
        }
        Assert.Empty(w.Platform.Calls);

        var told = await GatewayOwnerMoneyNoticeTests.Notices(w.Docs).NotifyPaymentExpiredAsync(Slug, res.Expired, l);
        var notes = w.Docs.Stored.OfType<Notification>().ToArray();
        foreach (var n in notes) output.WriteLine($"{(n.UserId == Buyer ? "المُشتَري" : n.UserId == Seller ? "البائِع" : n.UserId.ToString())}: «{n.Title}» · «{n.Body}»");
        Assert.Equal(2, told);
        Assert.Equal(2, notes.Count(n => n.UserId == Buyer && n.Title == l["notifications.deal_payment_expired.title"]));
        Assert.All(notes.Where(n => n.UserId == Buyer), n => Assert.Contains("24", n.Body, StringComparison.Ordinal));
        Assert.Equal(2, notes.Count(n => n.UserId == Seller && n.Title == l["notifications.deal_cancelled_for_vendor.title"]));
        Assert.Equal(4, notes.Length);
    }

    /// <summary><b>السالِب</b>: قَبلَ تاريخِها بِثانِيَة — «لَم يُدفَع» كَما كان، والصَفقَتانِ قائِمَتان، ولا نِداءَ إلغاء؛ والإشعارُ لِقائِمَةٍ لا يُرسَلُ شَيئاً.</summary>
    [Fact]
    public async Task An_open_invoice_before_its_date_stays_unpaid_and_nothing_is_cancelled()
    {
        var (w, verify) = await OpenedCart(TimeSpan.FromHours(24) - TimeSpan.FromSeconds(1));
        w.Moyasar.Then(HttpStatusCode.OK, PaidInvoice(w, "initiated"));

        var res = await verify.VerifyAsync(Slug, new DealPayer(Buyer), w.Ids[0].ToString("N"));

        Assert.Equal(GatewayPaymentProof.InvoiceUnpaid, res.Code);
        Assert.Empty(res.Expired);
        Assert.All(w.Ids, id => Assert.Equal(DealStatus.Active, Load(w, id).Status));
        Assert.Equal(3, w.Moyasar.Requests.Count);
        Assert.Equal(0, await GatewayOwnerMoneyNoticeTests.Notices(w.Docs).NotifyPaymentExpiredAsync(Slug, w.Ids, new L(new HttpContextAccessor())));
        Assert.Empty(w.Docs.Stored.OfType<Notification>());
    }

    // ═══ ٣) البابُ — نِداءُ الإشعارِ بَعدَ التَحَقُّق، والنُصوصُ حَيَّة ═════════════════════════════════════════════

    /// <summary>جِسمُ <c>POST /{slug}/payments/verify</c> (بِلا تَعليقات) يُنادي <c>NotifyPaymentExpiredAsync</c> بِما أَلغاه التَحَقُّقُ بَعدَ إشعارِ الدَفع — فَلا إلغاءَ
    /// بِلا خَبَر؛ ونُصوصُ الإشعارِ الثَلاثَةُ والسَبَبُ والشَريطُ عَرَبِيَّةٌ حَيَّة.</summary>
    [Fact]
    public void The_verify_door_tells_the_parties_of_what_the_verify_cancelled_and_every_text_is_live()
    {
        var file = WriteEndpointGuardTests.StripComments(File.ReadAllText(Path.Combine(RepoRoot(), "libs", "templates",
            "ACommerce.Templates.Customer.Marketplace", "Billing", "TenantGatewayEndpoints.cs")));
        var at = file.IndexOf("app.MapPost(\"/{slug}/payments/verify\"", StringComparison.Ordinal);
        Assert.True(at >= 0, "أَداةٌ عَمياء: لا نُقطَةَ تَحَقُّق.");
        var body = file[at..file.IndexOf(".DisableAntiforgery();", at, StringComparison.Ordinal)];
        var paid = body.IndexOf("await notices.NotifyPaidAsync(slug, res.Confirmed, l);", StringComparison.Ordinal);
        var expired = body.IndexOf("await notices.NotifyPaymentExpiredAsync(slug, res.Expired, l);", StringComparison.Ordinal);
        Assert.True(paid > 0 && expired > paid, $"الدَفع {paid} ← المُهلَة {expired}");

        var keys = new[] { "notifications.deal_payment_expired.title", "notifications.deal_payment_expired.body",
            "notifications.deal_payment_expired.push_body", GatewayPayments.ExpiryReasonKey, GatewayReturn.KeyFor(GatewayReturn.InvoiceExpired)! };
        foreach (var key in keys)
        {
            var text = LocaleCatalog.Text(LocaleCatalog.Arabic, key);
            Assert.NotEqual(key, text);
            Assert.False(LocaleCatalog.IsPlaceholder(text), key);
        }
        output.WriteLine($"نُصوصٌ حَيَّة: {keys.Length}");
    }

    // ═══ ٤) تَوصيفُ «اليَوم» — لا شَيءَ يُجَدوِلُ الإلغاء (مُراجَعَةُ تَيّارِ S3 الأولى، ‏2026-10-29) ══════════════════════════════════

    /// <summary>
    /// <para><b>تَوصيفُ «اليَوم» (القاعِدَة ٣) — فَتحُ الفاتورَةِ لا يُجَدوِلُ شَيئاً، وطَلَبٌ جاوَزَ مُهلَتَه يَبقى قائِماً ما لَم يَعُد مُشتَريه</b> (قَرارُ المالِك د5 (٤) «ب»:
    /// «يُلغى الطلب كله آليًّا»): الإلغاءُ في <c>VerifyAsync</c> وَحدَها ولا يَبلُغُها إلّا زِرُّ المُشتَري؛ فَبَعدَ ‏25 ساعَةً بِلا تَحَقُّقٍ الصَفقَتانِ قائِمَتان، والبَوّابَةُ لَم تُسأَل
    /// بَعدَ الفَتح، وصِفرُ <c>ScheduleAsync(</c> وصِفرُ <c>IMessageBus</c> في المَكتَبات. يَحمَرُّ يَومَ يُجَدوَلُ الإلغاءُ عِندَ الفَتح.</para>
    /// </summary>
    [Fact]
    public async Task Today_opening_an_invoice_schedules_nothing_and_an_order_past_its_date_stays_active_until_the_buyer_verifies()
    {
        var (w, _) = await OpenedCart(TimeSpan.FromHours(25));

        Assert.All(w.Ids, id => Assert.Equal(DealStatus.Active, Load(w, id).Status));
        Assert.Equal(new[] { "POST /v1/invoices", $"PUT /v1/invoices/{InvoiceId}" }, Sent(w));

        var files = Directory.EnumerateFiles(Path.Combine(RepoRoot(), "libs"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .Select(f => WriteEndpointGuardTests.StripComments(File.ReadAllText(f)))
            .ToArray();
        int Count(string needle) => files.Sum(f => f.Split(needle).Length - 1);
        var (scheduled, buses, messages) = (Count("ScheduleAsync("), Count("IMessageBus"), Count("GatewayInvoiceExpiryDue"));
        output.WriteLine($"مِلَفّاتٌ مَفحوصَة: {files.Length} · ScheduleAsync: {scheduled} · IMessageBus: {buses} · GatewayInvoiceExpiryDue: {messages}");
        Assert.True(files.Length >= 200, $"أَداةٌ عَمياء: {files.Length} مِلَفّاً.");
        Assert.Equal((0, 0, 0), (scheduled, buses, messages));
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "PlatformV1.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("لا جَذرَ لِلمُستَودَع");
    }
}
