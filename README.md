# منصة وسايل — Wasayel Platform

منصة تجارة وخدمات **متعددة المستأجرين، عربية أولاً**: المستأجر الواحد = تطبيق
كامل بهويته وأدواره وفئاته (سوق، إيجار عقاري، مشاوير تفاوضية، خدمات، سكن
مشترك)، يُبنى **بالتهيئة لا بالكود**، ويُهيّأ جزئياً عبر وكيل استوديو ذكي
مقيد الأدوات يعمل باعتماد بشري.

**المكدس**: ‎.NET 10 + ASP.NET Core + Blazor Server، و**Marten 9**
(مخزن أحداث + وثائق فوق Postgres بعزل مستأجرين تلقائي — conjoined tenancy)،
و**Wolverine 6** (وسيط + نقاط HTTP بدوال static بلا Controllers).

> **النسب**: هذا المستودع مستخرج بتاريخه الكامل من المستودع الأم
> `acommerce-lab/acommerce-platform` (مجلد `platform-v1`)، ثم زومن مع حالة
> النشر `deploy-hf`. منهجيات الحقبة السابقة (نموذج OAM) محفوظة في
> [docs/heritage](docs/heritage/README.md).

## تشغيل سريع

Windows (PowerShell كمسؤول):

```powershell
.\scripts\setup-windows.ps1   # مرة واحدة: .NET + Postgres + قاعدة البيانات
.\scripts\run.ps1             # في كل تشغيل
```

Linux / macOS: `./scripts/run.sh` (يفترض Postgres مشغلاً — راجع INSTALL.md).

الرابط: http://localhost:5050

## البنية

```
libs/core/        Shared (ITenantContext) · MultiTenancy (حل المستأجر من الرابط) · Hosting (AddPlatformHost)
libs/kits/        24 عدة بنمط ثلاثي: Core (نموذج+أحداث) / Server (معالجات+HTTP) / Providers (مزودات قابلة للتبديل)
libs/templates/   Templates.Customer.Marketplace (القالب الحي: مكونات، بوابات، خدمات، وكيل الاستوديو) · Templates.Shared
libs/widgets/     ACommerce.Widgets
apps/V1.App       المضيف الوحيد: Blazor + Wolverine HTTP في binary واحد
```

العُدد الأربع والعشرون: Auth (نفاذ/Twilio/Unifonic/SMTP + Mocks)، Cache (Redis)،
Cart، Chat، Culture، Delivery، DynamicAttributes، Favorites، Files
(Aliyun OSS/Google Cloud)، Listings، Maps، Notifications، Offers (تفاوض
InDrive-style)، Payments، Profiles، Realtime (SignalR+Redis)، Reports،
Reviews، Roles، SavedSearches، Subscriptions، Support، Tenants، Versions.

## الوثائق

| الوثيقة | محتواها |
|---|---|
| [docs/META-MODEL.md](docs/META-MODEL.md) | **العقد المرجعي**: تشريح العدة، البوابات، خط الصفقات، معجم الأنماط، كاتالوج الأدوار — ما يجب أن يستهدفه أي توليد قوالب |
| [docs/AGENT-TOOLS.md](docs/AGENT-TOOLS.md) | عقد وكيل الاستوديو: الأدوات السبع، دورة الاعتماد البشري، الثوابت والفجوات |
| [docs/TESTING-PROTOCOL.md](docs/TESTING-PROTOCOL.md) | بروتوكول حتمية التوليد وجودة القوالب: المقاييس C/V₁/V₃ والاختبارات T1–T8 |
| [docs/MILESTONES-DELIVERED.md](docs/MILESTONES-DELIVERED.md) | سجل الموجة الأخيرة: ما هو فعلي مقابل mock |
| [docs/PRODUCTION-PLAN.md](docs/PRODUCTION-PLAN.md) | خطة الوصول للإنتاج المدفوع |
| [docs/ROLE-APPS-PLAN.md](docs/ROLE-APPS-PLAN.md) | خطة تطبيقات الأدوار |
| [docs/LLM-ALTERNATIVES.md](docs/LLM-ALTERNATIVES.md) | بدائل مزودات النماذج اللغوية |
| [docs/heritage/](docs/heritage/README.md) | خمس منهجيات منقولة من حقبة OAM |

## الحالة — بصدق

**فعلي ويعمل**: العُدد الـ24، خط الصفقات الموحد بواجهاته (stepper + سجل زمني
+ إجراءات)، تذاكر الدعم بالرد والإغلاق، إشراف الإعلانات، التقييمات المتبادلة
المربوطة بالصفقات، وكيل الاستوديو (Anthropic/Gemini/OpenAI)، PWA لكل دور.

**Mock بمنافذ جاهزة للتبديل**: OTP بقناتيه — الهاتف (MockSms) والبريد
(MockEmail) — كلاهما يطبع رمزاً ثابتاً `123456` في اللوغ ولا يرسل شيئاً،
وهو الافتراضي التطويري؛ نفاذ (MockNafath)، الدفع (MockPaymentProvider —
فواتير بهيئة ZATCA وVAT ‏15% وidempotency keys)، التوصيل (دورة شحنة كاملة
على مؤقت)، الخرائط.

**بُني حديثاً (2026-08-09)**: مشروع اختبارات `tests/ACommerce.Platform.Tests`
(xUnit — توصيف `DealsPolicy` كبذرة T5/T6 + اختبارات مصادقة المخطط كبذرة T3)
مع بوابة CI على GitHub Actions، وبوابة تحقق رسمي بمخطط JSON
(`AgentToolValidator`) تُفرض أولاً في منفذ أدوات الوكيل. العدد اليوم
**140 اختباراً** خضراء.

