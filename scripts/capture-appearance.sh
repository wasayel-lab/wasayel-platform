#!/usr/bin/env bash
# ═══════════════════════════════════════════════════════════════════════
#  اِلتِقاط تَوصيف المَظهَر — أَساس المُقارَنَة بايتاً بِبايت
# ───────────────────────────────────────────────────────────────────────
#  يَلتَقِط HTML كامِلاً (بِكُلّ وُسوم <link> و<style> فيه) لِلصَفَحات
#  المَرجِعِيَّة مِن خادِم حَيّ، ويُطَبِّع **شَيئاً واحِداً فَقَط**:
#  تَعليق «Blazor-Server-Component-State» — وهو الوَحيد الَّذي يَتَغَيَّر
#  بَين طَلَبَين مُتَتالِيَين لِنَفس الصَفحَة (مُقاس، لا مُقَدَّر: طَلَبان
#  مُتَتالِيان لِـ /ashare يَختَلِفان في هذا السَطر وَحدَه).
#
#  ولِماذا لَقطات مَحفوظَة هُنا، والأَدوار قارَنَت مَسارَين؟ لِأَنّ
#  المَقيس هُنا **مُخرَج HTTP** لا دالَّة نَقِيَّة — لا يُوجَد «المَسار
#  القَديم» لِيُستَدعى بَعدَ التَّبديل. فَاللَقطَة هي الشَكل الوَحيد
#  المُتاح لِلبُرهان، وشَرطُها أَن تُؤخَذ **قَبل** أَيّ تَعديل — وهذا ما
#  يَفعَلُه هذا المِلَفّ في كوميت مُستَقِلّ سابِق.
#
#  الاستِعمال:
#     scripts/capture-appearance.sh [OUT_DIR] [BASE_URL]
#  الافتِراضيّ:
#     OUT_DIR  = tests/characterization/appearance/baseline
#     BASE_URL = http://localhost:5050
# ═══════════════════════════════════════════════════════════════════════
set -euo pipefail

OUT_DIR="${1:-tests/characterization/appearance/baseline}"
BASE_URL="${2:-http://localhost:5050}"

