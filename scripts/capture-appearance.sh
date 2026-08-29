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

  # ‏2026-08-16 (‏#22–#32) — المَوجَة التاسِعَة: **فُروعُ «المَجهول» في
  # صَفَحات العُضو**.
  #
  # صَفَحاتُ المُستَخدِم (`MyDealDetail`، `CreateListing`، `CheckoutPage`،
  # `CartPage`…) تَبدَأ كُلُّها بِفَرع `!Auth.IsAuthenticated` يُصَيِّر
  # `AcEmptyState` بِعُنوانٍ وزِرٍّ عَرَبِيَّين. وهذا الفَرع **لا تَبلُغُه
  # جَلسَة** بِالبِنيَة — فَمَكانُه هُنا لا في `GUARDED_PAGES`، وهو
  # الطَرَفُ المُقابِل لِلصَفحَة المَحروسَة نَفسِها.
  #
  # والقِياسُ قَبلَ الإضافَة: هذِه الفُروعُ وَحدَها تَحرُس ‏‎2–3 سَلاسِل
  # لِكُلّ مِلَفّ، وهي السَلاسِلُ الَّتي لا يُصَيِّرُها أَيُّ مِلَفّ
  # جَلسَة مَهما بَلَغ.
  "ejar-deal-anon:/ejar/deals/17497544-0a6a-485d-bc72-f5d738995169"
  "ejar-deals-anon:/ejar/deals"
  "ejar-create-listing-anon:/ejar/create-listing"
  "ejar-checkout-anon:/ejar/checkout"
  "ejar-cart-anon:/ejar/cart"
  "ejar-favorites-anon:/ejar/favorites"
  "ejar-chats-anon:/ejar/chats"
  "ejar-offers-anon:/ejar/me/offers"
  "ejar-listings-anon:/ejar/me/listings"
  "ejar-role-anon:/ejar/me/role"
  "theme-demo-manage-anon:/theme-demo/manage"

  # ‏2026-08-16 (‏#33–#48) — المَوجَة العاشِرَة: **بابُ الدُخول**.
  #
  # ‏`Login` (‏14) و`PhoneLoginForm` (‏7) و`NafathLoginForm` (‏9) —
  # ‏30 سِلسِلَة، وكُلُّها كانَت خارِجَ اللَقطَة رَغمَ أَنّ الصَفَحات
  # **عامَّة بِالكامِل**. والسَبَب أَنّ الفُروعَ تُفتَح بِـ`query` لا
  # بِجَلسَة: مَرحَلَة الرَمز (`stage=verify`)، وسَبعَةُ نُصوصِ خَطَأ
  # (`err=…`) في تَعبير `switch` واحِد لا يُصَيَّر مِنه إلّا واحِد.
  #
  # والقَناةُ مِن المُستَأجِر لا مِن الطَلَب (مَقيس في القاعِدَة):
  # ‏`ejar`=`phone` و`theme-demo`=`nafath`، **ولا مُستَأجِرَ واحِدَ
  # بِقَناة `email`** — فَـ`EmailLoginForm` (‏7) خارِجَ البُرهان
  # البايتيّ بِالبِنيَة، ويُقال ولا يُنسى.
  #
  # أَمّا `err` فَيُحسَب في `Login.razor` **قَبلَ** اختِيار النَموذَج،
  # فَالسَبعَةُ كُلُّها تُصَيَّر على قَناةٍ واحِدَة — ولِذلك سَبعَةُ
  # عَناوينَ على `ejar` وَحدَها، لا سَبعَةٌ في كُلّ قَناة.
  "ejar-login:/ejar/login"
  "ejar-login-verify:/ejar/login?stage=verify&phone=0512345678"
  "ejar-login-err-phone:/ejar/login?err=phone_required"
  "ejar-login-err-email-req:/ejar/login?err=email_required"
  "ejar-login-err-email-bad:/ejar/login?err=email_invalid"
  "ejar-login-err-send:/ejar/login?err=send_failed"
  "ejar-login-err-code:/ejar/login?err=code_invalid"
  "ejar-login-err-nid:/ejar/login?err=nid_required"
  "ejar-login-err-approve:/ejar/login?err=not_approved"
  "theme-demo-login:/theme-demo/login"
  "theme-demo-login-verify:/theme-demo/login?stage=verify&nid=1122334455&attempt=ab12cd34&code=42"
  # فَرعُ «المَتجَر غير مَوجود.» — مَسارٌ يُطابِق `/{slug}/login` بِمُعَرِّفٍ
  # لا مُستَأجِرَ لَه. لا يُنشِئ شَيئاً ولا يَكتُب حَرفاً.
  "login-unknown:/__no_such_tenant__/login"

  # ‏`TenantDrivers` (‏13): `order` وَحدَه فيه دَور `driver` (مَقيس في
  # `TestDataSeeder.Plan`)، فَجِسمُ الحَلَقَة لا يُصَيَّر في `ejar`.
  # والزَوجُ (مَملوء/فارِغ) يَحرُس الفَرعَين مَعاً.
  "order-drivers:/order/drivers"
  "ejar-drivers:/ejar/drivers"

  # ‏`VendorProfile` (‏10) — صَفحَةٌ عامَّة بِمُعَرِّف بائِع، والبائِعُ
  # هُنا مُستَخدِمُ `ejar/vendor` الَّذي بَذَرَه `TestDataSeeder`.
  "ejar-vendor:/ejar/vendor/e7862bae-5f0a-42b7-b071-c973d5a3305b"
  "ejar-vendor-missing:/ejar/vendor/00000000-0000-0000-0000-000000000001"

  # فَرعا «المَجهول» في صَفحَتَي الدَور والمَنطِقَة.
  "ejar-onboarding-anon:/ejar/me/role/onboarding"
  "ejar-area-anon:/ejar/me/area"

  # ‏2026-08-29 — صَفَحاتُ المَنَصَّةِ الخَمس، وفُروعُها الإنجليزِيَّة.
  #
  # كُلُّها **عامَّةٌ بِالكامِل** وبِلا مُستَأجِر، فَمَكانُها هُنا لا في
  # `GUARDED_PAGES`. وأُضيفَت في نَفسِ الكوميتِ الَّذي أَنشَأَها: صَفحَةٌ
  # تُكتَبُ اليَومَ ولا تُلتَقَط تَصيرُ غَداً «دَيناً لا يَراهُ عَدّاد».
  #
  # والفَرعُ الإنجليزيُّ مَسارٌ مُستَقِلٌّ لا كوكي، فَيُلتَقَطُ بِـ`curl`
  # بِلا حالَةٍ ولا تَفضيلِ لُغَة — ولَو كانَ كوكِيّاً لَما بَلَغَتهُ
  # هذِه الأَداةُ أَصلاً.
  #
  # و`/terms/zz` تَحرُسُ **فَرعَ اللُغَةِ غَيرِ المَعروفَة**: مَسارٌ
  # يُطابِقُ القالِبَ ولا يُطابِقُ مَعجَمَ اللُغات، فَيُصَيَّرُ
  # «غَير مَوجود» بَدَلَ أَن يُنتِجَ نُسخَةً عَرَبِيَّةً ثانِيَةً
  # بِعُنوانٍ آخَر.
  "platform-terms:/terms"
  "platform-terms-en:/terms/en"
  "platform-terms-unknown-lang:/terms/zz"
  "platform-privacy:/privacy"
  "platform-privacy-en:/privacy/en"
  "platform-refunds:/refunds"
  "platform-refunds-en:/refunds/en"
  "platform-pricing:/pricing"
  "platform-contact:/contact"
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
#
#  ‏2026-08-16 — مِلَفّان خامِس وسادِس، **بِقَناة دُخولٍ ثالِثَة**:
#    member  — عُضوُ مَتجَر في مُستَأجِرٍ **مَملوء**، وهو **طَرَفٌ في
#              صَفقَتَين** (مُبادِر). يَفتَح `MyDealDetail` و`MyDeals` و
#              `CreateListing` و`CartPage` و`MyOffers`…
#    member2 — الطَرَفُ المُقابِل في نَفس الصَفقَة. يَفتَح فَرعَ
#              «بانتِظار …» الَّذي **لا يَقَع لِلمُبادِر أَبَداً** — أَي
#              أَنّ الزَوجَ يَحرُس وَجهَي «دَورُكَ الآن» لا وَجهاً واحِداً.
#
#  ولِماذا قَناةٌ ثالِثَة: مُستَخدِمو `ejar` بَذَرَهُم `TestDataSeeder`
#  بِهاتِفٍ **وبِلا `NationalId`** (مَقيس)، فَنَفاذُ الوَهميّ لا يَبلُغُهُم.
#  والقَناةُ المَوجودَة أَصلاً هي OTP الهاتِف (`/{slug}/auth/phone/*`)
#  بِرَمزٍ ثابِت `MockSmsChannel.FixedCode` = `123456` — فَلا آلِيَّةَ
#  جَديدَة تُبنى، والقائِمُ يُستَعمَل (القاعِدَة ٨).
#
#  **وصِفرُ بَيانٍ يُنشَأ**: المُستَخدِمانِ قائِمانِ مُنذُ بَذرٍ سابِق،
#  والصَفقَتانِ قائِمَتان. و`NotifyAdminsAsync` لا تَقَع إلّا لِمُستَخدِمٍ
#  عُمرُه دونَ الدَقيقَة، فَالدُخولُ هُنا قِراءَةٌ خالِصَة.
#
#    WSL_CAPTURE_MEMBER_PHONE  — هاتِف عُضو المَتجَر (طَرَفٌ في صَفقَة)
#    WSL_CAPTURE_MEMBER2_PHONE — هاتِف الطَرَف المُقابِل
#    WSL_CAPTURE_MEMBER_TENANT — مُستَأجِرُهُما (افتِراضيّ ejar)
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

  # ══ المَوجَة التاسِعَة — صَفَحاتُ العُضو ═══════════════════════════
  #
  # ‏`{member}` تُستَبدَل بِـ`WSL_CAPTURE_MEMBER_TENANT` كَما تُستَبدَل
  # `{tenant}` لِمِلَفّ `user`.
  #
  # ‏**`MyDealDetail` (‏41 سِلسِلَة)** — خَمسَةُ فُروعٍ لا يَجتَمِعُ
  # مِنها في طَلَبٍ واحِد إلّا واحِد، وهذا مَقيس:
  #   · نَشِطَة/مُبادِر  → «دَورُكَ الآن» + الإجراء + الإلغاء
  #   · نَشِطَة/مُقابِل  → «بانتِظار …» + «بانتِظار الطَرَف الآخَر لِـ»
  #   · مُكتَمِلَة       → «اكتَمَلَت» + نَموذَج التَقييم بِنُجومِه الخَمس
  #   · مُعَرَّفٌ لا يُطابِق → «الصَّفقَة غَير مَوجودَة»
  #   · بِلا جَلسَة      → في `PAGES` أَعلاه
  "member-deal-active:member:/{member}/deals/17497544-0a6a-485d-bc72-f5d738995169"
  "member-deal-waiting:member2:/{member}/deals/17497544-0a6a-485d-bc72-f5d738995169"
  "member-deal-done:member:/{member}/deals/8c34e5ee-1760-4eba-b276-caf3e721f996"
  "member-deal-missing:member:/{member}/deals/00000000-0000-0000-0000-000000000001"

  # ‏**`CreateListing` (‏40)** — أَربَعَةُ فُروع. و`ejar` بِتِسع فِئات
  # على **خَمسَة `Kind`** فَيُصَيَّر فَرعُ الشَجَرَة (`groups.Count > 1`)
  # بِتَسمِياتِه («سَكَنيّ»، «تِجاريّ»…) — وهي فَرعٌ لا يَقَع في
  # `theme-demo` ذي الـ`Kind` الواحِد (مَقيس).
  "member-create-listing:member:/{member}/create-listing"
  "member-create-listing-cat:member:/{member}/create-listing?category=apartment"
  "member-create-listing-invalid:member:/{member}/create-listing?err=invalid"
  "member-create-listing-forbidden:member:/{member}/create-listing?err=forbidden"

  # ‏**`TenantManage` (‏24)** — ثَلاثَةُ فُروع: الإداريّ (‏`user` في
  # `theme-demo` يَملِك `tenant.manage`)، وغَيرُ الإداريّ (‏`member`)،
  # والمَجهول (في `PAGES`).
  "user-manage:user:/{tenant}/manage"
  "member-manage:member:/{member}/manage"

  # ‏**`CheckoutPage` (‏29)** — الفَرعُ الوَحيدُ الَّذي تَبلُغُه جَلسَة
  # اليَوم هو «السَّلَّة فارِغَة»: **صِفر سَلَّة في القاعِدَة كُلِّها**
  # (مَقيس). وجِسمُ المُعالِج الثُلاثيّ الخُطوات يَحتاج **بَياناتٍ**
  # لا جَلسَة — يُعلَن ولا يُرَحَّل بِلا بُرهان.
  # ‏2026-08-16، المَوجَة العاشِرَة — **البَذرَةُ تَفتَح الجِسم**.
  # ‏`CartSampleSeeder` (خَلفَ `CART_SAMPLE_SEED=1`) يَبذُر سَلَّةَ
  # عَيِّنَةٍ بِنيَوِيَّة لِصاحِبِ مِلَفّ `member`، فَتَنقَلِب هاتانِ
  # الصَفحَتان مِن «السَّلَّة فارِغَة» إلى الجِسم — وهو أَثَرُ بَياناتٍ
  # صَحيح **أُعيدَ تَثبيتُه مُعلَناً**، لا انحِدار.
  #
  # وخُطُواتُ المُعالِج الثَلاث لا يَجتَمِعُ مِنها في طَلَبٍ واحِدٍ إلّا
  # واحِدَة (`?step=`)، والثالِثَةُ فَرعان بِـ`pay`. وقيَمُ النَموذَج
  # في العُنوان **لاتينِيَّةٌ مُعَلَّمَةٌ عَيِّنَةً** لِأَنَّها تُطبَع في
  # الصَفحَة فَتَدخُل بايتات الأَساس.
  "member-checkout:member:/{member}/checkout"
  "member-checkout-pay:member:/{member}/checkout?step=2&name=SAMPLE&phone=0512345678&addr=SAMPLE-ADDRESS"
  "member-checkout-confirm-cash:member:/{member}/checkout?step=3&name=SAMPLE&phone=0512345678&addr=SAMPLE-ADDRESS&pay=cash"
  "member-checkout-confirm-card:member:/{member}/checkout?step=3&name=SAMPLE&phone=0512345678&addr=SAMPLE-ADDRESS&pay=card"
  # وفَرعُ «السَّلَّة فارِغَة» لا يَضيع: `member2` بِلا سَلَّة.
  "member2-checkout-empty:member2:/{member}/checkout"
  "member-cart:member:/{member}/cart"

  # ── جيرانٌ يَبلُغُهُم نَفسُ المِلَفّ في نَفسِ الجَولَة ──────────────
  "member-deals:member:/{member}/deals"
  "member-offers:member:/{member}/me/offers"
  "member-listings:member:/{member}/me/listings"
  "member-favorites:member:/{member}/favorites"
  "member-chats:member:/{member}/chats"
  "member-notifications:member:/{member}/notifications"
  "member-role:member:/{member}/me/role"

  # ══ شاشاتُ الحاضِنَة — بَذرَةٌ لا مِفتاح ═══════════════════════════
  #
  # ‏`StudioStudy` (‏99) و`IncubatorStudy` (‏44) و`StudioStudyPrint`
  # (‏41) = ‏184 سِلسِلَة، وكُلُّها تَقرَأ `AnalysisJson` مِن
  # `IncubatorSession`. وكانَ في القاعِدَة **صِفرُ جَلسَة**، وتَوليدُ
  # الدِراسَة يَمُرّ بِمُزَوِّد LLM حَقيقيّ.
  #
  # فَبُذِرَت جَلسَةٌ **كامِلَةُ البِنيَة بِقيَمٍ مُعَلَّمَةٍ عَيِّنَةً**
  # خَلفَ `INCUBATOR_SAMPLE_SEED=1` (‏`apps/V1.App/Seed/`)، بِمُعَرِّفٍ
  # ثابِت — فَالعُنوانُ هُنا حَرفيّ لا مُكتَشَف. بِلا المُتَغَيِّر
  # لا تُوجَد الجَلسَة وتَخرُج هذِه الأَربَعُ بِـ«الجَلسَة غَير مَوجودَة»،
  # وتَحمَرّ المُقارَنَة — وذاكَ صَحيح: بيئَةٌ ناقِصَة لا شيفرَةٌ
  # مُنحَرِفَة.
  #
  # ومالِكُ الجَلسَة هو صاحِبُ `PLATFORM_ADMIN_PHONE`، أَي مِلَفُّ
  # `admin` — و`StudioStudy` يَشتَرِط `OwnerUserId == Auth.UserId`.
  # ══ المَوجَة العاشِرَة — ما لا تَبلُغُه إلّا جَلسَة ═══════════════
  #
  # ‏`TenantUsers` (‏10): تَوأَمُ شاشات الاستوديو، ويَفتَحُها مِلَفُّ
  # `admin` بِمِلكِيَّة `owner-test` — بِلا بَيانٍ جَديد.
  "admin-tenant-users:admin:/admin/tenants/owner-test/users"

  # ‏`ChatRoom` (‏10): مُحادَثَةُ `ejar` الوَحيدَة، بَذَرَها
  # `TestDataSeeder` بَين أَوَّلِ مُستَخدِمَين — و`member` أَحَدُهُما.
  "member-chat:member:/{member}/chats/90e91b8c-330d-483a-8908-869196182fd5"

  # ‏`RoleOnboarding` (‏9) و`DriverArea` (‏11) بِجَلسَة — الطَرَفُ
  # المُقابِل لِفَرعَي «المَجهول» في `PAGES`.
  "member-onboarding:member:/{member}/me/role/onboarding"
  "member-area:member:/{member}/me/area"

  # فَرعُ «تَسجيل الدُخول» في `Login` نَفسِه: صاحِبُ الجَلسَة يُصَيِّر
  # سَطرَ التَحويل («جارٍ التَّحويل…») الَّذي لا يَبلُغُه زائِر أَبَداً.
  "member-login:member:/{member}/login"

  "studio-study:admin:/studio/s/5a3e1d00-0000-4000-8000-00000000ec01"
  "studio-study-print:admin:/studio/s/5a3e1d00-0000-4000-8000-00000000ec01/print"
  "admin-incubator-study:admin:/admin/incubator/5a3e1d00-0000-4000-8000-00000000ec01/study"
  "admin-incubator-wizard:admin:/admin/incubator/5a3e1d00-0000-4000-8000-00000000ec01"
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
MEMBER_TENANT="${WSL_CAPTURE_MEMBER_TENANT:-ejar}"

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
# نُقطَتان: `/studio/auth/login` **تُصدِر** الرَمزَ عَبر القَناة، و
# `/studio/auth/verify` تَستَهلِكُه وتَكتُب الكوكي.
#
# ‏2026-08-23 — **والأولى صارَت لازِمَة**. كانَت هذِه الدالَّةُ تَستَدعي
# الثانِيَةَ وَحدَها لِأَنّ التَحَقُّقَ كانَ `code == "123456"` ثابِتاً
# بِلا رَمزٍ مُصدَر. أَغلَقَ `942539b8` ذلكَ البابَ (رَمزٌ عَشوائيٌّ
# مُجَزَّأٌ بِمُهلَة عَبر `AuthHandlers.IssueAttempt`)، فَصارَ الاستِدعاءُ
# المُفرَد يَرتَدُّ بِلا كوكي — وأَداةُ اللَقطَة تَخرُج عِندَ أَوَّلِ
# مِلَفّ. والرَمزُ في التَطوير يَبقى `123456` لِأَنّ `MockSmsChannel`
# تُعلِن `DevHintCode`، و`AuthHandlers.NewCode` يُفَضِّلُه على العَشوائيّ.
# فَنَفسُ شَكل `phone_login` أَدناه حَرفاً: أَصدِر ثُمَّ استَهلِك.
studio_login() {
  local jar="$1" phone="$2"
  curl -s -o /dev/null -X POST -d "phone=$phone" "$BASE_URL/studio/auth/login"
  curl -s -c "$jar" -o /dev/null \
       -X POST -d "method=phone" -d "phone=$phone" -d "code=$DEV_CODE" \
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
# ── تَسجيل دُخول عُضو مَتجَر بِـOTP الهاتِف ─────────────────────────
# نُقطَتان كَما في الاستوديو: `…/phone/login` تَطلُب الرَمز بِلا حالَةٍ
# تُحفَظ في جَرَّتِنا، و`…/phone/verify` هي الَّتي تَكتُب الكوكي.
# ونَستَدعي الأولى لِأَنّ الثانِيَة تَقرَأ رَمزاً **مُصدَراً فِعلاً**
# (`AuthHandlers.VerifyPhoneOtpHandler`) — بِخِلاف مَسار الاستوديو.
#
# واسمُ الكوكي مُتَضَمِّنٌ لِلمُستَأجِر (`.acommerce.auth.{slug}`)، وهو
# ما يُفحَص — لا مُجَرَّد وُجود جَرَّة.
phone_login() {
  local jar="$1" tenant="$2" phone="$3"
  curl -s -o /dev/null -X POST -d "phone=$phone" "$BASE_URL/$tenant/auth/phone/login"
  curl -s -c "$jar" -o /dev/null \
       -X POST -d "phone=$phone" -d "code=$DEV_CODE" \
       "$BASE_URL/$tenant/auth/phone/verify"
  has_cookie "$jar" ".acommerce.auth.$tenant"
}

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

if [ -n "${WSL_CAPTURE_MEMBER_PHONE:-}" ]; then
  if phone_login "$JARS/member" "$MEMBER_TENANT" "$WSL_CAPTURE_MEMBER_PHONE"; then
    PROFILE_JAR[member]="$JARS/member"; PROFILES_READY="$PROFILES_READY member"
  else
    echo "✗ فَشِلَ دُخول مِلَفّ 'member' — لا كوكي $MEMBER_TENANT." >&2; exit 1
  fi
else
  PROFILES_MISSING="$PROFILES_MISSING member(WSL_CAPTURE_MEMBER_PHONE)"
fi

if [ -n "${WSL_CAPTURE_MEMBER2_PHONE:-}" ]; then
  if phone_login "$JARS/member2" "$MEMBER_TENANT" "$WSL_CAPTURE_MEMBER2_PHONE"; then
    PROFILE_JAR[member2]="$JARS/member2"; PROFILES_READY="$PROFILES_READY member2"
  else
    echo "✗ فَشِلَ دُخول مِلَفّ 'member2' — لا كوكي $MEMBER_TENANT." >&2; exit 1
  fi
else
  PROFILES_MISSING="$PROFILES_MISSING member2(WSL_CAPTURE_MEMBER2_PHONE)"
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
  path="${path//\{member\}/$MEMBER_TENANT}"

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
