// إشعارات المتصفّح الأصليّة (Web Notification API). تُظهر toast على مستوى
// نظام التشغيل عندما يكون التطبيق مفتوحاً لكن غير مرئي (تبويب آخر، نافذة
// أخرى، الهاتف مقفول). لا تتطلّب أيّ خادم خارجيّ ولا VAPID — تعمل فوراً.
//
// لإشعارات تصل عندما يكون التبويب **مغلقاً**: تتطلّب Web Push API
// (Service Worker + push subscription + VAPID + خادم push). تكاملها مع
// FCM موجود في libs/kits/Notifications/Providers/Firebase لكنّه يحتاج:
//   1) Firebase project + Service Account JSON على الـ Backend
//   2) VAPID public key يُرسَل للواجهة
//   3) Service worker register يستدعي pushManager.subscribe
//   4) إرسال الـ subscription إلى الـ Backend وتخزينه per-user
// نلجأ إليه عند توفّر هذه المتطلّبات. النسخة الحاليّة تستخدم الـ
// Notification API المباشر فقط.

window.ejarNotify = (function () {
  function permission() {
    try { return Notification.permission || 'default'; }
    catch { return 'unsupported'; }
  }

  async function requestPermission() {
    if (!('Notification' in window)) return 'unsupported';
    if (Notification.permission === 'granted') return 'granted';
    if (Notification.permission === 'denied')  return 'denied';
    try {
      const result = await Notification.requestPermission();
      return result;
    } catch { return 'denied'; }
  }

  // ── In-app toast (fallback عندما يفشل system notification) ───────────
  // يعمل على كلّ المتصفّحات (موبايل + كمبيوتر) لأنّه DOM نقيّ — لا يعتمد
  // على Notification API ولا على Service Worker. يظهر أعلى الشاشة بعد
  // taskbar الموبايل (top: 12px مع env safe-area).
  //
  // لماذا fallback ضروريّ:
  //   • iOS Safari قبل 16.4 لا يدعم Notification API إطلاقاً.
  //   • iOS PWA يدعمه فقط لو نُصِّب من home screen + عبر HTTPS.
  //   • Android Chrome يحتاج إذن الإشعارات؛ كثير من المستخدمين يرفضونه.
  // في كلّ هذه الحالات، رسائل الزمن الحقيقي تصل لكنّها لا تُرى — هذا
  // الـ toast يعالج ذلك.
  let _toastHost = null;
  function ensureToastHost() {
    if (_toastHost && document.body.contains(_toastHost)) return _toastHost;
    _toastHost = document.createElement('div');
    _toastHost.id = 'ejar-toast-host';
    Object.assign(_toastHost.style, {
      position: 'fixed',
      top: 'calc(env(safe-area-inset-top, 0px) + 12px)',
      left: '50%',
      transform: 'translateX(-50%)',
      zIndex: '99999',
      display: 'flex',
      flexDirection: 'column',
      gap: '8px',
      pointerEvents: 'none',
      maxWidth: 'calc(100vw - 24px)',
      width: 'min(420px, calc(100vw - 24px))'
    });
    document.body.appendChild(_toastHost);
    return _toastHost;
  }

  function showInAppToast(title, body, opts) {
    try {
      const host = ensureToastHost();
      const t = document.createElement('div');
      const tag = (opts && opts.tag) || 'ejar';
      t.dataset.tag = tag;

      // ابدأ بإزالة سابق بنفس الـ tag (مثل system Notification renotify).
      Array.from(host.querySelectorAll(`[data-tag="${CSS.escape(tag)}"]`))
        .forEach(prev => prev.remove());

      Object.assign(t.style, {
        background: 'var(--ac-surface, #fff)',
        color: 'var(--ac-on-surface, #1a1a1a)',
        boxShadow: '0 8px 24px rgba(0,0,0,0.18)',
        borderRadius: '12px',
        padding: '12px 14px',
        cursor: 'pointer',
        pointerEvents: 'auto',
        display: 'flex',
        alignItems: 'flex-start',
        gap: '10px',
        fontFamily: 'inherit',
        lineHeight: '1.4',
        animation: 'ejar-toast-in 180ms ease-out',
        border: '1px solid var(--ac-border, rgba(0,0,0,0.08))'
      });
      const tt = document.createElement('div');
      tt.style.cssText = 'flex:1;min-width:0';
      const tHead = document.createElement('div');
      tHead.style.cssText = 'font-weight:600;font-size:14px;margin-bottom:2px;overflow:hidden;text-overflow:ellipsis;white-space:nowrap';
      tHead.textContent = title || 'إيجار';
      const tBody = document.createElement('div');
      tBody.style.cssText = 'font-size:13px;opacity:0.85;overflow:hidden;text-overflow:ellipsis;display:-webkit-box;-webkit-line-clamp:2;-webkit-box-orient:vertical';
      tBody.textContent = body || '';
      tt.appendChild(tHead); tt.appendChild(tBody);
      const close = document.createElement('button');
      close.textContent = '×';
      close.setAttribute('aria-label', 'إغلاق');
      close.style.cssText = 'background:none;border:none;font-size:20px;line-height:1;cursor:pointer;color:inherit;opacity:0.5;padding:0 4px';
      close.addEventListener('click', e => { e.stopPropagation(); t.remove(); });
      t.appendChild(tt); t.appendChild(close);

      const url = opts && opts.url;
      if (url) {
        t.addEventListener('click', () => {
          try { window.location.href = url; } catch {}
        });
      }
      host.appendChild(t);

      // dismiss timer
      const ttl = (opts && opts.ttl) || 5000;
      setTimeout(() => {
        if (t.parentNode) {
          t.style.transition = 'opacity 200ms';
          t.style.opacity = '0';
          setTimeout(() => t.remove(), 220);
        }
      }, ttl);

      return true;
    } catch (e) {
      console.warn('[ejarNotify] in-app toast فشل:', e);
      return false;
    }
  }

  // أضِف keyframes للـ animation مرّة واحدة.
  (function injectStyle() {
    if (document.getElementById('ejar-toast-style')) return;
    const s = document.createElement('style');
    s.id = 'ejar-toast-style';
    s.textContent = `@keyframes ejar-toast-in{from{transform:translateY(-20px);opacity:0}to{transform:translateY(0);opacity:1}}`;
    document.head.appendChild(s);
  })();

  async function show(title, body, opts) {
    const url = (opts && opts.url) || null;
    const tabVisible = document.visibilityState === 'visible';
    const alwaysShow = !!(opts && opts.alwaysShow);
    const sysAvailable = ('Notification' in window) && Notification.permission === 'granted';

    // ① حاول إشعار النظام أوّلاً عند كون التبويب مخفيّاً، لأنّه يلفت النظر
    //    خارج التطبيق (taskbar Windows، notification center للجوال).
    if (sysAvailable && (!tabVisible || alwaysShow)) {
      try {
        const options = {
          body: body || '',
          icon: (opts && opts.icon) || '/icon-192.png',
          badge: '/icon-192.png',
          tag:  (opts && opts.tag)  || 'ejar',
          renotify: !!(opts && opts.renotify),
          data: { url },
        };

        // Chrome/Edge على Android يمنع `new Notification(...)` المباشر —
        // ServiceWorkerRegistration.showNotification يعمل على الموبايل والكمبيوتر.
        if ('serviceWorker' in navigator) {
          try {
            const reg = await navigator.serviceWorker.ready;
            if (reg && typeof reg.showNotification === 'function') {
              await reg.showNotification(title || 'إيجار', options);
              // ② لو التبويب أيضاً مرئيّ (alwaysShow=true)، ضع toast داخل
              //    التطبيق أيضاً — system notification قد يُكتم في focus mode.
              if (tabVisible && alwaysShow) showInAppToast(title, body, opts);
              return true;
            }
          } catch (e) {
            console.debug('[ejarNotify] SW.showNotification فشل، fallback:', e && e.message);
          }
        }
        const n = new Notification(title || 'إيجار', options);
        if (url) n.onclick = () => { window.focus(); window.location.href = url; n.close(); };
        if (tabVisible && alwaysShow) showInAppToast(title, body, opts);
        return true;
      } catch (e) {
        console.warn('[ejarNotify] system notification فشل، نتحوّل لـ in-app toast:', e);
        // fall through إلى in-app toast
      }
    }

    // ③ Fallback (الجوال غالباً): in-app toast. يعمل بدون Notification API
    //    وبدون إذن، فيغطّي iOS قبل 16.4 + Android Chrome مع إذن مرفوض +
    //    أيّ متصفّح في صفحة HTTP (notification API يحتاج HTTPS).
    return showInAppToast(title, body, opts);
  }

  return { permission, requestPermission, show, showInAppToast };
})();

// أدوات DOM صغيرة. ejarChatUi.scrollToBottom(selector) يدفع الـ container
// إلى آخر الرسائل (smooth إن كان already near bottom). يُستدعى من ChatRoom
// عند فتح المحادثة وعند كل رسالة جديدة.
window.ejarChatUi = (function () {
  function scrollToBottom(selector, behavior) {
    try {
      const el = document.querySelector(selector);
      if (!el) return;
      // double rAF: قبل الرسم لنجبر القيمة بعد ما يُحدّث Blazor الـ DOM.
      requestAnimationFrame(() => requestAnimationFrame(() => {
        el.scrollTo({ top: el.scrollHeight, behavior: behavior || 'auto' });
      }));
    } catch (_) { /* noop */ }
  }

  function focus(selector) {
    try {
      // double rAF لينتظر Blazor ما يُنهي إنشاء الـ DOM قبل focus.
      requestAnimationFrame(() => requestAnimationFrame(() => {
        const el = document.querySelector(selector);
        if (el && typeof el.focus === 'function') el.focus({ preventScroll: false });
      }));
    } catch (_) { /* noop */ }
  }

  return { scrollToBottom, focus };
})();
