# PITFALLS — مزالق متكرّرة وحلولها

هذه الوثيقة تجمع المزالق التي رأيناها فعلاً في كود إيجار، مع السبب
الجذريّ والحلّ المطبَّق. الهدف: لا نُعيد الخطأ نفسه في مهمّة لاحقة.

كلّ مزلَق يحوي:
- **المظهر**: ما رآه المستخدم/المطوِّر (سطر خطأ، سلوك غير متوقَّع).
- **الجذر**: لماذا حدث.
- **الحلّ**: الكود الصحيح.
- **الكشف**: كيف نلتقطه باكراً مرّة أخرى.

---

## P1 — احقن التجريد، لا المزوِّد

**المظهر.** بعد إضافة Redis كـ provider لـ realtime backplane، أكواد كانت
تستخدم `InMemoryConnectionTracker` مباشرةً تكسرت بأخطاء تتبّع الاتّصالات
موزَّعة على instances، وبدلاً من معرفة المشكلة في التركيب، حصلنا على
رسائل غامضة من SignalR.

**الجذر.** الكود في عدّة مواضع كان يكتب `services.AddSingleton<InMemoryConnectionTracker>()`
ويستهلكه عبر type ملموس، بدل تسجيل/استهلاك `IConnectionTracker`. لمّا
تبدّل المزوِّد، نصف المستهلكين بقي يحقن النسخة القديمة.

**الحلّ.**

```csharp
// ❌
services.AddSingleton<InMemoryConnectionTracker>();
public class Hub(InMemoryConnectionTracker tracker) { ... }

// ✅
services.AddSingleton<IConnectionTracker, InMemoryConnectionTracker>();
// (أو RedisConnectionTracker بدون أيّ تعديل في المستهلكين)
public class Hub(IConnectionTracker tracker) { ... }
```

**القاعدة العامّة:** كلّ ما له interface في المكتبة (`IDeviceTokenStore`,
`INotificationChannel`, `IRealtimeTransport`, `IChatStore`, …) يُحقَن بالـ
interface فقط. الـ concrete type يُذكَر في سطر التسجيل وحده.

**الكشف.** عند إضافة provider جديد، اقرأ كلّ `using` و كلّ ctor parameter
للـ class القديم بـ grep — لو ظهر اسمه خارج Program.cs/Extensions.cs
فهناك مكان نسي التجريد.

---

## P2 — Singleton يستهلك Scoped: استعمل IServiceScopeFactory

**المظهر.** تسجيل `FirebaseNotificationChannel` (Singleton من الـ kit)
يطلب `IDeviceTokenStore`. إذا جعلنا `EjarDeviceTokenStore` Scoped (لأنّه
يحتاج `EjarDbContext` Scoped)، ASP.NET DI يرفض البناء بـ "Cannot consume
scoped service from singleton".

**الجذر.** Lifetime mismatch. Singleton يعيش طوال عمر التطبيق، Scoped يعيش
طول عمر الطلب. حقن Scoped في Singleton يعني الـ Scoped لن يُتخلَّص منه
أبداً، ويصبح في الواقع Singleton ضمنيّاً.

**الحلّ.** اجعل الـ store نفسه Singleton، واحقن `IServiceScopeFactory` —
أنشئ scope جديد في كلّ استدعاء يستهلك DbContext.

```csharp
public sealed class EjarDeviceTokenStore : IDeviceTokenStore
{
    private readonly IServiceScopeFactory _scopes;
    public EjarDeviceTokenStore(IServiceScopeFactory scopes) => _scopes = scopes;

    private async Task WithDbAsync(Func<EjarDbContext, Task> work)
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EjarDbContext>();
        await work(db);
    }
}
```

التسجيل: `services.AddSingleton<IDeviceTokenStore, EjarDeviceTokenStore>();`

**الكشف.** أيّ class عندك ينفّذ interface من kit Singleton ويحتاج DB —
يجب أن يكون Singleton عبر هذا النمط، لا Scoped.

---

## P3 — الـ Service Worker يكسر CORS عند cross-origin

**المظهر.** كلّ طلبات SignalR negotiate إلى ejarapi.runasp.net تفشل من
ejarpwa.runasp.net بـ:

