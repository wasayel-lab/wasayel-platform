#!/usr/bin/env bash
# ═══════════════════════════════════════════════════════════════════════
#  rewalk-launch-journey.sh — رِحلَةُ الإطلاقِ مِن صَفحَةٍ أولى، بِـ`curl` وَحدَه
# ═══════════════════════════════════════════════════════════════════════
#
#  **الغَرَض**: يَمشي رِحلَةَ المُنتَجِ بَعدَ التَحَوُّلِ (‏ADR-032 §٢‑أ) عَلى
#  خادِمٍ حَيّ، ويَطبَعُ لِكُلِّ خُطوَةٍ الطَلَبَ ورَمزَه والنَصَّ الَّذي بَرهَنَها
#  — والنَصُّ يُقرَأُ مِن القامُوسِ بِمِفتاحِه (‏`ar.json`) لا مِن ذاكِرَةِ
#  السكربت، فَتَبديلُ صِياغَةٍ لا يُحمِرُّه ولا يُعميه:
#
#    رائِدُ أَعمالٍ يَبني مَتجَراً مُتَعَدِّدَ البائِعينَ مِن الاستوديو
#    ← بائِعٌ يُسَجِّلُ ويُسَكِّنُ نَفسَه `vendor` ويَنشُر
#    ← مُشتَرٍ يُسَجِّلُ ويُضيفُ لِلسَلَّةِ ويَطلُبُ نَقداً (والبِطاقَةُ لا تُعرَض)
#    ← البائِعُ يُشعَرُ ويَقبَلُ ويَرى العُنوانَ ويُوصِلُ إلى «تَسليم»
#      (وزِرُّ الشَحنِ مَخفِيٌّ بِلا رَمزِ OTO)
#    ← مُشرِفُ المَنَصَّةِ يَضبِطُ باقَةَ المَتجَرِ ويَظهَرُ سَطرُها في الاستوديو
#    ← المالِكُ يَمنَحُ نَفسَه إدارَةَ مَتجَرِه (عَدَدُ الأَفعالِ يُطبَع)
#    ← الزائِرُ بِلا جَلسَةٍ يَرى رابِطَ الاستِكشاف.
#      ‏(‏2026-10-29، قَرارُ المالِك د34، ‏ADR-048 — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه): بَل يَرى البِضاعَةَ أَوَّلاً — مَتجَرُ الاستوديو
#      فيه دَورُ مُشتَرٍ (`customer`)، فَجَذرُه رَئيسِيَّةُ المُشتَري وبِطاقاتُ الأَدوارِ وسَطرُ «لَدَيكَ حِساب» تَحتَها، ولا رابِطَ «دونَ تَسجيل».
#
#  **والجِدارُ يُقال ولا يُلتَفُّ عَلَيه صامِتاً**: التَحليلُ يَحتاجُ مِفتاحَ
#  وَكيلٍ (‏`docs/DEPLOY.md` §٤·ب). الخُطوَةُ ١ تَمشي المَسارَ الحَقيقيَّ حَتّى
#  التَحليلِ وتَطبَعُ ما رَدَّه المُزَوِّدُ وبَعدَ كَم ثانِيَة؛ فَإن اكتَمَلَ بُنِيَ المَتجَرُ مِنه،
#  وإن فَشِلَ بُنِيَ مِن **المَسارِ المُوَثَّقِ لِلتَطوير** — جَلسَةُ
#  `IncubatorSampleSeeder` (‏`INCUBATOR_SAMPLE_SEED=1`) المَملوكَةُ لِصاحِبِ
#  `PLATFORM_ADMIN_PHONE` — ويُقالُ أَيُّهُما في السَطر، و«المِفتاحُ» لا يُسَمّى سَبَباً
#  إلّا عَلى رَدِّ 401. وما زالَ «جارِياً» عِندَ آخِرِ النافِذَةِ (`ANALYZE_TRIES` أَدناه) أَو
#  حالَةٌ لا تَقولُ أَيّاً مِن الثَلاثَةِ تُوقِفُ الرِحلَةَ بِنَصِّها. ‏(‏2026-10-13، المُراجَعَةُ الثانِيَة:
#  كانَ «وإلّا» — فَتَحليلٌ أَبطَأُ مِن 30 ثانِيَةً يُقرَأُ «بِلا مِفتاح» ويَسقُطُ إلى مَسارِ التَطويرِ
#  تَحتَ المُشرِف، وفَشَلٌ بِمُهلَةِ النِداءِ يُطبَعُ «مِفتاحُ وَكيلِ التَحليلِ غائِب».)
#  ⚠ والبَذّارُ يَسبِقُ مَنحَ المُشرِفِ في `Program.cs`: عَلى قاعِدَةٍ بارِدَةٍ لا
#  مُستَخدِمَ استوديو يَومَئِذٍ فَلا جَلسَة — والإقلاعُ الثاني يَبذُرُها (مَقيسٌ
#  ‏2026-10-13، `docs/RELEASE-READINESS.md`).
#  ‏(‏2026-10-24، تَيّارُ S15 البَند E — ولا يُعادُ حَرفٌ فَوقَه: **لَم يَعُد** — البَذّارانِ بَعدَ مَنحِ المُشرِف، فَإقلاعٌ بارِدٌ واحِدٌ يَبذُرُ الجَلسَة.)
#
#  **ونَقرَةُ السَلَّةِ لا تُمَيِّزُ FLGAP-04 إلّا عَلى فَرعٍ واحِدٍ مِن المَصنَع**:
#  ‏ADR-034 غَيَّرَ فَرعَ `ecommerce` في `CategoriesForSector` وَحدَه (‏`products`/`deals`
#  بِكِندِ `"commercial"` ← `""`). فَالخُطوَةُ ٢ تَقرَأُ فِئاتِ المَتجَرِ المَبنِيِّ وكِندَ كُلٍّ
#  مِن مُحَرِّرِها وتَطبَعُ فَرعَه وحُكمَه — «يُمَيِّز» عَلى `ecommerce`، و«لا يُمَيِّز» عَلى
#  غَيرِه — وفي المَسارِ الحَقيقيِّ تَفحَصُ أَنّه `ecommerce` (‏ADR-034 §١(أ))، فَجَولَةُ
#  المالِكِ بِمِفتاحٍ تُبَرهِنُ الفَرعَ أَو تَحمَرّ. ومَتجَرُ جَلسَةِ العَيِّنَةِ (قِطاعُها
#  `other`) مِن فَرعِ السُقوط `general` بِكِندٍ فارِغٍ قَبلَ القَرارِ وبَعدَه: لا يُمَيِّز.
#
#  **الاستِعمال**:
#      ADMIN_PHONE=<PLATFORM_ADMIN_PHONE لِلخادِم> ./scripts/rewalk-launch-journey.sh
#      BASE=http://localhost:5055 ADMIN_PHONE=… MARK=134501 ./scripts/rewalk-launch-journey.sh
#  ‏`MARK` (سِتَّةُ أَرقام) يَصنَعُ هَواتِفَ الجَولَةِ وسلاجَ مَتجَرِها، فَتُعادُ
#  الجَولَةُ عَلى القاعِدَةِ نَفسِها بِلا تَصادُم.
#  ‏`ANALYZE_TRIES` عَدَدُ مَرّاتِ سُؤالِ صَفحَةِ الدِراسَةِ في الخُطوَة ١، بَينَ كُلِّ اثنَتَينِ ثانِيَتان.
#  **وافتِراضُه يُحسَبُ مِن سَقفِ الخادِمِ نَفسِه** لا يُكتَبُ رَقَماً: مُحاوَلاتُ الحَلقَةِ في `RunAnalysisAsync`
#  (‏`attempt < N`) × أَطوَلُ `HttpClient.Timeout` في `AgentBackends.cs` — اليَومَ 2 × 60ث = 120 ثانِيَة — نِصفُه
#  سُؤالاً وسُؤالانِ فَوقَه: الأَوَّلُ لِأَنّ السُؤالَ الأَوَّلَ في الثانِيَةِ صِفرٌ فَيَقَعُ ما بَعدَه عِندَ السَقفِ نَفسِه، والثاني
#  لِكِتاباتِ القاعِدَةِ بَعدَ النِداءَين (اليَومَ 62، آخِرُها بَعدَ ≥ 122 ثانِيَة). فَالجَلسَةُ تَبلُغُ «اكتَمَلَ» أَو «فَشِلَ»
#  داخِلَ النافِذَةِ ما لَم تَعلَق، ويُطبَعُ السَقفُ والنافِذَةُ قَبلَ الانتِظار وما انتُظِرَ فِعلاً بَعدَه.
#  ‏(‏2026-10-24، تَيّارُ S15 البَند K — ولا يُعادُ حَرفٌ فَوقَه: المُهلَةُ صارَت مِن الطَلَبِ لا مِن العَميل — `HttpClient.Timeout`
#  بِلا حَدّ، ونِداءُ التَحليلِ تَحتَ `AgentCallBound.Background` ‏420ث، فَالسَقفُ 2 × 420ث = 840 ثانِيَة والنافِذَةُ 422 سُؤالاً.
#  ويُقرَأُ سَطرُ `Background = TimeSpan.FromSeconds(N)` بِحَرفِه.)
#  ‏(‏2026-10-13، المُراجَعَةُ الثانِيَة: كانَ 15 — ‏30 ثانِيَة — فَبَديلٌ يُجيبُ بَعدَ 40 ثانِيَةً قُرِئَ «لا اكتِمالَ
#  ولا فَشَل» وسَقَطَت الجَولَةُ إلى مَسارِ التَطوير، فَوَقَفَت عِندَ الخُطوَة ٢ «بِلا مِفتاحٍ يَلزَمُ
#  INCUBATOR_SAMPLE_SEED=1» — فُحِصَ 11 · فَشِلَ 2.)
#  **ولا يُشَغَّلُ عَلى الإنتاج**:
#  يَكتُبُ مُستَخدِمينَ ومَتجَراً وإعلاناً وصَفقَة، والدُخولُ فيه بِرَمزِ المُحاكي
#  (‏Development وَحدَه).
#
#  **القاعِدَة ١٠**: كُلُّ فَحصٍ يُعَدّ، ويُطبَعُ العَدَدُ في آخِرِه، والصِفرُ
#  يُحمِر — «لا فَشَل» مِن سكربتٍ لَم يَفحَص شَيئاً لا يُمَيَّزُ عَن العَمى.
#
#  **«مِن صَفحَةٍ أولى» يُقاسُ لا يُدَّعى** (‏2026-10-25، تَدقيقُ الإطلاق brief-P1b-2 — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه): كانَ
#  ‏`POST /studio/begin` يُرسَلُ بِلا `GET /` قَبلَه، و`create-listing` و`checkout?step=2` مَبنِيَّينِ مِن عَناوينَ في السكربت،
#  و`grace=14 price=0` حَرفَينِ فيه. صارَت الخُطُواتُ تَأخُذُ التالِيَ مِن الصَفحَةِ الَّتي قَبلَها: الاستِمارَةُ وحَقلُها مِن `/`،
#  ورابِطُ «إعلان جَديد» مِن الصَفحَةِ الَّتي تُعيدُ إلَيها الشُروط، وحَقلا الفِئَةِ والدَورِ المَخفِيّانِ مِن استِمارَةِ النَشر،
#  ورابِطُ الدَفعِ مِن السَلَّة، والخُطُواتُ ١ ← ٢ ← ٣ ← الإرسالُ مِن استِماراتِها (`form_dump`)، والمُهلَةُ والسِعرُ قيمَتا
#  استِمارَةِ الباقَة. **وما يَبقى مَكتوباً — عَمداً ويُقالُ في سَطرِه**: ما يَكتُبُه الإنسانُ في حَقل (الفِكرَة، الهاتِف، العُنوان،
#  الاسم، السِعر، تاريخُ الانتِهاء، خِيارُ «اشتِراك يَدَوِيّ»)، ومِجَسّا الرَفضِ (`pay=card` و`/studio/begin` عَلى بابٍ مُغلَق —
#  طَلَبانِ لا تَرسُمُهُما الصَفحَةُ لِيُثبَتَ أَنّ النُقطَةَ تَرُدُّهُما)، والمَراحِلُ الَّتي تُعادُ بِأَزرارِها (`/advance`، `/accept`).
#
#  **شَرطُ الاستِخراج** (القاعِدَة ١): `post`/`get`/`dec` هُنا أَخَواتُ
#  دَوالِّ `walk-after-the-key.sh` لا نُسخَتُها بِحَرفِها (هُنا تُرسِلُ
#  الاستِمارَةُ نَوعَ مُحتَواها دائِماً كَالمُتَصَفِّح). مُستَهلِكانِ لا ثَلاثَة؛
#  فَإن احتاجَها سكربتٌ ثالِثٌ استُخرِجَت إلى `scripts/journey-common.sh`.
#
#  **‏(‏2026-11-07، الموجَة ١٣ W13-1·٩ — ADR-049 §١٠؛ يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه) بِلا مِفتاح، وFLGAP-04 كُلَّ تَشغيل.**
#  خَرَجَ التَحليلُ مِن الرِحلَة (·٥)، فَسَقَطَ كُلُّ ما فَوقَه عَنه: «الجِدارُ» ونافِذَةُ `ANALYZE_TRIES` وسَقفُ الخادِمِ مِن `AgentBackends.cs`
#  و`StaleAfter`، ومَسارُ التَطويرِ بِجَلسَةِ `IncubatorSampleSeeder`، و«مَبنِيٌّ سَلَفاً». الخُطوَتانِ ١ و٢ صارَتا ما يَنقُرُه رائِدُ الأَعمال:
#  دَعوَةُ الصَفحَةِ الأُولى «أَنشِئ مَتجَرَك» ← `/studio/new` ← الدُخول (‏`StudioStoreCreate.AfterSignIn` يُعيدُه إلى الاستِمارَة) ← المُوافَقَة
#  (‏`returnUrl` مِن استِمارَتِها) ← الاستِمارَةُ وحُقولُها مِن الصَفحَة (`form_dump`) ← `POST` بابِها ← `/studio/apps/{slug}?built=1`.
#  والمالِكُ في كُلِّ تَشغيلٍ رائِدُ الأَعمالِ نَفسُه، والمَتجَرُ `rewalk-$MARK` جَديدٌ دائِماً — و`MARK` مُكَرَّرٌ يَقِفُ عِندَ الدُخولِ بِسَبَبِه: هاتِفُ رائِدِ الأَعمالِ مِنه، ويَملِكُ مَتجَرَ الجَولَةِ السابِقَة
#  فَيَعودُ إلى `/studio` لا إلى الاستِمارَة (مَقيسٌ بِتَشغيلٍ ثانٍ بِالعَلامَةِ نَفسِها).
#  **وFLGAP-04 فَحصٌ لا سَطرُ مَعلومَة**: كُلُّ مَتجَرٍ مِن الاستوديو يولَدُ بِنَمَطٍ وقِطاعٍ فارِغَين ← `marketplace` ← فَرعُ `ecommerce`،
#  فَالخُطوَةُ ٢ تَفحَصُ الفَرعَ وكِندَه الفارِغَ في كُلِّ تَشغيلٍ بِكُتلَةِ `branch_of` (يُشَغِّلُها `RewalkKeylessTests` بِـbash).
#  و`walk-after-the-key.sh` تَقاعَد — لا مِفتاحَ يُنتَظَرُ بَعدَه — فَالدَوالُّ أَعلاه بِلا أُختٍ، وشَرطُ الاستِخراجِ يَبقى كَما كُتِب.
# ═══════════════════════════════════════════════════════════════════════
set -uo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
BASE="${BASE:-http://localhost:5055}"
ADMIN_PHONE="${ADMIN_PHONE:-}"
MARK="${MARK:-$(date +%H%M%S)}"
DEV_CODE="${DEV_CODE:-123456}"     # ‏`MockSmsChannel.FixedCode` — Development وَحدَه
T="${T:-25}"                       # مُهلَةُ كُلِّ نِداء (ثانِيَة)
# ‏(‏2026-11-07، W13-1·٩) `ANALYZE_TRIES` تَقاعَدَ مَعَ التَحليل — لا انتِظارَ في الرِحلَة.

