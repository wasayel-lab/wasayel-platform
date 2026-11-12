#!/usr/bin/env bash
# ═══════════════════════════════════════════════════════════════════════
#  prove-meter.sh — الهَيكَلُ الماشي لِلعَدِّ مِن طَرَفٍ إلى طَرَف: طَلَبٌ عَبرَ HTTP ← سَطرٌ ← صَفحَةُ المُشرِف ← المُجَمِّع
# ───────────────────────────────────────────────────────────────────────
#  **لِماذا هذا المِلَفّ** (‏2026-11-12، الموجَة ١٣ W13-6·٥ — ADR-051 §١٣؛ التَصميم §13 P2–P6، القاعِدَتانِ ٩ و١٤): البُنودُ ٣ و٤ و٥ تُقاسُ
#  كُلٌّ في طَبَقَتِه (المُستَمِع، والمُجَمِّع، والقارِئ). وهذا يَقيسُها **مَعاً عَلى الثُنائِيَّينِ المَبنِيَّين**: يُقلِعُ `V1.App` عَلى قاعِدَةٍ جَديدَةٍ
#  يُنشِئُها ويَحذِفُها، ورائِدُ أَعمالٍ يُنشِئُ مَتجَراً مِن الاستوديو، وبائِعٌ يَنشُر، ومُشتَرٍ يَطلُبُ نَقداً — كُلُّه بِـcurl ومِن استِماراتِ الصَفَحاتِ
#  نَفسِها — ثُمَّ يَقرَأُ صَفحَتَي المُشرِفِ ويُشَغِّلُ `V1.Meter --once`. وبِلا بَذرِ اختِبار (`TEST_DATA_SEED` غائِب): صَفَقاتُ البَذرِ تَكتُبُ سُطورَ `_seed`،
#  وعِندَها لا يُقرَأُ «deal.create pulled 1» إلّا مُختَلِطاً بِها.
#
#  الصُفوف:
#    P2  طَلَبٌ عَبرَ HTTP ← سَطرُ `deal.create` واحِد: المَتجَرُ = سلاجُه، ورائِدُ الأَعمالِ = `Tenant.OwnerUserId`، و`writer_xid` = `xmin` صَفِّ الصَفقَة
#    P3  إعادَةُ إرسالِ السَلَّةِ نَفسِها ← تَحويلٌ، والسَطرُ ما زالَ واحِداً
#    P4  الصَفحَةُ قَبلَ أَيِّ تَشغيلٍ لِلمُجَمِّع ← 1 مَعَ «المُجَمِّعُ لَم يَعمَل بَعد»، والرابِطُ في /admin، والمَتجَرُ في الفِهرِس
#    P5  `V1.Meter --once` ← «deal.create 1» في سَطرِ السَحب، والصَفحَةُ مِن المَجموع (ذَيل 0)
#    P6  ثَلاثَةُ طَلَباتٍ والمُجَمِّعُ واقِف ← الصَفحَةُ 4 (ذَيل 3)، ثُمَّ «deal.create 3» والصَفحَةُ 4 (ذَيل 0)؛ والطَلَبُ المَجهولُ لا يُرسَمُ لَه رَقَم
#
#  **ولا يُشَغَّلُ عَلى الإنتاج**: يُنشِئُ قاعِدَةً ويَحذِفُها عَلى خادِمِ Postgres مَحَلِّيٍّ وَحدَه (المُضيفُ localhost أَو 127.0.0.1 أَو ::1، والاسمُ `wasayel_…`)،
#  والدُخولُ بِرَمزِ المُحاكي (Development وَحدَه). ولا يُطبَعُ مِن الاتِّصالِ إلّا المُضيفُ واسمُ القاعِدَة.
#  **والعَدُّ وَحدَه**: لا سِعرَ في هذا المِلَفّ ولا في ما يَقرَؤُه (القاعِدَة ١٦)؛ وقيمَةُ حَقلِ `price` في استِمارَةِ الإعلانِ بَيانُ المَتجَرِ المَطلوب لا رَقَمُ عَدّ.
#
# ─── الاستِعمال ────────────────────────────────────────────────────────
#   dotnet build PlatformV1.slnx -c Release
#   ConnectionStrings__Postgres="Host=localhost;Username=postgres;Password=…" ./scripts/prove-meter.sh 5050
#   ‏`ConnectionStrings__Postgres` خادِمٌ مَحَلِّيٌّ (قاعِدَتُه تُتَجاهَل — يُنشِئُ `DB`)، و`ADMIN_PHONE` (افتِراضُه 0500000001) هاتِفُ مُشرِفِ المَنَصَّةِ لِلإقلاع،
#   و`DB` اسمُ القاعِدَةِ الجَديدَة (افتِراضُه `wasayel_prove_meter_<المَنفَذ>_<العَلامَة>`، ويُرفَضُ إن وُجِدَت)، و`MARK` سِتَّةُ أَرقامٍ تَصنَعُ الهَواتِفَ والسلاج،
#   و`CONFIG` تَهيِئَةُ البِناء (Release)، و`KEEP=1` يُبقي القاعِدَةَ بَعدَه.
#
# ─── رُموزُ الخُروج ────────────────────────────────────────────────────
#   0 — كُلُّ صَفٍّ كَما يَجِب.   1 — صَفٌّ خالَف، أَو صِفرُ فَحص (أَداةٌ عَمياء — القاعِدَة ١٠).   2 — وَسيطٌ أَو بيئَة: مَنفَذٌ مَشغول، ثُنائِيٌّ غائِب،
#   مُضيفٌ غَيرُ مَحَلِّيّ، قاعِدَةٌ قائِمَة، أَو التَطبيقُ لَم يَجهَز.
#
#  **شَرطُ الاستِخراج** (القاعِدَة ١): `req`/`dec`/`form` هُنا أَخَواتُ `post`/`get`/`dec`/`form_dump` في `rewalk-launch-journey.sh` لا نُسخَتُها
#  (هُنا دالَّةُ طَلَبٍ واحِدَةٌ بِالفِعلِ مُعامِلاً، ووِجهَةُ التَحويلِ مِن رُؤوسِ الجَوابِ نَفسِه). مُستَهلِكانِ لا ثَلاثَة؛ والثالِثُ يَستَخرِجُها إلى
#  `scripts/journey-common.sh` كَما كُتِبَ في رَأسِ الرِحلَة.
# ═══════════════════════════════════════════════════════════════════════
set -uo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
PORT="${1:-}"
CONFIG="${CONFIG:-Release}"
ADMIN_PHONE="${ADMIN_PHONE:-0500000001}"
DEV_CODE="${DEV_CODE:-123456}"     # ‏`MockSmsChannel.FixedCode` — Development وَحدَه
MARK="${MARK:-$(date +%H%M%S)}"
T="${T:-25}"                       # مُهلَةُ كُلِّ نِداء (ثانِيَة)
KEEP="${KEEP:-0}"
AR="$ROOT/libs/core/ACommerce.Platform.I18n/Locales/ar.json"
APP_DLL="$ROOT/apps/V1.App/bin/$CONFIG/net10.0/V1.App.dll"
METER_DLL="$ROOT/apps/V1.Meter/bin/$CONFIG/net10.0/V1.Meter.dll"

