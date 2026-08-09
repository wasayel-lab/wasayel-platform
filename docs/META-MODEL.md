# النموذج الفوقي — العقد المرجعي للمنصة

> **الغرض**: هذه الوثيقة هي *العقد* الذي يجب أن يستهدفه أي امتداد للمنصة —
> عدة جديدة، قالب جديد، أو (مستقبلاً) وكيل توليد قوالب. كل بند فيها متحقق
> منه من الكود مباشرة؛ ما هو مخطط وغير قائم مُعلَّم صراحة بـ **[مخطط]**.

## 1. الطبقات الثلاث

النموذج الفوقي للمنصة يعيش على ثلاث طبقات، من الأعم إلى الأخص:

1. **تشريح العدة** (Kit Anatomy) — كيف يُبنى أي نطاق.
2. **البوابات** (Gates) — كيف يُحرس أي أمر قبل تنفيذه.
3. **خط الصفقات** (Deal Pipeline) — كيف يتدفق أي تعامل بين طرفين.

---

## 2. تشريح العدة (Kit Anatomy)

كل عدة في `libs/kits/` تتبع النمط الثلاثي:

```
<Kit>.Core        النموذج: أحداث + مجمّع + أوامر + استعلامات (لا اعتماد على HTTP)
<Kit>.Server      معالجات Wolverine + نقاط HTTP (دوال static — لا Controllers)
<Kit>.Providers.* مزودات خارجية قابلة للتبديل عبر DI (ولكل مزود فعلي Mock مقابل)
```

### العناصر الإلزامية في `Core` (المثال المعياري: `Listings`، `Offers`)

| العنصر | القاعدة |
|---|---|
| **الأحداث** | `sealed record` يحمل **كامل** ما يلزم لإعادة بناء الحالة، ويحمل `TenantSlug` صراحة (رغم أن Marten conjoined tenancy يضيف `tenant_id`) لتوضيح الـ listeners وتجنب الاعتماد على سياق ضمني |
| **المجمّع** | صنف بحالة عامة + دالة `Apply` لكل حدث؛ يُحدَّث inline في نفس المعاملة عبر `Projections.Snapshot<T>(SnapshotLifecycle.Inline)` |
| **الأوامر** | `sealed record` باسم فعل (`CreateListing`, `EditListing`) — لا تعرف شيئاً عن HTTP أو cookies |
| **الاستعلامات** | `sealed record` تصف المعايير (`ListingsSearch`) |

### قواعد `Server`

- المعالج = دالة `static` موسومة `[WolverineGet/Post]`؛ Wolverine يكتشفها
  بمسح الـ assembly ويولّد كود الاستدعاء عند الإقلاع (صفر reflection في
  المسار الساخن).
- كل استعلام محصور بالمستأجر عبر `store.QuerySession(tenantSlug)` — منطق
  التطبيق **لا يكتب** `WHERE tenant_id = ?` يدوياً أبداً.
- تسجيل العدة في المضيف بسطر واحد:
  `AddKitAssembly(typeof(<Kit>Handlers).Assembly)` في `apps/V1.App/Program.cs`.

---

## 3. البوابات (Gates)

الموقع: `libs/templates/ACommerce.Templates.Customer.Marketplace/Gates/`.

الأمر **يعلن متطلباته** بواجهات علامة ولا يتدخل في منطق الفحص:

| الواجهة | تعلن | فشلها |
|---|---|---|
| `IRequireAuth` | `UserId` غير فارغ | `GateDeniedException("auth")` |
| `IRequireTenant` | `TenantSlug` محدد | `GateDeniedException("tenant")` |
| `IRequireAcceptedTerms` | المستخدم قَبِل الشروط بإصدار ≥ `TermsPolicy.CurrentVersion` | `GateDeniedException("terms")` |
| `IRequirePermission` | الدور الفعّال يملك `Permission` عبر `RolePermissions.Has` | `GateDeniedException("permission")` |

- **الترتيب ثابت**: auth ← tenant ← terms ← permission (كل بوابة تفترض نجاح
  ما قبلها).
- **وضع التوافق**: مستأجر بلا أدوار (`tenant.Roles.Count == 0`) = مفتوح
  (legacy mode).
- `ActiveRole` يُستخرج من الرابط في فلتر HTTP، ويُمرَّر صراحة مع الأوامر
  الخارجة عن HTTP (مهام خلفية).
- **[مخطط]** الانتقال إلى Wolverine middleware بسطر
  `Policies.ForMessagesImplementing<IRequireAuth>()...` — نفس الأوامر
  والواجهات، بلا تغيير في العقد.

---

## 4. خط الصفقات (Deal Pipeline)

