using System.Net;
using ACommerce.Kit.Delivery;
using ACommerce.Platform.Shared.Metering;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ ما تَقولُه عُدَّةُ OTO عَن كُلِّ شَكلِ جَواب — يُثَبَّتُ عَلى المُستَدعي المَعدودِ قَبلَ أَن يَصيرَ الحُكمُ مِن الجِسم (‏2026-11-13، مُراجَعَةُ المَرحَلَةِ ج الأولى · M6C-CNT-1 — ADR-051 §١٩) ═══
//
// **لِماذا قَبلَ التَبديل** (القاعِدَة ٣): المُستَدعي المَعدودُ يَحكُمُ عَلى نِداءِ OTO بِحالَتِه وَحدَها، والعُدَّةُ تَحكُمُ بِجِسمِه أَيضاً — ‏2xx بِـ<c>"success": false</c>
// عِندَها مَرفوض (<c>OtoClient.PostAsync</c>) وعِندَ العَدِّ مَقبولٌ يُكتَبُ سَطرُه: مَتجَرٌ نَفِدَ رَصيدُه عِندَ OTO (‏OTO1006) يُعَدُّ عَلَيه طَلَبُ شَحنٍ لا وُجودَ لَه.
// والتَبديلُ التالي يَنقُلُ قِراءَةَ الجِسمِ إلى داخِلِ المُستَدعي (الحِملُ ذو القارِئ، <c>ProviderHttp.SendAsync&lt;T&gt;</c>) ويُعيدُ إلى العُدَّةِ جَواباً مَبنِيّاً مِن
// الجِسمِ المَقروء. ووَعدُه أَنّ **ما تَقولُه العُدَّةُ لا يَتَغَيَّر**: لِكُلِّ عَمَلِيَّةٍ مَعدودَةٍ (الإنشاء، والحال، وتَسجيلُ الوارِد، وتَسجيلُ البائِع) ولِكُلِّ شَكلِ
// جَوابٍ — نَجاحٌ، و<c>success:false</c> في ‏200، و‏4xx، و‏5xx، وجِسمٌ لا يُقرَأ، ونَجاحٌ بِلا الحَقلِ المَطلوب، و‏401 ثُمَّ نَجاح — الحُكمُ بِرَمزِه وحالِه ورَمزِ OTO
// وقيمَتُه، وتَرتيبُ الطَلَبات.
//
// **والعُدَّةُ كَما يُرَكِّبُها الإنتاج**: <c>AddOtoShipping</c> ومَعَه كاتِبٌ صادِرٌ مُسَجَّل (فَالنِداءُ يَمُرُّ بِـ<c>InvokeAsync</c> لا بِطَريقِ المُستَدعي الَّذي لا يُسَجِّل)،
// والجَوابُ مِن المُعالِجِ الأَوَّلِ بِـ<c>ConfigureHttpClientDefaults</c>. ولا يُسَمّي المِلَفُّ نَوعاً يولَدُ في التَبديل، ولا يَقيسُ العَدَّ (يَقيسُه <c>ProviderCallsTests</c>
// والصَفُّ الحَيّ): هذا ما لا يَتَغَيَّر، لا ما يَتَغَيَّر. **وبِلا شَبَكَةٍ ولا قاعِدَة.**

public class OtoAnswerVerdictCharacterizationTests(ITestOutputHelper output)
{
    /// <summary>كاتِبٌ صادِرٌ لا يَكتُبُ شَيئاً — وُجودُه وَحدَه يَجعَلُ المُستَدعيَ مُسَجِّلاً، فَيَمُرُّ النِداءُ بِطَريقِ الإنتاج.</summary>
    private sealed class SilentRecorder : OutboundRecorder
    {
        public int Opened, Decided;
        public override Task OpenAsync(ProviderCall call, CancellationToken ct) { Interlocked.Increment(ref Opened); return Task.CompletedTask; }
        public override Task DecideAsync(ProviderCall call, ProviderAcceptance acceptance) { Interlocked.Increment(ref Decided); return Task.CompletedTask; }
    }

    private const string Token = "{\"access_token\":\"ACCESS-1\",\"success\":true,\"token_type\":\"Bearer\",\"expires_in\":\"3600\"}";

    private static readonly ProviderCall Call = ProviderCall.ForChannel(OutboundUnits.DeliveryOrderCreate, "verdict-store", "verdict-request");