# الصَفَحات المَرجِعِيَّة: بَوّابَة ashare (مِقياس التَّكافُؤ الصِفريّ
# الحَيّ) ورَئيسِيّات أَدوارِه الثَّلاثَة واستِكشافُه، ثُمَّ بَوّابَة
# adwar-demo (ساحَة التَّجرِبَة).
#
# ‏2026-08-11 — أُضيفَ مُتَغايِرا الاستِكشاف (‏#7 و#8). والسَبَب مَقيس:
# مَوجَة تَرحيل النُصوص تَمَسّ `TenantExplore.razor`، و`/ashare/explore`
# وَحدَها **لا تُصَيِّر فَرعَين مِنها** — نافِذَة الفَلاتِر (‏لا تُفتَح
# إلّا بِـ`?filters=open`) وحالَة الفَراغ (‏لا تَقَع إلّا بِفَلتَر بِلا
# نَتائِج). فَبَوّابَةٌ تُقارِن الصَفحَة الافتِراضِيَّة وَحدَها كانَت
# سَتُعطي أَخضَرَ عَن ‏17 سِلسِلَة **لَم تُقارَن أَصلاً** — وهو عَينُ
# العَمى الَّذي يَحرُسُه عَدّاد القاعِدَة ١٠.
#
# والزِيادَة لا تُبطِل اللَقطَة القائِمَة: `compare-appearance.sh`
# يَدور على مِلَفّات **الأَساس**، فَما زادَ في الالتِقاط ولا أَساسَ لَه
# يُهمَل.
#
# ‏2026-08-15 — أُضيفَت خَمس صَفَحات (‏#9–#13) لِمَوجَة النُصوص الثانِيَة،
# وكُلٌّ مِنها **مَقيسَة قَبل أَن تُضاف** لا مَظنونَة:
#   · `/` — الهُبوط: ‏28 مِن 29 سِلسِلَة في `Landing.razor` تَقَع فيها،
#     وصِفرٌ مِنها كانَ يَقَع في الثَماني القائِمَة.
#   · `/ashare/legal` وفَرعاها — `LegalHub.razor` ثَلاثَة فُروع
#     حَصرِيَّة (شُروط/خُصوصِيَّة/استِرجاع) لا يُصَيَّر مِنها في الطَلَب
#     الواحِد إلّا واحِد. والثَلاثَة مَعاً: ‏24 مِن 24 — تَغطِيَة تامَّة.
#   · `/ashare/plans` — ‏6 مِن 9 في `Plans.razor`.
#
# ‏2026-08-15 (‏#14–#15) — و`theme-demo` لِلسَبَب الَّذي أَسقَطَ
# `TenantHome` مِن المَوجَة الأولى: `ashare` و`adwar-demo` كِلاهُما
# **بِأَدوار**، فَزائِرُهُما يَقَع على فَرع `RoleLanding` ولا يُصَيَّر
# الفَرع الافتِراضيّ (هيرو + فِئات + مُمَيَّز + أَحدَث) أَبَداً. و
# `theme-demo` بِلا أَدوار فَيُصَيِّرُه. والفَرق مَقيس: ‏12/34 → ‏24/34.
# و`?city=` يَفتَح فَرع الفَراغ المُوَجَّه (‏«لا إعلانات في … بَعد»)
# الَّذي لا يَقَع بِلا فَلتَر مَدينَة.
# والقِياس نَفسُه صُحِّحَ مَرَّةً: أَوَّل عَدّ بَحَثَ عَن السِلسِلَة
# **خاماً** في الصَفحَة فَأَعطى أَصفاراً كاذِبَة، لِأَنّ Razor يَكتُب
# مُخرَج التَعبير مُرَمَّزاً (‏`&#x627;`…) — فَنَصٌّ يُصَيَّر عَبر خاصِّيَّة
# مُكَوِّن لا يوجَد خاماً أَبَداً. العَدّ الصَحيح يَفُكّ التَرميز أَوَّلاً.
#
# ‏2026-08-15 (‏#16–#19) — صَفَحات **مَجهولَة عَمداً** تَحرُس فُروع
# الرَفض. `/admin` بِلا جَلسَة يُصَيِّر فَرع `NeedAuth` مِن
# `RequirePlatformAdmin`، و`/admin/tenants/…` بِلا جَلسَة يُصَيِّر فَرع
# `RequireTenantAdmin`، و`/studio/auth` بِمَرحَلَتَيه لا يُصَيَّر
# **إلّا** لِمَجهول (صاحِب الجَلسَة يُحَوَّل إلى `/studio`).
PAGES=(
  "ashare-portal:/ashare"
  "ashare-role-customer:/ashare/r/customer"
  "ashare-role-host:/ashare/r/host"
  "ashare-role-vendor:/ashare/r/vendor"
  "ashare-explore:/ashare/explore"
  "adwar-demo-portal:/adwar-demo"
  "ashare-explore-filters:/ashare/explore?filters=open"
  "ashare-explore-empty:/ashare/explore?category=__none__"
  "landing:/"
  "ashare-legal-terms:/ashare/legal"
  "ashare-legal-privacy:/ashare/legal/privacy"
  "ashare-legal-returns:/ashare/legal/returns"
  "ashare-plans:/ashare/plans"
  "theme-demo-portal:/theme-demo"
  "theme-demo-city:/theme-demo?city=Riyadh"
  "admin-anon:/admin"
  "admin-tenant-anon:/admin/tenants/theme-demo/theme"
  "studio-auth:/studio/auth"
  # الرَقم هُنا **عَرضٌ لا مِفتاح**: الصَفحَة تَطبَعُه في «أُرسِلَ رَمز
  # إلى …»، فَهو يَدخُل بايتات الأَساس ويَجِب أَن يَكون ثابِتاً. ولِذلك
  # هو رَقم عَيِّنَة لا يُطابِق أَيّ مِلَفّ جَلسَة — وحَتّى لَو طابَقَ،
  # فَالطَلَب هُنا `GET` لا يُنشِئ جَلسَةً ولا مُستَخدِماً.
  "studio-auth-verify:/studio/auth?stage=verify&phone=0512345678&err=code"
  # ‏2026-08-16 (‏#20–#21) — المَوجَة الثامِنَة: **البابُ الأَوَّل**.
  #
  # ‏`TenantListingDetail` (‏105 سِلسِلَة) كانَ أَثقَلَ مِلَفّ في السِجِلّ
  # وخارِجَ كُلّ لَقطَة، لِسَبَبَين قيسا في المَوجَة الثالِثَة: صَفَحات
  # تَفصيل إعلانات `ashare` مَمنوعَة، ومُستَأجِرو التَجرِبَة **ظُنّوا**
  # بِصِفر إعلان. والقِياسُ اليَومَ يَرُدّ النِصفَ الثاني: `ejar` فيه
  # ‏15 إعلاناً، **وأَوَّلُها مُوَثَّق ومُمَيَّز بِسِمات كامِلَة** —
  # يَبثُّها `PlatformSeed` عَبر `ListingFlagsSet`. فَما كانَ ناقِصاً
  # بَياناتٍ لَم يَكُن ناقِصاً أَصلاً؛ كانَ ناقِصاً **سُؤالاً**.
  #
  # والصَفحَةُ صارَت قِراءَةً خالِصَة بِالمَوجَة السابِعَة (خَرَجَ
  # `ListingViewed` مِن `GET` إلى `POST …/view` بَعدَ التَصيير، ولا
  # يُطلِقُه `curl`)، فَطَلَبانِ مُتَتالِيانِ يُعطِيانِ نَفسَ البايتات —
  # قيسَ قَبلَ الإضافَة، لا مَظنوناً.
  #
  # ‏`45b17b1e…` هو **الإعلان المُوَثَّق المُمَيَّز** في `ejar` (شَقَّة،
  # ‏350,000، سِمات إيجار كامِلَة): يُصَيِّر شارَتَي الثِقَة، وتَحذيرَ
  # الاحتِيال، وشَبَكَةَ `DetailGridKeys`، والسِمات المَحلولَة، وفَرعَ
  # «بِلا سَلَّة» لِزائِر — أَي أَغلَبَ المِلَفّ في صَفحَة واحِدَة.
  # و`94877b94…` في `theme-demo` **مَفتوحٌ لِلعُروض**
  # (`accepts_offers=true`) فَيُصَيِّر قِسمَ العُروض وفَرعَ «دُخول
  # لِتَقديم عَرض» الَّذي لا يَقَع في الأَوَّل أَبَداً.
  #
  # ⚠ **وعَدّادُ المُشاهَدَة يَجعَل هاتَينِ الصَفحَتَينِ حَسّاسَتَين
  # لِلطَبَقَة السادِسَة**: المَوجَةُ السابِعَة أَخرَجَت `ListingViewed`
  # مِن `GET` إلى `POST …/view` يُطلِقُه **JS بَعدَ التَصيير**.
  # و`curl` لا يُنَفِّذ JS فَلا يُحَرِّك العَدّاد — لكِنّ
  # `verify-runtime.cs` مُتَصَفِّحٌ حَقيقيّ **يُنَفِّذُه**. فَكُلُّ
  # مَسحٍ لِلطَبَقَة السادِسَة عَلى عُنوان تَفصيلِ إعلانٍ **يَزيد
  # العَدّاد**، فَتَحمَرّ `ejar-listing.html` و`owner-app-listings.html`
  # بِسَطرٍ واحِد. وقَد وَقَعَ ‏2026-08-16 (‏0 ← 4). العِلاجُ إعادَةُ
  # تَثبيتٍ مُعلَنَة بَعدَ حَصر الفَرق، لا تَطبيعُ العَدّاد — فَتَطبيعُه
  # يُخفي انحِداراً حَقيقِيّاً في عَدّادٍ لَه أَربَعَةُ مُستَهلِكين.
  "ejar-listing:/ejar/listings/45b17b1e-1997-4fcc-b3ce-051ef484fb16"
  "theme-demo-listing-offers:/theme-demo/listings/94877b94-abb5-4a8b-8494-be8ef678cc59"
)