الموقع: `Services/Deals/` — **قلب النموذج الفوقي**.

### 4.1 الكيان

`Deal` وثيقة Marten تحت مستأجرها، تجمع: النمط، الطرفين
(`Initiator`/`Counterparty`)، المبلغ (`AmountSar` + `CommissionSar`
للمنصة)، المرحلة، الحالة، مراجع الكائنات الخارجية (`Refs`: PaymentId،
ShipmentId، ReviewId…)، خصائص ديناميكية لكل نمط (`Attributes`)، و**الخط
الزمني** (`Timeline`).

### 4.2 المفردات المغلقة

**المراحل** (`DealStage` — 8):
`Offered → Booked → Confirmed → Paid → Shipping → Delivered → Received → Reviewed`

**الحالات** (`DealStatus` — 4): `Active | Completed | Cancelled | Disputed`

**أفعال الخط الزمني** (`DealEvent.Action` — 6):
`advanced | cancelled | disputed | assigned | note | refund`

**الفاعلون** (4): `initiator | counterparty | either | platform`

### 4.3 سياسة الأنماط (`DealsPolicy` فوق `DealPatternCatalog`)

`DealsPolicy` **واجهة لا بيانات**: تواقيعها الأربعة
(`StagesFor`/`Next`/`Actor`/`LabelAr`) تقرأ من كاتالوج بيانات في موضع
واحد — `Services/Deals/DealPatternCatalog.cs` فوق سجل
`DealPatternDefinition(Pattern, Stages: DealStageRule[])` حيث
`DealStageRule(Stage, Actor, LabelAr)`. لكل نمط تسلسل **خطي** من المراحل:

| النمط | التسلسل |
|---|---|
| `trip` | Offered → Booked → Confirmed → Delivered → Reviewed |
| `rental` | Offered → Booked → Confirmed → Paid → Delivered → Received → Reviewed |
| `marketplace` | Offered → Booked → Confirmed → Paid → Shipping → Delivered → Reviewed |
| `service` | Offered → Booked → Confirmed → Paid → Delivered → Reviewed |
| `classifieds` | Offered → Booked → Confirmed (تنتهي الدورة عند التواصل) |
| (افتراضي) | Offered → Booked → Confirmed → Reviewed |

والفاعل المخوّل ثابت لكل مرحلة: Offered/Paid/Received = initiator؛
Booked/Shipping/Delivered = counterparty؛ Confirmed/Reviewed = either؛
عداها = platform.

### 4.4 الثوابت (Invariants)

1. **لا كتابة مباشرة للحالة إطلاقاً** — كل تحول يمر عبر `DealsService`
   (الواجهة: `Start / AssignCounterparty / Advance / Cancel / Dispute /
   AttachRef`+ الاستعلامات) الذي يفحص آلة الحالات والفاعل.
2. **كل تحول يكتب حدثاً في الخط الزمني** — مصدر الحقيقة للتدقيق.
3. **الاكتمال تلقائي** عند بلوغ آخر مرحلة في النمط.
4. **الخصائص الشكلية المضمونة اليوم**: التسلسل خطي بلا دورات، الانتهاء
   حتمي، ولكل مرحلة فاعل معرّف. أي توسيع للسياسة يجب أن يحافظ عليها
   ويثبت ذلك آلياً — والإثبات قائم منذ 2026-08-09: `DealPatternValidator`
   دوال نقية فوق `DealPatternDefinition` تفرض T5 (بنيوياً) وT6
   (الخصائص المُبرهنة)، والأنماط الخمسة تجتازها في الاختبارات
   (انظر [TESTING-PROTOCOL §T5–T6](TESTING-PROTOCOL.md)).
   **ملاحظة مقصودة**: «الانتهاء بـ `Reviewed`» ليس شرطاً — `classifieds`
   ينتهي عند `Confirmed` قصداً، والاكتمال التلقائي سلوك `DealsService`
   لا سلوك التعريف، فموضع فحصه T7 بالمحاكاة.

---

## 5. معجم الأنماط المزدوج — تنبيه صراحة

في المنصة **معجمان** للأنماط ولا يجوز خلطهما:

1. **أنماط التدفق** (سلاسل نصية في `DealsPolicy`):
   `marketplace | rental | trip | service | classifieds` — تحكم مراحل الصفقة.
2. **أنماط شخصية الواجهة** (`AppPattern` في `Services/Patterns/PatternProfile.cs`):
   `Roommate | Rental | Marketplace | Trip | Service` — تحكم تركيب
   الرئيسية والبطاقة والـ CTA والسمات المعروضة، **وتُشتق من أدوار/فئات
   المستأجر لا تُختار يدوياً**.

