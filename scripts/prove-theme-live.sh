#!/usr/bin/env bash
# ═══════════════════════════════════════════════════════════════════════
#  البُرهان الحَيّ — ثيم مُستَأجِر يَصير مَبثوثاً بِلا إعادَة تَشغيل
# ───────────────────────────────────────────────────────────────────────
#  كُلّ سَطر هُنا يَمُرّ مِن **الخادِم الحَيّ** بِنَفس الـPID مِن أَوَّلِه
#  إلى آخِرِه. وهذا هو مَوضِع الدَعوى: لَو كُتِبَت الوَثيقَة مِن عَمَلِيَّة
#  أُخرى لَأَثبَتنا أَنّ قاعِدَة بَيانات تَقبَل صَفّاً — لا أَنّ الخادِم
#  أَبطَلَ كاشَه وأَعادَ البَثّ.
#
#  التَسَلسُل:
#    ٠. طَبع الـPID قَبل كُلّ شَيء.
#    ١. لَقطَة «قَبل»: ‏:root لِـadwar-demo و ashare.
#    ٢. دُخول مُشرِف المَنصَّة (جَلسَة studio).
#    ٣. **السالِب**: ثيم بِلَون فاسِد ← مَرفوض بِرَمزِه، ولا وَثيقَة.
#    ٤. المُوجَب: اقتِراح ثيم أَخضَر ← مُعَلَّق. والصَفحَة **لا تَتَغَيَّر**
#       (المُعَلَّق لا يُبَثّ) — وهذا نِصف البُرهان.
#    ٥. الاعتِماد ← الكاش يُبطَل داخِل نَفس العَمَلِيَّة.
#    ٦. لَقطَة «بَعد»: adwar-demo تَغَيَّرَ، و ashare **بايتاً بِبايت**.
#    ٧. طَبع الـPID مَرَّةً أُخرى — نَفسُه.
#
#  الاستِعمال:
#     scripts/prove-theme-live.sh <admin-phone> [OUT_DIR] [BASE_URL]
# ═══════════════════════════════════════════════════════════════════════
set -euo pipefail

PHONE="${1:?يَلزَم هاتِف مُشرِف المَنصَّة}"
OUT="${2:-tests/characterization/appearance/proof}"
BASE="${3:-http://localhost:5050}"
BASELINE="tests/characterization/appearance/baseline"
JAR="$(mktemp)"
LOG="$OUT/live-proof.log"

mkdir -p "$OUT"
: > "$LOG"
trap 'rm -f "$JAR"' EXIT

say() { printf '%s  %s\n' "$(date -u +%Y-%m-%dT%H:%M:%S.%3NZ)" "$*" | tee -a "$LOG"; }

root_of() { curl -s "$BASE$1" | grep -o '<style id="wsl-theme">[^<]*</style>' \
            | sed 's/.*<style id="wsl-theme">//; s|</style>||'; }

pid_of_5050() { netstat -ano | grep ':5050 .*LISTENING' | head -1 | awk '{print $NF}'; }

# ─── ٠ ────────────────────────────────────────────────────────────────
PID_BEFORE="$(pid_of_5050)"
say "[pid] الخادِم قَبل البُرهان = $PID_BEFORE"

# ─── ١ ────────────────────────────────────────────────────────────────
root_of /adwar-demo > "$OUT/adwar-demo.root.before.css"
root_of /ashare     > "$OUT/ashare.root.before.css"
say "[before] adwar-demo primary=$(grep -o 'wsl-color-primary:[^;]*' "$OUT/adwar-demo.root.before.css")"
say "[before] adwar-demo radius.md=$(grep -o 'wsl-radius-md:[^;]*' "$OUT/adwar-demo.root.before.css")"
say "[before] ashare     primary=$(grep -o 'wsl-color-primary:[^;]*' "$OUT/ashare.root.before.css")"

# ─── ٢ ────────────────────────────────────────────────────────────────
curl -s -c "$JAR" -b "$JAR" -o /dev/null \
     -d "phone=$PHONE" -d "code=123456" "$BASE/studio/auth/verify"
say "[auth] جَلسَة مُشرِف المَنصَّة لِـ$PHONE — كوكي: $(grep -c acommerce.studio "$JAR" || true) سَطر"

# ─── ٣ السالِب ────────────────────────────────────────────────────────
BAD='{"slug":"adwar_bad","label":{"ar":"فاسِد"},"tokens":{"color.primary":"crimson"}}'
BAD_CODE=$(curl -s -o "$OUT/negative.body.txt" -w '%{http_code}' -b "$JAR" \
  --data-urlencode "theme_slug=adwar_bad" --data-urlencode "definition=$BAD" \
  "$BASE/admin/tenants/adwar-demo/theme/propose")
say "[negative] HTTP $BAD_CODE :: $(cat "$OUT/negative.body.txt")"

# سالِب ثانٍ — خُروج مِن التَصريحَة إلى المُستَند.
ESCAPE='{"slug":"adwar_esc","label":{"ar":"هُروب"},"tokens":{"color.primary":"red;}body{display:none"}}'
ESC_CODE=$(curl -s -o "$OUT/negative2.body.txt" -w '%{http_code}' -b "$JAR" \
  --data-urlencode "theme_slug=adwar_esc" --data-urlencode "definition=$ESCAPE" \
  "$BASE/admin/tenants/adwar-demo/theme/propose")
