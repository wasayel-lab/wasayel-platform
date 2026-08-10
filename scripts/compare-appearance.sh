#!/usr/bin/env bash
# ═══════════════════════════════════════════════════════════════════════
#  بَوّابَة القَبول — «الـ HTML خارِج كُتلَة الرُموز مُطابِق بايتاً بِبايت»
# ───────────────────────────────────────────────────────────────────────
#  يَلتَقِط الصَفَحات المَرجِعِيَّة الآن، يَنزَع مِنها **كُتلَة واحِدَة**
#  هي <style id="wsl-theme">…</style> (وهي الإضافَة الوَحيدَة المَأذون
#  بِها في هذه المَوجَة)، ثُمَّ يُقارِن الباقي بِلَقطَة الأَساس
#  المُلتَقَطَة قَبل أَيّ تَحويل.
#
#  أَيّ فَرق خارِج تِلكَ الكُتلَة = فَشَل. لا «يَبدو مُطابِقاً»، ولا
#  حُكم بِالعَين: `cmp` هو الحَكَم.
#
#  الاستِعمال:
#     scripts/compare-appearance.sh [BASELINE_DIR] [BASE_URL]
# ═══════════════════════════════════════════════════════════════════════
set -euo pipefail

BASELINE="${1:-tests/characterization/appearance/baseline}"
BASE_URL="${2:-http://localhost:5050}"
AFTER="$(mktemp -d)"
STRIPPED="$(mktemp -d)"

trap 'rm -rf "$AFTER" "$STRIPPED"' EXIT

bash "$(dirname "$0")/capture-appearance.sh" "$AFTER" "$BASE_URL" > /dev/null

fail=0
for f in "$BASELINE"/*.html; do
  name="$(basename "$f")"
  # نَزع كُتلَة الرُموز وَحدَها — سَطر واحِد بِـ perl، وحُدودُه صَريحَة
  # (المُعَرِّف wsl-theme) لا تَخمينِيَّة.
  perl -0pe 's{<style id="wsl-theme">.*?</style>}{}s' "$AFTER/$name" > "$STRIPPED/$name"

  if cmp -s "$f" "$STRIPPED/$name"; then
    echo "✓ $name — مُطابِق بايتاً بِبايت خارِج كُتلَة الرُموز"
  else
    echo "✗ $name — فَرق خارِج كُتلَة الرُموز:"
    diff <(fold -w120 "$f") <(fold -w120 "$STRIPPED/$name") | head -20
    fail=1
  fi
done

if [ "$fail" -eq 0 ]; then
  echo "بَوّابَة القَبول: خَضراء — لا بايت تَغَيَّرَ خارِج كُتلَة الرُموز."
else
  echo "بَوّابَة القَبول: حَمراء." >&2
fi
exit "$fail"
