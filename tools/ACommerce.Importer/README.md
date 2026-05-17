# ACommerce Importer

أَداة CLI تَنقُل بَيانات قَواعِد بَيانات تَطبيقات سابِقَة (عَشير V3 و إيجار V1
على SQL Server) إلى قاعِدَة PostgreSQL الخاصّ بِـ platform-v1.

## الإعداد

ضَع سَلاسِل الاتِّصال في `appsettings.Local.json` بِجانِب الـ tool
(الـ file مَدرَج في `.gitignore`):

```json
{
  "Target": {
    "Postgres": "Host=localhost;Port=5432;Database=acommerce_v1;Username=acommerce;Password=acommerce"
  },
  "Source": {
    "Ashare": "Server=db52027.public.databaseasp.net;Database=db52027;User Id=db52027;Password=…;Encrypt=True;TrustServerCertificate=True;",
    "Ejar":   "Server=db48897.public.databaseasp.net;Database=db48897;User Id=db48897;Password=…;Encrypt=True;TrustServerCertificate=True;"
  },
  "Options": { "Reset": false }
}
```

`Reset: true` يَحذِف مُستَنَدات الـ tenant في الهَدَف قَبل الاستيراد.
الافتراضي `false` يَدمَج (upsert بِالـ Id) فيُمكِن إعادَة التَّشغيل بِأَمان.

## التَّشغيل

```bash
# الاثنان مَعاً (إذا كانَت السَلاسِل مَوجودَة)
dotnet run --project platform-v1/tools/ACommerce.Importer

# مُستَأجِر واحِد
dotnet run --project platform-v1/tools/ACommerce.Importer -- --tenant ashare
dotnet run --project platform-v1/tools/ACommerce.Importer -- --tenant ejar

# إعادَة ضَبط ثُمّ استيراد
dotnet run --project platform-v1/tools/ACommerce.Importer -- --tenant ejar --reset
```

## ما يَنتَقِل

| المَصدَر (Ashare V3 / Ejar V1) | الهَدَف (platform-v1)          |
|--------------------------------|---------------------------------|
| ProductCategories / DiscoveryCategories | `Tenant.Categories`     |
| Profiles / Users               | `User`                          |
| ProductListings / Listings     | `Listing` (event-sourced)       |
| Favorites                      | `Favorite`                      |
| Chats+ChatParticipants / Conversations | `Conversation`          |
| Messages                       | `Message`                       |
| Notifications                  | `Notification`                  |

## idempotency

نَستَخدِم نَفس Id (Guid) مِن المَصدَر كَ Marten Id؛ تَشغيلات لاحِقَة
تُحَدِّث الحُقول بَدَل أن تُنشِئ تَكراراً.

## أَخطاء شائِعَة

- **مَنفَذ 1433 مَحجوب**: إذا كانَت سَلاسِل الاتِّصال صَحيحَة لَكِنّ
  المُضيف غَير مُتاح، تَحَقَّق مِن firewall أو IP allowlist.
- **`Source:Ashare` / `Source:Ejar` فارِغ**: الـ tool يَتَخَطّى ذلك
  المُستَأجِر بِلا خَطَأ — اضبُط السَلسلَة لِتَفعيله.
- **DB schema مُختَلِف**: عَشير V3 يَستَخدِم `ProductListings` بِينَما
  V1 الأَقدَم يَستَخدِم `Products` فَقَط — هذا الـ importer
  مَكتوب لِـ V3.
