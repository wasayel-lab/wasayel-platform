#!/usr/bin/env bash
# Usage: ./scripts/verify-i18n-debt.sh  [--emit-ledger]
#
# LAYER 7 — Literal-text debt (lexical, no build, no server).
#   "How much user-facing text is still written into .razor by hand,
#    and is that number going down?"
#
# Why 7 and not 6: layer 6 is the LIVE layer (verify-runtime.cs, needs an
# app on :5050 and a browser).  This one reads source only, so it joins
# the static gate; the number is kept free.
#
# ── What it measures, exactly ────────────────────────────────────────────
# Razor / HTML / C# comments are stripped first, then every run of Arabic
# characters is counted.  Two numbers come out of that:
#
#   RUNS      — every occurrence.  This is the per-file unit, because a
#               file is migrated occurrence by occurrence.
#   DISTINCT  — the distinct strings.  This is the wave unit, because one
#               key retires every occurrence of the same string.
#
# COVERAGE = keys / (keys + distinct-still-literal).  Monotone by
# construction: it can only rise by migrating, and a new hardcoded string
# lowers it.  It is NOT comparable to a "% of files touched" figure.
#
# ── Why the ledger is bidirectional at the FILE level and a ceiling per file
# The origin's layer 4 taught that a strictly two-way numeric ledger
# punishes improvement: dropping from 129 colours to 120 reddened a gate
# that existed to encourage exactly that.  So per-file counts are
# CEILINGS — going below one is reported loudly and passes.
#
# But the SET of files carrying debt is pinned both ways: a file that
# reaches zero must be struck from the ledger, and a file that gains debt
# while absent from it fails.  That is the half that cannot rot, and it
# costs one line of edit per migrated screen — which is the point: the
# debt shrinks only by a visible decision.
#
# ── The blind-check guard ────────────────────────────────────────────────
# Everything here depends on perl (the counter needs comment stripping and
# Unicode ranges; awk/grep cannot do it portably).  The origin shipped a
# CSS checker that shelled out to python3, which was absent on the dev
# machine, so it scanned zero files and printed a green tick forever.  So:
# perl is probed, and its absence FAILS loudly instead of counting zero.

set -eo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=scripts/verify-common.sh
source "$SCRIPT_DIR/verify-common.sh"

EMIT=0
[ "${1:-}" = "--emit-ledger" ] && EMIT=1

LEDGER="$SCRIPT_DIR/verify-i18n-debt-ledger.txt"
DICT="$ROOT/libs/templates/ACommerce.Templates.Customer.Marketplace/I18n/Locales/ar.json"
TMP=$(mktemp -d)
trap 'rm -rf "$TMP"' EXIT

if [ "$EMIT" -eq 0 ]; then
    echo "═══════════════════════════════════════════════"
    echo "   Layer 7 — Hardcoded Arabic text in .razor"
    echo "═══════════════════════════════════════════════"
    echo ""
fi

if ! command -v perl > /dev/null 2>&1; then
    echo "  ✗ BLIND CHECK: perl not found — the counter cannot run." >&2
    echo "    A counter that cannot count must fail, not report zero." >&2
    exit 1
fi

# ── The counter ──────────────────────────────────────────────────────────
cat > "$TMP/count.pl" <<'PERL'
use strict; use warnings;
use open ':std', ':encoding(UTF-8)';