# ═══════════════════════════════════════════════════════════════════════
#  الصَفَحات المَحروسَة بِجَلسَة — ‏2026-08-15، المَوجَة الثالِثَة
# ───────────────────────────────────────────────────────────────────────
#  التَقدير في `docs/I18N.md` §٨ قالَ إنّ الباقي «أَغلَبُه Studio* و
#  Admin/* وMy* — مَحروسٌ بِجَلسَة، لا تُصَيَّر لِزائِر، فَلا تَبلُغُها
#  لَقطَة capture-appearance.sh كَما هي اليَوم». وهذا يَرفَع الأَمر إلى
#  أَحَد طَريقَين: لَقطَة بِجَلسَة، أَو الاكتِفاء بِالمُقابَلَة
#  الحَرفِيَّة مَع HEAD. **والأَوَّل أَغلى مَرَّةً وأَرخَص خَمس مَوجات**،
#  فَنُفِّذَ هُنا.
#
#  ثَلاثَة مِلَفّات تَعريف، وكُلٌّ يُسَجَّل دُخولُه **مَرَّةً واحِدَة**:
#
#    admin  — مُشرِف مَنصَّة. جَلسَة studio لِهاتِف مَنَحَه
#             `PlatformAdminSeeder` عَبر `PLATFORM_ADMIN_PHONE`.
#             يَفتَح `RequirePlatformAdmin`.
#    studio — صاحِب استوديو عاديّ (لَيسَ مُشرِف مَنصَّة). يَفتَح شِلّ
#             `/studio`، ويُصَيِّر فَرع `Forbidden` في `/admin` —
#             وهُما فَرعان لا يَبلُغُهُما لا الزائِر ولا المُشرِف.
#    user   — مُستَخدِم مَتجَر. جَلسَة نَفاذ (‏MockNafath) في مُستَأجِر
#             تَجرِبَة. يَفتَح `/{slug}/me/*`.
#
#  **ولا سِرّ في المُستَودَع**: الهَواتِف والهُوِيَّة والرَمز كُلُّها
#  مُتَغَيِّرات بيئَة. وغِيابُها **لا يُنتِج لَقطَةً مَجهولَة صامِتَة** —
#  الصَفَحات المَحروسَة تُتَخَطّى بِصَوت عالٍ، فَتَخرُج
#  `compare-appearance.sh` حَمراءَ عَلى غيابِ طَرَفِها. الخَطَأ الَّذي
#  لا يُغتَفَر هُنا هو أَن يُكتَب مِلَفّ أَساسٍ فيه صَفحَةُ «سَجِّل
#  دُخولَك» بِاسم صَفحَةٍ مَحروسَة — فَتُصادِق البَوّابَة على شَيء لَم
#  تَرَه قَطّ.
#
#  المُتَغَيِّرات:
#    WSL_CAPTURE_ADMIN_PHONE   — هاتِف مُشرِف المَنصَّة (= PLATFORM_ADMIN_PHONE)
#    WSL_CAPTURE_STUDIO_PHONE  — هاتِف صاحِب استوديو عاديّ
#    WSL_CAPTURE_USER_NID      — هُوِيَّة نَفاذ (‏10 أَرقام)
#    WSL_CAPTURE_USER_TENANT   — مُستَأجِر التَجرِبَة (افتِراضيّ theme-demo)
#    WSL_CAPTURE_DEV_CODE      — رَمز OTP التَطويريّ (افتِراضيّ 123456،
#                                وهو ثابِتٌ مَكتوب في `StudioAuth.DevCode`
#                                فَلَيسَ سِرّاً — والمُتَغَيِّر لِيَتَبَدَّل
#                                إن تَبَدَّلَ المَصدَر لا لِيُخفى)
GUARDED_PAGES=(
  "admin-home:admin:/admin"
  "admin-tenant-new:admin:/admin/tenants/new"
  "admin-monitor:admin:/admin/monitor"
  "admin-incubator:admin:/admin/incubator"
  "admin-fixtures:admin:/admin/fixtures"
  "admin-audit:admin:/admin/audit"
  "admin-tenant-theme:admin:/admin/tenants/theme-demo/theme"
  "admin-forbidden:studio:/admin"
  "studio-home:studio:/studio"
  "studio-billing:studio:/studio/billing"
  "studio-new:studio:/studio/new"
  "studio-consent:studio:/studio/consent"
  "studio-upgrade-analyze:studio:/studio?upgrade=analyze"
  "studio-upgrade-refine:studio:/studio?upgrade=refine"
  "studio-upgrade-stores:studio:/studio?upgrade=stores"
  "studio-upgrade-feature:studio:/studio?upgrade=feature"
  "user-searches:user:/{tenant}/me/searches"
  "user-notifications:user:/{tenant}/notifications"

  # ═══ المَوجَة الثامِنَة — ‏2026-08-16 ═══════════════════════════════
  #
  # ‏**البابُ الثاني: صَفَحات `/studio/apps/{slug}/*`** (‏≈230 سِلسِلَة).
  # قالَت المَوجَة الثالِثَة إنَّها «تَشتَرِط مِلكِيَّةَ مُستَأجِر لا
  # مُجَرَّدَ جَلسَة»، وإنّ الطَريقَينِ كِلَيهِما قَرارُ مالِك: مُستَأجِرٌ
  # جَديد يُنشِئُه مِلَفُّ الالتِقاط (‏فَيَظهَر في صَفحَة الهُبوط
  # ويُبَدِّل لَقطَةَ أَساسٍ قائِمَة)، أَو نَقلُ مِلكِيَّة.
  #
  # **والقِياسُ أَسقَطَ السُؤالَ كُلَّه**: مِلَفُّ `admin` (‏هاتِفُ
  # `PLATFORM_ADMIN_PHONE`) **يَملِك `owner-test` بِالفِعل** —
  # ‏`StudioOwnershipSeeder` أَسنَدَ المُستَأجِرَ اليَتيمَ إلَيه عِندَ
  # إقلاعٍ سابِق. فَلا مُستَأجِرَ يُنشَأ، ولا مِلكِيَّةَ تُنقَل، ولا
  # بايتَ يَتَغَيَّر في `landing.html`. القاعِدَة ١٤ حَرفاً: قَبلَ
  # تَوجيه العَمَل نَحوَ آلِيَّةٍ جَديدَة، **اِبحَث هَل يَفعَلُها
  # المُستَودَع أَصلاً**.
  #
  # ولِماذا مِلَفُّ `admin` لا `studio`: الحارِسُ هُنا
  # (`StudioOwnsAsync`) يَسأَل عَن **المِلكِيَّة** لا عَن صَلاحِيَّة
  # المَنصَّة، فَأَيُّ المِلَفَّينِ مَلَكَ فَتَحَ. و`studio` صِفرُ
  # تَطبيق — ولِذلك بِالضَبط تَبقى `studio-home` كَما هي، فَلا يُمَسّ
  # أَساسٌ قائِم.
  "studio-app:admin:/studio/apps/owner-test"
  "studio-app-branding:admin:/studio/apps/owner-test/branding"
  "studio-app-categories:admin:/studio/apps/owner-test/categories"
  "studio-app-roles:admin:/studio/apps/owner-test/roles"
  "studio-app-regions:admin:/studio/apps/owner-test/regions"
  "studio-app-attributes:admin:/studio/apps/owner-test/attributes"
  "studio-app-pwa:admin:/studio/apps/owner-test/pwa"
  "studio-app-listings:admin:/studio/apps/owner-test/listings"
  "studio-app-deals:admin:/studio/apps/owner-test/deals"
  "studio-app-tickets:admin:/studio/apps/owner-test/tickets"
  "studio-app-console:admin:/studio/apps/owner-test/console"
  # فَرعُ «التَذكِرَة غَير مَوجودَة» — ‏`owner-test` بِلا تَذكِرَة
  # قابِلَة لِلقِراءَة، فَالمُعَرَّفُ ثابِتٌ لا يُطابِق شَيئاً. وهو
  # فَرعٌ **يُصَيَّر فِعلاً** ولا يَبلُغُه غَيرُ المالِك، فَمَكانُه هُنا.
  "studio-app-ticket-missing:admin:/studio/apps/owner-test/tickets/00000000-0000-0000-0000-000000000001"

  # ‏`TenantListingDetail` بِجَلسَة — فَرعانِ لا يَقَعانِ لِزائِر:
  #   · `user-listing-own`    — الإعلانُ مِلكُ صاحِبِ الجَلسَة
  #     (`owner_id` = هُوِيَّةُ مِلَفّ `user`)، فَيَظهَر مَدخَلُ
  #     التَحرير وتُخفى المُفَضَّلَةُ وقِسمُ التَواصُل.
  #   · `user-listing-offers` — إعلانٌ مَفتوحٌ لِلعُروض **لِغَيرِ
  #     مالِكِه**، فَيُصَيَّر نَموذَجُ تَقديم العَرض كامِلاً.
  "user-listing-own:user:/{tenant}/listings/ff8e1748-b98a-4ad0-9da0-10ac616eaf9e"
  "user-listing-offers:user:/{tenant}/listings/94877b94-abb5-4a8b-8494-be8ef678cc59"

  # ── تَوأَمُ الاستوديو الإداريّ: `/admin/tenants/{slug}/*` ──────────
  # ‏`AdminStudioPairCharacterizationTests` يُثَبِّت أَنّ هذِه الشاشات
  # **نَفسُ شاشات `/studio/apps/{slug}/*` لِجُمهورَين**. وحارِسُها
  # `TenantAdminGuard` يَشتَرِط مِلكِيَّةَ المُستَأجِر لا صَلاحِيَّةَ
  # المَنصَّة — ومِلَفُّ `admin` يَملِك `owner-test`، فَبَلَغَتها
  # بِلا بَيانٍ جَديد.
  "admin-tenant-edit:admin:/admin/tenants/owner-test"
  "admin-tenant-categories:admin:/admin/tenants/owner-test/categories"
  "admin-tenant-roles:admin:/admin/tenants/owner-test/roles"
  "admin-tenant-regions:admin:/admin/tenants/owner-test/regions"
  "admin-tenant-pwa:admin:/admin/tenants/owner-test/pwa"
  "admin-tenant-branding:admin:/admin/tenants/owner-test/branding"
  "admin-tenant-attributes:admin:/admin/tenants/owner-test/attributes"

  # ── مِلَفُّ `owner`: نَفسُ الشاشات على مُستَأجِرٍ **مَملوء** ────────
  # ‏`ejar` فيه ‏15 إعلاناً وصَفقَتانِ ومُستَخدِمون، فَتُصَيَّر أَجسامُ
  # الحَلَقات الَّتي لا يَبلُغُها `owner-test` الفارِغ. والزَوجُ
  # (فارِغ/مَملوء) يَحرُس الفَرعَين مَعاً بَدَل واحِد.
  "owner-app-listings:owner:/studio/apps/ejar/listings"
  "owner-app-deals:owner:/studio/apps/ejar/deals"
  "owner-app-deal-detail:owner:/studio/apps/ejar/deals/17497544-0a6a-485d-bc72-f5d738995169"
  "owner-app-console:owner:/studio/apps/ejar/console"
  "owner-app-attributes:owner:/studio/apps/ejar/attributes"
  "owner-app-pwa:owner:/studio/apps/ejar/pwa"
  "owner-app-home:owner:/studio/apps/ejar"
)

