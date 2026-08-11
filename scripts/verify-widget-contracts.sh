#!/usr/bin/env bash
# Usage: ./scripts/verify-widget-contracts.sh
#
# LAYER 5 — Widget property contracts (completeness).
#   "Does each contracted widget DECLARE the properties it needs to be
#    visible and usable — padding, border, background, colour, min-height?"
#
# Layer 3 catches WRONG values.  This layer catches MISSING properties: an
# .ac-input with no padding renders with its text glued to the frame; an
# .ac-card with no border has no visible shape at all.
#
# Contracts live in scripts/widget-contracts.json.
#
# ── Two corrections over the origin implementation ──────────────────────
#
# 1. SCOPE — served sheets only.
#    The origin concatenated every .css under libs/ and Apps/.  Here that
#    would read stylesheets the browser never loads: Layer 1 rule 11 shows
#    that libs/widgets/.../widgets.css and ACommerce.Templates.Shared's
#    sheet are referenced but never linked from App.razor, and the widgets
#    copy has diverged from the served one by 51 lines.  A contract
#    satisfied only by a dead sheet is a contract that fails on screen, so
#    this layer resolves the sheets App.razor actually links and reads only
#    those.  The list is printed on every run.
#
# 2. RULE EXTRACTION — single-line rules.
#    The origin's awk walker set `inblock=1` on the line matching the
#    selector and only cleared it on a LATER line containing `}`.  For a
#    single-line rule — `.ac-payform { display: flex; gap: 12px; }`, which
#    is how most of our widget CSS is written — it therefore kept consuming
#    the NEXT rules too, and any property they happened to declare counted
#    as satisfying the contract.  Demonstrated: a contract for .ac-payform
#    requiring padding+border passed on the strength of the following
#    unrelated rule.  Replaced with a normaliser that emits one rule per
#    line before matching.

set -eo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=scripts/verify-common.sh
source "$SCRIPT_DIR/verify-common.sh"

CONTRACTS="${CONTRACTS:-$SCRIPT_DIR/widget-contracts.json}"
TMP=$(mktemp -d)
trap 'rm -rf "$TMP"' EXIT

VIOLATIONS=0; CHECKED=0
: > "$TMP/violations.txt"
: > "$TMP/keys.txt"
report() { echo "$1" >> "$TMP/keys.txt"; echo "  ✗ $2" >> "$TMP/violations.txt"; VIOLATIONS=$((VIOLATIONS + 1)); }

echo "═══════════════════════════════════════════════"
echo "   Layer 5 — widget property contracts"
echo "═══════════════════════════════════════════════"
echo ""

[ -f "$CONTRACTS" ] || { echo "  ✗ contracts file missing: $CONTRACTS"; exit 1; }

# ── Resolve the stylesheets the host document actually links ─────────────
APP_RAZOR=$(wsl_find razor "$WSL_PAGE_ROOTS" | grep -E '/App\.razor$' | head -1)
[ -n "$APP_RAZOR" ] || { echo "  ✗ no App.razor found — cannot resolve served stylesheets"; exit 1; }

: > "$TMP/sheets.txt"
while IFS= read -r link; do
    pkg=${link#_content/}; pkg=${pkg%%/*}
    rest=${link#_content/"$pkg"/}
    libdir=$(find "$ROOT/libs" "$ROOT/apps" -type d -name "$pkg" -not -path '*/bin/*' -not -path '*/obj/*' 2>/dev/null | head -1)
    [ -n "$libdir" ] && [ -f "$libdir/wwwroot/$rest" ] && echo "$libdir/wwwroot/$rest" >> "$TMP/sheets.txt"
done < <(grep -oE '_content/[^"]+\.css' "$APP_RAZOR" | sort -u)

SHEET_CNT=$(sort -u "$TMP/sheets.txt" | grep -c . || true)
echo "--- Served stylesheets (linked from $(wsl_rel "$APP_RAZOR")) ---"
sort -u "$TMP/sheets.txt" | while IFS= read -r s; do echo "  · $(wsl_rel "$s")"; done
echo ""
wsl_require_subjects "served stylesheets" "$SHEET_CNT"

# ── Normalise CSS: one rule per line, as `selectors {decls}` ─────────────
# Comments are stripped, newlines collapsed, then the text is split at `}`.
# For a segment the last `{` separates selector text from declarations, so
# rules nested in @media resolve to their inner selector correctly.
sort -u "$TMP/sheets.txt" | xargs cat |
awk '
    BEGIN { RS = "\0" }
    {
        gsub(/\/\*([^*]|\*+[^*\/])*\*+\//, " ")   # strip comments
        gsub(/[\r\n\t]+/, " ")
        n = split($0, seg, "}")
        for (i = 1; i <= n; i++) {
            s = seg[i]
            p = 0
            for (k = length(s); k > 0; k--) if (substr(s, k, 1) == "{") { p = k; break }
            if (p == 0) continue
            sel = substr(s, 1, p - 1); dec = substr(s, p + 1)
            gsub(/^ +| +$/, "", sel); gsub(/^ +| +$/, "", dec)
            if (sel == "" || dec == "") continue
            printf "%s {%s}\n", sel, dec
        }
    }
' > "$TMP/rules.txt"

RULE_CNT=$(wc -l < "$TMP/rules.txt" | tr -d ' ')
wsl_require_subjects "CSS rules parsed" "$RULE_CNT" 100

# ── Contract checking ────────────────────────────────────────────────────
# All declarations belonging to a selector, across every rule that targets
# it (base rule plus modifiers/media overrides).
decls_for() {
    local sel="$1"
    # `|| true`: grep exits 1 when the selector appears nowhere, which under
    # `set -e` killed the run before it could report "NO CSS RULE FOUND" —
    # i.e. the one branch that matters most was the one that could not run.
    { grep -F "$sel" "$TMP/rules.txt" || true; } |
        awk -v sel="$sel" '
            {
                p = index($0, "{")
                head = substr($0, 1, p - 1)
                # the selector must appear as a whole token in the selector text
                if (head ~ ("(^|[ ,>+~])" sel "([ ,{:.>+~]|$)")) print substr($0, p)
            }
        '
}

check_selector() {
    local selector="$1" required="$2"
    CHECKED=$((CHECKED + 1))

    local block; block=$(decls_for "$selector")
    if [ -z "$block" ]; then
        report "$selector|NO-RULE" "$selector: NO CSS RULE FOUND in any served sheet (contract requires:$required)"
        return
    fi

    local missing="" prop
    for prop in $required; do
        case "$prop" in
            border)        echo "$block" | grep -qE '(^|[ ;{])border(-(width|style|color|top|right|bottom|left|inline|block))?:' || missing="$missing $prop" ;;
            border-bottom) echo "$block" | grep -qE '(^|[ ;{])border(-bottom)?:'                                              || missing="$missing $prop" ;;
            padding)       echo "$block" | grep -qE '(^|[ ;{])padding(-(top|right|bottom|left|inline|block))?:'                || missing="$missing $prop" ;;
            margin-bottom) echo "$block" | grep -qE '(^|[ ;{])margin(-bottom)?:'                                              || missing="$missing $prop" ;;
            background)    echo "$block" | grep -qE '(^|[ ;{])background(-color|-image)?:'                                    || missing="$missing $prop" ;;
            *)             echo "$block" | grep -qE "(^|[ ;{])${prop}:"                                                       || missing="$missing $prop" ;;
        esac
    done

    if [ -n "$missing" ]; then
        report "$selector|missing:$(echo "$missing" | tr -s ' ' | sed 's/^ //; s/ /,/g')" \
               "$selector missing required propert$([ "$(echo "$missing" | wc -w)" -gt 1 ] && echo ies || echo y):$missing"
    fi
}

