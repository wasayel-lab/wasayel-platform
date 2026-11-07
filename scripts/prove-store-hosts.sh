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
JAR=""                            # جَرَّةُ كَعكاتٍ تُرسَلُ مَعَ الصُفوفِ وَقتَ المَجموعَةِ ٦ وَحدَها
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
  [ "$method" = POST ] && args+=(-H 'Content-Type: application/x-www-form-urlencoded' --data '')
  local got; got="$(curl "${args[@]}" "$(url "$host" "$path")")" || got="000|"
  local code="${got%%|*}" redirect="${got#*|}" why=""
  [ "$code" = "$want" ] || why="الرَمز $code لا $want"
  if [ -z "$why" ] && [ "$loc" != "-" ]; then
    # curl يَحُلُّ الوِجهَةَ نِسبَةً إلى 127.0.0.1 — والمُقارَنَةُ بِالوِجهَةِ الكامِلَةِ مِن رَأسِ Location
    redirect="$(curl -s -m 20 -o /dev/null -D - "${verb[@]}" "${jar[@]}" -H "Host: $host" "$(url "$host" "$path")" | tr -d '\r' | sed -n 's/^[Ll]ocation: //p')"
    [ "$redirect" = "$loc" ] || why="الوِجهَة «$redirect» لا «$loc»"
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
row GET  "$S" "/api/$STORE/manifest.json" 200
row GET  "$S" "/api/push/vapid-key"     200

echo "٣) مَناطِقُ المَنَصَّةِ تَعودُ إلى الجَذر، والكِتابَةُ إلَيها لا تُقبَل"
row GET  "$S" "/studio?from=store"      301 "$SCHEME://$R/studio?from=store"
row GET  "$S" "/admin"                  301 "$SCHEME://$R/admin"
row GET  "$S" "/pricing"                301 "$SCHEME://$R/pricing"
row HEAD "$S" "/privacy"                301 "$SCHEME://$R/privacy"
row POST "$S" "/studio/apps/create"     404
# ‏(‏2026-11-07، مُراجَعَةُ C2a الجَولَةُ الأُولى · SEC-C2a-2) المَسارُ المُرَمَّزُ يُحَوَّلُ بِوِجهَةٍ مُرَمَّزَة — كانَ 500 («م» حَرفاً في Location)، و%3F يَنقَلِبُ استِعلاماً
row GET  "$S" "/studio/%D9%85"          301 "$SCHEME://$R/studio/%D9%85"
row GET  "$S" "/admin/%0d%0aSet-Cookie:x=1" 301 "$SCHEME://$R/admin/%0D%0ASet-Cookie:x=1"
row GET  "$S" "/studio/%3F%3Fx"         301 "$SCHEME://$R/studio/%3F%3Fx"

echo "٤) ما لا يَخُصُّ المَتجَرَ لا يُبلَغُ مِن مُضيفِه"
row GET  "$S" "/api/$OTHER/unread-counts" 404
row GET  "$S" "/api/v1/deals"           404
# ‏(‏2026-11-07، مُراجَعَةُ C2a الجَولَةُ الثانِيَة · SEC-C2a-R2-1) سَطحُ مَتجَرٍ اسمُه push لا يُخدَمُ عَلى مُضيفِ غَيرِه (كانَ 200 بِبَيانِه)، ولا مُضيفَ لِـpush وv1
# (كانَ v1.R/api/v1/deals يَبلُغُ API المَنَصَّة) — والثَلاثَةُ تَصدُقُ بِمَتجَرٍ بِذلك الاسمِ وبِدونِه
row GET  "$S" "/api/push/manifest.json" 404
row GET  "$(hp "push.$DOMAIN")" "/api/push/manifest.json" 404
row GET  "$(hp "v1.$DOMAIN")" "/api/v1/deals" 404
row GET  "$(hp "$STORE-vendor.$DOMAIN")" "/"   404
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
  row GET "$S" "/admin/tenants/$OTHER/categories?from=tenant" 301 "$SCHEME://$R/admin/tenants/$OTHER/categories?from=tenant"
  JAR=""
else
  echo "  – لَم تُفحَص: MANAGER_PHONE غائِب (هاتِفُ مُديرٍ لِـ$STORE، وDevelopment لِرَمزِ المُحاكي)"
fi

echo
echo "الصُفوفُ المَفحوصَة: $checked · فَشِلَ: $failed"
if [ "$checked" -eq 0 ]; then echo "✗ فَحصٌ أَعمى: صِفرُ صَفّ." >&2; exit 1; fi
[ "$failed" -eq 0 ] || exit 1
exit 0