say "[negative2] HTTP $ESC_CODE :: $(cat "$OUT/negative2.body.txt")"

# ─── ٤ المُوجَب: اقتِراح ─────────────────────────────────────────────
GREEN='{"slug":"adwar_green","label":{"ar":"أَخضَر أَدوار","en":null},"tokens":{"color.primary":"#14532D","color.primaryDark":"#052E16","color.primaryLight":"#22C55E","color.primaryHover":"#166534","color.secondary":"#CA8A04","color.bg":"#F4F7F4","color.bgAlt":"#E7EFE8","color.surface":"#FFFFFF","color.border":"rgba(5,46,22,.12)","color.text":"#0B1F13","color.textMuted":"#4B6B57","radius.sm":"2px","radius.md":"4px","radius.lg":"6px","radius.xl":"8px","density":"1"}}'
OK_CODE=$(curl -s -o "$OUT/propose.body.txt" -w '%{http_code}' -b "$JAR" \
  --data-urlencode "theme_slug=adwar_green" --data-urlencode "definition=$GREEN" \
  "$BASE/admin/tenants/adwar-demo/theme/propose")
say "[propose] HTTP $OK_CODE :: $(cat "$OUT/propose.body.txt")"

root_of /adwar-demo > "$OUT/adwar-demo.root.pending.css"
if cmp -s "$OUT/adwar-demo.root.before.css" "$OUT/adwar-demo.root.pending.css"; then
  say "[pending] الصَفحَة لَم تَتَغَيَّر بِحَرف — المُعَلَّق لا يُبَثّ. ✓"
else
  say "[pending] ✗ المُعَلَّق بُثَّ — عَقد الحالات مَكسور."; exit 1
fi

# ─── ٥ الاعتِماد ─────────────────────────────────────────────────────
DEC_CODE=$(curl -s -o "$OUT/decide.body.txt" -w '%{http_code}' -b "$JAR" \
  -d "decision=approve" "$BASE/admin/tenants/adwar-demo/theme/adwar_green/decide")
say "[decide] HTTP $DEC_CODE :: $(cat "$OUT/decide.body.txt")"

# ─── ٦ بَعد ──────────────────────────────────────────────────────────
root_of /adwar-demo > "$OUT/adwar-demo.root.after.css"
root_of /ashare     > "$OUT/ashare.root.after.css"
say "[after] adwar-demo primary=$(grep -o 'wsl-color-primary:[^;]*' "$OUT/adwar-demo.root.after.css")"
say "[after] adwar-demo radius.md=$(grep -o 'wsl-radius-md:[^;]*' "$OUT/adwar-demo.root.after.css")"
say "[after] ashare     primary=$(grep -o 'wsl-color-primary:[^;]*' "$OUT/ashare.root.after.css")"

if cmp -s "$OUT/adwar-demo.root.before.css" "$OUT/adwar-demo.root.after.css"; then
  say "[verdict] ✗ adwar-demo لَم يَتَغَيَّر — الكاش لَم يُبطَل."; exit 1
fi
say "[verdict] adwar-demo تَغَيَّرَ بِلا إعادَة تَشغيل. ✓"

if cmp -s "$OUT/ashare.root.before.css" "$OUT/ashare.root.after.css"; then
  say "[verdict] ashare بَقِيَ عَلى الافتِراضيّ حَرفاً. ✓"
else
  say "[verdict] ✗ ashare تَلَوَّثَ بِثيم مُستَأجِر آخَر — العَزل مَكسور."; exit 1
fi

# البُرهان الأَقوى: صَفحَة ashare كامِلَةً بايتاً بِبايت مُقابِل لَقطَة
# الأَساس المُودَعَة قَبل وُجود طَبَقَة الرُموز أَصلاً.
curl -s "$BASE/ashare" \
  | sed -E 's/Blazor-Server-Component-State:[A-Za-z0-9+\/=_-]+/Blazor-Server-Component-State:NORMALIZED/g' \
  | perl -0pe 's{[ \t]*<style id="wsl-theme">.*?</style>\r?\n\r?\n}{}s' > "$OUT/ashare.stripped.after.html"
if cmp -s "$BASELINE/ashare-portal.html" "$OUT/ashare.stripped.after.html"; then
  say "[verdict] ashare بايتاً بِبايت = لَقطَة ما قَبل الرُموز. ✓"
else
  say "[verdict] ✗ ashare انحَرَفَ عَن لَقطَة الأَساس."; exit 1
fi

# ─── ٧ ────────────────────────────────────────────────────────────────
PID_AFTER="$(pid_of_5050)"
say "[pid] الخادِم بَعد البُرهان = $PID_AFTER"
[ "$PID_BEFORE" = "$PID_AFTER" ] \
  && say "[pid] نَفس العَمَلِيَّة — لا إعادَة تَشغيل ولا إعادَة بِناء. ✓" \
  || { say "[pid] ✗ الـPID تَغَيَّرَ — البُرهان باطِل."; exit 1; }

say "البُرهان الحَيّ: مُكتَمِل."
