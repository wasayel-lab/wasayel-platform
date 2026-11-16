#!/usr/bin/env bash
# Usage: ./scripts/verify-api-contract.sh
#
# LAYER 10 — no redirect and no HTML under /api/v1 (lexical, no build,
#            no server).
#   "Does every answer under /api/v1 look like an answer to a MACHINE?"
#
# ── لِماذا هذِه الطَبَقَة، وبِأَيّ رَقَم ────────────────────────────────
# ‏**281** نِداءَ `Results.Redirect` في مِلَفّ النِقاط القائِم — مَقيسَة.
# وهذا هُوَ الانزِلاقُ الأَشيَع في هذا المُستَودَع بِفارِقٍ هائِل: يُكتَب
# بِلا تَفكير لِأَنَّه الشَكلُ الصَحيحُ لِنَموذَجِ HTML.
#
# **وأَثَرُه على عَميلٍ آلِيّ قاتِلٌ وصامِت**: العَميلُ يَتبَعُ التَحويلَ
# فَيَصِلُ صَفحَةَ دُخولٍ بِـ‏200، فَيَظُنُّ الرَفضَ نَجاحاً ويُفَكِّك
# HTML على أَنَّه جَوابُه. وقَد قيسَ الشَكلُ نَفسُه حَيّاً في هذا
# المُستَودَع: نَفسُ العَمَلِيَّة تُجيب ‏403 مِن `/admin/…/branding/save`
# و‏302 مِن `/studio/…/branding/save`.
#
# **و`Results.Forbid()` مَمنوعٌ لِسَبَبٍ أَشَدّ**: المِنَصَّة لا تَستَدعي
# `AddAuthentication` إطلاقاً، فَـ`ForbidHttpResult.ExecuteAsync` يَرمي
# `InvalidOperationException` — أَي أَنّ رَفضَ الصَلاحِيَّة يُخرِج **‏500
# لا ‏403**. عَطَبٌ مُثَبَّتٌ بِاختِبارٍ (`ForbidResultTests`)، ومَمنوعٌ
# هُنا نَصّاً كَي لا يُكتَب ثانِيَةً.
#
# ── ورَقمُ الحالَةِ يَأتي مِن المَعجَم لا مِن مَوضِع النِداء ────────────
# القاعِدَة ٤: السِياسَةُ بَيانات. `ApiErrorCatalog` يَحمِل لِكُلّ رَمزٍ
# حالَتَه؛ فَرَقَمٌ مَكتوبٌ بِاليَد في جِسمِ نُقطَةٍ هُوَ **تَعريفٌ ثانٍ
# لِقَرارٍ واحِد**، ويَنجَرِف. ولِذلك يُمنَع هُنا أَيضاً.
#
# ── حارِسُ العَمى ───────────────────────────────────────────────────────
# العَدَدُ يُطبَع، والصِفرُ يُحمِر (القاعِدَة ١٠). وهذا الفاحِصُ قيسَ
# بِحَقنِ عَيبٍ مُصطَنَع قَبل أَن يُوثَق بِه.

set -eo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=scripts/verify-common.sh
source "$SCRIPT_DIR/verify-common.sh"

TMP=$(mktemp -d)
trap 'rm -rf "$TMP"' EXIT

# نِطاقُ الفاحِص: مِلَفُّ السَطح ومَعَه المِلَفّاتُ الَّتي تَبني
# جَوابَه. ولِماذا الثَلاثَةُ لا واحِد: الرَدُّ لا يُبنى في جِسم
# النُقطَة بَل في `ApiOutcome` و`ApiErrors` — فَحَصرُ الفَحصِ في
# المِلَفّ الأَوَّل يَترُك البابَ الَّذي يُكتَب مِنه التَحويلُ فِعلاً.
SCOPE=(
  "libs/templates/ACommerce.Templates.Customer.Marketplace/Api/ApiV1Endpoints.cs"
  "libs/templates/ACommerce.Templates.Customer.Marketplace/Api/ApiOutcome.cs"
  "libs/templates/ACommerce.Templates.Customer.Marketplace/Api/ApiErrors.cs"
  "libs/templates/ACommerce.Templates.Customer.Marketplace/Gates/ApiKeyFilter.cs"
)

echo "═══════════════════════════════════════════════"
echo "   Layer 10 — no redirect and no HTML under /api/v1"
echo "═══════════════════════════════════════════════"
echo ""

if ! command -v perl > /dev/null 2>&1; then
    echo "  ✗ BLIND CHECK: perl not found — the scanner cannot run." >&2
    echo "    A checker that cannot check must fail, not report zero." >&2
    exit 1
fi

