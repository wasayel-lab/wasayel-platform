#!/usr/bin/env bash
# ═══════════════════════════════════════════════════════════════════════
#  البُرهان الحَيّ — الهُوِيَّة البَصَرِيَّة مِلَفّ يَتَبَدَّل أَمامَ العَين
# ───────────────────────────────────────────────────────────────────────
#  ثَلاث حُزَم تُطَبَّق تَتابُعاً عَلى adwar-demo مِن **مَسار صَفحَة
#  المُبَدِّل الفِعليّ** (‏/admin/tenants/{slug}/theme/apply) بِجَلسَة
#  مُشرِف مَنصَّة مُخَوَّلَة، وبَعدَ كُلّ تَبديل يُقاس ثَلاثَة أَشياء:
#
#    ١. كُتلَة ‏:root في بَوّابَة adwar-demo تَحمِل قِيَم الحُزمَة.
#    ٢. المُتَغايِرات **ظاهِرَة في الـHTML** — الصَنف تَغَيَّرَ في
#       المُكَوِّنات الثَلاثَة، والبِنيَة كَذلِك في «المَضغوط».
#    ٣. ashare **بايتاً بِبايت** كَما هُوَ مُقابِل لَقطَة الأَساس
#       المُودَعَة قَبل وُجود طَبَقَة الرُموز أَصلاً.
#
#  و«الأَزرَق الافتِراضيّ» يَحمِل عِبئاً زائِداً: بَعدَ تَطبيقِه يَجِب
#  أَن تَعود بَوّابَة adwar-demo **مُطابِقَةً لِلَقطَة الأَساس بايتاً
#  بِبايت** وأَن تُساوي كُتلَتُها كُتلَة ashare حَرفاً — أَي أَنّ الحُزمَة
#  الأُولى تَكافُؤ صِفريّ مَقيس لا مُدَّعى.
#
#  كُلّ ذلكَ بِنَفس الـPID مِن أَوَّلِه إلى آخِرِه: لَو كُتِبَت الوَثائِق
#  مِن عَمَلِيَّة أُخرى لَأَثبَتنا أَنّ قاعِدَة بَيانات تَقبَل صُفوفاً —
#  لا أَنّ الخادِم أَبطَلَ كاشَه وأَعادَ البَثّ.
#
#  الاستِعمال:
#     scripts/prove-theme-variants.sh <admin-phone> [OUT_DIR] [BASE_URL]
# ═══════════════════════════════════════════════════════════════════════
set -euo pipefail

PHONE="${1:?يَلزَم هاتِف مُشرِف المَنصَّة}"
OUT="${2:-tests/characterization/appearance/proof/variants}"
BASE="${3:-http://localhost:5050}"
BASELINE="tests/characterization/appearance/baseline"
TENANT="adwar-demo"
JAR="$(mktemp)"
LOG="$OUT/variants-proof.txt"

mkdir -p "$OUT"
: > "$LOG"
trap 'rm -f "$JAR"' EXIT

say() { printf '%s  %s\n' "$(date -u +%Y-%m-%dT%H:%M:%S.%3NZ)" "$*" | tee -a "$LOG"; }
die() { say "$*"; exit 1; }

root_of() { curl -s "$BASE$1" | grep -o '<style id="wsl-theme">[^<]*</style>' \
            | sed 's/.*<style id="wsl-theme">//; s|</style>||'; }

# الصَفحَة كامِلَةً، مُطَبَّعَةً بِنَفس تَطبيع أَداة الالتِقاط، ومَنزوعَةً
# مِنها كُتلَة الرُموز — أَي الطَرَف المُقارَن نَفسُه الَّذي تَستَعمِلُه
# بَوّابَة القَبول. لا تَطبيع ثانٍ ولا قاعِدَة نَزع أُخرى.
stripped_of() {
  curl -s "$BASE$1" \
    | sed -E 's/Blazor-Server-Component-State:[A-Za-z0-9+\/=_-]+/Blazor-Server-Component-State:NORMALIZED/g' \
    | perl -0pe 's{[ \t]*<style id="wsl-theme">.*?</style>\r?\n\r?\n}{}s'
}

