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
$env:ASPNETCORE_ENVIRONMENT = "Development"

dotnet run --no-launch-profile --project apps/V1.App --urls=http://localhost:5050
