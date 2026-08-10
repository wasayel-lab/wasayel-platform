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

### 3.1 بوابتا الإدارة — طبقتان، لكل طبقة تعريف واحد

سطح `/admin` **طبقتان لا واحدة**، ولكل طبقة ملف واحد يملك قرارها:

| الطبقة | النطاق | التعريف الوحيد | لافّ الصفحات |
|---|---|---|---|
| المنصة | `/admin`، `/admin/audit`، `/admin/monitor`، `/admin/fixtures`، `/admin/agent`، `/admin/incubator/*`، `/admin/tenants/new`، وإنشاء/تعليق المستأجرين | `Services/PlatformAdminGuard.cs` | `<RequirePlatformAdmin>` |
| المستأجر | `/admin/tenants/{slug}/*` | `Services/TenantAdminGuard.cs` | `<RequireTenantAdmin>` |

الطبقتان **لا تتداخلان ولا تُغني إحداهما عن الأخرى**: مالك متجر واحد يدير
متجره ولا يلمس المنصة؛ ومشرف المنصة يُنشئ المتاجر ويعلّقها. أي مسار جديد
تحت `/admin` يختار طبقته أولاً ثم ينادي تعريفها — ولا يكتب قراراً ثالثاً.

#### 3.1.1 بوابة إدارة المستأجر

الموقع: `libs/templates/.../Services/TenantAdminGuard.cs`.

كل ما تحت `/admin/tenants/{slug}/*` — **صفحة تُقرأ أو نقطة تُكتب** — يمر من
`TenantAdminGuard.CanAdministerAsync`، وهو **التعريف الوحيد** في المستودع
لقرار «يجوز له إدارة هذا المستأجر»:

1. مالك المتجر مسجّل دخولاً عبر Studio (`StudioAuth`، مطابقة `Tenant.OwnerUserId`)، أو
2. مستخدم مسجّل دخوله **داخل نفس المتجر** ودوره الفعّال يحمل `tenant.manage`
   (عبر `RolePermissions.Has` — فينطبق عليه وضع التوافق أعلاه).

- **الكتابة**: نقاط `MapPost` تنادي القرار مباشرة وترجع `403` عند الرفض.
- **القراءة**: صفحات Razor تناديه مرتين بلا كلفة ثانية (القرار محفوظ
  لـ `HttpContext` الواحد): مرة في `OnParametersSetAsync` **قبل أي استعلام**
  فلا تُحمَّل بيانات المتجر أصلاً، ومرة في `<RequireTenantAdmin>` الذي يحجب
  التصيير ويعرض حالة «غير مصرَّح».
- حالة الرفض **لا تذكر شيئاً من المتجر** — ولا اسمه ولا هل هو موجود — فلا
  تصلح الصفحة عدّاداً لتعداد المتاجر.

> **لماذا كُتب هذا البند:** كان القرار دالّة محلية داخل
> `MarketplaceTemplateExtensions` لا تراها الصفحات، فحُرست الكتابة وحدها.
> القياس يوم 2026-08-10: طلب `curl` مجهول بلا أي كوكي كان يرسم **ثماني صفحات
> لكل واحد من خمسة متاجر** كاملةً — كاتالوج الأدوار بصلاحياته، والفئات،
> والمدن، والخصائص، والهوية البصرية، وقائمة المستخدمين بهواتفهم وأرقام
> هوياتهم. القاعدة المستخلصة: **من حرس الكتابة فليحرس القراءة بنفس السطر، لا
> بسطر يشبهه.**

#### 3.1.2 بوابة إدارة المنصة

الموقع: `libs/templates/.../Services/PlatformAdminGuard.cs`.

القرار: **جلسة `StudioAuth` صالحة، وصاحبها `StudioUser.IsPlatformAdmin`** —
وما عداه رفض. ويعود بثلاث حالات لا اثنتين، لأن الواجهة تفرّق بين «لم
تسجّل دخولاً» (تعرض زر الدخول) و«سجّلت ولست مشرفاً» (تعرض المنع)، بينما
طرف الكتابة يطوي الحالتين في `403` واحدة.

- **الكتابة**: كل `MapPost` على نطاق المنصة يبدأ بـ
  `PlatformAdminGuard.EvaluateAsync` ويرجع `403` — **قبل قراءة الحقول**.
  والقرار يحمل معه الـ`StudioUser` فتكتب نقطة `suspend` سطر التدقيق باسمه
  بلا استعلام ثانٍ.
- **القراءة**: `<RequirePlatformAdmin>` ينادي نفس التعريف (القرار محفوظ
  لـ `HttpContext` الواحد).
- **الصفحة التفاعلية استثناء يستحق قاعدته**: `/admin/agent` كانت
  `@rendermode InteractiveServer` بكاملها، وأفعالها تنادي `AgentService`
  و`AgentToolExecutor` **مباشرة عبر SignalR لا عبر نقاط الـ POST** — فحراسة
  النقاط وحدها لا تحرسها، وداخل الـ circuit يصير `HttpContext` فارغاً فلا
  يقدر أي حارس يقرأ الكوكي أن يحكم هناك. الحل: **الحكم في مضيف ثابت
  (`AgentChat.razor`) والتفاعل في جزيرة (`AgentChatPanel.razor`)** لا
  تُبَثّ إلا بعد القبول. وواصفات مكوّنات Blazor Server محمية بـ Data
  Protection، فلا يفتح عميلٌ circuit لمكوّن لم يصيّره الخادم له — والقياس
  أثبته: الصفحة للمجهول فيها **صفر** واصفات، وللمشرف واحد.

> **لماذا كُتب هذا البند:** كان القرار مكتوباً **مرتين** — في
> `RequirePlatformAdmin.razor` وفي نقطة `suspend` — وغائباً عمّا سواهما.
> القياس يوم 2026-08-10: خمس صفحات منصة تُرسم كاملةً لطلب `curl` مجهول
> (منها `/admin/agent` بسجلّ محادثات صاحب المنصة نفسه مع الوكيل)، وتسع نقاط
> كتابة تعمل بلا أي تخويل — أخطرها `POST /admin/tenants/create`: **مجهول
> يُنشئ مستأجراً حقيقياً في المنصة بطلب واحد**. وكانت تبدو محروسة لأنها
> ترتدّ بـ`302` عن حقل ناقص — والارتداد كان من **تحقّق الحقول** لا من
> بوابة. القاعدة المستخلصة: **ترتيب الفحص جزء من الفحص — التخويل قبل
> التحقّق، وإلا صار خطأ التحقّق قناعاً للثغرة.**

#### 3.1.3 سجلّ الحصر المقيس (2026-08-10)

