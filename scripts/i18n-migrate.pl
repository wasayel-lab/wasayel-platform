#!/usr/bin/env perl
# ═══════════════════════════════════════════════════════════════════════
#  أَداةُ تَرحيلِ النُصوص — **لا تُكتَب قيمَةٌ بِاليَد، أَبَداً**
# ───────────────────────────────────────────────────────────────────────
#  المَوجَتانِ الثانِيَة والثالِثَة رَحَّلَتا ‏484 مِفتاحاً بِأَداةٍ
#  كُتِبَت ثُمَّ **لَم تُودَع** — فَأُعيدَت كِتابَتُها هُنا. والسَبَبُ
#  مَقيسٌ لا ذَوقيّ: «كلّ المُدُن» و«كُلّ المُدُن» تَختَلِفان بِشَدَّةٍ
#  واحِدَة لا تَراها العَين، و«جارٍ التَحميل…» و«جارٍ التَّحميل…»
#  كَذلك. أَيُّ قيمَةٍ تُنسَخ بِالعَين تُوَحِّد ما لَم يُقَرِّر أَحَدٌ
#  تَوحيدَه — وذاكَ **تَغييرٌ في نَصٍّ مَعروض بِلا قَرار**.
#
#  فَالأَداةُ تَنتَزِع القيمَة **مِن بايتات المِلَفّ نَفسِه**، وتَرفُض
#  المُضِيَّ إن لَم تَجِدها حَرفاً بِحَرف في `git show HEAD:`.
#
#  الاستِعمال:
#     scripts/i18n-migrate.pl --list  <razor>
#     scripts/i18n-migrate.pl --apply <razor> --spec <spec> [--dry]
#
#  ‏`--list` يَطبَع كُلّ سِلسِلَة عَرَبِيَّة في المِلَفّ بِرَقَمِها
#  وسَطرِها والنَمَط المُقتَرَح. و`--spec` سُطورٌ بِالشَكل:
#
#     IDX <TAB> KEY <TAB> MODE
#
#  والأَنماطُ ثَلاثَة، وهي **مَوضِعُ السِلسِلَة نَحوِيّاً** لا ذَوقاً
#  (‏docs/I18N.md §٢):
#
#     markup — عُقدَةُ نَصّ في المارك-أَب  → @L.Markup("KEY")
#              مَعَ الفَراغِ الحافّ صَريحاً على أَيّ طَرَفٍ صارَ حافَّة.
#     attr   — قيمَةُ خاصِّيَّة              → @L["KEY"]
#     cs     — سِلسِلَةٌ في كود C#          → L["KEY"]   (مَعَ عَلامَتَي
#              الاقتِباس المُحيطَتَين)
#     interp — جُزءٌ عَرَبيّ داخِلَ سِلسِلَةٍ مُدرَجَة `$"…{x}…"`
#                                          → {L["KEY"]}
#              (‏المُخرَجُ يَمُرّ عَبرَ C# في الحالَتَين، فَالتَرميزُ
#               واحِدٌ قَبلَ التَرحيل وبَعدَه)
#
#  والفَراغُ الحافّ (‏الفَخّ الأَوَّل): المُصَيِّر يَحذِف عُقدَةَ النَصّ
#  الفارِغَة عِندَ **طَرَفَي** مُحتَوى العُنصُر. وقَبلَ التَرحيل لا طَرَفَ
#  فارِغاً أَصلاً — الفَراغُ مُلتَصِقٌ بِالحَرفِيَّة في عُقدَةٍ واحِدَة.
#  فَإذا صارَ طَرَفاً كُتِبَ صَريحاً بِـ`@((MarkupString)"…")`.
#
#  و**CRLF يُطَبَّع إلى LF** في كُلّ فَراغٍ صَريح (الفَخّ الثالِث):
#  مَصادِرُنا مَخلوطَة، ومُخرَجُ المُصَيِّر صِفرُ `CR`.
# ═══════════════════════════════════════════════════════════════════════
use strict;
use warnings;
use open ':std', ':encoding(UTF-8)';

my $ROOT = `git rev-parse --show-toplevel`;
chomp $ROOT;
my $DICT = "$ROOT/libs/templates/ACommerce.Templates.Customer.Marketplace/I18n/Locales/ar.json";

