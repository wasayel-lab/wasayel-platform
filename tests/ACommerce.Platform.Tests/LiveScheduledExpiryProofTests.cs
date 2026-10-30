using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using ACommerce.Kit.Notifications;
using ACommerce.Kit.Payments;
using ACommerce.Platform.Providers;
using ACommerce.Templates.Customer.Marketplace;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using Marten;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Wolverine;
using Wolverine.Marten;
using Xunit;
using Xunit.Abstractions;
using static ACommerce.Platform.Tests.TenantGatewayEndpointsTests;

namespace ACommerce.Platform.Tests;

// ═══ البُرهانُ الحَيُّ لِرِسالَةِ المُهلَة — تُحفَظُ مُجَدوَلَةً، وتَعبُرُ الإقلاع، وتُنَفَّذُ فَتُلغي (مُراجَعَةُ تَيّارِ S3 الأولى، ‏2026-10-29؛ ‏ADR-046) ═══════════
//
// **الدَعوى المَفحوصَة**: أَنَّ `GatewayInvoiceExpiry.Configure` — اكتِشافُ المُعالِجِ بِنَوعِه وصُندوقٌ مُستَديمٌ لِطابورِ الرِسالَة — يَجعَلُ رِسالَةً يُجَدوِلُها فَتحُ
// الفاتورَةِ **صَفّاً في القاعِدَة** (`platform.wolverine_incoming_envelopes` بِحالِ `Scheduled` ووَقتِ تَنفيذِها)، لا مُؤَقِّتاً في الذاكِرَة؛ فَمُضيفٌ يَقِفُ قَبلَ
// وَقتِها لا يُضَيِّعُها، ومُضيفٌ آخَرُ يُقلِعُ بَعدَه يُنَفِّذُها فَيُلغي الطَلَبَ كُلَّه بِاسمِ المَنصَّةِ ويُخبِر. **ولِماذا حَيّ**: الوَثيقَةُ تَصِفُ الصُندوقَ المُستَديم،
// وتَركيبُنا (Marten بِإيجارٍ مُقتَرِن، `IntegrateWithWolverine`، الطَورُ المُتَوازِنُ الافتِراضِيّ) لَم يُشَغِّل رِسالَةً واحِدَةً قَبلَ اليَوم — القاعِدَةُ ١٤: القُدرَةُ تُفحَصُ
// بِنِداءٍ حَقيقِيّ.
//
// **والتَركيبُ تَركيبُ المُضيفِ في ما يَخُصُّ الدَعوى**: `AddCustomerMarketplaceTemplate` نَفسُها (فَتَركيبُ Wolverine مِن سَطرِها لا نُسخَة)، وMarten كَما يُرَكِّبُه
// `HostingExtensions`. والبَوّابَةُ مُعالِجٌ مُزَيَّفٌ بِرُدودٍ مِن الوَثيقَة — لا مِفتاحَ حَقيقِيّ («ج» عَلى المالِك).
//
// **والساعَةُ مُزاحَةٌ لا مُنتَظَرَة**: الفَتحُ يَكتُبُ «الآنَ + ‏24 ساعَة»، فَتُسَجَّلُ ساعَةُ الخِدمَةِ مُتَأَخِّرَةً بِالمُدَّةِ إلّا ثَوانِيَ — فَيَحينُ وَقتُ الرِسالَةِ بَعدَ ثَوانٍ
// حَقيقِيَّة، والبَوّابَةُ تَقولُ «expired» فَيَصدُقُ `ExpiryDue` بِحالِها لا بِالساعَة. **ولا يَعمَلُ في التَشغيلِ العادِيّ**: `WASAYEL_LIVE_PROOF=1` و`ConnectionStrings__Postgres`.

[Collection(LiveProofCollection.Name)]
public class LiveScheduledExpiryProofTests(ITestOutputHelper output)
{
    private static bool Enabled
        => LiveDealConcurrencyProofTests.Enabled && !string.IsNullOrEmpty(LiveDealConcurrencyProofTests.ConnectionString);

    private static string ResilientConnection =>
        LiveDealConcurrencyProofTests.ConnectionString!.TrimEnd(';') + ";Timeout=60;Command Timeout=120";

    /// <summary>كَم ثانِيَةً بَعدَ الفَتحِ تَحينُ الرِسالَة — أَطوَلُ مِن إيقافِ المُضيفِ الأَوَّلِ المَقيس (ثانِيَتانِ أَو أَقَلّ)، وأَقصَرُ مِن صَبرِ الاختِبار.</summary>
    private static readonly TimeSpan DueAfter = TimeSpan.FromSeconds(15);