كل مسار `/admin` في المستودع، مقيساً بـ`curl` **مجهول** قبل الإصلاح وبعده.
«يُرسم» = محتوى الصفحة الفعلي؛ «محجوب» = بطاقة الدخول/المنع وحدها.

**صفحات نطاق المنصة (١٠):**

| المسار | قبل | بعد |
|---|---|---|
| `/admin` | محجوب | محجوب |
| `/admin/audit` · `/admin/audit/{scope}` | محجوب | محجوب |
| `/admin/monitor` | محجوب | محجوب |
| `/admin/fixtures` | محجوب | محجوب |
| `/admin/agent` | **يُرسم** (٣٩ ك.ب — سجلّ المحادثات) | محجوب |
| `/admin/incubator` | **يُرسم** | محجوب |
| `/admin/incubator/{id}` | **يُرسم** | محجوب |
| `/admin/incubator/{id}/study` | **يُرسم** | محجوب |
| `/admin/tenants/new` | **يُرسم** | محجوب |

**نقاط كتابة نطاق المنصة (١٠):**

| النقطة | قبل | بعد |
|---|---|---|
| `POST /admin/tenants/create` | **٣٠٢ — بلغت المعالج** | `403` |
| `POST /admin/agent/ask` · `tool/{id}/apply` · `tool/{id}/reject` · `reset` | **٣٠٢ — بلغت المعالج** | `403` |
| `POST /admin/incubator/start` · `restart` · `{id}/answer` · `{id}/analyze` | **٣٠٢ — بلغت المعالج** | `403` |
| `POST /admin/tenants/{slug}/suspend` | `403` (نسخة القرار الثانية) | `403` (التعريف الواحد) |

**نطاق المستأجر (٨ صفحات + ٨ نقاط):** محجوبة/`403` قبل وبعد — الموجة
السابقة أغلقتها، وقيست هنا للتأكّد أنها لم تنكسر.

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

## 6. كاتالوج الأدوار (`RoleCatalog`) — ملفات، ومعجمان مغلقان، ومصادق

**عشرة** تعريفات أدوار مغلقة — الوكيل والواجهات يختارون منها ولا يخترعون
خارجها:

`customer 🛒 · rider 🧍 · vendor 🏪 · driver 🚗 · host 🏠 · shipper 📦 ·
tenant_admin 👔 · broker 💼 · mover 🚚 · organizer 🎉`

السبعة الأولى **نُقلت** من كود مجمَّع إلى ملفات (الموجة الأولى أدناه)،
والثلاثة الأخيرة **أُلِّفت ملفات ابتداءً** ولم يُكتب لها سطر C# واحد —
لا في التصيير، ولا في التنقل، ولا في تعداد الوكيل. انظر §6.6.

**ما تحقق (2026-08-10)**: القوالب السبعة خرجت من مصفوفة `RoleTemplate`
مكتوبة في `RoleCatalog.cs` ومجمَّعة معه، إلى **ملف JSON لكل دور** —
`libs/kits/Roles/ACommerce.Kit.Roles.Core/Definitions/{slug}.role.json`،
بترتيب `roles.index.json`. الواجهة العامة **لم تتغير حرفاً**
(`All`/`Find`/`InstantiateRole` بتواقيعها وأنواعها)، فلا مستهلك واحد من
التسعة احتاج تعديلاً. والتطابق **مبرهن لا مُدّعى**: اختبار توصيف يلتقط
سطح الكاتالوج كاملاً — بما فيه الصلاحيات بترتيبها والحقول بنصوصها
المشكولة حرفياً ومصفوفة `RolePermissions.Has` كاملة — كُتب واخضرّ في
كوميت مستقل **قبل** النقل، ولم يُمس سطر منه بعده.

### 6.1 شكل `RoleDefinition`

```json
{
  "slug": "driver",
  "icon": "🚗",
  "homeRoute": "/explore",
  "label":       { "ar": "سائِق", "en": null },
  "description": { "ar": "سائِق مَركَبَة يُقَدِّم عُروضاً…", "en": null },
  "permissions": ["listing.browse", "offer.submit", "chat.respond"],
  "fields": [
    { "code": "vehicle_type",
      "label": { "ar": "نَوع المَركَبَة", "en": null },
      "type": "SingleSelect", "isRequired": true,
      "options": [ { "value": "economy", "label": { "ar": "اقتِصاديّ", "en": null } } ] }
  ],
  "composition": {
    "home": "driverHome", "createListing": "defaultCreateForm",
    "nav": "driverNav", "explore": "driverExplore",
    "publicProfile": null, "extras": ["driverArea"]
  },
  "dealPatternAffinity": "trip"
}
```

**الترجمات**: كل سلسلة يراها المستخدم حاوية `{ar, en}`. العربية إلزامية
(مملوءة من القيم القائمة بتشكيلها حرفياً) والإنجليزية بنية جاهزة فارغة.
**والقراءة تبقى العربية كما اليوم** — `LocalizedText.Current` يرجع `ar`
دائماً؛ خدمة التوطين `L` وزر اللغة قائمان في المستودع و**لم يُربطا**
هنا، لأن الربط تغيير سلوكي وهذه الموجة شرطها ألا يتغير السلوك بتاً.

### 6.2 معجم الصلاحيات المغلق (`PermissionCatalog`) — موثقاً كاملاً

ثماني صلاحيات، لا تاسعة، مصدرها موضع واحد:

| الصلاحية | يمنحها |
|---|---|
| `listing.browse` | customer · rider · driver · shipper · tenant_admin |
| `listing.create` | rider · vendor · host · tenant_admin |
| `listing.edit` | vendor · host · tenant_admin |
| `listing.delete` | vendor · host · tenant_admin |
| `offer.submit` | customer · driver · shipper · tenant_admin |
| `chat.start` | customer · rider · tenant_admin |
| `chat.respond` | vendor · driver · host · shipper · tenant_admin |
| `tenant.manage` | tenant_admin |

مواضع الفحص (لا تتغير في هذه الموجة): `RequirePermission` في
`GateExtensions`، و`GatedPage.razor`، و`PermissionFilter`،
و`GatePipeline`، و`HasPermissionAsync` في `MarketplaceTemplateExtensions`،
وثلاث صفحات تفحص مباشرة (`Me`, `TenantHome`, `TenantManage`).

**وما ليس منه، بقصد**:

- `offer.accept` — يظهر **مثالاً في تعليق XML** على `RequirePermission`
  فقط. لا يمنحه قالب ولا يفحصه مسار.
