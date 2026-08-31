#!/usr/bin/env bash
# ═══════════════════════════════════════════════════════════════════════
#  walk-after-the-key.sh — الخُطُواتُ الخَمسُ الباقِيَةُ بَعدَ الجِدار
# ═══════════════════════════════════════════════════════════════════════
#
#  **الغَرَض**: يُنَفَّذُ **فَورَ** وُصولِ مِفتاحِ نَموذَجِ اللُغَة إلى
#  `scripts/.prod-gate.env`، فَيَمشي الرِحلَةَ مِن أَوَّلِها إلى آخِرِها
#  بِـ`curl` وَحدَه — **بِلا اكتِشافٍ جَديد، وبِلا لَمسَةِ مالِك**.
#
#  **ما قَبلَ هذا السكربت مَقيسٌ حَيّاً (‏2026-08-31، بِلا مِفتاح)**:
#  الخُطُواتُ ١–٣ مَشَت كامِلَةً وسَقَطَت عِندَ التَحليلِ بِـ
#  `anthropic (https://api.anthropic.com/) 401` — أَي أَنّ كُلَّ ما
#  دونَ المِفتاحِ يَعمَل. وهذا السكربت يُكمِلُ الخَمسَ الباقِيَة.
#
#  **الاستِعمال**:
#      bash scripts/walk-after-the-key.sh                 # مَنفَذ 5099
#      BASE=http://localhost:5050 bash scripts/…          # مَنفَذٌ آخَر
#      MARK=20260901 bash scripts/…                       # وَسمُ البَذر
#
#  **وجِدارٌ ثانٍ وُجِدَ وأُزيلَ في نَفسِ الجَولَة**: نَشرُ الإعلانِ —
#  الخُطوَةُ الَّتي لا طَلَبَ بِدونِها — كانَ يَرتَدُّ **‏500** في كُلِّ
#  مَتجَر (‏`IEntitlements` مَحقونٌ في تَوقيعِ النُقطَةِ فَيُعطي آخِرَ
#  مُسَجَّل). أُصلِحَ ويَحرُسُه
#  `EntitlementContractTests.No_endpoint_takes_the_entitlement_interface_as_a_parameter`.
#  فَلا تُشَغِّل هذا السكربتَ على ثُنائيٍّ أَقدَمَ مِن ذلكَ الإصلاح.
#
#  **ولا يُطبَعُ سِرٌّ ولا جُزءٌ مِنه**: المِفتاحُ يُقرَأُ مِن البيئَةِ
#  ولا يُلمَسُ، والتَحَقُّقُ مِنه بِسَطرِ الإقلاعِ «المِفتاح=مَضبوط»
#  وَحدَه.
#
#  **ولا نِداءَ بِلا مُهلَة**: كُلُّ `curl` بِـ`--max-time`، وكُلُّ
#  انتِظارٍ بِسَقفِ دَوَرات. تَعليقٌ يَبدو عَمَلاً أَسوَأُ مِن خَطَأ.
# ═══════════════════════════════════════════════════════════════════════
set -uo pipefail

BASE="${BASE:-http://localhost:5099}"
MARK="${MARK:-$(date +%Y%m%d%H%M)}"
JAR="$(mktemp -d)/jar"; OWNER="$JAR.owner"; SHOPPER="$JAR.shopper"
T="${T:-25}"                            # مُهلَةُ كُلِّ نِداء (ثانِيَة)
ANALYZE_TRIES="${ANALYZE_TRIES:-60}"    # ‏60 × 5ث = 5 دَقائِق سَقفاً لِلتَحليل
STEP=0

# ─── أَدَواتُ الطَبع — والفَشَلُ صَريحٌ لا صامِت ───────────────────────
say()  { printf '\n\033[1m%s\033[0m\n' "$*"; }
ok()   { printf '   \033[32m✓\033[0m %s\n' "$*"; }
info() { printf '   · %s\n' "$*"; }
die()  { printf '\n\033[31m✗ تَوَقَّفَ عِندَ الخُطوَة %s: %s\033[0m\n' "$STEP" "$*" >&2; exit 1; }

