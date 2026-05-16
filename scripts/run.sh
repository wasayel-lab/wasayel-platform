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

exec dotnet run --no-launch-profile --project apps/V1.App --urls=http://localhost:5050
