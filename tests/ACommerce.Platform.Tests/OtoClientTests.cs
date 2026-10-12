using System.Net;
using System.Text.Json;
using ACommerce.Kit.Delivery;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ عَميلُ OTO — ما يُرسَلُ بِالضَبط، وما يَعودُ رَمزاً لا استِثناءً ═══════════
//
// **ولا رَمزَ OTO حَقيقيّاً في هذِه الجَولَة ولا يُطلَب** (‏ADR-033 §٢‑هـ الخُطوَة 3):
// مُعالِجٌ وَهمِيٌّ يَلتَقِطُ الطَلَبَ ويُعيدُ **رُدودَ الوَثيقَةِ بِأَشكالِها** — مِثالُ
// «Refresh Token» (‏`"expires_in": "3600"` نَصّاً)، ومِثالُ «Create Order» (‏`"otoId": 540789`
// رَقَماً)، ومِثالُ «Order Tracking» (‏`"otoId": "30514281"` نَصّاً و`printAWBURL`)، ومِثالُ
// خَطَإِ «Create Shipment» (‏`OTO1006 · Credit is not enough`). مَقروءَةٌ مِن مَجموعَةِ
// الوَثائِقِ العامَّةِ في `https://apis.tryoto.com/` يَومَ ‏2026-10-12. نَفسُ نَمَطِ
// `PayPalProviderTests` و`BrevoEmailChannelTests`.
//
// **والبُرهانُ الحَيُّ دَينٌ مُعلَن** — `LiveOtoStagingProofTests` أَدناه يَتَخَطّى نَفسَه
// ما لَم يَضَعِ المالِكُ رَمزَ `staging`.

/// <summary>مُعالِجٌ يَلتَقِطُ الطَلَباتِ ويَرُدُّ رُدوداً مُرَتَّبَة — ويَعُدُّ نِداءاتِ الرَمز.</summary>
internal sealed class OtoScriptedHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpResponseMessage>> _replies = new();

    public List<HttpRequestMessage> Requests { get; } = new();
    public List<string?> Bodies { get; } = new();
    public int TokenCalls { get; private set; }

    public OtoScriptedHandler Then(HttpStatusCode status, string body)
    {
        _replies.Enqueue(() => new HttpResponseMessage(status) { Content = new StringContent(body) });
        return this;
    }

    public OtoScriptedHandler ThenThrow()
    {
        _replies.Enqueue(() => throw new HttpRequestException("لا اتِّصال"));
        return this;
    }

    /// <summary>رَدُّ «Refresh Token» بِشَكلِ مِثالِ الوَثيقَة — <c>expires_in</c> نَصّاً.</summary>
    public OtoScriptedHandler ThenToken(string token = "ACCESS-1", string expiresIn = "\"3600\"")
        => Then(HttpStatusCode.OK,
            $"{{\"access_token\":\"{token}\",\"refresh_token\":\"AMf-x\",\"success\":true," +
            $"\"token_type\":\"Bearer\",\"expires_in\":{expiresIn}}}");

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        Bodies.Add(request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken));
        if (request.RequestUri!.AbsolutePath == OtoClient.RefreshTokenPath) TokenCalls++;

        return _replies.Count > 0
            ? _replies.Dequeue()()
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };
    }
}

/// <summary>لوغٌ يُلتَقَطُ نَصّاً — لِيُقاسَ أَنّ الرَمزَ لا يَبلُغُه.</summary>
internal sealed class OtoCapturingLogger : ILogger<OtoClient>
{
    public List<string> Lines { get; } = new();
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter) => Lines.Add(formatter(state, exception));
}

public class OtoClientTests
{
    private const string Refresh = "RT-permanent-0001";

    private static OtoOptions Opts(string environment = OtoEnvironment.Staging) => new()
    {
        Environment = environment,
        TimeoutSeconds = 5,
    };

    private static OtoClient Client(OtoScriptedHandler handler, OtoOptions? opts = null,
        OtoTokenCache? cache = null, ILogger<OtoClient>? log = null)
        => new(new HttpClient(handler), Options.Create(opts ?? Opts()),
               cache ?? new OtoTokenCache(), log ?? NullLogger<OtoClient>.Instance);

    private static OtoOrderDraft CashDraft(string orderId = "0f8fad5bd9cb469fa165705ed7eb6e2a") => new(
        orderId, OtoClient.CashOnDelivery, 150m, 150m, "SAR",
        new OtoCustomer("مُشتَرٍ", "0500000000", "حَيُّ المِثال", "Riyadh", "SA"),
        "سِلعَة");

    private static JsonElement BodyOf(OtoScriptedHandler h, int index)
        => JsonDocument.Parse(h.Bodies[index]!).RootElement;

    // ─── البيئَة: مَعجَمٌ مُغلَق، والإنتاجُ مَعروفٌ ومُغلَق ─────────────────