# ‏POST يُعيدُ «الرَمز<TAB>الوِجهَة». الجارُّ `-b/-c` نَفسُ المِلَفِّ
# لِتَبقى الجَلسَةُ بَينَ الطَلَبات.
post() { # post <jar> <path> [--data-urlencode k=v …]
    local jar="$1" path="$2"; shift 2
    local h; h="$(mktemp)"
    local code; code="$(curl -s --max-time "$T" -o /dev/null -D "$h" \
        -c "$jar" -b "$jar" -X POST "$BASE$path" "$@" -w '%{http_code}')" || { rm -f "$h"; echo "000	"; return; }
    printf '%s\t%s\n' "$code" "$(grep -i '^location:' "$h" | tr -d '\r' | sed 's/^[Ll]ocation: //')"
    rm -f "$h"
}

get() { # get <jar> <path> <outfile> → يَطبَعُ الرَمز
    local jar="$1" path="$2" out="$3"
    curl -s --max-time "$T" -o "$out" -c "$jar" -b "$jar" "$BASE$path" -w '%{http_code}' 2>/dev/null || echo 000
}

# نَصُّ الصَفحَةِ مُقَطَّعاً عِندَ الوُسوم — لِلبَحثِ عَن عِبارَةٍ عَرَبِيَّة.
text() { tr '<' '\n' < "$1"; }

# ═══ ٠) قَبلَ كُلِّ شَيء — أَمَضبوطٌ المِفتاح؟ ══════════════════════════
STEP="٠ — بَوّابَةُ المِفتاح"
say "الخُطوَة ٠ · بَوّابَةُ المِفتاح"
[ -n "${AGENT_LOG:-}" ] || AGENT_LOG=""
if [ -n "$AGENT_LOG" ] && [ -f "$AGENT_LOG" ]; then
    if grep -q 'المِفتاح=مَضبوط' "$AGENT_LOG"; then
        ok "سَطرُ الإقلاع يَقول: المِفتاح=مَضبوط"
    else
        die "سَطرُ [agent] في $AGENT_LOG يَقولُ «غَير مَضبوط» — ضَعِ المِفتاحَ وأَعِدِ الإقلاعَ أَوَّلاً (§ التَقرير)."
    fi
else
    info "لَم يُمَرَّر AGENT_LOG — تُخَطّى البَوّابَة. (مَرِّرها: AGENT_LOG=/path/app.log)"
    info "والتَذكيرُ اللازِم: الخادِمُ يَخزُنُ المِفتاحَ لِعُمرِ العَمَلِيَّة — مِفتاحٌ يُضافُ والخادِمُ يَعمَلُ لا يُقرَأ."
fi

# ═══ ١) الدُخول — بِحِسابٍ عاديٍّ لا مُشرِف ════════════════════════════
STEP="١ — تَسجيلٌ وقَبولُ شُروطٍ وطَلَبُ تَحليل"
say "الخُطوَة ١ · تَسجيل ← شُروط ← تَحليل   (مُعادَةٌ لِيَبدَأَ السكربتُ مِن صَفحَةٍ أولى)"
rm -f "$OWNER"; EMAIL="agent-walk-$MARK@wasayel.test"
IDEA="مِنَصَّةُ تَأجيرِ مُعَدّاتِ المُقاوَلاتِ الصَغيرَةِ في الرِياضِ بِالساعَة"

read -r C L1 < <(post "$OWNER" /studio/begin --data-urlencode "prompt=$IDEA")
[ "$C" = 302 ] || die "‏/studio/begin رَدَّ $C لا 302."
ok "‏/studio/begin ⇐ 302 → $L1"

read -r C L2 < <(post "$OWNER" /studio/auth/email/login --data-urlencode "email=$EMAIL")
[ "$C" = 302 ] || die "‏/studio/auth/email/login رَدَّ $C."
# الرَمزُ في التَطويرِ ثابِتٌ مَعلوم ويُعرَضُ على الشاشَة — يُقرَأُ لا يُخمَّن.
V="$(mktemp)"
get "$OWNER" "/studio/auth?stage=verify&method=email&email=$(printf '%s' "$EMAIL" | sed 's/@/%40/')" "$V" >/dev/null
CODE="$(text "$V" | grep -A1 'وَضع التَّجرِبَة' | grep -oE '[0-9]{4,8}' | head -1)"
[ -n "$CODE" ] && ok "رَمزُ وَضعِ التَجرِبَة مَقروءٌ مِنَ الشاشَة" || { CODE=123456; info "لَم يُقرَأ مِنَ الشاشَة — يُستَعمَلُ الثابِتُ المَعلوم"; }