**قناة مصادقة بالبريد (2026-08-09)**: قيمة ثالثة لقناة المستأجر إلى جانب
`phone` و`nafath`. آلية الرمز **هي نفسها** (نفس التوليد والتجزئة ومهلة
العشر دقائق وحدود المعدل في `AuthHandlers`) — الجديد هو المُرسَل إليه
والمزود. مزودان: `Auth.Providers.MockEmail` (الافتراضي، تطويري، لا إرسال
فعلي) و`Auth.Providers.Smtp` (فعلي عبر MailKit، يعمل مع أي SMTP بما فيه
Azure Communication Services). التبديل بسطر تهيئة واحد:
`Auth:Email:Provider=smtp` مع `Auth:Email:Host/Port/Username/Password/From`
— **لا سرّ في الكود**، وبلا هذه التهيئة يبقى Mock عاملاً. الحالة بدقة:
المسار كامل من الواجهة إلى تخزين المستخدم ومختبَر منطقياً بلا قاعدة
بيانات، لكن **لم يُرسَل بريد فعلي عبر SMTP بعد** — راجع
[PRODUCTION-PLAN](docs/PRODUCTION-PLAN.md) لما يلزم لتفعيله إنتاجياً
(نطاق مُصادَق SPF/DKIM/DMARC واعتماد مزود وسمعة إرسال).

**وضوح وتحفيز وSEO (2026-08-09)**: ثلاث إضافات عرضية لا تمس خط الصفقات
ولا دلالته التشغيلية — `DealsPolicy` و`DealsService` والأحداث ومخططات أدوات
الوكيل بلا تغيير حرف واحد.

- **شرح التدفق** (`Components/FlowExplainer.razor`): خط زمني يبين مراحل
  الصفقة ومن يحرك كل مرحلة، **مشتق بالكامل** من `DealsPolicy.StagesFor`
  و`LabelAr` و`Actor` — لا نص مرحلة منسوخ، فتغيير السياسة يغير الشرح
  تلقائياً. مطوي في صفحة الإعلان، ومفتوح فوق الـ stepper في صفحة الصفقة.
- **«دورك الآن»** (`Services/Ux/DealTurnView.cs`): بطاقة تخبر المستخدم
  الحالي تحديداً هل الإجراء التالي منه، مبنية على **نفس** قاعدة الفاعل التي
  يفرضها `DealsService.AdvanceAsync` — واختبار يقارن الاثنتين عبر كل نمط
  وكل مرحلة، فلا تعد الواجهة بما يرفضه المحرك. مع حالات فارغة موجهة (كل
  قائمة فارغة صار لها خطوة تالية واحدة) ومؤشرات ثقة (مُوثَّق/مميَّز وعدد
  تقييمات المعلن) بألوان العلامة من المستأجر.
- **SEO** (`Kit.Tenants.Core/Seo.cs` + `Kit.Tenants.Server/SeoHandlers.cs`):
  ترويسة ديناميكية لكل مستأجر (عنوان + وصف + canonical + theme-color +
  OpenGraph/Twitter) تُبنى من وثيقة المستأجر التي حمّلها الـ middleware
  أصلاً — صفر استعلام إضافي؛ و JSON-LD (`Organization` + `WebSite`)
  للرئيسية؛ و`robots.txt` و`sitemap.xml` كنقطتي Wolverine HTTP تستثنيان
  `_admin` والمستأجرين المعلقين ومسارات الإدارة والاستوديو. **دوال البناء
  نقية** (بلا قاعدة بيانات) ومختبرة كذلك. الصفحات العامة كانت — وبقيت —
  SSR ثابتاً بالكامل (لا `@rendermode` إلا في صفحتي وكيل)، فالزاحف يستلم
  HTML مكتملاً بلا انتظار SignalR.

**عطل إقلاع قائم قبل هذه الموجة — يحتاج قراراً**: بعد ترقية Wolverine إلى 6
(`b8efc91a`) لم يعد الـ core يشحن مُصرِّف وقت التشغيل، فيرمي
`WolverineRuntime.StartAsync` استثناء `TypeLoadMode.Dynamic ... no
IAssemblyGenerator (Roslyn) is registered` قبل خدمة أي طلب. تحقق مستقل بنسخة
مصغرة بنفس الحزم (WolverineFx.Http 6.25.1 + Marten 9.22.5). العلاج أحدهما:
إضافة `WolverineFx.RuntimeCompilation` (‏6.25.1 مستقرة) إلى
`Directory.Packages.props`، أو توليد الكود مسبقاً
(`dotnet run -- codegen write`) مع `TypeLoadMode.Static` في CI. البناء
والاختبارات خضراء في الحالتين — العطل عند التشغيل فقط.

**غائب**: جهاز T1/T2/T4 (ذخيرة الطلبات الحقيقية والاستنساخية والجلسات
الذهبية) وT7 (القبول بالمحاكاة عبر `DealsService`)، `DealsPolicy` كبيانات
(اليوم كود مُجمّع). التفصيل والعلاج في
[TESTING-PROTOCOL](docs/TESTING-PROTOCOL.md)
و[META-MODEL §8](docs/META-MODEL.md).

---
*آخر تحقق من كل ادعاء أعلاه ضد الكود: 2026-08-09.*