    [Theory]
    [InlineData("staging", OtoEnvironment.StagingBaseUrl)]
    [InlineData(" STAGING ", OtoEnvironment.StagingBaseUrl)]
    [InlineData("production", OtoEnvironment.ProductionBaseUrl)]
    public void Known_environments_map_to_their_documented_host(string value, string expected)
        => Assert.Equal(expected, OtoEnvironment.BaseUrlFor(value));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("sandbox")]
    [InlineData("live")]
    public void An_unknown_environment_has_no_host(string? value)
        => Assert.Null(OtoEnvironment.BaseUrlFor(value));

    /// <summary><b>الإنتاجُ مُغلَقٌ في هذِه المَوجَة بِقَرار</b> (‏ADR-033 §٢‑د) — مُضيفُه
    /// مَعروفٌ والعَميلُ لا يُناديه. يَحمَرُّ هذا الصَفُّ بِاسمِه يَومَ يُفتَح.</summary>
    [Fact]
    public async Task Only_staging_is_open_and_production_answers_not_configured_without_a_call()
    {
        Assert.True(OtoEnvironment.IsOpen("staging"));
        Assert.False(OtoEnvironment.IsOpen("production"));
        Assert.False(OtoEnvironment.IsOpen(""));

        var handler = new OtoScriptedHandler();
        var client = Client(handler, Opts(OtoEnvironment.Production));
        Assert.Null(client.BaseUrl);

        var res = await client.CreateOrderAsync(Refresh, CashDraft());
        Assert.Equal(OtoFailures.NotConfigured, res.Failure!.Code);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public void The_environment_variables_are_the_keys_with_double_underscores()
    {
        Assert.Equal("Shipping__Oto__Environment", OtoEnvironment.EnvVarName(OtoEnvironment.EnvironmentKey));
        Assert.Equal("Shipping__Oto__RefreshToken", OtoEnvironment.EnvVarName(OtoEnvironment.RefreshTokenKey));
    }

    /// <summary>قيمَةٌ مَكتوبَةٌ خارِجَ المَعجَمِ تُفشِلُ الإقلاعَ بِاسمِ المُتَغَيِّر؛ والفَراغُ
    /// والقيمَتانِ المَعروفَتانِ لا — نَسَقُ <c>AddPaddleBilling</c>.</summary>
    [Theory]
    [InlineData("stagin", true)]
    [InlineData("sandbox", true)]
    [InlineData("", false)]
    [InlineData("staging", false)]
    [InlineData("production", false)]
    public void A_misspelt_environment_fails_the_boot_naming_the_variable(string value, bool throws)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [OtoEnvironment.EnvironmentKey] = value })
            .Build();
        var services = new ServiceCollection();

        if (!throws)
        {
            services.AddOtoShipping(config);
            Assert.Contains(services, d => d.ServiceType == typeof(OtoClient));
            return;
        }

        var ex = Assert.Throws<InvalidOperationException>(() => services.AddOtoShipping(config));
        Assert.Contains("Shipping__Oto__Environment", ex.Message);
    }

    /// <summary>العَميلُ يُسَجَّلُ بِـ<c>AddHttpClient</c> ويُحَلُّ مِن الوِعاء — والرَمزُ مُفرَد.</summary>
    [Fact]
    public void The_client_is_registered_with_AddHttpClient_and_the_token_cache_is_a_singleton()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [OtoEnvironment.EnvironmentKey] = "staging" })
            .Build();
        var services = new ServiceCollection().AddLogging();
        services.AddOtoShipping(config);
        using var sp = services.BuildServiceProvider();

        Assert.NotNull(sp.GetRequiredService<OtoClient>());
        Assert.Same(sp.GetRequiredService<OtoTokenCache>(), sp.GetRequiredService<OtoTokenCache>());
        Assert.Equal(OtoEnvironment.StagingBaseUrl, sp.GetRequiredService<OtoClient>().BaseUrl);
    }

    // ─── الرَمز ──────────────────────────────────────────────────────────

    [Fact]
    public async Task The_access_token_is_exchanged_with_the_refresh_token_in_the_body()
    {
        var handler = new OtoScriptedHandler().ThenToken("ACCESS-1");
        var res = await Client(handler).AccessTokenAsync(Refresh);

        Assert.Equal("ACCESS-1", res.Value);
        var req = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, req.Method);
        Assert.Equal(OtoEnvironment.StagingBaseUrl + OtoClient.RefreshTokenPath, req.RequestUri!.ToString());
        Assert.Equal(Refresh, BodyOf(handler, 0).GetProperty("refresh_token").GetString());
        Assert.Null(req.Headers.Authorization);
    }

    /// <summary><b>النِداءُ الثاني لا يَطلُبُ رَمزاً — ولَو مِن نُسخَةِ عَميلٍ أُخرى</b>: العَميلُ
    /// عابِرٌ بِـ<c>AddHttpClient</c>، فَالتَخزينُ في المُفرَدِ أَو لا تَخزين. والعَدّادُ البُرهان.</summary>
    [Fact]
    public async Task The_access_token_is_cached_across_calls_and_client_instances()
    {
        var cache = new OtoTokenCache();
        var handler = new OtoScriptedHandler()
            .ThenToken("ACCESS-1")
            .Then(HttpStatusCode.OK, "{\"success\":true,\"otoId\":540789}")
            .Then(HttpStatusCode.OK, "{\"success\":true,\"status\":\"new\",\"orderId\":\"o\"}");

        await Client(handler, cache: cache).CreateOrderAsync(Refresh, CashDraft());
        await Client(handler, cache: cache).GetOrderStatusAsync(Refresh, "o");

        Assert.Equal(1, handler.TokenCalls);
        Assert.Equal(1, cache.FetchCount);
        Assert.All(handler.Requests.Skip(1), r => Assert.Equal("Bearer", r.Headers.Authorization!.Scheme));
        Assert.All(handler.Requests.Skip(1), r => Assert.Equal("ACCESS-1", r.Headers.Authorization!.Parameter));
    }

    /// <summary>ساعَةُ الوَثيقَةِ إلّا دَقيقَة: ‏<c>"3600"</c> نَصّاً يُقرَأ، ويَنتَهي المُخَزَّنُ عِندَ
    /// ‏3540 ثانِيَة لا ‏3600 — مَقيسٌ عَلى المَخزَنِ بِوَقتٍ مُمَرَّر، ثُمَّ عَلى العَميلِ نَفسِه بِرَدِّ
    /// الوَثيقَةِ النَصِّيّ (ورَدٍّ بِعُمرِ دَقيقَةٍ يَنتَهي قَبلَ ساعَة — فَالقِراءَةُ تُمَيِّز).</summary>
    [Fact]
    public async Task The_documented_hour_is_read_from_the_string_and_the_margin_is_subtracted()
    {
        var cache = new OtoTokenCache();
        var now = DateTimeOffset.UnixEpoch;
        var key = OtoTokenCache.KeyOf(Refresh);

        await cache.GetAsync(key, _ => Task.FromResult(
            OtoResult<OtoAccessToken>.Success(new OtoAccessToken("T", 3600))), now);
        Assert.Equal("T", cache.Cached(key, now.AddSeconds(3539)));
        Assert.Null(cache.Cached(key, now.AddSeconds(3541)));

        var hour = new OtoTokenCache();
        await Client(new OtoScriptedHandler().ThenToken("H", "\"3600\""), cache: hour).AccessTokenAsync(Refresh);
        Assert.Equal("H", hour.Cached(key, DateTimeOffset.UtcNow.AddSeconds(3000)));
        Assert.Null(hour.Cached(key, DateTimeOffset.UtcNow.AddSeconds(3600)));

        var minute = new OtoTokenCache();
        await Client(new OtoScriptedHandler().ThenToken("M", "60"), cache: minute).AccessTokenAsync(Refresh);
        Assert.Null(minute.Cached(key, DateTimeOffset.UtcNow.AddSeconds(3000)));
    }

    /// <summary>حَقلُ العُمرِ الغائِبُ يَرتَدُّ إلى ساعَةِ الوَثيقَة — لا إلى صِفرٍ يُجَدِّدُ كُلَّ نِداء.</summary>
    [Fact]
    public async Task A_missing_lifetime_falls_back_to_the_documented_hour()
    {
        var cache = new OtoTokenCache();
        var key = OtoTokenCache.KeyOf(Refresh);
        var handler = new OtoScriptedHandler()
            .Then(HttpStatusCode.OK, "{\"access_token\":\"ACCESS-1\",\"success\":true}")
            .Then(HttpStatusCode.OK, "{\"success\":true,\"otoId\":1}")
            .Then(HttpStatusCode.OK, "{\"success\":true,\"otoId\":2}");

        await Client(handler, cache: cache).CreateOrderAsync(Refresh, CashDraft());
        await Client(handler, cache: cache).CreateOrderAsync(Refresh, CashDraft());

        Assert.Equal(1, handler.TokenCalls);
        Assert.Equal("ACCESS-1", cache.Cached(key, DateTimeOffset.UtcNow.AddSeconds(3000)));
        Assert.Null(cache.Cached(key, DateTimeOffset.UtcNow.AddSeconds(OtoEnvironment.DocumentedTokenLifetimeSeconds)));
    }

    /// <summary>‏401 عَلى عَمَلِيَّةٍ يُسقِطُ الرَمزَ المُخَزَّنَ ويُعيدُ مَرَّةً واحِدَةً بِرَمزٍ جَديد —
    /// لا حَلقَة، ولا رَمزٌ فاسِدٌ يَبقى ساعَتَه.</summary>
    [Fact]
    public async Task A_401_drops_the_cached_token_and_retries_exactly_once()
    {
        var handler = new OtoScriptedHandler()
            .ThenToken("OLD")
            .Then(HttpStatusCode.Unauthorized, "{}")
            .ThenToken("NEW")
            .Then(HttpStatusCode.OK, "{\"success\":true,\"otoId\":540789}");

        var res = await Client(handler).CreateOrderAsync(Refresh, CashDraft());

        Assert.True(res.Ok);
        Assert.Equal(2, handler.TokenCalls);
        Assert.Equal("NEW", handler.Requests[^1].Headers.Authorization!.Parameter);
        Assert.Equal(4, handler.Requests.Count);
    }

    // ─── العَمَلِيّاتُ الثَلاث ─────────────────────────────────────────────

    /// <summary><b>الطَلَبُ يُنشِئُ شُحنَتَه بِمَبلَغِ التَحصيل</b>: ‏<c>createShipment: true</c>،
    /// و<c>payment_method: "cod"</c> و<c>amount_due</c> = <c>amount</c> («‏If order paid amount_due
    /// will be 0, otherwise equal to amount»)، والمُستَلِمُ بِحُقولِه الخَمسَةِ الإلزامِيَّة — والمُعَرِّفانِ
    /// يَعودان: مُعَرِّفُنا ومُعَرِّفُ OTO (رَقَماً في المِثالِ فَيُقرَأُ نَصّاً).</summary>
    [Fact]
    public async Task Create_order_sends_createShipment_true_with_the_cod_amount_and_returns_both_ids()
    {
        var handler = new OtoScriptedHandler()
            .ThenToken()
            .Then(HttpStatusCode.OK, "{\"success\":true,\"otoId\":540789}");

        var res = await Client(handler).CreateOrderAsync(Refresh, CashDraft("deal-1"));

        Assert.True(res.Ok);
        Assert.Equal(new OtoCreatedOrder("deal-1", "540789"), res.Value);

        var req = handler.Requests[1];
        Assert.Equal(OtoEnvironment.StagingBaseUrl + OtoClient.CreateOrderPath, req.RequestUri!.ToString());
        var body = BodyOf(handler, 1);
        Assert.Equal("deal-1", body.GetProperty("orderId").GetString());
        Assert.True(body.GetProperty("createShipment").GetBoolean());
        Assert.Equal("cod", body.GetProperty("payment_method").GetString());
        Assert.Equal(150m, body.GetProperty("amount").GetDecimal());
        Assert.Equal(150m, body.GetProperty("amount_due").GetDecimal());
        Assert.Equal("SAR", body.GetProperty("currency").GetString());
        Assert.Equal("سِلعَة", body.GetProperty("item_description").GetString());
        var customer = body.GetProperty("customer");
        foreach (var field in new[] { "name", "mobile", "address", "city", "country" })
            Assert.False(string.IsNullOrEmpty(customer.GetProperty(field).GetString()), field);
        // ولا يُرسَلُ ما لا يُعرَف: لا خِيارَ ناقِلٍ ولا عُنوانَ مُرسِل (تُعَيِّنُهُما OTO مِن حِسابِ البائِع).
        Assert.False(body.TryGetProperty("deliveryOptionId", out _));
        Assert.False(body.TryGetProperty("senderInformation", out _));
        Assert.False(body.TryGetProperty("pickupLocationCode", out _));
    }

    [Fact]
    public async Task Create_order_reads_a_string_otoId_as_well_as_a_number()
    {
        var handler = new OtoScriptedHandler()
            .ThenToken()
            .Then(HttpStatusCode.OK, "{\"success\":true,\"otoId\":\"30514281\"}");

        var res = await Client(handler).CreateOrderAsync(Refresh, CashDraft("deal-2"));
        Assert.Equal("30514281", res.Value!.OtoId);
    }

    /// <summary>حالُ الطَلَبِ بِمِثالِ «Order Tracking» حَرفاً: الحالُ ورابِطُ البوليصَةِ ورابِطُ التَتَبُّع.</summary>
    [Fact]
    public async Task Order_status_posts_the_order_id_and_reads_status_label_and_tracking()
    {
        var handler = new OtoScriptedHandler()
            .ThenToken()
            .Then(HttpStatusCode.OK,
                "{\"date\":\"2026-04-27 12:42:07\",\"orderId\":\"OID-23331-9749\"," +
                "\"trackingUrl\":\"https://app.tryoto.com/otoflex-tracking?enc=x\",\"deliveryCompany\":\"otoDriverApp\"," +
                "\"printAWBURL\":\"https://app.tryoto.com/print/awb?enc=y\",\"shipmentId\":\"DCA87E10\"," +
                "\"success\":true,\"otoId\":\"30514281\",\"status\":\"returnShipmentProcessing\"}");

        var res = await Client(handler).GetOrderStatusAsync(Refresh, "OID-23331-9749");

        Assert.True(res.Ok);
        Assert.Equal(new OtoOrderState("OID-23331-9749", "30514281", "returnShipmentProcessing",
            "https://app.tryoto.com/print/awb?enc=y", "https://app.tryoto.com/otoflex-tracking?enc=x"), res.Value);
        Assert.False(res.Value!.IsDelivered);
        Assert.Equal(OtoEnvironment.StagingBaseUrl + OtoClient.OrderStatusPath, handler.Requests[1].RequestUri!.ToString());
        Assert.Equal("OID-23331-9749", BodyOf(handler, 1).GetProperty("orderId").GetString());
    }

    [Theory]
    [InlineData("delivered", true)]
    [InlineData("Delivered", false)]
    [InlineData("outForDelivery", false)]
    [InlineData("pickedUp", false)]
    public void Only_the_documented_status_word_counts_as_delivered(string status, bool delivered)
        => Assert.Equal(delivered, new OtoOrderState("o", null, status, null, null).IsDelivered);

    /// <summary><b>حالاتُ الشُحنَةِ القائِمَةِ بِحَرفِ «‏List Of Statuses» وبِتَرتيبِه</b> (مُراجَعَةُ تَيّارِ S7، ‏2026-10-12): صُفوفُ
    /// المَراحِلِ «Creation» (‏5) و«Pickup» (‏1) و«Transit» (‏11) و«Delivery» (‏1) — اثنَتا وسَبعونَ صَفّاً في الجَدوَل، هذِه
    /// ثَمانِيَةَ عَشَرَ مِنها؛ و<c>pickupAttemted</c> بِحَرفِ الوَثيقَة.</summary>
    [Fact]
    public void The_shipment_statuses_are_the_documented_forward_stages_in_order()
    {
        Assert.Equal(new[]
        {
            "searchingDriver", "shipmentCreated", "goingToPickup", "arrivedPickup", "pickupAttemted",
            "pickedUp",
            "arrivedDestinationTerminal", "arrivedTerminal", "departedTerminal", "inTransit", "arrivedOriginTerminal",
            "outForDelivery", "arrivedDestination", "shipmentInProgress", "undeliveredAttempt", "shipmentOnHold", "heldForPickup",
            "delivered",
        }, OtoClient.ShipmentStatuses);
        Assert.Equal(18, OtoClient.ShipmentStatuses.Distinct(StringComparer.Ordinal).Count());
        Assert.Contains(OtoClient.DeliveredStatus, OtoClient.ShipmentStatuses);
    }

    /// <summary>قائِمَةُ سَماح: حالٌ ذاهِبٌ مِنها شُحنَة؛ وما قَبلَها أَو خارِجَها — أَو حالٌ لا تَعرِفُه القائِمَة، أَو حَرفٌ
    /// كَبيرٌ في غَيرِ مَوضِعِه — لا.</summary>
    [Theory]
    [InlineData("searchingDriver", true)]
    [InlineData("pickupAttemted", true)]
    [InlineData("delivered", true)]
    [InlineData("new", false)]
    [InlineData("missingData", false)]
    [InlineData("assignedToWarehouse", false)]
    [InlineData("canceled", false)]
    [InlineData("shipmentCanceled", false)]
    [InlineData("returned", false)]
    [InlineData("shipmentProcessing", false)]
    [InlineData("SearchingDriver", false)]
    public void Only_a_documented_forward_status_means_a_shipment_exists(string status, bool exists)
        => Assert.Equal(exists, new OtoOrderState("o", null, status, null, null).HasShipment);

    /// <summary><b>الحالاتُ النِهائِيَّةُ الأَربَع</b> بِحَرفِ «List Of Statuses» (مُراجَعَةُ المَوجَة 3a، ‏2026-10-12؛ ‏ADR-033 §٧‑ح‑٣‑١): مُوجَبٌ
    /// لِكُلٍّ وسالِبٌ لِحالِ شُحنَةٍ ولِحالٍ قَبلَها ولِحالَتَي نِهايَةٍ لَم يُسَمِّهِما البَندُ ولِحَرفٍ كَبير؛ ولا حالَ في القائِمَتَينِ مَعاً.</summary>
    [Theory]
    [InlineData("canceled", true)]
    [InlineData("deleted", true)]
    [InlineData("returned", true)]
    [InlineData("shipmentCanceled", true)]
    [InlineData("searchingDriver", false)]
    [InlineData("missingData", false)]
    [InlineData("lostOrDamaged", false)]
    [InlineData("destroyed", false)]
    [InlineData("Canceled", false)]
    public void Only_the_four_documented_terminal_statuses_close_an_order(string status, bool terminal)
    {
        Assert.Equal(terminal, new OtoOrderState("o", null, status, null, null).IsTerminal);
        Assert.Empty(OtoClient.TerminalStatuses.Intersect(OtoClient.ShipmentStatuses));
    }

    /// <summary>فَحصُ السِعرِ بِالحُقولِ الثَلاثَةِ الإلزامِيَّة، والخِياراتُ مِن <c>deliveryCompany</c>.</summary>
    [Fact]
    public async Task Fee_check_posts_the_three_required_fields_and_reads_the_options()
    {
        var handler = new OtoScriptedHandler()
            .ThenToken()
            .Then(HttpStatusCode.OK,
                "{\"success\":true,\"deliveryCompany\":[" +
                "{\"deliveryOptionName\":\"Deliver Now\",\"deliveryOptionId\":7109,\"price\":16,\"currency\":\"SAR\"}," +
                "{\"deliveryOptionName\":\"SMSA PUDO\",\"deliveryOptionId\":7326,\"price\":16.65,\"currency\":\"SAR\"}]}");

        var res = await Client(handler).CheckOtoDeliveryFeeAsync(Refresh, new OtoFeeQuery("Riyadh", "Jeddah", 3m));

        Assert.True(res.Ok);
        Assert.Equal(2, res.Value!.Count);
        Assert.Equal(new OtoDeliveryOption("7326", "SMSA PUDO", 16.65m, "SAR"), res.Value[1]);
        var body = BodyOf(handler, 1);
        Assert.Equal("Riyadh", body.GetProperty("originCity").GetString());
        Assert.Equal("Jeddah", body.GetProperty("destinationCity").GetString());
        Assert.Equal(3m, body.GetProperty("weight").GetDecimal());
    }

    // ─── المَعجَم: لِكُلِّ رَمزٍ مُوجَبٌ وسالِب، ولا استِثناءَ يَصعَد ─────────────

    [Fact]
    public void The_failure_lexicon_is_five_and_every_code_describes_itself()
    {
        Assert.Equal(5, OtoFailures.All.Count);
        Assert.Equal(OtoFailures.All.Count, OtoFailures.All.Distinct(StringComparer.Ordinal).Count());
        foreach (var code in OtoFailures.All)
        {
            var f = OtoFailures.Describe(code, null, null);
            Assert.Equal(code, f.Code);
            Assert.True(f.MessageAr.Length > 20, code);
        }
        Assert.Throws<ArgumentException>(() => OtoFailures.Describe("oto_other", null, null));
    }

    [Fact]
    public async Task Not_configured_without_an_environment_or_a_token_and_no_request_leaves()
    {
        var handler = new OtoScriptedHandler();
        Assert.Equal(OtoFailures.NotConfigured,
            (await Client(handler, Opts("")).CreateOrderAsync(Refresh, CashDraft())).Failure!.Code);
        Assert.Equal(OtoFailures.NotConfigured,
            (await Client(handler).GetOrderStatusAsync("  ", "o")).Failure!.Code);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Configured_staging_with_a_token_is_not_refused_as_not_configured()
    {
        var handler = new OtoScriptedHandler().ThenToken().Then(HttpStatusCode.OK, "{\"success\":true,\"otoId\":1}");
        var res = await Client(handler).CreateOrderAsync(Refresh, CashDraft());
        Assert.True(res.Ok);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "{\"success\":false}")]
    [InlineData(HttpStatusCode.OK, "{\"success\":false,\"access_token\":\"x\"}")]
    [InlineData(HttpStatusCode.OK, "{\"success\":true}")]
    public async Task A_refused_refresh_token_is_token_refused_and_nothing_else_is_called(HttpStatusCode status, string body)
    {
        var handler = new OtoScriptedHandler().Then(status, body);
        var res = await Client(handler).CreateOrderAsync(Refresh, CashDraft());

        Assert.Equal(OtoFailures.TokenRefused, res.Failure!.Code);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task An_accepted_refresh_token_is_not_token_refused()
    {
        var res = await Client(new OtoScriptedHandler().ThenToken()).AccessTokenAsync(Refresh);
        Assert.True(res.Ok);
    }

    /// <summary>
    /// <para><b>قُلِبَ بِاسمِه</b> — كانَ <c>Today_a_5xx_from_the_token_endpoint_is_called_token_refused</c> (‏<c>1ee37ed</c>):
    /// <c>FetchTokenAsync</c> كانَت تَرُدُّ كُلَّ رَدٍّ غَيرِ ناجِحٍ بِـ<c>oto_token_refused</c>، بَينَما مَعجَمُ العَميلِ يَقولُ في <c>Unreachable</c>
    /// «لا جَواب: انقِطاعٌ، أَو مُهلَةٌ، أَو ‏5xx» و<c>PostAsync</c> تُصَنِّفُ ‏≥500 كَذلك (مُراجَعَةُ المَوجَة 3a، تَيّارُ S8، ‏2026-10-12).
    /// <b>والآن</b>: ‏5xx مِن نُقطَةِ الرَمزِ «لا جَواب» كَكُلِّ نِداء — والحالُ مَحمولٌ، وطَلَبٌ واحِد.</para>
    /// </summary>
    [Fact]
    public async Task A_5xx_from_the_token_endpoint_is_unreachable_like_every_other_call()
    {
        var handler = new OtoScriptedHandler().Then(HttpStatusCode.ServiceUnavailable, "<html/>");
        var res = await Client(handler).CreateOrderAsync(Refresh, CashDraft());

        Assert.Equal(OtoFailures.Unreachable, res.Failure!.Code);
        Assert.Equal(503, res.Failure.HttpStatus);
        Assert.Single(handler.Requests);
    }

    /// <summary><b>نُقطَةُ الرَمزِ تُصَنَّفُ كَأُختِها في <c>PostAsync</c></b>: ‏5xx «لا جَواب» (المُوجَب)، و‏4xx «رَمزٌ مَرفوض» (السالِب) —
    /// والسالِبُ يُثبِتُ أَنّ الفَرعَ الجَديدَ لا يَبتَلِعُ رَفضَ الرَمزِ الحَقيقيّ.</summary>
    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable, OtoFailures.Unreachable)]
    [InlineData(HttpStatusCode.InternalServerError, OtoFailures.Unreachable)]
    [InlineData(HttpStatusCode.Unauthorized, OtoFailures.TokenRefused)]
    [InlineData(HttpStatusCode.BadRequest, OtoFailures.TokenRefused)]
    public async Task The_token_endpoint_classifies_a_failure_as_PostAsync_does(HttpStatusCode status, string expected)
    {
        var res = await Client(new OtoScriptedHandler().Then(status, "{\"success\":false}")).AccessTokenAsync(Refresh);

        Assert.Equal(expected, res.Failure!.Code);
        Assert.Equal((int)status, res.Failure.HttpStatus);
    }

    /// <summary><b>فَشَلُ الشَبَكَةِ رَمزٌ لا استِثناء</b> — انقِطاعٌ، أَو ‏5xx: يَعودُ
    /// <c>oto_unreachable</c> ولا يَرمي إلى النُقطَة.</summary>
    [Fact]
    public async Task A_network_failure_or_a_5xx_is_unreachable_and_never_throws()
    {
        var thrown = await Client(new OtoScriptedHandler().ThenThrow()).CreateOrderAsync(Refresh, CashDraft());
        Assert.Equal(OtoFailures.Unreachable, thrown.Failure!.Code);

        var server = await Client(new OtoScriptedHandler().ThenToken().Then(HttpStatusCode.ServiceUnavailable, "<html/>"))
            .GetOrderStatusAsync(Refresh, "o");
        Assert.Equal(OtoFailures.Unreachable, server.Failure!.Code);
        Assert.Equal(503, server.Failure.HttpStatus);
    }

    [Fact]
    public async Task A_reachable_answer_is_not_unreachable()
    {
        var res = await Client(new OtoScriptedHandler().ThenToken().Then(HttpStatusCode.OK, "{\"status\":\"new\"}"))
            .GetOrderStatusAsync(Refresh, "o");
        Assert.True(res.Ok);
    }

    /// <summary>رَفضُ OTO بِشَكلِ الوَثيقَة — ‏400 و<c>OTO1006 · Credit is not enough</c> —
    /// يَعودُ <c>oto_refused</c> حامِلاً رَمزَ OTO؛ و<c>"success": false</c> في ‏200 كَذلك.</summary>
    [Fact]
    public async Task A_documented_refusal_is_refused_and_carries_the_oto_error_code()
    {
        var handler = new OtoScriptedHandler()
            .ThenToken()
            .Then(HttpStatusCode.BadRequest,
                "{\"success\":false,\"otoErrorCode\":\"OTO1006\",\"otoErrorMessage\":\"Credit is not enough\"}")
            .Then(HttpStatusCode.OK, "{\"success\":false,\"otoErrorCode\":\"OTO1002\"}");
        var client = Client(handler);

        var credit = await client.CreateOrderAsync(Refresh, CashDraft());
        Assert.Equal(OtoFailures.Refused, credit.Failure!.Code);
        Assert.Equal("OTO1006", credit.Failure.OtoErrorCode);
        Assert.Equal(400, credit.Failure.HttpStatus);

        var falseOk = await client.GetOrderStatusAsync(Refresh, "o");
        Assert.Equal(OtoFailures.Refused, falseOk.Failure!.Code);
        Assert.Equal("OTO1002", falseOk.Failure.OtoErrorCode);
    }

    [Fact]
    public async Task A_successful_answer_is_not_refused()
    {
        var res = await Client(new OtoScriptedHandler().ThenToken().Then(HttpStatusCode.OK, "{\"success\":true,\"otoId\":9}"))
            .CreateOrderAsync(Refresh, CashDraft());
        Assert.True(res.Ok);
        Assert.Null(res.Failure);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"success\":true}")]
    [InlineData("[1,2]")]
    public async Task A_success_that_cannot_be_read_is_unreadable(string body)
    {
        var res = await Client(new OtoScriptedHandler().ThenToken().Then(HttpStatusCode.OK, body))
            .CreateOrderAsync(Refresh, CashDraft());
        Assert.Equal(OtoFailures.Unreadable, res.Failure!.Code);
    }

    [Fact]
    public async Task A_readable_success_is_not_unreadable()
    {
        var res = await Client(new OtoScriptedHandler().ThenToken().Then(HttpStatusCode.OK, "{\"otoId\":\"7\"}"))
            .CreateOrderAsync(Refresh, CashDraft());
        Assert.Equal("7", res.Value!.OtoId);
    }

    /// <summary>
    /// <para><b>الرَمزانِ لا يَظهَرانِ في عُنوانٍ ولا في سَطرِ لوغ</b> — رَمزُ التَحديثِ في جِسمِ
    /// طَلَبِ الرَمزِ وَحدَه، ورَمزُ الوُصولِ في رَأسِ <c>Authorization</c> وَحدَه. يُقاسُ عَلى
    /// كُلِّ الطَلَباتِ والسُطورِ في مَسارِ نَجاحٍ ومَسارَي فَشَل.</para>
    /// </summary>
    [Fact]
    public async Task Neither_token_ever_appears_in_a_url_or_a_log_line()
    {
        var log = new OtoCapturingLogger();
        var handler = new OtoScriptedHandler()
            .ThenToken("ACCESS-SECRET-9")
            .Then(HttpStatusCode.OK, "{\"success\":true,\"otoId\":1}")
            .Then(HttpStatusCode.BadRequest, "{\"success\":false,\"otoErrorCode\":\"OTO1001\"}")
            .Then(HttpStatusCode.InternalServerError, "{}");
        var client = Client(handler, log: log);

        await client.CreateOrderAsync(Refresh, CashDraft());
        await client.CreateOrderAsync(Refresh, CashDraft());
        await client.GetOrderStatusAsync(Refresh, "o");

        Assert.True(handler.Requests.Count >= 4, $"أَداة عَمياء: {handler.Requests.Count} طَلَباً.");
        Assert.True(log.Lines.Count >= 2, $"أَداة عَمياء: {log.Lines.Count} سَطرَ لوغ.");
        foreach (var r in handler.Requests)
        {
            Assert.DoesNotContain(Refresh, r.RequestUri!.ToString());
            Assert.DoesNotContain("ACCESS-SECRET-9", r.RequestUri!.ToString());
        }
        foreach (var line in log.Lines)
        {
            Assert.DoesNotContain(Refresh, line);
            Assert.DoesNotContain("ACCESS-SECRET-9", line);
        }
        // والرَمزُ لا يُرسَلُ إلّا إلى مَسارِ الرَمز.
        Assert.Equal(1, handler.Bodies.Count(b => b is not null && b.Contains(Refresh, StringComparison.Ordinal)));
    }

    /// <summary>ومِفتاحُ المَخزَنِ تَجزِئَةُ الرَمزِ لا الرَمزُ نَفسُه.</summary>
    [Fact]
    public void The_cache_key_is_a_hash_and_not_the_refresh_token()
    {
        var key = OtoTokenCache.KeyOf(Refresh);
        Assert.DoesNotContain(Refresh, key);
        Assert.Equal(64, key.Length);
        Assert.Equal(key, OtoTokenCache.KeyOf(Refresh));
        Assert.NotEqual(key, OtoTokenCache.KeyOf(Refresh + "x"));
    }

    // ─── تَسجيلُ الوارِد — «Webhook › webhook» (تَيّارُ S8، ‏2026-10-12؛ ‏ADR-033 الخُطوَة 7) ─────────────────────
    //
    // الرُدودُ بِأَمثِلَةِ الوَثيقَةِ حَرفاً: «‏{"success": true, "id": "59", "message": "webhook has been created"}» لِلإنشاء،
    // و«‏{"success": true, "message": "webhook has been updated"}» لِلتَحديث.

    private const string InboundUrl = "https://wasayel.example/theme-demo/shipping/oto/order-status";
    private const string IssuedSecret = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    [Fact]
    public async Task Registering_the_inbound_posts_the_documented_body_with_the_vendor_bearer_and_returns_the_new_id()
    {
        var handler = new OtoScriptedHandler()
            .ThenToken("ACCESS-VENDOR")
            .Then(HttpStatusCode.OK, "{\"success\": true, \"id\": \"59\", \"message\": \"webhook has been created\"}");

        var res = await Client(handler).RegisterWebhookAsync(Refresh, InboundUrl, IssuedSecret);

        Assert.True(res.Ok, res.Failure?.Code);
        Assert.Equal("59", res.Value);
        Assert.Contains(Refresh, handler.Bodies[0]!, StringComparison.Ordinal);
        var req = handler.Requests[1];
        Assert.Equal(HttpMethod.Post, req.Method);
        Assert.Equal(OtoEnvironment.StagingBaseUrl + OtoClient.WebhookPath, req.RequestUri!.ToString());
        Assert.Equal("Bearer ACCESS-VENDOR", req.Headers.Authorization!.ToString());
        var body = BodyOf(handler, 1);
        Assert.Equal(new[] { "method", "url", "secretKey", "webhookType" },
            body.EnumerateObject().Select(p => p.Name).ToArray());
        Assert.Equal("post", body.GetProperty("method").GetString());
        Assert.Equal(InboundUrl, body.GetProperty("url").GetString());
        Assert.Equal(IssuedSecret, body.GetProperty("secretKey").GetString());
        Assert.Equal(OtoClient.OrderStatusWebhookType, body.GetProperty("webhookType").GetString());
        Assert.Equal("orderStatus", OtoClient.OrderStatusWebhookType);
        Assert.Equal("/rest/v2/webhook", OtoClient.WebhookPath);
    }

    [Fact]
    public async Task Re_registering_puts_the_same_body_with_the_existing_id_and_keeps_it()
    {
        var handler = new OtoScriptedHandler()
            .ThenToken()
            .Then(HttpStatusCode.OK, "{\"success\": true, \"message\": \"webhook has been updated\"}");

        var res = await Client(handler).RegisterWebhookAsync(Refresh, InboundUrl, IssuedSecret, existingId: "59");

        Assert.True(res.Ok, res.Failure?.Code);
        Assert.Equal("59", res.Value);
        Assert.Equal(HttpMethod.Put, handler.Requests[1].Method);
        Assert.Equal(OtoEnvironment.StagingBaseUrl + OtoClient.WebhookPath, handler.Requests[1].RequestUri!.ToString());
        var body = BodyOf(handler, 1);
        Assert.Equal("59", body.GetProperty("id").GetString());
        Assert.Equal(IssuedSecret, body.GetProperty("secretKey").GetString());
        Assert.Equal(InboundUrl, body.GetProperty("url").GetString());
    }

    /// <summary>إنشاءٌ يَنجَحُ بِلا <c>id</c> لا يُقرَأ — فَلا يُحفَظُ تَسجيلٌ لا مُعَرِّفَ لَه؛ ورَفضٌ بِشَكلِ الوَثيقَةِ <c>oto_refused</c>؛ ولا شَيءَ
    /// يُرسَلُ بِلا بيئَة. ولا يَظهَرُ السِرُّ المُصدَرُ في سَطرِ لوغ.</summary>
    [Fact]
    public async Task A_registration_without_an_id_is_unreadable_a_refusal_is_refused_and_the_secret_never_reaches_a_log()
    {
        var log = new OtoCapturingLogger();
        var handler = new OtoScriptedHandler()
            .ThenToken()
            .Then(HttpStatusCode.OK, "{\"success\": true, \"message\": \"webhook has been created\"}")
            .Then(HttpStatusCode.BadRequest, "{\"success\": false, \"otoErrorCode\": \"OTO1001\"}");
        var client = Client(handler, log: log);

        Assert.Equal(OtoFailures.Unreadable, (await client.RegisterWebhookAsync(Refresh, InboundUrl, IssuedSecret)).Failure!.Code);
        Assert.Equal(OtoFailures.Refused, (await client.RegisterWebhookAsync(Refresh, InboundUrl, IssuedSecret, "59")).Failure!.Code);
        Assert.Equal(OtoFailures.NotConfigured,
            (await Client(handler, Opts("")).RegisterWebhookAsync(Refresh, InboundUrl, IssuedSecret)).Failure!.Code);

        Assert.Equal(3, handler.Requests.Count);
        Assert.True(log.Lines.Count >= 1, $"أَداة عَمياء: {log.Lines.Count} سَطرَ لوغ.");
        Assert.All(log.Lines, l => Assert.DoesNotContain(IssuedSecret, l, StringComparison.Ordinal));
    }

    // ─── تَوقيعُ الوارِد — ‏HmacSHA256 عَلى «orderId:status:timestamp» بِـBase64 (تَيّارُ S8، ‏2026-10-12) ─────────────

    /// <summary>
    /// <para><b>قيَمٌ مَعروفَةُ الجَواب</b> — بَصمَةٌ حُسِبَت خارِجَ الكودِ (‏Python ‏<c>hmac.new(b"key1234", b"1234:delivered:1595941360328",
    /// sha256)</c> ثُمَّ ‏Base64) عَلى مُعَرِّفِ مِثالِ الوَثيقَةِ (<c>"orderId": "1234"</c>) وخَتمِه وكَلِمَةِ حالِه. فَإن انقَلَبَ تَرتيبُ الأَجزاءِ أَو
    /// الفاصِلُ أَو الخوارِزمِيَّةُ أَو التَرميزُ احمَرَّ الصَفّ.</para>
    /// </summary>
    [Theory]
    [InlineData("1234", "delivered", "WzRX4VySGrlFmZMgRQx54Dz0mf3Nns6Tzmq/kUC5x7g=")]
    [InlineData("1234", "outForDelivery", "ULY/YD5qLyeU8xMLuHmmc4YxBcufrjAg17AMIBQxs4k=")]
    public void The_signature_is_base64_hmac_sha256_over_orderId_status_timestamp(string orderId, string status, string expected)
    {
        Assert.Equal("1234:" + status + ":1595941360328", OtoWebhookSignature.SignedText(orderId, status, "1595941360328"));
        Assert.Equal(expected, OtoWebhookSignature.Compute("key1234", orderId, status, "1595941360328"));
        Assert.True(OtoWebhookSignature.Matches("key1234", orderId, status, "1595941360328", expected));
        Assert.True(OtoWebhookSignature.Matches("key1234", orderId, status, "1595941360328", " " + expected + " "));
    }

    [Theory]
    [InlineData("key1235", "1234", "delivered", "1595941360328")]
    [InlineData("key1234", "1235", "delivered", "1595941360328")]
    [InlineData("key1234", "1234", "Delivered", "1595941360328")]
    [InlineData("key1234", "1234", "delivered", "1595941360329")]
    public void Any_change_in_the_secret_or_the_signed_text_does_not_match(string secret, string orderId, string status, string timestamp)
        => Assert.False(OtoWebhookSignature.Matches(secret, orderId, status, timestamp, "WzRX4VySGrlFmZMgRQx54Dz0mf3Nns6Tzmq/kUC5x7g="));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not base64 !")]
    [InlineData("WzRX4VySGrlFmZMgRQx54Dz0mf3Nns6Tzmq/kUC5x7g")]
    [InlineData("AAAA")]
    public void A_missing_malformed_or_short_signature_does_not_match_and_never_throws(string? signature)
        => Assert.False(OtoWebhookSignature.Matches("key1234", "1234", "delivered", "1595941360328", signature));

    [Fact]
    public void The_issued_secret_is_32_random_bytes_as_64_lowercase_hex_characters_under_the_255_limit()
    {
        var secrets = Enumerable.Range(0, 16).Select(_ => OtoWebhookSignature.NewSecret()).ToList();
        Assert.Equal(32, OtoWebhookSignature.SecretBytes);
        Assert.All(secrets, x => Assert.Matches("^[0-9a-f]{64}$", x));
        Assert.Equal(secrets.Count, secrets.Distinct(StringComparer.Ordinal).Count());
        Assert.All(secrets, x => Assert.True(x.Length <= 255));
    }

    [Theory]
    [InlineData("1595941360328", 0L)]
    [InlineData("1595941355328", 5000L)]
    [InlineData("1595941365329", 5001L)]
    [InlineData("0", 1595941360328L)]
    public void The_drift_is_absolute_milliseconds_from_now(string timestamp, long drift)
        => Assert.Equal(drift, OtoWebhookSignature.DriftMilliseconds(timestamp, DateTimeOffset.FromUnixTimeMilliseconds(1595941360328)));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("yesterday")]
    [InlineData("-5")]
    [InlineData("+5")]
    [InlineData("1.5")]
    [InlineData(" 15 ")]
    [InlineData("99999999999999999999")]
    public void A_timestamp_that_is_not_plain_digits_has_no_drift(string? timestamp)
        => Assert.Null(OtoWebhookSignature.DriftMilliseconds(timestamp, DateTimeOffset.FromUnixTimeMilliseconds(1595941360328)));

    [Fact]
    public void The_window_is_five_seconds_inclusive()
    {
        Assert.Equal(5, OtoWebhookSignature.ToleranceSeconds);
        Assert.True(OtoWebhookSignature.IsFresh(0));
        Assert.True(OtoWebhookSignature.IsFresh(5000));
        Assert.False(OtoWebhookSignature.IsFresh(5001));
    }
}