- `tenant.roles_save`, `tenant.branding_save`, `tenant.suspend`,
  `user.grant_admin`, `deal.advance`, `deal.cancel`, `deal.dispute`,
  `listing.{action}` — **أسماء إجراءات في سجل التدقيق** لا صلاحيات
  أدوار. تشبهها شكلاً وتختلف عنها موضعاً ومعنى، وخلطهما كان الخطر
  الفعلي الذي أغلقه هذا المعجم.

**حدّ معلن**: المعجم يغلق الحلقة من جهة **التعريف** (تعريف يمنح صلاحية
خارجه يُرفض عند التحميل)، **لا من جهة الفحص** — `RequirePermission("…")`
ما زال يقبل أي سلسلة كما كان، وإغلاق ذلك الطرف تغيير سلوكي مؤجل.

### 6.3 معجم مكونات التركيب (`RoleComponents`) — وحدّ ما يقرأه التصيير

قسم `composition` يلتقط **تركيب كل دور كما هو اليوم** كبيانات موصوفة،
بمعجم مغلق حُصر بمسح كل مواضع `CatalogSlug` في الشجرة:

| الفتحة | القيم | مصدرها في الكود |
|---|---|---|
| `home` | `defaultHome` · `riderHome` · `driverHome` · `sellerHome` | فروع `TenantHome.razor` |
| `createListing` | `defaultCreateForm` · `riderCreateRequest` | فرعا `CreateListing.razor` |
| `nav` | `defaultNav` · `riderNav` · `driverNav` · `vendorNav` · `adminNav` | `switch` في `MainLayout.BuildNav` |
| `explore` | `defaultExplore` · `driverExplore` | `driverMode` في `TenantExplore.razor` |
| `publicProfile` | `vendorProfile` أو `null` | `Components/Pages/VendorProfile.razor` |
| `extras` | `driverArea` · `driversList` · `roleHomeHero` | صفحات `/me/area` و`/drivers` ومكون `RoleHomeHero` |

**التركيب الحالي**: customer ‏(defaultHome/defaultCreateForm/defaultNav/defaultExplore)،
rider ‏(riderHome/riderCreateRequest/riderNav + `driversList`)، vendor و host
‏(sellerHome/defaultCreateForm/vendorNav + `vendorProfile`)، driver و shipper
‏(driverHome/defaultCreateForm/driverNav/driverExplore + `driverArea`)،
tenant_admin ‏(defaultHome/defaultCreateForm/adminNav)، broker
‏(sellerHome/vendorNav + `vendorProfile`)، mover
‏(driverHome/driverNav/driverExplore + `driverArea`)، organizer
‏(sellerHome/vendorNav + `vendorProfile` + `driversList`).

**والفتحات مستقلة لا عائلات**: `organizer` يجمع وجه البائع
(`sellerHome`/`vendorNav`/`vendorProfile`) مع سطح كان الراكب وحده يبلغه
(`driversList`) — وهو ليس خلطاً، بل يتبع صلاحياته: صفحة `/drivers` غرضها
المعلَن «تواصَل مع سائق مباشرةً»، فهي تخدم من يملك `chat.start` حصراً.
‏`rider` يملكها ويبلغها؛ و`vendor`/`host` لا يملكانها ولا يبلغانها؛
و`organizer` يملكها فبلغها. **التركيب مشتقّ من الصلاحيات، لا مخترع.**

**الحدّ رُفع (2026-08-10 — الموجة الثانية)**: كان مكتوباً هنا أن **لا
شيء يقرأ قسم `composition` بعد**، وأن التصيير ما زال يتفرّع بـ `switch`
على `CatalogSlug` في المواضع الستة أعلاه. **صار التصيير يقرؤه.**

### آلية القراءة — نقطة قلب واحدة وقاموس مغلق في كل موضع

القرار خرج من المواضع الستة إلى دالة نقية واحدة:

```csharp
public static RoleComposition Resolve(string? catalogSlug) =>
    string.IsNullOrEmpty(catalogSlug)
        ? Fallback
        : RoleCatalog.FindDefinition(catalogSlug)?.Composition ?? Fallback;
```

ثم **كل موضع يحوّل قيمة فتحته عبر قاموس مغلق** — قيمة معجمية ← مندوب
تصيير، **لا انعكاس (reflection) على أسماء حرة** ولا تحويل اسم إلى نوع:

```csharp
var table = new Dictionary<string, RenderFragment?>(StringComparer.Ordinal)
{
    [RoleComponents.DefaultHome] = null,      // الفرع المضمَّن
    [RoleComponents.RiderHome]   = rider,
    [RoleComponents.DriverHome]  = driver,
    [RoleComponents.SellerHome]  = seller,
};
return RoleComponentMap.Map(table, Resolve(activeRole?.CatalogSlug).Home,
                            null, "الرَئيسِيَّة");
```

**والسقوط الآمن مقصود رغم أن المصادق يمنع المجهول**، لأن الحارسين
يغطيان خطرين مختلفين: المصادق يحرس **المعجم** (قيمة خارج
`RoleComponents.All` تُفشل الإقلاع)، و`RoleComponentMap.Map` يحرس
**القاموس** — أن تُضاف قيمة إلى المعجم ويُنسى تسجيلها في موضع تصيير.
القيمة غير المسجَّلة تسقط إلى الافتراضي وتطبع سطر تحذير، ولا تُسقط صفحة.

**الفتحتان اللتان تبنيان مختصرات الـ PWA**: `BuildShortcuts` لا يحتاج
فتحة سابعة — المختصرات **مرآة الـ nav** (كل مختصر أساسي يقابل تبويباً في
نفس عائلة التنقل بنفس المسار والتسمية)، والزائد عنها هو `extras` بعينه.
فـ `nav` + `extras` تكفيان، وهما ما يُقرأ.

**ما لم ينقلب، وسببه**:

- **`publicProfile` لا يقرؤه شيء — لأن لا فرع يقرؤه أصلاً.** فحص
  `VendorProfile.razor` كاملاً أظهر أنها صفحة مفتوحة بـ `vendorId` لا
  بوابة دور فيها، والرابط إليها في `TenantListingDetail` مشروط بوجود
  تقييمات للمعلن لا بدور الزائر. فالفتحة **توثيق لمن له صفحة عامة، لا
  حراسة لمن يبلغها** — وإضافة بوابة تغيير سلوك لا نقل.
- **`usesCart` في `BuildNav`** ما زال يفحص أسماء أدوار مباشرة
  (`rider`/`driver`/`shipper`/`host`). وهو على **محور النمط** لا محور
  التركيب — قريب من `dealPatternAffinity` أدناه ومن `AppPattern`، ونقله
  يحتاج قراراً في أي المعجمين يسكن، لا مجرد فتحة سابعة.
