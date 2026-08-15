#!/usr/bin/env dotnet
// ═══════════════════════════════════════════════════════════════════════
//  الطَبَقَة السادِسَة — التَحَقُّق البَصَرِيّ الآلِيّ مِن صَفحَة حَيَّة
// ───────────────────────────────────────────────────────────────────────
//  هذا مُقابِل `verify-runtime.mjs` في المُستَودَع القَديم — بِنَفس
//  عائِلات الفَحص، وبِلا Playwright وبِلا Node.
//
//  **لِماذا لا Playwright؟** لِأَنّ Node غَير مَوجود على هذا الجِهاز
//  إطلاقاً (مُقاس لا مُقَدَّر: `where node` فارِغ، ومَسح القُرصَين
//  C: و D: بِعُمق ٤ لَم يَجِد `node.exe`). وغِياب الأَداة لَيسَ غِياب
//  القُدرَة: Chrome مُثَبَّت، ويَتَكَلَّم **CDP** عَبر WebSocket،
//  و.NET — وهي عُدَّة المُستَودَع أَصلاً — فيها `ClientWebSocket`.
//  فَالمُحَرِّك هو نَفسُه مُحَرِّك Playwright (Chromium + CDP)؛
//  المُتَغَيِّر هو لِسان القِيادَة فَقَط.
//
//  ولِماذا مِلَفّ `.cs` مُفرَد بِلا `.csproj`؟ لِئَلّا يَدخُل مَشروع
//  جَديد في الحَلّ فَيَمَسّ البِناء أَو عَدَّ الاختِبارات. تَشغيل
//  المِلَفّ المُفرَد مُتاح في .NET 10.
//
//  الاستِعمال:
//     dotnet run scripts/verify-runtime.cs -- <url> [url...]
//     dotnet run scripts/verify-runtime.cs -- --viewport 390x844 <url>
//     dotnet run scripts/verify-runtime.cs -- --report-only <url>
//     dotnet run scripts/verify-runtime.cs -- --json out.json <url>
//
//  رَمز الخُروج: ٠ إن لَم تُوجَد مُخالَفَة، ١ إن وُجِدَت
//  (إلّا مَع `--report-only`)، ٢ عِند عَطَب تَشغيلِيّ.
// ═══════════════════════════════════════════════════════════════════════

using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

// ─── وُسَطاء سَطر الأَمر ────────────────────────────────────────────
var urls = new List<string>();
int vpW = 1280, vpH = 900;
bool reportOnly = false;
string? jsonOut = null;
string? injectCss = null;
var cookies = new List<(string Name, string Value)>();
string contractsPath = Path.Combine(AppContext.BaseDirectory, "spatial-contracts.json");
// المِلَفّ المُفرَد يُبنى في مُجَلَّد مُؤَقَّت، فَالعُقود تُطلَب بِجِوار المَصدَر.
string srcDir = Path.GetDirectoryName(Path.GetFullPath(GetSourcePath())) ?? ".";
if (!File.Exists(contractsPath)) contractsPath = Path.Combine(srcDir, "spatial-contracts.json");

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--viewport":
            var parts = args[++i].Split('x');
            vpW = int.Parse(parts[0]); vpH = int.Parse(parts[1]);
            break;
        case "--report-only": reportOnly = true; break;
        case "--inject-css": injectCss = args[++i]; break;
        case "--inject-preset": injectCss = Presets(args[++i]); break;
        // ─── جَلسَة: صَفحَةٌ مَحروسَة لا يَراها زائِر ─────────────────
        // الأَداةُ بِلا هذا **عَمياءُ بِالبِنيَة** عَن كُلّ `/me/*` و
        // `/studio/*` و`/admin/*`: تُصَيَّر لَها صَفحَةُ «سَجِّل
        // دُخولَك» فَتُعطي «صِفر مُخالَفَة» عَن تَخطيطٍ لَم تَرَه قَطّ.
        // ونَفسُ الحاجَة حُلَّت مَرَّةً في `capture-appearance.sh`
        // (‏`GUARDED_PAGES`)؛ والقاعِدَة ٨ تَقول: أَصلِح الأُنبوبَ
        // القائِم ولا تَبنِ رابِعاً.
        case "--cookie":
        {
            var kv = args[++i];
            var eq = kv.IndexOf('=');
            if (eq <= 0) { Console.Error.WriteLine($"✗ --cookie يَحتاج name=value، ووَصَلَ: {kv}"); return 2; }
            cookies.Add((kv[..eq], kv[(eq + 1)..]));
            break;
        }
        case "--json": jsonOut = args[++i]; break;
        case "--contracts": contractsPath = args[++i]; break;
        default: urls.Add(args[i]); break;
    }
}

if (urls.Count == 0)
{
    urls.AddRange(new[]
    {
        "http://localhost:5050/ashare",
        "http://localhost:5050/ashare/explore",
        "http://localhost:5050/ejar",
        "http://localhost:5050/theme-demo",
        "http://localhost:5050/owner-test",
    });
}

if (!File.Exists(contractsPath))
{
    Console.Error.WriteLine($"✗ مِلَفّ العُقود غَير مَوجود: {contractsPath}");
    return 2;
}
string contractsJson = File.ReadAllText(contractsPath);

// ─── العُثور على Chrome ─────────────────────────────────────────────
string? chrome = new[]
{
    @"C:\Program Files\Google\Chrome\Application\chrome.exe",
    @"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe",
    @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
    @"C:\Program Files\Microsoft\Edge\Application\msedge.exe",
    "/usr/bin/google-chrome", "/usr/bin/chromium", "/usr/bin/chromium-browser",
}.FirstOrDefault(File.Exists);

