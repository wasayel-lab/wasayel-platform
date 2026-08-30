using System.Net;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using ACommerce.Kit.Tenants;
using ACommerce.Kit.Versions;
using ACommerce.Platform.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace ACommerce.Platform.Tests;

// ═══ هُوِيَّةُ البِناء — «أَيُّ إيداعٍ يَخدِمُ الآن؟» ═══════════════════
//
// **العِلَّةُ المَقيسَةُ الَّتي كَتَبَت هذا المِلَفّ (‏2026-08-30)**: اكتَمَلَ
// نَشرٌ ناجِح، ورَدَّ المَوقِعُ ‏200 على تِسعَةِ مَسارات — **وتَعَذَّرَ
// إثباتُ أَيِّ إيداعٍ يَخدِمُه الـSpace**. ‏`huggingface.co/api/spaces/…`
// يَصِفُ **مُستودَعَ** الـSpace ولا يَقولُ أَيَّ بِناءٍ يَخدِمُ الآن
// (‏وتَصحيحٌ مَقيسٌ ‏2026-08-30: «يَرُدُّ ‏401 بِلا رَمز» غَيرُ صَحيح —
// قيسَ ‏200 بِلا رَمز، والـSpace عامّ؛ ‏ADR-019 §١)؛
// ورَأسُ `x-proxied-replica` يُثبِتُ **تَبَدُّلَ
// حاوِيَةٍ لا هُوِيَّةَ بِناء**؛ و`runtime.stage` — بِنَصِّ الوَظيفَةِ
// نَفسِها — **خَبَرٌ لا بُرهان**.
//
// **والقَيدُ الحاكِمُ على التَصميمِ كُلِّه**: **نُقطَةٌ تَقرَأُ حالَتَها
// وَقتَ التَشغيلِ تَكذِب.** البَصمَةُ تُحقَنُ **وَقتَ البِناء** في
// الثُنائِيِّ نَفسِه، فَلا تَستَطيعُ حاوِيَةٌ قَديمَةٌ أَن تَدَّعِيَ
// إيداعاً جَديداً. والاختِبارُ الثاني أَدناه **يَحقِنُ هذا العَيبَ
// صِناعِيّاً**: أَيُّ تَنفيذٍ يَقرَأُ البيئَةَ وَقتَ التَشغيلِ يَحمَرُّ
// فيه فَوراً (القاعِدَة ١٠ — الأَداةُ تُقاسُ بِحَقنِ عَيبٍ قَبلَ أَن
// يُوثَقَ بِها).
//
// ─── وما قيسَ بِاليَدِ قَبلَ كِتابَةِ حَرفٍ مِن العِلاج ────────────────
//
// ‏SDK ‏10.0.302، على `apps/V1.App/V1.App.csproj` نَفسِه لا على مِجَسٍّ
// خارِجِيّ، وبِمَسحِ ذاكِرَةِ `AssemblyInfo` بَينَ القِياسَين:
//
//   | البِناء                                          | السِمَةُ المُوَلَّدَة |
//   |--------------------------------------------------|----------------------|
//   | `-p:SourceRevisionId=b4bd8885…`                  | `1.0.0+b4bd8885…`    |
//   | `-p:SourceRevisionId=0000…0001`                  | `1.0.0+0000…0001`    |
//
// أَي أَنّ الخاصِّيَّةَ **تَبلُغُ الثُنائِيَّ فِعلاً** — والقيمَةُ
// المُمَرَّرَةُ هي الَّتي تَظهَر، لا قيمَةٌ أُخرى.
//
// **وتَصحيحٌ لِلمُواصَفَة، مَقيسٌ هُنا**: زُعِمَ أَنّ البِناءَ بِلا
// الخاصِّيَّةِ يُعطي `1.0.0` بِلا `+`، فَيَكونُ غِيابُ `+` عَلامَةً
// قاطِعَةً على بِناءٍ بِلا بَصمَة. **وهذا غَيرُ صَحيحٍ حَيثُ يوجَدُ
// `.git`**: ‏SDK ‏.NET 8 فَما فَوق يَشحَنُ SourceLink ضِمناً، فَيَشتَقُّ
// `SourceRevisionId` مِن رَأسِ git تِلقائيّاً — وقَد قيسَ: البِناءُ بِلا
// الخاصِّيَّةِ أَعطى رَقمَ `HEAD` بِعَينِه. **ولا يُغَيِّرُ هذا شَيئاً
// داخِلَ الحاوِيَة**: `.dockerignore:14` يَحجُبُ `.git/` عَن سِياقِ
// البِناء، فَلا git هُناكَ ولا اشتِقاق — والحارِسُ في الـ`Dockerfile`
// هُوَ ما يَضمَنُ البَصمَةَ لا صُدفَةُ وُجودِ مُستَودَع.
// **وأَثَرُه المَشروعُ الوَحيد**: `scripts/verify-production-boot.sh`
// يَنشُرُ مَحَلِّيّاً حَيثُ `.git` مَوجود، فَتُجيبُ النُقطَةُ رَقمَ
// الرَأسِ المَحَلِّيِّ لا `"unknown"` — وهي حالَةٌ لا تَخدِمُ أَحَداً.

