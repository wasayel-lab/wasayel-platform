// ─── مُستَهلِك SignalR في المُتَصَفِّح ─────────────────────────────────
// كانَ السيرفر يُرسِل "unread_changed" بَعد كُلّ رِسالَة/إشعار لكِن لا
// مُستَهلِك في الـ UI، فَالـ badges تَتَجَمَّد حَتَّى يَضغَط F5.
// هذا الـ script يَتَّصِل بِـ /realtime، ويُحَدِّث الـ badges فَور وُصول
// الحَدَث، ويُحَدِّث الصَفحَة لَو كانَ المُستَخدِم داخِل دَردَشَة.
(function () {
  if (!window.acRealtimeToken || !window.acRealtimeSlug) return;
  if (typeof signalR === 'undefined') return;

  const slug  = window.acRealtimeSlug;
  const token = window.acRealtimeToken;

  // accessTokenFactory يُمَرِّر الـ token كَ Bearer header لِـ negotiate
  // ويُعيد استِخدامَه لِـ WebSocket. بَديل عَن وَضعِه في query string
  // (يَتَسَرَّب في access logs).
  const conn = new signalR.HubConnectionBuilder()
    .withUrl('/realtime', { accessTokenFactory: () => token })
    .withAutomaticReconnect()
    .build();

  conn.on('unread_changed', refreshBadges);

  async function refreshBadges() {
    try {
      const r = await fetch(`/${slug}/api/me/unread`, { credentials: 'include' });
      if (!r.ok) return;
      const data = await r.json();
      updateBadge('messages',      data.messages      || 0);
      updateBadge('notifications', data.notifications || 0);

      // داخِل دَردَشَة مَفتوحَة؟ أَعِد تَحميل الصَفحَة لِجَلب الرِسالَة الجَديدَة.
      // SSR لا يَستَطيع حَقن DOM بَعد التَّحميل — full reload هو الحَلّ الأَبسَط.
      if (/\/chats\/[^\/]+$/.test(window.location.pathname)) {
        window.location.reload();
      }
    } catch { /* صامِت — شَبَكَة سَيِّئَة لا تَكسِر الصَفحَة */ }
  }

  function updateBadge(kind, count) {
    // الـ widgets تَكتُب data-badge-key="messages|notifications" — نَستَخدِم
    // نَفس المِفتاح بَدَلاً مِن إضافَة attribute مُوازٍ.
    document.querySelectorAll(`[data-badge-key="${kind}"]`).forEach(el => {
      if (count > 0) {
        el.textContent = count > 99 ? '99+' : String(count);
        el.style.display = '';
      } else {
        el.textContent = '';
        el.style.display = 'none';
      }
    });
  }

  conn.start().catch(() => { /* fail silently — UI تَستَمِرّ بِالعَمَل */ });
})();