pid_of_5050() { netstat -ano | grep ':5050 .*LISTENING' | head -1 | awk '{print $NF}'; }

# عَدّ ظُهور صَنف بِعَينِه في صَفحَة — الحُجَّة أَنّ المُتَغايِر **يُرى
# في الوَسم** لا أَنَّه مَحفوظ في قاعِدَة بَيانات.
count_in() { curl -s "$BASE$1" | grep -o "$2" | wc -l | tr -d ' '; }

# ─── ٠ ────────────────────────────────────────────────────────────────
PID_BEFORE="$(pid_of_5050)"
say "[pid] الخادِم قَبل البُرهان = $PID_BEFORE"

root_of "/$TENANT" > "$OUT/00.$TENANT.root.before.css"
root_of /ashare    > "$OUT/00.ashare.root.before.css"
say "[before] $TENANT primary=$(grep -o 'wsl-color-primary:[^;]*' "$OUT/00.$TENANT.root.before.css")"
say "[before] ashare     primary=$(grep -o 'wsl-color-primary:[^;]*' "$OUT/00.ashare.root.before.css")"

# ─── ١ السالِب الأَوَّل: الصَفحَة بِلا جَلسَة ─────────────────────────
ANON="$(curl -s "$BASE/admin/tenants/$TENANT/theme")"
printf '%s' "$ANON" > "$OUT/01.switcher.anonymous.html"
if printf '%s' "$ANON" | grep -q 'name="preset"'; then
  die "[guard] ✗ صَفحَة المُبَدِّل تَعرِض نَموذَج التَطبيق بِلا جَلسَة — البَوّابَة مَكسورَة."
fi
say "[guard] بِلا جَلسَة: لا نَموذَج تَطبيق واحِد في الصَفحَة. ✓ ($(printf '%s' "$ANON" | grep -c 'مُشرِف المَنصَّة' || true) إشارَة إلى بَوّابَة المُشرِف)"

ANON_POST=$(curl -s -o /dev/null -w '%{http_code}' -X POST \
  --data-urlencode "preset=akhdar_alwaha" "$BASE/admin/tenants/$TENANT/theme/apply")
[ "$ANON_POST" = "403" ] \
  && say "[guard] نُقطَة التَطبيق بِلا جَلسَة: HTTP 403. ✓" \
  || die "[guard] ✗ نُقطَة التَطبيق بِلا جَلسَة أَعطَت $ANON_POST لا 403."

# ─── ٢ الجَلسَة المُخَوَّلَة ────────────────────────────────────────────
curl -s -c "$JAR" -b "$JAR" -o /dev/null \
     -d "phone=$PHONE" -d "code=123456" "$BASE/studio/auth/verify"
say "[auth] جَلسَة مُشرِف المَنصَّة لِـ$PHONE — كوكي: $(grep -c acommerce.studio "$JAR" || true) سَطر"

curl -s -b "$JAR" "$BASE/admin/tenants/$TENANT/theme" > "$OUT/02.switcher.authorized.html"
FORMS=$(grep -o 'name="preset"' "$OUT/02.switcher.authorized.html" | wc -l | tr -d ' ')
[ "$FORMS" = "3" ] \
  && say "[switcher] الصَفحَة بِجَلسَة مُخَوَّلَة: ٣ نَماذِج تَطبيق. ✓" \
  || die "[switcher] ✗ عَدَد نَماذِج التَطبيق = $FORMS لا 3."
for p in azraq_iftiradi layl_ramliy akhdar_alwaha; do
  grep -q "value=\"$p\"" "$OUT/02.switcher.authorized.html" \
    || die "[switcher] ✗ الحُزمَة «$p» غائِبَة عَن الصَفحَة."
done
say "[switcher] الحُزَم الثَلاث مَعروضَة بِبِطاقاتِها. ✓"

# ─── ٣ السالِب الثاني: حُزمَة لا تُوجَد ───────────────────────────────
BAD=$(curl -s -o /dev/null -w '%{redirect_url}' -b "$JAR" \
  --data-urlencode "preset=layl_azraq" "$BASE/admin/tenants/$TENANT/theme/apply")