```
No 'Access-Control-Allow-Origin' header is present
service-worker.js:38   Uncaught (in promise) TypeError: Failed to fetch
```

رغم أنّ الباك مكوَّن بـ CORS صحيح (CORS headers تظهر فعلاً في الـ
preflight لو قاسناه مباشرةً).

**الجذر.** الـ SW كان يلتقط كلّ fetch (بما فيها cross-origin)، يعيد إصدارها
عبر `event.respondWith(fetch(event.request))`. بعض المتصفّحات (خصوصاً
Edge على Android) تُفقِد الـ CORS metadata عند المرور خلال SW، فيفشل
preflight رغم أنّ السيرفر يعطي الـ headers الصحيحة.

**الحلّ.** SW يخدم same-origin فقط — يتخطّى cross-origin بـ early return.

```javascript
self.addEventListener('fetch', event => {
  if (event.request.method !== 'GET') return;
  let url; try { url = new URL(event.request.url); } catch { return; }
  if (url.protocol !== 'http:' && url.protocol !== 'https:') return;

  // ⚠️ تجاوز كلّ cross-origin — لا تلمسها. الـ shell + الأصول الثابتة فقط.
  if (url.origin !== self.location.origin) return;

  // ... باقي الـ caching strategy
});
```

**الكشف.** أيّ SW يستهدف PWA + API على origins مختلفين يحتاج هذا.

---

## P4 — HttpClient بـ BaseAddress خاطئ يطلب من origin غلط

**المظهر.** الواجهة تستدعي `_http.Client.GetStringAsync("/firebase-config.json")`
فيذهب الطلب إلى `ejarapi.runasp.net/firebase-config.json` (404)، بينما
الملف موجود على `ejarpwa.runasp.net/firebase-config.json` (نفس origin
الواجهة).

**الجذر.** الـ HttpClient المسمّى "ejar" مُكوَّن بـ `BaseAddress = EjarApi:BaseUrl`.
كلّ طلب نسبيّ ينتمي للـ API. الملف في الواقع static asset على origin
الواجهة.

**الحلّ.** للـ static assets على origin الواجهة، استخدم `window.fetch`
عبر JS (نفس origin افتراضياً)، لا الـ HttpClient المحقون.

```csharp
// ❌
var json = await _http.Client.GetStringAsync("/firebase-config.json", ct);

// ✅ — JS bridge يستخدم window.fetch
var raw = await _js.InvokeAsync<JsonElement>("ejarFirebase.initFromUrl", "/firebase-config.json");
```

**الكشف.** اسأل: هل المسار يعود للـ API أم للـ origin الذي تجلس عليه
الصفحة؟ افحص BaseAddress في DI قبل أن تستعمل HttpClient لأيّ شيء جديد.

---

## P5 — ترتيب تحميل CDN scripts على الجوّال

**المظهر.** على الكمبيوتر الدردشة الحيّة تعمل، على الهاتف:

```
[Realtime] start failed: signalR is not defined
ReferenceError: signalR is not defined at Module.start (realtime.js:28)
```

**الجذر.** ترتيب `<script>` في index.html كان:
1. `blazor.webassembly.js`
2. ... سكربتات أخرى
3. `signalr.min.js` (آخر، من cloudflare CDN)

السكربتات بدون `async/defer` تُحلَّل synchronously لكن **التنزيل**
متوازٍ. على الكمبيوتر الـ CDN يصل بسرعة فيكون signalR جاهزاً قبل ما
Blazor runtime يستدعي `EjarRealtimeService.start`. على الهاتف بشبكة 4G
أبطأ، Blazor يبدأ ويستدعي realtime.start قبل ما ينتهي تنزيل signalR
من CDN خارجيّ.

**الحلّ.** ضع كلّ تبعيّة global أوّلاً:

```html
<!-- signalR قبل blazor -- يضمن أنّه معرَّف عند أوّل استدعاء -->
<script src=".../signalr.min.js" integrity="..." crossorigin></script>
<script src="_framework/blazor.webassembly.js"></script>
```

كحماية إضافيّة، أضف retry/wait داخل JS:

