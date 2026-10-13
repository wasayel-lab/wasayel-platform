#!/usr/bin/env bash
# ═══════════════════════════════════════════════════════════════════════
#  rewalk-launch-journey.sh — رِحلَةُ الإطلاقِ مِن صَفحَةٍ أولى، بِـ`curl` وَحدَه
# ═══════════════════════════════════════════════════════════════════════
#
#  **الغَرَض**: يَمشي رِحلَةَ المُنتَجِ بَعدَ التَحَوُّلِ (‏ADR-032 §٢‑أ) عَلى
#  خادِمٍ حَيّ، ويَطبَعُ لِكُلِّ خُطوَةٍ الطَلَبَ ورَمزَه والنَصَّ الَّذي بَرهَنَها
#  — والنَصُّ يُقرَأُ مِن القامُوسِ بِمِفتاحِه (‏`ar.json`) لا مِن ذاكِرَةِ
#  السكربت، فَتَبديلُ صِياغَةٍ لا يُحمِرُّه ولا يُعميه:
#
#    رائِدُ أَعمالٍ يَبني مَتجَراً مُتَعَدِّدَ البائِعينَ مِن الاستوديو
#    ← بائِعٌ يُسَجِّلُ ويُسَكِّنُ نَفسَه `vendor` ويَنشُر
#    ← مُشتَرٍ يُسَجِّلُ ويُضيفُ لِلسَلَّةِ ويَطلُبُ نَقداً (والبِطاقَةُ لا تُعرَض)
#    ← البائِعُ يُشعَرُ ويَقبَلُ ويَرى العُنوانَ ويُوصِلُ إلى «تَسليم»
#      (وزِرُّ الشَحنِ مَخفِيٌّ بِلا رَمزِ OTO)
#    ← مُشرِفُ المَنَصَّةِ يَضبِطُ باقَةَ المَتجَرِ ويَظهَرُ سَطرُها في الاستوديو
#    ← المالِكُ يَمنَحُ نَفسَه إدارَةَ مَتجَرِه (عَدَدُ الأَفعالِ يُطبَع)
#    ← الزائِرُ بِلا جَلسَةٍ يَرى رابِطَ الاستِكشاف.
#
#  **والجِدارُ يُقال ولا يُلتَفُّ عَلَيه صامِتاً**: التَحليلُ يَحتاجُ مِفتاحَ
#  وَكيلٍ (‏`docs/DEPLOY.md` §٤·ب). الخُطوَةُ ١ تَمشي المَسارَ الحَقيقيَّ حَتّى
#  التَحليلِ وتَطبَعُ ما رَدَّه المُزَوِّدُ؛ فَإن اكتَمَلَ بُنِيَ المَتجَرُ مِنه،
#  وإلّا بُنِيَ مِن **المَسارِ المُوَثَّقِ لِلتَطوير** — جَلسَةُ
#  `IncubatorSampleSeeder` (‏`INCUBATOR_SAMPLE_SEED=1`) المَملوكَةُ لِصاحِبِ
#  `PLATFORM_ADMIN_PHONE` — ويُقالُ أَيُّهُما في السَطر.
#  ⚠ والبَذّارُ يَسبِقُ مَنحَ المُشرِفِ في `Program.cs`: عَلى قاعِدَةٍ بارِدَةٍ لا
#  مُستَخدِمَ استوديو يَومَئِذٍ فَلا جَلسَة — والإقلاعُ الثاني يَبذُرُها (مَقيسٌ
#  ‏2026-10-13، `docs/RELEASE-READINESS.md`).
#
#  **الاستِعمال**:
#      ADMIN_PHONE=<PLATFORM_ADMIN_PHONE لِلخادِم> ./scripts/rewalk-launch-journey.sh
#      BASE=http://localhost:5055 ADMIN_PHONE=… MARK=134501 ./scripts/rewalk-launch-journey.sh
#  ‏`MARK` (سِتَّةُ أَرقام) يَصنَعُ هَواتِفَ الجَولَةِ وسلاجَ مَتجَرِها، فَتُعادُ
#  الجَولَةُ عَلى القاعِدَةِ نَفسِها بِلا تَصادُم. **ولا يُشَغَّلُ عَلى الإنتاج**:
#  يَكتُبُ مُستَخدِمينَ ومَتجَراً وإعلاناً وصَفقَة، والدُخولُ فيه بِرَمزِ المُحاكي
#  (‏Development وَحدَه).
#
#  **القاعِدَة ١٠**: كُلُّ فَحصٍ يُعَدّ، ويُطبَعُ العَدَدُ في آخِرِه، والصِفرُ
#  يُحمِر — «لا فَشَل» مِن سكربتٍ لَم يَفحَص شَيئاً لا يُمَيَّزُ عَن العَمى.
#
#  **شَرطُ الاستِخراج** (القاعِدَة ١): `post`/`get`/`dec` هُنا أَخَواتُ
#  دَوالِّ `walk-after-the-key.sh` لا نُسخَتُها بِحَرفِها (هُنا تُرسِلُ
#  الاستِمارَةُ نَوعَ مُحتَواها دائِماً كَالمُتَصَفِّح). مُستَهلِكانِ لا ثَلاثَة؛
#  فَإن احتاجَها سكربتٌ ثالِثٌ استُخرِجَت إلى `scripts/journey-common.sh`.
# ═══════════════════════════════════════════════════════════════════════
set -uo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
BASE="${BASE:-http://localhost:5055}"
ADMIN_PHONE="${ADMIN_PHONE:-}"
MARK="${MARK:-$(date +%H%M%S)}"
DEV_CODE="${DEV_CODE:-123456}"     # ‏`MockSmsChannel.FixedCode` — Development وَحدَه
T="${T:-25}"                       # مُهلَةُ كُلِّ نِداء (ثانِيَة)
ANALYZE_TRIES="${ANALYZE_TRIES:-15}"   # ‏15 × 2ث سَقفاً لِلتَحليل

