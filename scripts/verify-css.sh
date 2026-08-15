#!/usr/bin/env bash
# Usage: ./scripts/verify-css.sh
#
# LAYER 2 — Class existence.
#   "Does every class used in .razor resolve to a rule in some .css?"
#
# Catches typos and dangling references (`class="ac-crad"`), plus malformed
# CSS declarations (a value with no property name — `16px;` instead of
# `padding: 16px;`), which silently kill the *rest* of the rule in every
# browser.
#
# Ported from acommerce-lab/acommerce-platform.  Two deliberate changes:
#   1. Roots come from scripts/verify-common.sh (we have no `Apps/` tree).
#   2. The malformed-declaration scanner was rewritten from python3 to awk.
#      python3 is not present on the Windows dev machine (only the Store
#      stub), so the origin version silently scanned ZERO files there while
#      still printing "Malformed decls: 0".  awk is present everywhere.

set -eo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=scripts/verify-common.sh
source "$SCRIPT_DIR/verify-common.sh"

TMP=$(mktemp -d)
trap 'rm -rf "$TMP"' EXIT

echo "═══════════════════════════════════════════════"
echo "   Layer 2 — CSS class existence"
echo "═══════════════════════════════════════════════"
echo ""

wsl_find razor "$WSL_ALL_ROOTS" > "$TMP/razor-files.txt"
wsl_find css   "$WSL_ALL_ROOTS" > "$TMP/css-files.txt"
RAZOR_FILES=$(wc -l < "$TMP/razor-files.txt" | tr -d ' ')
CSS_FILES=$(wc -l < "$TMP/css-files.txt" | tr -d ' ')

echo "--- Subjects ---"
wsl_require_subjects "razor files" "$RAZOR_FILES"
wsl_require_subjects "css files"   "$CSS_FILES"

# ── Classes used in razor ─────────────────────────────────────────────────
# Literal class="a b c" attributes, split on whitespace.
xargs -a "$TMP/razor-files.txt" -d '\n' grep -hoE 'class="[^"]+"' 2>/dev/null |
  sed 's/class="//; s/"$//' |
  tr ' \t' '\n\n' |
  grep -E '^[a-z][a-z0-9_-]*$' |
  sort -u > "$TMP/used.txt" || true

# Literals inside razor expressions: class="@(cond ? "ac-x" : "ac-y")"
#
# The `sed` pass first deletes comparison OPERANDS — `Active == "home"`,
# `Section != "terms"`.  Those literals are C# values being tested, never
# classes being emitted; the origin extractor kept them and manufactured 9
# phantom "undefined classes" here (home, stores, terms, privacy, returns,
# ideas, new, user, category).  What survives the strip is the emitted
# branch of the ternary, which IS a class.
xargs -a "$TMP/razor-files.txt" -d '\n' grep -hoE 'class="[^"]*@\([^)]+\)[^"]*"' 2>/dev/null |
  sed -E 's/[!=]=[[:space:]]*"[^"]*"//g' |
  grep -oE '"[a-z][a-z0-9_-]*"' |
  tr -d '"' |
  grep -E '^[a-z][a-z0-9_-]*$' |
  sort -u >> "$TMP/used.txt" || true
sort -u -o "$TMP/used.txt" "$TMP/used.txt"

# ── Classes defined in css ────────────────────────────────────────────────
xargs -a "$TMP/css-files.txt" -d '\n' grep -hoE '\.[a-z][a-z0-9_-]*' 2>/dev/null |
  sed 's/^\.//' |
  sort -u > "$TMP/defined.txt" || true

# ── Ignore list ───────────────────────────────────────────────────────────
# State/flag classes toggled from C# and styled only via a compound selector
# (`.ac-chip.active`), plus razor-expression false positives. Carried over
# from the origin list, trimmed to what actually fires here.
cat > "$TMP/ignore.txt" <<'EOF'
active
all
ar
cancelled
closed
confirmed
danger
dark
delivered
disabled
en
error
hidden
info
invalid
is
light
list
loading
map
mine
modified
null
offers
open
pending
preparing
primary
r
ready
recommended
secondary
sr-only
success
true
false
unread
valid
vendors
visually-hidden
warning
EOF