- **`BuildNav` يفتاح على `Role.Slug` بينما `BuildShortcuts` على
  `Role.CatalogSlug`** — عدم تماثل قائم قبل الموجة، نُقل كما هو. لا أثر
  له اليوم لأن `InstantiateRole` يجعل الحقلين متساويين.

**مكوّن يتيم موثق**: `Components/RoleHomeHero.razor` **موجود في الشجرة
ولا يصيّره أحد** — بحث نصي في كل `.razor` و`.cs` لا يجد له مرجعاً
واحداً. أُبقي في المعجم توثيقاً للواقع ولا يُسند إلى أي دور، واختبار
يثبّت الأمرين معاً. **ولم يوصل في الموجة الثانية بقرار**: هو مسجَّل في
قاموس `extras` بلا مختصر (`() => null`)، فالقاموس مكتمل التغطية
والمكوّن يبقى يتيماً حتى يُحسم مصيره.

### 6.3.1 انجذاب نمط الصفقة (`dealPatternAffinity`)

`PatternFromTenant` كان يشتق نمط تدفّق الصفقة من أدوار المستأجر بشروط
متناثرة تذكر أسماء أدوار بأعيانها. **المسح قبل تقرير الشكل** أظهر أن
الاشتقاق قائم على **أدوار مفردة** (عضوية `rider`/`driver`/`host` في
مجموعة الأدوار) لا على تركيبات مجموعات — فموضعه الطبيعي **حقل في ملف
الدور** لا جدول قواعد:

| الدور | `dealPatternAffinity` |
|---|---|
| `rider` · `driver` · `mover` | `trip` |
| `host` | `rental` |
| `customer` · `vendor` · `shipper` · `tenant_admin` · `broker` · `organizer` | `null` |

(‏`mover` أُضيف **بسطر في ملفه** لا بشرط في كود — وهو البرهان العملي على
أن هذا الحقل نقل القرار من الكود إلى البيانات فعلاً. §6.6.)

و`RoleDealPatternAffinity.Resolve` تجمعها بـ **ترتيب غلبة معلن**
(`trip` قبل `rental`، فسائق + مالك سكن في متجر واحد = `trip`)، وقيمة
راحة `marketplace` حين لا يجرّ شيء. `marketplace` **خارج معجم الانجذاب
المسند** بقصد: هي ما يُعطى حين لا انجذاب، فإسنادها إلى دور لا يعني شيئاً.

**عدم تماثل موثق لا مصحَّح**: `shipper` بلا انجذاب رغم أنه دور سائق في
كل فتحة تركيب (`driverHome`/`driverNav`/`driverExplore`) — لأن
`PatternFromTenant` لم يكن يعدّه `trip`. تصحيحه تغيير سلوك، والموجة
شرطها ألا يتغير السلوك بتاً؛ فنُقل كما هو ومعه اختبار يثبّته.

**وهو نمط التدفّق لا شخصية الواجهة** (تنبيه §5): `AppPattern` في
`PatternProfileResolver` معجم آخر بقواعد أخرى — يقرأ الفئات أيضاً،
ويعدّ `shipper` من `Trip`، ويبدأ بـ `roommate`. **ولم يُمس**: توحيدهما
تغيير سلوكي. ولهذا سُمّي الحقل `dealPatternAffinity` لا `patternAffinity`
— الاسم العام كان سيوحي بأنه مصدر الاثنين وهو مصدر أحدهما.

**برهان التطابق**: `RoleCompositionCharacterizationTests` يثبّت الفتحات
الست لكل دور من السبعة، والحالات الحدّية التي كان يغطيها فرع `default`
(null، فارغ، مجهول، اختلاف حالة الحرف)، و`PatternFromTenant` على إحدى
وعشرين تركيبة أدوار. كُتب واخضرّ **على الـ `switch`** في كوميت مستقل قبل
التبديل، **ولم يُمس سطر منه بعده**. ومعه **عشرون صفحة مرجعية** ملتقطة
بـ curl من خادم حي قبل وبعد، **متطابقة بايتاً ببايت** بعد تطبيع تعليق
حالة Blazor المعمّاة وحده (حمولة تتغير كل طلب بنفس الكود).

### 6.4 التقييمات وعلاقتها بالأدوار — ما وُجد لا ما يُشتهى

مسح عدة `ACommerce.Kit.Reviews` كاملة أظهر أن `Review` **محايد الدور
تماماً**: يستهدف `TargetUserId` (مستخدماً لا دوراً)، ولا حقل دور فيه،
ولا فرع دور في `ReviewsService` ولا في `ReviewSummary`. ما فيه سياقاً هو
`DealPattern` — نمط الصفقة لا الدور.

لذلك **لا تهيئة تقييمات في `RoleDefinition`**: اختراع مفتاح لا يستهلكه
شيء أسوأ من غيابه. وعلاقة الدور بالتقييم قائمة في موضع واحد فقط وهي
**موصوفة فعلاً** عبر `composition.publicProfile`: صفحة `vendorProfile`
هي الوحيدة في المستودع التي تعرض النجوم وعدّاد التقييمات، وهي صفحة
الدورين `vendor` و`host` دون سواهما — واختبار يثبّت ذلك.

**وعدم تماثل موثق كامتداد مستقبلي معلَّم**: السائق هدف تقييم صريح في
تعليق `Review` نفسه («الراكب يقيّم السائق والسائق يقيّم الراكب»)، ومع
ذلك `driversList` لا يعرض له نجمة واحدة. توثيق لا تهيئة.

**وتوسّعت الدعوى لا تبدّلت (§6.6)**: `broker` و`organizer` انضمّا إلى
`vendorProfile`، والقاعدة نفسها هي التي أدخلتهما — **من يُختار بسمعته
قبل التعاقد له صفحة عامة**. ومن لا يعرض عمله للعامة يبقى بـ `null`:
`customer` · `rider` · `driver` · `shipper` · `mover` · `tenant_admin`.

### 6.5 المصادق (`RoleDefinitionValidator`)

دوال نقية بنمط `DealPatternValidator`، **مفروضة بوابةً عند التحميل**:
تعريف فاسد يُفشل الإقلاع برسالة تسمي الدور والرمز، ولا يمر صامتاً.
رموز الخرق الثابتة:

`slug_empty` · `slug_pattern` · `icon_missing` · `home_route_malformed` ·
`localized_arabic_missing` · `permission_out_of_vocabulary` ·
`permission_duplicate` · `field_code_empty` · `field_code_duplicate` ·
`field_type_out_of_vocabulary` · `select_without_options` ·
`option_value_empty` · `option_value_duplicate` ·
`composition_component_out_of_vocabulary` ·
`deal_pattern_affinity_out_of_vocabulary`