AR="$ROOT/libs/core/ACommerce.Platform.I18n/Locales/ar.json"
CATALOG="$ROOT/libs/templates/ACommerce.Templates.Customer.Marketplace/Services/Deals/DealPatternCatalog.cs"
SAMPLE_SEEDER="$ROOT/apps/V1.App/Seed/IncubatorSampleSeeder.cs"

WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

CHECKED=0
FAILED=0
STEP=""

# ─── الطَبعُ والعَدّ ────────────────────────────────────────────────────
say()   { printf '\n\033[1m%s\033[0m\n' "$*"; }
info()  { printf '   · %s\n' "$*"; }
block() { printf '   \033[33m⊘ %s\033[0m\n' "$*"; }
# check <1|0> <ما فُحِص> — يُعَدُّ فَحصاً في الحالَين، والفَشَلُ يُعَدُّ في عَدّادِه أَيضاً فَلا يُبتلَع.
check() {
    CHECKED=$((CHECKED + 1))
    if [ "$1" = 1 ]; then printf '   \033[32m✓\033[0m %s\n' "$2"
    else FAILED=$((FAILED + 1)); printf '   \033[31m✗\033[0m %s\n' "$2"; fi
}
# خُطوَةٌ تَتوقَّفُ عَلَيها ما بَعدَها — يُقالُ أَينَ تَوَقَّفَت الرِحلَة.
die() {
    printf '\n\033[31m✗ تَوَقَّفَت الرِحلَةُ عِندَ «%s»: %s\033[0m\n' "$STEP" "$*" >&2
    printf 'فُحِصَ %s · فَشِلَ %s\n' "$CHECKED" "$((FAILED + 1))" >&2
    exit 1
}

# ─── أَدَواتُ الطَلَب ───────────────────────────────────────────────────
# ‏POST يَضَعُ في `CODE` و`LOC`. **ونَوعُ المُحتَوى يُرسَلُ دائِماً** كَما
# يُرسِلُه المُتَصَفِّحُ لِأَيِّ استِمارَةٍ ولَو بِلا حَقل: `curl -X POST` بِلا
# جِسمٍ يُرسِلُ طَلَباً بِلا `Content-Type`، و`/advance` يَقرَأُ `req.Form`
# فَيَرُدُّ ‏500 — أَثَرُ الأَداةِ لا المُنتَج (مَقيسٌ ‏2026-10-13).
post() { # post <jar|-> <path> [args…] — args تُمَرَّرُ إلى curl (‏--data-urlencode أَو -F)؛ `-` بِلا جَلسَة
    local jar="$1" path="$2"; shift 2
    local h; h="$(mktemp -p "$WORK")"
    local body=(-d '') cookies=()
    [ "$#" -gt 0 ] && body=("$@")
    [ "$jar" = - ] || cookies=(-c "$jar" -b "$jar")
    CODE="$(curl -s --max-time "$T" -o /dev/null -D "$h" "${cookies[@]}" \
        -X POST "$BASE$path" "${body[@]}" -w '%{http_code}')" || CODE=000
    LOC="$(grep -i '^location:' "$h" | tr -d '\r' | sed 's/^[Ll]ocation: //')"
}
get() { # get <jar|-> <path> <out> — `-` بِلا جَلسَة
    local jar="$1" path="$2" out="$3"
    if [ "$jar" = - ]; then
        CODE="$(curl -s --max-time "$T" -o "$out" "$BASE$path" -w '%{http_code}')" || CODE=000
    else
        CODE="$(curl -s --max-time "$T" -o "$out" -c "$jar" -b "$jar" "$BASE$path" -w '%{http_code}')" || CODE=000
    fi
}
# نَصُّ الصَفحَةِ مَفكوكَ التَرميز: Razor يَكتُبُ مُخرَجَ التَعبيرِ كِياناتٍ
# (‏`&#x627;`…)، فَبَحثٌ خامٌ عَن نَصٍّ عَرَبِيٍّ يُعطي صِفراً كاذِباً.
dec() { perl -CSD -pe 's/&#x([0-9A-Fa-f]+);/chr(hex($1))/ge; s/&#(\d+);/chr($1)/ge; s/&quot;/"/g; s/&amp;/&/g' "$1"; }
# ‏`grep` بِلا `-q` عَمداً: تَحتَ `pipefail` يَخرُجُ `-q` عِندَ أَوَّلِ مُطابَقَةٍ فَيَموتُ `perl` بِـSIGPIPE
# فَيَفشَلُ الأُنبوبُ والنَصُّ مَوجود — أَحمَرُ كاذِبٌ مَقيسٌ في أَوَّلِ تَشغيل (‏2026-10-13).
has() { dec "$1" | grep -F -- "$2" >/dev/null; }      # has <file> <نَصّ>
count() { grep -oF -- "$2" "$1" | wc -l | tr -d ' '; } # count <file> <نَصّ خام>
# النَصُّ مِن القامُوسِ بِمِفتاحِه — ومِفتاحٌ غائِبٌ يُوقِفُ الرِحلَة لا يُطابِقُ الفَراغ.
ar() {
    local v; v="$(jq -r --arg k "$1" '.[$k] // empty' "$AR")"
    [ -n "$v" ] || die "مِفتاحُ القامُوسِ «$1» غائِب في ar.json"
    printf '%s' "$v"
}
# تَسمِيَةُ مَرحَلَةِ الصَفقَةِ مِن مَصدَرِها (‏`DealPatternCatalog.DefaultLabelsAr`).
# ‏مِن كُتلَةِ التَسمِياتِ وَحدَها: الجَدوَلُ الَّذي قَبلَها (‏`DefaultActors`) يُفَهرِسُ بِالمَفاتيحِ نَفسِها،
# فَبَحثٌ في المِلَفِّ كُلِّه يُعيدُ «counterparty» مَعَ «تَسليم» — أَحمَرُ مَقيسٌ في أَوَّلِ تَشغيل.
stage_label() {
    local v; v="$(awk '/DefaultLabelsAr/{f=1} f && /};/{exit} f' "$CATALOG" \
        | grep -o "\[DealStage\.$1\] *= \"[^\"]*\"" | sed 's/.*= "//; s/"$//')"
    [ -n "$v" ] || die "تَسمِيَةُ المَرحَلَة $1 غائِبَة في DealPatternCatalog.cs"
    printf '%s' "$v"
}
# دُخولُ عُضوٍ في مَتجَرٍ بِالهاتِف: إصدارٌ ثُمَّ استِهلاك — `phone_login` في
# `capture-appearance.sh` بِشَكلِه.
store_login() { # store_login <jar> <slug> <phone>
    post "$1" "/$2/auth/phone/login" --data-urlencode "phone=$3"
    check "$([ "$CODE" = 302 ] && [[ "$LOC" == *stage=verify* ]] && echo 1 || echo 0)" \
        "POST /$2/auth/phone/login → $CODE ${LOC}"
    post "$1" "/$2/auth/phone/verify" --data-urlencode "phone=$3" --data-urlencode "code=$DEV_CODE"
    check "$([ "$CODE" = 302 ] && [[ "$LOC" != *err=* ]] && echo 1 || echo 0)" \
        "POST /$2/auth/phone/verify → $CODE ${LOC}"
}

