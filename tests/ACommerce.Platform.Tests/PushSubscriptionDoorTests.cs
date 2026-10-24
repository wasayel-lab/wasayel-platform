using System.Reflection;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using ACommerce.Kit.Auth;
using ACommerce.Kit.Auth.Server;
using ACommerce.Templates.Customer.Marketplace.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using WebPush;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ تَوصيفُ «اليَوم» — اشتِراكُ الإشعاراتِ يَحفَظُ أَيَّ عُنوانِ نِهايَةٍ بِلا حَدّ، والخادِمُ يُرسِلُ POST إلى كُلِّها (تَدقيقُ الإطلاق lens-security-12) ═══
//
// **المَقيس** (‏2026-10-24، عِندَ `1d7483a`):
//   ١. `POST /api/{slug}/push/subscribe` بَعدَ رَمزِ الجَلسَةِ لا تَرُدُّ إلّا عُنواناً فارِغاً (`string.IsNullOrEmpty(endpoint)`): ‏`http://`
//      وعُنوانُ IP وأَيُّ مُضيفٍ يُحفَظ؛ والمَفتاحانِ لا يُفحَصان؛ وحَقلٌ ناقِصٌ يَرمي `KeyNotFoundException` (‏500) لا ‏400.
//   ٢. ‏`user.PushSubscriptions.Add(…)` بِلا حَدٍّ عَلى طولِ القائِمَة — كُلُّ عُنوانٍ جَديدٍ صَفٌّ جَديدٌ في وَثيقَةِ المُستَخدِم.
//   ٣. ‏`WebPushService.SendAsync` يَمُرُّ بِكُلِّ صَفٍّ مَحفوظٍ إلى `SendNotificationAsync` بِلا سُؤال — والمَكتَبَةُ تَبني `POST` إلى العُنوانِ
//      كَما هو (الصَفُّ الدائِمُ أَدناه). فَمُستَخدِمٌ مُوَثَّقٌ واحِدٌ يَجعَلُ الخادِمَ يَطرُقُ ما شاءَ مِن داخِلِ شَبَكَتِه عِندَ كُلِّ إشعار.
//
// **يُثَبَّتُ قَبلَ العِلاجِ** (القاعِدَة ٣): الصُفوفُ الثَلاثَةُ `Today_…` تَنقَلِبُ بِأَسمائِها، والصَفُّ الدائِمُ يَبقى حَرفاً.
//
// **العِلاج** (‏2026-10-24): `Services.PushSubscriptionDoor` قَرارٌ واحِدٌ بِمُستَهلِكَين — البابُ يَقرَأُ بِه الجِسمَ (‏https بِمَنفَذِه الافتِراضِيِّ عَلى
// مُضيفي خِدماتِ الإشعار، ومَفتاحا المُتَصَفِّحِ بِطولَيهِما؛ وما سِواه وحَقلٌ ناقِصٌ ‏400) قَبلَ أَن يَقرَأَ الوَثيقَة، و`Keep` يُبقي أَحدَثَ
// ‏`MaxPerUser`؛ والمُرسِلُ لا يَطرُقُ صَفّاً لا يَقبَلُه ويَمحوه كَالمُنتَهي. الصُفوفُ الثَلاثَةُ انقَلَبَت بِأَسمائِها، والدائِمُ كَما أُودِع.
public class PushSubscriptionDoorTests(ITestOutputHelper output)
{
    private const string Route = "/api/{slug}/push/subscribe";
    private const string Tpl = "ACommerce.Templates.Customer.Marketplace";

    internal static string SubscribeBody()
    {
        var doors = WriteEndpointGuardTests.AllMinimalApiEndpoints().Where(e => e.Route == Route).ToList();
        Assert.True(doors.Count == 1, $"أَبوابُ الاشتِراك: {doors.Count} — الأَداةُ عَمياء.");
        return doors[0].Body;
    }

    internal static string SenderSource() => WriteEndpointGuardTests.StripComments(
        StudioConsentCharacterizationTests.Source("libs", "templates", Tpl, "Services", "WebPushService.cs"));

