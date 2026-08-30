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
// يَرُدُّ ‏401 بِلا رَمز؛ ورَأسُ `x-proxied-replica` يُثبِتُ **تَبَدُّلَ
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
    public static async Task<HealthHost> StartAsync(
        string? informationalVersion,
        DateTimeOffset startedAt,
        bool withVersionGate = false)
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
        await app.StartAsync();

        var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        {
            BaseAddress = new Uri($"http://127.0.0.1:{port}"),
            Timeout = TimeSpan.FromSeconds(20),
        };

        return new HealthHost(app, client);
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
        var docker = ReadRepoFile("Dockerfile");

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
        var wf = ReadRepoFile(".github", "workflows", "deploy-hf.yml");

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
        var wf = ReadRepoFile(".github", "workflows", "deploy-hf.yml");

        Assert.Contains("/health", wf, StringComparison.Ordinal);
        Assert.Contains("application/json", wf, StringComparison.Ordinal);
        Assert.Contains("no-store", wf, StringComparison.Ordinal);
        Assert.Contains("HF_SPACE_URL", wf, StringComparison.Ordinal);

        // خَطُّ الأَساسِ يُقاسُ **قَبلَ الدَفع**: بِدونِه تَخضَرُّ
        // إعادَةُ تَشغيلِ الوَظيفَةِ على نَفسِ الإيداعِ خِلالَ ثَوانٍ
        // على **الحاوِيَةِ القَديمَة** — بَوّابَةٌ خَضراءُ قَبلَ أَن
        // تَعمَلَ لَيسَت بَوّابَة.
        Assert.Contains("base_started", wf, StringComparison.Ordinal);

        // والتَعليقُ الَّذي كانَ يَقتَرِحُ النُقطَةَ يُحذَف — فَقَد
        // نُفِّذَت. اقتِراحٌ باقٍ بَعدَ التَنفيذِ يُوَرِّثُ القارِئَ شَكّاً.
        Assert.DoesNotContain("اقتِراحُ نُقطَة", wf, StringComparison.Ordinal);
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
}
