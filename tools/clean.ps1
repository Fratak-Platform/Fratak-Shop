# پاک‌سازی فایل‌های ساختنی و موقت (bin، obj، .vs، node_modules، TestResults، publish) برای کم کردن حجم پوشهٔ پروژه.
# هیچ فایل کد، تنظیمات، کلید یا دادهٔ کاربر (App_Data، wwwroot/uploads) پاک نمی‌شود؛ همه با «dotnet build» و «npm ci» دوباره ساخته می‌شوند.
# استفاده (PowerShell از ریشهٔ مخزن، Visual Studio بسته باشد):
#   .\tools\clean.ps1             پاک‌سازی کامل + گزارش حجم
#   .\tools\clean.ps1 -KeepNode   node_modules (Tailwind) نگه داشته شود
#   .\tools\clean.ps1 -Report     فقط گزارش حجم، بدون پاک کردن (کدام پوشه بزرگ است؟)
param([switch]$KeepNode, [switch]$Report)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

function Get-FolderSizeMb([string]$path) {
    $sum = (Get-ChildItem -LiteralPath $path -Recurse -Force -File -ErrorAction SilentlyContinue | Measure-Object -Property Length -Sum).Sum
    if ($null -eq $sum) { $sum = 0 }
    [math]::Round(($sum / 1MB), 1)
}

$buildNames = @('bin', 'obj', 'TestResults', 'publish', 'node_modules', '.vs')

function Show-Report {
    Write-Host '── حجم پوشه‌ها (بزرگ‌ترها) ──'
    Get-ChildItem -LiteralPath $root -Directory -Force -ErrorAction SilentlyContinue |
        ForEach-Object { [pscustomobject]@{ MB = (Get-FolderSizeMb $_.FullName); Folder = $_.Name } } |
        Sort-Object MB -Descending | Select-Object -First 12 | Format-Table -AutoSize | Out-String | Write-Host
    Write-Host '── خروجی‌های ساختنی (قابل پاک شدن) ──'
    Get-ChildItem -Path $root -Recurse -Directory -Force -ErrorAction SilentlyContinue |
        Where-Object { $buildNames -contains $_.Name -and $_.FullName -notmatch '\\\.git\\' -and $_.FullName -notmatch '\\(bin|obj|node_modules)\\.+' } |
        ForEach-Object { [pscustomobject]@{ MB = (Get-FolderSizeMb $_.FullName); Folder = $_.FullName.Substring($root.Length).TrimStart('\') } } |
        Sort-Object MB -Descending | Select-Object -First 15 | Format-Table -AutoSize | Out-String | Write-Host
}

if ($Report) { Show-Report; return }

$before = Get-FolderSizeMb $root
$names = @('bin', 'obj', 'TestResults', 'publish')
if (-not $KeepNode) { $names += 'node_modules' }

Get-ChildItem -Path $root -Recurse -Directory -Force -ErrorAction SilentlyContinue |
    Where-Object { $names -contains $_.Name -and $_.FullName -notmatch '\\\.git\\' } |
    Sort-Object { $_.FullName.Length } |
    ForEach-Object { if (Test-Path -LiteralPath $_.FullName) { Remove-Item -LiteralPath $_.FullName -Recurse -Force -ErrorAction SilentlyContinue } }

if (Test-Path -LiteralPath (Join-Path $root '.vs')) { Remove-Item -LiteralPath (Join-Path $root '.vs') -Recurse -Force -ErrorAction SilentlyContinue }

$after = Get-FolderSizeMb $root
Write-Host "حجم پوشه: $before MB → $after MB"
Show-Report