# أَوراق الأَنماط الَّتي تُحَمِّلها كُلّ صَفحَة — المَظهَر = HTML + CSS،
# فَالتَّوصيف يَلزَمُه الطَّرَفان. (وهذه هي النُسخَة الَّتي تُثبِت لاحِقاً
# أَنّ قيمَة كُلّ رَمز مَبثوث تُساوي الحَرفِيَّة الَّتي حَلَّ مَحَلَّها.)
SHEETS=(
  "widgets.css:/_content/ACommerce.Templates.Customer.Marketplace/css/widgets.css"
  "templates-shared.css:/_content/ACommerce.Templates.Customer.Marketplace/css/templates-shared.css"
  "templates-marketplace.css:/_content/ACommerce.Templates.Customer.Marketplace/css/templates-marketplace.css"
  "app.css:/_content/ACommerce.Templates.Customer.Marketplace/css/app.css"
  "site.css:/_content/ACommerce.Templates.Customer.Marketplace/css/site.css"
  "studio.css:/_content/ACommerce.Templates.Customer.Marketplace/css/studio.css"
  "premium.css:/_content/ACommerce.Templates.Customer.Marketplace/css/premium.css"
  "branding-ashare.css:/branding/ashare.css"
)

DEV_CODE="${WSL_CAPTURE_DEV_CODE:-123456}"
USER_TENANT="${WSL_CAPTURE_USER_TENANT:-theme-demo}"

