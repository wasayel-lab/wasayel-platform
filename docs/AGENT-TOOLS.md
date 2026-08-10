# عقد وكيل الاستوديو — Agent Tools Contract

> **الغرض**: توثيق ملزم لواجهة الوكيل الذكي في المنصة: معماريته، أدواته
> السبع، دورة الاعتماد البشري، والثوابت التي لا يجوز كسرها. هذه الوثيقة
> هي «لغة الأثر التصريحي» الحالية — وأي توسيع لقدرات التوليد يبدأ من هنا.

## 1. المعمارية

- **محايد المزوّد**: `IAgentBackend` بثلاثة تطبيقات — Anthropic (الافتراضي،
  نموذج `claude-sonnet-4-6` مع prompt caching)، Gemini، OpenAI. التبديل
  بسطر إعدادات واحد دون لمس منطق الأدوات.
- **وكيلان منطقيان، ملفّان مستقلّان (2026-08-10)**: المنصّة تشغّل أكثر من
  وكيل، ولكلٍّ حاجة مختلفة — **`Analysis`** (محرّك دراسة الجدوى في الحاضنة،
  `FeasibilityAnalysisService`) يحتاج نموذجاً أذكى ومخرجاً JSON طويلاً،
  و**`Studio`** (`AgentService`، نداء الأدوات السبع) يحتاج نموذجاً أخفّ
  وأسرع. لكلٍّ **مزوّده وعنوانه ومفتاحه ونموذجه** مستقلّة.
- **حلّ الإعدادات — قاعدة واحدة في موضع واحد** (`AgentProfileResolver`):

  ```
  Agents:{Name}:{Key}   ←  وإن غاب  ←  Agent:{Key} (القديم)  ←  وإن غاب  ←  متغيّر البيئة
  ```

  أوّل قيمة **غير فارغة** تفوز؛ السلسلة الفارغة أو المسافات البيضاء تُعامَل
  كغائبة (‏`appsettings.json` يشحن `"ApiKey": ""` و`"Model": ""` حشواً،
  والمقصود بهما «غير مضبوط»). المفاتيح الخمسة: `Provider` (‏`anthropic |
  gemini | openai`)، `BaseUrl`، `ApiKey`، `Model`، `ProviderLabel`.
  الأسماء اليوم `Studio` و`Analysis`، والبنية مفتوحة لأسماء قادمة بلا
  سطر كود جديد.
- **سقوط البيئة يتبع المزوّد المحلول**: `anthropic` ← `ANTHROPIC_API_KEY`؛
  `gemini` ← `GEMINI_API_KEY` ثم `GOOGLE_API_KEY`؛ `openai` ←
  `OPENAI_API_KEY` ثم `GROQ_API_KEY` ثم `CEREBRAS_API_KEY` ثم
  `OPENROUTER_API_KEY`.
- **التوافق الرجعي**: تهيئة `Agent:*` وحدها تعطي وكيلين على **نفس الخلفية
  المشتركة بعينها** (`Assert.Same`) بنفس المزوّد والمفتاح والنموذج — أي
  سلوك ما قبل إعادة الهيكلة حرفياً. مثبت بتوصيف في
  `tests/…/AgentProfileTests.cs`.
- **المزوّد المسمّى**: `IAgentBackendProvider.For(name)` / `ModelFor(name)` —
  تسجيل DI واحد. يخزّن خلفية واحدة لكل ملفّ **متمايز**؛ ومعيار التمايز هو
  ما يغيّر الاتصال فقط (مزوّد + عنوان + مفتاح + تسمية)، **لا النموذج** —
  فالنموذج يُمرَّر في كل `AgentRequest`. فوكيلان بنفس المفتاح والعنوان
  ونموذجين مختلفين يتقاسمان `HttpClient` واحداً.