```javascript
function waitForSignalR(timeoutMs) {
    return new Promise((resolve, reject) => {
        const t0 = Date.now();
        (function check() {
            if (typeof signalR !== 'undefined') return resolve();
            if (Date.now() - t0 >= timeoutMs) return reject(new Error('timeout'));
            setTimeout(check, 100);
        })();
    });
}

export async function start(...) {
    try { await waitForSignalR(10000); } catch { return; }
    // ... استعمل signalR بأمان
}
```

**الكشف.** أيّ سكربت من CDN خارجيّ يُستهلَك من Blazor JS interop يجب أن
يأتي قبل blazor.webassembly.js.

---

## P6 — Service Worker handlers يجب أن تُسجَّل synchronously

**المظهر.** حتى بعد تحميل `firebase-messaging-sw.js` بنجاح:

```
register.ts:80 Event handler of 'push' event must be added on the
initial evaluation of worker script.
```

**الجذر.** كنّا نقرأ الإعداد عبر `await fetch('/firebase-config.json')`
ثمّ نستدعي `firebase.initializeApp(cfg)` و`firebase.messaging()` داخل
`(async () => {...})()`. SDK يُسجّل push handlers ضمن `messaging()`. بعد
أن SW أنهى evaluation أوّليّة، Chrome يرفض إضافة push handlers.

**الحلّ.** سجِّل كلّ شيء synchronously على top-level. الإعداد إذا كان
public (مفاتيح web Firebase كلّها عامّة) ضعه inline:

```javascript
importScripts('.../firebase-app-compat.js');
importScripts('.../firebase-messaging-compat.js');

self.addEventListener('install', e => self.skipWaiting());
self.addEventListener('activate', e => e.waitUntil(self.clients.claim()));

// مزامن — قبل أيّ await
firebase.initializeApp({ apiKey: "...", appId: "...", ... });
const messaging = firebase.messaging();

messaging.onBackgroundMessage(payload => { ... });

self.addEventListener('notificationclick', event => { ... });
```

**الكشف.** في SW، كلّ `addEventListener('push'/'notificationclick'/...)`
يجب أن يكون في top-level. لا async wrapper حول إعداد SDK.

---

## P7 — `new Notification()` ممنوع على Android Chrome/Edge

**المظهر.** على الكمبيوتر الإشعار يظهر، على الهاتف:

```
TypeError: Failed to construct 'Notification': Illegal constructor.
Use ServiceWorkerRegistration.showNotification() instead.
```

**الجذر.** Chrome/Edge على Android منذ سنوات يحظران constructor `Notification`
المباشر من page context. القناة الوحيدة المسموحة:
`ServiceWorkerRegistration.showNotification()`.

**الحلّ.** المسار الموحَّد عبر SW:

```javascript
async function show(title, body, opts) {
    if (Notification.permission !== 'granted') return false;
    const reg = await navigator.serviceWorker.ready;
    await reg.showNotification(title, {
        body, icon: '/icon-192.png',
        tag: opts.tag, data: { url: opts.url }
    });
}
```

النقر يُعالَج في SW عبر `notificationclick` event:

```javascript
self.addEventListener('notificationclick', event => {
    event.notification.close();
    const url = event.notification.data?.url || '/';
    event.waitUntil(self.clients.openWindow(url));
});
```

**الكشف.** أيّ كود يستدعي `new Notification(...)` خارج SW — يحتاج إعادة
كتابة عبر SW registration.

---

## P8 — العَلَم المحلّيّ لا يعكس حالة الخدمة

**المظهر.** بعد logout/login على نفس tab، الدردشة الحيّة تتوقّف. تحديث
الصفحة يحلّ.

**الجذر.** `MainLayout._realtimeConnected` كان يصبح `true` عند أوّل
connect، ولا يُعاد ضبطه أبداً. عند logout الـ service يفصل (`_connected
= false` داخله)، لكن العَلَم في layout يبقى true. عند login جديد على نفس
tab، `OnChange` يفحص `!_realtimeConnected` → false → يتجاوز ConnectAsync.

**الحلّ.** اعتمد على حالة الخدمة الفعليّة، لا mirror محلّي.

```csharp
// في الخدمة
public bool IsConnected => _connected;

// في layout
if (Auth.IsAuthenticated && !Realtime.IsConnected)
    await ConnectRealtimeAsync();
```