[ -n "$ADMIN_PHONE" ] || { echo "✗ ADMIN_PHONE غائِب — ضَعه = PLATFORM_ADMIN_PHONE لِلخادِم (‏DEPLOY.md §٢·ب)." >&2; exit 2; }
[[ "$MARK" =~ ^[0-9]{6}$ ]] || { echo "✗ MARK سِتَّةُ أَرقامٍ لا «$MARK»." >&2; exit 2; }
command -v jq >/dev/null || { echo "✗ jq غائِب — بِه يُقرَأُ القامُوس." >&2; exit 2; }

SLUG="rewalk-$MARK"
P_ENT="05${MARK}00"; P_VEN="05${MARK}01"; P_BUY="05${MARK}02"
J_ENT="$WORK/ent"; J_ADM="$WORK/adm"; J_VEN="$WORK/ven"; J_BUY="$WORK/buy"
ADDR="الرِياض — حَيُّ الفَحصِ $MARK"
F="$WORK/page.html"

printf 'الخادِم: %s · المَتجَر: %s · التاريخ: %s\n' "$BASE" "$SLUG" "$(date -u +%FT%TZ)"

# ═══ ٠) الخادِمُ يُجيبُ والقاعِدَةُ مَعَه ═══════════════════════════════
STEP="٠ — الجاهِزِيَّة"
say "٠ · /health/ready"
get - /health/ready "$F"
check "$([ "$CODE" = 200 ] && grep -q '"ready":true' "$F" && echo 1 || echo 0)" "GET /health/ready → $CODE $(cat "$F")"
[ "$CODE" = 200 ] || die "الخادِمُ لا يُجيب"

# ═══ ١) رائِدُ أَعمالٍ جَديد — المَسارُ الحَقيقيُّ حَتّى التَحليل ═════════
STEP="١ — /studio/begin حَتّى التَحليل"
say "١ · رائِدُ أَعمالٍ جَديد: فِكرَة ← دُخول ← شُروط ← تَحليل"
post "$J_ENT" /studio/begin --data-urlencode "prompt=مَتجَرٌ يَجمَعُ بائِعي التُمورِ والعَسَلِ في مَوقِعٍ واحِد — جَولَةُ $MARK"
check "$([ "$CODE" = 302 ] && [ "$LOC" = /studio/auth ] && echo 1 || echo 0)" "POST /studio/begin → $CODE $LOC"
post "$J_ENT" /studio/auth/login --data-urlencode "phone=$P_ENT"
check "$([ "$CODE" = 302 ] && [[ "$LOC" == *stage=verify* ]] && echo 1 || echo 0)" "POST /studio/auth/login → $CODE $LOC"
get "$J_ENT" "/studio/auth?stage=verify&method=phone&phone=$P_ENT" "$F"
check "$([ "$CODE" = 200 ] && has "$F" "$(ar studio.auth.dev_hint)" && echo 1 || echo 0)" \
    "GET /studio/auth?stage=verify → $CODE · «$(ar studio.auth.dev_hint)»"
post "$J_ENT" /studio/auth/verify --data-urlencode method=phone --data-urlencode "phone=$P_ENT" --data-urlencode "code=$DEV_CODE"
check "$([ "$CODE" = 302 ] && [[ "$LOC" == /studio/consent* ]] && echo 1 || echo 0)" \
    "POST /studio/auth/verify → $CODE $LOC  (‏ConsentPolicy.AcceptedBy في الاستِئناف: لا قَبولَ بَعد)"

# ‏ذَهابُ الشُروطِ وإيابُها: المُسنَدُ نَفسُه يَقرَؤُه بابُ الصَفحَةِ والاستِئناف.
get "$J_ENT" "/studio/consent?returnUrl=/studio" "$F"
check "$([ "$CODE" = 200 ] && [ "$(count "$F" 'action="/studio/consent/accept"')" = 1 ] \
        && has "$F" "$(ar studio.consent.version)" && echo 1 || echo 0)" \
    "GET /studio/consent → $CODE · الاستِمارَةُ مَرسومَة (HasConsentedAsync = false) · «$(ar studio.consent.version) …»"
