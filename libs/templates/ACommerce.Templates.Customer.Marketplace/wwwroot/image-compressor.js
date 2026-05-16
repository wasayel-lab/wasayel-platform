// ضغط الصور قبل الرفع باستخدام HTML5 Canvas — يقلّل حجم الـ payload بـ ~80%
// لصور الهواتف، ويُولّد مُصغّراً منفصلاً (~150KB) للعرض في البطاقات.
//
// السبب: الواجهة كانت تُرسل data URLs خام (JPEG عادةً 2-5MB لكلّ صورة)
// ويُخزّنها backend في `ImagesCsv`. عند فتح صفحة بها 10 إعلانات يحمل المتصفّح
// 50MB من البيانات. الآن:
//   - الصورة الأساسيّة: maxDim=1600، jpeg quality=0.8 (~250KB).
//   - المُصغّر: maxDim=400،  jpeg quality=0.7 (~30KB) — يُخزَّن في
//     ListingEntity.ThumbnailUrl ويُرجَع في /home/explore و /favorites
//     لاستهلاك minimal في القوائم.
//
// الواجهة تستدعي عبر JSInterop:
//   var result = await JS.InvokeAsync<ImageBundle>(
//       "ejarImg.compressFile", inputFile, /*maxDim*/ 1600, /*quality*/ 0.8,
//                                          /*thumbDim*/ 400, /*thumbQ*/ 0.7);
//   // result = { full: "data:image/jpeg;base64,...", thumb: "data:..." }

window.ejarImg = (function () {
  // يقرأ data URL إلى HTMLImageElement. اخترنا data URL بدل File لأنّ .NET
  // Blazor لا يستطيع تمرير IBrowserFile مباشرةً إلى JSInterop. C# يقرأ البايت
  // مرّة واحدة ويحوّلها إلى base64 string ثمّ يمرّرها هنا.
  function loadImage(dataUrl) {
    return new Promise((resolve, reject) => {
      const img = new Image();
      img.onload  = () => resolve(img);
      img.onerror = (e) => reject(e);
      img.src = dataUrl;
    });
  }

  // يرسم الصورة على canvas ضمن حدود maxDim مع الحفاظ على النسبة، ويُرجع
  // dataURL بصيغة JPEG على الجودة المطلوبة. الإطارات الصغيرة لا تُكبَّر —
  // لو الصورة أصغر من maxDim نُبقيها على حالها.
  function drawScaled(img, maxDim, quality) {
    let w = img.naturalWidth, h = img.naturalHeight;
    if (w > maxDim || h > maxDim) {
      const ratio = Math.min(maxDim / w, maxDim / h);
      w = Math.round(w * ratio);
      h = Math.round(h * ratio);
    }
    const canvas = document.createElement('canvas');
    canvas.width = w; canvas.height = h;
    const ctx = canvas.getContext('2d', { alpha: false });
    // خلفيّة بيضاء قبل الرسم — JPEG لا يدعم الشفافيّة، فالـ PNG الشفّافة
    // تظهر بأطراف سوداء بدون هذه الخطوة.
    ctx.fillStyle = '#ffffff';
    ctx.fillRect(0, 0, w, h);
    ctx.drawImage(img, 0, 0, w, h);
    return canvas.toDataURL('image/jpeg', quality);
  }

  return {
    /**
     * يضغط data URL لصورة ويُرجع نسختين: الكاملة (للعرض في صفحة التفاصيل)
     * والمُصغّرة (للعرض في البطاقات). يحوّل أيّ صيغة (PNG/HEIC/WEBP) إلى
     * JPEG موحّدة فيُسهّل التخزين والعرض.
     *
     * @param {string} dataUrl  أصل الصورة كـ data URL (data:image/...;base64,...)
     * @param {number} maxDim   أقصى بُعد للصورة الكاملة بالبكسل (افتراضي 1600).
     * @param {number} quality  جودة JPEG للكاملة (0.0-1.0، افتراضي 0.8).
     * @param {number} thumbDim أقصى بُعد للمُصغّر (افتراضي 400).
     * @param {number} thumbQ   جودة JPEG للمُصغّر (افتراضي 0.7).
     */
    compressDataUrl: async function (dataUrl, maxDim, quality, thumbDim, thumbQ) {
      maxDim   = maxDim   || 1600;
      quality  = quality  || 0.8;
      thumbDim = thumbDim || 400;
      thumbQ   = thumbQ   || 0.7;
      try {
        const img = await loadImage(dataUrl);
        const full  = drawScaled(img, maxDim, quality);
        const thumb = drawScaled(img, thumbDim, thumbQ);
        return { full, thumb };
      } catch (e) {
        console.warn('[ejarImg] compress failed:', e);
        return null;
      }
    },

    /** حجم data URL بالبايت (للعرض/التشخيص). */
    sizeOf: function (dataUrl) {
      if (!dataUrl) return 0;
      const i = dataUrl.indexOf(',');
      if (i < 0) return dataUrl.length;
      // base64: 4 chars ≈ 3 bytes
      return Math.floor((dataUrl.length - i - 1) * 3 / 4);
    }
  };
})();