**الكشف.** أيّ بول محلّي اسمه `_xConnected/_xLoaded/_xInitialized` يلمّع
حالة موجودة في خدمة منفصلة — اسأل: إذا تغيّرت تلك الحالة من خارجي
(disconnect، error، logout)، هل العَلَم يعرف؟ لو لا، احذفه واعتمد على
الخدمة.

---

## P9 — GitHub Push Protection يحجب الأسرار

**المظهر.** push يُرفَض:

```
remote: GH013: Repository rule violations found
remote: - GITHUB PUSH PROTECTION
remote:   Push cannot contain secrets
remote: —— Google Cloud Service Account Credentials ——
```

**الجذر.** حاولنا التزام `firebase-service-account.json` (يحوي private
key) في git.

**الحلّ.** الأسرار **لا** تذهب في git. النمط الصحيح:

1. الملف الفعليّ في `.gitignore` (يبقى محلّياً للتطوير).
2. ملف `.example.json` ملتزَم كنموذج فقط.
3. README في نفس المجلَّد يشرح:
   - من أين يحصل المستخدم على الملف الحقيقيّ.
   - أين يضعه (نفس مجلَّد ContentRoot).
   - أو بديل عبر env var (`Notifications__Firebase__CredentialsJson`).
4. `<None Update Condition="Exists(...)">` في csproj → ينسخ الملف إلى
   bin/publish لو موجوداً، فلا يفشل البناء على clone جديد.

**الكشف.** قبل أيّ commit يحوي JSON أو PEM أو يبدو كاعتماد، اسأل: هل
هذا حسّاس؟ إن نعم → gitignore + example + README.

---

## P10 — البولينج كخدمة يخالف OAM

**المظهر.** عُدنا للبولينج HTTP كحلّ سريع لمشكلة تسليم الرسائل، أنشأنا
`ChatPollingService` — وانتقد المستخدم بحقّ: "قاعدتنا تقول لا خدمات،
فقط قيود محاسبية".

**الجذر.** كلّ "خدمة" تخفي operation حقيقيّ. الـ OAM يقول: كلّ تغيير
حالة عمليّة بـ sender و receiver و tags. الـ realtime delivery ليس
استثناءً — هو interceptor على عمليّة chat.message أو ما يكافئها.

**الحلّ.** القنوات الحيّة تتمّ داخل تنفيذ العمليّة (`AppendMessageAsync`):
broadcast realtime + DB notification + (FCM إن مكوَّن). لا polling timer
كخدمة منفصلة.

**الكشف.** قبل ما تكتب class اسمه `*Service` أو `*Worker` أو `*Background*`،
اسأل: هل هذه عمليّة؟ هل لها sender/receiver واضحان؟ لو نعم → interceptor
أو child operation. لو لا → مرجع موصول بحالة، استخدم Razor lifecycle.

---

## P11 — `Versions:Latest` لا يُحدَّث تلقائياً

**المظهر.** الواجهة على إصدار 15، الباك يقول Latest = 13، فالواجهة
ترى نفسها قديمة وتعرض banner لتحديث "إلى" إصدار أقدم منها.

**الجذر.** `Versions:Latest` في `appsettings.json` يُسجَّل عند الإقلاع
كحالة Latest في DB. كلّ نشر يجب أن يرفع هذا الرقم بنفس الإصدار الجديد.
نحن كنّا نرفع `version.json` و `appsettings.json` للواجهة، لكن ننسى
الـ `Versions:Latest` في الباك.

**الحلّ.** عند أيّ bump للواجهة، حدّث **أربعة** أماكن:
- `Apps/Ejar/Customer/Frontend/Ejar.WebAssembly/wwwroot/version.json`
- `Apps/Ejar/Customer/Frontend/Ejar.WebAssembly/wwwroot/appsettings.json` → `App:Version`
- `Apps/Ejar/Customer/Frontend/Ejar.WebAssembly/wwwroot/service-worker.js` → `VERSION`
- `Apps/Ejar/Customer/Backend/Ejar.Api/appsettings.json` → `Versions:Latest:wasm` و `:web`

كقائمة مرجعيّة قبل أيّ commit نشر، تأكّد أنّ الأربعة على نفس الرقم.