comm -23 "$TMP/used.txt" "$TMP/defined.txt" | grep -vxF -f "$TMP/ignore.txt" > "$TMP/undefined.txt" || true
comm -13 "$TMP/used.txt" "$TMP/defined.txt" > "$TMP/unused.txt" || true

USED_CNT=$(wc -l < "$TMP/used.txt" | tr -d ' ')
DEFINED_CNT=$(wc -l < "$TMP/defined.txt" | tr -d ' ')
UNDEF_CNT=$(wc -l < "$TMP/undefined.txt" | tr -d ' ')
UNUSED_CNT=$(wc -l < "$TMP/unused.txt" | tr -d ' ')

# `WSL_DUMP=unused|undefined ./scripts/verify-css.sh` prints that list alone,
# one name per line, and exits.  Layers 3, 4 and 5 already dump their
# fingerprints; this layer reported its LARGEST number (614 defined-but-
# unused) with nothing behind it, so "614 dead classes" could be neither
# reviewed nor acted on.  It dumps BEFORE the report so the output is
# pipeable; a gating run never sets the variable.
case "${WSL_DUMP:-}" in
    unused)    cat "$TMP/unused.txt";    exit 0 ;;
    undefined) cat "$TMP/undefined.txt"; exit 0 ;;
esac

wsl_require_subjects "classes used in razor"  "$USED_CNT"
wsl_require_subjects "classes defined in css" "$DEFINED_CNT"

# ── Malformed declarations (awk rewrite of the origin python3 pass) ───────
echo ""
echo "--- Malformed CSS declarations (value with no property name) ---"
: > "$TMP/malformed.txt"
while IFS= read -r css; do
    awk -v path="$css" '
        BEGIN { depth = 0; incomment = 0; cont = 0 }
        {
            line = $0
            # Strip /* ... */, including comments spanning lines.
            out = ""
            i = 1
            while (i <= length(line)) {
                if (incomment) {
                    p = index(substr(line, i), "*/")
                    if (p == 0) { i = length(line) + 1 }
                    else { incomment = 0; i = i + p + 1 }
                } else {
                    p = index(substr(line, i), "/*")
                    if (p == 0) { out = out substr(line, i); i = length(line) + 1 }
                    else { out = out substr(line, i, p - 1); incomment = 1; i = i + p + 1 }
                }
            }
            gsub(/^[ \t]+|[ \t]+$/, "", out)

            # Brace depth is counted BEFORE the decision, matching the origin.
            n = length(out)
            for (k = 1; k <= n; k++) {
                c = substr(out, k, 1)
                if (c == "{") depth++
                else if (c == "}") depth--
            }

            if (depth <= 0 || out == "") { cont = 0; next }
            if (substr(out, 1, 1) == "@" || substr(out, n, 1) == "{") { cont = 0; next }

            opens = gsub(/\(/, "(", out); closes = gsub(/\)/, ")", out)
            # A line continues onto the next when it ends in "," or ":" or
            # leaves a paren open.  The ":" case is an addition over the
            # origin, which false-positived on the very common
            #     background:
            #         linear-gradient(...);
            # reporting the VALUE line as "a value with no property name".
            last = substr(out, length(out), 1)
            is_cont = (last == "," || last == ":" || opens > closes)

            if (cont)     { cont = is_cont; next }
            if (is_cont)  { cont = 1; next }

            if (index(out, ";") == 0) next
            if (index(out, ":") > 0) next
            if (index(out, "{") > 0 || index(out, "}") > 0) next
            printf "%s:%d:%s\n", path, NR, out
        }
    ' "$css" >> "$TMP/malformed.txt"
done < "$TMP/css-files.txt"

