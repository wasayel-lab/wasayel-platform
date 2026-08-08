# Kit Pattern — atomic libraries + distribution meta-package

> **هذه الوثيقة لخّصت من بناء `ACommerce.Kits.Chat`** (commit `05fce51`)
> لتعميم النمط على Kits قادمة (Notifications، Catalog، Bookings، Subscriptions).

## الفكرة في سطر

ميزة عابرة (Chat، Notifications، Catalog…) ≠ مكتبة واحدة كبيرة.
ميزة عابرة = **عدّة مكتبات صغيرة متخصّصة + meta-package** يجمعها للتوزيع.

نسخة من نموذج Spree (`spree_core` + `spree_api` + `spree_backend` + `spree_frontend`
+ `spree` meta-gem)، ومن `Microsoft.AspNetCore.App` (meta-package فوق ~٥٠ lib).

## التركيب القياسيّ لـ Kit واحد

```
libs/
  ├── backend/messaging/ACommerce.{Concern}.Operations/   ← interfaces + service impl
  │     IChatMessage / IChatService / ChatService / IChatStore-less helpers
  ├── frontend/ACommerce.{Concern}.Client.Blazor/         ← frontend client
  │     IChatClient / ChatClient / ChatMessage record
  └── kits/
      ├── ACommerce.Kits.{Concern}.Backend/                ← drop-in Controllers
      │     {Concern}Controller, I{Concern}Store, Options, AddXxxKit ext.
      ├── ACommerce.Kits.{Concern}.Templates/              ← Razor class library
      │     <Ac{Concern}Page> components
      └── ACommerce.Kits.{Concern}/                        ← meta — references all 4
```

أربع مكتبات ذرّيّة + خامسة جامعة. كلّ منها csproj مستقلّ ~٥–١٥ ملفّاً.

## كيف تستهلكها التطبيقات

**كلّ شيء**:
```xml
<ProjectReference Include=".../libs/kits/ACommerce.Kits.Chat/ACommerce.Kits.Chat.csproj" />
```
ثمّ في `Program.cs`:
```csharp
builder.Services.AddChatKit<MyChatStore>(o => o.PartyKind = "Provider");
```
هذا. الـ `ChatController`، `IChatService`، `IChatClient`، الـ Razor component
كلّها متوفّرة.

**جزء فقط** (مثلاً microservice خلفيّ بلا UI):
```xml
<ProjectReference Include=".../libs/backend/messaging/ACommerce.Chat.Operations/..." />
<ProjectReference Include=".../libs/kits/ACommerce.Kits.Chat.Backend/..." />
```

**Frontend فقط** (مثلاً storefront يخاطب خدمة دردشة منفصلة):
```xml
<ProjectReference Include=".../libs/frontend/ACommerce.Chat.Client.Blazor/..." />
<ProjectReference Include=".../libs/kits/ACommerce.Kits.Chat.Templates/..." />
```

## أدوار المكتبات الأربع

| المكتبة | الطبقة | تحوي |
|---|---|---|
| `Operations` | تجريد + خدمة | Interfaces (`IChatMessage`, `IChatService`)، تطبيق `ChatService` provider-agnostic، DI extensions |
| `Backend` | Adapter (HTTP) | Controller جاهز يكشف REST routes، `IXxxStore` port للتطبيق ينفّذه، `Options`، `AddKit<TStore>()` |
| `Templates` | Adapter (UI) | Razor class library، components قابلة للتركيب بـ tag واحد |
| `Client` | تجريد frontend | `IChatClient` + impl، يحقَن في الصفحات |
| meta | تجميع | `<ProjectReference>` للأربعة، لا كود |

## شروط Kit صحّيّ

1. **role-agnostic**: الـ Controller لا يفرّق Customer/Provider/Admin؛
   `IXxxStore.CanParticipateAsync` يقرّر. تطبيقات الـ Provider/Admin
   نسخة واحدة — لا حاجة لـ `ChatBackend.Customer` و `ChatBackend.Provider`
   منفصلين.
2. **port over plugin**: الـ Kit يحدّد ما يحتاج (`IChatStore`)، التطبيق يكتب
   نسخة. لا تحاول تخمين شكل البيانات للتطبيق.
3. **Options لا Configuration**: التخصيص عبر `XxxKitOptions` مكتوبة بـ C#،
   لا JSON/YAML. السبب: type-safety + IntelliSense + ميزات اللغة (records,
   nullable).