**الكشف.** سكربت bash بسيط:
```bash
grep -E '"version"|"VERSION"|"Version"|"wasm":|"web":' \
  Apps/Ejar/Customer/Frontend/Ejar.WebAssembly/wwwroot/{version.json,appsettings.json,service-worker.js} \
  Apps/Ejar/Customer/Backend/Ejar.Api/appsettings.json
```
كلّ السطور يجب أن تُظهر نفس الرقم.

---

## P12 — المسارات النسبيّة على IIS تنكسر

**المظهر.** على dev المسار `Secrets/firebase-service-account.json` يعمل،
على runasp.net (IIS):

```
DirectoryNotFoundException: Could not find a part of the path
'C:\windows\system32\Secrets\firebase-service-account.json'
```

**الجذر.** على IIS الـ `Environment.CurrentDirectory` ليس مجلَّد التطبيق
(غالباً `C:\windows\system32`). أيّ مكتبة تستعمل `File.OpenRead(relativePath)`
ستفشل. `GoogleCredential.FromFile()` على وجه التحديد تستخدم CWD كأساس.

**الحلّ.** حلّ المسار على `ContentRootPath` صراحةً قبل الاستهلاك:

```csharp
var credPath = fbCfg["CredentialsFilePath"];
if (!string.IsNullOrWhiteSpace(credPath) && !Path.IsPathRooted(credPath))
{
    var abs = Path.Combine(builder.Environment.ContentRootPath, credPath);
    fbCfg["CredentialsFilePath"] = abs; // كتابتها للـ Configuration
}
```

**الكشف.** أيّ مكان يقرأ `Path` نسبيّ من `Configuration` ويُمرَّر لـ
مكتبة third-party — احسبه absolute أوّلاً.

---

## P13 — أضِف المشروع الجديد إلى .sln فور إنشائه

**المظهر.** بعد إنشاء كيت جديد (Reports مثلاً) و إضافة `<ProjectReference>`
من `Ejar.Api.csproj` إليه، Visual Studio على Windows يفشل بـ:

```
NU1105: Unable to find project information for
'C:\...\libs\kits\Reports\ACommerce.Kits.Reports\ACommerce.Kits.Reports.csproj'.
The project may be unloaded or not part of the current solution.
```

`dotnet build` على CLI يعمل (يبني المشاريع المرجعيّة عبر `<ProjectReference>`
بصرف النظر عن الـ .sln)، فيظنّ المطوِّر الذي يستعمل CLI أنّ كلّ شيء سليم.
لكنّ Visual Studio يعتمد على .sln للـ restore/IntelliSense ويرفض المشروع
الذي يُحال إليه ولم يُسجَّل.

**الجذر.** `dotnet new classlib` لا يُضيف المشروع للـ .sln تلقائياً (بخلاف
ما يفعله `Add → New Project` من Visual Studio). كذلك إنشاء csproj يدوياً
عبر `Write` لا يحدّث الـ .sln. الكود يبني، لكنّ بعض المسارات تنكسر.

**الحلّ.** بعد كلّ مشروع جديد، **مباشرةً**:

```bash
# مرَّ كلّ مسارات الـ csproj الجديدة دفعة واحدة
dotnet sln ACommerce.Platform.sln add \
  libs/kits/Reports/ACommerce.Kits.Reports.Operations/ACommerce.Kits.Reports.Operations.csproj \
  libs/kits/Reports/ACommerce.Kits.Reports.Backend/ACommerce.Kits.Reports.Backend.csproj \
  libs/kits/Reports/ACommerce.Kits.Reports/ACommerce.Kits.Reports.csproj \
  --solution-folder "libs/kits/Reports"
```

`--solution-folder` ينظّمها تحت مجلَّد افتراضيّ (Solution Folder) في
Visual Studio بدل أن تتعلّق في الجذر.

**الكشف**:
```bash
# قبل الـ commit، تحقّق من تطابق الـ csproj مع الـ sln:
diff <(find libs Apps -name "*.csproj" -not -path "*/bin/*" -not -path "*/obj/*" | sort) \
     <(grep -oP '(?<=, ")[^"]+\.csproj' ACommerce.Platform.sln | sort) | head
```
لو ظهر فرق → هناك مشروع غير مسجَّل. أصلِح قبل الالتزام.

