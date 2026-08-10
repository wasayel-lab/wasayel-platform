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

**الحدّ الصادق — «بلا كود» صحيحة، و«بلا إعادة بناء» ليست بعد.** ملفات
التعريف **موارد مضمونة في التجميع** (`EmbeddedResource` — والسبب في
`RoleDefinitionLoader`: قارئ واحد تحت مضيفَين مختلفَي مسار، ومصدر واحد
للحقيقة بلا سقوط بين قرص ومضمون). فإضافة دور اليوم تعني: ملف + سطر
فهرس + **إعادة بناء ونشر**. ما سقط هو **كتابة الكود ومراجعته
واختباره**، لا دورة النشر. وإسقاط دورة النشر هو بالضبط ما تفتحه موجة
Marten القادمة (`define_role` فوق وثيقة دور لكل مستأجر) — ويتغير عندها
`RoleDefinitionLoader` وحده، لأن `RoleDefinitionLoader.ParseDefinition`
موجود أصلاً ويقرأ نصّ JSON بنفس خيارات القراءة المضمونة.

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
*آخر تحقق ضد الكود: 2026-08-09 (مُحدَّث مع رفع `DealsPolicy` إلى بيانات —
§4.3 و§8؛ الأساس السابق الكوميت `23067e3e`). أي تعديل على الملفات
المذكورة يستوجب تحديث هذه الوثيقة في نفس الـ PR.*
