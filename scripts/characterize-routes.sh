#!/usr/bin/env bash
# ═══════════════════════════════════════════════════════════════════════
#  تَوصيف المَسارات — «هَل تَغَيَّرَ سُلوك مَسارٍ واحِد؟»
# ───────────────────────────────────────────────────────────────────────
#  يَطبَع لِكُلّ مَسار: الفِعل، رَمز الحالَة، نَوع المُحتَوى، وحَجم
#  الجِسم بِالبايت. لا شَيءَ آخَر — فَالعُنوان والتاريخ يَتَغَيَّرانِ
#  بَينَ طَلَبَين مُتَتالِيَين ولَيسا سُلوكاً.
#
#  **لِماذا يُوجَد**: البَند ١ يُفَعِّل كَشف المُستَأجِر مِن وَسيط
#  المَسار في Wolverine.Http. والخَطَر المُعلَن أَنّ مَساراتٍ **لا
#  تَحمِل `slug`** (‏`/robots.txt`, `/sitemap.xml`, `/admin/…`,
#  `/studio/…`, `/api/push/vapid-key`, والجَذر) تَنكَسِر صامِتَةً.
#  فَالمُقارَنَة قَبل/بَعد هي البُرهان، لا الظَنّ بِأَنّ «الإعداد
#  يَخُصّ Wolverine وَحدَها».
#
#  ويَشمَل المُصَفوفَة عَمداً **نُقطَتَي Wolverine الوَحيدَتَين بِلا
#  slug** (‏`/robots.txt`, `/sitemap.xml`) — فَهُما بِالضَبط ما
#  يَكسِرُه `AssertExists()` لَو فُعِّل بِلا استِثناء مُعلَن.
#
#  الاستِعمال:
#     scripts/characterize-routes.sh [BASE_URL] > before.txt
# ═══════════════════════════════════════════════════════════════════════
set -uo pipefail

BASE_URL="${1:-http://localhost:5050}"

# "الفِعل المَسار" — والتَعليق بَعد '#' لِلقارِئ لا لِلأَداة.
ROUTES=(
  # ─── الجَذر وما لا يَحمِل slug ───────────────────────────────────
  "GET  /"
  "GET  /robots.txt"                      # نُقطَة Wolverine بِلا slug
  "GET  /sitemap.xml"                     # نُقطَة Wolverine بِلا slug
  "GET  /api/push/vapid-key"
  "GET  /admin"
  "GET  /admin/tenants/new"
  "GET  /admin/monitor"
  "GET  /studio"
  "GET  /studio/new"
  "GET  /studio/auth"
  "GET  /favicon.ico"

  # ─── مَسارات المُستَأجِر ─────────────────────────────────────────
  "GET  /ashare"
  "GET  /ashare/explore"
  "GET  /ashare/plans"
  "GET  /ashare/legal"
  "GET  /ashare/login"
  "GET  /theme-demo"
  "GET  /adwar-demo"
  "GET  /zz-no-such-tenant"               # سلاج غَير مَوجود
  "GET  /ashare/api/me/unread"

  # ─── نُقطَتا Wolverine الكاتِبَتانِ (‏تَحمِلانِ slug) ─────────────
  "POST /ashare/auth/nafath/request"
  "POST /ashare/auth/nafath/verify"
  "POST /zz-no-such-tenant/auth/nafath/request"

  # ─── نِقاط minimal API مُختارَة (‏لا يَجوز أَن تَتَأَثَّر) ────────
  "POST /admin/tenants/create"
  "POST /ashare/auth/phone/login"
  "POST /lang/ar"
  "POST /studio/begin"
)

printf '%-6s %-45s %-5s %-34s %s\n' "VERB" "ROUTE" "CODE" "CONTENT-TYPE" "BYTES"
printf '%s\n' "──────────────────────────────────────────────────────────────────────────────────────────────────────"

count=0
for entry in "${ROUTES[@]}"; do
  verb="${entry%% *}"
  path="${entry#* }"
  path="${path## }"
  # يُقتَطَع التَعليق إن وُجِد (‏المَسار لا يَحوي مَسافَة).
  path="${path%% *}"

  body="$(mktemp)"
  if [ "$verb" = "POST" ]; then
    read -r code ctype < <(curl -s -o "$body" -w '%{http_code} %{content_type}\n' \
      -X POST -H 'Content-Type: application/x-www-form-urlencoded' --data '' \
      "$BASE_URL$path")
  else
    read -r code ctype < <(curl -s -o "$body" -w '%{http_code} %{content_type}\n' \
      "$BASE_URL$path")
  fi
  bytes="$(wc -c < "$body" | tr -d ' ')"
  rm -f "$body"

  printf '%-6s %-45s %-5s %-34s %s\n' "$verb" "$path" "$code" "${ctype:-—}" "$bytes"
  count=$((count + 1))
done

printf '%s\n' "──────────────────────────────────────────────────────────────────────────────────────────────────────"
# حارِس العَمى (‏القاعِدَة ١٠): أَداةٌ وَصَفَت صِفر مَسارات لَيسَت
# تَوصيفاً، ومُقارَنَةُ مِلَفَّين فارِغَين تُعطي «لا فَرق» دائِماً.
printf 'المَسارات المَوصوفَة: %s\n' "$count"
if [ "$count" -lt 20 ]; then
  printf '✗ فَحصٌ أَعمى: %s مَسار فَقَط.\n' "$count" >&2
  exit 1
fi