**خصِّص خانة في checklist**: عند إنشاء أيّ kit جديد، الخطوات بالترتيب
هي:
1. `mkdir -p libs/kits/<NewKit>/...` لكلّ مشروع.
2. اكتب csproj + الكود.
3. `dotnet sln add ...` ← **خطوة لا تُنسى**.
4. أضف `<ProjectReference>` من المستهلكين (Ejar.Api مثلاً).
5. `dotnet build` للحلّ كاملاً للتأكّد.

---

## P14 — الطبقات الست لا تحمي من زرّ خام بكلاس "ضعيف"

**المظهر.** زرّ يظهر بدون أيّ تنسيقات في الإنتاج (border افتراضيّ، بدون
padding، بدون background)، رغم أنّ الـ٦ طبقات تعبر بدون انتهاكات.

**الجذر.** نمط الـ "raw button + ad-hoc class" يسلكه عبر الفجوات:

- **Layer 1** (`verify-page-structure.sh` — no-raw-button): يسمح بـ
  `<button class="acs-*">` لأنّ المشروع يستخدم prefix `.acs-` كـ widget
  دلاليّ شرعيّ (مثلاً `.acs-fav-btn` المعرَّف بكامله في app.css).
- **Layer 2** (`verify-css.sh`): الكلاس **معرَّف** في CSS فيمرّ. لا يفحص
  هل التعريف يحوي خصائص بصريّة كافية.
- **Layer 5** (`verify-widget-contracts.sh`): يفحص فقط الكلاسات المسجَّلة
  في `widget-contracts.json`. كلاس جديد بـ ad-hoc properties غير مسجَّل
  → لا يُفحَص.
- **Layer 6** (Playwright): لو الكلاس معرَّف بـ `background: transparent`،
  Playwright لا يميِّزه عن "كلاس بلا styling" — كلاهما computed صحيح.
- يضاف لذلك: لو CSS الجديد لم يصل للـ bundle المنشور (cache CDN، نشر
  جزئيّ) فالطبقات لا ترى ما يحدث في الإنتاج.

**الحلّ.** القاعدة: **لا** كلاس CSS ad-hoc لـ button. استخدم `<AcButton>`
دائماً مع `Variant` المناسب (`primary` / `ghost` / `outline-primary` /
`secondary` / `danger` / إلخ). هذه widgets معتمَدة في
`scripts/widget-contracts.json` فيُفرَض contract بصريّ كامل.

```razor
<!-- ❌ -->
<button type="button" class="acs-report-link" @onclick="OpenReport">إبلاغ</button>

<!-- ✅ -->
<AcButton Variant="ghost" Size="sm" Icon="flag"
          Text="إبلاغ" OnClick="@OpenReport" />
```

استثناءات: عناصر تفاعليّة فيها interactivity داخليّة معقّدة (مثل
`acs-fav-btn` التي تُشغّل قلباً متحرّكاً). تلك تُسجَّل صراحةً في
`widget-contracts.json` بـ MIN-PROPERTIES وتمرّ Layer 5.

**الكشف.** عند مراجعة PR قبل النشر، اسأل: هل أيّ ملف Razor جديد يستخدم
`<button>` خام؟ لو نعم، تحقّق من وجود الـ class في contracts، وإلاّ
استبدل بـ `<AcButton>`.

> **توصية**: تطوير لاحق على Layer 1: راقب `<button class="acs-*">` وتحقّق
> أنّ الكلاس المُستخدَم له entry في `widget-contracts.json`. لو لا → ✗.
> هذا يقلّل الاعتماد على المراجعة البشريّة. *Out of scope في هذه الجلسة.*

---

## ملاحظة — قواعد العمل (T1-T6)

`CLAUDE.md` يحوي قواعد T1-T6 المتعلّقة بانضباط استخدام الأدوات (Read قبل
Edit، لا تشغّل خوادم في Bash، إلخ). هذه الوثيقة (P1-P12) تكمّلها بقواعد
معماريّة. **اقرأ كلتيهما قبل أيّ مهمّة جديدة على المنصّة.**