AR="$ROOT/libs/core/ACommerce.Platform.I18n/Locales/ar.json"
CATALOG="$ROOT/libs/templates/ACommerce.Templates.Customer.Marketplace/Services/Deals/DealPatternCatalog.cs"
FACTORY="$ROOT/libs/templates/ACommerce.Templates.Customer.Marketplace/Services/Incubator/TenantFromAnalysisFactory.cs"

WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

CHECKED=0
FAILED=0
STEP=""

# ─── الطَبعُ والعَدّ ────────────────────────────────────────────────────
say()   { printf '\n\033[1m%s\033[0m\n' "$*"; }
info()  { printf '   · %s\n' "$*"; }
block() { printf '   \033[33m⊘ %s\033[0m\n' "$*"; }
# check <1|0> <ما فُحِص> — يُعَدُّ فَحصاً في الحالَين، والفَشَلُ يُعَدُّ في عَدّادِه أَيضاً فَلا يُبتلَع.
LAST_FAILED=0   # رَقمُ الفَحصِ الفاشِلِ الأَخير — يَقرَؤُه `die` فَلا يُعَدُّ التَوَقُّفُ مَرَّتَين
check() {
    CHECKED=$((CHECKED + 1))
    if [ "$1" = 1 ]; then printf '   \033[32m✓\033[0m %s\n' "$2"
    else FAILED=$((FAILED + 1)); LAST_FAILED=$CHECKED; printf '   \033[31m✗\033[0m %s\n' "$2"; fi
}
# خُطوَةٌ تَتوقَّفُ عَلَيها ما بَعدَها — يُقالُ أَينَ تَوَقَّفَت الرِحلَة.
# ‏(‏2026-10-24، تَيّارُ S15 البَند H(7) — **التَوَقُّفُ يُعَدُّ مَرَّةً**: كانَ يُضافُ واحِدٌ إلى «فَشِلَ» عِندَ كُلِّ تَوَقُّف، والتَوَقُّفُ
# كَثيراً ما يَكونُ الفَحصَ الفاشِلَ الَّذي قَبلَه نَفسَه — فَجَلسَةٌ «جارِيَةٌ» واحِدَةٌ طُبِعَت «فَشِلَ 2». صارَ: إن كانَ آخِرُ فَحصٍ هُوَ
# الفاشِلَ فَالتَوَقُّفُ هُوَ، ولا يُعَدُّ ثانِيَة؛ وتَوَقُّفٌ بِلا فَحصٍ فاشِلٍ قَبلَه يُعَدُّ واحِداً كَما كان.)
die() {
    local failed=$((FAILED + 1))
    [ "$LAST_FAILED" -gt 0 ] && [ "$LAST_FAILED" = "$CHECKED" ] && failed=$FAILED
    printf '\n\033[31m✗ تَوَقَّفَت الرِحلَةُ عِندَ «%s»: %s\033[0m\n' "$STEP" "$*" >&2
    printf 'فُحِصَ %s · فَشِلَ %s\n' "$CHECKED" "$failed" >&2
    exit 1
}

