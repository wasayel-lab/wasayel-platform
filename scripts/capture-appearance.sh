#!/usr/bin/env bash
# ═══════════════════════════════════════════════════════════════════════
#  اِلتِقاط تَوصيف المَظهَر — أَساس المُقارَنَة بايتاً بِبايت
# ───────────────────────────────────────────────────────────────────────
#  يَلتَقِط HTML كامِلاً (بِكُلّ وُسوم <link> و<style> فيه) لِلصَفَحات
#  المَرجِعِيَّة مِن خادِم حَيّ، ويُطَبِّع **شَيئاً واحِداً فَقَط**:
#  تَعليق «Blazor-Server-Component-State» — وهو الوَحيد الَّذي يَتَغَيَّر
#  بَين طَلَبَين مُتَتالِيَين لِنَفس الصَفحَة (مُقاس، لا مُقَدَّر: طَلَبان
#  مُتَتالِيان لِـ /ashare يَختَلِفان في هذا السَطر وَحدَه).
#
#  ولِماذا لَقطات مَحفوظَة هُنا، والأَدوار قارَنَت مَسارَين؟ لِأَنّ
#  المَقيس هُنا **مُخرَج HTTP** لا دالَّة نَقِيَّة — لا يُوجَد «المَسار
#  القَديم» لِيُستَدعى بَعدَ التَّبديل. فَاللَقطَة هي الشَكل الوَحيد
#  المُتاح لِلبُرهان، وشَرطُها أَن تُؤخَذ **قَبل** أَيّ تَعديل — وهذا ما
#  يَفعَلُه هذا المِلَفّ في كوميت مُستَقِلّ سابِق.
#
#  الاستِعمال:
#     scripts/capture-appearance.sh [OUT_DIR] [BASE_URL]
#  الافتِراضيّ:
#     OUT_DIR  = tests/characterization/appearance/baseline
#     BASE_URL = http://localhost:5050
# ═══════════════════════════════════════════════════════════════════════
set -euo pipefail

OUT_DIR="${1:-tests/characterization/appearance/baseline}"
BASE_URL="${2:-http://localhost:5050}"

# الصَفَحات المَرجِعِيَّة: بَوّابَة ashare (مِقياس التَّكافُؤ الصِفريّ
# الحَيّ) ورَئيسِيّات أَدوارِه الثَّلاثَة واستِكشافُه، ثُمَّ بَوّابَة
# adwar-demo (ساحَة التَّجرِبَة).
#
# ‏2026-08-11 — أُضيفَ مُتَغايِرا الاستِكشاف (‏#7 و#8). والسَبَب مَقيس:
# مَوجَة تَرحيل النُصوص تَمَسّ `TenantExplore.razor`، و`/ashare/explore`
# وَحدَها **لا تُصَيِّر فَرعَين مِنها** — نافِذَة الفَلاتِر (‏لا تُفتَح
# إلّا بِـ`?filters=open`) وحالَة الفَراغ (‏لا تَقَع إلّا بِفَلتَر بِلا
# نَتائِج). فَبَوّابَةٌ تُقارِن الصَفحَة الافتِراضِيَّة وَحدَها كانَت
# سَتُعطي أَخضَرَ عَن ‏17 سِلسِلَة **لَم تُقارَن أَصلاً** — وهو عَينُ
# العَمى الَّذي يَحرُسُه عَدّاد القاعِدَة ١٠.
#
# والزِيادَة لا تُبطِل اللَقطَة القائِمَة: `compare-appearance.sh`
# يَدور على مِلَفّات **الأَساس**، فَما زادَ في الالتِقاط ولا أَساسَ لَه
# يُهمَل.
#
# ‏2026-08-15 — أُضيفَت خَمس صَفَحات (‏#9–#13) لِمَوجَة النُصوص الثانِيَة،
# وكُلٌّ مِنها **مَقيسَة قَبل أَن تُضاف** لا مَظنونَة:
#   · `/` — الهُبوط: ‏28 مِن 29 سِلسِلَة في `Landing.razor` تَقَع فيها،
#     وصِفرٌ مِنها كانَ يَقَع في الثَماني القائِمَة.
#   · `/ashare/legal` وفَرعاها — `LegalHub.razor` ثَلاثَة فُروع
#     حَصرِيَّة (شُروط/خُصوصِيَّة/استِرجاع) لا يُصَيَّر مِنها في الطَلَب
#     الواحِد إلّا واحِد. والثَلاثَة مَعاً: ‏24 مِن 24 — تَغطِيَة تامَّة.
#   · `/ashare/plans` — ‏6 مِن 9 في `Plans.razor`.
#
# ‏2026-08-15 (‏#14–#15) — و`theme-demo` لِلسَبَب الَّذي أَسقَطَ
# `TenantHome` مِن المَوجَة الأولى: `ashare` و`adwar-demo` كِلاهُما
# **بِأَدوار**، فَزائِرُهُما يَقَع على فَرع `RoleLanding` ولا يُصَيَّر
# الفَرع الافتِراضيّ (هيرو + فِئات + مُمَيَّز + أَحدَث) أَبَداً. و
# `theme-demo` بِلا أَدوار فَيُصَيِّرُه. والفَرق مَقيس: ‏12/34 → ‏24/34.
# و`?city=` يَفتَح فَرع الفَراغ المُوَجَّه (‏«لا إعلانات في … بَعد»)
# الَّذي لا يَقَع بِلا فَلتَر مَدينَة.
# والقِياس نَفسُه صُحِّحَ مَرَّةً: أَوَّل عَدّ بَحَثَ عَن السِلسِلَة
# **خاماً** في الصَفحَة فَأَعطى أَصفاراً كاذِبَة، لِأَنّ Razor يَكتُب
# مُخرَج التَعبير مُرَمَّزاً (‏`&#x627;`…) — فَنَصٌّ يُصَيَّر عَبر خاصِّيَّة
# مُكَوِّن لا يوجَد خاماً أَبَداً. العَدّ الصَحيح يَفُكّ التَرميز أَوَّلاً.
PAGES=(
  "ashare-portal:/ashare"
  "ashare-role-customer:/ashare/r/customer"
  "ashare-role-host:/ashare/r/host"
  "ashare-role-vendor:/ashare/r/vendor"
  "ashare-explore:/ashare/explore"
  "adwar-demo-portal:/adwar-demo"
  "ashare-explore-filters:/ashare/explore?filters=open"
  "ashare-explore-empty:/ashare/explore?category=__none__"
  "landing:/"
  "ashare-legal-terms:/ashare/legal"
  "ashare-legal-privacy:/ashare/legal/privacy"
  "ashare-legal-returns:/ashare/legal/returns"
  "ashare-plans:/ashare/plans"
  "theme-demo-portal:/theme-demo"
  "theme-demo-city:/theme-demo?city=Riyadh"
)

