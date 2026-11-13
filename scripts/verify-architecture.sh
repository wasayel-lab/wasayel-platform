#!/usr/bin/env bash
# Usage: ./scripts/verify-architecture.sh  [--emit-ledger]
#
# LAYER 8 — Marten sessions opened from .razor  (lexical, no build, no server).
#   "How many pages talk to the database directly, are any of them WRITING,
#    and is that number going down?"
#
# ── لِماذا هذِه الطَبَقَة، وبِأَيّ رَقَم ───────────────────────────────
# قاسَت وَثيقَة القَرار المِعماريّ ‏55 مِلَفّ `.razor` يَفتَح جَلسَة
# Marten. والاستِنتاج الشائِع مِن هذا الرَقَم **أَثقَل مِمّا يَنبَغي**،
# والتَفصيل يَقلِب الأَولَوِيَّة:
#
#     يَفتَح QuerySession (‏قِراءَة)      51   تَسَرُّب استِعلام — لا خَطَر ذَرِّيَّة
#     يَفتَح LightweightSession           4   ChatRoom, Notifications,
#                                             StudioAppTicketDetail, TenantListingDetail
#     يَكتُب فِعلاً (SaveChangesAsync)    3   ← وهذِه وَحدَها الخَطَر
#
# ولِذلك **السِجِلّ يُمَيِّز الصِنفَين**، ولا يُعامِل صَفحَةً تَقرَأ
# مُعامَلَةَ صَفحَةٍ تَكتُب. الصَفحَة الَّتي تَكتُب تَخرُج مِن
# المُعامَلَة ومِن الصُندوق الصادِر مَعاً؛ والَّتي تَقرَأ تُكَلِّف
# استِعلاماً لا ذَرِّيَّة.
#
# ── ما يَحمَرّ وما لا يَحمَرّ (‏ثُنائيّ الاتِّجاه) ──────────────────────
#   ١. مِلَفّ يَفتَح جَلسَةً وهُوَ غَير مُثَبَّت           ⟵ أَحمَر
#   ٢. مِلَفّ مُثَبَّت `R` صارَ يَكتُب                     ⟵ أَحمَر (تَصعيد)
#   ٣. إدخالَة مُثَبَّتَة لِمِلَفّ لَم يَعُد يَفتَح جَلسَة  ⟵ أَحمَر (تَرِثّ)
#   ٤. مِلَفّ مُثَبَّت `W` صارَ يَقرَأ فَقَط               ⟵ يُقال بِصَوتٍ ويَمُرّ
#   ٥. المَجاميع سُقوف: النُزول عَنها يُقال ويَمُرّ
#
# والسَبَب في أَنّ (‏٤) و(‏٥) لا يَحمَرّانِ دَرسٌ مِن الطَبَقَة الرابِعَة
# في المُستَودَع الأُمّ: سِجِلٌّ عَدَدِيّ صارِم في الاتِّجاهَين
# **يُعاقِب التَحسين** — نُزولٌ مِن ‏129 لَوناً إلى ‏120 أَحمَرَ بَوّابَةً
# وُجِدَت لِتُشَجِّعَه.
#
# ── حارِس العَمى ───────────────────────────────────────────────────────
# القاعِدَة ١٠: «صِفر مُخالَفَة» مِن أَداةٍ فَحَصَت صِفراً لا يُمَيَّز
# عَن «صِفر مُخالَفَة» مِن أَداةٍ فَحَصَت كُلَّ شَيء. فَالعَدَد يُطبَع،
# والصِفر يُحمِر.

set -eo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=scripts/verify-common.sh
source "$SCRIPT_DIR/verify-common.sh"

EMIT=0
[ "${1:-}" = "--emit-ledger" ] && EMIT=1

LEDGER="$SCRIPT_DIR/verify-architecture-ledger.txt"
TMP=$(mktemp -d)
trap 'rm -rf "$TMP"' EXIT

