# Milestones المُنفَّذَة — التَّقدُّم نَحو الإنتاج

> سِجِلّ ما تَمَّ بِناؤه في المَوجَة الأَخيرَة، مَع تَمييز ما هو فِعليّ
> مُقابِل ما هو mock، والخُطوات المَلموسَة لِلإطلاق الحَقيقيّ.

## ما اكتَمَلَ

### م١ — Provider ports + mocks (الأَساس)
- `IMapsProvider` + `MockMapsProvider` (geocode/route/reverse، إحداثيّات
  سَعوديَّة، Haversine).
- `ACommerce.Kit.Delivery.Core` (kit جَديد): `IDeliveryProvider` +
  `MockDeliveryProvider` يُحاكي دَورَة حَياة الشَّحنَة كامِلَة (Created
  → DriverAssigned → PickedUp → InTransit → Delivered) عَلى مُؤَقِّت،
  مَع تَحديث مَوقِع المَندوب مِن Pickup إلى Dropoff.
- `ACommerce.Kit.Payments.Core` (kit جَديد): `IPaymentProvider` مَع
  authorize/capture/refund + اشتِراك مُتَكَرِّر + invoice (شَكل ZATCA،
  VAT 15% inclusive، أَرقام `INV-YYYY-NNNNNN`). `MockPaymentProvider`
  يَدعَم idempotency keys.
- جَميعها مُحقونَة عَبر DI، تَبديلها لِمُزَوِّد فِعليّ = سَطر واحِد.

### م٢ — Deal pipeline (التَّدَفُّق المُوَحَّد)
- `Deal` (Marten doc): النَّمَط + الأَطراف + المَبلَغ + Stage + Status
  + Refs (مَراجِع كائِنات خارِجيَّة) + Timeline (سِجِلّ كُلّ تَحَوُّل).
- `DealStage`: Offered → Booked → Confirmed → Paid → Shipping →
  Delivered → Received → Reviewed.
- `DealsPolicy`: لِكُلّ نَمَط (marketplace/rental/trip/service/
  classifieds) قائِمَة مَراحِله الفَرعيَّة + الفاعِل المَسموح في كُلّ
  مَرحَلَة.
- `DealsService`: البَوّابَة الوَحيدَة لِلتَّعديل، تَفحَص الـ state
  machine + الفاعِل، تَكتُب timeline، تُكمِل تِلقائيّاً عِندَ آخِر مَرحَلَة.

### م٣ — Deal management UI
- `/studio/apps/{slug}/deals` — قائِمَة بِـ ٤ إحصائيّات + جَدوَل كامِل
  بِأَيقونات لِكُلّ stage مُلَوَّنَة + شَريط حالَة.
- `/studio/apps/{slug}/deals/{id}` — تَفاصيل صَفقَة + **stepper بَصَريّ**
  لِلمَراحِل + سَجِلّ زَمَنيّ كامِل + إجراءات (advance/cancel/dispute).
- زِرّ "seed" لِإنشاء Deal تَجريبيَّة في أَيّ نَمَط لاختِبار التَّدَفُّق.

### م٤ — Ticket reply UI
- `/studio/apps/{slug}/tickets` — قائِمَة بِفَلتَرَة (open/answered/closed)
  + إحصائيّات.
- `/studio/apps/{slug}/tickets/{id}` — خَيط مُحادَثَة (الرِسالَة الأَصلِيَّة
  + كُلّ الرُّدود مَع تَمييز رُدود الفَريق بِلَون العَلامَة + أَفاتار)
  + نَموذَج رَدّ + زِرّ إغلاق.
- يَستَخدِم `TicketReplied`/`TicketClosed` الَّذَين كانا مَوجودَين كَ events
  لكِنّ بِلا UI.

### م٥ — Listing moderation
- `ListingModerated` event جَديد عَلى Listing aggregate (Hidden +
  Reason + ModeratorId).