usage() { echo "الاستِعمال: ConnectionStrings__Postgres=<خادِمٌ مَحَلِّيّ> $0 <port>" >&2; exit 2; }
[[ "$PORT" =~ ^[0-9]{2,5}$ ]] || usage
[[ "$MARK" =~ ^[0-9]{6}$ ]] || { echo "✗ MARK سِتَّةُ أَرقامٍ لا «$MARK»." >&2; exit 2; }
for tool in curl jq perl psql; do command -v "$tool" >/dev/null || { echo "✗ $tool غائِب." >&2; exit 2; }; done
for dll in "$APP_DLL" "$METER_DLL"; do [ -f "$dll" ] || { echo "✗ غَيرُ مَبنِيّ: $dll — dotnet build PlatformV1.slnx -c $CONFIG" >&2; exit 2; }; done

# ─── الاتِّصال: خادِمٌ مَحَلِّيٌّ وَحدَه، وقاعِدَةٌ جَديدَةٌ بِاسمٍ مَعلوم ─────────────────
CS_IN="${ConnectionStrings__Postgres:-}"
[ -n "$CS_IN" ] || usage
cs_get() { printf '%s' "$CS_IN" | tr ';' '\n' | sed -n "s/^[[:space:]]*\($1\)[[:space:]]*=[[:space:]]*//Ip" | head -1; }
PGH="$(cs_get 'Host\|Server')"; PGP="$(cs_get Port)"; PGU="$(cs_get 'Username\|User ID\|User')"; PGW="$(cs_get Password)"
PGP="${PGP:-5432}"
case "$PGH" in localhost|127.0.0.1|::1) ;; *) echo "✗ المُضيفُ «$PGH» لَيسَ مَحَلِّيّاً — هذا البُرهانُ يُنشِئُ قاعِدَةً ويَحذِفُها." >&2; exit 2;; esac
DB="${DB:-wasayel_prove_meter_${PORT}_${MARK}}"
[[ "$DB" =~ ^wasayel_[a-z0-9_]+$ ]] || { echo "✗ اسمُ القاعِدَة «$DB» لا يَبدَأُ بِـwasayel_." >&2; exit 2; }
CS="Host=$PGH;Port=$PGP;Database=$DB;Username=$PGU;Password=$PGW"
pg() { PGPASSWORD="$PGW" timeout 30 psql -h "$PGH" -p "$PGP" -U "$PGU" -X -q -At "$@"; }
q() { pg -d "$DB" -c "$1"; }