**ورمز سادس عشر لتعريفات المستأجر وحدها** (‏§6.7):
`slug_shadows_platform_catalog` — يُضيفه
`RoleDefinitionValidator.ValidateTenantDefinition`، وهو `Validate` نفسها
زائدَ فحصٍ واحد: ألّا يصادم الـ slug اسماً في كاتالوج المنصة. **دالة
منفصلة لا علم في `Validate`**، لأن تعريفات الكاتالوج نفسها تمر من
`Validate` عند الإقلاع — ولو كان الفحص فيها لرفض كل دور كاتالوج نفسه.

**وما لا يفحصه عمداً**: توافق التركيب مع الصلاحيات — `vendor` لا يملك
`listing.browse` وتركيبه مع ذلك `defaultExplore`. هذا واقع الكاتالوج
اليوم، وجعله خرقاً يرفض قالباً قياسياً قائماً — وهو بالضبط ما رفضه
`DealPatternValidator` حين لم يشترط انتهاء النمط بـ `Reviewed`.

**حدّ التخزين، معلناً**: التعريفات ملفات **مضمونة في العدة** لا وثائق
Marten لكل مستأجر — نفس حدّ الخطوة 4. الملفات ظاهرة في المستودع (تُقرأ
وتُحرَّر ويظهر فرقها في الـ diff) ومضمونة عند النشر، لأن القارئ يعمل
تحت مضيفَين مختلفَي مسار (تطبيق ASP.NET بـ ContentRoot، ومشغّل اختبارات
بمجلد عمل آخر). **ولا سقوط من قرص إلى مضمون**: مصدران للحقيقة يعنيان
انحرافاً صامتاً بالتعريف، وهو ما جاءت الموجة لتزيله. حين يُنفَّذ
التخزين يتغير `RoleDefinitionLoader` وحده.

### 6.6 كيف تؤلّف دوراً جديداً — خمس خطوات، بلا سطر C#

الادعاء مُختبَر لا مُفترض: `broker` و`mover` و`organizer` أُلِّفت بهذه
الخطوات بالضبط، ولم يُلمس ملف تصيير واحد ولا `MainLayout` ولا
`AgentService`.

1. **اكتب `Definitions/{slug}.role.json`.** الشكل في §6.1، وكل قيمة من
   المعاجم المغلقة: الصلاحيات من الثماني (§6.2)، وفتحات `composition`
   الست من `RoleComponents` (§6.3)، و`type` الحقل من `RoleFieldTypes`،
   و`dealPatternAffinity` من `{trip, rental, null}` (§6.3.1).
2. **أضف الـ slug إلى `Definitions/roles.index.json`.** الترتيب فيه هو
   ترتيب العرض في كل مكان — صفحة الإدارة، وبوابة `/{slug}`، و`SortOrder`
   عند التسكين. **ألحِقه في الذيل** ما لم يكن لك سبب في غيره: الإلحاق
   يبقي كل ما بُني على ترتيب السابقين كما هو.
3. **ابنِ.** الملفات موارد مضمونة — انظر «الحدّ الصادق» أدناه.
4. **فعِّله على مستأجر** من `/admin/tenants/{slug}/roles` (القائمة تُبنى
   من `RoleCatalog.All`، فالدور الجديد يظهر فيها بلا تعديل)، أو بأداة
   الوكيل `set_roles` (تعدادها مشتق من الكاتالوج نفسه).
   **وإن كان الدور لمتجر واحد لا للمنصة**، فلا تكتب ملفاً أصلاً — استخدم
   أداة `define_role` (§6.7): وثيقة لذلك المتجر وحده، حيّة بلا بناء.
5. **وسِّع التوصيف إضافةً لا تعديلاً**: أَلحِق الـ slug في ذيل
   `ProbedRoles` و`ProbedSlugs` وقائمة `[find]`، وحدِّث اللقطة الذهبية.
   القاعدة: **سطر العضوية وحده يتغير، وكل ما عداه إلحاق** — وتُقاس
   بـ `diff` لا بالعين.

**ما يأتي مجاناً بمجرد وجود الملف** — كل هذا مقيس على خادم حيّ:

| السطح | من أين يقرأ |
|---|---|
| بطاقة الدور في صفحة الإدارة (اسم/وصف/صلاحيات/حقول) | `RoleCatalog.All` |
| بطاقة الدور في بوابة `/{slug}` | `Tenant.Roles` المنسوخة من الكاتالوج |
| الرئيسية والتنقل ووضع الاستكشاف ونموذج الإنشاء | `composition` عبر `RoleCompositionResolver` |
| مختصرات PWA في `manifest.json` (بما فيها `extras`) | `composition.Nav` + `composition.Extras` |
| المسار بعد الدخول | `homeRoute` |
| شاشة الـ onboarding وحقولها وخياراتها | `fields` (والإلزامي منها يفرض الشاشة) |
| نمط تدفّق الصفقة للمستأجر | `dealPatternAffinity` |
| تعداد `set_roles` ووصفه وقاعدة الأدوار في رسالة نظام الوكيل | `RoleCatalog.All` |

**الحدّ الذي كان، ورُفع في §6.7.** كانت ملفات التعريف **موارد مضمونة في
التجميع** حصراً (`EmbeddedResource` — والسبب في `RoleDefinitionLoader`:
قارئ واحد تحت مضيفَين مختلفَي مسار، ومصدر واحد للحقيقة بلا سقوط بين
قرص ومضمون)، فإضافة دور كانت تعني: ملف + سطر فهرس + **إعادة بناء
ونشر**. هذا المسار **باقٍ كما هو** وهو مسار أدوار المنصة. وما أُضيف
فوقه — لا بدلاً منه — هو طبقة وثائق لكل مستأجر تُلتقط وقت التشغيل.

### 6.7 طبقة وثائق المستأجر — فوق الكاتالوج لا مكانه (2026-08-10)

**المصدران، ولماذا لا يتنازعان:**

| | كاتالوج المنصة | تعريفات المستأجر |
|---|---|---|
| أين يعيش | `Definitions/*.role.json` مضمونة في العدة | وثيقة `TenantRoleDefinition` في Marten |
| نطاقه | كل المتاجر | متجر واحد (إيجار **مقترن**) |
| متى يتغير | بإعادة بناء ونشر | **وقت التشغيل، بلا بناء ولا إعادة تشغيل** |
| من يكتبه | مطوّر، في المستودع | الوكيل بأداة `define_role` |
| من يُحييه | البناء | قرار بشري: مشرف المنصة |

