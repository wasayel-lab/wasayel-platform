#!/usr/bin/env bash
# Usage: ./scripts/ci-sdk.sh write  [Dockerfile] [global.json]
#        ./scripts/ci-sdk.sh verify [Dockerfile]
#
# ═══ CI يَبني ويَختَبِرُ بِـSDK الصورَةِ نَفسِه، بِرَقمِه لا بِقَناتِه (‏2026-10-25، تَدقيقُ الإطلاق brief-P3-3 وlens-checkers-ci-11) ═══
#
# **المَقيسُ قَبلَ**: الـ`Dockerfile` يَبني بِـ`sdk:10.0.401` و`ci.yml` يُثَبِّتُ `dotnet-version: 10.0.x` — قَناةً تَتَقَدَّمُ مَعَ كُلِّ رُقعَة،
# فَـCI الأَخضَرُ لَم يَكُن قِياساً لِلسِلسِلَةِ المَشحونَة. **ورَقمٌ دَقيقٌ في `setup-dotnet` وَحدَه لا يَكفي — مَقيساً**: صورَةُ
# ‏ubuntu-24.04 تَحمِلُ مُسبَقاً ‏SDK ‏8.0.131 … 10.0.112، 10.0.204، 10.0.303، 10.0.401 (‏`Ubuntu2404-Readme.md` في
# ‏`actions/runner-images`، ‏2026-10-25)، وبِلا `global.json` يَختارُ `dotnet` أَحدَثَ المُثَبَّت — فَيَومَ تَحمِلُ الصورَةُ رُقعَةً أَحدَثَ
# مِن وَسمِ الـ`Dockerfile` يَبني CI بِها صامِتاً.
#
# **فَالمَصدَرُ واحِد**: وَسمُ `FROM mcr.microsoft.com/dotnet/sdk:X AS build` في الـ`Dockerfile` (`DockerImagePinTests` يَحرُسُ دِقَّتَه).
#   write  — يَكتُبُ مِنه `global.json` لِهذا التَشغيلِ بِـ`rollForward: disable`، فَيُثَبِّتُه `setup-dotnet` (`global-json-file`)
#            ولا يَختارُ `dotnet` سِواه؛ ويَكتُبُ `version=X` إلى `$GITHUB_OUTPUT` إن وُجِد.
#   verify — يَسأَلُ `dotnet --version` ويَفشَلُ إن لَم يَكُن X.
# فَرَفعُ الوَسمِ في الـ`Dockerfile` يَرفَعُ CI مَعَه بِلا سَطرٍ ثانٍ يُنسى. **و`global.json` لا يُودَع** — يُكتَبُ في نُسخَةِ CI وَحدَها،
# فَجِهازُ التَطويرِ يَبني بِما عِندَه كَما كانَ (قَرارٌ مُسَجَّلٌ «أ» في سِجِلِّ المالِك).
#
# الرُموز: 0 مَقيسٌ ومُطابِق · 1 وَسمٌ غائِبٌ أَو مُتَحَرِّكٌ أَو مُكَرَّرٌ أَو SDK غَيرُه · 2 استِعمالٌ خاطِئ.
# ويَطبَعُ ما قَرَأَه (عَدَدَ أَسطُرِ `FROM … sdk`) ويَفشَلُ عَلى الصِفر — القاعِدَة ١٠.

set -uo pipefail

mode="${1:-}"
dockerfile="${2:-Dockerfile}"

case "$mode" in
    write|verify) ;;
    *) echo "الاستِعمال: $0 write|verify [Dockerfile] [global.json]" >&2; exit 2 ;;
esac

if [ ! -f "$dockerfile" ]; then
    echo "::error::${dockerfile} غائِب — لا وَسمَ يُقرَأ."
    exit 1
fi

tags="$(sed -nE 's#^FROM mcr\.microsoft\.com/dotnet/sdk:([^[:space:]]+) AS build[[:space:]]*$#\1#p' "$dockerfile" | tr -d '\r')"
count="$(printf '%s' "$tags" | grep -c . || true)"
echo "[ci-sdk] أَسطُرُ «FROM …/sdk:… AS build» في ${dockerfile}: ${count}"

if [ "$count" -ne 1 ]; then
    echo "::error::المُنتَظَرُ سَطرٌ واحِدٌ لِـ«FROM mcr.microsoft.com/dotnet/sdk:X AS build» والمَقروءُ ${count} — لا SDK يُشتَقّ."
    exit 1
fi

# ‏شَريطُ ميزاتٍ ورُقعَة (10.0.402) — قَناةٌ مِثلُ 10.0 أَو 10.0.x لا تُثَبِّتُ شَيئاً.
if ! [[ "$tags" =~ ^[0-9]+\.[0-9]+\.[0-9]{3}$ ]]; then
    echo "::error::وَسمُ الـSDK «${tags}» لَيسَ رُقعَةً دَقيقَة (مِثلُ 10.0.402) — قَناةٌ مُتَحَرِّكَةٌ لا تُثَبَّت."
    exit 1
fi

if [ "$mode" = write ]; then
    out="${3:-global.json}"
    printf '{\n  "sdk": {\n    "version": "%s",\n    "rollForward": "disable"\n  }\n}\n' "$tags" > "$out" || {
        echo "::error::تَعَذَّرَت كِتابَةُ ${out}."
        exit 1
    }
    if [ -n "${GITHUB_OUTPUT:-}" ]; then
        echo "version=${tags}" >> "$GITHUB_OUTPUT"
    fi
    echo "[ci-sdk] ${out} ← SDK ${tags} (rollForward: disable) مِن ${dockerfile}"
    exit 0
fi

# ‏`global.json` يُسَمّي SDK غَيرَ مُثَبَّتٍ ⇒ `dotnet --version` يَخرُجُ بِغَيرِ صِفرٍ ويَطبَعُ قائِمَةً لا رَقماً — فَيُقرَأُ الرَمزُ والسَطرُ الأَوَّل.
got="$(dotnet --version 2>/dev/null | tr -d '\r')"
rc=$?
first="$(printf '%s\n' "$got" | head -n 1)"
if [ "$rc" -ne 0 ] || [ "$got" != "$tags" ]; then
    echo "::error::الـSDK العامِلُ «${first:-<لا شَيء>}» (رَمزُ dotnet ${rc}) لا وَسمُ الصورَةِ «${tags}» — CI لا يَقيسُ السِلسِلَةَ المَشحونَة."
    exit 1
fi
echo "[ci-sdk] ✅ الـSDK العامِلُ ${got} = وَسمُ الصورَة"
exit 0