// ─── البُرهانُ الحَيّ — عَلى `staging-api.tryoto.com` بِرَمزِ المالِك ────────────────
//
// يَتَخَطّى نَفسَه ما لَم تُضبَط WASAYEL_LIVE_PROOF=1 و`Shipping__Oto__RefreshToken` — رَمزُ
// حِسابِ الاختِبارِ الَّذي يُنشِئُه المالِكُ («ج» في سِجِلِّ الأَسئِلَة). يَطلُبُ رَمزَ وُصولٍ ويُسَعِّرُ
// مَساراً بِأَسعارِ OTO (‏`SHGAP-04`)، والمَدينَتانِ والوَزنُ مِن مِثالِ الوَثيقَةِ نَفسِها
// («‏"weight":"3","originCity":"Riyadh","destinationCity":"Jeddah"») ما لَم يُضبَط غَيرُها. ولا
// يُنشِئُ طَلَباً: البوليصَةُ الواحِدَةُ تُبرهَنُ مِن تَطبيقِ `theme-demo` نَفسِه.

public class LiveOtoStagingProofTests(ITestOutputHelper output)
{
    private static bool Enabled => Environment.GetEnvironmentVariable("WASAYEL_LIVE_PROOF") == "1";

    private static string? Token =>
        Environment.GetEnvironmentVariable(OtoEnvironment.EnvVarName(OtoEnvironment.RefreshTokenKey));

