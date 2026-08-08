// ─── مُحَرِّرات الادمن المُهَيكَلَة (chip inputs + repeatable rows) ──────────
// بَديل مُربَّعات النَّصّ بِفَواصِل. يَعمَل تَحت Blazor enhanced navigation:
// يُهَيِّئ عِندَ التَّحميل الأَوَّل + بَعد كُلّ enhancedload. مُتَكَرِّر-آمِن
// (data-ac-init يَمنَع الرَّبط المُزدَوَج). يُسَلسِل DOM المُهَيكَل إلى الحَقل
// المَخفيّ بِالصيغَة الَّتي يَفهَمُها الـ endpoint — صِفر تَغيير خَلفيّ.

(function () {
  function slug(s) {
    return (s || '').trim().toLowerCase()
      .replace(/[^a-z0-9]+/g, '_').replace(/^_+|_+$/g, '');
  }
  function chip(text, raw) {
    var s = document.createElement('span');
    s.className = 'ac-chip-tag';
    if (raw != null) s.setAttribute('data-raw', raw);
    var t = document.createElement('span'); t.className = 't'; t.textContent = text;
    var b = document.createElement('button'); b.type = 'button'; b.textContent = '×';
    s.appendChild(t); s.appendChild(b);
    return s;
  }

  // ── مُحَرِّر المُدُن ───────────────────────────────────────────────────
  function initCities() {
    var editor = document.getElementById('cities-editor');
    var form   = document.getElementById('regions-form');
    var hidden = document.getElementById('regions-hidden');
    var addBtn = document.getElementById('add-city');
    var tpl    = document.getElementById('city-row-tpl');
    if (!editor || !form || !hidden || form.dataset.acInit) return;
    form.dataset.acInit = '1';

    editor.addEventListener('keydown', function (e) {
      if (e.key !== 'Enter') return;
      var inp = e.target.closest('.ac-chip-entry'); if (!inp) return;
      e.preventDefault();
      var v = inp.value.trim(); if (!v) return;
      inp.parentNode.insertBefore(chip(v, null), inp); inp.value = '';
    });
    editor.addEventListener('click', function (e) {
      if (e.target.closest('[data-remove-city]')) e.target.closest('[data-city-row]').remove();
      else if (e.target.closest('.ac-chip-tag button')) e.target.closest('.ac-chip-tag').remove();
    });
    if (addBtn && tpl) addBtn.addEventListener('click', function () {
      editor.appendChild(tpl.content.cloneNode(true));
      var n = editor.querySelectorAll('.ac-city-name');
      if (n.length) n[n.length - 1].focus();
    });
    form.addEventListener('submit', function () {
      var lines = [];
      editor.querySelectorAll('[data-city-row]').forEach(function (row) {
        var name = (row.querySelector('.ac-city-name').value || '').trim();
        if (!name) return;
        var ds = [];
        row.querySelectorAll('.ac-chip-tag .t').forEach(function (t) {
          var v = (t.textContent || '').trim(); if (v) ds.push(v);
        });
        lines.push(ds.length ? (name + ' > ' + ds.join('،')) : name);
      });
      hidden.value = lines.join('\n');
    });
  }

  // ── مُحَرِّر الخَصائِص ────────────────────────────────────────────────
  function syncOpts(row) {
    var type = row.querySelector('.ac-attr-type').value;
    var box  = row.querySelector('[data-options]');
    if (box) box.style.display = (type === 'SingleSelect' || type === 'MultiSelect') ? '' : 'none';
  }
  function initAttrs() {
    var editor = document.getElementById('attrs-editor');
    var form   = document.getElementById('attrs-form');
    var hidden = document.getElementById('defs-hidden');
    var addBtn = document.getElementById('add-attr');
    var tpl    = document.getElementById('attr-row-tpl');
    if (!editor || !form || !hidden || form.dataset.acInit) return;
    form.dataset.acInit = '1';

    editor.addEventListener('keydown', function (e) {
      if (e.key !== 'Enter') return;
      var inp = e.target.closest('.ac-chip-entry'); if (!inp) return;
      e.preventDefault();
      var v = inp.value.trim(); if (!v) return;
      var raw = v.indexOf('=') >= 0 ? v : (slug(v) + '=' + v);
      var label = raw.indexOf('=') >= 0 ? raw.substring(raw.indexOf('=') + 1) : raw;
      inp.parentNode.insertBefore(chip(label, raw), inp); inp.value = '';
    });
    editor.addEventListener('click', function (e) {
      if (e.target.closest('[data-remove-attr]')) e.target.closest('[data-attr-row]').remove();
      else if (e.target.closest('.ac-chip-tag button')) e.target.closest('.ac-chip-tag').remove();
    });
    editor.addEventListener('change', function (e) {
      if (e.target.closest('.ac-attr-type')) syncOpts(e.target.closest('[data-attr-row]'));
    });
    if (addBtn && tpl) addBtn.addEventListener('click', function () {
      editor.appendChild(tpl.content.cloneNode(true));
      var n = editor.querySelectorAll('.ac-attr-name');
      if (n.length) n[n.length - 1].focus();
    });
    form.addEventListener('submit', function () {
      var lines = [];
      editor.querySelectorAll('[data-attr-row]').forEach(function (row) {
        var name = (row.querySelector('.ac-attr-name').value || '').trim();
        var code = (row.querySelector('.ac-attr-code').value || '').trim();
        if (!code) code = slug(name);
        if (!code && !name) return;
        var type = row.querySelector('.ac-attr-type').value;
        var req  = row.querySelector('.ac-attr-req').checked ? 'req' : 'opt';
        var opts = [];
        row.querySelectorAll('.ac-chip-tag').forEach(function (c) {
          var raw = c.getAttribute('data-raw'); if (raw) opts.push(raw);
        });
        var line = code + ' | ' + name + ' | ' + type + ' | ' + req;
        if ((type === 'SingleSelect' || type === 'MultiSelect') && opts.length)
          line += ' | ' + opts.join(',');
        lines.push(line);
      });
      hidden.value = lines.join('\n');
    });
  }

  // ── مُحَرِّر الفِئات ─────────────────────────────────────────────────
  function initCats() {
    var editor = document.getElementById('cats-editor');
    var form   = document.getElementById('cats-form');
    var hidden = document.getElementById('cats-hidden');
    var addBtn = document.getElementById('add-cat');
    var tpl    = document.getElementById('cat-row-tpl');
    if (!editor || !form || !hidden || form.dataset.acInit) return;
    form.dataset.acInit = '1';

    editor.addEventListener('click', function (e) {
      if (e.target.closest('[data-remove-cat]')) e.target.closest('[data-cat-row]').remove();
    });
    if (addBtn && tpl) addBtn.addEventListener('click', function () {
      editor.appendChild(tpl.content.cloneNode(true));
      var n = editor.querySelectorAll('.ac-cat-label');
      if (n.length) n[n.length - 1].focus();
    });
    form.addEventListener('submit', function () {
      var lines = [];
      editor.querySelectorAll('[data-cat-row]').forEach(function (row) {
        var label = (row.querySelector('.ac-cat-label').value || '').trim();
        var sl    = (row.querySelector('.ac-cat-slug').value || '').trim();
        var icon  = (row.querySelector('.ac-cat-icon').value || '').trim() || '🏷️';
        var kind  = (row.querySelector('.ac-cat-kind').value || '').trim();
        if (!sl) sl = slug(label) || ('cat_' + Math.random().toString(36).slice(2, 7));
        if (!label && !sl) return;
        lines.push(sl + ' | ' + label + ' | ' + icon + ' | ' + kind);
      });
      hidden.value = lines.join('\n');
    });
  }

  function initAll() {
    try { initCities(); } catch (e) {}
    try { initAttrs(); } catch (e) {}
    try { initCats(); } catch (e) {}
  }

  initAll();
  document.addEventListener('DOMContentLoaded', initAll);
  // Blazor enhanced navigation: يُرَقِّع DOM بِلا reload — أَعِد التَّهيِئَة.
  if (window.Blazor && typeof Blazor.addEventListener === 'function') {
    try { Blazor.addEventListener('enhancedload', initAll); } catch (e) {}
  }
  // احتِياط: راقِب أَيّ إدراج لِلمُحَرِّرات (يَغطّي كُلّ مَسارات التَّنَقُّل).
  try {
    new MutationObserver(function () { initAll(); })
      .observe(document.body, { childList: true, subtree: true });
  } catch (e) {}
})();
