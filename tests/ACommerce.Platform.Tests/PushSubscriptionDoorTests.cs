using System.Security.Cryptography;
using System.Text.RegularExpressions;
using ACommerce.Kit.Auth;
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

    // ─── ١. البابُ لا يَرُدُّ إلّا الفارِغ ──────────────────────────────────────────────────────────────

    [Fact]
    public void Today_the_subscribe_door_refuses_only_an_empty_endpoint()
    {
        var body = SubscribeBody();
        Assert.Single(Regex.Matches(body, @"Results\.BadRequest\("));
        Assert.Contains("if (string.IsNullOrEmpty(endpoint)) return Results.BadRequest();", body, StringComparison.Ordinal);
        Assert.Contains("root.GetProperty(\"endpoint\")", body, StringComparison.Ordinal);
        Assert.DoesNotContain("https", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Uri", body, StringComparison.Ordinal);
    }

    // ─── ٢. القائِمَةُ بِلا حَدّ ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Today_the_subscribe_door_adds_a_row_with_no_bound_on_the_users_list()
    {
        var body = SubscribeBody();
        Assert.Contains("user.PushSubscriptions.RemoveAll(p => p.Endpoint == endpoint);", body, StringComparison.Ordinal);
        Assert.Contains("user.PushSubscriptions.Add(", body, StringComparison.Ordinal);
        Assert.DoesNotContain(".Count", body, StringComparison.Ordinal);
    }

    // ─── ٣. المُرسِلُ يَمُرُّ بِكُلِّ صَفٍّ مَحفوظ ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Today_the_sender_keeps_a_stored_endpoint_that_no_push_service_serves()
    {
        var sender = SenderSource();
        Assert.Matches(@"foreach \(var sub in user\.PushSubscriptions\)\s*\{\s*try\s*\{\s*var ps = new WebPush\.PushSubscription", sender);

        // مَفتاحانِ غَيرُ صالِحَينِ عَمداً: التَشفيرُ يَرمي قَبلَ أَيِّ طَلَبٍ فَلا يَخرُجُ مِن الاختِبارِ شَيء — والمُرسِلُ يَبتَلِعُ ويُبقي الصَفّ.
        var world = new DocWorld();
        var user = new User { Id = Guid.NewGuid(), TenantSlug = "theme-demo" };
        user.PushSubscriptions.Add(new ACommerce.Kit.Auth.PushSubscription { Endpoint = "http://169.254.169.254/latest/meta-data", P256dh = "x", Auth = "y" });
        world.Put(user);

        await NewSender().SendAsync(world.Store, "theme-demo", user.Id, "t", "b");

        Assert.Single(world.Read<User>(user.Id.ToString())!.PushSubscriptions);
        Assert.Equal(0, world.SaveCalls);
    }
}
