#!/usr/bin/env bash
# ══════════════════════════════════════════════════════════════════════
#  قائِمَة ما يُدفَع إلى الـSpace — **مَقيسَة مِن الـ`csproj` لا مَكتوبَة بِاليَد**
# ══════════════════════════════════════════════════════════════════════
#
#  Usage:
#    ./scripts/deploy-manifest.sh            # يَطبَع المَسارات المَسموحَة، سَطراً لِكُلّ مِلَفّ
#    ./scripts/deploy-manifest.sh --stage DIR  # ويَنسَخُها إلى DIR (يُنشَأ فارِغاً)
#
#  **العِلَّة**: الـSpace **عامّ** — ويَجِب أَن يَبقى عامّاً، فَالخاصّ
#  يَحجُب التَطبيقَ نَفسَه خَلفَ تَسجيل دُخول. فَكُلّ مِلَفّ يُدفَع
#  يَقرَؤُه أَيّ أَحَد. والمِرآةُ كانَت تَدفَع **الشَجَرَةَ كامِلَةً**
#  (‏757 مِلَفّاً مُتَتَبَّعاً) وفيها `docs/` و`CLAUDE.md` و`scripts/`
#  و`tests/` — أَي أَنّ غُرفَةَ العَمَل كُلَّها كانَت مَنشورَة لِبِناء
#  لا يَقرَأ مِنها سَطراً.
#
#  **ولِماذا لا يَكفي `.dockerignore`**: هو يَحجُب عَن **سِياق البِناء**
#  داخِلَ الـSpace، ولا يَحجُب عَن **مُستودَع الـSpace** — والمِلَفّ
#  المَدفوع مَقروءٌ في صَفحَة «Files» سَواءٌ دَخَلَ الصورَةَ أَم لا.
#  فَهذا الفِلتَر هو الخَطّ الأَوَّل، و`.dockerignore` يَبقى دِفاعاً
#  ثانِياً (لَو أُضيفَ يَوماً مُجَلَّدٌ إلى القائِمَة سَهواً).
#
#  **وكَيفَ تُبنى القائِمَة — قِياساً**: نَبدَأُ مِن
#  `apps/V1.App/V1.App.csproj` (وهو ما يَبنيه الـ`Dockerfile` حَرفاً)،
#  ونَمشي `ProjectReference` **تَعَدِّياً** حَتّى الإغلاق، فَنَحصُل على
#  مَجموعَة المَشاريع الَّتي يَحتاجُها `dotnet publish` فِعلاً. ثُمَّ
#  نَأخُذ **كُلّ مِلَفّ مُتَتَبَّع** تَحتَ مُجَلَّدات تِلكَ المَشاريع
#  (‏فَيَدخُل `Definitions/**` و`I18n/**` و`wwwroot/**` بِلا قائِمَة
#  يَدَوِيَّة تَنسى واحِداً)، مُضافاً إلَيها مِلَفّات الجَذر الَّتي
#  يَقرَؤُها البِناء أَو HF:
#
#    · `Directory.Build.props` / `Directory.Packages.props` — يَقرَؤُهُما
#      MSBuild تَصاعُدِيّاً؛ وبِغِيابِهِما لا `TargetFramework` ولا
#      إدارَة حُزَم مَركَزِيَّة، فَيَنهار `restore`.
#    · `Dockerfile` — بِلا هذا لا يَبني HF شَيئاً.
#    · `.dockerignore` — الدِفاع الثاني، ويُنسَخ مَعَه.
#    · `README.md` — تَرويسَتُه YAML هي ما يَقرَؤُه HF لِيَعرِف
#      `sdk: docker` و`app_port: 7860`.
#
#  **وما يُستَبعَد بِالبِناء لا بِقائِمَة سَوداء**: كُلّ ما لَيسَ في
#  الإغلاق أَعلاه — `docs/`, `CLAUDE.md`, `scripts/`, `tests/`,
#  `.github/`, `docker-compose.yml`, `INSTALL.md`, `PlatformV1.slnx`,
#  و`libs/` غَير المُحال إلَيها. ولا يَحتاج البِناءُ الحَلَّ
#  (`PlatformV1.slnx`): الـ`Dockerfile` يَبني المَشروعَ لا الحَلّ.
#
#  **والقائِمَة السَوداء تَبقى — بَوّابَةً لا فِلتَراً**: بَعدَ بِناء
#  القائِمَة يَفحَص السكريبتُ نَفسُه أَنّ الأَربَعَة المَحظورَة
#  **صِفر** فيها، ويَخرُج بِخَطَإٍ إن ظَهَرَ واحِد. فَلَو أَحالَ
#  مَشروعُ التَطبيق يَوماً إلى شَيءٍ تَحتَ `tests/` لَاحمَرَّ هذا هُنا
#  بَدَل أَن يُنشَر صامِتاً. (القاعِدَة ١٠: أَداةٌ تَطبَع ما فَحَصَت
#  وتَفشَل إن فَحَصَت صِفراً.)
#
# ══════════════════════════════════════════════════════════════════════
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR/.." || exit 1

