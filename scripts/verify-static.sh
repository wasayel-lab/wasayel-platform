#!/usr/bin/env bash
# Usage: ./scripts/verify-static.sh
#
# Runs the STATIC verification layers (1-5, 7-10) and summarises them.
#
# These layers read source files only — no build, no server, no browser — so
# they are the ones that can gate CI.  Layer 6 (verify-runtime, Playwright)
# needs a live app on :5050 and stays out of the gate; see
# docs/VERIFICATION-LAYERS.md.  Layers 7 and 8 are static and therefore in the
# gate, and keep their own numbers rather than taking 6's.
#
# Layers 9 and 10 arrived with the API surface (docs/API-SURFACE-DESIGN.md, section 5).
# They are narrower than 1-8 by design: they guard ONE file set — the
# /api/v1 surface — and they carry NO ledger and NO exception list, because
# the files are new and carry no debt.  The only condition is that they be
# written clean from day one, and that is exactly what they enforce.
#
# Every layer prints how much it inspected and fails if it inspected nothing.
# Each runs to completion even if an earlier one failed, so one broken layer
# does not hide the state of the others.

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR/.." || exit 1

LAYERS=(
    "1|Code hygiene        |verify-page-structure.sh"
    "2|Class existence     |verify-css.sh"
    "3|Per-value scale     |verify-design-tokens.sh"
    "4|Per-app diversity   |verify-design-quality.sh"
    "5|Widget contracts    |verify-widget-contracts.sh"
    "7|Literal-text debt   |verify-i18n-debt.sh"
    "8|Session in .razor   |verify-architecture.sh"
    "9|API bodies vs storage|verify-api-endpoints.sh"
    "10|API contract shape  |verify-api-contract.sh"
)

FAILED=0
declare -a RESULTS

for entry in "${LAYERS[@]}"; do
    IFS='|' read -r num name script <<< "$entry"
    echo ""
    echo "###############################################################"
    echo "# Layer $num — $name  ($script)"
    echo "###############################################################"
    if bash "$SCRIPT_DIR/$script"; then
        RESULTS+=("  ✅ Layer $num — $name  PASS")
    else
        RESULTS+=("  ❌ Layer $num — $name  FAIL")
        FAILED=1
    fi
done

echo ""
echo "═══════════════════════════════════════════════"
echo "   Static verification summary (layers 1-5, 7-10)"
echo "═══════════════════════════════════════════════"
printf '%s\n' "${RESULTS[@]}"
echo ""

if [ "$FAILED" -ne 0 ]; then
    echo "One or more static layers failed."
    echo "A layer fails only on a NEW breach or on a pinned entry that no longer"
    echo "fires — the existing measured debt is recorded in scripts/*-baseline.txt"
    echo "in scripts/verify-design-quality-ceilings.txt, in"
    echo "scripts/verify-i18n-debt-ledger.txt and in"
    echo "scripts/verify-architecture-ledger.txt — and does not fail the gate."
    exit 1
fi
echo "All nine static layers green."
exit 0