    private static (OtoClient Client, OtoScriptedHandler Handler, SilentRecorder Recorder, ServiceProvider Services) Compose()
    {
        var handler = new OtoScriptedHandler();
        var recorder = new SilentRecorder();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [OtoEnvironment.EnvironmentKey] = OtoEnvironment.Staging,
            [OtoEnvironment.RefreshTokenKey] = DealShipTests.ConfigToken,
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<OutboundRecorder>(recorder);
        services.ConfigureHttpClientDefaults(b => b.ConfigurePrimaryHttpMessageHandler(() => handler));
        services.AddOtoShipping(config);
        var sp = services.BuildServiceProvider();
        Assert.True(sp.GetRequiredService<ProviderHttp<OtoClient>>().IsRecording, "العُدَّةُ لا تَمُرُّ بِمُستَدعٍ مُسَجِّل — الصَفُّ لا يَقيسُ طَريقَ الإنتاج.");
        return (sp.GetRequiredService<OtoClient>(), handler, recorder, sp);
    }

    private static readonly OtoOrderDraft Draft = new("0f8fad5bd9cb469fa165705ed7eb6e2a", OtoClient.CashOnDelivery, 150m, 150m, "SAR",
        new OtoCustomer("مُشتَرٍ", "0500000000", "حَيُّ المِثال", "Riyadh", "SA"), "سِلعَة");

    private static readonly OtoVendorRegistration Vendor = new("SA", "Riyadh", "vendor@example.test", "0500000001", "بائِع", "المِثال", false, "حَيُّ المِثال");

    /// <summary>الحُكمُ سَطراً واحِداً: نَجَحَ أَم لا، ورَمزُ الفَشَلِ وحالُه ورَمزُ OTO، والقيمَة.</summary>
    private static string Verdict<T>(OtoResult<T> r, Func<T, string> value)
        => r.Ok ? $"ok · {value(r.Value!)}" : $"fail · {r.Failure!.Code} · {r.Failure.HttpStatus?.ToString() ?? "—"} · {r.Failure.OtoErrorCode ?? "—"}";

    private static async Task<string> RunAsync(OtoClient client, string operation) => operation switch
    {
        "create" => Verdict(await client.CreateOrderAsync(Call, DealShipTests.VendorToken, Draft), v => $"{v.OrderId}/{v.OtoId}"),
        "status" => Verdict(await client.GetOrderStatusAsync(Call, DealShipTests.VendorToken, Draft.OrderId), v => $"{v.Status}/{v.OtoId}"),
        "webhook" => Verdict(await client.RegisterWebhookAsync(Call, DealShipTests.VendorToken, "https://wasayel.example/x/shipping/oto/order-status", "secret-1"), v => v),
        "webhook-put" => Verdict(await client.RegisterWebhookAsync(Call, DealShipTests.VendorToken, "https://wasayel.example/x/shipping/oto/order-status", "secret-1", "59"), v => v),
        "register" => Verdict(await client.RegisterVendorAsync(Call, "MKT-token", Vendor), v => $"{v.RefreshToken}/{v.ActivationLink}"),
        _ => throw new ArgumentOutOfRangeException(nameof(operation)),
    };