# ─── أَدَواتُ الطَلَب ───────────────────────────────────────────────────
# ‏POST يَضَعُ في `CODE` و`LOC`. **ونَوعُ المُحتَوى يُرسَلُ دائِماً** كَما
# يُرسِلُه المُتَصَفِّحُ لِأَيِّ استِمارَةٍ ولَو بِلا حَقل: `curl -X POST` بِلا
# جِسمٍ يُرسِلُ طَلَباً بِلا `Content-Type`، و`/advance` يَقرَأُ `req.Form`
# فَيَرُدُّ ‏500 — أَثَرُ الأَداةِ لا المُنتَج (مَقيسٌ ‏2026-10-13).
post() { # post <jar|-> <path> [args…] — args تُمَرَّرُ إلى curl (‏--data-urlencode أَو -F)؛ `-` بِلا جَلسَة
    local jar="$1" path="$2"; shift 2
    local h; h="$(mktemp -p "$WORK")"
    local body=(-d '') cookies=()
    [ "$#" -gt 0 ] && body=("$@")
    [ "$jar" = - ] || cookies=(-c "$jar" -b "$jar")
    CODE="$(curl -s --max-time "$T" -o /dev/null -D "$h" "${cookies[@]}" \
        -X POST "$BASE$path" "${body[@]}" -w '%{http_code}')" || CODE=000
    LOC="$(grep -i '^location:' "$h" | tr -d '\r' | sed 's/^[Ll]ocation: //')"
}
get() { # get <jar|-> <path> <out> — `-` بِلا جَلسَة. ‏(‏2026-11-07، W13-1·C1D-1) ويَضبُطُ `LOC` بِوِجهَةِ التَحويلِ (فارِغَةً بِلا تَحويل) — صَفحَةٌ تُحَوِّلُ مِن الخادِمِ
    local jar="$1" path="$2" out="$3" w
    if [ "$jar" = - ]; then
        w="$(curl -s --max-time "$T" -o "$out" "$BASE$path" -w '%{http_code} %{redirect_url}')" || w=000
    else
        w="$(curl -s --max-time "$T" -o "$out" -c "$jar" -b "$jar" "$BASE$path" -w '%{http_code} %{redirect_url}')" || w=000
    fi
    CODE="${w%% *}"; LOC="${w#* }"; [ "$LOC" = "$w" ] && LOC=""; LOC="${LOC#"$BASE"}"
}
# نَصُّ الصَفحَةِ مَفكوكَ التَرميز: Razor يَكتُبُ مُخرَجَ التَعبيرِ كِياناتٍ
# (‏`&#x627;`…)، فَبَحثٌ خامٌ عَن نَصٍّ عَرَبِيٍّ يُعطي صِفراً كاذِباً.
dec() { perl -CSD -pe 's/&#x([0-9A-Fa-f]+);/chr(hex($1))/ge; s/&#(\d+);/chr($1)/ge; s/&quot;/"/g; s/&amp;/&/g' "$1"; }
# ‏`grep` بِلا `-q` عَمداً: تَحتَ `pipefail` يَخرُجُ `-q` عِندَ أَوَّلِ مُطابَقَةٍ فَيَموتُ `perl` بِـSIGPIPE
# فَيَفشَلُ الأُنبوبُ والنَصُّ مَوجود — أَحمَرُ كاذِبٌ مَقيسٌ في أَوَّلِ تَشغيل (‏2026-10-13).
has() { dec "$1" | grep -F -- "$2" >/dev/null; }      # has <file> <نَصّ>
count() { grep -oF -- "$2" "$1" | wc -l | tr -d ' '; } # count <file> <نَصّ خام>
# ─── بِدايَةُ form_dump ─── (‏`RewalkFormDumpTests` يُشَغِّلُ ما بَينَ العَلامَتَينِ بِـbash عَلى عَيِّنَةٍ مَعلومَةِ الجَوابِ وعَلى نُسَخٍ مَحقونَة)
# ‏(‏2026-10-25، brief-P1b-2) form_dump <file> <نَصٌّ في الاستِمارَة> — أَوَّلُ <form> يَحمِلُ النَصَّ (فاتِحَتُه أَو جِسمُه):
#   ‏«ACTION <action> <method>» ثُمَّ لِكُلِّ حَقل: «HIDDEN n=v» (مَخفِيّ)، «RADIO n=v» (خِيار)، «FIELD n» (مَرئِيٌّ مَطلوب)، و«VALUE n=v»
#   ‏(مَرئِيٌّ بِقيمَةٍ مَكتوبَة). بِلا استِمارَةٍ لا يَطبَعُ شَيئاً — والمُنادي يُحمِرُّ عَلى الفَراغ.
form_dump() {
    dec "$1" | MUST="$2" perl -0777 -ne '
        while (m{(<form\b[^>]*>)(.*?)</form>}gs) {
            my ($open, $body) = ($1, $2);
            next if index($open . $body, $ENV{MUST}) < 0;
            my ($act) = $open =~ /\baction="([^"]*)"/; my ($m) = $open =~ /\bmethod="([^"]*)"/;
            print "ACTION ", ($act // ""), " ", lc($m // "get"), "\n";
            while ($body =~ m{<(?:input|textarea|select)\b([^>]*)>}g) {
                my $a = $1;
                my ($n) = $a =~ /\bname="([^"]*)"/; next unless defined $n;
                my ($t) = $a =~ /\btype="([^"]*)"/; $t //= "";
                my ($v) = $a =~ /\bvalue="([^"]*)"/;
                if    ($t eq "hidden") { print "HIDDEN $n=", ($v // ""), "\n" }
                elsif ($t eq "radio")  { print "RADIO $n=", ($v // ""), "\n" }
                else {
                    print "FIELD $n\n" if $a =~ /\brequired\b/;
                    print "VALUE $n=$v\n" if defined $v && length $v;
                }
            }
            last;
        }'
}
fd_action() { printf '%s\n' "$1" | sed -n 's/^ACTION \([^ ]*\) .*/\1/p' | head -1; }               # fd_action "$DUMP"
fd_kind()   { printf '%s\n' "$1" | sed -n "s/^$2 \(.*\)/\1/p"; }                                 # fd_kind "$DUMP" HIDDEN|RADIO|FIELD|VALUE
fd_get()    { printf '%s\n' "$1" | sed -n "s/^$2 $3=//p" | head -1; }                               # fd_get "$DUMP" HIDDEN step
# ─── نِهايَةُ form_dump ───
# النَصُّ مِن القامُوسِ بِمِفتاحِه — ومِفتاحٌ غائِبٌ يُوقِفُ الرِحلَة لا يُطابِقُ الفَراغ.
ar() {
    local v; v="$(jq -r --arg k "$1" '.[$k] // empty' "$AR")"
    [ -n "$v" ] || die "مِفتاحُ القامُوسِ «$1» غائِب في ar.json"
    printf '%s' "$v"
}
# تَسمِيَةُ مَرحَلَةِ الصَفقَةِ مِن مَصدَرِها (‏`DealPatternCatalog.DefaultLabelsAr`).
# ‏مِن كُتلَةِ التَسمِياتِ وَحدَها: الجَدوَلُ الَّذي قَبلَها (‏`DefaultActors`) يُفَهرِسُ بِالمَفاتيحِ نَفسِها،
# فَبَحثٌ في المِلَفِّ كُلِّه يُعيدُ «counterparty» مَعَ «تَسليم» — أَحمَرُ مَقيسٌ في أَوَّلِ تَشغيل.
stage_label() {
    local v; v="$(awk '/DefaultLabelsAr/{f=1} f && /};/{exit} f' "$CATALOG" \
        | grep -o "\[DealStage\.$1\] *= \"[^\"]*\"" | sed 's/.*= "//; s/"$//')"
    [ -n "$v" ] || die "تَسمِيَةُ المَرحَلَة $1 غائِبَة في DealPatternCatalog.cs"
    printf '%s' "$v"
}
# فِئاتُ فَرعِ `ecommerce` مِن مَصدَرِها (‏`TenantFromAnalysisFactory.CategoriesForSector`)، مِن كُتلَتِه وَحدَها
# ومُرَتَّبَةً — الفَرعُ الوَحيدُ الَّذي غَيَّرَه ADR-034، لا قائِمَةٌ مِن ذاكِرَةِ السكربت.
ecommerce_slugs() {
    local v; v="$(awk '/"ecommerce" => new\[\]/{f=1} f && /^[[:space:]]*},/{exit} f' "$FACTORY" \
        | grep -o 'C("[a-z_-]*"' | sed 's/^C("//; s/"$//' | sort | tr '\n' ' ')"
    v="${v% }"
    [ -n "$v" ] || die "فَرعُ ecommerce غائِبٌ في TenantFromAnalysisFactory.cs"
    printf '%s' "$v"
}
# دُخولُ عُضوٍ في مَتجَرٍ بِالهاتِف: إصدارٌ ثُمَّ استِهلاك — `phone_login` في
# `capture-appearance.sh` بِشَكلِه.
store_login() { # store_login <jar> <slug> <phone>
    post "$1" "/$2/auth/phone/login" --data-urlencode "phone=$3"
    check "$([ "$CODE" = 302 ] && [[ "$LOC" == *stage=verify* ]] && echo 1 || echo 0)" \
        "POST /$2/auth/phone/login → $CODE ${LOC}"
    post "$1" "/$2/auth/phone/verify" --data-urlencode "phone=$3" --data-urlencode "code=$DEV_CODE"
    check "$([ "$CODE" = 302 ] && [[ "$LOC" != *err=* ]] && echo 1 || echo 0)" \
        "POST /$2/auth/phone/verify → $CODE ${LOC}"
}

[ -n "$ADMIN_PHONE" ] || { echo "✗ ADMIN_PHONE غائِب — ضَعه = PLATFORM_ADMIN_PHONE لِلخادِم (‏DEPLOY.md §٢·ب)." >&2; exit 2; }
[[ "$MARK" =~ ^[0-9]{6}$ ]] || { echo "✗ MARK سِتَّةُ أَرقامٍ لا «$MARK»." >&2; exit 2; }
command -v jq >/dev/null || { echo "✗ jq غائِب — بِه يُقرَأُ القامُوس." >&2; exit 2; }

# ‏(‏2026-11-07، W13-1·٩): كانَت هُنا كُتلَةُ `analysis_bounds` (سَقفُ نِداءاتِ التَحليلِ و`StaleAfter` مِن مَصدَرَيهِما) — خَرَجَت مَعَ التَحليل،
# ومَكانُها كُتلَةُ `branch_of` أَدناه: حُكمُ فَرعِ المَصنَعِ مِن فِئاتِ المَتجَرِ المَنشَإ، يُشَغِّلُها `RewalkKeylessTests` عَلى عَيِّناتٍ مَعلومَة.
# ─── بِدايَةُ branch_of ───
# branch_of <سُطورُ «slug:'kind'»> <فِئاتُ فَرعِ ecommerce مُرَتَّبَةً بِمَسافَة> ← «ecommerce <عَدَدُ الكِندِ غَيرِ الفارِغ>» أَو «other <الفِئات>».
branch_of() {
    local cats="$1" ecom="$2" slugs nonempty
    slugs="$(printf '%s\n' "$cats" | sed "s/:'.*//" | grep . | sort | tr '\n' ' ')"; slugs="${slugs% }"
    if [ -n "$slugs" ] && [ -n "$ecom" ] && [ "$slugs" = "$ecom" ]; then
        nonempty="$(printf '%s\n' "$cats" | grep . | grep -vc ":''$")"
        echo "ecommerce $nonempty"
    else
        echo "other ${slugs:-؟}"
    fi
}
# ─── نِهايَةُ branch_of ───

SLUG="rewalk-$MARK"
P_ENT="05${MARK}00"; P_VEN="05${MARK}01"; P_BUY="05${MARK}02"; P_ROL="05${MARK}03"
J_ENT="$WORK/ent"; J_ADM="$WORK/adm"; J_VEN="$WORK/ven"; J_BUY="$WORK/buy"; J_ROL="$WORK/rol"
ADDR="الرِياض — حَيُّ الفَحصِ $MARK"
F="$WORK/page.html"

# ‏(‏2026-10-24، S15 H(1)): المَتجَرُ لا يُطبَعُ هُنا — قَد يَصيرُ مَتجَراً مَبنِيّاً سَلَفاً في الخُطوَة ٢، فَيُطبَعُ سَطرُه بَعدَها.
printf 'الخادِم: %s · العَلامَة: %s · التاريخ: %s\n' "$BASE" "$MARK" "$(date -u +%FT%TZ)"

# ═══ ٠) الخادِمُ يُجيبُ والقاعِدَةُ مَعَه ═══════════════════════════════
STEP="٠ — الجاهِزِيَّة"
say "٠ · /health/ready"
get - /health/ready "$F"
check "$([ "$CODE" = 200 ] && grep -q '"ready":true' "$F" && echo 1 || echo 0)" "GET /health/ready → $CODE $(cat "$F")"
[ "$CODE" = 200 ] || die "الخادِمُ لا يُجيب"

# ═══ ١) رائِدُ أَعمالٍ جَديد — مِن دَعوَةِ الصَفحَةِ الأُولى إلى استِمارَةِ الإنشاء ═════════
# ‏(‏2026-11-07، الموجَة ١٣ W13-1·٩ — ADR-049 §١٠): كانَت الخُطوَةُ ١ «‏/studio/begin حَتّى التَحليل» بِجِدارِها ونافِذَتِها ومَسارِ التَطوير،
# والخُطوَةُ ٢ تَبني مِن جَلسَةِ تَحليلٍ مُكتَمِلَةٍ أَو مِن جَلسَةِ العَيِّنَة. صارَتا ما يَنقُرُه رائِدُ الأَعمالِ بِلا مِفتاح — والتالي مِن الصَفحَةِ الَّتي قَبلَه.
STEP="١ — مِن الصَفحَةِ الأُولى إلى استِمارَةِ الإنشاء"
CREATE_CTA="$(ar landing.cta.create)"
say "١ · رائِدُ أَعمالٍ جَديد: «$CREATE_CTA» ← دُخول ← شُروط ← استِمارَةُ الإنشاء"
get "$J_ENT" / "$F"
NBEGIN="$(count "$F" 'action="/studio/begin"')"
CTA="$(grep -o 'href="/studio/new"' "$F" | head -1 | sed 's/^href="//; s/"$//')"
check "$([ "$CODE" = 200 ] && [ -n "$CTA" ] && [ "$NBEGIN" = 0 ] && has "$F" "$CREATE_CTA" && echo 1 || echo 0)" \
    "GET / → $CODE · دَعوَةُ «$CREATE_CTA» إلى ${CTA:-غائِب} · استِمارَةُ /studio/begin = $NBEGIN"
[ -n "$CTA" ] || die "لا دَعوَةَ إلى /studio/new في الصَفحَةِ الأُولى"
# ‏(‏2026-11-07، مُراجَعَةُ عَدَساتِ C1 · W13-1·C1D-1) التَحويلُ مِن الخادِم (‏302) لا سكربتٌ في الصَفحَة: النَقرُ تَنَقُّلٌ مُحَسَّنٌ لا يُنَفِّذُ سكربتاً أَدرَجَتهُ
# الرُقعَة، فَكانَت هذِه الخُطوَةُ ✓ وصَفحَةُ الزائِرِ فارِغَة. والنَقرُ نَفسُه في مُتَصَفِّحٍ حَقيقيٍّ يَقيسُه `verify-runtime.cs --click … --expect …`.
get "$J_ENT" "$CTA" "$F"
check "$([ "$CODE" = 302 ] && [ "$LOC" = /studio/auth ] && echo 1 || echo 0)" \
    "GET $CTA (بِلا جَلسَة) → $CODE ${LOC:-بِلا تَحويل} · إلى /studio/auth"
