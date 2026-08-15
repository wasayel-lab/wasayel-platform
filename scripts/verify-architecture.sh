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
grep -vE '^(SESSION_FILES|WRITE_FILES)\|' "$TMP/ledger.txt" | sort > "$TMP/pinned.txt"
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
exit 0
