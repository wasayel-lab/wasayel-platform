// ─── مُستَهلِك SignalR في المُتَصَفِّح ─────────────────────────────────
// كانَ السيرفر يُرسِل "unread_changed" بَعد كُلّ رِسالَة/إشعار لكِن لا
// مُستَهلِك في الـ UI، فَالـ badges تَتَجَمَّد حَتَّى يَضغَط F5.
// هذا الـ script يَتَّصِل بِـ /realtime، ويُحَدِّث الـ badges فَور وُصول
// الحَدَث، ويُحَدِّث الصَفحَة لَو كانَ المُستَخدِم داخِل دَردَشَة.
//
// ─── وإشعارُ القِراءَة — المَوجَة ٧ ───────────────────────────────────
// كانَت صَفحَتا `ChatRoom` و`Notifications` **تَكتُبانِ في طَلَب `GET`**:
// تُصَفِّرانِ عَدّادَ غَير المَقروء أَثناءَ التَصيير. وذلك يَجعَل كُلَّ
// مَن يَفتَح الرابِط بِلا قِراءَة — زاحِفٌ، أَو جالِبٌ مُسبَق، أَو أَداةُ
// تَحَقُّقٍ بَصَريّ — يُبَدِّل حالَةَ القاعِدَة. فَصارَ الإشعارُ **نِداءً
// غَير مُتَزامِن بَعدَ التَصيير** إلى نُقطَةِ `POST` مَحروسَة.
//
// ولِماذا هُنا لا في الصَفحَتَين: هذا المِلَفُّ **مُحَمَّلٌ سَلَفاً على
// كُلّ صَفحَةٍ لِمُستَخدِمٍ مُوَثَّق** (شَرطُ `App.razor`)، وهُوَ يُفَرِّق
// أَصلاً بِمَسار الصَفحَة (سَطرُ إعادَةِ التَحميل داخِلَ الدَردَشَة).
// فَالسابِقَةُ قائِمَةٌ، ولا يُضاف وَسمُ `<script>` إلى صَفحَةٍ مَحروسَة
// بِلَقطَةِ مَظهَرٍ مُثَبَّتَة. (القاعِدَة ٨: استَعمِل القائِم.)
(function () {
  const slug = window.acRealtimeSlug;
  if (!slug) return;

  // ═══ ١) إشعارُ القِراءَة — بَعدَ التَصيير، لا داخِلَ الطَلَب ═══
  // المَسارانِ بِفَرعَيهِما: `/{slug}/…` و`/{slug}/r/{role}/…`.
  const path = window.location.pathname;
  const chat = path.match(/^\/[^\/]+(?:\/r\/[^\/]+)?\/chats\/([0-9a-fA-F-]{36})\/?$/);
  if (chat) {
    markRead(`/${slug}/chats/${chat[1]}/read`);
  } else if (/^\/[^\/]+(?:\/r\/[^\/]+)?\/notifications\/?$/.test(path)) {
    markRead(`/${slug}/notifications/read`);
  }

  async function markRead(url) {
    try {
      const r = await fetch(url, { method: 'POST', credentials: 'same-origin' });
      // العَدّاداتُ وَحدَها تُحَدَّث — **لا `onUnreadChanged`**: تِلكَ
      // تُعيد تَحميلَ صَفحَة الدَردَشَة، وإعادَةُ التَحميل تُطلِق هذا
      // النِداءَ ثانِيَةً فَتَدور الحَلقَة بِلا نِهايَة.
      if (r.ok) updateBadges();
    } catch { /* صامِت — إشعارُ قِراءَةٍ فاشِل لا يَكسِر صَفحَة */ }
  }

  // ═══ ٢) الهُبّ — يَبقى مَشروطاً بِمَكتَبَة CDN وبِتوكِن ═══
  const token = window.acRealtimeToken;
  if (token && typeof signalR !== 'undefined') {
    // accessTokenFactory يُمَرِّر الـ token كَ Bearer header لِـ negotiate
    // ويُعيد استِخدامَه لِـ WebSocket. بَديل عَن وَضعِه في query string
    // (يَتَسَرَّب في access logs).
    const conn = new signalR.HubConnectionBuilder()
      .withUrl('/realtime', { accessTokenFactory: () => token })
      .withAutomaticReconnect()
      .build();

    conn.on('unread_changed', onUnreadChanged);
    conn.start().catch(() => { /* fail silently — UI تَستَمِرّ بِالعَمَل */ });
  }

  async function onUnreadChanged() {
    await updateBadges();
    // داخِل دَردَشَة مَفتوحَة؟ أَعِد تَحميل الصَفحَة لِجَلب الرِسالَة الجَديدَة.
    // SSR لا يَستَطيع حَقن DOM بَعد التَّحميل — full reload هو الحَلّ الأَبسَط.
    if (/\/chats\/[^\/]+$/.test(window.location.pathname)) {
      window.location.reload();
    }
  }

  async function updateBadges() {
    try {
      const r = await fetch(`/${slug}/api/me/unread`, { credentials: 'include' });
      if (!r.ok) return;
      const data = await r.json();
      updateBadge('messages',      data.messages      || 0);
      updateBadge('notifications', data.notifications || 0);
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
})();