MALFORMED_CNT=$(wc -l < "$TMP/malformed.txt" | tr -d ' ')
if [ "$MALFORMED_CNT" -gt 0 ]; then
    echo "  ✗ $MALFORMED_CNT malformed declaration(s):"
    head -20 "$TMP/malformed.txt" | sed "s|$ROOT/|    |"
else
    echo "  ✓ none"
fi

echo ""
echo "═══════════════════════════════════════════════"
echo "   Layer 2 report"
echo "═══════════════════════════════════════════════"
echo "  Razor files scanned: $RAZOR_FILES"
echo "  CSS files scanned:   $CSS_FILES"
echo "  Classes used:        $USED_CNT"
echo "  Classes defined:     $DEFINED_CNT"
echo "  UNDEFINED:           $UNDEF_CNT   (build breakers)"
echo "  Malformed decls:     $MALFORMED_CNT   (build breakers)"
echo "  Defined-but-unused:  $UNUSED_CNT   (informational only)"
echo ""

FAIL=0
if [ "$MALFORMED_CNT" -gt 0 ]; then
    echo "Every declaration must be property:value; — a bare value voids the rest of the rule."
    FAIL=1
fi

# ── Pinned debt ───────────────────────────────────────────────────────────
# Same contract as the FlowInventoryTests dead-state ledger: the breach is
# recorded BY NAME, so it stays visible; a NEW breach fails the gate, and a
# FIXED breach also fails it — because a stale pin is a lie about the debt.
BASELINE="$SCRIPT_DIR/verify-css-baseline.txt"
if [ -f "$BASELINE" ]; then
    grep -vE '^\s*(#|$)' "$BASELINE" | sort -u > "$TMP/baseline.txt"
    comm -23 "$TMP/undefined.txt" "$TMP/baseline.txt" > "$TMP/new.txt" || true
    comm -13 "$TMP/undefined.txt" "$TMP/baseline.txt" > "$TMP/fixed.txt" || true
    NEW_CNT=$(wc -l < "$TMP/new.txt" | tr -d ' ')
    FIXED_CNT=$(wc -l < "$TMP/fixed.txt" | tr -d ' ')
    PINNED_CNT=$(( UNDEF_CNT - NEW_CNT ))

    echo "  Pinned debt (baseline): $PINNED_CNT   New: $NEW_CNT   Retired: $FIXED_CNT"
    echo ""

    if [ "$NEW_CNT" -gt 0 ]; then
        echo "=== ✗ NEW UNDEFINED CLASSES (not in the pinned baseline) ==="
        while IFS= read -r cls; do
            echo "  ✗ .$cls"
            # `|| true` guards the SIGPIPE that `head -3` sends to xargs; under
            # `set -eo pipefail` that would abort the loop after a few classes
            # and under-report the real count.
            { xargs -a "$TMP/razor-files.txt" -d '\n' grep -l "class=\"[^\"]*\b$cls\b" 2>/dev/null || true; } |
                head -3 | sed "s|$ROOT/|      used in: |" || true
        done < "$TMP/new.txt"
        FAIL=1
    fi

    if [ "$FIXED_CNT" -gt 0 ]; then
        echo "=== ✗ STALE BASELINE ENTRIES (fixed in code, still pinned) ==="
        sed 's/^/  ✗ ./' "$TMP/fixed.txt"
        echo "    Remove these from scripts/verify-css-baseline.txt — the debt shrank,"
        echo "    and a ledger that overstates the debt stops being read."
        FAIL=1
    fi
else
    if [ "$UNDEF_CNT" -gt 0 ]; then
        echo "=== ✗ UNDEFINED CLASSES (used in .razor, defined in no .css) ==="
        sed 's/^/  ✗ ./' "$TMP/undefined.txt"
        FAIL=1
    fi
fi

[ "$FAIL" -eq 1 ] && exit 1
echo "✅ Layer 2 green — no new undefined classes, every declaration well-formed."
exit 0