JARS="$(mktemp -d)"
trap 'rm -rf "$JARS"' EXIT

mkdir -p "$OUT_DIR" "$OUT_DIR/css"

# ── التَّطبيع: اثنان، وكِلاهُما مَقيس لا مُقَدَّر ────────────────────
# ‏(١) تَعليق `Blazor-Server-Component-State` — الوَحيد الَّذي يَتَغَيَّر
#     بَين طَلَبَين مُتَتالِيَين لِصَفحَة **بِلا جَلسَة**.
#
# ‏(٢) `window.acRealtimeToken` — ‏2026-08-15، أُضيفَ مَعَ الصَفَحات
#     المَحروسَة، وهو **اكتِشافُ الأَداةِ عَطَبَ نَفسِها**: أَوَّل
#     لَقطَتَين مُتَتالِيَتَين تَطابَقَتا في ‏35 مِلَفّاً واختَلَفَتا في
#     صَفحَتَي المُستَخدِم — والسَطر المُختَلِف واحِد.
#
#     والسَبَب أَخطَرُ مِن الاختِلاف: `App.razor` يَطبَع `@@Auth.Token`
#     في المُستَند لِـSignalR، أَي أَنّ **توكِن الجَلسَة نَفسَه** كانَ
#     سَيُكتَب في لَقطَة أَساس تُودَع في Git. فَالتَطبيع هُنا يَقتُل
#     عُصفورَين: يُثَبِّت الأَداةَ، ويُنَفِّذ شَرطَ «الناتِج لا يَحوي
#     كوكِيّاً» بَدَل أَن يَعِد بِه.
#
#     والنَمَط يَشتَرِط **مَحرَفاً واحِداً على الأَقَلّ** بَينَ العَلامَتَين:
#     الصَفحَة المَجهولَة تَطبَع `''` فَلا تُمَسّ — ولَولا هذا الشَرط
#     لَاحمَرَّت لَقطات المَوجَتَين الأولى والثانِيَة كُلُّها بِلا سَبَب.
normalize() {
  sed -E -e 's/Blazor-Server-Component-State:[A-Za-z0-9+\/=_-]+/Blazor-Server-Component-State:NORMALIZED/g' \
         -e "s/(window\.acRealtimeToken = ')[^']+'/\1NORMALIZED'/g" \
      "$1" > "$2"
}