read -r C L3 < <(post "$OWNER" /studio/auth/verify \
    --data-urlencode method=email --data-urlencode "email=$EMAIL" --data-urlencode "code=$CODE")
[ "$C" = 302 ] || die "‏/studio/auth/verify رَدَّ $C — الرَمزُ أَو القَناة."
ok "الحِسابُ أُنشِئ ودَخَل → $L3"

case "$L3" in
  /studio/consent*)
    read -r C L4 < <(post "$OWNER" /studio/consent/accept --data-urlencode "returnUrl=/studio")
    [ "$C" = 302 ] || die "قَبولُ الشُروطِ رَدَّ $C." ;;
  *) L4="$L3" ;;
esac
ok "الشُروطُ قُبِلَت → $L4"

case "$L4" in
  /studio/s/*) SID="${L4##*/}" ;;
  *) die "لَم تُستَأنَفِ المُطالَبَة — الوِجهَة $L4 لا /studio/s/{id}." ;;
esac
ok "جَلسَةُ التَحليل: $SID"

# ═══ ٢) الجِدار — أَعَبَرَتِ الدِراسَةُ إلى Completed؟ ═════════════════
STEP="٢ — اكتِمالُ التَحليل"
say "الخُطوَة ٢ · انتِظارُ التَحليل   (سَقف: $((ANALYZE_TRIES * 5)) ثانِيَة)"
S="$(mktemp)"; STATE=""
for i in $(seq 1 "$ANALYZE_TRIES"); do
    get "$OWNER" "/studio/s/$SID" "$S" >/dev/null
    if text "$S" | grep -q 'فَشِلَ التَّحليل'; then
        STATE=failed
        printf '\n\033[31m✗ فَشِلَ التَحليل. الرِسالَةُ كَما تَظهَرُ على الشاشَة:\033[0m\n'
        # شَكلُ `AgentErrorText.Where` حَرفاً: «تَسميَة (عُنوان) رَمز: جِسم».
        # فَالمُلتَقَطُ هُوَ الرِسالَةُ نَفسُها لا سَطرٌ مُجاوِر.
        text "$S" \
          | grep -oE '[A-Za-z0-9_-]+ \(https?://[^)]*\)[^"]*' \
          | head -1 | sed 's/&quot;/"/g; s/&#x27;/'"'"'/g; s/&amp;/\&/g; s/^/     /'
        die "التَحليلُ لَم يَعبُر. اقرَأِ الرِسالَةَ أَعلاه — تَحمِلُ **التَسميَةَ والعُنوانَ ورَمزَ الحالَة** (‏docs/AGENT-KEYS.md §٦)."
    fi
    if text "$S" | grep -q 'ابنِ\|بِناءُ التَطبيق\|name="slug"'; then STATE=done; break; fi
    sleep 5
done
[ "$STATE" = done ] || die "لَم تَكتَمِلِ الدِراسَةُ خِلالَ السَقف — الحالَةُ ما تَزالُ «جارٍ»."
ok "الدِراسَةُ اكتَمَلَت، واستِمارَةُ البِناءِ مَرسومَة"

# ═══ ٣) بِناءُ المَتجَر ════════════════════════════════════════════════
STEP="٣ — بِناءُ المَتجَر"
say "الخُطوَة ٣ · ابنِ المَتجَر"
SLUG="walk-$MARK"
read -r C L5 < <(post "$OWNER" "/studio/s/$SID/build" \
    --data-urlencode "slug=$SLUG" \
    --data-urlencode "name=مُعَدّاتُ الرِياض" \
    --data-urlencode "color=#2563eb" \
    --data-urlencode "tagline=تَأجيرُ المُعَدّاتِ بِالساعَة" \
    --data-urlencode "city=الرِياض")