if (chrome is null)
{
    Console.Error.WriteLine("✗ لَم يُعثَر على Chrome/Edge — الطَبَقَة السادِسَة تَحتاج مُحَرِّك Chromium.");
    return 2;
}

int port = FreePort();
string profile = Path.Combine(Path.GetTempPath(), "wsl-verify-" + Guid.NewGuid().ToString("N")[..8]);
Directory.CreateDirectory(profile);

var psi = new ProcessStartInfo(chrome)
{
    RedirectStandardError = true,
    RedirectStandardOutput = true,
    UseShellExecute = false,
};
foreach (var a in new[]
{
    "--headless=new", "--disable-gpu", "--no-sandbox", "--no-first-run",
    "--disable-extensions", "--disable-background-networking",
    "--disable-features=Translate,MediaRouter",
    "--hide-scrollbars", "--mute-audio",
    $"--window-size={vpW},{vpH}",
    $"--user-data-dir={profile}",
    $"--remote-debugging-port={port}",
    "about:blank",
}) psi.ArgumentList.Add(a);

using var proc = Process.Start(psi)!;
proc.BeginErrorReadLine(); proc.ErrorDataReceived += (_, _) => { };
proc.BeginOutputReadLine(); proc.OutputDataReceived += (_, _) => { };

int exitCode = 2;
try
{
    string wsUrl = await WaitForDebugger(port);
    using var ws = new ClientWebSocket();
    await ws.ConnectAsync(new Uri(wsUrl), CancellationToken.None);

    var cdp = new Cdp(ws);
    await cdp.Send("Page.enable");
    await cdp.Send("Runtime.enable");
    await cdp.Send("Emulation.setDeviceMetricsOverride",
        $$"""{"width":{{vpW}},"height":{{vpH}},"deviceScaleFactor":1,"mobile":{{(vpW < 768).ToString().ToLowerInvariant()}}}""");

    // الكوكيّات تُزرَع قَبلَ أَوَّل تَنَقُّل، وإلّا وَصَلَ الطَلَبُ
    // الأَوَّل مَجهولاً فَقيسَ فَرعَ الرَفض.
    if (cookies.Count > 0)
    {
        await cdp.Send("Network.enable");
        foreach (var (name, value) in cookies)
            await cdp.Send("Network.setCookie",
                $$"""{"name":{{Cdp.JStr(name)}},"value":{{Cdp.JStr(value)}},"domain":"localhost","path":"/"}""");
    }

    string js = BuildScript(contractsJson);

    var allReports = new List<PageReport>();
    int total = 0;

    Console.WriteLine();
    Console.WriteLine("══════════════════════════════════════════════════════════");
    Console.WriteLine($"  الطَبَقَة ٦ — تَحَقُّق حَيّ  ·  نافِذَة {vpW}×{vpH}  ·  {urls.Count} صَفحَة");
    // عَدّادٌ يُقال: جَولَةٌ بِجَلسَة وجَولَةٌ بِلا جَلسَة تُعطِيانِ
    // «صِفر مُخالَفَة» مُتَشابِهاً على صَفحَتَين مُختَلِفَتَين تَماماً.
    if (cookies.Count > 0)
        Console.WriteLine($"  · جَلسَة: {cookies.Count} كوكي مَزروع ({string.Join(", ", cookies.Select(c => c.Name))})");
    if (injectCss is not null)
    {
        // لا تَدَع جَولَةً مَحقونَة تَتَنَكَّر في صورَة قِياس نَظيف
        Console.WriteLine("  ⚠ عَطَبٌ مَحقون في الذاكِرَة — هذِه جَولَة بُرهان، لا قِياس نَظيف");
        Console.WriteLine($"     {injectCss[..Math.Min(96, injectCss.Length)]}…");
    }
    Console.WriteLine("══════════════════════════════════════════════════════════");

    foreach (var url in urls)
    {
        await cdp.Send("Page.navigate", $$"""{"url":{{Cdp.JStr(url)}}}""");
        await cdp.WaitForEvent("Page.loadEventFired", TimeSpan.FromSeconds(30));
        await Task.Delay(900); // اِستِقرار Blazor بَعدَ الحَدَث

        if (injectCss is not null)
        {
            // يُحقَن بَعدَ الاستِقرار لِيَغلِب على أَنماط الصَفحَة
            string inj = "(function(){var s=document.createElement('style');s.id='__fault__';" +
                         "s.textContent=" + Cdp.JStr(injectCss) + ";document.head.appendChild(s);" +
                         "return document.getElementById('__fault__') !== null;})()";
            var ir = await cdp.Send("Runtime.evaluate", $$"""{"expression":{{Cdp.JStr(inj)}},"returnByValue":true}""");
            bool applied = ir.RootElement.GetProperty("result").TryGetProperty("value", out var av) && av.ValueKind == JsonValueKind.True;
            if (!applied) Console.WriteLine("      ⚠ تَعَذَّرَ حَقن العَطَب");
            await Task.Delay(400); // إعادَة التَخطيط
        }

        var res = await cdp.Send("Runtime.evaluate",
            $$"""{"expression":{{Cdp.JStr(js)}},"returnByValue":true,"awaitPromise":true}""");

        var root = res.RootElement;
        if (root.TryGetProperty("exceptionDetails", out var ex))
        {
            Console.WriteLine($"\n  ✗ {url} — عَطَب في السِكرِبت: {ex}");
            total++;
            continue;
        }

        var value = root.GetProperty("result").GetProperty("value");
        var violations = new List<Violation>();
        foreach (var v in value.GetProperty("violations").EnumerateArray())
        {
            violations.Add(new Violation(
                v.GetProperty("category").GetString() ?? "?",
                v.GetProperty("message").GetString() ?? "?",
                v.TryGetProperty("selector", out var s) ? s.GetString() : null));
        }
        int elements = value.GetProperty("stats").GetProperty("elements").GetInt32();
        int checks = value.GetProperty("stats").GetProperty("checks").GetInt32();
        string byFamily = value.GetProperty("stats").GetProperty("byFamily").GetString() ?? "";

        allReports.Add(new PageReport(url, elements, checks, violations));
        total += violations.Count;

        string mark = violations.Count == 0 ? "✓" : "✗";
        Console.WriteLine();
        Console.WriteLine($"  {mark} {url}");
        Console.WriteLine($"      عَناصِر: {elements}   ·   تَأكيدات: {checks}   ·   مُخالَفات: {violations.Count}");
        Console.WriteLine($"      قِياسات: {byFamily}");

        foreach (var g in violations.GroupBy(v => v.Category).OrderBy(g => g.Key))
        {
            Console.WriteLine($"      ── {g.Key} ({g.Count()})");
            foreach (var v in g.Take(12)) Console.WriteLine($"         • {v.Message}");
            if (g.Count() > 12) Console.WriteLine($"         … و{g.Count() - 12} أُخرى");
        }
    }

    Console.WriteLine();
    Console.WriteLine("══════════════════════════════════════════════════════════");
    Console.WriteLine(total == 0
        ? $"  ✓ لا مُخالَفَة — {allReports.Sum(r => r.Checks)} تَأكيداً على {allReports.Count} صَفحَة"
        : $"  ✗ {total} مُخالَفَة على {allReports.Count} صَفحَة");
    Console.WriteLine("══════════════════════════════════════════════════════════");
    Console.WriteLine();

    if (jsonOut is not null)
    {
        var sb = new StringBuilder();
        sb.Append("{\n  \"startedAt\": ").Append(Cdp.JStr(DateTime.UtcNow.ToString("o")));
        sb.Append(",\n  \"viewport\": ").Append(Cdp.JStr($"{vpW}x{vpH}"));
        sb.Append(",\n  \"totalViolations\": ").Append(total);
        sb.Append(",\n  \"pages\": [\n");
        for (int i = 0; i < allReports.Count; i++)
        {
            var r = allReports[i];
            sb.Append("    { \"url\": ").Append(Cdp.JStr(r.Url));
            sb.Append(", \"elements\": ").Append(r.Elements);
            sb.Append(", \"checks\": ").Append(r.Checks);
            sb.Append(", \"violations\": [\n");
            for (int j = 0; j < r.Violations.Count; j++)
            {
                var v = r.Violations[j];
                sb.Append("      { \"category\": ").Append(Cdp.JStr(v.Category));
                sb.Append(", \"message\": ").Append(Cdp.JStr(v.Message));
                sb.Append(", \"selector\": ").Append(v.Selector is null ? "null" : Cdp.JStr(v.Selector));
                sb.Append(" }").Append(j < r.Violations.Count - 1 ? ",\n" : "\n");
            }
            sb.Append("    ] }").Append(i < allReports.Count - 1 ? ",\n" : "\n");
        }
        sb.Append("  ]\n}\n");
        File.WriteAllText(jsonOut, sb.ToString());
        Console.WriteLine($"  التَقرير: {jsonOut}");
    }

    exitCode = (total > 0 && !reportOnly) ? 1 : 0;
}
catch (Exception e)
{
    Console.Error.WriteLine($"✗ عَطَب تَشغيلِيّ: {e.Message}");
    exitCode = 2;
}
finally
{
    try { if (!proc.HasExited) proc.Kill(true); } catch { }
    try { Directory.Delete(profile, true); } catch { }
}
return exitCode;

