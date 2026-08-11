#!/usr/bin/env bash
# scripts/verify-common.sh — shared roots + guards for the static layers (1-5).
#
# Sourced, never executed.  Defines WHERE each layer looks, in one place, so
# the five verifiers cannot drift apart when the repo is restructured.
#
# ── Why these roots (wasayel-platform structure, differs from the origin repo) ──
#   The origin repo had many apps split as `Apps/<App>/Frontend` + `/Backend`,
#   with shared UI in `libs/frontend/`.  We have ONE unified host app
#   (`apps/V1.App`) and the UI lives in libraries:
#
#     libs/widgets/ACommerce.Widgets          — the component library (Ac* widgets)
#     libs/templates/**/Components/Pages      — the PAGES (68 @page routes)
#     libs/kits/**/wwwroot                    — kit-owned CSS (Maps)
#     apps/V1.App/wwwroot/branding            — per-tenant branding CSS
#
#   So "page consumer" here means libs/templates + apps, NOT apps alone.
#   Lowercase `apps`/`libs` is deliberate: CI runs ubuntu-latest (case-sensitive)
#   while Windows resolves either — only lowercase works in both.

ROOT="${ROOT:-$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)}"

# Every root that may contain .razor or .css we care about.
WSL_ALL_ROOTS=${WSL_ALL_ROOTS:-"libs apps"}

# Page-consumer roots: rules about "don't hand-roll what a widget provides"
# apply here.  libs/widgets is EXCLUDED on purpose — it is the widget library
# itself and must use raw HTML to implement the widgets.
WSL_PAGE_ROOTS=${WSL_PAGE_ROOTS:-"libs/templates apps"}

# Resolve roots to existing absolute paths; die loudly if a root vanished.
wsl_roots() {
    local out=() r
    for r in $1; do
        if [ -d "$ROOT/$r" ]; then
            out+=("$ROOT/$r")
        else
            echo "FATAL: configured root '$r' does not exist under $ROOT" >&2
            return 1
        fi
    done
    printf '%s\n' "${out[@]}"
}

# find .razor / .css under a root set, excluding build output.
wsl_find() {
    local ext="$1" roots="$2"
    local dirs
    mapfile -t dirs < <(wsl_roots "$roots") || return 1
    find "${dirs[@]}" -name "*.$ext" \
        -not -path '*/bin/*' -not -path '*/obj/*' 2>/dev/null | sort
}

# Path relative to repo root, portable (realpath --relative-to is GNU-only and
# chokes on mixed Windows drive prefixes in some shells).
wsl_rel() { echo "${1#"$ROOT"/}"; }

# ── The zero-subject guard ────────────────────────────────────────────────
# The lesson Layer 6 taught: "0 violations" printed by a tool that inspected
# nothing is indistinguishable from "0 violations" printed by a tool that
# inspected everything.  Every layer therefore declares how many subjects it
# found, and a layer that found none FAILS rather than passing silently.
wsl_require_subjects() {
    local label="$1" count="$2" minimum="${3:-1}"
    if [ "$count" -lt "$minimum" ]; then
        echo ""
        echo "  ✗ BLIND CHECK: '$label' inspected $count subjects (expected >= $minimum)."
        echo "    A verifier with nothing to verify is not a passing verifier."
        echo "    Either the repo moved and the roots in scripts/verify-common.sh"
        echo "    are stale, or the check has lost its subject and should be removed."
        return 1
    fi
    echo "  · $label: $count subject(s) inspected"
    return 0
}
