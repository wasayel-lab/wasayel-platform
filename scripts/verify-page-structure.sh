#!/usr/bin/env bash
# Usage: ./scripts/verify-page-structure.sh
#
# LAYER 1 — Code hygiene (lexical).
#   "Are pages written against the component library, or hand-rolled?"
#
# Forbids Bootstrap leftovers (btn / col-* / row / card / alert / spinner /
# form-control / table), raw inline colours that bypass the theme tokens, and
# UI-only operations being round-tripped through the HTTP engine.
#
# ── Scope: which files are "pages" here ──────────────────────────────────
# The origin repo scoped this to `Apps/` because its pages lived there and
# its libraries were allowed raw HTML.  Our pages live in
# `libs/templates/**/Components/**` (68 @page routes) and the host app is
# `apps/`; `libs/widgets` is EXCLUDED because it is the component library
# itself and must use raw HTML to implement AcButton, AcCard, AcAlert…
# Same rule, same intent, different address.
#
# ── Rules dropped in the port, and why ───────────────────────────────────
# Origin Rule 12 (AuthController must call PhoneNormalization before querying
# by PhoneNumber) and Rule 13 (AddControllers must not scan a piggy-backed
# service assembly) are NOT ported.  Measured: this repo contains zero
# `*Controller.cs`, zero `PhoneNumber ==` lookups and zero `AddControllers`
# calls — it is Blazor Server over Flows/kits, with no MVC layer at all.
# A ported check would have inspected 0 subjects and printed a green tick
# forever, which is worse than not having it.  If controllers are ever
# introduced, both rules should be revived from the origin repo.

set -eo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=scripts/verify-common.sh
source "$SCRIPT_DIR/verify-common.sh"

VIOLATIONS=0
TMP=$(mktemp -d)
trap 'rm -rf "$TMP"' EXIT
: > "$TMP/violations.txt"

report() {
    local rule="$1" file="$2" line="$3" content="$4"
    echo "  ✗ [$rule] $(wsl_rel "$file"):$line" >> "$TMP/violations.txt"
    echo "      ${content:0:160}" >> "$TMP/violations.txt"
    VIOLATIONS=$((VIOLATIONS + 1))
}

echo "═══════════════════════════════════════════════"
echo "   Layer 1 — Page structure / code hygiene"
echo "═══════════════════════════════════════════════"
echo ""

wsl_find razor "$WSL_PAGE_ROOTS" |
    grep -vE '/(App|Routes|MainLayout|_Imports)\.razor$' > "$TMP/files.txt"
FILE_CNT=$(wc -l < "$TMP/files.txt" | tr -d ' ')

echo "--- Subjects ---"
wsl_require_subjects "page files" "$FILE_CNT"
echo ""

scan() {  # scan <rule-name> <extended-regex>
    local rule="$1" re="$2"
    while IFS=: read -r file line content; do
        [ -z "$file" ] && continue
        report "$rule" "$file" "$line" "$content"
    done < <({ xargs -a "$TMP/files.txt" -d '\n' grep -HnE "$re" 2>/dev/null || true; })
}

# ── Rule 1/2: Bootstrap button classes ───────────────────────────────────
# TOKEN-ACCURATE, and this is a correction over the origin.  The origin used
# `\bbtn\b`, but `-` is not a word character, so `ac-btn` and `st-btn` — our
# own widget classes — matched it.  On this repo that regex reported 51
# "violations", every one of them a false positive.  A class token counts
# only when it IS `btn` or STARTS `btn-`.
BTN_TOKEN='class="([^"]* )?btn(-[a-z]+)*( |")'
scan "no-raw-bootstrap-button" "<button[^>]*$BTN_TOKEN"
scan "no-bootstrap-btn-link"   "<a[^>]*$BTN_TOKEN"

# ── Rule 3: Bootstrap grid ───────────────────────────────────────────────
scan "no-bootstrap-grid" 'class="[^"]*\b(col-md-|col-sm-|col-lg-|col-xs-)[0-9]'
scan "no-bootstrap-row"  'class="(row"|row )|class="[^"]* row(\s|")'