    [Fact]
    public async Task The_staging_token_refreshes_and_prices_a_route_at_oto_rates()
    {
        if (!Enabled || string.IsNullOrWhiteSpace(Token)) return;

        var origin = Environment.GetEnvironmentVariable("OTO_PROOF_ORIGIN") ?? "Riyadh";
        var destination = Environment.GetEnvironmentVariable("OTO_PROOF_DESTINATION") ?? "Jeddah";

        var client = new OtoClient(new HttpClient(), Options.Create(new OtoOptions { Environment = OtoEnvironment.Staging }),
            new OtoTokenCache(), NullLogger<OtoClient>.Instance);

        var token = await client.AccessTokenAsync(Token!);
        output.WriteLine($"رَمزُ الوُصول: {(token.Ok ? "صادِر" : token.Failure!.Code)}");
        Assert.True(token.Ok, token.Failure?.Code);

        var fee = await client.CheckOtoDeliveryFeeAsync(Token!, new OtoFeeQuery(origin, destination, 3m));
        Assert.True(fee.Ok, fee.Failure?.Code);
        output.WriteLine($"{origin} ← {destination} · ‏3 كغ · ‏{fee.Value!.Count} خِياراً");
        foreach (var o in fee.Value)
            output.WriteLine($"  {o.DeliveryOptionId} · {o.Name} · {o.Price} {o.Currency}");
    }
}