post "$J_ENT" /studio/auth/login --data-urlencode "phone=$P_ENT"
check "$([ "$CODE" = 302 ] && [[ "$LOC" == *stage=verify* ]] && echo 1 || echo 0)" "POST /studio/auth/login → $CODE $LOC"
get "$J_ENT" "/studio/auth?stage=verify&method=phone&phone=$P_ENT" "$F"
check "$([ "$CODE" = 200 ] && has "$F" "$(ar studio.auth.dev_hint)" && echo 1 || echo 0)" \
    "GET /studio/auth?stage=verify → $CODE · «$(ar studio.auth.dev_hint)»"
post "$J_ENT" /studio/auth/verify --data-urlencode method=phone --data-urlencode "phone=$P_ENT" --data-urlencode "code=$DEV_CODE"
check "$([ "$CODE" = 302 ] && [ "$LOC" = "$CTA" ] && echo 1 || echo 0)" \
    "POST /studio/auth/verify → $CODE $LOC  (‏StudioStoreCreate.AfterSignIn: لا مَتجَرَ ← الاستِمارَة)"
[ "$LOC" = "$CTA" ] || die "الدُخولُ لَم يُعِد إلى $CTA بَل إلى «$LOC» — ‏MARK=$MARK مُكَرَّرٌ عَلى هذِه القاعِدَة؟ هاتِفُ رائِدِ الأَعمالِ ($P_ENT) يَملِكُ مَتجَراً فَيَعودُ إلى لَوحَتِه؛ أَعِد بِعَلامَةٍ أُخرى"

# ‏ذَهابُ الشُروطِ وإيابُها: الاستِمارَةُ تُرسِلُ غَيرَ المُوافِقِ إلى صَفحَتِها، و`returnUrl` مِن استِمارَةِ القَبولِ يُعيدُه.
get "$J_ENT" "$CTA" "$F"
CONSENT_AT="$([ "$CODE" = 302 ] && [[ "$LOC" == /studio/consent* ]] && echo "$LOC")"
check "$([ "$CODE" = 302 ] && [ -n "$CONSENT_AT" ] && echo 1 || echo 0)" \
    "GET $CTA (لا قَبولَ بَعد) → $CODE · إلى ${CONSENT_AT:-غائِب} (‏HasConsentedAsync = false)"
[ -n "$CONSENT_AT" ] || die "الاستِمارَةُ لا تُرسِلُ غَيرَ المُوافِقِ إلى المُوافَقَة"
get "$J_ENT" "$CONSENT_AT" "$F"
CONSENT_DUMP="$(form_dump "$F" 'consent/accept')"
CONSENT_ACT="$(fd_action "$CONSENT_DUMP")"; RETURN_TO="$(fd_get "$CONSENT_DUMP" HIDDEN returnUrl)"
check "$([ "$CODE" = 200 ] && [ "$CONSENT_ACT" = /studio/consent/accept ] && [ "$RETURN_TO" = "$CTA" ] \
        && has "$F" "$(ar studio.consent.version)" && echo 1 || echo 0)" \
    "GET $CONSENT_AT → $CODE · الاستِمارَةُ إلى ${CONSENT_ACT:-غائِبَة} بِـreturnUrl=${RETURN_TO:-غائِب} · «$(ar studio.consent.version) …»"
post "$J_ENT" "${CONSENT_ACT:-/studio/consent/accept}" --data-urlencode "returnUrl=$RETURN_TO"
check "$([ "$CODE" = 302 ] && [ "$LOC" = "$CTA" ] && echo 1 || echo 0)" "POST ${CONSENT_ACT:-غائِب} → $CODE $LOC  (عادَ إلى الاستِمارَة)"
get "$J_ENT" "$CTA" "$F"
CREATE_DUMP="$(form_dump "$F" 'name="slug"')"
CREATE_ACT="$(fd_action "$CREATE_DUMP")"; COLOR="$(fd_get "$CREATE_DUMP" VALUE color)"
UNKNOWN_REQ="$(fd_kind "$CREATE_DUMP" FIELD | grep -vxE 'name|slug|color' | tr '\n' ' ')"
check "$([ "$CODE" = 200 ] && [ -n "$CREATE_ACT" ] && [ -n "$COLOR" ] && [ -z "$UNKNOWN_REQ" ] && has "$F" "$(ar studio.create.title)" && echo 1 || echo 0)" \
    "GET $CTA (مُوافِق) → $CODE · «$(ar studio.create.title)» · الاستِمارَةُ إلى ${CREATE_ACT:-غائِبَة} · اللَونُ المُعَبَّأ ${COLOR:-غائِب} · مَطلوبٌ لا يَعرِفُه السكربت: ${UNKNOWN_REQ:-لا شَيء}"
[ -n "$CREATE_ACT" ] || die "لا استِمارَةَ إنشاءٍ في $CTA"

# ═══ ٢) إنشاءُ المَتجَر — والفَرعُ يُفحَصُ كُلَّ تَشغيل ═══════════════════
STEP="٢ — إنشاءُ المَتجَر"
say "٢ · إنشاءُ المَتجَرِ بِاليَد"
# ‏المالِكُ رائِدُ الأَعمالِ نَفسُه في كُلِّ تَشغيل — وجَلسَتُه وهاتِفُه يَمشِيانِ الخُطوَتَينِ ٨ و٩. والقِيَمُ المَكتوبَةُ ما يَكتُبُه في الحُقول،
# واللَونُ قيمَةُ الاستِمارَةِ كَما رُسِمَت.
OWNER_JAR="$J_ENT"; OWNER_PHONE="$P_ENT"
post "$J_ENT" "$CREATE_ACT" --data-urlencode "slug=$SLUG" --data-urlencode "name=سوقُ الجَولَة $MARK" \
    --data-urlencode "color=$COLOR" --data-urlencode "tagline=مَتجَرٌ يَجمَعُ بائِعين"
check "$([ "$CODE" = 302 ] && [ "$LOC" = "/studio/apps/$SLUG?built=1" ] && echo 1 || echo 0)" \
    "POST $CREATE_ACT → $CODE $LOC"
[ "$LOC" = "/studio/apps/$SLUG?built=1" ] || die "لَم يُنشَأ المَتجَر ($LOC) — ‏MARK مُكَرَّرٌ يُرَدُّ بِـslug_taken، فَأَعِد بِعَلامَةٍ أُخرى"