# أَوراق الأَنماط الَّتي تُحَمِّلها كُلّ صَفحَة — المَظهَر = HTML + CSS،
# فَالتَّوصيف يَلزَمُه الطَّرَفان. (وهذه هي النُسخَة الَّتي تُثبِت لاحِقاً
# أَنّ قيمَة كُلّ رَمز مَبثوث تُساوي الحَرفِيَّة الَّتي حَلَّ مَحَلَّها.)
SHEETS=(
  "widgets.css:/_content/ACommerce.Templates.Customer.Marketplace/css/widgets.css"
  "templates-shared.css:/_content/ACommerce.Templates.Customer.Marketplace/css/templates-shared.css"
  "templates-marketplace.css:/_content/ACommerce.Templates.Customer.Marketplace/css/templates-marketplace.css"
  "app.css:/_content/ACommerce.Templates.Customer.Marketplace/css/app.css"
  "site.css:/_content/ACommerce.Templates.Customer.Marketplace/css/site.css"
  "studio.css:/_content/ACommerce.Templates.Customer.Marketplace/css/studio.css"
  "premium.css:/_content/ACommerce.Templates.Customer.Marketplace/css/premium.css"
  "branding-ashare.css:/branding/ashare.css"
)

mkdir -p "$OUT_DIR" "$OUT_DIR/css"

for entry in "${PAGES[@]}"; do
  name="${entry%%:*}"
  path="${entry#*:}"
  code=$(curl -s -o "$OUT_DIR/$name.raw" -w '%{http_code}' "$BASE_URL$path")
  if [ "$code" != "200" ]; then
    echo "✗ $path أَعطى $code — الالتِقاط مُتَوَقِّف." >&2
    exit 1
  fi
  # التَّطبيع الوَحيد المَسموح.
  sed -E 's/Blazor-Server-Component-State:[A-Za-z0-9+\/=_-]+/Blazor-Server-Component-State:NORMALIZED/g' \
      "$OUT_DIR/$name.raw" > "$OUT_DIR/$name.html"
  rm -f "$OUT_DIR/$name.raw"
  echo "✓ $path → $OUT_DIR/$name.html  ($(wc -c < "$OUT_DIR/$name.html") بايت)"
done

for entry in "${SHEETS[@]}"; do
  name="${entry%%:*}"
  path="${entry#*:}"
  code=$(curl -s -o "$OUT_DIR/css/$name" -w '%{http_code}' "$BASE_URL$path")
  if [ "$code" != "200" ]; then
    echo "✗ $path أَعطى $code — الالتِقاط مُتَوَقِّف." >&2
    exit 1
  fi
  echo "✓ $path → $OUT_DIR/css/$name  ($(wc -c < "$OUT_DIR/css/$name") بايت)"
done

echo "اِلتُقِطَت ${#PAGES[@]} صَفَحات و${#SHEETS[@]} أَوراق أَنماط في $OUT_DIR"