# ── Trailing `//` comments — the counter's own false positive ────────────
# The stripper used to be `^\s*//.*$`: only comments that START a line.  A
# comment that TRAILS code was counted as user-facing text.  Measured cost:
# five phantom entries in the ledger — three in TenantHome, one in App.razor,
# one in TenantListingDetail — none of which a user can ever read.  A counter
# that over-reports is as untrustworthy as one that under-reports: it makes
# "the debt is N" a claim nobody can act on.
#
# Why not simply strip `//` to end of line: `//` also appears INSIDE string
# literals (`href="https://…"`, `'/api/' + slug`).  Cutting there would delete
# real text after it — a FALSE NEGATIVE, which is the worse failure.
#
# So the rule is: strip from the first `//` whose prefix on that line closes
# every quote it opened.  And the rule was MEASURED before it was adopted —
# across every .razor in the repo, the number of lines carrying `//` inside an
# open quote AND Arabic after it is ZERO.  The scan that says so:
#
#   perl -CSD -ne 'if (m{//} && /[\x{0600}-\x{06FF}]/) { ... }'  (see git log)
#
# `i18n-migrate.pl` carries the same routine, deliberately duplicated: the two
# tools MUST agree on run indices or a spec file addresses the wrong string.
sub strip_line_comments {
    my ($text) = @_;
    my @out;
    for my $line (split /\n/, $text, -1) {
        my $off = 0;
        while ((my $j = index($line, '//', $off)) >= 0) {
            my $pre = substr($line, 0, $j);
            my $dq = ($pre =~ tr/"//);
            my $sq = ($pre =~ tr/'//);
            if ($dq % 2 == 0 && $sq % 2 == 0) { $line = $pre; last; }
            $off = $j + 2;
        }
        push @out, $line;
    }
    return join("\n", @out);
}

my (%runs, %files, $total);
for my $f (@ARGV) {
    open(my $fh, '<:encoding(UTF-8)', $f) or next;
    local $/; my $s = <$fh>; close $fh;
    $s =~ s/\@\*.*?\*\@//gs;      # Razor comments
    $s =~ s/<!--.*?-->//gs;       # HTML comments
    $s =~ s{/\*.*?\*/}{}gs;       # C# block comments
    $s =~ s{///.*$}{}gm;          # C# doc comments
    $s = strip_line_comments($s); # C# / JS line comments — ANYWHERE on the line
    my $n = 0;
    while ($s =~ /([\x{0600}-\x{06FF}\x{0750}-\x{077F}]
                   [\x{0600}-\x{06FF}\x{0750}-\x{077F}\s\x{060C}\x{061B}\x{061F}
                    \x{00AB}\x{00BB}\x{2026}\x{2014}\x{00B7}!?.,:\-()\/]*)/gx) {
        my $t = $1; $t =~ s/^\s+|\s+$//g;
        next if $t eq '';
        $runs{$t}++; $n++; $total++;
    }
    $files{$f} = $n if $n > 0;
}
$total ||= 0;
print "TOTAL_RUNS $total\n";
print "DISTINCT " . scalar(keys %runs) . "\n";
print "DEBT_FILES " . scalar(keys %files) . "\n";
print "FILE $files{$_} $_\n" for sort keys %files;
PERL

wsl_find razor "$WSL_ALL_ROOTS" > "$TMP/files.txt"
FILE_CNT=$(wc -l < "$TMP/files.txt" | tr -d ' ')

if [ "$EMIT" -eq 0 ]; then
    echo "--- Subjects ---"
    wsl_require_subjects ".razor files" "$FILE_CNT" 50
    echo ""
fi

xargs -a "$TMP/files.txt" -d '\n' perl "$TMP/count.pl" > "$TMP/raw.txt"

TOTAL_RUNS=$(awk '$1=="TOTAL_RUNS"{print $2}'  "$TMP/raw.txt")
DISTINCT=$(awk   '$1=="DISTINCT"{print $2}'    "$TMP/raw.txt")
DEBT_FILES=$(awk '$1=="DEBT_FILES"{print $2}'  "$TMP/raw.txt")
awk '$1=="FILE"{ p=$3; for(i=4;i<=NF;i++) p=p" "$i; sub(ROOT"/","",p); print p"|"$2 }' \
    ROOT="$ROOT" "$TMP/raw.txt" | sort > "$TMP/current.txt"

# ── The dictionary side ──────────────────────────────────────────────────
if [ ! -f "$DICT" ]; then
    echo "  ✗ BLIND CHECK: dictionary not found at $(wsl_rel "$DICT")." >&2
    exit 1
fi
KEYS=$(grep -cE '^[[:space:]]*"[a-z][a-z0-9_.]*"[[:space:]]*:' "$DICT")

if [ "$EMIT" -eq 1 ]; then
    echo "# scripts/verify-i18n-debt-ledger.txt — الدَين المَقيس، مُثَبَّتاً بِأَسمائِه."
    echo "# وُلِّد بِـ: ./scripts/verify-i18n-debt.sh --emit-ledger"
    # ── استِثناءٌ مُعلَن: ما لا يُتَرجَم أَبَداً ───────────────────────────
    # يُطبَع هُنا لا يُكتَب بِاليَد، وإلّا مَحاه أَوَّلُ `--emit-ledger`.
    # وهذِه السَلاسِلُ **لَيسَت نَصَّ واجِهَة**: هي أَنماطُ مُطابَقَةٍ
    # تُقابِل مُخرَجَ نَموذَجِ اللُغَة حَرفاً بِحَرف. تَرحيلُها يَربِط
    # مُطابَقَةَ بَياناتٍ بِقامُوسٍ يَتَغَيَّر — أَي يَكسِر التَصنيفَ يَومَ
    # تُصَحَّح شَكلَةٌ في قيمَة. وهي مَعدودَةٌ في الرَقَم أَعلاه، **ولا
    # تُحسَب دَيناً** يُنتَظَر سَدادُه.
    echo "# ــــــ استِثناءٌ مُعلَن (لا يُتَرجَم أَبَداً) ــــــ"
    echo "# StudioStudy.razor — ‏8 حَرفِيّات في تَعبيرَي \`switch\`:"
    echo "#   Level:         «منخفض» «مُنخَفِض» «عالي» «عالٍ»"
    echo "#   CategoryClass: «سوق» «تشغيل» «مالي» «تنظيم»"
    echo "#   السَبَب: مُطابَقَةُ مُخرَجِ النَموذَج، لا نَصُّ شاشَة."
    echo "#   وصِنفُها نَفسُ صِنفِ \`<option value=\"عَرَبيّ\">\` في نَموذَج"
    echo "#   التَبليغ: قيمَةٌ تُخَزَّن، لا تَسمِيَةٌ تُقرَأ."
    echo "# ــــــــــــــــــــــــــــــــــــــــــــــــــ"
    echo "TOTAL|$TOTAL_RUNS"
    echo "DISTINCT|$DISTINCT"
    cat "$TMP/current.txt"
    exit 0
fi

# الأَقواس حَول الشَرط لازِمَة: `>` داخِل قائِمَة وُسَطاء printf
# يَقرَؤُها awk إعادَةَ تَوجيه لا مُقارَنَة.
COVERAGE=$(awk -v k="$KEYS" -v d="$DISTINCT" 'BEGIN{ t=k+d; if (t>0) printf "%.1f", 100*k/t; else printf "0.0" }')

echo "--- Measurement ---"
echo "  .razor files scanned:          $FILE_CNT"
echo "  files carrying literal text:   $DEBT_FILES"
echo "  literal Arabic runs:           $TOTAL_RUNS"
echo "  distinct literal strings:      $DISTINCT"
echo "  dictionary keys (ar.json):     $KEYS"
echo "  coverage:                      $COVERAGE %"
echo ""

if [ ! -f "$LEDGER" ]; then
    echo "  ✗ no ledger at $(wsl_rel "$LEDGER")."
    echo "    Create it with: ./scripts/verify-i18n-debt.sh --emit-ledger > $(wsl_rel "$LEDGER")"
    exit 1
fi

grep -vE '^[[:space:]]*(#|$)' "$LEDGER" > "$TMP/ledger.txt"
PINNED_TOTAL=$(awk -F'|' '$1=="TOTAL"{print $2}' "$TMP/ledger.txt")
grep -vE '^(TOTAL|DISTINCT)\|' "$TMP/ledger.txt" | sort > "$TMP/pinned.txt"
PINNED_FILES=$(wc -l < "$TMP/pinned.txt" | tr -d ' ')

echo "--- Ledger ---"
echo "  pinned files: $PINNED_FILES   pinned total: $PINNED_TOTAL"
echo ""

FAIL=0
LOOSE=0

# 1. Files above their ceiling, or carrying debt while unlisted.
while IFS='|' read -r file count; do
    [ -z "$file" ] && continue
    pin=$(awk -F'|' -v f="$file" '$1==f{print $2}' "$TMP/pinned.txt")
    if [ -z "$pin" ]; then
        echo "  ✗ NEW debt file (not in ledger): $file  ($count runs)"
        FAIL=1
    elif [ "$count" -gt "$pin" ]; then
        echo "  ✗ debt GREW: $file  $pin → $count"
        FAIL=1
    elif [ "$count" -lt "$pin" ]; then
        echo "  · debt shrank: $file  $pin → $count   (ledger loose — tighten it)"
        LOOSE=$((LOOSE + 1))
    fi
done < "$TMP/current.txt"

# 2. Ledger entries whose file is now clean — struck, or the ledger rots.
while IFS='|' read -r file pin; do
    [ -z "$file" ] && continue
    if ! grep -qE "^$(printf '%s' "$file" | sed 's/[].[^$*\/]/\\&/g')\|" "$TMP/current.txt"; then
        echo "  ✗ STALE ledger entry: $file was pinned at $pin and is now clean —"
        echo "      strike it from $(wsl_rel "$LEDGER")."
        FAIL=1
    fi
done < "$TMP/pinned.txt"

# 3. The total ceiling.
if [ "$TOTAL_RUNS" -gt "$PINNED_TOTAL" ]; then
    echo "  ✗ total literal runs rose: $PINNED_TOTAL → $TOTAL_RUNS"
    FAIL=1
fi

echo ""
if [ "$FAIL" -eq 1 ]; then
    echo "Layer 7 red — literal-text debt grew, or the ledger no longer describes it."
    echo "Debt may only grow by a visible decision: migrate the text, or edit the"
    echo "ledger in the same commit and say why."
    exit 1
fi

[ "$LOOSE" -gt 0 ] && echo "  ($LOOSE ledger entries are looser than reality — tightening is free.)"
echo "✅ Layer 7 green — literal-text debt did not grow."
exit 0