echo ""
echo "--- Checking contracts ---"
current=""
while IFS= read -r line; do
    if echo "$line" | grep -qE '"\.[a-z][a-z0-9_-]*":[[:space:]]*\{'; then
        current=$(echo "$line" | grep -oE '"\.[a-z][a-z0-9_-]*"' | tr -d '"')
    fi
    if echo "$line" | grep -qE '"required":' && [ -n "$current" ]; then
        required=$(echo "$line" | grep -oE '\[[^]]*\]' | tr -d '[]",' | tr -s ' ')
        [ -n "$required" ] && check_selector "$current" "$required"
        current=""
    fi
done < "$CONTRACTS"

wsl_require_subjects "widget contracts checked" "$CHECKED"

echo ""
echo "═══════════════════════════════════════════════"
echo "   Layer 5 report"
echo "═══════════════════════════════════════════════"
echo "  Served stylesheets: $SHEET_CNT"
echo "  CSS rules parsed:   $RULE_CNT"
echo "  Contracts checked:  $CHECKED"
echo "  Violations:         $VIOLATIONS"
echo ""
[ "$VIOLATIONS" -gt 0 ] && { cat "$TMP/violations.txt"; echo ""; }

sort -u "$TMP/keys.txt" > "$TMP/current.txt"
if [ -n "${WSL_DUMP:-}" ]; then cat "$TMP/current.txt"; exit 0; fi

BASELINE="$SCRIPT_DIR/verify-widget-contracts-baseline.txt"
if [ ! -f "$BASELINE" ]; then
    [ "$VIOLATIONS" -gt 0 ] && { echo "No baseline present; $VIOLATIONS violation(s) above."; exit 1; }
    echo "  ✅ Every contracted widget satisfies its visual baseline."
    exit 0
fi

grep -vE '^\s*(#|$)' "$BASELINE" | sort -u > "$TMP/pinned.txt"
comm -23 "$TMP/current.txt" "$TMP/pinned.txt" > "$TMP/new.txt" || true
comm -13 "$TMP/current.txt" "$TMP/pinned.txt" > "$TMP/gone.txt" || true
NEW_CNT=$(wc -l < "$TMP/new.txt" | tr -d ' '); GONE_CNT=$(wc -l < "$TMP/gone.txt" | tr -d ' ')
echo "  Pinned debt entries: $(wc -l < "$TMP/pinned.txt" | tr -d ' ')   New: $NEW_CNT   Retired: $GONE_CNT"
FAIL=0
[ "$NEW_CNT" -gt 0 ]  && { echo "=== ✗ NEW contract breaches ==="; sed 's/^/  ✗ /' "$TMP/new.txt"; FAIL=1; }
[ "$GONE_CNT" -gt 0 ] && { echo "=== ✗ STALE baseline entries (satisfied now, still pinned) ==="; sed 's/^/  ✗ /' "$TMP/gone.txt"; FAIL=1; }
[ "$FAIL" -eq 1 ] && exit 1
echo "  ✅ Layer 5 green — no new contract breaches beyond the pinned debt."
exit 0
