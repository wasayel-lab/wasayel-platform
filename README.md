# منصّة ACommerce — V1 (Marten + Wolverine + Blazor)

نُسخَة **مُتَعَدِّدَة المُستَأجِرين** من الصفر، مَبنيّة على:

- **.NET 10** + **ASP.NET Core**
- **Marten 8** للـ event store + document store فوق Postgres
- **Wolverine 4** للوَسيط (mediator) + HTTP endpoints التلقائيّة
- **Blazor Server** للواجهَة الأماميّة

نُقطَة التَحَوُّل عن المَشروع القَديم: لا OAM، لا Stores، لا EF. كلّ
شيء events + projections + handlers قياسيّة.

---

## 🚀 تَشغيل سَريع

### Windows (PowerShell كَ Administrator):
```powershell
cd platform-v1
.\scripts\setup-windows.ps1   # مَرَّة واحِدَة (يُثَبِّت .NET + Postgres + يُنشِئ DB)
.\scripts\run.ps1              # في كلّ تَشغيل
```

### Linux / macOS:
```bash
cd platform-v1
./scripts/run.sh              # يَفترِض Postgres مُشَغَّلاً (راجع INSTALL.md)
```

كِلاهُما يَفتَح: http://localhost:5050

اِفتَح:
- http://localhost:5050/ → صَفحَة المَنصّة
- http://localhost:5050/ashare → مَتجَر عَشير (بَنَفسَجيّ)
- http://localhost:5050/ejar → مَتجَر إيجار (بُرتُقاليّ)

---

## 🏗️ بِنية المُجَلَّد

```
platform-v1/
├── PlatformV1.slnx
├── Directory.Build.props          (إعدادات MSBuild مُشتَرَكَة)
├── Directory.Packages.props       (Central Package Management)
│
├── libs/
│   ├── core/
│   │   ├── ACommerce.Platform.Shared/       ITenantContext
│   │   ├── ACommerce.Platform.MultiTenancy/ Middleware يَحُلّ tenant من URL slug
│   │   └── ACommerce.Platform.Hosting/      AddPlatformHost() — يَجمَع كلّ شيء
│   │
│   └── kits/
│       ├── Tenants/
│       │   ├── ACommerce.Kit.Tenants.Core/   نَموذج Tenant + Category
│       │   └── ACommerce.Kit.Tenants.Server/ Wolverine handlers + HTTP endpoints
│       │
│       └── Listings/
│           ├── ACommerce.Kit.Listings.Core/   Events + Aggregate + Commands
│           └── ACommerce.Kit.Listings.Server/ Handlers + queries (tenant-scoped)
│
├── apps/
│   └── V1.App/                       Blazor Server + Wolverine HTTP في binary واحِد
│       ├── Program.cs               (~٢٠ سَطر)
│       ├── Components/Pages/         Razor pages
│       ├── Seed/PlatformSeed.cs      بَذر Ashare + Ejar أوّل تَشغيل
│       └── wwwroot/css/site.css
│
└── scripts/
    └── run.sh
```

---

## 🎯 ما يَفعَله النَموذج

### تَعَدُّد المُستَأجِرين بـ Marten Conjoined Tenancy

كلّ event وكلّ document في Marten يَحمِل عَمود `tenant_id` تلقائيّاً.
`store.LightweightSession("ashare")` يَفتَح session مَحصور بـ Ashare —
كلّ INSERT يَأخُذ `tenant_id='ashare'`، كلّ SELECT يَفلتَر تلقائيّاً.

نَتيجَة: **مَنطِق التَطبيق لا يَكتُب `WHERE tenant_id = ?` يَدَوِيّاً**.
المُتَوسِّط الوَحيد بَينك وبَين tenant_id هو `ITenantContext` الذي
يَملَؤه middleware.

### Event Sourcing لِلإعلانات

