# تَشغيل المنصّة على Windows (PowerShell).
# يَفتَرِض أنّ Postgres يَعمَل على localhost:5432 مَع المُستَخدِم
# acommerce/acommerce وقاعِدَة acommerce_v1.
# راجِع INSTALL.md لو لم تُهَيَّأ بَعد.

$ErrorActionPreference = "Stop"
Set-Location -Path (Join-Path $PSScriptRoot "..")

# تَأكُّد من Postgres
$pgReady = $false
try {
    $env:PGPASSWORD = "acommerce"
    $r = & psql -h localhost -U acommerce -d acommerce_v1 -c "SELECT 1" 2>&1
    if ($LASTEXITCODE -eq 0) { $pgReady = $true }
}
catch {}

if (-not $pgReady) {
    Write-Host "⚠ Postgres غير جاهز. ثَبِّته أو شَغِّل Docker، ثُمّ نَفِّذ:" -ForegroundColor Yellow
    Write-Host "    psql -U postgres -c `"CREATE USER acommerce WITH PASSWORD 'acommerce' SUPERUSER;`""
    Write-Host "    psql -U postgres -c `"CREATE DATABASE acommerce_v1 OWNER acommerce;`""
    Write-Host ""
    Write-Host "أو راجِع platform-v1\INSTALL.md → قِسم Windows" -ForegroundColor Yellow
    exit 1
}

Write-Host "✓ Postgres جاهز." -ForegroundColor Green
Write-Host "▶ تَشغيل المنصّة على http://localhost:5050 ..."
Write-Host "  / → قائمَة المُستَأجِرين"
Write-Host "  /ashare → عَشير (بَنَفسَجيّ)"
Write-Host "  /ejar → إيجار (بُرتُقاليّ)"
Write-Host "  /admin/tenants → JSON API"
Write-Host ""

# البيئَةُ صَراحَةً (‏2026-10-11): `--no-launch-profile` يَتَخَطّى `launchSettings.json`
# الَّذي يَضبِطُ Development، فَتَسقُطُ البيئَةُ إلى Production — وفيها يَرمي حارِسُ
# سِرِّ الجَلسَةِ عِندَ الإقلاعِ بِـ`auth_secret_default` ما لَم يُضبَط
# ACOMMERCE_AUTH_SECRET (مَقيس؛ `AuthTokenSecretPolicy.AssertSafeOutsideDevelopment` في
# `Program.cs`). وهذا السكربتُ لِلتَطويرِ المَحَلِّيِّ وَحدَه.
#
# **ولِلتَشغيلِ وَحدَه لا لِلجَلسَة** (‏2026-10-11): `$env:` يَكتُبُ في عَمَلِيَّةِ PowerShell
# المُستَدعِيَةِ نَفسِها، فَكانَت Development تَبقى بَعدَ خُروجِ السكربت — و`dotnet run` تالٍ في
# الجَلسَةِ نَفسِها يُقلِعُ Development صامِتاً فَيُخفي الحارِسَ نَفسَه (مَقيس: قَبلَ السكربتِ
# غائِبَة أَو Staging، وبَعدَه Development). فَالقيمَةُ السابِقَةُ تُحفَظُ وتُعادُ في `finally` —
# وهو يَجري حَتّى عِندَ إيقافِ الخادِمِ بِـCtrl+C.
$previousEnvironment = $env:ASPNETCORE_ENVIRONMENT
$env:ASPNETCORE_ENVIRONMENT = "Development"

# والقاعِدَةُ صَراحَةً — الَّتي فَحَصَها السكربتُ أَعلاه حَرفاً، لا ما في جَلسَةِ المُستَدعي (‏2026-10-12).
# **العِلَّةُ مَقيسَة**: Development هُنا دائِماً — أَي `AutoCreate.All` و`PlatformSeed` في كُلِّ إقلاع — وكانَ
# التَطبيقُ يَتَّصِلُ بِأَيِّ `ConnectionStrings__Postgres` في البيئَة (يَغلِبُ `appsettings.json`)، بَينَما الفَحصُ
# يَسأَلُ `acommerce_v1` المَحَلِّيَّةَ وَحدَها. فَجَلسَةٌ ضَبَطَت سِلسِلَةَ Neon كانَت تُقلِعُ Development عَلَيها: تَبذُرُ
# وتُعيدُ كِتابَةَ المُخَطَّط («قاعِدَةُ الإنتاجِ لا يُشَغَّلُ عَلَيها التَطوير»). والسِلسِلَةُ نَفسُها حَرفاً في
# `apps/V1.App/appsettings.json`، ويُطبَعُ مُضيفُ المُتَجاوَزِ وَحدَه. **وتُعادُ قيمَةُ المُستَدعي في `finally`** كَأُختِها.
$previousConnection = $env:ConnectionStrings__Postgres
$localConnection = "Host=localhost;Port=5432;Database=acommerce_v1;Username=acommerce;Password=acommerce"
if ($previousConnection -and $previousConnection -ne $localConnection) {
    $inheritedHost = if ($previousConnection -match '(?i)host=([^;]*)') { $Matches[1].Trim() } else { "غَيرُ مَقروء" }
    Write-Host "⚠ ConnectionStrings__Postgres في بيئَتِك (المُضيف: $inheritedHost) يُتَجاوَزُ هُنا:" -ForegroundColor Yellow
    Write-Host "  هذا السكربتُ يُقلِعُ Development عَلى acommerce_v1 المَحَلِّيَّةِ الَّتي فَحَصَها وَحدَها." -ForegroundColor Yellow
}
$env:ConnectionStrings__Postgres = $localConnection
try {
    dotnet run --no-launch-profile --project apps/V1.App --urls=http://localhost:5050
}
finally {
    $env:ASPNETCORE_ENVIRONMENT = $previousEnvironment
    $env:ConnectionStrings__Postgres = $previousConnection
}