# ── Rules 4-7, 9: other Bootstrap leftovers ──────────────────────────────
scan "no-table-layout"       '<table[^>]*class="(table|table-)'
scan "no-bootstrap-spinner"  'class="([^"]* )?spinner-border( |")'
scan "no-bootstrap-alert"    'class="[^"]*\balert\s+alert-'
scan "no-bootstrap-card"     'class="(card|card-body|card-header)( |")'
scan "no-form-control"       'class="([^"]* )?form-control( |")'

# ── Rule 8: inline colours must go through the theme tokens ──────────────
# The whole point of the --wsl-* -> --ac-* indirection (META-MODEL §9) is
# that a tenant can restyle the app.  A literal #hex in a style="" attribute
# is invisible to that layer and can never be themed.
#
# The origin skipped any line containing `var(--ac-`.  That exemption is
# both redundant and harmful here: the regex below already anchors on the
# VALUE, so `color: var(--ac-text)` can never match it, while the exemption
# did suppress genuinely hardcoded values on mixed lines such as
#   style="background:#dcfce7;border:1px solid var(--st-success);"
# — 5 real violations hidden behind a var() that belonged to a different
# property.  Anchoring on the value alone is the correct test.
scan "no-inline-color" \
    'style="[^"]*color:\s*#[0-9a-fA-F]{3,8}|style="[^"]*background(-color)?:\s*(#[0-9a-fA-F]{3,8}|rgb)'

# ── Rule 10: UI-only operations must not round-trip through HTTP ─────────
scan "ui-op-routed-to-http" \
    'Engine\.(Execute|Dispatch)[A-Za-z]*Async.*ClientOps\.(SetLanguage|SetTheme|SignOut|SetRtl)'

# ── Rule 11: RCL CSS cohesion ────────────────────────────────────────────
# Every referenced library that SHIPS a stylesheet must have that stylesheet
# linked from the host document; otherwise its components render unstyled.
# Re-pointed for our graph: one host document (the Marketplace App.razor),
# and the reference closure walked from apps/*.csproj through libs/*.
echo "--- Rule 11: every CSS-shipping referenced library is linked ---"
APP_RAZOR=$(wsl_find razor "$WSL_PAGE_ROOTS" | grep -E '/App\.razor$' | head -1)
if [ -z "$APP_RAZOR" ]; then
    echo "  ✗ no App.razor host document found — cannot verify CSS cohesion"
    VIOLATIONS=$((VIOLATIONS + 1))
else
    echo "  · host document: $(wsl_rel "$APP_RAZOR")"
    grep -oE '_content/[^/]+/' "$APP_RAZOR" | sed 's|_content/||; s|/$||' | sort -u > "$TMP/linked.txt"

    # Reference closure from the host app csproj (2 hops covers our depth:
    # app -> template -> {widgets, shared, maps}).
    : > "$TMP/refs.txt"
    for app_csproj in $(find "$ROOT/apps" -name '*.csproj' -not -path '*/bin/*' -not -path '*/obj/*'); do
        echo "$app_csproj" >> "$TMP/refs.txt"
        for hop1 in $(grep -oE 'Include="[^"]*\.csproj"' "$app_csproj" | sed 's/Include="//; s/"$//' | tr '\\' '/'); do
            p1="$(cd "$(dirname "$app_csproj")" && cd "$(dirname "$hop1")" 2>/dev/null && pwd)/$(basename "$hop1")"
            [ -f "$p1" ] || continue
            echo "$p1" >> "$TMP/refs.txt"
            for hop2 in $(grep -oE 'Include="[^"]*\.csproj"' "$p1" | sed 's/Include="//; s/"$//' | tr '\\' '/'); do
                p2="$(cd "$(dirname "$p1")" && cd "$(dirname "$hop2")" 2>/dev/null && pwd)/$(basename "$hop2")"
                [ -f "$p2" ] && echo "$p2" >> "$TMP/refs.txt"
            done
        done
    done
    sort -u -o "$TMP/refs.txt" "$TMP/refs.txt"

    CSS_LIBS=0
    while IFS= read -r csproj; do
        pkg=$(basename "$csproj" .csproj)
        libdir=$(dirname "$csproj")
        # Only libraries matter; the host app serves its own wwwroot directly.
        case "$libdir" in "$ROOT/apps"*) continue ;; esac
        # `|| true`: find exits 1 when wwwroot/ does not exist, and under
        # `set -eo pipefail` that aborts the whole scan at the first
        # CSS-less library — silently, having checked almost nothing.
        sheet=$({ find "$libdir/wwwroot" -name '*.css' 2>/dev/null || true; } | head -1)
        [ -z "$sheet" ] && continue
        CSS_LIBS=$((CSS_LIBS + 1))
        if ! grep -qxF "$pkg" "$TMP/linked.txt"; then
            report "unlinked-rcl-css [$pkg]" "$APP_RAZOR" "1" \
                "referenced via csproj and ships $(wsl_rel "$sheet"), but App.razor never links _content/$pkg/*.css — its rules are dead"
        fi
    done < "$TMP/refs.txt"
    wsl_require_subjects "CSS-shipping referenced libraries" "$CSS_LIBS"