ROOT_PROJECT="apps/V1.App/V1.App.csproj"

# مِلَفّات الجَذر الَّتي يَقرَؤُها البِناء أَو HF — لا مَشاريع.
ROOT_FILES=(
    "Directory.Build.props"
    "Directory.Packages.props"
    "Dockerfile"
    ".dockerignore"
    "README.md"
)

# ما لا يَصِل الـSpace بِحالٍ — بَوّابَة تُفشِل، لا فِلتَر يَحذِف.
FORBIDDEN_PREFIXES=("docs/" "scripts/" "tests/" ".github/" "CLAUDE.md" "docker-compose.yml")

STAGE_DIR=""
if [ "${1:-}" = "--stage" ]; then
    STAGE_DIR="${2:?--stage يَحتاج مُجَلَّداً}"
fi

# ─── ١) الإغلاق التَعَدِّيّ لِـProjectReference ──────────────────────
# المَسارات في الـcsproj بِشَرطَة مَقلوبَة (‏`..\..\libs\…`) — تُسَوَّى
# إلى `/` قَبلَ أَيّ شَيء، ثُمَّ يُطَبَّع المَسار بِـ`realpath --relative-to`.
declare -A SEEN=()
QUEUE=("$ROOT_PROJECT")

while [ ${#QUEUE[@]} -gt 0 ]; do
    proj="${QUEUE[0]}"
    QUEUE=("${QUEUE[@]:1}")
    [ -n "${SEEN[$proj]:-}" ] && continue
    [ -f "$proj" ] || { echo "::error::مَشروع مُحالٌ إلَيه غَير مَوجود: $proj" >&2; exit 1; }
    SEEN[$proj]=1

    projdir="$(dirname "$proj")"
    while IFS= read -r inc; do
        [ -n "$inc" ] || continue
        inc="${inc//\\//}"
        abs="$(realpath -m "$projdir/$inc")"
        rel="$(realpath -m --relative-to="$PWD" "$abs")"
        QUEUE+=("$rel")
    done < <(grep -o '<ProjectReference[^>]*Include="[^"]*"' "$proj" \
             | sed 's/.*Include="//; s/"$//')
done

# ─── ٢) كُلّ مِلَفّ مُتَتَبَّع تَحتَ مُجَلَّدات تِلكَ المَشاريع ──────
PROJECT_DIRS=()
for proj in "${!SEEN[@]}"; do PROJECT_DIRS+=("$(dirname "$proj")"); done

MANIFEST="$(
    {
        git ls-files -- "${PROJECT_DIRS[@]}"
        for f in "${ROOT_FILES[@]}"; do
            git ls-files --error-unmatch -- "$f" >/dev/null 2>&1 \
                || { echo "::error::مِلَفّ جَذر مَطلوب وغَير مُتَتَبَّع: $f" >&2; exit 1; }
            echo "$f"
        done
    } | LC_ALL=C sort -u
)"

# ─── ٣) البَوّابَة: المَحظور صِفر، والمَفحوص لَيسَ صِفراً ────────────
count="$(printf '%s\n' "$MANIFEST" | grep -c . || true)"
if [ "$count" -eq 0 ]; then
    echo "::error::القائِمَة فارِغَة — الأَداةُ عَمياء لا الشَجَرَةُ خالِيَة." >&2
    exit 1
fi

leak=0
for prefix in "${FORBIDDEN_PREFIXES[@]}"; do
    hits="$(printf '%s\n' "$MANIFEST" | grep -c "^${prefix}" || true)"
    if [ "$hits" -ne 0 ]; then
        echo "::error::تَسَرَّبَ إلى القائِمَة ${hits} مِلَفّاً تَحتَ «${prefix}»" >&2
        leak=1
    fi
done
[ "$leak" -eq 0 ] || exit 1

echo "[deploy-manifest] مَشاريع في الإغلاق: ${#SEEN[@]} · مِلَفّات مَسموحَة: ${count}" >&2
printf '%s\n' "$MANIFEST" | awk -F/ '{print (NF==1 ? "(الجَذر)" : $1"/")}' \
    | LC_ALL=C sort | uniq -c | sed 's/^/[deploy-manifest]   /' >&2

# ─── ٤) الطَبع، وبِـ--stage النَسخ **مِن الإيداعِ لا مِن الشَجَرَة** ──
#
# ═══ البَصمَةُ كانَت تَشهَدُ بِالنَسَبِ لا بِالمُحتَوى ═══════════════
#
# **العَيبُ مُعادُ إنتاجُه لا مَظنون**: يُبنى ثُنائيٌّ مِن شَجَرَةٍ فيها
# تَعديلٌ غَيرُ مودَع، فَيُعلِنُ `/health` **رَأسَ الشَجَرَةِ النَظيف**
# وهو يَخدِمُ شَيئاً آخَر. وكانَ الطَرَفانِ هُنا مِن مَصدَرَينِ
# مُختَلِفَين: المُحتَوى بِـ`cp -p` **مِن شَجَرَةِ العَمَل**، والرَقَمُ
# بِـ`git rev-parse HEAD` مِن الإيداع. فَما يُعلَنُ لَيسَ ما يُنسَخ.
#
# اليَومَ لا تُستَغَلُّ في الوَظيفَة لِأَنّ خَطَواتِها بَينَ الاستِنساخِ
# والتَجهيزِ لا تُعَدِّلُ مِلَفّاً — أَي أَنَّها **تَصمُدُ بِتَرتيبِ
# الخَطَواتِ لا بِحارِس**، وتَرتيبُ الخَطَواتِ يَتَغَيَّرُ في PR واحِد.
#
# فَالمُحتَوى يُقرَأُ مِن `HEAD` نَفسِه (`git show`)، فَيَصيرُ «الرَقَمُ
# المُعلَنُ هُوَ الإيداعُ الَّذي بُنِيَ مِنه» **صَحيحاً بِالبِناء** لا
# مَشروطاً بِنَظافَةِ الشَجَرَة. والانحِرافُ — إن وُجِدَ — **يُقاسُ
# ويُطبَع**، فَالمُشَغِّلُ المَحَلِّيُّ يَعرِفُ أَنّ تَعديلَه لَم يَذهَب.
if [ -n "$STAGE_DIR" ]; then
    rm -rf "$STAGE_DIR"
    mkdir -p "$STAGE_DIR"

    # الانحِراف: كَم مِلَفّاً مِن القائِمَةِ يَختَلِفُ بَينَ الشَجَرَةِ
    # و‏HEAD. صِفرٌ في CI (‏شَجَرَةٌ مُستَنسَخَة)، وغَيرُ صِفرٍ مَحَلِّيّاً
    # حينَ يُحَرَّرُ مِلَفٌّ ولا يُودَع.
    drift="$(
        { git diff --name-only HEAD; git diff --cached --name-only; } 2>/dev/null \
        | LC_ALL=C sort -u \
        | comm -12 - <(printf '%s\n' "$MANIFEST") \
        | grep -c . || true
    )"
    if [ "${drift:-0}" -ne 0 ]; then
        echo "::warning::${drift} مِلَفّاً مِن القائِمَةِ يَختَلِفُ عَن HEAD — **يُجَهَّزُ مُحتَوى HEAD**، لا الشَجَرَة." >&2
    fi

    while IFS= read -r f; do
        [ -n "$f" ] || continue
        if ! git cat-file -e "HEAD:$f" 2>/dev/null; then
            echo "::error::مِلَفٌّ في القائِمَةِ ولَيسَ في HEAD: $f — مُتَتَبَّعٌ غَيرُ مودَع." >&2
            exit 1
        fi
        mkdir -p "$STAGE_DIR/$(dirname "$f")"
        git show "HEAD:$f" > "$STAGE_DIR/$f"
    done < <(printf '%s\n' "$MANIFEST")

    staged="$(find "$STAGE_DIR" -type f | wc -l | tr -d ' ')"
    if [ "$staged" -ne "$count" ]; then
        echo "::error::نُسِخَ ${staged} والمَطلوب ${count} — الكاتِبُ يُعيد القِراءَة." >&2
        exit 1
    fi
    echo "[deploy-manifest] جُهِّزَ ${staged} مِلَفّاً مِن HEAD=$(git rev-parse HEAD) إلى ${STAGE_DIR} (انحِراف: ${drift:-0})" >&2
fi

printf '%s\n' "$MANIFEST"