# ── تَسجيل دُخول studio (‏admin و studio) ────────────────────────────
# نُقطَتان: `/studio/auth/login` تَنقُل إلى مَرحَلَة الرَمز بِلا أَثَر،
# و`/studio/auth/verify` هي الَّتي تَكتُب الكوكي. نَستَدعي الثانِيَة
# مُباشَرَةً — الأولى لا حالَةَ لَها تُحفَظ (مَقيس في المَصدَر:
# تُعيد Redirect فَقَط).
studio_login() {
  local jar="$1" phone="$2"
  curl -s -c "$jar" -o /dev/null \
       -X POST -d "phone=$phone" -d "code=$DEV_CODE" \
       "$BASE_URL/studio/auth/verify"
  has_cookie "$jar" ".acommerce.studio"
}

# اسم الكوكي في الحَقل السادِس مِن صيغَة Netscape. الفَحص بِالحَقل لا
# بِالسَطر: `.acommerce.studio.name` يَحمِل الاسم لا التوكِن، فَمُطابَقَة
# جُزئيَّة كانَت سَتَقبَل جَرَّةً بِلا جَلسَة.
has_cookie() {
  awk -F'\t' -v want="$2" 'NF>=7 && $6==want {found=1} END{exit !found}' "$1" 2>/dev/null
}

# ── تَسجيل دُخول مُستَخدِم مَتجَر عَبر نَفاذ الوَهميّ ────────────────
# ‏MockNafath يوافِق تِلقائيّاً بَعد `AutoApproveSeconds` (‏5 اليَوم)،
# والمُحاوَلَة تَنتَهي بَعد دَقيقَتَين — فَالانتِظار داخِل النافِذَة.
nafath_login() {
  local jar="$1" tenant="$2" nid="$3"
  local loc attempt
  loc=$(curl -s -o /dev/null -w '%{redirect_url}' \
             -X POST -d "nid=$nid" "$BASE_URL/$tenant/auth/nafath/login")
  attempt=$(printf '%s' "$loc" | perl -ne 'print $1 if /attempt=([0-9a-f]+)/')
  [ -n "$attempt" ] || return 1
  sleep 6
  curl -s -c "$jar" -o /dev/null \
       -X POST -d "nid=$nid" -d "attempt=$attempt" \
       "$BASE_URL/$tenant/auth/nafath/verify"
  has_cookie "$jar" ".acommerce.auth.$tenant"
}