post "$J_ENT" /studio/consent/accept --data-urlencode returnUrl=/studio
check "$([ "$CODE" = 302 ] && [[ "$LOC" == /studio/s/* ]] && echo 1 || echo 0)" \
    "POST /studio/consent/accept → $CODE $LOC  (المُطالَبَةُ استُؤنِفَت)"
ENT_SID="${LOC##*/}"
get "$J_ENT" "/studio/consent?returnUrl=/studio" "$F"
check "$([ "$CODE" = 200 ] && [ "$(count "$F" 'action="/studio/consent/accept"')" = 0 ] \
        && grep -q "location.replace('/studio')" "$F" && echo 1 || echo 0)" \
    "GET /studio/consent → $CODE · بِلا استِمارَة (HasConsentedAsync = true)"

ANALYSIS=""
for _ in $(seq 1 "$ANALYZE_TRIES"); do
    get "$J_ENT" "/studio/s/$ENT_SID" "$F"
    if has "$F" "$(ar studio.study.failed_title)"; then ANALYSIS=failed; break; fi
    if grep -q "action=\"/studio/s/$ENT_SID/build\"" "$F"; then ANALYSIS=done; break; fi
    sleep 2
done
case "$ANALYSIS" in
  done)   check 1 "GET /studio/s/$ENT_SID → التَحليلُ اكتَمَل، واستِمارَةُ البِناءِ مَرسومَة" ;;
  failed) # الرِسالَةُ بِشَكلِ `AgentErrorText.Where`: «تَسميَة (عُنوان) رَمز: جِسم» — يُطبَعُ صَدرُها لا الجِسم.
          WHERE="$(dec "$F" | grep -oE '[A-Za-z0-9_-]+ \(https?://[^)]*\) [0-9]{3}' | head -1)"
          check 1 "GET /studio/s/$ENT_SID → «$(ar studio.study.failed_title)» — ${WHERE:-بِلا تَسميَة}"
          block "الجِدارُ الأَوَّل: مِفتاحُ وَكيلِ التَحليلِ غائِب (‏DEPLOY.md §٤·ب) — يُبنى المَتجَرُ مِن المَسارِ المُوَثَّقِ لِلتَطوير" ;;
  *)      check 0 "GET /studio/s/$ENT_SID → لا اكتِمالَ ولا فَشَلَ خِلالَ $((ANALYZE_TRIES * 2)) ثانِيَة" ;;
esac

# ═══ ٢) بِناءُ المَتجَر ═══════════════════════════════════════════════
STEP="٢ — بِناءُ المَتجَر"
say "٢ · بِناءُ المَتجَرِ مِن الاستوديو"
if [ "$ANALYSIS" = done ]; then
    BUILD_JAR="$J_ENT"; SID="$ENT_SID"; info "المَصدَر: جَلسَةُ الخُطوَة ١ نَفسُها (تَحليلٌ حَقيقيّ)"
else
    BUILD_JAR="$J_ADM"
    SID="$(grep -o 'Guid.Parse("[0-9a-f-]*")' "$SAMPLE_SEEDER" | head -1 | sed 's/.*("//; s/")//')"
    info "المَصدَر: المَسارُ المُوَثَّقُ لِلتَطوير — جَلسَةُ IncubatorSampleSeeder ($SID) لِحِسابِ $ADMIN_PHONE"
    post "$J_ADM" /studio/auth/login --data-urlencode "phone=$ADMIN_PHONE"
    check "$([ "$CODE" = 302 ] && echo 1 || echo 0)" "POST /studio/auth/login (المُشرِف) → $CODE $LOC"
    post "$J_ADM" /studio/auth/verify --data-urlencode method=phone --data-urlencode "phone=$ADMIN_PHONE" --data-urlencode "code=$DEV_CODE"
    check "$([ "$CODE" = 302 ] && [[ "$LOC" != *err=* ]] && echo 1 || echo 0)" "POST /studio/auth/verify (المُشرِف) → $CODE $LOC"
fi
get "$BUILD_JAR" "/studio/s/$SID" "$F"
if grep -q "action=\"/studio/s/$SID/build\"" "$F"; then
    check 1 "GET /studio/s/$SID → $CODE · استِمارَةُ البِناءِ مَرسومَة"
    post "$BUILD_JAR" "/studio/s/$SID/build" --data-urlencode "slug=$SLUG" \
        --data-urlencode "name=سوقُ الجَولَة $MARK" --data-urlencode "color=#2563eb" \
        --data-urlencode "tagline=مَتجَرٌ يَجمَعُ بائِعين" --data-urlencode "city=الرِياض"
    check "$([ "$CODE" = 302 ] && [ "$LOC" = "/studio/apps/$SLUG?built=1" ] && echo 1 || echo 0)" \
        "POST /studio/s/$SID/build → $CODE $LOC"
    [ "$LOC" = "/studio/apps/$SLUG?built=1" ] || die "لَم يُبنَ المَتجَر"
else
    # ‏الجَلسَةُ تَبني مَتجَراً واحِداً بِالشاشَة: بَعدَه تَرسُمُ `StudioStudy` «افتَح التَطبيق»
    # (`FindBySourceAnalysisAsync`) بَدَلَ الاستِمارَة. فَإعادَةُ الجَولَةِ تَمشي عَلى ذلكَ المَتجَرِ
    # ولا تَبني ثانِياً بِطَلَبٍ لا تَعرِضُه الشاشَة.
    BUILT="$(grep -o 'href="/studio/apps/[a-z0-9_-]*"' "$F" | head -1 | sed 's#.*/studio/apps/##; s/"$//')"
    [ -n "$BUILT" ] \
        || die "لا استِمارَةَ بِناءٍ ولا مَتجَرَ مَبنِيّاً في /studio/s/$SID ($CODE) — بِلا مِفتاحٍ يَلزَمُ INCUBATOR_SAMPLE_SEED=1 وإقلاعٌ ثانٍ (البَذّارُ يَسبِقُ مَنحَ المُشرِف)"
    SLUG="$BUILT"
    check 1 "GET /studio/s/$SID → $CODE · مَبنِيٌّ مِنها سَلَفاً: /studio/apps/$SLUG — تَمشي الجَولَةُ عَلَيه"