my ($mode_list, $mode_apply, $file, $spec, $dry) = (0, 0, undef, undef, 0);
while (@ARGV) {
    my $a = shift @ARGV;
    if    ($a eq '--list')  { $mode_list  = 1; $file = shift @ARGV }
    elsif ($a eq '--apply') { $mode_apply = 1; $file = shift @ARGV }
    elsif ($a eq '--spec')  { $spec = shift @ARGV }
    elsif ($a eq '--dry')   { $dry = 1 }
    else { die "وَسيطٌ مَجهول: $a\n" }
}
die "الاستِعمال: --list <razor> | --apply <razor> --spec <spec>\n"
    unless ($mode_list || $mode_apply) && defined $file;

# ─── قِراءَةُ المِلَفّ ─────────────────────────────────────────────────
open(my $fh, '<:encoding(UTF-8)', $file) or die "لا يُفتَح $file: $!\n";
# ‏`local $/` داخِلَ كُتلَة عَمداً: لَو تُرِكَ في نِطاق المِلَفّ لَبَقِيَ
# `undef` إلى آخِر البَرنامَج، فَابتَلَعَت قِراءَةُ مِلَفّ الأَمر كُلَّ
# سُطورِه دُفعَةً واحِدَة وطَبَّقَت سَطراً واحِداً بَدَل ثَلاثَةٍ
# وعِشرين. (وَقَعَ، وكَشَفَه عَدّادُ التَبديلات.)
my $src = do { local $/; <$fh> };
close $fh;