# ── تَهيِئَة المِلَفّات المُتاحَة ─────────────────────────────────────
declare -A PROFILE_JAR=()
PROFILES_READY=""
PROFILES_MISSING=""

if [ -n "${WSL_CAPTURE_ADMIN_PHONE:-}" ]; then
  if studio_login "$JARS/admin" "$WSL_CAPTURE_ADMIN_PHONE"; then
    PROFILE_JAR[admin]="$JARS/admin"; PROFILES_READY="$PROFILES_READY admin"
  else
    echo "✗ فَشِلَ دُخول مِلَفّ 'admin' — لا كوكي studio." >&2; exit 1
  fi
else
  PROFILES_MISSING="$PROFILES_MISSING admin(WSL_CAPTURE_ADMIN_PHONE)"
fi

if [ -n "${WSL_CAPTURE_STUDIO_PHONE:-}" ]; then
  if studio_login "$JARS/studio" "$WSL_CAPTURE_STUDIO_PHONE"; then
    PROFILE_JAR[studio]="$JARS/studio"; PROFILES_READY="$PROFILES_READY studio"
  else
    echo "✗ فَشِلَ دُخول مِلَفّ 'studio' — لا كوكي studio." >&2; exit 1
  fi
else
  PROFILES_MISSING="$PROFILES_MISSING studio(WSL_CAPTURE_STUDIO_PHONE)"
fi

# ‏2026-08-16 — مِلَفٌّ رابِع: صاحِبُ استوديو **يَملِك مُستَأجِراً
# مَملوءاً**. والحاجَةُ إلَيه مَقيسَة لا مَظنونَة: مِلَفُّ `admin` يَملِك
# `owner-test` وَحدَه، وهو بِصِفر إعلان وصِفر صَفقَة — فَجِسمُ
# `@foreach` في `StudioAppListings` و`StudioAppDeals` **لا يُصَيَّر**،
# وحالَةُ الفَراغ وَحدَها تُحرَس. أَي أَنّ ثُلُثَي المِلَفَّين كانا
# سَيُرَحَّلان بِبَوّابَةٍ عَمياءَ عَنهُما (‏القاعِدَة ١٠).
if [ -n "${WSL_CAPTURE_OWNER_PHONE:-}" ]; then
  if studio_login "$JARS/owner" "$WSL_CAPTURE_OWNER_PHONE"; then
    PROFILE_JAR[owner]="$JARS/owner"; PROFILES_READY="$PROFILES_READY owner"
  else
    echo "✗ فَشِلَ دُخول مِلَفّ 'owner' — لا كوكي studio." >&2; exit 1
  fi
else
  PROFILES_MISSING="$PROFILES_MISSING owner(WSL_CAPTURE_OWNER_PHONE)"
fi

if [ -n "${WSL_CAPTURE_USER_NID:-}" ]; then
  if nafath_login "$JARS/user" "$USER_TENANT" "$WSL_CAPTURE_USER_NID"; then
    PROFILE_JAR[user]="$JARS/user"; PROFILES_READY="$PROFILES_READY user"
  else
    echo "✗ فَشِلَ دُخول مِلَفّ 'user' — لا كوكي $USER_TENANT." >&2; exit 1
  fi
else
  PROFILES_MISSING="$PROFILES_MISSING user(WSL_CAPTURE_USER_NID)"
fi

for entry in "${PAGES[@]}"; do
  name="${entry%%:*}"
  path="${entry#*:}"
  code=$(curl -s -o "$OUT_DIR/$name.raw" -w '%{http_code}' "$BASE_URL$path")
  if [ "$code" != "200" ]; then
    echo "✗ $path أَعطى $code — الالتِقاط مُتَوَقِّف." >&2
    exit 1
  fi
  normalize "$OUT_DIR/$name.raw" "$OUT_DIR/$name.html"
  rm -f "$OUT_DIR/$name.raw"
  echo "✓ $path → $OUT_DIR/$name.html  ($(wc -c < "$OUT_DIR/$name.html") بايت)"
done

