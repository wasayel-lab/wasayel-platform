#!/usr/bin/env dotnet
// ═══════════════════════════════════════════════════════════════════════
//  تَفريغ الأَنماط المَحسوبَة — الطَرَف المُقارَن لِمَوجَة الرُموز
// ───────────────────────────────────────────────────────────────────────
//  **لِماذا هذا المِلَفّ أَصلاً.** `compare-appearance.sh` تُقارِن HTML
//  بايتاً بِبايت، و`ThemeZeroEquivalenceTests` تُقارِن قيمَة الرَمز
//  المَبثوث بِالحَرفِيَّة في لَقطَة CSS. وبَينَهُما ثُقب: حينَ تُستَبدَل
//  حَرفِيَّة داخِل قاعِدَة CSS بِـ`var(--ac-…)` **لا يَتَغَيَّر بايت
//  واحِد في الـHTML**، فَتَبقى بَوّابَة المَظهَر خَضراء مَهما انحَرَفَت
//  القيمَة. أَي أَنّ «‏٨ صَفَحات مُطابِقَة» عَن استِبدال في CSS دَعوى
//  **لا تَفحَص المَوضِع الَّذي تَغَيَّرَ**.
//
//  فَالمَقيس هُنا هو ما يَراه المُتَصَفِّح فِعلاً بَعدَ الكاسكيد كامِلاً:
//  ‏`getComputedStyle` لِكُلّ عُنصُر في كُلّ صَفحَة مَرجِعِيَّة، مَعَ
//  المُستَطيل المُحيط — فَالقيمَة والتَخطيط مَعاً. الفَرق الوَحيد
//  المَقبول هُوَ صِفر بايت.
//
//  ولِأَنّ الأَداة نَفسُها يَجِب أَن تُقاس (القاعِدَة ١٠): تَطبَع عَدَد
//  الصَفَحات والعَناصِر والخَصائِص المُفَرَّغَة، و`compare-computed.sh`
//  تُحمِرّ إن كانَ أَيٌّ مِنها صِفراً. وقَد كُذِّبَت تَحتَ الطَلَب مَرَّةً
//  بِـ`--inject-css` قَبلَ الاعتِماد.
//
//  المُحَرِّك هو نَفسُه مُحَرِّك `verify-runtime.cs`: Chrome بِلا رَأس
//  عَبر CDP، بِلا Node وبِلا Playwright.
//
//  الاستِعمال:
//     dotnet run scripts/dump-computed.cs -- <out-dir> [--viewport WxH]
//                                            [--inject-css "…"] [url…]
//
//  رَمز الخُروج: ٠ نَجاح، ٢ عَطَب تَشغيليّ.
// ═══════════════════════════════════════════════════════════════════════

using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

// ─── وُسَطاء سَطر الأَمر ────────────────────────────────────────────
string? outDir = null;
var urls = new List<string>();
int vpW = 1280, vpH = 900;
string? injectCss = null;

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--viewport":
            var parts = args[++i].Split('x');
            vpW = int.Parse(parts[0]); vpH = int.Parse(parts[1]);
            break;
        case "--inject-css": injectCss = args[++i]; break;
        default:
            if (outDir is null) outDir = args[i]; else urls.Add(args[i]);
            break;
    }
}

if (outDir is null)
{
    Console.Error.WriteLine("الاستِعمال: dotnet run scripts/dump-computed.cs -- <out-dir> [url…]");
    return 2;
}

// الصَفَحات المَرجِعِيَّة — نَفس قائِمَة `capture-appearance.sh` حَرفاً،
// وبِنَفس التَرتيب، فَلا يَنشَقّ الطَرَفان.
if (urls.Count == 0)
{
    urls.AddRange(new[]
    {
        "ashare-portal|http://localhost:5050/ashare",
        "ashare-role-customer|http://localhost:5050/ashare/r/customer",
        "ashare-role-host|http://localhost:5050/ashare/r/host",
        "ashare-role-vendor|http://localhost:5050/ashare/r/vendor",
        "ashare-explore|http://localhost:5050/ashare/explore",
        "adwar-demo-portal|http://localhost:5050/adwar-demo",
        "ashare-explore-filters|http://localhost:5050/ashare/explore?filters=open",
        "ashare-explore-empty|http://localhost:5050/ashare/explore?category=__none__",
    });
}