- **الخلفيّات خيارات-صِرفة**: مُنشئاتها تأخذ `AgentProfile` محلولاً ولا
  ترى `IConfiguration` ولا تقرأ بيئة بنفسها. كان تناثر قراءة `Agent:ApiKey`
  مكرّراً في الخلفيات الثلاث بسقوط بيئة مختلف لكلٍّ — والآن في موضع واحد.

### مثال التهيئة الثنائية المستهدفة — GitHub Models (مجّاني)

نموذج ذكي للتحليل وأخفّ للاستوديو، عبر خلفية OpenAI المتوافقة، بمفتاح PAT
واحد مشترك (‏`models:read`). يُلصَق في `appsettings.Local.json`:

```json
{
  "Agents": {
    "Analysis": {
      "Provider": "openai",
      "BaseUrl":  "https://models.github.ai/inference",
      "ApiKey":   "github_pat_…",
      "Model":    "openai/gpt-4o",
      "ProviderLabel": "github-models"
    },
    "Studio": {
      "Provider": "openai",
      "BaseUrl":  "https://models.github.ai/inference",
      "ApiKey":   "github_pat_…",
      "Model":    "openai/gpt-4o-mini",
      "ProviderLabel": "github-models"
    }
  }
}
```

المفتاح هنا واحد فيتقاسم الوكيلان خلفية واحدة و`HttpClient` واحداً، مع
نموذجين مختلفين. ولو اختلف المفتاح أو العنوان بين الوكيلين بُنيت خلفيتان
منفصلتان تلقائياً — لا تغيير كود. وما لا يُذكر في `Agents:*` يسقط إلى
`Agent:*` القديم، فيمكن وضع المشترك (`Provider`/`BaseUrl`/`ApiKey`) في
`Agent:*` والاكتفاء بـ`Agents:{Name}:Model` لكل وكيل:

```json
{
  "Agent":  { "Provider": "openai",
              "BaseUrl":  "https://models.github.ai/inference",
              "ApiKey":   "github_pat_…" },
  "Agents": { "Analysis": { "Model": "openai/gpt-4o" },
              "Studio":   { "Model": "openai/gpt-4o-mini" } }
}
```

> **حدّ معلن — شكل المسار لم يُتحقّق حيّاً بعد.** `OpenAIBackend` يلحق
> `v1/chat/completions` بالـ`BaseUrl` (وهو ما يصحّ لـGroq وCerebras
> وOpenRouter وOllama)، فالعنوان أعلاه يُنادى فعلياً على
> `https://models.github.ai/inference/v1/chat/completions`. لا مفتاح
> GitHub Models متاح في هذه الموجة، فالمسار **يُتحقَّق منه بأول PAT حقيقي**؛
> وإن رفضه المزوّد فالإصلاح سطر واحد في `OpenAIBackend.CallAsync` أو
> `BaseUrl` يستوعب اللاحقة. طبقة الملفّات نفسها لا تتأثّر: المزوّد والمفتاح
> والنموذج تُحلّ وتُختبر بمعزل عن شكل المسار.

- **الجلسات**: وثيقة Marten (`AgentSession`) تحت مستأجر `_admin` —
  جلسة مشتركة للإدارة (توافق رجعي) وجلسة منعزلة لكل رائد أعمال
  (`scope:<id>`) — **لا تسريب محادثات بين المستأجرين**.
- **الأنواع محايدة**: `AgentRequest / AgentMessage / AgentToolDef /
  AgentBackendResponse` — الـ backend يحوّلها لشكل API كل مزوّد.

## 2. دورة حياة الأداة — الاعتماد البشري إلزامي

```
الوكيل يقترح استدعاء أداة (InputJson وفق مخطط معلن)
        │
        ▼
   Status: pending  ──► إنسان يراجع ──► applied  ──► AgentToolExecutor ينفذ
        │                                  │              على نفس code-path
        │                                  │              نماذج /admin اليدوية
        │                                  ▼
        └────────────────────────────► rejected / error
                                           │
                                           ▼
                       ContinueAfterToolAsync: النتيجة تعود للوكيل ليكمل
```

