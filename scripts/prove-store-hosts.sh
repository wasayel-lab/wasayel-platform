#!/usr/bin/env bash
# ═══════════════════════════════════════════════════════════════════════
#  بُرهانُ مُضيفِ المَتجَرِ عَلى خادِمٍ حَيّ — مَصفوفَةُ curl بِرَأسِ Host
# ───────────────────────────────────────────────────────────────────────
#  **لِماذا هذا المِلَفّ** (‏2026-11-07، ADR-050 §٩ · W13-4·C2a·٧): المَرحَلَةُ الأُولى «مَتجَرٌ واحِدٌ عَلى مُضيفِه مِن طَرَفٍ إلى طَرَف، والمَساراتُ
#  كَما هي». الاختِباراتُ تَقيسُ القَرارَ النَقِيَّ والوَسيطَينِ عَلى مُضيفٍ مُصَغَّر؛ وهذا يَقيسُ **الخادِمَ نَفسَه** بِبَذرَتِه وبِـ
#  `MultiTenancy__BaseDomain` مَضبوطاً: مَتجَرٌ عَلى `{store}.{domain}`، ومَناطِقُ المَنَصَّةِ تَعودُ إلى الجَذر، وAPI مَتجَرٍ آخَرَ لا يُبلَغ،
#  والمُضيفُ المَجهولُ ومُضيفُ الدَورِ (في هذِه المَرحَلَة) 404، والـcanonical وخَريطَةُ المَوقِعِ مِن العُنوانِ الظاهِر — والمَسارُ عَلى الجَذرِ كَما كان.
#
#  **لا يَكتُبُ شَيئاً** (طَلَباتُ GET، وPOST واحِدٌ إلى مَنطِقَةِ مَنَصَّةٍ يُرَدُّ 404 قَبلَ أَيِّ مُعالِج). ويَحتاجُ خادِماً شُغِّلَ بِـ
#  `MultiTenancy__BaseDomain=<domain>` — و`localhost` مَحَلِّيّاً: Chrome وcurl يَحُلّانِ `*.localhost` إلى 127.0.0.1، والرَأسُ `Host` هُنا يُغني عَن DNS.
#
# ─── الاستِعمال ────────────────────────────────────────────────────────
#   MultiTenancy__BaseDomain=localhost dotnet run --project apps/V1.App --urls=http://localhost:5050   # خادِمٌ بِبَذرَةِ التَطوير
#   ./scripts/prove-store-hosts.sh                                     # STORE=ashare OTHER=ejar DOMAIN=localhost PORT=5050
#   STORE=theme-demo OTHER=ashare ./scripts/prove-store-hosts.sh
#   DIRECT=1 SCHEME=https PORT= DOMAIN=<R> STORE=<مَتجَر> OTHER=<آخَر> ./scripts/prove-store-hosts.sh   # بَعدَ النَشر: كُلُّ مُضيفٍ بِـDNS لا بِرَأسِ Host
#   STORE=<مَتجَر> MANAGER_PHONE=<هاتِفُ مُديرِه> ./scripts/prove-store-hosts.sh   # ومَعَه المَجموعَةُ ٦: مُديرُ المَتجَرِ يَدخُلُ عَلى مُضيفِه ويَفتَحُ بِطاقَةَ /manage
#   STORE=<مَتجَر> ROLE=vendor ROLE_PHONE=<هاتِف> ./scripts/prove-store-hosts.sh    # ومَعَه المَجموعَةُ ١٠: جَلسَةُ الدَورِ لا تُغادِرُ مُضيفَها (‏C2b-R1، تَكتُبُ دُخولاً)
#   ALLOWED=1 ./scripts/prove-store-hosts.sh   # خادِمٌ شُغِّلَ بِـAllowedHosts="<domain>;*.<domain>" — ومَعَه المَجموعَةُ ١٤ (C2c، ‏ADR-050 §١٥)
#   ADMIN_PHONE=<PLATFORM_ADMIN_PHONE لِلخادِم> ./scripts/prove-store-hosts.sh   # ومَعَه صُفوفُ الاستوديو في المَجموعَةِ ١٣ (C2c-R1، تَكتُبُ دُخولاً وتُسقِطُ جَلَساتِ المُشرِف)
#
#  ‏(‏2026-11-07، مُراجَعَةُ C2a الجَولَةُ الأُولى · C2a-P-01 — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه) **المَجموعَةُ ٦ تَكتُبُ دُخولاً** (تَحَدّي الرَمزِ وجَلسَتُه) فَلا تَجري إلّا
#  بِـ`MANAGER_PHONE` — هاتِفُ مُستَخدِمٍ لَه `tenant.manage` في `STORE` — وبِرَمزِ المُحاكي `DEV_CODE` (Development وَحدَه)؛ وبِلا هاتِفٍ تُطبَعُ «لَم تُفحَص» ولا تُعَدّ.
#
# ─── رُموزُ الخُروج ────────────────────────────────────────────────────
#   0 — كُلُّ صَفٍّ كَما يَجِب.   1 — صَفٌّ واحِدٌ خالَف، أَو صِفرُ صَفٍّ مَفحوص (أَداةٌ عَمياء — القاعِدَة ١٠).   2 — الخادِمُ لا يُجيب.
# ═══════════════════════════════════════════════════════════════════════
set -uo pipefail

DOMAIN="${DOMAIN:-localhost}"
PORT="${PORT-5050}"               # فارِغٌ = المَنفَذُ الافتِراضيُّ لِلمُخَطَّط (بَعدَ النَشر)
SCHEME="${SCHEME:-http}"
STORE="${STORE:-ashare}"
OTHER="${OTHER:-ejar}"
DIRECT="${DIRECT:-0}"             # 1 = يُطلَبُ كُلُّ مُضيفٍ بِاسمِه (DNS)، لا 127.0.0.1 بِرَأسِ Host
MANAGER_PHONE="${MANAGER_PHONE:-}" # هاتِفُ مُديرٍ لِـSTORE — يُشَغِّلُ المَجموعَةَ ٦ (دُخولٌ بِرَمزِ المُحاكي)
DEV_CODE="${DEV_CODE:-123456}"
ROLE="${ROLE-vendor}"              # دَورٌ في STORE مِن الكَتالوج — مُضيفُه STORE-ROLE (W13-4·C2b·5)؛ فارِغاً لا تُفحَصُ المَجموعَةُ ٩
ROLE_ABSENT="${ROLE_ABSENT:-driver}" # دَورٌ مِن الكَتالوجِ لا يَحمِلُه STORE — مُضيفُه 404
ROLE_PHONE="${ROLE_PHONE:-}"       # هاتِفٌ يَدخُلُ بِدَورِ ROLE (بِرَمزِ المُحاكي) — يُشَغِّلُ المَجموعَةَ ١٠؛ يَكتُبُ دُخولاً ومُستَخدِماً إن لَم يَكُن (C2b-R1)
ADMIN_PHONE="${ADMIN_PHONE:-}"     # PLATFORM_ADMIN_PHONE لِلخادِم — يُشَغِّلُ صُفوفَ الاستوديو في المَجموعَةِ ١٣ (C2c-R1)؛ يَكتُبُ دُخولاً ويَرفَعُ إصدارَ جَلسَتِه
GUEST_PHONE="${GUEST_PHONE:-}"     # هاتِفٌ لا يُديرُ STORE — يُشَغِّلُ المَجموعَةَ ٢٣ (C2z-R1)؛ يَكتُبُ دُخولاً ومُستَخدِماً إن لَم يَكُن، وحِصَّتُه غَيرُ حِصَّةِ ROLE_PHONE
JAR=""                            # جَرَّةُ كَعكاتٍ تُرسَلُ مَعَ الصُفوفِ وَقتَ المَجموعَةِ ٦ وَحدَها
REF=""                            # رَأسُ Referer يُرسَلُ مَعَ الصُفوفِ وَقتَ المَجموعَةِ ٧ وَحدَها
ORIGIN=""                         # رَأسُ Origin يُرسَلُ مَعَ الصُفوفِ وَقتَ المَجموعَةِ ١٢ وَحدَها (W13-4·C2c·1)
ALLOWED="${ALLOWED:-0}"           # 1 = الخادِمُ بِـAllowedHosts="$DOMAIN;*.$DOMAIN" — يُشَغِّلُ المَجموعَةَ ١٤ (W13-4·C2c·3)
hp() { if [ -n "$PORT" ]; then printf '%s:%s' "$1" "$PORT"; else printf '%s' "$1"; fi; }
ADDR="$SCHEME://$(hp 127.0.0.1)"
S="$(hp "$STORE.$DOMAIN")"        # مُضيفُ المَتجَر
R="$(hp "$DOMAIN")"               # الجَذر
TMP="$(mktemp -d)"; trap 'rm -rf "$TMP"' EXIT
url() { if [ "$DIRECT" = 1 ]; then printf '%s://%s%s' "$SCHEME" "$1" "$2"; else printf '%s%s' "$ADDR" "$2"; fi; }

curl -s -m 10 -o /dev/null "$(url "$R" /health)" || { echo "✗ الخادِمُ لا يُجيبُ عَلى $(url "$R" /health)" >&2; exit 2; }

checked=0; failed=0
# row <method> <host> <path> <status> [<location> | -] [<body must contain>...] [!<body must not contain>...]
row() {
  local method="$1" host="$2" path="$3" want="$4" loc="${5:--}"; shift 5 || shift $#
  local out="$TMP/body"
  local verb=(-X "$method"); [ "$method" = HEAD ] && verb=(-I)   # ‏`-X HEAD` يَنتَظِرُ جِسماً لا يَأتي
  local args=(-s -m 20 -o "$out" -w '%{http_code}|%{redirect_url}' "${verb[@]}" -H "Host: $host")
  local jar=(); [ -n "$JAR" ] && jar=(-b "$JAR"); args+=("${jar[@]}")
  [ -n "$REF" ] && args+=(-H "Referer: $REF")
  [ -n "$ORIGIN" ] && args+=(-H "Origin: $ORIGIN")
  [ "$method" = POST ] && args+=(-H 'Content-Type: application/x-www-form-urlencoded' --data '')
  # ‏(‏2026-11-07، مُراجَعَةُ C2a الجَولَةُ الثانِيَة · C2a-P2-01 — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه) الوِجهَةُ مِن رُؤوسِ الجَوابِ نَفسِه (`-D`) لا مِن طَلَبٍ ثانٍ: الطَلَبُ
  # الثاني كانَ بِلا جِسمِ الـPOST ولا Referer، فَلا يَقيسُ وِجهَةَ ما أُرسِل. ووِجهَةٌ تَنتَهي بِـ* تُقارَنُ بادِئَةً (لِما بَعدَ ? مِن رَمزِ رَفضٍ يَختَلِفُ بِقَناةِ المَتجَر).
  local got; got="$(curl -D "$TMP/headers" "${args[@]}" "$(url "$host" "$path")")" || got="000|"
  local code="${got%%|*}" redirect="${got#*|}" why=""
  [ "$code" = "$want" ] || why="الرَمز $code لا $want"
  if [ -z "$why" ] && [ "$loc" != "-" ]; then
    # curl يَحُلُّ الوِجهَةَ نِسبَةً إلى 127.0.0.1 — والمُقارَنَةُ بِالوِجهَةِ الكامِلَةِ مِن رَأسِ Location
    redirect="$(tr -d '\r' < "$TMP/headers" | sed -n 's/^[Ll]ocation: //p')"
    if [ "${loc: -1}" = "*" ]; then
      [ "${redirect#"${loc%\*}"}" != "$redirect" ] || why="الوِجهَة «$redirect» لا تَبدَأُ بِـ«${loc%\*}»"
    else
      [ "$redirect" = "$loc" ] || why="الوِجهَة «$redirect» لا «$loc»"
    fi
  fi
  for needle in "$@"; do
    [ -n "$why" ] && break
    if [ "${needle:0:1}" = "!" ]; then
      grep -qF -- "${needle:1}" "$out" && why="الجِسمُ يَحمِلُ «${needle:1}»"
    else
      grep -qF -- "$needle" "$out" || why="الجِسمُ بِلا «$needle»"
    fi
  done
  checked=$((checked + 1))
  if [ -z "$why" ]; then printf '  ✓ %-4s %-34s %-52s → %s\n' "$method" "$host" "$path" "$code"
  else failed=$((failed + 1)); printf '  ✗ %-4s %-34s %-52s → %s\n' "$method" "$host" "$path" "$why"; fi
}