4. **Razor templates لا تحتوي business logic**. تركيب شاشة + استدعاء client
   فقط. كلّ logic يذهب في `Client.Blazor` أو `Operations`.
5. **حزمة الإصدار واحدة**. كلّ المكتبات تترقّى معاً (monoversion). يتجنّب
   dependency hell الذي ضرب Spree.

## متى نُنشئ Kit جديداً

تعقّب الـ Rule of Three: لا تستخرج Kit حتّى يكرّر التطبيق نفس الـ Controller +
الصفحة + الـ Client **في ٣ مواقع** على الأقلّ.

| Concern | عدد التكرارات حاليّاً | الحالة |
|---|---:|---|
| Chat (`MessagesController` متكرّر في Customer/Vendor/Provider/Admin) | ٤ | ✅ استُخرج |
| Notifications | ٢-٣ | ⏳ قارب |
| Catalog/Listings | ٢ (Ejar Provider + Customer) | ⏳ ضعيف، انتظر منصّة ثالثة |
| Bookings | ١ (Ashare V2 only) | ❌ لا تستخرج بعد |

## الفصل بين النسخ المتخصّصة (Customer/Provider/Admin)

سؤال شائع: هل نحتاج `Kits.Chat.Backend.Customer` + `Kits.Chat.Backend.Provider`
+ `Kits.Chat.Backend.Admin` منفصلة؟

**الإجابة الحاليّة: لا**. الـ Controller واحد، يخدم كلّ الأدوار. الفرق بينها
هو التفويض (`CanParticipateAsync` + `Options.PartyKind`)، لا البنية.

نُنشئ نسخاً منفصلة فقط حين:
- الـ Controller يحتاج تخصّصاً منطقيّاً للدور (مثلاً Admin يستطيع أن يقرأ
  محادثات لا يشارك فيها للوساطة).
- الـ Razor component يحتاج تجربة UX مختلفة جوهريّاً (مثلاً Admin يرى ٣
  محادثات في عمود واحد، Provider يرى inbox).

عند ذاك:
```
ACommerce.Kits.Chat.Backend.Moderator/      ← Controller جديد للأدمن
ACommerce.Kits.Chat.Templates.Inbox/        ← Razor مختلف
```
لكن **بعد ٣ تطبيقات** أثبتت الحاجة، ليس قبل.

## أنماط مرفوضة (وعليك تجنّبها)

- **❌ Kit-as-monolith**: مكتبة واحدة `ACommerce.Chat.csproj` تحوي الكلّ
  (Backend + Frontend + Razor). لا يمكن لـ microservice أن يأخذ Backend
  فقط دون جلب Razor SDK + Blazor.
- **❌ Kit بدون port**: Controller يكتب SQL مباشرةً. التطبيق لا يستطيع تبديل
  الـ persistence. أضف دائماً `IXxxStore` interface.
- **❌ DTOs بدلاً من interfaces**: `Kit.DTOs.MessageDto` المسلسَل بدلاً من
  `IChatMessage` المنفَّذ من الكيان. ينقض Law 6.
- **❌ ربط الـ Kit بـ host particular**: `ChatController` يفترض Blazor Server.
  يجب أن يعمل على ASP.NET API + MAUI Blazor Hybrid + WASM بدون فرق.
- **❌ NuGet meta-packages متعدّدة الإصدارات**: `Kits.Chat 1.0` يجلب
  `Kits.Chat.Backend 1.5` (drift). monorepo + version موحَّد لكلّ الـ Kit.

## التوسّع: Plugin Marketplace

عند نضوج ٣–٤ Kits ووصولنا لمنتج تجاريّ، Plugin marketplace خيار طبيعيّ:
- Kit الأساسيّ يكشف `event` hooks (`OnMessageSent`، `OnConversationStarted`).
- إضافات الطرف الثالث تُسجَّل في Plugin loader عبر `IPlugin`.
- لكن: لا تبني هذا قبل عميل أوّل حقيقيّ. Plugins بدون مستخدمين هندسة بلا غرض.

## مرجعيّات

- Spree Commerce architecture (Spree Foundation, 2008-present).
- Saleor architecture docs (saleor.io).
- Sam Newman، *Building Microservices* (2015) — "small enough to be replaceable, large enough to be useful".
- Don Roberts، Rule of Three (Refactoring, 1999).
- Kent C. Dodds، AHA Programming (2019) — Avoid Hasty Abstractions.