# ‏فِئاتُ المَتجَرِ المَنشَإ وفَرعُها مِن المَصنَع — مِن مُحَرِّرِ الفِئاتِ لِلمالِك (‏`StudioAppCategories.razor`: نَصٌّ
# مَخفِيٌّ سَطرُه «slug | الاسم | أَيقونَة | تَجميع»). ‏(‏2026-10-13، مُراجَعَةُ S12: كانَت نَقرَةُ الخُطوَة ٥ تُسَمّى «FLGAP-04
# بِحَرفِه» عَلى مَتجَرِ العَيِّنَة — فَرعُ السُقوط `general` بِكِندٍ فارِغٍ قَبلَ ADR-034 أَيضاً (`git show c4278c0^`)،
# فَهي خَضراءُ عَلى الكاتِبِ القَديمِ والجَديدِ مَعاً — ولا يُطبَعُ الفَرع.) ‏(‏2026-11-07، W13-1·٩): ولا مَتجَرَ عَيِّنَةٍ بَعدَ اليَوم —
# الإنشاءُ بِاليَدِ يُعطي فَرعَ `ecommerce` دائِماً، فَالحُكمُ فَحصٌ في كُلِّ تَشغيل.
printf '\nالمَتجَر: %s/%s\n' "$BASE" "$SLUG"
ECOM="$(ecommerce_slugs)"
get "$OWNER_JAR" "/studio/apps/$SLUG/categories" "$F"
CATS="$(dec "$F" | perl -0777 -ne 'if (m{<textarea name="categories"[^>]*>(.*?)</textarea>}s) { for (split /\n/, $1) {
    my @p = map { s/^\s+|\s+$//gr } split /\|/, $_, -1; print "$p[0]:\x27$p[3]\x27\n" if @p >= 4 && length $p[0] } }')"
NCATS="$(printf '%s' "$CATS" | grep -c .)"
CATS_LINE="$(printf '%s' "$CATS" | tr '\n' ' ')"; CATS_LINE="${CATS_LINE% }"
check "$([ "$CODE" = 200 ] && [ "$NCATS" -gt 0 ] && echo 1 || echo 0)" \
    "GET /studio/apps/$SLUG/categories (المالِك) → $CODE · $NCATS فِئَة: ${CATS_LINE:-—}"
BRANCH_OUT="$(branch_of "$CATS" "$ECOM")"; BRANCH="${BRANCH_OUT%% *}"; BRANCH_REST="${BRANCH_OUT#* }"
if [ "$BRANCH" = ecommerce ]; then FLGAP="يُمَيِّزُ FLGAP-04"; else FLGAP="لا يُمَيِّزُ FLGAP-04"; fi
check "$([ "$BRANCH" = ecommerce ] && [ "$BRANCH_REST" = 0 ] && echo 1 || echo 0)" \
    "FLGAP-04 (‏ADR-034 §١(أ)، كُلَّ تَشغيل): الإنشاءُ بِاليَد ← فَرعُ ${BRANCH} (المُتَوَقَّع ecommerce: $ECOM) · كِندٌ غَيرُ فارِغ/الفِئات: $BRANCH_REST — نَقرَةُ الخُطوَة ٥ $FLGAP"

# ═══ ٣) الزائِرُ بِلا جَلسَة ══════════════════════════════════════════
STEP="٣ — الزائِر"
say "٣ · الزائِرُ عَلى /$SLUG"
BROWSE="$(ar roles.landing.browse)"; HAVE_ACCOUNT="$(ar roles.landing.have_account)"
# ‏(‏2026-10-29، د34، ‏ADR-048 — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه) كانَ الفَحصُ رابِطَ «$BROWSE» عَلى البَوّابَة. والآن رَئيسِيَّةُ المُشتَري:
# الهيرو، وإجراءُ الاستِكشافِ إلى /$SLUG/explore، ثُمَّ بِطاقَةُ `customer` وسَطرُ «لَدَيكَ حِساب» تَحتَها — ولا «$BROWSE» (زائِرُها يَتَصَفَّح).
get - "/$SLUG" "$F"
check "$([ "$CODE" = 200 ] && grep -q 'class="ac-hero ' "$F" && grep -q "href=\"/$SLUG/explore\"" "$F" \
        && grep -q "href=\"/$SLUG/r/customer/login\"" "$F" && has "$F" "$HAVE_ACCOUNT" && ! has "$F" "$BROWSE" && echo 1 || echo 0)" \
    "GET /$SLUG (بِلا جَلسَة) → $CODE · رَئيسِيَّةُ المُشتَري (هيرو ورابِطُ /$SLUG/explore) ثُمَّ البِطاقاتُ و«$HAVE_ACCOUNT» — ولا «$BROWSE»"
get - "/$SLUG/explore" "$F"
check "$([ "$CODE" = 200 ] && echo 1 || echo 0)" "GET /$SLUG/explore (بِلا جَلسَة) → $CODE"

# ═══ ٤) بائِعٌ يُسَجِّلُ ويُسَكِّنُ نَفسَه ويَنشُر ════════════════════
STEP="٤ — البائِع"
say "٤ · بائِعٌ: تَسجيل ← دَورُ vendor ذاتِيّاً ← نَشر"
store_login "$J_VEN" "$SLUG" "$P_VEN"
# ‏(2026-10-29، قَرارُ المالِك د40 «ج») أَوَّلُ دُخولٍ لِهاتِفٍ جَديدٍ يَسبِقُه خُطوَةُ الاسم — والاسمُ مَتروكٌ هُنا عَمداً («لاحِقاً»): اسمُ النَشاطِ
# في التَهيِئَةِ أَدناه هُوَ ما يَراه المُشتَري، وهو ما تَقيسُه الخُطوَة ٦.
check "$([[ "$LOC" == */me/name\?next=* ]] && echo 1 || echo 0)" "أَوَّلُ دُخولٍ لِلبائِعِ ← خُطوَةُ الاسم (‏د40) — $LOC"
# ‏(2026-10-29، مُراجَعَةُ تَيّارِ S2 الجَولَة ١ — قَرارُ المالِك د40 «ج») **وأَوَّلُ دُخولٍ مِن صَفحَةِ دُخولِ دَور** (`RoleLanding.razor` ← `/{slug}/r/{role}/login`):
# بابُ التَحَقُّقِ يَأخُذُ الدَورَ مِن `Referer` فَيُحَوِّلُ إلى `/{slug}/r/{role}/me/name`، وكانَت الصَفحَةُ لا تُعلِنُه — ‏404 مَقيسٌ حَيّاً، والسَطرُ أَعلاه أَعمى
# عَنه لِأَنّ `store_login` لا يُرسِلُ `Referer`. حِسابٌ رابِعٌ بِهاتِفِ الجَولَة، والاستِمارَتانِ تُقرَآنِ مِن صَفحَتَيهِما (`form_dump`) وتُرسَلانِ بِـ`Referer`
# الصَفحَةِ الَّتي رَسَمَتهُما — كَالمُتَصَفِّح.
ROLE_LOGIN="/$SLUG/r/vendor/login"
get "$J_ROL" "$ROLE_LOGIN" "$F"
ROLE_DUMP="$(form_dump "$F" 'auth/phone/login')"; ROLE_ACT="$(fd_action "$ROLE_DUMP")"; ROLE_AS="$(fd_get "$ROLE_DUMP" HIDDEN as)"
check "$([ "$CODE" = 200 ] && [ "$ROLE_ACT" = "/$SLUG/auth/phone/login" ] && [ "$ROLE_AS" = vendor ] && echo 1 || echo 0)" \
    "GET $ROLE_LOGIN → $CODE · الاستِمارَةُ إلى ${ROLE_ACT:-غائِب} بِـas=${ROLE_AS:-غائِب}"
post "$J_ROL" "$ROLE_ACT" -H "Referer: $BASE$ROLE_LOGIN" --data-urlencode "phone=$P_ROL" --data-urlencode "as=$ROLE_AS"
check "$([ "$CODE" = 302 ] && [[ "$LOC" == "$ROLE_LOGIN?"*stage=verify* ]] && echo 1 || echo 0)" "POST $ROLE_ACT (مِن $ROLE_LOGIN) → $CODE $LOC"
ROLE_VERIFY="$LOC"
get "$J_ROL" "$ROLE_VERIFY" "$F"
ROLE_DUMP="$(form_dump "$F" 'auth/phone/verify')"; ROLE_ACT="$(fd_action "$ROLE_DUMP")"; ROLE_ARGS=(); ROLE_HIDDEN=()
while IFS='=' read -r n v; do [ -n "$n" ] && { ROLE_ARGS+=(--data-urlencode "$n=$v"); ROLE_HIDDEN+=("$n"); }; done < <(fd_kind "$ROLE_DUMP" HIDDEN)
post "$J_ROL" "${ROLE_ACT:-/$SLUG/auth/phone/verify}" -H "Referer: $BASE$ROLE_VERIFY" "${ROLE_ARGS[@]}" --data-urlencode "code=$DEV_CODE"
check "$([ "$CODE" = 302 ] && [[ "$LOC" == "/$SLUG/r/vendor/me/name?next="* ]] && echo 1 || echo 0)" \
    "POST ${ROLE_ACT:-غائِب} (مِن صَفحَةِ الدَور، حُقولُ الاستِمارَةِ المَخفِيَّة: ${ROLE_HIDDEN[*]:-—}) → $CODE $LOC (‏د40)"
ROLE_NAME_AT="$LOC"
get "$J_ROL" "$ROLE_NAME_AT" "$F"
check "$([ "$CODE" = 200 ] && grep -q 'name="fullName"' "$F" && echo 1 || echo 0)" "GET $ROLE_NAME_AT → $CODE · حَقلُ الاسم (‏د40 — كانَ 404)"
get "$J_VEN" "/$SLUG/me/role" "$F"
check "$([ "$CODE" = 200 ] && grep -q 'name="role" value="vendor"' "$F" && echo 1 || echo 0)" \
    "GET /$SLUG/me/role → $CODE · خِيارُ vendor ذاتِيُّ المَنح"
post "$J_VEN" "/$SLUG/me/role/save" --data-urlencode role=vendor
check "$([ "$CODE" = 302 ] && [[ "$LOC" != *err=* ]] && echo 1 || echo 0)" "POST /$SLUG/me/role/save role=vendor → $CODE $LOC"
if [[ "$LOC" == */me/role/onboarding ]]; then
    get "$J_VEN" "/$SLUG/me/role/onboarding" "$F"
    # الحُقولُ المَطلوبَةُ تُقرَأُ مِن الاستِمارَةِ نَفسِها لا تُكتَبُ ثابِتَة، ويُطبَعُ عَدَدُها وأَسماؤُها.
    # ‏(‏2026-10-13: كانَ السَطرُ يَطبَعُ `${#ARGS[@]}` — عَدَدَ وَسائِطِ curl، اثنانِ لِكُلِّ حَقل — فَقالَ
    # «2» لِحَقلٍ واحِد (`business_name` في `vendor.role.json`). العَدَدُ يُعَدُّ بِالأَسماءِ لا بِالوَسائِط.)
    FIELDS=(); ARGS=()
    while read -r n; do FIELDS+=("$n"); ARGS+=(--data-urlencode "$n=بائِعُ الجَولَة $MARK"); done \
        < <(grep -o '<input[^>]*name="role_vendor_attr_[a-z_]*"[^>]*required' "$F" | grep -o 'name="[^"]*"' | sed 's/name="//; s/"$//')
    post "$J_VEN" "/$SLUG/me/role/onboarding/save" "${ARGS[@]}"
    check "$([ "$CODE" = 302 ] && [[ "$LOC" != *err=* ]] && echo 1 || echo 0)" \
        "POST /$SLUG/me/role/onboarding/save (${#FIELDS[@]} مِن الاستِمارَة: ${FIELDS[*]:-—}) → $CODE $LOC"
fi
post "$J_VEN" "/$SLUG/terms/accept"
check "$([ "$CODE" = 302 ] && echo 1 || echo 0)" "POST /$SLUG/terms/accept → $CODE $LOC"
# ‏(‏2026-10-25، brief-P1b-2): «إعلان جَديد» يُنقَرُ مِن الصَفحَةِ الَّتي أَعادَت إلَيها الشُروط — لا عُنوانٌ يَبنيه السكربت.
TERMS_TO="$LOC"
get "$J_VEN" "$TERMS_TO" "$F"
NEW_HREF="$(grep -o "href=\"/$SLUG/[^\"]*create-listing\"" "$F" | head -1 | sed 's/^href="//; s/"$//')"
check "$([ "$CODE" = 200 ] && [ -n "$NEW_HREF" ] && echo 1 || echo 0)" \
    "GET $TERMS_TO (البائِع، حَيثُ أَعادَته الشُروط) → $CODE · رابِطُ النَشر: ${NEW_HREF:-غائِب}"