```csharp
// Command
[WolverinePost("/api/listings")]
public static async Task<ListingCreated> Create(...) {
    session.Events.StartStream<Listing>(id, ev);  // ← أَوّل event في stream
    return ev;
}

// كلّ تَعديل = event جَديد:
session.Events.Append(id, new ListingEdited(...));
session.Events.Append(id, new ListingViewed(...));
session.Events.Append(id, new ListingDeleted(...));

// الـ Aggregate يُحَدَّث inline (نَفس الـ tx) عَبر:
opts.Projections.Snapshot<Listing>(SnapshotLifecycle.Inline);

// الاستِعلام تلقائيّ:
session.Query<Listing>().Where(x => !x.IsDeleted)...
```

كلّ زيارَة صَفحَة الإعلان تُلحِق `ListingViewed`. الـ aggregate
يُحَدِّث `ViewCount` تلقائيّاً. لا عَمود سِجِلّ مُنفَصِل، لا
`UPDATE Listing SET ViewCount = ViewCount + 1`.

### Wolverine HTTP بدون Controllers

```csharp
public static class TenantHandlers
{
    [WolverineGet("/admin/tenants")]
    public static Task<IReadOnlyList<Tenant>> List(IQuerySession session)
        => session.Query<Tenant>().ToListAsync()...;

    [WolverinePost("/admin/tenants/{slug}/categories")]
    public static async Task<Tenant?> AddCategoryHandler(
        string slug, AddCategory cmd, IDocumentSession session)
    { ... }
}
```

لا `[ApiController]`، لا constructor injection، لا `ControllerBase`.
الـ method الـ static = endpoint. Wolverine يَكتَشِفها بـ assembly scan.

---

## 🧪 اختِبارات سَريعَة

```bash
# قائمَة المُستَأجِرين (JSON):
curl http://localhost:5050/admin/tenants | jq .

# إضافَة فِئَة جَديدَة لـ Ashare:
curl -X POST http://localhost:5050/admin/tenants/ashare/categories \
  -H 'Content-Type: application/json' \
  -d '{"tenantSlug":"ashare","categorySlug":"shared-house","label":"بَيت مَشَع","icon":"🏘️","attributes":null}'

# الإعلانات في Ejar مُفَلتَرَة بـ villa:
curl "http://localhost:5050/api/listings?category=villa" \
  -H 'Host: localhost' \
  -H 'X-Tenant-Slug: ejar'  # (لاحقاً سيَأتي من URL، الآن من middleware)
```

---

## 📋 ما تَمَّ + ما لم يَتِمّ

### تَمَّ
- ✅ Marten + Wolverine + Postgres مُتكامِلَة
- ✅ تَعَدُّد المُستَأجِرين بـ conjoined tenancy
- ✅ tenant resolver middleware من URL slug
- ✅ كيت Tenants (نَموذج + handlers + HTTP)
- ✅ كيت Listings (event-sourced + projection inline)
- ✅ Blazor Server pages مَع تَلوين branded
- ✅ Seed لِـ Ashare + Ejar تلقائيّاً
- ✅ Wolverine HTTP endpoints (admin + api)

### مَنوِيّ
- 🔲 كيت Auth (مع OTP عَبر SMS)
- 🔲 كيت Chat (Conversations + Messages + receipts)
- 🔲 كيت Notifications (مَع FCM/Email)
- 🔲 SignalR للبَثّ الفَوريّ
- 🔲 لوحَة Concierge Admin لإنشاء/تَخصيص tenants
- 🔲 Cloudflare DNS provisioning للنِطاق المُخَصَّص
- 🔲 AI Agent onboarding

---

## 🔧 صيانَة

```bash
# مَسح كامِل + إعادَة بَذر:
sudo -u postgres psql -c "DROP DATABASE IF EXISTS acommerce_v1;"
sudo -u postgres psql -c "CREATE DATABASE acommerce_v1 OWNER acommerce;"
dotnet run --project apps/V1.App

# فَحص ما داخِل الـ DB:
PGPASSWORD=acommerce psql -h localhost -U acommerce -d acommerce_v1 -c "\\dt platform.*"
```