**المبدأ**: الوكيل يقترح، والإنسان يعتمد، والمحرك الحتمي ينفذ. لا يوجد
مسار ينفذ فيه الوكيل تغييراً دون المرور بحالة `pending`.

**بوابة ثانية لأداة واحدة (2026-08-10)**: `define_role` تُنشئ **دوراً**
لا إعداداً، فلها اعتماد ثانٍ بعد اعتماد نداء الأداة:

```
اعتماد النداء (applied) ──► المنفذ يكتب وثيقة Status = pending
                                        │
                       اعتماد التعريف (مشرف المنصة، صفحة أدوار المتجر)
                                        ▼
                                    approved = حيّ
```

الفرق ليس شكلياً: اعتماد النداء يقول «نفّذ ما طلبه الوكيل»، واعتماد
التعريف يقول «ليكن هذا دوراً يراه المستخدمون». وبين الاثنين تبقى
الوثيقة مكتوبة **ولا يقرؤها سطح واحد** — لا بوابة ولا تسجيل ولا تصيير.

## 3. الأدوات الثماني

| الأداة | الغرض | الحقول الإلزامية | القيود المفروضة بالمخطط |
|---|---|---|---|
| `create_tenant` | إنشاء متجر/تطبيق جديد | slug, name, color, channel, categories | `slug` بنمط مُرمّز `^[A-Za-z0-9_-]+$`؛ اللون بنمط hex مُرمّز؛ `channel ∈ {phone, nafath, email}` |
| `set_categories` | إعادة كتابة فئات مستأجر بالكامل | slug, categories | كل فئة: slug + label (+icon إيموجي، kind) |
| `set_branding` | تحديث الهوية البصرية | slug | البقية اختيارية (name, tagline, city, color, channel)؛ `channel ∈ {phone, nafath, email}` بنفس التعداد المغلق |
| `set_regions` | إعادة كتابة المدن والأحياء بالكامل | slug, cities | كل مدينة: name + districts[] |
| `set_roles` | اختيار أدوار المتجر من الكاتالوج | slug, roles | **تعداد مغلق مشتق** من `RoleCatalog.All` (عشرة اليوم) + `default_role`. **وفي سياق مستأجر يضم أدواره المؤلَّفة المعتمدة** — انظر أدناه |
| `define_role` | تأليف دور جديد لمتجر واحد، خارج كاتالوج المنصة | slug, definition | المخطط هو **شكل `RoleDefinition` نفسه** بمفاتيحه camelCase و`additionalProperties: false`؛ والمعاجم المغلقة يفرضها `RoleDefinitionValidator` برموزها |
| `set_attributes` | خصائص ديناميكية لنطاق محدد | slug, scope_id, definitions | النطاق: Guid فئة أو scope دور أو `…0F01` للبروفايل العام؛ الأنواع: `Text, LongText, Number, Boolean, SingleSelect, MultiSelect, Date` |
| `set_pwa` | اسم/أيقونة PWA مخصصة لدور | slug, role | الأيقونة `data:` URL حتى 256 ك.ب (الحد مُرمّز `maxLength` في المخطط)؛ سلسلة فارغة = حذف |

**دلالة الكتابة**: أدوات `set_*` القائمة على قوائم هي **إعادة كتابة كاملة**
(replace-all) لا دمجاً — على المولِّد إرسال القائمة النهائية دائماً.

**ترميز القيود (2026-08-09)**: نمط الـ slug (في الأدوات السبع)، ونمط اللون
hex، وحد حجم الأيقونة أصبحت مُرمّزة رسمياً في المخططات نفسها وتفرضها بوابة
`AgentToolValidator` قبل التنفيذ — الفحوص اليدوية في المنفذ دفاع ثانٍ لا أول.
نمط الـ slug في البوابة متسامح مع حالة الأحرف (`^[A-Za-z0-9_-]+$`) لأن
المنفذ يوحّدها صغيرة قبل الاستخدام.