[ -n "$NEW_HREF" ] || die "لا رابِطَ إلى create-listing في $TERMS_TO"
get "$J_VEN" "$NEW_HREF" "$F"
CAT="$(grep -o '<input type="hidden" name="category" value="[^"]*"' "$F" | head -1 | sed 's/.*value="//; s/"$//')"
if [ -z "$CAT" ]; then
    # ‏فِئَةٌ واحِدَةٌ تُختارُ وَحدَها (حَقلٌ مَخفِيّ)، وأَكثَرُ مِنها شَبَكَةُ رَوابِطَ `create-listing?category=…`
    # (‏`CreateListing.razor`) — يُنقَرُ أَوَّلُها كَما يَنقُرُ البائِع. ‏(‏2026-10-13: كانَ الحَقلُ المَخفِيُّ وَحدَه
    # يُقرَأ — شَكلُ مَتجَرِ العَيِّنَةِ بِفِئَتِه الواحِدَة — فَمَتجَرُ فَرعِ `ecommerce` بِفِئَتَيه يُوقِفُ الرِحلَةَ «الفِئَة «»».)
    NPICK="$(count "$F" 'create-listing?category=')"
    PICK="$(grep -o 'href="[^"]*create-listing?category=[^"]*"' "$F" | head -1 | sed 's/^href="//; s/"$//')"
    check "$([ "$CODE" = 200 ] && [ -n "$PICK" ] && echo 1 || echo 0)" \
        "GET /$SLUG/create-listing → $CODE · شَبَكَةُ $NPICK فِئَة — يُنقَرُ أَوَّلُها: $PICK"
    [ -n "$PICK" ] || die "لا حَقلَ فِئَةٍ ولا شَبَكَةَ فِئاتٍ في /$SLUG/create-listing"
    get "$J_VEN" "$PICK" "$F"
    CAT="$(grep -o '<input type="hidden" name="category" value="[^"]*"' "$F" | head -1 | sed 's/.*value="//; s/"$//')"
fi
check "$([ "$CODE" = 200 ] && [ -n "$CAT" ] && echo 1 || echo 0)" "GET /$SLUG/create-listing → $CODE · الفِئَة «$CAT»"
# ‏(‏2026-10-25، brief-P1b-2): البابُ والحَقلانِ المَخفِيّانِ (الفِئَةُ والدَور) مِن الاستِمارَة، وكُلُّ حَقلٍ مَطلوبٍ فيها يُملَأُ أَو تَقِفُ الرِحلَة
# — لا `as=` فارِغاً مِن السكربت. والقِيَمُ المَكتوبَةُ (العُنوانُ والوَصفُ والسِعرُ والحَيّ) ما يَكتُبُه البائِع.
LISTING_DUMP="$(form_dump "$F" 'listings/create"')"
LISTING_ACTION="$(fd_action "$LISTING_DUMP")"; LISTING_AS="$(fd_get "$LISTING_DUMP" HIDDEN as)"
UNKNOWN_REQ="$(fd_kind "$LISTING_DUMP" FIELD | grep -vxE 'title|description|price|district' | tr '\n' ' ')"
check "$([ -n "$LISTING_ACTION" ] && [ "$(fd_get "$LISTING_DUMP" HIDDEN category)" = "$CAT" ] && [ -z "$UNKNOWN_REQ" ] && echo 1 || echo 0)" \
    "استِمارَةُ النَشر: ${LISTING_ACTION:-غائِبَة} · مَخفِيّان category=«$CAT» as=«$LISTING_AS» · مَطلوبٌ لا يَعرِفُه السكربت: ${UNKNOWN_REQ:-لا شَيء}"
[ -n "$LISTING_ACTION" ] || die "لا استِمارَةَ نَشرٍ في $NEW_HREF"
post "$J_VEN" "$LISTING_ACTION" -F "title=تَمرُ سُكَّري — إعلانُ فَحصٍ $MARK" \
    -F "description=إعلانُ فَحصٍ آليّ في جَولَةِ الجاهِزِيَّة" -F "category=$CAT" -F price=100 \
    -F "city=الرِياض" -F "district=العُلَيّا" -F "as=$LISTING_AS"
LID="${LOC##*/listings/}"
check "$([ "$CODE" = 302 ] && [[ "$LID" =~ ^[0-9a-f-]{36}$ ]] && echo 1 || echo 0)" "POST /$SLUG/listings/create → $CODE $LOC"
[[ "$LID" =~ ^[0-9a-f-]{36}$ ]] || die "لَم يُنشَر الإعلان"

# ═══ ٥) مُشتَرٍ: سَلَّة ← دَفعٌ نَقدِيّ، والبِطاقَةُ لا تُعرَض ═══════════
STEP="٥ — المُشتَري"
say "٥ · مُشتَرٍ: تَسجيل ← «$(ar listings.detail.cta_cart)» ← نَقداً"
store_login "$J_BUY" "$SLUG" "$P_BUY"
post "$J_BUY" "/$SLUG/me/role/save" --data-urlencode role=customer
check "$([ "$CODE" = 302 ] && echo 1 || echo 0)" "POST /$SLUG/me/role/save role=customer → $CODE $LOC"
CART="$(ar listings.detail.cta_cart)"; RENT_CTA="$(ar pattern.rental.cta)"; CART_TAB="$(ar shell.nav.cart)"
get "$J_BUY" "/$SLUG/listings/$LID" "$F"
# ‏صَفحَةُ إعلانِ بائِعٍ في مَتجَرٍ بَناهُ المَصنَع، بِكوكي عُضو (‏ADR-032 §٦‑ج الصَفّ 2) — والقارِئانِ كِلاهُما
# (‏ADR-034 §١(أ)٥): استِمارَةُ `cart/add` خَلفَ `PatternOf`، وتَبويبُ السَلَّةِ خَلفَ نُسخَتِه في `MainLayout.razor`.
# والحُكمُ مِن فَرعِ الخُطوَة ٢ — «يُمَيِّز» عَلى `ecommerce` وَحدَه. ‏(‏2026-10-13: كانَ السَطرُ يَقولُ «FLGAP-04»
# لِكُلِّ مَتجَرٍ ولا يَفحَصُ التَبويب؛ وعَلى الكاتِبِ القَديمِ يُرسَمُ نَصُّ `pattern.rental.cta` بَدَلَ الاستِمارَة، فَيُطبَعُ إن رُسِم.)
NFORM="$(count "$F" "action=\"/$SLUG/listings/$LID/cart/add\"")"
HASCART="$(has "$F" "$CART" && echo 1 || echo 0)"
check "$([ "$CODE" = 200 ] && [ "$NFORM" = 1 ] && [ "$HASCART" = 1 ] && echo 1 || echo 0)" \
    "GET /$SLUG/listings/$LID (عُضو) → $CODE · استِمارَةُ cart/add = $NFORM · «$CART» = $HASCART$(has "$F" "$RENT_CTA" && printf ' · «%s» مَرسوم' "$RENT_CTA") — $FLGAP"
NTAB="$(dec "$F" | SL="$SLUG" LB="$CART_TAB" perl -0777 -ne '$n = 0;
    while (m{<a href="/\Q$ENV{SL}\E/cart"[^>]*>(.*?)</a>}gs) { ($t = $1) =~ s/<[^>]*>//g; $t =~ s/^\s+|\s+$//g; $n++ if $t eq $ENV{LB} }
    print $n')"
check "$([ "${NTAB:-0}" -ge 1 ] && echo 1 || echo 0)" \
    "… تَبويبُ «$CART_TAB» إلى /$SLUG/cart في الشَريط = ${NTAB:-0} (‏MainLayout — القارِئُ الثاني) — $FLGAP"
post "$J_BUY" "/$SLUG/listings/$LID/cart/add" --data-urlencode qty=1
check "$([ "$CODE" = 302 ] && [ "$LOC" = "/$SLUG/cart" ] && echo 1 || echo 0)" "POST /$SLUG/listings/$LID/cart/add → $CODE $LOC"
# ‏(‏2026-10-25، brief-P1b-2): رابِطُ الدَفعِ مِن السَلَّة، والخُطوَةُ ٢ مِن استِمارَةِ الخُطوَةِ ١ (بابُها وحَقلُها المَخفِيّ وحُقولُها المَطلوبَة)
# — كانَ `checkout?step=2&name=…` عُنواناً يَبنيه السكربت. والقِيَمُ ما يَكتُبُه المُشتَري؛ وحَقلٌ مَطلوبٌ لا يَعرِفُه يُوقِفُ الرِحلَة.
get "$J_BUY" "$LOC" "$F"
PAY_HREF="$(grep -o "href=\"/$SLUG/[^\"]*checkout\"" "$F" | head -1 | sed 's/^href="//; s/"$//')"
check "$([ "$CODE" = 200 ] && [ -n "$PAY_HREF" ] && echo 1 || echo 0)" "GET /$SLUG/cart → $CODE · رابِطُ الدَفع: ${PAY_HREF:-غائِب}"
[ -n "$PAY_HREF" ] || die "لا رابِطَ دَفعٍ في السَلَّة"
get "$J_BUY" "$PAY_HREF" "$F"
# ‏(2026-10-29، تَيّارُ S2 — ولا يُعادُ حَرفٌ فَوقَه): الخُطوَتانِ صارَتا `POST /{slug}/checkout/details` بِحَقلِ `from` (‏`0d1411d`، قَرارُ المالِك د60)
# والسكربتُ ما زالَ يَبحَثُ عَن `name="step" value="2"` ويَبني `GET …?step=2&name=…` — فَتَوَقَّفَت الجَولَةُ عِندَ «استِمارَةُ الخُطوَة ١ لا تُملَأ»
# (مَقيسٌ عَلى `wasayel_s2_d40_20261029`: فُحِصَ 29 · فَشِلَ 1). صارَت الخُطوَتانِ تُرسَلانِ بِاستِمارَتَيهِما كَالمُتَصَفِّح، والتالي مِن `Location`.
STEP1_DUMP="$(form_dump "$F" 'name="from" value="1"')"
STEP1_ACTION="$(fd_action "$STEP1_DUMP")"; STEP1_REQ="$(fd_kind "$STEP1_DUMP" FIELD | tr '\n' ' ')"; STEP1_REQ="${STEP1_REQ% }"
STEP1_FROM="$(fd_get "$STEP1_DUMP" HIDDEN from)"; STEP1_ARGS=(--data-urlencode "from=$STEP1_FROM"); UNKNOWN_REQ=""
for n in $STEP1_REQ; do
    case "$n" in
        name)  STEP1_ARGS+=(--data-urlencode "name=مُشتَري الجَولَة") ;;
        phone) STEP1_ARGS+=(--data-urlencode "phone=$P_BUY") ;;
        addr)  STEP1_ARGS+=(--data-urlencode "addr=$ADDR") ;;
        *)     UNKNOWN_REQ="$UNKNOWN_REQ $n" ;;
    esac
done
check "$([ "$CODE" = 200 ] && [ -n "$STEP1_ACTION" ] && [ "$STEP1_FROM" = 1 ] && [ -n "$STEP1_REQ" ] && [ -z "$UNKNOWN_REQ" ] && echo 1 || echo 0)" \
    "GET $PAY_HREF → $CODE · استِمارَةُ الخُطوَة ١ إلى ${STEP1_ACTION:-غائِبَة} (from=${STEP1_FROM:-—}) · مَطلوبُها: ${STEP1_REQ:-—}${UNKNOWN_REQ:+ · لا يَعرِفُه السكربت:$UNKNOWN_REQ}"
[ -n "$STEP1_ACTION" ] && [ -z "$UNKNOWN_REQ" ] || die "استِمارَةُ الخُطوَة ١ لا تُملَأ"
post "$J_BUY" "$STEP1_ACTION" "${STEP1_ARGS[@]}"
check "$([ "$CODE" = 302 ] && [[ "$LOC" == *checkout\?step=2 ]] && echo 1 || echo 0)" "POST $STEP1_ACTION from=1 → $CODE $LOC"
get "$J_BUY" "$LOC" "$F"
check "$([ "$CODE" = 200 ] && [ "$(count "$F" 'name="pay" value="cash"')" = 1 ] && has "$F" "$(ar checkout.payment.cash)" && echo 1 || echo 0)" \
    "GET /$SLUG/checkout?step=2 → $CODE · «$(ar checkout.payment.cash)» مَعروض"
check "$([ "$(count "$F" 'name="pay" value="card"')" = 0 ] && echo 1 || echo 0)" \
    "GET /$SLUG/checkout?step=2 → البِطاقَةُ غَيرُ مَعروضَة (name=\"pay\" value=\"card\" = $(count "$F" 'name="pay" value="card"'))"
# ‏(‏2026-10-25، brief-P1b-2): الخُطوَةُ ٣ مِن استِمارَةِ الخُطوَةِ ٢ بِخِيارِ «نَقد» المَرسومِ فيها، والإرسالُ مِن استِمارَةِ الخُطوَةِ ٣
# بِحُقولِها المَخفِيَّة — والمُشتَري لا يَكتُبُ بَعدَ الخُطوَةِ ١ شَيئاً.
STEP2_DUMP="$(form_dump "$F" 'name="from" value="2"')"
STEP2_ACTION="$(fd_action "$STEP2_DUMP")"; STEP2_ARGS=()
while IFS= read -r kv; do [ -n "$kv" ] && STEP2_ARGS+=(--data-urlencode "$kv"); done < <(fd_kind "$STEP2_DUMP" HIDDEN)
CASH_RADIO="$(fd_kind "$STEP2_DUMP" RADIO | grep -cx 'pay=cash')"
check "$([ -n "$STEP2_ACTION" ] && [ "$CASH_RADIO" = 1 ] && [ "$(fd_get "$STEP2_DUMP" HIDDEN from)" = 2 ] && echo 1 || echo 0)" \
    "استِمارَةُ الخُطوَة ٢ إلى ${STEP2_ACTION:-غائِبَة} · خِيارُ pay=cash = $CASH_RADIO · مَخفِيُّها:$(fd_kind "$STEP2_DUMP" HIDDEN | sed 's/=.*//' | tr '\n' ' ')"
[ -n "$STEP2_ACTION" ] && [ "$CASH_RADIO" = 1 ] || die "لا خِيارَ نَقدٍ في استِمارَةِ الخُطوَة ٢"
post "$J_BUY" "$STEP2_ACTION" "${STEP2_ARGS[@]}" --data-urlencode pay=cash
check "$([ "$CODE" = 302 ] && [[ "$LOC" == *checkout\?step=3 ]] && echo 1 || echo 0)" "POST $STEP2_ACTION from=2 pay=cash → $CODE $LOC"
get "$J_BUY" "$LOC" "$F"
SUBMIT_DUMP="$(form_dump "$F" 'checkout/submit"')"
SUBMIT_ACTION="$(fd_action "$SUBMIT_DUMP")"; SUBMIT_ARGS=()
while IFS= read -r kv; do [ -n "$kv" ] && SUBMIT_ARGS+=(--data-urlencode "$kv"); done < <(fd_kind "$SUBMIT_DUMP" HIDDEN)
check "$([ "$CODE" = 200 ] && [ -n "$SUBMIT_ACTION" ] && [ "$(fd_get "$SUBMIT_DUMP" HIDDEN pay)" = cash ] \
        && [ "$(fd_get "$SUBMIT_DUMP" HIDDEN phone)" = "$P_BUY" ] && echo 1 || echo 0)" \
    "GET /$SLUG/checkout?step=3 → $CODE · استِمارَةُ الإرسالِ إلى ${SUBMIT_ACTION:-غائِبَة} بِـ$(( ${#SUBMIT_ARGS[@]} / 2 )) حَقلاً مَخفِيّاً (pay=$(fd_get "$SUBMIT_DUMP" HIDDEN pay))"
