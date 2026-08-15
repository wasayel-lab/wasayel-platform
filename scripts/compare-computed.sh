#!/usr/bin/env bash
# ═══════════════════════════════════════════════════════════════════════
#  بَوّابَة القَبول الثانِيَة — «النَمَط المَحسوب مُطابِق بايتاً بِبايت»
# ───────────────────────────────────────────────────────────────────────
#  `compare-appearance.sh` تَحرُس الـHTML. وهي **عَمياء عَن CSS**:
#  استِبدال حَرفِيَّة بِـ`var(--ac-…)` داخِل ورَقَة أَنماط لا يُغَيِّر
#  بايتاً واحِداً في الوَسم، فَتَمُرّ خَضراء وإن انحَرَفَت القيمَة.
#  هذِه تَحرُس ما يُحاسِبُه المُتَصَفِّح بَعدَ الكاسكيد كامِلاً.
#
#  الاستِعمال:
#     scripts/compare-computed.sh <baseline-dir>
#  ولِالتِقاط أَساسٍ جَديد:
#     dotnet run scripts/dump-computed.cs -- <baseline-dir>
#
#  حارِس العَمى (‏القاعِدَة ١٠): يُطبَع عَدَد المِلَفّات والأَسطُر
#  المُقارَنَة، والصِفر يُحمِر.
# ═══════════════════════════════════════════════════════════════════════
set -euo pipefail

BASELINE="${1:?يَلزَم مُجَلَّد الأَساس}"
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
AFTER="$(mktemp -d)"
trap 'rm -rf "$AFTER"' EXIT

export DOTNET_ROOT="${DOTNET_ROOT:-/d/Testor/dotnet10}"
export PATH="$DOTNET_ROOT:$PATH"

dotnet run "$SCRIPT_DIR/dump-computed.cs" -- "$AFTER" > "$AFTER/dump.log" 2>&1 || {
  echo "✗ تَعَذَّرَ التَفريغ:" >&2; cat "$AFTER/dump.log" >&2; exit 2; }
grep -E '^  (✓|الصَفَحات)' "$AFTER/dump.log" || true

fail=0
compared=0
lines=0
for f in "$BASELINE"/*.computed.txt; do
  [ -e "$f" ] || continue
  name="$(basename "$f")"
  compared=$((compared + 1))
  lines=$((lines + $(wc -l < "$f")))
  if [ ! -f "$AFTER/$name" ]; then
    echo "✗ $name — لا نَظيرَ لَه في التَفريغ الجَديد"; fail=1; continue
  fi
  if cmp -s "$f" "$AFTER/$name"; then
    echo "✓ $name — النَمَط المَحسوب مُطابِق بايتاً بِبايت"
  else
    echo "✗ $name — فَرق في النَمَط المَحسوب:"
    # `|| true`: تَحتَ pipefail يَقتُل `head` الأُنبوبَ بِـSIGPIPE فَيَتَوَقَّف
    # السكربت عِندَ أَوَّل مِلَفّ أَحمَر ويُخفي البَقِيَّة — نَفس العَطَب
    # المُوَثَّق في compare-appearance.sh.
    { diff "$f" "$AFTER/$name" | head -30; } || true
    echo "    (‏$(diff "$f" "$AFTER/$name" | grep -c '^<' || true) سَطراً تَغَيَّرَ)"
    fail=1
  fi
done

echo "المِلَفّات المُقارَنَة: $compared   الأَسطُر: $lines"
if [ "$compared" -eq 0 ] || [ "$lines" -eq 0 ]; then
  echo "✗ فَحصٌ أَعمى: لا أَساسَ في «$BASELINE» — لا شَيءَ قُورِن." >&2
  exit 1
fi

if [ "$fail" -eq 0 ]; then
  echo "بَوّابَة النَمَط المَحسوب: خَضراء."
else
  echo "بَوّابَة النَمَط المَحسوب: حَمراء." >&2
fi
exit "$fail"
