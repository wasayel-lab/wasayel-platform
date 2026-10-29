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
//
// (‏2026-10-29، مُراجَعَةُ تَيّارِ S3 الأولى — ولا يُعادُ حَرفٌ فَوقَ ما سَبَق: «وكَسولٌ لا مُجَدوَل» لَم يَعُد — فَتحُ الفاتورَةِ يُجَدوِلُ رِسالَةً مُستَديمَةً في Wolverine
// المُرَكَّبِ لِتاريخِ انتِهائِها، ومُعالِجُها يُسَوّي كَما يُسَوّي التَحَقُّق (§٤ أَدناه؛ ‏ADR-046)؛ والبُرهانُ الحَيُّ `LiveScheduledExpiryProofTests`. وتَوصيفُه `17a9a19`.)

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
    /// <remarks>و<paramref name="bus"/> و<paramref name="log"/> (‏2026-10-29، مُراجَعَةُ تَيّارِ S3 الأولى): حافِلَةُ الرَسائِلِ ولوغُ بَوّابَةِ الفَتحِ — والافتِراضُ بِلا حافِلَةٍ
    /// فَلا يَتَحَرَّكُ صَفٌّ قائِم.</remarks>
    private static async Task<(World W, GatewayPayments Verify)> OpenedCart(TimeSpan verifyAfter,
        Wolverine.IMessageBus? bus = null, GatewayCapturingLogger? log = null)
    {
        ProviderVault? sealedWith = null;
        var w = await PaidCart(v => { sealedWith = v; return PaymentsBinding(v); }, withSeller: true, invoice: false);
        var client = new MoyasarClient(new HttpClient(w.Moyasar), NullLogger<MoyasarClient>.Instance);
        var deals = new DealsService(w.Docs.Store, w.Platform,
            new GatewayInvoiceCloser(new TenantProviderService(w.Docs.Store), sealedWith!, client));
        GatewayPayments At(DateTimeOffset now) => new(deals, new TenantProviderService(w.Docs.Store), sealedWith!, client, log, clock: new FixedClock(now), bus: bus);

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

    // ═══ ٤) الإلغاءُ مُجَدوَل — رِسالَةٌ واحِدَةٌ لِتاريخِ الفاتورَة، ومُعالِجٌ يُسَوّي كَما يُسَوّي التَحَقُّق (مُراجَعَةُ تَيّارِ S3 الأولى، ‏2026-10-29؛ ‏ADR-046) ══════

    private static string[] LibSources() => Directory.EnumerateFiles(Path.Combine(RepoRoot(), "libs"), "*.cs", SearchOption.AllDirectories)
        .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
        .ToArray();

    /// <summary>
    /// <para><b>فَتحُ الفاتورَةِ يُجَدوِلُ رِسالَةَ مُهلَتِها مَرَّةً لِتاريخِها المَكتوبِ نَفسِه</b> (قَرارُ المالِك د5 (٤) «ب»: «يُلغى الطلب كله آليًّا»؛ <b>قُلِبَ بِاسمِه</b> — كانَ
    /// <c>Today_opening_an_invoice_schedules_nothing_and_an_order_past_its_date_stays_active_until_the_buyer_verifies</c> (<c>17a9a19</c>): صِفرُ <c>ScheduleAsync(</c>
    /// وصِفرُ <c>IMessageBus</c> في ‏308 مِلَفّات، والطَلَبُ قائِمٌ بَعدَ ‏25 ساعَةً ما لَم يَعُد المُشتَري). اليَوم: رِسالَةٌ واحِدَةٌ بِالمَتجَرِ والصَفقَةِ المَرجِعِ ووَقتُها
    /// <see cref="GatewayPayments.ExpiresAtOf"/> بِالثانِيَة — وفي المَكتَباتِ نِداءُ جَدوَلَةٍ واحِدٌ في مِلَفٍّ واحِد. <b>وبِلا حافِلَةٍ</b> لا رِسالَةَ والطَلَبُ قائِمٌ كَما كان.</para>
    /// </summary>
    [Fact]
    public async Task Opening_an_invoice_schedules_one_expiry_message_for_its_written_date_and_nothing_without_a_bus()
    {
        var bus = new RecordingMessageBus();
        var (w, _) = await OpenedCart(TimeSpan.FromHours(25), bus);

        var sent = Assert.Single(bus.Sent);
        Assert.Equal(new GatewayInvoiceExpiryDue(Slug, w.Ids[0]), sent.Message);
        Assert.Equal(Opened + GatewayPayments.InvoiceLifetime, sent.Options!.ScheduledTime);
        Assert.All(w.Ids, id => Assert.Equal(sent.Options.ScheduledTime, GatewayPayments.ExpiresAtOf(Load(w, id))));
        Assert.Equal(new[] { "POST /v1/invoices", $"PUT /v1/invoices/{InvoiceId}" }, Sent(w));

        var (silent, _) = await OpenedCart(TimeSpan.FromHours(25));
        Assert.All(silent.Ids, id => Assert.Equal(DealStatus.Active, Load(silent, id).Status));

        var files = LibSources();
        var stripped = files.Select(f => (Name: Path.GetFileName(f), Code: WriteEndpointGuardTests.StripComments(File.ReadAllText(f)))).ToArray();
        var scheduling = stripped.Where(f => f.Code.Contains("ScheduleAsync(", StringComparison.Ordinal)).Select(f => f.Name).ToArray();
        var calls = stripped.Sum(f => f.Code.Split("ScheduleAsync(").Length - 1);
        output.WriteLine($"مِلَفّاتٌ مَفحوصَة: {files.Length} · نِداءاتُ الجَدوَلَة: {calls} · في: {string.Join(", ", scheduling)} · وَقتُ الرِسالَة: {sent.Options.ScheduledTime:O}");
        Assert.True(files.Length >= 200, $"أَداةٌ عَمياء: {files.Length} مِلَفّاً.");
        Assert.Equal(1, calls);
        Assert.Equal(new[] { "GatewayPayments.cs" }, scheduling);
    }

    /// <summary><b>تَعَذُّرُ الجَدوَلَةِ لا يُسقِطُ الشِراء</b>: الحافِلَةُ تَرمي ← الفاتورَةُ مَفتوحَةٌ ورابِطُها يَعود، والمَراجِعُ مَكتوبَة، وسَطرُ خَطَأٍ يُسَمّي الصَفقَةَ —
    /// وزِرُّ «تَحَقَّق» يُلغي بَعدَ المُهلَةِ كَما كان.</summary>
    [Fact]
    public async Task A_bus_that_fails_to_schedule_keeps_the_open_invoice_and_logs_the_deal()
    {
        var log = new GatewayCapturingLogger();
        var bus = new RecordingMessageBus { Throws = new InvalidOperationException("لا قاعِدَة") };
        var (w, _) = await OpenedCart(TimeSpan.FromHours(25), bus, log);

        Assert.Empty(bus.Sent);
        Assert.All(w.Ids, id => Assert.True(GatewayPayments.HasInvoice(Load(w, id))));
        Assert.All(w.Ids, id => Assert.NotNull(GatewayPayments.ExpiresAtOf(Load(w, id))));
        var line = Assert.Single(log.Lines, l => l.Contains("expiry not scheduled", StringComparison.Ordinal));
        Assert.Contains(w.Ids[0].ToString(), line, StringComparison.Ordinal);
        Assert.Contains(log.Lines, l => l.Contains("← opened", StringComparison.Ordinal));
    }

    /// <summary><b>المُوجِبُ كامِلاً — المُعالِجُ بِلا مُشتَري</b>: ‏24 ساعَةً وثانِيَةٌ والفاتورَةُ «initiated» — <see cref="GatewayInvoiceExpiryHandler.Handle"/> يُلغي الصَفقَتَينِ بِاسمِ
    /// المَنصَّة، وإلغاءُ الأَخيرَةِ يُلغي الفاتورَةَ عِندَ البَوّابَة، ويُخبَرُ المُشتَري مَرَّةً لِكُلِّ صَفقَةٍ وكُلُّ بائِعٍ بِالإشعارِ الواحِد — التَسلسُلُ نَفسُه الَّذي يَسلُكُه
    /// التَحَقُّقُ في <c>An_open_invoice_past_its_date_cancels_the_order_closes_the_invoice_and_tells_the_buyer_and_every_vendor</c>. والرِسالَةُ الثانِيَةُ لا تَكتُبُ ولا تُخبِر.</summary>
    [Fact]
    public async Task The_scheduled_expiry_cancels_the_unpaid_order_closes_the_invoice_and_tells_the_buyer_and_every_vendor_once()
    {
        var (w, gateway) = await OpenedCart(TimeSpan.FromHours(24) + TimeSpan.FromSeconds(1));
        var invoice = PaidInvoice(w, "initiated");
        w.Moyasar.Then(HttpStatusCode.OK, invoice).Then(HttpStatusCode.OK, invoice).Then(HttpStatusCode.OK, invoice)
                 .Then(HttpStatusCode.OK, PaidInvoice(w, "canceled"))
                 .Then(HttpStatusCode.OK, PaidInvoice(w, "canceled"));
        var l = new L(new HttpContextAccessor());
        var notices = GatewayOwnerMoneyNoticeTests.Notices(w.Docs);

        await GatewayInvoiceExpiryHandler.Handle(new GatewayInvoiceExpiryDue(Slug, w.Ids[0]), gateway, notices, l, CancellationToken.None);
        var notes = w.Docs.Stored.OfType<Notification>().ToArray();
        output.WriteLine($"البَوّابَة: [{string.Join(", ", Sent(w))}] · إشعارات: {notes.Length}");

        Assert.Equal(new[] { "POST /v1/invoices", $"PUT /v1/invoices/{InvoiceId}", $"GET /v1/invoices/{InvoiceId}",
            $"GET /v1/invoices/{InvoiceId}", $"GET /v1/invoices/{InvoiceId}", $"PUT /v1/invoices/{InvoiceId}{MoyasarClient.CancelSuffix}" }, Sent(w));
        foreach (var id in w.Ids)
        {
            var d = Load(w, id);
            Assert.Equal(DealStatus.Cancelled, d.Status);
            Assert.Equal(l[GatewayPayments.ExpiryReasonKey], d.CancelReason);
            Assert.True(DealEvent.IsByPlatform(Assert.Single(d.Timeline, e => e.Action == "cancelled")));
        }
        Assert.Equal(2, notes.Count(n => n.UserId == Buyer && n.Title == l["notifications.deal_payment_expired.title"]));
        Assert.Equal(2, notes.Count(n => n.UserId == Seller && n.Title == l["notifications.deal_cancelled_for_vendor.title"]));
        Assert.Equal(4, notes.Length);
        Assert.Empty(w.Platform.Calls);

        // والرِسالَةُ ثانِيَةً (أَعادَها الوَكيلُ أَو سَبَقَها التَحَقُّق): الفاتورَةُ «canceled» — لا إلغاءَ ولا إشعار.
        await GatewayInvoiceExpiryHandler.Handle(new GatewayInvoiceExpiryDue(Slug, w.Ids[0]), gateway, notices, l, CancellationToken.None);
        Assert.Equal(4, w.Docs.Stored.OfType<Notification>().Count());
        Assert.All(w.Ids, id => Assert.Single(Load(w, id).Timeline, e => e.Action == "cancelled"));
    }

    /// <summary><b>السالِب</b>: فاتورَةٌ دُفِعَت ولَم يُكتَب دَفعُها (الإشعارُ لَم يَصِل والمُشتَري لَم يَعُد) — يُكتَبُ الدَفعُ بِالكاتِبِ نَفسِه ويُخبَرُ البائِع، ولا يُلغى شَيء؛
    /// ثُمَّ رِسالَةٌ ثانِيَةٌ عَلى المَدفوعِ المَكتوبِ لا تَكتُبُ حَرفاً ولا تُخبِر.</summary>
    [Fact]
    public async Task The_scheduled_expiry_records_a_paid_invoice_once_and_cancels_nothing()
    {
        var (w, gateway) = await OpenedCart(TimeSpan.FromHours(24) + TimeSpan.FromSeconds(1));
        w.Moyasar.Then(HttpStatusCode.OK, PaidInvoice(w)).Then(HttpStatusCode.OK, PaidInvoice(w));
        var l = new L(new HttpContextAccessor());
        var notices = GatewayOwnerMoneyNoticeTests.Notices(w.Docs);

        var first = await gateway.ExpireAsync(Slug, w.Ids[0], l);
        await notices.NotifyPaidAsync(Slug, first.Confirmed, l);
        await notices.NotifyPaymentExpiredAsync(Slug, first.Expired, l);
        var told = w.Docs.Stored.OfType<Notification>().Count();
        var again = await gateway.ExpireAsync(Slug, w.Ids[0], l);
        output.WriteLine($"الأَوَّل: ok={first.Ok} كُتِب={first.Confirmed.Count} أُلغي={first.Expired.Count} · الثاني: كُتِب={again.Confirmed.Count} أُلغي={again.Expired.Count} · إشعارات: {told}");

        Assert.True(first.Ok);
        Assert.Equal(w.Ids.OrderBy(i => i), first.Confirmed.OrderBy(i => i));
        Assert.Empty(first.Expired);
        Assert.All(w.Ids, id => Assert.Equal(DealStatus.Active, Load(w, id).Status));
        Assert.All(w.Ids, id => Assert.True(GatewayReturn.IsConfirmed(Load(w, id))));
        Assert.True(told > 0);
        Assert.Empty(again.Confirmed);
        Assert.Empty(again.Expired);
        Assert.Equal(told, w.Docs.Stored.OfType<Notification>().Count());
    }

    /// <summary><b>السالِبُ لِكُلِّ حالٍ لا تُلغي</b>: قَبلَ التاريخِ بِثانِيَةٍ «لَم يُدفَع»، و«on_hold» بَعدَه — لا إلغاءَ ولا كِتابَةَ إلّا ما يَكتُبُه التَحَقُّقُ نَفسُه؛ وصَفقَةٌ
    /// غائِبَةٌ أَو بِلا فاتورَةٍ أَو مَتجَرٌ بِلا مِفتاحٍ أَو بَوّابَةٌ تَرُدُّ — رَمزُ كُلٍّ بِلا إلغاء، والثَلاثَةُ الأُولى بِلا نِداء.</summary>
    [Theory]
    [InlineData("before_date")]
    [InlineData("on_hold")]
    [InlineData("deal_not_found")]
    [InlineData("invoice_missing")]
    [InlineData("no_key")]
    [InlineData("gateway_refused")]
    public async Task The_scheduled_expiry_cancels_nothing_and_answers_its_code_for_every_other_case(string kind)
    {
        var after = kind == "before_date" ? TimeSpan.FromHours(24) - TimeSpan.FromSeconds(1) : TimeSpan.FromHours(25);
        var (w, gateway) = await OpenedCart(after);
        var callsBefore = w.Moyasar.Requests.Count;
        var target = w.Ids[0];
        string expected;
        switch (kind)
        {
            case "before_date": w.Moyasar.Then(HttpStatusCode.OK, PaidInvoice(w, "initiated")); expected = GatewayPaymentProof.InvoiceUnpaid; break;
            case "on_hold":     w.Moyasar.Then(HttpStatusCode.OK, PaidInvoice(w, "on_hold"));   expected = GatewayPaymentProof.InvoiceUnpaid; break;
            case "deal_not_found": target = Guid.NewGuid(); expected = DealPayAuthorization.DealNotFound; break;
            case "invoice_missing":
                var bare = await w.Deals.StartAsync(Slug, "marketplace", Buyer, "مُشتَرٍ", null, "سِلعَة", 10m, new());
                target = bare.Id; expected = DealPayAuthorization.InvoiceMissing; break;
            case "no_key":
                Assert.True(w.Docs.Docs.Remove(DocWorld.KeyOf(typeof(TenantProviderBinding), ProviderCapabilities.Payments))); expected = MoyasarFailures.NotConfigured; break;
            case "gateway_refused":
                w.Moyasar.Then(HttpStatusCode.Unauthorized, "{\"type\":\"authentication_error\"}"); expected = MoyasarFailures.Refused; break;
            default: throw new ArgumentOutOfRangeException(nameof(kind));
        }

        var res = await gateway.ExpireAsync(Slug, target);
        output.WriteLine($"{kind}: {res.Code} · نِداءاتٌ بَعدَ الفَتح: {w.Moyasar.Requests.Count - callsBefore}");

        Assert.Equal(expected, res.Code);
        Assert.Empty(res.Expired);
        Assert.Empty(res.Confirmed);
        Assert.All(w.Ids, id => Assert.Equal(DealStatus.Active, Load(w, id).Status));
        if (kind is "deal_not_found" or "invoice_missing" or "no_key")
            Assert.Equal(callsBefore, w.Moyasar.Requests.Count);
    }

    /// <summary><b>التَركيبُ في مَوضِعٍ واحِد</b>: <c>AddCustomerMarketplaceTemplate</c> يُنادي <c>ConfigureWolverine</c> بِـ<see cref="GatewayInvoiceExpiry.Configure"/> مَرَّةً، والدالَّةُ
    /// تَكتَشِفُ المُعالِجَ بِنَوعِه (لا مَكتَبَةَ القالَبِ كُلَّها) وتَجعَلُ طابورَ الرِسالَةِ مُستَديماً؛ والمُعالِجُ يَسأَلُ <see cref="GatewayPayments.ExpireAsync"/> ثُمَّ
    /// الإشعارَينِ بِتَرتيبِ نُقطَةِ التَحَقُّق. والبُرهانُ عَلى القاعِدَةِ (الرِسالَةُ تُحفَظُ مُجَدوَلَةً وتَعبُرُ الإقلاعَ وتُنَفَّذ) حَيٌّ: <c>LiveScheduledExpiryProofTests</c>.</summary>
    [Fact]
    public void The_template_configures_the_expiry_handler_by_type_and_a_durable_queue_once()
    {
        var root = Path.Combine(RepoRoot(), "libs", "templates", "ACommerce.Templates.Customer.Marketplace");
        var template = WriteEndpointGuardTests.StripComments(File.ReadAllText(Path.Combine(root, "MarketplaceTemplateExtensions.cs")));
        Assert.Equal(1, template.Split("Wolverine.HostBuilderExtensions.ConfigureWolverine(services, Services.Deals.GatewayInvoiceExpiry.Configure);").Length - 1);
        Assert.Equal(1, template.Split("ConfigureWolverine(").Length - 1);

        var expiry = WriteEndpointGuardTests.StripComments(File.ReadAllText(Path.Combine(root, "Services", "Deals", "GatewayInvoiceExpiry.cs")));
        Assert.Contains("opts.Discovery.IncludeType(typeof(GatewayInvoiceExpiryHandler));", expiry, StringComparison.Ordinal);
        Assert.Contains("opts.LocalQueueFor<GatewayInvoiceExpiryDue>().UseDurableInbox();", expiry, StringComparison.Ordinal);
        Assert.Contains("opts.CodeGeneration.AlwaysUseServiceLocationFor<ACommerce.Kit.Payments.MoyasarClient>();", expiry, StringComparison.Ordinal);
        Assert.DoesNotContain("IncludeAssembly", expiry, StringComparison.Ordinal);
        var ask = expiry.IndexOf("gateway.ExpireAsync(due.TenantSlug, due.DealId, l, ct)", StringComparison.Ordinal);
        var paid = expiry.IndexOf("notices.NotifyPaidAsync(due.TenantSlug, res.Confirmed, l, ct)", StringComparison.Ordinal);
        var expired = expiry.IndexOf("notices.NotifyPaymentExpiredAsync(due.TenantSlug, res.Expired, l, ct)", StringComparison.Ordinal);
        Assert.True(ask > 0 && paid > ask && expired > paid, $"تَسوِيَة {ask} ← دَفع {paid} ← مُهلَة {expired}");

        var opts = new Wolverine.WolverineOptions();
        GatewayInvoiceExpiry.Configure(opts);
    }

    // ═══ ٥) بَعدَ المُهلَةِ لا بابَ إلى فاتورَةٍ لا تُدفَع (مُراجَعَةُ تَيّارِ S3 الأولى، ‏2026-10-29؛ القاعِدَة ١٢) ═══════════════════════════

    private const string HostedUrl = "https://checkout.moyasar.com/invoices/efebc231-7795-4cfd-b69c-980fe4c02c49";

    /// <summary>صَفقَةٌ قائِمَةٌ عَلَيها فاتورَةُ بَوّابَةٍ ورابِطُها وتاريخُ انتِهائِها كَما يَكتُبُه الفَتح — <see cref="Opened"/> وأَربَعٌ وعِشرونَ ساعَة.</summary>
    private static Deal OpenDealExpiring()
    {
        var d = new Deal { Id = Guid.NewGuid(), Pattern = "marketplace", Status = DealStatus.Active, InitiatorId = Buyer, AmountSar = 150m };
        d.Refs[GatewayPaymentProof.InvoiceRef] = InvoiceId;
        d.Refs[GatewayPayments.InvoiceUrlRef] = HostedUrl;
        d.Refs[GatewayPayments.InvoiceExpiresAtRef] = MoyasarClient.ExpiryText(Opened + GatewayPayments.InvoiceLifetime);
        return d;
    }

    private static string PageSource(string name) => WriteEndpointGuardTests.StripComments(WriteEndpointGuardTests.StripMarkupComments(File.ReadAllText(
        Path.Combine(RepoRoot(), "libs", "templates", "ACommerce.Templates.Customer.Marketplace", "Components", "Pages", name))));

    /// <summary>
    /// <para><b>بَعدَ انتِهاءِ الفاتورَةِ لا رابِطَ إلَيها ولا «أَكمِلِ الدَّفع»، والصَفحَتانِ تَقولانِ ذلك</b> (القاعِدَة ١٢؛ <b>قُلِبَ بِاسمِه</b> — كانَ
    /// <c>Today_a_deal_past_its_written_expiry_still_draws_the_hosted_link_and_the_complete_payment_door</c> (<c>9f003b1</c>): الرابِطُ يَعودُ كَما هُوَ، وصِفرُ قِراءَةٍ
    /// لِتاريخِ الانتِهاءِ في الصَفحَتَين). اليَوم: <see cref="GatewayReturn.PayUrlOf(Deal, DateTimeOffset)"/> بِالسّاعَة؛ وصَفحَةُ العَودَةِ تَرسُمُ سَطرَ المُهلَةِ وزِرَّ «تَحَقَّق»
    /// بِلا رابِط، وصَفحَةُ الطَلَبِ سَطرَها وبابَ «تَحَقَّق» مَكانَ «أَكمِلِ الدَّفع». والنَصّانِ حَيّان.</para>
    /// </summary>
    [Fact]
    public void A_deal_past_its_written_expiry_draws_no_hosted_link_and_both_pages_say_the_window_closed()
    {
        var deal = OpenDealExpiring();
        var at = Opened + GatewayPayments.InvoiceLifetime;
        Assert.Null(GatewayReturn.PayUrlOf(deal, at));
        Assert.Null(GatewayReturn.PayUrlOf(deal, at.AddHours(1)));
        Assert.Equal(HostedUrl, GatewayReturn.PayUrlOf(deal, at.AddSeconds(-1)));
        Assert.Equal(HostedUrl, GatewayReturn.PayUrlOf(deal));   // قَواعِدُ الحالِ بِلا ساعَة — كَما كانَت

        var back = PageSource("GatewayPayReturn.razor");
        var detail = PageSource("MyDealDetail.razor");
        Assert.Contains("payUrl = GatewayReturn.PayUrlOf(deal, now);", back, StringComparison.Ordinal);
        Assert.Contains("windowClosed = deal.Status == DealStatus.Active && !confirmed && GatewayReturn.WindowClosed(deal, now);", back, StringComparison.Ordinal);
        Assert.Contains("<AcAlert Variant=\"warning\" Message=\"@L[\"pay.return.window_closed\"]\" />", back, StringComparison.Ordinal);
        Assert.Contains("paymentWindowClosed = awaitsPayment && GatewayReturn.WindowClosed(deal, DateTimeOffset.UtcNow);", detail, StringComparison.Ordinal);
        var invoice = detail.IndexOf("@if (GatewayPayments.HasInvoice(deal))", StringComparison.Ordinal);
        var closed = detail.IndexOf("@if (paymentWindowClosed)", invoice, StringComparison.Ordinal);
        var open = detail.IndexOf("else", closed, StringComparison.Ordinal);
        var door = detail.IndexOf("@L.Markup(\"deals.detail.complete_payment\")", StringComparison.Ordinal);
        Assert.True(invoice > 0 && closed > invoice && open > closed && door > open, $"فاتورَة {invoice} ← مُغلَقَة {closed} ← مَفتوحَة {open} ← «أَكمِل» {door}");
        Assert.Contains("@L[\"deals.detail.payment_window_closed\"]", detail[closed..open], StringComparison.Ordinal);
        Assert.DoesNotContain("deals.detail.complete_payment", detail[closed..open], StringComparison.Ordinal);

        var texts = new[] { "pay.return.window_closed", "deals.detail.payment_window_closed" };
        foreach (var key in texts)
        {
            var text = LocaleCatalog.Text(LocaleCatalog.Arabic, key);
            Assert.NotEqual(key, text);
            Assert.False(LocaleCatalog.IsPlaceholder(text), key);
            Assert.Contains("مُهلَةُ دَفعِ", text, StringComparison.Ordinal);
            Assert.Contains(LocaleCatalog.Text(LocaleCatalog.Arabic, "pay.return.verify"), text, StringComparison.Ordinal);
        }
        output.WriteLine($"نُصوصٌ حَيَّة: {texts.Length}");
    }

    /// <summary><b>المُوجِبُ والسالِبُ لِلدالَّة</b>: تَصدُقُ عِندَ التاريخِ المَكتوبِ وبَعدَه، ولا تَصدُقُ قَبلَه بِثانِيَة، ولا لِصَفقَةٍ بِلا تاريخٍ (فُتِحَت قَبلَه) أَو بِتاريخٍ
    /// لا يُقرَأ — فَبابُ هذِه كَما كان.</summary>
    [Theory]
    [InlineData("2026-10-30T08:00:00.000Z", 0, true)]
    [InlineData("2026-10-30T08:00:00.000Z", 3600, true)]
    [InlineData("2026-10-30T08:00:00.000Z", -1, false)]
    [InlineData(null, 86400, false)]
    [InlineData("غَداً", 86400, false)]
    public void The_window_closes_at_the_written_date_and_never_for_a_deal_without_one(string? at, int secondsAfter, bool closed)
    {
        var deal = OpenDealExpiring();
        if (at is null) deal.Refs.Remove(GatewayPayments.InvoiceExpiresAtRef); else deal.Refs[GatewayPayments.InvoiceExpiresAtRef] = at;
        var now = Opened.AddHours(24).AddSeconds(secondsAfter);
        Assert.Equal(closed, GatewayReturn.WindowClosed(deal, now));
        Assert.Equal(closed ? null : HostedUrl, GatewayReturn.PayUrlOf(deal, now));
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "PlatformV1.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("لا جَذرَ لِلمُستَودَع");
    }
}