    private static string B64Url(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>مَفتاحا اشتِراكٍ صالِحانِ كَما يُعطيهِما المُتَصَفِّح: نُقطَةُ P-256 غَيرُ مَضغوطَةٍ (‏65 بايتاً تَبدَأُ بِـ<c>0x04</c>) وسِرُّ ‏16 بايتاً.</summary>
    internal static (string P256dh, string Auth) BrowserKeys()
    {
        using var ecdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var q = ecdh.ExportParameters(false).Q;
        return (B64Url([0x04, .. q.X!, .. q.Y!]), B64Url(RandomNumberGenerator.GetBytes(16)));
    }

    internal static WebPushService NewSender()
        => new(new ConfigurationBuilder().Build(), NullLogger<WebPushService>.Instance);

    // ─── الصَفُّ الدائِم: المَكتَبَةُ تَبني POST إلى أَيِّ عُنوانٍ يَحمِلُه الاشتِراك — لا يَتَغَيَّرُ بِالعِلاج ───────────────

    [Fact]
    public void The_push_library_builds_a_post_to_whatever_endpoint_the_subscription_carries()
    {
        var (p256dh, auth) = BrowserKeys();
        var keys = VapidHelper.GenerateVapidKeys();
        var vapid = new VapidDetails("mailto:push-test@example.invalid", keys.PublicKey, keys.PrivateKey);
        var client = new WebPushClient();
        string[] endpoints = ["http://169.254.169.254/latest/meta-data", "https://10.0.0.7:8443/hook", "https://internal.example.invalid/x"];
        foreach (var endpoint in endpoints)
        {
            using var req = client.GenerateRequestDetails(new WebPush.PushSubscription(endpoint, p256dh, auth), "{}",
                new Dictionary<string, object> { ["vapidDetails"] = vapid });
            Assert.Equal(HttpMethod.Post, req.Method);
            Assert.Equal(new Uri(endpoint), req.RequestUri);
        }
        output.WriteLine($"بَنَت المَكتَبَةُ POST إلى {endpoints.Length} عَناوينَ لَيسَت خِدمَةَ إشعارٍ — بِلا سُؤال.");
    }

    // ─── ١. البابُ يَقبَلُ https عَلى مُضيفي خِدماتِ الإشعارِ بِمَفتاحَي المُتَصَفِّح، ويُقَرِّرُ قَبلَ أَن يَقرَأ ─────────────
    //
    // انقَلَبَ بِاسمِه (‏2026-10-24) مِن `Today_the_subscribe_door_refuses_only_an_empty_endpoint`.

    public static TheoryData<string> PushServiceEndpoints => new()
    {
        "https://fcm.googleapis.com/fcm/send/dQw4w9WgXcQ:APA91bH",
        "https://fcm.googleapis.com/wp/dQw4w9WgXcQ",
        "https://updates.push.services.mozilla.com/wpush/v2/gAAAAABk",
        "https://web.push.apple.com/QGxhbGxvd29ybGQ",
        "https://wns2-par02p.notify.windows.com/w/?token=BQYAAAB",
        "https://FCM.googleapis.com:443/fcm/send/x",
    };

    public static TheoryData<string?> RefusedEndpoints => new()
    {
        null, "", "   ", "/relative/path", "javascript:alert(1)",
        "http://fcm.googleapis.com/fcm/send/x",
        "https://fcm.googleapis.com:8443/fcm/send/x",
        "https://user:pw@fcm.googleapis.com/fcm/send/x",
        "https://169.254.169.254/latest/meta-data",
        "https://10.0.0.7/hook",
        "https://[::1]/x",
        "https://localhost/x",
        "https://internal.example.invalid/hook",
        "https://fcm.googleapis.com.evil.example/x",
        "https://evilpush.apple.com/x",
        "https://push.apple.com/x",
    };

    [Theory]
    [MemberData(nameof(PushServiceEndpoints))]
    public void A_push_service_endpoint_is_admitted(string endpoint)
        => Assert.True(PushSubscriptionDoor.IsPushServiceEndpoint(endpoint), endpoint);

    [Theory]
    [MemberData(nameof(RefusedEndpoints))]
    public void An_endpoint_no_push_service_serves_is_refused(string? endpoint)
        => Assert.False(PushSubscriptionDoor.IsPushServiceEndpoint(endpoint), endpoint);

    private static MemoryStream Json(string s) => new(System.Text.Encoding.UTF8.GetBytes(s));

    [Fact]
    public async Task The_subscribe_door_admits_only_a_push_service_endpoint_with_browser_keys()
    {
        var (p256dh, auth) = BrowserKeys();
        var now = new DateTime(2026, 10, 24, 12, 0, 0, DateTimeKind.Utc);
        string Body(string endpoint, string? p = null, string? a = null) => System.Text.Json.JsonSerializer.Serialize(
            new { endpoint, expirationTime = (object?)null, keys = new { p256dh = p ?? p256dh, auth = a ?? auth } });

        var ok = await PushSubscriptionDoor.ReadAsync(Json(Body("https://fcm.googleapis.com/fcm/send/abc")), now);
        Assert.NotNull(ok);
        Assert.Equal(("https://fcm.googleapis.com/fcm/send/abc", p256dh, auth, now), (ok!.Endpoint, ok.P256dh, ok.Auth, ok.CreatedAt));

        string[] refused =
        [
            Body("http://169.254.169.254/latest/meta-data"),
            Body("https://internal.example.invalid/hook"),
            Body("https://fcm.googleapis.com/fcm/send/abc", p: auth),
            Body("https://fcm.googleapis.com/fcm/send/abc", a: p256dh),
            Body("https://fcm.googleapis.com/fcm/send/abc", p: B64Url([0x05, .. new byte[64]])),
            Body("https://fcm.googleapis.com/fcm/send/abc", p: "not base64!"),
            "{\"endpoint\":\"https://fcm.googleapis.com/fcm/send/abc\"}",
            "{\"keys\":{}}",
            "{\"endpoint\":7,\"keys\":{\"p256dh\":1,\"auth\":2}}",
            "[]", "null", "not json",
        ];
        foreach (var json in refused)
            Assert.Null(await PushSubscriptionDoor.ReadAsync(Json(json), now));
        output.WriteLine($"قُبِلَ 1 · رُدَّ {refused.Length} جِسماً (وكانَ ثَلاثَةٌ مِنها ‏500، والباقي يُحفَظ)");

        // والنُقطَة: الرَمزُ، ثُمَّ القَرار، ثُمَّ الرَدّ، ثُمَّ قِراءَةُ الوَثيقَة، ثُمَّ الوَضع — ولا قِراءَةَ لِحَقلٍ في الجِسم.
        var body = SubscribeBody();
        Assert.True(DecidesBeforeItLoads(body), body);
        Assert.DoesNotContain("GetProperty(", body, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(body, @"Results\.BadRequest\("));
        // عَيِّنَةٌ مَحقونَة (القاعِدَة ١٠): الفاحِصُ يَحمَرُّ عَلى سَطرِ اليَومِ حَرفاً.
        Assert.False(DecidesBeforeItLoads(TodayBodySample));
    }

    private static bool DecidesBeforeItLoads(string body)
    {
        var token = body.IndexOf("AuthSession.ResolveToken(", StringComparison.Ordinal);
        var door = body.IndexOf("Services.PushSubscriptionDoor.ReadAsync(req.Body", StringComparison.Ordinal);
        var refuse = body.IndexOf("if (sub is null) return Results.BadRequest();", StringComparison.Ordinal);
        var load = body.IndexOf("LoadAsync<User>(", StringComparison.Ordinal);
        var keep = body.IndexOf("Services.PushSubscriptionDoor.Keep(user.PushSubscriptions, sub);", StringComparison.Ordinal);
        return token >= 0 && door > token && refuse > door && load > refuse && keep > load;
    }

    private const string TodayBodySample = """
        var token = AuthSession.ResolveToken(req, slug);
        var endpoint = root.GetProperty("endpoint").GetString() ?? "";
        if (string.IsNullOrEmpty(endpoint)) return Results.BadRequest();
        var user = await s.LoadAsync<User>(userId);
        user.PushSubscriptions.Add(new ACommerce.Kit.Auth.PushSubscription
        """;

    // ─── ٢. القائِمَةُ بِحَدّ: أَحدَثُ عَشرَة، والجِهازُ نَفسُه يَحِلُّ مَحَلَّ صَفِّه ──────────────────────────
    //
    // انقَلَبَ بِاسمِه (‏2026-10-24) مِن `Today_the_subscribe_door_adds_a_row_with_no_bound_on_the_users_list`.

    [Fact]
    public void The_users_list_keeps_the_newest_ten_and_a_resubscribe_replaces_its_own_row()
    {
        // العَدَدُ حِصَّةُ السّاعَةِ في عُدَّةِ الدُخولِ نَفسُها — لا رَقَمٌ جَديد («ب» W8-2).
        var hourly = (int)typeof(AuthHandlers).GetField("MaxRequestPerHour", BindingFlags.NonPublic | BindingFlags.Static)!.GetRawConstantValue()!;
        Assert.Equal(hourly, PushSubscriptionDoor.MaxPerUser);

        static ACommerce.Kit.Auth.PushSubscription Row(int i, int minute) =>
            new() { Endpoint = $"https://fcm.googleapis.com/fcm/send/{i}", CreatedAt = DateTime.UnixEpoch.AddMinutes(minute) };
        var list = new List<ACommerce.Kit.Auth.PushSubscription>();
        for (var i = 0; i < 25; i++) PushSubscriptionDoor.Keep(list, Row(i, i));
        Assert.Equal(Enumerable.Range(15, 10).Select(i => $"https://fcm.googleapis.com/fcm/send/{i}"), list.Select(p => p.Endpoint));

        // الجِهازُ نَفسُه: يَحِلُّ مَحَلَّ صَفِّه ويَصيرُ الأَحدَث، ولا يُسقِطُ غَيرَه.
        PushSubscriptionDoor.Keep(list, Row(15, 99));
        Assert.Equal(PushSubscriptionDoor.MaxPerUser, list.Count);
        Assert.Equal("https://fcm.googleapis.com/fcm/send/16", list[0].Endpoint);
        Assert.Equal("https://fcm.googleapis.com/fcm/send/15", list[^1].Endpoint);
        Assert.Single(list, p => p.Endpoint.EndsWith("/15", StringComparison.Ordinal));

        // والنُقطَةُ تَضَعُ بِه — ولا مَوضِعَ في المَكتَباتِ يُلحِقُ بِالقائِمَةِ مُباشَرَة.
        Assert.Contains("Services.PushSubscriptionDoor.Keep(user.PushSubscriptions, sub);", SubscribeBody(), StringComparison.Ordinal);
        Assert.Equal(0, ActorNameSourceTests.Matches(@"PushSubscriptions\.Add\("));
        Assert.Matches(@"PushSubscriptions\.Add\(", "user.PushSubscriptions.Add(new ACommerce.Kit.Auth.PushSubscription");
    }

    // ─── ٣. المُرسِلُ لا يَطرُقُ صَفّاً لا يَقبَلُه البابُ ويَمحوه ─────────────────────────────────────────
    //
    // انقَلَبَ بِاسمِه (‏2026-10-24) مِن `Today_the_sender_keeps_a_stored_endpoint_that_no_push_service_serves`.

    [Fact]
    public async Task The_sender_drops_a_stored_endpoint_the_door_would_refuse_without_posting_to_it()
    {
        var sender = SenderSource();
        Assert.Matches(@"foreach \(var sub in user\.PushSubscriptions\)\s*\{\s*if \(!PushSubscriptionDoor\.Admits\(sub\)\) \{ stale\.Add\(sub\); continue; \}\s*try\s*\{\s*var ps = new WebPush\.PushSubscription", sender);

        // صَفٌّ بِمَفتاحَينِ غَيرِ صالِحَين، وصَفٌّ بِمَفتاحَي مُتَصَفِّحٍ صالِحَينِ إلى مُضيفٍ داخِلِيّ — كِلاهُما يُمحى قَبلَ أَيِّ طَلَب.
        var (p256dh, auth) = BrowserKeys();
        var world = new DocWorld();
        var user = new User { Id = Guid.NewGuid(), TenantSlug = "theme-demo" };
        user.PushSubscriptions.Add(new ACommerce.Kit.Auth.PushSubscription { Endpoint = "http://169.254.169.254/latest/meta-data", P256dh = "x", Auth = "y" });
        user.PushSubscriptions.Add(new ACommerce.Kit.Auth.PushSubscription { Endpoint = "https://internal.example.invalid/hook", P256dh = p256dh, Auth = auth });
        world.Put(user);

        await NewSender().SendAsync(world.Store, "theme-demo", user.Id, "t", "b");

        Assert.Empty(world.Read<User>(user.Id.ToString())!.PushSubscriptions);
        Assert.Equal(1, world.SaveCalls);
    }
}