cat > "$TMP/strip.pl" <<'PERL'
use strict; use warnings;
use open ':std', ':encoding(UTF-8)';
local $/; my $s = <>;
$s =~ s{/\*.*?\*/}{}gs;
$s =~ s{^\s*//.*$}{}gm;
print $s;
PERL

VIOLATIONS=0
report() { echo "  ✗ $1"; VIOLATIONS=$((VIOLATIONS + 1)); }

echo "--- Subjects ---"
FILES=0
: > "$TMP/all.txt"
for f in "${SCOPE[@]}"; do
    if [ ! -f "$ROOT/$f" ]; then
        echo "  ✗ BLIND CHECK: a scoped file is missing: $f" >&2
        echo "    الفاحِصُ الَّذي فَقَدَ مَوضوعَه يَفشَل، ولا يُبَلِّغ صِفراً." >&2
        exit 1
    fi
    FILES=$((FILES + 1))
    echo "  · $f"
    perl "$TMP/strip.pl" < "$ROOT/$f" | perl -ne "print \"$f:\$.:\$_\"" >> "$TMP/all.txt"
done
wsl_require_subjects "API surface files" "$FILES" 4

LINES=$(wc -l < "$TMP/all.txt" | tr -d ' ')
wsl_require_subjects "scanned lines of code" "$LINES" 100

# ── القاعِدَة ١٠٫١: لا تَحويل ────────────────────────────────────────────
echo ""
echo "--- Rule 10.1: no Results.Redirect ---"
if grep -E 'Results\s*\.\s*Redirect|Results\s*\.\s*LocalRedirect|StatusCode\(\s*30[0-9]' "$TMP/all.txt" > "$TMP/r1.txt"; then
    while IFS= read -r hit; do report "a redirect under /api/v1: $hit"; done < "$TMP/r1.txt"
    echo "      عَميلٌ آلِيٌّ يَتبَعُ التَحويلَ فَيَقرَأُ صَفحَةَ دُخولٍ بِـ200."
else
    echo "  ✓ none"
fi

# ── القاعِدَة ١٠٫٢: لا Results.Forbid ────────────────────────────────────
echo ""
echo "--- Rule 10.2: no Results.Forbid (it throws 500 here) ---"
if grep -E 'Results\s*\.\s*Forbid' "$TMP/all.txt" > "$TMP/r2.txt"; then
    while IFS= read -r hit; do report "Results.Forbid() under /api/v1: $hit"; done < "$TMP/r2.txt"
    echo "      IAuthenticationService غَير مُسَجَّلَة — الرَفضُ يَخرُج 500."
else
    echo "  ✓ none"
fi

# ── القاعِدَة ١٠٫٣: لا HTML ──────────────────────────────────────────────
echo ""
echo "--- Rule 10.3: no HTML content type or view result ---"
if grep -E 'text/html|Results\s*\.\s*Content\(|Results\s*\.\s*File\(|\.razor' "$TMP/all.txt" > "$TMP/r3.txt"; then
    while IFS= read -r hit; do report "HTML under /api/v1: $hit"; done < "$TMP/r3.txt"
else
    echo "  ✓ none"
fi

# ── القاعِدَة ١٠٫٤: حالَةُ الخَطَأِ مِن المَعجَم لا مِن اليَد ───────────
# **واستِثناءانِ مُعلَنانِ لا مَبلوعان**، وكِلاهُما «المَوضِعُ الَّذي
# يُعَرِّف» لا «المَوضِعُ الَّذي يَستَعمِل»:
#
#   ١. `ApiErrors.cs` — هُوَ المَعجَمُ نَفسُه. حالاتُه هي التَعريف،
#      ومَنعُها فيه يَعني مَنعَ المَعجَمِ مِن أَن يوجَد.
#   ٢. `Status200OK` — الجَوابُ الناجِحُ الوَحيد، ولا رَمزَ خَطَأٍ لَه.
#      يَعيش في `ApiOutcome.Ok` في مَوضِعٍ واحِد.
#
# وهذا الاستِثناءُ الثاني **كَتَبَه الفاحِصُ نَفسُه**: أَوَّلُ صيغَةٍ
# مَنَعَت كُلَّ رَقمٍ فَاحمَرَّت على `Ok`. والقاعِدَةُ الصَحيحَةُ
# أَضيَق: **حالَةُ الخَطَأ** هي الَّتي تَأتي مَع رَمزِها.
echo ""
echo "--- Rule 10.4: error statuses come from the catalog, not from a literal ---"
if grep -E 'statusCode\s*:\s*[0-9]{3}|StatusCode\(\s*[0-9]{3}\s*\)|StatusCodes\.Status[0-9]' "$TMP/all.txt" \
   | grep -v 'ApiErrors\.cs' | grep -v 'Status200OK' > "$TMP/r4.txt"; then
    while IFS= read -r hit; do report "a hand-written status code: $hit"; done < "$TMP/r4.txt"
    echo "      الحالَةُ تَأتي مَع الرَمز مِن ApiErrorCatalog — تَعريفٌ واحِد."
else
    echo "  ✓ none"
fi

# ── القاعِدَة ١٠٫٥: كُلُّ رَمزِ خَطَأٍ عُضوٌ في المَعجَم ─────────────────
# الرُموزُ تُنادى بِثابِتٍ مِن `ApiErrorCatalog` — لا بِسِلسِلَةٍ
# حَرفِيَّة. وسِلسِلَةٌ حَرفِيَّةٌ في مَوضِع النِداء هي بِعَينِها الطَرَف
# الَّذي تَرَكَه `PermissionCatalog` مَفتوحاً فَانفَرَط.
echo ""
echo "--- Rule 10.5: error codes are catalog constants, never string literals ---"
if grep -E '(ApiError\.Of|ApiOutcome\.Error)\s*\(\s*"' "$TMP/all.txt" > "$TMP/r5.txt"; then
    while IFS= read -r hit; do report "a string-literal error code: $hit"; done < "$TMP/r5.txt"
else
    echo "  ✓ none"
fi

USES=$(grep -cE '(ApiError\.Of|ApiOutcome\.Error)\s*\(' "$TMP/all.txt" || true)
echo "  · error sites inspected: $USES"
wsl_require_subjects "error call sites" "$USES" 5

# ── التَقرير ────────────────────────────────────────────────────────────
echo ""
echo "═══════════════════════════════════════════════"
if [ "$VIOLATIONS" -ne 0 ]; then
    echo "Layer 10 red — $VIOLATIONS violation(s)."
    echo "‏§٤٫٤: JSON حَصراً، وخَطَأٌ مُوَحَّدٌ بِرَمزٍ مِن مَعجَمٍ مُغلَق."
    exit 1
fi
echo "✅ Layer 10 green — $FILES file(s), $LINES line(s), $USES error site(s); no redirect, no HTML."
exit 0