if [ "$EMIT" -eq 0 ]; then
    echo "═══════════════════════════════════════════════"
    echo "   Layer 8 — Marten sessions inside .razor"
    echo "═══════════════════════════════════════════════"
    echo ""
fi

if ! command -v perl > /dev/null 2>&1; then
    echo "  ✗ BLIND CHECK: perl not found — the classifier cannot run." >&2
    echo "    A checker that cannot check must fail, not report zero." >&2
    exit 1
fi

# ── المُصَنِّف ────────────────────────────────────────────────────────
# يُجَرِّد تَعليقات Razor/HTML/C# أَوَّلاً — فَذِكرُ `LightweightSession`
# في تَعليقٍ شارِح لَيسَ فَتحَ جَلسَة، وعَدُّه كَذلكَ يَجعَل الأَداة
# تَتَّهِم الوَثيقَة بِأَنَّها كود. (‏نَفس عِلَّة `StripComments` في
# `EntitlementContractTests`.)
cat > "$TMP/classify.pl" <<'PERL'
use strict; use warnings;
use open ':std', ':encoding(UTF-8)';
my @rows;
for my $f (@ARGV) {
    open(my $fh, '<:encoding(UTF-8)', $f) or next;
    local $/; my $s = <$fh>; close $fh;
    $s =~ s/\@\*.*?\*\@//gs;      # Razor comments
    $s =~ s/<!--.*?-->//gs;       # HTML comments
    $s =~ s{/\*.*?\*/}{}gs;       # C# block comments
    $s =~ s{^\s*//.*$}{}gm;       # C# line comments

    my $q = () = $s =~ /QuerySession\s*\(/g;
    my $l = () = $s =~ /LightweightSession\s*\(/g;
    my $w = () = $s =~ /SaveChangesAsync\s*\(/g;
    next unless ($q + $l) > 0;

    # الصِنف: `W` إن كَتَبَت فِعلاً أَو فَتَحَت جَلسَةً قابِلَةً
    # لِلكِتابَة؛ و`R` إن اكتَفَت بِالقِراءَة.
    my $kind = ($w > 0 || $l > 0) ? 'W' : 'R';
    push @rows, "$f|$kind|$q|$l|$w";
}
print "$_\n" for sort @rows;
PERL

wsl_find razor "$WSL_ALL_ROOTS" > "$TMP/files.txt"
FILE_CNT=$(wc -l < "$TMP/files.txt" | tr -d ' ')

if [ "$EMIT" -eq 0 ]; then
    echo "--- Subjects ---"
    wsl_require_subjects ".razor files" "$FILE_CNT" 50
    echo ""
fi

xargs -a "$TMP/files.txt" -d '\n' perl "$TMP/classify.pl" > "$TMP/raw.txt"

# مَسارات نِسبِيَّة لِلمُستَودَع — فَالسِجِلّ لا يَتَعَلَّق بِقُرصٍ ولا بِمُستَخدِم.
perl -pe 's{^\Q'"$ROOT"'\E/}{}' "$TMP/raw.txt" \
  | perl -pe 's{\\}{/}g' \
  | sort > "$TMP/current.txt"

SESSION_FILES=$(wc -l < "$TMP/current.txt" | tr -d ' ')
WRITE_FILES=$(perl -ne '$n++ if /\|W\|/; END { print $n // 0 }' "$TMP/current.txt")
READ_FILES=$((SESSION_FILES - WRITE_FILES))
ACTUAL_WRITES=$(perl -F'\|' -ane '$n += $F[4]; END { print $n // 0 }' "$TMP/current.txt")

if [ "$EMIT" -eq 1 ]; then
    echo "# scripts/verify-architecture-ledger.txt — الدَين المَقيس، مُثَبَّتاً بِأَسمائِه."
    echo "# وُلِّد بِـ: ./scripts/verify-architecture.sh --emit-ledger"
    echo "#"
    echo "# الصيغَة: <المَسار>|<الصِنف R|W>"
    echo "#   R = تَفتَح QuerySession فَقَط — تَسَرُّب استِعلام."
    echo "#   W = تَفتَح LightweightSession أَو تَستَدعي SaveChangesAsync —"
    echo "#       خارِج المُعامَلَة وخارِج الصُندوق الصادِر."
    echo "SESSION_FILES|$SESSION_FILES"
    echo "WRITE_FILES|$WRITE_FILES"
    perl -F'\|' -ane 'print "$F[0]|$F[1]\n"' "$TMP/current.txt"
    exit 0
fi

echo "--- Measurement ---"
echo "  .razor files scanned:            $FILE_CNT"
echo "  files opening a Marten session:  $SESSION_FILES"
echo "    · read-only (QuerySession):    $READ_FILES"
echo "    · writable (Lightweight/Save):  $WRITE_FILES"
echo "  SaveChangesAsync call sites:     $ACTUAL_WRITES"
echo ""

if [ ! -f "$LEDGER" ]; then
    echo "  ✗ no ledger at $(wsl_rel "$LEDGER")."
    echo "    Create it with: ./scripts/verify-architecture.sh --emit-ledger > $(wsl_rel "$LEDGER")"
    exit 1
fi

grep -vE '^[[:space:]]*(#|$)' "$LEDGER" > "$TMP/ledger.txt"
PINNED_SESSION=$(perl -F'\|' -ane 'print $F[1] if $F[0] eq "SESSION_FILES"' "$TMP/ledger.txt" | tr -d '\n')
PINNED_WRITE=$(perl -F'\|'   -ane 'print $F[1] if $F[0] eq "WRITE_FILES"'   "$TMP/ledger.txt" | tr -d '\n')
# ‏`|| true` لَيسَ تَساهُلاً بَل إصلاحُ عَطَبٍ ظَهَرَ يَومَ بَلَغَ الدَين
# صِفراً (المَوجَة ٧): سِجِلٌّ بِلا إدخالَةٍ واحِدَة يَجعَل `grep` يَرُدّ
# ‏1، فَيَموت السكربت تَحتَ `set -e` **صامِتاً بَعدَ طَبع القِياس** —
# فَيَبدو أَنَّه فَحَصَ ومَرّ، وهو لَم يَصِل إلى فَحصٍ واحِد. أَي أَنّ
# الأَداةَ كانَت **تَنكَسِر عِندَ النَجاح التامّ**. (القاعِدَة ١٠.)
grep -vE '^(SESSION_FILES|WRITE_FILES)\|' "$TMP/ledger.txt" | sort > "$TMP/pinned.txt" || true
PINNED_FILES=$(wc -l < "$TMP/pinned.txt" | tr -d ' ')

echo "--- Ledger ---"
echo "  pinned files: $PINNED_FILES   pinned sessions: $PINNED_SESSION   pinned writers: $PINNED_WRITE"
echo ""

FAIL=0
LOOSE=0

# ١. مِلَفّ يَفتَح جَلسَةً وهُوَ غَير مُثَبَّت، أَو صَعَّدَ R ⟶ W.
while IFS='|' read -r file kind _q _l _w; do
    [ -z "$file" ] && continue
    pin=$(perl -F'\|' -sane 'print $F[1] if $F[0] eq $f' -- -f="$file" "$TMP/pinned.txt" | tr -d '\n')
    if [ -z "$pin" ]; then
        echo "  ✗ NEW page opening a Marten session (not in ledger): $file  [$kind]"
        FAIL=1
    elif [ "$pin" = "R" ] && [ "$kind" = "W" ]; then
        echo "  ✗ ESCALATED to a writable session: $file  R → W"
        echo "      كِتابَةٌ مِن صَفحَة تَقَع خارِج المُعامَلَة وخارِج الصُندوق الصادِر."
        FAIL=1
    elif [ "$pin" = "W" ] && [ "$kind" = "R" ]; then
        echo "  · improved: $file  W → R   (السِجِلّ أَوسَع مِن الواقِع — شَدَّه مَجّانيّ)"
        LOOSE=$((LOOSE + 1))
    fi
done < "$TMP/current.txt"

# ٢. إدخالَة مُثَبَّتَة لِمِلَفّ لَم يَعُد يَفتَح جَلسَة — تُرفَع أَو تَرِثّ.
while IFS='|' read -r file pin; do
    [ -z "$file" ] && continue
    if ! perl -sne 'BEGIN{$found=0} $found=1 if /^\Q$f\E\|/; END{exit($found?0:1)}' \
            -- -f="$file" "$TMP/current.txt"; then
        echo "  ✗ STALE ledger entry: $file was pinned [$pin] and no longer opens a session —"
        echo "      strike it from $(wsl_rel "$LEDGER")."
        FAIL=1
    fi
done < "$TMP/pinned.txt"

# ٣. السُقوف.
if [ "$SESSION_FILES" -gt "$PINNED_SESSION" ]; then
    echo "  ✗ session-opening pages rose: $PINNED_SESSION → $SESSION_FILES"
    FAIL=1
fi
if [ "$WRITE_FILES" -gt "$PINNED_WRITE" ]; then
    echo "  ✗ writing pages rose: $PINNED_WRITE → $WRITE_FILES"
    FAIL=1
fi

echo ""
if [ "$FAIL" -eq 1 ]; then
    echo "Layer 8 red — a page reached for the database where it should not, or the"
    echo "ledger no longer describes the repo."
    echo "الدَين لا يَنمو إلّا بِقَرار مَرئيّ: انقُل الوُصول إلى مُعالِج،"
    echo "أَو عَدِّل السِجِلّ في نَفس الكوميت وقُل لِماذا."
    exit 1
fi

[ "$LOOSE" -gt 0 ] && echo "  ($LOOSE ledger entries are looser than reality — tightening is free.)"
echo "✅ Layer 8 green — no new page reached for a Marten session."

# ═══════════════════════════════════════════════════════════════════════
#  الطَبَقَة ٨-ب — كاتالوجُ المُزَوِّدين بَياناتٌ لا كود (‏ADR-012)
# ───────────────────────────────────────────────────────────────────────
#  القاعِدَة ٢: «لا SDK خارِجيّاً يُنادى مُباشَرَةً» حَدٌّ لا يَحفَظُه
#  اسمٌ ولا وَثيقَة — يَحفَظُه فاحِصٌ يَفشَل عِندَ خَرقِه. وهذا
#  الفاحِصُ يَسأَل ثَلاثَةَ أَسئِلَة يُجابُ عَنها **بِالعَدّ**:
#
#    ١. كَم مِلَفَّ تَعريفٍ في الكاتالوج؟ (وصِفرٌ = أَداةٌ عَمياء)
#    ٢. أَكُلُّ مِلَفٍّ عَلى القُرصِ مَذكورٌ في الفِهرِس؟ — فَتَعريفٌ
#       يُبنى ولا يُحَمَّل هُوَ **المَقبَرَةُ نَفسُها** الَّتي وَقَعَت
#       في الكود: سَبعَةُ مَشاريعِ مُزَوِّدين لا يُحيلُها csproj.
#    ٣. أَتُحيلُ مَكتَبَةُ الكاتالوجِ عُدَّةً واحِدَة؟ — المَعجَمُ
#       أَسماءٌ نَصِّيَّةٌ لا أَنواع، وأَيُّ إحالَةِ عُدَّةٍ تَقلِب
#       اتِّجاهَ الاعتِماد.
#
#  وحارِسُ العَمى هُنا كَما فَوقَه: العَدَدُ يُطبَع، والصِفرُ يُحمِر.
# ═══════════════════════════════════════════════════════════════════════

PROV_DIR="libs/core/ACommerce.Platform.Providers/Definitions"
PROV_CSPROJ="libs/core/ACommerce.Platform.Providers/ACommerce.Platform.Providers.csproj"
PROV_FAIL=0

echo ""
echo "═══════════════════════════════════════════════"
echo "   Layer 8b — provider catalogue is data"
echo "═══════════════════════════════════════════════"
echo ""

if [ ! -d "$PROV_DIR" ]; then
    echo "  ✗ BLIND CHECK: $PROV_DIR غَير مَوجود — الفاحِصُ لا يَجِد ما يَفحَص." >&2
    exit 1
fi

PROV_FILES=$(find "$PROV_DIR" -name '*.provider.json' | wc -l | tr -d ' ')
PROV_INDEXED=$(grep -oE '"[a-z][a-z0-9_]*"' "$PROV_DIR/providers.index.json" | tr -d '"' | grep -cv '^providers$')

echo "--- Measurement ---"
echo "  provider definition files: $PROV_FILES"
echo "  slugs listed in the index: $PROV_INDEXED"
echo ""

if [ "$PROV_FILES" -eq 0 ]; then
    echo "  ✗ BLIND CHECK: صِفرُ مِلَفِّ تَعريف — «صِفر مُخالَفَة» مِن أَداةٍ فَحَصَت صِفراً." >&2
    exit 1
fi

# ١. كُلُّ مِلَفٍّ عَلى القُرصِ مَذكورٌ في الفِهرِس.
for f in "$PROV_DIR"/*.provider.json; do
    slug=$(basename "$f" .provider.json)
    if ! grep -q "\"$slug\"" "$PROV_DIR/providers.index.json"; then
        echo "  ✗ ORPHAN: $slug.provider.json غَير مَذكورٍ في providers.index.json —"
        echo "      يُبنى ولا يُحَمَّل. هذِه هي مَقبَرَةُ السَبعَةِ الأَيتام في البَيانات."
        PROV_FAIL=1
    fi
done

# ٢. وكُلُّ سلاجٍ في الفِهرِسِ لَه مِلَفّ.
while read -r slug; do
    [ -z "$slug" ] && continue
    [ "$slug" = "providers" ] && continue
    if [ ! -f "$PROV_DIR/$slug.provider.json" ]; then
        echo "  ✗ MISSING: الفِهرِسُ يَذكُر «$slug» ولا مِلَفَّ لَه — يُفشِلُ الإقلاع."
        PROV_FAIL=1
    fi
done < <(grep -oE '"[a-z][a-z0-9_]*"' "$PROV_DIR/providers.index.json" | tr -d '"')

# ٣. مَكتَبَةُ الكاتالوجِ لا تُحيلُ عُدَّةً واحِدَة.
KIT_REFS=$(grep -cE 'ProjectReference[^>]*kits' "$PROV_CSPROJ" || true)
echo "  kit references from the catalogue library: $KIT_REFS"
if [ "$KIT_REFS" -ne 0 ]; then
    echo "  ✗ المَكتَبَةُ صارَت تُحيلُ عُدَّة — المَعجَمُ أَسماءٌ لا أَنواع،"
    echo "      وأَيُّ إحالَةٍ تَقلِبُ اتِّجاهَ الاعتِماد."
    PROV_FAIL=1
fi

echo ""
if [ "$PROV_FAIL" -eq 1 ]; then
    echo "Layer 8b red — تَعريفُ مُزَوِّدٍ لا يَبلُغ القارِئ، أَو الكاتالوجُ جَرَّ عُدَّة."
    exit 1
fi

echo "✅ Layer 8b green — $PROV_FILES تَعريفاً كُلُّها في الفِهرِس، وصِفرُ إحالَةِ عُدَّة."

# ═══════════════════════════════════════════════════════════════════════
#  الطَبَقَة ٨-ج — لا عَميلَ صادِراً خامّاً خارِجَ قائِمَتِه (‏2026-11-12، W13-6·١١ — ADR-051 §١٨)
# ───────────────────────────────────────────────────────────────────────
#  رَأسُ ٨-ب يُسَمّي الحَدَّ «لا SDK خارِجيّاً يُنادى مُباشَرَةً» ويَقيسُ البَياناتِ وَحدَها. وهذه تَقيسُ الكود — فَكُلُّ نِداءٍ يُغادِرُ العَمَلِيَّةَ
#  يَمُرُّ بِالمُستَدعي المَعدودِ (`ProviderHttp`/`ProviderCalls`) أَو يَحمَرّ:
#
#    ١. النَوع: مِلَفٌّ في libs/ يُسَمّي كودُه (بِلا تَعليقاتٍ ولا نُصوص) نَوعَ عَميلٍ خامٍّ أَو `.CreateClient(` وهُوَ لَيسَ في القائِمَة.
#    ٢. الفِعل: في مِلَفٍّ مُدرَجٍ بِحَقلِ SDK، وُصولٌ إلى عُضوٍ فيه (عَدا Dispose) خارِجَ وُسَطاءِ `InvokeAsync(` أَو `HelperAsync(` — بِعُمقِ الأَقواس.
#    ٣. الإعفاءُ المَيِّت: سَطرٌ في القائِمَةِ لِمِلَفٍّ غائِبٍ أَو لا يُسَمّي نَوعاً خامّاً.
#
#  ‏(‏2026-11-13، مُراجَعَةُ المَرحَلَةِ ج الأولى · M6C-RULES-R1-2 — ADR-051 §١٩) **والحَقلُ يُرى بِكُلِّ شَكلٍ يُستَعمَلُ بِه**: كانَ النَمَطُ
#  `\bحَقل\s*\.` وَحدَه — فَمَرَّ `_client?.X(…)` و`_client!.X(…)` والاسمُ المُستَعارُ (`var c = _client; c.X()`) والحَقلُ وَسيطاً (مَقيسٌ عِندَ المُراجِع:
#  `_client?.ListBucketsAsync` أَوَّلَ `DeleteAsync` ⇐ «violations: 0» والطَبَقَةُ خَضراء). الآنَ كُلُّ ذِكرٍ لِلحَقل: وُصولٌ بِـ`.`/`?.`/`!.`/`[`/`?[` (عَدا
#  `Dispose`) داخِلَ المُستَدعي أَو أَحمَر؛ والإعلانُ والإسنادُ إلَيه لا يُعَدّان؛ وكُلُّ ذِكرٍ سِواهُما **تَسَرُّبٌ** (`escape`) داخِلَ المُستَدعي أَو أَحمَر.
#  وسَطرُ قائِمَةٍ يُسَمّي حَقلاً ولا يَقيسُ لَه وُصولاً واحِداً (**حَقلٌ مَيِّت**) أَحمَر.
#  ولا رَقَمَ طَبَقَةٍ جَديد: داخِلَ ٨ فَلا تَتَغَيَّرُ أَسماءُ الطَبَقاتِ التِسع. وحارِسُ العَمى كَما فَوقَه: العَدَدُ يُطبَع، وصِفرُ مِلَفٍّ أَو صِفرُ رَمزٍ يُحمِر.
#  وتَوأَمُها في الاختِبارات: `OutboundInvokerBoundaryTests` (المُصَنِّفُ نَفسُه بِـC#، والحَقنُ المَقيس).
# ═══════════════════════════════════════════════════════════════════════

OUT_ALLOW="$SCRIPT_DIR/verify-outbound-allowlist.txt"

echo ""
echo "═══════════════════════════════════════════════"
echo "   Layer 8c — outbound clients only through the metered invoker"
echo "═══════════════════════════════════════════════"
echo ""

if [ ! -f "$OUT_ALLOW" ]; then
    echo "  ✗ BLIND CHECK: $(wsl_rel "$OUT_ALLOW") غائِب — لا قائِمَةَ يُقاسُ عَلَيها." >&2
    exit 1
fi

cat > "$TMP/outbound.pl" <<'PERL'
use strict; use warnings;
use open ':std', ':encoding(UTF-8)';
my %field;
{ open(my $fh, '<:encoding(UTF-8)', shift @ARGV) or die "allow-list"; while (<$fh>) { chomp; next if /^\s*(#|$)/; my ($p, $f) = split /\|/; $field{$p} = $f if defined $f && $f ne '-'; } close $fh; }
my $root = shift @ARGV;
my $TYPES = qr/\b(?:HttpClient|HttpMessageInvoker|IHttpClientFactory|SocketsHttpHandler|HttpClientHandler|AmazonS3Client|IAmazonS3|SmtpClient|WebPushClient|HubConnectionBuilder|ClientWebSocket|TcpClient|GrpcChannel)\b|\.CreateClient\(/;
sub strip {
    my $s = shift;
    $s =~ s{("""[\s\S]*?"""|\@"(?:""|[^"])*"|\$?"(?:\\.|[^"\\\n])*"|'(?:\\.|[^'\\\n])'|//[^\n]*|/\*[\s\S]*?\*/)}{ my $t = $1; ($t =~ m{^/[/*]}) ? " " : '""' }ge;
    return $s;
}
sub inside_invoke {
    my ($s, $at) = @_;
    my $depth = 0;
    for (my $i = $at - 1; $i >= 0; $i--) {
        my $c = substr($s, $i, 1);
        if ($c eq ')') { $depth++; }
        elsif ($c eq '(') {
            if ($depth > 0) { $depth--; next; }
            return 1 if substr($s, 0, $i) =~ /\b(?:InvokeAsync|HelperAsync)\s*(?:<[^<>()]*>)?\s*\z/;
        }
    }
    return 0;
}
for my $f (@ARGV) {
    open(my $fh, '<:encoding(UTF-8)', $f) or next;
    local $/; my $raw = <$fh>; close $fh;
    my $s = strip($raw);
    my $tokens = () = $s =~ /$TYPES/g;
    my ($acc, $bad, $esc, $named) = (0, 0, 0, 0);
    (my $rel = $f) =~ s{^\Q$root\E/}{};
    $rel =~ s{\\}{/}g;
    if (my $fld = $field{$rel}) {
        $named = 1;
        while ($s =~ /\b\Q$fld\E\b/g) {
            my ($pos, $end) = ($-[0], $+[0]);
            my $after = substr($s, $end, 160);
            my $from = $pos > 160 ? $pos - 160 : 0;
            my $before = substr($s, $from, $pos - $from);
            if ($after =~ /^\s*[?!]?\s*\.\s*(\w+)/) {
                next if $1 eq 'Dispose';
                $acc++;
                $bad++ unless inside_invoke($s, $pos);
                next;
            }
            if ($after =~ /^\s*[?!]?\s*\[/) { $acc++; $bad++ unless inside_invoke($s, $pos); next; }
            next if $after =~ /^\s*(?:=(?![=>])|\?\?=)/;
            next if $after =~ /^\s*[;,)]/ && $before =~ /[\w>\]?]\s+\z/
                 && $before !~ /\b(?:return|await|throw|yield|in|out|ref|is|as|case|when|else|and|or|not|new|nameof)\s+\z/;
            $acc++; $esc++;
            $bad++ unless inside_invoke($s, $pos);
        }
    }
    print "$rel|$tokens|$acc|$bad|$esc|$named\n";
}
PERL

find "$ROOT/libs" -name '*.cs' -not -path '*/bin/*' -not -path '*/obj/*' | sort > "$TMP/out-files.txt"
OUT_SCANNED=$(wc -l < "$TMP/out-files.txt" | tr -d ' ')
xargs -a "$TMP/out-files.txt" -d '\n' perl "$TMP/outbound.pl" "$OUT_ALLOW" "$ROOT" > "$TMP/out-raw.txt"
grep -vE '^[[:space:]]*(#|$)' "$OUT_ALLOW" | cut -d'|' -f1 | sort > "$TMP/out-allowed.txt" || true

OUT_NAMING=$(awk -F'|' '$2 > 0' "$TMP/out-raw.txt" | wc -l | tr -d ' ')
OUT_TOKENS=$(awk -F'|' '{ n += $2 } END { print n + 0 }' "$TMP/out-raw.txt")
OUT_ACCESSES=$(awk -F'|' '{ n += $3 } END { print n + 0 }' "$TMP/out-raw.txt")
OUT_ESCAPES=$(awk -F'|' '{ n += $5 } END { print n + 0 }' "$TMP/out-raw.txt")
OUT_FIELDS=$(awk -F'|' '$6 == 1' "$TMP/out-raw.txt" | wc -l | tr -d ' ')
OUT_ALLOWED=$(wc -l < "$TMP/out-allowed.txt" | tr -d ' ')
OUT_VIOL=0

echo "--- Measurement ---"
echo "  .cs files scanned under libs/:   $OUT_SCANNED"
echo "  files naming a raw client type:  $OUT_NAMING"
echo "  raw client type tokens:          $OUT_TOKENS"
echo "  SDK member accesses checked:     $OUT_ACCESSES"
echo "  SDK field bare uses (escapes):   $OUT_ESCAPES"
echo "  allow-listed SDK fields:         $OUT_FIELDS"
echo "  allow-listed files:              $OUT_ALLOWED"

if [ "$OUT_SCANNED" -eq 0 ] || [ "$OUT_TOKENS" -eq 0 ]; then
    echo "  ✗ BLIND CHECK: $OUT_SCANNED مِلَفّاً و$OUT_TOKENS رَمزاً — «صِفرُ مُخالَفَة» مِن أَداةٍ لَم تَرَ شَيئاً." >&2
    exit 1
fi

while IFS='|' read -r file tokens acc bad esc named; do
    [ -z "$file" ] && continue
    if [ "$tokens" -gt 0 ] && ! grep -qxF "$file" "$TMP/out-allowed.txt"; then
        echo "  ✗ RAW CLIENT outside the allow-list: $file ($tokens رَمزاً) — مَرِّرهُ بِـProviderHttp أَو ProviderCalls."
        OUT_VIOL=$((OUT_VIOL + 1))
    fi
    if [ "$bad" -gt 0 ]; then
        echo "  ✗ SDK VERB outside InvokeAsync/HelperAsync: $file ($bad مِن $acc وُصولاً، مِنها $esc تَسَرُّباً)"
        OUT_VIOL=$((OUT_VIOL + 1))
    fi
    if [ "$named" = 1 ] && [ "$acc" -eq 0 ]; then
        echo "  ✗ DEAD FIELD: $file — سَطرُه يُسَمّي حَقلاً لا وُصولَ إلَيه (0 وُصول): قاعِدَةُ الفِعلِ عَمياءُ عَن هذا المِلَفّ."
        OUT_VIOL=$((OUT_VIOL + 1))
    fi
done < "$TMP/out-raw.txt"

while read -r file; do
    [ -z "$file" ] && continue
    row=$(grep -F "$file|" "$TMP/out-raw.txt" | head -1)
    tokens=$(echo "$row" | cut -d'|' -f2)
    if [ -z "$row" ] || [ "${tokens:-0}" -eq 0 ]; then
        echo "  ✗ DEAD EXEMPTION: $file مُدرَجٌ ولا يُسَمّي نَوعاً خامّاً (أَو غائِب) — احذِف سَطرَه، فَالقائِمَةُ تَتَقَلَّص."
        OUT_VIOL=$((OUT_VIOL + 1))
    fi
done < "$TMP/out-allowed.txt"

echo "  violations:                      $OUT_VIOL"
echo ""
if [ "$OUT_VIOL" -gt 0 ]; then
    echo "Layer 8c red — نِداءٌ يُغادِرُ العَمَلِيَّةَ بِلا عَدّ، أَو قائِمَةٌ لا تَصِفُ المُستَودَع."
    exit 1
fi

echo "✅ Layer 8c green — $OUT_NAMING مِلَفّاً يُسَمّي عَميلاً خامّاً كُلُّها في القائِمَة ($OUT_ALLOWED)، وصِفرُ مُخالَفَة."

exit 0
