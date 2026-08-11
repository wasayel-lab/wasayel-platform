#!/usr/bin/env bash
# Usage: ./scripts/verify-design-quality.sh
#
# LAYER 4 — Aggregate design diversity, scoped to what ONE user loads.
#   "How many DISTINCT values does the app actually ship?"
#   (Whether an individual value is on the scale is Layer 3's question.)
#
# Metrics: distinct colours, spacings, font-sizes, icon sizes; plus widget
# usage distribution and per-page button-size symmetry.
#
# ── Scope, rewritten for our structure ──────────────────────────────────
# The origin iterated `Apps/*/Frontend/*.csproj` and unioned each app's CSS
# with its `libs/frontend/*` references.  Neither path exists here: we ship
# ONE unified host (apps/V1.App) and there is no libs/frontend.
#
# The replacement scope is strictly better anyway — it is what the browser
# loads: the stylesheets App.razor actually links (resolved through
# _content/, exactly as Layer 5 does) plus the host app's own wwwroot CSS.
# The origin's csproj-reference union counted stylesheets that ship but are
# never linked; Layer 1 rule 11 proves we have two of those.
#
# ── Report-only in the origin; a RATCHET here ───────────────────────────
# The origin exits 0 always, "a quality report, not a gate".  A CI step that
# can never fail trains the eye to skip it, so this version pins today's
# measurement as a CEILING and fails if a number GROWS.  Nothing has to be
# cleaned up for it to pass today, and nothing can quietly get worse.
#
# The ratchet is deliberately ONE-DIRECTIONAL, unlike the name-based ledgers
# in Layers 1-3.  There, a retired entry must be removed or the ledger lies
# about a specific breach.  Here the number is a bound, not a claim: an app
# that drops from 129 colours to 120 has not made the ceiling false, only
# loose.  Failing CI for an improvement would punish exactly the work the
# layer exists to encourage.  Loosening is reported loudly instead.

set -o pipefail   # NOT set -e: grep returning 1 on no-match is normal here.
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=scripts/verify-common.sh
source "$SCRIPT_DIR/verify-common.sh"

TMP=$(mktemp -d)
trap 'rm -rf "$TMP"' EXIT

echo "═══════════════════════════════════════════════"
echo "   Layer 4 — per-app aggregate design diversity"
echo "═══════════════════════════════════════════════"
echo ""

# ── Resolve the CSS a user actually loads ────────────────────────────────
APP_RAZOR=$(wsl_find razor "$WSL_PAGE_ROOTS" | grep -E '/App\.razor$' | head -1)
[ -n "$APP_RAZOR" ] || { echo "  ✗ no App.razor found — cannot resolve the served scope"; exit 1; }