**توسيع تعداد القناة (2026-08-09)**: أُضيفت `email` إلى تعداد `channel` في
`create_tenant` و`set_branding` مع قناة OTP بريدية كاملة في عدة Auth. تعداد
القيم صار **مصدرَه موضعٌ واحد** — `AuthChannels.All` في
`libs/kits/Auth/ACommerce.Kit.Auth.Core/Channels.cs` — بعد أن كان الشرط
`!= "phone" && != "nafath"` منسوخاً في أربعة مواضع (نموذجَي الإدارة ومسارَي
الوكيل). الخطر الذي يغلقه ذلك محدد: قيمة قناة صالحة **تُبتلع صامتة** وترتد
إلى `phone` لأن موضعاً واحداً نسي التعداد — لا خطأ ولا لوغ، فقط متجر بقناة
غير التي طُلبت. حارسه: `AuthChannelsTests` في
`tests/ACommerce.Platform.Tests/AuthEmailChannelTests.cs`، ويقابله في طبقة
المخطط اختبار موجب لكل أداة (`CreateTenant_ChannelEmail_Passes`،
`SetBranding_ChannelEmail_Passes`) واختبار سالب بقيمة خارج التعداد.

### 3.1 `define_role` — المخطط ودورة الاعتماد (2026-08-10)

```json
{
  "slug": "adwar-demo",
  "definition": {
    "slug": "khayyat",
    "icon": "🧵",
    "homeRoute": "/me/listings",
    "label":       { "ar": "خَيّاط", "en": null },
    "description": { "ar": "حِرَفيّ يَخيط ويُفَصِّل…", "en": null },
    "permissions": ["listing.create", "listing.edit", "listing.delete", "chat.respond"],
    "fields": [
      { "code": "workshop_name",
        "label": { "ar": "اسم الوَرشَة", "en": null },
        "type": "Text", "isRequired": true, "options": [] }
    ],
    "composition": {
      "home": "sellerHome", "createListing": "defaultCreateForm",
      "nav": "vendorNav", "explore": "defaultExplore",
      "publicProfile": "vendorProfile", "extras": []
    },
    "dealPatternAffinity": null
  }
}
```

**المسار عند التنفيذ — ثلاث خطوات، كلها مقاعد قائمة لا جديدة:**
`RoleDefinitionLoader.ParseDefinition` (نفس القارئ الذي يقرأ ملفات
الكاتالوج المضمونة، بنفس خيارات القراءة) ← `ValidateTenantDefinition`
(نفس المصادق، زائدَ قاعدة عدم الظل) ← **تخزين معلَّق**. لا يصير حياً
هنا بحال؛ الإحياء قرارُ مشرف المنصة من
`/admin/tenants/{slug}/roles`، وهو يعيد التحقق من النص المخزَّن قبل أن
يفعل.

**قسمة المسؤولية بين المخطط والمصادق، مُعلنة**: المخطط يحرس **الشكل**
(الأنواع، المفاتيح الإلزامية، ومنع المفتاح المجهول مطابقاً لـ
`UnmappedMemberHandling.Disallow` في القارئ)، والمصادق يحرس
**المفردات** (الصلاحيات الثماني، أنواع الحقول السبعة، مكوّنات التركيب،
انجذاب النمط). كان يمكن ترميزها `enum` في المخطط كما في `set_roles`،
والاختيار وقع على المصادق لسببين: أنه يعطي **رمز خرق ثابتاً** يصحّح
عليه الوكيل (`permission_out_of_vocabulary` أوضح من «لا يطابق enum»)،
وأن مصدرين للمعجم ينحرفان بصمت. والمعاجم مذكورة في `description` داخل
المخطط فيراها النموذج قبل أن يكتب.