Directory.CreateDirectory(outDir);

// ─── العُثور على Chrome ─────────────────────────────────────────────
string? chrome = new[]
{
    @"C:\Program Files\Google\Chrome\Application\chrome.exe",
    @"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe",
    @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
    @"C:\Program Files\Microsoft\Edge\Application\msedge.exe",
}.FirstOrDefault(File.Exists);

if (chrome is null)
{
    Console.Error.WriteLine("✗ لَم يُعثَر على Chrome ولا Edge — الأَداة الناقِصَة هي المُتَصَفِّح، لا القُدرَة.");
    return 2;
}

int port = FreePort();
string profile = Path.Combine(Path.GetTempPath(), "wsl-dump-" + Guid.NewGuid().ToString("N")[..8]);
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

int exit = 2;
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

    Console.WriteLine();
    Console.WriteLine("══════════════════════════════════════════════════════════");
    Console.WriteLine($"  تَفريغ الأَنماط المَحسوبَة  ·  نافِذَة {vpW}×{vpH}  ·  {urls.Count} صَفحَة");
    if (injectCss is not null)
        Console.WriteLine("  ⚠ عَطَبٌ مَحقون — جَولَة تَكذيب لِلأَداة، لا قِياس نَظيف");
    Console.WriteLine("══════════════════════════════════════════════════════════");

    long totalElements = 0, totalProps = 0;
    int pages = 0;

    foreach (var entry in urls)
    {
        int bar = entry.IndexOf('|');
        string name = bar < 0 ? "page" + pages : entry[..bar];
        string url = bar < 0 ? entry : entry[(bar + 1)..];

        await cdp.Send("Page.navigate", $$"""{"url":{{Cdp.JStr(url)}}}""");
        await cdp.WaitForEvent("Page.loadEventFired", TimeSpan.FromSeconds(30));
        await Task.Delay(900); // اِستِقرار Blazor بَعدَ الحَدَث

        // ── التَطبيع الوَحيد المَسموح: تَجميد الحَرَكات ────────────────
        // مَقيس لا مُقَدَّر: تَفريغان مُتَتالِيان لِنَفس الخادِم بِلا هذا
        // السَطر اختَلَفا في مَوضِعَين، كِلاهُما حَرَكَة في الطَيَران —
        // ‏`.acs-spinner` يَدور (المُستَطيل المُحيط 51.03 مَرَّةً و46.02
        // مَرَّةً)، و`ac-pwa-pop` في نِصف انبِثاقِها (‏opacity = 0.988).
        // القيمَة الساكِنَة النِهائيَّة هي المَقصودَة، وزَمَن الحَرَكَة
        // خارِج ما تَقيسُه هذِه الأَداة أَصلاً (‏VERIFICATION-LAYERS.md
        // «ما لا تُغَطّيه»). فَالتَجميد يُطَبَّق على الطَرَفَين مَعاً.
        const string freeze = "*,*::before,*::after{animation:none !important;" +
                              "transition:none !important;caret-color:transparent !important}";
        string frz = "(function(){var s=document.createElement('style');s.id='__freeze__';" +
                     "s.textContent=" + Cdp.JStr(freeze) + ";document.head.appendChild(s);return true;})()";
        await cdp.Send("Runtime.evaluate", $$"""{"expression":{{Cdp.JStr(frz)}},"returnByValue":true}""");
        await Task.Delay(250);

        if (injectCss is not null)
        {
            string inj = "(function(){var s=document.createElement('style');s.id='__fault__';" +
                         "s.textContent=" + Cdp.JStr(injectCss) + ";document.head.appendChild(s);return true;})()";
            await cdp.Send("Runtime.evaluate", $$"""{"expression":{{Cdp.JStr(inj)}},"returnByValue":true}""");
            await Task.Delay(400);
        }

        var res = await cdp.Send("Runtime.evaluate",
            $$"""{"expression":{{Cdp.JStr(Scripts.Body)}},"returnByValue":true,"awaitPromise":true}""");

        var root = res.RootElement;
        if (root.TryGetProperty("exceptionDetails", out var ex))
        {
            Console.Error.WriteLine($"✗ {url} — عَطَب في السِكرِبت: {ex}");
            return 2;
        }

        var value = root.GetProperty("result").GetProperty("value");
        string text = value.GetProperty("text").GetString() ?? "";
        int els = value.GetProperty("elements").GetInt32();
        int props = value.GetProperty("props").GetInt32();

        // بِلا BOM وبِنِهايات أَسطُر LF ثابِتَة — فَالمُقارَنَة بايتِيَّة.
        File.WriteAllText(Path.Combine(outDir, name + ".computed.txt"),
                          text.Replace("\r\n", "\n"), new UTF8Encoding(false));

        totalElements += els; totalProps += (long)props;
        pages++;
        Console.WriteLine($"  ✓ {name,-24} {els,5} عُنصُر × {props} خاصِّيَّة");
    }

    Console.WriteLine("──────────────────────────────────────────────────────────");
    Console.WriteLine($"  الصَفَحات: {pages}   العَناصِر: {totalElements}   الخَصائِص المُفَرَّغَة: {totalProps}");
    if (pages == 0 || totalElements == 0 || totalProps == 0)
    {
        Console.Error.WriteLine("✗ فَحصٌ أَعمى: لَم يُفَرَّغ شَيء.");
        return 2;
    }
    exit = 0;
}
catch (Exception e)
{
    Console.Error.WriteLine($"✗ عَطَب تَشغيليّ: {e.Message}");
    exit = 2;
}
finally
{
    try { proc.Kill(true); } catch { }
    try { Directory.Delete(profile, true); } catch { }
}

