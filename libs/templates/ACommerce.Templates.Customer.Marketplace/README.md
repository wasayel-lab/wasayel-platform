# ACommerce.Templates.Customer.Marketplace

قالَب V1 الكامِل المُسَتَخدَم بِواسِطة `Apps/Ejar/Customer/...` (Ejar.Web،
Ejar.WebAssembly، Ejar.Maui). الحِكمَة: التَطبيق ورَقَة، القالَب يَحوي
كلّ شَيء.

## شَجَرَة المَلَفّات

```
Components/
  Layout/             MainLayout (الإطار العام: navbar + bottom nav)
  Pages/              ٢٢ صَفحَة @page-routed مُنَظَّمَة في sub-folders
    Auth/                Login
    Listings/            Home, Explore, ListingDetails, MyListings, CreateListing, Search
    Chat/                Chats, ChatRoom
    Notifications/       Notifications
    Profiles/            Me, ProfileEdit, Settings
    Subscriptions/       Plans, MySubscription, Subscribe, PaymentCallback
    Support/             Help, Complaints, ComplaintDetail
    Favorites/           Favorites
    App/                 Legal, EjarDashboardPage
  Routes/             Router مَع AppAssembly + AdditionalAssemblies
  App.razor           shell SSR شَكل (يَستَخدِمه Ejar.Web's host)
  RedirectToLogin     مُكَوِّن لإعادَة التَوجيه عِند 401
  CookieConsentBanner

Services/             خَدَمات realtime/push/version/auth الـscoped
  EjarAuthenticationStateProvider   ‹دَور Blazor's AuthenticationStateProvider›
  EjarRealtimeService                ‹SignalR client wrapper›
  FavoritesSync                       ‹OAM-driven mutations مَع UI optimism›
  FirebasePushService                 ‹Web Push registration›
  UnreadService                        ‹عَدَّاد مُوَحَّد لِلرسائل + الإشعارات›
  VersionPoll                          ‹dotnet → /version.json polling›

Store/                حالَة المُسَتَخدِم المَركَزيّة (V1 legacy)
  AppStore             ‹Auth + Ui + Pinned + Culture state›
  AppStorePersistence  ‹localStorage save/restore›
  AppStateApplier      ‹IStateApplier — يَستهلِك envelopes ويُحَدِّث AppStore›
  ApiReader            ‹HttpClient wrapper لِقِراءات API›
  L + ITranslationProvider  ‹translations service›
  TimezoneService      ‹ITimezoneProvider + JsTimezoneProvider›
  UserCulture          ‹POCO record لِلغة + المِنطَقَة الزَمَنيّة + العُملَة›

Interceptors/         HTTP message handlers + KitApi pipeline
  AuthHeadersHandler        ‹يُلصِق Bearer JWT›
  CultureHeadersHandler     ‹يُلصِق Accept-Language + X-User-Timezone›
  CultureInterceptor        ‹يَفُكّ MultiCultureValue في envelopes›
  EjarCircuitHttp           ‹HttpClient أساسيّ يَستَخدِمه ApiReader›

Interpreters/         OperationInterpreter<AppStore>
  AuthInterpreter      ‹يُحَدِّث AppStore.Auth بَعد auth.* operations›

Operations/           OAM client-side
  EjarOps              ‹مَجموعَة tags لِلعَمَليات: ui.* / auth.*›

Resources/            موارد مُتَرجَمة
  Strings.resx + Strings.ar.resx

wwwroot/              static web assets — تُحَمَّل مِن host عَبر
                      _content/ACommerce.Templates.Customer.Marketplace/
  app.css               ‹التَنسيق العام لِلمَنصَّة›
  ui-prefs.js
  image-compressor.js
  notifications.js
  js/firebase-push.js
  js/realtime.js

EjarCustomerUiExtensions.cs   ‹AddEjarCustomerUI — تَسجيل DI الكامِل›
```

## ما تَمّ تَنظيفه (F60)

`UnreadComposition` جَديد في `libs/compositions/Customer.Unread/` —
يَجمَع `IChatStore.UnreadTotal` + `INotificationsStore.UnreadCount`
في طَبَقة composition قَائمَة بِذاتها. يَستَهلِكها أيّ تَطبيق:
```csharp
services.AddCustomerUnreadComposition();
@inject UnreadComposition Unread
<span class="badge">@Unread.Total</span>
```
`UnreadService` (V1 الخاصّ) يَبقى مُؤَقَّتاً لأنّه يَحوي realtime hooks
لـ `EjarRealtimeService` لا يُغَطّيها composition بَعد. سَيَتَقاعَد عِندَما
يَنتَقِل realtime إلى composition layer.