[ "$C" = 302 ] || die "‏/build رَدَّ $C."
case "$L5" in
  *build_err=*) die "رُفِضَ البِناء: ${L5##*build_err=}  (المَعجَم: slug_required · slug_format · slug_taken · slug_reserved · name_required · color_invalid · no_auth_channel)" ;;
  /studio/apps/*) ok "المَتجَرُ حَيّ → $L5" ;;
  *) die "وِجهَةٌ غَيرُ مُتَوَقَّعَة: $L5" ;;
esac
STORE="$(mktemp)"; [ "$(get "$OWNER" "/$SLUG" "$STORE")" = 200 ] \
    || die "‏/$SLUG لا يُصَيَّر بِـ200." ; ok "‏/$SLUG ⇐ 200"

# ═══ ٤) الضَبط — العَلامَةُ وباقَةٌ يُؤَلِّفُها المالِك ════════════════
STEP="٤ — الضَبط"
say "الخُطوَة ٤ · الضَبط: العَلامَة، ثُمَّ باقَةٌ يُؤَلِّفُها المالِك"
read -r C L6 < <(post "$OWNER" "/studio/apps/$SLUG/branding/save" \
    --data-urlencode "name=مُعَدّاتُ الرِياض" \
    --data-urlencode "tagline=تَأجيرُ المُعَدّاتِ بِالساعَة — بِلا وَسيط" \
    --data-urlencode "city=الرِياض" --data-urlencode "color=#0f766e")
[ "$C" = 302 ] || die "‏branding/save رَدَّ $C."
info "العَلامَة → $L6"; ok "العَلامَةُ حُفِظَت"

# الباقَةُ **بِسِعرِ صِفر**: هي وَحدَها الَّتي تُعرَض وتُمنَح ذاتِيّاً في
# مَتجَرٍ لا يَقبِض (‏PlanPurchasePolicy.Visible + Decide). و`free`/`basic`/`pro`
# سلاجاتٌ مَحجوزَة (‏slug_shadows_seeded_plan)، والمُدَّةُ يَجِبُ أَن تَكونَ
# مُوجَبَةً و≤730.
read -r C L7 < <(post "$OWNER" "/studio/apps/$SLUG/plans/save" \
    --data-urlencode "slug=tajriba" \
    --data-urlencode "label_ar=باقَةُ التَجرِبَة" \
    --data-urlencode "desc_ar=باقَةٌ مَجّانِيَّةٌ لِلتَجرِبَة — ثَلاثَةُ إعلانات" \
    --data-urlencode "price=0" --data-urlencode "quota=3" \
    --data-urlencode "days=30" --data-urlencode "active=1")
[ "$C" = 302 ] || die "‏plans/save رَدَّ $C."
case "$L7" in
  *saved=1*) ok "الباقَةُ «tajriba» أُلِّفَت → $L7" ;;
  *err=*)    die "رُفِضَت الباقَة: ${L7##*err=}" ;;
  *)         die "وِجهَةٌ غَيرُ مُتَوَقَّعَة: $L7" ;;
esac

# ═══ ٥) الاشتِراكُ بِوَضعِ التَجرِبَة ══════════════════════════════════
STEP="٥ — الاشتِراك"
say "الخُطوَة ٥ · الاشتِراكُ بِالباقَة، ثُمَّ إعلانٌ يُنشَر"
# المالِكُ يَدخُلُ **مَتجَرَه** كَمُستَخدِمٍ فيه — الجَلسَةُ غَيرُ جَلسَةِ الاستوديو.
door() { # door <jar> <slug> <marker> → يُسَجِّلُ ويَدخُل، بِالقَناةِ الَّتي يَعرِضُها المَتجَر
    local jar="$1" slug="$2" mk="$3" f="$(mktemp)" c l
    get "$jar" "/$slug/login" "$f" >/dev/null
    if grep -q "action=\"/$slug/auth/phone/login\"" "$f"; then
        read -r c l < <(post "$jar" "/$slug/auth/phone/login" --data-urlencode "phone=05${mk}")
        [ "$c" = 302 ] || return 1
        read -r c l < <(post "$jar" "/$slug/auth/phone/verify" --data-urlencode "phone=05${mk}" --data-urlencode "code=123456")
    elif grep -q "action=\"/$slug/auth/email/login\"" "$f"; then
        read -r c l < <(post "$jar" "/$slug/auth/email/login" --data-urlencode "email=agent-$mk@wasayel.test")
        [ "$c" = 302 ] || return 1
        read -r c l < <(post "$jar" "/$slug/auth/email/verify" --data-urlencode "email=agent-$mk@wasayel.test" --data-urlencode "code=123456")
    elif grep -q "action=\"/$slug/auth/nafath/login\"" "$f"; then
        read -r c l < <(post "$jar" "/$slug/auth/nafath/login" --data-urlencode "nid=10${mk}")
        [ "$c" = 302 ] || return 1
        local att="${l##*attempt=}"; att="${att%%&*}"
        sleep 5   # المُحاكي يَعتَمِد بَعدَ AutoApproveSeconds — لا يَعتَمِدُ فَوراً
        read -r c l < <(post "$jar" "/$slug/auth/nafath/verify" --data-urlencode "nid=10${mk}" --data-urlencode "attempt=$att")
    else
        return 2
    fi
    [ "$c" = 302 ] && [ "${l#*err=}" = "$l" ] && printf '%s\n' "$l"
}
rm -f "$SHOPPER"; SELLER="$JAR.seller"; rm -f "$SELLER"
SL="$(door "$SELLER" "$SLUG" "99887701")" || die "بابُ المَتجَرِ لَم يَفتَح لِلبائِع."
ok "البائِعُ دَخَلَ المَتجَر → $SL"

P="$(mktemp)"; get "$SELLER" "/$SLUG/plans" "$P" >/dev/null
PID="$(grep -o "action=\"/$SLUG/plans/[a-z0-9_]*/subscribe\"" "$P" | head -1 | sed "s#.*/plans/##;s#/subscribe\"##")"
[ -n "$PID" ] || die "لا زِرَّ اشتِراكٍ على /$SLUG/plans — الباقَةُ المَجّانِيَّةُ لَم تُعرَض."
read -r C L8 < <(post "$SELLER" "/$SLUG/plans/$PID/subscribe")
[ "$C" = 302 ] || die "الاشتِراكُ رَدَّ $C."
case "$L8" in *err=*) die "رُفِضَ الاشتِراك: ${L8##*err=}" ;; esac
ok "اشتُرِكَ في «$PID» → $L8"

