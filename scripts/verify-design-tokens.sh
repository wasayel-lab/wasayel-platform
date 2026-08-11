#!/usr/bin/env bash
# Usage: ./scripts/verify-design-tokens.sh
#
# LAYER 3 — PER-VALUE correctness of design tokens.
#   "Is THIS individual value on the allowed scale?"
#   (Not "how many distinct values exist?" — that is Layer 4.)
#
# Checks, each on the value itself:
#   1. Inline font-size    — on the px scale
#   2. Inline spacing      — padding/margin on the even-px scale, 4..48
#   3. AcIcon Size=""      — on the icon scale
#   4. AcIcon Name=""      — counted (palette breadth is Layer 4's business)
#   5. Razor nesting depth — no page beyond 16 levels
#
# Deliberately NOT here (would duplicate another layer):
#   • distinct-colour COUNT      -> Layer 4 (per-app aggregate)
#   • literal #hex in style=""   -> Layer 1 (rule no-inline-color)
#
# ── How this relates to our theme catalog (META-MODEL §9) ────────────────
# We have something the origin repo did not: a closed 37-token vocabulary in
# libs/kits/Theme/.../Definitions/default.theme.json, where --wsl-* inputs
# drive --ac-* outputs.  Its typographic scale is far NARROWER than the px
# scale enforced below — four sizes (0.875/1/1.25/1.5rem) against the
# fourteen px values allowed here, and a 4/8/16/24/32px spacing scale.
#
# This layer deliberately keeps the origin's WIDER px scale rather than
# adopting the token scale, for one reason: every value it inspects is an
# INLINE value in a .razor file, i.e. a value that already escaped the token
# layer.  Tightening the scale here would convert an existing measured debt
# (29 off-scale font sizes) into a much larger one overnight without anyone
# choosing that.  Narrowing the scale toward the catalog is a real option,
# but it is the owner's decision and belongs in its own pass.
#
# ── Correction over the origin ──────────────────────────────────────────
# The origin piped its font-size and spacing scans through `head -20` and
# `head -30`.  That caps the REPORT at 20/30 hits, so a codebase with 200
# violations and one with 21 look identical, and fixing 179 of them changes
# nothing on screen.  Both caps are removed; the totals below are complete.

set -eo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=scripts/verify-common.sh
source "$SCRIPT_DIR/verify-common.sh"

TMP=$(mktemp -d)
trap 'rm -rf "$TMP"' EXIT
VIOLATIONS=0
: > "$TMP/violations.txt"
: > "$TMP/keys.txt"

# report <machine-key> <human-message>
# The key is emitted explicitly rather than re-parsed out of the prose: a
# ledger whose fingerprints are scraped from human text silently changes
# meaning the moment someone rewords a message.
report() {
    echo "$1" >> "$TMP/keys.txt"
    echo "  ✗ $2" >> "$TMP/violations.txt"
    VIOLATIONS=$((VIOLATIONS + 1))
}

echo "═══════════════════════════════════════════════"
echo "   Layer 3 — per-value design-token scale"
echo "═══════════════════════════════════════════════"
echo ""

wsl_find razor "$WSL_ALL_ROOTS" > "$TMP/razor.txt"
RAZOR_CNT=$(wc -l < "$TMP/razor.txt" | tr -d ' ')
echo "--- Subjects ---"
wsl_require_subjects "razor files" "$RAZOR_CNT"

g() { { xargs -a "$TMP/razor.txt" -d '\n' grep "$@" 2>/dev/null || true; } ; }

# ── 1. Font-size scale ───────────────────────────────────────────────────
ALLOWED_PX='^(10|11|12|13|14|15|16|18|20|24|28|32|40|48)px$'
FONT_SCANNED=0
while IFS= read -r hit; do
    [ -z "$hit" ] && continue
    val=$(echo "$hit" | grep -oE 'font-size:\s*[0-9.]+px' | grep -oE '[0-9.]+px' | head -1)
    [ -z "$val" ] && continue
    FONT_SCANNED=$((FONT_SCANNED + 1))
    echo "$val" | grep -qE "$ALLOWED_PX" || report "font-size|$val" "off-scale font-size ($val): $(wsl_rel "${hit%%:*}")${hit#"${hit%%:*}"}"
done < <(g -HnoE 'font-size:\s*[0-9.]+px')

# ── 2. Spacing scale ─────────────────────────────────────────────────────
# Origin behaviour preserved exactly: 0 is fine; otherwise the value must be
# EVEN and within 4..48.  (Note: the origin's own comment claims a 4px grid,
# but its code tests `% 2`.  Kept as coded, not as commented — tightening to
# a true 4px grid is a separate, owner-visible decision.)
SPACE_SCANNED=0
while IFS= read -r hit; do
    [ -z "$hit" ] && continue
    loc=${hit%%:*}; rest=${hit#"$loc"}
    for val in $(echo "$hit" | grep -oE '(padding|margin)(-(top|right|bottom|left|inline|block|inline-start|inline-end))?:\s*[0-9]+px' | grep -oE '[0-9]+px'); do
        px=${val%px}
        SPACE_SCANNED=$((SPACE_SCANNED + 1))
        [ "$px" = "0" ] && continue
        if   [ "$px" -lt 4 ];            then report "spacing-under|$val" "off-scale spacing ($val, under 4px): $(wsl_rel "$loc")$rest"
        elif [ "$px" -gt 48 ];           then report "spacing-over|$val" "off-scale spacing ($val, over 48px): $(wsl_rel "$loc")$rest"
        elif [ "$((px % 2))" -ne 0 ];    then report "spacing-odd|$val" "odd-pixel spacing ($val): $(wsl_rel "$loc")$rest"
        fi
    done
done < <(g -HnE 'style="[^"]*(padding|margin)[^:]*:\s*[0-9]+px')

# ── 3. Icon size scale ───────────────────────────────────────────────────
ALLOWED_ICON='^(14|16|18|20|22|24|28|32|40|48)$'
ICON_SIZES=$(g -hoE '<AcIcon[^>]+Size="[0-9]+"' | grep -oE 'Size="[0-9]+"' | sed 's/Size="//; s/"//' | sort -un)
ICON_SCANNED=$(echo "$ICON_SIZES" | grep -c . || true)
while IFS= read -r size; do
    [ -z "$size" ] && continue
    echo "$size" | grep -qE "$ALLOWED_ICON" ||
        report "icon-size|$size" "off-scale icon size ($size) — scale is 14/16/18/20/22/24/28/32/40/48; used $(g -hoE "<AcIcon[^>]+Size=\"$size\"" | wc -l | tr -d ' ') time(s)"