// ═══════════════════════════════════════════════════════════════════
static string GetSourcePath([System.Runtime.CompilerServices.CallerFilePath] string p = "") => p;

// ─── حَقن العَطَب — بُرهان أَنّ الأَداة تَرى ───────────────────────
//  «صِفر مُخالَفَة» لا يُثبِت أَنّ الأَداة تَرى؛ قَد تَسكُت لِأَنَّها
//  لا تَفحَص شَيئاً. فَالبُرهان أَن يُحدَث عَطَبٌ مَعلوم، ويُثبَت أَنّ
//  الأَداة تَصرُخ وتُحَدِّد المَوضِع، ثُمَّ يُرفَع فَتَسكُت.
//
//  والحَقن يَجري **في الذاكِرَة عَبر CDP**، لا بِتَعديل مِلَفّ CSS في
//  المُستَودَع. وهذا أَسلَم وأَقوى مَعاً: أَسلَم لِأَنَّه لا يُخَلِّف
//  أَثَراً يُنسى إرجاعُه ولا يُزاحِم وَكيلاً آخَرَ يَعمَل في libs/؛
//  وأَقوى لِأَنّ البُرهان يَصير **قابِلاً لِلتَكرار** في كُلّ جَولَة
//  بَدَلَ تَعديلٍ يَدَوِيّ يُثبِت مَرَّةً ولا يُخَلِّف شَيئاً.
static string Presets(string name) => name switch
{
    // تَبايُن دونَ العَتَبَة: رَماديّ فاتِح على خَلفِيَّة فاتِحَة
    "contrast" => ".ac-space-title, .acm-role-landing-card, h1, h2 { color: #b9bfc7 !important; }",
    // فَيَضان: اِبن أَعرَض مِن حاوِيَتِه
    "overflow" => ".ac-space-body, .acm-role-landing-card { width: 3000px !important; max-width: none !important; }",
    // تَداخُل: بِطاقات تَركَب بَعضَها
    "overlap" => ".ac-space, .acm-role-landing-card { position: absolute !important; top: 120px !important; left: 40px !important; width: 400px !important; height: 300px !important; }",
    // هَدَف لَمس أَصغَر مِن الحَدّ
    "touch" => ".acm-role-landing-card-cta, .acs-bottom-nav-item, .acm-mobile-nav-btn { height: 12px !important; min-height: 0 !important; padding: 0 !important; overflow: hidden !important; }",
    _ => throw new ArgumentException($"عَطَب غَير مَعروف: {name} (المُتاح: contrast, overflow, overlap, touch)"),
};