BASE="http://localhost:$PORT"
curl -s -m 2 -o /dev/null "$BASE/health" && { echo "✗ المَنفَذُ $PORT مَشغول — هذا البُرهانُ يُقلِعُ خادِمَه." >&2; exit 2; }
[ "$(pg -d postgres -c "select count(*) from pg_database where datname = '$DB'")" = 0 ] \
    || { echo "✗ القاعِدَةُ $DB قائِمَة — البُرهانُ عَلى قاعِدَةٍ جَديدَةٍ وَحدَها (DB=… لِاسمٍ آخَر)." >&2; exit 2; }

WORK="$(mktemp -d)"
APP_PID=""
cleanup() {
    if [ -n "$APP_PID" ] && kill -0 "$APP_PID" 2>/dev/null; then
        kill "$APP_PID" 2>/dev/null
        for _ in $(seq 1 20); do kill -0 "$APP_PID" 2>/dev/null || break; sleep 1; done
        kill -0 "$APP_PID" 2>/dev/null && kill -9 "$APP_PID" 2>/dev/null
    fi
    if [ "$KEEP" != 1 ]; then pg -d postgres -c "drop database if exists \"$DB\" with (force)" >/dev/null 2>&1 && echo "حُذِفَت $DB"; fi
    rm -rf "$WORK"
}
trap cleanup EXIT

CHECKED=0; FAILED=0
F="$WORK/page.html"; H="$WORK/headers"
check() { # check <1|0> <ما فُحِص>
    CHECKED=$((CHECKED + 1))
    if [ "$1" = 1 ]; then printf '   ✓ %s\n' "$2"; else FAILED=$((FAILED + 1)); printf '   ✗ %s\n' "$2"; fi
}
die() { printf '\n✗ تَوَقَّفَ البُرهان: %s\nفُحِصَ %s · فَشِلَ %s\n' "$*" "$CHECKED" "$((FAILED + 1))" >&2; exit 1; }
say() { printf '\n%s\n' "$*"; }