# الفِئَةُ تُقرَأُ مِن رَوابِطِ المَتجَرِ نَفسِه لا تُكتَبُ ثابِتَة:
# مَتجَرٌ يُبنى بِلا قِطاعٍ مَعروفٍ يَأخُذُ فِئَتَي «ecommerce»
# (‏products · deals) — والقِراءَةُ تَصمُدُ لَو تَبَدَّلَ ذلك.
# نُقطَةُ نَشرِ الإعلانِ مَحروسَةٌ بِـ`.RequireAuth().RequireTerms()` —
# فَالشُروطُ تُقبَلُ قَبلَها، وإلّا رَدَّت البَوّابَةُ إلى صَفحَةِ الشُروط.
read -r C LT < <(post "$SELLER" "/$SLUG/terms/accept")
[ "$C" = 302 ] || die "قَبولُ شُروطِ المَتجَرِ رَدَّ $C."
ok "شُروطُ المَتجَرِ قُبِلَت → $LT"

EXPL="$(mktemp)"; get "$SELLER" "/$SLUG/explore" "$EXPL" >/dev/null
CAT="$(grep -oE 'category=[a-z0-9_-]+' "$EXPL" | head -1 | cut -d= -f2)"
[ -n "$CAT" ] || CAT=products
info "الفِئَة: $CAT"
read -r C L9 < <(post "$SELLER" "/$SLUG/listings/create" \
    --data-urlencode "title=حَفّارَةٌ صَغيرَةٌ بِالساعَة $MARK" \
    --data-urlencode "description=إعلانُ فَحصٍ آليّ — يُحذَف" \
    --data-urlencode "category=$CAT" --data-urlencode "price=250" \
    --data-urlencode "city=الرِياض" --data-urlencode "district=العُلَيّا")
[ "$C" = 302 ] || die "إنشاءُ الإعلانِ رَدَّ $C."
case "$L9" in *err=*) die "رُفِضَ الإعلان: ${L9##*err=} (‏quota ⇐ الاشتِراكُ لَم يُمنَح)" ;; esac
ok "الإعلانُ نُشِر → $L9"

# ═══ ٦) مُستَخدِمٌ جَديد ═══════════════════════════════════════════════
STEP="٦ — مُستَخدِمٌ جَديد"
say "الخُطوَة ٦ · مُستَخدِمٌ جَديدٌ يُسَجِّلُ في المَتجَر"
NL="$(door "$SHOPPER" "$SLUG" "99887702")" || die "بابُ المَتجَرِ لَم يَفتَح لِلمُشتَري."
ok "المُشتَري سَجَّلَ ودَخَل → $NL"

