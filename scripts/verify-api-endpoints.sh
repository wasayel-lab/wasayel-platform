#!/usr/bin/env bash
# Usage: ./scripts/verify-api-endpoints.sh
#
# LAYER 9 — an /api/v1 endpoint body never touches storage (lexical, no build,
#           no server).
#   "Can the parallel path even be WRITTEN?"
#
# ── لِماذا هذِه الطَبَقَة ───────────────────────────────────────────────
# وَثيقَةُ سَطح الـAPI (‏§٤٫١) تَقول: «جِسمُ نُقطَةِ الـAPI لا يَقبَل
# `IDocumentStore` ولا `IDocumentSession`. يَقبَل خِدمَةً فَقَط. فَإن لَم
# توجَد الخِدمَة، لا تُكشَف النُقطَة حَتّى تُستَخرَج.»
#
# وهذِه لَيسَت نَصيحَةَ أُسلوب بَل **الضَمانَةُ الوَحيدَة** الَّتي تَمنَع
# «المَسارَ المُوازي». والقِياسُ الَّذي كَتَبَها: سِتُّ عَمَلِيّاتٍ في
# المُستَودَع مَكتوبَةٌ **مَرَّتَين** — `/admin/tenants/{slug}/*` و
# `/studio/apps/{slug}/*` — وانحِرافُها مَقيسٌ بِخَمسَةِ فُروق، مِنها
# **بروتوكولا رَفضٍ مُختَلِفان** (‏403 مُقابِل 302) لِنَفس العَمَلِيَّة.
# ولَم يَقَع ذلك عَن إهمال بَل لِأَنّ كِتابَةَ النُسخَةِ الثانِيَة كانَت
# **مُمكِنَة**: كِلا الجِسمَين يَفتَح جَلسَتَه بِيَدِه.
#
# فَالحَدُّ هُنا **بِنيَوِيّ**: مِلَفٌّ لا يَملِك جَلسَةً لا يَستَطيع
# تَكرارَ مَنطِقٍ حَتّى لَو أَرادَ كاتِبُه.
#
# ── ولِماذا ساكِنٌ لا اختِبارُ وَحدَة ──────────────────────────────────
# ‏`EndpointStoreInjectionTests` يَفحَص **وُسَطاءَ اللامدا** في كُلّ
# المُستَودَع بِسِجِلٍّ مُثَبَّتٍ يَتَقَلَّص. وهذا يَفحَص **مِلَفّاً
# واحِداً بِلا سِجِلّ ولا استِثناء** — يَبدَأُ مِن صِفرٍ ويَبقى صِفراً،
# لِأَنّ المِلَفَّ جَديدٌ ولا دَينَ فيه. الشَرطُ الوَحيد أَن يُكتَب
# نَظيفاً مِن يَومِه، وهذا هُوَ ما يَحرُسُه.
#
# ── حارِسُ العَمى ───────────────────────────────────────────────────────
# القاعِدَة ١٠: «صِفرُ مُخالَفَة» مِن أَداةٍ فَحَصَت صِفراً لا يُمَيَّز
# عَن «صِفرُ مُخالَفَة» مِن أَداةٍ فَحَصَت كُلَّ شَيء. فَالعَدَدُ يُطبَع،
# والصِفرُ يُحمِر — وهذا الفاحِصُ نَفسُه قيسَ بِحَقنِ عَيبٍ مُصطَنَع
# قَبل أَن يُوثَق بِه (المُلحَق في `docs/API-SURFACE-DESIGN.md`).

set -eo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=scripts/verify-common.sh
source "$SCRIPT_DIR/verify-common.sh"

TMP=$(mktemp -d)
trap 'rm -rf "$TMP"' EXIT

SURFACE="libs/templates/ACommerce.Templates.Customer.Marketplace/Api/ApiV1Endpoints.cs"
PREFIX='/api/v1'

echo "═══════════════════════════════════════════════"
echo "   Layer 9 — /api/v1 endpoint bodies never touch storage"
echo "═══════════════════════════════════════════════"
echo ""

if ! command -v perl > /dev/null 2>&1; then
    echo "  ✗ BLIND CHECK: perl not found — the scanner cannot run." >&2
    echo "    A checker that cannot check must fail, not report zero." >&2
    exit 1
fi

VIOLATIONS=0
report() { echo "  ✗ $1"; VIOLATIONS=$((VIOLATIONS + 1)); }

# ── ١. أَينَ تَعيشُ نِقاطُ /api/v1 فِعلاً؟ ──────────────────────────────
# النِطاقُ يَفرِضُ نَفسَه: نُقطَةٌ تُسَجَّل خارِجَ مِلَفّ السَطح تُحمِر،
# وإلّا لَكانَ الفاحِصُ يَحرُس مِلَفّاً بَينَما يُكتَب المَسارُ المُوازي
# في مِلَفٍّ آخَر.
echo "--- Scope: every $PREFIX registration lives in the surface file ---"

find "$ROOT/libs" "$ROOT/apps" -type f \( -name '*.cs' -o -name '*.razor' \) \
     -not -path '*/bin/*' -not -path '*/obj/*' -print0 \
  | xargs -0 grep -lE "Map(Get|Post|Put|Delete|Patch)\s*\(\s*\"$PREFIX" 2>/dev/null \
  | perl -pe 's{^\Q'"$ROOT"'\E/}{}' | perl -pe 's{\\}{/}g' | sort > "$TMP/hosts.txt" || true