fi

# ═══ ٣) الزائِرُ بِلا جَلسَة ══════════════════════════════════════════
STEP="٣ — الزائِر"
say "٣ · الزائِرُ عَلى /$SLUG"
BROWSE="$(ar roles.landing.browse)"
get - "/$SLUG" "$F"
check "$([ "$CODE" = 200 ] && has "$F" "<a href=\"/$SLUG/explore\">$BROWSE</a>" && echo 1 || echo 0)" \
    "GET /$SLUG (بِلا جَلسَة) → $CODE · رابِطُ /$SLUG/explore «$BROWSE»"
get - "/$SLUG/explore" "$F"
check "$([ "$CODE" = 200 ] && echo 1 || echo 0)" "GET /$SLUG/explore (بِلا جَلسَة) → $CODE"

# ═══ ٤) بائِعٌ يُسَجِّلُ ويُسَكِّنُ نَفسَه ويَنشُر ════════════════════
STEP="٤ — البائِع"
say "٤ · بائِعٌ: تَسجيل ← «تاجِر» ذاتِيّاً ← نَشر"
store_login "$J_VEN" "$SLUG" "$P_VEN"
get "$J_VEN" "/$SLUG/me/role" "$F"
check "$([ "$CODE" = 200 ] && grep -q 'name="role" value="vendor"' "$F" && echo 1 || echo 0)" \
    "GET /$SLUG/me/role → $CODE · خِيارُ vendor ذاتِيُّ المَنح"
post "$J_VEN" "/$SLUG/me/role/save" --data-urlencode role=vendor
check "$([ "$CODE" = 302 ] && [[ "$LOC" != *err=* ]] && echo 1 || echo 0)" "POST /$SLUG/me/role/save role=vendor → $CODE $LOC"
if [[ "$LOC" == */me/role/onboarding ]]; then
    get "$J_VEN" "/$SLUG/me/role/onboarding" "$F"
    # الحُقولُ المَطلوبَةُ تُقرَأُ مِن الاستِمارَةِ نَفسِها لا تُكتَبُ ثابِتَة، ويُطبَعُ عَدَدُها وأَسماؤُها.
    # ‏(‏2026-10-13: كانَ السَطرُ يَطبَعُ `${#ARGS[@]}` — عَدَدَ وَسائِطِ curl، اثنانِ لِكُلِّ حَقل — فَقالَ
    # «2» لِحَقلٍ واحِد (`business_name` في `vendor.role.json`). العَدَدُ يُعَدُّ بِالأَسماءِ لا بِالوَسائِط.)
    FIELDS=(); ARGS=()
    while read -r n; do FIELDS+=("$n"); ARGS+=(--data-urlencode "$n=بائِعُ الجَولَة $MARK"); done \
        < <(grep -o '<input[^>]*name="role_vendor_attr_[a-z_]*"[^>]*required' "$F" | grep -o 'name="[^"]*"' | sed 's/name="//; s/"$//')
    post "$J_VEN" "/$SLUG/me/role/onboarding/save" "${ARGS[@]}"
    check "$([ "$CODE" = 302 ] && [[ "$LOC" != *err=* ]] && echo 1 || echo 0)" \
        "POST /$SLUG/me/role/onboarding/save (${#FIELDS[@]} مِن الاستِمارَة: ${FIELDS[*]:-—}) → $CODE $LOC"