[ -n "$SUBMIT_ACTION" ] || die "لا استِمارَةَ إرسالٍ في الخُطوَة ٣"
# ‏مِجَسُّ الرَفض — طَلَبٌ لا تَرسُمُه الصَفحَةُ عَمداً: الحُقولُ نَفسُها وpay=card.
post "$J_BUY" "$SUBMIT_ACTION" --data-urlencode "name=مُشتَري الجَولَة" --data-urlencode "phone=$P_BUY" \
    --data-urlencode "addr=$ADDR" --data-urlencode pay=card
check "$([ "$CODE" = 302 ] && [[ "$LOC" == *err=pay_card_unavailable* ]] && echo 1 || echo 0)" \
    "POST /$SLUG/checkout/submit pay=card → $CODE · err=pay_card_unavailable (النُقطَةُ تَرفُضُ ما لا تَعرِضُه الشاشَة)"
post "$J_BUY" "$SUBMIT_ACTION" "${SUBMIT_ARGS[@]}"
DID="${LOC##*/deals/}"
check "$([ "$CODE" = 302 ] && [[ "$DID" =~ ^[0-9a-f-]{36}$ ]] && echo 1 || echo 0)" "POST $SUBMIT_ACTION (حُقولُ الاستِمارَة، pay=cash) → $CODE $LOC"
[[ "$DID" =~ ^[0-9a-f-]{36}$ ]] || die "لَم تُنشَأ صَفقَة"

# ═══ ٦) البائِعُ يُشعَرُ ويَقبَلُ ويَرى العُنوان ═════════════════════════
STEP="٦ — قَبولُ البائِع"
say "٦ · البائِع: إشعار ← قَبول ← العُنوان"
get "$J_VEN" "/$SLUG/notifications" "$F"
check "$([ "$CODE" = 200 ] && has "$F" "$(ar notifications.deal_new.title)" && grep -q "/$SLUG/deals/$DID" "$F" && echo 1 || echo 0)" \
    "GET /$SLUG/notifications (البائِع) → $CODE · «$(ar notifications.deal_new.title)» ورابِطُ الصَفقَة"
get "$J_VEN" "/$SLUG/deals/$DID" "$F"
check "$([ "$CODE" = 200 ] && grep -q "action=\"/$SLUG/deals/$DID/accept\"" "$F" && has "$F" "$ADDR" && echo 1 || echo 0)" \
    "GET /$SLUG/deals/$DID (البائِع، قَبلَ القَبول) → $CODE · استِمارَةُ القَبولِ والعُنوانُ «$ADDR»"
post "$J_VEN" "/$SLUG/deals/$DID/accept"
check "$([ "$CODE" = 302 ] && [ "$LOC" = "/$SLUG/deals/$DID" ] && echo 1 || echo 0)" "POST /$SLUG/deals/$DID/accept → $CODE $LOC"
get "$J_BUY" "/$SLUG/notifications" "$F"
check "$([ "$CODE" = 200 ] && has "$F" "$(ar notifications.deal_accepted.title)" && echo 1 || echo 0)" \
    "GET /$SLUG/notifications (المُشتَري) → $CODE · «$(ar notifications.deal_accepted.title)»"
# ‏(2026-10-29، قَرارُ المالِك د40 «ج») ونَصُّ الإشعارِ المُخَزَّنِ يُسَمّي البائِعَ بِاسمِ نَشاطِه لا «$(ar me.profile.new_member)»، وصَفحَةُ الصَفقَةِ كَذلك.
VEN_SHOWN="بائِعُ الجَولَة $MARK"
check "$(has "$F" "$VEN_SHOWN قَبِلَ طَلَبَكَ" && ! has "$F" "$(ar me.profile.new_member) قَبِلَ طَلَبَكَ" && echo 1 || echo 0)" \
    "إشعارُ القَبولِ عِندَ المُشتَري: «$VEN_SHOWN قَبِلَ طَلَبَكَ …» — اسمُ النَشاطِ لا «$(ar me.profile.new_member)» (‏د40)"
get "$J_BUY" "/$SLUG/deals/$DID" "$F"
check "$([ "$CODE" = 200 ] && has "$F" "$VEN_SHOWN" && echo 1 || echo 0)" "GET /$SLUG/deals/$DID (المُشتَري) → $CODE · البائِعُ «$VEN_SHOWN» (‏د40)"

# ═══ ٧) إلى «تَسليم» — وزِرُّ الشَحنِ مَخفِيٌّ بِلا رَمزِ OTO ═══════════
STEP="٧ — التَسليم"
say "٧ · البائِعُ يُحَرِّكُ الصَفقَةَ إلى «$(stage_label Delivered)»"
SHIP_A="$(ar deals.detail.ship_submit)"; SHIP_B="$(ar deals.detail.ship_resume)"
NEXT_PREFIX="$(ar deals.detail.next_step)"; AFTER_DELIVERED="$(stage_label Reviewed)"
REACHED=0
for i in 1 2 3 4 5 6; do
    get "$J_VEN" "/$SLUG/deals/$DID" "$F"
    LINE_NEXT="$(dec "$F" | sed 's/<[^>]*>/\n/g' | grep -m1 -F -- "$NEXT_PREFIX")"
    NEXT="${LINE_NEXT#*"$NEXT_PREFIX"}"; NEXT="${NEXT#"${NEXT%%[![:space:]]*}"}"; NEXT="${NEXT%"${NEXT##*[![:space:]]}"}"
    SHIPF="$(count "$F" "/deals/$DID/ship\"")"
    check "$([ "$SHIPF" = 0 ] && ! has "$F" "$SHIP_A" && ! has "$F" "$SHIP_B" && has "$F" "$ADDR" && echo 1 || echo 0)" \
        "GET /$SLUG/deals/$DID (البائِع) → $CODE · التالِيَة «${NEXT:-؟}» · استِمارَةُ /ship = $SHIPF وبِلا «$SHIP_A» · العُنوانُ ظاهِر"
    if [ "$NEXT" = "$AFTER_DELIVERED" ]; then REACHED=1; break; fi
    grep -q "action=\"/$SLUG/deals/$DID/advance\"" "$F" || break
    post "$J_VEN" "/$SLUG/deals/$DID/advance"
    check "$([ "$CODE" = 302 ] && [ "$LOC" = "/$SLUG/deals/$DID" ] && echo 1 || echo 0)" "POST /$SLUG/deals/$DID/advance [$i] → $CODE $LOC"
done
check "$REACHED" "الصَفقَةُ عِندَ «$(stage_label Delivered)» (التالِيَةُ «$AFTER_DELIVERED»)"
post - "/$SLUG/shipping/oto/order-status" -H 'Content-Type: application/json' -d '{}'
check "$([ "$CODE" = 404 ] && echo 1 || echo 0)" \
    "POST /$SLUG/shipping/oto/order-status (مَتجَرٌ بِلا ناقِل) → $CODE (‏DEPLOY.md §٢·ط: 404 بِلا قِراءَة)"
# ‏(‏2026-10-24، S15 H(2)): **ضابِطٌ مُوجَب** — ‏404 يُجيبُ بِه أَيضاً مَسارٌ لا وُجودَ لَه (خَطَأٌ في الهِجاء)، فَوَحدَه لا يُثبِتُ أَنّ النُقطَةَ
# رَفَضَت. و‏GET عَلى المَسارِ نَفسِه ‏405: مَربوطٌ لِـPOST وَحدَه — فَالـ404 أَعلاه مِن النُقطَة (مَقيس: والمَسارُ المُخطوءُ ‏404 لِلفِعلَين).
get - "/$SLUG/shipping/oto/order-status" "$F"
check "$([ "$CODE" = 405 ] && echo 1 || echo 0)" \
    "GET /$SLUG/shipping/oto/order-status → $CODE (ضابِطٌ مُوجَب: المَسارُ مَربوطٌ لِـPOST — فَالـ404 أَعلاه رَفضُ النُقطَةِ لا غِيابُ المَسار)"
block "الجِدارُ الثاني: رَمزُ OTO غائِب — زِرُّ البوليصَةِ مَخفِيّ والشَحنُ يَدَوِيّ (‏DEPLOY.md §٢·ط)"
block "الجِدارُ الثالِث: مِفتاحُ البَوّابَةِ ومِفتاحُ الخِزانَةِ غائِبان — البِطاقَةُ لا تُعرَض (‏DEPLOY.md §٢·ح)"

# ═══ ٨) مُشرِفُ المَنَصَّةِ يَضبِطُ الباقَة، وسَطرُها في الاستوديو ════════
STEP="٨ — الباقَة"
say "٨ · /admin ← باقَةُ المَتجَر ← سَطرُها في الاستوديو"
if [ ! -s "$J_ADM" ]; then
    post "$J_ADM" /studio/auth/login --data-urlencode "phone=$ADMIN_PHONE"
    post "$J_ADM" /studio/auth/verify --data-urlencode method=phone --data-urlencode "phone=$ADMIN_PHONE" --data-urlencode "code=$DEV_CODE"
    check "$([ "$CODE" = 302 ] && [[ "$LOC" != *err=* ]] && echo 1 || echo 0)" "POST /studio/auth/verify (المُشرِف) → $CODE $LOC"
fi
get "$J_ADM" /admin "$F"
check "$([ "$CODE" = 200 ] && grep -q "href=\"/admin/tenants/$SLUG/plan\"" "$F" && echo 1 || echo 0)" \
    "GET /admin → $CODE · زِرُّ الباقَةِ عَلى بِطاقَةِ /$SLUG"
get "$J_ADM" "/admin/tenants/$SLUG/plan" "$F"
PLAN_LABEL="$(dec "$F" | grep -o '<option value="manual"[^>]*>[^<]*' | head -1 | sed 's/.*>//; s/[[:space:]]*$//')"
STARTS="$(date -u +%F)"; EXPIRES="$(date -u -d '+30 days' +%F)"
check "$([ "$CODE" = 200 ] && [ -n "$PLAN_LABEL" ] && echo 1 || echo 0)" "GET /admin/tenants/$SLUG/plan → $CODE · «$PLAN_LABEL»"
# السِعرُ قيمَةُ الاستِمارَةِ الافتِراضِيَّةُ (0) لا رَقَمٌ يُختَرَع (القاعِدَة ١٦)، والمُدَّةُ ‏30 يَوماً لِلفَحصِ وَحدَه.
# ‏(‏2026-10-25، brief-P1b-2 — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه): و«قيمَةُ الاستِمارَة» صارَت تُقرَأُ مِنها — المُهلَةُ والسِعرُ وتاريخُ البَدءِ
# قيَمُ حُقولِها كَما رُسِمَت (كانَت `grace=14 price=0` حَرفَينِ هُنا)، والبابُ بابُها؛ والمُشرِفُ يَختارُ «manual» ويَكتُبُ الانتِهاء.
PLAN_DUMP="$(form_dump "$F" 'plan/save"')"
PLAN_ACTION="$(fd_action "$PLAN_DUMP")"
GRACE="$(fd_get "$PLAN_DUMP" VALUE grace)"; PRICE="$(fd_get "$PLAN_DUMP" VALUE price)"; FORM_STARTS="$(fd_get "$PLAN_DUMP" VALUE starts)"
STARTS="${FORM_STARTS:-$STARTS}"
check "$([ -n "$PLAN_ACTION" ] && [ -n "$GRACE" ] && [ -n "$PRICE" ] && [ -n "$FORM_STARTS" ] && echo 1 || echo 0)" \
    "استِمارَةُ الباقَة إلى ${PLAN_ACTION:-غائِبَة} · قيَمُها: grace=${GRACE:-؟} price=${PRICE:-؟} starts=${FORM_STARTS:-؟} — والمَكتوبُ: plan=manual expires=$EXPIRES"