    /// <summary>ساعَةُ الخِدمَةِ مُزاحَة — «الآنَ» الحَقيقِيُّ ناقِصاً مُدَّةَ الفاتورَةِ وزائِداً <see cref="DueAfter"/>.</summary>
    private sealed class ShiftedClock(TimeSpan shift) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => base.GetUtcNow() + shift;
    }

    private static int FreePort()
    {
        using var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    /// <summary>المُضيفُ بِتَركيبِ القالَب. و<paramref name="shiftClock"/> لِلمُضيفِ الأَوَّلِ وَحدَه: الثاني بِلا <see cref="TimeProvider"/> مُسَجَّلٍ — كَالمُضيفِ الحَقيقِيّ —
    /// فَيُبنى كودُ المُعالِجِ بِتَسجيلاتِ الإنتاجِ نَفسِها (مَقيسٌ: سِياسَةُ <c>ServiceLocationPolicy.NotAllowed</c> تَحكُمُ ساعَةَ البِناء).</summary>
    private static WebApplication BuildApp(string kek, MoyasarScriptedHandler moyasar, bool shiftClock)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Configuration[ProviderVault.KekConfigKey] = kek;
        builder.Services.AddMarten(o =>
            {
                o.Connection(ResilientConnection);
                o.DatabaseSchemaName = "platform";
                o.Policies.AllDocumentsAreMultiTenanted();
                o.Events.TenancyStyle = global::JasperFx.MultiTenancy.TenancyStyle.Conjoined;
                o.Schema.For<ACommerce.Kit.Tenants.Tenant>().SingleTenanted().Identity(x => x.Id);
                o.AutoCreateSchemaObjects = JasperFx.AutoCreate.All;
            })
            .UseLightweightSessions()
            .IntegrateWithWolverine();
        builder.Host.UseWolverine(opts =>
        {
            opts.UseRuntimeCompilation();
            opts.Policies.AutoApplyTransactions();
        });
        builder.Services.AddSignalR();
        builder.Services.AddCustomerMarketplaceTemplate();
        builder.Services.AddSingleton<IPaymentProvider>(new DealMoneyClaimFirstTests.RecordingPaymentProvider());
        if (shiftClock) builder.Services.AddSingleton<TimeProvider>(new ShiftedClock(DueAfter - GatewayPayments.InvoiceLifetime));
        builder.Services.AddHttpClient<MoyasarClient>().ConfigurePrimaryHttpMessageHandler(() => moyasar);

        var app = builder.Build();
        app.Urls.Add($"http://127.0.0.1:{FreePort()}");
        return app;
    }

    /// <summary>صُفوفُ الصُندوقِ الَّتي تَحمِلُ رِسالَةَ مُهلَةٍ لِهذِه الصَفقَة — مُعَرِّفُها وحالُها ووَقتُ تَنفيذِها. والجِسمُ غِلافٌ ثُنائِيٌّ لا نَصّ (مَقيس: ‏`convert_from`
    /// يُرَدُّ بِـ22021)، فَيُبحَثُ فيه عَن بايتاتِ مُعَرِّفِ الصَفقَةِ هُنا لا في الاستِعلام.</summary>
    private static async Task<List<(Guid Id, string Status, DateTimeOffset? At)>> EnvelopesAsync(Guid dealId)
    {
        await using var conn = new NpgsqlConnection(ResilientConnection);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "select id, status, execution_time, body from platform.wolverine_incoming_envelopes where message_type like @type", conn);
        cmd.Parameters.AddWithValue("type", "%" + nameof(GatewayInvoiceExpiryDue) + "%");
        var needle = System.Text.Encoding.UTF8.GetBytes(dealId.ToString("D"));
        var rows = new List<(Guid, string, DateTimeOffset?)>();
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            var body = (byte[])r.GetValue(3);
            if (body.AsSpan().IndexOf(needle) < 0) continue;
            rows.Add((r.GetGuid(0), r.GetString(1), r.IsDBNull(2) ? null : new DateTimeOffset(DateTime.SpecifyKind(r.GetDateTime(2), DateTimeKind.Utc))));
        }
        return rows;
    }

    private static async Task DeleteEnvelopesAsync(Guid dealId)
    {
        var ids = (await EnvelopesAsync(dealId)).Select(e => e.Id).ToArray();
        await using var conn = new NpgsqlConnection(ResilientConnection);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("delete from platform.wolverine_incoming_envelopes where id = any(@ids)", conn);
        cmd.Parameters.AddWithValue("ids", ids);
        await cmd.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task A_scheduled_expiry_is_stored_survives_a_restart_and_cancels_the_order_as_the_platform()
    {
        if (!Enabled) return;

        var slug = "zz-exp-" + Guid.NewGuid().ToString("N")[..8];
        var (buyer, seller) = (Guid.NewGuid(), Guid.NewGuid());
        var kek = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var ids = new List<Guid>();
        try
        {
            // ── المُضيفُ الأَوَّل: يَفتَحُ الفاتورَةَ فَيُجَدوِلُ الرِسالَة، ثُمَّ يَقِفُ قَبلَ وَقتِها ─────────────────────
            var first = new MoyasarScriptedHandler()
                .Then(HttpStatusCode.Created, MoyasarClientTests.InvoiceJson("initiated", 15000, metadata: new Dictionary<string, string>()));
            var hostA = BuildApp(kek, first, shiftClock: true);
            await hostA.StartAsync();
            var storeA = hostA.Services.GetRequiredService<IDocumentStore>();
            var vault = hostA.Services.GetRequiredService<ProviderVault>();
            var binding = PaymentsBinding(vault, sealedFor: slug);
            binding.TenantSlug = slug;
            await using (var s = storeA.LightweightSession(slug)) { s.Store(binding); await s.SaveChangesAsync(); }

            DateTimeOffset expiresAt;
            using (var scope = hostA.Services.CreateScope())
            {
                var deals = scope.ServiceProvider.GetRequiredService<DealsService>();
                foreach (var amount in new[] { 100m, 50m })
                {
                    var born = await deals.StartAsync(slug, "marketplace", buyer, "مُشتَرٍ", null, "سِلعَة", amount, GatewayPayments.MarkedAtBirth(new()
                    {
                        [CheckoutPayMethods.AttributeKey] = CheckoutPayMethods.Card,
                    }, viaGateway: true));
                    await deals.AttachRefAsync(slug, born.Id, DealsService.ListingOwnerRef, seller.ToString());
                    await deals.BindListingOwnerAsync(slug, born.Id, seller.ToString());
                    ids.Add(born.Id);
                }
                first.Then(HttpStatusCode.OK, MoyasarClientTests.InvoiceJson("initiated", 15000, metadata: GatewayPaymentProof.MetadataFor(ids)));
                var gateway = scope.ServiceProvider.GetRequiredService<GatewayPayments>();
                var opened = await gateway.OpenInvoiceAsync(slug, new DealPayer(buyer), ids, "مَتجَر", "https://store.example/r",
                    scope.ServiceProvider.GetRequiredService<ACommerce.Platform.I18n.L>());
                Assert.True(opened.Ok, opened.Code);
                expiresAt = GatewayPayments.ExpiresAtOf((await deals.LoadAsync(slug, ids[0]))!)!.Value;
            }

            var scheduled = await EnvelopesAsync(ids[0]);
            var stopwatch = Stopwatch.StartNew();
            await hostA.StopAsync();
            await hostA.DisposeAsync();
            var stoppedIn = stopwatch.Elapsed;
            var afterStop = await EnvelopesAsync(ids[0]);
            output.WriteLine($"[A] الفاتورَةُ تَنتَهي {expiresAt:O} · صُفوفُ الصُندوق: [{string.Join(", ", scheduled.Select(e => $"{e.Status}@{e.At:O}"))}] · " +
                             $"وَقَفَ في {stoppedIn.TotalSeconds:0.0}ث · بَعدَ الإيقاف: [{string.Join(", ", afterStop.Select(e => e.Status))}]");

            var row = Assert.Single(scheduled);
            Assert.Equal("Scheduled", row.Status);
            Assert.NotNull(row.At);
            Assert.True(Math.Abs((row.At!.Value - expiresAt).TotalSeconds) < 1, $"وَقتُ الرِسالَة {row.At:O} لا يُطابِقُ انتِهاءَ الفاتورَة {expiresAt:O}");
            Assert.True(DateTimeOffset.UtcNow < expiresAt, "وَقَفَ المُضيفُ الأَوَّلُ بَعدَ وَقتِ الرِسالَة — البُرهانُ لا يَقيسُ العُبور.");
            Assert.Equal("Scheduled", Assert.Single(afterStop).Status);
            // ومَخزَنُ المُضيفِ الأَوَّلِ أُغلِقَ مَعَه — فَالقِراءَةُ بَينَ المُضيفَينِ بِمَخزَنٍ مُستَقِلّ.
            await using (var between = LiveDealConcurrencyProofTests.BuildStore())
            await using (var q = between.QuerySession(slug))
                foreach (var id in ids) Assert.Equal(DealStatus.Active, (await q.LoadAsync<Deal>(id))!.Status);

            // ── المُضيفُ الثاني: يُقلِعُ عَلى القاعِدَةِ نَفسِها فَيُنَفِّذُها حينَ تَحين ───────────────────────────────
            var expired = MoyasarClientTests.InvoiceJson("expired", 15000, metadata: GatewayPaymentProof.MetadataFor(ids));
            var second = new MoyasarScriptedHandler().Then(HttpStatusCode.OK, expired).Then(HttpStatusCode.OK, expired).Then(HttpStatusCode.OK, expired);
            await using var hostB = BuildApp(kek, second, shiftClock: false);
            await hostB.StartAsync();
            var store = hostB.Services.GetRequiredService<IDocumentStore>();

            // والانتِظارُ حَتّى يَفرُغَ المُعالِجُ كُلُّه — لا حَتّى تُلغى الصَفَقاتُ وَحدَها (‏2026-10-29، جَولَةُ الإصلاحِ الأولى لِتَيّارِ S3): المُعالِجُ يُلغي ثُمَّ
            // يُخبِر، فَالإلغاءُ يَسبِقُ الإشعارَ بِلَحظَة. مَقيسٌ عِندَ `6cf44f7` في سِتِّ جَولاتٍ مُتَتالِيَة: احمَرَّت اثنَتان، والَّتي قُرِئَ خَطَؤُها قَرَأَت الإشعاراتِ
            // والرِسالَةُ «Incoming» بَعد (إشعارٌ واحِدٌ مِن أَربَعَة). فَالعَلامَةُ حالُ الرِسالَةِ نَفسِها: لا «مُجَدوَلَة» ولا «واصِلَة» — أَي فَرَغَ مِنها الصُندوق.
            var deadline = Stopwatch.StartNew();
            List<Deal> now = new();
            List<(Guid Id, string Status, DateTimeOffset? At)> left = new();
            while (deadline.Elapsed < TimeSpan.FromSeconds(120))
            {
                now = new();
                await using (var q = store.QuerySession(slug))
                    foreach (var id in ids) now.Add((await q.LoadAsync<Deal>(id))!);
                left = await EnvelopesAsync(ids[0]);
                if (now.All(d => d.Status == DealStatus.Cancelled) && !left.Any(e => e.Status is "Scheduled" or "Incoming")) break;
                await Task.Delay(500);
            }
            var tookAfterDue = DateTimeOffset.UtcNow - expiresAt;
            List<Notification> notes;
            await using (var q = store.QuerySession(slug))
                notes = (await Marten.QueryableExtensions.ToListAsync(q.Query<Notification>())).ToList();
            await hostB.StopAsync();
            output.WriteLine($"[B] الحال: [{string.Join(", ", now.Select(d => d.Status))}] بَعدَ الوَقتِ بِـ{tookAfterDue.TotalSeconds:0.0}ث · " +
                             $"البَوّابَة: [{string.Join(", ", second.Requests.Select(r => $"{r.Method} {r.RequestUri!.AbsolutePath}"))}] · " +
                             $"صُفوفٌ باقِيَة: [{string.Join(", ", left.Select(e => e.Status))}] · إشعارات: {notes.Count} (المُشتَري {notes.Count(n => n.UserId == buyer)}، البائِع {notes.Count(n => n.UserId == seller)})");

            Assert.All(now, d => Assert.Equal(DealStatus.Cancelled, d.Status));
            Assert.All(now, d => Assert.True(DealEvent.IsByPlatform(Assert.Single(d.Timeline, e => e.Action == "cancelled"))));
            Assert.All(now, d => Assert.Equal(ACommerce.Platform.I18n.LocaleCatalog.Text(ACommerce.Platform.I18n.LocaleCatalog.Arabic, GatewayPayments.ExpiryReasonKey), d.CancelReason));
            Assert.Contains(second.Requests, r => r.Method == HttpMethod.Get && r.RequestUri!.AbsolutePath == $"/v1/invoices/{InvoiceId}");
            // والصَفُّ لَم يَعُد «مُجَدوَلاً»: نُفِّذَ — ويُبقيه الصُندوقُ «مُعالَجاً» مُدَّةً لِمَنعِ التَكرار، فَلا يُنَفَّذُ ثانِيَةً.
            Assert.DoesNotContain(left, e => e.Status == "Scheduled");
            Assert.Equal(2, notes.Count(n => n.UserId == buyer));
            Assert.Equal(2, notes.Count(n => n.UserId == seller));
        }
        finally
        {
            foreach (var id in ids) await DeleteEnvelopesAsync(id);
            await using var cleanup = LiveDealConcurrencyProofTests.BuildStore();
            await using var s = cleanup.LightweightSession(slug);
            foreach (var d in await Marten.QueryableExtensions.ToListAsync(s.Query<Deal>())) s.Delete(d);
            foreach (var b in await Marten.QueryableExtensions.ToListAsync(s.Query<TenantProviderBinding>())) s.Delete(b);
            foreach (var n in await Marten.QueryableExtensions.ToListAsync(s.Query<Notification>())) s.Delete(n);
            await s.SaveChangesAsync();
        }
    }

    /// <summary>صُفوفُ الرَسائِلِ الميتَةِ لِرِسالَةِ مُهلَةِ هذِه الصَفقَة — نَوعُ العَطَبِ وقابِلِيَّةُ الإعادَة (والجِسمُ يُبحَثُ فيه عَن المُعَرِّفِ هُنا، نَسَقُ <see cref="EnvelopesAsync"/>).</summary>
    private static async Task<List<(Guid Id, string? Exception, bool? Replayable)>> DeadLettersAsync(Guid dealId)
    {
        await using var conn = new NpgsqlConnection(ResilientConnection);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "select id, exception_type, replayable, body from platform.wolverine_dead_letters where message_type like @type", conn);
        cmd.Parameters.AddWithValue("type", "%" + nameof(GatewayInvoiceExpiryDue) + "%");
        var needle = System.Text.Encoding.UTF8.GetBytes(dealId.ToString("D"));
        var rows = new List<(Guid, string?, bool?)>();
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            if (((byte[])r.GetValue(3)).AsSpan().IndexOf(needle) < 0) continue;
            rows.Add((r.GetGuid(0), r.IsDBNull(1) ? null : r.GetString(1), r.IsDBNull(2) ? null : r.GetBoolean(2)));
        }
        return rows;
    }

    /// <summary>
    /// <para><b>مُعالِجٌ يَفشَلُ في كُلِّ مُحاوَلَة</b> (‏2026-10-30، تَدقيقُ الموجَة ١١ CO2 — المُرَحَّلُ [S3·١]): البَوّابَةُ تَرمي عَطَباً لا تَبتَلِعُه العَميل، فَيَرمي المُعالِج —
    /// <b>أَربَعُ مُحاوَلاتٍ</b> (الأولى وثَلاثُ إعاداتٍ بِتَأَنّي <see cref="GatewayInvoiceExpiry.RetryCooldowns"/>) ثُمَّ صَفٌّ في <c>platform.wolverine_dead_letters</c>، و<b>إشعارٌ واحِدٌ</b>
    /// لِصاحِبِ المَتجَرِ بِنَصِّ «طَلَبٌ انتَهَت مُهلَتُه لَم يُلغَ» ورابِطِ صَفقَتِه في الاستوديو، والصَفقَةُ قائِمَةٌ كَما كانَت. كانَ: نِداءٌ واحِدٌ ثُمَّ الصَفُّ بِلا كَلِمَة (‏ADR-046 §٤‑1).</para>
    /// </summary>
    [Fact]
    public async Task An_expiry_handler_failing_every_attempt_lands_in_the_dead_letters_with_one_owner_notice()
    {
        if (!Enabled) return;

        var attempts = 0;
        var failing = new MoyasarScriptedHandler();
        for (var i = 0; i < 8; i++)
            failing.ThenDoing(() => { Interlocked.Increment(ref attempts); throw new InvalidOperationException("عُطلٌ مَصنوعٌ في البَوّابَة"); }, HttpStatusCode.OK, "{}");
        await DeadLetterProofAsync("zz-expf-", failing, () => attempts, nameof(InvalidOperationException));
    }

    /// <summary>
    /// <para><b>بَوّابَةٌ تُجيبُ ‏503 أَو تَقطَعُ الاتِّصالَ في كُلِّ مُحاوَلَة</b> (‏2026-10-30، جَولَةُ الإصلاحِ الأولى لِلمَسار F3 — CO2 / [S3·١]): العَطَبُ الحَقيقِيُّ لا الاستِثناءُ
    /// المَصنوع — عَميلُ ميسر يَلتَقِطُ ‏5xx و<c>HttpRequestException</c> فَيُرجِعُ <c>gateway_unreachable</c> ولا يَرمي، فَكانَ المُعالِجُ يَفرُغُ ويُستَهلَكُ الصَفُّ بِلا كَلِمَة
    /// (<c>4bc251c</c>). اليَوم: <b>أَربَعُ مُحاوَلات</b> ثُمَّ صَفٌّ في <c>platform.wolverine_dead_letters</c> بِنَوعِ <see cref="GatewayInvoiceExpiryFetchFailed"/>، و<b>إشعارٌ واحِدٌ</b>
    /// لِصاحِبِ المَتجَر، والصَفقَةُ قائِمَة. الفَرديَّةُ ‏503 والزَوجِيَّةُ انقِطاع.</para>
    /// </summary>
    [Fact]
    public async Task A_gateway_answering_503_or_dropping_the_connection_at_every_attempt_lands_in_the_dead_letters_with_one_owner_notice()
    {
        if (!Enabled) return;

        var attempts = 0;
        var failing = new MoyasarScriptedHandler();
        for (var i = 0; i < 8; i++)
            failing.ThenDoing(() =>
            {
                if (Interlocked.Increment(ref attempts) % 2 == 0) throw new HttpRequestException("connection reset by peer");
            }, HttpStatusCode.ServiceUnavailable, "<html>upstream</html>");
        await DeadLetterProofAsync("zz-exp503-", failing, () => attempts, nameof(GatewayInvoiceExpiryFetchFailed));
    }

    /// <summary>مُضيفٌ بِتَركيبِ القالَبِ وبَوّابَةٍ تَفشَلُ كَما يَصِفُها <paramref name="failing"/>، ورِسالَةُ مُهلَةٍ لِصَفقَةٍ واحِدَة ⇒ أَربَعُ مُحاوَلاتٍ وصَفٌّ ميتٌ واحِدٌ بِنَوعِ
    /// <paramref name="exception"/> وإشعارٌ واحِدٌ لِصاحِبِ المَتجَر، والصَفقَةُ قائِمَة — جِسمُ الصَفِّ الأَوَّلِ بِحَرفِه (‏2026-10-30) نُقِلَ إلى هُنا لِيَقيسَ العَطَبَ الحَقيقِيَّ أَيضاً.</summary>
    private async Task DeadLetterProofAsync(string slugPrefix, MoyasarScriptedHandler failing, Func<int> attempts, string exception)
    {
        var slug = slugPrefix + Guid.NewGuid().ToString("N")[..8];
        var (buyer, owner) = (Guid.NewGuid(), Guid.NewGuid());
        var kek = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        Guid dealId = default;
        await using var host = BuildApp(kek, failing, shiftClock: false);
        await host.StartAsync();
        var store = host.Services.GetRequiredService<IDocumentStore>();
        try
        {
            var binding = PaymentsBinding(host.Services.GetRequiredService<ProviderVault>(), sealedFor: slug);
            binding.TenantSlug = slug;
            await using (var s = store.LightweightSession(slug)) { s.Store(binding); await s.SaveChangesAsync(); }
            await using (var s = store.LightweightSession())
            {
                s.Store(new ACommerce.Kit.Tenants.Tenant { Id = slug, Name = "مَتجَر", OwnerUserId = owner });
                await s.SaveChangesAsync();
            }
            using (var scope = host.Services.CreateScope())
            {
                var deals = scope.ServiceProvider.GetRequiredService<DealsService>();
                var born = await deals.StartAsync(slug, "marketplace", buyer, "مُشتَرٍ", null, "سِلعَة", 100m, GatewayPayments.MarkedAtBirth(new()
                {
                    [CheckoutPayMethods.AttributeKey] = CheckoutPayMethods.Card,
                }, viaGateway: true));
                await deals.AttachRefAsync(slug, born.Id, GatewayPaymentProof.InvoiceRef, InvoiceId);
                dealId = born.Id;
                await scope.ServiceProvider.GetRequiredService<IMessageBus>().SendAsync(new GatewayInvoiceExpiryDue(slug, dealId));
            }

            var deadline = Stopwatch.StartNew();
            List<(Guid Id, string? Exception, bool? Replayable)> dead = new();
            List<Notification> told = new();
            while (deadline.Elapsed < TimeSpan.FromSeconds(90))
            {
                dead = await DeadLettersAsync(dealId);
                await using (var q = store.QuerySession(slug))
                    told = (await Marten.QueryableExtensions.ToListAsync(q.Query<Notification>())).Where(n => n.UserId == owner).ToList();
                if (dead.Count > 0 && told.Count > 0) break;
                await Task.Delay(500);
            }
            await Task.Delay(1000);   // ولا ثانٍ يَلحَقُ الأَوَّل
            await using (var q = store.QuerySession(slug))
                told = (await Marten.QueryableExtensions.ToListAsync(q.Query<Notification>())).Where(n => n.UserId == owner).ToList();
            Deal? deal;
            await using (var q = store.QuerySession(slug)) deal = await q.LoadAsync<Deal>(dealId);
            output.WriteLine($"مُحاوَلات: {attempts()} · بَعدَ {deadline.Elapsed.TotalSeconds:0.0}ث · ميتَة: [{string.Join(", ", dead.Select(d => $"{d.Exception} replayable={d.Replayable}"))}] · " +
                             $"إشعاراتُ المالِك: {told.Count} «{told.FirstOrDefault()?.Title}» → {told.FirstOrDefault()?.RelatedUrl} · الصَفقَة {deal?.Status}");

            Assert.Equal(GatewayInvoiceExpiry.RetryCooldowns.Length + 1, attempts());
            Assert.Contains(exception, Assert.Single(dead).Exception ?? "");
            var note = Assert.Single(told);
            Assert.Equal(ACommerce.Platform.I18n.LocaleCatalog.Text(ACommerce.Platform.I18n.LocaleCatalog.Arabic, "notifications.deal_expiry_failed.title"), note.Title);
            Assert.Equal(DealNotices.StudioDealUrl(slug, dealId), note.RelatedUrl);
            Assert.Equal(DealStatus.Active, deal!.Status);
        }
        finally
        {
            await host.StopAsync();
            await using (var conn = new NpgsqlConnection(ResilientConnection))
            {
                await conn.OpenAsync();
                var ids = (await DeadLettersAsync(dealId)).Select(d => d.Id).ToArray();
                await using var cmd = new NpgsqlCommand("delete from platform.wolverine_dead_letters where id = any(@ids)", conn);
                cmd.Parameters.AddWithValue("ids", ids);
                await cmd.ExecuteNonQueryAsync();
            }
            await DeleteEnvelopesAsync(dealId);
            await using var cleanup = LiveDealConcurrencyProofTests.BuildStore();
            await using (var s = cleanup.LightweightSession(slug))
            {
                foreach (var d in await Marten.QueryableExtensions.ToListAsync(s.Query<Deal>())) s.Delete(d);
                foreach (var b in await Marten.QueryableExtensions.ToListAsync(s.Query<TenantProviderBinding>())) s.Delete(b);
                foreach (var n in await Marten.QueryableExtensions.ToListAsync(s.Query<Notification>())) s.Delete(n);
                await s.SaveChangesAsync();
            }
            await using (var s = cleanup.LightweightSession())
            {
                s.Delete<ACommerce.Kit.Tenants.Tenant>(slug);
                await s.SaveChangesAsync();
            }
        }
    }

    /// <summary>
    /// <para><b>طَلَبٌ مَدفوعٌ والبَوّابَةُ تُجيبُ ‏503 ساعَةَ تَحينُ مُهلَتُه</b> (‏2026-10-30، جَولَةُ الإصلاحِ الثانِيَةُ لِلمَسار F3؛ تَوصيفُه <c>0c2cd09</c> — مُراجَعَةُ <c>cc71027</c>
    /// الحاجِبَة): الرِسالَةُ تَحينُ لِكُلِّ فاتورَةٍ ولو دُفِعَت، فَكانَ جَلبٌ فاشِلٌ يُميتُها ويُخبِرُ صاحِبَ المَتجَرِ «أَلغِه» عَن طَلَبٍ مَدفوع. اليَوم، عَلى القاعِدَةِ وصُندوقِها المُستَديم:
    /// <b>مُحاوَلَةٌ واحِدَة</b>، و<b>لا صَفَّ</b> في <c>platform.wolverine_dead_letters</c>، و<b>لا إشعارَ</b> لِصاحِبِ المَتجَر، والصَفقَةُ قائِمَةٌ مَقبوضَة.</para>
    /// </summary>
    [Fact]
    public async Task A_paid_order_whose_gateway_answers_503_at_expiry_lands_no_dead_letter_and_no_owner_notice()
    {
        if (!Enabled) return;

        var attempts = 0;
        var failing = new MoyasarScriptedHandler();
        for (var i = 0; i < 8; i++)
            failing.ThenDoing(() => Interlocked.Increment(ref attempts), HttpStatusCode.ServiceUnavailable, "<html>upstream</html>");
        var slug = "zz-exppaid-" + Guid.NewGuid().ToString("N")[..8];
        var (buyer, owner) = (Guid.NewGuid(), Guid.NewGuid());
        var kek = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        Guid dealId = default;
        await using var host = BuildApp(kek, failing, shiftClock: false);
        await host.StartAsync();
        var store = host.Services.GetRequiredService<IDocumentStore>();
        try
        {
            var binding = PaymentsBinding(host.Services.GetRequiredService<ProviderVault>(), sealedFor: slug);
            binding.TenantSlug = slug;
            await using (var s = store.LightweightSession(slug)) { s.Store(binding); await s.SaveChangesAsync(); }
            await using (var s = store.LightweightSession())
            {
                s.Store(new ACommerce.Kit.Tenants.Tenant { Id = slug, Name = "مَتجَر", OwnerUserId = owner });
                await s.SaveChangesAsync();
            }
            using (var scope = host.Services.CreateScope())
            {
                var deals = scope.ServiceProvider.GetRequiredService<DealsService>();
                var born = await deals.StartAsync(slug, "marketplace", buyer, "مُشتَرٍ", null, "سِلعَة", 100m, GatewayPayments.MarkedAtBirth(new()
                {
                    [CheckoutPayMethods.AttributeKey] = CheckoutPayMethods.Card,
                }, viaGateway: true));
                await deals.AttachRefAsync(slug, born.Id, GatewayPaymentProof.InvoiceRef, InvoiceId);
                await deals.AttachRefAsync(slug, born.Id, "payment_status", nameof(PaymentStatus.Captured));   // دُفِعَت قَبلَ المُهلَة
                dealId = born.Id;
                await scope.ServiceProvider.GetRequiredService<IMessageBus>().SendAsync(new GatewayInvoiceExpiryDue(slug, dealId));
            }

            var deadline = Stopwatch.StartNew();
            while (deadline.Elapsed < TimeSpan.FromSeconds(60) && Volatile.Read(ref attempts) == 0) await Task.Delay(200);
            await Task.Delay(TimeSpan.FromSeconds(9));   // أَطوَلُ مِن الإعادَتَينِ الأولَيَين (‏1 ثُمَّ ‏5 ثَوانٍ) — فَإعادَةٌ لَو وَقَعَت تُرى
            var dead = await DeadLettersAsync(dealId);
            List<Notification> told;
            await using (var q = store.QuerySession(slug))
                told = (await Marten.QueryableExtensions.ToListAsync(q.Query<Notification>())).Where(n => n.UserId == owner).ToList();
            Deal? deal;
            await using (var q = store.QuerySession(slug)) deal = await q.LoadAsync<Deal>(dealId);
            output.WriteLine($"مُحاوَلات: {Volatile.Read(ref attempts)} · بَعدَ {deadline.Elapsed.TotalSeconds:0.0}ث · ميتَة: {dead.Count} · إشعاراتُ المالِك: {told.Count} · " +
                             $"الصَفقَة {deal?.Status} مَقبوضَة={deal is not null && DealsService.IsCaptured(deal)}");

            Assert.Equal(1, Volatile.Read(ref attempts));
            Assert.Empty(dead);
            Assert.Empty(told);
            Assert.Equal(DealStatus.Active, deal!.Status);
            Assert.True(DealsService.IsCaptured(deal));
        }
        finally
        {
            await host.StopAsync();
            await using (var conn = new NpgsqlConnection(ResilientConnection))
            {
                await conn.OpenAsync();
                var ids = (await DeadLettersAsync(dealId)).Select(d => d.Id).ToArray();
                await using var cmd = new NpgsqlCommand("delete from platform.wolverine_dead_letters where id = any(@ids)", conn);
                cmd.Parameters.AddWithValue("ids", ids);
                await cmd.ExecuteNonQueryAsync();
            }
            await DeleteEnvelopesAsync(dealId);
            await using var cleanup = LiveDealConcurrencyProofTests.BuildStore();
            await using (var s = cleanup.LightweightSession(slug))
            {
                foreach (var d in await Marten.QueryableExtensions.ToListAsync(s.Query<Deal>())) s.Delete(d);
                foreach (var b in await Marten.QueryableExtensions.ToListAsync(s.Query<TenantProviderBinding>())) s.Delete(b);
                foreach (var n in await Marten.QueryableExtensions.ToListAsync(s.Query<Notification>())) s.Delete(n);
                await s.SaveChangesAsync();
            }
            await using (var s = cleanup.LightweightSession())
            {
                s.Delete<ACommerce.Kit.Tenants.Tenant>(slug);
                await s.SaveChangesAsync();
            }
        }
    }
}