# ── الصَفَحات المَحروسَة ─────────────────────────────────────────────
# **بُرهان أَنّ الجَلسَة عَمِلَت، لِكُلّ صَفحَة على حِدَة**: تُجلَب
# مَرَّتَين — بِالجَلسَة وبِلا جَلسَة — ويُشتَرَط أَن تَختَلِفا. وهذا
# المِعيار عامّ وقابِل لِلفَحص بِلا قائِمَة عَلامات نَصِّيَّة تَنجَرِف:
# صَفحَةٌ مَحروسَة تُعطي المَجهولَ شَيئاً آخَر بِالضَرورَة، فَإن
# تَساوَت النُسخَتان فَإمّا أَنّ الجَلسَة لَم تَصِل (‏كوكي ساقِط،
# هاتِف خاطِئ) وإمّا أَنّ الصَفحَة **لَيسَت مَحروسَة أَصلاً** فَمَكانُها
# `PAGES`. وكِلتا الحالَتَين خَطَأ يَجِب أَن يُصرَخ بِه لا أَن يُكتَب
# مِلَفَّ أَساس.
guarded_ok=0
guarded_skipped=0
for entry in "${GUARDED_PAGES[@]}"; do
  name="${entry%%:*}"; rest="${entry#*:}"
  profile="${rest%%:*}"; path="${rest#*:}"
  path="${path//\{tenant\}/$USER_TENANT}"

  jar="${PROFILE_JAR[$profile]:-}"
  if [ -z "$jar" ]; then
    # إلى `stderr` عَمداً: `compare-appearance.sh` يَبتَلِع `stdout`
    # الالتِقاط، فَتَحذيرٌ مَكتوب هُناك لا يَراه أَحَد.
    echo "⚠ $path — مِلَفّ '$profile' غَير مُهَيَّأ، تُخُطِّيَت (لا تُكتَب مَجهولَةً)." >&2
    guarded_skipped=$((guarded_skipped + 1))
    continue
  fi

  code=$(curl -s -b "$jar" -o "$OUT_DIR/$name.raw" -w '%{http_code}' "$BASE_URL$path")
  if [ "$code" != "200" ]; then
    echo "✗ $path ($profile) أَعطى $code — الالتِقاط مُتَوَقِّف." >&2
    exit 1
  fi
  curl -s -o "$JARS/$name.anon" "$BASE_URL$path"
  if cmp -s "$OUT_DIR/$name.raw" "$JARS/$name.anon"; then
    echo "✗ $path ($profile) — الصَفحَة بِالجَلسَة تُساوي الصَفحَة بِلا جَلسَة." >&2
    echo "   إمّا أَنّ الجَلسَة لَم تَصِل، وإمّا أَنّ الصَفحَة غَير مَحروسَة." >&2
    exit 1
  fi
  normalize "$OUT_DIR/$name.raw" "$OUT_DIR/$name.html"
  rm -f "$OUT_DIR/$name.raw"
  guarded_ok=$((guarded_ok + 1))
  echo "✓ $path ($profile) → $OUT_DIR/$name.html  ($(wc -c < "$OUT_DIR/$name.html") بايت)"
done

# ── لا كوكي في المُخرَج ──────────────────────────────────────────────
# الشَرط مَكتوب في التَكليف، **ويُفحَص لا يُوعَد بِه**: قيمَة كُلّ توكِن
# مُسَجَّل تُقرَأ مِن الجَرَّة وتُبحَث في كُلّ مِلَفّ مَكتوب.
#
# وبِشَكلَيها: الجَرَّة تَحفَظ القيمَة كَما جاءَت في التَرويسَة (‏`%3D`
# لِـ`=`)، والصَفحَة تَطبَعُها مَفكوكَةَ التَرميز. فَفَحصُ الشَكل الخام
# وَحدَه كانَ **عَمىً مَقيساً**: التوكِن كانَ في `user-*.html` والفَحصُ
# أَخضَر، ولَم يُكشَف إلّا حينَ اختَلَفَت لَقطَتان مُتَتالِيَتان.
url_decode() { perl -pe 's/%([0-9A-Fa-f]{2})/chr(hex($1))/ge' ; }

for p in "${!PROFILE_JAR[@]}"; do
  while read -r tok; do
    [ ${#tok} -lt 12 ] && continue
    plain=$(printf '%s' "$tok" | url_decode)
    for form in "$tok" "$plain"; do
      if grep -rqF "$form" "$OUT_DIR" 2>/dev/null; then
        echo "✗ تَسَرَّبَ توكِن جَلسَة '$p' إلى لَقطَة الأَساس — الالتِقاط مُتَوَقِّف." >&2
        exit 1
      fi
    done
  done < <(awk -F'\t' 'NF>=7 && $1 !~ /^#/ {print $7}' "${PROFILE_JAR[$p]}")
done

for entry in "${SHEETS[@]}"; do
  name="${entry%%:*}"
  path="${entry#*:}"
  code=$(curl -s -o "$OUT_DIR/css/$name" -w '%{http_code}' "$BASE_URL$path")
  if [ "$code" != "200" ]; then
    echo "✗ $path أَعطى $code — الالتِقاط مُتَوَقِّف." >&2
    exit 1
  fi
  echo "✓ $path → $OUT_DIR/css/$name  ($(wc -c < "$OUT_DIR/css/$name") بايت)"
done

echo "اِلتُقِطَت ${#PAGES[@]} صَفَحات عامَّة و$guarded_ok مَحروسَة و${#SHEETS[@]} أَوراق أَنماط في $OUT_DIR"
[ -n "$PROFILES_READY" ] && echo "المِلَفّات المُهَيَّأَة:$PROFILES_READY"
if [ "$guarded_skipped" -gt 0 ]; then
  echo "⚠ تُخُطِّيَت $guarded_skipped صَفحَة مَحروسَة — الناقِص:$PROFILES_MISSING" >&2
fi
exit 0