: > "$TMP/sheets.txt"
while IFS= read -r link; do
    pkg=${link#_content/}; pkg=${pkg%%/*}
    rest=${link#_content/"$pkg"/}
    libdir=$(find "$ROOT/libs" "$ROOT/apps" -type d -name "$pkg" -not -path '*/bin/*' -not -path '*/obj/*' 2>/dev/null | head -1)
    [ -n "$libdir" ] && [ -f "$libdir/wwwroot/$rest" ] && echo "$libdir/wwwroot/$rest" >> "$TMP/sheets.txt"
done < <(grep -oE '_content/[^"]+\.css' "$APP_RAZOR" | sort -u)
find "$ROOT/apps" -name '*.css' -not -path '*/bin/*' -not -path '*/obj/*' >> "$TMP/sheets.txt" 2>/dev/null
sort -u -o "$TMP/sheets.txt" "$TMP/sheets.txt"

SHEET_CNT=$(grep -c . "$TMP/sheets.txt")
echo "--- Scope: stylesheets loaded by the app ---"
while IFS= read -r s; do echo "  · $(wsl_rel "$s")"; done < "$TMP/sheets.txt"
echo ""
wsl_require_subjects "stylesheets in scope" "$SHEET_CNT" || exit 1
xargs -a "$TMP/sheets.txt" -d '\n' cat > "$TMP/all.css"

# ── Metrics ──────────────────────────────────────────────────────────────
COLORS=$(grep -hoE '#[0-9a-fA-F]{3,8}\b|rgba?\([^)]+\)|hsl\([^)]+\)' "$TMP/all.css" | tr '[:upper:]' '[:lower:]' | sort -u | grep -c .)
SPACINGS=$(grep -hoE '(padding|margin|gap|row-gap|column-gap)(-[a-z]+)?:[[:space:]]*[0-9.]+(px|rem|em)' "$TMP/all.css" | grep -oE '[0-9.]+(px|rem|em)' | sort -u | grep -c .)
FONTSIZES=$(grep -hoE 'font-size:[[:space:]]*[0-9.]+(px|rem|em)' "$TMP/all.css" | grep -oE '[0-9.]+(px|rem|em)' | sort -u | grep -c .)
wsl_find razor "$WSL_ALL_ROOTS" > "$TMP/razor.txt"
ICONSIZES=$(xargs -a "$TMP/razor.txt" -d '\n' grep -hoE '<AcIcon[^>]+Size="[0-9]+"' 2>/dev/null | grep -oE 'Size="[0-9]+"' | sed 's/Size="//; s/"//' | sort -u | grep -c .)

wsl_require_subjects "distinct colours"    "$COLORS"    || exit 1
wsl_require_subjects "distinct spacings"   "$SPACINGS"  || exit 1
wsl_require_subjects "distinct font-sizes" "$FONTSIZES" || exit 1
wsl_require_subjects "distinct icon sizes" "$ICONSIZES" || exit 1

echo ""
echo "── 1-4. Aggregate diversity ──"
printf "  %-16s %8s %10s %s\n" "Metric" "Measured" "Reference" "Verdict"
printf "  %-16s %8s %10s %s\n" "------" "--------" "---------" "-------"

# Reference limits carried over from the origin (well-known usability caps).
verdict() { [ "$1" -le "$2" ] && echo "within" || echo "OVER by $(( $1 - $2 ))"; }
printf "  %-16s %8s %10s %s\n" "colours"    "$COLORS"    "60" "$(verdict "$COLORS" 60)"
printf "  %-16s %8s %10s %s\n" "spacings"   "$SPACINGS"  "20" "$(verdict "$SPACINGS" 20)"
printf "  %-16s %8s %10s %s\n" "font-sizes" "$FONTSIZES" "10" "$(verdict "$FONTSIZES" 10)"
printf "  %-16s %8s %10s %s\n" "icon-sizes" "$ICONSIZES" "6"  "$(verdict "$ICONSIZES" 6)"

# ── 5. Widget usage distribution (informational) ─────────────────────────
echo ""
echo "── 5. Widget usage, top 10 ──"
xargs -a "$TMP/razor.txt" -d '\n' grep -hoE '<Ac[A-Z][a-zA-Z]+' 2>/dev/null |
    sort | uniq -c | sort -rn | head -10 | awk '{ printf "  %5d  %s\n", $1, substr($2,2) }'

# ── 6. Per-page button-size symmetry ─────────────────────────────────────
echo ""
echo "── 6. Per-page sibling symmetry ──"
MIXED=0
while IFS= read -r f; do
    if grep -q 'Size="sm"' "$f" && grep -q 'Size="lg"' "$f"; then
        echo "  ⚠ mixed sm+lg on one page: $(wsl_rel "$f")"
        MIXED=$((MIXED + 1))
    fi
done < "$TMP/razor.txt"
echo "  pages mixing small and large button sizes: $MIXED"

# ── Section 7 of the origin is NOT ported ────────────────────────────────
# "Container hierarchy: does every page root use an approved layout?"  The
# origin carried a hardcoded list of ~35 page-component names, all of them
# its own (AcMarketplaceHomePage, AcVendorDashboard, …).  None exist here.
# Rebuilding the list from what our pages currently root on would approve
# whatever happens to exist and report zero violations forever — a check
# that cannot fail, which is the exact failure mode this port exists to
# remove.  A real container hierarchy has to be DECIDED before it can be
# enforced; that is a design decision, not a measurement.
echo ""
echo "── 7. Container hierarchy — not ported (see comment in this script) ──"

# ── Ratchet ──────────────────────────────────────────────────────────────
printf 'colours|%s\nspacings|%s\nfont-sizes|%s\nicon-sizes|%s\n' \
    "$COLORS" "$SPACINGS" "$FONTSIZES" "$ICONSIZES" | sort > "$TMP/current.txt"
if [ -n "${WSL_DUMP:-}" ]; then cat "$TMP/current.txt"; exit 0; fi

CEILINGS="$SCRIPT_DIR/verify-design-quality-ceilings.txt"
echo ""
echo "═══════════════════════════════════════════════"
echo "   Layer 4 report"
echo "═══════════════════════════════════════════════"

if [ ! -f "$CEILINGS" ]; then
    echo "  No ceilings file; nothing to ratchet against."
    exit 0
fi

FAIL=0
while IFS='|' read -r metric ceiling; do
    case "$metric" in ''|\#*) continue ;; esac
    measured=$(grep "^$metric|" "$TMP/current.txt" | cut -d'|' -f2)
    [ -z "$measured" ] && continue
    if [ "$measured" -gt "$ceiling" ]; then
        echo "  ✗ $metric GREW: $measured (ceiling $ceiling)"
        FAIL=1
    elif [ "$measured" -lt "$ceiling" ]; then
        echo "  ↓ $metric improved: $measured (ceiling $ceiling) — tighten the ceiling in $(basename "$CEILINGS")"
    else
        echo "  = $metric at ceiling: $measured"
    fi
done < <(grep -vE '^\s*(#|$)' "$CEILINGS")

if [ "$FAIL" -eq 1 ]; then
    echo ""
    echo "A diversity metric grew. Either reuse an existing value, or raise the"
    echo "ceiling deliberately and say why — the ceiling is a decision, not a fact."
    exit 1
fi
echo ""
echo "✅ Layer 4 green — no diversity metric grew beyond its pinned ceiling."
exit 0