[ -n "$PLAN_ACTION" ] && [ -n "$GRACE" ] && [ -n "$PRICE" ] || die "استِمارَةُ الباقَةِ لا تُقرَأ"
post "$J_ADM" "$PLAN_ACTION" --data-urlencode plan=manual --data-urlencode "starts=$STARTS" \
    --data-urlencode "expires=$EXPIRES" --data-urlencode "grace=$GRACE" --data-urlencode "price=$PRICE"
check "$([ "$CODE" = 302 ] && [[ "$LOC" == *saved=1* ]] && echo 1 || echo 0)" "POST /admin/tenants/$SLUG/plan/save → $CODE $LOC"
LINE="$(ar studio.plan.active_line)"; LINE="${LINE/\{0\}/$PLAN_LABEL}"; LINE="${LINE/\{1\}/$EXPIRES}"
get "$OWNER_JAR" /studio "$F"
check "$([ "$CODE" = 200 ] && has "$F" "$LINE" && echo 1 || echo 0)" "GET /studio (المالِك) → $CODE · «$LINE»"

# ═══ ٩) المالِكُ يَمنَحُ نَفسَه إدارَةَ مَتجَرِه — عَدَدُ الأَفعال ════════
STEP="٩ — أَوَّلُ مُشرِف"
say "٩ · /studio/apps/$SLUG ← /$SLUG/login ← عَودَة ← «$(ar admin.tenant_users.grant)»"
ACTS=0
# نُصوصُ الرَوابِطِ مِن القامُوسِ كَسائِرِ الفُحوص — ‏(‏2026-10-13: كانَ السَطرُ يَطبَعُ «افتَح المَتجَر» و«المُستَخدِمون»
# وَصفاً مِن الذاكِرَة، ولا نَصَّ بِهِما في الصَفحَة؛ والمَفحوصُ كانَ العُنوانَينِ وَحدَهُما.)
PREVIEW="$(ar studio.app_nav.preview)"; USERS_LINK="$(ar studio.app.users_title)"; HAVE_ACCOUNT="$(ar roles.landing.have_account)"
get "$OWNER_JAR" "/studio/apps/$SLUG" "$F"
PREVIEW_TABS="$(count "$F" "href=\"/$SLUG\" target=\"_blank\"")"
check "$([ "$CODE" = 200 ] && grep -q "href=\"/$SLUG\"" "$F" && grep -q "href=\"/admin/tenants/$SLUG/users?from=studio\"" "$F" \
        && has "$F" "$PREVIEW" && has "$F" "$USERS_LINK" && echo 1 || echo 0)" \
    "GET /studio/apps/$SLUG → $CODE · «$PREVIEW» إلى /$SLUG و«$USERS_LINK» إلى …/users?from=studio"
get "$OWNER_JAR" "/$SLUG" "$F"; ACTS=$((ACTS + 1))
check "$([ "$CODE" = 200 ] && grep -q "href=\"/$SLUG/login\"" "$F" && has "$F" "$HAVE_ACCOUNT" && echo 1 || echo 0)" \
    "[فِعل $ACTS] GET /$SLUG → $CODE · «$HAVE_ACCOUNT» ورابِطُ /$SLUG/login"
get "$OWNER_JAR" "/$SLUG/login" "$F"; ACTS=$((ACTS + 1))
check "$([ "$CODE" = 200 ] && echo 1 || echo 0)" "[فِعل $ACTS] GET /$SLUG/login → $CODE"
store_login "$OWNER_JAR" "$SLUG" "$OWNER_PHONE"; ACTS=$((ACTS + 2))
info "[فِعل $((ACTS - 1))–$ACTS] الهاتِفُ ثُمَّ الرَمز → $LOC"
STUDIO_LINKS="$(get "$OWNER_JAR" "$LOC" "$F"; count "$F" 'href="/studio')"
# ‏(‏2026-10-24، S15 H(3) — ولا يُعادُ حَرفٌ فَوقَه): «$PREVIEW» يَفتَحُ لِساناً جَديداً (`target="_blank"` في `StudioApp.razor`)، فَالاستوديو
# باقٍ في لِسانِه: العَودَةُ تَبديلُ لِسانٍ لا زِرُّ رُجوع — والسَطرُ يَقولُها مِن الصَفحَةِ نَفسِها لا مِن الذاكِرَة.
info "رابِطُ عَودَةٍ إلى الاستوديو مِن $LOC: $STUDIO_LINKS — و«$PREVIEW» إلى /$SLUG يَفتَحُ لِساناً جَديداً (target=\"_blank\" = $PREVIEW_TABS)، فَالعَودَةُ تَبديلُ لِسانٍ إلى الاستوديو، لا نَقرَةٌ ولا زِرُّ رُجوع"
get "$OWNER_JAR" "/admin/tenants/$SLUG/users?from=studio" "$F"; ACTS=$((ACTS + 1))
# الصَفُّ الَّذي يَحمِلُ هاتِفَ المالِكِ واستِمارَتَه — كُتلَةً كُتلَة، لا أَوَّلُ استِمارَةٍ في الصَفحَة.
# ‏(2026-10-29، تَيّارُ S2 — ولا يُعادُ حَرفٌ فَوقَه): الهاتِفُ يُكتَبُ مُوَحَّداً `+9665…` مُنذُ `c5325e4` (‏`PhoneNumber.Normalize`)، والسكربتُ
# يَبحَثُ بِما كُتِبَ في الحَقل (`05…`) فَغابَ الصَفُّ وتَوَقَّفَت الجَولَةُ هُنا (مَقيسٌ عَلى `wasayel_s2_d40_20261029`: فُحِصَ 63 · فَشِلَ 1).
# فَالبَحثُ بِالصيغَةِ المُوَحَّدَةِ لِلرَقَمِ السُعودِيّ — ما سِواه يُبحَثُ بِحَرفِه.
e164_sa() { local d; d="$(printf '%s' "$1" | tr -cd '0-9')"; d="${d#00}"; d="${d#966}"; d="${d#0}"
    if [[ "$d" =~ ^5[0-9]{8}$ ]]; then printf '+966%s' "$d"; else printf '%s' "$1"; fi; }
OWN_ROW="$(dec "$F" | PH="$(e164_sa "$OWNER_PHONE")" perl -0777 -ne \
    'for my $b (split /class="acm-cat-row"/) { if (index($b, $ENV{PH}) >= 0 && $b =~ m{users/([0-9a-f-]{36})/(grant|revoke)-admin}) { print "$1 $2"; last } }')"
OWN_ID="${OWN_ROW%% *}"; OWN_ACT="${OWN_ROW##* }"
check "$([ "$CODE" = 200 ] && [ -n "$OWN_ROW" ] && echo 1 || echo 0)" \
    "[فِعل $ACTS] GET /admin/tenants/$SLUG/users?from=studio → $CODE · صَفُّ $OWNER_PHONE (${OWN_ACT:-غائِب})"
[ -n "$OWN_ROW" ] || die "صَفُّ المالِكِ غائِبٌ في شاشَةِ المُستَخدِمين"
if [ "$OWN_ACT" = grant ]; then
    post "$OWNER_JAR" "/admin/tenants/$SLUG/users/$OWN_ID/grant-admin"; ACTS=$((ACTS + 1))
    check "$([ "$CODE" = 302 ] && [[ "$LOC" == *saved=1* ]] && echo 1 || echo 0)" \
        "[فِعل $ACTS] POST …/users/$OWN_ID/grant-admin «$(ar admin.tenant_users.grant)» → $CODE $LOC"
else
    info "المالِكُ مُشرِفُ مَتجَرِه مِن جَولَةٍ سابِقَة (‏revoke-admin في صَفِّه) — لا مَنحَ ثانِياً"
fi
get "$OWNER_JAR" "/$SLUG/manage" "$F"
check "$([ "$CODE" = 200 ] && has "$F" "$(ar tenant.manage.subtitle)" && echo 1 || echo 0)" \
    "GET /$SLUG/manage (جَلسَةُ المالِكِ في مَتجَرِه) → $CODE · «$(ar tenant.manage.subtitle)»"
# ‏(‏2026-10-24، S15 H(1)): وفي فَرعِ «مُشرِفٌ سَلَفاً» لا عَدّ — كانَ يُطبَعُ العَدُّ وما مُنِحَ شَيء.
if [ "$OWN_ACT" = grant ]; then
    info "أَوَّلُ مُشرِفٍ لِمَتجَرٍ مَبنِيّ: $ACTS أَفعالٍ داخِلَ المُنتَج + عَودَةٌ واحِدَةٌ خارِجَه (تَبديلُ لِسان)"
else
    info "لا عَدّ — المالِكُ مُشرِفٌ سَلَفاً (عَدُّ الأَفعالِ في الجَولَةِ الأولى عَلى هذا المَتجَر)"
fi

# ═══ ١٠) الزائِرُ مَرَّةً ثانِيَة ════════════════════════════════════════
STEP="١٠ — الزائِرُ أَخيراً"
say "١٠ · الزائِرُ بِلا جَلسَة — والمَتجَرُ بِإعلانِه وباقَتِه"
get - "/$SLUG" "$F"
# ‏(‏2026-10-29، د34، ‏ADR-048 — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه) كانَ «$BROWSE» عَلى البَوّابَة؛ والآن الإعلانُ نَفسُه عَلى الجَذر — البِضاعَةُ أَوَّلاً.
check "$([ "$CODE" = 200 ] && grep -q "href=\"/$SLUG/listings/$LID\"" "$F" && ! has "$F" "$BROWSE" && echo 1 || echo 0)" \
    "GET /$SLUG (بِلا جَلسَة) → $CODE · رابِطُ الإعلانِ عَلى الجَذر — ولا «$BROWSE»"
get - "/$SLUG/explore" "$F"
check "$([ "$CODE" = 200 ] && grep -q "href=\"/$SLUG/listings/$LID\"" "$F" && echo 1 || echo 0)" \
    "GET /$SLUG/explore (بِلا جَلسَة) → $CODE · رابِطُ الإعلان"

# ═══ الخُلاصَة — العَدَدُ يُطبَع، والصِفرُ يُحمِر (القاعِدَة ١٠) ═════════
say "الخُلاصَة"
printf 'المَتجَر: %s/%s · الصَفقَة: %s · الإعلان: %s\n' "$BASE" "$SLUG" "$DID" "$LID"
printf '%s — فِئاتُ المَتجَر: %s\n' "$FLGAP" "${CATS_LINE:-—}"
printf 'فُحِصَ %s · فَشِلَ %s\n' "$CHECKED" "$FAILED"
if [ "$CHECKED" -eq 0 ]; then echo "✗ فَحصٌ أَعمى: لَم يُفحَص شَيء." >&2; exit 1; fi
[ "$FAILED" -eq 0 ] || exit 1
exit 0