static int FreePort()
{
    var l = new TcpListener(IPAddress.Loopback, 0);
    l.Start();
    int p = ((IPEndPoint)l.LocalEndpoint).Port;
    l.Stop();
    return p;
}

static async Task<string> WaitForDebugger(int port)
{
    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
    for (int i = 0; i < 100; i++)
    {
        try
        {
            var s = await http.GetStringAsync($"http://127.0.0.1:{port}/json/list");
            using var doc = JsonDocument.Parse(s);
            foreach (var t in doc.RootElement.EnumerateArray())
            {
                if (t.GetProperty("type").GetString() == "page" &&
                    t.TryGetProperty("webSocketDebuggerUrl", out var w))
                    return w.GetString()!;
            }
        }
        catch { }
        await Task.Delay(200);
    }
    throw new Exception("تَعَذَّرَ الاتِّصال بِـCDP — لَم يَبدَأ Chrome في الوَقت المَسموح.");
}

static string BuildScript(string contractsJson) => "(function(C){\n" + Scripts.Body + "\n})(" + contractsJson + ")";

// ─── جِسر CDP ───────────────────────────────────────────────────────
sealed class Cdp(ClientWebSocket ws)
{
    int _id = 0;

    /// <summary>تَرميز سِلسِلَة كَـJSON بِلا انعِكاس — التَشغيل المُفرَد يُعَطِّل الانعِكاس.</summary>
    public static string JStr(string s)
    {
        var sb = new StringBuilder(s.Length + 16).Append('"');
        foreach (var c in s)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20 || c > 0x7E) sb.Append("\\u").Append(((int)c).ToString("x4"));
                    else sb.Append(c);
                    break;
            }
        }
        return sb.Append('"').ToString();
    }

    public async Task<JsonDocument> Send(string method, string? paramsJson = null)
    {
        int id = ++_id;
        var payload = $$"""{"id":{{id}},"method":{{JStr(method)}},"params":{{paramsJson ?? "{}"}}}""";
        await ws.SendAsync(Encoding.UTF8.GetBytes(payload), WebSocketMessageType.Text, true, CancellationToken.None);

        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (DateTime.UtcNow < deadline)
        {
            var doc = await Receive();
            if (doc.RootElement.TryGetProperty("id", out var rid) && rid.GetInt32() == id)
            {
                if (doc.RootElement.TryGetProperty("error", out var err))
                    throw new Exception($"CDP {method}: {err}");
                return JsonDocument.Parse(doc.RootElement.GetProperty("result").GetRawText());
            }
        }
        throw new Exception($"CDP {method}: اِنتَهَت المُهلَة.");
    }

    public async Task WaitForEvent(string name, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var doc = await Receive();
            if (doc.RootElement.TryGetProperty("method", out var m) && m.GetString() == name) return;
        }
        // لا نَرمي: بَعض الصَفَحات لا تُطلِق الحَدَث؛ نُكمِل بِالمُهلَة.
    }

    async Task<JsonDocument> Receive()
    {
        var buf = new byte[64 * 1024];
        var sb = new StringBuilder();
        while (true)
        {
            var r = await ws.ReceiveAsync(buf, CancellationToken.None);
            sb.Append(Encoding.UTF8.GetString(buf, 0, r.Count));
            if (r.EndOfMessage) break;
        }
        return JsonDocument.Parse(sb.ToString());
    }
}

record Violation(string Category, string Message, string? Selector);
record PageReport(string Url, int Elements, int Checks, List<Violation> Violations);