printf 'الخادِم: %s · النِطاق: %s · المَتجَر: %s · الآخَر: %s\n' "$ADDR" "$DOMAIN" "$STORE" "$OTHER"

echo "١) المَتجَرُ عَلى مُضيفِه — والـcanonical مِن العُنوانِ الظاهِر"
row GET  "$S" "/"                       200 - "<link rel=\"canonical\" href=\"$SCHEME://$S/\" />" '"hostMode":true' "\"slug\":\"$STORE\""
row GET  "$S" "/explore"                200 - "<link rel=\"canonical\" href=\"$SCHEME://$S/explore\" />" "<meta property=\"og:url\" content=\"$SCHEME://$S/explore\" />"
row GET  "$S" "/$STORE/explore"         200 - "<link rel=\"canonical\" href=\"$SCHEME://$S/explore\" />"
row GET  "$S" "/support"                200 - "<link rel=\"canonical\" href=\"$SCHEME://$S/support\" />"
row GET  "$S" "/$OTHER/listings"        404

echo "٢) جُذورُ البِنيَةِ التَحتِيَّةِ تُخدَمُ كَما هي"
row GET  "$S" "/sw.js"                  200 - "self.addEventListener"
row GET  "$S" "/offline.html"           200
row GET  "$S" "/robots.txt"             200 - "Sitemap: $SCHEME://$S/sitemap.xml"
row GET  "$S" "/sitemap.xml"            200 - "<loc>$SCHEME://$S/</loc>" "<loc>$SCHEME://$S/explore</loc>" "!/$OTHER" "!/$STORE/"
row GET  "$S" "/_content/ACommerce.Templates.Customer.Marketplace/css/app.css" 200
row GET  "$S" "/api/$STORE/manifest.json" 200 - '"id":"/"' '"start_url":"/"' '"scope":"/"' '"url":"/explore"'   # W13-4·C2b·2 — كانَ /$STORE/
row GET  "$R" "/api/$STORE/manifest.json" 200 - "\"scope\":\"/$STORE/\""
row GET  "$S" "/api/push/vapid-key"     200

echo "٣) مَناطِقُ المَنَصَّةِ تَعودُ إلى الجَذر، والكِتابَةُ إلَيها لا تُقبَل"
row GET  "$S" "/studio?from=store"      302 "$SCHEME://$R/studio?from=store"
row GET  "$S" "/admin"                  302 "$SCHEME://$R/admin"
row GET  "$S" "/pricing"                302 "$SCHEME://$R/pricing"
row HEAD "$S" "/privacy"                302 "$SCHEME://$R/privacy"
row POST "$S" "/studio/apps/create"     404
# ‏(‏2026-11-07، مُراجَعَةُ C2a الجَولَةُ الأُولى · SEC-C2a-2) المَسارُ المُرَمَّزُ يُحَوَّلُ بِوِجهَةٍ مُرَمَّزَة — كانَ 500 («م» حَرفاً في Location)، و%3F يَنقَلِبُ استِعلاماً
row GET  "$S" "/studio/%D9%85"          302 "$SCHEME://$R/studio/%D9%85"
row GET  "$S" "/admin/%0d%0aSet-Cookie:x=1" 302 "$SCHEME://$R/admin/%0D%0ASet-Cookie:x=1"
row GET  "$S" "/studio/%3F%3Fx"         302 "$SCHEME://$R/studio/%3F%3Fx"

echo "٤) ما لا يَخُصُّ المَتجَرَ لا يُبلَغُ مِن مُضيفِه"
row GET  "$S" "/api/$OTHER/unread-counts" 404
row GET  "$S" "/api/v1/deals"           404
# ‏(‏2026-11-07، مُراجَعَةُ C2a الجَولَةُ الثانِيَة · SEC-C2a-R2-1) سَطحُ مَتجَرٍ اسمُه push لا يُخدَمُ عَلى مُضيفِ غَيرِه (كانَ 200 بِبَيانِه)، ولا مُضيفَ لِـpush وv1
# (كانَ v1.R/api/v1/deals يَبلُغُ API المَنَصَّة) — والثَلاثَةُ تَصدُقُ بِمَتجَرٍ بِذلك الاسمِ وبِدونِه
row GET  "$S" "/api/push/manifest.json" 404
row GET  "$(hp "push.$DOMAIN")" "/api/push/manifest.json" 404
row GET  "$(hp "v1.$DOMAIN")" "/api/v1/deals" 404
# ‏(‏2026-11-07، W13-4·C2b·5 — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه) كانَ «$STORE-vendor ← 404» (مُضيفُ الدَورِ لا يُحَلُّ في المَرحَلَةِ الأُولى)؛ صارَ يُحَلُّ لِدَورٍ في المَتجَر
# (المَجموعَةُ ٩) — ودَورٌ لَيسَ في المَتجَرِ 404 هُنا: ROLE_ABSENT (افتِراضاً driver، لا يَحمِلُه ashare).
row GET  "$(hp "$STORE-$ROLE_ABSENT.$DOMAIN")" "/"   404
row GET  "$(hp "x.$STORE.$DOMAIN")" "/"   404
row GET  "$(hp "w13-no-such-store.$DOMAIN")" "/" 404

echo "٥) الجَذرُ والمَسارُ كَما كانا"
row GET  "$R" "/"                       200 - '"slug":""'
row GET  "$(hp "www.$DOMAIN")" "/"        200 - '"slug":""'
row GET  "$R" "/$STORE/explore"         200 - "<link rel=\"canonical\" href=\"$SCHEME://$R/$STORE/explore\" />" '"hostMode":false'
row GET  "$(hp "$OTHER.$DOMAIN")" "/"     200 - "<link rel=\"canonical\" href=\"$SCHEME://$(hp "$OTHER.$DOMAIN")/\" />" "\"slug\":\"$OTHER\""

echo "٦) مُديرُ المَتجَرِ عَلى مُضيفِه — يَدخُلُ ويَفتَحُ بِطاقاتِ /manage (كانَت 301 إلى الجَذرِ حَيثُ لا كَعكَتُه ← «غَير مُصَرَّح»)"
if [ -n "$MANAGER_PHONE" ]; then
  JAR="$TMP/jar"
  # ‏signin <path> <want> [--data-urlencode …] — خُطوَةُ دُخولٍ تُعَدُّ صَفّاً
  signin() {
    local path="$1" want="$2"; shift 2
    local code; code="$(curl -s -m 20 -o /dev/null -w '%{http_code}' -c "$JAR" -b "$JAR" -H "Host: $S" "$@" "$(url "$S" "$path")")" || code=000
    checked=$((checked + 1))
    if [ "$code" = "$want" ]; then printf '  ✓ %-4s %-34s %-52s → %s\n' POST "$S" "$path" "$code"
    else failed=$((failed + 1)); printf '  ✗ %-4s %-34s %-52s → الرَمز %s لا %s\n' POST "$S" "$path" "$code" "$want"; fi
  }
  signin "/auth/phone/login"  302 --data-urlencode "phone=$MANAGER_PHONE"
  signin "/auth/phone/verify" 302 --data-urlencode "phone=$MANAGER_PHONE" --data-urlencode "code=$DEV_CODE"
  row GET "$S" "/manage"                                   200 - "/admin/tenants/$STORE/categories?from=tenant"
  row GET "$S" "/admin/tenants/$STORE/categories?from=tenant" 200 - "acm-wizard-title" "!auth-title"
  row GET "$S" "/admin/tenants/$OTHER/categories?from=tenant" 302 "$SCHEME://$R/admin/tenants/$OTHER/categories?from=tenant"
  JAR=""
else
  echo "  – لَم تُفحَص: MANAGER_PHONE غائِب (هاتِفُ مُديرٍ لِـ$STORE، وDevelopment لِرَمزِ المُحاكي)"
fi

# ‏(‏2026-11-07، مُراجَعَةُ C2a الجَولَةُ الثانِيَة · C2a-P2-01 — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه) بابُ دُخولِ الجَوّالِ بِجَوّالٍ فارِغٍ يُحَوِّلُ بِـLink قَبلَ أَيِّ كِتابَة
# (رَمزُ رَفضٍ: phone_required، أَو القَناةُ لا يَعرِضُها المَتجَر) — فَالوِجهَةُ تَقيسُ دَورَ الـReferer وَحدَه.
echo "٧) الكِتابَةُ مِن صَفحَةِ دَورٍ بِعُنوانِها النَظيفِ تَبقى في الدَور (كانَ التَحويلُ يُسقِطُ /r/{role} — والـcanonical يُعلِنُ هذا العُنوان)"
# ‏(‏2026-11-07، ADR-050 §٩ المَرحَلَةُ الثانِيَة · W13-4·C2b·1 — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه) والوِجهَةُ نَظيفَةٌ عَلى مُضيفِ المَتجَر (`Link` عارِفٌ بِالمُضيف) — كانَت
# `/$STORE/r/customer/login?…`؛ والدَورُ مِن الـReferer كَما هُوَ.
REF="$SCHEME://$S/r/customer/login";                row POST "$S" "/auth/phone/login" 302 "/r/customer/login?*"
REF="$SCHEME://$S/$STORE/r/customer/login";         row POST "$S" "/auth/phone/login" 302 "/r/customer/login?*"
REF="$SCHEME://$S/login";                          row POST "$S" "/auth/phone/login" 302 "/login?*"
REF="$SCHEME://$(hp "$OTHER.$DOMAIN")/r/customer/login"; row POST "$S" "/auth/phone/login" 302 "/login?*"
REF=""

echo "٨) رَوابِطُ الصَفحَةِ نَظيفَةٌ عَلى مُضيفِ المَتجَرِ، وبِشَكلِ المَسارِ عَلى الجَذر (W13-4·C2b·1)"
row GET  "$S" "/"                       200 - 'href="/explore"' "!href=\"/$STORE/explore\""
row GET  "$S" "/explore"                200 - 'href="/explore"' 'ac-topnav-link-active' "!href=\"/$STORE/"
row GET  "$R" "/$STORE/explore"         200 - "href=\"/$STORE/explore\"" 'ac-topnav-link-active'