**اتساع `set_roles` في سياق مستأجر**: بلا هذا لكان الوكيل يؤلّف دوراً ثم
يعجز عن تسكينه — تعداد المخطط لا يعرفه. المنفذ يقرأ لقطة أدوار المتجر
من الحمولة قبل المصادقة، فيبني تعداد `set_roles` منها. **ومستأجر بلا
تأليف يمرّ على نفس المخططات المجمَّعة مرة واحدة** — لا بناء لكل طلب.

## 4. الثوابت

1. **الكاتالوج مغلق، وأدوار المستأجر تضاف فوقه ولا تظلّله**: لا يخترع
   الوكيل دوراً خارج التعداد بـ `set_roles`؛ وما يؤلّفه بـ `define_role`
   يُخزَّن معلَّقاً لمتجر واحد، ويُرفض إن صادم سلاجُه الكاتالوج
   (`slug_shadows_platform_catalog`). الصلاحيات والحقول تأتي معدة من
   `RoleCatalog` تلقائياً.
2. **مسار تنفيذ واحد**: `AgentToolExecutor` يستدعي نفس code-path الذي
   تستخدمه نماذج `/admin/tenants/*/save` اليدوية — لا منطق خاص بالوكيل.
3. **قابلية الاختبار**: المنفذ مفصول عن خدمة المحادثة عمداً.
4. **العزل**: جلسة كل رائد أعمال منعزلة بمعرّف نطاق. ووثائق تعريفات
   الأدوار مُتعددة الإيجار **مقترنة** — تُقرأ بجلسة سلاج المتجر، فالعزل
   في `tenant_id` لا في شرط مكتوب باليد، وأي كاش فوقها بمفتاح المستأجر
   ويُبطَل عند كل قرار.

## 5. فجوات معروفة — يجب سدها قبل أي توسيع للتوليد

| الفجوة | الأثر | العلاج |
|---|---|---|
| `/admin` بلا مصادقة (يُفترض VPN/proxy) | مقبول للتطوير فقط | نموذج صلاحيات المنصة في PRODUCTION-PLAN §1.1 |

**مُغلقة (2026-08-09)**:

| الفجوة | كيف أُغلقت |
|---|---|
| ~~لا تحقق رسمي بمخطط JSON عند المنفذ~~ — كانت القراءة يدوية بدوال `Str/TryStr` والمخطط يقيّد توليد النموذج دون أن يُفرض عند التنفيذ | بوابة `AgentToolValidator` (‏`Services/AgentToolValidation.cs`، مكتبة `JsonSchema.Net`): خريطة اسم الأداة ← مخطط مُجمّع مصدرها نفس تعريفات `BuildAbstractTools` — تُفرض **أولاً** في `AgentToolExecutor.ExecuteAsync` قبل فحص الملكية وقبل أي تنفيذ؛ الفشل يعيد رسالة عربية بالأخطاء. الفحوص اليدوية القائمة بقيت (دفاع متعدد الطبقات). حارسها: اختبارات [TESTING-PROTOCOL §T3](TESTING-PROTOCOL.md) في `tests/ACommerce.Platform.Tests` |

## 6. أصل بيانات مجاني — لا تفرّط فيه

حالات `applied / rejected` المحفوظة في جلسات Marten هي **ذخيرة مُعنونة
بشرياً تتراكم تلقائياً**: كل اعتماد = مثال صحيح، كل رفض = مثال خاطئ.
هذه الذخيرة هي مادة اختبارات الانحدار الذهبية (T4) ومادة أي تحسين لاحق
للموجهات — تُصان ولا تُحذف.

---
*آخر تحقق ضد الكود: 2026-08-09
(`Services/AgentService.cs`, `Services/AgentBackends.cs`,
`Services/AgentToolValidation.cs`, `Kit.Auth.Core/Channels.cs`). أي تغيير في
الأدوات أو مخططاتها يستوجب تحديث هذه الوثيقة في نفس الـ PR.*