**قاعدة عدم الظل**: slug مستأجر يصادم slug كاتالوج **يُرفض بالمصادقة**
(`slug_shadows_platform_catalog`) — فلا حاجة إلى قاعدة أولوية، ولا يمكن
لمتجر أن يغيّر معنى `vendor` على المنصة. الإضافة **فوق** فقط، وفي ذيل
القائمة، فترتيب العشرة وكل ما بُني عليه لا يتحرك.

**الدورة — ثلاث حالات ولا رابعة:**

```
define_role (الوكيل)  ──►  ParseDefinition  ──►  ValidateTenantDefinition
                                                        │
                                    خرق ──► رمز يعود للوكيل، ولا وثيقة تُكتب
                                                        │
                                                     يجتاز
                                                        ▼
                                              وثيقة Status = pending
                                                        │
                                        قرار بشري (مشرف المنصة)
                                          ┌─────────────┴─────────────┐
                                          ▼                           ▼
                                      approved                    rejected
                                (يُعاد التحقق من النص           (لا يُقرأ أبداً)
                                 المخزَّن قبل الإحياء)
```

**والمقروء واحد فقط: `approved`.** المعلّق والمرفوض لا يبلغان أي سطح
لاعب — لا بوابة ولا تسجيل ولا تصيير. ولهذا «قبل الاعتماد: البوابة بلا
الدور» صحيحة **بالبناء لا بالتوقيت**.

**طبقة القرار** — `TenantRoleSet` (في عدة الأدوار، نقية بلا Marten):
لقطة ساكنة تُبنى من الكاتالوج + تعريفات المستأجر المعتمدة، وواجهتها
**مرآة** لواجهة `RoleCatalog` (`Definitions`/`All`/`Find`/
`FindDefinition`) ومعها `ResolveComposition` و`DealPattern`
و`Materialize`. **التكافؤ الصفري هو عقدها**: مستأجر بلا وثيقة واحدة
يُعطى `RoleCatalog.Definitions` بنفس **المرجع** لا بنسخة، ويُعطى نفس
كائن السقوط نفسه في التركيب — مبرهن في
`tests/ACommerce.Platform.Tests/TenantRoleZeroEquivalenceTests.cs` الذي
كُتب واخضرّ **قبل** أي تبديل ولم يُمس بعده.

**القراءة والعزل** — `TenantRoleService` (في قالب المتجر):
- كل قراءة تُفتح بـ `QuerySession(tenantSlug)` والوثيقة مُتعددة الإيجار
  بالسياسة العامة ⇒ العزل في `tenant_id` لا في شرط مكتوب باليد.
- **الكاش بمفتاح المستأجر حصراً** (`ConcurrentDictionary<slug, set>`)،
  ويُبطَل عند الاقتراح والاعتماد والرفض. **لا لقطة ساكنة عابرة
  للمستأجرين.** وهذا بالضبط ما يجعل «فوراً» ممكناً: الاعتماد يبطل مفتاح
  المتجر، فالطلب التالي يقرأ من جديد.
- سقوط آمن عند تعذّر القراءة إلى `TenantRoleSet.Platform` (= سلوك
  اليوم حرفاً)، و**لا يُخزَّن الفشل** كي لا يتجمّد خلل عابر.

**مواضع الالتقاط الخمسة** (وكلها بلا فرع جديد — تسأل اللقطة بدل الساكن):

| الموضع | الملف | ما تغيّر |
|---|---|---|
| بوابة `/{slug}` + رئيسية الدور | `Pages/TenantHome.razor` | `Roles.LoadAsync` بدل `LoadAsync<Tenant>`، و`roleSet.ResolveComposition` |
| اختيار الدور | `Pages/RolePicker.razor` | نفس التحميل |
| onboarding | `Pages/RoleOnboarding.razor` | نفس التحميل |
| التنقل | `Layout/MainLayout.razor` | نفس التحميل + فتحة `nav` |
| نموذج الإنشاء / الاستكشاف | `Pages/CreateListing.razor`, `Pages/TenantExplore.razor` | نفس التحميل + الفتحتان |
| مسارات الدخول والتسكين والصلاحية | `MarketplaceTemplateExtensions.cs` | دالة واحدة `LoadTenantWithRolesAsync` |
| صفحة إدارة أدوار المستأجر | `Pages/Admin/TenantRoles.razor` | بطاقة التعريفات + زرّا الاعتماد/الرفض |
| تعداد `set_roles` | `Services/AgentService.cs` | `SetRolesSchemaFor(roleSet)` |

**التجسيد في الذاكرة لا في الوثيقة**: `Materialize` يُلحق دوراً لكل
تعريف معتمد بـ `Tenant.Roles` **المحمَّلة**، ولا يُحفظ ذلك أبداً — لأن
نسخ التعريف داخل `Tenant` يصنع مصدراً ثانياً للحقيقة، وهو ما جاءت
الموجات لتزيله. ولذلك `/admin/tenants/{slug}/roles/save` (وهو مسار
**يحفظ** المستأجر) يبقى على تحميله المباشر.

**بوابة الاعتماد هي بوابة مشرف المنصة** لا مشرف المتجر، ومُعلَنة: تعريف
يضيف دوراً خارج كاتالوج المنصة بصلاحياته وتركيبه — قرارُ مستوى منصة.
مشرف المتجر يرى التعريفات المعلّقة في صفحته (قراءةً) ولا يعتمدها.

**حدّ معلن (لم يُرفع)**: التوطين. حاويات `{ar, en}` كما هي، و`Current`
يُرجع العربية دائماً؛ خدمة `L` غير مربوطة و`en` غير مملوءة — لا في
الكاتالوج ولا في تعريفات المستأجر. وذلك بند لاحق، ومعه **الاتجاه** الذي
لا تحلّه الترجمة (تخطيط لا نصّ).

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
| `RoleCatalog` | **ملف JSON لكل دور** (`Definitions/*.role.json` فوق `RoleDefinition`) خلف نفس واجهة `RoleCatalog`، بمعجم صلاحيات مغلق ومصادق مفروض عند التحميل (§6)؛ **والتصيير يقرأ `composition` منها** عبر نقطة قلب واحدة وقاموس مغلق في كل موضع، و`PatternFromTenant` يقرأ `dealPatternAffinity` (§6.3)؛ **وتأليف دور جديد صار ملفاً فقط** — ثلاثة أدوار أُلِّفت بلا سطر C# (§6.6) | **[مخطط]** وثيقة دور لكل مستأجر (`define_role`) — يتغير `RoleDefinitionLoader` وحده، فيسقط شرط إعادة البناء أيضاً |
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

## 9. رموز التصميم (`ThemeCatalog`) — طبقة بيانات موازية للأدوار (2026-08-10)