# ─── الطَلَب ─────────────────────────────────────────────────────────────
# req <jar|-> <GET|POST> <path> [curl args…] — الجِسمُ في $F، والرَمزُ في CODE، والوِجهَةُ (نِسبِيَّةً) في LOC. ‏POST بِلا حُقولٍ يُرسِلُ
# نَوعَ مُحتَوى الاستِمارَةِ كَالمُتَصَفِّح (`-d ''`)، وإلّا قَرَأَت النُقطَةُ `req.Form` بِلا نَوعٍ فَرَمَت.
req() {
    local jar="$1" method="$2" path="$3"; shift 3
    local cookies=() body=()
    [ "$jar" = - ] || cookies=(-c "$jar" -b "$jar")
    [ "$method" = POST ] && [ "$#" -eq 0 ] && body=(-d '')
    CODE="$(curl -s --max-time "$T" -o "$F" -D "$H" "${cookies[@]}" -X "$method" "$BASE$path" "${body[@]}" "$@" -w '%{http_code}')" || CODE=000
    LOC="$(tr -d '\r' < "$H" | sed -n 's/^[Ll]ocation: //p' | head -1)"; LOC="${LOC#"$BASE"}"
}
# النَصُّ مَفكوكُ الكِيانات — Razor يَكتُبُ العَرَبِيَّةَ كِيانات.
dec() { perl -CSD -pe 's/&#x([0-9A-Fa-f]+);/chr(hex($1))/ge; s/&#(\d+);/chr($1)/ge; s/&quot;/"/g; s/&amp;/&/g' "$1"; }
has() { dec "$1" | grep -F -- "$2" >/dev/null; }
ar() { local v; v="$(jq -r --arg k "$1" '.[$k] // empty' "$AR")"; [ -n "$v" ] || die "مِفتاحُ القامُوسِ «$1» غائِب"; printf '%s' "$v"; }
# form <file> <نَصٌّ في الاستِمارَة> — أَوَّلُ <form> يَحمِلُه: «ACTION <a>» ثُمَّ «HIDDEN n=v» و«REQ n» (مَطلوبٌ مَرئِيّ) و«VALUE n=v».
form() {
    dec "$1" | MUST="$2" perl -0777 -ne '
        while (m{(<form\b[^>]*>)(.*?)</form>}gs) {
            my ($open, $body) = ($1, $2);
            next if index($open . $body, $ENV{MUST}) < 0;
            my ($act) = $open =~ /\baction="([^"]*)"/; print "ACTION ", ($act // ""), "\n";
            while ($body =~ m{<(?:input|textarea|select)\b([^>]*)>}g) {
                my $a = $1; my ($n) = $a =~ /\bname="([^"]*)"/; next unless defined $n;
                my ($t) = $a =~ /\btype="([^"]*)"/; my ($v) = $a =~ /\bvalue="([^"]*)"/;
                if (($t // "") eq "hidden") { print "HIDDEN $n=", ($v // ""), "\n" }
                else { print "REQ $n\n" if $a =~ /\brequired\b/; print "VALUE $n=$v\n" if defined $v && length $v; }
            }
            last;
        }'
}
f_action() { printf '%s\n' "$1" | sed -n 's/^ACTION //p' | head -1; }
f_kind()   { printf '%s\n' "$1" | sed -n "s/^$2 //p"; }
f_get()    { printf '%s\n' "$1" | sed -n "s/^$2 $3=//p" | head -1; }
attr()     { grep -o "$1=\"[^\"]*\"" "$F" | sed "s/^$1=\"//; s/\"$//" | paste -sd, -; }   # attr <data-…> — قِيَمُه في الصَفحَةِ بِتَرتيبِها
unit_in()  { perl -0777 -ne 'if (/\Q'"$1"'\E(.*)/s) { print $1 =~ /data-meter-unit="deal\.create" data-entries="(\d+)"/ ? $1 : "" }' "$F"; }

SLUG="meter-$MARK"
P_ENT="05${MARK}10"; P_VEN="05${MARK}11"; P_BUY="05${MARK}12"
J_ENT="$WORK/ent"; J_VEN="$WORK/ven"; J_BUY="$WORK/buy"; J_ADM="$WORK/adm"

# ═══ ٠) قاعِدَةٌ جَديدَةٌ والتَطبيقُ المَبنِيُّ عَلَيها ═════════════════════
say "٠ · $DB عَلى $PGH ← V1.App ($CONFIG) عَلى $BASE (العَلامَة $MARK)"
pg -d postgres -c "create database \"$DB\"" || { echo "✗ تَعَذَّرَ إنشاءُ $DB." >&2; exit 2; }
(
    cd "$ROOT/apps/V1.App" || exit 2
    exec env ConnectionStrings__Postgres="$CS" ASPNETCORE_ENVIRONMENT=Development PLATFORM_ADMIN_PHONE="$ADMIN_PHONE" \
        dotnet "$APP_DLL" --urls="$BASE"
) > "$WORK/app.log" 2>&1 < /dev/null &
APP_PID=$!
ready=0
for _ in $(seq 1 240); do
    curl -s -m 2 "$BASE/health/ready" 2>/dev/null | grep -q '"ready":true' && { ready=1; break; }
    kill -0 "$APP_PID" 2>/dev/null || break
    sleep 1
done
[ "$ready" = 1 ] || { echo "✗ التَطبيقُ لَم يَجهَز:" >&2; tail -20 "$WORK/app.log" >&2; exit 2; }
BOOT="$(grep -o '\[meter\] boot.*' "$WORK/app.log" | head -1)"
check "$([ "$BOOT" = "[meter] boot generation=1 units=1 epochs_new=1" ] && echo 1 || echo 0)" "إقلاعٌ بارِد: «$BOOT» (pid $APP_PID)"
L0="$(q "select count(*) from meter.usage_entry")"; C0="$(q "select count(*) from meter.agg_cursor")"
check "$([ "$L0" = 0 ] && [ "$C0" = 0 ] && echo 1 || echo 0)" "الدَفتَرُ فارِغٌ قَبلَ أَوَّلِ طَلَب: سُطور $L0 · مُؤَشِّر $C0 (لا بَذرَ صَفَقات)"

# ═══ ١) رائِدُ أَعمالٍ يُنشِئُ مَتجَرَه مِن الاستوديو ════════════════════════
say "١ · رائِدُ الأَعمال: دُخولٌ ← مُوافَقَة ← استِمارَةُ الإنشاء ← /$SLUG"
req "$J_ENT" POST /studio/auth/login --data-urlencode "phone=$P_ENT"
check "$([ "$CODE" = 302 ] && [[ "$LOC" == *stage=verify* ]] && echo 1 || echo 0)" "POST /studio/auth/login → $CODE $LOC"
req "$J_ENT" POST /studio/auth/verify --data-urlencode method=phone --data-urlencode "phone=$P_ENT" --data-urlencode "code=$DEV_CODE"
check "$([ "$CODE" = 302 ] && [[ "$LOC" != *err=* ]] && echo 1 || echo 0)" "POST /studio/auth/verify → $CODE $LOC"
req "$J_ENT" GET /studio/new
if [ "$CODE" = 302 ] && [[ "$LOC" == /studio/consent* ]]; then
    req "$J_ENT" GET "$LOC"
    D="$(form "$F" 'consent/accept')"
    req "$J_ENT" POST "$(f_action "$D")" --data-urlencode "returnUrl=$(f_get "$D" HIDDEN returnUrl)"
    check "$([ "$CODE" = 302 ] && [ "$LOC" = /studio/new ] && echo 1 || echo 0)" "المُوافَقَةُ مِن استِمارَتِها → $CODE $LOC"
    req "$J_ENT" GET /studio/new
fi
D="$(form "$F" 'name="slug"')"; ACT="$(f_action "$D")"
[ "$CODE" = 200 ] && [ -n "$ACT" ] || die "لا استِمارَةَ إنشاءٍ في /studio/new ($CODE $LOC)"
req "$J_ENT" POST "$ACT" --data-urlencode "slug=$SLUG" --data-urlencode "name=مَتجَرُ العَدّ $MARK" \
    --data-urlencode "color=$(f_get "$D" VALUE color)" --data-urlencode "tagline="
check "$([ "$CODE" = 302 ] && [ "$LOC" = "/studio/apps/$SLUG?built=1" ] && echo 1 || echo 0)" "POST $ACT slug=$SLUG → $CODE $LOC"
[ "$LOC" = "/studio/apps/$SLUG?built=1" ] || die "لَم يُنشَأ المَتجَر"
OWNER="$(q "select data->>'OwnerUserId' from platform.mt_doc_tenant where id = '$SLUG'")"
check "$([[ "$OWNER" =~ ^[0-9a-f-]{36}$ ]] && [ "$OWNER" != 00000000-0000-0000-0000-000000000000 ] && echo 1 || echo 0)" "مالِكُ /$SLUG: $OWNER"

# ═══ ٢) بائِعٌ يَنشُر ═══════════════════════════════════════════════════
say "٢ · البائِع: دُخول ← دَورُ vendor ← الشُروط ← إعلان"
store_login() { # store_login <jar> <phone>
    req "$1" POST "/$SLUG/auth/phone/login" --data-urlencode "phone=$2"
    check "$([ "$CODE" = 302 ] && [[ "$LOC" == *stage=verify* ]] && echo 1 || echo 0)" "POST /$SLUG/auth/phone/login → $CODE $LOC"
    req "$1" POST "/$SLUG/auth/phone/verify" --data-urlencode "phone=$2" --data-urlencode "code=$DEV_CODE"
    check "$([ "$CODE" = 302 ] && [[ "$LOC" != *err=* ]] && echo 1 || echo 0)" "POST /$SLUG/auth/phone/verify → $CODE $LOC"
}
store_login "$J_VEN" "$P_VEN"
req "$J_VEN" POST "/$SLUG/me/role/save" --data-urlencode role=vendor
check "$([ "$CODE" = 302 ] && [[ "$LOC" != *err=* ]] && echo 1 || echo 0)" "POST /$SLUG/me/role/save role=vendor → $CODE $LOC"
if [[ "$LOC" == */me/role/onboarding ]]; then
    req "$J_VEN" GET "/$SLUG/me/role/onboarding"
    ARGS=()
    while read -r n; do ARGS+=(--data-urlencode "$n=بائِعُ العَدّ $MARK"); done \
        < <(grep -o '<input[^>]*name="role_vendor_attr_[a-z_]*"[^>]*required' "$F" | grep -o 'name="[^"]*"' | sed 's/name="//; s/"$//')
    req "$J_VEN" POST "/$SLUG/me/role/onboarding/save" "${ARGS[@]}"
    check "$([ "$CODE" = 302 ] && [[ "$LOC" != *err=* ]] && echo 1 || echo 0)" "POST /$SLUG/me/role/onboarding/save ($(( ${#ARGS[@]} / 2 )) حَقل) → $CODE $LOC"
fi
req "$J_VEN" POST "/$SLUG/terms/accept"
check "$([ "$CODE" = 302 ] && echo 1 || echo 0)" "POST /$SLUG/terms/accept → $CODE $LOC"
req "$J_VEN" GET "$LOC"
NEW_HREF="$(grep -o "href=\"/$SLUG/[^\"]*create-listing\"" "$F" | head -1 | sed 's/^href="//; s/"$//')"
[ -n "$NEW_HREF" ] || die "لا رابِطَ إلى create-listing حَيثُ أَعادَت الشُروط"
req "$J_VEN" GET "$NEW_HREF"
CAT="$(grep -o '<input type="hidden" name="category" value="[^"]*"' "$F" | head -1 | sed 's/.*value="//; s/"$//')"
if [ -z "$CAT" ]; then
    PICK="$(grep -o 'href="[^"]*create-listing?category=[^"]*"' "$F" | head -1 | sed 's/^href="//; s/"$//')"
    [ -n "$PICK" ] || die "لا فِئَةَ في $NEW_HREF"
    req "$J_VEN" GET "$PICK"
fi
D="$(form "$F" 'listings/create"')"; ACT="$(f_action "$D")"
[ -n "$ACT" ] || die "لا استِمارَةَ نَشرٍ في $NEW_HREF"
# ‏الحُقولُ المَطلوبَةُ مِن الاستِمارَة؛ و`price` بَيانُ الإعلانِ المَطلوبُ في استِمارَتِه (لا رَقَمَ عَدّ).
req "$J_VEN" POST "$ACT" -F "title=إعلانُ العَدّ $MARK" -F "description=إعلانٌ لِبُرهانِ العَدّ" -F "category=$(f_get "$D" HIDDEN category)" \
    -F price=1 -F "city=الرِياض" -F "district=العُلَيّا" -F "as=$(f_get "$D" HIDDEN as)"
LID="${LOC##*/listings/}"
check "$([ "$CODE" = 302 ] && [[ "$LID" =~ ^[0-9a-f-]{36}$ ]] && echo 1 || echo 0)" "POST $ACT → $CODE $LOC"
[[ "$LID" =~ ^[0-9a-f-]{36}$ ]] || die "لَم يُنشَر الإعلان"

# ═══ ٣) مُشتَرٍ يَطلُبُ نَقداً ═══════════════════════════════════════════
say "٣ · المُشتَري: دُخول ← دَورُ customer"
store_login "$J_BUY" "$P_BUY"
req "$J_BUY" POST "/$SLUG/me/role/save" --data-urlencode role=customer
check "$([ "$CODE" = 302 ] && echo 1 || echo 0)" "POST /$SLUG/me/role/save role=customer → $CODE $LOC"
SUBMIT_ACT=""; SUBMIT_ARGS=()
# order <n> — سَلَّةٌ ← الخُطُواتُ الثَلاث مِن استِماراتِها ← الإرسال؛ ويَحفَظُ استِمارَةَ الإرسالِ لِـP3.
order() {
    req "$J_BUY" POST "/$SLUG/listings/$LID/cart/add" --data-urlencode qty=1
    [ "$CODE" = 302 ] && [ "$LOC" = "/$SLUG/cart" ] || { check 0 "الطَلَبُ $1: POST cart/add → $CODE $LOC"; return 1; }
    req "$J_BUY" GET "/$SLUG/cart"
    local pay; pay="$(grep -o "href=\"/$SLUG/[^\"]*checkout\"" "$F" | head -1 | sed 's/^href="//; s/"$//')"
    req "$J_BUY" GET "$pay"
    local d a args=() n; d="$(form "$F" 'name="from" value="1"')"; a="$(f_action "$d")"
    args=(--data-urlencode "from=$(f_get "$d" HIDDEN from)")
    for n in $(f_kind "$d" REQ); do
        case "$n" in
            name)  args+=(--data-urlencode "name=مُشتَري العَدّ") ;;
            phone) args+=(--data-urlencode "phone=$P_BUY") ;;
            addr)  args+=(--data-urlencode "addr=الرِياض — حَيُّ العَدّ $MARK") ;;
            *)     check 0 "الطَلَبُ $1: حَقلٌ مَطلوبٌ لا يَعرِفُه البُرهان «$n»"; return 1 ;;
        esac
    done
    req "$J_BUY" POST "$a" "${args[@]}"
    [[ "$LOC" == *checkout\?step=2 ]] || { check 0 "الطَلَبُ $1: الخُطوَة ١ → $CODE $LOC"; return 1; }
    req "$J_BUY" GET "$LOC"
    d="$(form "$F" 'name="from" value="2"')"; a="$(f_action "$d")"; args=()
    while IFS= read -r n; do [ -n "$n" ] && args+=(--data-urlencode "$n"); done < <(f_kind "$d" HIDDEN)
    req "$J_BUY" POST "$a" "${args[@]}" --data-urlencode pay=cash
    [[ "$LOC" == *checkout\?step=3 ]] || { check 0 "الطَلَبُ $1: الخُطوَة ٢ نَقداً → $CODE $LOC"; return 1; }
    req "$J_BUY" GET "$LOC"
    d="$(form "$F" 'checkout/submit"')"; SUBMIT_ACT="$(f_action "$d")"; SUBMIT_ARGS=()
    while IFS= read -r n; do [ -n "$n" ] && SUBMIT_ARGS+=(--data-urlencode "$n"); done < <(f_kind "$d" HIDDEN)
    req "$J_BUY" POST "$SUBMIT_ACT" "${SUBMIT_ARGS[@]}"
    local did="${LOC##*/deals/}"
    check "$([ "$CODE" = 302 ] && [[ "$did" =~ ^[0-9a-f-]{36}$ ]] && echo 1 || echo 0)" "الطَلَبُ $1: POST $SUBMIT_ACT (نَقداً، حُقولُ الاستِمارَة) → $CODE $LOC"
}
entries() { q "select count(*) from meter.usage_entry where app = '$SLUG' and unit = 'deal.create'"; }
deals() { q "select count(*) from platform.mt_doc_deal where tenant_id = '$SLUG'"; }