# ─── قِناعُ التَعليقات ────────────────────────────────────────────────
# تُستَبدَل بِفَراغٍ **بِنَفس الطول** لِتَبقى الإزاحات كَما هي: الأَداةُ
# تَبحَث في القِناع وتُحَرِّر الأَصل، فَلَو حُذِفَت التَعليقاتُ حَذفاً
# لَانزاحَت كُلُّ مَواضِعِ ما بَعدَها.
my $mask = $src;
my $blank = sub { my $t = shift; $t =~ s/[^\n]/ /g; $t };
$mask =~ s/(\@\*.*?\*\@)/$blank->($1)/ges;
$mask =~ s/(<!--.*?-->)/$blank->($1)/ges;
$mask =~ s{(/\*.*?\*/)}{$blank->($1)}ges;
$mask =~ s{(///[^\n]*)}{$blank->($1)}ge;
$mask =~ s{^([ \t]*//[^\n]*)}{$blank->($1)}gme;

# ─── التِقاطُ السَلاسِل — بِنَفس تَعبير `verify-i18n-debt.sh` ─────────
my $RUN = qr/(?x)
    [\x{0600}-\x{06FF}\x{0750}-\x{077F}]
    [\x{0600}-\x{06FF}\x{0750}-\x{077F}\s\x{060C}\x{061B}\x{061F}
     \x{00AB}\x{00BB}\x{2026}\x{2014}\x{00B7}!?.,:\-()\/]*
/;

my @runs;
while ($mask =~ /($RUN)/g) {
    my $raw   = $1;
    my $end   = pos($mask);
    my $start = $end - length($raw);
    # القَصُّ نَفسُه الَّذي يَفعَلُه العَدّاد: الفَراغُ الحافّ لا يُعَدّ
    # جُزءاً مِن القيمَة.
    my ($lead) = $raw =~ /^(\s*)/;
    my ($tail) = $raw =~ /(\s*)$/;
    my $val = $raw;
    $val =~ s/^\s+//; $val =~ s/\s+$//;
    next if $val eq '';
    push @runs, {
        start => $start + length($lead),
        end   => $end   - length($tail),
        val   => $val,
        line  => 1 + (() = substr($src, 0, $start) =~ /\n/g),
    };
}

# ─── تَخمينُ النَمَط — اقتِراحٌ يُراجَع، لا حُكم ──────────────────────
sub guess_mode {
    my ($r) = @_;
    my $before = substr($mask, 0, $r->{start});
    my $after  = substr($mask, $r->{end});
    # داخِلَ عَلامَتَي اقتِباس؟ (‏الحَرفُ قَبلَ الفَراغِ البادِئ، والحَرفُ
    # بَعدَ الفَراغِ اللاحِق)
    my ($pre)  = $before =~ /(.)\s*$/s;
    my ($post) = $after  =~ /^\s*(.)/s;
    $pre  //= ''; $post //= '';
    if ($pre eq '"' && $post eq '"') {
        # ‏`Attr="…"` أَم سِلسِلَةُ C#؟ الفَرقُ في ما يَسبِق الاقتِباس.
        return ($before =~ /[A-Za-z0-9_\-]\s*=\s*"$/) ? 'attr' : 'cs';
    }
    return 'markup';
}

if ($mode_list) {
    printf "%-4s %-6s %-7s %s\n", '#', 'سطر', 'نمط', 'القيمة';
    for my $i (0 .. $#runs) {
        my $r = $runs[$i];
        # سِياقٌ قَصير مِن الجانِبَين: بِه تُقَرَّر أَعمِدَةُ التَمديد
        # (‏LEFT/RIGHT) — أَهُناكَ سَهمٌ أَو لاحِقَةٌ لاتينيَّة تُبتَلَع؟
        my $lc = substr($src, ($r->{start} > 14 ? $r->{start} - 14 : 0),
                        ($r->{start} > 14 ? 14 : $r->{start}));
        my $rc = substr($src, $r->{end}, 14);
        s/\s+/ /g for ($lc, $rc);
        printf "%-4d %-6d %-7s «%s»   ⟦%s│%s⟧\n",
               $i, $r->{line}, guess_mode($r), $r->{val}, $lc, $rc;
    }
    printf "\nالمَجموع: %d\n", scalar @runs;
    exit 0;
}

# ═══ التَطبيق ═══════════════════════════════════════════════════════════
die "‏--apply يَحتاج --spec\n" unless defined $spec;

# ── الطَرَفُ المُقابِل: نُسخَةُ HEAD ──────────────────────────────────
# ‏(الفَخُّ الرابِع) ‏`git show HEAD:` يُعطي LF والشَجَرَةُ قَد تَكون CRLF،
# فَيُطَبَّع الطَرَفانِ قَبلَ المُقابَلَة وإلّا أَعطَت «غَير مَوجود» عَلى
# نَصٍّ مَوجودٍ حَرفاً بِحَرف.
my $rel = $file;
$rel =~ s{^\Q$ROOT\E/}{};
$rel =~ s{\\}{/}g;
my $head = do {
    my $out = `git show HEAD:"$rel" 2>/dev/null`;
    utf8::decode($out);
    $out =~ s/\r//g;
    $out;
};
die "لا نُسخَةَ HEAD لِـ$rel — التَرحيلُ بِلا طَرَفٍ مُقابِل مَمنوع.\n"
    unless length $head;

# ── قِراءَةُ الأَمر ───────────────────────────────────────────────────
open(my $sh, '<:encoding(UTF-8)', $spec) or die "لا يُفتَح $spec: $!\n";
my @plan;
while (my $l = <$sh>) {
    chomp $l;
    next if $l =~ /^\s*(#|$)/;
    # ‏`\t` مُفرَدَة لا `\t+`: العَمودُ الفارِغ بَينَ جَدوَلَتَين **مَعنىً**
    # (‏«لا تَمديدَ يَساراً، ومَدٌّ يَميناً»)، وطَيُّ الجَدوَلات يُزيح
    # الأَعمِدَة فَيَصير المَدُّ اليَمينيّ يَسارِيّاً. وَقَعَ، وأَمسَكَه
    # حارِسُ «مَحرَفٌ غَير آمِن» لِأَنّ المَدَّ ابتَلَعَ `>` مِن وَسم.
    my ($idx, $key, $m, $left, $right) = split /\t/, $l;
    die "سَطرٌ مَعطوب في الأَمر: $l\n" unless defined $idx && defined $key;
    $m //= '';
    die "مِفتاحٌ خارِجَ `domain.feature.label`: $key\n"
        unless $key =~ /^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*){2}$/;
    die "رَقمٌ خارِجَ المَدى: $idx\n" if $idx < 0 || $idx > $#runs;
    push @plan, {
        idx   => $idx + 0,
        key   => $key,
        mode  => ($m || guess_mode($runs[$idx])),
        left  => (defined $left  && $left  ne '' ? $left  : 0),
        right => (defined $right && $right ne '' ? $right : 0),
    };
}
close $sh;

# ── التَمديد: ابتِلاعُ مَحارِفَ مُجاوِرَة **مِن المِلَفّ** ────────────
# لِماذا يَلزَم أَصلاً — وهذا دَرسُ الفَخّ الثاني: عُنصُرٌ ساكِنٌ فيه
# نَصٌّ عَرَبيّ **ومَحرَفٌ غَيرُ لاتينيّ آخَر** (‏`←`، `→`، `↗`)
# يَنقَلِب ديناميكِيّاً بِتَرحيل العَرَبيّ وَحدَه، **فَيُعاد تَرميزُ
# السَهم** (`&#x2190;`) بِلا أَن يَتَبَدَّل حَرفٌ مِمّا يُرى. فَإمّا
# أَن يُؤَجَّل المَوضِع، وإمّا أَن يُبتَلَع السَهمُ في **قيمَة
# المِفتاح** فَلا يَبقى في العُنصُر ما يُرَمَّز — وهو ما فَعَلَته
# المَوجَةُ الثالِثَة بِـ`+ فِكرَة جَديدَة` فَخَضَّرَت خَمسَ صَفَحات.
#
# والابتِلاعُ **يُقتَطَع مِن بايتات المِلَفّ**، لا يُكتَب بِاليَد:
# عَدَدٌ = هذا العَدَدُ مِن المَحارِف، و`*` = إلى حافَّة عُقدَة النَصّ.
sub extend_left {
    my ($s, $n) = @_;
    if ($n eq '*') {
        $s-- while $s > 0 && substr($src, $s - 1, 1) !~ /[><}{]/;
        $s++ while $s < length($src) && substr($src, $s, 1) =~ /\s/;
    } elsif ($n eq 'q') {          # إلى الاقتِباس المُحيط — لِقيمَة خاصِّيَّة
        $s-- while $s > 0 && substr($src, $s - 1, 1) ne '"';
    } else { $s -= $n }
    return $s;
}
sub extend_right {
    my ($e, $n) = @_;
    if ($n eq '*') {
        $e++ while $e < length($src) && substr($src, $e, 1) !~ /[<>\@{}]/;
        $e-- while $e > 0 && substr($src, $e - 1, 1) =~ /\s/;
    } elsif ($n eq 'q') {
        $e++ while $e < length($src) && substr($src, $e, 1) ne '"';
    } else { $e += $n }
    return $e;
}
for my $p (@plan) {
    my $r = $runs[$p->{idx}];
    my ($s, $e) = ($r->{start}, $r->{end});
    $s = extend_left($s,  $p->{left})  if $p->{left};
    $e = extend_right($e, $p->{right}) if $p->{right};
    $p->{start} = $s;
    $p->{end}   = $e;
    $p->{val}   = substr($src, $s, $e - $s);
}

# ── التَحَقُّق: كُلُّ قيمَةٍ مَوجودَةٌ في HEAD حَرفاً بِحَرف ─────────
my %pairs;
for my $p (@plan) {
    my $v = $p->{val};
    # الطَرَفانِ يُطَبَّعانِ مِن `\r` قَبلَ المُقابَلَة (‏الفَخُّ الرابِع):
    # ‏`git show HEAD:` يُعطي LF والشَجَرَةُ CRLF، فَقيمَةٌ مَلفوفَة عَلى
    # سَطرَين تُعطي «غَير مَوجود» وهي مَوجودَةٌ حَرفاً بِحَرف.
    my $vn = $v; $vn =~ s/\r//g;
    die "القيمَةُ «$v» غَير مَوجودَة في HEAD — تَوَقَّف.\n"
        unless index($head, $vn) >= 0;
    die "قيمَةٌ فيها مَحرَفٌ غَير آمِن (<، >، &): «$v»\n" if $v =~ /[<>&]/;
    if (exists $pairs{$p->{key}} && $pairs{$p->{key}} ne $v) {
        die "تَعارُض: المِفتاح $p->{key} يُطلَب لِقيمَتَين مُختَلِفَتَين.\n";
    }
    $pairs{$p->{key}} = $v;
}

# ── حارِسُ المَعجَم (‏merge3): لا تُكتَب قيمَةٌ فَوقَ مِفتاحٍ قائِم ──
open(my $dh, '<:encoding(UTF-8)', $DICT) or die "لا يُفتَح المَعجَم: $!\n";
my $dict = do { local $/; <$dh> }; close $dh;
my %existing;
while ($dict =~ /^\s*"([a-z][a-z0-9_.]*)"\s*:\s*"((?:[^"\\]|\\.)*)"/gm) {
    $existing{$1} = $2;
}
my @add;
for my $k (sort keys %pairs) {
    my $v = $pairs{$k};
    # التَرميز إلى JSON: والسَطرُ الجَديد **يُطَبَّع إلى `\n`** لا
    # `\r\n` (‏الفَخُّ الثالِث: مُخرَجُ المُصَيِّر صِفرُ `CR`، ومَصادِرُنا
    # مَخلوطَة). وقيمَةٌ فيها سَطرٌ حَرفيّ تَكسِر JSON أَصلاً.
    my $esc = $v;
    $esc =~ s/\\/\\\\/g; $esc =~ s/"/\\"/g;
    $esc =~ s/\r\n/\n/g; $esc =~ s/\r/\n/g;
    $esc =~ s/\n/\\n/g;  $esc =~ s/\t/\\t/g;
    if (exists $existing{$k}) {
        die "المِفتاح $k قائِمٌ بِقيمَةٍ مُختَلِفَة — الدُفعَةُ مُتَوَقِّفَة.\n"
            . "  القائِم: «$existing{$k}»\n  المَطلوب: «$esc»\n"
            if $existing{$k} ne $esc;
        next;   # نَفسُ القيمَة → إعادَةُ استِعمالٍ مَشروعَة
    }
    push @add, [$k, $esc];
}

# ── التَبديل: مِن الآخِر إلى الأَوَّل لِتَبقى المَواضِعُ صالِحَة ──────
sub esc_ws {
    my $w = shift;
    $w =~ s/\r\n/\n/g; $w =~ s/\r/\n/g;
    $w =~ s/\n/\\n/g;  $w =~ s/\t/\\t/g;
    $w;
}

my $out = $src;
my $edits = 0;
for my $p (sort { $b->{start} <=> $a->{start} } @plan) {
    my ($s, $e) = ($p->{start}, $p->{end});
    my $repl;

    if ($p->{mode} eq 'cs') {
        # اِبتَلِع عَلامَتَي الاقتِباس المُحيطَتَين.
        die "‏cs بِلا اقتِباسٍ مُحيط عِندَ #$p->{idx}\n"
            unless substr($out, $s - 1, 1) eq '"' && substr($out, $e, 1) eq '"';
        $s--; $e++;
        $repl = qq{L["$p->{key}"]};
    }
    elsif ($p->{mode} eq 'attr') {
        $repl = qq{\@L["$p->{key}"]};
    }
    elsif ($p->{mode} eq 'interp') {
        $repl = qq[{L["$p->{key}"]}];
    }
    else {
        # ── عُقدَةُ نَصّ ──
        my $lead_ws = ($out =~ /(\s*)\z/ ? '' : '');
        my $ls = $s; $ls-- while $ls > 0 && substr($out, $ls - 1, 1) =~ /\s/;
        my $le = $e; $le++ while $le < length($out) && substr($out, $le, 1) =~ /\s/;
        my $lead = substr($out, $ls, $s - $ls);
        my $tail = substr($out, $e, $le - $e);

        # طَرَفٌ رَأسيّ: الفَراغُ يَنتَهي إلى `>` مِن وَسمٍ **فاتِح** غَير
        # مُغلَقٍ ذاتِيّاً. أَمّا `/>` (مُكَوِّنٌ سابِق) و`</…>` (شَقيقٌ
        # مُنتَهٍ) فَلَيسا رَأسَ مُحتوىً.
        my $head_edge = 0;
        if ($ls > 0 && substr($out, $ls - 1, 1) eq '>') {
            my $tagend = $ls - 1;
            my $tagstart = rindex(substr($out, 0, $tagend), '<');
            if ($tagstart >= 0) {
                my $tag = substr($out, $tagstart, $tagend - $tagstart + 1);
                $head_edge = 1 unless $tag =~ m{^</} || $tag =~ m{/>$};
            }
        }
        # طَرَفٌ ذَيليّ: الفَراغُ يَنتَهي إلى `</` (نِهايَةُ مُحتَوى العُنصُر).
        my $tail_edge = ($le < length($out) && substr($out, $le, 2) eq '</') ? 1 : 0;

        my $body = qq{\@L.Markup("$p->{key}")};
        my $pre  = $head_edge && $lead ne ''
                 ? qq{\@((MarkupString)"} . esc_ws($lead) . qq{")}
                 : $lead;
        my $post = $tail_edge && $tail ne ''
                 ? qq{\@((MarkupString)"} . esc_ws($tail) . qq{")} . $tail
                 : $tail;
        $s = $ls; $e = $le;
        $repl = $pre . $body . $post;
    }

    substr($out, $s, $e - $s) = $repl;
    $edits++;
}

# ── حَقنُ المَنفَذ إن غاب ─────────────────────────────────────────────
# صَفحَةٌ تُنادي `L` ولا تَحقِنُه لا تُبنى أَصلاً. والإضافَةُ ASCII
# خالِصَة، فَلا قيمَةَ عَرَبِيَّة تُكتَب بِاليَد هُنا.
my $injected = 0;
if ($out !~ /^\@inject\s+L\s+L\s*$/m && $out =~ /\S/) {
    if ($out =~ /(^\@inject[^\n]*\n)(?!\@inject)/m) {
        my $last = 0;
        $last = pos($out) while $out =~ /^\@inject[^\n]*\n/mg;
        substr($out, $last, 0) = "\@inject L L\n";
    } elsif ($out =~ /^\@page[^\n]*\n/m) {
        my $last = 0;
        $last = pos($out) while $out =~ /^\@page[^\n]*\n/mg;
        substr($out, $last, 0) = "\@inject L L\n";
    } else {
        substr($out, 0, 0) = "\@inject L L\n";
    }
    $injected = 1;
}

# ── الكِتابَة ─────────────────────────────────────────────────────────
if ($dry) {
    print "‏(تَجريبيّ) ‏$edits تَبديلاً في $rel، و" . scalar(@add) . " مِفتاحاً جَديداً.\n";
    printf "  %s = «%s»\n", $_->[0], $_->[1] for @add;
    exit 0;
}

open(my $wh, '>:encoding(UTF-8)', $file) or die "لا يُكتَب $file: $!\n";
print $wh $out; close $wh;

if (@add) {
    # الإدراجُ قَبلَ القَوس الأَخير، بِنَفس إزاحَة المَعجَم.
    my $block = join('', map { qq{  "$_->[0]": "$_->[1]",\n} } @add);
    # آخِرُ إدخالَةٍ في المِلَفّ بِلا فاصِلَة — نُضيفُ فاصِلَتَها ثُمَّ
    # نَحذِفُ فاصِلَةَ آخِرِ ما أَضَفنا.
    $block =~ s/,\n\z/\n/;
    # حَذفُ إدخالَةٍ أَخيرَة يَترُك فاصِلَةً مُعَلَّقَة، فَتُضاف الثانِيَة
    # فَوقَها ويَنكَسِر JSON بِسَطرٍ لا يَحمِل إلّا `,`. وَقَعَ، وأَعطى
    # ‏500 على كُلّ صَفحَة. فَتُقَصّ الفاصِلَةُ القائِمَة أَوَّلاً.
    $dict =~ s/,(\s*)\n\}\s*\z/$1\n}/;
    $dict =~ s/\n\}\s*\z/,\n\n$block}\n/
        or die "شَكلُ المَعجَم غَير مُتَوَقَّع — لا إدراج.\n";
    open(my $dw, '>:encoding(UTF-8)', $DICT) or die "لا يُكتَب المَعجَم: $!\n";
    print $dw $dict; close $dw;
}

printf "✓ %s — %d تَبديلاً، %d مِفتاحاً جَديداً.\n", $rel, $edits, scalar @add;