# ‏(‏2026-11-07، ADR-050 §٤ المَرحَلَةُ الثالِثَة · W13-4·C2b·5 — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه) مُضيفُ الدَورِ {store}-{role}.{domain}: صَفَحاتُ GET بِدَورِه، والبَيانُ بِنِطاقِ /،
# والكِتابَةُ ومَناطِقُ المَنَصَّةِ كَمُضيفِ المَتجَر، ودَورٌ لا يَحمِلُه المَتجَرُ 404، وبِطاقاتُ الأَدوارِ عَلى مُضيفِ المَتجَرِ رَوابِطُ إلى مُضيفاتِها.
RH="$(hp "$STORE-$ROLE.$DOMAIN")"
if [ -n "$ROLE" ]; then
echo "٩) مُضيفُ الدَور $RH — دَعوى في صَفَحاتِ GET وَحدَها"
row GET  "$RH" "/"                       200 - "<link rel=\"canonical\" href=\"$SCHEME://$RH/\" />" '"hostMode":true' "\"slug\":\"$STORE\"" '"role":""' "/api/$STORE/r/$ROLE/manifest.json"
row GET  "$RH" "/explore"                200 - "<link rel=\"canonical\" href=\"$SCHEME://$RH/explore\" />" 'href="/explore"' "!href=\"/$STORE/"
row GET  "$RH" "/api/$STORE/r/$ROLE/manifest.json" 200 - '"id":"/"' '"start_url":"/"' '"scope":"/"'
row GET  "$RH" "/sw.js"                  200 - "self.addEventListener"
row GET  "$RH" "/studio"                 302 "$SCHEME://$R/studio"
row POST "$RH" "/studio/apps/create"     404
row GET  "$RH" "/api/$OTHER/unread-counts" 404
row GET  "$(hp "$STORE-$ROLE_ABSENT.$DOMAIN")" "/explore" 404
row GET  "$(hp "x-$STORE-$ROLE.$DOMAIN")" "/" 404
REF="$SCHEME://$RH/login";                         row POST "$RH" "/auth/phone/login" 302 "/login?*"
REF="$SCHEME://$S/login";                          row POST "$RH" "/auth/phone/login" 302 "/login?*"
REF=""
row GET  "$S" "/"                        200 - "$SCHEME://$RH/login"
row GET  "$R" "/$STORE/r/$ROLE"          200 - "<link rel=\"canonical\" href=\"$SCHEME://$R/$STORE/r/$ROLE\" />" '"hostMode":false' "\"role\":\"$ROLE\""
else
  echo "٩) – لَم تُفحَص: ROLE فارِغ (دَورٌ مِن الكَتالوجِ يَحمِلُه $STORE — مُضيفُه $STORE-{role})"
fi

# ‏(‏2026-11-07، مُراجَعَةُ C2b الجَولَةُ الأُولى · SEC-C2b-2 وC2b-P-01 وC2b-P-02 — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه) **الجَلسَةُ تَنجو مِن النَقرَة** (القاعِدَة ١٢): عُضوٌ يَدخُلُ
# بِدَورِه عَلى مُضيفِ الدَور، وآخَرُ بِالدَورِ نَفسِه عَلى مُضيفِ المَتجَرِ (`/r/{role}/…` حَيثُ يَنزِلُ تَحويلُ `For`)، وتُعَدُّ رَوابِطُ صَفَحاتِه الخارِجَةُ إلى مُضيفٍ آخَرَ
# مِن عائِلَةِ النِطاق — والكَعكَتانِ بِلا `Domain=` فَكُلُّ رابِطٍ مِنها قَفزَةٌ إلى جَرَّةٍ فارِغَة. كانَ: الجَرَسُ و«تَنزيلُ بَياناتي» ومُرَشِّحاتُ الاستِكشافِ عَلى مُضيفِ الدَور،
# وشَريطُ التَنَقُّلِ كُلُّه مِن `/r/{role}` عَلى مُضيفِ المَتجَر، وأَوَّلُ كِتابَةٍ إلى شُروطٍ بِلا جَلسَة. **تَكتُبُ دُخولاً** (ومُستَخدِماً جَديداً إن لَم يَكُن) فَلا تَجري إلّا بِـ`ROLE_PHONE`.
echo "١٠) جَلسَةُ الدَورِ لا تُغادِرُ مُضيفَها — رَوابِطُ صَفَحاتِه عَلى المُضيفِ نَفسِه، والتَنزيلُ والشُروطُ تُخدَمُ هُناك (C2b-R1)"
if [ -n "$ROLE" ] && [ -n "$ROLE_PHONE" ]; then
  # ‏signin_on <host> <jar> <path> <want> [--data-urlencode …] — خُطوَةُ دُخولٍ تُعَدُّ صَفّاً
  signin_on() {
    local host="$1" jar="$2" path="$3" want="$4"; shift 4
    local code; code="$(curl -s -m 20 -o /dev/null -w '%{http_code}' -c "$jar" -b "$jar" -H "Host: $host" "$@" "$(url "$host" "$path")")" || code=000
    checked=$((checked + 1))
    if [ "$code" = "$want" ]; then printf '  ✓ %-4s %-34s %-52s → %s\n' POST "$host" "$path" "$code"
    else failed=$((failed + 1)); printf '  ✗ %-4s %-34s %-52s → الرَمز %s لا %s\n' POST "$host" "$path" "$code" "$want"; fi
  }
  # ‏stays <host> <path> — يَجلِبُ الصَفحَةَ بِالجَرَّةِ ويَعُدُّ رَوابِطَها: الخارِجُ إلى مُضيفٍ آخَرَ تَحتَ $DOMAIN يُفشِلُ الصَفّ؛ والصِفرُ رابِطاً أَداةٌ عَمياء
  hrefs_all=0
  stays() {
    local host="$1" path="$2" out="$TMP/page" code
    code="$(curl -s -m 20 -o "$out" -w '%{http_code}' -b "$JAR" -H "Host: $host" "$(url "$host" "$path")")" || code=000
    local all off
    all="$(grep -oE 'href="[^"]*"' "$out" | wc -l)"
    off="$(grep -oE 'href="(https?:)?//[^"/]+' "$out" | sed -E 's#href="(https?:)?//##' \
          | awk -v h="$host" -v d="$DOMAIN" '{ n=$0; sub(/:[0-9]+$/, "", n); if ($0 != h && (n == d || n ~ ("\\." d "$"))) print }' | sort | uniq -c)"
    checked=$((checked + 1)); hrefs_all=$((hrefs_all + all))
    # والجَلسَةُ حاضِرَة: صَفحَةٌ بِلا جَلسَةٍ تَرسُمُ رابِطَ دُخول (مَقيس: 1 في الجَرَسِ خارِجَ المُضيف، و0 عَلى مُضيفِ الدَور)
    if [ "$code" = 200 ] && [ "$all" -gt 0 ] && [ -z "$off" ] && ! grep -qE 'href="[^"]*/login(\?[^"]*)?"' "$out"; then
      printf '  ✓ %-4s %-34s %-52s → 200 · رَوابِط %s · خارِجَ المُضيف 0\n' GET "$host" "$path" "$all"
    else
      failed=$((failed + 1)); printf '  ✗ %-4s %-34s %-52s → %s · رَوابِط %s · خارِجَ المُضيف: %s\n' GET "$host" "$path" "$code" "$all" "$(echo "$off" | tr '\n' ' ')"
    fi
  }
  # مُضيفُ الدَور
  JAR="$TMP/rjar"
  signin_on "$RH" "$JAR" "/auth/phone/login"  302 --data-urlencode "phone=$ROLE_PHONE" --data-urlencode "as=$ROLE"
  signin_on "$RH" "$JAR" "/auth/phone/verify" 302 --data-urlencode "phone=$ROLE_PHONE" --data-urlencode "code=$DEV_CODE" --data-urlencode "as=$ROLE"
  for p in / /explore /explore?filters=open /me /me/data /notifications /me/listings; do stays "$RH" "$p"; done
  row GET  "$RH" "/me/data/download"       200 - '"'
  row GET  "$RH" "/notifications"          200 - "href=\"/notifications\"" "!href=\"$SCHEME://$S/notifications\""
  row POST "$RH" "/listings/create"        302 "/$STORE/terms?returnUrl=%2F$STORE%2Flistings%2Fcreate"
  row GET  "$RH" "/$STORE/terms?returnUrl=%2F$STORE%2Flistings%2Fcreate" 200 - "/$STORE/terms/accept"
  # مُضيفُ المَتجَر، صَفَحاتُ الدَور
  JAR="$TMP/sjar"
  signin_on "$S" "$JAR" "/auth/phone/login"  302 --data-urlencode "phone=$ROLE_PHONE" --data-urlencode "as=$ROLE"
  signin_on "$S" "$JAR" "/auth/phone/verify" 302 --data-urlencode "phone=$ROLE_PHONE" --data-urlencode "code=$DEV_CODE" --data-urlencode "as=$ROLE"
  for p in "/r/$ROLE" "/r/$ROLE/explore" "/r/$ROLE/me" "/r/$ROLE/me/listings" "/r/$ROLE/notifications"; do stays "$S" "$p"; done
  JAR=""
  echo "  الرَوابِطُ المَعدودَةُ في صَفَحاتِ الجَلسَة: $hrefs_all"
  if [ "$hrefs_all" -eq 0 ]; then echo "✗ فَحصٌ أَعمى: صِفرُ رابِط." >&2; failed=$((failed + 1)); fi
else
  echo "  – لَم تُفحَص: ROLE_PHONE غائِب (هاتِفٌ يَدخُلُ بِدَورِ ROLE، وDevelopment لِرَمزِ المُحاكي)"
fi