## ما تَمّ تَنظيفه (F59)

ICultureContext امتَدّ بِـ Currency؛ Static + Mutable + Middleware حُدّثَت.

النَقل إلى الكيتس:
- `Interceptors/CultureHeadersHandler.cs` → `Culture.Defaults` (يَستَهلِك ICultureContext)
- `Interceptors/CultureInterceptor.cs` → `Culture.Interceptors` (يَستَهلِك ICultureContext)
- مُجَلَّد `Interceptors/` فارِغ ⇒ حُذِف

L10n مُوَحَّد:
- `Store/L.cs` + `Store/ITranslationProvider.cs` حُذِفا — استُبدِلا بِـ
  `ACommerce.L10n.Blazor.L` + `ITranslationProvider`
- `Store/EjarTranslationProvider.cs` جَديد — V1's resx-backed impl لِـ
  `L10n.Blazor.ITranslationProvider`
- `Store/UserCulture.cs` حُذِف (record renamed in-template to EjarUserCulture)
- `AppStore.cs` + `AppStoreCultureContext` adapter يَكشِف AppStore.Ui
  كَ `ICultureContext` + `ILanguageContext` لِيَستَهلِكه كلّ شَيء

## ما تَمّ تَنظيفه (F58)

`Services/VersionPoll.cs` انتَقَلَ إلى `libs/kits/Versions/ACommerce.Kits.Versions.Templates/`
ويُسَجَّل تلقائيّاً مَع `AddVersionsTemplates(httpClientName)`. أيّ تَطبيق
يَستَخدِم Versions kit يَحصُل عَلى الـ poller مَجّاناً.

### مُؤَجَّل بِسَبَب الحاجَة لِتَجريد جَديد:

`Interceptors/CultureHeadersHandler.cs` و `Interceptors/CultureInterceptor.cs`
+ `Store/L.cs` + `Store/ITranslationProvider.cs` + `Store/UserCulture.cs` —
تَعتَمِد كلّها عَلى `AppStore.Ui.Culture`. لِنَقلها لِلكيتس نَحتاج إنشاء
`ICultureState` في `ACommerce.Culture.Abstractions` أوّلاً (مُكافِئ
`IClientAuthState`)، ثُمّ Culture handlers + Interceptor تَستَهلِكه. تَنتَظِر
جَلسة مُستَقِلّة تُؤَسِّس هذا التَجريد في الكيت.

## ما تَمّ تَنظيفه (F57)

مَجموعة Auth انتَقَلَت بِكامِلها إلى `ClientHost.Auth`:
- حُذِف `Services/EjarAuthenticationStateProvider.cs` (←  `ClientAuthStateProvider`)
- حُذِف `Interceptors/AuthHeadersHandler.cs` (← `AuthenticatedHttpClient` يَحفَظ Bearer)
- حُذِف `Interceptors/EjarCircuitHttp.cs` (← `AuthenticatedHttpClient`)
- حُذِف `Interpreters/AuthInterpreter.cs` (Login.razor + Me.SignOut يَكتُبان مُباشَرَة)
- `Store/AppStorePersistence.cs` فَقَد KeyAuth (← `LocalStorageClientAuthPersistence`)
- `Store/AppStore.cs` AuthState صار façade فوق `IClientAuthState`

V1 Pages لَم تَتَغَيَّر — لا تَزال تَكتُب `Store.Auth.UserId = ...` كَما
كانَت، لكن البَيانات تَعيش الآن في الحالة المُوَحَّدة. `EjarChatClient`
+ `FirebasePushService` + `ListingDetails.razor` بُدِّلَت من
`EjarCircuitHttp` إلى `AuthenticatedHttpClient`.

## ما تَمّ تَنظيفه (F56)

حُذِفَت ٨ Bindings (EjarAuthStore … EjarFavoritesStore) — كانَت تُنَفِّذ
`IXxxStore` لِلكيتس لكن لَم تُسَجَّل في DI أَبَداً ولا تُحقَن في صَفحات،
فهي dead code. + RequiredAuthAnalyzer (٠ refs) + TelemetryInterceptor
(٠ refs) + UiInterpreter (٠ refs) + EjarRoutes (٠ refs).

`EjarChatClient` يَبقى — مُسَجَّل كَ `IChatClient` ويَستَخدِم
`EjarCircuitHttp` لِضَمان Bearer header في WASM (الـChatClient
الافتراضيّ في الكيت يَطلُب HttpClient مِن factory ويَفقِد الـtoken).
سَيَنتَقِل في Phase B بَعد إصلاح الكيت.