say "P2 · طَلَبٌ عَبرَ HTTP ← سَطرٌ واحِدٌ بِمَتجَرِه ومالِكِه ومَصدَرِه"
order 1 || die "الطَلَبُ الأَوَّلُ لَم يَكتَمِل"
# ‏والسَطرُ في مُعامَلَةِ الصَفقَةِ نَفسِها (writer_xid = xmin) يَقيسُه P1 (`LiveMeterPersistenceProofTests`): هُنا يُكتَبُ صَفُّ الصَفقَةِ ثانِيَةً بَعدَ وِلادَتِها
# في الطَلَبِ نَفسِه (مَقيس ‏2026-11-12: writer_xid 528473 · xmin الصَفِّ 528475 · mt_last_modified بَعدَ occurred_at بِـ30ms) — فَالمُقارَنَةُ هُنا
# بِمَصدَرِ السَطرِ (الصَفقَةُ بِعَينِها) والمَتجَرِ والمالِك.
ROW="$(q "select app || ' · ' || entrepreneur || ' · ' || role || ' · ' || feature || ' · ' || provider || ' · ' || source_type from meter.usage_entry where app = '$SLUG'")"
ENT="$(q "select string_agg(distinct entrepreneur, ',') from meter.usage_entry where app = '$SLUG'")"
SRC="$(q "select count(*) from meter.usage_entry e join platform.mt_doc_deal d on d.id::text = e.source_id and d.tenant_id = e.app where e.app = '$SLUG'")"
check "$([ "$(entries)" = 1 ] && [ "$(deals)" = 1 ] && [ "$SRC" = 1 ] && echo 1 || echo 0)" \
    "P2 · صَفَقاتُ /$SLUG $(deals) · سُطورُ deal.create $(entries) · مَصدَرُ السَطرِ صَفقَتُه بِعَينِها $SRC"