    /// <summary>
    /// <para><b>الحُكمُ لِكُلِّ عَمَلِيَّةٍ مَعدودَةٍ ولِكُلِّ شَكلِ جَواب</b> — الرَمزُ يُبادَلُ أَوَّلاً ثُمَّ العَمَلِيَّة؛ والحُكمُ والمَساراتُ بِتَرتيبِها كَما هِيَ اليَوم.
    /// ومَعَه: كُلُّ عَمَلِيَّةٍ نِيَّةٌ واحِدَةٌ وحُكمٌ واحِدٌ عِندَ الكاتِب (الطَريقُ مُسَجِّل)، لا ما يَحكُمُ بِه.</para>
    /// </summary>
    [Theory]
    // الإنشاء
    [InlineData("create", 200, "{\"success\":true,\"otoId\":540789}", "ok · 0f8fad5bd9cb469fa165705ed7eb6e2a/540789")]
    [InlineData("create", 200, "{\"success\":false,\"otoErrorCode\":\"OTO1006\",\"otoErrorMessage\":\"Credit is not enough\"}", "fail · oto_refused · 200 · OTO1006")]
    [InlineData("create", 400, "{\"success\":false,\"otoErrorCode\":\"OTO1001\"}", "fail · oto_refused · 400 · OTO1001")]
    [InlineData("create", 503, "<html/>", "fail · oto_unreachable · 503 · —")]
    [InlineData("create", 200, "not json", "fail · oto_unreadable · 200 · —")]
    [InlineData("create", 200, "{\"success\":true}", "fail · oto_unreadable · — · —")]
    // الحال
    [InlineData("status", 200, "{\"success\":true,\"orderId\":\"0f8fad5bd9cb469fa165705ed7eb6e2a\",\"otoId\":\"540789\",\"status\":\"searchingDriver\"}", "ok · searchingDriver/540789")]
    [InlineData("status", 200, "{\"success\":false,\"otoErrorCode\":\"OTO1002\"}", "fail · oto_refused · 200 · OTO1002")]
    [InlineData("status", 404, "{\"success\":false}", "fail · oto_refused · 404 · —")]
    // تَسجيلُ الوارِد — الإنشاءُ والتَحديث
    [InlineData("webhook", 200, "{\"success\": true, \"id\": \"59\", \"message\": \"webhook has been created\"}", "ok · 59")]
    [InlineData("webhook", 200, "{\"success\": false, \"message\": \"invalid url\"}", "fail · oto_refused · 200 · —")]
    [InlineData("webhook-put", 200, "{\"success\": true, \"message\": \"webhook has been updated\"}", "ok · 59")]
    [InlineData("webhook-put", 200, "{\"success\": false, \"otoErrorCode\": \"OTO1010\"}", "fail · oto_refused · 200 · OTO1010")]
    // تَسجيلُ البائِع
    [InlineData("register", 200, "{\"success\": true, \"activationLink\": \"https://login.tryoto.com/XYZ\", \"refreshToken\": \"_refreshToken_\"}", "ok · _refreshToken_/https://login.tryoto.com/XYZ")]
    [InlineData("register", 200, "{\"success\": false, \"otoErrorCode\": \"OTO1003\"}", "fail · oto_refused · 200 · OTO1003")]
    public async Task Each_counted_operation_says_the_same_for_each_answer_shape(string operation, int status, string body, string expected)
    {
        var (client, handler, recorder, sp) = Compose();
        await using var _ = sp;
        handler.Then(HttpStatusCode.OK, Token).Then((HttpStatusCode)status, body);

        var verdict = await RunAsync(client, operation);
        var paths = string.Join(" → ", handler.Requests.Select(r => $"{r.Method} {r.RequestUri!.AbsolutePath}"));
        output.WriteLine($"{operation} {status} {body} ⇒ {verdict} · {paths} · نِيّات {recorder.Opened} · أَحكام {recorder.Decided}");
        Assert.Equal(expected, verdict);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(OtoClient.RefreshTokenPath, handler.Requests[0].RequestUri!.AbsolutePath);
        Assert.Equal((1, 1), (recorder.Opened, recorder.Decided));
    }

    /// <summary><b>‏401 يُسقِطُ الرَمزَ ويُعيدُ المُبادَلَةَ والعَمَلِيَّةَ مَرَّةً واحِدَة</b> — بِالنِداءِ نَفسِه: الحُكمُ الثاني هُوَ الحُكم؛ و‏401 مَرَّتَينِ مَرفوض.</summary>
    [Theory]
    [InlineData(200, "{\"success\":true,\"otoId\":540789}", "ok · 0f8fad5bd9cb469fa165705ed7eb6e2a/540789")]
    [InlineData(200, "{\"success\":false,\"otoErrorCode\":\"OTO1006\"}", "fail · oto_refused · 200 · OTO1006")]
    [InlineData(401, "{\"success\":false}", "fail · oto_refused · 401 · —")]
    public async Task A_401_refreshes_the_token_once_and_the_second_answer_is_the_verdict(int second, string body, string expected)
    {
        var (client, handler, recorder, sp) = Compose();
        await using var _ = sp;
        handler.Then(HttpStatusCode.OK, Token).Then(HttpStatusCode.Unauthorized, "{\"success\":false}")
               .Then(HttpStatusCode.OK, Token).Then((HttpStatusCode)second, body);

        var verdict = await RunAsync(client, "create");
        var paths = string.Join(" → ", handler.Requests.Select(r => r.RequestUri!.AbsolutePath));
        output.WriteLine($"401 ثُمَّ {second} {body} ⇒ {verdict} · {paths} · نِيّات {recorder.Opened} · أَحكام {recorder.Decided}");
        Assert.Equal(expected, verdict);
        Assert.Equal(new[] { OtoClient.RefreshTokenPath, OtoClient.CreateOrderPath, OtoClient.RefreshTokenPath, OtoClient.CreateOrderPath },
                     handler.Requests.Select(r => r.RequestUri!.AbsolutePath));
        Assert.Equal((2, 2), (recorder.Opened, recorder.Decided));
    }
}
