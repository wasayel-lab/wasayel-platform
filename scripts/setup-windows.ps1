# تَهيِئَة بيئَة التَطوير على Windows. اِفتَح PowerShell كَ Administrator.
# يَستَخدِم winget (مُتَوَفِّر افتراضيّاً على Windows 10 1809+ و Windows 11).
# ما يَفعَل:
#   1. يُثَبِّت .NET 10 SDK
#   2. يُثَبِّت PostgreSQL 16
#   3. يُنشِئ مُستَخدِم acommerce + قاعِدَة acommerce_v1

$ErrorActionPreference = "Stop"

Write-Host "━━━ تَثبيت .NET 10 SDK ━━━" -ForegroundColor Cyan
winget install --id Microsoft.DotNet.SDK.10 -e --accept-source-agreements --accept-package-agreements

Write-Host ""
Write-Host "━━━ تَثبيت PostgreSQL 16 ━━━" -ForegroundColor Cyan
Write-Host "سيُطلَب مِنك كَلِمَة سِرّ لِمُستَخدِم postgres الافتراضيّ — احفظها." -ForegroundColor Yellow
winget install --id PostgreSQL.PostgreSQL.16 -e --accept-source-agreements --accept-package-agreements

# تَحديث PATH للـ session الحاليّ (Postgres bin)
$pgBin = "C:\Program Files\PostgreSQL\16\bin"
if (Test-Path $pgBin) {
    $env:Path += ";$pgBin"
    Write-Host "✓ أُضيفَ PostgreSQL bin للـ PATH (هذه الـ session)." -ForegroundColor Green
}

# تَأكُّد أنّ خِدمَة Postgres تَعمَل
$svc = Get-Service "postgresql*" -ErrorAction SilentlyContinue | Select-Object -First 1
if ($svc -and $svc.Status -ne "Running") {
    Start-Service $svc.Name
    Write-Host "✓ بُدِئَت خِدمَة $($svc.Name)" -ForegroundColor Green
}

Write-Host ""
Write-Host "━━━ إنشاء قاعِدَة بَيانات التَطبيق ━━━" -ForegroundColor Cyan
Write-Host "أَدخِل كَلِمَة سِرّ مُستَخدِم postgres التي حَدَّدتها في الخَطوَة السابِقَة:"

# psql يَطلُب كَلِمَة السِرّ تفاعليّاً
psql -U postgres -c "CREATE USER acommerce WITH PASSWORD 'acommerce' SUPERUSER;"
psql -U postgres -c "CREATE DATABASE acommerce_v1 OWNER acommerce;"

# تَأكيد الاتِّصال
$env:PGPASSWORD = "acommerce"
$test = psql -h localhost -U acommerce -d acommerce_v1 -c "SELECT 'connected as ' || current_user;" 2>&1
if ($LASTEXITCODE -eq 0) {
    Write-Host "✓ كلّ شَيء جاهز." -ForegroundColor Green
    Write-Host ""
    Write-Host "الآن شَغِّل:"
    Write-Host "    .\scripts\run.ps1" -ForegroundColor White
    Write-Host "أو مُباشَرَة:"
    Write-Host "    dotnet run --project apps/V1.App --urls=http://localhost:5050" -ForegroundColor White
    Write-Host ""
    Write-Host "ثُمّ افتَح:"
    Write-Host "    http://localhost:5050/ashare"
    Write-Host "    http://localhost:5050/ejar"
} else {
    Write-Host "⚠ فَشِل الاتِّصال:" -ForegroundColor Red
    Write-Host $test
}