check "$([ "${ROW%% · *}" = "$SLUG" ] && [ "$ENT" = "$OWNER" ] && echo 1 || echo 0)" \
    "P2 · السَطر: $ROW — المَتجَرُ = السلاج، ورائِدُ الأَعمالِ = Tenant.OwnerUserId ($OWNER)"

say "P3 · إعادَةُ إرسالِ السَلَّةِ نَفسِها"
req "$J_BUY" POST "$SUBMIT_ACT" "${SUBMIT_ARGS[@]}"
check "$([ "$CODE" = 302 ] && [ "$(entries)" = 1 ] && [ "$(deals)" = 1 ] && echo 1 || echo 0)" \
    "P3 · POST $SUBMIT_ACT ثانِيَةً → $CODE $LOC · صَفَقات $(deals) · سُطور $(entries)"

# ═══ المُشرِف ═══════════════════════════════════════════════════════════
req "$J_ADM" POST /studio/auth/login --data-urlencode "phone=$ADMIN_PHONE"
req "$J_ADM" POST /studio/auth/verify --data-urlencode method=phone --data-urlencode "phone=$ADMIN_PHONE" --data-urlencode "code=$DEV_CODE"
check "$([ "$CODE" = 302 ] && [[ "$LOC" != *err=* ]] && echo 1 || echo 0)" "دُخولُ مُشرِفِ المَنَصَّة → $CODE $LOC"
# store_page <جَرَّة> — صَفحَةُ المَتجَرِ وقَرائِنُها: «الحالَة · المُجَمِّع · الذَيل · المَجموع · deal.create».
store_page() {
    req "$1" GET "/admin/meter/stores/$SLUG"
    printf '%s · %s · %s · %s · %s' "$(attr data-meter-state)" "$(attr data-meter-agg)" "$(attr data-meter-tail)" "$(attr data-meter-total)" "$(unit_in data-meter-total)"
}
anonymous() {
    req - GET "/admin/meter/stores/$SLUG"
    check "$([ "$CODE" = 200 ] && ! grep -q 'data-meter-' "$F" && grep -q 'href="/studio/auth"' "$F" && echo 1 || echo 0)" \
        "$1 · GET /admin/meter/stores/$SLUG بِلا جَلسَة → $CODE · قَرائِنُ العَدّ $(grep -c 'data-meter-' "$F") · بابُ الدُخول"
}

