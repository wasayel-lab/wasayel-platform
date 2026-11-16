using System.Net;
using ACommerce.Kit.Tenants;
using ACommerce.Platform.Shared.Metering;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ تَوصيفُ «اليَوم»: نِزاعٌ ثُمَّ مُهلَةٌ ثُمَّ استِئنافٌ بَعدَ النافِذَةِ ثُمَّ مُهلَةٌ ثانِيَة — ما يَبلُغُ البَوّابَةَ وما يَصيرُ إلَيه الطَلَب (‏2026-11-13، الموجَة ١٣ W13-6 ·
//     مُراجَعَةُ المَرحَلَةِ ج الثانِيَة · M6C-CNT-R2-1؛ القاعِدَة ٣) ═══
//
// التَبديلُ التالي يُعطي كُلَّ تَشغيلٍ لِلمُهلَةِ بَعدَ استِئنافٍ زِنادَه في مُعَرِّفِ جَلبِه، فَيُعَدُّ الجَلبُ الثاني سَطراً لا يَبتَلِعُه `ON CONFLICT (id) DO NOTHING`. وهذا المِلَفُّ
// يُثَبِّتُ قَبلَه ما لا يَجوزُ أَن يَتَغَيَّر: الطَلَبَ عِندَ كُلِّ خُطوَة، وما يَبلُغُ ميسّر بِتَرتيبِه، والرِسالَةَ الَّتي يُرسِلُها الاستِئنافُ بِحَرفِها (عَقدٌ مُخَزَّن — ‏ADR-046 §٤)،
// وأَنّ إعادَةَ الرِسالَةِ نَفسِها لا تُضيفُ سَطراً، وأَنّ مُعَرِّفاتِ التَشغيلِ الأَوَّلِ والإغلاقِ والإلغاءِ في الدَفتَرِ كَما هِيَ. ولا يُثَبِّتُ عَدَدَ سُطورِ غَرَضِ المُهلَة —
// فَذاكَ ما يُصَحِّحُه التَبديل. ولا يُسَمّي نَوعاً ولا عُضواً يولَدُ فيه.
//
// المُضيفُ مُضيفُ الأَبوابِ عَلى القاعِدَة (`LiveTemplateDoorHost`) بِكاتِبِ الدَفتَرِ الحَقيقِيّ، وميسّر مُعالِجٌ يُجيبُ بِلا شَبَكَة، والحافِلَةُ حافِلَةٌ تُسَجِّلُ ما يُرسَلُ إلَيها —
// فَتُعالَجُ الرِسالَةُ الَّتي أَرسَلَها الاستِئنافُ نَفسُها بِمُعالِجِها نَفسِه. ولا يَعمَلُ إلّا بِـ`WASAYEL_LIVE_PROOF=1` و`ConnectionStrings__Postgres`.

[Collection(LiveProofCollection.Name)]
public class LiveExpiryResumeRunCharacterizationProofTests(ITestOutputHelper output)
{
    private static bool Enabled
        => LiveDealConcurrencyProofTests.Enabled && !string.IsNullOrEmpty(LiveDealConcurrencyProofTests.ConnectionString);

    private static async Task<List<string>> LedgerIdsAsync(string app)
    {
        await using var conn = new NpgsqlConnection(LiveDealConcurrencyProofTests.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("select id from meter.usage_entry where app = @a order by id", conn);
        cmd.Parameters.AddWithValue("a", app);
        var ids = new List<string>();
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) ids.Add(r.GetString(0));
        return ids;
    }

    [Fact]
    public async Task A_dispute_then_the_window_then_a_resume_then_its_message_cancels_the_order_with_these_gateway_calls()
    {
        if (!Enabled) return;
        var moyasar = new MoyasarScriptedHandler();
        var vault = TenantGatewayEndpointsTests.NewVault();
        var bus = new RecordingMessageBus();
        await using var host = LiveTemplateDoorHost.Build(services: s =>
        {
            s.AddSingleton(vault);
            s.AddSingleton<Wolverine.IMessageBus>(bus);
            s.ConfigureHttpClientDefaults(b => b.ConfigurePrimaryHttpMessageHandler(() => moyasar));
        });
        var slug = "w13r-" + Guid.NewGuid().ToString("N")[..8];
        var owner = Guid.NewGuid();
        var buyer = Guid.NewGuid();
        var invoice = Guid.NewGuid().ToString("D");
        await using (var s = host.Store.LightweightSession()) { s.Insert(new Tenant { Id = slug, Name = "مَتجَرُ الاستِئناف", OwnerUserId = owner }); await s.SaveChangesAsync(); }
        var binding = TenantGatewayEndpointsTests.PaymentsBinding(vault, sealedFor: slug);
        binding.TenantSlug = slug;
        await using (var s = host.Store.LightweightSession(slug)) { s.Store(binding); await s.SaveChangesAsync(); }

        Guid deal;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var deals = scope.ServiceProvider.GetRequiredService<ACommerce.Templates.Customer.Marketplace.Services.Deals.DealsService>();
            var born = await deals.StartAsync(slug, "marketplace", buyer, "مُشتَرٍ", null, "سِلعَة", 100m,
                ACommerce.Templates.Customer.Marketplace.Services.Deals.GatewayPayments.MarkedAtBirth(new()
                {
                    [ACommerce.Templates.Customer.Marketplace.Services.Deals.CheckoutPayMethods.AttributeKey] =
                        ACommerce.Templates.Customer.Marketplace.Services.Deals.CheckoutPayMethods.Card,
                }, viaGateway: true));
            deal = born.Id;
            await deals.AttachRefAsync(slug, deal, ACommerce.Templates.Customer.Marketplace.Services.Deals.GatewayPaymentProof.InvoiceRef, invoice);
            await deals.AttachRefAsync(slug, deal, ACommerce.Templates.Customer.Marketplace.Services.Deals.GatewayPayments.InvoiceExpiresAtRef,
                ACommerce.Kit.Payments.MoyasarClient.ExpiryText(DateTimeOffset.UtcNow.AddMinutes(-5)));
        }
        var listed = ACommerce.Templates.Customer.Marketplace.Services.Deals.GatewayPaymentProof.MetadataFor(new[] { deal });
        var l = new ACommerce.Platform.I18n.L(new Microsoft.AspNetCore.Http.HttpContextAccessor());

        async Task<ACommerce.Templates.Customer.Marketplace.Services.Deals.DealStatus> StatusAsync()
        {
            await using var q = host.Store.QuerySession(slug);
            return (await q.LoadAsync<ACommerce.Templates.Customer.Marketplace.Services.Deals.Deal>(deal))!.Status;
        }
        async Task HandleAsync(ACommerce.Templates.Customer.Marketplace.Services.Deals.GatewayInvoiceExpiryDue due)
        {
            await using var scope = host.Services.CreateAsyncScope();
            await ACommerce.Templates.Customer.Marketplace.Services.Deals.GatewayInvoiceExpiryHandler.Handle(due,
                scope.ServiceProvider.GetRequiredService<ACommerce.Templates.Customer.Marketplace.Services.Deals.GatewayPayments>(),
                scope.ServiceProvider.GetRequiredService<ACommerce.Templates.Customer.Marketplace.Services.Deals.DealNotices>(), l, CancellationToken.None);
        }
        string Methods() => string.Join(",", moyasar.Requests.Select(r => r.Method.Method));

        // ١ · النِزاع.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var disputed = await scope.ServiceProvider.GetRequiredService<ACommerce.Templates.Customer.Marketplace.Services.Deals.DealsService>()
                .DisputeAsync(slug, deal, owner, "صاحِبُ المَتجَر", "نِزاع");
            Assert.True(disputed.Ok);
        }
        Assert.Equal(ACommerce.Templates.Customer.Marketplace.Services.Deals.DealStatus.Disputed, await StatusAsync());

        // ٢ · رِسالَةُ المُهلَةِ المُجَدوَلَةُ تَحينُ والطَلَبُ في نِزاع ⇒ جَلبَةٌ واحِدَةٌ ولا يُلغى شَيء.
        moyasar.Then(HttpStatusCode.OK, MoyasarClientTests.InvoiceJson("initiated", 10000, metadata: listed, id: invoice));
        await HandleAsync(new(slug, deal));
        output.WriteLine($"[المُهلَة ١] {await StatusAsync()} · ميسّر {Methods()}");
        Assert.Equal(ACommerce.Templates.Customer.Marketplace.Services.Deals.DealStatus.Disputed, await StatusAsync());
        Assert.Equal("GET", Methods());

        // ٣ · الاستِئنافُ بَعدَ انقِضاءِ النافِذَة ⇒ قائِمَة، ورِسالَةُ مُهلَةٍ واحِدَةٌ الآنَ بِحَرفِها.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var resumed = await scope.ServiceProvider.GetRequiredService<ACommerce.Templates.Customer.Marketplace.Services.Deals.DealsService>()
                .ResolveDisputeAsync(slug, deal, new ACommerce.Templates.Customer.Marketplace.Services.Deals.DealCanceller(owner, "صاحِبُ المَتجَر", true),
                    ACommerce.Templates.Customer.Marketplace.Services.Deals.DealDisputeResolution.Resume, "استِئناف");
            Assert.True(resumed.Ok, resumed.Violation?.Code);
        }
        Assert.Equal(ACommerce.Templates.Customer.Marketplace.Services.Deals.DealStatus.Active, await StatusAsync());
        var sent = Assert.Single(bus.Sent);
        Assert.Equal(new ACommerce.Templates.Customer.Marketplace.Services.Deals.GatewayInvoiceExpiryDue(slug, deal), sent.Message);
        Assert.Null(sent.Options);

        // ٤ · رِسالَةُ الاستِئنافِ نَفسُها ⇒ الطَلَبُ يُلغى بِالمُهلَة، وجَلبَتُها، وجَلبُ الإغلاقِ والإلغاءُ عِندَ البَوّابَة.
        moyasar.Then(HttpStatusCode.OK, MoyasarClientTests.InvoiceJson("initiated", 10000, metadata: listed, id: invoice))
               .Then(HttpStatusCode.OK, MoyasarClientTests.InvoiceJson("initiated", 10000, metadata: listed, id: invoice))
               .Then(HttpStatusCode.OK, MoyasarClientTests.InvoiceJson("canceled", 10000, metadata: listed, id: invoice));
        var due = (ACommerce.Templates.Customer.Marketplace.Services.Deals.GatewayInvoiceExpiryDue)sent.Message;
        await HandleAsync(due);
        var afterRun = await LedgerIdsAsync(slug);
        output.WriteLine($"[المُهلَة ٢] {await StatusAsync()} · ميسّر {Methods()} · الدَفتَر {string.Join(", ", afterRun)}");
        Assert.Equal(ACommerce.Templates.Customer.Marketplace.Services.Deals.DealStatus.Cancelled, await StatusAsync());
        Assert.Equal("GET,GET,GET,PUT", Methods());

        // ٥ · الرِسالَةُ نَفسُها مُعادَة ⇒ جَلبَةٌ أُخرى ولا سَطرَ جَديد.
        moyasar.Then(HttpStatusCode.OK, MoyasarClientTests.InvoiceJson("canceled", 10000, metadata: listed, id: invoice));
        await HandleAsync(due);
        var afterRetry = await LedgerIdsAsync(slug);
        output.WriteLine($"[المُهلَة ٢ مُعادَة] {await StatusAsync()} · ميسّر {Methods()} · الدَفتَر {afterRetry.Count}");
        Assert.Equal(ACommerce.Templates.Customer.Marketplace.Services.Deals.DealStatus.Cancelled, await StatusAsync());
        Assert.Equal("GET,GET,GET,PUT,GET", Methods());
        Assert.Equal(afterRun, afterRetry);

        // ومُعَرِّفاتُ ما سِوى تَشغيلِ الاستِئنافِ كَما هِيَ: جَلبَةُ المُهلَةِ الأولى، وجَلبُ الإغلاقِ بِالصَفقَة، والإلغاءُ عِندَ البَوّابَة.
        string Fetch(string tail) => $"{OutboundUnits.PaymentGatewayInvoiceFetch}:{ProviderNames.StoreGateway}:{invoice}:{tail}";
        Assert.Contains(Fetch("expiry"), afterRetry);
        Assert.Contains(Fetch($"close:{deal:N}"), afterRetry);
        Assert.Contains($"{OutboundUnits.PaymentGatewayInvoiceCancel}:{ProviderNames.StoreGateway}:{invoice}:close", afterRetry);
    }
}