// ─── مُضيفٌ مُصَغَّرٌ حَيّ — نَفسُ نَمَط `PayPalEndpointBehaviourTests` ──
//
// **ولا تُضافُ `Microsoft.AspNetCore.Mvc.Testing`**: غِيابُها قَرارٌ
// مُوَثَّقٌ بِسَبَبِه هُناك — `WebApplicationFactory` تُقلِعُ `Program`
// كامِلاً ومَعَه Marten وWolverine وRedis، أَي **قاعِدَةَ بَياناتٍ
// حَقيقِيَّةً في بَوّابَةٍ يَجِبُ أَن تَخضَرَّ بِلا شَبَكَة**.
file sealed class HealthHost : IAsyncDisposable
{
    private readonly WebApplication _app;

    public HttpClient Client { get; }

    private HealthHost(WebApplication app, HttpClient client)
    {
        _app = app; Client = client;
    }

    private static int FreePort()
    {
        using var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    /// <summary><paramref name="withVersionGate"/> يُركِّبُ
    /// <c>UseVersionGate</c> قَبلَ النِقاط — بِنَفسِ تَرتيبِ
    /// <c>Program.cs</c> — فَيُقاسُ المَسارُ لا المُعالِجُ وَحدَه.</summary>
    /// <summary><b>ولِماذا حَلقَةُ إعادَة</b>: ‏`FreePort` تُغلِقُ
    /// المُستَمِعَ ثُمَّ يُفتَحُ المَنفَذُ مَرَّةً أُخرى — فَبَينَهُما
    /// فُرجَةٌ يَستَطيعُ فيها **مُضيفٌ في صَنفٍ آخَر** (وxUnit يُشَغِّلُ
    /// الأَصنافَ مُتَوازِيَةً) أَن يَختَطِفَ نَفسَ الرَقَم. وقَد وَقَعَ
    /// فِعلاً: فَشَلٌ واحِدٌ عابِرٌ في جَولَةٍ مِن أَربَع، لَم يَتَكَرَّر
    /// في الثَلاثِ التالِيَة. **وبَوّابَةٌ تَحمَرُّ بِلا ذَنبٍ تُعَطَّلُ
    /// بَعدَ أُسبوع** — فَالإعادَةُ ثَمَنُها ثَلاثَةُ أَسطُر.</summary>
    public static async Task<HealthHost> StartAsync(
        string? informationalVersion,
        DateTimeOffset startedAt,
        bool withVersionGate = false)
    {
        for (var attempt = 1; ; attempt++)
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.Logging.ClearProviders();
            if (withVersionGate)
                builder.Services.AddVersionGate(o => o.MinimumSupported = "1.0.0");

            var port = FreePort();
            var app = builder.Build();
            app.Urls.Add($"http://127.0.0.1:{port}");
            if (withVersionGate)
                app.UseVersionGate();
            app.MapBuildIdentity(informationalVersion, startedAt);

            try
            {
                await app.StartAsync();
            }
            catch (IOException) when (attempt < 5)
            {
                await app.DisposeAsync();
                continue;
            }

            var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
            {
                BaseAddress = new Uri($"http://127.0.0.1:{port}"),
                Timeout = TimeSpan.FromSeconds(20),
            };

            return new HealthHost(app, client);
        }
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}

public class BuildIdentityEndpointTests
{
    private const string Sha = "b4bd8885cb4789e20bba4945b3aacf7f827cd9a0";
    private static readonly DateTimeOffset Boot =
        new(2026, 8, 30, 6, 53, 21, TimeSpan.Zero);

    private static string RepoRoot => ThemeZeroEquivalenceTests.RepoRoot;

    private static string ReadRepoFile(params string[] parts)
    {
        var path = Path.Combine(RepoRoot, Path.Combine(parts));
        Assert.True(File.Exists(path), $"مِلَفٌّ مَفقود: {path} — البُرهانُ بِلا طَرَفٍ مَفحوص.");
        return File.ReadAllText(path);
    }

    // ─── ١) النُقطَةُ تُشَغَّلُ على HTTP حَقيقيّ، لا تُقرَأُ نَصّاً ──────

    /// <summary><b>الحُقولُ حَقلانِ لا أَكثَر، ومَجموعَةُ المَفاتيحِ
    /// مُثَبَّتَةٌ بِالضَبط</b> — فَلا تَنزَلِقُ التَسمِيَةُ مَعَ تَرقِيَةِ
    /// إطار، ولا يَتَسَرَّبُ حَقلٌ ثالِثٌ بِلا قَرار.</summary>
    [Fact]
    public async Task Health_answers_the_stamp_over_real_http()
    {
        await using var host = await HealthHost.StartAsync($"1.0.0+{Sha}", Boot);

        var res = await host.Client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.StartsWith("application/json",
            res.Content.Headers.ContentType?.ToString() ?? "",
            StringComparison.Ordinal);

        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        var keys = doc.RootElement.EnumerateObject().Select(p => p.Name).OrderBy(k => k, StringComparer.Ordinal);
        Assert.Equal(new[] { "commit", "startedAt" }, keys);

        Assert.Equal(Sha, doc.RootElement.GetProperty("commit").GetString());
        Assert.Equal("2026-08-30T06:53:21Z", doc.RootElement.GetProperty("startedAt").GetString());
    }

    /// <summary>ثُنائيٌّ بِلا بَصمَةٍ يَقولُ <c>"unknown"</c> حَرفاً — لا
    /// يَختَرِعُ رَقماً ولا يَنهار.</summary>
    [Fact]
    public async Task Health_says_unknown_when_the_binary_carries_no_stamp()
    {
        await using var host = await HealthHost.StartAsync("1.0.0", Boot);

        using var doc = JsonDocument.Parse(await host.Client.GetStringAsync("/health"));
        Assert.Equal("unknown", doc.RootElement.GetProperty("commit").GetString());
    }

    // ─── ٢) حَقنُ العَيب — وهذِه هي الحالَةُ المَقصودَةُ بِعَينِها ──────

    /// <summary><b>الجَوابُ لا يَتَحَرَّكُ حينَ تَتَحَرَّكُ البيئَة.</b>
    /// تُضبَطُ ثَلاثَةُ مُتَغَيِّراتٍ يُغري اسمُها بِالقِراءَةِ وَقتَ
    /// التَشغيل، ويُشتَرَطُ أَن يَكونَ الجِسمانِ **مُتَطابِقَينِ حَرفاً**.
    /// تَنفيذٌ يَقرَأُ البيئَةَ يَرُدُّ <c>TAMPERED</c> فَيَحمَرّ؛
    /// وتَنفيذٌ يَقرَأُ السِمَةَ لا يَراها.</summary>
    [Fact]
    public async Task Health_body_does_not_move_when_the_environment_moves()
    {
        await using var host = await HealthHost.StartAsync($"1.0.0+{Sha}", Boot);

        var before = await host.Client.GetStringAsync("/health");

        var names = new[] { "SOURCE_REVISION_ID", "GIT_COMMIT", "DEPLOY_SHA" };
        var saved = names.ToDictionary(n => n, Environment.GetEnvironmentVariable);
        try
        {
            foreach (var n in names) Environment.SetEnvironmentVariable(n, "TAMPERED");
            var after = await host.Client.GetStringAsync("/health");

            Assert.Equal(before, after);
            Assert.DoesNotContain("TAMPERED", after, StringComparison.Ordinal);
        }
        finally
        {
            foreach (var (n, v) in saved) Environment.SetEnvironmentVariable(n, v);
        }
    }

    // ─── ٣) الشَكلُ لا السُلوكُ وَحدَه ──────────────────────────────────

    /// <summary><b>يَحرُسُ ما لا تَراهُ الحالَةُ ٢</b>: لَو نُقِلَت
    /// القِراءَةُ وَقتَ التَشغيلِ إلى خارِجِ جِسمِ النُقطَةِ (‏إلى
    /// التَسجيلِ نَفسِه) لَبَقِيَ الجِسمانِ مُتَطابِقَينِ وعادَ الكَذِب.
    ///
    /// <para><b>ويَمنَعُ المَصدَرَ المُكلِفَ لا الكاذِبَ وَحدَه</b>: صِفرُ
    /// <c>Marten</c> وصِفرُ جَلسَةٍ وصِفرُ <c>HttpClient</c>. الحارِسُ
    /// الَّذي يَمنَعُ الكَذِبَ ولا يَمنَعُ الاستِنزافَ يَترُكُ البابَ
    /// مَفتوحاً لِنُقطَةٍ عامَّةٍ بِلا تَحديدِ مُعَدَّلٍ تَفتَحُ جَلسَةً
    /// على Neon في كُلِّ طَلَب.</para></summary>
    [Fact]
    public void Health_registration_reads_nothing_but_the_attribute()
    {
        var src = ReadRepoFile("libs", "core", "ACommerce.Platform.Hosting", "BuildIdentity.cs");
        var code = StripBlockAndLineComments(src);

        string[] forbidden =
        {
            "Environment.GetEnvironmentVariable", "Environment.ExpandEnvironmentVariables",
            "File.", "Directory.", "Process",
            "Request.Headers", "HttpContext.Request",
            "Marten", "IDocumentSession", "IDocumentStore", "HttpClient",
        };

        var found = forbidden.Where(f => code.Contains(f, StringComparison.Ordinal)).ToList();

        Assert.True(found.Count == 0,
            "نُقطَةُ هُوِيَّةِ البِناءِ تَقرَأُ مَصدَراً غَيرَ السِمَة — " +
            "أَو تَفتَحُ مَصدَراً مُكلِفاً:\n  " + string.Join("\n  ", found));

        // عَدّاد: أَداةٌ فَحَصَت صِفراً لا تُميَّزُ عَن أَداةٍ عَمياء.
        Assert.True(code.Contains("AssemblyInformationalVersion", StringComparison.Ordinal)
                    || code.Contains("informationalVersion", StringComparison.Ordinal),
            "أَداة عَمياء: لَم يُقرَأ مِلَفُّ البَصمَةِ أَصلاً.");
    }

    // ─── ٤) مَنعُ التَخزين ─────────────────────────────────────────────

    /// <summary><b>ضَرورِيَّةٌ لا تَزيينِيَّة</b>: مَسحُ المُستَودَعِ
    /// كُلِّه أَعطى <b>مَوضِعاً واحِداً يَتيماً</b> يَضبُطُ
    /// <c>Cache-Control</c> — <c>MissingFilePlaceholder</c> بِقيمَةِ
    /// <c>public, max-age=300</c>. أَي **صِفرُ <c>no-store</c> في
    /// المُستَودَع**، فَالافتِراضُ بِأَنَّه سَيُضبَطُ افتِراضٌ لا قِياس.
    ///
    /// <para><b>وما لا تَدَّعيه</b>: <c>no-store</c> **في الرَدّ** لا
    /// يُثبِتُ أَنّ الرَدَّ لَم يُخَزَّن — الرَأسُ يُسافِرُ مَعَ الجِسمِ
    /// المُخَزَّن. نَقضُ التَخزينِ بِالطَلَبِ يَقَعُ في بَوّابَةِ النَشر،
    /// وهُناكَ تُفحَصُ `age`/`etag`/`x-cache` أَيضاً.</para></summary>
    [Fact]
    public async Task Health_forbids_caching()
    {
        await using var host = await HealthHost.StartAsync($"1.0.0+{Sha}", Boot);

        var res = await host.Client.GetAsync("/health");

        var cc = res.Headers.CacheControl?.ToString() ?? "";
        Assert.Contains("no-store", cc, StringComparison.OrdinalIgnoreCase);
        Assert.Null(res.Headers.ETag);
        Assert.Null(res.Content.Headers.LastModified);
    }

    // ─── ٥) لا سِرَّ بِحال — فَحصٌ بِالقيمَةِ لا بِالنِيَّة ─────────────

    [Fact]
    public async Task Health_carries_no_secret_value()
    {
        await using var host = await HealthHost.StartAsync($"1.0.0+{Sha}", Boot);
        var body = await host.Client.GetStringAsync("/health");

        var risky = new Regex("TOKEN|SECRET|KEY|PASSWORD|CONNECTION", RegexOptions.IgnoreCase);
        var leaked = new List<string>();
        var scanned = 0;

        foreach (System.Collections.DictionaryEntry e in Environment.GetEnvironmentVariables())
        {
            var name = e.Key?.ToString() ?? "";
            var value = e.Value?.ToString() ?? "";
            if (!risky.IsMatch(name) || value.Length < 8) continue;
            scanned++;
            if (body.Contains(value, StringComparison.Ordinal))
                leaked.Add(name);
        }

        Assert.True(leaked.Count == 0,
            "جِسمُ /health يَحمِلُ قيمَةَ مُتَغَيِّرٍ حَسّاس: " + string.Join(", ", leaked));

        // والفَحصُ الَّذي لا يَعتَمِدُ على وُجودِ مُتَغَيِّرٍ في البيئَة —
        // فَبيئَةٌ نَظيفَةٌ كانَت سَتَجعَلُ الحالَةَ أَعلاهُ عَمياء.
        Assert.Equal(2, JsonDocument.Parse(body).RootElement.EnumerateObject().Count());
        Assert.True(body.Length < 200,
            $"جِسمُ /health صارَ {body.Length} بايتاً — حَقلانِ لا يَبلُغانِ ذلك، فَشَيءٌ دَخَل. (فُحِصَ {scanned} مُتَغَيِّراً حَسّاساً.)");
    }

    // ─── ٦) الطابَعُ لَحظَةُ الإقلاعِ لا لَحظَةُ الطَلَب ────────────────

    [Fact]
    public async Task Health_startedAt_is_the_boot_not_the_request()
    {
        await using var host = await HealthHost.StartAsync($"1.0.0+{Sha}", DateTimeOffset.UtcNow);

        var first = await host.Client.GetStringAsync("/health");
        await Task.Delay(1100);
        var second = await host.Client.GetStringAsync("/health");

        Assert.Equal(first, second);
    }

    // ─── ٦·ب) المُراقِبُ يَبعَثُ HEAD — والنُقطَةُ كانَت تَسقُط ─────────

    /// <summary><b>العِلَّة</b>: <c>MapGet</c> يُطابِقُ GET وَحدَه.
    /// و<b>الأَسوَأُ لَيسَ ‏405</b>: في التَطبيقِ الحَقيقيِّ يَنزِلُ
    /// طَلَبُ HEAD إلى <c>@page "/{slug}"</c> فَيُرَدُّ ‏200 بِـ
    /// <c>text/html</c> — فَيَقرَأُ المُراقِبُ صَفحَةَ مُستَأجِرٍ
    /// ويَحسِبُها النُقطَةَ سَليمَة. **كَذِبٌ في اتِّجاهِ السَلامَة.**
    ///
    /// <para>وهُنا يُقاسُ التَسجيلُ فَقَط (المُضيفُ المُصَغَّرُ لا يَحمِلُ
    /// <c>/{slug}</c>)؛ والابتِلاعُ نَفسُه يُقاسُ على الأُنبوبِ الكامِلِ
    /// في <c>verify-production-boot.sh</c>.</para></summary>
    [Fact]
    public async Task Health_answers_HEAD_because_that_is_what_monitors_send()
    {
        await using var host = await HealthHost.StartAsync($"1.0.0+{Sha}", Boot);

        using var req = new HttpRequestMessage(HttpMethod.Head, "/health");
        var res = await host.Client.SendAsync(req);

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.StartsWith("application/json",
            res.Content.Headers.ContentType?.ToString() ?? "", StringComparison.Ordinal);
        Assert.Contains("no-store", res.Headers.CacheControl?.ToString() ?? "",
            StringComparison.OrdinalIgnoreCase);
        Assert.Equal("", await res.Content.ReadAsStringAsync());
    }

    // ─── ٨·ب) الـDockerfile يُبَرهِنُ الأَثَرَ لا النِيَّةَ وَحدَها ──────

    /// <summary><b>الثَغرَةُ الَّتي يَسُدُّها، مَقيسَةً بِحَقن</b>:
    /// <c>-p:IncludeSourceRevisionInInformationalVersion=false</c> يُلغي
    /// بَثَّ السِمَةِ فَتَرُدُّ النُقطَةُ <c>"unknown"</c>، **وحارِسُ
    /// المِلَفِّ يَمُرُّ ويَطبَعُ «البَصمَة: …»**. ومَوضِعُ الخاصِّيَّةِ
    /// الطَبيعيُّ <c>Directory.Build.props</c>، وهو **مِن المِلَفّاتِ
    /// المَنسوخَةِ إلى الـSpace** — فَالثَغرَةُ داخِلَ الشَجَرَةِ
    /// المُصدَّقَة. فَالمَشروطُ أَن يُقرَأَ الثُنائِيُّ بَعدَ النَشرِ
    /// وتُطابَقَ القيمَةُ حَرفاً.</summary>
    [Fact]
    public void Dockerfile_proves_the_stamp_reached_the_binary()
    {
        var docker = StripHashComments(ReadRepoFile("Dockerfile"));

        Assert.Contains("/publish/V1.App.dll", docker, StringComparison.Ordinal);
        Assert.Contains("1\\.0\\.0\\+[0-9a-f]{40}", docker, StringComparison.Ordinal);
        Assert.Contains("1.0.0+$rev", docker, StringComparison.Ordinal);
    }

    // ─── ٩·ب) التَجهيزُ مِن الإيداعِ لا مِن شَجَرَةِ العَمَل ────────────

    /// <summary><b>العَيبُ مُعادُ إنتاجُه</b>: ثُنائيٌّ بُنِيَ مِن شَجَرَةٍ
    /// فيها تَعديلٌ غَيرُ مودَعٍ أَعلَنَ <b>رَأسَ الشَجَرَةِ النَظيف</b>
    /// وهُوَ يَخدِمُ شَيئاً آخَر. وكانَ الطَرَفانِ مِن مَصدَرَين:
    /// المُحتَوى بِـ<c>cp -p</c> مِن شَجَرَةِ العَمَل، والرَقَمُ مِن
    /// <c>git rev-parse HEAD</c>. فَـ«الرَقَمُ المُعلَنُ هُوَ الإيداعُ
    /// الَّذي بُنِيَ مِنه» كانَ يَصمُدُ <b>بِتَرتيبِ خَطَواتِ الوَظيفَةِ
    /// لا بِحارِس</b> — وتَرتيبُ الخَطَواتِ يَتَغَيَّرُ في PR واحِد.</summary>
    [Fact]
    public void Deploy_manifest_stages_from_the_commit_not_the_working_tree()
    {
        var sh = StripHashComments(ReadRepoFile("scripts", "deploy-manifest.sh"));

        Assert.Contains("git show \"HEAD:$f\"", sh, StringComparison.Ordinal);
        Assert.DoesNotContain("cp -p \"$f\"", sh, StringComparison.Ordinal);
        // والانحِرافُ يُقاسُ ويُطبَع — فَالمُشَغِّلُ المَحَلِّيُّ يَعرِفُ
        // أَنّ تَعديلَه لَم يَذهَب، ولا يُكتَشَفُ ذلك مِن نَتيجَةٍ غامِضَة.
        Assert.Contains("drift", sh, StringComparison.Ordinal);
    }

    // ─── ١٠·ب) السِلسِلَةُ كامِلَةً في مَوضِعٍ واحِدٍ على V1.App ────────

    /// <summary><b>ما كانَ ناقِصاً</b>: ‏CI يَقيسُ «مِلَفّ ← خاصِّيَّة ←
    /// سِمَة»، والاختِباراتُ تَقيسُ «سِمَة ← جِسم» على مُضيفٍ مُصَغَّر،
    /// وبَوّابَةُ الإقلاعِ كانَت تَنشُرُ <b>بِلا</b> الخاصِّيَّةِ وتَشتَرِطُ
    /// وُجودَ المِفتاحِ <c>"commit"</c> وَحدَه — فَتَخضَرُّ على
    /// <c>"unknown"</c> سَواءً. فَالسِلسِلَةُ كامِلَةً لَم تُنَفَّذ قَطُّ
    /// على <c>V1.App</c> في مَوضِعٍ واحِد. الآنَ تُمَرَّرُ بَصمَةٌ
    /// مَعلومَةٌ ويُشتَرَطُ أَن يَرُدَّها الجِسمُ حَرفاً.</summary>
    [Fact]
    public void The_production_boot_gate_runs_the_whole_chain_on_the_real_app()
    {
        var gate = ReadRepoFile("scripts", "verify-production-boot.sh");

        Assert.Contains("GATE_STAMP=", gate, StringComparison.Ordinal);
        Assert.Contains("-p:SourceRevisionId=\"$GATE_STAMP\"", gate, StringComparison.Ordinal);
        Assert.Contains("\\\"commit\\\":\\\"${GATE_STAMP}\\\"", gate, StringComparison.Ordinal);
        // و`HEAD` مَعَ `GET`: هُنا وَحدَه يُقاسُ الابتِلاعُ على الأُنبوبِ
        // الكامِلِ الَّذي يَحمِلُ `/{slug}`.
        Assert.Contains("\"HEAD|/health|200\"", gate, StringComparison.Ordinal);
    }

    // ─── ٧) الدالَّةُ النَقِيَّة — مُوجِباً وسالِباً ────────────────────

    [Theory]
    [InlineData("1.0.0+b4bd8885cb4789e20bba4945b3aacf7f827cd9a0", "b4bd8885cb4789e20bba4945b3aacf7f827cd9a0")]
    [InlineData("2.7.3-beta+b4bd8885cb4789e20bba4945b3aacf7f827cd9a0", "b4bd8885cb4789e20bba4945b3aacf7f827cd9a0")]
    public void CommitFrom_reads_a_forty_hex_suffix(string informational, string expected)
        => Assert.Equal(expected, BuildIdentity.CommitFrom(informational));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("1.0.0")]
    [InlineData("1.0.0+")]
    [InlineData("1.0.0+abc")]
    // أَربَعونَ مِحرَفاً فيها `g` — الطولُ صَحيحٌ والأَبجَدِيَّةُ لا.
    [InlineData("1.0.0+g4bd8885cb4789e20bba4945b3aacf7f827cd9a0")]
    // واحِدٌ وأَربَعون.
    [InlineData("1.0.0+b4bd8885cb4789e20bba4945b3aacf7f827cd9a00")]
    public void CommitFrom_refuses_anything_that_is_not_forty_hex(string? informational)
        => Assert.Null(BuildIdentity.CommitFrom(informational));

    // ─── ٨) الـDockerfile يَرفُضُ النَشرَ بِلا بَصمَة ───────────────────

    /// <summary><b>وحَدُّه مُصَرَّحٌ بِه</b>: لا Docker على جِهازِ
    /// التَطوير، فَهذا يُثبِتُ **الأَمرَ** لا الحاوِيَة.</summary>
    [Fact]
    public void Dockerfile_refuses_to_publish_without_a_stamp()
    {
        var docker = StripHashComments(ReadRepoFile("Dockerfile"));

        Assert.Contains("-p:SourceRevisionId=", docker, StringComparison.Ordinal);
        Assert.Contains(".source-revision", docker, StringComparison.Ordinal);
        Assert.Contains("exit 1", docker, StringComparison.Ordinal);

        // ‏`ARG SOURCE_REVISION_ID=unknown` — الَّذي اقتَرَحَته
        // `docs/DEPLOY.md` — **يُنتِجُ حاوِيَةً كاذِبَةً صامِتَة**:
        // تُبنى وتَخدِمُ وتَقولُ «‏unknown» ولا يَعلَمُ أَحَد.
        Assert.DoesNotContain("SOURCE_REVISION_ID=unknown", docker, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ARG SOURCE_REVISION_ID", docker, StringComparison.OrdinalIgnoreCase);
    }

    // ─── ٩) الوَظيفَةُ تَكتُبُ البَصمَةَ بَعدَ التَجهيزِ وقَبلَ الإيداع ──

    /// <summary><b>بِحَدَّينِ لا بِحَدٍّ واحِد.</b> الحَدُّ الأَعلى
    /// وَحدَه (‏قَبلَ <c>git add -A</c>) لا يَرى الفَخَّ المُقابِل:
    /// <c>deploy-manifest.sh --stage</c> يَبدَأُ بِـ<c>rm -rf</c>، فَمَن
    /// يَنقُلُ الكِتابَةَ يَوماً إلى **قَبلَ** التَجهيزِ يَمحوها المَسحُ
    /// صامِتاً — وفَحصُ «نُسِخَ = المَطلوب» داخِلَ السكريبتِ **لا
    /// يَعرِفُ عَنها شَيئاً** فَيَبقى أَخضَر. فَتُدفَعُ شَجَرَةٌ بِلا
    /// بَصمَة، ويَفشَلُ حارِسُ الـ<c>Dockerfile</c>، وتَبقى الحاوِيَةُ
    /// القَديمَةُ تَخدِم.</summary>
    [Fact]
    public void Deploy_workflow_writes_the_stamp_after_staging_and_before_committing()
    {
        var wf = StripHashComments(ReadRepoFile(".github", "workflows", "deploy-hf.yml"));

        var stage = wf.IndexOf("deploy-manifest.sh --stage", StringComparison.Ordinal);
        var write = wf.IndexOf("/.source-revision", StringComparison.Ordinal);
        var add = wf.IndexOf("git add -A", StringComparison.Ordinal);

        Assert.True(stage >= 0, "لا نِداءَ لِـdeploy-manifest.sh --stage — الأَداةُ عَمياء.");
        Assert.True(write >= 0, "الوَظيفَةُ لا تَكتُبُ .source-revision إطلاقاً.");
        Assert.True(add >= 0, "لا git add -A — الأَداةُ عَمياء.");

        Assert.True(stage < write,
            "‏.source-revision تُكتَبُ قَبلَ --stage، و`rm -rf` يَمحوها صامِتاً.");
        Assert.True(write < add,
            "‏.source-revision تُكتَبُ بَعدَ `git add -A` — تُدفَعُ شَجَرَةٌ بِلا بَصمَة.");
    }

    /// <summary>وبَوّابَةُ النَشرِ تَستَطلِعُ الحَيَّ وتُفشِلُ على عَدَمِ
    /// المُطابَقَة — لا تَطبَعُ خَبَراً. <b>والشُروطُ الأَربَعَةُ
    /// مُثَبَّتَةٌ نَصّاً</b>، فَإسقاطُ أَيِّها يَحمَرُّ هُنا.</summary>
    [Fact]
    public void Deploy_workflow_asserts_the_live_space_serves_this_commit()
    {
        var raw = ReadRepoFile(".github", "workflows", "deploy-hf.yml");

        // الشُروطُ تُقرَأُ مِن **الكود** لا مِن التَعليقِ الشارِح — وإلّا
        // اخضَرَّ الحارِسُ على وَظيفَةٍ تَصِفُ ما لا تَفعَل.
        var wf = StripHashComments(raw);

        Assert.Contains("/health", wf, StringComparison.Ordinal);
        Assert.Contains("application/json", wf, StringComparison.Ordinal);
        Assert.Contains("no-store", wf, StringComparison.Ordinal);
        Assert.Contains("HF_SPACE_URL", wf, StringComparison.Ordinal);
        // نَقضُ التَخزينِ بِالطَلَبِ وفَحصُ آثارِ وَسيطٍ مُخَزِّن — في
        // البَوّابَةِ الآلِيَّةِ لا في قائِمَةٍ يَدَوِيَّةٍ تُنَفَّذُ مَرَّةً.
        Assert.Contains("probe=", wf, StringComparison.Ordinal);
        Assert.Contains("cf-cache-status", wf, StringComparison.Ordinal);

        // خَطُّ الأَساسِ يُقاسُ **قَبلَ الدَفع**: بِدونِه تَخضَرُّ
        // إعادَةُ تَشغيلِ الوَظيفَةِ على نَفسِ الإيداعِ خِلالَ ثَوانٍ
        // على **الحاوِيَةِ القَديمَة** — بَوّابَةٌ خَضراءُ قَبلَ أَن
        // تَعمَلَ لَيسَت بَوّابَة.
        Assert.Contains("base_starts", wf, StringComparison.Ordinal);

        // والتَعليقُ الَّذي كانَ يَقتَرِحُ النُقطَةَ يُحذَف — فَقَد
        // نُفِّذَت. اقتِراحٌ باقٍ بَعدَ التَنفيذِ يُوَرِّثُ القارِئَ شَكّاً.
        // **ويُفحَصُ الخام**: المَقصودُ تَعليقٌ لا كود.
        Assert.DoesNotContain("اقتِراحُ نُقطَة", raw, StringComparison.Ordinal);
    }

    /// <summary><b>وخَطُّ أَساسٍ فارِغٌ كانَ يُلغي الحارِسَ صامِتاً — وهي
    /// الحالَةُ الَّتي كُتِبَ الحارِسُ لَها بِعَينِها.</b>
    ///
    /// <para>الفارِغُ حالَتان: «‏أَجابَ HTTP ولا نُقطَة» (‏أَوَّلُ نَشر —
    /// مَشروعَة) و«‏فَشِلَ الاستِطلاع» (‏مُهلَةٌ عابِرَة أَو ‏5xx). وكانَ
    /// الشَرطُ <c>[ -n "$base_started" ]</c> يُمَرِّرُ الثانِيَةَ كَما
    /// يُمَرِّرُ الأولى، فَتَعبُرُ حاوِيَةٌ قَديمَةٌ تَحمِلُ
    /// <c>sha</c> سَلفاً بِثَلاثِ استِطلاعاتٍ مُتَّفِقَةٍ في ثَوانٍ ثُمَّ
    /// يَفشَلُ بِناءُ HF. فَالحالاتُ صارَت مُسَمّاةً، ولِـ<c>failed</c>
    /// أَرضِيَّةٌ زَمَنِيَّةٌ تُلتَقَطُ قَبلَ الدَفع.</para></summary>
    [Fact]
    public void Deploy_workflow_tells_a_failed_baseline_from_an_absent_one()
    {
        var wf = StripHashComments(ReadRepoFile(".github", "workflows", "deploy-hf.yml"));

        Assert.Contains("base_state", wf, StringComparison.Ordinal);
        Assert.Contains("deploy_floor", wf, StringComparison.Ordinal);
        Assert.Contains("measured", wf, StringComparison.Ordinal);
        Assert.Contains("absent", wf, StringComparison.Ordinal);

        // والشَرطُ القَديمُ — «‏فارِغٌ ⇒ مُرَّ» — لا يَعودُ إلى الوَظيفَةِ
        // بِصيغَتِه الأولى: هُوَ الثَغرَةُ نَفسُها.
        Assert.DoesNotContain("[ -n \"${base_started:-}\" ]", wf, StringComparison.Ordinal);

        // و`base_commit` كانَ يُلتَقَطُ ويُطبَعُ **ولا يُقرَأُ في تَأكيد**،
        // وهُوَ أَدَقُّ إشارَةٍ إلى الحالَة: الحَيُّ يَحمِلُ `sha` سَلفاً.
        Assert.Contains("base_same", wf, StringComparison.Ordinal);
    }

    /// <summary><b>والقيمَتانِ مِن رَدٍّ بَعيدٍ لا تُكتَبانِ في
    /// <c>$GITHUB_ENV</c> بِلا تَصديق</b>: سَطرٌ في جِسمِ JSON يَحقِنُ
    /// مُتَغَيِّراً في الخَطَواتِ التالِيَة — <b>بِما فيها
    /// <c>sha</c> الَّذي تُقارِنُ بِه البَوّابَةُ نَفسُها</b>. أَي أَنّ
    /// الطَرَفَ الَّذي وُضِعَت لِكَشفِه يَستَطيعُ الكَذِبَ عَلَيها.</summary>
    [Fact]
    public void Deploy_workflow_validates_remote_values_before_exporting_them()
    {
        var wf = StripHashComments(ReadRepoFile(".github", "workflows", "deploy-hf.yml"));

        Assert.Contains("[0-9]{4}-[0-9]{2}-[0-9]{2}T", wf, StringComparison.Ordinal);
        Assert.Contains("*[!0-9a-f]*", wf, StringComparison.Ordinal);
    }

    /// <summary><b>والمَسارُ العاري يُفحَصُ أَيضاً</b>: سِلسِلَةُ
    /// الاستِعلامِ <c>probe=</c> تَمنَعُ التَخزينَ <b>وتَمنَعُ كَشفَه
    /// مَعاً</b> — فَما بُرهِنَ هُوَ أَنّ رَدّاً مَنقوضَ التَخزينِ صَحيح،
    /// ولَم يُفحَص قَطُّ العُنوانُ الَّذي يُنادِيه المُراقِبُ فِعلاً.
    /// <b>ورَدٌّ مُخَزَّنٌ عَلَيه هُوَ الكَذِبَةُ بِعَينِها.</b></summary>
    [Fact]
    public void Deploy_workflow_probes_the_bare_path_too()
    {
        var wf = StripHashComments(ReadRepoFile(".github", "workflows", "deploy-hf.yml"));

        Assert.Contains("bare_ok", wf, StringComparison.Ordinal);
        Assert.Contains("b1.hdr", wf, StringComparison.Ordinal);
        Assert.Contains("^date:", wf, StringComparison.Ordinal);
    }

    /// <summary><b>وآثارُ التَخزينِ تُقرَأُ بِالقيمَةِ لا بِالحُضور</b>:
    /// <c>cf-cache-status: DYNAMIC|BYPASS|MISS</c> و<c>Age: 0</c> أَدِلَّةٌ
    /// على <b>عَدَمِ</b> التَخزين، و<c>ETag</c> مُصادِقٌ لا مَخزَن. فَعَدُّ
    /// الحُضورِ كانَ يَجعَلُ حافَّةً واحِدَةً تُضيفُ رَأساً مُطَمئِنّاً
    /// تُحمِرُّ <b>كُلَّ نَشرٍ سَليم</b>.</summary>
    [Fact]
    public void Deploy_workflow_reads_cache_evidence_by_value_not_by_presence()
    {
        var wf = StripHashComments(ReadRepoFile(".github", "workflows", "deploy-hf.yml"));

        Assert.Contains("cache_evidence", wf, StringComparison.Ordinal);
        Assert.Contains("hit|stale|revalidated", wf, StringComparison.Ordinal);
        Assert.DoesNotContain("'^(age|etag|x-cache|cf-cache-status):'", wf, StringComparison.Ordinal);
    }

    /// <summary><b>والحُكمُ يُسَمّي الشَرطَ الساقِط</b>: لَو ثَبَتَت
    /// المُطابَقَةُ وسَقَطَ شَرطٌ آخَرُ حَتّى نَفادِ الميزانِيَّة، كانَ
    /// يُقالُ «البِناءُ لَم يَجهَز» <b>والبِناءُ جاهِز</b> — حُمرَةٌ
    /// تُرسِلُ القارِئَ إلى العِلاجِ الخَطَإ.</summary>
    [Fact]
    public void Deploy_workflow_names_the_condition_that_failed()
    {
        var wf = StripHashComments(ReadRepoFile(".github", "workflows", "deploy-hf.yml"));
        Assert.Contains("last_fail", wf, StringComparison.Ordinal);
    }

    /// <summary><b>ولا يُنادى شَيءٌ بِلا مُهلَة</b>: <c>git push</c> كانَ
    /// الانتِظارَ المَفتوحَ الوَحيدَ في الوَظيفَة — يَعلَقُ حَتّى تَنتَهي
    /// مُهلَةُ الوَظيفَةِ كامِلَةً فَيُخَلِّفُ نَشراً لا أَحَدَ قَرَأَ
    /// نَتيجَتَه. <b>تَعليقٌ يَبدو عَمَلاً أَسوَأُ مِن خَطَإٍ صَريح.</b></summary>
    [Fact]
    public void Deploy_workflow_bounds_the_push_with_a_timeout()
    {
        var wf = StripHashComments(ReadRepoFile(".github", "workflows", "deploy-hf.yml"));
        Assert.Contains("timeout 300 git push", wf, StringComparison.Ordinal);
    }

    /// <summary><b>والمُصَدِّقُ الوَحيدُ لا يَبقى مَشروطاً بِنَقرَةٍ
    /// يَدَوِيَّة</b>: قيسَ يَومَ ‏2026-08-30 أَنّ <c>vars.HF_SPACE_URL</c>
    /// <b>غَيرُ مَضبوطٍ فِعلاً</b> — أَي أَنّ البَوّابَةَ الحَيَّةَ كانَت
    /// تُحَذِّرُ وتَخرُجُ بِصِفرٍ في كُلِّ نَشر، وكُلُّ ثَغرَةٍ تَحتَها
    /// تُشحَنُ خَضراء. والاشتِقاقُ مِن ترويسَةِ <c>link</c> كانَ تَخميناً
    /// بِحَقّ؛ أَمّا <c>.host</c> في رَدِّ API الـSpace فَهُوَ
    /// <b>تَصريحُ HF بِمُضيفِ الـSpace</b> — قيسَ بِلا رَمز: ‏200 و
    /// <c>"host":"https://…hf.space"</c>.</summary>
    [Fact]
    public void Deploy_workflow_resolves_the_live_host_without_a_manual_variable()
    {
        var wf = StripHashComments(ReadRepoFile(".github", "workflows", "deploy-hf.yml"));

        Assert.Contains("live_host", wf, StringComparison.Ordinal);
        Assert.Contains(".host // empty", wf, StringComparison.Ordinal);
        Assert.Contains("HF_SPACE_URL", wf, StringComparison.Ordinal);
    }

    // ─── ١٠) النُقطَةُ مُسَجَّلَةٌ فِعلاً في التَطبيق ───────────────────

    /// <summary><b>ولِماذا مَسحُ مَصدَرٍ لا مُضيفٌ مُصَغَّر</b>: المُضيفُ
    /// المُصَغَّرُ **يُسَجِّلُ النُقطَةَ بِيَدِه** ثُمَّ يُسَرُّ بِأَنَّه
    /// وَجَدَها — وَهمُ تَغطِيَةٍ لا تَغطِيَة. الشَيءُ الوَحيدُ الَّذي
    /// يُثبِتُ أَنّ <c>V1.App</c> يُركِّبُها هو هذا، وبُرهانُ التَسجيلِ
    /// الحَيُّ هو <c>scripts/verify-production-boot.sh</c> على إقلاعٍ
    /// إنتاجِيٍّ فِعليّ.</summary>
    [Fact]
    public void Program_registers_the_build_identity_endpoint()
    {
        var program = StripBlockAndLineComments(
            ReadRepoFile("apps", "V1.App", "Program.cs"));

        Assert.Contains("MapBuildIdentity(", program, StringComparison.Ordinal);
        Assert.Contains("AssemblyInformationalVersionAttribute", program, StringComparison.Ordinal);
    }

    /// <summary>والبَوّابَةُ الإنتاجِيَّةُ تَفحَصُ <b>الجِسمَ</b> لا
    /// الرَمزَ وَحدَه. <b>والعِلَّةُ مَقيسَة</b>: ‏`/{slug}` يَبتَلِعُ
    /// `/health` ويَرُدُّ ‏200 بِـ`text/html` — قيسَ على المَوقِعِ
    /// الحَيِّ يَومَ ‏2026-08-30: ‏200 · `text/html` · ‏30,812 بايتاً.
    /// فَبَوّابَةٌ تُقارِنُ الرَمزَ وَحدَه كانَت **سَتَخضَرُّ قَبلَ أَن
    /// يُكتَبَ حَرف**، وحَذفُ سَطرِ التَسجيلِ يَوماً يَمُرُّ بِها
    /// صامِتاً.</summary>
    [Fact]
    public void The_production_boot_gate_checks_the_health_body_not_only_its_code()
    {
        var gate = ReadRepoFile("scripts", "verify-production-boot.sh");

        Assert.Contains("/health", gate, StringComparison.Ordinal);
        Assert.Contains("BODY_MUST_CONTAIN", gate, StringComparison.Ordinal);

        // والرَأسُ يُقاسُ على **الأُنبوبِ الكامِل** لا على مُضيفٍ
        // مُصَغَّر: ‏Serilog وStaticFiles وRouting وحَلُّ المُستَأجِرِ
        // وبَوّابَةُ الإصدار، ومُصَيِّرُ Razor يَضبُطُ `Cache-Control`
        // بِنَفسِه على الصَفَحات. فَهُنا يُثبَتُ أَنّ رَأسَنا هو الواصِل.
        Assert.Contains("HEADER_MUST_CONTAIN", gate, StringComparison.Ordinal);
    }

    // ─── ١١) رَأسُ العَميلِ لا يَقلِبُ جَوابَ نُقطَةِ الهُوِيَّة ────────

    /// <summary><b>العِلَّة</b>: <c>VersionGateMiddleware</c> يَسبِقُ
    /// النِقاطَ في <c>Program.cs</c>، ويَرُدُّ ‏426 بِجِسمِ JSON لِأَيِّ
    /// طَلَبٍ يَحمِلُ <c>X-App-Version</c> أَدنى مِن المَدعوم — <b>بِلا
    /// <c>commit</c> وبِلا <c>no-store</c></b>. فَمُراقِبٌ يَبعَثُ
    /// إصدارَ عَميلِه يَقرَأُ ‏426 دائِماً ويُعلِنُ الخِدمَةَ ساقِطَةً
    /// وهي تَعمَل. ودَعوى «الجَوابُ لا يَعتَمِدُ إلّا على السِمَة»
    /// صادِقَةٌ على **المُعالِج** وكاذِبَةٌ على **المَسار** — وهذا
    /// يُصلِحُ المَسار.</summary>
    [Fact]
    public async Task Health_answers_even_when_the_client_sends_an_unsupported_app_version()
    {
        await using var host = await HealthHost.StartAsync($"1.0.0+{Sha}", Boot, withVersionGate: true);

        using var req = new HttpRequestMessage(HttpMethod.Get, "/health");
        req.Headers.Add("X-App-Version", "0.0.1");
        var res = await host.Client.SendAsync(req);

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.StartsWith("application/json",
            res.Content.Headers.ContentType?.ToString() ?? "", StringComparison.Ordinal);
        Assert.Contains("no-store", res.Headers.CacheControl?.ToString() ?? "",
            StringComparison.OrdinalIgnoreCase);

        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        Assert.Equal(Sha, doc.RootElement.GetProperty("commit").GetString());
    }

    /// <summary>والبَوّابَةُ ما زالَت تَعمَلُ على ما سِواه — وإلّا كانَ
    /// الإعفاءُ ثَقباً في الحارِسِ لا فَتحَةً مَقصودَة.</summary>
    [Fact]
    public async Task The_version_gate_still_rejects_an_old_client_on_other_paths()
    {
        await using var host = await HealthHost.StartAsync($"1.0.0+{Sha}", Boot, withVersionGate: true);

        using var req = new HttpRequestMessage(HttpMethod.Get, "/not-health");
        req.Headers.Add("X-App-Version", "0.0.1");
        var res = await host.Client.SendAsync(req);

        Assert.Equal(HttpStatusCode.UpgradeRequired, res.StatusCode);
    }

    // ─── ١٢) الحَجزُ بِعِلَّتِه مَكتوبَةً ───────────────────────────────

    /// <summary><b>ولِمَ هذا لَيسَ تَحصيلَ حاصِل</b>: بِلا الحَجز،
    /// <c>TenantResolverMiddleware</c> يَستَعلِمُ عَن مُستَأجِرٍ اسمُه
    /// «‏health» عِندَ **كُلِّ استِطلاع** — فَتَصيرُ نُقطَةُ الهُوِيَّةِ
    /// مَرتَبِطَةً بِالقاعِدَةِ **مِن البابِ الخَلفِيّ**، وهو ما لا تَراهُ
    /// الحالَةُ ٣ لِأَنَّها تَمسَحُ <c>BuildIdentity.cs</c> لا
    /// الوُسَطاء.</summary>
    [Fact]
    public void Health_is_a_reserved_slug_so_no_tenant_lookup_runs_on_every_probe()
        => Assert.Contains("health", ReservedTenantSlugs.All);

    // ─── أَداة ────────────────────────────────────────────────────────

    /// <summary>يُقرَأُ الكودُ لا التَعليق: ذِكرُ <c>Environment.</c> في
    /// تَعليقٍ شارِحٍ لَيسَ مَوضِعَ نِداء، وعَدُّه كَذلكَ يَجعَلُ الأَداةَ
    /// تَتَّهِمُ الوَثيقَةَ بِأَنَّها كود.</summary>
    private static string StripBlockAndLineComments(string text)
    {
        text = Regex.Replace(text, @"/\*.*?\*/", " ", RegexOptions.Singleline);
        text = Regex.Replace(text, @"//[^\n]*", " ");
        return text;
    }

    /// <summary><b>تَعليقُ <c>#</c> في YAML وDockerfile — والأَداةُ
    /// قيسَت بِخَطَئِها قَبلَ أَن يُوثَقَ بِها</b> (القاعِدَة ١٠).
    ///
    /// <para>أَوَّلُ نُسخَةٍ مِن هذَينِ الفاحِصَينِ قَرَأَت النَصَّ خاماً،
    /// فَوَقَعَت في العَطَبِ الَّذي تَحرُسُه بِعَينِه — <b>مَرَّتَين</b>:
    /// حارِسُ الـ<c>Dockerfile</c> اتَّهَمَ التَعليقَ الَّذي <b>يَشرَحُ
    /// لِماذا رُفِضَ <c>ARG SOURCE_REVISION_ID=unknown</c></b> بِأَنَّه
    /// الاستِعمالُ نَفسُه؛ وحارِسُ التَرتيبِ وَجَدَ «<c>git add -A</c>»
    /// في تَعليقٍ يَسبِقُ الكِتابَةَ فَحَكَمَ أَنّ البَصمَةَ تُكتَبُ
    /// بَعدَ الإيداع. <b>الأَداةُ كانَت تَكذِب، لا المَفحوص.</b></para>
    ///
    /// <para>ويُشطَبُ السَطرُ الَّذي أَوَّلُ مِحرَفٍ غَيرِ فارِغٍ فيه
    /// <c>#</c> — لا كُلُّ <c>#</c> أَينَما وَقَع، فَذاكَ يَبتُرُ
    /// سَلاسِلَ الأَوامِرِ في <c>run:</c>.</para></summary>
    private static string StripHashComments(string text)
        => Regex.Replace(text, @"(?m)^[ \t]*#.*$", "");
}