done <<< "$ICON_SIZES"

# ── 4. Icon name palette (informational) ─────────────────────────────────
ICON_NAMES=$(g -hoE '<AcIcon[^>]+Name="[^"]+"' | grep -oE 'Name="[^"]+"' | sed 's/Name="//; s/"$//' | sort -u)
ICON_NAME_CNT=$(echo "$ICON_NAMES" | grep -c . || true)

# ── 5. Razor nesting depth ───────────────────────────────────────────────
DEPTH_SCANNED=0; MAX_DEPTH=0
while IFS= read -r file; do
    DEPTH_SCANNED=$((DEPTH_SCANNED + 1))
    lvl=$(awk '{ match($0, /^ */); if (RLENGTH > m) m = RLENGTH } END { print int(m/4) }' "$file")
    [ "$lvl" -gt "$MAX_DEPTH" ] && MAX_DEPTH=$lvl
    [ "$lvl" -gt 16 ] && report "deep-nesting|$(wsl_rel "$file")" "deep nesting ($lvl levels): $(wsl_rel "$file")"
done < <(wsl_find razor "$WSL_PAGE_ROOTS")

# ── Report ───────────────────────────────────────────────────────────────
wsl_require_subjects "inline font-size declarations" "$FONT_SCANNED"
wsl_require_subjects "inline spacing declarations"   "$SPACE_SCANNED"
wsl_require_subjects "distinct AcIcon sizes"         "$ICON_SCANNED"
wsl_require_subjects "distinct AcIcon names"         "$ICON_NAME_CNT"
wsl_require_subjects "pages measured for depth"      "$DEPTH_SCANNED"

echo ""
echo "═══════════════════════════════════════════════"
echo "   Layer 3 report"
echo "═══════════════════════════════════════════════"
echo "  Inline font-sizes scanned: $FONT_SCANNED"
echo "  Inline spacings scanned:   $SPACE_SCANNED"
echo "  Distinct icon sizes:       $ICON_SCANNED"
echo "  Distinct icon names:       $ICON_NAME_CNT"
echo "  Deepest page nesting:      $MAX_DEPTH levels (cap 16)"
echo "  Violations:                $VIOLATIONS"
echo ""

if [ "$VIOLATIONS" -gt 0 ]; then
    cat "$TMP/violations.txt"
    echo ""
fi

# ── Pinned debt (see Layer 2 for the contract) ───────────────────────────
# Fingerprint = <kind>|<value>|<occurrences>.  Keyed by the OFF-SCALE VALUE
# rather than by file, because that is the unit a fix actually removes: one
# decision ("13.5px is not a size we ship") retires one line of this ledger
# wherever those 8 occurrences happen to live.
BASELINE="$SCRIPT_DIR/verify-design-tokens-baseline.txt"
sort "$TMP/keys.txt" | uniq -c | awk '{printf "%s|%d\n", $2, $1}' | sort > "$TMP/current.txt"

# `WSL_DUMP=1 ./scripts/verify-design-tokens.sh` prints the fingerprints the
# script itself computed, for authoring/refreshing the baseline.  Generating
# the ledger from a hand-written reproduction of the logic is how a ledger
# ends up describing a slightly different measurement than the gate enforces.
if [ -n "${WSL_DUMP:-}" ]; then cat "$TMP/current.txt"; exit 0; fi

if [ ! -f "$BASELINE" ]; then
    [ "$VIOLATIONS" -gt 0 ] && { echo "No baseline present; $VIOLATIONS violation(s) above."; exit 1; }
    echo "✅ Layer 3 green."
    exit 0
fi

grep -vE '^\s*(#|$)' "$BASELINE" | sort > "$TMP/pinned.txt"
comm -23 "$TMP/current.txt" "$TMP/pinned.txt" > "$TMP/new.txt" || true
comm -13 "$TMP/current.txt" "$TMP/pinned.txt" > "$TMP/gone.txt" || true
NEW_CNT=$(wc -l < "$TMP/new.txt" | tr -d ' '); GONE_CNT=$(wc -l < "$TMP/gone.txt" | tr -d ' ')

echo "  Pinned debt entries: $(wc -l < "$TMP/pinned.txt" | tr -d ' ')   New: $NEW_CNT   Retired: $GONE_CNT"
FAIL=0
[ "$NEW_CNT" -gt 0 ]  && { echo "=== ✗ NEW off-scale values ==="; sed 's/^/  ✗ /' "$TMP/new.txt"; FAIL=1; }
[ "$GONE_CNT" -gt 0 ] && { echo "=== ✗ STALE baseline entries (fixed, still pinned) ==="; sed 's/^/  ✗ /' "$TMP/gone.txt"; FAIL=1; }
[ "$FAIL" -eq 1 ] && exit 1
echo "✅ Layer 3 green — no new off-scale values beyond the pinned debt."
exit 0