/// <summary>حافِلَةُ رَسائِلَ تُسَجِّلُ ما يُرسَلُ إلَيها ولا تُرسِل (‏2026-10-29، مُراجَعَةُ تَيّارِ S3 الأولى) — <c>ScheduleAsync</c> امتِدادٌ يَمُرُّ بِـ<c>PublishAsync</c>
/// أَو <c>SendAsync</c> بِـ<see cref="Wolverine.DeliveryOptions.ScheduledTime"/>، فَيُسَجَّلُ الاثنان؛ و<see cref="Throws"/> تَجعَلُها تَرمي. وما سِواهُما لا يُنادى هُنا.</summary>
internal sealed class RecordingMessageBus : Wolverine.IMessageBus
{
    public List<(object Message, Wolverine.DeliveryOptions? Options)> Sent { get; } = new();
    public Exception? Throws { get; init; }
    public string? TenantId { get; set; }

    private ValueTask Record(object? message, Wolverine.DeliveryOptions? options)
    {
        if (Throws is not null) throw Throws;
        Sent.Add((message!, options));
        return ValueTask.CompletedTask;
    }

    public ValueTask SendAsync<T>(T message, Wolverine.DeliveryOptions? options = null) => Record(message, options);
    public ValueTask PublishAsync<T>(T message, Wolverine.DeliveryOptions? options = null) => Record(message, options);
    public ValueTask BroadcastToTopicAsync(string topicName, object message, Wolverine.DeliveryOptions? options = null) => throw new NotSupportedException();
    public Wolverine.IDestinationEndpoint EndpointFor(string endpointName) => throw new NotSupportedException();
    public Wolverine.IDestinationEndpoint EndpointFor(Uri uri) => throw new NotSupportedException();
    public IReadOnlyList<Wolverine.Envelope> PreviewSubscriptions(object message) => throw new NotSupportedException();
    public IReadOnlyList<Wolverine.Envelope> PreviewSubscriptions(object message, Wolverine.DeliveryOptions options) => throw new NotSupportedException();
    public Task InvokeForTenantAsync(string tenantId, object message, CancellationToken cancellation = default, TimeSpan? timeout = null) => throw new NotSupportedException();
    public Task<T> InvokeForTenantAsync<T>(string tenantId, object message, CancellationToken cancellation = default, TimeSpan? timeout = null) => throw new NotSupportedException();
    public Task InvokeAsync(object message, CancellationToken cancellation = default, TimeSpan? timeout = null) => throw new NotSupportedException();
    public Task InvokeAsync(object message, Wolverine.DeliveryOptions options, CancellationToken cancellation = default, TimeSpan? timeout = null) => throw new NotSupportedException();
    public Task<T> InvokeAsync<T>(object message, CancellationToken cancellation = default, TimeSpan? timeout = null) => throw new NotSupportedException();
    public Task<T> InvokeAsync<T>(object message, Wolverine.DeliveryOptions options, CancellationToken cancellation = default, TimeSpan? timeout = null) => throw new NotSupportedException();
    public Task<TResponse> StreamAsync<TRequest, TResponse>(IAsyncEnumerable<TRequest> requests, CancellationToken cancellation = default, TimeSpan? timeout = null) => throw new NotSupportedException();
    public Task<TResponse> StreamAsync<TRequest, TResponse>(IAsyncEnumerable<TRequest> requests, Wolverine.DeliveryOptions options, CancellationToken cancellation = default, TimeSpan? timeout = null) => throw new NotSupportedException();
    public IAsyncEnumerable<TResponse> StreamAsync<TResponse>(object message, CancellationToken cancellation = default) => throw new NotSupportedException();
    public IAsyncEnumerable<TResponse> StreamAsync<TResponse>(object message, Wolverine.DeliveryOptions options, CancellationToken cancellation = default) => throw new NotSupportedException();
}
