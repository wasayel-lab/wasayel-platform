using ACommerce.Templates.Customer.Marketplace.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ تَوصيفُ «اليَوم» — مَفاتيحُ VAPID إعدادٌ إنتاجيٌّ لا يُسَمّيه دَليلُ النَشر، وبِدونِها تَكسِرُ كُلُّ إعادَةِ تَشغيلٍ الاشتِراكاتِ القائِمَة (تَدقيقُ الإطلاق lens-docs-vs-code-1) ═══
//
// **المَقيس** (‏2026-10-25، عِندَ `2d20144`):
//   ١. ‏`WebPushService` يَقرَأُ `WebPush:Subject/PublicKey/PrivateKey`؛ وغِيابُ المِفتاحَينِ يُوَلِّدُ زَوجاً **في كُلِّ إنشاء** — والخِدمَةُ
//      `AddSingleton` فَإنشاءٌ واحِدٌ في كُلِّ عَمَلِيَّة: كُلُّ إقلاعٍ مِفتاحٌ عامٌّ جَديدٌ يُقَدِّمُه `/api/push/vapid-key`.
//   ٢. والمُتَصَفِّحُ لا يَشتَرِكُ مِن جَديد: `subscribeToPush` في `App.razor` يَأخُذُ `getSubscription()` القائِمَ (المَصنوعَ بِالمِفتاحِ السابِق)
//      ويُعيدُ إرسالَه؛ والمُرسِلُ لا يَمحو صَفّاً إلّا عَلى ‏404/410 — فَيَبقى الصَفُّ ولا يَصِلُ إشعارٌ، ولا يَقولُ ذلكَ شَيء.
//   ٣. وسَطرُ التَحذيرِ يَنصَحُ بِحِفظِها في `appsettings.Local.json` — **ولا يَقرَأُ المُضيفُ ذلكَ المِلَفّ** (لا `AddJsonFile` في
//      `Program.cs`)، ولا يَطبَعُ المِفتاحَ الخاصَّ فَلا يُحفَظُ الزَوجُ أَصلاً. والمُهَيَّأُ لا يَكتُبُ سَطراً.
//
// **يُثَبَّتُ قَبلَ العِلاجِ** (القاعِدَة ٣): الصَفّانِ `Today_…` يَنقَلِبانِ بِأَسمائِهِما، والثَلاثَةُ الدائِمَةُ تَبقى حَرفاً.
public class WebPushVapidKeysTests(ITestOutputHelper output)
{
    private static string RepoRoot => ThemeZeroEquivalenceTests.RepoRoot;

    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine(new[] { RepoRoot }.Concat(parts).ToArray()));

    private const string Tpl = "ACommerce.Templates.Customer.Marketplace";

    internal sealed class CapturingLogger : ILogger<WebPushService>
    {
        public List<(LogLevel Level, string Text)> Lines { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Lines.Add((logLevel, formatter(state, exception)));
    }

    private static IConfiguration Config(IDictionary<string, string?> values)
        => new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private static (WebPushService Service, CapturingLogger Log) Build(IDictionary<string, string?> values)
    {
        var log = new CapturingLogger();
        return (new WebPushService(Config(values), log), log);
    }

    private static readonly Dictionary<string, string?> NoKeys = new();

    // ─── الدائِمَة ──────────────────────────────────────────────────────────────────────────────

    /// <summary><b>بِلا مِفتاحَينِ زَوجٌ جَديدٌ في كُلِّ إنشاء</b> — وهذا ما يَجعَلُ كُلَّ إقلاعٍ مِفتاحاً عامّاً آخَر. يَبقى بِحَرفِه بَعدَ
    /// العِلاج: لا يُحفَظُ مِفتاحٌ في القاعِدَةِ ولا يُكتَبُ عَلى قُرص — العِلاجُ أَن يُضبَطَ ويُقال، لا أَن يَتَذَكَّرَه الخادِم.</summary>
    [Fact]
    public void Missing_keys_generate_a_fresh_pair_on_every_construction_so_every_restart_serves_another_public_key()
    {
        var (a, _) = Build(NoKeys);
        var (b, _) = Build(NoKeys);
        Assert.False(a.IsConfigured);
        Assert.False(b.IsConfigured);
        Assert.False(string.IsNullOrEmpty(a.PublicKey));
        Assert.NotEqual(a.PublicKey, b.PublicKey);
        // والخِدمَةُ وَحيدَةٌ في العَمَلِيَّة — إنشاءٌ واحِدٌ لِكُلِّ إقلاع.
        var mte = Read("libs", "templates", Tpl, "MarketplaceTemplateExtensions.cs");
        Assert.Contains("services.AddSingleton<ACommerce.Templates.Customer.Marketplace.Services.WebPushService>();", mte, StringComparison.Ordinal);
    }

    /// <summary>والمَضبوطُ يُقَدَّمُ كَما كُتِب — مِن أَيِّ مَصدَرِ تَهيئَة، ومِنه مُتَغَيِّرُ البيئَة <c>WebPush__PublicKey</c>.</summary>
    [Fact]
    public void Configured_keys_are_served_as_given()
    {
        var pair = WebPush.VapidHelper.GenerateVapidKeys();
        var (svc, _) = Build(new Dictionary<string, string?>
        {
            ["WebPush:Subject"] = "mailto:owner@example.invalid",
            ["WebPush:PublicKey"] = pair.PublicKey,
            ["WebPush:PrivateKey"] = pair.PrivateKey,
        });
        Assert.True(svc.IsConfigured);
        Assert.Equal(pair.PublicKey, svc.PublicKey);
    }

    /// <summary><b>والمُتَصَفِّحُ يُعيدُ الاشتِراكَ القَديم، والمُرسِلُ لا يَمحوه إلّا عَلى ‏404/410</b> — فَمِفتاحٌ تَبَدَّلَ لا يُرى. يَبقى بِحَرفِه.</summary>
    [Fact]
    public void The_browser_reposts_its_old_subscription_and_the_sender_drops_a_row_only_on_404_or_410()
    {
        var app = Read("libs", "templates", Tpl, "Components", "App.razor");
        var subscribe = app[app.IndexOf("async function subscribeToPush(reg)", StringComparison.Ordinal)..];
        subscribe = subscribe[..subscribe.IndexOf("window.acEnableNotifications = async function", StringComparison.Ordinal)];
        Assert.Contains("var sub = await reg.pushManager.getSubscription();", subscribe, StringComparison.Ordinal);
        Assert.Contains("if (!sub) {", subscribe, StringComparison.Ordinal);
        Assert.Contains("'/push/subscribe'", subscribe, StringComparison.Ordinal);

        var svc = Read("libs", "templates", Tpl, "Services", "WebPushService.cs");
        Assert.Contains("ex.StatusCode == System.Net.HttpStatusCode.Gone ||", svc, StringComparison.Ordinal);
        Assert.Contains("ex.StatusCode == System.Net.HttpStatusCode.NotFound)", svc, StringComparison.Ordinal);
        Assert.Contains("_log.LogWarning(ex, \"Push send failed for user {UserId}\", userId);", svc, StringComparison.Ordinal);
    }

    // ─── «اليَوم» — يَنقَلِبانِ بِأَسمائِهِما ───────────────────────────────────────────────────────

    /// <summary>«اليَوم»: التَحذيرُ يَنصَحُ بِمِلَفٍّ لا يَقرَؤُه المُضيف، ولا يُسَمّي مُتَغَيِّرَ بيئَة.</summary>
    [Fact]
    public void Today_the_missing_keys_warning_points_at_a_file_the_host_never_reads()
    {
        var (_, log) = Build(NoKeys);
        var warning = Assert.Single(log.Lines);
        output.WriteLine($"{warning.Level} · {warning.Text}");
        Assert.Equal(LogLevel.Warning, warning.Level);
        Assert.Contains("appsettings.Local.json", warning.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("WebPush__PrivateKey", warning.Text, StringComparison.Ordinal);
        var program = Read("apps", "V1.App", "Program.cs");
        Assert.DoesNotContain("AddJsonFile", program, StringComparison.Ordinal);
    }

    /// <summary>«اليَوم»: المُهَيَّأُ صامِت — فَلا يُفَرِّقُ سِجِلُّ الإقلاعِ بَينَ «مَضبوط» و«لَم يُنشَأ بَعد».</summary>
    [Fact]
    public void Today_a_configured_service_logs_nothing()
    {
        var pair = WebPush.VapidHelper.GenerateVapidKeys();
        var (_, log) = Build(new Dictionary<string, string?>
        {
            ["WebPush:PublicKey"] = pair.PublicKey,
            ["WebPush:PrivateKey"] = pair.PrivateKey,
        });
        Assert.Empty(log.Lines);
    }
}
