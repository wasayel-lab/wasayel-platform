# التَثبيت — منصّة ACommerce V1

دَليل تَهيِئة بيئَة التَطوير المَحَلِّيَّة. نَفسه يَنطَبِق على
Ubuntu 22.04/24.04 (سيرفر أو محليّاً). للأنظِمَة الأخرى: المَنطِق نَفسه،
المَدير فقط يَختَلِف (brew/yum/إلخ).

## ١) المُتَطَلَّبات

- نَظام تَشغيل Ubuntu 22.04+ (أو ما يُكافِئه)
- صَلاحِيّات `sudo`
- اتِّصال إنترنت لِجَلب الحُزَم

## ٢) تَثبيت .NET 10 SDK

```bash
# المَصدَر الرَسميّ مِن Microsoft (مَوصول بـ Ubuntu repos القياسيّة):
sudo apt-get update \
  -o Dir::Etc::sourcelist="sources.list" \
  -o Dir::Etc::sourceparts="-" \
  -o APT::Get::List-Cleanup="0"

sudo apt-get install -y --fix-missing dotnet-sdk-10.0

# تَحَقُّق:
dotnet --version       # 10.0.x
dotnet --list-sdks     # /usr/lib/dotnet/sdk
```

> **مُلاحَظَة**: إذا فَشِل التَثبيت بـ 404، أَعِد المُحاوَلَة بَعد دَقائق
> (مُشكِلَة CDN عابِرَة في Ubuntu mirrors). تَجاوُز `Dir::Etc::sourcelist`
> الإضافيّ يَتَجَنَّب PPAs قَد تَكون مُعَطَّلَة.

## ٣) تَثبيت PostgreSQL 16

```bash
sudo apt-get install -y postgresql postgresql-contrib

# تَشغيل الخِدمَة:
sudo service postgresql start

# تَأكيد:
pg_isready
# يَجِب أن يَردّ: /var/run/postgresql:5432 - accepting connections
```

## ٤) إنشاء مُستَخدِم وقاعِدَة بَيانات التَطبيق

```bash
sudo -u postgres psql -c "CREATE USER acommerce WITH PASSWORD 'acommerce' SUPERUSER;"
sudo -u postgres psql -c "CREATE DATABASE acommerce_v1 OWNER acommerce;"

# اختِبار:
PGPASSWORD=acommerce psql -h localhost -U acommerce -d acommerce_v1 -c "SELECT 1"
```

> SUPERUSER لِأنّ Marten يَحتاج إنشاء functions + schemas تلقائيّاً في
> dev. في الإنتاج يُمكِن تَقليل الصَلاحِيّات.

## ٥) بَديل Docker (اختياريّ)

إذا لا تُريد تَثبيت Postgres مَحَلِّيّاً:

```bash
# تَأكَّد أنّ Docker مُثَبَّت + الخِدمَة شَغّالَة (sudo systemctl start docker)
cd platform-v1
docker compose up -d
# يُشَغِّل Postgres على المَنفَذ 5432 مَع نَفس الـ credentials
```

> لو الخادم لا يَدعَم `systemctl` (sandbox)، استَخدِم Postgres المَحَلِّيّ
> بَدَل Docker.

## ٦) تَهيِئَة الـ connection string

`apps/V1.App/appsettings.json` يَحوي الافتراض:
```
Host=localhost;Port=5432;Database=acommerce_v1;Username=acommerce;Password=acommerce
```

غَيِّره عَبر:
- `appsettings.Development.json` لِبيئَة التَطوير
- `appsettings.Production.json` للإنتاج
- مُتَغَيِّر بيئَة `ConnectionStrings__Postgres` لو على Docker/Kubernetes

## ٧) بِناء وتَشغيل المنصّة

```bash
cd platform-v1
dotnet build PlatformV1.slnx
dotnet run --project apps/V1.App --urls=http://localhost:5050
```

أو الـ script المُختَصَر:
```bash
./scripts/run.sh
```

عند أَوّل تَشغيل، Marten يُنشِئ الـ schema تلقائيّاً، ثُمّ
`PlatformSeed.RunAsync` يُنشئ مُستَأجِرَين تَجريبِيَّين (Ashare + Ejar) مَع
فِئات وإعلانات.

## ٨) الوصول لِلتَطبيق

| URL | الوصف |
|---|---|
| http://localhost:5050/ | صَفحَة المَنصّة (قائمَة المُستَأجِرين) |
| http://localhost:5050/ashare | مَتجَر "عَشير" (بَنَفسَجيّ) |
| http://localhost:5050/ashare/listings | كلّ إعلانات Ashare |
| http://localhost:5050/ashare/listings?category=room | فلتر بالفِئَة |
| http://localhost:5050/ejar | مَتجَر "إيجار" (بُرتُقاليّ) |
| http://localhost:5050/admin/tenants | API: قائمَة المُستَأجِرين (JSON) |

## ٩) إعادَة البَذر (لو غَيَّرت السكِريبت)

```bash
# امسَح قاعِدَة البَيانات بالكامِل:
sudo -u postgres psql -c "DROP DATABASE acommerce_v1;"
sudo -u postgres psql -c "CREATE DATABASE acommerce_v1 OWNER acommerce;"

# أَعِد التَشغيل — Marten يُعيد إنشاء الـ schema + Seed يُعيد البَذر:
dotnet run --project apps/V1.App
```

## ١٠) استِكشاف الأَخطاء

- **`Failed to connect to localhost port 5050`**: التَطبيق لم يَستَيقِظ
  بَعد. اِنتَظِر ٣٠-٦٠ ثانيَة عند أَوّل تَشغيل (Marten يُنشِئ الـ schema).
- **`Required usage of IServiceCollection.AddWolverineHttp()`**: مَكتَبَة
  Hosting لم تُسَجِّل الخِدمَة — تَأكَّد من بِناء أَحدَث نُسخَة.
- **`relation "platform.mt_doc_tenant" does not exist`**: schema لم تُنشأ.
  تَأكَّد أنّ `AutoCreate.All` مُفَعَّل في Development (الـ Hosting لِبَيئَة
  Production يَستَخدِم Migrations كَ خَيار آخَر).