# ‏(‏2026-11-07، مُراجَعَةُ C2b الجَولَةُ الثانِيَة · SEC-C2b-R2-2 — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه) **الخُروجُ عَلى مُضيفٍ يُخرِجُ مِن مُضيفاتِ المَتجَرِ كُلِّها**: جَرَّةٌ
# واحِدَةٌ تَعرِفُ النِطاقاتِ (مُتَصَفِّح) — كُلُّ طَلَبٍ إلى المُضيفِ بِاسمِه (و`--resolve` إلى 127.0.0.1 ما لَم يَكُن DIRECT)، فَالكَعكَةُ مَقصورَةٌ عَلى مُضيفِها كَما في
# المُتَصَفِّح. العُضوُ يَدخُلُ عَلى المُضيفَين، ثُمَّ يَخرُجُ عَلى أَحَدِهِما وتُتبَعُ التَحويلاتُ واحِدَةً واحِدَة (كُلُّ وِجهَةٍ مُضيفٌ لِهذا المَتجَرِ تَحتَ النِطاق)،
# فَيَجِبُ أَن تَنتَهي في جَذرِ مُضيفِ البِدايَةِ وأَن يَطلُبَ المُضيفانِ الدُخولَ بَعدَها، ولا كَعكَةَ جَلسَةٍ لِلمَتجَرِ في الجَرَّة. كانَ: مُضيفُ الدَورِ داخِلاً بَعدَ الخُروجِ
# عَلى مُضيفِ المَتجَر (4 كَعكاتٍ باقِيَة). ومَحَطَّةُ مُرورٍ بِتَذكِرَةٍ مُختَلَقَةٍ لا تُخرِجُ أَحَداً. **تَكتُبُ دُخولاً** فَلا تَجري إلّا بِـ`ROLE_PHONE`.
echo "١١) الخُروجُ عَلى مُضيفٍ يُخرِجُ مِن مُضيفاتِ المَتجَرِ كُلِّها، ويَنتَهي حَيثُ بَدَأ (C2b-R2)"
if [ -n "$ROLE" ] && [ -n "$ROLE_PHONE" ]; then
  B="$TMP/browser"
  # ‏go <method> <host[:port]> <path> [curl…] — طَلَبٌ بِجَرَّةِ المُتَصَفِّحِ إلى المُضيفِ بِاسمِه؛ يَطبَعُ «الرَمز|الوِجهَة» (Location كَما هو)
  go() {
    local method="$1" hostport="$2" path="$3"; shift 3
    local name="${hostport%%:*}" port="${hostport##*:}" res=()
    [ "$port" = "$hostport" ] && port="$([ "$SCHEME" = https ] && echo 443 || echo 80)"
    [ "$DIRECT" = 1 ] || res=(--resolve "$name:$port:127.0.0.1")
    curl -s -m 20 -o "$TMP/gobody" -D "$TMP/gohead" -w '%{http_code}' -X "$method" -c "$B" -b "$B" "${res[@]}" "$@" "$SCHEME://$hostport$path" || echo 000
    printf '|%s' "$(tr -d '\r' < "$TMP/gohead" | sed -n 's/^[Ll]ocation: //p' | head -1)"
  }
  mark() { # mark <ok:0|1> <label> <detail>
    checked=$((checked + 1))
    if [ "$1" = 1 ]; then printf '  ✓ %-60s %s\n' "$2" "$3"; else failed=$((failed + 1)); printf '  ✗ %-60s %s\n' "$2" "$3"; fi
  }
  # ‏(‏2026-11-11، مُراجَعَةُ إغلاقِ W13-4 · SEC-C2b-R3-1 — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه) **وجَرَّةُ الجَذرِ (`R/$STORE`) ثالِثَة**: الجَذرُ يَخدِمُ المَتجَرَ بِمَسارِه
  # بِجَرَّةٍ مُستَقِلَّة، والخُروجُ عَلى المُضيفِ كانَ يَترُكُها داخِلَة، والخُروجُ عَلَيها يَترُكُ المُضيفاتِ داخِلَة (مَقيسٌ عِندَ 818ef7b). فَالدُخولُ والحالَةُ بِبادِئَةِ
  # مَسارٍ (`/$STORE` عَلى الجَذر)، والجَولَةُ تَبدَأُ مِن الثَلاثَة، وآخِرُ سِلسِلَةٍ بَدَأَت عَلى الجَذرِ `R/$STORE`.
  pre() { [ "$1" = "$R" ] && printf '/%s' "$STORE"; }   # بادِئَةُ المَسارِ عَلى الجَذرِ وَحدَه
  signin_b() { # signin_b <host>
    local g1 g2
    g1="$(go POST "$1" "$(pre "$1")/auth/phone/login" --data-urlencode "phone=$ROLE_PHONE" --data-urlencode "as=$ROLE")"
    g2="$(go POST "$1" "$(pre "$1")/auth/phone/verify" --data-urlencode "phone=$ROLE_PHONE" --data-urlencode "code=$DEV_CODE" --data-urlencode "as=$ROLE")"
    mark "$([ "${g1%%|*}" = 302 ] && [ "${g2%%|*}" = 302 ] && echo 1 || echo 0)" "دُخولٌ بِـ$ROLE عَلى $1$(pre "$1")" "${g1%%|*} ← ${g2%%|*}"
  }
  # ‏state <host> → in (صَفحَةُ /me لِهذا المَتجَرِ بِجَلسَة) · out (صَفحَتُه تَطلُبُ الدُخول، أَو تَحويلٌ إلى دُخول) · وما سِواهُما رَمزُه — فَصَفحَةٌ لَيسَت لِلمَتجَرِ
  # (404، أَو قِشرَةُ المَنَصَّة) لا تُقرَأُ «داخِلاً» ولا «خارِجاً» (مَقيس: بِلا نِطاقٍ كانَت قِشرَةُ المَنَصَّةِ تُقرَأُ «بِجَلسَة»).
  state() {
    local g code; g="$(go GET "$1" "$(pre "$1")/me")"; code="${g%%|*}"
    if [ "$code" = 200 ] && grep -qF "\"slug\":\"$STORE\"" "$TMP/gobody"; then
      if grep -qE 'href="[^"]*/login(\?[^"]*)?"' "$TMP/gobody"; then echo out; else echo in; fi
    elif [ "$code" = 302 ] && [[ "${g#*|}" == */login* ]]; then echo out
    else echo "$code"; fi
  }
  inside() { [ "$(state "$1")" = in ] && echo 1 || echo 0; }
  outside() { [ "$(state "$1")" = out ] && echo 1 || echo 0; }
  ours() { # ours <host[:port]> → 1 إن كانَ مُضيفاً لِهذا المَتجَرِ تَحتَ النِطاق
    local n="${1%%:*}"
    case "$n" in "$STORE.$DOMAIN"|"$STORE"-*."$DOMAIN"|"$DOMAIN") echo 1 ;; *) echo 0 ;; esac
  }
  # حَدُّ التَحَقُّقِ في المُنتَجِ خَمسٌ في الدَقيقَةِ لِكُلِّ جَوّالٍ ومَتجَر (`AuthHandlers.MaxVerifyPerMinute`)، والمَجموعَةُ ١٠ تَحَقَّقَت مَرَّتَين: فَالجَولَةُ الثانِيَةُ
  # تَنتَظِرُ النافِذَةَ — وإلّا رُدَّ دُخولُها بِحَدِّ المُنتَجِ لا بِعَيبٍ فيه (مَقيس: «قَبلَ الخُروج … بِجَلسَة ✗» في الدُخولِ السادِس).
  # ‏(‏2026-11-11، إغلاقُ W13-4 · C2z·8 — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه) **وحِصَّةُ الإرسالِ عَشرٌ في الساعَةِ لِكُلِّ جَوّالٍ ومَتجَر** (`AuthHandlers.MaxRequestPerHour`):
  # بِثَلاثِ جَرّاتٍ في كُلِّ جَولَةٍ صارَت هذِه المَجموعَةُ تِسعَ رَسائِل، ومَعَ المَجموعَتَينِ ١٠ (2) و١٣ (1) اثنَتَي عَشرَة — فَرُدَّت الحادِيَةَ عَشرَة والثانِيَةَ
  # عَشرَة بِحَدِّ المُنتَجِ لا بِعَيبٍ فيه (مَقيس: «✗ قَبلَ الخُروج: localhost:5050/{store}/me بِجَلسَة» في الجَولَةِ الثالِثَة، و«0 مِن 0» في ١٣ — «172 · فَشِلَ: 8»).
  # فَالجَولَةُ الأولى (مِن المَتجَر) بِالثَلاث، والثانِيَةُ (مِن الدَور) بِالدَورِ والجَذر، والثالِثَةُ (مِن الجَذر) بِالجَذرِ والمَتجَر: كُلُّ مُضيفٍ مَحَطَّةٌ يُرى مَحوُها
  # مِن بِدايَةٍ أُخرى، والمَجموعُ عَشر. **وإعادَةُ التَشغيلِ بِالجَوّالِ نَفسِه قَبلَ ساعَةٍ تُرَدّ** — جَوّالٌ آخَرُ أَو خادِمٌ أُعيدَ تَشغيلُه (العَدّادُ في الذاكِرَة).
  for START in "$S" "$RH" "$R"; do
    rm -f "$B"
    [ "$START" != "$S" ] && { echo "  … انتِظارُ نافِذَةِ حَدِّ التَحَقُّق (61 ثانِيَة)"; sleep 61; }
    case "$START" in "$S") IN=("$RH" "$S" "$R") ;; "$RH") IN=("$RH" "$R") ;; *) IN=("$R" "$S") ;; esac
    for h in "${IN[@]}"; do signin_b "$h"; done
    for h in "${IN[@]}"; do mark "$(inside "$h")" "قَبلَ الخُروج: $h$(pre "$h")/me بِجَلسَة" ""; done
    if [ "$START" = "$RH" ]; then
      # ومَحَطَّةُ مُرورٍ بِتَذكِرَةٍ مُختَلَقَةٍ لا تُخرِجُ أَحَداً — والجَلسَةُ قائِمَةٌ قَبلَها وبَعدَها
      g="$(go GET "$RH" "/$STORE/auth/logout/continue?t=bG9nb3V0LWhvcHx4")"
      mark "$([ "${g%%|*}" = 302 ] && [ "${g#*|}" = "/" ] && [ "$(inside "$RH")" = 1 ] && echo 1 || echo 0)" \
           "تَذكِرَةٌ مُختَلَقَةٌ عَلى $RH لا تُخرِجُ أَحَداً" "${g%%|*} → ${g#*|}"
    fi
    g="$(go POST "$START" "/$STORE/auth/logout" -H 'Content-Type: application/x-www-form-urlencoded' --data '')"
    hops=0; bad=""; final="$SCHEME://$START/"; [ "$START" = "$R" ] && final="$SCHEME://$R/$STORE"
    while [ "${g%%|*}" = 302 ] && [ "${g#*|}" != "$final" ] && [ "$hops" -lt 15 ]; do
      loc="${g#*|}"; rest="${loc#*://}"; hp_="${rest%%/*}"; p_="/${rest#*/}"
      [ "$(ours "$hp_")" = 1 ] || { bad="وِجهَةٌ خارِجَ مُضيفاتِ المَتجَر: $loc"; break; }
      hops=$((hops + 1)); g="$(go GET "$hp_" "$p_")"
    done
    mark "$([ -z "$bad" ] && [ "${g%%|*}" = 302 ] && [ "${g#*|}" = "$final" ] && [ "$hops" -ge 1 ] && echo 1 || echo 0)" \
         "الخُروجُ عَلى $START ← يَنتَهي في $final" "مَحَطّاتُ المُرور: $hops ${bad:-· آخِرُها ${g%%|*} → ${g#*|}}"
    for h in "${IN[@]}"; do mark "$(outside "$h")" "بَعدَ الخُروج: $h$(pre "$h")/me يَطلُبُ الدُخول" ""; done
    left="$(grep -c "acommerce\.auth\.$STORE" "$B" || true)"
    mark "$([ "$left" = 0 ] && echo 1 || echo 0)" "بَعدَ الخُروج: كَعكاتُ جَلسَةِ $STORE في الجَرَّة" "$left"
  done
  rm -f "$B"
else
  echo "  – لَم تُفحَص: ROLE أَو ROLE_PHONE غائِب (هاتِفٌ يَدخُلُ بِدَورِ ROLE، وDevelopment لِرَمزِ المُحاكي)"
fi

# ‏(‏2026-11-07، ADR-050 §٦ · W13-4·C2c·1 — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه) **طَلَبُ الكِتابَةِ مِن أَصلٍ آخَرَ يُرَدُّ 403** (`CrossOriginWriteGate`): بابُ دُخولِ الجَوّالِ بِجَوّالٍ
# فارِغٍ يُحَوِّلُ قَبلَ أَيِّ كِتابَة (المَجموعَةُ ٧)، فَالـ302 يَعني أَنّ الطَلَبَ بَلَغَ البابَ والـ403 أَنّ الفَحصَ رَدَّه. ونُقطَةُ إشعارِ البَوّابَةِ مُستَثناةٌ بِاسمِها: تَبلُغُ
# مُعالِجَها (404 «لا سِرَّ لِهذا المَتجَر» — لا يُقرَأُ جِسمٌ ولا يُكتَبُ شَيء) بِأَصلِ مُزَوِّد. **لا يَكتُبُ شَيئاً.**
echo "١٢) طَلَبُ الكِتابَةِ مِن أَصلٍ آخَرَ يُرَدّ، ومِن أَصلِه يَبلُغُ بابَه (C2c·1)"
ORIGIN="$SCHEME://$S";                             row POST "$S" "/auth/phone/login" 302 "/login?*"
ORIGIN="";                                         row POST "$S" "/auth/phone/login" 302 "/login?*"
ORIGIN="$SCHEME://$(hp "$OTHER.$DOMAIN")";         row POST "$S" "/auth/phone/login" 403
ORIGIN="$SCHEME://$R";                             row POST "$S" "/auth/phone/login" 403
ORIGIN="null";                                     row POST "$S" "/auth/phone/login" 403
ORIGIN="$SCHEME://$R";                             row POST "$R" "/$STORE/auth/phone/login" 302 "/$STORE/login?*"
ORIGIN="https://evil.example";                     row POST "$R" "/$STORE/auth/phone/login" 403
ORIGIN="$SCHEME://$S";                             row POST "$R" "/$STORE/auth/phone/login" 403
ORIGIN="https://api.moyasar.com";                  row POST "$R" "/$STORE/payments/notify" 404
if [ -n "$ROLE" ]; then
ORIGIN="$SCHEME://$S";                             row POST "$RH" "/auth/phone/login" 403
ORIGIN="$SCHEME://$RH";                            row POST "$RH" "/auth/phone/login" 302 "/login?*"
fi
ORIGIN=""
# ‏(‏2026-11-08، مُراجَعَةُ C2c الجَولَةُ الثانِيَة · C2c-P2-01 — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه) **قِراءَةُ المالِكِ بِحَرفِها** (DEPLOY §٢·ي، قِسمُ Cloudflare، الخُطوَةُ ٠):
# صُفوفُ `row` تُضيفُ نَوعَ الاستِمارَةِ وجِسماً فارِغاً، والأَمرُ المَكتوبُ لِلمالِكِ كانَ `-X POST` بِلا جِسم — فَأَجابَ أَصلُه 415 لا 302، ولَم يُقَس قَطّ. فَهُنا
# أَعلامُ الخُطوَةِ ٠ حَرفاً (`OWNER_READ`؛ ويَحمَرُّ `OwnerOriginReadTests` إن اختَلَفَت عَن أَعلامِ الدَليل)، ولا يُضافُ إلَيها إلّا ما يَبلُغُ بِه العُنوان: رَأسُ `Host`
# مَحَلِّيّاً ومُهلَة. والأَمرُ القَديمُ (`OWNER_READ_BARE`) يَبلُغُ ما بَعدَ فَحصِ الأَصلِ ويُرَدُّ 415 مِن بَوّابَةِ الاستِمارَة — فَـ415 لا يَحكُمُ عَلى الفَحص. **لا يَكتُبُ شَيئاً.**
OWNER_READ=(-s -o /dev/null -w '%{http_code}\n' -d '')
OWNER_READ_BARE=(-s -o /dev/null -w '%{http_code}\n' -X POST)
owner_read() { # owner_read <اسمُ مَصفوفَةِ الأَعلام> <origin> <want>
  local -n flags="$1"; local origin="$2" want="$3" got reach=(-m 20)
  [ "$DIRECT" = 1 ] || reach+=(-H "Host: $R")
  got="$(curl "${flags[@]}" "${reach[@]}" -H "Origin: $origin" "$(url "$R" "/$STORE/auth/phone/login")")" || got="000"
  checked=$((checked + 1))
  if [ "$got" = "$want" ]; then printf '  ✓ %-4s %-34s %-52s → %s\n' "$1" "$R" "/$STORE/auth/phone/login · Origin: $origin" "$got"
  else failed=$((failed + 1)); printf '  ✗ %-4s %-34s %-52s → الرَمز %s لا %s\n' "$1" "$R" "/$STORE/auth/phone/login · Origin: $origin" "$got" "$want"; fi
}
owner_read OWNER_READ      "https://evil.example" 403
owner_read OWNER_READ      "$SCHEME://$R"         302
owner_read OWNER_READ_BARE "$SCHEME://$R"         415

# ‏(‏2026-11-07، ADR-050 §٥ · W13-4·C2c·2 — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه) **كَعكاتُ الجَلسَةِ عَلى HTTPS بِالبادِئَةِ `__Host-`**: مَحَلِّيّاً يُقالُ HTTPS بِرَأسِ
# `X-Forwarded-Proto: https` — والوَسيطُ يُصَدِّقُه مِن أَيِّ نِدٍّ (`KnownProxies` فارِغ؛ وهي عَينُ ما يُرسِلُه Cloudflare وFly، ولِذلك يُقفَلُ الأَصلُ عَلى Cloudflare — DEPLOY
# §٢·ي). العُضوُ يَدخُلُ بِدَورِه عَلى مُضيفِ المَتجَر: كُلُّ `Set-Cookie` لِلمَتجَرِ `__Host-` بِـ`Secure` و`Path=/` وبِلا `Domain`؛ وصَفحَةُ `/me` بِتِلكَ الكَعكاتِ داخِلَة،
# وبِالقيَمِ نَفسِها تَحتَ الاسمِ القَديمِ (ما يَزرَعُه شَقيقٌ بِـ`Domain=R`) خارِجَة. **تَكتُبُ دُخولاً** فَلا تَجري إلّا بِـ`ROLE_PHONE`.
echo "١٣) كَعكاتُ الجَلسَةِ عَلى HTTPS بِالبادِئَةِ __Host-، والاسمُ القَديمُ لا يُقرَأ (C2c·2)"
if [ -n "$ROLE" ] && [ -n "$ROLE_PHONE" ]; then
  hs() { # hs <path> <out-headers> [curl…] — طَلَبٌ عَلى مُضيفِ المَتجَرِ بِـHTTPS المُمَرَّر؛ يَطبَعُ الرَمز
    local path="$1" head="$2"; shift 2
    curl -s -m 20 -o "$TMP/hsbody" -D "$head" -w '%{http_code}' -H "Host: $S" -H 'X-Forwarded-Proto: https' "$@" "$(url "$S" "$path")" || echo 000
  }
  tick() { # tick <ok:0|1> <label> <detail>
    checked=$((checked + 1))
    if [ "$1" = 1 ]; then printf '  ✓ %-60s %s\n' "$2" "$3"; else failed=$((failed + 1)); printf '  ✗ %-60s %s\n' "$2" "$3"; fi
  }
  c1="$(hs /auth/phone/login "$TMP/h1" --data-urlencode "phone=$ROLE_PHONE" --data-urlencode "as=$ROLE")"
  c2="$(hs /auth/phone/verify "$TMP/h2" --data-urlencode "phone=$ROLE_PHONE" --data-urlencode "code=$DEV_CODE" --data-urlencode "as=$ROLE")"
  setc="$(tr -d '\r' < "$TMP/h2" | sed -n 's/^[Ss]et-[Cc]ookie: //p' | grep -E "acommerce\.auth\.$STORE" || true)"
  total="$(printf '%s\n' "$setc" | grep -c . || true)"
  good="$(printf '%s\n' "$setc" | grep -E "^__Host-\.acommerce\.auth\.$STORE[^=]*=" | grep -i '; secure' | grep -i '; path=/' | grep -vic '; domain=' || true)"
  tick "$([ "$c1" = 302 ] && [ "$c2" = 302 ] && [ "$total" -ge 2 ] && [ "$good" = "$total" ] && echo 1 || echo 0)" \
       "دُخولٌ بِـ$ROLE عَلى $S بِـHTTPS: كُلُّ Set-Cookie __Host- بِشُروطِها" "$c1 ← $c2 · $good مِن $total"
  pairs="$(printf '%s\n' "$setc" | sed 's/;.*//' | paste -sd';' - | sed 's/;/; /g')"
  legacy="$(printf '%s' "$pairs" | sed 's/__Host-//g')"
  for v in "with:$pairs" "legacy:$legacy"; do
    kind="${v%%:*}" cookie="${v#*:}"
    code="$(hs /me "$TMP/h3" -H "Cookie: $cookie")"
    if [ "$code" = 200 ] && grep -qF "\"slug\":\"$STORE\"" "$TMP/hsbody" && ! grep -qE 'href="[^"]*/login(\?[^"]*)?"' "$TMP/hsbody"; then st=in; else st=out; fi
    if [ "$kind" = with ]; then tick "$([ "$st" = in ] && echo 1 || echo 0)" "/me بِكَعكاتِ __Host- عَلى HTTPS ← داخِل" "$code · $st"
    else tick "$([ "$st" = out ] && echo 1 || echo 0)" "/me بِالقيَمِ نَفسِها تَحتَ الاسمِ القَديم ← خارِج" "$code · $st"; fi
  done
  # ‏(‏2026-11-07، مُراجَعَةُ C2c الجَولَةُ الأُولى · SEC-C2c-1 وC2c-P-01 — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه) **«اخرج من كل الأجهزة» لا يَعودُ بِمُكَرَّرٍ ولا بِتَنصيص**: كانَ
  # القارِئُ بِالبادِئَةِ يَقرَأُ أَوَّلَ زَوجٍ بِحُروفِه (بِلا تَنصيص) والبَوّابَةُ تَحكُمُ بِالقامُوس (آخِرُ المُكَرَّرِ بِلا حالَةِ أَحرُف) — فَالرَمزُ الساقِطُ ومَعَه «=x» بَعدَه
  # (بِحُروفِه، أَو __HOST-، أَو في سَطرٍ ثانٍ) أَو بَينَ عَلامَتَي تَنصيصٍ كانَ /me بِه داخِلاً. المُكَرَّرُ قَبلَ الإسقاطِ داخِلٌ (الصَفُّ لَيسَ أَعمى)، ثُمَّ الإسقاطُ بِالجَلسَةِ
  # نَفسِها، ثُمَّ الرَمزُ وَحدَه وبِصُوَرِه خارِج — العامُّ عَلى /me وكَعكَةُ الدَورِ عَلى /r/$ROLE/me. **يَكتُبُ** (يَرفَعُ إصدارَ جَلسَةِ ROLE_PHONE).
  me_state() { # me_state <path> <curl…> — «الرَمز in|out» لِصَفحَةٍ عَلى مُضيفِ المَتجَرِ بِـHTTPS المُمَرَّر
    local path="$1" code; shift
    code="$(hs "$path" "$TMP/h4" "$@")"
    if [ "$code" = 200 ] && grep -qF "\"slug\":\"$STORE\"" "$TMP/hsbody" && ! grep -qE 'href="[^"]*/login(\?[^"]*)?"' "$TMP/hsbody"; then echo "$code in"; else echo "$code out"; fi
  }
  gpair="$(printf '%s\n' "$setc" | sed 's/;.*//' | grep -E "^__Host-\.acommerce\.auth\.$STORE=" | head -1)"
  rpair="$(printf '%s\n' "$setc" | sed 's/;.*//' | grep -E "^__Host-\.acommerce\.auth\.$STORE\.$ROLE=" | head -1)"
  gn="${gpair%%=*}" gv="${gpair#*=}" rn="${rpair%%=*}" rv="${rpair#*=}"
  st="$(me_state /me -H "Cookie: $gn=$gv; $gn=x")"
  tick "$([ -n "$gpair" ] && [ -n "$rpair" ] && [ "${st#* }" = in ] && echo 1 || echo 0)" "قَبلَ الإسقاط: /me بِالرَمزِ ومَعَه مُكَرَّرٌ تالِف ← داخِل" "$st"
  code="$(hs /me/sessions/revoke "$TMP/h5" -H "Cookie: $pairs" -H 'Content-Type: application/x-www-form-urlencoded' --data '')"
  tick "$([ "$code" = 302 ] && echo 1 || echo 0)" "POST /me/sessions/revoke («اخرج من كل الأجهزة») ← 302" "$code"
  for v in "وَحدَه|/me|Cookie: $gn=$gv" "ومَعَه مُكَرَّرٌ بِحُروفِه|/me|Cookie: $gn=$gv; $gn=x" "ومَعَه مُكَرَّرٌ __HOST-|/me|Cookie: $gn=$gv; __HOST-${gn#__Host-}=x" \
           "بَينَ عَلامَتَي تَنصيص|/me|Cookie: $gn=\"$gv\"" "كَعكَةُ الدَورِ ومَعَها مُكَرَّر|/r/$ROLE/me|Cookie: $rn=$rv; $rn=x"; do
    IFS='|' read -r label path cookie <<< "$v"
    st="$(me_state "$path" -H "$cookie")"
    tick "$([ "${st#* }" = out ] && echo 1 || echo 0)" "بَعدَ الإسقاط: $path بِالساقِطِ $label ← خارِج" "$st"
  done
  st="$(me_state /me -H "Cookie: $gn=$gv" -H "Cookie: $gn=x")"
  tick "$([ "${st#* }" = out ] && echo 1 || echo 0)" "بَعدَ الإسقاط: /me بِالساقِطِ ومُكَرَّرِه في سَطرَينِ ← خارِج" "$st"
else
  echo "  – لَم تُفحَص: ROLE أَو ROLE_PHONE غائِب (هاتِفٌ يَدخُلُ بِدَورِ ROLE، وDevelopment لِرَمزِ المُحاكي)"
fi
# ‏(‏2026-11-07، مُراجَعَةُ C2c الجَولَةُ الأُولى · SEC-C2c-1 — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه) **والاستوديو مِثلُه**: مُشرِفُ المَنَصَّةِ يَدخُلُ عَلى الجَذرِ بِـHTTPS المُمَرَّر، و/admin
# بِكَعكَتِه وَحدَها ومَعَ مُكَرَّرٍ تالِفٍ لَوحَةُ الإدارَة؛ ثُمَّ «اخرج من كل الأجهزة» (/studio/me/sessions/revoke)، فَـ/admin بِها وَحدَها وبِالمُكَرَّرِ بِحُروفِه وبِـ__HOST- لَيسَ لَوحَةَ
# الإدارَة (صِفرُ رابِطٍ إلى /admin/tenants). كانَ المُكَرَّرُ يُعيدُها. **يَكتُبُ دُخولاً ويَرفَعُ إصدارَ جَلسَةِ المُشرِف** فَلا يَجري إلّا بِـ`ADMIN_PHONE`.
if [ -n "$ADMIN_PHONE" ]; then
  hr() { # hr <path> <out-headers> [curl…] — طَلَبٌ عَلى الجَذرِ بِـHTTPS المُمَرَّر؛ يَطبَعُ الرَمز
    local path="$1" head="$2"; shift 2
    curl -s -m 20 -o "$TMP/hrbody" -D "$head" -w '%{http_code}' -H "Host: $R" -H 'X-Forwarded-Proto: https' "$@" "$(url "$R" "$path")" || echo 000
  }
  tick() { # tick <ok:0|1> <label> <detail>
    checked=$((checked + 1))
    if [ "$1" = 1 ]; then printf '  ✓ %-60s %s\n' "$2" "$3"; else failed=$((failed + 1)); printf '  ✗ %-60s %s\n' "$2" "$3"; fi
  }
  admin_state() { # admin_state <curl…> — «الرَمز in|out · رَوابِط» لِـ/admin
    local code links; code="$(hr /admin "$TMP/h6" "$@")"
    links="$(grep -oE 'href="/admin/tenants[^"]*"' "$TMP/hrbody" | wc -l)"
    if [ "$code" = 200 ] && [ "$links" -gt 0 ]; then echo "$code in · $links"; else echo "$code out · $links"; fi
  }
  c1="$(hr /studio/auth/login "$TMP/a1" --data-urlencode "phone=$ADMIN_PHONE")"
  c2="$(hr /studio/auth/verify "$TMP/a2" --data-urlencode method=phone --data-urlencode "phone=$ADMIN_PHONE" --data-urlencode "code=$DEV_CODE")"
  spair="$(tr -d '\r' < "$TMP/a2" | sed -n 's/^[Ss]et-[Cc]ookie: //p' | sed 's/;.*//' | grep -E '^__Host-\.acommerce\.studio=' | head -1)"
  sn="${spair%%=*}" sv="${spair#*=}"
  tick "$([ "$c1" = 302 ] && [ "$c2" = 302 ] && [ -n "$spair" ] && echo 1 || echo 0)" "دُخولُ المُشرِفِ عَلى $R بِـHTTPS: كَعكَةُ __Host-.acommerce.studio" "$c1 ← $c2"
  for v in "وَحدَها|Cookie: $sn=$sv" "ومَعَها مُكَرَّرٌ تالِف|Cookie: $sn=$sv; $sn=x"; do
    IFS='|' read -r label cookie <<< "$v"
    st="$(admin_state -H "$cookie")"
    tick "$([ "$(echo "$st" | cut -d' ' -f2)" = in ] && echo 1 || echo 0)" "قَبلَ الإسقاط: /admin بِكَعكَةِ المُشرِفِ $label ← داخِل" "$st"
  done
  code="$(hr /studio/me/sessions/revoke "$TMP/a3" -H "Cookie: $sn=$sv" -H 'Content-Type: application/x-www-form-urlencoded' --data '')"
  tick "$([ "$code" = 302 ] && echo 1 || echo 0)" "POST /studio/me/sessions/revoke ← 302" "$code"
  for v in "وَحدَها|Cookie: $sn=$sv" "ومَعَها مُكَرَّرٌ بِحُروفِه|Cookie: $sn=$sv; $sn=x" "ومَعَها مُكَرَّرٌ __HOST-|Cookie: $sn=$sv; __HOST-${sn#__Host-}=x"; do
    IFS='|' read -r label cookie <<< "$v"
    st="$(admin_state -H "$cookie")"
    tick "$([ "$(echo "$st" | cut -d' ' -f2)" = out ] && echo 1 || echo 0)" "بَعدَ الإسقاط: /admin بِالساقِطَةِ $label ← خارِج" "$st"
  done
else
  echo "  – صُفوفُ الاستوديو لَم تُفحَص: ADMIN_PHONE غائِب (PLATFORM_ADMIN_PHONE لِلخادِم، وDevelopment لِرَمزِ المُحاكي)"
fi

# ‏(‏2026-11-07، ADR-050 §٩ المَرحَلَةُ الرابِعَة · W13-4·C2c·3 — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه) **`AllowedHosts=R;*.R` يُقاسُ قَبلَ أَن يُضبَط** (A2 W18 — دَعمُ
# الـwildcard لَم يَكُن في النَصِّ المَجلوب): خادِمٌ شُغِّلَ بِـ`AllowedHosts="$DOMAIN;*.$DOMAIN"` يُجيبُ الجَذرَ ومُضيفَ المَتجَرِ ومُضيفَ الدَور، ويَرُدُّ 400 لِمُضيفٍ
# خارِجَ النِطاقِ ولِعُنوانِ IP — **ومِنه فَحصُ صِحَّةٍ يُنادي بِعُنوانِ IP** (يُقالُ في DEPLOY). لا يَكتُبُ شَيئاً.
if [ "$ALLOWED" = 1 ]; then
echo "١٤) AllowedHosts=$DOMAIN;*.$DOMAIN — النِطاقُ ومُضيفاتُه تُجاب، وما سِواها 400 (C2c·3)"
row GET  "$R" "/health"                  200
row GET  "$S" "/"                        200 - "\"slug\":\"$STORE\""
[ -n "$ROLE" ] && row GET "$RH" "/"      200 - "\"slug\":\"$STORE\""
row GET  "$(hp "evil.example")" "/health" 400
row GET  "$(hp "127.0.0.1")" "/health"   400
row GET  "$(hp "$DOMAIN.evil.example")" "/health" 400
fi

# ‏(‏2026-11-11، مُراجَعَةُ إغلاقِ W13-4 · C2a-P3-01 — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه) **نِطاقُ عامِلِ الخِدمَةِ مِن بادِئَةِ العُنوانِ الظاهِر** (`base` في `acTenant`): صَفحَةُ
# دَورٍ بِشَكلِ المَسارِ عَلى المُضيفِ (رابِطٌ مَحفوظ، أَو `RelatedUrl`، أَو عَودَةُ استِمارَة) كانَت تُسَجِّلُ عامِلَها بِنِطاقِ `/r/{role}/` الَّذي لا يَحكُمُها. البادِئَةُ
# `/{store}` حَيثُ يَحمِلُها العُنوانُ وفارِغَةٌ عَلى العُنوانِ النَظيف، وعَلى الجَذرِ `/{store}` كَما كانَ السكربتُ يَبنيها. **لا يَكتُبُ شَيئاً.**
echo "١٥) نِطاقُ عامِلِ الخِدمَةِ مِن بادِئَةِ العُنوانِ الظاهِر (C2z·1)"
row GET  "$S" "/explore"                 200 - '"base":""'
row GET  "$S" "/$STORE/explore"          200 - "\"base\":\"/$STORE\""
row GET  "$R" "/$STORE/explore"          200 - "\"base\":\"/$STORE\""
if [ -n "$ROLE" ]; then
row GET  "$S" "/$STORE/r/$ROLE/explore"  200 - "\"base\":\"/$STORE\"" "\"role\":\"$ROLE\""
row GET  "$S" "/r/$ROLE/explore"         200 - '"base":""' "\"role\":\"$ROLE\""
fi

# ‏(‏2026-11-11، مُراجَعَةُ إغلاقِ W13-4 · SEC-C2c-R3-1 — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه) **HTTP مُغلَقٌ عِندَ الحافَّة** (DEPLOY §٢·ي، قِسمُ Cloudflare، الخُطوَةُ ٣·ب):
# `__Host-` وفَحصُ `Origin` يَحرُسانِ زِيارَةَ HTTPS وَحدَها، والتَطبيقُ لا يُحَوِّلُ HTTP. فَبَعدَ النَشرِ (`DIRECT=1 SCHEME=https`) كُلُّ عائِلَةِ مُضيفٍ بِـ`http://`
# تُجابُ تَحويلاً إلى `https://` بِالمُضيفِ نَفسِه — مِن Cloudflare لا مِن التَطبيق؛ ومَحَلِّيّاً لا حافَّةَ فَلا تُفحَص. **لا يَكتُبُ شَيئاً.**
echo "١٦) HTTP يُحَوَّلُ إلى HTTPS عِندَ الحافَّةِ لِكُلِّ عائِلَةِ مُضيف (C2z·1)"
if [ "$DIRECT" = 1 ] && [ "$SCHEME" = https ]; then
  for h in "$R" "$S" ${ROLE:+"$RH"}; do
    got="$(curl -s -m 20 -o /dev/null -D "$TMP/edge" -w '%{http_code}' "http://$h/")" || got=000
    loc="$(tr -d '\r' < "$TMP/edge" | sed -n 's/^[Ll]ocation: //p' | head -1)"
    checked=$((checked + 1))
    if [[ "$got" =~ ^30[1278]$ && "$loc" == "https://$h/"* ]]; then
      printf '  ✓ %-4s %-34s %-52s → %s\n' GET "http://$h" "/" "$got $loc"
    else failed=$((failed + 1)); printf '  ✗ %-4s %-34s %-52s → %s\n' GET "http://$h" "/" "الرَمز $got والوِجهَة «$loc» — لا تَحويلَ إلى https"; fi
  done
else
  echo "  – لَم تُفحَص: بَعدَ النَشرِ وَحدَه (DIRECT=1 SCHEME=https) — مَحَلِّيّاً لا حافَّة"
fi

# ‏(‏2026-11-11، مُراجَعَةُ إغلاقِ W13-4 · C2c-P3-01 — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه) **خَلفَ عامِلٍ يُعيدُ كِتابَةَ `Host`** (DEPLOY الخُطوَتانِ ٦ و٩): خادِمٌ شُغِّلَ بِـ
# `ForwardedHeaders__AllowedHosts="$DOMAIN,*.$DOMAIN"` يَتَلَقّى ما يُرسِلُه عامِلٌ أَمامَ HF — `Host` اسمُ الأَصلِ الداخِلِيّ، والاسمُ العامُّ في `X-Forwarded-Host` — فَالكِتابَةُ
# بِأَصلِها عَلى الجَذرِ وعَلى المُضيفِ تَبلُغُ بابَها (302)؛ وكانَت عَلى الجَذرِ 403 بِـ`*.$DOMAIN` وَحدَه. ومَعَ `ALLOWED=1` يُقاسُ أَنّ `AllowedHosts` يَحكُمُ عَلى `Host`
# الواصِل: الاسمُ الداخِلِيُّ خارِجَ القائِمَةِ ← 400. **لا يَكتُبُ شَيئاً.**
if [ "${WORKER:-0}" = 1 ]; then
echo "١٧) خَلفَ عامِلٍ يُعيدُ كِتابَةَ Host — الجَذرُ بِاسمِه في ForwardedHeaders__AllowedHosts (C2z·1)"
  INTERNAL="$(hp "space-owner.hf.example")"
  worker() { # worker <x-forwarded-host> <path> <origin> <want> [method]
    local got extra=()
    [ -n "$3" ] && extra+=(-H "Origin: $3")
    [ "${5:-POST}" = POST ] && extra+=(-d '')
    got="$(curl -s -m 20 -o /dev/null -w '%{http_code}' -X "${5:-POST}" -H "Host: $INTERNAL" -H "X-Forwarded-Host: $1" -H "X-Forwarded-Proto: $SCHEME" \
      "${extra[@]}" "$ADDR$2")" || got=000
    checked=$((checked + 1))
    if [ "$got" = "$4" ]; then printf '  ✓ %-4s %-34s %-52s → %s\n' "${5:-POST}" "XFH $1" "$2" "$got"
    else failed=$((failed + 1)); printf '  ✗ %-4s %-34s %-52s → الرَمز %s لا %s\n' "${5:-POST}" "XFH $1" "$2" "$got" "$4"; fi
  }
  if [ "$ALLOWED" = 1 ]; then
    worker "$R" "/health" "" 400 GET
  else
    worker "$R" "/$STORE/auth/phone/login" "$SCHEME://$R" 302
    worker "$R" "/studio/auth/login"       "$SCHEME://$R" 302
    worker "$S" "/auth/phone/login"        "$SCHEME://$S" 302
    worker "$R" "/$STORE/auth/phone/login" "https://evil.example" 403
  fi
fi

# ‏(‏2026-11-11، مُراجَعَةُ إغلاقِ W13-4 · SEC-C2c-2 وSEC-C2c-R3-2 وC2c-P2-04 — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه) **مُصافَحَةُ WebSocket مِن أَصلٍ آخَرَ تُرَدّ** (ADR-050 §١٥):
# كانَت `GET` فَيُمَرِّرُها فَحصُ `Origin`، فَمُضيفٌ شَقيقٌ يَفتَحُ `/realtime` بِكَعكَةِ الضَحِيَّةِ ويَنضَمُّ إلى مَجموعَتِه (101). مُصافَحَةٌ خامٌ بِلا كَعكَة: مِن مُضيفٍ شَقيقٍ
# ومِن أَصلٍ غَريبٍ 403، ومِن أَصلِها 101 (عَلى المُضيفِ وعَلى الجَذر). **لا يَكتُبُ شَيئاً.**
echo "١٨) مُصافَحَةُ WebSocket مِن أَصلٍ آخَرَ تُرَدُّ قَبلَ المَركَز، ومِن أَصلِها تُقبَل (C2z·2)"
ws() { # ws <host> <path> <origin> <want>
  local got; got="$(curl -s -m 3 -o /dev/null -w '%{http_code}' -H "Host: $1" -H "Origin: $3" -H 'Connection: Upgrade' -H 'Upgrade: websocket' \
    -H 'Sec-WebSocket-Version: 13' -H 'Sec-WebSocket-Key: dGhlIHNhbXBsZSBub25jZQ==' "$(url "$1" "$2")" 2>/dev/null)"
  checked=$((checked + 1))
  if [ "$got" = "$4" ]; then printf '  ✓ %-4s %-34s %-52s → %s\n' WS "$1" "$2 · Origin: $3" "$got"
  else failed=$((failed + 1)); printf '  ✗ %-4s %-34s %-52s → الرَمز %s لا %s\n' WS "$1" "$2 · Origin: $3" "$got" "$4"; fi
}
ws "$S" "/realtime?slug=$STORE" "$SCHEME://$(hp "$OTHER.$DOMAIN")" 403
ws "$S" "/realtime?slug=$STORE" "https://evil.example"           403
ws "$S" "/_blazor"              "$SCHEME://$(hp "$OTHER.$DOMAIN")" 403
ws "$S" "/realtime?slug=$STORE" "$SCHEME://$S"                   101
ws "$R" "/realtime?slug=$STORE" "$SCHEME://$R"                   101
ws "$R" "/realtime?slug=$STORE" "$SCHEME://$S"                   403
# ‏(‏2026-11-11، مُراجَعَةُ إغلاقِ W13-4 الجَولَةُ الأُولى · SEC-C2z-R1-1 — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه) **والمُصافَحَةُ هِيَ الطَريقُ الوَحيد**: كانَ الاستِطلاعُ الطَويلُ
# (`GET /realtime?id=…` بِلا تَرقِيَة) يَمُرُّ بِكَعكَةِ الضَحِيَّةِ مِن صَفحَةِ مُضيفٍ شَقيقٍ ولا يُسأَلُ Origin، فَيَنضَمُّ اتِّصالُ المُهاجِمِ إلى مَجموعَتِه (مَقيسٌ عِندَ `61dab36`).
# الآن `negotiate` يَعرِضُ WebSockets وَحدَه لِلمَركَزَين (`LiveConnections`)، والاستِطلاعُ مِن أَصلٍ شَقيقٍ ← 404 قَبلَ المَركَز. **لا يَكتُبُ شَيئاً** (اتِّصالٌ في الذاكِرَةِ بِلا نَقل).
negotiated() { # negotiated <host> <hub> <query-prefix> — النَواقِلُ المَعروضَةُ WebSockets بِالضَبط، والاستِطلاعُ الطَويلُ بِأَصلٍ شَقيقٍ 404
  local body list tok got
  body="$(curl -s -m 10 -X POST -H "Host: $1" -H "Origin: $SCHEME://$1" "$(url "$1" "$2/negotiate?$3negotiateVersion=1")")"
  list="$(printf '%s' "$body" | grep -oE '"transport":"[A-Za-z]+"' | sed 's/"transport":"//; s/"$//' | tr '\n' ' ')"
  checked=$((checked + 1))
  if [ "$list" = "WebSockets " ]; then printf '  ✓ %-4s %-34s %-52s → %s\n' POST "$1" "$2/negotiate" "[${list% }]"
  else failed=$((failed + 1)); printf '  ✗ %-4s %-34s %-52s → النَواقِل [%s] لا [WebSockets]\n' POST "$1" "$2/negotiate" "${list% }"; fi
  tok="$(printf '%s' "$body" | sed -n 's/.*"connectionToken":"\([^"]*\)".*/\1/p')"
  got="$(curl -s -m 5 -o /dev/null -w '%{http_code}' -H "Host: $1" -H "Origin: $SCHEME://$(hp "$OTHER.$DOMAIN")" "$(url "$1" "$2?$3id=$tok")")" || got=000
  checked=$((checked + 1))
  if [ -n "$tok" ] && [ "$got" = 404 ]; then printf '  ✓ %-4s %-34s %-52s → %s\n' GET "$1" "$2?id=… · استِطلاعٌ مِن $OTHER" "$got"
  else failed=$((failed + 1)); printf '  ✗ %-4s %-34s %-52s → الرَمز %s لا 404\n' GET "$1" "$2?id=… · استِطلاعٌ مِن $OTHER" "$got"; fi
}
negotiated "$S" "/realtime" "slug=$STORE&"
negotiated "$S" "/_blazor"  ""
negotiated "$R" "/realtime" "slug=$STORE&"

# ‏(‏2026-11-11، مُراجَعَةُ إغلاقِ W13-4 · SEC-C2a-5 وSEC-C2a-R3-1 وSEC-C2b-R3-3 — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه) **التَحويلُ مُؤَقَّتٌ ومُخَطَّطُه مِن قائِمَة، والمُضيفُ
# المُختَلَقُ 400**: صُفوفُ مَناطِقِ المَنَصَّةِ أَعلاه صارَت 302 (كانَت 301 — يَحفَظُه المُتَصَفِّحُ بِلا انقِضاءٍ و`/terms` عَلى المُضيفِ سُؤالٌ مَفتوح)؛ و`X-Forwarded-Proto`
# غَريبٌ لا يَصيرُ مُخَطَّطَ الوِجهَة؛ و`Host` بِـpunycode لا يُفَكُّ أَو بِمَنفَذٍ فَوقَ 65535 ← 400 لا 500 عَلى المُضيفِ وعَلى الجَذر. **لا يَكتُبُ شَيئاً.**
echo "١٩) التَحويلُ مُؤَقَّتٌ ومُخَطَّطُه مِن {http, https}، والمُضيفُ المُختَلَقُ 400 (C2z·3)"
if [ "$DIRECT" != 1 ]; then
xfp() { # xfp <proto> <want-location>
  local got loc; got="$(curl -s -m 20 -o /dev/null -D "$TMP/xfp" -w '%{http_code}' -H "Host: $S" -H "X-Forwarded-Proto: $1" "$(url "$S" /studio)")" || got=000
  loc="$(tr -d '\r' < "$TMP/xfp" | sed -n 's/^[Ll]ocation: //p')"
  checked=$((checked + 1))
  if [ "$got" = 302 ] && [ "$loc" = "$2" ]; then printf '  ✓ %-4s %-34s %-52s → %s\n' GET "$S" "/studio · X-Forwarded-Proto: $1" "$got $loc"
  else failed=$((failed + 1)); printf '  ✗ %-4s %-34s %-52s → %s %s\n' GET "$S" "/studio · X-Forwarded-Proto: $1" "$got" "$loc"; fi
}
xfp javascript "https://$R/studio"
xfp data       "https://$R/studio"
row GET  "$(hp "xn--zz.$DOMAIN")"        "/"            400
row GET  "$(hp "xn--mgbh0fb-vendor.$DOMAIN")" "/"       400
row GET  "xn--zz"                        "/$STORE"      400
row GET  "$DOMAIN:99999"                 "/$STORE"      400
row GET  "$STORE.$DOMAIN:70000"          "/"            400
# ‏(‏2026-11-11، مُراجَعَةُ إغلاقِ W13-4 الجَولَةُ الأُولى · SEC-C2z-R1-2 وC2z-PATHS-R1-03 — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه) **وخَلفَ العامِلِ الاسمُ في الرَأسِ المُمَرَّر**:
# DEPLOY الخُطوَةُ ٦ (`ForwardedHeaders__AllowedHosts=R,*.R`) تَجعَلُ اسمَ كُلِّ زائِرٍ في `X-Forwarded-Host`، والإطارُ كانَ يَفُكُّه قَبلَ الحارِسِ فَـ`xn--zz.R` ← 500
# (مَقيسٌ عِندَ `61dab36`). الآن 400 قَبلَ أَن يُقرَأ، والاسمُ الصالِحُ يَبلُغُ كَما كان. ومَعَ `ALLOWED=1` يُرَدُّ الاسمُ الداخِلِيُّ قَبلَها فَلا تُقاس. **لا يَكتُبُ شَيئاً.**
if [ "${WORKER:-0}" = 1 ] && [ "$ALLOWED" != 1 ]; then
  worker "$(hp "xn--zz.$DOMAIN")"             "/$STORE" "" 400 GET
  worker "$(hp "xn--mgbh0fb-vendor.$DOMAIN")" "/"       "" 400 GET
  worker "$S, $(hp "xn--zz.$DOMAIN")"         "/"       "" 400 GET
  worker "$S"                                 "/"       "" 200 GET
fi
else
  echo "  – لَم تُفحَص: X-Forwarded-Proto يَكتُبُه Cloudflare، والمُضيفُ المُختَلَقُ لا يَحُلُّه DNS — مَحَلِّيّاً وَحدَه"
fi

# ‏(‏2026-11-11، مُراجَعَةُ إغلاقِ W13-4 · SEC-C2a-4 وSEC-C2a-R2-4 — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه) **المِلصَقُ المَحجوزُ لا يَخدِمُ المَنَصَّة**: كانَ `admin.R`
# و`studio.R` و`api.R` … نُسَخاً كامِلَةً مِن المَنَصَّة (دُخولُ الاستوديو والإشراف ومَتاجِرُ المَسار). الآن 404، و`www` يَمُرُّ إلى الجَذرِ كَما كان. **لا يَكتُبُ شَيئاً.**
echo "٢٠) المِلصَقاتُ المَحجوزَةُ تَحتَ النِطاقِ لا تَخدِمُ المَنَصَّة، و www كَما كان (C2z·4)"
for L in admin studio api cdn blog support; do
row GET  "$(hp "$L.$DOMAIN")"            "/"                 404
done
row GET  "$(hp "admin.$DOMAIN")"         "/studio/auth"      404
row GET  "$(hp "studio.$DOMAIN")"        "/$STORE"           404
row GET  "$(hp "www.$DOMAIN")"           "/"                 200

# ‏(‏2026-11-11، مُراجَعَةُ إغلاقِ W13-4 · SEC-C2b-3 وC2b-P3-01 — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه) **مُضيفُ الدَورِ لا يُفَهرَس**: كانَت خَريطَتُه تَسرُدُ واجِهَةَ المَتجَرِ تَحتَ
# اسمِه بِـcanonical ذاتِيّ (نُسخَةٌ لِكُلِّ دَور). الآن robots يَمنَعُ كُلَّ شَيءٍ بِلا خَريطَة، والخَريطَةُ فارِغَة، وكُلُّ جَوابٍ بِـ`X-Robots-Tag: noindex`؛ ومُضيفُ المَتجَرِ كَما كان.
# **لا يَكتُبُ شَيئاً.**
echo "٢١) مُضيفُ الدَورِ لا يُفَهرَس، ومُضيفُ المَتجَرِ النُسخَةُ الوَحيدَة (C2z·5)"
if [ -n "$ROLE" ]; then
row GET  "$RH" "/robots.txt"             200 - "Disallow: /" "!Sitemap:"
row GET  "$RH" "/sitemap.xml"            200 - "urlset" "!<loc>"
row GET  "$S"  "/robots.txt"             200 - "Sitemap: $SCHEME://$S/sitemap.xml"
tag() { # tag <host> <want: noindex|none>
  local got; got="$(curl -s -m 20 -o /dev/null -D - -H "Host: $1" "$(url "$1" /)" | tr -d '\r' | sed -n 's/^[Xx]-[Rr]obots-[Tt]ag: //p' | head -1)"
  checked=$((checked + 1))
  if [ "${got:-none}" = "$2" ]; then printf '  ✓ %-4s %-34s %-52s → %s\n' GET "$1" "/ · X-Robots-Tag" "${got:-none}"
  else failed=$((failed + 1)); printf '  ✗ %-4s %-34s %-52s → «%s» لا «%s»\n' GET "$1" "/ · X-Robots-Tag" "${got:-none}" "$2"; fi
}
tag "$RH" noindex
tag "$S"  none
fi

# ‏(‏2026-11-11، مُراجَعَةُ إغلاقِ W13-4 · SEC-C2a-R2-2 — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه) **قِراءَةُ أَيقونَةِ الـPWA قَبلَ ضَبطِ النِطاقِ هِيَ هذِه** (DEPLOY §٢·ي
# الخُطوَةُ ٨، وADR-050 §٥): كانَت `curl -sI` — طَلَبَ HEAD تُجيبُه النُقطَةُ (`MapGet`) 405 بِلا CSP، فَتَقولُ «قَبلَ الإصلاح» عَلى بِناءٍ مُصلَحٍ أَبَداً. الآن GET
# بِرُؤوسِه، وأَعلامُه `PWA_READ` هِيَ ما في الدَليلِ حَرفاً (`GoLiveReadsAndWiringTests` يَحمَرُّ إن افتَرَقا). **لا يَكتُبُ شَيئاً.**
echo "٢٢) قِراءَةُ أَيقونَةِ الـPWA قَبلَ ضَبطِ النِطاق: GET بِرُؤوسِه عَلى الجَذر (C2z·7)"
PWA_READ=(-s -D - -o /dev/null)
pwa() { # pwa <host> <want>
  local got; got="$(curl -m 20 "${PWA_READ[@]}" -H "Host: $1" "$(url "$1" "/api/$STORE/icon.svg")" | tr -d '\r' | grep -i '^content-security-policy' | sed 's/^[^:]*: //' | head -1)"
  checked=$((checked + 1))
  if [ "$got" = "$2" ]; then printf '  ✓ %-4s %-34s %-52s → %s\n' GET "$1" "/api/$STORE/icon.svg · CSP" "$got"
  else failed=$((failed + 1)); printf '  ✗ %-4s %-34s %-52s → «%s» لا «%s»\n' GET "$1" "/api/$STORE/icon.svg · CSP" "${got:-لا شَيء}" "$2"; fi
}
pwa "$R" "default-src 'none'; sandbox"

# ‏(‏2026-11-11، مُراجَعَةُ إغلاقِ W13-4 الجَولَةُ الأُولى · SEC-C2z-R1-3 — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه) **دُخولُ غَيرِ المُديرِ عَلى مُضيفِ الإدارَةِ يُرَدُّ ولا يُكتَب**:
# كانَت الدَعوى تُسقَطُ فَتُكتَبُ الكَعكَةُ العامَّةُ وَحدَها — ولا صَفحَةَ عَلى `{store}-admin` تَقرَؤُها، فَكُلُّ دُخولٍ يَنجَحُ والتالِيَةُ تَطلُبُ الدُخول (مَقيسٌ عِندَ
# `61dab36`: `/me/name` ثُمَّ `/me/role` ثُمَّ `/me`). الآن 302 إلى `/login?err=not_store_admin` بِلا كَعكَة، وصَفحَتُه رَفضٌ بِلا استِمارَةٍ ومَخرَجُه المَتجَرُ عَلى مُضيفِه؛
# والمُديرُ (`MANAGER_PHONE`) يَدخُلُ ويَبلُغُ `/manage`. **يَكتُبُ دُخولاً** (ومُستَخدِماً جَديداً لِـ`GUEST_PHONE` إن لَم يَكُن) فَلا يَجري إلّا بِـ`GUEST_PHONE`.
echo "٢٣) دُخولُ غَيرِ المُديرِ عَلى مُضيفِ الإدارَةِ يُرَدُّ بِمَخرَجٍ ولا يُكتَب، والمُديرُ يَبلُغُ /manage (C2z-R1)"
AH="$(hp "$STORE-admin.$DOMAIN")"
if [ -n "$GUEST_PHONE" ] && [ "$(curl -s -m 10 -o /dev/null -w '%{http_code}' -H "Host: $AH" "$(url "$AH" /)")" = 200 ]; then
  admin_signin() { # admin_signin <jar> <phone> — يَطبَعُ «رَمزُ التَحَقُّق|الوِجهَة»
    curl -s -m 20 -o /dev/null -c "$1" -b "$1" -H "Host: $AH" --data-urlencode "phone=$2" --data-urlencode "as=tenant_admin" "$(url "$AH" /auth/phone/login)" >/dev/null
    curl -s -m 20 -o /dev/null -D "$TMP/ah" -w '%{http_code}' -c "$1" -b "$1" -H "Host: $AH" --data-urlencode "phone=$2" --data-urlencode "code=$DEV_CODE" \
      --data-urlencode "as=tenant_admin" "$(url "$AH" /auth/phone/verify)"
    printf '|%s' "$(tr -d '\r' < "$TMP/ah" | sed -n 's/^[Ll]ocation: //p' | head -1)"
  }
  AJ="$TMP/ajar"; rm -f "$AJ"
  g="$(admin_signin "$AJ" "$GUEST_PHONE")"
  checked=$((checked + 1))
  if [ "${g%%|*}" = 302 ] && [ "${g#*|}" = "/login?err=not_store_admin" ]; then printf '  ✓ %-4s %-34s %-52s → %s\n' POST "$AH" "/auth/phone/verify · غَيرُ مُدير" "${g%%|*} ${g#*|}"
  else failed=$((failed + 1)); printf '  ✗ %-4s %-34s %-52s → %s\n' POST "$AH" "/auth/phone/verify · غَيرُ مُدير" "${g%%|*} ${g#*|} — لا /login?err=not_store_admin"; fi
  left="$(grep -c "acommerce\.auth\.$STORE" "$AJ" || true)"
  checked=$((checked + 1))
  if [ "$left" = 0 ]; then printf '  ✓ %-4s %-34s %-52s → %s\n' JAR "$AH" "كَعكاتُ $STORE بَعدَ الرَفض" 0
  else failed=$((failed + 1)); printf '  ✗ %-4s %-34s %-52s → %s\n' JAR "$AH" "كَعكاتُ $STORE بَعدَ الرَفض" "$left"; fi
  JAR="$AJ"
  row GET "$AH" "/login?err=not_store_admin" 200 - "href=\"$SCHEME://$S/\"" "href=\"/login\"" "!<form" "!name=\"code\""
  JAR=""
  if [ -n "$MANAGER_PHONE" ]; then
    MJ="$TMP/mjar"; rm -f "$MJ"
    g="$(admin_signin "$MJ" "$MANAGER_PHONE")"
    checked=$((checked + 1))
    # الوِجهَةُ /manage — أَو خُطوَةُ الاسمِ قَبلَها لِحِسابٍ اسمُه افتِراضِيّ (د40 «ج»)، والصَفُّ التالي يَقيسُ أَنّ /manage تُفتَحُ بِالجَرَّة.
    if [ "${g%%|*}" = 302 ] && { [ "${g#*|}" = "/manage" ] || [ "${g#*|}" = "/me/name?next=%2Fmanage" ]; }; then printf '  ✓ %-4s %-34s %-52s → %s\n' POST "$AH" "/auth/phone/verify · المُدير" "${g%%|*} ${g#*|}"
    else failed=$((failed + 1)); printf '  ✗ %-4s %-34s %-52s → %s\n' POST "$AH" "/auth/phone/verify · المُدير" "${g%%|*} ${g#*|} — لا /manage"; fi
    JAR="$MJ"
    row GET "$AH" "/manage" 200 - "/admin/tenants/$STORE/categories?from=tenant"
    JAR=""
  else
    echo "  – صَفُّ المُديرِ لَم يُفحَص: MANAGER_PHONE غائِب"
  fi
else
  echo "  – لَم تُفحَص: GUEST_PHONE غائِب، أَو $STORE بِلا دَورِ إدارَة (مُضيفُه لا يُجيب 200)"
fi

echo
echo "الصُفوفُ المَفحوصَة: $checked · فَشِلَ: $failed"
if [ "$checked" -eq 0 ]; then echo "✗ فَحصٌ أَعمى: صِفرُ صَفّ." >&2; exit 1; fi
[ "$failed" -eq 0 ] || exit 1
exit 0