لاحظ عدم التطابق (لا `classifieds` في الثاني، ولا `Roommate` في الأول) —
توحيد المعجمين قرار مفتوح **ما زال مفتوحاً**: رفع السياسة إلى بيانات
(§8) نُفذ في 2026-08-09 ولم يوحّد المعجمين عمداً، لأن التوحيد تغيير
سلوكي والخطوة اشترطت ألا يتغير السلوك بتاً.

---

## 6. كاتالوج الأدوار (`RoleCatalog`)

سبعة قوالب أدوار مغلقة — الوكيل والواجهات يختارون منها ولا يخترعون خارجها:

`customer 🛒 · rider 🧍 · vendor 🏪 · driver 🚗 · host · shipper · tenant_admin`

كل قالب: `Slug, Label, Icon, Description, HomeRoute, Permissions[],
Fields[]` — والحقول منمّطة (`RoleField`: نص/اختيار مفرد بخيارات/…) وتظهر
في بروفايل الدور تلقائياً.

---

## 7. عدة العروض (Offers) — نموذج التفاوض

نمط InDrive: صاحب الإعلان ينشر، والطرف الآخر يقدم عروضاً مضادة
(سعر + موقع + خصائص منمّطة بـ `attr_`)، وقبول عرض يغلق البقية.

- دورة العرض: `Pending → Accepted | Rejected | Withdrawn | Expired`.
- **فك ارتباط متعمد**: العدة لا تعدّل `Listing` — المطابقة تُسجل في وثيقة
  مستقلة `ListingMatch` (بمعرّف = ListingId) تتتبع أيضاً دورة الرحلة بعد
  القبول: `Active → Completed | Aborted` مع `ArrivedAt` لتأكيد الوصول.

---

## 8. الحالي مقابل المخطط — بوابة متجر القوالب

| | اليوم | **[مخطط]** |
|---|---|---|
| `DealsPolicy` | **كاتالوج بيانات في موضع واحد** (`DealPatternCatalog` فوق `DealPatternDefinition`) خلف نفس واجهة `DealsPolicy` و`DealsService` | **[مخطط]** الكاتالوج نفسه وثيقة Marten لكل مستأجر — نفس الشكل، مصدر قراءة آخر |
| إنشاء عمود تجاري جديد | إضافة تعريف نمط إلى الكاتالوج (سطر بيانات) وإعادة نشر | **أثر بيانات** يولَّد ويُتحقق منه ويُعتمد بلا نشر |
| ضمان الخصائص الشكلية | **فحص آلي** بدوال نقية (`DealPatternValidator` — T5/T6) | نفس الفحص مفروضاً **بوابةً** قبل حفظ أي نمط مولَّد |

**ما تحقق (2026-08-09)**: الأنماط الخمسة صارت `DealPatternDefinition` —
سجل بيانات: اسم النمط + صفوف مراحل، كل صف يحمل مرحلته وفاعلها وتسميتها،
فالتعريف **مكتفٍ بذاته** وقابل لأن يُخزَّن وثيقةً واحدة كما هو. جدولا
الفاعلين والتسميات مشتركان اليوم بين كل الأنماط ويعيشان في الكاتالوج،
ويُنسخان في صفوف كل تعريف — ولذلك صار اختلاف الفاعل بين نمطين ممكناً بلا
تغيير شكل. الواجهة العامة (`StagesFor`/`Next`/`Actor`/`LabelAr`) لم تتغير
حرفاً، و`DealsService` لم يُمس، والتطابق مبرهن باختبار توصيف كُتب واخضرّ
قبل الرفع ولم يُمس بعده.

**الحد المعلن**: **لا تخزين في هذه الموجة** — الكاتالوج في الكود، لا
وثائق Marten ولا قراءة لكل مستأجر. السبب: لا Postgres محلياً للتحقق،
وبوابة التحقق أولى من بوابة التخزين. حين يُنفَّذ التخزين يتغير
`DealPatternCatalog.For` وحده.

بهذا لم يبق للشرط التقني لفتح توليد القوالب إلا **التخزين لكل مستأجر**
وأداة الوكيل للتوليد؛ وشرطه المقابل قائم كما هو: ألا يمر أي تعريف نمط
مولَّد إلا عبر بروتوكول [TESTING-PROTOCOL](TESTING-PROTOCOL.md) كاملاً.

---
*آخر تحقق ضد الكود: 2026-08-09 (مُحدَّث مع رفع `DealsPolicy` إلى بيانات —
§4.3 و§8؛ الأساس السابق الكوميت `23067e3e`). أي تعديل على الملفات
المذكورة يستوجب تحديث هذه الوثيقة في نفس الـ PR.*
