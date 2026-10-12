#!/bin/bash
# تَشغيل المنصّة. يَفترِض أنّ Postgres يَعمَل على localhost:5432
# مَع المُستَخدِم acommerce/acommerce وقاعِدَة acommerce_v1.
# راجِع INSTALL.md لو لم تُهَيَّأ بَعد.

set -e

cd "$(dirname "$0")/.."

# تَأكُّد من Postgres
if ! pg_isready -q 2>/dev/null; then
    echo "⚠ Postgres لا يَستَجيب. اِبدأه بـ:"
    echo "    sudo service postgresql start"
    exit 1
fi

# تَأكُّد من قاعِدَة البَيانات
if ! PGPASSWORD=acommerce psql -h localhost -U acommerce -d acommerce_v1 -c "SELECT 1" >/dev/null 2>&1; then
    echo "⚠ قاعِدَة acommerce_v1 غير مَوجودَة. أَنشِئها بـ:"
    echo "    sudo -u postgres psql -c \"CREATE USER acommerce WITH PASSWORD 'acommerce' SUPERUSER;\""
    echo "    sudo -u postgres psql -c \"CREATE DATABASE acommerce_v1 OWNER acommerce;\""
    exit 1
fi

echo "✓ Postgres جاهز."
echo "▶ تَشغيل المنصّة على http://localhost:5050 ..."
echo "  / → قائمَة المُستَأجِرين"
echo "  /ashare → عَشير (بَنَفسَجيّ)"
echo "  /ejar → إيجار (بُرتُقاليّ)"
echo "  /admin/tenants → JSON API"
echo ""

# البيئَةُ صَراحَةً (‏2026-10-11): `--no-launch-profile` يَتَخَطّى `launchSettings.json`
# الَّذي يَضبِطُ Development، فَتَسقُطُ البيئَةُ إلى Production — وفيها يَرمي حارِسُ
# سِرِّ الجَلسَةِ عِندَ الإقلاعِ بِـ`auth_secret_default` ما لَم يُضبَط
# ACOMMERCE_AUTH_SECRET (مَقيس؛ `AuthTokenSecretPolicy.AssertSafeOutsideDevelopment` في
# `Program.cs`). وهذا السكربتُ لِلتَطويرِ المَحَلِّيِّ وَحدَه.
export ASPNETCORE_ENVIRONMENT=Development

# والقاعِدَةُ صَراحَةً — الَّتي فَحَصَها السكربتُ أَعلاه حَرفاً، لا ما تُصَدِّرُه صَدَفَةُ المُستَدعي (‏2026-10-12).
# **العِلَّةُ مَقيسَة**: Development هُنا دائِماً — أَي `AutoCreate.All` و`PlatformSeed` في كُلِّ إقلاع — وكانَ
# التَطبيقُ يَتَّصِلُ بِأَيِّ `ConnectionStrings__Postgres` في البيئَة (يَغلِبُ `appsettings.json`)، بَينَما الفَحصُ
# يَسأَلُ `acommerce_v1` المَحَلِّيَّةَ وَحدَها. فَصَدَفَةٌ صَدَّرَت سِلسِلَةَ Neon — كَما يَفعَلُ قِسمُ «بيئَة التَطوير» في
# `CLAUDE.md` — كانَت تُقلِعُ Development عَلَيها: تَبذُرُ وتُعيدُ كِتابَةَ المُخَطَّط («قاعِدَةُ الإنتاجِ لا يُشَغَّلُ
# عَلَيها التَطوير»). والسِلسِلَةُ نَفسُها حَرفاً في `apps/V1.App/appsettings.json`. ويُطبَعُ مُضيفُ المُتَجاوَزِ وَحدَه.
LOCAL_CS="Host=localhost;Port=5432;Database=acommerce_v1;Username=acommerce;Password=acommerce"
if [ -n "${ConnectionStrings__Postgres:-}" ] && [ "${ConnectionStrings__Postgres}" != "$LOCAL_CS" ]; then
    INHERITED_HOST="$(printf '%s' "${ConnectionStrings__Postgres}" | sed -n 's/.*[Hh]ost=\([^;]*\).*/\1/p' | tr -d ' ')"
    echo "⚠ ConnectionStrings__Postgres في بيئَتِك (المُضيف: ${INHERITED_HOST:-غَيرُ مَقروء}) يُتَجاوَزُ هُنا:"
    echo "  هذا السكربتُ يُقلِعُ Development عَلى acommerce_v1 المَحَلِّيَّةِ الَّتي فَحَصَها وَحدَها."
fi
export ConnectionStrings__Postgres="$LOCAL_CS"

exec dotnet run --no-launch-profile --project apps/V1.App --urls=http://localhost:5050