fi
post "$J_VEN" "/$SLUG/terms/accept"
check "$([ "$CODE" = 302 ] && echo 1 || echo 0)" "POST /$SLUG/terms/accept → $CODE $LOC"
get "$J_VEN" "/$SLUG/create-listing" "$F"
CAT="$(grep -o '<input type="hidden" name="category" value="[^"]*"' "$F" | head -1 | sed 's/.*value="//; s/"$//')"
check "$([ "$CODE" = 200 ] && [ -n "$CAT" ] && echo 1 || echo 0)" "GET /$SLUG/create-listing → $CODE · الفِئَة «$CAT»"
post "$J_VEN" "/$SLUG/listings/create" -F "title=تَمرُ سُكَّري — إعلانُ فَحصٍ $MARK" \
    -F "description=إعلانُ فَحصٍ آليّ في جَولَةِ الجاهِزِيَّة" -F "category=$CAT" -F price=100 \
    -F "city=الرِياض" -F "district=العُلَيّا" -F as=
LID="${LOC##*/listings/}"
check "$([ "$CODE" = 302 ] && [[ "$LID" =~ ^[0-9a-f-]{36}$ ]] && echo 1 || echo 0)" "POST /$SLUG/listings/create → $CODE $LOC"
[[ "$LID" =~ ^[0-9a-f-]{36}$ ]] || die "لَم يُنشَر الإعلان"

# ═══ ٥) مُشتَرٍ: سَلَّة ← دَفعٌ نَقدِيّ، والبِطاقَةُ لا تُعرَض ═══════════
STEP="٥ — المُشتَري"
say "٥ · مُشتَرٍ: تَسجيل ← «أَضِف لِلسَلَّة» ← نَقداً"
store_login "$J_BUY" "$SLUG" "$P_BUY"
post "$J_BUY" "/$SLUG/me/role/save" --data-urlencode role=customer
check "$([ "$CODE" = 302 ] && echo 1 || echo 0)" "POST /$SLUG/me/role/save role=customer → $CODE $LOC"
CART="$(ar listings.detail.cta_cart)"
get "$J_BUY" "/$SLUG/listings/$LID" "$F"
# ‏FLGAP-04 بِحَرفِه (‏ADR-032 §٦‑ج الصَفّ 2): صَفحَةُ إعلانِ بائِعٍ في مَتجَرٍ بَناهُ المَصنَع، بِكوكي عُضو.
check "$([ "$CODE" = 200 ] && [ "$(count "$F" "action=\"/$SLUG/listings/$LID/cart/add\"")" = 1 ] && has "$F" "$CART" && echo 1 || echo 0)" \
    "GET /$SLUG/listings/$LID (عُضو) → $CODE · استِمارَةٌ إلى cart/add و«$CART» (FLGAP-04)"
post "$J_BUY" "/$SLUG/listings/$LID/cart/add" --data-urlencode qty=1
check "$([ "$CODE" = 302 ] && [ "$LOC" = "/$SLUG/cart" ] && echo 1 || echo 0)" "POST /$SLUG/listings/$LID/cart/add → $CODE $LOC"
Q="name=$(jq -rn --arg v "مُشتَري الجَولَة" '$v|@uri')&phone=$P_BUY&addr=$(jq -rn --arg v "$ADDR" '$v|@uri')"
get "$J_BUY" "/$SLUG/checkout?step=2&$Q" "$F"
check "$([ "$CODE" = 200 ] && [ "$(count "$F" 'name="pay" value="cash"')" = 1 ] && has "$F" "$(ar checkout.payment.cash)" && echo 1 || echo 0)" \
    "GET /$SLUG/checkout?step=2 → $CODE · «$(ar checkout.payment.cash)» مَعروض"
check "$([ "$(count "$F" 'name="pay" value="card"')" = 0 ] && echo 1 || echo 0)" \
    "GET /$SLUG/checkout?step=2 → البِطاقَةُ غَيرُ مَعروضَة (name=\"pay\" value=\"card\" = $(count "$F" 'name="pay" value="card"'))"
post "$J_BUY" "/$SLUG/checkout/submit" --data-urlencode "name=مُشتَري الجَولَة" --data-urlencode "phone=$P_BUY" \
    --data-urlencode "addr=$ADDR" --data-urlencode pay=card
check "$([ "$CODE" = 302 ] && [[ "$LOC" == *err=pay_card_unavailable* ]] && echo 1 || echo 0)" \
    "POST /$SLUG/checkout/submit pay=card → $CODE · err=pay_card_unavailable (النُقطَةُ تَرفُضُ ما لا تَعرِضُه الشاشَة)"
post "$J_BUY" "/$SLUG/checkout/submit" --data-urlencode "name=مُشتَري الجَولَة" --data-urlencode "phone=$P_BUY" \
    --data-urlencode "addr=$ADDR" --data-urlencode pay=cash
DID="${LOC##*/deals/}"
check "$([ "$CODE" = 302 ] && [[ "$DID" =~ ^[0-9a-f-]{36}$ ]] && echo 1 || echo 0)" "POST /$SLUG/checkout/submit pay=cash → $CODE $LOC"
[[ "$DID" =~ ^[0-9a-f-]{36}$ ]] || die "لَم تُنشَأ صَفقَة"

# ═══ ٦) البائِعُ يُشعَرُ ويَقبَلُ ويَرى العُنوان ═════════════════════════
STEP="٦ — قَبولُ البائِع"
say "٦ · البائِع: إشعار ← قَبول ← العُنوان"
get "$J_VEN" "/$SLUG/notifications" "$F"
check "$([ "$CODE" = 200 ] && has "$F" "$(ar notifications.deal_new.title)" && grep -q "/$SLUG/deals/$DID" "$F" && echo 1 || echo 0)" \
    "GET /$SLUG/notifications (البائِع) → $CODE · «$(ar notifications.deal_new.title)» ورابِطُ الصَفقَة"
get "$J_VEN" "/$SLUG/deals/$DID" "$F"
check "$([ "$CODE" = 200 ] && grep -q "action=\"/$SLUG/deals/$DID/accept\"" "$F" && has "$F" "$ADDR" && echo 1 || echo 0)" \
    "GET /$SLUG/deals/$DID (البائِع، قَبلَ القَبول) → $CODE · استِمارَةُ القَبولِ والعُنوانُ «$ADDR»"
post "$J_VEN" "/$SLUG/deals/$DID/accept"
check "$([ "$CODE" = 302 ] && [ "$LOC" = "/$SLUG/deals/$DID" ] && echo 1 || echo 0)" "POST /$SLUG/deals/$DID/accept → $CODE $LOC"
get "$J_BUY" "/$SLUG/notifications" "$F"
check "$([ "$CODE" = 200 ] && has "$F" "$(ar notifications.deal_accepted.title)" && echo 1 || echo 0)" \
    "GET /$SLUG/notifications (المُشتَري) → $CODE · «$(ar notifications.deal_accepted.title)»"

