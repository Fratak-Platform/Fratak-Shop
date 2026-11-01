# پاک‌سازی فایل‌های ساختنی و موقت (bin، obj، .vs، node_modules، TestResults، publish) برای کم کردن حجم پوشهٔ پروژه.
# هیچ فایل کد یا تنظیماتی پاک نمی‌شود؛ همه با «dotnet build» و «npm ci» دوباره ساخته می‌شوند.
# استفاده (PowerShell از ریشهٔ مخزن، Visual Studio بسته باشد):  .\tools\clean.ps1      یا  .\tools\clean.ps1 -KeepNode
param([switch]$KeepNode)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

function Get-FolderSizeMb([string]$path) {
    $sum = (Get-ChildItem -LiteralPath $path -Recurse -Force -File -ErrorAction SilentlyContinue | Measure-Object -Property Length -Sum).Sum
    [math]::Round(($sum / 1MB), 1)
}

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