**الدعوى المعمارية**: المظهر يُعامَل **بنفس معمارية الأدوار حرفاً**، لا
بمعمارية جديدة. أربعة أركان، والمقابلة واحدة إلى واحدة:

| الركن | الأدوار (§6) | المظهر |
|---|---|---|
| كاتالوج مضمن | `Definitions/*.role.json` | `Definitions/default.theme.json` |
| وثيقة مستأجر في Marten | `TenantRoleDefinition` | `TenantThemeDefinition` |
| مصادق برموز خرق ثابتة | `RoleDefinitionValidator` | `ThemeDefinitionValidator` |
| طبقة قرار بتكافؤ صفري | `TenantRoleSet` | `TenantThemeSet` |
| خدمة قراءة بكاش لكل سلاج | `TenantRoleService` | `TenantThemeService` |

**ولماذا المحاكاة حرفية وليست «مستوحاة»**: لأن كل قرار في الأدوار كان
جواباً على سؤال يتكرر هنا بعينه — لماذا نصّاً لا كائناً، لماذا مورداً
مضموناً لا ملفاً على القرص، لماذا لا يُخزَّن الفشل في الكاش، لماذا رمز
خرق لا استثناء. إعادة اشتقاق الأجوبة كانت ستنتج نفسها أو تنتج انحرافاً؛
والانحراف هنا أغلى، لأن **قارئ الشيفرة يقرأ نمطاً واحداً مرتين لا
نمطين**.

### 9.1 المعجم المغلق (`ThemeTokenCatalog`)

سبعة وثلاثون رمزاً، لكل واحد **مفتاح** في `theme.json` و**اسم متغير CSS**
مكتوب صراحةً (لا مشتقّاً) و**نوع قيمة** يحدد نحوها:

| المجموعة | المفاتيح | النوع |
|---|---|---|
| العلامة | `color.primary`, `primaryDark`, `primaryLight`, `primaryHover`, `secondary`, `secondaryHover` | لون |
| الأسطح | `color.bg`, `bgAlt`, `surface`, `surface2` | لون |
| الحدود | `color.border`, `borderStrong` | لون |
| النص | `color.text`, `textMuted`, `textSoft` | لون |
| الحالات | `color.success`, `danger`, `warning`, `info` | لون |
| الأنصاف | `radius.sm/md/lg/xl/pill` | طول |
| المسافات | `space.xs/sm/md/lg/xl` | طول |
| الطباعة | `fontSize.sm/base/lg/xl`, `fontWeight.normal/bold`, `lineHeight.base` | طول/وزن/عدد |
| الكثافة | `density` | عدد |

**كل رمز له مستهلك حقيقي مقيس** — لا رمز «للاكتمال». المعجم اشتُقّ بعدّ
استعمالات `var(--ac-…)` في أوراق الأنماط السبع (`--ac-primary` ‏244،
`--ac-text` ‏151، `--ac-border` ‏151، `--ac-radius-md` ‏72…)، **وأُسقط**
`--ac-surface-alt` و`--ac-error` لأن عددهما **صفر**: رمزٌ يغيّره المستأجر
فلا يتغيّر شيء أسوأ من غيابه.

**وما ليس رمزاً عمداً**:
- **عائلة الخط** — ثابتة (Cairo). تغطية المحارف العربية شرط لا تفضيل،
  وخطٌّ بلا تغطية يرسم مربعات فارغة على الجهاز. تُفتح حين يُفحص كل خط
  مرشَّح فعلاً، لا قبل.
- **الظلال والانتقالات و`--ac-tint-*`** — قيمها مركّبة (`color-mix`،
  ظلال متعددة الطبقات)، ونحوٌ يقبلها يقبل معها CSS عشوائياً. تبقى مشتقة
  من `--ac-primary` كما هي.
- **الاتجاه** — تخطيط لا رمز، ويُحسم مع تعدد اللغات.

### 9.2 آلية البثّ — مُدخلات و مُخرجات، لا فضاء اسم واحد

```
default.theme.json (مورد مضمون)
        │
        ├── + وثيقة المستأجر المعتمدة (تغليب مفتاحاً بمفتاح)
        ▼
   EffectiveTheme.Css  =  :root{--wsl-…:قيمة;…}
        │
   يُبَثّ في <head> كتلةَ <style id="wsl-theme"> واحدة
        │
        ▼
   أوراق الأنماط:  --ac-bg: var(--wsl-color-bg);
        │
        ▼
   المكوِّنات:  background: var(--ac-bg);
```

**`--wsl-*` مُدخلات و`--ac-*` مُخرجات، والسابقتان منفصلتان عمداً.** لو
كتب الثيم في `--ac-*` مباشرةً لانتزع من `branding.css` ومن
`MainLayout` ملكيةَ لون المتجر. والفصل يجعل الحدّ التالي **بنيوياً لا
اتفاقياً**.

**التحويل وقع في مواضع التصريح الغالبة فقط** — لا في مواضع الاستعمال.
هذه هي الرافعة: تحويل ~37 سطر تصريح يجعل **كل** استعمالات `var(--ac-*)`
(‏1300+) مقودةً بالثيم دفعةً واحدة، لأن طبقة `--ac-*` كانت أصلاً موجودة
لهذا الغرض. الأوراق الثلاث الحاملة للتصريحات الغالبة بترتيب التحميل:
`widgets.css` ← `app.css` ← `premium.css`.

### 9.3 الحدّ المعلن مع `set_branding` — لون العلامة يبقى للمتجر

`set_branding` **لم تُمس في هذه الموجة**، وسلوكها كما هو: تكتب
`Tenant.BrandColor`، ويكتبه `MainLayout` أسلوباً **مضمَّناً** على
`.acs-page` و`.acm-mobile-nav`:

```razor
<div class="acs-page" style="--ac-primary:@brand; --ac-primary-dark:@brand; …">
```

والأسلوب المضمَّن يغلب كل `:root`. فالنتيجة، **مقيسة لا مفترضة**:

| على صفحة متجر | من يملكه |
|---|---|
| `--ac-primary` وعائلته | `Tenant.BrandColor` (‏set_branding) — كما كان |
| الأسطح والنص والحدود والأنصاف والمسافات والطباعة | **الثيم** |

وهذا ليس تنازلاً بل **اتساقاً**: `premium.css` تعلن منذ كتابتها «لا تلمس
`--ac-primary` إطلاقاً — يبقى مملوكاً لـ `branding.css` لكل متجر».
طبقة الرموز تتبع الحدّ القائم بدل أن تنقضه. توحيد المصدرين قرار قائم
بذاته، ومحلّه موجة المبدّل.

### 9.4 المصادق — ثلاث طبقات، لأن القيمة تُبثّ في `<style>`