# ═══ ٧) إلى «تَسليم» — وزِرُّ الشَحنِ مَخفِيٌّ بِلا رَمزِ OTO ═══════════
STEP="٧ — التَسليم"
say "٧ · البائِعُ يُحَرِّكُ الصَفقَةَ إلى «$(stage_label Delivered)»"
SHIP_A="$(ar deals.detail.ship_submit)"; SHIP_B="$(ar deals.detail.ship_resume)"
NEXT_PREFIX="$(ar deals.detail.next_step)"; AFTER_DELIVERED="$(stage_label Reviewed)"
REACHED=0
for i in 1 2 3 4 5 6; do
    get "$J_VEN" "/$SLUG/deals/$DID" "$F"
    LINE_NEXT="$(dec "$F" | sed 's/<[^>]*>/\n/g' | grep -m1 -F -- "$NEXT_PREFIX")"
    NEXT="${LINE_NEXT#*"$NEXT_PREFIX"}"; NEXT="${NEXT#"${NEXT%%[![:space:]]*}"}"; NEXT="${NEXT%"${NEXT##*[![:space:]]}"}"
    SHIPF="$(count "$F" "/deals/$DID/ship\"")"
    check "$([ "$SHIPF" = 0 ] && ! has "$F" "$SHIP_A" && ! has "$F" "$SHIP_B" && has "$F" "$ADDR" && echo 1 || echo 0)" \
        "GET /$SLUG/deals/$DID (البائِع) → $CODE · التالِيَة «${NEXT:-؟}» · استِمارَةُ /ship = $SHIPF وبِلا «$SHIP_A» · العُنوانُ ظاهِر"
    if [ "$NEXT" = "$AFTER_DELIVERED" ]; then REACHED=1; break; fi
    grep -q "action=\"/$SLUG/deals/$DID/advance\"" "$F" || break
    post "$J_VEN" "/$SLUG/deals/$DID/advance"
    check "$([ "$CODE" = 302 ] && [ "$LOC" = "/$SLUG/deals/$DID" ] && echo 1 || echo 0)" "POST /$SLUG/deals/$DID/advance [$i] → $CODE $LOC"
done
check "$REACHED" "الصَفقَةُ عِندَ «$(stage_label Delivered)» (التالِيَةُ «$AFTER_DELIVERED»)"
post - "/$SLUG/shipping/oto/order-status" -H 'Content-Type: application/json' -d '{}'
check "$([ "$CODE" = 404 ] && echo 1 || echo 0)" \
    "POST /$SLUG/shipping/oto/order-status (مَتجَرٌ بِلا ناقِل) → $CODE (‏DEPLOY.md §٢·ط: 404 بِلا قِراءَة)"
block "الجِدارُ الثاني: رَمزُ OTO غائِب — زِرُّ البوليصَةِ مَخفِيّ والشَحنُ يَدَوِيّ (‏DEPLOY.md §٢·ط)"
block "الجِدارُ الثالِث: مِفتاحُ البَوّابَةِ ومِفتاحُ الخِزانَةِ غائِبان — البِطاقَةُ لا تُعرَض (‏DEPLOY.md §٢·ح)"

# ═══ ٨) مُشرِفُ المَنَصَّةِ يَضبِطُ الباقَة، وسَطرُها في الاستوديو ════════
STEP="٨ — الباقَة"
say "٨ · /admin ← باقَةُ المَتجَر ← سَطرُها في الاستوديو"
if [ ! -s "$J_ADM" ]; then
    post "$J_ADM" /studio/auth/login --data-urlencode "phone=$ADMIN_PHONE"
    post "$J_ADM" /studio/auth/verify --data-urlencode method=phone --data-urlencode "phone=$ADMIN_PHONE" --data-urlencode "code=$DEV_CODE"
    check "$([ "$CODE" = 302 ] && [[ "$LOC" != *err=* ]] && echo 1 || echo 0)" "POST /studio/auth/verify (المُشرِف) → $CODE $LOC"
fi
get "$J_ADM" /admin "$F"
check "$([ "$CODE" = 200 ] && grep -q "href=\"/admin/tenants/$SLUG/plan\"" "$F" && echo 1 || echo 0)" \
    "GET /admin → $CODE · زِرُّ الباقَةِ عَلى بِطاقَةِ /$SLUG"
get "$J_ADM" "/admin/tenants/$SLUG/plan" "$F"
PLAN_LABEL="$(dec "$F" | grep -o '<option value="manual"[^>]*>[^<]*' | head -1 | sed 's/.*>//; s/[[:space:]]*$//')"
STARTS="$(date -u +%F)"; EXPIRES="$(date -u -d '+30 days' +%F)"
check "$([ "$CODE" = 200 ] && [ -n "$PLAN_LABEL" ] && echo 1 || echo 0)" "GET /admin/tenants/$SLUG/plan → $CODE · «$PLAN_LABEL»"
# السِعرُ قيمَةُ الاستِمارَةِ الافتِراضِيَّةُ (0) لا رَقَمٌ يُختَرَع (القاعِدَة ١٦)، والمُدَّةُ ‏30 يَوماً لِلفَحصِ وَحدَه.
post "$J_ADM" "/admin/tenants/$SLUG/plan/save" --data-urlencode plan=manual --data-urlencode "starts=$STARTS" \
    --data-urlencode "expires=$EXPIRES" --data-urlencode grace=14 --data-urlencode price=0
check "$([ "$CODE" = 302 ] && [[ "$LOC" == *saved=1* ]] && echo 1 || echo 0)" "POST /admin/tenants/$SLUG/plan/save → $CODE $LOC"
LINE="$(ar studio.plan.active_line)"; LINE="${LINE/\{0\}/$PLAN_LABEL}"; LINE="${LINE/\{1\}/$EXPIRES}"
get "$J_ADM" /studio "$F"
check "$([ "$CODE" = 200 ] && has "$F" "$LINE" && echo 1 || echo 0)" "GET /studio (المالِك) → $CODE · «$LINE»"

# ═══ ٩) المالِكُ يَمنَحُ نَفسَه إدارَةَ مَتجَرِه — عَدَدُ الأَفعال ════════
STEP="٩ — أَوَّلُ مُشرِف"
say "٩ · /studio/apps/$SLUG ← /$SLUG/login ← عَودَة ← «$(ar admin.tenant_users.grant)»"
ACTS=0
get "$J_ADM" "/studio/apps/$SLUG" "$F"
check "$([ "$CODE" = 200 ] && grep -q "href=\"/$SLUG\"" "$F" && grep -q "href=\"/admin/tenants/$SLUG/users?from=studio\"" "$F" && echo 1 || echo 0)" \
    "GET /studio/apps/$SLUG → $CODE · رابِطا «افتَح المَتجَر» و«المُستَخدِمون»"