fi

# ── Report ───────────────────────────────────────────────────────────────
echo ""
echo "═══════════════════════════════════════════════"
echo "   Layer 1 report"
echo "═══════════════════════════════════════════════"
echo "  Page files scanned: $FILE_CNT"
echo "  Violations:         $VIOLATIONS"
echo ""

if [ "$VIOLATIONS" -gt 0 ]; then
    echo "Counts by rule:"
    grep -oE '^  ✗ \[[^]]+\]' "$TMP/violations.txt" | sed 's/  ✗ \[//; s/\]//' |
        sed 's/ \[.*//' | sort | uniq -c | sort -rn | awk '{printf "  %4d  %s\n", $1, $2}'
    echo ""
fi

# Fingerprint each violation as `rule|file|count` — deliberately WITHOUT the
# line number, so that unrelated edits above a violation do not churn the
# ledger, while adding or removing one in an already-dirty file still does.
grep -E '^  ✗ \[' "$TMP/violations.txt" |
    sed -E 's/^  ✗ \[([^]]+(\[[^]]*\])?)\] ([^:]+):.*/\1|\3/' |
    sed -E 's/^([a-z-]+) \[[^]]*\]\|/\1|/' |
    sort | uniq -c | awk '{printf "%s|%d\n", $2, $1}' | sort > "$TMP/current.txt"

BASELINE="$SCRIPT_DIR/verify-page-structure-baseline.txt"
if [ ! -f "$BASELINE" ]; then
    echo "No baseline present. Full violation list:"
    cat "$TMP/violations.txt"
    [ "$VIOLATIONS" -gt 0 ] && exit 1
    echo "✅ Layer 1 green."
    exit 0
fi

grep -vE '^\s*(#|$)' "$BASELINE" | sort > "$TMP/pinned.txt"
comm -23 "$TMP/current.txt" "$TMP/pinned.txt" > "$TMP/new.txt" || true
comm -13 "$TMP/current.txt" "$TMP/pinned.txt" > "$TMP/gone.txt" || true
NEW_CNT=$(wc -l < "$TMP/new.txt" | tr -d ' ')
GONE_CNT=$(wc -l < "$TMP/gone.txt" | tr -d ' ')

echo "  Pinned debt entries: $(wc -l < "$TMP/pinned.txt" | tr -d ' ')   New: $NEW_CNT   Retired: $GONE_CNT"
echo ""

FAIL=0
if [ "$NEW_CNT" -gt 0 ]; then
    echo "=== ✗ NEW violations (not in the pinned baseline) ==="
    sed 's/^/  ✗ /' "$TMP/new.txt"
    echo ""
    echo "Details:"
    while IFS='|' read -r rule file _; do
        grep -F "[$rule]" "$TMP/violations.txt" | grep -F "$file" | head -4
    done < "$TMP/new.txt"
    FAIL=1
fi
if [ "$GONE_CNT" -gt 0 ]; then
    echo "=== ✗ STALE baseline entries (fixed in code, still pinned) ==="
    sed 's/^/  ✗ /' "$TMP/gone.txt"
    echo "    Remove them from scripts/verify-page-structure-baseline.txt."
    FAIL=1
fi

[ "$FAIL" -eq 1 ] && exit 1
echo "✅ Layer 1 green — no new structural violations beyond the pinned debt."
exit 0
