/* ACommerce PWA Service Worker — مُشتَرَك بَين كُلّ التَّطبيقات الفَرعيَّة.
   الـ scope يَتَحَدَّد عِندَ التَّسجيل في App.razor — كُلّ /{slug}/r/{role}/
   يُسَجِّل SW بِـ scope مُنفَصِل، فَيَتَعامَل المُتَصَفِّح مَع كُلّ تَطبيق
   كَ PWA مُستَقِلّ. */

const VERSION = 'ac-pwa-v2';

// عِندَ ضَغط المُستَخدِم زِرّ "تَحديث جاهِز" في الواجِهَة، الـ JS يُرسِل
// SKIP_WAITING لِنَنشَط فَوراً بَدَلاً مِن انتِظار إغلاق كُلّ الـ tabs.
self.addEventListener('message', (event) => {
    if (event.data && event.data.type === 'SKIP_WAITING') self.skipWaiting();
});

self.addEventListener('install', (event) => {
    // اِفتَح cache بَسيط لِلصَفحَة الافتراضيَّة فَقَط. الباقي
    // يَستَخدِم network-first.
    event.waitUntil(
        caches.open(VERSION).then(cache => cache.addAll([
            '/offline.html'
        ]).catch(() => { /* اِتَجاهَل خَطَأ pre-cache */ }))
    );
    self.skipWaiting();
});

self.addEventListener('activate', (event) => {
    // اِحذِف caches القَديمَة
    event.waitUntil((async () => {
        const keys = await caches.keys();
        await Promise.all(keys.filter(k => k !== VERSION).map(k => caches.delete(k)));
        await self.clients.claim();
    })());
});

self.addEventListener('fetch', (event) => {
    // network-first مَع fallback إلى offline.html لِلتَّنَقُّل (HTML).
    // GET فَقَط — الـ POST/PUT/DELETE تَمُرّ مُباشَرَة.
    if (event.request.method !== 'GET') return;
    const acceptsHtml = (event.request.headers.get('Accept') || '').includes('text/html');
    if (!acceptsHtml) return;   // أَصول CSS/JS/img → دَع المُتَصَفِّح يَتَوَلَّى

    event.respondWith((async () => {
        try {
            return await fetch(event.request);
        } catch (_) {
            const cached = await caches.match('/offline.html');
            return cached || new Response('Offline', { status: 503 });
        }
    })());
});

// ─── Web Push ─────────────────────────────────────────────────────
// يَستَلِم رِسالَة push مِن السيرفر، يَفُكّ JSON، وَيَعرِض إشعاراً.
// payload: { title, body, url, tag, badge?, icon? }
self.addEventListener('push', (event) => {
    let data = {};
    try { data = event.data ? event.data.json() : {}; } catch (_) {}
    const title = data.title || 'إشعار جَديد';
    const opts = {
        body:  data.body || '',
        icon:  data.icon || '/favicon.ico',
        badge: data.badge,
        tag:   data.tag,           // إشعارات بِنَفس tag تَستَبدِل بَعضها
        data:  { url: data.url || '/' },
        lang:  'ar',
        dir:   'rtl',
    };
    event.waitUntil(self.registration.showNotification(title, opts));
});

self.addEventListener('notificationclick', (event) => {
    event.notification.close();
    const url = (event.notification.data && event.notification.data.url) || '/';
    event.waitUntil((async () => {
        const all = await self.clients.matchAll({ type: 'window', includeUncontrolled: true });
        for (const c of all) {
            if (c.url.includes(url)) return c.focus();
        }
        for (const c of all) {
            const asPath = pathFormOnStoreHost(c.url, url);
            if (asPath && asPath.includes(url)) return c.focus();
        }
        return self.clients.openWindow(url);
    })());
});

// ‏(2026-11-12، W13-4·C2f-R3·1) عَلى مُضيفِ المَتجَرِ أَو الدَور يُحَوَّلُ رابِطُ الإشعار (/{slug}/…) إلى عُنوانِه النَظيف (/…)، فَالنافِذَةُ الَّتي فَتَحَها
// لا تَحمِلُه. هذا عُنوانُ النافِذَةِ كَما يَكتُبُه شَكلُ المَسار — فَيُسأَلُ السُؤالُ نَفسُه. والنِطاقُ / أَو /r/{role}/ وَحدَه (صَفحَةٌ نَظيفَةٌ عَلى مُضيف)؛
// وفي وَضعِ المَسارِ النِطاقُ /{slug}/… فَلا شَيء.
function pathFormOnStoreHost(href, url) {
    if (!/^\/(r\/[^/]+\/)?$/.test(new URL(self.registration.scope).pathname)) return null;
    const store = /^\/([^/?#]+)/.exec(url);
    if (!store) return null;
    const at = new URL(href);
    const label = at.hostname.split('.')[0];
    if (label !== store[1] && !label.startsWith(store[1] + '-')) return null;
    return at.origin + '/' + store[1] + (at.pathname === '/' ? '' : at.pathname) + at.search + at.hash;
}