say "P4 · الصَفحَةُ قَبلَ أَيِّ تَشغيلٍ لِلمُجَمِّع"
req "$J_ADM" GET /admin
check "$([ "$CODE" = 200 ] && grep -q 'href="/admin/meter"' "$F" && echo 1 || echo 0)" "GET /admin → $CODE · رابِطُ /admin/meter (القاعِدَة ١٢)"
req "$J_ADM" GET /admin/meter
check "$([ "$CODE" = 200 ] && [ "$(unit_in "data-meter-app=\"$SLUG\"")" = 1 ] && echo 1 || echo 0)" \
    "GET /admin/meter → $CODE · المَتاجِر «$(attr data-meter-app)» · /$SLUG deal.create $(unit_in "data-meter-app=\"$SLUG\"")"
P4="$(store_page "$J_ADM")"
check "$([ "$P4" = "data · absent ·  · 1 · 1" ] && has "$F" "$(ar admin.meter.agg_absent)" && grep -q "data-meter-entrepreneur=\"$OWNER\"" "$F" && echo 1 || echo 0)" \
    "P4 · /admin/meter/stores/$SLUG → «$P4» · «$(ar admin.meter.agg_absent)» · رائِدُ الأَعمالِ $OWNER"
anonymous P4

say "P5 · V1.Meter --once"
agg_once() { ConnectionStrings__Postgres="$CS" timeout 300 dotnet "$METER_DLL" --once > "$WORK/agg.txt" 2>&1; AGG_EXIT=$?; AGG="$(grep -o '\[meter-agg\] gen .*' "$WORK/agg.txt" | head -1)"; }
agg_once
check "$([ "$AGG_EXIT" = 0 ] && [[ "$AGG" == *"· pulled 1 (deal.create 1) ·"* ]] && echo 1 || echo 0)" "P5 · خُروج $AGG_EXIT · $AGG"
P5="$(store_page "$J_ADM")"
check "$([ "$P5" = "data · ran · 0 · 1 · 1" ] && echo 1 || echo 0)" "P5 · /admin/meter/stores/$SLUG → «$P5» (مِن المَجموع، ذَيل 0)"