`Stores/` مُجَلَّد فارِغ مِن F51 — حُذِف.

## خارِطَة Phase B — التَّرحيل النِّهائيّ إلى الكيتس

كلّ هذه الخَدَمات / Store / Interceptors يَنبَغي أن تُستَبدَل بِما يُعادِلها
مِن الكيتس / ClientHost / Compositions في جَلَسات لاحِقَة. الجَدوَل التالي
يُحَدِّد المُكافِئ:

| اليوم في القالَب | يُسْتَبدَل بـ | كَيف |
|------------------|------------|------|
| `EjarAuthenticationStateProvider` | `ClientAuthStateProvider` (ClientHost.Auth) | استَبدِل التَّسجيل + احذِف الـclass. AuthInterpreter يَنقُل بَيانات إلى `IClientAuthState` بَدَل `AppStore.Auth`. |
| `Store/AppStore` (Auth جُزء) | `IClientAuthState` (ClientHost.Auth) | احذِف `Auth` partial من AppStore، حَوِّل قارئيه لِـ `IClientAuthState`. |
| `Store/AppStore` (Pinned/Ui جُزء) | App-level `IUiPreferences` كَ scoped service | استَخرِج هذه الجُزء كَ contract صَغير. |
| `Store/ApiReader` | `KitHttpClient` (ClientHost.KitApi) + kit ApiClients | الصَّفحات تَستَهلِك `IXxxApiClient` بَدَل `ApiReader.GetAsync`. |
| `Store/AppStateApplier` | يَختَفي بَعد إِزالَة AppStore | تَجمَّل dispatch في kit Stores. |
| `Store/L` + `ITranslationProvider` | `Translations` kit (مَوجود لكن غير مُدمَج) | إضافَة `AddTranslations` في AddTemplate. |
| `Interceptors/AuthHeadersHandler` | `AuthenticatedHttpClient` (ClientHost.Auth) | حَذف الـ handler. |
| `Interceptors/CultureHeadersHandler` | `Culture.Defaults` kit handler | نَقل الـ class إلى الكيت. |
| `Interceptors/CultureInterceptor` | `Culture.Interceptors` kit | نَقل. |
| `Interceptors/EjarCircuitHttp` | `AuthenticatedHttpClient` | حَذف. |
| `Services/UnreadService` | `Composition` يَجمَع `IChatStore.UnreadTotal` + `INotificationsStore.UnreadCount` | طَبَقَة composition جَديدة. |
| `Services/FavoritesSync` | `IFavoritesStore` (kit) + optimistic interceptor | الكيت يُغَطّي الأَساسيّات؛ optimism يَنتَقِل لِـ ClientOpInterceptor. |
| `Services/EjarRealtimeService` | `Realtime` composition (libs/compositions/...) | نَقل إلى compositions. |
| `Services/EjarChatClient` | `Chat.Client.Blazor` كَيت + `Chat.Realtime` composition | الكيت + composition يُغَطّيان. |
| `Services/FirebasePushService` | `Firebase` provider (libs/providers/Notifications/...) | provider يَستَهلِك kit. |
| `Services/VersionPoll` | `Versions.Templates` kit | يُضاف هُناك. |
| `Operations/EjarOps` | tags داخِل kit Operations | tags صارَت في الكيت. |
| `Interpreters/AuthInterpreter` | يَختَفي بَعد إِزالَة AppStore.Auth | `IClientAuthState` يُحَدَّث مُباشَرَةً مِن `IAuthStore`. |

## النَّمَط النِّهائيّ المُنشود

```
Apps/<App>/Customer/Frontend/<App>.Web/
  Program.cs           ~٢٠ سَطر — AddTemplate_<X> + branding
  Components/App.razor — shell SSR (CSS chain)
  wwwroot/branding.css — overrides فقط

libs/templates/<App>.<Style>/
  Components/Pages/<Kit>/   صَفحات هَذه النَّكهة
  Components/Layout/        layout هَذه النَّكهة
  ServicesCollectionExtensions.cs  AddTemplate_<X>(o => …)

libs/kits/<Kit>/
  Operations/    OAM (مُوَحَّد)
  Backend/       server controllers
  Frontend/Customer/Stores/    IXxxStore + Default + ApiClient (لا razor، لا UI)
```

كلّ شَيء يَحتاجه التَطبيق يَأتي مِن template واحِد. التَّخصيص = branding.css
+ خَيارات `AddTemplate_<X>(o)`.