get "$J_ADM" "/$SLUG" "$F"; ACTS=$((ACTS + 1))
check "$([ "$CODE" = 200 ] && grep -q "href=\"/$SLUG/login\"" "$F" && echo 1 || echo 0)" "[فِعل $ACTS] GET /$SLUG → $CODE · رابِطُ /$SLUG/login"
get "$J_ADM" "/$SLUG/login" "$F"; ACTS=$((ACTS + 1))
check "$([ "$CODE" = 200 ] && echo 1 || echo 0)" "[فِعل $ACTS] GET /$SLUG/login → $CODE"
store_login "$J_ADM" "$SLUG" "$ADMIN_PHONE"; ACTS=$((ACTS + 2))
info "[فِعل $((ACTS - 1))–$ACTS] الهاتِفُ ثُمَّ الرَمز → $LOC"
STUDIO_LINKS="$(get "$J_ADM" "$LOC" "$F"; count "$F" 'href="/studio')"
info "رابِطُ عَودَةٍ إلى الاستوديو مِن $LOC: $STUDIO_LINKS — فَالعَودَةُ بِزِرِّ المُتَصَفِّحِ أَو بِكِتابَةِ العُنوان، لا بِنَقرَة"
get "$J_ADM" "/admin/tenants/$SLUG/users?from=studio" "$F"; ACTS=$((ACTS + 1))
# الصَفُّ الَّذي يَحمِلُ هاتِفَ المالِكِ واستِمارَتَه — كُتلَةً كُتلَة، لا أَوَّلُ استِمارَةٍ في الصَفحَة.
OWN_ROW="$(dec "$F" | PH="$ADMIN_PHONE" perl -0777 -ne \
    'for my $b (split /class="acm-cat-row"/) { if (index($b, $ENV{PH}) >= 0 && $b =~ m{users/([0-9a-f-]{36})/(grant|revoke)-admin}) { print "$1 $2"; last } }')"
OWN_ID="${OWN_ROW%% *}"; OWN_ACT="${OWN_ROW##* }"
check "$([ "$CODE" = 200 ] && [ -n "$OWN_ROW" ] && echo 1 || echo 0)" \
    "[فِعل $ACTS] GET /admin/tenants/$SLUG/users?from=studio → $CODE · صَفُّ $ADMIN_PHONE (${OWN_ACT:-غائِب})"
[ -n "$OWN_ROW" ] || die "صَفُّ المالِكِ غائِبٌ في شاشَةِ المُستَخدِمين"
if [ "$OWN_ACT" = grant ]; then
    post "$J_ADM" "/admin/tenants/$SLUG/users/$OWN_ID/grant-admin"; ACTS=$((ACTS + 1))
    check "$([ "$CODE" = 302 ] && [[ "$LOC" == *saved=1* ]] && echo 1 || echo 0)" \
        "[فِعل $ACTS] POST …/users/$OWN_ID/grant-admin «$(ar admin.tenant_users.grant)» → $CODE $LOC"
else
    info "المالِكُ مُشرِفُ مَتجَرِه مِن جَولَةٍ سابِقَة (‏revoke-admin في صَفِّه) — لا مَنحَ ثانِياً، والعَدُّ لِلجَولَةِ الأولى"
fi
get "$J_ADM" "/$SLUG/manage" "$F"
check "$([ "$CODE" = 200 ] && has "$F" "$(ar tenant.manage.subtitle)" && echo 1 || echo 0)" \
    "GET /$SLUG/manage (جَلسَةُ المالِكِ في مَتجَرِه) → $CODE · «$(ar tenant.manage.subtitle)»"
info "أَوَّلُ مُشرِفٍ لِمَتجَرٍ مَبنِيّ: $ACTS أَفعالٍ داخِلَ المُنتَج + عَودَةٌ واحِدَةٌ خارِجَه (بِلا رابِط)"

# ═══ ١٠) الزائِرُ مَرَّةً ثانِيَة ════════════════════════════════════════
STEP="١٠ — الزائِرُ أَخيراً"
say "١٠ · الزائِرُ بِلا جَلسَة — والمَتجَرُ بِإعلانِه وباقَتِه"
get - "/$SLUG" "$F"
check "$([ "$CODE" = 200 ] && has "$F" "<a href=\"/$SLUG/explore\">$BROWSE</a>" && echo 1 || echo 0)" \
    "GET /$SLUG (بِلا جَلسَة) → $CODE · «$BROWSE»"
get - "/$SLUG/explore" "$F"
check "$([ "$CODE" = 200 ] && grep -q "href=\"/$SLUG/listings/$LID\"" "$F" && echo 1 || echo 0)" \
    "GET /$SLUG/explore (بِلا جَلسَة) → $CODE · رابِطُ الإعلان"

# ═══ الخُلاصَة — العَدَدُ يُطبَع، والصِفرُ يُحمِر (القاعِدَة ١٠) ═════════
say "الخُلاصَة"
printf 'المَتجَر: %s/%s · الصَفقَة: %s · الإعلان: %s\n' "$BASE" "$SLUG" "$DID" "$LID"
printf 'فُحِصَ %s · فَشِلَ %s\n' "$CHECKED" "$FAILED"
if [ "$CHECKED" -eq 0 ]; then echo "✗ فَحصٌ أَعمى: لَم يُفحَص شَيء." >&2; exit 1; fi
[ "$FAILED" -eq 0 ] || exit 1
exit 0
