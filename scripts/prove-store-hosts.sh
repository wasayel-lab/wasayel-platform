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
JAR=""                            # جَرَّةُ كَعكاتٍ تُرسَلُ مَعَ الصُفوفِ وَقتَ المَجموعَةِ ٦ وَحدَها
REF=""                            # رَأسُ Referer يُرسَلُ مَعَ الصُفوفِ وَقتَ المَجموعَةِ ٧ وَحدَها
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
  row GET "$S" "/admin/tenants/$OTHER/categories?from=tenant" 301 "$SCHEME://$R/admin/tenants/$OTHER/categories?from=tenant"
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
row GET  "$RH" "/studio"                 301 "$SCHEME://$R/studio"
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
  signin_b() { # signin_b <host>
    local g1 g2
    g1="$(go POST "$1" "/auth/phone/login" --data-urlencode "phone=$ROLE_PHONE" --data-urlencode "as=$ROLE")"
    g2="$(go POST "$1" "/auth/phone/verify" --data-urlencode "phone=$ROLE_PHONE" --data-urlencode "code=$DEV_CODE" --data-urlencode "as=$ROLE")"
    mark "$([ "${g1%%|*}" = 302 ] && [ "${g2%%|*}" = 302 ] && echo 1 || echo 0)" "دُخولٌ بِـ$ROLE عَلى $1" "${g1%%|*} ← ${g2%%|*}"
  }
  # ‏state <host> → in (صَفحَةُ /me لِهذا المَتجَرِ بِجَلسَة) · out (صَفحَتُه تَطلُبُ الدُخول، أَو تَحويلٌ إلى دُخول) · وما سِواهُما رَمزُه — فَصَفحَةٌ لَيسَت لِلمَتجَرِ
  # (404، أَو قِشرَةُ المَنَصَّة) لا تُقرَأُ «داخِلاً» ولا «خارِجاً» (مَقيس: بِلا نِطاقٍ كانَت قِشرَةُ المَنَصَّةِ تُقرَأُ «بِجَلسَة»).
  state() {
    local g code; g="$(go GET "$1" "/me")"; code="${g%%|*}"
    if [ "$code" = 200 ] && grep -qF "\"slug\":\"$STORE\"" "$TMP/gobody"; then
      if grep -qE 'href="[^"]*/login(\?[^"]*)?"' "$TMP/gobody"; then echo out; else echo in; fi
    elif [ "$code" = 302 ] && [[ "${g#*|}" == */login* ]]; then echo out
    else echo "$code"; fi
  }
  inside() { [ "$(state "$1")" = in ] && echo 1 || echo 0; }
  outside() { [ "$(state "$1")" = out ] && echo 1 || echo 0; }
  ours() { # ours <host[:port]> → 1 إن كانَ مُضيفاً لِهذا المَتجَرِ تَحتَ النِطاق
    local n="${1%%:*}"
    case "$n" in "$STORE.$DOMAIN"|"$STORE"-*."$DOMAIN") echo 1 ;; *) echo 0 ;; esac
  }
  # حَدُّ التَحَقُّقِ في المُنتَجِ خَمسٌ في الدَقيقَةِ لِكُلِّ جَوّالٍ ومَتجَر (`AuthHandlers.MaxVerifyPerMinute`)، والمَجموعَةُ ١٠ تَحَقَّقَت مَرَّتَين: فَالجَولَةُ الثانِيَةُ
  # تَنتَظِرُ النافِذَةَ — وإلّا رُدَّ دُخولُها بِحَدِّ المُنتَجِ لا بِعَيبٍ فيه (مَقيس: «قَبلَ الخُروج … بِجَلسَة ✗» في الدُخولِ السادِس).
  for START in "$S" "$RH"; do
    rm -f "$B"
    [ "$START" = "$RH" ] && { echo "  … انتِظارُ نافِذَةِ حَدِّ التَحَقُّق (61 ثانِيَة)"; sleep 61; }
    signin_b "$RH"; signin_b "$S"
    mark "$(inside "$RH")" "قَبلَ الخُروج: $RH/me بِجَلسَة" ""
    mark "$(inside "$S")"  "قَبلَ الخُروج: $S/me بِجَلسَة" ""
    if [ "$START" = "$RH" ]; then
      # ومَحَطَّةُ مُرورٍ بِتَذكِرَةٍ مُختَلَقَةٍ لا تُخرِجُ أَحَداً — والجَلسَةُ قائِمَةٌ قَبلَها وبَعدَها
      g="$(go GET "$RH" "/$STORE/auth/logout/continue?t=bG9nb3V0LWhvcHx4")"
      mark "$([ "${g%%|*}" = 302 ] && [ "${g#*|}" = "/" ] && [ "$(inside "$RH")" = 1 ] && echo 1 || echo 0)" \
           "تَذكِرَةٌ مُختَلَقَةٌ عَلى $RH لا تُخرِجُ أَحَداً" "${g%%|*} → ${g#*|}"
    fi
    g="$(go POST "$START" "/$STORE/auth/logout" -H 'Content-Type: application/x-www-form-urlencoded' --data '')"
    hops=0; bad=""; final="$SCHEME://$START/"
    while [ "${g%%|*}" = 302 ] && [ "${g#*|}" != "$final" ] && [ "$hops" -lt 15 ]; do
      loc="${g#*|}"; rest="${loc#*://}"; hp_="${rest%%/*}"; p_="/${rest#*/}"
      [ "$(ours "$hp_")" = 1 ] || { bad="وِجهَةٌ خارِجَ مُضيفاتِ المَتجَر: $loc"; break; }
      hops=$((hops + 1)); g="$(go GET "$hp_" "$p_")"
    done
    mark "$([ -z "$bad" ] && [ "${g%%|*}" = 302 ] && [ "${g#*|}" = "$final" ] && [ "$hops" -ge 1 ] && echo 1 || echo 0)" \
         "الخُروجُ عَلى $START ← يَنتَهي في $final" "مَحَطّاتُ المُرور: $hops ${bad:-· آخِرُها ${g%%|*} → ${g#*|}}"
    mark "$(outside "$RH")" "بَعدَ الخُروج: $RH/me يَطلُبُ الدُخول" ""
    mark "$(outside "$S")"  "بَعدَ الخُروج: $S/me يَطلُبُ الدُخول" ""
    left="$(grep -c "acommerce\.auth\.$STORE" "$B" || true)"
    mark "$([ "$left" = 0 ] && echo 1 || echo 0)" "بَعدَ الخُروج: كَعكاتُ جَلسَةِ $STORE في الجَرَّة" "$left"
  done
  rm -f "$B"
else
  echo "  – لَم تُفحَص: ROLE أَو ROLE_PHONE غائِب (هاتِفٌ يَدخُلُ بِدَورِ ROLE، وDevelopment لِرَمزِ المُحاكي)"
fi

echo
echo "الصُفوفُ المَفحوصَة: $checked · فَشِلَ: $failed"
if [ "$checked" -eq 0 ]; then echo "✗ فَحصٌ أَعمى: صِفرُ صَفّ." >&2; exit 1; fi
[ "$failed" -eq 0 ] || exit 1
exit 0