لهذه البوابة عبء لا تحمله بوابة الأدوار: **قيمة الرمز تدخل وسم `<style>`
في صفحة يراها كل زائر**. تعريف دور فاسد يشوّه قائمة؛ وقيمة ثيم غير
مفحوصة مثل `red;}body{display:none` تكتب CSS عشوائياً للجميع. الدفاع
ثلاث طبقات مستقلة:

1. **المفتاح من المعجم** — واسم المتغير المبثوث يُؤخذ من
   `ThemeTokenCatalog` لا من الوثيقة، فلا يكتب مستأجر اسماً أصلاً.
2. **منع المحارف الخطرة** صراحةً (`value_unsafe_characters`) **قبل** أي
   نحو، فلا يعتمد الأمان على دقة تعبير نمطي.
3. **نحو مثبَّت بـ `^…$`** لكل نوع — لون أو طول أو عدد أو وزن، بأرقام
   ووحدات معروفة فقط.

**رموز الخرق**: `slug_empty`, `slug_pattern`, `localized_arabic_missing`,
`tokens_empty`, `token_key_out_of_vocabulary`, `token_value_empty`,
`value_unsafe_characters`, `color_malformed`, `length_malformed`,
`number_malformed`, `weight_out_of_range`, `slug_shadows_platform_catalog`
(بوابة المستأجر)، `default_theme_incomplete` (بوابة الافتراضي).

**ولماذا قاموس مسطّح لا أصناف متداخلة**: الأصناف كانت ستغلق المعجم عند
الترجمة — وهذا جيد — لكنها تجعل «مفتاح خارج المعجم» **استثناء قراءة** لا
**رمز خرق**، والوكيل يصحّح على الرموز لا على نصوص استثناءات JSON.

**وثلاث دوال لا علم واحد**، بنفس مبرر فصل `ValidateTenantDefinition` في
الأدوار: `Validate` (المشترك)، `ValidateDefault` (+ الاكتمال — ولو كان
في المشترك لرَفَض كل ثيم مستأجر جزئي)، `ValidateTenantDefinition`
(+ عدم الظل — ولو كان في المشترك لرفض الثيم الافتراضي نفسه).

### 9.5 التكافؤ الصفري — مقيس ضد الملف القديم لا ضد جدول مكتوب

العقد: **قيمة كل رمز يبثه الثيم الافتراضي ≡ الحرفية التي كانت مكتوبة في
CSS مكانه قبل التحويل.**

وطرَف المقارنة **ليس من تأليفي**. `ThemeZeroEquivalenceTests` يقرأ لقطة
CSS المودَعة في كوميت سابق مستقل
(`tests/characterization/appearance/baseline/css/`)، ويحسب منها
**التصريحة الغالبة** لكل متغير بترتيب تحميل الأوراق، ثم يقارن. لو كتبتُ
الجدول بيدي لأثبت الاختبارُ أن الرموز تطابق ما أعتقده عن CSS — وهو بعينه
ما يُخطئ فيه المرء. هذا نظير «تُقارَن المسارات لا اللقطات» في §6.7:
هناك استُدعي المسار القديم، وهنا يُقرأ الملف القديم.

**والكثافة تدخل بتكافؤ صفري برهاني**: `--ac-space-*` صارت
`calc(var(--wsl-space-*) * var(--wsl-density))` و`density = 1`، و
`calc(1rem · 1)` يُحسب إلى `1rem` بالضبط — مؤكَّداً من `getComputedStyle`
على الجهاز، لا استنتاجاً.

### 9.6 الحدود المعلنة (دَين مُعلن، لا صمت)

- **ألوان حرفية داخل قواعد المكوِّنات لم تُحوَّل**: ‏406 قيمة HEX عبر
  الأوراق السبع (‏223 منها في `widgets.css`)، وقرابة 50 نصف قطر حرفياً،
  وأحجام خط بالبكسل (‏13px×56، ‏14px×37…). هذه لا يقودها الثيم اليوم.
  المحوَّل هو **مواضع التصريح الغالبة** وحدها، وهي كافية لبرهان الفكرة
  ولقيادة الأسطح المرئية الرئيسية.
- **‏805 أسلوباً مضمَّناً (`style="…"`) في ملفات `.razor`** — خارج الطبقة
  بالكامل.
- **`site.css`, `studio.css`, `templates-*.css`** لا تصرّح أياً من هذه
  المتغيرات، فلم تُمس؛ ومعجم `--st-*` (‏سطح الاستوديو) طبقة مستقلة لم
  تدخل هذه الموجة.
- **الوضع الداكن** (`body.ac-dark`) انتقاء أشدّ يغلب في سياقه، ولم يُمس.
- **عامل الخدمة (PWA) يخزّن أوراق CSS مؤقتاً** — عميل مثبَّت يرى الورقة
  القديمة حتى تدور نسخة الكاش. وهذا **حجة للتصميم لا ضده**: كتلة
  `:root` مضمَّنة في HTML، فقيم الرموز تصل طازجةً دائماً؛ الذي يتقادم
  هو بنية الأوراق لا قيم الثيم.
- **لا متغايرات مكوّنات ولا مبدّل واجهة** — الموجة التالية.

### 9.7 البرهان الحيّ (2026-08-10، PID ‏30104)

`scripts/prove-theme-live.sh` — كل سطر يمر من الخادم الحي بنفس الـ PID:

| الخطوة | النتيجة |
|---|---|
| سالب: `color.primary = "crimson"` | HTTP 400 · `color_malformed` · **لا وثيقة** |
| سالب: `"red;}body{display:none"` | HTTP 400 · `value_unsafe_characters` · **لا وثيقة** |
| اقتراح `adwar_green` | 200 · `pending` — والصفحة **لم تتغير بحرف** |
| اعتماد | 200 — الكاش أُبطل داخل نفس العملية |
| `/adwar-demo` بعده | `primary: #14532D`, `radius.md: 4px` |
| `/ashare` بعده | **بايتاً ببايت = لقطة ما قبل الرموز** |
| PID | ‏30104 قبلُ وبعدُ — لا إعادة تشغيل ولا بناء |

وحالة Marten بعده: **وثيقة واحدة** (`adwar_green`, `approved`) — السالبان
لم يُخزَّنا أصلاً.

---
*آخر تحقق ضد الكود: 2026-08-10 (مُحدَّث مع طبقة رموز التصميم — §9؛
والتحقق السابق 2026-08-09 مع رفع `DealsPolicy` إلى بيانات — §4.3 و§8).
أي تعديل على الملفات المذكورة يستوجب تحديث هذه الوثيقة في نفس الـ PR.*