say "P6 · ثَلاثَةُ طَلَباتٍ والمُجَمِّعُ واقِف"
for n in 2 3 4; do order "$n" || die "الطَلَبُ $n لَم يَكتَمِل"; done
P6A="$(store_page "$J_ADM")"
check "$([ "$(entries)" = 4 ] && [ "$P6A" = "data · ran · 3 · 4 · 4" ] && echo 1 || echo 0)" "P6 · سُطور $(entries) · قَبلَ السَحب «$P6A» (مَجموع 1 + ذَيل 3)"
agg_once
check "$([ "$AGG_EXIT" = 0 ] && [[ "$AGG" == *"· pulled 3 (deal.create 3) ·"* ]] && echo 1 || echo 0)" "P6 · خُروج $AGG_EXIT · $AGG"
P6B="$(store_page "$J_ADM")"
check "$([ "$P6B" = "data · ran · 0 · 4 · 4" ] && echo 1 || echo 0)" "P6 · بَعدَ السَحب «$P6B»"
anonymous P6

PER_UNIT="$(q "select string_agg(unit || ' ' || n || ' (_unresolved ' || u || ' · _seed ' || s || ')', ' · ') from (select unit, count(*) n, count(*) filter (where role = '_unresolved' or entrepreneur = '_unresolved') u, count(*) filter (where role = '_seed') s from meter.usage_entry group by unit) t")"
AGG_ROWS="$(q "select coalesce(string_agg(app || ' ' || unit || ' ' || entries, ' · '), '—') from meter.agg_daily")"
printf '\nالدَفتَرُ لِكُلِّ وَحدَة: %s · المَجاميع: %s\n' "${PER_UNIT:-—}" "$AGG_ROWS"
printf 'فُحِصَ %s · فَشِلَ %s\n' "$CHECKED" "$FAILED"
[ "$CHECKED" -gt 0 ] || exit 1
[ "$FAILED" = 0 ] || exit 1
exit 0