return exit;

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
                if (t.GetProperty("type").GetString() == "page" &&
                    t.TryGetProperty("webSocketDebuggerUrl", out var w))
                    return w.GetString()!;
        }
        catch { }
        await Task.Delay(200);
    }
    throw new Exception("تَعَذَّرَ الاتِّصال بِـCDP — لَم يَبدَأ Chrome في الوَقت المَسموح.");
}

// ─── جِسر CDP — مَنقول حَرفاً عَن verify-runtime.cs ─────────────────
sealed class Cdp(ClientWebSocket ws)
{
    int _id = 0;

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

        var deadline = DateTime.UtcNow.AddSeconds(120);
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
    }

    async Task<JsonDocument> Receive()
    {
        var buf = new byte[256 * 1024];
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

// ─── السِكرِبت داخِل الصَفحَة ────────────────────────────────────────
//  يُفَرِّغ ثَلاثَة أَشياء بِتَرتيب ثابِت:
//    ١. قيمَة كُلّ رَمز `--wsl-*` و`--ac-*` كَما يَحسِبُها المُتَصَفِّح
//       على :root — فَهذا هو الطَرَف الَّذي يَحكُم كُلّ استِبدال.
//    ٢. لِكُلّ عُنصُر في تَرتيب المُستَند: مَجموعَة خَصائِص مَحسوبَة
//       تُغَطّي اللَون والمَسافَة والحَدّ والخَطّ والظِلّ.
//    ٣. المُستَطيل المُحيط بِكُلّ عُنصُر — فَالتَخطيط جُزء مِن المَظهَر.
//
//  (‏في صَنف لِأَنّ الدَوالّ المَحَلِّيَّة لِلبَرنامَج العُلويّ لا تَجوز
//   بَعدَ أَوَّل تَصريح نَوع.)
static class Scripts
{
    public const string Body = @"(function(){
  var PROPS = ['color','background-color','border-top-color','border-right-color',
    'border-bottom-color','border-left-color','border-top-width','border-right-width',
    'border-bottom-width','border-left-width','border-top-style','border-top-left-radius',
    'border-top-right-radius','border-bottom-left-radius','border-bottom-right-radius',
    'padding-top','padding-right','padding-bottom','padding-left',
    'margin-top','margin-right','margin-bottom','margin-left',
    'font-size','font-weight','font-family','line-height','letter-spacing',
    'text-align','text-decoration-line','text-decoration-color','text-transform',
    'display','position','flex-direction','flex-wrap','align-items','justify-content',
    'gap','row-gap','column-gap','box-shadow','opacity','fill','stroke',
    'min-height','min-width','max-width','box-sizing','overflow-x','overflow-y',
    'inset-inline-start','inset-inline-end','top','bottom','z-index'];

  // التَطبيع الثاني والأَخير: يُنزَع لافِت تَحديث عامِل الخِدمَة.
  // وُجودُه لَيسَ مَظهَراً بَل دَورَة حَياة: يُنشِئُه JS عِندَ رَصد نُسخَة
  // مُنتَظِرَة، فَيَظهَر أَحياناً ويَغيب أَحياناً في نَفس البِناء — قيسَ:
  // تَفريغان مُتَتالِيان بِلا أَيّ تَغيير في المَصدَر اختَلَفا بِـ228
  // سَطراً في ثَلاث صَفَحات، كُلُّها هذا العُنصُر وحدَه. وحُكمٌ يَتَقَلَّب
  // بِلا سَبَبٍ في المَقيس لَيسَ حُكماً.
  var upd = document.getElementById('ac-update-banner');
  if (upd && upd.parentNode) upd.parentNode.removeChild(upd);

  var lines = [];
  var cs = getComputedStyle(document.documentElement);
  var names = [];
  for (var i = 0; i < cs.length; i++) {
    var n = cs[i];
    if (n.indexOf('--wsl-') === 0 || n.indexOf('--ac-') === 0 || n.indexOf('--acm-') === 0) names.push(n);
  }
  names.sort();
  for (var j = 0; j < names.length; j++)
    lines.push('TOKEN ' + names[j] + ' = ' + cs.getPropertyValue(names[j]).trim());

  // التَرشيح **قَبل** التَرقيم لا بَعدَه: لَو رُقِّمَت القائِمَة الخام
  // لَأَزاحَ وَسم <style> واحِد مَحقون كُلَّ فِهرِس بَعدَه، فَصارَ
  // الاختِلاف كُلُّه إزاحَةً — وحينَها لا تُثبِت جَولَة الحَقن أَنّ
  // الأَداة تَرى العَطَب، بَل أَنَّها تَرى وَسماً زائِداً.
  var raw = document.querySelectorAll('*');
  var all = [];
  for (var q = 0; q < raw.length; q++) {
    var t = raw[q].tagName.toLowerCase();
    if (t === 'script' || t === 'style' || t === 'link' || t === 'meta' || t === 'title') continue;
    all.push(raw[q]);
  }
  for (var k = 0; k < all.length; k++) {
    var el = all[k];
    var tag = el.tagName.toLowerCase();
    var s = getComputedStyle(el);
    var r = el.getBoundingClientRect();
    var id = k + ' ' + tag + '.' + (el.getAttribute('class') || '');
    var box = 'BOX ' + id + ' = ' + Math.round(r.x*100)/100 + ',' + Math.round(r.y*100)/100 +
              ',' + Math.round(r.width*100)/100 + ',' + Math.round(r.height*100)/100;
    lines.push(box);
    for (var p = 0; p < PROPS.length; p++)
      lines.push('CSS ' + id + ' | ' + PROPS[p] + ' = ' + s.getPropertyValue(PROPS[p]));
  }
  return { text: lines.join('\n') + '\n',
           elements: all.length,
           props: PROPS.length };
})()";
}