# ═══ ٧) طَلَبٌ نَقديّ ══════════════════════════════════════════════════
STEP="٧ — طَلَبٌ نَقديّ"
say "الخُطوَة ٧ · طَلَبٌ نَقديٌّ حَتّى الصَفقَة"
E="$(mktemp)"; get "$SHOPPER" "/$SLUG/explore" "$E" >/dev/null
LID="$(grep -o "href=\"/$SLUG/listings/[0-9a-f-]\{36\}\"" "$E" | head -1 | sed "s#.*/listings/##;s/\"//")"
[ -n "$LID" ] || die "لا إعلانَ في /$SLUG/explore."
ok "الإعلان: $LID"

read -r C LA < <(post "$SHOPPER" "/$SLUG/listings/$LID/cart/add" --data-urlencode "qty=1")
[ "$C" = 302 ] || die "الإضافَةُ لِلسَلَّةِ رَدَّت $C."
ok "أُضيفَ لِلسَلَّة → $LA"

# الشاشَةُ والنُقطَةُ تَقرَآنِ `CheckoutPaymentPolicy` نَفسَها: مَتجَرٌ لا
# يَقبِض ⇒ **النَقدُ وَحدَه مَعروض**. فَإن ظَهَرَتِ البِطاقَةُ فَذلك رَبطُ
# دَفعٍ فَعّال، ولا يَلزَمُ لِهذا المَسار.
CO="$(mktemp)"; get "$SHOPPER" "/$SLUG/checkout?step=3&name=%D9%85%D8%B4%D8%AA%D8%B1&phone=0500000099&addr=%D8%A7%D9%84%D8%B1%D9%8A%D8%A7%D8%B6" "$CO" >/dev/null
info "طُرُقُ الدَفعِ المَعروضَة: $(grep -o 'name="pay" value="[a-z]*"' "$CO" | sed 's/.*value="//;s/"//' | tr '\n' ' ')"

read -r C LB < <(post "$SHOPPER" "/$SLUG/checkout/submit" \
    --data-urlencode "name=مُشتَرٍ آليٌّ $MARK" --data-urlencode "phone=0500000099" \
    --data-urlencode "addr=الرِياض — بَياناتُ فَحص" --data-urlencode "pay=cash")
[ "$C" = 302 ] || die "‏checkout/submit رَدَّ $C."
case "$LB" in
  */deals/*) ok "الصَفقَةُ أُنشِئَت → $LB" ;;
  *) die "لَم تُنشَأ صَفقَة — الوِجهَة $LB (‏err= ⇐ اقرَأ pay_card_unavailable)." ;;
esac
D="$(mktemp)"; [ "$(get "$SHOPPER" "${LB}" "$D")" = 200 ] || die "صَفحَةُ الصَفقَةِ لا تُصَيَّر."
ok "صَفحَةُ الصَفقَةِ ⇐ 200"

# ═══ الخُلاصَة ═════════════════════════════════════════════════════════
say "تَمَّتِ الخَمسُ الباقِيَة"
cat <<EOF
   المَتجَر     : $BASE/$SLUG        (‏slug = $SLUG)
   لَوحَةُ المالِك: $BASE/studio/apps/$SLUG
   الدِراسَة     : $BASE/studio/s/$SID
   الصَفقَة      : $BASE$LB

   ما بُذِرَ بِوَسمِ «$MARK» — يُحذَفُ بِيَدِ صاحِبِ المَشروع مَتى شاء:
     · مُستَأجِر  $SLUG          (‏وثيقَةُ Tenant + إيجارُها)
     · StudioUser $EMAIL
     · مُستَخدِما المَتجَر بِوَسمَي 99887701 / 99887702
     · إعلانٌ واحِدٌ وصَفقَةٌ واحِدَةٌ داخِلَ $SLUG

   ولِلاشتِراكِ المَدفوعِ ووَضعِ التَجرِبَةِ على البِطاقَة — جِدارٌ ثانٍ
   لا يَفتَحُه هذا السكربت: Payments__Provider=simulation يُشَغِّلُ
   المُحاكاةَ، **لكِنَّ ظُهورَ البِطاقَةِ يَتَوَقَّفُ على رابِطِ دَفعٍ
   حَقيقيٍّ يَربِطُه المالِكُ** في /studio/apps/$SLUG/providers
   (‏moyasar_hosted). راجِع ADR-025 §٢-هـ.
EOF