- `/studio/apps/{slug}/listings` — جَدوَل لِكُلّ إعلانات التَّطبيق
  بِنَظرَة إشرافيَّة (نَشِط/مُخفي/مَحذوف) + إجراءات (إخفاء بِسَبَب،
  إظهار، حَذف بِتَأكيد).

### م٦ — Reviews kit
- `ACommerce.Kit.Reviews.Core` (kit جَديد): `Review` doc + `ReviewSummary`
  + `ReviewsService` (Submit/Respond/Hide/Summary/List/HasReviewed).
- مُتَبادَل (السائِق يُقَيِّم الراكِب وَالعَكس)، مَربوط بِـ DealId،
  مُتَحَقَّق إن جاءَ مِن صَفقَة فِعليَّة.
- نَموذَج التَّقييم مَدموج في `StudioAppDealDetail` عِندَ Reviewed/
  Delivered/Received.

### م٧ — Audit log
- `AuditEntry` + `AuditWriter` (scope = tenant slug أَو "_platform").
- صَفحَة `/admin/audit/{scope}` لِمُشرِف المَنصَّة مَع chip picker لِكُلّ
  مُستَأجِر + سِجِلّ زَمَنيّ.

### م٨ — فَوتَرَة فِعليَّة (mock)
- `POST /studio/billing/select` يَستَدعي `IPaymentProvider.
  CreateSubscriptionAsync` مَع idempotency key. الفَشَل → بَنِر خَطَأ،
  النَّجاح → audit entry لِلمَنصَّة.
- إعلانات مَخفِيَّة إشرافيّاً تَختَفي مِن البَحث العامّ (TenantHome +
  TenantExplore).

### م٩ — تَوسيع audit
- كُلّ `deal.advance/cancel/dispute` + كُلّ `ticket.reply/close` +
  `listing.hide/unhide/delete` + `billing.subscription.create` يَكتُب
  AuditEntry.

### م١٠ — رُؤيَة شامِلَة + تَعليق
- StudioHome يَجمَع آخِر العَمَلِيّات عَبر كُلّ تَطبيقات صاحِب المَشروع
  (٥ بُنود بِـ stage emoji + slug + chip).
- `POST /admin/tenants/{slug}/suspend` (action=suspend/reactivate):
  ميزَة `Tenant.IsSuspended` + `SuspensionReason` + audit لِلمَنصَّة.

---

## ما هو فِعليّ مُقابِل mock

| المُكَوِّن | الحالَة | لِلإنتاج |
|---|---|---|
| الـ Deal pipeline | فِعليّ كامِل | جاهِز |
| Reviews | فِعليّ كامِل | جاهِز (يَحتاج UI عَلى البروفايل لاحِقاً) |
| Audit log | فِعليّ كامِل | جاهِز |
| Listing moderation | فِعليّ كامِل | جاهِز |
| Ticket reply | فِعليّ كامِل | جاهِز |
| Mock Maps | يُحاكي OSM | بَدِّل بِـ Google Maps أَو احتَفِظ بِـ OSM |
| Mock Delivery | يُحاكي حَركَة شَحنَة | بَدِّل بِـ Saee/Mrsool/Aramex |
| Mock Payments | invoice + sub + capture/refund | بَدِّل بِـ Moyasar/Tap |
| Mock SMS (OTP) | "123456" دائِماً | بَدِّل بِـ Unifonic/Taqnyat |
| Mock Nafath | auto-approve في ٥ث | بَدِّل بِـ Nafath الفِعليّ |
| Tier subscription | يُغَيِّر الباقَة فَوراً | تَكامُل دَفع فِعليّ مَطلوب |

كُلّ الـ mocks خَلف ports — التَّبديل = سَطر تَسجيل في `Program.cs`
+ تَنفيذ HTTP لِواجِهَة المُزَوِّد. لا تَغيير في الكود الَّذي يَستَخدِمها.

---

## ما يَتَبَقَّى لِلإطلاق الفِعليّ

### حَواجِز إلزاميَّة (P0)
1. **Payment provider** فِعليّ (Moyasar/Tap) + webhook handler +
   حالات الفَشَل/الاستِرداد. حاليّاً mock.