// ═══════════════════════════════════════════════════════════════════
//  جِسم السِكرِبت — يَعمَل داخِل الصَفحَة، ويُعيد JSON
//  عائِلات الفَحص مَنقولَة عَن `verify-runtime.mjs`:
//   A أَنماط · B تَموضُع · C احتِواء · D مُحاذاة · E تَداخُل
//   F قِيَم مَحسوبَة · G تَبايُن WCAG · H صُندوق · I فَيَضان نَصّ
// ═══════════════════════════════════════════════════════════════════
static class Scripts
{
    public const string Body = """
    var V = [], checks = 0, CK = {};
    function add(cat, msg, sel) { V.push({ category: cat, message: msg, selector: sel || null }); }
    // عَدّاد لِكُلّ عائِلَة — لِأَنّ «صِفر مُخالَفَة» لا مَعنى لَها
    // ما لَم يُعرَف كَم تَأكيداً جَرى فِعلاً. صِفرٌ مِن صِفر لَيسَ نَجاحاً.
    function ck(f) { checks++; CK[f] = (CK[f] || 0) + 1; }
    function px(s) { var m = /^(-?\d+(?:\.\d+)?)px/.exec(s || ''); return m ? parseFloat(m[1]) : null; }
    function q(sel) { try { return Array.prototype.slice.call(document.querySelectorAll(sel)); } catch (e) { return []; } }
    function shown(n) {
        if (n !== document.body && !n.offsetParent) {
            var cs = getComputedStyle(n);
            if (cs.position !== 'fixed' || cs.display === 'none') return false;
        }
        var r = n.getBoundingClientRect();
        return r.width > 0 && r.height > 0;
    }
    function R(n) {
        var r = n.getBoundingClientRect();
        return { top: r.top, left: r.left, right: r.right, bottom: r.bottom, width: r.width, height: r.height };
    }
    // اِسم العُنصُر نَفسِه — لا صَدى المُحَدِّد. المُحَدِّد المُرَكَّب
    // يَطبَع سَطراً لا يَدُلّ على مَوضِع، والتَقرير الَّذي لا يُوَضِّع لا يُصلَح بِه.
    function nm(e) {
        if (!e) return '?';
        return (e.className && typeof e.className === 'string' && e.className.trim())
            ? '.' + e.className.trim().split(/\s+/).slice(0, 2).join('.')
            : '<' + e.tagName.toLowerCase() + '>';
    }

    // ─── A. عُقود الأَنماط ─────────────────────────────────────────
    var contracts = C.style_contracts || {};
    for (var sel in contracts) {
        var contract = contracts[sel], els = q(sel);
        for (var i = 0; i < Math.min(els.length, 20); i++) {
            var n = els[i]; if (!shown(n)) continue;
            var s = getComputedStyle(n); ck('A-style');
            var mins = contract['min-values'] || {};
            if (mins.padding !== undefined) {
                var m = Math.min(px(s.paddingTop) || 0, px(s.paddingRight) || 0, px(s.paddingBottom) || 0, px(s.paddingLeft) || 0);
                if (m < mins.padding) add('A-style', nm(n) + ': padding ' + m + 'px < ' + mins.padding + 'px', sel);
            }
            if (mins['min-height'] !== undefined) {
                var h = Math.max(px(s.minHeight) || 0, n.offsetHeight || 0);
                if (h < mins['min-height']) add('A-style', nm(n) + ': height ' + h + 'px < ' + mins['min-height'] + 'px', sel);
            }
            if (mins['border-width'] !== undefined) {
                var b = Math.max(px(s.borderTopWidth) || 0, px(s.borderRightWidth) || 0, px(s.borderBottomWidth) || 0, px(s.borderLeftWidth) || 0);
                if (b < mins['border-width']) add('A-style', nm(n) + ': border ' + b + 'px < ' + mins['border-width'] + 'px', sel);
            }
            if (mins['font-weight'] !== undefined) {
                var f = parseInt(s.fontWeight) || 400;
                if (f < mins['font-weight']) add('A-style', nm(n) + ': font-weight ' + f + ' < ' + mins['font-weight'], sel);
            }
            if ((contract.required || []).indexOf('background') >= 0 && /rgba?\([^)]*,\s*0\s*\)/.test(s.backgroundColor))
                add('A-style', nm(n) + ': خَلفِيَّة شَفّافَة — العُنصُر غَير مَرئِيّ', sel);
        }
    }

    // ─── B. قَواعِد التَموضُع ──────────────────────────────────────
    (C.position_rules || []).forEach(function (rule) {
        var n = document.querySelector(rule.selector); if (!n || !shown(n)) return;
        var tol = rule.tolerance_px == null ? 2 : rule.tolerance_px, r = R(n); ck('B-position');
        if (rule.rule === 'anchored-viewport-bottom') {
            if (Math.abs(r.bottom - window.innerHeight) > tol)
                add('B-position', rule.selector + ': bottom=' + r.bottom.toFixed(1) + '، والمُتَوَقَّع ≈' + window.innerHeight, rule.selector);
        } else if (rule.rule === 'sticky-top') {
            var before = window.scrollY;
            window.scrollTo(0, 300);
            var t2 = n.getBoundingClientRect().top;
            window.scrollTo(0, before);
            if (Math.abs(t2) > tol)
                add('B-position', rule.selector + ': يَدَّعي sticky-top لَكِنّ top=' + t2.toFixed(1) + ' بَعدَ التَمرير', rule.selector);
        } else if (rule.rule === 'attached-top-of-parent') {
            var p = n.offsetParent || n.parentElement;
            if (p && r.top > p.getBoundingClientRect().top + tol)
                add('B-position', rule.selector + ': top=' + r.top.toFixed(1) + '، وأَعلى الأَب=' + p.getBoundingClientRect().top.toFixed(1), rule.selector);
        }
    });

    // ─── C. الاحتِواء ──────────────────────────────────────────────
    (C.containment_rules || []).forEach(function (rule) {
        q(rule.parent).forEach(function (p) {
            if (!shown(p)) return;
            var pr = R(p), axis = rule.axis || 'both';
            var kids = rule.children === '> *'
                ? Array.prototype.slice.call(p.children)
                : Array.prototype.slice.call(p.querySelectorAll(rule.children));
            kids.forEach(function (k) {
                if (!shown(k)) return;
                var kr = R(k); ck('C-containment');
                if ((axis === 'horizontal' || axis === 'both') && (kr.left < pr.left - 1 || kr.right > pr.right + 1))
                    add('C-containment', rule.children + ' يَفيض خارِج ' + rule.parent + ' أُفُقِيّاً (' +
                        kr.left.toFixed(0) + '..' + kr.right.toFixed(0) + ' مُقابِل ' + pr.left.toFixed(0) + '..' + pr.right.toFixed(0) + ')', rule.children);
                if (axis === 'both' && (kr.top < pr.top - 1 || kr.bottom > pr.bottom + 1))
                    add('C-containment', rule.children + ' يَفيض خارِج ' + rule.parent + ' رَأسِيّاً (' +
                        kr.top.toFixed(0) + '..' + kr.bottom.toFixed(0) + ' مُقابِل ' + pr.top.toFixed(0) + '..' + pr.bottom.toFixed(0) + ')', rule.children);
            });
        });
    });

    // ─── D. مُحاذاة الإِخوَة ───────────────────────────────────────
    (C.sibling_alignment_rules || []).forEach(function (rule) {
        q(rule.container).forEach(function (c) {
            if (!shown(c)) return;
            var kids = Array.prototype.slice.call(c.children).filter(shown).map(R);
            if (kids.length < 2) return;
            ck('D-alignment');
            var tol = rule.tolerance_px == null ? 4 : rule.tolerance_px;
            var ref = kids[0].top + kids[0].height / 2;
            var bad = kids.filter(function (k) { return Math.abs((k.top + k.height / 2) - ref) > tol; }).length;
            if (bad > 0)
                add('D-alignment', rule.container + ': ' + bad + '/' + kids.length + ' مِن الأَبناء خارِج مَركَز المُحاذاة (سَماح ' + tol + 'px)', rule.container);
        });
    });

    // ─── E. عَدَم التَداخُل ────────────────────────────────────────
    (C.no_overlap_rules || []).forEach(function (rule) {
        (rule.selectors || []).forEach(function (sel) {
            var rects = q(sel).filter(shown).map(R);
            if (rects.length < 2) return;
            ck('E-overlap');
            var n = 0;
            for (var i = 0; i < rects.length; i++)
                for (var j = i + 1; j < rects.length; j++) {
                    var a = rects[i], b = rects[j];
                    var ox = Math.max(0, Math.min(a.right, b.right) - Math.max(a.left, b.left));
                    var oy = Math.max(0, Math.min(a.bottom, b.bottom) - Math.max(a.top, b.top));
                    if (ox > 4 && oy > 4) n++;
                }
            if (n > 0) add('E-overlap', sel + ': ' + n + ' زَوجاً مُتَداخِلاً', sel);
        });
    });

    // ─── F. القِيَم المَحسوبَة ─────────────────────────────────────
    (C.computed_value_rules || []).forEach(function (rule) {
        var maxV = rule.max_violations == null ? 50 : rule.max_violations, count = 0;
        var els = q(rule.selector);
        if (rule.rule === 'font-size-on-scale') {
            var tol = rule.tolerance_px == null ? 0.5 : rule.tolerance_px;
            for (var i = 0; i < els.length && count < maxV; i++) {
                var n = els[i];
                if (!shown(n)) continue;
                // نَصّ مُباشِر فَقَط — لا نَصّ الأَبناء
                var own = '';
                for (var c = 0; c < n.childNodes.length; c++)
                    if (n.childNodes[c].nodeType === 3) own += n.childNodes[c].textContent;
                if (own.trim().length === 0) continue;
                var fs = px(getComputedStyle(n).fontSize); if (fs == null) continue;
                ck('F-computed');
                var ok = rule.allowed_px.some(function (a) { return Math.abs(a - fs) <= tol; });
                if (!ok) { add('F-computed', 'مَقاس خَطّ خارِج السُلَّم ' + fs + 'px على <' + n.tagName.toLowerCase() + '> «' + own.trim().slice(0, 24) + '»', rule.selector); count++; }
            }
        } else if (rule.rule === 'has-visible-border') {
            for (var i = 0; i < els.length && count < maxV; i++) {
                if (!shown(els[i])) continue;
                ck('F-computed');
                var bw = getComputedStyle(els[i]).borderTopWidth;
                if ((px(bw) || 0) < 0.5) { add('F-computed', nm(els[i]) + ': border-width=' + bw + ' — الحَقل غَير مَرئِيّ', rule.selector); count++; }
            }
        } else if (rule.rule === 'min-touch-target') {
            var minH = rule.min_height_px == null ? 32 : rule.min_height_px;
            for (var i = 0; i < els.length && count < maxV; i++) {
                if (!shown(els[i])) continue;
                ck('F-computed');
                var h = els[i].getBoundingClientRect().height;
                if (h < minH) { add('F-computed', nm(els[i]) + ': ارتِفاع ' + h.toFixed(1) + 'px < ' + minH + 'px — أَصغَر مِن هَدَف اللَمس', rule.selector); count++; }
            }
        }
    });

    // ─── G. تَبايُن WCAG AA ────────────────────────────────────────
    function parseRgb(s) {
        var m = /rgba?\(\s*([\d.]+)[\s,]+([\d.]+)[\s,]+([\d.]+)(?:[\s,/]+([\d.]+))?/.exec(s || '');
        return m ? { r: +m[1], g: +m[2], b: +m[3], a: m[4] == null ? 1 : +m[4] } : null;
    }
    function lum(c) {
        var a = [c.r, c.g, c.b].map(function (v) {
            v /= 255; return v <= 0.03928 ? v / 12.92 : Math.pow((v + 0.055) / 1.055, 2.4);
        });
        return 0.2126 * a[0] + 0.7152 * a[1] + 0.0722 * a[2];
    }
    function ratio(f, b) {
        var l1 = lum(f), l2 = lum(b), hi = Math.max(l1, l2), lo = Math.min(l1, l2);
        return (hi + 0.05) / (lo + 0.05);
    }
    // دَمج لَون شِبه شَفّاف فَوقَ خَلفِيَّتِه
    function over(fg, bg) {
        if (fg.a >= 0.999) return fg;
        return { r: fg.r * fg.a + bg.r * (1 - fg.a), g: fg.g * fg.a + bg.g * (1 - fg.a), b: fg.b * fg.a + bg.b * (1 - fg.a), a: 1 };
    }
    (C.contrast_rules || []).forEach(function (rule) {
        var maxV = rule.max_violations == null ? 10 : rule.max_violations, count = 0;
        var els = q(rule.selector);
        for (var i = 0; i < els.length && count < maxV; i++) {
            var n = els[i];
            if (!shown(n)) continue;
            var own = '';
            for (var c = 0; c < n.childNodes.length; c++)
                if (n.childNodes[c].nodeType === 3) own += n.childNodes[c].textContent;
            own = own.trim();
            if (own.length === 0) continue;
            var s = getComputedStyle(n);
            if (parseFloat(s.opacity) < 0.15) continue;
            // صُعود الشَجَرَة حَتّى خَلفِيَّة غَير شَفّافَة
            var bgc = parseRgb(s.backgroundColor), p = n.parentElement, hops = 0;
            var stack = [];
            if (bgc && bgc.a > 0.001) stack.push(bgc);
            while ((!bgc || bgc.a < 0.999) && p && hops < 12) {
                var pb = parseRgb(getComputedStyle(p).backgroundColor);
                if (pb && pb.a > 0.001) { stack.push(pb); if (pb.a >= 0.999) { bgc = pb; break; } }
                p = p.parentElement; hops++;
            }
            var bg = { r: 255, g: 255, b: 255, a: 1 };
            for (var k = stack.length - 1; k >= 0; k--) bg = over(stack[k], bg);
            var fg = parseRgb(s.color); if (!fg) continue;
            fg = over(fg, bg);
            ck('G-contrast');
            var rr = ratio(fg, bg);
            // WCAG: النَصّ الكَبير (≥24px أَو ≥18.66px عَريض) عَتَبَتُه 3:1
            var fsz = px(s.fontSize) || 16, fw = parseInt(s.fontWeight) || 400;
            var large = fsz >= 24 || (fsz >= 18.66 && fw >= 700);
            var need = large ? (rule.min_ratio_large == null ? 3 : rule.min_ratio_large) : rule.min_ratio;
            if (rr < need)
                add('G-contrast', '<' + n.tagName.toLowerCase() + '> «' + own.slice(0, 26) + '» نِسبَة ' + rr.toFixed(2) + ' < ' + need +
                    ' (نَصّ rgb(' + [fg.r, fg.g, fg.b].map(Math.round).join(',') + ') على rgb(' + [bg.r, bg.g, bg.b].map(Math.round).join(',') + ')' +
                    (large ? '، نَصّ كَبير' : '') + ')', rule.selector), count++;
        }
    });

    // ─── H. سَلامَة الصُندوق ───────────────────────────────────────
    (C.box_model_rules || []).forEach(function (rule) {
        var maxV = rule.max_violations == null ? 10 : rule.max_violations, count = 0;
        var els = q(rule.selector);
        for (var i = 0; i < els.length && count < maxV; i++) {
            var n = els[i]; if (!shown(n)) continue;
            var s = getComputedStyle(n); ck('H-box');
            if (rule.rule === 'box-sizing-border-box' && s.boxSizing !== 'border-box') {
                // content-box لا يَضُرّ إلّا إذا كانَ لِلعُنصُر قَيد عَرض مُصَرَّح
                // مَع حَشو: عِندَها يَتَجاوَز الصُندوق القَيد. أَمّا عُنصُر
                // مَشدود داخِل grid/flex بِلا قَيد فَلا أَثَر لَه — والإِبلاغ
                // عَنه صُراخٌ كاذِب يُفقِد الأَداة مِصداقِيَّتَها.
                var padded = Math.max(px(s.paddingTop) || 0, px(s.paddingRight) || 0,
                                      px(s.paddingBottom) || 0, px(s.paddingLeft) || 0) > 0;
                var constrained = s.maxWidth !== 'none' || n.style.width || n.style.maxWidth;
                if (padded && constrained) {
                    add('H-box', nm(n) + ': box-sizing=' + s.boxSizing +
                        ' مَع max-width=' + s.maxWidth + ' وحَشو — الحَشو يَتَجاوَز القَيد', rule.selector);
                    count++;
                }
            } else if (rule.rule === 'symmetric-padding') {
                var min = rule.min_padding_px == null ? 4 : rule.min_padding_px;
                var p = [px(s.paddingTop) || 0, px(s.paddingRight) || 0, px(s.paddingBottom) || 0, px(s.paddingLeft) || 0];
                if (Math.min.apply(null, p) < min) {
                    add('H-box', nm(n) + ': حَشو ' + p.join('/') + ' — جِهَة دونَ ' + min + 'px', rule.selector); count++;
                }
            }
        }
    });

    // ─── I. فَيَضان النَصّ ─────────────────────────────────────────
    (C.text_overflow_rules || []).forEach(function (rule) {
        var maxV = rule.max_violations == null ? 10 : rule.max_violations, count = 0;
        var els = q(rule.selector);
        for (var i = 0; i < els.length && count < maxV; i++) {
            var n = els[i]; if (!shown(n)) continue;
            var s = getComputedStyle(n);
            if (s.overflowX === 'auto' || s.overflowX === 'scroll') continue; // تَمرير مَقصود
            ck('I-overflow');
            if (n.scrollWidth > n.clientWidth + 1) {
                add('I-overflow', nm(n) + ': scrollWidth=' + n.scrollWidth + ' > clientWidth=' + n.clientWidth + ' — النَصّ مَقصوص أُفُقِيّاً', rule.selector);
                count++;
            }
        }
    });

    // ─── K. الابن لا يَتَجاوَز حاوِيَتَه عَرضاً ────────────────────
    //  أُضيفَت هي الأُخرى بِسَبَب بُرهان الحَقن: العَطَب المَحقون
    //  (بِطاقَة عَرضُها 3000px) فاتَ C لِأَنّ الزَوج غَير مَذكور،
    //  وفاتَ I لِأَنّ scrollWidth الابن = clientWidth‌ه، وفاتَ J
    //  لِأَنّ سَلَفاً قاصّاً (overflow:hidden) يَمنَع تَمرير الصَفحَة
    //  فَيُخفي الفَيَضان عَن مُستَوى المُستَند. والقِياس الوَحيد الَّذي
    //  يَراه: مُقارَنَة الابن بِأَبيه مُباشَرَةً — والقَصّ لا يُخفيها.
    (C.container_fit_rules || []).forEach(function (rule) {
        var maxV = rule.max_violations == null ? 10 : rule.max_violations, count = 0;
        var tol = rule.tolerance_px == null ? 1 : rule.tolerance_px;
        var els = q(rule.selector);
        for (var i = 0; i < els.length && count < maxV; i++) {
            var n = els[i]; if (!shown(n)) continue;
            var s = getComputedStyle(n);
            // الخارِج عَن السِياق العادِيّ يُقاس بِقَواعِد أُخرى
            if (s.position === 'absolute' || s.position === 'fixed') continue;
            var p = n.parentElement; if (!p) continue;
            var pw = p.clientWidth; if (pw <= 0) continue;
            ck('K-fit');
            var w = n.getBoundingClientRect().width;
            if (w > pw + tol) {
                var nm = function (e) {
                    return (e.className && typeof e.className === 'string')
                        ? '.' + e.className.trim().split(/\s+/).slice(0, 2).join('.')
                        : '<' + e.tagName.toLowerCase() + '>';
                };
                add('K-fit', nm(n) + ': عَرض ' + w.toFixed(0) + 'px > عَرض الحاوِيَة ' +
                    nm(p) + ' (' + pw + 'px) — الابن أَعرَض مِن أَبيه', rule.selector);
                count++;
            }
        }
    });

    // ─── J. فَيَضان النافِذَة أُفُقِيّاً ──────────────────────────
    //  أَعَمّ مِن C: قاعِدَة الاحتِواء تَفحَص أَزواج أَب/ابن مَذكورَة
    //  بِأَسمائِها، فَابنٌ يَفيض عَن أَبٍ لَم يُذكَر يَمُرّ دونَ أَن
    //  يُرى — وهذا ما كَشَفَه بُرهان الحَقن. أَمّا هُنا فَالسُؤال
    //  واحِد لا يَحتاج عَقداً: هَل تُمَرَّر الصَفحَة أُفُقِيّاً؟
    //  وهو على الهاتِف عَطَبٌ دائِماً، ولا يَحتاج تَعداد مُحَدِّدات.
    (C.viewport_rules || []).forEach(function (rule) {
        if (rule.rule !== 'no-horizontal-scroll') return;
        ck('J-viewport');
        var de = document.documentElement;
        var vw = de.clientWidth;
        var sw = Math.max(de.scrollWidth, document.body ? document.body.scrollWidth : 0);
        var tol = rule.tolerance_px == null ? 1 : rule.tolerance_px;
        if (sw <= vw + tol) return;
        // البَحث عَن المُتَسَبِّبين — العُنصُر الأَبعَد حافَّةً
        var off = [];
        var all = document.querySelectorAll('*');
        for (var i = 0; i < all.length; i++) {
            var n = all[i]; if (!shown(n)) continue;
            var r = n.getBoundingClientRect();
            if (r.width > 0 && (r.right > vw + tol || r.left < -tol)) off.push({ n: n, right: r.right, left: r.left });
        }
        off.sort(function (a, b) { return b.right - a.right; });
        var names = off.slice(0, 5).map(function (o) {
            var cls = (o.n.className && typeof o.n.className === 'string')
                ? '.' + o.n.className.trim().split(/\s+/).slice(0, 3).join('.') : '';
            return '<' + o.n.tagName.toLowerCase() + cls + '> يَمتَدّ إلى ' + o.right.toFixed(0);
        });
        add('J-viewport', 'الصَفحَة تُمَرَّر أُفُقِيّاً: scrollWidth=' + sw + ' > عَرض النافِذَة=' + vw +
            (names.length ? ' — المُتَسَبِّبون: ' + names.join('  |  ') : ''), null);
    });

    var fam = [];
    for (var k in CK) fam.push(k + '=' + CK[k]);
    fam.sort();
    return { violations: V, stats: { elements: document.querySelectorAll('*').length, checks: checks, byFamily: fam.join('  ') } };
    """;
}