case "$BAD" in
  *err=*) say "[negative] حُزمَة مَجهولَة «layl_azraq» ← إعادَة تَوجيه بِـerr. ✓" ;;
  *)      die "[negative] ✗ حُزمَة مَجهولَة لَم تُرَدّ: $BAD" ;;
esac
root_of "/$TENANT" > "$OUT/03.$TENANT.root.after-negative.css"
cmp -s "$OUT/00.$TENANT.root.before.css" "$OUT/03.$TENANT.root.after-negative.css" \
  && say "[negative] ولا بايت تَغَيَّرَ في الصَفحَة. ✓" \
  || die "[negative] ✗ الصَفحَة تَغَيَّرَت رَغمَ رَفض الحُزمَة."

# ─── ٤ التَبديلات الثَلاث ─────────────────────────────────────────────
#
# التَرتيب مَقصود: اللَيل أَوَّلاً (أَبعَد ما يَكون عَن الحاضِر)، ثُمَّ
# الأَزرَق الافتِراضيّ (العَودَة إلى الصِفر — وهو الفَحص الأَقسى)، ثُمَّ
# الواحَة (الحالَة الَّتي تَبقى لِلصَباح).
step=4
for PRESET in layl_ramliy azraq_iftiradi akhdar_alwaha; do
  say "─── تَبديل: $PRESET ────────────────────────────────────────────"

  # نِداء واحِد لِلتَطبيق — لا اثنان. تَكرارُه كانَ سَيُطَبِّق الحُزمَة
  # مَرَّتَين ويَجعَل عَدَد القَرارات في اللوغ ضِعف الحَقيقَة.
  RESULT=$(curl -s -o /dev/null -w '%{http_code} %{redirect_url}' -b "$JAR" \
    --data-urlencode "preset=$PRESET" "$BASE/admin/tenants/$TENANT/theme/apply")
  CODE="${RESULT%% *}"
  LOCA="${RESULT#* }"
  case "$LOCA" in
    *saved=*) say "[$PRESET] HTTP $CODE ← $(printf '%s' "$LOCA" | sed 's/.*saved=//')" ;;
    *)        die "[$PRESET] ✗ التَطبيق فَشِلَ: $LOCA" ;;
  esac

  root_of "/$TENANT" > "$OUT/$step.$TENANT.root.$PRESET.css"
  say "[$PRESET] :root ← $(grep -o 'wsl-color-bg:[^;]*'      "$OUT/$step.$TENANT.root.$PRESET.css") · \
$(grep -o 'wsl-color-text:[^;]*'   "$OUT/$step.$TENANT.root.$PRESET.css") · \
$(grep -o 'wsl-radius-md:[^;]*'    "$OUT/$step.$TENANT.root.$PRESET.css") · \
$(grep -o 'wsl-density:[^;]*'      "$OUT/$step.$TENANT.root.$PRESET.css")"

  # المُتَغايِرات في الوَسم — المُكَوِّنات الثَلاثَة.
  CARDS=$(curl -s "$BASE/$TENANT" | grep -o 'class="acm-role-landing-cards[^"]*"' | head -1)
  HEADER=$(curl -s "$BASE/$TENANT/explore" | grep -o 'class="acm-v2-topnav-wrap[^"]*"' | head -1)
  CARD=$(curl -s "$BASE/$TENANT/explore" | grep -o '<article class="ac-space[^"]*"' | head -1)
  say "[$PRESET] بِطاقَة الدَور  : $CARDS"
  say "[$PRESET] الترويسَة       : $HEADER"
  say "[$PRESET] بِطاقَة الإعلان : $CARD"

  # الفَرق البِنيَويّ: وَصف بِطاقَة الدَور لا يُصَيَّر في «المَضغوط».
  DESCS=$(curl -s "$BASE/$TENANT" | grep -o 'acm-role-landing-card-icon' | wc -l | tr -d ' ')
  BODY=$(curl -s "$BASE/$TENANT")
  SPANS=$(printf '%s' "$BODY" | grep -o '<div><strong>[^<]*</strong><span>' | wc -l | tr -d ' ')
  say "[$PRESET] بِطاقات الأَدوار = $DESCS · مِنها بِوَصف مُصَيَّر = $SPANS"

  curl -s "$BASE/$TENANT"         > "$OUT/$step.$TENANT.portal.$PRESET.html"
  curl -s "$BASE/$TENANT/explore" > "$OUT/$step.$TENANT.explore.$PRESET.html"

  # ─── سَلامَة ashare بَعدَ كُلّ تَبديل ─────────────────────────────
  root_of /ashare > "$OUT/$step.ashare.root.$PRESET.css"
  cmp -s "$OUT/00.ashare.root.before.css" "$OUT/$step.ashare.root.$PRESET.css" \
    || die "[$PRESET] ✗ كُتلَة ashare تَحَرَّكَت — العَزل مَكسور."
  stripped_of /ashare > "$OUT/$step.ashare.stripped.$PRESET.html"
  cmp -s "$BASELINE/ashare-portal.html" "$OUT/$step.ashare.stripped.$PRESET.html" \
    || die "[$PRESET] ✗ ashare انحَرَفَ عَن لَقطَة الأَساس."
  say "[$PRESET] ashare بايتاً بِبايت = لَقطَة ما قَبل الرُموز. ✓"

  # ─── العِبء الزائِد لِلحُزمَة الافتِراضيَّة ─────────────────────────
  if [ "$PRESET" = "azraq_iftiradi" ]; then
    cmp -s "$OUT/$step.ashare.root.$PRESET.css" "$OUT/$step.$TENANT.root.$PRESET.css" \
      && say "[$PRESET] كُتلَة $TENANT = كُتلَة ashare حَرفاً — عادَ إلى الصِفر. ✓" \
      || die "[$PRESET] ✗ كُتلَة $TENANT لا تُساوي الافتِراضيّ."
    stripped_of "/$TENANT" > "$OUT/$step.$TENANT.stripped.$PRESET.html"
    cmp -s "$BASELINE/adwar-demo-portal.html" "$OUT/$step.$TENANT.stripped.$PRESET.html" \
      && say "[$PRESET] بَوّابَة $TENANT بايتاً بِبايت = لَقطَة الأَساس. ✓ (تَكافُؤ صِفريّ مَقيس)" \
      || die "[$PRESET] ✗ بَوّابَة $TENANT انحَرَفَت عَن لَقطَة الأَساس."
  fi

  step=$((step + 1))
