# ساخت خروجی انتشار کم‌حجم برای سرور (مرحلهٔ هفدهم).
# فقط فایل‌های بومی سیستم‌عامل مقصد کپی می‌شود (پیش‌فرض: ویندوز/IIS/Plesk)، بدون App_Data، wwwroot/uploads و
# appsettings.Development.json؛ در پایان حجم و فایل zip آمادهٔ آپلود ساخته می‌شود.
# استفاده (PowerShell از ریشهٔ مخزن):
#   .\tools\deploy\publish.ps1                    ویندوز (IIS / Plesk) ← publish\windows و publish\fratak-shop-windows.zip
#   .\tools\deploy\publish.ps1 -Target linux      لینوکس
#   .\tools\deploy\publish.ps1 -NoZip             بدون zip
# روی سرور: اول فایل app_offline.htm را در پوشهٔ سایت بگذارید (برنامه آزاد می‌شود)، فایل‌ها را جایگزین کنید، بعد app_offline.htm
# را پاک کنید. پوشه‌های App_Data و wwwroot/uploads روی سرور را هرگز پاک نکنید (کلیدها، لاگ‌ها و عکس‌ها). راهنما: docs/DEPLOYMENT.md
param(
    [ValidateSet('windows', 'linux', 'all')][string]$Target = 'windows',
    [switch]$NoZip
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
Set-Location $root

$out = Join-Path $root "publish\$Target"
if (Test-Path $out) { Remove-Item -Recurse -Force $out }

dotnet publish src/Fratak-Shop.Web/Fratak-Shop.Web.csproj -c Release -o $out "-p:FratakPublishTarget=$Target"
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }

$sum = (Get-ChildItem -LiteralPath $out -Recurse -File | Measure-Object -Property Length -Sum).Sum
Write-Host ("خروجی: {0}  ({1:N1} MB)" -f $out, ($sum / 1MB))

if (-not $NoZip) {
    $zip = Join-Path $root "publish\fratak-shop-$Target.zip"
    if (Test-Path $zip) { Remove-Item -Force $zip }
    Compress-Archive -Path (Join-Path $out '*') -DestinationPath $zip -CompressionLevel Optimal
    Write-Host ("فایل آپلود: {0}  ({1:N1} MB)" -f $zip, ((Get-Item $zip).Length / 1MB))
}