HOST_CNT=$(wc -l < "$TMP/hosts.txt" | tr -d ' ')
echo "  · files registering $PREFIX endpoints: $HOST_CNT"
while IFS= read -r f; do
    [ -z "$f" ] && continue
    echo "      $f"
    if [ "$f" != "$SURFACE" ]; then
        report "an $PREFIX endpoint is registered outside the surface file: $f"
        echo "      كُلُّ نُقطَةِ API في مِلَفٍّ واحِد — وإلّا صارَ النِطاقُ دَعوى."
    fi
done < "$TMP/hosts.txt"

if [ ! -f "$ROOT/$SURFACE" ]; then
    echo "  ✗ BLIND CHECK: the surface file is missing: $SURFACE" >&2
    exit 1
fi

# ── ٢. عَدُّ النِقاط داخِلَ المِلَفّ ─────────────────────────────────────
ENDPOINTS=$(grep -cE "Map(Get|Post|Put|Delete|Patch)\s*\(\s*\"$PREFIX" "$ROOT/$SURFACE" || true)
echo ""
echo "--- Subjects ---"
wsl_require_subjects "$PREFIX endpoints" "$ENDPOINTS" 1

# ── ٣. القاعِدَة: لا مَخزَنَ ولا جَلسَة في مِلَفّ السَطح ────────────────
# التَعليقاتُ تُبَيَّض أَوَّلاً: ذِكرُ `IDocumentSession` في تَعليقٍ شارِح
# لَيسَ لَمساً لِلتَخزين، وعَدُّه كَذلكَ يَجعَل الأَداةَ تَتَّهِم
# الوَثيقَةَ بِأَنَّها كود. (نَفسُ عِلَّةِ `StripComments`.)
cat > "$TMP/strip.pl" <<'PERL'
use strict; use warnings;
use open ':std', ':encoding(UTF-8)';
local $/; my $s = <>;
$s =~ s{/\*.*?\*/}{}gs;      # C# block + doc comments
$s =~ s{^\s*//.*$}{}gm;      # C# line comments
print $s;
PERL

perl "$TMP/strip.pl" < "$ROOT/$SURFACE" > "$TMP/code.txt"

echo ""
echo "--- Rule 9.1: no Marten store or session in the surface file ---"
FORBIDDEN_TYPES='IDocumentStore|IDocumentSession|IQuerySession|QuerySession\(|LightweightSession\(|SaveChangesAsync'
if grep -nE "$FORBIDDEN_TYPES" "$TMP/code.txt" > "$TMP/hits.txt"; then
    while IFS= read -r hit; do
        report "storage reached from the API surface: $hit"
    done < "$TMP/hits.txt"
    echo "      المَسارُ الصَحيح: خِدمَةٌ تَملِك الجَلسَة. فَإن لَم توجَد،"
    echo "      **لا تُكشَف النُقطَة** حَتّى تُستَخرَج (‏§٤٫١)."
else
    echo "  ✓ none"
fi

# ── ٤. القاعِدَة: كُلُّ نُقطَةٍ تُعلِن حارِسَها ─────────────────────────
# الحِراسَةُ في التَوقيع لا في الجِسم (القاعِدَة ٦). و`RequireApiKey`
# يَجمَع الاعتِمادَ والنِطاقَ واستِحقاقَ `api.call` — فَنُقطَةٌ بِلا
# سَطرِه مَكشوفَةٌ ثَلاثَ مَرّات.
echo ""
echo "--- Rule 9.2: every endpoint declares .RequireApiKey(<scope>) ---"
GUARDS=$(grep -cE '\.RequireApiKey\(' "$TMP/code.txt" || true)
echo "  · endpoints: $ENDPOINTS   guards: $GUARDS"
if [ "$GUARDS" -lt "$ENDPOINTS" ]; then
    report "an $PREFIX endpoint carries no .RequireApiKey( — $GUARDS guard(s) for $ENDPOINTS endpoint(s)"
fi

# ── ٥. القاعِدَة: المُستَأجِرُ لا يُقرَأُ مِن الطَلَب ──────────────────
# ‏§٣٫٦: المُستَأجِرُ يُشتَقُّ مِن الاعتِماد ولا يُقبَل مِن الطَلَب
# أَبَداً — لا مِن مَسار، ولا رَأس، ولا جِسم. وهذا هُوَ الفَرقُ بَينَ
# «عَزلٍ مَفروض» و«عَزلٍ مَرجُوّ».
echo ""
echo "--- Rule 9.3: the tenant is never read from the request ---"
TENANT_FROM_REQUEST='RouteValues\[\s*"slug"|string\s+slug|Headers\[\s*"X-Tenant|tenant_slug'
if grep -nE "$TENANT_FROM_REQUEST" "$TMP/code.txt" > "$TMP/tenant.txt"; then
    while IFS= read -r hit; do
        report "the tenant is read from the request: $hit"
    done < "$TMP/tenant.txt"
    echo "      المُستَأجِرُ مِن وَثيقَةِ المِفتاح — `http.ApiPrincipal().TenantSlug`."
else
    echo "  ✓ none"
fi

# ── التَقرير ────────────────────────────────────────────────────────────
echo ""
echo "═══════════════════════════════════════════════"
if [ "$VIOLATIONS" -ne 0 ]; then
    echo "Layer 9 red — $VIOLATIONS violation(s)."
    echo "‏§٤٫١: جِسمُ نُقطَةِ الـAPI يَقبَل خِدمَةً فَقَط."
    exit 1
fi
echo "✅ Layer 9 green — $ENDPOINTS $PREFIX endpoint(s), none touches storage."
exit 0