2. **SMS provider** فِعليّ (Unifonic/Taqnyat) — كُلّ الـ OTPs مُزَوَّرَة.
3. **Audit** هَل مَوجود — لكِنّ projection/فِهرَسَة لِلسُّرعَة.
4. **TLS + النَّشر**: حاليّاً يَعمَل على localhost.

### تَجرِبَة مُكتَمِلَة (P1)
5. **Media/Images kit**: تَخزين صور (Azure Blob/S3) + معالَجَة (thumbs).
6. **User management UI في studio** بَدَل التَّفويض لِـ `/admin/tenants/
   {slug}/users` الَّذي بِالتَّصميم القَديم.
7. **Team membership** لِرائِد الأَعمال (دَعوَة أَعضاء بِأَدوار).
8. **Live tracking** لِنَمَط Trip (موقع السائِق عَبر SignalR).
9. **Reviews summary** عَلى بروفايل المُستَخدِم.

### نُضج (P2)
10. **Rental/Bookings kit**: تَوفُّر + حَجز + تَقويم.
11. **Projections** لِلوحات (QualityMonitor، StudioHome) — حاليّاً
    تَجميع في الذاكِرَة.
12. **Integration tests** عَلى الـ workflows الحَرِجَة.
13. **i18n** كامِل عَبر `L["..."]` (بَعض اللوحات الجَديدَة hardcoded).
14. **a11y**: مُراجَعَة contrast، تَنَقُّل لوحَة مَفاتيح، ARIA.
15. **PWA مُحَدَّث** لِكُلّ دَور بِمُحتَوى offline قابِل لِلتَخزين.
16. **GDPR/PDPL**: تَصدير + حَذف بَيانات المُستَخدِم.

---

## القَرارات المُلَخَّصَة (مِن أَجوبَة المُستَخدِم)

| القَرار | الجَواب |
|---|---|
| Payment | mock الآن، عَلى DI/ports — تَبديل بِسَطر |
| SMS | تَأجيل، إبقاء IOtpChannel مُجَرَّد |
| نَموذَج الإيراد | اشتِراك + عمولة |
| النَّمَط الأَوَّل | **التَّدَفُّق المُوَحَّد** (Deal pipeline) يُغَطّي كُلّ الأَنماط |
| Rental | تَأجيل (المُستَخدِم لَم يَختَر هذا، لكِنّ Deal pattern يَكفي) |

---

## تَدَفُّق إنتاجيّ كامِل (end-to-end كَما يَعمَل الآن)

1. زائِر → `/` → يَكتُب فِكرَة → `/studio/auth` → 123456 → `/studio/consent`
   (أَوَّل مَرَّة) → تَحليل LLM + بَيانات سَعوديَّة + حَفظ تَدقيق
   `prompt_version`.
2. يَعرِض الدِراسَة بِخَريطَة مَخاطِر + إعادَة توليد قِسم + تَقييم
   👍/👎 + تَصدير PDF/Excel.
3. زِرّ «أَنشِئ التَّطبيق» يُولِّد Tenant مَملوكاً مَع فِئات وأَدوار
   مَبدَئيَّة، مَع رَبط بِـ SourceAnalysisId.
4. لوحَة التَّطبيق: ٤ تَبويبات للتَّهيئَة + ٣ تَبويبات لِلتَّشغيل
   (Deals/Listings/Tickets) — جَميعها مَحمِيَّة بِالمِلكِيَّة.
5. كُلّ إجراء إداريّ يَكتُب AuditEntry — قابِل لِلعَرض في
   `/admin/audit/{slug}`.
6. مُشرِف المَنصَّة يَرى quality dashboard + يَستَطيع تَعليق مُستَأجِر.
7. اختِيار باقَة → استِدعاء IPaymentProvider → اشتِراك (mock) + audit.

كُلّ ذلِك بِبِنيَة قابِلَة لِلإنتاج (مَنفَذ مَفصول، DI، audit) حالما
يُسحَب الـ mocks الثَلاث (Payment/SMS/Delivery).