done

# ─── ٧ الحالَة الباقِيَة لِلصَباح ──────────────────────────────────────
say "─── الحالَة النِهائيَّة ─────────────────────────────────────────────"
FINAL_TENANT="$(root_of "/$TENANT")"
FINAL_ASHARE="$(root_of /ashare)"
[ "$FINAL_TENANT" != "$FINAL_ASHARE" ] \
  && say "[side-by-side] $TENANT و ashare بِهُوِيَّتَين مُختَلِفَتَين مِن نَفس المَصدَر. ✓" \
  || die "[side-by-side] ✗ المَتجَرانِ بِنَفس الكُتلَة."
say "[final] $TENANT = $(printf '%s' "$FINAL_TENANT" | grep -o 'wsl-color-primary:[^;]*') · \
$(printf '%s' "$FINAL_TENANT" | grep -o 'wsl-color-bg:[^;]*')"
say "[final] ashare     = $(printf '%s' "$FINAL_ASHARE" | grep -o 'wsl-color-primary:[^;]*') · \
$(printf '%s' "$FINAL_ASHARE" | grep -o 'wsl-color-bg:[^;]*')"

# ─── ٨ ────────────────────────────────────────────────────────────────
PID_AFTER="$(pid_of_5050)"
say "[pid] الخادِم بَعد البُرهان = $PID_AFTER"
[ "$PID_BEFORE" = "$PID_AFTER" ] \
  && say "[pid] نَفس العَمَلِيَّة — ثَلاثَة تَبديلات بِلا إعادَة تَشغيل ولا بِناء. ✓" \
  || die "[pid] ✗ الـPID تَغَيَّرَ — البُرهان باطِل."

say "البُرهان الحَيّ لِلمُتَغايِرات والحُزَم: مُكتَمِل."
